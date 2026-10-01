// ═══════════ muster.test.mjs — импорт армий из текста и шаблоны отрядов ═══════════
// Образцы в shared/battles — настоящие отчёты о битвах из «Кодекса Альтера», без правок.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import * as E from "../src/engine/index.js";

const fixture = name => fs.readFileSync(new URL(`../../shared/battles/${name}.txt`, import.meta.url), "utf8");
const lines = side => side.contingents.flatMap(c => c.lines);

// ── числа ──
test("числа: точки, пробелы, тысячи, примерные", () => {
  const n = s => E.parseCount(s)?.n;
  assert.equal(n("18.000 всадников"), 18000);
  assert.equal(n("1.000 пешие рыцари"), 1000);
  assert.equal(n("19500 всадников"), 19500);
  assert.equal(n("18 000 всадников"), 18000);
  assert.equal(n("2.5к солдат"), 2500);
  assert.equal(n("3 тыс. рыцарей"), 3000);
  assert.equal(n("10 тысяч копейщиков"), 10000);
  assert.equal(n("300 копейщиков"), 300, "«к» в начале слова — не тысячи");
  assert.equal(E.parseCount("~2000").approx, true);
  assert.equal(E.parseCount("Рыцари"), null);
});

// ── падежи ──
test("название после числа — в именительный падеж", () => {
  const cases = {
    "всадников Красных Кольчуг": "Всадники Красных Кольчуг",
    "солдат ополчения Сильварейнов": "Солдаты ополчения Сильварейнов",
    "лучников ополчения": "Лучники ополчения",
    "пешие рыцари Сильварейнов": "Пешие рыцари Сильварейнов",
    "рыцари \"Багрового лотоса\"": "Рыцари \"Багрового лотоса\"",
    "дуртангское ополчение": "Дуртангское ополчение",
    "пеших солдат Арнора": "Пешие солдаты Арнора",
    "рыцарей королевской стражи": "Рыцари королевской стражи",
    "наёмных арбалетчиков": "Наёмные арбалетчики",
    "ополчения Пикшарпа": "Ополчение Пикшарпа",
    "крестьян": "Крестьяне",
    "пехотинцев ополчения": "Пехотинцы ополчения",
    "горцев Дуртанга": "Горцы Дуртанга",
    "копейщиков ополчения Дуртанга": "Копейщики ополчения Дуртанга",
    "лучники-ополченцы Дуртанга": "Лучники-ополченцы Дуртанга",
    "пеших": "Пешие",
    "конных": "Конные",
    "вампиров": "Вампиры",
    "самураев": "Самураи",
  };
  for(const [src, want] of Object.entries(cases)) assert.equal(E.toNominative(src), want, src);
});

// ── тип войск ──
test("тип войск: «пешие» отменяют кавалерию, но только с начала слова", () => {
  assert.equal(E.guessUnitType("Пешие рыцари Сильварейнов").type, "infantry");
  assert.equal(E.guessUnitType("Рыцари Цепешей").type, "cavalry", "«Цепешей» — не «пешие»");
  assert.equal(E.guessUnitType("Конные").type, "cavalry");
  assert.equal(E.guessUnitType("Крестьяне").type, "infantry");
  assert.equal(E.guessUnitType("Горцы Дуртанга").type, "infantry");
  // старое поведение не сломано
  assert.equal(E.guessUnitType("Всадники Бладколла").type, "cavalry");
  assert.equal(E.guessUnitType("Пикинёры Эштауна").type, "pike");
  assert.equal(E.guessUnitType("Лучники Эштауна").type, "archer");
  assert.equal(E.guessUnitType("Роххирим").weapon, "ranged");
});

// ── шаблоны ──
test("шаблоны: подбор по названию", () => {
  const id = n => E.matchTemplate(n).id;
  assert.equal(id("Всадники Красных Кольчуг"), "knights");
  assert.equal(id("Элитные всадники Бладколла"), "elite_cavalry");
  assert.equal(id("Рыцари серого Ордена"), "elite_cavalry");
  assert.equal(id("Пешие рыцари Сильварейнов"), "foot_knights");
  assert.equal(id("Лучники ополчения"), "militia_archers");
  assert.equal(id("Лучники Сокола"), "archers");
  assert.equal(id("Арбалетчики Дуртанга"), "crossbowmen");
  assert.equal(id("Копейщики ополчения"), "pikemen");
  assert.equal(id("Дуртангское ополчение"), "militia");
  assert.equal(E.matchTemplate("Красные Кольчуги").fallback, true, "название без подсказки — ополчение с пометкой");
});

