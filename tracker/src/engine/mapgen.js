// ═══════════ mapgen.js — генераторы карт (К8, К9, К27, К28) ═══════════
// Каждый шаблон — функция от настроек и зерна: одно и то же зерно всегда даёт одну и ту же карту,
// «ещё вариант» — просто новое зерно. Случайность — только mulberry32(seed), как во всём движке.
import { mulberry32 } from "./util.js";
import { TERRAIN_BY_KEY, createTerrain, paintDisc, paintSegment } from "./terrain.js";

const K = key => TERRAIN_BY_KEY[key].id;
const SIDES = [["south", "юг"], ["north", "север"], ["west", "запад"], ["east", "восток"]];
const SIZE = (w, h) => [
  {key: "widthM", name: "Ширина, м", type: "number", def: w, min: 200, max: 7000, step: 50},
  {key: "depthM", name: "Глубина, м", type: "number", def: h, min: 200, max: 7000, step: 50},
];
const FORT = [
  {key: "hill", name: "На холме", type: "bool", def: false},
  {key: "moat", name: "Ров", type: "bool", def: true},
  {key: "gates", name: "Ворот", type: "number", def: 1, min: 1, max: 4, step: 1},
  {key: "gateSide", name: "Главные ворота", type: "select", def: "south", options: SIDES},
];

// Шаблоны первой очереди (К9, К34). Вторая очередь — Тракт, Болото, Ущелье, Осадный лагерь,
// Городские стены, Город с улицами — добавляется сюда же.
export const MAP_TEMPLATES = [
  {id: "field", name: "Поле", group: "Поле", params: [...SIZE(2000, 1500),
    {key: "groves", name: "Рощи", type: "select", def: "rare", options: [["none", "нет"], ["rare", "редко"], ["often", "часто"]]},
    {key: "hills", name: "Холмы", type: "number", def: 1, min: 0, max: 3, step: 1},
    {key: "road", name: "Дорога", type: "bool", def: true}]},
  {id: "forest", name: "Лес", group: "Поле", params: [...SIZE(2000, 1500),
    {key: "density", name: "Густота", type: "select", def: "mid", options: [["low", "редкий"], ["mid", "средний"], ["high", "густой"]]},
    {key: "clearings", name: "Поляны", type: "number", def: 3, min: 0, max: 8, step: 1},
    {key: "road", name: "Лесная дорога", type: "bool", def: true}]},
  {id: "hills", name: "Холмы", group: "Поле", params: [...SIZE(2000, 1500),
    {key: "count", name: "Холмов", type: "number", def: 3, min: 1, max: 6, step: 1},
    {key: "maxHeight", name: "Наибольшая высота", type: "number", def: 2, min: 1, max: 3, step: 1},
    {key: "slopeForest", name: "Лес на склонах", type: "bool", def: true}]},
  {id: "river", name: "Река", group: "Поле", params: [...SIZE(2000, 1500),
    {key: "width", name: "Ширина реки", type: "select", def: "mid", options: [["narrow", "узкая, 15 м"], ["mid", "средняя, 30 м"], ["wide", "широкая, 60 м"]]},
    {key: "direction", name: "Течёт", type: "select", def: "across", options: [["across", "поперёк поля"], ["along", "вдоль поля"], ["diagonal", "по диагонали"]]},
    {key: "fords", name: "Бродов", type: "number", def: 1, min: 0, max: 3, step: 1},
    {key: "bridges", name: "Мостов", type: "number", def: 1, min: 0, max: 2, step: 1}]},
  {id: "desert", name: "Пустыня", group: "Поле", params: [...SIZE(2000, 1500),
    {key: "dunes", name: "Барханы", type: "bool", def: true},
    {key: "oasis", name: "Оазис", type: "bool", def: true},
    {key: "rocks", name: "Скалы", type: "bool", def: true}]},
  {id: "palisade", name: "Частокол (острог)", group: "Крепость", params: [...SIZE(500, 400), ...FORT]},
  {id: "castle", name: "Каменный замок", group: "Крепость", params: [...SIZE(600, 500), ...FORT]},
  {id: "concentric", name: "Концентрический замок", group: "Крепость", params: [...SIZE(700, 600), ...FORT]},
];
export const getMapTemplate = id => MAP_TEMPLATES.find(t => t.id === id) || null;

