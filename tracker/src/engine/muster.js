// ═══════════ muster.js — сбор армий из текста ═══════════
// Формат Алекса (как в отчётах о битвах, см. tests/fixtures):
//   Штурм Тринидара :                       ← заголовок (до «Силы сторон»)
//   Силы сторон:                            ← отсюда начинаются войска
//   Красные Кольчуги,                       ← строка с запятой в конце — фракция
//   "Бог войны" Король Генрих Файрлайн      ← строка без числа — полководец
//   18.000 всадников Красных Кольчуг        ← число + название — войска
//   +                                       ← союзный контингент той же стороны (станет подфракцией)
//   =                                       ← дальше итог стороны: «28.000 солдат …» — сверяется с суммой
//   VS                                      ← следующая сторона
//   Ход боя:                                ← здесь разбор останавливается
// Три шага: parseArmyText (текст → стороны) → planMuster (подбор шаблонов, можно править)
// → expandMuster (нарезка на отряды). Всё без браузера — проверяется tests/muster.test.mjs.
import { guessUnitType } from "./units.js";
import { matchTemplate, resolveTemplate } from "./templates.js";

// Сколько отрядов даёт строка и что делать с остатком
export const MUSTER = {
  mergeRemainderBelow: 0.25,   // остаток меньше четверти отряда вливается в последний отряд
  maxUnitsPerLine: 100,        // защита от «200000 по 1»: размер отряда увеличится
};

// ── числа: 18.000 · 18 000 · 19500 · 2.5к · 3 тыс. · ~2000 ──
const NUM_RE = /^(~|≈|около\s+|примерно\s+)?(\d{1,3}(?:[.,\u00a0\u202f ]\d{3})+(?!\d)|\d+(?:[.,]\d+)?)(\s*(?:тысяч[аи]?|тыс\.?|к|k)(?![а-яёa-z]))?/i;
export function parseCount(str){
  const m = NUM_RE.exec(String(str || "").trim());
  if(!m) return null;
  const digits = m[2];
  let n;
  if(m[3]) n = parseFloat(digits.replace(/[\u00a0\u202f ]/g, "").replace(",", ".")) * 1000;
  else if(/^\d+[.,]\d{1,2}$/.test(digits)) n = parseFloat(digits.replace(",", "."));
  else n = parseInt(digits.replace(/[.,\u00a0\u202f ]/g, ""), 10);
  return {n: Math.round(n), approx: !!m[1], rest: String(str).trim().slice(m[0].length)};
}

// ── «всадников Красных Кольчуг» → «Всадники Красных Кольчуг» ──
// После числа от пяти (и после «тысяч») название стоит в родительном падеже множественного числа.
// Меняем только голову: ведущие прилагательные и первое существительное; хвост («Красных Кольчуг»,
// «ополчения Дуртанга») остаётся как есть. Если голова уже в именительном — не трогаем ничего.
const NOUN_EXCEPTIONS = {
  "солдат": "солдаты", "драгун": "драгуны", "гусар": "гусары", "улан": "уланы", "партизан": "партизаны",
  "кирасир": "кирасиры", "гренадер": "гренадеры", "людей": "люди", "братьев": "братья",
  // собирательные в родительном единственного: «3000 ополчения Пикшарпа»
  "ополчения": "ополчение", "пехоты": "пехота", "конницы": "конница", "кавалерии": "кавалерия",
  "стражи": "стража", "гвардии": "гвардия", "дружины": "дружина",
  "пушек": "пушки", "орудий": "орудия",
};
const NOUN_RULES = [
  [/([кгхжшщч])ов$/, "$1и"],   // всадников → всадники, магов → маги
  [/ц[ео]в$/, "цы"],            // пехотинцев → пехотинцы, бойцов → бойцы
  [/([аеиоуяю])ев$/, "$1и"],    // самураев → самураи
  [/ов$/, "ы"],                 // воинов → воины, пикинёров → пикинёры
  [/ей$/, "и"],                 // рыцарей → рыцари
  [/иц$/, "ицы"],               // лучниц → лучницы
  [/жан$/, "жане"],             // горожан → горожане
  [/ян$/, "яне"],               // крестьян → крестьяне
];
const matchCase = (src, out) => src[0] && src[0] === src[0].toUpperCase() && src[0] !== src[0].toLowerCase()
  ? out[0].toUpperCase() + out.slice(1) : out;
