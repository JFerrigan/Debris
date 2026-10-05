using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=16)]
    public struct CoupledContactBody
    {
        public Vector2 Center;
        public float InverseMass,InverseInertia;
    }

    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct CoupledContactRow
    {
        public uint A,B;
        public Vector2 Point,Direction;
        public float InverseRoot,Padding;
    }

    // Isolated frozen-graph GPU product for V2-1. The caller supplies contact
    // rows; no gameplay state or legacy solver buffer is modified by this probe.
    public sealed class CoupledContactOperator : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack=4, Size=16)]
        struct Segment
        {
            public uint Body,Start,Count,Padding;
        }

        public sealed class Result
        {
            public Vector4[] EndpointMotion;
            public float[] RowVelocity;
        }

        readonly ComputeShader shader;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 frozen contact operator"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer bodyBuffer,rowBuffer,xBuffer,freeBuffer,adjacencyBuffer,
            segmentBuffer,segmentStartBuffer,sideBuffer,partialBuffer,motionBuffer,yBuffer;
        readonly int expand,reduce,applyBodies,applyRows;
        readonly int bodyCount,rowCount,segmentCount;
        bool disposed;
        public int BodyCount=>bodyCount;
        public int RowCount=>rowCount;
        public int SegmentCount=>segmentCount;
        public int MaxSegmentsPerBody { get; }
        public long BufferBytes { get; private set; }

        public CoupledContactOperator(CoupledContactBody[] bodies,CoupledContactRow[] rows)
        {
            if(bodies==null||rows==null||bodies.Length==0||bodies.Length>8210||rows.Length>262144)
                throw new ArgumentException("Frozen graph exceeds the V2-1 endpoint or row limit");
            bodyCount=bodies.Length;rowCount=rows.Length;
            var incident=new List<uint>[bodyCount];
            for(int i=0;i<bodyCount;i++)
            {
                var b=bodies[i];
                if(!Finite(b.Center.x)||!Finite(b.Center.y)||!Finite(b.InverseMass)||!Finite(b.InverseInertia)||
                    b.InverseMass<0||b.InverseInertia<0)throw new ArgumentException("Invalid body mass or center");
                incident[i]=new List<uint>();
            }
            for(int i=0;i<rowCount;i++)
            {
                var row=rows[i];
                if(row.A>=bodyCount||row.B>=bodyCount||row.A==row.B||
                    !Finite(row.Point.x)||!Finite(row.Point.y)||!Finite(row.Direction.x)||!Finite(row.Direction.y)||
                    !Finite(row.InverseRoot)||row.InverseRoot<=0||
                    Math.Abs(row.Direction.sqrMagnitude-1)>1e-3f)
                    throw new ArgumentException("Invalid frozen contact row");
                var a=bodies[row.A];var b=bodies[row.B];
                Vector2 armA=row.Point-a.Center,armB=row.Point-b.Center;
                float torqueA=armA.x*row.Direction.y-armA.y*row.Direction.x;
                float torqueB=armB.x*row.Direction.y-armB.y*row.Direction.x;
                double diagonal=a.InverseMass+b.InverseMass+
                    a.InverseInertia*torqueA*torqueA+b.InverseInertia*torqueB*torqueB;
                if(!(diagonal>0)||Math.Abs(row.InverseRoot*Math.Sqrt(diagonal)-1)>1e-4)
                    throw new ArgumentException("Row scaling does not match physical mass and inertia");
                incident[row.A].Add((uint)(2*i));incident[row.B].Add((uint)(2*i+1));
            }
            var adjacency=new List<uint>(rowCount*2);
            var segments=new List<Segment>();var starts=new uint[bodyCount+1];int maxSegments=0;
            for(int body=0;body<bodyCount;body++)
            {
                starts[body]=(uint)segments.Count;
                var list=incident[body];
                for(int first=0;first<list.Count;first+=64)
                {
                    int count=Math.Min(64,list.Count-first);
                    segments.Add(new Segment{Body=(uint)body,Start=(uint)adjacency.Count,Count=(uint)count});
                    for(int j=0;j<count;j++)adjacency.Add(list[first+j]);
                }
                maxSegments=Math.Max(maxSegments,segments.Count-(int)starts[body]);
            }
            starts[bodyCount]=(uint)segments.Count;
            segmentCount=segments.Count;MaxSegmentsPerBody=maxSegments;
            var asset=Resources.Load<ComputeShader>("CoupledContactOperator");
            if(asset==null)throw new InvalidOperationException("Missing CoupledContactOperator compute resource");
            shader=UnityEngine.Object.Instantiate(asset);
            bodyBuffer=Buffer(bodyCount,16);rowBuffer=Buffer(Math.Max(1,rowCount),32);
            xBuffer=Buffer(Math.Max(1,rowCount),4);freeBuffer=Buffer(bodyCount,16);
            adjacencyBuffer=Buffer(Math.Max(1,adjacency.Count),4);
            segmentBuffer=Buffer(Math.Max(1,segmentCount),16);segmentStartBuffer=Buffer(bodyCount+1,4);
            sideBuffer=Buffer(Math.Max(1,rowCount*2),16);
            partialBuffer=Buffer(Math.Max(1,segmentCount),16);
            motionBuffer=Buffer(bodyCount,16);yBuffer=Buffer(Math.Max(1,rowCount),4);
            bodyBuffer.SetData(bodies);if(rowCount>0)rowBuffer.SetData(rows);
            if(adjacency.Count>0)adjacencyBuffer.SetData(adjacency.ToArray());
            if(segmentCount>0)segmentBuffer.SetData(segments.ToArray());
            segmentStartBuffer.SetData(starts);
            shader.SetInt("_BodyCount",bodyCount);shader.SetInt("_RowCount",rowCount);
            shader.SetInt("_SegmentCount",segmentCount);
            expand=shader.FindKernel("ExpandRows");reduce=shader.FindKernel("ReduceSegments");
            applyBodies=shader.FindKernel("ApplyBodies");applyRows=shader.FindKernel("ApplyRows");
            Bind(expand,("_Rows",rowBuffer),("_Bodies",bodyBuffer),("_X",xBuffer),("_SideReactions",sideBuffer));
            Bind(reduce,("_Adjacency",adjacencyBuffer),("_Segments",segmentBuffer),
                ("_SideReactions",sideBuffer),("_SegmentReactions",partialBuffer));
            Bind(applyBodies,("_Bodies",bodyBuffer),("_BodySegmentStarts",segmentStartBuffer),
                ("_SegmentReactions",partialBuffer),("_FreeMotion",freeBuffer),("_Motion",motionBuffer));
            Bind(applyRows,("_Rows",rowBuffer),("_Bodies",bodyBuffer),("_Motion",motionBuffer),("_Y",yBuffer));
        }

        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        GraphicsBuffer Buffer(int count,int stride)
        {
            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        // Synchronous readback is intentional here: this is a diagnostic
        // product, not the eventual frame submission or completion path.
        public Result Apply(Vector4[] freeMotion,float[] scaledImpulse)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(freeMotion==null||scaledImpulse==null||freeMotion.Length!=bodyCount||scaledImpulse.Length!=rowCount)
                throw new ArgumentException("Input length does not match the frozen graph");
            foreach(var value in freeMotion)
                if(!Finite(value.x)||!Finite(value.y)||!Finite(value.z)||!Finite(value.w))throw new ArgumentException("Nonfinite free motion");
            foreach(float value in scaledImpulse)if(!Finite(value))throw new ArgumentException("Nonfinite scaled impulse");
            freeBuffer.SetData(freeMotion);if(rowCount>0)xBuffer.SetData(scaledImpulse);
            commands.Clear();
            if(rowCount>0)commands.DispatchCompute(shader,expand,(rowCount+63)/64,1,1);
            if(segmentCount>0)commands.DispatchCompute(shader,reduce,segmentCount,1,1);
            commands.DispatchCompute(shader,applyBodies,(bodyCount+63)/64,1,1);
            if(rowCount>0)commands.DispatchCompute(shader,applyRows,(rowCount+63)/64,1,1);
            Graphics.ExecuteCommandBuffer(commands);
            var result=new Result{EndpointMotion=new Vector4[bodyCount],RowVelocity=new float[rowCount]};
            motionBuffer.GetData(result.EndpointMotion);
            if(rowCount>0)yBuffer.GetData(result.RowVelocity);
            return result;
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)buffer.Release();
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
