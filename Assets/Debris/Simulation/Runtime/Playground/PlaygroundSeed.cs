using System;
using System.Linq;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using Debris.Simulation.ParallelProof;
using UnityEngine;

namespace Debris.Simulation.Playground
{
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=32)]
    public struct PlaygroundShapeRecord
    {
        public Vector2 Half;
        public Vector4 Color;
        public uint Anchored,Grain;
    }
    internal sealed class PlaygroundSeed
    {
        internal PlaygroundWorldDefinition World;
        internal PlaygroundObject[] Ordered;
        internal CoupledTickInitialState Initial;
        internal CoupledContactBody[] Bodies;
        internal CoupledContactPrimitive[] Primitives;
        internal CoupledEndpointIdentityTable Identities;
        internal PlaygroundShapeRecord[] Shapes;
        internal static PlaygroundSeed Build(PlaygroundWorldDefinition source)
        {
            source.Validate();var world=source.Copy();
            var ordered=world.Objects.OrderBy(o=>o.Shape==PlaygroundShape.Grain?0:1).ThenBy(o=>o.Id,StringComparer.Ordinal).ToArray();
            int n=ordered.Length,g=ordered.Count(o=>o.Shape==PlaygroundShape.Grain);
            if(n==0)return new PlaygroundSeed{World=world,Ordered=ordered,Shapes=Array.Empty<PlaygroundShapeRecord>()};
            var ids=new CoupledEndpointIdentityTable(n);
            var initial=new CoupledTickInitialState{Endpoints=new BodyState[n],Parameters=new BodyParameters[n],
                Grains=new Debris.Simulation.ParallelProof.LooseCell[g],Cache=new CoupledCachePoint[Math.Max(16,n*8)]};
            var bodies=new CoupledContactBody[n];var primitives=new CoupledContactPrimitive[n];var shapes=new PlaygroundShapeRecord[n];
            for(int i=0;i<n;i++)
            {
                var o=ordered[i];bool grain=o.Shape==PlaygroundShape.Grain;
                var e=new BodyState{Center=o.Center,Velocity=o.Velocity,Angle=o.Angle,AngularVelocity=o.Spin};
                float im=o.Anchored?0:1/o.Mass,ii=o.Anchored?0:1/o.Inertia;
                initial.Endpoints[i]=e;initial.Parameters[i]=new BodyParameters{InverseMass=im,InverseInertia=ii,
                    Mobility=o.Anchored?0u:1u,ShapeRevision=1};
                if(grain)initial.Grains[i]=new Debris.Simulation.ParallelProof.LooseCell{Center=e.Center,Velocity=e.Velocity,
                    Angle=e.Angle,AngularVelocity=e.AngularVelocity,Identity=o.GrainIdentity,Material=1};
                var guid=new Guid(o.Id).ToByteArray();
                ids.Bind(i,grain?CoupledPersistentIdentity.Grain(o.GrainIdentity):new CoupledPersistentIdentity{
                    Kind=2,Low=BitConverter.ToUInt64(guid,0),High=BitConverter.ToUInt64(guid,8)},1);
                bodies[i]=new CoupledContactBody{Center=e.Center,InverseMass=im,InverseInertia=ii};
                primitives[i]=new CoupledContactPrimitive{Center=e.Center,Half=o.Half,Angle=e.Angle,Endpoint=(uint)i,
                    Anchored=o.Anchored?1u:0u,ContactClass=grain?1u:2u,FeatureId=1};
                shapes[i]=new PlaygroundShapeRecord{Half=o.Half,Anchored=o.Anchored?1u:0u,Grain=grain?1u:0u,
                    Color=o.Anchored?new Vector4(.32f,.39f,.48f,1):grain?new Vector4(.95f,.68f,.32f,1):new Vector4(.22f,.78f,.72f,1)};
            }
            initial.Bindings=ids.Snapshot();var h=initial.Header;h.IdentityRevision=ids.TableRevision;initial.Header=h;
            return new PlaygroundSeed{World=world,Ordered=ordered,Initial=initial,Bodies=bodies,Primitives=primitives,Identities=ids,Shapes=shapes};
        }
    }
}
