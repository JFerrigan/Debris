using System;
using System.Collections.Generic;
using Debris.Core;
using UnityEngine;
namespace Debris.Ships
{
    public enum UnitKind { Command,Thruster,Tank,Drill,Suction,Door,MiscStorage,Pressure,Weapon }
    [Serializable] public sealed class UnitDefinition
    {
        public string Key;
        public UnitKind Kind;
        public Vector2Int Size;
        public float Mass=20,MaximumHealth=100,Output=1;
        public int InventoryCapacity;
    }
    [Serializable] public sealed class UnitPlacement
    {
        public string Id;
        public UnitDefinition Definition;
        public Vector2Int Position,Anchor;
    }
    [Serializable] public struct StructuralCell
    {
        public Vector2Int Position;
        public ushort Material;
        public StructuralCell(int x,int y,ushort material){Position=new Vector2Int(x,y);Material=material;}
    }
    [CreateAssetMenu(menuName="Debris/Ships/Blueprint")]
    public sealed class ShipBlueprint : ScriptableObject
    {
        public string Key="starter";
        public List<StructuralCell> Structure=new List<StructuralCell>();
        public List<UnitPlacement> Units=new List<UnitPlacement>();
        public RectInt CargoCavity=new RectInt(-25,-25,50,50);
        public Vector2Int CoreAnchor=new Vector2Int(27,0);
        public void DrawCell(int x,int y,ushort material)
        {
            var p=new Vector2Int(x,y);
            if(material==0)throw new ArgumentException("Structure must have material.");
            if(Structure.Exists(c=>c.Position==p)||CargoCavity.Contains(p)||Units.Exists(u=>new RectInt(u.Position,u.Definition.Size).Contains(p)))throw new InvalidOperationException("Structure placement overlaps occupied or cargo space.");
            Structure.Add(new StructuralCell(x,y,material));
        }
        public void PlacePrefab(IEnumerable<StructuralCell> cells,Vector2Int offset)
        {
            int initial=Structure.Count;
            try{foreach(var c in cells)DrawCell(c.Position.x+offset.x,c.Position.y+offset.y,c.Material);}
            catch{Structure.RemoveRange(initial,Structure.Count-initial);throw;}
        }
        public void PlaceUnit(UnitPlacement unit)
        {
            new StableId(unit.Id);
            if(unit.Definition==null||unit.Definition.Size.x<1||unit.Definition.Size.y<1)throw new ArgumentException("Invalid whole unit footprint.");
            var box=new RectInt(unit.Position,unit.Definition.Size);
            if(box.Overlaps(CargoCavity)||Structure.Exists(c=>box.Contains(c.Position))||Units.Exists(u=>u.Id==unit.Id||new RectInt(u.Position,u.Definition.Size).Overlaps(box)))throw new InvalidOperationException("Unit footprint overlaps structure/cargo/unit.");
            if(!Structure.Exists(c=>c.Position==unit.Anchor))throw new InvalidOperationException("Unit needs a structural anchor.");
            Units.Add(unit);
        }
        public void Validate()
        {
            var positions=new HashSet<Vector2Int>();
            foreach(var c in Structure)if(c.Material==0||!positions.Add(c.Position)||CargoCavity.Contains(c.Position))throw new InvalidOperationException("Invalid structural cell.");
            if(!positions.Contains(CoreAnchor))throw new InvalidOperationException("Missing command support.");
            var visited=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();queue.Enqueue(CoreAnchor);visited.Add(CoreAnchor);
            var directions=new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right};
            while(queue.Count>0){var p=queue.Dequeue();foreach(var d in directions)if(positions.Contains(p+d)&&visited.Add(p+d))queue.Enqueue(p+d);}
            if(visited.Count!=positions.Count)throw new InvalidOperationException("Disconnected blueprint structure.");
            var units=Units.ToArray();Units.Clear();try{foreach(var u in units)PlaceUnit(u);}finally{Units.Clear();Units.AddRange(units);}
        }
        public static ShipBlueprint Starter(ushort hullMaterial)
        {
            var b=CreateInstance<ShipBlueprint>();
            // A real 50x50 cavity, open at the rear. Three-cell upper/lower rails and a front spine.
            for(int x=-40;x<39;x++)for(int n=0;n<3;n++){b.DrawCell(x,25+n,hullMaterial);b.DrawCell(x,-28+n,hullMaterial);}
            for(int x=25;x<29;x++)for(int y=-25;y<25;y++)b.DrawCell(x,y,hullMaterial);
            for(int x=39;x<55;x++)for(int n=0;n<3;n++)b.DrawCell(x,25+n,hullMaterial);
            for(int x=52;x<55;x++)for(int y=5;y<25;y++)b.DrawCell(x,y,hullMaterial);
            int id=1;
            Action<string,UnitKind,int,int,int,int,int,int,int> add=(key,kind,x,y,w,h,ax,ay,capacity)=>b.PlaceUnit(new UnitPlacement{Id=(id++).ToString("x32"),Definition=new UnitDefinition{Key=key,Kind=kind,Size=new Vector2Int(w,h),InventoryCapacity=capacity},Position=new Vector2Int(x,y),Anchor=new Vector2Int(ax,ay)});
            add("command",UnitKind.Command,29,-7,12,14,28,0,0);
            add("tank",UnitKind.Tank,29,8,10,14,28,10,300);
            add("upper-thruster",UnitKind.Thruster,-45,28,15,8,-35,27,0);
            add("lower-thruster",UnitKind.Thruster,-45,-36,15,8,-35,-28,0);
            add("drill",UnitKind.Drill,41,-5,14,10,52,5,0);
            add("suction",UnitKind.Suction,-40,17,10,8,-35,25,0);
            add("rear-door",UnitKind.Door,-29,-25,4,50,-29,25,0);
            b.Validate();return b;
        }
    }
}
