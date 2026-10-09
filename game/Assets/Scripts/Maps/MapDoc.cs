// ═══════════ MapDoc.cs — карта редактора: кисть по клеткам + пути и участки (Г100, как Canvas of Kings) ═══════════
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
        public static string NameOf(string kind) => PathKinds.Concat(PlotKinds).FirstOrDefault(p => p.Kind == kind).Name ?? kind;
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
        public MapFeature Copy() => new MapFeature { Kind = Kind, Pts = Pts.Select(p => (double[])p.Clone()).ToList(), Closed = Closed, Seed = Seed, Width = Width, Every = Every, Level = Level, Gates = new List<double>(Gates) };
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

        public TerrainMap Bake()
        {
            var m = new TerrainMap { Cell = Cell, W = W, H = H, T = (byte[])T.Clone(), Z = (byte[])Z.Clone() };
            m.Meta = new Dictionary<string, object> { ["name"] = Name, ["editor"] = "Журнал боевых действий" };
            foreach (var f in Features.Where(f => !f.IsPath && Ground.Contains(f.Kind) && f.Pts.Count >= 3)) FillPoly(m, f.Pts, (i, x, y) => m.T[i] = K(f.Kind));
            foreach (var f in Features.Where(f => f.Kind == "hill" && f.Pts.Count >= 3)) Hill(m, f);
            foreach (var f in Features.Where(f => f.Kind == "town" && f.Pts.Count >= 3)) Town(m, f);
            foreach (var f in Features.Where(f => f.Kind == "village" && f.Pts.Count >= 3)) Village(m, f);
            foreach (var f in Features.Where(f => f.Kind == "keep" && f.Pts.Count >= 3)) FillPoly(m, f.Pts, (i, x, y) => m.T[i] = K("building"));
            foreach (var f in Features.Where(f => (f.Kind == "river" || f.Kind == "moat" || f.Kind == "trench") && f.Pts.Count >= 2))
                Thick(m, f, f.Kind == "river" ? K("water") : K(f.Kind), _ => true);
            foreach (var f in Features.Where(f => f.Kind == "road" && f.Pts.Count >= 2)) Road(m, f);
            var walls = Features.Where(f => (f.Kind == "wall" || f.Kind == "palisade") && f.Pts.Count >= 2).ToList();
            foreach (var f in walls) WallLine(m, f);
            foreach (var f in walls) Towers(m, f);
            foreach (var f in walls) GateCells(m, f);
            return m;
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
        public List<(double x, double y)> TowerSpots(MapFeature f)
        {
            var spots = new List<(double, double)>(); if (f.E < 0) return spots;
            int n = f.Pts.Count; if (n < 2) return spots;
            var corners = new List<double>(); double acc = 0;
            for (int i = 0; i < n; i++)
            {
                bool end = !f.Closed && (i == 0 || i == n - 1);
                if (i > 0) acc += MapFeature.Hyp(f.Pts[i][0] - f.Pts[i - 1][0], f.Pts[i][1] - f.Pts[i - 1][1]);
                if (end) { corners.Add(acc); continue; }
                if (!f.Closed && (i == 0 || i == n - 1)) continue;
                var p = f.Pts[(i - 1 + n) % n]; var c = f.Pts[i]; var q = f.Pts[(i + 1) % n];
                double a1 = Math.Atan2(c[1] - p[1], c[0] - p[0]), a2 = Math.Atan2(q[1] - c[1], q[0] - c[0]);
                double turn = Math.Abs(Math.IEEERemainder(a2 - a1, 2 * Math.PI));
                if (turn > 25 * Math.PI / 180) corners.Add(acc);
            }
            double L = f.Length();
            if (f.Closed && corners.Count == 0) corners.Add(0);
            corners.Sort();
            var marks = new List<double>(corners);
            for (int k = 0; k < corners.Count; k++)
            {
                double s0 = corners[k], s1 = k + 1 < corners.Count ? corners[k + 1] : (f.Closed ? corners[0] + L : s0);
                double span = s1 - s0; if (span <= 0 || f.E <= 0) continue;
                int cnt = (int)Math.Floor(span / f.E);
                for (int j = 1; j <= cnt; j++) marks.Add((s0 + span * j / (cnt + 1)) % Math.Max(L, 1e-9));
            }
            foreach (double s in marks)
            {
                if (f.Gates.Any(g => CircDist(g, s, L, f.Closed) < 14)) continue;
                var (x, y, _, _) = f.At(s); spots.Add((x, y));
            }
            if (f.Kind == "wall")
                foreach (double g in f.Gates)
                    foreach (int sd in new[] { -1, 1 }) { var (x, y, _, _) = f.At(Wrap(g + sd * 10, L, f.Closed)); spots.Add((x, y)); }
            return spots;
        }
        static double CircDist(double a, double b, double L, bool closed) { double d = Math.Abs(a - b); return closed && L > 0 ? Math.Min(d, L - d) : d; }
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
        // ворота: проём 10 м вдоль стены — клетки стены и башен у точки становятся воротами
        void GateCells(TerrainMap m, MapFeature f)
        {
            byte gate = K("gate"), wall = K(f.Kind), tower = K("tower");
            foreach (double g in f.Gates)
            {
                var (x, y, tx, ty) = f.At(g); double nx = -ty, ny = tx;
                for (double along = -4.5; along <= 4.5; along += 1.25)
                    for (double across = -4; across <= 4; across += 1.25)
                    {
                        int cx = (int)Math.Floor((x + tx * along + nx * across) / Cell), cy = (int)Math.Floor((y + ty * along + ny * across) / Cell);
                        if (cx < 0 || cy < 0 || cx >= W || cy >= H) continue;
                        int i = cy * W + cx; if (m.T[i] == wall || m.T[i] == tower) m.T[i] = gate;
                    }
            }
        }
        // ── поселения ──
        // город: сетка улиц через 35 м (мостовая), площадь у середины с церковью, кварталы — дома с дворами
        void Town(TerrainMap m, MapFeature f)
        {
            var rng = new Mulberry32((uint)Math.Max(1, f.Seed)); Func<double> R = rng.Next;
            var cells = new HashSet<int>(); FillPoly(m, f.Pts, (i, x, y) => cells.Add(i));
            if (cells.Count == 0) return;
            byte pave = K("pavement"), bld = K("building");
            double cxm = f.Pts.Average(p => p[0]) / Cell, cym = f.Pts.Average(p => p[1]) / Cell;
            int step = 7, ox = (int)(R() * step), oy = (int)(R() * step);
            bool Street(int x, int y) => ((x + ox) % step == 0) || ((y + oy) % step == 0);
            foreach (int i in cells) { int x = i % W, y = i / W; if (Street(x, y)) m.T[i] = pave; }
            // площадь 6 × 6 и церковь 3 × 5 у её северного края
            int sx = (int)Math.Round(cxm) - 3, sy = (int)Math.Round(cym) - 3;
            for (int y = sy; y < sy + 6; y++) for (int x = sx; x < sx + 6; x++) { int i = y * W + x; if (x >= 0 && y >= 0 && x < W && y < H && cells.Contains(i)) m.T[i] = pave; }
            for (int y = sy - 5; y < sy; y++) for (int x = sx + 1; x < sx + 4; x++) { int i = y * W + x; if (x >= 0 && y >= 0 && x < W && y < H && cells.Contains(i)) m.T[i] = bld; }
            // кварталы: дома 2 × 2, 2 × 3, 3 × 2 с проходом в клетку; кое-где — огород (пусто)
            int minX = cells.Min(i => i % W), maxX = cells.Max(i => i % W), minY = cells.Min(i => i / W), maxY = cells.Max(i => i / W);
            for (int by = (minY / step) * step - oy; by <= maxY; by += step)
                for (int bx = (minX / step) * step - ox; bx <= maxX; bx += step)
                    for (int ly = by + 1; ly < by + step - 1; ly += 3)
                        for (int lx = bx + 1; lx < bx + step - 1; lx += 3)
                        {
                            if (R() < 0.18) continue;
                            int w = R() < 0.5 ? 2 : 3, h = w == 3 ? 2 : (R() < 0.5 ? 2 : 3);
                            if (lx + w > bx + step - 1) w = bx + step - 1 - lx; if (ly + h > by + step - 1) h = by + step - 1 - ly;
                            if (w < 2 || h < 2) continue;
                            bool ok = true;
                            for (int y = ly; y < ly + h && ok; y++) for (int x = lx; x < lx + w && ok; x++) { int i = y * W + x; ok = x >= 0 && y >= 0 && x < W && y < H && cells.Contains(i) && m.T[i] != pave && m.T[i] != bld; }
                            if (!ok) continue;
                            for (int y = ly; y < ly + h; y++) for (int x = lx; x < lx + w; x++) m.T[y * W + x] = bld;
                        }
        }
        // деревня: избы 2 × 2 и 2 × 3 вразброс, не ближе двух клеток друг к другу
        void Village(TerrainMap m, MapFeature f)
        {
            var rng = new Mulberry32((uint)Math.Max(1, f.Seed)); Func<double> R = rng.Next;
            var cells = new List<int>(); FillPoly(m, f.Pts, (i, x, y) => cells.Add(i));
            if (cells.Count == 0) return;
            var set = new HashSet<int>(cells); byte bld = K("building");
            int want = Math.Max(2, cells.Count / 70);
            for (int tries = 0, made = 0; tries < want * 20 && made < want; tries++)
            {
                int c = cells[(int)(R() * cells.Count)], x0 = c % W, y0 = c / W, w = 2, h = R() < 0.5 ? 2 : 3;
                if (R() < 0.5) (w, h) = (h, w);
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
                made++;
            }
        }
        // многоугольник, раздвинутый на d метров наружу (по биссектрисам углов; обход — любой)
        public static List<double[]> Offset(List<double[]> poly, double d)
        {
            int n = poly.Count; double area = 0;
            for (int i = 0; i < n; i++) { var a = poly[i]; var b = poly[(i + 1) % n]; area += a[0] * b[1] - b[0] * a[1]; }
            double s = area > 0 ? -1 : 1;   // y вниз: «наружу» зависит от обхода
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
                var o = new JObject { ["kind"] = f.Kind, ["pts"] = new JArray(f.Pts.Select(p => new JArray(Math.Round(p[0], 2), Math.Round(p[1], 2)))) };
                if (f.Closed) o["closed"] = true;
                if (f.Seed != 1) o["seed"] = f.Seed;
                if (f.Width > 0) o["width"] = f.Width;
                if (f.Every != 0) o["every"] = f.Every;
                if (f.Kind == "hill") o["level"] = f.Level;
                if (f.Gates.Count > 0) o["gates"] = new JArray(f.Gates.Select(g => Math.Round(g, 2)));
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
                    Width = (double?)fo["width"] ?? 0, Every = (double?)fo["every"] ?? 0, Level = (int?)fo["level"] ?? 1 };
                foreach (var p in (JArray)fo["pts"]) f.Pts.Add(new[] { (double)p[0], (double)p[1] });
                if (fo["gates"] is JArray g) f.Gates.AddRange(g.Select(x => (double)x));
                d.Features.Add(f);
            }
            return d;
        }
    }
}
