// ═══════════ Kits.cs — снаряжение бойцов (В9, В16, В18), как kitsF в core/Tests/polygon-men-flat.js ═══════════
// У отряда — 6–12 комплектов, у бойца — один из них по номеру. Хэш и таблицы — те же, что в пробе, поэтому отряд
// с тем же номером одет так же. Стиль (В16) меняет наборы: чего в стиле нет — как у западного. Комплект хранит имена
// частей атласа (core/Tests/atlas-export.js) и цвета частей: пурпурный в атласе Unity перекрашивает в цвет вершины,
// белые части (волосы, шапки, предплечья) — умножает на него (Men.shader).
// Без UnityEngine: файл входит и в game/Tools/bench (сверка угадывания облика с трекером).
using System.Collections.Generic;

namespace Journal.Art
{
    // цвет части: Rgb ≥ 0 — свой (0xRRGGBB); −1 — цвет стороны, сдвинутый на Tone (−1…1: к чёрному или к белому)
    public struct Tint
    {
        public int Rgb; public float Tone;
        public static Tint Team(float tone = 0) => new Tint { Rgb = -1, Tone = tone };
        public static Tint Of(int rgb) => new Tint { Rgb = rgb };
    }

    public sealed class Kit
    {
        public string Look, Style; public bool Horse;
        public string Armour, Layout, LayoutKey;     // доспех; раскладка цвета стороны (fLayout) и ключ части тела в атласе
        public string Helm, Weapon, Base, Side, BackKind, ShieldShape, Bard, Mark, TabPaint;
        public int C2, Coat, Socks;                  // второй цвет герба (номер в DEVICE), масть (номер в COATS), белые ноги (биты ЛП, ПП, ЛЗ, ПЗ)
        public bool Crest;
        // имена частей атласа
        public string Body, Head, Back, ShieldTop, ShieldFlat, Hand, Rider, BowPart;
        public string HorseHead, HorseCover, DeadHorse;
        // цвета: тело и ноги всадника, рукава, голова (HeadTint — белая, умножить на цвет), поле щита, ткань одежды
        public Tint Cloth, BodyCol, Sleeve, HeadCol, ShieldCol;
        public bool HeadTint;
        public float Tone;                           // сдвиг тона цвета стороны у одежды (−0,1…0,1)
    }

    public static class Kits
    {
        // хэш пробы: Math.imul и сдвиги без знака — тот же результат бит в бит
        public static double Hash(int a, int b)
        {
            unchecked
            {
                int x = (a ^ (int)0x9e3779b9) * (int)0x85ebca6b ^ (b + 0x632be5ab) * (int)0xc2b2ae35;
                x ^= (int)((uint)x >> 15); x *= 0x2c1b3c6d; x ^= (int)((uint)x >> 12);
                return (uint)x / 4294967296.0;
            }
        }

        public static readonly Dictionary<string, string> LookByTpl = new Dictionary<string, string>
        {
            ["militia"] = "militia", ["infantry"] = "spear", ["guard"] = "sword", ["foot_knights"] = "sword", ["pikemen"] = "pike",
            ["militia_archers"] = "bow", ["archers"] = "bow", ["crossbowmen"] = "crossbow", ["knights"] = "lance", ["elite_cavalry"] = "barded",
        };
        static readonly Dictionary<string, string> LookByType = new Dictionary<string, string> { ["infantry"] = "spear", ["pike"] = "pike", ["archer"] = "bow", ["cavalry"] = "lance" };
        public static string LookOf(string tpl, string type) => LookByTpl.TryGetValue(tpl ?? "", out var l) ? l : LookByType.TryGetValue(type ?? "", out var t) ? t : "spear";

        // новое оружие держат и бьют, как похожее старое: позы и кисти — от него
        static readonly Dictionary<string, string> BaseOf = new Dictionary<string, string>
        { ["halberd"] = "spear", ["daneaxe"] = "spear", ["naginata"] = "spear", ["javelin"] = "spear", ["sabre"] = "sword", ["katana"] = "sword", ["recurve"] = "bow", ["yumi"] = "bow" };
        public static string Base(string w) => w != null && BaseOf.TryGetValue(w, out var b) ? b : w;

