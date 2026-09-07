using System;
using System.Linq;
using Debris.Ships;
using Debris.Simulation;
using UnityEngine;
namespace Debris.Persistence
{
    public sealed class SiteDeparture
    {
        public SalvageSave Site,Portable;
    }
    public static class SiteTransit
    {
        static ShipSnapshot Copy(ShipSnapshot ship)=>JsonUtility.FromJson<ShipSnapshot>(JsonUtility.ToJson(ship));
        static SalvageSave Copy(SalvageSave save)=>new SalvageSave{SiteId=save.SiteId,GeneratorKey=save.GeneratorKey,GeneratorRevision=save.GeneratorRevision,GeneratorSeed=save.GeneratorSeed,MaterialKeys=(string[])save.MaterialKeys.Clone(),Matter=FuelTransfers.Copy(save.Matter),Ship=save.Ship==null?null:Copy(save.Ship)};
        static void KeepCells(MatterSnapshot state,bool cargo)
        {
            int before=state.Cells.Length;state.Cells=state.Cells.Where(c=>((c.Flags&4)!=0)==cargo).ToArray();
            var identities=state.Cells.Select(c=>c.Identity).ToHashSet();state.FuelCells=state.FuelCells.Where(f=>identities.Contains(f.Identity)).ToArray();
            state.Counters[0]=(uint)state.Cells.Length;state.Counters[1]-=(uint)(before-state.Cells.Length);
        }
        public static SiteDeparture Depart(SalvageSave current)
        {
            CpuCutReference.Validate(current.Matter);
            if(current.Ship==null||!current.Matter.ShipEnabled)throw new InvalidOperationException("A player ship is required for departure.");
            if(current.Matter.Impact[3]!=0)throw new InvalidOperationException("Resolve the queued impact before departure.");
            if(current.Ship.Fuel.Count>0&&current.Ship.Units.Any(u=>u.Placement.Definition.Kind==UnitKind.Tank&&(!u.Supported||u.Destroyed)))throw new InvalidOperationException("Release the damaged tank's remaining fuel before departure.");
            var site=Copy(current);var portable=Copy(current);KeepCells(site.Matter,false);KeepCells(portable.Matter,true);
            site.Matter.ShipEnabled=false;site.Matter.Hull=new uint[16384];site.Matter.ShipPose=new Vector4[3];site.Matter.Impact=new uint[4];
            // The inactive site's DTO is solely a registry for its fragment machinery.
            site.Ship.Units=site.Ship.Units.Where(u=>u.OwnerId!=site.Ship.Id).ToArray();site.Ship.Structure=Array.Empty<StructuralCell>();
            site.Ship.Fuel=new TankInventory{Capacity=0};site.Ship.Position=site.Ship.Velocity=Vector2.zero;site.Ship.Angle=site.Ship.AngularVelocity=site.Ship.CargoMass=0;site.Ship.DoorOpen=false;
            portable.Ship.Units=portable.Ship.Units.Where(u=>u.OwnerId==portable.Ship.Id).ToArray();portable.Ship.Fragments=Array.Empty<ShipFragment>();
            portable.Matter.Fragments=Array.Empty<RigidFragmentSnapshot>();portable.Matter.Fields=portable.Matter.Fields.Select(c=>new uint[c.Length]).ToArray();
            portable.Matter.Damage=portable.Matter.Damage.Select(c=>new float[c.Length]).ToArray();portable.Matter.Counters[1]=(uint)portable.Matter.Cells.Length;
            portable.Matter.Dirty=new uint[portable.Matter.Dirty.Length];portable.Matter.Counters[3]=0;
            CpuCutReference.Validate(site.Matter);CpuCutReference.Validate(portable.Matter);return new SiteDeparture{Site=site,Portable=portable};
        }
        public static SalvageSave Arrive(SalvageSave inactive,SalvageSave portable,uint nextIdentity,Vector2 position)
        {
            if(inactive.Matter.ShipEnabled||inactive.Matter.Cells.Any(c=>(c.Flags&4)!=0))throw new InvalidOperationException("Destination still owns a player ship.");
            if(!inactive.MaterialKeys.SequenceEqual(portable.MaterialKeys))throw new InvalidOperationException("Resolve both sites against the same catalog before travel.");
            var result=Copy(inactive);var state=result.Matter;var carried=portable.Matter;
            if(state.Cells.Length+carried.Cells.Length>state.Capacity)throw new InvalidOperationException("Destination active debris budget is full; departure retained.");
            state.Cells=state.Cells.Concat(carried.Cells).ToArray();state.FuelCells=state.FuelCells.Concat(carried.FuelCells).ToArray();
            state.Counters[0]=(uint)state.Cells.Length;state.Counters[1]+=(uint)carried.Cells.Length;state.NextIdentity=nextIdentity;
            state.ShipEnabled=true;state.Hull=(uint[])carried.Hull.Clone();state.ShipPose=(Vector4[])carried.ShipPose.Clone();state.ShipPose[0]=new Vector4(position.x,position.y,portable.Ship.Angle,1);state.ShipPose[1]=Vector4.zero;state.Impact=new uint[4];
            result.Ship=Copy(portable.Ship);result.Ship.Position=position;result.Ship.Velocity=Vector2.zero;result.Ship.AngularVelocity=0;
            if(inactive.Ship!=null)
            {
                result.Ship.Fragments=Copy(inactive.Ship).Fragments;
                result.Ship.Units=result.Ship.Units.Concat(result.Ship.Fragments.SelectMany(f=>f.Units)).ToArray();
            }
            if(result.Ship.Units.Select(u=>u.Placement.Id).Distinct().Count()!=result.Ship.Units.Length)throw new InvalidOperationException("Duplicate machinery identity during travel.");
            uint maximum=state.Cells.Length==0?0:state.Cells.Max(c=>c.Identity);
            if(nextIdentity<=maximum)throw new InvalidOperationException("World identity is behind destination cells.");
            CpuCutReference.ValidateShipPlacement(state,result.Ship.Id);return result;
        }
    }
}
