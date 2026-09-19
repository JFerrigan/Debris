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
        bool awaiting,disposed;
        uint placementStatus;
        int clear,validate,publish;
        public bool Busy=>awaiting||edit!=null;

        internal CandidateTerrainTransaction(ParallelGrainSolver solver,MatterSession mirror,CandidateTerrainState terrain)
        {
            this.solver=solver??throw new ArgumentNullException(nameof(solver));this.mirror=mirror??throw new ArgumentNullException(nameof(mirror));this.terrain=terrain??throw new ArgumentNullException(nameof(terrain));
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("CandidateTerrainEdits"));
            clear=shader.FindKernel("ClearPlacementStatus");validate=shader.FindKernel("ValidatePlacementGrains");publish=shader.FindKernel("PublishTerrainCell");
        }
        internal CandidateEditStatus Prepare(CandidateTerrainEdit value,CandidateBoundaryBuilder.CandidateBoundaryReplacement boundaries,float density)
        {
            if(disposed||Busy||value==null||boundaries==null||!terrain.IsCurrent(value)||!float.IsFinite(density)||density<=0)return CandidateEditStatus.Unavailable;
            if(value.Release&&solver.GrainCount>=solver.GrainCapacity)return CandidateEditStatus.GrainCapacity;
            if(boundaries.Patches.Length>solver.BoundaryCapacity)return CandidateEditStatus.BoundaryCapacity;
            edit=value;replacement=boundaries;grainMass=density;grain=new SolverGrain{Center=new Vector2(value.Cell.x+.5f,value.Cell.y+.5f),Material=value.OldMaterial,Identity=value.Identity};return value.Status;
        }
        internal void BeginPlacementValidation()
        {
            if(disposed||edit==null||awaiting)throw new InvalidOperationException("No prepared candidate edit.");
            commands.Clear();commands.SetComputeBufferParam(shader,clear,"_Status",status);commands.DispatchCompute(shader,clear,1,1,1);
            if(edit.Release){commands.SetComputeBufferParam(shader,validate,"_Status",status);commands.SetComputeBufferParam(shader,validate,"_Grains",solver.Grains);commands.SetComputeIntParam(shader,"_GrainCount",solver.GrainCount);commands.SetComputeVectorParam(shader,"_Centre",grain.Center);commands.SetComputeFloatParam(shader,"_Tolerance",.0001f);commands.DispatchCompute(shader,validate,(solver.GrainCount+63)/64,1,1);}
            Graphics.ExecuteCommandBuffer(commands);awaiting=true;
            AsyncGPUReadback.Request(status,r=>{awaiting=false;placementStatus=r.hasError?uint.MaxValue:r.GetData<uint>()[0];});
        }
        internal bool TryCompletePlacementValidation(out CandidateEditStatus result)
        {
            result=CandidateEditStatus.Busy;if(awaiting)return false;if(edit==null){result=CandidateEditStatus.Unavailable;return true;}
            result=placementStatus==0?edit.Status:placementStatus==uint.MaxValue?CandidateEditStatus.Faulted:CandidateEditStatus.PlacementBlocked;return true;
        }
        internal void Abort(){if(awaiting)return;edit=null;replacement=null;placementStatus=0;}
        internal CandidateEditStatus TryPublish()
        {
            if(disposed||edit==null||awaiting||placementStatus!=0||!terrain.IsCurrent(edit))return CandidateEditStatus.StaleEdit;
            if(edit.Release&&!solver.TryReplaceBoundaryCache(replacement.Patches,replacement.Definitions))return CandidateEditStatus.BoundaryCapacity;
            int x=edit.Index%terrain.ChunkSize,y=edit.Index/terrain.ChunkSize;commands.Clear();commands.SetComputeBufferParam(shader,publish,"_Dirty",mirror.Dirty);commands.SetComputeTextureParam(shader,publish,"_Field",mirror.Field);commands.SetComputeTextureParam(shader,publish,"_Damage",mirror.Damage);commands.SetComputeIntParams(shader,"_Address",x,y);commands.SetComputeIntParam(shader,"_Slice",edit.Slice);commands.SetComputeIntParam(shader,"_DirtySlice",edit.Slice);commands.SetComputeIntParam(shader,"_Material",unchecked((int)(edit.Release?0:edit.OldMaterial)));commands.SetComputeFloatParam(shader,"_DamageValue",edit.NewDamage);commands.DispatchCompute(shader,publish,1,1,1);Graphics.ExecuteCommandBuffer(commands);
            if(edit.Release&&!solver.TryAppend(grain,grainMass))return CandidateEditStatus.GrainCapacity;
            terrain.Publish(edit);var result=edit.Status;edit=null;replacement=null;return result;
        }
        public void Dispose(){if(disposed)return;disposed=true;AsyncGPUReadback.WaitAllRequests();commands.Dispose();status.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
    }
}
