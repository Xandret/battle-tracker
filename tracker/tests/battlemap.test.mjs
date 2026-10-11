// ═══════════ battlemap.test.mjs — отряд на карте в метрах, местность в бою (черновик 6а) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const R = E.getRules("base");
const K = k => E.TERRAIN_BY_KEY[k].id;
const unit = (o = {}) => Object.assign({id: 1, name: "Отряд", type: "infantry", weapon: "melee", soldiers: 1000, initial: 1000,
  discipline: 50, morale: 60, eqAtk: 40, eqDef: 40, exp: 20, mastery: 0, fatigue: 0, status: "active",
  factionId: 1, acted: false, attacksMade: 0, countersMade: 0, turnsActive: 0, fleeChecks: 0, breakGrace: 0,
  broken: false, onMap: true, mapX: 50, mapY: 50, facing: 0}, o);
const near = (a, b, eps = 1e-6) => Math.abs(a - b) < eps;

test("строй в метрах (К21)", () => {
  assert.deepEqual(E.footprint(unit(), R), {front: 125, depth: 8}, "пехота 1000 — 125 × 8 м");
  assert.deepEqual(E.footprint(unit({type: "cavalry"}), R), {front: 300, depth: 15}, "конница 1000 — 300 × 15 м");
  assert.deepEqual(E.footprint(unit({type: "pike"}), R), {front: 100, depth: 10});
  assert.deepEqual(E.footprint(unit({type: "archer", soldiers: 300}), R), {front: 60, depth: 5});
  assert.deepEqual(E.footprint(unit({soldiers: 500}), R), {front: 63, depth: 8}, "потери сужают фронт, а не глубину");
  assert.deepEqual(E.footprint(unit({soldiers: 3}), R), {front: 1, depth: 3}, "горстка — в одну колонну");
});

test("прямоугольник строя поворачивается с фасингом; расстояние между краями", () => {
  const geo = {map: null, W: 1000, H: 1000};
  const a = unit({mapX: 50, mapY: 50});                 // центр 500, 500; фронт 125 по x
  const P = E.unitCorners(a, geo, R);
  assert.ok(near(P[0][0], 437.5) && near(P[0][1], 496), "передний левый угол при фасинге 0°");
  const turned = E.unitCorners(unit({facing: 90}), geo, R);
  assert.ok(near(turned[0][0], 504) && near(turned[0][1], 437.5), "фасинг 90° — фронт смотрит на восток");
  const b = unit({id: 2, mapX: 50, mapY: 52});           // на 20 м южнее: 20 − 4 − 4 = 12 м между краями
  assert.ok(near(E.unitGap(a, b, geo, R), 12));
  const c = unit({id: 3, mapX: 50, mapY: 50.5});         // перекрываются
  assert.equal(E.unitGap(a, c, geo, R), 0);
  const d = unit({id: 4, mapX: 70, mapY: 50, facing: 90}); // 200 м восточнее, повёрнут: 200 − 62,5 − 4
  assert.ok(near(E.unitGap(a, d, geo, R), 133.5));
});

test("что под отрядом: местность большинства и средняя высота", () => {
  const map = E.createTerrain(1000, 1000, K("field"));   // 200 × 200 клеток
  E.paintRect(map, "t", 0, 0, 199, 99, K("forest"));     // вся северная половина — лес
  E.paintRect(map, "z", 0, 0, 199, 99, 2);
  const geo = {map, W: 1000, H: 1000};
  const inForest = E.groundUnder(unit({mapY: 25}), geo, R);
  assert.equal(inForest.key, "forest");
  assert.equal(inForest.z, 2);
  assert.equal(E.groundUnder(unit({mapY: 75}), geo, R).key, "field");
  assert.equal(E.groundUnder(unit(), {map: null, W: 1, H: 1}, R), null, "нет местности — нет решения");
  const blank = E.groundUnder(unit(), {map: E.createTerrain(1000, 1000), W: 1000, H: 1000}, R);
  assert.equal(blank.key, null, "не нарисовано — вид местности не решён");
  assert.equal(E.mapModsFor(unit(), unit({id: 2}), {map: E.createTerrain(1000, 1000), W: 1000, H: 1000}, R), null, "пустая карта — без модификаторов");
});

