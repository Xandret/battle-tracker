// ═══════════ PlayScenarios.cs — битвы для режима игры (И2) ═══════════
// Учебное поле: два войска по пять отрядов, холм, лес, ручей с бродом — проверить приказы и ход.
// Сохранения трекера (armiya_hodN.txt в game/Saves или на рабочем столе): армии и расстановка Алекса на его карте —
// загрузка та же, что у смотрелки (SaveScene): стороны по логу боёв, бежавшие — снова в строю, приказов нет.
// «Вторая битва при Пикшарпе» (Г6) — следующим шагом, когда сложится масштаб и засада.
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;
using Journal.Viewer;

namespace Journal.Play
{
    public sealed class PlayBattle
    {
        public string Name, Note;
        public Geo Geo;
        public Battle Battle;
        public BattleSession Session;
        public readonly Dictionary<Mover, string> Tpl = new Dictionary<Mover, string>();   // шаблон — облик бойцов
        public readonly Dictionary<Mover, double> StartMen = new Dictionary<Mover, double>();
        public readonly Dictionary<Mover, string> Color = new Dictionary<Mover, string>();  // цвет фракции (сохранение); нет — оттенок стороны
        public readonly Dictionary<Mover, string> Style = new Dictionary<Mover, string>();  // стиль облика (В16, сохранение); нет — западный
        public byte[] Image;                                                                // картинка карты (сохранение); null — земля по клеткам
    }

    public static class PlayScenarios
    {
        static readonly Rules R = Rules.Base;

        // меню битв: готовые битвы (имя, подпись, как построить); сохранения трекера — отдельно (Saves), через выбор состава
        public static List<(string Name, string Note, Func<PlayBattle> Make)> All() =>
            new List<(string, string, Func<PlayBattle>)> { ("Учебное поле", "5 на 5 через ручей с двумя бродами; холм, лес", () => Training()) };

        // сохранения трекера: сначала открытые недавно, потом найденные в game/Saves, на рабочем столе и рядом с открытыми
        public static List<string> Saves()
        {
            var recent = FileDialog.Recent();
            var found = SaveScene.Find(recent.Select(System.IO.Path.GetDirectoryName));
            return recent.Concat(found).GroupBy(p => System.IO.Path.GetFullPath(p).ToLowerInvariant()).Select(g => g.First()).ToList();
        }

        // битва из сохранения; include — номера отрядов, что выйдут на поле (малый состав), null — все
        public static PlayBattle FromSave(string path, ICollection<int> include = null)
        {
            var sc = SaveScene.Make(path, 99, include: include);
            var pb = new PlayBattle { Name = sc.Name, Note = sc.Note, Geo = sc.Geo, Battle = sc.Battle, Image = sc.Image };
            pb.Session = new BattleSession(pb.Battle);
            foreach (var kv in sc.SideNames) pb.Session.SideNames[kv.Key] = kv.Value;
            foreach (var (m, _) in sc.Units)
            {
                pb.Tpl[m] = sc.Tpl[m]; pb.StartMen[m] = m.P.U.Soldiers;
                if (sc.Color.TryGetValue(m, out var c) && c != null) pb.Color[m] = c;
                if (sc.Style.TryGetValue(m, out var st) && st != null) pb.Style[m] = st;
            }
            return pb;
        }

        // ── своя армия против армии: файлы армий (формат трекера, редактор «Армии»), по фракции на сторону ──
        // Отряды — какие отмечены (null — все в строю у фракции); стороны движку — 1 и 2 (как у сохранения: FactionId —
        // сторона), номера отрядов второй стороны сдвинуты, чтобы не совпали с первой. Расстановка — сама: в линию лицом
        // к врагу, пехота в центре, конница по флангам, стрелки на 70 м впереди; не влезает в ширину карты — следующая
        // линия позади. Между армиями ~520 м. Карта — шаблон генератора (поле, лес, холмы, река, пустыня).
        public sealed class ArmyPick { public string Path; public int? FactionId; public ICollection<int> Units; }

