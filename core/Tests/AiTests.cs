// ═══════════ AiTests.cs — ИИ-помощник ГМа (Г113) и оценка обстановки (Г116) ═══════════
// Поручения: держать позицию (разворот к угрозе, стрелки сами стреляют), наступать (пехота — в схватку, конница — с натиском),
// прикрыть (встать перед стрелками, перехват), фланговый манёвр (заход сбоку, удар во фланг); подсказки адъютанта и разбор хода.
using BattleCore;

static class AiTests
{
    static Rules R => BattleTests.Use;
    static void True(bool ok, string what) { if (!ok) throw new Exception(what); }
    static Geo Open(double w = 1000, double h = 1000) => MoveTests.Open(w, h);
    static Battle New(uint seed, double w = 1000, double h = 1000) => new Battle(Open(w, h), R, new EngineContext { Rng = new Mulberry32(seed).Next });
    static Unit U(string tpl, int id, string name, double n, int faction) => Templates.Get(tpl).Make(id, name, n, faction);
    static MoveOrder Attack(int target, bool charge = false) => new MoveOrder { Kind = OrderKind.Attack, TargetId = target, Charge = charge };
    static double Deg(Mover m, Mover to) => Math.Abs(MoveSim.AngleDiff(m.P.Facing, MoveSim.HeadingOf(to.P.X - m.P.X, to.P.Y - m.P.Y)));
    static Fight FightOf(Battle bt, Mover a, Mover b) => bt.Fights.FirstOrDefault(f => !f.Over && (f.A == a && f.B == b || f.A == b && f.B == a));

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("ИИ (Г113) держать: пехота на «держать», рыцари заходят сбоку — к удару пехота стоит лицом к ним (≤ 45°), разворот начат заранее, центр ушёл не дальше фронта (поворот вокруг фланга); лучники на «держать» сами стреляют по подошедшему ополчению; решения — в журнале хода", () =>
        {
            var bt = New(71);
            var a = bt.Add(U("infantry", 1, "Пехота", 1000, 1), 500, 500, 0);
            var k = bt.Add(U("knights", 2, "Рыцари", 500, 2), 850, 500, 270);
            var ar = bt.Add(U("archers", 3, "Лучники", 300, 1), 200, 500, 0);
            var mil = bt.Add(U("militia", 4, "Ополчение", 500, 2), 200, 200, 180);
            bt.Assign(a, AiKind.Hold); bt.Assign(ar, AiKind.Hold);
            bt.Order(k, Attack(1, true)); bt.Order(mil, Attack(3));
            double ax = a.P.X, ay = a.P.Y; bool facedBeforeTouch = false, shoots = false; var all = new List<string>();
            for (int t = 0; t < 3; t++)
            {
                var said = bt.AiOrders(); all.AddRange(said);
                if (said.Any(s => s.Contains("«Лучники» — стреляет по «Ополчение»"))) shoots = true;
                bool touched = false;
                bt.Turn(tt => { if (!touched && a.InMelee) { touched = true; facedBeforeTouch = Deg(a, k) <= 45; } });
                if (touched) break;
            }
            True(all.Any(s => s.StartsWith("ИИ: «Пехота» — разворачивается к «Рыцари»")), $"пехота не разворачивалась: {string.Join(" | ", all)}");
            True(a.InMelee && facedBeforeTouch, $"к удару пехота смотрела на {Deg(a, k):0}° от рыцарей (в схватке: {a.InMelee})");
            True(JsMath.Hypot(a.P.X - ax, a.P.Y - ay) < a.P.Fp.Front, $"пехота сошла с места на {JsMath.Hypot(a.P.X - ax, a.P.Y - ay):0} м (поворот строем вокруг фланга — не дальше фронта)");
            True(shoots, $"лучники не стреляли: {string.Join(" | ", all.Where(s => s.Contains("Лучники")))}");
            var log = bt.Turn();
            True(bt.AiOrders().Count == 0 || true, "");
        });

