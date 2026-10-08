// ═══════════ Тесты движка на C# ═══════════
// Главный тест — эталон v29: 400 сценариев из shared/golden отыгрываются так же, как в трекере,
// строка в строку. Запуск: dotnet run --project Tests (из core/). Код возврата 0 — всё зелёное.
// dotnet run --project Tests -- calibrate [ходов рукопашной] [ходов стрельбы] — сверка модели со столом: shared/calibration/*
// (или calibrate-melee / calibrate-ranged по отдельности)
// dotnet run --project Tests -- polygon — полигон движения (Г55): core/polygon/polygon.html
// dotnet run -c Release --project Tests -- bench-paths [зазор между линиями, м] [figs] — замер путей (FlowField) на карте 4000 × 3000 м; figs — старые фигурки-капсулы
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
Test("раскладка на фигурки (Г32, старый режим тел — Rules.Figures): 1000 пехоты по 10 — квадратики 5 × 2, 4 ряда по 25", () =>
{
    var u = new Unit { Id = 7, Type = "infantry", Soldiers = 1000 };
    Eq(Formation.Shape(u, 10, Rules.Figures), (5, 2), "форма пехоты");
    var figs = Formation.Layout(u, 10, Rules.Figures);
    Eq(figs.Count, 100, "фигурок");
    Eq(figs.Sum(f => f.Men), 1000.0, "бойцов всего");
    Eq(figs.Max(f => f.Rank), 3, "последний ряд квадратиков");
    var front = figs.Where(f => f.Rank == 0).ToList();
    Eq(front.Count, 25, "квадратиков в переднем ряду");
    Eq(front.Sum(f => f.Width), 125.0, "ширина переднего ряда = фронт строя");
    Eq((front[0].Width, front[0].Depth, front[0].Men), (5.0, 2.0, 10.0), "квадратик 5 × 2 м");
    Eq(front.Min(f => f.X - f.Width / 2), -62.5, "левый край строя");
    Eq(front[0].Y, -3.0, "передний ряд — у переднего края (шеренги 1–2)");
    var half = Formation.Layout(new Unit { Type = "infantry", Soldiers = 500 }, 10, Rules.Figures);
    Eq(half.Sum(f => f.Men), 500.0, "500 бойцов");
    Eq(half.Where(f => f.Rank == 0).Sum(f => f.Width), 63.0, "потери сужают фронт — как фишка трекера");
    Eq(Formation.Layout(u, 1, Rules.Figures).Count, 1000, "1:1 — по фигурке на бойца");
    Eq(Formation.Shape(new Unit { Type = "pike", Soldiers = 1000 }, 10, Rules.Figures), (5, 2), "пикинёры: 10 шеренг — тоже 5 × 2");
    Eq(Formation.Shape(new Unit { Type = "archer", Soldiers = 1000 }, 10, Rules.Figures), (2, 5), "стрелки: 5 шеренг — колонки 2 × 5");
    Eq(Formation.Shape(new Unit { Type = "cavalry", Soldiers = 1000 }, 10, Rules.Figures), (10, 1), "конница: 5 шеренг — 10 всадников в ряд");
    Eq(Formation.Layout(new Unit { Type = "cavalry", Soldiers = 1000 }, 10, Rules.Figures).Count, 100, "конница 5 × 200 по 10");
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

// ── И1: поштучная рукопашная (Г26–Г30) ──
TurnOutcome Mel(string ta, string tb, uint seed, TurnSetup s = null, MeleeOptions o = null)
{
    s ??= new TurnSetup(); o ??= new MeleeOptions { FullContact = true };
    var A = Templates.Get(ta); var B = Templates.Get(tb);
    var (pa, pb) = MeleeSim.Setup(A.Make(1, A.Name, 1000, 1), B.Make(2, B.Name, 1000, 2), s, o, Rules.Base);
    return MeleeSim.Turn(pa, pb, s.ChargeA, s.Ground, new EngineContext { Rng = new Mulberry32(seed).Next }, o);
}
Test("поштучная рукопашная: одно зерно — один исход, в конце хода целые солдаты", () =>
{
    var x = Mel("infantry", "infantry", 7); var y = Mel("infantry", "infantry", 7);
    Eq((x.LossA, x.LossB), (y.LossA, y.LossB), "воспроизводимость");
    Eq(x.LossA == Math.Floor(x.LossA) && x.LossB == Math.Floor(x.LossB), true, "целые солдаты");
    Eq(x.Strikes, 4, "пехота на пехоту: у каждой стороны атака и ответ — 4 окна");
    Eq(Mel("guard", "infantry", 7).Strikes, 5, "гвардия (дисц. 80+): 2 атаки + ответ; пехота: атака + 1 ответ");
});
Test("натиск — всплеск (Г29): первые 2 с цель не отвечает, потом бьются", () =>
{
    var o = Mel("knights", "infantry", 3, new TurnSetup { ChargeA = true });
    var at2 = o.Timeline.First(p => p[0] == 2);
    Eq(at2[1], 1000.0, "рыцари никого не потеряли за первые 2 с");
    Eq(at2[2] < 1000, true, "пехота уже теряет людей");
    Eq(o.LossA > 0, true, "потом пехота бьёт своей атакой");
    var p = Mel("knights", "pikemen", 3, new TurnSetup { ChargeA = true });
    Eq(p.Timeline.First(q => q[0] == 2)[1] < 1000, true, "пики во фронт гасят натиск — рыцари теряют людей с первых секунд");
});
Test("полный контакт (Г27): пехота на пехоту — средние потери стола ±10%", () =>
{
    double tA = 0, tB = 0, mA = 0, mB = 0;
    var inf = Templates.Get("infantry");
    for (uint i = 1; i <= 200; i++)
    {
        var t = Tabletop.Turn(inf.Make(1, "A", 1000, 1), inf.Make(2, "B", 1000, 2), new TurnSetup(), new EngineContext { Rng = new Mulberry32(i).Next });
        var m = Mel("infantry", "infantry", i + 5000);
        tA += t.LossA; tB += t.LossB; mA += m.LossA; mB += m.LossB;
    }
    if (Math.Abs(mA / tA - 1) > 0.1 || Math.Abs(mB / tB - 1) > 0.1)
        throw new Exception($"стол {tA / 200:0}/{tB / 200:0}, модель {mA / 200:0}/{mB / 200:0}");
});
Test("лоб в лоб без заворота (Г27): рыцари 300 м против пехоты 125 м — в бою половина рыцарей", () =>
{
    var o = Mel("knights", "infantry", 1, null, new MeleeOptions());
    if (!(o.EngagedA >= 0.45 && o.EngagedA <= 0.55)) throw new Exception($"рыцари в бою: {o.EngagedA:0.00}");
    Eq(o.EngagedB, 1.0, "пехота в бою вся");
});

// ── И1: стрельба по баллистике (Г33, Г37–Г41) ──
TurnOutcome Shoot(string ta, string tb, uint seed, TurnSetup s = null, RangedOptions o = null, Rules r = null, Action<Placed, Placed> tweak = null)
{
    s ??= new TurnSetup(); o ??= new RangedOptions(); r ??= Rules.Base;
    var A = Templates.Get(ta); var B = Templates.Get(tb);
    var (pa, pb) = RangedSim.Setup(A.Make(1, A.Name, 1000, 1), B.Make(2, B.Name, 1000, 2), s, r);
    tweak?.Invoke(pa, pb);
    return RangedSim.Turn(pa, pb, new EngineContext { Rules = r, Rng = new Mulberry32(seed).Next }, o);
}
Test("баллистика: скорость лука, полёт без воздуха по школьной формуле, прицел в точку", () =>
{
    var bow = Rules.Base.Ranged.Bows["longbow"];
    if (Math.Abs(Ballistics.V0(bow) - 56.92) > 0.05) throw new Exception($"v0 = {Ballistics.V0(bow)}");
    double v = 50, th = 20 * Math.PI / 180, d = 120;
    double exact = d * Math.Tan(th) - 9.81 * d * d / (2 * v * v * Math.Cos(th) * Math.Cos(th));
    double got = Ballistics.HeightAt(v, th, 0, 9.81, d, 0.02);
    if (Math.Abs(got - exact) > 0.01) throw new Exception($"z({d}) = {got:0.000}, должно {exact:0.000}");
    double k = Ballistics.DragK(bow, Rules.Base.Ranged), v0 = Ballistics.V0(bow);
    double low = Ballistics.Aim(v0, k, 9.81, 100, -0.4, 0.02).Value;
    if (Math.Abs(Ballistics.HeightAt(v0, low, k, 9.81, 100, 0.02) + 0.4) > 0.01) throw new Exception("настильный прицел мимо");
    double high = Ballistics.Aim(v0, k, 9.81, 100, -0.4, 0.02, high: true).Value;
    if (Math.Abs(Ballistics.HeightAt(v0, high, k, 9.81, 100, 0.02) + 0.4) > 0.01) throw new Exception("навесной прицел мимо");
    Eq(high > low + 0.5, true, "навес круче настильного");
    Eq(Ballistics.Aim(v0, k, 9.81, 400, 0, 0.02) == null, true, "400 м боевому луку не достать");
});
Test("стрельба: одно зерно — один исход; по своим не бьют; пехота на 100 м не отвечает", () =>
{
    var x = Shoot("archers", "infantry", 11); var y = Shoot("archers", "infantry", 11);
    Eq((x.LossA, x.LossB, x.Shots.Arrows), (y.LossA, y.LossB, y.Shots.Arrows), "воспроизводимость");
    Eq(x.Shots.HitsFriendly, 0L, "стрелы по своим на 100 м");
    Eq(x.LossA, 0.0, "потери стрелков");
    Eq(x.Shots.Arrows > 1000 && x.Shots.Arrows < 8000, true, $"стрел за ход: {x.Shots.Arrows}");
    Eq(Shoot("archers", "archers", 11).LossA > 0, true, "лучники-цель отвечают стрелами");
});
Test("опорная стычка (Г37): лучники → пехота, 100 м — средние стола ±10%", () =>
{
    double t = 0, m = 0; var A = Templates.Get("archers"); var B = Templates.Get("infantry");
    for (uint i = 1; i <= 200; i++)
    {
        t += TabletopVolley.Turn(A.Make(1, "A", 1000, 1), B.Make(2, "B", 1000, 2), new TurnSetup(), new EngineContext { Rng = new Mulberry32(i).Next }).LossB;
        m += Shoot("archers", "infantry", i + 3000).LossB;
    }
    if (Math.Abs(m / t - 1) > 0.1) throw new Exception($"стол {t / 200:0}, модель {m / 200:0}");
});
Test("баллистика сверх стола: вблизи смертоноснее, в спину вдвое, лес укрывает (Г38)", () =>
{
    double Mean(Func<uint, TurnOutcome> f) { double s = 0; for (uint i = 1; i <= 40; i++) s += f(i).LossB; return s / 40; }
    double at100 = Mean(i => Shoot("archers", "infantry", i));
    double at30 = Mean(i => Shoot("archers", "infantry", i, new TurnSetup { Distance = 30 }));
    double back = Mean(i => Shoot("archers", "infantry", i, null, null, null, (pa, pb) => pb.Facing = 180));
    double forest = Mean(i => Shoot("archers", "infantry", i, null, new RangedOptions { Forest = (x, y) => y > -14 }));
    if (!(at30 > at100 * 1.2)) throw new Exception($"30 м: {at30:0}, 100 м: {at100:0}");
    if (!(back > at100 * 1.6)) throw new Exception($"в спину: {back:0}, в лицо: {at100:0}");
    if (!(forest < at100 * 0.9)) throw new Exception($"в лесу: {forest:0}, в поле: {at100:0}");
});

// ── карта 6а: общие сценарии с трекером (правило 7) — shared/golden/map.json, см. MapCases.cs ──
if (args.Length > 0 && args[0] == "bench-paths") { BenchPaths(args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 400, args.Contains("figs"), args.Contains("reserve")); return 0; }
if (args.Length > 0 && args[0] == "orders") { foreach (var (n, r) in OrdersTests.All()) { try { r(); Console.WriteLine("✓ " + n); } catch (Exception e) { Console.WriteLine("✘ " + n + ": " + e.Message); } } return 0; }
if (args.Length > 0 && args[0] == "b1-cross") { MenBodyProbe.Cross(); return 0; }
if (args.Length > 0 && args[0] == "b1-river") { MenBodyProbe2.River(); return 0; }
if (args.Length > 0 && args[0] == "b1-bench") { MenBodyBench.Run(); return 0; }
if (args.Length > 0 && args[0] == "battle-figs") { BattleTests.Use = Rules.Figures; foreach (var (n, r) in BattleTests.All()) { var sw = System.Diagnostics.Stopwatch.StartNew(); try { r(); Console.WriteLine($"✓ {n} ({sw.Elapsed.TotalSeconds:0.0} с)"); } catch (Exception e) { Console.WriteLine($"✘ {n}: {e.Message} ({sw.Elapsed.TotalSeconds:0.0} с)"); } } return 0; }
if (args.Length > 0 && args[0] == "b2") { MenMeleeProbe.Run(args.Length > 1 ? args[1] : null); return 0; }
if (args.Length > 0 && args[0] == "b3-rally") { MenRallyProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "b3-unwrap") { MenUnwrapProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "b3-g44") { MenG44Probe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g94b") { MenTurnBattleProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g90-pike") { MenPikeFrontProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g90-depth") { MenChargeDepthProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g90") { MenChargeProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g86") { MenRigidProbe.Run(args.Skip(1).ToArray()); return 0; }
if (args.Length > 0 && args[0] == "g94") { MenTurnProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "g81") { MenRetreatProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "b3-flee") { MenFleeProbe.Run(); return 0; }
if (args.Length > 0 && args[0] == "b2-mix") { MenMeleeMix.Run(); return 0; }
if (args.Length > 0 && args[0] == "b2-knights") { MenMeleeKnights.Run(); return 0; }
if (args.Length > 0 && args[0] == "b2-flank") { MenMeleeFlank.Run(); return 0; }
if (args.Length > 0 && args[0] == "b1-ov") { MenBodyProbe3.Overlaps(args.Skip(1).ToArray()); return 0; }
if (args.Length > 0 && args[0] == "b1") { foreach (var (n, r) in MenBodyTests.All()) { var sw = System.Diagnostics.Stopwatch.StartNew(); try { r(); Console.WriteLine($"✓ {n} ({sw.Elapsed.TotalSeconds:0.0} с)"); } catch (Exception e) { Console.WriteLine($"✘ {n}: {e.Message} ({sw.Elapsed.TotalSeconds:0.0} с)"); } } return 0; }
if (args.Length > 0 && args[0] == "men") { foreach (var (n, r) in MenTests.All()) { try { r(); Console.WriteLine("✓ " + n); } catch (Exception e) { Console.WriteLine("✘ " + n + ": " + e.Message); } } return 0; }
if (args.Length > 0 && args[0] == "jumps") { var all = Polygon.Jumps(); Console.WriteLine($"прыжков {all.Count}"); foreach (var l in all.Take(20)) Console.WriteLine("  " + l); return 0; }
foreach (var (name, run) in MapCases.All(ReadJson("shared/golden/map.json"))) Test(name, run);

// ── шаблоны отрядов: общие сценарии с трекером (правило 7) — shared/golden/templates.json, см. TemplateCases.cs ──
if (args.Length > 0 && args[0] == "templates") { foreach (var (n, r) in TemplateCases.All(ReadJson("shared/golden/templates.json"))) { try { r(); Console.WriteLine("✓ " + n); } catch (Exception e) { Console.WriteLine("✘ " + n + ": " + e.Message); } } return 0; }
foreach (var (name, run) in TemplateCases.All(ReadJson("shared/golden/templates.json"))) Test(name, run);

// ── И1: движение строя фигурками (Г31 шаг 1; Г52–Г54), см. MoveTests.cs ──
foreach (var (name, run) in MoveTests.All()) Test(name, run);

// ── И1: бой в движении (БД1; Г62–Г64), см. BattleTests.cs ──
foreach (var (name, run) in BattleTests.All()) Test(name, run);
// ── данные для рисунка (В6), см. LookTests.cs ──
foreach (var (name, run) in LookTests.All()) Test(name, run);
// ── живые бойцы (Г75–Г78), см. MenTests.cs ──
foreach (var (name, run) in MenTests.All()) Test(name, run);
foreach (var (name, run) in MenBodyTests.All()) Test(name, run);   // Б1: бойцы — тела (Г82, Г92)
// ── приказы и ход (И2, Г79–Г81), см. OrdersTests.cs ──
foreach (var (name, run) in OrdersTests.All()) Test(name, run);
Test("тела не прыгают: ни тело, ни фигурка в кадре полигона не сдвигается за шаг дальше 45 м/с (мост, давка, бой с потерями)", () =>
{
    var j = Polygon.Jumps("Река: брод и мост", "Бой: фланг и потери");
    if (j.Count > 0) throw new Exception($"прыжков {j.Count}: " + string.Join(" | ", j.Take(3)));
});

if (args.Length > 0 && args[0] == "polygon") { Polygon.Write(root); return 0; }
if (args.Length > 0 && args[0] == "calibrate") { Calibrate(args.Length > 1 ? int.Parse(args[1]) : 1000); CalibrateRanged(args.Length > 2 ? int.Parse(args[2]) : 400); return 0; }
if (args.Length > 0 && args[0] == "calibrate-melee") { Calibrate(args.Length > 1 ? int.Parse(args[1]) : 1000); return 0; }
if (args.Length > 0 && args[0] == "calibrate-ranged") { CalibrateRanged(args.Length > 1 ? int.Parse(args[1]) : 400); return 0; }

void Calibrate(int RUNS)
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
    // Г21: не меньше 200 ходов на пару; по умолчанию 1000 — при 200 шум средних (~4% в лесу) сам съедает допуск ±10%
    string Ru(Ground g) => g == Ground.Field ? "поле" : g == Ground.Forest ? "лес" : "холм";
    string SecRu(string s) => s == "front" ? "фронт" : s == "flank" ? "фланг" : "тыл";
    string Pct(double model, double table) => table == 0 ? "—" : $"{(model / table - 1) * 100:+0;−0;0}%";
    bool Close(double model, double table) => Math.Abs(model - table) <= Math.Max(5, 0.10 * table);
    EngineContext Ctx(int seed) => new EngineContext { Rng = new Mulberry32((uint)seed).Next };

    var target = new System.Text.StringBuilder();
    target.AppendLine("# Мишень поштучной модели: ход рукопашной за столом (Г21)");
    target.AppendLine();
    target.AppendLine($"По {RUNS} ходов на пару, отряды по 1000 из шаблонов v30.2, A бьёт первым. Считает настоящий ResolveBattle трекера.");
    target.AppendLine("Потери — убитые + раненые за ход. Холм: A выше B (черновик 6а). Стрелки в контакте рубятся (Г28). Сгенерировано `dotnet run --project Tests -- calibrate`.");
    target.AppendLine();
    target.AppendLine("| A | B | местность | натиск | сектор | потери A: среднее ± откл. | потери B: среднее ± откл. | B в 10–90% | B сломлен |");
    target.AppendLine("|---|---|---|---|---|---|---|---|---|");

    var cmp = new System.Text.StringBuilder();
    var geo = new System.Text.StringBuilder();
    var rows = new List<object>();
    int idx = 0, okMean = 0, okSpread = 0, okSpreadTurn = 0;
    var sdRatio = new List<double>(); var sdRatioTurn = new List<double>();
    foreach (var s in setups)
    {
        idx++;
        var ta = Templates.Get(s.A); var tb = Templates.Get(s.B);
        var setup = new TurnSetup { Ground = s.G, ChargeA = s.Charge, SectorA = s.Sector };
        TurnOutcome Model(int i, MeleeOptions o)
        {
            var (pa, pb) = MeleeSim.Setup(ta.Make(1, ta.Name, 1000, 1), tb.Make(2, tb.Name, 1000, 2), setup, o, Rules.Base);
            return MeleeSim.Turn(pa, pb, s.Charge, s.G, Ctx(idx * 100000 + 50000 + i + 1), o);
        }
        var table = Enumerable.Range(0, RUNS).Select(i => Tabletop.Turn(ta.Make(1, ta.Name, 1000, 1), tb.Make(2, tb.Name, 1000, 2), setup, Ctx(idx * 100000 + i + 1))).ToList();
        var full = Enumerable.Range(0, RUNS).Select(i => Model(i, new MeleeOptions { FullContact = true })).ToList();
        var turn = Enumerable.Range(0, RUNS).Select(i => Model(i, new MeleeOptions { FullContact = true, Fortune = FortuneMode.PerTurn })).ToList();

        Stat A(List<TurnOutcome> xs) => Stat.Of(xs.Select(o => o.LossA));
        Stat B(List<TurnOutcome> xs) => Stat.Of(xs.Select(o => o.LossB));
        double Broken(List<TurnOutcome> xs) => xs.Count(o => o.MoraleB == 0) / (double)xs.Count;
        var tA = A(table); var tB = B(table); var mA = A(full); var mB = B(full); var uB = B(turn);
        double rs = tB.Sd > 0 ? mB.Sd / tB.Sd : 1, ru = tB.Sd > 0 ? uB.Sd / tB.Sd : 1;
        bool mean = Close(mA.Mean, tA.Mean) && Close(mB.Mean, tB.Mean);
        bool spread = rs >= 0.8 && rs <= 1.25, spreadTurn = ru >= 0.8 && ru <= 1.25;
        if (mean) okMean++; if (spread) okSpread++; if (spreadTurn) okSpreadTurn++;
        sdRatio.Add(rs); sdRatioTurn.Add(ru);

        target.AppendLine($"| {ta.Name} | {tb.Name} | {Ru(s.G)} | {(s.Charge ? "да" : "—")} | {SecRu(s.Sector)} | {tA.Mean:0} ± {tA.Sd:0} | {tB.Mean:0} ± {tB.Sd:0} | {tB.P10:0}–{tB.P90:0} | {Broken(table) * 100:0}% |");
        cmp.AppendLine($"| {ta.Name} | {tb.Name} | {Ru(s.G)} | {(s.Charge ? "да" : "—")} | {SecRu(s.Sector)} | {tA.Mean:0} → {mA.Mean:0} ({Pct(mA.Mean, tA.Mean)}) | {tB.Mean:0} → {mB.Mean:0} ({Pct(mB.Mean, tB.Mean)}) | {tB.Sd:0} → {mB.Sd:0} (×{rs:0.00}) | ×{ru:0.00} | {Broken(table) * 100:0}% → {Broken(full) * 100:0}% | {(mean ? "✔" : "✘")}{(spread ? "" : " разброс")} |");

        object geoRow = null;
        if (s.Sector == "front")
        {
            var g = Enumerable.Range(0, RUNS).Select(i => Model(i, new MeleeOptions())).ToList();
            var gA = A(g); var gB = B(g);
            geo.AppendLine($"| {ta.Name} | {tb.Name} | {Ru(s.G)} | {(s.Charge ? "да" : "—")} | {g[0].EngagedA * 100:0}% | {g[0].EngagedB * 100:0}% | {gA.Mean:0} ({Pct(gA.Mean, mA.Mean)}) | {gB.Mean:0} ({Pct(gB.Mean, mB.Mean)}) |");
            geoRow = new { engagedA = g[0].EngagedA, engagedB = g[0].EngagedB, lossA = gA, lossB = gB };
        }
        rows.Add(new
        {
            a = s.A, b = s.B, ground = s.G.ToString().ToLowerInvariant(), charge = s.Charge, sector = s.Sector, runs = RUNS,
            table = new { lossA = tA, lossB = tB, brokenB = Broken(table) },
            model = new { lossA = mA, lossB = mB, brokenB = Broken(full), fortunePerTurnSdB = uB.Sd },
            geometry = geoRow, withinTolerance = mean, spreadSimilar = spread,
        });
    }

    double Median(List<double> xs) { var v = xs.OrderBy(x => x).ToList(); return v[v.Count / 2]; }
    var md = new System.Text.StringBuilder();
    md.AppendLine("# Поштучная рукопашная против стола (И1, Г21, Г26–Г30)");
    md.AppendLine();
    md.AppendLine($"По {RUNS} ходов на пару, отряды по 1000 из шаблонов, фигурки 1:10. Модель — `MeleeSim` (ход 15 с, шаг 0,1 с), стол — `Tabletop.Turn` (настоящий ResolveBattle).");
    md.AppendLine("Сгенерировано `dotnet run --project Tests -- calibrate`. Допуск Г21: среднее ±10% (для малых потерь — ±5 человек), разброс «похож» — отклонение ×0,8–1,25 от стола.");
    md.AppendLine();
    md.AppendLine("## Итог");
    md.AppendLine();
    md.AppendLine($"- Средние потери обеих сторон в допуске: **{okMean} из {setups.Count}** стычек.");
    md.AppendLine($"- Разброс похож (бросок удачи на каждый удар): **{okSpread} из {setups.Count}**, медиана отношения ×{Median(sdRatio):0.00}.");
    md.AppendLine($"- Для сравнения — один бросок удачи на весь ход: похож в {okSpreadTurn} из {setups.Count}, медиана ×{Median(sdRatioTurn):0.00}.");
    md.AppendLine();
    md.AppendLine("## Полный контакт: все колонны в бою (здесь проверяется ±10%)");
    md.AppendLine();
    md.AppendLine("Формат: стол → модель (отклонение). «Разброс B» — стандартное отклонение потерь B; «на ход» — отношение разброса, если бросать удачу раз за ход.");
    md.AppendLine();
    md.AppendLine("| A | B | местность | натиск | сектор | потери A | потери B | разброс B | на ход | B сломлен | итог |");
    md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
    md.Append(cmp);
    md.AppendLine();
    md.AppendLine("## Лоб в лоб по геометрии (без заворота): намеренное расхождение Г27");
    md.AppendLine();
    md.AppendLine("Строи стоят лицом к лицу, центр к центру; бьют только колонны, что касаются врага. Свисающие ряды заворачивают на фланг вместе с движением (Г31) — здесь их ещё нет. В скобках — отличие от полного контакта.");
    md.AppendLine();
    md.AppendLine("| A | B | местность | натиск | A в бою | B в бою | потери A | потери B |");
    md.AppendLine("|---|---|---|---|---|---|---|---|");
    md.Append(geo);

    var dir = Path.Combine(root, "shared", "calibration");
    Directory.CreateDirectory(dir);
    var jo = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    File.WriteAllText(Path.Combine(dir, "tabletop.md"), target.ToString());
    File.WriteAllText(Path.Combine(dir, "melee.md"), md.ToString());
    File.WriteAllText(Path.Combine(dir, "melee.json"), JsonSerializer.Serialize(rows, jo));
    File.Delete(Path.Combine(dir, "tabletop.json"));
    Console.WriteLine($"рукопашная: {setups.Count} стычек × {RUNS} ходов; в допуске {okMean}, разброс похож {okSpread} → {dir}");
}

void CalibrateRanged(int RUNS)
{
    // Г37: опорные стычки — ±10%; остальное — что баллистика даёт сверх стола (новые правила, отчёт ГМу)
    EngineContext Ctx(int seed, Rules r) => new EngineContext { Rules = r, Rng = new Mulberry32((uint)seed).Next };
    string Pct(double model, double table) => table == 0 ? "—" : $"{(model / table - 1) * 100:+0;−0;0}%";
    bool Close(double model, double table) => Math.Abs(model - table) <= Math.Max(5, 0.10 * table);
    int idx = 0; long friendly = 0;
    var rows = new List<object>();
    (Stat tA, Stat tB, Stat mA, Stat mB, ShotStats st, double bT, double bM) Run(string label, string sa, string sb, TurnSetup s,
        Func<Placed, Placed, RangedOptions> opt = null, Rules r = null, Action<Placed, Placed> tweak = null)
    {
        idx++; r ??= Rules.Base;
        var A = Templates.Get(sa); var B = Templates.Get(sb);
        var table = Enumerable.Range(0, RUNS).Select(i => TabletopVolley.Turn(A.Make(1, A.Name, 1000, 1), B.Make(2, B.Name, 1000, 2), s, Ctx(idx * 100000 + i + 1, r))).ToList();
        var st = new ShotStats();
        var model = Enumerable.Range(0, RUNS).Select(i =>
        {
            var (pa, pb) = RangedSim.Setup(A.Make(1, A.Name, 1000, 1), B.Make(2, B.Name, 1000, 2), s, r);
            tweak?.Invoke(pa, pb);
            return RangedSim.Turn(pa, pb, Ctx(idx * 100000 + 50000 + i + 1, r), opt?.Invoke(pa, pb) ?? new RangedOptions(), st);
        }).ToList();
        friendly += st.HitsFriendly;
        var res = (Stat.Of(table.Select(x => x.LossA)), Stat.Of(table.Select(x => x.LossB)), Stat.Of(model.Select(x => x.LossA)), Stat.Of(model.Select(x => x.LossB)),
                   st, table.Count(x => x.MoraleB == 0) / (double)RUNS, model.Count(x => x.MoraleB == 0) / (double)RUNS);
        rows.Add(new
        {
            label, a = sa, b = sb, distance = s.Distance, ground = s.Ground.ToString().ToLowerInvariant(), runs = RUNS,
            table = new { lossA = res.Item1, lossB = res.Item2 }, model = new { lossA = res.Item3, lossB = res.Item4 },
            arrowsPerTurn = st.Arrows / (double)RUNS, hitRate = st.Arrows > 0 ? st.Hits / (double)st.Arrows : 0,
            friendlyHits = st.HitsFriendly, blocked = st.Blocked, killedShare = st.Out > 0 ? st.Killed / (double)st.Out : 0,
        });
        return res;
    }
    string Shots(ShotStats st) => $"{st.Arrows / (double)RUNS:0} | {(st.Arrows > 0 ? st.Hits * 100.0 / st.Arrows : 0):0}%";

    var md = new System.Text.StringBuilder();
    md.AppendLine("# Стрельба по баллистике против стола (И1, Г33, Г37–Г41)");
    md.AppendLine();
    md.AppendLine($"По {RUNS} ходов на строку, отряды по 1000 из шаблонов. Модель — `RangedSim`: каждая стрела летит сама (сопротивление воздуха, ошибка прицела), попадание — в момент встречи с телом. Стол — `TabletopVolley` (настоящий ResolveBattle, дальний бой).");
    md.AppendLine("Число стрел — по формуле стола × коэффициент лука (`Rules.Ranged.Bows[…].VolleyK`), подобранный на опорной стычке. Всё, что не опорная стычка, — то, что баллистика даёт сверх стола: это новые правила, их одобряет ГМ. Сгенерировано `dotnet run --project Tests -- calibrate-ranged`.");
    md.AppendLine();

    md.AppendLine("## Опорные стычки (Г37): поле, 100 м, пехота в строю — здесь проверяется ±10%");
    md.AppendLine();
    md.AppendLine("| стрелки | лук | стол: потери B | модель: потери B | разброс B | стрел за ход | попаданий | итог |");
    md.AppendLine("|---|---|---|---|---|---|---|---|");
    int ok = 0, total = 0;
    foreach (var sa in new[] { "archers", "militia_archers", "crossbowmen" })
    {
        var t = Templates.Get(sa);
        var r = Run("опорная", sa, "infantry", new TurnSetup());
        bool pass = Close(r.mB.Mean, r.tB.Mean); total++; if (pass) ok++;
        double rs = r.tB.Sd > 0 ? r.mB.Sd / r.tB.Sd : 1;
        md.AppendLine($"| {t.Name} | {Rules.Base.Ranged.Bows[Ballistics.BowKeyFor(t.Make(1, t.Name, 1, 1))].Name} | {r.tB.Mean:0} ± {r.tB.Sd:0} | {r.mB.Mean:0} ± {r.mB.Sd:0} ({Pct(r.mB.Mean, r.tB.Mean)}) | ×{rs:0.00} | {Shots(r.st)} | {(pass ? "✔" : "✘")} |");
    }
    md.AppendLine();
    md.AppendLine($"В допуске: **{ok} из {total}**.");
    md.AppendLine();

    md.AppendLine("## Дистанция: лучники → пехота, поле");
    md.AppendLine();
    md.AppendLine("За столом дистанция не важна; в баллистике вблизи стрелы точнее, у предела дальности — реже попадают.");
    md.AppendLine();
    md.AppendLine("| дистанция | стол: потери B | модель: потери B | к столу | стрел за ход | попаданий |");
    md.AppendLine("|---|---|---|---|---|---|");
    foreach (var d in new[] { 30.0, 60, 100, 150, 200 })
    {
        var r = Run($"дистанция {d}", "archers", "infantry", new TurnSetup { Distance = d });
        md.AppendLine($"| {d:0} м | {r.tB.Mean:0} | {r.mB.Mean:0} | {Pct(r.mB.Mean, r.tB.Mean)} | {Shots(r.st)} |");
    }
    md.AppendLine();

    md.AppendLine("## Цели: лучники → разные отряды, 100 м, поле");
    md.AppendLine();
    md.AppendLine("Броня — та же, что за столом (шанс вывести = 1 / (защита ÷ 10)); отличаются тело и глубина строя: конь — большая мишень, мелкий строй пропускает больше стрел за спину.");
    md.AppendLine();
    md.AppendLine("| цель | стол: потери B | модель: потери B | к столу | стрел за ход | попаданий |");
    md.AppendLine("|---|---|---|---|---|---|");
    foreach (var tb in new[] { "militia", "infantry", "guard", "pikemen", "knights" })
    {
        var r = Run($"цель {tb}", "archers", tb, new TurnSetup());
        md.AppendLine($"| {Templates.Get(tb).Name} | {r.tB.Mean:0} | {r.mB.Mean:0} | {Pct(r.mB.Mean, r.tB.Mean)} | {Shots(r.st)} |");
    }
    md.AppendLine();

    md.AppendLine("## Местность и строй: лучники → пехота, 100 м");
    md.AppendLine();
    md.AppendLine("| условия | стол: потери B | модель: потери B | к столу | к модели в поле | стрел за ход | попаданий |");
    md.AppendLine("|---|---|---|---|---|---|---|");
    var field = Run("поле", "archers", "infantry", new TurnSetup());
    void Row(string name, (Stat tA, Stat tB, Stat mA, Stat mB, ShotStats st, double bT, double bM) r) =>
        md.AppendLine($"| {name} | {r.tB.Mean:0} | {r.mB.Mean:0} | {Pct(r.mB.Mean, r.tB.Mean)} | {Pct(r.mB.Mean, field.mB.Mean)} | {Shots(r.st)} |");
    Row("поле (опорная)", field);
    var forest = Run("лес", "archers", "infantry", new TurnSetup { Ground = Ground.Forest },
        (pa, pb) => new RangedOptions { Forest = (x, y) => y > -(pb.Fp.Depth / 2 + 10) });
    Row("цель в лесу, 10 м от опушки (Г38: за столом ×1,4)", forest);
    md.AppendLine($"|  | стрел застряло в ветвях: {forest.st.Blocked * 100.0 / Math.Max(1, forest.st.Arrows):0}% |  |  |  |  |  |");
    var hill = Run("холм", "archers", "infantry", new TurnSetup { Ground = Ground.Hill },
        (pa, pb) => new RangedOptions { GroundZ = (x, y) => y < -(pb.Fp.Depth / 2 + 50) ? Rules.Base.Ranged.MetersPerLevel : 0 });
    Row($"стрелки на холме +{Rules.Base.Ranged.MetersPerLevel:0} м (Г41)", hill);
    var open = new Rules(); open.Map.Formation["infantry"] = new Rules.FormationR(2, 8, 2);
    Row("цель разомкнула ряды: 2 × 2 м на бойца (Г34)", Run("разомкнутый строй", "archers", "infantry", new TurnSetup(), null, open));
    Row("стрелы в спину (цель стоит к стрелкам тылом)", Run("в спину", "archers", "infantry", new TurnSetup(), null, null, (pa, pb) => pb.Facing = 180));
    md.AppendLine();

    md.AppendLine("## Перестрелка: лучники ⇄ лучники, 100 м, поле");
    md.AppendLine();
    var duel = Run("перестрелка", "archers", "archers", new TurnSetup());
    md.AppendLine("| | стол | модель | к столу |");
    md.AppendLine("|---|---|---|---|");
    md.AppendLine($"| потери A | {duel.tA.Mean:0} ± {duel.tA.Sd:0} | {duel.mA.Mean:0} ± {duel.mA.Sd:0} | {Pct(duel.mA.Mean, duel.tA.Mean)} |");
    md.AppendLine($"| потери B | {duel.tB.Mean:0} ± {duel.tB.Sd:0} | {duel.mB.Mean:0} ± {duel.mB.Sd:0} | {Pct(duel.mB.Mean, duel.tB.Mean)} |");
    md.AppendLine();

    md.AppendLine("## Куда попадают стрелы (опорная стычка лучников) и доля убитых (Г39)");
    md.AppendLine();
    var p = field.st.Parts; double hits = Math.Max(1, field.st.Hits);
    md.AppendLine($"Голова и плечи {p["head"] * 100 / hits:0}%, торс {p["torso"] * 100 / hits:0}%, ноги {p["legs"] * 100 / hits:0}%. " +
                  $"Выбыл каждый {field.st.Hits / (double)Math.Max(1, field.st.Out):0.0}-й задетый (броня пехоты 60 → 1 из 6). " +
                  $"Убитых среди выбывших: {field.st.Killed * 100.0 / Math.Max(1, field.st.Out):0}% — как у броска летальности стола в среднем (нормировка частей тела {Rules.Base.Ranged.PartNorm}).");
    md.AppendLine();
    md.AppendLine($"Стрел по своим за все прогоны: {friendly}.");

    var dir = Path.Combine(root, "shared", "calibration");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "ranged.md"), md.ToString());
    File.WriteAllText(Path.Combine(dir, "ranged.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    Console.WriteLine($"стрельба: опорных в допуске {ok} из {total} → {dir}");
}

// Замер путей (dotnet run -c Release --project Tests -- bench-paths [зазор, м]): синтетический бой на карте 4000 × 3000 м
// (река с бродами и мостом, рощи, кусты), 20 отрядов по 1500 — 30 тыс. бойцов, две стороны в линию в gap м друг от друга.
// Перед каждым ходом каждому, кто в строю и не в схватке, — «атаковать ближайшего врага» (как сцена из сохранения). 2 хода.
// dotnet run -c Release --project Tests -- bench-paths [зазор] [figs] [reserve] — figs: старые фигурки-капсулы (Rules.Figures), иначе бойцы-тела;
// reserve: за каждой линией ещё 10 отрядов в 400 м — резерв на марше вдали от врага (замер Г86)
void BenchPaths(double gap, bool figures = false, bool reserve = false)
{
    var R = figures ? Rules.Figures : Rules.Base;
    var setup = System.Diagnostics.Stopwatch.StartNew();
    var map = MapGen.Generate("river", new Dictionary<string, object>
    {
        ["widthM"] = 4000.0, ["depthM"] = 3000.0, ["width"] = "mid", ["direction"] = "along", ["fords"] = 2.0, ["bridges"] = 1.0,
    }, 11);
    var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
    var bt = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(16).Next });
    string[] tpl = { "infantry", "militia", "pikemen", "knights", "archers", "guard", "militia", "infantry", "elite_cavalry", "foot_knights" };
    // строй целиком на суше (с запасом 5 м) — иначе фигурки стоят в воде
    bool Dry(Unit u, double x, double y)
    {
        var fp = Formation.Of(u, R);
        for (double py = y - fp.Depth / 2 - 5; py <= y + fp.Depth / 2 + 5; py += 2.5)
            for (double px = x - fp.Front / 2 - 5; px <= x + fp.Front / 2 + 5; px += 2.5)
            {
                if (px < 0 || py < 0 || px >= geo.W || py >= geo.H) return false;
                int c = (int)(py / Terrain.CellM) * map.W + (int)(px / Terrain.CellM);
                if (BattleMap.MoveMult(map, c, BattleMap.IsHorse(u), R) == null) return false;
            }
        return true;
    }
    int id = 0;
    for (int side = 1; side <= 2; side++)
    for (int row = 0; row < (reserve ? 2 : 1); row++)
    {
        double line = geo.H / 2 + (side == 1 ? -gap / 2 - row * 400 : gap / 2 + row * 400), cursor = 100;
        for (int k = 0; k < 10; k++)
        {
            var T = Templates.Get(tpl[(k + 3 * side) % tpl.Length]);
            var u = T.Make(++id, $"{T.Name} {id}", 1500, side);
            double front = Formation.Of(u, R).Front, x = cursor + front / 2;
            while (x + front / 2 < geo.W - 100 && !Dry(u, x, line)) x += 10;
            if (x + front / 2 >= geo.W - 100) { line += side == 1 ? -150 : 150; cursor = 100; x = cursor + front / 2; while (!Dry(u, x, line)) x += 10; }
            bt.Add(u, x, line, side == 1 ? 180 : 0);
            cursor = x + front / 2 + 40;
        }
    }
    double men = bt.Movers.Sum(m => m.P.U.Soldiers);
    Prof.Reset();
    double rigSum = 0; int rigN = 0, rigU = 0, rigK = 0; long arrowsDone = 0;
    void PrintProf()
    {
        Console.WriteLine($"   взгляд вперёд: смотрящих {Prof.N[5]}, соседей просмотрено {Prof.N[6]} ({(Prof.N[5] > 0 ? Prof.N[6] / (double)Prof.N[5] : 0):0.0} на смотрящего); толкотня: пар в клетках {Prof.N[7]}, вызовов пары {Prof.N[8]}, с перекрытием {Prof.N[9]}");
        Console.WriteLine($"   фазы: движение {Prof.Sec(0):0.00} (бойцы: смыкание и курсы {Prof.Sec(10):0.00}, якоря {Prof.Sec(11):0.00}, желания {Prof.Sec(12):0.00}, взгляд {Prof.Sec(13):0.00}, шаг {Prof.Sec(14):0.00}, расталкивание и итог {Prof.Sec(15):0.00} = сетка {Prof.Sec(18):0.00} + пары {Prof.Sec(19):0.00} + сброс {Prof.Sec(20):0.00} + итог), касания {Prof.Sec(1):0.00}, охват и пр. {Prof.Sec(2):0.00}, удары {Prof.Sec(3):0.00}, стрельба {Prof.Sec(4):0.00} (тела и сетка {Prof.Sec(16):0.00}, пуск и полёт {Prof.Sec(17):0.00}; стрел {bt.Volleys.Sum(v => v.Arrows) + arrowsDone}, подшагов полёта {Prof.N[0]}, поисков тел {Prof.N[1]}, тел проверено {Prof.N[2]}, подборов прицела {Prof.N[3]}, тел в сетке за ход {Prof.N[4]}), раскладка {Prof.Sec(5):0.00}");
        Prof.Reset();
    }
    Console.WriteLine($"карта {geo.W:0} × {geo.H:0} м, {map.W * map.H} клеток; отрядов {bt.Movers.Count}, бойцов {men:0}; между линиями {gap:0} м; подготовка {setup.Elapsed.TotalSeconds:0.0} с");

    void OrderNearest()
    {
        bool Up(Mover m) => !m.Gone && !m.Fleeing && m.P.U.Status == "active" && m.P.U.Soldiers > 0;
        var busy = new HashSet<Mover>(bt.Fights.Where(f => !f.Over).SelectMany(f => new[] { f.A, f.B }));
        foreach (var m in bt.Movers)
        {
            if (!Up(m) || busy.Contains(m)) continue;
            Mover best = null; double bd = double.MaxValue;
            foreach (var e in bt.Movers)
            {
                if (!Up(e) || e.P.U.FactionId == m.P.U.FactionId) continue;
                double d = (e.P.X - m.P.X) * (e.P.X - m.P.X) + (e.P.Y - m.P.Y) * (e.P.Y - m.P.Y);
                if (d < bd) { bd = d; best = e; }
            }
            if (best != null && (m.Order == null || m.Order.Kind != OrderKind.Attack || m.Order.TargetId != best.P.U.Id))
                bt.Order(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = best.P.U.Id });
        }
    }

    FlowField.StatBuilds = FlowField.StatBuildTicks = FlowField.StatSearchTicks = FlowField.StatSettled = FlowField.StatShared = FlowField.StatGrounds = 0;
    int gc2 = GC.CollectionCount(2);
    var total = System.Diagnostics.Stopwatch.StartNew();
    for (int turn = 1; turn <= 2; turn++)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OrderNearest();
        bt.Turn(_ => { if (++rigK % 30 == 0) { rigSum += MenBodies.RigidMen; rigU += MenBodies.RigidUnits; rigN++; } });
        Console.WriteLine($"ход {turn}: {sw.Elapsed.TotalSeconds:0.00} с; схваток {bt.Fights.Count(f => !f.Over)}, бежит {bt.Movers.Count(m => m.Fleeing)}, бойцов {bt.Movers.Sum(m => m.P.U.Soldiers):0}; жёстких бойцов в среднем {(rigN > 0 ? rigSum / rigN : 0):0} из {bt.Movers.Sum(m => m.Men.Count(x => x.Alive))}, отрядов вдали {(rigN > 0 ? rigU / (double)rigN : 0):0.0} из {bt.Movers.Count}");
        rigSum = 0; rigN = 0; rigU = 0;
        PrintProf();
    }
    double tf = System.Diagnostics.Stopwatch.Frequency;
    Console.WriteLine($"всего счёта: {total.Elapsed.TotalSeconds:0.00} с");
    long nb = Math.Max(1, FlowField.StatBuilds);
    Console.WriteLine($"FlowField.Build: {FlowField.StatBuilds} вызовов, {FlowField.StatBuildTicks / tf:0.00} с, {FlowField.StatBuildTicks / tf * 1000 / nb:0.0} мс на вызов");
    // поиск цен ленивый — идёт и после Build, когда спрашивают цену клетки: его время — тоже в счёт путей
    Console.WriteLine($"поиск цен после Build: {FlowField.StatSearchTicks / tf:0.00} с; клеток осело {FlowField.StatSettled} — в среднем {FlowField.StatSettled / nb} на карту из {map.W * map.H}");
    Console.WriteLine($"пути всего (Build + поиск): {(FlowField.StatBuildTicks + FlowField.StatSearchTicks) / tf:0.00} с; поиск взят готовым {FlowField.StatShared} раз; местность считалась {FlowField.StatGrounds} раз; сборок мусора gen2 {GC.CollectionCount(2) - gc2}");
}

// ── прогон ──
int failed = 0;
var slow = new List<(string name, double sec)>();
var clock = System.Diagnostics.Stopwatch.StartNew();
foreach (var (name, run) in tests)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { run(); }
    catch (Exception e)
    {
        failed++;
        if (failed <= 20) Console.WriteLine($"✘ {name}\n    {e.Message}");
    }
    if (sw.Elapsed.TotalSeconds > 3) slow.Add((name, sw.Elapsed.TotalSeconds));
}
// долгие тесты — чтобы прогон не разрастался незаметно
foreach (var (name, sec) in slow.OrderByDescending(s => s.sec)) Console.WriteLine($"⏱ {sec:0.0} с — {name}");
Console.WriteLine($"\nтестов {tests.Count} · прошло {tests.Count - failed} · упало {failed} · {clock.Elapsed.TotalSeconds:0} с");
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
