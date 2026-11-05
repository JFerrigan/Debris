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
                // Unity can report Success while Resources shaders contain errors.
                var shaderErrors=new List<string>();
                var renderShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Debris/Presentation/Resources/CoupledPlayground.shader");
                foreach(var message in ShaderUtil.GetShaderMessages(renderShader))
                    if(message.severity.ToString()=="Error")shaderErrors.Add(message.message);
                foreach(string guid in AssetDatabase.FindAssets("t:ComputeShader",new[]{"Assets/Debris/Simulation/Resources"}))
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);
                    if(!Path.GetFileName(path).StartsWith("Coupled",StringComparison.Ordinal))continue;
                    foreach(var message in ShaderUtil.GetComputeShaderMessages(AssetDatabase.LoadAssetAtPath<ComputeShader>(path)))
                        if(message.severity.ToString()=="Error")shaderErrors.Add(path+": "+message.message);
                }
                if(shaderErrors.Count>0)throw new InvalidOperationException("Playground shader compilation failed: "+string.Join("\n",shaderErrors.Distinct()));
                File.WriteAllText("Builds/PhysicsPlayground/build-manifest.json",json);
                Debug.Log("PHYSICS_PLAYGROUND_BUILD "+Output+" source="+identity.SourceHash+" (not launched)");
            }
            finally
            {PlayerSettings.productName=oldName;PlayerSettings.defaultScreenWidth=oldWidth;PlayerSettings.defaultScreenHeight=oldHeight;PlayerSettings.fullScreenMode=oldMode;}
        }
        public static void ValidateShaders()
        {
            bool previous=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try { ValidateShadersSynchronous(); }
            finally { ShaderUtil.allowAsyncCompilation=previous; }
        }
        static void ValidateShadersSynchronous()
        {
            const string renderPath="Assets/Debris/Presentation/Resources/CoupledPlayground.shader";
            AssetDatabase.ImportAsset(renderPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var render=AssetDatabase.LoadAssetAtPath<Shader>(renderPath);
            var material=new Material(render);
            try { ShaderUtil.CompilePass(material,0,true); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            if(!render.isSupported||ShaderUtil.ShaderHasError(render))throw new InvalidOperationException("Playground render shader is unsupported or has compilation errors");
            int kernels=0;
            foreach(string path in Directory.GetFiles("Assets/Debris/Simulation/Resources","Coupled*.compute"))
            {
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                foreach(var line in File.ReadLines(path))
                {
                    var words=line.Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
                    if(words.Length<3||words[0]!="#pragma"||words[1]!="kernel")continue;
                    int kernel=compute.FindKernel(words[2]);
                    compute.GetKernelThreadGroupSizes(kernel,out uint x,out uint y,out uint z);
                    if(!compute.IsSupported(kernel))throw new InvalidOperationException(path+": unsupported kernel "+words[2]+
                        $" groups={x},{y},{z} bufferLimit={SystemInfo.maxComputeBufferInputsCompute}; "+
                        string.Join(" | ",ShaderUtil.GetComputeShaderMessages(compute).Select(m=>m.severity+": "+m.message)));
                    if(x==0||y==0||z==0)throw new InvalidOperationException(path+": invalid kernel "+words[2]);
                    kernels++;
                }
                foreach(var message in ShaderUtil.GetComputeShaderMessages(compute))
                    if(message.severity.ToString()=="Error")throw new InvalidOperationException(path+": "+message.message);
            }
            Debug.Log("PHYSICS_PLAYGROUND_SHADERS validated kernels="+kernels+" (no simulation dispatched)");
        }
    }
}
