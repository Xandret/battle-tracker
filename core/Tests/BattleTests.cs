// ═══════════ BattleTests.cs — бой в движении, БД1 (Г62–Г64, Г44, Г29, Г30) ═══════════
// Рукопашная внутри хода с движением: средние стола в полном контакте, бой через границу хода, натиск
// и его условия, кто начинает обмен, фланг без самоповорота, потери — фигурками.
using BattleCore;

static class BattleTests
{
    static Rules R => Rules.Base;
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    static Geo Open(double w = 1000, double h = 1000) => MoveTests.Open(w, h);
    // A — в dist м от переднего края B (B в центре, лицом вверх; A смотрит на него сверху)
    static (Battle bt, Mover a, Mover b) Duel(string ta, string tb, double dist, uint seed, int factionB = 2)
    {
        var bt = new Battle(Open(), R, new EngineContext { Rng = new Mulberry32(seed).Next });
        var TA = Templates.Get(ta); var TB = Templates.Get(tb);
        var b = bt.Add(TB.Make(2, TB.Name, 1000, factionB), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), R);
        var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + dist + fa.Depth / 2), 180);
        return (bt, a, b);
    }
    static MoveOrder Attack(int target, bool charge = false) => new MoveOrder { Kind = OrderKind.Attack, TargetId = target, Charge = charge };

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("бой (Г62): полный контакт с начала хода — средние потери стола ±10%", () =>
        {
            foreach (var (ta, tb) in new[] { ("infantry", "infantry"), ("infantry", "militia") })
            {
                // стол — 1000 ходов (он быстрый, среднее точное), бой в движении — 40
                const int N = 40, NT = 1000;
                double tA = 0, tB = 0, bA = 0, bB = 0;
                var TA = Templates.Get(ta); var TB = Templates.Get(tb);
                for (uint i = 1; i <= NT; i++)
                {
                    var t = Tabletop.Turn(TA.Make(1, "A", 1000, 1), TB.Make(2, "B", 1000, 2), new TurnSetup(), new EngineContext { Rng = new Mulberry32(i).Next });
                    tA += t.LossA / (double)NT * N; tB += t.LossB / (double)NT * N;
                }
                for (uint i = 1; i <= N; i++)
                {
                    var (bt, a, b) = Duel(ta, tb, 0.5, i + 7000);
                    bt.Order(a, Attack(2));
                    bt.Turn();
                    bA += 1000 - a.P.U.Soldiers; bB += 1000 - b.P.U.Soldiers;
                }
                True(Math.Abs(bA / tA - 1) <= 0.1 && Math.Abs(bB / tB - 1) <= 0.1,
                    $"{ta} → {tb}: стол {tA / N:0}/{tB / N:0}, бой в движении {bA / N:0}/{bB / N:0}");
            }
        });

        yield return ("бой (Г62): сошлись посреди хода — бьются до его конца и дальше без перерыва", () =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 60, 3);
            bt.Order(a, Attack(2));
            var log1 = bt.Turn();
            var f = bt.Fights.SingleOrDefault();
            True(f != null, "схватка началась в первом ходу: " + string.Join(" | ", log1));
            True(f.T0 > 5 && f.T0 < 14, $"коснулись на {f.T0:0.0} с — посреди хода");
            double l1 = 2000 - a.P.U.Soldiers - b.P.U.Soldiers;
            True(l1 > 0, "до конца первого хода уже есть потери");
            bt.Turn();
            True(bt.Fights.Contains(f) && !f.Over, "во втором ходу — та же схватка, без перерыва");
            double l2 = 2000 - a.P.U.Soldiers - b.P.U.Soldiers - l1;
            True(l2 > l1, $"полный второй ход ({l2:0}) — больше потерь, чем неполный первый ({l1:0})");
        });

        yield return ("натиск (Г29): с разбега 150 м — всплеск урона, первые 2 с цель не отвечает", () =>
        {
            var (bt, a, b) = Duel("knights", "infantry", 150, 5);
            bt.Order(a, Attack(2, charge: true));
            double t0 = -1, aAt0 = 0, aAt2 = 0, bAt0 = 0, bAt2 = 0;
            var log = bt.Turn(t =>
            {
                var f = bt.Fights.FirstOrDefault();
                if (f == null) return;
                if (t0 < 0) { t0 = f.T0; aAt0 = a.P.U.Soldiers; bAt0 = b.P.U.Soldiers; }
                if (Math.Abs(t - (t0 + 1.95)) < 0.026) { aAt2 = a.P.U.Soldiers; bAt2 = b.P.U.Soldiers; }
            });
            True(log.Any(l => l.Contains("натиск «Конные рыцари»")), "журнал: натиск — " + string.Join(" | ", log));
            True(bt.Details.Any(d => d.Contains("(натиск)")), "окно натиска: " + string.Join(" | ", bt.Details));
            True(aAt2 == aAt0, $"рыцари за первые 2 с потеряли {aAt0 - aAt2:0.0}");
            True(bAt0 - bAt2 > 50, $"пехота за первые 2 с потеряла {bAt0 - bAt2:0.0}");
        });

        yield return ("натиск (К29): без разбега 50 м — нет; пики во фронт — гасят", () =>
        {
            var (bt, a, _) = Duel("knights", "infantry", 30, 5);
            bt.Order(a, Attack(2, charge: true));
            var log = bt.Turn();
            True(log.Any(l => l.Contains("без натиска: разбег")), "мало разбега: " + string.Join(" | ", log));
            var (bt2, a2, _) = Duel("knights", "pikemen", 150, 5);
            bt2.Order(a2, Attack(2, charge: true));
            var log2 = bt2.Turn();
            True(log2.Any(l => l.Contains("пики во фронт гасят натиск")), "пики: " + string.Join(" | ", log2));
        });

        yield return ("бой (Г44): начинает атакующий, даже если у стоящего дисциплина выше", () =>
        {
            var (bt, a, b) = Duel("militia", "guard", 40, 9);
            bt.Order(a, Attack(2));
            bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
            bt.Turn();
            var f = bt.Fights.Single();
            True(f.A == a, $"начал «{f.A.P.U.Name}», а атаковало ополчение");
        });

        yield return ("бой (Г63): атакованный во фланг сам не поворачивается, удар идёт во фланг", () =>
        {
            var bt = new Battle(Open(), R, new EngineContext { Rng = new Mulberry32(4).Next });
            var inf = Templates.Get("infantry");
            var b = bt.Add(inf.Make(2, "Стоят", 1000, 2), 500, 500, 0);
            var a = bt.Add(inf.Make(1, "Во фланг", 1000, 1), 500 - 62.5 - 4 - 40, 500, 90);   // слева от B, смотрит на восток
            bt.Order(a, Attack(2));
            Fight f = null; bool flank = false;
            for (int turn = 0; turn < 2; turn++)
                bt.Turn(t => { f ??= bt.Fights.FirstOrDefault(); if (f != null && f.SA.Flank > 0.5) flank = true; });
            True(f != null, "сошлись");
            True(flank, "удар пришёлся во фланг");
            True(Math.Abs(MoveSim.AngleDiff(b.P.Facing, 0)) < 1e-9, $"«Стоят» повернулись на {b.P.Facing:0}°");
        });

        yield return ("потери (Г30): фигурки падают, строй смыкается — фронт как у фишки трекера", () =>
        {
            var (bt, a, b) = Duel("guard", "militia", 0.5, 11);
            bt.Order(a, Attack(2));
            for (int turn = 0; turn < 2; turn++) bt.Turn();
            foreach (var m in new[] { a, b })
            {
                var u = m.P.U;
                var lay = Formation.Layout(u, 10, R);
                True(m.P.Figs.Count == lay.Count && m.Figs.Count == lay.Count, $"«{u.Name}»: фигурок {m.P.Figs.Count}, тел {m.Figs.Count}, по численности {lay.Count}");
                True(Math.Abs(m.P.Fp.Front - Formation.Of(u, R).Front) < 1e-9, $"«{u.Name}»: фронт {m.P.Fp.Front} м, у фишки {Formation.Of(u, R).Front} м");
            }
            True(b.Fallen.Count > 0 && b.P.U.Soldiers < 1000, $"ополчение потеряло {1000 - b.P.U.Soldiers:0}, упало фигурок {b.Fallen.Count}");
        });

        yield return ("бой: одно зерно — один исход", () =>
        {
            double[] Run()
            {
                var (bt, a, b) = Duel("knights", "infantry", 120, 21);
                bt.Order(a, Attack(2, charge: true));
                for (int i = 0; i < 2; i++) bt.Turn();
                return new[] { a.P.U.Soldiers, b.P.U.Soldiers }.Concat(a.Figs.SelectMany(f => new[] { f.X, f.Y })).ToArray();
            }
            True(Run().SequenceEqual(Run()), "повтор совпал");
        });
    }
}
