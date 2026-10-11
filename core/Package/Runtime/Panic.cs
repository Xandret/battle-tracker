// ═══════════ Panic.cs — каскадная паника (копия panic.js; этап 6а, К11, К25) — ЧЕРНОВИК ДО ГМа ═══════════
// Отряд побежал → свои в радиусе бросают проверку на побег d100 ≤ дисциплина. Побежавший запускает
// проверку у своих соседей, даже не видевших первого. Каждый отряд — раз за волну. Гасителей нет.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class SightResult { public bool Ok = true; public string Why; }

    public sealed class PanicResult
    {
        public List<string> Lines = new List<string>();
        public List<UnitPatch> Patches = new List<UnitPatch>();
        public List<int> Fled = new List<int>();
    }

    public static class Panic
    {
        // Прямая видимость между центрами строя: холм выше обоих закрывает; лес пропускает взгляд
        // на Sight метров (100), стены, башни, здания и скалы — нисколько.
        public static SightResult LineOfSight(Unit a, Unit b, Geo geo, Rules r)
        {
            if (geo == null || geo.Map == null) return new SightResult();
            var (ax, ay) = BattleMap.UnitCenter(a, geo); var (bx, by) = BattleMap.UnitCenter(b, geo);
            double za = BattleMap.GroundUnder(a, geo, r)?.Z ?? 0, zb = BattleMap.GroundUnder(b, geo, r)?.Z ?? 0;
            return LineOfSight(ax, ay, za, bx, by, zb, geo, r);
        }
        // то же по точкам в метрах и уровням высоты под ними (Г107: бой в движении, положения отрядов живые)
        public static SightResult LineOfSight(double ax, double ay, double za, double bx, double by, double zb, Geo geo, Rules r)
        {
            if (geo == null || geo.Map == null) return new SightResult();
            double len = JsMath.Hypot(bx - ax, by - ay);
            int n = (int)Math.Max(2, Math.Ceiling(len / 5));
            double step = len / n;
            var through = new Dictionary<string, double>();
            for (int k = 1; k < n; k++)
            {
                var c = Terrain.CellAt(geo.Map, (ax + (bx - ax) * k / n) / geo.W, (ay + (by - ay) * k / n) / geo.H);
                if (c.Z > Math.Max(za, zb)) return new SightResult { Ok = false, Why = "холм" };
                if (c.T == 0) continue;
                var t = Terrain.ById[c.T];
                if (r.Map.Terrain.TryGetValue(t.Key, out var tr) && tr.Sight.HasValue)
                {
                    through[t.Key] = (through.TryGetValue(t.Key, out var was) ? was : 0) + step;
                    if (through[t.Key] > tr.Sight.Value) return new SightResult { Ok = false, Why = t.Name.ToLowerInvariant() };
                }
            }
            return new SightResult();
        }

        // Свои — та же фракция (подфракции внутри неё тоже свои). Союзов между фракциями пока нет.
        static bool SameSide(Unit a, Unit b) => a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId;

        // Волна паники от отряда sourceId. Отряды не меняются: возвращаются патчи и строки журнала.
        // moraleLoss — переключатель «−100 БД вместе с проверкой».
        public static PanicResult Wave(IList<Unit> units, int sourceId, Geo geo, EngineContext ctx, bool moraleLoss = false)
        {
            var R = ctx.Rules; var P = R.Map.Panic;
            var order = units.Select(u => u.Id).ToList();
            var state = units.ToDictionary(u => u.Id, u => u.Clone());
            var res = new PanicResult();
            var L = res.Lines;
            var patches = new Dictionary<int, Patch>(); var patchOrder = new List<int>();
            if (!state.TryGetValue(sourceId, out var src) || !src.OnMap) return res;
            var checkedIds = new HashSet<int> { sourceId };
            var queue = new List<Unit> { src };
            int ring = 0, total = 0;
            while (queue.Count > 0)
            {
                var next = new List<Unit>();
                foreach (var runner in queue)
                {
                    var near = order.Select(id => state[id])
                        .Where(v => v.Status == "active" && v.OnMap && !checkedIds.Contains(v.Id) && SameSide(v, runner))
                        .Select(v => (v, gap: BattleMap.UnitGap(runner, v, geo, R)))
                        .Where(x => x.gap <= P.Radius)
                        .OrderBy(x => x.gap).ThenBy(x => x.v.Id)
                        .ToList();
                    foreach (var (v, gap) in near)
                    {
                        string dist = $"{Js.Num(Js.Round(gap))} м от «{runner.Name}»";
                        // первое кольцо — только те, кто видел бегство; дальше волна идёт без видимости
                        if (ring == 0)
                        {
                            var los = LineOfSight(runner, v, geo, R);
                            if (!los.Ok) { L.Add($"«{v.Name}» ({dist}) не видел бегства — мешает {los.Why}"); continue; }
                        }
                        checkedIds.Add(v.Id); total++;
                        var patch = new Patch();
                        var extra = new List<string>();
                        if (moraleLoss)
                        {
                            patch.Merge(MoraleRules.ApplyMoraleChange(v, v.Morale - P.MoraleLoss, extra, R));
                            extra.Insert(0, $"БД «{v.Name}» −{Js.Num(P.MoraleLoss)}: {Js.Num(v.Morale)} → {Js.Num(patch.Morale.Value)}");
                        }
                        double roll = Dice.Roll(ctx.Rng, 100);
                        string wave = ring != 0 ? ", волна " + (ring + 1) : "";
                        if (roll <= v.Discipline)
                            L.Add($"«{v.Name}» ({dist}{wave}): d100 = {Js.Num(roll)} ≤ {Js.Num(v.Discipline)} — держится");
                        else
                        {
                            L.Add($"✘ «{v.Name}» ({dist}{wave}): d100 = {Js.Num(roll)} > {Js.Num(v.Discipline)} — бежит!");
                            patch.Status = "fled";
                            patch.FleeChecks = v.FleeChecks + 1;
                            res.Fled.Add(v.Id);
                        }
                        foreach (var l in extra) L.Add("  " + l);
                        patch.ApplyTo(v);
                        if (!patch.IsEmpty)
                        {
                            if (!patches.ContainsKey(v.Id)) { patches[v.Id] = new Patch(); patchOrder.Add(v.Id); }
                            patches[v.Id].Merge(patch);
                        }
                        if (patch.Status == "fled") next.Add(v);
                    }
                }
                queue = next; ring++;
            }
            if (total > 0) L.Add($"Итог волны: бежали {res.Fled.Count} из {total} проверенных");
            foreach (var id in patchOrder) res.Patches.Add(new UnitPatch(id, patches[id]));
            return res;
        }
    }
}
