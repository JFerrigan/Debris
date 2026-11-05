using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Debris.Simulation.Playground;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Debris.Presentation
{
    public sealed class PhysicsPlaygroundBootstrap : MonoBehaviour
    {
        enum Tool { Select,Grains,Rectangle,Erase,Push,Anchor }
        PlaygroundEditTransaction editor;
        CoupledPlaygroundView view;
        Camera cameraView;
        Tool tool;
        PlaygroundPreset preset=PlaygroundPreset.HeavyBlock;
        PlaygroundObject inspected,preview;
        PlaygroundInput lastInput;
        string selected,status="Opening physics workbench…",sceneName="workbench.json";
        string spawnVx="0",spawnVy="0",spawnSpin="0",gravityX="0",gravityY="-9.81";
        Vector2 localAnchor,pointer,previousPointer,bodyStart,pushStart,queuedImpulse,scroll;
        bool running,stepRequested,grabbing,drawingBody,pushing,focused=true,keepWalls=true,ignoreAnchors,
            bodyAnchored,localAxes,diagnostics,previewValid=true,previewPending,faultLogged,pointerRequest;
        float radius=.5f,density=1,sizeX=4,sizeY=4,spawnAngle,force=400,speed=1,accumulator,brushTime,previewTime,rateTime;
        int pointerSerial,rateTicks;
        ulong nextCommandId;
        double ticksPerSecond;
        string px="0",py="0",vx="0",vy="0",angle="0",spin="0",editDensity="1",width="1",height="1";
        bool editAnchor;
        Rect Panel=>new Rect(12,12,310,Mathf.Max(250,Screen.height-24));
        CoupledPlaygroundSession Session=>editor?.Current;
        bool Busy=>editor==null||editor.Busy;
        string DirectoryPath=>Path.Combine(Application.persistentDataPath,"PhysicsPlayground");
        string ScenePath=>Path.Combine(DirectoryPath,Path.GetFileName(string.IsNullOrWhiteSpace(sceneName)?"workbench.json":sceneName));
        string FailurePath=>Path.Combine(DirectoryPath,"failure.json");

        async void Start()
        {
            Application.targetFrameRate=60;
            cameraView=new GameObject("Physics playground camera",typeof(Camera)).GetComponent<Camera>();
            cameraView.tag="MainCamera";cameraView.orthographic=true;cameraView.orthographicSize=30;
            cameraView.transform.position=new Vector3(-8,0,-10);cameraView.nearClipPlane=.1f;cameraView.farClipPlane=100;
            cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color(.035f,.05f,.07f);
            editor=new PlaygroundEditTransaction();
            try
            {
                view=new CoupledPlaygroundView(cameraView);
                editor.Freeze+=world=>{CancelManipulation();view.Freeze(world);};
                editor.Rebound+=()=>{view.ClearFrozen();faultLogged=false;};
                await ResetPreset();
            }
            catch(Exception e){status="Startup failed: "+e.Message;Debug.LogError(status);}
        }
        void Update()
        {
            if(cameraView==null)return;
            var mouse=Mouse.current;var keyboard=Keyboard.current;
            Vector2 screen=mouse?.position.ReadValue()??Vector2.zero;
            pointer=cameraView.ScreenToWorldPoint(new Vector3(screen.x,screen.y,10));
            Vector2 guiPoint=new Vector2(screen.x,Screen.height-screen.y);
            bool overUI=Panel.Contains(guiPoint)||new Rect(338,12,Mathf.Max(100,Screen.width-350),56).Contains(guiPoint);
            bool text=GUI.GetNameOfFocusedControl().StartsWith("edit",StringComparison.Ordinal);
            if(Session?.Poll()==true)
            {
                if(Session.Faulted)
                {
                    running=false;stepRequested=false;queuedImpulse=lastInput.Impulse;status=Session.Fault;
                    if(!faultLogged){Debug.LogError("PHYSICS_PLAYGROUND "+status);faultLogged=true;}
                }
                else rateTicks++;
            }
            rateTime+=Time.unscaledDeltaTime;
            if(rateTime>=1){ticksPerSecond=rateTicks/rateTime;rateTicks=0;rateTime=0;}
            if(mouse!=null&&focused)
            {
                bool pan=mouse.middleButton.isPressed||((keyboard?.altKey.isPressed??false)&&mouse.leftButton.isPressed);
                if(!overUI&&pan)
                {
                    Vector2 delta=(screen-previousPointer)*(cameraView.orthographicSize*2/Screen.height);
                    cameraView.transform.position-=(Vector3)delta;CancelManipulation();
                }
                if(!overUI&&mouse.scroll.ReadValue().y!=0)
                {
                    Vector2 before=cameraView.ScreenToWorldPoint(new Vector3(screen.x,screen.y,10));
                    cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize*Mathf.Exp(-mouse.scroll.ReadValue().y*.001f),3,300);
                    Vector2 after=cameraView.ScreenToWorldPoint(new Vector3(screen.x,screen.y,10));
                    cameraView.transform.position+=(Vector3)(before-after);
                }
                if(mouse.rightButton.wasPressedThisFrame)CancelManipulation();
                if(!mouse.leftButton.isPressed)
                {
                    grabbing=false;
                    if(drawingBody){var shape=RectanglePreview();drawingBody=false;if(!overUI&&!Busy)_=AddRectangle(shape);}
                    if(pushing){pushing=false;if(!overUI&&!Busy){queuedImpulse=Vector2.ClampMagnitude(pointer-pushStart,force/60);stepRequested=!running;}}
                }
                if(!Busy&&!overUI&&!pan&&!text)
                {
                    if(mouse.leftButton.wasPressedThisFrame)
                    {
                        if(tool==Tool.Select||tool==Tool.Push||tool==Tool.Anchor)_=PickTool(pointer,tool);
                        else if(tool==Tool.Rectangle){bodyStart=pointer;drawingBody=true;}
                    }
                    brushTime-=Time.unscaledDeltaTime;
                    if(mouse.leftButton.isPressed&&brushTime<=0&&(tool==Tool.Grains||tool==Tool.Erase))
                    {brushTime=.15f;if(tool==Tool.Grains)_=PaintGrains(pointer);else _=Erase(pointer);}
                }
            }
            previousPointer=screen;
            if(focused&&!text&&!Busy&&keyboard!=null)
            {
                for(int i=0;i<6;i++)if(keyboard[(Key)((int)Key.Digit1+i)].wasPressedThisFrame)SetTool((Tool)i);
                if(keyboard.spaceKey.wasPressedThisFrame)ToggleRun();
                if(keyboard.periodKey.wasPressedThisFrame){running=false;stepRequested=true;}
                if(keyboard.rKey.wasPressedThisFrame)_=ResetPreset();
                if(keyboard.escapeKey.wasPressedThisFrame)CancelManipulation();
                if(keyboard.deleteKey.wasPressedThisFrame||keyboard.backspaceKey.wasPressedThisFrame)_=DeleteSelected();
            }
            var session=Session;
            if(!Busy&&session!=null&&!session.Faulted&&session.Count>0)
            {
                if(running)accumulator=Mathf.Min(accumulator+Time.unscaledDeltaTime*speed,2f/60);
                if(!session.Pending&&!pointerRequest&&(stepRequested||(running&&accumulator>=1f/60)))
                {
                    Vector2 drive=Vector2.zero;float turn=0;
                    if(focused&&!text&&keyboard!=null)
                    {
                        drive=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),
                            (keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
                        turn=(keyboard.eKey.isPressed?1:0)-(keyboard.qKey.isPressed?1:0);
                    }
                    var command=new PlaygroundInput{WorldGeneration=session.Generation,CommandId=++nextCommandId,SelectedId=selected,Cursor=pointer,LocalAnchor=localAnchor,
                        Pull=grabbing,Drive=drive,Turn=turn,MaximumForce=force,LocalAxes=localAxes,Impulse=queuedImpulse};
                    if(session.Submit(command))
                    {lastInput=command;queuedImpulse=Vector2.zero;stepRequested=false;accumulator=Mathf.Max(0,accumulator-1f/60);}
                    else if(session.Faulted){running=false;status=session.Fault;}
                }
            }
            else if(session?.Count==0){stepRequested=false;accumulator=0;}
            preview=tool==Tool.Grains?new PlaygroundObject{Shape=PlaygroundShape.Grain,Center=GridPoint(pointer),Density=density}:
                tool==Tool.Rectangle?RectanglePreview():null;
            if(preview!=null&&!Busy&&!overUI)
            {
                previewTime-=Time.unscaledDeltaTime;
                if(!pointerRequest&&!previewPending&&previewTime<=0&&session!=null&&!session.Pending){previewTime=.15f;_=CheckPreview(preview,session);}
            }
            view?.DrawWorld(session,Busy||session==null,selected,localAnchor,grabbing);
            if(!Busy&&!overUI&&preview!=null)view?.Preview(preview,previewValid);
            if(!Busy&&!overUI&&(tool==Tool.Grains||tool==Tool.Erase))view?.Brush(pointer,radius,tool==Tool.Erase);
            if(pushing)view?.Arrow(pushStart,pointer);
        }
        void SetTool(Tool value){CancelManipulation();tool=value;GUI.FocusControl(null);}
        void CancelManipulation(){pointerSerial++;grabbing=drawingBody=pushing=false;queuedImpulse=Vector2.zero;stepRequested=false;}
        void OnApplicationFocus(bool value){focused=value;if(!value){CancelManipulation();running=false;}}
        void OnDisable(){CancelManipulation();running=false;}
        void ToggleRun()
        {CancelManipulation();if(Session?.Faulted==true){status="Edit or reset the rejected world before resuming";return;}running=!running;accumulator=0;}
        static Vector2 GridPoint(Vector2 p)=>new Vector2(Mathf.Floor(p.x)+.5f,Mathf.Floor(p.y)+.5f);
        PlaygroundObject RectanglePreview()
        {
            Vector2 delta=pointer-bodyStart;
            bool drawn=drawingBody&&delta.magnitude>.5f;
            return new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Center=drawn?(bodyStart+pointer)*.5f:pointer,
                Half=drawn?new Vector2(Mathf.Max(.125f,Mathf.Abs(delta.x)*.5f),Mathf.Max(.125f,Mathf.Abs(delta.y)*.5f)):new Vector2(sizeX,sizeY)*.5f,
                Density=density,Angle=spawnAngle*Mathf.Deg2Rad,Anchored=bodyAnchored};
        }
        async Task CheckPreview(PlaygroundObject value,CoupledPlaygroundSession owner)
        {
            previewPending=true;
            try { bool valid=await owner.CheckPlacementAsync(value);if(Session==owner)previewValid=valid; }
            catch(Exception e){status="Placement preview unavailable: "+e.Message;}
            finally { previewPending=false; }
        }
        async Task PickTool(Vector2 at,Tool action)
        {
            var owner=Session;if(owner==null||pointerRequest)return;int serial=++pointerSerial;pointerRequest=true;
            try
            {
                while(owner.Pending&&Session==owner&&!Busy&&serial==pointerSerial)await Task.Yield();
                if(this==null||Session!=owner||Busy||serial!=pointerSerial)return;
                var hit=await owner.PickAsync(at,action==Tool.Anchor||!ignoreAnchors);
                if(this==null||Session!=owner||serial!=pointerSerial||hit.Generation!=owner.Generation)return;
                selected=hit.Id;localAnchor=hit.LocalPoint;
                if(selected==null)return;
                if(action==Tool.Anchor)
                    await Mutate("Change anchor",world=>{
                        var o=world.Find(selected);if(o==null)throw new InvalidOperationException("Selection was removed");
                        o.Anchored=!o.Anchored;o.Wall=false;o.Velocity=Vector2.zero;o.Spin=0;return world;
                    });
                else
                {
                    if(!hit.Anchored&&(Mouse.current?.leftButton.isPressed??false)&&focused)
                    {if(action==Tool.Select)grabbing=true;else {pushing=true;pushStart=at;}}
                    await InspectSelected();
                }
            }
            catch(Exception e){status="Picking failed: "+e.Message;}
            finally { pointerRequest=false; }
        }
        async Task InspectSelected()
        {
            var owner=Session;string id=selected;if(owner==null||id==null){inspected=null;return;}if(owner.Pending)return;
            try
            {
                var value=await owner.InspectAsync(id);if(this==null||Session!=owner||id!=selected||value==null)return;
                inspected=value;px=Text(value.Center.x);py=Text(value.Center.y);vx=Text(value.Velocity.x);vy=Text(value.Velocity.y);
                angle=Text(value.Angle*Mathf.Rad2Deg);spin=Text(value.Spin);editDensity=Text(value.Density);
                width=Text(value.Half.x*2);height=Text(value.Half.y*2);editAnchor=value.Anchored;
            }
            catch(Exception e){status="Inspection failed: "+e.Message;}
        }
        async Task<bool> Mutate(string name,Func<PlaygroundWorldDefinition,PlaygroundWorldDefinition> change,bool resume=true)
        {
            if(Busy)return false;bool wasRunning=running;running=false;CancelManipulation();
            bool ok=await editor.ApplyAsync(name,change);if(this==null)return false;
            status=editor.Status;accumulator=0;
            if(Session?.IndexOf(selected)<0){selected=null;inspected=null;}
            if(ok&&resume)running=wasRunning;
            if(ok&&selected!=null)await InspectSelected();return ok;
        }
        async Task ResetPreset()
        {
            if(Busy)return;running=false;selected=null;inspected=null;
            HomeCamera();
            await Mutate("Reset "+preset,w=>PlaygroundWorldDefinition.Preset(preset),false);
        }
        void HomeCamera(){if(cameraView){cameraView.transform.position=new Vector3(-8,0,-10);cameraView.orthographicSize=30;}}
        Task AddRectangle(PlaygroundObject value)=>Mutate("Add rectangle",world=>{
            if(!value.Anchored){value.Velocity=new Vector2(Number(spawnVx),Number(spawnVy));value.Spin=Number(spawnSpin);}
            if(!world.TryAdd(value,out var reason))throw new InvalidOperationException(reason);return world;
        });
        async Task PaintGrains(Vector2 at)
        {
            int placed=0,blocked=0;string refused=null;
            bool ok=await Mutate("Paint grains",world=>{
            int extent=Mathf.CeilToInt(radius);Vector2 center=GridPoint(at);
            for(int y=-extent;y<=extent&&placed<32;y++)for(int x=-extent;x<=extent&&placed<32;x++)
            {
                Vector2 p=center+new Vector2(x,y);if((p-center).magnitude>radius)continue;
                var o=new PlaygroundObject{Shape=PlaygroundShape.Grain,Center=p,Density=density};
                if(world.TryAdd(o,out var reason))placed++;else {blocked++;refused=reason;}
            }
            if(placed==0)throw new InvalidOperationException("No clear grain sites ("+blocked+" blocked)");
            return world;
            });
            if(ok)status+=$": {placed} placed; {blocked} blocked"+(refused==null?"":" ("+refused+")")+(placed==32?"; batch limit reached":"");
        }
        async Task Erase(Vector2 at)
        {
            var owner=Session;if(owner==null||pointerRequest||Busy)return;int serial=++pointerSerial;pointerRequest=true;
            try
            {
                while(owner.Pending&&Session==owner&&!Busy&&serial==pointerSerial)await Task.Yield();
                if(this==null||Session!=owner||Busy||serial!=pointerSerial)return;
                var hit=await owner.PickAsync(at,true);if(Session!=owner)return;string id=hit.Id;
                await Mutate("Erase",world=>{
                    int count=world.Objects.RemoveAll(o=>(o.Shape==PlaygroundShape.Grain&&(o.Center-at).magnitude<=radius)||
                        (o.Id==id&&(!o.Wall||!keepWalls)));
                    if(count==0)throw new InvalidOperationException("No removable object under brush");return world;
                });
            }
            catch(Exception e){status="Erase failed: "+e.Message;}
            finally { pointerRequest=false; }
        }
        Task DeleteSelected()=>Mutate("Delete selection",world=>{
            var o=world.Find(selected);if(o==null)throw new InvalidOperationException("Select an object first");
            if(o.Wall&&keepWalls)throw new InvalidOperationException("Uncheck keep walls to delete an arena wall");
            world.Objects.Remove(o);return world;
        });
        Task ApplyInspector()=>Mutate("Edit properties",world=>{
            var o=world.Find(selected);if(o==null)throw new InvalidOperationException("Selection was removed");
            var old=o.Copy();o.Center=new Vector2(Number(px),Number(py));o.Velocity=new Vector2(Number(vx),Number(vy));
            o.Angle=Number(angle)*Mathf.Deg2Rad;o.Spin=Number(spin);o.Density=Number(editDensity);
            if(o.Shape==PlaygroundShape.Rectangle)o.Half=new Vector2(Number(width),Number(height))*.5f;
            if(o.Anchored!=editAnchor)o.Wall=false;o.Anchored=editAnchor;
            if(o.Anchored){o.Velocity=Vector2.zero;o.Spin=0;}
            if((o.Center!=old.Center||o.Half!=old.Half||o.Angle!=old.Angle)&&!world.CanPlace(o,o.Id))
                throw new InvalidOperationException("Edited shape placement is blocked");return world;
        },false);
        static string Text(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static float Number(string text)
        {
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))
                throw new ArgumentException("Enter finite numbers using a decimal point");return value;
        }
        async Task SaveScene()
        {
            if(Busy)return;bool resume=running;running=false;CancelManipulation();
            try { var world=await editor.CaptureAsync();PlaygroundSceneFile.Save(ScenePath,world);status="Saved "+ScenePath; }
            catch(Exception e){status="Save failed: "+e.Message;}
            finally { if(this!=null&&Session?.Faulted!=true)running=resume; }
        }
        async Task LoadScene()
        {
            try { var world=PlaygroundSceneFile.Load(ScenePath);await Mutate("Load scene",w=>world,false); }
            catch(Exception e){status="Load refused: "+e.Message;}
        }
        async Task ExportFailure()
        {
            if(Busy)return;running=false;CancelManipulation();
            try
            {
                var world=await editor.CaptureAsync();var session=Session;
                var build=Resources.Load<TextAsset>("PhysicsPlaygroundBuildIdentity");
                var file=new PlaygroundFailureFile{Utc=DateTime.UtcNow.ToString("O"),World=world,Command=lastInput,
                    Reason=session?.Fault??status,WorldRevision=editor.Revision,WorldGeneration=session?.Generation??0,AttemptedTick=session?.AttemptedTick??0,
                    AcceptedTick=session?.AcceptedTick??0,Diagnostics=session?.Diagnostics,
                    UnityVersion=Application.unityVersion,ApplicationVersion=Application.version,
                    AssemblyId=typeof(CoupledPlaygroundSession).Assembly.ManifestModule.ModuleVersionId.ToString(),BuildIdentity=build?build.text:"Editor / source identity unrecorded"};
                PlaygroundSceneFile.WriteAtomic(FailurePath,JsonUtility.ToJson(file,true));status="Failure exported to "+FailurePath;
            }
            catch(Exception e){status="Failure export failed: "+e.Message;}
        }
        async Task ReplayFailure()
        {
            try
            {
                var file=PlaygroundFailureFile.Load(FailurePath);
                if(await Mutate("Restore failure input",w=>file.World,false))
                {
                    var replay=file.Command;replay.WorldGeneration=Session.Generation;replay.CommandId=++nextCommandId;
                    if(Session.Submit(replay)){lastInput=replay;status="Failure command replay submitted; inspect resulting verdict";}
                }
            }
            catch(Exception e){status="Failure replay refused: "+e.Message;}
        }
        void OnGUI()
        {
            GUI.Box(Panel,GUIContent.none);GUILayout.BeginArea(new Rect(22,22,290,Mathf.Max(220,Screen.height-44)));
            scroll=GUILayout.BeginScrollView(scroll);
            GUILayout.Label("PHYSICS WORKBENCH",GUI.skin.box);GUILayout.Label("V2 solver • experimental / runtime unverified");
            GUI.enabled=!Busy;
            GUILayout.BeginHorizontal();if(GUILayout.Button(running?"Pause [Space]":"Run [Space]"))ToggleRun();
            if(GUILayout.Button("Step [.]")){running=false;stepRequested=true;}GUILayout.EndHorizontal();
            if(GUILayout.Button("Home camera"))HomeCamera();
            GUILayout.Label("Simulation speed "+speed.ToString("F2")+"×");speed=GUILayout.HorizontalSlider(speed,.1f,1);
            string[] tools={"1 Select / pull","2 Grain brush","3 Rectangle","4 Erase","5 Push / throw","6 Anchor"};
            int chosen=GUILayout.SelectionGrid((int)tool,tools,2);if(chosen!=(int)tool)SetTool((Tool)chosen);
            GUILayout.Label("WASD force • Q/E torque\nMiddle / Alt-drag pan • wheel zoom\nRight click / Esc cancel • R reset");
            localAxes=GUILayout.Toggle(localAxes,"Body-local keyboard force");ignoreAnchors=GUILayout.Toggle(ignoreAnchors,"Skip anchors when selecting");
            keepWalls=GUILayout.Toggle(keepWalls,"Keep walls when erasing / clearing");
            GUILayout.Label("Brush radius "+radius.ToString("F1"));radius=GUILayout.HorizontalSlider(radius,.5f,5);
            GUILayout.Label("New density "+density.ToString("F1"));density=GUILayout.HorizontalSlider(density,.1f,50);
            GUILayout.Label("Tool force limit "+force.ToString("F0"));force=GUILayout.HorizontalSlider(force,1,20000);
            if(tool==Tool.Rectangle)
            {
                GUILayout.Label("Rectangle size "+sizeX.ToString("F1")+" × "+sizeY.ToString("F1"));
                sizeX=GUILayout.HorizontalSlider(sizeX,.5f,24);sizeY=GUILayout.HorizontalSlider(sizeY,.5f,24);
                GUILayout.Label("Angle "+spawnAngle.ToString("F0")+"°");spawnAngle=GUILayout.HorizontalSlider(spawnAngle,-180,180);
                bodyAnchored=GUILayout.Toggle(bodyAnchored,"Place fixed rectangle");
                Field("Initial velocity X",ref spawnVx);Field("Initial velocity Y",ref spawnVy);Field("Initial spin",ref spawnSpin);
            }
            GUILayout.Space(8);GUILayout.Label("PRESETS");
            preset=(PlaygroundPreset)GUILayout.SelectionGrid((int)preset,Enum.GetNames(typeof(PlaygroundPreset)),2);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Reset preset"))_=ResetPreset();
            if(GUILayout.Button("Clear"))_=Mutate("Clear world",w=>{w.Objects.RemoveAll(o=>!keepWalls||!o.Wall);return w;});GUILayout.EndHorizontal();
            bool gravity=Session?.GravityEnabled??false;
            bool nextGravity=GUILayout.Toggle(gravity,"Gravity enabled");
            if(nextGravity!=gravity)_=Mutate("Toggle gravity",w=>{w.GravityEnabled=nextGravity;return w;});
            Field("Gravity X",ref gravityX);Field("Gravity Y",ref gravityY);
            if(GUILayout.Button("Apply gravity vector"))_=Mutate("Change gravity",w=>{w.Gravity=new Vector2(Number(gravityX),Number(gravityY));return w;});
            var profile=Session?.Profile??Debris.Simulation.CoupledContacts.CoupledSolveProfile.C1;
            GUILayout.Label("Solver profile (unqualified)");
            int nextProfile=GUILayout.SelectionGrid((int)profile,new[]{"C1","C2","C3"},3);
            if(nextProfile!=(int)profile)_=Mutate("Change profile",w=>{w.Profile=(Debris.Simulation.CoupledContacts.CoupledSolveProfile)nextProfile;return w;},false);
            if(Session?.Faulted==true)
            {
                GUILayout.Label(Session.Fault,GUI.skin.box);
                if(GUILayout.Button("Rebuild retained world"))_=Mutate("Recover retained world",w=>w,false);
            }
            if(selected!=null)
            {
                GUILayout.Space(8);GUILayout.Label("SELECTED "+selected.Substring(0,8));
                if(GUILayout.Button("Refresh properties (compact readback)"))_=InspectSelected();
                if(inspected!=null)
                {
                    GUILayout.Label(inspected.Shape+" • mass "+inspected.Mass.ToString("F2")+" • inertia "+inspected.Inertia.ToString("F2"));
                    GUILayout.Label("Apply edits while paused");bool enable=GUI.enabled;GUI.enabled=enable&&!running;
                    Field("Position X",ref px);Field("Position Y",ref py);Field("Velocity X",ref vx);Field("Velocity Y",ref vy);
                    Field("Angle degrees",ref angle);Field("Spin rad/s",ref spin);Field("Density",ref editDensity);
                    if(inspected.Shape==PlaygroundShape.Rectangle){Field("Width",ref width);Field("Height",ref height);}
                    editAnchor=GUILayout.Toggle(editAnchor,"Anchored");if(GUILayout.Button("Apply properties"))_=ApplyInspector();GUI.enabled=enable;
                }
                if(GUILayout.Button("Delete selection"))_=DeleteSelected();
            }
            GUILayout.Space(8);GUI.SetNextControlName("editSceneName");sceneName=GUILayout.TextField(sceneName);
            GUILayout.BeginHorizontal();if(GUILayout.Button("Export scene"))_=SaveScene();if(GUILayout.Button("Import scene"))_=LoadScene();GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();if(GUILayout.Button("Export failure"))_=ExportFailure();if(GUILayout.Button("Replay failure"))_=ReplayFailure();GUILayout.EndHorizontal();
            diagnostics=GUILayout.Toggle(diagnostics,"Diagnostics");
            if(diagnostics&&Session!=null)
            {
                GUILayout.Label($"World revision {editor.Revision} • generation {Session.Generation}\nAccepted {Session.AcceptedTick} / attempted {Session.AttemptedTick}\nCommand {nextCommandId} • profile {Session.Profile}\nObjects {Session.Count} • substeps {Session.SelectedSubsteps}\nExplicit buffers {(Session.BufferBytes+(view?.BufferBytes??0))/1048576.0:F2} MiB\nRecording {Session.RecordingMilliseconds:F2} ms\nAccepted rate {ticksPerSecond:F1} ticks/s\nFrame {Time.unscaledDeltaTime*1000:F1} ms\nGPU time: unmeasured\nDriver/command memory: unmeasured");
                if(editor.History.Count>0){var h=editor.History[editor.History.Count-1];GUILayout.Label($"Last edit: {h.Command}\nMass change {h.MassChange:G4}\nMomentum change {h.MomentumChange}\nAngular change {h.AngularMomentumChange:G4}");}
            }
            GUI.enabled=true;GUILayout.EndScrollView();GUILayout.EndArea();
            GUI.Box(new Rect(338,12,Mathf.Max(100,Screen.width-350),56),(Busy?"Applying edit… ":pointerRequest?"Picking… ":running?"RUNNING • ":"PAUSED • ")+status);
            if(Session==null&&!Busy)GUI.Box(new Rect(350,90,Mathf.Max(120,Screen.width-380),100),"No live solver world. Reset a preset or import a valid scene.\n"+status);
        }
        static void Field(string label,ref string text)
        {GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(110));GUI.SetNextControlName("edit"+label);text=GUILayout.TextField(text);GUILayout.EndHorizontal();}
        void OnDestroy(){CancelManipulation();editor?.Dispose();view?.Dispose();if(cameraView)Destroy(cameraView.gameObject);}
    }
}
