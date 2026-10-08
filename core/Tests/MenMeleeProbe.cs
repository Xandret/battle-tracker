// Б2: зонд рукопашной по бойцам (dotnet run --project Tests -- b2) — потери против колонн-фигурок и стола, счёт ударов
using BattleCore;

static class MenMeleeProbe
{
    static (Battle bt, Mover a, Mover b) Duel(Rules r, string ta, string tb, double dist, uint seed, double ox = 0, double face = 180)
    {
        var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(seed).Next });
        var TA = Templates.Get(ta); var TB = Templates.Get(tb);
        var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), r);
        var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500 + ox, 500 - (b.P.Fp.Depth / 2 + dist + fa.Depth / 2), face);
        return (bt, a, b);
    }

    public static void Run(string only = null)
    {
        var RB = MenBodyTests.RB;
        void Case(string title, int turns, int seeds, Func<Rules, uint, (Battle bt, Mover a, Mover b)> make, Action<Battle, Mover, Mover> order)
        {
            if (only != null && !title.Contains(only)) return;
            foreach (var r in new[] { Rules.Base, RB })
            {
                double la = 0, lb = 0, flank = 0; var st = new Battle.MenMeleeStats(); int strikersBack = 0;
                for (uint s = 1; s <= seeds; s++)
                {
                    var (bt, a, b) = make(r, s);
                    order(bt, a, b);
                    for (int t = 0; t < turns; t++)
                        bt.Turn(_ =>
                        {
                            foreach (var f in bt.Fights) flank = Math.Max(flank, f.Of(a).Flank + f.Of(a).Rear);
                            if (r == RB) strikersBack += a.Men.Count(x => x.Alive && x.Foe != null && x.Row > 0);
                        });
                    la += 1000 - a.P.U.Soldiers; lb += 1000 - b.P.U.Soldiers;
                    st.Swings += bt.MenMelee.Swings; st.Hits += bt.MenMelee.Hits; st.Parries += bt.MenMelee.Parries; st.Fallback += bt.MenMelee.Fallback;
                }
                string extra = r == RB ? $" · ударов {st.Swings / seeds}, попаданий {st.Hits / seeds}, на щит {st.Parries / seeds}, пало без удара {st.Fallback / seeds}, бьющих из задних шеренг (сумма по шагам) {strikersBack / seeds}" : "";
                Console.WriteLine($"{title} · {(r == RB ? "бойцы (Б2)" : "фигурки")}: потери A {la / seeds:0}, B {lb / seeds:0}; фланг+тыл до {flank:0.00}{extra}");
            }
        }
        Case("пехота на пехоту, в упор, 1 ход", 1, 12, (r, s) => Duel(r, "infantry", "infantry", 0.5, s + 7000), (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }));
        Case("пехота на ополчение, в упор, 1 ход", 1, 12, (r, s) => Duel(r, "infantry", "militia", 0.5, s + 7000), (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }));
        Case("рыцари натиском с 150 м, 2 хода", 2, 8, (r, s) => Duel(r, "knights", "infantry", 150, s + 5), (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true }));
        Case("рыцари на пехоту в упор (охват), 2 хода", 2, 8, (r, s) => Duel(r, "knights", "infantry", 0.5, s + 300), (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }));
        Case("пикинёры на пехоту, в упор, 1 ход", 1, 8, (r, s) => Duel(r, "pikemen", "infantry", 0.5, s + 900), (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }));
        Case("пехота во фланг пехоте, 2 хода", 2, 8, (r, s) =>
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(s + 40).Next });
            var inf = Templates.Get("infantry");
            var b = bt.Add(inf.Make(2, "Стоят", 1000, 2), 500, 500, 0);
            var a = bt.Add(inf.Make(1, "Во фланг", 1000, 1), 500 - 62.5 - 4 - 40, 500, 90);
            return (bt, a, b);
        }, (bt, a, b) => bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }));
    }
}

