// ═══════════ MenView.cs — бойцы смотрелки из частей рисунка полигона (В5–В9) ═══════════
// Каждый кадр: бойцы фигурок — сеткой по местам в строю (как полигон до В10), у каждого — комплект снаряжения отряда;
// боец собран из частей атласа (поклажа, тело, голова, щит, оружие; конь и всадник), позы — по шеренге (копья опущены
// в первых двух, пики — в первых четырёх). Павшие лежат головой по удару, рядом — их оружие, под ними — кровь.
// Стрелы летят дугой по данным движка, тень на земле отходит с высотой. Всё рисуется сетками с шейдером Men.
// Оси: как в полигоне — метры карты, y вниз; курс h — поворот canvas; в мир Unity: X = x, Y = −y.
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
        Kit[][] kits;
        Recording rec;
        public bool Ok => men != null && horses != null && dead != null;

        static Mesh NewMesh() { var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.MarkDynamic(); return m; }

        public MenView(Transform parent)
        {
            men = ArtAtlas.Load("men"); horses = ArtAtlas.Load("horses"); dead = ArtAtlas.Load("dead");
            if (!Ok) return;
            var sh = Shader.Find("Journal/Men");
            menMat = new Material(sh) { mainTexture = men.Tex };
            horseMat = new Material(sh) { mainTexture = horses.Tex };
            deadMat = new Material(sh) { mainTexture = dead.Tex };
            // слои снизу вверх: кровь → павшие → их головы, оружие, щиты и упавшие стрелы → кони → бойцы → стрелы в воздухе
            Layer(parent, "Кровь", decalMesh, menMat, 4);
            Layer(parent, "Павшие", deadMesh, deadMat, 5);
            Layer(parent, "Оружие павших и стрелы на земле", deadTopMesh, menMat, 6);
            Layer(parent, "Кони", horseMesh, horseMat, 9);
            Layer(parent, "Бойцы", menMesh, menMat, 10);
            Layer(parent, "Стрелы в полёте", airMesh, menMat, 20);
        }
        static void Layer(Transform parent, string name, Mesh mesh, Material mat, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = order;
        }

        public void SetRecording(Recording r)
        {
            rec = r;
            kits = new Kit[r.Units.Count][];
            for (int i = 0; i < r.Units.Count; i++) kits[i] = Kits.Of(r.Units[i].Id, Kits.LookOf(r.Units[i].Tpl, r.Units[i].Type));
        }

        // ── сетка из четырёхугольников ──
        sealed class Batch
        {
            public readonly List<Vector3> V = new List<Vector3>(); public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> U = new List<Vector2>(); public readonly List<Vector4> P = new List<Vector4>(); public readonly List<int> I = new List<int>();
            public void Clear() { V.Clear(); C.Clear(); U.Clear(); P.Clear(); I.Clear(); }
            public void Quad(Part p, Aff m, Color32 col, Vector4 prm)
            {
                if (!p.Ok) return;
                int b = V.Count;
                Add(m, p.X0, p.Y0); Add(m, p.X1, p.Y0); Add(m, p.X1, p.Y1); Add(m, p.X0, p.Y1);
                U.Add(new Vector2(p.U0, p.V1)); U.Add(new Vector2(p.U1, p.V1)); U.Add(new Vector2(p.U1, p.V0)); U.Add(new Vector2(p.U0, p.V0));
                for (int k = 0; k < 4; k++) { C.Add(col); P.Add(prm); }
                I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3);
            }
            void Add(Aff m, float x, float y) => V.Add(new Vector3(m.a * x + m.c * y + m.e, -(m.b * x + m.d * y + m.f), 0));
            public void To(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(V); mesh.SetColors(C); mesh.SetUVs(0, U); mesh.SetUVs(1, P); mesh.SetTriangles(I, 0);
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
            }
        }

        // поза в покое: где оружие и щит, [x, y, поворот, масштаб поперёк, вдоль] — как restPose полигона
        static readonly float[] None = null;
        static (float[] W, float[] Sh) Rest(Kit k, int rank)
        {
            float[] W = None, Sh = None;
            string w = k.Weapon;
            if (w == "spear" || w == "fork") W = rank < 2 ? new[] { 0.2f, -0.1f, 0, 1, 1 } : new[] { 0.21f, -0.06f, 0.15f, 1, 0.2f };
            else if (w == "pike") W = rank < 4 ? new[] { rank % 2 == 1 ? -0.3f : 0.12f, 0, 0, 1, 1 } : new[] { 0.15f, -0.04f, 0.1f, 1, 0.12f };
            else if (w == "lance") W = new[] { 0.22f, 0.05f, 0.05f, 1, 0.24f };
            else if (w != "bow" && w != "crossbow") W = new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f };
            if (k.Shield != null) Sh = k.Horse ? new[] { -0.27f, 0, Mathf.PI / 2, 0.75f, 0.42f } : k.ShieldShape == "buckler" ? new[] { -0.2f, 0.07f, 0, 1, 0.6f } : new[] { -0.16f, -0.23f, -0.25f, 1, -0.42f };
            return (W, Sh);
        }
        static Aff Pose(Aff m, float[] p) => m.P(p[0], p[1], p[2], p[3], p[4]);
        static float H(int a, int b) => (float)Kits.Hash(a, b);

        public void Hide() { foreach (var m in new[] { decalMesh, deadMesh, deadTopMesh, horseMesh, menMesh, airMesh }) m.Clear(); }

        // ── кадр ──
        public void Draw(double t, Color32[] unitCol, Rect view, float ppm)
        {
            if (!Ok || rec == null) return;
            decals.Clear(); corpses.Clear(); deadTop.Clear(); horseB.Clear(); menB.Clear(); air.Clear();
            double ft = t / rec.Dt; int f0 = Math.Min((int)Math.Floor(ft), rec.Frames.Count - 1), f1 = Math.Min(f0 + 1, rec.Frames.Count - 1); float q = (float)(ft - f0);
            var noP = Vector4.zero;
            bool In(float x, float y, float pad) => x > view.xMin - pad && x < view.xMax + pad && y > view.yMin - pad && y < view.yMax + pad;
            // ── павшие и кровь ──
            var blood = new Color32(123, 18, 18, 255); var solid = new Vector4(0, 0, 1, 0);
            var disc = men.Get("util/disc");
            for (int i = 0; i < rec.Dead.Count; i++)
            {
                var dd = rec.Dead[i];
                if (dd.Frame > f0 || !In(dd.X, dd.Y, 3)) continue;
                int seed = dd.Frame * 131 + dd.Unit * 7919 + Mathf.RoundToInt(dd.X * 13) + Mathf.RoundToInt(dd.Y * 7);
                float since = (float)(t - dd.Frame * rec.Dt), grow = Mathf.Min(1, 0.25f + since / 1.2f), a = dd.Dir * Mathf.Deg2Rad;
                float big = dd.Part == 3 ? 1.7f : dd.Part == 0 ? 0.65f : dd.Part == 2 ? 0.85f : 1;
                for (int k = 0; k < 3; k++)
                {
                    float r = (0.15f + 0.13f * H(seed, k)) * big * grow, off = (0.3f + 0.35f * H(seed, k + 7)) * grow;
                    float ox = Mathf.Cos(a) * off + (H(seed, k + 3) - 0.5f) * 0.3f, oy = Mathf.Sin(a) * off + (H(seed, k + 5) - 0.5f) * 0.3f;
                    decals.Quad(disc, Aff.At(dd.X + ox, dd.Y + oy).R(a).S(2 * r, 2 * r * (0.65f + 0.35f * H(seed, k + 9))), blood, solid);
                }
                // павший с известным номером — в своём комплекте (тот же выбор, что у живого бойца)
                var u = kits[dd.Unit]; var kit = dd.Man > 0 ? u[(int)(H(rec.Units[dd.Unit].Id * 7919 + dd.Man, 4) * u.Length)] : u[(int)(H(seed, 50) * u.Length)];
                int v = (int)(H(seed, 51) * 2);
                float fall = Mathf.Min(1, since / 0.35f), e = 1 - (1 - fall) * (1 - fall);
                // тело ложится от ног: голова — туда, куда толкнул удар
                var cm = Aff.At(dd.X, dd.Y).R(a + Mathf.PI / 2 + (H(seed, 52) - 0.5f) * 0.5f).S(1, 0.25f + 0.75f * e).T(0, -0.8f);
                var dp = new Vector4(kit.ClothKey == "f" ? kit.Tone : 0, 0.38f, 0, 0);
                var col = unitCol[dd.Unit];
                corpses.Quad(dead.Get($"corpse/{kit.ClothKey}/{kit.ArmourKey}/{v}"), cm, col, dp);
                var hm = cm.T(0, -0.56f);
                deadTop.Quad(men.Get(kit.Head), hm, col, dp);
                string w = kit.Weapon == "bow" || kit.Weapon == "crossbow" ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
                if (w != null)
                {
                    float sc = w == "pike" || w == "lance" ? 0.4f : w == "spear" || w == "fork" ? 0.7f : 0.9f;
                    deadTop.Quad(men.Get("weapon/" + w), cm.T(0.45f + 0.15f * H(v * 131 + 7, 5), -0.25f + 0.5f * H(v * 131 + 7, 6)).R(H(v * 131 + 7, 7) * 6.283f).S(sc, sc), col, dp);
                }
                if (kit.Shield != null) deadTop.Quad(men.Get(kit.Shield), cm.T(-0.48f - 0.15f * H(v * 131 + 7, 8), -0.1f + 0.4f * H(v * 131 + 7, 9)).R(H(v * 131 + 7, 10) * 6.283f).S(0.85f, 0.85f), col, dp);
            }
            // ── бойцы ──
            var A = rec.Frames[f0]; var B = rec.Frames[f1];
            var shadow = new Color32(24, 18, 8, 80); var soft = men.Get("util/soft");
            float t32 = (float)t;
            for (int ui = 0; ui < A.Length; ui++)
            {
                var info = rec.Units[ui]; var a = A[ui]; var b = B[ui];
                int st = rec.StateAt(ui, f0); if (st == 2) continue;
                var ks = kits[ui]; var col = unitCol[ui];
                float pm = (float)info.PerMan, rd = (float)info.RankDepth;
                int fd = 1; foreach (var fg in info.Figs) fd = Math.Max(fd, Mathf.RoundToInt((float)fg[1] / rd));
                float h0 = a[2], dh = Mathf.DeltaAngle(a[2], b[2]);
                // живые бойцы движка (Г75): у каждого своё место, курс и номер; комплект — по номеру
                if (f0 < rec.Men.Count && DrawEngineMen(ui, rec.Men[f0][ui], rec.Men[Math.Min(f1, rec.Men.Count - 1)][ui], q, ks, col, fd, In, soft, shadow, ppm, t32)) continue;
                for (int k = 0; 5 + 2 * k < a.Length; k++)
                {
                    float fx = a[4 + 2 * k], fy = a[5 + 2 * k];
                    if (float.IsNaN(fx)) continue;
                    float bx = 5 + 2 * k < b.Length ? b[4 + 2 * k] : float.NaN, by = 5 + 2 * k < b.Length ? b[5 + 2 * k] : float.NaN;
                    bool moving = false;
                    if (!float.IsNaN(bx)) { moving = (bx - fx) * (bx - fx) + (by - fy) * (by - fy) > 0.01f; fx += (bx - fx) * q; fy += (by - fy) * q; }
                    var fig = k < info.Figs.Count ? info.Figs[k] : info.Figs[0];
                    float w = (float)fig[0], d = (float)fig[1];
                    if (!In(fx, fy, Mathf.Max(w, d) + 5)) continue;
                    float head = (rec.Heads[f0].TryGetValue(ui * 65536 + k, out var hd) ? hd : h0 + dh * q) * Mathf.Deg2Rad;
                    var fm = Aff.At(fx, fy).R(head);
                    int cols = Math.Max(1, Mathf.RoundToInt(w / pm)), rows = Math.Max(1, Mathf.RoundToInt(d / rd));
                    int left = Mathf.RoundToInt((float)fig[2]);
                    for (int j = 0; j < rows && left > 0; j++)
                    {
                        int n = Math.Min(cols, left); float off = (cols - n) / 2f; left -= n;
                        int rank = (int)fig[3] * fd + j;
                        for (int i = 0; i < n; i++)
                        {
                            int seed = info.Id * 7919 + k * 64 + j * 8 + i;
                            float x = -w / 2 + (off + i + 0.5f) * pm + (H(seed, 1) - 0.5f) * 0.12f * pm, y = -d / 2 + (j + 0.5f) * rd + (H(seed, 2) - 0.5f) * 0.12f * rd;
                            float ph = H(seed, 5);
                            if (moving) { float s = Mathf.Sin((t32 * 2f + ph) * 6.283f); y += s * 0.05f; }
                            var kit = ks[(int)(H(seed, 4) * ks.Length)];
                            DrawMan(kit, fm.T(x, y), rank, moving, t32, ph, col, soft, shadow, ppm);
                        }
                    }
                }
            }
            // ── стрелы: упавшие — торчат, в полёте — дугой с тенью ──
            var px = men.Get("util/px");
            var shaft = new Color32(42, 31, 22, 255); var fletch = new Color32(232, 226, 210, 255); var shade = new Color32(20, 16, 10, 56);
            float thin = Mathf.Max(1, 0.9f / ppm / 0.035f);   // древко не тоньше ~1 px: вдали иначе стрел не видно
            if (ppm >= 2.5f)
                foreach (var ar in rec.Arrows)
                {
                    if (ar.T1 > t) continue;
                    if (ar.End > 2 || !In(ar.X1, ar.Y1, 1)) continue;
                    float ang = Mathf.Atan2(ar.Y1 - ar.Y0, ar.X1 - ar.X0), L = ar.End == 1 ? 0.3f : ar.End == 2 ? 0.7f : 0.4f;
                    float x = ar.X1, y = ar.Y1;
                    if (ar.End == 2) { int s = (int)(ar.T0 * 1000); x += (H(s, 1) - 0.5f) * 0.7f; y += (H(s, 2) - 0.5f) * 0.7f; ang += (H(s, 3) - 0.5f) * 2.5f; }
                    var m = Aff.At(x - Mathf.Cos(ang) * L / 2, y - Mathf.Sin(ang) * L / 2).R(ang + Mathf.PI / 2);
                    deadTop.Quad(px, m.S(0.35f * thin, L * 10), shaft, solid);
                }
            foreach (var ar in rec.Arrows)
            {
                if (ar.T0 > t || ar.T1 <= t) continue;   // в живой записи стрелы идут не по порядку вылета
                var (x, y, z, ang, pitch) = ArrowAt(ar, (float)t);
                if (!In(x, y, 3)) continue;
                float L = 0.8f * Mathf.Max(0.25f, Mathf.Cos(pitch)) * (1 + Mathf.Max(0, z) * 0.012f);
                var m = Aff.At(x, y).R(ang + Mathf.PI / 2);
                float sx = -0.6f * z * 0.55f, sy = 0.8f * z * 0.55f;
                air.Quad(px, Aff.At(x + sx, y + sy).R(ang + Mathf.PI / 2).S(0.5f * thin, L * 10), shade, solid);
                air.Quad(px, m.S(0.45f * thin, L * 10), shaft, solid);
                if (ppm >= 8) air.Quad(px, m.T(0, L * 0.38f).S(0.9f, 1.4f), fletch, solid);
            }
            decals.To(decalMesh); corpses.To(deadMesh); deadTop.To(deadTopMesh); horseB.To(horseMesh); menB.To(menMesh); air.To(airMesh);
        }

        bool DrawEngineMen(int ui, MenFrame m0, MenFrame m1, float q, Kit[] ks, Color32 col, int fd, Func<float, float, float, bool> In, Part soft, Color32 shadow, float ppm, float t)
        {
            var info = rec.Units[ui]; bool any = false;
            int n = m0.Xyh.Length / 3;
            for (int id = 1; id < n; id++)
            {
                float x = m0.Xyh[3 * id];
                if (float.IsNaN(x)) continue;
                any = true;
                float y = m0.Xyh[3 * id + 1], h = m0.Xyh[3 * id + 2];
                bool moving = false;
                if (3 * id + 2 < m1.Xyh.Length && !float.IsNaN(m1.Xyh[3 * id]))
                {
                    float x1 = m1.Xyh[3 * id], y1 = m1.Xyh[3 * id + 1];
                    moving = (x1 - x) * (x1 - x) + (y1 - y) * (y1 - y) > 0.0036f;   // быстрее 0,3 м/с
                    x += (x1 - x) * q; y += (y1 - y) * q; h += Mathf.DeltaAngle(h, m1.Xyh[3 * id + 2]) * q;
                }
                if (!In(x, y, 6)) continue;
                int fig = m0.Fig[id], rank = (fig < info.Figs.Count ? (int)info.Figs[fig][3] : 0) * fd + m0.Row[id];
                int seed = info.Id * 7919 + id;
                var kit = ks[(int)(H(seed, 4) * ks.Length)];
                var m = Aff.At(x, y).R(h * Mathf.Deg2Rad);
                if (moving) m = m.T(0, Mathf.Sin((t * 2f + H(seed, 5)) * 6.283f) * 0.04f);
                DrawMan(kit, m, rank, moving, t, H(seed, 5), col, soft, shadow, ppm);
            }
            return any;
        }

        void DrawMan(Kit kit, Aff m, int rank, bool moving, float t, float ph, Color32 col, Part soft, Color32 shadow, float ppm)
        {
            var prm = new Vector4(kit.ClothKey == "f" ? kit.Tone : 0, 0, 0, 0); var neutral = Vector4.zero;
            var (W, Sh) = Rest(kit, rank);
            // мягкая тень влево вниз (свет справа сверху)
            if (ppm >= 8)
            {
                float so = kit.Horse ? 0.45f : 0.2f;
                var sm = Aff.At(m.e - 0.6f * so, m.f + 0.8f * so); sm.a = m.a; sm.b = m.b; sm.c = m.c; sm.d = m.d;
                (kit.Horse ? horseB : menB).Quad(soft, sm.T(0, kit.Horse ? 0.12f : 0.02f).S(0.66f, kit.Horse ? 2.0f : 0.48f), shadow, new Vector4(0, 0, 1, 0));
            }
            if (kit.Horse)
            {
                int frame = moving ? (int)((t * 2.4f + ph) % 1f * 4) : -1;
                horseB.Quad(horses.Get($"{kit.HorseKey}/{frame}"), m, col, neutral);
                var rm = m.T(0, 0.02f + (moving ? Mathf.Sin((t * 2.4f + ph) * 12.566f) * 0.03f : 0));
                menB.Quad(men.Get(kit.Body), rm, col, prm);
                menB.Quad(men.Get(kit.Head), rm, col, prm);
                if (Sh != null) menB.Quad(men.Get(kit.Shield), Pose(rm, Sh), col, neutral);
                float[] lw = kit.Weapon == "lance" && moving ? new[] { 0.2f, 0.25f, -0.04f, 1, 1 } : W;
                if (lw != null) menB.Quad(men.Get("weapon/" + kit.Weapon), Pose(rm, lw), col, neutral);
                return;
            }
            if (kit.Back != null) menB.Quad(men.Get(kit.Back), m, col, neutral);
            menB.Quad(men.Get(kit.Body), m, col, prm);
            menB.Quad(men.Get(kit.Head), m, col, prm);
            if (Sh != null) menB.Quad(men.Get(kit.Shield), Pose(m, Sh), col, neutral);
            if (kit.Weapon == "bow") menB.Quad(men.Get("bow/0"), m, col, neutral);
            else if (kit.Weapon == "crossbow") menB.Quad(men.Get("xbow/0"), m, col, neutral);
            else if (W != null) menB.Quad(men.Get("weapon/" + kit.Weapon), Pose(m, W), col, neutral);
        }

        // полёт стрелы по краям из движка: высота — парабола с той же скоростью вверх, путь по земле — с замедлением
        static (float x, float y, float z, float ang, float pitch) ArrowAt(ArrowRec a, float t)
        {
            float tau = Mathf.Max(1e-3f, a.T1 - a.T0), u = Mathf.Clamp01((t - a.T0) / tau), dx = a.X1 - a.X0, dy = a.Y1 - a.Y0, D = Mathf.Sqrt(dx * dx + dy * dy), vh = Mathf.Sqrt(a.VX * a.VX + a.VY * a.VY);
            float kk = D > 0.01f ? Mathf.Clamp(vh * tau / D, 1, 2) : 1, s = kk * u + (1 - kk) * u * u, tt = u * tau;
            float gz = 2 * (a.Z0 + a.VZ * tau - a.Z1) / (tau * tau), z = a.Z0 + a.VZ * tt - 0.5f * gz * tt * tt;
            float vz = a.VZ - gz * tt, vhh = D / tau * (kk + 2 * (1 - kk) * u);
            return (a.X0 + dx * s, a.Y0 + dy * s, z, D > 0.01f ? Mathf.Atan2(dy, dx) : Mathf.Atan2(a.VY, a.VX), Mathf.Atan2(vz, vhh));
        }
    }
}
