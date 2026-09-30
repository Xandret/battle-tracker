// ═══════════ make-golden.mjs — эталон поведения v29 ═══════════
// Прогоняет сценарии через оригинальный v29 в эмуляторе браузера (jsdom) с сидированным Math.random
// и сохраняет результат в shared/golden/golden.json. Запускается один раз; тесты потом работают без jsdom.
import fs from "node:fs";
import { createRequire } from "node:module";
import { buildScenarios } from "../tests/scenarios.mjs";

const require = createRequire(import.meta.url);
const { JSDOM } = require("jsdom");

const V29 = process.argv[2];
if(!V29){ console.error("usage: node tools/make-golden.mjs path/to/tracker_boya_v29.html"); process.exit(1); }
const html = fs.readFileSync(V29, "utf8");
const MULBERRY = `function mulberry32(seed){ let a = seed >>> 0; return function(){ a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }`;

const scenarios = buildScenarios();
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true });
const w = dom.window;
w.confirm = () => true; w.alert = () => {};
const ev = c => w.eval(c);
ev(MULBERRY);
// журнал и отрисовка не влияют на расчёт; сохранение и откат — лишняя работа
ev(`saveState = function(){}; pushUndo = function(){};`);

const out = [];
for(const sc of scenarios){
  ev(`applyLoadedState(${JSON.stringify(sc.state)}); log = []; renderAll();`);
  const a = sc.action;
  if(a.kind === "battle"){
    ev(`(function(){
      const r = ${JSON.stringify(a.req)};
      document.getElementById('attSel').innerHTML = '<option value="${a.att}">a</option>';
      document.getElementById('attSel').value = '${a.att}';
      document.getElementById('defSel').innerHTML = '<option value="${a.def}">d</option>';
      document.getElementById('defSel').value = '${a.def}';
      document.getElementById('modeSel').value = r.mode;
      document.getElementById('sitBox').classList.toggle('hidden', isMeleeMode(r.mode));
      updateChargeBox();
      document.getElementById('charge').checked = r.charge;
      document.getElementById('counterCharge').checked = r.counterCharge;
      document.getElementById('mutual').checked = r.mutual;
      document.getElementById('sitPct').value = String(r.sitPct);
      document.getElementById('fatigueMode').value = r.fatigueMode;
      // renderSelects внутри resolveBattle перестроит списки — это уже после расчёта
      renderSelects = function(){};
      Math.random = mulberry32(${sc.seed});
      resolveBattle();
    })()`);
  } else if(a.kind === "morale"){
    ev(`Math.random = mulberry32(${sc.seed}); moraleCheck(${a.id});`);
  } else if(a.kind === "flee"){
    ev(`Math.random = mulberry32(${sc.seed}); fleeCheck(${a.id});`);
  } else {
    ev(`Math.random = mulberry32(${sc.seed}); endTurn();`);
  }
  const entry = ev(`log.length ? JSON.stringify({title: log[0].title, lines: log[0].lines, tone: log[0].tone}) : "null"`);
  const units = ev(`JSON.stringify(units)`);
  out.push({ n: sc.n, log: JSON.parse(entry), units: JSON.parse(units), turn: ev("turn") });
}
fs.writeFileSync(new URL("../../shared/golden/golden.json", import.meta.url), JSON.stringify(out));
console.log(`эталон записан: ${out.length} сценариев`);
