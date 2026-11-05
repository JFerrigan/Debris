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
        uint generation;
        bool disposed,requiresRestart;
        uint NewGeneration()=>generation==uint.MaxValue?throw new InvalidOperationException("World generation exhausted"):++generation;

        public async Task<bool> ReplaceAsync(string command,PlaygroundWorldDefinition replacement)
            =>await ApplyAsync(command,w=>replacement.Copy());
        public async Task<bool> ApplyAsync(string command,Func<PlaygroundWorldDefinition,PlaygroundWorldDefinition> change)
        {
            if(disposed||Busy)return false;
            if(requiresRestart){Status="Restart the playground before editing: GPU owner cleanup or recovery failed";return false;}
            Busy=true;
            CoupledPlaygroundSession candidate=null;bool retired=false,published=false;
            try
            {
                await DrainAsync();
                var before=Current==null?(Recovery?.Copy()??new PlaygroundWorldDefinition()):await Current.SnapshotAsync();
                Recovery=before.Copy();var proposed=change(before.Copy());
                if(disposed)return false;
                if(proposed==null)throw new ArgumentException("An edit must supply a world");
                PlaygroundBudget.Admit(proposed);
                uint nextRevision=checked(Revision+1),nextGeneration=NewGeneration();
                var acceptedCopy=proposed.Copy();var fact=MakeFact(command,before,proposed,nextRevision);
                Freeze?.Invoke(before);
                if(disposed)return false;
                try { Current?.Dispose(); }
                catch(Exception release)
                {
                    Current=null;requiresRestart=true;Status=command+" stopped: previous GPU owner could not retire: "+release.Message;
                    return false; // Do not allocate another arena after uncertain cleanup.
                }
                Current=null;retired=true;
                candidate=new CoupledPlaygroundSession(proposed,nextGeneration);
                await candidate.ValidateInitialAsync();
                if(disposed){candidate.Dispose();return false;}
                Current=candidate;candidate=null;published=true;Revision=nextRevision;Recovery=acceptedCopy;
                if(history.Count==128)history.RemoveAt(0);history.Add(fact);
                Status=command+" applied";NotifyRebound();return true;
            }
            catch(Exception e)
            {
                if(published){Status=command+" applied; post-publication error: "+e.Message;return true;}
                Status=command+" refused: "+e.Message;
                try { candidate?.Dispose(); }
                catch(Exception release){requiresRestart=true;Status+=". Candidate cleanup failed: "+release.Message;return false;}
                if(retired&&!disposed)
                {
                    try { Current=new CoupledPlaygroundSession(Recovery,NewGeneration());NotifyRebound(); }
                    catch(Exception restore){requiresRestart=true;Current?.Dispose();Current=null;Status+=". Recovery could not allocate: "+restore.Message;}
                }
                return false;
            }
            finally { Busy=false; }
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
                catch(Exception e){Status+="; view notification failed: "+e.Message;}
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
