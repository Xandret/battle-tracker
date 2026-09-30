// ═══════════ rules.js — наборы правил ═══════════
// Каждый набор — это объект с числами, которые раньше были зашиты прямо в формулы.
// Набор 1 («base») воспроизводит поведение v29 один в один — это проверяется тестами.
// Наборы 2 (совмещённый) и 3 (предложенный) появятся после согласования документа с ГМом.

export const BASE_RULES = {
  id: "base",
  name: "Набор 1 — наши формулы",

  // Сколько атак и ответных ударов за ход: 1, а при дисциплине от eliteDisc — 2
  actions: { eliteDisc: 80 },

  // Бросок d(численность) не может быть ниже дисциплины × rollFloorPerDisc
  rollFloorPerDisc: 5,

  // Делитель в формуле атаки для каждого типа боя
  modeDiv: { melee_form: 50, ranged_form: 50, melee_rough: 25, ranged_rough: 25 },

  // Стрелки в ближнем бою: все параметры × archerMeleeMult
  archerMeleeMult: 0.5,

  // Кавалерия в ближнем бою: с натиском × chargeMult, без натиска × noChargeMult
  cavalry: { chargeMult: 1.5, noChargeMult: 0.7 },

  // Контрудар пикинёров по коннице в лоб
  pikeCounterMult: 3,

  // Защита цели
  defense: {
    meleeEqDiv: 20,     // ближний бой: урон / (Снар.защ/20 + Дисц/10)
    meleeDiscDiv: 10,
    rangedEqDiv: 10,    // дальний бой: урон / (Снар.защ/10), дисциплина не учитывается
    minDivisor: 1,      // делитель не ниже 1
    rearEqMult: 0.5,    // удар в тыл: снаряжение защиты × 0.5 на эту атаку
  },

  // Летальность: base% гарантированных убитых + d(die) / (Опыт/expDiv)
  lethality: { base: 10, die: 60, expDiv: 20 },

  // Потеря БД за потери
  moraleLoss: { perCasualties: 15, cap: 50, crushingFrac: 0.5, crushingLoss: 150 },

  // Боевой дух
  morale: {
    max: 150,
    checkAt: 40,            // при БД ≤ 40 требуется проверка
    checkMult: 2,           // проверка БД: d100 ≤ БД × 2
    waverFrom: 20,          // «колеблются» (20–39) — помеха в проверке БД
    waverTo: 40,
    shakenBelow: 20,        // «дрогнули» (< 20) — помеха в проверке на побег, нет ответного удара
  },

  // Слом БД
  breakdown: {
    delayTurns: 3,          // штраф дисциплины откладывается на 3 хода
    discPenalty: 20,
    passiveDiscLoss: 10,    // каждый ход на нуле БД — −10 дисциплины (после отсрочки выучкой)
    grace: [[80, 3], [70, 2], [60, 1]],   // [дисциплина от, ходов отсрочки]
  },

  // Проверка на побег
  flee: { standFastDisc: 60 },   // дисциплина ≥ 60: первый бросок игнорируется

  // Усталость
  fatigue: { step: 10, max: 100, threshold: 4, eliteThreshold: 8, eliteDisc: 90 },

  // Сектора удара относительно фасинга цели
  sectors: { frontMax: 45, rearMin: 135 },
};

export const RULESETS = { base: BASE_RULES };
export const PLANNED_RULESETS = [
  { id: "hybrid",   name: "Набор 2 — совмещённый",   status: "ждёт согласования с ГМом" },
  { id: "proposed", name: "Набор 3 — предложенный",  status: "ждёт согласования с ГМом" },
];

export function getRules(id){
  return RULESETS[id] || BASE_RULES;
}