        sealed class K
        {
            public (string, int)[] Helm, Weapon, Shield, Paint, Back, Armour, Side, Bard;
            public double Own; public bool Uniform;
        }
        static (string, int)[] W(params (string, int)[] a) => a;
        // ── наборы западного стиля (KIT в polygon-men.js) ──
        static readonly Dictionary<string, K> Table = new Dictionary<string, K>
        {
            ["militia"] = new K { Helm = W(("hair", 40), ("cap", 25), ("hood", 25), ("kettle", 10)), Weapon = W(("spear", 55), ("axe", 12), ("fork", 10), ("club", 13), ("sword", 5), ("mace", 5)),
                Shield = W(("none", 50), ("round", 45), ("oval", 5)), Paint = W(("wood", 55), ("plain", 30), ("halves", 15)), Back = W(("none", 50), ("roll", 25), ("bag", 25)),
                Armour = W(("cloth", 85), ("leather", 15)), Own = 0.55 },
            ["spear"] = new K { Helm = W(("kettle", 30), ("nasal", 35), ("capSteel", 20), ("hood", 10), ("hair", 5)), Weapon = W(("spear", 88), ("axe", 6), ("sword", 6)),
                Shield = W(("round", 50), ("heater", 35), ("oval", 15)), Paint = W(("plain", 35), ("halves", 15), ("stripe", 15), ("cross", 10), ("boss", 25)),
                Back = W(("none", 70), ("roll", 20), ("bag", 10)), Armour = W(("cloth", 50), ("mail", 30), ("leather", 20)), Own = 0.12 },
            ["sword"] = new K { Helm = W(("nasal", 50), ("great", 30), ("kettle", 20)), Weapon = W(("sword", 70), ("mace", 15), ("axe", 15)), Shield = W(("heater", 100)),
                Paint = W(("halves", 25), ("quarters", 25), ("chevron", 25), ("cross", 25)), Back = W(("none", 65), ("cape", 35)), Armour = W(("mail", 70), ("plate", 30)),
                Own = 0.03, Uniform = true },
            ["pike"] = new K { Helm = W(("kettle", 45), ("morion", 25), ("capSteel", 20), ("hair", 10)), Weapon = W(("pike", 100)), Shield = W(("none", 80), ("buckler", 20)),
                Paint = W(("plain", 100)), Back = W(("none", 75), ("roll", 25)), Armour = W(("cloth", 55), ("mail", 25), ("leather", 20)), Own = 0.1 },
            ["bow"] = new K { Helm = W(("hood", 40), ("cap", 25), ("kettle", 20), ("hair", 15)), Weapon = W(("bow", 100)), Side = W(("none", 45), ("falchion", 35), ("axe", 20)),
                Shield = W(("none", 100)), Back = W(("quiver", 100)), Armour = W(("cloth", 60), ("leather", 40)), Own = 0.2 },
            ["crossbow"] = new K { Helm = W(("kettle", 50), ("nasal", 30), ("cap", 20)), Weapon = W(("crossbow", 100)), Side = W(("none", 50), ("sword", 30), ("mace", 20)),
                Shield = W(("none", 100)), Back = W(("pavise", 100)), Armour = W(("cloth", 45), ("mail", 35), ("leather", 20)), Own = 0.1 },
            ["lance"] = new K { Helm = W(("great", 50), ("bascinet", 30), ("nasal", 20)), Weapon = W(("lance", 80), ("sword", 12), ("mace", 8)), Shield = W(("heater", 100)),
                Paint = W(("halves", 20), ("quarters", 20), ("chevron", 20), ("cross", 15), ("stripe", 15), ("plain", 10)), Back = W(("none", 100)),
                Armour = W(("mail", 60), ("plate", 40)), Bard = W(("cloth", 60), ("none", 40)), Own = 0 },
            ["barded"] = new K { Helm = W(("great", 100)), Weapon = W(("lance", 100)), Shield = W(("heater", 100)), Paint = W(("quarters", 50), ("chevron", 50)), Back = W(("none", 100)),
                Armour = W(("plate", 100)), Bard = W(("full", 100)), Own = 0, Uniform = true },
        };
        // ── стили (В16, F_STYLE в пробе): поля, что заменяют западные ──
        static readonly Dictionary<string, Dictionary<string, K>> Styled = new Dictionary<string, Dictionary<string, K>>
        {
            ["west"] = new Dictionary<string, K> { ["pike"] = new K { Weapon = W(("pike", 70), ("halberd", 30)) } },
            ["north"] = new Dictionary<string, K>   // Скандинавия, Русь, Балтика: шишаки, круглые и каплевидные щиты, кольчуга и чешуя, топоры, плащи
            {
                ["militia"] = new K { Helm = W(("hair", 45), ("hood", 25), ("cap", 20), ("shishak", 10)), Weapon = W(("spear", 45), ("axe", 35), ("club", 10), ("sword", 10)),
                    Shield = W(("round", 80), ("none", 20)), Paint = W(("wood", 40), ("plain", 30), ("halves", 15), ("boss", 15)) },
                ["spear"] = new K { Helm = W(("shishak", 45), ("nasal", 35), ("capSteel", 20)), Weapon = W(("spear", 75), ("axe", 25)), Shield = W(("round", 60), ("kite", 40)),
                    Paint = W(("plain", 30), ("boss", 30), ("halves", 20), ("stripe", 20)), Armour = W(("mail", 45), ("scale", 25), ("cloth", 30)), Back = W(("none", 70), ("roll", 30)) },
                ["sword"] = new K { Helm = W(("shishak", 55), ("nasal", 45)), Weapon = W(("daneaxe", 45), ("sword", 35), ("axe", 20)), Shield = W(("kite", 50), ("round", 50)),
                    Paint = W(("halves", 25), ("quarters", 25), ("cross", 25), ("stripe", 25)), Armour = W(("mail", 60), ("scale", 40)), Back = W(("none", 100)) },
                ["pike"] = new K { Helm = W(("shishak", 50), ("capSteel", 30), ("hair", 20)), Armour = W(("cloth", 50), ("scale", 25), ("mail", 25)) },
                ["bow"] = new K { Helm = W(("hood", 30), ("cap", 30), ("shishak", 20), ("hair", 20)), Side = W(("axe", 60), ("none", 40)) },
                ["crossbow"] = new K { Helm = W(("kettle", 40), ("shishak", 40), ("cap", 20)) },
                ["lance"] = new K { Helm = W(("shishak", 60), ("nasal", 40)), Weapon = W(("lance", 70), ("sword", 15), ("axe", 15)), Shield = W(("kite", 70), ("round", 30)),
                    Armour = W(("mail", 60), ("scale", 40)), Bard = W(("none", 70), ("cloth", 30)) },
                ["barded"] = new K { Helm = W(("shishak", 100)), Armour = W(("scale", 100)), Shield = W(("kite", 100)), Bard = W(("full", 100)) },
            },
            ["east"] = new Dictionary<string, K>    // Византия, степь, Персия: ламелляр, остроконечные шлемы с бармицей, круглые щиты, сабли, составные луки
            {
                ["militia"] = new K { Helm = W(("turban", 35), ("cap", 30), ("hair", 20), ("pointed", 15)), Weapon = W(("spear", 50), ("sabre", 20), ("mace", 15), ("club", 15)),
                    Shield = W(("round", 60), ("none", 40)), Paint = W(("plain", 50), ("boss", 30), ("wood", 20)), Back = W(("none", 60), ("bag", 40)) },
                ["spear"] = new K { Helm = W(("pointed", 60), ("turban", 20), ("capSteel", 20)), Weapon = W(("spear", 80), ("sabre", 10), ("mace", 10)), Shield = W(("round", 100)),
                    Paint = W(("boss", 40), ("plain", 30), ("stripe", 30)), Armour = W(("lamellar", 50), ("leather", 25), ("cloth", 25)) },
                ["sword"] = new K { Helm = W(("pointed", 100)), Weapon = W(("sabre", 60), ("mace", 40)), Shield = W(("round", 100)), Paint = W(("boss", 50), ("halves", 25), ("stripe", 25)),
                    Armour = W(("lamellar", 70), ("mail", 30)), Back = W(("none", 100)) },
                ["pike"] = new K { Helm = W(("pointed", 50), ("turban", 30), ("cap", 20)), Armour = W(("cloth", 60), ("lamellar", 40)) },
                ["bow"] = new K { Helm = W(("pointed", 35), ("cap", 35), ("turban", 30)), Weapon = W(("recurve", 100)), Side = W(("sabre", 60), ("none", 40)),
                    Armour = W(("cloth", 50), ("leather", 30), ("lamellar", 20)) },
                ["crossbow"] = new K { Helm = W(("pointed", 50), ("turban", 50)), Back = W(("pavise", 30), ("none", 70)), Armour = W(("cloth", 50), ("lamellar", 50)) },
                ["lance"] = new K { Helm = W(("pointed", 100)), Weapon = W(("lance", 50), ("sabre", 30), ("mace", 20)), Shield = W(("round", 100)), Paint = W(("boss", 50), ("plain", 50)),
                    Armour = W(("lamellar", 70), ("mail", 30)), Bard = W(("none", 60), ("cloth", 40)) },
                ["barded"] = new K { Helm = W(("pointed", 100)), Armour = W(("lamellar", 100)), Shield = W(("round", 100)), Paint = W(("boss", 100)), Bard = W(("lamellar", 100)) },
            },
            ["south"] = new Dictionary<string, K>   // Италия, Иберия, мавры: бригантины, салады и барбюты, адарги, дротики
            {
                ["militia"] = new K { Helm = W(("hair", 35), ("cap", 35), ("kettle", 30)), Weapon = W(("spear", 40), ("javelin", 15), ("fork", 15), ("club", 15), ("sword", 15)),
                    Shield = W(("round", 40), ("none", 60)), Paint = W(("plain", 50), ("halves", 25), ("wood", 25)) },
                ["spear"] = new K { Helm = W(("barbute", 35), ("kettle", 35), ("sallet", 30)), Weapon = W(("spear", 85), ("javelin", 15)), Shield = W(("heater", 50), ("round", 30), ("buckler", 20)),
                    Paint = W(("plain", 30), ("halves", 25), ("stripe", 25), ("quarters", 20)), Armour = W(("leather", 55), ("mail", 25), ("cloth", 20)) },
                ["sword"] = new K { Helm = W(("sallet", 50), ("barbute", 50)), Weapon = W(("sword", 70), ("falchion", 30)), Shield = W(("heater", 60), ("buckler", 40)),
                    Armour = W(("leather", 50), ("plate", 50)), Back = W(("none", 100)) },
                ["pike"] = new K { Helm = W(("sallet", 40), ("kettle", 40), ("barbute", 20)), Weapon = W(("pike", 60), ("halberd", 40)), Armour = W(("leather", 50), ("cloth", 50)) },
                ["bow"] = new K { Helm = W(("cap", 50), ("kettle", 50)) },
                ["crossbow"] = new K { Helm = W(("kettle", 50), ("sallet", 50)), Armour = W(("leather", 50), ("mail", 30), ("cloth", 20)) },
                ["lance"] = new K { Helm = W(("sallet", 50), ("barbute", 30), ("bascinet", 20)), Shield = W(("heater", 100)), Armour = W(("plate", 60), ("leather", 40)),
                    Bard = W(("cloth", 80), ("none", 20)) },
                ["barded"] = new K { Helm = W(("sallet", 100)) },
            },
            ["fareast"] = new Dictionary<string, K> // Япония: о-ёрой, кабуто, нагината, юми, сасимоно; щитов в руке нет
            {
                ["militia"] = new K { Helm = W(("jingasa", 60), ("hachimaki", 25), ("hair", 15)), Weapon = W(("spear", 60), ("naginata", 25), ("katana", 15)), Shield = W(("none", 100)),
                    Armour = W(("cloth", 50), ("dou", 50)), Back = W(("sashimono", 50), ("none", 50)) },
                ["spear"] = new K { Helm = W(("jingasa", 80), ("hachimaki", 20)), Weapon = W(("spear", 100)), Shield = W(("none", 100)), Armour = W(("dou", 70), ("cloth", 30)),
                    Back = W(("sashimono", 90), ("none", 10)) },
                ["sword"] = new K { Helm = W(("kabuto", 100)), Weapon = W(("katana", 50), ("naginata", 50)), Shield = W(("none", 100)), Armour = W(("oyoroi", 100)),
                    Back = W(("sashimono", 70), ("none", 30)) },
                ["pike"] = new K { Helm = W(("jingasa", 100)), Shield = W(("none", 100)), Armour = W(("dou", 80), ("cloth", 20)), Back = W(("sashimono", 80), ("none", 20)) },
                ["bow"] = new K { Helm = W(("jingasa", 50), ("kabuto", 30), ("hachimaki", 20)), Weapon = W(("yumi", 100)), Side = W(("katana", 60), ("none", 40)),
                    Armour = W(("dou", 50), ("cloth", 30), ("oyoroi", 20)) },
                ["crossbow"] = new K { Helm = W(("jingasa", 100)), Weapon = W(("yumi", 100)), Side = W(("katana", 50), ("none", 50)), Back = W(("quiver", 100)), Armour = W(("dou", 70), ("cloth", 30)) },
                ["lance"] = new K { Helm = W(("kabuto", 100)), Weapon = W(("yumi", 50), ("naginata", 30), ("katana", 20)), Shield = W(("none", 100)), Armour = W(("oyoroi", 100)),
                    Back = W(("sashimono", 50), ("none", 50)), Bard = W(("none", 100)) },
                ["barded"] = new K { Helm = W(("kabuto", 100)), Weapon = W(("naginata", 60), ("spear", 40)), Shield = W(("none", 100)), Armour = W(("oyoroi", 100)),
                    Back = W(("sashimono", 100)), Bard = W(("cloth", 100)) },
            },
        };
        // ── подвиды (Алекс 10.10.2026): поправки поверх культуры, по наборам; чего нет — как у культуры ──
        static readonly Dictionary<string, Dictionary<string, K>> SubStyled = new Dictionary<string, Dictionary<string, K>>
        {
            ["west-england"] = new Dictionary<string, K>   // длинные луки, биллы, бригантины
            {
                ["militia"] = new K { Helm = W(("hood", 35), ("kettle", 25), ("cap", 25), ("hair", 15)), Weapon = W(("spear", 35), ("halberd", 30), ("club", 15), ("axe", 10), ("sword", 10)) },
                ["spear"] = new K { Helm = W(("sallet", 40), ("kettle", 40), ("bascinet", 20)), Weapon = W(("halberd", 55), ("spear", 35), ("axe", 10)), Shield = W(("none", 50), ("buckler", 30), ("heater", 20)),
                    Armour = W(("leather", 50), ("mail", 30), ("cloth", 20)) },
                ["sword"] = new K { Helm = W(("bascinet", 50), ("sallet", 30), ("great", 20)), Weapon = W(("sword", 50), ("mace", 25), ("axe", 25)), Armour = W(("plate", 70), ("mail", 30)) },
                ["pike"] = new K { Weapon = W(("halberd", 70), ("pike", 30)), Helm = W(("sallet", 40), ("kettle", 40), ("capSteel", 20)) },
                ["bow"] = new K { Helm = W(("sallet", 30), ("kettle", 30), ("cap", 20), ("hood", 20)), Side = W(("sword", 40), ("falchion", 30), ("mace", 30)), Armour = W(("leather", 60), ("cloth", 40)) },
                ["lance"] = new K { Helm = W(("bascinet", 50), ("great", 30), ("sallet", 20)), Armour = W(("plate", 70), ("mail", 30)) },
                ["barded"] = new K { Helm = W(("great", 60), ("bascinet", 40)) },
            },
            ["west-central"] = new Dictionary<string, K>   // Империя: салады, шапели, павезы, алебарды
            {
                ["militia"] = new K { Helm = W(("kettle", 35), ("hair", 25), ("cap", 25), ("hood", 15)), Weapon = W(("spear", 35), ("halberd", 20), ("club", 15), ("axe", 10), ("fork", 10), ("sword", 10)) },
                ["spear"] = new K { Helm = W(("kettle", 50), ("sallet", 35), ("capSteel", 15)), Weapon = W(("spear", 50), ("halberd", 50)), Shield = W(("none", 40), ("heater", 30), ("buckler", 30)),
                    Armour = W(("mail", 40), ("plate", 20), ("cloth", 20), ("leather", 20)) },
                ["sword"] = new K { Helm = W(("sallet", 60), ("bascinet", 25), ("great", 15)), Weapon = W(("sword", 60), ("mace", 40)), Shield = W(("heater", 60), ("buckler", 40)), Armour = W(("plate", 80), ("mail", 20)) },
                ["pike"] = new K { Weapon = W(("pike", 60), ("halberd", 40)), Helm = W(("kettle", 50), ("sallet", 30), ("capSteel", 20)) },
                ["bow"] = new K { Helm = W(("kettle", 50), ("cap", 50)) },
                ["crossbow"] = new K { Helm = W(("kettle", 50), ("sallet", 50)), Armour = W(("mail", 40), ("leather", 30), ("cloth", 30)) },
                ["lance"] = new K { Helm = W(("sallet", 50), ("great", 30), ("bascinet", 20)), Armour = W(("plate", 80), ("mail", 20)), Bard = W(("cloth", 50), ("full", 20), ("none", 30)) },
            },
            ["north-rus"] = new Dictionary<string, K>   // Русь: шишаки, каплевидные щиты, чешуя, топоры и сабли, сложные луки
            {
                ["militia"] = new K { Helm = W(("hair", 35), ("cap", 30), ("shishak", 20), ("hood", 15)), Weapon = W(("spear", 45), ("axe", 35), ("club", 10), ("fork", 10)), Shield = W(("round", 50), ("kite", 25), ("none", 25)) },
                ["spear"] = new K { Helm = W(("shishak", 70), ("capSteel", 15), ("nasal", 15)), Weapon = W(("spear", 75), ("axe", 25)), Shield = W(("kite", 70), ("round", 30)),
                    Armour = W(("mail", 40), ("scale", 40), ("cloth", 20)) },
                ["sword"] = new K { Helm = W(("shishak", 100)), Weapon = W(("sword", 40), ("sabre", 30), ("axe", 30)), Shield = W(("kite", 80), ("round", 20)), Armour = W(("scale", 50), ("mail", 50)) },
                ["bow"] = new K { Weapon = W(("recurve", 100)), Helm = W(("cap", 40), ("shishak", 30), ("hair", 30)), Side = W(("axe", 50), ("sabre", 30), ("none", 20)) },
                ["lance"] = new K { Helm = W(("shishak", 100)), Weapon = W(("lance", 60), ("sabre", 25), ("mace", 15)), Shield = W(("kite", 80), ("round", 20)), Armour = W(("scale", 50), ("mail", 50)),
                    Bard = W(("none", 80), ("cloth", 20)) },
                ["barded"] = new K { Helm = W(("shishak", 100)), Armour = W(("scale", 100)), Shield = W(("kite", 100)), Bard = W(("cloth", 100)) },
            },
            ["east-steppe"] = new Dictionary<string, K>   // степь: малахаи, конные лучники, сабли
            {
                ["militia"] = new K { Helm = W(("cap", 50), ("pointed", 30), ("hair", 20)), Weapon = W(("spear", 40), ("sabre", 25), ("axe", 20), ("club", 15)), Shield = W(("round", 50), ("none", 50)) },
                ["spear"] = new K { Helm = W(("pointed", 60), ("cap", 40)), Weapon = W(("spear", 60), ("sabre", 40)), Armour = W(("lamellar", 40), ("leather", 40), ("cloth", 20)) },
                ["sword"] = new K { Weapon = W(("sabre", 70), ("mace", 30)), Armour = W(("lamellar", 100)) },
                ["bow"] = new K { Helm = W(("cap", 50), ("pointed", 50)), Side = W(("sabre", 70), ("none", 30)), Armour = W(("leather", 50), ("cloth", 30), ("lamellar", 20)) },
                ["lance"] = new K { Helm = W(("pointed", 60), ("cap", 40)), Weapon = W(("recurve", 50), ("sabre", 30), ("lance", 20)), Shield = W(("round", 40), ("none", 60)),
                    Armour = W(("lamellar", 40), ("leather", 40), ("cloth", 20)), Bard = W(("none", 90), ("lamellar", 10)) },
            },
            ["east-china"] = new Dictionary<string, K>   // Китай: ламелляр и чешуя, гуаньдао и цзи, арбалеты
            {
                ["militia"] = new K { Helm = W(("cap", 40), ("hair", 30), ("pointed", 30)), Weapon = W(("spear", 60), ("halberd", 15), ("sabre", 15), ("club", 10)), Shield = W(("round", 40), ("none", 60)) },
                ["spear"] = new K { Helm = W(("pointed", 70), ("capSteel", 30)), Weapon = W(("spear", 60), ("halberd", 40)), Shield = W(("round", 50), ("none", 50)),
                    Armour = W(("lamellar", 50), ("scale", 30), ("cloth", 20)) },
                ["sword"] = new K { Weapon = W(("sabre", 50), ("sword", 50)), Shield = W(("round", 70), ("none", 30)), Armour = W(("lamellar", 60), ("scale", 40)) },
                ["pike"] = new K { Weapon = W(("pike", 50), ("halberd", 50)), Helm = W(("pointed", 60), ("cap", 40)) },
                ["bow"] = new K { Helm = W(("cap", 50), ("pointed", 50)), Side = W(("sabre", 60), ("none", 40)) },
                ["crossbow"] = new K { Helm = W(("pointed", 50), ("cap", 50)), Back = W(("quiver", 100)), Armour = W(("lamellar", 40), ("cloth", 60)) },
                ["lance"] = new K { Weapon = W(("lance", 60), ("sabre", 40)), Shield = W(("round", 50), ("none", 50)), Armour = W(("lamellar", 60), ("scale", 40)), Bard = W(("none", 60), ("cloth", 40)) },
            },
            ["south-byzantium"] = new Dictionary<string, K>   // Византия: ламелляр, каплевидные щиты, катафракты
            {
                ["militia"] = new K { Helm = W(("cap", 40), ("hair", 30), ("pointed", 30)), Weapon = W(("spear", 60), ("club", 15), ("mace", 15), ("sword", 10)), Shield = W(("round", 60), ("none", 40)) },
                ["spear"] = new K { Helm = W(("pointed", 60), ("capSteel", 25), ("nasal", 15)), Weapon = W(("spear", 90), ("mace", 10)), Shield = W(("kite", 50), ("round", 50)),
                    Armour = W(("lamellar", 50), ("mail", 30), ("cloth", 20)) },
                ["sword"] = new K { Helm = W(("pointed", 70), ("nasal", 30)), Weapon = W(("sword", 50), ("mace", 30), ("axe", 20)), Shield = W(("kite", 60), ("round", 40)), Armour = W(("lamellar", 70), ("mail", 30)) },
                ["pike"] = new K { Helm = W(("pointed", 50), ("capSteel", 50)), Armour = W(("cloth", 50), ("lamellar", 50)) },
                ["bow"] = new K { Weapon = W(("recurve", 100)), Helm = W(("pointed", 40), ("cap", 60)), Side = W(("sword", 50), ("none", 50)) },
                ["crossbow"] = new K { Helm = W(("pointed", 60), ("kettle", 40)), Back = W(("none", 60), ("pavise", 40)) },
                ["lance"] = new K { Helm = W(("pointed", 100)), Weapon = W(("lance", 60), ("mace", 25), ("sword", 15)), Shield = W(("kite", 70), ("round", 30)), Armour = W(("lamellar", 80), ("mail", 20)),
                    Bard = W(("none", 40), ("cloth", 30), ("lamellar", 30)) },
                ["barded"] = new K { Helm = W(("pointed", 100)), Armour = W(("lamellar", 100)), Shield = W(("kite", 100)), Bard = W(("lamellar", 100)) },
            },
            ["south-iberia"] = new Dictionary<string, K>   // Иберия, мавры: адарги, дротики, тюрбаны, хинеты
            {
                ["militia"] = new K { Helm = W(("hair", 30), ("cap", 25), ("turban", 25), ("kettle", 20)), Weapon = W(("javelin", 35), ("spear", 30), ("fork", 10), ("club", 10), ("sword", 15)),
                    Shield = W(("adarga", 35), ("round", 25), ("none", 40)) },
                ["spear"] = new K { Helm = W(("kettle", 40), ("barbute", 30), ("turban", 30)), Weapon = W(("spear", 60), ("javelin", 40)), Shield = W(("adarga", 50), ("round", 30), ("heater", 20)),
                    Armour = W(("leather", 60), ("mail", 20), ("cloth", 20)) },
                ["sword"] = new K { Helm = W(("sallet", 40), ("barbute", 30), ("turban", 30)), Weapon = W(("sword", 60), ("falchion", 40)), Shield = W(("adarga", 50), ("heater", 30), ("buckler", 20)) },
                ["bow"] = new K { Helm = W(("cap", 40), ("turban", 40), ("kettle", 20)) },
                ["crossbow"] = new K { Helm = W(("kettle", 60), ("sallet", 40)) },
                ["lance"] = new K { Helm = W(("turban", 30), ("sallet", 40), ("barbute", 30)), Weapon = W(("javelin", 40), ("lance", 40), ("sword", 20)), Shield = W(("adarga", 70), ("heater", 30)),
                    Armour = W(("leather", 60), ("mail", 40)), Bard = W(("none", 50), ("cloth", 50)) },
            },
        };
        static readonly Dictionary<string, int[]> SubCloths = new Dictionary<string, int[]>
        {
            ["west-england"] = new[] { 0xb03a2e, 0xd8d0c0, 0x2f3f6a, 0x7a6b4f, 0x8b7a5c, 0x5f6650, 0xa3977d },
            ["west-central"] = new[] { 0xd0a838, 0x2a2622, 0xc8c0a8, 0x8a2a2a, 0x5a5a5a, 0x7a6b4f, 0x3a4a3a },
            ["north-rus"] = new[] { 0x9a2a22, 0x6a5a48, 0xd8d0b8, 0x3a4a6a, 0x4a5a3a, 0x7a3a2a, 0x8a7a5f },
            ["east-steppe"] = new[] { 0x7a5a3a, 0x5a4a3a, 0x9a7a4a, 0x3a4a5a, 0x8a3a2a, 0xb09060, 0x4a3a2a },
            ["east-china"] = new[] { 0x9a2a2a, 0x2a2a2a, 0xc8a040, 0x2a3a5a, 0x6a4a2a, 0xb03030, 0x3a3a3a },
            ["south-byzantium"] = new[] { 0x5a2a5a, 0x8a2a2a, 0xb8902a, 0x2a3a6a, 0xd8d0c0, 0x4a2a4a, 0x7a5a3a },
            ["south-iberia"] = new[] { 0xd8ccb0, 0xb0823a, 0x8a3a2a, 0x5a3a2a, 0x2a5a4a, 0xe8e0c8, 0x9a5a2e },
        };
        // своя одежда, масти (номера в COATS) и волосы — по стилю
        static readonly int[] ClothsWest = { 0x8b7a5c, 0x7d6b4f, 0xa08d6a, 0x6f6a5e, 0x9a8f7a, 0x7a5c48, 0x5f6650, 0xa3977d };
        static readonly Dictionary<string, int[]> Cloths = new Dictionary<string, int[]>
        {
            ["west"] = ClothsWest,
            ["north"] = new[] { 0x6b5a48, 0x7a6a55, 0x5a6470, 0x8a7a5f, 0x4f5a4a, 0x8e8a7c, 0x6e4a3a },
            ["east"] = new[] { 0xb5462e, 0xc9973a, 0x2f5f8a, 0x7b3b6b, 0x3f7a5a, 0xd8c8a0, 0x8a2f2f },
            ["south"] = new[] { 0xc8b48a, 0xe2d6b8, 0x9a5a2e, 0x6e7d3a, 0xb0823a, 0x5a3a2a, 0x3a5a7a },
            ["fareast"] = new[] { 0x2c3550, 0x4a3a30, 0x6b2a2a, 0x3a4a3a, 0x7a6a4a, 0x34302c, 0x8a7a5a },
        };
        static readonly Dictionary<string, int[]> Coats = new Dictionary<string, int[]>
        { ["west"] = new[] { 0, 1, 2, 3, 4, 5, 6 }, ["north"] = new[] { 1, 2, 4, 5, 6 }, ["east"] = new[] { 0, 2, 3, 4, 5 }, ["south"] = new[] { 0, 1, 2, 3, 6 }, ["fareast"] = new[] { 1, 2, 5, 6 } };
        static readonly int[] Hairs = { 0x2e241c, 0x4b3a2a, 0x6b4a2f, 0x8a6a45, 0xb08a50, 0x8c8c86, 0x7a3a20 };
        static readonly int[] HairsFarEast = { 0x1b1816, 0x231d19, 0x2b231d };
        static readonly (string, int)[] Marks = W(("none", 50), ("star", 15), ("blaze", 25), ("snip", 10));
        // цвета доспеха (F_ARMC в пробе) — рукава у раскладок с бронёй
        static readonly Dictionary<string, int> ArmourRgb = new Dictionary<string, int> { ["mail"] = 0xa2a7aa, ["plate"] = 0xb4b9bc, ["scale"] = 0xa9a48d, ["lamellar"] = 0xaeb3b5 };
        static readonly HashSet<string> Tabards = new HashSet<string> { "plain", "halves", "quarters", "cross", "chevron", "stripe" };
        public static bool IsStyle(string s) => s != null && Styled.ContainsKey(Journal.Art.Styles.BaseOf(s));

