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
            var a = bt.Add(inf.Make(1, "Во фланг", 1000, 1), 500 - 62.5 - 4 - 40, 500, 90);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            Console.WriteLine(r.Move.MenBodies ? "— бойцы —" : "— фигурки —");
            for (int t = 0; t < 2; t++)
            {
                int k = 0;
                var log = bt.Turn(_ =>
                {
                    if (++k % 30 != 0) return;
                    var f = bt.Fights.FirstOrDefault();
                    double gap = double.MaxValue;
                    foreach (var x in a.Men) if (x.Alive) foreach (var y in b.Men) if (y.Alive && Math.Abs(x.X - y.X) < 6 && Math.Abs(x.Y - y.Y) < 6) gap = Math.Min(gap, JsMath.Hypot(x.X - y.X, x.Y - y.Y));
                    int foes = a.Men.Count(x => x.Alive && x.Foe != null), wrap = a.Figs.Count(s => s.Wrap);
                    if (r.Move.MenBodies && (k == 120 || k == 270))
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
                    if (r.Move.MenBodies && (k == 150) && t == 1)
                    {
                        foreach (var s in a.Figs.Where(q => q.Wrap && !q.Fighting && JsMath.Hypot(q.WX - q.AX, q.WY - q.AY) > 100))
                        {
                            var F = a.Field;
                            var mm = a.Men.Where(x => x.Alive && x.Fig == s).ToList();
                            Console.WriteLine($"     !! колонна {s.Id}: бойцов {mm.Count}, якорь {s.AX:0.0},{s.AY:0.0}, цель {s.WX:0.0},{s.WY:0.0}, карта {(F == null ? "нет" : $"{F.W}x{F.H}")}, via у {mm.Count(x => !double.IsNaN(x.ViaX))}; якорь идёт {s.AVx:0.0},{s.AVy:0.0} хочет {s.Dvx:0.0},{s.Dvy:0.0}; бойцы {s.X:0.0},{s.Y:0.0} сдвиг мест {s.MLx:0.0},{s.MLy:0.0}; рамка {a.P.X:0.0},{a.P.Y:0.0} курс {a.P.Facing:0}; путь от якоря к цели {(F == null ? 0 : F.SegmentCost(s.AX, s.AY, s.WX, s.WY)):0.0}");
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
