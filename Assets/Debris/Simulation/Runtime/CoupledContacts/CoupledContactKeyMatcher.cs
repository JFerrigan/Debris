using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Exact GPU lookup between two compact row generations. The caller owns
    // the keys and their GPU counts; no CPU copy or row-order assumption is
    // needed. A match is a previous row index or uint.MaxValue.
    public sealed class CoupledContactKeyMatcher : IDisposable
    {
        public const int MaxKeys=CoupledContactRowBatch.MaxRows;
        const int Buckets=262144;
        readonly ComputeShader shader;
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        int allocationIndex;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer previousHeads,currentHeads,previousNext,currentNext;
        readonly GraphicsBuffer matches,fault,previousArgs,currentArgs;
        readonly int prepare,clear,linkPrevious,linkCurrent,
            validatePrevious,match;
        bool disposed;
        public long BufferBytes { get; }
        public GraphicsBuffer Matches=>matches;
        // 1=count overflow, 2=duplicate previous key, 4=duplicate current
        // key, 8=corrupt chain. Any bit makes cache reuse invalid.
        public GraphicsBuffer Fault=>fault;

        public CoupledContactKeyMatcher(CoupledContactArena arena=null)
        {
            this.arena=arena??new CoupledContactArena();
            arenaOwner=this.arena.NewOwner("KeyMatcher");
            try
            {
            var asset=Resources.Load<ComputeShader>("CoupledContactKeyMatch");
            if(asset==null)throw new InvalidOperationException(
                "Missing CoupledContactKeyMatch compute resource");
            BufferBytes=(long)(2*Buckets+3*MaxKeys+1+6)*4;
            this.arena.EnsureCapacity(BufferBytes);
            shader=UnityEngine.Object.Instantiate(asset);
            previousHeads=Buffer(Buckets,4);
            currentHeads=Buffer(Buckets,4);
            previousNext=Buffer(MaxKeys,4);
            currentNext=Buffer(MaxKeys,4);
            matches=Buffer(MaxKeys,4);
            fault=Buffer(1,4);
            var indirect=GraphicsBuffer.Target.Structured|
                GraphicsBuffer.Target.IndirectArguments;
            previousArgs=Buffer(3,4,indirect);
            currentArgs=Buffer(3,4,indirect);
            prepare=shader.FindKernel("Prepare");
            clear=shader.FindKernel("ClearHeads");
            linkPrevious=shader.FindKernel("LinkPrevious");
            linkCurrent=shader.FindKernel("LinkCurrent");
            validatePrevious=shader.FindKernel("ValidatePrevious");
            match=shader.FindKernel("MatchCurrent");
            shader.SetInt("_BucketCount",Buckets);
            Bind(prepare,("_Fault",fault),("_PreviousArgs",previousArgs),
                ("_CurrentArgs",currentArgs));
            Bind(clear,("_PreviousHeads",previousHeads),
                ("_CurrentHeads",currentHeads));
            Bind(linkPrevious,("_PreviousHeads",previousHeads),
                ("_PreviousNext",previousNext));
            Bind(linkCurrent,("_CurrentHeads",currentHeads),
                ("_CurrentNext",currentNext));
            Bind(validatePrevious,("_PreviousHeads",previousHeads),
                ("_PreviousNext",previousNext),("_Fault",fault));
            Bind(match,("_PreviousHeads",previousHeads),
                ("_CurrentHeads",currentHeads),
                ("_PreviousNext",previousNext),
                ("_CurrentNext",currentNext),("_Matches",matches),
                ("_Fault",fault));
            }
            catch { Dispose();throw; }
        }

        GraphicsBuffer Buffer(int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+(allocationIndex++),count,stride,target);
            owned.Add(buffer);return buffer;
        }

        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] entries)
        {
            foreach(var entry in entries)
                shader.SetBuffer(kernel,entry.Name,entry.Buffer);
        }

        static void CheckKeys(GraphicsBuffer keys,GraphicsBuffer count,string name)
        {
            if(keys==null||keys.stride!=32||keys.count<1||keys.count>MaxKeys||
               count==null||count.stride!=4||count.count<1)
                throw new ArgumentException("Invalid V2 contact key/count buffers",name);
        }

        public void Record(CommandBuffer target,GraphicsBuffer previousKeys,
            GraphicsBuffer previousCount,GraphicsBuffer currentKeys,
            GraphicsBuffer currentCount)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactKeyMatcher));
            if(target==null)throw new ArgumentNullException(nameof(target));
            CheckKeys(previousKeys,previousCount,nameof(previousKeys));
            CheckKeys(currentKeys,currentCount,nameof(currentKeys));
            if(previousKeys==currentKeys||previousCount==currentCount)
                throw new ArgumentException("Previous and current generations need separate storage");
            target.SetComputeIntParam(shader,"_PreviousCapacity",previousKeys.count);
            target.SetComputeIntParam(shader,"_CurrentCapacity",currentKeys.count);
            target.SetComputeBufferParam(shader,prepare,"_PreviousCount",previousCount);
            target.SetComputeBufferParam(shader,prepare,"_CurrentCount",currentCount);
            target.SetComputeBufferParam(shader,linkPrevious,"_PreviousKeys",previousKeys);
            target.SetComputeBufferParam(shader,linkPrevious,"_PreviousCount",previousCount);
            target.SetComputeBufferParam(shader,linkCurrent,"_CurrentKeys",currentKeys);
            target.SetComputeBufferParam(shader,linkCurrent,"_CurrentCount",currentCount);
            target.SetComputeBufferParam(shader,validatePrevious,"_PreviousKeys",previousKeys);
            target.SetComputeBufferParam(shader,validatePrevious,"_PreviousCount",previousCount);
            target.SetComputeBufferParam(shader,match,"_PreviousKeys",previousKeys);
            target.SetComputeBufferParam(shader,match,"_CurrentKeys",currentKeys);
            target.SetComputeBufferParam(shader,match,"_PreviousCount",previousCount);
            target.SetComputeBufferParam(shader,match,"_CurrentCount",currentCount);
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,clear,Buckets/64,1,1);
            target.DispatchCompute(shader,linkPrevious,previousArgs,0u);
            target.DispatchCompute(shader,linkCurrent,currentArgs,0u);
            target.DispatchCompute(shader,validatePrevious,previousArgs,0u);
            target.DispatchCompute(shader,match,currentArgs,0u);
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
