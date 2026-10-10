// Приказы и ход (И2, Г79–Г81): ход по шагам, «отступить», предпросмотр приказа, сессия WEGO
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

static class OrdersTests
{
    static readonly Rules R = Rules.Base;
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    static (Battle bt, Mover a, Mover b) Duel(string ta, string tb, double gap, uint seed, double w = 1000, double h = 1000)
    {
        var bt = new Battle(MoveTests.Open(w, h), R, new EngineContext { Rng = new Mulberry32(seed).Next });
        var TA = Templates.Get(ta); var TB = Templates.Get(tb);
        var b = bt.Add(TB.Make(2, "B", 1000, 2), w / 2, h / 2, 0);
        var fa = Formation.Of(TA.Make(1, "A", 1000, 1), R);
        var a = bt.Add(TA.Make(1, "A", 1000, 1), w / 2, h / 2 - (b.P.Fp.Depth / 2 + gap + fa.Depth / 2), 180);
        return (bt, a, b);
    }

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("ход по шагам (Г43): BeginTurn — Step — EndTurn даёт то же, что Turn целиком", () =>
        {
            double[] Run(bool steps)
            {
                var (bt, a, b) = Duel("infantry", "infantry", 30, 3);
                bt.Order(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
                for (int turn = 0; turn < 2; turn++)
                {
                    if (!steps) { bt.Turn(); continue; }
                    bt.BeginTurn();
                    int n = 0; while (bt.Step()) n++;
                    True(n + 1 == MoveSim.StepsPerTurn(R), $"шагов {n + 1}");
                    bt.EndTurn();
                }
                return new[] { a.P.U.Soldiers, b.P.U.Soldiers, a.P.X, a.P.Y }.Concat(b.Men.Where(x => x.Alive).SelectMany(x => new[] { x.X, x.Y })).ToArray();
            }
            True(Run(true).SequenceEqual(Run(false)), "по шагам вышло иначе");
        });

        yield return ("расстановка (Алекс 10.10.2026): до первого хода сессия в фазе Deploy — отряд переставляется свободно (Place), не тратя хода и без приказа; за свою зону — отказ с причиной; приказы в расстановке не принимаются; EndDeploy — к приказам, после первого хода расстановки нет", () =>
        {
            var bt = new Battle(MoveTests.Open(1000, 1000), R, new EngineContext { Rng = new Mulberry32(7).Next });
            var T = Templates.Get("infantry");
            var a = bt.Add(T.Make(1, "Свои", 1000, 1), 500, 800, 0); var b = bt.Add(T.Make(2, "Враг", 1000, 2), 500, 200, 180);
            var ses = new BattleSession(bt);
            ses.Zones[1] = (0, 500, 1000, 1000); ses.Zones[2] = (0, 0, 1000, 500);
            bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });   // «держать» из сценария расстановке не мешает
            True(ses.BeginDeploy() && ses.Phase == Phase.Deploy, "в расстановку не вошли");
            True(ses.SetOrder(a, new MoveOrder { X = 500, Y = 600, Facing = 0 }) != null, "приказ в расстановке принят");
            True(ses.Place(a, 300, 650, 90) == null && Math.Abs(a.P.X - 300) < 1e-6 && Math.Abs(a.P.Y - 650) < 1e-6 && Math.Abs(a.P.Facing - 90) < 1e-6 && a.Order == null && ses.Turn == 1, $"не переставился: ({a.P.X:0}, {a.P.Y:0}) курс {a.P.Facing:0}, приказ {a.Order?.Kind}");
            True(ses.Place(a, 300, 300, 0) != null && Math.Abs(a.P.Y - 650) < 1e-6, "за зону поставили");
            True(ses.Place(b, 700, 300, 180) == null, "врага не переставили");
            ses.EndDeploy(); True(ses.Phase == Phase.Orders, "к приказам не перешли");
            True(ses.Place(a, 300, 600, 0) != null, "после расстановки переставили");
            ses.SetOrder(a, new MoveOrder { X = 300, Y = 600, Facing = 90 }); ses.Go(); while (ses.Step()) { }
            True(!ses.BeginDeploy(), "после первого хода расстановка открылась");
        });

        yield return ("отступить (Г81): пятится лицом к врагу на половине нормы и выходит из схватки", () =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 0.5, 4);
            bt.Order(a, new MoveOrder { Kind = OrderKind.Hold });
            bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
            bt.Turn();
            True(bt.Fights.Any(f => f.Touching), "сошлись");
            double x0 = b.P.X, y0 = b.P.Y, f0 = b.P.Facing;
            bt.Order(b, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });
            bt.Turn();
            double back = JsMath.Hypot(b.P.X - x0, b.P.Y - y0);
            True(back >= 38 && back <= 55, $"отошёл на {back:0} м, полнормы 50");
            True(b.P.Y > y0 + 30, "отходил не назад");
            True(Math.Abs(MoveSim.AngleDiff(f0, b.P.Facing)) < 3, $"повернулся: {f0:0}° → {b.P.Facing:0}°");
            bt.Turn();
            True(!bt.Fights.Any(f => !f.Over && f.Touching), "из схватки не вышел");
        });

        yield return ("предпросмотр: путь, где встанет к концу хода, натиск и дальность — отряд не трогает", () =>
        {
            var (bt, a, b) = Duel("knights", "infantry", 150, 5, 1600, 1600);
            double ax = a.P.X, ay = a.P.Y; var ord = a.Order;
            var far = bt.Preview(a, new MoveOrder { Kind = OrderKind.Move, X = a.P.X + 600, Y = a.P.Y, Facing = 90 });
            True(far.Note == null && !far.ReachThisTurn && Math.Abs(far.ThisTurn - 250) < 1, $"за ход {far.ThisTurn:0} из нормы 250");
            True(Math.Abs(JsMath.Hypot(far.EndX - ax, far.EndY - ay) - 250) < 15, $"встанет в {JsMath.Hypot(far.EndX - ax, far.EndY - ay):0} м");
            var ch = bt.Preview(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            True(ch.ChargeOk == true && ch.ReachThisTurn, $"натиск со 150 м: {ch.ChargeWhy}");
            True(a.P.X == ax && a.P.Y == ay && a.Order == ord && a.Track == null, "предпросмотр сдвинул отряд");
            var (bt2, a2, _) = Duel("knights", "infantry", 20, 5);
            var ch2 = bt2.Preview(a2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            True(ch2.ChargeOk == false && ch2.ChargeWhy.Contains("разбег"), $"натиск с 20 м: {ch2.ChargeWhy}");
            var (bt3, a3, _) = Duel("archers", "infantry", 120, 5);
            var sh = bt3.Preview(a3, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            True(sh.InRange == true && sh.ReachThisTurn && sh.Gap < sh.Range, $"лучники со 120 м: зазор {sh.Gap:0}, дальность {sh.Range:0}");
            var rt = bt.Preview(a, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });
            True(Math.Abs(rt.ThisTurn - 125) < 1 && Math.Abs(MoveSim.AngleDiff(rt.EndFacing, a.P.Facing)) < 1, $"отступить: {rt.ThisTurn:0} м нормы, курс {rt.EndFacing:0}");
        });

        yield return ("сессия (Г79): приказы копятся и уходят разом по «Ход!», ход по шагам, потом снова приказы", () =>
        {
            var (bt, a, b) = Duel("infantry", "infantry", 200, 6);
            var s = new BattleSession(bt);
            s.SideNames[1] = "Пикшарп"; s.SideNames[2] = "Враг";
            True(s.SetOrder(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 }) == null, "приказ не принят");
            True(s.SetOrder(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 1 }) != null, "атаковать себя — принято");
            True(s.SetOrder(b, new MoveOrder { Kind = OrderKind.Rally }) != null, "сплотить отряд в строю — принято");
            True(a.Order == null, "приказ ушёл до «Ход!»");
            s.Go();
            True(s.Phase == Phase.Playing && a.Order != null && a.Order.Kind == OrderKind.Attack, "после «Ход!» приказ у отряда");
            True(s.SetOrder(a, new MoveOrder { Kind = OrderKind.Hold }) != null, "приказ посреди хода принят");
            int steps = 0; while (s.Step()) steps++;
            True(s.Phase == Phase.Orders && s.Turn == 2 && s.Logs.Count == 1, $"фаза {s.Phase}, ход {s.Turn}");
            True(s.OrderOf(a).Kind == OrderKind.Attack, "без нового приказа отряд продолжает прежний");
        });

        yield return ("сессия: у стороны не осталось отрядов в строю — битва кончилась, победитель назван", () =>
        {
            var (bt, a, b) = Duel("guard", "militia", 0.5, 7);
            b.P.U.Discipline = 1; b.P.U.Morale = 10;
            var s = new BattleSession(bt);
            s.SideNames[1] = "Гвардия"; s.SideNames[2] = "Ополчение";
            s.SetOrder(a, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            for (int i = 0; i < 6 && s.Phase != Phase.Over; i++)
            {
                if (s.Phase == Phase.Orders) s.Go();
                while (s.Step()) { }
            }
            True(s.Phase == Phase.Over && s.Winner == 1 && s.Outcome.Contains("Гвардия"), $"фаза {s.Phase}, итог «{s.Outcome}»");
            True(s.SetOrder(a, new MoveOrder { Kind = OrderKind.Hold }) != null, "приказ после конца битвы принят");
        });
    }
}
