// ═══════════ MapImage.shader — земля из картинки карты (сохранение трекера): непрозрачная, как Ground ═══════════
// Картинка рисуется в очереди Geometry с записью глубины — раньше всех прозрачных слоёв (кровь, павшие, бойцы).
// Спрайтовый шейдер URP для этого не годится: он прозрачный и в 2D-рендерере уходит в свой проход поверх бойцов.
Shader "Journal/MapImage"
{
    Properties
    {
        _MainTex ("Картинка карты", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // текстура sRGB: выборка уже в линейном пространстве — как есть
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