// Настройки: значения по умолчанию + введённое, в пределах шаблона
export function mapParams(id, input = {}){
  const t = getMapTemplate(id); if(!t) throw new Error("нет такого шаблона карты: " + id);
  const p = {};
  for(const d of t.params){
    const v = input[d.key];
    if(d.type === "number") p[d.key] = Number.isFinite(+v) && v !== "" && v !== null ? Math.min(d.max, Math.max(d.min, Math.round(+v))) : d.def;
    else if(d.type === "bool") p[d.key] = v === undefined ? d.def : !!v;
    else p[d.key] = d.options.some(o => o[0] === v) ? v : d.def;
  }
  return p;
}

export function generateMap(id, input, seed){
  const t = getMapTemplate(id); if(!t) throw new Error("нет такого шаблона карты: " + id);
  const p = mapParams(id, input);
  seed = (seed >>> 0) || 1;
  const rng = mulberry32(seed);
  const m = createTerrain(p.widthM, p.depthM, id === "desert" ? K("sand") : K("field"));
  GENERATORS[id](m, rng, p);
  m.meta = {template: id, name: t.name, params: p, seed};
  return m;
}

// ── помощники ──
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = t => t * t * (3 - 2 * t);
const rint = (rng, a, b) => a + Math.floor(rng() * (b - a + 1));
// Гладкий шум: случайные значения в узлах решётки с шагом scale клеток, между ними — сглаженно
function valueNoise(rng, w, h, scale){
  scale = Math.max(1, scale);
  const gw = Math.ceil(w / scale) + 2, gh = Math.ceil(h / scale) + 2;
  const g = new Float32Array(gw * gh);
  for(let i = 0; i < g.length; i++) g[i] = rng();
  return (x, y) => {
    const fx = Math.max(0, x) / scale, fy = Math.max(0, y) / scale;
    const x0 = Math.min(gw - 2, Math.floor(fx)), y0 = Math.min(gh - 2, Math.floor(fy));
    const tx = smooth(Math.min(1, fx - x0)), ty = smooth(Math.min(1, fy - y0));
    const i = y0 * gw + x0;
    return lerp(lerp(g[i], g[i + 1], tx), lerp(g[i + gw], g[i + gw + 1], tx), ty);
  };
}
function fbm(rng, w, h, scale, octaves = 3){
  const ns = [];
  for(let o = 0; o < octaves; o++) ns.push(valueNoise(rng, w, h, scale / (1 << o)));
  return (x, y) => { let s = 0, a = 1, tot = 0; for(const n of ns){ s += n(x, y) * a; tot += a; a *= 0.5; } return s / tot; };
}
const cells = m => m.w * m.h;
function eachCell(m, fn){ for(let y = 0; y < m.h; y++) for(let x = 0; x < m.w; x++) fn(x, y, y * m.w + x); }
// Пятно с неровным краем (роща, болото, скалы): радиус r клеток, край «гуляет» на rough долю радиуса
function blob(m, layer, cx, cy, r, value, noise, rough = 0.35, only = null){
  const arr = layer === "z" ? m.z : m.t;
  for(let y = Math.floor(cy - r * 1.4); y <= cy + r * 1.4; y++)
    for(let x = Math.floor(cx - r * 1.4); x <= cx + r * 1.4; x++){
      if(x < 0 || y < 0 || x >= m.w || y >= m.h) continue;
      const d = Math.hypot(x + 0.5 - cx, y + 0.5 - cy) / r;
      if(d > 1 + rough * (noise(x, y) - 0.5) * 2) continue;
      const i = y * m.w + x;
      if(only && !only.includes(arr[i])) continue;
      arr[i] = layer === "z" ? Math.max(arr[i], value) : value;
    }
}
// Извилистая линия от края до края: точки с плавным отклонением поперёк направления
function wander(m, rng, from, to, amp, waves, steps = 48){
  const dx = to[0] - from[0], dy = to[1] - from[1], len = Math.hypot(dx, dy);
  const nx = -dy / len, ny = dx / len;
  const ph = rng() * Math.PI * 2, ph2 = rng() * Math.PI * 2;
  const pts = [];
  for(let i = 0; i <= steps; i++){
    const t = i / steps;
    const off = amp * (Math.sin(t * Math.PI * 2 * waves + ph) * 0.7 + Math.sin(t * Math.PI * 2 * waves * 2.3 + ph2) * 0.3)
              * Math.sin(Math.PI * Math.min(1, t * 1.2 + 0.05));
    pts.push([from[0] + dx * t + nx * off, from[1] + dy * t + ny * off]);
  }
  return pts;
}
function strokePath(m, pts, r, value){
  for(let i = 1; i < pts.length; i++) paintSegment(m, "t", pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], r, value);
}
function crossRoad(m, rng, vertical){
  const from = vertical ? [m.w * (0.3 + rng() * 0.4), -2] : [-2, m.h * (0.3 + rng() * 0.4)];
  const to = vertical ? [m.w * (0.3 + rng() * 0.4), m.h + 2] : [m.w + 2, m.h * (0.3 + rng() * 0.4)];
  const pts = wander(m, rng, from, to, Math.min(m.w, m.h) * 0.08, 1 + rng());
  strokePath(m, pts, 1, K("road"));
  return pts;
}
function groves(m, rng, count, rMin, rMax, keepOut){
  const n = fbm(rng, m.w, m.h, 8, 2);
  for(let k = 0; k < count; k++){
    let cx, cy, tries = 0;
    do{ cx = rng() * m.w; cy = rng() * m.h; tries++; } while(keepOut && keepOut(cx, cy) && tries < 20);
    const r = lerp(rMin, rMax, rng());
    blob(m, "t", cx, cy, r * 1.25, K("shrub"), n, 0.4, [K("field"), K("sand")]);
    blob(m, "t", cx, cy, r, K("forest"), n, 0.4, [K("field"), K("shrub"), K("sand")]);
  }
}
function hill(m, rng, cx, cy, r, top, noise){
  for(let lvl = 1; lvl <= top; lvl++) blob(m, "z", cx, cy, r * (1 - (lvl - 1) / (top + 0.6)), lvl, noise, 0.3);
}