        public static PlayBattle FromArmies(ArmyPick a, ArmyPick b, string mapId, uint seed)
        {
            var picks = new[] { a, b };
            var sides = new List<(Unit u, string tpl, string style, string color)>[2];
            var names = new string[2];
            var cmdrs = new Dictionary<int, Commander>();   // полководцы обеих сторон (номера второй сдвинуты, как у отрядов)
            for (int k = 0; k < 2; k++)
            {
                var f = Journal.Armies.ArmyFile.Load(picks[k].Path);
                foreach (var c in f.Commanders) { var cm = Journal.Viewer.SaveScene.CommanderOf(c); cm.Id += k * 100000; cm.FactionId = k + 1; cmdrs[cm.Id] = cm; }
                var fac = f.Faction(picks[k].FactionId);
                names[k] = (string)fac?["name"] ?? "Без фракции";
                string color = (string)fac?["color"];
                sides[k] = new List<(Unit, string, string, string)>();
                foreach (var e in f.UnitsOf(picks[k].FactionId))
                {
                    var u = SaveScene.UnitOf(e);
                    if (u.Status == "destroyed" || u.Soldiers < 1) continue;
                    if (picks[k].Units != null && !picks[k].Units.Contains(u.Id)) continue;
                    u.Status = "active"; u.Broken = false;
                    u.Id += k * 100000; u.FactionId = k + 1; if (u.CommanderId.HasValue) u.CommanderId += k * 100000;
                    sides[k].Add((u, Journal.Art.KitSets.TplOf(Journal.Armies.ArmyFile.KitOf(e)), Journal.Armies.ArmyFile.StyleOf(e), color));
                }
                if (sides[k].Count == 0) throw new InvalidOperationException($"у стороны «{names[k]}» нет отрядов в строю");
            }
            // ширина карты — под самую широкую линию, но не уже шаблона
            double widest = sides.Max(L => L.Sum(x => Formation.Of(x.u, R).Front + 25));
            var input = new Dictionary<string, object> { ["widthM"] = Math.Max(1600, Math.Min(7000, widest + 500)), ["depthM"] = 1300.0 };
            // карта: шаблон движка или файл редактора карт («file:путь», Г102) — тогда её размер как есть
            bool fromFile = mapId.StartsWith("file:");
            var mapDoc = fromFile ? Journal.Maps.MapDoc.FromJson(System.IO.File.ReadAllText(mapId.Substring(5))) : null;
            var map = fromFile ? mapDoc.Bake() : MapGen.Generate(mapId, input, seed);
            var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
            var tmpl = fromFile ? new MapTemplate { Name = mapDoc.Name } : MapGen.Get(mapId);
            var pb = new PlayBattle
            {
                Name = $"{names[0]} против {names[1]}", Geo = geo,
                Note = $"Карта «{tmpl?.Name ?? mapId}» {geo.W:0} × {geo.H:0} м, зерно {seed}. Армии расставлены сами: пехота в центре, конница по флангам, стрелки впереди.",
                Battle = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(seed).Next,
                    CommanderOf = u => u.CommanderId.HasValue && cmdrs.TryGetValue(u.CommanderId.Value, out var cm) ? cm : null }),
            };
            pb.Session = new BattleSession(pb.Battle);
            pb.Session.SideNames[1] = names[0]; pb.Session.SideNames[2] = names[1];
            for (int k = 0; k < 2; k++) Deploy(pb, sides[k], k + 1);
            return pb;
        }

        static void Deploy(PlayBattle pb, List<(Unit u, string tpl, string style, string color)> list, int side)
        {
            double W = pb.Geo.W, H = pb.Geo.H, dir = side == 1 ? 1 : -1, facing = side == 1 ? 0 : 180;
            bool Ranged(Unit u) => u.Weapon == "ranged" || u.Type == "archer";
            var ranged = list.Where(x => Ranged(x.u)).ToList();
            var cav = list.Where(x => !Ranged(x.u) && x.u.Type == "cavalry").ToList();
            var foot = list.Where(x => !Ranged(x.u) && x.u.Type != "cavalry").OrderByDescending(x => x.u.Soldiers).ToList();
            // главная линия: половина конницы, пехота, другая половина конницы
            var main = cav.Where((_, i) => i % 2 == 0).Concat(foot).Concat(cav.Where((_, i) => i % 2 == 1)).ToList();
            // между армиями ~520 м; на мелкой карте (редактор, Г102) — ближе, чтобы обе влезли
            double off = Math.Min(260, H * 0.36);
            Line(pb, main, H / 2 + dir * off, dir, facing, side);
            Line(pb, ranged, H / 2 + dir * (off - 70), dir, facing, side);
        }

        // ряд отрядов по центру карты на высоте y; не влез — следующий ряд дальше от врага
        static void Line(PlayBattle pb, List<(Unit u, string tpl, string style, string color)> row, double y, double dir, double facing, int side)
        {
            double W = pb.Geo.W, gap = 25, maxW = W - 200;
            int i = 0;
            while (i < row.Count)
            {
                var take = new List<(Unit u, string tpl, string style, string color)>(); double w = 0, depth = 0;
                while (i < row.Count)
                {
                    var fp = Formation.Of(row[i].u, R);
                    if (take.Count > 0 && w + gap + fp.Front > maxW) break;
                    w += (take.Count > 0 ? gap : 0) + fp.Front; depth = Math.Max(depth, fp.Depth); take.Add(row[i]); i++;
                }
                double x = W / 2 - w / 2;
                foreach (var t in take)
                {
                    var fp = Formation.Of(t.u, R);
                    double cx = x + fp.Front / 2, cy = y;
                    for (int s = 0; s < 12 && Blocked(pb.Geo, cx, cy); s++) cy += dir * 20;   // в воде или скалах — сдвинуть назад
                    var m = pb.Battle.Add(t.u, cx, cy, facing);
                    pb.Battle.Order(m, new MoveOrder { Kind = OrderKind.Hold });
                    pb.Tpl[m] = t.tpl; pb.StartMen[m] = t.u.Soldiers;
                    if (t.style != null) pb.Style[m] = t.style;
                    if (t.color != null) pb.Color[m] = t.color;
                    x += fp.Front + gap;
                }
                y += dir * (depth + 30);
            }
        }

        // непроходимая клетка (вода, скалы, стены, здания) — по правилам местности
        static bool Blocked(Geo g, double x, double y)
        {
            var m = g.Map; int cx = (int)(x / m.Cell), cy = (int)(y / m.Cell);
            if (cx < 0 || cy < 0 || cx >= m.W || cy >= m.H) return false;
            int t = m.T[cy * m.W + cx]; if (t == 0) t = 1;
            return Terrain.ById.TryGetValue(t, out var tt) && R.Map.Terrain.TryGetValue(tt.Key, out var tr) && tr.Move == null;
        }

        public static PlayBattle Training(uint seed = 2026)
        {
            var map = Terrain.Create(1600, 1100, Terrain.Id("field"));
            Terrain.PaintDisc(map, "z", 230, 64, 26, 1); Terrain.PaintDisc(map, "z", 230, 64, 13, 2);   // холм в два уровня у Дарлтонов
            Terrain.PaintRect(map, "t", 40, 40, 90, 90, Terrain.Id("forest"));                       // лес 250 × 250 м
            Terrain.PaintSegment(map, "t", 0, 116, 320, 104, 1, Terrain.Id("water"));                  // ручей поперёк поля
            Terrain.PaintSegment(map, "t", 140, 111, 156, 110, 1, Terrain.Id("ford"));                 // брод
            Terrain.PaintSegment(map, "t", 236, 107, 252, 106, 1, Terrain.Id("ford"));                 // второй брод
            var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
            var pb = new PlayBattle
            {
                Name = "Учебное поле", Geo = geo,
                Note = "Два войска через ручей с двумя бродами. Холм — у Дарлтонов, лес — на левом крыле Пикшарпа.",
                Battle = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(seed).Next }),
            };
            pb.Session = new BattleSession(pb.Battle);
            pb.Session.SideNames[1] = "Пикшарп"; pb.Session.SideNames[2] = "Дарлтоны";
            // Пикшарп — снизу, лицом на север
            Add(pb, "infantry", 1, "Пехота Арнора", 620, 900, 0, 1);
            Add(pb, "infantry", 2, "Пехота Арнора II", 820, 900, 0, 1);
            Add(pb, "archers", 3, "Лучники Арнора", 720, 990, 0, 1);
            Add(pb, "knights", 4, "Королевская стража", 1150, 930, 0, 1);
            Add(pb, "pikemen", 5, "Пикинёры", 380, 930, 0, 1);
            // Дарлтоны — сверху, лицом на юг
            Add(pb, "infantry", 11, "Пехота Дарлтонов", 700, 260, 180, 2);
            Add(pb, "infantry", 12, "Пехота Тосавы", 920, 260, 180, 2);
            Add(pb, "militia", 13, "Ополчение Вульфхартов", 540, 380, 180, 2);
            Add(pb, "knights", 14, "Конница Дарлтонов", 1180, 300, 180, 2);
            Add(pb, "crossbowmen", 15, "Арбалетчики Берга", 820, 160, 180, 2);
            return pb;
        }

        static void Add(PlayBattle pb, string tpl, int id, string name, double x, double y, double facing, int side)
        {
            var m = pb.Battle.Add(Templates.Get(tpl).Make(id, name, 1000, side), x, y, facing);
            pb.Battle.Order(m, new MoveOrder { Kind = OrderKind.Hold });
            pb.Tpl[m] = tpl; pb.StartMen[m] = m.P.U.Soldiers;
        }
    }
}
