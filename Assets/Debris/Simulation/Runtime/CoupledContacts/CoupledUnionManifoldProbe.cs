using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=40)]
    public struct CoupledUnionQuery
    {
        public Vector2 SquareCenter,SquareHalf;
        public float SquareAngle;
        public uint SquareEndpoint,FaceIndex,UnionEndpoint,GroupIndex,Pad;
    }

    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct CoupledUnionQueryGroup
    {
        public Vector2 UnionCenter;
        public float UnionAngle;
        public uint PatchStart,PatchCount,FirstQuery,QueryCount,Reserved;
    }

    // Source-only narrow-phase diagnostic for prepared union boundary queries.
    // A later GPU gather must supply these bounded query groups without CPU
    // enumeration or a synchronous per-frame readback.
    public sealed class CoupledUnionManifoldProbe : IDisposable
    {
        readonly ComputeShader shader;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 union manifolds"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer patches,faces,queries,groups,raw,output,fault;
        readonly int build,resolve,count;
        bool disposed;
        public long BufferBytes { get; private set; }

        public sealed class Result
        {
            public CoupledContactManifold[] Manifolds;
            public uint FaultFlags;
            public bool Success=>FaultFlags==0;
        }

        public CoupledUnionManifoldProbe(CoupledUnionPatch[] sourcePatches,
            CoupledExteriorFace[] sourceFaces,CoupledUnionQuery[] sourceQueries,
            CoupledUnionQueryGroup[] sourceGroups,int bodyCount,float margin=.25f)
        {
            if(sourcePatches==null||sourceFaces==null||sourceQueries==null||sourceGroups==null||
               sourcePatches.Length>4096||sourceFaces.Length>4096||
               sourceQueries.Length>65536||sourceGroups.Length>65536||
               bodyCount<=0||bodyCount>8210||!Finite(margin)||margin<0)
                throw new ArgumentException("Invalid V2 union query arena");
            foreach(var p in sourcePatches)
                if(!Finite(p.Center.x)||!Finite(p.Center.y)||!Finite(p.Half.x)||!Finite(p.Half.y)||
                   p.Half.x<=0||p.Half.y<=0)
                    throw new ArgumentException("Invalid V2 union patch");
            foreach(var f in sourceFaces)
                if(!Finite(f.A.x)||!Finite(f.A.y)||!Finite(f.B.x)||!Finite(f.B.y)||
                   !Finite(f.Normal.x)||!Finite(f.Normal.y)||f.Side>3||
                   (f.B-f.A).sqrMagnitude<=1e-12f||
                   Math.Abs(f.Normal.sqrMagnitude-1)>1e-3f)
                    throw new ArgumentException("Invalid V2 exterior interval");
            for(int i=0;i<sourceGroups.Length;i++)
            {
                var g=sourceGroups[i];
                if(!Finite(g.UnionCenter.x)||!Finite(g.UnionCenter.y)||!Finite(g.UnionAngle)||
                   (ulong)g.PatchStart+g.PatchCount>(ulong)sourcePatches.Length||
                   (ulong)g.FirstQuery+g.QueryCount>(ulong)sourceQueries.Length||g.QueryCount>64)
                    throw new ArgumentException("Invalid V2 union group");
            }
            for(int i=0;i<sourceQueries.Length;i++)
            {
                var q=sourceQueries[i];
                if(q.GroupIndex>=sourceGroups.Length||q.FaceIndex>=sourceFaces.Length||
                   q.SquareEndpoint>=bodyCount||q.UnionEndpoint>=bodyCount||
                   q.SquareEndpoint==q.UnionEndpoint||
                   !Finite(q.SquareCenter.x)||!Finite(q.SquareCenter.y)||
                   !Finite(q.SquareHalf.x)||!Finite(q.SquareHalf.y)||
                   q.SquareHalf.x<=0||q.SquareHalf.y<=0||!Finite(q.SquareAngle))
                    throw new ArgumentException("Invalid V2 union face query");
                var g=sourceGroups[(int)q.GroupIndex];
                if(i<g.FirstQuery||(ulong)i>=(ulong)g.FirstQuery+g.QueryCount)
                    throw new ArgumentException("Union query lies outside its group");
            }
            foreach(var g in sourceGroups)
            {
                if(g.QueryCount==0)continue;
                var first=sourceQueries[(int)g.FirstQuery];
                for(uint offset=1;offset<g.QueryCount;offset++)
                {
                    var q=sourceQueries[(int)(g.FirstQuery+offset)];
                    if(q.SquareEndpoint!=first.SquareEndpoint||q.UnionEndpoint!=first.UnionEndpoint||
                       q.SquareCenter!=first.SquareCenter||q.SquareHalf!=first.SquareHalf||
                       q.SquareAngle!=first.SquareAngle)
                        throw new ArgumentException("Union query group mixes square poses");
                }
            }
            var asset=Resources.Load<ComputeShader>("CoupledUnionManifold");
            if(asset==null)throw new InvalidOperationException("Missing CoupledUnionManifold compute resource");
            shader=UnityEngine.Object.Instantiate(asset);
            count=sourceQueries.Length;
            patches=Buffer(Math.Max(1,sourcePatches.Length),20);
            faces=Buffer(Math.Max(1,sourceFaces.Length),32);
            queries=Buffer(Math.Max(1,count),40);
            groups=Buffer(Math.Max(1,sourceGroups.Length),32);
            raw=Buffer(Math.Max(1,count),52);
            output=Buffer(Math.Max(1,count),52);
            fault=Buffer(1,4);
            if(sourcePatches.Length>0)patches.SetData(sourcePatches);
            if(sourceFaces.Length>0)faces.SetData(sourceFaces);
            if(count>0)queries.SetData(sourceQueries);
            if(sourceGroups.Length>0)groups.SetData(sourceGroups);
            shader.SetInt("_PatchCount",sourcePatches.Length);
            shader.SetInt("_FaceCount",sourceFaces.Length);
            shader.SetInt("_QueryCount",count);
            shader.SetInt("_GroupCount",sourceGroups.Length);
            shader.SetFloat("_Margin",margin);
            build=shader.FindKernel("BuildUnionManifolds");
            resolve=shader.FindKernel("ResolveStripExits");
            Bind(build,("_Patches",patches),("_Faces",faces),("_Queries",queries),
                ("_Groups",groups),("_RawManifolds",raw),("_Fault",fault));
            Bind(resolve,("_Patches",patches),("_Queries",queries),("_Groups",groups),
                ("_RawManifolds",raw),("_Manifolds",output),("_Fault",fault));
        }

        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        GraphicsBuffer Buffer(int count,int stride)
        {
            var buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var item in bindings)shader.SetBuffer(kernel,item.Name,item.Buffer);}

        public Result Build()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledUnionManifoldProbe));
            fault.SetData(new uint[1]);
            commands.Clear();
            if(count>0)
            {
                commands.DispatchCompute(shader,build,(count+63)/64,1,1);
                commands.DispatchCompute(shader,resolve,(count+63)/64,1,1);
                Graphics.ExecuteCommandBuffer(commands);
            }
            var flags=new uint[1];fault.GetData(flags);
            var result=new Result{FaultFlags=flags[0],Manifolds=Array.Empty<CoupledContactManifold>()};
            if(result.Success&&count>0)
            {
                result.Manifolds=new CoupledContactManifold[count];
                output.GetData(result.Manifolds);
            }
            return result;
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)buffer.Release();
            if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
            else UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
