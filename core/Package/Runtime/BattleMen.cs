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
        double[] mgUx = new double[0], mgUy = new double[0];   // курс бойца — куда смотрит пика
        double[] mgAx = new double[0], mgAy = new double[0];   // ось капсулы тела — вдоль колонны (бегущего — по курсу), как у толкотни (Г87)
        static long CellKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        // Г90: натиск готов — кони с разбега пешему врагу не уступают. Те же условия, что у натиска стола (Г29, К29): приказ
        // «натиск», конница, натиск в этом ходу не тратился, разбег ≥ 50 м по чистому, под целью местность с натиском, не пики во
        // фронт. Идёт окно всплеска — готов до его конца; сцепились без натиска — нет
        bool ChargeReadyOf(Mover m)
        {
            if (!Alive(m) || m.Fleeing || m.Order == null || !m.Order.Charge || m.Order.Kind != OrderKind.Attack || !Units.IsCav(m.P.U)) return false;
            foreach (var f in Fights)
                if (!f.Over && (f.A == m || f.B == m))
                {
                    foreach (var w in f.Wins) if (w.Charge && w.Att == m && !w.Closed) return true;
                    if (f.Other(m).P.U.Id == m.Order.TargetId) return false;
                }
            if (chargesLeft.TryGetValue(m, out var left) && left <= 0) return false;
            if (BattleMap.RunUpBlock(m.P.U, R) != null) return false;
            var t = ById(m.Order.TargetId);
            if (t == null || !OnField(t)) return false;
            var g = GroundOf(t.P.X, t.P.Y);
            if (g != null && g.NoCharge) return false;
            if (Units.IsPike(t.P.U) && Math.Abs(MoveSim.AngleDiff(t.P.Facing, MoveSim.HeadingOf(m.P.X - t.P.X, m.P.Y - t.P.Y))) <= R.Sectors.FrontMax) return false;
            return true;
        }

        // Г105 (Алекс 10.10.2026: «ворота должны быть физичным объектом, который можно выбить»): бойцы врага у закрытых ворот без
        // противника рубят их в своём ритме ударов (SwingAt — для рисунка); прочность — участка укреплений (Fortify, Siege.Hp:
        // деревянные 40, окованные 80), GateHitPerSwing за удар на человека; обнулилась — пролом: клетки ворот становятся проломом,
        // проходимым всем, идущим приказ заново. Своих ворот хозяин не рубит; конница не рубит
        void GateStrikes(double t, double dt)
        {
            var map = Geo?.Map; if (map == null) return;
            byte gate = Terrain.Id("gate");
            if (!Terrain.HasAny(map, gate, gate)) return;
            Fortify.EnsureSections(map, R);
            if (map.S == null) return;
            var MR = R.Men; double per = R.Garrison.GateHitPerSwing; int W = map.W;
            Dictionary<int, double> dmg = null;
            for (int c = 0; c < map.T.Length; c++)
            {
                if (map.T[c] != gate || openGates.Contains(c) || map.S[c] == 0) continue;
                double gx0 = (c % W) * Terrain.CellM, gy0 = (c / W) * Terrain.CellM, gx1 = gx0 + Terrain.CellM, gy1 = gy0 + Terrain.CellM;
                foreach (var m in Movers)
                {
                    if (IsOwner(m.P.U) || !Alive(m) || m.Fleeing || m.Order == null || BattleMap.IsHorse(m.P.U)) continue;
                    if (JsMath.Hypot(m.P.X - gx0 - Terrain.CellM / 2, m.P.Y - gy0 - Terrain.CellM / 2) > JsMath.Hypot(m.P.Fp.Front, m.P.Fp.Depth) / 2 + 10) continue;
                    var f = R.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                    double rad = MR.BodyShare * Math.Min(f.PerMan, f.RankDepth);
                    foreach (var a in m.Men)
                    {
                        if (!a.Alive || a.Foe != null || a.DownLeft > 0) continue;
                        double ex = Math.Max(gx0 - a.X, Math.Max(0, a.X - gx1)), ey = Math.Max(gy0 - a.Y, Math.Max(0, a.Y - gy1));
                        if (JsMath.Hypot(ex, ey) - rad > MR.ReachM) continue;
                        if (double.IsNaN(a.NextSwing) || a.NextSwing < t - MR.SwingSec) a.NextSwing = t + MR.SwingSec * 0.5 * MoveSim.Hash01(m.P.U.Id, a.Id, 18);
                        if (a.NextSwing >= t + dt) continue;
                        a.SwingAt = a.NextSwing; a.SwingN++; MenMelee.Swings++;
                        a.NextSwing = a.SwingAt + MR.SwingSec * (0.75 + 0.5 * MoveSim.Hash01(m.P.U.Id, a.Id * 97 + a.SwingN, 19));
                        if (dmg == null) dmg = new Dictionary<int, double>();
                        dmg[map.S[c]] = (dmg.TryGetValue(map.S[c], out var v) ? v : 0) + per * a.Men;
                    }
                }
            }
            if (dmg == null) return;
            bool opened = false;
            foreach (var kv in dmg)
            {
                var hit = Fortify.DamageSection(map, kv.Key, kv.Value, double.NaN, double.NaN, R);
                if (hit != null && hit.Opened > 0) { opened = true; events.Add($"ворота выбиты — {Fortify.SectionName(Fortify.GetSection(map, kv.Key))}"); }
            }
            if (opened) { Terrain.Touched(map); RefreshPass(); }
        }
        // прочность ворот у точки для рисунка и интерфейса: (осталось, всего); null — ворот там нет или участков на карте нет
        public (double hp, double max)? GateHp(double x, double y)
        {
            var map = Geo?.Map; if (map == null) return null;
            Fortify.EnsureSections(map, R);
            var sec = Fortify.SectionAt(map, x / Geo.W, y / Geo.H);
            if (sec == null || !(sec.Kind == "gateWood" || sec.Kind == "gateIron")) return null;
            return (Fortify.SectionHp(sec, R), Fortify.SectionMax(sec, R));
        }

        // Касания бойцов: заполняет touches (по фигуркам, как Bodies.Touch) и menSec, ставит каждому бойцу противника
        void MenTouches()
        {
            var MR = R.Men; double reach = MR.ReachM, stepM = R.Garrison.StepM;
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
            var rad = new double[nm]; var half = new double[nm]; var halfBase = new double[nm]; var rankD = new double[nm]; var pike = new bool[nm];
            int c = 0; double maxBody = 0;
            for (int mi = 0; mi < nm; mi++)
            {
                var m = Movers[mi];
                if (!any[mi]) { foreach (var man in m.Men) man.Foe = null; continue; }
                var f = R.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                rad[mi] = MR.BodyShare * Math.Min(f.PerMan, f.RankDepth);
                half[mi] = Soldiers.BodyHalf(m, R); halfBase[mi] = Soldiers.BodyHalf(m, R, 1);   // Г87: тело из k человек — капсула вдоль колонны; half — самое длинное
                rankD[mi] = f.RankDepth; pike[mi] = Units.IsPike(m.P.U);
                bodyOf[m] = (rad[mi], half[mi], rankD[mi], pike[mi]);
                maxBody = Math.Max(maxBody, rad[mi] + half[mi]);
                c += m.Men.Count;
            }
            if (c == 0) return;
            if (mgMan.Length < c)
            {
                int cap = c * 2;
                mgMan = new Man[cap]; mgNext = new int[cap]; mgMover = new int[cap]; mgFig = new int[cap]; mgUx = new double[cap]; mgUy = new double[cap]; mgAx = new double[cap]; mgAy = new double[cap];
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
                    double h = man.Facing * Math.PI / 180;   // курс тела (Г94) — куда смотрит пика
                    double ah = m.Fleeing ? h : man.Fig.Hd * Math.PI / 180;   // капсула тела (k человек, конь) лежит вдоль колонны
                    mgMan[c] = man; mgMover[c] = mi; mgFig[c] = k; mgUx[c] = Math.Sin(h); mgUy[c] = -Math.Cos(h); mgAx[c] = Math.Sin(ah); mgAy[c] = -Math.Cos(ah);
                    long key = CellKey((int)Math.Floor(man.X / cell), (int)Math.Floor(man.Y / cell));
                    mgNext[c] = mgHead.TryGetValue(key, out int head) ? head : -1;
                    mgHead[key] = c;
                    c++;
                }
            }
            // 3) каждому бойцу — противник: прежний, если ещё в досягаемости, иначе ближний
            // пики (Г90): острия на PikeTipM впереди первой шеренги, задние шеренги достают через головы своих
            double pikeMax = 0;
            for (int mi = 0; mi < nm; mi++) if (any[mi] && pike[mi]) pikeMax = Math.Max(pikeMax, MR.PikeTipM + (MR.PikeRanks - 1) * rankD[mi]);
            double cosFront = Math.Cos(R.Sectors.FrontMax * Math.PI / 180);
            for (int i = 0; i < c; i++)
            {
                var man = mgMan[i]; int xi = mgMover[i]; var x = Movers[xi];
                if (man.DownLeft > 0) { man.Foe = null; continue; }   // сбит с ног (Г90) — не бьётся
                double extra = pike[xi] && man.Row < MR.PikeRanks ? MR.PikeTipM + man.Row * rankD[xi] : 0;
                int ring = (int)Math.Ceiling((2 * maxBody + reach + Math.Max(extra, pikeMax)) / cell);
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
                            var oj = mgMan[j];
                            if (Math.Abs(oj.Z - man.Z) > stepM) continue;   // Г104: со стены вниз и снизу на стену — не достать (до приступа)
                            double extraJ = pike[yj] && oj.Row < MR.PikeRanks && oj.DownLeft <= 0 ? MR.PikeTipM + oj.Row * rankD[yj] : 0;
                            double ex = oj.X - man.X, ey = oj.Y - man.Y, lim = rad[xi] + half[xi] + rad[yj] + half[yj] + reach + Math.Max(extra, extraJ);
                            if (ex * ex + ey * ey > lim * lim) continue;
                            double gap = Gap(i, rad[xi], halfBase[xi] + (man.Men - 1) * rankD[xi] / 2, j, rad[yj], halfBase[yj] + (oj.Men - 1) * rankD[yj] / 2, out double dx, out double dy);
                            bool can = gap <= reach;
                            double dl = Math.Sqrt(dx * dx + dy * dy);
                            // пика — только вперёд: враг в пределах FrontMax от курса пикинёра; чья пика достаёт — с тем и бьётся (у острия)
                            if (!can && extra > 0 && gap <= reach + extra) can = dl > 1e-9 && (dx * mgUx[i] + dy * mgUy[i]) / dl >= cosFront;
                            if (!can && extraJ > 0 && gap <= reach + extraJ) can = dl > 1e-9 && (-dx * mgUx[j] - dy * mgUy[j]) / dl >= cosFront;
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
            double d2 = Bodies.SegSeg(a.X - mgAx[i] * hi, a.Y - mgAy[i] * hi, a.X + mgAx[i] * hi, a.Y + mgAy[i] * hi,
                                      b.X - mgAx[j] * hj, b.Y - mgAy[j] * hj, b.X + mgAx[j] * hj, b.Y + mgAy[j] * hj,
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
                if (d == null || !a.Alive || !d.Alive || !Alive(am) || dm.Gone || a.DownLeft > 0) continue;
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
                a.NextSwing = at + MR.SwingSec * (0.75 + 0.5 * MoveSim.Hash01(am.P.U.Id, a.Id * 97 + a.SwingN, 19)) / Math.Max(1, a.Men - a.Lost);   // Г87: тело бьёт за своих людей
                double dx = d.X - a.X, dy = d.Y - a.Y, dl = JsMath.Hypot(dx, dy);
                double ux = dl > 1e-9 ? dx / dl : 0, uy = dl > 1e-9 ? dy / dl : 0;
                // противник назначен на касании (раз в ContactEverySec) — с тех пор мог отойти: дальше досягаемости — мимо
                if (bodyOf.TryGetValue(am, out var ba) && bodyOf.TryGetValue(dm, out var bd))
                {
                    double reach = MR.ReachM + (ba.pike && a.Row < MR.PikeRanks ? MR.PikeTipM + a.Row * ba.rankD : 0);
                    if (bd.pike && d.Row < MR.PikeRanks) reach = Math.Max(reach, MR.ReachM + MR.PikeTipM + d.Row * bd.rankD);   // бьётся с остриём
                    if (dl - ba.rad - ba.half - bd.rad - bd.half > reach + 0.5) continue;
                }
                if (Owed(dm) >= 1) { Fell(dm, d, at, ux, uy); dm.StruckDown++; MenMelee.Hits++; }
                else { d.ParryAt = at; d.Vx += ux * MR.RecoilMps; d.Vy += uy * MR.RecoilMps; MenMelee.Parries++; }
            }
        }
        // Сколько людей стол уже снял, а бойцы ещё стоят
        int Owed(Mover m) => m.LaidMen - ((int)Math.Max(0, Js.Round(m.P.U.Soldiers)) - m.LeftMen) - m.ShotDown - m.StruckDown;

        // Боец пал от удара: лицом к врагу, кровь — по удару (от врага). Убит или ранен — как у павших в раскладке.
        // Г87: тело из k человек — выбыл один из них (павший на месте тела), тело падает с последним
        void Fell(Mover m, Man x, double t, double ux, double uy)
        {
            x.Wound();
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