static class MenMeleeFlank
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(41).Next });
            var inf = Templates.Get("infantry");
            var b = bt.Add(inf.Make(2, "Стоят", 1000, 2), 500, 500, 0);
            var fa0 = Formation.Of(inf.Make(1, "A", 1000, 1), r);
            var a = bt.Add(inf.Make(1, "Во фланг", 1000, 1), 500 - (b.P.Fp.Front / 2 + 0.5 + fa0.Depth / 2), 500, 90);   // как тест: в упор к флангу
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            Console.WriteLine(r.Move.MenBodies ? "— бойцы —" : "— фигурки —");
            for (int t = 0; t < 1; t++)
            {
                int k = 0;
                var log = bt.Turn(_ =>
                {
                    if (++k % 30 != 0) return;
                    var f = bt.Fights.FirstOrDefault();
                    double gap = double.MaxValue;
                    foreach (var x in a.Men) if (x.Alive) foreach (var y in b.Men) if (y.Alive && Math.Abs(x.X - y.X) < 6 && Math.Abs(x.Y - y.Y) < 6) gap = Math.Min(gap, JsMath.Hypot(x.X - y.X, x.Y - y.Y));
                    int foes = a.Men.Count(x => x.Alive && x.Foe != null), wrap = a.Figs.Count(s => s.Wrap);
                    string Loc(double wx, double wy) { b.P.ToLocal(wx, wy, out var lx, out var ly); return $"{lx:0.0},{ly:0.0}"; }
                    if (r.Move.MenBodies && k == 300)
                    {
                        var idle = a.Figs.Where(q => !q.Fighting).ToList();
                        Console.WriteLine($"   не бьются {idle.Count}: в охвате {idle.Count(q => q.Wrap)}, второй линией {idle.Count(q => q.Wrap && q.WBehind > 0)}, у места (<1,5 м) {idle.Count(q => q.Wrap && q.GoalM < 1.5)}, в пути {idle.Count(q => q.Wrap && q.GoalM >= 1.5)}, без места {idle.Count(q => !q.Wrap)}");
                        foreach (var q in idle.Where(q => q.Wrap && q.GoalM >= 1.5).Take(8))
                        {
                            b.P.ToLocal(q.AX, q.AY, out var lx, out var ly);
                            double g3 = b.Men.Where(y => y.Alive).Min(y => JsMath.Hypot(y.X - q.X, y.Y - q.Y));
                            Console.WriteLine($"     в пути {q.Id}: якорь у B {lx:0.0},{ly:0.0} → место {q.WSlotX:0.0},{q.WSlotY:0.0}, до точки {q.GoalM:0.0} м (точка у B {Loc(q.WX, q.WY)}); якорь идёт {JsMath.Hypot(q.AVx, q.AVy):0.0} (хочет {JsMath.Hypot(q.Dvx, q.Dvy):0.0}, предел {q.Vmax:0.0}); бойцы от опоры {JsMath.Hypot(q.RefX - q.AX, q.RefY - q.AY):0.0}; до врага {g3:0.0}; упёрлась {q.BlockedBy}");
                        }
                        foreach (var q in idle.Where(q => q.Wrap && q.GoalM < 1.5).Take(0))
                        {
                            b.P.ToLocal(q.X, q.Y, out var lx, out var ly);
                            double g2 = b.Men.Where(y => y.Alive).Min(y => JsMath.Hypot(y.X - q.X, y.Y - q.Y));
                            Console.WriteLine($"     у места, но не бьётся {q.Id}: место {q.WSlotX:0.0},{q.WSlotY:0.0}, середина бойцов у B {lx:0.0},{ly:0.0}, бойцов {q.MenN}, до ближнего врага (центр) {g2:0.0}, курс {q.Hd:0} (нужно {q.WH:0}), Turned {q.Turned}");
                        }
                    }
                    if (r.Move.MenBodies && k == 240 && false)
                        foreach (var s in a.Figs.Where(q => q.Wrap && !q.Fighting).Take(12))
                        {
                            b.P.ToLocal(s.AX, s.AY, out var lx, out var ly);
                            Console.WriteLine($"     охват {s.Id}: якорь у B {lx:0.0},{ly:0.0} → место {s.WSlotX:0.0},{s.WSlotY:0.0}; до точки пути {s.GoalM:0.0} м; якорь идёт {JsMath.Hypot(s.AVx, s.AVy):0.0}, бойцы {JsMath.Hypot(s.Vx, s.Vy):0.0}, хочет {JsMath.Hypot(s.Dvx, s.Dvy):0.0}; бойцы от опоры {JsMath.Hypot(s.RefX - s.AX, s.RefY - s.AY):0.0}; упёрлась в {s.BlockedBy}");
                        }
                    if (r.Move.MenBodies && (k == 120 || k == 270) && false)
                        foreach (var s in a.Figs.Where(q => q.Wrap).Take(6))
                            Console.WriteLine($"     колонна {s.Id}: якорь {s.AX:0.0},{s.AY:0.0} → цель {s.WX:0.0},{s.WY:0.0} место {s.WSlotX:0.0},{s.WSlotY:0.0}; бойцы {s.X:0.0},{s.Y:0.0} (живых {s.MenN}); хочет {JsMath.Hypot(s.Dvx, s.Dvy):0.00} предел {s.Vmax:0.00}; упёрлась в {s.BlockedBy} бьётся {s.Fighting}");
                    Console.WriteLine($"  {t * 15 + k * r.Move.Dt:0.0} с: A центр {a.P.X:0.0},{a.P.Y:0.0} Held {a.Held} done {a.Done} · бойцов A с противником {foes}, колонн в охвате {wrap}/{a.Figs.Count} · мин. между центрами {(gap > 1e9 ? -1 : gap):0.00} · Spent {a.Spent:0} · схватка {(f == null ? "-" : $"касаются {f.Touching}, A в деле {f.Of(a).Engaged:0.00} фланг {f.Of(a).Flank:0.00}")} · потери A {1000 - a.P.U.Soldiers:0} B {1000 - b.P.U.Soldiers:0}");
                });
            }
        }
    }
}

static class MenMeleeKnights
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(301).Next });
            var TA = Templates.Get("knights"); var TB = Templates.Get("infantry");
            var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), r);
            var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            Console.WriteLine((r.Move.MenBodies ? "— бойцы —" : "— фигурки —") + $" рыцари: фронт {a.P.Fp.Front:0.0} глубина {a.P.Fp.Depth:0.0}, колонн {a.Figs.Count}; пехота фронт {b.P.Fp.Front:0.0}");
            for (int t = 0; t < 2; t++)
            {
                int k = 0;
                bt.Turn(_ =>
                {
                    if (++k % 30 != 0) return;
                    var f = bt.Fights.FirstOrDefault();
                    int foesA = a.Men.Count(x => x.Alive && x.Foe != null), foesB = b.Men.Count(x => x.Alive && x.Foe != null), wrap = a.Figs.Count(s => s.Wrap), fight = a.Figs.Count(s => s.Fighting);
                    if (r.Move.MenBodies && (k == 120) && t == 0)
                    {
                        foreach (var s in a.Figs.Where(q => q.Wrap && !q.Fighting && JsMath.Hypot(q.WX - q.AX, q.WY - q.AY) > 100))
                        {
                            var F = a.Field;
                            var mm = a.Men.Where(x => x.Alive && x.Fig == s).ToList();
                            Console.WriteLine($"     !! колонна {s.Id}: бойцов {mm.Count}, якорь {s.AX:0.0},{s.AY:0.0}, цель {s.WX:0.0},{s.WY:0.0}, карта {(F == null ? "нет" : $"{F.W}x{F.H}")}, via у {mm.Count(x => !double.IsNaN(x.ViaX))}; якорь идёт {s.AVx:0.0},{s.AVy:0.0} хочет {s.Dvx:0.0},{s.Dvy:0.0}; бойцы {s.X:0.0},{s.Y:0.0} опора якоря {s.RefX:0.0},{s.RefY:0.0} ({s.RefN}); рамка {a.P.X:0.0},{a.P.Y:0.0} курс {a.P.Facing:0}; путь от якоря к цели {(F == null ? 0 : F.SegmentCost(s.AX, s.AY, s.WX, s.WY)):0.0}");
                            foreach (var x in mm.Take(3))
                            {
                                var h = Soldiers.HomeOf(a, x);
                                Console.WriteLine($"        боец {x.Id}: {x.X:0.0},{x.Y:0.0} место {h.x:0.0},{h.y:0.0} via {x.ViaX:0.0},{x.ViaY:0.0}; путь к месту {(F == null ? 0 : F.SegmentCost(x.X, x.Y, h.x, h.y)):0.0}; Lx {x.Lx:0.0} Ly {x.Ly:0.0}");
                            }
                        }
                        foreach (var s in a.Figs.Where(q => q.Wrap && !q.Fighting))
                        {
                            b.P.ToLocal(s.X, s.Y, out var lx, out var ly);
                            double gap = b.Men.Where(y => y.Alive).Min(y => JsMath.Hypot(y.X - s.X, y.Y - s.Y));
                            Console.WriteLine($"     колонна {s.Id}: до цели якорь {JsMath.Hypot(s.WX - s.AX, s.WY - s.AY):0.0} м, бойцы от якоря {JsMath.Hypot(s.X - s.AX, s.Y - s.AY):0.0} м; место {s.WSlotX:0.0},{s.WSlotY:0.0} (за своим {s.WBehind}); бойцы у пехоты {lx:0.0},{ly:0.0}; до ближней пехоты {gap:0.0} м; упёрлась в {s.BlockedBy}; Vmax {s.Vmax:0.0} хочет {JsMath.Hypot(s.Dvx, s.Dvy):0.0} якорь идёт {JsMath.Hypot(s.AVx, s.AVy):0.0} бойцы идут {JsMath.Hypot(s.Vx, s.Vy):0.0} сквозь своих {s.Slowed} ресит {a.Men.Count(x => x.Fig == s && x.Reseat)} via {a.Men.Count(x => x.Fig == s && !double.IsNaN(x.ViaX))}");
                        }
                    }
                    Console.WriteLine($"  {t * 15 + k * r.Move.Dt:0.0} с: рыцарей с противником {foesA}, пехоты {foesB}; колонн в охвате {wrap}/{a.Figs.Count}, бьются {fight} · {(f == null ? "-" : $"A в деле {f.Of(a).Engaged:0.00} (фронт {f.Of(a).Front:0.00} фланг {f.Of(a).Flank:0.00} тыл {f.Of(a).Rear:0.00}), B в деле {f.Of(b).Engaged:0.00}")} · потери A {1000 - a.P.U.Soldiers:0} B {1000 - b.P.U.Soldiers:0}");
                });
            }
        }
    }
}

