// ═══════════ MapGen.cs — генераторы карт (копия mapgen.js; К8, К9, К27, К28) ═══════════
// Шаблон — функция от настроек и зерна: одно зерно — одна карта, в трекере и в игре одинаковая до клетки.
// Поэтому здесь сохранён порядок всего, что влияет на результат: какие случайные числа берутся и когда
// (в том числе условия циклов, которые бросают кубик на каждом круге), порядок сложения, Float32 в шуме,
// синусы и hypot как в Node (JsMath). Менять генератор — только вместе с mapgen.js (правило 7).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class MapParamDef
    {
        public string Key, Name, Type;       // number / bool / select
        public object Def;
        public double Min, Max, Step;
        public string[][] Options;
    }

    public sealed class MapTemplate
    {
        public string Id, Name, Group;
        public List<MapParamDef> Params;
    }

    public static class MapGen
    {
        static MapParamDef Num(string key, string name, double def, double min, double max, double step = 1) =>
            new MapParamDef { Key = key, Name = name, Type = "number", Def = def, Min = min, Max = max, Step = step };
        static MapParamDef Bool(string key, string name, bool def) => new MapParamDef { Key = key, Name = name, Type = "bool", Def = def };
        static MapParamDef Sel(string key, string name, string def, params string[][] opts) =>
            new MapParamDef { Key = key, Name = name, Type = "select", Def = def, Options = opts };
        static string[] O(string v, string n) => new[] { v, n };
        static readonly string[][] Sides = { O("south", "юг"), O("north", "север"), O("west", "запад"), O("east", "восток") };
        static List<MapParamDef> Size(double w, double h) => new List<MapParamDef>
        {
            Num("widthM", "Ширина, м", w, 200, 7000, 50), Num("depthM", "Глубина, м", h, 200, 7000, 50),
        };
        static List<MapParamDef> Fort() => new List<MapParamDef>
        {
            Bool("hill", "На холме", false), Bool("moat", "Ров", true),
            Num("gates", "Ворот", 1, 1, 4), Sel("gateSide", "Главные ворота", "south", Sides),
        };
        static MapTemplate T(string id, string name, string group, List<MapParamDef> size, params MapParamDef[] rest) =>
            new MapTemplate { Id = id, Name = name, Group = group, Params = size.Concat(rest).ToList() };

        // Шаблоны первой очереди (К9, К34)
        public static readonly List<MapTemplate> Templates = new List<MapTemplate>
        {
            T("field", "Поле", "Поле", Size(2000, 1500),
              Sel("groves", "Рощи", "rare", O("none", "нет"), O("rare", "редко"), O("often", "часто")),
              Num("hills", "Холмы", 1, 0, 3), Bool("road", "Дорога", true)),
            T("forest", "Лес", "Поле", Size(2000, 1500),
              Sel("density", "Густота", "mid", O("low", "редкий"), O("mid", "средний"), O("high", "густой")),
              Num("clearings", "Поляны", 3, 0, 8), Bool("road", "Лесная дорога", true)),
            T("hills", "Холмы", "Поле", Size(2000, 1500),
              Num("count", "Холмов", 3, 1, 6), Num("maxHeight", "Наибольшая высота", 2, 1, 3), Bool("slopeForest", "Лес на склонах", true)),
            T("river", "Река", "Поле", Size(2000, 1500),
              Sel("width", "Ширина реки", "mid", O("narrow", "узкая, 15 м"), O("mid", "средняя, 30 м"), O("wide", "широкая, 60 м")),
              Sel("direction", "Течёт", "across", O("across", "поперёк поля"), O("along", "вдоль поля"), O("diagonal", "по диагонали")),
              Num("fords", "Бродов", 1, 0, 3), Num("bridges", "Мостов", 1, 0, 2)),
            T("desert", "Пустыня", "Поле", Size(2000, 1500),
              Bool("dunes", "Барханы", true), Bool("oasis", "Оазис", true), Bool("rocks", "Скалы", true)),
            T("palisade", "Частокол (острог)", "Крепость", Size(500, 400), Fort().ToArray()),
            T("castle", "Каменный замок", "Крепость", Size(600, 500), Fort().ToArray()),
            T("concentric", "Концентрический замок", "Крепость", Size(700, 600), Fort().ToArray()),
        };
        public static MapTemplate Get(string id) => Templates.FirstOrDefault(t => t.Id == id);

        // Настройки: значения по умолчанию + введённое, в пределах шаблона (как mapParams)
        public static Dictionary<string, object> Params(string id, IDictionary<string, object> input = null)
        {
            var t = Get(id) ?? throw new ArgumentException("нет такого шаблона карты: " + id);
            var p = new Dictionary<string, object>();
            foreach (var d in t.Params)
            {
                object v = null;
                bool has = input != null && input.TryGetValue(d.Key, out v);
                if (d.Type == "number")
                {
                    double num = double.NaN;
                    if (has && v != null)
                    {
                        if (v is string s) { if (s.Trim() != "" && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ps)) num = ps; }
                        else if (v is bool b) num = b ? 1 : 0;
                        else num = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    p[d.Key] = !double.IsNaN(num) && !double.IsInfinity(num) ? Math.Min(d.Max, Math.Max(d.Min, Js.Round(num))) : d.Def;
                }
                else if (d.Type == "bool") p[d.Key] = has ? Truthy(v) : d.Def;
                else p[d.Key] = has && v is string sv && d.Options.Any(o => o[0] == sv) ? sv : d.Def;
            }
            return p;
        }
        static bool Truthy(object v) => v switch
        {
            null => false, bool b => b, string s => s.Length > 0, double x => x != 0 && !double.IsNaN(x),
            int i => i != 0, long l => l != 0, _ => true,
        };

        public static TerrainMap Generate(string id, IDictionary<string, object> input, uint seed)
        {
            var t = Get(id) ?? throw new ArgumentException("нет такого шаблона карты: " + id);
            var p = Params(id, input);
            if (seed == 0) seed = 1;
            var rng = new Mulberry32(seed);
            Func<double> R = rng.Next;
            var m = Terrain.Create(D(p, "widthM"), D(p, "depthM"), id == "desert" ? K("sand") : K("field"));
            var g = new Gen(m, R, p);
            switch (id)
            {
                case "field": g.Field(); break;
                case "forest": g.Forest(); break;
                case "hills": g.Hills(); break;
                case "river": g.River(); break;
                case "desert": g.Desert(); break;
                case "palisade": g.Palisade(); break;
                case "castle": g.Castle(); break;
                case "concentric": g.Concentric(); break;
            }
            m.Meta = new Dictionary<string, object> { ["template"] = id, ["name"] = t.Name, ["params"] = p, ["seed"] = (double)seed };
            return m;
        }

        static double D(Dictionary<string, object> p, string k) => Convert.ToDouble(p[k], System.Globalization.CultureInfo.InvariantCulture);
        static byte K(string key) => Terrain.Id(key);

        // ── сами генераторы: одно поле, один генератор случайных чисел, одни настройки ──
        sealed class Gen
        {
            readonly TerrainMap m; readonly Func<double> rng; readonly Dictionary<string, object> p;
            public Gen(TerrainMap m, Func<double> rng, Dictionary<string, object> p) { this.m = m; this.rng = rng; this.p = p; }
            double Pn(string k) => D(p, k);
            bool Pb(string k) => (bool)p[k];
            string Ps(string k) => (string)p[k];

            static double Lerp(double a, double b, double t) => a + (b - a) * t;
            static double Smooth(double t) => t * t * (3 - 2 * t);
            int Rint(int a, int b) => a + (int)Math.Floor(rng() * (b - a + 1));
            int Cells => m.W * m.H;

            // Гладкий шум: случайные значения (Float32, как в трекере) в узлах решётки с шагом scale клеток
            Func<double, double, double> ValueNoise(double scale)
            {
                scale = Math.Max(1, scale);
                int gw = (int)Math.Ceiling(m.W / scale) + 2, gh = (int)Math.Ceiling(m.H / scale) + 2;
                var g = new float[gw * gh];
                for (int i = 0; i < g.Length; i++) g[i] = (float)rng();
                return (x, y) =>
                {
                    double fx = Math.Max(0, x) / scale, fy = Math.Max(0, y) / scale;
                    int x0 = (int)Math.Min(gw - 2, Math.Floor(fx)), y0 = (int)Math.Min(gh - 2, Math.Floor(fy));
                    double tx = Smooth(Math.Min(1, fx - x0)), ty = Smooth(Math.Min(1, fy - y0));
                    int i = y0 * gw + x0;
                    return Lerp(Lerp(g[i], g[i + 1], tx), Lerp(g[i + gw], g[i + gw + 1], tx), ty);
                };
            }
            Func<double, double, double> Fbm(double scale, int octaves = 3)
            {
                var ns = new List<Func<double, double, double>>();
                for (int o = 0; o < octaves; o++) ns.Add(ValueNoise(scale / (1 << o)));
                return (x, y) =>
                {
                    double s = 0, a = 1, tot = 0;
                    foreach (var n in ns) { s += n(x, y) * a; tot += a; a *= 0.5; }
                    return s / tot;
                };
            }
            void EachCell(Action<int, int, int> fn) { for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) fn(x, y, y * m.W + x); }

            // Пятно с неровным краем (роща, болото, скалы): радиус r клеток, край «гуляет» на rough долю радиуса
            void Blob(string layer, double cx, double cy, double r, int value, Func<double, double, double> noise, double rough = 0.35, byte[] only = null)
            {
                var arr = layer == "z" ? m.Z : m.T;
                for (int y = (int)Math.Floor(cy - r * 1.4); y <= cy + r * 1.4; y++)
                    for (int x = (int)Math.Floor(cx - r * 1.4); x <= cx + r * 1.4; x++)
                    {
                        if (x < 0 || y < 0 || x >= m.W || y >= m.H) continue;
                        double d = JsMath.Hypot(x + 0.5 - cx, y + 0.5 - cy) / r;
                        if (d > 1 + rough * (noise(x, y) - 0.5) * 2) continue;
                        int i = y * m.W + x;
                        if (only != null && Array.IndexOf(only, arr[i]) < 0) continue;
                        arr[i] = layer == "z" ? (byte)Math.Max(arr[i], value) : (byte)value;
                    }
            }

            // Извилистая линия от края до края: точки с плавным отклонением поперёк направления
            List<double[]> Wander(double[] from, double[] to, double amp, double waves, int steps = 48)
            {
                double dx = to[0] - from[0], dy = to[1] - from[1], len = JsMath.Hypot(dx, dy);
                double nx = -dy / len, ny = dx / len;
                double ph = rng() * Math.PI * 2, ph2 = rng() * Math.PI * 2;
                var pts = new List<double[]>();
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double off = amp * (JsMath.Sin(t * Math.PI * 2 * waves + ph) * 0.7 + JsMath.Sin(t * Math.PI * 2 * waves * 2.3 + ph2) * 0.3)
                               * JsMath.Sin(Math.PI * Math.Min(1, t * 1.2 + 0.05));
                    pts.Add(new[] { from[0] + dx * t + nx * off, from[1] + dy * t + ny * off });
                }
                return pts;
            }
            void StrokePath(List<double[]> pts, double r, int value)
            {
                for (int i = 1; i < pts.Count; i++) Terrain.PaintSegment(m, "t", pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], r, value);
            }
            void CrossRoad(bool vertical)
            {
                double[] from = vertical ? new[] { m.W * (0.3 + rng() * 0.4), -2.0 } : new[] { -2.0, m.H * (0.3 + rng() * 0.4) };
                double[] to = vertical ? new[] { m.W * (0.3 + rng() * 0.4), m.H + 2.0 } : new[] { m.W + 2.0, m.H * (0.3 + rng() * 0.4) };
                var pts = Wander(from, to, Math.Min(m.W, m.H) * 0.08, 1 + rng());
                StrokePath(pts, 1, K("road"));
            }
            void Groves(int count, double rMin, double rMax, Func<double, double, bool> keepOut = null)
            {
                var n = Fbm(8, 2);
                for (int k = 0; k < count; k++)
                {
                    double cx, cy; int tries = 0;
                    do { cx = rng() * m.W; cy = rng() * m.H; tries++; } while (keepOut != null && keepOut(cx, cy) && tries < 20);
                    double r = Lerp(rMin, rMax, rng());
                    Blob("t", cx, cy, r * 1.25, K("shrub"), n, 0.4, new[] { K("field"), K("sand") });
                    Blob("t", cx, cy, r, K("forest"), n, 0.4, new[] { K("field"), K("shrub"), K("sand") });
                }
            }
            void Hill(double cx, double cy, double r, int top, Func<double, double, double> noise)
            {
                for (int lvl = 1; lvl <= top; lvl++) Blob("z", cx, cy, r * (1 - (lvl - 1) / (top + 0.6)), lvl, noise, 0.3);
            }

            // ── поле ──
            public void Field()
            {
                var n = Fbm(30);
                double s = Math.Min(m.W, m.H);
                for (int k = 0; k < Pn("hills"); k++)
                {
                    double hx = rng() * m.W, hy = rng() * m.H, hr = s * Lerp(0.12, 0.22, rng());
                    Hill(hx, hy, hr, Rint(1, 2), n);
                }
                if (Pb("road")) CrossRoad(rng() < 0.5);
                double per = Ps("groves") == "none" ? 0 : Ps("groves") == "rare" ? 3 : 8;
                double count = Js.Round(per * Cells / 120000);
                if (count == 0) count = per != 0 ? 1 : 0;
                Groves((int)count, 6, 20);
            }
            // ── лес ──
            public void Forest()
            {
                double thr = Ps("density") == "low" ? 0.6 : Ps("density") == "mid" ? 0.5 : 0.4;
                var n = Fbm(40, 4);
                EachCell((x, y, i) =>
                {
                    double v = n(x, y);
                    if (v > thr) m.T[i] = K("forest");
                    else if (v > thr - 0.05) m.T[i] = K("shrub");
                });
                var e = Fbm(6, 2);
                for (int k = 0; k < Pn("clearings"); k++)
                {
                    double bx = rng() * m.W, by = rng() * m.H, br = Lerp(6, 16, rng());
                    Blob("t", bx, by, br, K("field"), e, 0.35);
                }
                if (Pb("road")) CrossRoad(rng() < 0.5);
            }
            // ── холмы ──
            public void Hills()
            {
                var n = Fbm(25);
                double s = Math.Min(m.W, m.H);
                for (int k = 0; k < Pn("count"); k++)
                {
                    int top = k == 0 ? (int)Pn("maxHeight") : Rint(1, (int)Pn("maxHeight"));
                    double hx = Lerp(0.12, 0.88, rng()) * m.W, hy = Lerp(0.12, 0.88, rng()) * m.H, hr = s * Lerp(0.12, 0.25, rng());
                    Hill(hx, hy, hr, top, n);
                }
                if (Pb("slopeForest"))
                {
                    var f = Fbm(10, 3);
                    EachCell((x, y, i) =>
                    {
                        if (m.Z[i] >= 1 && f(x, y) > 0.58) m.T[i] = K("forest");
                        else if (m.Z[i] >= 1 && f(x, y) > 0.53) m.T[i] = K("shrub");
                    });
                }
                Groves((int)Js.Round(2.0 * Cells / 120000), 5, 12);
            }
            // ── река ──
            public void River()
            {
                double r = Ps("width") == "narrow" ? 1.5 : Ps("width") == "mid" ? 3 : 6;   // 15 / 30 / 60 м
                double[] from, to;
                if (Ps("direction") == "across")
                {
                    from = new[] { -3.0, m.H * Lerp(0.35, 0.65, rng()) };
                    to = new[] { m.W + 3.0, m.H * Lerp(0.35, 0.65, rng()) };
                }
                else if (Ps("direction") == "along")
                {
                    from = new[] { m.W * Lerp(0.35, 0.65, rng()), -3.0 };
                    to = new[] { m.W * Lerp(0.35, 0.65, rng()), m.H + 3.0 };
                }
                else
                {
                    from = new[] { -3.0, m.H * Lerp(0.05, 0.25, rng()) };
                    to = new[] { m.W + 3.0, m.H * Lerp(0.75, 0.95, rng()) };
                }
                var pts = Wander(from, to, Math.Min(m.W, m.H) * 0.12, 1 + rng() * 1.5, 80);
                // берега: кустарник и рощицы вдоль воды
                var n = Fbm(6, 2);
                for (int i = 0; i < pts.Count; i++)
                    if (i % 6 == 0 && rng() < 0.5)
                    {
                        double br = r + Lerp(3, 8, rng());
                        Blob("t", pts[i][0], pts[i][1], br, K("shrub"), n, 0.5, new[] { K("field") });
                    }
                Groves((int)Js.Round(3.0 * Cells / 120000), 5, 14);
                // мосты: дорога поперёк реки до краёв карты
                int nb = (int)Pn("bridges"), nf = (int)Pn("fords");
                var spots = PickSpots(pts.Count, nb + nf);
                var bridges = spots.Take(nb).ToList(); var fords = spots.Skip(nb).ToList();
                foreach (int i in bridges)
                {
                    var (q, nx, ny) = RiverNormal(pts, i);
                    double far = Math.Max(m.W, m.H) * 2;
                    Terrain.PaintSegment(m, "t", q[0] - nx * far, q[1] - ny * far, q[0] + nx * far, q[1] + ny * far, 1, K("road"));
                }
                StrokePath(pts, r, K("water"));
                foreach (int i in bridges)
                {
                    var (q, nx, ny) = RiverNormal(pts, i);
                    Terrain.PaintSegment(m, "t", q[0] - nx * (r + 2), q[1] - ny * (r + 2), q[0] + nx * (r + 2), q[1] + ny * (r + 2), 1, K("bridge"));
                }
                foreach (int i in fords) ReplaceNear(pts[i][0], pts[i][1], r + 3, new[] { K("water") }, K("ford"));
            }
            List<int> PickSpots(int n, int k)
            {
                var outp = new List<int>();
                for (int j = 0; j < k; j++) outp.Add((int)Js.Round(n * (0.15 + 0.7 * (j + 0.3 + rng() * 0.4) / Math.Max(1, k))));
                return outp;
            }
            static (double[] q, double nx, double ny) RiverNormal(List<double[]> pts, int i)
            {
                var a = pts[Math.Max(0, i - 1)]; var b = pts[Math.Min(pts.Count - 1, i + 1)];
                double dx = b[0] - a[0], dy = b[1] - a[1], len = JsMath.Hypot(dx, dy);
                if (len == 0) len = 1;
                return (pts[i], -dy / len, dx / len);
            }
            void ReplaceNear(double cx, double cy, double r, byte[] from, byte to)
            {
                for (int y = (int)Math.Floor(cy - r); y <= cy + r; y++)
                    for (int x = (int)Math.Floor(cx - r); x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= m.W || y >= m.H) continue;
                        if (JsMath.Hypot(x + 0.5 - cx, y + 0.5 - cy) > r) continue;
                        int i = y * m.W + x;
                        if (Array.IndexOf(from, m.T[i]) >= 0) m.T[i] = to;
                    }
            }
            // ── пустыня ──
            public void Desert()
            {
                if (Pb("dunes"))
                {
                    // барханы — вытянутые гряды высотой 1 (К27)
                    var n = Fbm(20, 2);
                    double ang = rng() * Math.PI, ca = JsMath.Cos(ang), sa = JsMath.Sin(ang);
                    double lam = Lerp(9, 14, rng());
                    EachCell((x, y, i) => { if (JsMath.Sin((x * ca + y * sa) / lam + n(x, y) * 6) > 0.72) m.Z[i] = 1; });
                }
                if (Pb("rocks"))
                {
                    var n = Fbm(5, 2);
                    for (int k = 0; k < Math.Max(2, Js.Round(5.0 * Cells / 120000)); k++)
                    {
                        double bx = rng() * m.W, by = rng() * m.H, br = Lerp(2, 7, rng());
                        Blob("t", bx, by, br, K("rocks"), n, 0.5);
                    }
                }
                if (Pb("oasis"))
                {
                    double cx = m.W * Lerp(0.3, 0.7, rng()), cy = m.H * Lerp(0.3, 0.7, rng()), r = Lerp(4, 8, rng());
                    var n = Fbm(5, 2);
                    // в оазисе барханов нет
                    for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) if (JsMath.Hypot(x - cx, y - cy) < r * 3.2) m.Z[y * m.W + x] = 0;
                    Blob("t", cx, cy, r * 3.2, K("field"), n, 0.35);
                    Blob("t", cx, cy, r * 2.4, K("shrub"), n, 0.35);
                    Blob("t", cx, cy, r * 1.8, K("forest"), n, 0.35);
                    Blob("t", cx, cy, r, K("water"), n, 0.25);
                }
            }

            // ── крепости (К28) ──
            // Стена — клетки внутри формы, у которых хотя бы один из восьми соседей снаружи
            List<(int x, int y)> RingOf(Func<int, int, bool> inside)
            {
                var ring = new List<(int, int)>();
                EachCell((x, y, i) =>
                {
                    if (!inside(x, y)) return;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && !inside(x + dx, y + dy)) { ring.Add((x, y)); return; }
                });
                return ring;
            }
            static Func<int, int, bool> RectShape(double cx, double cy, double hw, double hh) =>
                (x, y) => Math.Abs(x + 0.5 - cx) <= hw && Math.Abs(y + 0.5 - cy) <= hh;
            static Func<int, int, bool> EllipseShape(double cx, double cy, double a, double b) =>
                (x, y) => { double u = (x + 0.5 - cx) / a, v = (y + 0.5 - cy) / b; return u * u + v * v <= 1; };
            void SetT(int x, int y, byte v) { if (x >= 0 && y >= 0 && x < m.W && y < m.H) m.T[y * m.W + x] = v; }
            void FillShape(Func<int, int, bool> shape, byte v) => EachCell((x, y, i) => { if (shape(x, y)) m.T[i] = v; });
            void Block(double cx, double cy, double hw, double hh, byte v)
            {
                for (int y = (int)Js.Round(cy - hh); y < Js.Round(cy + hh); y++)
                    for (int x = (int)Js.Round(cx - hw); x < Js.Round(cx + hw); x++) SetT(x, y, v);
            }
            // Порядок сторон для ворот: главная, противоположная, левая, правая
            List<string> GateSides()
            {
                var opp = new Dictionary<string, string> { ["south"] = "north", ["north"] = "south", ["west"] = "east", ["east"] = "west" };
                var left = new Dictionary<string, string> { ["south"] = "west", ["north"] = "east", ["west"] = "north", ["east"] = "south" };
                string s = Ps("gateSide");
                return new List<string> { s, opp[s], left[s], opp[left[s]] }.Take((int)Pn("gates")).ToList();
            }
            sealed class Side { public double X, Y, Nx, Ny; }
            static Side SidePoint(string side, double cx, double cy, double hw, double hh, double shift = 0)
            {
                switch (side)
                {
                    case "south": return new Side { X = cx + shift * hw, Y = cy + hh, Nx = 0, Ny = 1 };
                    case "north": return new Side { X = cx + shift * hw, Y = cy - hh, Nx = 0, Ny = -1 };
                    case "west": return new Side { X = cx - hw, Y = cy + shift * hh, Nx = -1, Ny = 0 };
                    default: return new Side { X = cx + hw, Y = cy + shift * hh, Nx = 1, Ny = 0 };
                }
            }
            // Ворота: клетки стены у точки — в ворота (проём 10 м); дорога от ворот к краю карты; мост через ров
            void MakeGate(Side g, int half, byte[] wallIds, bool roadTo)
            {
                double ax = g.Nx == 0 ? 1 : 0, ay = g.Nx == 0 ? 0 : 1;
                for (int d = -3; d <= 3; d++)
                    for (int s = -half; s < half; s++)
                    {
                        int x = (int)Math.Floor(g.X + ax * (s + 0.5) + g.Nx * d * 0.5), y = (int)Math.Floor(g.Y + ay * (s + 0.5) + g.Ny * d * 0.5);
                        if (x >= 0 && y >= 0 && x < m.W && y < m.H && Array.IndexOf(wallIds, m.T[y * m.W + x]) >= 0) m.T[y * m.W + x] = K("gate");
                    }
                if (roadTo)
                {
                    double far = Math.Max(m.W, m.H);
                    double ox = g.X + g.Nx * 1.5, oy = g.Y + g.Ny * 1.5;
                    PaintRoadOver(ox, oy, g.X + g.Nx * far, g.Y + g.Ny * far);
                }
            }
            // Дорога не стирает стены и ров: где ров — там мост
            void PaintRoadOver(double x0, double y0, double x1, double y1)
            {
                double len = JsMath.Hypot(x1 - x0, y1 - y0);
                int n = (int)Math.Ceiling(len * 2);
                for (int k = 0; k <= n; k++)
                {
                    double x = x0 + (x1 - x0) * k / n, y = y0 + (y1 - y0) * k / n;
                    for (int dy = -1; dy <= 0; dy++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            int cx = (int)Math.Floor(x + dx + 0.5), cy = (int)Math.Floor(y + dy + 0.5);
                            if (cx < 0 || cy < 0 || cx >= m.W || cy >= m.H) continue;
                            int i = cy * m.W + cx; byte t = m.T[i];
                            if (t == K("moat") || t == K("water")) m.T[i] = K("bridge");
                            else if (t == K("field") || t == K("shrub") || t == K("forest") || t == K("sand")) m.T[i] = K("road");
                        }
                }
            }
            (double cx, double cy) FortBase(double radius, int extra = 0)
            {
                double cx = m.W / 2.0, cy = m.H / 2.0;
                var n = Fbm(12, 2);
                if (Pb("hill")) Hill(cx, cy, radius + 22, 2 + extra, n);
                // пара рощ поодаль — лес, где у Кордуа нашлись лестницы
                Groves(3, 4, 10, (x, y) => JsMath.Hypot(x - cx, y - cy) < radius + 18);
                return (cx, cy);
            }
            void MoatAround(Func<int, int, bool> outer, Func<int, int, bool> inner) =>
                EachCell((x, y, i) => { if (outer(x, y) && !inner(x, y)) m.T[i] = K("moat"); });

            public void Palisade()
            {
                const double a = 15, b = 11;   // ~150 × 110 м
                var (cx, cy) = FortBase(a);
                if (Pb("moat")) MoatAround(EllipseShape(cx, cy, a + 5, b + 5), EllipseShape(cx, cy, a + 2, b + 2));
                var inside = EllipseShape(cx, cy, a, b);
                var ring = RingOf(inside);
                FillShape(inside, K("field"));
                foreach (var (x, y) in ring) SetT(x, y, K("palisade"));
                // вышки по кругу
                int towers = Rint(4, 6); double off = rng() * Math.PI;
                for (int k = 0; k < towers; k++)
                {
                    double t = off + k * Math.PI * 2 / towers;
                    Block(cx + JsMath.Cos(t) * (a - 0.5), cy + JsMath.Sin(t) * (b - 0.5), 1, 1, K("tower"));
                }
                // избы (число бросается заново на каждом круге — так в трекере)
                for (int k = 0; k < Rint(5, 9); k++)
                {
                    double t = rng() * Math.PI * 2, d = Math.Sqrt(rng()) * 0.65;
                    Block(cx + JsMath.Cos(t) * a * d, cy + JsMath.Sin(t) * b * d, 1, 1, K("building"));
                }
                foreach (var side in GateSides()) MakeGate(SidePoint(side, cx, cy, a - 0.5, b - 0.5), 1, new[] { K("palisade"), K("tower") }, true);
            }
            Func<int, int, bool> StoneWalls(double cx, double cy, double hw, double hh)
            {
                var inside = RectShape(cx, cy, hw, hh);
                var ring = RingOf(inside);
                FillShape(inside, K("pavement"));
                foreach (var (x, y) in ring) SetT(x, y, K("wall"));
                return inside;
            }
            void CornerTowers(double cx, double cy, double hw, double hh, bool mids)
            {
                foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) Block(cx + sx * hw, cy + sy * hh, 1.5, 1.5, K("tower"));
                if (mids) foreach (var (sx, sy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) }) Block(cx + sx * hw, cy + sy * hh, 1.5, 1.5, K("tower"));
            }
            // Надвратная башня: две башни по бокам проёма
            void Gatehouse(Side g)
            {
                double ax = g.Nx == 0 ? 1 : 0, ay = g.Nx == 0 ? 0 : 1;
                foreach (int s in new[] { -1, 1 }) Block(g.X + ax * s * 2.5 - g.Nx * 0.5, g.Y + ay * s * 2.5 - g.Ny * 0.5, 1.5, 1.5, K("tower"));
            }
            public void Castle()
            {
                const double hw = 12, hh = 9;   // 120 × 90 м
                var (cx, cy) = FortBase(hw);
                if (Pb("moat")) MoatAround(RectShape(cx, cy, hw + 6, hh + 6), RectShape(cx, cy, hw + 3, hh + 3));
                StoneWalls(cx, cy, hw, hh);
                CornerTowers(cx, cy, hw - 0.5, hh - 0.5, false);
                Block(cx + Lerp(-2, 2, rng()), cy - 1, 2.5, 2.5, K("building"));   // донжон
                for (int k = 0; k < Rint(2, 4); k++)   // казармы у стен; число бросается на каждом круге
                {
                    double bx = cx + Lerp(-hw + 4, hw - 4, rng()), by = cy + (rng() < 0.5 ? -hh + 2.5 : hh - 2.5);
                    Block(bx, by, 2, 1, K("building"));
                }
                var sides = GateSides();
                for (int i = 0; i < sides.Count; i++)
                {
                    var g = SidePoint(sides[i], cx, cy, hw - 0.5, hh - 0.5);
                    if (i == 0) Gatehouse(g);
                    MakeGate(g, 1, new[] { K("wall"), K("tower") }, true);
                }
            }
            public void Concentric()
            {
                const double ow = 16, oh = 13;   // внешнее кольцо 160 × 130 м
                const double gap = 4;            // полоса 20 м между кольцами (К28)
                double iw = ow - gap, ih = oh - gap;
                var (cx, cy) = FortBase(ow, 1);
                if (Pb("moat")) MoatAround(RectShape(cx, cy, ow + 6, oh + 6), RectShape(cx, cy, ow + 3, oh + 3));
                StoneWalls(cx, cy, ow, oh);
                FillShape(RectShape(cx, cy, ow - 1, oh - 1), K("field"));   // полоса под обстрелом
                var inner = StoneWalls(cx, cy, iw, ih);
                // внутреннее кольцо выше внешнего (К28): двор на уровень выше
                int baseZ = Pb("hill") ? 2 : 0;
                EachCell((x, y, i) => { if (inner(x, y)) m.Z[i] = (byte)Math.Max(m.Z[i], baseZ + 1); });
                CornerTowers(cx, cy, ow - 0.5, oh - 0.5, true);
                CornerTowers(cx, cy, iw - 0.5, ih - 0.5, false);
                Block(cx, cy, 3, 3, K("building"));   // донжон
                // ворота внешние и внутренние — не на одной линии
                foreach (var side in GateSides())
                {
                    var go = SidePoint(side, cx, cy, ow - 0.5, oh - 0.5, -0.45);
                    var gi = SidePoint(side, cx, cy, iw - 0.5, ih - 0.5, 0.45);
                    Gatehouse(go); Gatehouse(gi);
                    MakeGate(go, 1, new[] { K("wall"), K("tower") }, true);
                    MakeGate(gi, 1, new[] { K("wall"), K("tower") }, false);
                }
            }
        }
    }
}
