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
        public byte[] Image;                                                                // картинка карты (сохранение); null — земля по клеткам
    }

    public static class PlayScenarios
    {
        static readonly Rules R = Rules.Base;

        // меню битв: имя, подпись, как построить
        public static List<(string Name, string Note, Func<PlayBattle> Make)> All()
        {
            var list = new List<(string, string, Func<PlayBattle>)> { ("Учебное поле", "5 на 5 через ручей с двумя бродами; холм, лес", () => Training()) };
            foreach (var f in SaveScene.Find())
                list.Add((SaveScene.Label(f), System.IO.Path.GetFileName(f) + " — твои армии и расстановка, бежавшие снова в строю", () => FromSave(f)));
            return list;
        }

        public static PlayBattle FromSave(string path)
        {
            var sc = SaveScene.Make(path, 99);
            var pb = new PlayBattle { Name = sc.Name, Note = sc.Note, Geo = sc.Geo, Battle = sc.Battle, Image = sc.Image };
            pb.Session = new BattleSession(pb.Battle);
            foreach (var kv in sc.SideNames) pb.Session.SideNames[kv.Key] = kv.Value;
            foreach (var (m, _) in sc.Units)
            {
                pb.Tpl[m] = sc.Tpl[m]; pb.StartMen[m] = m.P.U.Soldiers;
                if (sc.Color.TryGetValue(m, out var c) && c != null) pb.Color[m] = c;
            }
            return pb;
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
