using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // One restarted GMRES(16) Arnoldi cycle. It owns 17 basis vectors, two
    // work vectors and fixed-order scalar reductions; no second z-basis.
    // Scalar Givens/back-substitution reconstructs a direction. GPU control
    // masks later recorded columns after convergence, failure or budget stop.
    public sealed class CoupledArnoldiWorkspace : IDisposable
    {
        public const int RestartLength=16;
        readonly CoupledContactOperator op;
        readonly CoupledVelocityResidual velocity;
        readonly CoupledManifoldPreconditioner preconditioner;
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer basis,zWork,wWork,deltaTotal,dotBlocks,dotCoefficients,
            normBlocks,hessenberg,columnNorm,stepStatus,rotCos,rotSin,
            rotRhs,smallY,estimate,solveStatus,workControl,verificationGate,
            acceptedDirectionGate;
        readonly int initialize,resetStep,copyZ,copyDerivative,dot,finalizeDots,
            subtract,norm,finalizeNorm,normalize,initializeRotations,givens,
            solveUpper,buildCombination,clearDelta,accumulateDelta,copyDelta,
            addRegularization,initializeControl,advanceControl,
            prepareTrueVerification,inspectTrue,beginRetry,
            resetCycleControl;
        readonly int rowCapacity,blockCapacity,rowGroups;
        int nextColumn;
        float regularizationEta;
        bool disposed,initialized,directionRecorded;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer Basis=>basis;
        public GraphicsBuffer Hessenberg=>hessenberg;
        public GraphicsBuffer StepStatus=>stepStatus;
        public GraphicsBuffer ColumnNorm=>columnNorm;
        public GraphicsBuffer EstimatedRelativeResidual=>estimate;
        public GraphicsBuffer SolveStatus=>solveStatus;
        // Gate, counted Krylov products, active columns, stop reason, cap,
        // highest search-only regularization stage attempted.
        public GraphicsBuffer WorkControl=>workControl;
        // One after the actual relative linear check passes; zero otherwise.
        public GraphicsBuffer VerifiedDirectionGate=>verificationGate;
        // Latched true-pass gate for the line search after masked restart slots.
        public GraphicsBuffer AcceptedDirectionGate=>acceptedDirectionGate;
        internal CoupledContactOperator Operator=>op;
        internal CoupledVelocityResidual Velocity=>velocity;

        public CoupledArnoldiWorkspace(CoupledContactOperator op,
            CoupledVelocityResidual velocity,
            CoupledManifoldPreconditioner preconditioner)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            this.velocity=velocity??throw new ArgumentNullException(nameof(velocity));
            this.preconditioner=preconditioner??
                throw new ArgumentNullException(nameof(preconditioner));
            if(velocity.Operator!=op||preconditioner.Operator!=op||
               preconditioner.Velocity!=velocity)
                throw new ArgumentException("Arnoldi requires one frozen graph and residual");
            arena=op.Arena;
            arenaOwner=arena.NewOwner("Arnoldi");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledArnoldi");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledArnoldi compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                rowCapacity=Math.Max(1,op.RowCount);
                blockCapacity=Math.Max(1,(rowCapacity+255)/256);
                rowGroups=Math.Max(1,(rowCapacity+255)/256);
                basis=Buffer((RestartLength+1)*rowCapacity,4);
                zWork=Buffer(rowCapacity,4);
                wWork=Buffer(rowCapacity,4);
                deltaTotal=Buffer(rowCapacity,4);
                dotBlocks=Buffer((RestartLength+1)*blockCapacity,4);
                dotCoefficients=Buffer(RestartLength+1,4);
                normBlocks=Buffer(blockCapacity,4);
                hessenberg=Buffer((RestartLength+1)*RestartLength,4);
                columnNorm=Buffer(RestartLength,4);
                stepStatus=Buffer(RestartLength,4);
                rotCos=Buffer(RestartLength,4);
                rotSin=Buffer(RestartLength,4);
                rotRhs=Buffer(RestartLength+1,4);
                smallY=Buffer(RestartLength,4);
                estimate=Buffer(1,4);
                solveStatus=Buffer(1,4);
                workControl=Buffer(6,4);
                verificationGate=Buffer(1,4);
                acceptedDirectionGate=Buffer(1,4);
                shader.SetInt("_RowCapacity",op.RowCount);
                shader.SetInt("_BlockCapacity",blockCapacity);
                initialize=shader.FindKernel("InitializeBasis");
                initializeRotations=shader.FindKernel("InitializeRotations");
                initializeControl=shader.FindKernel("InitializeWorkControl");
                advanceControl=shader.FindKernel("AdvanceWorkControl");
                prepareTrueVerification=shader.FindKernel("PrepareTrueVerification");
                inspectTrue=shader.FindKernel("InspectTrueResidual");
                resetCycleControl=shader.FindKernel("ResetCycleControl");
                beginRetry=shader.FindKernel("BeginRegularizedRetry");
                clearDelta=shader.FindKernel("ClearAccumulatedDirection");
                accumulateDelta=shader.FindKernel("AccumulateDirection");
                copyDelta=shader.FindKernel("CopyAccumulatedDirection");
                resetStep=shader.FindKernel("ResetStep");
                copyZ=shader.FindKernel("CopyPreconditionedDirection");
                copyDerivative=shader.FindKernel("CopyDerivativeToWork");
                addRegularization=shader.FindKernel("AddSearchRegularization");
                dot=shader.FindKernel("DotBlocks");
                finalizeDots=shader.FindKernel("FinalizeDots");
                subtract=shader.FindKernel("SubtractProjection");
                norm=shader.FindKernel("NormBlocks");
                finalizeNorm=shader.FindKernel("FinalizeNorm");
                normalize=shader.FindKernel("NormalizeNext");
                givens=shader.FindKernel("ApplyGivens");
                solveUpper=shader.FindKernel("SolveUpper");
                buildCombination=shader.FindKernel("BuildDirectionCombination");
                Bind(initialize,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_CycleResidual",velocity.Residual),
                    ("_CycleMerit",velocity.BaseMerit),("_Basis",basis),
                    ("_WorkControl",workControl));
                Bind(initializeRotations,("_CycleMerit",velocity.BaseMerit),
                    ("_RotCos",rotCos),("_RotSin",rotSin),
                    ("_RotRhs",rotRhs),("_SmallY",smallY),
                    ("_Estimate",estimate),("_SolveStatus",solveStatus),
                    ("_WorkControl",workControl));
                Bind(initializeControl,("_CycleMerit",velocity.BaseMerit),
                    ("_WorkControl",workControl),
                    ("_VerificationGate",verificationGate),
                    ("_AcceptedDirectionGate",acceptedDirectionGate),
                    ("_InitialGate",workControl));
                Bind(advanceControl,("_StepStatus",stepStatus),
                    ("_Estimate",estimate),("_WorkControl",workControl));
                Bind(prepareTrueVerification,("_SolveStatus",solveStatus),
                    ("_WorkControl",workControl),
                    ("_VerificationGate",verificationGate));
                Bind(inspectTrue,("_TrueLinearResult",velocity.LinearResult),
                    ("_SolveStatus",solveStatus),("_WorkControl",workControl),
                    ("_VerificationGate",verificationGate),
                    ("_AcceptedDirectionGate",acceptedDirectionGate));
                Bind(resetCycleControl,("_WorkControl",workControl),
                    ("_VerificationGate",verificationGate));
                Bind(beginRetry,("_TrialSelection",velocity.TrialSelection),
                    ("_WorkControl",workControl),
                    ("_VerificationGate",verificationGate),
                    ("_AcceptedDirectionGate",acceptedDirectionGate),
                    ("_ProjectionStatus",velocity.ProjectionStatus));
                Bind(clearDelta,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_AccumulatedDirection",deltaTotal),
                    ("_WorkControl",workControl));
                Bind(accumulateDelta,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_AccumulatedDirection",deltaTotal),
                    ("_Correction",zWork),
                    ("_VerificationGate",verificationGate));
                Bind(copyDelta,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_AccumulatedDirection",deltaTotal),
                    ("_ProductInput",op.ProductInput),
                    ("_VerificationGate",verificationGate));
                Bind(resetStep,("_StepStatus",stepStatus),
                    ("_WorkControl",workControl));
                Bind(copyZ,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Preconditioned",zWork),
                    ("_ProductInput",op.ProductInput),
                    ("_WorkControl",workControl));
                Bind(copyDerivative,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Derivative",velocity.Derivative),("_Work",wWork),
                    ("_WorkControl",workControl));
                Bind(addRegularization,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Preconditioned",zWork),("_Work",wWork),
                    ("_WorkControl",workControl));
                Bind(dot,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Basis",basis),("_Work",wWork),
                    ("_DotBlocks",dotBlocks),("_StepStatus",stepStatus),
                    ("_WorkControl",workControl));
                Bind(finalizeDots,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_DotBlocks",dotBlocks),
                    ("_DotCoefficients",dotCoefficients),
                    ("_Hessenberg",hessenberg),("_StepStatus",stepStatus),
                    ("_WorkControl",workControl));
                Bind(subtract,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Basis",basis),("_Work",wWork),
                    ("_DotCoefficients",dotCoefficients),
                    ("_StepStatus",stepStatus),("_WorkControl",workControl));
                Bind(norm,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Work",wWork),("_NormBlocks",normBlocks),
                    ("_StepStatus",stepStatus),("_WorkControl",workControl));
                Bind(finalizeNorm,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_NormBlocks",normBlocks),
                    ("_ColumnNorm",columnNorm),
                    ("_Hessenberg",hessenberg),("_StepStatus",stepStatus),
                    ("_WorkControl",workControl));
                Bind(normalize,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Work",wWork),("_ColumnNorm",columnNorm),
                    ("_StepStatus",stepStatus),("_Basis",basis),
                    ("_WorkControl",workControl));
                Bind(givens,("_StepStatus",stepStatus),
                    ("_Hessenberg",hessenberg),("_RotCos",rotCos),
                    ("_RotSin",rotSin),("_RotRhs",rotRhs),
                    ("_CycleMerit",velocity.BaseMerit),("_Estimate",estimate),
                    ("_WorkControl",workControl));
                Bind(solveUpper,("_StepStatus",stepStatus),
                    ("_Hessenberg",hessenberg),("_RotRhs",rotRhs),
                    ("_SmallY",smallY),("_SolveStatus",solveStatus),
                    ("_WorkControl",workControl));
                Bind(buildCombination,("_ActiveRowCount",op.ActiveRowCounter),
                    ("_Basis",basis),("_SmallY",smallY),
                    ("_SolveStatus",solveStatus),("_Work",wWork),
                    ("_WorkControl",workControl));
            }
            catch { Dispose();throw; }
        }

        GraphicsBuffer Buffer(int count,int stride)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+owned.Count,count,stride);
            owned.Add(buffer);BufferBytes+=(long)count*stride;
            return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] entries)
        {foreach(var item in entries)shader.SetBuffer(kernel,item.Name,item.Buffer);}

        // Base F and branches must already be current. The preconditioner is
        // fixed for this cycle; every following step uses its same factors.
        public void RecordInitialize(CommandBuffer target,float eta=0,
            int krylovCap=16,GraphicsBuffer activityGate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledArnoldiWorkspace));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!velocity.BaseReady)throw new InvalidOperationException(
                "Arnoldi needs a current base residual");
            if(eta!=0&&eta!=1e-5f&&eta!=1e-3f)
                throw new ArgumentOutOfRangeException(nameof(eta),
                    "V2 search retries use eta=0, 1e-5 or 1e-3");
            if(krylovCap!=16&&krylovCap!=24&&krylovCap!=32)
                throw new ArgumentOutOfRangeException(nameof(krylovCap));
            if(activityGate!=null&&
               (activityGate.stride!=4||activityGate.count<1))
                throw new ArgumentException("A GPU uint activity gate is required",
                    nameof(activityGate));
            regularizationEta=eta;
            preconditioner.RecordBuild(target,activityGate);
            target.SetComputeBufferParam(shader,initialize,"_CycleResidual",
                velocity.Residual);
            target.SetComputeBufferParam(shader,initialize,"_CycleMerit",
                velocity.BaseMerit);
            target.SetComputeBufferParam(shader,initializeRotations,"_CycleMerit",
                velocity.BaseMerit);
            target.SetComputeBufferParam(shader,givens,"_CycleMerit",
                velocity.BaseMerit);
            target.SetComputeIntParam(shader,"_KrylovCap",krylovCap);
            target.SetComputeIntParam(shader,"_UseInitialGate",
                activityGate==null?0:1);
            target.SetComputeBufferParam(shader,initializeControl,"_InitialGate",
                activityGate??workControl);
            target.SetComputeBufferParam(shader,initializeControl,"_CycleMerit",
                velocity.BaseMerit);
            target.DispatchCompute(shader,initializeControl,1,1,1);
            target.DispatchCompute(shader,clearDelta,rowGroups,1,1);
            target.DispatchCompute(shader,initialize,rowGroups,1,1);
            target.DispatchCompute(shader,initializeRotations,1,1,1);
            nextColumn=0;
            initialized=true;
            directionRecorded=false;
        }

        // The computed (H+eta*I)*delta+F becomes the next cycle's
        // right-hand side. The preconditioner and accumulated delta are kept.
        public void RecordRestart(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledArnoldiWorkspace));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!initialized||!directionRecorded)throw new InvalidOperationException(
                "A restart requires a true-residual evaluation from the prior cycle");
            target.SetComputeBufferParam(shader,initialize,"_CycleResidual",
                velocity.LinearResidualVector);
            target.SetComputeBufferParam(shader,initialize,"_CycleMerit",
                velocity.LinearResult);
            target.SetComputeBufferParam(shader,initializeRotations,"_CycleMerit",
                velocity.LinearResult);
            target.SetComputeBufferParam(shader,givens,"_CycleMerit",
                velocity.LinearResult);
            target.DispatchCompute(shader,resetCycleControl,1,1,1);
            target.DispatchCompute(shader,initialize,rowGroups,1,1);
            target.DispatchCompute(shader,initializeRotations,1,1,1);
            nextColumn=0;
            directionRecorded=false;
        }

        // Record either search-only shift after a failed direction or after
        // eight rejected trials. The GPU decides whether this retry is live;
        // its counted products and selected Krylov cap survive the retry.
        public void RecordRetry(CommandBuffer target,float eta,
            bool afterLineSearch)
        {RecordRetryCore(target,eta,afterLineSearch?1:0);}

        // Fixed schedules can record a retry after a masked line search.
        // The GPU accepts either a linear failure or eight rejected trials.
        public void RecordRetry(CommandBuffer target,float eta)
        {RecordRetryCore(target,eta,2);}

        void RecordRetryCore(CommandBuffer target,float eta,int retryMode)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledArnoldiWorkspace));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!initialized||!directionRecorded||!velocity.BaseReady)
                throw new InvalidOperationException(
                    "A regularized retry needs a recorded direction and base residual");
            if(eta!=1e-5f&&eta!=1e-3f)
                throw new ArgumentOutOfRangeException(nameof(eta));
            regularizationEta=eta;
            target.SetComputeIntParam(shader,"_RetryStage",eta==1e-5f?1:2);
            target.SetComputeIntParam(shader,"_RetryAfterLineSearch",retryMode);
            target.DispatchCompute(shader,beginRetry,1,1,1);
            target.SetComputeBufferParam(shader,initialize,"_CycleResidual",
                velocity.Residual);
            target.SetComputeBufferParam(shader,initialize,"_CycleMerit",
                velocity.BaseMerit);
            target.SetComputeBufferParam(shader,initializeRotations,"_CycleMerit",
                velocity.BaseMerit);
            target.SetComputeBufferParam(shader,givens,"_CycleMerit",
                velocity.BaseMerit);
            target.DispatchCompute(shader,clearDelta,rowGroups,1,1);
            target.DispatchCompute(shader,initialize,rowGroups,1,1);
            target.DispatchCompute(shader,initializeRotations,1,1,1);
            nextColumn=0;
            directionRecorded=false;
        }

        // One H*M^-1 Arnoldi product and two full GS passes. GPU work control
        // masks this column when an earlier recorded column stopped the cycle.
        public void RecordStep(CommandBuffer target,int column)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledArnoldiWorkspace));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!initialized||column!=nextColumn||column<0||column>=RestartLength)
                throw new InvalidOperationException("Arnoldi columns must be recorded in order");
            if(directionRecorded)throw new InvalidOperationException(
                "Restart before recording another Arnoldi column");
            target.SetComputeIntParam(shader,"_Column",column);
            target.DispatchCompute(shader,resetStep,1,1,1);
            preconditioner.RecordApply(target,basis,zWork,
                column*rowCapacity,0,workControl);
            target.DispatchCompute(shader,copyZ,rowGroups,1,1);
            velocity.RecordDerivativeMasked(target,workControl);
            target.DispatchCompute(shader,copyDerivative,rowGroups,1,1);
            target.SetComputeFloatParam(shader,"_RegularizationEta",regularizationEta);
            target.DispatchCompute(shader,addRegularization,rowGroups,1,1);
            for(int pass=0;pass<2;pass++)
            {
                target.SetComputeIntParam(shader,"_Pass",pass);
                target.DispatchCompute(shader,dot,blockCapacity,RestartLength+1,1);
                target.DispatchCompute(shader,finalizeDots,RestartLength+1,1,1);
                target.DispatchCompute(shader,subtract,rowGroups,1,1);
            }
            target.DispatchCompute(shader,norm,blockCapacity,1,1);
            target.DispatchCompute(shader,finalizeNorm,1,1,1);
            target.DispatchCompute(shader,normalize,rowGroups,1,1);
            target.DispatchCompute(shader,givens,1,1,1);
            target.DispatchCompute(shader,advanceControl,1,1,1);
            nextColumn++;
        }

        public void RecordDirectionAndVerify(CommandBuffer target)
        {RecordDirectionAndVerifyCore(target,0,true);}

        // Reconstruct deltaX=M^-1*sum(y_i*v_i), then verify the true frozen
        // search equation. Nonlinear line search remains unshifted.
        public void RecordDirectionAndVerify(CommandBuffer target,int columns)
        {RecordDirectionAndVerifyCore(target,columns,false);}

        void RecordDirectionAndVerifyCore(CommandBuffer target,int columns,
            bool useControlColumns)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledArnoldiWorkspace));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!initialized||nextColumn==0||(!useControlColumns&&
               (columns<1||columns>nextColumn)))
                throw new InvalidOperationException("Direction needs recorded Arnoldi columns");
            target.SetComputeIntParam(shader,"_UseControlColumns",
                useControlColumns?1:0);
            target.SetComputeIntParam(shader,"_SolveColumns",columns);
            target.DispatchCompute(shader,solveUpper,1,1,1);
            target.DispatchCompute(shader,prepareTrueVerification,1,1,1);
            target.DispatchCompute(shader,buildCombination,rowGroups,1,1);
            preconditioner.RecordApply(target,wWork,zWork,0,0,verificationGate);
            target.DispatchCompute(shader,accumulateDelta,rowGroups,1,1);
            target.DispatchCompute(shader,copyDelta,rowGroups,1,1);
            velocity.RecordDerivativeMasked(target,verificationGate);
            velocity.RecordLinearVerification(target,regularizationEta,
                verificationGate);
            target.DispatchCompute(shader,inspectTrue,1,1,1);
            directionRecorded=true;
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
