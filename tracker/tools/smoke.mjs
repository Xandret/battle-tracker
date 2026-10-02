// ═══════════ smoke.mjs — сквозная проверка собранного dist/tracker.html ═══════════
// Требует jsdom (не входит в проект — это инструмент разработчика).
import fs from "node:fs";
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const { JSDOM } = require("jsdom");

const html = fs.readFileSync(new URL("../dist/tracker.html", import.meta.url), "utf8");
// url нужен, чтобы работал localStorage: без него jsdom считает страницу «непрозрачным источником»
// Холста в jsdom нет: трекер это переживает (местность просто не рисуется), заглушка убирает шум в выводе
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true, url: "http://localhost/",
  beforeParse(win){ win.HTMLCanvasElement.prototype.getContext = () => null; } });
const w = dom.window;
w.confirm = () => true; w.alert = () => {};
const ev = c => w.eval(c);
const ok = (cond, msg) => { console.log((cond ? "  ✔ " : "  ✘ ") + msg); if(!cond) process.exitCode = 1; };

console.log("Запуск и шапка");
ok(ev("typeof Engine") === "object", "движок загружен как объект Engine");
ok(ev("document.getElementById('rulesetSel').options.length") === 3, "в списке правил 3 набора (1 доступен, 2 в ожидании)");
ok(ev("document.getElementById('rulesetSel').value") === "base", "выбран набор 1");
ok(ev("document.getElementById('newsBody').innerHTML").includes("Фундамент"), "«Что нового» показывает патчноут");
ok(ev("document.querySelectorAll('#newsBody pre').length") >= 1, "блоки кода в патчноуте отрисованы");

console.log("Армии");
ev(`document.getElementById('fx_name').value='Бладколл'; addFaction();
    document.getElementById('fx_name').value='Тосава'; addFaction();`);
const mk = (n, fi, ex) => ev(`newUnit(); document.getElementById('f_name').value=${JSON.stringify(n)};
  document.getElementById('f_faction').value=String(factions[${fi}].id); onUnitFactionChange();
  autoDetectType(); ${ex || ""} saveUnit();`);
mk("Всадники Бладколла", 0, `document.getElementById('f_soldiers').value='1000'; document.getElementById('f_disc').value='85';`);
mk("Пикинёры Эштауна", 1, `document.getElementById('f_soldiers').value='1000'; document.getElementById('f_disc').value='60';`);
mk("Лучники Эштауна", 1, `document.getElementById('f_soldiers').value='800';`);
ok(ev("units.length") === 3, "создано 3 отряда");
ok(ev("units.map(u=>u.type).join()") === "cavalry,pike,archer", "типы определены по названию");

console.log("Бой через панель");
ev(`setSide('att', units[0].id); setSide('def', units[2].id);
    document.getElementById('modeSel').value='melee_form'; onModeChange(); updateChargeBox();
    document.getElementById('charge').checked = true; resolveBattle();`);
ok(ev("log[0].title").includes("Всадники Бладколла → Лучники Эштауна"), "бой состоялся: " + ev("log[0].title"));
ok(ev("units[2].soldiers") < 800, "лучники понесли потери: " + ev("units[2].soldiers"));
ok(ev("units[0].attacksMade") === 1 && ev("units[0].acted") === true, "атака засчитана, отмечен «Походил»");
ok(ev("undoStack.length") > 0, "бой попал в откат");

console.log("Отказы движка доходят до журнала");
ev(`units[0].attacksMade = 2; setSide('att', units[0].id); setSide('def', units[1].id); resolveBattle();`);
ok(ev("log[0].title") === "Бой невозможен", "исчерпанные атаки дают отказ");

console.log("Бой с карты: пики против конницы в лоб");
ev(`endTurn(); units.forEach(u => placeOnMap(u.id));
    updUnit(units[0].id, {mapX:50, mapY:20}); updUnit(units[1].id, {mapX:50, mapY:50, facing:0}); renderMap();`);
ev(`startTargeting(units[0].id, true, 'melee_form'); tokenClick(units[1].id);`);
const L = JSON.parse(ev("JSON.stringify(log[0].lines)"));
ok(L.some(l => l.includes("натиск остановлен")), "пики останавливают натиск");
ok(L.some(l => l.includes("+200% урона")), "контрудар пикинёров ×3");

console.log("Проверки и конец хода");
ev(`updUnit(units[2].id, {morale: 30, status: 'active'}); moraleCheck(units[2].id);`);
ok(ev("log[0].title").startsWith("Проверка БД"), "проверка БД работает");
ev(`updUnit(units[2].id, {morale: 0, fleeChecks: 1}); fleeCheck(units[2].id);`);
ok(ev("log[0].title").startsWith("Проверка на побег"), "проверка на побег работает");
const t = ev("turn"); ev("endTurn()");
ok(ev("turn") === t + 1, "конец хода увеличил счётчик");
ok(ev("units.every(u => (u.attacksMade||0) === 0)"), "счётчики атак сброшены");

console.log("Сохранение");
const saved = JSON.parse(ev("JSON.stringify(stateObj())"));
ok(saved.ruleset === "base", "в сохранении записан набор правил");
ev(`applyLoadedState(${JSON.stringify(Object.assign({}, saved, {ruleset: "неизвестный"}))})`);
ok(ev("ruleset") === "base", "неизвестный набор при загрузке откатывается к набору 1");
ev("undo()");
ok(true, "откат отработал без ошибок");

console.log("Тип войск по названию (теперь в движке)");
ev(`newUnit(); document.getElementById('f_name').value = 'Пешие рыцари Сильварейнов'; autoDetectType();`);
ok(ev("document.getElementById('f_type').value") === "infantry", "«Пешие рыцари» — пехота, а не конница");
ev("hideForm()");

