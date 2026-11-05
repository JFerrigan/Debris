using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debris.Simulation.CoupledContacts;

namespace Debris.Simulation.Playground
{
    public enum PlaygroundShape { Grain, Rectangle }
    public enum PlaygroundPreset { Empty, HeavyBlock, OffCenter, Stack, PackedPocket, FreePair }

    [Serializable]
    public sealed class PlaygroundObject
    {
        public string Id=Guid.NewGuid().ToString("N");
        public PlaygroundShape Shape;
        public uint GrainIdentity;
        public Vector2 Center,Velocity,Half=Vector2.one*.5f;
        public float Angle,Spin,Density=1;
        public bool Anchored,Wall;
        public float Mass=>Density*4*Half.x*Half.y;
        public float Inertia=>Mass*(Half.x*Half.x+Half.y*Half.y)/3;
        public PlaygroundObject Copy()=>(PlaygroundObject)MemberwiseClone();
    }

    [Serializable]
    public sealed class PlaygroundWorldDefinition
    {
        public const int GrainLimit=256,BodyLimit=16;
        public const float PlacementGap=.002f;
        public int Version=1;
        public string Name="Workbench";
        public uint NextGrainIdentity=1;
        public Vector2 Gravity=new Vector2(0,-9.81f);
        public bool GravityEnabled;
        public CoupledSolveProfile Profile=CoupledSolveProfile.C1;
        public List<PlaygroundObject> Objects=new List<PlaygroundObject>();
        public PlaygroundWorldDefinition Copy()=>new PlaygroundWorldDefinition{
            Version=Version,Name=Name,NextGrainIdentity=NextGrainIdentity,Gravity=Gravity,
            GravityEnabled=GravityEnabled,Profile=Profile,Objects=Objects.Select(o=>o.Copy()).ToList()};
        public PlaygroundObject Find(string id)=>Objects.Find(o=>o.Id==id);
        public void Validate()
        {
            if(Version!=1||Objects==null||!Finite(Gravity)||Gravity.magnitude>50||
                Profile<CoupledSolveProfile.C1||Profile>CoupledSolveProfile.C3)
                throw new ArgumentException("Unsupported playground version or settings");
            if(Objects.Exists(o=>o==null))throw new ArgumentException("Missing playground object");
            if(Objects.Count(o=>o.Shape==PlaygroundShape.Grain)>GrainLimit||
               Objects.Count(o=>o.Shape==PlaygroundShape.Rectangle)>BodyLimit)
                throw new ArgumentException("Playground capacity is 256 grains and 16 rectangles (including walls)");
            var ids=new HashSet<string>();var grains=new HashSet<uint>();uint maximum=0;
            foreach(var o in Objects)
            {
                if(!Guid.TryParseExact(o.Id,"N",out var guid)||guid==Guid.Empty||o.Id!=guid.ToString("N")||!ids.Add(o.Id)||
                   !Finite(o.Center)||!Finite(o.Velocity)||!Finite(o.Half)||!Finite(o.Angle)||!Finite(o.Spin)||
                   !Finite(o.Density)||o.Density<.01f||o.Density>10000||o.Half.x<.125f||o.Half.y<.125f||
                   o.Half.x>128||o.Half.y>128||!Finite(o.Mass)||!Finite(o.Inertia)||
                   (o.Anchored&&(!o.Velocity.Equals(Vector2.zero)||o.Spin!=0))||
                   (o.Shape!=PlaygroundShape.Grain&&o.Shape!=PlaygroundShape.Rectangle))
                    throw new ArgumentException("Invalid object identity, geometry, density or motion");
                if(o.Shape==PlaygroundShape.Grain)
                {
                    if(!o.Half.Equals(Vector2.one*.5f)||o.GrainIdentity==0||!grains.Add(o.GrainIdentity)||o.Wall)
                        throw new ArgumentException("Grains must be unique unit squares");
                    maximum=Math.Max(maximum,o.GrainIdentity);
                }
                else if(o.GrainIdentity!=0)throw new ArgumentException("A rectangle cannot own a grain identity");
                if(o.Wall&&!o.Anchored)throw new ArgumentException("Arena walls must remain explicitly anchored");
                Vector2 extent=Extent(o);
                if(o.Center.x-extent.x< -511.5f||o.Center.y-extent.y< -511.5f||
                   o.Center.x+extent.x>=511.5f||o.Center.y+extent.y>=511.5f)
                    throw new ArgumentException("Whole objects must fit inside the supported page");
                if(o.Velocity.magnitude+Mathf.Abs(o.Spin)*o.Half.magnitude>120)
                    throw new ArgumentException("Initial surface speed exceeds the sixteen-substep limit");
            }
            if(NextGrainIdentity==0||NextGrainIdentity<=maximum)throw new ArgumentException("Invalid next grain identity");
            for(int a=0;a<Objects.Count;a++)for(int b=a+1;b<Objects.Count;b++)
            {
                var x=Objects[a];var y=Objects[b];
                if(x.Anchored&&y.Anchored)continue;
                float bound=x.Shape==PlaygroundShape.Grain&&y.Shape==PlaygroundShape.Grain?.01f:.001f;
                if(Gap(x,y)< -bound)throw new ArgumentException("Objects overlap beyond the physical acceptance bound");
            }
        }
        public bool CanPlace(PlaygroundObject value,string ignoreId=null)
        {
            foreach(var o in Objects)if(o.Id!=ignoreId&&Gap(value,o)<PlacementGap)return false;
            return true;
        }
        public bool TryAdd(PlaygroundObject value,out string reason)
        {
            reason=null;var proposed=value.Copy();
            if(proposed.Shape==PlaygroundShape.Grain)
            {
                if(NextGrainIdentity==uint.MaxValue){reason="Grain identity sequence exhausted";return false;}
                proposed.GrainIdentity=NextGrainIdentity;
            }
            if(!CanPlace(proposed)){reason="Placement blocked";return false;}
            Objects.Add(proposed);uint old=NextGrainIdentity;
            if(proposed.Shape==PlaygroundShape.Grain)NextGrainIdentity++;
            try { Validate();return true; }
            catch(Exception e){Objects.RemoveAt(Objects.Count-1);NextGrainIdentity=old;reason=e.Message;return false;}
        }
        public static Vector2 Rotate(Vector2 v,float angle)
        {float c=Mathf.Cos(angle),s=Mathf.Sin(angle);return new Vector2(c*v.x-s*v.y,s*v.x+c*v.y);}
        public static Vector2 Extent(PlaygroundObject o)
        {
            float c=Mathf.Abs(Mathf.Cos(o.Angle)),s=Mathf.Abs(Mathf.Sin(o.Angle));
            return new Vector2(c*o.Half.x+s*o.Half.y,s*o.Half.x+c*o.Half.y);
        }
        // Independent command-time SAT. Runtime contact authority remains GPU.
        public static float Gap(PlaygroundObject a,PlaygroundObject b)
        {
            Vector2 ax=Rotate(Vector2.right,a.Angle),ay=Rotate(Vector2.up,a.Angle),
                bx=Rotate(Vector2.right,b.Angle),by=Rotate(Vector2.up,b.Angle),delta=b.Center-a.Center;
            float best=float.NegativeInfinity;
            foreach(var n in new[]{ax,ay,bx,by})
            {
                float ra=Mathf.Abs(Vector2.Dot(n,ax))*a.Half.x+Mathf.Abs(Vector2.Dot(n,ay))*a.Half.y;
                float rb=Mathf.Abs(Vector2.Dot(n,bx))*b.Half.x+Mathf.Abs(Vector2.Dot(n,by))*b.Half.y;
                best=Mathf.Max(best,Mathf.Abs(Vector2.Dot(delta,n))-ra-rb);
            }
            return best;
        }
        public static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static bool Finite(Vector2 v)=>Finite(v.x)&&Finite(v.y);

