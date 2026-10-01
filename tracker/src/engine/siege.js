// ═══════════ siege.js — осадные орудия (этап 6б, Ш3–Ш4, Г47, Г49) — ЧЕРНОВИК ДО ГМа ═══════════
// Машина — фишка-батарея из count одинаковых орудий с расчётом (crew человек на всех). Числа каждого вида —
// в rules.js (siege.engines). Здесь — выстрел и его последствия; движок ничего не пишет в состояние,
// а возвращает патчи: машине (machine), отрядам (patches). Участок стены меняется на месте, как кисть (fortify.js).
//
// Залп (Г47): стреляют орудия, на которые хватает расчёта; каждое — бросок d100 на попадание: шанс по дистанции
// между «от» и «до» плюс выучка расчёта. Порох перед выстрелом бросает на разрыв ствола: разорвало — орудие и его
// расчёт потеряны. Попадание по стене снимает фиксированную прочность; по людям — d(die) за попадание, ÷ броня,
// как стрельба за столом, дальше потери, летальность и БД — те же, что у стола; порох добавляет шок БД.
// Маг-батарея — один бросок на весь залп, уносит долю численности цели почти без учёта брони, брызги — соседям.
// Таран бьёт без броска. Меньше людей — дольше перезарядка (Г49).
import { clamp, r1, rollDie } from "./util.js";
import { applyMoraleChange } from "./morale.js";
import { casualtyPatch } from "./combat.js";
import { FORT_KINDS, TERRAIN_BY_ID, cellAt, fortCode } from "./terrain.js";
import { getSection, damageSection } from "./fortify.js";
import { unitCenter, unitCorners, polyGap, groundUnder } from "./battlemap.js";

export const engineOf = (key, R) => (key && Object.prototype.hasOwnProperty.call(R.siege.engines, key) ? R.siege.engines[key] : null);

// Новая батарея: count орудий с полным расчётом, выучка 50; у маг-пушки — навык мага 10 (поле, 1–20)
export function makeMachine(key, count, R, extra = {}){
  const e = engineOf(key, R);
  if(!e) throw new Error("нет такого орудия: " + key);
  const n = Math.max(1, Math.round(+count) || 1);
  return Object.assign({engine: key, name: e.name, count: n, countFull: n, crew: n * e.crew, exp: 50,
                        mageSkill: e.magic ? 10 : 0, ready: 0, deployLeft: 0, dmg: 0}, extra);
}
// Сколько орудий может стрелять: на каждое нужен хотя бы minCrew
export function firingGuns(m, R){
  const e = engineOf(m.engine, R);
  if(!e || m.count <= 0 || m.crew <= 0) return 0;
  return Math.min(m.count, Math.floor(m.crew / e.minCrew));
}
// Перезарядка, ходов: при полном расчёте — reload, при неполном — во столько раз дольше, во сколько людей меньше
export function machineReload(m, R){
  const e = engineOf(m.engine, R);
  if(!e || !e.reload || m.count <= 0 || m.crew <= 0) return 0;
  return Math.max(e.reload, Math.ceil(e.reload * m.count * e.crew / m.crew));
}
// Шанс попасть: по дистанции между «от» и «до» плюс выучка (у маг-пушки — навык мага); вне дальности — null
function chanceParts(m, vs, dist, R){
  const S = R.siege, e = engineOf(m.engine, R);
  if(!e || e.ram || e.tower) return null;
  const [a, b] = e.range;
  if(!(dist >= a && dist <= b)) return null;
  const [near, far] = vs === "wall" ? e.hitWall : e.hitTroops;
  const base = near + (far - near) * (b > a ? (dist - a) / (b - a) : 0);
  const bonus = e.magic ? (m.mageSkill - 10) * S.magic.skillK : (m.exp - 50) * S.skillK;
  return {base, bonus, chance: clamp(Math.round(base + bonus), S.hitMin, S.hitMax)};
}
export function hitChance(m, vs, dist, R){
  const p = chanceParts(m, vs, dist, R);
  return p ? p.chance : null;
}

