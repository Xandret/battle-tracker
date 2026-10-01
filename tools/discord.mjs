// ═══════════ discord.mjs — нарезка патчноута на сообщения Discord ═══════════
// Лимит сообщения с Nitro — 4000 символов. Режем по заголовкам разделов (## …),
// не разрывая блоки кода; каждая часть подписана версией и номером.
// Запуск: node tools/discord.mjs  →  dist/discord/part-1.md, part-2.md …
import fs from "node:fs";

// Для вебхука лимит 2000 (4000 — только у обычного аккаунта с Nitro): DISCORD_LIMIT=2000
const LIMIT = Number(process.env.DISCORD_LIMIT) || 4000;
const ROOT = new URL("../", import.meta.url);
const pkg = JSON.parse(fs.readFileSync(new URL("package.json", ROOT), "utf8"));
const md = fs.readFileSync(new URL("PATCHNOTES.md", ROOT), "utf8").trim();
const version = pkg.version.replace(/\.0$/, "");

// 1. Разбиваем на блоки по заголовкам верхних уровней, следя за ``` внутри
const blocks = [];
let cur = [], inCode = false;
for(const line of md.split("\n")){
  if(line.startsWith("```")) inCode = !inCode;
  if(!inCode && /^#{1,2} /.test(line) && cur.length){ blocks.push(cur.join("\n")); cur = []; }
  cur.push(line);
}
if(cur.length) blocks.push(cur.join("\n"));

// 2. Жадно собираем части, оставляя запас под подпись
const RESERVE = 60;
const parts = [];
let buf = "";
for(const b of blocks){
  if(b.length > LIMIT - RESERVE) throw new Error("раздел длиннее лимита Discord: " + b.slice(0, 60));
  if((buf + "\n\n" + b).length > LIMIT - RESERVE && buf){ parts.push(buf); buf = b; }
  else buf = buf ? buf + "\n\n" + b : b;
}
if(buf) parts.push(buf);

const dir = new URL("dist/discord/", ROOT);
fs.rmSync(dir, { recursive: true, force: true });
fs.mkdirSync(dir, { recursive: true });
parts.forEach((p, i) => {
  const text = p + `\n\n-# v${version} · часть ${i + 1}/${parts.length}`;
  fs.writeFileSync(new URL(`part-${i + 1}.md`, dir), text);
  console.log(`часть ${i + 1}/${parts.length}: ${text.length} символов`);
});