// Б2: смешиваются ли строи — сколько бойцов зашло за передний край врага (в его рамке) и как глубоко
static class MenMeleeMix
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            // как сцена «Облик: анимации» (пехота рубит ополчение): по 400, в упор
            var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(13).Next });
            var TA = Templates.Get("infantry"); var TB = Templates.Get("militia");
            var b = bt.Add(TB.Make(2, TB.Name, 400, 2), 500, 500, 0);
            var a = bt.Add(TA.Make(1, TA.Name, 400, 1), 500, 500 - (4 + 0.5 + 4), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            Console.WriteLine(r.Move.MenBodies ? "— бойцы —" : "— фигурки —");
            int k = 0;
            // передний край по живым бойцам: B смотрит вверх (−y), A — вниз; граница — середина между краями
            for (int t = 0; t < 2; t++)
                bt.Turn(_ =>
                {
                    if (++k % 25 != 0 && k != 190) return;
                    var am = a.Men.Where(x => x.Alive).ToList(); var bm = b.Men.Where(x => x.Alive).ToList();
                    // линия схватки — медиана y передних бойцов: A — самые южные 10% , B — самые северные 10%
                    double aFront = am.Select(x => x.Y).OrderByDescending(v => v).Take(Math.Max(1, am.Count / 10)).Average();
                    double bFront = bm.Select(x => x.Y).OrderBy(v => v).Take(Math.Max(1, bm.Count / 10)).Average();
                    double line = (aFront + bFront) / 2;
                    // зашли за линию: A южнее линии, B севернее — глубже 1 м
                    var aIn = am.Where(x => x.Y > line + 1).Select(x => x.Y - line).ToList();
                    var bIn = bm.Where(x => x.Y < line - 1).Select(x => line - x.Y).ToList();
                    // соседи-враги: у скольких бойцов A ближайший сосед (до 1,2 м) — враг, а враг стоит «за ним» по курсу A
                    int Inside(List<Man> mine, List<Man> foes)
                    {
                        int n = 0;
                        foreach (var x in mine)
                        {
                            double fx = Math.Sin(x.Facing * Math.PI / 180), fy = -Math.Cos(x.Facing * Math.PI / 180);
                            bool ahead = false, behind = false;
                            foreach (var e in foes)
                            {
                                double dx = e.X - x.X, dy = e.Y - x.Y;
                                if (dx * dx + dy * dy > 1.5 * 1.5) continue;
                                double along = dx * fx + dy * fy;
                                if (along > 0.3) ahead = true; else if (along < -0.3) behind = true;
                            }
                            if (ahead && behind) n++;
                        }
                        return n;
                    }
                    if (r.Move.MenBodies && (k == 190))
                    {
                        Console.WriteLine($"  карта {k * r.Move.Dt:0.0} с (x 488…512, y 489…505, клетка 0,5 м; a — пехота, b — ополчение, * — оба), ополчение {b.P.U.Status} БД {b.P.U.Morale:0}:");
                        for (double y = 489; y < 505; y += 0.5)
                        {
                            var sb = new System.Text.StringBuilder("   ");
                            for (double x = 488; x < 512; x += 0.5)
                            {
                                bool ha = am.Any(q => q.X >= x && q.X < x + 0.5 && q.Y >= y && q.Y < y + 0.5), hb = bm.Any(q => q.X >= x && q.X < x + 0.5 && q.Y >= y && q.Y < y + 0.5);
                                sb.Append(ha && hb ? '*' : ha ? 'a' : hb ? 'b' : '.');
                            }
                            Console.WriteLine(sb);
                        }
                    }
                    Console.WriteLine($"  {k * r.Move.Dt:0.0} с: внутри чужого строя (враг и впереди, и позади ближе 1,5 м): A {Inside(am, bm)}, B {Inside(bm, am)}");
                    if (k % 50 != 0) return;
                    Console.WriteLine($"  {k * r.Move.Dt:0.0} с: линия y {line:0.0}; A за линией глубже 1 м: {aIn.Count} (макс {(aIn.Count > 0 ? aIn.Max() : 0):0.0} м), B за линией: {bIn.Count} (макс {(bIn.Count > 0 ? bIn.Max() : 0):0.0} м); потери A {400 - a.P.U.Soldiers:0} B {400 - b.P.U.Soldiers:0}");
                });
        }
    }
}

