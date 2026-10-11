// ═══════════ MapMakers.cs — генераторы карт редактора (Г102): поле боя, замок, город, деревня ═══════════
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

        // типы замков (Алекс 11.10: «замки однотипные — квадрат со рвом; опирайся на реальные замки XIII–XIV веков, Толкина
        // и „Игру престолов“»): у каждого своё очертание, число колец, рвы и рельеф
        static readonly string[][] CastleTypeOpts =
        {
            O("random", "случайный"), O("enclosure", "стены с башнями (Конуи, Каэрнарвон)"), O("concentric", "концентрический (Бомарис, Крак-де-Шевалье)"),
            O("motte", "мотт и бейли (холм с донжоном и двор)"), O("round", "круглый (Рестормел)"), O("water", "водный, на озере (Бодиам)"),
            O("spur", "на отроге со щитовой стеной"), O("ridge", "дворы по гребню (Шато-Гайар)"), O("tiered", "ярусная твердыня (Минас Тирит)"),
            O("gorge", "крепость в ущелье (Хельмова Падь)"), O("pentagon", "исполинский пятибашенный (Харренхол)"), O("double", "двойные стены и ров между (Винтерфелл)"),
        };
        static readonly string[] CastleTypes = CastleTypeOpts.Skip(1).Select(o => o[0]).ToArray();
        // свои шаблоны: замок, город, деревня; поле боя — шаблоны движка группы «Поле»
        public static readonly List<MapTemplate> Own = new List<MapTemplate>
        {
            new MapTemplate { Id = "x-castle", Name = "Замок", Group = "Крепость", Params = Size(800, 700).Concat(new[]
            {
                Sel("type", "Тип замка", "random", CastleTypeOpts),
                Sel("size", "Размер", "mid", O("small", "малый, ~100 м"), O("mid", "средний, ~130 м"), O("large", "большой, ~180 м")),
                Sel("material", "Стены", "stone", O("stone", "каменные"), O("wood", "частокол")),
                Num("every", "Башни через, м", 45, 25, 90, 5),
                Num("gates", "Ворот", 1, 1, 4), Sel("gateSide", "Главные ворота", "south", Sides),
                Bool("moat", "Ров", true), Bool("hill", "На холме", false), Bool("keep", "Донжон", true), Bool("houses", "Постройки во дворе", true),
                Bool("town", "Посад у стен", false), Bool("woods", "Рощи вокруг", true),
            }).ToList() },
            new MapTemplate { Id = "x-town", Name = "Город", Group = "Поселение", Params = Size(1400, 1100).Concat(new[]
            {
                Sel("size", "Размер", "mid", O("small", "малый, ~250 м"), O("mid", "средний, ~400 м"), O("large", "большой, ~600 м")),
                Bool("walls", "Городская стена", true), Num("gates", "Ворот", 3, 1, 4), Sel("gateSide", "Главные ворота", "south", Sides),
                Bool("castle", "Замок в городе", false), Sel("type", "Тип замка в городе", "random", CastleTypeOpts[0], CastleTypeOpts[1], CastleTypeOpts[2], CastleTypeOpts[3], CastleTypeOpts[4], CastleTypeOpts[10], CastleTypeOpts[11]), Bool("river", "Река рядом", true), Bool("villages", "Деревни вокруг", true), Bool("woods", "Рощи", true),
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
                case "x-castle": mk.Castle(d.WM / 2, d.HM / 2, true); break;   // ворота замка — у края: AwayFromCorners
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
            // стена с башней в каждой точке: углы — точки, длинные стороны поделены на куски около every метров
            // (тянешь точку — тянешь башню, Алекс 11.10)
            MapFeature WallRing(List<double[]> pts, bool stone, double every, bool closed = true)
            {
                var r = new List<double[]>();
                int segs = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    double L = MapFeature.Hyp(b[0] - a[0], b[1] - a[1]); int k = Math.Max(1, (int)Math.Round(L / every));
                    for (int j = 0; j < k; j++) r.Add(new[] { Math.Round(a[0] + (b[0] - a[0]) * j / k, 1), Math.Round(a[1] + (b[1] - a[1]) * j / k, 1) });
                }
                if (!closed) r.Add(pts[pts.Count - 1]);
                var f = Add(stone ? "wall" : "palisade", r, closed); f.PtTowers = true; f.Every = -1;
                return f;
            }
            // ворота: по лучу из середины в сторону side со сдвигом shift, не у самой башни-точки
            double Gate(MapFeature ring, double cx, double cy, string side, double shift = 0)
            {
                double s = ring.AwayFromCorners(GateAt(ring, cx, cy, side, shift), ring.Kind == "wall" ? 28 : 16);
                ring.Gates.Add(s); return s;
            }
            (double x, double y) GatePt(MapFeature ring, double s) { var (x, y, _, _) = ring.At(s); return (x, y); }
            void RoadBetween(double x0, double y0, double x1, double y1, double w = 6) => Add("road", new List<double[]> { new[] { x0, y0 }, new[] { x1, y1 } }).Width = w;
            // многоугольник: n углов на эллипсе (a, b), поворот rot, неровность jit
            List<double[]> Poly(double cx, double cy, int n, double a, double b, double rot, double jit)
            {
                return Enumerable.Range(0, n).Select(k =>
                {
                    double t = rot + k * Math.PI * 2 / n + (R() - 0.5) * jit * 0.6, rr = 1 + (R() - 0.5) * jit;
                    return new[] { cx + Math.Cos(t) * a * rr, cy + Math.Sin(t) * b * rr };
                }).ToList();
            }
            static (double x, double y) Side(string s) => Dir(s);
            static readonly Dictionary<string, string> Opp = new Dictionary<string, string> { ["south"] = "north", ["north"] = "south", ["west"] = "east", ["east"] = "west" };

            // замок: тип — из настроек (случайный — по зерну); в городе (outskirts = false) — только те, что встают на ровном
            // месте. Возвращает внутреннее кольцо и точку главных ворот (к ней ведут дорогу из города)
            public (MapFeature core, double gx, double gy) Castle(double cx, double cy, bool outskirts)
            {
                string type = p.TryGetValue("type", out var tv) && tv is string ts ? ts : "random";
                string[] flat = { "enclosure", "concentric", "motte", "round", "double", "pentagon" };
                if (type == "random") type = (outskirts ? CastleTypes : flat)[(int)(R() * (outskirts ? CastleTypes.Length : flat.Length))];
                else if (!outskirts && !flat.Contains(type)) type = "enclosure";
                double half = S("size") switch { "small" => 50, "large" => 90, _ => 65 };
                bool stone = S("material") == "stone" && type != "motte";
                double every = N("every");
                string side = S("gateSide");
                var (fx, fy) = Side(side); double rx = -fy, ry = fx;   // вперёд — к главным воротам, вправо — поперёк
                double[] L(double u, double v) => new[] { cx + rx * u + fx * v, cy + ry * u + fy * v };
                (double, double) Lp(double u, double v) { var q = L(u, v); return (q[0], q[1]); }
                double rot = Math.Atan2(fy, fx);
                var sides = GateSides((int)N("gates"));
                bool moat = B("moat"), keep = B("keep"), houses = B("houses");
                MapFeature outer, core; double mgx, mgy;
                void Moat(MapFeature ring, double off = 14, double w = 10) { var m = Add("moat", MapDoc.Offset(ring.Pts, off), true); m.Width = w; }
                void Keep(double u, double v, double k) { var q = L(u, v); Add("keep", Poly(q[0], q[1], 4, k, k * 0.85, rot + Math.PI / 4, 0.12), true); }
                void Court(MapFeature ring, double k = 0.8) { if (!houses) return; var c = Centroid(ring.Pts); Add("village", MapDoc.Offset(ring.Pts, -14).Select(q => new[] { c.x + (q[0] - c.x) * k, c.y + (q[1] - c.y) * k }).ToList(), true); }
                void Hill(List<double[]> pts, int lv, double off) { var h = Add("hill", MapDoc.Offset(pts, off), true); h.Level = lv; }
                void Rocks(List<double[]> pts) => Add("rocks", WallRingPts(pts, 16, 0.7), true);   // скалы — с рваным краем
                // все ворота кольца: главные — по side со сдвигом, остальные — по прочим сторонам
                double Gates(MapFeature ring, double shift = 0)
                {
                    double s0 = Gate(ring, cx, cy, sides[0], shift);
                    for (int i = 1; i < sides.Count; i++) Gate(ring, cx, cy, sides[i]);
                    return s0;
                }
                if (outskirts && B("hill") && type != "motte" && type != "spur" && type != "ridge" && type != "tiered" && type != "gorge" && type != "water")
                {
                    var h = Add("hill", Ring(cx, cy, half + 45, half * 0.8 + 45, true), true); h.Level = 2;
                }
                switch (type)
                {
                    case "concentric":   // Бомарис: внутренний двор с частыми башнями, вокруг — низкий внешний пояс; ворота вразнобой
                    {
                        double a = half * 0.78;
                        var inner = Poly(cx, cy, 4, a * 1.41, a * 1.2, rot + Math.PI / 4, 0.08);
                        var outerPts = MapDoc.Offset(inner, 30);
                        // у внешнего пояса углы срезаны — восьмиугольник
                        var cut = new List<double[]>();
                        for (int i = 0; i < outerPts.Count; i++)
                        {
                            var a0 = outerPts[(i - 1 + outerPts.Count) % outerPts.Count]; var c0 = outerPts[i]; var b0 = outerPts[(i + 1) % outerPts.Count];
                            cut.Add(new[] { c0[0] + (a0[0] - c0[0]) * 0.18, c0[1] + (a0[1] - c0[1]) * 0.18 });
                            cut.Add(new[] { c0[0] + (b0[0] - c0[0]) * 0.18, c0[1] + (b0[1] - c0[1]) * 0.18 });
                        }
                        outer = WallRing(cut, stone, every * 1.2);
                        core = WallRing(inner, stone, every * 0.75);
                        double so = Gates(outer, -half * 0.35); Gate(core, cx, cy, sides[0], half * 0.3);
                        if (sides.Count > 1) Gate(core, cx, cy, sides[1], -half * 0.2);
                        (mgx, mgy) = GatePt(outer, so);
                        var (ix, iy) = GatePt(core, core.Gates[0]); RoadBetween(mgx, mgy, ix, iy);
                        if (moat) Moat(outer);
                        if (keep) Keep(0, -a * 0.35, a * 0.3);
                        Court(core, 0.75);
                        break;
                    }
                    case "motte":   // мотт и бейли: насыпной холм с частоколом и деревянной башней, перед ним — двор в частоколе
                    {
                        double mr = half * 0.34;
                        var mc = L(0, -half * 0.55);
                        var mottePts = Poly(mc[0], mc[1], 10, mr, mr, rot, 0.08);
                        Hill(mottePts, 3, 4);
                        var motte = WallRing(mottePts, false, every);
                        var bc = L(0, half * 0.25);
                        var bailey = WallRing(Poly(bc[0], bc[1], 9, half * 0.95, half * 0.62, rot + Math.PI / 2, 0.15), false, every);
                        outer = bailey; core = motte;
                        double so = Gates(bailey);
                        Gate(motte, mc[0], mc[1], sides[0]);
                        (mgx, mgy) = GatePt(bailey, so);
                        var (ix, iy) = GatePt(motte, motte.Gates[0]); RoadBetween(mgx, mgy, ix, iy, 5);
                        if (moat) { Moat(motte, 10, 8); Moat(bailey, 12, 8); }
                        Add("keep", Poly(mc[0], mc[1], 4, mr * 0.45, mr * 0.45, rot + Math.PI / 4, 0.05), true);
                        Court(bailey, 0.85);
                        break;
                    }
                    case "round":   // круглый: кольцо стен (каменный «шелл-кип»), внутри — постройки по кругу
                    {
                        double r = half * 0.72;
                        outer = core = WallRing(Poly(cx, cy, 14, r, r, rot, 0.04), stone, every * 1.3);
                        double so = Gates(outer); (mgx, mgy) = GatePt(outer, so);
                        if (moat) Moat(outer, 14, 12);
                        if (keep) Keep(0, -r * 0.2, r * 0.22);
                        Court(outer, 0.8);
                        break;
                    }
                    case "water":   // водный: квадратный двор с башнями по углам и посередине стен, кругом — озеро, к воротам — мост
                    {
                        double a = half * 0.7;
                        var sq = Poly(cx, cy, 4, a * 1.41, a * 1.41, rot + Math.PI / 4, 0.03);
                        outer = core = WallRing(sq, stone, a);
                        Add("water", WallRingPts(Poly(cx, cy, 12, a * 2.3, a * 2.1, rot, 0.12), 22, 0.35), true);
                        double so = Gates(outer); (mgx, mgy) = GatePt(outer, so);
                        if (keep) Keep(0, -a * 0.3, a * 0.28);
                        Court(outer, 0.8);
                        break;
                    }
                    case "spur":   // на отроге: клин острием назад, к подходу — щитовая стена (двойная) и ров поперёк; скалы по бокам
                    {
                        double a = half;
                        var pts = new List<double[]> { L(-a * 0.75, a * 0.45), L(a * 0.75, a * 0.45), L(a * 0.55, -a * 0.35), L(0, -a * 1.05), L(-a * 0.55, -a * 0.35) };
                        Hill(pts, 2, 25); Hill(pts, 3, 4);
                        Rocks(new List<double[]> { L(-a * 1.4, a * 0.3), L(-a * 0.95, a * 0.35), L(-a * 0.8, -a * 0.5), L(-a * 0.15, -a * 1.35), L(-a * 0.5, -a * 1.6), L(-a * 1.3, -a * 0.8) });
                        Rocks(new List<double[]> { L(a * 1.4, a * 0.3), L(a * 0.95, a * 0.35), L(a * 0.8, -a * 0.5), L(a * 0.15, -a * 1.35), L(a * 0.5, -a * 1.6), L(a * 1.3, -a * 0.8) });
                        outer = core = WallRing(pts, stone, every);
                        var shield = WallRing(new List<double[]> { L(-a * 0.85, a * 0.58), L(a * 0.85, a * 0.58) }, stone, every * 0.7, closed: false);
                        var ditch = Add("trench", new List<double[]> { L(-a * 1.2, a * 0.85), L(a * 1.2, a * 0.85) }); ditch.Width = 10;
                        // ворота — сбоку у щитовой стены: проход в обход неё
                        var gs = Gate(outer, cx, cy, sides[0], a * 0.55);
                        for (int i = 1; i < sides.Count; i++) Gate(outer, cx, cy, sides[i]);
                        shield.Gates.Add(shield.AwayFromCorners(shield.Nearest(L(a * 0.55, a * 0.58)[0], L(a * 0.55, a * 0.58)[1]).s, 14));
                        (mgx, mgy) = GatePt(shield, shield.Gates[0]);
                        if (keep) Keep(0, a * 0.12, a * 0.2);   // бергфрид — сразу за щитовой стеной
                        Court(outer, 0.7);
                        break;
                    }
                    case "ridge":   // по гребню: три двора друг за другом, между ними рвы; последний — с донжоном
                    {
                        double a = half;
                        var w1 = new List<double[]> { L(-a * 0.5, a * 1.15), L(a * 0.5, a * 1.15), L(a * 0.6, a * 0.55), L(-a * 0.6, a * 0.55) };
                        var w2 = Poly(L(0, -a * 0.05)[0], L(0, -a * 0.05)[1], 6, a * 0.75, a * 0.48, rot + Math.PI / 2, 0.12);
                        var w3 = Poly(L(0, -a * 0.95)[0], L(0, -a * 0.95)[1], 9, a * 0.45, a * 0.4, rot, 0.08);
                        var all = new List<double[]> { L(-a * 0.8, a * 1.3), L(a * 0.8, a * 1.3), L(a * 0.95, -a * 0.3), L(a * 0.6, -a * 1.5), L(-a * 0.6, -a * 1.5), L(-a * 0.95, -a * 0.3) };
                        Hill(all, 1, 30); Hill(all, 2, 8); Hill(w3, 3, 6);
                        var r1 = WallRing(w1, stone, every); var r2 = WallRing(w2, stone, every); var r3 = WallRing(w3, stone, every * 0.8);
                        outer = r1; core = r3;
                        double s1 = Gate(r1, cx, cy, sides[0]); (mgx, mgy) = GatePt(r1, s1);
                        // ворота дворов — друг к другу
                        var (c2x, c2y) = Centroid(w2); var (c3x, c3y) = Centroid(w3);
                        double s1b = r1.AwayFromCorners(r1.Nearest(c2x, c2y).s, 28); r1.Gates.Add(s1b);
                        double s2a = r2.AwayFromCorners(r2.Nearest(GatePt(r1, s1b).x, GatePt(r1, s1b).y).s, 28); r2.Gates.Add(s2a);
                        double s2b = r2.AwayFromCorners(r2.Nearest(c3x, c3y).s, 28); r2.Gates.Add(s2b);
                        double s3 = r3.AwayFromCorners(r3.Nearest(GatePt(r2, s2b).x, GatePt(r2, s2b).y).s, 28); r3.Gates.Add(s3);
                        RoadBetween(GatePt(r1, s1b).x, GatePt(r1, s1b).y, GatePt(r2, s2a).x, GatePt(r2, s2a).y, 5);
                        RoadBetween(GatePt(r2, s2b).x, GatePt(r2, s2b).y, GatePt(r3, s3).x, GatePt(r3, s3).y, 5);
                        if (moat) { var d1 = Add("trench", new List<double[]> { L(-a * 0.9, a * 0.45), L(a * 0.9, a * 0.45) }); d1.Width = 8; var d2 = Add("trench", new List<double[]> { L(-a * 0.8, -a * 0.52), L(a * 0.8, -a * 0.52) }); d2.Width = 8; }
                        if (keep) Keep(0, -a * 1.0, a * 0.18);
                        Court(r2, 0.75);
                        Rocks(new List<double[]> { L(-a * 1.1, -a * 0.6), L(-a * 0.7, -a * 1.7), L(-a * 1.5, -a * 1.9), L(-a * 1.7, -a * 0.9) });
                        Rocks(new List<double[]> { L(a * 1.1, -a * 0.6), L(a * 0.7, -a * 1.7), L(a * 1.5, -a * 1.9), L(a * 1.7, -a * 0.9) });
                        break;
                    }
                    case "tiered":   // ярусы: дуги стен у скалы, каждая выше прежней; ворота — то справа, то слева (не пройти прямо)
                    {
                        int tiers = S("size") == "large" ? 4 : 3;
                        double R0 = half * 1.25, cliffV = -half * 0.55;
                        var cc = L(0, cliffV);
                        MapFeature prev = null; outer = core = null; mgx = mgy = 0;
                        for (int k = 0; k < tiers; k++)
                        {
                            double r = R0 * (1 - k * 0.8 / tiers);
                            var arc = new List<double[]>();
                            for (int j = 0; j <= 10; j++) { double t = Math.PI * j / 10; arc.Add(L(-Math.Cos(t) * r, cliffV + Math.Sin(t) * r)); }
                            var ring = WallRing(arc, stone, every * (k == tiers - 1 ? 1.0 : 1.5));
                            Hill(arc, Math.Min(3, k + 1), 0);
                            double sh = (k % 2 == 0 ? 1 : -1) * r * 0.45;
                            double gs = ring.AwayFromCorners(ring.Nearest(L(sh, cliffV + Math.Sqrt(Math.Max(0, r * r - sh * sh)))[0], L(sh, cliffV + Math.Sqrt(Math.Max(0, r * r - sh * sh)))[1]).s, 28);
                            ring.Gates.Add(gs);
                            var (gx, gy) = GatePt(ring, gs);
                            if (prev == null) { outer = ring; mgx = gx; mgy = gy; } else { var (px, py) = GatePt(prev, prev.Gates[0]); RoadBetween(px, py, gx, gy, 5); }
                            if (houses && k < tiers - 1) Add("village", arc.Select(q => new[] { cc[0] + (q[0] - cc[0]) * 0.92, cc[1] + (q[1] - cc[1]) * 0.92 }).ToList(), true);
                            prev = ring; core = ring;
                        }
                        if (keep) Keep(0, cliffV + R0 * 0.12, half * 0.14);   // белая башня цитадели
                        // скала за спиной: полукруг утёса шире всех ярусов
                        Rocks(new List<double[]> { L(-R0 * 1.6, cliffV + 6), L(R0 * 1.6, cliffV + 6), L(R0 * 1.4, cliffV - half * 1.2), L(0, cliffV - half * 1.6), L(-R0 * 1.4, cliffV - half * 1.2) });
                        break;
                    }
                    case "gorge":   // ущелье: скалы с двух сторон, поперёк — длинная стена с рвом, сбоку на отроге — твердыня с воротами
                    {
                        double a = half, gw = a * 0.75;
                        Rocks(new List<double[]> { L(-gw - a * 1.6, a * 0.6), L(-gw - 4, a * 0.45), L(-gw, -a * 0.6), L(-gw * 0.6, -a * 2.2), L(-gw - a * 1.8, -a * 2.2) });
                        Rocks(new List<double[]> { L(gw + a * 1.6, a * 0.6), L(gw + 4, a * 0.45), L(gw, -a * 0.6), L(gw * 0.6, -a * 2.2), L(gw + a * 1.8, -a * 2.2) });
                        var wallLine = WallRing(new List<double[]> { L(-gw - 8, a * 0.25), L(gw + 8, a * 0.25) }, stone, every, closed: false);
                        var ditch = Add("trench", new List<double[]> { L(-gw, a * 0.55), L(gw, a * 0.55) }); ditch.Width = 8;
                        // твердыня — на отроге у левой скалы, выдвинута вперёд стены
                        var hc = L(-gw * 0.55, a * 0.5);
                        var hb = Poly(hc[0], hc[1], 7, a * 0.38, a * 0.32, rot, 0.1);
                        Hill(hb, 2, 10);
                        var horn = WallRing(hb, stone, every * 0.7);
                        outer = horn; core = horn;
                        double gs = Gate(horn, hc[0], hc[1], sides[0], a * 0.15);
                        (mgx, mgy) = GatePt(horn, gs);
                        // задняя калитка твердыни — в ущелье
                        horn.Gates.Add(horn.AwayFromCorners(horn.Nearest(L(-gw * 0.55, a * 0.1)[0], L(-gw * 0.55, a * 0.1)[1]).s, 28));
                        if (keep) Add("keep", Poly(hc[0], hc[1] , 4, a * 0.12, a * 0.12, rot + Math.PI / 4, 0.05), true);
                        // в глубине — пещеры и укрытие: деревня у дна ущелья
                        if (houses) { var vc = L(0, -a * 0.9); Add("village", Poly(vc[0], vc[1], 6, gw * 0.6, a * 0.45, rot + Math.PI / 2, 0.15), true); }
                        break;
                    }
                    case "pentagon":   // исполинский: пять огромных башен по углам пятиугольника, между ними — голые стены; внутри — палаты и богорощa
                    {
                        double r = half * 1.45;
                        outer = core = WallRing(Poly(cx, cy, 5, r, r * 0.9, rot - Math.PI / 2, 0.06), stone, 1e6);
                        double so = Gates(outer); (mgx, mgy) = GatePt(outer, so);
                        if (moat) Moat(outer, 16, 12);
                        for (int k = 0; k < 3; k++) { var q = L((k - 1) * r * 0.35, -r * 0.25 + (k % 2) * r * 0.1); Add("keep", Poly(q[0], q[1], 4, r * 0.16, r * 0.08, rot, 0.05), true); }
                        var gwc = L(r * 0.4, r * 0.3); Add("forest", Poly(gwc[0], gwc[1], 7, r * 0.18, r * 0.15, rot, 0.25), true);
                        Court(outer, 0.6);
                        if (outskirts) { var lc = L(r * 2.3, -r * 0.3); Add("water", Poly(lc[0], lc[1], 9, r * 0.9, r * 0.6, rot, 0.3), true); }
                        break;
                    }
                    case "double":   // Винтерфелл: высокая внутренняя стена, ров, низкая внешняя; внутри — донжон и богороща
                    {
                        double a = half * 0.85;
                        var inner = Poly(cx, cy, 6, a * 1.15, a, rot + Math.PI / 6, 0.12);
                        core = WallRing(inner, stone, every * 0.8);
                        outer = WallRing(MapDoc.Offset(inner, 34), stone, every * 1.3);
                        var mid = Add("moat", MapDoc.Offset(inner, 17), true); mid.Width = 10;
                        double so = Gates(outer, half * 0.25); Gate(core, cx, cy, sides[0], -half * 0.2);
                        (mgx, mgy) = GatePt(outer, so);
                        var (ix, iy) = GatePt(core, core.Gates[0]); RoadBetween(mgx, mgy, ix, iy);
                        if (keep) Keep(-a * 0.25, -a * 0.2, a * 0.25);
                        var gwc = L(a * 0.45, -a * 0.35); Add("forest", Poly(gwc[0], gwc[1], 7, a * 0.3, a * 0.22, rot, 0.3), true);
                        Court(core, 0.7);
                        break;
                    }
                    default:   // стены с башнями: неровный четырёх-шестиугольник вытянут поперёк подхода (Конуи — вдоль скалы)
                    {
                        double a = half * (1 + R() * 0.35), b = half * (0.6 + R() * 0.25);
                        int n = 4 + (int)(R() * 3);
                        var pts = Poly(cx, cy, n, a, b, rot + Math.PI / 2 + (R() - 0.5) * 0.4 + (n == 4 ? Math.PI / 4 : 0), 0.12);
                        outer = core = WallRing(pts, stone, every);
                        double so = Gates(outer); (mgx, mgy) = GatePt(outer, so);
                        if (moat) Moat(outer);
                        if (keep) Keep((R() - 0.5) * a * 0.4, -b * 0.35, Math.Min(a, b) * 0.3);
                        Court(outer, 0.8);
                        // у половины — внутренняя стена, делящая двор (верхний и нижний двор, как в Конуи)
                        if (R() < 0.5 && n >= 5) { var cross = WallRing(new List<double[]> { L(-a * 1.1, -b * 0.05), L(a * 1.1, -b * 0.05) }, stone, every * 2, closed: false); cross.Every = -1; cross.PtTowers = false;
                            cross.Pts = ClipToPoly(cross.Pts, pts); if (cross.Pts.Count >= 2) cross.Gates.Add(cross.Length() / 2); else d.Features.Remove(cross); }
                        break;
                    }
                }
                if (outskirts)
                {
                    // дороги наружу — от каждых ворот внешнего кольца (у твердыни в ущелье и у щитовой — от главных)
                    var ringOut = outer;
                    if (type == "gorge" || type == "spur") RoadOutFrom(mgx, mgy, cx, cy);
                    else foreach (double s in ringOut.Gates) RoadOut(ringOut, s, cx, cy);
                }
                if (outskirts && B("town"))
                {
                    var (dx, dy) = Dir(sides[0]);
                    double ext = Math.Max(half * 1.5, MapFeature.Hyp(mgx - cx, mgy - cy)) + 130;
                    double tx = cx + dx * ext, ty = cy + dy * ext;
                    Add("village", Ring(tx, ty, 110, 80, true), true);
                }
                if (outskirts && B("woods")) Groves(4, cx, cy, half * 2 + 120);
                return (core, mgx, mgy);
            }
            static (double x, double y) Centroid(List<double[]> pts) => (pts.Average(q => q[0]), pts.Average(q => q[1]));
            // неровный контур с точками через step метров (берег озера)
            List<double[]> WallRingPts(List<double[]> pts, double step, double jit)
            {
                var r = new List<double[]>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    double L = MapFeature.Hyp(b[0] - a[0], b[1] - a[1]); int k = Math.Max(1, (int)(L / step));
                    for (int j = 0; j < k; j++) r.Add(new[] { a[0] + (b[0] - a[0]) * j / k + (R() - 0.5) * step * jit, a[1] + (b[1] - a[1]) * j / k + (R() - 0.5) * step * jit });
                }
                return r;
            }
            // отрезок, обрезанный многоугольником: от первого входа до первого выхода
            static List<double[]> ClipToPoly(List<double[]> seg, List<double[]> poly)
            {
                var a = seg[0]; var b = seg[seg.Count - 1]; var inside = new List<double[]>();
                for (int k = 0; k <= 200; k++) { double t = k / 200.0; var q = new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t }; if (MapDoc.Inside(poly, q[0], q[1])) inside.Add(q); }
                return inside.Count < 2 ? new List<double[]>() : new List<double[]> { inside[0], inside[inside.Count - 1] };
            }
            // дорога от точки (ворот) прочь от середины — к краю карты
            void RoadOutFrom(double x, double y, double cx, double cy)
            {
                var tmp = new MapFeature { Kind = "road", Pts = new List<double[]> { new[] { x, y }, new[] { x + 1e-3, y } } };
                RoadOut(tmp, 0, cx, cy);
            }

            // ── город ──
            // дороги — от ворот, а без стены — от края города по тем же сторонам (Алекс 11.10: «без стен к городу перестают
            // вести дороги — а это не связано»); замок в городе — с дорогой от своих ворот к площади, кварталы в его дворе не строятся
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
                var sides = GateSides((int)N("gates"));
                if (B("castle"))
                {
                    // замок — у края города, со стороны, противной главным воротам; ворота замка — к середине города
                    var (dx, dy) = Dir(sides[0]);
                    string toward = sides[0];
                    p["size"] = r >= 250 ? "mid" : "small"; p["gates"] = 1.0; p["moat"] = r >= 250; p["keep"] = true; p["houses"] = false;
                    p["material"] = "stone"; p["every"] = 40.0; p["gateSide"] = toward;
                    if (!p.ContainsKey("type") || p["type"] as string == null) p["type"] = "random";
                    var (core, gx, gy) = Castle(cx - dx * r * 0.42, cy - dy * r * 0.42 * 0.8, false);
                    Add("road", new List<double[]> { new[] { gx, gy }, new[] { cx, cy } }).Width = 8;
                }
                MapFeature edge;
                if (B("walls")) { edge = Add("wall", MapDoc.Offset(townPoly, 10), true); edge.Every = 55; }
                else edge = new MapFeature { Kind = "road", Pts = MapDoc.Offset(townPoly, 4), Closed = true };   // без стены — край города
                foreach (var side in sides)
                {
                    double s = GateAt(edge, cx, cy, side);
                    if (edge.Kind == "wall") { s = edge.AwayFromCorners(s, 28); edge.Gates.Add(s); }
                    var (gx, gy, _, _) = edge.At(s);
                    Add("road", new List<double[]> { new[] { gx, gy }, new[] { cx, cy } }).Width = 10;
                    RoadOut(edge, s, cx, cy);
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
