// ═══════════ engine.test.mjs — движок против эталона v29 ═══════════
// Запуск: npm test (node --test). Никакого браузера не нужно.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { buildScenarios } from "./scenarios.mjs";
import * as E from "../src/engine/index.js";

const golden = JSON.parse(fs.readFileSync(new URL("../../shared/golden/golden.json", import.meta.url), "utf8"));
const scenarios = buildScenarios();

// Прогон одного сценария через новый движок — так, как это делает интерфейс.
function runEngine(sc){
  const st = structuredClone(sc.state);
  let units = st.units;
  const ctx = {
    rules: E.getRules("base"),
    rng: E.mulberry32(sc.seed),
    commanderOf: u => st.commanders.find(c => c.id === u.commanderId) || null,
    factionName: id => (st.factions.find(f => f.id === id) || {}).name || "Без фракции",
  };
  const apply = (id, patch) => { units = units.map(u => u.id === id ? Object.assign({}, u, patch) : u); };
  const a = sc.action;
  let entry = null, turn = st.turn;

  if(a.kind === "battle"){
    const A = units.find(u => u.id === a.att), B = units.find(u => u.id === a.def);
    const r = E.resolveBattle(A, B, a.req, ctx);
    r.patches.forEach(p => apply(p.id, p.patch));
    entry = {title: r.title, lines: r.lines, tone: r.tone};
  } else if(a.kind === "morale" || a.kind === "flee"){
    const u = units.find(x => x.id === a.id);
    const r = a.kind === "morale" ? E.moraleCheck(u, ctx) : E.fleeCheck(u, ctx);
    apply(u.id, r.patch);
    entry = {title: r.title, lines: r.lines, tone: r.tone};
  } else {
    const r = E.endTurn(units, ctx);
    units = r.units; turn += 1;
    entry = {title: `— Конец хода ${turn - 1} —`, lines: r.lines.length ? r.lines : ["Без изменений"], tone: "info"};
  }
  return {entry, units, turn};
}

test("эталон содержит все сценарии", () => {
  assert.equal(golden.length, scenarios.length);
});

// Движок на C# (core/) читает замороженные сценарии — генератор не должен тихо от них уйти
test("замороженные сценарии в shared/ совпадают с генератором", () => {
  const frozen = JSON.parse(fs.readFileSync(new URL("../../shared/golden/scenarios.json", import.meta.url), "utf8"));
  assert.deepEqual(frozen, JSON.parse(JSON.stringify(scenarios)));
});

for(const sc of scenarios){
  const g = golden[sc.n];
  test(`сценарий ${sc.n}: ${sc.action.kind}`, () => {
    const r = runEngine(sc);
    assert.equal(r.entry.title, g.log.title, "заголовок записи журнала");
    assert.deepEqual(r.entry.lines, g.log.lines, "строки журнала");
    assert.equal(r.entry.tone, g.log.tone, "тон записи");
    assert.equal(r.turn, g.turn, "номер хода");
    // v29 дописывает при загрузке поля по умолчанию — сравниваем только поля движка
    for(const gu of g.units){
      const eu = r.units.find(u => u.id === gu.id);
      for(const k of Object.keys(eu)) assert.deepEqual(eu[k], gu[k], `отряд ${gu.id}, поле ${k}`);
    }
  });
}

test("сидированный генератор воспроизводим", () => {
  const a = E.mulberry32(42), b = E.mulberry32(42);
  for(let i = 0; i < 100; i++) assert.equal(a(), b());
});

test("набор правил 1 — значение по умолчанию", () => {
  assert.equal(E.getRules("нет такого").id, "base");
  assert.equal(E.getRules("base").rollFloorPerDisc, 5);
});
