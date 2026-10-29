using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Actual-geometry acceptance for one frozen position correction. Reuses
    // the operator's manifold scratch, then rebuilds its graph at the selected
    // pose. Only private working poses change; no session state is published.
    public sealed class CoupledPositionSearch : IDisposable
    {
        const int ManifoldCapacity=CoupledContactManifoldBatch.MaxActive;
        readonly CoupledContactOperator op;
        readonly CoupledPositionSolve solve;
        readonly CoupledContactManifoldBatch manifolds;
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer control,facts,workingBodies,originBodies,workingPrimitives,
            workingGroups,originPrimitives,trialBodies,trialPrimitives,trialGroups,
            displacement,heads,nodes,metricBlocks,metric,baseMetric;
        readonly int beginSubstep,snapshotSubstep,beginSearch,snapshotDirection,
            resetBaseline,buildBodies,buildPrimitives,buildGroups,resetFacts,
            evaluate,finalize,storeBaseline,select,checkGraph,publishBodies,
            publishPrimitives,publishGroups;
        readonly int groupCount;
        readonly bool hasUnion;
        bool disposed,initialized;
        public long BufferBytes { get; private set; }
        // active, accepted, selected trial, fault, target reached, relinearize,
        // trials evaluated, hard bounds pass, source solve fault, failed trial,
        // maximum conservative surface travel (float bits), initialized.
        public GraphicsBuffer Control=>control;
        public GraphicsBuffer WorkingBodies=>workingBodies;
        public GraphicsBuffer WorkingPrimitives=>workingPrimitives;
        public GraphicsBuffer WorkingUnionGroups=>hasUnion?workingGroups:null;

        public CoupledPositionSearch(CoupledContactOperator op,CoupledPositionSolve solve)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            this.solve=solve??throw new ArgumentNullException(nameof(solve));
            manifolds=op.Manifolds;
            if(solve.Operator!=op||!op.NormalOnlyRows||manifolds==null)
                throw new ArgumentException("Position search requires one generated normal graph");
            groupCount=manifolds.UnionGroupCount;
            hasUnion=manifolds.HasUnion;
            if(hasUnion&&!manifolds.ContainmentEnabled)
                throw new ArgumentException("Union search requires independent containment validation");
            arena=op.Arena;
            string owner=arena.NewOwner("PositionSearch");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledPositionSearch");
                if(asset==null)throw new InvalidOperationException("Missing CoupledPositionSearch resource");
                shader=UnityEngine.Object.Instantiate(asset);
                control=Buffer(owner,12,4);facts=Buffer(owner,2,4);
                workingBodies=Buffer(owner,op.BodyCount,16);
                originBodies=Buffer(owner,op.BodyCount,16);
                workingPrimitives=Buffer(owner,Math.Max(1,op.SourcePrimitiveCount),56);
                workingGroups=Buffer(owner,Math.Max(1,groupCount),32);
                originPrimitives=Buffer(owner,Math.Max(1,op.SourcePrimitiveCount),56);
                trialBodies=Buffer(owner,op.BodyCount,16);
                trialPrimitives=Buffer(owner,Math.Max(1,op.SourcePrimitiveCount),56);
                trialGroups=Buffer(owner,Math.Max(1,groupCount),32);
                displacement=Buffer(owner,op.BodyCount,16);
                heads=Buffer(owner,ManifoldCapacity,4);
                nodes=Buffer(owner,2*ManifoldCapacity,16);
                metricBlocks=Buffer(owner,ManifoldCapacity/256,8);
                metric=Buffer(owner,1,16);baseMetric=Buffer(owner,1,16);
                shader.SetInt("_BodyCount",op.BodyCount);
                shader.SetInt("_PrimitiveCount",op.SourcePrimitiveCount);
                shader.SetInt("_GroupCount",groupCount);
                beginSubstep=Kernel("BeginSubstep");snapshotSubstep=Kernel("SnapshotSubstep");
                beginSearch=Kernel("BeginSearch");snapshotDirection=Kernel("SnapshotDirection");
                resetBaseline=Kernel("ResetBaseline");buildBodies=Kernel("BuildTrialBodies");
                buildPrimitives=Kernel("BuildTrialPrimitives");buildGroups=Kernel("BuildTrialGroups");
                resetFacts=Kernel("ResetTrialFacts");evaluate=Kernel("EvaluateGeometry");
                finalize=Kernel("FinalizeMetric");storeBaseline=Kernel("StoreBaseline");
                select=Kernel("SelectTrial");checkGraph=Kernel("CheckRefreshedGraph");
                publishBodies=Kernel("PublishWorkingBodies");publishPrimitives=Kernel("PublishWorkingPrimitives");
                publishGroups=Kernel("PublishWorkingGroups");
                foreach(int kernel in new[]{beginSubstep,beginSearch,snapshotDirection,
                    buildBodies,buildPrimitives,buildGroups,storeBaseline,select,checkGraph,
                    publishBodies,publishPrimitives,publishGroups})
                    shader.SetBuffer(kernel,"_Control",control);
                Bind(snapshotSubstep,("_SourceBodies",op.BodiesBuffer),
                    ("_SourcePrimitives",op.SourcePrimitives),
                    ("_SourceGroups",manifolds.UnionGroupsBuffer),
                    ("_WorkingBodies",workingBodies),("_OriginBodies",originBodies),
                    ("_WorkingPrimitives",workingPrimitives),
                    ("_WorkingGroups",workingGroups),("_OriginPrimitives",originPrimitives));
                Bind(beginSearch,("_SolveControl",solve.Control));
                Bind(snapshotDirection,("_SourceDisplacement",solve.Displacement),
                    ("_Displacement",displacement));
                Bind(resetBaseline,("_Heads",heads));
                Bind(resetFacts,("_Facts",facts));
                Bind(buildBodies,("_WorkingBodies",workingBodies),("_OriginBodies",originBodies),
                    ("_Displacement",displacement),("_TrialBodies",trialBodies));
                Bind(buildPrimitives,("_WorkingBodies",workingBodies),("_OriginBodies",originBodies),
                    ("_WorkingPrimitives",workingPrimitives),("_OriginPrimitives",originPrimitives),
                    ("_Displacement",displacement),("_TrialPrimitives",trialPrimitives));
                Bind(buildGroups,("_WorkingBodies",workingBodies),
                    ("_WorkingGroups",workingGroups),("_Displacement",displacement),
                    ("_TrialGroups",trialGroups));
                Bind(evaluate,("_Manifolds",manifolds.CompactManifolds),
                    ("_Indices",manifolds.CompactIndices),("_Count",manifolds.CompactCounter),
                    ("_SourcePrimitives",manifolds.PrimitivesBuffer),
                    ("_Heads",heads),("_Nodes",nodes),("_Facts",facts),
                    ("_MetricBlocks",metricBlocks));
                Bind(finalize,("_MetricBlocks",metricBlocks),("_Metric",metric),
                    ("_Count",manifolds.CompactCounter),("_BroadFault",manifolds.BroadFaultBuffer),
                    ("_CompactFault",manifolds.CompactFaultBuffer),
                    ("_ValidationFacts",manifolds.ValidationFactsBuffer),("_Facts",facts));
                Bind(storeBaseline,("_Metric",metric),("_BaseMetric",baseMetric));
                Bind(select,("_Metric",metric),("_BaseMetric",baseMetric),("_Facts",facts));
                Bind(checkGraph,("_RowFault",op.RowFault),
                    ("_BroadFault",manifolds.BroadFaultBuffer),("_CompactFault",manifolds.CompactFaultBuffer),
                    ("_ValidationFacts",manifolds.ValidationFactsBuffer));
                Bind(publishBodies,("_TrialBodies",trialBodies),("_WorkingBodies",workingBodies));
                Bind(publishPrimitives,("_TrialPrimitives",trialPrimitives),("_WorkingPrimitives",workingPrimitives));
                Bind(publishGroups,("_TrialGroups",trialGroups),("_WorkingGroups",workingGroups));
            }
            catch { Dispose();throw; }
        }

        int Kernel(string name)
        {
            return shader.FindKernel(name);
        }
        GraphicsBuffer Buffer(string owner,int count,int stride)
        {
            var buffer=arena.Allocate(owner+"/"+owned.Count,count,stride);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] entries)
        {foreach(var entry in entries)shader.SetBuffer(kernel,entry.Name,entry.Buffer);}
        static int Groups(int count)=>Math.Max(1,(count+63)/64);

        // Record at the predicted pose, before any position geometry update.
        // The caller must have prepared the operator's graph at that pose.
        // This is the sole reset of the .25-cell correction envelope.
        public void RecordBeginSubstep(CommandBuffer target)
        {
            Check(target);
            target.DispatchCompute(shader,beginSubstep,1,1,1);
            target.DispatchCompute(shader,snapshotSubstep,
                Groups(Math.Max(op.BodyCount,Math.Max(op.SourcePrimitiveCount,groupCount))),1,1);
            initialized=true;
        }

        // Record after the frozen position solve. Full baseline/trial queries
        // reuse the same topology and include previously absent contacts. The
        // selected graph is rebuilt once and its next solve must start p=0.
        public void Record(CommandBuffer target)
        {
            Check(target);
            if(!initialized)throw new InvalidOperationException("Begin the predicted substep first");
            target.DispatchCompute(shader,beginSearch,1,1,1);
            target.DispatchCompute(shader,snapshotDirection,Groups(op.BodyCount),1,1);
            target.DispatchCompute(shader,resetBaseline,ManifoldCapacity/256,1,1);
            manifolds.RecordRequery(target,workingPrimitives,hasUnion?workingGroups:null);
            RecordMetric(target,true);
            target.DispatchCompute(shader,storeBaseline,1,1,1);
            for(int trial=0;trial<8;trial++)
            {
                target.SetComputeIntParam(shader,"_TrialIndex",trial);
                RecordPose(target,false);
                manifolds.RecordRequery(target,trialPrimitives,hasUnion?trialGroups:null);
                RecordMetric(target,false);
                target.DispatchCompute(shader,select,1,1,1);
            }
            RecordPose(target,true);
            op.RecordRequeryGraph(target,trialPrimitives,trialBodies,hasUnion?trialGroups:null);
            target.DispatchCompute(shader,checkGraph,1,1,1);
            target.DispatchCompute(shader,publishBodies,Groups(op.BodyCount),1,1);
            target.DispatchCompute(shader,publishPrimitives,Groups(op.SourcePrimitiveCount),1,1);
            if(groupCount>0)target.DispatchCompute(shader,publishGroups,Groups(groupCount),1,1);
        }
        void RecordPose(CommandBuffer target,bool selected)
        {
            target.SetComputeIntParam(shader,"_SelectOutput",selected?1:0);
            target.DispatchCompute(shader,buildBodies,Groups(op.BodyCount),1,1);
            target.DispatchCompute(shader,buildPrimitives,Groups(op.SourcePrimitiveCount),1,1);
            if(groupCount>0)target.DispatchCompute(shader,buildGroups,Groups(groupCount),1,1);
        }
        void RecordMetric(CommandBuffer target,bool baseline)
        {
            target.SetComputeIntParam(shader,"_Baseline",baseline?1:0);
            target.DispatchCompute(shader,resetFacts,1,1,1);
            target.DispatchCompute(shader,evaluate,ManifoldCapacity/256,1,1);
            target.DispatchCompute(shader,finalize,1,1,1);
        }
        void Check(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPositionSearch));
            if(target==null)throw new ArgumentNullException(nameof(target));
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var buffer in owned)arena.Release(buffer);
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
