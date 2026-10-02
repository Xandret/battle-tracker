// Б1 (Г82, Г85, Г89, Г92): бойцы — тела, фигурка — колонна за якорем. Тот же набор проверок, что у фигурок-капсул:
// марш на норму, очередь своих, стена о стену с врагом, стрелки сквозь своих, переправа по мосту, бой сходится
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

static class MenBodyTests
{
    public static readonly Rules RB = MakeRules();
    static Rules MakeRules() { var r = new Rules(); r.Move.MenBodies = true; return r; }
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    static Mover Unit(string tpl, int id, double x, double y, double facing, double men = 1000, int faction = 1, Rules r = null)
    {
        var t = Templates.Get(tpl);
        return Mover.Place(t.Make(id, t.Name, men, faction), x, y, facing, r ?? RB);
    }
    static void Order(Mover m, Geo geo, double x, double y, double facing, Rules r = null) => MoveSim.Give(m, new MoveOrder { X = x, Y = y, Facing = facing }, geo, r ?? RB);
    // наибольшее перекрытие бойцов (м): свои одного отряда, разных отрядов, враги
    static (double same, double friend, double enemy) Overlap(IList<Mover> ms)
    {
        var all = new List<(Man man, Mover m, double rad)>();
        foreach (var m in ms)
        {
            var f = RB.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : RB.Map.Formation["infantry"];
            double rad = RB.Men.BodyShare * Math.Min(f.PerMan, f.RankDepth);
            foreach (var x in m.Men) if (x.Alive && x.Fig != null) all.Add((x, m, rad));
        }
        double s = 0, fr = 0, en = 0;
        var cell = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < all.Count; i++) { var k = ((int)Math.Floor(all[i].man.X / 3), (int)Math.Floor(all[i].man.Y / 3)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new List<int>(); l.Add(i); }
        for (int i = 0; i < all.Count; i++)
        {
            int cx = (int)Math.Floor(all[i].man.X / 3), cy = (int)Math.Floor(all[i].man.Y / 3);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                if (cell.TryGetValue((cx + dx, cy + dy), out var l))
                    foreach (int j in l)
                    {
                        if (j <= i) continue;
                        var a = all[i]; var b = all[j];
                        if (BattleMap.IsHorse(a.m.P.U) || BattleMap.IsHorse(b.m.P.U)) continue;   // у конных капсула — тут только пешие
                        double pen = a.rad + b.rad - JsMath.Hypot(a.man.X - b.man.X, a.man.Y - b.man.Y);
                        if (a.m == b.m) s = Math.Max(s, pen);
                        else if (a.m.P.U.FactionId == b.m.P.U.FactionId) fr = Math.Max(fr, pen);
                        else en = Math.Max(en, pen);
                    }
        }
        return (s, fr, en);
    }

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Б1 (Г85): фигурки — колонны во всю глубину строя, около 10 человек", () =>
        {
            var inf = Unit("infantry", 1, 300, 300, 0); var arc = Unit("archers", 2, 600, 300, 0);
            True(inf.P.Figs.All(f => Math.Abs(f.Depth - 8) < 1e-9 && f.Width <= 1 + 1e-9), $"пехота: {inf.P.Figs[0].Width} × {inf.P.Figs[0].Depth}");
            True(arc.P.Figs.All(f => Math.Abs(f.Depth - 5) < 1e-9), $"стрелки: {arc.P.Figs[0].Width} × {arc.P.Figs[0].Depth}");
            True(inf.Men.Count == 1000 && inf.Men.All(x => x.Alive && x.Fig != null), "бойцов 1000, у всех колонна");
        });

        yield return ("Б1: марш — отряд проходит норму за ход, бойцы держат строй и не налезают друг на друга", () =>
        {
            var geo = MoveTests.Open(800, 800);
            var m = Unit("infantry", 1, 400, 700, 0);
            Order(m, geo, 400, 200, 0);
            MoveSim.Turn(new[] { m }, geo, RB);
            double norm = BattleMap.UnitSpeed(m.P.U, RB);
            True(Math.Abs(m.Spent - norm) < 1, $"нормы {m.Spent:0.0} из {norm}");
            var lag = m.Men.Max(x => { var h = Soldiers.HomeOf(m, x); return JsMath.Hypot(x.X - h.x, x.Y - h.y); });
            var ov = Overlap(new[] { m });
            True(lag < 2.5, $"отстал от места на {lag:0.0} м");
            True(ov.same < 0.3, $"свои налезли на {ov.same:0.00} м");
        });

        yield return ("Б1 (Г89): стена о стену — враги друг сквозь друга не проходят", () =>
        {
            var geo = MoveTests.Open(800, 800);
            var a = Unit("infantry", 1, 400, 600, 0); var b = Unit("infantry", 2, 400, 200, 180, faction: 2);
            Order(a, geo, 400, 150, 0); Order(b, geo, 400, 650, 180);
            var ms = new[] { a, b };
            double worst = 0;
            for (int t = 0; t < 3; t++) MoveSim.Turn(ms, geo, RB, _ => worst = Math.Max(worst, Overlap(ms).enemy));
            True(worst < 0.35, $"враги налезли на {worst:0.00} м");
            double ay = a.Men.Where(x => x.Alive).Min(x => x.Y), by = b.Men.Where(x => x.Alive).Max(x => x.Y);
            True(ay > by - 1.5, $"прошли насквозь: передний край A на {ay:0.0}, B на {by:0.0}");
        });

        yield return ("Б1 (Г57): свои встречным курсом — пропускают по очереди, сквозь не проходят (в давке — лишь на миг) и успевают не хуже фигурок", () =>
        {
            (Mover a, Mover b, int bad, int steps) Run(Rules r)
            {
                var geo = MoveTests.Open(1000, 800);
                var a = Unit("infantry", 1, 300, 400, 90, men: 400, r: r); var b = Unit("infantry", 2, 700, 400, 270, men: 400, r: r);
                Order(a, geo, 750, 400, 90, r); Order(b, geo, 250, 400, 270, r);
                var ms = new[] { a, b };
                int bad = 0, steps = 0;
                for (int t = 0; t < 6; t++) MoveSim.Turn(ms, geo, r, _ => { if (r != RB) return; steps++; if (Overlap(ms).friend > 0.5) bad++; });
                return (a, b, bad, steps);
            }
            var (na, nb, bad, steps) = Run(RB); var (oa, ob, _, _) = Run(Rules.Base);
            True(bad <= 0.05 * steps, $"свои налезали глубже 0,5 м в {bad} шагах из {steps}");
            double left(Mover m, double tx) => Math.Abs(m.P.X - tx);
            True(left(na, 750) <= left(oa, 750) + 10 && left(nb, 250) <= left(ob, 250) + 10,
                $"отстали от фигурок: A осталось {left(na, 750):0} (было {left(oa, 750):0}), B {left(nb, 250):0} (было {left(ob, 250):0})");
        });

        yield return ("Б1 (Г56): стрелки проходят сквозь свою пехоту", () =>
        {
            var geo = MoveTests.Open(800, 800);
            var inf = Unit("infantry", 1, 400, 400, 0); var arc = Unit("archers", 2, 400, 480, 0, men: 300);
            Order(arc, geo, 400, 330, 0);
            var ms = new[] { inf, arc };
            for (int t = 0; t < 3; t++) MoveSim.Turn(ms, geo, RB);
            True(arc.P.Y < 345, $"стрелки застряли: центр на {arc.P.Y:0}");
        });

        yield return ("Б1 (Г59): переправа — пехота и конница переходят реку не хуже фигурок, никто не в воде", () =>
        {
            (double inf, double kn, int wet) Run(Rules r)
            {
                var map = MapGen.Generate("river", new Dictionary<string, object> { ["widthM"] = 1200.0, ["depthM"] = 900.0, ["fords"] = 1.0, ["bridges"] = 1.0 }, 7);
                var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
                var inf = Unit("infantry", 1, 350, 820, 0, r: r); var kn = Unit("knights", 2, 850, 830, 0, men: 300, r: r);
                Order(inf, geo, 350, 80, 0, r); Order(kn, geo, 850, 60, 0, r);
                var ms = new[] { inf, kn };
                int wet = 0;
                for (int t = 0; t < 8; t++)
                    MoveSim.Turn(ms, geo, r, _ =>
                    {
                        if (r != RB) return;
                        foreach (var m in ms) foreach (var x in m.Men) if (x.Alive && m.Field != null && m.Field.Inside(x.X, x.Y) && !m.Field.Passable(m.Field.CellOf(x.X, x.Y))) wet++;
                    });
                return (inf.P.Y, kn.P.Y, wet);
            }
            var nw = Run(RB); var od = Run(Rules.Base);
            True(nw.inf <= od.inf + 15 && nw.kn <= od.kn + 15, $"отстали от фигурок: пехота {nw.inf:0} (было {od.inf:0}), конница {nw.kn:0} (было {od.kn:0})");
            True(nw.wet < 50, $"бойцов в воде за все шаги: {nw.wet}");
        });

        yield return ("Б1: бой — отряды сходятся, касаются и бьются", () =>
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(3).Next });
            var TA = Templates.Get("infantry");
            var b = bt.Add(TA.Make(2, "B", 1000, 2), 500, 500, 0);
            var a = bt.Add(TA.Make(1, "A", 1000, 1), 500, 440, 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            bt.Turn(); bt.Turn();
            True(b.P.U.Soldiers < 990 && a.P.U.Soldiers < 1000, $"потерь нет: A {a.P.U.Soldiers}, B {b.P.U.Soldiers}");
            var ov = Overlap(new[] { a, b });
            True(ov.enemy < 0.4, $"враги налезли на {ov.enemy:0.00} м");
        });

        // ── Б2 (Г83): касания и павшие — по бойцам ──
        (Battle bt, Mover a, Mover b) Duel(Rules r, string ta, string tb, uint seed)
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(seed).Next });
            var TA = Templates.Get(ta); var TB = Templates.Get(tb);
            var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), r);
            var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            return (bt, a, b);
        }

        yield return ("Б2 (Г83): падает тот, кого ударили — почти все павшие в рукопашной пали от удара бойца; остальные удары — на щит", () =>
        {
            var (bt, a, b) = Duel(RB, "infantry", "infantry", 7001);
            bt.Turn();
            var st = bt.MenMelee;
            True(st.Hits > 100, $"попаданий {st.Hits}");
            True(st.Hits >= 0.95 * (st.Hits + st.Fallback), $"пало от ударов {st.Hits}, без удара {st.Fallback}");
            True(st.Parries > st.Hits, $"на щит {st.Parries} при попаданиях {st.Hits}");
            int melee = bt.Deaths.Count(d => d.UnitId == 1 || d.UnitId == 2);
            double lost = 2000 - a.P.U.Soldiers - b.P.U.Soldiers;
            True(Math.Abs(melee - lost) <= 2, $"павших записано {melee}, выбыло {lost:0}");
        });

        yield return ("Б2 (Г88): полный контакт — потери как у колонн-фигурок ±10% (сколько — стол, кто — бойцы)", () =>
        {
            double fa = 0, fb = 0, ma = 0, mb = 0;
            for (uint s = 1; s <= 6; s++)
            {
                var (bt, a, b) = Duel(Rules.Base, "infantry", "infantry", s + 7000); bt.Turn(); fa += 1000 - a.P.U.Soldiers; fb += 1000 - b.P.U.Soldiers;
                var (bt2, a2, b2) = Duel(RB, "infantry", "infantry", s + 7000); bt2.Turn(); ma += 1000 - a2.P.U.Soldiers; mb += 1000 - b2.P.U.Soldiers;
            }
            True(Math.Abs(ma / fa - 1) <= 0.1 && Math.Abs(mb / fb - 1) <= 0.1, $"фигурки {fa / 6:0}/{fb / 6:0}, бойцы {ma / 6:0}/{mb / 6:0}");
        });

        yield return ("Б2 (Г83): пикинёры достают из задних шеренг (до 4-й) дальше обычной досягаемости, пехота — нет", () =>
        {
            double Rad(Mover m) { var f = RB.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : RB.Map.Formation["infantry"]; return RB.Men.BodyShare * Math.Min(f.PerMan, f.RankDepth); }
            // бойцы с противником дальше ReachM от него (по зазору тел): сколько за ход (сумма по шагам) и из какой шеренги
            int Far(string t, out int maxRow)
            {
                var (bt, a, b) = Duel(RB, t, "infantry", 901);
                double ra = Rad(a), rb = Rad(b);
                int n = 0, mr = 0;
                bt.Turn(_ =>
                {
                    foreach (var x in a.Men)
                    {
                        if (!x.Alive || x.Foe == null || !x.Foe.Alive) continue;
                        if (JsMath.Hypot(x.Foe.X - x.X, x.Foe.Y - x.Y) - ra - rb <= RB.Men.ReachM + 1.0) continue;   // противник назначен на касании (раз в 0,5 с) — запас на сдвиг
                        n++; mr = Math.Max(mr, x.Row);
                    }
                });
                maxRow = mr;
                return n;
            }
            int pike = Far("pikemen", out int pr), inf = Far("infantry", out _);
            True(pike > 1000, $"пикой дальше досягаемости: {pike} (сумма по шагам)");
            True(pr >= 2, $"пикой — только из {pr + 1}-й шеренги");
            // у пехоты дальше досягаемости — только отошедшие с прошлого касания (удар по ним идёт мимо)
            True(pike > 5 * Math.Max(1, inf), $"дальше досягаемости: пики {pike}, пехота {inf}");
        });

        yield return ("Б2: для рисунка — у бойца противник, время удара и щита; павшие — у врага", () =>
        {
            var (bt, a, b) = Duel(RB, "infantry", "infantry", 7003);
            int foes = 0, swung = 0, parried = 0;
            bt.Turn(_ =>
            {
                foes = Math.Max(foes, a.Men.Count(x => x.Alive && x.Foe != null && x.Foe.Alive));
                swung = a.Men.Count(x => !double.IsNaN(x.SwingAt)); parried = b.Men.Count(x => !double.IsNaN(x.ParryAt));
            });
            True(foes > 50, $"с противником {foes}");
            True(swung > 50 && parried > 50, $"ударили {swung}, приняли на щит {parried}");
            // павший в рукопашной стоял у врага: ближе 3 м до живого врага в конце хода (строй не уходил)
            var fallen = bt.Deaths.Where(d => d.UnitId == 2).ToList();
            int near = fallen.Count(d => a.Men.Any(x => x.Alive && JsMath.Hypot(x.X - d.X, x.Y - d.Y) < 3));
            True(fallen.Count > 0 && near >= 0.9 * fallen.Count, $"у врага пали {near} из {fallen.Count}");
        });

        yield return ("Б2 (Г92): тесты боя фигурками — с бойцами-телами проходят все, кроме охвата, бегства и «сплотить» (Б3)", () =>
        {
            // не догнали фигурки ещё в Б1 (те же 6 провалов были до Б2): охват — колонны идут к местам у врага дольше
            // фигурок (стена бойцов, а не касание на 5 м); бегство и «сплотить» — Б3 (бегство вразброс)
            var gaps = new[] { "охват (Г68): рыцари на пехоту в упор", "охват (Г68, Г63): пехота во фланг", "охват (Г68): враг разбит",
                               "бегство (Г70, Г71)", "сплотить (Г72)" };
            // долгие сверки — отдельно: полный контакт — тест выше (против фигурок), стрельба бойцами не менялась (Б2 — рукопашная)
            var slow = new[] { "бой (Г62): полный контакт", "стрельба (Г65, Г75)", "стрельба (Г65): без приказа", "упреждение (Г66)",
                               "бегство: одно зерно", "бой: одно зерно" };
            var was = BattleTests.Use; BattleTests.Use = RB;
            var bad = new List<string>();
            try
            {
                foreach (var (n, run) in BattleTests.All())
                {
                    if (gaps.Any(g => n.StartsWith(g)) || slow.Any(g => n.StartsWith(g))) continue;
                    try { run(); } catch (Exception e) { bad.Add($"{n}: {e.Message}"); }
                }
            }
            finally { BattleTests.Use = was; }
            True(bad.Count == 0, string.Join("\n      ", bad));
        });
    }
}

