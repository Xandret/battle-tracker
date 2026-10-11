// ═══════════ battlemap.js — отряд на карте в метрах (этап 6а, К21, К24) ═══════════
// Чистая геометрия и местность, без браузера. geo = {map, W, H}: местность (или null) и размер карты в метрах.
// Положение отряда хранится как раньше — mapX/mapY в процентах карты; фасинг 0° — «вверх» (север).
// Всё, что отсюда влияет на бой, включается только переключателями «Правила карты».
import { cellAt, TERRAIN_BY_ID } from "./terrain.js";

// Строй: фронт × глубина в метрах. Шеренг столько, сколько положено типу, — пока людей хватает;
// потери сужают фронт, а не глубину.
export function footprint(u, rules){
  const F = rules.map.formation[u.type] || rules.map.formation.infantry;
  const n = Math.max(1, Math.round(u.soldiers || 0));
  const ranks = Math.min(F.ranks, n);
  return {front: Math.max(1, Math.ceil(n / ranks) * F.perMan), depth: Math.max(1, ranks * F.rankDepth)};
}
export const unitCenter = (u, geo) => [u.mapX / 100 * geo.W, u.mapY / 100 * geo.H];

// Четыре угла прямоугольника строя в метрах; первые два — передний край (куда смотрит отряд)
export function unitCorners(u, geo, rules){
  const {front, depth} = footprint(u, rules);
  const [cx, cy] = unitCenter(u, geo);
  const a = (u.facing || 0) * Math.PI / 180, c = Math.cos(a), s = Math.sin(a);
  return [[-front / 2, -depth / 2], [front / 2, -depth / 2], [front / 2, depth / 2], [-front / 2, depth / 2]]
    .map(([x, y]) => [cx + x * c - y * s, cy + x * s + y * c]);
}

// Расстояние между краями двух выпуклых многоугольников (0 — касаются или перекрываются)
function overlaps(P, Q){
  for(const poly of [P, Q]) for(let i = 0; i < poly.length; i++){
    const [x1, y1] = poly[i], [x2, y2] = poly[(i + 1) % poly.length];
    const nx = y2 - y1, ny = x1 - x2;
    const proj = pts => pts.map(([x, y]) => x * nx + y * ny);
    const a = proj(P), b = proj(Q);
    if(Math.max(...a) < Math.min(...b) || Math.max(...b) < Math.min(...a)) return false;
  }
  return true;
}
function pointSeg([px, py], [x1, y1], [x2, y2]){
  const dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
  const t = l2 ? Math.max(0, Math.min(1, ((px - x1) * dx + (py - y1) * dy) / l2)) : 0;
  return Math.hypot(px - (x1 + t * dx), py - (y1 + t * dy));
}
export function polyGap(P, Q){
  if(overlaps(P, Q)) return 0;
  let d = Infinity;
  for(const [A, B] of [[P, Q], [Q, P]])
    for(const p of A) for(let i = 0; i < B.length; i++) d = Math.min(d, pointSeg(p, B[i], B[(i + 1) % B.length]));
  return d;
}
export const unitGap = (a, b, geo, rules) => polyGap(unitCorners(a, geo, rules), unitCorners(b, geo, rules));

// Что под отрядом: местность под большей частью строя (по сетке точек 5 × 3) и средняя высота.
// Клетки «не задано» не голосуют; если вид местности под строем не нарисован — key: null, но высота считается.
// Поровну — побеждает пересечённая местность: строй, наполовину вошедший в лес, уже не «в строю».
export function groundUnder(u, geo, rules, nx = 5, ny = 3){
  if(!geo || !geo.map) return null;
  const P = unitCorners(u, geo, rules);
  const votes = {};
  let zSum = 0, n = 0;
  for(let i = 0; i < nx; i++) for(let j = 0; j < ny; j++){
    const a = (i + 0.5) / nx, b = (j + 0.5) / ny;
    // билинейно внутри прямоугольника строя
    const x = (P[0][0] * (1 - a) + P[1][0] * a) * (1 - b) + (P[3][0] * (1 - a) + P[2][0] * a) * b;
    const y = (P[0][1] * (1 - a) + P[1][1] * a) * (1 - b) + (P[3][1] * (1 - a) + P[2][1] * a) * b;
    const c = cellAt(geo.map, x / geo.W, y / geo.H);
    zSum += c.z; n++;
    if(c.t) votes[c.t] = (votes[c.t] || 0) + 1;
  }
  const rough = id => (rules.map.terrain[TERRAIN_BY_ID[+id].key] || {}).mode === "rough" ? 1 : 0;
  const best = Object.entries(votes).sort((p, q) => q[1] - p[1] || rough(q[0]) - rough(p[0]))[0];
  const z = Math.round(zSum / n);
  if(!best) return {id: 0, key: null, name: "не задано", z, share: 0};
  const t = TERRAIN_BY_ID[+best[0]];
  return {id: t.id, key: t.key, name: t.name, z, share: best[1] / n};
}

