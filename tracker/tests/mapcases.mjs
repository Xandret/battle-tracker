// ═══════════ mapcases.mjs — общие сценарии карты 6а для двух реализаций (правило 7, Г25) ═══════════
// Всё, что считают terrain.js, mapgen.js, battlemap.js и panic.js, — на заданных картах, отрядах и зёрнах.
// Результат замораживается в shared/golden/map.json (tools/export-map-golden.mjs); движок на C# (core/)
// отыгрывает те же сценарии и обязан совпасть бит в бит. Тест mapgolden.test.mjs ловит, если сам JS
// ушёл от замороженного: тогда либо правка намеренная (перезаморозить и поправить C# в том же коммите),
// либо это ошибка.
import { mulberry32 } from "../src/engine/util.js";
import { getRules } from "../src/engine/rules.js";
import { createTerrain, serializeTerrain, deserializeTerrain, paintDisc, paintSegment, paintRect, floodFill,
         encodeLayer, mapWidthM, mapHeightM } from "../src/engine/terrain.js";
import { generateMap } from "../src/engine/mapgen.js";
import { footprint, unitCorners, unitGap, groundUnder, mapModsFor, fatigueMultFor, unitSpeed, reachMap, pathCost,
         runOver, runUpBlock, rangeOf, attackReach } from "../src/engine/battlemap.js";
import { lineOfSight, panicWave } from "../src/engine/panic.js";
import { buildSections, damageSection, repairSection, sectionHp, getSection } from "../src/engine/fortify.js";
import { makeMachine, siegeVolley, siegeEndTurn, machineMoved, hitMachine, magicStrike, captureMachine, siegeAim } from "../src/engine/siege.js";
import { sectionGap, defendersOf, towersAt, assaultWall, laddersFor } from "../src/engine/assault.js";

// FNV-1a (32 бита) по байтам — короткий отпечаток слоя карты или массива цен пути
export function fnv(bytes){
  let h = 0x811c9dc5;
  for(const b of bytes){ h ^= b; h = Math.imul(h, 0x01000193) >>> 0; }
  return h.toString(16).padStart(8, "0");
}
const f64bytes = arr => new Uint8Array(new Float64Array(arr).buffer);
// JSON не знает бесконечности — пишем словом
const num = v => Number.isFinite(v) ? v : v > 0 ? "inf" : v < 0 ? "-inf" : "nan";

export const MAP_VARIANTS = [
  ["field", {}], ["field", {groves: "often", hills: 3, road: false}], ["field", {groves: "none", hills: 0, widthM: 800, depthM: 600}],
  ["forest", {}], ["forest", {density: "high", clearings: 8}], ["forest", {density: "low", road: false}],
  ["hills", {}], ["hills", {count: 6, maxHeight: 3, slopeForest: false}],
  ["river", {}], ["river", {width: "wide", direction: "along", fords: 3, bridges: 2}],
  ["river", {width: "narrow", direction: "diagonal", fords: 0, bridges: 0}],
  ["desert", {}], ["desert", {dunes: false, oasis: false}],
  ["palisade", {}], ["palisade", {hill: true, moat: false, gates: 4, gateSide: "east"}],
  ["castle", {}], ["castle", {hill: true, gates: 2, gateSide: "west"}],
  ["concentric", {}], ["concentric", {hill: true, moat: false, gates: 3, gateSide: "north"}],
];
export const SEEDS = [1, 777, 123456789];

function maps(){
  const out = [];
  for(const [template, input] of MAP_VARIANTS) for(const seed of SEEDS){
    const m = generateMap(template, input, seed);
    const counts = {};
    for(const v of m.t) counts[v] = (counts[v] || 0) + 1;
    const zCounts = [0, 0, 0, 0];
    for(const v of m.z) zCounts[v]++;
    out.push({template, input, seed, w: m.w, h: m.h, t: fnv(m.t), z: fnv(m.z), counts, zCounts});
  }
  return out;
}

