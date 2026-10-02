// ═══════════ Men.shader — бойцы, павшие, природа из атласов полигона; цвет стороны — перекраской; свет — по карте объёма ═══════════
// В атласе цвет стороны — маркерный пурпурный (1, 0, 1) со светлыми и тёмными оттенками. Пиксель раскладывается на
// «пурпурную долю» m = min(r, b) − g и остаток; пурпурную долю заменяем цветом отряда из цвета вершины:
// c + (F − (1, 0, 1)) · m. Так светлые и тёмные оттенки, края и полутени переходят в оттенки цвета стороны.
// TEXCOORD1: x — сдвиг тона цвета стороны (−1…1: к чёрному или белому), y — выцветание (павшие), z — 1: спрайт белый,
// красим цветом вершины целиком (кровь, тени, стрелы).
// Свет (В15): _NormalTex — карта объёма атласа (ArtNormals: нормаль в осях текстуры, A — металл). TEXCOORD2 — куда в мире
// смотрят оси текстуры u и v у этого четырёхугольника (части повёрнуты с бойцом). Свет — справа сверху, чуть от зрителя:
// ровная поверхность остаётся своего цвета, склоны к свету светлеют, от света — темнеют; металл блестит.
// Сила — глобальная _JLight (0 — плоско, как в полигоне; 1 — полный свет): смотрелка переключает для сравнения.
Shader "Journal/Men"
{
    Properties
    {
        _MainTex ("Атлас", 2D) = "white" {}
        _NormalTex ("Карта объёма", 2D) = "bump" {}
        _JHasNormals ("Есть карта объёма", Float) = 0
    }
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
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            float _JLight;          // сила света 0…1 (глобальная)
            float _JHasNormals;     // у материала есть карта объёма
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 p : TEXCOORD1; float4 ax : TEXCOORD2; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 p : TEXCOORD1; float4 ax : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color; o.uv = v.uv; o.p = v.p; o.ax = v.ax;
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
                    // ── свет по карте объёма ──
                    float k = _JLight * _JHasNormals;
                    if (k > 0.001)
                    {
                        float4 nm = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv);
                        float3 nt = nm.xyz * 2 - 1;
                        // мир: x вправо, y вверх по экрану, z — к зрителю (камера смотрит вдоль +z, поэтому «к зрителю» — −z)
                        float2 tu = i.ax.xy, tv = i.ax.zw;
                        float3 n = normalize(float3(nt.x * tu + nt.y * tv, -nt.z));
                        const float3 L = normalize(float3(0.42, 0.55, -0.72));      // справа сверху, от зрителя
                        const float3 V = float3(0, 0, -1);
                        float flat = -L.z;                                            // ровная поверхность — свой цвет
                        float diff = saturate(dot(n, L)) / flat;
                        float shade = lerp(0.42, 1, saturate(diff)) + 0.22 * saturate(diff - 1) * 4;   // тень до 0,42; к свету — светлее
                        float3 lit = o * shade;
                        // металл: блик по Блинну — Фонгу; дерево и ткань — матовые
                        float3 Hh = normalize(L + V);
                        float spec = pow(saturate(dot(n, Hh)), 38) * nm.a;
                        lit += spec * 0.55 * (1 - i.p.y);
                        o = lerp(o, lit, k);
                    }
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
