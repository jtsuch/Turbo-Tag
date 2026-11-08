// See-through-walls pass for URP: draws the mesh again in a flat colour, only where something is well in front of
// it, with a brighter rim. The mesh is pulled _Margin towards the camera along each view ray (same spot on screen,
// nearer depth) before the ZTest Greater, so only cover at least that much in front counts: the player's own outfit,
// hat or flippers don't (a hunter in a onesie looked red all the time). Used as an extra material on other players'
// bodies by PlayerAppearance (teammates, hunters for hiders, X-Ray donut, hunter reveals); Resources/Cosmetics/XRay
// keeps the shader in builds.
Shader "TurboTag/XRay"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.25, 0.3, 0.75)
        _Margin ("Cover In Front (m)", Float) = 0.6
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
                float _Margin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Slide towards the camera along the view ray: the same pixel, _Margin nearer
                float3 positionVS = TransformWorldToView(positionWS);
                float distance = length(positionVS);
                positionVS *= max(distance - _Margin, 0.05) / max(distance, 0.0001);
                output.positionCS = TransformWViewToHClip(positionVS);
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
