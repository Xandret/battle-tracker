// ═══════════ discord-search.mjs — поиск по выгруженным каналам ═══════════
// node tools/discord-search.mjs "текст" [--author имя] [--channel часть-названия] [--limit 50]
// Регистр не важен. Ищет по тексту сообщения, вложениям и эмбедам.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const args = process.argv.slice(2);
const opt = n => { const i = args.indexOf("--" + n); return i >= 0 ? args.splice(i, 2)[1] : null; };
const author = opt("author")?.toLowerCase();
const channel = opt("channel")?.toLowerCase();
const limit = Number(opt("limit")) || 50;
const query = args.join(" ").toLowerCase();
if(!query && !author) throw new Error('Использование: node tools/discord-search.mjs "текст" [--author имя] [--channel имя] [--limit N]');

const ROOT = fileURLToPath(new URL("../discord-export/", import.meta.url));
if(!fs.existsSync(ROOT)) throw new Error("Нет выгрузки — сначала node tools/discord-export.mjs");

let found = 0;
outer:
for(const guild of fs.readdirSync(ROOT)){
  for(const f of fs.readdirSync(path.join(ROOT, guild)).filter(f => f.endsWith(".jsonl"))){
    if(channel && !f.toLowerCase().includes(channel)) continue;
    for(const line of fs.readFileSync(path.join(ROOT, guild, f), "utf8").split("\n")){
      if(!line) continue;
      const m = JSON.parse(line);
      const name = (m.author?.global_name || m.author?.username || "").toLowerCase();
      if(author && !name.includes(author) && !(m.author?.username || "").toLowerCase().includes(author)) continue;
      const hay = [m.content, ...(m.attachments || []).map(a => a.filename), ...(m.embeds || []).map(e => `${e.title ?? ""} ${e.description ?? ""}`)].join(" ").toLowerCase();
      if(query && !hay.includes(query)) continue;
      console.log(`[${m.timestamp.slice(0, 16).replace("T", " ")}] ${f.replace(/\.jsonl$/, "")} · ${m.author?.global_name || m.author?.username}: ${(m.content || "").replace(/\n/g, " ").slice(0, 300)}`);
      if(++found >= limit) break outer;
    }
  }
}
console.log(`\nНайдено: ${found}${found >= limit ? " (достигнут лимит, уточните запрос или --limit)" : ""}`);
