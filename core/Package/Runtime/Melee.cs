// ═══════════ Melee.cs — поштучная рукопашная за ход (И1; Г17, Г26–Г30) ═══════════
// Два отряда стоят на поле фигурками (Formation.Layout). Ход — 15 с фиксированными шагами (Г43).
// Удары стола растянуты во времени: у каждой стороны за ход столько «окон удара», сколько за столом
// атак и ответных ударов. Внутри окна урон течёт каждый шаг по формуле стола (Combat.StrikeDamage):
// бросок = доля удачи (Г26) × бойцы в начале окна × доля бойцов в колоннах, что касаются врага (Г27). В конце окна —
// округление до солдат, бросок летальности и штраф БД, ровно как после удара за столом.
// Натиск (Г29) — отдельное окно в первые 2 с контакта; цель в это время не бьёт.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public enum FortuneMode { PerStrike, PerTurn }   // бросок удачи: на каждый удар / один на весь ход

    public sealed class MeleeOptions
    {
        public double TurnSec = 15, Dt = 0.1, BurstSec = 2, ContactEverySec = 0.5;
        public double MenPerFigure = 10;
        public FortuneMode Fortune = FortuneMode.PerStrike;
        public bool FullContact;   // калибровка (Г27): все колонны в бою, секторы — по расстановке
    }

    // Отряд на поле: центр строя (м), фасинг (0° — вверх, по часовой), уровень высоты, фигурки
    public sealed class Placed
    {
        public Unit U;
        public double X, Y, Facing, Level;
        public List<Figure> Figs = new List<Figure>();
        public Footprint Fp = new Footprint();
        public double Engaged;                          // доля бойцов в колоннах, что касаются врага
        public double FrontFrac, FlankFrac, RearFrac;   // как касаются: во фронт, во фланг, в тыл врага
        int laidOut = -1;

        public void Relayout(Rules r, double menPerFigure, bool force = false)
        {
            int n = (int)Math.Max(0, Js.Round(U.Soldiers));
            if (n == laidOut && !force) return;   // force — строй сменился при тех же людях (Г101, Г104)
            laidOut = n;
            Figs = Formation.Layout(U, menPerFigure, r);
            Fp = Formation.Of(U, r);
        }

        // Оси строя в мире: вправо вдоль фронта и вперёд
        void Axes(out double rx, out double ry, out double fx, out double fy)
        {
            double t = Facing * Math.PI / 180;
            rx = Math.Cos(t); ry = Math.Sin(t); fx = Math.Sin(t); fy = -Math.Cos(t);
        }
        public void ToWorld(double lx, double ly, out double wx, out double wy)
        {
            Axes(out var rx, out var ry, out var fx, out var fy);
            wx = X + lx * rx - ly * fx; wy = Y + lx * ry - ly * fy;
        }
        public void ToLocal(double wx, double wy, out double lx, out double ly)
        {
            Axes(out var rx, out var ry, out var fx, out var fy);
            double dx = wx - X, dy = wy - Y;
            lx = dx * rx + dy * ry; ly = -(dx * fx + dy * fy);
        }
    }

    public static class MeleeSim
    {
        sealed class Win
        {
            public Placed Att, Def;
            public bool Counter, Charge;
            public double Extra = 1, Factor = 1;   // Extra — пики против конницы ×3; Factor — доля фронтальных касаний врага
            public double T0, T1, U, N0, Acc;   // N0 — бойцов у бьющего в начале окна
            public bool Open, Closed, Skipped;
        }

        // Расстановка для стычки: B в (0, 0) лицом вверх, A — спереди, сбоку или сзади, в 0,5 м от края B
        public static (Placed a, Placed b) Setup(Unit a0, Unit b0, TurnSetup s, MeleeOptions o, Rules r)
        {
            var a = new Placed { U = a0.Clone() };
            var b = new Placed { U = b0.Clone() };
            a.U.OnMap = b.U.OnMap = false;
            a.Relayout(r, o.MenPerFigure); b.Relayout(r, o.MenPerFigure);
            const double gap = 0.5;
            if (s.SectorA == "flank") { a.X = -(b.Fp.Front / 2 + gap + a.Fp.Depth / 2); a.Y = 0; a.Facing = 90; }
            else if (s.SectorA == "rear") { a.X = 0; a.Y = b.Fp.Depth / 2 + gap + a.Fp.Depth / 2; a.Facing = 0; }
            else { a.X = 0; a.Y = -(b.Fp.Depth / 2 + gap + a.Fp.Depth / 2); a.Facing = 180; }
            if (s.Ground == Ground.Hill) a.Level = 1;
            return (a, b);
        }

        // Кто кого касается (Г27): колонна фигурок в бою, если хоть одна её фигурка ближе meleeGap к строю врага;
        // бьёт вся колонна. Сектор — по ближайшей точке касания: прямо перед строем врага — фронт, прямо за ним — тыл.
        public static void Contact(Placed x, Placed y, Rules r, bool full)
        {
            double gap = r.Map.MeleeGap;
            double total = 0, engaged = 0, front = 0, flank = 0, rear = 0;
            var colMen = new Dictionary<int, double>();
            var colSector = new Dictionary<int, (double d, string s)>();
            foreach (var f in x.Figs)
            {
                total += f.Men;
                colMen[f.File] = (colMen.TryGetValue(f.File, out var m) ? m : 0) + f.Men;
                double best = double.MaxValue; string sec = "front";
                for (int k = 0; k < 5; k++)
                {
                    double lx = f.X + (k == 0 ? 0 : (k % 2 == 1 ? -0.5 : 0.5) * f.Width);
                    double ly = f.Y + (k == 0 ? 0 : (k <= 2 ? -0.5 : 0.5) * f.Depth);
                    x.ToWorld(lx, ly, out var wx, out var wy);
                    y.ToLocal(wx, wy, out var yx, out var yy);
                    double ex = Math.Abs(yx) - y.Fp.Front / 2, ey = Math.Abs(yy) - y.Fp.Depth / 2;
                    double d = Math.Sqrt(Math.Max(0, ex) * Math.Max(0, ex) + Math.Max(0, ey) * Math.Max(0, ey));
                    // фронт и тыл — только прямо перед строем и прямо за ним; всё сбоку, включая углы, — фланг
                    if (d < best) { best = d; sec = ex <= 0 ? (yy < 0 ? "front" : "rear") : "flank"; }
                }
                if (best <= gap && (!colSector.TryGetValue(f.File, out var cs) || best < cs.d)) colSector[f.File] = (best, sec);
            }
            foreach (var kv in colSector)
            {
                double m = colMen[kv.Key];
                engaged += m;
                if (kv.Value.s == "front") front += m; else if (kv.Value.s == "flank") flank += m; else rear += m;
            }
            foreach (var f in x.Figs) f.Engaged = colSector.ContainsKey(f.File);
            x.Engaged = total > 0 ? (full ? (engaged > 0 ? 1 : 0) : engaged / total) : 0;
            x.FrontFrac = engaged > 0 ? front / engaged : 0;
            x.FlankFrac = engaged > 0 ? flank / engaged : 0;
            x.RearFrac = engaged > 0 ? rear / engaged : 0;
        }

        static double Overlap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

        // Доля удачи (Г26): как бросок d(численность) за столом, с минимумом по дисциплине
        internal static double Fortune(Unit att, string mode, EngineContext ctx)
        {
            double n = Math.Max(1, Js.Round(att.Soldiers));
            var A = Combat.Eff(att, mode, null, ctx);
            double roll = Math.Max(Dice.Roll(ctx.Rng, n), Js.Round(A.Discipline * ctx.Rules.RollFloorPerDisc));
            return roll / n;
        }

        public static TurnOutcome Turn(Placed a, Placed b, bool chargeA, Ground ground, EngineContext ctx, MeleeOptions o)
        {
            var R = ctx.Rules;
            var outc = new TurnOutcome();
            string mode = ground == Ground.Forest ? Modes.MeleeRough : Modes.MeleeForm;
            var opts = new BattleRequest { Mode = mode, FatigueMode = "percent" };
            double startA = a.U.Soldiers, startB = b.U.Soldiers;
            var H = R.Map.Height;
            var down = new StrikeMod { Mult = H.DownhillMelee, Note = "⛰ Удар сверху вниз" };
            var up = new StrikeMod { Mult = H.UphillMelee, Note = "⛰ Удар снизу вверх" };
            StrikeMod ModFor(Placed x, Placed y) => x.Level > y.Level ? down : x.Level < y.Level ? up : null;

            a.Relayout(R, o.MenPerFigure); b.Relayout(R, o.MenPerFigure);
            Contact(a, b, R, o.FullContact); Contact(b, a, R, o.FullContact);
            outc.EngagedA = a.Engaged; outc.EngagedB = b.Engaged;

            // ── расписание окон удара: «обмены», как за столом ──
            // Ход стола — череда обменов: сначала атаки A (на каждую B отвечает, если удар пришёл ему во фронт
            // и ответы не кончились), потом атаки B (отвечает A). Здесь обмены идут друг за другом в тех же 15 с;
            // атака и ответ одного обмена текут одновременно. A — тот, кто вошёл в контакт.
            bool PikeStop(Placed x, Placed y) => Units.IsCav(x.U) && Units.IsPike(y.U) && x.FrontFrac > 0;
            bool burstA = chargeA && Units.IsCav(a.U) && a.Engaged > 0 && !PikeStop(a, b);
            double tStart = burstA ? o.BurstSec : 0;
            var wins = new List<Win>();
            // натиск (Г29): первая атака A — всплеск в первые секунды, без ответа
            if (burstA) wins.Add(new Win { Att = a, Def = b, Charge = true, T0 = 0, T1 = o.BurstSec });
            var exchanges = new List<(Placed x, Placed y)>();
            int atkA = a.Engaged > 0 ? (int)Units.AttackLimit(a.U, R) - (burstA ? 1 : 0) : 0;
            int atkB = b.Engaged > 0 ? (int)Units.AttackLimit(b.U, R) : 0;
            for (int i = 0; i < atkA; i++) exchanges.Add((a, b));
            for (int i = 0; i < atkB; i++) exchanges.Add((b, a));
            var countersLeft = new Dictionary<Placed, int> { [a] = (int)Units.CounterLimit(a.U, R), [b] = (int)Units.CounterLimit(b.U, R) };
            double len = (o.TurnSec - tStart) / Math.Max(1, exchanges.Count);
            for (int j = 0; j < exchanges.Count; j++)
            {
                var (x, y) = exchanges[j];
                double t0w = tStart + j * len, t1w = tStart + (j + 1) * len;
                wins.Add(new Win { Att = x, Def = y, T0 = t0w, T1 = t1w });
                // y отвечает на удар x, пришедший ему во фронт (удар во фланг и тыл глушит ответ, Г17)
                if (y.Engaged > 0 && x.FrontFrac > 0 && countersLeft[y] > 0)
                {
                    countersLeft[y]--;
                    wins.Add(new Win
                    {
                        Att = y, Def = x, Counter = true, Factor = x.FrontFrac, T0 = t0w, T1 = t1w,
                        Extra = PikeStop(x, y) ? R.PikeCounterMult : 1,
                    });
                }
            }

            var unitU = new Dictionary<Placed, double>();
            if (o.Fortune == FortuneMode.PerTurn)
            {
                unitU[a] = Fortune(a.U, mode, ctx);
                unitU[b] = Fortune(b.U, mode, ctx);
            }

            var scratch = new List<string>();
            int steps = (int)Math.Round(o.TurnSec / o.Dt);
            int contactEvery = Math.Max(1, (int)Math.Round(o.ContactEverySec / o.Dt));
            var inc = new double[wins.Count];
            outc.Timeline.Add(new[] { 0, a.U.Soldiers, b.U.Soldiers });

            for (int k = 0; k < steps; k++)
            {
                double t0 = k * o.Dt, t1 = t0 + o.Dt;
                if (!o.FullContact && k > 0 && k % contactEvery == 0)
                {
                    a.Relayout(R, o.MenPerFigure); b.Relayout(R, o.MenPerFigure);
                    Contact(a, b, R, false); Contact(b, a, R, false);
                }
                // открываем окна
                foreach (var w in wins)
                {
                    if (w.Open || w.Skipped || w.T0 >= t1 - 1e-9) continue;
                    if (w.Att.U.Status != "active" || w.Def.U.Status == "destroyed" || w.Def.U.Soldiers <= 0) { w.Skipped = true; continue; }
                    if (w.Counter && w.Att.U.Morale < R.Morale.ShakenBelow) { w.Skipped = true; continue; }   // дрогнувший не отвечает
                    w.Open = true;
                    w.U = o.Fortune == FortuneMode.PerTurn ? unitU[w.Att] : Fortune(w.Att.U, mode, ctx);
                    w.N0 = w.Att.U.Soldiers;
                }
                // урон за шаг — от состояния на начало шага, обе стороны одновременно
                var effA = Combat.Eff(a.U, mode, null, ctx); var effB = Combat.Eff(b.U, mode, null, ctx);
                double toA = 0, toB = 0;
                for (int i = 0; i < wins.Count; i++)
                {
                    var w = wins[i]; inc[i] = 0;
                    if (!w.Open || w.Closed) continue;
                    double portion = Overlap(w.T0, w.T1, t0, t1) / (w.T1 - w.T0);
                    if (portion <= 0) continue;
                    var At = w.Att == a ? effA : effB; var Df = w.Att == a ? effB : effA;
                    if (w.Att.U.Soldiers <= 0) continue;
                    // сила удара — по численности в начале окна, как за столом («по численности до потерь»)
                    double roll = w.U * w.N0 * w.Att.Engaged;
                    var mod = ModFor(w.Att, w.Def);
                    double dmg = 0;
                    if (w.Counter)
                        dmg = Combat.StrikeDamage(w.Att.U, w.Def.U, At, Df, opts, null, ctx, false, w.Extra, "", "front", mod, roll * w.Factor);
                    else
                    {
                        var x = w.Att;
                        if (x.FrontFrac > 0) dmg += x.FrontFrac * Combat.StrikeDamage(x.U, w.Def.U, At, Df, opts, null, ctx, w.Charge, 1, "", "front", mod, roll);
                        if (x.FlankFrac > 0) dmg += x.FlankFrac * Combat.StrikeDamage(x.U, w.Def.U, At, Df, opts, null, ctx, w.Charge, 1, "", "flank", mod, roll);
                        if (x.RearFrac > 0) dmg += x.RearFrac * Combat.StrikeDamage(x.U, w.Def.U, At, Df, opts, null, ctx, w.Charge, 1, "", "rear", mod, roll);
                    }
                    inc[i] = Math.Max(0, dmg) * portion;
                    if (w.Def == a) toA += inc[i]; else toB += inc[i];
                }
                double kA = toA > a.U.Soldiers && toA > 0 ? a.U.Soldiers / toA : 1;
                double kB = toB > b.U.Soldiers && toB > 0 ? b.U.Soldiers / toB : 1;
                for (int i = 0; i < wins.Count; i++)
                {
                    if (inc[i] <= 0) continue;
                    var w = wins[i];
                    double take = inc[i] * (w.Def == a ? kA : kB);
                    w.Acc += take;
                    w.Def.U.Soldiers -= take;
                }
                // закрываем окна: округление, летальность, штраф БД — как после удара за столом
                foreach (var w in wins)
                {
                    if (!w.Open || w.Closed || w.T1 > t1 + 1e-9) continue;
                    w.Closed = true;
                    var def = w.Def.U;
                    double before = def.Soldiers + w.Acc;
                    double cas = Math.Min(Js.Round(w.Acc), Math.Floor(before + 1e-9));
                    var clone = def.Clone(); clone.Soldiers = before;
                    var res = Combat.Casualties(clone, cas, null, ctx);
                    scratch.Clear();
                    var patch = Combat.CasualtyPatch(clone, res, scratch, ctx, out _);
                    patch.ApplyTo(def);
                    // сотни дробных вычитаний оставляют хвост вроде 761.0000000001 — снимаем его
                    double whole = Math.Round(def.Soldiers);
                    if (Math.Abs(def.Soldiers - whole) < 1e-6) def.Soldiers = whole;
                    outc.Strikes++;
                    outc.Log.Add($"{Js.Num(Js.R1(w.T1))} с · {w.Att.U.Name} → {def.Name}{(w.Charge ? " (натиск)" : w.Counter ? " (ответ)" : "")}: −{Js.Num(cas)}");
                }
                if (Math.Abs(t1 - Math.Round(t1)) < 1e-9) outc.Timeline.Add(new[] { Math.Round(t1), a.U.Soldiers, b.U.Soldiers });
            }

            outc.LossA = startA - a.U.Soldiers; outc.LossB = startB - b.U.Soldiers;
            outc.MoraleA = a.U.Morale; outc.MoraleB = b.U.Morale;
            outc.StatusA = a.U.Status; outc.StatusB = b.U.Status;
            return outc;
        }
    }
}
