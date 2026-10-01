// ═══════════ terrain.js — местность карты: клетки, высота, правка ═══════════
// Карта — сетка клеток по CELL_M метров (К13). Два слоя одинакового размера:
//   t — вид местности (код из TERRAIN, 0 — «не задано»: сквозь клетку видна картинка),
//   z — высота, уровни 0…MAX_HEIGHT (К16).
// Здесь только данные и операции над ними — рисует интерфейс, что клетка значит в бою — rules.js.

export const CELL_M = 5;
export const MAX_HEIGHT = 3;
export const MAX_CELLS = 2500000;   // 10 × 1,25 км или 7 × 1,8 км — хватит на любое поле боя

// Коды не менять: они лежат в сохранениях. Новые виды — только в конец.
export const TERRAIN = [
  {id: 1,  key: "field",    name: "Поле",            group: "Открытое"},
  {id: 2,  key: "road",     name: "Дорога",          group: "Открытое"},
  {id: 3,  key: "sand",     name: "Песок",           group: "Открытое"},
  {id: 4,  key: "snow",     name: "Снег",            group: "Открытое"},
  {id: 5,  key: "shrub",    name: "Кустарник",       group: "Заросли"},
  {id: 6,  key: "forest",   name: "Лес",             group: "Заросли"},
  {id: 7,  key: "water",    name: "Глубокая вода",   group: "Вода"},
  {id: 8,  key: "ford",     name: "Брод",            group: "Вода"},
  {id: 9,  key: "bridge",   name: "Мост",            group: "Вода"},
  {id: 10, key: "swamp",    name: "Болото",          group: "Вода"},
  {id: 11, key: "rocks",    name: "Скалы",           group: "Камень"},
  {id: 12, key: "wall",     name: "Стена",           group: "Постройки"},
  {id: 13, key: "gate",     name: "Ворота",          group: "Постройки"},
  {id: 14, key: "tower",    name: "Башня",           group: "Постройки"},
  {id: 15, key: "palisade", name: "Частокол",        group: "Постройки"},
  {id: 16, key: "moat",     name: "Ров",             group: "Постройки"},
  {id: 17, key: "trench",   name: "Окоп / вал",      group: "Постройки"},
  {id: 18, key: "building", name: "Здание",          group: "Постройки"},
  {id: 19, key: "pavement", name: "Мостовая",        group: "Постройки"},
  {id: 20, key: "breach",   name: "Пролом",          group: "Служебное", system: true},   // ставит трекер (6б)
];
export const TERRAIN_BY_ID = Object.fromEntries(TERRAIN.map(t => [t.id, t]));
export const TERRAIN_BY_KEY = Object.fromEntries(TERRAIN.map(t => [t.key, t]));
export const terrainName = id => (TERRAIN_BY_ID[id] || {name: "не задано"}).name;

// ── создание ──
export function createTerrain(widthM, heightM, fill = 0){
  const w = Math.max(4, Math.round(widthM / CELL_M)), h = Math.max(4, Math.round(heightM / CELL_M));
  if(w * h > MAX_CELLS) throw new Error(`карта слишком велика: ${w}×${h} клеток, предел ${MAX_CELLS}`);
  const t = new Uint8Array(w * h);
  if(fill) t.fill(fill);
  return {v: 1, cell: CELL_M, w, h, t, z: new Uint8Array(w * h), meta: {}};
}
export const mapWidthM = m => m.w * m.cell;
export const mapHeightM = m => m.h * m.cell;

