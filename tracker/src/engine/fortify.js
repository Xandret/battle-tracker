// ═══════════ fortify.js — укрепления: участки стен с прочностью (этап 6б, Г46) — ЧЕРНОВИК ДО ГМа ═══════════
// Стена, частокол, ворота и башни — не просто клетки местности, а участки с прочностью. Слой участков
// (m.s, m.forts) лежит в карте и в сохранении (terrain.js); в карте — только полученный урон, сама прочность —
// из rules.js (siege.hp): поправка чисел ГМом не требует пересобирать карты.
//
// Нарезка (Ш1): связный кусок стены или частокола режется по пути вдоль него на равные участки не длиннее
// siege.sectionM — так стена между башнями делится сама, а башни и ворота — каждые отдельным участком.
// Работает одинаково на сгенерированной и на нарисованной вручную карте.
// Пролом (Ш2): прочность участка обнулилась — breachM метров стены у точки попадания становятся проломом;
// остаток удара идёт дальше, и каждая следующая полная прочность — ещё пролом. Ворота выбиваются, башня
// рушится целиком. Пролом — обычная местность «пролом»: путь, обзор и бой видят его сами.
import { TERRAIN_BY_KEY, FORT_KINDS, MAX_SECTIONS, N8X, N8Y, fortCode, bfs8, indexSections } from "./terrain.js";

const ID = key => TERRAIN_BY_KEY[key].id;
const WALL = ID("wall"), PALISADE = ID("palisade"), GATE = ID("gate"), TOWER = ID("tower"), BREACH = ID("breach");
const isFort = t => t === WALL || t === PALISADE || t === GATE || t === TOWER;

