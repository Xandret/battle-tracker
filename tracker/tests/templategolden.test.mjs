// Общие сценарии шаблонов отрядов заморожены в shared/golden/templates.json — по ним сверяется движок на C# (правило 7).
// Упал этот тест — значит, подбор шаблона или правки посчитались иначе, чем записано. Если правка намеренная:
// node tools/export-template-golden.mjs и в том же коммите — та же правка в core/Package/Runtime/Templates.cs.
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { buildTemplateCases } from "./templatecases.mjs";

test("шаблоны отрядов: общие сценарии совпадают с замороженными в shared/golden/templates.json", () => {
  const frozen = JSON.parse(fs.readFileSync(new URL("../../shared/golden/templates.json", import.meta.url), "utf8"));
  assert.deepEqual(JSON.parse(JSON.stringify(buildTemplateCases())), frozen);
});
