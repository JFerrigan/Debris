using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Debris.Materials;
using Debris.Simulation;
using Debris.Sites;
using Debris.Ships;
using Debris.Persistence;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
namespace Debris.Presentation
{
    public sealed class Showcase : MonoBehaviour
    {
        ShipRuntime ship;ShipBlueprint loadedBlueprint;bool saveBusy;string saveStatus="F5 save • F9 load • P pump fuel • J release fuel • T other site";
        string SavePath=>Path.Combine(shipBenchmark?Application.temporaryCachePath:Application.persistentDataPath,shipBenchmark?"DebrisVerification":"Saves","salvage.debris");
        WorldManifest worldManifest;string worldRoot,currentSiteId="00000000000000000000000000000001";ulong currentSeed=42;
        MatterSession session;MatterView view;MaterialCatalog catalog;
        InputActionAsset input;Camera cameraView;bool paused,benchmark,shipBenchmark,contactBenchmark;
        float accumulator,statsTime;ushort inspected;Vector2 pointerWorld;
        readonly FrameTiming[] timings=new FrameTiming[1];
        readonly List<double> cpu=new List<double>(),gpu=new List<double>(),frames=new List<double>();
        GUIStyle title,label,small;Texture2D solid;
        public MatterSession Session=>session;
        void Start()
        {
            Application.targetFrameRate=60;cameraView=Camera.main;
            catalog=Resources.Load<MaterialCatalog>("Materials");input=Instantiate(Resources.Load<InputActionAsset>("Debris"));input.Enable();
            contactBenchmark=Array.Exists(Environment.GetCommandLineArgs(),a=>a=="-debrisContactBenchmark");
            shipBenchmark=contactBenchmark||Array.Exists(Environment.GetCommandLineArgs(),a=>a=="-debrisShipBenchmark");
            benchmark=Array.Exists(Environment.GetCommandLineArgs(),a=>a=="-debrisBenchmark");
            worldRoot=Path.Combine(shipBenchmark?Application.temporaryCachePath:Application.persistentDataPath,shipBenchmark?"DebrisVerification/world-"+Guid.NewGuid().ToString("N"):"World");
            ResetSession(benchmark?2:4,8192);
            if(benchmark)StartCoroutine(CheckedBenchmark(Benchmark()));
            if(contactBenchmark)StartCoroutine(CheckedBenchmark(ContactBenchmark()));
            else if(shipBenchmark)StartCoroutine(CheckedBenchmark(ShipBenchmark()));
            if(!benchmark&&!shipBenchmark&&WorldStore.Exists(worldRoot))_ = LoadCheckpoint();
        }
        void ResetSession(int side,int capacity)
        {
            view?.Dispose();session?.Dispose();if(loadedBlueprint){Destroy(loadedBlueprint);loadedBlueprint=null;}
            session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),side,128,capacity);
            ship=benchmark?null:new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            if(ship!=null){session.ConfigureShip(ship.CollisionMask(),ship.Position);session.ConfigureShipBody(ship.MassProperties(catalog));cameraView.orthographicSize=180;}
            view=new MatterView(session);accumulator=0;inspected=0;
        }
        void Update()
        {
            if(session==null)return;
            if(!benchmark&&!shipBenchmark&&!saveBusy)
            {
                if(input["Save"].WasPressedThisFrame())_ = SaveCheckpoint();
                else if(input["Load"].WasPressedThisFrame())_ = LoadCheckpoint();
                else if(input["PumpFuel"].WasPressedThisFrame())_ = TransferFuel(true);
                else if(input["SpillFuel"].WasPressedThisFrame())_ = TransferFuel(false);
                else if(input["VisitSite"].WasPressedThisFrame())_ = TravelTo(currentSiteId.EndsWith("1",StringComparison.Ordinal)?"00000000000000000000000000000002":"00000000000000000000000000000001");
            }
            if(!saveBusy&&!benchmark&&!shipBenchmark&&session.ImpactStats[3]!=0)
            {
                float speed=BitConverter.ToSingle(BitConverter.GetBytes(session.ImpactStats[2]),0);
                var point=new Vector2Int((int)session.ImpactStats[0]-64,(int)session.ImpactStats[1]-64);
                if(speed>6)_ = ApplyDamage(new[]{point},(speed-6)*10);else session.ClearImpact();
            }
            if(input["Pause"].WasPressedThisFrame())paused=!paused;
            if(input["Reset"].WasPressedThisFrame()&&!benchmark&&!shipBenchmark&&!saveBusy){if(WorldStore.Exists(worldRoot))_ = LoadCheckpoint();else ResetSession(4,8192);}
            var pointer=input["Pointer"].ReadValue<Vector2>();pointerWorld=cameraView.ScreenToWorldPoint(new Vector3(pointer.x,pointer.y,10));
            var movement=input["Move"].ReadValue<Vector2>();if(ship==null)cameraView.transform.position+=(Vector3)(movement*(cameraView.orthographicSize*Time.unscaledDeltaTime));
            else
            {
                if(!saveBusy&&input["CargoDoor"].WasPressedThisFrame()&&ship.Has(UnitKind.Door))ship.DoorOpen=!ship.DoorOpen;
                var pose=session.ShipStats[0];cameraView.transform.position=Vector3.Lerp(cameraView.transform.position,new Vector3(pose.x+50,pose.y,-10),1-Mathf.Exp(-4*Time.unscaledDeltaTime));
                ship.CargoMass=session.ShipStats[2].y;
                ship.Angle=session.ShipStats[0].z;
                ship.Position=new Vector2(pose.x,pose.y);
                ship.Velocity=new Vector2(session.ShipStats[1].x,session.ShipStats[1].y);ship.AngularVelocity=session.ShipStats[1].z;
            }
            cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-input["Zoom"].ReadValue<float>()*.025f,30,400);
            if(!paused&&!benchmark&&!shipBenchmark&&!saveBusy)
            {
                accumulator=Mathf.Min(accumulator+Time.deltaTime,4f/60);
                while(accumulator>=1f/60)
                {
                    SiteCommand? command=null;
                    if(ship!=null)
                    {
                        float turn=input["Turn"].ReadValue<float>();
                        var force=ship.FlightForce(new Vector2(movement.y,-movement.x),-turn,1f/60,ship.MassProperties(catalog));
                        session.ConfigureShipBody(ship.MassProperties(catalog));
                        bool cut=input["Cut"].IsPressed()&&ship.Has(UnitKind.Drill);
                        if(cut)command=new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,6,120,1);
                        bool suction=input["Suction"].IsPressed()&&ship.Has(UnitKind.Suction);
                        session.Step(command,suction?40:0,default,default,ship.DoorOpen,cut,suction,force);
                    }
                    else session.Step(command);
                    accumulator-=1f/60;
                }
            }
            statsTime+=Time.unscaledDeltaTime;
            if(statsTime>.2f){statsTime=0;
                if(!saveBusy&&!benchmark&&!shipBenchmark&&ship!=null&&ship.Fuel.Count>0&&ship.Units.Exists(u=>u.Placement.Definition.Kind==UnitKind.Tank&&(u.Destroyed||!u.Supported)))_ = TransferFuel(false);
                session.PollStats();session.Inspect(Vector2Int.FloorToInt(pointerWorld),m=>inspected=m);}
            view.Draw();
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)>0){if(benchmark||shipBenchmark){cpu.Add(timings[0].cpuFrameTime);if(timings[0].gpuFrameTime>0)gpu.Add(timings[0].gpuFrameTime);}}
            if(benchmark||shipBenchmark)frames.Add(Time.unscaledDeltaTime*1000);
        }
        async Task<bool> ApplyDamage(IEnumerable<Vector2Int> positions,float unitDamage=0)
        {
            saveBusy=true;
            try
            {
                var snapshot=await session.SnapshotAsync();
                using(var damage=ShipDamage.CutHull(snapshot,ship,positions,unitDamage))
                {
                    session.Restore(damage.Matter);if(loadedBlueprint)Destroy(loadedBlueprint);
                    ship=damage.Ship;loadedBlueprint=damage.Blueprint;damage.Blueprint=null;session.ConfigureShipBody(ship.MassProperties(catalog));
                    saveStatus=$"Impact damage: {damage.Released} hull cells released; {ship.Fragments.Count} detached regions.";
                }
                return true;
            }
            catch(Exception e){saveStatus="Damage admission deferred: "+e.Message;Debug.LogWarning(saveStatus);return false;}
            finally{saveBusy=false;accumulator=0;}
        }
        async Task<int> TransferFuel(bool pump)
        {
            if(ship==null||pump&&(!ship.Has(UnitKind.Tank)||!ship.Has(UnitKind.Suction))){saveStatus="Fuel pump requires a supported tank and suction unit.";return 0;}
            saveBusy=true;
            try
            {
                var state=await session.SnapshotAsync();var port=FuelTransfers.World(state,new Vector2(35,32));var outletVelocity=ship.Velocity;
                var tank=ship.Units.FirstOrDefault(u=>u.Placement.Definition.Kind==UnitKind.Tank);
                if(tank!=null&&tank.OwnerId!=ship.Id)
                {
                    var fragment=state.Fragments.Single(f=>f.Id==tank.OwnerId);var local=(Vector2)tank.Placement.Position+new Vector2(tank.Placement.Definition.Size.x*.6f,tank.Placement.Definition.Size.y+10);
                    float c=Mathf.Cos(fragment.Pose.z),sn=Mathf.Sin(fragment.Pose.z);port=new Vector2(fragment.Pose.x+local.x*c-local.y*sn,fragment.Pose.y+local.x*sn+local.y*c);outletVelocity=new Vector2(fragment.Motion.x,fragment.Motion.y);
                }
                FuelTransferResult proposal;
                if(pump)proposal=FuelTransfers.Pump(state,ship.Fuel,catalog,port,40);
                else
                {
                    var outlets=new List<Vector2>();
                    for(int y=0;y<8;y++)for(int x=0;x<8;x++)outlets.Add((Vector2)Vector2Int.FloorToInt(port+new Vector2(x*2,y*2)));
                    proposal=FuelTransfers.Spill(state,ship.Fuel,catalog,outlets,outletVelocity);
                }
                if(proposal.Count>0){session.Restore(proposal.Matter);ship.Fuel=proposal.Tank;}
                saveStatus=proposal.Count>0?$"{(pump?"Recovered":"Released")} {proposal.Count} fuel cells.":pump?"No recoverable fuel in pump range, or tank full.":"Fuel retained: tank empty, outlet blocked, or debris capacity full.";
                return proposal.Count;
            }
            catch(Exception e){saveStatus="Fuel transfer failed: "+e.Message;Debug.LogException(e);return 0;}
            finally{saveBusy=false;accumulator=0;}
        }
        async Task<SalvageSave> CaptureCurrent()
        {
            var matter=await session.SnapshotAsync();ShipDamage.SynchronizeFragments(matter,ship);var state=ShipSnapshot.Capture(ship);
            state.Position=new Vector2(matter.ShipPose[0].x,matter.ShipPose[0].y);state.Angle=matter.ShipPose[0].z;
            if(matter.ShipPose[1].w>0){state.Velocity=Vector2.zero;state.AngularVelocity=0;}
            return new SalvageSave{SiteId=currentSiteId,GeneratorSeed=currentSeed,Matter=matter,Ship=state,MaterialKeys=SalvageSave.Keys(catalog)};
        }
        async Task<WorldManifest> CommitWorld(SalvageSave active,params SalvageSave[] changes)
        {
            var profile=Resources.Load<AsteroidProfile>("Asteroid");
            var inputs=changes.Select(save=>(save,json:save.Ship==null?"":JsonUtility.ToJson(save.Ship),baseline:SparseSiteStore.Baseline(save,catalog,profile))).ToArray();
            long expected=worldManifest?.Revision??0;string root=worldRoot;
            return await Task.Run(()=>
            {
                var committed=WorldStore.Commit(root,expected,active.SiteId,active.Matter.NextIdentity,inputs.Select(p=>SparseSiteStore.Prepare(p.save,p.json,p.baseline)).ToArray());
                // Maintenance never changes the outcome of an already published gameplay transaction.
                try{var cleanup=WorldStore.CollectUnreferenced(root);if(cleanup.Deferred)Debug.LogWarning("World maintenance deferred: "+cleanup.Reason);}
                catch(Exception e){Debug.LogWarning("World maintenance deferred: "+e.Message);}
                return committed;
            });
        }
        async Task<bool> SaveCheckpoint()
        {
            saveBusy=true;saveStatus="Saving site…";
            try
            {
                var save=await CaptureCurrent();worldManifest=await CommitWorld(save,save);
                saveStatus="World saved. T visits the other salvage site; F9 reloads.";return true;
            }
            catch(Exception e){saveStatus="Save failed: "+e.Message;Debug.LogException(e);return false;}
            finally{saveBusy=false;accumulator=0;}
        }
        void Adopt(SalvageSave saved,MatterSession candidate,MatterView replacement,ShipRuntime restored,ShipBlueprint blueprint)
        {
            view.Dispose();session.Dispose();if(loadedBlueprint)Destroy(loadedBlueprint);
            ship=restored;loadedBlueprint=blueprint;session=candidate;view=replacement;currentSiteId=saved.SiteId;currentSeed=saved.GeneratorSeed;
        }
        async Task<bool> LoadCheckpoint()
        {
            saveBusy=true;saveStatus="Loading world…";MatterSession candidate=null;ShipBlueprint blueprint=null;MatterView replacement=null;
            try
            {
                SalvageSave saved;WorldManifest manifest=null;
                if(WorldStore.Exists(worldRoot))
                {
                    var result=await Task.Run(()=>{var m=WorldStore.Read(worldRoot);return (manifest:m,data:WorldStore.ReadSite(worldRoot,m,m.ActiveSiteId));});
                    saved=SparseSiteStore.Reconstruct(result.data,catalog,Resources.Load<AsteroidProfile>("Asteroid"));manifest=result.manifest;saved.RecoveredBackup=manifest.RecoveredBackup;
                }
                else
                {
                    var result=await Task.Run(()=>{var save=AtomicSalvageStore.Read(SavePath,out var json);return (save,json);});saved=result.save;
                    if(saved.GeneratorKey!="asteroid"||saved.GeneratorRevision!=1)throw new NotSupportedException("This site's generator revision is unavailable.");
                    SalvageSaveCodec.ResolveContent(saved,result.json,catalog);
                }
                if(saved.Ship==null)throw new InvalidDataException("This checkpoint has no player ship.");
                var restored=saved.Ship.Restore(out blueprint);var m=saved.Matter;
                candidate=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),m.Side,m.ChunkSize,m.Capacity,saved.GeneratorSeed,new Debris.Core.StableId(saved.SiteId));
                candidate.Restore(m);candidate.ConfigureShipBody(restored.MassProperties(catalog));replacement=new MatterView(candidate);
                Adopt(saved,candidate,replacement,restored,blueprint);blueprint=null;candidate=null;replacement=null;worldManifest=manifest;
                saveStatus=saved.RecoveredBackup?"Recovered the previous verified world; latest generation was damaged.":"Site restored, including cargo, fuel and damage. T visits the other site.";return true;
            }
            catch(Exception e){saveStatus="Load failed: "+e.Message;Debug.LogWarning(saveStatus);return false;}
            finally{replacement?.Dispose();candidate?.Dispose();if(blueprint)Destroy(blueprint);saveBusy=false;accumulator=0;}
        }
        async Task<bool> TravelTo(string destination)
        {
            if(destination==currentSiteId)return true;
            saveBusy=true;saveStatus="Saving departure and preparing arrival…";MatterSession candidate=null;MatterView replacement=null;ShipBlueprint blueprint=null;
            try
            {
                var current=await CaptureCurrent();
                if(current.Matter.Impact[3]!=0&&BitConverter.ToSingle(BitConverter.GetBytes(current.Matter.Impact[2]),0)<=6){session.ClearImpact();current.Matter.Impact=new uint[4];}
                var departure=SiteTransit.Depart(current);var profile=Resources.Load<AsteroidProfile>("Asteroid");
                var archived=worldManifest==null?null:await Task.Run(()=>WorldStore.ReadSite(worldRoot,worldManifest,destination));SalvageSave target;
                if(archived!=null)target=SparseSiteStore.Reconstruct(archived,catalog,profile);
                else
                {
                    candidate=new MatterSession(catalog,profile,current.Matter.Side,current.Matter.ChunkSize,current.Matter.Capacity,42,new Debris.Core.StableId(destination));
                    target=new SalvageSave{SiteId=destination,Matter=await candidate.SnapshotAsync(),MaterialKeys=SalvageSave.Keys(catalog)};
                }
                uint next=Math.Max(current.Matter.NextIdentity,worldManifest?.NextIdentity??1);SalvageSave arrived=null;
                foreach(var at in new[]{new Vector2(-175,0),new Vector2(-175,96),new Vector2(-175,-96),new Vector2(0,175),new Vector2(0,-175),new Vector2(175,0)})
                {
                    try{arrived=SiteTransit.Arrive(target,departure.Portable,next,at);break;}
                    catch(InvalidOperationException){/* Try another physically clear berth before retaining departure. */}
                }
                if(arrived==null)throw new InvalidOperationException("No clear arrival berth or debris capacity is available; current site retained.");
                var restored=arrived.Ship.Restore(out blueprint);var m=arrived.Matter;
                if(candidate==null)candidate=new MatterSession(catalog,profile,m.Side,m.ChunkSize,m.Capacity,arrived.GeneratorSeed,new Debris.Core.StableId(destination));
                candidate.Restore(m);candidate.ConfigureShipBody(restored.MassProperties(catalog));replacement=new MatterView(candidate);
                var committed=await CommitWorld(arrived,departure.Site,arrived);
                Adopt(arrived,candidate,replacement,restored,blueprint);candidate=null;replacement=null;blueprint=null;worldManifest=committed;
                saveStatus="Arrived at salvage site "+destination.Substring(30)+". Deposited matter remains at its original site.";return true;
            }
            catch(Exception e){saveStatus="Travel deferred: "+e.Message;Debug.LogWarning(saveStatus);return false;}
            finally{replacement?.Dispose();candidate?.Dispose();if(blueprint)Destroy(blueprint);saveBusy=false;accumulator=0;}
        }
        IEnumerator CheckedBenchmark(IEnumerator run)
        {
            while(true)
            {
                bool more=false;Exception failure=null;
                try{more=run.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){Debug.LogException(failure);Application.Quit(1);yield break;}
                if(!more)yield break;yield return run.Current;
            }
        }
        IEnumerator ContactBenchmark()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Logs"));Directory.CreateDirectory(output);
            var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
            var state=task.Result;
            foreach(var field in state.Fields)Array.Clear(field,0,field.Length);
            foreach(var damage in state.Damage)Array.Clear(damage,0,damage.Length);
            state.Cells=new[]{new LooseCell{Position=ship.Position+new Vector2(55,0),Material=1,Identity=1,Flags=1}};
            state.NextIdentity=2;state.Counters=new uint[]{1,1,0,0};Array.Clear(state.Dirty,0,state.Dirty.Length);
            state.ShipPose[1]=new Vector4(10,0,0,0);session.Restore(state);
            var mass=ship.MassProperties(catalog);session.ConfigureShipBody(mass);
            session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
            var hit=task.Result;float pixelMass=catalog.DefinitionAt(1).Density;
            if(hit.ShipPose[1].x<9.95f||hit.Cells[0].Velocity.x<9.9f||hit.ContactStats[0]==0||hit.ContactStats[1]!=0)
                throw new InvalidOperationException("Single-pixel momentum acceptance failed: "+hit.ShipPose[1]+" cell="+hit.Cells[0].Velocity+" fallbacks="+hit.ContactStats[1]);
            float momentumError=Mathf.Abs(hit.ShipPose[1].x*mass.Mass+hit.Cells[0].Velocity.x*pixelMass-10*mass.Mass);
            if(momentumError>mass.Mass*.00001f)throw new InvalidOperationException("Single-pixel momentum drift.");
            CpuCutReference.ValidateShipPlacement(hit,ship.Id);
            cpu.Clear();gpu.Clear();frames.Clear();
            for(int i=0;i<300;i++)
            {
                session.Step(shipForce:new Vector3(mass.Mass*.2f,0,0));yield return null;
                if(i%60==59)
                {
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
                    CpuCutReference.ValidateShipPlacement(task.Result,ship.Id);
                }
            }
            double frame95=Percentile(frames,.95),gpu95=Percentile(gpu,.95);
            var moving=task.Result;
            if(moving.ShipPose[1].x<10.8f||moving.ContactStats[1]!=0)throw new InvalidOperationException("Continued thrust/fallback acceptance failed.");
            var save=SaveCheckpoint();while(!save.IsCompleted)yield return null;if(!save.Result)throw new InvalidOperationException("Contact save failed.");
            var load=LoadCheckpoint();while(!load.IsCompleted)yield return null;if(!load.Result)throw new InvalidOperationException("Contact load failed.");
            task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
            if(!moving.Cells.SequenceEqual(task.Result.Cells)||!moving.ShipPose.SequenceEqual(task.Result.ShipPose))throw new InvalidOperationException("Contact checkpoint changed motion.");
            string line=$"preset=starter-single-sleeping-cell ship_mass={mass.Mass:F3} cell_mass={pixelMass:F3} initial_speed=10 impact_speed={hit.ShipPose[1].x:F6} pixel_speed={hit.Cells[0].Velocity.x:F6} final_speed={moving.ShipPose[1].x:F6} momentum_error={momentumError:F6} impulses={moving.ContactStats[0]} fallbacks={moving.ContactStats[1]} substeps={moving.ContactStats[2]} frame_p95={frame95:F3} gpu_p95={gpu95:F3} nonoverlap=true thrust=true save_load=true";
            File.WriteAllText(Path.Combine(output,"contact-benchmark.txt"),SystemInfo.graphicsDeviceName+" / Unity "+Application.unityVersion+"\n"+line);
            Debug.Log("DEBRIS_CONTACT_BENCHMARK "+line);Application.Quit();
        }
        IEnumerator ShipBenchmark()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Logs"));Directory.CreateDirectory(output);
            var initial=session.SnapshotAsync();while(!initial.IsCompleted)yield return null;
            if(initial.IsFaulted)throw initial.Exception;
            // Controlled loaded-cavity workload, explicitly separate from earned salvage.
            var state=initial.Result;var cells=new List<LooseCell>();
            for(int y=-24;y<24;y+=2)for(int x=-24;x<24;x+=2)cells.Add(new LooseCell{Position=new Vector2(x,y),Material=2,Identity=(uint)cells.Count+1,Flags=4});
            state.Cells=cells.ToArray();state.NextIdentity=(uint)cells.Count+1;state.Counters[0]=(uint)cells.Count;state.Counters[1]+=(uint)cells.Count;session.Restore(state);
            for(int i=0;i<60;i++){session.Step(shipMotion:new Vector3(0,.005f,.001f));yield return null;}
            ship.Velocity=new Vector2(0,-3);
            var damage=ApplyDamage(new[]{new Vector2Int(24,-28),new Vector2Int(24,-27),new Vector2Int(24,-26)});
            while(!damage.IsCompleted)yield return null;if(!damage.Result)throw new InvalidOperationException("Fragment showcase damage failed.");
            cpu.Clear();gpu.Clear();frames.Clear();
            for(int i=0;i<300;i++){session.Step(shipMotion:new Vector3(0,.005f,.001f));yield return null;}
            double frame95=Percentile(frames,.95),cpu95=Percentile(cpu,.95),gpu95=Percentile(gpu,.95);int dispatches=session.Dispatches;
            ship.Fuel.Consume(.75);double energyBefore=ship.Fuel.Energy;
            var transferWatch=System.Diagnostics.Stopwatch.StartNew();
            var release=TransferFuel(false);while(!release.IsCompleted)yield return null;long spillMs=transferWatch.ElapsedMilliseconds;
            transferWatch.Restart();var recovery=TransferFuel(true);while(!recovery.IsCompleted)yield return null;long pumpMs=transferWatch.ElapsedMilliseconds;
            if(release.Result!=8||recovery.Result!=8||Math.Abs(ship.Fuel.Energy-energyBefore)>1e-9)throw new InvalidOperationException("Player fuel transfer changed energy or cell count.");
            var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;
            CpuCutReference.Validate(task.Result);
            var saved=SaveCheckpoint();while(!saved.IsCompleted)yield return null;if(!saved.Result)throw new InvalidOperationException("Player save verification failed.");
            var loaded=LoadCheckpoint();while(!loaded.IsCompleted)yield return null;if(!loaded.Result)throw new InvalidOperationException("Player load verification failed.");
            var revisited=session.SnapshotAsync();while(!revisited.IsCompleted)yield return null;
            var original=task.Result;var restored=revisited.Result;
            if(original.Fragments.Length!=restored.Fragments.Length)throw new InvalidOperationException("Player fragment count changed.");
            for(int f=0;f<original.Fragments.Length;f++)if(original.Fragments[f].Pose!=restored.Fragments[f].Pose||original.Fragments[f].Motion!=restored.Fragments[f].Motion||!System.Linq.Enumerable.SequenceEqual(original.Fragments[f].Hull,restored.Fragments[f].Hull))throw new InvalidOperationException("Player fragment state changed.");
            if(!System.Linq.Enumerable.SequenceEqual(original.Cells,restored.Cells)||!System.Linq.Enumerable.SequenceEqual(original.ShipPose,restored.ShipPose))throw new InvalidOperationException("Player disk resume changed cargo or ship pose.");
            for(int i=0;i<original.Fields.Length;i++)if(!System.Linq.Enumerable.SequenceEqual(original.Fields[i],restored.Fields[i])||!System.Linq.Enumerable.SequenceEqual(original.Damage[i],restored.Damage[i]))throw new InvalidOperationException("Player disk resume changed terrain.");
            var travelWatch=System.Diagnostics.Stopwatch.StartNew();
            var leave=TravelTo("00000000000000000000000000000002");while(!leave.IsCompleted)yield return null;if(!leave.Result)throw new InvalidOperationException("Player departure failed.");
            loaded=LoadCheckpoint();while(!loaded.IsCompleted)yield return null;if(!loaded.Result||currentSiteId!="00000000000000000000000000000002")throw new InvalidOperationException("Player destination resume failed.");
            var returnTrip=TravelTo("00000000000000000000000000000001");while(!returnTrip.IsCompleted)yield return null;if(!returnTrip.Result)throw new InvalidOperationException("Player revisit failed.");
            long travelMs=travelWatch.ElapsedMilliseconds;
            revisited=session.SnapshotAsync();while(!revisited.IsCompleted)yield return null;var back=revisited.Result;
            if(!original.Cells.OrderBy(c=>c.Identity).SequenceEqual(back.Cells.OrderBy(c=>c.Identity))||back.Fragments.Length!=original.Fragments.Length)throw new InvalidOperationException("Player revisit changed cells/fragments.");
            for(int f=0;f<original.Fragments.Length;f++)if(original.Fragments[f].Pose!=back.Fragments[f].Pose||!original.Fragments[f].Hull.SequenceEqual(back.Fragments[f].Hull))throw new InvalidOperationException("Deposited fragment changed during absence.");
            for(int i=0;i<original.Fields.Length;i++)if(!original.Fields[i].SequenceEqual(back.Fields[i])||!original.Damage[i].SequenceEqual(back.Damage[i]))throw new InvalidOperationException("Player revisit changed terrain.");
            CpuCutReference.ValidateShipPlacement(back,ship.Id);
            string line=$"preset=damaged-rotating-starter fragments={session.FragmentCount} cells={task.Result.Cells.Length} chunks={session.Side*session.Side} capacity={session.Capacity} frame_p95={frame95:F3} cpu_p95={cpu95:F3} gpu_p95={gpu95:F3} buffers={session.BufferBytes} dispatches={dispatches} nonoverlap=true conserved=true disk_roundtrip=true fuel_roundtrip=true leave_revisit=true sites=2 spill_ms={spillMs} pump_ms={pumpMs} travel_resume_return_ms={travelMs} save_bytes={Directory.GetFiles(worldRoot,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length)} angle={task.Result.ShipPose[0].z:F3}";
            File.WriteAllText(Path.Combine(output,"ship-benchmark.txt"),SystemInfo.graphicsDeviceName+" / "+Application.unityVersion+"\n"+line);Debug.Log("DEBRIS_SHIP_BENCHMARK "+line);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"ship-showcase.png"));yield return null;yield return null;Application.Quit();
        }
        IEnumerator Benchmark()
        {
            string output=Path.Combine(Application.dataPath,"../../Logs");Directory.CreateDirectory(output);
            var reports=new List<string>();
            foreach(int capacity in new[]{1024,8192,32768})
            {
                ResetSession(capacity==32768?4:2,capacity);
                for(int step=0;step<120;step++){float a=step*.31f;session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*(step%65),Vector2.up*5,12,600,1));yield return null;}
                cpu.Clear();gpu.Clear();frames.Clear();
                for(int step=0;step<300;step++){float a=step*.13f;session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(Mathf.Cos(a),Mathf.Sin(a))*40,Vector2.up*5,16,600,1));yield return null;}
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                if(task.IsFaulted)throw task.Exception;
                var snapshot=task.Result;long fixedCount=0;foreach(var chunk in snapshot.Fields)foreach(var m in chunk)if(m!=0)fixedCount++;
                string line=$"capacity={capacity} chunks={session.Side*session.Side} fixed={fixedCount} loose={snapshot.Cells.Length} initial={snapshot.Counters[1]} overflow={snapshot.Counters[2]} conserved={fixedCount+snapshot.Cells.Length==snapshot.Counters[1]} cpu_p50={Percentile(cpu,.5):F3} cpu_p95={Percentile(cpu,.95):F3} gpu_p50={Percentile(gpu,.5):F3} gpu_p95={Percentile(gpu,.95):F3} frame_p95={Percentile(frames,.95):F3} gpu_samples={gpu.Count} buffers={session.BufferBytes} unity_allocated={Profiler.GetTotalAllocatedMemoryLong()}";
                reports.Add(line);Debug.Log("DEBRIS_BENCHMARK "+line);
            }
            File.WriteAllText(Path.Combine(output,"benchmark.txt"),SystemInfo.deviceModel+" / "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.operatingSystem+" / Unity "+Application.unityVersion+"\n"+string.Join("\n",reports));
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"showcase.png"));yield return null;yield return null;Application.Quit();
        }
        static double Percentile(List<double> values,double percentile){if(values.Count==0)return -1;var copy=values.ToArray();Array.Sort(copy);return copy[(int)((copy.Length-1)*percentile)];}
        void Styles()
        {
            if(title!=null)return;
            solid=new Texture2D(1,1);solid.SetPixel(0,0,Color.white);solid.Apply();
            title=new GUIStyle(GUI.skin.label){fontSize=32,fontStyle=FontStyle.Bold};title.normal.textColor=new Color(.87f,.92f,.9f);
            label=new GUIStyle(GUI.skin.label){fontSize=16};label.normal.textColor=new Color(.73f,.82f,.83f);
            small=new GUIStyle(label){fontSize=12,wordWrap=true};
        }
        void Panel(Rect r,Color c){GUI.color=c;GUI.DrawTexture(r,solid);GUI.color=Color.white;}
        void OnGUI()
        {
            if(session==null)return;Styles();
            Panel(new Rect(0,0,Screen.width,132),new Color(.025f,.045f,.065f,.97f));
            Panel(new Rect(28,28,4,72),new Color(.26f,.86f,.69f));
            GUI.Label(new Rect(48,22,650,48),"D E B R I S",title);
            GUI.Label(new Rect(50,76,700,26),"SALVAGE FLIGHT   /   EE INC. CONTRACTOR VESSEL",small);
            GUI.Label(new Rect(50,101,850,22),"W/S thrust • A/D strafe • Q/E turn • LMB drill • RMB suction • G cargo door • scroll zoom • Esc pause • R restore • T other site",small);
            float x=Screen.width-262;
            Panel(new Rect(x-18,152,262,Screen.height-180),new Color(.025f,.045f,.065f,.94f));
            GUI.Label(new Rect(x,174,230,30),"SITE  /  "+currentSiteId.Substring(28),label);
            var s=session.Stats;
            string info=$"FIXED MATTER     {s[1]-s[0]:N0}\nLOOSE CELLS       {s[0]:N0}\nPOOL CAPACITY  {session.Capacity:N0}\nDIRTY CHUNKS   {s[3]} / {session.Side*session.Side}\nTHROTTLED          {s[2]:N0}\nDISPATCHES         {session.Dispatches}\nREADBACKS          {session.ReadbackQueue}\nGPU BUFFERS      {session.BufferBytes/1048576f:F1} MiB\nFRAME                  {Time.unscaledDeltaTime*1000:F1} ms\nGPU                       {(timings[0].gpuFrameTime>0?timings[0].gpuFrameTime.ToString("F2")+" ms":"unavailable")}";
            GUI.Label(new Rect(x,220,230,240),info,small);
            Panel(new Rect(x,470,220,1),new Color(.18f,.3f,.32f));
            GUI.Label(new Rect(x,487,220,25),"MATERIAL INSPECTION",small);
            if(ship!=null)GUI.Label(new Rect(30,145,650,70),$"FUEL {ship.Fuel.Energy:F1}   /   CARGO {session.ShipStats[2].x:N0} / 2,500   /   DOOR {(session.ShipStats[2].z>0?"OPEN":"CLOSED")}",label);
            var material=catalog.DefinitionAt(inspected);
            GUI.Label(new Rect(x,520,220,65),material?material.MaterialKey.ToUpperInvariant()+"\n"+material.UnitValue+" credits / cell":"VACUUM",label);
            GUI.Label(new Rect(x,610,225,95),s[2]>0?"POOL SATURATED\nCutter throttled. Unreleased material remains in the asteroid.":"All released matter remains physical. Cells retain their mass and volume.",small);
            if(paused)GUI.Label(new Rect(Screen.width/2-80,145,180,30),"SIMULATION PAUSED",label);
            if(!paused&&Event.current.type==EventType.Repaint){var p=input["Pointer"].ReadValue<Vector2>();GUI.color=new Color(.25f,.95f,.75f);GUI.DrawTexture(new Rect(p.x-10,Screen.height-p.y,20,1),solid);GUI.DrawTexture(new Rect(p.x,Screen.height-p.y-10,1,20),solid);GUI.color=Color.white;}
            GUI.Label(new Rect(30,Screen.height-35,1050,24),saveStatus,small);
        }
        void OnDestroy(){if(loadedBlueprint)Destroy(loadedBlueprint);view?.Dispose();session?.Dispose();if(input){input.Disable();Destroy(input);}if(solid)Destroy(solid);}
    }
}