// Строит участки заново. prev — прежние {s, forts} (после правки в редакторе): клетки-проломы прежних участков
// остаются в своих участках, а урон и проломы переходят к новому участку, если большая часть его клеток была
// в одном прежнем участке того же вида.
export function buildSections(m, R, prev = null){
  const n = m.w * m.h;
  const prevById = prev && prev.s && prev.forts ? new Map(prev.forts.map(f => [f.id, f])) : null;
  const grp = new Uint8Array(n);
  for(let i = 0; i < n; i++){
    const t = m.t[i];
    if(isFort(t)) grp[i] = t;
    else if(t === BREACH && prevById && prev.s[i]){ const f = prevById.get(prev.s[i]); if(f) grp[i] = fortCode(f.kind); }
  }
  const comp = new Int32Array(n).fill(-1), own = new Int32Array(n).fill(-1), dist = new Int32Array(n).fill(-1);
  const bucket = new Int32Array(n), queue = new Int32Array(n);
  const L = Math.max(1, Math.round(R.siege.sectionM / m.cell));
  const pieces = [];   // {kind, cells} — будущие участки
  let token = 0;
  // Кусок стены режется по пути вдоль него: от дальней клетки (дальней от первой) — отрезки на равные части
  // не длиннее L; связная часть одного отрезка — участок. Кольцо сходится «рукавами» в начале и в конце пути —
  // такой участок длиннее L и режется тем же способом ещё раз.
  function chunk(cells, kind){
    const tag = ++token;
    for(const i of cells) own[i] = tag;
    const inSet = j => own[j] === tag;
    cells.sort((p, q) => p - q);
    const a = bfs8(m, cells[0], inSet, dist, queue);
    for(const i of cells) dist[i] = -1;
    const b = bfs8(m, a.far, inSet, dist, queue);
    const D = b.farD;
    if(D + 1 <= L){ for(const i of cells) dist[i] = -1; pieces.push({kind, cells}); return; }
    const parts = Math.ceil((D + 1) / L);
    for(const i of cells){ bucket[i] = Math.floor(dist[i] * parts / (D + 1)); dist[i] = -1; }
    const split = [];
    for(const i of cells){
      if(own[i] !== tag) continue;
      const ptag = ++token, piece = [i];
      own[i] = ptag;
      for(let head = 0; head < piece.length; head++){
        const p = piece[head], x = p % m.w, y = (p - x) / m.w;
        for(let k = 0; k < 8; k++){
          const nx = x + N8X[k], ny = y + N8Y[k];
          if(nx < 0 || ny < 0 || nx >= m.w || ny >= m.h) continue;
          const j = ny * m.w + nx;
          if(own[j] === tag && bucket[j] === bucket[i]){ own[j] = ptag; piece.push(j); }
        }
      }
      split.push(piece);
    }
    for(const piece of split) chunk(piece, kind);
  }
  let compId = 0;
  for(let i0 = 0; i0 < n; i0++){
    const g = grp[i0];
    if(!g || comp[i0] !== -1) continue;
    // связный кусок одного вида (восемь соседей)
    const c = compId++, cells = [i0];
    comp[i0] = c;
    for(let head = 0; head < cells.length; head++){
      const i = cells[head], x = i % m.w, y = (i - x) / m.w;
      for(let k = 0; k < 8; k++){
        const nx = x + N8X[k], ny = y + N8Y[k];
        if(nx < 0 || ny < 0 || nx >= m.w || ny >= m.h) continue;
        const j = ny * m.w + nx;
        if(grp[j] === g && comp[j] === -1){ comp[j] = c; cells.push(j); }
      }
    }
    if(g === TOWER) pieces.push({kind: "tower", cells: cells.sort((p, q) => p - q)});
    else if(g === GATE) pieces.push({kind: gateKind(m, cells), cells: cells.sort((p, q) => p - q)});
    else chunk(cells, g === PALISADE ? "palisade" : "wall");
  }
  // номера — по первой клетке участка: не зависят от порядка нарезки
  pieces.sort((p, q) => p.cells[0] - q.cells[0]);
  const s = new Uint16Array(n), forts = [];
  for(const p of pieces){
    if(forts.length >= MAX_SECTIONS) break;
    const id = forts.length + 1;
    forts.push({id, kind: p.kind, dmg: 0, breaches: 0});
    for(const i of p.cells) s[i] = id;
  }
  if(prevById){
    // урон и проломы — от прежнего участка, где лежит большинство клеток (при равенстве — меньший номер)
    const votes = forts.map(() => new Map());
    for(let i = 0; i < n; i++) if(s[i] && prev.s[i]) { const v = votes[s[i] - 1]; v.set(prev.s[i], (v.get(prev.s[i]) || 0) + 1); }
    forts.forEach((f, k) => {
      let best = 0, bestN = 0;
      for(const [id, cnt] of votes[k]) if(cnt > bestN || (cnt === bestN && id < best)){ best = id; bestN = cnt; }
      const old = best && prevById.get(best);
      if(old && old.kind === f.kind){ f.dmg = old.dmg; f.breaches = old.breaches; if(old.holder !== undefined) f.holder = old.holder; }
    });
  }
  m.s = s; m.forts = forts;
  indexSections(m);
  return m.forts;
}
// Ворота в частоколе — деревянные, в каменной стене — окованные: по клеткам стены и частокола не дальше
// GATE_LOOK клеток от ворот (у замка по бокам ворот надвратные башни — стена начинается за ними)
const GATE_LOOK = 4;
function gateKind(m, cells){
  let x0 = m.w, y0 = m.h, x1 = -1, y1 = -1;
  for(const i of cells){ const x = i % m.w, y = (i - x) / m.w; x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
  let pal = 0, wall = 0;
  for(let y = Math.max(0, y0 - GATE_LOOK); y <= Math.min(m.h - 1, y1 + GATE_LOOK); y++)
    for(let x = Math.max(0, x0 - GATE_LOOK); x <= Math.min(m.w - 1, x1 + GATE_LOOK); x++){
      const t = m.t[y * m.w + x];
      if(t === PALISADE) pal++; else if(t === WALL) wall++;
    }
  return wall > pal ? "gateIron" : "gateWood";
}
// Участков ещё нет (старая партия, новая карта) — построить
export function ensureSections(m, R){
  if(m && (!m.s || !m.forts)) buildSections(m, R);
  return m;
}

// ── чтение ──
export const sectionMax = (f, R) => R.siege.hp[f.kind];
export const sectionHp = (f, R) => f.up > 0 ? Math.max(0, sectionMax(f, R) - f.dmg) : 0;
export const sectionName = f => `участок №${f.id} (${FORT_KINDS[f.kind]})`;
export const getSection = (m, id) => m && m.forts ? m.forts.find(f => f.id === id) || null : null;
// Участок под точкой (fx, fy — доли карты 0…1) или null
export function sectionAt(m, fx, fy){
  if(!m || !m.s) return null;
  const x = Math.min(m.w - 1, Math.max(0, Math.floor(fx * m.w))), y = Math.min(m.h - 1, Math.max(0, Math.floor(fy * m.h)));
  return getSection(m, m.s[y * m.w + x]);
}

// ── урон и починка: меняют карту на месте (как кисти), возвращают строки журнала ──
// at — точка попадания в клетках {x, y}; без неё — центр участка.
export function damageSection(m, id, amount, at, R){
  const f = getSection(m, id);
  if(!f) return null;
  const max = sectionMax(f, R), name = sectionName(f), lines = [], cells = [];
  let up = [];
  const code = fortCode(f.kind);
  for(let i = 0; i < m.s.length; i++) if(m.s[i] === id && m.t[i] === code) up.push(i);
  if(!up.length) return {lines: [`${capFirst(name)} уже разрушен`], cells, opened: 0, destroyed: true};
  amount = Math.max(0, amount);
  const hp0 = max - f.dmg;
  f.dmg += amount;
  let opened = 0;
  const whole = f.kind === "tower" || f.kind === "gateWood" || f.kind === "gateIron";
  const ax = at ? at.x : f.cx, ay = at ? at.y : f.cy;
  while(f.dmg >= max && up.length){
    let take = up;
    if(!whole){
      // пролом: breachM метров вдоль стены × толщина стены — ближайшие к попаданию целые клетки
      const k = Math.ceil(R.siege.breachM / m.cell) * Math.max(1, Math.round(f.n / f.len));
      take = up.map(i => { const dx = i % m.w + 0.5 - ax, dy = Math.floor(i / m.w) + 0.5 - ay; return [i, dx * dx + dy * dy]; })
               .sort((p, q) => p[1] - q[1] || p[0] - q[0]).slice(0, k).map(p => p[0]);
    }
    for(const i of take) m.t[i] = BREACH;
    cells.push(...take);
    opened++; f.breaches++;
    f.dmg -= max;
    up = up.filter(i => m.t[i] !== BREACH);
  }
  f.up = up.length;
  if(!f.up) f.dmg = 0;
  const hp1 = sectionHp(f, R);
  lines.push(`🏰 ${capFirst(name)}: удар −${fmtHp(amount)} прочности, было ${fmtHp(hp0)} из ${max} · черновик`);
  if(opened){
    const meters = Math.round(cells.length / Math.max(1, Math.round(f.n / f.len)) * m.cell);
    lines.push(f.kind === "tower" ? `💥 Башня рухнула — на её месте пролом`
             : whole ? `💥 Ворота выбиты — проход открыт`
             : `💥 Пролом${opened > 1 ? ` ×${opened}` : ""}: ${meters} м стены обрушено`);
  }
  lines.push(f.up ? `Прочность участка №${f.id}: ${fmtHp(hp1)} из ${max}${f.breaches ? ` · проломов ${f.breaches}` : ""}`
                  : `☠ ${capFirst(name)} разрушен целиком`);
  return {lines, cells, opened, destroyed: !f.up};
}
// Починка: прочность снова полная, проломы остаются (их заделывает осада — этап 6в)
export function repairSection(m, id, R){
  const f = getSection(m, id);
  if(!f || !f.up) return null;
  const before = sectionHp(f, R);
  f.dmg = 0;
  return {lines: [`🔧 ${capFirst(sectionName(f))}: прочность ${fmtHp(before)} → ${sectionMax(f, R)} · черновик`]};
}
const capFirst = s => s[0].toUpperCase() + s.slice(1);
const fmtHp = v => String(Math.round(v * 10) / 10).replace(".", ",");
