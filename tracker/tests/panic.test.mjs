// ═══════════ panic.test.mjs — каскадная паника (черновик 6а, К25) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const R = E.getRules("base");
const K = k => E.TERRAIN_BY_KEY[k].id;
let nextId = 1;
const unit = (o = {}) => Object.assign({id: nextId++, name: "Отряд", type: "infantry", weapon: "melee", soldiers: 400, initial: 400,
  discipline: 50, morale: 60, eqAtk: 40, eqDef: 40, exp: 20, mastery: 0, fatigue: 0, status: "active",
  factionId: 1, acted: false, attacksMade: 0, countersMade: 0, turnsActive: 0, fleeChecks: 0, breakGrace: 0,
  broken: false, onMap: true, mapX: 50, mapY: 50, facing: 0}, o);
const ctx = seed => ({rules: R, rng: E.mulberry32(seed)});
const open = {map: null, W: 1000, H: 1000};

test("видимость: лес пропускает взгляд на 100 м, холм и стена закрывают", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  const geo = {map, W: 1000, H: 1000};
  const a = unit({mapX: 20, mapY: 50}), b = unit({mapX: 50, mapY: 50});   // 300 м между центрами
  assert.equal(E.lineOfSight(a, b, geo, R).ok, true);
  E.paintRect(map, "t", 70, 0, 89, 199, K("forest"));                   // 100 м леса
  assert.equal(E.lineOfSight(a, b, geo, R).ok, true, "ровно 100 м леса — ещё видно");
  E.paintRect(map, "t", 70, 0, 95, 199, K("forest"));                   // 130 м леса
  assert.deepEqual(E.lineOfSight(a, b, geo, R), {ok: false, why: "лес"});
  const m2 = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(m2, "z", 70, 0, 75, 199, 1);
  assert.deepEqual(E.lineOfSight(a, b, {map: m2, W: 1000, H: 1000}, R), {ok: false, why: "холм"}, "холм между двумя в низине");
  const m3 = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(m3, "t", 70, 0, 70, 199, K("wall"));
  assert.equal(E.lineOfSight(a, b, {map: m3, W: 1000, H: 1000}, R).why, "стена");
});

test("волна: свои в радиусе 150 м проверяются, дальние и чужие — нет", () => {
  const src = unit({name: "Бегущие", status: "fled", mapX: 50});
  const near = unit({name: "Рядом", mapX: 50, mapY: 60, discipline: 100});     // 100 м к югу, край ~96 м
  const far = unit({name: "Далеко", mapX: 50, mapY: 80, discipline: 100});     // 300 м
  const foe = unit({name: "Враг", factionId: 2, mapX: 55, mapY: 50, discipline: 1});
  const r = E.panicWave([src, near, far, foe], src.id, open, ctx(1));
  assert.ok(r.lines.some(l => l.startsWith("«Рядом»") && l.includes("держится")), "дисциплина 100 — держится всегда");
  assert.ok(!r.lines.some(l => l.includes("Далеко")), "за радиусом не проверяется");
  assert.ok(!r.lines.some(l => l.includes("Враг")), "бегство врага паники не вызывает");
  assert.deepEqual(r.fled, []);
});

test("каскад по цепочке: побежавший запускает проверку у своих соседей", () => {
  // цепочка через каждые 120 м; дисциплина 1 — почти наверняка бегут
  const chain = [0, 1, 2, 3, 4].map(i => unit({name: "Звено " + i, mapX: 10 + i * 12, mapY: 50, discipline: 1, status: i ? "active" : "fled"}));
  const r = E.panicWave(chain, chain[0].id, open, ctx(3));
  assert.equal(r.fled.length, 4, "вся цепочка рухнула");
  assert.ok(r.lines.some(l => l.includes("волна 4")), "четвёртое кольцо");
  assert.equal(new Set(r.patches.map(p => p.id)).size, r.patches.length, "каждый отряд — раз за волну");
  assert.ok(r.patches.every(p => p.patch.status === "fled"));
});

test("первое кольцо — только по видимости; дальние кольца — без неё (К25)", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 112, 199, 114, K("wall"));            // стена поперёк карты на 560–575 м
  const geo = {map, W: 1000, H: 1000};
  const src = unit({name: "Источник", status: "fled", mapX: 50, mapY: 50});      // 500 × 500 м
  const side = unit({name: "Сбоку", mapX: 62, mapY: 50, discipline: 1});         // 120 м к востоку — видит
  const hidden = unit({name: "За стеной", mapX: 62, mapY: 62, discipline: 100});  // за стеной от источника
  const r = E.panicWave([src, side, hidden], src.id, geo, ctx(2));
  assert.ok(r.lines.some(l => l.startsWith("«За стеной»") && l.includes("не видел бегства — мешает стена")), "первое кольцо: не видел");
  assert.ok(r.fled.includes(side.id), "сбоку видел и побежал");
  assert.ok(r.lines.some(l => l.startsWith("«За стеной» (112 м от «Сбоку», волна 2)") && l.includes("держится")),
    "второе кольцо проверило того, кто не видел: " + r.lines.join(" | "));
});

test("переключатель «−100 БД вместе с проверкой»", () => {
  const src = unit({status: "fled"});
  const n = unit({name: "Сосед", mapY: 55, morale: 80, discipline: 100});
  const r = E.panicWave([src, n], src.id, open, ctx(1), {moraleLoss: true});
  const p = r.patches.find(x => x.id === n.id).patch;
  assert.equal(p.morale, 0);
  assert.equal(p.broken, true, "БД на нуле — слом");
  assert.ok(r.lines.some(l => l.includes("БД «Сосед» −100: 80 → 0")));
  const plain = E.panicWave([src, n], src.id, open, ctx(1));
  assert.equal(plain.patches.length, 0, "без переключателя БД не трогаем");
});

test("одно зерно — одна волна", () => {
  const mk = () => [0, 1, 2, 3].map(i => unit({id: 100 + i, name: "О" + i, mapX: 20 + i * 10, discipline: 50, status: i ? "active" : "fled"}));
  const a = E.panicWave(mk(), 100, open, ctx(77)), b = E.panicWave(mk(), 100, open, ctx(77));
  assert.deepEqual(a, b);
});
