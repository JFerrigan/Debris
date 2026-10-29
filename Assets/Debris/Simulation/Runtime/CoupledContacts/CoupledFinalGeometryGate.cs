using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Final-pose contact discovery and independent hard-bound verdict stay on
    // the GPU. Callers consume Decision[0] before publishing a solver tick.
    public sealed class CoupledFinalGeometryGate : IDisposable
    {
        readonly CoupledContactManifoldBatch manifolds;
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly GraphicsBuffer decision;
        readonly int decide;
        bool disposed;
        public GraphicsBuffer Decision=>decision;
        public long BufferBytes=>32;

        public CoupledFinalGeometryGate(CoupledContactManifoldBatch manifolds)
        {
            this.manifolds=manifolds??throw new ArgumentNullException(nameof(manifolds));
            arena=manifolds.Arena;
            string owner=arena.NewOwner("FinalGeometryGate");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledFinalGeometryGate");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledFinalGeometryGate compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                decision=arena.Allocate(owner+"/0",8,4);
                decide=shader.FindKernel("DecideFinalGeometry");
                shader.SetInt("_CheckContainment",
                    manifolds.ContainmentEnabled?1:0);
                shader.SetBuffer(decide,"_ValidationFacts",
                    manifolds.ValidationFactsBuffer);
                shader.SetBuffer(decide,"_BroadFault",
                    manifolds.BroadFaultBuffer);
                shader.SetBuffer(decide,"_CompactFault",
                    manifolds.CompactFaultBuffer);
                shader.SetBuffer(decide,"_FirstContained",
                    manifolds.FirstContainedBuffer??
                    manifolds.ValidationFactsBuffer);
                shader.SetBuffer(decide,"_Decision",decision);
            }
            catch { Dispose();throw; }
        }

        public void RecordRequery(CommandBuffer target,GraphicsBuffer updatedPrimitives,
            GraphicsBuffer updatedUnionGroups=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledFinalGeometryGate));
            if(target==null)throw new ArgumentNullException(nameof(target));
            manifolds.RecordRequery(target,updatedPrimitives,updatedUnionGroups);
            target.DispatchCompute(shader,decide,1,1,1);
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            if(decision!=null)arena.Release(decision);
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
