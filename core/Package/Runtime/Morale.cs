// ═══════════ Morale.cs — боевой дух, слом, проверки (копия morale.js) ═══════════
using System.Collections.Generic;

namespace BattleCore
{
    public static class MoraleRules
    {
        // Патч отряда после изменения БД; пояснения — в lines
        public static Patch ApplyMoraleChange(Unit u, double newMorale, List<string> lines, Rules R)
        {
            var patch = new Patch { Morale = Js.Clamp(newMorale, 0, R.Morale.Max) };
            if (patch.Morale == 0 && !u.Broken)
            {
                patch.Broken = true;
                patch.BreakGrace = Units.GraceByDisc(u.Discipline, R);
                patch.BreakPenalty = R.Breakdown.DelayTurns;
                lines.Add($"⚠ БД «{u.Name}» упал до нуля — дисциплина −{Js.Num(R.Breakdown.DiscPenalty)} вступит в силу через {Js.Num(R.Breakdown.DelayTurns)} хода");
                if (patch.BreakGrace > 0) lines.Add($"Выучка держит строй: пассивная потеря дисциплины отложена на {Js.Num(patch.BreakGrace.Value)} х.");
                lines.Add($"«{u.Name}»: требуется проверка на побег!");
            }
            if (patch.Morale > 0 && u.Broken)
            {
                patch.Broken = false; patch.BreakGrace = 0;
                if (u.BreakPenalty > 0)
                {
                    patch.BreakPenalty = 0;
                    lines.Add($"Отложенный штраф дисциплины «{u.Name}» отменён — отряд взяли в руки вовремя");
                }
                lines.Add($"БД «{u.Name}» восстановлен — юнит вновь в руках командира");
            }
            return patch;
        }

        // Проверка БД: d100 ≤ БД × 2; «колеблются» — с помехой (худший из двух)
        public static ActionResult MoraleCheck(Unit u, EngineContext ctx)
        {
            var R = ctx.Rules;
            var L = new List<string>();
            bool disadv = u.Morale >= R.Morale.WaverFrom && u.Morale < R.Morale.WaverTo;
            double roll = Dice.Roll(ctx.Rng, 100);
            if (disadv)
            {
                double r2v = Dice.Roll(ctx.Rng, 100);
                L.Add($"Помеха («колеблются»): броски {Js.Num(roll)} и {Js.Num(r2v)}, берём худший");
                roll = System.Math.Max(roll, r2v);
            }
            double target = u.Morale * R.Morale.CheckMult;
            L.Add($"d100: {Js.Num(roll)} против {Js.Num(target)} (БД × {Js.Num(R.Morale.CheckMult)})");
            if (roll <= target)
            {
                L.Add($"✔ Успех — строй держится, БД остаётся {Js.Num(u.Morale)}");
                return new ActionResult { Title = $"Проверка БД: {u.Name}", Lines = L, Tone = "info", Patch = new Patch() };
            }
            L.Add("✘ Провал — БД падает до нуля");
            var patch = ApplyMoraleChange(u, 0, L, R);
            return new ActionResult { Title = $"Проверка БД: {u.Name}", Lines = L, Tone = "danger", Patch = patch };
        }

        // Проверка на побег: d100 ≤ дисциплина; «дрогнули» — с помехой.
        // Дисциплина ≥ 60: первый бросок игнорируется («стоять насмерть»).
        public static ActionResult FleeCheck(Unit u, EngineContext ctx)
        {
            var R = ctx.Rules;
            var L = new List<string>();
            string title = $"Проверка на побег: {u.Name}";
            if (u.Discipline >= R.Flee.StandFastDisc && u.FleeChecks == 0)
            {
                L.Add($"«Стоять насмерть» (дисц ≥ {Js.Num(R.Flee.StandFastDisc)}): первый бросок на побег игнорируется");
                return new ActionResult { Title = title, Lines = L, Tone = "info", Patch = new Patch { FleeChecks = 1 } };
            }
            bool disadv = u.Morale < R.Morale.ShakenBelow;
            double roll = Dice.Roll(ctx.Rng, 100);
            if (disadv)
            {
                double r2v = Dice.Roll(ctx.Rng, 100);
                L.Add($"Помеха («дрогнули»): броски {Js.Num(roll)} и {Js.Num(r2v)}, берём худший");
                roll = System.Math.Max(roll, r2v);
            }
            L.Add($"d100: {Js.Num(roll)} против {Js.Num(u.Discipline)} (дисциплина)");
            if (roll <= u.Discipline)
            {
                L.Add("✔ Строй держится на одной муштре");
                return new ActionResult { Title = title, Lines = L, Tone = "info", Patch = new Patch { FleeChecks = u.FleeChecks + 1 } };
            }
            L.Add("✘ Юнит обращён в бегство!");
            return new ActionResult { Title = title, Lines = L, Tone = "danger", Patch = new Patch { Status = "fled", FleeChecks = u.FleeChecks + 1 } };
        }
    }
}
