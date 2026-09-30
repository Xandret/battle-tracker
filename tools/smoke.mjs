// ═══════════ smoke.mjs — сквозная проверка собранного dist/tracker.html ═══════════
// Требует jsdom (не входит в проект — это инструмент разработчика).
import fs from "node:fs";
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
const { JSDOM } = require("jsdom");

const html = fs.readFileSync(new URL("../dist/tracker.html", import.meta.url), "utf8");
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true });
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

console.log(process.exitCode ? "\nЕСТЬ ОШИБКИ" : "\nВсё в порядке");
