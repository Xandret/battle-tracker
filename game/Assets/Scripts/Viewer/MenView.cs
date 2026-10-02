// ═══════════ MenView.cs — бойцы смотрелки из частей рисунка полигона (В5–В13) ═══════════
// Перенос drawMen / drawDead полигона (core/Tests/polygon-men.js): меняется полигон — меняем и здесь.
// Боец собран из частей атласа (поклажа, тело, голова, щит, оружие; конь — из ног, хвоста, туловища, головы и попоны),
// у каждого — комплект снаряжения отряда по своему номеру. Каждый кадр по данным записи решается, что он делает:
// - рукопашная: передние у врага бьют (колют, рубят сбоку или сверху — каждый удар заново), щит идёт навстречу удару,
//   в миг удара у острия вспышка; задние напирают и поворачиваются к врагу;
// - стрелки: лук натягивают перед своей стрелой и отпускают в миг вылета; арбалет взводят через стремя;
// - под стрелами, что вот-вот упадут, поднимают щиты; древки (копья, пики) к бою опускают у врага, передние первыми;
// - бегство: бегом, щит за спиной, оглядываются, почти половина бросила оружие — оно лежит, где побежали;
//   сплотившиеся первые 4 с вскидывают оружие и подпрыгивают;
// - ноги и шаг — по фазе, набранной по пути (не скользят); конь — аллюр по скорости, стоящий переступает.
// Павшие: первые 0,15 с качаются от удара, потом ложатся головой по удару; всадник слетает с убитого коня;
// раненые (Г39) ползут рывками прочь от врага с кровавым следом или корчатся, потом затихают.
// Стрелы летят дугой, тень на земле отходит с высотой. Всё — сетками с шейдером Men.
// Оси: как в полигоне — метры карты, y вниз; курс h — поворот canvas (вперёд — −y); в мир Unity: X = x, Y = −y.
using System;
using System.Collections.Generic;
using Journal.Art;
using UnityEngine;

namespace Journal.Viewer
{
    // аффинное преобразование как у canvas: x' = a x + c y + e, y' = b x + d y + f
    public struct Aff
    {
        public float a, b, c, d, e, f;
        public static Aff At(float x, float y) => new Aff { a = 1, d = 1, e = x, f = y };
        public Aff T(float x, float y) => new Aff { a = a, b = b, c = c, d = d, e = e + a * x + c * y, f = f + b * x + d * y };
        public Aff R(float r) { float co = Mathf.Cos(r), s = Mathf.Sin(r); return new Aff { a = a * co + c * s, b = b * co + d * s, c = c * co - a * s, d = d * co - b * s, e = e, f = f }; }
        public Aff S(float x, float y) => new Aff { a = a * x, b = b * x, c = c * y, d = d * y, e = e, f = f };
        public Aff P(float tx, float ty, float rot, float sx, float sy) => T(tx, ty).R(rot).S(sx, sy);
    }

    public sealed class MenView
    {
        readonly ArtAtlas men, horses, dead;
        readonly Material menMat, horseMat, deadMat;
        readonly Mesh decalMesh = NewMesh(), deadMesh = NewMesh(), deadTopMesh = NewMesh(), horseMesh = NewMesh(), menMesh = NewMesh(), airMesh = NewMesh();
        readonly Batch decals = new Batch(), corpses = new Batch(), deadTop = new Batch(), horseB = new Batch(), menB = new Batch(), air = new Batch();
        Kit[][] kits; string[] looks;
        Recording rec;
        public bool Ok => men != null && horses != null && dead != null;

        // по отряду: доля «древки к бою» по кадрам (В13) и была ли рядом вражья линия в последнем проверенном кадре
        List<float>[] lowL; bool[] lowNear;
        // стрелы отряда по времени вылета — номера в rec.Arrows (дописываются по мере записи)
        List<int>[] arrowsOf; int arrowsSeen;

        static Mesh NewMesh() { var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.MarkDynamic(); return m; }

        public MenView(Transform parent)
        {
            men = ArtAtlas.Load("men"); horses = ArtAtlas.Load("horses"); dead = ArtAtlas.Load("dead");
            if (!Ok) return;
            var sh = Shader.Find("Journal/Men");
            menMat = Mat(sh, men); horseMat = Mat(sh, horses); deadMat = Mat(sh, dead);
            if (!lightSet) { Shader.SetGlobalFloat("_JLight", 1); lightSet = true; }
            // слои снизу вверх: кровь → павшие → их головы, оружие, щиты и упавшие стрелы → кони → бойцы → стрелы в воздухе, вспышки
            Layer(parent, "Кровь", decalMesh, menMat, 4);
            Layer(parent, "Павшие", deadMesh, deadMat, 5);
            Layer(parent, "Оружие павших и стрелы на земле", deadTopMesh, menMat, 6);
            Layer(parent, "Кони", horseMesh, horseMat, 9);
            Layer(parent, "Бойцы", menMesh, menMat, 10);
            Layer(parent, "Стрелы в полёте", airMesh, menMat, 20);
        }
        // материал атласа: рисунок и карта объёма (ArtNormals, <атлас>_n) — свет и блеск металла (В15)
        static bool lightSet;
        static Material Mat(Shader sh, ArtAtlas a)
        {
            var m = new Material(sh) { mainTexture = a.Tex };
            if (a.Normals != null) { m.SetTexture("_NormalTex", a.Normals); m.SetFloat("_JHasNormals", 1); }
            return m;
        }
        // свет вкл/выкл (сравнить с плоским рисунком полигона)
        public static bool Light { get => Shader.GetGlobalFloat("_JLight") > 0.5f; set { Shader.SetGlobalFloat("_JLight", value ? 1 : 0); lightSet = true; } }
        static void Layer(Transform parent, string name, Mesh mesh, Material mat, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = order;
        }

        public void SetRecording(Recording r)
        {
            rec = r;
            int n = r.Units.Count;
            kits = new Kit[n][]; looks = new string[n];
            lowL = new List<float>[n]; lowNear = new bool[n]; arrowsOf = new List<int>[n]; arrowsSeen = 0;
            for (int i = 0; i < n; i++)
            {
                looks[i] = Kits.LookOf(r.Units[i].Tpl, r.Units[i].Type);
                kits[i] = Kits.Of(r.Units[i].Id, looks[i]);
                lowL[i] = new List<float>(); arrowsOf[i] = new List<int>();
            }
        }

        // ── сетка из четырёхугольников ──
        sealed class Batch
        {
            public readonly List<Vector3> V = new List<Vector3>(); public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> U = new List<Vector2>(); public readonly List<Vector4> P = new List<Vector4>(); public readonly List<int> I = new List<int>();
            public readonly List<Vector4> X = new List<Vector4>();   // куда в мире смотрят оси текстуры u, v — для света (В15)
            public void Clear() { V.Clear(); C.Clear(); U.Clear(); P.Clear(); X.Clear(); I.Clear(); }
            public void Quad(Part p, Aff m, Color32 col, Vector4 prm)
            {
                if (!p.Ok) return;
                int b = V.Count;
                Add(m, p.X0, p.Y0); Add(m, p.X1, p.Y0); Add(m, p.X1, p.Y1); Add(m, p.X0, p.Y1);
                U.Add(new Vector2(p.U0, p.V1)); U.Add(new Vector2(p.U1, p.V1)); U.Add(new Vector2(p.U1, p.V0)); U.Add(new Vector2(p.U0, p.V0));
                // u — вдоль +x части: (a, b) карты → мир (a, −b); v — к переду (−y части): (−c, −d) карты → мир (−c, d)
                float ul = Mathf.Sqrt(m.a * m.a + m.b * m.b), vl = Mathf.Sqrt(m.c * m.c + m.d * m.d);
                var ax = new Vector4(ul > 1e-6f ? m.a / ul : 1, ul > 1e-6f ? -m.b / ul : 0, vl > 1e-6f ? -m.c / vl : 0, vl > 1e-6f ? m.d / vl : 1);
                for (int k = 0; k < 4; k++) { C.Add(col); P.Add(prm); X.Add(ax); }
                I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3);
            }
            void Add(Aff m, float x, float y) => V.Add(new Vector3(m.a * x + m.c * y + m.e, -(m.b * x + m.d * y + m.f), 0));
            public void To(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(V); mesh.SetColors(C); mesh.SetUVs(0, U); mesh.SetUVs(1, P); mesh.SetUVs(2, X); mesh.SetTriangles(I, 0);
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
            }
        }

