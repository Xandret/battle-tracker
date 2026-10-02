// ═══════════ Kits.cs — снаряжение бойцов (В9), как в полигоне (kitsOf в core/Tests/polygon-men.js) ═══════════
// У отряда — 6–12 комплектов, у бойца — один из них по номеру. Хэш и таблицы — те же, что в полигоне, поэтому отряд
// с тем же номером одет так же. Комплект хранит имена частей атласа: цвет стороны в атласе — маркерный, его красит шейдер.
using System.Collections.Generic;

namespace Journal.Art
{
    public sealed class Kit
    {
        public string Look; public bool Horse;
        public string Body, Head, Back, Shield, Weapon, Side, Bard;   // имена частей атласа; null — нет
        public string Tabard, Arm, Hand;                               // сюрко (у кольчуги и лат), предплечье, кисть (В15)
        public string Armour;                                          // cloth, mail, leather, plate
        // стоящий боец (В17): шлем и цвет капюшона или шапки (f — сторона, cN — своя ткань), что за спиной, герб сюрко, кожа
        public string Helm, HelmCloth, BackKind, TabPaint; public int Leather;
        public object Fig;                                             // части фигурки, найденные в атласе (FigKit смотрелки)
        public string ClothKey, ArmourKey;                            // для павшего: corpse/<ткань>/<броня>/<поза>
        public string HorseKey;                                        // horse/<масть>/<попона>[/<герб>]
        public string ShieldShape;
        public float Tone;                                             // сдвиг тона цвета стороны (−0,1…0,1)
        public int Coat, C2;
    }

    public static class Kits
    {
        // хэш полигона: Math.imul и сдвиги без знака — тот же результат бит в бит
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

        sealed class K
        {
            public (string, int)[] Helm, Weapon, Shield, Paint, Back, Armour, Side, Bard;
            public double Own; public bool Uniform;
        }
        static (string, int)[] W(params (string, int)[] a) => a;
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
        // сюрко (В15): у кольчуги и лат — всегда; герб — как на щите, иначе без герба
        static readonly HashSet<string> Tabards = new HashSet<string> { "plain", "halves", "quarters", "cross", "chevron", "stripe" };
        static string Pick((string, int)[] list, double r)
        {
            double sum = 0; foreach (var (_, w) in list) sum += w;
            double x = r * sum;
            foreach (var (v, w) in list) { x -= w; if (x < 0) return v; }
            return list[list.Length - 1].Item1;
        }

        // Комплекты отряда: как kitsOf в полигоне
        public static Kit[] Of(int unitId, string look)
        {
            var T = Table[look];
            int seed = unitId * 1013 + 7, N = T.Uniform ? 6 : 12;
            bool horse = look == "lance" || look == "barded";
            int c2 = (int)(Hash(seed, 2) * 3);
            string oneHelm = Pick(T.Helm, Hash(seed, 3)), onePaint = Pick(T.Paint ?? W(("plain", 1)), Hash(seed, 4));
            var kits = new Kit[N];
            for (int i = 0; i < N; i++)
            {
                int s = seed * 31 + i * 7 + 1;
                double R(int j) => Hash(s, j);
                bool own = R(1) < T.Own;
                string cloth = own ? "c" + (int)(R(2) * 8) : "f";
                float tone = own ? 0 : (float)((R(3) - 0.5) * 0.2);
                string helm = T.Uniform ? oneHelm : Pick(T.Helm, R(4));
                string head = helm == "hair" ? "head/hair/" + (int)(R(5) * 7)
                    : helm == "cap" || helm == "hood" ? $"head/{helm}/" + (R(6) < 0.5 ? "c" + (int)(R(7) * 8) : "f")
                    : "head/" + helm;
                string shape = Pick(T.Shield, R(8));
                string paint = T.Uniform ? onePaint : Pick(T.Paint ?? W(("plain", 1)), R(9));
                int sc2 = look == "militia" ? (int)(R(11) * 3) : c2;
                string shield = shape == "none" ? null : shape == "buckler" ? "shield/buckler/steel/0" : $"shield/{shape}/{paint}/{sc2}";
                string back = Pick(T.Back, R(12));
                string armour = Pick(T.Armour, R(13));
                string armourKey = armour == "leather" ? "leather" + (int)(R(14) * 3) : armour;
                if (helm == "great" && R(15) < 0.35) head = "head/great/crest";
                string weapon = Pick(T.Weapon, R(16));
                string side = T.Side != null ? Pick(T.Side, R(17)) : "none";
                int coat = (int)(R(18) * 7);
                string bard = T.Bard != null ? Pick(T.Bard, R(19)) : "none";
                kits[i] = new Kit
                {
                    Look = look, Horse = horse, ClothKey = cloth, ArmourKey = armourKey, Tone = tone, Coat = coat, C2 = c2,
                    Body = $"body/{cloth}/{armourKey}", Head = head, Shield = shield, ShieldShape = shape,
                    Back = back == "none" ? null : back == "pavise" ? "back/pavise/" + c2 : "back/" + back,
                    Weapon = weapon, Side = side, Bard = bard, Armour = armour,
                    Tabard = armour == "mail" || armour == "plate" ? $"tabard/{(Tabards.Contains(paint) ? paint : "plain")}/{c2}" : null,
                    TabPaint = armour == "mail" || armour == "plate" ? (Tabards.Contains(paint) ? paint : "plain") : null,
                    Helm = helm, HelmCloth = head.StartsWith("head/cap/") || head.StartsWith("head/hood/") ? head.Substring(head.LastIndexOf('/') + 1) : null,
                    BackKind = back, Leather = armour == "leather" ? armourKey[7] - '0' : 0,
                    Arm = "arm/" + (armour == "cloth" ? cloth : armourKey),
                    Hand = armour == "plate" ? "hand/plate" : armour == "mail" ? "hand/glove" : "hand/skin",
                    HorseKey = $"horse/{coat}/{bard}" + (bard == "full" ? "/" + c2 : ""),
                };
            }
            return kits;
        }
    }
}
