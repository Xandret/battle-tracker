// ═══════════ FortView.cs — укрепления на карте смотрелки (В19): стены и частокол лентами, башни, ворота, обломки ═══════════
// Что и где — FortMap (клетки карты → линии, башни, ворота, проломы). Рисунок — из пробы core/Tests/build-flat.html
// (atlas-export.js): атлас build (башни, вышка, ворота, обломки, мягкий круг тени) и ленты wall_tile / palisade_tile —
// кусок стены 5,8 м и частокола 5,76 м, повторяются вдоль линии; наружа ленты — верх текстуры, зубцы ставятся наружу.
// Тени — как в пробе: влево вниз на (−0,3; 0,24) × высоту (стена 9 м, башня 11, вышка 8), мягкие края.
// Всё неподвижно: сетки строятся один раз на карту. Шейдер — Men (те же атласы полигона, цвета без перевода).
// Слои: земля 0, тени построек 1, стены и частокол 2, башни, ворота и обломки 3 — ниже павших (5) и бойцов (10):
// бойцы стоят на стенах; шатры каменных башен — 11, выше бойцов: гарнизон башни под крышей (Г104).
// Оси: метры карты, y вниз; в мир Unity: X = x, Y = −y.
using System.Collections.Generic;
using BattleCore;
using Journal.Art;
using UnityEngine;

namespace Journal.Viewer
{
    public sealed class FortView
    {
        readonly ArtAtlas build;
        readonly Material buildMat, wallMat, palMat;
        // кровли: фактура (серая, повтор по обеим осям) × цвет ската; шаг куска фактуры вдоль свеса и по скату, м
        static readonly string[] RoofKinds = { "tile", "slate", "shingle", "thatch" };
        static readonly string[] RoofCol = { "#bd6440", "#6e7a87", "#8d6741", "#cfae5e" };
        static readonly Vector2[] RoofTile = { new Vector2(2.88f, 2.7f), new Vector2(3, 3), new Vector2(2.8f, 3.3f), new Vector2(3, 3) };
        readonly Material[] roofMat = new Material[4];
        readonly Transform parent;
        readonly List<GameObject> made = new List<GameObject>();
        public bool Ok => build != null && wallMat != null && palMat != null;
        public FortMap Map { get; private set; }

        const float WallW = 5.7f, WallTile = 5.8f, PalW = 2.0f, PalTile = 5.76f;   // ширина ленты (с выступом зубцов) и длина куска, м
        const float WallH = 9, TowerH = 11, OpenH = 8, WoodH = 8;                   // высота — для длины тени
        static readonly Vector2 Sun = new Vector2(-0.3f, 0.24f);                    // тень на метр высоты, оси карты

        public FortView(Transform parent)
        {
            this.parent = parent;
            build = ArtAtlas.Load("build");
            var sh = Shader.Find("Journal/Men");
            var wt = Resources.Load<Texture2D>("Art/wall_tile"); var pt = Resources.Load<Texture2D>("Art/palisade_tile");
            if (build == null || wt == null || pt == null) { Debug.LogWarning("нет рисунка построек Art/build, wall_tile, palisade_tile — выгрузи из build-flat.html"); return; }
            buildMat = new Material(sh) { mainTexture = build.Tex };
            wallMat = new Material(sh) { mainTexture = wt }; palMat = new Material(sh) { mainTexture = pt };
            for (int k = 0; k < 4; k++) { var rt = Resources.Load<Texture2D>("Art/roof_" + RoofKinds[k] + "_tile"); if (rt != null) roofMat[k] = new Material(sh) { mainTexture = rt }; }
        }

