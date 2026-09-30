// ═══════════ Тесты движка на C# ═══════════
// Главный тест — эталон v29: 400 сценариев из shared/golden отыгрываются так же, как в трекере,
// строка в строку. Запуск: dotnet run --project Tests (из core/). Код возврата 0 — всё зелёное.
using System.Text.Json;
using BattleCore;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Eq<T>(T actual, T expected, string what)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new Exception($"{what}: ожидалось «{expected}», получено «{actual}»");
}

string root = FindRoot();
JsonElement ReadJson(string rel) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, rel))).RootElement;

// ── основа: генератор и числа как в JS ──
Test("mulberry32 совпадает с JS", () =>
{
    var r = new Mulberry32(42u);
    double[] js = { 0.6011037519201636, 0.44829055899754167, 0.8524657934904099, 0.6697340414393693, 0.17481389874592423 };
    foreach (var v in js) Eq(r.Next(), v, "mulberry32(42)");
    var q = new Mulberry32(4294967295u);
    Eq(q.Next(), 0.8964226141106337, "mulberry32(2³²−1) #1");
    Eq(q.Next(), 0.189478256739676, "mulberry32(2³²−1) #2");
});
Test("округление и печать чисел как в JS", () =>
{
    Eq(Js.Round(2.5), 3.0, "Math.round(2.5)");
    Eq(Js.Round(-2.5), -2.0, "Math.round(−2.5)");
    Eq(Js.Round(0.49999999999999994), 0.0, "Math.round(0.49999999999999994)");
    Eq(Js.R1(1323.94), 1323.9, "r1(1323.94)");
    Eq(Js.Num(Js.R1(-0.04)), "0", "минус ноль печатается как 0");
    Eq(Js.Num(0.1 + 0.2), "0.30000000000000004", "String(0.1 + 0.2)");
    Eq(Js.Num(1323.9), "1323.9", "String(1323.9)");
    Eq(Js.Num(415), "415", "целое без .0");
});

// ── эталон v29: как engine.test.mjs трекера ──
var scenarios = ReadJson("shared/golden/scenarios.json");
var golden = ReadJson("shared/golden/golden.json");
Test("эталон содержит все сценарии", () => Eq(golden.GetArrayLength(), scenarios.GetArrayLength(), "число сценариев"));
foreach (var sc in scenarios.EnumerateArray())
{
    int n = sc.GetProperty("n").GetInt32();
    var g = golden[n];
    Test($"сценарий {n}: {sc.GetProperty("action").GetProperty("kind").GetString()}", () => RunScenario(sc, g));
}

void RunScenario(JsonElement sc, JsonElement g)
{
    var st = sc.GetProperty("state");
    var units = st.GetProperty("units").EnumerateArray().Select(UnitFromJson).ToList();
    var commanders = st.GetProperty("commanders").EnumerateArray().Select(CommanderFromJson).ToList();
    var factions = st.GetProperty("factions").EnumerateArray()
        .ToDictionary(f => f.GetProperty("id").GetInt32(), f => f.GetProperty("name").GetString());
    var rng = new Mulberry32(sc.GetProperty("seed").GetDouble());
    var ctx = new EngineContext
    {
        Rules = Rules.Base,
        Rng = rng.Next,
        CommanderOf = u => commanders.FirstOrDefault(c => c.Id == u.CommanderId),
        FactionName = id => id.HasValue && factions.TryGetValue(id.Value, out var name) ? name : "Без фракции",
    };
    void Apply(int id, Patch p) { var u = units.First(x => x.Id == id); p.ApplyTo(u); }

    var a = sc.GetProperty("action");
    string kind = a.GetProperty("kind").GetString();
    string title, tone; List<string> lines;
    double turn = st.GetProperty("turn").GetDouble();
    if (kind == "battle")
    {
        var A = units.First(u => u.Id == a.GetProperty("att").GetInt32());
        var B = units.First(u => u.Id == a.GetProperty("def").GetInt32());
        var q = a.GetProperty("req");
        var req = new BattleRequest
        {
            Mode = q.GetProperty("mode").GetString(),
            SitPct = q.GetProperty("sitPct").GetDouble(),
            FatigueMode = q.GetProperty("fatigueMode").GetString(),
            Mutual = q.GetProperty("mutual").GetBoolean(),
            Charge = q.GetProperty("charge").GetBoolean(),
            CounterCharge = q.GetProperty("counterCharge").GetBoolean(),
        };
        var r = Combat.ResolveBattle(A, B, req, ctx);
        foreach (var p in r.Patches) Apply(p.Id, p.Patch);
        title = r.Title; lines = r.Lines; tone = r.Tone;
    }
    else if (kind == "morale" || kind == "flee")
    {
        var u = units.First(x => x.Id == a.GetProperty("id").GetInt32());
        var r = kind == "morale" ? MoraleRules.MoraleCheck(u, ctx) : MoraleRules.FleeCheck(u, ctx);
        Apply(u.Id, r.Patch);
        title = r.Title; lines = r.Lines; tone = r.Tone;
    }
    else
    {
        var r = Turn.EndTurn(units, ctx);
        units = r.Units; turn += 1;
        title = $"— Конец хода {Js.Num(turn - 1)} —";
        lines = r.Lines.Count > 0 ? r.Lines : new List<string> { "Без изменений" };
        tone = "info";
    }

    var gl = g.GetProperty("log");
    Eq(title, gl.GetProperty("title").GetString(), "заголовок записи журнала");
    var glines = gl.GetProperty("lines").EnumerateArray().Select(x => x.GetString()).ToList();
    for (int i = 0; i < Math.Max(lines.Count, glines.Count); i++)
        Eq(i < lines.Count ? lines[i] : "(нет строки)", i < glines.Count ? glines[i] : "(нет строки)", $"строка журнала {i + 1}");
    Eq(tone, gl.GetProperty("tone").GetString(), "тон записи");
    Eq(turn, g.GetProperty("turn").GetDouble(), "номер хода");
    foreach (var gu in g.GetProperty("units").EnumerateArray())
    {
        var eu = units.First(u => u.Id == gu.GetProperty("id").GetInt32());
        foreach (var prop in gu.EnumerateObject())
            Eq(FieldOf(eu, prop.Name), JsonValue(prop.Value), $"отряд {eu.Id}, поле {prop.Name}");
    }
}

