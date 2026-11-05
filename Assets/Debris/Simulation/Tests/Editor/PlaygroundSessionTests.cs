using System;
using System.Collections;
using System.Threading.Tasks;
using Debris.Simulation.Playground;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class PlaygroundSessionTests
    {
        static PlaygroundWorldDefinition OneBody()
        {
            Assume.That(SystemInfo.supportsComputeShaders&&SystemInfo.supportsAsyncGPUReadback,Is.True);
            var world=new PlaygroundWorldDefinition();
            world.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Half=Vector2.one,Density=2});return world;
        }
        static IEnumerator Await(Task task)
        {
            while(!task.IsCompleted)yield return null;
            if(task.IsFaulted)throw task.Exception.GetBaseException();Assert.That(task.IsCanceled,Is.False);
        }
        [UnityTest,Timeout(600000)] public IEnumerator OffCenterMouseForceUsesMassInertiaAndAcceptedMotion()
        {
            var world=OneBody();var id=world.Objects[0].Id;
            using(var session=new CoupledPlaygroundSession(world,1))
            {
                Assert.That(session.BufferBytes,Is.EqualTo(PlaygroundBudget.RequiredBytes(1,0)));
                var admitted=session.ValidateInitialAsync();yield return Await(admitted);
                Assert.That(session.Submit(new PlaygroundInput{WorldGeneration=0,CommandId=1,MaximumForce=100}),Is.False,"Old-world command must not advance the new owner");
                Assert.That(session.Submit(new PlaygroundInput{WorldGeneration=session.Generation,CommandId=1,SelectedId=id,Pull=true,
                    LocalAnchor=Vector2.up,Cursor=Vector2.one,MaximumForce=100}),Is.True);
                while(session.Pending){session.Poll();yield return null;}
                Assert.That(session.Faulted,Is.False,session.Fault);Assert.That(session.AcceptedTick,Is.EqualTo(1ul));
                var snapshot=session.SnapshotAsync();yield return Await(snapshot);var body=snapshot.Result.Objects[0];
                Assert.That(body.Velocity.x,Is.EqualTo(100f/(8*60)).Within(2e-5));
                Assert.That(body.Spin,Is.EqualTo(-100f/((16f/3)*60)).Within(2e-5));
                Assert.That(session.Diagnostics[0],Is.Zero);
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator GravityAndAnchorPolicyShareTheForceAccounting()
        {
            var world=OneBody();world.GravityEnabled=true;world.Gravity=new Vector2(0,-6);
            world.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Center=new Vector2(8,0),Anchored=true});
            string anchor=world.Objects[1].Id;
            using(var session=new CoupledPlaygroundSession(world,7))
            {
                var pick=session.PickAsync(new Vector2(8,0));yield return Await(pick);
                Assert.That(pick.Result.Id,Is.EqualTo(anchor));Assert.That(pick.Result.Generation,Is.EqualTo(7u));
                Assert.That(pick.Result.Anchored,Is.True);
                var miss=session.PickAsync(new Vector2(20,20));yield return Await(miss);
                Assert.That(miss.Result.Id,Is.Null);Assert.That(miss.Result.Generation,Is.EqualTo(7u));
                Assert.That(session.Submit(new PlaygroundInput{WorldGeneration=session.Generation,CommandId=1,SelectedId=anchor,Drive=Vector2.right,MaximumForce=500}),Is.True);
                while(session.Pending){session.Poll();yield return null;}
                Assert.That(session.Faulted,Is.False,session.Fault);
                var snapshot=session.SnapshotAsync();yield return Await(snapshot);
                Assert.That(snapshot.Result.Objects[0].Velocity.y,Is.EqualTo(-.1f).Within(2e-5));
                Assert.That(snapshot.Result.Find(anchor).Center,Is.EqualTo(new Vector2(8,0)));
                Assert.That(snapshot.Result.Find(anchor).Velocity,Is.EqualTo(Vector2.zero));
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator RefusedEditKeepsOwnerAndValidRebuildPreservesAcceptedState()
        {
            var world=OneBody();string id=world.Objects[0].Id;
            using(var editor=new PlaygroundEditTransaction())
            {
                var initial=editor.ReplaceAsync("Initial",world);yield return Await(initial);Assert.That(initial.Result,Is.True,editor.Status);
                var owner=editor.Current;uint revision=editor.Revision;
                var refusal=editor.ApplyAsync("Bad overlap",w=>{
                    w.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Center=Vector2.zero});return w;
                });yield return Await(refusal);
                Assert.That(refusal.Result,Is.False);Assert.That(editor.Current,Is.SameAs(owner));Assert.That(editor.Revision,Is.EqualTo(revision));
                Assert.That(owner.Submit(new PlaygroundInput{WorldGeneration=owner.Generation,CommandId=1,SelectedId=id,Drive=Vector2.right,MaximumForce=48}),Is.True);
                while(owner.Pending){owner.Poll();yield return null;}
                Assert.That(owner.Faulted,Is.False,owner.Fault);
                var before=editor.CaptureAsync();yield return Await(before);
                var add=editor.ApplyAsync("Add grain",w=>{
                    if(!w.TryAdd(new PlaygroundObject{Center=new Vector2(10,0)},out var reason))throw new InvalidOperationException(reason);return w;
                });yield return Await(add);Assert.That(add.Result,Is.True,editor.Status);
                Assert.That(editor.Current.Generation,Is.Not.EqualTo(owner.Generation));
                var after=editor.CaptureAsync();yield return Await(after);
                Assert.That(after.Result.Find(id).Center,Is.EqualTo(before.Result.Find(id).Center));
                Assert.That(after.Result.Find(id).Velocity,Is.EqualTo(before.Result.Find(id).Velocity));
                Assert.That(after.Result.Objects.Count,Is.EqualTo(2));
                var clear=editor.ApplyAsync("Clear",w=>{w.Objects.Clear();return w;});yield return Await(clear);
                Assert.That(clear.Result,Is.True,editor.Status);Assert.That(editor.Current.Count,Is.Zero);Assert.That(editor.Current.BufferBytes,Is.Zero);
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator ViewNotificationFailureCannotRollBackPublishedWorld()
        {
            using(var editor=new PlaygroundEditTransaction())
            {
                int notifications=0;
                editor.Rebound+=()=>throw new InvalidOperationException("Broken view subscriber");
                editor.Rebound+=()=>notifications++;
                var first=editor.ReplaceAsync("Initial empty",new PlaygroundWorldDefinition());yield return Await(first);
                Assert.That(first.Result,Is.True,editor.Status);Assert.That(editor.Revision,Is.EqualTo(1u));
                Assert.That(editor.Current,Is.Not.Null);Assert.That(editor.Status,Does.Contain("notification failed"));
                var owner=editor.Current;
                var second=editor.ApplyAsync("Rename",w=>{w.Name="Accepted rename";return w;});yield return Await(second);
                Assert.That(second.Result,Is.True,editor.Status);Assert.That(editor.Current,Is.Not.SameAs(owner));
                Assert.That(editor.Revision,Is.EqualTo(2u));Assert.That(editor.Recovery.Name,Is.EqualTo("Accepted rename"));
                Assert.That(editor.History.Count,Is.EqualTo(2));Assert.That(editor.Current.BufferBytes,Is.Zero);
                Assert.That(notifications,Is.EqualTo(2),"A failed subscriber cannot suppress remaining view updates");
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator FreezeFailureRetainsOwnerWithoutPublishingAnEdit()
        {
            using(var editor=new PlaygroundEditTransaction())
            {
                var first=editor.ReplaceAsync("Initial empty",new PlaygroundWorldDefinition());yield return Await(first);
                Assert.That(first.Result,Is.True,editor.Status);var owner=editor.Current;
                editor.Freeze+=w=>throw new InvalidOperationException("Frozen display unavailable");
                var failed=editor.ApplyAsync("Rename",w=>{w.Name="Refused rename";return w;});yield return Await(failed);
                Assert.That(failed.Result,Is.False);Assert.That(editor.Current,Is.SameAs(owner));
                Assert.That(editor.Revision,Is.EqualTo(1u));Assert.That(editor.History.Count,Is.EqualTo(1));
                Assert.That(editor.Status,Does.Contain("Frozen display unavailable"));
            }
        }
        [UnityTest,Timeout(600000)] public IEnumerator DisposingDuringFreezeCannotConstructANewOwner()
        {
            using(var editor=new PlaygroundEditTransaction())
            {
                var first=editor.ReplaceAsync("Initial empty",new PlaygroundWorldDefinition());yield return Await(first);
                Assert.That(first.Result,Is.True,editor.Status);
                editor.Freeze+=w=>editor.Dispose();
                var stopped=editor.ApplyAsync("Close during edit",w=>{w.Name="Must not publish";return w;});yield return Await(stopped);
                Assert.That(stopped.Result,Is.False);Assert.That(editor.Current,Is.Null);
                Assert.That(editor.Revision,Is.EqualTo(1u));Assert.That(editor.History.Count,Is.EqualTo(1));
            }
        }
    }
}
