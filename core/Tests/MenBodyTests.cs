// Б1 (Г82, Г85, Г89, Г92): бойцы — тела, фигурка — колонна за якорем. Тот же набор проверок, что у фигурок-капсул:
// марш на норму, очередь своих, стена о стену с врагом, стрелки сквозь своих, переправа по мосту, бой сходится
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

static class MenBodyTests
{
    public static readonly Rules RB = Rules.Base;   // с 03.10.2026 бойцы-тела — умолчание (Г92); старый режим — Rules.Figures
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

    // Г87: стычка с заданным k человек в теле, без проверок БД (сравниваем механику, не бегство)
    static (Battle bt, Mover a, Mover b) DuelK(string ta, string tb, double dist, uint seed, int k, double w = 1000, double h = 1000)
    {
        var bt = new Battle(MoveTests.Open(w, h), RB, new EngineContext { Rng = new Mulberry32(seed).Next }) { BodyK = k, MoraleChecks = false };
        var TA = Templates.Get(ta); var TB = Templates.Get(tb);
        var b = bt.Add(TB.Make(2, "B", 1000, 2), w / 2, h * 0.57, 0);
        var fa = Formation.Of(TA.Make(1, "A", 1000, 1), RB);
        var a = bt.Add(TA.Make(1, "A", 1000, 1), w / 2, h * 0.57 - (b.P.Fp.Depth / 2 + dist + fa.Depth / 2), 180);
        return (bt, a, b);
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

        // ── Г94 (Алекс): бойцы разворачиваются, а не едут задом ──
        // отступление (Г81) на battle: курс тела каждого бойца по шагам, сколько шли задом
        (double maxBack, double maxTurn, double turnedBack, double faceEnd) Retreat(string tpl)
        {
            var bt = new Battle(MoveTests.Open(800, 800), RB, new EngineContext { Rng = new Mulberry32(5).Next });
            var t = Templates.Get(tpl);
            var m = bt.Add(t.Make(1, t.Name, 300, 1), 400, 300, 180);   // смотрит вниз (+y), отступает вверх
            bt.Order(m, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });   // точки нет — прямо назад (Г81)
            var was = m.Men.ToDictionary(x => x, x => x.Facing);
            var win = m.Men.ToDictionary(x => x, x => (x.X, x.Y, x.Facing));
            double maxBack = 0, maxTurn = 0; int turned = 0, samples = 0;
            int frame = 0; const int W = 10;   // ход назад — смещением за W шагов (0,5 с): толчки соседей в толпе — толкотня, не ход
            for (int turn = 0; turn < 2; turn++)
                bt.Turn(_ =>
                {
                    frame++;
                    foreach (var x in m.Men)
                    {
                        if (!x.Alive) continue;
                        if (was.TryGetValue(x, out var f0)) maxTurn = Math.Max(maxTurn, Math.Abs(MoveSim.AngleDiff(f0, x.Facing)));
                        was[x] = x.Facing;
                        samples++;
                        if (Math.Abs(MoveSim.AngleDiff(x.Facing, 0)) < 45) turned++;   // смотрит туда, куда отступают
                        if (frame % W != 0) continue;
                        var w = win[x];
                        // первые полсекунды тела оседают после расстановки; окно, где курс повернулся больше 30°, — доворот, а не ход задом
                        if (frame > W && Math.Abs(MoveSim.AngleDiff(w.Facing, x.Facing)) < 30)
                        {
                            double a0 = w.Facing * Math.PI / 180, a1 = x.Facing * Math.PI / 180;
                            double fx = Math.Sin(a0) + Math.Sin(a1), fy = -Math.Cos(a0) - Math.Cos(a1), fl = Math.Max(1e-9, Math.Sqrt(fx * fx + fy * fy));
                            maxBack = Math.Max(maxBack, -((x.X - w.X) * fx + (x.Y - w.Y) * fy) / fl / (W * RB.Move.Dt));
                        }
                        win[x] = (x.X, x.Y, x.Facing);
                    }
                });
            // в конце: доля бойцов, что смотрят по строю (до 3% коней после отступления стоят в чужом ряду лицом к своему месту — Б4)
            double faceEnd = m.Men.Where(x => x.Alive).Count(x => Math.Abs(MoveSim.AngleDiff(x.Facing, 180)) >= 30) * 100.0 / m.Men.Count(x => x.Alive);
            return (maxBack, maxTurn, turned / (double)samples, faceEnd);
        }