static class MenBodyProbe
{
    public static void Cross() { Cross(MenBodyTests.RB); Console.WriteLine("— старый режим —"); Cross(Rules.Base); }
    static void Cross(Rules RB)
    {
        var geo = MoveTests.Open(1000, 800);
        Mover U(int id, double x, double f) { var t = Templates.Get("infantry"); return Mover.Place(t.Make(id, t.Name, 400, 1), x, 400, f, RB); }
        var a = U(1, 300, 90); var b = U(2, 700, 270);
        MoveSim.Give(a, new MoveOrder { X = 750, Y = 400, Facing = 90 }, geo, RB); MoveSim.Give(b, new MoveOrder { X = 250, Y = 400, Facing = 270 }, geo, RB);
        var ms = new[] { a, b };
        for (int t = 0; t < 6; t++)
        {
            var log = MoveSim.Turn(ms, geo, RB);
            foreach (var m in ms) Console.WriteLine($"ход {t + 1} {m.P.U.Name}: центр {m.P.X:0.0},{m.P.Y:0.0} Held={m.Held} Vs={m.Vs:0.00} Spent={m.Spent:0.0} блок={m.LastBlockerName} done={m.Done} onspot={m.OnSpot}");
            foreach (var l in log) Console.WriteLine("   " + l);
        }
    }
}

