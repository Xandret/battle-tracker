// ═══════════ morale.js — боевой дух, слом, проверки ═══════════
import { clamp, rollDie } from "./util.js";
import { graceByDisc } from "./units.js";

// Возвращает патч для отряда после изменения БД; пишет пояснения в lines.
export function applyMoraleChange(u, newMorale, lines, rules){
  const R = rules;
  const patch = {morale: clamp(newMorale, 0, R.morale.max)};
  if(patch.morale === 0 && !u.broken){
    patch.broken = true;
    patch.breakGrace = graceByDisc(u.discipline, R);
    patch.breakPenalty = R.breakdown.delayTurns;
    lines.push(`⚠ БД «${u.name}» упал до нуля — дисциплина −${R.breakdown.discPenalty} вступит в силу через ${R.breakdown.delayTurns} хода`);
    if(patch.breakGrace > 0) lines.push(`Выучка держит строй: пассивная потеря дисциплины отложена на ${patch.breakGrace} х.`);
    lines.push(`«${u.name}»: требуется проверка на побег!`);
  }
  if(patch.morale > 0 && u.broken){
    patch.broken = false; patch.breakGrace = 0;
    if(u.breakPenalty > 0){
      patch.breakPenalty = 0;
      lines.push(`Отложенный штраф дисциплины «${u.name}» отменён — отряд взяли в руки вовремя`);
    }
    lines.push(`БД «${u.name}» восстановлен — юнит вновь в руках командира`);
  }
  return patch;
}

// Проверка БД: d100 ≤ БД × 2; «колеблются» — с помехой (худший из двух).
// Возвращает { title, lines, tone, patch } — патч может быть пустым.
export function moraleCheck(u, ctx){
  const R = ctx.rules;
  const L = [];
  const disadv = u.morale >= R.morale.waverFrom && u.morale < R.morale.waverTo;
  let roll = rollDie(ctx.rng, 100);
  if(disadv){
    const r2v = rollDie(ctx.rng, 100);
    L.push(`Помеха («колеблются»): броски ${roll} и ${r2v}, берём худший`);
    roll = Math.max(roll, r2v);
  }
  const target = u.morale * R.morale.checkMult;
  L.push(`d100: ${roll} против ${target} (БД × ${R.morale.checkMult})`);
  if(roll <= target){
    L.push(`✔ Успех — строй держится, БД остаётся ${u.morale}`);
    return {title: `Проверка БД: ${u.name}`, lines: L, tone: "info", patch: {}};
  }
  L.push(`✘ Провал — БД падает до нуля`);
  const patch = applyMoraleChange(u, 0, L, R);
  return {title: `Проверка БД: ${u.name}`, lines: L, tone: "danger", patch};
}

// Проверка на побег: d100 ≤ дисциплина; «дрогнули» — с помехой.
// Дисциплина ≥ 60: первый бросок игнорируется («стоять насмерть»).
export function fleeCheck(u, ctx){
  const R = ctx.rules;
  const L = [];
  if(u.discipline >= R.flee.standFastDisc && u.fleeChecks === 0){
    L.push(`«Стоять насмерть» (дисц ≥ ${R.flee.standFastDisc}): первый бросок на побег игнорируется`);
    return {title: `Проверка на побег: ${u.name}`, lines: L, tone: "info", patch: {fleeChecks: 1}};
  }
  const disadv = u.morale < R.morale.shakenBelow;
  let roll = rollDie(ctx.rng, 100);
  if(disadv){
    const r2v = rollDie(ctx.rng, 100);
    L.push(`Помеха («дрогнули»): броски ${roll} и ${r2v}, берём худший`);
    roll = Math.max(roll, r2v);
  }
  L.push(`d100: ${roll} против ${u.discipline} (дисциплина)`);
  if(roll <= u.discipline){
    L.push(`✔ Строй держится на одной муштре`);
    return {title: `Проверка на побег: ${u.name}`, lines: L, tone: "info", patch: {fleeChecks: u.fleeChecks + 1}};
  }
  L.push(`✘ Юнит обращён в бегство!`);
  return {title: `Проверка на побег: ${u.name}`, lines: L, tone: "danger",
          patch: {status: "fled", fleeChecks: u.fleeChecks + 1}};
}
