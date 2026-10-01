// ═══════════ index.js — публичный интерфейс движка ═══════════
// Порядок файлов в сборке задаётся в tools/build.mjs (ENGINE_FILES).
export { clamp, r1, rollDie, mulberry32 } from "./util.js";
export { BASE_RULES, RULESETS, PLANNED_RULESETS, getRules } from "./rules.js";
export { MODES, isMeleeMode, attackLimit, counterLimit, moraleStage, discStage, graceByDisc,
         isCav, isPike, isArcherType, canBeTargeted, attackSector, SECTOR_RU,
         TYPE_KEYWORDS, HORSE_ARCHERS, FOOT_RE, guessUnitType } from "./units.js";
export { applyMoraleChange, moraleCheck, fleeCheck } from "./morale.js";
export { effStats, computeStrike, casualtyPatch, resolveBattle } from "./combat.js";
export { endTurn } from "./turn.js";
export { TEMPLATE_STATS, TEMPLATE_LIMITS, BASE_TEMPLATES, getTemplate, matchTemplate,
         normalizeOverrides, clampField, factionOverrides, resolveTemplate } from "./templates.js";
export { MUSTER, SPECIAL_RE, parseCount, toNominative, splitCommanders, instrumentalToNom, parseArmyText, planLine, planMuster,
         splitSoldiers, lineSize, numberedNames, expandMuster } from "./muster.js";
export { CELL_M, MAX_HEIGHT, MAX_CELLS, TERRAIN, TERRAIN_BY_ID, TERRAIN_BY_KEY, terrainName, createTerrain,
         mapWidthM, mapHeightM, encodeLayer, decodeLayer, serializeTerrain, deserializeTerrain, cloneTerrain,
         cellAt, hasTerrain, paintDisc, paintSegment, paintRect, floodFill,
         MAX_SECTIONS, FORT_KINDS, fortCode, indexSections } from "./terrain.js";
export { MAP_TEMPLATES, getMapTemplate, mapParams, generateMap } from "./mapgen.js";
export { footprint, unitCenter, unitCorners, polyGap, unitGap, groundUnder, mapModsFor, fatigueMultFor,
         unitSpeed, reachMap, pathCost, runOver, runUpBlock, rangeOf, attackReach } from "./battlemap.js";
export { lineOfSight, panicWave } from "./panic.js";
export { buildSections, ensureSections, sectionMax, sectionHp, sectionName, getSection, sectionAt,
         damageSection, repairSection } from "./fortify.js";
