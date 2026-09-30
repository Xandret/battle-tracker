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

  // ── Карта в метрах (этап 6а, К17, К21–К25, К29, К33) — ЧЕРНОВИК ДО ГМа ──
  // Работает только при включённых переключателях «Правила карты» (их положение хранится в партии).
  // Выключены — ни одно число отсюда в бой не попадает, и набор 1 считает ровно как v29.
  map: {
    draft: true,
    // строй (К21): метров по фронту на бойца, шеренг, метров на шеренгу вглубь
    formation: {
      infantry: { perMan: 1,   ranks: 8,  rankDepth: 1 },   // 1000 → 125 × 8 м
      pike:     { perMan: 1,   ranks: 10, rankDepth: 1 },   // 1000 → 100 × 10 м
      archer:   { perMan: 1,   ranks: 5,  rankDepth: 1 },   // 1000 → 200 × 5 м
      cavalry:  { perMan: 1.5, ranks: 5,  rankDepth: 3 },   // 1000 → 300 × 15 м
    },
    // местность (К24): режим боя под целью; движение ×[пехота, конница], null — непроходимо (v30.6);
    // укрытие от стрел, % от итогового урона; усталость ×; натиск невозможен;
    // sight — сколько метров этой местности пропускает взгляд (лес 100 м, стены и скалы — 0)
    terrain: {
      field:    { mode: "form",  move: [1, 1] },
      road:     { mode: "form",  move: [0.7, 0.7] },
      sand:     { mode: "form",  move: [1.5, 2],   fatigue: 2 },
      snow:     { mode: "form",  move: [1.5, 1.5], fatigue: 1.5 },
      shrub:    { mode: "rough", move: [1.5, 2],   cover: 15 },
      forest:   { mode: "rough", move: [2, 3],     cover: 30, noCharge: true, sight: 100 },
      water:    { mode: "rough", move: null },
      ford:     { mode: "rough", move: [2, 2],     noCharge: true },
      bridge:   { mode: "form",  move: [1, 1] },
      swamp:    { mode: "rough", move: [3, 4],     noCharge: true },
      rocks:    { mode: "rough", move: null,       sight: 0 },
      wall:     { mode: "rough", move: null,       cover: 50, sight: 0 },
      gate:     { mode: "rough", move: null,       cover: 50, sight: 0 },
      tower:    { mode: "rough", move: null,       cover: 50, sight: 0 },
      palisade: { mode: "rough", move: [3, 4],     cover: 30 },
      moat:     { mode: "rough", move: [3, 4] },
      trench:   { mode: "rough", move: [3, 4],     cover: 30 },
      building: { mode: "rough", move: null,       cover: 50, sight: 0 },
      pavement: { mode: "form",  move: [1, 1] },
      breach:   { mode: "rough", move: [3, 4] },
    },
    // высота: ближний бой сверху вниз / снизу вверх; +дальность стрельбы за уровень и подъём (v30.6)
    height: { downhillMelee: 1.2, uphillMelee: 0.9, rangePerLevel: 0.1, climbCost: 1.5 },
    // движение за ход, м (К22) — относительные темпы: конница в 2,5 раза быстрее пехоты
    speed: { infantry: 100, archer: 100, pike: 80, cavalry: 250, horseArcher: 300 },
    // дальность стрельбы, м (К23); у отряда может быть своя (поле «Дальность»)
    range: { archer: 200, horseArcher: 150, other: 150 },
    meleeGap: 5,        // «вплотную» — края строя ближе одной клетки (К23)
    chargeRunUp: 50,    // натиску нужен разбег по чистой местности (К29)
    // каскадная паника (К11, К25): радиус от края до края строя; −БД вместе с проверкой (переключатель)
    panic: { radius: 150, moraleLoss: 100 },
  },
};

export const RULESETS = { base: BASE_RULES };
export const PLANNED_RULESETS = [
  { id: "hybrid",   name: "Набор 2 — совмещённый",   status: "ждёт согласования с ГМом" },
  { id: "proposed", name: "Набор 3 — предложенный",  status: "ждёт согласования с ГМом" },
];

export function getRules(id){
  return RULESETS[id] || BASE_RULES;
}
