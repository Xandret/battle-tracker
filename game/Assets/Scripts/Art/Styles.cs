// ═══════════ Styles.cs — стили снаряжения XIV–XV веков (В16) и наборы снаряжения отряда ═══════════
// Стиль и набор — поля отряда в сохранении (`style`, `kit`), выбираются при создании отряда; нет — угадываем по имени.
// Набор — облик комплектов (Kits.Table): ополчение, копейщики, мечники, пикинёры, лучники, арбалетчики, рыцари,
// тяжёлые рыцари. Стиль пока только хранится — рисунок по стилям придёт с перерисовкой солдатиков (В15, шаг 2).
using System.Collections.Generic;
using System.Linq;

namespace Journal.Art
{
    public static class Styles
    {
        public static readonly (string Id, string Name, string Note)[] All =
        {
            ("west",    "Западный",        "Франция, Англия, Империя: бацинеты, топфхелмы, треугольные щиты, сюрко, арбалеты и длинные луки"),
            ("north",   "Северный",        "Скандинавия, Русь, Балтика: шишаки, круглые и каплевидные щиты, кольчуга и чешуя, топоры, плащи"),
            ("east",    "Восточный",       "Византия, степь, Персия: ламелляр, остроконечные шлемы с бармицей, круглые щиты, сабли, составные луки"),
            ("south",   "Южный",           "Италия, Иберия, мавры: бригантины, салады и барбюты, адарги, дротики"),
            ("fareast", "Дальневосточный", "Япония: о-ёрой, кабуто, нагината, юми, сасимоно"),
        };
        public const string Default = "west";
        public static string NameOf(string id) => All.FirstOrDefault(s => s.Id == id).Name ?? All[0].Name;
        public static bool Known(string id) => All.Any(s => s.Id == id);

        // по имени отряда; не угадали — null (тогда — стиль прошлого отряда фракции или Западный)
        static readonly (string Id, string[] Words)[] Hints =
        {
            ("fareast", new[] { "самура", "ронин", "асигару", "сохей", "ниндзя", "катан", "нагинат" }),
            ("north",   new[] { "викинг", "хускарл", "дружин", "варяг", "ярл", "хирд", "берсерк", "норд" }),
            ("east",    new[] { "мамлюк", "гулям", "степ", "кочев", "катафракт", "визант", "печенег", "половц", "монгол", "татар", "янычар", "сипах", "конные лучники" }),
            ("south",   new[] { "кондотьер", "мавр", "альмогавар", "генуэз", "венеци", "арагон", "кастил", "иберий", "андалус" }),
        };
        public static string Guess(string name)
        {
            var n = (name ?? "").ToLowerInvariant().Replace('ё', 'е');
            foreach (var (id, words) in Hints) if (words.Any(w => n.Contains(w))) return id;
            return null;
        }
    }

    public static class KitSets
    {
        public static readonly (string Id, string Name, string Tpl)[] All =
        {
            ("militia",  "Ополчение",       "militia"),
            ("spear",    "Копейщики",       "infantry"),
            ("sword",    "Мечники",         "guard"),
            ("pike",     "Пикинёры",        "pikemen"),
            ("bow",      "Лучники",         "archers"),
            ("crossbow", "Арбалетчики",     "crossbowmen"),
            ("lance",    "Рыцари",          "knights"),
            ("barded",   "Тяжёлые рыцари",  "elite_cavalry"),
        };
        public static string NameOf(string id) => All.FirstOrDefault(s => s.Id == id).Name ?? id;
        public static bool Known(string id) => All.Any(s => s.Id == id);
        // шаблон, чей облик — этот набор (облик в смотрелке берётся из шаблона: Kits.LookByTpl)
        public static string TplOf(string id) => All.FirstOrDefault(s => s.Id == id).Tpl;

        // по имени и роду войск (как угадывала смотрелка для сохранений)
        public static string Guess(string name, string type, string weapon = null)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("арбалет")) return "crossbow";
            if (n.Contains("лучник") || n.Contains("охотник") || n.Contains("стрелк") || type == "archer" || weapon == "ranged" && type != "cavalry") return "bow";
            if (n.Contains("пикин") || n.Contains("алебард") || type == "pike") return "pike";
            if (type == "cavalry") return n.Contains("элит") || n.Contains("тяжел") || n.Contains("тяжёл") ? "barded" : "lance";
            if (n.Contains("страж") || n.Contains("гвард") || n.Contains("мечник") || n.Contains("рыцар") || n.Contains("латник")) return "sword";
            if (n.Contains("ополч") || n.Contains("крестьян") || n.Contains("новобран")) return "militia";
            return "spear";
        }
    }
}