function codec(){
  const full = [["field", {widthM: 400, depthM: 300}, 5], ["castle", {widthM: 300, depthM: 250, hill: true}, 9]].map(([template, input, seed]) => {
    const s = serializeTerrain(generateMap(template, input, seed));
    return {template, input, seed, w: s.w, h: s.h, t: s.t, z: s.z};
  });
  const saves = [
    {v: 1, cell: 5, w: 4, h: 4, t: "1.8,6.8", z: "0.g"},          // целая
    {v: 1, cell: 5, w: 4, h: 4, t: "1.f", z: "0.g"},              // не хватает клетки
    {v: 1, cell: 5, w: 4, h: 4, t: "1.8,6.9", z: "0.g"},          // лишняя клетка
    {v: 1, cell: 5, w: 4, h: 4, t: "!.g", z: "0.g"},              // не число
    {v: 1, cell: 5, w: 3, h: 10, t: "1.u", z: "0.u"},             // слишком узкая
    {v: 1, cell: 0, w: 4, h: 4, t: "z.8,3.8", z: "9.g"},          // код больше известных, высота больше 3 — обрезаются
    {v: 1, cell: 5, w: 4, h: 4, t: "", z: ""},                    // пустые слои — всё «не задано»
  ];
  const corrupt = saves.map(input => {
    const m = deserializeTerrain(input);
    return {input, result: m && {cell: m.cell, w: m.w, h: m.h, t: encodeLayer(m.t), z: encodeLayer(m.z)}};
  });
  return {full, corrupt};
}

function paint(){
  const m = createTerrain(200, 150);   // 40 × 30 клеток
  const ops = [
    ["disc", "t", [10.3, 8.7, 3.2], 6], ["disc", "z", [12, 10, 4.5], 2], ["segment", "t", [2.5, 25, 37.2, 4.1, 1.5], 2],
    ["rect", "t", [5, 5, 20.7, 15.2], 12, 1], ["rect", "z", [25, 3, 35, 12], 3], ["flood", "t", [30, 25], 1],
    ["flood", "t", [0, 0], 7], ["disc", "z", [30, 20, 2], 9], ["segment", "t", [5, 5, 5, 5, 0.2], 14],
    ["rect", "t", [-3, -3, 2, 2], 11, 0], ["disc", "t", [39.9, 29.9, 0.1], 18],
  ];
  const steps = ops.map(([op, layer, a, value, outline]) => {
    const n = op === "disc" ? paintDisc(m, layer, a[0], a[1], a[2], value)
            : op === "segment" ? paintSegment(m, layer, a[0], a[1], a[2], a[3], a[4], value)
            : op === "rect" ? paintRect(m, layer, a[0], a[1], a[2], a[3], value, outline || 0)
            : floodFill(m, layer, a[0], a[1], value);
    return {op, layer, args: a, value, outline: outline || 0, n, t: encodeLayer(m.t), z: encodeLayer(m.z)};
  });
  return {widthM: 200, depthM: 150, steps};
}

const TYPES = ["infantry", "pike", "archer", "cavalry"];
function makeUnits(seed, n){
  const rng = mulberry32(seed);
  const units = [];
  for(let i = 0; i < n; i++){
    const type = TYPES[Math.floor(rng() * TYPES.length)];
    const weapon = type === "archer" || (type === "cavalry" && rng() < 0.4) ? "ranged" : "melee";
    units.push({id: i + 1, name: `Отряд ${i + 1}`, type, weapon, soldiers: 50 + Math.floor(rng() * 1500),
      factionId: 1 + (i % 2), status: "active", onMap: true,
      mapX: 8 + rng() * 84, mapY: 8 + rng() * 84, facing: rng() < 0.3 ? Math.floor(rng() * 4) * 90 : rng() * 360,
      range: rng() < 0.2 ? 120 + Math.floor(rng() * 100) : 0, runUpM: Math.floor(rng() * 90),
      morale: 60, discipline: 40, fleeChecks: 0});
  }
  return units;
}
const groundOut = g => g && {id: g.id, key: g.key, name: g.name, z: g.z, share: g.share};
const modOut = x => ({mult: x.mult ?? null, note: x.note ?? null, coverPct: x.coverPct ?? 0, coverNote: x.coverNote ?? null});
const modsOut = m => m && {mode: m.mode, noCharge: m.noCharge, notes: m.notes, ab: modOut(m.ab), ba: modOut(m.ba)};
const reachOut = r => ({dist: r.dist, max: r.max, ok: r.ok, text: r.text});

