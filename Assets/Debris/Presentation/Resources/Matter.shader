Shader "Debris/Matter"
{
    Properties { _Loose("Loose mode",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Cell { float2 position; float2 velocity; uint material; uint identity; uint step; uint flags; };
            Texture2DArray<uint> _Field;
            StructuredBuffer<Cell> _Cells;
            StructuredBuffer<uint> _Counters, _Hull;
            StructuredBuffer<float4> _ShipPose;
            float2 ShipWorld(float2 p){float4 pose=_ShipPose[0];float c=cos(pose.z),s=sin(pose.z);return pose.xy+float2(p.x*c-p.y*s,p.x*s+p.y*c);}
            StructuredBuffer<float4> _Palette, _Shadows, _Emissions;
            float _Loose;
            int _ChunkSize,_Side;
            float2 _Origin;
            struct Out { float4 position:SV_POSITION;float2 uv:TEXCOORD0;nointerpolation uint slice:TEXCOORD1;nointerpolation uint material:TEXCOORD2;float2 world:TEXCOORD3; };
            Out Vert(float3 vertex:POSITION,uint instance:SV_InstanceID)
            {
                Out o;o.uv=vertex.xy;o.slice=instance;o.material=0;
                float2 p;
                if(_Loose>1.5)
                {
                    uint m=_Hull[instance];if(m==0||(m==0xffffffff&&_ShipPose[2].z>0)){o.position=float4(2,2,2,1);o.world=0;return o;}
                    o.material=m==0xffffffff?2:m;p=ShipWorld(float2(instance%128,instance/128)-64+vertex.xy);
                }
                else if(_Loose>.5)
                {
                    if(instance>=_Counters[0]){o.position=float4(2,2,2,1);o.world=0;return o;}
                    Cell c=_Cells[instance];p=(c.flags&4)!=0?ShipWorld(c.position+vertex.xy):c.position+vertex.xy;o.material=c.material;
                }
                else p=_Origin+float2(instance%_Side,instance/_Side)*_ChunkSize+vertex.xy*_ChunkSize;
                o.world=p;o.position=TransformWorldToHClip(float3(p,_Loose>.5?-.1:0));return o;
            }
            half4 Frag(Out i):SV_Target
            {
                uint m=i.material;
                if(_Loose<.5) m=_Field.Load(int4(min((int2)(i.uv*_ChunkSize),_ChunkSize-1),i.slice,0));
                if(m==0)discard;
                float2 cell=floor(i.world);float shade=frac(sin(dot(cell,float2(12.9898,78.233)))*43758.5453);
                half3 color=lerp(_Shadows[m].rgb,_Palette[m].rgb,.35+shade*.65)+_Emissions[m].rgb*(.5+.15*sin(_Time.y*1.4+cell.x));
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
