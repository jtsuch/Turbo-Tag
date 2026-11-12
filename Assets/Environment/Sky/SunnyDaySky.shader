// A sunny-day skybox for the floating-island maps: a blue gradient, a sun with a soft glow, slow drifting clouds
// overhead and a sea of clouds below the horizon (the islands float above it). The sun's direction comes from the
// material (_SunDir, set by Editor/Atmosphere.cs from the scene's directional light).
Shader "TurboTag/Sunny Day Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.22, 0.47, 0.86, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.85, 0.97, 1)
        _BelowColor ("Below the horizon (haze)", Color) = (0.80, 0.87, 0.95, 1)
        _SunColor ("Sun", Color) = (1, 0.95, 0.84, 1)
        _SunDir ("Sun direction (towards the sun)", Vector) = (0.4, 0.75, -0.5, 0)
        _SunSize ("Sun size", Range(0.0005, 0.02)) = 0.0035
        _SunGlow ("Sun glow", Range(0, 2)) = 0.6
        _CloudColor ("Cloud lit", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud shade", Color) = (0.70, 0.76, 0.86, 1)
        _CloudCover ("Cloud cover overhead", Range(0, 1)) = 0.42
        _SeaCover ("Cloud cover below", Range(0, 1)) = 0.72
        _CloudScale ("Cloud scale", Float) = 1.4
        _CloudSpeed ("Cloud drift (xz per second)", Vector) = (0.006, 0.002, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ZenithColor, _HorizonColor, _BelowColor, _SunColor, _SunDir, _CloudColor, _CloudShade, _CloudSpeed;
            float _SunSize, _SunGlow, _CloudCover, _SeaCover, _CloudScale;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * noise(p); p = p * 2.03 + float2(17.1, 9.2); a *= 0.5; }
                return v;
            }

            // Cloud density on a layer seen along dir (y > 0 above, < 0 below); cover 0..1
            float clouds(float3 dir, float cover, float2 drift, float scale)
            {
                float2 uv = dir.xz / (abs(dir.y) + 0.12) * scale + drift;
                float d = fbm(uv);
                return smoothstep(1 - cover, 1 - cover + 0.28, d);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float sunDot = saturate(dot(dir, sun));
                float2 drift = _Time.y * _CloudSpeed.xy;

                float3 col;
                if (dir.y >= 0)
                {
                    // Sky: horizon → zenith, a glow round the sun
                    col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(dir.y), 0.55));
                    col += _SunColor.rgb * pow(sunDot, 12) * 0.35 * _SunGlow;
                    // Clouds, fading out toward the horizon; lit on the sun's side
                    float c = clouds(dir, _CloudCover, drift, _CloudScale) * smoothstep(0.02, 0.3, dir.y);
                    float lit = 0.55 + 0.45 * fbm(dir.xz / (dir.y + 0.12) * _CloudScale + drift + sun.xz * 0.35);
                    float3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lit) + _SunColor.rgb * pow(sunDot, 6) * 0.25;
                    col = lerp(col, cloud, c * 0.92);
                    // The sun's disc over everything
                    float disc = smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, dot(dir, sun));
                    col = lerp(col, _SunColor.rgb * 1.6, disc * (1 - c * 0.7));
                }
                else
                {
                    // Below: a sea of clouds far beneath, melting into haze at the horizon
                    float c = clouds(dir, _SeaCover, drift * 0.6, _CloudScale * 0.8);
                    float3 sea = lerp(_CloudShade.rgb, _CloudColor.rgb, c);
                    col = lerp(_BelowColor.rgb, sea, smoothstep(0.0, 0.35, -dir.y));
                    col = lerp(col, _HorizonColor.rgb, pow(1 - saturate(-dir.y), 12));
                }
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
