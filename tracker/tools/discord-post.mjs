// ═══════════ discord-post.mjs — публикация патчноута в канал Discord вебхуком ═══════════
// npm run discord:post                         — пробный прогон: что уйдёт, по частям; ничего не отправляет
// npm run discord:post -- --send               — отправить свежий патч (запись текущей версии из package.json)
// npm run discord:post -- --send --version 30.8 — отправить запись другой версии
// npm run discord:post -- --send --all         — всю историю, от старых записей к свежей
// Адрес вебхука — DISCORD_WEBHOOK_URL в tracker/.env (файл вне git). Нарезка и отправка — tools/discord-webhook.mjs:
// сообщения до 2000 символов (предел вебхука), блоки кода не рвутся, на 429 ждём и повторяем.
import fs from "node:fs";
import { loadEnv } from "./env.mjs";
import { entryOf, allEntries, splitForWebhook, withFooter, sendParts, webhookUrl } from "./discord-webhook.mjs";

loadEnv();
const ROOT = new URL("../", import.meta.url);
const args = process.argv.slice(2);
const SEND = args.includes("--send"), ALL = args.includes("--all");
const vi = args.indexOf("--version");
const pkg = JSON.parse(fs.readFileSync(new URL("package.json", ROOT), "utf8"));
const md = fs.readFileSync(new URL("PATCHNOTES.md", ROOT), "utf8");

let entries;
if(ALL) entries = allEntries(md).reverse();
else {
  const version = vi >= 0 && args[vi + 1] ? args[vi + 1] : pkg.version.replace(/\.0$/, "");
  const text = entryOf(md, version);
  if(!text){ console.error(`В PATCHNOTES.md нет записи v${version}`); process.exit(1); }
  entries = [{version, text}];
}
const messages = entries.flatMap(e => withFooter(splitForWebhook(e.text), e.version));
console.log(`${entries.length === 1 ? `Патчноут v${entries[0].version}` : `Патчноуты v${entries[0].version}…v${entries[entries.length - 1].version}`}: ${messages.length} сообщ.`);

if(!SEND){
  messages.forEach((m, i) => console.log(`\n────── сообщение ${i + 1}/${messages.length} · ${m.length} символов ──────\n${m}`));
  console.log("\nЭто пробный прогон — ничего не отправлено. Отправить: npm run discord:post -- --send");
} else {
  const url = webhookUrl(ROOT);
  if(!url){
    console.error("Нет адреса вебхука: добавь строку DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/… в tracker/.env (файл не попадает в git).");
    process.exit(1);
  }
  try{ await sendParts(url, messages); console.log("Готово."); }
  catch(e){ console.error("✘ " + e.message); process.exit(1); }
}