// Потери от урона — как в конце удара за столом: округление до солдат и бросок летальности
function siegeCasualties(def, dmg, L, ctx){
  const R = ctx.rules;
  const casualties = Math.min(def.soldiers, Math.max(0, Math.round(dmg)));
  L.push(`Потери «${def.name}»: ${casualties} солдат`);
  let killed = 0, wounded = 0;
  if(casualties > 0){
    const Lt = R.lethality;
    const lroll = rollDie(ctx.rng, Lt.die);
    const expDiv = Math.max(1, def.exp) / Lt.expDiv;
    const pct = clamp(Lt.base + lroll / expDiv, 0, 100);
    killed = Math.round(casualties * pct / 100);
    wounded = casualties - killed;
    L.push(`Из них: ${killed} убитых, ${wounded} раненых (летальность ${Lt.base}% + d${Lt.die}(${lroll}) / (${Math.max(1, def.exp)}/${Lt.expDiv}) = ${r1(pct)}%)`);
  }
  return {casualties, killed, wounded};
}

// Залп по участку стены или по отряду.
// target — {section: {map, id, at: {x, y} в клетках | null}} или {unit, splash: [{unit, gap}]}; gap — м от края цели.
// opts — {dist: м до цели, los: видна ли цель (для прямой наводки), coverPct: укрытие цели, % (только прямая наводка)}.
// Возвращает {ok, title, lines, tone, machine: патч машины, patches: [{id, patch}], section: итог удара по стене, stats}.
export function siegeVolley(m, target, opts, ctx){
  const R = ctx.rules, S = R.siege, e = engineOf(m.engine, R);
  const fail = lines => ({ok: false, title: "Выстрел невозможен", lines, tone: "danger", machine: null, patches: [], section: null, stats: null});
  if(!e) return fail([`Неизвестное орудие: ${m.engine}`]);
  const who = `«${m.name}»`;
  if(e.tower) return fail([`${who} не стреляет: осадная башня ведёт на стену (бой на стене — следующим шагом)`]);
  if(m.count <= 0) return fail([`${who}: все орудия выбиты`]);
  if(e.magic && !(m.mageSkill > 0)) return fail([`${who}: нет мага — батарея молчит. Захваченной батарее мага назначает ГМ`]);
  const guns = firingGuns(m, R);
  if(!guns) return fail([`${who}: расчёта (${m.crew} чел.) не хватает даже на одно орудие — нужно ${e.minCrew}`]);
  if(m.deployLeft > 0) return fail([`${who} разворачивается после марша — ещё ${m.deployLeft} х.`]);
  if(m.ready > 0) return fail([`${who} перезаряжается — ещё ${m.ready} х.`]);
  const map = target.section ? target.section.map : null;
  const sec = target.section ? getSection(map, target.section.id) : null;
  const def = target.unit || null;
  if(target.section && !sec) return fail(["Нет такого участка стены"]);
  if(!sec && !def) return fail(["Нет цели"]);
  if(sec && !sec.up) return fail([`Участок №${sec.id} (${FORT_KINDS[sec.kind]}) уже разрушен`]);
  if(def && (def.status === "destroyed" || def.soldiers <= 0)) return fail([`«${def.name}» уже уничтожен`]);
  const dist = +opts.dist;
  let parts = null;
  if(e.ram){
    if(!sec) return fail([`${who}: таран бьёт только ворота и стены`]);
    if(!(dist <= e.range[1])) return fail([`${who}: таран должен стоять вплотную (до ${e.range[1]} м), до цели ${Math.round(dist)} м`]);
  } else {
    parts = chanceParts(m, sec ? "wall" : "troops", dist, R);
    if(!parts) return fail([`${who}: цель вне дальности — ${Math.round(dist)} м, орудие бьёт от ${e.range[0]} до ${e.range[1]} м`]);
    if(!e.indirect && opts.los === false) return fail([`${who}: цели не видно — навесом через стены бьют только катапульта, требушет и мортира`]);
  }

  const L = [];
  const targetName = sec ? `участок №${sec.id} (${FORT_KINDS[sec.kind]})` : `«${def.name}»`;
  const title = `${e.magic ? "🔮" : "⚙"} ${m.name} → ${targetName}`;
  L.push(`${e.name}${m.count > 1 ? ` ×${m.count}` : ""}: бьёт ${guns} из ${m.count}${guns < m.count ? ` — расчёта ${m.crew} из ${m.count * e.crew}` : ""} · до цели ${Math.round(dist)} м${e.indirect ? " · навесом" : ""}`);
  if(parts){
    const b = r1(parts.bonus);
    L.push(`Шанс попасть: ${r1(parts.base)}% на ${Math.round(dist)} м${b ? `, ${e.magic ? "навык мага" : "выучка расчёта"} ${b > 0 ? "+" : "−"}${Math.abs(b)}%` : ""} → ${parts.chance}%`);
  }

  // броски на попадание (и на разрыв ствола у пороха)
  let hits = 0, bursts = 0;
  if(e.ram) hits = guns;
  else if(e.magic){
    const r = rollDie(ctx.rng, 100);
    hits = r <= parts.chance ? guns : 0;
    L.push(`Бросок d100: ${r} — ${hits ? "попадание" : "мимо"}`);
  } else {
    const rolls = [];
    for(let g = 0; g < guns; g++){
      if(e.burst){
        const b = rollDie(ctx.rng, 100);
        if(b <= e.burst){ bursts++; rolls.push(`разрыв (${b})`); continue; }
      }
      const r = rollDie(ctx.rng, 100);
      rolls.push(String(r));
      if(r <= parts.chance) hits++;
    }
    L.push(`Броски d100: ${rolls.join(", ")} → попаданий ${hits} из ${guns - bursts}`);
    if(bursts) L.push(`💥 Разрыв ствола (d100 ≤ ${e.burst}): потеряно орудий ${bursts}, погибло ${bursts * e.crew} из расчёта · черновик`);
  }

  const patches = [];
  let section = null, tone = hits ? "attack" : "info";
  if(sec){
    let dmg = hits * e.wall;
    if(e.ram){
      const k = sec.kind === "gateWood" || sec.kind === "gateIron" || sec.kind === "palisade" ? 1 : S.ram.wallK;
      dmg = guns * e.wall * k;
      L.push(`Таран: ${guns} × ${e.wall}${k !== 1 ? ` × ${k} — по камню таран слаб` : ""} = ${r1(dmg)} урона`);
    } else if(hits) L.push(`Урон стене: ${hits} × ${e.wall} = ${hits * e.wall}`);
    if(dmg > 0){
      section = damageSection(map, sec.id, dmg, target.section.at || null, R);
      section.lines.forEach(l => L.push(l));
      if(section.opened) tone = "danger";
    } else L.push("Стена цела");
  } else if(e.magic){
    if(hits) magicFire(m, e, guns, def, target.splash || [], L, patches, ctx);
    else L.push(`Огонь ушёл мимо — «${def.name}» цел`);
  } else if(!hits){
    L.push(`Все мимо — «${def.name}» без потерь`);
  } else {
    const dice = [];
    let sum = 0;
    for(let k = 0; k < hits; k++){ const v = rollDie(ctx.rng, e.die); dice.push(v); sum += v; }
    L.push(`Урон: ${hits} × d${e.die} = ${hits > 1 ? dice.join(" + ") + " = " : ""}${sum}`);
    const Df = R.defense;
    const divisor = Math.max(Df.minDivisor, def.eqDef / Df.rangedEqDiv);
    let dmg = sum / divisor;
    L.push(`Защита цели: урон / (${def.eqDef}/${Df.rangedEqDiv}) → ${sum} / ${r1(divisor)} = ${r1(dmg)}`);
    const cover = +opts.coverPct || 0;
    if(cover > 0 && !e.indirect){
      const before = dmg;
      dmg *= 1 - cover / 100;
      L.push(`Укрытие цели: −${cover}% (${r1(before)} → ${r1(dmg)}) · черновик`);
    } else if(cover > 0) L.push(`Навесом — укрытие цели (${cover}%) не спасает`);
    const res = siegeCasualties(def, dmg, L, ctx);
    const {patch} = casualtyPatch(def, res, L, ctx);
    if(e.shock && patch.status !== "destroyed"){
      const cur = Object.assign({}, def, patch);
      L.push(`💥 Грохот и дым: БД «${def.name}» −${e.shock} сверх потерь · черновик`);
      Object.assign(patch, applyMoraleChange(cur, cur.morale - e.shock, L, R));
    }
    patches.push({id: def.id, patch});
  }

  // машина после залпа: разрывы уносят орудия и людей, перезарядка — по оставшемуся расчёту
  const count = m.count - bursts, crew = Math.max(0, m.crew - bursts * e.crew);
  const ready = count > 0 ? machineReload(Object.assign({}, m, {count, crew}), R) : 0;
  if(count > 0) L.push(`${who}: перезарядка ${ready} х.${ready > e.reload ? ` — людей мало (${crew} из ${count * e.crew})` : ""}`);
  else L.push(`☠ ${who}: все орудия потеряны`);
  if(bursts) tone = "danger";
  return {ok: true, title, lines: L, tone, machine: {count, crew, ready}, patches, section,
          stats: {guns, hits, bursts}};
}