function nounToNom(word){
  const low = word.toLowerCase();
  if(NOUN_EXCEPTIONS[low]) return matchCase(word, NOUN_EXCEPTIONS[low]);
  // «лучников-ополченцев»: каждую часть отдельно
  if(low.includes("-")){
    const parts = word.split("-").map(nounToNom);
    return parts.every(Boolean) ? parts.join("-") : null;
  }
  for(const [re, rep] of NOUN_RULES) if(re.test(low)) return matchCase(word, low.replace(re, rep));
  return null;
}
const isGenPlAdj = w => /^[а-яё]+(ых|их)$/i.test(w);
const adjToNom = w => w.replace(/ых$/i, "ые").replace(/их$/i, "ие");
const capitalize = s => {
  const i = s.search(/[a-zа-яё]/i);
  return i < 0 ? s : s.slice(0, i) + s[i].toUpperCase() + s.slice(i + 1);
};
export function toNominative(phrase){
  const words = String(phrase || "").trim().split(/\s+/).filter(Boolean);
  if(!words.length) return "";
  let i = 0;
  while(i < words.length && isGenPlAdj(words[i])) i++;
  if(i < words.length){
    const noun = nounToNom(words[i]);
    if(noun){
      for(let k = 0; k < i; k++) words[k] = adjToNom(words[k]);
      words[i] = noun;
    }
  } else {
    // одни прилагательные: «20.000 пеших», «4000 конных»
    for(let k = 0; k < words.length; k++) words[k] = adjToNom(words[k]);
  }
  return capitalize(words.join(" "));
}

// ── разбор строк ──
// Заголовок раздела — это «Метка …:» (с двоеточием) или голая метка из короткого списка.
// Без двоеточия по началу строки не ловим: иначе полководец «Раненый лорд Эдмунд» оборвёт разбор.
// \b здесь не годится: для регулярных выражений JS кириллица — не «буквы слова».
const START = [/^силы сторон(?![а-яё])/i, /^силы сторон$/i];
const STOP = [/^(ход боя|итог|убит|потер|сбежал|бежал|взят|пленен|пленён|последстви|ранен|трофе|результат)/i,
              /^(потери( сторон)?|итог( боя)?|ход боя|последствия( сражения)?|убитые( и (тяжело)?раненые)?|сбежало( с поля боя)?)$/i];
const SKIP = [/^(аннотация|примечание|дата|место)(?![а-яё])/i, null];
const TOTAL_RE = /^(итого|всего)(?![а-яё])\s*:?\s*/i;
const VS_RE = /^[-—–\s]*(vs\.?|против|versus)[-—–\s]*$/i;
const MAX_NAME = 100;   // строка длиннее — скорее рассказ, чем имя полководца
// Предложение: точка/!/? в конце и больше пяти слов. «Сир Родрик.» — ещё имя.
const isProse = l => /[.!?…]$/.test(l) && l.split(" ").length > 5;

const clean = l => l.replace(/[\u00a0\u202f\t]/g, " ").replace(/\s+/g, " ").trim()
  .replace(/^[-—–•*·]\s+(?=\S)/, "");
const isHeader = (l, [prefix, exact]) => {
  const m = /^([^:]+?)\s*:/.exec(l);
  if(m) return prefix.test(m[1]);
  return !!exact && exact.test(l);
};