console.log("Сбор армий из текста: Штурм Тринидара");
const trinidar = fs.readFileSync(new URL("../../shared/battles/trinidar.txt", import.meta.url), "utf8");
const count = () => JSON.parse(ev("JSON.stringify({u: units.length, f: factions.length, c: commanders.length, undo: undoStack.length})"));
const before = count();
ev(`document.getElementById('importText').value = ${JSON.stringify(trinidar)}; parseImport();`);
ok(ev("document.querySelectorAll('#importPreview .imp-side').length") === 2, "предпросмотр: две стороны");
ok(ev("document.querySelector('#importPreview .imp-warn').textContent").includes("22 000"), "видно расхождение итога у Персиваля");
ok(ev("document.getElementById('impTotal').textContent").includes("50 отрядов"), "предпросмотр: 50 отрядов");
ok(ev("[...document.getElementById('tplScope').options].some(o => o.value === 'Красные Кольчуги')"),
   "сторона из предпросмотра доступна в шаблонах");
ev(`document.getElementById('tplScope').value = 'Красные Кольчуги'; renderTemplates(); setTplValue('knights', 'discipline', '80');`);
ok(ev("Engine.resolveTemplate('knights', 'Красные Кольчуги', templateOverrides).discipline") === 80, "правка шаблона для фракции");
ok(ev("document.querySelectorAll('#tplTable input.ovr').length") === 1, "изменённое значение подсвечено");
ev(`impLine(0, 0, 0, 'size', 2000);`);
const res = ev("document.getElementById('impRes_0_0_0').textContent");
ok(res.includes("9 отр. по 2 000"), "размер отряда переопределён в предпросмотре: " + res);
ev("commitImport()");
const after = count();
ok(after.u - before.u === 41, "собрано 41 отряд (9 + 6 + 3 + 1 и 22): " + (after.u - before.u));
ok(after.f - before.f === 2 && after.c - before.c === 2, "две фракции и два полководца");
const rider = JSON.parse(ev("JSON.stringify(units.find(u => u.name === 'Всадники Красных Кольчуг'))"));
ok(rider.type === "cavalry" && rider.discipline === 80 && rider.soldiers === 2000 && rider.initial === 2000,
   "всадники: кавалерия, дисциплина 80 от правки фракции, по 2000");
ok(ev(`commanders.find(c => c.id === ${rider.commanderId}).name`) === '"Бог войны" Король Генрих Файрлайн', "всадники под Генрихом");
ok(ev("log[0].title").includes("Штурм Тринидара"), "запись в журнале: " + ev("log[0].title"));
ok(ev("importDraft") === null && ev("document.getElementById('importPreview').innerHTML") === "", "предпросмотр закрыт");
ok(after.undo - before.undo === 2, "правка шаблона и сбор — два шага отката");
ev(`parseImport(); commitImport();`);
ok(ev("units.some(u => u.name === 'Всадники Красных Кольчуг №10')"), "повторный сбор продолжает нумерацию");
ok(ev("factions.filter(f => f.name === 'Красные Кольчуги').length") === 1, "фракция не задвоилась");
ok(ev("commanders.length") === after.c, "полководцы не задвоились");
ev("undo()"); ev("undo()");
ok(ev("units.length") === before.u, "два отката убрали оба сбора");
ok(ev("Engine.resolveTemplate('knights', 'Красные Кольчуги', templateOverrides).discipline") === 80, "правка шаблона раньше сбора осталась");

console.log("Шаблоны в сохранении");
const withTpl = JSON.parse(ev("JSON.stringify(stateObj())"));
ok(withTpl.templateOverrides.factions["Красные Кольчуги"].knights.discipline === 80, "правки шаблонов записаны в партию");
ev(`applyLoadedState(${JSON.stringify(Object.assign({}, withTpl, {templateOverrides: undefined}))})`);
ok(ev("JSON.stringify(templateOverrides)") === JSON.stringify({base: {}, factions: {}}), "старое сохранение без шаблонов открывается");
ev(`applyLoadedState(${JSON.stringify(withTpl)}); resetAll();`);
ok(ev("units.length") === 0 && ev("templateOverrides.factions['Красные Кольчуги'].knights.discipline") === 80,
   "«Сбросить всё» стирает партию, но не шаблоны");

console.log("Загрузка отряда из JSON");
await new Promise(done => {
  const file = new w.File([JSON.stringify({name: "Наёмные арбалетчики", type: "archer", weapon: "ranged", soldiers: 300})], "otryad.json");
  w.eval("importUnitJson")({target: {files: [file], value: ""}});
  setTimeout(done, 50);
});
ok(ev("units.find(u => u.name === 'Наёмные арбалетчики').type") === "archer", "арбалетчики остались стрелками, а не пехотой");

console.log("Полководцы при сборе: Вторая битва при Пикшарпе");
const piksharp = fs.readFileSync(new URL("../../shared/battles/pikshsharp2.txt", import.meta.url), "utf8");
ev(`document.getElementById('importText').value = ${JSON.stringify(piksharp)}; parseImport();`);
ok(ev("document.querySelectorAll('#importPreview .imp-side')[1].querySelector('.imp-cmdsel').options.length") === 5,
   "в списке четыре полководца и «без полководца»");
ok(ev("document.querySelectorAll('#importPreview .imp-line.guess').length") >= 2, "строки с несколькими полководцами подсвечены");
ev(`impCmdr(1, 0, 1, 'принц Токимори Тосава'); commitImport();`);
const konn = JSON.parse(ev(`JSON.stringify(units.filter(u => u.name.startsWith('Конные')).map(u => commanders.find(c => c.id === u.commanderId).name))`));
ok(konn.length === 4 && konn.every(n => n === "принц Токимори Тосава"), "конные ушли Токимори");
ok(ev("commanders.filter(c => ['Лорд Андреас Дарлтон','принц Токимори Тосава','виконт Кельдар Берг','герцог Вольфганг Дарлтон'].includes(c.name)).length") === 4,
   "заведены все четыре полководца");

