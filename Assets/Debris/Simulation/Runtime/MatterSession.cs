using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Debris.Core;
using Debris.Materials;
using Debris.Sites;
using Debris.Ships;
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
    [Serializable] public struct FuelCellState { public uint Identity;public double Energy; }
    public sealed class MatterSnapshot
    {
        public int Side, ChunkSize, Capacity, OriginX, OriginY, Tick;
        public uint[][] Fields;
        public float[][] Damage;
        public LooseCell[] Cells;
        public uint[] Counters, Dirty, Hull;
        public Vector4[] ShipPose;
        public bool ShipEnabled;
        public uint NextIdentity;
        public FuelCellState[] FuelCells=Array.Empty<FuelCellState>();
        public RigidFragmentSnapshot[] Fragments=Array.Empty<RigidFragmentSnapshot>();
        public uint[] Impact=new uint[4];
        public uint[] ContactStats=new uint[4];
    }
    // Sole GPU resource owner; snapshots only at save/stream boundaries, compact async facts during play.
    public sealed class MatterSession : IDisposable
    {
        public const int CellStride=32;
        public readonly int Side, ChunkSize, Width, Capacity;
        public readonly Vector2Int Origin;
        public readonly RenderTexture Field, Damage;
        public readonly GraphicsBuffer Cells, Counters, Dirty, Properties, Palette, Shadows, Emissions;
        readonly GraphicsBuffer occupancy, upload, inspection, cargoOccupancy;
        public readonly GraphicsBuffer Hull, ShipPose,FragmentHull,FragmentPose;
        readonly GraphicsBuffer fragmentNextPose,impact,shipSweep,contactStats,hullAnchors,fragmentMass,fragmentContacts;
        readonly MaterialCatalog materialCatalog;
        readonly int forceKernel,contactKernel,physicalIntegrateKernel,gatherAnchorsKernel,gatherFragmentContactsKernel,solveFragmentContactsKernel;
        bool physicalShip;
        public uint[] ImpactStats {get;private set;}=new uint[4];
        bool impactPending;
        RigidFragmentSnapshot[] fragments=Array.Empty<RigidFragmentSnapshot>();
        public int FragmentCount=>fragments.Length;
        public bool ShipEnabled {get;private set;}
        uint[] hullData=new uint[128*128];
        public Vector4[] ShipStats {get;private set;}=new Vector4[3];
        readonly ComputeShader shader;
        readonly int uploadKernel,cutKernel,integrateKernel,inspectKernel,restoreKernel,damageKernel,moveShipKernel,transferKernel,fragmentKernel,prepareShipKernel,checkShipHullKernel,checkShipCargoKernel;
        bool disposed,statsPending,inspectionPending,snapshotPending,shipStatsPending;
        int tick;uint identityBase;FuelCellState[] fuelCells=Array.Empty<FuelCellState>();
        public uint[] Stats {get;private set;}=new uint[4];
        public int Dispatches {get;private set;}
        public int ReadbackQueue {get;private set;}
        public long BufferBytes => (long)Width*Width*12+(long)Capacity*CellStride+Side*Side*4+ChunkSize*ChunkSize*4+(Properties.count*64)+20+128*128*8+48+16*16384*4+32*16*2+24+16384*16+16*16+32768*16+16;
        public MatterSession(MaterialCatalog catalog,AsteroidProfile profile,int side=2,int chunkSize=128,int capacity=8192,ulong seed=42,StableId? site=null)
        {
            if(side<2||side>16||side%2!=0||chunkSize<8||chunkSize>256||capacity<1)throw new ArgumentOutOfRangeException();
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback)throw new NotSupportedException("Compute and async GPU readback are required.");
            catalog.Validate();profile.Validate(catalog);materialCatalog=catalog;
            Side=side;ChunkSize=chunkSize;Width=side*chunkSize;Capacity=capacity;Origin=new Vector2Int(-Width/2,-Width/2);
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("Matter"));
            Field=Texture(GraphicsFormat.R32_UInt);Damage=Texture(GraphicsFormat.R32_SFloat);
            Cells=Buffer(capacity,CellStride);Counters=Buffer(4,4);Dirty=Buffer(side*side,4);
            Hull=Buffer(128*128,4);cargoOccupancy=Buffer(128*128,4);ShipPose=Buffer(3,16);
            fragmentContacts=Buffer(32768,16);
            fragmentMass=Buffer(16,16);fragmentMass.SetData(new Vector4[16]);
            hullAnchors=Buffer(16384,16);
            contactStats=Buffer(4,4);contactStats.SetData(new uint[4]);
            shipSweep=Buffer(2,4);impact=Buffer(4,4);impact.SetData(new uint[4]);
            FragmentHull=Buffer(16*16384,4);
            FragmentPose=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.CopyDestination,32,16);
            fragmentNextPose=new GraphicsBuffer(GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.CopySource,32,16);
            FragmentHull.SetData(new uint[16*16384]);FragmentPose.SetData(new Vector4[32]);fragmentNextPose.SetData(new Vector4[32]);
            Hull.SetData(hullData);cargoOccupancy.SetData(new int[128*128]);ShipPose.SetData(ShipStats);
            occupancy=Buffer(Width*Width,4);upload=Buffer(chunkSize*chunkSize,4);inspection=Buffer(1,4);
            Properties=Buffer(catalog.Count+1,16);Palette=Buffer(catalog.Count+1,16);Shadows=Buffer(catalog.Count+1,16);Emissions=Buffer(catalog.Count+1,16);
            var properties=new Vector4[catalog.Count+1];var palette=new Vector4[catalog.Count+1];var shadows=new Vector4[catalog.Count+1];var emissions=new Vector4[catalog.Count+1];
            for(int m=1;m<=catalog.Count;m++){var d=catalog.DefinitionAt((ushort)m);properties[m]=new Vector4(d.Durability,d.Density,d.UnitValue,0);palette[m]=d.BaseColor;shadows[m]=d.ShadowColor;emissions[m]=(Vector4)(d.EmissiveColor*d.EmissiveIntensity);}
            Properties.SetData(properties);Palette.SetData(palette);Shadows.SetData(shadows);Emissions.SetData(emissions);
            Cells.SetData(new LooseCell[capacity]);Counters.SetData(new uint[4]);Dirty.SetData(new uint[side*side]);occupancy.SetData(new int[Width*Width]);
            uploadKernel=shader.FindKernel("Upload");cutKernel=shader.FindKernel("Cut");integrateKernel=shader.FindKernel("Integrate");inspectKernel=shader.FindKernel("Inspect");restoreKernel=shader.FindKernel("RestoreOccupancy");
            gatherFragmentContactsKernel=shader.FindKernel("GatherFragmentContacts");solveFragmentContactsKernel=shader.FindKernel("SolveFragmentContacts");gatherAnchorsKernel=shader.FindKernel("GatherShipAnchors");physicalIntegrateKernel=shader.FindKernel("IntegratePhysical");forceKernel=shader.FindKernel("ApplyShipForce");contactKernel=shader.FindKernel("SolveShipCells");
            prepareShipKernel=shader.FindKernel("PrepareShip");checkShipHullKernel=shader.FindKernel("CheckShipHull");checkShipCargoKernel=shader.FindKernel("CheckShipCargo");
            damageKernel=shader.FindKernel("UploadDamage");moveShipKernel=shader.FindKernel("MoveShip");transferKernel=shader.FindKernel("TransferCargo");fragmentKernel=shader.FindKernel("MoveFragment");
            shader.SetInt("_ChunkSize",chunkSize);shader.SetInt("_Side",side);shader.SetInt("_Width",Width);shader.SetInt("_Capacity",capacity);shader.SetInts("_Origin",Origin.x,Origin.y);
            foreach(int kernel in new[]{uploadKernel,cutKernel,integrateKernel,inspectKernel,restoreKernel,damageKernel,moveShipKernel,transferKernel,fragmentKernel,prepareShipKernel,checkShipHullKernel,checkShipCargoKernel,forceKernel,contactKernel,physicalIntegrateKernel,gatherAnchorsKernel,gatherFragmentContactsKernel,solveFragmentContactsKernel})
            {
                shader.SetBuffer(kernel,"_FragmentContacts",fragmentContacts);shader.SetBuffer(kernel,"_FragmentContactOutput",fragmentContacts);shader.SetBuffer(kernel,"_FragmentMass",fragmentMass);shader.SetBuffer(kernel,"_ContactCounts",Counters);shader.SetBuffer(kernel,"_HullAnchorOutput",hullAnchors);shader.SetBuffer(kernel,"_HullAnchors",hullAnchors);shader.SetBuffer(kernel,"_ContactStats",contactStats);shader.SetBuffer(kernel,"_ShipSweep",shipSweep);shader.SetBuffer(kernel,"_ShipImpact",impact);shader.SetBuffer(kernel,"_FragmentHull",FragmentHull);shader.SetBuffer(kernel,"_FragmentPose",FragmentPose);shader.SetBuffer(kernel,"_FragmentNextPose",fragmentNextPose);
                shader.SetBuffer(kernel,"_Hull",Hull);shader.SetBuffer(kernel,"_CargoOccupancy",cargoOccupancy);shader.SetBuffer(kernel,"_ShipPose",ShipPose);
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
        public void ConfigureShip(uint[] hull,Vector2 position,float angle=0)
        {
            if(snapshotPending||disposed)throw new InvalidOperationException("Session unavailable.");
            if(hull==null||hull.Length!=128*128)throw new ArgumentException("Starter hull requires a 128-square local mask.");
            hullData=(uint[])hull.Clone();Hull.SetData(hullData);ShipEnabled=true;
            ShipStats=new[]{new Vector4(position.x,position.y,angle,1),Vector4.zero,Vector4.zero};ShipPose.SetData(ShipStats);
            shader.SetInt("_ShipEnabled",1);
        }
        public void ConfigureShipBody(BodyMass mass)
        {
            if(snapshotPending||disposed||!ShipEnabled)throw new InvalidOperationException("Ship unavailable.");
            mass.Validate();physicalShip=true;shader.SetInt("_PhysicalShip",1);
            shader.SetVector("_ShipMass",new Vector4(mass.InverseMass,mass.InverseInertia,mass.Center.x,mass.Center.y));
        }
        public void UpdateHull(uint[] hull)
        {
            if(snapshotPending||disposed)throw new InvalidOperationException("Session unavailable.");
            if(hull==null||hull.Length!=16384)throw new ArgumentException("Invalid local hull.");
            hullData=(uint[])hull.Clone();Hull.SetData(hullData);
        }
        public void ConfigureFragments(RigidFragmentSnapshot[] values)
        {
            if(snapshotPending||disposed)throw new InvalidOperationException("Session unavailable.");
            if(values==null||values.Length>16)throw new ArgumentException("Active fragment budget exceeded; retain unloaded records before admitting more.");
            var mask=new uint[16*16384];var poses=new Vector4[32];var masses=new Vector4[16];var owned=new RigidFragmentSnapshot[values.Length];var ids=new HashSet<string>();
            for(int i=0;i<values.Length;i++)
            {
                var f=values[i];new StableId(f.Id);
                if(!ids.Add(f.Id)||f.Hull==null||f.Hull.Length!=16384)throw new ArgumentException("Invalid fragment identity/field.");
                Array.Copy(f.Hull,0,mask,i*16384,16384);poses[i*2]=f.Pose;poses[i*2+1]=f.Motion;
                if(!float.IsFinite(f.Motion.x)||!float.IsFinite(f.Motion.y)||!float.IsFinite(f.Motion.z)||Mathf.Abs(f.Motion.x)>96||Mathf.Abs(f.Motion.y)>96||Mathf.Abs(f.Motion.z)>1.44f)throw new ArgumentException("Fragment motion exceeds bounded substeps.");
                var body=f.Mass;
                if(body.Mass==0)
                {
                    float total=0,moment=0;Vector2 first=Vector2.zero;
                    for(int cell=0;cell<f.Hull.Length;cell++)if(f.Hull[cell]!=0)
                    {
                        float amount=materialCatalog.DefinitionAt((ushort)f.Hull[cell]).Density;var point=new Vector2(cell%128-63.5f,cell/128-63.5f);
                        total+=amount;first+=point*amount;moment+=amount*(point.sqrMagnitude+1f/6);
                    }
                    body=new BodyMass{Mass=Mathf.Max(.0001f,total),Center=first/Mathf.Max(.0001f,total),Inertia=Mathf.Max(.0001f,moment-first.sqrMagnitude/Mathf.Max(.0001f,total))};
                }
                body.Validate();masses[i]=new Vector4(body.InverseMass,body.InverseInertia,body.Center.x,body.Center.y);
                owned[i]=new RigidFragmentSnapshot{Mass=body,Id=f.Id,Hull=(uint[])f.Hull.Clone(),Pose=f.Pose,Motion=f.Motion};
            }
            fragments=owned;fragmentMass.SetData(masses);FragmentHull.SetData(mask);FragmentPose.SetData(poses);fragmentNextPose.SetData(poses);shader.SetInt("_FragmentCount",values.Length);
        }
        public void Step(SiteCommand? command=null,float force=0,Vector2 forcePosition=default,Vector3 shipMotion=default,bool doorOpen=false,bool mountedCut=false,bool mountedSuction=false,Vector3 shipForce=default)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MatterSession));
            if(snapshotPending)throw new InvalidOperationException("Snapshot owns the mutation fence.");
            if(!float.IsFinite(shipMotion.x)||!float.IsFinite(shipMotion.y)||!float.IsFinite(shipMotion.z)||Mathf.Abs(shipMotion.x)>.4f||Mathf.Abs(shipMotion.y)>.4f||Mathf.Abs(shipMotion.z)>.006f||!float.IsFinite(force)||force<0||!float.IsFinite(forcePosition.x)||!float.IsFinite(forcePosition.y))throw new ArgumentException("Invalid or unbounded motion/force command.");
            if(!float.IsFinite(shipForce.x)||!float.IsFinite(shipForce.y)||!float.IsFinite(shipForce.z))throw new ArgumentException("Invalid ship force.");
            const float delta=1f/60;Dispatches=0;
            shader.SetFloat("_Delta",delta);
            shader.SetVector("_ShipForce",shipForce);
            shader.SetInt("_MountedCut",mountedCut&&ShipEnabled?1:0);shader.SetInt("_MountedSuction",mountedSuction&&ShipEnabled?1:0);
            shader.SetInt("_DoorOpen",doorOpen?1:0);
            if(ShipEnabled&&!physicalShip){shader.SetVector("_ShipMotion",shipMotion);shader.Dispatch(prepareShipKernel,1,1,1);shader.Dispatch(checkShipHullKernel,256,1,1);shader.Dispatch(checkShipCargoKernel,(Capacity+63)/64,1,1);shader.Dispatch(moveShipKernel,1,1,1);Dispatches+=4;}
            if(command.HasValue)
            {
                var c=command.Value;
                if(c.RadiusCells<0||c.RadiusCells>16||float.IsNaN(c.RadiusCells)||!float.IsFinite(c.PositionCells.x)||!float.IsFinite(c.PositionCells.y)||!(c.Strength>=0))throw new ArgumentException("Invalid bounded cutter command.");
                shader.SetVector("_CutPosition",c.PositionCells);shader.SetVector("_Impulse",c.Direction);shader.SetFloat("_Radius",c.RadiusCells);shader.SetFloat("_Power",c.Strength);shader.SetFloat("_Delta",delta);
                shader.Dispatch(cutKernel,1,1,1);Dispatches++;
            }
            if(ShipEnabled&&physicalShip){shader.Dispatch(forceKernel,1,1,1);Dispatches++;}
            int substeps=ShipEnabled&&physicalShip?4:1;
            shader.SetFloat("_Delta",delta/substeps);shader.SetFloat("_Force",force);shader.SetVector("_ForcePosition",forcePosition);
            for(int substep=0;substep<substeps;substep++)
            {
                shader.SetInt("_Tick",++tick);
                if(ShipEnabled&&physicalShip){shader.Dispatch(prepareShipKernel,1,1,1);shader.Dispatch(gatherAnchorsKernel,256,1,1);
                    for(int f=0;f<fragments.Length;f++){shader.SetInt("_MovingFragment",f);shader.Dispatch(gatherFragmentContactsKernel,256,1,1);shader.Dispatch(solveFragmentContactsKernel,1,1,1);Graphics.CopyBuffer(fragmentNextPose,FragmentPose);Dispatches+=2;}
                    shader.Dispatch(contactKernel,1,1,1);if(fragments.Length>0)Graphics.CopyBuffer(fragmentNextPose,FragmentPose);Dispatches+=3;}
                if(ShipEnabled&&physicalShip){shader.Dispatch(physicalIntegrateKernel,1,1,1);Dispatches++;}
                else for(int domain=0;domain<(ShipEnabled?2:1);domain++)
                {
                    shader.SetInt("_Domain",domain);
                    for(int color=0;color<16;color++){shader.SetInt("_Color",color);shader.Dispatch(integrateKernel,(Capacity+63)/64,1,1);Dispatches++;}
                }
                if(ShipEnabled&&physicalShip){shader.Dispatch(checkShipHullKernel,256,1,1);shader.Dispatch(checkShipCargoKernel,(Capacity+63)/64,1,1);shader.Dispatch(moveShipKernel,1,1,1);Dispatches+=3;
                    shader.SetFloat("_FragmentDelta",delta/substeps);
                    for(int f=0;f<fragments.Length;f++){shader.SetInt("_MovingFragment",f);shader.Dispatch(fragmentKernel,1,1,1);Graphics.CopyBuffer(fragmentNextPose,FragmentPose);Dispatches++;}
                }
            }
            if(ShipEnabled){shader.Dispatch(transferKernel,1,1,1);Dispatches++;}
            if(!physicalShip)for(int f=0;f<fragments.Length;f++)
            {
                shader.SetInt("_MovingFragment",f);var motion=fragments[f].Motion;
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(Mathf.Max(Mathf.Abs(motion.x),Mathf.Abs(motion.y))/12,Mathf.Abs(motion.z)/.18f)));
                shader.SetFloat("_FragmentDelta",delta/steps);
                for(int substep=0;substep<steps;substep++){shader.Dispatch(fragmentKernel,1,1,1);Graphics.CopyBuffer(fragmentNextPose,FragmentPose);Dispatches++;}
            }
        }
        public void ClearImpact(){impact.SetData(new uint[4]);ImpactStats=new uint[4];}
        public void PollStats()
        {
            if(ShipEnabled&&!impactPending&&!disposed)
            {
                impactPending=true;ReadbackQueue++;
                AsyncGPUReadback.Request(impact,r=>{impactPending=false;ReadbackQueue--;if(!disposed&&!r.hasError)ImpactStats=r.GetData<uint>().ToArray();});
            }
            if(ShipEnabled&&!shipStatsPending&&!disposed)
            {
                shipStatsPending=true;ReadbackQueue++;
                AsyncGPUReadback.Request(ShipPose,r=>{shipStatsPending=false;ReadbackQueue--;if(!disposed&&!r.hasError)ShipStats=r.GetData<Vector4>().ToArray();});
            }
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
            var fragmentStates=new RigidFragmentSnapshot[fragments.Length];
            if(fragments.Length>0)
            {
                var poses=await Read<Vector4>(FragmentPose);
                for(int i=0;i<fragments.Length;i++)fragmentStates[i]=new RigidFragmentSnapshot{Mass=fragments[i].Mass,Id=fragments[i].Id,Hull=(uint[])fragments[i].Hull.Clone(),Pose=poses[i*2],Motion=poses[i*2+1]};
            }
            return new MatterSnapshot{Side=Side,ChunkSize=ChunkSize,Capacity=Capacity,OriginX=Origin.x,OriginY=Origin.y,Tick=tick,ContactStats=await Read<uint>(contactStats),Impact=await Read<uint>(impact),Fragments=fragmentStates,Cells=cells,NextIdentity=identityBase+counts[0]+1,FuelCells=(FuelCellState[])fuelCells.Clone(),Hull=(uint[])hullData.Clone(),ShipEnabled=ShipEnabled,ShipPose=await Read<Vector4>(ShipPose),Counters=counts,Dirty=await Read<uint>(Dirty),Fields=fields,Damage=damage};
            }
            finally {snapshotPending=false;}
        }
        public void Restore(MatterSnapshot snapshot)
        {
            if(snapshotPending||disposed)throw new InvalidOperationException("Session unavailable.");
            if(snapshot.Side!=Side||snapshot.ChunkSize!=ChunkSize||snapshot.Capacity!=Capacity||snapshot.OriginX!=Origin.x||snapshot.OriginY!=Origin.y||snapshot.Cells.Length>Capacity)
                throw new ArgumentException("Snapshot geometry/capacity mismatch.");
            CpuCutReference.Validate(snapshot);
            ConfigureFragments(snapshot.Fragments);impact.SetData(snapshot.Impact);ImpactStats=(uint[])snapshot.Impact.Clone();
            uint maximum=0;foreach(var cell in snapshot.Cells)maximum=Math.Max(maximum,cell.Identity);
            uint next=snapshot.NextIdentity==0?checked(maximum+1):snapshot.NextIdentity;
            if(next<=maximum||(ulong)next+(uint)(Capacity-snapshot.Cells.Length)>uint.MaxValue)throw new InvalidOperationException("Cell identity range exhausted or invalid.");
            identityBase=next-(uint)snapshot.Cells.Length-1;shader.SetInt("_IdentityBase",unchecked((int)identityBase));
            fuelCells=(FuelCellState[])snapshot.FuelCells.Clone();
            ShipEnabled=snapshot.ShipEnabled;shader.SetInt("_ShipEnabled",ShipEnabled?1:0);
            if(snapshot.Hull!=null){hullData=(uint[])snapshot.Hull.Clone();Hull.SetData(hullData);}
            if(snapshot.ShipPose!=null){ShipStats=(Vector4[])snapshot.ShipPose.Clone();ShipPose.SetData(ShipStats);shader.SetInt("_DoorOpen",ShipStats[2].z>0?1:0);}
            cargoOccupancy.SetData(new int[128*128]);
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
            foreach(var b in new[]{Cells,Counters,Dirty,Properties,Palette,Shadows,Emissions,occupancy,upload,inspection,Hull,ShipPose,cargoOccupancy,FragmentHull,FragmentPose,fragmentNextPose,impact,shipSweep,contactStats,hullAnchors,fragmentMass,fragmentContacts})b.Dispose();
            Field.Release();Damage.Release();UnityEngine.Object.DestroyImmediate(Field);UnityEngine.Object.DestroyImmediate(Damage);UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
