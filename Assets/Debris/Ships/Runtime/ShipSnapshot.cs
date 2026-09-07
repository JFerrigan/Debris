using System;
using System.Linq;
using UnityEngine;
namespace Debris.Ships
{
    [Serializable]
    public sealed class ShipSnapshot
    {
        public string Id,BlueprintJson;
        public StructuralCell[] Structure;
        public UnitState[] Units;
        public ShipFragment[] Fragments;
        public TankInventory Fuel;
        public Vector2 Position,Velocity;
        public float Angle,AngularVelocity,CargoMass;
        public bool DoorOpen;

        public static ShipSnapshot Capture(ShipRuntime ship)
        {
            var state=new ShipSnapshot
            {
                Id=ship.Id,BlueprintJson=JsonUtility.ToJson(ship.Blueprint),
                Structure=ship.Structure.OrderBy(c=>c.Key.y).ThenBy(c=>c.Key.x).Select(c=>new StructuralCell(c.Key.x,c.Key.y,c.Value)).ToArray(),
                Units=ship.Units.ToArray(),Fragments=ship.Fragments.ToArray(),Fuel=ship.Fuel,
                Position=ship.Position,Velocity=ship.Velocity,Angle=ship.Angle,AngularVelocity=ship.AngularVelocity,CargoMass=ship.CargoMass,DoorOpen=ship.DoorOpen
            };
            // Snapshot owns its data while the save worker serializes it.
            return JsonUtility.FromJson<ShipSnapshot>(JsonUtility.ToJson(state));
        }
        public ShipRuntime Restore(out ShipBlueprint blueprint)
        {
            blueprint=ScriptableObject.CreateInstance<ShipBlueprint>();
            try
            {
                JsonUtility.FromJsonOverwrite(BlueprintJson,blueprint);blueprint.Validate();
                var copy=JsonUtility.FromJson<ShipSnapshot>(JsonUtility.ToJson(this));copy.Fuel.Validate();
                var ship=new ShipRuntime(blueprint,Id){Fuel=copy.Fuel,Position=Position,Velocity=Velocity,Angle=Angle,AngularVelocity=AngularVelocity,CargoMass=CargoMass,DoorOpen=DoorOpen};
                ship.Structure.Clear();foreach(var cell in copy.Structure)ship.Structure.Add(cell.Position,cell.Material);
                ship.Units.Clear();ship.Units.AddRange(copy.Units);ship.Fragments.AddRange(copy.Fragments);
                // Fragments refer to the stable unit registry, not duplicated machines.
                foreach(var fragment in ship.Fragments)
                    for(int i=0;i<fragment.Units.Count;i++)fragment.Units[i]=ship.Units.Single(u=>u.Placement.Id==fragment.Units[i].Placement.Id);
                return ship;
            }
            catch{UnityEngine.Object.DestroyImmediate(blueprint);throw;}
        }
    }
}
