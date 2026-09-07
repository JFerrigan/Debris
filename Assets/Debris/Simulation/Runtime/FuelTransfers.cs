using System;
using System.Collections.Generic;
using System.Linq;
using Debris.Materials;
using Debris.Ships;
using UnityEngine;
namespace Debris.Simulation
{
    public sealed class FuelTransferResult
    {
        public MatterSnapshot Matter;
        public TankInventory Tank;
        public int Count;
    }
    // Explicit bounded transfer boundary, never a per-frame CPU cell simulation.
    // A proposal owns copied mutable records. The caller uploads it before replacing the tank.
    public static class FuelTransfers
    {
        static MatterSnapshot Copy(MatterSnapshot s)=>new MatterSnapshot
        {
            Side=s.Side,ChunkSize=s.ChunkSize,Capacity=s.Capacity,OriginX=s.OriginX,OriginY=s.OriginY,Tick=s.Tick,
            Fields=s.Fields,Damage=s.Damage,Dirty=s.Dirty,Hull=s.Hull,ShipEnabled=s.ShipEnabled,
            Cells=(LooseCell[])s.Cells.Clone(),Counters=(uint[])s.Counters.Clone(),ShipPose=(Vector4[])s.ShipPose.Clone(),
            FuelCells=(FuelCellState[])s.FuelCells.Clone(),NextIdentity=s.NextIdentity
        };
        public static Vector2 World(MatterSnapshot s,Vector2 local)
        {
            var p=s.ShipPose[0];float c=Mathf.Cos(p.z),sn=Mathf.Sin(p.z);return new Vector2(p.x+local.x*c-local.y*sn,p.y+local.x*sn+local.y*c);
        }
        static string Grade(MaterialCatalog catalog,uint material)
        {
            var definition=catalog.DefinitionAt((ushort)material);string key=definition?definition.MaterialKey:"";
            if(!key.StartsWith("fuel-",StringComparison.Ordinal))return null;string grade=key.Substring(5);return TankInventory.GradeEnergy(grade)>0?grade:null;
        }
        static void CargoStats(MatterSnapshot s,MaterialCatalog catalog)
        {
            if(!s.ShipEnabled)return;float count=0,mass=0;
            foreach(var c in s.Cells)if((c.Flags&4)!=0){count++;mass+=catalog.DefinitionAt((ushort)c.Material).Density;}
            s.ShipPose[2]=new Vector4(count,mass,s.ShipPose[2].z,0);
        }
        public static FuelTransferResult Pump(MatterSnapshot original,TankInventory tank,MaterialCatalog catalog,Vector2 port,float radius,int limit=8)
        {
            if(!float.IsFinite(radius)||radius<0||!float.IsFinite(port.x)||!float.IsFinite(port.y)||limit<1||limit>32)throw new ArgumentOutOfRangeException();
            CpuCutReference.Validate(original);var s=Copy(original);var inventory=tank.Copy();
            var kept=new List<LooseCell>();var energies=s.FuelCells.ToDictionary(f=>f.Identity,f=>f.Energy);var removed=new HashSet<uint>();int moved=0;
            foreach(var cell in s.Cells)
            {
                string grade=Grade(catalog,cell.Material);var position=(cell.Flags&4)!=0?World(s,cell.Position+Vector2.one*.5f):cell.Position+Vector2.one*.5f;
                if(grade==null||moved>=limit||Vector2.Distance(position,port)>radius||!inventory.AddCell(grade,energies.TryGetValue(cell.Identity,out var energy)?energy:TankInventory.GradeEnergy(grade)))kept.Add(cell);
                else{removed.Add(cell.Identity);moved++;}
            }
            s.Cells=kept.ToArray();s.FuelCells=s.FuelCells.Where(f=>!removed.Contains(f.Identity)).ToArray();s.Counters[0]=(uint)s.Cells.Length;s.Counters[1]-=(uint)moved;
            CargoStats(s,catalog);CpuCutReference.Validate(s);return new FuelTransferResult{Matter=s,Tank=inventory,Count=moved};
        }
        public static FuelTransferResult Spill(MatterSnapshot original,TankInventory tank,MaterialCatalog catalog,IEnumerable<Vector2> outlets,Vector2 velocity,int limit=8)
        {
            if(limit<1||limit>32||!float.IsFinite(velocity.x)||!float.IsFinite(velocity.y))throw new ArgumentOutOfRangeException();
            CpuCutReference.Validate(original);var s=Copy(original);var inventory=tank.Copy();
            var cells=new List<LooseCell>(s.Cells);var energies=new List<FuelCellState>(s.FuelCells);int moved=0,attempts=0;
            if(s.NextIdentity==0)s.NextIdentity=cells.Count==0?1:checked(cells.Max(c=>c.Identity)+1);
            foreach(var at in outlets)
            {
                if(++attempts>128||moved>=limit||inventory.Count==0||cells.Count>=s.Capacity)break;
                if(!float.IsFinite(at.x)||!float.IsFinite(at.y))throw new ArgumentException("Invalid outlet.");
                var fuel=inventory.Contents[0];uint id=s.NextIdentity;if(id==uint.MaxValue)throw new InvalidOperationException("Cell identities exhausted.");
                cells.Add(new LooseCell{Position=at,Velocity=velocity,Material=catalog.IndexOf("fuel-"+fuel.Grade),Identity=id});
                energies.Add(new FuelCellState{Identity=id,Energy=fuel.Energy});
                s.Cells=cells.ToArray();s.FuelCells=energies.ToArray();s.Counters[0]=(uint)cells.Count;s.Counters[1]=original.Counters[1]+(uint)moved+1;
                try{CpuCutReference.Validate(s);}
                catch(InvalidOperationException){cells.RemoveAt(cells.Count-1);energies.RemoveAt(energies.Count-1);continue;}
                inventory.Contents.RemoveAt(0);s.NextIdentity++;moved++;
            }
            s.Cells=cells.ToArray();s.FuelCells=energies.ToArray();s.Counters[0]=(uint)cells.Count;s.Counters[1]=original.Counters[1]+(uint)moved;
            CargoStats(s,catalog);CpuCutReference.Validate(s);return new FuelTransferResult{Matter=s,Tank=inventory,Count=moved};
        }
    }
}
