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
import { buildSections, damageSection, repairSection, sectionHp } from "../src/engine/fortify.js";

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

export function buildMapCases(){
  const R = getRules("base");
  return {maps: maps(), codec: codec(), paint: paint(), geo: geoCases(R), panic: panicCases(R), forts: fortCases(R)};
}