        public static PlaygroundWorldDefinition Preset(PlaygroundPreset preset)
        {
            var world=new PlaygroundWorldDefinition{Name=preset.ToString()};
            void Box(Vector2 center,Vector2 size,float density=1,bool anchored=false,bool wall=false)
            {world.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Center=center,Half=size*.5f,Density=density,Anchored=anchored,Wall=wall});}
            void Grain(float x,float y)=>world.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Grain,
                Center=new Vector2(x,y),GrainIdentity=world.NextGrainIdentity++});
            if(preset!=PlaygroundPreset.FreePair)
            {
                Box(new Vector2(0,-22),new Vector2(82,2),1,true,true);
                Box(new Vector2(0,22),new Vector2(82,2),1,true,true);
                Box(new Vector2(-42,0),new Vector2(2,42),1,true,true);
                Box(new Vector2(42,0),new Vector2(2,42),1,true,true);
            }
            switch(preset)
            {
                case PlaygroundPreset.Empty:break;
                case PlaygroundPreset.HeavyBlock:
                    Box(new Vector2(-14,0),new Vector2(8,6),5);
                    for(int y=0;y<4;y++)for(int x=0;x<6;x++)Grain(2+x*1.2f,-2+y*1.2f);
                    break;
                case PlaygroundPreset.OffCenter:
                    Box(Vector2.zero,new Vector2(8,4),2);Grain(-15,1.5f);world.Objects.Last().Velocity=new Vector2(8,0);break;
                case PlaygroundPreset.Stack:
                    world.GravityEnabled=true;for(int i=0;i<4;i++)Box(new Vector2(0,-18+i*4.1f),new Vector2(6,4));break;
                case PlaygroundPreset.PackedPocket:
                    Box(new Vector2(-7,0),new Vector2(3,12),10);
                    for(int y=0;y<8;y++)for(int x=0;x<8;x++)Grain(x*1.02f,-4+y*1.02f);break;
                case PlaygroundPreset.FreePair:
                    Box(new Vector2(-8,0),new Vector2(4,4));Box(new Vector2(8,0),new Vector2(6,4),3);break;
                default:throw new ArgumentOutOfRangeException(nameof(preset));
            }
            world.Validate();return world;
        }
    }
}
