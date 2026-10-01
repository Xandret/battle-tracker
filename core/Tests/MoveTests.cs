// ═══════════ MoveTests.cs — движение строя фигурками (Г31 шаг 1; Г52–Г54) ═══════════
// Карта направлений сверяется с зоной досягаемости трекера; ход — с решениями: норма за ход как за столом,
// разгон, поворот колесом и кругом, ближний ход боком, фигурки на местах и не в непроходимом.
using BattleCore;

static class MoveTests
{
    static Rules R => Rules.Base;
    static void Eq<T>(T actual, T expected, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"{what}: ожидалось «{expected}», получено «{actual}»");
    }
    static void Near(double actual, double expected, double tol, string what)
    {
        if (!(Math.Abs(actual - expected) <= tol)) throw new Exception($"{what}: ожидалось {expected} ± {tol}, получено {actual}");
    }
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }

    // Ровное поле w × h м; fill — чем залить
    public static Geo Open(double w, double h, string fill = "field")
    {
        var m = Terrain.Create(w, h, Terrain.Id(fill));
        return new Geo { Map = m, W = Terrain.WidthM(m), H = Terrain.HeightM(m) };
    }
    public static Mover Unit(string tpl, int id, double x, double y, double facing, double men = 1000)
    {
        var t = Templates.Get(tpl);
        return Mover.Place(t.Make(id, t.Name, men, 1), x, y, facing, R);
    }
    public static List<string> Go(Mover m, Geo geo, double x, double y, double facing, Action<double> frame = null)
    {
        MoveSim.Give(m, new MoveOrder { X = x, Y = y, Facing = facing }, geo, R);
        return MoveSim.Turn(new[] { m }, geo, R, frame);
    }
    static double SlotGap(Mover m)
    {
        double worst = 0;
        for (int k = 0; k < m.Figs.Count; k++)
        {
            m.P.ToWorld(m.P.Figs[k].X, m.P.Figs[k].Y, out var sx, out var sy);
            worst = Math.Max(worst, Math.Sqrt((sx - m.Figs[k].X) * (sx - m.Figs[k].X) + (sy - m.Figs[k].Y) * (sy - m.Figs[k].Y)));
        }
        return worst;
    }

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("движение: карта направлений даёт ту же цену пути, что зона досягаемости трекера", () =>
        {
            foreach (var (tpl, input, seed) in new[] { ("hills", new Dictionary<string, object> { ["count"] = 6.0, ["maxHeight"] = 3.0 }, 1u),
                                                       ("forest", new Dictionary<string, object>(), 777u), ("river", new Dictionary<string, object>(), 1u) })
            {
                var map = MapGen.Generate(tpl, input, seed);
                var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
                foreach (var (type, horse) in new[] { ("infantry", false), ("cavalry", true) })
                {
                    // от центров клеток — чтобы обе стороны взяли одну и ту же клетку
                    double ax = (map.W / 4 + 0.5) * 5, ay = (map.H / 3 + 0.5) * 5, bx = (map.W * 3 / 4 + 0.5) * 5, by = (map.H * 2 / 3 + 0.5) * 5;
                    var u = new Unit { Type = type, MapX = ax / geo.W * 100, MapY = ay / geo.H * 100 };
                    var reach = BattleMap.Reach(u, geo, R, double.PositiveInfinity);
                    var field = FlowField.Build(geo, R, horse, bx, by);
                    double table = BattleMap.PathCost(reach, u, bx / geo.W, by / geo.H, geo), game = field.Cost[field.CellOf(ax, ay)];
                    if (double.IsInfinity(table)) { Eq(double.IsInfinity(game), true, $"{tpl}, {type}: пути нет у обоих"); continue; }
                    Near(game, table, 1e-6, $"{tpl}, {type}: цена пути");
                    // путь строя — не дороже пути по клеткам и только по проходимому (иначе Track не строится)
                    var route = field.Route(ax, ay, bx, by);
                    var track = Track.Build(field, route);
                    True(track != null, $"{tpl}, {type}: путь построен");
                    True(track.Cost <= game + 1e-6, $"{tpl}, {type}: натянутый путь {track.Cost:0.0} дороже цепочки клеток {game:0.0}");
                }
            }
        });

        yield return ("движение: по открытому полю путь — одна прямая, метр за метр нормы", () =>
        {
            var geo = Open(600, 600);
            var f = FlowField.Build(geo, R, false, 452, 100);
            var route = f.Route(102, 500, 452, 100);
            Eq(route.Count, 2, "точек в пути");
            Near(Track.Build(f, route).Cost, Math.Sqrt(350 * 350 + 400 * 400), 1e-9, "цена прямой");
        });

        yield return ("движение: путь обходит озеро", () =>
        {
            var geo = Open(600, 600);
            Terrain.PaintDisc(geo.Map, "t", 60, 60, 20, Terrain.Id("water"));   // озеро 200 м посередине
            var f = FlowField.Build(geo, R, false, 300, 100);
            var route = f.Route(300, 500, 300, 100);
            True(route.Count > 2, "путь огибает воду");
            var tr = Track.Build(f, route);
            True(tr != null && tr.Pieces.All(p => p.Rho == 1), "весь путь по полю");
            True(tr.Cost <= f.Cost[f.CellOf(300, 500)] + 1e-6, "не дороже пути по клеткам");
        });

        yield return ("движение (Г53): пехота с места по полю — ровно 100 м за ход и стоит у цели", () =>
        {
            var geo = Open(600, 600);
            var m = Unit("infantry", 1, 300, 500, 0);
            double arrivedAt = -1;
            var log = Go(m, geo, 300, 400, 0, t => { if (m.OnSpot && arrivedAt < 0) arrivedAt = t; });
            True(m.Done, "дошёл и стоит: " + string.Join(" | ", log));
            Near(m.P.X, 300, 1e-6, "x"); Near(m.P.Y, 400, 1e-6, "y");
            Near(m.Spent, 100, 1e-6, "нормы за ход");
            True(arrivedAt >= 14.5 && arrivedAt <= 15 + 1e-9, $"дошёл к концу хода: {arrivedAt:0.00} с");
            Eq(m.WheelSec, 0.0, "без поворотов");
        });

        yield return ("движение (Г53): цель за три хода — каждый ход ровно норма, скорость переходит в следующий ход", () =>
        {
            var geo = Open(600, 600);
            var m = Unit("infantry", 1, 300, 560, 0);
            Go(m, geo, 300, 210, 0);
            for (int turn = 1; turn <= 3; turn++)
            {
                if (turn > 1) MoveSim.Turn(new[] { m }, geo, R);
                Near(m.Spent, 100, 0.01, $"ход {turn}: нормы");
                True(m.Vs > 6 && m.Vs < 7.2, $"ход {turn}: идёт дальше, скорость {m.Vs:0.00}");
            }
            MoveSim.Turn(new[] { m }, geo, R);
            True(m.Done, "на четвёртый ход — на месте");
            Near(m.P.Y, 210, 1e-6, "y");
            Near(m.Spent, 50, 1e-6, "последние 50 м");
        });

        yield return ("движение (Г53): конница — 250 м за ход, разгон дольше пехоты", () =>
        {
            var geo = Open(600, 600);
            var k = Unit("knights", 1, 300, 560, 0);
            var p = Unit("infantry", 2, 100, 560, 0);
            double kAt1 = -1, pAt1 = -1;
            MoveSim.Give(k, new MoveOrder { X = 300, Y = 310, Facing = 0 }, geo, R);
            MoveSim.Give(p, new MoveOrder { X = 100, Y = 460, Facing = 0 }, geo, R);
            MoveSim.Turn(new[] { k, p }, geo, R, t => { if (Math.Abs(t - 1) < 1e-9) { kAt1 = k.Vs / MoveSim.TopSpeed(k.P.U, R); pAt1 = p.Vs / MoveSim.TopSpeed(p.P.U, R); } });
            True(k.Done && p.Done, "оба дошли");
            Near(k.Spent, 250, 1e-6, "нормы у конницы");
            Near(pAt1, 1, 1e-6, "пехота за 1 с — в полную силу");
            Near(kAt1, 1.0 / 3, 0.01, "конница за 1 с — треть скорости");
        });

        yield return ("движение: лес — вдвое медленнее по земле, норма та же", () =>
        {
            var geo = Open(600, 600, "forest");
            var m = Unit("infantry", 1, 300, 560, 0);
            Go(m, geo, 300, 100, 0);
            Near(m.Spent, 100, 0.01, "нормы");
            Near(m.Moved, 50, 0.01, "метров по земле");
        });

        yield return ("движение (Г52): поворот колесом на 90° у 1000 пехоты — около 5 с, ход недобран", () =>
        {
            var geo = Open(800, 600);
            var m = Unit("infantry", 1, 200, 300, 0);
            double turned = -1;
            var log = Go(m, geo, 700, 300, 90, t => { if (turned < 0 && Math.Abs(MoveSim.AngleDiff(m.P.Facing, 90)) < 1e-6) turned = t; });
            double full = Math.PI / 2 * (125.0 / 2) / (3 * 100.0 / 15);   // дуга фланга ÷ (3 × марш) = 4,9 с
            Near(turned, full, 0.06, "весь поворот на 90°");
            // стоя — пока курс расходится с путём больше чем на 20°, последние 20° — уже на ходу
            Near(m.WheelSec, full * (90 - R.Move.MarchAlignDeg) / 90, 0.06, "секунд стоя на поворот");
            Near(m.P.Facing, 90, 1e-6, "смотрит на восток");
            // после поворота — с места на наибольшей скорости до конца хода: столько нормы и успевает
            double top = MoveSim.TopSpeed(m.P.U, R), ta = MoveSim.AccelSec(m.P.U, R);
            Near(m.Spent, top * (15 - m.WheelSec - ta / 2), 0.2, "нормы после поворота");
            True(log[0].Contains("недобрал"), "журнал говорит про недобор: " + log[0]);
        });

        yield return ("движение (Г52): цель сзади — кругом за 1 с, фигурки не сходят с мест", () =>
        {
            var geo = Open(600, 800);
            var m = Unit("infantry", 1, 300, 200, 0);
            var start = m.Figs.Select(f => (f.X, f.Y)).ToList();
            double moved = 0;
            Go(m, geo, 300, 700, 180, t =>
            {
                if (t <= 1 + 1e-9)
                    for (int k = 0; k < start.Count; k++) moved = Math.Max(moved, Math.Abs(m.Figs[k].X - start[k].X) + Math.Abs(m.Figs[k].Y - start[k].Y));
            });
            Near(m.AboutSec, 1, 1e-6, "секунд на разворот");
            Eq(m.WheelSec, 0.0, "колесом не поворачивал");
            True(moved < 0.01, $"за разворот фигурки сдвинулись на {moved:0.000} м");
            Near(m.P.Facing, 180, 1e-6, "смотрит на юг");
            True(m.Spent > 90, $"нормы {m.Spent:0.0} — разворот почти не стоит хода");
        });

        yield return ("движение (Г54): шаг вбок — без поворота, на половине скорости", () =>
        {
            var geo = Open(600, 600);
            var m = Unit("infantry", 1, 300, 300, 0);
            double maxTurn = 0, arrivedAt = -1;
            Go(m, geo, 275, 300, 0, t => { maxTurn = Math.Max(maxTurn, Math.Abs(MoveSim.AngleDiff(0, m.P.Facing))); if (m.OnSpot && arrivedAt < 0) arrivedAt = t; });
            True(m.Side, "ближний ход");
            True(m.Done, "дошёл");
            Eq(maxTurn, 0.0, "не поворачивался");
            double top = MoveSim.TopSpeed(m.P.U, R) * 0.5, acc = MoveSim.TopSpeed(m.P.U, R);   // разгон 1 с до полной скорости
            Near(arrivedAt, 25 / top + top / acc, 0.2, "время на 25 м боком");
        });

        yield return ("движение: фигурки встают на свои места и не заходят в воду", () =>
        {
            var geo = Open(600, 600);
            Terrain.PaintDisc(geo.Map, "t", 60, 60, 20, Terrain.Id("water"));
            var m = Unit("infantry", 1, 300, 560, 0);
            int wet = 0;
            MoveSim.Give(m, new MoveOrder { X = 300, Y = 40, Facing = 0 }, geo, R);
            for (int turn = 0; turn < 6; turn++)
                MoveSim.Turn(new[] { m }, geo, R, t =>
                {
                    foreach (var f in m.Figs) if (!m.Field.Passable(m.Field.CellOf(f.X, f.Y))) wet++;
                });
            Eq(wet, 0, "кадров с фигуркой в воде");
            True(m.Done, "дошёл в обход озера");
            True(SlotGap(m) < 0.05, $"фигурка дальше всех от места — {SlotGap(m):0.000} м");
        });

        yield return ("движение: одно и то же — один исход", () =>
        {
            double[] Run()
            {
                var map = MapGen.Generate("forest", new Dictionary<string, object>(), 777);
                var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
                var m = Unit("knights", 1, 400, 1200, 30);
                MoveSim.Give(m, new MoveOrder { X = 1500, Y = 300, Facing = 45 }, geo, R);
                for (int i = 0; i < 3; i++) MoveSim.Turn(new[] { m }, geo, R);
                return m.Figs.SelectMany(f => new[] { f.X, f.Y }).Append(m.P.X).Append(m.P.Y).ToArray();
            }
            Eq(Run().SequenceEqual(Run()), true, "повтор совпал");
        });
    }
}
