// ═══════════ Polygon.cs — полигон движения (Г55): показательные ходы для просмотра глазами ═══════════
// dotnet run --project Tests -- polygon → core/polygon/polygon.html (шаблон — Tests/polygon.html, данные — внутри файла).
// Сцены: марш и разгон, повороты (колесо, кругом, шаг вбок, назад), обход озера, леса и холма, река с бродом
// и мостом на сгенерированной карте. Запись — каждые 0,2 с: центр строя, курс, скорость нормы, все фигурки.
using System.Text.Json;
using BattleCore;

static class Polygon
{
    sealed class Scene
    {
        public string Name, Note;
        public Geo Geo;
        public int Turns;
        public List<(Mover M, MoveOrder O)> Units = new List<(Mover M, MoveOrder O)>();
        public void Add(string tpl, int id, string name, double x, double y, double facing, double tx, double ty, double tf, int faction = 1)
        {
            var m = MoveTests.Unit(tpl, id, x, y, facing, faction: faction);
            m.P.U.Name = name;
            Units.Add((m, new MoveOrder { X = tx, Y = ty, Facing = tf }));
        }
        // стоит без приказа (Order = null)
        public void Stand(string tpl, int id, string name, double x, double y, double facing, int faction = 1)
        {
            var m = MoveTests.Unit(tpl, id, x, y, facing, faction: faction);
            m.P.U.Name = name;
            Units.Add((m, null));
        }
    }