// Б3: бегство бойцами — куда идут рамка, якоря колонн и бойцы (как тест «сплотить»)
static class MenFleeProbe
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            var bt = new Battle(MoveTests.Open(1200, 1600), r, new EngineContext { Rng = new Mulberry32(6).Next });
            var ub = Templates.Get("infantry").Make(2, "Пехота", 1000, 2); ub.Morale = 30; ub.Discipline = 1;
            var b = bt.Add(ub, 600, 400, 0);
            var fa = Formation.Of(Templates.Get("infantry").Make(1, "Враг", 300, 1), r);
            var a = bt.Add(Templates.Get("infantry").Make(1, "Враг", 300, 1), 600, 400 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            Console.WriteLine(r.Move.MenBodies ? "— бойцы —" : "— фигурки —");
            for (int t = 0; t < 5; t++)
            {
                if (t == 1) { bt.Order(a, new MoveOrder { Kind = OrderKind.Hold }); b.P.U.Discipline = 100; bt.Order(b, new MoveOrder { Kind = OrderKind.Rally }); }
                int k = 0;
                bt.Turn(_ =>
                {
                    if (++k % 60 != 0) return;
                    var men = b.Men.Where(x => x.Alive).ToList();
                    double mx = men.Count > 0 ? men.Average(x => x.X) : 0, my = men.Count > 0 ? men.Average(x => x.Y) : 0;
                    double fx = b.Figs.Count > 0 ? b.Figs.Average(s => s.X) : 0, fy = b.Figs.Count > 0 ? b.Figs.Average(s => s.Y) : 0;
                    double ax = b.Figs.Count > 0 ? b.Figs.Average(s => s.AX) : 0, ay = b.Figs.Count > 0 ? b.Figs.Average(s => s.AY) : 0;
                    double vm = men.Count > 0 ? men.Average(x => JsMath.Hypot(x.Vx, x.Vy)) : 0, dv = b.Figs.Count > 0 ? b.Figs.Average(s => JsMath.Hypot(s.Dvx, s.Dvy)) : 0;
                    if (r.Move.MenBodies && t == 3 && k == 300)
                        foreach (var s in b.Figs.OrderBy(q => q.Y).Take(5))
                            Console.WriteLine($"     ближняя к врагу колонна {s.Id}: середина {s.X:0.0},{s.Y:0.0} якорь {s.AX:0.0},{s.AY:0.0} бойцов {s.MenN}; FleeH {s.FleeH:0}; хочет {s.Dvx:0.0},{s.Dvy:0.0} (предел {s.Vmax:0.0}); идёт {s.AVx:0.0},{s.AVy:0.0}; упёрлась в {s.BlockedBy}");
                    if (r.Move.MenBodies && t == 2 && k == 300)
                    {
                        var stuck = men.Where(x => x.Y < 450).ToList();
                        Console.WriteLine($"   отставшие (y < 450): {stuck.Count}");
                        foreach (var g in stuck.GroupBy(x => x.Fig).Take(8))
                        {
                            var s = g.Key; var x0 = g.First();
                            var h = Soldiers.HomeOf(b, x0);
                            Console.WriteLine($"     колонна {s.Id}: бойцов здесь {g.Count()}, всего в ней {s.MenN}; якорь {s.AX:0.0},{s.AY:0.0}, середина {s.X:0.0},{s.Y:0.0}; FleeH {s.FleeH:0}; хочет {s.Dvx:0.0},{s.Dvy:0.0}; боец {x0.X:0.0},{x0.Y:0.0} v {x0.Vx:0.0},{x0.Vy:0.0} место {h.x:0.0},{h.y:0.0} Lx {x0.Lx:0.0} Ly {x0.Ly:0.0} via {x0.ViaX:0} reseat {x0.Reseat} foe {(x0.Foe != null)}");
                        }
                    }
                    Console.WriteLine($"  ход {t + 1}, {k * r.Move.Dt:0} с: бежит {b.Fleeing}, к {b.FleeX:0},{b.FleeY:0}; рамка {b.P.X:0},{b.P.Y:0}; колонны {fx:0},{fy:0}; якоря {ax:0},{ay:0}; бойцы {mx:0},{my:0} ({men.Count}, скорость {vm:0.0}, колонны хотят {dv:0.0}); зазор до врага {Bodies.MinGap(a, b):0}; сплотить ждёт {b.RallyPending}, статус {b.P.U.Status}; потери B {1000 - b.P.U.Soldiers:0}; прошёл {b.Moved:0}");
                });
            }
        }
    }
}

// Б3: после «сплотить» колонны идут к местам — какая застряла и почему (как тест «сплотить»)
static class MenRallyProbe
{
    public static void Run()
    {
        var R = MenBodyTests.RB;
        var bt = new Battle(MoveTests.Open(1200, 1600), R, new EngineContext { Rng = new Mulberry32(6).Next });
        var ub = Templates.Get("infantry").Make(2, "Пехота", 1000, 2); ub.Morale = 30; ub.Discipline = 1;
        var b = bt.Add(ub, 600, 400, 0);
        var fa = Formation.Of(Templates.Get("infantry").Make(1, "Враг", 300, 1), R);
        var a = bt.Add(Templates.Get("infantry").Make(1, "Враг", 300, 1), 600, 400 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        bt.Turn();
        bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
        b.P.U.Discipline = 100;
        bt.Order(b, new MoveOrder { Kind = OrderKind.Rally });
        for (int i = 0; i < 4 && b.Fleeing; i++) bt.Turn();
        Console.WriteLine($"сплотились: {!b.Fleeing}, рамка {b.P.X:0},{b.P.Y:0}, враг {a.P.X:0},{a.P.Y:0}");
        for (int i = 0; i < 8; i++)
        {
            bt.Turn();
            double far = 0; int fk = -1;
            for (int k = 0; k < b.Figs.Count; k++) { b.P.ToWorld(b.P.Figs[k].X, b.P.Figs[k].Y, out var sx, out var sy); double d = JsMath.Hypot(b.Figs[k].AX - sx, b.Figs[k].AY - sy); if (d > far) { far = d; fk = k; } }
            var s = b.Figs[fk]; b.P.ToWorld(b.P.Figs[fk].X, b.P.Figs[fk].Y, out var px, out var py);
            Console.WriteLine($"  ход +{i + 1}: дальше всех колонна {s.Id} — {far:0.0} м; якорь {s.AX:0.0},{s.AY:0.0} → место {px:0.0},{py:0.0}; середина {s.X:0.0},{s.Y:0.0} бойцов {s.MenN}; хочет {s.Dvx:0.0},{s.Dvy:0.0} (предел {s.Vmax:0.0}); идёт {s.AVx:0.0},{s.AVy:0.0}; упёрлась в {s.BlockedBy}; Wrap {s.Wrap} Returning {s.Returning}");
        }
    }
}

// Б3: враг разбит — сняли ли колонны охват, разворот, возврат (как тест «враг разбит»)
static class MenUnwrapProbe
{
    public static void Run()
    {
        var R = MenBodyTests.RB;
        var bt = new Battle(MoveTests.Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(31).Next });
        var TA = Templates.Get("knights"); var TB = Templates.Get("infantry");
        var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), R);
        var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        bt.Turn();
        Console.WriteLine($"в охвате {a.Figs.Count(s => s.Wrap)}");
        b.P.U.Status = "destroyed"; b.P.U.Soldiers = 0;
        int k = 0;
        var track = a.Figs.FirstOrDefault(q => q.Id == 42);
        bt.Turn(_ =>
        {
            if (track != null && k % 10 == 0)
            {
                int q = a.Figs.IndexOf(track);
                if (q >= 0) { a.P.ToWorld(a.P.Figs[q].X, a.P.Figs[q].Y, out var sx, out var sy); Console.WriteLine($"    {k * R.Move.Dt:0.0} с: колонна 42 — №{q} (ряд {a.P.Figs[q].Rank}, колонна строя {a.P.Figs[q].File}), место {sx:0.0},{sy:0.0}, якорь {track.AX:0.0},{track.AY:0.0}, GoalM {track.GoalM:0.0}; солдат {a.P.U.Soldiers:0.0}, разложено {a.LaidMen}, колонн {a.Cols}/{a.NominalCols}, рамка {a.P.X:0.0},{a.P.Y:0.0} курс {a.P.Facing:0}"); }
            }
            if (++k % 50 != 0) return;
            double far = 0, farA = 0;
            for (int q = 0; q < a.Figs.Count; q++) { a.P.ToWorld(a.P.Figs[q].X, a.P.Figs[q].Y, out var sx, out var sy); far = Math.Max(far, JsMath.Hypot(a.Figs[q].X - sx, a.Figs[q].Y - sy)); farA = Math.Max(farA, JsMath.Hypot(a.Figs[q].AX - sx, a.Figs[q].AY - sy)); }
            if (k % 100 == 0)
                foreach (var q in Enumerable.Range(0, a.Figs.Count).OrderByDescending(q => { a.P.ToWorld(a.P.Figs[q].X, a.P.Figs[q].Y, out var sx, out var sy); return JsMath.Hypot(a.Figs[q].AX - sx, a.Figs[q].AY - sy); }).Take(4))
                {
                    var s = a.Figs[q]; a.P.ToWorld(a.P.Figs[q].X, a.P.Figs[q].Y, out var sx, out var sy);
                    if (k == 300) foreach (var x in a.Men.Where(x => x.Alive && x.Fig == s).Take(6)) { var h = Soldiers.HomeOf(a, x); Console.WriteLine($"        боец {x.Id}: {x.X:0.0},{x.Y:0.0} v {x.Vx:0.0},{x.Vy:0.0} место {h.x:0.0},{h.y:0.0} (Lx {x.Lx:0.0} Ly {x.Ly:0.0} ряд {x.Row}) reseat {x.Reseat} via {x.ViaX:0.0} foe {(x.Foe != null)}"); }
                    Console.WriteLine($"     колонна {s.Id}: якорь {s.AX:0.0},{s.AY:0.0} → место {sx:0.0},{sy:0.0} ({JsMath.Hypot(s.AX - sx, s.AY - sy):0.0} м); бойцов {s.MenN}, середина {s.X:0.0},{s.Y:0.0}; хочет {s.Dvx:0.0},{s.Dvy:0.0} (предел {s.Vmax:0.0}); идёт {s.AVx:0.0},{s.AVy:0.0}; упёрлась в {s.BlockedBy}; GoalM {s.GoalM:0.0}");
                }
            Console.WriteLine($"  {k * R.Move.Dt:0.0} с: Wrap {a.Figs.Count(s => s.Wrap)}, Turned {a.Figs.Count(s => s.Turned)}, Returning {a.Figs.Count(s => s.Returning)}; дальше всех от места: середина {far:0.0} м, якорь {farA:0.0} м; приказ {a.Order?.Kind} done {a.Done}");
        });
    }
}

