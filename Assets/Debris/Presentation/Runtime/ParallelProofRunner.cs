using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Debris.Simulation.ParallelProof;
using UnityEngine;
namespace Debris.Presentation
{
    // Opt-in correctness-first experiment. Failed necessary cases disqualify a profile before timing.
    public sealed class ParallelProofRunner : MonoBehaviour
    {
        ParallelGrainSolver solver;
        Material material,wallMaterial;
        readonly List<string> lines=new List<string>();
        string output;
        IEnumerator Start()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=60;Screen.SetResolution(1280,800,false);
            Camera.main.orthographicSize=35;Camera.main.transform.position=new Vector3(0,0,-10);
            output=Path.Combine(Application.temporaryCachePath,"B3R-parallel-proof.txt");
            var commandLine=Environment.GetCommandLineArgs();int index=Array.IndexOf(commandLine,"-debrisProofOutput");if(index>=0&&index+1<commandLine.Length)output=commandLine[index+1];
            material=new Material(Resources.Load<Shader>("ParallelProof"));wallMaterial=new Material(material);wallMaterial.SetInt("_DrawBoundaries",1);
            Record($"B3R_PARALLEL_PROOF utc={DateTime.UtcNow:O} device={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} unity={Application.unityVersion} resolution=1280x800 vsync=0 dt=1/60 slots=64 substeps=4..16 friction=.3");
            Record("Protocol: correctness-first necessary packed cases, 120 ticks; stop each case on rejection. No performance PASS from these untimed runs. Warm120/sample600 and all remaining workloads require a correctness-passing profile.");
            int passing=0;
            foreach(int velocity in new[]{4,8,12})
            {
                bool profilePass=true;
                foreach(bool shared in new[]{true,false})
                {
                    var fixture=ProofFixtures.Packed(shared);solver=fixture.Create(velocity,velocity/2);material.SetBuffer("_Grains",solver.Grains);material.SetBuffer("_Bodies",solver.Bodies);material.SetBuffer("_Boundaries",solver.Boundaries);
                    wallMaterial.SetBuffer("_Grains",solver.Grains);wallMaterial.SetBuffer("_Bodies",solver.Bodies);wallMaterial.SetBuffer("_Boundaries",solver.Boundaries);
                    int attempted=0;uint[] facts=null;double maximumSubmission=0;
                    for(int tick=0;tick<120;tick++)
                    {
                        long begin=System.Diagnostics.Stopwatch.GetTimestamp();solver.Step(fixture.Force,fixture.Torque);
                        maximumSubmission=Math.Max(maximumSubmission,(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000.0/System.Diagnostics.Stopwatch.Frequency);
                        attempted++;var task=solver.DiagnosticsAsync();while(!task.IsCompleted)yield return null;
                        if(task.IsFaulted)throw task.Exception;facts=task.Result;
                        if(facts[0]!=0)break;
                        yield return null;
                    }
                    var snapshotTask=solver.SnapshotAsync();while(!snapshotTask.IsCompleted)yield return null;var snapshot=snapshotTask.Result;
                    bool passed=facts[0]==0&&facts[1]==120;profilePass&=passed;
                    Record($"PROFILE {velocity}/{velocity/2} packed={(shared?"shared-rigid-motion":"rest-under-thrust-torque")} count=2500 attempted={attempted} completed={facts[1]} fault={(SolverFault)facts[0]} substeps={facts[2]} binMax={facts[4]} candidates={facts[5]} maxRow={facts[6]} maxGrainPenetration={snapshot.GrainPenetration:R} maxSolidPenetration={snapshot.SolidPenetration:R} envelope={facts[10]} buffers={solver.BufferBytes} cpuSubmissionMaxMs={maximumSubmission:F3} result={(passed?"NECESSARY_CASE_PASS":"FAIL")} physicsGpuP95=unmeasured totalGpuP95=unmeasured");
                    // Full state is read only after the case, outside any timing window.
                    Record($"COMMITTED bodyPosition={snapshot.Endpoints[2500].Center} bodyVelocity={snapshot.Endpoints[2500].Velocity} bodyAngle={snapshot.Endpoints[2500].Angle:R} bodySpin={snapshot.Endpoints[2500].AngularVelocity:R}");
                    solver.Dispose();solver=null;
                }
                if(profilePass)passing++;
            }
            Record(passing==0?"STOP: all three profiles fail necessary packed correctness. Integration stopped; performance gate cannot pass. Remaining R1 workloads and R2-R4 not executed.":"OPEN: at least one profile passed these necessary cases; full R1 correctness/performance workloads remain required.");
            File.WriteAllLines(output,lines);Debug.Log("B3R_PROOF_OUTPUT "+output);Application.Quit(passing==0?2:0);
        }
        void Record(string text){lines.Add(text);Debug.Log(text);}
        void Update()
        {
            if(solver!=null&&material!=null){Graphics.DrawProcedural(material,new Bounds(Vector3.zero,Vector3.one*1024),MeshTopology.Triangles,6,2500);Graphics.DrawProcedural(wallMaterial,new Bounds(Vector3.zero,Vector3.one*1024),MeshTopology.Triangles,6,4);}
        }
        void OnDestroy(){solver?.Dispose();if(material)Destroy(material);if(wallMaterial)Destroy(wallMaterial);}
    }
}
