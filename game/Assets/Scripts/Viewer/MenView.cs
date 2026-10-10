// ═══════════ MenView.cs — бойцы смотрелки из частей рисунка (В5–В13, облик В18) ═══════════
// Облик — как у Iron Kings, строго сверху и плоско (В18): перенос fMan / fCav пробы core/Tests/polygon-men-flat.js,
// части — из её атласов (atlas-export.js): меняется проба — меняем и здесь.
// Боец собран из частей атласа (поклажа, предплечья, тело-капсула, щит ребром, голова или шлем, оружие, кисти; конь —
// из ног, хвоста, шеи с головой, туловища и попоны), у каждого — комплект снаряжения отряда своего стиля (В16) по своему
// номеру. Цвет стороны в атласе — пурпурный, его красит шейдер Men. Каждый кадр по данным записи решается, что он делает:
// - рукопашная: передние у врага бьют (колют, рубят сбоку или сверху — каждый удар заново), щит идёт навстречу удару,
//   в миг удара у острия вспышка; задние напирают и поворачиваются к врагу;
// - стрелки: лук натягивают перед своей стрелой и отпускают в миг вылета; арбалет взводят через стремя;
// - под стрелами, что вот-вот упадут, поднимают щиты; древки (копья, пики) к бою опускают у врага, передние первыми;
// - бегство: бегом, щит за спиной, оглядываются, почти половина бросила оружие — оно лежит, где побежали;
//   сплотившиеся первые 4 с вскидывают оружие и подпрыгивают;
// - ноги и шаг — по фазе, набранной по пути (не скользят); конь — аллюр по скорости, стоящий переступает.
// Сглаживание (Б-вид): движок двигает бойцов рывками — выпады, расталкивание телами, у коней по полметра за кадр; рисуем
// бойца не там, где он в кадре, а там, куда его плавно ведёт пружина (в схватке мягче, у коня ещё мягче), — относительно
// середины отряда, чтобы на марше не отставал. Ноги и аллюр — по нарисованному ходу, а не по рывкам движка.
// Удар виден у цели (Iron Kings): попал — брызги крови и отброс павшего, на щит — искра у щита и толчок назад; у оружия
// в миг удара — след (смазанные копии). Всадник в рубке: конь переступает и доворачивает под удар, копьё после сшибки —
// меч. Сбитый конём (Г90) падает навзничь, лежит и встаёт.
// Сшибка (В20): конь, что шёл быстрее 4 м/с и за кадр потерял треть хода, — врезался: рывок вперёд и назад, голова вниз,
// всадника бросает вперёд, пыль из-под копыт, копьё ломается щепками — дальше меч. Сбитого отбрасывает на 0,7–1,4 м,
// кувырком; встаёт — возвращается в строй. Второй ряд: древковые (пики — до четвёртой шеренги) колют через плечо передних.
// Павшие: первые 0,15 с качаются от удара, потом ложатся головой по удару; всадник слетает с убитого коня;
// раненые (Г39) ползут рывками прочь от врага с кровавым следом или корчатся, потом затихают.
// Стрелы летят дугой, тень на земле отходит с высотой. Всё — сетками с шейдером Men.
// Оси: как в полигоне — метры карты, y вниз; курс h — поворот canvas (вперёд — −y); в мир Unity: X = x, Y = −y.
using System;
using System.Collections.Generic;
using System.Linq;
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
        // полководец и стража (Алекс 10.10.2026): у отряда с полководцем — боец у знамени (полководец) и 4–12 ближайших к нему
        // (стража); выбраны по первому кадру; пал полководец — его место занимает ближайший живой из стражи
        int[] cmdId; int[][] guardIds; HashSet<int>[] guardSet; Kit[] cmdKit; Kit[][] guardKits; HashSet<int> frameGuard;
        readonly Dictionary<int, Vector3> cmdAt = new Dictionary<int, Vector3>();   // отряд → где полководец в кадре (x, y, курс°) — личное знамя
        public Vector3? CmdAt(int ui) => cmdAt.TryGetValue(ui, out var v) ? v : (Vector3?)null;
        // круг поединка: утоптанная земля кольцом (стража стоит по нему)
        void DuelRing(DuelRec d, float t)
        {
            var px = men.Get("util/px"); var solid = new Vector4(0, 0, 1, 0);
            float a0 = Mathf.Clamp01((t - d.StartT) / 1.5f), fade = float.IsNaN(d.EndT) ? 1 : Mathf.Clamp01(1 - (t - d.EndT) / 3);
            var dust = new Color32(196, 170, 120, (byte)(110 * a0 * fade));
            for (int k = 0; k < 36; k++)
            {
                float a = k * Mathf.PI * 2 / 36, seg = d.R * Mathf.PI * 2 / 36 * 1.1f;
                deadTop.Quad(px, Aff.At(d.X + Mathf.Cos(a) * d.R, d.Y + Mathf.Sin(a) * d.R).R(a).S(5.5f, seg * 10), dust, solid);
            }
        }
        void PickCommand(int i)
        {
            cmdId[i] = -1; guardIds[i] = new int[0]; guardSet[i] = null; cmdKit[i] = null; guardKits[i] = null;
            if (rec.Units[i].Commander <= 0 || rec.Men.Count == 0 || rec.Frames.Count == 0) return;
            var mf = rec.Men[0][i]; var fr = rec.Frames[0][i];
            float h = fr[2] * Mathf.Deg2Rad, dep = (float)rec.Units[i].Depth;
            float bx = fr[0] + Mathf.Sin(h) * dep * 0.2f, by = fr[1] - Mathf.Cos(h) * dep * 0.2f;   // как знамя отряда
            var alive = new List<int>();
            for (int id = 1; id < mf.Xyh.Length / 3; id++) if (!float.IsNaN(mf.Xyh[3 * id])) alive.Add(id);
            if (alive.Count == 0) return;
            float D(int id, float x, float y) { float dx = mf.Xyh[3 * id] - x, dy = mf.Xyh[3 * id + 1] - y; return dx * dx + dy * dy; }
            int c = alive.OrderBy(id => D(id, bx, by)).First();
            float cx = mf.Xyh[3 * c], cy = mf.Xyh[3 * c + 1];
            int n = Mathf.Clamp(alive.Count / 10, 4, 12);
            cmdId[i] = c; guardIds[i] = alive.Where(id => id != c).OrderBy(id => D(id, cx, cy)).Take(n).ToArray();
            guardSet[i] = new HashSet<int>(guardIds[i]);
            string st = ForceStyle ?? rec.Units[i].Style;
            cmdKit[i] = Kits.Commander(rec.Units[i].Id, looks[i], st); guardKits[i] = Kits.Guard(rec.Units[i].Id, looks[i], st);
        }
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

        // стиль облика всем отрядам (В16) — посмотреть запись в другом стиле; null — как в данных
        public static string ForceStyle;
        public void Restyle() { if (rec != null) SetRecording(rec); }

        public void SetRecording(Recording r)
        {
            rec = r;
            int n = r.Units.Count;
            kits = new Kit[n][]; looks = new string[n];
            cmdId = new int[n]; guardIds = new int[n][]; guardSet = new HashSet<int>[n]; cmdKit = new Kit[n]; guardKits = new Kit[n][];
            lowL = new List<float>[n]; lowNear = new bool[n]; arrowsOf = new List<int>[n]; arrowsSeen = 0; vis = new Vis[n][];
            for (int i = 0; i < n; i++)
            {
                looks[i] = Kits.LookOf(r.Units[i].Tpl, r.Units[i].Type);
                kits[i] = Kits.Of(r.Units[i].Id, looks[i], ForceStyle ?? r.Units[i].Style);
                lowL[i] = new List<float>(); arrowsOf[i] = new List<int>();
                PickCommand(i);
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
        internal static bool Thrust(string w) => w == "spear" || w == "fork" || w == "pike" || w == "lance";
        // какой удар (fKind пробы): колющие колют; алебарда, нагината, секира — то сбоку, то сверху; мечи и сабли чаще рубят,
        // иногда колют; топоры, булавы, дубины — сверху и сбоку. Каждый удар выбирается заново — у соседей разный порядок
        static string Kind(string w, int s, int blow)
        {
            string b = Kits.Base(w);
            if (w == "halberd" || w == "naginata" || w == "daneaxe") return H(s * 7 + blow, 42) < 0.5f ? "swing" : "chop";
            if (Thrust(b)) return "thrust";
            if (b == "sword") return H(s * 7 + blow, 41) < 0.35f ? "thrust" : "swing";
            return H(s * 7 + blow, 42) < 0.5f ? "chop" : "swing";
        }

        // поза в покое: где оружие и щит, [x, y, поворот, масштаб поперёк, вдоль]; low — насколько опустили древки (В13):
        // 0 — стоймя (марш, покой), 1 — к бою: копья в 2 передних шеренгах, пики в 4, передняя — первой
        static (float[] W, float[] Sh) Rest(Kit k, int rank, float low)
        {
            float[] W = null, Sh = null;
            string w = k.Base;
            if (w == "spear" || w == "fork") W = Mix(new[] { 0.21f, -0.06f, 0.15f, 1, 0.2f }, new[] { 0.2f, -0.1f, 0, 1, 1 }, rank < 2 ? Ease(Mathf.Clamp01(low * 1.25f - rank * 0.2f)) : 0);
            else if (w == "pike") W = Mix(new[] { 0.15f, -0.04f, 0.1f, 1, 0.12f }, new[] { rank % 2 == 1 ? -0.3f : 0.12f, 0, 0, 1, 1 }, rank < 4 ? Ease(Mathf.Clamp01(low * 1.4f - rank * 0.13f)) : 0);
            else if (w == "lance") W = new[] { 0.22f, 0.05f, 0.05f, 1, 0.24f };
            else if (w != "bow" && w != "crossbow") W = new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f };
            // щит в руке сверху виден ребром (В18): перед собой у левого плеча; у всадника — вдоль левого бока
            if (k.ShieldTop != null) Sh = k.Horse ? new[] { -0.29f, 0.05f, Mathf.PI / 2, 1, 1 } : k.ShieldShape == "buckler" ? new[] { -0.25f, -0.12f, -0.2f, 1, 1 } : new[] { -0.24f, -0.15f, -0.6f, 0.9f, 1 };
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
            // запись укоротили (откат хода, Г112) — указатель стрел заново
            if (arrowsSeen > rec.Arrows.Count) { foreach (var l0 in arrowsOf) l0.Clear(); arrowsSeen = 0; }
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
        // MeleeFor — сколько секунд в схватке (−1 — нет), Down — сколько секунд лежит сбитый (−1 — стоит), Rise — сколько до подъёма
        // Impact — сколько секунд со сшибки коня (−1 — не было за 0,6 с); Second — второй ряд колет через плечо
        // Brace — защитник ждёт удара своего противника (0…1, к мигу удара — 1): щит или оружие навстречу
        // Z — высота над землёй (Г104: на стене 9 м, на башне 11)
        // Role — 1 полководец, 2 стража, 0 — рядовой
        struct ManP { public byte Role; public float X, Y, Face, Sp, Ph, Shot, Ap, Parry, MeleeFor, Down, Rise, Impact, Brace, Z; public int Id, Seed, Rank, Fig, Blow; public bool Atk, Vis, Second; public Kit Kit; }
        // парные поединки: по кому и когда придётся ближайший удар (отряд << 32 | боец → миг удара), на кадр
        readonly Dictionary<long, float> incoming = new Dictionary<long, float>();
        int curUnit;   // отряд, которого бойцов сейчас рисуем (для MenMelee)
        void Incoming(int f0, float t)
        {
            incoming.Clear();
            for (int f = f0; f <= Math.Min(f0 + 1, rec.Men.Count - 1); f++)
                foreach (var mf in rec.Men[f])
                {
                    if (mf?.EngFoe == null) continue;
                    for (int k = 0; k < mf.EngFoe.Length; k++)
                    {
                        int foe = mf.EngFoe[k]; if (foe < 0) continue;
                        long key = (long)(foe >> 20) << 32 | (uint)(foe & 0xFFFFF);
                        foreach (float T in new[] { mf.EngSw[k], mf.EngNx[k] })
                        {
                            if (float.IsNaN(T) || T - t < -0.15f || T - t > 0.45f) continue;
                            if (!incoming.TryGetValue(key, out var was) || Math.Abs(T - t) < Math.Abs(was - t)) incoming[key] = T;
                        }
                    }
                }
        }
        readonly HashSet<int> figAtk = new HashSet<int>();
        readonly List<ManP> M = new List<ManP>();
        readonly Dictionary<int, float> figTop = new Dictionary<int, float>();
        readonly HashSet<long> fallenNow = new HashSet<long>();
        readonly Dictionary<int, (float sw, float nx, float pa)> mel0 = new Dictionary<int, (float, float, float)>(), mel1 = new Dictionary<int, (float, float, float)>();
        readonly List<(float x, float y, float face, float reach, float ap)> sparks = new List<(float, float, float, float, float)>();
        readonly List<(float x, float y, float face, float age)> clangs = new List<(float, float, float, float)>();   // удар на щит: искра у щита
        readonly Dictionary<int, (float at, float end)> down0 = new Dictionary<int, (float, float)>();
        // где боец нарисован (сглаживание): сдвиг от середины отряда и его скорость, где был, ход и фаза шага, доля «в схватке»,
        // с какого времени в схватке; Dx, Dy — нарисован минус место в движке (павший ляжет там, где его видели)
        struct Vis { public float T, Ex, Ey, Vx, Vy, Px, Py, Sp, Ph, Melee, Since, Dx, Dy; }
        Vis[][] vis;
        Vis[] VisOf(int ui, int n)
        {
            var a = vis[ui];
            if (a == null || a.Length < n) { var b = new Vis[n + n / 4 + 16]; if (a != null) Array.Copy(a, b, a.Length); vis[ui] = a = b; }
            return a;
        }

        public void Draw(double t, Color32[] unitCol, Rect view, float ppm)
        {
            if (!Ok || rec == null || rec.Frames.Count == 0) return;
            decals.Clear(); corpses.Clear(); deadTop.Clear(); horseB.Clear(); menB.Clear(); air.Clear(); sparks.Clear(); clangs.Clear(); cmdAt.Clear();
            double ft = t / rec.Dt; int f0 = Math.Min((int)Math.Floor(ft), rec.Frames.Count - 1), f1 = Math.Min(f0 + 1, rec.Frames.Count - 1); float q = (float)(ft - f0);
            float t32 = (float)t;
            bool In(float x, float y, float pad) => x > view.xMin - pad && x < view.xMax + pad && y > view.yMin - pad && y < view.yMax + pad;
            IndexArrows();
            PrepFrame(f0, t32, In);
            if (rec.MenMelee) Incoming(f0, t32);
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
                int st = rec.StateAt(ui, f0); if (st == 2 || f0 >= rec.Men.Count || !rec.Visible(ui, f0, BattleViewer.ViewSide)) continue;   // туман (Г18)
                DrawUnit(ui, rec.Men[f0][ui], rec.Men[Math.Min(f1, rec.Men.Count - 1)][ui], q, st, ft, t32, unitCol[ui], In, soft, shadow, ppm);
            }
            // вспышки ударов (В13): в миг удара у острия — звёздочка, за 0,1 с вырастает и гаснет
            var spark = men.Get("util/spark");
            foreach (var (x, y, face, reach, ap) in sparks)
            {
                float e = (ap - 0.42f) / 0.08f, k2 = 0.6f + 0.8f * e;
                air.Quad(spark, Aff.At(x, y).R(face).T(0.2f, reach).S(k2, k2), new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(1 - e))), Vector4.zero);
            }
            // удар на щит: у щита искра, за 0,12 с вырастает, поворачивается и гаснет
            var solidC = new Vector4(0, 0, 1, 0);
            foreach (var (x, y, face, age) in clangs)
            {
                float e = age / 0.12f, k2 = 0.45f + 0.8f * e;
                air.Quad(spark, Aff.At(x, y).R(face).T(-0.28f, -0.4f).R(e * 0.9f).S(k2, k2), new Color32(255, 238, 190, (byte)(255 * (1 - e))), solidC);
            }
            DrawArrows(t32, In, ppm);
            decals.To(decalMesh); corpses.To(deadMesh); deadTop.To(deadTopMesh); horseB.To(horseMesh); menB.To(menMesh); air.To(airMesh);
        }

        // ── отряд: бойцы движка (Г75) с их местами и курсами; что каждый делает — по схваткам, стрелам и состоянию ──
        void DrawUnit(int ui, MenFrame m0, MenFrame m1, float q, int st, double ft, float t, Color32 col, Func<float, float, float, bool> In, Part soft, Color32 shadow, float ppm)
        {
            var info = rec.Units[ui]; var ks = kits[ui]; string look = looks[ui]; curUnit = ui;
            bool horse = look == "lance" || look == "barded", flee = st == 1 || st == 3;
            int fi = Math.Min((int)ft, rec.Frames.Count - 1);
            bool cheer = st == 4 && (fi - StateSince(ui, fi)) * rec.Dt < 4;   // сплотились (БД4) — первые 4 с ликуют
            float rd = (float)info.RankDepth, low = LowerAt(ui, ft);
            int fd = 1; foreach (var fg in info.Figs) fd = Math.Max(fd, Mathf.RoundToInt((float)fg[1] / rd));
            // бойцы — с местом, курсом, скоростью и фазой шага (между кадрами — плавно)
            M.Clear();
            int n = m0.Xyh.Length / 3; bool anyVis = false;
            if (rec.MenMelee) { Fill(mel0, m0); Fill(mel1, m1); Downs(m0); } else { mel0.Clear(); mel1.Clear(); down0.Clear(); }
            var efv = flee ? null : eng[ui];
            // середина отряда в кадре — бойцов сглаживаем относительно неё (на марше не отстают, дрожь в схватке гаснет)
            var ua = rec.Frames[fi][ui]; var ub = rec.Frames[Math.Min(fi + 1, rec.Frames.Count - 1)][ui];
            float ucx = ua[0] + (ub[0] - ua[0]) * q, ucy = ua[1] + (ub[1] - ua[1]) * q;
            var vs = VisOf(ui, n);
            // полководец пал — его место у ближайшего живого из стражи; есть в записи (движок, Г108) — из записи
            int cmd = cmdId[ui];
            if (cmd > 0 && (3 * cmd >= m0.Xyh.Length || float.IsNaN(m0.Xyh[3 * cmd])))
            { cmd = -1; foreach (var gid in guardIds[ui]) if (3 * gid < m0.Xyh.Length && !float.IsNaN(m0.Xyh[3 * gid])) { cmd = gid; break; } }
            var gset = guardSet[ui];
            if (m0.Cmd >= 0)
            {
                cmd = m0.Cmd;
                if (m0.Guard != null) { if (frameGuard == null) frameGuard = new HashSet<int>(); frameGuard.Clear(); foreach (var g in m0.Guard) frameGuard.Add(g); gset = frameGuard; }
                if (cmdKit[ui] == null) { string sty = ForceStyle ?? info.Style; cmdKit[ui] = Kits.Commander(info.Id, look, sty); guardKits[ui] = Kits.Guard(info.Id, look, sty); }
            }
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
                bool fight = efv != null && efv.ContainsKey(fig) || mel0.ContainsKey(id);
                Smooth(ref vs[id], x, y, ucx, ucy, sp, ph, fight, horse, t, out x, out y, out sp, out ph);
                float mfor = float.IsNaN(vs[id].Since) ? -1 : t - vs[id].Since, dn = -1, rise = 0;
                if (down0.TryGetValue(id, out var dw)) { dn = Mathf.Max(0, t - dw.at); rise = dw.end - t; }
                bool vis = In(x, y, 6); anyVis |= vis;
                float imp = horse && vis ? ImpactAge(ui, id, fi, t) : -1;
                float z0 = m0.Z != null && id < m0.Z.Length ? m0.Z[id] : 0, z1 = m1.Z != null && id < m1.Z.Length ? m1.Z[id] : 0;
                M.Add(new ManP { X = x, Y = y, Z = z0 + (z1 - z0) * q, Face = h * Mathf.Deg2Rad, Sp = sp, Ph = ph, Id = id, Seed = seed, Fig = fig, Vis = vis, Shot = float.NaN, Ap = -1, Parry = -1, MeleeFor = mfor, Down = dn, Rise = rise, Impact = imp,
                    Rank = (fig < info.Figs.Count ? (int)info.Figs[fig][3] : 0) * fd + m0.Row[id],
                    Role = (byte)(id == cmd ? 1 : gset != null && gset.Contains(id) ? 2 : 0),
                    Kit = id == cmd ? cmdKit[ui] : gset != null && gset.Contains(id) ? guardKits[ui][id % guardKits[ui].Length] : ks[(int)(H(seed, 4) * ks.Length)] });
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
            // второй ряд (В20): древковые второй шеренги (пики — до четвёртой) колют через плечо передних, если их колонна рубится
            if (!flee && !horse && look != "bow" && look != "crossbow")
            {
                figAtk.Clear();
                foreach (var m in M) if (m.Atk && m.Rank == 0) figAtk.Add(m.Fig);
                if (figAtk.Count > 0)
                    for (int i = 0; i < M.Count; i++)
                    {
                        var m = M[i]; string b = m.Kit.Base;
                        if (m.Atk || m.Down >= 0 || m.Rank < 1 || m.Rank > (b == "pike" ? 3 : 1) || !Thrust(b) || !figAtk.Contains(m.Fig)) continue;
                        m.Atk = true; m.Ap = -1; m.Second = true; M[i] = m;
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
                    for (int i = 0; i < M.Count; i++) if (M[i].Vis && M[i].Role == 0) GridAdd(Cell(M[i].X, M[i].Y, 2), i);   // полководец и стража не стреляют
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
                // мягкая тень (В18): форма — по телу, сдвиг — в мире, вправо вниз; у коня — в слое коней
                var hsoft = horses.Get("util/soft");
                foreach (var m in M)
                {
                    if (!m.Vis) continue;
                    if (!horse) { float k = 1 + 0.012f * m.Z; menB.Quad(soft, Aff.At(m.X + 0.045f * k, m.Y + 0.065f * k).R(m.Face).S(0.72f * k, 0.48f * k), shadow, new Vector4(0, 0, 1, 0)); }
                    else horseB.Quad(hsoft, Aff.At(m.X + 0.08f, m.Y + 0.1f).R(m.Face).S(0.9f, 2.25f), shadow, new Vector4(0, 0, 1, 0));
                }
            }
            // поединок (Г108): удары полководца — по записи поединка: замах до удара, удар, возврат; противник принимает на щит
            // или ранен (кровь); круг поединка — утоптанная земля
            foreach (var d in rec.Duels)
            {
                if (d.A != ui && d.B != ui || t < d.T0 || !float.IsNaN(d.EndT) && t > d.EndT + 3) continue;
                if (ui == d.A && !float.IsNaN(d.StartT)) DuelRing(d, t);
                for (int i = 0; i < M.Count; i++)
                {
                    var m = M[i]; if (m.Role != 1) continue;
                    float best = float.NaN; bool mine = false, hit = false;
                    foreach (var s in d.Strikes) { float dd = t - s.t; if (dd >= -0.45f && dd <= 0.55f && (float.IsNaN(best) || Mathf.Abs(dd) < Mathf.Abs(t - best))) { best = s.t; mine = s.unit == ui; hit = s.hit; } }
                    if (!float.IsNaN(best))
                    {
                        if (mine) { m.Atk = true; m.Ap = 0.45f + (t - best); m.Blow = Mathf.RoundToInt(best * 20); }
                        else if (!hit && t >= best && t - best < 0.35f) m.Parry = t - best;
                        else if (hit && t >= best && t - best < 0.45f && m.Vis) Spray(m.X, m.Y, m.Face + Mathf.PI / 2, t - best, Mathf.RoundToInt(best * 97) + ui, men.Get("util/disc"));   // удар — назад от лица
                        else if (!mine) m.Brace = Mathf.Clamp01(1 - (best - t) / 0.4f);
                    }
                    M[i] = m; break;
                }
            }
            // полководец (Алекс 10.10.2026): золотое кольцо под ногами — видно и издали; где он — личному знамени
            foreach (var m in M)
            {
                if (m.Role != 1) continue;
                cmdAt[ui] = new Vector3(m.X, m.Y, m.Face * Mathf.Rad2Deg);
                if (!m.Vis || flee) break;
                var px = men.Get("util/px"); float r = horse ? 2.0f : 0.95f, w = Mathf.Max(0.09f, 2.2f / ppm);
                var gold = new Color32(232, 186, 82, 230); var solid = new Vector4(0, 0, 1, 0);
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI * 2 / 24, seg = r * Mathf.PI * 2 / 24 * 1.08f;
                    deadTop.Quad(px, Aff.At(m.X + Mathf.Cos(a) * r, m.Y + Mathf.Sin(a) * r).R(a).S(w * 10, seg * 10), gold, solid);
                }
                break;
            }
            for (int i = 0; i < M.Count; i++)
            {
                var m = M[i]; if (!m.Vis) continue;
                bool engaged = ef != null && ef.ContainsKey(m.Fig), fireHere = fire.Contains(ui * 100000L + m.Fig);
                if (m.Role > 0 && !horse && (look == "bow" || look == "crossbow")) DrawMan(m, "sword", false, flee, cheer, engaged, false, false, false, low, t, col, ppm);   // полководец и стража стрелков — с мечом
                else DrawMan(m, look, horse, flee, cheer, engaged, fireHere, shoot, active, low, t, col, ppm);
            }
        }

        // ── рукопашная по бойцам (Б2): противник, удар и щит — из движка ──
        // Боец смотрит на своего противника — это курс из движка (Г94); удар проигрывается вокруг своего времени в движке: замах — до (по NextSwing),
        // вспышка — в миг удара (тогда же падает ударенный), возврат — после; принял удар на щит — щит рывком навстречу
        void MenMelee(MenFrame m0, MenFrame m1, float q, float t)
        {
            if (mel0.Count == 0 && mel1.Count == 0) return;
            for (int i = 0; i < M.Count; i++)
            {
                var m = M[i];
                bool a0 = mel0.TryGetValue(m.Id, out var e0), a1 = mel1.TryGetValue(m.Id, out var e1);
                if (!a0 && !a1) continue;
                // курс — из движка как есть (Г94: Man.Facing в схватке уже смотрит на противника и поворачивается плавно)
                // ближний удар к t: −0,45 с — замах, 0 — удар, +0,55 с — возврат
                float best = float.NaN;
                void Try(float T) { if (float.IsNaN(T)) return; float d = t - T; if (d >= -0.45f && d <= 0.55f && (float.IsNaN(best) || Mathf.Abs(d) < Mathf.Abs(t - best))) best = T; }
                if (a0) { Try(e0.sw); Try(e0.nx); }
                if (a1) { Try(e1.sw); Try(e1.nx); }
                if (!float.IsNaN(best)) { m.Atk = true; m.Ap = 0.45f + (t - best); m.Blow = Mathf.RoundToInt(best * 20); }
                float pa = a1 && !float.IsNaN(e1.pa) && e1.pa <= t ? e1.pa : a0 ? e0.pa : float.NaN;
                if (!float.IsNaN(pa) && t - pa >= 0 && t - pa < 0.35f) m.Parry = t - pa;
                // противник замахнулся на него: за 0,4 с до удара — щит (или оружие) навстречу, после удара 0,15 с — опускает
                if (incoming.TryGetValue((long)curUnit << 32 | (uint)m.Id, out var tin)) { float dd = tin - t; m.Brace = dd >= 0 ? 1 - dd / 0.4f : Mathf.Max(0, 1 + dd / 0.15f); }
                if (m.Parry >= 0 && m.Parry < 0.12f && m.Vis && m.Kit.ShieldTop != null) clangs.Add((m.X, m.Y, m.Face, m.Parry));
                M[i] = m;
            }
        }
        void Downs(MenFrame f)
        {
            down0.Clear();
            if (f?.Down == null) return;
            for (int k = 0; k < f.Down.Length; k++) down0[f.Down[k]] = (f.DownAt[k], f.DownEnd[k]);
        }

        // сглаживание бойца: пружина (SmoothDamp) к месту в движке — относительно середины отряда; время пружины — у пешего
        // 0,05 с на марше и 0,2 с в схватке, у коня 0,08 и 0,4; в схватку входит за 0,3 с, выходит за 1 с. Перемотка, пауза
        // дольше 0,5 с или рывок дальше 6 м — сразу на место. Ход и фаза шага — по нарисованному пути
        static void Smooth(ref Vis v, float x, float y, float cx, float cy, float sp, float ph, bool fight, bool horse, float t,
                           out float ox, out float oy, out float osp, out float oph)
        {
            float dtv = t - v.T, ex = x - cx, ey = y - cy;
            if (v.T <= 0 || dtv < 0 || dtv > 0.5f || (ex - v.Ex) * (ex - v.Ex) + (ey - v.Ey) * (ey - v.Ey) > 36)
            {
                v.Ex = ex; v.Ey = ey; v.Vx = v.Vy = 0; v.Sp = sp; v.Ph = ph; v.Px = x; v.Py = y; v.T = t;
                v.Melee = fight ? 1 : 0; v.Since = fight ? t : float.NaN;
            }
            else if (dtv > 0)
            {
                v.Melee = Mathf.MoveTowards(v.Melee, fight ? 1 : 0, dtv / (fight ? 0.3f : 1f));
                if (fight && float.IsNaN(v.Since)) v.Since = t; else if (!fight && v.Melee <= 0) v.Since = float.NaN;
                float st = Mathf.Lerp(horse ? 0.08f : 0.05f, horse ? 0.4f : 0.2f, v.Melee);
                v.Ex = Mathf.SmoothDamp(v.Ex, ex, ref v.Vx, st, Mathf.Infinity, dtv);
                v.Ey = Mathf.SmoothDamp(v.Ey, ey, ref v.Vy, st, Mathf.Infinity, dtv);
                float nx = cx + v.Ex, ny = cy + v.Ey, d = Mathf.Sqrt((nx - v.Px) * (nx - v.Px) + (ny - v.Py) * (ny - v.Py));
                v.Sp += (d / dtv - v.Sp) * (1 - Mathf.Exp(-dtv / 0.2f));
                float stride = horse ? (v.Sp < 2.3f ? 1.7f : v.Sp < 4.8f ? 2.8f : 5f) : v.Sp > 2.6f ? 2.4f : 1.4f;
                v.Ph += d / stride; v.Px = nx; v.Py = ny; v.T = t;
            }
            ox = cx + v.Ex; oy = cy + v.Ey; osp = v.Sp; oph = v.Ph; v.Dx = ox - x; v.Dy = oy - y;
        }

        static void Fill(Dictionary<int, (float, float, float)> d, MenFrame f)
        {
            d.Clear();
            if (f?.Eng == null) return;
            for (int k = 0; k < f.Eng.Length; k++) d[f.Eng[k]] = (f.EngSw[k], f.EngNx[k], f.EngPa[k]);
        }

        // ── один боец: как в drawMen полигона ──
        void DrawMan(ManP m, string look, bool horse, bool flee, bool cheer, bool engaged, bool fireHere, bool shoot, bool active, float low, float t, Color32 col, float ppm)
        {
            var kit = m.Kit; int s = m.Seed; float ph0 = H(s, 5); bool walking = m.Sp > 0.8f;
            if (m.Down >= 0 && m.Rise > 0 && !horse) { DrawDown(m, col); return; }
            float rot = 0, ox = 0, oy = 0, step = 0, ap = -1; int blow = 0;
            if (m.Atk && m.Ap >= 0) { ap = m.Ap; blow = m.Blow; }   // Б2: удар — когда он в движке
            else if (m.Atk) { float per = 1.1f + 0.9f * H(s, 7), u0 = t / per + H(s, 8); ap = Frac(u0); blow = Mathf.FloorToInt(u0); }
            if (walking && !m.Atk) { step = Mathf.Sin(m.Ph * 6.283f); rot += (m.Sp > 2.6f ? 0.11f : 0.07f) * step; }   // бегом плечи ходят сильнее
            else if (!m.Atk) { rot += 0.035f * Mathf.Sin(t * 0.9f + ph0 * 6.283f) + (engaged ? 0 : Glance(t, s)); ox = 0.02f * Mathf.Sin(t * 0.6f + ph0 * 9); }
            if (engaged && !m.Atk) oy = -0.03f - 0.03f * Mathf.Sin(t * 3 + ph0 * 6.283f);   // задние напирают
            var (PW, PSh) = Rest(kit, m.Rank, low);
            var bas = Aff.At(m.X, m.Y).R(m.Face);
            if (m.Z > 0) { float k = 1 + 0.012f * m.Z; bas = bas.S(k, k); }
            if (m.Role > 0) { float k = m.Role == 1 ? 1.15f : 1.05f; bas = bas.S(k, k); }   // полководец крупнее на 15 %, стража — на 5 %   // на стене (Г104): ближе к глазу — на 11–13 % крупнее

            // издали (меньше 16 px/м, В18) — как у образца: капсула со шлемом; из оружия — древки передних шеренг и опущенное копьё
            if (ppm < 16)
            {
                var fm = bas.T(ox, oy);
                if (horse)
                {
                    horseB.Quad(horses.Get(kit.HorseHead), bas.T(0, -0.5f), col, Vector4.zero);
                    horseB.Quad(horses.Get($"hbody/{kit.Coat}"), bas, col, Vector4.zero);
                    horseB.Quad(horses.Get(kit.HorseCover), bas, col, Vector4.zero);
                    fm = bas.T(0, 0.03f);
                }
                menB.Quad(men.Get(kit.Body), fm, Col(kit.BodyCol, col), Vector4.zero);
                menB.Quad(men.Get(kit.Head), fm.T(0, -0.03f), kit.HeadTint ? Col(kit.HeadCol, col) : col, kit.HeadTint ? Tint1 : Vector4.zero);
                bool pole = !flee && Thrust(kit.Base) && (horse ? walking || m.Atk : m.Rank < 2);
                if (pole && PW != null) menB.Quad(men.Get("weapon/" + kit.Weapon), Pose(fm, horse ? new[] { 0.2f, 0.25f, -0.04f, 1, 1 } : PW), col, Vector4.zero);
                return;
            }
            // бегство (В11): бегом, щит за спиной, оружие несут как придётся или бросили; оглядываются (В13)
            if (flee && !horse)
            {
                rot += LookBack(t, s);
                var Mf = bas.T(ox, -0.05f).R(rot);
                string w = Drops(s) || kit.Base == "bow" || kit.Weapon == "crossbow" ? null : kit.Weapon;
                var Wf = w == null ? null : Thrust(Kits.Base(w)) ? new[] { 0.2f, 0.05f, 0.4f + 0.05f * step, 1, 0.25f } : new[] { 0.2f, 0.05f, 0.6f, 1, 0.5f };
                Flat(kit, bas, Mf, 0.11f * step, w, Wf, null, step, 0, 0, false, true, false, col, Vector4.zero);
                return;
            }
            // всадник (В18): конь по частям — аллюр по скорости, стоящий переступает и машет хвостом; шея уходит под грудь;
            // поводья — от удил к рукам; всадник в седле, ноги по бокам, копьё на скаку опущено
            if (horse)
            {
                bool run = walking || m.Atk;
                // в рубке конь не стоит столбом: переступает и доворачивает корпус под удар всадника
                bool fightH = m.Atk && m.Sp < 1.2f;
                HorsePose(fightH ? 1f : m.Sp, fightH ? t * 0.9f + ph0 : m.Ph, t, s, out var nod, out var tail, out var bob);
                float sw = m.Atk && ap >= 0 ? Mathf.Sin(ap * 6.283f) : 0;
                if (sw != 0) bas = bas.R(0.12f * sw);
                // сшибка: за 0,5 с рывок вперёд и назад, голова вниз, всадника бросает вперёд; пыль из-под копыт
                float jolt = m.Impact >= 0 && m.Impact < 0.5f ? Mathf.Sin(m.Impact / 0.5f * Mathf.PI) * (1 - m.Impact / 0.5f) : 0;
                if (jolt > 0) { bas = bas.T(0, -0.3f * jolt); nod += 0.14f * jolt; }
                if (m.Impact >= 0 && m.Impact < 0.6f) Dust(bas, 0, -0.6f, m.Impact, 0.6f, s);
                var z = Vector4.zero; var white = new Color32(255, 255, 255, 255);
                for (int i = 0; i < 4; i++) horseB.Quad(horses.Get((kit.Socks >> i & 1) != 0 ? $"hleg/{kit.Coat}/s" : $"hleg/{kit.Coat}"), bas.T(HLeg[i, 0], HLeg[i, 1] + legs[i]), white, z);
                horseB.Quad(horses.Get($"htail/{kit.Coat}"), bas.T(0, 0.84f).R(tail), white, z);
                horseB.Quad(horses.Get(kit.HorseHead), bas.T(0, -0.5f + nod), col, z);
                horseB.Quad(horses.Get($"hbody/{kit.Coat}"), bas, white, z);
                horseB.Quad(horses.Get(kit.HorseCover), bas, col, z);
                if (ppm >= 30) for (int sd = -1; sd <= 1; sd += 2) Strip(menB, bas, sd * 0.1f, -1.19f + nod, sd * 0.07f, -0.13f, 0.9f / ppm, new Color32(74, 48, 32, 255));
                var R0 = bas.T(0, 0.03f - bob * 0.5f); var Mr = R0.T(ox + 0.05f * sw, -0.18f * jolt).R(rot * 0.35f + 0.15f * sw);
                // копьё — на сшибку: ломается щепками (через 0,15 с после удара — меч); без сшибки — меч после 0,5 с рубки на месте
                string hw = kit.Weapon, hb = kit.Base;
                if (hb == "lance" && m.Impact >= 0 && m.Impact < 0.5f) Splinters(bas, m.Impact, s);
                if (hb == "lance" && (m.Impact >= 0.15f || m.Atk && m.MeleeFor > 0.5f && m.Sp < 3)) { hw = "sword"; hb = "sword"; }
                var W = hb == "lance" || PW == null ? PW : new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f };
                if (hb == "lance") { if (run) W = new[] { 0.2f, 0.25f + (m.Atk ? ThrustOff(ap) : 0), -0.04f, 1, 1 }; }
                else if (m.Atk && W != null) { var (r2, sy2) = SwingAng(ap); W = new[] { 0.22f, -0.08f, r2, 1, sy2 }; if (ap > 0.3f && ap < 0.56f) Smear(Mr, hw, "swing", hb, ap, W, col); }
                Flat(kit, R0, Mr, 0, hw, W, PSh, 0, 0, 0, false, false, true, col, Vector4.zero);
                if (!rec.MenMelee && m.Atk && ap >= 0.42f && ap < 0.5f) sparks.Add((m.X, m.Y, m.Face, hb == "lance" ? -2.6f : -0.75f, ap));
                return;
            }
            // стрелок: состояние лука — по времени до своего выстрела
            int bowSt = 0, xb = 0; float lean = 0;
            if (shoot && !m.Atk)
            {
                if (!float.IsNaN(m.Shot))
                {
                    float dt = m.Shot;
                    if (kit.Base == "bow") bowSt = dt < -0.45f ? 1 : dt < -0.2f ? 2 : dt < 0 ? 3 : dt < 0.22f ? 4 : 1;
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
            string kind = !m.Atk || wk == null ? null : Kind(wk, s, blow);
            if (m.Second && kind == "swing") kind = "chop";   // из-за спин — только колоть и сверху
            if (m.Atk && ap >= 0) { oy -= m.Ap < 0 && ap > 0.3f && ap < 0.55f ? 0.06f : 0;   // Б2: выпад к противнику уже в X/Y движка (Г78)
                 if (m.Ap >= 0) oy -= 0.16f * (ap < 0.3f || ap > 0.62f ? 0 : ap < 0.43f ? Ease((ap - 0.3f) / 0.13f) : 1 - Ease((ap - 0.43f) / 0.19f));   // поединок: шаг в удар, в такт ему
                 rot += kind == "swing" ? 0.18f * Mathf.Sin(ap * 6.283f) : kind == "chop" ? -0.08f * Mathf.Sin(ap * 6.283f) : 0; }
            if (cheer && !m.Atk) oy -= 0.05f * Mathf.Max(0, Mathf.Sin(t * 9 + ph0 * 6.283f));   // ликуют — подпрыгивают
            if (m.Parry >= 0) { float c = 1 - m.Parry / 0.35f; oy += 0.12f * c * c; }   // принял удар на щит — толкнуло назад
            bool guard = m.Brace > 0 && (!m.Atk || ap < 0.22f || ap > 0.66f);   // ждёт удара, сам сейчас не бьёт
            if (guard) oy += 0.05f * m.Brace;
            var Mm = bas.T(ox, oy - lean).R(rot);
            float st = step != 0 ? step : m.Atk ? Mathf.Sin(ap * 6.283f) * 0.6f : 0;   // ноги: на ходу и в бою шагают
            // щит: под стрелами — над головой; в рукопашной — вперёд, навстречу удару врага (прикрывается между своими ударами)
            bool raise = fireHere && !m.Atk && PSh != null && kit.ShieldShape != "buckler" && H(s, 13) < 0.85f;
            var Sh = PSh;
            if (Sh != null)
            {
                if (raise) Sh = new[] { -0.03f, -0.05f, -0.1f, 1, 0.9f };
                else if (m.Parry >= 0) { float c = 1 - m.Parry / 0.35f; c = c * c; Sh = new[] { Sh[0] + 0.07f * c, Sh[1] - 0.14f * c, Sh[2] + 0.4f * c, Sh[3], Sh[4] }; }   // Б2: принял удар
                else if (m.Atk) { float c = Mathf.Max(0, Mathf.Sin((ap + 0.5f) * 6.283f)); Sh = new[] { Sh[0] + 0.04f + 0.05f * c, Sh[1] - 0.06f - 0.09f * c, Sh[2] + 0.15f + 0.2f * c, Sh[3], Sh[4] }; }
                if (guard && m.Parry < 0) Sh = Mix(Sh, new[] { -0.07f, -0.3f, -0.1f, Sh[3] * 1.05f, Sh[4] }, Ease(m.Brace));   // щит — перед собой, навстречу удару
            }
            // оружие
            string wpn = wk, wb = Kits.Base(wk); var W2 = PW;
            if (m.Atk && shoot) W2 = wpn != null ? new[] { 0.22f, -0.06f, 0.3f, 1, 0.65f } : null;
            if (m.Atk && wpn != null && W2 != null)
            {
                var Wb = W2; W2 = StrikeW(kind, wb, ap, Wb);
                if (ap > 0.3f && ap < 0.56f) Smear(Mm, wpn, kind, wb, ap, Wb, col);
                if (!rec.MenMelee && ap >= 0.42f && ap < 0.5f) sparks.Add((m.X, m.Y, m.Face, kind == "thrust" ? (wb == "pike" ? -3.7f : Thrust(wb) ? -1.35f : -0.85f) : -0.75f, ap));
            }
            else if (guard && Sh == null && W2 != null && !Thrust(wb)) W2 = Mix(W2, new[] { 0.08f, -0.3f, -1.15f, 1, 0.7f }, Ease(m.Brace));   // без щита — оружие поперёк, принять удар
            else if (cheer && W2 != null) W2 = new[] { W2[0], W2[1] - 0.05f, W2[2] * 0.3f - 0.1f, 1, Mathf.Min(W2[4], 0.3f) + 0.08f * Mathf.Sin(t * 9 + ph0 * 6.283f) };   // вскинули оружие
            else if (W2 != null && step != 0) W2 = new[] { W2[0], W2[1], W2[2] + 0.03f * step, W2[3], W2[4] };
            // плоский боец (В18); стрелок в рукопашной держит запасное оружие одной рукой
            float sway = walking && !m.Atk ? (m.Sp > 2.6f ? 0.11f : 0.07f) * step : 0;
            Flat(kit, bas.T(ox, oy), Mm, sway, wpn, wpn != null ? W2 : null, Sh, st, bowSt, xb, raise, false, false, col, Vector4.zero);
        }

        // оружие в ударе: доля круга удара ap → поза (как Rest: x, y, поворот, масштаб поперёк, вдоль); Wb — поза в покое
        static float[] StrikeW(string kind, string wb, float ap, float[] Wb)
        {
            if (kind == "thrust") return Thrust(wb) ? new[] { Wb[0], (wb == "pike" ? 0 : -0.1f) + ThrustOff(ap), 0, 1, 1 } : new[] { 0.16f, -0.14f + 0.8f * ThrustOff(ap), 0.04f, 1, 1 };
            if (kind == "chop") { var (y2, sy2) = ChopPose(ap); return new[] { 0.2f, y2, 0.1f, 1, sy2 }; }
            var (r2, s2) = SwingAng(ap); return new[] { 0.22f, -0.08f, r2, 1, s2 };
        }
        // след удара: три полупрозрачные копии оружия на 0,03 круга позади — ярче всего в миг удара (ap ≈ 0,43)
        void Smear(Aff Mm, string wpn, string kind, string wb, float ap, float[] Wb, Color32 col)
        {
            var p = men.Get("weapon/" + wpn);
            float env = Mathf.Clamp01(1 - Mathf.Abs(ap - 0.43f) / 0.13f);
            for (int g = 1; g <= 3; g++)
            {
                float apg = ap - 0.03f * g, a = (0.45f - 0.12f * g) * env;
                if (apg < 0.3f || a <= 0.02f) continue;
                var c = col; c.a = (byte)(255 * a);
                menB.Quad(p, Pose(Mm, StrikeW(kind, wb, apg, Wb)), c, Vector4.zero);
            }
        }
        // сбит с ног конём (Г90): падает навзничь (прочь от курса) за 0,25 с, лежит, за 0,35 с до подъёма встаёт
        // Отброс (В20): летит назад 0,7–1,4 м за 0,35 с, кувыркаясь (в полёте крупнее), на земле — пыль; встаёт — возвращается
        // к своему месту (в движке он лежит там, где сбит)
        void DrawDown(ManP m, Color32 col)
        {
            var kit = m.Kit;
            float e = Mathf.Min(Ease(Mathf.Clamp01(m.Down / 0.25f)), Mathf.Clamp01(m.Rise / 0.35f));
            float a = Mathf.Atan2(Mathf.Cos(m.Face), -Mathf.Sin(m.Face)) + (H(m.Seed, 91) - 0.5f) * 0.6f;
            float fl = Mathf.Clamp01(m.Down / 0.35f), back = Mathf.Clamp01(m.Rise / 0.6f), D = (0.7f + 0.7f * H(m.Seed, 90)) * (1 - (1 - fl) * (1 - fl)) * back;
            float x = m.X + Mathf.Cos(a) * D, y = m.Y + Mathf.Sin(a) * D, air0 = 1 + 0.18f * Mathf.Sin(fl * Mathf.PI), spin = (H(m.Seed, 92) - 0.5f) * 1.4f * (1 - fl);
            if (m.Down > 0.25f && m.Down < 0.85f) Dust(Aff.At(x, y), 0, 0, m.Down - 0.25f, 0.6f, m.Seed);
            var cm = Aff.At(x, y).R(a + Mathf.PI / 2 + spin).S(air0, (0.25f + 0.75f * e) * air0).T(0, -0.8f);
            corpses.Quad(dead.Get($"corpse/{kit.LayoutKey}/{(int)(H(m.Seed, 51) * 2)}"), cm, Col(kit.BodyCol, col), Vector4.zero);
            deadTop.Quad(men.Get(kit.Head), cm.T(0, -0.56f), kit.HeadTint ? Col(kit.HeadCol, col) : col, kit.HeadTint ? Tint1 : Vector4.zero);
        }

        // сшибка (В20): конь две пятых секунды шёл быстрее 6 м/с не в схватке, потом две пятых — медленнее 0,6 того и уже
        // в схватке — врезался. Возраст удара (с); −1 — не было за 0,6 с. Без состояния — по кадрам записи (и при перемотке)
        float ImpactAge(int ui, int id, int f0, float t)
        {
            int last = Math.Min(f0 + 1, rec.Men.Count - 2);   // кадр вперёд есть: счёт идёт впереди показа
            for (int f = last; f >= Math.Max(3, f0 - 3); f--)
            {
                float b = Mathf.Min(SpeedAt(ui, id, f - 3), SpeedAt(ui, id, f - 2));
                if (b < 6 || SpeedAt(ui, id, f - 1) > 0.6f * b || SpeedAt(ui, id, f) > 0.6f * b) continue;
                if (InEng(ui, id, f - 3) || !InEng(ui, id, f) && !InEng(ui, id, f + 1)) continue;
                return t - (f - 1) * (float)rec.Dt;
            }
            return -1;
        }
        bool InEng(int ui, int id, int f) { var e = rec.Men[f][ui].Eng; return e != null && Array.IndexOf(e, id) >= 0; }
        float SpeedAt(int ui, int id, int f)
        {
            var a = rec.Men[f][ui].Xyh; var b = rec.Men[f + 1][ui].Xyh;
            if (3 * id + 1 >= a.Length || 3 * id + 1 >= b.Length || float.IsNaN(a[3 * id]) || float.IsNaN(b[3 * id])) return 0;
            float dx = b[3 * id] - a[3 * id], dy = b[3 * id + 1] - a[3 * id + 1];
            return Mathf.Sqrt(dx * dx + dy * dy) / (float)rec.Dt;
        }
        // пыль: два-три клуба в осях m у (x, y), растут и гаснут за life с
        void Dust(Aff m, float x, float y, float age, float life, int seed)
        {
            if (age < 0 || age >= life) return;
            float u = age / life; var soft = men.Get("util/soft"); var solid = new Vector4(0, 0, 1, 0);
            for (int k = 0; k < 3; k++)
            {
                float ox = (H(seed, 100 + k) - 0.5f) * 1.2f, oy = (H(seed, 103 + k) - 0.5f) * 0.6f, r = (0.5f + 0.5f * H(seed, 106 + k)) * (0.5f + 1.2f * u);
                decals.Quad(soft, m.T(x + ox * (0.6f + u), y + oy).S(r * 1.6f, r * 1.6f), new Color32(146, 122, 82, (byte)(190 * (1 - u) * (1 - u))), solid);
            }
        }
        // щепки сломанного копья: от острия вперёд и в стороны, кувыркаются, падают за 0,5 с
        void Splinters(Aff bas, float age, int seed)
        {
            float u = age / 0.5f, fly = 1 - (1 - u) * (1 - u); var px = men.Get("util/px"); var solid = new Vector4(0, 0, 1, 0);
            for (int k = 0; k < 6; k++)
            {
                float ang = (H(seed, 110 + k) - 0.5f) * 2.2f, dist = (0.6f + 1.4f * H(seed, 116 + k)) * fly;
                var at = bas.T(0.2f + Mathf.Sin(ang) * dist, -2.4f - Mathf.Cos(ang) * dist).R(ang + 8 * u * (H(seed, 122 + k) - 0.5f));
                air.Quad(px, at.S(0.5f, 2.5f + 2 * H(seed, 128 + k)), new Color32(120, 84, 48, (byte)(255 * (1 - u * u))), solid);
            }
        }

        // ── плоский боец (В18), как fMan пробы: ступни или ноги в седле, поклажа, предплечья, тело, щит ребром, голова,
        // оружие, кисти. M0 — оси бойца без покачивания, Mm — с покачиванием и выпадом; sway — на сколько качнуло плечи
        // (голова качается меньше); raise — щит над головой под стрелами, slung — щит за спиной (бегство); dim — приглушение ──
        static readonly Vector4 Tint1 = new Vector4(0, 0, 0, 1);
        static readonly float[] RaiseSh = { -0.03f, -0.05f, -0.1f, 1, 0.9f };
        // цвет части: свой (RGB) или цвет стороны, сдвинутый к белому или чёрному
        static Color32 Col(Tint t, Color32 team)
        {
            if (t.Rgb >= 0) return new Color32((byte)(t.Rgb >> 16), (byte)(t.Rgb >> 8 & 255), (byte)(t.Rgb & 255), 255);
            if (t.Tone == 0) return team;
            return Color32.Lerp(team, t.Tone > 0 ? new Color32(255, 255, 255, team.a) : new Color32(0, 0, 0, team.a), Mathf.Abs(t.Tone));
        }
        void Flat(Kit k, Aff M0, Aff Mm, float sway, string wpn, float[] W, float[] Sh, float feet, int bowSt, int xb, bool raise, bool slung, bool mounted, Color32 team, Vector4 dim)
        {
            var B = menB; var tint = new Vector4(0, dim.y, 0, 1);
            if (mounted) B.Quad(men.Get(k.Rider), M0, Col(k.Cloth, team), dim);
            else if (feet != 0) { var bt = men.Get("boot"); B.Quad(bt, M0.T(-0.085f, 0.02f + 0.15f * feet), team, dim); B.Quad(bt, M0.T(0.085f, 0.02f - 0.15f * feet), team, dim); }
            string bw = Kits.Base(wpn);
            bool bowRest = bw == "bow" && bowSt == 0;   // лук в покое — опущен у левого бока вдоль тела; поперёк — когда стреляет
            Vector2? hr, hl; bool showL;
            if (bowRest) { hr = null; hl = new Vector2(-0.36f, 0.02f); showL = true; }
            else (hr, hl, showL) = HandsOf(bw, W, raise ? RaiseSh : slung ? null : Sh, bowSt, xb);
            if (k.Back != null) B.Quad(men.Get(k.Back), Mm, team, dim);
            if (slung && k.ShieldFlat != null) B.Quad(men.Get(k.ShieldFlat), Mm.T(0, 0.15f).S(0.7f, 0.7f), Col(k.ShieldCol, team), dim);
            var arm = men.Get("arm"); var sleeve = Col(k.Sleeve, team);
            for (int side = 1; side >= -1; side -= 2)
            {
                var h = side > 0 ? hr : hl; if (h == null) continue;
                float ex = side * ElbX, dx = h.Value.x - ex, dy = h.Value.y - ElbY, L = Mathf.Sqrt(dx * dx + dy * dy);
                if (L >= 0.02f) B.Quad(arm, Mm.T(ex, ElbY).R(Mathf.Atan2(dx, -dy)).S(1, L / FaLen), sleeve, tint);
            }
            B.Quad(men.Get(k.Body), Mm, Col(k.BodyCol, team), dim);
            if (Sh != null && !raise && !slung && k.ShieldTop != null) B.Quad(men.Get(k.ShieldTop), Pose(Mm, Sh), Col(k.ShieldCol, team), dim);
            B.Quad(men.Get(k.Head), Mm.T(0, -0.03f).R(-0.4f * sway), k.HeadTint ? Col(k.HeadCol, team) : team, k.HeadTint ? tint : dim);
            if (raise && k.ShieldFlat != null) B.Quad(men.Get(k.ShieldFlat), Mm.T(-0.03f, -0.05f).R(-0.1f).S(0.95f, 0.95f), Col(k.ShieldCol, team), dim);   // щит над головой
            if (bowRest && k.BowPart != null) B.Quad(men.Get(k.BowPart + "0"), Mm.T(-0.08f, 0.02f).R(-Mathf.PI / 2).S(0.8f, 0.8f), team, dim);
            else if (bw == "bow" && k.BowPart != null) B.Quad(men.Get(k.BowPart + bowSt), Mm, team, dim);
            else if (wpn == "crossbow") B.Quad(men.Get("xbow/" + xb), Mm, team, dim);
            else if (W != null && wpn != null) B.Quad(men.Get("weapon/" + wpn), Pose(Mm, W), team, dim);
            var hand = men.Get(k.Hand);
            if (hr != null) B.Quad(hand, Mm.T(hr.Value.x, hr.Value.y), team, dim);
            if (hl != null && showL) B.Quad(hand, Mm.T(hl.Value.x, hl.Value.y), team, dim);
        }
        // тонкая полоска (поводья) из белого квадрата: от (x0, y0) к (x1, y1) в осях m, ширина w метров
        void Strip(Batch B, Aff m, float x0, float y0, float x1, float y1, float w, Color32 c)
        {
            float dx = x1 - x0, dy = y1 - y0, L = Mathf.Sqrt(dx * dx + dy * dy);
            B.Quad(men.Get("util/px"), m.T((x0 + x1) / 2, (y0 + y1) / 2).R(Mathf.Atan2(dx, -dy)).S(w / 0.1f, L / 0.1f), c, new Vector4(0, 0, 1, 0));
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
        internal static (Vector2? r, Vector2? l, bool showL) HandsOf(string weapon, float[] W, float[] Sh, int bowSt = 0, int xb = 0)
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
                // где его видели живым (сглаживание), отброс по удару 0,25–0,65 м за 0,3 с; у места удара — брызги
                float X0 = dd.X, Y0 = dd.Y;
                var va = dd.Man > 0 && vis != null ? vis[dd.Unit] : null;
                if (va != null && dd.Man < va.Length && va[dd.Man].T > 0) { X0 += va[dd.Man].Dx; Y0 += va[dd.Man].Dy; }
                float kd = dd.Part == 3 ? 0 : (0.25f + 0.4f * H(seed, 58)) * Ease(Mathf.Min(1, age / 0.3f));
                float X = X0 + Mathf.Cos(a) * kd, Y = Y0 + Mathf.Sin(a) * kd;
                if (age < 0.45f && dd.Part != 3) Spray(X0, Y0, a, age, seed, disc);
                float big = dd.Part == 3 ? 1.7f : dd.Part == 0 ? 0.65f : dd.Part == 2 ? 0.85f : 1;
                for (int k = 0; k < 3; k++)
                {
                    float r = (0.15f + 0.13f * H(seed, k)) * big * grow, off = (0.3f + 0.35f * H(seed, k + 7)) * grow;
                    float ox = Mathf.Cos(a) * off + (H(seed, k + 3) - 0.5f) * 0.3f, oy = Mathf.Sin(a) * off + (H(seed, k + 5) - 0.5f) * 0.3f;
                    decals.Quad(disc, Aff.At(X + ox, Y + oy).R(a).S(2 * r, 2 * r * (0.65f + 0.35f * H(seed, k + 9))), Blood, solid);
                }
                // павший с известным номером — в своём комплекте (тот же выбор, что у живого бойца)
                var u = kits[dd.Unit]; var kit = dd.Man > 0 ? u[(int)(H(rec.Units[dd.Unit].Id * 7919 + dd.Man, 4) * u.Length)] : u[(int)(H(seed, 50) * u.Length)];
                var col = unitCol[dd.Unit];
                bool rider = dd.Part == 3 && kit.Horse, wounded = !dd.Killed && !rider;
                // удар (В13): первые 0,15 с боец ещё стоит — его качнуло по удару; потом падает
                if (age < 0.15f && !rider)
                {
                    float k2 = age / 0.15f, d = dd.Dir * Mathf.Deg2Rad;
                    float sx = X + Mathf.Cos(d) * 0.05f * k2, sy = Y + Mathf.Sin(d) * 0.05f * k2;
                    var (W, Sh) = Rest(kit, 0, 0);
                    var sm = Aff.At(sx, sy).R(dd.Facing * Mathf.Deg2Rad + (H(seed, 55) - 0.5f) * 0.6f * k2);
                    Flat(kit, sm, sm, 0, kit.Weapon, W, Sh, 0, 0, 0, false, false, false, col, Vector4.zero);
                    continue;
                }
                int v = (int)(H(seed, 51) * 2);
                float p = Mathf.Min(1, (age - 0.15f) / 0.35f), e = 1 - (1 - p) * (1 - p);
                var dp = new Vector4(0, wounded ? 0 : 0.38f, 0, 0); var dpt = new Vector4(0, dp.y, 0, 1);   // раненый — краски живые
                var fm = Aff.At(X, Y).R(a + Mathf.PI / 2 + (H(seed, 52) - 0.5f) * 0.5f);
                if (rider)
                {
                    // убитый конь лежит; всадник слетает с него (В13): за 0,55 с — в сторону от туши, в полёте крупнее
                    float he = 1 - Mathf.Pow(1 - Mathf.Min(1, age / 0.5f), 2);
                    corpses.Quad(dead.Get(kit.DeadHorse), fm.S(1, 0.4f + 0.6f * he).T(0, -0.6f), col, new Vector4(0, 0.38f, 0, 0));
                    float fl = Mathf.Min(1, age / 0.55f), k3 = 1 + 0.3f * Mathf.Sin(fl * Mathf.PI);
                    fm = fm.T(-0.9f * Ease(fl), 0.3f * Ease(fl)).S(k3, k3);
                }
                float flip = 1;
                if (wounded)
                {
                    // раненый (Г39, В13) бросил оружие и щит, где упал; ползёт рывками прочь от врага (0,32 м за 0,9 с) с кровавым
                    // следом — или корчится на месте; через 4–12 с затихает
                    string w = kit.Base == "bow" || kit.Weapon == "crossbow" ? kit.Side : kit.Weapon;
                    if (w != null && w != "none") deadTop.Quad(men.Get("weapon/" + w), fm.T(0.45f, -0.5f).R(H(seed, 56) * 6.283f).S(LyingScale(w), LyingScale(w)), col, dp);
                    if (kit.ShieldFlat != null) deadTop.Quad(men.Get(kit.ShieldFlat), fm.T(-0.5f, -0.4f).R(H(seed, 57) * 6.283f).S(0.85f, 0.85f), Col(kit.ShieldCol, col), dp);
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
                corpses.Quad(dead.Get(wounded ? $"crawl/{kit.LayoutKey}" : $"corpse/{kit.LayoutKey}/{v}"), cm, Col(kit.BodyCol, col), dp);
                deadTop.Quad(men.Get(kit.Head), cm.T(0, -0.56f), kit.HeadTint ? Col(kit.HeadCol, col) : col, kit.HeadTint ? dpt : dp);
                if (!wounded)
                {
                    string w = kit.Base == "bow" || kit.Weapon == "crossbow" ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
                    var wm = cm.T(0.45f + 0.15f * H(v * 131 + 7, 5), -0.25f + 0.5f * H(v * 131 + 7, 6)).R(H(v * 131 + 7, 7) * 6.283f);
                    if (w != null) deadTop.Quad(men.Get("weapon/" + w), wm.S(LyingScale(w), LyingScale(w)), col, dp);
                    else if (kit.BowPart != null) deadTop.Quad(men.Get(kit.BowPart + "0"), wm, col, dp);
                    if (kit.ShieldFlat != null) deadTop.Quad(men.Get(kit.ShieldFlat), cm.T(-0.48f - 0.15f * H(v * 131 + 7, 8), -0.1f + 0.4f * H(v * 131 + 7, 9)).R(H(v * 131 + 7, 10) * 6.283f).S(0.85f, 0.85f), Col(kit.ShieldCol, col), dp);
                    if (dd.Part == 0 && p >= 1) decals.Quad(disc, cm.T(0, -0.56f).S(0.2f, 0.2f), new Color32(123, 18, 18, 204), solid);
                }
            }
        }

        // брызги удара (Iron Kings): 7 капель разлетаются по удару за 0,45 с и гаснут; первые 0,1 с — красная вспышка
        void Spray(float x, float y, float a, float age, int seed, Part disc)
        {
            float u = age / 0.45f, fly = 1 - (1 - u) * (1 - u); var solid = new Vector4(0, 0, 1, 0);
            for (int k = 0; k < 7; k++)
            {
                float ang = a + (H(seed, 70 + k) - 0.5f) * 1.1f, dist = (0.3f + 1.1f * H(seed, 77 + k)) * fly, r = (0.04f + 0.05f * H(seed, 84 + k)) * (1 - 0.5f * u);
                air.Quad(disc, Aff.At(x + Mathf.Cos(ang) * dist, y + Mathf.Sin(ang) * dist).S(2 * r, 2 * r), new Color32(150, 20, 20, (byte)(255 * (1 - u * u))), solid);
            }
            if (age < 0.1f) { float e = age / 0.1f, k2 = 0.6f + 0.9f * e; air.Quad(men.Get("util/spark"), Aff.At(x, y).R(a + Mathf.PI / 2).S(k2, k2), new Color32(255, 70, 50, (byte)(230 * (1 - e))), solid); }
        }

        // лежащее оружие сверху короче: пика и копьё лежат, видны целиком, но рисунок рассчитан на стоящих — ужимаем
        static float LyingScale(string w) { var b = Kits.Base(w); return b == "pike" || b == "lance" ? 0.4f : b == "spear" || b == "fork" ? 0.7f : 0.9f; }

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
                        string w = kit.Base == "bow" || kit.Weapon == "crossbow" ? (kit.Side != "none" ? kit.Side : null) : kit.Weapon;
                        var at = Aff.At(x, y).R(H(seed, 22) * 6.283f);
                        if (w != null) deadTop.Quad(men.Get("weapon/" + w), at.S(0.9f, Kits.Base(w) == "pike" ? 0.5f : 0.9f), unitCol[ui], dp);
                        else if (kit.BowPart != null) deadTop.Quad(men.Get(kit.BowPart + "0"), at, unitCol[ui], dp);
                    }
                }
            }
        }

        // вода под точкой: глубокая (7), брод (8), ров (16)
        bool Wet(float x, float y)
        {
            var mp = rec.Map; if (mp == null) return false;
            int cx = (int)(x / mp.Cell), cy = (int)(y / mp.Cell); if (cx < 0 || cy < 0 || cx >= mp.W || cy >= mp.H) return false;
            int k = mp.T[cy * mp.W + cx]; return k == 7 || k == 8 || k == 16;
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
                    if (ar.T1 > t || ar.End > 2 && ar.End != 5 || !In(ar.X1, ar.Y1, 1)) continue;
                    if (ar.End == 5)
                    {
                        // в постройку (Г103): торчит из стены назад, к стрелку — настильная длинно, с крыши (круто сверху) коротко; оперение на конце
                        ArrowAt(ar, ar.T1 - 0.02f, out _, out _, out _, out var fa, out var fp);
                        float Lb = Mathf.Max(0.18f, 0.7f * Mathf.Cos(fp)), cx = ar.X1 - Mathf.Cos(fa) * Lb / 2, cy = ar.Y1 - Mathf.Sin(fa) * Lb / 2;
                        var mb = Aff.At(cx, cy).R(fa + Mathf.PI / 2);
                        deadTop.Quad(px, mb.S(0.4f * thin, Lb * 10), shaft, solid);
                        if (ppm >= 8) deadTop.Quad(px, Aff.At(ar.X1 - Mathf.Cos(fa) * Lb * 0.9f, ar.Y1 - Mathf.Sin(fa) * Lb * 0.9f).R(fa + Mathf.PI / 2).S(0.9f, 1.3f), fletch, solid);
                        continue;
                    }
                    if (Wet(ar.X1, ar.Y1)) continue;   // в воду, брод или ров — ушла под воду, не торчит
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