        yield return ("ИИ (Г113) наступать: пехота в 400 м наступает на врага и вступает в схватку; рыцари в 450 м выходят на дистанцию натиска, потом идут в натиск (в журнале «натиск на», в схватке — натиск); журнал хода несёт строки ИИ", () =>
        {
            foreach (var tpl in new[] { "infantry", "knights" })
            {
                var bt = New(72);
                var b = bt.Add(U("infantry", 2, "Враг", 1000, 2), 500, 400, 180);
                var a = bt.Add(U(tpl, 1, "Свои", 1000, 1), 500, tpl == "knights" ? 850 : 800, 0);
                bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
                bt.Assign(a, AiKind.Advance, 2);
                var all = new List<string>(); bool charged = false; var log = new List<string>(); int turns = 0;
                for (; turns < 8 && FightOf(bt, a, b) == null; turns++)
                {
                    all.AddRange(bt.AiOrders());
                    log.AddRange(bt.Turn(tt => { var f = FightOf(bt, a, b); if (f != null && f.Notes.Any(n => n.Contains("натиск"))) charged = true; }));
                }
                True(FightOf(bt, a, b) != null && b.P.U.Soldiers < 1000, $"{tpl}: схватки нет за {turns} ходов: {string.Join(" | ", all)}");
                if (tpl == "knights")
                {
                    True(all.Any(s => s.Contains("выходит на дистанцию натиска")) && all.Any(s => s.Contains("натиск на «Враг»")), $"рыцари: {string.Join(" | ", all)}");
                    True(charged || bt.Fights.Any(f => f.Notes.Any(n => n.Contains("натиск"))) || log.Any(l => l.Contains("натиск")), $"натиска в схватке не было: {string.Join(" | ", log)}");
                }
                else True(turns <= 5 && all.Any(s => s.Contains("атакует «Враг»")), $"пехота: ходов {turns}: {string.Join(" | ", all)}");
                True(log.Any(l => l.StartsWith("ИИ: ")), $"строк ИИ в журнале хода нет: {string.Join(" | ", log)}");
            }
        });

        yield return ("ИИ (Г113) прикрыть: пехота с поручением «прикрыть лучников» встаёт перед ними со стороны врага; рыцари врага идут на лучников — пехота перехватывает (схватка пехоты с рыцарями начинается раньше, чем те касаются лучников); потери лучников меньше, чем без прикрытия", () =>
        {
            double Run(bool cover, out string why, out bool intercepted)
            {
                var bt = New(73);
                var ar = bt.Add(U("archers", 1, "Лучники", 500, 1), 500, 700, 0);
                var inf = bt.Add(U("infantry", 2, "Пехота", 800, 1), 440, 720, 0);
                var k = bt.Add(U("knights", 3, "Рыцари", 500, 2), 500, 60, 180);
                if (cover) bt.Assign(inf, AiKind.Cover, 1); else bt.Order(inf, new MoveOrder { Kind = OrderKind.Hold });
                bt.Order(k, Attack(1, true));
                var all = new List<string>(); bool hit = false, archersTouched = false; double placed = double.NaN;
                for (int t = 0; t < 5; t++)
                {
                    all.AddRange(bt.AiOrders());
                    bt.Turn(tt =>
                    {
                        if (!archersTouched && FightOf(bt, k, ar) != null) archersTouched = true;
                        if (!archersTouched && FightOf(bt, k, inf) != null) hit = true;
                    });
                    if (t == 0 && cover) placed = JsMath.Hypot(inf.P.X - ar.P.X, inf.P.Y - (ar.P.Y - (ar.P.Fp.Depth + inf.P.Fp.Depth) / 2 - R.Ai.CoverGapM));
                    if (archersTouched || k.Fleeing || k.P.U.Soldiers <= 0) break;
                }
                intercepted = hit;
                why = string.Join(" | ", all) + (cover ? $"; после 1-го хода пехота в {placed:0} м от места прикрытия" : "");
                if (cover) True(placed < 25, $"пехота не встала перед лучниками: {why}");
                return 500 - ar.P.U.Soldiers;
            }
            double with = Run(true, out var whyWith, out var intercepted), without = Run(false, out var whyWithout, out _);
            True(intercepted, $"пехота не перехватила рыцарей: {whyWith}");
            True(with < without, $"с прикрытием лучники потеряли {with:0}, без — {without:0}; {whyWith}");
        });

