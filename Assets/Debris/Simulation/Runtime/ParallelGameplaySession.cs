using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Debris.Materials;
using Debris.Ships;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation
{
    // Opt-in R2a bridge.  It deliberately owns only the candidate state; the
    // legacy MatterSession is retained as a presentation mirror until R3.
    public sealed class ParallelGameplaySession : IDisposable
    {
        public const int MaximumPendingTicks=16;
        public const int StarterCargoCapacity=2500;
        readonly ParallelGrainSolver solver;
        readonly Pending[] pending=new Pending[MaximumPendingTicks];
        readonly MaterialCatalog catalog;
        readonly MatterSnapshot source;
        readonly bool[] cargo;
        readonly CandidateTerrainState terrain;
        int pendingHead,pendingCount;
        uint lastSubmittedTick;
        bool faulted,disposed;
        double unresolvedFuel;
        public bool Faulted=>faulted;
        public string Fault { get; private set; }
        public int PendingTicks=>pendingCount;
        public uint NextSubmissionTick=>lastSubmittedTick+1;
        public ParallelGrainSolver Solver=>solver;
        public CandidateTerrainState Terrain=>terrain;
        public uint TerrainRevision=>terrain.Revision;
        public double ProvisionalFuel
        {
            get { double total=unresolvedFuel;for(int i=0;i<pendingCount;i++)total+=pending[(pendingHead+i)%MaximumPendingTicks].Fuel;return total; }
        }

        sealed class Pending
        {
            public uint Tick;
            public double Fuel;
            public Task<ProofCompletion> Readback;
        }
        public readonly struct Completion
        {
            public readonly uint Tick;
            public readonly SolverFault Fault;
            public readonly Vector2 ShipCenter,ShipVelocity;
            public readonly float ShipAngle,ShipSpin,CargoMass;
            public readonly int CargoCount;
            public readonly double FuelBurn;
            public Completion(uint tick,SolverFault fault,BodyState ship,int count,float mass,double fuelBurn=0)
            {Tick=tick;Fault=fault;ShipCenter=ship.Center;ShipVelocity=ship.Velocity;ShipAngle=ship.Angle;ShipSpin=ship.AngularVelocity;CargoCount=count;CargoMass=mass;FuelBurn=fuelBurn;}
        }

        ParallelGameplaySession(ParallelGrainSolver value, MatterSnapshot imported, MaterialCatalog materials, bool[] cargoFlags, CandidateTerrainState terrainState)
        {solver=value;source=imported;catalog=materials;cargo=cargoFlags;terrain=terrainState;}

        public static ParallelGameplaySession Import(MatterSnapshot snapshot,ShipRuntime ship,MaterialCatalog catalog,int velocityIterations=4,int positionIterations=2)
        {
            if(snapshot==null||ship==null||catalog==null)throw new ArgumentNullException();
            if(snapshot.Cells==null||snapshot.Cells.Length>snapshot.Capacity)throw new InvalidOperationException("Candidate import would exceed the loose-pool capacity.");
            if(snapshot.Fragments==null)throw new InvalidOperationException("Candidate import requires explicit fragment records.");
            if(snapshot.Fragments.Length>16)throw new InvalidOperationException("Candidate import exceeds the rigid-body endpoint limit.");
            var grains=new Debris.Simulation.ParallelProof.LooseCell[snapshot.Cells.Length];var grainMasses=new float[grains.Length];var flags=new bool[grains.Length];
            var pose=snapshot.ShipPose!=null&&snapshot.ShipPose.Length>=2?snapshot.ShipPose[0]:new Vector4(ship.Position.x,ship.Position.y,ship.Angle,1);
            for(int i=0;i<grains.Length;i++)
            {
                var cell=snapshot.Cells[i];
                if(cell.Material==0||cell.Identity==0)throw new InvalidOperationException("Candidate import found an invalid loose grain.");
                // Legacy loose positions are lower-left. Cargo positions are local
                // lower-left, so transform their centres through the ship pose.
                var center=cell.Position+Vector2.one*.5f;flags[i]=(cell.Flags&4)!=0;
                if(flags[i])center=World(center,pose);
                // ParallelGrains owns a fixed 256x256 page of four-cell bins.
                // Reject an incompatible legacy world here, before its first
                // tick can fault and leave an unexplained paused presentation.
                if(center.x < -512 || center.x >= 512 || center.y < -512 || center.y >= 512)
                    throw new InvalidOperationException($"Candidate grain {cell.Identity} at ({center.x:F2}, {center.y:F2}) is outside the supported [-512, 512) solver page.");
                var density=catalog.DefinitionAt((ushort)cell.Material).Density;
                if(!float.IsFinite(density)||density<=0)throw new InvalidOperationException("Candidate import found a material without positive density.");
                grainMasses[i]=density;
                grains[i]=new Debris.Simulation.ParallelProof.LooseCell{Center=center,Velocity=cell.Velocity,Angle=flags[i]?pose.z:0,AngularVelocity=flags[i]?snapshot.ShipPose[1].z:0,Material=cell.Material,Identity=cell.Identity,Flags=cell.Flags};
            }
            var bodies=new List<BodyState>();var parameters=new List<BodyParameters>();var patches=new List<Boundary>();
            AddBody(bodies,parameters,patches,snapshot.Capacity,ship.CollisionMask(),pose,snapshot.ShipPose[1],ship.MassProperties(catalog),1,0,-64,-64);
            foreach(var fragment in snapshot.Fragments)
                AddBody(bodies,parameters,patches,snapshot.Capacity,fragment.Hull,fragment.Pose,fragment.Motion,fragment.Mass,1,(uint)bodies.Count,-64,-64);
            // Terrain is represented by an explicit anchored mask body.  Its
            // geometry must fit the solver cache; importing a partial world is
            // never acceptable.
            var terrainState=new CandidateTerrainState(snapshot,snapshot.Capacity+bodies.Count);
            var terrain=terrainState.BuildMask();
            // Patch coordinates are terrain-local; pose is its hull origin.
            // Keeping these spaces separate prevents translated terrain from
            // receiving its world offset twice in the rigid contact shader.
            AddBody(bodies,parameters,patches,snapshot.Capacity,terrain,new Vector4(snapshot.OriginX,snapshot.OriginY,0,1),Vector4.zero,new BodyMass{Mass=1,Inertia=1},0,uint.MaxValue,0,0,true);
            if(patches.Count>4096)throw new InvalidOperationException("Candidate import exceeds the 4,096 collision-patch cache; world activation was refused.");
            if(bodies.Count!=snapshot.Fragments.Length+2)throw new InvalidOperationException("Candidate endpoint accounting is invalid.");
            var solver=new ParallelGrainSolver(grains,bodies.ToArray(),parameters.ToArray(),patches.ToArray(),velocityIterations,positionIterations,grainMasses:grainMasses,allocatedGrainCapacity:snapshot.Capacity,allocatedBoundaryCapacity:4096);
            return new ParallelGameplaySession(solver,snapshot,catalog,flags,terrainState);
        }
        static Vector2 World(Vector2 local,Vector4 pose)
        {float c=Mathf.Cos(pose.z),s=Mathf.Sin(pose.z);return new Vector2(pose.x+local.x*c-local.y*s,pose.y+local.x*s+local.y*c);}
        static void AddBody(List<BodyState> bodies,List<BodyParameters> parameters,List<Boundary> patches,int grainCount,uint[] mask,Vector4 pose,Vector4 motion,BodyMass mass,uint mobility,uint revision,int originX=0,int originY=0,bool enclosePage=false)
        {
            if(mask==null)throw new InvalidOperationException("Candidate import found a body without mask geometry.");
            int width=(int)Mathf.Sqrt(mask.Length);if(width*width!=mask.Length)throw new InvalidOperationException("Candidate mask is not square.");
            uint first=(uint)patches.Count;int body=grainCount+bodies.Count;
            CandidateBoundaryBuilder.AppendMaskBoundaries(patches,mask,body,originX,originY,enclosePage);
            if(patches.Count==first)throw new InvalidOperationException("Candidate import found a body with no collision patches.");
            mass.Validate();var com=mass.Center;
            // The solver stores body centre at COM.  Existing GPU velocity is
            // already the COM velocity and is intentionally not recomputed.
            var center=World(com,pose);
            bodies.Add(new BodyState{Center=center,Velocity=new Vector2(motion.x,motion.y),Angle=pose.z,AngularVelocity=motion.z});
            parameters.Add(new BodyParameters{InverseMass=mobility==0?0:mass.InverseMass,InverseInertia=mobility==0?0:mass.InverseInertia,LocalCOM=com,BoundaryStart=first,BoundaryCount=(uint)patches.Count-first,Mobility=mobility,ShapeRevision=revision});
        }
        public bool Submit(uint tick,MatterStepInput input,double provisionalFuel=0)
        {
            if(disposed||faulted||tick==0||tick<=lastSubmittedTick||pendingCount>=MaximumPendingTicks||provisionalFuel<0||double.IsNaN(provisionalFuel)||double.IsInfinity(provisionalFuel))return false;
            solver.Step(new Vector2(input.LocalForce.x,input.LocalForce.y),input.LocalForce.z);
            pending[(pendingHead+pendingCount)%MaximumPendingTicks]=new Pending{Tick=tick,Fuel=provisionalFuel,Readback=solver.CompletionAsync(solver.BodyStart)};pendingCount++;lastSubmittedTick=tick;return true;
        }
        public bool TryAcknowledge(out Completion completion)
        {
            completion=default;if(disposed||pendingCount==0||!pending[pendingHead].Readback.IsCompleted)return false;
            var next=pending[pendingHead];pending[pendingHead]=null;pendingHead=(pendingHead+1)%MaximumPendingTicks;pendingCount--;if(next.Readback.IsFaulted)
            {
                faulted=true;Fault=next.Readback.Exception?.GetBaseException().Message??"Candidate GPU readback failed.";
                // Commit state is unknowable after readback failure. Keep all
                // reservations until reset so a possible thrust is never free.
                unresolvedFuel+=next.Fuel+ProvisionalFuel-unresolvedFuel;ClearPending();return true;
            }
            var result=next.Readback.Result;if(result.Fault!=SolverFault.None)
            {
                faulted=true;Fault="Candidate solver fault: "+result.Fault;ClearPending();completion=new Completion(next.Tick,result.Fault,default,0,0);return true;
            }
            int count=0;float cargoMass=0;for(int i=0;i<cargo.Length;i++)if(cargo[i]){count++;cargoMass+=catalog.DefinitionAt((ushort)source.Cells[i].Material).Density;}
            completion=new Completion(next.Tick,SolverFault.None,result.State,count,cargoMass,next.Fuel);return true;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // A candidate owns both the command buffer and its completion
            // readbacks. Fence them before releasing their buffers so a reset
            // or destruction cannot invoke a callback against disposed memory.
            AsyncGPUReadback.WaitAllRequests();
            ClearPending();solver.Dispose();
        }
        void ClearPending(){Array.Clear(pending,0,pending.Length);pendingHead=0;pendingCount=0;}
    }
}
