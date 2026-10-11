// ═══════════ Units.cs — справочники и свойства отрядов (копия units.js) ═══════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BattleCore
{
    public static class Modes
    {
        public const string MeleeForm = "melee_form", RangedForm = "ranged_form",
                            MeleeRough = "melee_rough", RangedRough = "ranged_rough";

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            [MeleeForm] = "Ближний бой · в строю",
            [RangedForm] = "Дальний бой · в строю",
            [MeleeRough] = "Ближний бой · пересечённая местность",
            [RangedRough] = "Дальний бой · пересечённая местность",
        };

        public static bool IsMelee(string m) => m == MeleeForm || m == MeleeRough;
    }

    public sealed class MoraleStageInfo
    {
        public string Label, Note, Color;
        public double Mult;
        public MoraleStageInfo(string label, string note, string color, double mult) { Label = label; Note = note; Color = color; Mult = mult; }
    }

    // Тип войск, угаданный по названию отряда (guessUnitType): Why — почему так решили, для предпросмотра
    public sealed class UnitGuess
    {
        public readonly string Type, Weapon, Why;
        public UnitGuess(string type, string weapon, string why) { Type = type; Weapon = weapon; Why = why; }
    }

    public static class Units
    {
        // ── Тип войск по названию отряда (guessUnitType) ──
        // Слова сравниваются как подстроки названия в нижнем регистре, «ё» читается как «е».
        public static readonly Dictionary<string, string[]> TypeKeywords = new Dictionary<string, string[]>
        {
            ["archer"] = new[] { "лучник", "лучниц", "стрелк", "стрелец", "стрельц", "арбалетчик", "арбалетч", "арбалетр",
                                 "пращник", "пращ", "застрельщик", "застрельщ", "охотник", "егер", "мушкетёр", "мушкетер",
                                 "аркебуз", "снайпер", "метател", "дротикомет", "самура", "самурай", "йомен", "лонгбоу" },
            ["cavalry"] = new[] { "кавалер", "конниц", "конн", "всадник", "наездник", "рыцар", "драгун", "гусар", "улан",
                                  "кирасир", "катафракт", "жандарм", "ездов", "верхов", "витяз", "паладин", "сипах", "мамлюк",
                                  "роххирим", "рохирим", "рохиррим", "роханц", "степняк", "орда" },
            ["pike"] = new[] { "пикинёр", "пикинер", "пикейщ", "копейщ", "копьеносц", "копьенос", "сарисс", "фаланг",
                               "алебард", "бердыш", "протазан", "гвардейц с пиками" },
            ["infantry"] = new[] { "пехот", "ополчен", "ополчение", "мечник", "дружин", "стража", "стражник", "гвард", "воин",
                                   "латник", "секирщ", "топорщ", "щитоносц", "легионер", "берсерк", "наёмник", "наемник", "солдат",
                                   "крестьян", "горц" },
        };
        public static readonly string[] HorseArchers = { "роххирим", "рохирим", "рохиррим", "конные лучник", "конных лучник", "степняк", "орда", "всадники-лучник" };
        // «Пешие рыцари», «пешие солдаты»: слово «пеш…» отменяет кавалерию, которую подсказал бы «рыцарь».
        // Только с начала слова — иначе «Рыцари Цепешей» стали бы пехотой.
        static readonly Regex FootRe = new Regex("(^|[^а-яa-z])пеш", RegexOptions.CultureInvariant);

        // Название в виде для поиска по словам: нижний регистр, «ё» → «е»
        public static string NameKey(string name) => (name ?? "").ToLowerInvariant().Replace('ё', 'е');

        // null — название ничего не подсказало
        public static UnitGuess GuessType(string name)
        {
            string n = NameKey(name);
            if (HorseArchers.Any(n.Contains)) return new UnitGuess("cavalry", "ranged", "конные лучники");
            bool Hit(string key) => TypeKeywords[key].Any(w => n.Contains(w.Replace('ё', 'е')));
            bool isFoot = FootRe.IsMatch(n);
            bool isArcher = Hit("archer"), isCav = !isFoot && Hit("cavalry"), isPike = Hit("pike");
            if (isCav && isArcher) return new UnitGuess("cavalry", "ranged", "конные стрелки");
            if (isCav) return new UnitGuess("cavalry", "melee", "кавалерия");
            if (isPike) return new UnitGuess("pike", "melee", "пикинёры");
            if (isArcher) return new UnitGuess("archer", "ranged", "лучники");
            if (isFoot || Hit("infantry")) return new UnitGuess("infantry", "melee", isFoot ? "пешие" : "пехота");
            return null;
        }

        public static double AttackLimit(Unit u, Rules r) => u.Discipline >= r.Actions.EliteDisc ? 2 : 1;
        public static double CounterLimit(Unit u, Rules r) => u.Discipline >= r.Actions.EliteDisc ? 2 : 1;

        public static MoraleStageInfo MoraleStage(double bd)
        {
            if (bd < 20) return new MoraleStageInfo("Дрогнули", "бросок на побег с помехой", "#B0402E", 1);
            if (bd < 40) return new MoraleStageInfo("Колеблются", "проверка БД с помехой", "#C97B2E", 1);
            if (bd < 50) return new MoraleStageInfo("Безучастны", "нет эффектов", "#98A08F", 1);
            if (bd < 80) return new MoraleStageInfo("Рьяны", "могут атаковать сильнейшего", "#7FA05A", 1);
            if (bd < 120) return new MoraleStageInfo("Воодушевлены", "урон +20%", "#C9A227", 1.2);
            return new MoraleStageInfo("Геройство", "урон +50%", "#E0C34A", 1.5);
        }

        public static string DiscStage(double d)
        {
            if (d < 10) return "Сброд: побег до боя, неуправляем";
            if (d < 20) return "Побег до боя, неуправляем, штраф осады";
            if (d < 30) return "Побег до боя, неуправляем";
            if (d < 40) return "Побег при низком БД, команды с трудом";
            if (d < 50) return "Осада без штрафов";
            if (d < 60) return "Может ретироваться";
            if (d < 70) return "Стоит насмерть, отмена атаки";
            if (d < 80) return "Свободный манёвр, перестроение";
            if (d < 90) return "Две атаки за ход";
            return "Элита: 2 атаки, усталость с 8-го хода";
        }

        public static double GraceByDisc(double d, Rules r)
        {
            foreach (var g in r.Breakdown.Grace) if (d >= g[0]) return g[1];
            return 0;
        }

        public static bool IsCav(Unit u) => u.Type == "cavalry";
        public static bool IsPike(Unit u) => u.Type == "pike";
        public static bool IsArcherType(Unit u) => u.Type == "archer";
        public static bool CanBeTargeted(Unit u) => u.Status == "active" || u.Status == "fled";

        // Сектор удара относительно фасинга цели: front / flank / rear.
        // Если хотя бы один из отрядов не на карте — удар считается фронтальным.
        public static string AttackSector(Unit att, Unit def, Rules r)
        {
            if (!att.OnMap || !def.OnMap) return "front";
            double dx = att.MapX - def.MapX, dy = att.MapY - def.MapY;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return "front";
            double ang = Math.Atan2(dx, -dy) * 180 / Math.PI;   // 0° — вверх, по часовой
            double rel = ang - def.Facing;
            while (rel > 180) rel -= 360;
            while (rel < -180) rel += 360;
            double a = Math.Abs(rel);
            if (a <= r.Sectors.FrontMax) return "front";
            if (a >= r.Sectors.RearMin) return "rear";
            return "flank";
        }

        public static readonly Dictionary<string, string> SectorRu = new Dictionary<string, string>
        {
            ["front"] = "во фронт", ["flank"] = "во фланг", ["rear"] = "в тыл",
        };
    }
}