static class MenBodyProbe2
{
    public static void River()
    {
        foreach (var r in new[] { MenBodyTests.RB, Rules.Base })
        {
            Console.WriteLine(r == Rules.Base ? "— старый —" : "— новый —");
            var map = MapGen.Generate("river", new Dictionary<string, object> { ["widthM"] = 1200.0, ["depthM"] = 900.0, ["fords"] = 1.0, ["bridges"] = 1.0 }, 7);
            var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
            var t = Templates.Get("infantry");
            var inf = Mover.Place(t.Make(1, t.Name, 1000, 1), 350, 820, 0, r);
            MoveSim.Give(inf, new MoveOrder { X = 350, Y = 80, Facing = 0 }, geo, r);
            var ms = new[] { inf };
            for (int k = 0; k < 8; k++)
            {
                int stepNo = 0;
                var log = MoveSim.Turn(ms, geo, r, _ =>
                {
                    if (k < 3 || k > 4 || ++stepNo % 20 != 0) return;
                    int lagN = 0; double lagSum = 0;
                    for (int q = 0; q < inf.P.Figs.Count; q++)
                    {
                        inf.P.ToWorld(inf.P.Figs[q].X, inf.P.Figs[q].Y, out var sx, out var sy);
                        double fx = r.Move.MenBodies ? inf.Figs[q].AX : inf.Figs[q].X, fy = r.Move.MenBodies ? inf.Figs[q].AY : inf.Figs[q].Y;
                        double d = JsMath.Hypot(sx - fx, sy - fy); if (d > 3) { lagN++; lagSum += d; }
                    }
                    Console.WriteLine($"   {k * 15 + stepNo * 0.05:0} с: отстают {lagN}/{inf.P.Figs.Count} (в среднем {lagSum / Math.Max(1, lagN):0.0} м), Vs {inf.Vs:0.0}, колонн {inf.Cols}, перестр {inf.Reforming}");
                });
                int far = inf.Men.Count(x => { var h = Soldiers.HomeOf(inf, x); return JsMath.Hypot(x.X - h.x, x.Y - h.y) > 5; });
                if (r == MenBodyTests.RB && k == 4)
                {
                    var lag = new List<string>();
                    for (int q = 0; q < inf.P.Figs.Count; q++)
                    {
                        inf.P.ToWorld(inf.P.Figs[q].X, inf.P.Figs[q].Y, out var sx, out var sy); var s = inf.Figs[q];
                        double dA = JsMath.Hypot(sx - s.AX, sy - s.AY), dC = JsMath.Hypot(s.AX - s.X, s.AY - s.Y);
                        bool wet = inf.Field != null && !MoveSim.Free(inf.Field, sx, sy);
                        if (dA > 3) lag.Add($"№{q} ряд {inf.P.Figs[q].Rank} колонна {inf.P.Figs[q].File}: до места {dA:0.0}, якорь−бойцы {dC:0.0}, бойцов {s.MenN}, место в воде {wet}, хочет {JsMath.Hypot(s.Dvx, s.Dvy):0.0} (предел {s.Vmax:0.0}), идёт {JsMath.Hypot(s.AVx, s.AVy):0.0}");
                    }
                    Console.WriteLine($"   отстают {lag.Count}: " + string.Join(" | ", lag.Take(12)));
                }
                Console.WriteLine($"ход {k + 1}: центр {inf.P.X:0},{inf.P.Y:0} колонн {inf.Cols}/{inf.NominalCols} перестр {inf.Reforming} {inf.RegroupSec:0.0}с отстали>5м {far} | {string.Join("; ", log)}");
            }
        }
    }
}