test("шаблоны: база → общие правки → правки фракции", () => {
  const ov = E.normalizeOverrides({
    base: {knights: {discipline: 50}},
    factions: {"Красные Кольчуги": {knights: {discipline: 70, size: 500}}, "Пусто": {}},
  });
  assert.equal(E.resolveTemplate("knights", "Тосава", ov).discipline, 50);
  assert.equal(E.resolveTemplate("knights", "красные кольчуги", ov).discipline, 70, "фракция без учёта регистра");
  assert.equal(E.resolveTemplate("knights", "Красные Кольчуги", ov).size, 500);
  assert.equal(E.resolveTemplate("knights", "Красные Кольчуги", ov).eqAtk, 80, "непереопределённое — из базы");
  assert.equal(E.resolveTemplate("militia", "Красные Кольчуги", ov).discipline, 40);
  assert.deepEqual(Object.keys(ov.factions), ["Красные Кольчуги"], "пустые правки не хранятся");
  assert.equal(E.normalizeOverrides({base: {knights: {discipline: 500}}}).base.knights.discipline, 100, "пределы как у формы");
  assert.deepEqual(E.normalizeOverrides(undefined), {base: {}, factions: {}}, "старое сохранение без шаблонов");
});

// ── нарезка ──
test("нарезка на отряды", () => {
  assert.deepEqual(E.splitSoldiers(18000, 1000), new Array(18).fill(1000));
  assert.deepEqual(E.splitSoldiers(19500, 1000), [...new Array(19).fill(1000), 500]);
  assert.deepEqual(E.splitSoldiers(1050, 1000), [1050], "мелкий остаток вливается");
  assert.deepEqual(E.splitSoldiers(2200, 1000), [1000, 1200]);
  assert.deepEqual(E.splitSoldiers(300, 1000), [300]);
  assert.deepEqual(E.splitSoldiers(0, 1000), []);
  const big = E.lineSize({count: 200000, size: 1}, {size: 1000});
  assert.equal(E.splitSoldiers(200000, big).length, E.MUSTER.maxUnitsPerLine, "не больше лимита отрядов на строку");
});

test("нумерация продолжает существующие отряды", () => {
  const taken = new Set(["Рыцари Дуртанга", "Рыцари Дуртанга №2"]);
  assert.deepEqual(E.numberedNames("Рыцари Дуртанга", 2, taken), ["Рыцари Дуртанга №3", "Рыцари Дуртанга №4"]);
  assert.deepEqual(E.numberedNames("Горцы", 2, new Set()), ["Горцы", "Горцы №2"]);
});

// ── реальные тексты ──
test("Штурм Тринидара: Генрих против Персиваля", () => {
  const p = E.parseArmyText(fixture("trinidar"));
  assert.equal(p.title, "Штурм Тринидара");
  assert.equal(p.sides.length, 2);
  const [henry, perc] = p.sides;
  assert.equal(henry.contingents[0].faction, "Красные Кольчуги");
  assert.deepEqual(henry.contingents[0].commanders, ['"Бог войны" Король Генрих Файрлайн']);
  assert.deepEqual(lines(henry).map(l => l.count), [18000, 6000, 3000, 1000]);
  assert.deepEqual(lines(henry).map(l => l.name),
    ["Всадники Красных Кольчуг", "Солдаты ополчения Сильварейнов", "Лучники ополчения", "Пешие рыцари Сильварейнов"]);
  assert.equal(henry.total.n, 28000);
  assert.equal(henry.sum, 28000);
  assert.deepEqual(perc.contingents[0].commanders, ['Магистр ордена "Багрового лотоса", сир Персиваль']);
  assert.equal(lines(perc).length, 8);
  // В тексте итог 21.000, а строки дают 22.000 — это надо показать мастеру
  assert.equal(perc.sum, 22000);
  assert.ok(p.warnings.some(w => w.text.includes("22 000") && w.text.includes("21 000")));
  // Разбор остановился на «Ход боя» — потери из «Убитые и тяжелораненые» не попали
  assert.ok(!lines(perc).some(l => l.count === 2200));

  const plan = E.planMuster(p);
  assert.equal(plan.sides[1].faction, 'Магистр ордена "Багрового лотоса", сир Персиваль', "без строки фракции — по полководцу");
  const r = E.expandMuster(plan);
  assert.equal(r.unitsTotal, 50);
  assert.equal(r.soldiersTotal, 50000);
  const [F1, F2] = r.factions;
  assert.equal(F1.units.length, 28);
  assert.equal(F1.units[0].name, "Всадники Красных Кольчуг");
  assert.equal(F1.units[17].name, "Всадники Красных Кольчуг №18");
  assert.equal(F1.units[0].type, "cavalry");
  assert.equal(F1.units[0].commander, '"Бог войны" Король Генрих Файрлайн');
  assert.equal(F1.subfactions.length, 0, "один контингент — без подфракций");
  // Нумерация своя в каждой фракции: у Персиваля ополчение Сильварейнов начинается с первого
  assert.equal(F2.units[0].name, "Солдаты ополчения Сильварейнов");
  const foot = F2.units.find(u => u.name === "Пешие рыцари Сильварейнов");
  assert.equal(foot.type, "infantry");
  assert.equal(foot.templateId, "foot_knights");
});

