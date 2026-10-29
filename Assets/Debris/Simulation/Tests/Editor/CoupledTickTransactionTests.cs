using System;
using System.Collections.Generic;
using Debris.Simulation.CoupledContacts;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledTickTransactionTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly CoupledContactArena Arena=new CoupledContactArena();
            public readonly CoupledTickInitialState Initial;
            public readonly CoupledTickState State;
            public readonly CoupledTickTransaction Tick;
            public readonly CommandBuffer Commands=new CommandBuffer();
            readonly List<GraphicsBuffer> staging=new List<GraphicsBuffer>();
            readonly GraphicsBuffer velocityPass,velocityFail,pass,fail;
            ulong NextMutationTick;
            public Fixture(ulong initialTick=0)
            {
                Assume.That(SystemInfo.supportsComputeShaders,Is.True);
                var grain=new ProofGrain{Material=1,Identity=7,Velocity=Vector2.right*.5f};
                Initial=new CoupledTickInitialState{
                    Endpoints=new[]{new BodyState{Velocity=grain.Velocity},new BodyState{Center=Vector2.right*10}},
                    Grains=new[]{grain},Parameters=new[]{
                        new BodyParameters{InverseMass=1,InverseInertia=6,Mobility=1,ShapeRevision=1},
                        new BodyParameters{ShapeRevision=1}},
                    Bindings=new[]{Binding(CoupledPersistentIdentity.Grain(7)),
                        Binding(new CoupledPersistentIdentity{Kind=2,Low=11})},
                    Cache=new[]{new CoupledCachePoint{ImpulseNormal=Vector4.one}},
                    InventoryWords=new uint[]{10,0},CargoFacts=new uint[]{0,0,0},TerrainRevisions=new uint[]{1,2}
                };
                var header=Initial.Header;header.CompletedTick=initialTick;header.ActiveCacheCount=1;Initial.Header=header;
                NextMutationTick=initialTick+1;
                try
                {
                    State=new CoupledTickState(Initial,Arena);Tick=new CoupledTickTransaction(State);
                    velocityPass=Buffer(new uint[]{0,1,0,0});velocityFail=Buffer(new uint[]{0,0,0,123});
                    pass=Buffer(new uint[]{1,0});fail=Buffer(new uint[]{0,77});
                }
                catch { Dispose();throw; }
            }
            static CoupledEndpointBinding Binding(CoupledPersistentIdentity id)=>new CoupledEndpointBinding{
                Identity=id,Bound=1,Generation=1,SlotGeneration=1,Revision=1};
            GraphicsBuffer Buffer<T>(T[] data) where T:struct
            {
                var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,data.Length,
                    System.Runtime.InteropServices.Marshal.SizeOf<T>());
                staging.Add(buffer);buffer.SetData(data);return buffer;
            }
            public void Write<T>(GraphicsBuffer destination,T[] data) where T:struct
            {Commands.CopyBuffer(Buffer(data),destination);}
            // Real GPU copies occur after Begin's snapshot in queue order.
            // No declared stage result here is evidence of physical solving.
            public void Mutate(float movement,uint inventory)
            {
                var endpoints=(BodyState[])Initial.Endpoints.Clone();
                endpoints[0].Center=new Vector2(movement,2);endpoints[0].Velocity=new Vector2(3,4);
                endpoints[0].Angle=.3f;endpoints[0].AngularVelocity=.7f;
                Write(State.WorkingEndpoints,endpoints);
                var grains=(ProofGrain[])Initial.Grains.Clone();grains[0].Flags=1;Write(State.WorkingGrains,grains);
                Write(State.WorkingCache,new[]{new CoupledCachePoint{ImpulseNormal=new Vector4(movement,2,3,4)}});
                Write(State.WorkingInventoryWords,new[]{inventory,99u});
                Write(State.WorkingCargoFacts,new uint[]{1,0,1});
                var header=Initial.Header;header.ActiveCacheCount=0;
                // Preserve the committed generation expected by queued ticks.
                // The fixture fills each tick's header from its CPU recording
                // number; ordinary owners update only this field on the GPU.
                header.CompletedTick=NextMutationTick-1;
                header.Generation=(uint)(NextMutationTick-Initial.Header.CompletedTick);
                header.CacheGeneration=header.Generation;
                Write(State.WorkingHeader,new[]{header});
                NextMutationTick++;
            }
            public void Stages(int substeps,CoupledTickStage reject=0)
            {
                for(int s=0;s<substeps;s++)
                {
                    bool last=s==substeps-1;
                    Tick.RecordVelocityResult(Commands,last&&reject==CoupledTickStage.Velocity?velocityFail:velocityPass);
                    Tick.RecordPredictionResult(Commands,last&&reject==CoupledTickStage.Prediction?fail:pass);
                    Tick.RecordPositionResult(Commands,last&&reject==CoupledTickStage.Position?fail:pass);
                    Tick.RecordSubstep(Commands);
                }
                Tick.RecordFinalFacts(Commands,reject==CoupledTickStage.Final?fail:pass);
            }
            public void Execute(){Graphics.ExecuteCommandBuffer(Commands);Commands.Clear();}
            public CoupledTickCompletion Completion(CoupledTickTicket ticket)
            {
                var result=new CoupledTickCompletion[1];Tick.Completions.GetData(result,0,(int)ticket.Slot,1);return result[0];
            }
            public static T[] Read<T>(GraphicsBuffer buffer,int count) where T:struct
            {var result=new T[count];buffer.GetData(result,0,0,count);return result;}
            public void AssertInitial()
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
            }
            public void Dispose()
            {
                Commands.Dispose();foreach(var buffer in staging)buffer.Dispose();
                Tick?.Dispose();State?.Dispose();Arena.AssertEmpty();
            }
        }

        [TestCase(CoupledTickStage.Velocity)]
        [TestCase(CoupledTickStage.Prediction)]
        [TestCase(CoupledTickStage.Position)]
        [TestCase(CoupledTickStage.Final)]
        public void FailureAfterMovementRollsBackEveryPayloadAndRejectsQueuedDependency(CoupledTickStage stage)
        {
            using(var f=new Fixture())
            {
                var first=f.Tick.RecordBegin(f.Commands,1,2);f.Mutate(5,9);f.Stages(2,stage);f.Tick.RecordEnd(f.Commands);
                var second=f.Tick.RecordBegin(f.Commands,2,1);f.Mutate(99,1);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                f.Execute();f.AssertInitial();
                var a=f.Completion(first);var b=f.Completion(second);
                Assert.That(a.Status,Is.EqualTo(2u));Assert.That(a.Fault,Is.EqualTo((uint)CoupledTickFault.StageRejected));
                Assert.That(a.Stage,Is.EqualTo((uint)stage));
                Assert.That(a.PassedSubsteps,Is.EqualTo(stage==CoupledTickStage.Final?2u:1u));
                Assert.That(b.Status,Is.EqualTo(3u));Assert.That(b.Fault,Is.EqualTo((uint)CoupledTickFault.Dependency));
                Assert.That(b.CompletedTick,Is.Zero);Assert.That(b.Generation,Is.EqualTo(1u));
                var failure=Fixture.Read<uint>(f.Tick.FirstFailure,5);
                Assert.That(failure[1],Is.EqualTo(1u));Assert.That(failure[4],Is.EqualTo((uint)stage));
            }
        }

        [Test] public void SuccessfulTicksPublishMotionCacheInventoryAndCompletionTogether()
        {
            using(var f=new Fixture())
            {
                var first=f.Tick.RecordBegin(f.Commands,1,2);f.Mutate(5,9);f.Stages(2);f.Tick.RecordEnd(f.Commands);
                var second=f.Tick.RecordBegin(f.Commands,2,1);f.Mutate(6,8);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                f.Execute();
                var header=Fixture.Read<CoupledTickHeader>(f.State.CommittedHeader,1)[0];
                Assert.That(header.CompletedTick,Is.EqualTo(2ul));Assert.That(header.Generation,Is.EqualTo(3u));
                Assert.That(header.CacheGeneration,Is.EqualTo(3u));
                Assert.That(header.ActiveCacheCount,Is.Zero);
                var endpoint=Fixture.Read<BodyState>(f.State.CommittedEndpoints,2)[0];
                var grain=Fixture.Read<ProofGrain>(f.State.CommittedGrains,1)[0];
                Assert.That(endpoint.Center.x,Is.EqualTo(6));Assert.That(grain.Center,Is.EqualTo(endpoint.Center));
                Assert.That(grain.Velocity,Is.EqualTo(endpoint.Velocity));Assert.That(grain.Identity,Is.EqualTo(7u));
                Assert.That(grain.Material,Is.EqualTo(1u));Assert.That(grain.Flags,Is.EqualTo(1u));
                Assert.That(Fixture.Read<uint>(f.State.CommittedInventoryWords,2),Is.EqualTo(new uint[]{8,99}));
                Assert.That(Fixture.Read<uint>(f.State.CommittedCargoFacts,3),Is.EqualTo(new uint[]{1,0,1}));
                Assert.That(Fixture.Read<CoupledCachePoint>(f.State.CommittedCache,1)[0].ImpulseNormal.x,Is.EqualTo(6));
                Assert.That(f.Completion(first).Generation,Is.EqualTo(2u));
                Assert.That(f.Completion(second).Generation,Is.EqualTo(3u));
                f.Tick.Retire(first,f.Completion(first));f.Tick.Retire(second,f.Completion(second));
            }
        }

        [Test] public void LaterFailureRetainsEarlierCommittedTickAndItsFuelCacheGeneration()
        {
            using(var f=new Fixture())
            {
                var first=f.Tick.RecordBegin(f.Commands,1,1);f.Mutate(5,9);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                var second=f.Tick.RecordBegin(f.Commands,2,1);f.Mutate(6,8);f.Stages(1,CoupledTickStage.Position);f.Tick.RecordEnd(f.Commands);
                var third=f.Tick.RecordBegin(f.Commands,3,1);f.Mutate(7,7);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                f.Execute();
                var header=Fixture.Read<CoupledTickHeader>(f.State.CommittedHeader,1)[0];
                Assert.That(header.CompletedTick,Is.EqualTo(1ul));Assert.That(header.Generation,Is.EqualTo(2u));
                Assert.That(header.CacheGeneration,Is.EqualTo(2u));
                Assert.That(Fixture.Read<BodyState>(f.State.CommittedEndpoints,2)[0].Center.x,Is.EqualTo(5));
                Assert.That(Fixture.Read<BodyState>(f.State.WorkingEndpoints,2)[0].Center.x,Is.EqualTo(5));
                Assert.That(Fixture.Read<uint>(f.State.CommittedInventoryWords,2)[0],Is.EqualTo(9u));
                Assert.That(Fixture.Read<CoupledCachePoint>(f.State.CommittedCache,1)[0].ImpulseNormal.x,Is.EqualTo(5));
                Assert.That(f.Completion(first).Status,Is.EqualTo(1u));
                Assert.That(f.Completion(second).Status,Is.EqualTo(2u));Assert.That(f.Completion(third).Status,Is.EqualTo(3u));
            }
        }

        [Test] public void IdentityTerrainAndGrainMaterialChangesAreRejectedDespiteDeclaredPasses()
        {
            using(var f=new Fixture())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);f.Mutate(5,9);
                var bindings=(CoupledEndpointBinding[])f.Initial.Bindings.Clone();bindings[0].SlotGeneration++;
                f.Write(f.State.WorkingBindings,bindings);
                f.Write(f.State.WorkingTerrainRevisions,new uint[]{2,2});
                var grains=(ProofGrain[])f.Initial.Grains.Clone();grains[0].Material=2;f.Write(f.State.WorkingGrains,grains);
                f.Stages(1);f.Tick.RecordEnd(f.Commands);f.Execute();f.AssertInitial();
                Assert.That(f.Completion(ticket).Fault,Is.EqualTo((uint)CoupledTickFault.ImmutableState));
            }
        }

        [Test] public void NonfiniteWorkingEndpointCannotPublish()
        {
            using(var f=new Fixture())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);f.Mutate(float.NaN,9);
                f.Stages(1);f.Tick.RecordEnd(f.Commands);f.Execute();f.AssertInitial();
                Assert.That(f.Completion(ticket).Fault,Is.EqualTo((uint)CoupledTickFault.InvalidState));
            }
        }

        [Test] public void MissingStagesCannotPublishEvenWithoutAnotherFault()
        {
            using(var f=new Fixture())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);f.Mutate(5,9);f.Tick.RecordEnd(f.Commands);
                f.Execute();f.AssertInitial();
                Assert.That(f.Completion(ticket).Fault,Is.EqualTo((uint)CoupledTickFault.StageOrder));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonfiniteProvisionalCacheCannotPublish(bool anchor)
        {
            using(var f=new Fixture())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);f.Mutate(5,9);
                var point=f.Initial.Cache[0];
                if(anchor)point.LocalAnchors.x=float.PositiveInfinity;
                else point.ImpulseNormal.x=float.NaN;
                f.Write(f.State.WorkingCache,new[]{point});
                f.Stages(1);f.Tick.RecordEnd(f.Commands);f.Execute();f.AssertInitial();
                Assert.That(f.Completion(ticket).Fault,Is.EqualTo((uint)CoupledTickFault.InvalidState));
            }
        }

        [Test] public void TickStagesCannotBeRecordedIntoAnotherCommandBuffer()
        {
            using(var f=new Fixture())
            using(var other=new CommandBuffer())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);
                Assert.Throws<InvalidOperationException>(()=>f.Tick.RecordSubstep(other));
                Assert.Throws<InvalidOperationException>(()=>f.Tick.RecordEnd(other));
                f.Stages(1);f.Tick.RecordEnd(f.Commands);f.Execute();
                Assert.That(f.Completion(ticket).Status,Is.EqualTo(1u));
            }
        }

        [Test] public void CorruptedCompletionCannotReleaseItsSlot()
        {
            using(var f=new Fixture())
            {
                var ticket=f.Tick.RecordBegin(f.Commands,1,1);
                f.Stages(1);f.Tick.RecordEnd(f.Commands);f.Execute();
                var completion=f.Completion(ticket);var corrupt=completion;
                corrupt.IdentityRevision++;
                Assert.Throws<InvalidOperationException>(()=>f.Tick.Retire(ticket,corrupt));
                corrupt=completion;corrupt.PassedSubsteps=0;
                Assert.Throws<InvalidOperationException>(()=>f.Tick.Retire(ticket,corrupt));
                f.Tick.Retire(ticket,completion);
            }
        }

        [Test] public void CompletionSlotsRequireMatchingCompletedTicketBeforeReuse()
        {
            using(var f=new Fixture())
            {
                CoupledTickTicket first=default;
                for(ulong tick=1;tick<=CoupledTickTransaction.CompletionCapacity;tick++)
                {
                    var ticket=f.Tick.RecordBegin(f.Commands,tick,1);if(tick==1)first=ticket;
                    f.Stages(1);f.Tick.RecordEnd(f.Commands);
                }
                Assert.Throws<InvalidOperationException>(()=>f.Tick.RecordBegin(f.Commands,17,1));
                Assert.Throws<InvalidOperationException>(()=>f.Tick.Retire(first,default));
                f.Execute();var completed=f.Completion(first);f.Tick.Retire(first,completed);
                var next=f.Tick.RecordBegin(f.Commands,17,1);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                Assert.That(next.Slot,Is.EqualTo(first.Slot));
                Assert.Throws<InvalidOperationException>(()=>f.Tick.Retire(first,completed));
            }
        }

        [Test] public void TickIdentitySurvivesLowWordWrap()
        {
            using(var f=new Fixture(uint.MaxValue))
            {
                ulong next=(ulong)uint.MaxValue+1;
                var ticket=f.Tick.RecordBegin(f.Commands,next,1);f.Stages(1);f.Tick.RecordEnd(f.Commands);
                f.Execute();var completed=f.Completion(ticket);
                Assert.That(completed.AttemptedTick,Is.EqualTo(next));
                Assert.That(completed.ExpectedPreviousTick,Is.EqualTo((ulong)uint.MaxValue));
                Assert.That(completed.CompletedTick,Is.EqualTo(next));Assert.That(completed.Status,Is.EqualTo(1u));
            }
        }

        [Test] public void StateRejectsDuplicateTransactionOwnerWithoutLeakingArenaSlices()
        {
            using(var f=new Fixture())
            {
                long used=f.Arena.UsedBytes;
                Assert.Throws<InvalidOperationException>(()=>new CoupledTickTransaction(f.State));
                Assert.That(f.Arena.UsedBytes,Is.EqualTo(used));
            }
        }
    }
}
