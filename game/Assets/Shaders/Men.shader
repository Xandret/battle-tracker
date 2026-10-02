// ═══════════ Men.shader — бойцы, павшие, природа из атласов полигона; цвет стороны — перекраской ═══════════
// В атласе цвет стороны — маркерный пурпурный (1, 0, 1) со светлыми и тёмными оттенками. Пиксель раскладывается на
// «пурпурную долю» m = min(r, b) − g и остаток; пурпурную долю заменяем цветом отряда из цвета вершины:
// c + (F − (1, 0, 1)) · m. Так светлые и тёмные оттенки, края и полутени переходят в оттенки цвета стороны.
// TEXCOORD1: x — сдвиг тона цвета стороны (−1…1: к чёрному или белому), y — выцветание (павшие), z — 1: спрайт белый,
// красим цветом вершины целиком (кровь, тени, стрелы).
Shader "Journal/Men"
{
    Properties { _MainTex ("Атлас", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 p : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 p : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color; o.uv = v.uv; o.p = v.p;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);   // значения как в PNG (sRGB, без перевода)
                float3 o;
                if (i.p.z > 0.5) o = i.color.rgb;
                else
                {
                    float3 rgb = c.rgb;
                    float m = saturate(min(rgb.r, rgb.b) - rgb.g);
                    float3 F = i.color.rgb;
                    F = i.p.x >= 0 ? lerp(F, 1, i.p.x) : lerp(F, 0, -i.p.x);
                    o = rgb + (F - float3(1, 0, 1)) * m;
                    o = lerp(o, dot(o, float3(0.3, 0.59, 0.11)) * float3(0.95, 0.93, 0.9), i.p.y);
                }
                #if !defined(UNITY_COLORSPACE_GAMMA)
                o = SRGBToLinear(saturate(o));
                #endif
                return half4(o, c.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
