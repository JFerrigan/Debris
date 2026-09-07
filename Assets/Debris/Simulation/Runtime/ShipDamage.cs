using System;
using System.Collections.Generic;
using System.Linq;
using Debris.Ships;
using UnityEngine;
namespace Debris.Simulation
{
    public sealed class ShipDamageResult : IDisposable
    {
        public MatterSnapshot Matter;
        public ShipRuntime Ship;
        public ShipBlueprint Blueprint;
        public int Released;
        public void Dispose(){if(Blueprint)UnityEngine.Object.DestroyImmediate(Blueprint);Blueprint=null;}
    }
    public static class ShipDamage
    {
        public static void SynchronizeFragments(MatterSnapshot matter,ShipRuntime ship)
        {
            foreach(var state in matter.Fragments)
            {
                var fragment=ship.Fragments.Find(f=>f.Id==state.Id);
                if(fragment==null)throw new InvalidOperationException("Missing fragment identity in ship registry.");
                fragment.Position=new Vector2(state.Pose.x,state.Pose.y);fragment.Angle=state.Pose.z;
                fragment.Velocity=new Vector2(state.Motion.x,state.Motion.y);fragment.AngularVelocity=state.Motion.z;
            }
        }
        public static ShipDamageResult CutHull(MatterSnapshot original,ShipRuntime source,IEnumerable<Vector2Int> positions,float unitDamage=0)
        {
            CpuCutReference.Validate(original);
            var result=new ShipDamageResult();
            try
            {
                result.Matter=FuelTransfers.Copy(original);result.Ship=ShipSnapshot.Capture(source).Restore(out var blueprint);result.Blueprint=blueprint;
                var s=result.Matter;var ship=result.Ship;
                ship.Position=new Vector2(s.ShipPose[0].x,s.ShipPose[0].y);ship.Angle=s.ShipPose[0].z;
                if(s.ShipPose[1].w>0){ship.Velocity=Vector2.zero;ship.AngularVelocity=0;}
                SynchronizeFragments(s,ship);
                var cells=new List<LooseCell>(s.Cells);int attempts=0;
                foreach(var p in positions)
                {
                    if(++attempts>32)throw new ArgumentException("Hull cuts are limited to 32 cells per command.");
                    if(!ship.Structure.TryGetValue(p,out var material))
                    {
                        var unit=ship.Units.Find(u=>u.OwnerId==ship.Id&&new RectInt(u.Placement.Position,u.Placement.Definition.Size).Contains(p));
                        if(unit!=null&&unitDamage>0)ship.DamageUnit(unit.Placement.Id,unitDamage);
                        continue;
                    }
                    if(cells.Count>=s.Capacity)break;
                    if(s.NextIdentity==0)s.NextIdentity=cells.Count==0?1:checked(cells.Max(c=>c.Identity)+1);
                    if(s.NextIdentity==uint.MaxValue)throw new InvalidOperationException("Cell identities exhausted.");
                    ship.RemoveHull(p);
                    if(ship.Fragments.Count>16)throw new InvalidOperationException("Active fragment admission exhausted; hull cut retained until regions can stream.");
                    var local=(Vector2)p+Vector2.one*.5f;var radial=local.normalized*2+new Vector2(-local.y,local.x)*ship.AngularVelocity;
                    var velocity=ship.Velocity+ship.ToWorld(radial)-ship.Position;
                    cells.Add(new LooseCell{Position=p,Velocity=velocity,Material=material,Identity=s.NextIdentity++,Flags=4});result.Released++;
                }
                s.Cells=cells.ToArray();s.Counters[0]=(uint)cells.Count;s.Counters[1]+=(uint)result.Released;
                s.Hull=ship.CollisionMask();s.Fragments=ship.Fragments.Select(RigidFragmentSnapshot.FromShip).ToArray();
                s.Impact=new uint[4];CpuCutReference.Validate(s);return result;
            }
            catch{result.Dispose();throw;}
        }
    }
}