test("Битва при Бране: союзники через «+» — подфракции", () => {
  const p = E.parseArmyText(fixture("bran"));
  assert.equal(p.title, "Битва при Бране");
  assert.equal(p.warnings.length, 0, JSON.stringify(p.warnings));
  const [a, b] = p.sides;
  assert.equal(a.contingents.length, 2);
  assert.deepEqual(a.contingents[1].commanders, ['"Кровавый Лорд" Матиас Цепеш']);
  assert.equal(a.total.n, 25500);
  assert.equal(b.contingents.length, 2);
  assert.equal(b.contingents[1].faction, "Бладколл Зиркнифа");
  assert.equal(b.total.n, 32000, "итог в одной строке с «=»");
  assert.equal(b.sum, 32000);
  const r = E.expandMuster(E.planMuster(p));
  assert.deepEqual(r.factions[0].subfactions, ["Красные Кольчуги", '"Кровавый Лорд" Матиас Цепеш']);
  assert.deepEqual(r.factions[1].subfactions, ["Семья Карнштайн", "Бладколл Зиркнифа"]);
  assert.equal(r.factions[0].units.filter(u => u.name.startsWith("Всадники")).length, 20, "19500 → 19 по 1000 + 500");
  assert.equal(r.factions[0].units.find(u => u.name === "Рыцари Цепешей").type, "cavalry");
});

test("Вторая битва при Пикшарпе: без фракций, «+ 4000 солдат Вульфхартов»", () => {
  const p = E.parseArmyText(fixture("pikshsharp2"));
  assert.equal(p.title, "Вторая Битва при Пикшарпе (25.06)");
  const [a, b] = p.sides;
  assert.deepEqual(a.contingents[0].commanders, ["Нахт Файрлайн"]);
  assert.deepEqual(lines(a).map(l => [l.count, l.name]), [[4000, "Пешие солдаты Арнора"], [1000, "Рыцари королевской стражи"]]);
  assert.deepEqual(lines(b).map(l => [l.count, l.name]), [[20000, "Пешие"], [4000, "Конные"], [4000, "Солдаты Вульфхартов"]]);
  assert.equal(b.contingents.length, 2, "«+» открывает союзный контингент");
  const plan = E.planMuster(p);
  assert.equal(plan.sides[0].faction, "Нахт Файрлайн");
  assert.equal(plan.sides[1].contingents[1].name, "Солдаты Вульфхартов");
  assert.equal(lines(plan.sides[1])[1].type, "cavalry");
});

test("Битва у Эльсвея: голое число и сторона без войск", () => {
  const p = E.parseArmyText(fixture("elsvey"));
  const plan = E.planMuster(p);
  const l = plan.sides[0].contingents[0].lines[0];
  assert.equal(l.count, 20000);
  assert.equal(l.name, "Красные Кольчуги", "голое число — отряд по имени фракции");
  assert.equal(l.fallback, true, "шаблон не угадан — предпросмотр это покажет");
  assert.ok(p.warnings.some(w => w.text.includes("сторона 2: войска не перечислены")));
});

test("Красные Кольчуги: полководец после отрядов командует следующими", () => {
  const p = E.parseArmyText(fixture("kolchugi"));
  assert.equal(p.title, null, "без «Силы сторон» весь текст — войска");
  const c = p.sides[0].contingents[0];
  assert.equal(c.faction, "Красные Кольчуги");
  assert.deepEqual(c.lines.map(l => [l.count, l.commander]), [
    [19500, '"Бог войны" Король Генрих Файрлайн'],
    [500, "капитан Вальтер из Лайоши, особые навыки отсутствуют"],
  ]);
  assert.ok(!c.commanders.some(x => x.includes("засаду")), "рассказ не становится полководцем");
  assert.equal(p.warnings.filter(w => w.text.includes("рассказ")).length, 3);
});

