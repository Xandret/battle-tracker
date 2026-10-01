// ═══════════ siege.test.mjs — осадные орудия (6б, Ш3–Ш4, Г47, Г49) ═══════════
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../src/engine/index.js";

const R = E.getRules("base"), S = R.siege;
const ctxOf = rng => ({rules: R, rng, commanderOf: () => null, factionName: () => ""});
// генератор, который отдаёт заданные значения по кругу — чтобы вызвать попадание, промах или разрыв наверняка
const seq = (...v) => { let i = 0; return () => v[i++ % v.length]; };
const foot = (o = {}) => Object.assign({id: 1, name: "Пехота", soldiers: 1000, eqDef: 60, exp: 20, morale: 70, discipline: 50,
  broken: false, breakGrace: 0, breakPenalty: 0, totKilled: 0, totWounded: 0, status: "active"}, o);
const castle = () => { const m = E.generateMap("castle", {}, 1); E.buildSections(m, R); return m; };

test("каталог: все десять орудий с числами (Ш3)", () => {
  assert.deepEqual(Object.keys(S.engines), ["ballista", "catapult", "trebuchet", "bombard", "cannon", "mortar", "ribauldequin", "magic", "ram", "tower"]);
  for(const [k, e] of Object.entries(S.engines)){
    assert.ok(e.name && e.crew > 0 && e.minCrew > 0 && e.minCrew <= e.crew && e.hp > 0 && Array.isArray(e.range), k);
    if(!e.ram && !e.tower) assert.ok(e.hitWall.length === 2 && e.hitTroops.length === 2 && e.reload >= 1, k);
  }
  assert.ok(S.engines.bombard.burst && S.engines.cannon.burst && S.engines.mortar.burst && S.engines.ribauldequin.burst, "порох рвётся");
  assert.ok(!S.engines.trebuchet.burst && !S.engines.magic.burst);
  assert.deepEqual(["catapult", "trebuchet", "mortar"], Object.keys(S.engines).filter(k => S.engines[k].indirect), "навесом — трое");
});

test("шанс попасть: по дистанции от «от» до «до», выучка ±10%, вне дальности — нет (Г47)", () => {
  const t = E.makeMachine("trebuchet", 1, R);   // 100–350 м, по стене 60 → 30
  assert.equal(E.hitChance(t, "wall", 100, R), 60);
  assert.equal(E.hitChance(t, "wall", 350, R), 30);
  assert.equal(E.hitChance(t, "wall", 225, R), 45);
  assert.equal(E.hitChance(t, "wall", 99, R), null, "ближе «от» — не достаёт");
  assert.equal(E.hitChance(t, "wall", 351, R), null);
  assert.equal(E.hitChance(Object.assign({}, t, {exp: 100}), "wall", 100, R), 70, "опыт 100 — +10%");
  assert.equal(E.hitChance(Object.assign({}, t, {exp: 0}), "troops", 350, R), 5, "не меньше 5%");
  const mg = E.makeMachine("magic", 3, R);
  assert.equal(E.hitChance(Object.assign({}, mg, {mageSkill: 20}), "troops", 0, R), 95, "навык мага 20 — +30%, но не больше 95%");
});

test("расчёт: орудия молчат без людей, перезарядка дольше при нехватке (Г49)", () => {
  const b = E.makeMachine("bombard", 4, R);       // 10 чел. на орудие, без 2 — молчит
  assert.equal(b.crew, 40);
  assert.equal(E.firingGuns(b, R), 4);
  assert.equal(E.machineReload(b, R), 3);
  const half = Object.assign({}, b, {crew: 20});
  assert.equal(E.firingGuns(half, R), 4, "20 человек — на все четыре по двое");
  assert.equal(E.machineReload(half, R), 6, "людей вдвое меньше — вдвое дольше");
  const few = Object.assign({}, b, {crew: 5});
  assert.equal(E.firingGuns(few, R), 2);
  const r = E.siegeVolley(Object.assign({}, b, {crew: 1}), {unit: foot()}, {dist: 100}, ctxOf(Math.random));
  assert.equal(r.ok, false);
  assert.match(r.lines[0], /не хватает/);
});

test("залп по стене: попадание снимает фиксированную прочность, пролом — как в v30.8", () => {
  const map = castle(), m = E.makeMachine("trebuchet", 4, R);
  const r = E.siegeVolley(m, {section: {map, id: 3, at: null}}, {dist: 100}, ctxOf(seq(0.1)));   // d100 = 11 ≤ 60 — все в цель
  assert.equal(r.ok, true);
  assert.equal(r.stats.hits, 4);
  assert.ok(r.lines.includes("Урон стене: 4 × 25 = 100"));
  assert.equal(r.section.opened, 1, "100 урона по стене 100 — пролом");
  assert.deepEqual(r.machine, {count: 4, crew: 48, ready: 2});
  const miss = E.siegeVolley(m, {section: {map, id: 4, at: null}}, {dist: 100}, ctxOf(seq(0.99)));
  assert.equal(miss.stats.hits, 0);
  assert.ok(miss.lines.includes("Стена цела"));
});