        static string Pick((string, int)[] list, double r)
        {
            double sum = 0; foreach (var (_, w) in list) sum += w;
            double x = r * sum;
            foreach (var (v, w) in list) { x -= w; if (x < 0) return v; }
            return list[list.Length - 1].Item1;
        }
        // раскладка цвета стороны (fLayout в пробе)
        public static string LayoutOf(string armour, string style) =>
            armour == "cloth" || armour == "leather" ? "cloth" : armour == "oyoroi" ? "lacing" : armour == "dou" ? "dou" : style == "north" ? "cloak" : armour == "lamellar" ? "kaftan" : "tabard";

        // полководец и стража (Алекс 10.10.2026: «должны выделяться на фоне всего отряда», снаряжение — по культуре): лучший
        // комплект культуры и подвида — пешим мечники, конным тяжёлые рыцари; у всех плащ; у полководца на топфхелме гребень
        public static Kit Commander(int unitId, string look, string style)
        {
            bool horse = look == "lance" || look == "barded";
            var k = Of(unitId * 7 + 900001, horse ? "barded" : "sword", style)[0];
            if (!horse) { k.Back = "back/cape"; k.BackKind = "cape"; }
            if (k.Helm == "great") { k.Crest = true; k.Head = "head/great/crest"; }
            return k;
        }
        public static Kit[] Guard(int unitId, string look, string style)
        {
            bool horse = look == "lance" || look == "barded";
            var ks = Of(unitId * 7 + 900003, horse ? "barded" : "sword", style);
            if (!horse) foreach (var k in ks) { k.Back = "back/cape"; k.BackKind = "cape"; }
            return ks;
        }

