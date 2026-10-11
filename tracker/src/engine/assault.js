// ═══════════ assault.js — приступ на стену (этап 6б, Г48, Г50, Ш10–Ш14) — ЧЕРНОВИК ДО ГМа ═══════════
// Отряд идёт на участок стены одним из трёх путей:
//   по лестницам — в бой за ход вступает «лестниц × 10» бойцов; защитник бьёт сверху вниз (×1,2), штурмующие снизу
//                  вверх (×0,9), как высота в 6а; ответным ударом защитник сбрасывает лестницы с шансом (Г48);
//   через осадную башню — своя башня у стены, отряд у башни: за ход вступает «башен × вместимость», высоты нет;
//   в пролом — весь отряд, без высоты; ограничение фронта — 1 отряд на каждые 10 м пролома (Г50).
// На участок за ход — не больше 2 отрядов по лестницам и башням (Г50). Бой — рукопашная стола в пересечённой
// местности (computeStrike), атака расходует атаку отряда, ответ — ответный удар защитника.
// Защитника на участке нет — взобравшиеся занимают его (поле holder у участка — фракция занявших).
// Движок не пишет в состояние: возвращает патчи отрядов и новое число лестниц; holder участка меняет на месте.
import { rollDie } from "./util.js";
import { attackLimit, counterLimit, isCav } from "./units.js";
import { computeStrike, casualtyPatch } from "./combat.js";
import { unitCorners, polyGap } from "./battlemap.js";
import { FORT_KINDS } from "./terrain.js";
import { getSection } from "./fortify.js";
import { engineOf, siegeAim } from "./siege.js";

// Лестниц на отряд по черновику: одна на laddersPer человек
export const laddersFor = (u, R) => Math.floor(Math.max(0, u.soldiers || 0) / R.siege.assault.laddersPer);

// От края строя до ближайшей клетки участка, м
export function sectionGap(u, secId, geo, R){
  const map = geo.map, cw = geo.W / map.w, ch = geo.H / map.h, P = unitCorners(u, geo, R);
  let best = Infinity;
  for(let i = 0; i < map.s.length; i++){
    if(map.s[i] !== secId) continue;
    const x = i % map.w, y = (i - x) / map.w;
    const d = polyGap(P, [[x * cw, y * ch], [(x + 1) * cw, y * ch], [(x + 1) * cw, (y + 1) * ch], [x * cw, (y + 1) * ch]]);
    if(d < best) best = d;
  }
  return best;
}
// Защитники участка: чужие отряды в строю на карте, чей строй ближе defenderReachM к стене — ближние первыми
export function defendersOf(secId, units, attacker, geo, R){
  const reach = R.siege.assault.defenderReachM;
  return units
    .filter(u => u.onMap && u.status === "active" && u.soldiers > 0 && u.id !== attacker.id && !(attacker.factionId && u.factionId === attacker.factionId))
    .map(u => ({unit: u, gap: sectionGap(u, secId, geo, R)}))
    .filter(x => x.gap <= reach)
    .sort((a, b) => a.gap - b.gap || a.unit.id - b.unit.id);
}
// Сколько своих осадных башен у участка: башня не дальше towerReachM от стены и от строя отряда
export function towersAt(secId, machines, A, geo, R){
  const reach = R.siege.assault.towerReachM;
  let n = 0;
  for(const m of machines){
    const e = engineOf(m.engine, R);
    if(!e || !e.tower || !(m.count > 0) || !m.onMap || (A.factionId && m.factionId !== A.factionId)) continue;
    const wall = siegeAim(m, {section: {id: secId}}, geo, R);
    if(!wall || wall.dist > reach) continue;
    if(siegeAim(m, {unit: A}, geo, R).dist > reach) continue;
    n += m.count;
  }
  return n;
}