        sealed class Geo
        {
            public readonly List<Vector3> V = new List<Vector3>(); public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> U = new List<Vector2>(); public readonly List<Vector4> P = new List<Vector4>(); public readonly List<int> I = new List<int>();
            public int Vert(float x, float y, float u, float v, Color32 c, Vector4 p) { V.Add(new Vector3(x, -y, 0)); U.Add(new Vector2(u, v)); C.Add(c); P.Add(p); return V.Count - 1; }
            public void Tri(int a, int b, int c) { I.Add(a); I.Add(b); I.Add(c); }
            public void Quad(Part p, Aff m, Color32 col, Vector4 prm)
            {
                if (!p.Ok) return;
                (float, float) W(float x, float y) => (m.a * x + m.c * y + m.e, m.b * x + m.d * y + m.f);
                var (x0, y0) = W(p.X0, p.Y0); var (x1, y1) = W(p.X1, p.Y0); var (x2, y2) = W(p.X1, p.Y1); var (x3, y3) = W(p.X0, p.Y1);
                int b = Vert(x0, y0, p.U0, p.V1, col, prm); Vert(x1, y1, p.U1, p.V1, col, prm); Vert(x2, y2, p.U1, p.V0, col, prm); Vert(x3, y3, p.U0, p.V0, col, prm);
                Tri(b, b + 1, b + 2); Tri(b, b + 2, b + 3);
            }
            public Mesh ToMesh() { var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; Fill(mesh); return mesh; }
            public void Fill(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(V); mesh.SetColors(C); mesh.SetUVs(0, U); mesh.SetUVs(1, P); mesh.SetTriangles(I, 0);
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
            }
        }

        // створки ворот (Г104): по записи боя — закрыты, открыты или распахнуты, пока в проходе свои; поворот на петлях внутрь
        // за 1,2 с. Hinge — середина створок у наружного края прохода, O — наружу, Tg — вдоль стены
        // Hit — сколько секунд назад по воротам пришёлся удар (дрожат, летят щепки), Broken — выбиты (Г105)
        sealed class GateDraw { public Vector2 Mid, O, Tg; public float Half, Open = -1, Hit = 99; public int Rec = -1, HitFrame = -1; public bool Broken; }
        readonly List<GateDraw> gates = new List<GateDraw>();
        Mesh leafMesh; Recording gatesFor; Vector2 leafUv;

