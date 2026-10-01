// ═══════════ Bodies.cs — тела фигурок: толкотня и уступание (Г31 шаг 2; Г56–Г58) — ЧЕРНОВИК ДО ГМа ═══════════
// Фигурка — твёрдая капсула размером со свой квадратик: 5 × 2 м → отрезок 3 м вдоль фронта с радиусом 1 м,
// поэтому соседи в строю касаются ровно и не расталкиваются. Каждый шаг (после того как каждая фигурка
// решила, куда хочет, — MoveSim.Desire):
//   1) взгляд вперёд (Iron Kings): где фигурка будет через LookAheadSec; если там встанет поперёк фигурке,
//      которой надо уступить, — убирает из скорости шаг навстречу ей (идти вбок и назад можно);
//   2) шаг с пределом ускорения; в непроходимое не входит, скользит вдоль;
//   3) расталкивание того, что всё же перекрылось: свои из одного отряда — пополам; свой чужой отряд —
//      сдвигается тот, кто уступает; враг — тот, кто на него шёл (стоящего не теснят, Г58).
// Кто кому уступает: стрелки и свои проходят друг сквозь друга на половине скорости (Г56); свои твёрдые —
// кто раньше дошёл до места встречи, при равенстве — дисциплина, потом порядок в списке (Г57); врагу уступают
// оба — тела упираются (Г58). Упёрлась доля фигурок HeldShare — центр строя стоит (MoveSim.Lead, пробка).
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public static class Bodies
    {
        sealed class B
        {
            public Mover M; public int Mi; public FigState S;
            public double Ux, Uy, Half, Rad;   // ось капсулы (единичная), полудлина отрезка, радиус
            public double X0, Y0;              // где была до шага
            public bool Archer, Pushed;
        }
        enum Rel { Same, Ghost, Friend, Enemy }

        static bool SameSide(Unit a, Unit b) => a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId;
        static Rel RelOf(B a, B b)
        {
            if (a.M == b.M) return Rel.Same;
            if (!SameSide(a.M.P.U, b.M.P.U)) return Rel.Enemy;
            return a.Archer || b.Archer ? Rel.Ghost : Rel.Friend;
        }

        public static void Step(IList<Mover> ms, double dt, Rules r)
        {
            var M = r.Move;
            var bs = new List<B>();
            for (int mi = 0; mi < ms.Count; mi++)
            {
                var m = ms[mi]; var P = m.P;
                double h = P.Facing * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
                bool archer = P.U.Type == "archer";
                for (int k = 0; k < P.Figs.Count; k++)
                {
                    var f = P.Figs[k]; var s = m.Figs[k];
                    s.BlockedBy = 0; s.BlockedByEnemy = false; s.Slowed = false;
                    bool wide = f.Width >= f.Depth;
                    bs.Add(new B
                    {
                        M = m, Mi = mi, S = s, Ux = wide ? rx : fx, Uy = wide ? ry : fy,
                        Half = Math.Abs(f.Width - f.Depth) / 2, Rad = Math.Min(f.Width, f.Depth) / 2, X0 = s.X, Y0 = s.Y, Archer = archer,
                    });
                }
            }
            var pairs = Pairs(bs, M);

            // 1) взгляд вперёд
            double tau = M.LookAheadSec;
            foreach (var (ia, ib) in pairs)
            {
                var a = bs[ia]; var b = bs[ib]; var rel = RelOf(a, b);
                if (rel == Rel.Same) continue;
                if (rel == Rel.Ghost)
                {
                    if (Dist(a, a.S.X, a.S.Y, b, b.S.X, b.S.Y, out _, out _) < 0) a.S.Slowed = b.S.Slowed = true;
                    continue;
                }
                double ax = a.S.X + a.S.Dvx * tau, ay = a.S.Y + a.S.Dvy * tau, bx = b.S.X + b.S.Dvx * tau, by = b.S.Y + b.S.Dvy * tau;
                double d = Dist(a, ax, ay, b, bx, by, out double nx, out double ny);   // n — от b к a
                if (d >= M.YieldMargin) continue;
                bool enemy = rel == Rel.Enemy;
                bool aFirst = !enemy && First(a, b, ax, ay, bx, by, dt, M);
                if (enemy || !aFirst) Yield(a, nx, ny, b, enemy);
                if (enemy || aFirst) Yield(b, -nx, -ny, a, enemy);
            }

            // 2) шаг: предел скорости (сквозь своих — медленнее), предел ускорения, непроходимое
            var amaxOf = new double[ms.Count];
            for (int mi = 0; mi < ms.Count; mi++) amaxOf[mi] = MoveSim.FigAccel(ms[mi], r) * dt;
            foreach (var b in bs)
            {
                var s = b.S;
                double vmax = s.Vmax * (s.Slowed ? M.PassThroughSpeed : 1), dvx = s.Dvx, dvy = s.Dvy, dv = JsMath.Hypot(dvx, dvy);
                if (dv > vmax) { dvx *= vmax / dv; dvy *= vmax / dv; }
                double ax = dvx - s.Vx, ay = dvy - s.Vy, a = JsMath.Hypot(ax, ay), amax = amaxOf[b.Mi];
                if (a > amax) { ax *= amax / a; ay *= amax / a; }
                s.Vx += ax; s.Vy += ay;
                double nx = s.X + s.Vx * dt, ny = s.Y + s.Vy * dt;
                var F = b.M.Field;
                if (F == null || MoveSim.Free(F, nx, ny)) { s.X = nx; s.Y = ny; }
                else if (MoveSim.Free(F, nx, s.Y)) { s.X = nx; s.Vy = 0; }
                else if (MoveSim.Free(F, s.X, ny)) { s.Y = ny; s.Vx = 0; }
                else { s.Vx = 0; s.Vy = 0; }
            }

            // 3) расталкивание перекрывшихся (два прохода)
            for (int iter = 0; iter < 2; iter++)
                foreach (var (ia, ib) in pairs)
                {
                    var a = bs[ia]; var b = bs[ib]; var rel = RelOf(a, b);
                    if (rel == Rel.Ghost) continue;
                    double d = Dist(a, a.S.X, a.S.Y, b, b.S.X, b.S.Y, out double nx, out double ny);
                    double pen = -d;
                    if (pen <= M.BodyTol) continue;
                    double wa;
                    if (rel == Rel.Same) wa = 0.5;
                    else if (rel == Rel.Friend) wa = First(a, b, a.S.X, a.S.Y, b.S.X, b.S.Y, dt, M) ? 0 : 1;
                    else
                    {
                        // враг: сдвигается тот, кто шёл на другого; оба стоят — пополам
                        double pa = Math.Max(0, -(a.S.Vx * nx + a.S.Vy * ny)), pb = Math.Max(0, b.S.Vx * nx + b.S.Vy * ny);
                        wa = pa + pb < 0.05 ? 0.5 : pa / (pa + pb);
                    }
                    Push(a, nx * pen * wa, ny * pen * wa, rel != Rel.Same ? b : null, rel == Rel.Enemy);
                    Push(b, -nx * pen * (1 - wa), -ny * pen * (1 - wa), rel != Rel.Same ? a : null, rel == Rel.Enemy);
                }
            foreach (var b in bs)
                if (b.Pushed) { b.S.Vx = (b.S.X - b.X0) / dt; b.S.Vy = (b.S.Y - b.Y0) / dt; }

            // 4) упёрлась заметная доля фигурок — центр строя стоит (не меньше HoldSec подряд); кому уступал — в журнал
            var byId = new Dictionary<int, Mover>();
            foreach (var m in ms) byId[m.P.U.Id] = m;
            foreach (var m in ms)
            {
                var count = new Dictionary<int, (int n, bool enemy)>();
                int blocked = 0;
                foreach (var s in m.Figs)
                    if (s.BlockedBy != 0)
                    {
                        blocked++;
                        count[s.BlockedBy] = ((count.TryGetValue(s.BlockedBy, out var c) ? c.n : 0) + 1, s.BlockedByEnemy);
                    }
                bool active = m.Order != null && m.Track != null && !m.Done;
                if (blocked > 0)
                {
                    int main = 0, best = -1; bool enemy = false;
                    foreach (var kv in count) if (kv.Value.n > best) { best = kv.Value.n; main = kv.Key; enemy = kv.Value.enemy; }
                    m.LastBlocker = main; m.LastBlockerEnemy = enemy; m.LastBlockerName = byId.TryGetValue(main, out var o) ? o.P.U.Name : "?";
                }
                double need = m.Held ? Math.Max(2, M.HeldKeepShare * m.Figs.Count) : M.HeldShare * m.Figs.Count;
                if (active && m.Figs.Count > 0 && blocked > 0 && blocked >= need) m.HoldLeft = M.HoldSec;
                else m.HoldLeft = Math.Max(0, m.HoldLeft - dt);
                m.Held = active && m.HoldLeft > 1e-9;
                // журнал: всё время, что строй стоит, — тому, в кого упёрся последним
                if (m.Held && m.LastBlocker != 0)
                    m.Blockers[m.LastBlocker] = ((m.Blockers.TryGetValue(m.LastBlocker, out var was) ? was.sec : 0) + dt, m.LastBlockerEnemy, m.LastBlockerName);
            }
        }

        // Наименьший зазор между телами двух отрядов, м (минус — перекрытие): для журнала и проверок
        public static double MinGap(Mover a, Mover b)
        {
            var A = Of(a); var Bs = Of(b);
            double best = double.PositiveInfinity;
            foreach (var x in A) foreach (var y in Bs) best = Math.Min(best, Dist(x, x.S.X, x.S.Y, y, y.S.X, y.S.Y, out _, out _));
            return best;
        }
        static List<B> Of(Mover m)
        {
            var P = m.P; var list = new List<B>();
            double h = P.Facing * Math.PI / 180;
            for (int k = 0; k < P.Figs.Count; k++)
            {
                var f = P.Figs[k]; bool wide = f.Width >= f.Depth;
                list.Add(new B { M = m, S = m.Figs[k], Ux = wide ? Math.Cos(h) : Math.Sin(h), Uy = wide ? Math.Sin(h) : -Math.Cos(h),
                                 Half = Math.Abs(f.Width - f.Depth) / 2, Rad = Math.Min(f.Width, f.Depth) / 2 });
            }
            return list;
        }

        // Уступить: убрать из желаемой скорости шаг навстречу (n — от другого к себе)
        static void Yield(B y, double nx, double ny, B other, bool enemy)
        {
            double ap = y.S.Dvx * nx + y.S.Dvy * ny;
            if (ap >= 0) return;
            y.S.Dvx -= ap * nx; y.S.Dvy -= ap * ny;
            if (ap < -0.1) { y.S.BlockedBy = other.M.P.U.Id; y.S.BlockedByEnemy = enemy; }
        }

        static void Push(B b, double dx, double dy, B other, bool enemy)
        {
            if (dx == 0 && dy == 0) return;
            double nx = b.S.X + dx, ny = b.S.Y + dy;
            var F = b.M.Field;
            if (F != null && !MoveSim.Free(F, nx, ny)) return;   // в воду и в стену не выталкиваем
            b.S.X = nx; b.S.Y = ny; b.Pushed = true;
            if (other != null) { b.S.BlockedBy = other.M.P.U.Id; b.S.BlockedByEnemy = enemy; }
        }

        // Г57: идёт ли первым отряд a перед своим отрядом b. Решается при первой встрече и помнится, пока
        // отряды не разойдутся на RightsForgetSec: кто раньше у места встречи; стоящий — уже там; равно — дисциплина, порядок.
        static bool First(B a, B b, double ax, double ay, double bx, double by, double dt, Rules.MoveR M)
        {
            Mover A = a.M, Bm = b.M;
            int idA = A.P.U.Id, idB = Bm.P.U.Id;
            if (A.Rights.TryGetValue(idB, out var e) && (A.Steps - e.seen) * dt <= M.RightsForgetSec)
            {
                A.Rights[idB] = (e.mine, A.Steps); Bm.Rights[idA] = (!e.mine, Bm.Steps);
                return e.mine;
            }
            double cx = (ax + bx) / 2, cy = (ay + by) / 2;
            double tA = TimeTo(a, cx, cy), tB = TimeTo(b, cx, cy);
            bool mine;
            if (Math.Abs(tA - tB) > M.TieSec) mine = tA < tB;
            else if (A.P.U.Discipline != Bm.P.U.Discipline) mine = A.P.U.Discipline > Bm.P.U.Discipline;
            else mine = a.Mi < b.Mi;
            A.Rights[idB] = (mine, A.Steps); Bm.Rights[idA] = (!mine, Bm.Steps);
            return mine;
        }
        static double TimeTo(B x, double cx, double cy)
        {
            double v = Math.Max(JsMath.Hypot(x.S.Dvx, x.S.Dvy), JsMath.Hypot(x.S.Vx, x.S.Vy));
            if (v < 0.2) return 0;   // стоит — уже на месте
            return JsMath.Hypot(cx - x.S.X, cy - x.S.Y) / v;
        }

        // Кандидаты в пары — по сетке 6 м: каждая фигурка лежит во всех ячейках, которые задевает её рамка
        // с запасом на взгляд вперёд; пара — если рамки делят ячейку. Порядок пар — детерминированный.
        static List<(int, int)> Pairs(List<B> bs, Rules.MoveR M)
        {
            const double C = 6;
            var grid = new Dictionary<long, List<int>>();
            var range = new (int x0, int y0, int x1, int y1)[bs.Count];
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                double reach = JsMath.Hypot(b.S.Dvx, b.S.Dvy) * M.LookAheadSec + M.YieldMargin + b.Rad;
                double ex = Math.Abs(b.Ux) * b.Half + reach, ey = Math.Abs(b.Uy) * b.Half + reach;
                var rg = ((int)Math.Floor((b.S.X - ex) / C), (int)Math.Floor((b.S.Y - ey) / C), (int)Math.Floor((b.S.X + ex) / C), (int)Math.Floor((b.S.Y + ey) / C));
                range[i] = rg;
                for (int gy = rg.Item2; gy <= rg.Item4; gy++)
                    for (int gx = rg.Item1; gx <= rg.Item3; gx++)
                    {
                        long key = ((long)gx << 32) ^ (uint)gy;
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(i);
                    }
            }
            var pairs = new List<(int, int)>();
            var stamp = new int[bs.Count];
            for (int i = 0; i < stamp.Length; i++) stamp[i] = -1;
            for (int i = 0; i < bs.Count; i++)
            {
                var rg = range[i];
                for (int gy = rg.y0; gy <= rg.y1; gy++)
                    for (int gx = rg.x0; gx <= rg.x1; gx++)
                        foreach (int j in grid[((long)gx << 32) ^ (uint)gy])
                            if (j > i && stamp[j] != i) { stamp[j] = i; pairs.Add((i, j)); }
            }
            return pairs;
        }

        // Расстояние между капсулами (минус — перекрытие) и направление n от b к a
        static double Dist(B a, double ax, double ay, B b, double bx, double by, out double nx, out double ny)
        {
            double d2 = SegSeg(ax - a.Ux * a.Half, ay - a.Uy * a.Half, ax + a.Ux * a.Half, ay + a.Uy * a.Half,
                               bx - b.Ux * b.Half, by - b.Uy * b.Half, bx + b.Ux * b.Half, by + b.Uy * b.Half,
                               out double c1x, out double c1y, out double c2x, out double c2y);
            double d = Math.Sqrt(d2);
            if (d > 1e-9) { nx = (c1x - c2x) / d; ny = (c1y - c2y) / d; }
            else
            {
                double ex = ax - bx, ey = ay - by, el = JsMath.Hypot(ex, ey);
                if (el > 1e-9) { nx = ex / el; ny = ey / el; } else { nx = 1; ny = 0; }
            }
            return d - a.Rad - b.Rad;
        }

        // Ближайшие точки двух отрезков (Эриксон, «Real-Time Collision Detection», 5.1.9); квадрат расстояния
        static double SegSeg(double p1x, double p1y, double q1x, double q1y, double p2x, double p2y, double q2x, double q2y,
                             out double c1x, out double c1y, out double c2x, out double c2y)
        {
            const double eps = 1e-12;
            double d1x = q1x - p1x, d1y = q1y - p1y, d2x = q2x - p2x, d2y = q2y - p2y, rx = p1x - p2x, ry = p1y - p2y;
            double a = d1x * d1x + d1y * d1y, e = d2x * d2x + d2y * d2y, f = d2x * rx + d2y * ry;
            double s, t;
            if (a <= eps && e <= eps) { s = 0; t = 0; }
            else if (a <= eps) { s = 0; t = Clamp01(f / e); }
            else
            {
                double c = d1x * rx + d1y * ry;
                if (e <= eps) { t = 0; s = Clamp01(-c / a); }
                else
                {
                    double bb = d1x * d2x + d1y * d2y, denom = a * e - bb * bb;
                    s = denom > eps ? Clamp01((bb * f - c * e) / denom) : 0;
                    t = (bb * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp01((bb - c) / a); }
                }
            }
            c1x = p1x + d1x * s; c1y = p1y + d1y * s; c2x = p2x + d2x * t; c2y = p2y + d2y * t;
            return (c1x - c2x) * (c1x - c2x) + (c1y - c2y) * (c1y - c2y);
        }
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