// Число в начале («18.000 всадников», «6000 - Рыцари Дуртанга») или в конце
// («Рыцари Кольчуг - 2000», «Сэйрайские копейшики 10к», «Боевые слоны 10(просто десять)»)
const TAIL_NUM = "[~≈]?(?:\\d{1,3}(?: \\d{3})+|\\d[\\d.,]*)(?:\\s?(?:к|k|тыс\\.?|тысяч[аи]?))?";
const TAIL_DASH_RE = new RegExp("^(.+?)\\s+[-—–]\\s+(" + TAIL_NUM + ")$", "i");
const TAIL_BARE_RE = new RegExp("^(.+?)\\s+(" + TAIL_NUM + ")\\s*(?:\\(([^)]*)\\))?$", "i");
function parseUnitLine(l){
  if(/^\?+$/.test(l)) return {count: null, approx: false, name: "", unknown: true};
  const head = parseCount(l);
  if(head){
    let name = head.rest.replace(/^\s*[-—–:]\s*/, "").trim();
    let note = "";
    const par = /\s*\(([^)]*)\)\s*$/.exec(name);
    if(par){ note = par[1]; name = name.slice(0, par.index).trim(); }
    return {count: head.n, approx: head.approx, name, note};
  }
  for(const re of [TAIL_DASH_RE, TAIL_BARE_RE]){
    const m = re.exec(l);
    if(!m) continue;
    const c = parseCount(m[2]);
    if(c && c.n > 0 && !c.rest.trim()) return {count: c.n, approx: c.approx, name: m[1].trim(), note: m[3] || ""};
  }
  return null;
}

// ── полководцы в одной строке ──
// «Лорд Андреас Дарлтон, принц Токимори Тосава» — двое; «Герцог Пикшарпа, Уильям Нерин» — один (К1).
// Кусок без личного имени — титул с владением, прозвище в кавычках — приклеивается к следующему.
const TITLE_RE = /^(лорд|леди|сир|сэр|дон|донна|герцог|герцогиня|граф|графиня|виконт|виконтесса|барон|баронесса|маркиз|князь|княгиня|принц|принцесса|король|королева|император|императрица|царь|шах|паша|султан|хан|эмир|магистр|кастелян|капитан|командир|генерал|маршал|воевода|епископ|кардинал|даймё|ярл|тан|рыцарь|мастер)$/i;
const ADDRESS_RE = /^(сир|сэр|леди|дон|донна|мастер)$/i;   // после обращения идёт имя, а не владение
function splitOutsideQuotes(s){
  const out = []; let cur = "", q = null;
  for(const ch of s){
    if(q){ if(ch === q) q = null; }
    else if(ch === '"') q = '"';
    else if(ch === "«") q = "»";
    else if(ch === ","){ out.push(cur); cur = ""; continue; }
    cur += ch;
  }
  out.push(cur);
  return out.map(x => x.trim()).filter(Boolean);
}
function personInfo(part){
  const words = part.replace(/"[^"]*"|«[^»]*»/g, " ").split(/\s+/).filter(Boolean);
  let k = 0, address = false;
  while(k < words.length && TITLE_RE.test(words[k])){ if(ADDRESS_RE.test(words[k])) address = true; k++; }
  const names = words.slice(k).filter(w => /^[A-ZА-ЯЁ]/.test(w)).length;
  return {names, address, titled: k > 0};
}
export function splitCommanders(line){
  const parts = splitOutsideQuotes(line);
  if(parts.length < 2) return [line.trim()];
  const people = [];
  let pending = "";
  parts.forEach((p, i) => {
    const full = pending ? pending + ", " + p : p;
    const info = personInfo(p);
    const isPerson = info.names >= 2 || (info.names === 1 && (info.address || !info.titled));
    if(isPerson){ people.push(full); pending = ""; }
    else if(i === parts.length - 1 && people.length) people[people.length - 1] += ", " + full;   // хвост без имени — пояснение
    else pending = full;
  });
  if(pending){ if(people.length) people[people.length - 1] += ", " + pending; else people.push(pending); }
  return people;
}
// «во главе с Шахом Джаффар Ибн Аббасом» → «Шах Джаффар Ибн Аббас»: творительный падеж → именительный
export function instrumentalToNom(s){
  return s.trim().split(/\s+/).map(w => {
    if(w.length < 4) return w;
    if(/[еа]ем$/i.test(w)) return w.replace(/([еа])ем$/i, "$1й");   // Андреем → Андрей
    if(/[её]м$/i.test(w) && !/[аоуыэияю]$/i.test(w.slice(0, -2))) return w.slice(0, -2) + "ь";   // Персивалем → Персиваль
    if(/ом$/i.test(w)) return w.slice(0, -2);                          // Аббасом → Аббас
    return w;
  }).join(" ");
}
const LEAD_RE = /^(.+?),?\s+во главе со?\s+(.+)$/i;