export const GEO_MAPS = [
  ["field", {}, 1, 1], ["forest", {}, 777, 1], ["hills", {count: 6, maxHeight: 3}, 1, 1], ["river", {}, 1, 1.3],
  ["castle", {hill: true, widthM: 600, depthM: 500}, 1, 1], ["desert", {}, 1, 1], ["concentric", {hill: true}, 777, 0.8],
];
function geoCases(R){
  return GEO_MAPS.map(([template, input, seed, stretch], gi) => {
    const m = generateMap(template, input, seed);
    const geo = {map: m, W: mapWidthM(m) * stretch, H: mapHeightM(m)};
    const units = makeUnits(1000 + gi, 10);
    const per = units.map(u => ({
      footprint: footprint(u, R), corners: unitCorners(u, geo, R), ground: groundOut(groundUnder(u, geo, R)),
      ground53: groundOut(groundUnder(u, geo, R, 3, 2)), fatigue: fatigueMultFor(u, geo, R), speed: unitSpeed(u, R),
      range: rangeOf(u, R), runUp: runUpBlock(u, R),
    }));
    const pairs = [];
    for(let i = 0; i < units.length; i++) for(const j of [(i + 1) % units.length, (i + 3) % units.length]){
      const a = units[i], b = units[j];
      const los = lineOfSight(a, b, geo, R);
      const run = runOver(a, [a.mapX / 100, a.mapY / 100], [b.mapX / 100, b.mapY / 100], geo, R);
      pairs.push({a: i, b: j, gap: unitGap(a, b, geo, R), mods: modsOut(mapModsFor(a, b, geo, R)),
        melee: reachOut(attackReach(a, b, true, geo, R)), ranged: reachOut(attackReach(a, b, false, geo, R)),
        los: {ok: los.ok, why: los.why ?? null}, run: {len: run.len, clear: run.clear}});
    }
    const reach = [0, 1, 2].map(k => {
      const u = units[k], limit = unitSpeed(u, R) * 1.5;
      const rm = reachMap(u, geo, R, limit);
      const samples = [[0.5, 0.5], [0.1, 0.9], [u.mapX / 100 + 0.02, u.mapY / 100], [u.mapX / 100, u.mapY / 100 - 0.03],
                       [u.mapX / 100 + 0.05, u.mapY / 100 + 0.05], [0.99, 0.01]]
        .map(([fx, fy]) => [fx, fy, num(pathCost(rm, u, fx, fy, geo))]);
      let finite = 0;
      for(const c of rm.cost) if(Number.isFinite(c)) finite++;
      return {unit: k, limit, hash: fnv(f64bytes(rm.cost)), finite, samples};
    });
    return {template, input, seed, stretch, W: geo.W, H: geo.H, units, per, pairs, reach};
  });
}

// Паника: кучка своих вокруг побежавшего и чужие рядом; холмы и лес закрывают первое кольцо
function panicCases(R){
  const out = [];
  const setups = [["hills", {count: 6, maxHeight: 3}, 1], ["forest", {}, 777], ["field", {groves: "often"}, 1]];
  const geos = setups.map(([template, input, seed]) => {
    const m = generateMap(template, input, seed);
    return {map: m, W: mapWidthM(m), H: mapHeightM(m)};
  });
  const run = (si, s, moraleLoss, rngSeed) => {
    const [template, input, seed] = setups[si];
    const rng = mulberry32(5000 + si * 10 + s);
    const cx = 30 + rng() * 40, cy = 30 + rng() * 40;
    const units = [];
    for(let i = 0; i < 9; i++){
      const type = TYPES[Math.floor(rng() * TYPES.length)];
      units.push({id: i + 1, name: `Полк ${i + 1}`, type, weapon: type === "archer" ? "ranged" : "melee",
        soldiers: 200 + Math.floor(rng() * 800), factionId: i < 7 ? 1 : 2, status: i === 5 ? "fled" : "active", onMap: i !== 6,
        mapX: cx + (rng() - 0.5) * 16, mapY: cy + (rng() - 0.5) * 16, facing: Math.floor(rng() * 8) * 45,
        morale: 30 + Math.floor(rng() * 90), discipline: 20 + Math.floor(rng() * 70), fleeChecks: i === 2 ? 1 : 0,
        broken: false, breakGrace: 0, breakPenalty: 0});
    }
    const res = panicWave(units, 1, geos[si], {rules: R, rng: mulberry32(rngSeed)}, {moraleLoss});
    out.push({template, input, seed, moraleLoss, rngSeed, units, source: 1,
      lines: res.lines, patches: res.patches, fled: res.fled});
  };
  setups.forEach((_, si) => { for(const moraleLoss of [false, true]) for(const s of [1, 2, 3]) run(si, s, moraleLoss, s * 97); });
  // Граница «d100 ≤ дисциплина — держится»: зерно 32 даёт «Полку 5» бросок 26 при дисциплине 26 (подобрано перебором)
  run(2, 1, false, 32);
  return out;
}

