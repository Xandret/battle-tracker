// ═══════════ panic.js — каскадная паника (этап 6а, К11, К25) — ЧЕРНОВИК ДО ГМа ═══════════
// Отряд побежал → свои в радиусе бросают проверку на побег d100 ≤ дисциплина. Побежавший запускает
// проверку у своих соседей, даже не видевших первого. Каждый отряд — раз за волну. Гасителей нет:
// армия может рухнуть за ход — так задумано (SPEC, этап 6). Работает только при включённом переключателе.
import { rollDie } from "./util.js";
import { applyMoraleChange } from "./morale.js";
import { cellAt, TERRAIN_BY_ID } from "./terrain.js";
import { unitGap, unitCenter, groundUnder } from "./battlemap.js";

// Прямая видимость между центрами строя: холм выше обоих закрывает; лес пропускает взгляд
// на sight метров (100), стены, башни, здания и скалы — нисколько.
export function lineOfSight(a, b, geo, rules){
  if(!geo || !geo.map) return {ok: true};
  const [ax, ay] = unitCenter(a, geo), [bx, by] = unitCenter(b, geo);
  const za = (groundUnder(a, geo, rules) || {z: 0}).z, zb = (groundUnder(b, geo, rules) || {z: 0}).z;
  const len = Math.hypot(bx - ax, by - ay), n = Math.max(2, Math.ceil(len / 5)), step = len / n;
  const through = {};
  for(let k = 1; k < n; k++){
    const c = cellAt(geo.map, (ax + (bx - ax) * k / n) / geo.W, (ay + (by - ay) * k / n) / geo.H);
    if(c.z > Math.max(za, zb)) return {ok: false, why: "холм"};
    const t = c.t && TERRAIN_BY_ID[c.t], r = t && rules.map.terrain[t.key];
    if(r && r.sight !== undefined){
      through[t.key] = (through[t.key] || 0) + step;
      if(through[t.key] > r.sight) return {ok: false, why: t.name.toLowerCase()};
    }
  }
  return {ok: true};
}

// Свои — та же фракция (подфракции внутри неё тоже свои). Союзов между фракциями в трекере пока нет.
const sameSide = (a, b) => !!a.factionId && a.factionId === b.factionId;

// Волна паники от отряда sourceId. units не меняются: возвращаются патчи и строки журнала.
// opts.moraleLoss — переключатель «−100 БД вместе с проверкой».
export function panicWave(units, sourceId, geo, ctx, opts = {}){
  const R = ctx.rules, P = R.map.panic;
  const state = new Map(units.map(u => [u.id, Object.assign({}, u)]));
  const src = state.get(sourceId);
  const L = [], patches = new Map(), fled = [];
  if(!src || !src.onMap) return {lines: L, patches: [], fled};
  const checked = new Set([sourceId]);
  let queue = [src], ring = 0, total = 0;
  while(queue.length){
    const next = [];
    for(const runner of queue){
      const near = [...state.values()]
        .filter(v => v.status === "active" && v.onMap && !checked.has(v.id) && sameSide(v, runner))
        .map(v => ({v, gap: unitGap(runner, v, geo, R)}))
        .filter(x => x.gap <= P.radius)
        .sort((a, b) => a.gap - b.gap || a.v.id - b.v.id);
      for(const {v, gap} of near){
        const dist = `${Math.round(gap)} м от «${runner.name}»`;
        // первое кольцо — только те, кто видел бегство; дальше волна идёт без видимости
        if(ring === 0){
          const los = lineOfSight(runner, v, geo, R);
          if(!los.ok){ L.push(`«${v.name}» (${dist}) не видел бегства — мешает ${los.why}`); continue; }
        }
        checked.add(v.id); total++;
        const patch = {};
        const extra = [];
        if(opts.moraleLoss){
          Object.assign(patch, applyMoraleChange(v, v.morale - P.moraleLoss, extra, R));
          extra.unshift(`БД «${v.name}» −${P.moraleLoss}: ${v.morale} → ${patch.morale}`);
        }
        const roll = rollDie(ctx.rng, 100);
        if(roll <= v.discipline){
          L.push(`«${v.name}» (${dist}${ring ? ", волна " + (ring + 1) : ""}): d100 = ${roll} ≤ ${v.discipline} — держится`);
        } else {
          L.push(`✘ «${v.name}» (${dist}${ring ? ", волна " + (ring + 1) : ""}): d100 = ${roll} > ${v.discipline} — бежит!`);
          patch.status = "fled";
          patch.fleeChecks = (v.fleeChecks || 0) + 1;
          fled.push(v.id);
        }
        extra.forEach(l => L.push("  " + l));
        Object.assign(v, patch);
        if(Object.keys(patch).length) patches.set(v.id, Object.assign(patches.get(v.id) || {}, patch));
        if(patch.status === "fled") next.push(v);
      }
    }
    queue = next; ring++;
  }
  if(total) L.push(`Итог волны: бежали ${fled.length} из ${total} проверенных`);
  return {lines: L, patches: [...patches].map(([id, patch]) => ({id, patch})), fled};
}
