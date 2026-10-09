// ═══════════ MapMakers.cs — генераторы карт редактора (Г100): поле боя, замок, город, деревня ═══════════
// Поле, лес, холмы, река, пустыня — шаблоны движка (MapGen, как в трекере), они ложатся в основу кистью. Замок, город и
// деревня строятся объектами редактора (пути и участки) — после генерации их можно двигать и править, как нарисованные
// руками. Одно зерно — одна карта. Настройки — в виде MapParamDef, как у шаблонов движка: панель строит поля сама.
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

namespace Journal.Maps
{
    public static class MapMakers
    {
        static MapParamDef Num(string key, string name, double def, double min, double max, double step = 1) =>
            new MapParamDef { Key = key, Name = name, Type = "number", Def = def, Min = min, Max = max, Step = step };
        static MapParamDef Bool(string key, string name, bool def) => new MapParamDef { Key = key, Name = name, Type = "bool", Def = def };
        static MapParamDef Sel(string key, string name, string def, params string[][] opts) => new MapParamDef { Key = key, Name = name, Type = "select", Def = def, Options = opts };
        static string[] O(string v, string n) => new[] { v, n };
        static readonly string[][] Sides = { O("south", "юг"), O("north", "север"), O("west", "запад"), O("east", "восток") };
        static List<MapParamDef> Size(double w, double h) => new List<MapParamDef> { Num("widthM", "Ширина, м", w, 300, 7000, 50), Num("depthM", "Глубина, м", h, 300, 7000, 50) };

        // свои шаблоны: замок, город, деревня; поле боя — шаблоны движка группы «Поле»
        public static readonly List<MapTemplate> Own = new List<MapTemplate>
        {
            new MapTemplate { Id = "x-castle", Name = "Замок", Group = "Крепость", Params = Size(800, 700).Concat(new[]
            {
                Sel("size", "Размер", "mid", O("small", "малый, ~100 м"), O("mid", "средний, ~130 м"), O("large", "большой, ~180 м")),
                Sel("shape", "Очертание", "rect", O("rect", "прямоугольник"), O("poly", "многоугольник по местности")),
                Sel("material", "Стены", "stone", O("stone", "каменные"), O("wood", "частокол")),
                Num("rings", "Колец стен", 1, 1, 2), Num("every", "Башни через, м", 45, 25, 90, 5),
                Num("gates", "Ворот", 1, 1, 4), Sel("gateSide", "Главные ворота", "south", Sides),
                Bool("moat", "Ров", true), Bool("hill", "На холме", false), Bool("keep", "Донжон", true), Bool("houses", "Постройки во дворе", true),
                Bool("town", "Посад у стен", false), Bool("woods", "Рощи вокруг", true),
            }).ToList() },
            new MapTemplate { Id = "x-town", Name = "Город", Group = "Поселение", Params = Size(1400, 1100).Concat(new[]
            {
                Sel("size", "Размер", "mid", O("small", "малый, ~250 м"), O("mid", "средний, ~400 м"), O("large", "большой, ~600 м")),
                Bool("walls", "Городская стена", true), Num("gates", "Ворот", 3, 1, 4), Sel("gateSide", "Главные ворота", "south", Sides),
                Bool("castle", "Замок в городе", false), Bool("river", "Река рядом", true), Bool("villages", "Деревни вокруг", true), Bool("woods", "Рощи", true),
            }).ToList() },
            new MapTemplate { Id = "x-village", Name = "Деревня", Group = "Поселение", Params = Size(1200, 900).Concat(new[]
            {
                Num("count", "Деревень", 1, 1, 4), Bool("palisade", "Частокол вокруг", false), Bool("river", "Ручей", true), Bool("woods", "Лес вокруг", true),
            }).ToList() },
        };
        public static IEnumerable<MapTemplate> All() => MapGen.Templates.Where(t => t.Group == "Поле").Concat(Own);
        public static MapTemplate Get(string id) => All().FirstOrDefault(t => t.Id == id);