// ── сохранение: повторы подряд сжимаются, «значение.длина» в base36 через запятую ──
// Поле 800 × 600 с лесом и рекой — это десятки килобайт, а не полмегабайта.
export function encodeLayer(arr){
  const out = [];
  let v = arr[0], n = 0;
  for(let i = 0; i < arr.length; i++){
    if(arr[i] === v){ n++; continue; }
    out.push(v.toString(36) + "." + n.toString(36));
    v = arr[i]; n = 1;
  }
  if(arr.length) out.push(v.toString(36) + "." + n.toString(36));
  return out.join(",");
}
export function decodeLayer(str, len, max, Type = Uint8Array){
  const arr = new Type(len);
  if(!str) return arr;
  let p = 0;
  for(const run of String(str).split(",")){
    const [vs, ns] = run.split(".");
    const v = parseInt(vs, 36), n = parseInt(ns, 36);
    if(!Number.isFinite(v) || !Number.isFinite(n) || n < 0 || p + n > len) throw new Error("повреждён слой карты");
    arr.fill(Math.min(Math.max(v, 0), max), p, p + n);
    p += n;
  }
  if(p !== len) throw new Error("повреждён слой карты: не хватает клеток");
  return arr;
}
export function serializeTerrain(m){
  if(!m) return null;
  const o = {v: 1, cell: m.cell, w: m.w, h: m.h, t: encodeLayer(m.t), z: encodeLayer(m.z), meta: Object.assign({}, m.meta)};
  // участки укреплений (6б): слой номеров и состояние; до v30.8 их нет — построятся при надобности
  if(m.s && m.forts){ o.s = encodeLayer(m.s); o.forts = m.forts.map(f => ({id: f.id, kind: f.kind, dmg: f.dmg, breaches: f.breaches})); }
  return o;
}
// Неизвестное или битое — null: партия всё равно откроется, просто без местности
export function deserializeTerrain(o){
  try{
    if(!o || typeof o !== "object") return null;
    const w = Math.round(+o.w), h = Math.round(+o.h);
    if(!(w >= 4 && h >= 4 && w * h <= MAX_CELLS)) return null;
    const maxId = TERRAIN[TERRAIN.length - 1].id;
    const m = {v: 1, cell: +o.cell > 0 ? +o.cell : CELL_M, w, h,
            t: decodeLayer(o.t, w * h, maxId), z: decodeLayer(o.z, w * h, MAX_HEIGHT),
            meta: o.meta && typeof o.meta === "object" ? Object.assign({}, o.meta) : {}};
    readSections(m, o);
    return m;
  }catch(e){ return null; }
}
// Битые участки не роняют карту: местность откроется, участки построятся заново
function readSections(m, o){
  if(typeof o.s !== "string" || !Array.isArray(o.forts)) return;
  try{
    const s = decodeLayer(o.s, m.w * m.h, MAX_SECTIONS, Uint16Array);
    const forts = [], seen = new Set();
    for(const f of o.forts){
      const id = Math.round(+(f && f.id));
      if(!(id >= 1 && id <= MAX_SECTIONS) || seen.has(id) || !Object.prototype.hasOwnProperty.call(FORT_KINDS, f.kind)) continue;
      seen.add(id);
      const dmg = +f.dmg, br = Math.round(+f.breaches);
      forts.push({id, kind: f.kind, dmg: Number.isFinite(dmg) && dmg > 0 ? dmg : 0, breaches: br > 0 ? br : 0});
    }
    m.s = s; m.forts = forts.sort((a, b) => a.id - b.id);
    indexSections(m);
  }catch(e){ delete m.s; delete m.forts; }
}
export const cloneTerrain = m => {
  if(!m) return m;
  const c = {v: 1, cell: m.cell, w: m.w, h: m.h, t: m.t.slice(), z: m.z.slice(), meta: Object.assign({}, m.meta)};
  if(m.s && m.forts){ c.s = m.s.slice(); c.forts = m.forts.map(f => Object.assign({}, f)); }
  return c;
};

// ── участки укреплений (6б, Г46): слой s — номер участка в клетке (0 — нет), m.forts — список ──
// Здесь только данные: что лежит в карте и как это читать. Нарезка и урон — fortify.js (по правилам).
export const MAX_SECTIONS = 65535;
export const FORT_KINDS = {palisade: "частокол", wall: "каменная стена", gateWood: "деревянные ворота",
                           gateIron: "окованные ворота", tower: "башня"};
// Код местности, которым стоит целая клетка участка (пролом — код breach)
export const fortCode = kind => TERRAIN_BY_KEY[kind === "gateWood" || kind === "gateIron" ? "gate" : kind].id;
// восемь соседей клетки: сначала верхний ряд, потом свой, потом нижний
export const N8X = [-1, 0, 1, -1, 1, -1, 0, 1];
export const N8Y = [-1, -1, -1, 0, 0, 1, 1, 1];
// Обход в ширину по восьми соседям внутри множества inSet(j); dist — расстояния в шагах (−1 — не дошли).
// Возвращает самую дальнюю клетку (при равенстве — с меньшим номером) и её расстояние.
export function bfs8(m, from, inSet, dist, queue){
  let head = 0, tail = 0, far = from, farD = 0;
  dist[from] = 0; queue[tail++] = from;
  while(head < tail){
    const i = queue[head++], d = dist[i], x = i % m.w, y = (i - x) / m.w;
    if(d > farD || (d === farD && i < far)){ far = i; farD = d; }
    for(let k = 0; k < 8; k++){
      const nx = x + N8X[k], ny = y + N8Y[k];
      if(nx < 0 || ny < 0 || nx >= m.w || ny >= m.h) continue;
      const j = ny * m.w + nx;
      if(dist[j] !== -1 || !inSet(j)) continue;
      dist[j] = d + 1; queue[tail++] = j;
    }
  }
  return {far, farD, count: tail};
}
// Разметка участков: клеток n, целых up, длина вдоль стены len (клеток), центр cx, cy (в клетках)
export function indexSections(m){
  const byId = new Map(m.forts.map(f => [f.id, f]));
  const cells = new Map();
  for(const f of m.forts){ f.n = 0; f.up = 0; f.len = 1; f.cx = 0; f.cy = 0; cells.set(f.id, []); }
  for(let i = 0; i < m.s.length; i++){
    const id = m.s[i]; if(!id) continue;
    const f = byId.get(id); if(!f){ m.s[i] = 0; continue; }
    const x = i % m.w, y = (i - x) / m.w;
    f.n++; f.cx += x; f.cy += y;
    if(m.t[i] === fortCode(f.kind)) f.up++;
    cells.get(id).push(i);
  }
  const dist = new Int32Array(m.s.length).fill(-1), queue = new Int32Array(m.s.length);
  for(const f of m.forts){
    const list = cells.get(f.id);
    if(!f.n){ f.cx = f.cy = 0; continue; }
    f.cx = f.cx / f.n + 0.5; f.cy = f.cy / f.n + 0.5;
    // длина — поперечник участка: от самой дальней клетки до самой дальней от неё, +1
    const inSec = j => m.s[j] === f.id;
    const a = bfs8(m, list[0], inSec, dist, queue);
    for(const i of list) dist[i] = -1;
    const b = bfs8(m, a.far, inSec, dist, queue);
    for(const i of list) dist[i] = -1;
    f.len = b.farD + 1;
  }
  m.forts = m.forts.filter(f => f.n > 0);
}

