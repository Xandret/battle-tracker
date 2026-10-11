// ═══════════ TiltShift.shader — «миниатюры на столе» (В15): размытие верха и низа кадра, середина резкая ═══════════
// Проход на весь экран (FullScreenPassRendererFeature 2D-рендерера, после постобработки): как будто снимали макет
// с близкого расстояния и малой глубиной резкости. Резкая полоса — середина кадра, к краям размытие растёт до _JTiltPx
// пикселей (при 1080 px высоты). Сила — глобальная _JTilt (0 — выкл), её ставит смотрелка.
Shader "Journal/TiltShift"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off
        Pass
        {
            Name "TiltShift"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _JTilt;      // 0…1
            float _JTiltPx;    // наибольший радиус размытия, px при 1080 px высоты

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // резко в середине, к верху и низу — сильнее; чуть-чуть и к бокам
                float dy = abs(uv.y - 0.5), dx = abs(uv.x - 0.5);
                float k = smoothstep(0.16, 0.5, dy) + 0.35 * smoothstep(0.3, 0.5, dx);
                float r = saturate(k) * _JTilt * _JTiltPx * (_ScreenParams.y / 1080.0);
                if (r < 0.35) return c0;
                float2 px = 1.0 / _ScreenParams.xy;
                // диск из 12 точек на двух кольцах (золотой угол) + середина
                float4 acc = c0; float wsum = 1;
                [unroll] for (int i = 0; i < 12; i++)
                {
                    float a = i * 2.39996, rr = r * sqrt((i + 0.5) / 12.0);
                    float2 o = float2(cos(a), sin(a)) * rr * px;
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + o); wsum += 1;
                }
                return acc / wsum;
            }
            ENDHLSL
        }
    }
}
