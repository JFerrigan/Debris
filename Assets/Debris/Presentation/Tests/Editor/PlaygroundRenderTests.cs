using System;
using System.IO;
using Debris.Simulation.Playground;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Presentation.Tests
{
    public sealed class PlaygroundRenderTests
    {
        [TestCase(true),TestCase(false),Timeout(30000)]
        public void BodyIsVisibleOverGrid(bool frozen)
        {
            var host=new GameObject("Playground render regression",typeof(Camera));
            var camera=host.GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=8;
            camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;
            var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var pixels=new Texture2D(128,128,TextureFormat.RGBA32,false);
            CoupledPlaygroundSession session=null;var previous=RenderTexture.active;
            try
            {
                var world=new PlaygroundWorldDefinition();world.Objects.Add(new PlaygroundObject{Shape=PlaygroundShape.Rectangle,Half=Vector2.one*2});
                using(var view=new CoupledPlaygroundView(camera))
                {
                    if(frozen)view.Freeze(world);else session=new CoupledPlaygroundSession(world,1);
                    view.DrawWorld(session,frozen,null,Vector2.zero,false);
                    var request=new RenderPipeline.StandardRequest{destination=target};
                    Assert.That(RenderPipeline.SupportsRenderRequest(camera,request),Is.True,"Use the actual project render pipeline");
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();
                    File.WriteAllBytes("/private/tmp/debris-render-"+(frozen?"frozen":"live")+".png",pixels.EncodeToPNG());
                    Color body=pixels.GetPixel(69,69);
                    Assert.That(body.g,Is.GreaterThan(body.r*1.5f),"Visible teal body over the grid; actual "+body);
                    Assert.That(body.b,Is.GreaterThan(body.r*1.5f),"Visible teal body over the grid; actual "+body);
                }
            }
            finally
            {
                session?.Dispose();RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