// ── поле ──
function genField(m, rng, p){
  const n = fbm(rng, m.w, m.h, 30);
  const s = Math.min(m.w, m.h);
  for(let k = 0; k < p.hills; k++) hill(m, rng, rng() * m.w, rng() * m.h, s * lerp(0.12, 0.22, rng()), rint(rng, 1, 2), n);
  if(p.road) crossRoad(m, rng, rng() < 0.5);
  const per = {none: 0, rare: 3, often: 8}[p.groves];
  const count = Math.round(per * cells(m) / 120000) || (per ? 1 : 0);
  groves(m, rng, count, 6, 20);
}
// ── лес ──
function genForest(m, rng, p){
  const thr = {low: 0.6, mid: 0.5, high: 0.4}[p.density];
  const n = fbm(rng, m.w, m.h, 40, 4);
  eachCell(m, (x, y, i) => {
    const v = n(x, y);
    if(v > thr) m.t[i] = K("forest");
    else if(v > thr - 0.05) m.t[i] = K("shrub");
  });
  const e = fbm(rng, m.w, m.h, 6, 2);
  for(let k = 0; k < p.clearings; k++) blob(m, "t", rng() * m.w, rng() * m.h, lerp(6, 16, rng()), K("field"), e, 0.35);
  if(p.road) crossRoad(m, rng, rng() < 0.5);
}
// ── холмы ──
function genHills(m, rng, p){
  const n = fbm(rng, m.w, m.h, 25);
  const s = Math.min(m.w, m.h);
  for(let k = 0; k < p.count; k++){
    const top = k === 0 ? p.maxHeight : rint(rng, 1, p.maxHeight);
    hill(m, rng, lerp(0.12, 0.88, rng()) * m.w, lerp(0.12, 0.88, rng()) * m.h, s * lerp(0.12, 0.25, rng()), top, n);
  }
  if(p.slopeForest){
    const f = fbm(rng, m.w, m.h, 10, 3);
    eachCell(m, (x, y, i) => { if(m.z[i] >= 1 && f(x, y) > 0.58) m.t[i] = K("forest"); else if(m.z[i] >= 1 && f(x, y) > 0.53) m.t[i] = K("shrub"); });
  }
  groves(m, rng, Math.round(2 * cells(m) / 120000), 5, 12);
}
// ── река ──
function genRiver(m, rng, p){
  const r = {narrow: 1.5, mid: 3, wide: 6}[p.width];   // 15 / 30 / 60 м
  let from, to;
  if(p.direction === "across") { from = [-3, m.h * lerp(0.35, 0.65, rng())]; to = [m.w + 3, m.h * lerp(0.35, 0.65, rng())]; }
  else if(p.direction === "along") { from = [m.w * lerp(0.35, 0.65, rng()), -3]; to = [m.w * lerp(0.35, 0.65, rng()), m.h + 3]; }
  else { from = [-3, m.h * lerp(0.05, 0.25, rng())]; to = [m.w + 3, m.h * lerp(0.75, 0.95, rng())]; }
  const pts = wander(m, rng, from, to, Math.min(m.w, m.h) * 0.12, 1 + rng() * 1.5, 80);
  // берега: кустарник и рощицы вдоль воды
  const n = fbm(rng, m.w, m.h, 6, 2);
  pts.forEach((q, i) => { if(i % 6 === 0 && rng() < 0.5) blob(m, "t", q[0], q[1], r + lerp(3, 8, rng()), K("shrub"), n, 0.5, [K("field")]); });
  groves(m, rng, Math.round(3 * cells(m) / 120000), 5, 14);
  // мосты: дорога поперёк реки до краёв карты
  const spots = pickSpots(rng, pts.length, p.bridges + p.fords);
  const bridges = spots.slice(0, p.bridges), fords = spots.slice(p.bridges);
  bridges.forEach(i => {
    const [q, nx, ny] = riverNormal(pts, i);
    const far = Math.max(m.w, m.h) * 2;
    paintSegment(m, "t", q[0] - nx * far, q[1] - ny * far, q[0] + nx * far, q[1] + ny * far, 1, K("road"));
  });
  strokePath(m, pts, r, K("water"));
  bridges.forEach(i => {
    const [q, nx, ny] = riverNormal(pts, i);
    paintSegment(m, "t", q[0] - nx * (r + 2), q[1] - ny * (r + 2), q[0] + nx * (r + 2), q[1] + ny * (r + 2), 1, K("bridge"));
  });
  fords.forEach(i => {
    const q = pts[i];
    replaceNear(m, q[0], q[1], r + 3, [K("water")], K("ford"));
  });
}
function pickSpots(rng, n, k){
  const out = [];
  for(let j = 0; j < k; j++) out.push(Math.round(n * (0.15 + 0.7 * (j + 0.3 + rng() * 0.4) / Math.max(1, k))));
  return out;
}
function riverNormal(pts, i){
  const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
  const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.hypot(dx, dy) || 1;
  return [pts[i], -dy / len, dx / len];
}
function replaceNear(m, cx, cy, r, from, to){
  for(let y = Math.floor(cy - r); y <= cy + r; y++) for(let x = Math.floor(cx - r); x <= cx + r; x++){
    if(x < 0 || y < 0 || x >= m.w || y >= m.h) continue;
    if(Math.hypot(x + 0.5 - cx, y + 0.5 - cy) > r) continue;
    const i = y * m.w + x;
    if(from.includes(m.t[i])) m.t[i] = to;
  }
}
// ── пустыня ──
function genDesert(m, rng, p){
  if(p.dunes){
    // барханы — вытянутые гряды высотой 1 (К27)
    const n = fbm(rng, m.w, m.h, 20, 2), ang = rng() * Math.PI, ca = Math.cos(ang), sa = Math.sin(ang);
    const lam = lerp(9, 14, rng());
    eachCell(m, (x, y, i) => { if(Math.sin((x * ca + y * sa) / lam + n(x, y) * 6) > 0.72) m.z[i] = 1; });
  }
  if(p.rocks){
    const n = fbm(rng, m.w, m.h, 5, 2);
    for(let k = 0; k < Math.max(2, Math.round(5 * cells(m) / 120000)); k++) blob(m, "t", rng() * m.w, rng() * m.h, lerp(2, 7, rng()), K("rocks"), n, 0.5);
  }
  if(p.oasis){
    const cx = m.w * lerp(0.3, 0.7, rng()), cy = m.h * lerp(0.3, 0.7, rng()), r = lerp(4, 8, rng());
    const n = fbm(rng, m.w, m.h, 5, 2);
    // в оазисе барханов нет
    for(let y = 0; y < m.h; y++) for(let x = 0; x < m.w; x++) if(Math.hypot(x - cx, y - cy) < r * 3.2) m.z[y * m.w + x] = 0;
    blob(m, "t", cx, cy, r * 3.2, K("field"), n, 0.35);
    blob(m, "t", cx, cy, r * 2.4, K("shrub"), n, 0.35);
    blob(m, "t", cx, cy, r * 1.8, K("forest"), n, 0.35);
    blob(m, "t", cx, cy, r, K("water"), n, 0.25);
  }
}

