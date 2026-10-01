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
        public Dictionary<Mover, string> Tpl = new Dictionary<Mover, string>();   // шаблон — облик бойцов на рисунке
        public void Add(string tpl, int id, string name, double x, double y, double facing, double tx, double ty, double tf, int faction = 1, double men = 1000)
        {
            var m = MoveTests.Unit(tpl, id, x, y, facing, men, faction);
            m.P.U.Name = name;
            Units.Add((m, new MoveOrder { X = tx, Y = ty, Facing = tf }));
            Tpl[m] = tpl;
        }
        // бой (БД1): отряды живут в Battle, приказы — через Battle.Order; ходы считает Battle.Turn
        public Battle Battle;
        public Mover Fighter(string tpl, int id, string name, double x, double y, double facing, int faction = 1, double men = 1000)
        {
            var t = Templates.Get(tpl);
            var m = Battle.Add(t.Make(id, name, men, faction), x, y, facing);
            Units.Add((m, null)); Tpl[m] = tpl;
            return m;
        }
        public void Order(Mover m, MoveOrder o)
        {
            Battle.Order(m, o);
            int i = Units.FindIndex(u => u.M == m); Units[i] = (m, o);
        }
        // стоит без приказа (Order = null)
        public void Stand(string tpl, int id, string name, double x, double y, double facing, int faction = 1)
        {
            var m = MoveTests.Unit(tpl, id, x, y, facing, faction: faction);
            m.P.U.Name = name;
            Units.Add((m, null));
            Tpl[m] = tpl;
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

        var narrows = new Scene { Name = "Узости: мост и ворота", Turns = 10, Geo = MoveTests.Open(1200, 900),
            Note = "Шаг 3 (Г59, Г60): где проход уже строя, строй заранее складывается в колонну и за узостью разворачивается обратно; " +
                   "пока перестраивается — идёт вдвое медленнее. Между двумя близкими воротами колонна не разворачивается — взгляд вперёд. " +
                   "Слева — река с мостом 15 м, справа — две стены с воротами 10 м в 35 м друг от друга." };
        var nm = narrows.Geo.Map;
        Terrain.PaintRect(nm, "t", 0, 70, 119, 77, Terrain.Id("water"));
        Terrain.PaintRect(nm, "t", 59, 70, 61, 77, Terrain.Id("bridge"));
        foreach (int row in new[] { 70, 78 })
        {
            Terrain.PaintRect(nm, "t", 120, row, 239, row, Terrain.Id("wall"));
            Terrain.PaintRect(nm, "t", 179, row, 180, row, Terrain.Id("field"));
        }
        Terrain.PaintRect(nm, "t", 120, 71, 239, 77, Terrain.Id("water"));   // за стенами — ров: обойти нельзя
        Terrain.PaintRect(nm, "t", 179, 71, 180, 77, Terrain.Id("field"));
        narrows.Add("infantry", 1, "Через мост", 300, 780, 0, 300, 120, 0);
        narrows.Add("infantry", 2, "Через ворота", 900, 800, 0, 900, 110, 0);
        list.Add(narrows);

        var around = new Scene { Name = "Обход: дом и свои", Turns = 8, Geo = MoveTests.Open(1100, 800),
            Note = "Шаг 3 (Г59, Г61): дом на пути строй не обходит — фигурки огибают его с двух сторон и смыкаются за ним. " +
                   "Свой стоит на пути — ждут 3 с, вдруг пройдёт, и обходят. Встречные свои лоб в лоб — обходит тот, кто уступает по очереди (Г57)." };
        Terrain.PaintRect(around.Geo.Map, "t", 39, 79, 40, 80, Terrain.Id("building"));
        around.Add("infantry", 1, "Мимо дома", 200, 650, 0, 200, 100, 0);
        around.Stand("infantry", 2, "Стоят", 550, 400, 0);
        around.Add("infantry", 3, "Идут", 550, 650, 0, 550, 100, 0);
        around.Add("infantry", 4, "Север", 900, 650, 0, 900, 120, 0);
        around.Add("infantry", 5, "Юг", 900, 150, 180, 900, 680, 180);
        list.Add(around);

        // Облик (Г32): по отряду каждого шаблона, по 200 человек — приблизь, чтобы разглядеть бойцов
        var parade = new Scene { Name = "Рода войск", Turns = 3, Geo = MoveTests.Open(1100, 600),
            Note = "Облик фигурок (Г32): фигурка 5 × 2 — десять человечков сверху; щит, капюшон или попона — цвет отряда. " +
                   "Пикинёры опускают пики в первых четырёх шеренгах, задние держат стоймя. Издали фигурка снова плашка. Рисунки свои (Г10)." };
        string[][] kinds = {
            new[] { "militia", "Ополчение" }, new[] { "infantry", "Пехота" }, new[] { "guard", "Гвардия" }, new[] { "pikemen", "Пикинёры" },
            new[] { "archers", "Лучники" }, new[] { "crossbowmen", "Арбалетчики" }, new[] { "knights", "Конные рыцари" }, new[] { "elite_cavalry", "Элитная конница" },
        };
        for (int k = 0; k < kinds.Length; k++)
        {
            double x = 80 + k * 130;
            parade.Add(kinds[k][0], k + 1, kinds[k][1], x, 500, 0, x, 300, 0, men: 200);
        }
        list.Add(parade);
        // ── бой в движении (БД1) ──
        var hit = new Scene { Name = "Бой: натиск и стычки", Turns = 3, Geo = MoveTests.Open(1100, 800),
            Note = "БД1 (Г62, Г29, К29): касание — обмен ударами по формуле стола, бой идёт через границу хода без перерыва. " +
                   "Рыцари с разбега 150 м — натиск: всплеск урона в первые 2 с, пехота в это время не отвечает. По пикам во фронт натиска нет, " +
                   "а ответ пик по коннице ×3. Пехота сходится с пехотой посреди хода. Павшие фигурки — серые крестики, строй смыкается с краёв (Г30)." };
        hit.Battle = new Battle(hit.Geo, R0, new EngineContext { Rng = new Mulberry32(5).Next });
        hit.Fighter("infantry", 2, "Враг: пехота", 200, 520, 0, faction: 2);
        var k1 = hit.Fighter("knights", 1, "Рыцари", 200, 520 - (4 + 150 + 7.5), 180);
        hit.Fighter("pikemen", 4, "Враг: пикинёры", 650, 520, 0, faction: 2);
        var k2 = hit.Fighter("knights", 3, "Рыцари на пики", 650, 520 - (5 + 150 + 7.5), 180);
        hit.Fighter("infantry", 6, "Враг: пехота Б", 980, 520, 0, faction: 2);
        var i1 = hit.Fighter("infantry", 5, "Пехота", 980, 520 - (4 + 60 + 4), 180);
        hit.Order(k1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
        hit.Order(k2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4, Charge = true });
        hit.Order(i1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
        list.Add(hit);

        var flank = new Scene { Name = "Бой: фланг и потери", Turns = 3, Geo = MoveTests.Open(1000, 700),
            Note = "Г63: атакованный во фланг сам не поворачивается — ждёт приказа, удар во фланг глушит его ответ. " +
                   "Г44: начинает атакующий. Гвардия (дисциплина 80+: 2 удара за круг) рубит ополчение — фигурки падают, фронт сужается как у фишки трекера (Г30)." };
        flank.Battle = new Battle(flank.Geo, R0, new EngineContext { Rng = new Mulberry32(7).Next });
        flank.Fighter("infantry", 2, "Враг: стоят", 300, 400, 0, faction: 2);
        var fl = flank.Fighter("infantry", 1, "Во фланг", 300 - 62.5 - 4 - 40, 400, 90);
        flank.Fighter("militia", 4, "Враг: ополчение", 750, 400, 0, faction: 2);
        var gd = flank.Fighter("guard", 3, "Гвардия", 750, 400 - (4 + 30 + 4), 180);
        flank.Order(fl, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        flank.Order(gd, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
        list.Add(flank);

        var shoot = new Scene { Name = "Бой: стрельба", Turns = 3, Geo = MoveTests.Open(1300, 800),
            Note = "БД2 (Г65, Г66, Г67): стрелки бьют только по приказу и отвечают, если по ним стреляют. Каждая стрела летит сама и задевает того, " +
                   "кого встретит; бойцы идут вместе со своими фигурками. По идущей цели — с упреждением и ошибкой. Павшие остаются лежать, под ними — кровь, " +
                   "брызги — по направлению удара. Слева — лучники по стоящей пехоте, посередине — перестрелка, справа — лучники встречают наступающую пехоту." };
        shoot.Battle = new Battle(shoot.Geo, R0, new EngineContext { Rng = new Mulberry32(9).Next });
        shoot.Fighter("infantry", 2, "Враг: пехота", 220, 520, 0, faction: 2);
        var s1 = shoot.Fighter("archers", 1, "Лучники", 220, 520 - (4 + 100 + 2.5), 180);
        shoot.Fighter("archers", 4, "Враг: стрелки", 650, 520, 0, faction: 2);
        var s2 = shoot.Fighter("archers", 3, "Стрелки", 650, 520 - (2.5 + 120 + 2.5), 180);
        var s3 = shoot.Fighter("archers", 5, "Лучники Б", 1080, 330, 0);
        var foe = shoot.Fighter("infantry", 6, "Враг: наступают", 1080, 330 - (2.5 + 190 + 4), 180, faction: 2);
        shoot.Order(s1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        shoot.Order(s2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
        shoot.Order(s3, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
        shoot.Order(foe, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
        list.Add(shoot);

        var wrap = new Scene { Name = "Бой: охват и двое на одного", Turns = 3, Geo = MoveTests.Open(1100, 800),
            Note = "БД3 (Г68, Г69). Слева — рыцари шире пехоты: свисающие колонны сами огибают её углы и бьют во фланг и в тыл, " +
                   "колонна идёт целиком, голова — к врагу; мест не хватило — встают второй линией. Справа — двое на одного: " +
                   "колонны «Стоят» делятся между врагами (каждая бьёт того, кого касается), ответ — один на круг на всех: " +
                   "первый ударивший его получает, второй — нет; удар во фланг ответа не даёт вовсе." };
        wrap.Battle = new Battle(wrap.Geo, R0, new EngineContext { Rng = new Mulberry32(11).Next });
        wrap.Fighter("infantry", 2, "Враг: пехота", 260, 450, 0, faction: 2);
        var kn = wrap.Fighter("knights", 1, "Рыцари", 260, 450 - (4 + 40 + 7.5), 180);
        wrap.Fighter("infantry", 5, "Враг: стоят", 780, 450, 0, faction: 2);
        var w1 = wrap.Fighter("infantry", 3, "В лоб", 780, 450 - (4 + 40 + 4), 180, men: 500);
        var w2 = wrap.Fighter("infantry", 4, "Во фланг", 780 + 62.5 + 4 + 60, 450, 270, men: 500);
        wrap.Order(kn, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
        wrap.Order(w1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
        wrap.Order(w2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
        list.Add(wrap);
        return list;
    }
    static readonly Rules R0 = Rules.Base;

    public static void Write(string root)
    {
        var R = Rules.Base;
        var scenes = new List<object>();
        foreach (var sc in Scenes())
        {
            var ms = sc.Units.Select(u => u.M).ToList();
            if (sc.Battle == null) foreach (var (m, o) in sc.Units) if (o != null) MoveSim.Give(m, o, sc.Geo, R);
            var frames = new List<double[][]> { Snap(ms) };
            var heads = new List<List<double[]>> { Heads(ms) };   // фигурки, развёрнутые не по строю (охват, Г68)
            // каким отряд был до первого хода — численность, строй, фигурки (в бою они меняются, Г30)
            var start = ms.Select(m => new
            {
                men = m.P.U.Soldiers, front = m.P.Fp.Front, depth = m.P.Fp.Depth,
                figs = m.P.Figs.Select(f => new[] { f.Width, f.Depth, f.Men, f.Rank }).ToList(),
            }).ToList();
            // бой (БД1): численность по кадрам и где упали фигурки — [x, y, номер кадра]
            var soldiers = new List<double[]> { ms.Select(m => Math.Round(m.P.U.Soldiers)).ToArray() };
            var fallen = ms.Select(_ => new List<double[]>()).ToList();
            // павшие поимённо (Г67): [x, y, кадр, отряд, куда смотрел, откуда удар, часть тела 0 голова 1 корпус 2 ноги 3 конь]
            var dead = new List<double[]>();
            var idx = ms.Select((m, i) => (m.P.U.Id, i)).ToDictionary(p => p.Id, p => p.i);
            int seenDead = 0;
            var logs = new List<List<string>>();
            var stats = new List<object>();
            for (int turn = 0; turn < sc.Turns; turn++)
            {
                int k = 0;
                Action<double> rec = t =>
                {
                    if (++k % 4 != 0) return;
                    frames.Add(Snap(ms));
                    heads.Add(Heads(ms));
                    soldiers.Add(ms.Select(m => Math.Round(m.P.U.Soldiers)).ToArray());
                    if (sc.Battle != null)
                        for (; seenDead < sc.Battle.Deaths.Count; seenDead++)
                        {
                            var d = sc.Battle.Deaths[seenDead];
                            int part = d.Part == "head" ? 0 : d.Part == "legs" ? 2 : d.Part == "horse" ? 3 : 1;
                            dead.Add(new[] { Math.Round(d.X, 2), Math.Round(d.Y, 2), frames.Count - 1, idx[d.UnitId], Math.Round(d.Facing), Math.Round(d.Dir), part });
                        }
                    for (int i = 0; i < ms.Count; i++)
                        while (fallen[i].Count < ms[i].Fallen.Count)
                        {
                            var p = ms[i].Fallen[fallen[i].Count];
                            fallen[i].Add(new[] { Math.Round(p.x, 1), Math.Round(p.y, 1), frames.Count - 1 });
                        }
                };
                logs.Add(sc.Battle != null ? sc.Battle.Turn(rec) : MoveSim.Turn(ms, sc.Geo, R, rec));
                stats.Add(ms.Select(m => new { spent = Math.Round(m.Spent, 1), moved = Math.Round(m.Moved, 1), wheel = Math.Round(m.WheelSec, 2), about = Math.Round(m.AboutSec, 2), done = m.Done }).ToList());
            }
            var gm = sc.Geo.Map;
            scenes.Add(new
            {
                name = sc.Name, note = sc.Note, w = sc.Geo.W, h = sc.Geo.H, cols = gm.W, rows = gm.H,
                t = Terrain.EncodeLayer(gm.T), z = Terrain.EncodeLayer(gm.Z),
                dt = 0.2, turnSec = R.Move.TurnSec, turns = sc.Turns,
                units = sc.Units.Select((u, ui) => new
                {
                    id = u.M.P.U.Id, name = u.M.P.U.Name, type = u.M.P.U.Type, men = start[ui].men, tpl = sc.Tpl[u.M],
                    norm = BattleMap.UnitSpeed(u.M.P.U, R), front = start[ui].front, depth = start[ui].depth,
                    // фигурка: ширина, глубина, бойцов, ряд квадратиков (0 — передний); шаг бойца в строю — pm × rd
                    figs = start[ui].figs,
                    pm = FormationOf(u.M.P.U, R).PerMan, rd = FormationOf(u.M.P.U, R).RankDepth,
                    order = u.O == null ? null : new[] { u.O.X, u.O.Y, u.O.Facing },
                    route = u.M.Track?.Points.Select(p => new[] { Math.Round(p.x, 1), Math.Round(p.y, 1) }).ToList(),
                    flow = Flow(u.M.Field), note = u.M.Note,
                }).ToList(),
                frames, logs, stats,
                soldiers = sc.Battle != null ? soldiers : null, fallen = sc.Battle != null ? fallen : null, dead = sc.Battle != null ? dead : null,
                heads = sc.Battle != null ? heads : null,
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

    static Rules.FormationR FormationOf(Unit u, Rules r) =>
        r.Map.Formation.TryGetValue(u.Type, out var f) ? f : r.Map.Formation["infantry"];

    // Кадр: по отряду [x, y, курс, скорость нормы, фигурка₀ x, y, фигурка₁ x, y, …], до 0,1 м
    static double[][] Snap(List<Mover> ms) => ms.Select(m =>
    {
        var a = new double[4 + 2 * m.Figs.Count];
        a[0] = Math.Round(m.P.X, 1); a[1] = Math.Round(m.P.Y, 1); a[2] = Math.Round(m.P.Facing, 1); a[3] = Math.Round(m.Vs, 2);
        for (int k = 0; k < m.Figs.Count; k++) { a[4 + 2 * k] = Math.Round(m.Figs[k].X, 1); a[5 + 2 * k] = Math.Round(m.Figs[k].Y, 1); }
        return a;
    }).ToArray();

    // Курсы фигурок, развёрнутых не по строю (охват, Г68): [отряд, фигурка, курс°] — ось тела, повёрнутая
    // в ту сторону, куда фигурка смотрит (к врагу в охвате, по строю — когда возвращается)
    static List<double[]> Heads(List<Mover> ms)
    {
        var list = new List<double[]>();
        for (int i = 0; i < ms.Count; i++)
            for (int k = 0; k < ms[i].Figs.Count; k++)
            {
                var s = ms[i].Figs[k];
                if (!s.Turned) continue;
                double want = s.Wrap ? s.WH : ms[i].P.Facing, a = s.Axis;
                if (Math.Abs(MoveSim.AngleDiff(a, want)) > 90) a += 180;
                list.Add(new[] { i, k, Math.Round(MoveSim.Norm(a), 1) });
            }
        return list;
    }

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