// Приступ. defenders — из defendersOf (бьётся ближний); opts — {via: "ladders" | "tower" | "breach",
// gap: м от строя до участка, ladders: лестниц у отряда, towers: башен у участка (towersAt),
// engaged: id отрядов, уже штурмовавших этот участок в этом ходу, fatigueMode}.
// Возвращает {ok, title, lines, tone, patches: [{id, patch}], ladders: лестниц осталось, captured}.
export function assaultWall(A, map, secId, defenders, opts, ctx){
  const R = ctx.rules, S = R.siege, Q = S.assault, H = R.map.height;
  const fail = lines => ({ok: false, title: "Приступ невозможен", lines, tone: "danger", patches: [], ladders: null, captured: false});
  const sec = getSection(map, secId);
  if(!sec) return fail(["Нет такого участка стены"]);
  const name = `участок №${sec.id} (${FORT_KINDS[sec.kind]})`, via = opts.via;
  if(A.status !== "active" || !(A.soldiers > 0)) return fail([`«${A.name}» не в строю`]);
  const lim = attackLimit(A, R);
  if((A.attacksMade || 0) >= lim) return fail([`«${A.name}» уже израсходовал атаки в этом ходу (${A.attacksMade}/${lim})`]);
  if(via !== "breach" && isCav(A)) return fail([`«${A.name}» — конница: по лестницам и с башни на стену не лезет, в пролом — можно`]);
  if(via === "breach"){
    if(!sec.breaches) return fail([`В участке №${sec.id} нет пролома`]);
  } else if(!sec.up) return fail([`Участок №${sec.id} разрушен целиком — лезть некуда, бой идёт в проломе`]);
  if(via === "ladders" && !(opts.ladders > 0)) return fail([`У отряда «${A.name}» нет лестниц`]);
  if(via === "tower" && !(opts.towers > 0))
    return fail([`У отряда «${A.name}» нет своей осадной башни у этого участка — башня и строй должны стоять не дальше ${Q.towerReachM} м от стены и друг от друга`]);
  if(via !== "tower" && !(opts.gap <= Q.reachM)) return fail([`«${A.name}» далеко от стены: ${Math.round(opts.gap)} м, нужно не дальше ${Q.reachM} м`]);
  const engaged = (opts.engaged || []).filter(id => id !== A.id);
  const limit = via === "breach" ? Math.max(1, Math.floor(sec.breaches * S.breachM / Q.breachPerUnitM)) : Q.maxUnits;
  if(engaged.length >= limit)
    return fail([`На участке №${sec.id} в этом ходу уже бились ${engaged.length} отр. — больше не влезет (${via === "breach" ? `в пролом — 1 отряд на каждые ${Q.breachPerUnitM} м` : `не больше ${Q.maxUnits} отрядов`}, Г50)`]);

  const cap = engineOf("tower", R).capacity;
  const men = via === "ladders" ? Math.min(A.soldiers, opts.ladders * Q.perLadder)
            : via === "tower" ? Math.min(A.soldiers, opts.towers * cap) : A.soldiers;
  const how = via === "ladders" ? `по лестницам (${opts.ladders} шт. × ${Q.perLadder} бойцов)`
            : via === "tower" ? `через осадную башню (${opts.towers} шт. × ${cap} бойцов)` : `в пролом (${sec.breaches * S.breachM} м)`;
  const L = [`🪜 «${A.name}» идёт на ${name} ${how}: в бой вступают ${men} из ${A.soldiers} · черновик`];
  const title = `🪜 Приступ: ${A.name} → ${name}`;
  const pa = {attacksMade: (A.attacksMade || 0) + 1, acted: true};
  const D = defenders.length ? defenders[0].unit : null;
  if(!D){
    sec.holder = A.factionId || 0;
    L.push(`Защитников на стене нет — участок №${sec.id} занят`);
    return {ok: true, title, lines: L, tone: "attack", patches: [{id: A.id, patch: pa}], ladders: via === "ladders" ? opts.ladders : null, captured: true};
  }
  L.push(`Участок держит «${D.name}» (${Math.round(defenders[0].gap)} м от стены)`);
  const A2 = Object.assign({}, A, {soldiers: men});
  const up = via === "ladders";
  const ab = up ? {mult: H.uphillMelee, note: "🪜 Снизу вверх по лестницам"} : null;
  const ba = up ? {mult: H.downhillMelee, note: "🏰 Со стены сверху вниз"} : null;
  const op = {mode: "melee_rough", sitPct: 0, fatigueMode: opts.fatigueMode};
  L.push(`—— Приступ: ${A.name} → ${D.name} ——`);
  const resD = computeStrike(A2, D, Object.assign({rollCap: men}, op), L, ctx, false, 1, "", "front", ab);

  let counter = true, why = "";
  if(D.morale === 0){ counter = false; why = `«${D.name}» сломлен (БД на нуле) — ответного удара нет`; }
  else if(D.morale < R.morale.shakenBelow){ counter = false; why = `«${D.name}» дрогнул (БД ниже ${R.morale.shakenBelow}) — ответного удара нет`; }
  const cl = counterLimit(D, R);
  if(counter && (D.countersMade || 0) >= cl){ counter = false; why = `«${D.name}» уже израсходовал ответные удары в этом ходу (${D.countersMade}/${cl})`; }
  let resA = {casualties: 0, killed: 0, wounded: 0};
  if(counter){
    L.push(`—— Ответ со стены: ${D.name} → ${A.name} (по численности до потерь) ——`);
    resA = computeStrike(D, A2, op, L, ctx, false, 1, "", "front", ba);
  } else { L.push("—— Без ответного удара ——"); L.push(why); }

  // ответным ударом защитник сбрасывает лестницы (Г48): каждая из приставленных — d100 ≤ pushPct
  let ladders = via === "ladders" ? opts.ladders : null;
  if(up && counter){
    const used = Math.min(opts.ladders, Math.ceil(men / Q.perLadder));
    const rolls = [];
    let thrown = 0;
    for(let k = 0; k < used; k++){ const r = rollDie(ctx.rng, 100); rolls.push(r); if(r <= Q.pushPct) thrown++; }
    L.push(`Сброс лестниц (d100 ≤ ${Q.pushPct}): ${rolls.join(", ")} → сброшено ${thrown} из ${used} · черновик`);
    ladders -= thrown;
    const fallen = Math.min(Math.max(0, A.soldiers - resA.casualties), thrown * Q.fallMen);
    if(fallen > 0){
      resA = {casualties: resA.casualties + fallen, killed: resA.killed, wounded: resA.wounded + fallen};
      L.push(`Со сброшенных лестниц упали ${fallen} — выбыли ранеными`);
    }
  }
  const pd = casualtyPatch(D, resD, L, ctx).patch;
  if(counter) pd.countersMade = (D.countersMade || 0) + 1;
  const pA = Object.assign(casualtyPatch(A, resA, L, ctx).patch, pa);
  if(pd.status === "destroyed") L.push(`На участке №${sec.id} не осталось «${D.name}» — если других защитников нет, следующий приступ его займёт`);
  return {ok: true, title, lines: L, tone: "attack", patches: [{id: D.id, patch: pd}, {id: A.id, patch: pA}], ladders, captured: false};
}
