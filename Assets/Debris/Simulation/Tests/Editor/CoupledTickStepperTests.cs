using System;
using Debris.Simulation.CoupledContacts;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledTickStepperTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly CoupledContactArena Arena=new CoupledContactArena();
            public readonly CoupledTickInitialState Initial;
            public readonly CoupledTickState State;
            public readonly CoupledTickTransaction Transaction;
            public readonly CoupledContactOperator Graph;
            public readonly CoupledTickStepper Stepper;
            public readonly CommandBuffer Commands=new CommandBuffer();

            public Fixture(Vector2 center,Vector2 velocity,float spin=0)
            {
                Assume.That(SystemInfo.supportsComputeShaders,Is.True);
                var grain=new ProofGrain{Center=center,Velocity=velocity,AngularVelocity=spin,Material=1,Identity=7};
                Initial=new CoupledTickInitialState{
                    Endpoints=new[]{new BodyState{Center=center,Velocity=velocity,AngularVelocity=spin},
                        new BodyState{Center=center+Vector2.right*3}},
                    Grains=new[]{grain},
                    Parameters=new[]{new BodyParameters{InverseMass=.5f,InverseInertia=3,Mobility=1,ShapeRevision=1},
                        new BodyParameters{ShapeRevision=1}},
                    Bindings=new[]{Binding(CoupledPersistentIdentity.Grain(7)),
                        Binding(new CoupledPersistentIdentity{Kind=2,Low=11})},
                    Cache=new[]{new CoupledCachePoint{ImpulseNormal=Vector4.one}},
                    InventoryWords=new uint[]{10,20},CargoFacts=new uint[]{0,0,0},TerrainRevisions=new uint[]{1,2}
                };
                var header=Initial.Header;header.ActiveCacheCount=1;Initial.Header=header;
                try
                {
                    State=new CoupledTickState(Initial,Arena);
                    Transaction=new CoupledTickTransaction(State);
                    Graph=new CoupledContactOperator(new[]{
                        new CoupledContactBody{Center=center,InverseMass=.5f,InverseInertia=3},
                        new CoupledContactBody{Center=Initial.Endpoints[1].Center}
                    },new[]{
                        new CoupledContactPrimitive{Center=center,Half=Vector2.one*.5f,ContactClass=1},
                        new CoupledContactPrimitive{Center=Initial.Endpoints[1].Center,Half=Vector2.one*.5f,
                            Endpoint=1,Anchored=1,ContactClass=2}
                    },center-Vector2.one*4,1,8,8,arena:Arena);
                    Stepper=new CoupledTickStepper(State,Transaction,Graph);
                }
                catch { Dispose();throw; }
            }
            static CoupledEndpointBinding Binding(CoupledPersistentIdentity identity)=>new CoupledEndpointBinding{
                Identity=identity,Bound=1,Generation=1,SlotGeneration=1,Revision=1};
            public void Execute(){Graphics.ExecuteCommandBuffer(Commands);Commands.Clear();}
            public CoupledTickCompletion Completion(CoupledTickTicket ticket)
            {
                var result=new CoupledTickCompletion[1];
                Transaction.Completions.GetData(result,0,(int)ticket.Slot,1);return result[0];
            }
            public static T[] Read<T>(GraphicsBuffer buffer,int count) where T:struct
            {var values=new T[count];buffer.GetData(values,0,0,count);return values;}
            public void AssertRestored()
            {
                Assert.That(Read<CoupledTickHeader>(State.CommittedHeader,1),Is.EqualTo(new[]{Initial.Header}));
                Assert.That(Read<CoupledTickHeader>(State.WorkingHeader,1),Is.EqualTo(new[]{Initial.Header}));
                foreach(bool working in new[]{false,true})
                {
                    Assert.That(Read<BodyState>(working?State.WorkingEndpoints:State.CommittedEndpoints,2),Is.EqualTo(Initial.Endpoints));
                    Assert.That(Read<ProofGrain>(working?State.WorkingGrains:State.CommittedGrains,1),Is.EqualTo(Initial.Grains));
                    Assert.That(Read<BodyParameters>(working?State.WorkingParameters:State.CommittedParameters,2),Is.EqualTo(Initial.Parameters));
                    Assert.That(Read<CoupledEndpointBinding>(working?State.WorkingBindings:State.CommittedBindings,2),Is.EqualTo(Initial.Bindings));
                    Assert.That(Read<CoupledCachePoint>(working?State.WorkingCache:State.CommittedCache,1),Is.EqualTo(Initial.Cache));
                    Assert.That(Read<uint>(working?State.WorkingInventoryWords:State.CommittedInventoryWords,2),Is.EqualTo(Initial.InventoryWords));
                    Assert.That(Read<uint>(working?State.WorkingCargoFacts:State.CommittedCargoFacts,3),Is.EqualTo(Initial.CargoFacts));
                    Assert.That(Read<uint>(working?State.WorkingTerrainRevisions:State.CommittedTerrainRevisions,2),Is.EqualTo(Initial.TerrainRevisions));
                }
                var bodies=Read<CoupledContactBody>(Graph.BodiesBuffer,2);
                Assert.That(bodies[0].Center,Is.EqualTo(Initial.Endpoints[0].Center));
                Assert.That(bodies[1].Center,Is.EqualTo(Initial.Endpoints[1].Center));
            }
            public void Dispose()
            {
                Commands.Dispose();Stepper?.Dispose();Graph?.Dispose();Transaction?.Dispose();State?.Dispose();Arena.AssertEmpty();
            }
        }

        [Test,Timeout(600000)]
        public void ForcedFreeTickPublishesAnalyticalMotionAndPreservesInactiveSlotFacts()
        {
            using(var f=new Fixture(Vector2.zero,Vector2.right*.25f,.1f))
            using(var forces=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,16))
            {
                forces.SetData(new[]{new Vector4(2,-1,.25f,0),new Vector4(50,25,10,0)});
                var ticket=f.Stepper.RecordTick(f.Commands,1,CoupledSolveProfile.C1,forces);f.Execute();
                var completion=f.Completion(ticket);
                Assert.That(completion.Status,Is.EqualTo((uint)CoupledTickStatus.Committed));
                Assert.That(completion.Substeps,Is.EqualTo(4u));Assert.That(completion.PassedSubsteps,Is.EqualTo(4u));
                var clock=Fixture.Read<uint>(f.Stepper.StepClock,4);
                var facts=Fixture.Read<uint>(f.Stepper.Diagnostics,16);
                Assert.That(clock[0],Is.EqualTo(4u));Assert.That(clock[2],Is.Zero);
                Assert.That(facts[(int)CoupledTickDiagnosticWord.Fault],Is.Zero);
                Assert.That(facts[(int)CoupledTickDiagnosticWord.CheckedSubsteps],Is.EqualTo(4u));
                Assert.That(Fixture.Read<uint>(f.Stepper.FinalDecision,2),Is.EqualTo(new uint[]{1,0}));
                Assert.That(Fixture.Read<uint>(f.Stepper.FinalGeometryDecision,8)[0],Is.EqualTo(1u));
                const float dt=1f/60f,h=dt/4;
                var endpoint=Fixture.Read<BodyState>(f.State.CommittedEndpoints,2);
                var expectedVelocity=new Vector2(.25f+dt,-.5f*dt);
                var expectedCenter=Vector2.right*(.25f*dt)+new Vector2(1,-.5f)*(10*h*h);
                Assert.That(endpoint[0].Velocity.x,Is.EqualTo(expectedVelocity.x).Within(2e-6));
                Assert.That(endpoint[0].Velocity.y,Is.EqualTo(expectedVelocity.y).Within(2e-6));
                Assert.That(endpoint[0].Center.x,Is.EqualTo(expectedCenter.x).Within(2e-6));
                Assert.That(endpoint[0].Center.y,Is.EqualTo(expectedCenter.y).Within(2e-6));
                Assert.That(endpoint[0].AngularVelocity,Is.EqualTo(.1f+.75f*dt).Within(2e-6));
                Assert.That(endpoint[0].Angle,Is.EqualTo(.1f*dt+.75f*10*h*h).Within(2e-6));
                Assert.That(endpoint[1],Is.EqualTo(f.Initial.Endpoints[1]),"Applied forces cannot move the explicit anchor");
                var grain=Fixture.Read<ProofGrain>(f.State.CommittedGrains,1)[0];
                Assert.That(grain.Center,Is.EqualTo(endpoint[0].Center));Assert.That(grain.Velocity,Is.EqualTo(endpoint[0].Velocity));
                Assert.That(grain.Angle,Is.EqualTo(endpoint[0].Angle));Assert.That(grain.AngularVelocity,Is.EqualTo(endpoint[0].AngularVelocity));
                Assert.That(grain.Material,Is.EqualTo(1u));Assert.That(grain.Identity,Is.EqualTo(7u));
                var header=Fixture.Read<CoupledTickHeader>(f.State.CommittedHeader,1)[0];
                Assert.That(header.CompletedTick,Is.EqualTo(1ul));Assert.That(header.Generation,Is.EqualTo(2u));
                Assert.That(header.CacheGeneration,Is.EqualTo(2u));Assert.That(header.ActiveCacheCount,Is.Zero);
                Assert.That(Fixture.Read<uint>(f.State.CommittedInventoryWords,2),Is.EqualTo(f.Initial.InventoryWords));
                Assert.That(Fixture.Read<uint>(f.State.CommittedCargoFacts,3),Is.EqualTo(f.Initial.CargoFacts));
                Assert.That(Fixture.Read<uint>(f.State.CommittedTerrainRevisions,2),Is.EqualTo(f.Initial.TerrainRevisions));
                f.Transaction.Retire(ticket,completion);
            }
        }

        [Test,Timeout(600000)]
        public void MoreThanSixteenRequiredSubstepsRejectsWithoutPublishing()
        {
            using(var f=new Fixture(Vector2.zero,Vector2.right*121))
            {
                var ticket=f.Stepper.RecordTick(f.Commands,1,CoupledSolveProfile.C1);f.Execute();
                var completion=f.Completion(ticket);f.AssertRestored();
                Assert.That(completion.Status,Is.EqualTo((uint)CoupledTickStatus.Rejected));
                Assert.That(completion.Fault,Is.EqualTo((uint)CoupledTickFault.Speed));
                Assert.That(completion.Stage,Is.EqualTo((uint)CoupledTickStage.Input));
                Assert.That(completion.PassedSubsteps,Is.Zero);
                Assert.That(Fixture.Read<uint>(f.Stepper.StepClock,4)[2],Is.EqualTo(1u));
                f.Transaction.Retire(ticket,completion);
            }
        }

        [Test,Timeout(600000)]
        public void GrainPageFailureAfterPredictionRestoresEveryPayloadAndRejectsQueuedTick()
        {
            using(var f=new Fixture(new Vector2(511.99f,0),Vector2.right*4))
            {
                var first=f.Stepper.RecordTick(f.Commands,1,CoupledSolveProfile.C1);
                var second=f.Stepper.RecordTick(f.Commands,2,CoupledSolveProfile.C1);f.Execute();
                var failure=f.Completion(first);var dependency=f.Completion(second);f.AssertRestored();
                Assert.That(failure.Status,Is.EqualTo((uint)CoupledTickStatus.Rejected));
                Assert.That(failure.Fault,Is.EqualTo((uint)CoupledTickFault.StageRejected));
                Assert.That(failure.Stage,Is.EqualTo((uint)CoupledTickStage.Substep));
                Assert.That(failure.PassedSubsteps,Is.Zero);
                var retained=Fixture.Read<uint>(f.Transaction.FirstFailure,6);
                Assert.That(retained[1],Is.EqualTo(1u));
                Assert.That(retained[5]&(uint)CoupledTickPhysicalFault.GrainPage,
                    Is.EqualTo((uint)CoupledTickPhysicalFault.GrainPage));
                Assert.That(dependency.Status,Is.EqualTo((uint)CoupledTickStatus.DependencyFailed));
                Assert.That(dependency.CompletedTick,Is.Zero);
                f.Transaction.Retire(first,failure);f.Transaction.Retire(second,dependency);
            }
        }
    }
}