        yield return ("Г94: конница не едет задом — разворачивается, отъезжает и на месте снова смотрит на врага", () =>
        {
            var (back, turn, turnedBack, faceEnd) = Retreat("knights");
            // назад при устойчивом курсе — не быстрее шага: на ходу предел 0,3 м/с, в стоящей колонне шаг назад до StandBackMps плюс
            // толчки соседей в сжатой колонне; ловим ход задом (были 5–50 м/с), не шаг
            True(back <= Math.Max(RB.Men.HorseBackMps, RB.Men.StandBackMps) + 1.0, $"конь пятился со скоростью {back:0.00} м/с (за 0,5 с, курс устойчив)");
            True(turnedBack > 0.3, $"развернулись по ходу в {turnedBack:P0} кадров");
            True(turn <= RB.Men.HorseTurnDegPerSec * RB.Move.Dt + 1e-6, $"курс повернулся за шаг на {turn:0.0}°");
            True(faceEnd <= 5, $"на месте не по строю {faceEnd:0.0}% бойцов");
        });

        yield return ("Г94 (Г81): пехота при отступлении пятится лицом к врагу, курс не прыгает", () =>
        {
            var (back, turn, turnedBack, faceEnd) = Retreat("infantry");
            True(turnedBack < 0.05, $"повернулись спиной к врагу в {turnedBack:P0} кадров");
            True(back > 0.5, $"пятились со скоростью {back:0.00} м/с");
            True(turn <= RB.Men.FootTurnDegPerSec * RB.Move.Dt + 1e-6, $"курс повернулся за шаг на {turn:0.0}°");
            True(faceEnd <= 5, $"на месте не по строю {faceEnd:0.0}% бойцов");
        });