// Модификаторы боя A → B по местности (К24). ab — для удара A по B, ba — для ответа B по A.
// mult действует только в ближнем бою (высота), coverPct — только в дальнем (укрытие от стрел).
export function mapModsFor(A, B, geo, rules){
  const gB = groundUnder(B, geo, rules);
  if(!gB) return null;
  const gA = groundUnder(A, geo, rules);
  const T = rules.map.terrain, H = rules.map.height;
  if(!gB.key && !gB.z && !(gA && (gA.key || gA.z))) return null;   // под обоими ничего не нарисовано
  const tb = T[gB.key] || {}, ta = gA ? (T[gA.key] || {}) : {};
  const low = s => s.toLowerCase();
  const where = g => g ? `${low(g.name)}${g.z ? ", высота " + g.z : ""}` : "не задано";
  const mods = {mode: tb.mode || null, ab: {}, ba: {}, noCharge: null,
                notes: [`🗺 Местность: «${B.name}» — ${where(gB)}; «${A.name}» — ${where(gA)}`]};
  if(tb.cover) Object.assign(mods.ab, {coverPct: tb.cover, coverNote: `Укрытие «${B.name}» (${low(gB.name)})`});
  if(ta.cover) Object.assign(mods.ba, {coverPct: ta.cover, coverNote: `Укрытие «${A.name}» (${low(gA.name)})`});
  const dz = (gA ? gA.z : 0) - gB.z;
  if(dz > 0){
    Object.assign(mods.ab, {mult: H.downhillMelee, note: `⛰ Удар сверху вниз (высота ${gA.z} → ${gB.z})`});
    Object.assign(mods.ba, {mult: H.uphillMelee, note: `⛰ Ответ снизу вверх (высота ${gB.z} → ${gA.z})`});
  } else if(dz < 0){
    Object.assign(mods.ab, {mult: H.uphillMelee, note: `⛰ Удар снизу вверх (высота ${gA ? gA.z : 0} → ${gB.z})`});
    Object.assign(mods.ba, {mult: H.downhillMelee, note: `⛰ Ответ сверху вниз (высота ${gB.z} → ${gA ? gA.z : 0})`});
  }
  if(tb.noCharge) mods.noCharge = "местность: " + low(gB.name);
  else if(ta.noCharge) mods.noCharge = "местность: " + low(gA.name);
  return mods;
}

// Во сколько раз быстрее копится усталость на местности под отрядом (К17: песок, К33: снег)
export function fatigueMultFor(u, geo, rules){
  const g = groundUnder(u, geo, rules);
  const r = g && g.key && rules.map.terrain[g.key];
  return r && r.fatigue ? {mult: r.fatigue, name: g.name} : null;
}