// Маг-батарея: доля численности цели × доля живых орудий, броня — не больше −15%; брызги — соседям, своим тоже
function magicFire(m, e, guns, def, splash, L, patches, ctx){
  const R = ctx.rules, M = R.siege.magic;
  const share = guns / m.countFull;
  const pct = M.killPct[0] + rollDie(ctx.rng, M.killPct[1] - M.killPct[0] + 1) - 1;
  const armorK = 1 - Math.min(M.armorCap, def.eqDef / M.armorDiv);
  const dmg = def.soldiers * pct / 100 * share * armorK;
  L.push(`Огонь батареи: ${pct}% численности × ${guns}/${m.countFull} орудий, броня −${r1((1 - armorK) * 100)}% → ${r1(dmg)} · черновик`);
  const res = siegeCasualties(def, dmg, L, ctx);
  patches.push({id: def.id, patch: casualtyPatch(def, res, L, ctx).patch});
  for(const s of splash){
    const u = s.unit;
    if(!u || u.id === def.id || u.status === "destroyed" || u.soldiers <= 0 || !(s.gap <= M.splashM)) continue;
    const sp = M.splashPct[0] + rollDie(ctx.rng, M.splashPct[1] - M.splashPct[0] + 1) - 1;
    const d = u.soldiers * sp / 100 * share;
    L.push(`Брызги огня — «${u.name}» в ${Math.round(s.gap)} м от цели: ${sp}% × ${guns}/${m.countFull} → ${r1(d)}`);
    const r = siegeCasualties(u, d, L, ctx);
    patches.push({id: u.id, patch: casualtyPatch(u, r, L, ctx).patch});
  }
}

