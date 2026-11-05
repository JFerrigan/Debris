using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Debris.Editor
{
    public static class PhysicsPlaygroundBuild
    {
        public const string Scene="Assets/Scenes/PhysicsPlayground.unity";
        public const string Output="Builds/PhysicsPlayground/Debris Physics Playground.app";
        [Serializable] sealed class SourceIdentity
        {
            public string Utc,UnityVersion,SourceHash;
            public string[] Files,Hashes;
        }
        [MenuItem("Debris/Physics Playground/Open isolated scene")]
        public static void OpenScene()
        {
            if(UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);
        }
        [MenuItem("Debris/Physics Playground/Build Mac (do not launch)")]
        public static void BuildMac()
        {
            if(!File.Exists(Scene))throw new FileNotFoundException("Missing isolated playground scene",Scene);
            var sources=new List<string>();
            foreach(var directory in new[]{"Assets/Debris/Simulation/Runtime/CoupledContacts","Assets/Debris/Simulation/Runtime/Playground"})
                sources.AddRange(Directory.GetFiles(directory,"*.cs",SearchOption.AllDirectories));
            sources.AddRange(Directory.GetFiles("Assets/Debris/Simulation/Resources","Coupled*"));
            sources.AddRange(new[]{"Assets/Debris/Simulation/Runtime/ParallelProof/ProofRecords.cs",
                "Assets/Debris/Presentation/Runtime/PhysicsPlaygroundBootstrap.cs",
                "Assets/Debris/Presentation/Runtime/CoupledPlaygroundView.cs",
                "Assets/Debris/Presentation/Resources/CoupledPlayground.shader",
                "Assets/Debris/Editor/PhysicsPlaygroundBuild.cs",Scene});
            sources=sources.Where(p=>!p.EndsWith(".meta",StringComparison.Ordinal)).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToList();
            string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
            var hashes=sources.Select(p=>Hash(File.ReadAllBytes(p))).ToArray();
            var identity=new SourceIdentity{Utc=DateTime.UtcNow.ToString("O"),UnityVersion=Application.unityVersion,
                Files=sources.ToArray(),Hashes=hashes,SourceHash=Hash(Encoding.UTF8.GetBytes(string.Join("\n",sources.Zip(hashes,(p,h)=>p+":"+h))))};
            string json=JsonUtility.ToJson(identity,true);
            string identityPath="Assets/Debris/Presentation/Resources/PhysicsPlaygroundBuildIdentity.json";
            File.WriteAllText(identityPath,json);AssetDatabase.ImportAsset(identityPath,ImportAssetOptions.ForceSynchronousImport);
            string oldName=PlayerSettings.productName;int oldWidth=PlayerSettings.defaultScreenWidth,oldHeight=PlayerSettings.defaultScreenHeight;
            var oldMode=PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.productName="Debris Physics Playground";PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
                PlayerSettings.fullScreenMode=FullScreenMode.Windowed;Directory.CreateDirectory("Builds/PhysicsPlayground");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=Output,
                    target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Playground build failed: "+report.summary.result);
                File.WriteAllText("Builds/PhysicsPlayground/build-manifest.json",json);
                Debug.Log("PHYSICS_PLAYGROUND_BUILD "+Output+" source="+identity.SourceHash+" (not launched)");
            }
            finally
            {PlayerSettings.productName=oldName;PlayerSettings.defaultScreenWidth=oldWidth;PlayerSettings.defaultScreenHeight=oldHeight;PlayerSettings.fullScreenMode=oldMode;}
        }
    }
}
