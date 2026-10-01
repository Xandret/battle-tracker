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
    public static Mover Unit(string tpl, int id, double x, double y, double facing, double men = 1000, int faction = 1)
    {
        var t = Templates.Get(tpl);
        return Mover.Place(t.Make(id, t.Name, men, faction), x, y, facing, R);
    }
    static void Order(Mover m, Geo geo, double x, double y, double facing) => MoveSim.Give(m, new MoveOrder { X = x, Y = y, Facing = facing }, geo, R);
    // сколько сдвинулась самая беспокойная фигурка с начальных мест
    static Func<double> Watch(Mover m)
    {
        var start = m.Figs.Select(f => (f.X, f.Y)).ToList();
        return () => m.Figs.Select((f, k) => Math.Sqrt((f.X - start[k].X) * (f.X - start[k].X) + (f.Y - start[k].Y) * (f.Y - start[k].Y))).Max();
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
            // 6 ходов — дойти в обход озера, седьмой — собраться: фигурки твёрдые и протискиваются (шаг 2)
            for (int turn = 0; turn < 7; turn++)
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

        // ── шаг 2: тела, толкотня, уступание (Г56–Г58) ──
        yield return ("тела (Г57): на перекрёстке первым идёт тот, кто раньше подошёл; второй пропускает, сквозь не проходит", () =>
        {
            var geo = Open(900, 700);
            var a = Unit("infantry", 1, 300, 300, 90);    // уже у перекрёстка, идёт на восток
            var b = Unit("infantry", 2, 400, 470, 0);     // подходит снизу позже
            a.P.U.Name = "Первые"; b.P.U.Name = "Вторые";
            Order(a, geo, 700, 300, 90); Order(b, geo, 400, 100, 0);
            double worst = double.PositiveInfinity;
            var logs = new List<string>();
            for (int turn = 0; turn < 3; turn++)
            {
                logs.AddRange(MoveSim.Turn(new[] { a, b }, geo, R, t => worst = Math.Min(worst, Bodies.MinGap(a, b))));
                Near(a.Spent, turn < 2 ? 100 : a.Spent, 1e-6, $"ход {turn + 1}: первые идут без задержки");
                Eq(a.Blockers.Count, 0, $"ход {turn + 1}: первые никому не уступали");
            }
            True(worst > -0.3, $"тела перекрылись на {-worst:0.00} м");
            True(logs.Any(l => l.StartsWith("«Вторые»") && l.Contains("пропускал «Первые»")), "журнал: вторые пропускали первых — " + string.Join(" | ", logs));
        });

        yield return ("тела (Г57): подошли одновременно — первым идёт тот, у кого дисциплина выше", () =>
        {
            foreach (var (da, db) in new[] { (70.0, 50.0), (50.0, 70.0) })
            {
                var geo = Open(900, 900);
                var a = Unit("infantry", 1, 200, 400, 90); var b = Unit("infantry", 2, 400, 600, 0);
                a.P.U.Discipline = da; b.P.U.Discipline = db;
                Order(a, geo, 800, 400, 90); Order(b, geo, 400, 0, 0);
                for (int turn = 0; turn < 4; turn++) MoveSim.Turn(new[] { a, b }, geo, R);
                var (lead, wait) = da > db ? (a, b) : (b, a);
                True(wait.Rights.TryGetValue(lead.P.U.Id, out var w) && !w.mine, $"дисциплина {da}/{db}: уступает отряд с меньшей");
                True(lead.Rights.TryGetValue(wait.P.U.Id, out var l) && l.mine, $"дисциплина {da}/{db}: идёт отряд с большей");
            }
        });

        yield return ("тела (Г57): свой стоящий отряд — уже на месте: сквозь него не идут и не толкают", () =>
        {
            var geo = Open(600, 700);
            var stand = Unit("infantry", 1, 300, 250, 0); stand.P.U.Name = "Стоят";
            var go = Unit("infantry", 2, 300, 450, 0); go.P.U.Name = "Идут";
            var moved = Watch(stand);
            Order(go, geo, 300, 100, 0);
            var logs = new List<string>();
            double worst = double.PositiveInfinity;
            for (int turn = 0; turn < 3; turn++) logs.AddRange(MoveSim.Turn(new[] { stand, go }, geo, R, t => worst = Math.Min(worst, Bodies.MinGap(stand, go))));
            True(moved() < 0.2, $"стоящих сдвинули на {moved():0.00} м");
            True(worst > -0.3, $"перекрылись на {-worst:0.00} м");
            True(Bodies.MinGap(stand, go) < 3, $"идущие встали вплотную: зазор {Bodies.MinGap(stand, go):0.0} м");
            True(logs.Any(l => l.Contains("пропускал «Стоят»")), "журнал: пробка — " + string.Join(" | ", logs));
        });

        yield return ("тела (Г56): лучники отходят сквозь свою пехоту на половине скорости, пехоту не толкают", () =>
        {
            var geo = Open(600, 600);
            var inf = Unit("infantry", 1, 300, 300, 0);
            var arc = Unit("archers", 2, 300, 250, 0);
            var moved = Watch(inf);
            Order(arc, geo, 300, 360, 0);
            bool slowed = false;
            for (int turn = 0; turn < 3; turn++) MoveSim.Turn(new[] { inf, arc }, geo, R, t => slowed |= arc.Figs.Any(f => f.Slowed));
            True(arc.Done, "лучники дошли за пехоту");
            True(slowed, "в толще пехоты шли медленнее");
            Eq(arc.Blockers.Count, 0, "никому не уступали");
            True(moved() < 0.2, $"пехоту сдвинули на {moved():0.00} м");
        });

        yield return ("тела (Г58): упираются во врага и стоят, стоящего врага не теснят", () =>
        {
            var geo = Open(600, 700);
            var foe = Unit("infantry", 1, 300, 200, 180, faction: 2); foe.P.U.Name = "Враг";
            var go = Unit("infantry", 2, 300, 420, 0);
            var moved = Watch(foe);
            Order(go, geo, 300, 100, 0);
            var logs = new List<string>();
            double worst = double.PositiveInfinity;
            for (int turn = 0; turn < 3; turn++) logs.AddRange(MoveSim.Turn(new[] { foe, go }, geo, R, t => worst = Math.Min(worst, Bodies.MinGap(foe, go))));
            True(moved() < 0.2, $"врага сдвинули на {moved():0.00} м");
            True(worst > -0.3, $"перекрылись на {-worst:0.00} м");
            double gap = Bodies.MinGap(foe, go);
            True(gap >= -0.3 && gap <= R.Map.MeleeGap, $"в контакте — зазор {gap:0.0} м, «вплотную» до {R.Map.MeleeGap} м");
            True(logs.Any(l => l.Contains("упёрся во врага «Враг»")), "журнал: упёрся — " + string.Join(" | ", logs));
        });

        yield return ("тела: перепутанный строй собирается за секунды — места перераспределяются", () =>
        {
            var geo = Open(600, 600);
            var m = Unit("infantry", 1, 300, 300, 0);
            // колонны строя поменялись местами: левый край стоит справа, правый — слева
            int maxFile = m.P.Figs.Max(f => f.File);
            var pos = m.Figs.Select(f => (f.X, f.Y)).ToList();
            for (int k = 0; k < m.Figs.Count; k++)
            {
                var f = m.P.Figs[k];
                int mirror = m.P.Figs.FindIndex(g => g.Rank == f.Rank && g.File == maxFile - f.File && g.Width == f.Width && g.Men == f.Men);
                if (mirror >= 0) { m.Figs[k].X = pos[mirror].X; m.Figs[k].Y = pos[mirror].Y; }
            }
            double walked = 0;
            var last = m.Figs.ToDictionary(f => f, f => (f.X, f.Y));
            MoveSim.Turn(new[] { m }, geo, R, t =>
            {
                foreach (var f in m.Figs) { var p = last[f]; walked = Math.Max(walked, Math.Sqrt((f.X - p.X) * (f.X - p.X) + (f.Y - p.Y) * (f.Y - p.Y))); }
            });
            True(SlotGap(m) < 0.1, $"фигурка дальше всех от места — {SlotGap(m):0.00} м");
        });

        // ── шаг 3: узости, взгляд вперёд, обход (Г59–Г61) ──
        // ходить, пока все с приказом не встанут (не больше turns ходов); bad — проверка на каждом шаге
        List<string> Until(IList<Mover> ms, Geo geo, int turns, Action<double> each = null)
        {
            var logs = new List<string>();
            for (int i = 0; i < turns && ms.Any(x => x.Order != null && !x.Done); i++) logs.AddRange(MoveSim.Turn(ms, geo, R, each));
            return logs;
        }
        int Wet(Mover m) => m.Figs.Count(f => !m.Field.Passable(m.Field.CellOf(f.X, f.Y)));

        yield return ("узости (Г59): озеро строй обходит с запасом и не сужается — места вокруг хватает", () =>
        {
            var geo = Open(600, 600);
            Terrain.PaintDisc(geo.Map, "t", 60, 60, 20, Terrain.Id("water"));   // озеро 200 м, центр (302,5; 302,5)
            var m = Unit("infantry", 1, 300, 560, 0);
            Order(m, geo, 300, 40, 0);
            double near = double.PositiveInfinity;
            for (double s = 0; s <= m.Track.Length; s += 2.5)
            {
                var (x, y, _, _) = m.Track.AtMeters(s);
                near = Math.Min(near, Math.Sqrt((x - 302.5) * (x - 302.5) + (y - 302.5) * (y - 302.5)) - 100);
            }
            True(near > 45, $"центр строя у воды ближе {near:0.0} м — строй задевает берег (полфронта 62,5)");
            // у озера — пока центр строя на его высоте (у цели строй косо подходит к краю карты и может сузиться — это верно)
            int narrowest = m.NominalCols;
            Until(new[] { m }, geo, 9, t => { if (m.P.Y > 180 && m.P.Y < 420) narrowest = Math.Min(narrowest, m.Cols); });
            True(m.Done, "дошёл");
            Eq(narrowest, m.NominalCols, "колонн у озера");
        });

        yield return ("узости (Г59, Г60): мост 15 м — в колонну по 2, перестроение медленнее, за мостом — снова линия", () =>
        {
            var geo = Open(800, 700);
            Terrain.PaintRect(geo.Map, "t", 0, 56, 159, 63, Terrain.Id("water"));     // река 40 м поперёк
            Terrain.PaintRect(geo.Map, "t", 79, 56, 81, 63, Terrain.Id("bridge"));   // мост 15 м
            var m = Unit("infantry", 1, 400, 600, 0); m.P.U.Name = "Пехота";
            Order(m, geo, 400, 100, 0);
            int narrowest = m.NominalCols, wet = 0;
            var logs = Until(new[] { m }, geo, 12, t => { narrowest = Math.Min(narrowest, m.Cols); wet += Wet(m); });
            True(m.Done, "перешёл реку и встал: " + string.Join(" | ", logs));
            Eq(narrowest, 2, "колонна на мосту — по 2 фигурки");
            Eq(m.Cols, m.NominalCols, "за мостом — снова линия");
            Eq(wet, 0, "кадров с фигуркой в воде");
            True(logs.Any(l => l.Contains("колонна по 2")) && logs.Any(l => l.Contains("перестроение")), "журнал: колонна и перестроение — " + string.Join(" | ", logs));
        });

        yield return ("взгляд вперёд (Г59): между двумя близкими воротами колонна не разворачивается", () =>
        {
            var geo = Open(800, 800);
            foreach (int row in new[] { 60, 68 })   // две стены поперёк, ворота 10 м, между стенами 35 м
            {
                Terrain.PaintRect(geo.Map, "t", 0, row, 159, row, Terrain.Id("wall"));
                Terrain.PaintRect(geo.Map, "t", 79, row, 80, row, Terrain.Id("field"));
            }
            var m = Unit("infantry", 1, 400, 650, 0);
            Order(m, geo, 400, 100, 0);
            var seq = new List<int> { m.Cols };
            Until(new[] { m }, geo, 14, t => { if (m.Cols != seq[seq.Count - 1]) seq.Add(m.Cols); });
            True(m.Done, "прошёл обе стены");
            True(seq.Count == 3 && seq[1] <= 2 && seq[2] == m.NominalCols, "сузился один раз и развернулся один раз: " + string.Join(" → ", seq));
        });

        yield return ("мелкое препятствие (Г59): дом на пути — строй не делает крюк, фигурки огибают его с двух сторон", () =>
        {
            var geo = Open(800, 700);
            Terrain.PaintRect(geo.Map, "t", 79, 59, 80, 60, Terrain.Id("building"));   // дом 10 × 10 м прямо на пути
            var m = Unit("infantry", 1, 400, 560, 0);
            Order(m, geo, 400, 80, 0);
            double side = m.Track.Points.Max(p => Math.Abs(p.x - 400));
            True(side < 12, $"центр строя ушёл вбок на {side:0.0} м");
            int narrowest = m.NominalCols, inside = 0;
            Until(new[] { m }, geo, 8, t => { narrowest = Math.Min(narrowest, m.Cols); inside += Wet(m); });
            True(m.Done, "дошёл");
            Eq(narrowest, m.NominalCols, "перед домом не перестраивался");
            Eq(inside, 0, "кадров с фигуркой в доме");
            True(SlotGap(m) < 0.1, $"за домом сомкнулись: {SlotGap(m):0.00} м");
        });

        yield return ("обход своих (Г61): свой стоит на пути — ждёт 3 с и обходит; стоящего не толкают", () =>
        {
            var geo = Open(900, 800);
            var stand = Unit("infantry", 1, 450, 400, 0); stand.P.U.Name = "Стоят";
            var go = Unit("infantry", 2, 450, 600, 0); go.P.U.Name = "Идут";
            var moved = Watch(stand);
            Order(go, geo, 450, 150, 0);
            double worst = double.PositiveInfinity;
            var logs = Until(new[] { stand, go }, geo, 10, t => worst = Math.Min(worst, Bodies.MinGap(stand, go)));
            True(go.Done, "дошли в обход: " + string.Join(" | ", logs));
            True(logs.Any(l => l.Contains("обошёл «Стоят»")), "журнал: обход — " + string.Join(" | ", logs));
            True(moved() < 0.3, $"стоящих сдвинули на {moved():0.00} м");
            True(worst > -0.3, $"перекрылись на {-worst:0.00} м");
        });

        yield return ("обход своих (Г61): встречные лоб в лоб — уступающий обходит, оба доходят", () =>
        {
            var geo = Open(900, 900);
            var a = Unit("infantry", 1, 450, 700, 0); a.P.U.Name = "Север";
            var b = Unit("infantry", 2, 450, 200, 180); b.P.U.Name = "Юг";
            Order(a, geo, 450, 150, 0); Order(b, geo, 450, 750, 180);
            double worst = double.PositiveInfinity;
            var logs = Until(new[] { a, b }, geo, 14, t => worst = Math.Min(worst, Bodies.MinGap(a, b)));
            True(a.Done && b.Done, "оба на месте: " + string.Join(" | ", logs));
            True(logs.Any(l => l.Contains("обошёл")), "журнал: кто-то обходил — " + string.Join(" | ", logs));
            True(worst > -0.5, $"перекрылись на {-worst:0.00} м");
        });

        yield return ("тела: несколько отрядов — одно и то же, один исход", () =>
        {
            double[] Run()
            {
                var geo = Open(900, 900);
                var ms = new[] { Unit("infantry", 1, 200, 400, 90), Unit("knights", 2, 450, 750, 0), Unit("archers", 3, 600, 300, 270), Unit("infantry", 4, 450, 150, 180, faction: 2) };
                Order(ms[0], geo, 800, 400, 90); Order(ms[1], geo, 450, 100, 0); Order(ms[2], geo, 150, 300, 270);
                for (int i = 0; i < 3; i++) MoveSim.Turn(ms, geo, R);
                return ms.SelectMany(m => m.Figs.SelectMany(f => new[] { f.X, f.Y })).ToArray();
            }
            Eq(Run().SequenceEqual(Run()), true, "повтор совпал");
        });
    }
}
