using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Debris.Materials;
using Debris.Simulation;
using Debris.Sites;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
namespace Debris.Presentation
{
    public sealed class Showcase : MonoBehaviour
    {
        MatterSession session;MatterView view;MaterialCatalog catalog;
        InputActionAsset input;Camera cameraView;bool paused,benchmark;
        float accumulator,statsTime;ushort inspected;Vector2 pointerWorld;
        readonly FrameTiming[] timings=new FrameTiming[1];
        readonly List<double> cpu=new List<double>(),gpu=new List<double>(),frames=new List<double>();
        GUIStyle title,label,small;Texture2D solid;
        public MatterSession Session=>session;
        void Start()
        {
            Application.targetFrameRate=60;cameraView=Camera.main;
            catalog=Resources.Load<MaterialCatalog>("Materials");input=Instantiate(Resources.Load<InputActionAsset>("Debris"));input.Enable();
            benchmark=Array.Exists(Environment.GetCommandLineArgs(),a=>a=="-debrisBenchmark");
            ResetSession(2,8192);
            if(benchmark)StartCoroutine(Benchmark());
        }
        void ResetSession(int side,int capacity)
        {
            view?.Dispose();session?.Dispose();
            session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),side,128,capacity);
            view=new MatterView(session);accumulator=0;inspected=0;
        }
        void Update()
        {
            if(session==null)return;
            if(input["Pause"].WasPressedThisFrame())paused=!paused;
            if(input["Reset"].WasPressedThisFrame()&&!benchmark)ResetSession(2,8192);
            var pointer=input["Pointer"].ReadValue<Vector2>();pointerWorld=cameraView.ScreenToWorldPoint(new Vector3(pointer.x,pointer.y,10));
            var movement=input["Move"].ReadValue<Vector2>();cameraView.transform.position+=(Vector3)(movement*(cameraView.orthographicSize*Time.unscaledDeltaTime));
            cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-input["Zoom"].ReadValue<float>()*.025f,30,400);
            if(!paused&&!benchmark)
            {
                accumulator=Mathf.Min(accumulator+Time.deltaTime,4f/60);
                while(accumulator>=1f/60)
                {
                    SiteCommand? command=null;
                    if(input["Cut"].IsPressed()&&pointer.y<Screen.height-150&&pointer.x<Screen.width-280)
                        command=new SiteCommand(SiteCommandType.CutterStroke,pointerWorld,(pointerWorld.normalized+Vector2.up)*5,6,120,1);
                    session.Step(command);accumulator-=1f/60;
                }
            }
            statsTime+=Time.unscaledDeltaTime;
            if(statsTime>.2f){statsTime=0;session.PollStats();session.Inspect(Vector2Int.FloorToInt(pointerWorld),m=>inspected=m);}
            view.Draw();
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)>0){if(benchmark){cpu.Add(timings[0].cpuFrameTime);if(timings[0].gpuFrameTime>0)gpu.Add(timings[0].gpuFrameTime);}}
            if(benchmark)frames.Add(Time.unscaledDeltaTime*1000);
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
            GUI.Label(new Rect(50,76,700,26),"MATERIAL FIELD LAB   /   EE INC. INDUSTRIAL RESEARCH",small);
            GUI.Label(new Rect(50,101,850,22),"Hold left mouse to cut • WASD pan • scroll zoom • Esc pause • R reset",small);
            float x=Screen.width-262;
            Panel(new Rect(x-18,152,262,Screen.height-180),new Color(.025f,.045f,.065f,.94f));
            GUI.Label(new Rect(x,174,230,30),"SESSION  /  0001",label);
            var s=session.Stats;
            string info=$"FIXED MATTER     {s[1]-s[0]:N0}\nLOOSE CELLS       {s[0]:N0}\nPOOL CAPACITY  {session.Capacity:N0}\nDIRTY CHUNKS   {s[3]} / {session.Side*session.Side}\nTHROTTLED          {s[2]:N0}\nDISPATCHES         {session.Dispatches}\nREADBACKS          {session.ReadbackQueue}\nGPU BUFFERS      {session.BufferBytes/1048576f:F1} MiB\nFRAME                  {Time.unscaledDeltaTime*1000:F1} ms\nGPU                       {(timings[0].gpuFrameTime>0?timings[0].gpuFrameTime.ToString("F2")+" ms":"unavailable")}";
            GUI.Label(new Rect(x,220,230,240),info,small);
            Panel(new Rect(x,470,220,1),new Color(.18f,.3f,.32f));
            GUI.Label(new Rect(x,487,220,25),"MATERIAL INSPECTION",small);
            var material=catalog.DefinitionAt(inspected);
            GUI.Label(new Rect(x,520,220,65),material?material.MaterialKey.ToUpperInvariant()+"\n"+material.UnitValue+" credits / cell":"VACUUM",label);
            GUI.Label(new Rect(x,610,225,95),s[2]>0?"POOL SATURATED\nCutter throttled. Unreleased material remains in the asteroid.":"All released matter remains physical. Cells retain their mass and volume.",small);
            if(paused)GUI.Label(new Rect(Screen.width/2-80,145,180,30),"SIMULATION PAUSED",label);
            if(!paused&&Event.current.type==EventType.Repaint){var p=input["Pointer"].ReadValue<Vector2>();GUI.color=new Color(.25f,.95f,.75f);GUI.DrawTexture(new Rect(p.x-10,Screen.height-p.y,20,1),solid);GUI.DrawTexture(new Rect(p.x,Screen.height-p.y-10,1,20),solid);GUI.color=Color.white;}
            GUI.Label(new Rect(30,Screen.height-35,800,24),"DEVELOPMENT SHOWCASE   •   CHUNKED GPU MATTER   •   1 CELL = 1 PHYSICAL UNIT",small);
        }
        void OnDestroy(){view?.Dispose();session?.Dispose();if(input){input.Disable();Destroy(input);}if(solid)Destroy(solid);}
    }
}
