// ═══════════ discord-export.mjs — выгрузка каналов сервера ботом ═══════════
// node tools/discord-export.mjs            — все серверы бота, все доступные каналы и ветки
// node tools/discord-export.mjs --guild ID — только один сервер
// Результат: discord-export/<сервер>/<канал>.jsonl (одно сообщение на строку, от старых к новым).
// Повторный запуск докачивает только новые сообщения. Токен — в .env: DISCORD_BOT_TOKEN=...
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { loadEnv } from "./env.mjs";

loadEnv();
const TOKEN = process.env.DISCORD_BOT_TOKEN;
if(!TOKEN) throw new Error("DISCORD_BOT_TOKEN не задан (.env)");
const onlyGuild = process.argv.includes("--guild") ? process.argv[process.argv.indexOf("--guild") + 1] : null;
const OUT = fileURLToPath(new URL("../discord-export/", import.meta.url));
const API = "https://discord.com/api/v10";
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function api(route){
  for(;;){
    const res = await fetch(API + route, { headers: { Authorization: `Bot ${TOKEN}`, "User-Agent": "battle-tracker-export (local script, 1.0)" } });
    if(res.status === 429){
      const { retry_after = 1 } = await res.json().catch(() => ({}));
      await sleep(retry_after * 1000 + 100);
      continue;
    }
    if(res.status === 401) throw new Error("401: токен неверный");
    if(!res.ok) return { error: res.status };
    // не упираемся в лимит: если ведро пустое — ждём сброса
    if(res.headers.get("x-ratelimit-remaining") === "0")
      await sleep(parseFloat(res.headers.get("x-ratelimit-reset-after") || "1") * 1000 + 50);
    return { data: await res.json() };
  }
}

const safe = s => s.replace(/[\\/:*?"<>|]/g, "_").trim() || "_";
const TEXT = new Set([0, 5, 15]); // текст, объявления, форум (у форума — ветки)

async function exportChannel(dir, ch, label){
  const file = path.join(dir, safe(label) + ".jsonl");
  let last = "0";
  if(fs.existsSync(file)){
    const lines = fs.readFileSync(file, "utf8").trimEnd().split("\n");
    if(lines[lines.length - 1]) last = JSON.parse(lines[lines.length - 1]).id;
  }
  let added = 0;
  for(;;){
    const r = await api(`/channels/${ch.id}/messages?limit=100&after=${last}`);
    if(r.error){ console.log(`  ${label}: пропущен (HTTP ${r.error})`); return; }
    if(!r.data.length) break;
    const page = r.data.sort((a, b) => (BigInt(a.id) < BigInt(b.id) ? -1 : 1));
    fs.appendFileSync(file, page.map(m => JSON.stringify(m)).join("\n") + "\n");
    last = page[page.length - 1].id;
    added += page.length;
  }
  if(added) console.log(`  ${label}: +${added}`);
}

const guilds = (await api("/users/@me/guilds")).data ?? [];
if(!guilds.length) console.log("Бот не состоит ни на одном сервере — сначала добавьте его по ссылке-приглашению.");
for(const g of guilds){
  if(onlyGuild && g.id !== onlyGuild) continue;
  console.log(`Сервер: ${g.name}`);
  const dir = path.join(OUT, safe(g.name));
  fs.mkdirSync(dir, { recursive: true });
  const channels = (await api(`/guilds/${g.id}/channels`)).data ?? [];
  const byId = new Map(channels.map(c => [c.id, c]));
  const targets = [];
  for(const c of channels.filter(c => TEXT.has(c.type))){
    const cat = byId.get(c.parent_id)?.name;
    if(c.type !== 15) targets.push([c, cat ? `${cat}__${c.name}` : c.name]);
    // ветки: активные придут одним запросом ниже, архивные — по каналу
    const arch = await api(`/channels/${c.id}/threads/archived/public?limit=100`);
    for(const t of arch.data?.threads ?? []) targets.push([t, `${c.name}__ветка__${t.name}_${t.id}`]);
  }
  for(const t of (await api(`/guilds/${g.id}/threads/active`)).data?.threads ?? [])
    targets.push([t, `${byId.get(t.parent_id)?.name ?? "?"}__ветка__${t.name}_${t.id}`]);
  const seen = new Set();
  for(const [c, label] of targets){
    if(seen.has(c.id)) continue;
    seen.add(c.id);
    await exportChannel(dir, c, label);
  }
}
console.log("Готово →", OUT);
