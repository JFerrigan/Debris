using System;
using System.Collections.Generic;
using UnityEngine;
namespace Debris.Simulation
{
    internal static class FragmentValidator
    {
        static Vector2 Rotate(Vector2 p,float angle){float c=Mathf.Cos(angle),s=Mathf.Sin(angle);return new Vector2(p.x*c-p.y*s,p.x*s+p.y*c);}
        static Vector2 World(Vector2 p,Vector4 pose)=>new Vector2(pose.x,pose.y)+Rotate(p,pose.z);
        static Vector2 Local(Vector2 p,Vector4 pose)=>Rotate(p-new Vector2(pose.x,pose.y),-pose.z);
        static bool Pair(Vector2 a,float angleA,Vector2 b,float angleB)
        {
            float angle=angleA-angleB,extent=.5f*(1+Mathf.Abs(Mathf.Cos(angle))+Mathf.Abs(Mathf.Sin(angle)))-.0001f;
            var da=Rotate(b-a,-angleA);var db=Rotate(b-a,-angleB);
            return Mathf.Abs(da.x)<extent&&Mathf.Abs(da.y)<extent&&Mathf.Abs(db.x)<extent&&Mathf.Abs(db.y)<extent;
        }
        static bool InLocal(Vector2Int p)=>p.x>=-64&&p.x<64&&p.y>=-64&&p.y<64;
        public static void Validate(MatterSnapshot s)
        {
            if(s.Fragments==null||s.Fragments.Length>16)throw new InvalidOperationException("Invalid active fragment count.");
            var ids=new HashSet<string>();var worldCells=new Dictionary<Vector2Int,LooseCell>();var cargoCells=new Dictionary<Vector2Int,LooseCell>();
            foreach(var cell in s.Cells)((cell.Flags&4)!=0?cargoCells:worldCells).Add(Vector2Int.FloorToInt(cell.Position),cell);
            int width=s.Side*s.ChunkSize;
            for(int f=0;f<s.Fragments.Length;f++)
            {
                var fragment=s.Fragments[f];
                if(fragment==null||!ids.Add(fragment.Id)||fragment.Hull==null||fragment.Hull.Length!=16384)throw new InvalidOperationException("Invalid fragment identity/field.");
                new Debris.Core.StableId(fragment.Id);
                for(int axis=0;axis<4;axis++)if(!float.IsFinite(fragment.Pose[axis])||!float.IsFinite(fragment.Motion[axis]))throw new InvalidOperationException("Nonfinite fragment motion.");
                for(int cell=0;cell<fragment.Hull.Length;cell++)
                {
                    if(fragment.Hull[cell]==0)continue;
                    var center=World(new Vector2(cell%128-63.5f,cell/128-63.5f),fragment.Pose);var at=Vector2Int.FloorToInt(center);
                    if(at.x<s.OriginX||at.y<s.OriginY||at.x>=s.OriginX+width||at.y>=s.OriginY+width)throw new InvalidOperationException("Fragment outside active region.");
                    for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                    {
                        var p=at+new Vector2Int(x,y);int lx=p.x-s.OriginX,ly=p.y-s.OriginY;
                        if(lx>=0&&ly>=0&&lx<width&&ly<width&&s.Fields[(ly/s.ChunkSize)*s.Side+lx/s.ChunkSize][(ly%s.ChunkSize)*s.ChunkSize+lx%s.ChunkSize]>0&&Pair(center,fragment.Pose.z,(Vector2)p+Vector2.one*.5f,0))throw new InvalidOperationException("Fragment overlaps terrain.");
                        if(worldCells.TryGetValue(p,out var other)&&Pair(center,fragment.Pose.z,other.Position+Vector2.one*.5f,0))throw new InvalidOperationException("Fragment overlaps loose matter.");
                    }
                    if(s.ShipEnabled)
                    {
                        var pose=s.ShipPose[0];var local=Vector2Int.FloorToInt(Local(center,pose));
                        for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                        {
                            var p=local+new Vector2Int(x,y);if(!InLocal(p))continue;uint m=s.Hull[(p.y+64)*128+p.x+64];
                            if(m!=0&&(m!=uint.MaxValue||s.ShipPose[2].z==0)&&Pair(center,fragment.Pose.z,World((Vector2)p+Vector2.one*.5f,pose),pose.z))throw new InvalidOperationException("Fragment overlaps ship hull.");
                            if(cargoCells.TryGetValue(p,out var other)&&Pair(center,fragment.Pose.z,World(other.Position+Vector2.one*.5f,pose),pose.z))throw new InvalidOperationException("Fragment overlaps cargo.");
                        }
                    }
                    for(int previous=0;previous<f;previous++)
                    {
                        var other=s.Fragments[previous];var local=Vector2Int.FloorToInt(Local(center,other.Pose));
                        for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                        {
                            var p=local+new Vector2Int(x,y);if(InLocal(p)&&other.Hull[(p.y+64)*128+p.x+64]!=0&&Pair(center,fragment.Pose.z,World((Vector2)p+Vector2.one*.5f,other.Pose),other.Pose.z))throw new InvalidOperationException("Fragments overlap.");
                        }
                    }
                }
            }
        }
    }
}