console.log("Редактор карты (v30.3)");
const T = k => ev(`Engine.TERRAIN_BY_KEY.${k}.id`);
const cells = id => ev(`Array.from(terrainMap.t).filter(v => v === ${id}).length`);
ev("openEditor()");
ok(!ev("document.getElementById('mapEditor').classList.contains('hidden')"), "редактор открылся поверх трекера");
ok(ev("document.getElementById('edMapInfo').textContent").includes("Местности ещё нет"), "без местности предлагает создать карту");
ev(`document.getElementById('edNewW').value = '1000'; document.getElementById('edNewH').value = '600'; edNewSize(); edCreate();`);
ok(ev("terrainMap.w") === 200 && ev("terrainMap.h") === 120, "1000 × 600 м — это 200 × 120 клеток по 5 м");
ok(cells(T("field")) === 200 * 120, "новая карта залита полем");
const undo0 = ev("undoStack.length");
ev(`edSetValue(${T("forest")}); edSetSize(50);
    edBegin({x: 50, y: 50, fx: .25, fy: 50/120}); edMove({x: 70, y: 50, fx: .35, fy: 50/120}); edEnd();`);
ok(cells(T("forest")) > 150, "кисть 50 м нарисовала лес: " + cells(T("forest")) + " клеток");
ok(ev("undoStack.length") === undo0 + 1, "мазок — один шаг отката");
ev(`edSetValue(${T("wall")}); edSetTool('rect'); edSetSize(5);
    edBegin({x: 120, y: 20, fx: .6, fy: 20/120}); edMove({x: 150, y: 50, fx: .75, fy: 50/120}); edEnd();`);
ok(cells(T("wall")) === 120, "контур 31 × 31 клетку стеной толщиной 5 м — 120 клеток: " + cells(T("wall")));
ev(`edSetValue(${T("sand")}); edSetTool('fill'); edBegin({x: 135, y: 35, fx: 135/200, fy: 35/120});`);
ok(cells(T("sand")) === 29 * 29, "заливка двора остановилась на стенах: " + cells(T("sand")));
const undo1 = ev("undoStack.length");
ev(`edBegin({x: 135, y: 35, fx: 135/200, fy: 35/120});`);
ok(ev("undoStack.length") === undo1, "пустой мазок не засоряет откат");
ev(`edSetLayer('z'); edSetZ(2); edSetTool('brush'); edSetSize(30); edBegin({x: 30, y: 90, fx: .15, fy: .75}); edEnd();`);
ok(ev("Engine.cellAt(terrainMap, .15, .75).z") === 2 && ev("Engine.cellAt(terrainMap, .15, .75).t") === T("field"),
   "высота нарисована отдельным слоем, поле под ней осталось");
ev(`edSetLayer('t'); edSetTool('pick'); edBegin({x: 135, y: 35, fx: 135/200, fy: 35/120});`);
ok(ev("ed.value") === T("sand") && ev("ed.tool") === "brush", "пипетка взяла песок и вернула кисть");
ev("undo()");
ok(ev("Engine.cellAt(terrainMap, .15, .75).z") === 0, "откат убрал последний мазок — холм");
ok(ev("document.getElementById('edTools').textContent").includes("Прямоугольник"), "панель инструментов на месте");
ev(`document.getElementById('edLibName').value = 'Проба'; libSaveCurrent();`);
ok(ev("libRead().length") === 1 && ev("libRead()[0].name") === "Проба", "карта сохранена в свои шаблоны");
ev("closeEditor()");
ok(ev("document.getElementById('mapEditor').classList.contains('hidden')"), "«Готово» закрывает редактор");
const arb = ev("units.find(u => u.name === 'Наёмные арбалетчики').id");
ev(`placeOnMap(${arb}, 30, ${50 / 120 * 100}); renderMap();`);
ok(ev(`document.querySelector('.token[data-tid="${arb}"] .ttip').textContent`).includes("Местность: Лес"), "в подсказке фишки — местность под ней");
ev("zoomBattle(1.5)");
ok(ev("battleView.z") === 1.5, "карта приближается");
ev("fitBattle()");
ok(ev("battleView.z") === 1, "«Вся карта» возвращает масштаб");
const withMap = JSON.parse(ev("JSON.stringify(stateObj())"));
ok(withMap.battleMap && withMap.battleMap.w === 200 && JSON.stringify(withMap.battleMap).length < 5000,
   "местность в сохранении партии, сжата до " + JSON.stringify(withMap.battleMap).length + " символов");
ev(`applyLoadedState(${JSON.stringify(Object.assign({}, withMap, {battleMap: undefined}))})`);
ok(ev("terrainMap") === null, "старое сохранение без местности открывается");
ev(`applyLoadedState(${JSON.stringify(withMap)})`);
ok(cells(T("sand")) === 29 * 29, "местность вернулась из сохранения");
ev("resetAll()");
ok(ev("terrainMap") === null && ev("libRead().length") === 1, "«Сбросить всё» стирает карту партии, но не свои шаблоны");
ev("openEditor(); libApply(libRead()[0].id); closeEditor();");
ok(ev("terrainMap && terrainMap.w") === 200 && cells(T("wall")) === 120, "карта взята из своих шаблонов");

console.log("Шаблоны карт (v30.4)");
let asked = 0;
w.confirm = () => { asked++; return true; };
ev("openEditor()");
ok(ev("document.getElementById('edGen').querySelectorAll('option').length") >= 8, "в списке восемь шаблонов");
ev(`edGenPick('castle'); edGenSet('gateSide', 'north'); ed.gen.seed = 123; edGenerate(false);`);
ok(asked === 1, "карта была нарисована руками — трекер спросил, заменять ли");
ok(ev("terrainMap.meta.template") === "castle" && ev("terrainMap.meta.seed") === 123, "замок по шаблону, зерно 123");
ok(cells(T("wall")) > 50 && cells(T("tower")) > 20 && cells(T("gate")) > 0, "стены, башни и ворота на месте");
const castle123 = ev("JSON.stringify(Engine.serializeTerrain(terrainMap).t)");
ev("edGenerate(true)");
ok(asked === 1, "непоправленную карту по шаблону заменяет без вопроса");
ok(ev("terrainMap.meta.seed") !== 123 && ev("JSON.stringify(Engine.serializeTerrain(terrainMap).t)") !== castle123, "«Ещё вариант» — новое зерно, другая карта");
ev(`ed.gen.seed = 123; edGenerate(false);`);
ok(ev("JSON.stringify(Engine.serializeTerrain(terrainMap).t)") === castle123, "то же зерно — та же карта");
ev(`edGenPick('river'); edGenSet('bridges', 2); edGenerate(true);`);
ok(cells(T("water")) > 100 && cells(T("bridge")) > 0, "река с мостами");
ev("closeEditor()");
ok(ev("log[0].title") === "Карта местности обновлена" && ev("log[0].lines[0]").includes("«Река»"),
   "одна запись в журнал за сеанс правки: " + ev("log[0].lines[0]"));

