// ═══════════ discord-webhook.mjs — патчноут в Discord через вебхук: нарезка и отправка ═══════════
// Библиотека для tools/discord-post.mjs (команда — npm run discord:post). Запись одной версии из PATCHNOTES.md
// режется на сообщения не длиннее 2000 символов (у вебхука свой предел, Nitro на него не действует): по разделам
// «## …», слишком длинный раздел — по строкам, блок кода не рвётся (закрывается и открывается заново).
// Отправка — по порядку, на 429 ждёт retry_after и повторяет, упоминания выключены, ссылки без превью.
// Адрес вебхука — секрет: DISCORD_WEBHOOK_URL в tracker/.env (или в окружении); запасной вариант — первая строка
// tracker/discord-webhook.txt. Оба файла — в .gitignore. Принимается только https://discord.com/api/webhooks/….
import fs from "node:fs";

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

// Все записи по порядку файла (свежая — первая): [{version, text}]
export function allEntries(md){
  const out = [];
  let inCode = false;
  for(const line of md.replace(/\r\n/g, "\n").split("\n")){
    if(line.startsWith("```")) inCode = !inCode;
    const m = !inCode && /^# .*\bv(\d+\.\d+)(?![\d.])/.exec(line);
    if(m && !out.some(e => e.version === m[1])) out.push({version: m[1], text: entryOf(md, m[1])});
  }
  return out;
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

// Адрес вебхука: переменная окружения (её заполняет .env), иначе первая строка discord-webhook.txt
export function webhookUrl(root){
  if(process.env.DISCORD_WEBHOOK_URL) return process.env.DISCORD_WEBHOOK_URL.trim();
  const f = new URL("discord-webhook.txt", root);
  if(fs.existsSync(f)) return (fs.readFileSync(f, "utf8").split(/\r?\n/).find(l => l.trim()) || "").trim();
  return "";
}