// ── участки укреплений (6б, Ш1–Ш2): нарезка, удары, проломы, сохранение, пересборка ──
const u16bytes = arr => { const b = new Uint8Array(arr.length * 2); arr.forEach((v, i) => { b[2 * i] = v & 255; b[2 * i + 1] = v >> 8; }); return b; };
const fortsOut = m => m.forts.map(f => [f.id, f.kind, f.n, f.up, f.len, f.cx, f.cy, f.dmg, f.breaches]);
export const FORT_MAPS = [
  ["palisade", {}, 1], ["palisade", {hill: true, moat: false, gates: 4, gateSide: "east"}, 777],
  ["castle", {}, 1], ["castle", {gates: 2, gateSide: "west"}, 123456789],
  ["concentric", {}, 777], ["concentric", {hill: true, gates: 3, gateSide: "north"}, 1],
];
// Нарисованное рукой: кольцо без башен, толстая стена, косая стена, круглый частокол с воротами, одинокие ворота
function drawnFort(){
  const m = createTerrain(300, 200, 1);   // 60 × 40 клеток
  paintRect(m, "t", 2, 2, 20, 14, 12, 1);                   // кольцо стены без башен
  paintRect(m, "t", 24, 2, 40, 12, 12, 2);                  // толстое кольцо (2 клетки)
  paintRect(m, "t", 30, 6, 33, 8, 1);                       // двор толстого кольца
  paintSegment(m, "t", 3, 36, 28, 19, 0.8, 12);             // косая стена
  paintDisc(m, "t", 46, 26, 9, 15); paintDisc(m, "t", 46, 26, 7.6, 1);   // круглый частокол
  paintRect(m, "t", 45, 34, 47, 35, 13);                    // ворота в частоколе
  paintRect(m, "t", 37, 17, 38, 18, 14); paintRect(m, "t", 54, 18, 55, 19, 14);   // башни у частокола
  paintRect(m, "t", 10, 30, 11, 30, 13);                    // ворота посреди поля
  paintRect(m, "t", 56, 2, 57, 3, 14);                      // башня 2 × 2
  return m;
}
function fortCases(R){
  const sections = FORT_MAPS.map(([template, input, seed]) => {
    const m = generateMap(template, input, seed);
    buildSections(m, R);
    return {template, input, seed, s: fnv(u16bytes(m.s)), forts: fortsOut(m)};
  });
  const dm = drawnFort();
  buildSections(dm, R);
  const drawn = {t: encodeLayer(dm.t), s: encodeLayer(dm.s), forts: fortsOut(dm)};
  // серии ударов: [номер участка, урон, точка попадания в клетках или null]; «repair» — починка
  const runs = [
    ["castle", {}, 1, [[3, 60, null], [3, 60, [50.2, 41.5]], [3, 250, null], [3, 5, null], [19, 79, null], [19, 1, null], [1, 151, null],
                      [8, 99.5, [48.5, 50.1]], [8, 0.5, [48.5, 50.1]], ["repair", 8], [8, 30, null], [99, 10, null]]],
    ["palisade", {}, 1, [[2, 29, null], [2, 31, [30.5, 30.5]], [18, 40, null], [5, 300, null], [7, 12.5, null], ["repair", 7]]],
    ["concentric", {hill: true}, 777, [[4, 100, null], [10, 210, [40.5, 30.5]], [40, 80, null], [20, 1000, null]]],
    ["drawn", null, 0, [[1, 100, [5.5, 2.5]], [1, 100, [3.5, 2.5]], [2, 450, null], [5, 101, [31.5, 3.5]], [9, 40, null], [10, 300, [10.2, 33.1]]]],
  ];
  const damage = runs.map(([template, input, seed, steps]) => {
    const m = template === "drawn" ? drawnFort() : generateMap(template, input, seed);
    buildSections(m, R);
    const out = steps.map(([id, amount, at]) => {
      const f = m.forts.find(q => q.id === (id === "repair" ? amount : id));
      const r = id === "repair" ? repairSection(m, amount, R) : damageSection(m, id, amount, at && {x: at[0], y: at[1]}, R);
      return {id, amount, at, r: r && {lines: r.lines, cells: r.cells ?? null, opened: r.opened ?? null, destroyed: r.destroyed ?? null},
              f: f ? [f.dmg, f.breaches, f.up, sectionHp(f, R)] : null, t: fnv(m.t)};
    });
    const saved = serializeTerrain(m);
    const back = deserializeTerrain(JSON.parse(JSON.stringify(saved)));
    // правка в редакторе: стену частично стёрли, рядом дорисовали — пересборка с прежними участками
    paintRect(m, "t", 0, 0, m.w * 0.3, 1, 1);
    paintSegment(m, "t", m.w * 0.2, m.h * 0.85, m.w * 0.6, m.h * 0.85, 0.6, 12);
    buildSections(m, R, {s: m.s, forts: m.forts});
    return {template, input, seed, steps: out, saved: {s: saved.s, forts: saved.forts}, back: back && {s: encodeLayer(back.s), forts: fortsOut(back)},
            rebuilt: {s: fnv(u16bytes(m.s)), forts: fortsOut(m)}};
  });
  // битые и старые сохранения участков; слой — первые 36 клеток в участке 1 (или 1 и 2)
  const base = serializeTerrain(drawnFort());
  const N = base.w * base.h;
  const layer = (...runs) => { const a = new Uint16Array(N); let p = 0; for(const [v, k] of runs){ a.fill(v, p, p + k); p += k; } return encodeLayer(a); };
  const one = layer([1, 36]), two = layer([1, 36], [2, 5]);
  const corrupt = [
    null,                                                                // до v30.8 — участков нет
    {s: layer([0, N]), forts: []},                                       // слой пустой
    {s: one, forts: [{id: 1, kind: "wall", dmg: 12.5, breaches: 1}]},
    {s: one, forts: [{id: 1, kind: "moat", dmg: 1, breaches: 0}]},       // неизвестный вид — выброшен, номера в слое обнулены
    {s: one, forts: [{id: 1, kind: "tower", dmg: -5, breaches: -2}, {id: 1, kind: "wall", dmg: 3, breaches: 0}]},   // повтор номера
    {s: two, forts: [{id: 2, kind: "gateWood", dmg: "7", breaches: 2.6}]},   // номер 1 без участка — обнулён
    {s: "!." + N.toString(36), forts: []},                               // битый слой — участков нет
    {s: "1.10", forts: []},                                              // не хватает клеток
    {s: one, forts: "нет"},                                              // не список
    {s: (70000).toString(36) + ".10,0." + (N - 36).toString(36), forts: [{id: 65535, kind: "wall", dmg: 0, breaches: 0}]},   // номер больше предела — срезан до 65535
  ].map(extra => {
    const o = Object.assign({}, base, extra || {});
    if(!extra){ delete o.s; delete o.forts; }
    const m = deserializeTerrain(o);
    return {extra, result: m && (m.s ? {s: encodeLayer(m.s), forts: fortsOut(m)} : null)};
  });
  return {sections, drawn, damage, corrupt};
}

