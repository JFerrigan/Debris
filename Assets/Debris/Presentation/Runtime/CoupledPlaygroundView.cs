using System;
using Debris.Simulation.Playground;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Presentation
{
    public sealed class CoupledPlaygroundView : IDisposable
    {
        readonly Camera camera;
        readonly Material grid,bodies,preview;
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        readonly GraphicsBuffer fallbackEndpoints,fallbackShapes;
        GraphicsBuffer frozenEndpoints,frozenShapes;
        int frozenCount;
        static readonly Bounds Bounds=new Bounds(Vector3.zero,Vector3.one*4096);
        public long FrozenBufferBytes=>frozenEndpoints==null?0:(long)frozenCount*64;
        public long BufferBytes=>64+FrozenBufferBytes;
        public CoupledPlaygroundView(Camera camera)
        {
            this.camera=camera;
            var shader=Resources.Load<Shader>("CoupledPlayground");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Playground render shader is missing or unsupported");
            try
            {
                grid=new Material(shader);grid.SetInt("_Mode",0);
                bodies=new Material(shader);bodies.SetInt("_Mode",1);
                preview=new Material(shader);preview.SetInt("_Mode",2);
                fallbackEndpoints=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32);
                fallbackShapes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32);
                fallbackEndpoints.SetData(new BodyState[1]);fallbackShapes.SetData(new PlaygroundShapeRecord[1]);
                foreach(var material in new[]{grid,bodies,preview})Bind(material,fallbackEndpoints,fallbackShapes);
            }
            catch { Dispose();throw; }
        }
        static void Bind(Material material,GraphicsBuffer endpoints,GraphicsBuffer shapes)
        {material.SetBuffer("_Endpoints",endpoints);material.SetBuffer("_Shapes",shapes);}
        public void Freeze(PlaygroundWorldDefinition world)
        {
            ClearFrozen();frozenCount=world.Objects.Count;if(frozenCount==0)return;
            var endpoints=new BodyState[frozenCount];var shapes=new PlaygroundShapeRecord[frozenCount];
            for(int i=0;i<frozenCount;i++)
            {
                var o=world.Objects[i];endpoints[i]=new BodyState{Center=o.Center,Angle=o.Angle};
                shapes[i]=new PlaygroundShapeRecord{Half=o.Half,Anchored=o.Anchored?1u:0u,
                    Color=o.Anchored?new Vector4(.32f,.39f,.48f,1):new Vector4(.22f,.78f,.72f,1)};
            }
            try
            {
                frozenEndpoints=new GraphicsBuffer(GraphicsBuffer.Target.Structured,frozenCount,32);frozenEndpoints.SetData(endpoints);
                frozenShapes=new GraphicsBuffer(GraphicsBuffer.Target.Structured,frozenCount,32);frozenShapes.SetData(shapes);
            }
            catch { ClearFrozen();throw; }
        }
        public void ClearFrozen()
        {frozenEndpoints?.Dispose();frozenShapes?.Dispose();frozenEndpoints=frozenShapes=null;frozenCount=0;}
        Matrix4x4 Matrix=>GL.GetGPUProjectionMatrix(camera.projectionMatrix,false)*camera.worldToCameraMatrix;
        void Draw(Material material,int count)
        {if(count>0)Graphics.DrawProcedural(material,Bounds,MeshTopology.Triangles,6,count,camera,properties,ShadowCastingMode.Off,false);}
        public void DrawWorld(CoupledPlaygroundSession session,bool frozen,string selected,Vector2 localAnchor,bool grabbing)
        {
            properties.Clear();properties.SetMatrix("_WorldToClip",Matrix);
            properties.SetVector("_Camera",new Vector4(camera.transform.position.x,camera.transform.position.y,camera.orthographicSize*camera.aspect,camera.orthographicSize));Draw(grid,1);
            if(frozen||session==null)
            {
                if(frozenEndpoints==null)return;Bind(bodies,frozenEndpoints,frozenShapes);properties.SetInt("_Selected",-1);Draw(bodies,frozenCount);
            }
            else if(session.Count>0)
            {
                Bind(bodies,session.Endpoints,session.Shapes);properties.SetInt("_Selected",session.IndexOf(selected));
                properties.SetVector("_Grab",new Vector4(localAnchor.x,localAnchor.y,grabbing?1:0,0));Draw(bodies,session.Count);
            }
        }
        public void Preview(PlaygroundObject o,bool valid)
        {
            if(o==null)return;properties.Clear();properties.SetMatrix("_WorldToClip",Matrix);
            properties.SetInt("_Mode",2);
            properties.SetVector("_Preview",new Vector4(o.Center.x,o.Center.y,o.Half.x,o.Half.y));properties.SetFloat("_Angle",o.Angle);
            properties.SetVector("_Color",valid?new Vector4(.4f,1,.65f,.4f):new Vector4(1,.25f,.25f,.5f));Draw(preview,1);
        }
        public void Arrow(Vector2 from,Vector2 to)
        {
            var delta=to-from;if(delta.sqrMagnitude<.0001f)return;
            void Line(Vector2 a,Vector2 b)
            {
                var d=b-a;Preview(new PlaygroundObject{Center=(a+b)*.5f,Half=new Vector2(d.magnitude*.5f,.06f),Angle=Mathf.Atan2(d.y,d.x)},true);
            }
            Line(from,to);Vector2 direction=delta.normalized,side=new Vector2(-direction.y,direction.x);
            float head=Mathf.Min(1,delta.magnitude*.3f);Line(to,to-head*direction+head*.5f*side);Line(to,to-head*direction-head*.5f*side);
        }
        public void Brush(Vector2 center,float radius,bool erase)
        {
            properties.Clear();properties.SetMatrix("_WorldToClip",Matrix);properties.SetInt("_Mode",3);
            properties.SetVector("_Preview",new Vector4(center.x,center.y,radius,radius));properties.SetFloat("_Angle",0);
            properties.SetVector("_Color",erase?new Vector4(1,.35f,.35f,.7f):new Vector4(.4f,1,.65f,.7f));Draw(preview,1);
        }
        public void Dispose()
        {
            ClearFrozen();fallbackEndpoints?.Dispose();fallbackShapes?.Dispose();
            foreach(var m in new[]{grid,bodies,preview})if(m)UnityEngine.Object.DestroyImmediate(m);
        }
    }
}