test("строй поровну на поле и в лесу — решает пересечённая местность", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 0, 99, 199, K("forest"));     // западная половина — лес
  const geo = {map, W: 1000, H: 1000};
  const u = unit({mapX: 50, mapY: 50, soldiers: 800});   // фронт 100 м: половина слева, половина справа
  const g = E.groundUnder(u, geo, R, 4, 2);               // 4 × 2 точки: 4 в лесу, 4 в поле
  assert.equal(g.key, "forest");
  assert.equal(g.share, 0.5);
});

test("модификаторы: лес — пересечённая, укрытие, без натиска; высота — сверху вниз (К24)", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 0, 199, 99, K("forest"));
  E.paintRect(map, "z", 100, 100, 199, 199, 1);          // юго-восток — холм
  const geo = {map, W: 1000, H: 1000};
  const archers = unit({id: 1, name: "Лучники", type: "archer", weapon: "ranged", mapX: 50, mapY: 70});
  const inWood = unit({id: 2, name: "В лесу", factionId: 2, mapX: 50, mapY: 25});
  const m = E.mapModsFor(archers, inWood, geo, R);
  assert.equal(m.mode, "rough");
  assert.equal(m.ab.coverPct, 30);
  assert.equal(m.ba.coverPct, undefined, "стрелки на поле без укрытия");
  assert.equal(m.noCharge, "местность: лес");
  const onHill = unit({id: 3, name: "На холме", mapX: 75, mapY: 75});
  const below = unit({id: 4, name: "Внизу", factionId: 2, mapX: 25, mapY: 75});
  const h = E.mapModsFor(onHill, below, geo, R);
  assert.equal(h.mode, "form");
  assert.equal(h.ab.mult, 1.2, "удар сверху вниз");
  assert.equal(h.ba.mult, 0.9, "ответ снизу вверх");
});

// Один и тот же бой с одним зерном — с картой и без: разница только в модификаторах карты
function fight(A, B, req, seed){
  const ctx = {rules: R, rng: E.mulberry32(seed), commanderOf: () => null, factionName: () => ""};
  return E.resolveBattle(A, B, req, ctx);
}

test("укрытие снижает потери от стрел; в журнале — пометка «черновик»", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 0, 199, 99, K("forest"));
  const geo = {map, W: 1000, H: 1000};
  const A = unit({id: 1, name: "Лучники", type: "archer", weapon: "ranged", mapY: 70, eqAtk: 70});
  const B = unit({id: 2, name: "В лесу", factionId: 2, mapY: 25});
  const mm = E.mapModsFor(A, B, geo, R);
  let lessTotal = 0, plainTotal = 0;
  for(let seed = 1; seed <= 20; seed++){
    const plain = fight(A, B, {mode: "ranged_rough", mutual: false}, seed);
    const cover = fight(A, B, {mode: "ranged_rough", mutual: false, mapMods: mm}, seed);
    const dmg = r => r.patches.find(p => p.id === 2).patch.soldiers;
    plainTotal += 1000 - dmg(plain); lessTotal += 1000 - dmg(cover);
    assert.ok(cover.lines.some(l => l.startsWith("Укрытие «В лесу» (лес): −30%") && l.endsWith("черновик")));
    assert.ok(cover.lines.some(l => l.startsWith("🗺 Местность")));
  }
  assert.ok(lessTotal < plainTotal * 0.8, `укрытие 30%: ${lessTotal} против ${plainTotal}`);
});