    static List<Scene> Scenes()
    {
        var list = new List<Scene>();

        var march = new Scene { Name = "Марш и разгон", Turns = 4, Geo = MoveTests.Open(1000, 700),
            Note = "Норма за ход — как за столом: пехота 100 м, пикинёры 80, конница 250. С места — разгон (пехота 1 с, пикинёры 1,5, конница 3), " +
                   "к концу хода — ровно норма; не дошёл — идёт дальше, не тормозя (Г53). У цели — торможение." };
        march.Add("infantry", 1, "Пехота", 150, 620, 0, 150, 270, 0);
        march.Add("pikemen", 2, "Пикинёры", 340, 620, 0, 340, 380, 0);
        march.Add("knights", 3, "Конные рыцари", 700, 640, 0, 700, 80, 0);
        list.Add(march);

        var turns = new Scene { Name = "Повороты", Turns = 3, Geo = MoveTests.Open(1200, 900),
            Note = "Колесо: строй поворачивается вокруг центра, фланги идут втрое быстрее марша — 90° у 1000 пехоты ≈ 5 с; последние 20° — на ходу (Г52). " +
                   "Кругом (больше 135°): каждый на месте за 1 с, задняя шеренга становится передней. Ближе трети нормы — без поворота: боком и назад на половине скорости (Г54)." };
        turns.Add("infantry", 1, "Колесо", 200, 250, 0, 500, 250, 90);
        turns.Add("infantry", 2, "Кругом и марш", 850, 200, 0, 850, 450, 180);
        turns.Add("infantry", 3, "Шаг вбок", 200, 650, 0, 175, 650, 0);
        turns.Add("infantry", 4, "Пятится", 500, 650, 0, 500, 675, 0);
        turns.Add("infantry", 5, "Кругом на месте", 900, 760, 0, 900, 760, 180);
        list.Add(turns);

        var ground = new Scene { Name = "Озеро, лес, холм", Turns = 6, Geo = MoveTests.Open(1000, 800),
            Note = "Путь — по карте направлений (дейкстра по клеткам 5 м, та же цена, что у зоны трекера), натянутый как нить. " +
                   "Озеро — обход; лес ×2 для пехоты и ×3 для конницы — конница обходит; подъём ×1,5 за уровень. " +
                   "Фигурка, которой путь к месту закрыт, идёт по карте направлений отряда; место в воде — встаёт у берега." };
        var g = ground.Geo.Map;
        Terrain.PaintDisc(g, "t", 100, 80, 15, Terrain.Id("water"));             // озеро 150 м
        Terrain.PaintRect(g, "t", 135, 25, 185, 75, Terrain.Id("forest"));       // лес 250 × 250 м
        Terrain.PaintDisc(g, "z", 40, 55, 22, 1); Terrain.PaintDisc(g, "z", 40, 55, 11, 2);   // холм в два уровня
        Terrain.PaintSegment(g, "t", 0, 140, 200, 140, 1, Terrain.Id("road"));
        ground.Add("infantry", 1, "Пехота у озера", 500, 680, 0, 500, 120, 0);
        ground.Add("knights", 2, "Конница у леса", 800, 720, 0, 800, 60, 0);
        ground.Add("infantry", 3, "Пехота на холм", 150, 700, 0, 200, 150, 0);
        list.Add(ground);

        var map = MapGen.Generate("river", new Dictionary<string, object> { ["widthM"] = 1200.0, ["depthM"] = 900.0, ["fords"] = 1.0, ["bridges"] = 1.0 }, 7);
        var river = new Scene { Name = "Река: брод и мост", Turns = 6, Geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) },
            Note = "Сгенерированная карта «Река», зерно 7. Глубокая вода непроходима; брод ×2; мост — как поле. Каждый ищет свою переправу по своей карте направлений." };
        river.Add("infantry", 1, "Пехота", 350, 820, 0, 350, 80, 0);
        river.Add("knights", 2, "Конница", 850, 830, 0, 850, 60, 0);
        list.Add(river);

        var jam = new Scene { Name = "Пробки и проход сквозь своих", Turns = 3, Geo = MoveTests.Open(1100, 850),
            Note = "Шаг 2: фигурки — твёрдые тела. Перекрёсток: «Первые» подошли раньше и идут, «Вторые» ждут и пропускают (Г57). " +
                   "Сквозь своих стоящих не проходят — «Идут» встают за «Стоят». Лучники отходят сквозь свою пехоту на половине скорости (Г56). Пробка — в журнале." };
        jam.Add("infantry", 1, "Первые", 300, 300, 90, 750, 300, 90);
        jam.Add("infantry", 2, "Вторые", 420, 470, 0, 420, 100, 0);
        jam.Stand("infantry", 3, "Стоят", 850, 560, 0);
        jam.Add("infantry", 4, "Идут", 850, 780, 0, 850, 400, 0);
        jam.Stand("infantry", 5, "Пехота", 150, 680, 0);
        jam.Add("archers", 6, "Лучники", 150, 620, 0, 150, 770, 0);
        list.Add(jam);

        var clash = new Scene { Name = "Встреча с врагом", Turns = 3, Geo = MoveTests.Open(1100, 750),
            Note = "Г58: тела упираются в контакте, никто никого не теснит — как за столом; стоящего врага не сдвигают. " +
                   "Рукопашной здесь ещё нет: бой и движение сойдутся в одном ходу отдельным шагом. Враги — с пометкой в имени." };
        clash.Add("infantry", 1, "Пехота", 150, 600, 0, 150, 120, 0);
        clash.Stand("infantry", 2, "Враг: пехота", 150, 300, 180, faction: 2);
        clash.Add("infantry", 3, "Идут навстречу", 480, 640, 0, 480, 100, 0);
        clash.Add("infantry", 4, "Враг: навстречу", 480, 110, 180, 480, 660, 180, faction: 2);
        clash.Add("knights", 5, "Рыцари", 820, 690, 0, 820, 60, 0);
        clash.Stand("pikemen", 6, "Враг: пикинёры", 820, 330, 180, faction: 2);
        list.Add(clash);
        return list;
    }

    public static void Write(string root)
    {
        var R = Rules.Base;
        var scenes = new List<object>();
        foreach (var sc in Scenes())
        {
            var ms = sc.Units.Select(u => u.M).ToList();
            foreach (var (m, o) in sc.Units) if (o != null) MoveSim.Give(m, o, sc.Geo, R);
            var frames = new List<double[][]> { Snap(ms) };
            var logs = new List<List<string>>();
            var stats = new List<object>();
            for (int turn = 0; turn < sc.Turns; turn++)
            {
                int k = 0;
                logs.Add(MoveSim.Turn(ms, sc.Geo, R, t => { if (++k % 4 == 0) frames.Add(Snap(ms)); }));
                stats.Add(ms.Select(m => new { spent = Math.Round(m.Spent, 1), moved = Math.Round(m.Moved, 1), wheel = Math.Round(m.WheelSec, 2), about = Math.Round(m.AboutSec, 2), done = m.Done }).ToList());
            }
            var gm = sc.Geo.Map;
            scenes.Add(new
            {
                name = sc.Name, note = sc.Note, w = sc.Geo.W, h = sc.Geo.H, cols = gm.W, rows = gm.H,
                t = Terrain.EncodeLayer(gm.T), z = Terrain.EncodeLayer(gm.Z),
                dt = 0.2, turnSec = R.Move.TurnSec, turns = sc.Turns,
                units = sc.Units.Select(u => new
                {
                    id = u.M.P.U.Id, name = u.M.P.U.Name, type = u.M.P.U.Type, men = u.M.P.U.Soldiers,
                    norm = BattleMap.UnitSpeed(u.M.P.U, R), front = u.M.P.Fp.Front, depth = u.M.P.Fp.Depth,
                    figs = u.M.P.Figs.Select(f => new[] { f.Width, f.Depth }).ToList(),
                    order = u.O == null ? null : new[] { u.O.X, u.O.Y, u.O.Facing },
                    route = u.M.Track?.Points.Select(p => new[] { Math.Round(p.x, 1), Math.Round(p.y, 1) }).ToList(),
                    flow = Flow(u.M.Field), note = u.M.Note,
                }).ToList(),
                frames, logs, stats,
            });
        }
        var json = JsonSerializer.Serialize(new { scenes, made = DateTime.Now.ToString("dd.MM.yyyy HH:mm") },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var tpl = File.ReadAllText(Path.Combine(root, "core", "Tests", "polygon.html"));
        var dir = Path.Combine(root, "core", "polygon");
        Directory.CreateDirectory(dir);
        var outp = Path.Combine(dir, "polygon.html");
        File.WriteAllText(outp, tpl.Replace("/*DATA*/null", json.Replace("</", "<\\/")));
        Console.WriteLine($"полигон: {scenes.Count} сцен → {outp} ({new FileInfo(outp).Length / 1024} КБ)");
    }

    // Кадр: по отряду [x, y, курс, скорость нормы, фигурка₀ x, y, фигурка₁ x, y, …], до 0,1 м
    static double[][] Snap(List<Mover> ms) => ms.Select(m =>
    {
        var a = new double[4 + 2 * m.Figs.Count];
        a[0] = Math.Round(m.P.X, 1); a[1] = Math.Round(m.P.Y, 1); a[2] = Math.Round(m.P.Facing, 1); a[3] = Math.Round(m.Vs, 2);
        for (int k = 0; k < m.Figs.Count; k++) { a[4 + 2 * k] = Math.Round(m.Figs[k].X, 1); a[5 + 2 * k] = Math.Round(m.Figs[k].Y, 1); }
        return a;
    }).ToArray();

    // Карта направлений строкой: на клетку — номер соседа 0…7 (как в FlowField), «T» — цель, «.» — не дойти
    static string Flow(FlowField f)
    {
        if (f == null) return null;
        var sb = new System.Text.StringBuilder(f.W * f.H);
        for (int i = 0; i < f.W * f.H; i++)
        {
            if (i == f.Target) { sb.Append('T'); continue; }
            int j = f.Next(i);
            if (j < 0) { sb.Append('.'); continue; }
            int dx = j % f.W - i % f.W, dy = j / f.W - i / f.W;
            sb.Append((char)('0' + (dy + 1) * 3 + (dx + 1) - ((dy + 1) * 3 + (dx + 1) > 4 ? 1 : 0)));
        }
        return sb.ToString();
    }
}
