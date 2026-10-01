// ═══════════ siege-report.mjs — каталог осадных орудий для ГМа (6б, Ш3–Ш4) ═══════════
// Таблица чисел из rules.js (siege.engines) и что они дают: сколько выстрелов на пролом каменной стены,
// сколько потерь за ход по пехоте — рядом с залпом лучников за столом. Ожидания считаются точно (по формулам),
// залп лучников — броском через тот же resolveBattle, что в трекере (2000 залпов, зерно 1).
// Запуск: node tools/siege-report.mjs → shared/siege/catalog.md
import fs from "node:fs";
import * as E from "../src/engine/index.js";

const R = E.getRules("base"), S = R.siege;
const REF = 100;                                  // опорная дистанция, м
const tpl = id => { const t = E.getTemplate(id); return {id: 1, name: t.name, type: t.type, weapon: t.weapon, soldiers: 1000, initial: 1000,
  discipline: t.discipline, morale: t.morale, eqAtk: t.eqAtk, eqDef: t.eqDef, exp: t.exp, mastery: t.mastery, fatigue: 0,
  status: "active", attacksMade: 0, countersMade: 0, totKilled: 0, totWounded: 0, broken: false, breakGrace: 0, breakPenalty: 0, factionId: 1}; };
const foot = tpl("infantry");
const n1 = v => String(Math.round(v * 10) / 10).replace(".", ",");
const n2 = v => String(Math.round(v * 100) / 100).replace(".", ",");
const pct = v => n1(v) + "%";
const archersWord = n => n % 10 === 1 && n % 100 !== 11 ? "лучник" : [2, 3, 4].includes(n % 10) && ![12, 13, 14].includes(n % 100) ? "лучника" : "лучников";
const turnsToBreach = {};

// залп 1000 лучников по 1000 пехоты за столом — средние потери
function archersVolley(){
  const rng = E.mulberry32(1);
  const ctx = {rules: R, rng, commanderOf: () => null, factionName: () => ""};
  let sum = 0;
  const N = 2000;
  for(let i = 0; i < N; i++){
    const a = tpl("archers"), b = Object.assign(tpl("infantry"), {id: 2, factionId: 2});
    const r = E.resolveBattle(a, b, {mode: "ranged_form", mutual: false}, ctx);
    const p = r.patches.find(x => x.id === 2);
    sum += 1000 - p.patch.soldiers;
  }
  return sum / N;
}
const bow = archersVolley();

const rows = [], out = [];
for(const [key, e] of Object.entries(S.engines)){
  const m = E.makeMachine(key, 1, R);
  const range = e.tower ? "—" : e.ram ? "вплотную" : `${e.range[0]}–${e.range[1]} м`;
  const hit = (arr) => arr ? `${arr[0]} → ${arr[1]}` : "—";
  rows.push(`| ${e.name} | ${e.crew} (${e.minCrew}) | ${e.reload ? e.reload + " х." : "—"} | ${e.move ? e.move + " м" : "строится на месте"} | ${e.deploy ? "+" + e.deploy + " х." : "—"} | ${range} | ${hit(e.hitWall)} | ${hit(e.hitTroops)} | ${e.indirect ? "да" : "—"} | ${e.wall || "—"} | ${e.die ? "d" + e.die : e.magic ? "70–100% цели" : "—"} | ${e.hp} | ${e.burst ? e.burst + "%" : "—"} | ${e.shock ? "−" + e.shock : "—"} |`);
  if(e.tower) { out.push(`| ${e.name} | — | — | — | — | — | — | не стреляет: ведёт на стену ${e.capacity} человек за ход (бой на стене — следующим шагом) |`); continue; }
  // стена: каменная (100) на опорной дистанции; таран — по окованным воротам
  let wallShot, breachShots, breachTurns, troopsShot = null, troopsTurn = null, burst10 = null;
  if(e.ram){
    wallShot = e.wall;
    breachShots = S.hp.gateIron / e.wall;
  } else {
    const pw = E.hitChance(m, "wall", REF, R) / 100;
    wallShot = pw * e.wall;
    breachShots = S.hp.wall / wallShot;
  }
  breachTurns = breachShots * e.reload;
  turnsToBreach[key] = breachTurns;
  if(e.magic){
    const pt = E.hitChance(m, "troops", REF, R) / 100;
    const armorK = 1 - Math.min(S.magic.armorCap, foot.eqDef / S.magic.armorDiv);
    troopsShot = pt * 1000 * (S.magic.killPct[0] + S.magic.killPct[1]) / 2 / 100 * armorK;
    troopsTurn = troopsShot / e.reload;
  } else if(e.die){
    const pt = E.hitChance(m, "troops", REF, R) / 100;
    troopsShot = pt * (e.die + 1) / 2 / Math.max(R.defense.minDivisor, foot.eqDef / R.defense.rangedEqDiv);
    troopsTurn = troopsShot / e.reload;
  }
  if(e.burst) burst10 = 1 - Math.pow(1 - e.burst / 100, 10);
  out.push(`| ${e.name} | ${e.ram ? "без броска" : pct(E.hitChance(m, "wall", REF, R))} | ${n1(wallShot)} | ${n1(breachShots)} | ${n1(breachTurns)} | ${troopsShot === null ? "—" : n2(troopsShot)} | ${troopsTurn === null ? "—" : n2(troopsTurn)} | ${troopsTurn === null ? "—" : e.magic ? "весь отряд" : "≈ " + Math.round(troopsTurn / bow * 1000) + " " + archersWord(Math.round(troopsTurn / bow * 1000))}${burst10 === null ? "" : ` · разрыв за 10 выстрелов: ${pct(burst10 * 100)}`} |`);
}

