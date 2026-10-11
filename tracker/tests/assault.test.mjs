// ═══════════ assault.test.mjs — приступ на стену: лестницы, башня, пролом (6б, Г48, Г50, Ш10–Ш14) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const R = E.getRules("base"), Q = R.siege.assault;
const seq = (...v) => { let i = 0; return () => v[i++ % v.length]; };
const ctxOf = rng => ({rules: R, rng, commanderOf: () => null, factionName: () => ""});
// Замок (зерно 1, 120 × 100 клеток): участок №17 — южная стена, клетки y = 58
function scene(){
  const map = E.generateMap("castle", {}, 1);
  E.buildSections(map, R);
  const geo = {map, W: E.mapWidthM(map), H: E.mapHeightM(map)};
  const wall = map.forts.find(f => f.kind === "wall" && f.cy > 55);
  const at = (cx, cy) => ({mapX: cx * 5 / geo.W * 100, mapY: cy * 5 / geo.H * 100});
  const base = {discipline: 50, morale: 70, eqAtk: 60, eqDef: 60, exp: 20, mastery: 10, fatigue: 0, status: "active", onMap: true,
    attacksMade: 0, countersMade: 0, totKilled: 0, totWounded: 0, broken: false, breakGrace: 0, breakPenalty: 0, weapon: "melee", type: "infantry"};
  const D = Object.assign({}, base, {id: 2, name: "Гарнизон", soldiers: 300, factionId: 2, facing: 180}, at(wall.cx, wall.cy));
  const A = Object.assign({}, base, {id: 1, name: "Штурмовые", soldiers: 1000, factionId: 1, facing: 0, ladders: 20}, at(wall.cx, wall.cy + 3.2));
  return {map, geo, wall, at, base, A, D};
}
const go = (s, A, defs, opts, rng = seq(0.5)) =>
  E.assaultWall(A, s.map, s.wall.id, defs, Object.assign({gap: E.sectionGap(A, s.wall.id, s.geo, R), ladders: A.ladders || 0, towers: 0, engaged: [], fatigueMode: "percent"}, opts), ctxOf(rng));

test("кто у стены: дистанция до участка, защитники — чужие ближе 5 м, своя башня у стены и у строя", () => {
  const s = scene();
  assert.equal(E.sectionGap(s.D, s.wall.id, s.geo, R), 0, "гарнизон стоит на стене");
  assert.ok(Math.abs(E.sectionGap(s.A, s.wall.id, s.geo, R) - 9.5) < 1e-9);
  const ally = Object.assign({}, s.D, {id: 3, factionId: 1});
  assert.deepEqual(E.defendersOf(s.wall.id, [s.A, s.D, ally], s.A, s.geo, R).map(d => d.unit.id), [2], "свои — не защитники");
  const far = Object.assign({}, s.D, {id: 4}, s.at(s.wall.cx, s.wall.cy - 6));
  assert.deepEqual(E.defendersOf(s.wall.id, [far], s.A, s.geo, R), [], "дальше 5 м от стены — не защищает");
  const tw = E.makeMachine("tower", 1, R, Object.assign({onMap: true, factionId: 1}, s.at(s.wall.cx + 2, s.wall.cy + 2)));
  assert.equal(E.towersAt(s.wall.id, [tw], s.A, s.geo, R), 1);
  assert.equal(E.towersAt(s.wall.id, [Object.assign({}, tw, {factionId: 2})], s.A, s.geo, R), 0, "чужая башня не в счёт");
  assert.equal(E.towersAt(s.wall.id, [Object.assign({}, tw, s.at(s.wall.cx, s.wall.cy + 12))], s.A, s.geo, R), 0, "башня далеко от стены");
  assert.equal(E.laddersFor(s.A, R), 20, "1000 человек — 20 лестниц");
});

test("по лестницам: в бой — лестниц × 10, высота у защитника, минимум броска — не больше бойцов в деле (Г48)", () => {
  const s = scene();
  const r = go(s, s.A, [{unit: s.D, gap: 0}], {via: "ladders"});
  assert.equal(r.ok, true);
  assert.ok(r.lines[0].includes("в бой вступают 200 из 1000"));
  assert.ok(r.lines.some(l => l.startsWith("🪜 Снизу вверх по лестницам: −10%")));
  assert.ok(r.lines.some(l => l.startsWith("🏰 Со стены сверху вниз: +20%")));
  assert.ok(r.lines.some(l => l.includes("не больше бойцов в деле — 200")));
  const pa = r.patches.find(p => p.id === 1).patch, pd = r.patches.find(p => p.id === 2).patch;
  assert.equal(pa.attacksMade, 1); assert.equal(pd.countersMade, 1);
  assert.equal(r.ladders, 20, "d100 = 51 > 15 — ни одной не сбросили");
  // все лестницы сбросили: d100 = 1
  // броски: атака, летальность, ответ, летальность — потом 20 лестниц по d100 = 1
  const rr = go(s, s.A, [{unit: s.D, gap: 0}], {via: "ladders"}, seq(0.5, 0.5, 0.5, 0.5, ...new Array(20).fill(0)));
  assert.equal(rr.ladders, 0, "все 20 лестниц сброшены");
  assert.ok(rr.lines.some(l => l === `Со сброшенных лестниц упали ${20 * Q.fallMen} — выбыли ранеными`));
});

