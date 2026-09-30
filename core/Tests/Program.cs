// ═══════════ Тесты движка на C# ═══════════
// Главный тест — эталон v29: 400 сценариев из shared/golden отыгрываются так же, как в трекере,
// строка в строку. Запуск: dotnet run --project Tests (из core/). Код возврата 0 — всё зелёное.
// dotnet run --project Tests -- calibrate — мишень стола и сверка поштучной модели: shared/calibration/*
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
Test("раскладка на фигурки (Г32): 1000 пехоты по 10 — квадратики 5 × 2, 4 ряда по 25", () =>
{
    var u = new Unit { Id = 7, Type = "infantry", Soldiers = 1000 };
    Eq(Formation.Shape(u, 10, Rules.Base), (5, 2), "форма пехоты");
    var figs = Formation.Layout(u, 10, Rules.Base);
    Eq(figs.Count, 100, "фигурок");
    Eq(figs.Sum(f => f.Men), 1000.0, "бойцов всего");
    Eq(figs.Max(f => f.Rank), 3, "последний ряд квадратиков");
    var front = figs.Where(f => f.Rank == 0).ToList();
    Eq(front.Count, 25, "квадратиков в переднем ряду");
    Eq(front.Sum(f => f.Width), 125.0, "ширина переднего ряда = фронт строя");
    Eq((front[0].Width, front[0].Depth, front[0].Men), (5.0, 2.0, 10.0), "квадратик 5 × 2 м");
    Eq(front.Min(f => f.X - f.Width / 2), -62.5, "левый край строя");
    Eq(front[0].Y, -3.0, "передний ряд — у переднего края (шеренги 1–2)");
    var half = Formation.Layout(new Unit { Type = "infantry", Soldiers = 500 }, 10, Rules.Base);
    Eq(half.Sum(f => f.Men), 500.0, "500 бойцов");
    Eq(half.Where(f => f.Rank == 0).Sum(f => f.Width), 63.0, "потери сужают фронт — как фишка трекера");
    Eq(Formation.Layout(u, 1, Rules.Base).Count, 1000, "1:1 — по фигурке на бойца");
    Eq(Formation.Shape(new Unit { Type = "pike", Soldiers = 1000 }, 10, Rules.Base), (5, 2), "пикинёры: 10 шеренг — тоже 5 × 2");
    Eq(Formation.Shape(new Unit { Type = "archer", Soldiers = 1000 }, 10, Rules.Base), (2, 5), "стрелки: 5 шеренг — колонки 2 × 5");
    Eq(Formation.Shape(new Unit { Type = "cavalry", Soldiers = 1000 }, 10, Rules.Base), (10, 1), "конница: 5 шеренг — 10 всадников в ряд");
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

if (args.Length > 0 && args[0] == "calibrate") { Calibrate(args.Length > 1 ? int.Parse(args[1]) : 1000); return 0; }

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
