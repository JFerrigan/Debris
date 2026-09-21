using System.Collections;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelStartupTests
    {
        [UnityTest,Timeout(120000)]
        public IEnumerator GeneratedStartupWorldAcknowledgesEmptyCargoAndFlight()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");
            var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);
                source.ConfigureShipBody(ship.MassProperties(catalog));
                var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                Assert.That(read.Result.Cells,Is.Empty);
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    for(uint tick=1;tick<=3;tick++)
                    {
                        var input=new MatterStepInput(null,0,default,false,false,false,new Vector3(ship.MassProperties(catalog).Mass,0,0));
                        Assert.That(candidate.Submit(tick,input),Is.True);
                        ParallelGameplaySession.Completion completion;
                        while(!candidate.TryAcknowledge(out completion))yield return null;
                        Assert.That(candidate.Faulted,Is.False,candidate.Fault);
                        Assert.That(completion.Fault,Is.EqualTo(SolverFault.None));
                        Assert.That(completion.Tick,Is.EqualTo(tick));
                        Assert.That(completion.ShipVelocity.x,Is.GreaterThan(0));
                        Assert.That(candidate.Solver.SnapshotRequests,Is.Zero,"normal acknowledgements must use compact completion readback");
                    }
                }
            }
        }
        [UnityTest,Timeout(120000)]
        public IEnumerator CompletionRingRejectsSaturationWithoutAddingFuelReservation()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    var input=new MatterStepInput(null,0,default,false,false,false,Vector3.zero);
                    for(uint tick=1;tick<=ParallelGameplaySession.MaximumPendingTicks;tick++)Assert.That(candidate.Submit(tick,input,.25),Is.True);
                    Assert.That(candidate.PendingTicks,Is.EqualTo(ParallelGameplaySession.MaximumPendingTicks));Assert.That(candidate.ProvisionalFuel,Is.EqualTo(4).Within(.000001));
                    Assert.That(candidate.Submit(17,input,.25),Is.False);Assert.That(candidate.PendingTicks,Is.EqualTo(ParallelGameplaySession.MaximumPendingTicks));Assert.That(candidate.ProvisionalFuel,Is.EqualTo(4).Within(.000001));
                }
            }
        }
        [UnityTest,Timeout(120000)]
        public IEnumerator SubmissionIdentityIsMonotonicAcrossAcknowledgements()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    var input=new MatterStepInput(null,0,default,false,false,false,Vector3.zero);Assert.That(candidate.NextSubmissionTick,Is.EqualTo(1));Assert.That(candidate.Submit(candidate.NextSubmissionTick,input),Is.True);
                    ParallelGameplaySession.Completion completion;while(!candidate.TryAcknowledge(out completion))yield return null;Assert.That(completion.Tick,Is.EqualTo(1));Assert.That(candidate.NextSubmissionTick,Is.EqualTo(2));Assert.That(candidate.Submit(1,input),Is.False);Assert.That(candidate.Submit(candidate.NextSubmissionTick,input),Is.True);
                }
            }
        }
        [UnityTest,Timeout(120000)]
        public IEnumerator ImportedShipUsesHullOriginAndRotatedLocalCom()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                var snapshot=read.Result;snapshot.OriginX=121;snapshot.OriginY=-95;snapshot.ShipPose[0]=new Vector4(31.25f,-17.5f,.61f,1);snapshot.ShipPose[1]=new Vector4(3,-2,.17f,0);var mass=ship.MassProperties(catalog);
                float c=Mathf.Cos(snapshot.ShipPose[0].z),s=Mathf.Sin(snapshot.ShipPose[0].z);var expected=new Vector2(snapshot.ShipPose[0].x+mass.Center.x*c-mass.Center.y*s,snapshot.ShipPose[0].y+mass.Center.x*s+mass.Center.y*c);
                using(var candidate=ParallelGameplaySession.Import(snapshot,ship,catalog))
                {
                    var state=candidate.Solver.SnapshotAsync();while(!state.IsCompleted)yield return null;var shipState=state.Result.Endpoints[candidate.Solver.BodyStart];
                    Assert.That(candidate.Solver.BodyStart,Is.EqualTo(snapshot.Capacity));Assert.That(shipState.Center,Is.EqualTo(expected).Using(new Vector2Comparer(.0001f)));Assert.That(shipState.Angle,Is.EqualTo(.61f).Within(.0001f));Assert.That(shipState.Velocity,Is.EqualTo(new Vector2(3,-2)).Using(new Vector2Comparer(.0001f)));Assert.That(shipState.AngularVelocity,Is.EqualTo(.17f).Within(.0001f));Assert.That(state.Result.Endpoints[candidate.Solver.BodyStart+1].Center,Is.EqualTo(new Vector2(121,-95)).Using(new Vector2Comparer(.0001f)));
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator ReservedGrainCapacityKeepsShipEndpointStableOnGrowth()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    var before=candidate.Solver.SnapshotAsync();while(!before.IsCompleted)yield return null;int endpoint=candidate.Solver.BodyStart;var material=(ushort)1;
                    Assert.That(candidate.Solver.GrainCapacity,Is.EqualTo(8192));Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=new Vector2(-300,0),Material=material,Identity=999999},catalog.DefinitionAt(material).Density),Is.True);
                    var after=candidate.Solver.SnapshotAsync();while(!after.IsCompleted)yield return null;
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=new Vector2(-302,0),Material=material,Identity=999999},catalog.DefinitionAt(material).Density),Is.False);Assert.That(candidate.Solver.GrainCount,Is.EqualTo(1));Assert.That(after.Result.Grains[0].Identity,Is.EqualTo(999999));Assert.That(after.Result.Endpoints[endpoint].Center,Is.EqualTo(before.Result.Endpoints[endpoint].Center).Using(new Vector2Comparer(.0001f)));
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator EffectiveDoorOpensWithoutSnapshotAndClosesAfterFence()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    uint initial=candidate.Solver.BodyDefinitions[0].ShapeRevision;
                    Assert.That(candidate.Submit(1,new MatterStepInput(null,0,default,true,false,false,Vector3.zero)),Is.True);Assert.That(candidate.DoorOpen,Is.True);Assert.That(candidate.Solver.SnapshotRequests,Is.Zero);Assert.That(candidate.Solver.BodyDefinitions[0].ShapeRevision,Is.EqualTo(initial+1));
                    Assert.That(candidate.Submit(2,new MatterStepInput(null,0,default,false,false,false,Vector3.zero)),Is.False,"door transition must wait for the submitted tick");
                    ParallelGameplaySession.Completion completion;while(!candidate.TryAcknowledge(out completion))yield return null;
                    Assert.That(candidate.Submit(2,new MatterStepInput(null,0,default,false,false,false,Vector3.zero)),Is.False);
                    while(!candidate.Submit(2,new MatterStepInput(null,0,default,false,false,false,Vector3.zero)))yield return null;
                    Assert.That(candidate.DoorOpen,Is.False);Assert.That(candidate.DoorObstructed,Is.False);Assert.That(candidate.Solver.SnapshotRequests,Is.EqualTo(1));Assert.That(candidate.Solver.BodyDefinitions[0].ShapeRevision,Is.EqualTo(initial+2));
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator ObstructedEffectiveDoorStaysOpen()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip")){DoorOpen=true};
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    var grain=new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(-27.5f,.5f)),Material=1,Identity=99991};
                    Assert.That(candidate.Solver.TryAppend(grain,catalog.DefinitionAt(1).Density),Is.True);
                    uint initial=candidate.Solver.BodyDefinitions[0].ShapeRevision;var close=new MatterStepInput(null,0,default,false,false,false,Vector3.zero);
                    Assert.That(candidate.Submit(1,close),Is.False);while(!candidate.Submit(1,close))yield return null;
                    Assert.That(candidate.DoorOpen,Is.True);Assert.That(candidate.DoorObstructed,Is.True);Assert.That(candidate.Solver.SnapshotRequests,Is.EqualTo(1));Assert.That(candidate.Solver.BodyDefinitions[0].ShapeRevision,Is.EqualTo(initial));Assert.That(candidate.Faulted,Is.False,candidate.Fault);
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator CandidateCargoUsesWholeSquareCavityAndCompactMaterialFacts()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));ship.Blueprint.CargoCavity=new RectInt(-10,-10,20,20);
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(Vector2.zero),Material=1,Identity=811},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(9.75f,0)),Material=1,Identity=812},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Submit(1,new MatterStepInput(null,0,default,false,false,false,Vector3.zero)),Is.True);ParallelGameplaySession.Completion completion;while(!candidate.TryAcknowledge(out completion))yield return null;
                    Assert.That(completion.CargoCount,Is.EqualTo(1));Assert.That(completion.CargoMass,Is.EqualTo(catalog.DefinitionAt(1).Density).Within(.0001f));Assert.That(candidate.Solver.SnapshotRequests,Is.Zero,"normal cargo acknowledgement must remain compact");
                    var snapshot=candidate.Solver.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;Assert.That(snapshot.Result.Grains[0].Flags&4,Is.EqualTo(4));Assert.That(snapshot.Result.Grains[1].Flags&4,Is.Zero);
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator CandidateSuctionUsesCurrentShipPoseOutsideItsIntactMouth()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(-48,21)),Material=1,Identity=821},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(-20,21)),Material=1,Identity=822},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Submit(1,new MatterStepInput(null,40,default,false,false,true,Vector3.zero)),Is.True);ParallelGameplaySession.Completion completion;while(!candidate.TryAcknowledge(out completion))yield return null;
                    var snapshot=candidate.Solver.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;Assert.That(snapshot.Result.Grains[0].Velocity.x,Is.GreaterThan(.5f));Assert.That(snapshot.Result.Grains[1].Velocity.magnitude,Is.LessThan(.001f),"suction must not pull through the intact hull");Assert.That(candidate.Faulted,Is.False,candidate.Fault);
                }
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator CandidateCargoCapacityRejectsTheNextWholeSquare()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));ship.Blueprint.CargoCavity=new RectInt(-10,-10,20,20);
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    candidate.Solver.ConfigureCargoCavity(ship.Blueprint.CargoCavity,1);
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(-5,0)),Material=1,Identity=831},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Solver.TryAppend(new Debris.Simulation.ParallelProof.LooseCell{Center=ship.ToWorld(new Vector2(5,0)),Material=1,Identity=832},catalog.DefinitionAt(1).Density),Is.True);
                    Assert.That(candidate.Submit(1,new MatterStepInput(null,0,default,false,false,false,Vector3.zero)),Is.True);ParallelGameplaySession.Completion completion;while(!candidate.TryAcknowledge(out completion))yield return null;
                    Assert.That(completion.CargoCount,Is.EqualTo(1));Assert.That(completion.CargoRejected,Is.EqualTo(1));var snapshot=candidate.Solver.SnapshotAsync();while(!snapshot.IsCompleted)yield return null;Assert.That(((snapshot.Result.Grains[0].Flags&4)!=0)^((snapshot.Result.Grains[1].Flags&4)!=0),Is.True);
                }
            }
        }
        sealed class Vector2Comparer : System.Collections.Generic.IEqualityComparer<Vector2>
        {
            readonly float tolerance;public Vector2Comparer(float value){tolerance=value;}public bool Equals(Vector2 a,Vector2 b)=>Vector2.Distance(a,b)<=tolerance;public int GetHashCode(Vector2 value)=>0;
        }
    }
}
