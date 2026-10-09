// ═══════════ LookTests.cs — данные для рисунка (В6): полёты стрел пишутся, а бой от этого не меняется ═══════════
using BattleCore;

static class LookTests
{
    static Rules R => Rules.Base;
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    public static IEnumerable<(string, Action)> All()
    {
        yield return ("рисунок (В6): запись полёта стрел не меняет бой; у долетевшей стрелы — конец полёта", () =>
        {
            (List<Death> deaths, double left, long arrows, List<ArrowTrace> log) Run(bool record)
            {
                var bt = new Battle(MoveTests.Open(2000, 1400), R, new EngineContext { Rng = new Mulberry32(41).Next });
                var TB = Templates.Get("infantry"); var TA = Templates.Get("archers");
                var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 1000, 800, 0);
                var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), R);
                var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 1000, 800 - (b.P.Fp.Depth / 2 + 100 + fa.Depth / 2), 180);
                if (record) bt.ArrowLog = new List<ArrowTrace>();
                bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
                long arrows = 0;
                for (int t = 0; t < 2; t++) { bt.Turn(); arrows += bt.Shots.Arrows; }
                return (bt.Deaths.ToList(), b.P.U.Soldiers, arrows, bt.ArrowLog);
            }
            var plain = Run(false); var rec = Run(true);
            True(plain.log == null, "без просьбы журнал стрел не ведётся");
            True(plain.left == rec.left && plain.arrows == rec.arrows && plain.deaths.Count == rec.deaths.Count,
                $"запись изменила бой: в строю {plain.left} → {rec.left}, стрел {plain.arrows} → {rec.arrows}, павших {plain.deaths.Count} → {rec.deaths.Count}");
            for (int i = 0; i < plain.deaths.Count; i++)
                True(plain.deaths[i].X == rec.deaths[i].X && plain.deaths[i].Y == rec.deaths[i].Y && plain.deaths[i].T == rec.deaths[i].T, $"павший {i} не там");
            True(rec.arrows > 100 && rec.log.Count == rec.arrows, $"в журнале {rec.log.Count} стрел из {rec.arrows}");
            var done = rec.log.Where(x => x.T1 > x.T0).ToList();
            True(done.Count >= rec.log.Count * 0.9, $"долетели {done.Count} из {rec.log.Count}");
            True(done.Count(x => x.End == 1) == rec.deaths.Count, $"сваливших стрел {done.Count(x => x.End == 1)}, павших {rec.deaths.Count}");
            foreach (var x in done)
            {
                double d = Math.Sqrt((x.X1 - x.X0) * (x.X1 - x.X0) + (x.Y1 - x.Y0) * (x.Y1 - x.Y0)), v = Math.Sqrt(x.VX * x.VX + x.VY * x.VY);
                True(x.End <= 5 && x.Z1 > -0.5 && x.Z1 < 3, $"конец полёта: End {x.End}, высота {x.Z1:0.0}");
                True(d <= v * (x.T1 - x.T0) + 0.5, $"стрела ушла дальше, чем могла: {d:0} м за {x.T1 - x.T0:0.00} с при {v:0} м/с");
            }
        });
    }
}