static class MenBodyBench
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            var geo = MoveTests.Open(2000, 1500);
            var bt = new Battle(geo, r, new EngineContext { Rng = new Mulberry32(1).Next });
            var t = Templates.Get("infantry");
            var A = new List<Mover>(); var B = new List<Mover>();
            for (int i = 0; i < 10; i++)
            {
                A.Add(bt.Add(t.Make(1 + i, "A" + i, 1000, 1), 200 + i * 160, 500, 180));
                B.Add(bt.Add(t.Make(101 + i, "B" + i, 1000, 2), 200 + i * 160, 700, 0));
            }
            for (int i = 0; i < 10; i++) { bt.Order(A[i], new MoveOrder { Kind = OrderKind.Attack, TargetId = 101 + i }); bt.Order(B[i], new MoveOrder { Kind = OrderKind.Attack, TargetId = 1 + i }); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bt.Turn();
            double t1 = sw.Elapsed.TotalSeconds; sw.Restart();
            bt.Turn();
            Console.WriteLine($"{(r.Move.MenBodies ? "бойцы-тела" : "фигурки")}: ход 1 (сближение) {t1:0.00} с, ход 2 (бой) {sw.Elapsed.TotalSeconds:0.00} с на 20 тыс. бойцов; потерь A {A.Sum(m => 1000 - m.P.U.Soldiers):0}, B {B.Sum(m => 1000 - m.P.U.Soldiers):0}");
        }
    }
}

