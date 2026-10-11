// ═══════════ Ground.shader — земля смотрелки: попиксельный проход полигона (В1), перенесённый на видеокарту ═══════════
// Клетки 5 м лежат в текстуре _Cells (R — вид земли, G — высота, B — высота, сглаженная 3 × 3), цвета видов — в _Pal
// (A — неровность края). Вид в точке — доля каждого вида среди четырёх ближних клеток плюс шум: край идёт по границе
// клеток, но живой. Сверху: пятна, сухая трава на лугу, лужи на болоте, тёмная кромка воды и светлая отмель, обводка
// построек, светотень холмов (свет справа сверху), горизонтали, тетрадная клетка 10 м, зерно бумаги.
Shader "Journal/Ground"
{
    Properties
    {
        _Cells ("Клетки", 2D) = "black" {}
        _Pal ("Цвета видов", 2D) = "white" {}
        _Info ("колонок, рядов, клетка м, клетка 10 м вкл", Vector) = (1, 1, 5, 1)
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
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_Cells);
            TEXTURE2D(_Pal);
            float4 _Info;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 map : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.map = float2(w.x, -w.y);   // метры карты: y вниз, как у движка
                return o;
            }

            // ── шум: значения в узлах решётки, плавно между ними ──
            float H01(int x, int y, int s)
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return h * (1.0 / 4294967296.0);
            }
            float VN(float2 p, int s)
            {
                int2 i = (int2)floor(p); float2 f = p - i, u = f * f * (3 - 2 * f);
                float a = H01(i.x, i.y, s), b = H01(i.x + 1, i.y, s), c = H01(i.x, i.y + 1, s), d = H01(i.x + 1, i.y + 1, s);
                return a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
            }

            float4 Cell(int x, int y)
            {
                x = clamp(x, 0, (int)_Info.x - 1); y = clamp(y, 0, (int)_Info.y - 1);
                return LOAD_TEXTURE2D(_Cells, int2(x, y));
            }
            int Kind(float4 c) { return (int)round(c.r * 255); }
            float3 PalCol(int k) { return LOAD_TEXTURE2D(_Pal, int2(k, 0)).rgb; }
            float Amp(int k) { return LOAD_TEXTURE2D(_Pal, int2(k, 0)).a * 0.5; }
            bool Wet(int k) { return k == 7 || k == 8 || k == 16; }
            bool Built(int k) { return k == 9 || (k >= 12 && k <= 15) || k == 18 || k == 20; }

            // вид земли в точке и запас до ближайшего другого вида; высота как есть и сглаженная
            int Classify(float2 m, out float margin, out float zs, out float zh)
            {
                float2 f = m / _Info.z - 0.5; int2 i = (int2)floor(f); float2 t = f - i;
                float4 c00 = Cell(i.x, i.y), c10 = Cell(i.x + 1, i.y), c01 = Cell(i.x, i.y + 1), c11 = Cell(i.x + 1, i.y + 1);
                float w00 = (1 - t.x) * (1 - t.y), w10 = t.x * (1 - t.y), w01 = (1 - t.x) * t.y, w11 = t.x * t.y;
                zs = (c00.g * w00 + c10.g * w10 + c01.g * w01 + c11.g * w11) * 255 / 16;
                zh = (c00.b * w00 + c10.b * w10 + c01.b * w01 + c11.b * w11) * 255 / 16;
                int k4[4] = { Kind(c00), Kind(c10), Kind(c01), Kind(c11) };
                float w4[4] = { w00, w10, w01, w11 };
                margin = 1;
                if (k4[0] == k4[1] && k4[0] == k4[2] && k4[0] == k4[3]) return k4[0];
                int best = k4[0]; float bs = -9, ss = -9;
                [unroll] for (int q = 0; q < 4; q++)
                {
                    int k = k4[q]; bool dup = false;
                    [unroll] for (int p = 0; p < q; p++) dup = dup || k4[p] == k;
                    if (dup) continue;
                    float s = 0;
                    [unroll] for (int r = 0; r < 4; r++) s += k4[r] == k ? w4[r] : 0;
                    float a = Amp(k);
                    if (a > 0) s += a * (1.2 * VN(m / 5.5 + float2(k * 7.3, -k * 3.1), k) + 0.8 * VN(m / 1.9 + float2(k * 5.1, 0), k + 40) - 1);
                    if (s > bs) { ss = bs; bs = s; best = k; } else if (s > ss) ss = s;
                }
                margin = bs - ss;
                return best;
            }
            int KindAt(float2 m) { float a, b, c; return Classify(m, a, b, c); }
            float ZsAt(float2 m) { float a, b, c; Classify(m, a, b, c); return b; }

            half4 frag(Varyings i) : SV_Target
            {
                float2 m = i.map;
                float px = max(max(abs(ddx(m.x)), abs(ddy(m.x))), 1e-4);   // метров в пикселе
                float margin, zs, zh;
                int k = Classify(m, margin, zs, zh);
                float3 col = PalCol(k);
                // пятна: крупные и мелкие перепады тона; на лугу — проплешины сухой травы; на болоте — лужи
                float f = 0.92 + 0.1 * VN(m / 17, 3) + (px < 0.5 ? 0.05 * VN(m / 4, 4) : 0.025);
                if (k == 1 || k == 5)
                {
                    float dry = VN(m / 48, 5);
                    if (dry > 0.58) { float a = (dry - 0.58) * 1.4; col += (float3(196, 184, 118) / 255 - col) * a * float3(1, 0.7, 0.6); }
                }
                else if (k == 10 && VN(m / 3.2, 8) > 0.6) col = float3(92, 118, 112) / 255;
                // соседи на толщину кромки: вода — тёмная кромка, суша рядом — сырая полоса; постройки — обводка
                float E = max(0.3, px * 1.2);
                int n1 = KindAt(m + float2(-E, 0)), n2 = KindAt(m + float2(E, 0)), n3 = KindAt(m + float2(0, -E)), n4 = KindAt(m + float2(0, E));
                bool edge = n1 != k || n2 != k || n3 != k || n4 != k;
                if (Wet(k))
                {
                    if (margin < 0.5) f *= 1.13 - 0.26 * margin;   // у берега вода светлее
                    if (!Wet(n1) || !Wet(n2) || !Wet(n3) || !Wet(n4)) { col = float3(60, 72, 62) / 255; f = 1; }
                }
                else if (Wet(n1) || Wet(n2) || Wet(n3) || Wet(n4)) f *= 0.84;
                if (Built(k) && edge) { col = float3(43, 38, 33) / 255; f = 1; }
                else if ((k == 2 || k == 19) && edge) f *= k == 2 ? 0.9 : 0.8;
                // светотень (свет справа сверху) и общий подъём тона с высотой
                float g = max(px, 0.6), a0, b0, c0;
                Classify(m + float2(g, 0), a0, b0, c0); float zr = c0;
                Classify(m - float2(g, 0), a0, b0, c0); float zl = c0;
                Classify(m + float2(0, g), a0, b0, c0); float zd = c0;
                Classify(m - float2(0, g), a0, b0, c0); float zu = c0;
                float gx = (zr - zl) / (2 * g), gy = (zd - zu) / (2 * g);
                f *= 1 + clamp((-gx * 0.7 + gy * 0.7) * 0.9, -0.22, 0.22) + zh * 0.02;
                col *= f;
                // горизонтали — граница уровней высоты
                float zd2 = max(px, 0.15);
                if (round(ZsAt(m + float2(zd2, 0))) != round(zs) || round(ZsAt(m + float2(0, zd2))) != round(zs)) col = col * 0.55 + float3(43, 31, 16) / 255;
                // тетрадная клетка 10 м
                if (_Info.w > 0.5 && 10 / px >= 8)
                {
                    float2 d = abs(frac(m / 10 + 0.5) - 0.5) * 10;
                    if (min(d.x, d.y) < px * 0.5) col *= 0.95;
                }
                // зерно бумаги: редкие тёмные и светлые точки
                float2 gp = floor(m / max(px, 0.04));
                float h = H01((int)gp.x, (int)gp.y, 77);
                if (h < 0.035) col *= 0.82; else if (h > 0.985) col = lerp(col, float3(1, 0.98, 0.92), 0.25);
                // цвета и вся арифметика — в sRGB, как в полигоне; проект в линейном пространстве — переводим на выходе
                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = SRGBToLinear(saturate(col));
                #endif
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
