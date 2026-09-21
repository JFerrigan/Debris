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
        Boundary[] dynamicPrefix;
        readonly Boundary[] nonShipDynamic;
        readonly uint[] closedShipMask;
        readonly RectInt[] doorCells;
        CandidateBoundaryBuilder.TerrainBoundaryCache terrainCache;
        CandidateBoundaryBuilder.TerrainBoundaryCache preparedTerrainCache;
        int pendingHead,pendingCount;
        uint lastSubmittedTick;
        bool faulted,disposed;
        bool doorOpen,doorRequestKnown,requestedDoorOpen,doorObstructed,doorTransitionPending;
        Task<ProofSnapshot> doorSnapshot;
        bool drillRequested,drillValidating;
        float drillPower=120,drillRadius=6;
        uint drillRequestId;
        BodyState acknowledgedShip;
        CandidateTerrainEdit drillEdit;
        CandidateTerrainTransaction transaction;
        CandidateEditResult lastEdit;
        double unresolvedFuel;
        public bool Faulted=>faulted;
        public string Fault { get; private set; }
        public int PendingTicks=>pendingCount;
        public uint NextSubmissionTick=>lastSubmittedTick+1;
        public ParallelGrainSolver Solver=>solver;
        public CandidateTerrainState Terrain=>terrain;
        public uint TerrainRevision=>terrain.Revision;
        public bool TopologyBusy=>drillRequested||drillValidating||(transaction?.Busy??false)||doorSnapshot!=null;
        public CandidateEditResult LastEditResult=>lastEdit;
        public bool DrawingAvailable=>!disposed&&!faulted;
        public bool DoorOpen=>doorOpen;
        public bool DoorObstructed=>doorObstructed;
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

        ParallelGameplaySession(ParallelGrainSolver value, MatterSnapshot imported, MaterialCatalog materials, bool[] cargoFlags, CandidateTerrainState terrainState, Boundary[] patches, BodyState shipState, uint[] closedMask, RectInt[] doors, bool initialDoorOpen)
        {
            solver=value;source=imported;catalog=materials;cargo=cargoFlags;terrain=terrainState;terrainCache=new CandidateBoundaryBuilder.TerrainBoundaryCache(terrainState);acknowledgedShip=shipState;closedShipMask=closedMask;doorCells=doors;doorOpen=initialDoorOpen;
            int prefix=(int)solver.BodyDefinitions[solver.BodyCount-1].BoundaryStart;dynamicPrefix=new Boundary[prefix];Array.Copy(patches,dynamicPrefix,prefix);
            int shipCount=(int)solver.BodyDefinitions[0].BoundaryCount;nonShipDynamic=new Boundary[prefix-shipCount];Array.Copy(patches,shipCount,nonShipDynamic,0,nonShipDynamic.Length);
        }

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
            var closedMask=CandidateShipMask(ship,false);var initialMask=CandidateShipMask(ship,ship.DoorOpen);
            AddBody(bodies,parameters,patches,snapshot.Capacity,initialMask,pose,snapshot.ShipPose[1],ship.MassProperties(catalog),1,0,-64,-64);
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
            AddBody(bodies,parameters,patches,snapshot.Capacity,terrain,new Vector4(snapshot.OriginX,snapshot.OriginY,0,1),Vector4.zero,new BodyMass{Mass=1,Inertia=1},0,terrainState.Revision,0,0,true);
            if(patches.Count>4096)throw new InvalidOperationException("Candidate import exceeds the 4,096 collision-patch cache; world activation was refused.");
            if(bodies.Count!=snapshot.Fragments.Length+2)throw new InvalidOperationException("Candidate endpoint accounting is invalid.");
            var solver=new ParallelGrainSolver(grains,bodies.ToArray(),parameters.ToArray(),patches.ToArray(),velocityIterations,positionIterations,grainMasses:grainMasses,allocatedGrainCapacity:snapshot.Capacity,allocatedBoundaryCapacity:4096);
            return new ParallelGameplaySession(solver,snapshot,catalog,flags,terrainState,patches.ToArray(),bodies[0],closedMask,DoorCells(ship),ship.DoorOpen);
        }
        static uint[] CandidateShipMask(ShipRuntime ship,bool open)
        {
            var mask=ship.CollisionMask();if(!open)return mask;
            foreach(var unit in ship.Units)if(unit.Supported&&!unit.Destroyed&&unit.Placement.Definition.Kind==UnitKind.Door)
            {
                var box=new RectInt(unit.Placement.Position,unit.Placement.Definition.Size);
                for(int y=box.yMin;y<box.yMax;y++)for(int x=box.xMin;x<box.xMax;x++)mask[(y+64)*128+x+64]=0;
            }
            return mask;
        }
        static RectInt[] DoorCells(ShipRuntime ship)
        {
            var cells=new List<RectInt>();foreach(var unit in ship.Units)if(unit.Supported&&!unit.Destroyed&&unit.Placement.Definition.Kind==UnitKind.Door)cells.Add(new RectInt(unit.Placement.Position,unit.Placement.Definition.Size));return cells.ToArray();
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
            if(disposed||faulted||drillRequested||drillValidating||(transaction?.Busy??false)||tick==0||tick<=lastSubmittedTick||pendingCount>=MaximumPendingTicks||provisionalFuel<0||double.IsNaN(provisionalFuel)||double.IsInfinity(provisionalFuel))return false;
            if(!TryApplyDoorRequest(input.DoorRequestedOpen))return false;
            solver.Step(new Vector2(input.LocalForce.x,input.LocalForce.y),input.LocalForce.z);
            pending[(pendingHead+pendingCount)%MaximumPendingTicks]=new Pending{Tick=tick,Fuel=provisionalFuel,Readback=solver.CompletionAsync(solver.BodyStart)};pendingCount++;lastSubmittedTick=tick;return true;
        }
        bool TryApplyDoorRequest(bool requestedOpen)
        {
            if(!doorRequestKnown||requestedDoorOpen!=requestedOpen)
            {
                doorRequestKnown=true;requestedDoorOpen=requestedOpen;doorObstructed=false;doorTransitionPending=requestedOpen!=doorOpen;
            }
            if(doorTransitionPending&&pendingCount>0)return false;
            if(doorTransitionPending&&requestedDoorOpen){doorTransitionPending=!ReplaceShipDoorTopology(true);return !doorTransitionPending;}
            if(doorTransitionPending&&doorSnapshot==null)try{doorSnapshot=solver.SnapshotAsync();return false;}
            catch(Exception e){FaultTransaction("Candidate door inspection submission failed: "+e.Message);return false;}
            if(doorSnapshot==null)return true;
            if(!doorSnapshot.IsCompleted)return false;
            try
            {
                var snapshot=doorSnapshot.Result;doorSnapshot=null;
                if(snapshot.Fault!=SolverFault.None){FaultTransaction("Candidate door inspection solver fault: "+snapshot.Fault);return false;}
                if(AnyDoorObstruction(snapshot)){doorObstructed=true;doorTransitionPending=false;return true;}
                doorTransitionPending=!ReplaceShipDoorTopology(false);return !doorTransitionPending;
            }
            catch(Exception e){doorSnapshot=null;FaultTransaction("Candidate door inspection readback failed: "+e.Message);return false;}
        }
        bool ReplaceShipDoorTopology(bool open)
        {
            if(pendingCount!=0)return false;
            var patches=new List<Boundary>();CandidateBoundaryBuilder.AppendMaskBoundaries(patches,CandidateShipMask(closedShipMask,open),solver.BodyStart,-64,-64,false);int shipCount=patches.Count;
            patches.AddRange(nonShipDynamic);var terrainPatches=terrainCache.Flatten();patches.AddRange(terrainPatches);
            if(patches.Count>solver.BoundaryCapacity){FaultTransaction("Candidate door boundary cache exceeded its reserved capacity.");return false;}
            for(int i=0;i<patches.Count;i++){var patch=patches[i];patch.Feature=(uint)i;patches[i]=patch;}
            var definitions=solver.BodyDefinitions;int oldShipCount=(int)definitions[0].BoundaryCount,delta=shipCount-oldShipCount;
            try
            {
                definitions[0].BoundaryStart=0;definitions[0].BoundaryCount=(uint)shipCount;definitions[0].ShapeRevision=checked(definitions[0].ShapeRevision+1);
                for(int i=1;i<definitions.Length-1;i++)definitions[i].BoundaryStart=checked((uint)((int)definitions[i].BoundaryStart+delta));
                int terrainDefinition=definitions.Length-1;definitions[terrainDefinition].BoundaryStart=(uint)(shipCount+nonShipDynamic.Length);definitions[terrainDefinition].BoundaryCount=(uint)terrainPatches.Length;
            }
            catch(OverflowException){FaultTransaction("Candidate door shape revision overflowed.");return false;}
            if(!solver.TryReplaceBoundaryCache(patches.ToArray(),definitions)){FaultTransaction("Candidate door boundary publication failed.");return false;}
            dynamicPrefix=new Boundary[shipCount+nonShipDynamic.Length];Array.Copy(patches.ToArray(),dynamicPrefix,dynamicPrefix.Length);
            doorOpen=open;return true;
        }
        static uint[] CandidateShipMask(uint[] closedMask,bool open)
        {
            var mask=(uint[])closedMask.Clone();if(!open)return mask;
            for(int i=0;i<mask.Length;i++)if(mask[i]==uint.MaxValue)mask[i]=0;return mask;
        }
        bool AnyDoorObstruction(ProofSnapshot snapshot)
        {
            if(doorCells.Length==0)return false;var body=snapshot.Endpoints[solver.BodyStart];var com=solver.BodyDefinitions[0].LocalCOM;
            foreach(var grain in snapshot.Grains)
            {
                var local=ToLocal(grain.Center,body)+com;float angle=grain.Angle-body.Angle;
                foreach(var door in doorCells)if(Overlaps(local,angle,door))return true;
            }
            return false;
        }
        static Vector2 ToLocal(Vector2 point,BodyState body)
        {var delta=point-body.Center;float c=Mathf.Cos(body.Angle),s=Mathf.Sin(body.Angle);return new Vector2(delta.x*c+delta.y*s,-delta.x*s+delta.y*c);}
        static bool Overlaps(Vector2 center,float angle,RectInt box)
        {
            var boxCenter=(Vector2)box.position+(Vector2)box.size*.5f;var boxHalf=(Vector2)box.size*.5f;var x=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));var y=new Vector2(-x.y,x.x);var axes=new[]{Vector2.right,Vector2.up,x,y};
            foreach(var axis in axes){float grain=.5f*(Mathf.Abs(Vector2.Dot(axis,x))+Mathf.Abs(Vector2.Dot(axis,y)));float rect=boxHalf.x*Mathf.Abs(axis.x)+boxHalf.y*Mathf.Abs(axis.y);if(Mathf.Abs(Vector2.Dot(center-boxCenter,axis))>grain+rect)return false;}return true;
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
            acknowledgedShip=result.State;
            completion=new Completion(next.Tick,SolverFault.None,result.State,count,cargoMass,next.Fuel);return true;
        }
        public void AttachTerrainMirror(MatterSession mirror)
        {
            if(disposed||faulted||transaction!=null)throw new InvalidOperationException("Candidate terrain mirror is unavailable.");
            transaction=new CandidateTerrainTransaction(solver,mirror,terrain);
        }
        public bool RequestMountedDrill(float power=120,float radius=6)
        {
            if(disposed||faulted||transaction==null||TopologyBusy||!float.IsFinite(power)||!float.IsFinite(radius)||power<0||radius<0)return false;
            drillRequested=true;drillPower=power;drillRadius=radius;drillRequestId++;return true;
        }
        Vector2 MountedDrillCenter()
        {
            var local=solver.BodyDefinitions[0].LocalCOM;var offset=new Vector2(58,0)-local;float c=Mathf.Cos(acknowledgedShip.Angle),s=Mathf.Sin(acknowledgedShip.Angle);
            return acknowledgedShip.Center+new Vector2(offset.x*c-offset.y*s,offset.x*s+offset.y*c);
        }
        public bool TryAdvanceTerrainEdit(out CandidateEditResult result)
        {
            result=lastEdit;if(disposed||faulted||!drillRequested)return false;
            if(pendingCount>0){lastEdit=new CandidateEditResult(CandidateEditStatus.Busy,drillRequestId,default,0,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
            if(!drillValidating)
            {
                if(!terrain.TrySelectDrillCell(MountedDrillCenter(),drillRadius,out var cell)){drillRequested=false;lastEdit=new CandidateEditResult(CandidateEditStatus.NoTarget,drillRequestId,default,0,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
                var status=terrain.TryPrepareCell(cell,drillPower,1f/60,catalog,out drillEdit);
                if(status!=CandidateEditStatus.DamageApplied&&status!=CandidateEditStatus.Released){drillRequested=false;lastEdit=new CandidateEditResult(status,drillRequestId,cell,terrain.MaterialAt(cell),0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
                CandidateBoundaryBuilder.CandidateBoundaryReplacement replacement=null;CandidateBoundaryBuilder.TerrainBoundaryCache nextCache=null;
                if(drillEdit.Release&&!CandidateBoundaryBuilder.TryPrepareTerrainReplacement(terrain,drillEdit,terrainCache,solver.BodyDefinitions,dynamicPrefix,solver.BoundaryCapacity,out replacement,out nextCache)){drillRequested=false;lastEdit=new CandidateEditResult(CandidateEditStatus.BoundaryCapacity,drillRequestId,cell,drillEdit.OldMaterial,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
                float density=catalog.DefinitionAt((ushort)drillEdit.OldMaterial).Density;status=transaction.Prepare(drillEdit,replacement,density);if(status!=drillEdit.Status){drillRequested=false;lastEdit=new CandidateEditResult(status,drillRequestId,cell,drillEdit.OldMaterial,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
                if(!drillEdit.Release){var damagePublished=transaction.TryPublish();lastEdit=new CandidateEditResult(damagePublished,drillRequestId,cell,drillEdit.OldMaterial,0,terrain.Revision,solver.GrainCount);drillRequested=false;drillEdit=null;result=lastEdit;return true;}
                preparedTerrainCache=nextCache;try{transaction.BeginPlacementValidation();drillValidating=true;return false;}catch(Exception e){transaction.Abort();preparedTerrainCache=null;FaultTransaction("Candidate terrain placement submission failed: "+e.Message);drillRequested=false;drillEdit=null;lastEdit=new CandidateEditResult(CandidateEditStatus.Faulted,drillRequestId,cell,0,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
            }
            if(!transaction.TryCompletePlacementValidation(out var validation))return false;
            if(validation!=drillEdit.Status){transaction.Abort();preparedTerrainCache=null;if(validation==CandidateEditStatus.Faulted)FaultTransaction("Candidate terrain placement readback failed.");drillRequested=drillValidating=false;lastEdit=new CandidateEditResult(validation,drillRequestId,drillEdit.Cell,drillEdit.OldMaterial,0,terrain.Revision,solver.GrainCount);result=lastEdit;return true;}
            var published=transaction.TryPublish();if(published==CandidateEditStatus.Released)terrainCache=preparedTerrainCache;preparedTerrainCache=null;if(published==CandidateEditStatus.Faulted)FaultTransaction("Candidate terrain publication failed.");lastEdit=new CandidateEditResult(published,drillRequestId,drillEdit.Cell,drillEdit.OldMaterial,published==CandidateEditStatus.Released?drillEdit.Identity:0,terrain.Revision,solver.GrainCount);drillRequested=drillValidating=false;drillEdit=null;result=lastEdit;return true;
        }
        void FaultTransaction(string message){faulted=true;Fault=message;ClearPending();}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // A candidate owns both the command buffer and its completion
            // readbacks. Fence them before releasing their buffers so a reset
            // or destruction cannot invoke a callback against disposed memory.
            AsyncGPUReadback.WaitAllRequests();
            ClearPending();transaction?.Dispose();solver.Dispose();
        }
        void ClearPending(){Array.Clear(pending,0,pending.Length);pendingHead=0;pendingCount=0;}
    }
}
