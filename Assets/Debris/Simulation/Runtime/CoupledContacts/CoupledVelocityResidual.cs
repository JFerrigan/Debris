using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // V2-2 projected-law arithmetic on the V2-1 frozen graph. Normal-only
    // position systems reuse the bounded search machinery with a gap RHS.
    // The caller owns
    // the operator and records its graph once before repeated products.
    public sealed class CoupledVelocityResidual : IDisposable
    {
        readonly CoupledContactOperator op;
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly float friction;
        readonly string arenaOwner;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer residual,derivative,projected,branches,facts,args,
            meritBlocks,merit,meritArgs,baseX,baseY,searchZ,searchBz,
            savedFree,baseMerit,trialMerits,trialValid,trialPhysical,trialSelection,
            alwaysGate,projectionGate,projectionStatus;
        readonly GraphicsBuffer linearResidual,linearBlocks,linearResult;
        readonly int resetPosition,prepare,evaluate,snapshotBase,snapshotDirection,evaluateTrial,
            snapshotFree,clearFree,restoreFree,storeBaseMerit,storeTrial,
            selectTrial,applySelected,applyDerivative,project,copyProjected,
            reduceMerit,finalizeMerit,formLinear,reduceLinear,finalizeLinear,
            selectProjection,inspectProjection,resetProjection;
        readonly int[] lineGateKernels;
        bool disposed,prepared,directionReady,projectedReady,
            gatedProjectionReady;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer ImpulseInput=>op.ProductInput;
        public GraphicsBuffer FreeMotionInput=>op.FreeMotionInput;
        public GraphicsBuffer Residual=>residual;
        internal GraphicsBuffer Branches=>branches;
        internal GraphicsBuffer PointArgs=>args;
        internal GraphicsBuffer BaseMerit=>baseMerit;
        internal GraphicsBuffer LinearResidualVector=>linearResidual;
        internal CoupledContactOperator Operator=>op;
        internal bool BaseReady=>prepared;
        internal float Friction=>friction;
        internal bool PositionSystem=>op.NormalOnlyRows;
        public GraphicsBuffer Merit=>merit;
        public GraphicsBuffer Derivative=>derivative;
        // [0]=true half-squared linear residual, [1]=relative L2 norm,
        // [2]=valid, [3]=relative norm <= .1, stored as floats.
        public GraphicsBuffer LinearResult=>linearResult;
        public GraphicsBuffer ProjectedImpulse=>projected;
        // Gate is one only after the projected true residual passes every
        // physical bound. Status: 0=ineligible, 1=pending, 2=pass, 3=fail.
        public GraphicsBuffer ProjectionGate=>projectionGate;
        public GraphicsBuffer ProjectionStatus=>projectionStatus;
        // Selection[0] is the first accepted trial index, or uint.MaxValue;
        // Selection[1] is 1 for Armijo and 2 for the physical stop.
        public GraphicsBuffer TrialSelection=>trialSelection;
        // Facts: normal/tangent max residual as float bits; first invalid
        // point/row; max direct impulse, speed, complementarity and friction
        // violations as float bits.
        public GraphicsBuffer Facts=>facts;

        public CoupledVelocityResidual(CoupledContactOperator op,float substepDuration,
            float friction=.3f,float physicalTolerance=1e-5f)
            :this(op,substepDuration,friction,physicalTolerance,false){}

        internal CoupledVelocityResidual(CoupledContactOperator op,
            float substepDuration,float friction,float physicalTolerance,
            bool positionSystem)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            this.friction=friction;
            arena=op.Arena;
            arenaOwner=arena.NewOwner("VelocityResidual");
            try
            {
                if(!Finite(substepDuration)||substepDuration<=0||
                   !Finite(friction)||friction<0||
                   !Finite(physicalTolerance)||physicalTolerance<=0||
                   op.RowCount>CoupledContactRowBatch.MaxRows||
                   op.NormalOnlyRows!=positionSystem||
                   (!positionSystem&&(op.RowCount&1)!=0)||
                   (positionSystem&&(op.RowFeatures==null||
                       op.SourcePrimitives==null||op.SourcePrimitiveCount<=0)))
                    throw new ArgumentException("Invalid V2 velocity residual contract");
                var asset=Resources.Load<ComputeShader>("CoupledVelocityResidual");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledVelocityResidual compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                int rowCapacity=Math.Max(1,op.RowCount);
                int pointCapacity=Math.Max(1,positionSystem?rowCapacity:rowCapacity/2);
                residual=Buffer(rowCapacity,4);
                derivative=Buffer(rowCapacity,4);
                projected=Buffer(rowCapacity,4);
                branches=Buffer(pointCapacity,4);
                facts=Buffer(8,4);
                args=Buffer(3,4,GraphicsBuffer.Target.Structured|
                    GraphicsBuffer.Target.IndirectArguments);
                args.SetData(new uint[]{1,1,1});
                meritBlocks=Buffer(Math.Max(1,(rowCapacity+255)/256),4);
                merit=Buffer(1,4);
                meritArgs=Buffer(3,4,GraphicsBuffer.Target.Structured|
                    GraphicsBuffer.Target.IndirectArguments);
                meritArgs.SetData(new uint[]{1,1,1});
                baseX=Buffer(rowCapacity,4);
                baseY=Buffer(rowCapacity,4);
                searchZ=Buffer(rowCapacity,4);
                searchBz=Buffer(rowCapacity,4);
                savedFree=Buffer(Math.Max(1,op.BodyCount),16);
                baseMerit=Buffer(1,4);
                trialMerits=Buffer(8,4);
                trialValid=Buffer(8,4);
                trialPhysical=Buffer(8,4);
                trialSelection=Buffer(2,4);
                alwaysGate=Buffer(1,4);
                alwaysGate.SetData(new uint[]{1});
                projectionGate=Buffer(1,4);
                projectionStatus=Buffer(1,4);
                linearResidual=Buffer(rowCapacity,4);
                linearBlocks=Buffer(Math.Max(1,(rowCapacity+255)/256),4);
                linearResult=Buffer(4,4);
                shader.SetInt("_RowCapacity",op.RowCount);
                shader.SetInt("_BodyCount",op.BodyCount);
                shader.SetInt("_NormalOnly",positionSystem?1:0);
                shader.SetInt("_PrimitiveCount",op.SourcePrimitiveCount);
                shader.SetFloat("_StepDuration",substepDuration);
                shader.SetInt("_UseStepClock",0);
                shader.SetFloat("_Friction",friction);
                shader.SetFloat("_PhysicalTolerance",physicalTolerance);
                resetPosition=shader.FindKernel("ResetPositionInput");
                Bind(resetPosition,("_WritableProductInput",op.ProductInput),
                    ("_FreeMotion",op.FreeMotionInput),("_ProductGate",alwaysGate));
                prepare=shader.FindKernel("PrepareVelocityRows");
                evaluate=shader.FindKernel("EvaluateVelocityResidual");
                snapshotBase=shader.FindKernel("SnapshotVelocityBase");
                snapshotDirection=shader.FindKernel("SnapshotVelocityDirection");
                evaluateTrial=shader.FindKernel("EvaluateVelocityTrial");
                snapshotFree=shader.FindKernel("SnapshotFreeMotion");
                clearFree=shader.FindKernel("ClearFreeMotion");
                restoreFree=shader.FindKernel("RestoreFreeMotion");
                storeBaseMerit=shader.FindKernel("StoreBaseMerit");
                storeTrial=shader.FindKernel("StoreTrialOutcome");
                selectTrial=shader.FindKernel("SelectVelocityTrial");
                applySelected=shader.FindKernel("ApplySelectedTrial");
                applyDerivative=shader.FindKernel("ApplyVelocityDerivative");
                formLinear=shader.FindKernel("FormLinearResidual");
                reduceLinear=shader.FindKernel("ReduceLinearResidualBlocks");
                finalizeLinear=shader.FindKernel("FinalizeLinearResidual");
                project=shader.FindKernel("ProjectVelocityImpulse");
                copyProjected=shader.FindKernel("CopyProjectedToProduct");
                selectProjection=shader.FindKernel("SelectVelocityProjection");
                inspectProjection=shader.FindKernel("InspectVelocityProjection");
                resetProjection=shader.FindKernel("ResetVelocityProjection");
                reduceMerit=shader.FindKernel("ReduceResidualBlocks");
                finalizeMerit=shader.FindKernel("FinalizeResidualMerit");
                Bind(prepare,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Facts",facts),("_PointArgs",args),
                    ("_MeritArgs",meritArgs));
                Bind(evaluate,("_Rows",op.RowsBuffer),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_CurrentX",op.ProductInput),("_CurrentY",op.ProductOutput),
                    ("_Residual",residual),("_Branches",branches),("_Facts",facts));
                // Both shader modes need valid resource bindings on Metal.
                // Velocity never reads these one-element fallback records.
                var features=positionSystem?op.RowFeatures:Buffer(1,24);
                var primitives=positionSystem?op.SourcePrimitives:Buffer(1,56);
                foreach(int kernel in new[]{evaluate,evaluateTrial})
                    Bind(kernel,("_Features",features),("_Primitives",primitives),("_StepClock",alwaysGate));
                Bind(snapshotBase,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_CurrentX",op.ProductInput),("_CurrentY",op.ProductOutput),
                    ("_BaseX",baseX),("_BaseY",baseY));
                Bind(snapshotDirection,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_DirectionZ",op.ProductInput),("_ProductBz",op.ProductOutput),
                    ("_SearchZ",searchZ),("_SearchBz",searchBz),
                    ("_ProductGate",alwaysGate));
                Bind(snapshotFree,("_FreeMotion",op.FreeMotionInput),
                    ("_SavedFreeMotion",savedFree),
                    ("_ProductGate",alwaysGate));
                Bind(clearFree,("_FreeMotion",op.FreeMotionInput),("_ProductGate",alwaysGate));
                Bind(restoreFree,("_FreeMotion",op.FreeMotionInput),
                    ("_SavedFreeMotion",savedFree),("_ProductGate",alwaysGate));
                Bind(storeBaseMerit,("_Facts",facts),("_Merit",merit),
                    ("_BaseMerit",baseMerit));
                Bind(storeTrial,("_Facts",facts),("_Merit",merit),
                    ("_TrialMerits",trialMerits),("_TrialValid",trialValid),
                    ("_TrialPhysical",trialPhysical));
                Bind(selectTrial,("_BaseMerit",baseMerit),
                    ("_TrialMerits",trialMerits),("_TrialValid",trialValid),
                    ("_TrialPhysical",trialPhysical),
                    ("_TrialSelection",trialSelection));
                Bind(applySelected,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_BaseX",baseX),("_SearchZ",searchZ),
                    ("_TrialSelection",trialSelection),
                    ("_WritableProductInput",op.ProductInput));
                Bind(evaluateTrial,("_Rows",op.RowsBuffer),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_BaseX",baseX),("_BaseY",baseY),
                    ("_SearchZ",searchZ),("_SearchBz",searchBz),
                    ("_Residual",residual),("_Branches",branches),("_Facts",facts));
                Bind(applyDerivative,("_Rows",op.RowsBuffer),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_DirectionZ",op.ProductInput),("_ProductBz",op.ProductOutput),
                    ("_Branches",branches),("_Derivative",derivative),
                    ("_Facts",facts),("_ProductGate",alwaysGate));
                Bind(formLinear,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Derivative",derivative),("_Residual",residual),
                    ("_SearchZ",searchZ),
                    ("_LinearResidual",linearResidual),("_Facts",facts),
                    ("_ProductGate",alwaysGate));
                Bind(reduceLinear,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_LinearResidual",linearResidual),
                    ("_LinearBlocks",linearBlocks),("_Facts",facts),
                    ("_ProductGate",alwaysGate));
                Bind(finalizeLinear,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_LinearBlocks",linearBlocks),("_BaseMerit",baseMerit),
                    ("_Facts",facts),("_LinearResult",linearResult),
                    ("_ProductGate",alwaysGate));
                Bind(project,("_Rows",op.RowsBuffer),
                    ("_ActiveRowCount",op.ActiveRowCounter),
                    ("_CurrentX",op.ProductInput),("_ProjectedX",projected),
                    ("_Facts",facts),("_ProductGate",alwaysGate));
                Bind(copyProjected,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_ProjectedX",projected),
                    ("_WritableProductInput",op.ProductInput),
                    ("_ProductGate",alwaysGate));
                Bind(selectProjection,("_SelectionGate",alwaysGate),("_Facts",facts),
                    ("_BaseMerit",baseMerit),
                    ("_ProjectionGate",projectionGate),
                    ("_ProjectionStatus",projectionStatus));
                Bind(inspectProjection,("_Facts",facts),
                    ("_BaseMerit",baseMerit),
                    ("_ProjectionGate",projectionGate),
                    ("_ProjectionStatus",projectionStatus));
                Bind(resetProjection,("_ProjectionGate",projectionGate),
                    ("_ProjectionStatus",projectionStatus),
                    ("_TrialSelection",trialSelection),
                    ("_ProductGate",alwaysGate));
                Bind(reduceMerit,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Residual",residual),("_ResidualBlocks",meritBlocks),
                    ("_Facts",facts));
                Bind(finalizeMerit,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_ResidualBlocks",meritBlocks),("_Merit",merit),
                    ("_Facts",facts));
                lineGateKernels=new[]{prepare,evaluate,snapshotBase,evaluateTrial,
                    storeBaseMerit,storeTrial,selectTrial,applySelected,
                    reduceMerit,finalizeMerit,restoreFree};
                foreach(int kernel in lineGateKernels)
                    shader.SetBuffer(kernel,"_ProductGate",alwaysGate);
            }
            catch { Dispose();throw; }
        }

        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        GraphicsBuffer Buffer(int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var result=arena.Allocate(arenaOwner+"/"+owned.Count,count,stride,target);
            owned.Add(result);BufferBytes+=(long)count*stride;
            return result;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] entries)
        {foreach(var entry in entries)shader.SetBuffer(kernel,entry.Name,entry.Buffer);}

        internal void RecordStepClock(CommandBuffer target,GraphicsBuffer clock)
        {
            if(clock==null||clock.stride!=4||clock.count<2)throw new ArgumentException("Invalid GPU step clock");
            target.SetComputeIntParam(shader,"_UseStepClock",1);
            foreach(int kernel in new[]{evaluate,evaluateTrial})
                target.SetComputeBufferParam(shader,kernel,"_StepClock",clock);
        }

        internal void RecordPositionInitialize(CommandBuffer target,GraphicsBuffer gate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!PositionSystem)throw new InvalidOperationException("Requires a position graph");
            target.SetComputeIntParam(shader,"_UseProductGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,resetPosition,"_ProductGate",gate??alwaysGate);
            target.DispatchCompute(shader,resetPosition,
                Math.Max(1,(Math.Max(op.RowCount,op.BodyCount)+63)/64),1,1);
            prepared=false;
            directionReady=false;
        }

        public void RecordGraph(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            prepared=false;
            directionReady=false;
            projectedReady=false;
            gatedProjectionReady=false;
            op.RecordGraph(target);
        }

        // ProductInput contains current scaled impulse; FreeMotionInput is the
        // immutable free state. This records a true product and then F(x).
        public void RecordResidual(CommandBuffer target)
        {RecordResidualCore(target,null);}

        // A zero GPU activity gate preserves the last accepted candidate and
        // all solve facts while skipping the base operator product.
        public void RecordResidualMasked(CommandBuffer target,GraphicsBuffer gate)
        {
            if(gate==null||gate.stride!=4||gate.count<1)
                throw new ArgumentException("A GPU uint activity gate is required",
                    nameof(gate));
            RecordResidualCore(target,gate);
        }

        void RecordResidualCore(CommandBuffer target,GraphicsBuffer gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            target.SetComputeIntParam(shader,"_UseProductGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,resetProjection,"_ProductGate",
                gate??alwaysGate);
            target.SetComputeBufferParam(shader,snapshotFree,"_ProductGate",
                gate??alwaysGate);
            foreach(int kernel in lineGateKernels)
                target.SetComputeBufferParam(shader,kernel,"_ProductGate",
                    gate??alwaysGate);
            target.DispatchCompute(shader,resetProjection,1,1,1);
            target.DispatchCompute(shader,snapshotFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            if(gate==null)op.RecordProduct(target);
            else op.RecordProductMasked(target,gate);
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,snapshotBase,args,0u);
            target.DispatchCompute(shader,evaluate,args,0u);
            target.DispatchCompute(shader,reduceMerit,meritArgs,0u);
            target.DispatchCompute(shader,finalizeMerit,1,1,1);
            target.DispatchCompute(shader,storeBaseMerit,1,1,1);
            prepared=true;
            directionReady=false;
            projectedReady=false;
            gatedProjectionReady=false;
        }

        // ProductInput now contains z. The saved free motion is cleared on
        // GPU for B*z; branches come from the last frozen-graph residual.
        public void RecordDerivative(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!prepared)throw new InvalidOperationException(
                "V2 derivative requires a residual on the frozen graph");
            target.SetComputeIntParam(shader,"_UseProductGate",0);
            target.SetComputeBufferParam(shader,applyDerivative,
                "_ProductGate",alwaysGate);
            target.SetComputeBufferParam(shader,snapshotDirection,
                "_ProductGate",alwaysGate);
            target.SetComputeBufferParam(shader,clearFree,"_ProductGate",alwaysGate);
            target.SetComputeBufferParam(shader,restoreFree,"_ProductGate",alwaysGate);
            target.DispatchCompute(shader,clearFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            op.RecordProduct(target);
            target.DispatchCompute(shader,applyDerivative,args,0u);
            target.DispatchCompute(shader,snapshotDirection,args,0u);
            target.DispatchCompute(shader,restoreFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            directionReady=true;
        }

        // A zero GPU gate skips the expensive operator product and leaves the
        // derivative/search snapshots unchanged. A controller must not consume
        // those buffers until it has established that the gate was nonzero.
        public void RecordDerivativeMasked(CommandBuffer target,GraphicsBuffer gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!prepared)throw new InvalidOperationException(
                "V2 derivative requires a residual on the frozen graph");
            if(gate==null||gate.stride!=4||gate.count<1)
                throw new ArgumentException("A GPU uint product gate is required",nameof(gate));
            target.SetComputeIntParam(shader,"_UseProductGate",1);
            target.SetComputeBufferParam(shader,applyDerivative,"_ProductGate",gate);
            target.SetComputeBufferParam(shader,snapshotDirection,"_ProductGate",gate);
            target.SetComputeBufferParam(shader,clearFree,"_ProductGate",gate);
            target.SetComputeBufferParam(shader,restoreFree,"_ProductGate",gate);
            target.DispatchCompute(shader,clearFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            op.RecordProductMasked(target,gate);
            target.DispatchCompute(shader,applyDerivative,args,0u);
            target.DispatchCompute(shader,snapshotDirection,args,0u);
            target.DispatchCompute(shader,restoreFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            directionReady=true;
        }

        // The caller may record any alpha from the fixed eight-trial schedule.
        // The trial reuses one B*z product and leaves the operator input and
        // endpoint buffers untouched. Merit and Facts describe this trial.
        public void RecordTrial(CommandBuffer target,int trialIndex)
        {
            if(target==null)throw new ArgumentNullException(nameof(target));
            target.SetComputeIntParam(shader,"_UseProductGate",0);
            RecordTrialCore(target,trialIndex);
        }

        void RecordTrialCore(CommandBuffer target,int trialIndex)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!directionReady)throw new InvalidOperationException(
                "V2 trial requires a residual and direction on the frozen graph");
            if(trialIndex<0||trialIndex>7)throw new ArgumentOutOfRangeException(nameof(trialIndex));
            target.SetComputeFloatParam(shader,"_TrialAlpha",1f/(1<<trialIndex));
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,evaluateTrial,args,0u);
            target.DispatchCompute(shader,reduceMerit,meritArgs,0u);
            target.DispatchCompute(shader,finalizeMerit,1,1,1);
            prepared=false;
        }

        // A Hessenberg estimate is insufficient: this checks the actual
        // (H+eta*I)*deltaX+F vector after RecordDerivative using its true B*z.
        public void RecordLinearVerification(CommandBuffer target,float eta=0,
            GraphicsBuffer gate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!prepared||!directionReady)throw new InvalidOperationException(
                "V2 linear verification requires a base residual and derivative");
            if(!Finite(eta)||eta<0)throw new ArgumentOutOfRangeException(nameof(eta));
            if(gate!=null&&(gate.stride!=4||gate.count<1))
                throw new ArgumentException("A GPU uint verification gate is required",
                    nameof(gate));
            target.SetComputeIntParam(shader,"_UseProductGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,formLinear,"_ProductGate",
                gate??alwaysGate);
            target.SetComputeBufferParam(shader,reduceLinear,"_ProductGate",
                gate??alwaysGate);
            target.SetComputeBufferParam(shader,finalizeLinear,"_ProductGate",
                gate??alwaysGate);
            target.SetComputeFloatParam(shader,"_LinearShift",eta);
            target.DispatchCompute(shader,formLinear,
                Math.Max(1,(op.RowCount+63)/64),1,1);
            target.DispatchCompute(shader,reduceLinear,meritArgs,0u);
            target.DispatchCompute(shader,finalizeLinear,1,1,1);
        }

        // All eight trials consume the one saved B*z product. The GPU selects
        // the first finite Armijo/physical winner and restores the saved free
        // motion before a true operator/residual recheck. A missing winner is
        // reported in TrialSelection; no caller may publish that result.
        public void RecordLineSearch(CommandBuffer target,GraphicsBuffer gate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!directionReady)throw new InvalidOperationException(
                "V2 line search requires a residual and direction on the frozen graph");
            if(gate!=null&&(gate.stride!=4||gate.count<1))
                throw new ArgumentException("A GPU uint direction gate is required",
                    nameof(gate));
            target.SetComputeIntParam(shader,"_UseProductGate",gate==null?0:1);
            foreach(int kernel in lineGateKernels)
                target.SetComputeBufferParam(shader,kernel,"_ProductGate",
                    gate??alwaysGate);
            for(int trial=0;trial<8;trial++)
            {
                RecordTrialCore(target,trial);
                target.SetComputeIntParam(shader,"_TrialIndex",trial);
                target.DispatchCompute(shader,storeTrial,1,1,1);
            }
            target.DispatchCompute(shader,selectTrial,1,1,1);
            target.DispatchCompute(shader,applySelected,args,0u);
            target.DispatchCompute(shader,restoreFree,
                Math.Max(1,(op.BodyCount+63)/64),1,1);
            if(gate==null)op.RecordProduct(target);
            else op.RecordProductMasked(target,gate);
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,snapshotBase,args,0u);
            target.DispatchCompute(shader,evaluate,args,0u);
            target.DispatchCompute(shader,reduceMerit,meritArgs,0u);
            target.DispatchCompute(shader,finalizeMerit,1,1,1);
            target.DispatchCompute(shader,storeBaseMerit,1,1,1);
            prepared=true;
            directionReady=false;
            projectedReady=false;
            gatedProjectionReady=false;
        }

        // ProductInput again contains the candidate. Projection alone is not
        // acceptance; RecordProjectedResidual recomputes true GPU motion/F.
        public void RecordProjection(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!prepared)throw new InvalidOperationException(
                "V2 projection requires a residual on the frozen graph");
            target.SetComputeIntParam(shader,"_UseProductGate",0);
            target.DispatchCompute(shader,project,args,0u);
            projectedReady=true;
            gatedProjectionReady=false;
        }

        // The current true residual and direct physical facts decide whether
        // projection is eligible. The subsequent recheck still decides pass.
        public void RecordProjectionIfPhysical(CommandBuffer target,GraphicsBuffer activityGate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!prepared)throw new InvalidOperationException(
                "V2 projection requires a current physical residual");
            if(activityGate!=null&&(activityGate.stride!=4||activityGate.count<1))
                throw new ArgumentException("A GPU uint activity gate is required",nameof(activityGate));
            target.SetComputeIntParam(shader,"_UseSelectionGate",activityGate==null?0:1);
            target.SetComputeBufferParam(shader,selectProjection,"_SelectionGate",activityGate??alwaysGate);
            target.DispatchCompute(shader,selectProjection,1,1,1);
            target.SetComputeIntParam(shader,"_UseProductGate",1);
            target.SetComputeBufferParam(shader,project,"_ProductGate",projectionGate);
            target.DispatchCompute(shader,project,args,0u);
            projectedReady=true;
            gatedProjectionReady=true;
        }

        public void RecordProjectedResidual(CommandBuffer target)
        {RecordProjectedResidualCore(target,null);}

        public void RecordProjectedResidualIfPhysical(CommandBuffer target)
        {
            if(!gatedProjectionReady)throw new InvalidOperationException(
                "Select physical projection before its gated recheck");
            RecordProjectedResidualCore(target,projectionGate);
        }

        void RecordProjectedResidualCore(CommandBuffer target,GraphicsBuffer gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocityResidual));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!projectedReady)throw new InvalidOperationException(
                "V2 projected residual requires a projected candidate");
            target.SetComputeIntParam(shader,"_UseProductGate",gate==null?0:1);
            target.SetComputeBufferParam(shader,copyProjected,"_ProductGate",
                gate??alwaysGate);
            foreach(int kernel in lineGateKernels)
                target.SetComputeBufferParam(shader,kernel,"_ProductGate",
                    gate??alwaysGate);
            target.DispatchCompute(shader,copyProjected,args,0u);
            if(gate==null)op.RecordProduct(target);
            else op.RecordProductMasked(target,gate);
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,snapshotBase,args,0u);
            target.DispatchCompute(shader,evaluate,args,0u);
            target.DispatchCompute(shader,reduceMerit,meritArgs,0u);
            target.DispatchCompute(shader,finalizeMerit,1,1,1);
            target.DispatchCompute(shader,storeBaseMerit,1,1,1);
            if(gate!=null)target.DispatchCompute(shader,inspectProjection,1,1,1);
            directionReady=false;
            projectedReady=false;
            gatedProjectionReady=false;
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
