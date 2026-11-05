using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Debris.Simulation.Playground;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Presentation.Tests
{
    public sealed class PlaygroundRunStateTests
    {
        sealed class Fixture : IDisposable
        {
            readonly GameObject host=new GameObject("Playground run-state regression");
            public readonly PlaygroundEditTransaction Editor=new PlaygroundEditTransaction();
            readonly PhysicsPlaygroundBootstrap controller;
            const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
            public Fixture()
            {
                controller=host.AddComponent<PhysicsPlaygroundBootstrap>();
                typeof(PhysicsPlaygroundBootstrap).GetField("editor",Private).SetValue(controller,Editor);
            }
            public bool Running
            {
                get=>(bool)typeof(PhysicsPlaygroundBootstrap).GetField("running",Private).GetValue(controller);
                set=>typeof(PhysicsPlaygroundBootstrap).GetField("running",Private).SetValue(controller,value);
            }
            public void Toggle()=>typeof(PhysicsPlaygroundBootstrap).GetMethod("ToggleRun",Private).Invoke(controller,null);
            public Task<bool> Edit(Func<PlaygroundWorldDefinition,PlaygroundWorldDefinition> change,bool resume=true)
                =>(Task<bool>)typeof(PhysicsPlaygroundBootstrap).GetMethod("Mutate",Private).Invoke(controller,new object[]{"Tool action",change,resume});
            public void Dispose(){UnityEngine.Object.DestroyImmediate(host);Editor.Dispose();}
        }
        static IEnumerator Await(Task task)
        {
            while(!task.IsCompleted)yield return null;
            if(task.IsFaulted)throw task.Exception.GetBaseException();
            Assert.That(task.IsCanceled,Is.False);
        }
        // Empty worlds allocate no solver buffers and dispatch no simulation.
        [UnityTest,Timeout(30000)] public IEnumerator SuccessfulToolEditKeepsRunning()
        {
            using(var f=new Fixture())
            {
                f.Running=true;var edit=f.Edit(w=>{w.Name="Tool applied";return w;});yield return Await(edit);
                Assert.That(edit.Result,Is.True,f.Editor.Status);Assert.That(f.Running,Is.True);
                Assert.That(f.Editor.Recovery.Name,Is.EqualTo("Tool applied"));
            }
        }
        [UnityTest,Timeout(30000)] public IEnumerator RefusedPlacementDoesNotPauseRetainedWorld()
        {
            using(var f=new Fixture())
            {
                var initial=f.Editor.ReplaceAsync("Initial",new PlaygroundWorldDefinition());yield return Await(initial);
                var owner=f.Editor.Current;f.Running=true;
                var edit=f.Edit(w=>throw new InvalidOperationException("Placement blocked"));yield return Await(edit);
                Assert.That(edit.Result,Is.False);Assert.That(f.Running,Is.True);Assert.That(f.Editor.Current,Is.SameAs(owner));
            }
        }
        [UnityTest,Timeout(30000)] public IEnumerator ToolEditKeepsPausedWorldPaused()
        {
            using(var f=new Fixture())
            {
                f.Running=false;var edit=f.Edit(w=>w);yield return Await(edit);
                Assert.That(edit.Result,Is.True,f.Editor.Status);Assert.That(f.Running,Is.False);
            }
        }
        [UnityTest,Timeout(30000)] public IEnumerator PauseChosenDuringEditIsNotOverwrittenByCompletion()
        {
            using(var f=new Fixture())
            {
                f.Running=true;f.Editor.Freeze+=w=>f.Toggle();
                var edit=f.Edit(w=>w);yield return Await(edit);
                Assert.That(edit.Result,Is.True,f.Editor.Status);Assert.That(f.Running,Is.False);
            }
        }
        [UnityTest,Timeout(30000)] public IEnumerator ExplicitStopOperationStillPauses()
        {
            using(var f=new Fixture())
            {
                f.Running=true;var edit=f.Edit(w=>w,false);yield return Await(edit);
                Assert.That(edit.Result,Is.True,f.Editor.Status);Assert.That(f.Running,Is.False);
            }
        }
    }
}
