// ═══════════ mapgen.test.mjs — генераторы карт ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const T = k => E.TERRAIN_BY_KEY[k].id;
const count = (m, id) => m.t.reduce((a, v) => a + (v === id ? 1 : 0), 0);
const share = (m, id) => count(m, id) / (m.w * m.h);
const gen = (id, p = {}, seed = 7) => E.generateMap(id, p, seed);

// Можно ли дойти из клетки до края карты, если непроходимы blocked (соседи по 8 направлениям — строже, чем по 4)
function reachesEdge(m, sx, sy, blocked){
  const seen = new Uint8Array(m.w * m.h), stack = [sy * m.w + sx];
  seen[stack[0]] = 1;
  while(stack.length){
    const i = stack.pop(), x = i % m.w, y = (i - x) / m.w;
    if(x === 0 || y === 0 || x === m.w - 1 || y === m.h - 1) return true;
    for(let dy = -1; dy <= 1; dy++) for(let dx = -1; dx <= 1; dx++){
      const j = (y + dy) * m.w + (x + dx);
      if(!seen[j] && !blocked.includes(m.t[j])){ seen[j] = 1; stack.push(j); }
    }
  }
  return false;
}

test("все шаблоны первой очереди на месте (К9)", () => {
  assert.deepEqual(E.MAP_TEMPLATES.map(t => t.id), ["field", "forest", "hills", "river", "desert", "palisade", "castle", "concentric"]);
  for(const t of E.MAP_TEMPLATES){
    const m = gen(t.id);
    assert.equal(m.meta.template, t.id);
    assert.equal(m.cell, 5);
  }
});

test("одно зерно — одна карта, новое зерно — «ещё вариант» (К8)", () => {
  for(const id of ["field", "river", "castle"]){
    const a = gen(id, {}, 42), b = gen(id, {}, 42), c = gen(id, {}, 43);
    assert.deepEqual(Array.from(a.t), Array.from(b.t), id + ": воспроизводимо");
    assert.deepEqual(Array.from(a.z), Array.from(b.z));
    assert.notDeepEqual(Array.from(a.t), Array.from(c.t), id + ": другое зерно — другая карта");
  }
  assert.equal(gen("field", {}, 42).meta.seed, 42, "зерно хранится в карте");
});

test("размер в метрах и пределы настроек", () => {
  const m = gen("field", {widthM: 1000, depthM: 600});
  assert.deepEqual([m.w, m.h], [200, 120]);
  const p = E.mapParams("river", {fords: 99, width: "огромная"});
  assert.equal(p.fords, 3, "не больше предела");
  assert.equal(p.width, "mid", "неизвестный вариант — по умолчанию");
});

test("Поле: дорога, рощи, холмы по настройкам", () => {
  const bare = gen("field", {groves: "none", hills: 0, road: false});
  assert.equal(share(bare, T("field")), 1, "без рощ, холмов и дороги — чистое поле");
  assert.ok(bare.z.every(v => v === 0));
  const full = gen("field", {groves: "often", hills: 3, road: true});
  assert.ok(count(full, T("road")) > 300, "дорога через всё поле");
  assert.ok(count(full, T("forest")) > 500, "рощи");
  assert.ok(full.z.some(v => v > 0) && full.z.every(v => v <= 2), "холмы до 2 уровня");
});

test("Лес: густота меняет долю леса, поляны и дорога есть", () => {
  const low = gen("forest", {density: "low", clearings: 0, road: false});
  const high = gen("forest", {density: "high", clearings: 0, road: false});
  assert.ok(share(high, T("forest")) > share(low, T("forest")) + 0.2,
    `густой ${share(high, T("forest")).toFixed(2)} против редкого ${share(low, T("forest")).toFixed(2)}`);
  assert.ok(count(high, T("shrub")) > 0, "опушка — кустарник");
  assert.ok(count(gen("forest", {road: true}), T("road")) > 300);
});

test("Холмы: наибольшая высота и лес на склонах (К16)", () => {
  const m = gen("hills", {count: 4, maxHeight: 3, slopeForest: true});
  assert.equal(Math.max(...m.z), 3);
  const onSlopes = m.t.filter((v, i) => v === T("forest") && m.z[i] >= 1).length;
  assert.ok(onSlopes > 100, "лес растёт на склонах: " + onSlopes);
  const flat = gen("hills", {count: 2, maxHeight: 1, slopeForest: false});
  assert.equal(Math.max(...flat.z), 1);
});

test("Река: течёт через всю карту, броды и мосты", () => {
  const m = gen("river", {direction: "across", fords: 2, bridges: 1, width: "mid"});
  // вода (или брод, мост) есть в каждом столбце — река не обрывается
  const river = [T("water"), T("ford"), T("bridge")];
  for(let x = 0; x < m.w; x++){
    let has = false;
    for(let y = 0; y < m.h && !has; y++) has = river.includes(m.t[y * m.w + x]);
    assert.ok(has, "столбец " + x + " без реки");
  }
  assert.ok(count(m, T("ford")) > 10, "броды");
  assert.ok(count(m, T("bridge")) > 5, "мост");
  assert.ok(count(m, T("road")) > 100, "к мосту ведёт дорога");
  const none = gen("river", {fords: 0, bridges: 0});
  assert.equal(count(none, T("ford")) + count(none, T("bridge")), 0);
  const wide = gen("river", {width: "wide", fords: 0, bridges: 0}), narrow = gen("river", {width: "narrow", fords: 0, bridges: 0});
  assert.ok(count(wide, T("water")) > count(narrow, T("water")) * 2.5, "широкая река шире узкой");
});