        // Комплекты отряда: как kitsF в пробе
        public static Kit[] Of(int unitId, string look, string style = "west")
        {
            if (!IsStyle(style)) style = "west";
            string sub = style; style = Journal.Art.Styles.BaseOf(style);   // подвид: культура + поправки
            var T0 = Table[look]; Styled[style].TryGetValue(look, out var S);
            K U = null; if (SubStyled.TryGetValue(sub, out var su)) su.TryGetValue(look, out U);
            K Over(K a, K o) => o == null ? a : new K { Helm = o.Helm ?? a.Helm, Weapon = o.Weapon ?? a.Weapon, Shield = o.Shield ?? a.Shield, Paint = o.Paint ?? a.Paint,
                Back = o.Back ?? a.Back, Armour = o.Armour ?? a.Armour, Side = o.Side ?? a.Side, Bard = o.Bard ?? a.Bard, Own = a.Own, Uniform = a.Uniform };
            var T = Over(Over(T0, S), U);
            int seed = unitId * 1013 + 7, N = T.Uniform ? 6 : 12;
            bool horse = look == "lance" || look == "barded";
            var cloths = SubCloths.TryGetValue(sub, out var sc) ? sc : Cloths[style]; var coats = Coats[style]; var hairs = style == "fareast" || sub == "east-china" ? HairsFarEast : Hairs;
            int c2 = (int)(Hash(seed, 2) * 3);
            string oneHelm = Pick(T.Helm, Hash(seed, 3)), onePaint = Pick(T.Paint ?? W(("plain", 1)), Hash(seed, 4));
            var kits = new Kit[N];
            for (int i = 0; i < N; i++)
            {
                int s = seed * 31 + i * 7 + 1;
                double R(int j) => Hash(s, j);
                bool own = R(1) < T.Own;
                var k = new Kit { Look = look, Style = style, Horse = horse, C2 = c2 };
                k.Tone = own ? 0 : (float)((R(3) - 0.5) * 0.2);
                k.Cloth = own ? Tint.Of(cloths[(int)(R(2) * cloths.Length)]) : Tint.Team(k.Tone);
                // голова: волосы, шапка, капюшон, тюрбан — белые в атласе, красятся цветом; повязка — по цвету волос; шлемы — свои
                k.Helm = T.Uniform ? oneHelm : Pick(T.Helm, R(4));
                int hair = (int)(R(5) * hairs.Length);
                k.Head = "head/" + k.Helm; k.HeadCol = Tint.Team();
                if (k.Helm == "hair") { k.HeadTint = true; k.HeadCol = Tint.Of(hairs[hair]); }
                else if (k.Helm == "hachimaki") k.Head = "head/hachimaki/" + hair;
                else if (k.Helm == "turban") { k.HeadTint = true; k.HeadCol = R(6) < 0.6 ? Tint.Of(0xebe3cf) : Tint.Of(cloths[(int)(R(7) * cloths.Length)]); }
                else if (k.Helm == "cap" || k.Helm == "hood") { k.HeadTint = true; k.HeadCol = R(6) < 0.5 ? Tint.Of(cloths[(int)(R(7) * cloths.Length)]) : Tint.Team(-0.25f); }
                // щит: поле — своя одежда или цвет стороны, герб — второй цвет (у ополчения — у каждого свой)
                k.ShieldShape = Pick(T.Shield, R(8));
                string paint = T.Uniform ? onePaint : Pick(T.Paint ?? W(("plain", 1)), R(9));
                bool ownField = own && R(10) < 0.5;
                int sc2 = look == "militia" ? (int)(R(11) * 3) : c2;
                string spaint = k.ShieldShape == "buckler" ? "steel" : paint;
                if (k.ShieldShape != "none")
                {
                    k.ShieldTop = k.ShieldShape == "buckler" ? "shtop/buckler/steel" : $"shtop/{k.ShieldShape}/{spaint}/{sc2}";
                    k.ShieldFlat = k.ShieldShape == "buckler" ? "shield/buckler/steel" : $"shield/{k.ShieldShape}/{spaint}/{sc2}";
                    k.ShieldCol = ownField ? k.Cloth : Tint.Team();
                }
                k.BackKind = Pick(T.Back, R(12));
                k.Back = k.BackKind == "none" ? null : k.BackKind == "pavise" || k.BackKind == "sashimono" ? $"back/{k.BackKind}/{c2}"
                    : k.BackKind == "quiver" && style == "fareast" ? "back/quiver/lacq" : "back/" + k.BackKind;
                k.Armour = Pick(T.Armour, R(13));
                k.TabPaint = Tabards.Contains(paint) ? paint : k.ShieldShape != "none" && Tabards.Contains(spaint) ? spaint : "plain";
                k.Crest = k.Helm == "great" && R(15) < 0.35;
                if (k.Crest) k.Head = "head/great/crest";
                k.Weapon = Pick(T.Weapon, R(16)); k.Base = Base(k.Weapon);
                k.Side = T.Side != null ? Pick(T.Side, R(17)) : "none";
                k.Coat = coats[(int)(R(18) * coats.Length)];
                k.Bard = T.Bard != null ? Pick(T.Bard, R(19)) : "none";
                k.Mark = Pick(Marks, R(20)); k.Socks = R(21) < 0.55 ? 0 : 1 + (int)(R(22) * 15);
                // тело по раскладке; рукава, кисти, ноги всадника
                k.Layout = LayoutOf(k.Armour, style);
                k.LayoutKey = k.Layout == "cloth" ? (k.Armour == "leather" ? "leather" : "cloth")
                    : k.Layout == "tabard" ? $"tabard/{(k.Armour == "plate" ? "plate" : "mail")}/{k.TabPaint}/{c2}"
                    : k.Layout == "cloak" ? $"cloak/{(k.Armour == "scale" ? "scale" : "mail")}" : k.Layout == "dou" ? $"dou/{c2}" : k.Layout;
                k.Body = "body/" + k.LayoutKey;
                k.BodyCol = k.Layout == "cloth" || k.Layout == "dou" ? k.Cloth : Tint.Team();
                k.Sleeve = k.Layout == "kaftan" ? Tint.Team() : k.Layout == "lacing" ? Tint.Of(0x3b3533) : k.Layout == "cloth" || k.Layout == "dou" ? k.Cloth : Tint.Of(ArmourRgb.TryGetValue(k.Armour, out var ar) ? ar : 0xa2a7aa);
                k.Hand = k.Armour == "plate" ? "hand/plate" : k.Armour == "mail" ? "hand/glove" : "hand/skin";
                k.Rider = k.Armour == "plate" ? "rider/plate" : k.Armour == "oyoroi" ? "rider/oyoroi" : "rider/n";
                k.BowPart = k.Base == "bow" ? $"bow/{k.Weapon}/" : null;
                // конь: голова по отметине (у брони — своя), попона, павший
                k.HorseHead = k.Bard == "full" ? $"hhead/{k.Coat}/full/{c2}" : k.Bard == "lamellar" ? $"hhead/{k.Coat}/lamellar" : $"hhead/{k.Coat}/{k.Mark}";
                k.HorseCover = k.Bard == "full" ? $"hcover/full/{k.TabPaint}/{c2}" : k.Bard == "cloth" ? $"hcover/cloth/{c2}" : k.Bard == "lamellar" ? "hcover/lamellar" : "hcover/none";
                k.DeadHorse = k.Bard == "full" ? $"deadhorse/{k.Coat}/full/{c2}" : $"deadhorse/{k.Coat}/{k.Bard}";
                kits[i] = k;
            }
            return kits;
        }
    }
}