test("натиск в лес невозможен; сверху вниз бьют сильнее", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 0, 199, 99, K("forest"));
  E.paintRect(map, "z", 0, 100, 199, 199, 1);
  const geo = {map, W: 1000, H: 1000};
  const cav = unit({id: 1, name: "Всадники", type: "cavalry", mapY: 75});
  const wood = unit({id: 2, name: "В лесу", factionId: 2, mapY: 25});
  const r = fight(cav, wood, {mode: "melee_rough", mutual: true, charge: true, mapMods: E.mapModsFor(cav, wood, geo, R)}, 5);
  assert.ok(r.lines.some(l => l.startsWith("🐎 Натиск невозможен — местность: лес")));
  assert.ok(r.lines.some(l => l.startsWith("Кавалерия без натиска")), "бьёт без натиска −30%");
  assert.ok(!r.title.includes("натиск"), "в заголовке натиска нет: " + r.title);
  // холм на юге, лес на севере: всадники с холма (z 1) бьют вниз, в лес (z 0)
  assert.ok(r.lines.some(l => l.startsWith("⛰ Удар сверху вниз") && l.includes("+20%")));
});

test("без модификаторов карты бой прежний (эталон v29 не трогаем)", () => {
  const A = unit({id: 1, name: "А"}), B = unit({id: 2, name: "Б", factionId: 2, mapY: 52});
  const a = fight(A, B, {mode: "melee_form", mutual: true}, 9);
  const b = fight(A, B, {mode: "melee_form", mutual: true, mapMods: null}, 9);
  assert.deepEqual(a, b);
});

test("усталость на песке копится вдвое быстрее (К17)", () => {
  const map = E.createTerrain(1000, 1000, K("sand"));
  const geo = {map, W: 1000, H: 1000};
  const u = unit({acted: true, turnsActive: 10});
  const ctx = {rules: R, fatigueMult: x => E.fatigueMultFor(x, geo, R)};
  const r = E.endTurn([u], ctx);
  assert.equal(r.units[0].fatigue, 20, "шаг 10 × 2");
  assert.ok(r.lines[0].includes("песок ×2"));
  assert.equal(E.endTurn([u], {rules: R}).units[0].fatigue, 10, "без карты — как было");
});

// ── v30.6: движение и дальности ──
test("скорости за ход (К22)", () => {
  assert.equal(E.unitSpeed(unit(), R), 100);
  assert.equal(E.unitSpeed(unit({type: "pike"}), R), 80);
  assert.equal(E.unitSpeed(unit({type: "cavalry"}), R), 250);
  assert.equal(E.unitSpeed(unit({type: "cavalry", weapon: "ranged"}), R), 300, "конные стрелки");
});

test("путь по клеткам: лес дороже, река без моста непроходима, подъём дороже", () => {
  const map = E.createTerrain(1000, 1000, K("field"));      // 200 × 200 клеток по 5 м
  const geo = {map, W: 1000, H: 1000};
  const u = unit({mapX: 20, mapY: 50});                       // 200 м от западного края
  let reach = E.reachMap(u, geo, R, 400);
  const east100 = E.pathCost(reach, u, 0.3, 0.5, geo);
  assert.ok(Math.abs(east100 - 100) <= 5, "по полю 100 м — 100: " + east100);
  E.paintRect(map, "t", 45, 0, 199, 199, K("forest"));      // лес с 225 м на восток
  reach = E.reachMap(u, geo, R, 400);
  const into = E.pathCost(reach, u, 0.3, 0.5, geo);
  assert.ok(into >= 170 && into <= 185, "25 м полем и 75 м лесом ×2 — около 175 с точностью до клетки: " + into);
  // река поперёк с мостом
  const m2 = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(m2, "t", 0, 98, 199, 101, K("water"));
  E.paintRect(m2, "t", 150, 98, 151, 101, K("bridge"));
  const g2 = {map: m2, W: 1000, H: 1000};
  const v = unit({mapX: 30, mapY: 40});
  const r2 = E.reachMap(v, g2, R, 5000);
  const across = E.pathCost(r2, v, 0.3, 0.6, g2);
  assert.ok(across > 700, "река непроходима — в обход через мост: " + Math.round(across));
  assert.equal(E.pathCost(r2, v, 0.3, 0.499, g2), Infinity, "в саму реку не войти");
  // подъём
  const m3 = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(m3, "z", 60, 0, 199, 199, 1);
  const g3 = {map: m3, W: 1000, H: 1000};
  const w = unit({mapX: 20, mapY: 50});
  const up = E.pathCost(E.reachMap(w, g3, R, 400), w, 0.4, 0.5, g3);
  assert.ok(up > 200 && up < 210, "200 м с одним подъёмом: +50% за шаг наверх: " + up);
  assert.equal(E.reachMap(w, {map: null, W: 1, H: 1}, R, 100), null, "без местности — по прямой");
  assert.ok(Math.abs(E.pathCost(null, w, 0.3, 0.5, {map: null, W: 1000, H: 1000}) - 100) < 1e-9);
});

