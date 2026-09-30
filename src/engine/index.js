// ═══════════ index.js — публичный интерфейс движка ═══════════
// Порядок файлов в сборке задаётся в tools/build.mjs (ENGINE_FILES).
export { clamp, r1, rollDie, mulberry32 } from "./util.js";
export { BASE_RULES, RULESETS, PLANNED_RULESETS, getRules } from "./rules.js";
export { MODES, isMeleeMode, attackLimit, counterLimit, moraleStage, discStage, graceByDisc,
         isCav, isPike, isArcherType, canBeTargeted, attackSector, SECTOR_RU } from "./units.js";
export { applyMoraleChange, moraleCheck, fleeCheck } from "./morale.js";
export { effStats, computeStrike, casualtyPatch, resolveBattle } from "./combat.js";
export { endTurn } from "./turn.js";
