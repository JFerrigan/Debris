using System;
using System.Collections.Generic;
using Debris.Materials;
using Debris.Sites;
using UnityEngine;
namespace Debris.Simulation
{
    // Small correctness oracle only; never used as the production simulator.
    public static class CpuCutReference
    {
        public static void Apply(MatterSnapshot state,MaterialCatalog catalog,SiteCommand command)
        {
            var cells=new List<LooseCell>(state.Cells);
            int width=state.Side*state.ChunkSize;
            for(int y=Mathf.FloorToInt(command.PositionCells.y-command.RadiusCells);y<=Mathf.CeilToInt(command.PositionCells.y+command.RadiusCells);y++)
            for(int x=Mathf.FloorToInt(command.PositionCells.x-command.RadiusCells);x<=Mathf.CeilToInt(command.PositionCells.x+command.RadiusCells);x++)
            {
                int lx=x-state.OriginX,ly=y-state.OriginY;
                if(lx<0||ly<0||lx>=width||ly>=width||Vector2.Distance(new Vector2(x+.5f,y+.5f),command.PositionCells)>command.RadiusCells)continue;
                int slice=(ly/state.ChunkSize)*state.Side+lx/state.ChunkSize,index=(ly%state.ChunkSize)*state.ChunkSize+lx%state.ChunkSize;
                uint m=state.Fields[slice][index];if(m==0)continue;
                float damage=state.Damage[slice][index]+command.Strength/60;
                if(damage<catalog.DefinitionAt((ushort)m).Durability) {state.Damage[slice][index]=damage;if(state.Dirty[slice]==0){state.Dirty[slice]=1;state.Counters[3]++;}continue;}
                if(cells.Count>=state.Capacity){state.Counters[2]++;continue;}
                cells.Add(new LooseCell{Position=new Vector2(x,y),Velocity=command.Direction,Material=m,Identity=(uint)cells.Count+1});
                state.Fields[slice][index]=0;state.Damage[slice][index]=0;
                if(state.Dirty[slice]==0){state.Dirty[slice]=1;state.Counters[3]++;}
            }
            state.Cells=cells.ToArray();state.Counters[0]=(uint)cells.Count;
        }
        static bool Overlap(Vector2 a,Vector2 b,float angle)
        {
            float c=Mathf.Cos(angle),sn=Mathf.Sin(angle),extent=.5f*(1+Mathf.Abs(c)+Mathf.Abs(sn))-.0001f;
            var d=b-a;var local=new Vector2(d.x*c+d.y*sn,-d.x*sn+d.y*c);
            return Mathf.Abs(d.x)<extent&&Mathf.Abs(d.y)<extent&&Mathf.Abs(local.x)<extent&&Mathf.Abs(local.y)<extent;
        }
        public static void Validate(MatterSnapshot state)
        {
            if(state==null||state.Side<2||state.ChunkSize<8||state.Capacity<1||state.Cells==null||state.Cells.Length>state.Capacity||state.Counters==null||state.Counters.Length!=4||state.Counters[0]!=state.Cells.Length||state.Fields==null||state.Fields.Length!=state.Side*state.Side||state.Damage==null||state.Damage.Length!=state.Fields.Length||state.Dirty==null||state.Dirty.Length!=state.Fields.Length)throw new InvalidOperationException("Invalid snapshot dimensions.");
            long count=state.Cells.Length;int width=state.Side*state.ChunkSize;
            for(int i=0;i<state.Fields.Length;i++)
            {
                if(state.Fields[i].Length!=state.ChunkSize*state.ChunkSize||state.Damage[i].Length!=state.Fields[i].Length)throw new InvalidOperationException("Invalid chunk length.");
                foreach(uint m in state.Fields[i])if(m!=0)count++;
                foreach(float d in state.Damage[i])if(!float.IsFinite(d)||d<0)throw new InvalidOperationException("Invalid damage.");
            }
            if(count!=state.Counters[1])throw new InvalidOperationException("Material accounting mismatch.");
            bool ship=state.ShipEnabled;
            if(ship&&(state.Hull==null||state.Hull.Length!=16384||state.ShipPose==null||state.ShipPose.Length!=3))throw new InvalidOperationException("Missing ship collision state.");
            Vector4 pose=ship?state.ShipPose[0]:Vector4.zero;float c=Mathf.Cos(pose.z),s=Mathf.Sin(pose.z);
            Vector2 World(Vector2 p)=>new Vector2(pose.x+p.x*c-p.y*s,pose.y+p.x*s+p.y*c);
            Vector2 Local(Vector2 p){p-=new Vector2(pose.x,pose.y);return new Vector2(p.x*c+p.y*s,-p.x*s+p.y*c);}
            var identities=new HashSet<uint>();var bins=new Dictionary<Vector3Int,LooseCell>();
            foreach(var cell in state.Cells)
            {
                bool cargo=(cell.Flags&4)!=0;
                if(cell.Material==0||cell.Identity==0||!identities.Add(cell.Identity)||!float.IsFinite(cell.Position.x)||!float.IsFinite(cell.Position.y)||!float.IsFinite(cell.Velocity.x)||!float.IsFinite(cell.Velocity.y)||cargo&&!ship)throw new InvalidOperationException("Invalid/duplicated cell state.");
                var bin=new Vector3Int(Mathf.FloorToInt(cell.Position.x),Mathf.FloorToInt(cell.Position.y),cargo?1:0);
                if(bins.ContainsKey(bin))throw new InvalidOperationException("Overlapping occupancy bucket.");bins.Add(bin,cell);
                if(cargo?(bin.x<-64||bin.y<-64||cell.Position.x>63||cell.Position.y>63):(bin.x<state.OriginX||bin.y<state.OriginY||cell.Position.x>state.OriginX+width-1||cell.Position.y>state.OriginY+width-1))throw new InvalidOperationException("Cell outside active domain.");
            }
            foreach(var cell in state.Cells)
            {
                bool cargo=(cell.Flags&4)!=0;Vector2 center=cargo?World(cell.Position+Vector2.one*.5f):cell.Position+Vector2.one*.5f;
                for(int domain=0;domain<(ship?2:1);domain++)
                {
                    var position=domain==1?Local(center):center;var bin=Vector2Int.FloorToInt(position);
                    for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                    {
                        var p=bin+new Vector2Int(x,y);bool same=cargo==(domain==1);
                        if(bins.TryGetValue(new Vector3Int(p.x,p.y,domain),out var other)&&other.Identity!=cell.Identity)
                        {
                            bool overlap=same?Mathf.Abs(other.Position.x-cell.Position.x)<.9999f&&Mathf.Abs(other.Position.y-cell.Position.y)<.9999f:Overlap(domain==0?other.Position+Vector2.one*.5f:center,domain==1?World(other.Position+Vector2.one*.5f):center,pose.z);
                            if(overlap)throw new InvalidOperationException("Loose cells geometrically overlap.");
                        }
                        bool solid=false;
                        if(domain==1&&p.x>=-64&&p.x<64&&p.y>=-64&&p.y<64)
                        {uint m=state.Hull[(p.y+64)*128+p.x+64];solid=m!=0&&(m!=uint.MaxValue||state.ShipPose[2].z==0);}
                        if(domain==0)
                        {int lx=p.x-state.OriginX,ly=p.y-state.OriginY;if(lx>=0&&ly>=0&&lx<width&&ly<width)solid=state.Fields[(ly/state.ChunkSize)*state.Side+lx/state.ChunkSize][(ly%state.ChunkSize)*state.ChunkSize+lx%state.ChunkSize]!=0;}
                        if(!solid)continue;
                        bool hit=same?Mathf.Abs(p.x-cell.Position.x)<.9999f&&Mathf.Abs(p.y-cell.Position.y)<.9999f:Overlap(domain==0?(Vector2)p+Vector2.one*.5f:center,domain==1?World((Vector2)p+Vector2.one*.5f):center,pose.z);
                        if(hit)throw new InvalidOperationException("Loose/solid matter overlaps.");
                    }
                }
            }
        }
    }
}
