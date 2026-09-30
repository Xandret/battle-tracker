// ═══════════ turn.js — конец хода ═══════════
// Принимает массив отрядов, возвращает новый массив и строки журнала. Исходный массив не меняется.

export function endTurn(units, ctx){
  const R = ctx.rules, F = R.fatigue, B = R.breakdown;
  const L = [];
  const next = units.map(u => {
    const p = Object.assign({}, u, {attacksMade: 0, countersMade: 0});
    if(u.status !== "active") return p;
    if(p.acted){
      p.turnsActive = p.turnsActive + 1;
      const threshold = p.discipline >= F.eliteDisc ? F.eliteThreshold : F.threshold;
      if(p.turnsActive > threshold && p.fatigue < F.max){
        p.fatigue = Math.min(F.max, p.fatigue + F.step);
        L.push(`${u.name}: усталость +${F.step} → ${p.fatigue}`);
      }
      p.acted = false;
    }
    if(p.broken && p.morale === 0 && p.breakPenalty > 0){
      p.breakPenalty -= 1;
      if(p.breakPenalty === 0){
        const was = p.discipline;
        p.discipline = Math.max(1, p.discipline - B.discPenalty);
        L.push(`${u.name}: отложенный штраф за слом БД — дисциплина −${B.discPenalty} (${was} → ${p.discipline})`);
      } else {
        L.push(`${u.name}: штраф дисциплины за слом БД вступит в силу через ${p.breakPenalty} х.`);
      }
    }
    if(p.broken && p.morale === 0){
      if(p.breakGrace > 0){
        p.breakGrace -= 1;
        L.push(`${u.name}: выучка держит строй (осталось ${p.breakGrace} х.)`);
      } else {
        p.discipline = Math.max(0, p.discipline - B.passiveDiscLoss);
        L.push(`${u.name}: БД на нуле — дисциплина −${B.passiveDiscLoss} → ${p.discipline}`);
        if(p.discipline <= 0){
          p.status = "fled";
          L.push(`${u.name}: дисциплина иссякла — побег без броска!`);
        }
      }
    }
    return p;
  });
  return {units: next, lines: L};
}
