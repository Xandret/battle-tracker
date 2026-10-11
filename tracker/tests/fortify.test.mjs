// ═══════════ fortify.test.mjs — участки стен с прочностью (6б, Г46, Ш1–Ш2) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const R = E.getRules("base");
const T = k => E.TERRAIN_BY_KEY[k].id;
const FORT = [T("wall"), T("palisade"), T("gate"), T("tower")];
const fortified = id => { const m = E.generateMap(id, {}, 3); E.buildSections(m, R); return m; };
const kinds = m => m.forts.reduce((a, f) => (a[f.kind] = (a[f.kind] || 0) + 1, a), {});
// Нарисованная вручную стена: 40 × 30 клеток, линия от (5, 10) до (25, 10) толщиной th клеток
function drawnWall(th = 1){
  const m = E.createTerrain(200, 150, T("field"));
  E.paintRect(m, "t", 5, 10, 24, 10 + th - 1, T("wall"));
  E.buildSections(m, R);
  return m;
}

test("каждая клетка стены, частокола, ворот и башни — в участке; виды по шаблону крепости", () => {
  for(const id of ["palisade", "castle", "concentric"]){
    const m = fortified(id);
    for(let i = 0; i < m.t.length; i++) if(FORT.includes(m.t[i])) assert.ok(m.s[i] > 0, `${id}: клетка ${i} без участка`);
    for(let i = 0; i < m.t.length; i++) if(m.s[i]) assert.ok(FORT.includes(m.t[i]), `${id}: участок на чужой клетке`);
    assert.deepEqual(m.forts.map(f => f.id), m.forts.map((_, k) => k + 1), "номера по порядку");
  }
  assert.deepEqual(kinds(fortified("palisade")), {tower: 4, palisade: 16, gateWood: 1});
  assert.deepEqual(kinds(fortified("castle")), {tower: 6, wall: 14, gateIron: 1}, "ворота замка — окованные: стена за надвратными башнями");
  assert.equal(kinds(fortified("concentric")).gateIron, 2, "концентрический: внешние и внутренние ворота");
});

test("стена режется на участки не длиннее 25 м (Ш1), башня и ворота — целиком", () => {
  const m = fortified("castle");
  for(const f of m.forts.filter(f => f.kind === "wall")) assert.ok(f.len * m.cell <= R.siege.sectionM, `участок №${f.id}: ${f.len * m.cell} м`);
  for(const f of m.forts.filter(f => f.kind === "tower")) assert.equal(f.n, 9, "угловая башня — 3 × 3 клетки");
  // стена 100 м нарисована рукой — четыре участка по 25 м
  const w = drawnWall(1);
  assert.deepEqual(w.forts.map(f => [f.kind, f.n, f.len]), [["wall", 5, 5], ["wall", 5, 5], ["wall", 5, 5], ["wall", 5, 5]]);
  // толстая стена (3 клетки) — те же четыре участка, только толще
  const thick = drawnWall(3);
  assert.equal(thick.forts.length, 4);
  for(const f of thick.forts) assert.equal(Math.round(f.n / f.len), 3, "толщина 3 клетки");
});

test("одна карта — одни участки (воспроизводимо)", () => {
  const a = fortified("concentric"), b = fortified("concentric");
  assert.deepEqual(Array.from(a.s), Array.from(b.s));
  assert.deepEqual(a.forts, b.forts);
});

test("удар снимает прочность; обнуление — пролом 10 м у точки попадания, остаток идёт дальше (Ш2)", () => {
  const m = drawnWall(1), f = m.forts[1];   // клетки x = 10…14, y = 10
  let r = E.damageSection(m, f.id, 60, {x: 12.5, y: 10.5}, R);
  assert.equal(r.opened, 0);
  assert.equal(E.sectionHp(f, R), 40);
  r = E.damageSection(m, f.id, 60, {x: 10.2, y: 10.5}, R);
  assert.equal(r.opened, 1);
  assert.deepEqual(r.cells.map(i => i % m.w), [10, 11], "10 м у точки попадания");
  for(const i of r.cells) assert.equal(m.t[i], T("breach"));
  assert.equal(E.sectionHp(f, R), 80, "120 − 100 = 20 урона в остатке");
  assert.equal(f.breaches, 1);
  assert.ok(r.lines.some(l => l.includes("Пролом")));
  // 250 за раз — два пролома, и от участка остаётся одна клетка
  r = E.damageSection(m, f.id, 250, null, R);
  assert.equal(r.opened, 2);
  assert.equal(r.destroyed, true, "в участке 5 клеток: 2 + 2 + последняя");
  assert.equal(f.up, 0);
  assert.equal(E.sectionHp(f, R), 0);
  assert.match(E.damageSection(m, f.id, 10, null, R).lines[0], /уже разрушен/);
});