        public void SetMap(TerrainMap m)
        {
            foreach (var go in made) Object.Destroy(go);
            made.Clear(); Map = null; gates.Clear(); leafMesh = null; gatesFor = null;
            if (!Ok || m == null) return;
            var f = FortMap.Build(m); Map = f;
            if (f.Lines.Count == 0 && f.Towers.Count == 0 && f.Gates.Count == 0 && f.Rubbles.Count == 0 && f.Houses.Count == 0) return;
            Geo shadows = new Geo(), walls = new Geo(), pals = new Geo(), top = new Geo(), roofTop = new Geo();
            var roofs = new Geo[4]; for (int k = 0; k < 4; k++) roofs[k] = new Geo();
            var plain = Vector4.zero; var white = new Color32(255, 255, 255, 255);
            var shadowP = new Vector4(0, 0, 1, 0); var dark = new Color32(28, 22, 12, 255);
            var disc = build.Get("shadow/disc"); float du = (disc.U0 + disc.U1) / 2, dv = (disc.V0 + disc.V1) / 2;   // середина круга — непрозрачная
            foreach (var L in f.Lines)
            {
                if (L.Mat == FortMap.Trench) continue;   // вал и окоп — земля (клетки)
                bool wall = L.Mat == FortMap.Wall;
                if (wall) Ribbon(shadows, L, 5.2f, 0, 0, Sun * WallH, 1.3f, dark, 0.38f, du, dv);
                else Ribbon(shadows, L, 1.2f, 0, 0, Sun * 3.5f, 0.6f, dark, 0.3f, du, dv);
                Ribbon(wall ? walls : pals, L, wall ? WallW : PalW, wall ? WallTile : PalTile, 1, Vector2.zero, 0, white, 1, 0, 0);
            }
            // торцы стен у пролома и у края: тёмная кромка поперёк ленты (у башен и ворот её закроют они)
            var ink = new Color32(34, 24, 15, 255);
            foreach (var L in f.Lines)
            {
                if (L.Mat != FortMap.Wall || L.Closed || L.P.Count < 2) continue;
                foreach (var (p, q) in new[] { (L.P[0], L.P[1]), (L.P[L.P.Count - 1], L.P[L.P.Count - 2]) })
                {
                    var t = new Vector2(p.x - q.x, p.y - q.y); if (t.sqrMagnitude < 1e-6f) continue; t.Normalize();
                    var nn = new Vector2(-t.y, t.x) * (WallW / 2 - 0.25f);
                    int b0 = top.Vert(p.x + nn.x, p.y + nn.y, du, dv, ink, shadowP), b1 = top.Vert(p.x - nn.x, p.y - nn.y, du, dv, ink, shadowP);
                    int b2 = top.Vert(p.x - nn.x + t.x * 0.16f, p.y - nn.y + t.y * 0.16f, du, dv, ink, shadowP), b3 = top.Vert(p.x + nn.x + t.x * 0.16f, p.y + nn.y + t.y * 0.16f, du, dv, ink, shadowP);
                    top.Tri(b0, b1, b2); top.Tri(b0, b2, b3);
                }
            }
            // башни: тень — мягкий круг; каменные — шатёр одной кровли на крепость, у мелких — открытая площадка
            string roof = Kits.Hash(f.Cols * 31 + f.Rows, 4) < 0.5 ? "slate" : "tile";
            foreach (var T in f.Towers)
            {
                bool wood = T.Mat == FortMap.Palisade, open = !wood && T.R < 3.5f && Kits.Hash(T.Seed, 5) < 0.4;
                float h = wood ? WoodH : open ? OpenH : TowerH, s = T.R / 1.05f;
                top.Quad(disc, Aff.At(T.X + Sun.x * h, T.Y + Sun.y * h).S(s, s), new Color32(28, 22, 12, 96), shadowP);
            }
            foreach (var T in f.Towers)
            {
                bool wood = T.Mat == FortMap.Palisade, open = !wood && T.R < 3.5f && Kits.Hash(T.Seed, 5) < 0.4;
                if (wood) { float s = T.R * 2 * 0.9f / 4.6f; top.Quad(build.Get("tower/wood"), Aff.At(T.X, T.Y).S(s, s), white, plain); }
                else { float s = T.R / 5; (open ? top : roofTop).Quad(build.Get("tower/stone/" + (open ? "open" : roof)), Aff.At(T.X, T.Y).R((float)Kits.Hash(T.Seed, 6) * 6.283f).S(s, s), white, plain); }   // под шатром — гарнизон внутри: шатёр выше бойцов
            }
            // ворота (09.10.2026, отзыв Алекса «очень странно отрисовываются»): проход открыт сверху — в нём видна дорога;
            // по бокам — каменные косяки во всю толщину стены, у наружного края — две створки из досок с железными полосами.
            // Всё — по оси прохода (Ax, Ay), так что и на косой стене ворота встают вдоль неё
            foreach (var G in f.Gates)
            {
                var o = new Vector2(G.Out * G.Ax, G.Out * G.Ay); if (o.sqrMagnitude < 1e-6f) o = new Vector2(0, -1); o.Normalize();
                var tg = new Vector2(-o.y, o.x); var c0 = new Vector2(G.X, G.Y);
                bool wood = G.Mat == FortMap.Palisade;
                float depth = wood ? PalW + 0.6f : WallW, half = Mathf.Max(2.2f, G.W / 2 - (wood ? 0.3f : 0.6f)), jamb = wood ? 0.45f : 0.8f;
                var stone = Hex(wood ? "#6e4a2c" : "#cfc8b6"); var plank = Hex("#94612f"); var plankD = Hex("#5c3a1b"); var iron = Hex("#4d5157");
                void R(Vector2 c, float ht, float ho, Color32 col)   // прямоугольник: полуразмеры вдоль стены (ht) и вдоль прохода (ho)
                {
                    var a = c - tg * ht - o * ho; var b = c + tg * ht - o * ho; var cc = c + tg * ht + o * ho; var d = c - tg * ht + o * ho;
                    int i0 = top.Vert(a.x, a.y, du, dv, col, shadowP); top.Vert(b.x, b.y, du, dv, col, shadowP); top.Vert(cc.x, cc.y, du, dv, col, shadowP); top.Vert(d.x, d.y, du, dv, col, shadowP);
                    top.Tri(i0, i0 + 1, i0 + 2); top.Tri(i0, i0 + 2, i0 + 3);
                }
                // тень в проходе от свода стены — мягкой полосой поперёк
                R(c0, half, depth / 2, new Color32(30, 22, 12, 70));
                foreach (int sd in new[] { -1, 1 })
                {
                    var jc = c0 + tg * sd * (half + jamb / 2);
                    R(jc, jamb / 2 + 0.08f, depth / 2 + 0.08f, ink); R(jc, jamb / 2, depth / 2, stone);
                }
                // створки — у наружного края, с щелью посередине (рисует BuildLeaves: они открываются)
                gates.Add(new GateDraw { Mid = c0 + o * (depth / 2 - 0.55f), O = o, Tg = tg, Half = half });
            }
            foreach (var R in f.Rubbles) top.Quad(build.Get("rubble/" + (R.Seed % 3)), Aff.At(R.X, R.Y).R((float)Kits.Hash(R.Seed, 7) * 6.283f), white, plain);
            // дома: вальмовая крыша на прямоугольник клеток; кровля — по дому (в остроге — солома и тёс), тень по высоте 6 м
            foreach (var Hs in f.Houses)
            {
                double r = Kits.Hash(Hs.Seed, 8);
                int kind = f.Rustic ? (r < 0.6 ? 3 : 2) : (r < 0.5 ? 0 : r < 0.85 ? 1 : 2);
                float x0 = Hs.X0 + 0.4f, y0 = Hs.Y0 + 0.4f, x1 = Hs.X1 - 0.4f, y1 = Hs.Y1 - 0.4f;
                SoftRect(shadows, x0, y0, x1, y1, Sun * 6, 1.0f, dark, 0.34f, du, dv);
                Roof(roofs[kind], x0, y0, x1, y1, Hex(RoofCol[kind]), RoofTile[kind], Hs.Seed);
            }
            Layer("Тени построек", shadows, buildMat, 1);
            for (int k = 0; k < 4; k++) if (roofMat[k] != null) Layer("Кровли: " + RoofKinds[k], roofs[k], roofMat[k], 3);
            Layer("Стены", walls, wallMat, 2);
            Layer("Частокол", pals, palMat, 2);
            Layer("Башни и ворота", top, buildMat, 3);
            Layer("Шатры башен", roofTop, buildMat, 11);   // выше бойцов (10): кто в башне — под крышей
            if (gates.Count > 0)
            {
                leafUv = new Vector2(du, dv); leafMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; leafMesh.MarkDynamic();
                var go = new GameObject("Створки ворот"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = leafMesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = buildMat; r.sortingOrder = 3;
                made.Add(go);
                foreach (var G in gates) G.Open = 0;
                BuildLeaves();
            }
        }

        // ворота по записи на время t: чьи ворота закрыты — створки сходятся, открыты или в проходе свои — расходятся
        public void Gates(Recording rec, double t)
        {
            if (leafMesh == null) return;
            if (rec != gatesFor)
            {
                gatesFor = rec;
                foreach (var G in gates)
                {
                    G.Rec = -1; float bd = 12;
                    if (rec != null) for (int g = 0; g < rec.Gates.Count; g++) { float d = Vector2.Distance(G.Mid, new Vector2(rec.Gates[g].X, rec.Gates[g].Y)); if (d < bd) { bd = d; G.Rec = g; } }
                }
            }
            int fr = rec == null || rec.Frames.Count == 0 ? 0 : Mathf.Clamp((int)(t / rec.Dt), 0, rec.Frames.Count - 1);
            bool dirty = false; float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            foreach (var G in gates)
            {
                int st = G.Rec >= 0 ? rec.GateAt(G.Rec, fr) : 0;
                float want = st != 0 ? 1 : 0, was = G.Open;
                G.Open = Mathf.MoveTowards(was, want, dt / 1.2f);
                dirty |= G.Open != was;
                bool broken = (st & 4) != 0; dirty |= broken != G.Broken; G.Broken = broken;
                // удар: прочность упала в одном из двух последних кадров — створки вздрагивают, летят щепки
                float hit = 99;
                if (G.Rec >= 0 && !broken)
                    for (int f = fr; f >= Mathf.Max(1, fr - 2); f--)
                        if (rec.GateHpAt(G.Rec, f, out var h1, out _) && rec.GateHpAt(G.Rec, f - 1, out var h0, out _) && h1 < h0 - 1e-3f || rec.GateHpAt(G.Rec, f, out _, out _) && !rec.GateHpAt(G.Rec, f - 1, out _, out _))
                        { hit = (float)(t - f * rec.Dt); G.HitFrame = f; break; }
                if (hit < 0.6f || G.Hit < 0.6f) dirty = true;
                G.Hit = hit;
            }
            if (dirty) BuildLeaves();
        }
        // две створки из досок с железными полосами, на петлях у косяков; открыты — повёрнуты внутрь вдоль прохода
        void BuildLeaves()
        {
            var g = new Geo(); var prm = new Vector4(0, 0, 1, 0);
            var ink = new Color32(34, 24, 15, 255); var plank = Hex("#94612f"); var plankD = Hex("#5c3a1b"); var iron = Hex("#4d5157");
            void Q(Vector2 c, Vector2 a, float ha, Vector2 b, float hb, Color32 col)
            {
                var v0 = c - a * ha - b * hb; var v1 = c + a * ha - b * hb; var v2 = c + a * ha + b * hb; var v3 = c - a * ha + b * hb;
                int i = g.Vert(v0.x, v0.y, leafUv.x, leafUv.y, col, prm); g.Vert(v1.x, v1.y, leafUv.x, leafUv.y, col, prm);
                g.Vert(v2.x, v2.y, leafUv.x, leafUv.y, col, prm); g.Vert(v3.x, v3.y, leafUv.x, leafUv.y, col, prm);
                g.Tri(i, i + 1, i + 2); g.Tri(i, i + 2, i + 3);
            }
            var chip = Hex("#b98a52");
            foreach (var G in gates)
            {
                float e = G.Open * G.Open * (3 - 2 * G.Open), ang = e * 1.45f, lh = G.Half / 2 - 0.05f;   // до 83°: к стене прохода
                // удар тараном или топорами: створки вдавливает внутрь и отпускает (0,25 с), щепки разлетаются наружу
                float jolt = G.Hit < 0.25f ? 0.12f * (1 - G.Hit / 0.25f) : 0;
                if (G.Hit < 0.6f)
                    for (int k = 0; k < 6; k++)
                    {
                        float hs = (float)Kits.Hash(G.HitFrame * 31 + k, 3), a = (hs - 0.5f) * 2.4f, sp = 2 + 3 * (float)Kits.Hash(G.HitFrame * 31 + k, 4), u = G.Hit;
                        var p = G.Mid + G.O * (0.4f + sp * u) + G.Tg * ((float)Kits.Hash(G.HitFrame * 31 + k, 5) - 0.5f) * G.Half * 1.6f + G.Tg * a * sp * u * 0.5f;
                        var cd = (G.Tg * Mathf.Cos(a * 3) + G.O * Mathf.Sin(a * 3)).normalized;
                        var cc = chip; cc.a = (byte)(255 * (1 - u / 0.6f));
                        Q(p, cd, 0.18f, new Vector2(-cd.y, cd.x), 0.05f, cc);
                    }
                foreach (int sd in new[] { -1, 1 })
                {
                    var hinge = G.Mid + G.Tg * sd * G.Half - G.O * jolt;
                    var dir = -sd * Mathf.Cos(ang) * G.Tg - Mathf.Sin(ang) * G.O; var n = new Vector2(-dir.y, dir.x);
                    if (G.Broken)
                    {
                        // выбиты (Г105): левая створка повисла на одной петле, правая сорвана и лежит в проходе; по проходу — доски
                        float sk = sd < 0 ? 1.1f : 0.35f;
                        dir = -sd * Mathf.Cos(sk) * G.Tg - Mathf.Sin(sk) * G.O; n = new Vector2(-dir.y, dir.x);
                        if (sd > 0) hinge = G.Mid - G.O * (1.6f + 0.4f * (float)Kits.Hash((int)(G.Mid.x * 7), 6)) + G.Tg * 0.8f;
                        for (int k = 0; k < 5; k++)
                        {
                            var pp = G.Mid - G.O * (0.6f + 2.6f * (float)Kits.Hash(k * 13 + (int)G.Mid.y, 7)) + G.Tg * ((float)Kits.Hash(k * 17 + (int)G.Mid.x, 8) - 0.5f) * G.Half * 1.6f;
                            float aa = (float)Kits.Hash(k * 19, 9) * 6.283f; var dd = new Vector2(Mathf.Cos(aa), Mathf.Sin(aa));
                            Q(pp, dd, 0.55f, new Vector2(-dd.y, dd.x), 0.12f, plankD);
                        }
                    }
                    var lc = hinge + dir * (lh + 0.02f);
                    Q(lc, dir, lh + 0.07f, n, 0.4f, ink); Q(lc, dir, lh, n, 0.32f, plank);
                    for (int k = 1; k < 4; k++) Q(lc + dir * (-lh + 2 * lh * k / 4), dir, 0.03f, n, 0.32f, plankD);
                    foreach (float fr in new[] { -0.55f, 0.55f }) Q(lc + dir * fr * lh, dir, 0.1f, n, 0.36f, iron);
                }
            }
            g.Fill(leafMesh);
        }

        // лента вдоль линии: ширина w, кусок рисунка длиной tile (0 — сплошная заливка тенью), сдвиг off (тень);
        // feather — мягкий край по бокам (только тень). Наружа (L.Out) — верх текстуры
        static void Ribbon(Geo g, FortMap.Line L, float w, float tile, int textured, Vector2 off, float feather, Color32 col, float alpha, float su, float sv)
        {
            int n = L.P.Count; if (n < 2) return;
            var P = L.P; float s = 0;
            var prm = textured == 1 ? Vector4.zero : new Vector4(0, 0, 1, 0);
            int prevA = -1, prevB = -1, prevFa = -1, prevFb = -1;
            for (int k = 0; k < n; k++)
            {
                bool first = k == 0, last = k == n - 1;
                var p = P[k]; var a = L.Closed && first ? P[n - 2] : P[Mathf.Max(0, k - 1)]; var b = L.Closed && last ? P[1] : P[Mathf.Min(n - 1, k + 1)];
                Vector2 d1 = new Vector2(p.x - a.x, p.y - a.y), d2 = new Vector2(b.x - p.x, b.y - p.y);
                if (d1.sqrMagnitude > 1e-8f) d1.Normalize(); if (d2.sqrMagnitude > 1e-8f) d2.Normalize();
                var t = d1 + d2; if (t.sqrMagnitude < 1e-8f) t = d1.sqrMagnitude > 0 ? d1 : d2; t.Normalize();
                float km = d1.sqrMagnitude > 0 && d2.sqrMagnitude > 0 ? 1 / Mathf.Max(0.5f, Vector2.Dot(d1, t)) : 1;
                var nrm = new Vector2(-t.y, t.x) * km;                     // слева по ходу (как bBand: L)
                if (k > 0) s += Mathf.Sqrt((p.x - P[k - 1].x) * (p.x - P[k - 1].x) + (p.y - P[k - 1].y) * (p.y - P[k - 1].y));
                float u = tile > 0 ? s / tile : 0, cx = p.x + off.x, cy = p.y + off.y, h = w / 2;
                int outSide = L.Out.Count > k ? L.Out[k] : 1;               // +1 — наружа слева: верх текстуры (v = 1) слева
                float vl = outSide > 0 ? 1 : 0, vr = 1 - vl;
                var ca = col; ca.a = (byte)(255 * alpha);
                int A = textured == 1 ? g.Vert(cx + nrm.x * h, cy + nrm.y * h, u, vl, col, prm) : g.Vert(cx + nrm.x * h, cy + nrm.y * h, su, sv, ca, prm);
                int B = textured == 1 ? g.Vert(cx - nrm.x * h, cy - nrm.y * h, u, vr, col, prm) : g.Vert(cx - nrm.x * h, cy - nrm.y * h, su, sv, ca, prm);
                int Fa = -1, Fb = -1;
                if (feather > 0)
                {
                    var c0 = col; c0.a = 0; float hf = h + feather;
                    Fa = g.Vert(cx + nrm.x * hf, cy + nrm.y * hf, su, sv, c0, prm); Fb = g.Vert(cx - nrm.x * hf, cy - nrm.y * hf, su, sv, c0, prm);
                }
                if (k > 0)
                {
                    g.Tri(prevA, A, B); g.Tri(prevA, B, prevB);
                    if (feather > 0) { g.Tri(prevFa, Fa, A); g.Tri(prevFa, A, prevA); g.Tri(prevB, B, Fb); g.Tri(prevB, Fb, prevFb); }
                }
                prevA = A; prevB = B; prevFa = Fa; prevFb = Fb;
            }
        }

        static Color32 Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        // скат светлее, если смотрит к солнцу (вправо вверх) — bFacet пробы
        static Color32 Facet(Color32 b, float nx, float ny)
        {
            float k = (nx * 0.7f - ny * 0.7f) * 0.22f; Color c = b;
            return k >= 0 ? Color.Lerp(c, Color.white, k) : Color.Lerp(c, Color.black, -k);
        }
        // вальмовая крыша: конёк вдоль длинной стороны, четыре ската в два тона; фактура — ряды вдоль свеса; контур
        // по краю, тоньше — конёк и рёбра; у половины домов — труба у конька
        static void Roof(Geo g, float x0, float y0, float x1, float y1, Color32 col, Vector2 tile, int seed)
        {
            float w = x1 - x0, h = y1 - y0, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            bool along = w >= h; float r = (along ? h : w) / 2;
            Vector2 A = along ? new Vector2(x0 + r, cy) : new Vector2(cx, y0 + r), B = along ? new Vector2(x1 - r, cy) : new Vector2(cx, y1 - r);   // концы конька
            var nw = new Vector2(x0, y0); var ne = new Vector2(x1, y0); var se = new Vector2(x1, y1); var sw = new Vector2(x0, y1);
            var neutral = new Vector4(0, 0, 0, 1);
            void Face(Vector2[] P, float nx, float ny)
            {
                var c = Facet(col, nx, ny); int b = g.V.Count;
                foreach (var p in P)
                {
                    float u = ny != 0 ? p.x / tile.x : p.y / tile.x;
                    float v = ny < 0 ? (p.y - y0) / tile.y : ny > 0 ? (y1 - p.y) / tile.y : nx < 0 ? (p.x - x0) / tile.y : (x1 - p.x) / tile.y;
                    g.Vert(p.x, p.y, u, v, c, neutral);
                }
                for (int k = 1; k + 1 < P.Length; k++) g.Tri(b, b + k, b + k + 1);
            }
            // скаты: у длинных сторон — трапеции до конька, у коротких — треугольники
            if (along)
            {
                Face(new[] { nw, ne, B, A }, 0, -1); Face(new[] { se, sw, A, B }, 0, 1);
                Face(new[] { sw, nw, A }, -1, 0); Face(new[] { ne, se, B }, 1, 0);
            }
            else
            {
                Face(new[] { sw, nw, A, B }, -1, 0); Face(new[] { ne, se, B, A }, 1, 0);
                Face(new[] { nw, ne, A }, 0, -1); Face(new[] { se, sw, B }, 0, 1);
            }
            var ink = new Color32(34, 24, 15, 255); var inkA = new Color32(34, 24, 15, 130); var lp = new Vector4(0, 0, 1, 0);
            Seg(g, A, B, 0.09f, inkA, lp);
            foreach (var (c, e) in new[] { (nw, along ? A : A), (sw, along ? A : B), (ne, along ? B : A), (se, B) }) Seg(g, c, e, 0.08f, inkA, lp);
            foreach (var (a, b) in new[] { (nw, ne), (ne, se), (se, sw), (sw, nw) }) Seg(g, a, b, 0.16f, ink, lp);
            if (Kits.Hash(seed, 11) < 0.5)
            {
                var p = Vector2.Lerp(A, B, 0.3f) + (along ? new Vector2(0, -0.6f) : new Vector2(-0.6f, 0));
                Box(g, p, 0.42f, Hex("#9a958a"), lp); Box(g, p, 0.2f, Hex("#3a332c"), lp);
                var q0 = p + new Vector2(-0.42f, -0.42f); var q1 = p + new Vector2(0.42f, -0.42f); var q2 = p + new Vector2(0.42f, 0.42f); var q3 = p + new Vector2(-0.42f, 0.42f);
                Seg(g, q0, q1, 0.07f, ink, lp); Seg(g, q1, q2, 0.07f, ink, lp); Seg(g, q2, q3, 0.07f, ink, lp); Seg(g, q3, q0, 0.07f, ink, lp);
            }
        }
        static void Seg(Geo g, Vector2 a, Vector2 b, float w, Color32 col, Vector4 p)
        {
            var d = b - a; float L = d.magnitude; if (L < 1e-5f) return; d /= L; var n = new Vector2(-d.y, d.x) * (w / 2); a -= d * (w / 2); b += d * (w / 2);
            int i = g.Vert(a.x + n.x, a.y + n.y, 0.5f, 0.5f, col, p); g.Vert(b.x + n.x, b.y + n.y, 0.5f, 0.5f, col, p); g.Vert(b.x - n.x, b.y - n.y, 0.5f, 0.5f, col, p); g.Vert(a.x - n.x, a.y - n.y, 0.5f, 0.5f, col, p);
            g.Tri(i, i + 1, i + 2); g.Tri(i, i + 2, i + 3);
        }
        static void Box(Geo g, Vector2 c, float r, Color32 col, Vector4 p)
        {
            int i = g.Vert(c.x - r, c.y - r, 0.5f, 0.5f, col, p); g.Vert(c.x + r, c.y - r, 0.5f, 0.5f, col, p); g.Vert(c.x + r, c.y + r, 0.5f, 0.5f, col, p); g.Vert(c.x - r, c.y + r, 0.5f, 0.5f, col, p);
            g.Tri(i, i + 1, i + 2); g.Tri(i, i + 2, i + 3);
        }
        // мягкая тень прямоугольника: середина — alpha, края на feather сходят на нет
        static void SoftRect(Geo g, float x0, float y0, float x1, float y1, Vector2 off, float feather, Color32 col, float alpha, float su, float sv)
        {
            var p = new Vector4(0, 0, 1, 0); var c1 = col; c1.a = (byte)(255 * alpha); var c0 = col; c0.a = 0;
            x0 += off.x; x1 += off.x; y0 += off.y; y1 += off.y; float f = feather;
            float[] X = { x0 - f, x0, x1, x1 + f }, Y = { y0 - f, y0, y1, y1 + f };
            var id = new int[4, 4];
            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) id[i, j] = g.Vert(X[i], Y[j], su, sv, (i == 1 || i == 2) && (j == 1 || j == 2) ? c1 : c0, p);
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) { g.Tri(id[i, j], id[i + 1, j], id[i + 1, j + 1]); g.Tri(id[i, j], id[i + 1, j + 1], id[i, j + 1]); }
        }

        void Layer(string name, Geo g, Material mat, int order)
        {
            if (g.V.Count == 0) return;
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = g.ToMesh();
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = order;
            made.Add(go);
        }
    }
}
