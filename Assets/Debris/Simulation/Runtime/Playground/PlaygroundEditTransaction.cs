using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Debris.Simulation.Playground
{
    [Serializable]
    public sealed class PlaygroundEditFact
    {
        public string Utc,Command;
        public uint Revision;
        public int BeforeCount,AfterCount;
        public double MassChange,AngularMomentumChange;
        public Vector2 MomentumChange;
    }

    // Owns the single live arena. Rejected proposals preserve it; failed GPU
    // reconstruction recreates the exact accepted CPU recovery snapshot cold.
    public sealed class PlaygroundEditTransaction : IDisposable
    {
        public CoupledPlaygroundSession Current { get; private set; }
        public PlaygroundWorldDefinition Recovery { get; private set; }
        public uint Revision { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; }
        readonly List<PlaygroundEditFact> history=new List<PlaygroundEditFact>(128);
        public IReadOnlyList<PlaygroundEditFact> History=>history;
        public event Action<PlaygroundWorldDefinition> Freeze;
        public event Action Rebound;
        public PlaygroundDebugLog Log { get; set; }
        ulong editId;
        uint generation;
        bool disposed,requiresRestart;
        uint NewGeneration()=>generation==uint.MaxValue?throw new InvalidOperationException("World generation exhausted"):++generation;

        public async Task<bool> ReplaceAsync(string command,PlaygroundWorldDefinition replacement)
            =>await ApplyAsync(command,w=>replacement.Copy());
        public async Task<bool> ApplyAsync(string command,Func<PlaygroundWorldDefinition,PlaygroundWorldDefinition> change)
        {
            if(disposed||Busy){Log?.Write("edit.ignored",command+": disposed="+disposed+", busy="+Busy);return false;}
            if(requiresRestart){Status="Restart the playground before editing: GPU owner cleanup or recovery failed";Log?.Write("edit.ignored",Status);return false;}
            Busy=true;var watch=System.Diagnostics.Stopwatch.StartNew();ulong id=++editId;
            void Trace(string stage,string detail=null)=>Log?.Write("edit."+stage,detail,"edit="+id+" command="+command+" revision="+Revision+" generation="+(Current?.Generation??0)+" elapsedMs="+watch.Elapsed.TotalMilliseconds);
            Trace("begin");
            CoupledPlaygroundSession candidate=null;bool retired=false,published=false;
            try
            {
                Trace("drain");await DrainAsync();Trace("snapshot");
                var before=Current==null?(Recovery?.Copy()??new PlaygroundWorldDefinition()):await Current.SnapshotAsync();
                Recovery=before.Copy();var proposed=change(before.Copy());
                if(disposed)return false;
                if(proposed==null)throw new ArgumentException("An edit must supply a world");
                Trace("proposal", "objects="+proposed.Objects.Count);PlaygroundBudget.Admit(proposed);
                uint nextRevision=checked(Revision+1),nextGeneration=NewGeneration();
                var acceptedCopy=proposed.Copy();var fact=MakeFact(command,before,proposed,nextRevision);
                Trace("freeze");Freeze?.Invoke(before);
                if(disposed)return false;
                Trace("retire");try { Current?.Dispose(); }
                catch(Exception release)
                {
                    Trace("retire_failed",release.ToString());Current=null;requiresRestart=true;Status=command+" stopped: previous GPU owner could not retire: "+release.Message;
                    return false; // Do not allocate another arena after uncertain cleanup.
                }
                Current=null;retired=true;
                Trace("construct","nextGeneration="+nextGeneration);candidate=new CoupledPlaygroundSession(proposed,nextGeneration){Log=Log};
                Trace("validate_geometry");await candidate.ValidateInitialAsync();
                if(disposed){candidate.Dispose();return false;}
                Current=candidate;candidate=null;published=true;Revision=nextRevision;Recovery=acceptedCopy;
                if(history.Count==128)history.RemoveAt(0);history.Add(fact);
                Trace("published",JsonUtility.ToJson(fact));Status=command+" applied";NotifyRebound();return true;
            }
            catch(Exception e)
            {
                Trace("exception",e.ToString());
                if(published){Status=command+" applied; post-publication error: "+e.Message;return true;}
                Status=command+" refused: "+e.Message;
                try { candidate?.Dispose(); }
                catch(Exception release){Trace("cleanup_failed",release.ToString());requiresRestart=true;Status+=". Candidate cleanup failed: "+release.Message;return false;}
                if(retired&&!disposed)
                {
                    Trace("recover");try { Current=new CoupledPlaygroundSession(Recovery,NewGeneration()){Log=Log};NotifyRebound(); }
                    catch(Exception restore){Trace("recovery_failed",restore.ToString());requiresRestart=true;Current?.Dispose();Current=null;Status+=". Recovery could not allocate: "+restore.Message;}
                }
                return false;
            }
            finally { Busy=false;Trace("end",Status); }
        }
        public async Task<PlaygroundWorldDefinition> CaptureAsync()
        {
            if(disposed||Busy)throw new InvalidOperationException("World editor is busy");Busy=true;
            try
            {
                await DrainAsync();var world=Current==null?Recovery?.Copy():await Current.SnapshotAsync();
                return world??new PlaygroundWorldDefinition();
            }
            finally { Busy=false; }
        }
        async Task DrainAsync()
        {
            while(!disposed&&Current!=null&&Current.Pending){Current.Poll();await Task.Yield();}
            if(disposed)throw new ObjectDisposedException(nameof(PlaygroundEditTransaction));
        }
        void NotifyRebound()
        {
            foreach(var callback in Rebound?.GetInvocationList()??Array.Empty<Delegate>())
            {
                try { ((Action)callback)(); }
                catch(Exception e){Log?.Write("edit.view_exception",e.ToString());Status+="; view notification failed: "+e.Message;}
            }
        }
        static PlaygroundEditFact MakeFact(string command,PlaygroundWorldDefinition before,PlaygroundWorldDefinition after,uint revision)
        {
            double mass=0,angular=0;Vector2 momentum=Vector2.zero;
            void Sum(PlaygroundWorldDefinition world,int sign)
            {
                foreach(var o in world.Objects)
                {
                    mass+=sign*o.Mass;
                    if(o.Anchored)continue;
                    var p=o.Mass*o.Velocity;momentum+=sign*p;
                    angular+=sign*((double)o.Center.x*p.y-(double)o.Center.y*p.x+o.Inertia*o.Spin);
                }
            }
            Sum(before,-1);Sum(after,1);
            return new PlaygroundEditFact{Utc=DateTime.UtcNow.ToString("O"),Command=command,Revision=revision,
                BeforeCount=before.Objects.Count,AfterCount=after.Objects.Count,MassChange=mass,MomentumChange=momentum,AngularMomentumChange=angular};
        }
        public void Dispose(){if(disposed)return;disposed=true;Current?.Dispose();Current=null;}
    }
}
