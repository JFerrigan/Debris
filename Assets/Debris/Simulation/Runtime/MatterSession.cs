using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Debris.Core;
using Debris.Materials;
using Debris.Sites;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
namespace Debris.Simulation
{
    [StructLayout(LayoutKind.Sequential, Pack=4)]
    public struct LooseCell
    {
        public Vector2 Position, Velocity;
        public uint Material, Identity, Step, Flags;
    }
    public sealed class MatterSnapshot
    {
        public int Side, ChunkSize, Capacity, OriginX, OriginY, Tick;
        public uint[][] Fields;
        public float[][] Damage;
        public LooseCell[] Cells;
        public uint[] Counters, Dirty;
    }
    // Sole GPU resource owner; snapshots only at save/stream boundaries, compact async facts during play.
    public sealed class MatterSession : IDisposable
    {
        public const int CellStride=32;
        public readonly int Side, ChunkSize, Width, Capacity;
        public readonly Vector2Int Origin;
        public readonly RenderTexture Field, Damage;
        public readonly GraphicsBuffer Cells, Counters, Dirty, Properties, Palette, Shadows, Emissions;
        readonly GraphicsBuffer occupancy, upload, inspection;
        readonly ComputeShader shader;
        readonly int uploadKernel,cutKernel,integrateKernel,inspectKernel,restoreKernel,damageKernel;
        bool disposed,statsPending,inspectionPending,snapshotPending;
        int tick;
        public uint[] Stats {get;private set;}=new uint[4];
        public int Dispatches {get;private set;}
        public int ReadbackQueue {get;private set;}
        public long BufferBytes => (long)Width*Width*12+(long)Capacity*CellStride+Side*Side*4+ChunkSize*ChunkSize*4+(Properties.count*64)+20;
        public MatterSession(MaterialCatalog catalog,AsteroidProfile profile,int side=2,int chunkSize=128,int capacity=8192,ulong seed=42,StableId? site=null)
        {
            if(side<2||side>16||side%2!=0||chunkSize<8||chunkSize>256||capacity<1)throw new ArgumentOutOfRangeException();
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback)throw new NotSupportedException("Compute and async GPU readback are required.");
            catalog.Validate();profile.Validate(catalog);
            Side=side;ChunkSize=chunkSize;Width=side*chunkSize;Capacity=capacity;Origin=new Vector2Int(-Width/2,-Width/2);
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("Matter"));
            Field=Texture(GraphicsFormat.R32_UInt);Damage=Texture(GraphicsFormat.R32_SFloat);
            Cells=Buffer(capacity,CellStride);Counters=Buffer(4,4);Dirty=Buffer(side*side,4);
            occupancy=Buffer(Width*Width,4);upload=Buffer(chunkSize*chunkSize,4);inspection=Buffer(1,4);
            Properties=Buffer(catalog.Count+1,16);Palette=Buffer(catalog.Count+1,16);Shadows=Buffer(catalog.Count+1,16);Emissions=Buffer(catalog.Count+1,16);
            var properties=new Vector4[catalog.Count+1];var palette=new Vector4[catalog.Count+1];var shadows=new Vector4[catalog.Count+1];var emissions=new Vector4[catalog.Count+1];
            for(int m=1;m<=catalog.Count;m++){var d=catalog.DefinitionAt((ushort)m);properties[m]=new Vector4(d.Durability,d.Density,d.UnitValue,0);palette[m]=d.BaseColor;shadows[m]=d.ShadowColor;emissions[m]=(Vector4)(d.EmissiveColor*d.EmissiveIntensity);}
            Properties.SetData(properties);Palette.SetData(palette);Shadows.SetData(shadows);Emissions.SetData(emissions);
            Cells.SetData(new LooseCell[capacity]);Counters.SetData(new uint[4]);Dirty.SetData(new uint[side*side]);occupancy.SetData(new int[Width*Width]);
            uploadKernel=shader.FindKernel("Upload");cutKernel=shader.FindKernel("Cut");integrateKernel=shader.FindKernel("Integrate");inspectKernel=shader.FindKernel("Inspect");restoreKernel=shader.FindKernel("RestoreOccupancy");
            damageKernel=shader.FindKernel("UploadDamage");
            shader.SetInt("_ChunkSize",chunkSize);shader.SetInt("_Side",side);shader.SetInt("_Width",Width);shader.SetInt("_Capacity",capacity);shader.SetInts("_Origin",Origin.x,Origin.y);
            foreach(int kernel in new[]{uploadKernel,cutKernel,integrateKernel,inspectKernel,restoreKernel,damageKernel})
            {
                shader.SetTexture(kernel,"_Field",Field);shader.SetTexture(kernel,"_Damage",Damage);
                shader.SetBuffer(kernel,"_Cells",Cells);shader.SetBuffer(kernel,"_Counters",Counters);shader.SetBuffer(kernel,"_Dirty",Dirty);
                shader.SetBuffer(kernel,"_Occupancy",occupancy);shader.SetBuffer(kernel,"_Properties",Properties);
                shader.SetBuffer(kernel,"_Upload",upload);shader.SetBuffer(kernel,"_Inspection",inspection);
            }
            uint total=0;
            for(int cy=0;cy<side;cy++)for(int cx=0;cx<side;cx++)
            {
                var data=AsteroidGenerator.GenerateChunk(seed,site??new StableId("00000000000000000000000000000001"),Origin.x/chunkSize+cx,Origin.y/chunkSize+cy,chunkSize,profile,catalog);
                var packed=new uint[data.Length];for(int i=0;i<data.Length;i++){packed[i]=data[i];if(data[i]!=0)total++;}
                UploadChunk(cy*side+cx,packed);
            }
            Stats=new uint[]{0,total,0,0};Counters.SetData(Stats);
        }
        GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
        RenderTexture Texture(GraphicsFormat format)
        {
            var value=new RenderTexture(new RenderTextureDescriptor(ChunkSize,ChunkSize){graphicsFormat=format,depthBufferBits=0,dimension=TextureDimension.Tex2DArray,volumeDepth=Side*Side,enableRandomWrite=true,msaaSamples=1});
            value.filterMode=FilterMode.Point;value.wrapMode=TextureWrapMode.Clamp;value.Create();return value;
        }
        public void UploadChunk(int slice,uint[] values)
        {
            if(snapshotPending)throw new InvalidOperationException("Snapshot owns the mutation fence.");
            if(slice<0||slice>=Side*Side||values.Length!=ChunkSize*ChunkSize)throw new ArgumentException("Invalid chunk payload.");
            upload.SetData(values);shader.SetInt("_UploadSlice",slice);shader.Dispatch(uploadKernel,(ChunkSize+7)/8,(ChunkSize+7)/8,1);
        }
        public void Step(SiteCommand? command=null,float force=0,Vector2 forcePosition=default)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MatterSession));
            if(snapshotPending)throw new InvalidOperationException("Snapshot owns the mutation fence.");
            const float delta=1f/60;Dispatches=0;
            if(command.HasValue)
            {
                var c=command.Value;
                if(c.RadiusCells<0||c.RadiusCells>16||float.IsNaN(c.RadiusCells)||!float.IsFinite(c.PositionCells.x)||!float.IsFinite(c.PositionCells.y)||!(c.Strength>=0))throw new ArgumentException("Invalid bounded cutter command.");
                shader.SetVector("_CutPosition",c.PositionCells);shader.SetVector("_Impulse",c.Direction);shader.SetFloat("_Radius",c.RadiusCells);shader.SetFloat("_Power",c.Strength);shader.SetFloat("_Delta",delta);
                shader.Dispatch(cutKernel,1,1,1);Dispatches++;
            }
            shader.SetInt("_Tick",++tick);shader.SetFloat("_Delta",delta);shader.SetFloat("_Force",force);shader.SetVector("_ForcePosition",forcePosition);
            for(int color=0;color<16;color++){shader.SetInt("_Color",color);shader.Dispatch(integrateKernel,(Capacity+63)/64,1,1);Dispatches++;}
        }
        public void PollStats()
        {
            if(statsPending||disposed)return;statsPending=true;ReadbackQueue++;
            AsyncGPUReadback.Request(Counters,r=>{statsPending=false;ReadbackQueue--;if(!disposed&&!r.hasError)Stats=r.GetData<uint>().ToArray();});
        }
        public void Inspect(Vector2Int position,Action<ushort> result)
        {
            if(inspectionPending||disposed)return;inspectionPending=true;ReadbackQueue++;
            shader.SetInts("_InspectPosition",position.x,position.y);shader.Dispatch(inspectKernel,1,1,1);
            AsyncGPUReadback.Request(inspection,r=>{inspectionPending=false;ReadbackQueue--;if(!disposed&&!r.hasError)result((ushort)r.GetData<uint>()[0]);});
        }
        public async Task<MatterSnapshot> SnapshotAsync()
        {
            if(disposed)throw new ObjectDisposedException(nameof(MatterSession));
            if(snapshotPending)throw new InvalidOperationException("Snapshot already pending.");
            snapshotPending=true;
            try
            {
            var counts=await Read<uint>(Counters);
            var cells=await Read<LooseCell>(Cells);Array.Resize(ref cells,(int)counts[0]);
            var fields=await ReadTexture<uint>(Field);var damage=await ReadTexture<float>(Damage);
            return new MatterSnapshot{Side=Side,ChunkSize=ChunkSize,Capacity=Capacity,OriginX=Origin.x,OriginY=Origin.y,Tick=tick,Cells=cells,Counters=counts,Dirty=await Read<uint>(Dirty),Fields=fields,Damage=damage};
            }
            finally {snapshotPending=false;}
        }
        public void Restore(MatterSnapshot snapshot)
        {
            if(snapshotPending||disposed)throw new InvalidOperationException("Session unavailable.");
            if(snapshot.Side!=Side||snapshot.ChunkSize!=ChunkSize||snapshot.Capacity!=Capacity||snapshot.OriginX!=Origin.x||snapshot.OriginY!=Origin.y||snapshot.Cells.Length>Capacity)
                throw new ArgumentException("Snapshot geometry/capacity mismatch.");
            CpuCutReference.Validate(snapshot);
            for(int i=0;i<Side*Side;i++)
            {
                UploadChunk(i,snapshot.Fields[i]);
                var bits=new uint[ChunkSize*ChunkSize];System.Buffer.BlockCopy(snapshot.Damage[i],0,bits,0,bits.Length*4);
                upload.SetData(bits);shader.SetInt("_UploadSlice",i);shader.Dispatch(damageKernel,(ChunkSize+7)/8,(ChunkSize+7)/8,1);
            }
            if(snapshot.Cells.Length>0)Cells.SetData(snapshot.Cells,0,0,snapshot.Cells.Length);
            Counters.SetData(snapshot.Counters);Dirty.SetData(snapshot.Dirty);occupancy.SetData(new int[Width*Width]);
            tick=snapshot.Tick;Stats=(uint[])snapshot.Counters.Clone();shader.Dispatch(restoreKernel,(Capacity+63)/64,1,1);
        }
        Task<T[]> Read<T>(GraphicsBuffer buffer) where T:struct
        {
            var task=new TaskCompletionSource<T[]>();ReadbackQueue++;
            AsyncGPUReadback.Request(buffer,r=>{ReadbackQueue--;if(r.hasError)task.SetException(new Exception("GPU snapshot readback failed."));else task.SetResult(r.GetData<T>().ToArray());});return task.Task;
        }
        Task<T[][]> ReadTexture<T>(RenderTexture texture) where T:struct
        {
            var task=new TaskCompletionSource<T[][]>();ReadbackQueue++;
            AsyncGPUReadback.Request(texture,0,r=>{ReadbackQueue--;if(r.hasError){task.SetException(new Exception("Chunk snapshot failed."));return;}var data=new T[Side*Side][];for(int i=0;i<data.Length;i++)data[i]=r.GetData<T>(i).ToArray();task.SetResult(data);});return task.Task;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;AsyncGPUReadback.WaitAllRequests();
            foreach(var b in new[]{Cells,Counters,Dirty,Properties,Palette,Shadows,Emissions,occupancy,upload,inspection})b.Dispose();
            Field.Release();Damage.Release();UnityEngine.Object.DestroyImmediate(Field);UnityEngine.Object.DestroyImmediate(Damage);UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
