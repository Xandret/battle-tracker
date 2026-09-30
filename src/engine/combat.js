// ═══════════ combat.js — расчёт боя ═══════════
// ctx = { rules, rng, commanderOf(u) → полководец|null, factionName(id) → строка }
import { clamp, r1, rollDie } from "./util.js";
import { MODES, isMeleeMode, attackLimit, counterLimit, moraleStage,
         isCav, isPike, canBeTargeted, attackSector, SECTOR_RU } from "./units.js";
import { applyMoraleChange } from "./morale.js";

// Эффективные параметры отряда в данном типе боя: баффы полководца, штраф стрелков в рукопашной.
export function effStats(u, mode, L, ctx){
  const R = ctx.rules;
  let eqAtk = u.eqAtk, eqDef = u.eqDef, disc = u.discipline,
      exp = u.exp, mast = u.mastery, mor = u.morale;
  const cmdr = ctx.commanderOf(u);
  if(cmdr && (cmdr.buffMorale || cmdr.buffDisc)){
    if(cmdr.buffMorale) mor = clamp(mor + cmdr.buffMorale, 0, R.morale.max);
    if(cmdr.buffDisc) disc = clamp(disc + cmdr.buffDisc, 1, 100);
    L.push(`⚜ ${cmdr.name} ведёт «${u.name}»: ${cmdr.buffMorale ? `БД ${cmdr.buffMorale > 0 ? "+" : ""}${cmdr.buffMorale} → ${mor}` : ""}${cmdr.buffMorale && cmdr.buffDisc ? ", " : ""}${cmdr.buffDisc ? `дисц ${cmdr.buffDisc > 0 ? "+" : ""}${cmdr.buffDisc} → ${disc}` : ""}`);
  }
  if(u.weapon === "ranged" && isMeleeMode(mode)){
    const k = R.archerMeleeMult;
    eqAtk = r1(eqAtk * k); eqDef = r1(eqDef * k); disc = r1(disc * k);
    exp = r1(exp * k); mast = r1(mast * k); mor = r1(mor * k);
    L.push(`«${u.name}» — стрелки в ближнем бою: все параметры −${Math.round((1 - k) * 100)}%`);
  }
  return {eqAtk, eqDef, discipline: disc, exp, mastery: mast, morale: mor, cmdr};
}