// ── осадные орудия (6б, Ш3–Ш4): залпы на зерне — по стене, по отряду, маг-батарея, таран, отказы, между залпами ──
const SIEGE_UNITS = [
  {id: 1, name: "Пехота"}, {id: 2, name: "Рыцари", soldiers: 500, eqDef: 120, exp: 60, morale: 90, discipline: 40},
  {id: 3, name: "Ополчение", soldiers: 800, eqDef: 30, exp: 0, morale: 25, discipline: 30}, {id: 4, name: "Соседи", soldiers: 300},
];
const siegeUnits = () => SIEGE_UNITS.map(o => Object.assign({soldiers: 1000, eqDef: 60, exp: 20, morale: 70, discipline: 50,
  broken: false, breakGrace: 0, breakPenalty: 0, totKilled: 0, totWounded: 0, status: "active"}, o));
// [[вид, число орудий, поля машины], [цель: "unit" номер | "section" номер участка замка, точка], opts, зерно]
export const SIEGE_SHOTS = [
  [["ballista", 5, {}], ["unit", 0], {dist: 120, los: true, coverPct: 0}, 1],
  [["catapult", 3, {}], ["unit", 1], {dist: 200, los: false, coverPct: 30}, 2],
  [["trebuchet", 4, {}], ["section", 3, [50.5, 41.5]], {dist: 150}, 3],
  [["trebuchet", 4, {}], ["section", 3], {dist: 360}, 3],
  [["bombard", 6, {exp: 80}], ["section", 19], {dist: 100}, 4],
  [["bombard", 10, {}], ["unit", 2], {dist: 80, los: true}, 5],
  [["cannon", 8, {}], ["unit", 0], {dist: 300, los: true, coverPct: 30}, 6],
  [["cannon", 8, {crew: 20}], ["unit", 0], {dist: 100, los: true}, 7],
  [["mortar", 4, {exp: 20}], ["unit", 1], {dist: 120, los: false}, 8],
  [["ribauldequin", 6, {}], ["unit", 0], {dist: 60, los: true}, 9],
  [["ribauldequin", 6, {}], ["unit", 0], {dist: 200, los: true}, 9],
  [["magic", 3, {mageSkill: 14}], ["unit", 0], {dist: 300, los: true}, 10],
  [["magic", 4, {count: 2}], ["section", 5], {dist: 200, los: true}, 11],
  [["magic", 3, {mageSkill: 0}], ["unit", 0], {dist: 100, los: true}, 12],
  [["ram", 1, {}], ["section", 19], {dist: 3}, 13],
  [["ram", 2, {}], ["section", 3], {dist: 2}, 13],
  [["ram", 1, {}], ["unit", 0], {dist: 2}, 13],
  [["tower", 1, {}], ["section", 3], {dist: 2}, 13],
  [["cannon", 2, {ready: 1}], ["unit", 0], {dist: 100}, 14],
  [["bombard", 2, {deployLeft: 1}], ["unit", 0], {dist: 100}, 14],
  [["ballista", 2, {}], ["section", 3], {dist: 100, los: false}, 15],
  [["bombard", 20, {}], ["section", 8], {dist: 200, los: true}, 16],
  [["bombard", 20, {}], ["section", 8], {dist: 200, los: true}, 17],
  [["bombard", 20, {crew: 33}], ["section", 8], {dist: 200, los: true}, 18],
  [["cannon", 3, {}], ["unit", 3], {dist: 0, los: true}, 19],
];
function siegeCases(R){
  const out = r => r && {ok: r.ok, title: r.title, lines: r.lines, tone: r.tone, machine: r.machine, patches: r.patches, stats: r.stats};
  const shots = SIEGE_SHOTS.map(([[kind, count, extra], [tk, ti, at], opts, seed]) => {
    const m = makeMachine(kind, count, R, extra);
    let map = null, target;
    if(tk === "section"){
      map = generateMap("castle", {}, 1);
      buildSections(map, R);
      target = {section: {map, id: ti, at: at ? {x: at[0], y: at[1]} : null}};
    } else {
      const us = siegeUnits();
      target = {unit: us[ti], splash: us.filter((_, k) => k !== ti).map((u, k) => ({unit: u, gap: 10 + k * 15}))};
    }
    const r = siegeVolley(m, target, opts, {rules: R, rng: mulberry32(seed)});
    const f = map ? getSection(map, ti) : null;
    return {kind, count, extra, target: [tk, ti, at || null], opts, seed, r: out(r), t: map ? fnv(map.t) : null,
            sec: f ? [f.dmg, f.breaches, f.up] : null};
  });
  // между залпами: конец хода, марш, удар по машине, взрыв маг-пушки, захват
  const kinds = Object.keys(R.siege.engines);
  const moved = kinds.map(k => machineMoved(makeMachine(k, 2, R), R));
  const endTurn = [{ready: 2, deployLeft: 1}, {ready: 0, deployLeft: 0}, {}].map(m => siegeEndTurn(m));
  const tre = makeMachine("trebuchet", 3, R);
  const h1 = hitMachine(tre, 120, R), h2 = hitMachine(Object.assign({}, tre, h1.patch), 30, R), h3 = hitMachine(makeMachine("magic", 2, R), 39.5, R);
  const strikes = [3, 4, 5, 6, 7, 8, 9, 10].map(seed => {
    const mg = makeMachine("magic", 3, R, {mageSkill: 9});
    const us = siegeUnits();
    const r = magicStrike(mg, us.map((u, k) => ({unit: u, gap: k * 20})), {rules: R, rng: mulberry32(seed)});
    return {seed, calm: r.calm, lines: r.lines, patches: r.patches, machine: r.machine};
  });
  const capture = [captureMachine(makeMachine("magic", 3, R, {ready: 1}), 5, R), captureMachine(makeMachine("cannon", 3, R, {deployLeft: 2}), 5, R)];
  // прицел с карты: замок на холме, машина в разных местах — до участков и до отрядов
  const am = generateMap("castle", {hill: true, widthM: 600, depthM: 500}, 1);
  buildSections(am, R);
  const ageo = {map: am, W: mapWidthM(am), H: mapHeightM(am)};
  const tower = am.forts.find(f => f.kind === "tower");
  const spots = [[50, 85], [50, 15], [50, 50], [tower.cx / am.w * 100, tower.cy / am.h * 100], [8, 50], [30, 70]];
  const secs = [am.forts.find(f => f.kind === "wall").id, am.forts.find(f => f.kind === "gateIron").id, am.forts[am.forts.length - 1].id, tower.id];
  const aus = siegeUnits().slice(0, 3).map((u, k) => Object.assign(u, {type: ["infantry", "cavalry", "archer"][k], onMap: true,
    mapX: [50, 50, 25][k], mapY: [48, 80, 30][k], facing: [0, 30, 90][k]}));
  const aimOut = a => a && {dist: a.dist, los: a.los, coverPct: a.coverPct, coverNote: a.coverNote, at: a.at};
  const aims = spots.map(([x, y]) => {
    const m = makeMachine("cannon", 1, R, {mapX: x, mapY: y});
    return {spot: [x, y], sections: secs.map(id => aimOut(siegeAim(m, {section: {id}}, ageo, R))),
            units: aus.map(u => aimOut(siegeAim(m, {unit: u}, ageo, R))),
            open: aus.map(u => aimOut(siegeAim(m, {unit: u}, {map: null, W: ageo.W, H: ageo.H}, R)))};
  });
  return {units: siegeUnits(), shots, misc: {moved, endTurn, hits: [h1, h2, h3], strikes, capture},
          aim: {secs, units: aus, aims}};
}

