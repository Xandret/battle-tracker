// ═══════════ scenarios.mjs — набор сценариев для сверки движка с v29 ═══════════
// Сценарии генерируются детерминированно из зерна, чтобы фикстуры можно было пересоздать.
import { mulberry32 } from "../src/engine/util.js";

const MODES = ["melee_form", "ranged_form", "melee_rough", "ranged_rough"];
const TYPES = ["infantry", "cavalry", "archer", "pike"];

function pick(rng, arr){ return arr[Math.floor(rng() * arr.length)]; }
function int(rng, lo, hi){ return lo + Math.floor(rng() * (hi - lo + 1)); }

function makeUnit(rng, id, factionId, commanderId){
  const type = pick(rng, TYPES);
  const weapon = type === "archer" ? "ranged" : (rng() < 0.15 ? "ranged" : "melee");
  const initial = pick(rng, [100, 300, 500, 800, 1000, 1000, 1000]);
  const soldiers = rng() < 0.7 ? initial : int(rng, 1, initial);
  const morale = pick(rng, [0, 10, 25, 35, 45, 60, 90, 110, 130, int(rng, 0, 150)]);
  const broken = morale === 0 && rng() < 0.7;
  return {
    id, name: `Отряд ${id}`, type, weapon, factionId, subfactionId: null, commanderId,
    soldiers, initial, discipline: int(rng, 5, 100), morale,
    eqAtk: int(rng, 0, 100), eqDef: int(rng, 0, 100), exp: int(rng, 0, 100),
    mastery: int(rng, 0, 60), fatigue: pick(rng, [0, 0, 0, 10, 30, 60]),
    status: rng() < 0.12 ? "fled" : "active",
    turnsActive: int(rng, 0, 10), fleeChecks: pick(rng, [0, 0, 1, 2]),
    breakGrace: broken ? int(rng, 0, 3) : 0, broken, breakPenalty: broken ? int(rng, 0, 3) : 0,
    acted: rng() < 0.4, attacksMade: pick(rng, [0, 0, 0, 1, 2]), countersMade: pick(rng, [0, 0, 1, 2]),
    totKilled: int(rng, 0, 300), totWounded: int(rng, 0, 300),
    onMap: rng() < 0.5, mapX: int(rng, 5, 95), mapY: int(rng, 5, 95),
    facing: pick(rng, [0, 45, 90, 135, 180, 270]), tokenScale: 1,
  };
}

function makeCommander(rng, id, factionId){
  return {
    id, name: `Полководец ${id}`, factionId,
    buffMorale: pick(rng, [0, 0, 10, 20, -10]), buffDisc: pick(rng, [0, 0, 5, 10, -5]),
    buffDmg: pick(rng, [0, 0, 10, 15, -10]), buffDef: pick(rng, [0, 0, 10, 20]),
  };
}

export function buildScenarios(count = 400, seed = 20260930){
  const rng = mulberry32(seed);
  const list = [];
  for(let i = 0; i < count; i++){
    const factions = [{id: 1, name: "Сторона А", color: "#B0402E"}, {id: 2, name: "Сторона Б", color: "#5A8FB0"}];
    const commanders = [];
    if(rng() < 0.5) commanders.push(makeCommander(rng, 10, 1));
    if(rng() < 0.5) commanders.push(makeCommander(rng, 11, 2));
    const cmdA = commanders.find(c => c.factionId === 1);
    const cmdB = commanders.find(c => c.factionId === 2);
    const A = makeUnit(rng, 101, 1, cmdA && rng() < 0.8 ? cmdA.id : null);
    // иногда цель из той же фракции — проверка запрета
    const sameFaction = rng() < 0.03;
    const B = makeUnit(rng, 202, sameFaction ? 1 : 2, cmdB && rng() < 0.8 ? cmdB.id : null);

    const kindRoll = rng();
    let action;
    if(kindRoll < 0.72){
      action = {kind: "battle", att: A.id, def: B.id, req: {
        mode: pick(rng, MODES), sitPct: pick(rng, [0, 0, 20, 40]),
        fatigueMode: pick(rng, ["percent", "flat"]), mutual: rng() < 0.9,
        charge: rng() < 0.6, counterCharge: rng() < 0.4,
      }};
    } else if(kindRoll < 0.82){
      action = {kind: "morale", id: A.id};
    } else if(kindRoll < 0.92){
      action = {kind: "flee", id: A.id};
    } else {
      action = {kind: "endTurn"};
    }
    list.push({ n: i, seed: 1000 + i, state: {factions, subfactions: [], commanders, units: [A, B], log: [], turn: 1, nextId: 500}, action });
  }
  return list;
}
