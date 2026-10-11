// Общие сценарии облика отряда (В16) заморожены в shared/golden/looks.json — по ним сверяется игра на Unity.
// Упал этот тест — значит, облик угадывается иначе, чем записано. Если правка намеренная:
// node tools/export-look-golden.mjs и в том же коммите — та же правка в game/Assets/Scripts/Art/Styles.cs.
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { buildLookCases } from "./lookcases.mjs";
import { guessKit, defaultStyle, styleOf, kitOf } from "../src/engine/looks.js";

test("облик отряда: общие сценарии совпадают с замороженными в shared/golden/looks.json", () => {
  const frozen = JSON.parse(fs.readFileSync(new URL("../../shared/golden/looks.json", import.meta.url), "utf8"));
  assert.deepEqual(JSON.parse(JSON.stringify(buildLookCases())), frozen);
});

test("облик отряда: смысл угадывания", () => {
  // пеший отряд с «рыцарями» в названии — пешие рыцари, не конница
  assert.equal(guessKit("Рыцари Лоутайда", "infantry", "melee"), "sword");
  assert.equal(guessKit("Рыцари Лоутайда", "cavalry", "melee"), "lance");
  // тип по умолчанию не мешает названию: лучники остались «пехотой ближнего боя»
  assert.equal(guessKit("Лоутайдские лучники", "infantry", "melee"), "bow");
  assert.equal(guessKit("Ополчение Гринпорта с Алебардами", "infantry", "melee"), "pike");
  // нераспознанная пехота — копейщики, а не ополчение
  assert.equal(guessKit("Отряд 82", "infantry", "melee"), "spear");
  assert.equal(guessKit("Наёмные арбалетчики", "infantry", "melee"), "crossbow");
  // стиль: как у прошлого отряда фракции; у первого — по названию, иначе западный
  const units = [{id: 1, factionId: 7, style: "north"}];
  assert.equal(defaultStyle(units, 7, "Самураи"), "north");
  assert.equal(defaultStyle(units, 8, "Самураи"), "fareast");
  assert.equal(defaultStyle(units, 8, "Копейщики"), "west");
  assert.equal(defaultStyle(units, 7, "Самураи", 1), "fareast");
  // записанное — важнее угаданного; нет — угадывается
  assert.equal(styleOf({name: "Самураи", style: "south"}), "south");
  assert.equal(styleOf({name: "Самураи"}), "fareast");
  assert.equal(kitOf({name: "Лучники", type: "archer", weapon: "ranged", kit: "pike"}), "pike");
  assert.equal(kitOf({name: "Лучники", type: "archer", weapon: "ranged", kit: "чепуха"}), "bow");
});