// Б3: ополчение идёт на гвардию с 40 м (тест Г44) — сошлись ли за ход
static class MenG44Probe
{
    public static void Run()
    {
        var R = MenBodyTests.RB;
        var bt = new Battle(MoveTests.Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(9).Next });
        var TA = Templates.Get("militia"); var TB = Templates.Get("guard");
        var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), R);
        var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 40 + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
        int k = 0;
        bt.Turn(_ =>
        {
            if (++k % 30 != 0) return;
            double gap = double.MaxValue;
            foreach (var x in a.Men) if (x.Alive) foreach (var y in b.Men) if (y.Alive && Math.Abs(x.Y - y.Y) < 4 && Math.Abs(x.X - y.X) < 4) gap = Math.Min(gap, JsMath.Hypot(x.X - y.X, x.Y - y.Y));
            double ay = a.Men.Where(x => x.Alive).Max(x => x.Y), by = b.Men.Where(x => x.Alive).Min(x => x.Y);
            Console.WriteLine($"  {k * R.Move.Dt:0.0} с: рамка A {a.P.X:0},{a.P.Y:0.0} done {a.Done} held {a.Held}; передний край A {ay:0.0}, B {by:0.0}; мин. между бойцами {(gap > 1e9 ? -1 : gap):0.00}; колонн развёрнуто {a.Figs.Count(s => s.Turned)}, в охвате {a.Figs.Count(s => s.Wrap)}; схваток {bt.Fights.Count}");
        });
    }
}

// Г94: кто из коней «пятится» быстро на отступлении
static class MenTurnProbe
{
    public static void Run()
    {
        var RB = MenBodyTests.RB;
        var bt = new Battle(MoveTests.Open(800, 800), RB, new EngineContext { Rng = new Mulberry32(5).Next });
        var t = Templates.Get("knights");
        var m = bt.Add(t.Make(1, t.Name, 300, 1), 400, 300, 180);
        bt.Order(m, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });   // точки нет — прямо назад (Г81)
        Console.WriteLine($"приказ: {m.Order?.Kind} к {m.Order?.X:0},{m.Order?.Y:0}; рамка {m.P.X:0},{m.P.Y:0} курс {m.P.Facing:0}");
        int k = 0; double worst = 0;
        var prev = m.Men.ToDictionary(x => x, x => (x.X, x.Y));
        bt.Turn(_ =>
        {
            k++;
            foreach (var x in m.Men)
            {
                if (!x.Alive) continue;
                double h = x.Facing * Math.PI / 180, back = -(x.Vx * Math.Sin(h) - x.Vy * Math.Cos(h));
                var p = prev[x]; double jump = JsMath.Hypot(x.X - p.X, x.Y - p.Y) / RB.Move.Dt;
                prev[x] = (x.X, x.Y);
                if (k * RB.Move.Dt >= 0.5 && back > worst && back > 1) { worst = back; Console.WriteLine($"  {k * RB.Move.Dt:0.00} с: боец {x.Id} назад {back:0.0} м/с, скорость {x.Vx:0.0},{x.Vy:0.0} (по смещению {jump:0.0}), курс {x.Facing:0}, колонна курс {x.Fig.Hd:0}, якорь {x.Fig.AX:0.0},{x.Fig.AY:0.0}, сам {x.X:0.0},{x.Y:0.0}, reseat {x.Reseat}"); }
            }
            if (k % 40 == 0) Console.WriteLine($"  {k * RB.Move.Dt:0.0} с: рамка {m.P.X:0},{m.P.Y:0.0}, курс бойцов: по ходу (около 0°) {m.Men.Count(x => Math.Abs(MoveSim.AngleDiff(x.Facing, 0)) < 45)}, к врагу (около 180°) {m.Men.Count(x => Math.Abs(MoveSim.AngleDiff(x.Facing, 180)) < 45)}");
        });
        // второй ход: кто к концу не довернулся к строю (180°) и почему
        int k2 = 0;
        var stuck = m.Men.Where(x => x.Alive && Math.Abs(MoveSim.AngleDiff(x.Facing, 180)) > 30).Take(2).ToList();
        var trace = new List<string>();
        bt.Turn(_ =>
        {
            k2++;
            if (k2 <= 30) trace.Add(string.Join(" ", stuck.Select(x => $"{x.Facing:0}°/{JsMath.Hypot(x.Vx, x.Vy):0.0}")));
            if (k2 == 30) Console.WriteLine("  курс/скорость двух застрявших по шагам (1,5 с): " + string.Join(" | ", trace));
            if (k2 % 100 != 0 && k2 != 300) return;
            var off = m.Men.Where(x => x.Alive && Math.Abs(MoveSim.AngleDiff(x.Facing, 180)) > 30).ToList();
            Console.WriteLine($"  ход 2, {k2 * RB.Move.Dt:0.0} с: не довернулись {off.Count} из {m.Men.Count(x => x.Alive)}; рамка {m.P.X:0.0},{m.P.Y:0.0} done {m.Done}");
            foreach (var x in off.Take(4))
            {
                var h = Soldiers.HomeOf(m, x); var s = x.Fig;
                var occ = m.Men.Where(o => o.Alive && o != x).OrderBy(o => JsMath.Hypot(o.X - h.x, o.Y - h.y)).First(); var oh = Soldiers.HomeOf(m, occ);
                Console.WriteLine($"     боец {x.Id} ряд {x.Row} колонна {s.Id}: курс {x.Facing:0}, V {JsMath.Hypot(x.Vx, x.Vy):0.00}, до места {JsMath.Hypot(h.x - x.X, h.y - x.Y):0.0} м (место {h.x:0.0},{h.y:0.0}, сам {x.X:0.0},{x.Y:0.0}, якорь {s.AX:0.0},{s.AY:0.0}); у его места стоит боец {occ.Id} колонны {occ.Fig.Id} ряд {occ.Row} в {JsMath.Hypot(occ.X - h.x, occ.Y - h.y):0.0} м, тот от своего места в {JsMath.Hypot(oh.x - occ.X, oh.y - occ.Y):0.0} м");
            }
        });
    }
}