// ── приступ на стену (6б, Г48, Г50, Ш10–Ш14): кто у стены, лестницы, башня, пролом, отказы, занятие участка ──
function assaultCases(R){
  const mk = () => { const map = generateMap("castle", {}, 1); buildSections(map, R); return {map, geo: {map, W: mapWidthM(map), H: mapHeightM(map)}}; };
  const {map: m0, geo: g0} = mk();
  const wall = m0.forts.find(f => f.kind === "wall" && f.cy > 55);
  const others = [m0.forts.find(f => f.kind === "tower" && f.cy > 55).id, m0.forts.find(f => f.kind === "gateIron").id];
  const at = (cx, cy) => ({mapX: cx * 5 / g0.W * 100, mapY: cy * 5 / g0.H * 100});
  const base = {discipline: 50, morale: 70, eqAtk: 60, eqDef: 60, exp: 20, mastery: 10, fatigue: 0, status: "active", onMap: true,
    attacksMade: 0, countersMade: 0, totKilled: 0, totWounded: 0, broken: false, breakGrace: 0, breakPenalty: 0, weapon: "melee", type: "infantry"};
  const units = () => [
    Object.assign({}, base, {id: 1, name: "Штурмовые", soldiers: 1000, factionId: 1, facing: 0, ladders: 20}, at(wall.cx, wall.cy + 3.2)),
    Object.assign({}, base, {id: 2, name: "Гарнизон", soldiers: 300, factionId: 2, facing: 180}, at(wall.cx, wall.cy)),
    Object.assign({}, base, {id: 3, name: "Рыцари", type: "cavalry", soldiers: 500, factionId: 1, facing: 0, eqDef: 80, discipline: 40}, at(wall.cx - 3, wall.cy + 6)),
    Object.assign({}, base, {id: 4, name: "Стрелки на стене", type: "archer", weapon: "ranged", soldiers: 400, factionId: 2, facing: 180, eqDef: 30}, at(wall.cx + 5, wall.cy)),
    Object.assign({}, base, {id: 5, name: "Ополчение во дворе", soldiers: 200, factionId: 2, facing: 180, morale: 15, eqDef: 40, discipline: 40}, at(wall.cx, wall.cy - 1.5)),
    Object.assign({}, base, {id: 6, name: "Далёкие", soldiers: 600, factionId: 1, facing: 0, ladders: 10, discipline: 85}, at(wall.cx, wall.cy + 12)),
  ];
  const towers = [makeMachine("tower", 1, R, Object.assign({onMap: true, factionId: 1}, at(wall.cx + 2, wall.cy + 2))),
                  makeMachine("tower", 2, R, Object.assign({onMap: true, factionId: 2}, at(wall.cx, wall.cy + 2))),
                  makeMachine("tower", 1, R, Object.assign({onMap: false, factionId: 1}, at(wall.cx, wall.cy + 2)))];
  const us = units();
  const near = us.map(u => ({id: u.id, ladders: laddersFor(u, R), gaps: [wall.id, ...others].map(id => sectionGap(u, id, g0, R)),
    defenders: defendersOf(wall.id, us, u, g0, R).map(d => [d.unit.id, d.gap]), towers: towersAt(wall.id, towers, u, g0, R)}));
  // [кто идёт (номер), путь, правка отряда, правка opts, зерно, участок пробит заранее (урон)]
  const RUNS = [
    [0, "ladders", {}, {}, 1, 0], [0, "ladders", {}, {}, 2, 0], [0, "ladders", {ladders: 3}, {}, 3, 0],
    [0, "tower", {}, {}, 4, 0], [5, "ladders", {}, {}, 5, 0], [0, "ladders", {}, {engaged: [7, 8]}, 6, 0],
    [2, "ladders", {}, {}, 7, 0], [2, "breach", {}, {}, 8, 150], [0, "breach", {}, {}, 9, 250], [0, "breach", {}, {engaged: [9]}, 10, 120],
    [0, "ladders", {attacksMade: 1}, {}, 11, 0], [0, "ladders", {ladders: 0}, {}, 12, 0], [0, "breach", {}, {}, 13, 0],
    [0, "ladders", {}, {noDefenders: true}, 14, 0], [0, "tower", {}, {noDefenders: true}, 15, 0], [3, "ladders", {ladders: 8}, {}, 16, 0],
  ];
  const runs = RUNS.map(([k, via, uPatch, oPatch, seed, pre]) => {
    const {map, geo} = mk();
    if(pre) damageSection(map, wall.id, pre, null, R);
    const all = units(), A = Object.assign(all[k], uPatch);
    const defs = oPatch.noDefenders ? [] : defendersOf(wall.id, all, A, geo, R);
    const opts = {via, gap: sectionGap(A, wall.id, geo, R), ladders: A.ladders || 0, towers: towersAt(wall.id, towers, A, geo, R),
                  engaged: oPatch.engaged || [], fatigueMode: "percent"};
    const r = assaultWall(A, map, wall.id, defs, opts, {rules: R, rng: mulberry32(seed), commanderOf: () => null, factionName: () => ""});
    const f = getSection(map, wall.id);
    return {k, via, uPatch, oPatch, seed, pre, r: {ok: r.ok, title: r.title, lines: r.lines, tone: r.tone, patches: r.patches, ladders: r.ladders, captured: r.captured},
            holder: f.holder ?? null, saved: serializeTerrain(map).forts.find(x => x.id === wall.id)};
  });
  return {wall: wall.id, others, units: units(), towers, near, runs};
}

export function buildMapCases(){
  const R = getRules("base");
  return {maps: maps(), codec: codec(), paint: paint(), geo: geoCases(R), panic: panicCases(R), forts: fortCases(R), siege: siegeCases(R),
          assault: assaultCases(R)};
}
