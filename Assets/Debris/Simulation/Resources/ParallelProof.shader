Shader "Debris/ParallelProof"
{
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Grain {float2 center,velocity;float angle,spin;uint material,identity,flags,r0,r1,r2;};
            StructuredBuffer<Grain> _Grains;
            struct Body {float2 center,velocity;float angle,spin;uint r0,r1;};
            struct Boundary {float2 center,halfSize;uint body,feature,r0,r1;};
            StructuredBuffer<Body> _Bodies;
            StructuredBuffer<Boundary> _Boundaries;
            struct Parameters {float im,ii;float2 com;uint first,count,mobility,revision;};
            StructuredBuffer<Parameters> _Parameters;
            int _DrawBoundaries;
            struct Varyings {float4 position:SV_POSITION;float2 local:TEXCOORD0;float shade:TEXCOORD1;};
            Varyings Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID)
            {
                float2 corners[6]={float2(-.5,-.5),float2(.5,-.5),float2(.5,.5),float2(-.5,-.5),float2(.5,.5),float2(-.5,.5)};
                Grain g=(Grain)0;float2 p=corners[vertex];
                if(_DrawBoundaries!=0){Boundary b=_Boundaries[instance];Body body=_Bodies[b.body];float bs,bc;sincos(body.angle,bs,bc);float2 local=b.center-_Parameters[b.body].com;g.center=body.center+float2(bc*local.x-bs*local.y,bs*local.x+bc*local.y);g.angle=body.angle;g.identity=0;p*=2*b.halfSize;}else g=_Grains[instance];
                float s,c;sincos(g.angle,s,c);Varyings o;
                o.position=TransformWorldToHClip(float3(g.center+float2(c*p.x-s*p.y,s*p.x+c*p.y),0));o.local=p;o.shade=.65+.35*((g.identity*37)%13)/12.;return o;
            }
            float4 Frag(Varyings input):SV_Target {float edge=max(abs(input.local.x),abs(input.local.y));return float4(float3(.45,.7,.75)*input.shade*(edge>.47?.5:1),1);}
            ENDHLSL
        }
    }
}
