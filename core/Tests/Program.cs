// ═══════════ Тесты движка на C# ═══════════
// Главный тест — эталон v29: 400 сценариев из shared/golden отыгрываются так же, как в трекере,
// строка в строку. Запуск: dotnet run --project Tests (из core/). Код возврата 0 — всё зелёное.
// dotnet run --project Tests -- calibrate — мишень для поштучной модели: shared/calibration/tabletop.*
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

// ── И1: строй, фигурки, ход за столом ──
Test("строй в метрах совпадает с трекером (battlemap.footprint)", () =>
{
    var R = Rules.Base;
    Footprint F(string type, double n) => Formation.Of(new Unit { Type = type, Soldiers = n }, R);
    Eq((F("infantry", 1000).Front, F("infantry", 1000).Depth), (125.0, 8.0), "пехота 1000");
    Eq((F("cavalry", 1000).Front, F("cavalry", 1000).Depth), (300.0, 15.0), "конница 1000");
    Eq((F("pike", 1000).Front, F("pike", 1000).Depth), (100.0, 10.0), "пикинёры 1000");
    Eq((F("archer", 300).Front, F("archer", 300).Depth), (60.0, 5.0), "стрелки 300");
    Eq((F("infantry", 500).Front, F("infantry", 500).Depth), (63.0, 8.0), "потери сужают фронт");
    Eq((F("infantry", 3).Front, F("infantry", 3).Depth), (1.0, 3.0), "горстка в одну колонну");
});
Test("раскладка на фигурки: 1000 пехоты по 10 — 8 шеренг по 13 фигурок", () =>
{
    var u = new Unit { Id = 7, Type = "infantry", Soldiers = 1000 };
    var figs = Formation.Layout(u, 10, Rules.Base);
    Eq(figs.Count, 104, "фигурок");
    Eq(figs.Sum(f => f.Men), 1000.0, "бойцов всего");
    Eq(figs.Max(f => f.Rank), 7, "последняя шеренга");
    var front = figs.Where(f => f.Rank == 0).ToList();
    Eq(front.Count, 13, "фигурок в передней шеренге");
    Eq(front.Sum(f => f.Width), 125.0, "ширина передней шеренги = фронт строя");
    Eq(front[12].Men, 5.0, "последняя фигурка шеренги неполная");
    Eq(front.Min(f => f.X - f.Width / 2), -62.5, "левый край строя");
    Eq(front[0].Y, -3.5, "передняя шеренга — у переднего края");
    Eq(Formation.Layout(u, 1, Rules.Base).Count, 1000, "1:1 — по фигурке на бойца");
    Eq(Formation.Layout(new Unit { Type = "cavalry", Soldiers = 1000 }, 10, Rules.Base).Count, 100, "конница 5 × 200 по 10");
});
Test("масштаб фигурок под битву (Г24)", () =>
{
    Eq(Formation.ScaleFor(2500), 1.0, "стычка 1:1");
    Eq(Formation.ScaleFor(33000), 10.0, "Второй Пикшарп 1:10");
    Eq(Formation.ScaleFor(90000), 20.0, "огромное сражение 1:20");
});
Test("ход за столом: одно зерно — один исход; натиск бьёт сильнее", () =>
{
    var inf = Templates.Get("infantry"); var kn = Templates.Get("knights");
    TurnOutcome Run(bool charge, uint seed) => Tabletop.Turn(kn.Make(1, "Рыцари", 1000, 1), inf.Make(2, "Пехота", 1000, 2),
        new TurnSetup { ChargeA = charge }, new EngineContext { Rng = new Mulberry32(seed).Next });
    var x = Run(true, 5); var y = Run(true, 5);
    Eq((x.LossA, x.LossB), (y.LossA, y.LossB), "воспроизводимость");
    double withCharge = 0, without = 0;
    for (uint s = 1; s <= 200; s++) { withCharge += Run(true, s).LossB; without += Run(false, s).LossB; }
    if (!(withCharge > without * 1.5)) throw new Exception($"натиск: {withCharge} против {without}");
});

if (args.Length > 0 && args[0] == "calibrate") { Calibrate(); return 0; }

void Calibrate()
{
    // Матрица Г21: пехота, конница, пики, стрелки × поле, лес, холм; конница — с натиском и без; плюс фланг и тыл
    string[] types = { "infantry", "knights", "pikemen", "archers" };
    var setups = new List<(string A, string B, Ground G, bool Charge, string Sector)>();
    foreach (var g in new[] { Ground.Field, Ground.Forest, Ground.Hill })
        foreach (var ta in types) foreach (var tb in types)
        {
            setups.Add((ta, tb, g, false, "front"));
            if (ta == "knights") setups.Add((ta, tb, g, true, "front"));
        }
    foreach (var sec in new[] { "flank", "rear" })
    {
        setups.Add(("infantry", "infantry", Ground.Field, false, sec));
        setups.Add(("knights", "infantry", Ground.Field, true, sec));
    }
    const int RUNS = 200;
    var rows = new List<object>();
    var md = new System.Text.StringBuilder();
    md.AppendLine("# Мишень поштучной модели: ход рукопашной за столом (Г21)");
    md.AppendLine();
    md.AppendLine($"По {RUNS} ходов на пару, отряды по 1000 из шаблонов v30.2, A бьёт первым. Считает настоящий ResolveBattle трекера.");
    md.AppendLine("Потери — убитые + раненые за ход. Холм: A выше B (черновик 6а). Сгенерировано `dotnet run --project Tests -- calibrate`.");
    md.AppendLine();
    md.AppendLine("| A | B | местность | натиск | сектор | потери A: среднее ± откл. | потери B: среднее ± откл. | B в 10–90% | B сломлен |");
    md.AppendLine("|---|---|---|---|---|---|---|---|---|");
    string Ru(Ground g) => g == Ground.Field ? "поле" : g == Ground.Forest ? "лес" : "холм";
    int idx = 0;
    foreach (var s in setups)
    {
        idx++;
        var ta = Templates.Get(s.A); var tb = Templates.Get(s.B);
        var la = new List<double>(); var lb = new List<double>(); int broken = 0;
        for (int i = 0; i < RUNS; i++)
        {
            var ctx = new EngineContext { Rng = new Mulberry32((uint)(idx * 100000 + i + 1)).Next };
            var o = Tabletop.Turn(ta.Make(1, ta.Name, 1000, 1), tb.Make(2, tb.Name, 1000, 2),
                new TurnSetup { Ground = s.G, ChargeA = s.Charge, SectorA = s.Sector }, ctx);
            la.Add(o.LossA); lb.Add(o.LossB);
            if (o.MoraleB == 0) broken++;
        }
        var sa = Stat.Of(la); var sb = Stat.Of(lb);
        rows.Add(new { a = s.A, b = s.B, ground = s.G.ToString().ToLowerInvariant(), charge = s.Charge, sector = s.Sector, runs = RUNS,
                       lossA = sa, lossB = sb, brokenB = broken / (double)RUNS });
        md.AppendLine($"| {ta.Name} | {tb.Name} | {Ru(s.G)} | {(s.Charge ? "да" : "—")} | {s.Sector} | {sa.Mean:0} ± {sa.Sd:0} | {sb.Mean:0} ± {sb.Sd:0} | {sb.P10:0}–{sb.P90:0} | {broken * 100 / RUNS}% |");
    }
    var dir = Path.Combine(root, "shared", "calibration");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "tabletop.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    File.WriteAllText(Path.Combine(dir, "tabletop.md"), md.ToString());
    Console.WriteLine($"мишень: {setups.Count} стычек × {RUNS} ходов → {dir}");
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