console.log("Правила карты: метры и местность (v30.5)");
ev(`terrainMap = Engine.createTerrain(2000, 1000, ${T("field")}); Engine.paintRect(terrainMap, 't', 0, 0, 399, 99, ${T("forest")}); terrainVersion++;
    document.getElementById('fx_name').value = 'Лучники Севера'; addFaction();
    document.getElementById('fx_name').value = 'Лесные'; addFaction();`);
const mkU = (name, fac, x, y, extra) => ev(`newUnit(); document.getElementById('f_name').value = ${JSON.stringify(name)};
  document.getElementById('f_faction').value = String(factions.find(f => f.name === ${JSON.stringify(fac)}).id); onUnitFactionChange();
  autoDetectType(); ${extra || ""} saveUnit(); placeOnMap(units[units.length - 1].id, ${x}, ${y}); units[units.length - 1].id`);
const bows = mkU("Лучники на опушке", "Лучники Севера", 50, 75, "document.getElementById('f_soldiers').value = '1000';");
const wood = mkU("Ополчение в лесу", "Лесные", 50, 25, "document.getElementById('f_soldiers').value = '1000';");
ok(ev("mapActive()") === false, "по умолчанию правила карты выключены");
ev(`openTokenMenu(${bows})`);
ok(!ev("document.querySelector('.tmenu').textContent").includes("по местности"), "выключены — в меню прежние кнопки строя");
ev(`closeTokenMenu(); setMapRule('on', true);`);
ok(ev("mapRules.on") === true && ev("document.getElementById('mapRulesBtn').textContent").includes("вкл"), "правила карты включены, кнопка в шапке это показывает");
ok(ev("log[0].title") === "Правила карты включены", "включение записано в журнал");
ev("renderMap()");
const tw = ev(`parseInt(document.querySelector('.token[data-tid="${bows}"] .tdisc').style.width)`);
const expectW = ev(`Math.max(6, Math.round(Engine.footprint(units.find(u => u.id === ${bows}), currentRules()).front * battleView.sw / mapWidthMeters()))`);
ok(ev(`document.querySelector('.token[data-tid="${bows}"] .tdisc').classList.contains('scaled')`) && tw === expectW,
   `фишка — прямоугольник строя в масштабе: ${tw} px`);
ok(ev(`document.querySelector('.token[data-tid="${bows}"] .ttip').textContent`).includes("Строй: 200 × 5 м"), "в подсказке — размер строя в метрах");
ev(`openTokenMenu(${bows})`);
const menu = ev("document.querySelector('.tmenu').textContent");
ok(menu.includes("режим по местности") && !menu.includes("Меньше"), "в меню — атака «по местности», ручного размера нет");
ev(`startTargeting(${bows}, false, 'auto'); tokenClick(${wood});`);
ok(ev("log[0].title").includes("Дальний бой · пересечённая"), "цель в лесу — режим «пересечённая»: " + ev("log[0].title"));
const L5 = JSON.parse(ev("JSON.stringify(log[0].lines)"));
ok(L5.some(l => l.startsWith("🗺 Местность: «Ополчение в лесу» — лес")), "в журнале — местность обеих сторон");
ok(L5.some(l => l.startsWith("Укрытие «Ополчение в лесу» (лес): −30%") && l.endsWith("черновик")), "укрытие от стрел 30% с пометкой «черновик»");
ok(ev("document.getElementById('mapWidthM').disabled") === true, "с местностью масштаб задан ею — поле ширины заблокировано");
ev(`Engine.paintRect(terrainMap, 't', 0, 100, 399, 199, ${T("sand")}); terrainVersion++;
    updUnit(${bows}, {acted: true, turnsActive: 9, fatigue: 0}); endTurn();`);
ok(ev(`units.find(u => u.id === ${bows}).fatigue`) === 20, "на песке усталость копится вдвое быстрее (черновик)");
const withRules = JSON.parse(ev("JSON.stringify(stateObj())"));
ok(withRules.mapRules.on === true, "положение переключателей хранится в партии");
ev(`applyLoadedState(${JSON.stringify(Object.assign({}, withRules, {mapRules: undefined}))}); renderAll();`);
ok(ev("mapRules.on") === false, "старое сохранение — правила карты выключены");
ev(`applyLoadedState(${JSON.stringify(withRules)}); setMapRule('terrain', false);`);
ev(`units.forEach(u => updUnit(u.id, {attacksMade: 0})); startTargeting(${bows}, false, 'ranged_form'); tokenClick(${wood});`);
ok(!JSON.parse(ev("JSON.stringify(log[0].lines)")).some(l => l.startsWith("Укрытие")), "местность выключена отдельно — укрытия нет, остальное работает");
ev(`setMapRule('on', false); renderMap();`);
ok(!ev(`document.querySelector('.token[data-tid="${bows}"] .tdisc').classList.contains('scaled')`), "выключены — снова фишки-значки");
ev(`terrainMap = null; terrainVersion++; mapImage = 'data:image/gif;base64,R0lGODlhAQABAAAAACw='; renderMap();
    applyCalibration([0.1, 0.5], [0.6, 0.5], 300);`);
ok(ev("mapOpts.widthM") === 600 && ev("document.getElementById('mapWidthM').value") === "600", "калибровка: 300 м на половину ширины — карта 600 м");
ev("mapImage = null; renderMap();");