// Один удар att → def. Броски: d(численность атакующего), затем d(летальность), если есть потери.
// mapMod — модификаторы карты для этого удара (черновик 6а, только при включённых «Правилах карты»):
// mult — высота в ближнем бою, coverPct — укрытие цели от стрел. Нет mapMod — расчёт ровно как v29.
export function computeStrike(att, def, opts, L, ctx, isCharge, extraMult, extraNote, sector, mapMod){
  const R = ctx.rules;
  const A = effStats(att, opts.mode, L, ctx);
  const D = effStats(def, opts.mode, L, ctx);

  let roll = rollDie(ctx.rng, att.soldiers);
  const rollFloor = Math.round(A.discipline * R.rollFloorPerDisc);
  if(roll < rollFloor){
    L.push(`Бросок d${att.soldiers}: ${roll} → поднят до минимума ${rollFloor} (дисциплина ${A.discipline} × ${R.rollFloorPerDisc})`);
    roll = rollFloor;
  } else {
    L.push(`Бросок d${att.soldiers}: ${roll} (минимум по дисциплине: ${rollFloor})`);
  }

  const div = R.modeDiv[opts.mode];
  let dmg = 0;
  if(opts.mode === "melee_form"){
    const f = A.eqAtk + A.discipline/2 + A.exp/2;
    dmg = roll * f / div;
    L.push(`Атака: ${roll} × (${A.eqAtk} + ${A.discipline}/2 + ${A.exp}/2) / ${div} = ${r1(dmg)}`);
  } else if(opts.mode === "ranged_form"){
    const f = A.eqAtk + A.exp/2 + A.mastery/2;
    dmg = roll * f / div;
    L.push(`Атака: ${roll} × (${A.eqAtk} + ${A.exp}/2 + ${A.mastery}/2) / ${div} = ${r1(dmg)}`);
  } else if(opts.mode === "melee_rough"){
    const f = A.exp/2 + A.morale/2 + A.mastery + A.eqAtk;
    dmg = roll * f / div;
    L.push(`Атака: ${roll} × (${A.exp}/2 + ${A.morale}/2 + ${A.mastery} + ${A.eqAtk}) / ${div} = ${r1(dmg)}`);
  } else {
    const f = A.eqAtk + A.exp/2 + A.mastery/2;
    dmg = roll * f / div;
    L.push(`Атака: ${roll} × (${A.eqAtk} + ${A.exp}/2 + ${A.mastery}/2) / ${div} = ${r1(dmg)}`);
  }

  if(att.fatigue > 0){
    if(opts.fatigueMode === "percent"){
      const before = dmg;
      dmg *= 1 - att.fatigue/100;
      L.push(`Усталость ${att.fatigue}: −${att.fatigue}% урона (${r1(before)} → ${r1(dmg)})`);
    } else {
      dmg -= att.fatigue;
      L.push(`Усталость ${att.fatigue}: урон −${att.fatigue} = ${r1(dmg)}`);
    }
  }

  const st = moraleStage(A.morale);
  if(st.mult > 1){
    const before = dmg;
    dmg *= st.mult;
    L.push(`Боевой дух «${st.label}»: ×${st.mult} (${r1(before)} → ${r1(dmg)})`);
  }

  if(isCav(att) && isMeleeMode(opts.mode)){
    const before = dmg;
    if(isCharge){
      dmg *= R.cavalry.chargeMult;
      L.push(`🐎 Натиск: +${Math.round((R.cavalry.chargeMult - 1) * 100)}% урона (${r1(before)} → ${r1(dmg)})`);
    } else {
      dmg *= R.cavalry.noChargeMult;
      L.push(`Кавалерия без натиска: −${Math.round((1 - R.cavalry.noChargeMult) * 100)}% урона (${r1(before)} → ${r1(dmg)})`);
    }
  }

  if(extraMult && extraMult !== 1){
    const before = dmg;
    dmg *= extraMult;
    L.push(`${extraNote || "Особый модификатор"}: ×${extraMult} (${r1(before)} → ${r1(dmg)})`);
  }

  if(mapMod && mapMod.mult && mapMod.mult !== 1 && isMeleeMode(opts.mode)){
    const before = dmg;
    dmg *= mapMod.mult;
    L.push(`${mapMod.note}: ${mapMod.mult > 1 ? "+" : "−"}${Math.round(Math.abs(mapMod.mult - 1) * 100)}% урона (${r1(before)} → ${r1(dmg)}) · черновик`);
  }

  if(A.cmdr && A.cmdr.buffDmg){
    const before = dmg;
    dmg *= 1 + A.cmdr.buffDmg/100;
    L.push(`⚜ ${A.cmdr.name}: урон ${A.cmdr.buffDmg > 0 ? "+" : ""}${A.cmdr.buffDmg}% (${r1(before)} → ${r1(dmg)})`);
  }

  {
    const Df = R.defense;
    const ranged = !isMeleeMode(opts.mode);
    let defEq = D.eqDef;
    if(sector === "rear"){
      const was = defEq;
      defEq = r1(defEq * Df.rearEqMult);
      L.push(`Удар в тыл: снаряжение защиты «${def.name}» −${Math.round((1 - Df.rearEqMult) * 100)}% на эту атаку (${was} → ${defEq})`);
    }
    const dv = ranged ? defEq/Df.rangedEqDiv : defEq/Df.meleeEqDiv + D.discipline/Df.meleeDiscDiv;
    const divisor = Math.max(Df.minDivisor, dv);
    const before = dmg;
    dmg = dmg / divisor;
    L.push(ranged
      ? `Защита цели: урон / (${defEq}/${Df.rangedEqDiv}) → ${r1(before)} / ${r1(divisor)} = ${r1(dmg)} (дисциплина от стрел не спасает)${dv < Df.minDivisor ? " · делитель не ниже 1" : ""}`
      : `Защита цели: урон / (${defEq}/${Df.meleeEqDiv} + ${D.discipline}/${Df.meleeDiscDiv}) → ${r1(before)} / ${r1(divisor)} = ${r1(dmg)}${dv < Df.minDivisor ? " (делитель не ниже 1)" : ""}`);
  }

  if(D.cmdr && D.cmdr.buffDef){
    const before = dmg;
    dmg *= Math.max(0, 1 - D.cmdr.buffDef/100);
    L.push(`⚜ ${D.cmdr.name} прикрывает «${def.name}»: урон −${D.cmdr.buffDef}% (${r1(before)} → ${r1(dmg)})`);
  }

  if(!isMeleeMode(opts.mode) && opts.sitPct > 0){
    const before = dmg;
    dmg *= 1 - opts.sitPct/100;
    L.push(`Ситуативный модификатор: −${opts.sitPct}% от итогового урона (${r1(before)} → ${r1(dmg)})`);
  }

  if(mapMod && mapMod.coverPct > 0 && !isMeleeMode(opts.mode)){
    const before = dmg;
    dmg *= 1 - mapMod.coverPct/100;
    L.push(`${mapMod.coverNote}: −${mapMod.coverPct}% от итогового урона (${r1(before)} → ${r1(dmg)}) · черновик`);
  }

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

// Патч отряда после понесённых потерь (без записи в состояние) + признак «тяжёлого» исхода.
export function casualtyPatch(def, res, L, ctx){
  const R = ctx.rules, M = R.moraleLoss;
  const patch = {
    soldiers: def.soldiers - res.casualties,
    totKilled: (def.totKilled || 0) + res.killed,
    totWounded: (def.totWounded || 0) + res.wounded,
  };
  let broke = false;
  if(res.casualties > 0){
    const frac = res.casualties / def.soldiers;
    let moraleLoss;
    if(frac > M.crushingFrac){
      moraleLoss = M.crushingLoss;
      L.push(`💥 Сокрушительные потери «${def.name}» (>${Math.round(M.crushingFrac * 100)}% за одно действие): БД −${M.crushingLoss}`);
    } else {
      moraleLoss = Math.min(M.cap, Math.round(res.casualties / M.perCasualties));
      L.push(`Штраф БД «${def.name}» за потери: ${res.casualties} / ${M.perCasualties} = −${moraleLoss}`);
    }
    Object.assign(patch, applyMoraleChange(def, def.morale - moraleLoss, L, R));
    if(patch.broken) broke = true;
  }
  if(patch.soldiers <= 0){
    patch.status = "destroyed"; patch.soldiers = 0;
    L.push(`☠ Юнит «${def.name}» уничтожен полностью`);
    broke = true;
  } else {
    const m = (patch.morale !== undefined ? patch.morale : def.morale);
    if(m <= R.morale.checkAt && m > 0) L.push(`БД «${def.name}» ≤ ${R.morale.checkAt} — требуется проверка БД`);
  }
  return {patch, broke};
}

// Полный бой между A и B.
// req = { mode, sitPct, fatigueMode, mutual, charge, counterCharge } — пожелания мастера;
// движок сам решает, применимы ли натиск и встречный натиск.
// Возвращает { ok, title, lines, tone, patches:[{id, patch}] } — ничего не пишет в состояние.
export function resolveBattle(A, B, req, ctx){
  const R = ctx.rules;
  const fail = lines => ({ok: false, title: "Бой невозможен", lines, tone: "danger", patches: []});

  if(!A || !B || A.id === B.id || A.status !== "active" || !canBeTargeted(B))
    return fail(["Атакующий должен быть в строю, а цель — не уничтожена."]);
  if(A.factionId && A.factionId === B.factionId)
    return fail([`«${A.name}» и «${B.name}» — одна фракция (${ctx.factionName(A.factionId)}). Свои по своим не бьют.`]);
  const limit = attackLimit(A, R);
  if((A.attacksMade || 0) >= limit)
    return fail([`«${A.name}» уже израсходовал атаки в этом ходу (${A.attacksMade}/${limit}).${limit === 1 ? ` Дисциплина ${R.actions.eliteDisc}+ даёт 2 атаки за ход.` : ""}`]);

  const opts = { mode: req.mode, sitPct: clamp(+req.sitPct || 0, 0, 100), fatigueMode: req.fatigueMode };
  const mutual = !!req.mutual;
  // Модификаторы карты (battlemap.mapModsFor) — только при включённых «Правилах карты»;
  // noCharge — причина, по которой натиска нет (местность, нет разбега)
  const MM = req.mapMods || null;
  const chargeBlocked = !!(MM && MM.noCharge);
  const charge = !!req.charge && !chargeBlocked && A.type === "cavalry" && isMeleeMode(opts.mode);
  const counterCharge = !!req.counterCharge && !chargeBlocked && B.type === "cavalry" && isMeleeMode(opts.mode) && B.status === "active";

  const A0 = Object.assign({}, A);
  const B0 = Object.assign({}, B);
  const L = [];
  let tone = "attack";

  const sector = attackSector(A, B, R);
  const flanked = sector !== "front";
  const pikeStop = isPike(B) && isCav(A) && isMeleeMode(opts.mode) && sector === "front" && B.status === "active";
  if(charge && counterCharge){
    L.push(`—— Встречный натиск: обе конницы идут навстречу ——`);
    L.push(`«${A0.name}» и «${B0.name}» сшибаются на полном ходу — натиск считается обеим сторонам`);
  }
  if(A.onMap && B.onMap){
    L.push(`Направление удара: ${SECTOR_RU[sector]} (фасинг «${B0.name}»: ${Math.round(B0.facing || 0)}°)`);
  }
  if(MM){
    MM.notes.forEach(n => L.push(n));
    if(chargeBlocked && (req.charge || req.counterCharge) && isMeleeMode(opts.mode))
      L.push(`🐎 Натиск невозможен — ${MM.noCharge} · черновик`);
  }
  let effCharge = charge;
  if(pikeStop){
    if(charge){
      effCharge = false;
      L.push(`🛡 Пикинёры «${B0.name}» встречают конницу копьями во фронт — натиск остановлен`);
    } else {
      L.push(`🛡 Конница входит в лоб на копья «${B0.name}» — останавливать нечего, контрудар будет тройным`);
    }
  }

  L.push(`—— Удар: ${A0.name} → ${B0.name} ——`);
  const resB = computeStrike(A0, B0, opts, L, ctx, effCharge, 1, "", sector, MM ? MM.ab : null);

  let counter = mutual;
  let noCounterReason = "";
  if(counter && flanked){
    counter = false;
    noCounterReason = `Удар ${SECTOR_RU[sector]}: «${B0.name}» не успевает развернуться и не отвечает`;
  }
  if(counter && charge && !counterCharge && !pikeStop){
    counter = false;
    noCounterReason = `«${B0.name}» смят натиском — ответного удара нет`;
  }
  if(counter && !isMeleeMode(opts.mode) && B0.weapon !== "ranged"){
    counter = false;
    noCounterReason = `«${B0.name}» — отряд ближнего боя под обстрелом: ответить нечем`;
  }
  if(counter && B0.status === "fled"){
    counter = false;
    noCounterReason = `«${B0.name}» бежит с поля боя — отбиваться некому`;
  }
  if(counter && B0.morale === 0){
    counter = false;
    noCounterReason = `«${B0.name}» сломлен (БД на нуле) — ответного удара нет`;
  } else if(counter && B0.morale < R.morale.shakenBelow){
    counter = false;
    noCounterReason = `«${B0.name}» дрогнул (БД ниже ${R.morale.shakenBelow}) — ответного удара нет`;
  }
  const cLimit = counterLimit(B0, R);
  if(counter && (B0.countersMade || 0) >= cLimit){
    counter = false;
    noCounterReason = `«${B0.name}» уже израсходовал ответные удары в этом ходу (${B0.countersMade}/${cLimit})${cLimit === 1 ? ` — дисциплина ${R.actions.eliteDisc}+ даёт 2 ответных удара` : ""}`;
  }

  let resA = {casualties: 0, killed: 0, wounded: 0};
  if(counter){
    L.push(pikeStop
      ? `—— Контрудар пикинёров: ${B0.name} → ${A0.name} (по численности до потерь) ——`
      : counterCharge
        ? `—— Встречный удар с натиском: ${B0.name} → ${A0.name} (по численности до потерь) ——`
        : `—— Ответный удар: ${B0.name} → ${A0.name} (по численности до потерь) ——`);
    resA = computeStrike(B0, A0, opts, L, ctx, counterCharge, pikeStop ? R.pikeCounterMult : 1,
      pikeStop ? `🛡 Копья против конского строя: +${(R.pikeCounterMult - 1) * 100}% урона` : "", "front", MM ? MM.ba : null);
  } else if(mutual && noCounterReason){
    L.push(`—— Без ответного удара ——`);
    L.push(noCounterReason);
  }

  if(B0.status === "fled"){
    L.push(`Преследование: «${B0.name}» бежит и не оказывает сопротивления`);
  }
  L.push(`—— Итоги боя ——`);
  const patches = [];
  const cb = casualtyPatch(B0, resB, L, ctx);
  patches.push({id: B.id, patch: cb.patch});
  if(cb.broke) tone = "danger";
  if(counter){
    const ca = casualtyPatch(A0, resA, L, ctx);
    patches.push({id: A.id, patch: ca.patch});
    if(ca.broke) tone = "danger";
  }

  patches.push({id: A.id, patch: {attacksMade: (A.attacksMade || 0) + 1, acted: true}});
  L.push(`«${A0.name}»: атака засчитана (${(A.attacksMade || 0) + 1}/${limit}), отмечен «Походил»`);
  if(counter){
    patches.push({id: B.id, patch: {acted: true, countersMade: (B.countersMade || 0) + 1}});
    L.push(`«${B0.name}»: дрался в ответ (ответных ударов: ${(B.countersMade || 0) + 1}/${cLimit}) — отмечен «Походил»`);
  }

  const chargeTag = (pikeStop ? (charge ? " · натиск остановлен пиками" : " · лоб на копья") : "")
                  + (charge && counterCharge ? " · встречный натиск"
                     : charge && !pikeStop ? " · натиск"
                     : counterCharge ? " · контратака с натиском" : "")
                  + (flanked ? " · удар " + SECTOR_RU[sector] : "");
  const title = (counter ? `${A.name} ⇄ ${B.name}` : `${A.name} → ${B.name}`) + ` · ${MODES[opts.mode]}${chargeTag}`;
  return {ok: true, title, lines: L, tone, patches};
}
