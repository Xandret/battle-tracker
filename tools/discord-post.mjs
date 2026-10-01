// ═══════════ discord-post.mjs — публикация патчноута вебхуком ═══════════
// node tools/discord-post.mjs          — dry-run: показывает, что уйдёт, ничего не отправляет
// node tools/discord-post.mjs --send   — отправляет части по порядку
// URL вебхука — в .env: DISCORD_WEBHOOK_URL=...
import fs from "node:fs";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { loadEnv } from "./env.mjs";

loadEnv();
const SEND = process.argv.includes("--send");
const LIMIT = 2000; // лимит сообщения вебхука (Nitro на вебхуки не распространяется)

// Свежая нарезка под лимит вебхука
execFileSync(process.execPath, [fileURLToPath(new URL("discord.mjs", import.meta.url))], {
  env: { ...process.env, DISCORD_LIMIT: String(LIMIT) }, stdio: "inherit",
});

const dir = new URL("../dist/discord/", import.meta.url);
const files = fs.readdirSync(dir).filter(f => /^part-\d+\.md$/.test(f))
  .sort((a, b) => parseInt(a.slice(5)) - parseInt(b.slice(5)));
const parts = files.map(f => fs.readFileSync(new URL(f, dir), "utf8"));
for(const [i, p] of parts.entries()) if(p.length > LIMIT) throw new Error(`часть ${i + 1} длиннее ${LIMIT}`);

if(!SEND){
  console.log("\n--- DRY-RUN: ничего не отправлено ---");
  parts.forEach((p, i) => console.log(`\n=== часть ${i + 1}/${parts.length} (${p.length}) ===\n${p}`));
  console.log("\nЧтобы отправить: node tools/discord-post.mjs --send");
  process.exit(0);
}

const url = process.env.DISCORD_WEBHOOK_URL;
if(!url || !/^https:\/\/(discord|discordapp)\.com\/api\/webhooks\//.test(url))
  throw new Error("DISCORD_WEBHOOK_URL не задан или не похож на вебхук Discord (.env)");

for(const [i, content] of parts.entries()){
  for(;;){
    const res = await fetch(url + "?wait=true", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content, allowed_mentions: { parse: [] } }),
    });
    if(res.status === 429){
      const { retry_after = 1 } = await res.json().catch(() => ({}));
      await new Promise(r => setTimeout(r, retry_after * 1000 + 100));
      continue;
    }
    if(!res.ok) throw new Error(`часть ${i + 1}: HTTP ${res.status} ${await res.text()}`);
    console.log(`отправлено ${i + 1}/${parts.length}`);
    break;
  }
  await new Promise(r => setTimeout(r, 700));
}
