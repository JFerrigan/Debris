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
        public static void Validate(MatterSnapshot state)
        {
            long count=state.Cells.Length;int width=state.Side*state.ChunkSize;
            foreach(var chunk in state.Fields)foreach(uint m in chunk)if(m!=0)count++;
            if(count!=state.Counters[1])throw new InvalidOperationException("Material accounting mismatch.");
            var identities=new HashSet<uint>();var bins=new Dictionary<Vector2Int,LooseCell>();
            foreach(var cell in state.Cells)
            {
                if(cell.Material==0||!identities.Add(cell.Identity))throw new InvalidOperationException("Invalid/duplicated cell identity.");
                var bin=Vector2Int.FloorToInt(cell.Position);
                if(bins.ContainsKey(bin))throw new InvalidOperationException("Overlapping occupancy bucket.");
                bins.Add(bin,cell);
            }
            foreach(var cell in state.Cells)
            {
                var bin=Vector2Int.FloorToInt(cell.Position);
                for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
                {
                    var p=bin+new Vector2Int(x,y);
                    if(bins.TryGetValue(p,out var other)&&other.Identity!=cell.Identity&&Mathf.Abs(other.Position.x-cell.Position.x)<.9999f&&Mathf.Abs(other.Position.y-cell.Position.y)<.9999f)throw new InvalidOperationException("Loose cells geometrically overlap.");
                    int lx=p.x-state.OriginX,ly=p.y-state.OriginY;
                    if(lx<0||ly<0||lx>=width||ly>=width)continue;
                    uint fixedMaterial=state.Fields[(ly/state.ChunkSize)*state.Side+lx/state.ChunkSize][(ly%state.ChunkSize)*state.ChunkSize+lx%state.ChunkSize];
                    if(fixedMaterial>0&&Mathf.Abs(p.x-cell.Position.x)<.9999f&&Mathf.Abs(p.y-cell.Position.y)<.9999f)throw new InvalidOperationException("Loose/fixed matter overlaps.");
                }
            }
        }
    }
}
