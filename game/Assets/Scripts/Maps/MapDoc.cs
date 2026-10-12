// ═══════════ MapDoc.cs — карта редактора: кисть по клеткам + пути и участки (Г102, как Canvas of Kings) ═══════════
// Два слоя. Кисть — основа: вид местности и высота по клеткам 5 м, как в трекере. Поверх — объекты, которые остаются
// правимыми: путь (стена, частокол, река, дорога, ров, окоп) — ломаная, вдоль неё всё ставится само (у стены — башни
// на углах и через Every метров, ворота там, где их отметили); участок (лес, поле, болото, город, деревня, донжон, холм…)
// — обводка, внутри всё раскладывается само по своему зерну. Bake собирает из основы и объектов обычную карту движка
// (TerrainMap) — ту же, что в сохранениях трекера. Без UnityEngine: генераторы и проверки — где угодно.
// Порядок сборки: участки местности → холмы → поселения → реки, рвы, окопы → дороги (через воду — мост) →
// стены и частоколы → башни → ворота.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BattleCore;
using Newtonsoft.Json.Linq;

namespace Journal.Maps
{
    public sealed class MapFeature
    {
        public string Kind;                                   // см. PathKinds / PlotKinds
        public List<double[]> Pts = new List<double[]>();     // метры карты (x вправо, y вниз)
        public bool Closed;                                    // путь замкнут (кольцо стен); участок замкнут всегда
        public int Seed = 1;                                   // раскладка внутри участка (дома, улицы)
        public double Width;                                   // путь: ширина, м (0 — по виду)
        public double Every;                                   // стена, частокол: башни через, м (0 — по виду, <0 — без)
        public int Level = 1;                                  // холм: уровень высоты 1…3
        public List<double> Gates = new List<double>();        // ворота: расстояние вдоль пути от начала, м
        public bool PtTowers;                                  // стена, частокол: башня в каждой точке (Алекс 11.10: «каждая башня — точка, её можно передвинуть»)
        public string Prop;                                    // постройка (Kind «prop»): вид из PropKinds; Pts[0] — середина
        public int Rot;                                        // постройка: 1 — повёрнута на 90°
        public int PW, PH;                                     // постройка: свой размер в клетках (0 — по каталогу; дома разобранного участка)

        // постройки поштучно (Алекс 11.10: «взять отдельно стоящий домик, который понравился»): размер в клетках 5 м,
        // что ставят в клетки (здание, башня, лес, скалы или ничего — только рисунок) и ориентир для рисунка (церковь, ратуша…)
        public static readonly (string Id, string Name, int W, int H, string Cell, string Mark)[] PropKinds =
        {
            ("hut", "Изба", 2, 2, "building", null), ("house", "Дом", 2, 3, "building", null), ("longhouse", "Длинный дом", 2, 5, "building", null),
            ("barn", "Амбар", 3, 4, "building", "barn"), ("manor", "Усадьба", 4, 5, "building", "manor"), ("mill", "Мельница", 2, 2, "building", "mill"),
            ("church", "Церковь", 3, 7, "building", "church"), ("cathedral", "Собор", 6, 11, "building", "cathedral"), ("townhall", "Ратуша", 5, 4, "building", "townhall"),
            ("market", "Торговые ряды", 5, 5, null, "market"), ("well", "Колодец", 1, 1, null, "well"), ("tents", "Шатры", 4, 4, null, "tents"),
            ("tower", "Башня", 3, 3, "tower", null), ("tree", "Дерево", 1, 1, "forest", null), ("rock", "Валун", 1, 1, "rocks", null),
        };
        public static int PropIndex(string id) => Math.Max(0, Array.FindIndex(PropKinds, p => p.Id == id));
        // клетки постройки: левый верхний угол и размер (с поворотом)
        public (int x0, int y0, int w, int h) PropCells(double cell)
        {
            var k = PropKinds[PropIndex(Prop)]; int w0 = PW > 0 ? PW : k.W, h0 = PH > 0 ? PH : k.H, w = Rot == 1 ? h0 : w0, h = Rot == 1 ? w0 : h0;
            double cx = Pts.Count > 0 ? Pts[0][0] : 0, cy = Pts.Count > 0 ? Pts[0][1] : 0;
            return ((int)Math.Round(cx / cell - w / 2.0), (int)Math.Round(cy / cell - h / 2.0), w, h);
        }

        public static readonly (string Kind, string Name)[] PathKinds =
        {
            ("wall", "Каменная стена"), ("palisade", "Частокол"), ("river", "Река"), ("road", "Дорога"), ("moat", "Ров"), ("trench", "Окоп / вал"),
        };
        public static readonly (string Kind, string Name)[] PlotKinds =
        {
            ("town", "Город"), ("village", "Деревня"), ("keep", "Донжон"), ("forest", "Лес"), ("shrub", "Кустарник"), ("field", "Поле"),
            ("swamp", "Болото"), ("water", "Озеро"), ("rocks", "Скалы"), ("sand", "Песок"), ("pavement", "Площадь"), ("hill", "Холм"),
        };
        public bool IsPath => PathKinds.Any(p => p.Kind == Kind);
        public bool IsProp => Kind == "prop";
        public bool IsWall => Kind == "wall" || Kind == "palisade";
        public static string NameOf(string kind) => kind == "prop" ? "Постройка" : PathKinds.Concat(PlotKinds).FirstOrDefault(p => p.Kind == kind).Name ?? kind;
        public string Title => IsProp ? PropKinds[PropIndex(Prop)].Name : NameOf(Kind);
        public double DefWidth => Kind switch { "river" => 20, "road" => 5, "moat" => 10, "trench" => 5, _ => 5 };
        public double DefEvery => Kind switch { "wall" => 45, "palisade" => 35, _ => 0 };
        public double W => Width > 0 ? Width : DefWidth;
        public double E => Every != 0 ? Every : DefEvery;

