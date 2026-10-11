// ═══════════ discord.test.mjs — патчноут в Discord через вебхук (tools/discord-webhook.mjs, npm run discord:post) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { entryOf, allEntries, splitForWebhook, withFooter, sendParts, LIMIT, WEBHOOK_RE } from "../tools/discord-webhook.mjs";

const md = fs.readFileSync(new URL("../PATCHNOTES.md", import.meta.url), "utf8");
const fences = s => (s.match(/^```/gm) || []).length;

test("запись одной версии: от её заголовка до следующего, без «---»", () => {
  const e = entryOf(md, "30.8");
  assert.ok(e.startsWith("# ⚔ Журнал боевых действий — v30.8 «Пролом»"));
  assert.ok(!e.includes("v30.9") && !e.includes("v30.7 «"), "только своя запись");
  assert.ok(!e.trimEnd().endsWith("---"));
  assert.equal(entryOf(md, "30.0"), entryOf(md, "30.0"), "старые записи тоже находятся");
  assert.ok(entryOf(md, "30.0").includes("Фундамент"));
  assert.equal(entryOf(md, "99.1"), null);
});

test("все записи файла по порядку, у каждой — своя версия", () => {
  const all = allEntries(md);
  assert.ok(all.length >= 11);
  assert.equal(all[all.length - 1].version, "30.0", "последняя — самая старая");
  for(const e of all) assert.equal(e.text, entryOf(md, e.version));
});

test("каждая запись режется на сообщения до 2000 символов, код не рвётся, строки не теряются", () => {
  for(const v of ["30.9", "30.8", "30.7", "30.6", "30.5", "30.1", "30.0"]){
    const e = entryOf(md, v);
    const msgs = withFooter(splitForWebhook(e), v);
    for(const m of msgs){
      assert.ok(m.length <= LIMIT, `v${v}: ${m.length} символов`);
      assert.equal(fences(m) % 2, 0, `v${v}: блок кода не закрыт`);
    }
    const lines = e.split("\n").filter(l => l.trim() && !l.startsWith("```"));
    const all = msgs.join("\n");
    for(const l of lines) assert.ok(all.includes(l), `v${v}: потерялась строка «${l.slice(0, 40)}»`);
    assert.match(msgs[msgs.length - 1], new RegExp(`-# v${v.replace(".", "\\.")} · часть ${msgs.length}/${msgs.length}$`));
  }
});

test("длинный раздел и длинный блок кода режутся с повторным открытием блока", () => {
  const code = Array.from({length: 120}, (_, i) => `строка кода ${i} — ` + "x".repeat(20)).join("\n");
  const text = "# Заголовок v1.0\n\nВступление\n\n## Раздел\n```\n" + code + "\n```\nхвост раздела";
  const msgs = withFooter(splitForWebhook(text), "1.0");
  assert.ok(msgs.length >= 3);
  for(const m of msgs){ assert.ok(m.length <= LIMIT); assert.equal(fences(m) % 2, 0); }
  for(let i = 0; i < 120; i++) assert.ok(msgs.some(m => m.includes(`строка кода ${i} — `)), `строка ${i}`);
});

test("отправка: только адрес вебхука Discord, по порядку, 429 — ждём и повторяем", async () => {
  assert.ok(WEBHOOK_RE.test("https://discord.com/api/webhooks/123456/abc_DEF-9"));
  assert.ok(!WEBHOOK_RE.test("https://example.com/api/webhooks/1/x"));
  assert.ok(!WEBHOOK_RE.test("http://discord.com/api/webhooks/1/x"), "только https");
  await assert.rejects(sendParts("https://evil.example/api/webhooks/1/x", ["a"], {fetchImpl: () => { throw new Error("не должно"); }}), /не адрес вебхука/);
  const sent = [], waits = [];
  let first = true;
  const fetchImpl = async (url, opt) => {
    const body = JSON.parse(opt.body);
    if(first){ first = false; return {status: 429, ok: false, json: async () => ({retry_after: 1.5})}; }
    sent.push({url, body});
    return {status: 200, ok: true};
  };
  await sendParts("https://discord.com/api/webhooks/1/key", ["первая", "вторая"], {fetchImpl, sleep: async ms => { waits.push(ms); }, log: () => {}});
  assert.deepEqual(sent.map(s => s.body.content), ["первая", "вторая"]);
  assert.equal(sent[0].url, "https://discord.com/api/webhooks/1/key?wait=true");
  assert.deepEqual(sent[0].body.allowed_mentions, {parse: []}, "никаких @everyone");
  assert.equal(waits[0], 1600, "ждём столько, сколько сказал Discord");
  await assert.rejects(sendParts("https://discord.com/api/webhooks/1/key", ["x"],
    {fetchImpl: async () => ({status: 404, ok: false, text: async () => "Unknown Webhook"}), sleep: async () => {}, log: () => {}}), /404 Unknown Webhook/);
});
