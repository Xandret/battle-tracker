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

  // ── Штурм (этап 6б, Г46–Г51) — ЧЕРНОВИК ДО ГМа ──
  // Работает только при включённом переключателе «Штурм» в «Правилах карты».
  siege: {
    draft: true,
    sectionM: 25,     // стена режется на участки не длиннее (Г46, Ш1)
    breachM: 10,      // пролом за каждое обнуление прочности участка (Г46, Ш2)
    // прочность участка (Г46): частокол, каменная стена, ворота деревянные / окованные, башня
    hp: { palisade: 30, wall: 100, gateWood: 40, gateIron: 80, tower: 150 },

    // Орудия (Ш3, Г47, Г49). Фишка — батарея из count одинаковых орудий.
    //   crew / minCrew — расчёт одного орудия: полный и без которого оно молчит;
    //   reload — ходов между выстрелами при полном расчёте (меньше людей — дольше, Г49);
    //   move — м за ход (0 — строится на месте); deploy — ходов развёртывания после марша (сверх хода марша);
    //   range — [от, до] м; hitWall / hitTroops — шанс попасть, %: [на «от», на «до»], между — по прямой (Г47);
    //   indirect — навесом через стены, видеть цель не нужно; wall — урон прочности участка за попадание;
    //   die — урон по людям: d(die) за попадание, как d(численность) стрелков за столом, дальше ÷ (снар. защиты / 10);
    //   hp — прочность одного орудия; burst — % разрыва ствола за выстрел; shock — −БД цели за залп с попаданием.
    skillK: 0.2,              // выучка расчёта: шанс + (опыт − 50) × 0.2, то есть ±10%
    hitMin: 5, hitMax: 95,    // шанс попасть не меньше и не больше
    engines: {
      ballista:     { name: "Баллиста",           crew: 3,  minCrew: 1, reload: 1, move: 60, deploy: 0, range: [0, 350],   hitWall: [80, 45], hitTroops: [60, 25], wall: 3,  die: 30,  hp: 20 },
      catapult:     { name: "Катапульта (онагр)", crew: 6,  minCrew: 2, reload: 1, move: 40, deploy: 0, range: [50, 300],  hitWall: [65, 30], hitTroops: [45, 15], wall: 10, die: 40,  hp: 30, indirect: true },
      trebuchet:    { name: "Требушет",           crew: 12, minCrew: 4, reload: 2, move: 0,  deploy: 0, range: [100, 350], hitWall: [60, 30], hitTroops: [35, 10], wall: 25, die: 80,  hp: 50, indirect: true },
      bombard:      { name: "Бомбарда",           crew: 10, minCrew: 2, reload: 3, move: 15, deploy: 1, range: [30, 400],  hitWall: [75, 40], hitTroops: [35, 10], wall: 40, die: 60,  hp: 60, burst: 3, shock: 15 },
      cannon:       { name: "Пушка",              crew: 6,  minCrew: 2, reload: 2, move: 50, deploy: 0, range: [0, 500],   hitWall: [70, 40], hitTroops: [55, 20], wall: 15, die: 80,  hp: 40, burst: 2, shock: 10 },
      mortar:       { name: "Мортира",            crew: 6,  minCrew: 2, reload: 2, move: 20, deploy: 1, range: [50, 300],  hitWall: [55, 25], hitTroops: [40, 15], wall: 10, die: 60,  hp: 50, burst: 2, shock: 15, indirect: true },
      ribauldequin: { name: "Рибодекин",          crew: 4,  minCrew: 1, reload: 3, move: 80, deploy: 0, range: [0, 150],   hitWall: [70, 40], hitTroops: [75, 35], wall: 1,  die: 150, hp: 25, burst: 3, shock: 10 },
      magic:        { name: "Маг-пушка",          crew: 3,  minCrew: 1, reload: 2, move: 60, deploy: 1, range: [0, 600],   hitWall: [85, 45], hitTroops: [85, 45], wall: 50, die: 0,   hp: 40, magic: true },
      ram:          { name: "Таран",              crew: 12, minCrew: 4, reload: 1, move: 40, deploy: 0, range: [0, 10],    wall: 25, die: 0, hp: 60, ram: true },   // от центра фишки: таран ~10 м длиной
      tower:        { name: "Осадная башня",      crew: 20, minCrew: 8, reload: 0, move: 25, deploy: 0, range: [0, 0],     wall: 0,  die: 0, hp: 80, tower: true, capacity: 40 },
    },
    // Маг-батарея (SPEC 6б): залп — один бросок на попадание; шанс + (навык мага − 10) × 3;
    // попадание уносит 70–100% численности цели × (живых орудий / орудий в полной батарее), броня — не больше −15%;
    // брызги — всем отрядам ближе splashM от края цели, своим тоже: 5–15% × доля батареи;
    // удар по самой пушке, способный её уничтожить: d20 ≤ навык мага — гаснет, иначе взрыв: 20–50% всем ближе explodeM.
    magic: { skillK: 3, killPct: [70, 100], armorCap: 0.15, armorDiv: 1000, splashM: 30, splashPct: [5, 15], explodeM: 40, explodePct: [20, 50] },
    // Таран бьёт без броска на попадание, ворота — полным уроном, стену и башню — wallK
    ram: { wallK: 0.25 },
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
