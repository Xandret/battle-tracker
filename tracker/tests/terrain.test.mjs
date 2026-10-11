// ═══════════ terrain.test.mjs — местность карты: клетки, высота, правка, сохранение ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const F = E.TERRAIN_BY_KEY;
const count = (m, layer, v) => (layer === "z" ? m.z : m.t).reduce((a, x) => a + (x === v ? 1 : 0), 0);

test("карта в клетках по 5 м (К13)", () => {
  const m = E.createTerrain(2000, 1500, F.field.id);
  assert.equal(m.cell, 5);
  assert.equal(m.w, 400);
  assert.equal(m.h, 300);
  assert.equal(E.mapWidthM(m), 2000);
  assert.equal(count(m, "t", F.field.id), 400 * 300);
  assert.equal(count(m, "z", 0), 400 * 300, "высота по умолчанию — 0");
  assert.throws(() => E.createTerrain(20000, 20000), /слишком велика/);
});

test("коды местности не меняются — они лежат в сохранениях", () => {
  assert.equal(F.field.id, 1);
  assert.equal(F.forest.id, 6);
  assert.equal(F.wall.id, 12);
  assert.equal(F.breach.id, 20);
  assert.equal(new Set(E.TERRAIN.map(t => t.id)).size, E.TERRAIN.length);
  assert.equal(E.terrainName(0), "не задано");
});

test("кисть: круг нужного радиуса", () => {
  const m = E.createTerrain(200, 200);
  const n = E.paintDisc(m, "t", 20, 20, 4, F.forest.id);
  assert.ok(n > 40 && n < 60, "π·4² ≈ 50 клеток: " + n);
  assert.equal(E.cellAt(m, 20 / m.w, 20 / m.h).t, F.forest.id);
  assert.equal(E.cellAt(m, 30 / m.w, 20 / m.h).t, 0, "за радиусом не закрашено");
  assert.equal(E.paintDisc(m, "t", 20, 20, 4, F.forest.id), 0, "повторный мазок ничего не меняет");
});

test("высота: слой отдельно от местности, уровни 0–3 (К16)", () => {
  const m = E.createTerrain(200, 200, F.field.id);
  E.paintDisc(m, "t", 10, 10, 3, F.forest.id);
  E.paintDisc(m, "z", 10, 10, 5, 7);
  const c = E.cellAt(m, 10.5 / m.w, 10.5 / m.h);
  assert.equal(c.t, F.forest.id, "лес на холме остаётся лесом");
  assert.equal(c.z, E.MAX_HEIGHT, "выше 3 не бывает");
});

test("линия с толщиной: стена толщиной в одну клетку", () => {
  const m = E.createTerrain(200, 200, F.field.id);
  const n = E.paintSegment(m, "t", 5.5, 10.5, 25.5, 10.5, 0.5, F.wall.id);
  assert.equal(n, 21, "от 5 до 25 включительно");
  assert.equal(count(m, "t", F.wall.id), 21);
  const road = E.paintSegment(m, "t", 5, 20, 25, 30, 1, F.road.id);
  assert.ok(road > 40, "наклонная дорога в две клетки: " + road);
});

test("прямоугольник: заливка и контур (стены замка)", () => {
  const m = E.createTerrain(200, 200, F.field.id);
  assert.equal(E.paintRect(m, "t", 2, 2, 11, 11, F.building.id), 100);
  const m2 = E.createTerrain(200, 200, F.field.id);
  assert.equal(E.paintRect(m2, "t", 2, 2, 11, 11, F.wall.id, 1), 36, "контур 10×10 толщиной 1 — 36 клеток");
  assert.equal(E.cellAt(m2, 6 / m2.w, 6 / m2.h).t, F.field.id, "двор внутри не тронут");
});

test("заливка останавливается на границе", () => {
  const m = E.createTerrain(100, 100, F.field.id);   // 20 × 20
  E.paintRect(m, "t", 0, 10, 19, 10, F.water.id);   // река поперёк карты
  const n = E.floodFill(m, "t", 5, 5, F.sand.id);
  assert.equal(n, 20 * 10, "только верхний берег");
  assert.equal(E.cellAt(m, 5 / 20, 15 / 20).t, F.field.id, "за рекой не залило");
  assert.equal(E.floodFill(m, "t", 5, 5, F.sand.id), 0);
});

test("сохранение: сжатие повторов и обратно без потерь", () => {
  const m = E.createTerrain(4000, 3000, F.field.id);   // 800 × 600
  E.paintDisc(m, "t", 200, 200, 60, F.forest.id);
  E.paintSegment(m, "t", 0, 300, 799, 350, 6, F.water.id);
  E.paintDisc(m, "z", 600, 150, 40, 2);
  m.meta.name = "проба";
  const s = E.serializeTerrain(m);
  const json = JSON.stringify(s);
  assert.ok(json.length < 60000, "480 000 клеток ужались до " + json.length + " символов");
  const back = E.deserializeTerrain(JSON.parse(json));
  assert.equal(back.w, 800);
  assert.deepEqual(Array.from(back.t), Array.from(m.t));
  assert.deepEqual(Array.from(back.z), Array.from(m.z));
  assert.equal(back.meta.name, "проба");
});

test("битая карта не ломает загрузку партии", () => {
  assert.equal(E.deserializeTerrain(null), null);
  assert.equal(E.deserializeTerrain({w: 10, h: 10, t: "1.5", z: ""}), null, "клеток меньше, чем надо");
  assert.equal(E.deserializeTerrain({w: 1e6, h: 1e6}), null, "слишком большая");
  const ok = E.deserializeTerrain({w: 4, h: 4, t: "1.g", z: ""});
  assert.equal(ok.t.length, 16);
  assert.equal(E.hasTerrain(ok), true);
  assert.equal(E.hasTerrain(E.createTerrain(100, 100)), false, "пустой слой — «не задано»");
});

test("клетка под фишкой по долям карты", () => {
  const m = E.createTerrain(100, 50);   // 20 × 10
  E.paintRect(m, "t", 10, 0, 19, 9, F.forest.id);
  assert.equal(E.cellAt(m, 0.75, 0.5).t, F.forest.id);
  assert.equal(E.cellAt(m, 0.25, 0.5).t, 0);
  assert.deepEqual([E.cellAt(m, 1, 1).x, E.cellAt(m, 1, 1).y], [19, 9], "правый нижний край — последняя клетка");
});