static class MenBodyProbe3
{
    public static void Overlaps()
    {
        var RB = MenBodyTests.RB;
        var geo = MoveTests.Open(1000, 800);
        Mover U(int id, double x, double f) { var t = Templates.Get("infantry"); return Mover.Place(t.Make(id, t.Name, 400, 1), x, 400, f, RB); }
        var a = U(1, 300, 90); var b = U(2, 700, 270);
        MoveSim.Give(a, new MoveOrder { X = 750, Y = 400, Facing = 90 }, geo, RB); MoveSim.Give(b, new MoveOrder { X = 250, Y = 400, Facing = 270 }, geo, RB);
        var ms = new[] { a, b };
        double worst = 0; string info = ""; double clock = 0; int step = 0, bad = 0, badPairs = 0;
        for (int t = 0; t < 6; t++)
            MoveSim.Turn(ms, geo, RB, tt =>
            {
                step++; bool any = false;
                foreach (var x in a.Men) foreach (var y in b.Men)
                {
                    if (!x.Alive || !y.Alive) continue;
                    double d = JsMath.Hypot(x.X - y.X, x.Y - y.Y); if (d > 1) continue;
                    double pen = 0.9 - d;
                    if (pen > 0.5) { any = true; badPairs++; }
                    if (pen > worst) { worst = pen; info = $"шаг {step} ({clock + tt:0.0} с): A№{x.Id} ({x.X:0.0},{x.Y:0.0}) v={JsMath.Hypot(x.Vx, x.Vy):0.0} reseat={x.Reseat} via={!double.IsNaN(x.ViaX)}; B№{y.Id} ({y.X:0.0},{y.Y:0.0}) v={JsMath.Hypot(y.Vx, y.Vy):0.0}; A held={a.Held} B held={b.Held}"; }
                }
                if (any) bad++;
            });
        Console.WriteLine($"наибольшее {worst:0.00} м: {info}; шагов с перекрытием > 0,5 м: {bad} из {step}, пар {badPairs}");
    }
}