test("Пустыня: песок, барханы высотой 1, оазис с водой (К17, К27)", () => {
  const m = gen("desert", {dunes: true, oasis: true, rocks: true});
  assert.ok(share(m, T("sand")) > 0.7);
  assert.equal(Math.max(...m.z), 1, "барханы — уровень 1");
  assert.ok(count(m, T("water")) > 10 && count(m, T("forest")) > 10, "оазис: вода и пальмы");
  assert.ok(count(m, T("rocks")) > 5);
  const flat = gen("desert", {dunes: false, oasis: false, rocks: false});
  assert.equal(share(flat, T("sand")), 1);
});

// Крепость замкнута: при закрытых воротах из двора наружу не выйти, при открытых — выйти
function checkEnclosed(m, walls, name){
  const cx = Math.floor(m.w / 2), cy = Math.floor(m.h / 2) + 3;
  assert.equal(reachesEdge(m, cx, cy, [...walls, T("gate")]), false, name + ": стены замкнуты");
  assert.equal(reachesEdge(m, cx, cy, walls), true, name + ": через ворота — наружу");
}

test("Частокол: овал около 150 м, вышки, ворота, ров (К28)", () => {
  const m = gen("palisade", {moat: true, gates: 2, gateSide: "south"});
  assert.ok(count(m, T("palisade")) > 60);
  assert.ok(count(m, T("tower")) >= 16, "вышки 2 × 2");
  assert.ok(count(m, T("moat")) > 100);
  assert.ok(count(m, T("bridge")) > 0, "через ров к воротам — мост");
  checkEnclosed(m, [T("palisade"), T("tower"), T("building")], "частокол");
  for(let seed = 1; seed <= 5; seed++) checkEnclosed(gen("palisade", {gates: 1}, seed), [T("palisade"), T("tower"), T("building")], "частокол, зерно " + seed);
});

test("Каменный замок: 120 × 90 м, угловые и надвратная башни, донжон (К28)", () => {
  const m = gen("castle", {moat: true, gates: 1, gateSide: "north", hill: true});
  assert.ok(count(m, T("wall")) > 50);
  assert.ok(count(m, T("tower")) >= 4 * 9 + 2 * 9 - 10, "4 угловые + 2 надвратные башни 3 × 3");
  assert.ok(count(m, T("building")) >= 25, "донжон 5 × 5");
  assert.ok(Math.max(...m.z) >= 2, "на холме");
  // главные ворота — на севере: проём в верхней половине
  const gates = [];
  m.t.forEach((v, i) => { if(v === T("gate")) gates.push(Math.floor(i / m.w)); });
  assert.ok(gates.length && gates.every(y => y < m.h / 2), "ворота на севере");
  checkEnclosed(m, [T("wall"), T("tower"), T("building")], "замок");
  for(const side of ["south", "west", "east"]) checkEnclosed(gen("castle", {gateSide: side, gates: 3}), [T("wall"), T("tower"), T("building")], "замок, " + side);
});

test("Концентрический замок: два кольца, полоса между ними, ворота не на одной линии (К28)", () => {
  const m = gen("concentric", {moat: true, gates: 1, gateSide: "south"});
  const walls = [T("wall"), T("tower"), T("building")];
  checkEnclosed(m, walls, "внешнее кольцо");
  // внутренний двор отдельно замкнут: из центра при закрытых воротах не выйти даже в полосу
  const cx = Math.floor(m.w / 2), cy = Math.floor(m.h / 2) + 4;
  // полоса между кольцами с юга: внутренняя стена ~ +8.5 клеток от центра, внешняя ~ +12.5
  const gapY = Math.floor(m.h / 2) + 10;
  let open = 0;
  for(let x = cx - 14; x <= cx + 14; x++) if(m.t[gapY * m.w + x] === T("field")) open++;
  assert.ok(open >= 10, "полоса между кольцами — открытая земля: " + open + " клеток в строке");
  // ворота: внешние и внутренние со смещением
  const gx = [];
  m.t.forEach((v, i) => { if(v === T("gate")) gx.push(i % m.w); });
  assert.ok(Math.max(...gx) - Math.min(...gx) >= 8, "внешние и внутренние ворота разнесены");
  // внутреннее кольцо выше внешнего
  assert.ok(m.z[Math.floor(m.h / 2) * m.w + cx] > m.z[gapY * m.w + cx - 12], "двор выше полосы");
  assert.ok(reachesEdge(m, cx, cy, [...walls, T("gate")]) === false, "внутренний двор закрыт");
});

test("карта генератора сохраняется и открывается без потерь", () => {
  const m = gen("concentric", {}, 99);
  const back = E.deserializeTerrain(JSON.parse(JSON.stringify(E.serializeTerrain(m))));
  assert.deepEqual(Array.from(back.t), Array.from(m.t));
  assert.equal(back.meta.seed, 99);
  assert.equal(back.meta.template, "concentric");
});