// Итог, записанный в тысячах («= 4к»), сверяется с точностью до тысячи
const closeEnough = (n, sum, text) =>
  n === sum || (/(к|k|тыс)/i.test(text || "") && Math.round(sum / 1000) === Math.round(n / 1000));

const newContingent = () => ({faction: null, commanders: [], lines: [], total: null});
const newSide = () => ({contingents: [newContingent()], total: null, events: [], seq: 0});
const linesSum = lines => lines.reduce((a, x) => a + (x.count || 0), 0);

export function parseArmyText(text){
  const raw = String(text || "").split(/\r?\n/);
  const hasStart = raw.some(l => isHeader(clean(l), START));
  const warnings = [];
  const sides = [newSide()];
  let title = null, inForces = !hasStart, awaitingTotal = false, currentCmdr = null, cmdrGroup = false;
  const side = () => sides[sides.length - 1];
  const cont = () => { const s = side(); return s.contingents[s.contingents.length - 1]; };
  const warn = (lineNo, text) => warnings.push({line: lineNo, text});
  const pushContingent = () => { side().contingents.push(newContingent()); currentCmdr = null; cmdrGroup = false; };
  // Каждое «=» запоминаем: последнее станет итогом стороны, остальные — итогами контингентов
  const totalEvent = (n, lineNo, text) => {
    const s = side(), c = cont();
    s.events.push({n, line: lineNo, text, cont: c, contSum: linesSum(c.lines),
                   sideSum: s.contingents.reduce((a, x) => a + linesSum(x.lines), 0), seq: s.seq});
  };
  const addCommanders = line => {
    const people = splitCommanders(line);
    people.forEach(p => { if(!cont().commanders.includes(p)) cont().commanders.push(p); });
    currentCmdr = people[0];
    cmdrGroup = people.length > 1;   // войска достались первому — ГМ проверит выбор
  };

  for(let idx = 0; idx < raw.length; idx++){
    const lineNo = idx + 1;
    let l = clean(raw[idx]);
    if(!l || l.startsWith("```")) continue;
    if(!inForces){
      if(isHeader(l, START)){ inForces = true; continue; }
      if(!title && !isHeader(l, SKIP)) title = l.replace(/\s*:\s*$/, "");
      continue;
    }
    if(isHeader(l, STOP)) break;
    if(isHeader(l, SKIP)) continue;
    if(/^[-—–_=*·.\s]+$/.test(l) && !/^=\s*$/.test(l)) continue;   // разделители «------»

    if(VS_RE.test(l)){ sides.push(newSide()); currentCmdr = null; cmdrGroup = false; awaitingTotal = false; continue; }

    // итог: «=» на своей строке (число — следующей) или «= 32000 солдат …», «Итого: 28000»
    const eq = /^=\s*(.*)$/.exec(l) || (TOTAL_RE.test(l) ? [l, l.replace(TOTAL_RE, "")] : null);
    if(eq){
      const c = parseCount(eq[1]);
      if(c){ totalEvent(c.n, lineNo, l); awaitingTotal = false; }
      else awaitingTotal = true;
      continue;
    }
    if(awaitingTotal){
      awaitingTotal = false;
      const c = parseCount(l);
      if(c){ totalEvent(c.n, lineNo, l); continue; }
    }

    // «+» — союзный контингент; «+ 4000 солдат Вульфхартов» — контингент из одной строки
    const plus = /^\+\s*(.*)$/.exec(l);
    if(plus){
      pushContingent();
      l = plus[1].trim();
      if(!l) continue;
    }

    const u = parseUnitLine(l);
    if(u){
      const c = cont();
      if(u.unknown){ warn(lineNo, `численность не указана («${l}») — строка пропущена`); continue; }
      const name = u.name ? toNominative(u.name) : "";
      c.lines.push({line: lineNo, src: l, count: u.count, approx: u.approx, name,
                    note: u.note || "", commander: currentCmdr, cmdrCheck: cmdrGroup, seq: side().seq++});
      if(u.approx) warn(lineNo, `численность примерная: «${l}»`);
      continue;
    }

    // «Армия Сэйрая, во главе с Каэль Квислинг» — фракция и полководец одной строкой
    const lead = LEAD_RE.exec(l);
    if(lead){
      if(cont().faction || cont().lines.length) pushContingent();
      cont().faction = lead[1].trim();
      addCommanders(instrumentalToNom(lead[2]));
      continue;
    }

    // Фракция: строка с запятой в конце. Если в контингенте уже есть войска — это новый контингент.
    if(/,$/.test(l) || (!hasStart && /:$/.test(l))){
      const name = l.replace(/[,:]\s*$/, "").trim();
      if(cont().faction || cont().lines.length) pushContingent();
      cont().faction = name;
      continue;
    }

    if(l.length > MAX_NAME || isProse(l)){ warn(lineNo, `строка похожа на рассказ, а не на имя — пропущена: «${l.slice(0, 60)}${l.length > 60 ? "…" : ""}»`); continue; }
    // Полководец: войска ниже этой строки — под его началом
    addCommanders(l);
  }

  // Пустые контингенты (например, «+» в конце) не нужны
  for(const s of sides) s.contingents = s.contingents.filter(c => c.lines.length || c.commanders.length || c.faction);
  const used = sides.filter(s => s.contingents.length);
  used.forEach((s, i) => {
    settleTotals(s, i, warn);
    s.sum = s.contingents.reduce((a, c) => a + linesSum(c.lines), 0);
    if(!s.contingents.some(c => c.lines.length)) warn(s.total ? s.total.line : 0, `сторона ${i + 1}: войска не перечислены`);
    if(s.total && !closeEnough(s.total.n, s.sum, s.total.text))
      warn(s.total.line, `сторона ${i + 1}: по строкам ${fmt(s.sum)}, а в итоге написано ${fmt(s.total.n)}`);
    delete s.events; delete s.seq;
    s.contingents.forEach(c => c.lines.forEach(x => { delete x.seq; }));
  });
  return {title, sides: used, warnings};
}