// ── прогон ──
int failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); }
    catch (Exception e)
    {
        failed++;
        if (failed <= 20) Console.WriteLine($"✘ {name}\n    {e.Message}");
    }
}
Console.WriteLine($"\nтестов {tests.Count} · прошло {tests.Count - failed} · упало {failed}");
return failed == 0 ? 0 : 1;

// ── помощники ──
static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !Directory.Exists(Path.Combine(d.FullName, "shared", "golden"))) d = d.Parent;
    return d?.FullName ?? throw new Exception("не найдена папка shared/golden");
}

static int? IntOrNull(JsonElement e, string name) =>
    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : (int?)null;
static double Num(JsonElement e, string name, double def = 0) =>
    e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
static string Str(JsonElement e, string name, string def) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;

static Unit UnitFromJson(JsonElement e) => new Unit
{
    Id = e.GetProperty("id").GetInt32(), Name = Str(e, "name", ""), Type = Str(e, "type", "infantry"), Weapon = Str(e, "weapon", "melee"),
    FactionId = IntOrNull(e, "factionId"), SubfactionId = IntOrNull(e, "subfactionId"), CommanderId = IntOrNull(e, "commanderId"),
    Soldiers = Num(e, "soldiers"), Initial = Num(e, "initial"), Discipline = Num(e, "discipline"), Morale = Num(e, "morale"),
    EqAtk = Num(e, "eqAtk"), EqDef = Num(e, "eqDef"), Exp = Num(e, "exp"), Mastery = Num(e, "mastery"), Fatigue = Num(e, "fatigue"),
    Status = Str(e, "status", "active"), TurnsActive = Num(e, "turnsActive"), FleeChecks = Num(e, "fleeChecks"),
    BreakGrace = Num(e, "breakGrace"), BreakPenalty = Num(e, "breakPenalty"), Broken = Bool(e, "broken"), Acted = Bool(e, "acted"),
    AttacksMade = Num(e, "attacksMade"), CountersMade = Num(e, "countersMade"), TotKilled = Num(e, "totKilled"), TotWounded = Num(e, "totWounded"),
    OnMap = Bool(e, "onMap"), MapX = Num(e, "mapX", 50), MapY = Num(e, "mapY", 50), Facing = Num(e, "facing"), TokenScale = Num(e, "tokenScale", 1),
};
static Commander CommanderFromJson(JsonElement e) => new Commander
{
    Id = e.GetProperty("id").GetInt32(), Name = Str(e, "name", ""), FactionId = IntOrNull(e, "factionId"),
    BuffMorale = Num(e, "buffMorale"), BuffDisc = Num(e, "buffDisc"), BuffDmg = Num(e, "buffDmg"), BuffDef = Num(e, "buffDef"),
};

static object JsonValue(JsonElement v) => v.ValueKind switch
{
    JsonValueKind.Number => v.GetDouble(),
    JsonValueKind.String => v.GetString(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    _ => null,
};
static object FieldOf(Unit u, string name) => name switch
{
    "id" => (double)u.Id, "name" => u.Name, "type" => u.Type, "weapon" => u.Weapon,
    "factionId" => u.FactionId.HasValue ? (double)u.FactionId.Value : null,
    "subfactionId" => u.SubfactionId.HasValue ? (double)u.SubfactionId.Value : null,
    "commanderId" => u.CommanderId.HasValue ? (double)u.CommanderId.Value : null,
    "soldiers" => u.Soldiers, "initial" => u.Initial, "discipline" => u.Discipline, "morale" => u.Morale,
    "eqAtk" => u.EqAtk, "eqDef" => u.EqDef, "exp" => u.Exp, "mastery" => u.Mastery, "fatigue" => u.Fatigue,
    "status" => u.Status, "turnsActive" => u.TurnsActive, "fleeChecks" => u.FleeChecks, "breakGrace" => u.BreakGrace,
    "breakPenalty" => u.BreakPenalty, "broken" => u.Broken, "acted" => u.Acted, "attacksMade" => u.AttacksMade,
    "countersMade" => u.CountersMade, "totKilled" => u.TotKilled, "totWounded" => u.TotWounded, "onMap" => u.OnMap,
    "mapX" => u.MapX, "mapY" => u.MapY, "facing" => u.Facing, "tokenScale" => u.TokenScale,
    _ => throw new Exception($"поле эталона «{name}» не известно движку на C#"),
};
