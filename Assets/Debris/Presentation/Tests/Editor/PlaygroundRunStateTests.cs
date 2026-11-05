using System;
using System.Collections;
using System.IO;
using System.Linq;
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
            public Fixture(PlaygroundDebugLog log=null)
            {
                Editor.Log=log;controller=host.AddComponent<PhysicsPlaygroundBootstrap>();
                typeof(PhysicsPlaygroundBootstrap).GetField("debugLog",Private).SetValue(controller,log);
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
        static string LogPath()=>Path.Combine(Path.GetTempPath(),"debris-debug-"+Guid.NewGuid().ToString("N"),"debug.jsonl");
        static void Cleanup(string path){if(Directory.Exists(Path.GetDirectoryName(path)))Directory.Delete(Path.GetDirectoryName(path),true);}
        [Test] public void DebugRecordsRoundTripWithSequenceAndContext()
        {
            string path=LogPath();
            try
            {
                using(var log=new PlaygroundDebugLog(path))
                {
                    log.Write("input.left_down","line one\nline two","generation=7");log.Write("run.request","focus loss");
                    var records=File.ReadAllLines(path).Select(JsonUtility.FromJson<PlaygroundDebugEvent>).ToArray();
                    Assert.That(records.Length,Is.EqualTo(2));Assert.That(records[0].Detail,Is.EqualTo("line one\nline two"));
                    Assert.That(records[0].Context,Is.EqualTo("generation=7"));Assert.That(records[1].Sequence,Is.EqualTo(2));
                    Assert.That(records[1].Session,Is.EqualTo(records[0].Session));Assert.That(log.Error,Is.Null);
                }
            }
            finally{Cleanup(path);}
        }
        [Test] public void DebugLogBoundsFilesAndRecentEvents()
        {
            string path=LogPath();
            try
            {
                using(var log=new PlaygroundDebugLog(path,1024))
                {
                    for(int i=0;i<300;i++)log.Write("click",new string('x',100));
                    Assert.That(log.ExportTail.Length,Is.EqualTo(16));Assert.That(log.ExportTail[0].Sequence,Is.EqualTo(285));
                    Assert.That(log.Recent.Length,Is.EqualTo(128));Assert.That(log.Recent[0].Sequence,Is.EqualTo(173));
                    Assert.That(new FileInfo(path).Length,Is.LessThanOrEqualTo(1024));
                    Assert.That(new FileInfo(path+".previous").Length,Is.LessThanOrEqualTo(1024));
                    Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)).Length,Is.EqualTo(2));
                }
            }
            finally{Cleanup(path);}
        }
        [UnityTest,Timeout(30000)] public IEnumerator UnwritableLogDoesNotInterruptEditsOrPause()
        {
            string path=LogPath();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"block directory");
            try
            {
                using(var log=new PlaygroundDebugLog(Path.Combine(path,"debug.jsonl")))
                using(var f=new Fixture(log))
                {
                    Assert.That(log.Error,Is.Not.Null);f.Running=true;
                    var edit=f.Edit(w=>w);yield return Await(edit);
                    Assert.That(edit.Result,Is.True);Assert.That(f.Running,Is.True);
                    Assert.That(log.Recent.Any(e=>e.Event=="edit.published"),Is.True);
                    f.Toggle();Assert.That(f.Running,Is.False);
                    Assert.That(log.Recent.Any(e=>e.Event=="run.request"&&e.Detail.Contains("before=True after=False")),Is.True);
                }
            }
            finally{Cleanup(path);}
        }
        [UnityTest,Timeout(30000)] public IEnumerator RefusedEditLogsExceptionAndKeepsRunIntent()
        {
            string path=LogPath();
            try
            {
                using(var log=new PlaygroundDebugLog(path))
                using(var f=new Fixture(log))
                {
                    var initial=f.Editor.ReplaceAsync("Initial",new PlaygroundWorldDefinition());yield return Await(initial);
                    f.Running=true;var edit=f.Edit(w=>throw new InvalidOperationException("blocked placement"));yield return Await(edit);
                    Assert.That(edit.Result,Is.False);Assert.That(f.Running,Is.True);
                    var failure=log.Recent.Single(e=>e.Event=="edit.exception");
                    Assert.That(failure.Detail,Does.Contain("InvalidOperationException: blocked placement"));
                    Assert.That(failure.Context,Does.Contain("edit=2 command=Tool action"));
                    Assert.That(log.Recent.Any(e=>e.Event=="edit.end"),Is.True);
                }
            }
            finally{Cleanup(path);}
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