// Последнее «=» стороны — её итог; остальные — итоги контингентов (Кордуа: «= 23к», «= 16к», «= 4к», «= 43к»).
// Строки после итога стороны — обычно повтор («46 пушек в артелерии» ещё раз): дубликаты выбрасываем.
function settleTotals(s, i, warn){
  const ev = s.events;
  if(!ev.length) return;
  let last = ev[ev.length - 1];
  const multi = s.contingents.length > 1;
  // «=» после последнего контингента, совпавшее с ним, а не со всей стороной, — итог контингента
  if(multi && closeEnough(last.n, last.contSum, last.text) && !closeEnough(last.n, last.sideSum, last.text)) last = null;
  ev.forEach(e => {
    if(e === last) return;
    e.cont.total = e.n;
    if(!closeEnough(e.n, e.contSum, e.text)){
      const name = e.cont.faction || e.cont.commanders[0] || `контингент ${s.contingents.indexOf(e.cont) + 1}`;
      warn(e.line, `сторона ${i + 1}, «${name}»: по строкам ${fmt(e.contSum)}, а в итоге написано ${fmt(e.n)}`);
    }
  });
  if(!last) return;
  s.total = {n: last.n, line: last.line, text: last.text};
  const before = s.contingents.flatMap(c => c.lines).filter(x => x.seq < last.seq);
  const key = x => x.name.toLowerCase() + "|" + x.count;
  const seen = new Set(before.map(key));
  s.contingents.forEach(c => {
    c.lines = c.lines.filter(x => {
      if(x.seq < last.seq) return true;
      if(seen.has(key(x))){ warn(x.line, `«${x.src}» после итога стороны — повтор, пропущено`); return false; }
      warn(x.line, `«${x.src}» стоит после итога стороны — проверь, не лишняя ли`);
      return true;
    });
  });
}
const fmt = n => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, " ");

