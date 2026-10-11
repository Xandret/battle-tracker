// ═══════════ export-map-golden.mjs — общие сценарии карты 6а для движка на C# ═══════════
// Замораживает tests/mapcases.mjs в shared/golden/map.json. C# (core/) отыгрывает их и обязан совпасть.
// Запуск: node tools/export-map-golden.mjs — только при намеренной правке terrain / mapgen / battlemap / panic,
// и тогда в том же коммите правится core/ (правило 7).
import fs from "node:fs";
import { buildMapCases } from "../tests/mapcases.mjs";

const out = new URL("../../shared/golden/map.json", import.meta.url);
fs.writeFileSync(out, JSON.stringify(buildMapCases()));
console.log("записано:", out.pathname, fs.statSync(out).size, "байт");