// ── между залпами ──
// Конец хода: перезарядка и развёртывание на ход ближе
export const siegeEndTurn = m => ({ready: Math.max(0, (m.ready || 0) - 1), deployLeft: Math.max(0, (m.deployLeft || 0) - 1)});
// Машина двигалась: в этот ход не стреляет, тяжёлым ещё deploy ходов на развёртывание
export function machineMoved(m, R){
  const e = engineOf(m.engine, R);
  return {deployLeft: e ? 1 + e.deploy : 0};
}
// Удар по самой машине (контрбатарея, ГМ): прочность одного орудия — hp; выбитое орудие уносит свой расчёт
export function hitMachine(m, amount, R){
  const e = engineOf(m.engine, R), L = [];
  let dmg = (m.dmg || 0) + Math.max(0, amount), count = m.count, lost = 0;
  while(dmg >= e.hp && count > 0){ dmg -= e.hp; count--; lost++; }
  const crew = Math.min(m.crew, count * e.crew);
  if(!count) dmg = 0;
  L.push(`⚙ «${m.name}»: удар −${r1(amount)} прочности${lost ? `, выбито орудий ${lost}` : ""} · черновик`);
  L.push(count ? `Осталось орудий ${count} из ${m.countFull}, повреждение ${r1(dmg)} из ${e.hp}` : `☠ «${m.name}» уничтожена`);
  return {patch: {count, crew, dmg}, lines: L, lost};
}
// Прямое попадание по маг-пушке средством, способным её уничтожить (SPEC 6б): d20 ≤ навык мага — пушка гаснет,
// иначе взрыв — 20–50% численности всем ближе explodeM (gap — м от пушки до края отряда), своим тоже
export function magicStrike(m, nearby, ctx){
  const R = ctx.rules, M = R.siege.magic, L = [], patches = [];
  const roll = rollDie(ctx.rng, 20);
  const calm = roll <= m.mageSkill;
  L.push(`🔮 Попадание по «${m.name}»: d20 = ${roll} ${calm ? "≤" : ">"} навык мага ${m.mageSkill}`);
  const count = Math.max(0, m.count - 1);
  if(calm) L.push(`Маг удержал огонь — пушка погасла, взрыва нет`);
  else {
    L.push(`💥 Взрыв маг-пушки — всем в ${M.explodeM} м · черновик`);
    for(const s of nearby){
      const u = s.unit;
      if(!u || u.status === "destroyed" || u.soldiers <= 0 || !(s.gap <= M.explodeM)) continue;
      const p = M.explodePct[0] + rollDie(ctx.rng, M.explodePct[1] - M.explodePct[0] + 1) - 1;
      L.push(`«${u.name}» в ${Math.round(s.gap)} м: ${p}% численности`);
      const r = siegeCasualties(u, u.soldiers * p / 100, L, ctx);
      patches.push({id: u.id, patch: casualtyPatch(u, r, L, ctx).patch});
    }
  }
  const e = engineOf(m.engine, R);
  return {calm, lines: L, patches, machine: {count, crew: Math.min(m.crew, count * e.crew)}};
}
// Захват (Г49): машина переходит к захватившему; маг-батарее нового мага назначает ГМ
export function captureMachine(m, factionId, R){
  const e = engineOf(m.engine, R);
  return {factionId, mageSkill: e && e.magic ? 0 : m.mageSkill, ready: m.ready, deployLeft: m.deployLeft};
}