// ── крепости (К28) ──
// Форма крепости — предикат «клетка внутри». Стена — клетки внутри, у которых хотя бы один
// из восьми соседей снаружи: такое кольцо не обойти ни прямо, ни по диагонали.
function ringOf(m, inside){
  const ring = [];
  eachCell(m, (x, y) => {
    if(!inside(x, y)) return;
    for(let dy = -1; dy <= 1; dy++) for(let dx = -1; dx <= 1; dx++)
      if((dx || dy) && !inside(x + dx, y + dy)){ ring.push([x, y]); return; }
  });
  return ring;
}
const rectShape = (cx, cy, hw, hh) => (x, y) => Math.abs(x + 0.5 - cx) <= hw && Math.abs(y + 0.5 - cy) <= hh;
const ellipseShape = (cx, cy, a, b) => (x, y) => ((x + 0.5 - cx) / a) ** 2 + ((y + 0.5 - cy) / b) ** 2 <= 1;
const setT = (m, x, y, v) => { if(x >= 0 && y >= 0 && x < m.w && y < m.h) m.t[y * m.w + x] = v; };
const fillShape = (m, shape, v, onlyIf) => eachCell(m, (x, y, i) => { if(shape(x, y) && (!onlyIf || onlyIf(m.t[i]))) m.t[i] = v; });
function block(m, cx, cy, hw, hh, v){ for(let y = Math.round(cy - hh); y < Math.round(cy + hh); y++) for(let x = Math.round(cx - hw); x < Math.round(cx + hw); x++) setT(m, x, y, v); }
// Порядок сторон для ворот: главная, противоположная, левая, правая
function gateSides(p){
  const opp = {south: "north", north: "south", west: "east", east: "west"};
  const left = {south: "west", north: "east", west: "north", east: "south"};
  return [p.gateSide, opp[p.gateSide], left[p.gateSide], opp[left[p.gateSide]]].slice(0, p.gates);
}
// Точка на стороне прямоугольника/эллипса и направление наружу; shift — смещение вдоль стороны (доля)
function sidePoint(side, cx, cy, hw, hh, shift = 0){
  switch(side){
    case "south": return {x: cx + shift * hw, y: cy + hh, nx: 0, ny: 1};
    case "north": return {x: cx + shift * hw, y: cy - hh, nx: 0, ny: -1};
    case "west":  return {x: cx - hw, y: cy + shift * hh, nx: -1, ny: 0};
    default:      return {x: cx + hw, y: cy + shift * hh, nx: 1, ny: 0};
  }
}
// Ворота: клетки стены у точки — в ворота (проём 10 м); дорога от ворот к краю карты; мост через ров
function makeGate(m, g, half, wallIds, roadTo){
  const along = g.nx === 0 ? [1, 0] : [0, 1];
  for(let d = -3; d <= 3; d++) for(let s = -half; s < half; s++){
    const x = Math.floor(g.x + along[0] * (s + 0.5) + g.nx * d * 0.5), y = Math.floor(g.y + along[1] * (s + 0.5) + g.ny * d * 0.5);
    if(x >= 0 && y >= 0 && x < m.w && y < m.h && wallIds.includes(m.t[y * m.w + x])) m.t[y * m.w + x] = K("gate");
  }
  if(roadTo){
    const far = Math.max(m.w, m.h);
    const ox = g.x + g.nx * 1.5, oy = g.y + g.ny * 1.5;
    paintRoadOver(m, ox, oy, g.x + g.nx * far, g.y + g.ny * far);
  }
}
// Дорога не стирает стены и ров: где ров — там мост
function paintRoadOver(m, x0, y0, x1, y1){
  const len = Math.hypot(x1 - x0, y1 - y0), n = Math.ceil(len * 2);
  for(let k = 0; k <= n; k++){
    const x = x0 + (x1 - x0) * k / n, y = y0 + (y1 - y0) * k / n;
    for(let dy = -1; dy <= 0; dy++) for(let dx = -1; dx <= 0; dx++){
      const cx = Math.floor(x + dx + 0.5), cy = Math.floor(y + dy + 0.5);
      if(cx < 0 || cy < 0 || cx >= m.w || cy >= m.h) continue;
      const i = cy * m.w + cx, t = m.t[i];
      if(t === K("moat") || t === K("water")) m.t[i] = K("bridge");
      else if(t === K("field") || t === K("shrub") || t === K("forest") || t === K("sand")) m.t[i] = K("road");
    }
  }
}
function fortBase(m, rng, p, radius, extra = 0){
  const cx = m.w / 2, cy = m.h / 2;
  const n = fbm(rng, m.w, m.h, 12, 2);
  if(p.hill) hill(m, rng, cx, cy, radius + 22, 2 + extra, n);
  // пара рощ поодаль — лес, где у Кордуа нашлись лестницы
  groves(m, rng, 3, 4, 10, (x, y) => Math.hypot(x - cx, y - cy) < radius + 18);
  return {cx, cy};
}
function moatAround(m, shapeOuter, shapeInner){
  eachCell(m, (x, y, i) => { if(shapeOuter(x, y) && !shapeInner(x, y)) m.t[i] = K("moat"); });
}
function genPalisade(m, rng, p){
  const a = 15, b = 11;   // ~150 × 110 м
  const {cx, cy} = fortBase(m, rng, p, a);
  if(p.moat) moatAround(m, ellipseShape(cx, cy, a + 5, b + 5), ellipseShape(cx, cy, a + 2, b + 2));
  const inside = ellipseShape(cx, cy, a, b);
  const ring = ringOf(m, inside);
  fillShape(m, inside, K("field"));
  ring.forEach(([x, y]) => setT(m, x, y, K("palisade")));
  // вышки по кругу
  const towers = rint(rng, 4, 6), off = rng() * Math.PI;
  for(let k = 0; k < towers; k++){
    const t = off + k * Math.PI * 2 / towers;
    block(m, cx + Math.cos(t) * (a - 0.5), cy + Math.sin(t) * (b - 0.5), 1, 1, K("tower"));
  }
  // избы
  for(let k = 0; k < rint(rng, 5, 9); k++){
    const t = rng() * Math.PI * 2, d = Math.sqrt(rng()) * 0.65;
    block(m, cx + Math.cos(t) * a * d, cy + Math.sin(t) * b * d, 1, 1, K("building"));
  }
  gateSides(p).forEach(side => makeGate(m, sidePoint(side, cx, cy, a - 0.5, b - 0.5), 1, [K("palisade"), K("tower")], true));
}
function stoneWalls(m, cx, cy, hw, hh){
  const inside = rectShape(cx, cy, hw, hh);
  const ring = ringOf(m, inside);
  fillShape(m, inside, K("pavement"));
  ring.forEach(([x, y]) => setT(m, x, y, K("wall")));
  return inside;
}
function cornerTowers(m, cx, cy, hw, hh, mids){
  for(const sx of [-1, 1]) for(const sy of [-1, 1]) block(m, cx + sx * hw, cy + sy * hh, 1.5, 1.5, K("tower"));
  if(mids) for(const [sx, sy] of [[0, -1], [0, 1], [-1, 0], [1, 0]]) block(m, cx + sx * hw, cy + sy * hh, 1.5, 1.5, K("tower"));
}
// Надвратная башня: две башни по бокам проёма
function gatehouse(m, g){
  const along = g.nx === 0 ? [1, 0] : [0, 1];
  for(const s of [-1, 1]) block(m, g.x + along[0] * s * 2.5 - g.nx * 0.5, g.y + along[1] * s * 2.5 - g.ny * 0.5, 1.5, 1.5, K("tower"));
}
function genCastle(m, rng, p){
  const hw = 12, hh = 9;   // 120 × 90 м
  const {cx, cy} = fortBase(m, rng, p, hw);
  if(p.moat) moatAround(m, rectShape(cx, cy, hw + 6, hh + 6), rectShape(cx, cy, hw + 3, hh + 3));
  stoneWalls(m, cx, cy, hw, hh);
  cornerTowers(m, cx, cy, hw - 0.5, hh - 0.5, false);
  block(m, cx + lerp(-2, 2, rng()), cy - 1, 2.5, 2.5, K("building"));   // донжон
  for(let k = 0; k < rint(rng, 2, 4); k++) block(m, cx + lerp(-hw + 4, hw - 4, rng()), cy + (rng() < 0.5 ? -hh + 2.5 : hh - 2.5), 2, 1, K("building"));   // казармы у стен
  gateSides(p).forEach((side, i) => {
    const g = sidePoint(side, cx, cy, hw - 0.5, hh - 0.5);
    if(i === 0) gatehouse(m, g);
    makeGate(m, g, 1, [K("wall"), K("tower")], true);
  });
}
function genConcentric(m, rng, p){
  const ow = 16, oh = 13;   // внешнее кольцо 160 × 130 м
  const gap = 4;            // полоса 20 м между кольцами (К28: 15–20 м)
  const iw = ow - gap, ih = oh - gap;
  const {cx, cy} = fortBase(m, rng, p, ow, 1);
  if(p.moat) moatAround(m, rectShape(cx, cy, ow + 6, oh + 6), rectShape(cx, cy, ow + 3, oh + 3));
  stoneWalls(m, cx, cy, ow, oh);
  fillShape(m, rectShape(cx, cy, ow - 1, oh - 1), K("field"));   // полоса под обстрелом
  const inner = stoneWalls(m, cx, cy, iw, ih);
  // внутреннее кольцо выше внешнего (К28): двор на уровень выше
  const base = p.hill ? 2 : 0;
  eachCell(m, (x, y, i) => { if(inner(x, y)) m.z[i] = Math.max(m.z[i], base + 1); });
  cornerTowers(m, cx, cy, ow - 0.5, oh - 0.5, true);
  cornerTowers(m, cx, cy, iw - 0.5, ih - 0.5, false);
  block(m, cx, cy, 3, 3, K("building"));   // донжон
  // ворота внешние и внутренние — не на одной линии
  gateSides(p).forEach((side, i) => {
    const go = sidePoint(side, cx, cy, ow - 0.5, oh - 0.5, -0.45);
    const gi = sidePoint(side, cx, cy, iw - 0.5, ih - 0.5, 0.45);
    gatehouse(m, go); gatehouse(m, gi);
    makeGate(m, go, 1, [K("wall"), K("tower")], true);
    makeGate(m, gi, 1, [K("wall"), K("tower")], false);
  });
}

const GENERATORS = {field: genField, forest: genForest, hills: genHills, river: genRiver, desert: genDesert,
                    palisade: genPalisade, castle: genCastle, concentric: genConcentric};