        // ── позы и движения (как в полигоне) ──
        static float H(int a, int b) => (float)Kits.Hash(a, b);
        static float Frac(float v) => v - Mathf.Floor(v);
        static float Ease(float p) => p * p * (3 - 2 * p);
        static float[] Mix(float[] a, float[] b, float e) { var r = new float[5]; for (int i = 0; i < 5; i++) r[i] = a[i] + (b[i] - a[i]) * e; return r; }
        static Aff Pose(Aff m, float[] p) => m.P(p[0], p[1], p[2], p[3], p[4]);
        static bool Thrust(string w) => w == "spear" || w == "fork" || w == "pike" || w == "lance";

        // поза в покое: где оружие и щит, [x, y, поворот, масштаб поперёк, вдоль]; low — насколько опустили древки (В13):
        // 0 — стоймя (марш, покой), 1 — к бою: копья в 2 передних шеренгах, пики в 4, передняя — первой
        static (float[] W, float[] Sh) Rest(Kit k, int rank, float low)
        {
            float[] W = null, Sh = null;
            string w = k.Weapon;
            if (w == "spear" || w == "fork") W = Mix(new[] { 0.21f, -0.06f, 0.15f, 1, 0.2f }, new[] { 0.2f, -0.1f, 0, 1, 1 }, rank < 2 ? Ease(Mathf.Clamp01(low * 1.25f - rank * 0.2f)) : 0);
            else if (w == "pike") W = Mix(new[] { 0.15f, -0.04f, 0.1f, 1, 0.12f }, new[] { rank % 2 == 1 ? -0.3f : 0.12f, 0, 0, 1, 1 }, rank < 4 ? Ease(Mathf.Clamp01(low * 1.4f - rank * 0.13f)) : 0);
            else if (w == "lance") W = new[] { 0.22f, 0.05f, 0.05f, 1, 0.24f };
            else if (w != "bow" && w != "crossbow") W = new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f };
            if (k.Shield != null) Sh = k.Horse ? new[] { -0.27f, 0, Mathf.PI / 2, 0.75f, 0.42f } : k.ShieldShape == "buckler" ? new[] { -0.2f, 0.07f, 0, 1, 0.6f } : new[] { -0.16f, -0.23f, -0.25f, 1, -0.42f };
            return (W, Sh);
        }
        // удары (В8, В13): доля круга удара p 0…1
        static float ThrustOff(float p)   // колющие: замах назад, выпад, держит, возврат
        {
            if (p < 0.3f) return 0.12f * Ease(p / 0.3f);
            if (p < 0.42f) return 0.12f - 0.62f * Ease((p - 0.3f) / 0.12f);
            if (p < 0.5f) return -0.5f;
            if (p < 0.8f) return -0.5f + 0.5f * Ease((p - 0.5f) / 0.3f);
            return 0;
        }
        static (float r, float sy) SwingAng(float p)   // рубящие сбоку: замах вправо-вверх, удар поперёк, возврат
        {
            if (p < 0.35f) { float e = Ease(p / 0.35f); return (0.3f + 1.1f * e, 0.65f - 0.25f * e); }
            if (p < 0.47f) { float e = (p - 0.35f) / 0.12f; return (1.4f - 2.3f * e, 0.4f + 0.6f * e); }
            if (p < 0.85f) { float e = Ease((p - 0.47f) / 0.38f); return (-0.9f + 1.2f * e, 1 - 0.35f * e); }
            return (0.3f, 0.65f);
        }
        static (float y, float sy) ChopPose(float p)   // сверху (В13): замах вверх — оружие к нам, видно коротким; удар вниз-вперёд
        {
            if (p < 0.35f) { float e = Ease(p / 0.35f); return (-0.08f + 0.1f * e, 0.65f - 0.5f * e); }
            if (p < 0.47f) { float e = (p - 0.35f) / 0.12f; return (0.02f - 0.2f * e, 0.15f + 0.95f * e); }
            if (p < 0.85f) { float e = Ease((p - 0.47f) / 0.38f); return (-0.18f + 0.1f * e, 1.1f - 0.45f * e); }
            return (-0.08f, 0.65f);
        }
        // бегущий оглядывается через плечо (В13): почти половина — раз в 2–4 с на полсекунды
        static float LookBack(float t, int s)
        {
            if (H(s, 43) > 0.45f) return 0;
            float u = Frac(t / (2 + 2 * H(s, 44)) + H(s, 45));
            return u < 0.18f ? (H(s, 46) < 0.5f ? -0.9f : 0.9f) * Mathf.Sin(u / 0.18f * Mathf.PI) : 0;
        }
        // взгляд по сторонам: раз в несколько секунд боец поворачивает голову и плечи
        static float Glance(float t, int s)
        {
            int slot = Mathf.FloorToInt(t / 3 + H(s, 9) * 3);
            if (H(s * 7 + slot, 11) > 0.35f) return 0;
            return (H(s + slot, 12) - 0.5f) * 0.9f * Mathf.Sin(Frac(t / 3 + H(s, 9) * 3) * Mathf.PI);
        }
        static bool Drops(int s) => H(s, 21) < 0.45f;   // кто бросает оружие на бегу (В11)

        // конь из частей (В13): места ног, корень хвоста и шеи; аллюр по скорости — шаг, рысь, галоп
        static readonly float[,] HLeg = { { -0.17f, -0.5f }, { 0.17f, -0.5f }, { -0.17f, 0.66f }, { 0.17f, 0.66f } };   // ЛП, ПП, ЛЗ, ПЗ
        sealed class Gait { public float Amp, Nod, NodN, Tail, Bob; public float[] Off; }
        static readonly Gait Walk = new Gait { Amp = 0.15f, Off = new[] { 0.25f, 0.75f, 0, 0.5f }, Nod = 0.04f, NodN = 2, Tail = 0.1f, Bob = 0.008f };
        static readonly Gait Trot = new Gait { Amp = 0.22f, Off = new[] { 0, 0.5f, 0.5f, 0 }, Nod = 0.012f, NodN = 2, Tail = 0.07f, Bob = 0.025f };
        static readonly Gait Gallop = new Gait { Amp = 0.32f, Off = new[] { 0.4f, 0.5f, 0, 0.1f }, Nod = 0.06f, NodN = 1, Tail = 0.18f, Bob = 0.035f };
        readonly float[] legs = new float[4];
        // поза коня: ноги (сдвиг вперёд со знаком минус), кивок, хвост (рад), качка всадника; стоит — переступает и машет хвостом
        void HorsePose(float v, float ph, float t, int s, out float nod, out float tail, out float bob)
        {
            var G = v < 0.35f ? null : v < 2.3f ? Walk : v < 4.8f ? Trot : Gallop;
            const float tau = 2 * Mathf.PI;
            if (G == null)
            {
                int slot = Mathf.FloorToInt(t / 3.5f + H(s, 31) * 4); float r = H(s * 13 + slot, 32), u = Frac(t / 3.5f + H(s, 31) * 4);
                float pulse = Mathf.Sin(u * Mathf.PI);
                legs[0] = legs[1] = 0; legs[2] = r < 0.25f ? -0.06f * pulse : 0; legs[3] = r > 0.75f ? -0.06f * pulse : 0;
                nod = r > 0.4f && r < 0.6f ? 0.05f * pulse : 0.01f * Mathf.Sin(t * 0.8f + s);
                tail = (r < 0.5f ? 0.25f : 0.06f) * Mathf.Sin(t * 5 + s) * pulse; bob = 0;
                return;
            }
            for (int i = 0; i < 4; i++) legs[i] = -G.Amp * Mathf.Sin(tau * (ph - G.Off[i]));
            nod = -G.Nod * Mathf.Sin(tau * G.NodN * ph); tail = G.Tail * Mathf.Sin(tau * ph + 1); bob = G.Bob * Mathf.Sin(tau * 2 * ph);
        }

        public void Hide() { foreach (var m in new[] { decalMesh, deadMesh, deadTopMesh, horseMesh, menMesh, airMesh }) m.Clear(); }

        // ── по кадру: схватки, стрелы над головой, древки к бою ──
        Dictionary<int, float>[] eng;                          // отряд → тело → куда враг (угол, рад)
        readonly HashSet<long> fire = new HashSet<long>();     // тела, куда через 1,5 с упадут стрелы: поднимают щиты
        readonly Dictionary<int, List<int>> grid = new Dictionary<int, List<int>>();
        readonly Stack<List<int>> listPool = new Stack<List<int>>();
        readonly List<float> gx = new List<float>(), gy = new List<float>(); readonly List<long> gk = new List<long>();
        static int Cell(float x, float y, float size) => (Mathf.FloorToInt(x / size) & 0xFFFF) << 16 | (Mathf.FloorToInt(y / size) & 0xFFFF);
        void GridClear() { foreach (var l in grid.Values) { l.Clear(); listPool.Push(l); } grid.Clear(); }
        void GridAdd(int key, int v) { if (!grid.TryGetValue(key, out var l)) grid[key] = l = listPool.Count > 0 ? listPool.Pop() : new List<int>(); l.Add(v); }

        void Engage(float[] fa, float[] fb, int a, int b)
        {
            var ia = rec.Units[a]; var ib = rec.Units[b];
            float db = (ib.Figs.Count > 0 ? (float)ib.Figs[0][1] : 5) / 2;
            for (int k = 0; 5 + 2 * k < fa.Length; k++)
            {
                float x = fa[4 + 2 * k], y = fa[5 + 2 * k];
                if (float.IsNaN(x)) continue;
                float best = float.MaxValue, bx = 0, by = 0;
                for (int j = 0; 5 + 2 * j < fb.Length; j++)
                {
                    float ex = fb[4 + 2 * j]; if (float.IsNaN(ex)) continue;
                    float dx = ex - x, dy = fb[5 + 2 * j] - y, d = dx * dx + dy * dy;
                    if (d < best) { best = d; bx = dx; by = dy; }
                }
                float reach = (k < ia.Figs.Count ? (float)ia.Figs[k][1] : 5) / 2 + db + 3;
                if (best < reach * reach) (eng[a] ??= new Dictionary<int, float>())[k] = Mathf.Atan2(by, bx);
            }
        }
        void PrepFrame(int f0, float t, Func<float, float, float, bool> In)
        {
            int n = rec.Units.Count;
            if (eng == null || eng.Length != n) eng = new Dictionary<int, float>[n];
            for (int i = 0; i < n; i++) eng[i]?.Clear();
            var F = rec.Frames[f0];
            if (rec.Fights.Count > 0)
            {
                var pairs = rec.Fights[Math.Min(f0, rec.Fights.Count - 1)];
                for (int q = 0; q + 1 < pairs.Length; q += 2) { Engage(F[pairs[q]], F[pairs[q + 1]], pairs[q], pairs[q + 1]); Engage(F[pairs[q + 1]], F[pairs[q]], pairs[q + 1], pairs[q]); }
            }
            // тела на экране — в сетку 10 м; стрелы, что упадут через 0–1,5 с рядом с телом, — щиты над головой
            fire.Clear(); GridClear(); gx.Clear(); gy.Clear(); gk.Clear();
            for (int ui = 0; ui < F.Length; ui++)
            {
                var a = F[ui];
                for (int k = 0; 5 + 2 * k < a.Length; k++)
                {
                    float x = a[4 + 2 * k], y = a[5 + 2 * k];
                    if (float.IsNaN(x) || !In(x, y, 12)) continue;
                    GridAdd(Cell(x, y, 10), gx.Count); gx.Add(x); gy.Add(y); gk.Add(ui * 100000L + k);
                }
            }
            if (gx.Count == 0) return;
            foreach (var ar in rec.Arrows)
            {
                if (ar.T0 > t || ar.T1 <= t || ar.T1 - t > 1.5f || float.IsInfinity(ar.T1) || !In(ar.X1, ar.Y1, 8)) continue;
                int cx = Mathf.FloorToInt(ar.X1 / 10), cy = Mathf.FloorToInt(ar.Y1 / 10);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue(((cx + dx) & 0xFFFF) << 16 | ((cy + dy) & 0xFFFF), out var l))
                            foreach (var i in l) if (Mathf.Abs(gx[i] - ar.X1) < 6 && Mathf.Abs(gy[i] - ar.Y1) < 6) fire.Add(gk[i]);
            }
        }

        // древки к бою (В13): отряд в схватке или край врага ближе 30 м — опускают за 1,2 с, иначе поднимают за 2 с.
        // Отряд — отрезок своего фронта; зазор — между отрезками минус полглубины обоих; близость — раз в 3 кадра (0,6 с)
        static float SegDist(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            float Pt(float px, float py, float x0, float y0, float x1, float y1)
            {
                float vx = x1 - x0, vy = y1 - y0, L = vx * vx + vy * vy, u = L > 0 ? Mathf.Clamp01(((px - x0) * vx + (py - y0) * vy) / L) : 0;
                float ex = px - x0 - u * vx, ey = py - y0 - u * vy; return Mathf.Sqrt(ex * ex + ey * ey);
            }
            return Mathf.Min(Mathf.Min(Pt(ax, ay, cx, cy, dx, dy), Pt(bx, by, cx, cy, dx, dy)), Mathf.Min(Pt(cx, cy, ax, ay, bx, by), Pt(dx, dy, ax, ay, bx, by)));
        }
        float LowerAt(int ui, double ft)
        {
            var L = lowL[ui]; int N = rec.Frames.Count;
            if (L.Count < N)
            {
                float cur = L.Count > 0 ? L[L.Count - 1] : 0; var info = rec.Units[ui];
                for (int f = L.Count; f < N; f++)
                {
                    var p = rec.Frames[f][ui];
                    if (f % 3 == 0)
                    {
                        bool near = false;
                        float h = p[2] * Mathf.Deg2Rad, c = Mathf.Cos(h) * (float)info.Front / 2, s = Mathf.Sin(h) * (float)info.Front / 2;
                        for (int j = 0; j < rec.Units.Count && !near; j++)
                        {
                            var uj = rec.Units[j]; if (uj.Faction == info.Faction || rec.StateAt(j, f) == 2) continue;
                            var e = rec.Frames[f][j]; float he = e[2] * Mathf.Deg2Rad, ce = Mathf.Cos(he) * (float)uj.Front / 2, se = Mathf.Sin(he) * (float)uj.Front / 2;
                            if (SegDist(p[0] - c, p[1] - s, p[0] + c, p[1] + s, e[0] - ce, e[1] - se, e[0] + ce, e[1] + se) - (float)(info.Depth + uj.Depth) / 2 < 30) near = true;
                        }
                        lowNear[ui] = near;
                    }
                    bool fight = false;
                    if (rec.Fights.Count > 0) { var pr = rec.Fights[Math.Min(f, rec.Fights.Count - 1)]; for (int q = 0; q < pr.Length && !fight; q++) fight = pr[q] == ui; }
                    cur = lowNear[ui] || fight ? Mathf.Min(1, cur + (float)rec.Dt / 1.2f) : Mathf.Max(0, cur - (float)rec.Dt / 2);
                    L.Add(cur);
                }
            }
            int f0 = Mathf.Clamp((int)Math.Floor(ft), 0, N - 1), f1 = Math.Min(f0 + 1, N - 1);
            return L[f0] + (L[f1] - L[f0]) * (float)(ft - f0);
        }
        int StateSince(int ui, int f)
        {
            var L = rec.States?[ui]; if (L == null) return 0;
            int since = 0; for (int i = 0; i < L.Count && L[i] <= f; i += 2) since = L[i];
            return since;
        }
        // стрелы отряда — по времени вылета (в живой записи дописываются почти по порядку)
        void IndexArrows()
        {
            for (; arrowsSeen < rec.Arrows.Count; arrowsSeen++)
            {
                var ar = rec.Arrows[arrowsSeen]; if (ar.Unit < 0 || ar.Unit >= arrowsOf.Length) continue;
                var l = arrowsOf[ar.Unit]; int i = l.Count;
                while (i > 0 && rec.Arrows[l[i - 1]].T0 > ar.T0) i--;
                l.Insert(i, arrowsSeen);
            }
        }
        int LowerT0(List<int> idx, float t)   // первая стрела отряда с вылетом не раньше t
        {
            int lo = 0, hi = idx.Count;
            while (lo < hi) { int m = (lo + hi) >> 1; if (rec.Arrows[idx[m]].T0 < t) lo = m + 1; else hi = m; }
            return lo;
        }

        // ── кадр ──
        // Ap — фаза удара из движка (Б2; −1 — считать по своему ритму), Parry — сколько секунд назад принял удар на щит (−1 — нет)
        struct ManP { public float X, Y, Face, Sp, Ph, Shot, Ap, Parry; public int Id, Seed, Rank, Fig, Blow; public bool Atk, Vis; public Kit Kit; }
        readonly List<ManP> M = new List<ManP>();
        readonly Dictionary<int, float> figTop = new Dictionary<int, float>();
        readonly HashSet<long> fallenNow = new HashSet<long>();
        readonly Dictionary<int, (float h, float sw, float nx, float pa)> mel0 = new Dictionary<int, (float, float, float, float)>(), mel1 = new Dictionary<int, (float, float, float, float)>();
        readonly List<(float x, float y, float face, float reach, float ap)> sparks = new List<(float, float, float, float, float)>();

        public void Draw(double t, Color32[] unitCol, Rect view, float ppm)
        {
            if (!Ok || rec == null || rec.Frames.Count == 0) return;
            decals.Clear(); corpses.Clear(); deadTop.Clear(); horseB.Clear(); menB.Clear(); air.Clear(); sparks.Clear();
            double ft = t / rec.Dt; int f0 = Math.Min((int)Math.Floor(ft), rec.Frames.Count - 1), f1 = Math.Min(f0 + 1, rec.Frames.Count - 1); float q = (float)(ft - f0);
            float t32 = (float)t;
            bool In(float x, float y, float pad) => x > view.xMin - pad && x < view.xMax + pad && y > view.yMin - pad && y < view.yMax + pad;
            IndexArrows();
            PrepFrame(f0, t32, In);
            // павшие между кадрами (Б2: в миг удара) — уже лежат, живыми их не рисуем
            fallenNow.Clear();
            for (int i = rec.Dead.Count - 1; i >= 0 && rec.Dead[i].Frame > f0; i--)
                if (rec.Dead[i].Frame == f0 + 1 && rec.Dead[i].T <= t32 && rec.Dead[i].Man > 0) fallenNow.Add((long)rec.Dead[i].Unit << 32 | (uint)rec.Dead[i].Man);
            DrawDead(t, f0, unitCol, In);
            DrawDropped(f0, unitCol, In);
            // ── бойцы ──
            var shadow = new Color32(24, 18, 8, 80); var soft = men.Get("util/soft");
            for (int ui = 0; ui < rec.Units.Count; ui++)
            {
                int st = rec.StateAt(ui, f0); if (st == 2 || f0 >= rec.Men.Count) continue;
                DrawUnit(ui, rec.Men[f0][ui], rec.Men[Math.Min(f1, rec.Men.Count - 1)][ui], q, st, ft, t32, unitCol[ui], In, soft, shadow, ppm);
            }
            // вспышки ударов (В13): в миг удара у острия — звёздочка, за 0,1 с вырастает и гаснет
            var spark = men.Get("util/spark");
            foreach (var (x, y, face, reach, ap) in sparks)
            {
                float e = (ap - 0.42f) / 0.08f, k2 = 0.6f + 0.8f * e;
                air.Quad(spark, Aff.At(x, y).R(face).T(0.2f, reach).S(k2, k2), new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(1 - e))), Vector4.zero);
            }
            DrawArrows(t32, In, ppm);
            decals.To(decalMesh); corpses.To(deadMesh); deadTop.To(deadTopMesh); horseB.To(horseMesh); menB.To(menMesh); air.To(airMesh);
        }

        // ── отряд: бойцы движка (Г75) с их местами и курсами; что каждый делает — по схваткам, стрелам и состоянию ──
        void DrawUnit(int ui, MenFrame m0, MenFrame m1, float q, int st, double ft, float t, Color32 col, Func<float, float, float, bool> In, Part soft, Color32 shadow, float ppm)
        {
            var info = rec.Units[ui]; var ks = kits[ui]; string look = looks[ui];
            bool horse = look == "lance" || look == "barded", flee = st == 1 || st == 3;
            int fi = Math.Min((int)ft, rec.Frames.Count - 1);
            bool cheer = st == 4 && (fi - StateSince(ui, fi)) * rec.Dt < 4;   // сплотились (БД4) — первые 4 с ликуют
            float rd = (float)info.RankDepth, low = LowerAt(ui, ft);
            int fd = 1; foreach (var fg in info.Figs) fd = Math.Max(fd, Mathf.RoundToInt((float)fg[1] / rd));
            // бойцы — с местом, курсом, скоростью и фазой шага (между кадрами — плавно)
            M.Clear();
            int n = m0.Xyh.Length / 3; bool anyVis = false;
            for (int id = 1; id < n; id++)
            {
                float x = m0.Xyh[3 * id];
                if (float.IsNaN(x)) continue;
                float y = m0.Xyh[3 * id + 1], h = m0.Xyh[3 * id + 2], ph = m0.Ph != null ? m0.Ph[id] : 0, sp = 0;
                if (3 * id + 2 < m1.Xyh.Length && !float.IsNaN(m1.Xyh[3 * id]))
                {
                    float x1 = m1.Xyh[3 * id], y1 = m1.Xyh[3 * id + 1];
                    sp = Mathf.Sqrt((x1 - x) * (x1 - x) + (y1 - y) * (y1 - y)) / (float)rec.Dt;
                    x += (x1 - x) * q; y += (y1 - y) * q; h += Mathf.DeltaAngle(h, m1.Xyh[3 * id + 2]) * q;
                    if (m1.Ph != null && id < m1.Ph.Length) ph += (m1.Ph[id] - ph) * q;
                }
                if (fallenNow.Count > 0 && fallenNow.Contains((long)ui << 32 | (uint)id)) continue;
                int fig = m0.Fig[id], seed = info.Id * 7919 + id;
                bool vis = In(x, y, 6); anyVis |= vis;
                M.Add(new ManP { X = x, Y = y, Face = h * Mathf.Deg2Rad, Sp = sp, Ph = ph, Id = id, Seed = seed, Fig = fig, Vis = vis, Shot = float.NaN, Ap = -1, Parry = -1,
                    Rank = (fig < info.Figs.Count ? (int)info.Figs[fig][3] : 0) * fd + m0.Row[id], Kit = ks[(int)(H(seed, 4) * ks.Length)] });
            }
            if (!anyVis) return;
            // рукопашная: в теле у врага бьют передние (ближе к врагу, чем 1,6 шеренги от самого переднего); остальные напирают
            var ef = flee ? null : eng[ui];
            if (rec.MenMelee) MenMelee(m0, m1, q, t);
            else if (ef != null && ef.Count > 0)
            {
                figTop.Clear();
                foreach (var m in M) if (ef.TryGetValue(m.Fig, out var a)) { float pr = m.X * Mathf.Cos(a) + m.Y * Mathf.Sin(a); figTop[m.Fig] = figTop.TryGetValue(m.Fig, out var tp) ? Mathf.Max(tp, pr) : pr; }
                for (int i = 0; i < M.Count; i++)
                {
                    var m = M[i]; if (!ef.TryGetValue(m.Fig, out var a)) continue;
                    float toward = a + Mathf.PI / 2;
                    m.Atk = m.X * Mathf.Cos(a) + m.Y * Mathf.Sin(a) > figTop[m.Fig] - 1.6f * rd;
                    m.Face = m.Atk ? toward : m.Face + Mathf.DeltaAngle(m.Face * Mathf.Rad2Deg, toward * Mathf.Rad2Deg) * Mathf.Deg2Rad * 0.5f;
                    M[i] = m;
                }
            }
            // стрелки (В6): кто ближе всех к точке вылета стрелы, натягивает лук перед ней и отпускает в миг вылета
            bool shoot = look == "bow" || look == "crossbow", active = false;
            if (shoot)
            {
                var idx = arrowsOf[ui]; float back = look == "crossbow" ? 2.6f : 0.3f;
                int j3 = LowerT0(idx, t - 3);
                active = j3 < idx.Count && rec.Arrows[idx[j3]].T0 < t + 3;
                int from = LowerT0(idx, t - back);
                if (from < idx.Count && rec.Arrows[idx[from]].T0 <= t + 0.8f)
                {
                    GridClear();
                    for (int i = 0; i < M.Count; i++) if (M[i].Vis) GridAdd(Cell(M[i].X, M[i].Y, 2), i);
                    for (int qq = from; qq < idx.Count && rec.Arrows[idx[qq]].T0 <= t + 0.8f; qq++)
                    {
                        var ar = rec.Arrows[idx[qq]];
                        int cx = Mathf.FloorToInt(ar.X0 / 2), cy = Mathf.FloorToInt(ar.Y0 / 2), best = -1; float bd = 1.5f;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                if (grid.TryGetValue(((cx + dx) & 0xFFFF) << 16 | ((cy + dy) & 0xFFFF), out var l))
                                    foreach (var i in l) { float d = Mathf.Sqrt((M[i].X - ar.X0) * (M[i].X - ar.X0) + (M[i].Y - ar.Y0) * (M[i].Y - ar.Y0)); if (d < bd) { bd = d; best = i; } }
                        if (best < 0) continue;
                        var mb = M[best]; float dt = t - ar.T0;
                        if (float.IsNaN(mb.Shot) || Mathf.Abs(dt) < Mathf.Abs(mb.Shot)) { mb.Shot = dt; M[best] = mb; }
                    }
                }
            }
            // тени — до бойцов, влево вниз (свет справа сверху)
            if (ppm >= 8)
            {
                float so = horse ? 0.45f : 0.2f;
                foreach (var m in M)
                {
                    if (!m.Vis) continue;
                    var sm = Aff.At(m.X - 0.6f * so, m.Y + 0.8f * so).R(m.Face);
                    (horse ? horseB : menB).Quad(soft, sm.T(0, horse ? 0.12f : 0.02f).S(0.66f, horse ? 2.0f : 0.48f), shadow, new Vector4(0, 0, 1, 0));
                }
            }
            for (int i = 0; i < M.Count; i++)
            {
                var m = M[i]; if (!m.Vis) continue;
                bool engaged = ef != null && ef.ContainsKey(m.Fig), fireHere = fire.Contains(ui * 100000L + m.Fig);
                DrawMan(m, look, horse, flee, cheer, engaged, fireHere, shoot, active, low, t, col, ppm);
            }
        }

        // ── рукопашная по бойцам (Б2): противник, удар и щит — из движка ──
        // Боец смотрит на своего противника; удар проигрывается вокруг своего времени в движке: замах — до (по NextSwing),
        // вспышка — в миг удара (тогда же падает ударенный), возврат — после; принял удар на щит — щит рывком навстречу
        void MenMelee(MenFrame m0, MenFrame m1, float q, float t)
        {
            Fill(mel0, m0); Fill(mel1, m1);
            if (mel0.Count == 0 && mel1.Count == 0) return;
            for (int i = 0; i < M.Count; i++)
            {
                var m = M[i];
                bool a0 = mel0.TryGetValue(m.Id, out var e0), a1 = mel1.TryGetValue(m.Id, out var e1);
                if (!a0 && !a1) continue;
                float h = a0 && a1 && !float.IsNaN(e0.h) && !float.IsNaN(e1.h) ? e0.h + Mathf.DeltaAngle(e0.h, e1.h) * q : a0 && !float.IsNaN(e0.h) ? e0.h : a1 ? e1.h : float.NaN;
                if (!float.IsNaN(h)) m.Face = h * Mathf.Deg2Rad;
                // ближний удар к t: −0,45 с — замах, 0 — удар, +0,55 с — возврат
                float best = float.NaN;
                void Try(float T) { if (float.IsNaN(T)) return; float d = t - T; if (d >= -0.45f && d <= 0.55f && (float.IsNaN(best) || Mathf.Abs(d) < Mathf.Abs(t - best))) best = T; }
                if (a0) { Try(e0.sw); Try(e0.nx); }
                if (a1) { Try(e1.sw); Try(e1.nx); }
                if (!float.IsNaN(best)) { m.Atk = true; m.Ap = 0.45f + (t - best); m.Blow = Mathf.RoundToInt(best * 20); }
                float pa = a1 && !float.IsNaN(e1.pa) && e1.pa <= t ? e1.pa : a0 ? e0.pa : float.NaN;
                if (!float.IsNaN(pa) && t - pa >= 0 && t - pa < 0.35f) m.Parry = t - pa;
                M[i] = m;
            }
        }
        static void Fill(Dictionary<int, (float, float, float, float)> d, MenFrame f)
        {
            d.Clear();
            if (f?.Eng == null) return;
            for (int k = 0; k < f.Eng.Length; k++) d[f.Eng[k]] = (f.EngH[k], f.EngSw[k], f.EngNx[k], f.EngPa[k]);
        }

        // ── один боец: как в drawMen полигона ──
        void DrawMan(ManP m, string look, bool horse, bool flee, bool cheer, bool engaged, bool fireHere, bool shoot, bool active, float low, float t, Color32 col, float ppm)
        {
            var kit = m.Kit; int s = m.Seed; float ph0 = H(s, 5); bool walking = m.Sp > 0.8f;
            var prm = new Vector4(kit.ClothKey == "f" ? kit.Tone : 0, 0, 0, 0); var neutral = Vector4.zero;
            float rot = 0, ox = 0, oy = 0, step = 0, ap = -1; int blow = 0;
            if (m.Atk && m.Ap >= 0) { ap = m.Ap; blow = m.Blow; }   // Б2: удар — когда он в движке
            else if (m.Atk) { float per = 1.1f + 0.9f * H(s, 7), u0 = t / per + H(s, 8); ap = Frac(u0); blow = Mathf.FloorToInt(u0); }
            if (walking && !m.Atk) { step = Mathf.Sin(m.Ph * 6.283f); rot += (m.Sp > 2.6f ? 0.11f : 0.07f) * step; }   // бегом плечи ходят сильнее
            else if (!m.Atk) { rot += 0.035f * Mathf.Sin(t * 0.9f + ph0 * 6.283f) + (engaged ? 0 : Glance(t, s)); ox = 0.02f * Mathf.Sin(t * 0.6f + ph0 * 9); }
            if (engaged && !m.Atk) oy = -0.03f - 0.03f * Mathf.Sin(t * 3 + ph0 * 6.283f);   // задние напирают
            var (PW, PSh) = Rest(kit, m.Rank, low);
            var bas = Aff.At(m.X, m.Y).R(m.Face);
            bool boots = ppm >= 14;
            var bootP = men.Get("boot");

            // бегство (В11): бегом, щит за спиной, оружие несут как придётся или бросили; оглядываются (В13)
            if (flee && !horse)
            {
                rot += LookBack(t, s);
                var Mf = bas.T(ox, -0.05f).R(rot);
                if (step != 0 && boots) { menB.Quad(bootP, Mf.T(-0.09f, 0.03f + 0.14f * step), col, neutral); menB.Quad(bootP, Mf.T(0.09f, 0.03f - 0.14f * step), col, neutral); }
                if (kit.Shield != null && kit.ShieldShape != "buckler") menB.Quad(men.Get(kit.Shield), Pose(Mf, new[] { 0, 0.24f, 0, 0.85f, 0.45f }), col, neutral);
                BodyHead(kit, Mf, col, prm);
                if (!Drops(s))
                {
                    string w = kit.Weapon;
                    if (w == "bow") menB.Quad(men.Get("bow/0"), Pose(Mf, new[] { 0.05f, 0.25f, 0.4f, 0.8f, 0.5f }), col, neutral);
                    else if (w == "crossbow") menB.Quad(men.Get("xbow/1"), Pose(Mf, new[] { 0.05f, 0.3f, 0.5f, 1, 0.5f }), col, neutral);
                    else menB.Quad(men.Get("weapon/" + w), Pose(Mf, Thrust(w) ? new[] { 0.2f, 0.05f, 0.4f + 0.05f * step, 1, 0.25f } : new[] { 0.2f, 0.05f, 0.6f, 1, 0.5f }), col, neutral);
                }
                return;
            }
            // конь по частям (В13): аллюр по скорости, стоящий переступает и машет хвостом
            if (horse)
            {
                bool run = walking || m.Atk;
                HorsePose(m.Sp, m.Ph, t, s, out var nod, out var tail, out var bob);
                var leg = horses.Get("hrig/leg/" + kit.Coat);
                for (int i = 0; i < 4; i++) horseB.Quad(leg, bas.T(HLeg[i, 0], HLeg[i, 1] + legs[i]), col, neutral);
                horseB.Quad(horses.Get("hrig/tail/" + kit.Coat), bas.T(0, 0.84f).R(tail), col, neutral);
                horseB.Quad(horses.Get("hrig/body/" + kit.Coat), bas, col, neutral);
                horseB.Quad(horses.Get("hrig/head/" + kit.Coat + (kit.Bard == "full" ? "/full" : "")), bas.T(0, -0.5f + nod), col, neutral);
                horseB.Quad(horses.Get(kit.Bard == "full" ? "hrig/cover/full/" + kit.C2 : "hrig/cover/" + (kit.Bard == "cloth" ? "cloth" : "none")), bas, col, neutral);
                var Mr = bas.T(ox, 0.02f + bob).R(rot * 0.35f);
                var W = PW;
                if (kit.Weapon == "lance") { if (run) W = new[] { 0.2f, 0.25f + (m.Atk ? ThrustOff(ap) : 0), -0.04f, 1, 1 }; }
                else if (m.Atk) { var (r2, sy2) = SwingAng(ap); W = new[] { 0.22f, -0.08f, r2, 1, sy2 }; }
                var (rr, rl, rShowL) = HandsOf(kit.Weapon, W, PSh);
                Arms(kit, Mr, rr, rl, col, prm);
                BodyHead(kit, Mr, col, prm);
                if (PSh != null) menB.Quad(men.Get(kit.Shield), Pose(Mr, PSh), col, neutral);
                if (W != null) menB.Quad(men.Get("weapon/" + kit.Weapon), Pose(Mr, W), col, neutral);
                Hands(kit, Mr, rr, rShowL ? rl : null, col);
                if (m.Atk && ap >= 0.42f && ap < 0.5f) sparks.Add((m.X, m.Y, m.Face, kit.Weapon == "lance" ? -2.6f : -0.9f, ap));
                return;
            }
            // стрелок: состояние лука — по времени до своего выстрела
            int bowSt = 0, xb = 0; float lean = 0;
            if (shoot && !m.Atk)
            {
                if (!float.IsNaN(m.Shot))
                {
                    float dt = m.Shot;
                    if (look == "bow") bowSt = dt < -0.45f ? 1 : dt < -0.2f ? 2 : dt < 0 ? 3 : dt < 0.22f ? 4 : 1;
                    else xb = dt < 0 ? 0 : dt < 0.25f ? 1 : 2;
                }
                else if (active) { bowSt = H(s, 14) < 0.5f ? 1 : 0; xb = H(s, 14) < 0.5f ? 0 : 2; }
                if (bowSt >= 2) rot -= 0.14f;
                // арбалет взводят через стремя (В13): нагнулся к стремени (2), тянет тетиву крюком к поясу (3) — и снова
                if (xb == 2) { xb = Frac(t * 0.45f + ph0) < 0.5f ? 2 : 3; lean = xb == 2 ? 0.1f : 0.04f; rot += 0.04f * Mathf.Sin(t * 4 + ph0 * 6); }
                if (xb == 1) oy += 0.04f;
            }
            // удар (В13): копья и пики колют; меч то рубит, то колет; топор, булава и дубина — то сбоку, то сверху;
            // каждый удар выбирается заново (хешем по номеру удара) — у соседей разный порядок
            string wk = m.Atk && shoot ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
            string kind = !m.Atk || wk == null ? null : Thrust(wk) ? "thrust"
                : wk == "sword" || wk == "falchion" ? (H(s * 7 + blow, 41) < 0.35f ? "thrust" : "swing") : H(s * 7 + blow, 42) < 0.5f ? "chop" : "swing";
            if (m.Atk && ap >= 0) { oy -= ap > 0.3f && ap < 0.55f ? 0.06f : 0; rot += kind == "swing" ? 0.18f * Mathf.Sin(ap * 6.283f) : kind == "chop" ? -0.08f * Mathf.Sin(ap * 6.283f) : 0; }
            if (cheer && !m.Atk) oy -= 0.05f * Mathf.Max(0, Mathf.Sin(t * 9 + ph0 * 6.283f));   // ликуют — подпрыгивают
            var Mm = bas.T(ox, oy - lean).R(rot);
            float st = step != 0 ? step : m.Atk ? Mathf.Sin(ap * 6.283f) * 0.6f : 0;   // ноги: на ходу и в бою шагают
            if (st != 0 && boots) { menB.Quad(bootP, Mm.T(-0.09f, 0.03f + 0.12f * st), col, neutral); menB.Quad(bootP, Mm.T(0.09f, 0.03f - 0.12f * st), col, neutral); }
            // щит: под стрелами — над головой; в рукопашной — вперёд, навстречу удару врага (прикрывается между своими ударами)
            bool raise = fireHere && !m.Atk && PSh != null && kit.ShieldShape != "buckler" && H(s, 13) < 0.85f;
            var Sh = PSh;
            if (Sh != null)
            {
                if (raise) Sh = new[] { -0.03f, -0.05f, -0.1f, 1, 0.9f };
                else if (m.Parry >= 0) { float c = 1 - m.Parry / 0.35f; c = c * c; Sh = new[] { Sh[0] + 0.07f * c, Sh[1] - 0.14f * c, Sh[2] + 0.4f * c, Sh[3], Sh[4] }; }   // Б2: принял удар
                else if (m.Atk) { float c = Mathf.Max(0, Mathf.Sin((ap + 0.5f) * 6.283f)); Sh = new[] { Sh[0] + 0.04f + 0.05f * c, Sh[1] - 0.06f - 0.09f * c, Sh[2] + 0.15f + 0.2f * c, Sh[3], Sh[4] }; }
            }
            // оружие
            string wpn = wk; var W2 = PW;
            if (m.Atk && shoot) W2 = wpn != null ? new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f } : null;
            if (m.Atk && wpn != null && W2 != null)
            {
                if (kind == "thrust") W2 = Thrust(wpn) ? new[] { W2[0], (wpn == "pike" ? 0 : -0.1f) + ThrustOff(ap), 0, 1, 1 } : new[] { 0.16f, -0.14f + 0.8f * ThrustOff(ap), 0.04f, 1, 1 };
                else if (kind == "chop") { var (y2, sy2) = ChopPose(ap); W2 = new[] { 0.2f, y2, 0.1f, 1, sy2 }; }
                else { var (r2, sy2) = SwingAng(ap); W2 = new[] { 0.22f, -0.08f, r2, 1, sy2 }; }
                if (ap >= 0.42f && ap < 0.5f) sparks.Add((m.X, m.Y, m.Face, kind == "thrust" ? (wpn == "pike" ? -3.7f : Thrust(wpn) ? -1.35f : -0.85f) : -0.75f, ap));
            }
            else if (cheer && W2 != null) W2 = new[] { W2[0], W2[1] - 0.05f, W2[2] * 0.3f - 0.1f, 1, Mathf.Min(W2[4], 0.3f) + 0.08f * Mathf.Sin(t * 9 + ph0 * 6.283f) };   // вскинули оружие
            else if (W2 != null && step != 0) W2 = new[] { W2[0], W2[1], W2[2] + 0.03f * step, W2[3], W2[4] };
            // руки (В15): предплечья под телом, кисти поверх оружия; стрелок в рукопашной держит запасное оружие одной рукой
            var (hr, hl, showL) = HandsOf(m.Atk && shoot ? wpn ?? "none" : kit.Weapon, wpn != null ? W2 : null, Sh, bowSt, xb);
            if (kit.Back != null) menB.Quad(men.Get(kit.Back), Mm, col, neutral);
            Arms(kit, Mm, hr, hl, col, prm);
            BodyHead(kit, Mm, col, prm);
            if (Sh != null) menB.Quad(men.Get(kit.Shield), Pose(Mm, Sh), col, neutral);
            if (wpn == "bow" && !m.Atk) menB.Quad(men.Get("bow/" + bowSt), Mm, col, neutral);
            else if (wpn == "crossbow" && !m.Atk) menB.Quad(men.Get("xbow/" + xb), Mm, col, neutral);
            else if (W2 != null && wpn != null) menB.Quad(men.Get("weapon/" + wpn), Pose(Mm, W2), col, neutral);
            Hands(kit, Mm, hr, showL ? hl : null, col);
        }
        // тело, сюрко (В15: у кольчуги и лат; цвет стороны без сдвига тона) и голова
        void BodyHead(Kit kit, Aff m, Color32 col, Vector4 prm)
        {
            menB.Quad(men.Get(kit.Body), m, col, prm);
            if (kit.Tabard != null) menB.Quad(men.Get(kit.Tabard), m, col, new Vector4(0, prm.y, prm.z, prm.w));
            menB.Quad(men.Get(kit.Head), m, col, prm);
        }

        // ── руки (В15), как handsOf полигона: правая — на рукояти; копьё, пика, вилы без щита — двумя руками (левая впереди
        // по древку); левая — за щитом (не видна), на луке или ложе арбалета; без всего — у бедра ──
        static readonly float[] BowD = { 0, 0.15f, 0.55f, 1, 0 };
        const float ElbX = 0.215f, ElbY = 0, FaLen = 0.3f;
        static Vector2 OnPose(float[] T, float x, float y)
        {
            float c = Mathf.Cos(T[2]), sn = Mathf.Sin(T[2]), px = x * T[3], py = y * T[4];
            return new Vector2(T[0] + px * c - py * sn, T[1] + px * sn + py * c);
        }
        static (Vector2? r, Vector2? l, bool showL) HandsOf(string weapon, float[] W, float[] Sh, int bowSt = 0, int xb = 0)
        {
            if (weapon == "bow")
            {
                float d = BowD[bowSt], ty = -0.2f - 0.02f * d, cy = -0.56f - 0.1f * d;
                return (new Vector2(0.02f, Mathf.Min(0.05f, ty + 0.36f * d + 0.03f)), new Vector2(0, (ty + cy) / 2), true);
            }
            if (weapon == "crossbow") return (xb >= 2 ? new Vector2(0.05f, xb == 2 ? -0.4f : -0.2f) : new Vector2(0.05f, -0.02f), new Vector2(0.05f, xb >= 2 ? -0.46f : -0.3f), true);
            Vector2? r = W != null ? new Vector2(W[0], W[1]) : (Vector2?)null;
            if (W != null && Sh == null && (weapon == "pike" || weapon == "spear" || weapon == "fork")) return (r, OnPose(W, 0, -0.4f), true);
            return (r, Sh != null ? new Vector2(Sh[0], Sh[1]) : new Vector2(-0.235f, -0.07f), Sh == null);
        }
        // предплечье — от локтя к кисти, растянуто по длине (в атласе — длиной FaLen)
        void Arms(Kit kit, Aff m, Vector2? r, Vector2? l, Color32 col, Vector4 prm)
        {
            var arm = men.Get(kit.Arm);
            for (int side = 1; side >= -1; side -= 2)
            {
                var h = side > 0 ? r : l; if (h == null) continue;
                float ex = side * ElbX, dx = h.Value.x - ex, dy = h.Value.y - ElbY, L = Mathf.Sqrt(dx * dx + dy * dy);
                if (L >= 0.02f) menB.Quad(arm, m.T(ex, ElbY).R(Mathf.Atan2(dx, -dy)).S(1, L / FaLen), col, prm);
            }
        }
        void Hands(Kit kit, Aff m, Vector2? r, Vector2? l, Color32 col)
        {
            var hand = men.Get(kit.Hand);
            if (r != null) menB.Quad(hand, m.T(r.Value.x, r.Value.y), col, Vector4.zero);
            if (l != null) menB.Quad(hand, m.T(l.Value.x, l.Value.y), col, Vector4.zero);
        }

        // ── павшие и кровь (В8, В13) ──
        static readonly Color32 Blood = new Color32(123, 18, 18, 255);
        void DrawDead(double t, int f0, Color32[] unitCol, Func<float, float, float, bool> In)
        {
            var solid = new Vector4(0, 0, 1, 0); var disc = men.Get("util/disc");
            for (int i = 0; i < rec.Dead.Count; i++)
            {
                var dd = rec.Dead[i];
                if (dd.Frame > f0 + 1 || dd.Frame == f0 + 1 && !(dd.T <= t) || !In(dd.X, dd.Y, 3)) continue;   // Б2: лежит с мига удара
                int seed = dd.Frame * 131 + dd.Unit * 7919 + Mathf.RoundToInt(dd.X * 13) + Mathf.RoundToInt(dd.Y * 7);
                float age = (float)(t - dd.T), grow = Mathf.Min(1, 0.25f + age / 1.2f), a = dd.Dir * Mathf.Deg2Rad;
                float big = dd.Part == 3 ? 1.7f : dd.Part == 0 ? 0.65f : dd.Part == 2 ? 0.85f : 1;
                for (int k = 0; k < 3; k++)
                {
                    float r = (0.15f + 0.13f * H(seed, k)) * big * grow, off = (0.3f + 0.35f * H(seed, k + 7)) * grow;
                    float ox = Mathf.Cos(a) * off + (H(seed, k + 3) - 0.5f) * 0.3f, oy = Mathf.Sin(a) * off + (H(seed, k + 5) - 0.5f) * 0.3f;
                    decals.Quad(disc, Aff.At(dd.X + ox, dd.Y + oy).R(a).S(2 * r, 2 * r * (0.65f + 0.35f * H(seed, k + 9))), Blood, solid);
                }
                // павший с известным номером — в своём комплекте (тот же выбор, что у живого бойца)
                var u = kits[dd.Unit]; var kit = dd.Man > 0 ? u[(int)(H(rec.Units[dd.Unit].Id * 7919 + dd.Man, 4) * u.Length)] : u[(int)(H(seed, 50) * u.Length)];
                var col = unitCol[dd.Unit];
                bool rider = dd.Part == 3 && kit.Horse, wounded = !dd.Killed && !rider;
                var live = new Vector4(kit.ClothKey == "f" ? kit.Tone : 0, 0, 0, 0);
                // удар (В13): первые 0,15 с боец ещё стоит — его качнуло по удару; потом падает
                if (age < 0.15f && !rider)
                {
                    float k2 = age / 0.15f, d = dd.Dir * Mathf.Deg2Rad;
                    var sm = Aff.At(dd.X + Mathf.Cos(d) * 0.12f * k2, dd.Y + Mathf.Sin(d) * 0.12f * k2).R(dd.Facing * Mathf.Deg2Rad + (H(seed, 55) - 0.5f) * 0.6f * k2);
                    var (W, Sh) = Rest(kit, 0, 0);
                    if (kit.Back != null) menB.Quad(men.Get(kit.Back), sm, col, Vector4.zero);
                    BodyHead(kit, sm, col, live);
                    if (Sh != null) menB.Quad(men.Get(kit.Shield), Pose(sm, Sh), col, Vector4.zero);
                    if (kit.Weapon == "bow") menB.Quad(men.Get("bow/0"), sm, col, Vector4.zero);
                    else if (kit.Weapon == "crossbow") menB.Quad(men.Get("xbow/0"), sm, col, Vector4.zero);
                    else if (W != null) menB.Quad(men.Get("weapon/" + kit.Weapon), Pose(sm, W), col, Vector4.zero);
                    continue;
                }
                int v = (int)(H(seed, 51) * 2);
                float p = Mathf.Min(1, (age - 0.15f) / 0.35f), e = 1 - (1 - p) * (1 - p);
                var dp = new Vector4(kit.ClothKey == "f" ? kit.Tone : 0, wounded ? 0 : 0.38f, 0, 0);   // раненый — краски живые
                var fm = Aff.At(dd.X, dd.Y).R(a + Mathf.PI / 2 + (H(seed, 52) - 0.5f) * 0.5f);
                if (rider)
                {
                    // убитый конь лежит; всадник слетает с него (В13): за 0,55 с — в сторону от туши, в полёте крупнее
                    float he = 1 - Mathf.Pow(1 - Mathf.Min(1, age / 0.5f), 2);
                    corpses.Quad(dead.Get($"deadhorse/{kit.Coat}/{kit.Bard}"), fm.S(1, 0.4f + 0.6f * he).T(0, -0.6f), col, new Vector4(0, 0.38f, 0, 0));
                    float fl = Mathf.Min(1, age / 0.55f), k3 = 1 + 0.3f * Mathf.Sin(fl * Mathf.PI);
                    fm = fm.T(-0.9f * Ease(fl), 0.3f * Ease(fl)).S(k3, k3);
                }
                float flip = 1;
                if (wounded)
                {
                    // раненый (Г39, В13) бросил оружие и щит, где упал; ползёт рывками прочь от врага (0,32 м за 0,9 с) с кровавым
                    // следом — или корчится на месте; через 4–12 с затихает
                    string w = kit.Weapon == "bow" || kit.Weapon == "crossbow" ? kit.Side : kit.Weapon;
                    if (w != null && w != "none")
                    {
                        float sc = w == "pike" || w == "lance" ? 0.4f : w == "spear" || w == "fork" ? 0.7f : 0.9f;
                        deadTop.Quad(men.Get("weapon/" + w), fm.T(0.45f, -0.5f).R(H(seed, 56) * 6.283f).S(sc, sc), col, dp);
                    }
                    if (kit.Shield != null) deadTop.Quad(men.Get(kit.Shield), fm.T(-0.5f, -0.4f).R(H(seed, 57) * 6.283f).S(0.85f, 0.85f), col, dp);
                    float life = Mathf.Min(Mathf.Max(0, age - 0.5f), 4 + 8 * H(seed, 54));
                    if (H(seed, 53) < 0.65f)
                    {
                        float nn = life / 0.9f; int j = Mathf.FloorToInt(nn);
                        var drop = new Color32(123, 18, 18, 178);
                        for (int qd = 0; qd < j && qd < 16; qd++) { float r = 0.035f + 0.03f * H(seed, 80 + qd); decals.Quad(disc, fm.T((H(seed, 60 + qd) - 0.5f) * 0.14f, -0.32f * qd - 0.25f).S(2 * r, 2 * r), drop, solid); }
                        fm = fm.T(0, -0.32f * (j + Ease(nn - j))); flip = j % 2 == 1 ? -1 : 1;
                    }
                    else { fm = fm.R(0.12f * Mathf.Sin(life * 2.3f + seed)); flip = Mathf.FloorToInt(life / 1.6f) % 2 == 1 ? -1 : 1; }
                }
                // падение: тело «ложится» от ног — голова туда, куда толкнул удар
                var cm = fm.S(flip, 0.25f + 0.75f * e).T(0, -0.8f);
                corpses.Quad(dead.Get(wounded ? $"crawl/{kit.ClothKey}/{kit.ArmourKey}" : $"corpse/{kit.ClothKey}/{kit.ArmourKey}/{v}"), cm, col, dp);
                deadTop.Quad(men.Get(kit.Head), cm.T(0, -0.56f), col, dp);
                if (!wounded)
                {
                    string w = kit.Weapon == "bow" || kit.Weapon == "crossbow" ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
                    if (w != null)
                    {
                        float sc = w == "pike" || w == "lance" ? 0.4f : w == "spear" || w == "fork" ? 0.7f : 0.9f;
                        deadTop.Quad(men.Get("weapon/" + w), cm.T(0.45f + 0.15f * H(v * 131 + 7, 5), -0.25f + 0.5f * H(v * 131 + 7, 6)).R(H(v * 131 + 7, 7) * 6.283f).S(sc, sc), col, dp);
                    }
                    if (kit.Shield != null) deadTop.Quad(men.Get(kit.Shield), cm.T(-0.48f - 0.15f * H(v * 131 + 7, 8), -0.1f + 0.4f * H(v * 131 + 7, 9)).R(H(v * 131 + 7, 10) * 6.283f).S(0.85f, 0.85f), col, dp);
                    if (dd.Part == 0 && p >= 1) decals.Quad(disc, cm.T(0, -0.56f).S(0.2f, 0.2f), new Color32(123, 18, 18, 204), solid);
                }
            }
        }

        // брошенное на бегу (В11): кто бросил оружие (почти половина), — оно лежит там, где отряд побежал
        void DrawDropped(int f0, Color32[] unitCol, Func<float, float, float, bool> In)
        {
            var dp = new Vector4(0, 0.38f, 0, 0);
            for (int ui = 0; ui < rec.Units.Count; ui++)
            {
                var L = rec.States?[ui]; if (L == null || L.Count < 4) continue;
                var info = rec.Units[ui]; var ks = kits[ui];
                for (int i = 2; i + 1 < L.Count && L[i] <= f0; i += 2)
                {
                    bool fl = L[i + 1] == 1 || L[i + 1] == 3, was = L[i - 1] == 1 || L[i - 1] == 3;
                    if (!fl || was || L[i] >= rec.Men.Count) continue;   // начало бегства
                    var mf = rec.Men[L[i]][ui]; int n = mf.Xyh.Length / 3;
                    for (int id = 1; id < n; id++)
                    {
                        float x = mf.Xyh[3 * id]; if (float.IsNaN(x)) continue;
                        float y = mf.Xyh[3 * id + 1]; int seed = info.Id * 7919 + id;
                        if (!Drops(seed) || !In(x, y, 3)) continue;
                        var kit = ks[(int)(H(seed, 4) * ks.Length)];
                        string w = kit.Weapon == "bow" || kit.Weapon == "crossbow" ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
                        var at = Aff.At(x, y).R(H(seed, 22) * 6.283f);
                        if (w != null) deadTop.Quad(men.Get("weapon/" + w), at.S(0.9f, w == "pike" ? 0.5f : 0.9f), unitCol[ui], dp);
                        else if (kit.Weapon == "bow") deadTop.Quad(men.Get("bow/0"), at, unitCol[ui], dp);
                    }
                }
            }
        }

        // ── стрелы: упавшие — торчат, в полёте — дугой с тенью ──
        void DrawArrows(float t, Func<float, float, float, bool> In, float ppm)
        {
            var px = men.Get("util/px"); var solid = new Vector4(0, 0, 1, 0);
            var shaft = new Color32(42, 31, 22, 255); var fletch = new Color32(232, 226, 210, 255); var shade = new Color32(20, 16, 10, 56);
            float thin = Mathf.Max(1, 0.9f / ppm / 0.035f);   // древко не тоньше ~1 px: вдали иначе стрел не видно
            if (ppm >= 2.5f)
                foreach (var ar in rec.Arrows)
                {
                    if (ar.T1 > t || ar.End > 2 || !In(ar.X1, ar.Y1, 1)) continue;
                    float ang = Mathf.Atan2(ar.Y1 - ar.Y0, ar.X1 - ar.X0), L = ar.End == 1 ? 0.3f : ar.End == 2 ? 0.7f : 0.4f;
                    float x = ar.X1, y = ar.Y1;
                    if (ar.End == 2) { int s = (int)(ar.T0 * 1000); x += (H(s, 1) - 0.5f) * 0.7f; y += (H(s, 2) - 0.5f) * 0.7f; ang += (H(s, 3) - 0.5f) * 2.5f; }
                    var m = Aff.At(x - Mathf.Cos(ang) * L / 2, y - Mathf.Sin(ang) * L / 2).R(ang + Mathf.PI / 2);
                    deadTop.Quad(px, m.S(0.35f * thin, L * 10), shaft, solid);
                }
            foreach (var ar in rec.Arrows)
            {
                if (ar.T0 > t || ar.T1 <= t) continue;   // в живой записи стрелы идут не по порядку вылета
                if (!ArrowAt(ar, t, out var x, out var y, out var z, out var ang, out var pitch) || !In(x, y, 3)) continue;
                float L = 0.8f * Mathf.Max(0.25f, Mathf.Cos(pitch)) * (1 + Mathf.Max(0, z) * 0.012f);
                var m = Aff.At(x, y).R(ang + Mathf.PI / 2);
                float sx = -0.6f * z * 0.55f, sy = 0.8f * z * 0.55f;
                air.Quad(px, Aff.At(x + sx, y + sy).R(ang + Mathf.PI / 2).S(0.5f * thin, L * 10), shade, solid);
                air.Quad(px, m.S(0.45f * thin, L * 10), shaft, solid);
                if (ppm >= 8) air.Quad(px, m.T(0, L * 0.38f).S(0.9f, 1.4f), fletch, solid);
            }
        }

        // Полёт стрелы: конец известен — по краям из движка (высота — парабола с той же скоростью вверх, путь по земле —
        // с замедлением); ещё летит (живая запись, T1 = ∞) — бросок от вылета, пока движок не допишет конец (не дольше 10 с)
        public static bool ArrowAt(ArrowRec a, float t, out float x, out float y, out float z, out float ang, out float pitch)
        {
            if (float.IsInfinity(a.T1))
            {
                float tt = t - a.T0;
                x = a.X0 + a.VX * tt; y = a.Y0 + a.VY * tt; z = a.Z0 + a.VZ * tt - 4.905f * tt * tt;
                ang = Mathf.Atan2(a.VY, a.VX); pitch = Mathf.Atan2(a.VZ - 9.81f * tt, Mathf.Sqrt(a.VX * a.VX + a.VY * a.VY));
                return tt >= 0 && tt < 10 && z > -0.5f;
            }
            float tau = Mathf.Max(1e-3f, a.T1 - a.T0), u = Mathf.Clamp01((t - a.T0) / tau), dx = a.X1 - a.X0, dy = a.Y1 - a.Y0, D = Mathf.Sqrt(dx * dx + dy * dy), vh = Mathf.Sqrt(a.VX * a.VX + a.VY * a.VY);
            float kk = D > 0.01f ? Mathf.Clamp(vh * tau / D, 1, 2) : 1, s = kk * u + (1 - kk) * u * u, t2 = u * tau;
            float gz = 2 * (a.Z0 + a.VZ * tau - a.Z1) / (tau * tau);
            z = a.Z0 + a.VZ * t2 - 0.5f * gz * t2 * t2;
            float vz = a.VZ - gz * t2, vhh = D / tau * (kk + 2 * (1 - kk) * u);
            x = a.X0 + dx * s; y = a.Y0 + dy * s;
            ang = D > 0.01f ? Mathf.Atan2(dy, dx) : Mathf.Atan2(a.VY, a.VX); pitch = Mathf.Atan2(vz, vhh);
            return true;
        }
    }
}
