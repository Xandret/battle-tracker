// ═══════════ BattleMen.cs — рукопашная по бойцам (Б2: Г83, Г91) — ЧЕРНОВИК ДО ГМа ═══════════
// При Rules.Move.MenBodies касание и павшие — по бойцам; сколько выбыло — по-прежнему стол (Strike: окна ударов, Г88).
// Касание (раз в ContactEverySec): боец касается врага, если зазор между их телами не больше Men.ReachM; пикинёр
// достаёт из Men.PikeRanks шеренг вперёд. Колонна бьётся, если касается хоть один её боец (Г27, Г85); фронт, фланг и тыл —
// по тому, сколько её касающихся бойцов стоит перед строем врага, сбоку и сзади (Г83).
// Удары (каждый шаг): касающийся держится за своего противника — ближнего в досягаемости, пока тот жив и рядом, — и бьёт
// в своём ритме. У врага есть неотданные потери (стол уже снял людей, а бойцы ещё стоят) — ударенный падает; нет — удар
// принят на щит, ударенный отшатывается. Ударов не хватило (натиск, всплеск потерь) — в раскладке падают те, кого бьют,
// потом касающиеся, потом ближние к врагу. Бегущий не бьёт (Г70), но его бьют.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed partial class Battle
    {
        bool MenMode => R.Move.MenBodies;
        // Счёт ударов с начала боя: замахов, попаданий (ударенный пал), принятых на щит; павших в раскладке без удара
        // (ударов не хватило)
        public sealed class MenMeleeStats { public int Swings, Hits, Parries, Fallback; }
        public MenMeleeStats MenMelee = new MenMeleeStats();
        // кто кого бьёт — заново на каждом касании; бьют только отряды в строю
        readonly List<(Mover am, Man a, Mover dm)> strikers = new List<(Mover am, Man a, Mover dm)>();
        // касающиеся бойцы x у врага y, по фигуркам x: сколько стоят перед строем врага, сбоку, сзади
        readonly Dictionary<(Mover x, Mover y), (int front, int flank, int rear)[]> menSec = new Dictionary<(Mover x, Mover y), (int front, int flank, int rear)[]>();
        // тела бойцов отряда: радиус, полудлина (конь), глубина шеренги, пики — для досягаемости удара
        readonly Dictionary<Mover, (double rad, double half, double rankD, bool pike)> bodyOf = new Dictionary<Mover, (double rad, double half, double rankD, bool pike)>();

        // сетка бойцов на касание: голова клетки — словарь (поле большое, бойцов мало), «следующий» — массив
        readonly Dictionary<long, int> mgHead = new Dictionary<long, int>();
        Man[] mgMan = new Man[0]; int[] mgNext = new int[0], mgMover = new int[0], mgFig = new int[0];
        double[] mgUx = new double[0], mgUy = new double[0];
        static long CellKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        // Касания бойцов: заполняет touches (по фигуркам, как Bodies.Touch) и menSec, ставит каждому бойцу противника
        void MenTouches()
        {
            var MR = R.Men; double reach = MR.ReachM;
            strikers.Clear(); menSec.Clear();
            int nm = Movers.Count;
            // 1) пары врагов поблизости — как у фигурок
            var near = new bool[nm, nm]; var any = new bool[nm];
            for (int i = 0; i < nm; i++)
                for (int j = i + 1; j < nm; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    if (!(OnField(x) && OnField(y) && (Alive(x) || Alive(y)) && Enemies(x.P.U, y.P.U))) continue;
                    double rr = (JsMath.Hypot(x.P.Fp.Front, x.P.Fp.Depth) + JsMath.Hypot(y.P.Fp.Front, y.P.Fp.Depth)) / 2 + R.Map.MeleeGap + 10 + WrapReach(x) + WrapReach(y);
                    if (JsMath.Hypot(x.P.X - y.P.X, x.P.Y - y.P.Y) > rr) continue;
                    near[i, j] = near[j, i] = true; any[i] = any[j] = true;
                    touches[(x, y)] = Fresh(x.Figs.Count); touches[(y, x)] = Fresh(y.Figs.Count);
                    menSec[(x, y)] = new (int, int, int)[x.Figs.Count]; menSec[(y, x)] = new (int, int, int)[y.Figs.Count];
                }
            // 2) тела бойцов этих отрядов — в сетку
            var rad = new double[nm]; var half = new double[nm]; var rankD = new double[nm]; var pike = new bool[nm];
            int c = 0; double maxBody = 0;
            for (int mi = 0; mi < nm; mi++)
            {
                var m = Movers[mi];
                if (!any[mi]) { foreach (var man in m.Men) man.Foe = null; continue; }
                var f = R.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                rad[mi] = MR.BodyShare * Math.Min(f.PerMan, f.RankDepth);
                half[mi] = BattleMap.IsHorse(m.P.U) ? R.Move.HorseHalfShare * f.RankDepth : 0;
                rankD[mi] = f.RankDepth; pike[mi] = Units.IsPike(m.P.U);
                bodyOf[m] = (rad[mi], half[mi], rankD[mi], pike[mi]);
                maxBody = Math.Max(maxBody, rad[mi] + half[mi]);
                c += m.Men.Count;
            }
            if (c == 0) return;
            if (mgMan.Length < c)
            {
                int cap = c * 2;
                mgMan = new Man[cap]; mgNext = new int[cap]; mgMover = new int[cap]; mgFig = new int[cap]; mgUx = new double[cap]; mgUy = new double[cap];
            }
            double cell = 2 * maxBody + reach;
            mgHead.Clear(); c = 0;
            for (int mi = 0; mi < nm; mi++)
            {
                if (!any[mi]) continue;
                var m = Movers[mi];
                var idx = new Dictionary<FigState, int>();
                for (int k = 0; k < m.Figs.Count; k++) idx[m.Figs[k]] = k;
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Fig == null || !idx.TryGetValue(man.Fig, out int k)) { man.Foe = null; continue; }
                    double h = man.Facing * Math.PI / 180;
                    mgMan[c] = man; mgMover[c] = mi; mgFig[c] = k; mgUx[c] = Math.Sin(h); mgUy[c] = -Math.Cos(h);
                    long key = CellKey((int)Math.Floor(man.X / cell), (int)Math.Floor(man.Y / cell));
                    mgNext[c] = mgHead.TryGetValue(key, out int head) ? head : -1;
                    mgHead[key] = c;
                    c++;
                }
            }
            // 3) каждому бойцу — противник: прежний, если ещё в досягаемости, иначе ближний
            for (int i = 0; i < c; i++)
            {
                var man = mgMan[i]; int xi = mgMover[i]; var x = Movers[xi];
                double extra = pike[xi] && man.Row < MR.PikeRanks ? man.Row * rankD[xi] : 0;
                int ring = (int)Math.Ceiling((2 * maxBody + reach + extra) / cell);
                int cx = (int)Math.Floor(man.X / cell), cy = (int)Math.Floor(man.Y / cell);
                int best = -1; double bestGap = double.MaxValue; bool keep = false;
                for (int gy = cy - ring; gy <= cy + ring && !keep; gy++)
                    for (int gx = cx - ring; gx <= cx + ring && !keep; gx++)
                    {
                        if (!mgHead.TryGetValue(CellKey(gx, gy), out int j)) continue;
                        for (; j >= 0; j = mgNext[j])
                        {
                            int yj = mgMover[j];
                            if (!near[xi, yj]) continue;
                            // отсев по квадрату расстояния — точный зазор только для тех, кто может достать
                            double ex = mgMan[j].X - man.X, ey = mgMan[j].Y - man.Y, lim = rad[xi] + half[xi] + rad[yj] + half[yj] + reach + extra;
                            if (ex * ex + ey * ey > lim * lim) continue;
                            double gap = Gap(i, rad[xi], half[xi], j, rad[yj], half[yj], out double dx, out double dy);
                            bool can = gap <= reach;
                            // пика — из задних шеренг, только вперёд: враг в пределах FrontMax от курса бойца
                            if (!can && extra > 0 && gap <= reach + extra)
                            {
                                double dl = Math.Sqrt(dx * dx + dy * dy);
                                can = dl > 1e-9 && (dx * mgUx[i] + dy * mgUy[i]) / dl >= Math.Cos(R.Sectors.FrontMax * Math.PI / 180);
                            }
                            if (!can) continue;
                            if (mgMan[j] == man.Foe) { best = j; bestGap = gap; keep = true; break; }   // держится за своего
                            if (gap < bestGap || gap == bestGap && j < best) { best = j; bestGap = gap; }
                        }
                    }
                if (best < 0) { man.Foe = null; continue; }
                var foe = mgMan[best]; var y = Movers[mgMover[best]];
                man.Foe = foe;
                int k = mgFig[i];
                var t = touches[(x, y)];
                if (t[k].ky < 0 || bestGap < t[k].d) t[k] = (mgFig[best], bestGap);
                // сектор — по тому, где боец стоит относительно строя врага: прямо перед ним — фронт, прямо за — тыл
                y.P.ToLocal(man.X, man.Y, out var lx, out var ly);
                var sec = menSec[(x, y)];
                if (Math.Abs(lx) - y.P.Fp.Front / 2 > 0) sec[k].flank++;
                else if (ly < 0) sec[k].front++;
                else sec[k].rear++;
                if (Alive(x)) strikers.Add((x, man, y));   // бегущий не бьёт (Г70)
            }
        }
        static (int ky, double d)[] Fresh(int n)
        {
            var a = new (int ky, double d)[n];
            for (int k = 0; k < n; k++) a[k] = (-1, double.PositiveInfinity);
            return a;
        }
        // Зазор между телами бойцов сетки i и j (минус — перекрытие); d — от i к j (между центрами)
        double Gap(int i, double ri, double hi, int j, double rj, double hj, out double dx, out double dy)
        {
            Man a = mgMan[i], b = mgMan[j];
            dx = b.X - a.X; dy = b.Y - a.Y;
            if (hi == 0 && hj == 0) return Math.Sqrt(dx * dx + dy * dy) - ri - rj;
            double d2 = Bodies.SegSeg(a.X - mgUx[i] * hi, a.Y - mgUy[i] * hi, a.X + mgUx[i] * hi, a.Y + mgUy[i] * hi,
                                      b.X - mgUx[j] * hj, b.Y - mgUy[j] * hj, b.X + mgUx[j] * hj, b.Y + mgUy[j] * hj,
                                      out _, out _, out _, out _);
            return Math.Sqrt(d2) - ri - rj;
        }

        // Удары этого шага — по порядку во времени: у врага есть неотданные потери — ударенный падает, нет — щит и отшатнулся
        void MenSwings(double t, double dt)
        {
            var MR = R.Men;
            var due = new List<(double at, int n)>();
            for (int n = 0; n < strikers.Count; n++)
            {
                var (am, a, dm) = strikers[n];
                var d = a.Foe;
                if (d == null || !a.Alive || !d.Alive || !Alive(am) || dm.Gone) continue;
                // новый противник после передышки — замах: первый удар не сразу
                if (double.IsNaN(a.NextSwing) || a.NextSwing < t - MR.SwingSec) a.NextSwing = t + MR.SwingSec * 0.5 * MoveSim.Hash01(am.P.U.Id, a.Id, 18);
                if (a.NextSwing < t + dt) due.Add((Math.Max(t, a.NextSwing), n));
            }
            foreach (var (at, n) in due.OrderBy(q => q.at).ThenBy(q => q.n))
            {
                var (am, a, dm) = strikers[n];
                var d = a.Foe;
                if (!a.Alive || d == null || !d.Alive) continue;   // пал раньше в этом же шаге
                a.SwingAt = at; a.SwingN++; MenMelee.Swings++;
                a.NextSwing = at + MR.SwingSec * (0.75 + 0.5 * MoveSim.Hash01(am.P.U.Id, a.Id * 97 + a.SwingN, 19));
                double dx = d.X - a.X, dy = d.Y - a.Y, dl = JsMath.Hypot(dx, dy);
                double ux = dl > 1e-9 ? dx / dl : 0, uy = dl > 1e-9 ? dy / dl : 0;
                // противник назначен на касании (раз в ContactEverySec) — с тех пор мог отойти: дальше досягаемости — мимо
                if (bodyOf.TryGetValue(am, out var ba) && bodyOf.TryGetValue(dm, out var bd))
                {
                    double reach = MR.ReachM + (ba.pike && a.Row < MR.PikeRanks ? a.Row * ba.rankD : 0);
                    if (dl - ba.rad - ba.half - bd.rad - bd.half > reach + 0.5) continue;
                }
                if (Owed(dm) >= 1) { Fell(dm, d, at, ux, uy); dm.StruckDown++; MenMelee.Hits++; }
                else { d.ParryAt = at; d.Vx += ux * MR.RecoilMps; d.Vy += uy * MR.RecoilMps; MenMelee.Parries++; }
            }
        }
        // Сколько людей стол уже снял, а бойцы ещё стоят
        int Owed(Mover m) => m.LaidMen - ((int)Math.Max(0, Js.Round(m.P.U.Soldiers)) - m.LeftMen) - m.ShotDown - m.StruckDown;

        // Боец пал от удара: лицом к врагу, кровь — по удару (от врага). Убит или ранен — как у павших в раскладке
        void Fell(Mover m, Man x, double t, double ux, double uy)
        {
            x.Alive = false; x.Foe = null;
            if (x.Body != null) x.Body.Alive = false;
            double u = look();
            Deaths.Add(new Death
            {
                X = x.X, Y = x.Y, T = t, Facing = x.Facing + (look() - 0.5) * 60, Dir = Math.Atan2(uy, ux) * 180 / Math.PI + (look() - 0.5) * 50,
                UnitId = m.P.U.Id, ManId = x.Id, Part = u < 0.25 ? "head" : u < 0.8 ? "torso" : "legs",
                Killed = MoveSim.Hash01(m.P.U.Id, x.Id, 16) < KilledShare(m),
            });
        }

        // Кого бьют бойцы врага (для раскладки: ударов не хватило — падают первыми они)
        HashSet<Man> StruckAt(Mover m)
        {
            var hit = new HashSet<Man>();
            foreach (var (_, a, dm) in strikers) if (dm == m && a.Alive && a.Foe != null && a.Foe.Alive) hit.Add(a.Foe);
            return hit;
        }
    }
}