test("залп по людям — как стрельба за столом: d(урон) за попадание ÷ (снар. защиты / 10)", () => {
  // одна пушка, всё в цель: бросок разрыва 51 (нет), попадание 1, урон d80 = 80 → 80 / 6 = 13,3 → 13 солдат
  const c = E.makeMachine("cannon", 1, R);
  const r = E.siegeVolley(c, {unit: foot()}, {dist: 0}, ctxOf(seq(0.5, 0, 0.999, 0.5)));
  assert.equal(r.stats.hits, 1);
  assert.ok(r.lines.includes("Урон: 1 × d80 = 80"));
  assert.ok(r.lines.includes("Защита цели: урон / (60/10) → 80 / 6 = 13.3"));
  assert.ok(r.lines.includes("Потери «Пехота»: 13 солдат"));
  const p = r.patches[0].patch;
  assert.equal(p.soldiers, 987);
  assert.equal(p.morale, 70 - 1 - 10, "−1 за потери и −10 шока пороха");
  // в среднем — как в таблице для ГМа (shared/siege/catalog.md): 64%... по людям 55 → 20 на 0–500 м
  const rng = E.mulberry32(5);
  let sum = 0;
  for(let i = 0; i < 4000; i++) sum += 1000 - E.siegeVolley(c, {unit: foot()}, {dist: 100}, ctxOf(rng)).patches.reduce((a, x) => x.patch.soldiers, 1000);
  const expect = 0.48 * 40.5 / 6 * 0.98;   // шанс 48% на 100 м, d80 в среднем 40,5, без разорванных (2%)
  assert.ok(Math.abs(sum / 4000 - expect) / expect < 0.08, `в среднем ${sum / 4000} против ${expect}`);
});

test("порох: разрыв ствола уносит орудие и его расчёт", () => {
  const b = E.makeMachine("bombard", 3, R);
  // первое орудие: разрыв (d100 = 1 ≤ 3); остальные: без разрыва, мимо
  const r = E.siegeVolley(b, {unit: foot()}, {dist: 100}, ctxOf(seq(0, 0.5, 0.99, 0.5, 0.99)));
  assert.equal(r.stats.bursts, 1);
  assert.deepEqual(r.machine, {count: 2, crew: 20, ready: 3});
  assert.equal(r.tone, "danger");
  assert.ok(r.lines.some(l => l.startsWith("💥 Разрыв ствола")));
  const all = E.siegeVolley(E.makeMachine("cannon", 2, R), {unit: foot()}, {dist: 100}, ctxOf(seq(0)));
  assert.deepEqual(all.machine, {count: 0, crew: 0, ready: 0});
  assert.ok(all.lines.at(-1).startsWith("☠"));
});

test("навесом через стену — без видимости и без укрытия; прямой наводке нужна видимость", () => {
  const u = foot();
  const tr = E.siegeVolley(E.makeMachine("trebuchet", 1, R), {unit: u}, {dist: 150, los: false, coverPct: 50}, ctxOf(seq(0)));
  assert.equal(tr.ok, true);
  assert.ok(tr.lines.includes("Навесом — укрытие цели (50%) не спасает"));
  const bal = E.siegeVolley(E.makeMachine("ballista", 1, R), {unit: u}, {dist: 150, los: false}, ctxOf(seq(0)));
  assert.equal(bal.ok, false);
  assert.match(bal.lines[0], /цели не видно/);
  const cov = E.siegeVolley(E.makeMachine("ballista", 1, R), {unit: u}, {dist: 150, los: true, coverPct: 50}, ctxOf(seq(0, 0.999)));
  assert.ok(cov.lines.some(l => l.startsWith("Укрытие цели: −50%")));
});

test("маг-пушка: без мага молчит; попадание уносит 70–100% × долю батареи; брызги — всем рядом, своим тоже", () => {
  const mg = E.makeMachine("magic", 4, R);
  assert.equal(E.siegeVolley(Object.assign({}, mg, {mageSkill: 0}), {unit: foot()}, {dist: 100}, ctxOf(seq(0))).ok, false);
  const near = foot({id: 2, name: "Свои рядом"}), far = foot({id: 3, name: "Далеко"});
  // попадание (d100 = 1), доля 100% (d31 = 31 → 70 + 30), брызги 15%
  const r = E.siegeVolley(Object.assign({}, mg, {count: 2}), {unit: foot({eqDef: 200}), splash: [{unit: near, gap: 25}, {unit: far, gap: 31}]},
    {dist: 100}, ctxOf(seq(0, 0.999)));
  assert.ok(r.lines.some(l => l.startsWith("Огонь батареи: 100% численности × 2/4 орудий, броня −15%")));
  assert.equal(r.patches[0].patch.soldiers, 1000 - 425, "1000 × 100% × 2/4 × 0,85");
  assert.deepEqual(r.patches.map(p => p.id), [1, 2], "брызги — только ближе 30 м");
  assert.equal(r.patches[1].patch.soldiers, 1000 - 75, "15% × 2/4");
});