test("Битва при Пикшарпе (2 этап): числа до тысячи, крестьяне", () => {
  const p = E.parseArmyText(fixture("pikshsharp1"));
  const [a, b] = p.sides;
  assert.deepEqual(a.contingents[0].commanders, ["Герцог Пикшарпа, Уильям Нерин"], "запятая внутри — не фракция");
  assert.deepEqual(lines(a).map(l => l.name), ["Рыцари Пикшарпа", "Наёмные арбалетчики", "Ополчение Пикшарпа", "Крестьяне"]);
  assert.deepEqual(lines(b).map(l => l.count), [3000]);
  const r = E.expandMuster(E.planMuster(p));
  assert.deepEqual(r.factions[0].units.slice(0, 2).map(u => u.soldiers), [300, 500]);
});

test("заголовки без двоеточия ловятся только целиком", () => {
  const p = E.parseArmyText("Сторона А,\nРаненый лорд Эдмунд\n1000 рыцарей\nПотери\n500 рыцарей");
  assert.deepEqual(p.sides[0].contingents[0].commanders, ["Раненый лорд Эдмунд"]);
  assert.deepEqual(lines(p.sides[0]).map(l => l.count), [1000], "«Потери» остановили разбор");
});

test("формат «название - число» из отчётов о потерях", () => {
  const p = E.parseArmyText("Армия,\nРыцари Красных кольчуг - 2000\n6000 - Рыцари Дуртанга");
  assert.deepEqual(lines(p.sides[0]).map(l => [l.count, l.name]), [[2000, "Рыцари Красных кольчуг"], [6000, "Рыцари Дуртанга"]]);
});

// ── v30.2: хвосты сбора армий ──
test("несколько полководцев в строке делятся, титул с владением — нет (К1)", () => {
  const split = E.splitCommanders;
  assert.deepEqual(split("Лорд Андреас Дарлтон, принц Токимори Тосава, виконт Кельдар Берг, герцог Вольфганг Дарлтон"),
    ["Лорд Андреас Дарлтон", "принц Токимори Тосава", "виконт Кельдар Берг", "герцог Вольфганг Дарлтон"]);
  assert.deepEqual(split("Герцог Пикшарпа, Уильям Нерин"), ["Герцог Пикшарпа, Уильям Нерин"]);
  assert.deepEqual(split('Магистр ордена "Багрового лотоса", сир Персиваль'), ['Магистр ордена "Багрового лотоса", сир Персиваль']);
  assert.deepEqual(split('"Лев Долины Ветров", Лорд Джон Карнштайн'), ['"Лев Долины Ветров", Лорд Джон Карнштайн']);
  assert.deepEqual(split("Кастелян Артедайна, сир Родрик"), ["Кастелян Артедайна, сир Родрик"]);
  assert.deepEqual(split("капитан Вальтер из Лайоши, особые навыки отсутствуют"),
    ["капитан Вальтер из Лайоши, особые навыки отсутствуют"], "хвост без имени — пояснение");
});

test("Вторая битва при Пикшарпе: четыре полководца, войска первому с пометкой (К12)", () => {
  const p = E.parseArmyText(fixture("pikshsharp2"));
  const c = p.sides[1].contingents[0];
  assert.equal(c.commanders.length, 4);
  assert.ok(c.lines.every(l => l.commander === "Лорд Андреас Дарлтон" && l.cmdrCheck));
  assert.ok(p.sides[0].contingents[0].lines.every(l => !l.cmdrCheck), "у Нахта один полководец — проверять нечего");
  // мастер отдал конных Токимори
  const plan = E.planMuster(p);
  plan.sides[1].contingents[0].lines[1].commander = "принц Токимори Тосава";
  const r = E.expandMuster(plan);
  const riders = r.factions[1].units.filter(u => u.name.startsWith("Конные"));
  assert.ok(riders.length === 4 && riders.every(u => u.commander === "принц Токимори Тосава"));
  assert.equal(r.factions[1].commanders.length, 4, "заведены все четверо");
});

test("творительный падеж после «во главе с»", () => {
  assert.equal(E.instrumentalToNom("Шахом Джаффар Ибн Аббасом"), "Шах Джаффар Ибн Аббас");
  assert.equal(E.instrumentalToNom("Персивалем"), "Персиваль");
  assert.equal(E.instrumentalToNom("Андреем"), "Андрей");
  assert.equal(E.instrumentalToNom("Каэль Квислинг"), "Каэль Квислинг", "уже именительный — не трогаем");
});

