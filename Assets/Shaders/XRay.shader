// See-through-walls pass for URP: draws the mesh again in a flat colour, only where something is in front of
// it (ZTest Greater), with a brighter rim. Used as an extra material on other players' bodies while the
// X-Ray Vision effect is on (PlayerAppearance.XRay); Resources/Cosmetics/XRay keeps the shader in builds.
Shader "TurboTag/XRay"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.25, 0.3, 0.75)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" }

        Pass
        {
            Name "XRay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS     = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float rim = 1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half4 c = _Color;
                c.rgb = lerp(c.rgb, 1.0.xxx, rim * rim * 0.6);
                c.a   = saturate(c.a * (0.55 + rim));
                return c;
            }
            ENDHLSL
        }
    }
}
