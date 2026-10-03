// Живые бойцы внутри фигурок (Г75–Г78): по бойцу на человека, место с запаздыванием, толкотня со всеми,
// выпады передних в схватке, павшие — те самые бойцы, задние выходят вперёд
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

static class MenTests
{
    public static Rules Use = Rules.Base;
    static Rules R => Use;
    // Г92: умолчание — бойцы-тела. Тесты именно старого режима (фигурки-капсулы, живые бойцы внутри них) — в обёртке Figs,
    // пока его код не убран (Г97)
    static Action Figs(Action run) => () => { var was = Use; Use = Rules.Figures; try { run(); } finally { Use = was; } };
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    // как далеко бойцы от своих мест (без выпадов): наибольшее и среднее
    static (double max, double avg) Lag(Mover m)
    {
        double mx = 0, sum = 0; int n = 0;
        foreach (var man in m.Men)
        {
            if (!man.Alive) continue;
            var (hx, hy) = Soldiers.HomeOf(m, man);
            double d = JsMath.Hypot(man.X - hx, man.Y - hy);
            mx = Math.Max(mx, d); sum += d; n++;
        }
        return (mx, sum / Math.Max(1, n));
    }
    static (Battle bt, Mover a, Mover b) Duel(string ta, string tb, double gap, uint seed)
    {
        var bt = new Battle(MoveTests.Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(seed).Next });
        var TA = Templates.Get(ta); var TB = Templates.Get(tb);
        var b = bt.Add(TB.Make(2, "B", 1000, 2), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, "A", 1000, 1), R);
        var a = bt.Add(TA.Make(1, "A", 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + gap + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        return (bt, a, b);
    }

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("бойцы (Г75): по бойцу на человека, у каждого своё место; стоящий отряд — каждый на месте", Figs(() =>
        {
            var geo = MoveTests.Open(600, 600);
            var m = MoveTests.Unit("infantry", 1, 300, 300, 0);
            True(m.Men.Count == 1000 && m.Men.Select(x => x.Id).Distinct().Count() == 1000, $"бойцов {m.Men.Count}");
            True(m.Men.All(x => m.Figs.Contains(x.Fig)), "каждый — в своей фигурке");
            MoveSim.Turn(new[] { m }, geo, R);
            var (mx, _) = Lag(m);
            True(mx < 0.05, $"стоят дальше {mx:0.00} м от мест");
        }));

        yield return ("бойцы (Г76): на марше с поворотом отстают, но не дальше 2,5 м; встали — снова на местах", Figs(() =>
        {
            var geo = MoveTests.Open(800, 800);
            var m = MoveTests.Unit("infantry", 1, 200, 600, 0);
            MoveSim.Give(m, new MoveOrder { X = 280, Y = 560, Facing = 90 }, geo, R);
            double worst = 0, moving = 0;
            MoveSim.Turn(new[] { m }, geo, R, t => { var l = Lag(m); worst = Math.Max(worst, l.max); if (!m.Done) moving = Math.Max(moving, l.avg); });
            True(worst <= R.Men.MaxLagM + 0.6, $"отстал на {worst:0.0} м");
            True(moving > 0.05, $"на ходу никто не отстаёт ({moving:0.000} м) — строй неживой");
            MoveSim.Turn(new[] { m }, geo, R);
            var (mx, _) = Lag(m);
            True(m.Done && mx < 0.3, $"встали: дальше всех {mx:0.00} м");
        }));

        yield return ("толкотня (Г77): бойцы друг на друга не налезают — ни свои, ни враги в схватке", Figs(() =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 3);
            bt.Turn();
            var all = a.Men.Where(x => x.Alive).Select(x => (x, a)).Concat(b.Men.Where(x => x.Alive).Select(x => (x, b))).ToList();
            double rad = R.Men.BodyShare * 1;   // пехота: шаг 1 × 1 м
            int close = 0;
            var cells = all.GroupBy(p => ((int)Math.Floor(p.x.X / 2), (int)Math.Floor(p.x.Y / 2))).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var kv in cells)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (cells.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var other))
                            foreach (var p in kv.Value) foreach (var q in other)
                                if (p.x.Id != q.x.Id || p.Item2 != q.Item2)
                                    if (JsMath.Hypot(p.x.X - q.x.X, p.x.Y - q.x.Y) < rad) close++;
            True(close == 0, $"пар бойцов ближе {rad:0.00} м: {close / 2}");
        }));

        yield return ("схватка (Г78): передние касающихся фигурок выходят к врагу; падают — те, кто у врага", Figs(() =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 4);
            double forward = 0; int n = 0, seen = 0, near = 0, deaths = 0;
            bt.Turn(t =>
            {
                // павший — у врага: в момент гибели ближе 4 м к его бойцу (охват бьёт и во фланг, и в тыл)
                for (; seen < bt.Deaths.Count; seen++)
                {
                    var d = bt.Deaths[seen];
                    if (d.UnitId != 2) continue;
                    deaths++;
                    if (a.Men.Any(x => x.Alive && JsMath.Hypot(x.X - d.X, x.Y - d.Y) < 4)) near++;
                }
                if (t < 5) return;
                foreach (var man in b.Men)
                {
                    if (!man.Alive || man.Row != 0 || !man.Fig.Fighting) continue;
                    var (hx, hy) = Soldiers.HomeOf(b, man);
                    forward += (man.X - hx) * man.Fig.FightX + (man.Y - hy) * man.Fig.FightY; n++;
                }
            });
            True(n > 0 && forward / n > 0.08, $"передние в среднем на {forward / Math.Max(1, n):0.00} м к врагу");   // враги тоже делают выпад — передние упираются друг в друга
            var ds = bt.Deaths.Where(d => d.UnitId == 2).ToList();
            True(ds.Count > 0 && ds.All(d => d.ManId > 0), "у каждого павшего — номер бойца");
            True(near >= 0.9 * deaths, $"у врага пало {near} из {deaths}");
            True(ds.All(d => !b.Men.Any(x => x.Id == d.ManId && x.Alive)), "павший снова в строю");
        }));

        yield return ("стрелы (Г75): падает тот самый боец, в которого попали, — там, где стоял", () =>
        {
            var bt = new Battle(MoveTests.Open(2000, 1400), R, new EngineContext { Rng = new Mulberry32(5).Next });
            var b = bt.Add(Templates.Get("infantry").Make(2, "B", 1000, 2), 1000, 800, 0);
            var a = bt.Add(Templates.Get("archers").Make(1, "A", 1000, 1), 1000, 800 - (4 + 100 + 2.5), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            var last = new Dictionary<int, (double x, double y)>();
            int seen = 0, far = 0;
            bt.Turn(t =>
            {
                for (; seen < bt.Deaths.Count; seen++)
                {
                    var d = bt.Deaths[seen];
                    if (d.UnitId != 2) continue;
                    if (!last.TryGetValue(d.ManId, out var p) || JsMath.Hypot(p.x - d.X, p.y - d.Y) > 1) far++;
                }
                foreach (var man in b.Men) if (man.Alive) last[man.Id] = (man.X, man.Y);
            });
            var ds = bt.Deaths.Where(d => d.UnitId == 2).ToList();
            True(ds.Count > 20, $"павших от стрел {ds.Count}");
            True(ds.Select(d => d.ManId).Distinct().Count() == ds.Count, "один боец пал дважды");
            True(far == 0, $"павших не там, где стоял боец: {far} из {ds.Count}");
        });

        yield return ("потери (Г30, Г75): задние выходят вперёд — передний ряд передних фигурок полон", () =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 6);
            bt.Turn();
            var f = R.Map.Formation["infantry"];
            int holes = 0;
            for (int k = 0; k < b.Figs.Count; k++)
            {
                var fg = b.P.Figs[k];
                if (fg.Rank != 0) continue;
                int cols = (int)Math.Round(fg.Width / f.PerMan);
                int need = Math.Min(cols, (int)Math.Round(fg.Men));
                int row0 = b.Men.Count(x => x.Alive && x.Fig == b.Figs[k] && x.Row == 0);
                if (row0 < need) holes += need - row0;
            }
            True(b.P.U.Soldiers < 1000 && holes == 0, $"дыр в переднем ряду {holes} (потерь {1000 - b.P.U.Soldiers:0})");
            int alive = b.Men.Count(x => x.Alive);
            True(Math.Abs(alive - Math.Round(b.P.U.Soldiers)) <= 5, $"бойцов {alive}, в строю {b.P.U.Soldiers:0}");
        });

        yield return ("раненые (Г39, В13): в рукопашной павшие — и убитые, и раненые, доля убитых как в итогах удара стола", () =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 5);
            bt.Turn(); bt.Turn();
            var melee = bt.Deaths.Where(d => d.UnitId == 2).ToList();
            int killed = melee.Count(d => d.Killed), wounded = melee.Count - killed;
            double table = b.P.U.TotKilled / Math.Max(1e-9, b.P.U.TotKilled + b.P.U.TotWounded), seen = killed / (double)Math.Max(1, melee.Count);
            True(melee.Count >= 30 && killed > 0 && wounded > 0, $"павших {melee.Count}: убитых {killed}, раненых {wounded}");
            True(Math.Abs(seen - table) < 0.15, $"доля убитых {seen:0.00}, у стола {table:0.00}");
        });

        yield return ("замена павших (В14): на место павшего шагает стоящий за ним, остальные почти не двигаются, из других фигурок не идут", Figs(() =>
        {
            var bt = new Battle(MoveTests.Open(600, 600), R, new EngineContext { Rng = new Mulberry32(1).Next });
            var m = bt.Add(Templates.Get("infantry").Make(1, "A", 1000, 1), 300, 300, 0);
            var front = m.Men.Where(x => x.Row == 0).OrderBy(x => Math.Abs(x.X - 300) + Math.Abs(x.Y - 300)).First();   // в середине передней шеренги
            var before = m.Men.ToDictionary(x => x, x => (Soldiers.HomeOf(m, x), x.Fig));
            front.Alive = false; m.P.U.Soldiers -= 1;
            bt.Relayout(m);
            int far = 0, moved = 0, other = 0;
            foreach (var x in m.Men.Where(x => x.Alive))
            {
                var (h0, f0) = before[x]; var h1 = Soldiers.HomeOf(m, x);
                double d = JsMath.Hypot(h1.x - h0.x, h1.y - h0.y);
                if (d > 1.6) far++;
                if (d > 0.6) moved++;   // больше полушага: вышедший вперёд и пересевшие в крайней фигурке, что сузилась (Г30)
                if (x.Fig != f0) other++;
            }
            var info = string.Join("; ", m.Men.Where(x => x.Alive).Select(x => (x, d: JsMath.Hypot(Soldiers.HomeOf(m, x).x - before[x].Item1.x, Soldiers.HomeOf(m, x).y - before[x].Item1.y))).Where(q => q.d > 0.6)
                .Select(q => $"№{q.x.Id} ряд {q.x.Row} фигурка {m.Figs.IndexOf(q.x.Fig)}{(q.x.Fig == front.Fig ? "*" : "")} {q.d:0.00} м"));
            True(far == 0 && other == 0 && moved <= 2, $"ушли дальше 1,6 м: {far}, сменили фигурку: {other}, сдвинулись: {moved} — {info}");
        }));

        yield return ("замена павших (В14): в бою на новое место идут шагом — никто не перескакивает и не бежит сверх хода фигурки", Figs(() =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 7);
            var pos = new Dictionary<Man, (double x, double y, double fx, double fy, FigState f)>();
            double worst = 0; Man who = null;
            bt.Turn(t =>
            {
                foreach (var m in new[] { a, b })
                    foreach (var x in m.Men)
                    {
                        if (!x.Alive || x.Fig == null) continue;
                        if (x.Reseat && pos.TryGetValue(x, out var p) && p.f == x.Fig && !x.Fig.Fighting)
                        {
                            double d = JsMath.Hypot(x.X - p.x - (x.Fig.X - p.fx), x.Y - p.y - (x.Fig.Y - p.fy));
                            if (d > worst) { worst = d; who = x; }
                        }
                        pos[x] = (x.X, x.Y, x.Fig.X, x.Fig.Y, x.Fig);
                    }
            });
            double lim = (R.Men.ReseatRunMps + 2.5) * R.Move.Dt;   // трусцой (отставший далеко) плюс толкотня
            True(worst <= lim, $"боец №{who?.Id} сдвинулся за шаг на {worst:0.00} м сверх своей фигурки (предел {lim:0.00})");
        }));

        yield return ("бойцы: одно зерно — один исход", () =>
        {
            double[] Run()
            {
                var (bt, a, b) = Duel("knights", "infantry", 40, 8);
                bt.Turn();
                return a.Men.Concat(b.Men).Where(x => x.Alive).SelectMany(x => new[] { x.Id, x.X, x.Y }).ToArray();
            }
            True(Run().SequenceEqual(Run()), "повтор совпал");
        });
    }
}