test("Битва при Кордуа: число в конце, «во главе с», итоги контингентов, повтор после итога (К3)", () => {
  const p = E.parseArmyText(fixture("kordua"));
  assert.equal(p.title, "Битва при Кордуа");
  const [sin, kord] = p.sides;
  assert.equal(sin.contingents.length, 3);
  assert.equal(sin.contingents[0].faction, "Армия Сэйрая");
  assert.deepEqual(sin.contingents[0].commanders, ["Каэль Квислинг"]);
  assert.deepEqual(sin.contingents[1].commanders, ["Халид - Паша"]);
  assert.deepEqual(sin.contingents[0].lines.map(l => [l.count, l.name]),
    [[10000, "Сэйрайские копейшики"], [4000, "Сэйрайские лучники"], [4000, "Синские всадники"], [46, "Пушки в артелерии"]]);
  assert.deepEqual(sin.contingents[2].lines.map(l => [l.count, l.name, l.note]),
    [[4000, "Синские лучники", ""], [10, "Боевые слоны", "просто десять"]]);
  assert.equal(sin.total.n, 43000, "последнее «=» — итог стороны");
  assert.deepEqual(sin.contingents.map(c => c.total), [23000, 16000, 4000], "остальные — итоги контингентов");
  assert.ok(p.warnings.some(w => w.text.includes("«Армия Сэйрая»") && w.text.includes("18 000") && w.text.includes("23 000")),
    "арифметика Алекса: 10к + 4к + 4к — это 18к, а не 23к");
  assert.equal(p.warnings.filter(w => w.text.includes("повтор")).length, 2, "пушки и слоны после итога — повтор");
  assert.ok(!p.warnings.some(w => w.text.includes("Абдул")), "«= 4к» при 4010 — в пределах тысячи");
  assert.equal(kord.contingents[0].faction, "Армия Кордуя");
  assert.deepEqual(kord.contingents[0].commanders, ["Шах Джаффар Ибн Аббас"]);
  assert.equal(kord.sum, 19000);
  const plan = E.planMuster(p);
  const guns = plan.sides[0].contingents[0].lines[3];
  assert.equal(guns.machine, "cannon", "пушки — батарея (Ш4), а не отряд");
  assert.equal(guns.special, false);
  assert.equal(plan.sides[0].contingents[2].lines[1].special, true, "слоны — по-прежнему отряд с пометкой");
  const ex = E.expandMuster(plan);
  assert.deepEqual(ex.factions[0].machines.map(m => [m.name, m.engine, m.count]), [["Пушки в артелерии", "cannon", 46]], "46 пушек — одна батарея");
  assert.ok(!ex.factions[0].units.some(u => u.name.startsWith("Пушки")), "отряда «Пушки» нет");
  assert.equal(ex.machinesTotal, 1);
  assert.equal(plan.sides[1].contingents[0].lines[1].templateId, "infantry", "кордуанские мечники — пехота");
});

test("шаблоны пехоты и гвардии (К2)", () => {
  const id = n => E.matchTemplate(n).id;
  assert.equal(id("Мечники Дуртанга"), "infantry");
  assert.equal(id("Горцы Дуртанга"), "infantry");
  assert.equal(id("Королевская стража Данмира"), "guard");
  assert.equal(id("Лоутайдское ополчение с мечами"), "militia", "ополчение с мечами — всё ещё ополчение");
  assert.equal(id("Рыцари королевской стражи"), "knights", "конные — не гвардия пехоты");
  assert.equal(E.getTemplate("guard").discipline, 80);
});

test("правки фракции применяются при нарезке", () => {
  const ov = E.normalizeOverrides({factions: {"Красные Кольчуги": {knights: {discipline: 80, size: 500}}}});
  const plan = E.planMuster(E.parseArmyText(fixture("trinidar")), {overrides: ov});
  const r = E.expandMuster(plan, {overrides: ov, existingNames: {"красные кольчуги": ["Всадники Красных Кольчуг"]}});
  const riders = r.factions[0].units.filter(u => u.name.startsWith("Всадники"));
  assert.equal(riders.length, 36, "18000 по 500");
  assert.equal(riders[0].discipline, 80);
  assert.equal(riders[0].name, "Всадники Красных Кольчуг №2", "нумерация после уже стоящего отряда");
});
