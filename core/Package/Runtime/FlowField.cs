// ═══════════ FlowField.cs — карта направлений и путь строя (Г31, шаг 1) ═══════════
// Карта направлений — цена пути до цели из каждой клетки 5 м: та же дейкстра и те же цены шага, что у зоны
// досягаемости трекера (BattleMap.Reach), только от цели назад. Из любой клетки видно, куда шагать.
// Путь центра строя — спуск по карте до цели, «натянутый как нить»: где прямой отрезок не дороже пути
// по клеткам, клетки срезаются (по открытому полю — одна прямая). Track — этот путь, промеренный по
// местности точно, клетка за клеткой: сколько метров он идёт по каждой клетке и сколько это стоит нормы.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class FlowField
    {
        public TerrainMap Map; public Geo Geo; public Rules R; public bool Horse;
        public int W, H, Target;
        public double CellW, CellH;
        public double[] Cost;   // цена пути до цели, м нормы; бесконечность — не дойти

        // Соседи — в том же порядке, что у BattleMap.Reach
        static readonly int[] DX = { -1, 0, 1, -1, 1, -1, 0, 1 }, DY = { -1, -1, -1, 0, 0, 1, 1, 1 };

        // множители местности по клеткам — один раз на карту (NaN — непроходимо): фигурок сотни, шагов сотни
        double[] mult;
        public double? Mult(int i) { double v = mult[i]; return double.IsNaN(v) ? (double?)null : v; }
        public bool Passable(int i) => !double.IsNaN(mult[i]);
        readonly Dictionary<int, int> nearCache = new Dictionary<int, int>();
        public int NearestPassableCached(int i)
        {
            if (!nearCache.TryGetValue(i, out var j)) nearCache[i] = j = NearestPassable(i);
            return j;
        }
        public int CellOf(double x, double y)
        {
            int cx = (int)Math.Min(W - 1, Math.Max(0, Math.Floor(x / CellW)));
            int cy = (int)Math.Min(H - 1, Math.Max(0, Math.Floor(y / CellH)));
            return cy * W + cx;
        }
        public bool Inside(double x, double y) => x >= 0 && y >= 0 && x < W * CellW && y < H * CellH;
        public (double x, double y) CenterOf(int i) => ((i % W + 0.5) * CellW, (i / W + 0.5) * CellH);

        // Цена шага из клетки i в соседнюю j — как в BattleMap.Reach; null — шагнуть нельзя
        public double? Step(int i, int j)
        {
            var mult = Mult(j);
            if (mult == null) return null;
            int x = i % W, y = i / W, nx = j % W, ny = j / W, dx = nx - x, dy = ny - y;
            if (dx != 0 && dy != 0 && Mult(y * W + nx) == null && Mult(ny * W + x) == null) return null;
            int up = Map.Z[j] - Map.Z[i];
            double step = dx != 0 && dy != 0 ? JsMath.Hypot(CellW, CellH) : dx != 0 ? CellW : CellH;
            return step * mult.Value * (up > 0 ? Math.Pow(R.Map.Height.ClimbCost, up) : 1);
        }

        // Ближайшая проходимая клетка (цель в воде или в стене — встаём рядом); −1 — на карте пройти негде
        public int NearestPassable(int i)
        {
            if (Passable(i)) return i;
            int x0 = i % W, y0 = i / W;
            for (int r = 1; r < Math.Max(W, H); r++)
            {
                int best = -1; double bd = double.MaxValue;
                for (int y = y0 - r; y <= y0 + r; y++)
                    for (int x = x0 - r; x <= x0 + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - x0), Math.Abs(y - y0)) != r || x < 0 || y < 0 || x >= W || y >= H) continue;
                        int j = y * W + x;
                        double d = (x - x0) * (x - x0) * CellW * CellW + (y - y0) * (y - y0) * CellH * CellH;
                        if (d < bd && Passable(j)) { bd = d; best = j; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        // Карта направлений к точке (tx, ty), м. Без местности — null: путь по прямой.
        public static FlowField Build(Geo geo, Rules r, bool horse, double tx, double ty)
        {
            var m = geo?.Map;
            if (m == null) return null;
            var f = new FlowField { Map = m, Geo = geo, R = r, Horse = horse, W = m.W, H = m.H, CellW = geo.W / m.W, CellH = geo.H / m.H };
            f.mult = new double[m.W * m.H];
            for (int i = 0; i < f.mult.Length; i++) f.mult[i] = BattleMap.MoveMult(m, i, horse, r) ?? double.NaN;
            f.Cost = new double[m.W * m.H];
            for (int i = 0; i < f.Cost.Length; i++) f.Cost[i] = double.PositiveInfinity;
            f.Target = f.NearestPassable(f.CellOf(tx, ty));
            if (f.Target < 0) return f;
            var heap = new MinHeap();
            f.Cost[f.Target] = 0; heap.Push(0, f.Target);
            while (heap.Count > 0)
            {
                var (c, j) = heap.Pop();
                if (c > f.Cost[j]) continue;
                int jx = j % f.W, jy = j / f.W;
                for (int k = 0; k < 8; k++)
                {
                    int nx = jx + DX[k], ny = jy + DY[k];
                    if (nx < 0 || ny < 0 || nx >= f.W || ny >= f.H) continue;
                    int i = ny * f.W + nx;
                    if (!f.Passable(i)) continue;
                    var s = f.Step(i, j);   // шаг из i в j: путь идёт к цели
                    if (s == null) continue;
                    double nc = c + s.Value;
                    if (nc < f.Cost[i]) { f.Cost[i] = nc; heap.Push(nc, i); }
                }
            }
            return f;
        }

        // Куда шагать из клетки i: сосед, через которого путь до цели дешевле всего; −1 — это цель или не дойти
        public int Next(int i)
        {
            if (i == Target || double.IsInfinity(Cost[i])) return -1;
            int ix = i % W, iy = i / W, best = -1;
            double bv = double.PositiveInfinity;
            for (int k = 0; k < 8; k++)
            {
                int nx = ix + DX[k], ny = iy + DY[k];
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                int j = ny * W + nx;
                if (!(Cost[j] < Cost[i])) continue;
                var s = Step(i, j);
                if (s == null) continue;
                double v = s.Value + Cost[j];
                if (v < bv) { bv = v; best = j; }
            }
            return best;
        }

        // Цепочка клеток от start до цели по карте направлений; null — не дойти
        public List<int> Chain(int start)
        {
            if (double.IsInfinity(Cost[start])) return null;
            var chain = new List<int> { start };
            for (int i = start; i != Target;)
            {
                i = Next(i);
                if (i < 0) return null;
                chain.Add(i);
            }
            return chain;
        }

        // Путь центра строя из (sx, sy) к (tx, ty), м: ломаная. Цепочка клеток натягивается как нить:
        // от текущей точки — к самому дальнему узлу, прямой отрезок до которого не дороже цепочки.
        public List<(double x, double y)> Route(double sx, double sy, double tx, double ty)
        {
            int s = CellOf(sx, sy);
            var chain = Passable(s) ? Chain(s) : null;
            if (chain == null) return null;
            var end = CellOf(tx, ty) == Target && Inside(tx, ty) ? (tx, ty) : CenterOf(Target);
            var nodes = new List<(double x, double y, double c)>();
            for (int k = 1; k < chain.Count; k++) { var (x, y) = CenterOf(chain[k]); nodes.Add((x, y, Cost[chain[k]])); }
            if (nodes.Count == 0) nodes.Add((end.Item1, end.Item2, 0));
            else nodes[nodes.Count - 1] = (end.Item1, end.Item2, 0);

            // цепочка считается от центров клеток, а строй стоит и встаёт не в центре: на концах пути —
            // поправка на этот сдвиг (по неравенству треугольника прямая не длиннее «до центра + цепочка»)
            var (scx, scy) = CenterOf(s); var (tcx, tcy) = CenterOf(Target);
            double startSlack = JsMath.Hypot(sx - scx, sy - scy) * (Mult(s) ?? 1);
            double endSlack = JsMath.Hypot(end.Item1 - tcx, end.Item2 - tcy) * (Mult(Target) ?? 1);
            var route = new List<(double x, double y)> { (sx, sy) };
            double cx = sx, cy = sy, cc = Cost[s] + startSlack;
            for (int idx = -1; idx < nodes.Count - 1;)
            {
                int best = idx + 1;
                for (int k = idx + 2; k < nodes.Count; k++)
                {
                    double slack = k == nodes.Count - 1 ? endSlack : 0;
                    if (SegmentCost(cx, cy, nodes[k].x, nodes[k].y) <= cc - nodes[k].c + slack + 1e-6) best = k;
                    else break;
                }
                idx = best; cx = nodes[idx].x; cy = nodes[idx].y; cc = nodes[idx].c;
                route.Add((cx, cy));
            }
            // натянутая нить зацепила угол непроходимой клетки — идём по цепочке целиком
            if (Track.Build(this, route) == null)
            {
                route = new List<(double x, double y)> { (sx, sy), CenterOf(s) };
                foreach (var n in nodes) route.Add((n.x, n.y));
            }
            return route;
        }

        // Отрезок по клеткам: куски «одна клетка — один кусок». Цена куска = длина × множитель местности,
        // в клетку, куда поднялись (выше предыдущей), — ещё × ClimbCost на каждый уровень, как шаг дейкстры.
        // Диагональ точно через угол — как шаг дейкстры: нельзя, если обе соседние по сторонам непроходимы.
        // false — отрезок упёрся в непроходимое или ушёл с карты. cost — сюда прибавляется цена отрезка.
        public bool Walk(double ax, double ay, double bx, double by, ref int prevCell, int leg, List<Track.Piece> outp, ref double cost)
        {
            double dx = bx - ax, dy = by - ay, len = JsMath.Hypot(dx, dy);
            if (len == 0) return true;
            if (!Inside(ax, ay) || !Inside(bx, by)) return false;
            int cx = (int)Math.Min(W - 1, Math.Floor(ax / CellW)), cy = (int)Math.Min(H - 1, Math.Floor(ay / CellH));
            int stepX = Math.Sign(dx), stepY = Math.Sign(dy);
            double tMaxX = dx != 0 ? ((stepX > 0 ? (cx + 1) * CellW : cx * CellW) - ax) / dx : double.PositiveInfinity;
            double tMaxY = dy != 0 ? ((stepY > 0 ? (cy + 1) * CellH : cy * CellH) - ay) / dy : double.PositiveInfinity;
            double tdX = dx != 0 ? CellW / Math.Abs(dx) : double.PositiveInfinity, tdY = dy != 0 ? CellH / Math.Abs(dy) : double.PositiveInfinity;
            double t = 0;
            while (true)
            {
                double tNext = Math.Min(1, Math.Min(tMaxX, tMaxY));
                int cell = cy * W + cx;
                if (tNext - t > 1e-12)
                {
                    var mult = Mult(cell);
                    if (mult == null) return false;
                    int up = prevCell >= 0 ? Map.Z[cell] - Map.Z[prevCell] : 0;
                    double rho = mult.Value * (up > 0 ? Math.Pow(R.Map.Height.ClimbCost, up) : 1);
                    // «поднялись в клетку» — на всём куске в ней; дальше по той же клетке — уже без подъёма
                    var last = outp != null && outp.Count > 0 ? outp[outp.Count - 1] : null;
                    if (cell == prevCell && last != null && last.Cell == cell) rho = last.Rho;
                    outp?.Add(new Track.Piece
                    {
                        X0 = ax + dx * t, Y0 = ay + dy * t, X1 = ax + dx * tNext, Y1 = ay + dy * tNext,
                        Len = (tNext - t) * len, Rho = rho, Cell = cell, Leg = leg,
                    });
                    cost += (tNext - t) * len * rho;
                    prevCell = cell;
                }
                if (tNext >= 1) return true;
                if (Math.Abs(tMaxX - tMaxY) < 1e-12)
                {
                    int ox = cx + stepX, oy = cy + stepY;
                    if (ox < 0 || oy < 0 || ox >= W || oy >= H) return false;
                    if (Mult(cy * W + ox) == null && Mult(oy * W + cx) == null) return false;
                    cx = ox; cy = oy; t = tMaxX; tMaxX += tdX; tMaxY += tdY;
                }
                else if (tMaxX < tMaxY) { cx += stepX; t = tMaxX; tMaxX += tdX; }
                else { cy += stepY; t = tMaxY; tMaxY += tdY; }
                if (cx < 0 || cy < 0 || cx >= W || cy >= H) return false;
            }
        }
        // Цена прямого отрезка, м нормы; бесконечность — не пройти
        public double SegmentCost(double ax, double ay, double bx, double by)
        {
            double cost = 0;
            int prev = CellOf(ax, ay);
            return Walk(ax, ay, bx, by, ref prev, 0, null, ref cost) ? cost : double.PositiveInfinity;
        }
    }

    // Путь, промеренный по местности: куски по клеткам, от каждого — сколько метров и сколько нормы
    public sealed class Track
    {
        public sealed class Piece
        {
            public double X0, Y0, X1, Y1, Len, Rho, S0, C0;   // Rho — сколько нормы стоит метр; S0, C0 — метров и нормы до куска
            public int Cell, Leg;
        }
        public List<Piece> Pieces = new List<Piece>();
        public List<(double x, double y)> Points;
        public double Length, Cost;

        // Без карты (f == null) — по прямой, метр за метр нормы
        public static Track Build(FlowField f, List<(double x, double y)> route)
        {
            // повторы подряд — вон: у отрезка нулевой длины нет направления
            var pts = new List<(double x, double y)> { route[0] };
            foreach (var q in route)
                if (JsMath.Hypot(q.x - pts[pts.Count - 1].x, q.y - pts[pts.Count - 1].y) > 1e-9) pts.Add(q);
            var tr = new Track { Points = pts };
            int prev = f != null ? f.CellOf(pts[0].x, pts[0].y) : -1;
            double cost = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                var (ax, ay) = pts[k - 1]; var (bx, by) = pts[k];
                if (f == null)
                {
                    double len = JsMath.Hypot(bx - ax, by - ay);
                    if (len > 0) tr.Pieces.Add(new Piece { X0 = ax, Y0 = ay, X1 = bx, Y1 = by, Len = len, Rho = 1, Cell = -1, Leg = k - 1 });
                }
                else if (!f.Walk(ax, ay, bx, by, ref prev, k - 1, tr.Pieces, ref cost)) return null;
            }
            foreach (var p in tr.Pieces) { p.S0 = tr.Length; p.C0 = tr.Cost; tr.Length += p.Len; tr.Cost += p.Len * p.Rho; }
            return tr;
        }

        // Точка пути, до которой потрачено cost нормы, и номер куска
        public (double x, double y, int piece) At(double cost)
        {
            if (Pieces.Count == 0) return (Points[0].x, Points[0].y, -1);
            int lo = 0, hi = Pieces.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Pieces[mid].C0 <= cost) lo = mid; else hi = mid - 1; }
            var p = Pieces[lo];
            double k = Math.Max(0, Math.Min(1, (cost - p.C0) / (p.Len * p.Rho)));
            return (p.X0 + (p.X1 - p.X0) * k, p.Y0 + (p.Y1 - p.Y0) * k, lo);
        }
        public double MetersAt(double cost)
        {
            var (_, _, i) = At(cost);
            if (i < 0) return 0;
            var p = Pieces[i];
            return p.S0 + Math.Max(0, Math.Min(p.Len, (cost - p.C0) / p.Rho));
        }
    }

    // Двоичная куча для дейкстры: при равных ценах порядок — как у кучи в BattleMap.Reach
    sealed class MinHeap
    {
        readonly List<double> c = new List<double>(); readonly List<int> v = new List<int>();
        public int Count => c.Count;
        void Swap(int p, int q) { (c[p], c[q]) = (c[q], c[p]); (v[p], v[q]) = (v[q], v[p]); }
        public void Push(double cost, int i)
        {
            c.Add(cost); v.Add(i); int k = c.Count - 1;
            while (k > 0) { int p = (k - 1) >> 1; if (c[p] <= c[k]) break; Swap(p, k); k = p; }
        }
        public (double c, int i) Pop()
        {
            var top = (c[0], v[0]);
            int last = c.Count - 1;
            c[0] = c[last]; v[0] = v[last]; c.RemoveAt(last); v.RemoveAt(last);
            for (int k = 0; ;)
            {
                int l = 2 * k + 1, r = l + 1, s = k;
                if (l < c.Count && c[l] < c[s]) s = l;
                if (r < c.Count && c[r] < c[s]) s = r;
                if (s == k) break;
                Swap(s, k); k = s;
            }
            return top;
        }
    }
}
