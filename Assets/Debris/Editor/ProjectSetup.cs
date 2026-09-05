using System;
using System.IO;
using Debris.Materials;
using Debris.Sites;
using Debris.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Debris.Editor
{
    public static class ProjectSetup
    {
        [MenuItem("Debris/Setup project and content")]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/Settings"); Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Content/Resources");
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.companyName = "JFerrigan"; PlayerSettings.productName = "Debris";
            PlayerSettings.defaultScreenWidth = 1440; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true; PlayerSettings.enableFrameTimingStats = true;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/DebrisRenderer.asset");
            if (!renderer) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer,"Assets/Settings/DebrisRenderer.asset"); }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/DebrisURP.asset");
            if (!pipeline) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline,"Assets/Settings/DebrisURP.asset"); }
            pipeline.msaaSampleCount = 1; pipeline.supportsHDR = true;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            string[] keys = {"rock","iron","copper","ice","carbon","arcanium"};
            Color[] colors = { new Color(.3f,.34f,.39f),new Color(.5f,.59f,.62f),new Color(.8f,.4f,.22f),new Color(.34f,.77f,.87f),new Color(.2f,.22f,.25f),new Color(.2f,.96f,.75f) };
            int[] values = {1,5,9,4,3,40};
            var definitions = new MaterialDefinition[keys.Length];
            for(int i=0;i<keys.Length;i++)
            {
                string path="Assets/Content/Resources/"+keys[i]+".asset";
                definitions[i]=AssetDatabase.LoadAssetAtPath<MaterialDefinition>(path);
                if (!definitions[i]) { definitions[i]=ScriptableObject.CreateInstance<MaterialDefinition>(); definitions[i].Configure(keys[i],colors[i],1+i*.4f,1+i*.3f,values[i],i==5?colors[i]*.5f:Color.black); AssetDatabase.CreateAsset(definitions[i],path); }
            }
            var catalog=AssetDatabase.LoadAssetAtPath<MaterialCatalog>("Assets/Content/Resources/Materials.asset");
            if (!catalog) { catalog=ScriptableObject.CreateInstance<MaterialCatalog>(); catalog.Configure(definitions); AssetDatabase.CreateAsset(catalog,"Assets/Content/Resources/Materials.asset"); }
            var profile=AssetDatabase.LoadAssetAtPath<AsteroidProfile>("Assets/Content/Resources/Asteroid.asset");
            if (!profile) { profile=ScriptableObject.CreateInstance<AsteroidProfile>(); profile.Configure(70,90,new[]{new MaterialBand{MaterialKey="rock",Weight=.5f},new MaterialBand{MaterialKey="iron",Weight=.22f},new MaterialBand{MaterialKey="copper",Weight=.14f},new MaterialBand{MaterialKey="ice",Weight=.08f},new MaterialBand{MaterialKey="carbon",Weight=.05f},new MaterialBand{MaterialKey="arcanium",Weight=.01f}}); AssetDatabase.CreateAsset(profile,"Assets/Content/Resources/Asteroid.asset"); }
            if (!File.Exists("Assets/Content/Resources/Debris.inputactions"))
            {
                var input=ScriptableObject.CreateInstance<InputActionAsset>(); var map=input.AddActionMap("Salvage");
                map.AddAction("Cut",InputActionType.Button,"<Mouse>/leftButton");
                map.AddAction("Pointer",InputActionType.Value,"<Pointer>/position",expectedControlLayout:"Vector2");
                map.AddAction("Zoom",InputActionType.Value,"<Mouse>/scroll/y");
                map.AddAction("Pause",InputActionType.Button,"<Keyboard>/escape");
                map.AddAction("Reset",InputActionType.Button,"<Keyboard>/r");
                map.AddAction("Move",InputActionType.Value,expectedControlLayout:"Vector2").AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");
                File.WriteAllText("Assets/Content/Resources/Debris.inputactions", input.ToJson()); UnityEngine.Object.DestroyImmediate(input);
            }
            string[] scenes={"Bootstrap","DevShowcase"};
            foreach(var name in scenes)
            {
                string path="Assets/Scenes/"+name+".unity";
                if (File.Exists(path)) continue;
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Camera").AddComponent<Camera>(); camera.tag="MainCamera";
                camera.orthographic=true;camera.orthographicSize=145; camera.transform.position=new Vector3(0,0,-10);
                camera.backgroundColor=new Color(.018f,.027f,.045f);camera.clearFlags=CameraClearFlags.SolidColor;
                camera.GetUniversalAdditionalCameraData();
                new GameObject("Debris session").AddComponent<Showcase>();
                EditorSceneManager.SaveScene(scene,path);
            }
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Bootstrap.unity",true),new EditorBuildSettingsScene("Assets/Scenes/DevShowcase.unity",true)};
            Validate(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("DEBRIS_SETUP_PASS Unity="+Application.unityVersion);
        }
        [MenuItem("Debris/Validate content")]
        public static void Validate()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<MaterialCatalog>("Assets/Content/Resources/Materials.asset");
            if (!catalog) throw new InvalidOperationException("Run Debris setup to create content.");
            catalog.Validate();
            AssetDatabase.LoadAssetAtPath<AsteroidProfile>("Assets/Content/Resources/Asteroid.asset").Validate(catalog);
        }
        public static void BuildMac()
        {
            Validate(); Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{"Assets/Scenes/Bootstrap.unity"},locationPathName="Builds/Debris.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Debris Mac build failed: "+report.summary.result);
        }
    }
}