// ── план: у каждой строки — шаблон, тип, размер отряда; всё это можно поправить в предпросмотре ──
const sideName = (s, i) => {
  const f = s.contingents.find(c => c.faction);
  if(f) return f.faction;
  const c = s.contingents.find(c => c.commanders.length);
  return c ? c.commanders[0] : `Сторона ${i + 1}`;
};
const contingentName = (c, j) => c.faction || c.commanders[0] || (c.lines[0] && c.lines[0].name) || `Контингент ${j + 1}`;

// Слоны и прочее особое: своей механики нет — заводятся обычным отрядом с пометкой.
// Орудия (6б, Ш4) — не отряд, а батарея: число в строке — число орудий (siege.engines в rules.js).
export const SPECIAL_RE = /пушк|орудий|орудия|катапульт|требушет|баллист|бомбард|мортир|слон/i;
const MACHINE_WORDS = [
  [/маг\S*[\s-]*(пушк|артил|орудия|орудий)/i, "magic"], [/бомбард/i, "bombard"], [/мортир/i, "mortar"],
  [/рибодекин|органн\S* пушк/i, "ribauldequin"], [/требушет/i, "trebuchet"], [/катапульт|онагр/i, "catapult"],
  [/баллист|скорпион/i, "ballista"], [/таран/i, "ram"], [/осадн\S* башн/i, "tower"], [/пушк|орудий|орудия|артил/i, "cannon"],
];
export function guessMachine(name){
  for(const [re, key] of MACHINE_WORDS) if(re.test(name || "")) return key;
  return null;
}
export function planLine(line, factionName, overrides){
  const g = guessUnitType(line.name);
  const t = matchTemplate(line.name, g);
  const tpl = resolveTemplate(t.id, factionName, overrides);
  const machine = guessMachine(line.name);
  return Object.assign({}, line, {
    templateId: t.id, fallback: t.fallback, machine, special: !machine && SPECIAL_RE.test(line.name),
    type: g ? g.type : tpl.type, weapon: g ? g.weapon : tpl.weapon,
    size: null,   // null — размер из шаблона (с правками фракции)
  });
}

export function planMuster(parsed, opts = {}){
  const overrides = opts.overrides;
  const sides = parsed.sides.map((s, i) => {
    const faction = sideName(s, i);
    return {
      faction, total: s.total ? s.total.n : null, sum: s.sum,
      contingents: s.contingents.map((c, j) => ({
        name: contingentName(c, j),
        commanders: c.commanders.slice(),
        lines: c.lines.map(l => planLine(Object.assign({}, l, {name: l.name || c.faction || "Войско"}), faction, overrides)),
      })),
    };
  });
  return {title: parsed.title, warnings: parsed.warnings.slice(), sides};
}