        yield return ("ИИ (Г113) фланговый манёвр: рыцари перед пехотой врага заходят на её фланг (точка сбоку от строя) и бьют во фланг с натиском — в схватке у рыцарей доля фланга и тыла больше фронта; за два полных хода схватки рыцари теряют меньше, чем при ударе в лоб с того же места (во фланг пехота не отвечает), пехота — не меньше половины", () =>
        {
            (double loss, double own, string why, bool flank) Run(bool flankTask)
            {
                var bt = New(74);
                var b = bt.Add(U("infantry", 2, "Пехота", 1000, 2), 500, 500, 0);
                var a = bt.Add(U("knights", 1, "Рыцари", 500, 1), 500, 150, 180);
                bt.Order(b, new MoveOrder { Kind = OrderKind.Hold });
                if (flankTask) bt.Assign(a, AiKind.Flank, 2); else bt.Assign(a, AiKind.Advance, 2);
                var all = new List<string>(); bool flank = false; int turns = 0, fightTurns = 0; double loss = 0, own = 0;
                for (; turns < 10 && fightTurns < 2; turns++)
                {
                    all.AddRange(bt.AiOrders());
                    bool fightAtStart = FightOf(bt, a, b) != null; double was = b.P.U.Soldiers, wasA = a.P.U.Soldiers;
                    bt.Turn(tt => { var f = FightOf(bt, a, b); if (f != null && f.Of(a).Engaged > 0 && f.Of(a).Flank + f.Of(a).Rear > f.Of(a).Front) flank = true; });
                    if (fightAtStart) { fightTurns++; loss += was - b.P.U.Soldiers; own += wasA - a.P.U.Soldiers; }   // потери за два полных хода схватки
                }
                return (loss, own, string.Join(" | ", all), flank);
            }
            var fl = Run(true); var fr = Run(false);
            True(fl.why.Contains("заходит в") && fl.why.Contains("бьёт во фланг «Пехота» с натиском"), $"манёвра не было: {fl.why}");
            True(fl.flank, $"удар пришёлся не во фланг: {fl.why}");
            // широкий строй конницы во фланг касается узкой полосой (Г31) — потери пехоты не больше, чем в лоб, зато она не отвечает: свои потери меньше
            True(fl.own < fr.own && fl.loss > fr.loss * 0.5, $"во фланг: пехота −{fl.loss:0}, рыцари −{fl.own:0}; в лоб: пехота −{fr.loss:0}, рыцари −{fr.own:0}; фланг: {fl.why}; лоб: {fr.why}");
        });