        public static Dictionary<string, object> Params(MapTemplate t, IDictionary<string, object> input)
        {
            var p = new Dictionary<string, object>();
            foreach (var d in t.Params)
            {
                object v = null; bool has = input != null && input.TryGetValue(d.Key, out v) && v != null;
                if (d.Type == "number") p[d.Key] = has ? Math.Min(d.Max, Math.Max(d.Min, Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture))) : d.Def;
                else if (d.Type == "bool") p[d.Key] = has ? v is bool b ? b : Convert.ToBoolean(v) : d.Def;
                else p[d.Key] = has && v is string s && d.Options.Any(o => o[0] == s) ? s : d.Def;
            }
            return p;
        }

        public static MapDoc Make(string id, IDictionary<string, object> input, uint seed)
        {
            if (seed == 0) seed = 1;
            var t = Get(id) ?? throw new ArgumentException("нет шаблона " + id);
            if (!id.StartsWith("x-")) return MapDoc.FromTerrain(MapGen.Generate(id, input, seed), t.Name);
            var p = Params(t, input);
            Func<double> R = new Mulberry32(seed).Next;
            var d = MapDoc.Blank(D(p, "widthM"), D(p, "depthM"));
            d.Name = t.Name;
            var mk = new Maker(d, R, p);
            switch (id)
            {
                case "x-castle": mk.Castle(d.WM / 2, d.HM / 2, true); break;
                case "x-town": mk.Town(); break;
                case "x-village": mk.Villages(); break;
            }
            return d;
        }
        static double D(Dictionary<string, object> p, string k) => Convert.ToDouble(p[k], System.Globalization.CultureInfo.InvariantCulture);

        sealed class Maker
        {
            readonly MapDoc d; readonly Func<double> R; readonly Dictionary<string, object> p;
            public Maker(MapDoc d, Func<double> r, Dictionary<string, object> p) { this.d = d; R = r; this.p = p; }
            double N(string k) => Convert.ToDouble(p[k], System.Globalization.CultureInfo.InvariantCulture);
            bool B(string k) => p.TryGetValue(k, out var v) && v is bool b && b;
            string S(string k) => (string)p[k];
            double Lerp(double a, double b, double t) => a + (b - a) * t;
            int Seed() => 1 + (int)(R() * 1e6);
            MapFeature Add(string kind, List<double[]> pts, bool closed = false)
            {
                var f = new MapFeature { Kind = kind, Pts = pts, Closed = closed, Seed = Seed() };
                d.Features.Add(f); return f;
            }
            static (double x, double y) Dir(string side) => side switch { "north" => (0, -1), "west" => (-1, 0), "east" => (1, 0), _ => (0, 1) };
            List<string> GateSides(int n)
            {
                var opp = new Dictionary<string, string> { ["south"] = "north", ["north"] = "south", ["west"] = "east", ["east"] = "west" };
                var left = new Dictionary<string, string> { ["south"] = "west", ["north"] = "east", ["west"] = "north", ["east"] = "south" };
                string s = S("gateSide");
                return new List<string> { s, opp[s], left[s], opp[left[s]] }.Take(n).ToList();
            }
            // кольцо: прямоугольник или неровный многоугольник вокруг (cx, cy) с полуосями (a, b)
            List<double[]> Ring(double cx, double cy, double a, double b, bool poly)
            {
                if (!poly) return new List<double[]> { new[] { cx - a, cy - b }, new[] { cx + a, cy - b }, new[] { cx + a, cy + b }, new[] { cx - a, cy + b } };
                int n = 6 + (int)(R() * 4); double off = R() * Math.PI * 2;
                return Enumerable.Range(0, n).Select(k =>
                {
                    double t = off + k * Math.PI * 2 / n + (R() - 0.5) * 0.35, rr = Lerp(0.85, 1.12, R());
                    return new[] { cx + Math.Cos(t) * a * rr, cy + Math.Sin(t) * b * rr };
                }).ToList();
            }
            // точка выхода луча из (cx, cy) в сторону side через кольцо — позиция на пути (для ворот)
            static double GateAt(MapFeature ring, double cx, double cy, string side, double shift = 0)
            {
                var (dx, dy) = Dir(side); double far = 5000;
                double px = cx + dx * far - dy * shift, py = cy + dy * far + dx * shift;
                // ближайшая к лучу точка пути — пересечение по отрезкам
                double best = double.MaxValue, bestS = 0, acc = 0;
                foreach (var (a, b) in ring.Segs())
                {
                    double L = MapFeature.Hyp(b[0] - a[0], b[1] - a[1]);
                    for (int k = 0; k <= 40; k++)
                    {
                        double x = a[0] + (b[0] - a[0]) * k / 40, y = a[1] + (b[1] - a[1]) * k / 40;
                        double rx = x - (cx - dy * shift), ry = y - (cy + dx * shift), along = rx * dx + ry * dy, perp = Math.Abs(-rx * dy + ry * dx);
                        if (along > 0 && perp < best) { best = perp; bestS = acc + L * k / 40; }
                    }
                    acc += L;
                }
                return bestS;
            }
            // дорога от ворот наружу до края карты
            void RoadOut(MapFeature ring, double s, double cx, double cy)
            {
                var (x, y, tx, ty) = ring.At(s);
                double nx = x - cx, ny = y - cy, l = Math.Max(1e-6, MapFeature.Hyp(nx, ny)); nx /= l; ny /= l;
                double far = Math.Max(d.WM, d.HM) * 1.5;
                var pts = new List<double[]> { new[] { x - nx * 12, y - ny * 12 }, new[] { x + nx * 60, y + ny * 60 } };
                // к краю — с лёгким изгибом
                double bx = x + nx * 60 + (R() - 0.5) * 80, by = y + ny * 60 + (R() - 0.5) * 80;
                pts.Add(new[] { Clamp(bx + nx * far * 0.2, 0, d.WM), Clamp(by + ny * far * 0.2, 0, d.HM) });
                pts.Add(new[] { Clamp(x + nx * far, -10, d.WM + 10), Clamp(y + ny * far, -10, d.HM + 10) });
                var r = Add("road", pts); r.Width = 10;
            }
            static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
            void Groves(int n, double cx, double cy, double keepOut)
            {
                for (int k = 0; k < n * 4 && n > 0; k++)
                {
                    double x = R() * d.WM, y = R() * d.HM;
                    if (MapFeature.Hyp(x - cx, y - cy) < keepOut) continue;
                    double a = Lerp(30, 90, R()), b = Lerp(25, 70, R());
                    Add("forest", Ring(x, y, a, b, true), true);
                    n--;
                }
            }

