using System;
using System.Collections.Generic;
using Debris.Ships;
using Debris.Materials;
using UnityEngine;
namespace Debris.Simulation
{
    public sealed class RigidFragmentSnapshot
    {
        public string Id;
        public uint[] Hull;
        public Vector4 Pose,Motion;
        public BodyMass Mass;
        public static RigidFragmentSnapshot FromShip(ShipFragment fragment)=>FromShip(fragment,null);
        public static RigidFragmentSnapshot FromShip(ShipFragment fragment,TankInventory fuel)
        {
            var mask=new uint[128*128];
            void Put(int x,int y,uint material)
            {
                if(x<-64||x>=64||y<-64||y>=64)throw new InvalidOperationException("Fragment exceeds the starter local field; page it before activation.");
                mask[(y+64)*128+x+64]=material;
            }
            foreach(var cell in fragment.Cells)Put(cell.Position.x,cell.Position.y,cell.Material);
            foreach(var unit in fragment.Units)
            {
                var p=unit.Placement;for(int y=p.Position.y;y<p.Position.y+p.Definition.Size.y;y++)for(int x=p.Position.x;x<p.Position.x+p.Definition.Size.x;x++)Put(x,y,unit.Destroyed?5u:2u);
            }
            var mass=ShipRuntime.FragmentMassProperties(fragment,Resources.Load<MaterialCatalog>("Materials"),fuel);
            float c=Mathf.Cos(fragment.Angle),sn=Mathf.Sin(fragment.Angle);var offset=new Vector2(mass.Center.x*c-mass.Center.y*sn,mass.Center.x*sn+mass.Center.y*c);
            var velocity=ContactPhysics.Surface(fragment.Velocity,fragment.AngularVelocity,offset);
            return new RigidFragmentSnapshot{Mass=mass,Id=fragment.Id,Hull=mask,Pose=new Vector4(fragment.Position.x,fragment.Position.y,fragment.Angle,1),Motion=new Vector4(velocity.x,velocity.y,fragment.AngularVelocity,0)};
        }
    }
}
