using System;
using Debris.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Debris.Presentation
{
    public sealed class MatterView : IDisposable
    {
        readonly Mesh quad;
        readonly Material fixedMaterial,looseMaterial;
        readonly MatterSession session;
        readonly RenderParams fixedParams,looseParams;
        public MatterView(MatterSession value)
        {
            session=value;
            quad=new Mesh{name="Universal material cell"};quad.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};quad.triangles=new[]{0,2,1,0,3,2};quad.RecalculateBounds();
            fixedMaterial=new Material(Resources.Load<Shader>("Matter")){enableInstancing=true};looseMaterial=new Material(fixedMaterial);looseMaterial.SetFloat("_Loose",1);
            foreach(var material in new[]{fixedMaterial,looseMaterial})
            {
                material.SetTexture("_Field",session.Field);material.SetBuffer("_Cells",session.Cells);material.SetBuffer("_Counters",session.Counters);
                material.SetBuffer("_Palette",session.Palette);material.SetBuffer("_Shadows",session.Shadows);material.SetBuffer("_Emissions",session.Emissions);
                material.SetInt("_ChunkSize",session.ChunkSize);material.SetInt("_Side",session.Side);material.SetVector("_Origin",new Vector4(session.Origin.x,session.Origin.y,0,0));
            }
            var bounds=new Bounds(Vector3.zero,new Vector3(session.Width,session.Width,10));
            fixedParams=new RenderParams(fixedMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
            looseParams=new RenderParams(looseMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
        }
        public void Draw(){Graphics.RenderMeshPrimitives(fixedParams,quad,0,session.Side*session.Side);Graphics.RenderMeshPrimitives(looseParams,quad,0,session.Capacity);}
        public void Dispose(){UnityEngine.Object.DestroyImmediate(quad);UnityEngine.Object.DestroyImmediate(fixedMaterial);UnityEngine.Object.DestroyImmediate(looseMaterial);}
    }
}