            // ── замок ──
            public MapFeature Castle(double cx, double cy, bool outskirts)
            {
                double half = S("size") switch { "small" => 50, "large" => 90, _ => 65 };
                double a = half, b = half * 0.78; bool poly = S("shape") == "poly", stone = S("material") == "stone";
                if (outskirts && B("hill")) { var h = Add("hill", Ring(cx, cy, a + 45, b + 45, true), true); h.Level = 2; }
                int rings = (int)N("rings");
                double oa = rings == 2 ? a + 30 : a, ob = rings == 2 ? b + 30 : b;
                var outer = Add(stone ? "wall" : "palisade", Ring(cx, cy, oa, ob, poly), true); outer.Every = N("every");
                if (B("moat")) { var m = Add("moat", MapDoc.Offset(outer.Pts, 14), true); m.Width = 10; }
                MapFeature inner = null;
                if (rings == 2) { inner = Add(stone ? "wall" : "palisade", Ring(cx, cy, a, b, poly), true); inner.Every = N("every"); }
                var core = inner ?? outer;
                if (B("keep")) { double k = Math.Min(a, b) * 0.32; Add("keep", Ring(cx + (R() - 0.5) * k, cy - k * 0.3, k, k * 0.85, false), true); }
                if (B("houses")) Add("village", Ring(cx, cy, a * 0.8, b * 0.8, false), true);
                var sides = GateSides((int)N("gates"));
                for (int i = 0; i < sides.Count; i++)
                {
                    double s = GateAt(outer, cx, cy, sides[i], rings == 2 ? -a * 0.4 : 0);
                    outer.Gates.Add(s);
                    if (inner != null) inner.Gates.Add(GateAt(inner, cx, cy, sides[i], a * 0.4));
                    if (outskirts) RoadOut(outer, s, cx, cy);
                }
                if (outskirts && B("town"))
                {
                    var (dx, dy) = Dir(sides[0]);
                    double tx = cx + dx * (Math.Max(oa, ob) + 130), ty = cy + dy * (Math.Max(oa, ob) + 130);
                    Add("village", Ring(tx, ty, 110, 80, true), true);
                }
                if (outskirts && B("woods")) Groves(4, cx, cy, Math.Max(oa, ob) + 120);
                return core;
            }