test("таран: без броска, ворота — полным уроном, камень — четвертью; только вплотную", () => {
  const map = castle(), ram = E.makeMachine("ram", 1, R);
  const g = E.siegeVolley(ram, {section: {map, id: 19, at: null}}, {dist: 2}, ctxOf(Math.random));
  assert.ok(g.lines.includes("Таран: 1 × 25 = 25 урона"));
  assert.equal(E.getSection(map, 19).dmg, 25);
  const w = E.siegeVolley(ram, {section: {map, id: 3, at: null}}, {dist: 2}, ctxOf(Math.random));
  assert.ok(w.lines.includes("Таран: 1 × 25 × 0.25 — по камню таран слаб = 6.3 урона"));
  assert.equal(E.siegeVolley(ram, {section: {map, id: 3, at: null}}, {dist: 20}, ctxOf(Math.random)).ok, false);
  assert.equal(E.siegeVolley(ram, {unit: foot()}, {dist: 2}, ctxOf(Math.random)).ok, false);
  assert.match(E.siegeVolley(E.makeMachine("tower", 1, R), {unit: foot()}, {dist: 2}, ctxOf(Math.random)).lines[0], /осадная башня/);
});

test("перезарядка и марш: не стреляет, пока не готова; конец хода и марш", () => {
  const c = Object.assign(E.makeMachine("cannon", 2, R), {ready: 2});
  assert.match(E.siegeVolley(c, {unit: foot()}, {dist: 100}, ctxOf(Math.random)).lines[0], /перезаряжается — ещё 2 х\./);
  assert.deepEqual(E.siegeEndTurn(c), {ready: 1, deployLeft: 0});
  const b = E.makeMachine("bombard", 1, R);
  assert.deepEqual(E.machineMoved(b, R), {deployLeft: 2}, "ход марша и ход развёртывания");
  assert.deepEqual(E.machineMoved(E.makeMachine("cannon", 1, R), R), {deployLeft: 1});
  assert.match(E.siegeVolley(Object.assign({}, b, {deployLeft: 1}), {unit: foot()}, {dist: 100}, ctxOf(Math.random)).lines[0], /разворачивается/);
  assert.match(E.siegeVolley(c, {unit: foot()}, {dist: 900}, ctxOf(Math.random)).lines[0], /перезаряжается/);
  assert.match(E.siegeVolley(Object.assign({}, c, {ready: 0}), {unit: foot()}, {dist: 900}, ctxOf(Math.random)).lines[0], /вне дальности — 900 м/);
});

test("удар по машине, взрыв маг-пушки и захват", () => {
  const t = E.makeMachine("trebuchet", 3, R);   // прочность орудия 50
  let h = E.hitMachine(t, 120, R);
  assert.deepEqual(h.patch, {count: 1, crew: 12, dmg: 20});
  h = E.hitMachine(Object.assign({}, t, h.patch), 30, R);
  assert.equal(h.patch.count, 0);
  assert.ok(h.lines.at(-1).startsWith("☠"));
  const mg = Object.assign(E.makeMachine("magic", 2, R), {mageSkill: 12});
  const calm = E.magicStrike(mg, [{unit: foot(), gap: 10}], ctxOf(seq(0.5)));   // d20 = 11 ≤ 12
  assert.equal(calm.calm, true);
  assert.equal(calm.patches.length, 0);
  assert.deepEqual(calm.machine, {count: 1, crew: 3});
  const boom = E.magicStrike(mg, [{unit: foot(), gap: 10}, {unit: foot({id: 2}), gap: 60}], ctxOf(seq(0.99, 0)));   // d20 = 20 > 12; 20%
  assert.equal(boom.calm, false);
  assert.deepEqual(boom.patches.map(p => [p.id, p.patch.soldiers]), [[1, 800]], "взрыв — только ближе 40 м");
  assert.deepEqual(E.captureMachine(mg, 7, R), {factionId: 7, mageSkill: 0, ready: 0, deployLeft: 0}, "маг-батарее мага назначает ГМ");
  assert.equal(E.captureMachine(t, 7, R).mageSkill, 0);
});
