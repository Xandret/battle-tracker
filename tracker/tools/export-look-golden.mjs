// ═══════════ export-look-golden.mjs — общие сценарии облика отряда (В16) для игры на Unity ═══════════
// Замораживает tests/lookcases.mjs в shared/golden/looks.json. Unity (Styles.cs) сверяется по нему:
// cd game/Tools/bench && dotnet run -c Release -- looks.
// Запуск: node tools/export-look-golden.mjs — только при намеренной правке looks.js, и тогда в том же коммите —
// та же правка в game/Assets/Scripts/Art/Styles.cs.
import fs from "node:fs";
import { buildLookCases } from "../tests/lookcases.mjs";

const out = new URL("../../shared/golden/looks.json", import.meta.url);
const c = buildLookCases();
// по строке на случай — файл читается глазами и в диффе видно, что поменялось
const rows = a => "[\n" + a.map(r => "  " + JSON.stringify(r)).join(",\n") + "\n ]";
fs.writeFileSync(out, `{\n "kits": ${rows(c.kits)},\n "styles": ${rows(c.styles)},\n "units": ${rows(c.units)},\n "defaults": ${rows(c.defaults)}\n}\n`);
console.log("записано:", out.pathname, fs.statSync(out).size, "байт");
