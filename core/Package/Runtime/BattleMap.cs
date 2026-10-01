// ═══════════ BattleMap.cs — отряд на карте в метрах (копия battlemap.js; этап 6а, К21–К24, К29) ═══════════
// Чистая геометрия и местность. Geo — местность (или null) и размер карты в метрах. Положение отряда
// хранится как в трекере — MapX/MapY в процентах карты; фасинг 0° — «вверх» (север), по часовой.
// Всё, что отсюда влияет на бой, в трекере включается переключателями «Правила карты».
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Geo
    {
        public TerrainMap Map;
        public double W, H;   // размер карты в метрах (картинка может быть растянута — W и H отдельно)
    }

    public sealed class GroundInfo
    {
        public int Id; public string Key, Name;
        public double Z, Share;
    }

    public sealed class ReachResult
    {
        public string KindName;   // melee / ranged
        public double Dist, Max;
        public bool Ok;
        public string Text;
    }

    public sealed class ReachMap { public double[] Cost; public int W, H; }

    public static class BattleMap
    {
        public static (double x, double y) UnitCenter(Unit u, Geo geo) => (u.MapX / 100 * geo.W, u.MapY / 100 * geo.H);

        // Четыре угла прямоугольника строя в метрах; первые два — передний край (куда смотрит отряд)
        public static double[][] UnitCorners(Unit u, Geo geo, Rules r)
        {
            var fp = Formation.Of(u, r);
            var (cx, cy) = UnitCenter(u, geo);
            double a = u.Facing * Math.PI / 180, c = JsMath.Cos(a), s = JsMath.Sin(a);
            double f = fp.Front, d = fp.Depth;
            var local = new[] { new[] { -f / 2, -d / 2 }, new[] { f / 2, -d / 2 }, new[] { f / 2, d / 2 }, new[] { -f / 2, d / 2 } };
            return local.Select(p => new[] { cx + p[0] * c - p[1] * s, cy + p[0] * s + p[1] * c }).ToArray();
        }

        // Пересекаются ли выпуклые многоугольники (разделяющая ось)
        static bool Overlaps(double[][] P, double[][] Q)
        {
            foreach (var poly in new[] { P, Q })
                for (int i = 0; i < poly.Length; i++)
                {
                    double x1 = poly[i][0], y1 = poly[i][1], x2 = poly[(i + 1) % poly.Length][0], y2 = poly[(i + 1) % poly.Length][1];
                    double nx = y2 - y1, ny = x1 - x2;
                    double aMin = double.PositiveInfinity, aMax = double.NegativeInfinity, bMin = double.PositiveInfinity, bMax = double.NegativeInfinity;
                    foreach (var p in P) { double v = p[0] * nx + p[1] * ny; aMin = Math.Min(aMin, v); aMax = Math.Max(aMax, v); }
                    foreach (var p in Q) { double v = p[0] * nx + p[1] * ny; bMin = Math.Min(bMin, v); bMax = Math.Max(bMax, v); }
                    if (aMax < bMin || bMax < aMin) return false;
                }
            return true;
        }
        static double PointSeg(double[] p, double[] a, double[] b)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], l2 = dx * dx + dy * dy;
            double t = l2 != 0 ? Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l2)) : 0;
            return JsMath.Hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy));
        }
        // Расстояние между краями двух выпуклых многоугольников (0 — касаются или перекрываются)
        public static double PolyGap(double[][] P, double[][] Q)
        {
            if (Overlaps(P, Q)) return 0;
            double d = double.PositiveInfinity;
            foreach (var (A, B) in new[] { (P, Q), (Q, P) })
                foreach (var p in A)
                    for (int i = 0; i < B.Length; i++) d = Math.Min(d, PointSeg(p, B[i], B[(i + 1) % B.Length]));
            return d;
        }
        public static double UnitGap(Unit a, Unit b, Geo geo, Rules r) => PolyGap(UnitCorners(a, geo, r), UnitCorners(b, geo, r));

        static bool IsRough(int id, Rules r) =>
            Terrain.ById.TryGetValue(id, out var t) && r.Map.Terrain.TryGetValue(t.Key, out var tr) && tr.Mode == "rough";

        // Что под отрядом: местность под большей частью строя (сетка точек 5 × 3) и средняя высота.
        // «Не задано» не голосует; поровну — побеждает пересечённая местность, при равенстве и тут — меньший код.
        public static GroundInfo GroundUnder(Unit u, Geo geo, Rules r, int nx = 5, int ny = 3)
        {
            if (geo == null || geo.Map == null) return null;
            var P = UnitCorners(u, geo, r);
            var votes = new SortedDictionary<int, int>();
            double zSum = 0; int n = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    double a = (i + 0.5) / nx, b = (j + 0.5) / ny;
                    double x = (P[0][0] * (1 - a) + P[1][0] * a) * (1 - b) + (P[3][0] * (1 - a) + P[2][0] * a) * b;
                    double y = (P[0][1] * (1 - a) + P[1][1] * a) * (1 - b) + (P[3][1] * (1 - a) + P[2][1] * a) * b;
                    var c = Terrain.CellAt(geo.Map, x / geo.W, y / geo.H);
                    zSum += c.Z; n++;
                    if (c.T != 0) votes[c.T] = (votes.TryGetValue(c.T, out var v) ? v : 0) + 1;
                }
            // как Object.entries (коды по возрастанию) + устойчивая сортировка: больше голосов, затем пересечённая
            var best = votes.ToList().OrderByDescending(kv => kv.Value).ThenByDescending(kv => IsRough(kv.Key, r) ? 1 : 0).FirstOrDefault();
            double z = Js.Round(zSum / n);
            if (votes.Count == 0) return new GroundInfo { Id = 0, Key = null, Name = "не задано", Z = z, Share = 0 };
            var t = Terrain.ById[best.Key];
            return new GroundInfo { Id = t.Id, Key = t.Key, Name = t.Name, Z = z, Share = (double)best.Value / n };
        }

        static Rules.TerrainR TR(Rules r, string key) => key != null && r.Map.Terrain.TryGetValue(key, out var t) ? t : null;

        // Модификаторы боя A → B по местности (К24). Ab — удар A по B, Ba — ответ B по A.
        // Mult — только ближний бой (высота), CoverPct — только дальний (укрытие от стрел).
        public static MapMods MapModsFor(Unit A, Unit B, Geo geo, Rules r)
        {
            var gB = GroundUnder(B, geo, r);
            if (gB == null) return null;
            var gA = GroundUnder(A, geo, r);
            var H = r.Map.Height;
            if (gB.Key == null && gB.Z == 0 && !(gA != null && (gA.Key != null || gA.Z != 0))) return null;   // под обоими ничего не нарисовано
            var tb = TR(r, gB.Key); var ta = gA != null ? TR(r, gA.Key) : null;
            string Low(string s) => s.ToLowerInvariant();
            string Where(GroundInfo g) => g != null ? $"{Low(g.Name)}{(g.Z != 0 ? ", высота " + Js.Num(g.Z) : "")}" : "не задано";
            var mods = new MapMods { Mode = tb?.Mode };
            mods.Notes.Add($"🗺 Местность: «{B.Name}» — {Where(gB)}; «{A.Name}» — {Where(gA)}");
            if (tb != null && tb.Cover != 0) { mods.Ab.CoverPct = tb.Cover; mods.Ab.CoverNote = $"Укрытие «{B.Name}» ({Low(gB.Name)})"; }
            if (ta != null && ta.Cover != 0) { mods.Ba.CoverPct = ta.Cover; mods.Ba.CoverNote = $"Укрытие «{A.Name}» ({Low(gA.Name)})"; }
            double zA = gA != null ? gA.Z : 0, dz = zA - gB.Z;
            if (dz > 0)
            {
                mods.Ab.Mult = H.DownhillMelee; mods.Ab.Note = $"⛰ Удар сверху вниз (высота {Js.Num(gA.Z)} → {Js.Num(gB.Z)})";
                mods.Ba.Mult = H.UphillMelee; mods.Ba.Note = $"⛰ Ответ снизу вверх (высота {Js.Num(gB.Z)} → {Js.Num(gA.Z)})";
            }
            else if (dz < 0)
            {
                mods.Ab.Mult = H.UphillMelee; mods.Ab.Note = $"⛰ Удар снизу вверх (высота {Js.Num(zA)} → {Js.Num(gB.Z)})";
                mods.Ba.Mult = H.DownhillMelee; mods.Ba.Note = $"⛰ Ответ сверху вниз (высота {Js.Num(gB.Z)} → {Js.Num(zA)})";
            }
            if (tb != null && tb.NoCharge) mods.NoCharge = "местность: " + Low(gB.Name);
            else if (ta != null && ta.NoCharge) mods.NoCharge = "местность: " + Low(gA.Name);
            return mods;
        }

        // Во сколько раз быстрее копится усталость на местности под отрядом (К17: песок, К33: снег)
        public static FatigueMult FatigueMultFor(Unit u, Geo geo, Rules r)
        {
            var g = GroundUnder(u, geo, r);
            var t = g != null ? TR(r, g.Key) : null;
            return t != null && t.Fatigue != 0 ? new FatigueMult { Mult = t.Fatigue, Name = g.Name } : null;
        }

        // ═══════════ движение и дальности (К22, К23, К29) — черновик ═══════════
        static bool IsHorseArcher(Unit u) => u.Type == "cavalry" && u.Weapon == "ranged";
        public static double UnitSpeed(Unit u, Rules r)
        {
            var S = r.Map.Speed;
            if (IsHorseArcher(u)) return S.HorseArcher;
            switch (u.Type)
            {
                case "infantry": return S.Infantry;
                case "archer": return S.Archer;
                case "pike": return S.Pike;
                case "cavalry": return S.Cavalry;
                default: return S.Infantry;
            }
        }
        // Множитель пути по клетке; null — непроходимо. «Не задано» — как поле.
        static double? MoveMult(TerrainMap map, int i, Unit u, Rules r)
        {
            int t = map.T[i];
            if (t == 0) return 1;
            var tr = TR(r, Terrain.ById[t].Key);
            if (tr == null || tr.Move == null) return null;
            return tr.Move[u.Type == "cavalry" ? 1 : 0];
        }

        // Карта досягаемости: цена пути в метрах до каждой клетки (Дейкстра, 8 соседей), до limitM.
        // Куча — та же, что в трекере, шаг в шаг: при равных ценах порядок обхода решает, какая сумма
        // с плавающей точкой дойдёт до клетки первой, а это последний бит цены.
        public static ReachMap Reach(Unit u, Geo geo, Rules r, double limitM)
        {
            var m = geo?.Map; if (m == null) return null;
            var (cx, cy) = UnitCenter(u, geo);
            int sx = (int)Math.Min(m.W - 1, Math.Max(0, Math.Floor(cx / geo.W * m.W)));
            int sy = (int)Math.Min(m.H - 1, Math.Max(0, Math.Floor(cy / geo.H * m.H)));
            double cw = geo.W / m.W, ch = geo.H / m.H, cd = JsMath.Hypot(cw, ch);
            var cost = new double[m.W * m.H];
            for (int i = 0; i < cost.Length; i++) cost[i] = double.PositiveInfinity;
            double climb = r.Map.Height.ClimbCost;
            var hc = new List<double>(); var hi = new List<int>();
            void Swap(int p, int q) { (hc[p], hc[q]) = (hc[q], hc[p]); (hi[p], hi[q]) = (hi[q], hi[p]); }
            void Push(double c, int i)
            {
                hc.Add(c); hi.Add(i); int k = hc.Count - 1;
                while (k > 0) { int p = (k - 1) >> 1; if (hc[p] <= hc[k]) break; Swap(p, k); k = p; }
            }
            (double c, int i) Pop()
            {
                var top = (hc[0], hi[0]);
                int last = hc.Count - 1;
                double lc = hc[last]; int li = hi[last];
                hc.RemoveAt(last); hi.RemoveAt(last);
                if (hc.Count > 0)
                {
                    hc[0] = lc; hi[0] = li; int k = 0;
                    for (; ; )
                    {
                        int l = 2 * k + 1, rr = l + 1, s = k;
                        if (l < hc.Count && hc[l] < hc[s]) s = l;
                        if (rr < hc.Count && hc[rr] < hc[s]) s = rr;
                        if (s == k) break;
                        Swap(s, k); k = s;
                    }
                }
                return top;
            }
            int start = sy * m.W + sx;
            cost[start] = 0; Push(0, start);
            while (hc.Count > 0)
            {
                var (c, i) = Pop();
                if (c > cost[i] || c > limitM) continue;
                int x = i % m.W, y = (i - x) / m.W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                        int j = ny * m.W + nx;
                        var mult = MoveMult(m, j, u, r);
                        if (mult == null) continue;
                        // по диагонали нельзя «протиснуться» между двумя непроходимыми клетками
                        if (dx != 0 && dy != 0 && MoveMult(m, y * m.W + nx, u, r) == null && MoveMult(m, ny * m.W + x, u, r) == null) continue;
                        int up = m.Z[j] - m.Z[i];
                        double step = (dx != 0 && dy != 0 ? cd : dx != 0 ? cw : ch) * mult.Value * (up > 0 ? Math.Pow(climb, up) : 1);
                        double nc = c + step;
                        if (nc < cost[j]) { cost[j] = nc; Push(nc, j); }
                    }
            }
            return new ReachMap { Cost = cost, W = m.W, H = m.H };
        }

        // Цена пути до точки (доли карты); без местности — расстояние по прямой
        public static double PathCost(ReachMap reach, Unit u, double fx, double fy, Geo geo)
        {
            if (reach == null)
            {
                var (cx, cy) = UnitCenter(u, geo);
                return JsMath.Hypot(fx * geo.W - cx, fy * geo.H - cy);
            }
            int x = (int)Math.Min(reach.W - 1, Math.Max(0, Math.Floor(fx * reach.W)));
            int y = (int)Math.Min(reach.H - 1, Math.Max(0, Math.Floor(fy * reach.H)));
            return reach.Cost[y * reach.W + x];
        }

        // Прямой отрезок пути: длина и «чистый» ли он для разбега (без леса, болота, брода, непроходимого)
        public static (double len, bool clear) RunOver(Unit u, double[] from, double[] to, Geo geo, Rules r)
        {
            double len = Js.Round(JsMath.Hypot((to[0] - from[0]) * geo.W, (to[1] - from[1]) * geo.H) * 10) / 10;
            bool clear = true;
            if (geo.Map != null)
            {
                int n = (int)Math.Max(2, Math.Ceiling(len / 5));
                for (int k = 0; k <= n && clear; k++)
                {
                    var c = Terrain.CellAt(geo.Map, from[0] + (to[0] - from[0]) * k / n, from[1] + (to[1] - from[1]) * k / n);
                    var tr = c.T != 0 ? TR(r, Terrain.ById[c.T].Key) : null;
                    if (tr != null && (tr.NoCharge || tr.Move == null)) clear = false;
                }
            }
            return (len, clear);
        }

        // Натиск без разбега невозможен (К29): причина или null
        public static string RunUpBlock(Unit u, Rules r)
        {
            double need = r.Map.ChargeRunUp, got = Js.Round(u.RunUpM);
            return got >= need ? null : $"разбег {Js.Num(got)} м из {Js.Num(need)}";
        }

        // Дальность стрельбы: своя у отряда или по типу; ближнему бою — 0
        public static double RangeOf(Unit u, Rules r)
        {
            if (u.Weapon != "ranged") return 0;
            if (u.Range > 0) return u.Range;
            var R = r.Map.Range;
            return IsHorseArcher(u) ? R.HorseArcher : u.Type == "archer" ? R.Archer : R.Other;
        }

        // Достаёт ли A до B этим видом боя: расстояние между краями строя против дальности
        public static ReachResult AttackReach(Unit A, Unit B, bool melee, Geo geo, Rules r)
        {
            double dist = Js.Round(UnitGap(A, B, geo, r));
            if (melee)
            {
                double mx = r.Map.MeleeGap;
                return new ReachResult
                {
                    KindName = "melee", Dist = dist, Max = mx, Ok = dist <= mx,
                    Text = dist <= mx ? "" : $"до «{B.Name}» {Js.Num(dist)} м — для ближнего боя нужно вплотную (до {Js.Num(mx)} м)",
                };
            }
            double max = RangeOf(A, r);
            var gA = GroundUnder(A, geo, r); var gB = GroundUnder(B, geo, r);
            double dz = (gA != null ? gA.Z : 0) - (gB != null ? gB.Z : 0);
            double rpl = r.Map.Height.RangePerLevel;
            if (dz > 0) max = Js.Round(max * (1 + rpl * dz));
            return new ReachResult
            {
                KindName = "ranged", Dist = dist, Max = max, Ok = dist <= max,
                Text = dist <= max ? "" : $"до «{B.Name}» {Js.Num(dist)} м — дальность «{A.Name}» {Js.Num(max)} м{(dz > 0 ? $" (с высоты +{Js.Num(Js.Round(rpl * dz * 100))}%)" : "")}",
            };
        }
    }
}
