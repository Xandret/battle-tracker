// Общие сценарии карты 6а заморожены в shared/golden/map.json — по ним сверяется движок на C# (правило 7).
// Упал этот тест — значит, JS-карта посчитала иначе, чем записано. Если правка намеренная:
// node tools/export-map-golden.mjs и в том же коммите — та же правка в core/.
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { buildMapCases } from "./mapcases.mjs";

test("карта 6а: общие сценарии совпадают с замороженными в shared/golden/map.json", () => {
  const frozen = JSON.parse(fs.readFileSync(new URL("../../shared/golden/map.json", import.meta.url), "utf8"));
  assert.deepEqual(JSON.parse(JSON.stringify(buildMapCases())), frozen);
});