console.log("Движение и дальности (v30.6)");
let confirms = 0;
w.confirm = () => { confirms++; return true; };
ev(`terrainMap = Engine.createTerrain(1000, 1000, ${T("field")}); Engine.paintRect(terrainMap, 't', 100, 0, 199, 199, ${T("forest")}); terrainVersion++;
    setMapRule('on', true); units.forEach(u => updUnit(u.id, {onMap: false, attacksMade: 0, movedM: 0, runUpM: 0})); renderAll();`);
ok(ev("moveActive() && rangeActive()") === true, "движение и дальности включены вместе с правилами карты");
const foot = mkU("Пехота на марше", "Лучники Севера", 20, 50);
ev(`accountMoves([{id: ${foot}, from: [.2, .5], to: [.3, .5]}]);`);
ok(ev(`units.find(u => u.id === ${foot}).movedM`) === 100 && ev("log[0].title") !== "Движение сверх нормы — решение мастера",
   "100 м по полю — в норме, без записи");
ev(`updUnit(${foot}, {mapX: 30}); accountMoves([{id: ${foot}, from: [.3, .5], to: [.4, .5]}]);`);
ok(ev("log[0].title") === "Движение сверх нормы — решение мастера" && ev("log[0].lines[0]").includes("за ход 200 из 100 м"),
   "ещё 100 м — сверх нормы, запись в журнал: " + ev("log[0].lines[0]"));
ev(`updUnit(${foot}, {mapX: 40}); accountMoves([{id: ${foot}, from: [.4, .5], to: [.55, .5]}]);`);
const went = +/за ход ([0-9]+) из/.exec(ev("log[0].lines[0]"))[1];
ok(went >= 395 && went <= 410, "лес дороже: 50 м полем и 50 м лесом ×2 — ещё около 200 м (до клетки): " + ev("log[0].lines[0]"));
ev("endTurn()");
ok(ev(`units.find(u => u.id === ${foot}).movedM`) === 0, "конец хода сбрасывает пройденное");
ev(`renderMap();`);
ok(ev(`document.querySelector('.token[data-tid="${foot}"] .ttip').textContent`).includes("Прошёл за ход: 0 / 100 м"), "в подсказке — пройденное за ход");
// разбег для натиска
const riders = mkU("Всадники с разбегу", "Лучники Севера", 30, 80, "document.getElementById('f_soldiers').value = '100';");
const pikes = mkU("Пехота врага", "Лесные", 30, 81.5, "document.getElementById('f_soldiers').value = '100';");
ev(`setSide('att', ${riders}); setSide('def', ${pikes}); document.getElementById('modeSel').value = 'melee_form'; onModeChange(); updateChargeBox();
    document.getElementById('charge').checked = true; resolveBattle();`);
ok(JSON.parse(ev("JSON.stringify(log[0].lines)")).some(l => l.startsWith("🐎 Натиск невозможен — разбег 0 м из 50")), "без разбега натиска нет");
ev(`units.forEach(u => updUnit(u.id, {attacksMade: 0})); updUnit(${riders}, {mapY: 70});
    accountMoves([{id: ${riders}, from: [.3, .7], to: [.3, .8]}]); updUnit(${riders}, {mapY: 80});`);
ok(ev(`units.find(u => u.id === ${riders}).runUpM`) === 100, "100 м по полю — разбег есть");
ev(`setSide('att', ${riders}); setSide('def', ${pikes}); document.getElementById('modeSel').value = 'melee_form'; onModeChange(); updateChargeBox();
    document.getElementById('charge').checked = true; resolveBattle();`);
const Lc = JSON.parse(ev("JSON.stringify(log[0].lines)"));
ok(!Lc.some(l => l.includes("Натиск невозможен")) && Lc.some(l => l.startsWith("🐎 Натиск")), "с разбегом — натиск");
ok(ev(`units.find(u => u.id === ${riders}).runUpM`) === 0, "разбег истрачен на удар");
// дальности
const bowsFar = mkU("Лучники дозора", "Лучники Севера", 50, 95, "document.getElementById('f_soldiers').value = '300';");
const farT = mkU("Дальняя цель", "Лесные", 50, 5, "document.getElementById('f_soldiers').value = '300';");
ev(`startTargeting(${bowsFar}, false, 'auto');`);
ok(ev(`document.querySelector('.token[data-tid="${farT}"]').classList.contains('far-target')`), "цель в 900 м — серая");
const c0 = confirms;
ev(`tokenClick(${farT});`);
ok(confirms === c0 + 1 && JSON.parse(ev("JSON.stringify(log[0].lines)")).some(l => l.startsWith("⚠ Вне досягаемости: до «Дальняя цель»")),
   "атака вне досягаемости — только с подтверждения мастера и с пометкой");
ev(`newUnit(); document.getElementById('f_name').value = 'Лучники с длинными луками'; autoDetectType(); document.getElementById('f_range').value = '260'; saveUnit();`);
ok(ev("units[units.length - 1].range") === 260 && ev("Engine.rangeOf(units[units.length - 1], currentRules())") === 260, "своя дальность в карточке отряда");
ev(`setMapRule('move', false); accountMoves([{id: ${foot}, from: [.4, .5], to: [.9, .5]}]);`);
ok(ev(`units.find(u => u.id === ${foot}).movedM`) === 0, "движение выключено отдельно — путь не считается");
ev("setMapRule('on', false);");

console.log("Каскадная паника (v30.7)");
ev(`terrainMap = Engine.createTerrain(1000, 1000, ${T("field")}); terrainVersion++;
    units.forEach(u => updUnit(u.id, {onMap: false})); setMapRule('on', true);`);