// ── чтение ──
// fx, fy — доли карты 0…1 (так фишки и хранят положение: mapX/100)
export function cellAt(m, fx, fy){
  const x = Math.min(m.w - 1, Math.max(0, Math.floor(fx * m.w)));
  const y = Math.min(m.h - 1, Math.max(0, Math.floor(fy * m.h)));
  const i = y * m.w + x;
  return {x, y, t: m.t[i], z: m.z[i]};
}
export const hasTerrain = m => !!m && m.t.some(v => v !== 0);

// ── правка: координаты в клетках (дробные), радиус в клетках; возвращают число изменённых клеток ──
const layerOf = (m, layer) => layer === "z" ? m.z : m.t;
const clampValue = (layer, v) => layer === "z" ? Math.min(MAX_HEIGHT, Math.max(0, v | 0)) : (v | 0);
function setCell(m, arr, x, y, v){
  if(x < 0 || y < 0 || x >= m.w || y >= m.h) return 0;
  const i = y * m.w + x;
  if(arr[i] === v) return 0;
  arr[i] = v;
  return 1;
}
export function paintDisc(m, layer, cx, cy, r, value){
  const arr = layerOf(m, layer), v = clampValue(layer, value);
  const rr = Math.max(0.5, r), r2 = rr * rr;
  let n = 0;
  for(let y = Math.floor(cy - rr); y <= Math.ceil(cy + rr); y++)
    for(let x = Math.floor(cx - rr); x <= Math.ceil(cx + rr); x++){
      const dx = x + 0.5 - cx, dy = y + 0.5 - cy;
      if(dx * dx + dy * dy <= r2) n += setCell(m, arr, x, y, v);
    }
  return n;
}
// Толстая линия — «капсула»: все клетки не дальше r от отрезка (стены, дороги, реки)
export function paintSegment(m, layer, x0, y0, x1, y1, r, value){
  const arr = layerOf(m, layer), v = clampValue(layer, value);
  const rr = Math.max(0.5, r), r2 = rr * rr;
  const dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
  let n = 0;
  for(let y = Math.floor(Math.min(y0, y1) - rr); y <= Math.ceil(Math.max(y0, y1) + rr); y++)
    for(let x = Math.floor(Math.min(x0, x1) - rr); x <= Math.ceil(Math.max(x0, x1) + rr); x++){
      const px = x + 0.5, py = y + 0.5;
      const k = len2 ? Math.max(0, Math.min(1, ((px - x0) * dx + (py - y0) * dy) / len2)) : 0;
      const ex = px - (x0 + k * dx), ey = py - (y0 + k * dy);
      if(ex * ex + ey * ey <= r2) n += setCell(m, arr, x, y, v);
    }
  return n;
}
// Прямоугольник по двум углам; outline > 0 — только контур такой толщины в клетках (стены замка)
export function paintRect(m, layer, x0, y0, x1, y1, value, outline = 0){
  const arr = layerOf(m, layer), v = clampValue(layer, value);
  const l = Math.floor(Math.min(x0, x1)), r = Math.floor(Math.max(x0, x1));
  const t = Math.floor(Math.min(y0, y1)), b = Math.floor(Math.max(y0, y1));
  const th = Math.max(0, Math.round(outline));
  let n = 0;
  for(let y = t; y <= b; y++)
    for(let x = l; x <= r; x++){
      if(th && x >= l + th && x <= r - th && y >= t + th && y <= b - th) continue;
      n += setCell(m, arr, x, y, v);
    }
  return n;
}
// Заливка связной области одного значения (соседи по сторонам)
export function floodFill(m, layer, x, y, value){
  const arr = layerOf(m, layer), v = clampValue(layer, value);
  x = Math.floor(x); y = Math.floor(y);
  if(x < 0 || y < 0 || x >= m.w || y >= m.h) return 0;
  const from = arr[y * m.w + x];
  if(from === v) return 0;
  const stack = [y * m.w + x];
  let n = 0;
  while(stack.length){
    const i = stack.pop();
    if(arr[i] !== from) continue;
    arr[i] = v; n++;
    const cx = i % m.w, cy = (i - cx) / m.w;
    if(cx > 0) stack.push(i - 1);
    if(cx < m.w - 1) stack.push(i + 1);
    if(cy > 0) stack.push(i - m.w);
    if(cy < m.h - 1) stack.push(i + m.w);
  }
  return n;
}
