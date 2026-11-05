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
        public readonly List<PlaygroundEditFact> History=new List<PlaygroundEditFact>();
        public event Action<PlaygroundWorldDefinition> Freeze;
        public event Action Rebound;
        uint generation;
        bool disposed;
        uint NewGeneration()=>generation==uint.MaxValue?throw new InvalidOperationException("World generation exhausted"):++generation;

        public async Task<bool> ReplaceAsync(string command,PlaygroundWorldDefinition replacement)
            =>await ApplyAsync(command,w=>replacement.Copy());
        public async Task<bool> ApplyAsync(string command,Func<PlaygroundWorldDefinition,PlaygroundWorldDefinition> change)
        {
            if(disposed||Busy)return false;Busy=true;
            CoupledPlaygroundSession candidate=null;bool retired=false;
            try
            {
                await DrainAsync();
                var before=Current==null?(Recovery?.Copy()??new PlaygroundWorldDefinition()):await Current.SnapshotAsync();
                Recovery=before.Copy();var proposed=change(before.Copy());
                if(proposed==null)throw new ArgumentException("An edit must supply a world");
                PlaygroundBudget.Admit(proposed);Freeze?.Invoke(before);
                Current?.Dispose();Current=null;retired=true;
                candidate=new CoupledPlaygroundSession(proposed,NewGeneration());
                await candidate.ValidateInitialAsync();
                if(disposed){candidate.Dispose();return false;}
                Current=candidate;candidate=null;Revision=checked(Revision+1);Recovery=proposed.Copy();
                Record(command,before,proposed);Status=command+" applied";Rebound?.Invoke();return true;
            }
            catch(Exception e)
            {
                candidate?.Dispose();Status=command+" refused: "+e.Message;
                if(retired&&!disposed)
                {
                    try { Current=new CoupledPlaygroundSession(Recovery,NewGeneration());Rebound?.Invoke(); }
                    catch(Exception restore){Current?.Dispose();Current=null;Status+=". Recovery could not allocate: "+restore.Message;}
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
        void Record(string command,PlaygroundWorldDefinition before,PlaygroundWorldDefinition after)
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
            History.Add(new PlaygroundEditFact{Utc=DateTime.UtcNow.ToString("O"),Command=command,Revision=Revision,
                BeforeCount=before.Objects.Count,AfterCount=after.Objects.Count,MassChange=mass,MomentumChange=momentum,AngularMomentumChange=angular});
            if(History.Count>128)History.RemoveAt(0);
        }
        public void Dispose(){if(disposed)return;disposed=true;Current?.Dispose();Current=null;}
    }
}
