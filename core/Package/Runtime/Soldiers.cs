// ═══════════ Soldiers.cs — живые бойцы внутри фигурок (Г75–Г78) — ЧЕРНОВИК ДО ГМа ═══════════
// Фигурка — тело строя: столкновения отрядов, касания и бой идут по ней (Г5, Г27), правила и сверка со столом — те же.
// Внутри фигурки — бойцы поимённо. У каждого место в фигурке (её оси) с небольшим личным смещением; боец догоняет
// место с запаздыванием ~Tau (Г76), со своим пределом скорости и разгона; расталкивается с любым бойцом рядом — своим
// и чужим (Г77), но фигурки этим не двигает. В схватке передняя шеренга касающихся фигурок делает шаг к врагу и
// бьётся выпадами (Г78). Стрела бьёт в бойца там, где он стоит (Man.Body, Г33); в рукопашной падают те, кто у врага;
// при раскладке (Г30) задние выходят на места павших — сперва внутри своей фигурки, лишние — к ближайшим пустым местам.
// Случайность только хешем от номеров — генератор боя не трогаем.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Man
    {
        public int Id;               // постоянный номер бойца в отряде (с 1)
        public FigState Fig;         // чья фигурка
        public double Lx, Ly;        // место в фигурке, её оси: x — вдоль фронта вправо, y — вглубь (минус — вперёд)
        public int Row;              // ряд в фигурке (0 — передний)
        public double X, Y, Vx, Vy, Facing;
        public bool Alive = true;
        public double Hu;            // своя доля 0…1 (хеш номера): фаза выпадов и виляния
        public Body Body;            // мишень для стрел (Г33) — та же точка
    }

    public static class Soldiers
    {
        // Курс фигурки на деле: развёрнута не по строю (охват, бегство своим курсом) — по её оси, повёрнутой туда,
        // куда она смотрит; иначе — по строю
        public static double FigHeading(Mover m, FigState s)
        {
            if (!s.Turned) return m.P.Facing;
            double want = s.Wrap ? s.WH : m.Fleeing && !double.IsNaN(s.FleeH) ? s.FleeH : m.P.Facing, a = s.Axis;
            if (Math.Abs(MoveSim.AngleDiff(a, want)) > 90) a += 180;
            return MoveSim.Norm(a);
        }

        // Место бойца в мире (без выпадов и толпы) — куда он стремится в строю
        public static (double x, double y) HomeOf(Mover m, Man man)
        {
            World(m, man.Fig, man.Lx, man.Ly, out var x, out var y);
            return (x, y);
        }

        static Rules.FormationR Grid(Unit u, Rules r) => r.Map.Formation.TryGetValue(u.Type, out var f) ? f : r.Map.Formation["infantry"];

        // Места бойцов в фигурке k на count бойцов: сетка шагом PerMan × RankDepth, ряды спереди назад, неполный ряд —
        // по центру; бойцов больше, чем сетка, — лишние встают рядами сзади
        static List<(double ox, double oy, int row)> SlotsOf(Mover m, int k, Rules r, int count)
        {
            var fg = m.P.Figs[k]; var f = Grid(m.P.U, r);
            int cols = Math.Max(1, (int)Math.Round(fg.Width / f.PerMan));
            int left = count;
            var list = new List<(double, double, int)>();
            for (int j = 0; left > 0; j++)
            {
                int n = Math.Min(cols, left); double off = (cols - n) / 2.0; left -= n;
                for (int i = 0; i < n; i++) list.Add((-fg.Width / 2 + (off + i + 0.5) * f.PerMan, -fg.Depth / 2 + (j + 0.5) * f.RankDepth, j));
            }
            return list;
        }

        static void World(Mover m, FigState s, double lx, double ly, out double x, out double y)
        {
            double h = FigHeading(m, s) * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            x = s.X + lx * rx - ly * fx; y = s.Y + lx * ry - ly * fy;
        }

        static void Seat(Mover m, Man man, FigState s, (double ox, double oy, int row) sl, Rules r)
        {
            var f = Grid(m.P.U, r); var M = r.Men;
            man.Fig = s; man.Row = sl.row;
            man.Lx = sl.ox + (MoveSim.Hash01(m.P.U.Id, man.Id, 11) - 0.5) * M.Jitter * f.PerMan;
            man.Ly = sl.oy + (MoveSim.Hash01(m.P.U.Id, man.Id, 12) - 0.5) * M.Jitter * f.RankDepth;
        }

        // Разложить бойцов по фигуркам. spawn — новый отряд: бойцы встают ровно на места. Иначе (после потерь, Г30):
        // в каждой фигурке должно быть столько бойцов, сколько велит раскладка; бойцы остаются в своей фигурке, а
        // нехватку закрывают по цепочке соседних фигурок: из соседней в нехватку, из её соседки — в соседнюю… — каждый
        // сдвигается на одну фигурку, а не бежит через весь строй; так задние и выходят на места павших. Бойцы
        // исчезнувших фигурок сперва приписаны к ближайшей. Внутри фигурки — места спереди назад ближайшим
        public static void Assign(Mover m, Rules r, bool spawn = false)
        {
            var P = m.P;
            if (spawn)
            {
                m.Men.Clear();
                for (int k = 0; k < P.Figs.Count; k++)
                    foreach (var sl in SlotsOf(m, k, r, (int)Math.Round(P.Figs[k].Men)))
                    {
                        var man = new Man { Id = ++m.NextManId };
                        man.Hu = MoveSim.Hash01(m.P.U.Id, man.Id, 13);
                        Seat(m, man, m.Figs[k], sl, r);
                        World(m, m.Figs[k], man.Lx, man.Ly, out man.X, out man.Y);
                        man.Facing = FigHeading(m, m.Figs[k]);
                        m.Men.Add(man);
                    }
                m.MenVersion++;
                return;
            }
            m.Men.RemoveAll(x => !x.Alive);
            int nk = P.Figs.Count;
            if (nk == 0) { m.Men.Clear(); m.MenVersion++; return; }
            var idx = new Dictionary<FigState, int>();
            for (int k = 0; k < nk; k++) idx[m.Figs[k]] = k;
            var members = Enumerable.Range(0, nk).Select(_ => new List<Man>()).ToList();
            foreach (var man in m.Men)
            {
                if (man.Fig != null && idx.TryGetValue(man.Fig, out int k)) { members[k].Add(man); continue; }
                // фигурки нет — к ближайшей
                int best = 0; double bd = double.MaxValue;
                for (int q = 0; q < nk; q++) { double d = (m.Figs[q].X - man.X) * (m.Figs[q].X - man.X) + (m.Figs[q].Y - man.Y) * (m.Figs[q].Y - man.Y); if (d < bd) { bd = d; best = q; } }
                members[best].Add(man);
            }
            var want = Enumerable.Range(0, nk).Select(k => (int)Math.Round(P.Figs[k].Men)).ToArray();
            // соседи — фигурки ближе полутора своих размеров (по месту на поле: в охвате колонна соседствует с колонной)
            var nbr = Enumerable.Range(0, nk).Select(_ => new List<int>()).ToList();
            for (int a = 0; a < nk; a++)
                for (int b = a + 1; b < nk; b++)
                {
                    double lim = 1.6 * Math.Max(Math.Max(P.Figs[a].Width, P.Figs[a].Depth), Math.Max(P.Figs[b].Width, P.Figs[b].Depth));
                    if (JsMath.Hypot(m.Figs[a].X - m.Figs[b].X, m.Figs[a].Y - m.Figs[b].Y) <= lim) { nbr[a].Add(b); nbr[b].Add(a); }
                }
            Man Nearest(List<Man> from, FigState to) => from.OrderBy(x => (x.X - to.X) * (x.X - to.X) + (x.Y - to.Y) * (x.Y - to.Y)).ThenBy(x => x.Id).First();
            // нехватка — спереди назад: по цепочке от ближайшей (по соседству) фигурки с лишними
            foreach (int d in Enumerable.Range(0, nk).OrderBy(k => P.Figs[k].Rank).ThenBy(k => P.Figs[k].Y).ThenBy(k => P.Figs[k].X))
            {
                while (members[d].Count < want[d])
                {
                    var prev = new int[nk]; for (int q = 0; q < nk; q++) prev[q] = -2;
                    prev[d] = -1; var queue = new Queue<int>(); queue.Enqueue(d); int src = -1;
                    while (queue.Count > 0 && src < 0)
                    {
                        int u = queue.Dequeue();
                        foreach (int v in nbr[u].OrderBy(v => JsMath.Hypot(m.Figs[v].X - m.Figs[u].X, m.Figs[v].Y - m.Figs[u].Y)))
                        {
                            if (prev[v] != -2) continue;
                            prev[v] = u;
                            if (members[v].Count > want[v]) { src = v; break; }
                            queue.Enqueue(v);
                        }
                    }
                    if (src < 0)
                    {
                        // по соседству лишних нет — ближайшая фигурка с лишними напрямую
                        int best = -1; double bd = double.MaxValue;
                        for (int q = 0; q < nk; q++)
                            if (members[q].Count > want[q]) { double dd = JsMath.Hypot(m.Figs[q].X - m.Figs[d].X, m.Figs[q].Y - m.Figs[d].Y); if (dd < bd) { bd = dd; best = q; } }
                        if (best < 0) break;   // лишних нет нигде — фигурка неполная
                        var man0 = Nearest(members[best], m.Figs[d]); members[best].Remove(man0); members[d].Add(man0);
                        continue;
                    }
                    // сдвиг по цепочке: каждая фигурка на пути отдаёт ближайшего к следующей
                    for (int v = src; prev[v] != -1; v = prev[v])
                    {
                        int to = prev[v];
                        var man1 = Nearest(members[v], m.Figs[to]); members[v].Remove(man1); members[to].Add(man1);
                    }
                }
            }
            // по местам внутри фигурки: спереди назад — ближайшему
            for (int k = 0; k < nk; k++)
            {
                var s = m.Figs[k]; var mine = members[k];
                foreach (var sl in SlotsOf(m, k, r, mine.Count).OrderBy(q => q.oy).ThenBy(q => q.ox))
                {
                    World(m, s, sl.ox, sl.oy, out var wx, out var wy);
                    int best = 0; double bd = double.MaxValue;
                    for (int q = 0; q < mine.Count; q++) { double dd = (mine[q].X - wx) * (mine[q].X - wx) + (mine[q].Y - wy) * (mine[q].Y - wy); if (dd < bd) { bd = dd; best = q; } }
                    Seat(m, mine[best], s, sl, r); mine.RemoveAt(best);
                }
            }
            m.MenVersion++;
        }

        // Шаг бойцов: к своему месту с запаздыванием, выпады в схватке, толкотня со всеми. Фигурки не двигает.
        // Курс фигурки (синус, косинус) — один раз на фигурку за шаг; соседи для толкотни — сетка на массивах без выделений
        public static void Step(IList<Mover> ms, double dt, Rules r)
        {
            var M = r.Men;
            foreach (var m in ms)
            {
                if (m.Men.Count == 0) continue;
                foreach (var s in m.Figs) { double h = FigHeading(m, s) * Math.PI / 180; s.Hc = Math.Cos(h); s.Hs = Math.Sin(h); s.Hd = FigHeading(m, s); }
                double amax = M.AccelK * MoveSim.FigAccel(m, r) * dt, time = m.Steps * dt;
                var F = m.Field;
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Fig == null) continue;
                    var s = man.Fig;
                    double lx = man.Lx, ly = man.Ly, hu = man.Hu;
                    if (m.Fleeing)
                    {
                        // толпа (Г70): места шире, каждый чуть виляет
                        lx *= M.FleeSpread; ly *= M.FleeSpread;
                        lx += Math.Sin(time * 2 * Math.PI / (2 + hu) + hu * 6.283) * M.FleeWanderM;
                    }
                    // место в мире: x = X + lx·(cos, sin) − ly·(sin, −cos)
                    double hx = s.X + lx * s.Hc - ly * s.Hs, hy = s.Y + lx * s.Hs + ly * s.Hc;
                    if (s.Fighting && man.Row == 0 && !m.Fleeing)
                    {
                        // Г78: передние касающихся фигурок — шаг к врагу и выпады вперёд-назад
                        double lunge = M.LungeM + M.LungeAmpM * Math.Sin(time * 2 * Math.PI / M.LungeSec + hu * 6.283);
                        hx += s.FightX * lunge; hy += s.FightY * lunge;
                    }
                    double vx = s.Vx + (hx - man.X) / M.Tau, vy = s.Vy + (hy - man.Y) / M.Tau;
                    double vmax = Math.Max(s.Vmax, M.WalkMin) * M.SpeedK, v2 = vx * vx + vy * vy;
                    if (v2 > vmax * vmax) { double k = vmax / Math.Sqrt(v2); vx *= k; vy *= k; }
                    double ax = vx - man.Vx, ay = vy - man.Vy, a2 = ax * ax + ay * ay;
                    if (a2 > amax * amax) { double k = amax / Math.Sqrt(a2); ax *= k; ay *= k; }
                    man.Vx += ax; man.Vy += ay;
                    double nx = man.X + man.Vx * dt, ny = man.Y + man.Vy * dt;
                    double ex = nx - hx, ey = ny - hy, e2 = ex * ex + ey * ey;
                    if (e2 > M.MaxLagM * M.MaxLagM) { double k = M.MaxLagM / Math.Sqrt(e2); nx = hx + ex * k; ny = hy + ey * k; }   // не отрывается от места
                    if (F == null || MoveSim.Free(F, nx, ny) || !MoveSim.Free(F, man.X, man.Y)) { man.X = nx; man.Y = ny; }
                    else { man.Vx = 0; man.Vy = 0; }
                    man.Facing = m.Fleeing && man.Vx * man.Vx + man.Vy * man.Vy > 0.25 ? MoveSim.HeadingOf(man.Vx, man.Vy) : s.Hd;
                }
            }
            Jostle(ms, r);
        }

        // Г77: толкотня — любые два бойца ближе суммы радиусов расходятся (поровну); радиус — доля шага в строю.
        // Сетка клеток 2 м по рамке всех бойцов: голова списка в клетке и «следующий» у бойца; пары — своя клетка и
        // четыре соседние (каждая пара — один раз)
        // свои у каждого потока: смотрелка считает бой в фоне, пока игра считает свой ход в главном потоке
        [ThreadStatic] static Man[] gMen; [ThreadStatic] static Mover[] gOwn; [ThreadStatic] static double[] gRad;
        [ThreadStatic] static int[] gNext, gHead;
        static void Jostle(IList<Mover> ms, Rules r)
        {
            var M = r.Men;
            const double cell = 2;
            int n = 0;
            foreach (var m in ms) n += m.Men.Count;
            if (n < 2) return;
            if (gMen == null || gMen.Length < n) { gMen = new Man[n * 2]; gOwn = new Mover[n * 2]; gRad = new double[n * 2]; gNext = new int[n * 2]; }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int c = 0;
            foreach (var m in ms)
            {
                if (m.Men.Count == 0) continue;
                var f = Grid(m.P.U, r);
                double rad = M.BodyShare * Math.Min(f.PerMan, f.RankDepth);
                foreach (var man in m.Men)
                {
                    if (!man.Alive) continue;
                    gMen[c] = man; gOwn[c] = m; gRad[c] = rad; c++;
                    if (man.X < x0) x0 = man.X; if (man.X > x1) x1 = man.X;
                    if (man.Y < y0) y0 = man.Y; if (man.Y > y1) y1 = man.Y;
                }
            }
            if (c < 2) return;
            int W = (int)((x1 - x0) / cell) + 1, H = (int)((y1 - y0) / cell) + 1;
            if ((long)W * H > 4_000_000) return;   // бойцы разбросаны на десятки километров — толкаться некому
            if (gHead == null || gHead.Length < W * H) gHead = new int[W * H];
            Array.Fill(gHead, -1, 0, W * H);
            for (int i = 0; i < c; i++)
            {
                int cx = (int)((gMen[i].X - x0) / cell), cy = (int)((gMen[i].Y - y0) / cell), k = cy * W + cx;
                gNext[i] = gHead[k]; gHead[k] = i;
            }
            for (int cy = 0; cy < H; cy++)
                for (int cx = 0; cx < W; cx++)
                {
                    int k0 = cy * W + cx;
                    for (int i = gHead[k0]; i >= 0; i = gNext[i])
                    {
                        for (int j = gNext[i]; j >= 0; j = gNext[j]) Pair(i, j, M);   // своя клетка
                        // соседи: справа, снизу-слева, снизу, снизу-справа — каждая пара клеток один раз
                        if (cx + 1 < W) for (int j = gHead[k0 + 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                        if (cy + 1 < H)
                        {
                            int kb = k0 + W;
                            if (cx > 0) for (int j = gHead[kb - 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                            for (int j = gHead[kb]; j >= 0; j = gNext[j]) Pair(i, j, M);
                            if (cx + 1 < W) for (int j = gHead[kb + 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                        }
                    }
                }
        }
        static void Pair(int i, int j, Rules.MenR M)
        {
            var p = gMen[i]; var q = gMen[j];
            double dx = q.X - p.X, dy = q.Y - p.Y, need = gRad[i] + gRad[j], d2 = dx * dx + dy * dy;
            if (d2 >= need * need) return;
            double d = Math.Sqrt(d2);
            if (d < 1e-9) { double ang = MoveSim.Hash01(p.Id, q.Id, 14) * 6.283; dx = Math.Cos(ang); dy = Math.Sin(ang); d = 1; }
            double push = (need - Math.Min(d, need)) * M.PushShare / 2, ux = dx / d, uy = dy / d;
            Nudge(gOwn[i], p, -ux * push, -uy * push);
            Nudge(gOwn[j], q, ux * push, uy * push);
        }
        static void Nudge(Mover m, Man man, double dx, double dy)
        {
            double nx = man.X + dx, ny = man.Y + dy;
            if (m.Field != null && !MoveSim.Free(m.Field, nx, ny) && MoveSim.Free(m.Field, man.X, man.Y)) return;   // в воду и в стену не выталкиваем
            man.X = nx; man.Y = ny;
        }
    }
}