ok(ev("panicActive()") === true && ev("mapRules.panicMorale") === false, "паника включена с правилами карты, «−100 БД» — выключено");
const line = [0, 1, 2, 3].map(i => mkU("Звено " + i, "Лучники Севера", 20 + i * 11, 50, "document.getElementById('f_soldiers').value = '400'; document.getElementById('f_disc').value = '1';"));
const foe7 = mkU("Враг рядом", "Лесные", 25, 55, "document.getElementById('f_soldiers').value = '400'; document.getElementById('f_disc').value = '1';");
ev(`markFled(${line[0]})`);
ok(ev("log[0].title") === "🏳 Каскадная паника: бегство «Звено 0»", "волна паники — отдельной записью: " + ev("log[0].title"));
ok(line.every(id => ev(`units.find(u => u.id === ${id}).status`) === "fled"), "вся цепочка побежала — волна за волной");
ok(ev(`units.find(u => u.id === ${foe7}).status`) === "active", "бегство врага паники не вызывает");
ok(ev("log[0].lines.some(l => l.includes('волна 3'))"), "в журнале видно кольца волны");
ok(ev("undoStack[undoStack.length - 1].label") === "«Звено 0» покинул поле боя", "побег и вся волна — один шаг отката");
ev("undo()");
ok(line.every(id => ev(`units.find(u => u.id === ${id}).status`) === "active"), "откат вернул всех");
ev(`setMapRule('panicMorale', true); markFled(${line[3]});`);
ok(ev("log[0].lines.some(l => l.includes('−100'))"), "«−100 БД вместе с проверкой» — в журнале");
ev(`undo(); undo(); setMapRule('panic', false); markFled(${line[0]});`);
ok(ev("log[0].title") === "«Звено 0» покинул поле боя" && ev(`units.find(u => u.id === ${line[1]}).status`) === "active",
   "паника выключена отдельно — бегство без волны");
ev(`undo(); setMapRule('panic', true); updUnit(${line[1]}, {morale: 0, fleeChecks: 1}); fleeCheck(${line[1]});`);
ok(ev(`units.find(u => u.id === ${line[1]}).status`) !== "fled" || ev("log[0].title").startsWith("🏳 Каскадная паника"),
   "проваленная проверка на побег тоже запускает волну");
ev("setMapRule('on', false);");

console.log("Штурм: участки стен (v30.8)");
ev(`terrainMap = Engine.generateMap('castle', {}, 3); terrainVersion++; setMapRule('on', true);`);
ok(ev("siegeActive()") === true && ev("mapRules.siege") === true, "штурм включён вместе с правилами карты");
ev("renderMap()");
ok(ev("terrainMap.forts.length") === 21, "участки замка построены при первом взгляде: " + ev("terrainMap.forts.length"));
// верхняя стена замка: участок №3 — клетки x 50…54, y 41; щёлкаем по левому краю
const pctX = x => x / ev("terrainMap.w") * 100, pctY = y => y / ev("terrainMap.h") * 100;
ev(`openFortMenu(3, ${pctX(50.4)}, ${pctY(41.5)})`);
ok(ev("document.querySelector('.fmenu .tm-head').textContent").includes("Участок №3 · каменная стена"), "меню участка открыто");
ok(ev("document.querySelector('.fmenu .tm-sub').textContent").includes("100 из 100"), "полная прочность — 100 из 100");
ev(`document.getElementById('fortDmg').value = '120'; fortHit();`);
ok(ev("log[0].title") === "🏰 Удар по участку №3 (каменная стена)", "удар — запись журнала: " + ev("log[0].title"));
ok(ev("log[0].lines.some(l => l.includes('Пролом: 10 м'))"), "прочность на нуле — пролом 10 м");
ok(cells(T("breach")) === 2 && ev(`terrainMap.t[41 * terrainMap.w + 50]`) === T("breach"), "две клетки у точки щелчка стали проломом");
ok(ev("document.querySelector('.fmenu .tm-sub').textContent").includes("80 из 100"), "остаток удара: 80 из 100");
const savedMap = JSON.parse(ev("localStorage.getItem('battle_tracker_v13')")).battleMap;
ok(savedMap.s && savedMap.forts.find(f => f.id === 3).dmg === 20 && savedMap.forts.find(f => f.id === 3).breaches === 1, "урон и пролом — в сохранении партии");
ev("undo()");
ok(cells(T("breach")) === 0 && ev("Engine.getSection(terrainMap, 3).dmg") === 0, "откат вернул стену целой");
ev(`openFortMenu(19, ${pctX(60)}, ${pctY(58.5)}); document.getElementById('fortDmg').value = '80'; fortHit();`);
ok(ev("log[0].lines.some(l => l.includes('Ворота выбиты'))") && ev("Engine.getSection(terrainMap, 19).up") === 0, "окованные ворота выбиты за 80");
ev(`undo(); openFortMenu(3, ${pctX(52)}, ${pctY(41.5)}); document.getElementById('fortDmg').value = '30'; fortHit(); fortRepair();`);
ok(ev("log[0].title").startsWith("🔧 Починка участка №3") && ev("Engine.getSection(terrainMap, 3).dmg") === 0, "починка — прочность снова 100");
ev(`setMapRule('siege', false); openFortMenu(3, ${pctX(52)}, ${pctY(41.5)});`);
ok(ev("document.querySelector('.fmenu')") === null && ev("fortMenu") === null, "штурм выключен — меню участка не открывается");
const old = JSON.parse(ev("JSON.stringify(stateObj())")); delete old.battleMap.s; delete old.battleMap.forts; delete old.mapRules.siege;
ev(`applyLoadedState(${JSON.stringify(old)}); renderAll();`);
ok(ev("mapRules.siege") === true && ev("terrainMap.forts.length") === 21, "сохранение v30.7: штурм включён по умолчанию, участки построены заново");
ev("setMapRule('on', false);");

console.log("Осадные машины на карте (v30.9)");
ev(`terrainMap = Engine.generateMap('castle', {}, 3); terrainVersion++; setMapRule('on', true); setMapRule('siege', true);`);
ev(`document.getElementById('mc_kind').value = 'trebuchet'; document.getElementById('mc_count').value = '4';
    document.getElementById('mc_faction').value = String(factions[0].id); addMachine();`);
const trb = ev("machines[machines.length - 1].id");
ok(ev("machines.length") === 1 && ev("machines[0].name") === "Требушет" && ev("machines[0].count") === 4 && ev("machines[0].crew") === 48,
   "машина из панели: требушет ×4, расчёт 48");