// Г94: кто из коней в бою (охват рыцарей) уходит назад за 0,5 с и почему
static class MenTurnBattleProbe
{
    public static void Run()
    {
        var RB = MenBodyTests.RB;
        var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(301).Next });
        var TA = Templates.Get("knights"); var TB = Templates.Get("infantry");
        var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
        var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), RB);
        var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        var win = new Dictionary<Man, (double X, double Y, double F)>();
        int frame = 0, total = 0, back = 0, printed = 0;
        var why = new Dictionary<string, int>();
        bt.Turn(_ =>
        {
            if (++frame % 10 != 0) return;
            var near = new HashSet<(int, int)>();
            foreach (var y in b.Men) if (y.Alive) near.Add(((int)Math.Floor(y.X / 3), (int)Math.Floor(y.Y / 3)));
            bool Close(Man x) { int cx = (int)Math.Floor(x.X / 3), cy = (int)Math.Floor(x.Y / 3); for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) if (near.Contains((cx + i, cy + j))) return true; return false; }
            foreach (var x in a.Men)
            {
                if (!x.Alive) continue;
                if (win.TryGetValue(x, out var w) && x.Foe == null && !Close(x))
                {
                    double a0 = w.F * Math.PI / 180, dx = x.X - w.X, dy = x.Y - w.Y;
                    double bk = -(dx * Math.Sin(a0) - dy * Math.Cos(a0)) / 0.5;
                    total++;
                    if (bk > 1)
                    {
                        back++;
                        var s = x.Fig; string k = (s.Wrap ? (s.Fighting ? "охват, бьётся" : "охват, идёт") : s.Returning ? "возврат" : s.Fighting ? "в строю, бьётся" : "в строю") + (bk > 3 ? " (>3 м/с)" : "");
                        why[k] = why.TryGetValue(k, out var c) ? c + 1 : 1;
                        if (k.StartsWith("охват, идёт") && printed++ < 8) Console.WriteLine($"  {frame * RB.Move.Dt:0.0} с: боец {x.Id} назад {bk:0.0} м/с, курс был {w.F:0} стал {x.Facing:0}, сдвиг {dx:0.0},{dy:0.0}; колонна: {k}, курс колонны {s.Hd:0}, хочет {s.Dvx:0.0},{s.Dvy:0.0}, идёт {s.AVx:0.0},{s.AVy:0.0}; reseat {x.Reseat}; сквозь своих {s.Wrap && !s.Fighting && s.GoalM > 3}, GoalM {s.GoalM:0.0}, скорость {x.Vx:0.0},{x.Vy:0.0}");
                    }
                }
                win[x] = (x.X, x.Y, x.Facing);
            }
        });
        Console.WriteLine($"окон {total}, назад {back}: " + string.Join(", ", why.Select(kv => $"{kv.Key} {kv.Value}")));
    }
}

// Г90: натиск телами — сбитые с ног, на сколько шеренг вошли кони; на пики во фронт — конь у острия
static class MenChargeProbe
{
    public static void Run()
    {
        var RB = MenBodyTests.RB;
        foreach (var tb in new[] { "infantry", "pikemen" })
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(5).Next });
            var TA = Templates.Get("knights"); var TB = Templates.Get(tb);
            var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), RB);
            var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 150 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            double front = b.Men.Where(x => x.Alive).Min(x => x.Y), rank = RB.Map.Formation[TB.Type].RankDepth;
            double deep = 0, minGap = double.MaxValue, vHit = 0; int knocked = 0, k = 0, readyAt = -1;
            var log = new List<string>();
            for (int t = 0; t < 2; t++)
                log.AddRange(bt.Turn(_ =>
                {
                    k++;
                    if (a.ChargeReady && readyAt < 0) readyAt = k;
                    knocked = b.Men.Count(x => !double.IsNaN(x.DownAt));
                    // вглубь строя врага — по его рамке (колонны охвата в тылу не в счёт): от переднего края, внутри фронта
                    foreach (var h in a.Men)
                    {
                        if (!h.Alive) continue;
                        b.P.ToLocal(h.X, h.Y, out var lx, out var ly);
                        if (Math.Abs(lx) < b.P.Fp.Front / 2 - 2 && ly < 0) deep = Math.Max(deep, ly + b.P.Fp.Depth / 2);
                    }
                    if (tb == "pikemen")
                        foreach (var h in a.Men.Where(q => q.Alive && q.Y > front - 8))
                            foreach (var p in b.Men.Where(q => q.Alive && q.Row == 0 && Math.Abs(q.X - h.X) < 3))
                                minGap = Math.Min(minGap, p.Y - h.Y);
                }));
            foreach (var h in a.Men.Where(q => q.Alive).Select(q => { b.P.ToLocal(q.X, q.Y, out var lx, out var ly); return (q, lx, ly); })
                                   .Where(p => Math.Abs(p.lx) < b.P.Fp.Front / 2 - 2 && p.ly < 0 && p.ly > -b.P.Fp.Depth / 2 + 1).Take(4))
                Console.WriteLine($"   конь {h.q.Id} внутри: у B {h.lx:0.0},{h.ly:0.0}; колонна в охвате {h.q.Fig.Wrap}, курс колонны {h.q.Fig.Hd:0}, свой {h.q.Facing:0}; противник {(h.q.Foe != null)}");
            Console.WriteLine($"   строй B: фронт {b.P.Fp.Front:0.0}, глубина {b.P.Fp.Depth:0.0}, живых {b.Men.Count(q => q.Alive)}, у B рамка {b.P.X:0},{b.P.Y:0}");
            Console.WriteLine($"рыцари натиском на {tb}: натиск готов с {readyAt * RB.Move.Dt:0.0} с; сбито с ног {knocked}; кони вошли за передний край на {deep:0.0} м ({deep / rank:0.0} шеренги); потери A {1000 - a.P.U.Soldiers:0} B {1000 - b.P.U.Soldiers:0}" + (tb == "pikemen" ? $"; ближе всего конь к переднему пикинёру по y {minGap:0.0} м" : ""));
            Console.WriteLine("   " + string.Join(" | ", log.Where(l => l.Contains("натиск")).Take(3)));
        }
    }
}

