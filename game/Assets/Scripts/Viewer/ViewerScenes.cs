// ═══════════ ViewerScenes.cs — сцены смотрелки боя: те же, что в полигоне (core/Tests/Polygon.cs) ═══════════
// Сцена — карта, отряды и приказы. Считает её движок (BattleCore) прямо в Unity — BattleRecord.Run.
// Повторяет сцены полигона, чтобы картинки можно было сравнить; меняются сцены полигона — меняем и здесь.
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

namespace Journal.Viewer
{
    public sealed class SceneDef
    {
        public string Name, Note;
        public Geo Geo;
        public int Turns;
        public Battle Battle;                                    // бой (БД1–БД4); null — только движение (MoveSim)
        public readonly List<(Mover M, MoveOrder O)> Units = new List<(Mover, MoveOrder)>();
        public readonly Dictionary<Mover, string> Tpl = new Dictionary<Mover, string>();
        public Action<int> Before;                               // перед ходом i (с нуля): приказы между ходами
        public byte[] Image;                                     // картинка карты вместо земли по клеткам (сохранение трекера)
        public readonly Dictionary<Mover, string> Color = new Dictionary<Mover, string>();   // цвет отряда (#rrggbb); нет — оттенок стороны
        public readonly Dictionary<Mover, string> Style = new Dictionary<Mover, string>();   // стиль облика (В16); нет — западный
        public readonly Dictionary<int, string> SideNames = new Dictionary<int, string>();   // сторона → имя (сохранение — фракции через запятую)

        static Rules R => Rules.Base;
        public static Geo Open(double w, double h, string fill = "field")
        {
            var m = Terrain.Create(w, h, Terrain.Id(fill));
            return new Geo { Map = m, W = Terrain.WidthM(m), H = Terrain.HeightM(m) };
        }
        public static Geo Of(TerrainMap m) => new Geo { Map = m, W = Terrain.WidthM(m), H = Terrain.HeightM(m) };

        // движение без боя: отряд с приказом «иди туда, встань так»
        public void Add(string tpl, int id, string name, double x, double y, double facing, double tx, double ty, double tf, int faction = 1, double men = 1000)
        {
            var t = Templates.Get(tpl);
            var m = Mover.Place(t.Make(id, name, men, faction), x, y, facing, R);
            Units.Add((m, new MoveOrder { X = tx, Y = ty, Facing = tf }));
            Tpl[m] = tpl;
        }
        // бой: отряды живут в Battle
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
    }

    public static class ViewerScenes
    {
        static Rules R => Rules.Base;
        static Battle NewBattle(Geo geo, uint seed, Rules r = null) => new Battle(geo, r ?? R, new EngineContext { Rng = new Mulberry32(seed).Next });

        // Имена — для списка в смотрелке; сцена строится заново при каждом выборе (в фоне, не в кадре)
        public static readonly (string Name, Func<SceneDef> Make)[] All = Builtin()
            .Concat(SaveScene.Find().Select(f => (SaveScene.Label(f), (Func<SceneDef>)(() => SaveScene.Make(f))))).ToArray();
        static (string Name, Func<SceneDef> Make)[] Builtin() => new (string, Func<SceneDef>)[]
        {
            ("Бой: стрельба", Shoot),
            ("Бой: натиск и стычки", Hit),
            ("Бой: фланг и потери", Flank),
            ("Бой: охват и двое на одного", Wrap),
            ("Бой: бегство и паника", Flee),
            ("Бой: сплотить", Rally),
            ("Река: брод и мост", River),
            ("Рода войск", Parade),
            ("Облик: анимации", () => Anim()),
            ("Бойцы: рукопашная (Б2)", () => Anim(true)),
        };

