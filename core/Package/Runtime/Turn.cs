// ═══════════ Turn.cs — конец хода (копия turn.js) ═══════════
// Принимает отряды, возвращает новые копии и строки журнала. Исходные отряды не меняются.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class EndTurnResult
    {
        public List<Unit> Units = new List<Unit>();
        public List<string> Lines = new List<string>();
    }

    public static class Turn
    {
        public static EndTurnResult EndTurn(IEnumerable<Unit> units, EngineContext ctx)
        {
            var R = ctx.Rules; var F = R.Fatigue; var B = R.Breakdown;
            var res = new EndTurnResult();
            var L = res.Lines;
            foreach (var u in units)
            {
                var p = u.Clone();
                p.AttacksMade = 0; p.CountersMade = 0;
                res.Units.Add(p);
                if (u.Status != "active") continue;
                if (p.Acted)
                {
                    p.TurnsActive = p.TurnsActive + 1;
                    double threshold = p.Discipline >= F.EliteDisc ? F.EliteThreshold : F.Threshold;
                    if (p.TurnsActive > threshold && p.Fatigue < F.Max)
                    {
                        // ctx.FatigueMult — черновик карты (6а): песок, снег; не передан — шаг как в v29
                        var fm = ctx.FatigueMult?.Invoke(u);
                        double step = fm != null ? Js.Round(F.Step * fm.Mult) : F.Step;
                        p.Fatigue = Math.Min(F.Max, p.Fatigue + step);
                        L.Add($"{u.Name}: усталость +{Js.Num(step)} → {Js.Num(p.Fatigue)}{(fm != null ? $" ({fm.Name.ToLowerInvariant()} ×{Js.Num(fm.Mult)} · черновик)" : "")}");
                    }
                    p.Acted = false;
                }
                if (p.Broken && p.Morale == 0 && p.BreakPenalty > 0)
                {
                    p.BreakPenalty -= 1;
                    if (p.BreakPenalty == 0)
                    {
                        double was = p.Discipline;
                        p.Discipline = Math.Max(1, p.Discipline - B.DiscPenalty);
                        L.Add($"{u.Name}: отложенный штраф за слом БД — дисциплина −{Js.Num(B.DiscPenalty)} ({Js.Num(was)} → {Js.Num(p.Discipline)})");
                    }
                    else
                    {
                        L.Add($"{u.Name}: штраф дисциплины за слом БД вступит в силу через {Js.Num(p.BreakPenalty)} х.");
                    }
                }
                if (p.Broken && p.Morale == 0)
                {
                    if (p.BreakGrace > 0)
                    {
                        p.BreakGrace -= 1;
                        L.Add($"{u.Name}: выучка держит строй (осталось {Js.Num(p.BreakGrace)} х.)");
                    }
                    else
                    {
                        p.Discipline = Math.Max(0, p.Discipline - B.PassiveDiscLoss);
                        L.Add($"{u.Name}: БД на нуле — дисциплина −{Js.Num(B.PassiveDiscLoss)} → {Js.Num(p.Discipline)}");
                        if (p.Discipline <= 0)
                        {
                            p.Status = "fled";
                            L.Add($"{u.Name}: дисциплина иссякла — побег без броска!");
                        }
                    }
                }
            }
            return res;
        }
    }
}
