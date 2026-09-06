using System;
using System.Collections.Generic;
using Debris.Core;
using UnityEngine;
namespace Debris.Ships
{
    [Serializable] public sealed class UnitState
    {
        public UnitPlacement Placement;
        public float Health=100;
        public bool Supported=true,Destroyed;
        public bool Operational=>Supported&&!Destroyed&&Health>0;
    }
    [Serializable] public sealed class TankInventory
    {
        public int Capacity=300;
        public int Low,Standard,Dense;
        public double BurnRemainder;
        public int Count=>Low+Standard+Dense;
        public double Energy=>Low+Standard*2+Dense*4-BurnRemainder;
        public bool Add(string grade,int count)
        {
            if(count<0||count>Capacity-Count)return false;
            switch(grade){case "low":Low+=count;break;case "standard":Standard+=count;break;case "dense":Dense+=count;break;default:return false;}return true;
        }
        public bool Consume(double energy)
        {
            if(double.IsNaN(energy)||double.IsInfinity(energy)||energy<0||energy>Energy)return false;
            BurnRemainder+=energy;
            while(Low>0&&BurnRemainder>=1){Low--;BurnRemainder-=1;}
            while(Low==0&&Standard>0&&BurnRemainder>=2){Standard--;BurnRemainder-=2;}
            while(Low==0&&Standard==0&&Dense>0&&BurnRemainder>=4){Dense--;BurnRemainder-=4;}
            return true;
        }
    }
    [Serializable] public sealed class ShipFragment
    {
        public string Id=StableId.New().Value;
        public List<StructuralCell> Cells=new List<StructuralCell>();
        public List<UnitState> Units=new List<UnitState>();
        public Vector2 Position,Velocity;
        public float Angle,AngularVelocity;
    }
    // Authoritative low-volume ship state. GPU owns cargo and moving cell collision.
    public sealed class ShipRuntime
    {
        public readonly string Id;
        public readonly ShipBlueprint Blueprint;
        public readonly Dictionary<Vector2Int,ushort> Structure=new Dictionary<Vector2Int,ushort>();
        public readonly List<UnitState> Units=new List<UnitState>();
        public readonly List<ShipFragment> Fragments=new List<ShipFragment>();
        public TankInventory Fuel=new TankInventory();
        public Vector2 Position=new Vector2(-175,0),Velocity;
        public float Angle,AngularVelocity,CargoMass;
        public bool DoorOpen;
        public bool Pressurized=>false;
        public float DryMass { get { float mass=Structure.Count;foreach(var unit in Units)if(unit.Supported)mass+=unit.Placement.Definition.Mass;return mass; } }
        public float TotalMass=>DryMass+CargoMass+Fuel.Count;
        public ShipRuntime(ShipBlueprint blueprint,string id=null)
        {
            blueprint.Validate();Blueprint=blueprint;Id=id??StableId.New().Value;
            foreach(var c in blueprint.Structure)Structure.Add(c.Position,c.Material);
            foreach(var unit in blueprint.Units)Units.Add(new UnitState{Placement=unit,Health=unit.Definition.MaximumHealth});
            Fuel.Capacity=0;foreach(var unit in Units)if(unit.Placement.Definition.Kind==UnitKind.Tank)Fuel.Capacity+=unit.Placement.Definition.InventoryCapacity;
            Fuel.Add("standard",Math.Min(250,Fuel.Capacity));
        }
        public uint[] CollisionMask()
        {
            var mask=new uint[128*128];
            foreach(var c in Structure)
            {
                if(c.Key.x<-64||c.Key.x>=64||c.Key.y<-64||c.Key.y>=64)throw new InvalidOperationException("Starter GPU mask exceeded; larger ships require paged masks.");
                mask[(c.Key.y+64)*128+c.Key.x+64]=c.Value;
            }
            foreach(var unit in Units)
            {
                if(!unit.Supported||unit.Destroyed)continue;
                var p=unit.Placement;var size=p.Definition.Size;
                for(int y=p.Position.y;y<p.Position.y+size.y;y++)for(int x=p.Position.x;x<p.Position.x+size.x;x++)
                {
                    if(x<-64||x>=64||y<-64||y>=64)throw new InvalidOperationException("Unit exceeds starter GPU mask.");
                    mask[(y+64)*128+x+64]=p.Definition.Kind==UnitKind.Door?uint.MaxValue:2u;
                }
            }
            return mask;
        }
        public bool Has(UnitKind kind)=>Units.Exists(u=>u.Placement.Definition.Kind==kind&&u.Operational)&&Units.Exists(u=>u.Placement.Definition.Kind==UnitKind.Command&&u.Operational);
        public Vector2 ToWorld(Vector2 local){float c=Mathf.Cos(Angle),s=Mathf.Sin(Angle);return Position+new Vector2(local.x*c-local.y*s,local.x*s+local.y*c);}
        public Vector2 ToLocal(Vector2 world){var p=world-Position;float c=Mathf.Cos(Angle),s=Mathf.Sin(Angle);return new Vector2(p.x*c+p.y*s,-p.x*s+p.y*c);}
        public void Tick(Vector2 thrust,float turn,float delta)
        {
            if(!float.IsFinite(delta)||delta<=0||delta>.1f||!float.IsFinite(thrust.x)||!float.IsFinite(thrust.y)||!float.IsFinite(turn))throw new ArgumentOutOfRangeException(nameof(delta));
            if(!Has(UnitKind.Command)){Position+=Velocity*delta;Angle+=AngularVelocity*delta;return;}
            int thrusters=Units.FindAll(u=>u.Placement.Definition.Kind==UnitKind.Thruster&&u.Operational).Count;
            float load=DryMass/Mathf.Max(1,TotalMass);
            float effort=Mathf.Clamp01(thrust.magnitude)+Mathf.Abs(Mathf.Clamp(turn,-1,1))*.4f;
            if(thrusters>0&&Has(UnitKind.Tank)&&Fuel.Consume(effort*delta*.9))
            {
                float c=Mathf.Cos(Angle),s=Mathf.Sin(Angle);var local=Vector2.ClampMagnitude(thrust,1);
                Velocity+=new Vector2(local.x*c-local.y*s,local.x*s+local.y*c)*(12*load*delta*thrusters/2);
                AngularVelocity+=Mathf.Clamp(turn,-1,1)*.4f*load*delta*thrusters/2;
            }
            Velocity=Vector2.ClampMagnitude(Velocity,22);AngularVelocity=Mathf.Clamp(AngularVelocity,-.35f,.35f);
            Position+=Velocity*delta;Angle+=AngularVelocity*delta;
        }
        public bool RemoveHull(Vector2Int p)
        {
            if(!Structure.Remove(p))return false;ResolveSupport();return true;
        }
        public void DamageUnit(string id,float amount)
        {
            if(!(amount>=0)||float.IsInfinity(amount))throw new ArgumentOutOfRangeException(nameof(amount));
            var unit=Units.Find(u=>u.Placement.Id==id);if(unit==null)return;
            unit.Health=Mathf.Max(0,unit.Health-amount);if(unit.Health==0)unit.Destroyed=true;
        }
        void ResolveSupport()
        {
            var supported=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();
            if(Structure.ContainsKey(Blueprint.CoreAnchor)){supported.Add(Blueprint.CoreAnchor);queue.Enqueue(Blueprint.CoreAnchor);}
            var directions=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
            while(queue.Count>0){var p=queue.Dequeue();foreach(var d in directions)if(Structure.ContainsKey(p+d)&&supported.Add(p+d))queue.Enqueue(p+d);}
            var unsupported=new HashSet<Vector2Int>(Structure.Keys);unsupported.ExceptWith(supported);
            while(unsupported.Count>0)
            {
                var enumerator=unsupported.GetEnumerator();enumerator.MoveNext();var first=enumerator.Current;
                var fragment=new ShipFragment{Position=Position,Velocity=Velocity,Angle=Angle,AngularVelocity=AngularVelocity};
                queue.Enqueue(first);unsupported.Remove(first);
                var region=new HashSet<Vector2Int>();
                while(queue.Count>0){var p=queue.Dequeue();region.Add(p);fragment.Cells.Add(new StructuralCell(p.x,p.y,Structure[p]));Structure.Remove(p);foreach(var d in directions)if(unsupported.Remove(p+d))queue.Enqueue(p+d);}
                foreach(var unit in Units)if(region.Contains(unit.Placement.Anchor)){unit.Supported=false;fragment.Units.Add(unit);}
                Fragments.Add(fragment);
            }
            foreach(var unit in Units)unit.Supported=supported.Contains(unit.Placement.Anchor);
        }
    }
}
