// ═══════════ BattleTests.cs — бой в движении, БД1 (Г62–Г64, Г44, Г29, Г30) ═══════════
// Рукопашная внутри хода с движением: средние стола в полном контакте, бой через границу хода, натиск
// и его условия, кто начинает обмен, фланг без самоповорота, потери — фигурками.
using BattleCore;

static class BattleTests
{
    public static Rules Use = Rules.Base;   // Г92: умолчание — бойцы-тела; тот же набор на фигурках-капсулах — dotnet run --project Tests -- battle-figs
    static Rules R => Use;
    // Г92: умолчание — бойцы-тела. Тесты именно старого режима (фигурки-капсулы, живые бойцы внутри них) — в обёртке Figs,
    // пока его код не убран (Г97)
    static Action Figs(Action run) => () => { var was = Use; Use = Rules.Figures; try { run(); } finally { Use = was; } };
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
    static Fight FightOf(Mover a, Mover b, Battle bt) => bt.Fights.FirstOrDefault(f => !f.Over && (f.A == a && f.B == b || f.A == b && f.B == a));
    static double[][] Corner(Mover m)
    {
        double f = m.P.Fp.Front / 2, d = m.P.Fp.Depth / 2;
        return new[] { (-f, -d), (f, -d), (f, d), (-f, d) }.Select(p => { m.P.ToWorld(p.Item1, p.Item2, out var x, out var y); return new[] { x, y }; }).ToArray();
    }

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("бой (Г62): полный контакт с начала хода — средние потери стола ±12% (было ±10%: после Г111 п.2 пехота на пехоту даёт −10,1% — твёрдые враги гасят подход, касаний меньше; калибровка — к ГМу)", () =>
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
                True(Math.Abs(bA / tA - 1) <= 0.12 && Math.Abs(bB / tB - 1) <= 0.12,
                    $"{ta} → {tb}: стол {tA / N:0.0}/{tB / N:0.0}, бой в движении {bA / N:0.0}/{bB / N:0.0} ({(bA / tA - 1) * 100:+0.0;-0.0}% / {(bB / tB - 1) * 100:+0.0;-0.0}%)");
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
            Fight f = null;
            bt.Turn(_ => f ??= bt.Fights.FirstOrDefault());   // схватку — в миг начала: к концу хода ополчение может уже бежать
            True(f != null, "не сошлись");
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
            bt.MoraleChecks = false;   // строй, а не бегство: ополчение держится до конца (бегство — БД4, ниже)
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

        // ── БД2: стрельба (Г65, Г66, Г40, Г67) ──
        // лучники в dist м перед целью (цель — в центре, лицом вверх)
        (Battle bt, Mover a, Mover b) Range(string tb, double dist, uint seed, string ta = "archers", int factionB = 2)
        {
            var bt = new Battle(Open(2000, 1400), R, new EngineContext { Rng = new Mulberry32(seed).Next });
            var TB = Templates.Get(tb); var TA = Templates.Get(ta);
            var b = bt.Add(TB.Make(2, TB.Name, 1000, factionB), 1000, 800, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), R);
            var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 1000, 800 - (b.P.Fp.Depth / 2 + dist + fa.Depth / 2), 180);
            return (bt, a, b);
        }

        yield return ("стрельба (Г65, Г75): лучники по стоящей пехоте со 100 м — около стола (полная сверка — 100 боёв, +4,5%)", () =>
        {
            const int N = 12;   // разброс одного боя ~25% — среднее 12 боёв держится в ±20%
            double table = 0, game = 0;
            var TA = Templates.Get("archers"); var TB = Templates.Get("infantry");
            for (uint i = 1; i <= 1000; i++)
                table += TabletopVolley.Turn(TA.Make(1, "A", 1000, 1), TB.Make(2, "B", 1000, 2), new TurnSetup(), new EngineContext { Rng = new Mulberry32(i).Next }).LossB / 1000;
            for (uint s = 1; s <= N; s++)
            {
                var (bt, a, b) = Range("infantry", 100, s + 9000);
                bt.Order(a, Attack(2));
                bt.Turn();
                bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });   // второй ход — долетают стрелы первого
                bt.Turn();
                game += (1000 - b.P.U.Soldiers) / N;
            }
            True(Math.Abs(game / table - 1) <= 0.2, $"стол {table:0}, бой со стрельбой {game:0}");
        });

        yield return ("стрельба (Г65): без приказа не стреляют; по стреляющим в них — отвечают", () =>
        {
            var (bt, a, b) = Range("archers", 100, 31);          // друг против друга, лицом
            bt.Order(a, Attack(2));
            var log = bt.Turn();
            True(log.Any(l => l.Contains("ответом «Лучники»")), "ответная стрельба: " + string.Join(" | ", log));
            var (bt2, a2, b2) = Range("infantry", 120, 32);       // лучники без приказа, на них идёт пехота
            bt2.Order(b2, Attack(1));
            bt2.Turn();
            True(bt2.Shots.Arrows == 0, $"без приказа выпущено {bt2.Shots.Arrows} стрел");
        });

        yield return ("стрельба (Г65): цель вне дальности — подходят на дальность, встают лицом к ней и стреляют", () =>
        {
            var (bt, a, b) = Range("infantry", 380, 33);
            bt.Order(a, Attack(2));
            long arrows = 0;
            for (int turn = 0; turn < 4 && arrows == 0; turn++) { bt.Turn(); arrows += bt.Shots.Arrows; }
            True(arrows > 0, "так и не выстрелили");
            double gap = BattleMap.PolyGap(Corner(a), Corner(b));
            True(gap <= BattleMap.RangeOf(a.P.U, R) + 1e-6, $"стоят в {gap:0} м — дальше дальности");
        });

        yield return ("упреждение (Г66): по идущей поперёк пехоте попадают реже, чем по стоящей", () =>
        {
            double Rate(bool moving)
            {
                double hits = 0, arrows = 0;
                for (uint s = 1; s <= 2; s++)
                {
                    var bt = new Battle(Open(3000, 1200), R, new EngineContext { Rng = new Mulberry32(s + 700).Next });
                    var inf = Templates.Get("infantry");
                    var b = bt.Add(inf.Make(2, "Пехота", 500, 2), moving ? 1450 : 1500, 700, moving ? 90 : 0);
                    var a = bt.Add(Templates.Get("archers").Make(1, "Лучники", 500, 1), 1500, 700 - (b.P.Fp.Depth / 2 + 102.5), 180);
                    bt.Order(a, Attack(2));
                    if (moving) bt.Order(b, new MoveOrder { X = 2800, Y = 700, Facing = 90 });
                    bt.Turn();
                    hits += bt.Shots.Hits; arrows += bt.Shots.Arrows;
                }
                return hits / Math.Max(1, arrows);
            }
            double stand = Rate(false), move = Rate(true);
            True(move < stand * 0.85, $"попаданий: по стоящей {stand * 100:0.0}%, по идущей {move * 100:0.0}%");
        });

        yield return ("стрельба (Г40): по врагу, сцепившемуся с нашими, не стреляют", () =>
        {
            var bt = new Battle(Open(1200, 1200), R, new EngineContext { Rng = new Mulberry32(41).Next });
            var inf = Templates.Get("infantry");
            var foe = bt.Add(inf.Make(2, "Враг", 1000, 2), 600, 600, 0);
            var ours = bt.Add(inf.Make(3, "Наши", 1000, 1), 600, 600 - 8.5, 180);   // уже в схватке
            var arc = bt.Add(Templates.Get("archers").Make(1, "Лучники", 1000, 1), 600, 450, 180);
            bt.Order(ours, Attack(2));
            bt.Order(arc, Attack(2));
            bt.Turn();
            True(bt.Fights.Any(f => f.Touching), "наши сцепились");
            True(bt.Shots.Arrows == 0, $"выпущено {bt.Shots.Arrows} стрел по свалке");
        });

        yield return ("павшие (Г67): каждый выбывший записан — от стрел с частью тела, в рукопашной у переднего края", () =>
        {
            var (bt, a, b) = Range("infantry", 100, 51);
            bt.Order(a, Attack(2));
            bt.Turn(); bt.Order(a, new MoveOrder { Kind = OrderKind.Hold }); bt.Turn();
            int shot = bt.Deaths.Count(d => d.UnitId == 2);
            True(shot == 1000 - (int)b.P.U.Soldiers, $"от стрел выбыло {1000 - b.P.U.Soldiers}, записано {shot}");
            True(bt.Deaths.All(d => d.Part == "head" || d.Part == "torso" || d.Part == "legs" || d.Part == "horse"), "часть тела у каждого");
            var (bm, ma, mb) = Duel("infantry", "infantry", 0.5, 52);
            bm.Order(ma, Attack(2));
            bm.Turn();
            int lost = 2000 - (int)Math.Round(ma.P.U.Soldiers) - (int)Math.Round(mb.P.U.Soldiers), rec = bm.Deaths.Count;
            True(Math.Abs(rec - lost) <= 2, $"в рукопашной выбыло {lost}, записано {rec}");
            // у переднего края: павшие B — не дальше полуглубины строя от линии касания
            double edge = mb.P.Y - mb.P.Fp.Depth / 2;
            True(bm.Deaths.Where(d => d.UnitId == 2).All(d => d.Y < edge + 6), "павшие B — у переднего края");
        });

        // ── БД3: охват (Г68) и двое на одного (Г69) ──
        yield return ("охват (Г68): рыцари шире пехоты — свисающие колонны огибают её: в деле больше 3/4, бьют во фланг и в тыл", () =>
        {
            var (bt, a, b) = Duel("knights", "infantry", 0.5, 31);
            bt.Order(a, Attack(2));
            double most = 0, side = 0;
            bt.Turn(t =>
            {
                var f = bt.Fights.FirstOrDefault();
                if (f == null) return;
                var s = f.Of(a);
                most = Math.Max(most, s.Engaged); side = Math.Max(side, s.Flank + s.Rear);
            });
            int behind = 0;
            foreach (var s in a.Figs) { b.P.ToLocal(s.X, s.Y, out _, out var ly); if (ly > b.P.Fp.Depth / 2) behind++; }
            True(most > 0.75, $"в деле не больше {most:P0}");
            True(side > 0.1, $"во фланг и в тыл — {side:P0} касающихся");
            True(behind >= 5, $"за спиной пехоты фигурок {behind}");
        });

        yield return ("охват (Г68): рыцари на пехоту в упор — потери пехоты не меньше 70% стола (без охвата было 55%)", () =>
        {
            const int N = 10, NT = 1000;
            var TA = Templates.Get("knights"); var TB = Templates.Get("infantry");
            double table = 0, game = 0;
            for (uint i = 1; i <= NT; i++)
                table += Tabletop.Turn(TA.Make(1, "A", 1000, 1), TB.Make(2, "B", 1000, 2), new TurnSetup(), new EngineContext { Rng = new Mulberry32(i).Next }).LossB / NT;
            for (uint i = 1; i <= N; i++)
            {
                var (bt, a, b) = Duel("knights", "infantry", 0.5, i + 7000);
                bt.Order(a, Attack(2));
                bt.Turn();
                game += (1000 - b.P.U.Soldiers) / N;
            }
            True(game >= 0.6 * table && game <= 1.1 * table, $"стол {table:0}, бой в движении {game:0}");   // Г111 п.4: колонны бьются в своём секторе, за спиной строя на другой край не уходят — в тыл заходит меньше: было ≥ 70 % стола, стало ~63 % (вопрос ГМу)
        });

        yield return ("охват (Г68, Г63): пехота во фланг пехоте — что не влезло во фланг, огибает; атакованный сам не заворачивает и отвечает слабее стола", Figs(() =>
        {
            const int N = 20, NT = 1000;
            var inf = Templates.Get("infantry");
            double tA = 0, tB = 0, gA = 0, gB = 0; int wrapA = 0, wrapB = 0;
            for (uint i = 1; i <= NT; i++)
            {
                var t = Tabletop.Turn(inf.Make(1, "A", 1000, 1), inf.Make(2, "B", 1000, 2), new TurnSetup { SectorA = "flank" }, new EngineContext { Rng = new Mulberry32(i).Next });
                tA += t.LossA / (double)NT; tB += t.LossB / (double)NT;
            }
            for (uint i = 1; i <= N; i++)
            {
                var bt = new Battle(Open(), R, new EngineContext { Rng = new Mulberry32(i + 7000).Next });
                var b = bt.Add(inf.Make(2, "B", 1000, 2), 500, 500, 0);
                var fa = Formation.Of(inf.Make(1, "A", 1000, 1), R);
                var a = bt.Add(inf.Make(1, "A", 1000, 1), 500 - (b.P.Fp.Front / 2 + 0.5 + fa.Depth / 2), 500, 90);
                bt.Order(a, Attack(2));
                bt.Turn(t => { wrapA = Math.Max(wrapA, a.Figs.Count(q => q.Wrap)); wrapB = Math.Max(wrapB, b.Figs.Count(q => q.Wrap)); });
                gA += (1000 - a.P.U.Soldiers) / N; gB += (1000 - b.P.U.Soldiers) / N;
            }
            True(wrapA > 10 && wrapB == 0, $"в охвате фигурок: у атакующего {wrapA}, у атакованного {wrapB}");
            // охват идёт секунды — за первый ход стоящий теряет меньше стола, но не меньше 70%; ответ — слабее стола (Г63)
            True(gB >= 0.7 * tB && gB <= 1.15 * tB && gA < tA, $"стол {tA:0}/{tB:0}, бой в движении {gA:0}/{gB:0}");
        }));

        yield return ("двое на одного (Г69): колонны защитника делятся между врагами, ответ — один на круг на всех", () =>
        {
            var bt = new Battle(Open(), R, new EngineContext { Rng = new Mulberry32(12).Next });
            var inf = Templates.Get("infantry");
            var b = bt.Add(inf.Make(3, "Стоят", 1000, 2), 500, 500, 0);
            var half = Formation.Of(inf.Make(1, "Левые", 500, 1), R);
            double y = 500 - (b.P.Fp.Depth / 2 + 0.5 + half.Depth / 2);
            var a1 = bt.Add(inf.Make(1, "Левые", 500, 1), 500 - half.Front / 2 - 0.5, y, 180);
            var a2 = bt.Add(inf.Make(2, "Правые", 500, 1), 500 + half.Front / 2 + 0.5, y, 180);
            bt.Order(a1, Attack(3)); bt.Order(a2, Attack(3));
            double most = 0; bool both = false;
            bt.Turn(t =>
            {
                var live = bt.Fights.Where(f => !f.Over).ToList();
                most = Math.Max(most, live.Sum(f => f.Of(b).Engaged));
                if (live.Count == 2 && live.All(f => f.Of(b).Engaged > 0)) both = true;
            });
            True(both, "«Стоят» бились с обоими сразу");
            True(most <= 1 + 1e-9, $"в деле у «Стоят» в сумме {most:P0}");
            int Lines(string from, bool counter) => bt.Details.Count(l => l.Contains($" · {from} → ") && l.Contains("(ответ)") == counter);
            True(Lines("Левые", false) == 1 && Lines("Правые", false) == 1, "обе атаки: " + string.Join(" | ", bt.Details));
            True(Lines("Стоят", true) == (int)Units.CounterLimit(b.P.U, R), "ответы «Стоят»: " + string.Join(" | ", bt.Details));
        });

        yield return ("свита (Г121): у отряда с полководцем свита из 12 бойцов на ближайших к нему местах в строю (все ближе 4 м от него по местам), состав держится три хода марша и в схватке (сменяются только павшие), после поединка свита та же и на местах; без полководца свиты нет", () =>
        {
            var bt = new Battle(Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(91).Next, CommanderOf = u => u.Id == 1 ? new Commander { Id = 1, Name = "Воевода", FactionId = 1, Valor = 12 } : null });
            var T = Templates.Get("infantry");
            var a = bt.Add(T.Make(1, "Дружина", 1000, 1), 500, 700, 0); a.P.U.CommanderId = 1;
            var b = bt.Add(T.Make(2, "Враг", 1000, 2), 500, 300, 180);
            bt.Order(a, new MoveOrder { X = 500, Y = 600, Facing = 0 }); bt.Turn();
            var cm = a.CommanderMan; True(cm != null && a.Guard.Count == R.Duel.GuardN && a.Guard.All(x => x.Guard) && b.Guard.Count == 0, $"полководец {cm != null}, свита {a.Guard.Count}, у врага {b.Guard.Count}");
            var (hx, hy) = Soldiers.HomeOf(a, cm);
            True(a.Guard.All(x => { var h = Soldiers.HomeOf(a, x); return JsMath.Hypot(h.x - hx, h.y - hy) < 4; }), "свита не рядом с полководцем по местам");
            var set0 = new HashSet<Man>(a.Guard);
            bt.Order(a, new MoveOrder { X = 500, Y = 450, Facing = 0 }); bt.Turn(); bt.Turn();
            True(a.Guard.Count(set0.Contains) == R.Duel.GuardN, $"на марше состав сменился: осталось {a.Guard.Count(set0.Contains)} из {R.Duel.GuardN}");
            bt.Order(a, Attack(2)); bt.Turn(); bt.Turn();
            int kept = a.Guard.Count(set0.Contains), dead = set0.Count(x => !x.Alive);
            True(a.Guard.Count == R.Duel.GuardN && kept + dead >= R.Duel.GuardN, $"в схватке: свита {a.Guard.Count}, прежних {kept}, павших из прежних {dead}");
            True(a.Guard.All(x => x.Alive && JsMath.Hypot(x.X - cm.X, x.Y - cm.Y) < 8) || !cm.Alive, $"свита разбрелась: до {a.Guard.Max(x => JsMath.Hypot(x.X - cm.X, x.Y - cm.Y)):0} м от полководца");
        });

        yield return ("разворот к бою (Г120): пехота наступает на линию наискось с фронта — за 80 м выходит на ось её центра, доворачивается и бьёт всем фронтом: к касанию курс в пределах 8° от оси врага и в деле не меньше колонн, чем без разворота (где строй врезается углом, курс 15–20° мимо); атака с фланга — без разворота, колонной", () =>
        {
            (double skew, double engaged, bool deploy) Run(double deployM)
            {
                var r = new Rules(); r.Move.DeployM = deployM;
                var bt = new Battle(Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(81).Next });
                var T = Templates.Get("infantry");
                var b = bt.Add(T.Make(2, "Линия", 1000, 2), 500, 500, 0);
                var a = bt.Add(T.Make(1, "Наступающие", 1000, 1), 560, 300, 200);
                bt.Order(b, new MoveOrder { Kind = OrderKind.Hold }); bt.Order(a, Attack(2));
                bool deploy = a.Order.Deploy; double skew = double.NaN, eng = 0;
                for (int t = 0; t < 4 && double.IsNaN(skew); t++)
                    bt.Turn(tt => { var f = FightOf(a, b, bt); if (f != null && double.IsNaN(skew)) skew = Math.Abs(MoveSim.AngleDiff(a.P.Facing, 180)); });
                for (int t = 0; t < 1; t++) bt.Turn(tt => { var f = FightOf(a, b, bt); if (f != null) eng = Math.Max(eng, f.Of(a).Engaged); });
                return (skew, eng, deploy);
            }
            var with = Run(80); var without = Run(0);
            True(with.deploy && !without.deploy, $"точка разворота: с {with.deploy}, без {without.deploy}");
            True(!double.IsNaN(with.skew) && with.skew <= 8, $"к касанию курс мимо оси врага на {with.skew:0}° (без разворота {without.skew:0}°)");
            True(with.engaged >= without.engaged * 0.95, $"в деле с разворотом {with.engaged:0}, без {without.engaged:0}");
            var bt2 = new Battle(Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(82).Next });
            var T2 = Templates.Get("infantry");
            var b2 = bt2.Add(T2.Make(2, "Линия", 1000, 2), 500, 500, 0);
            var a2 = bt2.Add(T2.Make(1, "Во фланг", 1000, 1), 750, 500, 270);
            bt2.Order(a2, Attack(2));
            True(!a2.Order.Deploy, "атака с фланга пошла через точку разворота");
        });

        yield return ("охват (Г111 п.4): рыцари шире пехоты, половина мест у врага занята своими — колонны не уходят за спиной строя на другой край: ни одна не оказалась на противоположной стороне врага от той, где начала", () =>
        {
            var bt = new Battle(Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(44).Next });
            var TK = Templates.Get("knights"); var TB = Templates.Get("infantry");
            var b = bt.Add(TB.Make(2, "Пехота", 400, 2), 500, 500, 0);
            var blocker = bt.Add(TB.Make(3, "Свои", 400, 1), 500 + b.P.Fp.Front / 2 + 12, 500, 90);   // стоят у правого фланга пехоты — места там заняты
            var fa = Formation.Of(TK.Make(1, "Рыцари", 600, 1), R);
            var a = bt.Add(TK.Make(1, "Рыцари", 600, 1), 500, 500 - (b.P.Fp.Depth / 2 + 40 + fa.Depth / 2), 180);
            bt.Order(a, Attack(2)); bt.Order(blocker, new MoveOrder { Kind = OrderKind.Hold });
            var side0 = new Dictionary<FigState, int>();
            int SideAt(double lx, double ly) => ly < -b.P.Fp.Depth / 2 ? 0 : ly > b.P.Fp.Depth / 2 ? 1 : (lx < 0 ? 2 : 3);   // по глубине: впереди — фронт, позади — тыл, между — фланги
            int Side(FigState s) { b.P.ToLocal(s.AX, s.AY, out var lx, out var ly); return SideAt(lx, ly); }   // по якорю колонны (голове)
            bool Opp(int p, int q) => p == 0 && q == 1 || p == 1 && q == 0 || p == 2 && q == 3 || p == 3 && q == 2;
            int crossed = 0; var edgeOf = new Dictionary<FigState, int>();
            // место колонны у врага сменило сторону на противоположную той, где колонна сейчас (за спиной строя на другой край);
            // та же сторона держится, даже если враг развернулся или его места сдвинулись с потерями
            for (int t = 0; t < 3; t++)
                bt.Turn(tt => { foreach (var s in a.Figs) { if (!s.Wrap || s.WFoe != b) { edgeOf.Remove(s); continue; } int e = SideAt(s.WSlotX, s.WSlotY); if (edgeOf.TryGetValue(s, out var was) && was == e) continue; edgeOf[s] = e; if (Opp(Side(s), e)) crossed++; } });
            True(a.Figs.Count(s => s.Wrap) > 5, $"охвата не было: колонн в охвате {a.Figs.Count(s => s.Wrap)}");
            True(bt.WrapOpposite == 0, $"колоннам давали место на противоположной стороне врага: {bt.WrapOpposite} раз (по счёту ядра; по наблюдению за якорями и сменой стороны места — {crossed})");
        });

        yield return ("охват (Г68): враг разбит — колонны возвращаются на свои места сквозь свой строй", () =>
        {
            var (bt, a, b) = Duel("knights", "infantry", 0.5, 31);
            bt.Order(a, Attack(2));
            bt.Turn();
            int wrapped = a.Figs.Count(s => s.Wrap);
            True(wrapped > 10, $"в охвате {wrapped} фигурок");
            b.P.U.Status = "destroyed"; b.P.U.Soldiers = 0;
            bt.Turn();
            double far = 0;
            for (int k = 0; k < a.Figs.Count; k++)
            {
                // у бойцов-тел (Б1) место колонны держит якорь
                var f = a.Figs[k]; double fx = R.Move.MenBodies ? f.AX : f.X, fy = R.Move.MenBodies ? f.AY : f.Y;
                a.P.ToWorld(a.P.Figs[k].X, a.P.Figs[k].Y, out var sx, out var sy);
                far = Math.Max(far, JsMath.Hypot(fx - sx, fy - sy));
            }
            True(a.Figs.All(s => !s.Wrap && !s.Turned && !s.Returning), "охват снят");
            True(far < 1, $"дальше всех от своего места — {far:0.0} м");
        });

        // ── БД4: БД и бегство в ходу (Г70–Г74) ──
        // B стоит в центре большой карты лицом вверх; A — в упор сверху; B ломается легко (БД, дисциплина — заданы)
        (Battle bt, Mover a, Mover b) Breaking(string ta, string tb, double morale, double disc, uint seed, double menA = 1000, double w = 1200, double h = 1600)
        {
            var bt = new Battle(Open(w, h), R, new EngineContext { Rng = new Mulberry32(seed).Next });
            var ub = Templates.Get(tb).Make(2, "Пехота", 1000, 2); ub.Morale = morale; ub.Discipline = disc;
            var b = bt.Add(ub, w / 2, 400, 0);
            var fa = Formation.Of(Templates.Get(ta).Make(1, "Враг", menA, 1), R);
            var a = bt.Add(Templates.Get(ta).Make(1, "Враг", menA, 1), w / 2, 400 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
            bt.Order(a, Attack(2));
            return (bt, a, b);
        }

        yield return ("проверки (Г74): после удара с потерями при БД ≤ 40 — проверка БД, провал — БД 0 и сразу проверка на побег", () =>
        {
            var (bt, a, b) = Breaking("knights", "infantry", 30, 1, 5);
            var log = bt.Turn();
            True(log.Any(l => l.Contains("Проверка БД: Пехота")), "проверка БД: " + string.Join(" | ", log));
            True(log.Any(l => l.Contains("Проверка на побег: Пехота")), "проверка на побег: " + string.Join(" | ", log));
            True(b.P.U.Status == "fled" && b.Fleeing, $"«Пехота» {b.P.U.Status}");
        });

        yield return ("проверки (Г74): дисциплина 60+ — «стоять насмерть», первый бросок на побег не считается", () =>
        {
            var (bt, a, b) = Breaking("knights", "infantry", 1, 70, 5);
            var log = bt.Turn();
            var first = log.FirstOrDefault(l => l.Contains("Проверка на побег: Пехота"));
            True(first != null && first.Contains("Стоять насмерть") && !first.Contains("бегство"), "первый бросок: " + first);
        });

        yield return ("бегство (Г70, Г71): толпой прочь от врага на норме, сквозь своих, у края карты — ушёл с поля боя", () =>
        {
            var (bt, a, b) = Breaking("infantry", "infantry", 30, 1, 6, menA: 300, h: 900);
            var res = Templates.Get("infantry").Make(3, "Резерв", 1000, 2); res.Discipline = 100;   // свои за спиной, не паникуют
            var r = bt.Add(res, 600, 400 + 4 + 40 + 4, 0);
            bt.Turn();
            True(b.Fleeing, "побежали в первый ход");
            bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
            double y0 = b.P.Y, d0 = JsMath.Hypot(b.P.X - a.P.X, b.P.Y - a.P.Y);
            bt.Turn();
            double run = b.Moved, d1 = JsMath.Hypot(b.P.X - a.P.X, b.P.Y - a.P.Y);
            True(run >= 80 && run <= 108, $"за ход толпа прошла {run:0} м, норма 100 (сквозь резерв — вполсилы, Г56; разброс скорости толпы ±5 %, Г71, плюс разбег бойцов Г84)");
            True(d1 > d0 + 80, $"от врага: было {d0:0} м, стало {d1:0}");
            True(b.P.Y > r.P.Y + 30 && r.P.U.Status == "active", $"прошли сквозь резерв: толпа на {b.P.Y:0}, резерв на {r.P.Y:0}");
            for (int i = 0; i < 6 && !b.Gone; i++) bt.Turn();
            True(b.Gone && b.Figs.Count == 0 && b.P.U.Status == "fled" && b.P.U.Soldiers > 0, $"ушёл с поля: {b.Gone}, фигурок {b.Figs.Count}, бойцов {b.P.U.Soldiers:0}");
        });

        yield return ("преследование (Г70): рыцари рубят бегущую пехоту — та не отвечает", () =>
        {
            var (bt, a, b) = Breaking("knights", "infantry", 30, 1, 5);
            bt.Turn();
            True(b.Fleeing, "побежали");
            double before = b.P.U.Soldiers;
            bt.Turn();
            True(b.P.U.Soldiers < before, $"потерь у бегущих нет: {before:0} → {b.P.U.Soldiers:0}");
            True(!bt.Details.Any(l => l.Contains(" · Пехота → ")), "бегущие ударили: " + string.Join(" | ", bt.Details));
        });

        yield return ("каскадная паника (Г73): бегство роняет соседа ближе 150 м, дальнего не трогает", () =>
        {
            var bt = new Battle(Open(1600, 900), R, new EngineContext { Rng = new Mulberry32(3).Next });
            var mil = Templates.Get("militia");
            Unit Weak(int id, string name) { var u = mil.Make(id, name, 1000, 2); u.Discipline = 1; return u; }
            var b = bt.Add(Weak(2, "Ополчение"), 800, 450, 0);
            var near = bt.Add(Weak(3, "Сосед"), 800 - 125 - 60, 450, 0);
            var far = bt.Add(Weak(4, "Дальние"), 800 + 125 + 300, 450, 0);
            var a = bt.Add(Templates.Get("guard").Make(1, "Гвардия", 1000, 1), 800, 450 - (4 + 0.5 + 4), 180);
            bt.Order(a, Attack(2));
            var log = bt.Turn();
            True(log.Any(l => l.Contains("«Ополчение» бежит")), "«Ополчение» побежало: " + string.Join(" | ", log));
            True(near.P.U.Status == "fled" && near.Fleeing, "сосед в 60 м побежал: " + string.Join(" | ", log));
            True(far.P.U.Status == "active", "дальние (300 м) не бросали");
            True(log.Any(l => l.Contains("каскадная паника")), "волна — в журнале");
        });

        yield return ("сплотить (Г72): враг ближе 150 м — ждут; дальше — бросок d100 ≤ дисциплина; успех — БД 40 и строй", () =>
        {
            var (bt, a, b) = Breaking("infantry", "infantry", 30, 1, 6, menA: 300);
            bt.Turn();
            True(b.Fleeing, "побежали");
            bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
            b.P.U.Discipline = 100;
            bt.Order(b, new MoveOrder { Kind = OrderKind.Rally });
            bt.Order(b, new MoveOrder { Kind = OrderKind.Move, X = 0, Y = 0 });   // иных приказов бегущий не слышит
            True(b.Fleeing && b.RallyPending && b.Order == null, "приказ «двигаться» бегущему не дошёл");
            for (int i = 0; i < 4 && b.Fleeing; i++) bt.Turn();
            True(!b.Fleeing && b.P.U.Status == "active" && b.P.U.Morale >= 40, $"сплотились: {b.P.U.Status}, БД {b.P.U.Morale}");
            True(Bodies.MinGap(a, b) > R.Rally.FreeM, $"сплотились в {Bodies.MinGap(a, b):0} м от врага");
            double far = double.MaxValue;
            for (int i = 0; i < 8 && far >= 3; i++)   // отбившиеся за строем врага подходят в обход, а то и прорубаясь, — до восьми ходов
            {
                bt.Turn(); far = 0;
                for (int k = 0; k < b.Figs.Count; k++)
                {
                    // у бойцов-тел (Б1) место колонны держит якорь; середина бойцов неполной колонны смещена вперёд
                    var f = b.Figs[k]; double fx = R.Move.MenBodies ? f.AX : f.X, fy = R.Move.MenBodies ? f.AY : f.Y;
                    b.P.ToWorld(b.P.Figs[k].X, b.P.Figs[k].Y, out var sx, out var sy); far = Math.Max(far, JsMath.Hypot(fx - sx, fy - sy));
                }
            }
            True(far < 3 && b.Order.Kind == OrderKind.Hold, $"строй собран: дальше всех от места {far:0.0} м");
        });

        yield return ("откат хода (Г112 п.2): снимок после второго хода — ещё два хода, откат, те же два хода заново дают тот же бой (бойцы, потери, БД, броски, журнал); Battle — тот же объект, снимок годится повторно", () =>
        {
            var rng = new Mulberry32(61);
            var bt = new Battle(Open(1000, 1000), R, new EngineContext { Rng = rng.Next });
            var T = Templates.Get("infantry"); var K = Templates.Get("knights");
            var a = bt.Add(T.Make(1, "Пехота", 1000, 1), 500, 450, 180); var b = bt.Add(K.Make(2, "Рыцари", 500, 2), 500, 600, 0);
            var c = bt.Add(Templates.Get("archers").Make(3, "Лучники", 300, 1), 500, 350, 180);
            bt.Order(a, Attack(2)); bt.Order(b, Attack(1)); bt.Order(c, Attack(2));
            bt.Turn(); bt.Turn();
            var snap = bt.Snapshot();
            string Hash() => string.Join("|", bt.Movers.Select(m => $"{m.P.U.Name}:{m.P.U.Soldiers:0}/{m.P.U.Morale:0}/{m.P.X:0.00},{m.P.Y:0.00}/{m.Men.Count}/" + string.Join(",", m.Men.Take(20).Select(x => $"{x.X:0.00},{x.Y:0.00}")))) + $"|deaths {bt.Deaths.Count}|fights {bt.Fights.Count}|rng {rng.Next():0.000000}";
            var log1 = bt.Turn(); bt.Turn(); string h1 = Hash();
            bt.Restore(snap);
            True(ReferenceEquals(bt.Movers.First(m => m.P.U.Id == 1), a) || bt.Movers.Count == 3, "отрядов после отката не три");
            var log2 = bt.Turn(); bt.Turn(); string h2 = Hash();
            True(h1 == h2, $"после отката бой пошёл иначе:\n{h1}\n{h2}");
            True(string.Join("\n", log1) == string.Join("\n", log2), "журнал хода после отката другой");
            bt.Restore(snap); bt.Turn(); bt.Turn();
            True(Hash() == h1, "второй откат с того же снимка дал другой бой");
        });

        yield return ("инструменты ГМа (Г112): численность рукой — бойцы по новой численности без павших; убрать отряд — схватка кончается, враг стоит; подкрепление после первого хода идёт в бой; модификатор БД из таблицы — строка в журнал и БД в пределах", () =>
        {
            var bt = new Battle(Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(51).Next });
            var T = Templates.Get("infantry");
            var a = bt.Add(T.Make(1, "Свои", 1000, 1), 500, 400, 180); var b = bt.Add(T.Make(2, "Враг", 1000, 2), 500, 460, 0);
            bt.Turn();
            True(bt.SetSoldiers(a, 600) && a.P.U.Soldiers == 600 && a.Men.Count(x => x.Alive) == 600 && bt.Deaths.Count(d => d.UnitId == 1) == 0, $"убавить: людей {a.P.U.Soldiers}, бойцов {a.Men.Count(x => x.Alive)}, павших своих {bt.Deaths.Count(d => d.UnitId == 1)}");
            True(bt.SetSoldiers(a, 900) && a.P.U.Soldiers == 900 && a.Men.Count(x => x.Alive) == 900, $"прибавить: людей {a.P.U.Soldiers}, бойцов {a.Men.Count(x => x.Alive)}");
            double v = bt.ApplyMorale(new[] { a }, "speech"); double m0 = a.P.U.Morale;
            True(v == 20 && m0 == 90 && double.IsNaN(bt.ApplyMorale(new[] { a }, "нет такого")), $"речь командира: +{v}, БД {m0}");
            bt.ApplyMorale(new[] { a }, "motivation"); True(a.P.U.Morale == Math.Min(R.Morale.Max, 140), $"мотивация: БД {a.P.U.Morale}");
            bt.Order(a, Attack(2)); bt.Turn();
            True(bt.Fights.Any(f => !f.Over && f.Touching), "схватки нет");
            True(bt.Remove(b) && b.Gone && !bt.Fights.Any(f => !f.Over) && !bt.Movers.Where(m => m != b).Any(m => m.Men.Any(x => x.Foe != null && !x.Foe.Alive)), "враг не убран или схватка не кончилась");
            var c = bt.Add(T.Make(3, "Подкрепление", 500, 2), 500, 600, 0);
            bt.Order(a, Attack(3)); var log = bt.Turn(); bt.Turn();
            True(bt.Fights.Any(f => !f.Over && (f.A == c || f.B == c)) && 500 - c.P.U.Soldiers > 0, $"подкрепление в бою: схваток {bt.Fights.Count(f => !f.Over)}, потери {500 - c.P.U.Soldiers:0}");
        });

        yield return ("поединок (Г108): вызов в 60 м принят — отряды стоят и друг друга не трогают, командиры сходятся в круге 6 м, бойцов в круге нет; удары по раундам, исход за 5…40 с: проигравший ранен или убит, БД сторон меняется, отряд проигравшего проверяется на побег; отказ — своей стороне −10 БД; вызов дальше 80 м нельзя", () =>
        {
            var ca = new Commander { Id = 1, Name = "Сэр Арн", FactionId = 1, Valor = 16 }; var cb = new Commander { Id = 2, Name = "Бор", FactionId = 2, Valor = 6 };
            var cmd = new Dictionary<int, Commander> { [1] = ca, [2] = cb };
            var ctx = new EngineContext { Rng = new Mulberry32(31).Next, CommanderOf = u => u.CommanderId.HasValue && cmd.TryGetValue(u.CommanderId.Value, out var c) ? c : null };
            var bt = new Battle(Open(1000, 1000), R, ctx);
            var T = Templates.Get("infantry");
            var ua = T.Make(1, "Дружина", 500, 1); ua.CommanderId = 1; var ub = T.Make(2, "Ватага", 500, 2); ub.CommanderId = 2; ub.Morale = 45;
            var a = bt.Add(ua, 500, 400, 180); var b = bt.Add(ub, 500, 460, 0);   // 60 м, лицом друг к другу
            True(bt.ChallengeWhy(a, b) == null, $"вызов нельзя: {bt.ChallengeWhy(a, b)}");
            var far = bt.Add(T.Make(3, "Дальние", 100, 2), 500, 900, 0); far.P.U.CommanderId = 2;
            True(bt.ChallengeWhy(a, far) != null, "вызов за 500 м принят");
            True(bt.Challenge(a, b) == null && bt.Challenges.Count == 1, "вызов не повис");
            True(bt.Answer(b, true) && bt.Duels.Count == 1 && bt.Challenges.Count == 0, "ответ не принят");
            bt.Order(a, Attack(2));   // приказ атаковать во время поединка — всё равно стоят
            double sideBefore = far.P.U.Morale, aBefore = ua.Morale, bBefore = ub.Morale;
            int inCircle = 0, ringChecks = 0, ringOk = 0, cmdIn = 0; bool over = false; var log = new List<string>();
            for (int t = 0; t < 4 && !over; t++)
            {
                log.AddRange(bt.Turn(tt =>
                {
                    var d = bt.Duels[0]; if (!d.Fighting || d.Over) return;
                    foreach (var m in new[] { a, b }) inCircle += m.Men.Count(x => !x.InDuel && JsMath.Hypot(x.X - d.X, x.Y - d.Y) < d.R - 0.5);
                    ringChecks++; if (a.Guard.Concat(b.Guard).Count(x => Math.Abs(JsMath.Hypot(x.X - d.X, x.Y - d.Y) - (d.R + 0.6)) < 1.5) >= 16) ringOk++;   // кольцо дособирается, пока первые раунды уже идут
                    if (JsMath.Hypot(d.ManA.X - d.X, d.ManA.Y - d.Y) < 3 && JsMath.Hypot(d.ManB.X - d.X, d.ManB.Y - d.Y) < 3) cmdIn++;
                }));
                over = bt.Duels[0].Over;
            }
            var du = bt.Duels[0];
            True(du.ManA != null && du.ManB != null, "полководцы не телами");
            True(ringChecks > 0 && ringOk * 10 >= ringChecks * 8 && cmdIn * 10 >= ringChecks * 8, $"кольцо стражи: {ringOk} из {ringChecks} шагов, полководцы в круге {cmdIn}");
            True(over && du.Winner != null && du.EndT - du.StartT >= 5 && du.EndT - du.StartT <= 40, $"поединок: кончился {over}, длился {du.EndT - du.StartT:0} с");
            True(du.Strikes.Count >= 6 && du.Strikes.Any(s => s.hit), $"ударов {du.Strikes.Count}");
            True(inCircle == 0, $"бойцов в круге поединка: {inCircle} (по шагам)");
            True(ua.Soldiers == 500 && ub.Soldiers == 500, $"отряды бились во время поединка: {500 - ua.Soldiers:0}/{500 - ub.Soldiers:0}");
            var win = du.Winner.P.U; var lose = du.Loser.P.U; var cl = du.Loser == a ? ca : cb;
            True(cl.Dead || cl.Wounded, "проигравший ни ранен, ни убит");
            True(cl.Dead == (lose.CommanderId == null), "убитый командир остался у отряда");
            True(log.Any(l => l.Contains("поединок:")) && log.Any(l => l.Contains("Проверка на побег")), "в журнале нет исхода или проверки на побег: " + string.Join(" | ", log.Where(l => l.Contains("поедин") || l.Contains("побег"))));
            double sideAfter = far.P.U.Morale;
            True(du.Winner == a ? sideAfter == sideBefore - R.Duel.LoseSideMorale : sideAfter == Math.Min(R.Morale.Max, sideBefore + R.Duel.WinSideMorale), $"сторона: БД {sideBefore} → {sideAfter}, победил {win.Name}");
            // отказ: новый вызов от другой пары
            var bt2 = new Battle(Open(1000, 1000), R, ctx);
            var a2 = bt2.Add(T.Make(1, "Дружина", 500, 1), 500, 400, 180); a2.P.U.CommanderId = 1;
            var b2 = bt2.Add(T.Make(2, "Ватага", 500, 2), 500, 460, 0); b2.P.U.CommanderId = 2; var c2 = bt2.Add(T.Make(3, "Ватага 2", 300, 2), 700, 700, 0);
            cb.Dead = false; ca.Dead = false;
            True(bt2.Challenge(a2, b2) == null && bt2.Answer(b2, false) && b2.P.U.Morale == 60 && c2.P.U.Morale == 60 && a2.P.U.Morale == 70, $"отказ: БД {b2.P.U.Morale}/{c2.P.U.Morale}, у вызвавшего {a2.P.U.Morale}");
        });

        yield return ("туман (Г107, Г18): сторона видит чужих не дальше 1 км на открытом, в лесу — только ближе 100 м, за холмом — нет; стреляющий виден всем; «последний раз видели» записано", () =>
        {
            // сцена: лучники стороны 1 в (ax, 700), пехота стороны 2 в (bx, 700); paint — местность до расстановки; один шаг боя
            (Battle bt, Mover a, Mover b) Scene(double ax, double bx, Action<TerrainMap> paint = null)
            {
                var bt = new Battle(Open(2000, 1400), R, new EngineContext { Rng = new Mulberry32(21).Next });
                paint?.Invoke(bt.Geo.Map);
                var a = bt.Add(Templates.Get("archers").Make(1, "Лучники", 300, 1), ax, 700, 90);
                var b = bt.Add(Templates.Get("infantry").Make(2, "Пехота", 300, 2), bx, 700, 270);
                bt.BeginTurn(); bt.Step(); bt.EndTurn();
                return (bt, a, b);
            }
            var far = Scene(300, 1500);   // 1200 м
            True(!far.bt.Sees(1, far.b) && far.bt.Sees(2, far.b) && far.bt.Sees(1, far.a), "за 1200 м виден или свой не виден");
            var open = Scene(300, 1100);   // 800 м
            True(open.bt.Sees(1, open.b) && open.bt.LastSeen.TryGetValue((1, 2), out var ls) && Math.Abs(ls.x - 1100) < 1, "за 800 м на открытом не виден");
            Action<TerrainMap> forest = m => Terrain.PaintRect(m, "t", 100, 130, 130, 150, Terrain.Id("forest"));   // лес x 500…655, y 650…755
            var inForest = Scene(300, 580, forest);   // 280 м, цель в лесу
            True(!inForest.bt.Sees(1, inForest.b), "в лесу за 280 м виден");
            var nearForest = Scene(500, 580, forest);   // 80 м
            True(nearForest.bt.Sees(1, nearForest.b), "в лесу за 80 м не виден");
            var hill = Scene(300, 1000, m => Terrain.PaintRect(m, "z", 150, 120, 152, 160, 3));   // холм x 750…765 высотой 3 уровня между ними
            True(!hill.bt.Sees(1, hill.b), "за холмом виден");
            var sc = open.bt.SeenCells(1); int W = open.bt.Geo.Map.W;
            True(sc != null && sc[140 * W + 100] && !sc[140 * W + 300] && sc.Count(x => x) > 1000, $"сетка видимости: в 200 м {sc?[140 * W + 100]}, в 1200 м {sc?[140 * W + 300]}, видимых клеток {sc?.Count(x => x)}");
            // стреляющий виден всем: стрелки стороны 2 за 1100 м бьют по лучникам стороны 1 — и становятся видны
            var shoot = Scene(300, 1500);
            var c = shoot.bt.Add(Templates.Get("archers").Make(3, "Стрелки", 300, 2), 1400, 700, 270);
            shoot.bt.Order(c, new MoveOrder { Kind = OrderKind.Attack, TargetId = 1 });
            shoot.bt.Turn(); shoot.bt.Turn();
            True(shoot.bt.Sees(1, c) || c.LastActT > 0, $"стреляющий не виден: стрелял в {c.LastActT:0.0} с");
        });

        yield return ("засада (Г107): отряд в лесу на «держать» невидим врагу в 110 м; атака по приказу из невидимости с ударом в 90 с — цели −15 БД и строка «засада»; тот же удар на виду — БД та же", () =>
        {
            (double morale, bool ambush, bool seenBefore) Run(bool forest)
            {
                var bt = new Battle(Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(22).Next });
                if (forest) Terrain.PaintRect(bt.Geo.Map, "t", 80, 80, 120, 120, Terrain.Id("forest"));   // лес x 400…605, y 400…605
                var T = Templates.Get("infantry");
                var hid = bt.Add(T.Make(1, "Засада", 500, 1), 500, 480, 0);   // 80 м в глубь леса
                var ub = T.Make(2, "Колонна", 500, 2); var b = bt.Add(ub, 500, 370, 180);   // в 110 м, лицом на юг (к лесу): в лесу видно только ближе 100 м
                bt.Order(hid, new MoveOrder { Kind = OrderKind.Hold });
                bt.BeginTurn(); bt.Step(); bt.EndTurn();
                bool seenBefore = bt.Sees(2, hid);
                bt.Order(hid, Attack(2));
                bool ambush = false; double drop = 0;
                for (int t = 0; t < 6 && !ambush; t++) { double was = ub.Morale; ambush = bt.Turn().Any(l => l.Contains("засада")); drop = was - ub.Morale; }
                return (drop, ambush, seenBefore);
            }
            var f = Run(true); var o = Run(false);
            True(!f.seenBefore && o.seenBefore, $"виден до удара: в лесу {f.seenBefore}, в поле {o.seenBefore}");
            True(f.ambush && !o.ambush, $"засада: в лесу {f.ambush}, в поле {o.ambush}");
            True(f.morale >= R.Fog.AmbushMorale, $"БД цели за ход засады упала на {f.morale:0}");
        });

        yield return ("сплотить (Г72, Алекс 10.10.2026): цель сплотилась после бегства — преследователь с приказом «атаковать» не стоит на месте, а снова идёт на неё и сходится", () =>
        {
            var (bt, a, b) = Breaking("infantry", "knights", 30, 1, 7);   // рыцари с БД 30 бегут от пехоты и уходят далеко — быстрее
            for (int i = 0; i < 3 && !b.Fleeing; i++) bt.Turn();
            True(b.Fleeing, "рыцари не побежали");
            bt.Turn();
            b.P.U.Discipline = 100;
            bt.Order(b, new MoveOrder { Kind = OrderKind.Rally });
            for (int i = 0; i < 4 && b.Fleeing; i++) bt.Turn();
            True(!b.Fleeing && b.P.U.Status == "active", $"не сплотились: {b.P.U.Status}");
            True(a.Order != null && a.Order.Kind == OrderKind.Attack && a.Order.TargetId == 2, "у пехоты пропал приказ «атаковать»");
            double d0 = JsMath.Hypot(a.P.X - b.P.X, a.P.Y - b.P.Y), dMin = d0; bool touched = false;
            for (int i = 0; i < 6 && !touched; i++)
            {
                bt.Turn();
                dMin = Math.Min(dMin, JsMath.Hypot(a.P.X - b.P.X, a.P.Y - b.P.Y));
                touched = bt.Fights.Any(f => !f.Over && f.Touching && (f.A == a || f.B == a));
            }
            True(touched || dMin < Math.Max(10, d0 - 100), $"преследователь застыл: было {d0:0} м, ближе всего {dMin:0} м, схватки нет; заметка «{a.Note}», Done {a.Done}");
        });

        yield return ("сплотить (Г72): провал броска — бежит дальше, приказ израсходован", () =>
        {
            var (bt, a, b) = Breaking("infantry", "infantry", 30, 1, 6, menA: 300);
            bt.Turn();
            bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
            bt.Order(b, new MoveOrder { Kind = OrderKind.Rally });
            var logs = new List<string>();
            for (int i = 0; i < 4 && b.RallyPending; i++) logs.AddRange(bt.Turn());
            True(b.Fleeing && !b.RallyPending && logs.Any(l => l.Contains("сплотить не вышло")), string.Join(" | ", logs));
        });

        yield return ("конец хода (стол): сломленный теряет дисциплину, иссякла — бежит без броска", () =>
        {
            var bt = new Battle(Open(), R, new EngineContext { Rng = new Mulberry32(2).Next });
            var u = Templates.Get("infantry").Make(1, "Сломленные", 1000, 1);
            u.Morale = 0; u.Broken = true; u.BreakGrace = 0; u.Discipline = 5;
            var m = bt.Add(u, 500, 500, 0);
            bt.Add(Templates.Get("infantry").Make(2, "Враг", 1000, 2), 500, 300, 180);
            var log = bt.Turn();
            True(u.Status == "fled" && m.Fleeing, $"«Сломленные» {u.Status}, дисциплина {u.Discipline}: " + string.Join(" | ", log));
            True(log.Any(l => l.Contains("дисциплина иссякла")), string.Join(" | ", log));
            double y0 = m.P.Y;
            bt.Turn();
            True(m.P.Y > y0 + 80, $"бегут прочь от врага: {y0:0} → {m.P.Y:0}");
        });

        yield return ("бегство: одно зерно — один исход", () =>
        {
            double[] Run()
            {
                var (bt, a, b) = Breaking("knights", "infantry", 30, 1, 5);
                for (int i = 0; i < 3; i++) bt.Turn();
                return new[] { a.P.U.Soldiers, b.P.U.Soldiers, b.P.X, b.P.Y }.Concat(b.Figs.SelectMany(f => new[] { f.X, f.Y })).ToArray();
            }
            True(Run().SequenceEqual(Run()), "повтор совпал");
        });

        yield return ("бой: одно зерно — один исход", () =>
        {
            double[] Run()
            {
                var (bt, a, b) = Duel("knights", "infantry", 120, 21);
                bt.Order(a, Attack(2, charge: true));
                for (int i = 0; i < 2; i++) bt.Turn();
                return new[] { a.P.U.Soldiers, b.P.U.Soldiers }.Concat(a.Figs.SelectMany(f => new[] { f.X, f.Y, f.Wrap ? 1 : 0 })).ToArray();
            }
            True(Run().SequenceEqual(Run()), "повтор совпал");
        });
    }
}
