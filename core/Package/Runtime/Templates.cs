// ═══════════ Templates.cs — шаблоны отрядов (копия templates.js) ═══════════
// ЧЕРНОВИК ДО ГМа: медианы по сохранению armiya_hod1 (v30.1–v30.2), откуда взята каждая цифра — в templates.js.
// Подбор шаблона по названию (Match), правки партии (NormalizeOverrides) и итоговый профиль (Resolve) сверяются
// с трекером по shared/golden/templates.json (правило 7).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class UnitTemplate
    {
        public string Id, Name, Type, Weapon;
        public double Size, Discipline, Morale, EqAtk, EqDef, Exp, Mastery;

        public UnitTemplate(string id, string name, string type, string weapon, double size,
                            double disc, double morale, double atk, double def, double exp, double mastery)
        {
            Id = id; Name = name; Type = type; Weapon = weapon; Size = size;
            Discipline = disc; Morale = morale; EqAtk = atk; EqDef = def; Exp = exp; Mastery = mastery;
        }

        public UnitTemplate Clone() => (UnitTemplate)MemberwiseClone();

        // Поле профиля по имени из правок (size, discipline, morale, eqAtk, eqDef, exp, mastery)
        public double this[string field]
        {
            get => field switch
            {
                "size" => Size, "discipline" => Discipline, "morale" => Morale, "eqAtk" => EqAtk,
                "eqDef" => EqDef, "exp" => Exp, "mastery" => Mastery, _ => double.NaN,
            };
            set
            {
                switch (field)
                {
                    case "size": Size = value; break;
                    case "discipline": Discipline = value; break;
                    case "morale": Morale = value; break;
                    case "eqAtk": EqAtk = value; break;
                    case "eqDef": EqDef = value; break;
                    case "exp": Exp = value; break;
                    case "mastery": Mastery = value; break;
                }
            }
        }

        // Отряд по шаблону: soldiers человек, в строю, не на карте
        public Unit Make(int id, string name, double soldiers, int? factionId) => new Unit
        {
            Id = id, Name = name, Type = Type, Weapon = Weapon, FactionId = factionId,
            Soldiers = soldiers, Initial = soldiers, Discipline = Discipline, Morale = Morale,
            EqAtk = EqAtk, EqDef = EqDef, Exp = Exp, Mastery = Mastery,
        };
    }

    // Какой шаблон подошёл названию. Fallback — название ничего не подсказало, взято ополчение (видно в предпросмотре).
    // Type, Weapon, Why — что угадано по названию (guessUnitType); null, если ничего
    public sealed class TemplateMatch
    {
        public readonly string Id;
        public readonly bool Fallback;
        public readonly UnitGuess Guess;
        public TemplateMatch(string id, bool fallback, UnitGuess guess) { Id = id; Fallback = fallback; Guess = guess; }
        public string Type => Guess?.Type;
        public string Weapon => Guess?.Weapon;
        public string Why => Guess?.Why;
    }

    // Правки шаблонов из партии: общие (Base: шаблон → поле → число) и по фракциям (Factions: фракция → то же).
    // Собирать через Templates.NormalizeOverrides — она чистит и ограничивает значения
    public sealed class TemplateOverrides
    {
        public readonly Dictionary<string, Dictionary<string, double>> Base = new Dictionary<string, Dictionary<string, double>>();
        public readonly Dictionary<string, Dictionary<string, Dictionary<string, double>>> Factions =
            new Dictionary<string, Dictionary<string, Dictionary<string, double>>>();
    }

    public static class Templates
    {
        public static readonly string[] Stats = { "discipline", "morale", "eqAtk", "eqDef", "exp", "mastery" };
        // Что можно переопределить в партии и в каких пределах (те же, что у формы отряда)
        public static readonly Dictionary<string, double[]> Limits = new Dictionary<string, double[]>
        {
            ["size"] = new double[] { 1, 100000 },
            ["discipline"] = new double[] { 1, 100 },
            ["morale"] = new double[] { 0, 150 },
            ["eqAtk"] = new double[] { 0, 1000 },
            ["eqDef"] = new double[] { 0, 1000 },
            ["exp"] = new double[] { 0, 100 },
            ["mastery"] = new double[] { 0, 1000 },
        };

        public static readonly List<UnitTemplate> Base = new List<UnitTemplate>
        {
            new UnitTemplate("militia", "Ополчение", "infantry", "melee", 1000, 40, 70, 40, 40, 0, 0),
            new UnitTemplate("infantry", "Пехота", "infantry", "melee", 1000, 50, 70, 60, 60, 20, 10),
            new UnitTemplate("guard", "Гвардия", "infantry", "melee", 1000, 80, 120, 90, 90, 70, 60),
            new UnitTemplate("militia_archers", "Лучники ополчения", "archer", "ranged", 1000, 30, 70, 30, 30, 0, 0),
            new UnitTemplate("archers", "Лучники", "archer", "ranged", 1000, 70, 80, 70, 30, 20, 40),
            new UnitTemplate("crossbowmen", "Арбалетчики", "archer", "ranged", 1000, 60, 70, 60, 60, 30, 20),
            new UnitTemplate("pikemen", "Пикинёры", "pike", "melee", 1000, 60, 80, 30, 70, 20, 0),
            new UnitTemplate("foot_knights", "Пешие рыцари", "infantry", "melee", 1000, 60, 60, 60, 60, 10, 20),
            new UnitTemplate("knights", "Конные рыцари", "cavalry", "melee", 1000, 40, 80, 80, 80, 20, 20),
            new UnitTemplate("elite_cavalry", "Элитная конница", "cavalry", "melee", 1000, 80, 100, 80, 80, 60, 40),
        };

        public static UnitTemplate Get(string id) => Base.FirstOrDefault(t => t.Id == id);

        // Профессиональная пехота: названия, по которым отряд — не ополчение
        static readonly string[] ProInfantry = { "мечник", "горц", "латник", "секирщ", "топорщ", "щитонос", "легионер", "берсерк",
                                                 "наемник", "наёмник", "дружин", "воин" };
        static bool Has(string n, params string[] words) => words.Any(n.Contains);

        // Какой шаблон подходит отряду (matchTemplate): тип угадывается по названию
        public static TemplateMatch Match(string name) => Match(name, Units.GuessType(name));

        public static TemplateMatch Match(string name, UnitGuess g)
        {
            string n = Units.NameKey(name), type = g?.Type;
            if (Has(n, "арбалет")) return new TemplateMatch("crossbowmen", false, g);
            if (type == "archer") return new TemplateMatch(Has(n, "ополч", "крестьян") ? "militia_archers" : "archers", false, g);
            if (type == "pike") return new TemplateMatch("pikemen", false, g);
            if (type == "cavalry") return new TemplateMatch(Has(n, "элит", "гвард", "орден") ? "elite_cavalry" : "knights", false, g);
            if (Has(n, "рыцар")) return new TemplateMatch("foot_knights", false, g);
            if (Has(n, "ополч", "крестьян")) return new TemplateMatch("militia", false, g);
            if (Has(n, "гвард", "страж", "телохран", "лейб")) return new TemplateMatch("guard", false, g);
            if (Has(n, ProInfantry)) return new TemplateMatch("infantry", false, g);
            if (type == "infantry") return new TemplateMatch("militia", false, g);
            return new TemplateMatch("militia", true, g);
        }

        // Значение правки в пределах поля, целым (clampField); поле без пределов — только округление
        public static double ClampField(string field, double v)
        {
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            if (field != null && Limits.TryGetValue(field, out var l)) { lo = l[0]; hi = l[1]; }
            return Math.Min(hi, Math.Max(lo, Js.Round(v)));
        }

        // Правки из партии в чистом виде (normalizeOverrides). raw — дерево как после JSON: словари
        // (IDictionary со строковыми ключами), списки, строки, числа, true/false, null. Неизвестные шаблоны и поля,
        // не-числа и пустые правки выбрасываются, числа — как унарный плюс JS, в пределах поля и целые
        public static TemplateOverrides NormalizeOverrides(object raw)
        {
            var o = new TemplateOverrides();
            if (!(raw is IDictionary d)) return o;
            foreach (var (id, patch) in Clean(Field(d, "base"))) o.Base[id] = patch;
            foreach (var (name, src) in Entries(Field(d, "factions")))
            {
                var c = Clean(src);
                string key = Js.Trim(name);
                if (key.Length > 0 && c.Count > 0)
                {
                    var bucket = new Dictionary<string, Dictionary<string, double>>();
                    foreach (var (id, patch) in c) bucket[id] = patch;
                    o.Factions[key] = bucket;
                }
            }
            return o;
        }

        static List<(string, Dictionary<string, double>)> Clean(object src)
        {
            var r = new List<(string, Dictionary<string, double>)>();
            foreach (var (id, patch) in Entries(src))
            {
                if (Get(id) == null || !(patch is IDictionary || patch is IList)) continue;
                var p = new Dictionary<string, double>();
                foreach (var (k, v) in Entries(patch))
                {
                    if (!Limits.ContainsKey(k)) continue;
                    double x = Js.ToNumber(v);
                    if (!double.IsNaN(x) && !double.IsInfinity(x)) p[k] = ClampField(k, x);
                }
                if (p.Count > 0) r.Add((id, p));
            }
            return r;
        }

        static object Field(IDictionary d, string key) => d.Contains(key) ? d[key] : null;

        // Object.entries: словарь — его пары, список — номера как ключи; остальное — пусто
        static IEnumerable<(string, object)> Entries(object v)
        {
            if (v is IDictionary d)
            {
                foreach (DictionaryEntry e in d) yield return (Convert.ToString(e.Key, System.Globalization.CultureInfo.InvariantCulture), e.Value);
            }
            else if (v is IList l)
            {
                for (int i = 0; i < l.Count; i++) yield return (i.ToString(System.Globalization.CultureInfo.InvariantCulture), l[i]);
            }
        }

        // Правки фракции ищутся по названию без учёта регистра и пробелов по краям (factionOverrides)
        public static Dictionary<string, Dictionary<string, double>> FactionOverrides(TemplateOverrides o, string factionName)
        {
            if (o == null) return new Dictionary<string, Dictionary<string, double>>();
            string want = Js.Trim(factionName).ToLowerInvariant();
            foreach (var kv in o.Factions)
                if (kv.Key.ToLowerInvariant() == want) return kv.Value;
            return new Dictionary<string, Dictionary<string, double>>();
        }

        // Итоговый профиль (resolveTemplate): база → общие правки → правки фракции. Неизвестный шаблон — ополчение
        public static UnitTemplate Resolve(string id, string factionName, TemplateOverrides o)
        {
            var b = Get(id) ?? Base[0];
            var t = b.Clone();
            if (o != null && o.Base.TryGetValue(b.Id, out var common)) foreach (var kv in common) t[kv.Key] = kv.Value;
            if (FactionOverrides(o, factionName).TryGetValue(b.Id, out var own)) foreach (var kv in own) t[kv.Key] = kv.Value;
            return t;
        }
    }
}