            // ── город ──
            public void Town()
            {
                double cx = d.WM / 2, cy = d.HM / 2, r = S("size") switch { "small" => 125, "large" => 300, _ => 200 };
                if (B("river"))
                {
                    double y0 = cy + r + 90 + R() * 60, wob = 50;
                    var river = Add("river", Enumerable.Range(0, 7).Select(k => new[] { -20 + (d.WM + 40) * k / 6.0, y0 + (R() - 0.5) * wob * 2 }).ToList());
                    river.Width = 25;
                }
                var townPoly = Ring(cx, cy, r, r * 0.8, true);
                var town = Add("town", townPoly, true);
                if (B("castle"))
                {
                    p["size"] = "small"; p["rings"] = 1.0; p["gates"] = 1.0; p["moat"] = false; p["keep"] = true; p["houses"] = false; p["shape"] = "rect"; p["material"] = "stone"; p["every"] = 40.0;
                    p["gateSide"] = "south";
                    Castle(cx + r * 0.25, cy - r * 0.2, false);
                }
                if (B("walls"))
                {
                    var wall = Add("wall", MapDoc.Offset(townPoly, 10), true); wall.Every = 55;
                    foreach (var side in GateSides((int)N("gates")))
                    {
                        double s = GateAt(wall, cx, cy, side);
                        wall.Gates.Add(s);
                        var (gx, gy, _, _) = wall.At(s);
                        Add("road", new List<double[]> { new[] { gx, gy }, new[] { cx, cy } }).Width = 10;
                        RoadOut(wall, s, cx, cy);
                    }
                }
                if (B("villages"))
                    for (int k = 0; k < 3; k++)
                    {
                        double t = R() * Math.PI * 2, dist = r + 260 + R() * 120;
                        double vx = Clamp(cx + Math.Cos(t) * dist, 120, d.WM - 120), vy = Clamp(cy + Math.Sin(t) * dist * 0.8, 120, d.HM - 120);
                        Add("village", Ring(vx, vy, 80, 60, true), true);
                    }
                if (B("woods")) Groves(5, cx, cy, r + 200);
            }

            // ── деревни ──
            public void Villages()
            {
                int n = (int)N("count");
                if (B("river"))
                {
                    double x0 = d.WM * Lerp(0.25, 0.75, R());
                    var river = Add("river", Enumerable.Range(0, 6).Select(k => new[] { x0 + (R() - 0.5) * 120, -20 + (d.HM + 40) * k / 5.0 }).ToList());
                    river.Width = 12;
                }
                var spots = new List<(double x, double y)>();
                for (int k = 0; k < n; k++)
                {
                    double x = 0, y = 0;
                    for (int tries = 0; tries < 30; tries++)   // не ближе 320 м к прежним
                    {
                        x = d.WM * Lerp(0.15, 0.85, R()); y = d.HM * Lerp(0.2, 0.8, R());
                        double xx = x, yy = y;
                        if (spots.All(s2 => MapFeature.Hyp(s2.x - xx, s2.y - yy) >= 320)) break;
                    }
                    spots.Add((x, y));
                    var poly = Ring(x, y, 90, 70, true);
                    Add("village", poly, true);
                    if (B("palisade"))
                    {
                        var pal = Add("palisade", MapDoc.Offset(poly, 8), true);
                        pal.Gates.Add(GateAt(pal, x, y, "south"));
                        var (gx, gy, _, _) = pal.At(pal.Gates[0]);
                        Add("road", new List<double[]> { new[] { gx, gy }, new[] { gx, d.HM + 10 } });
                    }
                    else Add("road", new List<double[]> { new[] { x, y - 80 }, new[] { x + (R() - 0.5) * 60, y }, new[] { x, d.HM + 10 } });
                }
                if (B("woods")) Groves(6, d.WM / 2, d.HM / 2, 160);
            }
        }
    }
}