const md = `# Каталог осадных орудий — черновик для ГМа (6б, Ш3–Ш4)

Сгенерировано \`node tools/siege-report.mjs\` из \`tracker/src/engine/rules.js\` (\`siege.engines\`). Все числа — **черновик**: их одобряет или правит ГМ, после этого правится только \`rules.js\` (и \`Rules.cs\` в игре — правило 7), а таблица пересобирается.

## Как устроен выстрел

- Фишка — батарея из нескольких одинаковых орудий с расчётом. Стреляют орудия, на которые хватает людей (на каждое — не меньше минимального расчёта).
- Каждое орудие бросает d100 на попадание. Шанс — по дистанции: на ближней границе дальности — первое число, на дальней — второе, между ними — по прямой; плюс выучка расчёта (опыт − 50) × 0,2, то есть ±10%. Шанс не меньше 5% и не больше 95%.
- **По стене:** попадание снимает с участка фиксированную прочность (Г47). Каменная стена — 100, частокол — 30, ворота — 40 / окованные 80, башня — 150. Прочность на нуле — пролом 10 м (v30.8).
- **По людям — как стрельба за столом:** за каждое попадание d(урон), сумма ÷ (снаряжение защиты / 10), потом укрытие (только прямой наводкой), потери, летальность и потеря БД — те же, что у стола.
- **Порох:** перед выстрелом каждое орудие бросает d100 на разрыв ствола — разорвало, орудие и его расчёт потеряны. Попадание по отряду — ещё шок БД сверх потерь (за залп, не за ствол).
- **Навесом** (катапульта, требушет, мортира) бьют через стены, видеть цель не нужно, укрытие цели не спасает. Остальные — прямой наводкой: нужна видимость.
- **Меньше людей — дольше перезарядка** (Г49): во столько раз, во сколько расчёт меньше полного.
- **Марш:** в ход перемещения орудие не стреляет; бомбарде, мортире и маг-пушке нужен ещё ход на развёртывание.
- **Маг-пушка** (SPEC 6б): один бросок на весь залп, шанс + (навык мага − 10) × 3. Попадание уносит 70–100% численности цели × долю живых орудий батареи, броня снижает не больше чем на 15%. Брызги — 5–15% всем отрядам ближе 30 м от края цели, своим тоже. Попадание по самой пушке средством, способным её уничтожить: d20 ≤ навык мага — пушка гаснет, иначе взрыв: 20–50% численности всем ближе 40 м. Без мага батарея молчит; захваченной мага назначает ГМ.
- **Таран** бьёт каждый ход без броска: ворота и частокол — полным уроном, камень — четвертью.

## Числа

| Орудие | Расчёт (мин.) | Перезарядка | Ход | Развёртывание | Дальность | Попадание по стене, % | По людям, % | Навесом | Урон стене | Урон людям | Прочность орудия | Разрыв | Шок БД |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
${rows.join("\n")}

## Что это даёт — одно орудие, полный расчёт, выучка 50, дистанция ${REF} м

Стена — каменная (100); таран — по окованным воротам (80). Люди — шаблон «Пехота» (снаряжение защиты ${foot.eqDef}). Для сравнения: **1000 лучников** (шаблон «Лучники») за столом по 1000 такой же пехоты в строю — в среднем **${Math.round(bow)}** потерь за залп (2000 залпов через resolveBattle трекера).

| Орудие | Попадание по стене | Урон стене за выстрел | Выстрелов на пролом | Ходов на пролом | Потерь за выстрел | Потерь за ход | По людям за ход ≈ |
|---|---|---|---|---|---|---|---|
${out.join("\n")}

Батарея из N орудий ломает стену в N раз быстрее и бьёт людей в N раз сильнее (кроме маг-пушки — у неё доля живых орудий от полной батареи).

## Вопросы ГМу

1. Пушки и бомбарды по людям слабы: одно орудие за ход — как несколько лучников. Так задумано («ранние, неэффективные»), или усилить урон по людям?
2. Разрыв ствола 2–3% за выстрел: батарея из 10 бомбард за 10 залпов в среднем теряет ${n1(10 * 10 * S.engines.bombard.burst / 100)} орудия. Не много ли?
3. Требушет ломает каменную стену за ~${n1(turnsToBreach.trebuchet)} ходов в одиночку, батарея из 4 — за ~${n1(turnsToBreach.trebuchet / 4)} хода. Подходит ли темп осады?
4. Маг-пушка из 3 орудий почти каждым залпом выносит отряд — как в SPEC («решает бой»). Противовес — захват, ОВП и вражеская маг-артиллерия. Достаточно?
`;
const dir = new URL("../../shared/siege/", import.meta.url);
fs.mkdirSync(dir, {recursive: true});
fs.writeFileSync(new URL("catalog.md", dir), md);
console.log("записано: shared/siege/catalog.md · залп 1000 лучников ≈ " + Math.round(bow));