// Г90: на пики во фронт — где встают кони перед остриями; потери против фигурок
static class MenPikeFrontProbe
{
    public static void Run()
    {
        foreach (var r in new[] { Rules.Base, MenBodyTests.RB })
        {
            double la = 0, lb = 0; int near = 0, at = 0, samples = 0;
            for (uint seed = 1; seed <= 6; seed++)
            {
                var bt = new Battle(MoveTests.Open(1000, 1000), r, new EngineContext { Rng = new Mulberry32(seed + 40).Next });
                var TA = Templates.Get("knights"); var TB = Templates.Get("pikemen");
                var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
                var fa = Formation.Of(TA.Make(1, TA.Name, 1000, 1), r);
                var a = bt.Add(TA.Make(1, TA.Name, 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 150 + fa.Depth / 2), 180);
                bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
                int k = 0;
                bt.Turn(_ =>
                {
                    if (!r.Move.MenBodies || ++k % 20 != 0) return;
                    // кони перед фронтом пикинёров (в пределах их фронта): зазор до ближнего пикинёра первых 4 рядов
                    var pk = b.Men.Where(q => q.Alive && q.Row < 4).ToList();
                    foreach (var h in a.Men)
                    {
                        if (!h.Alive) continue;
                        b.P.ToLocal(h.X, h.Y, out var lx, out var ly);
                        if (Math.Abs(lx) > b.P.Fp.Front / 2 - 3 || ly > -b.P.Fp.Depth / 2) continue;
                        double g = pk.Count == 0 ? 99 : pk.Min(q => JsMath.Hypot(q.X - h.X, q.Y - h.Y));
                        if (g > 8) continue;
                        samples++; if (g < 2) near++; else if (g < 5) at++;
                    }
                });
                la += 1000 - a.P.U.Soldiers; lb += 1000 - b.P.U.Soldiers;
            }
            Console.WriteLine($"{(r.Move.MenBodies ? "бойцы" : "фигурки")}: рыцари натиском на пики во фронт — потери рыцарей {la / 6:0}, пикинёров {lb / 6:0}" + (r.Move.MenBodies ? $"; кони у фронта: ближе 2 м до пикинёра {near}, 2–5 м (у острия) {at} из {samples}" : ""));
        }
    }
}

// Г90: глубина натиска — от начального переднего края пехоты, в первые 3 с после касания
static class MenChargeDepthProbe
{
    public static void Run()
    {
        var RB = MenBodyTests.RB;
        for (uint seed = 1; seed <= 4; seed++)
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), RB, new EngineContext { Rng = new Mulberry32(seed + 4).Next });
            var TA = Templates.Get("knights"); var TB = Templates.Get("infantry");
            var b = bt.Add(TB.Make(2, TB.Name, 1000, 2), 500, 500, 0);
            var fa = Formation.Of(TA.Make(1, TA.Name, 150, 1), RB);   // рыцарей уже, чем пехоты: огибать нечего — только натиск
            var a = bt.Add(TA.Make(1, TA.Name, 150, 1), 500, 500 - (b.P.Fp.Depth / 2 + 150 + fa.Depth / 2), 180);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            double front = b.Men.Where(x => x.Alive).Min(x => x.Y), F = b.P.Fp.Front / 2;
            double t0 = -1, deep = 0, vmax = 0; int k = 0, maxKnocks = 0;
            var deeps = new List<double>();
            bt.Turn(_ =>
            {
                k++;
                double t = k * RB.Move.Dt;
                if (t0 < 0 && bt.Fights.Count > 0) t0 = t;
                if (t0 < 0 || t > t0 + 3) return;
                // конь внутри строя пехоты: пехотинцы и спереди, и сзади (по y) ближе 3 м — глубина от начального переднего края
                var bm = b.Men.Where(x => x.Alive && x.DownLeft <= 0).ToList();
                foreach (var h in a.Men)
                {
                    if (!h.Alive || Math.Abs(h.X - 500) > F - 3) continue;   // у фланга — колонны охвата, не натиск
                    vmax = Math.Max(vmax, JsMath.Hypot(h.Vx, h.Vy)); maxKnocks = Math.Max(maxKnocks, h.Knocks);
                    bool before = false, after = false;
                    foreach (var x in bm)
                    {
                        if (Math.Abs(x.X - h.X) > 1.5 || Math.Abs(x.Y - h.Y) > 3) continue;
                        if (x.Y < h.Y - 0.5) before = true; else if (x.Y > h.Y + 0.5) after = true;
                    }
                    deep = Math.Max(deep, h.Y - front); deeps.Add(h.Y - front);   // центр коня за начальным передним краем
                }
            });
            var hs = deeps.OrderByDescending(v => v).ToList();
            int down = b.Men.Count(x => !double.IsNaN(x.DownAt)), killedDown = b.Men.Count(x => !x.Alive && !double.IsNaN(x.DownAt));
            Console.WriteLine($"зерно {seed + 4}: коснулись на {t0:0.0} с; за 3 с центр коня (середина фронта) глубже всего на {deep:0.0} м за начальным краем (шеренга 1 м, полкорпуса коня 1,4 м), 95% замеров — не глубже {(hs.Count > 20 ? hs[hs.Count / 20] : 0):0.0} м (замеров {hs.Count}); скорость до {vmax:0.0} м/с; сбито {down} (из них пали потом {killedDown}); больше всех сбил конь — {maxKnocks}; потери за ход A {150 - a.P.U.Soldiers:0} B {1000 - b.P.U.Soldiers:0}; в охвате колонн {a.Figs.Count(q => q.Wrap)}");
        }
    }
}