// ── нарезка на отряды ──
export function splitSoldiers(total, size, mergeBelow = MUSTER.mergeRemainderBelow){
  total = Math.round(total);
  if(!(total > 0)) return [];
  if(!(size > 0) || total <= size) return [total];
  const n = Math.floor(total / size), rem = total - n * size;
  const out = new Array(n).fill(size);
  if(rem > 0){
    if(rem < size * mergeBelow) out[n - 1] += rem;
    else out.push(rem);
  }
  return out;
}
// Размер отряда для строки: правка в предпросмотре или шаблон; не больше maxUnitsPerLine отрядов
export function lineSize(line, tpl){
  let size = +line.size > 0 ? Math.round(+line.size) : tpl.size;
  const count = Math.round(line.count || 0);
  if(size > 0 && count / size > MUSTER.maxUnitsPerLine) size = Math.ceil(count / MUSTER.maxUnitsPerLine);
  return size;
}
// Названия с продолжением нумерации, как у клонирования: «Имя», «Имя №2»…
export function numberedNames(base, count, taken){
  const esc = base.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const re = new RegExp("^" + esc + " №(\\d+)$");
  let maxN = 0;
  for(const t of taken){
    if(t === base) maxN = Math.max(maxN, 1);
    const m = re.exec(t);
    if(m) maxN = Math.max(maxN, +m[1]);
  }
  const out = [];
  for(let k = 0; k < count; k++){
    const n = maxN + 1 + k;
    const name = n === 1 ? base : `${base} №${n}`;
    out.push(name);
    taken.add(name);
  }
  return out;
}

// План → фракции, подфракции, полководцы и отряды (без id — их раздаёт интерфейс).
// existingNames: {"фракция в нижнем регистре": [названия её отрядов]} — нумерация своя в каждой фракции.
export function expandMuster(plan, opts = {}){
  const overrides = opts.overrides;
  const existing = opts.existingNames || {};
  const warnings = [];
  const perLine = {};
  const factions = plan.sides.map((s, i) => {
    const name = String(s.faction || "").trim() || `Сторона ${i + 1}`;
    const taken = new Set(existing[name.toLowerCase()] || []);
    const multi = s.contingents.length > 1;
    const F = {name, subfactions: [], commanders: [], units: [], machines: []};
    s.contingents.forEach((c, j) => {
      const sub = multi ? (String(c.name || "").trim() || `Контингент ${j + 1}`) : null;
      if(sub && !F.subfactions.includes(sub)) F.subfactions.push(sub);
      c.commanders.forEach(cm => { if(!F.commanders.includes(cm)) F.commanders.push(cm); });
      c.lines.forEach((l, k) => {
        const key = `${i}.${j}.${k}`;
        const count = Math.round(+l.count || 0);
        const unitName = String(l.name || "").trim();
        if(!(count > 0) || !unitName){
          perLine[key] = {units: 0, sizes: [], size: 0};
          warnings.push(`«${l.src || unitName}»: ${count > 0 ? "нет названия" : "нет численности"} — пропущено`);
          return;
        }
        // орудия — одна батарея на строку: число в строке — число орудий (Ш4)
        if(l.machine){
          perLine[key] = {units: 0, sizes: [], size: 0, machine: {engine: l.machine, count}};
          F.machines.push({name: numberedNames(unitName, 1, taken)[0], engine: l.machine, count, subfaction: sub, commander: l.commander || null});
          return;
        }
        const tpl = resolveTemplate(l.templateId, name, overrides);
        const size = lineSize(l, tpl);
        const sizes = splitSoldiers(count, size);
        perLine[key] = {units: sizes.length, sizes, size};
        numberedNames(unitName, sizes.length, taken).forEach((un, n) => F.units.push({
          name: un, subfaction: sub, commander: l.commander || null,
          type: l.type, weapon: l.weapon, templateId: tpl.id,
          soldiers: sizes[n],
          discipline: tpl.discipline, morale: tpl.morale, eqAtk: tpl.eqAtk, eqDef: tpl.eqDef,
          exp: tpl.exp, mastery: tpl.mastery,
        }));
      });
    });
    return F;
  });
  const unitsTotal = factions.reduce((a, F) => a + F.units.length, 0);
  const soldiersTotal = factions.reduce((a, F) => a + F.units.reduce((b, u) => b + u.soldiers, 0), 0);
  const machinesTotal = factions.reduce((a, F) => a + F.machines.length, 0);
  return {factions, perLine, warnings, unitsTotal, soldiersTotal, machinesTotal};
}