ok(ev("log[0].title") === "⚙ Новая машина: «Требушет»", "новая машина — в журнале");
ev(`placeMachine(${trb}); machines[0].mapX = 60 / terrainMap.w * 100; machines[0].mapY = 82 / terrainMap.h * 100; renderMap();`);
ok(ev("document.querySelector('.mtoken .mt-ab').textContent") === "ТРБ", "фишка машины на карте");
ev(`machineClick(${trb})`);
ok(ev("document.querySelector('.mmenu .tm-head').textContent").includes("Требушет"), "щелчок по фишке — меню машины");
ev(`startMachineAim(${trb})`);
ok(ev("document.getElementById('mapHint').textContent").includes("Цель для «Требушет»") && ev("document.getElementById('mapWrap').classList.contains('aiming')"),
   "режим прицела: подсказка и прицел вместо курсора");
const wallS = JSON.parse(ev("JSON.stringify(terrainMap.forts.find(f => f.kind === 'wall' && f.cy > 55))"));
const fire = () => ev(`fireMachine({sectionId: ${wallS.id}, x: ${wallS.cx} / terrainMap.w * 100, y: ${wallS.cy} / terrainMap.h * 100})`);
fire();
ok(ev("log[0].title") === `⚙ Требушет → участок №${wallS.id} (каменная стена)`, "залп по стене — запись журнала: " + ev("log[0].title"));
ok(ev("log[0].lines[0]").startsWith("🗺 До цели") && ev("log[0].lines.some(l => l.startsWith('Шанс попасть'))"), "в журнале — дистанция и шанс попасть");
ok(ev("machines[0].ready") === 2 && ev("undoStack[undoStack.length - 1].label") === "залп «Требушет»", "после залпа — перезарядка 2 хода, залп в откате");
const dmg1 = ev(`Engine.getSection(terrainMap, ${wallS.id}).dmg + Engine.getSection(terrainMap, ${wallS.id}).breaches * 100`);
ev(`machineAim = ${trb};`); fire();
ok(ev("log[0].title") === "Выстрел невозможен" && ev("log[0].lines.some(l => l.includes('перезаряжается'))"), "перезаряжается — второй залп не даёт");
ev("endTurn(); endTurn();");
ok(ev("machines[0].ready") === 0, "конец хода снимает перезарядку");
ev("undo(); undo(); undo();");
ok(ev(`Engine.getSection(terrainMap, ${wallS.id}).dmg`) === 0 && ev("machines[0].ready") === 0, "откат залпа вернул стену и машину" + (dmg1 ? "" : " (залп промахнулся)"));
ok(JSON.parse(ev("localStorage.getItem('battle_tracker_v13')")).machines.length === 1, "машины — в сохранении партии");
// перемещение: ход марша
ev(`moveMachine(${trb}, 30, 85)`);
ok(ev("machines[0].mapX") === 30, "требушет перенесли (подтверждение — разборка)");
ok(ev("machines[0].deployLeft") === 1 && ev("log[0].title") === "⚙ «Требушет»: новая позиция", "после марша — ход без выстрела");
// удар по машине и захват
ev(`machineClick(${trb}); document.getElementById('mmHit').value = '120'; hitMachineUi(${trb});`);
ok(ev("machines[0].count") === 2 && ev("log[0].lines.some(l => l.includes('выбито орудий 2'))"), "удар 120 по прочности 50 — выбито два орудия");
ev(`openMachineId = ${trb}; renderMap(); document.getElementById('mmFaction').value = String(factions[1].id); captureMachineUi(${trb});`);
ok(ev("machines[0].factionId") === ev("factions[1].id") && ev("log[0].title").startsWith("🏳 «Требушет» захвачена"), "захват — машина у другой фракции");
// маг-батарея по отряду: брызги и шок не нужны — без мага молчит
ev(`machines.push(Engine.makeMachine('magic', 2, currentRules(), {id: nextId++, onMap: true, mapX: 50, mapY: 90, mageSkill: 0})); renderAll();`);
const mg = ev("machines[machines.length - 1].id");
ev(`machineAim = ${mg}; fireMachine({unitId: units.find(u => u.onMap || true).id});`);
ok(ev("log[0].lines.some(l => l.includes('нет мага'))"), "маг-пушка без мага молчит");
// сбор армий: пушки — батарея, а не отряд
ev(`document.getElementById('importText').value = 'Армия Пробы,\\n1000 копейщиков\\n46 пушек\\n3 маг-пушки'; parseImport(); commitImport();`);
ok(ev("machines.some(m => m.engine === 'cannon' && m.count === 46)") && ev("machines.some(m => m.engine === 'magic' && m.count === 3)"),
   "из текста: 46 пушек и 3 маг-пушки — батареи");
ok(!ev("units.some(u => u.factionId === factions.find(f => f.name === 'Армия Пробы').id && /пуш/i.test(u.name))") && ev("log[0].lines.some(l => l.includes('⚙ машины'))"),
   "отрядов «пушки» нет, в журнале — машины");
// старое сохранение без машин
const oldS = JSON.parse(ev("JSON.stringify(stateObj())")); delete oldS.machines;
ev(`applyLoadedState(${JSON.stringify(oldS)}); renderAll();`);
ok(ev("Array.isArray(machines) && machines.length === 0"), "сохранение v30.8 без машин открывается");
ev("setMapRule('on', false);");

console.log("Приступ на стену (v30.10)");
ev(`terrainMap = Engine.generateMap('castle', {}, 1); terrainVersion++; setMapRule('on', true); setMapRule('siege', true); assaults = {};
    (() => {
      const geo = mapGeo(), wall = terrainMap.forts.find(f => f.kind === 'wall' && f.cy > 55);
      const pos = (cx, cy) => ({mapX: cx * 5 / geo.W * 100, mapY: cy * 5 / geo.H * 100});
      const base = {type: 'infantry', weapon: 'melee', discipline: 50, morale: 70, eqAtk: 60, eqDef: 60, exp: 20, mastery: 10, fatigue: 0, onMap: true};
      units.forEach(u => { u.onMap = false; });
      units.push(makeUnit(Object.assign({}, base, {name: 'Гарнизон стены', factionId: factions[1].id, soldiers: 300, facing: 180}, pos(wall.cx, wall.cy))));
      units.push(makeUnit(Object.assign({}, base, {name: 'Штурмовые', factionId: factions[0].id, soldiers: 1000, facing: 0}, pos(wall.cx, wall.cy + 3.2))));
      window._wall = wall.id; renderAll();
    })();`);