// Г86: марш одинокого отряда — сколько бойцов у мест (ближе RigidSnapM), сколько жёстких, отставание от места
static class MenRigidProbe
{
    public static void Run(string[] opts)
    {
        var RB = MenBodyTests.RB;
        foreach (var o in opts)
        {
            if (o == "nowave") RB.Men.WaveRowSec = 0;
            else if (o.StartsWith("snap")) RB.Men.RigidSnapM = double.Parse(o.Substring(4), System.Globalization.CultureInfo.InvariantCulture);
        }
        Console.WriteLine($"  правила: WaveRowSec {RB.Men.WaveRowSec}, RigidSnapM {RB.Men.RigidSnapM}");
        var geo = MoveTests.Open(1200, 1200);
        var t = Templates.Get("infantry");
        var m = Mover.Place(t.Make(1, t.Name, 1000, 1), 600, 1100, 0, RB);
        MoveSim.Give(m, new MoveOrder { X = 600, Y = 100, Facing = 0 }, geo, RB);
        int k = 0;
        var tr8 = new List<string>();
        for (int turn = 0; turn < 2; turn++)
            MoveSim.Turn(new[] { m }, geo, RB, _ =>
            {
                k++;
                if (k >= 100 && k < 124) { var q = m.Men.First(z => z.Id == 8); var hq = Soldiers.HomeOf(m, q); double ax = -(q.Y - hq.y); tr8.Add($"{(q.WasRigid ? "Ж" : "о")} {ax:+0.00;-0.00} V{JsMath.Hypot(q.Vx, q.Vy):0.0}"); }
                if (k == 124) Console.WriteLine("   боец 8 по шагам (Ж — жёсткий; впереди места +, позади −; м): " + string.Join(" | ", tr8));
                if (k % 60 != 0) return;
                var far = m.Men.Where(x => x.Alive).Select(x => { var h = Soldiers.HomeOf(m, x); return JsMath.Hypot(h.x - x.X, h.y - x.Y); }).OrderBy(v => v).ToList();
                Console.WriteLine($"  {k * RB.Move.Dt:0.0} с: жёстких {MenBodies.RigidMen}, отрядов вдали {MenBodies.RigidUnits}; от места: медиана {far[far.Count / 2]:0.00}, 90% {far[far.Count * 9 / 10]:0.00}, макс {far[far.Count - 1]:0.00}; ближе 1 м — {far.Count(v => v < 1) * 100 / far.Count}%; центр y {m.P.Y:0}, Vs {m.Vs:0.0}, якорь 0-й колонны идёт {JsMath.Hypot(m.Figs[0].AVx, m.Figs[0].AVy):0.0}");
                var al = m.Men.Where(x => x.Alive).ToList();
                if (k == 180)
                    foreach (var x in al.Where(q => !q.WasRigid).Take(5))
                    {
                        var h = Soldiers.HomeOf(m, x); var sg = x.Fig; var f = RB.Map.Formation["infantry"];
                        double alv = Math.Max(1e-9, Math.Sqrt(sg.AVx * sg.AVx + sg.AVy * sg.AVy));
                        double ahead = ((x.Lx * sg.Hc - x.Ly * sg.Hs) * sg.AVx + (x.Lx * sg.Hs + x.Ly * sg.Hc) * sg.AVy) / alv;
                        double wave = Math.Max(0, (f.Ranks * f.RankDepth / 2 - ahead) / f.RankDepth) * RB.Men.WaveRowSec;
                        Console.WriteLine($"       нежёсткий {x.Id} ряд {x.Row}: от места {JsMath.Hypot(h.x - x.X, h.y - x.Y):0.00}, время−StartT {m.Steps * RB.Move.Dt - sg.StartT:0.00} (StartT {sg.StartT:0.00}, StopT {sg.StopT:0.00}), волна {wave:0.00}, Moving {sg.Moving}, место свободно {MoveSim.Free(m.Field, h.x, h.y)}, сам на суше {MoveSim.Free(m.Field, x.X, x.Y)}, V {JsMath.Hypot(x.Vx, x.Vy):0.0}, якорь идёт {alv:0.0}");
                    }
                Console.WriteLine($"     WasRigid {al.Count(x => x.WasRigid)}, reseat {al.Count(x => x.Reseat)}, thaw {al.Count(x => x.Thaw)}, бьются {al.Count(x => x.Fig.Fighting)}, охват {al.Count(x => x.Fig.Wrap)}, возврат {al.Count(x => x.Fig.Returning)}, лежат {al.Count(x => x.DownLeft > 0)}, via {al.Count(x => !double.IsNaN(x.ViaX))}; колонн {m.Figs.Count}, Moving {m.Figs.Count(f => f.Moving)}, StartT>−∞ {m.Figs.Count(f => !double.IsNegativeInfinity(f.StartT))}");
            });
    }
}

// Г81 при бойцах-телах: отступление из схватки по шагам — кто держит отряд (dotnet run --project Tests -- g81)
static class MenRetreatProbe
{
    public static void Run()
    {
        var R = Rules.Base;
        var bt = new Battle(MoveTests.Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(4).Next });
        var T = Templates.Get("infantry");
        var b = bt.Add(T.Make(2, "B", 1000, 2), 500, 500, 0);
        var fa = Formation.Of(T.Make(1, "A", 1000, 1), R);
        var a = bt.Add(T.Make(1, "A", 1000, 1), 500, 500 - (b.P.Fp.Depth / 2 + 0.5 + fa.Depth / 2), 180);
        bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
        bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
        bt.Turn();
        Console.WriteLine($"сошлись: {bt.Fights.Any(f => f.Touching)}; B y={b.P.Y:0.0} facing={b.P.Facing:0}");
        double y0 = b.P.Y;
        bt.Order(b, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });
        Console.WriteLine($"приказ: track={(b.Track == null ? "нет" : b.Track.Cost.ToString("0"))} onSpot={b.OnSpot} done={b.Done} note={b.Note} order=({b.Order?.X:0},{b.Order?.Y:0}) side={b.Side}");
        double last = -1;
        bt.Turn(t =>
        {
            if (t - last < 1 - 1e-9) return; last = t;
            int fight = b.Figs.Count(s => s.Fighting), blocked = b.Figs.Count(s => s.BlockedBy != 0), foeMen = b.Men.Count(x => x.Alive && x.Foe != null);
            double my = b.Men.Where(x => x.Alive).Average(x => x.Y), ay = b.Figs.Average(s => s.AY);
            Console.WriteLine($"{t,5:0.0} с: y={b.P.Y - y0,6:0.0} бойцы y={my - y0,6:0.0} якоря y={ay - y0,6:0.0} Vs={b.Vs:0.00} held={b.Held} hold={b.HoldLeft:0.00} blocker={b.LastBlockerName}/{b.LastBlockerEnemy} reform={b.Reforming} done={b.Done} onSpot={b.OnSpot} колонн в схватке {fight}, упёрлись {blocked}, бойцов с противником {foeMen}");
        });
    }
}
