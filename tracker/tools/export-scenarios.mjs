// ═══════════ export-scenarios.mjs — входные данные эталона для других реализаций движка ═══════════
// 400 сценариев из tests/scenarios.mjs замораживаются в shared/golden/scenarios.json.
// Движок на C# (core/) читает их вместе с golden.json и обязан выдать те же строки журнала и те же отряды.
// Запуск: node tools/export-scenarios.mjs (нужен, только если меняется генератор сценариев)
import fs from "node:fs";
import { buildScenarios } from "../tests/scenarios.mjs";

const out = new URL("../../shared/golden/scenarios.json", import.meta.url);
fs.writeFileSync(out, JSON.stringify(buildScenarios()));
console.log("записано:", out.pathname);