        static SceneDef Shoot()
        {
            var sc = new SceneDef { Name = "Бой: стрельба", Turns = 3, Geo = SceneDef.Open(1300, 800),
                Note = "Слева — лучники по стоящей пехоте, посередине — перестрелка, справа — лучники встречают наступающую пехоту." };
            sc.Battle = NewBattle(sc.Geo, 9);
            sc.Fighter("infantry", 2, "Враг: пехота", 220, 520, 0, faction: 2);
            var s1 = sc.Fighter("archers", 1, "Лучники", 220, 520 - (4 + 100 + 2.5), 180);
            sc.Fighter("archers", 4, "Враг: стрелки", 650, 520, 0, faction: 2);
            var s2 = sc.Fighter("archers", 3, "Стрелки", 650, 520 - (2.5 + 120 + 2.5), 180);
            var s3 = sc.Fighter("archers", 5, "Лучники Б", 1080, 330, 0);
            var foe = sc.Fighter("infantry", 6, "Враг: наступают", 1080, 330 - (2.5 + 190 + 4), 180, faction: 2);
            sc.Order(s1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(s2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
            sc.Order(s3, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
            sc.Order(foe, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
            return sc;
        }
        static SceneDef Hit()
        {
            var sc = new SceneDef { Name = "Бой: натиск и стычки", Turns = 3, Geo = SceneDef.Open(1100, 800),
                Note = "Рыцари с разбега 150 м — натиск; по пикам во фронт натиска нет. Пехота сходится с пехотой посреди хода." };
            sc.Battle = NewBattle(sc.Geo, 5);
            sc.Fighter("infantry", 2, "Враг: пехота", 200, 520, 0, faction: 2);
            var k1 = sc.Fighter("knights", 1, "Рыцари", 200, 520 - (4 + 150 + 7.5), 180);
            sc.Fighter("pikemen", 4, "Враг: пикинёры", 650, 520, 0, faction: 2);
            var k2 = sc.Fighter("knights", 3, "Рыцари на пики", 650, 520 - (5 + 150 + 7.5), 180);
            sc.Fighter("infantry", 6, "Враг: пехота Б", 980, 520, 0, faction: 2);
            var i1 = sc.Fighter("infantry", 5, "Пехота", 980, 520 - (4 + 60 + 4), 180);
            sc.Order(k1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2, Charge = true });
            sc.Order(k2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4, Charge = true });
            sc.Order(i1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
            return sc;
        }
        static SceneDef Flank()
        {
            var sc = new SceneDef { Name = "Бой: фланг и потери", Turns = 3, Geo = SceneDef.Open(1000, 700),
                Note = "Атакованный во фланг сам не поворачивается. Гвардия рубит ополчение — фигурки падают, фронт сужается." };
            sc.Battle = NewBattle(sc.Geo, 7);
            sc.Fighter("infantry", 2, "Враг: стоят", 300, 400, 0, faction: 2);
            var fl = sc.Fighter("infantry", 1, "Во фланг", 300 - 62.5 - 4 - 40, 400, 90);
            sc.Fighter("militia", 4, "Враг: ополчение", 750, 400, 0, faction: 2);
            var gd = sc.Fighter("guard", 3, "Гвардия", 750, 400 - (4 + 30 + 4), 180);
            sc.Order(fl, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(gd, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
            return sc;
        }
        static SceneDef Wrap()
        {
            var sc = new SceneDef { Name = "Бой: охват и двое на одного", Turns = 3, Geo = SceneDef.Open(1100, 800),
                Note = "Слева рыцари шире пехоты — свисающие колонны огибают её. Справа двое на одного." };
            sc.Battle = NewBattle(sc.Geo, 11);
            sc.Fighter("infantry", 2, "Враг: пехота", 260, 450, 0, faction: 2);
            var kn = sc.Fighter("knights", 1, "Рыцари", 260, 450 - (4 + 40 + 7.5), 180);
            sc.Fighter("infantry", 5, "Враг: стоят", 780, 450, 0, faction: 2);
            var w1 = sc.Fighter("infantry", 3, "В лоб", 780, 450 - (4 + 40 + 4), 180, men: 500);
            var w2 = sc.Fighter("infantry", 4, "Во фланг", 780 + 62.5 + 4 + 60, 450, 270, men: 500);
            sc.Order(kn, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(w1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
            sc.Order(w2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 5 });
            return sc;
        }
        static SceneDef Flee()
        {
            var sc = new SceneDef { Name = "Бой: бегство и паника", Turns = 4, Geo = SceneDef.Open(1800, 1100),
                Note = "Гвардия ломает ополчение — сосед видит бегство и тоже бежит. Рыцари ломают пехоту и рубят бегущих." };
            sc.Battle = NewBattle(sc.Geo, 3);
            Mover Weak(string tpl, int id, string name, double x, double y, double disc, double morale)
            {
                var m = sc.Fighter(tpl, id, name, x, y, 0, faction: 2);
                m.P.U.Discipline = disc; m.P.U.Morale = morale; return m;
            }
            Weak("militia", 2, "Враг: ополчение", 450, 560, 1, 70);
            Weak("militia", 3, "Враг: сосед", 450 - 125 - 60, 560, 1, 70);
            Weak("militia", 4, "Враг: дальние", 450 + 125 + 320, 560, 1, 70);
            var fg = sc.Fighter("guard", 1, "Гвардия", 450, 560 - (4 + 0.5 + 4), 180);
            Weak("infantry", 6, "Враг: пехота", 1450, 420, 1, 30);
            var fk = sc.Fighter("knights", 5, "Рыцари", 1450, 420 - (4 + 0.5 + 7.5), 180);
            sc.Order(fg, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(fk, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
            return sc;
        }
        static SceneDef Rally()
        {
            var sc = new SceneDef { Name = "Бой: сплотить", Turns = 7, Geo = SceneDef.Open(1000, 760),
                Note = "Новобранцам слева каждый ход приказывают сплотиться; справа приказа нет — бегут за край." };
            sc.Battle = NewBattle(sc.Geo, 11);
            Mover Recruits(int id, string name, double x, double disc)
            {
                var m = sc.Fighter("infantry", id, name, x, 330, 0, faction: 2);
                m.P.U.Discipline = disc; m.P.U.Morale = 12; return m;
            }
            var rec1 = Recruits(2, "Новобранцы", 270, 55);
            Recruits(4, "Новобранцы Б", 730, 20);
            var r1 = sc.Fighter("infantry", 1, "Пехота", 270, 330 - (4 + 0.5 + 4), 180, men: 500);
            var r2 = sc.Fighter("infantry", 3, "Пехота Б", 730, 330 - (4 + 0.5 + 4), 180, men: 500);
            sc.Order(r1, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(r2, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
            sc.Before = turn =>
            {
                if (turn < 1) return;
                if (turn == 1) { sc.Order(r1, new MoveOrder { Kind = OrderKind.Hold }); sc.Order(r2, new MoveOrder { Kind = OrderKind.Hold }); }
                if (rec1.Fleeing) sc.Battle.Order(rec1, new MoveOrder { Kind = OrderKind.Rally });
            };
            return sc;
        }
        static SceneDef River()
        {
            var map = MapGen.Generate("river", new Dictionary<string, object> { ["widthM"] = 1200.0, ["depthM"] = 900.0, ["fords"] = 1.0, ["bridges"] = 1.0 }, 7);
            var sc = new SceneDef { Name = "Река: брод и мост", Turns = 6, Geo = SceneDef.Of(map),
                Note = "Сгенерированная карта «Река», зерно 7. Каждый ищет свою переправу." };
            sc.Add("infantry", 1, "Пехота", 350, 820, 0, 350, 80, 0);
            sc.Add("knights", 2, "Конница", 850, 830, 0, 850, 60, 0);
            return sc;
        }
        // Облик (В13): анимации пачки 1 — как сцена «Облик: анимации» полигона (core/Tests/Polygon.cs)
        // menBodies — те же отряды при рукопашной по бойцам (Б2, переключатель MenBodies, черновик чата механики): у каждого бойца
        // свой противник, удар в своём ритме, падает тот, кого ударили, — смотрелка играет удары и щит по движку
        static SceneDef Anim(bool menBodies = false)
        {
            var sc = new SceneDef { Name = menBodies ? "Бойцы: рукопашная (Б2)" : "Облик: анимации", Turns = 3, Geo = SceneDef.Open(1300, 800),
                Note = menBodies ? "Б2. Рукопашная по бойцам: каждый бьётся со своим противником — удар в миг удара в движке, ударенный падает тогда же; " +
                       "не попал в потери — принял на щит и отшатнулся." :
                       "В13. Пикинёры опускают пики в 30 м от врага; арбалетчики взводят через стремя; пехота рубит ополчение — " +
                       "удары сбоку и сверху, щит навстречу, вспышки; рыцари шагом и рысью выходят на 40 м и встают; раненые ползут." };
            Rules r = null;
            if (menBodies) { r = new Rules(); r.Move.MenBodies = true; }
            sc.Battle = NewBattle(sc.Geo, 13, r);
            sc.Fighter("infantry", 2, "Враг: пехота", 190, 420, 0, faction: 2, men: 400);
            var pk = sc.Fighter("pikemen", 1, "Пикинёры", 190, 420 - 170, 180, men: 400);
            sc.Fighter("infantry", 4, "Враг: пехота Б", 520, 420, 0, faction: 2, men: 400);
            var xb = sc.Fighter("crossbowmen", 3, "Арбалетчики", 520, 420 - 115, 180, men: 300);
            sc.Fighter("militia", 6, "Враг: ополчение", 850, 420, 0, faction: 2, men: 400);
            var inf = sc.Fighter("infantry", 5, "Пехота", 850, 420 - (4 + 0.5 + 4), 180, men: 400);
            var kn = sc.Fighter("knights", 7, "Рыцари", 1150, 640, 0, men: 200);
            sc.Order(pk, new MoveOrder { Kind = OrderKind.Attack, TargetId = 2 });
            sc.Order(xb, new MoveOrder { Kind = OrderKind.Attack, TargetId = 4 });
            sc.Order(inf, new MoveOrder { Kind = OrderKind.Attack, TargetId = 6 });
            sc.Order(kn, new MoveOrder { X = 1150, Y = 600, Facing = 0 });
            return sc;
        }

        static SceneDef Parade()
        {
            var sc = new SceneDef { Name = "Рода войск", Turns = 3, Geo = SceneDef.Open(1100, 600),
                Note = "По отряду каждого шаблона, по 200 человек." };
            string[][] kinds =
            {
                new[] { "militia", "Ополчение" }, new[] { "infantry", "Пехота" }, new[] { "guard", "Гвардия" }, new[] { "pikemen", "Пикинёры" },
                new[] { "archers", "Лучники" }, new[] { "crossbowmen", "Арбалетчики" }, new[] { "knights", "Конные рыцари" }, new[] { "elite_cavalry", "Элитная конница" },
            };
            for (int k = 0; k < kinds.Length; k++) { double x = 80 + k * 130; sc.Add(kinds[k][0], k + 1, kinds[k][1], x, 500, 0, x, 300, 0, men: 200); }
            return sc;
        }
    }
}
