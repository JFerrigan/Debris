using System;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;
using SolverGrain = Debris.Simulation.ParallelProof.LooseCell;

namespace Debris.Simulation
{
    // One owner, one outstanding placement readback. The transaction does not
    // download the grain population; it returns a single rejection word.
    internal sealed class CandidateTerrainTransaction : IDisposable
    {
        readonly ParallelGrainSolver solver;
        readonly MatterSession mirror;
        readonly ComputeShader shader;
        readonly CommandBuffer commands=new CommandBuffer {name="Candidate terrain transaction"};
        readonly GraphicsBuffer status=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4);
        CandidateTerrainState terrain;
        CandidateTerrainEdit edit;
        CandidateBoundaryBuilder.CandidateBoundaryReplacement replacement;
        SolverGrain grain;
        float grainMass;
        bool awaiting,disposed,faulted;
        uint placementStatus;
        int clear,validate,validateBodies,validateFragments,publish;
        public bool Busy=>awaiting||edit!=null;
        internal bool Faulted=>faulted;

        internal CandidateTerrainTransaction(ParallelGrainSolver solver,MatterSession mirror,CandidateTerrainState terrain)
        {
            this.solver=solver??throw new ArgumentNullException(nameof(solver));this.mirror=mirror??throw new ArgumentNullException(nameof(mirror));this.terrain=terrain??throw new ArgumentNullException(nameof(terrain));ValidateMirror();
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("CandidateTerrainEdits"));
            clear=shader.FindKernel("ClearPlacementStatus");validate=shader.FindKernel("ValidatePlacementGrains");validateBodies=shader.FindKernel("ValidatePlacementBodies");validateFragments=shader.FindKernel("ValidatePlacementFragments");publish=shader.FindKernel("PublishTerrainCell");
        }
        void ValidateMirror()
        {
            if(mirror.Field==null||mirror.Damage==null||mirror.Dirty==null||mirror.Hull==null||mirror.FragmentHull==null||mirror.Width!=terrain.Width||mirror.Side!=terrain.Side||mirror.ChunkSize!=terrain.ChunkSize||mirror.Origin!=terrain.Origin||mirror.Capacity!=solver.GrainCapacity)
                throw new InvalidOperationException("Candidate terrain mirror does not match the candidate authority.");
        }
        internal CandidateEditStatus Prepare(CandidateTerrainEdit value,CandidateBoundaryBuilder.CandidateBoundaryReplacement boundaries,float density)
        {
            if(disposed||faulted||Busy||value==null||!terrain.IsCurrent(value)||!float.IsFinite(density)||density<=0)return CandidateEditStatus.Unavailable;
            // Every admission check has to happen before recording the first
            // live write.  TryPublish is deliberately only an enqueue plus
            // host-fact publication, never a sequence of recoverable writes.
            if(value.Release)
            {
                if(boundaries==null||!CandidateTerrainState.IsSupportedGrainSquare(new Vector2(value.Cell.x+.5f,value.Cell.y+.5f)))return CandidateEditStatus.Unavailable;
                var boundaryStatus=solver.ValidateBoundaryReplacement(boundaries);
                if(boundaryStatus!=CandidateEditStatus.Released)return boundaryStatus;
                grainMass=density;grain=new SolverGrain{Center=new Vector2(value.Cell.x+.5f,value.Cell.y+.5f),Material=value.OldMaterial,Identity=value.Identity};
                var grainStatus=solver.ValidateAppend(grain,grainMass);
                if(grainStatus!=CandidateEditStatus.Released)return grainStatus;
            }
            edit=value;replacement=boundaries;grainMass=density;grain=new SolverGrain{Center=new Vector2(value.Cell.x+.5f,value.Cell.y+.5f),Material=value.OldMaterial,Identity=value.Identity};return value.Status;
        }
        internal void BeginPlacementValidation()
        {
            if(disposed||faulted||edit==null||awaiting)throw new InvalidOperationException("No prepared candidate edit.");
            commands.Clear();commands.SetComputeBufferParam(shader,clear,"_Status",status);commands.DispatchCompute(shader,clear,1,1,1);
            if(edit.Release){commands.SetComputeVectorParam(shader,"_Centre",grain.Center);commands.SetComputeFloatParam(shader,"_Tolerance",.0001f);if(solver.GrainCount>0){commands.SetComputeBufferParam(shader,validate,"_Status",status);commands.SetComputeBufferParam(shader,validate,"_Grains",solver.Grains);commands.SetComputeIntParam(shader,"_GrainCount",solver.GrainCount);commands.DispatchCompute(shader,validate,(solver.GrainCount+63)/64,1,1);}commands.SetComputeBufferParam(shader,validateBodies,"_Status",status);commands.SetComputeBufferParam(shader,validateBodies,"_Bodies",solver.Bodies);commands.SetComputeBufferParam(shader,validateBodies,"_Hull",mirror.Hull);commands.SetComputeIntParam(shader,"_BodyStart",solver.BodyStart);commands.SetComputeVectorParam(shader,"_LocalCom",solver.BodyDefinitions[0].LocalCOM);commands.DispatchCompute(shader,validateBodies,256,1,1);if(solver.BodyCount>2){commands.SetComputeBufferParam(shader,validateFragments,"_Status",status);commands.SetComputeBufferParam(shader,validateFragments,"_Bodies",solver.Bodies);commands.SetComputeBufferParam(shader,validateFragments,"_Parameters",solver.Parameters);commands.SetComputeBufferParam(shader,validateFragments,"_FragmentHull",mirror.FragmentHull);commands.SetComputeIntParam(shader,"_BodyCount",solver.BodyCount);commands.DispatchCompute(shader,validateFragments,((solver.BodyCount-2)*16384+63)/64,1,1);}}
            Graphics.ExecuteCommandBuffer(commands);awaiting=true;
            AsyncGPUReadback.Request(status,r=>{try{placementStatus=r.hasError?uint.MaxValue:r.GetData<uint>()[0];}catch{placementStatus=uint.MaxValue;}finally{awaiting=false;}});
        }
        internal bool TryCompletePlacementValidation(out CandidateEditStatus result)
        {
            result=CandidateEditStatus.Busy;if(awaiting)return false;if(edit==null){result=CandidateEditStatus.Unavailable;return true;}
            if(placementStatus==uint.MaxValue)faulted=true;
            result=placementStatus==0?edit.Status:placementStatus==uint.MaxValue?CandidateEditStatus.Faulted:CandidateEditStatus.PlacementBlocked;return true;
        }
        internal void Abort(){if(awaiting)return;edit=null;replacement=null;placementStatus=0;}
        internal CandidateEditStatus TryPublish()
        {
            if(disposed||edit==null||awaiting||placementStatus!=0){Abort();return CandidateEditStatus.StaleEdit;}
            if(faulted)return CandidateEditStatus.Faulted;
            if(!terrain.IsCurrent(edit)){Abort();return CandidateEditStatus.StaleEdit;}

            // The status readback completed while the session edit fence was
            // held. Recheck all CPU facts before this point; no ordinary
            // rejection is permitted after the first command is recorded.
            if(edit.Release)
            {
                var boundaryStatus=solver.ValidateBoundaryReplacement(replacement);
                if(boundaryStatus!=CandidateEditStatus.Released){Abort();return boundaryStatus;}
                var grainStatus=solver.ValidateAppend(grain,grainMass);
                if(grainStatus!=CandidateEditStatus.Released){Abort();return grainStatus;}
            }
            try
            {
                ValidateMirror();
                int x=edit.Index%terrain.ChunkSize,y=edit.Index/terrain.ChunkSize;
                commands.Clear();
                if(edit.Release)
                {
                    solver.RecordReplaceBoundaryCache(commands,replacement);
                    solver.RecordAppend(commands,grain,grainMass);
                    solver.RecordTopologyEpoch(commands);
                }
                commands.SetComputeBufferParam(shader,publish,"_Dirty",mirror.Dirty);commands.SetComputeTextureParam(shader,publish,"_Field",mirror.Field);commands.SetComputeTextureParam(shader,publish,"_Damage",mirror.Damage);commands.SetComputeIntParams(shader,"_Address",x,y);commands.SetComputeIntParam(shader,"_Slice",edit.Slice);commands.SetComputeIntParam(shader,"_DirtySlice",edit.Slice);commands.SetComputeIntParam(shader,"_Material",unchecked((int)(edit.Release?0:edit.OldMaterial)));commands.SetComputeFloatParam(shader,"_DamageValue",edit.NewDamage);commands.DispatchCompute(shader,publish,1,1,1);
                Graphics.ExecuteCommandBuffer(commands);
            }
            catch(Exception)
            {
                // A submission exception has no honest rollback guarantee.
                // Keep the staged CPU edit unpublished and make this owner
                // unusable until its session is reset/disposed.
                faulted=true;commands.Clear();return CandidateEditStatus.Faulted;
            }
            // ExecuteCommandBuffer has enqueued every GPU mutation in order.
            // These host facts are therefore published as one indivisible
            // candidate result, after (and only after) that enqueue succeeds.
            if(edit.Release)solver.PublishTerrainEdit(replacement,grain,grainMass,true);
            terrain.Publish(edit);var result=edit.Status;edit=null;replacement=null;placementStatus=0;return result;
        }
        public void Dispose(){if(disposed)return;disposed=true;AsyncGPUReadback.WaitAllRequests();commands.Dispose();status.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
    }
}