        // ломаная пути как отрезки (замкнутая — с последним к первому)
        public IEnumerable<(double[] a, double[] b)> Segs()
        {
            for (int i = 0; i + 1 < Pts.Count; i++) yield return (Pts[i], Pts[i + 1]);
            if (Closed && Pts.Count > 2) yield return (Pts[Pts.Count - 1], Pts[0]);
        }
        public double Length() => Segs().Sum(s => Hyp(s.b[0] - s.a[0], s.b[1] - s.a[1]));
        // точка на пути в s метрах от начала и направление (единичное)
        public (double x, double y, double tx, double ty) At(double s)
        {
            foreach (var (a, b) in Segs())
            {
                double L = Hyp(b[0] - a[0], b[1] - a[1]);
                if (L < 1e-9) continue;
                if (s <= L) { double k = s / L; return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, (b[0] - a[0]) / L, (b[1] - a[1]) / L); }
                s -= L;
            }
            var last = Closed ? Pts[0] : Pts[Pts.Count - 1]; var prev = Closed ? Pts[Pts.Count - 1] : Pts[Math.Max(0, Pts.Count - 2)];
            double l2 = Math.Max(1e-9, Hyp(last[0] - prev[0], last[1] - prev[1]));
            return (last[0], last[1], (last[0] - prev[0]) / l2, (last[1] - prev[1]) / l2);
        }
        // ближайшая к точке позиция на пути: (s, расстояние)
        public (double s, double d) Nearest(double x, double y)
        {
            double acc = 0, bestS = 0, bestD = double.MaxValue;
            foreach (var (a, b) in Segs())
            {
                double dx = b[0] - a[0], dy = b[1] - a[1], L2 = dx * dx + dy * dy, L = Math.Sqrt(L2);
                double k = L2 > 0 ? Math.Max(0, Math.Min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / L2)) : 0;
                double d = Hyp(x - (a[0] + k * dx), y - (a[1] + k * dy));
                if (d < bestD) { bestD = d; bestS = acc + k * L; }
                acc += L;
            }
            return (bestS, bestD);
        }
        public MapFeature Copy() => new MapFeature { Kind = Kind, Pts = Pts.Select(p => (double[])p.Clone()).ToList(), Closed = Closed, Seed = Seed, Width = Width, Every = Every, Level = Level, Gates = new List<double>(Gates),
            PtTowers = PtTowers, Prop = Prop, Rot = Rot, PW = PW, PH = PH };
        // расстояния вдоль пути до точек ломаной (у замкнутой — без повтора первой)
        public List<double> VertexS()
        {
            var r = new List<double>(); double acc = 0;
            for (int i = 0; i < Pts.Count; i++) { if (i > 0) acc += Hyp(Pts[i][0] - Pts[i - 1][0], Pts[i][1] - Pts[i - 1][1]); r.Add(acc); }
            return r;
        }
        // ворота не у самого угла (Алекс 11.10, скрин: ворота на изломе — надвратные башни на разных отрезках, проём косой):
        // ближе minD к точке ломаной — на середину своего отрезка, если он длиннее 2·minD, иначе — на minD от точки внутрь отрезка
        public double AwayFromCorners(double s, double minD)
        {
            int n = Pts.Count; if (n < 2) return s;
            double L = Length(); var vs = VertexS(); if (Closed) vs.Add(L);
            for (int i = 0; i + 1 < vs.Count; i++)
            {
                double a = vs[i], b = vs[i + 1];
                if (s < a - 1e-9 || s > b + 1e-9) continue;
                double len = b - a;
                if (len >= 2 * minD) return Math.Max(a + minD, Math.Min(b - minD, s));
                return (a + b) / 2;
            }
            return s;
        }
        internal static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);
    }

    public sealed class MapDoc
    {
        public string Name = "Новая карта";
        public int W, H;                                       // клеток
        public double Cell = Terrain.CellM;
        public byte[] T, Z;                                    // кисть: основа под путями и участками
        public List<MapFeature> Features = new List<MapFeature>();
        public double WM => W * Cell;
        public double HM => H * Cell;

        public static MapDoc Blank(double wm, double hm, string fill = "field")
        {
            var m = Terrain.Create(wm, hm, Terrain.Id(fill));
            return new MapDoc { W = m.W, H = m.H, T = m.T, Z = m.Z };
        }
        // карта движка (из генератора или сохранения трекера) — в основу, объектов нет
        public static MapDoc FromTerrain(TerrainMap m, string name)
        {
            var t = (byte[])m.T.Clone(); for (int i = 0; i < t.Length; i++) if (t[i] == 0) t[i] = 1;
            return new MapDoc { Name = name, W = m.W, H = m.H, Cell = m.Cell, T = t, Z = (byte[])m.Z.Clone() };
        }
        public MapDoc Clone() => new MapDoc { Name = Name, W = W, H = H, Cell = Cell, T = (byte[])T.Clone(), Z = (byte[])Z.Clone(), Features = Features.Select(f => f.Copy()).ToList() };

        // ── сборка карты движка ──
        static byte K(string key) => Terrain.Id(key);
        static readonly HashSet<string> Ground = new HashSet<string> { "forest", "shrub", "field", "swamp", "water", "rocks", "sand", "pavement" };

        // ориентиры для рисунка (церковь, ратуша, ряды, колодец…): движку — клетки «здание», смотрелке — что за дом
        // (FortMap: Meta["landmarks"], строкой — переживает сохранение боя в JSON)
        readonly List<string> marks = new List<string>();
        void Mark(string kind, double x0, double y0, double x1, double y1, int seed)
        {
            var c = CultureInfo.InvariantCulture;
            marks.Add(string.Join(" ", kind, x0.ToString("0.##", c), y0.ToString("0.##", c), x1.ToString("0.##", c), y1.ToString("0.##", c), seed.ToString(c)));
        }
        public TerrainMap Bake()
        {
            var m = new TerrainMap { Cell = Cell, W = W, H = H, T = (byte[])T.Clone(), Z = (byte[])Z.Clone() };
            m.Meta = new Dictionary<string, object> { ["name"] = Name, ["editor"] = "Журнал боевых действий" };
            marks.Clear();
            foreach (var f in Features.Where(f => !f.IsPath && Ground.Contains(f.Kind) && f.Pts.Count >= 3)) FillPoly(m, f.Pts, (i, x, y) => m.T[i] = K(f.Kind));
            foreach (var f in Features.Where(f => f.Kind == "hill" && f.Pts.Count >= 3)) Hill(m, f);
            foreach (var f in Features.Where(f => f.Kind == "town" && f.Pts.Count >= 3)) Town(m, f);
            foreach (var f in Features.Where(f => f.Kind == "village" && f.Pts.Count >= 3)) Village(m, f);
            foreach (var f in Features.Where(f => f.Kind == "keep" && f.Pts.Count >= 3))
            {
                // донжон — каменная башня, не дома (Алекс 11.10): клетки — здание, рисунку — контур (FortView: кладка, зубцы, башенки)
                FillPoly(m, f.Pts, (i, x, y) => m.T[i] = K("building"));
                var kci = CultureInfo.InvariantCulture;
                Mark("keep", f.Pts.Min(p => p[0]), f.Pts.Min(p => p[1]), f.Pts.Max(p => p[0]), f.Pts.Max(p => p[1]), f.Seed);
                marks[marks.Count - 1] += " " + string.Join(",", f.Pts.Select(p => p[0].ToString("0.#", kci) + ":" + p[1].ToString("0.#", kci)));
            }
            foreach (var f in Features.Where(f => f.IsProp && f.Pts.Count >= 1)) Prop(m, f);
            foreach (var f in Features.Where(f => (f.Kind == "river" || f.Kind == "moat" || f.Kind == "trench") && f.Pts.Count >= 2))
                Thick(m, f, f.Kind == "river" ? K("water") : K(f.Kind), _ => true);
            foreach (var f in Features.Where(f => f.Kind == "road" && f.Pts.Count >= 2)) Road(m, f);
            var walls = Features.Where(f => (f.Kind == "wall" || f.Kind == "palisade") && f.Pts.Count >= 2).ToList();
            foreach (var f in walls) WallLine(m, f);
            foreach (var f in walls) Towers(m, f);
            foreach (var f in walls) GateCells(m, f);
            // ворота рисунку — как их поставили (Алекс 11.10: на косой стене клетки проёма лесенкой распадались на несколько
            // «ворот» по сторонам света, стена рвалась): середина, направление стены, ширина, материал
            var gs = new List<string>(); var ci = CultureInfo.InvariantCulture;
            foreach (var f in walls)
            {
                double L = f.Length();
                foreach (double g in f.Gates) { var (x, y, tx, ty) = f.At(Math.Max(0, Math.Min(L, g))); gs.Add(string.Join(" ", x.ToString("0.##", ci), y.ToString("0.##", ci), tx.ToString("0.####", ci), ty.ToString("0.####", ci), f.Kind == "wall" ? "1" : "2")); }
            }
            if (gs.Count > 0) m.Meta["gates"] = string.Join(";", gs);
            if (marks.Count > 0) m.Meta["landmarks"] = string.Join(";", marks);
            return m;
        }
        // постройка: клетки по виду (здание, башня, лес, скалы или ничего) и ориентир для рисунка
        void Prop(TerrainMap m, MapFeature f)
        {
            var k = MapFeature.PropKinds[MapFeature.PropIndex(f.Prop)];
            var (x0, y0, w, h) = f.PropCells(Cell);
            if (k.Cell != null)
            {
                byte v = K(k.Cell);
                for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < W && y < H) m.T[y * W + x] = v;
            }
            if (k.Mark != null) Mark(k.Mark + (f.Rot == 1 ? "/r" : ""), x0 * Cell, y0 * Cell, (x0 + w) * Cell, (y0 + h) * Cell, f.Seed);
        }
        // хэш клетки участка: раскладка зависит от места относительно участка, а не от карты — тянешь участок, дома едут
        // с ним (Алекс 11.10: «при перемещении деревня перегенерируется, этого быть не должно»)
        static double Hc(int seed, int x, int y, int k)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)y * 0xC2B2AE3Du ^ (uint)k * 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return h / 4294967296.0;
            }
        }
        // начало отсчёта раскладки — клетка первой точки участка
        (int ox, int oy) Origin(MapFeature f) => ((int)Math.Floor(f.Pts[0][0] / Cell), (int)Math.Floor(f.Pts[0][1] / Cell));
        // клетки внутри других стен (замок в городе, Алекс 11.10): там двор замка, а не кварталы города
        HashSet<int> InsideOtherWalls(TerrainMap m, MapFeature f)
        {
            var r = new HashSet<int>();
            double area = Area(f.Pts);
            foreach (var w in Features.Where(w => w.IsWall && w.Closed && w.Pts.Count >= 3 && Area(w.Pts) < area * 0.7))
                FillPoly(m, Offset(w.Pts, 8), (i, x, y) => r.Add(i));
            foreach (var k in Features.Where(k => k.Kind == "keep" && k.Pts.Count >= 3)) FillPoly(m, Offset(k.Pts, 5), (i, x, y) => r.Add(i));
            return r;
        }
        public static double Area(List<double[]> p)
        {
            double a = 0; for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u[0] * v[1] - v[0] * u[1]; }
            return Math.Abs(a) / 2;
        }
        // башни стены — в точки ломаной (кнопка «Башни — в точки»): по нынешним местам башен встают точки, дальше каждую
        // можно тянуть; ворота стоят по длине пути — точки на прямой её не меняют
        // точка стены без башни: третье число точки = 1 (снята «Правкой»: T над точкой или меню стены)
        public static bool NoTower(double[] p) => p.Length > 2 && p[2] == 1;
        public static void SetTower(MapFeature f, int i, bool on)
        {
            var p = f.Pts[i];
            f.Pts[i] = on ? new[] { p[0], p[1] } : new[] { p[0], p[1], 1.0 };
        }
        // башни только на углах (поворот больше 25°) и на концах — с прочих точек сняты
        public static void TowersOnCorners(MapFeature f)
        {
            int n = f.Pts.Count;
            for (int i = 0; i < n; i++)
            {
                bool end = !f.Closed && (i == 0 || i == n - 1), corner = end;
                if (!end)
                {
                    var p = f.Pts[(i - 1 + n) % n]; var c = f.Pts[i]; var q = f.Pts[(i + 1) % n];
                    double a1 = Math.Atan2(c[1] - p[1], c[0] - p[0]), a2 = Math.Atan2(q[1] - c[1], q[0] - c[0]);
                    corner = Math.Abs(Math.IEEERemainder(a2 - a1, 2 * Math.PI)) > 25 * Math.PI / 180;
                }
                SetTower(f, i, corner);
            }
        }
        public void TowersToPoints(MapFeature f)
        {
            if (!f.IsWall || f.Pts.Count < 2) return;
            double L = f.Length();
            var keep = TowerSpots(f, gateTowers: false).Select(t => f.Nearest(t.x, t.y).s).ToList();
            var vs = f.VertexS();
            var pts = new List<double[]>();
            int segs = f.Closed ? f.Pts.Count : f.Pts.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = f.Pts[i]; var b = f.Pts[(i + 1) % f.Pts.Count];
                double s0 = vs[i], s1 = i + 1 < vs.Count ? vs[i + 1] : L;
                pts.Add(a);
                foreach (double s in keep.Where(s => s > s0 + 3 && s < s1 - 3).OrderBy(s => s))
                {
                    double k = (s - s0) / Math.Max(1e-9, s1 - s0);
                    pts.Add(new[] { Math.Round(a[0] + (b[0] - a[0]) * k, 1), Math.Round(a[1] + (b[1] - a[1]) * k, 1) });
                }
            }
            if (!f.Closed) pts.Add(f.Pts[f.Pts.Count - 1]);
            f.Pts = pts; f.PtTowers = true; f.Every = -1;
        }

        // клетки, чей центр внутри многоугольника (метры)
        void FillPoly(TerrainMap m, List<double[]> poly, Action<int, int, int> act)
        {
            double x0 = poly.Min(p => p[0]) / Cell, x1 = poly.Max(p => p[0]) / Cell, y0 = poly.Min(p => p[1]) / Cell, y1 = poly.Max(p => p[1]) / Cell;
            for (int y = Math.Max(0, (int)Math.Floor(y0)); y <= Math.Min(H - 1, (int)Math.Ceiling(y1)); y++)
                for (int x = Math.Max(0, (int)Math.Floor(x0)); x <= Math.Min(W - 1, (int)Math.Ceiling(x1)); x++)
                    if (Inside(poly, (x + 0.5) * Cell, (y + 0.5) * Cell)) act(y * W + x, x, y);
        }
        public static bool Inside(List<double[]> poly, double px, double py)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double xi = poly[i][0], yi = poly[i][1], xj = poly[j][0], yj = poly[j][1];
                if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) c = !c;
            }
            return c;
        }
        // холм: внутри — уровень Level, полоса 10 м вокруг — уровнем ниже (склон)
        void Hill(TerrainMap m, MapFeature f)
        {
            int lv = Math.Max(1, Math.Min(Terrain.MaxHeight, f.Level));
            var outer = Offset(f.Pts, 10);
            if (lv > 1) FillPoly(m, outer, (i, x, y) => m.Z[i] = (byte)Math.Max(m.Z[i], lv - 1));
            FillPoly(m, f.Pts, (i, x, y) => m.Z[i] = (byte)Math.Max(m.Z[i], lv));
        }
        // толстый путь (река, ров, окоп): клетки не дальше полуширины от ломаной
        void Thick(TerrainMap m, MapFeature f, byte v, Func<byte, bool> over)
        {
            double r = Math.Max(0.5, f.W / 2 / Cell);
            foreach (var (a, b) in f.Segs())
                Terrain.PaintSegment(m, "t", a[0] / Cell, a[1] / Cell, b[0] / Cell, b[1] / Cell, r, v);
        }
        // дорога: поверх поля, леса, кустов, песка; через воду, ров и болото — мост; стены и дома не трогает
        void Road(TerrainMap m, MapFeature f)
        {
            double r = Math.Max(0.5, f.W / 2 / Cell);
            byte road = K("road"), bridge = K("bridge");
            var water = new HashSet<byte> { K("water"), K("moat"), K("swamp"), K("ford") };
            var keep = new HashSet<byte> { K("wall"), K("gate"), K("tower"), K("palisade"), K("building"), K("pavement"), K("bridge") };
            foreach (var (a, b) in f.Segs())
                Capsule(a[0] / Cell, a[1] / Cell, b[0] / Cell, b[1] / Cell, r, i =>
                {
                    byte t = m.T[i];
                    if (water.Contains(t)) m.T[i] = bridge; else if (!keep.Contains(t)) m.T[i] = road;
                });
        }
        void Capsule(double x0, double y0, double x1, double y1, double r, Action<int> act)
        {
            double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy, r2 = r * r;
            for (int y = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - r)); y <= Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, y1) + r)); y++)
                for (int x = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - r)); x <= Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, x1) + r)); x++)
                {
                    double px = x + 0.5, py = y + 0.5, k = len2 > 0 ? Math.Max(0, Math.Min(1, ((px - x0) * dx + (py - y0) * dy) / len2)) : 0;
                    double ex = px - (x0 + k * dx), ey = py - (y0 + k * dy);
                    if (ex * ex + ey * ey <= r2) act(y * W + x);
                }
        }
        // стена — клетки, через которые проходит ломаная, связно по сторонам (без щелей по диагонали)
        void WallLine(TerrainMap m, MapFeature f)
        {
            byte v = K(f.Kind);
            foreach (var (a, b) in f.Segs()) Supercover(a[0] / Cell, a[1] / Cell, b[0] / Cell, b[1] / Cell, (x, y) => { if (x >= 0 && y >= 0 && x < W && y < H) m.T[y * W + x] = v; });
        }
        static void Supercover(double x0, double y0, double x1, double y1, Action<int, int> f)
        {
            int x = (int)Math.Floor(x0), y = (int)Math.Floor(y0), xe = (int)Math.Floor(x1), ye = (int)Math.Floor(y1);
            double dx = x1 - x0, dy = y1 - y0; int sx = Math.Sign(dx), sy = Math.Sign(dy);
            double tdx = dx != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity, tdy = dy != 0 ? Math.Abs(1 / dy) : double.PositiveInfinity;
            double tmx = dx > 0 ? (Math.Floor(x0) + 1 - x0) * tdx : dx < 0 ? (x0 - Math.Floor(x0)) * tdx : double.PositiveInfinity;
            double tmy = dy > 0 ? (Math.Floor(y0) + 1 - y0) * tdy : dy < 0 ? (y0 - Math.Floor(y0)) * tdy : double.PositiveInfinity;
            f(x, y);
            // ровно |Δx| + |Δy| шагов; ось, что дошла до конца, больше не шагает (иначе из-за округления — мимо конца и дальше)
            for (int k = Math.Abs(xe - x) + Math.Abs(ye - y); k > 0; k--)
            {
                bool stepX = y == ye || x != xe && tmx < tmy;
                if (stepX) { tmx += tdx; x += sx; } else { tmy += tdy; y += sy; }
                f(x, y);
            }
        }
        // башни: на углах (поворот больше 25°), у концов незамкнутой стены и через Every метров; у ворот — нет (там
        // надвратные башни по бокам проёма). Каменная — 3 × 3 клетки (15 м), частокола — вышка 2 × 2
        public List<(double x, double y)> TowerSpots(MapFeature f, bool gateTowers = true)
        {
            var spots = new List<(double, double)>(); if (f.E < 0 && !f.PtTowers) return spots;
            int n = f.Pts.Count; if (n < 2) return spots;
            var corners = new List<double>(); double acc = 0;
            for (int i = 0; i < n; i++)
            {
                bool end = !f.Closed && (i == 0 || i == n - 1);
                if (i > 0) acc += MapFeature.Hyp(f.Pts[i][0] - f.Pts[i - 1][0], f.Pts[i][1] - f.Pts[i - 1][1]);
                if (f.PtTowers) { if (!NoTower(f.Pts[i])) corners.Add(acc); continue; }   // башни в точках — в каждой, кроме снятых
                if (end) { corners.Add(acc); continue; }
                var p = f.Pts[(i - 1 + n) % n]; var c = f.Pts[i]; var q = f.Pts[(i + 1) % n];
                double a1 = Math.Atan2(c[1] - p[1], c[0] - p[0]), a2 = Math.Atan2(q[1] - c[1], q[0] - c[0]);
                double turn = Math.Abs(Math.IEEERemainder(a2 - a1, 2 * Math.PI));
                if (turn > 25 * Math.PI / 180) corners.Add(acc);
            }
            double L = f.Length();
            if (f.Closed && corners.Count == 0 && !f.PtTowers) corners.Add(0);
            corners.Sort();
            var marks = new List<double>(corners);
            for (int k = 0; k < corners.Count; k++)
            {
                double s0 = corners[k], s1 = k + 1 < corners.Count ? corners[k + 1] : (f.Closed ? corners[0] + L : s0);
                double span = s1 - s0; if (span <= 0 || f.E <= 0) continue;
                int cnt = (int)Math.Floor(span / f.E);
                for (int j = 1; j <= cnt; j++) marks.Add((s0 + span * j / (cnt + 1)) % Math.Max(L, 1e-9));
            }
            // башни в точках (Алекс 11.10: «ставлю ворота на косую стену — рядом ещё две башни»): ближайшая точка с каждой
            // стороны ворот в 9…30 м — сама надвратная башня; своя надвратная — только где такой точки нет
            var gateSide = new HashSet<(double, int)>();
            if (f.PtTowers)
                foreach (double g in f.Gates)
                    foreach (int sd in new[] { -1, 1 })
                        if (marks.Any(s => { double d = SignedDist(g, s, L, f.Closed); return d * sd >= 9 && d * sd <= 30; })) gateSide.Add((g, sd));
            // башни не налезают друг на друга: ближе 18 м (каменная — 15 м в поперечнике) — одна
            if (f.PtTowers)
            {
                double minGap = f.Kind == "wall" ? 18 : 11;
                var sorted = marks.Distinct().OrderBy(x => x).ToList(); var kept = new List<double>();
                foreach (double sm in sorted) if (kept.Count == 0 || sm - kept[kept.Count - 1] >= minGap) kept.Add(sm);
                if (f.Closed && kept.Count > 1 && L - kept[kept.Count - 1] + kept[0] < minGap) kept.RemoveAt(kept.Count - 1);
                marks = kept;
            }
            foreach (double s in marks)
            {
                if (f.PtTowers ? f.Gates.Any(g => { double d = CircDist(g, s, L, f.Closed); return d < 9 || d < 27 && !gateSide.Contains((g, Math.Sign(SignedDist(g, s, L, f.Closed)))); })
                               : f.Gates.Any(g => CircDist(g, s, L, f.Closed) < (f.Kind == "wall" ? 27 : 14))) continue;   // надвратные башни — в 12,5 м, сами 15 м
                var (x, y, _, _) = f.At(s); spots.Add((x, y));
            }
            if (f.Kind == "wall" && gateTowers)
                foreach (double g in f.Gates)
                    foreach (int sd in new[] { -1, 1 }) { if (gateSide.Contains((g, sd))) continue; var (x, y, _, _) = f.At(Wrap(g + sd * 12.5, L, f.Closed)); spots.Add((x, y)); }
            return spots;
        }
        static double CircDist(double a, double b, double L, bool closed) { double d = Math.Abs(a - b); return closed && L > 0 ? Math.Min(d, L - d) : d; }
        // от a до b вдоль пути со знаком (у кольца — кратчайшим путём)
        static double SignedDist(double a, double b, double L, bool closed)
        {
            double d = b - a; if (closed && L > 0) { d = ((d % L) + L) % L; if (d > L / 2) d -= L; }
            return d;
        }
        static double Wrap(double s, double L, bool closed) => closed && L > 0 ? ((s % L) + L) % L : Math.Max(0, Math.Min(L, s));
        void Towers(TerrainMap m, MapFeature f)
        {
            byte tower = K("tower"); int size = f.Kind == "wall" ? 3 : 2;
            foreach (var (x, y) in TowerSpots(f))
            {
                int cx = (int)Math.Floor(x / Cell - size / 2.0 + 0.5), cy = (int)Math.Floor(y / Cell - size / 2.0 + 0.5);
                for (int dy = 0; dy < size; dy++) for (int dx = 0; dx < size; dx++) { int xx = cx + dx, yy = cy + dy; if (xx >= 0 && yy >= 0 && xx < W && yy < H) m.T[yy * W + xx] = tower; }
            }
        }
        // ворота: проём 10 м вдоль стены — клетки самой стены на линии в ±5 м от точки становятся воротами (башни — нет:
        // иначе проём выходит неровным и сдвигается наружу)
        void GateCells(TerrainMap m, MapFeature f)
        {
            byte gate = K("gate"), wall = K(f.Kind);
            double L = f.Length();
            foreach (double g in f.Gates)
                for (double d = -5; d <= 5; d += 0.25)
                {
                    var (x, y, _, _) = f.At(Wrap(g + d, L, f.Closed));
                    int cx = (int)Math.Floor(x / Cell), cy = (int)Math.Floor(y / Cell);
                    if (cx < 0 || cy < 0 || cx >= W || cy >= H) continue;
                    int i = cy * W + cx; if (m.T[i] == wall) m.T[i] = gate;
                }
        }
        // ── поселения ──
        // город: сетка улиц через 35 м (мостовая), площадь у середины с церковью, кварталы — дома с дворами
        // Алекс 11.10: «города скучные — ни ратуши, ни церкви, ни торговых рядов». Теперь у площади: собор (у большого города)
        // или церковь — с северной стороны, ратуша — с восточной, на площади — торговые ряды и колодец; у большого города
        // ещё приходские церкви по кварталам. Сетка улиц и дома — от первой точки участка: тянешь город — он едет целиком
        void Town(TerrainMap m, MapFeature f)
        {
            int seed = Math.Max(1, f.Seed);
            var cells = new HashSet<int>(); FillPoly(m, f.Pts, (i, x, y) => cells.Add(i));
            cells.ExceptWith(InsideOtherWalls(m, f));
            if (cells.Count == 0) return;
            byte pave = K("pavement"), bld = K("building");
            var (ox0, oy0) = Origin(f);
            int step = 7, ox = (int)(Hc(seed, 0, 0, 1) * step), oy = (int)(Hc(seed, 0, 0, 2) * step);
            bool Street(int x, int y) => ((x - ox0 + ox) % step + step) % step == 0 || ((y - oy0 + oy) % step + step) % step == 0;
            bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && cells.Contains(y * W + x);
            void Set(int x, int y, byte v) { if (In(x, y)) m.T[y * W + x] = v; }
            foreach (int i in cells) { int x = i % W, y = i / W; if (Street(x, y)) m.T[i] = pave; }
            bool big = cells.Count > 4200, mid = cells.Count > 1800;
            // площадь: у середины участка (по клеткам) — 8 × 8 у большого города, 7 × 7 у среднего, 6 × 6 у малого
            double cxm = cells.Average(i => i % W), cym = cells.Average(i => i / W);
            int sq = big ? 8 : mid ? 7 : 6, sx = (int)Math.Round(cxm) - sq / 2, sy = (int)Math.Round(cym) - sq / 2;
            for (int y = sy; y < sy + sq; y++) for (int x = sx; x < sx + sq; x++) Set(x, y, pave);
            // собор или церковь — к северу от площади, лицом к ней (не встаёт — к югу, к западу); вокруг — мостовая
            int cw = big ? 6 : 3, ch = big ? 11 : 7;
            bool Fits(int ax, int ay, int aw, int ah) => Enumerable.Range(ay - 1, ah + 2).All(y => Enumerable.Range(ax - 1, aw + 2).All(x => In(x, y)));
            foreach (var (cx0, cy0, ww, hh, rot) in new[] { (sx + (sq - cw) / 2, sy - ch - 1, cw, ch, false), (sx + (sq - cw) / 2, sy + sq + 1, cw, ch, false), (sx - ch - 1, sy + (sq - cw) / 2, ch, cw, true) })
            {
                if (!Fits(cx0, cy0, ww, hh)) continue;
                for (int y = cy0 - 1; y <= cy0 + hh; y++) for (int x = cx0 - 1; x <= cx0 + ww; x++) Set(x, y, pave);
                for (int y = cy0; y < cy0 + hh; y++) for (int x = cx0; x < cx0 + ww; x++) Set(x, y, bld);
                Mark((big ? "cathedral" : "church") + (rot ? "/r" : ""), cx0 * Cell, cy0 * Cell, (cx0 + ww) * Cell, (cy0 + hh) * Cell, seed);
                break;
            }
            // ратуша — к востоку от площади, длинной стороной к ней (у малого города — поменьше)
            int tw = mid ? 4 : 3, th = mid ? 5 : 4, tx0 = sx + sq + 1, ty0 = sy + (sq - th) / 2;
            for (int y = ty0 - 1; y <= ty0 + th; y++) for (int x = tx0 - 1; x <= tx0 + tw; x++) Set(x, y, pave);
            if (Enumerable.Range(ty0, th).All(y => Enumerable.Range(tx0, tw).Count(x => In(x, y)) == tw))
            {
                for (int y = ty0; y < ty0 + th; y++) for (int x = tx0; x < tx0 + tw; x++) Set(x, y, bld);
                Mark("townhall/r", tx0 * Cell, ty0 * Cell, (tx0 + tw) * Cell, (ty0 + th) * Cell, seed);
            }
            // на площади: ряды — палатки под навесами (рисунок, клетки — мостовая: проходимы), колодец посередине
            Mark("market", (sx + 1) * Cell, (sy + sq / 2 + 0.5) * Cell, (sx + sq - 1) * Cell, (sy + sq - 1) * Cell, seed);
            Mark("well", (sx + sq / 2 - 0.5) * Cell, (sy + sq / 2 - 1.5) * Cell, (sx + sq / 2 + 0.5) * Cell, (sy + sq / 2 - 0.5) * Cell, seed);
            // кварталы: дома 2 × 2, 2 × 3, 3 × 2 с проходом в клетку; кое-где — огород (пусто); у большого города — приходская
            // церковь на квартал из шести-семи
            int minX = cells.Min(i => i % W), maxX = cells.Max(i => i % W), minY = cells.Min(i => i / W), maxY = cells.Max(i => i / W);
            int bx0 = minX - ((minX - ox0 + ox) % step + step) % step, by0 = minY - ((minY - oy0 + oy) % step + step) % step;
            for (int by = by0; by <= maxY; by += step)
                for (int bx = bx0; bx <= maxX; bx += step)
                {
                    int qx = (bx - ox0) / step, qy = (by - oy0) / step;   // номер квартала от начала участка
                    if (big && Hc(seed, qx, qy, 9) < 0.07 && Math.Abs(bx - sx) > step * 2)
                    {
                        int px = bx + 2, py = by + 1, pw = 3, ph = 5;
                        bool ok = true;
                        for (int y = py; y < py + ph && ok; y++) for (int x = px; x < px + pw && ok; x++) ok = In(x, y) && m.T[y * W + x] != pave && m.T[y * W + x] != bld;
                        if (ok) { for (int y = py; y < py + ph; y++) for (int x = px; x < px + pw; x++) m.T[y * W + x] = bld; Mark("church", px * Cell, py * Cell, (px + pw) * Cell, (py + ph) * Cell, seed + qx * 31 + qy); continue; }
                    }
                    for (int ly = by + 1; ly < by + step - 1; ly += 3)
                        for (int lx = bx + 1; lx < bx + step - 1; lx += 3)
                        {
                            int lxx = lx - ox0, lyy = ly - oy0;
                            if (Hc(seed, lxx, lyy, 3) < 0.18) continue;
                            int w = Hc(seed, lxx, lyy, 4) < 0.5 ? 2 : 3, h = w == 3 ? 2 : (Hc(seed, lxx, lyy, 5) < 0.5 ? 2 : 3);
                            if (lx + w > bx + step - 1) w = bx + step - 1 - lx; if (ly + h > by + step - 1) h = by + step - 1 - ly;
                            if (w < 2 || h < 2) continue;
                            bool ok = true;
                            for (int y = ly; y < ly + h && ok; y++) for (int x = lx; x < lx + w && ok; x++) ok = In(x, y) && m.T[y * W + x] != pave && m.T[y * W + x] != bld;
                            if (!ok) continue;
                            for (int y = ly; y < ly + h; y++) for (int x = lx; x < lx + w; x++) m.T[y * W + x] = bld;
                        }
                }
        }
        // деревня: избы 2 × 2 и 2 × 3, иногда амбар или усадьба, по неровной сетке через 6 клеток от первой точки участка
        // (тянешь деревню — едет целиком; новая раскладка — только по «Перегенерировать»); не ближе двух клеток друг к другу
        void Village(TerrainMap m, MapFeature f)
        {
            int seed = Math.Max(1, f.Seed);
            var set = new HashSet<int>(); FillPoly(m, f.Pts, (i, x, y) => set.Add(i));
            set.ExceptWith(InsideOtherWalls(m, f));
            if (set.Count == 0) return;
            byte bld = K("building");
            var (ox0, oy0) = Origin(f);
            int minX = set.Min(i => i % W), maxX = set.Max(i => i % W), minY = set.Min(i => i / W), maxY = set.Max(i => i / W);
            const int step = 6;
            int gx0 = (int)Math.Floor((minX - ox0) / (double)step) - 1, gx1 = (int)Math.Ceiling((maxX - ox0) / (double)step) + 1;
            int gy0 = (int)Math.Floor((minY - oy0) / (double)step) - 1, gy1 = (int)Math.Ceiling((maxY - oy0) / (double)step) + 1;
            for (int gy = gy0; gy <= gy1; gy++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    if (Hc(seed, gx, gy, 1) < 0.3) continue;   // пустырь, огород
                    double r = Hc(seed, gx, gy, 2);
                    int w = 2, h = Hc(seed, gx, gy, 3) < 0.5 ? 2 : 3; string mk = null;
                    if (r < 0.06) { w = 4; h = 5; mk = "manor"; } else if (r < 0.16) { w = 3; h = 4; mk = "barn"; }
                    bool rot = Hc(seed, gx, gy, 4) < 0.5; if (rot) (w, h) = (h, w);
                    int x0 = ox0 + gx * step + (int)(Hc(seed, gx, gy, 5) * 3) - 1, y0 = oy0 + gy * step + (int)(Hc(seed, gx, gy, 6) * 3) - 1;
                    bool ok = true;
                    for (int y = y0 - 2; y < y0 + h + 2 && ok; y++) for (int x = x0 - 2; x < x0 + w + 2 && ok; x++)
                        {
                            bool core = x >= x0 && x < x0 + w && y >= y0 && y < y0 + h;
                            if (x < 0 || y < 0 || x >= W || y >= H) { ok = !core; continue; }
                            int i = y * W + x;
                            if (core && !set.Contains(i)) ok = false;
                            if (m.T[i] == bld) ok = false;
                        }
                    if (!ok) continue;
                    for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) m.T[y * W + x] = bld;
                    if (mk != null) Mark(mk + (rot ? "/r" : ""), x0 * Cell, y0 * Cell, (x0 + w) * Cell, (y0 + h) * Cell, seed + gx * 131 + gy);
                }
        }
        // дома участка (город, деревня) — отдельными постройками (кнопка «Разобрать на дома»): каждую можно тянуть, удалить
        public List<MapFeature> HousesOf(MapFeature f, List<int> pave = null)
        {
            var only = new MapDoc { Name = Name, W = W, H = H, Cell = Cell, T = new byte[T.Length], Z = new byte[Z.Length] };
            for (int i = 0; i < only.T.Length; i++) only.T[i] = 1;
            only.Features.AddRange(Features.Where(x => x != f && (x.IsWall || x.Kind == "keep")).Select(x => x.Copy()));
            only.Features.Add(f.Copy());
            var m = new TerrainMap { Cell = Cell, W = W, H = H, T = only.T, Z = only.Z };
            only.marks.Clear();
            if (f.Kind == "town") only.Town(m, f); else only.Village(m, f);
            byte bld = K("building");
            if (pave != null) { byte pv = K("pavement"); for (int i = 0; i < m.T.Length; i++) if (m.T[i] == pv) pave.Add(i); }
            var seen = new bool[W * H]; var r = new List<MapFeature>();
            var lm = only.marks.Select(s => s.Split(' ')).ToList();
            for (int s0 = 0; s0 < W * H; s0++)
            {
                if (m.T[s0] != bld || seen[s0]) continue;
                int x0 = s0 % W, y0 = s0 / W, x1 = x0, y1 = y0;
                while (x1 + 1 < W && m.T[y0 * W + x1 + 1] == bld && !seen[y0 * W + x1 + 1]) x1++;
                while (y1 + 1 < H && Enumerable.Range(x0, x1 - x0 + 1).All(x => m.T[(y1 + 1) * W + x] == bld && !seen[(y1 + 1) * W + x])) y1++;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) seen[y * W + x] = true;
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                double cx = (x0 + w / 2.0) * Cell, cy = (y0 + h / 2.0) * Cell;
                // какой ориентир здесь стоял — тем видом и ставим
                var hit = lm.FirstOrDefault(a => a.Length >= 5 && Math.Abs(double.Parse(a[1], CultureInfo.InvariantCulture) - x0 * Cell) < 0.1 && Math.Abs(double.Parse(a[2], CultureInfo.InvariantCulture) - y0 * Cell) < 0.1);
                string kind = hit != null ? hit[0].Replace("/r", "") : null;
                int pi = kind != null ? Array.FindIndex(MapFeature.PropKinds, p => p.Mark == kind) : -1;
                if (pi < 0) pi = Array.FindIndex(MapFeature.PropKinds, p => p.Mark == null && p.Cell == "building" && (p.W == Math.Min(w, h) && p.H == Math.Max(w, h)));
                if (pi < 0) { r.Add(new MapFeature { Kind = "prop", Prop = "house", PW = w, PH = h, Pts = new List<double[]> { new[] { cx, cy } }, Seed = f.Seed + s0 }); continue; }
                var k = MapFeature.PropKinds[pi];
                r.Add(new MapFeature { Kind = "prop", Prop = k.Id, Rot = k.W == w && k.H == h ? 0 : 1, Pts = new List<double[]> { new[] { cx, cy } }, Seed = f.Seed + s0 });
            }
            foreach (var a in lm.Where(a => a[0] == "market" || a[0] == "well"))
            {
                var c = CultureInfo.InvariantCulture; double x0 = double.Parse(a[1], c), y0 = double.Parse(a[2], c), x1 = double.Parse(a[3], c), y1 = double.Parse(a[4], c);
                r.Add(new MapFeature { Kind = "prop", Prop = a[0], Pts = new List<double[]> { new[] { (x0 + x1) / 2, (y0 + y1) / 2 } }, Seed = f.Seed });
            }
            return r;
        }
        // многоугольник, раздвинутый на d метров наружу (по биссектрисам углов; обход — любой)
        public static List<double[]> Offset(List<double[]> poly, double d)
        {
            int n = poly.Count; double area = 0;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a[0] * b[1] - b[0] * a[1]; }
            double s = area > 0 ? 1 : -1;   // «наружу» зависит от обхода (09.10.2026: знак был наоборот — ров замка и стены города уходили внутрь)
            var r = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                var p = poly[(i - 1 + n) % n]; var c = poly[i]; var q = poly[(i + 1) % n];
                double ax = c[0] - p[0], ay = c[1] - p[1], bx = q[0] - c[0], by = q[1] - c[1];
                double la = Math.Max(1e-9, MapFeature.Hyp(ax, ay)), lb = Math.Max(1e-9, MapFeature.Hyp(bx, by));
                double n1x = ay / la * s, n1y = -ax / la * s, n2x = by / lb * s, n2y = -bx / lb * s;
                double mx = n1x + n2x, my = n1y + n2y, ml = MapFeature.Hyp(mx, my);
                if (ml < 1e-6) { mx = n1x; my = n1y; ml = 1; }
                mx /= ml; my /= ml;
                double k = d / Math.Max(0.3, mx * n1x + my * n1y);
                r.Add(new[] { c[0] + mx * k, c[1] + my * k });
            }
            return r;
        }

        // ── файл .map.json: основа (как у трекера — повторы в base36) и объекты ──
        public string ToJson()
        {
            var feats = new JArray();
            foreach (var f in Features)
            {
                var o = new JObject { ["kind"] = f.Kind, ["pts"] = new JArray(f.Pts.Select(p => p.Length > 2 && p[2] != 0 ? new JArray(Math.Round(p[0], 2), Math.Round(p[1], 2), p[2]) : new JArray(Math.Round(p[0], 2), Math.Round(p[1], 2)))) };
                if (f.Closed) o["closed"] = true;
                if (f.Seed != 1) o["seed"] = f.Seed;
                if (f.Width > 0) o["width"] = f.Width;
                if (f.Every != 0) o["every"] = f.Every;
                if (f.Kind == "hill") o["level"] = f.Level;
                if (f.Gates.Count > 0) o["gates"] = new JArray(f.Gates.Select(g => Math.Round(g, 2)));
                if (f.PtTowers) o["ptTowers"] = true;
                if (f.Prop != null) o["prop"] = f.Prop;
                if (f.Rot != 0) o["rot"] = f.Rot;
                if (f.PW > 0) { o["pw"] = f.PW; o["ph"] = f.PH; }
                feats.Add(o);
            }
            var root = new JObject
            {
                ["format"] = "journal-map", ["v"] = 1, ["name"] = Name, ["cell"] = Cell, ["w"] = W, ["h"] = H,
                ["t"] = Terrain.EncodeLayer(T), ["z"] = Terrain.EncodeLayer(Z), ["features"] = feats,
            };
            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        public static MapDoc FromJson(string text)
        {
            var o = JObject.Parse(text);
            if ((string)o["format"] != "journal-map") throw new FormatException("это не карта редактора");
            int w = (int)o["w"], h = (int)o["h"];
            var d = new MapDoc { Name = (string)o["name"] ?? "Карта", W = w, H = h, Cell = (double?)o["cell"] ?? Terrain.CellM,
                T = Terrain.DecodeLayer((string)o["t"], w * h, 255), Z = Terrain.DecodeLayer((string)o["z"], w * h, Terrain.MaxHeight) };
            for (int i = 0; i < d.T.Length; i++) if (d.T[i] == 0) d.T[i] = 1;
            foreach (var fo in (JArray)o["features"] ?? new JArray())
            {
                var f = new MapFeature { Kind = (string)fo["kind"], Closed = (bool?)fo["closed"] ?? false, Seed = (int?)fo["seed"] ?? 1,
                    Width = (double?)fo["width"] ?? 0, Every = (double?)fo["every"] ?? 0, Level = (int?)fo["level"] ?? 1,
                    PtTowers = (bool?)fo["ptTowers"] ?? false, Prop = (string)fo["prop"], Rot = (int?)fo["rot"] ?? 0, PW = (int?)fo["pw"] ?? 0, PH = (int?)fo["ph"] ?? 0 };
                foreach (var p in (JArray)fo["pts"]) f.Pts.Add(((JArray)p).Count > 2 ? new[] { (double)p[0], (double)p[1], (double)p[2] } : new[] { (double)p[0], (double)p[1] });
                if (fo["gates"] is JArray g) f.Gates.AddRange(g.Select(x => (double)x));
                d.Features.Add(f);
            }
            return d;
        }
    }
}