        yield return ("Г94: в бою — курс тела не прыгает; кони вне схватки задом не ходят (охват рыцарей)", () =>
        {
            var (bt, a, b) = Duel(RB, "knights", "infantry", 301);
            var was = new Dictionary<Man, double>();
            var win = new Dictionary<Man, (double X, double Y, double F, bool Foe, bool Near, bool Fight)>();
            double worstTurn = 0; int frame = 0, backN = 0, horseN = 0; const int W = 10;
            bt.Turn(_ =>
            {
                frame++;
                // враги рядом (клетки 3 м): давку у врага (толкают пехотинцы на выпадах) — в натиск телами (Г90, Б3)
                var near = new HashSet<(int, int)>();
                if (frame % W == 0) foreach (var y in b.Men) if (y.Alive) near.Add(((int)Math.Floor(y.X / 3), (int)Math.Floor(y.Y / 3)));
                bool Close(Man x) { int cx = (int)Math.Floor(x.X / 3), cy = (int)Math.Floor(x.Y / 3); for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) if (near.Contains((cx + i, cy + j))) return true; return false; }
                foreach (var m in new[] { a, b })
                {
                    bool horse = BattleMap.IsHorse(m.P.U);
                    double lim = (horse ? RB.Men.HorseTurnDegPerSec : RB.Men.FootTurnDegPerSec) * RB.Move.Dt;
                    foreach (var x in m.Men)
                    {
                        if (!x.Alive) continue;
                        if (was.TryGetValue(x, out var f0)) worstTurn = Math.Max(worstTurn, Math.Abs(MoveSim.AngleDiff(f0, x.Facing)) / lim);
                        was[x] = x.Facing;
                        if (!horse || frame % W != 0) continue;
                        // конь без противника за 0,5 с ушёл назад быстрее 2 м/с — пятился (толкотня в давке — не в счёт: она туда-сюда)
                        // вне схватки: ни сам, ни его колонна не бьётся, врага ближе 3 м нет (давка — в натиск телами, Г90)
                        if (win.TryGetValue(x, out var w) && !w.Foe && x.Foe == null && !w.Near && !Close(x) && !w.Fight && !x.Fig.Fighting)
                        {
                            double a0 = w.F * Math.PI / 180;
                            double back = -((x.X - w.X) * Math.Sin(a0) - (x.Y - w.Y) * Math.Cos(a0)) / (W * RB.Move.Dt);
                            horseN++; if (back > 2.0) backN++;   // метр назад за полсекунды — уже ход; меньше — толчок соседа
                        }
                        win[x] = (x.X, x.Y, x.Facing, x.Foe != null, Close(x), x.Fig.Fighting);
                    }
                }
            });
            True(worstTurn <= 1 + 1e-6, $"курс повернулся быстрее предела в {worstTurn:0.00} раза");
            True(horseN > 1000 && backN <= 0.01 * horseN, $"кони вне схватки пятились быстрее 2 м/с в {backN} окнах по 0,5 с из {horseN}");
        });

        // ── Г90: натиск телами ──
        // рыцарей уже, чем пехоты (огибать нечего): натиск с разбега dist м; глубина центра переднего коня за начальным краем
        (Battle bt, Mover a, Mover b, double deep, int knocked, int maxKnocks, int stillDown) Charge(string tb, double dist, uint seed)
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(seed).Next });
            var TA = Templates.Get("knights"); var TB = Templates.Get(tb);
            var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 150, 1), RB);
            var a = bt.Add(TA.Make(1, TA.Name, 150, 1), 500, 500 - (b.P.Fp.Depth / 2 + dist + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            double front = b.Men.Where(x => x.Alive).Min(x => x.Y), F = b.P.Fp.Front / 2, deep = double.MinValue;
            int maxKnocks = 0;
            bt.Turn(_ =>
            {
                foreach (var h in a.Men)
                {
                    if (!h.Alive || Math.Abs(h.X - 500) > F - 3) continue;
                    deep = Math.Max(deep, h.Y - front); maxKnocks = Math.Max(maxKnocks, h.Knocks);
                }
            });
            return (bt, a, b, deep, b.Men.Count(x => !double.IsNaN(x.DownAt)), maxKnocks, b.Men.Count(x => x.Alive && x.DownLeft > 0 && x.DownAt < bt.Clock - RB.Men.DownSecMax - 0.1));
        }

        yield return ("Г90: натиск телами — передний ряд коней вламывается на 1–2 шеренги, сбивает вставших на пути; сбитые встают; убивает только стол", () =>
        {
            var (bt, a, b, deep, knocked, maxKnocks, stillDown) = Charge("infantry", 150, 5);
            True(knocked >= 5, $"сбито с ног {knocked}");
            True(maxKnocks <= RB.Men.ChargeKnocks, $"конь сбил {maxKnocks}");
            // центр коня — на полкорпуса (1,4 м) за мордой: морда в 1–2 шеренгах (1 м) — центр от −0,5 до 1,5 м за начальным краем
            True(deep > -0.5 && deep < 1.5, $"центр коня за начальным краем пехоты на {deep:0.0} м");
            True(stillDown == 0, $"не встали дольше {RB.Men.DownSecMax} с: {stillDown}");
            var st = bt.MenMelee;
            double lost = 1150 - a.P.U.Soldiers - b.P.U.Soldiers;
            True(Math.Abs(bt.Deaths.Count(d => d.UnitId == 1 || d.UnitId == 2) - lost) <= 2, $"павших записано {bt.Deaths.Count}, выбыло {lost:0} — натиск сам не убивает");
        });

        yield return ("Г90: без разбега натиска нет — кони не ломятся, никого не сбивают", () =>
        {
            var (_, _, _, deep, knocked, _, _) = Charge("infantry", 30, 6);
            True(knocked == 0, $"сбито с ног {knocked}");
        });

        yield return ("Г90: на пики во фронт натиска нет — кони встают у острия, никого не сбивают", () =>
        {
            var (_, _, _, deep, knocked, _, _) = Charge("pikemen", 150, 7);
            True(knocked == 0, $"сбито пикинёров {knocked}");
            True(deep < -RB.Men.PikeTipM + 1, $"центр коня за начальным краем пикинёров на {deep:0.0} м — острия на {RB.Men.PikeTipM} м впереди");
        });

        // ── старт волной и бегство вразброс (Г84) ──
        yield return ("старт волной: колонна трогается с переднего ряда, задние — следом; через 3 с идут все", () =>
        {
            var geo = MoveTests.Open(800, 800);
            var m = Unit("infantry", 1, 400, 700, 0);
            Order(m, geo, 400, 200, 0);
            double vFront = 0, vBack = 0, vAll = 0; int k = 0;
            MoveSim.Turn(new[] { m }, geo, RB, _ =>
            {
                k++;
                double t = k * RB.Move.Dt;
                if (Math.Abs(t - 0.4) < 1e-6)
                {
                    vFront = m.Men.Where(x => x.Row == 0).Average(x => JsMath.Hypot(x.Vx, x.Vy));
                    vBack = m.Men.Where(x => x.Row >= 6).Average(x => JsMath.Hypot(x.Vx, x.Vy));
                }
                if (Math.Abs(t - 3) < 1e-6) vAll = m.Men.Min(x => JsMath.Hypot(x.Vx, x.Vy));
            });
            True(vFront > 1 && vBack < vFront * 0.5, $"на 0,4 с: передний ряд {vFront:0.0} м/с, задние {vBack:0.0}");
            True(vAll > 1, $"на 3 с самый медленный боец {vAll:0.0} м/с");
            double norm = BattleMap.UnitSpeed(m.P.U, RB);
            True(Math.Abs(m.Spent - norm) < 1.5, $"нормы {m.Spent:0.0} из {norm}");
        });

        yield return ("Г84: бегство рассыпается за 2–4 с — первыми бегут задние ряды, передние ещё держат строй; потом толпа бежит вся", () =>
        {
            var bt = new Battle(MoveTests.Open(1200, 1600), RB, new EngineContext { Rng = new Mulberry32(6).Next });
            var ub = Templates.Get("infantry").Make(2, "Пехота", 1000, 2); ub.Morale = 30; ub.Discipline = 1;
            var b = bt.Add(ub, 600, 400, 0);
            var fa = Formation.Of(Templates.Get("infantry").Make(1, "Враг", 300, 1), RB);
            var a = bt.Add(Templates.Get("infantry").Make(1, "Враг", 300, 1), 600, 400 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            double t0 = -1, backAt1 = 0, frontAt1 = 0, slowestAt5 = 0;
            bt.Turn(_ =>
            {
                if (!b.Fleeing) return;
                if (t0 < 0) t0 = bt.StepTime;
                double since = bt.StepTime - t0;
                var live = b.Men.Where(x => x.Alive).ToList();
                // через 1,5 с: задние уже разогнались (разгон колонны — около 2 с), передние (срок не раньше 1,9 с) ещё стоят
                if (Math.Abs(since - 1.5) < 0.026) { backAt1 = live.Where(x => x.Row >= 6).Average(x => JsMath.Hypot(x.Vx, x.Vy)); frontAt1 = live.Where(x => x.Row == 0).Average(x => JsMath.Hypot(x.Vx, x.Vy)); }
                if (Math.Abs(since - 5) < 0.026) slowestAt5 = live.Average(x => JsMath.Hypot(x.Vx, x.Vy));
            });
            True(t0 >= 0 && t0 < 9, $"побежали на {t0:0.0} с");
            True(backAt1 > 2 && frontAt1 < backAt1 * 0.5, $"через 1,5 с после бегства: задние {backAt1:0.0} м/с, передние {frontAt1:0.0}");
            True(slowestAt5 > 4, $"через 5 с толпа бежит в среднем {slowestAt5:0.0} м/с");
        });

        // ── Г86 (Б4): колонна вдали от врага — одним телом ──
        yield return ("Г86: на марше вдали от всех бойцы идут одним телом — жёсткие почти все, норма та же; у своих, у врага и под стрелами — поштучно", () =>
        {
            var geo = MoveTests.Open(1200, 1200);
            var m = Unit("infantry", 1, 600, 1100, 0);
            Order(m, geo, 600, 100, 0);
            int rigidAt2 = -1, alive = m.Men.Count, k = 0;
            MoveSim.Turn(new[] { m }, geo, RB, _ => { if (++k == 60) rigidAt2 = MenBodies.RigidMen; });   // на 3 с: после старта волной задние ряды догоняют около 2 с
            True(rigidAt2 >= 0.95 * alive, $"на марше жёстких {rigidAt2} из {alive}");
            True(Math.Abs(m.Spent - BattleMap.UnitSpeed(m.P.U, RB)) < 1.5, $"нормы {m.Spent:0.0} из {BattleMap.UnitSpeed(m.P.U, RB)}");
            // свои встречным курсом: вдали — жёсткие, рядом — поштучно
            var a = Unit("infantry", 1, 300, 400, 90, men: 400); var b = Unit("infantry", 2, 700, 400, 270, men: 400);
            Order(a, geo, 750, 400, 90); Order(b, geo, 250, 400, 270);
            int minRigid = int.MaxValue, maxRigid = 0; k = 0;
            for (int t = 0; t < 3; t++) MoveSim.Turn(new[] { a, b }, geo, RB, _ => { if (++k % 20 == 0) { minRigid = Math.Min(minRigid, MenBodies.RigidMen); maxRigid = Math.Max(maxRigid, MenBodies.RigidMen); } });
            True(maxRigid > 500 && minRigid == 0, $"встречные свои: жёстких от {minRigid} до {maxRigid}");
            // под стрелами со 100 м (враг дальше 60 м) — цель поштучно
            var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(5).Next });
            var inf = Templates.Get("infantry"); var arc = Templates.Get("archers");
            var tgt = bt.Add(inf.Make(2, "Цель", 1000, 2), 500, 500, 0);
            var sh = bt.Add(arc.Make(1, "Лучники", 1000, 1), 500, 500 - (tgt.P.Fp.Depth / 2 + 100 + 10), 180);
            bt.Order(sh, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            int tgtRigid = 0; k = 0;
            bt.Turn(_ => { if (++k >= 100) tgtRigid += tgt.Men.Count(x => x.WasRigid); });
            True(bt.Volleys.Count > 0, "лучники стреляют");
            True(tgtRigid == 0, $"цель под стрелами — жёстких (сумма по шагам) {tgtRigid}");
        });

        yield return ("Б3 (Г92): тесты боя фигурками — с бойцами-телами проходят все, кроме удара пехоты во фланг пехоте", () =>
        {
            // пехота во фланг: колонны атакующего огибают врага дольше фигурок (касание бойцов, а не на 5 м) — за ход атакованный
            // теряет 166 при пороге 70% стола = 178
            var gaps = new[] { "охват (Г68, Г63): пехота во фланг" };
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

        // ── Г87: укрупнение — одно тело = k человек одного ряда в глубину ──
        yield return ("Г87: укрупнение — тела по рядам своего файла, людей и строй те же (конница при k = 4: 4 + 1, пехота 4 + 4, стрелки при k = 2: 2 + 2 + 1)", () =>
        {
            foreach (var (tpl, k, bodies, sizes) in new[] { ("knights", 4, 400, "1×200 4×200"), ("infantry", 4, 250, "4×250"), ("archers", 2, 600, "1×200 2×400") })
            {
                var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(1).Next }) { BodyK = k };
                var T = Templates.Get(tpl);
                var b = bt.Add(T.Make(2, "B", 1000, 2), 500, 500, 0);
                bt.BeginTurn();
                var f = RB.Map.Formation.TryGetValue(b.P.U.Type, out var ff) ? ff : RB.Map.Formation["infantry"];
                True(b.Men.Count == bodies && b.Men.Sum(x => x.Men) == 1000, $"{tpl}: тел {b.Men.Count}, людей {b.Men.Sum(x => x.Men)}");
                var got = string.Join(" ", b.Men.GroupBy(x => x.Men).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}"));
                True(got == sizes, $"{tpl}: тела по размеру {got}, надо {sizes}");
                var rows = new Dictionary<int, int>();   // люди тел по рядам — как места бойцов (Formation.MenPositions)
                foreach (var man in b.Men)
                    for (int i = 0; i < man.Men; i++)
                    {
                        int band = (int)Math.Floor((man.Y - 500 + (i - (man.Men - 1) / 2.0) * f.RankDepth + b.P.Fp.Depth / 2) / f.RankDepth);
                        rows[band] = rows.TryGetValue(band, out var c) ? c + 1 : 1;
                    }
                var want = Formation.MenPositions(b.P.U, RB).GroupBy(q => q.rank).ToDictionary(g => g.Key, g => g.Count());
                True(rows.Count == want.Count && want.All(kv => rows.TryGetValue(kv.Key, out var c) && c == kv.Value),
                     $"{tpl}: людей по рядам {string.Join(",", rows.OrderBy(q => q.Key).Select(q => q.Key + ":" + q.Value))}, надо {string.Join(",", want.OrderBy(q => q.Key).Select(q => q.Key + ":" + q.Value))}");
            }
        });

        yield return ("Г87: укрупнение — потери стола те же (±10%): пехота в упор и лучники со 100 м при k = 4 как при k = 1; тело падает, когда выбыл последний его человек", () =>
        {
            (double melee, double shot, int deaths, int lost) Run(int k)
            {
                double melee = 0, shot = 0; int deaths = 0, lost = 0;
                for (uint s = 1; s <= 6; s++)
                {
                    var (bt, a, b) = DuelK("infantry", "infantry", 0.5, s + 7000, k);
                    bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
                    bt.Turn(); bt.Turn();
                    melee += (1000 - b.P.U.Soldiers) / 6.0;
                    deaths += bt.Deaths.Count(d => d.UnitId == 2); lost += 1000 - (int)Math.Round(b.P.U.Soldiers);
                    True(b.Men.Where(x => x.Alive).All(x => x.Lost < x.Men) && b.Men.Where(x => !x.Alive).All(x => x.Lost >= x.Men), "тело живо, пока выбыли не все его люди");
                    var (bt2, a2, b2) = DuelK("archers", "infantry", 100, s + 9000, k, 2000, 1400);
                    bt2.Order(a2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
                    bt2.Turn(); bt2.Order(a2, new MoveOrder { Kind = OrderKind.Hold }); bt2.Turn();
                    shot += (1000 - b2.P.U.Soldiers) / 6.0;
                }
                return (melee, shot, deaths, lost);
            }
            var r1 = Run(1); var r4 = Run(4);
            True(Math.Abs(r4.melee / r1.melee - 1) <= 0.1, $"рукопашная: k=1 {r1.melee:0}, k=4 {r4.melee:0}");
            True(Math.Abs(r4.shot / r1.shot - 1) <= 0.1, $"стрелы: k=1 {r1.shot:0}, k=4 {r4.shot:0}");
            True(Math.Abs(r4.deaths - r4.lost) <= 6, $"павших записано {r4.deaths}, выбыло {r4.lost}");
        });

        yield return ("Г87: k — по численности на поле: 50 000 человек при пороге 40 000 — k = 2 и тел вдвое меньше; 10 000 — k = 1", () =>
        {
            var T = Templates.Get("infantry");
            var bt = new Battle(MoveTests.Open(7000, 3000), RB, new EngineContext { Rng = new Mulberry32(1).Next });
            bt.Add(T.Make(1, "A", 25000, 1), 3500, 1200, 180); bt.Add(T.Make(2, "B", 25000, 2), 3500, 1800, 0);
            bt.BeginTurn();
            True(bt.BodyK == 2 && bt.Movers.Sum(m => m.Men.Count) == 25000 && bt.Movers.Sum(m => m.Men.Sum(x => x.Men)) == 50000, $"k = {bt.BodyK}, тел {bt.Movers.Sum(m => m.Men.Count)}");
            var small = new Battle(MoveTests.Open(2000, 1000), RB, new EngineContext { Rng = new Mulberry32(1).Next });
            small.Add(T.Make(1, "A", 5000, 1), 1000, 300, 180); small.Add(T.Make(2, "B", 5000, 2), 1000, 700, 0);
            small.BeginTurn();
            True(small.BodyK == 1 && small.Movers.Sum(m => m.Men.Count) == 10000, $"k = {small.BodyK}, тел {small.Movers.Sum(m => m.Men.Count)}");
        });

        // ── Б5: лес — деревья как тела ──
        yield return ("Б5: лес — деревья как тела: пехота идёт сквозь полосу леса в 80 м — в лесу строй рассыпается (дальше 0,8 м от мест, в поле 0,2), в стволах не стоит, за лесом смыкается и приходит; одно зерно — один исход", () =>
        {
            (double inForest, double final, int deep, int checks, bool done, string hash) Run()
            {
                var geo = MoveTests.Open(600, 900);
                Terrain.PaintRect(geo.Map, "t", 0, 70, 119, 85, Terrain.Id("forest"));   // полоса леса 350…430 м по y
                var T = Templates.Get("infantry");
                var m = Mover.Place(T.Make(1, T.Name, 400, 1), 300, 600, 0, RB);
                MoveSim.Give(m, new MoveOrder { X = 300, Y = 150, Facing = 0 }, geo, RB);
                var ms = new[] { m }; int forestId = Terrain.Id("forest");
                double sumF = 0; int nF = 0, deep = 0, checks = 0; var xs = new double[16]; var ys = new double[16];
                for (int t = 0; t < 10 && !m.Done; t++)
                    MoveSim.Turn(ms, geo, RB, tt =>
                    {
                        foreach (var man in m.Men)
                        {
                            int cx = (int)(man.X / Terrain.CellM), cy = (int)(man.Y / Terrain.CellM), cell = cy * geo.Map.W + cx;
                            if (geo.Map.T[cell] != forestId) continue;
                            var hm = Soldiers.HomeOf(m, man); sumF += JsMath.Hypot(hm.x - man.X, hm.y - man.Y); nF++;
                            int k = Terrain.Trees(geo.Map, cell, RB, xs, ys);
                            for (int i = 0; i < k; i++) { checks++; if (JsMath.Hypot(xs[i] - man.X, ys[i] - man.Y) < RB.Men.TreeRadiusM + 0.45 - 0.3) deep++; }
                        }
                    });
                double fin = m.Men.Average(x => { var hm = Soldiers.HomeOf(m, x); return JsMath.Hypot(hm.x - x.X, hm.y - x.Y); });
                string h = string.Join(";", m.Men.Take(5).Select(x => $"{x.X:0.000},{x.Y:0.000}"));
                return (sumF / Math.Max(1, nF), fin, deep, checks, m.Done, h);
            }
            var r1 = Run(); var r2 = Run();
            True(r1.done, "не дошёл за 10 ходов");
            True(r1.inForest > 0.8, $"в лесу строй не рассыпался: до мест в среднем {r1.inForest:0.00} м (в поле 0,2)");
            True(r1.final < 1.0, $"пришёл, не сомкнувшись: до мест в среднем {r1.final:0.00} м");
            True(r1.deep * 1000 < r1.checks, $"в стволах глубже 0,3 м: {r1.deep} из {r1.checks}");
            True(r1.hash == r2.hash, "одно зерно — разный исход");
        });

        yield return ("Б5: дома — отряд, поставленный на дом, сдвигается на ближайшее место, где строй помещается (в доме никого); марш сквозь дом 10 × 10 м — никто не входит в дом, строй делится, смыкается и приходит", () =>
        {
            int bId = Terrain.Id("building");
            var geo = MoveTests.Open(600, 900);
            Terrain.PaintRect(geo.Map, "t", 59, 78, 60, 79, bId);   // дом: метры 295…305 × 390…400
            var T = Templates.Get("infantry");
            int Inside(Mover u) => u.Men.Count(x => x.Alive && geo.Map.T[(int)(x.Y / Terrain.CellM) * geo.Map.W + (int)(x.X / Terrain.CellM)] == bId);
            var bt = new Battle(geo, RB, new EngineContext { Rng = new Mulberry32(1).Next });
            var a = bt.Add(T.Make(1, "На доме", 400, 1), 300, 395, 0);
            True(Inside(a) == 0 && JsMath.Hypot(a.P.X - 300, a.P.Y - 395) > 1 && JsMath.Hypot(a.P.X - 300, a.P.Y - 395) < 30, $"в доме {Inside(a)}, сдвинут на {JsMath.Hypot(a.P.X - 300, a.P.Y - 395):0.0} м");
            True(bt.Fits(a.P.U, a.P.X, a.P.Y, a.P.Facing), "после сдвига строй всё ещё на непроходимом");
            bt.Turn();
            True(Inside(a) == 0, $"через ход в доме {Inside(a)}");
            var bt2 = new Battle(geo, RB, new EngineContext { Rng = new Mulberry32(2).Next });
            var m = bt2.Add(T.Make(1, "Сквозь дом", 400, 1), 300, 600, 0);
            bt2.Order(m, new MoveOrder { X = 300, Y = 150, Facing = 0 });
            int inside = 0; double maxOff = 0; int turns = 0;
            for (int t = 0; t < 8 && !m.Done; t++) { turns++; bt2.Turn(tt => { inside += Inside(m); foreach (var x in m.Men) { var hm = Soldiers.HomeOf(m, x); maxOff = Math.Max(maxOff, JsMath.Hypot(hm.x - x.X, hm.y - x.Y)); } }); }
            double fin = m.Men.Average(x => { var hm = Soldiers.HomeOf(m, x); return JsMath.Hypot(hm.x - x.X, hm.y - x.Y); });
            True(m.Done && turns <= 6, $"не дошёл за {turns} ходов");
            True(inside == 0, $"бойцов в клетках дома за марш: {inside}");
            True(maxOff > 3 && fin < 1.0, $"у дома разошлись до {maxOff:0.0} м, в конце до мест {fin:0.00} м");
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
    public static void Overlaps(string[] opts)
    {
        var RB = MenBodyTests.RB;
        foreach (var o in opts)
        {
            if (o == "off") { RB.Men.FarEnemyM = double.MaxValue; RB.Men.FarFriendM = double.MaxValue; }   // Г86 выключен: все поштучно
            else if (o.StartsWith("j")) RB.Men.Jitter = double.Parse(o.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
            else if (o.StartsWith("f")) RB.Men.FarFriendM = double.Parse(o.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
        }
        Console.WriteLine($"  правила: Jitter {RB.Men.Jitter}, FarFriendM {RB.Men.FarFriendM}, FarEnemyM {RB.Men.FarEnemyM}");
        var geo = MoveTests.Open(1000, 800);
        Mover U(int id, double x, double f) { var t = Templates.Get("infantry"); return Mover.Place(t.Make(id, t.Name, 400, 1), x, 400, f, RB); }
        var a = U(1, 300, 90); var b = U(2, 700, 270);
        MoveSim.Give(a, new MoveOrder { X = 750, Y = 400, Facing = 90 }, geo, RB); MoveSim.Give(b, new MoveOrder { X = 250, Y = 400, Facing = 270 }, geo, RB);
        var ms = new[] { a, b };
        double worst = 0; string info = ""; double clock = 0; int step = 0, bad = 0, badPairs = 0; int late = 0;
        for (int t = 0; t < 6; t++)
            MoveSim.Turn(ms, geo, RB, tt =>
            {
                step++; bool any = false;
                if (step % 100 == 0) Console.WriteLine($"  шаг {step}: жёстких {MenBodies.RigidMen}, отрядов вдали {MenBodies.RigidUnits}; центры A {a.P.X:0} B {b.P.X:0}; перекрытий > 0,5 м пока {bad}");
                foreach (var x in a.Men) foreach (var y in b.Men)
                {
                    if (!x.Alive || !y.Alive) continue;
                    double d = JsMath.Hypot(x.X - y.X, x.Y - y.Y); if (d > 1) continue;
                    double pen = 0.9 - d;
                    if (pen > 0.5) { any = true; badPairs++; }
                    if (step >= 1700 && pen > 0.5 && late++ < 6) { var hx0 = Soldiers.HomeOf(a, x); var hy0 = Soldiers.HomeOf(b, y); Console.WriteLine($"  поздняя пара, шаг {step}: A№{x.Id} колонна {x.Fig.Id} в ({x.X:0.0},{x.Y:0.0}), место ({hx0.x:0.0},{hx0.y:0.0}), якорь ({x.Fig.AX:0.0},{x.Fig.AY:0.0}), колонна в m.Figs {a.Figs.Contains(x.Fig)}; B№{y.Id} колонна {y.Fig.Id} в ({y.X:0.0},{y.Y:0.0}), место ({hy0.x:0.0},{hy0.y:0.0}), якорь ({y.Fig.AX:0.0},{y.Fig.AY:0.0}), в m.Figs {b.Figs.Contains(y.Fig)}"); }
                    if (pen > worst) { worst = pen; info = $"шаг {step} ({clock + tt:0.0} с): A№{x.Id} ({x.X:0.0},{x.Y:0.0}) v={JsMath.Hypot(x.Vx, x.Vy):0.0} reseat={x.Reseat} via={!double.IsNaN(x.ViaX)}; B№{y.Id} ({y.X:0.0},{y.Y:0.0}) v={JsMath.Hypot(y.Vx, y.Vy):0.0}; A held={a.Held} B held={b.Held}; жёстких бойцов {MenBodies.RigidMen}, отрядов вдали {MenBodies.RigidUnits}; центры A {a.P.X:0},{a.P.Y:0} B {b.P.X:0},{b.P.Y:0}"; }

                }
                if (any) bad++;
            });
        Console.WriteLine($"наибольшее {worst:0.00} м: {info}; шагов с перекрытием > 0,5 м: {bad} из {step}, пар {badPairs}");
    }
}
