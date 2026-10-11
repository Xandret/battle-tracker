// ═══════════ env.mjs — минимальный загрузчик .env (без зависимостей) ═══════════
import fs from "node:fs";

export function loadEnv(){
  let text = "";
  try { text = fs.readFileSync(new URL("../.env", import.meta.url), "utf8"); } catch { /* файла нет — берём process.env */ }
  for(const line of text.split(/\r?\n/)){
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/);
    if(!m || line.trimStart().startsWith("#")) continue;
    if(process.env[m[1]] === undefined) process.env[m[1]] = m[2].replace(/^(['"])(.*)\1$/, "$2");
  }
}
