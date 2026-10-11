// ═══════════ TemplateCases.cs — шаблоны отрядов: общие сценарии с трекером (правило 7) ═══════════
// Отыгрывает shared/golden/templates.json — его пишет tracker/tools/export-template-golden.mjs из tracker/tests/templatecases.mjs:
// тип войск по названию (guessUnitType), подбор шаблона (matchTemplate), чистка правок партии (normalizeOverrides),
// итоговый профиль (resolveTemplate) и пределы полей (clampField). C# обязан совпасть с трекером.
using System.Text.Json;
using BattleCore;

static class TemplateCases
{
    public static IEnumerable<(string Name, Action Run)> All(JsonElement g)
    {
        yield return ("шаблоны отрядов: базовые профили", () => BaseCase(g.GetProperty("base")));
        var names = g.GetProperty("names");
        yield return ($"шаблоны отрядов: тип войск и шаблон по {names.GetArrayLength()} названиям", () => Names(names));
        int k = 0;
        foreach (var c in g.GetProperty("overrides").EnumerateArray())
        {
            int n = k++;
            yield return ($"шаблоны отрядов: правки партии №{n + 1} — чистка и итоговые профили", () => Overrides(c, $"overrides[{n}]"));
        }
        yield return ("шаблоны отрядов: пределы и округление полей", () => Clamp(g.GetProperty("clamp")));
    }

    static void BaseCase(JsonElement list)
    {
        var d = new Diffs();
        d.Eq(Templates.Base.Count, list.GetArrayLength(), "base: число шаблонов");
        int i = 0;
        foreach (var e in list.EnumerateArray()) { if (i < Templates.Base.Count) Profile(e, Templates.Base[i], $"base[{i}]", d); i++; }
        d.Throw();
    }

    static void Names(JsonElement list)
    {
        var d = new Diffs();
        foreach (var c in list.EnumerateArray())
        {
            string name = c.GetProperty("name").ValueKind == JsonValueKind.Null ? null : c.GetProperty("name").GetString();
            string at = $"«{name ?? "null"}»";
            var g = Units.GuessType(name);
            var eg = c.GetProperty("guess");
            if (eg.ValueKind == JsonValueKind.Null) d.Eq(g == null ? null : g.Type, null, $"{at}: тип");
            else if (g == null) d.Add($"{at}: тип — трекер {eg.GetProperty("type").GetString()}, C# ничего");
            else
            {
                d.Eq(g.Type, eg.GetProperty("type").GetString(), $"{at}: тип");
                d.Eq(g.Weapon, eg.GetProperty("weapon").GetString(), $"{at}: оружие");
                d.Eq(g.Why, eg.GetProperty("why").GetString(), $"{at}: почему");
            }
            var m = Templates.Match(name);
            var em = c.GetProperty("match");
            d.Eq(m.Id, em.GetProperty("id").GetString(), $"{at}: шаблон");
            d.Eq(m.Fallback, em.GetProperty("fallback").GetBoolean(), $"{at}: наугад");
            d.Eq(m.Type, g?.Type, $"{at}: тип в подборе");
        }
        d.Throw();
    }

    static void Overrides(JsonElement c, string where)
    {
        var d = new Diffs();
        var o = Templates.NormalizeOverrides(Raw(c.GetProperty("input")));
        var en = c.GetProperty("normalized");
        Patches(en.GetProperty("base"), o.Base, $"{where}.base", d);
        var ef = en.GetProperty("factions");
        d.Eq(string.Join(" | ", o.Factions.Keys), string.Join(" | ", ef.EnumerateObject().Select(p => p.Name)), $"{where}.factions: фракции");
        foreach (var p in ef.EnumerateObject())
            if (o.Factions.TryGetValue(p.Name, out var own)) Patches(p.Value, own, $"{where}.factions[{p.Name}]", d);
        foreach (var r in c.GetProperty("resolve").EnumerateArray())
        {
            string id = r.GetProperty("id").GetString();
            string faction = r.GetProperty("faction").ValueKind == JsonValueKind.Null ? null : r.GetProperty("faction").GetString();
            Profile(r.GetProperty("out"), Templates.Resolve(id, faction, o), $"{where}: {id} для «{faction ?? "null"}»", d);
        }
        d.Throw();
    }

    // Правки одного уровня: шаблон → поле → число, тот же набор ключей
    static void Patches(JsonElement e, Dictionary<string, Dictionary<string, double>> a, string where, Diffs d)
    {
        d.Eq(string.Join(",", a.Keys.OrderBy(x => x, StringComparer.Ordinal)),
             string.Join(",", e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal)), $"{where}: шаблоны");
        foreach (var p in e.EnumerateObject())
        {
            if (!a.TryGetValue(p.Name, out var patch)) continue;
            d.Eq(string.Join(",", patch.Keys.OrderBy(x => x, StringComparer.Ordinal)),
                 string.Join(",", p.Value.EnumerateObject().Select(f => f.Name).OrderBy(x => x, StringComparer.Ordinal)), $"{where}.{p.Name}: поля");
            foreach (var f in p.Value.EnumerateObject())
                if (patch.TryGetValue(f.Name, out var v)) d.Eq(v, f.Value.GetDouble(), $"{where}.{p.Name}.{f.Name}");
        }
    }

    static void Profile(JsonElement e, UnitTemplate t, string where, Diffs d)
    {
        d.Eq(t.Id, e.GetProperty("id").GetString(), $"{where}: id");
        d.Eq(t.Name, e.GetProperty("name").GetString(), $"{where}: name");
        d.Eq(t.Type, e.GetProperty("type").GetString(), $"{where}: type");
        d.Eq(t.Weapon, e.GetProperty("weapon").GetString(), $"{where}: weapon");
        foreach (var k in new[] { "size", "discipline", "morale", "eqAtk", "eqDef", "exp", "mastery" })
            d.Eq(t[k], e.GetProperty(k).GetDouble(), $"{where}: {k}");
    }

    static void Clamp(JsonElement list)
    {
        var d = new Diffs();
        foreach (var c in list.EnumerateArray())
        {
            string k = c.GetProperty("k").GetString();
            double v = c.GetProperty("v").GetDouble();
            d.Eq(Templates.ClampField(k, v), c.GetProperty("out").GetDouble(), $"clampField({k}, {Js.Num(v)})");
        }
        d.Throw();
    }

    // JSON — в дерево, как его отдаёт разбор JSON в игре: словари по порядку ключей, списки, строки, числа, true/false, null
    static object Raw(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Aggregate(new Dictionary<string, object>(), (m, p) => { m[p.Name] = Raw(p.Value); return m; }),
        JsonValueKind.Array => e.EnumerateArray().Select(Raw).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    sealed class Diffs
    {
        const int Max = 8;
        readonly List<string> list = new List<string>();
        public void Add(string s) { if (list.Count < Max) list.Add(s); }
        public void Eq<T>(T actual, T expected, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected)) Add($"{what}: трекер «{expected}», C# «{actual}»");
        }
        public void Throw()
        {
            if (list.Count > 0) throw new Exception($"расхождений {(list.Count >= Max ? Max + "+" : list.Count.ToString())}:\n      " + string.Join("\n      ", list));
        }
    }
}