const stm = ev("units[units.length - 1].id"), gar = ev("units[units.length - 2].id"), wallId = ev("_wall");
const menuText = () => ev("Array.from(document.querySelectorAll('.tmenu button')).map(b => b.textContent).join(' | ')");
ev(`openTokenMenu(${stm})`);
ok(menuText().includes("Выдать лестницы: 20 (1 на 50)"), "в меню — выдать лестницы по черновику");
ev(`giveLadders(${stm})`);
ok(ev(`units.find(u => u.id === ${stm}).ladders`) === 20 && ev("log[0].title") === "🪜 «Штурмовые»: лестниц 20", "лестницы выданы — в журнале");
ev(`openTokenMenu(${stm})`);
ok(menuText().includes("На стену по лестницам (20)"), "в меню — приступ по лестницам");
ev(`startAssault(${stm}, 'ladders')`);
ok(ev("document.getElementById('mapHint').textContent").includes("Приступ «Штурмовые» по лестницам"), "выбор участка: подсказка");
ev(`doAssault(${wallId})`);
ok(ev("log[0].title") === `🪜 Приступ: Штурмовые → участок №${wallId} (каменная стена)`, "приступ — запись журнала: " + ev("log[0].title"));
ok(ev("log[0].lines[0]").includes("в бой вступают 200 из 1000") && ev("log[0].lines.some(l => l.startsWith('🏰 Со стены сверху вниз'))"),
   "по лестницам — 200 бойцов, у защитника высота");
ok(ev(`units.find(u => u.id === ${gar}).soldiers`) < 300 && ev(`assaults[${wallId}].includes(${stm})`), "гарнизон понёс потери, приступ записан на участок");
ok(ev("undoStack[undoStack.length - 1].label") === "приступ «Штурмовые»", "приступ — шаг отката");
ev(`startAssault(${stm}, 'ladders'); doAssault(${wallId})`);
ok(ev("log[0].title") === "Приступ невозможен" && ev("log[0].lines[0]").includes("израсходовал атаки"), "вторая атака за ход — отказ");
ev("endTurn()");
ok(Object.keys(JSON.parse(ev("JSON.stringify(assaults)"))).length === 0, "конец хода — фронт свободен");
ev(`updUnit(${gar}, {status: 'destroyed', soldiers: 0}); startAssault(${stm}, 'ladders'); doAssault(${wallId})`);
ok(ev(`Engine.getSection(terrainMap, ${wallId}).holder`) === ev("factions[0].id") && ev("log[0].lines.some(l => l.includes('занят'))"),
   "защитников нет — участок занят");
ev(`openFortMenu(${wallId}, 50, 50)`);
ok(ev("document.querySelector('.fmenu').textContent").includes("🚩 Занят"), "в меню участка — кто его занял");
const sv = JSON.parse(ev("localStorage.getItem('battle_tracker_v13')"));
ok(sv.battleMap.forts.find(f => f.id === wallId).holder === ev("factions[0].id") && sv.units.find(u => u.id === stm).ladders >= 0, "занятый участок и лестницы — в сохранении");
ev("undo()");
ok(ev(`Engine.getSection(terrainMap, ${wallId}).holder`) === undefined, "откат вернул участок защитникам");
ev("setMapRule('on', false);");

console.log("Облик отряда в игре (v30.11)");
ev("resetAll()");
ev(`document.getElementById('fx_name').value = 'Дом Кацуры'; addFaction();`);
const fk = ev("factions[0].id");
const fv = id => ev(`document.getElementById('${id}').value`);
const lookForm = name => ev(`newUnit(); document.getElementById('f_name').value = ${JSON.stringify(name)};
  document.getElementById('f_faction').value = String(${fk}); onUnitFactionChange(); autoDetectType();`);
lookForm("Самураи Кацуры");
ok(fv("f_style") === "fareast" && fv("f_kit") === "bow", "облик нового отряда — по названию: самураи — дальневосточные лучники");
ok(ev("document.getElementById('lookHint').textContent").includes("Япония"), "подсказка — во что одет стиль");
ev(`document.getElementById('f_style').value = 'south'; styleTouched = true;
  document.getElementById('f_name').value = 'Копейщики Кацуры'; autoDetectType(); saveUnit();`);
const ku = JSON.parse(ev("JSON.stringify(units[units.length - 1])"));
ok(ku.style === "south" && ku.kit === "pike", "стиль, выбранный вручную, название не сбивает; облик записан в отряд");
lookForm("Ополчение Кацуры");
ok(fv("f_style") === "south" && fv("f_kit") === "militia", "стиль нового отряда — как у прошлого отряда фракции");
ev("hideForm()");
ev(`cloneUnit(${ku.id})`);
ok(ev("units[units.length - 1].style") === "south" && ev("units[units.length - 1].kit") === "pike", "клон — с тем же обликом");
// старая партия без облика: открывается, форма показывает угаданный, «Сохранить» записывает
const oldLook = {id: 900, name: "Рыцари Лоутайда", type: "infantry", weapon: "melee", factionId: null, soldiers: 500, initial: 500,
  discipline: 60, morale: 60, eqAtk: 60, eqDef: 60, exp: 10, mastery: 20, fatigue: 0, status: "active"};
ev(`applyLoadedState({factions: [], units: [${JSON.stringify(oldLook)}], log: [], turn: 1, nextId: 901}); renderAll(); startEdit(900);`);
ok(fv("f_style") === "west" && fv("f_kit") === "sword" && ev("document.getElementById('lookHint').textContent").includes("ещё нет"),
   "старый отряд без облика — угаданный (пешие рыцари — мечники), с пометкой");
ev("saveUnit()");
ok(ev("units[0].style") === "west" && ev("units[0].kit") === "sword", "«Сохранить» записывает облик");

console.log(process.exitCode ? "\nЕСТЬ ОШИБКИ" : "\nВсё в порядке");