// ── прицел с карты (Ш4) ──
// Машина — точка (mapX, mapY в % карты). До отряда — до края строя, до участка — до ближайшей целой клетки
// (её центр — точка попадания at, в клетках). Видимость — как у паники: шаги по 5 м, холм выше обоих концов
// и больше sight метров леса или камня закрывают; но клетки самой цели и укрепления, на котором стоит
// машина, не мешают — с башни стреляют. Укрытие — местность под строем цели (rules.map.terrain.cover).
function sightLine(geo, R, ax, ay, za, bx, by, zb, skip){
  const len = Math.hypot(bx - ax, by - ay), n = Math.max(2, Math.ceil(len / 5)), step = len / n;
  const through = {};
  for(let k = 1; k < n; k++){
    const c = cellAt(geo.map, (ax + (bx - ax) * k / n) / geo.W, (ay + (by - ay) * k / n) / geo.H);
    if(skip(c.y * geo.map.w + c.x)) continue;
    if(c.z > Math.max(za, zb)) return {ok: false, why: "холм"};
    const t = c.t && TERRAIN_BY_ID[c.t], r = t && R.map.terrain[t.key];
    if(r && r.sight !== undefined){
      through[t.key] = (through[t.key] || 0) + step;
      if(through[t.key] > r.sight) return {ok: false, why: t.name.toLowerCase()};
    }
  }
  return {ok: true};
}
export function siegeAim(m, target, geo, R){
  const mx = m.mapX / 100 * geo.W, my = m.mapY / 100 * geo.H;
  const map = geo.map || null;
  const mc = map ? cellAt(map, mx / geo.W, my / geo.H) : null;
  const own = map && map.s ? map.s[mc.y * map.w + mc.x] : 0;
  if(target.unit){
    const u = target.unit;
    const dist = polyGap([[mx, my]], unitCorners(u, geo, R));
    if(!map) return {dist, los: {ok: true}, coverPct: 0, coverNote: null, at: null};
    const g = groundUnder(u, geo, R);
    const [bx, by] = unitCenter(u, geo);
    const los = sightLine(geo, R, mx, my, mc.z, bx, by, g.z, i => !!own && map.s[i] === own);
    const cover = g.key ? (R.map.terrain[g.key].cover || 0) : 0;
    return {dist, los, coverPct: cover, coverNote: cover ? `укрытие «${u.name}»: ${g.name.toLowerCase()}` : null, at: null};
  }
  const f = map && target.section ? getSection(map, target.section.id) : null;
  if(!f) return null;
  const cw = geo.W / map.w, ch = geo.H / map.h, code = fortCode(f.kind);
  let best = -1, bestD = Infinity;
  for(const standing of [true, false]){
    for(let i = 0; i < map.s.length; i++){
      if(map.s[i] !== f.id || (standing && map.t[i] !== code)) continue;
      const x = i % map.w, y = (i - x) / map.w;
      const dx = Math.max(x * cw - mx, 0, mx - (x + 1) * cw), dy = Math.max(y * ch - my, 0, my - (y + 1) * ch);
      const d = Math.hypot(dx, dy);
      if(d < bestD){ bestD = d; best = i; }
    }
    if(best >= 0) break;
  }
  const bx0 = best % map.w, by0 = (best - bx0) / map.w;
  const los = sightLine(geo, R, mx, my, mc.z, (bx0 + 0.5) * cw, (by0 + 0.5) * ch, map.z[best],
                        i => map.s[i] === f.id || (!!own && map.s[i] === own));
  return {dist: bestD, los, coverPct: 0, coverNote: null, at: {x: bx0 + 0.5, y: by0 + 0.5}};
}
