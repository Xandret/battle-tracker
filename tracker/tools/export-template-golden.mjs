// ═══════════ export-template-golden.mjs — общие сценарии шаблонов отрядов для движка на C# ═══════════
// Замораживает tests/templatecases.mjs в shared/golden/templates.json. C# (core/) отыгрывает их и обязан совпасть.
// Запуск: node tools/export-template-golden.mjs — только при намеренной правке units.js (guessUnitType) или
// templates.js, и тогда в том же коммите правится core/Package/Runtime/Templates.cs (правило 7).
import fs from "node:fs";
import { buildTemplateCases } from "../tests/templatecases.mjs";

const out = new URL("../../shared/golden/templates.json", import.meta.url);
fs.writeFileSync(out, JSON.stringify(buildTemplateCases(), null, 1) + "\n");
console.log("записано:", out.pathname, fs.statSync(out).size, "байт");
