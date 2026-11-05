Shader "Debris/PhysicsPlayground"
{
    SubShader
    {
        Tags {"Queue"="Transparent"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            struct Endpoint { float2 center,velocity; float angle,spin; uint r0,r1; };
            struct Shape { float2 half; float4 color; uint anchored,grain; };
            StructuredBuffer<Endpoint> _Endpoints;
            StructuredBuffer<Shape> _Shapes;
            float4x4 _WorldToClip;
            float4 _Camera,_Preview,_Color,_Grab;
            float _Angle;
            int _Mode,_Selected;
            struct Out { float4 position:SV_POSITION;float2 local:TEXCOORD0;float2 world:TEXCOORD1;
                nointerpolation float4 color:COLOR0;nointerpolation uint anchored:TEXCOORD2;
                nointerpolation uint selected:TEXCOORD3;nointerpolation float2 half:TEXCOORD4; };
            float2 rotate(float2 p,float a){float s,c;sincos(a,s,c);return float2(c*p.x-s*p.y,s*p.x+c*p.y);}
            Out Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID)
            {
                const float2 corners[6]={float2(-1,-1),float2(1,1),float2(1,-1),float2(-1,-1),float2(-1,1),float2(1,1)};
                float2 uv=corners[vertex];Out o;o.anchored=0;o.selected=0;o.color=_Color;
                float2 center;float angle;
                if(_Mode==0){center=_Camera.xy;o.half=_Camera.zw;angle=0;}
                else if(_Mode==1)
                {
                    Endpoint e=_Endpoints[instance];Shape shape=_Shapes[instance];
                    center=e.center;angle=e.angle;o.half=shape.half;o.color=shape.color;
                    o.anchored=shape.anchored;o.selected=(int)instance==_Selected?1u:0u;
                }
                else {center=_Preview.xy;o.half=_Preview.zw;angle=_Angle;}
                o.local=uv*o.half;o.world=center+rotate(o.local,angle);
                o.position=mul(_WorldToClip,float4(o.world,0,1));return o;
            }
            float4 Frag(Out i):SV_Target
            {
                if(_Mode==0)
                {
                    float2 derivative=max(fwidth(i.world),.00001);
                    float2 unit=abs(frac(i.world+.5)-.5)/derivative;
                    float2 major=abs(frac(i.world/10+.5)-.5)*10/derivative;
                    float gridLine=1-saturate(min(unit.x,unit.y));float large=1-saturate(min(major.x,major.y));
                    return float4(float3(.035,.05,.07)+gridLine*.025+large*.04,1);
                }
                if(_Mode==3)
                {
                    float radius=length(i.local/i.half);if(radius>1)discard;
                    return float4(i.color.rgb,i.color.a*(radius>.94?1:.15));
                }
                float edge=min(i.half.x-abs(i.local.x),i.half.y-abs(i.local.y));
                float border=1-smoothstep(0,max(.025,2*max(fwidth(i.local.x),fwidth(i.local.y))),edge);
                float3 color=i.color.rgb;
                if(i.anchored!=0)color*=.75+.25*step(.5,frac((i.local.x+i.local.y)*.6));
                color=lerp(color,i.selected!=0?float3(1,1,1):color*.45,border);
                // Orientation notch and center of mass, drawn in shape-local space.
                if(abs(i.local.y)<.045&&i.local.x>0&&i.local.x<i.half.x*.65)color*=.3;
                if(i.selected!=0&&abs(i.local.y)<.035&&i.local.x>0&&i.local.x<i.half.x*.5)color=float3(.3,1,.4);
                if(i.selected!=0&&abs(i.local.x)<.035&&i.local.y>0&&i.local.y<i.half.y*.5)color=float3(.3,.65,1);
                if(length(i.local)<.08)color=float3(.06,.1,.13);
                if(i.selected!=0&&_Grab.z>0&&distance(i.local,_Grab.xy)<.15)color=float3(1,.35,.35);
                return float4(color,i.color.a);
            }
            ENDHLSL
        }
    }
}