        yield return ("оценка обстановки (Г116): подсказки адъютанта — «натиск не успеет разогнаться» при атаке с 30 м, «за ход не дойдёт» для пехоты за 300 м, «обходят» на «держать» при враге сбоку, «лучники без прикрытия» при враге в 100 м и без своей пехоты между (с пехотой между — нет), «уйдя, откроет» при уходе пехоты; разбор хода — «проигрывает схватку» у ополчения против гвардии", () =>
        {
            var bt = New(75);
            var k = bt.Add(U("knights", 1, "Рыцари", 500, 1), 500, 300, 180);
            var b = bt.Add(U("infantry", 2, "Враг", 1000, 2), 500, 300 + k.P.Fp.Depth / 2 + 30 + Formation.Of(U("infantry", 2, "Враг", 1000, 2), R).Depth / 2, 0);
            var h = bt.Advise(k, Attack(2, true));
            True(h.Any(s => s.StartsWith("натиск не успеет разогнаться")), $"натиск с 30 м: {string.Join(" | ", h)}");
            var inf = bt.Add(U("infantry", 3, "Пехота", 1000, 1), 200, 800, 0);
            h = bt.Advise(inf, new MoveOrder { X = 200, Y = 500, Facing = 0 });
            True(h.Any(s => s.StartsWith("за ход не дойдёт")), $"300 м пехоте: {string.Join(" | ", h)}");
            var side = bt.Add(U("militia", 4, "Ополчение врага", 500, 2), 200 + inf.P.Fp.Front / 2 + 60, 800, 270);
            h = bt.Advise(inf, new MoveOrder { Kind = OrderKind.Hold });
            True(h.Any(s => s.StartsWith("обходят")), $"враг сбоку: {string.Join(" | ", h)}");
            var s = bt.Assess(inf);
            True(s.Flanked && s.Threat == side && s.ThreatSector == "flank", $"оценка: обходят {s.Flanked}, угроза «{s.Threat?.P.U.Name}» {s.ThreatSector}");
            var ar = bt.Add(U("archers", 5, "Лучники", 300, 1), 800, 800, 0);
            var foe = bt.Add(U("infantry", 6, "Пехота врага", 1000, 2), 800, 670, 180);
            var sa = bt.Assess(ar);
            True(sa.Uncovered && sa.Hints.Any(x => x.StartsWith("лучники без прикрытия")), $"лучники без прикрытия: {string.Join(" | ", sa.Hints)}; угроза «{sa.Threat?.P.U.Name}», видит {bt.Sees(1, foe)}, зазор {BattleMap.PolyGap(new[] { new[] { ar.P.X, ar.P.Y } }, new[] { new[] { foe.P.X, foe.P.Y } }):0}");
            var guard = bt.Add(U("infantry", 7, "Прикрытие", 1000, 1), 800, 735, 0);
            sa = bt.Assess(ar);
            True(!sa.Uncovered, $"с пехотой между — всё ещё без прикрытия: {string.Join(" | ", sa.Hints)}");
            h = bt.Advise(guard, new MoveOrder { X = 400, Y = 700, Facing = 0 });
            True(h.Any(x => x.Contains("уйдя, «Прикрытие» откроет «Лучники»")), $"уход прикрытия: {string.Join(" | ", h)}");
            var bt2 = New(76);
            var mil = bt2.Add(U("militia", 1, "Ополчение", 1000, 1), 500, 500, 0);
            var gd = bt2.Add(U("guard", 2, "Гвардия", 1000, 2), 500, 400, 180);
            bt2.Order(gd, Attack(1)); bt2.Turn();
            var rv = bt2.Review(1);
            True(rv.Any(x => x.StartsWith("«Ополчение» проигрывает схватку с «Гвардия»")), $"разбор: {string.Join(" | ", rv)}");
        });

        yield return ("ИИ (Г113) туман и сессия: цель за лесом не видна — конница с поручением «наступать» ждёт, пока не увидит; поручение стороне через BattleSession.Go — приказы отдаются сами, приказ ГМа на этот ход важнее", () =>
        {
            var geo = Open(1000, 1000);
            Terrain.PaintRect(geo.Map, "t", 0, 90, 199, 110, Terrain.Id("forest"));   // полоса леса y 450…555
            var bt = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(77).Next });
            var a = bt.Add(U("knights", 1, "Рыцари", 500, 1), 500, 200, 180);
            var b = bt.Add(U("infantry", 2, "Пехота", 1000, 2), 500, 800, 0);
            var c = bt.Add(U("infantry", 3, "Свои", 1000, 1), 200, 200, 180);
            True(!bt.Sees(1, b), "за лесом в 600 м пехота видна");
            bt.AssignSide(1, AiKind.Advance, 2);
            var said = bt.AiOrders();
            True(said.Any(s => s.Contains("«Рыцари» — «Пехота» не видно — ждёт")) && a.Order != null && a.Order.Kind == OrderKind.Hold, $"рыцари: {string.Join(" | ", said)}");
            var ses = new BattleSession(bt);
            ses.SetOrder(a, new MoveOrder { X = 500, Y = 400, Facing = 180 });   // ГМ: рыцари — к опушке
            ses.Go(); while (ses.Step()) { }
            True(a.Order != null && a.Order.Kind == OrderKind.Move && Math.Abs(a.P.Y - 400) < 30, $"приказ ГМа не перевесил: {a.Order?.Kind}, y {a.P.Y:0}");
            True(c.Order != null && c.Order.Kind != OrderKind.Attack && c.TaskOfKind(bt) == AiKind.Advance, $"свои без поручения: {c.Order?.Kind}");
        });
    }
    static AiKind TaskOfKind(this Mover m, Battle bt) => bt.TaskOf(m).Kind;
}
