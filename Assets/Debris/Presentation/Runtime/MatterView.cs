using System;
using Debris.Simulation;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;
namespace Debris.Presentation
{
    public sealed class MatterView : IDisposable
    {
        readonly Mesh quad;
        readonly Material fixedMaterial,looseMaterial,shipMaterial,fragmentMaterial,candidateLoose,candidateShip,candidateFragment;
        readonly GraphicsBuffer fallbackCandidateGrains,fallbackCandidateBodies,fallbackCandidateParameters;
        readonly MatterSession session;
        readonly RenderParams fixedParams,looseParams,shipParams,fragmentParams;
        public MatterView(MatterSession value)
        {
            session=value;
            quad=new Mesh{name="Universal material cell"};quad.vertices=new[]{Vector3.zero,Vector3.right,new Vector3(1,1,0),Vector3.up};quad.triangles=new[]{0,2,1,0,3,2};quad.RecalculateBounds();
            fixedMaterial=new Material(Resources.Load<Shader>("Matter")){enableInstancing=true};looseMaterial=new Material(fixedMaterial);looseMaterial.SetFloat("_Loose",1);shipMaterial=new Material(fixedMaterial);shipMaterial.SetFloat("_Loose",2);fragmentMaterial=new Material(fixedMaterial);fragmentMaterial.SetFloat("_Loose",3);
            candidateLoose=new Material(fixedMaterial);candidateLoose.SetFloat("_Loose",1);candidateLoose.SetFloat("_Candidate",1);
            candidateShip=new Material(fixedMaterial);candidateShip.SetFloat("_Loose",2);candidateShip.SetFloat("_Candidate",1);
            candidateFragment=new Material(fixedMaterial);candidateFragment.SetFloat("_Loose",3);candidateFragment.SetFloat("_Candidate",1);
            // Metal validates every declared buffer even when the legacy branch
            // does not read it. Keep a harmless binding until candidate import.
            fallbackCandidateGrains=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,48);
            fallbackCandidateBodies=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32);
            fallbackCandidateParameters=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32);
            foreach(var material in new[]{fixedMaterial,looseMaterial,shipMaterial,fragmentMaterial})
            {
                material.SetBuffer("_FragmentHull",session.FragmentHull);material.SetBuffer("_FragmentPose",session.FragmentPose);material.SetBuffer("_Hull",session.Hull);material.SetBuffer("_ShipPose",session.ShipPose);
                material.SetTexture("_Field",session.Field);material.SetBuffer("_Cells",session.Cells);material.SetBuffer("_Counters",session.Counters);
                material.SetBuffer("_Palette",session.Palette);material.SetBuffer("_Shadows",session.Shadows);material.SetBuffer("_Emissions",session.Emissions);
                material.SetInt("_ChunkSize",session.ChunkSize);material.SetInt("_Side",session.Side);material.SetVector("_Origin",new Vector4(session.Origin.x,session.Origin.y,0,0));
            }
            foreach(var material in new[]{fixedMaterial,looseMaterial,shipMaterial,fragmentMaterial,candidateLoose,candidateShip,candidateFragment})
            {material.SetBuffer("_CandidateGrains",fallbackCandidateGrains);material.SetBuffer("_CandidateBodies",fallbackCandidateBodies);material.SetBuffer("_CandidateParameters",fallbackCandidateParameters);}
            var bounds=new Bounds(Vector3.zero,new Vector3(session.Width,session.Width,10));
            fixedParams=new RenderParams(fixedMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
            fragmentParams=new RenderParams(fragmentMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
            shipParams=new RenderParams(shipMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
            looseParams=new RenderParams(looseMaterial){worldBounds=bounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false};
        }
        ParallelGrainSolver candidate;
        public void BindCandidate(ParallelGameplaySession value)
        {
            candidate=value?.Solver;
            if(candidate==null)return;
            foreach(var material in new[]{candidateLoose,candidateShip,candidateFragment})
            {
                material.SetBuffer("_CandidateGrains",candidate.Grains);material.SetBuffer("_CandidateBodies",candidate.Bodies);material.SetBuffer("_CandidateParameters",candidate.Parameters);
                material.SetBuffer("_Cells",session.Cells);material.SetBuffer("_Counters",session.Counters);material.SetBuffer("_ShipPose",session.ShipPose);material.SetBuffer("_FragmentPose",session.FragmentPose);material.SetBuffer("_FragmentHull",session.FragmentHull);material.SetBuffer("_Hull",session.Hull);material.SetBuffer("_Palette",session.Palette);material.SetBuffer("_Shadows",session.Shadows);material.SetBuffer("_Emissions",session.Emissions);
                material.SetTexture("_Field",session.Field);material.SetInt("_ChunkSize",session.ChunkSize);material.SetInt("_Side",session.Side);material.SetVector("_Origin",new Vector4(session.Origin.x,session.Origin.y,0,0));
            }
            candidateShip.SetInt("_CandidateBody",candidate.GrainCount);
        }
        public void DrawCandidate()
        {
            if(candidate==null){Draw();return;}
            Graphics.RenderMeshPrimitives(fixedParams,quad,0,session.Side*session.Side);
            Graphics.RenderMeshPrimitives(new RenderParams(candidateLoose){worldBounds=looseParams.worldBounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false},quad,0,candidate.GrainCount);
            Graphics.RenderMeshPrimitives(new RenderParams(candidateShip){worldBounds=shipParams.worldBounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false},quad,0,128*128);
            for(int f=1;f<candidate.BodyCount-1;f++){candidateFragment.SetInt("_CandidateBody",candidate.GrainCount+f);candidateFragment.SetInt("_CandidateFragment",f-1);Graphics.RenderMeshPrimitives(new RenderParams(candidateFragment){worldBounds=fragmentParams.worldBounds,shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false},quad,0,16384);}
        }
        public void Draw(){Graphics.RenderMeshPrimitives(fixedParams,quad,0,session.Side*session.Side);Graphics.RenderMeshPrimitives(looseParams,quad,0,session.Capacity);if(session.FragmentCount>0)Graphics.RenderMeshPrimitives(fragmentParams,quad,0,session.FragmentCount*16384);if(session.ShipEnabled)Graphics.RenderMeshPrimitives(shipParams,quad,0,128*128);}
        public void Dispose(){fallbackCandidateGrains.Dispose();fallbackCandidateBodies.Dispose();fallbackCandidateParameters.Dispose();UnityEngine.Object.DestroyImmediate(quad);foreach(var m in new[]{fixedMaterial,looseMaterial,shipMaterial,fragmentMaterial,candidateLoose,candidateShip,candidateFragment})UnityEngine.Object.DestroyImmediate(m);}
    }
}
