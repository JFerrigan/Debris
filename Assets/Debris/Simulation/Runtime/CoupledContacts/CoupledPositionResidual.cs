using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Arithmetic for one frozen normal-only position linearization. Geometry
    // refresh, Newton control and pose acceptance belong to the step solver.
    public sealed class CoupledPositionResidual : IDisposable
    {
        readonly CoupledContactOperator op;
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer residual,derivative,branches,facts,meritBlocks,merit;
        readonly int initialize,resetFacts,evaluate,reduce,finalize,applyDerivative;
        readonly int rowGroups,resetGroups;
        bool disposed;
        public GraphicsBuffer Residual=>residual;
        public GraphicsBuffer Derivative=>derivative;
        // [0] maximum physical residual, [1] first invalid row,
        // [2] maximum target-gap violation, [3] first tolerance failure.
        public GraphicsBuffer Facts=>facts;
        public GraphicsBuffer Merit=>merit;
        public long BufferBytes { get; private set; }

        public CoupledPositionResidual(CoupledContactOperator op,
            float physicalTolerance=1e-5f)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            if(!op.NormalOnlyRows||op.RowFeatures==null||
               op.SourcePrimitives==null||op.SourcePrimitiveCount<=0||
               float.IsNaN(physicalTolerance)||
               float.IsInfinity(physicalTolerance)||physicalTolerance<=0)
                throw new ArgumentException("Position residual requires classified normal-only geometry");
            arena=op.Arena;
            string owner=arena.NewOwner("PositionResidual");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledPositionResidual");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledPositionResidual compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                int rows=Math.Max(1,op.RowCount);
                rowGroups=Math.Max(1,(rows+63)/64);
                resetGroups=Math.Max(1,(Math.Max(rows,op.BodyCount)+63)/64);
                residual=Buffer(owner,rows,4);
                derivative=Buffer(owner,rows,4);
                branches=Buffer(owner,rows,4);
                facts=Buffer(owner,4,4);
                meritBlocks=Buffer(owner,Math.Max(1,(rows+255)/256),4);
                merit=Buffer(owner,1,4);
                shader.SetInt("_RowCapacity",op.RowCount);
                shader.SetInt("_BodyCount",op.BodyCount);
                shader.SetInt("_PrimitiveCount",op.SourcePrimitiveCount);
                shader.SetFloat("_PhysicalTolerance",physicalTolerance);
                initialize=shader.FindKernel("ResetPositionSystem");
                resetFacts=shader.FindKernel("ResetPositionFacts");
                evaluate=shader.FindKernel("EvaluatePositionResidual");
                reduce=shader.FindKernel("ReducePositionMerit");
                finalize=shader.FindKernel("FinalizePositionMerit");
                applyDerivative=shader.FindKernel("ApplyPositionDerivative");
                Bind(initialize,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_WritableProductInput",op.ProductInput),
                    ("_FreeMotion",op.FreeMotionInput),("_Facts",facts));
                Bind(resetFacts,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Facts",facts));
                Bind(evaluate,("_Rows",op.RowsBuffer),
                    ("_Features",op.RowFeatures),
                    ("_Primitives",op.SourcePrimitives),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_ProductInput",op.ProductInput),
                    ("_ProductOutput",op.ProductOutput),
                    ("_Residual",residual),("_Branches",branches),
                    ("_Facts",facts));
                Bind(reduce,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Residual",residual),("_MeritBlocks",meritBlocks));
                Bind(finalize,("_MeritBlocks",meritBlocks),
                    ("_Merit",merit),("_Facts",facts));
                Bind(applyDerivative,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_ProductInput",op.ProductInput),
                    ("_ProductOutput",op.ProductOutput),
                    ("_Branches",branches),("_Derivative",derivative),
                    ("_Facts",facts));
            }
            catch { Dispose();throw; }
        }

        GraphicsBuffer Buffer(string owner,int count,int stride)
        {
            var buffer=arena.Allocate(owner+"/"+owned.Count,count,stride);
            owned.Add(buffer);BufferBytes+=(long)count*stride;
            return buffer;
        }

        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        // Record after the frozen normal graph. A new geometry linearization
        // always starts with p=0 and has zero free displacement.
        public void RecordInitialize(CommandBuffer target)
        {
            Check(target);
            target.DispatchCompute(shader,initialize,resetGroups,1,1);
        }

        // ProductInput contains the current scaled position multiplier.
        public void RecordResidual(CommandBuffer target)
        {
            Check(target);
            target.DispatchCompute(shader,resetFacts,1,1,1);
            op.RecordProduct(target);
            target.DispatchCompute(shader,evaluate,rowGroups,1,1);
            target.DispatchCompute(shader,reduce,
                Math.Max(1,(op.RowCount+255)/256),1,1);
            target.DispatchCompute(shader,finalize,1,1,1);
        }

        // ProductInput now contains z; the preceding residual supplied the
        // branch mask. The caller must preserve x before writing z.
        public void RecordDerivative(CommandBuffer target)
        {
            Check(target);
            op.RecordProduct(target);
            target.DispatchCompute(shader,applyDerivative,rowGroups,1,1);
        }

        void Check(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPositionResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            foreach(var buffer in owned)arena.Release(buffer);
            owned.Clear();
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
