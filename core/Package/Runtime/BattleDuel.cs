// ═══════════ BattleDuel.cs — поединки командиров (Г108) ═══════════
// Вызов по кнопке (Challenge) висит до «Ход!», вызванный отвечает в фазу приказов (Answer); отказ — своему войску −БД.
// Принят — с первого шага хода оба отряда стоят и друг друга не атакуют, командиры выходят на середину между отрядами,
// бойцы держат круг (препятствие MenBodies.Obstacles). Раунды: d20 + доблесть у каждого, кто выше — ранит; три раны —
// проигравший ранен или убит; победителю и его стороне +БД, проигравшему и его стороне −БД, отряду проигравшего — проверка
// на побег. Duels — идущие и законченные, с положениями командиров и ударами — для рисунка.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Duel
    {
        public Mover A, B; public Commander CA, CB;          // A вызвал
        public double X, Y, R;                               // круг
        public double AX, AY, AFacing, BX, BY, BFacing;      // где командиры: идут к кругу, потом стоят друг против друга
        public double T0, StartT = double.NaN, EndT = double.NaN, NextRound = double.NaN;
        public int WoundsA, WoundsB;
        public bool Pending = true, Fighting, Over;
        public Mover Winner, Loser; public bool LoserKilled;
        public List<(double t, int unitId, bool hit)> Strikes = new List<(double, int, bool)>();   // удары: кто (отряд командира) и попал ли
        public Man ManA, ManB;                               // тела полководцев (null — старый режим фигурок: точки AX, AY / BX, BY)
        public bool Has(Mover m) => m == A || m == B;
    }

    public sealed partial class Battle
    {
        public List<Duel> Duels = new List<Duel>();
        public List<(Mover from, Mover to, double t)> Challenges = new List<(Mover, Mover, double)>();   // висящие вызовы

        Commander CmdrOf(Mover m) => Ctx.CommanderOf?.Invoke(m.P.U);
        bool InDuel(Mover m) => Duels.Any(d => !d.Over && d.Has(m));
        bool InDuel(Mover x, Mover y) => Duels.Any(d => !d.Over && d.Has(x) && d.Has(y));

        // почему нельзя вызвать (для подсказки), null — можно
        public string ChallengeWhy(Mover a, Mover b)
        {
            if (a == b || !Enemies(a.P.U, b.P.U)) return "это свои";
            if (!Alive(a) || !Alive(b)) return "отряда на поле нет";
            var ca = CmdrOf(a); var cb = CmdrOf(b);
            if (ca == null || ca.Dead) return "у своего отряда нет командира";
            if (cb == null || cb.Dead) return "у врага нет командира";
            if (a.Fleeing) return "свой отряд бежит";
            if (b.Fleeing) return "враг бежит";
            if (InDuel(a) || Challenges.Any(c => c.from == a || c.to == a)) return "свой командир уже в поединке или вызван";
            if (InDuel(b) || Challenges.Any(c => c.from == b || c.to == b)) return "враг уже в поединке или вызван";
            if (JsMath.Hypot(a.P.X - b.P.X, a.P.Y - b.P.Y) > R.Duel.RangeM) return $"дальше {R.Duel.RangeM:0} м";
            return null;
        }
        public string Challenge(Mover a, Mover b)
        {
            var why = ChallengeWhy(a, b); if (why != null) return why;
            Challenges.Add((a, b, Clock));
            events.Add($"«{CmdrOf(a).Name}» («{a.P.U.Name}») вызывает на поединок «{CmdrOf(b).Name}» («{b.P.U.Name}»)");
            return null;
        }
        // ответ вызванного: принял — поединок с первого шага хода; отказался — его стороне −RefuseMorale БД. false — вызова нет
        public bool Answer(Mover b, bool accept)
        {
            int i = Challenges.FindIndex(c => c.to == b); if (i < 0) return false;
            var (a, _, _) = Challenges[i]; Challenges.RemoveAt(i);
            if (!accept)
            {
                foreach (var m in Movers) if (OnField(m) && !Enemies(m.P.U, b.P.U)) m.P.U.Morale = Math.Max(0, m.P.U.Morale - R.Duel.RefuseMorale);
                events.Add($"«{CmdrOf(b).Name}» отказался от поединка — войско его стороны: БД −{Js.Num(R.Duel.RefuseMorale)}");
                return true;
            }
            var d = new Duel { A = a, B = b, CA = CmdrOf(a), CB = CmdrOf(b), T0 = Clock, R = R.Duel.CircleR };
            d.AX = a.P.X; d.AY = a.P.Y; d.BX = b.P.X; d.BY = b.P.Y;
            Duels.Add(d);
            foreach (var m in new[] { a, b }) { m.Order = new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = m.P.Facing }; m.Track = null; m.Done = true; m.Vs = 0; aimed.Remove(m); }
            events.Add($"«{d.CB.Name}» принял вызов «{d.CA.Name}» — поединок между «{a.P.U.Name}» и «{b.P.U.Name}»");
            return true;
        }

        // Г108: полководец — тело отряда, ближайшее к знамени (центр строя, на 1/5 глубины к фронту); нет командира (или он убит) — null.
        // Пал — место занимает ближайший живой (в Relayout). Стража — GuardN ближайших к нему
        public Man PickCommander(Mover m)
        {
            var c = CmdrOf(m); if (c == null || c.Dead || m.Men.Count == 0) { m.CommanderMan = null; return null; }
            if (m.CommanderMan != null && m.CommanderMan.Alive && m.Men.Contains(m.CommanderMan)) return m.CommanderMan;
            m.P.ToWorld(0, -m.P.Fp.Depth / 5, out var bx, out var by);
            m.CommanderMan = m.Men.Where(x => x.Alive).OrderBy(x => (x.X - bx) * (x.X - bx) + (x.Y - by) * (x.Y - by)).FirstOrDefault();
            return m.CommanderMan;
        }
        static void Post(Man x, double px, double py, double face) { x.PostX = px; x.PostY = py; x.PostFacing = face; }
        static void Release(Man x) { x.PostX = double.NaN; x.PostFacing = double.NaN; x.InDuel = false; x.Reseat = true; }

        // шаг поединков: выход командиров и стражи к кругу, раунды, исход; круги — в препятствия бойцов
        void DuelStep(double t, double dt)
        {
            MenBodies.Obstacles.Clear();
            foreach (var d in Duels)
            {
                if (d.Over) continue;
                var a = d.A; var b = d.B;
                if (!OnField(a) || !OnField(b) || a.Fleeing || b.Fleeing) { Finish(d, null, t); events.Add($"{At(t)} с · поединок «{d.CA.Name}» и «{d.CB.Name}» сорван — отряд бежит"); continue; }
                if (d.Pending)
                {
                    d.Pending = false; d.StartT = t;
                    d.X = (a.P.X + b.P.X) / 2; d.Y = (a.P.Y + b.P.Y) / 2;
                    d.AX = a.P.X; d.AY = a.P.Y; d.BX = b.P.X; d.BY = b.P.Y;
                    // полководцы и стража — телами (Алекс: «стража обоих полководцев образует кольцо»)
                    d.ManA = PickCommander(a); d.ManB = PickCommander(b);
                    foreach (var (m, cm) in new[] { (a, d.ManA), (b, d.ManB) })
                    {
                        if (cm == null) continue;
                        cm.InDuel = true;
                        m.Guard = m.Men.Where(x => x.Alive && x != cm).OrderBy(x => (x.X - cm.X) * (x.X - cm.X) + (x.Y - cm.Y) * (x.Y - cm.Y)).Take(R.Duel.GuardN).ToList();
                    }
                }
                MenBodies.Obstacles.Add((d.X, d.Y, d.R));
                double ux = b.P.X - a.P.X, uy = b.P.Y - a.P.Y, ul = Math.Max(1e-9, JsMath.Hypot(ux, uy)); ux /= ul; uy /= ul;
                double ax = d.X - ux * 2, ay = d.Y - uy * 2, bx = d.X + ux * 2, by = d.Y + uy * 2;
                bool arrivedA, arrivedB;
                if (d.ManA != null && d.ManB != null)
                {
                    // тела идут на посты сами (MenBodies): полководцы в круг друг против друга, стража — по кольцу на своей половине, лицом внутрь
                    Post(d.ManA, ax, ay, MoveSim.HeadingOf(bx - ax, by - ay)); Post(d.ManB, bx, by, MoveSim.HeadingOf(ax - bx, ay - by));
                    double baseA = Math.Atan2(-uy, -ux), baseB = Math.Atan2(uy, ux), rr = d.R + 0.6;
                    for (int side = 0; side < 2; side++)
                    {
                        var g = side == 0 ? a.Guard : b.Guard; double ba = side == 0 ? baseA : baseB;
                        for (int k = 0; k < g.Count; k++)
                        {
                            double ang = ba + (k - (g.Count - 1) / 2.0) * Math.PI / Math.Max(1, g.Count);
                            double px = d.X + rr * Math.Cos(ang), py = d.Y + rr * Math.Sin(ang);
                            Post(g[k], px, py, MoveSim.HeadingOf(d.X - px, d.Y - py));
                        }
                    }
                    d.AX = d.ManA.X; d.AY = d.ManA.Y; d.AFacing = d.ManA.Facing; d.BX = d.ManB.X; d.BY = d.ManB.Y; d.BFacing = d.ManB.Facing;
                    arrivedA = JsMath.Hypot(d.ManA.X - ax, d.ManA.Y - ay) < 1.0; arrivedB = JsMath.Hypot(d.ManB.X - bx, d.ManB.Y - by) < 1.0;
                    if (!d.ManA.Alive || !d.ManB.Alive) { Finish(d, !d.ManA.Alive ? b : a, t); continue; }   // полководец пал от стрелы — поединок за другим
                }
                else
                {
                    // без тел (старый режим фигурок): командиры — точки, идут к кругу со скоростью WalkMps
                    arrivedA = Walk(ref d.AX, ref d.AY, ax, ay, R.Duel.WalkMps * dt);
                    arrivedB = Walk(ref d.BX, ref d.BY, bx, by, R.Duel.WalkMps * dt);
                    d.AFacing = MoveSim.HeadingOf(d.BX - d.AX, d.BY - d.AY); d.BFacing = MoveSim.HeadingOf(d.AX - d.BX, d.AY - d.BY);
                }
                if (!(arrivedA && arrivedB)) continue;
                if (!d.Fighting) { d.Fighting = true; d.NextRound = t + R.Duel.RoundSec; continue; }
                if (t + dt <= d.NextRound) continue;
                double at = d.NextRound; d.NextRound += R.Duel.RoundSec;
                double ra = Dice.Roll(Ctx.Rng, R.Duel.Die) + d.CA.Valor, rb = Dice.Roll(Ctx.Rng, R.Duel.Die) + d.CB.Valor;
                if (ra > rb) { d.WoundsB++; d.Strikes.Add((at, a.P.U.Id, true)); d.Strikes.Add((at, b.P.U.Id, false)); }
                else if (rb > ra) { d.WoundsA++; d.Strikes.Add((at, b.P.U.Id, true)); d.Strikes.Add((at, a.P.U.Id, false)); }
                else { d.Strikes.Add((at, a.P.U.Id, false)); d.Strikes.Add((at, b.P.U.Id, false)); }
                if (d.WoundsA < R.Duel.WoundsToLose && d.WoundsB < R.Duel.WoundsToLose) continue;
                Finish(d, d.WoundsA >= R.Duel.WoundsToLose ? b : a, at);
            }
        }
        static bool Walk(ref double x, ref double y, double tx, double ty, double step)
        {
            double dx = tx - x, dy = ty - y, dl = JsMath.Hypot(dx, dy);
            if (dl <= step) { x = tx; y = ty; return true; }
            x += dx / dl * step; y += dy / dl * step; return false;
        }
        void Finish(Duel d, Mover winner, double t)
        {
            // тела — обратно в строй
            foreach (var m in new[] { d.A, d.B }) { foreach (var g in m.Guard) Release(g); m.Guard.Clear(); }
            if (d.ManA != null) Release(d.ManA); if (d.ManB != null) Release(d.ManB);
            d.Over = true; d.Fighting = false; d.EndT = t;
            if (winner == null) return;   // сорван
            var loser = winner == d.A ? d.B : d.A; var cw = winner == d.A ? d.CA : d.CB; var cl = winner == d.A ? d.CB : d.CA;
            d.Winner = winner; d.Loser = loser;
            d.LoserKilled = Dice.Roll(Ctx.Rng, 100) <= R.Duel.KillPct;
            if (d.LoserKilled)
            {
                cl.Dead = true; loser.P.U.CommanderId = null;
                ApplyMorale(new[] { loser }, "commanderDead", t);   // Г112 п.4: гибель полководца — единственный модификатор таблицы, что ставится сам
                var lm = loser == d.A ? d.ManA : d.ManB; var wm = winner == d.A ? d.ManA : d.ManB;
                if (lm != null && lm.Alive) { double ux = wm != null ? lm.X - wm.X : 0, uy = wm != null ? lm.Y - wm.Y : 1, ul = Math.Max(1e-9, JsMath.Hypot(ux, uy)); Fell(loser, lm, t, ux / ul, uy / ul); loser.CommanderMan = null; }
            }
            else cl.Wounded = true;
            var DR = R.Duel;
            foreach (var m in Movers)
            {
                if (!OnField(m)) continue;
                var u = m.P.U;
                if (m == winner) u.Morale = Math.Min(R.Morale.Max, u.Morale + DR.WinMorale);
                else if (m == loser) { if (!d.LoserKilled) u.Morale = Math.Max(0, u.Morale - DR.LoseMorale); }   // убит — таблица «гибель полководца» (Г112 п.4), а не −LoseMorale
                else if (!Enemies(u, winner.P.U)) u.Morale = Math.Min(R.Morale.Max, u.Morale + DR.WinSideMorale);
                else if (!Enemies(u, loser.P.U)) u.Morale = Math.Max(0, u.Morale - DR.LoseSideMorale);
            }
            events.Add($"{At(t)} с · ⚔ поединок: «{cw.Name}» ({cw.Valor:0}) одолел «{cl.Name}» ({cl.Valor:0}) — {(d.LoserKilled ? "убит" : "ранен")}; «{winner.P.U.Name}» БД +{Js.Num(DR.WinMorale)}, сторона +{Js.Num(DR.WinSideMorale)}; «{loser.P.U.Name}» БД {(d.LoserKilled ? "по таблице (гибель полководца)" : "−" + Js.Num(DR.LoseMorale))}, сторона −{Js.Num(DR.LoseSideMorale)}");
            // отряд проигравшего дрогнул — проверка на побег сразу
            var lu = loser.P.U;
            Check(t, MoraleRules.FleeCheck(lu, Ctx), lu);
            if (lu.Status == "fled") { Flee(loser, t, "проиграл поединок"); PanicFrom(loser, t); }
        }
    }
}