test("разбег для натиска (К29)", () => {
  const map = E.createTerrain(1000, 1000, K("field"));
  E.paintRect(map, "t", 0, 0, 199, 49, K("swamp"));
  const geo = {map, W: 1000, H: 1000};
  assert.deepEqual(E.runOver(unit(), [0.5, 0.9], [0.5, 0.8], geo, R), {len: 100, clear: true});
  assert.equal(E.runOver(unit(), [0.5, 0.4], [0.5, 0.1], geo, R).clear, false, "через болото — не разбег");
  assert.equal(E.runUpBlock(unit({runUpM: 30}), R), "разбег 30 м из 50");
  assert.equal(E.runUpBlock(unit({runUpM: 60}), R), null);
});

test("дальность и «вплотную» (К23)", () => {
  assert.equal(E.rangeOf(unit({type: "archer", weapon: "ranged"}), R), 200);
  assert.equal(E.rangeOf(unit({type: "cavalry", weapon: "ranged"}), R), 150);
  assert.equal(E.rangeOf(unit({type: "archer", weapon: "ranged", range: 260}), R), 260, "своя дальность");
  assert.equal(E.rangeOf(unit(), R), 0, "ближний бой не стреляет");
  const geo = {map: null, W: 1000, H: 1000};
  const bows = unit({id: 1, name: "Лучники", type: "archer", weapon: "ranged", mapY: 50});
  const far = unit({id: 2, name: "Цель", factionId: 2, mapY: 76});   // 260 м центр — края 2,5 + 4 → 253,5
  const r = E.attackReach(bows, far, false, geo, R);
  assert.equal(r.ok, false);
  assert.ok(r.text.includes("дальность «Лучники» 200 м"));
  const map = E.createTerrain(1000, 1000);
  E.paintRect(map, "z", 0, 0, 199, 139, 3);                      // лучники на уровне 3
  const hi = E.attackReach(bows, far, false, {map, W: 1000, H: 1000}, R);
  assert.equal(hi.max, 260, "с высоты 3 над целью: +30% дальности");
  assert.equal(hi.ok, true);
  const touch = unit({id: 3, name: "Вплотную", factionId: 2, mapY: 51.2});
  assert.equal(E.attackReach(unit({id: 4}), touch, true, geo, R).ok, true, "4 м между краями — вплотную");
  assert.equal(E.attackReach(unit({id: 4}), far, true, geo, R).ok, false);
});

test("зона досягаемости без дыр: по диагонали через лес тоже считается", () => {
  const map = E.createTerrain(1000, 1000, K("forest"));
  const geo = {map, W: 1000, H: 1000};
  const cav = unit({type: "cavalry", mapX: 50, mapY: 50});
  const r = E.reachMap(cav, geo, R, 300);
  const c = Math.floor(map.w / 2);
  let holes = 0;
  for(let y = c - 10; y <= c + 10; y++) for(let x = c - 10; x <= c + 10; x++) if(!isFinite(r.cost[y * map.w + x])) holes++;
  assert.equal(holes, 0, "в квадрате 21 × 21 клетка все досягаемы");
  const diag = r.cost[(c + 5) * map.w + (c + 5)];
  assert.ok(Math.abs(diag - 5 * Math.hypot(5, 5) * 3) < 0.01, "5 диагональных шагов по лесу ×3: " + diag);
});
