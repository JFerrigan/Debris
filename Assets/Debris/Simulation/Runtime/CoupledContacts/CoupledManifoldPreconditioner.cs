using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Fixed local right preconditioner for the frozen velocity derivative.
    // Each generated manifold owns one normal/tangent pair or two point pairs.
    public sealed class CoupledManifoldPreconditioner : IDisposable
    {
        readonly CoupledContactOperator op;
        readonly CoupledVelocityResidual velocity;
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer factors,pivotCodes,fault,alwaysGate;
        readonly int reset,build,apply;
        bool disposed,built;
        public long BufferBytes { get; private set; }
        // Fault[0]: 1=invalid manifold grouping, 2=invalid coefficients/vector.
        // Fault[1]: count of pivoted-LU blocks using shifted diagonal fallback.
        public GraphicsBuffer Fault=>fault;
        internal CoupledContactOperator Operator=>op;
        internal CoupledVelocityResidual Velocity=>velocity;

        public CoupledManifoldPreconditioner(CoupledContactOperator op,
            CoupledVelocityResidual velocity)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            this.velocity=velocity??throw new ArgumentNullException(nameof(velocity));
            if(velocity.Operator!=op)
                throw new ArgumentException("Preconditioner and residual need one frozen graph");
            arena=op.Arena;
            arenaOwner=arena.NewOwner("ManifoldPreconditioner");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledManifoldPreconditioner");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledManifoldPreconditioner compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                int rows=Math.Max(1,op.RowCount);
                factors=Buffer(rows,16);
                pivotCodes=Buffer(Math.Max(1,op.NormalOnlyRows?rows:rows/2),4);
                fault=Buffer(2,4);
                alwaysGate=Buffer(1,4);
                alwaysGate.SetData(new uint[]{1});
                shader.SetInt("_RowCapacity",op.RowCount);
                shader.SetInt("_BodyCount",op.BodyCount);
                shader.SetInt("_NormalOnly",op.NormalOnlyRows?1:0);
                shader.SetFloat("_Friction",velocity.Friction);
                reset=shader.FindKernel("ResetPreconditioner");
                build=shader.FindKernel("FactorManifolds");
                apply=shader.FindKernel("ApplyManifoldFactors");
                Bind(reset,("_Fault",fault),("_WorkGate",alwaysGate));
                Bind(build,("_Bodies",op.BodiesBuffer),
                    ("_Rows",op.RowsBuffer),
                    ("_RowManifoldIds",op.RowManifoldIds),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Branches",velocity.Branches),
                    ("_Factors",factors),("_PivotCodes",pivotCodes),
                    ("_Fault",fault),("_WorkGate",alwaysGate));
                Bind(apply,("_RowManifoldIds",op.RowManifoldIds),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Factors",factors),("_PivotCodes",pivotCodes),
                    ("_Fault",fault),("_WorkGate",alwaysGate));
            }
            catch { Dispose();throw; }
        }

        GraphicsBuffer Buffer(int count,int stride)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+owned.Count,count,stride);
            owned.Add(buffer);
            BufferBytes+=(long)count*stride;
            return buffer;
        }

        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var item in bindings)shader.SetBuffer(kernel,item.Name,item.Buffer);}

        // Record after a base residual on the current graph. The exact local
        // H block is built from current branch bits, contact rows and masses.
        public void RecordBuild(CommandBuffer target,GraphicsBuffer gate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledManifoldPreconditioner));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!velocity.BaseReady)throw new InvalidOperationException(
                "Build the preconditioner after a current base residual");
            if(gate!=null&&(gate.stride!=4||gate.count<1))
                throw new ArgumentException("A GPU uint work gate is required",
                    nameof(gate));
            target.SetComputeIntParam(shader,"_UseWorkGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,reset,"_WorkGate",
                gate??alwaysGate);
            target.SetComputeBufferParam(shader,build,"_WorkGate",
                gate??alwaysGate);
            target.DispatchCompute(shader,reset,1,1,1);
            target.DispatchCompute(shader,build,velocity.PointArgs,0u);
            built=true;
        }

        // In a GMRES Arnoldi step, output=M^-1*input. Both vectors stay on GPU.
        public void RecordApply(CommandBuffer target,GraphicsBuffer input,
            GraphicsBuffer output,int inputOffset=0,int outputOffset=0,
            GraphicsBuffer gate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledManifoldPreconditioner));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!built)throw new InvalidOperationException(
                "Build the local factors before applying them");
            if(input==null||output==null||input==output||
               input.stride!=4||output.stride!=4||
               inputOffset<0||outputOffset<0||
               (long)inputOffset+op.RowCount>input.count||
               (long)outputOffset+op.RowCount>output.count)
                throw new ArgumentException("Distinct full-row float vectors are required");
            if(gate!=null&&(gate.stride!=4||gate.count<1))
                throw new ArgumentException("A GPU uint work gate is required",nameof(gate));
            target.SetComputeIntParam(shader,"_UseWorkGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,apply,"_WorkGate",gate??alwaysGate);
            target.SetComputeIntParam(shader,"_InputOffset",inputOffset);
            target.SetComputeIntParam(shader,"_OutputOffset",outputOffset);
            target.SetComputeBufferParam(shader,apply,"_VectorInput",input);
            target.SetComputeBufferParam(shader,apply,"_VectorOutput",output);
            target.DispatchCompute(shader,apply,velocity.PointArgs,0u);
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            foreach(var buffer in owned)arena.Release(buffer);
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