// ═══════════ движение и дальности (К22, К23, К29) — черновик ═══════════
const isHorseArcher = u => u.type === "cavalry" && u.weapon === "ranged";
export function unitSpeed(u, rules){
  const S = rules.map.speed;
  return isHorseArcher(u) ? S.horseArcher : (S[u.type] || S.infantry);
}
// Множитель пути по клетке для отряда; null — непроходимо. «Не задано» — как поле.
function moveMult(map, i, u, rules){
  const t = map.t[i];
  if(!t) return 1;
  const r = rules.map.terrain[TERRAIN_BY_ID[t].key];
  if(!r || !r.move) return null;
  return r.move[u.type === "cavalry" ? 1 : 0];
}
// Карта досягаемости: цена пути в метрах до каждой клетки (алгоритм Дейкстры, 8 соседей).
// Шаг по клетке = размер клетки × (√2 по диагонали) × множитель местности × подъём (climbCost за уровень).
// Считаем до limitM — дальше Infinity. Без местности — null (тогда путь по прямой).
export function reachMap(u, geo, rules, limitM){
  const m = geo && geo.map; if(!m) return null;
  const [cx, cy] = unitCenter(u, geo);
  const sx = Math.min(m.w - 1, Math.max(0, Math.floor(cx / geo.W * m.w)));
  const sy = Math.min(m.h - 1, Math.max(0, Math.floor(cy / geo.H * m.h)));
  // клетка карты в метрах (карта может быть растянута под картинку — берём по ширине и высоте отдельно)
  const cw = geo.W / m.w, ch = geo.H / m.h, cd = Math.hypot(cw, ch);
  // Float64: в 32 битах цена диагонального шага (7,07 × 3 м) округляется, и проверка «уже дешевле»
  // при извлечении из кучи ложно отбрасывает клетку — зона выходит крестом с дырами
  const cost = new Float64Array(m.w * m.h).fill(Infinity);
  const climb = rules.map.height.climbCost;
  const heap = [];   // [цена, клетка] — двоичная куча
  const push = (c, i) => { heap.push([c, i]); let k = heap.length - 1;
    while(k > 0){ const p = (k - 1) >> 1; if(heap[p][0] <= heap[k][0]) break; [heap[p], heap[k]] = [heap[k], heap[p]]; k = p; } };
  const pop = () => { const top = heap[0], last = heap.pop();
    if(heap.length){ heap[0] = last; let k = 0;
      for(;;){ const l = 2 * k + 1, r = l + 1; let s = k;
        if(l < heap.length && heap[l][0] < heap[s][0]) s = l;
        if(r < heap.length && heap[r][0] < heap[s][0]) s = r;
        if(s === k) break; [heap[s], heap[k]] = [heap[k], heap[s]]; k = s; } }
    return top; };
  const start = sy * m.w + sx;
  cost[start] = 0; push(0, start);
  while(heap.length){
    const [c, i] = pop();
    if(c > cost[i] || c > limitM) continue;
    const x = i % m.w, y = (i - x) / m.w;
    for(let dy = -1; dy <= 1; dy++) for(let dx = -1; dx <= 1; dx++){
      if(!dx && !dy) continue;
      const nx = x + dx, ny = y + dy;
      if(nx < 0 || ny < 0 || nx >= m.w || ny >= m.h) continue;
      const j = ny * m.w + nx;
      const mult = moveMult(m, j, u, rules);
      if(mult === null) continue;
      // по диагонали нельзя «протиснуться» между двумя непроходимыми клетками
      if(dx && dy && moveMult(m, y * m.w + nx, u, rules) === null && moveMult(m, ny * m.w + x, u, rules) === null) continue;
      const up = m.z[j] - m.z[i];
      const step = (dx && dy ? cd : dx ? cw : ch) * mult * (up > 0 ? Math.pow(climb, up) : 1);
      const nc = c + step;
      if(nc < cost[j]){ cost[j] = nc; push(nc, j); }
    }
  }
  return {cost, w: m.w, h: m.h};
}
// Цена пути до точки (доли карты); без местности — расстояние по прямой
export function pathCost(reach, u, fx, fy, geo){
  if(!reach){
    const [cx, cy] = unitCenter(u, geo);
    return Math.hypot(fx * geo.W - cx, fy * geo.H - cy);
  }
  const x = Math.min(reach.w - 1, Math.max(0, Math.floor(fx * reach.w)));
  const y = Math.min(reach.h - 1, Math.max(0, Math.floor(fy * reach.h)));
  return reach.cost[y * reach.w + x];
}
// Прямой отрезок пути: длина и «чистый» ли он для разбега (без леса, болота, брода)
export function runOver(u, from, to, geo, rules){
  const len = Math.round(Math.hypot((to[0] - from[0]) * geo.W, (to[1] - from[1]) * geo.H) * 10) / 10;
  let clear = true;
  if(geo.map){
    const n = Math.max(2, Math.ceil(len / 5));
    for(let k = 0; k <= n && clear; k++){
      const c = cellAt(geo.map, from[0] + (to[0] - from[0]) * k / n, from[1] + (to[1] - from[1]) * k / n);
      const r = c.t && rules.map.terrain[TERRAIN_BY_ID[c.t].key];
      if(r && (r.noCharge || !r.move)) clear = false;
    }
  }
  return {len, clear};
}
// Натиск без разбега невозможен (К29): причина или null
export function runUpBlock(u, rules){
  const need = rules.map.chargeRunUp, got = Math.round(u.runUpM || 0);
  return got >= need ? null : `разбег ${got} м из ${need}`;
}
// Дальность стрельбы: своя у отряда или по типу; ближнему бою — 0
export function rangeOf(u, rules){
  if(u.weapon !== "ranged") return 0;
  if(+u.range > 0) return +u.range;
  const R = rules.map.range;
  return isHorseArcher(u) ? R.horseArcher : u.type === "archer" ? R.archer : R.other;
}
// Достаёт ли A до B этим видом боя: расстояние между краями строя против дальности
export function attackReach(A, B, melee, geo, rules){
  const dist = Math.round(unitGap(A, B, geo, rules));
  if(melee){
    const max = rules.map.meleeGap;
    return {kind: "melee", dist, max, ok: dist <= max,
            text: dist <= max ? "" : `до «${B.name}» ${dist} м — для ближнего боя нужно вплотную (до ${max} м)`};
  }
  let max = rangeOf(A, rules);
  const gA = groundUnder(A, geo, rules), gB = groundUnder(B, geo, rules);
  const dz = (gA ? gA.z : 0) - (gB ? gB.z : 0);
  if(dz > 0) max = Math.round(max * (1 + rules.map.height.rangePerLevel * dz));
  return {kind: "ranged", dist, max, ok: dist <= max,
          text: dist <= max ? "" : `до «${B.name}» ${dist} м — дальность «${A.name}» ${max} м${dz > 0 ? ` (с высоты +${Math.round(rules.map.height.rangePerLevel * dz * 100)}%)` : ""}`};
}