test("толстая стена: пролом на всю толщину", () => {
  const m = drawnWall(3), f = m.forts[0];
  const r = E.damageSection(m, f.id, 100, {x: 7.5, y: 11.5}, R);
  assert.equal(r.cells.length, 6, "2 клетки вдоль × 3 поперёк");
  assert.ok(r.lines.some(l => l.includes("10 м")));
});

test("ворота выбиваются, башня рушится целиком — проход открыт", () => {
  const m = fortified("castle");
  const gate = m.forts.find(f => f.kind === "gateIron"), tower = m.forts.find(f => f.kind === "tower");
  let r = E.damageSection(m, gate.id, 79, null, R);
  assert.equal(r.opened, 0, "окованные ворота держат 80");
  r = E.damageSection(m, gate.id, 1, null, R);
  assert.equal(r.cells.length, gate.n);
  assert.ok(r.lines.some(l => l.includes("Ворота выбиты")));
  r = E.damageSection(m, tower.id, 150, null, R);
  assert.equal(r.cells.length, 9);
  assert.ok(r.lines.some(l => l.includes("Башня рухнула")));
  // по пролому можно пройти: путь из замка наружу через выбитые ворота
  const geo = {map: m, W: E.mapWidthM(m), H: E.mapHeightM(m)};
  const u = {id: 1, type: "infantry", soldiers: 100, onMap: true, mapX: 50, mapY: 48, facing: 0};
  const rm = E.reachMap(u, geo, R, 1000);
  assert.ok(Number.isFinite(E.pathCost(rm, u, 0.5, 0.97, geo)), "из замка — к краю карты");
});

test("починка возвращает прочность, проломы остаются", () => {
  const m = drawnWall(1), f = m.forts[0];
  E.damageSection(m, f.id, 130, null, R);
  assert.equal(E.sectionHp(f, R), 70);
  const r = E.repairSection(m, f.id, R);
  assert.match(r.lines[0], /70 → 100/);
  assert.equal(E.sectionHp(f, R), 100);
  assert.equal(f.breaches, 1);
});

test("сохранение: слой участков и урон переживают выгрузку; без слоя — старая карта открывается", () => {
  const m = fortified("castle");
  E.damageSection(m, 3, 130, null, R);
  E.damageSection(m, 19, 30, null, R);
  const back = E.deserializeTerrain(JSON.parse(JSON.stringify(E.serializeTerrain(m))));
  assert.deepEqual(Array.from(back.s), Array.from(m.s));
  assert.deepEqual(back.forts, m.forts);
  // старая партия (v30.7): слоя нет — участки строятся по месту
  const old = E.serializeTerrain(fortified("palisade")); delete old.s; delete old.forts;
  const o = E.deserializeTerrain(old);
  assert.equal(o.s, undefined);
  E.ensureSections(o, R);
  assert.equal(o.forts.length, 21);
  // битый слой участков не роняет карту
  const bad = E.serializeTerrain(m); bad.s = "zz.1";
  const b = E.deserializeTerrain(bad);
  assert.ok(b && b.t.length === m.t.length && !b.s, "местность цела, участки — заново");
  // копия для отката независима
  const c = E.cloneTerrain(m); c.forts[0].dmg = 99; c.s[0] = 7;
  assert.notEqual(m.forts[0].dmg, 99); assert.notEqual(m.s[0], 7);
});

test("пересборка после правки в редакторе: урон и проломы остаются у своих участков", () => {
  const m = fortified("castle");
  E.damageSection(m, 3, 130, {x: 50.5, y: 41.5}, R);   // пролом и 30 урона
  E.damageSection(m, 12, 40, null, R);
  const before = m.forts.map(f => [f.id, f.kind, f.dmg, f.breaches]);
  // правка далеко от стен — участки те же
  E.paintDisc(m, "t", 10, 10, 3, T("forest"));
  E.buildSections(m, R, {s: m.s, forts: m.forts});
  assert.deepEqual(m.forts.map(f => [f.id, f.kind, f.dmg, f.breaches]), before);
  assert.ok(m.forts[2].n === 5 && m.forts[2].up === 3, "клетки пролома остались в участке");
});
