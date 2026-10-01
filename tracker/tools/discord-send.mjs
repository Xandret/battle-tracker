// ═══════════ discord-send.mjs — патчноут в канал Discord через вебхук ═══════════
// Берёт из PATCHNOTES.md запись одной версии (по умолчанию — текущей из package.json), режет её на сообщения
// не длиннее 2000 символов (у вебхука свой предел, Nitro на него не действует), не разрывая блоки кода,
// и отправляет по порядку. Discord ответил «слишком часто» (429) — ждёт, сколько он сказал, и повторяет.
//
// Адрес вебхука — секрет: кто его знает, тот пишет в канал. Он лежит вне git:
//   переменная окружения DISCORD_WEBHOOK_URL или первая строка файла tracker/discord-webhook.txt (в .gitignore).
// Принимается только адрес вида https://discord.com/api/webhooks/<номер>/<ключ>.
//
// Запуск (из папки tracker):
//   node tools/discord-send.mjs --dry          показать части, ничего не отправляя
//   node tools/discord-send.mjs                отправить запись текущей версии
//   node tools/discord-send.mjs --version 30.8 отправить запись другой версии
import fs from "node:fs";
import { pathToFileURL } from "node:url";

export const LIMIT = 2000;
const RESERVE = 60;   // запас под подпись «-# v30.9 · часть 1/3»
export const WEBHOOK_RE = /^https:\/\/(?:(?:ptb|canary)\.)?discord(?:app)?\.com\/api\/webhooks\/\d+\/[\w-]+$/;

// Запись версии: от её заголовка «# … — v30.9 «…»» до следующего заголовка первого уровня
export function entryOf(md, version){
  const lines = md.replace(/\r\n/g, "\n").split("\n");
  let start = -1, inCode = false;
  const head = new RegExp(`^# .*\\bv${version.replace(/\./g, "\\.")}(?![\\d.])`);
  for(let i = 0; i < lines.length; i++){
    if(lines[i].startsWith("```")) inCode = !inCode;
    if(inCode || !lines[i].startsWith("# ")) continue;
    if(start >= 0) return lines.slice(start, i).join("\n").replace(/\n+---\s*$/, "").trim();
    if(head.test(lines[i])) start = i;
  }
  return start >= 0 ? lines.slice(start).join("\n").replace(/\n+---\s*$/, "").trim() : null;
}

// Режем запись на сообщения: разделы «## …» целиком, слишком длинный раздел — по строкам; если кусок
// кончается внутри блока кода, блок закрывается и открывается заново в следующем куске
export function splitForWebhook(text, limit = LIMIT){
  const max = limit - RESERVE;
  const blocks = [];
  let cur = [], inCode = false;
  for(const line of text.split("\n")){
    if(line.startsWith("```")) inCode = !inCode;
    if(!inCode && /^#{1,2} /.test(line) && cur.length){ blocks.push(cur.join("\n")); cur = []; }
    cur.push(line);
  }
  if(cur.length) blocks.push(cur.join("\n"));
  const pieces = [];
  for(const b of blocks){
    if(b.length <= max){ pieces.push(b); continue; }
    let buf = [], len = 0, code = false, fence = "```";
    const flush = () => {
      if(!buf.length) return;
      pieces.push(code ? buf.join("\n") + "\n```" : buf.join("\n"));
      buf = code ? [fence] : []; len = code ? fence.length : 0;
    };
    for(let line of b.split("\n")){
      while(line.length > max - 8){ flush(); pieces.push(line.slice(0, max - 8)); line = line.slice(max - 8); }
      if(len + line.length + 1 > max - 4) flush();
      buf.push(line); len += line.length + 1;
      if(line.startsWith("```")){ code = !code; if(code) fence = line; }
    }
    if(buf.length && !(buf.length === 1 && buf[0] === fence && code)) pieces.push(buf.join("\n"));
  }
  const parts = [];
  let acc = "";
  for(const p of pieces){
    if(acc && (acc + "\n\n" + p).length > max){ parts.push(acc); acc = p; }
    else acc = acc ? acc + "\n\n" + p : p;
  }
  if(acc) parts.push(acc);
  return parts;
}
export const withFooter = (parts, version) => parts.map((p, i) => `${p}\n\n-# v${version} · часть ${i + 1}/${parts.length}`);

// Отправка по порядку; fetchImpl и sleep подменяются в тесте
export async function sendParts(url, messages, {fetchImpl = fetch, sleep = ms => new Promise(r => setTimeout(r, ms)), log = console.log} = {}){
  if(!WEBHOOK_RE.test(url)) throw new Error("это не адрес вебхука Discord (нужен https://discord.com/api/webhooks/…)");
  for(let i = 0; i < messages.length; i++){
    for(let attempt = 1; ; attempt++){
      const res = await fetchImpl(url + "?wait=true", {
        method: "POST", headers: {"Content-Type": "application/json"},
        // упоминания выключены (в тексте нет @everyone по ошибке), ссылки — без превью
        body: JSON.stringify({content: messages[i], allowed_mentions: {parse: []}, flags: 4}),
      });
      if(res.status === 429 && attempt < 6){
        let wait = 1;
        try{ wait = +(await res.json()).retry_after || 1; }catch(e){}
        log(`  Discord просит подождать ${wait} с — жду и повторяю часть ${i + 1}`);
        await sleep(Math.ceil(wait * 1000) + 100);
        continue;
      }
      if(!res.ok){
        let why = "";
        try{ why = (await res.text()).slice(0, 300); }catch(e){}
        throw new Error(`часть ${i + 1}/${messages.length}: Discord ответил ${res.status} ${why}`);
      }
      log(`  ✔ часть ${i + 1}/${messages.length} отправлена (${messages[i].length} символов)`);
      break;
    }
    if(i < messages.length - 1) await sleep(700);   // по порядку и без спама
  }
}

function webhookUrl(root){
  if(process.env.DISCORD_WEBHOOK_URL) return process.env.DISCORD_WEBHOOK_URL.trim();
  const f = new URL("discord-webhook.txt", root);
  if(fs.existsSync(f)) return (fs.readFileSync(f, "utf8").split(/\r?\n/).find(l => l.trim()) || "").trim();
  return "";
}

async function main(){
  const ROOT = new URL("../", import.meta.url);
  const args = process.argv.slice(2);
  const dry = args.includes("--dry");
  const vi = args.indexOf("--version");
  const pkg = JSON.parse(fs.readFileSync(new URL("package.json", ROOT), "utf8"));
  const version = vi >= 0 && args[vi + 1] ? args[vi + 1] : pkg.version.replace(/\.0$/, "");
  const entry = entryOf(fs.readFileSync(new URL("PATCHNOTES.md", ROOT), "utf8"), version);
  if(!entry){ console.error(`В PATCHNOTES.md нет записи v${version}`); process.exit(1); }
  const messages = withFooter(splitForWebhook(entry), version);
  console.log(`Патчноут v${version}: ${messages.length} сообщ.`);
  if(dry){
    messages.forEach((m, i) => console.log(`\n────── часть ${i + 1}/${messages.length} · ${m.length} символов ──────\n${m}`));
    return;
  }
  const url = webhookUrl(ROOT);
  if(!url){
    console.error("Нет адреса вебхука. Положи его первой строкой в tracker/discord-webhook.txt (файл не попадает в git)\n" +
                  "или задай переменную окружения DISCORD_WEBHOOK_URL.");
    process.exit(1);
  }
  await sendParts(url, messages);
  console.log("Готово.");
}

if(process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href){
  main().catch(e => { console.error("✘ " + e.message); process.exit(1); });
}