test("через осадную башню — вместимость башни, без высоты; в пролом — весь отряд", () => {
  const s = scene();
  const r = go(s, s.A, [{unit: s.D, gap: 0}], {via: "tower", towers: 1, gap: 99});
  assert.ok(r.lines[0].includes("через осадную башню (1 шт. × 40 бойцов): в бой вступают 40 из 1000"));
  assert.ok(!r.lines.some(l => l.includes("Снизу вверх")), "башня вровень со стеной");
  assert.equal(r.ladders, null);
  E.damageSection(s.map, s.wall.id, 100, null, R);   // пролом 10 м
  const b = go(s, s.A, [{unit: s.D, gap: 0}], {via: "breach"});
  assert.ok(b.lines[0].includes("в пролом (10 м): в бой вступают 1000 из 1000"));
});

test("ограничение фронта (Г50): на участок — 2 отряда за ход, в пролом — 1 на 10 м", () => {
  const s = scene();
  assert.equal(go(s, s.A, [], {via: "ladders", engaged: [7]}).ok, true);
  const f = go(s, s.A, [{unit: s.D, gap: 0}], {via: "ladders", engaged: [7, 8]});
  assert.equal(f.ok, false);
  assert.match(f.lines[0], /уже бились 2 отр\./);
  E.damageSection(s.map, s.wall.id, 100, null, R);
  assert.equal(go(s, s.A, [], {via: "breach", engaged: [7]}).ok, false, "пролом 10 м — один отряд");
  E.damageSection(s.map, s.wall.id, 100, null, R);
  assert.equal(go(s, s.A, [], {via: "breach", engaged: [7]}).ok, true, "два пролома — два отряда");
});

test("отказы: конница не лезет по лестницам, нет лестниц, далеко, атаки кончились, нет пролома", () => {
  const s = scene();
  const cav = Object.assign({}, s.A, {type: "cavalry"});
  assert.match(go(s, cav, [], {via: "ladders"}).lines[0], /конница/);
  assert.match(go(s, Object.assign({}, s.A, {ladders: 0}), [], {via: "ladders"}).lines[0], /нет лестниц/);
  assert.match(go(s, s.A, [], {via: "ladders", gap: 30}).lines[0], /далеко от стены: 30 м/);
  assert.match(go(s, Object.assign({}, s.A, {attacksMade: 1}), [], {via: "ladders"}).lines[0], /израсходовал атаки/);
  assert.match(go(s, s.A, [], {via: "breach"}).lines[0], /нет пролома/);
  assert.match(go(s, s.A, [], {via: "tower", towers: 0}).lines[0], /нет своей осадной башни/);
});

test("защитников нет — участок занят; занятый участок переживает сохранение и правку карты", () => {
  const s = scene();
  const r = go(s, s.A, [], {via: "ladders"});
  assert.equal(r.captured, true);
  assert.equal(E.getSection(s.map, s.wall.id).holder, 1);
  const back = E.deserializeTerrain(JSON.parse(JSON.stringify(E.serializeTerrain(s.map))));
  assert.equal(E.getSection(back, s.wall.id).holder, 1);
  assert.equal(E.getSection(back, s.wall.id - 1).holder, undefined, "незанятые — без поля");
  E.buildSections(back, R, {s: back.s, forts: back.forts});
  assert.equal(E.getSection(back, s.wall.id).holder, 1, "пересборка помнит, чей участок");
});

test("дрогнувший защитник не отвечает — и лестниц не сбрасывает", () => {
  const s = scene();
  const r = go(s, s.A, [{unit: Object.assign({}, s.D, {morale: 10}), gap: 0}], {via: "ladders"}, seq(0));
  assert.ok(r.lines.some(l => l.includes("дрогнул")));
  assert.equal(r.ladders, 20);
  assert.equal(r.patches.find(p => p.id === 2).patch.countersMade, undefined);
});
