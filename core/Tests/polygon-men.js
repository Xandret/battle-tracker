// ═══════════ polygon-men.js — бойцы полигона: снаряжение, анимация, стрелы, павшие (В5–В9) ═══════════
// Вставляется в polygon.html при сборке (Polygon.cs ставит его вместо <script src="polygon-men.js">). Идеи — по
// роликам Iron Kings, рисунок свой (Г10). Всё здесь — только вид: правил и генератора боя нет, «случайное» — хэш
// от номера бойца, поэтому одна и та же сцена всегда выглядит одинаково.
//
// Боец собран из частей, каждая — маленький холст под текущее приближение (спрайт): ноги, тело с головой и поклажей,
// щит, оружие. Части двигаются отдельно: шаг, покачивание, взгляд по сторонам, удар, выстрел, щит над головой под
// обстрелом. Снаряжение у каждого своё — из набора отряда (В9). Издали (меньше 7 px/м) боец — один готовый спрайт
// без анимации, ещё дальше (меньше 3 px/м) фигурка — плашка.

const MEN_FROM = 3, PARTS_FROM = 7;   // px на метр: ближе MEN_FROM — человечки, ближе PARTS_FROM — с анимацией
const INK = "#2b2621", STEEL = "#a4a8ab", STEEL_D = "#767b80", SHINE = "#e2e4e5", WOOD = "#7a5a36", IRON = "#56585b";
const CLOTH = ["#cfcbc2", "#c5c1b7", "#d8d4cb"];                                         // Г7: серая одежда
const CLOTHS = ["#8b7a5c", "#7d6b4f", "#a08d6a", "#6f6a5e", "#9a8f7a", "#7a5c48", "#5f6650", "#a3977d"];   // своя одежда
const LEATHER = ["#7a5634", "#6a4a2e", "#8a6440"];
const HAIRS = ["#2e241c", "#4b3a2a", "#6b4a2f", "#8a6a45", "#b08a50", "#8c8c86", "#7a3a20"];
const COATS = ["#e4dfd3", "#3b332d", "#7b4b2c", "#985c30", "#8f8a84", "#b9955f", "#5a3a26"];   // масти
const DEVICE = ["#ece6d6", "#d9b44a", "#2e2a26"];                                         // второй цвет герба
const BLOOD = "#7b1212";
const LOOK_BY_TPL = {militia: "militia", infantry: "spear", guard: "sword", foot_knights: "sword", pikemen: "pike",
  militia_archers: "bow", archers: "bow", crossbowmen: "crossbow", knights: "lance", elite_cavalry: "barded"};
const LOOK_BY_TYPE = {infantry: "spear", pike: "pike", archer: "bow", cavalry: "lance"};
const lookOf = u => LOOK_BY_TPL[u.tpl] || LOOK_BY_TYPE[u.type] || "spear";
let GREY = false;   // Г7: серые фигурки (флажок)

function hash(a, b){ let x = Math.imul(a ^ 0x9e3779b9, 0x85ebca6b) ^ Math.imul(b + 0x632be5ab, 0xc2b2ae35); x ^= x >>> 15; x = Math.imul(x, 0x2c1b3c6d); x ^= x >>> 12; return (x >>> 0) / 4294967296; }
function mix(a, b, k){
  const p = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
  const x = p(a), y = p(b);
  return "#" + x.map((v, i) => Math.round(v + (y[i] - v) * k).toString(16).padStart(2, "0")).join("");
}
const shade = (hex, k) => k >= 0 ? mix(hex, "#ffffff", k) : mix(hex, "#000000", -k);
const frac = v => v - Math.floor(v);
const ease = p => p * p * (3 - 2 * p);

// ── Снаряжение (В9): у отряда — набор комплектов, у бойца — один из них по номеру ──
// Вес — как часто встречается. Ополчение пёстрое (своя одежда, что нашлось в руках), гвардия и элитная конница
// единообразны (один шлем и герб на всех), остальные — посередине.
const KIT = {
  militia: {helm: [["hair", 40], ["cap", 25], ["hood", 25], ["kettle", 10]], weapon: [["spear", 55], ["axe", 12], ["fork", 10], ["club", 13], ["sword", 5], ["mace", 5]],
    shield: [["none", 50], ["round", 45], ["oval", 5]], paint: [["wood", 55], ["plain", 30], ["halves", 15]], back: [["none", 50], ["roll", 25], ["bag", 25]],
    armour: [["cloth", 85], ["leather", 15]], own: 0.55},
  spear: {helm: [["kettle", 30], ["nasal", 35], ["capSteel", 20], ["hood", 10], ["hair", 5]], weapon: [["spear", 88], ["axe", 6], ["sword", 6]],
    shield: [["round", 50], ["heater", 35], ["oval", 15]], paint: [["plain", 35], ["halves", 15], ["stripe", 15], ["cross", 10], ["boss", 25]],
    back: [["none", 70], ["roll", 20], ["bag", 10]], armour: [["cloth", 50], ["mail", 30], ["leather", 20]], own: 0.12},
  sword: {helm: [["nasal", 50], ["great", 30], ["kettle", 20]], weapon: [["sword", 70], ["mace", 15], ["axe", 15]], shield: [["heater", 100]],
    paint: [["halves", 25], ["quarters", 25], ["chevron", 25], ["cross", 25]], back: [["none", 65], ["cape", 35]], armour: [["mail", 70], ["plate", 30]],
    own: 0.03, uniform: true},
  pike: {helm: [["kettle", 45], ["morion", 25], ["capSteel", 20], ["hair", 10]], weapon: [["pike", 100]], shield: [["none", 80], ["buckler", 20]],
    paint: [["plain", 100]], back: [["none", 75], ["roll", 25]], armour: [["cloth", 55], ["mail", 25], ["leather", 20]], own: 0.1},
  bow: {helm: [["hood", 40], ["cap", 25], ["kettle", 20], ["hair", 15]], weapon: [["bow", 100]], side: [["none", 45], ["falchion", 35], ["axe", 20]],
    shield: [["none", 100]], back: [["quiver", 100]], armour: [["cloth", 60], ["leather", 40]], own: 0.2},
  crossbow: {helm: [["kettle", 50], ["nasal", 30], ["cap", 20]], weapon: [["crossbow", 100]], side: [["none", 50], ["sword", 30], ["mace", 20]],
    shield: [["none", 100]], back: [["pavise", 100]], armour: [["cloth", 45], ["mail", 35], ["leather", 20]], own: 0.1},
  lance: {helm: [["great", 50], ["bascinet", 30], ["nasal", 20]], weapon: [["lance", 80], ["sword", 12], ["mace", 8]], shield: [["heater", 100]],
    paint: [["halves", 20], ["quarters", 20], ["chevron", 20], ["cross", 15], ["stripe", 15], ["plain", 10]], back: [["none", 100]],
    armour: [["mail", 60], ["plate", 40]], bard: [["cloth", 60], ["none", 40]], own: 0},
  barded: {helm: [["great", 100]], weapon: [["lance", 100]], shield: [["heater", 100]], paint: [["quarters", 50], ["chevron", 50]], back: [["none", 100]],
    armour: [["plate", 100]], bard: [["full", 100]], own: 0, uniform: true},
};
const TABARD = new Set(["plain", "halves", "quarters", "cross", "chevron", "stripe"]);
function pickW(list, r){
  let sum = 0; for(const [, w] of list) sum += w;
  let x = r * sum;
  for(const [v, w] of list){ x -= w; if(x < 0) return v; }
  return list[list.length - 1][0];
}
function kitsOf(u){
  const stamp = u.col + (GREY ? "g" : "");
  if(u.kits && u.kitStamp === stamp) return u.kits;
  const look = lookOf(u), K = KIT[look], seed = u.id * 1013 + 7, N = K.uniform ? 6 : 12, horse = look === "lance" || look === "barded";
  const c2 = DEVICE[Math.floor(hash(seed, 2) * DEVICE.length)];
  const one = {helm: pickW(K.helm, hash(seed, 3)), paint: pickW(K.paint || [["plain", 1]], hash(seed, 4))};   // единый шлем и герб
  u.kits = Array.from({length: N}, (_, i) => {
    const s = seed * 31 + i * 7 + 1, R = j => hash(s, j);
    const own = R(1) < K.own;
    let cloth = own ? CLOTHS[Math.floor(R(2) * CLOTHS.length)] : shade(u.col, (R(3) - 0.5) * 0.2);
    if(GREY) cloth = mix(CLOTH[i % 3], u.col, 0.3);
    const helm = K.uniform ? one.helm : pickW(K.helm, R(4));
    const helmCol = helm === "hair" ? HAIRS[Math.floor(R(5) * HAIRS.length)]
      : helm === "cap" || helm === "hood" ? (R(6) < 0.5 ? CLOTHS[Math.floor(R(7) * CLOTHS.length)] : shade(u.col, -0.25)) : STEEL;
    const shape = pickW(K.shield, R(8));
    const paint = K.uniform ? one.paint : pickW(K.paint || [["plain", 1]], R(9));
    const shield = shape === "none" ? null : {shape, paint: shape === "buckler" ? "steel" : paint,
      c1: own && R(10) < 0.5 ? cloth : u.col, c2: look === "militia" ? DEVICE[Math.floor(R(11) * 3)] : c2};
    if(shield) shield.key = [shield.shape, shield.paint, shield.c1, shield.c2].join(",");
    const back = pickW(K.back, R(12));
    const armour = pickW(K.armour, R(13));
    // сюрко (В15): у кольчуги и лат — всегда, цвета стороны; герб — как на щите, у единообразных — один на всех
    const tabard = armour === "mail" || armour === "plate" ? (TABARD.has(paint) ? paint : shield && TABARD.has(shield.paint) ? shield.paint : "plain") : null;
    const k = {look, horse, cloth, armour, tabard, leather: LEATHER[Math.floor(R(14) * 3)], helm, helmCol,
      crest: helm === "great" && R(15) < 0.35 ? u.col : null, back, backCol: back === "pavise" ? u.col : shade(u.col, -0.3),
      weapon: pickW(K.weapon, R(16)), side: K.side ? pickW(K.side, R(17)) : "none", shield, col: u.col, c2,
      coat: COATS[Math.floor(R(18) * COATS.length)], bard: K.bard ? pickW(K.bard, R(19)) : "none"};
    k.bodyKey = [k.cloth, k.armour, k.tabard, k.leather, k.helm, k.helmCol, k.crest, k.back, k.backCol, c2].join(",");
    k.horseKey = [k.coat, k.bard, u.col, c2].join(",");
    return k;
  });
  u.kitStamp = stamp;
  return u.kits;
}

// ── Рисование частей: метры, вперёд — вверх (−y), начало — середина плеч ──
function ell(g, x, y, rx, ry, fill, rot = 0, stroke = true){
  g.beginPath(); g.ellipse(x, y, rx, ry, rot, 0, Math.PI * 2);
  if(fill){ g.fillStyle = fill; g.fill(); }
  if(stroke){ g.strokeStyle = INK; g.lineWidth = 0.04; g.stroke(); }
}
function stick(g, x0, y0, x1, y1, width, color){
  g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.strokeStyle = INK; g.lineWidth = width + 0.04; g.stroke();
  g.strokeStyle = color; g.lineWidth = width; g.stroke();
}
function tip(g, x, y, len){ g.beginPath(); g.moveTo(x - 0.045, y); g.lineTo(x, y - len); g.lineTo(x + 0.045, y); g.closePath(); g.fillStyle = SHINE; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.025; g.stroke(); }
// ── Облик «настоящих солдатиков» (В15, шаг 2): объём краской, как у расписанной миниатюры ──
// Свет не направленный — боец поворачивается, а направленный свет даёт Unity по картам нормалей: к середине детали
// светлее, к краю темнее, по краю — тонкий контур. Пропорции — как у фигурок: плечи шире, голова меньше.
function vol(g, col, cx, cy, r, k = 1){   // заливка объёмом: светлее в середине, темнее к краю
  const gr = g.createRadialGradient(cx, cy, 0, cx, cy, r);
  gr.addColorStop(0, shade(col, 0.18 * k)); gr.addColorStop(0.55, col); gr.addColorStop(1, shade(col, -0.3 * k));
  return gr;
}
function edge(g, w = 0.035){ g.strokeStyle = INK; g.lineWidth = w; g.stroke(); }
const ARM_X = 0.232, NECK_R = 0.135;
// плечи и спина сверху: грудь чуть площе, лопатки круглее
function torsoPath(g){
  const w = 0.19, f = -0.115, b = 0.15;
  g.beginPath(); g.moveTo(-w + 0.04, f);
  g.quadraticCurveTo(0, f - 0.03, w - 0.04, f); g.quadraticCurveTo(w + 0.03, f + 0.01, w + 0.035, 0.02);
  g.quadraticCurveTo(w + 0.03, b - 0.02, w - 0.06, b); g.quadraticCurveTo(0, b + 0.035, -w + 0.06, b);
  g.quadraticCurveTo(-w - 0.03, b - 0.02, -w - 0.035, 0.02); g.quadraticCurveTo(-w - 0.03, f + 0.01, -w + 0.04, f);
  g.closePath();
}
function rings(g, x0, y0, x1, y1, col){   // кольчуга: ряды колец со сдвигом
  g.strokeStyle = col; g.lineWidth = 0.009;
  for(let y = y0, row = 0; y <= y1; y += 0.03, row++)
    for(let x = x0 + (row % 2) * 0.016; x <= x1; x += 0.032){ g.beginPath(); g.arc(x, y, 0.011, 0, 6.283); g.stroke(); }
}
function paintHead(g, k){
  const c = k.helmCol, steel = (x, y, rx, ry, col = STEEL, rot = 0) => {   // сталь: объём и блик почти посередине
    g.beginPath(); g.ellipse(x, y, rx, ry, rot, 0, 6.283); g.fillStyle = vol(g, col, x - rx * 0.15, y - ry * 0.15, Math.max(rx, ry) * 1.1); g.fill(); edge(g);
  };
  const glint = (x, y, r) => ell(g, x, y, r, r * 1.1, SHINE, 0, false);
  switch(k.helm){
    case "hair":
      g.beginPath(); g.ellipse(0, 0.005, 0.1, 0.106, 0, 0, 6.283); g.fillStyle = vol(g, c, 0, -0.01, 0.12, 0.8); g.fill(); edge(g);
      g.beginPath(); g.moveTo(0, -0.09); g.lineTo(0.008, 0.08); g.strokeStyle = shade(c, -0.35); g.lineWidth = 0.014; g.stroke(); break;
    case "cap":   // шапка с отворотом
      g.beginPath(); g.ellipse(0, 0, 0.108, 0.108, 0, 0, 6.283); g.fillStyle = vol(g, c, 0, 0, 0.12); g.fill(); edge(g);
      g.beginPath(); g.arc(0, 0, 0.078, 0, 6.283); g.strokeStyle = shade(c, -0.3); g.lineWidth = 0.014; g.stroke(); break;
    case "hood":  // капюшон с наплечной пелериной
      g.beginPath(); g.moveTo(0.11, 0); g.arc(0, 0, 0.11, 0, Math.PI, true); g.quadraticCurveTo(-0.075, 0.15, 0, 0.23); g.quadraticCurveTo(0.075, 0.15, 0.11, 0);
      g.fillStyle = vol(g, c, 0, 0, 0.16); g.fill(); edge(g);
      g.beginPath(); g.arc(0, -0.005, 0.07, Math.PI * 1.1, Math.PI * 1.9); g.strokeStyle = shade(c, -0.35); g.lineWidth = 0.014; g.stroke(); break;
    case "kettle":   // шапель: широкие поля, купол — кольцо, как у Iron Kings
      steel(0, 0, 0.15, 0.145); g.beginPath(); g.arc(0, 0, 0.142, 0, 6.283); g.strokeStyle = STEEL_D; g.lineWidth = 0.012; g.stroke();
      steel(0, 0, 0.085, 0.085, shade(STEEL, 0.1)); glint(-0.02, -0.025, 0.022); break;
    case "capSteel":
      steel(0, 0, 0.108, 0.108);
      g.beginPath(); g.arc(0, 0, 0.066, 0, 6.283); g.strokeStyle = STEEL_D; g.lineWidth = 0.018; g.stroke(); glint(-0.025, -0.03, 0.024); break;
    case "nasal":    // шлем с наносником и гребнем-швом
      steel(0, 0.002, 0.106, 0.112);
      g.beginPath(); g.moveTo(0, -0.105); g.lineTo(0, 0.1); g.moveTo(0, -0.11); g.lineTo(0, -0.145); g.strokeStyle = STEEL_D; g.lineWidth = 0.022; g.stroke(); glint(-0.035, -0.025, 0.022); break;
    case "great":    // топфхельм: плоский верх с ободом, спереди — смотровая щель, у некоторых — гребень цвета отряда
      steel(0, 0, 0.12, 0.12, "#8f9498");
      g.beginPath(); g.arc(0, 0, 0.09, 0, 6.283); g.strokeStyle = "#62676c"; g.lineWidth = 0.018; g.stroke();
      g.beginPath(); g.moveTo(-0.07, -0.1); g.quadraticCurveTo(0, -0.125, 0.07, -0.1); g.strokeStyle = INK; g.lineWidth = 0.016; g.stroke();
      if(k.crest){ g.beginPath(); g.ellipse(0, 0, 0.03, 0.1, 0, 0, 6.283); g.fillStyle = vol(g, k.crest, 0, -0.02, 0.1); g.fill(); edge(g, 0.025); }
      else glint(-0.035, -0.035, 0.024); break;
    case "bascinet": // бацинет с «клювом» забрала вперёд
      steel(0, 0.01, 0.106, 0.118); steel(0, -0.125, 0.045, 0.06, STEEL_D);
      g.beginPath(); g.moveTo(-0.025, -0.12); g.lineTo(0.025, -0.12); g.strokeStyle = INK; g.lineWidth = 0.01; g.stroke(); glint(-0.035, -0.015, 0.024); break;
    case "morion":   // морион: поля лодочкой спереди назад, гребень
      steel(0, 0, 0.112, 0.18);
      g.beginPath(); g.moveTo(0, -0.17); g.lineTo(0, 0.17); g.strokeStyle = STEEL_D; g.lineWidth = 0.032; g.stroke(); glint(-0.045, -0.04, 0.02); break;
  }
}
function backItem(g, k){
  switch(k.back){
    case "cape":
      g.beginPath(); g.moveTo(-0.24, 0.04); g.lineTo(0.24, 0.04); g.lineTo(0.2, 0.34); g.quadraticCurveTo(0, 0.38, -0.2, 0.34); g.closePath();
      g.fillStyle = k.backCol; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.035; g.stroke(); break;
    case "roll":
      ell(g, 0, 0.17, 0.21, 0.065, "#b09a72");
      g.beginPath(); g.moveTo(-0.08, 0.11); g.lineTo(-0.08, 0.23); g.moveTo(0.08, 0.11); g.lineTo(0.08, 0.23); g.strokeStyle = "#5a4630"; g.lineWidth = 0.02; g.stroke(); break;
    case "bag":
      g.beginPath(); g.rect(-0.11, 0.07, 0.22, 0.19); g.fillStyle = LEATHER[1]; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.03; g.stroke(); break;
    case "quiver":
      g.save(); g.translate(0.12, 0.17); g.rotate(0.35);
      g.beginPath(); g.rect(-0.055, -0.16, 0.11, 0.3); g.fillStyle = "#6e4c2e"; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.03; g.stroke();
      for(const x of [-0.025, 0.005, 0.03]) ell(g, x, -0.175, 0.016, 0.026, "#efe9dc", 0, false);
      g.restore(); break;
    case "pavise":
      g.beginPath(); g.rect(-0.28, 0.12, 0.56, 0.18); g.fillStyle = k.backCol; g.fill();
      g.fillStyle = k.c2; g.fillRect(-0.05, 0.12, 0.1, 0.18);
      g.strokeStyle = INK; g.lineWidth = 0.035; g.strokeRect(-0.28, 0.12, 0.56, 0.18); break;
  }
}
// Тело сверху: руки от плеча по бокам, плечи и спина — по доспеху. Ткань — стёганка с простёжкой; кожа — бригантина
// (ткань одежды на пластинах, заклёпки), рукава кожаные; кольчуга и латы — под сюрко цвета стороны (paintTabard,
// отдельный слой), видны руки и бармица вокруг шлема. Цвет одежды или стороны — на каждом бойце.
const MAIL = "#9a9ea2", MAIL_D = "#6f7377";
function paintBody(g, k){
  backItem(g, k);
  const arm = sleeveOf(k), metal = k.armour === "mail" || k.armour === "plate";
  for(const s of [-1, 1]){
    g.beginPath(); g.ellipse(s * ARM_X, 0.02, 0.068, 0.118, 0, 0, 6.283);
    g.fillStyle = vol(g, arm, s * ARM_X, 0.0, 0.13, k.armour === "plate" ? 1.3 : 1); g.fill();
    if(k.armour === "mail"){ g.save(); g.clip(); rings(g, s * ARM_X - 0.07, -0.1, s * ARM_X + 0.07, 0.14, MAIL_D); g.restore(); }
    if(k.armour === "plate"){ g.beginPath(); g.ellipse(s * ARM_X, 0.03, 0.06, 0.025, 0, 0, 6.283); g.strokeStyle = STEEL_D; g.lineWidth = 0.012; g.stroke(); }
    g.beginPath(); g.ellipse(s * ARM_X, 0.02, 0.068, 0.118, 0, 0, 6.283); edge(g);
  }
  const base = k.armour === "mail" ? MAIL : k.armour === "plate" ? STEEL : k.cloth;
  torsoPath(g); g.fillStyle = vol(g, base, 0, 0.01, 0.26, k.armour === "plate" ? 1.3 : 1); g.fill();
  g.save(); torsoPath(g); g.clip();
  if(k.armour === "mail") rings(g, -0.24, -0.15, 0.24, 0.2, MAIL_D);
  else if(k.armour === "plate"){   // наплечники и хребет спинной пластины
    for(const s of [-1, 1]){ g.beginPath(); g.ellipse(s * 0.15, 0.0, 0.09, 0.12, 0, 0, 6.283); g.fillStyle = vol(g, STEEL, s * 0.15, -0.02, 0.12, 1.4); g.fill(); edge(g, 0.022); }
    g.beginPath(); g.moveTo(0, -0.1); g.lineTo(0, 0.17); g.strokeStyle = STEEL_D; g.lineWidth = 0.014; g.stroke();
  } else if(k.armour === "leather"){   // бригантина: ткань на пластинах — ряды заклёпок, швы пластин
    g.strokeStyle = shade(base, -0.35); g.lineWidth = 0.01; g.beginPath();
    for(const y of [-0.04, 0.05, 0.13]){ g.moveTo(-0.22, y); g.lineTo(0.22, y); } g.stroke();
    for(const y of [-0.075, 0.005, 0.09]) for(let x = -0.18; x <= 0.18; x += 0.045) ell(g, x, y, 0.011, 0.011, "#c9c3b4", 0, false);
  } else {   // стёганка: простёжка ромбом
    g.strokeStyle = shade(base, -0.32); g.lineWidth = 0.009; g.beginPath();
    for(let d = -0.5; d <= 0.5; d += 0.075){ g.moveTo(d - 0.2, -0.2); g.lineTo(d + 0.2, 0.2); g.moveTo(d + 0.2, -0.2); g.lineTo(d - 0.2, 0.2); }
    g.stroke();
  }
  g.restore();
  torsoPath(g); edge(g, 0.04);
  if(metal){   // бармица — кольцо кольчуги вокруг шеи; видна в вырезе сюрко
    g.beginPath(); g.arc(0, 0, 0.15, 0, 6.283); g.fillStyle = vol(g, MAIL, 0, 0, 0.16); g.fill();
    g.save(); g.clip(); rings(g, -0.16, -0.16, 0.16, 0.16, MAIL_D); g.restore(); g.beginPath(); g.arc(0, 0, 0.15, 0, 6.283); edge(g, 0.025);
  }
  if(k.tabard) paintTabard(g, k.tabard, k.col, k.c2);
  paintHead(g, k);
}
// Сюрко (В15): поверх плеч и спины, вырез у шеи; поле — цвет стороны, второй цвет — герб, как на щите
function tabardPath(g){
  g.beginPath(); g.moveTo(-0.15, -0.105);
  g.quadraticCurveTo(0, -0.13, 0.15, -0.105); g.quadraticCurveTo(0.2, -0.08, 0.205, 0.02); g.quadraticCurveTo(0.2, 0.13, 0.15, 0.155);
  g.quadraticCurveTo(0, 0.18, -0.15, 0.155); g.quadraticCurveTo(-0.2, 0.13, -0.205, 0.02); g.quadraticCurveTo(-0.2, -0.08, -0.15, -0.105);
  g.closePath(); g.moveTo(NECK_R, 0); g.arc(0, 0, NECK_R, 0, 6.283, true);
}
function paintTabard(g, paint, c1, c2){
  tabardPath(g); g.fillStyle = vol(g, c1, 0, 0.02, 0.24); g.fill("evenodd");
  g.save(); tabardPath(g); g.clip("evenodd"); g.fillStyle = c2;
  switch(paint){
    case "halves": g.fillRect(0, -1, 1, 2); break;
    case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
    case "stripe": g.fillRect(-1, 0.04, 2, 0.05); break;
    case "cross": g.fillRect(-0.03, -1, 0.06, 2); g.fillRect(-1, 0.06, 2, 0.05); break;
    case "chevron": g.beginPath(); g.moveTo(-0.25, 0.17); g.lineTo(0, 0.02); g.lineTo(0.25, 0.17); g.lineTo(0.25, 0.25); g.lineTo(0, 0.1); g.lineTo(-0.25, 0.25); g.closePath(); g.fill(); break;
  }
  // складки ткани по спине и тень у края
  g.strokeStyle = "rgba(43,38,33,.28)"; g.lineWidth = 0.012; g.beginPath();
  for(const x of [-0.09, 0.09]){ g.moveTo(x, 0.06); g.quadraticCurveTo(x * 1.2, 0.12, x * 1.1, 0.165); } g.stroke();
  g.restore();
  tabardPath(g); g.strokeStyle = INK; g.lineWidth = 0.03; g.stroke();
}
// ── Руки (В15): предплечье от локтя к кисти и кисть, держащая оружие и щит ──
// Предплечье лежит под телом (у плеча его прячет рука от плеча), кисть — поверх рукояти. Кисть: голая, кожаная перчатка
// под кольчугой, латная рукавица под латами.
const SKIN = "#c49a74", ELB = [0.215, 0.0], FA_LEN = 0.3;
const handKind = k => k.armour === "plate" ? "plate" : k.armour === "mail" ? "glove" : "skin";
const HAND_COL = {skin: SKIN, glove: "#5d4734", plate: STEEL};
// точка оружия (в его осях) — в осях бойца по позе T = [x, y, поворот, масштаб поперёк, вдоль]
function onPose(T, x, y){ const c = Math.cos(T[2]), sn = Math.sin(T[2]), px = x * T[3], py = y * T[4]; return [T[0] + px * c - py * sn, T[1] + px * sn + py * c]; }
// где кисти: [правая, левая, видна ли левая]. Правая — на рукояти; копьё, пика, вилы без щита — двумя руками (левая
// впереди по древку); левая — за щитом (не видна), на луке или ложе арбалета; без всего — у бедра
function handsOf(k, W, Sh, bowSt = 0, xb = 0){
  const w = k.weapon;
  if(w === "bow"){ const d = [0, 0.15, 0.55, 1, 0][bowSt], ty = -0.2 - 0.02 * d, cy = -0.56 - 0.1 * d; return [[0.02, Math.min(0.05, ty + 0.36 * d + 0.03)], [0, (ty + cy) / 2], true]; }
  if(w === "crossbow") return [xb >= 2 ? [0.05, xb === 2 ? -0.4 : -0.2] : [0.05, -0.02], [0.05, xb >= 2 ? -0.46 : -0.3], true];
  const r = W ? [W[0], W[1]] : null;
  if(W && !Sh && (w === "pike" || w === "spear" || w === "fork")) return [r, onPose(W, 0, -0.4), true];
  return [r, Sh ? [Sh[0], Sh[1]] : [-0.235, -0.07], !Sh];
}
function paintForearm(g, k){ stick(g, 0, 0, 0, -FA_LEN, 0.072, sleeveOf(k)); }          // в своих осях: локоть (0, 0), кисть (0, −FA_LEN)
function paintHand(g, kind){                                                              // кулак: пальцы — штрихи поперёк
  const c = HAND_COL[kind];
  g.beginPath(); g.ellipse(0, 0, 0.04, 0.045, 0, 0, 6.283); g.fillStyle = vol(g, c, 0, -0.01, 0.05, kind === "plate" ? 1.4 : 1); g.fill(); edge(g, 0.022);
  g.beginPath(); for(const y of [-0.012, 0.008]){ g.moveTo(-0.028, y); g.lineTo(0.028, y); } g.strokeStyle = shade(c, -0.4); g.lineWidth = 0.008; g.stroke();
}
// рука прямо в рисунке (готовый спрайт бойца): от локтя к кисти
function drawArm(g, k, side, h){
  const ex = side * ELB[0], ey = ELB[1], dx = h[0] - ex, dy = h[1] - ey, L = Math.hypot(dx, dy);
  if(L < 0.02) return;
  g.save(); g.translate(ex, ey); g.rotate(Math.atan2(dx, -dy)); g.scale(1, L / FA_LEN); paintForearm(g, k); g.restore();
}
function drawHand(g, k, h){ g.save(); g.translate(h[0], h[1]); paintHand(g, handKind(k)); g.restore(); }
function shieldPath(g, shape){
  g.beginPath();
  if(shape === "heater"){ g.moveTo(-0.2, -0.24); g.lineTo(0.2, -0.24); g.quadraticCurveTo(0.21, 0.08, 0, 0.29); g.quadraticCurveTo(-0.21, 0.08, -0.2, -0.24); g.closePath(); }
  else if(shape === "oval") g.ellipse(0, 0, 0.19, 0.28, 0, 0, 6.283);
  else g.arc(0, 0, shape === "buckler" ? 0.13 : 0.24, 0, 6.283);
}
function paintShield(g, sh){
  const field = sh.paint === "wood" ? "#9a7a50" : sh.paint === "steel" ? STEEL : sh.c1;
  shieldPath(g, sh.shape); g.fillStyle = vol(g, field, 0, 0, sh.shape === "buckler" ? 0.15 : 0.3, sh.paint === "steel" ? 1.3 : 0.8); g.fill();
  g.save(); g.clip(); g.fillStyle = sh.c2;
  switch(sh.paint){
    case "halves": g.fillRect(0, -1, 1, 2); break;
    case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
    case "stripe": g.fillRect(-1, -0.06, 2, 0.12); break;
    case "cross": g.fillRect(-0.045, -1, 0.09, 2); g.fillRect(-1, -0.1, 2, 0.09); break;
    case "chevron": g.beginPath(); g.moveTo(-0.3, 0.12); g.lineTo(0, -0.12); g.lineTo(0.3, 0.12); g.lineTo(0.3, 0.24); g.lineTo(0, 0); g.lineTo(-0.3, 0.24); g.closePath(); g.fill(); break;
    case "boss": g.beginPath(); g.arc(0, 0, 0.15, 0, 6.283); g.lineWidth = 0.05; g.strokeStyle = sh.c2; g.stroke(); break;
    case "wood": g.beginPath(); for(let x = -0.18; x < 0.3; x += 0.09){ g.moveTo(x, -0.3); g.lineTo(x, 0.3); } g.strokeStyle = "rgba(60,40,20,.45)"; g.lineWidth = 0.012; g.stroke(); break;
  }
  // объём: к краю темнее, по краю — обод (у дерева и круглых — железный, у треугольного — кожаный кант)
  const gr = g.createRadialGradient(0, 0, 0.05, 0, 0, 0.3); gr.addColorStop(0, "rgba(255,255,255,.12)"); gr.addColorStop(0.6, "rgba(0,0,0,0)"); gr.addColorStop(1, "rgba(20,14,8,.32)");
  g.fillStyle = gr; g.fillRect(-1, -1, 2, 2);
  g.restore();
  if(sh.paint !== "steel"){ shieldPath(g, sh.shape); g.strokeStyle = sh.shape === "heater" ? "#5a4028" : STEEL_D; g.lineWidth = 0.05; g.stroke(); }
  shieldPath(g, sh.shape); edge(g);
  if(sh.shape !== "heater"){
    g.beginPath(); g.arc(0, 0, sh.shape === "buckler" ? 0.05 : 0.06, 0, 6.283); g.fillStyle = vol(g, STEEL, -0.01, -0.01, 0.07, 1.4); g.fill(); edge(g, 0.025);
    ell(g, -0.014, -0.016, 0.016, 0.016, SHINE, 0, false);
  }
}
// оружие: в своих осях, рукоять — в начале (там кисть), острие — вперёд (−y)
const WBOX = {spear: [-0.06, -1.47, 0.06, 0.6], fork: [-0.1, -1.23, 0.1, 0.45], pike: [-0.06, -4.06, 0.06, 0.75], lance: [-0.08, -2.94, 0.27, 0.65],
  sword: [-0.09, -0.76, 0.09, 0.13], falchion: [-0.09, -0.66, 0.1, 0.13], axe: [-0.05, -0.72, 0.18, 0.16], mace: [-0.09, -0.6, 0.09, 0.14], club: [-0.08, -0.64, 0.08, 0.13]};
function paintWeapon(g, w, col){
  switch(w){
    case "spear": stick(g, 0, 0.55, 0, -1.25, 0.035, WOOD); tip(g, 0, -1.25, 0.18); break;
    case "fork":
      stick(g, 0, 0.4, 0, -1.0, 0.035, WOOD);
      g.beginPath(); g.moveTo(-0.06, -1.18); g.lineTo(-0.06, -1.03); g.quadraticCurveTo(-0.06, -0.98, 0, -0.98); g.quadraticCurveTo(0.06, -0.98, 0.06, -1.03); g.lineTo(0.06, -1.18);
      g.strokeStyle = INK; g.lineWidth = 0.045; g.stroke(); g.strokeStyle = STEEL; g.lineWidth = 0.024; g.stroke(); break;
    case "pike": stick(g, 0, 0.7, 0, -3.8, 0.04, WOOD); tip(g, 0, -3.8, 0.22); break;
    case "lance":
      stick(g, 0, 0.6, 0, -2.7, 0.045, "#8a6a45"); ell(g, 0, -0.06, 0.075, 0.03, STEEL); tip(g, 0, -2.7, 0.2);
      g.beginPath(); g.moveTo(0.02, -2.46); g.lineTo(0.24, -2.38); g.lineTo(0.02, -2.29); g.closePath(); g.fillStyle = col; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.02; g.stroke(); break;
    case "sword": case "falchion": {
      stick(g, 0, 0.09, 0, -0.03, 0.03, LEATHER[0]); stick(g, -0.075, -0.035, 0.075, -0.035, 0.025, STEEL_D);
      g.beginPath();
      if(w === "sword"){ g.moveTo(-0.018, -0.04); g.lineTo(-0.014, -0.66); g.lineTo(0, -0.73); g.lineTo(0.014, -0.66); g.lineTo(0.018, -0.04); }
      else { g.moveTo(-0.02, -0.04); g.lineTo(-0.022, -0.46); g.quadraticCurveTo(-0.01, -0.64, 0.07, -0.5); g.lineTo(0.03, -0.04); }
      g.closePath(); g.fillStyle = SHINE; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.016; g.stroke();
      ell(g, 0, 0.1, 0.022, 0.022, STEEL_D); break;
    }
    case "axe":
      stick(g, 0, 0.12, 0, -0.6, 0.035, WOOD);
      g.beginPath(); g.moveTo(0.012, -0.64); g.lineTo(0.12, -0.69); g.quadraticCurveTo(0.17, -0.56, 0.12, -0.43); g.lineTo(0.012, -0.5); g.closePath();
      g.fillStyle = STEEL; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.02; g.stroke(); break;
    case "mace":
      stick(g, 0, 0.1, 0, -0.45, 0.032, WOOD);
      g.beginPath();
      for(let q = 0; q < 6; q++){ const a = q * Math.PI / 3; g.moveTo(Math.cos(a) * 0.04, -0.5 + Math.sin(a) * 0.04); g.lineTo(Math.cos(a) * 0.08, -0.5 + Math.sin(a) * 0.08); }
      g.strokeStyle = INK; g.lineWidth = 0.035; g.stroke(); g.strokeStyle = STEEL_D; g.lineWidth = 0.02; g.stroke();
      ell(g, 0, -0.5, 0.05, 0.05, STEEL); break;
    case "club":
      g.beginPath(); g.moveTo(-0.02, 0.1); g.lineTo(-0.055, -0.5); g.quadraticCurveTo(0, -0.62, 0.055, -0.5); g.lineTo(0.02, 0.1); g.closePath();
      g.fillStyle = "#6a4a2e"; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.02; g.stroke();
      ell(g, -0.02, -0.35, 0.012, 0.012, "#3e2a18", 0, false); ell(g, 0.025, -0.48, 0.012, 0.012, "#3e2a18", 0, false); break;
  }
}
// лук — в осях бойца: 0 опущен, 1 стрела на тетиве, 2 натянут вполовину, 3 до щеки, 4 выстрел (тетива дрожит)
function paintBow(g, st){
  const d = [0, 0.15, 0.55, 1, 0][st], tx = 0.3 - 0.06 * d, ty = -0.2 - 0.02 * d, cy = -0.56 - 0.1 * d, ny = ty + 0.36 * d;
  if(st === 4){
    g.beginPath(); g.moveTo(-tx, ty + 0.03); g.quadraticCurveTo(0, ty + 0.06, tx, ty + 0.03); g.moveTo(-tx, ty - 0.03); g.quadraticCurveTo(0, ty - 0.06, tx, ty - 0.03);
    g.strokeStyle = "rgba(239,233,220,.45)"; g.lineWidth = 0.012; g.stroke();
  }
  g.beginPath(); g.moveTo(-tx, ty); g.lineTo(0.02, ny); g.lineTo(tx, ty); g.strokeStyle = "#efe9dc"; g.lineWidth = 0.014; g.stroke();
  if(st >= 1 && st <= 3){ stick(g, 0.02, ny + 0.02, 0.02, ny - 0.72, 0.014, "#d8cdb5"); tip(g, 0.02, ny - 0.72, 0.07); ell(g, 0.02, ny + 0.03, 0.022, 0.03, "#efe9dc", 0, false); }
  g.beginPath(); g.moveTo(-tx, ty); g.quadraticCurveTo(0, cy, tx, ty);
  g.strokeStyle = INK; g.lineWidth = 0.075; g.stroke(); g.strokeStyle = WOOD; g.lineWidth = 0.045; g.stroke();
}
// арбалет — в осях бойца: 0 взведён, болт на ложе; 1 разряжен; 2 и 3 — взвод через стремя (В13): ложе наклонено к земле
// (сверху видно коротким), стремя впереди у ног; 2 — тетива у дуги, 3 — подтянута крюком на ремне к поясу
function paintXbow(g, st){
  if(st >= 2){
    const sy = st === 2 ? -0.42 : -0.2;
    stick(g, 0.05, -0.12, 0.05, -0.44, 0.06, WOOD);
    g.beginPath(); g.ellipse(0.05, -0.53, 0.06, 0.04, 0, 0, 6.283); g.strokeStyle = INK; g.lineWidth = 0.035; g.stroke();
    g.strokeStyle = STEEL_D; g.lineWidth = 0.018; g.stroke();
    g.beginPath(); g.moveTo(-0.2, -0.44); g.lineTo(0.05, sy); g.lineTo(0.3, -0.44); g.strokeStyle = "#efe9dc"; g.lineWidth = 0.014; g.stroke();
    if(st === 3) stick(g, 0.05, sy, 0.05, -0.06, 0.012, "#5a4630");
    g.beginPath(); g.moveTo(-0.2, -0.44); g.quadraticCurveTo(0.05, -0.5, 0.3, -0.44);
    g.strokeStyle = INK; g.lineWidth = 0.07; g.stroke(); g.strokeStyle = STEEL; g.lineWidth = 0.04; g.stroke();
    return;
  }
  stick(g, 0.05, 0.02, 0.05, -0.5, 0.06, WOOD);
  g.beginPath(); g.moveTo(-0.22, -0.45); g.lineTo(0.05, st ? -0.45 : -0.3); g.lineTo(0.32, -0.45); g.strokeStyle = "#efe9dc"; g.lineWidth = 0.014; g.stroke();
  if(!st){ stick(g, 0.05, -0.29, 0.05, -0.6, 0.016, "#d8cdb5"); tip(g, 0.05, -0.6, 0.06); }
  g.beginPath(); g.moveTo(-0.22, -0.45); g.quadraticCurveTo(0.05, -0.58, 0.32, -0.45);
  g.strokeStyle = INK; g.lineWidth = 0.07; g.stroke(); g.strokeStyle = STEEL; g.lineWidth = 0.04; g.stroke();
}
// ── Конь по частям (В13): ноги, хвост, туловище, голова с шеей, попона — отдельные части, походку задаёт код ──
// Оси коня: начало — середина спины (седло), вперёд — −y. Рисуются по порядку: ноги, хвост, туловище, голова, попона,
// потом всадник. Так в атласе Unity у коня 5 частей на масть вместо кадра на каждую масть × попону × кадр.
const HLEG = [[-0.17, -0.5], [0.17, -0.5], [-0.17, 0.66], [0.17, 0.66]];   // ЛП, ПП, ЛЗ, ПЗ — где нога в покое (чуть из-под боков)
const HTAIL = [0, 0.84], HNECK = [0, -0.5];                                   // корень хвоста и шеи
const HBOX = {leg: [-0.075, -0.18, 0.075, 0.18], tail: [-0.16, -0.05, 0.16, 0.55], body: [-0.25, -0.7, 0.25, 0.95],
  head: [-0.16, -0.82, 0.16, 0.06], cover: [-0.3, -0.65, 0.3, 0.9]};
function paintHorseLeg(g, k){ ell(g, 0, 0, 0.06, 0.16, shade(k.coat, -0.4)); }   // нога из-под туловища
function paintHorseTail(g, k){                                                     // от корня назад (+y)
  g.beginPath(); for(const s of [-1, 0, 1]){ g.moveTo(s * 0.03, 0); g.quadraticCurveTo(s * 0.09, 0.22, s * 0.06 + 0.02, 0.46); }
  g.strokeStyle = INK; g.lineWidth = 0.075; g.stroke(); g.strokeStyle = shade(k.coat, -0.4); g.lineWidth = 0.04; g.stroke();
}
function paintHorseBody(g, k){ ell(g, 0, 0.12, 0.22, 0.8, k.coat); }
function paintHorseHead(g, k){                                                     // от основания шеи вперёд (−y)
  const head = k.bard === "full" ? STEEL : k.coat;
  g.beginPath(); g.moveTo(-0.1, 0); g.quadraticCurveTo(-0.13, -0.4, -0.065, -0.7);
  g.quadraticCurveTo(0, -0.78, 0.065, -0.7); g.quadraticCurveTo(0.13, -0.4, 0.1, 0); g.closePath();
  g.fillStyle = head; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.04; g.stroke();
  ell(g, -0.06, -0.48, 0.025, 0.05, head); ell(g, 0.06, -0.48, 0.025, 0.05, head);               // уши
  if(k.bard !== "full") stick(g, 0, 0.02, 0, -0.44, 0.04, shade(k.coat, -0.4));                 // грива
}
function paintHorseCover(g, k){
  if(k.bard === "full"){ ell(g, 0, 0.14, 0.26, 0.74, k.col); ell(g, 0, 0.14, 0.2, 0.66, shade(k.col, 0.12), 0, false);
    g.fillStyle = k.c2; g.fillRect(-0.025, -0.55, 0.05, 1.35); }                                   // попона до копыт, полоса герба
  else if(k.bard === "cloth") ell(g, 0, 0.08, 0.27, 0.34, shade(k.col, -0.15));                 // чепрак
  else ell(g, 0, 0.08, 0.17, 0.22, LEATHER[0]);                                                  // седло
}
// Аллюр (В13) по скорости, м/с: стоит < 0,35 ≤ шаг < 2,3 ≤ рысь < 4,8 ≤ галоп. Фаза — круги шага (1 — все четыре ноги
// прошли по разу), копится по пройденному: путь ÷ длина круга аллюра, поэтому ноги не скользят по земле.
// Ноги ходят вперёд-назад со сдвигом по фазе: шаг — по одной (ЛЗ, ЛП, ПЗ, ПП), рысь — по диагонали парами,
// галоп — задние почти вместе, потом передние. Голова кивает (на шаге дважды за круг), хвост машет, всадника качает.
const GAIT = {
  walk:   {amp: 0.15, off: [0.25, 0.75, 0, 0.5], stride: 1.7, nod: 0.04, nodN: 2, tail: 0.1, bob: 0.008},
  trot:   {amp: 0.22, off: [0, 0.5, 0.5, 0], stride: 2.8, nod: 0.012, nodN: 2, tail: 0.07, bob: 0.025},
  gallop: {amp: 0.32, off: [0.4, 0.5, 0, 0.1], stride: 5.0, nod: 0.06, nodN: 1, tail: 0.18, bob: 0.035},
};
const gaitOf = v => v < 0.35 ? null : v < 2.3 ? "walk" : v < 4.8 ? "trot" : "gallop";
const strideOf = v => { const G = GAIT[gaitOf(v)]; return G ? G.stride : 1.7; };
// поза коня: ноги (сдвиг вперёд, м — со знаком минус), кивок головы, поворот хвоста (рад), качка всадника.
// Стоит — переступает, отмахивается хвостом и вскидывает голову раз в несколько секунд (у каждого коня — свой ритм)
function horsePose(v, ph, t, s){
  const G = GAIT[gaitOf(v)], tau = 2 * Math.PI;
  if(!G){
    const slot = Math.floor(t / 3.5 + hash(s, 31) * 4), r = hash(s * 13 + slot, 32), u = frac(t / 3.5 + hash(s, 31) * 4);
    const pulse = Math.sin(u * Math.PI);
    return {legs: [0, 0, r < 0.25 ? -0.06 * pulse : 0, r > 0.75 ? -0.06 * pulse : 0], nod: r > 0.4 && r < 0.6 ? 0.05 * pulse : 0.01 * Math.sin(t * 0.8 + s),
      tail: (r < 0.5 ? 0.25 : 0.06) * Math.sin(t * 5 + s) * pulse, bob: 0};
  }
  return {legs: G.off.map(o => -G.amp * Math.sin(tau * (ph - o))), nod: -G.nod * Math.sin(tau * G.nodN * ph),
    tail: G.tail * Math.sin(tau * ph + 1), bob: G.bob * Math.sin(tau * 2 * ph)};
}
// конь целиком одним рисунком (для бойца издали и кадров атласа старого вида): f — кадр галопа 0…3, −1 — стоит
function paintHorse(g, k, f){
  const P = f < 0 ? horsePose(0, 0, 0, 0) : horsePose(8, f / 4, 0, 0);
  const at = (x, y, r, fn) => { g.save(); g.translate(x, y); g.rotate(r); fn(); g.restore(); };
  HLEG.forEach(([x, y], i) => at(x, y + P.legs[i], 0, () => paintHorseLeg(g, k)));
  at(HTAIL[0], HTAIL[1], P.tail, () => paintHorseTail(g, k));
  paintHorseBody(g, k);
  at(HNECK[0], HNECK[1] + P.nod, 0, () => paintHorseHead(g, k));
  paintHorseCover(g, k);
}
// Лежащий (павший, раненый — В8, Г39; облик В17): руки и ноги — трубки с объёмом, кисти и сапоги, корпус по доспеху
// (простёжка, бригантина, кольчуга, латы) с поясом. Лежащий виден сверху во весь рост — вид сверху для него верен.
// P.legs — ломаные от бедра к ступне, P.arms — от плеча к кисти; голова — отдельно (Unity кладёт шлем)
function paintLying(g, cloth, armour, leather, P){
  const pants = mix(cloth, "#3e3328", 0.55), boot = "#3a2c20", sleeve = sleeveOf({armour, leather, cloth});
  const tube = (pts, w, col) => {
    g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y));
    g.strokeStyle = INK; g.lineWidth = w + 0.025; g.stroke();
    g.strokeStyle = shade(col, -0.3); g.lineWidth = w; g.stroke();
    g.strokeStyle = col; g.lineWidth = w * 0.62; g.stroke();
    g.strokeStyle = shade(col, 0.15); g.lineWidth = w * 0.22; g.stroke();
  };
  for(const L of P.legs){
    tube(L, 0.11, pants);
    const [fx, fy] = L[L.length - 1]; g.beginPath(); g.ellipse(fx, fy + 0.04, 0.055, 0.08, 0, 0, 6.283); g.fillStyle = vol(g, boot, fx, fy + 0.03, 0.09); g.fill(); edge(g, 0.02);
  }
  for(const A of P.arms){
    tube(A, 0.095, sleeve);
    const [hx, hy] = A[A.length - 1]; g.beginPath(); g.arc(hx, hy, 0.04, 0, 6.283); g.fillStyle = vol(g, SKIN, hx, hy, 0.05); g.fill(); edge(g, 0.018);
  }
  const torso = () => { g.beginPath(); g.moveTo(-0.21, -0.36); g.quadraticCurveTo(-0.23, -0.45, -0.12, -0.45); g.lineTo(0.12, -0.45); g.quadraticCurveTo(0.23, -0.45, 0.21, -0.36);
    g.lineTo(0.18, 0.12); g.quadraticCurveTo(0, 0.17, -0.18, 0.12); g.closePath(); };
  const base = armour === "mail" ? MAIL : armour === "plate" ? STEEL : cloth;
  torso(); g.fillStyle = vol(g, base, 0, -0.15, 0.35, armour === "plate" ? 1.2 : 0.8); g.fill();
  g.save(); torso(); g.clip();
  if(armour === "mail") rings(g, -0.25, -0.5, 0.25, 0.2, MAIL_D);
  else if(armour === "plate"){ g.strokeStyle = SHINE; g.lineWidth = 0.02; g.beginPath(); g.moveTo(0, -0.42); g.lineTo(0, 0.1); g.stroke(); }
  else if(armour === "leather"){ for(let y = -0.38; y < 0.12; y += 0.08) for(let x = -0.15; x <= 0.15; x += 0.06) ell(g, x, y, 0.01, 0.01, "#c9c3b4", 0, false); }
  else { g.strokeStyle = shade(base, -0.3); g.lineWidth = 0.009; g.beginPath(); for(let d = -0.7; d <= 0.7; d += 0.08){ g.moveTo(d - 0.3, -0.5); g.lineTo(d + 0.3, 0.2); g.moveTo(d + 0.3, -0.5); g.lineTo(d - 0.3, 0.2); } g.stroke(); }
  g.fillStyle = "#5a4028"; g.fillRect(-0.2, 0.0, 0.4, 0.045);
  g.restore(); torso(); edge(g, 0.03);
}
// поза павшего: на спине, ноги чуть врозь, руки раскинуты (v — вариант)
function corpsePose(v){
  const r = j => hash(v * 131 + 7, j), sp = 0.05 + 0.12 * r(1), la = -0.3 - 1.2 * r(2), ra = 0.3 + 1.2 * r(3);
  const arm = (sx, a) => [[sx, -0.3], [sx + Math.sin(a) * 0.46, -0.3 + Math.cos(a) * 0.46]];
  return {legs: [[[-0.07, 0.1], [-0.07 - sp, 0.78]], [[0.07, 0.1], [0.07 + sp * 0.7, 0.8]]], arms: [arm(-0.19, la), arm(0.19, ra)]};
}
// поза раненого: лицом вниз, правая рука вперёд, левая согнута, правая нога подтянута
const CRAWL_POSE = {legs: [[[-0.08, 0.1], [-0.12, 0.8]], [[0.08, 0.1], [0.3, 0.42], [0.22, 0.76]]],
  arms: [[[0.19, -0.3], [0.27, -0.95]], [[-0.19, -0.3], [-0.4, -0.46], [-0.27, -0.76]]]};
// павший: лежит на спине, голова — к −y, ноги у начала; рядом — оружие и щит; краски приглушены (В8)
function paintCorpse(g, k, v){
  const r = j => hash(v * 131 + 7, j), pants = mix(k.cloth, "#3e3328", 0.55), boot = "#3a2c20";
  const sleeve = k.armour === "mail" ? "#8e9296" : k.armour === "leather" ? k.leather : k.armour === "plate" ? STEEL : k.cloth;
  if(k.shield){ g.save(); g.translate(-0.48 - 0.15 * r(8), -0.1 + 0.4 * r(9)); g.rotate(r(10) * 6.283); g.scale(0.85, 0.85); paintShield(g, k.shield); g.restore(); }
  const w = k.weapon === "bow" || k.weapon === "crossbow" ? k.side : k.weapon;
  if(w && w !== "none"){
    g.save(); g.translate(0.45 + 0.15 * r(5), -0.25 + 0.5 * r(6)); g.rotate(r(7) * 6.283);
    const sc = w === "pike" || w === "lance" ? 0.4 : w === "spear" || w === "fork" ? 0.7 : 0.9; g.scale(sc, sc); paintWeapon(g, w, k.col); g.restore();
  } else if(k.weapon === "bow"){ g.save(); g.translate(0.5, 0.1); g.rotate(r(7) * 6.283); paintBow(g, 0); g.restore(); }
  paintLying(g, k.cloth, k.armour, k.leather, corpsePose(v));
  g.save(); g.translate(0, -0.56); paintHead(g, k); g.restore();
  g.globalCompositeOperation = "source-atop"; g.fillStyle = "rgba(112,106,96,.38)"; g.fillRect(-2, -2, 4, 4); g.globalCompositeOperation = "source-over";
}
// раненый (Г39, В13): лежит лицом вниз, голова к −y, правая рука вытянута вперёд, левая согнута в локте, правая нога
// подтянута — так он ползёт; следующий рывок — тот же рисунок, отражённый слева направо. Краски живые, не приглушены
function sleeveOf(k){ return k.armour === "mail" ? "#8e9296" : k.armour === "leather" ? k.leather : k.armour === "plate" ? STEEL : k.cloth; }
function paintCrawlBody(g, cloth, armour, leather){ paintLying(g, cloth, armour, leather, CRAWL_POSE); }
function paintCrawl(g, k){ paintCrawlBody(g, k.cloth, k.armour, k.leather); g.save(); g.translate(0, -0.56); paintHead(g, k); g.restore(); }
// павший конь лежит на боку: ноги в сторону
function paintDeadHorse(g, k){
  const hc = k.coat, dark = shade(hc, -0.4);
  for(const [y, a] of [[-0.45, 0.3], [-0.35, 0.7], [0.5, 0.2], [0.62, 0.6]]) stick(g, 0.12, y, 0.12 + Math.cos(a) * 0.55, y + Math.sin(a) * 0.25, 0.08, dark);
  ell(g, 0, 0.1, 0.34, 0.82, hc);
  g.beginPath(); g.moveTo(-0.08, -0.6); g.quadraticCurveTo(-0.3, -0.95, -0.2, -1.22); g.quadraticCurveTo(-0.08, -1.3, 0.0, -1.18); g.quadraticCurveTo(0.08, -0.9, 0.1, -0.62); g.closePath();
  g.fillStyle = hc; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.04; g.stroke();
  if(k.bard === "full") ell(g, 0, 0.12, 0.3, 0.72, k.col); else if(k.bard === "cloth") ell(g, -0.04, 0.05, 0.3, 0.32, shade(k.col, -0.15));
  g.globalCompositeOperation = "source-atop"; g.fillStyle = "rgba(112,106,96,.38)"; g.fillRect(-2, -2, 4, 4); g.globalCompositeOperation = "source-over";
}

// ── Спрайты частей: кэш по ключу и ступени приближения ──
const SPR = new Map();
let ZK = "", ZQ = 1;
function setZoom(){ ZQ = Math.pow(1.2, Math.round(Math.log(view.s) / Math.log(1.2))); ZK = "|" + ZQ.toFixed(3); }
function spr(key, box, paint){
  const k2 = key + ZK;
  let sp = SPR.get(k2);
  if(sp) return sp;
  const [x0, y0, x1, y1] = box, k = Math.min(ZQ, 100) * (window.devicePixelRatio || 1);
  const c = document.createElement("canvas");
  c.width = Math.max(1, Math.ceil((x1 - x0) * k)); c.height = Math.max(1, Math.ceil((y1 - y0) * k));
  const g = c.getContext("2d");
  g.scale(k, k); g.translate(-x0, -y0); g.lineJoin = "round"; g.lineCap = "round";
  paint(g);
  if(SPR.size > 4000){ let n = 0; for(const kk of SPR.keys()){ SPR.delete(kk); if(++n >= 1200) break; } }
  sp = {c, x: x0, y: y0, w: x1 - x0, h: y1 - y0};
  SPR.set(k2, sp);
  return sp;
}
const bodySpr = k => spr("b" + k.bodyKey, [-0.31, -0.3, 0.31, 0.39], g => paintBody(g, k));
const shieldSpr = sh => spr("s" + sh.key, [-0.25, -0.3, 0.25, 0.31], g => paintShield(g, sh));
const weapSpr = (w, col) => spr("w" + w + (w === "lance" ? col : ""), WBOX[w], g => paintWeapon(g, w, col));
const bowSpr = st => spr("bow" + st, [-0.36, -1.12, 0.36, 0.2], g => paintBow(g, st));
const xbowSpr = st => spr("xb" + st, [-0.3, -0.68, 0.38, 0.08], g => paintXbow(g, st));
const armSpr = k => spr("fa" + sleeveOf(k), [-0.06, -FA_LEN - 0.06, 0.06, 0.06], g => paintForearm(g, k));
const handSpr = kind => spr("hd" + kind, [-0.05, -0.055, 0.05, 0.055], g => paintHand(g, kind));
// рука по частям: предплечье — от локтя к кисти (растянуто по длине), кисть — отдельно, поверх оружия
function putArm(M, k, side, h){
  const ex = side * ELB[0], ey = ELB[1], dx = h[0] - ex, dy = h[1] - ey, L = Math.hypot(dx, dy);
  if(L >= 0.02) put(armSpr(k), mS(mR(mT(M, ex, ey), Math.atan2(dx, -dy)), 1, L / FA_LEN));
}
const putHand = (M, k, h) => put(handSpr(handKind(k)), mT(M, h[0], h[1]));
const bootSpr = () => spr("boot", [-0.06, -0.08, 0.06, 0.08], g => ell(g, 0, 0, 0.045, 0.07, "#3a2c20", 0, false));
const sparkSpr = () => spr("spark", [-0.16, -0.16, 0.16, 0.16], paintSpark);
// вспышка удара (В13): четырёхлучевая звезда — белая, середина жёлтая, тонкий контур
function paintSpark(g){
  g.beginPath();
  for(let i = 0; i < 8; i++){ const a = i * Math.PI / 4 - Math.PI / 2, r = i % 2 ? 0.045 : 0.15; g.lineTo(Math.cos(a) * r, Math.sin(a) * r); }
  g.closePath(); g.fillStyle = "#fffbe8"; g.fill(); g.strokeStyle = "rgba(43,38,33,.6)"; g.lineWidth = 0.012; g.stroke();
  ell(g, 0, 0, 0.04, 0.04, "#f3d36b", 0, false);
}
const horseSpr = (k, f) => spr("h" + k.horseKey + "|" + f, [-0.36, -1.36, 0.36, 1.37], g => paintHorse(g, k, f));
// конь по частям (В13): поза — из horsePose
function putHorse(k, base, P){
  const leg = spr("hl" + k.coat, HBOX.leg, g => paintHorseLeg(g, k));
  HLEG.forEach(([x, y], i) => put(leg, mT(base, x, y + P.legs[i])));
  put(spr("ht" + k.coat, HBOX.tail, g => paintHorseTail(g, k)), mR(mT(base, HTAIL[0], HTAIL[1]), P.tail));
  put(spr("hb" + k.coat, HBOX.body, g => paintHorseBody(g, k)), base);
  put(spr("hh" + k.coat + (k.bard === "full" ? "f" : ""), HBOX.head, g => paintHorseHead(g, k)), mT(base, HNECK[0], HNECK[1] + P.nod));
  put(spr("hc" + k.horseKey, HBOX.cover, g => paintHorseCover(g, k)), base);
}
const corpseSpr = (k, v) => spr("c" + k.bodyKey + (k.shield ? k.shield.key : "") + k.weapon + k.side + v, [-1.05, -1.0, 1.05, 1.0], g => paintCorpse(g, k, v));
const deadHorseSpr = k => spr("dh" + k.horseKey, [-0.5, -1.35, 0.75, 1.0], g => paintDeadHorse(g, k));
const crawlSpr = k => spr("cr" + k.bodyKey, [-0.5, -1.06, 0.45, 0.95], g => paintCrawl(g, k));
// мягкая тень под бойцом (В5)
const SHADOW = (() => {
  const c = document.createElement("canvas"); c.width = c.height = 32;
  const g = c.getContext("2d"), gr = g.createRadialGradient(16, 16, 0, 16, 16, 16);
  gr.addColorStop(0, "rgba(24,18,8,.42)"); gr.addColorStop(0.6, "rgba(24,18,8,.22)"); gr.addColorStop(1, "rgba(24,18,8,0)");
  g.fillStyle = gr; g.fillRect(0, 0, 32, 32);
  return c;
})();

// Поза в покое: где оружие и щит. [x, y, поворот, масштаб поперёк, масштаб вдоль] — вдоль меньше 1: древко наклонено к нам
// (стоймя копьё сверху — короткий обрубок), щит в руке виден наискосок.
// low (В13) — насколько отряд опустил древки: 0 — стоймя (марш, покой), 1 — к бою: копья в 2 передних шеренгах,
// пики в 4; передняя шеренга опускает первой, следующие — с запаздыванием.
const mixPose = (a, b, e) => a.map((v, i) => v + (b[i] - v) * e);
function restPose(k, rank, low = 1){
  const w = k.weapon;
  let W = null, Sh = null;
  if(w === "spear" || w === "fork") W = mixPose([0.21, -0.06, 0.15, 1, 0.2], [0.2, -0.1, 0, 1, 1], rank < 2 ? ease(Math.min(1, Math.max(0, low * 1.25 - rank * 0.2))) : 0);
  else if(w === "pike") W = mixPose([0.15, -0.04, 0.1, 1, 0.12], [rank % 2 ? -0.3 : 0.12, 0, 0, 1, 1], rank < 4 ? ease(Math.min(1, Math.max(0, low * 1.4 - rank * 0.13))) : 0);
  else if(w === "lance") W = [0.22, 0.05, 0.05, 1, 0.24];
  else if(w !== "bow" && w !== "crossbow") W = [0.22, -0.06, 0.3, 1, 0.65];
  if(k.shield) Sh = k.horse ? [-0.27, 0, Math.PI / 2, 0.75, 0.42] : k.shield.shape === "buckler" ? [-0.2, 0.07, 0, 1, 0.6] : [-0.16, -0.23, -0.25, 1, -0.42];
  return {W, Sh};
}
function compSpr(k, rank){   // издали — весь боец одним спрайтом
  const pose = k.weapon === "pike" ? (rank < 4 ? rank % 2 : 2) : k.weapon === "spear" || k.weapon === "fork" ? (rank < 2 ? 0 : 1) : 0;
  const box = k.horse ? [-0.5, -3.0, 0.5, 1.45] : k.weapon === "pike" ? [-0.45, -4.1, 0.45, 0.85] : k.weapon === "spear" || k.weapon === "fork" ? [-0.45, -1.5, 0.45, 0.62] : [-0.45, -1.15, 0.45, 0.45];
  return spr("m" + k.bodyKey + (k.shield ? k.shield.key : "") + k.weapon + k.horseKey + pose, box, g => {
    const P = restPose(k, [0, 2, 9][pose] ?? rank);
    if(k.weapon === "pike") P.W = pose === 2 ? [0.15, -0.04, 0.1, 1, 0.12] : [pose ? -0.3 : 0.12, 0, 0, 1, 1];
    if(k.horse) paintHorse(g, k, -1);
    const [hr, hl, showL] = handsOf(k, P.W, P.Sh);
    if(hr) drawArm(g, k, 1, hr); if(hl) drawArm(g, k, -1, hl);
    paintBody(g, k);
    const at = (T, fn) => { g.save(); g.translate(T[0], T[1]); g.rotate(T[2]); g.scale(T[3], T[4]); fn(); g.restore(); };
    if(P.Sh) at(P.Sh, () => paintShield(g, k.shield));
    if(k.weapon === "bow") paintBow(g, 0); else if(k.weapon === "crossbow") paintXbow(g, 0);
    else if(P.W) at(P.W, () => paintWeapon(g, k.weapon, k.col));
    if(hr) drawHand(g, k, hr); if(hl && showL) drawHand(g, k, hl);
  });
}

// ── Матрицы: [a, b, c, d, e, f], как у canvas ──
const mT = (m, x, y) => [m[0], m[1], m[2], m[3], m[4] + m[0] * x + m[2] * y, m[5] + m[1] * x + m[3] * y];
function mR(m, r){ const c = Math.cos(r), s = Math.sin(r); return [m[0] * c + m[2] * s, m[1] * c + m[3] * s, m[2] * c - m[0] * s, m[3] * c - m[1] * s, m[4], m[5]]; }
const mS = (m, x, y) => [m[0] * x, m[1] * x, m[2] * y, m[3] * y, m[4], m[5]];
const mP = (m, T) => mS(mR(mT(m, T[0], T[1]), T[2]), T[3], T[4]);
function put(sp, m){ ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]); ctx.drawImage(sp.c, sp.x, sp.y, sp.w, sp.h); }

// ═══════════ Стоящий боец (В17): косой вид — земля сверху, фигурка стоит ═══════════
// Экран (в метрах карты) = (x, y − OBL_K·z): высота уходит вверх по экрану. Боец — из частей: цилиндры (ноги, руки,
// шея, колчан), шары (суставы, голова, кисти), срезы корпуса (по краю — пояс, простёжка, заклёпки, кольчуга, сюрко
// с гербом: виден тот край, что к зрителю), сверху — плечи (рисунок вида сверху), лицо или забрало — спереди головы,
// шлем — сверху, щит, павеза и плащ — в вертикальной плоскости, оружие — по направлению из позы вида сверху (удары и
// движения переносятся как есть). Части — от дальних к ближним по глубине y·K + z.
// Unity собирает фигурку так же (MenView) из тех же частей атласа; ткань, кожу, масть Unity умножает на белую основу
// части, полигон рисует сразу своим цветом.
const OBL_K = 0.6, CYL_R = 0.06, CYL_L = 0.3, BALL_R = 0.1, SLICE_RX = 0.2, SLICE_RY = 0.12;
const mM = (A, B) => [A[0] * B[0] + A[2] * B[1], A[1] * B[0] + A[3] * B[1], A[0] * B[2] + A[2] * B[3], A[1] * B[2] + A[3] * B[3],
  A[0] * B[4] + A[2] * B[5] + A[4], A[1] * B[4] + A[3] * B[5] + A[5]];

// цилиндр (кусок руки, ноги, шеи): от (0, 0) до (0, −CYL_L), радиус CYL_R; концы прячут шары суставов, поэтому контур —
// только по длинным краям и растягивать по длине можно; тень к краям поровну (направленный свет — в Unity)
function paintCyl(g, kind, col = "#ffffff"){
  const r = CYL_R, L = CYL_L, base = kind === "mail" ? MAIL : kind === "plate" ? STEEL : col;
  const gr = g.createLinearGradient(-r, 0, r, 0);
  gr.addColorStop(0, shade(base, -0.3)); gr.addColorStop(0.5, base); gr.addColorStop(1, shade(base, -0.3));
  g.fillStyle = gr; g.fillRect(-r, -L, 2 * r, L);
  g.save(); g.beginPath(); g.rect(-r, -L, 2 * r, L); g.clip();
  if(kind === "mail") rings(g, -r, -L, r, 0, MAIL_D);
  else if(kind === "plate"){
    g.fillStyle = SHINE; g.fillRect(-0.012, -L, 0.02, L);
    g.strokeStyle = STEEL_D; g.lineWidth = 0.008; g.beginPath(); for(let y = -0.06; y > -L; y -= 0.075){ g.moveTo(-r, y); g.lineTo(r, y); } g.stroke();
  } else if(kind === "leather"){
    g.setLineDash([0.015, 0.012]); g.strokeStyle = shade(base, -0.45); g.lineWidth = 0.007;
    g.beginPath(); g.moveTo(0.025, -L); g.lineTo(0.025, 0); g.stroke(); g.setLineDash([]);
  } else if(kind === "cloth"){
    g.strokeStyle = shade(base, -0.22); g.lineWidth = 0.008; g.beginPath();
    for(const y of [-0.07, -0.16, -0.24]){ g.moveTo(-r, y); g.quadraticCurveTo(0, y + 0.02, r * 0.4, y - 0.01); } g.stroke();
  }
  g.restore();
  g.beginPath(); g.moveTo(-r, -L); g.lineTo(-r, 0); g.moveTo(r, -L); g.lineTo(r, 0); g.strokeStyle = INK; g.lineWidth = 0.02; g.stroke();
}
// шар (сустав, голова, кисть) радиусом BALL_R
function paintBall(g, kind, col = "#ffffff"){
  const base = kind === "steel" ? STEEL : kind === "mail" ? MAIL : col;
  g.beginPath(); g.arc(0, 0, BALL_R, 0, 6.283); g.fillStyle = vol(g, base, 0, -0.02, BALL_R * 1.15, kind === "steel" ? 1.3 : 0.9); g.fill();
  if(kind === "mail"){ g.save(); g.clip(); rings(g, -BALL_R, -BALL_R, BALL_R, BALL_R, MAIL_D); g.restore(); g.beginPath(); g.arc(0, 0, BALL_R, 0, 6.283); }
  edge(g, 0.022);
  if(kind === "steel") ell(g, -0.025, -0.03, 0.022, 0.024, SHINE, 0, false);
}
// срез корпуса — эллипс SLICE_RX × SLICE_RY. Срезы лежат стопкой, у нижних виден только край к зрителю — по краю и
// рисуется: kind cloth — стёганка (простёжка), brig — бригантина (заклёпки), mail, plate, belt — пояс с пряжкой спереди,
// tab — сюрко: поле и герб по секторам (paint) и поясам по высоте (band: u — грудь, l — низ)
function paintSlice(g, kind, col = "#ffffff", paint = "plain", band = "u", c2 = DEVICE[0]){
  const rx = SLICE_RX, ry = SLICE_RY, path = () => { g.beginPath(); g.ellipse(0, 0, rx, ry, 0, 0, 6.283); };
  const base = kind === "mail" ? MAIL : kind === "plate" ? STEEL : kind === "belt" ? "#5a4028" : col;
  path(); g.fillStyle = vol(g, base, 0, 0, rx, kind === "plate" ? 1.2 : 0.7); g.fill();
  g.save(); path(); g.clip();
  if(kind === "mail") rings(g, -rx, -ry, rx, ry, MAIL_D);
  else if(kind === "plate"){ g.strokeStyle = SHINE; g.lineWidth = 0.015; g.beginPath(); g.moveTo(0, -ry); g.lineTo(0, -ry * 0.4); g.stroke(); }
  else if(kind === "brig"){ for(let a = 0; a < 6.283; a += 0.35) ell(g, Math.cos(a) * rx * 0.86, Math.sin(a) * ry * 0.8, 0.011, 0.011, "#c9c3b4", 0, false); }
  else if(kind === "cloth"){
    g.strokeStyle = shade(base, -0.28); g.lineWidth = 0.008; g.beginPath();
    for(let a = 0; a < 6.283; a += 0.5){ g.moveTo(Math.cos(a) * rx * 0.75, Math.sin(a) * ry * 0.75); g.lineTo(Math.cos(a) * rx, Math.sin(a) * ry); } g.stroke();
  } else if(kind === "belt"){ g.fillStyle = "#c9a86a"; g.fillRect(-0.03, -ry - 0.01, 0.06, 0.04); }
  else if(kind === "tab"){
    g.fillStyle = c2;
    if(paint === "halves") g.fillRect(0, -1, 1, 2);
    else if(paint === "quarters") band === "u" ? g.fillRect(0, -1, 1, 2) : g.fillRect(-1, -1, 1, 2);
    else if(paint === "cross") band === "u" ? g.fillRect(-1, -1, 2, 2) : g.fillRect(-0.035, -1, 0.07, 2);
    else if(paint === "stripe"){ if(band === "u") g.fillRect(-1, -1, 2, 2); }
    else if(paint === "chevron"){ if(band === "l") g.fillRect(-0.06, -1, 0.12, 2); }
  }
  g.restore();
  // контур — тёмный своего цвета: срезы стопкой, чёрный дал бы рубчики
  if(kind !== "tab"){ path(); g.strokeStyle = shade(base, -0.3); g.lineWidth = 0.008; g.stroke(); }
}
// лицо или забрало — в вертикальной плоскости перед шаром головы
function paintFace(g, kind){
  if(kind === "great"){
    g.beginPath(); g.ellipse(0, 0, 0.075, 0.08, 0, 0, 6.283); g.fillStyle = vol(g, "#8f9498", 0, -0.02, 0.09, 1.2); g.fill(); edge(g, 0.018);
    g.fillStyle = INK; g.fillRect(-0.06, -0.022, 0.12, 0.016); g.fillRect(-0.004, -0.004, 0.008, 0.06);
    for(const x of [-0.035, 0.035]) for(const y of [0.025, 0.045]) ell(g, x, y, 0.005, 0.005, INK, 0, false);
    return;
  }
  if(kind === "bascinet"){
    g.beginPath(); g.moveTo(-0.06, -0.05); g.quadraticCurveTo(0, -0.08, 0.06, -0.05); g.lineTo(0.03, 0.06); g.lineTo(0, 0.09); g.lineTo(-0.03, 0.06); g.closePath();
    g.fillStyle = vol(g, STEEL_D, 0, -0.02, 0.09, 1.2); g.fill(); edge(g, 0.018); g.fillStyle = INK; g.fillRect(-0.045, -0.025, 0.09, 0.012);
    return;
  }
  // открытое: глаза, брови, нос, рот
  g.beginPath(); g.ellipse(0, 0.005, 0.068, 0.078, 0, 0, 6.283); g.fillStyle = vol(g, SKIN, 0, -0.01, 0.08, 0.6); g.fill();
  for(const x of [-0.026, 0.026]) ell(g, x, -0.012, 0.01, 0.008, INK, 0, false);
  g.strokeStyle = shade(SKIN, -0.45); g.lineWidth = 0.009; g.beginPath();
  g.moveTo(-0.042, -0.032); g.lineTo(-0.013, -0.028); g.moveTo(0.013, -0.028); g.lineTo(0.042, -0.032);
  g.moveTo(0, -0.004); g.lineTo(0.005, 0.022); g.moveTo(-0.018, 0.044); g.quadraticCurveTo(0, 0.05, 0.018, 0.044); g.stroke();
}
// пара ног издали (стоймя, к зрителю; −y спрайта — вверх): белые штаны (Unity умножает на цвет) и тёмные сапоги снизу
function paintLegs2(g, col = "#ffffff"){
  for(const x of [-0.085, 0.085]){
    g.beginPath(); g.rect(x - 0.06, -0.43, 0.12, 0.86); g.fillStyle = col; g.fill();
    g.fillStyle = shade(col, -0.75); g.fillRect(x - 0.065, 0.2, 0.13, 0.23);
    g.beginPath(); g.rect(x - 0.06, -0.43, 0.12, 0.86); g.strokeStyle = INK; g.lineWidth = 0.02; g.stroke();
  }
}
// конь стоймя (В17): срез туловища — вытянутый эллипс 0,25 × 0,85 (вперёд — −y); coat — белый (Unity умножает на масть),
// bard — попона цвета стороны с полосой герба вдоль хребта и каймой
const HSL_RX = 0.25, HSL_RY = 0.85;
function paintHSlice(g, kind, col = "#ffffff", c2 = DEVICE[0]){
  const path = () => { g.beginPath(); g.ellipse(0, 0, HSL_RX, HSL_RY, 0, 0, 6.283); };
  path(); g.fillStyle = vol(g, col, 0, 0, HSL_RY * 0.6, 0.6); g.fill();
  if(kind === "bard"){ g.save(); path(); g.clip(); g.fillStyle = c2; g.fillRect(-0.05, -1, 0.1, 2); g.strokeStyle = c2; g.lineWidth = 0.04; path(); g.stroke(); g.restore(); }
  else { path(); g.strokeStyle = shade(col, -0.3); g.lineWidth = 0.01; g.stroke(); }
}
// седло сверху: чепрак (цвет стороны или своё сукно у полной барды), кожаное седло с высокой лукой
function paintSaddle(g, kind, col){
  if(kind !== "none"){ g.beginPath(); g.rect(-0.27, -0.3, 0.54, 0.62); g.fillStyle = vol(g, col, 0, 0, 0.4, 0.6); g.fill(); edge(g, 0.025); }
  g.beginPath(); g.ellipse(0, 0, 0.17, 0.26, 0, 0, 6.283); g.fillStyle = vol(g, "#6a4a2e", 0, -0.04, 0.26, 0.9); g.fill(); edge(g, 0.025);
  g.beginPath(); g.ellipse(0, 0.17, 0.15, 0.06, 0, 0, 6.283); g.fillStyle = "#4e3520"; g.fill(); edge(g, 0.02);   // задняя лука
  g.beginPath(); g.ellipse(0, -0.2, 0.1, 0.05, 0, 0, 6.283); g.fillStyle = "#4e3520"; g.fill(); edge(g, 0.02);     // передняя
}
// изнанка щита: доски и ремни (лицом от зрителя)
function paintShieldBack(g, shape){
  const wood = shape === "buckler" ? STEEL : "#7a5a36";
  shieldPath(g, shape); g.fillStyle = vol(g, wood, 0, 0, 0.3, 0.8); g.fill();
  g.save(); g.clip();
  if(shape !== "buckler"){ g.strokeStyle = "rgba(40,26,12,.5)"; g.lineWidth = 0.012; g.beginPath(); for(let x = -0.2; x < 0.3; x += 0.08){ g.moveTo(x, -0.3); g.lineTo(x, 0.3); } g.stroke(); }
  g.fillStyle = "#3e2a18"; g.fillRect(-0.16, -0.035, 0.32, 0.03); g.fillRect(-0.16, 0.05, 0.32, 0.03);
  g.restore(); shieldPath(g, shape); edge(g);
}
// плащ сзади, в вертикальной плоскости: от плеч (−y спрайта — вверх) до колен, складки
function paintCapeV(g, col){
  g.beginPath(); g.moveTo(-0.17, -0.42); g.lineTo(0.17, -0.42); g.lineTo(0.24, 0.4); g.quadraticCurveTo(0, 0.45, -0.24, 0.4); g.closePath();
  g.fillStyle = vol(g, col, 0, -0.1, 0.5, 0.6); g.fill(); edge(g, 0.025);
  g.strokeStyle = shade(col, -0.35); g.lineWidth = 0.012; g.beginPath(); for(const x of [-0.1, 0, 0.1]){ g.moveTo(x * 0.8, -0.35); g.lineTo(x * 1.3, 0.4); } g.stroke();
}
// павеза за спиной, в вертикальной плоскости: поле — сторона, полоса — герб
function paintPaviseV(g, col, c2){
  g.beginPath(); g.rect(-0.28, -0.45, 0.56, 0.9); g.fillStyle = vol(g, col, 0, -0.1, 0.55, 0.6); g.fill();
  g.fillStyle = c2; g.fillRect(-0.05, -0.45, 0.1, 0.9);
  g.beginPath(); g.rect(-0.28, -0.45, 0.56, 0.9); edge(g);
}

// спрайты частей (кэш по виду и цвету)
const cylSpr = (kind, col) => spr("cy" + kind + col, [-CYL_R - 0.015, -CYL_L, CYL_R + 0.015, 0], g => paintCyl(g, kind, col));
const ballSpr = (kind, col) => spr("ba" + kind + col, [-BALL_R - 0.015, -BALL_R - 0.015, BALL_R + 0.015, BALL_R + 0.015], g => paintBall(g, kind, col));
const sliceSpr = (kind, col, paint, band, c2) => spr("sl" + kind + col + paint + band + c2, [-SLICE_RX - 0.015, -SLICE_RY - 0.03, SLICE_RX + 0.015, SLICE_RY + 0.015], g => paintSlice(g, kind, col, paint, band, c2));
const faceSpr = kind => spr("fc" + kind, [-0.09, -0.1, 0.09, 0.1], g => paintFace(g, kind));
const topSpr = k => spr("tp" + [k.cloth, k.armour, k.leather].join(), [-0.31, -0.16, 0.31, 0.2], g => paintBody(g, {cloth: k.cloth, armour: k.armour, leather: k.leather, back: "none", helm: "none"}));
const tabTopSpr = k => spr("tt" + k.tabard + k.col + k.c2, [-0.22, -0.15, 0.22, 0.2], g => paintTabard(g, k.tabard, k.col, k.c2));
const helmSpr = k => spr("hm" + k.helm + k.helmCol + (k.crest || ""), [-0.2, -0.24, 0.2, 0.29], g => paintHead(g, k));
const shieldBackSpr = shape => spr("sb" + shape, [-0.25, -0.3, 0.25, 0.31], g => paintShieldBack(g, shape));
const capeVSpr = col => spr("cv" + col, [-0.26, -0.44, 0.26, 0.47], g => paintCapeV(g, col));
const paviseVSpr = (col, c2) => spr("pv" + col + c2, [-0.3, -0.47, 0.3, 0.47], g => paintPaviseV(g, col, c2));

// Фигурка: части в осях карты (боец в x, y, курс face). o — поза, как у drawMen (вид сверху): W, Sh — оружие и щит,
// step — шаг, bowSt, xb — лук и арбалет, raise — щит над головой
function figure(k, x, y, face, o){
  const F = [], K = OBL_K, cf = Math.cos(face), sf = Math.sin(face);
  const W3 = p => [x + p[0] * cf - p[1] * sf, y + p[0] * sf + p[1] * cf, p[2]];
  const pr = P => [P[0], P[1] - K * P[2]], dk = P => P[1] * K + P[2];
  const push = (sp, M, key) => F.push({sp, M, key});
  const cyl = (a, b, r, kind, col, bias = 0) => {
    const A = W3(a), B = W3(b), sa = pr(A), sb = pr(B), dx = sb[0] - sa[0], dy = sb[1] - sa[1], L = Math.hypot(dx, dy);
    if(L > 1e-4) push(cylSpr(kind, col), mS(mR([1, 0, 0, 1, sa[0], sa[1]], Math.atan2(dx, -dy)), r / CYL_R, L / CYL_L), (dk(A) + dk(B)) / 2 + bias);
  };
  const ball = (c, r, kind, col, bias = 0) => { const C = W3(c), s = pr(C); push(ballSpr(kind, col), [r / BALL_R, 0, 0, r / BALL_R, s[0], s[1]], dk(C) + bias); };
  const flat = (z, T, sp, bias = 0) => push(sp, mP(mR([1, 0, 0, 1, x, y - K * z], face), T), dk(W3([T[0], T[1], z])) + bias);
  const vert = (c, u, sp, sx = 1, bias = 0) => {
    const C = W3(c), s = pr(C), ux = u[0] * cf - u[1] * sf, uy = u[0] * sf + u[1] * cf;
    push(sp, [ux * sx, uy * sx, 0, K, s[0], s[1]], dk(C) + bias);
  };
  const wpn = (g3, d3, sp, bias = 0) => {
    const G = W3(g3), sg = pr(G), dx = d3[0] * cf - d3[1] * sf, dy = d3[0] * sf + d3[1] * cf, px = dx, py = dy - K * d3[2];
    push(sp, mS(mR([1, 0, 0, 1, sg[0], sg[1]], Math.atan2(px, -py)), 1, Math.hypot(px, py)), dk(G) + bias);
  };
  const metal = k.armour === "mail" || k.armour === "plate";
  const sleeveK = k.armour === "mail" ? "mail" : k.armour === "plate" ? "plate" : k.armour === "leather" ? "leather" : "cloth";
  const sleeveC = k.armour === "leather" ? k.leather : k.cloth;
  const legK = k.armour === "plate" ? "plate" : k.armour === "mail" && (k.look === "sword" || k.look === "lance" || k.look === "barded") ? "mail" : "cloth";
  const legC = mix(k.cloth, "#3e3328", 0.55);
  // ноги: ступни, сапоги, голени, колени, бёдра; на ходу — шаг
  const st = o.step || 0;
  for(const s of [-1, 1]){
    const fx = s * 0.09, fy = -0.02 + s * 0.13 * st, hip = [s * 0.085, 0.0, 0.86], ank = [fx, fy + 0.02, 0.1];
    const knee = [(hip[0] + ank[0]) / 2, (hip[1] + ank[1]) / 2 - 0.05, 0.48];
    flat(0.01, [fx, fy, 0, 1, 1], bootSpr(), -0.3);
    cyl([fx, fy + 0.02, 0.04], [ank[0], ank[1], 0.3], 0.063, "leather", "#4a3828");
    cyl([ank[0], ank[1], 0.28], knee, 0.058, legK, legC);
    ball(knee, 0.064, legK === "cloth" ? "cloth" : legK === "plate" ? "steel" : "mail", legC);
    cyl(knee, hip, 0.07, legK, legC);
  }
  // корпус: срезы от низа полы до плеч (у сюрко пола длиннее), пояс, сверху — плечи вида сверху и сюрко
  const tab = !!k.tabard, z0 = tab ? 0.62 : 0.76;
  for(let z = z0; z <= 1.38; z += 0.055){
    const w = z < 0.95 ? 0.17 + (0.95 - z) * 0.12 : z < 1.2 ? 0.165 + (z - 0.95) * 0.14 : 0.2 + (z - 1.2) * 0.06;
    const d = w * 0.6, T = [0, 0.01, 0, w / SLICE_RX, d / SLICE_RY];
    const belt = Math.abs(z - 0.94) < 0.028;
    const sp = belt ? sliceSpr("belt", "#5a4028", "plain", "u", "") : tab ? sliceSpr("tab", k.col, k.tabard, z < 1.1 ? "l" : "u", k.c2)
      : sliceSpr(k.armour === "leather" ? "brig" : metal ? k.armour : "cloth", k.cloth, "plain", "u", "");
    flat(z, T, sp);
  }
  flat(1.41, [0, 0, 0, 0.92, 0.92], topSpr(k));
  if(tab) flat(1.415, [0, 0, 0, 0.92, 0.92], tabTopSpr(k));
  // поклажа за спиной
  if(k.back === "quiver"){ cyl([0.1, 0.13, 0.82], [0.15, 0.2, 1.48], 0.048, "leather", "#6e4c2e", -0.05); ball([0.155, 0.205, 1.52], 0.045, "cloth", "#efe9dc", -0.05); }
  else if(k.back === "pavise") vert([0, 0.19, 1.0], [1, 0], paviseVSpr(k.col, k.c2), 1, -0.1);
  else if(k.back === "cape") vert([0, 0.165, 0.98], [1, 0], capeVSpr(k.backCol), 1, -0.08);
  else if(k.back === "roll") cyl([-0.17, 0.13, 1.44], [0.17, 0.13, 1.44], 0.055, "cloth", "#b09a72", 0.02);
  else if(k.back === "bag") ball([0.09, 0.17, 1.0], 0.08, "cloth", LEATHER[1], -0.05);
  // руки: плечо → локоть → кисть; кисти — где держат (handsOf), по высоте — у пояса, занесённое оружие — выше
  const W = o.W, sh = o.Sh, raise = o.raise;
  const [hr, hl, showL] = o.hands || handsOf(k, W, sh, o.bowSt || 0, o.xb || 0);
  const oneHand = W && !THRUST.has(k.weapon), up = W ? Math.min(1, Math.abs(W[4])) : 1;
  const hz = k.weapon === "bow" ? 1.3 : k.weapon === "crossbow" ? 1.2 : oneHand ? 1.05 + 0.4 * (1 - up) : 1.03;
  const dir = W ? [Math.sin(W[2]) * up, -Math.cos(W[2]) * up, Math.sqrt(Math.max(0, 1 - up * up))] : null;
  const H3r = hr ? [hr[0], hr[1], hz] : null;
  let H3l = hl ? [hl[0], hl[1], raise ? 1.75 : sh ? 1.08 : hz] : null;
  if(H3l && H3r && W && !sh && THRUST.has(k.weapon)) H3l = [H3r[0] + dir[0] * 0.28, H3r[1] + dir[1] * 0.28, H3r[2] + dir[2] * 0.28];
  for(const [s, H] of [[1, H3r || [0.235, -0.02, 0.95]], [-1, H3l || [-0.235, -0.02, 0.95]]]){   // пустая рука — опущена
    const S = [s * 0.205, 0.01, 1.36], E = [(S[0] + H[0]) / 2 + s * 0.06, (S[1] + H[1]) / 2 + 0.06, (S[2] + H[2]) / 2 - 0.12];
    cyl(S, E, 0.058, sleeveK, sleeveC); ball(E, 0.058, sleeveK === "plate" ? "steel" : sleeveK === "mail" ? "mail" : "cloth", sleeveC);
    cyl(E, H, 0.052, sleeveK, sleeveC);
  }
  // кисти — поверх рукояти; левая со щитом — за щитом
  const hk = handKind(k), hc = HAND_COL[hk];
  if(H3r) ball(H3r, 0.046, hk === "plate" ? "steel" : "cloth", hc, 0.03);
  if(H3l && (showL || !sh)) ball(H3l, 0.046, hk === "plate" ? "steel" : "cloth", hc, 0.03);
  // шея, голова, лицо или забрало, шлем
  cyl([0, 0.0, 1.36], [0, -0.015, 1.5], 0.048, metal ? "mail" : "cloth", SKIN);
  const closed = k.helm === "great" || k.helm === "bascinet";
  ball([0, -0.03, 1.6], 0.112, closed ? "steel" : "cloth", closed ? STEEL : k.helm === "hood" ? k.helmCol : SKIN);
  if(-cf > -0.25) vert([0, -0.122, 1.59], [1, 0], faceSpr(closed ? k.helm : "open"), 1, 0.25);
  flat(1.675, [0, -0.03, 0, 1, 1], helmSpr(k), 0.3);
  // оружие: от кисти по направлению позы (сверху: поворот W[2]; масштаб вдоль W[4] — насколько наклонено к нам — вверх)
  if(k.weapon === "bow") flat(hz, [0, 0, 0, 1, 1], bowSpr(o.bowSt || 0), 0.02);
  else if(k.weapon === "crossbow") flat(hz, [0, 0, 0, 1, 1], xbowSpr(o.xb || 0), 0.02);
  else if(W && H3r) wpn(H3r, dir, weapSpr(k.weapon, k.col), 0.01);
  // щит: на левой руке — в вертикальной плоскости, лицом вперёд (повёрнут на Sh[2]); над головой — плашмя
  if(sh){
    if(raise) flat(1.95, [sh[0], sh[1], sh[2], 1, 1], shieldSpr(k.shield), 0.5);
    else {
      const u = [Math.cos(sh[2]), Math.sin(sh[2])], n = [Math.sin(sh[2]), -Math.cos(sh[2])];
      const ny = n[0] * sf + n[1] * cf;   // нормаль в осях карты: к зрителю — y > 0
      vert([H3l[0] + n[0] * 0.05, H3l[1] + n[1] * 0.05, H3l[2] + 0.02], u, ny > 0 ? shieldSpr(k.shield) : shieldBackSpr(k.shield.shape), Math.abs(sh[3]), 0.06);
    }
  }
  return F;
}
function drawFigure(F, B){ F.sort((a, b) => a.key - b.key); for(const f of F) put(f.sp, mM(B, f.M)); }


// ── Удары (В8): доля круга удара p 0…1 → смещение или поворот оружия ──
function thrustOff(p){   // колющие: замах назад, выпад, держит, возврат
  if(p < 0.3) return 0.12 * ease(p / 0.3);
  if(p < 0.42) return 0.12 - 0.62 * ease((p - 0.3) / 0.12);
  if(p < 0.5) return -0.5;
  if(p < 0.8) return -0.5 + 0.5 * ease((p - 0.5) / 0.3);
  return 0;
}
function swingAng(p){   // рубящие: замах вправо-вверх, удар поперёк, возврат; [поворот, масштаб вдоль]
  if(p < 0.35){ const e = ease(p / 0.35); return [0.3 + 1.1 * e, 0.65 - 0.25 * e]; }
  if(p < 0.47){ const e = (p - 0.35) / 0.12; return [1.4 - 2.3 * e, 0.4 + 0.6 * e]; }
  if(p < 0.85){ const e = ease((p - 0.47) / 0.38); return [-0.9 + 1.2 * e, 1 - 0.35 * e]; }
  return [0.3, 0.65];
}
function chopPose(p){   // сверху (В13): замах вверх — оружие к нам, видно коротким; удар вниз-вперёд; возврат; [сдвиг вперёд, масштаб вдоль]
  if(p < 0.35){ const e = ease(p / 0.35); return [-0.08 + 0.1 * e, 0.65 - 0.5 * e]; }
  if(p < 0.47){ const e = (p - 0.35) / 0.12; return [0.02 - 0.2 * e, 0.15 + 0.95 * e]; }
  if(p < 0.85){ const e = ease((p - 0.47) / 0.38); return [-0.18 + 0.1 * e, 1.1 - 0.45 * e]; }
  return [-0.08, 0.65];
}
const THRUST = new Set(["spear", "fork", "pike", "lance"]);
// бегущий оглядывается через плечо (В13): почти половина — раз в 2–4 с на полсекунды, плечи поворачиваются до 50°
function lookBack(t, s){
  if(hash(s, 43) > 0.45) return 0;
  const u = frac(t / (2 + 2 * hash(s, 44)) + hash(s, 45));
  return u < 0.18 ? (hash(s, 46) < 0.5 ? -0.9 : 0.9) * Math.sin(u / 0.18 * Math.PI) : 0;
}
// взгляд по сторонам: раз в несколько секунд боец поворачивает голову и плечи
function glance(t, s){
  const slot = Math.floor(t / 3 + hash(s, 9) * 3), r = hash(s * 7 + slot, 11);
  if(r > 0.35) return 0;
  return (hash(s + slot, 12) - 0.5) * 0.9 * Math.sin(frac(t / 3 + hash(s, 9) * 3) * Math.PI);
}

// ── Рукопашная и обстрел по кадру: кто с кем дерётся, кого накрывают стрелы ──
let MEL = new Map(), FIRE = new Set(), MEN_FRAME = -1, MEN_SCENE = null;
function prepMenScene(){
  if(S._menPrep) return;
  S._menPrep = true;
  if(!S.arrows) return;
  const A = S.arrows;
  S._arrU = S.units.map(() => []); S._maxFly = 0; S._stuck = [];
  A.forEach((a, i) => { S._arrU[a[11]].push(i); S._maxFly = Math.max(S._maxFly, a[7] - a[0]); if(a[12] <= 2) S._stuck.push(i); });
  S._stuck.sort((i, j) => A[i][7] - A[j][7]);
}
function lowerT0(A, idx, t){   // первая стрела (по списку idx или по всем) с вылетом не раньше t
  let lo = 0, hi = idx ? idx.length : A.length;
  while(lo < hi){ const m = (lo + hi) >> 1; if((idx ? A[idx[m]] : A[m])[0] < t) lo = m + 1; else hi = m; }
  return lo;
}
function prepFrameMen(){
  const fi = Math.min(Math.round(frame), S.frames.length - 1);
  if(fi === MEN_FRAME && S === MEN_SCENE) return;
  MEN_FRAME = fi; MEN_SCENE = S; MEL = new Map(); FIRE = new Set();
  prepMenScene();
  const F = S.frames[fi], pairs = S.fights ? S.fights[Math.min(fi, S.fights.length - 1)] : null;
  if(pairs) for(let q = 0; q < pairs.length; q += 2){ engage(F, pairs[q], pairs[q + 1]); engage(F, pairs[q + 1], pairs[q]); }
  if(!S.arrows) return;
  // фигурки, куда через секунду-полторы упадут стрелы: там поднимают щиты
  const t = fi * S.dt, A = S.arrows, grid = new Map();
  S.units.forEach((u, ui) => {
    const f = F[ui]; if(!f) return;
    for(let k = 0; 5 + 2 * k < f.length; k++){
      if(f[4 + 2 * k] == null) continue;   // тело выбыло
      const key = Math.floor(f[4 + 2 * k] / 10) * 4096 + Math.floor(f[5 + 2 * k] / 10);
      let l = grid.get(key); if(!l) grid.set(key, l = []); l.push(ui, k, f[4 + 2 * k], f[5 + 2 * k]);
    }
  });
  for(let i = lowerT0(A, null, t - S._maxFly); i < A.length && A[i][0] <= t; i++){
    const a = A[i]; if(a[7] <= t || a[7] - t > 1.5) continue;
    const cx = Math.floor(a[8] / 10), cy = Math.floor(a[9] / 10);
    for(let dx = -1; dx <= 1; dx++) for(let dy = -1; dy <= 1; dy++){
      const l = grid.get((cx + dx) * 4096 + cy + dy); if(!l) continue;
      for(let q = 0; q < l.length; q += 4) if(Math.abs(l[q + 2] - a[8]) < 6 && Math.abs(l[q + 3] - a[9]) < 6) FIRE.add(l[q] * 100000 + l[q + 1]);
    }
  }
}
function engage(F, a, b){
  const ua = S.units[a], ub = S.units[b], fa = F[a], fb = F[b];
  if(!fa || !fb) return;
  let m = MEL.get(a); if(!m) MEL.set(a, m = new Map());
  const db = (ub.figs[0] ? ub.figs[0][1] : 5) / 2;
  for(let k = 0; 5 + 2 * k < fa.length; k++){
    const x = fa[4 + 2 * k], y = fa[5 + 2 * k];
    if(x == null) continue;
    let best = 1e18, bx = 0, by = 0;
    for(let j = 0; 5 + 2 * j < fb.length; j++){ if(fb[4 + 2 * j] == null) continue; const dx = fb[4 + 2 * j] - x, dy = fb[5 + 2 * j] - y, d = dx * dx + dy * dy; if(d < best){ best = d; bx = dx; by = dy; } }
    const reach = (ua.figs[k] ? ua.figs[k][1] : 5) / 2 + db + 3;
    if(best < reach * reach) m.set(k, Math.atan2(by, bx));
  }
}

// ── Бойцы поштучно (В10): у каждого своё место, к нему идут шагом, а не прыгают ──
// Тела (фигурки по 10 бойцов) двигает движок; бойцов внутри рисунок ведёт сам — один раз на сцену, по всем кадрам
// с начала (поэтому ползунок можно двигать куда угодно). У бойца — место в своей фигурке: ряды спереди назад,
// неполный задний ряд — по середине. К месту он идёт не быстрее 1,6 нормы отряда. Павший — тот, кто ближе всех к
// месту гибели из данных движка: тело ложится там, где он стоял. На его место шагает стоящий позади, освободившееся
// в заднем ряду занимает последний. Тело пропало — его бойцы идут на свободные места в ближайших фигурках.
// Курс фигурки — куда она идёт (колонна по мосту смотрит вдоль моста); стоит — курс отряда; отряд, что пятится
// или шагает вбок (Г54), не разворачивается. Курс тела от движка (охват, бегство — heads) главнее.
let AG = [], HEADS = [], DEADS = [], AG_SCENE = null;
const angD = (a, b) => Math.atan2(Math.sin(a - b), Math.cos(a - b));   // a − b в (−π, π]
const turnTo = (a, b, max) => { const d = angD(b, a); return a + Math.max(-max, Math.min(max, d)); };
function agScene(){ if(AG_SCENE !== S){ AG_SCENE = S; AG = []; HEADS = []; DEADS = []; } }
function headsOf(ui){
  agScene();
  if(HEADS[ui]) return HEADS[ui];
  const F = S.frames, N = F.length;
  let nb = 0; for(const fr of F) if(fr[ui]) nb = Math.max(nb, (fr[ui].length - 4) >> 1);
  const H = new Float32Array(N * nb).fill(NaN), hd = new Map(), maxTurn = 2.0 * S.dt;   // поворот до ~115°/с
  // по движению — только на марше: идёт весь отряд и сама фигурка, не медленнее трети нормы; толкотня и перестроение
  // на месте курс не трогают — иначе стоящие фигурки крутятся
  const u = S.units[ui], go = Math.max(1.5, 0.33 * (u.norm || 100) / (S.turnSec || 15));
  if(S.heads) S.heads.forEach((list, f) => { if(list) for(const [u2, id, a] of list) if(u2 === ui) hd.set(f * 65536 + id, a * Math.PI / 180); });
  // отряд сложен в колонну (узость, Г59): тела вытянуты вдоль курса сильнее, чем поперёк. Только тогда фигурки смотрят,
  // куда идут; в линии все держат курс отряда — иначе на манёврах фигурки расходятся «ёлочкой»
  const folded = new Uint8Array(N);
  for(let f = 0; f < N; f++){
    const p = F[f][ui]; if(!p) continue;
    const uh = p[2] * Math.PI / 180, c = Math.cos(uh), s = Math.sin(uh);
    let a0 = 1e9, a1 = -1e9, b0 = 1e9, b1 = -1e9;
    for(let k = 0; k < nb; k++){
      const x = p[4 + 2 * k], y = p[5 + 2 * k]; if(x == null) continue;
      const lat = (x - p[0]) * c + (y - p[1]) * s, lon = -(x - p[0]) * s + (y - p[1]) * c;
      a0 = Math.min(a0, lat); a1 = Math.max(a1, lat); b0 = Math.min(b0, lon); b1 = Math.max(b1, lon);
    }
    folded[f] = b1 - b0 > (a1 - a0) * 1.2 && b1 - b0 > 15 ? 1 : 0;
  }
  for(let k = 0; k < nb; k++){
    let cur = NaN;
    for(let f = 0; f < N; f++){
      const p = F[f][ui];
      if(!p || p[4 + 2 * k] == null){ cur = NaN; continue; }
      const uh = p[2] * Math.PI / 180;
      let want = hd.get(f * 65536 + k);
      if(want === undefined){
        want = uh;
        const fa = Math.max(0, f - 4), fb = Math.min(N - 1, f + 4), a = F[fa][ui], b = F[fb][ui];
        if(fb > fa && a && b && a[4 + 2 * k] != null && b[4 + 2 * k] != null){
          const dt = (fb - fa) * S.dt, dx = b[4 + 2 * k] - a[4 + 2 * k], dy = b[5 + 2 * k] - a[5 + 2 * k];
          const ux = b[0] - a[0], uy = b[1] - a[1], march = Math.sqrt(ux * ux + uy * uy) / dt > go && Math.abs(angD(Math.atan2(ux, -uy), uh)) < 1.05;
          if(folded[f] && march && Math.sqrt(dx * dx + dy * dy) / dt > go){ const mh = Math.atan2(dx, -dy); if(Math.abs(angD(mh, uh)) < 1.75) want = mh; }
        }
      }
      // «кругом» (Г52): разворот больше 135° — сразу; бойцы не обходят фигурку, а поворачиваются на месте (см. buildAgents)
      cur = isNaN(cur) || Math.abs(angD(want, cur)) > 2.35 ? want : turnTo(cur, want, maxTurn);
      H[f * nb + k] = cur;
    }
  }
  return HEADS[ui] = {H, nb};
}
// курс фигурки сейчас, рад (для плашек издали); undefined — тела нет
function figHead(ui, k){
  const {H, nb} = headsOf(ui);
  if(k >= nb) return undefined;
  const N = S.frames.length, fi = Math.min(Math.floor(frame), N - 1), f1 = Math.min(fi + 1, N - 1), a = H[fi * nb + k], b = H[f1 * nb + k];
  if(isNaN(a)) return undefined;
  return isNaN(b) ? a : a + angD(b, a) * (frame - fi);
}
// Состояние отряда по кадрам (БД4): 0 в строю, 1 бежит, 2 ушёл с поля, 3 бежит, но приказ «сплотить» ждёт, 4 сплотился
function stateAt(ui, fr){
  const L = S.states && S.states[ui];
  if(!L) return 0;
  let st = L[1];
  for(let i = 0; i < L.length && L[i] <= fr; i += 2) st = L[i + 1];
  return st;
}
function stateSince(ui, fr){   // с какого кадра нынешнее состояние
  const L = S.states && S.states[ui];
  if(!L) return 0;
  let since = 0;
  for(let i = 0; i < L.length && L[i] <= fr; i += 2) since = L[i];
  return since;
}
const fleeing = st => st === 1 || st === 3;
// Древки к бою (В13): отряд в схватке или край врага ближе 30 м — опускают за 1,2 с; иначе поднимают за 2 с.
// Отряд — отрезок своего фронта (центр, курс, ширина); зазор — расстояние между отрезками минус полглубины обоих.
let LOW = [], LOW_SCENE = null;
function segDist(ax, ay, bx, by, cx, cy, dx, dy){
  const pt = (px, py, x0, y0, x1, y1) => { const vx = x1 - x0, vy = y1 - y0, L = vx * vx + vy * vy, u = L > 0 ? Math.max(0, Math.min(1, ((px - x0) * vx + (py - y0) * vy) / L)) : 0; return Math.hypot(px - x0 - u * vx, py - y0 - u * vy); };
  return Math.min(pt(ax, ay, cx, cy, dx, dy), pt(bx, by, cx, cy, dx, dy), pt(cx, cy, ax, ay, bx, by), pt(dx, dy, ax, ay, bx, by));
}
function lowerOf(ui){
  if(LOW_SCENE !== S){ LOW_SCENE = S; LOW = []; }
  if(LOW[ui]) return LOW[ui];
  const F = S.frames, N = F.length, L = new Float32Array(N), U = S.units, side = U[ui].faction || 1;
  const seg = (j, p) => { const h = p[2] * Math.PI / 180, c = Math.cos(h) * (U[j].front || 20) / 2, s = Math.sin(h) * (U[j].front || 20) / 2; return [p[0] - c, p[1] - s, p[0] + c, p[1] + s]; };
  let cur = 0, near = 0;   // близость врага — раз в 3 кадра (0,6 с), между ними — последняя
  for(let f = 0; f < N; f++){
    const p = F[f][ui];
    let want = 0;
    if(p){
      if(f % 3 === 0){
        near = 0;
        const a = seg(ui, p);
        for(let j = 0; j < U.length && !near; j++){
          const e = F[f][j]; if(!e || (U[j].faction || 1) === side) continue;
          const b = seg(j, e);
          if(segDist(a[0], a[1], a[2], a[3], b[0], b[1], b[2], b[3]) - ((U[ui].depth || 10) + (U[j].depth || 10)) / 2 < 30) near = 1;
        }
      }
      const pairs = S.fights && S.fights[Math.min(f, S.fights.length - 1)];
      want = near || (pairs && pairs.includes(ui)) ? 1 : 0;
    }
    cur = want ? Math.min(1, cur + S.dt / 1.2) : Math.max(0, cur - S.dt / 2);
    L[f] = cur;
  }
  return LOW[ui] = L;
}
function lowerAt(ui){
  const L = lowerOf(ui), N = S.frames.length, fi = Math.min(Math.floor(frame), N - 1), f1 = Math.min(fi + 1, N - 1);
  return L[fi] + (L[f1] - L[fi]) * (frame - fi);
}
const drops = s => hash(s, 21) < 0.45;   // кто бросает оружие на бегу (В11)
// павшие отряда по кадрам: кадр → [[x, y, номер в S.dead], …]
function deadsOf(ui){
  agScene();
  if(DEADS[ui]) return DEADS[ui];
  const m = new Map();
  if(S.dead) S.dead.forEach((d, i) => { if(d[3] !== ui) return; let l = m.get(d[2]); if(!l) m.set(d[2], l = []); l.push([d[0], d[1], i]); });
  return DEADS[ui] = m;
}
function agentsOf(ui){
  agScene();
  if(AG[ui]) return AG[ui];
  const E = engineMen();   // бойцы движка (В14) — как в игре; данных нет — свои, как раньше
  if(!E || E.failed) return AG[ui] = buildAgents(ui);
  if(!E.ready) return NO_MEN;   // распаковываются — через миг перерисуем
  return AG[ui] = buildFromEngine(ui, E);
}
const NO_MEN = {n: 0, figMen: () => [], deadSeed: new Map(), dropped: []};

// ── Бойцы движка (В14): полигон рисует тех же бойцов, что считает движок и показывает игра ──
// S.men — раз в every кадров по отряду и номеру бойца: тело, смещение от тела в его осях (int16 по 0,05 м; в старых
// полигонах — int8 по 0,1 м), курс к телу (2°);
// разница с прошлым отсчётом побайтно, gzip, base64 (Polygon.cs, MenFrame). Распаковка — один раз на сцену, в фоне;
// между отсчётами смещение от тела плавно меняется, а тело идёт по своим кадрам — бойцы не отстают от фигурок.
let MEN_DEC = null, MEN_DEC_SCENE = null;
function engineMen(){
  if(!S.men || typeof DecompressionStream === "undefined") return null;
  if(MEN_DEC_SCENE === S) return MEN_DEC;
  MEN_DEC_SCENE = S; MEN_DEC = {ready: false};
  const scene = S, M = S.men, mine = MEN_DEC;
  (async () => {
    const bin = Uint8Array.from(atob(M.z), c => c.charCodeAt(0));
    const raw = new Uint8Array(await new Response(new Blob([bin]).stream().pipeThrough(new DecompressionStream("gzip"))).arrayBuffer());
    const B = M.bytes || 5, U = M.n.length, per = M.n.map(n => B * n);
    const out = M.n.map(n => new Uint8Array(M.frames * B * n));   // отряд → [отсчёт × боец × B], уже без разниц
    let o = 0;
    for(let fi = 0; fi < M.frames; fi++)
      for(let u = 0; u < U; u++){
        const dst = out[u], base = fi * per[u], prev = (fi - 1) * per[u];
        for(let b = 0; b < per[u]; b++) dst[base + b] = fi === 0 ? raw[o + b] : (dst[prev + b] + raw[o + b]) & 255;
        o += per[u];
      }
    if(o > raw.length){ mine.failed = true; if(S === scene){ AG_SCENE = null; draw(); } return; }
    Object.assign(mine, {ready: true, every: M.every, bytes: B, n: M.n, frames: M.frames, data: out});
    if(S === scene){ AG_SCENE = null; if(typeof draw === "function") draw(); }
  })().catch(() => { mine.failed = true; });
  return MEN_DEC;
}
function buildFromEngine(ui, E){
  const u = S.units[ui], F = S.frames, N = F.length, Nm = E.n[ui], D = E.data[ui], every = E.every, last = E.frames - 1;
  const horse = lookOf(u) === "lance" || lookOf(u) === "barded", rd = u.rd || 1;
  const fd = u.fd || (u.fd = Math.max(1, ...u.figs.map(q => Math.round(q[1] / rd))));
  const pos = new Float32Array(N * Nm * 2), face = new Float32Array(N * Nm), fig = new Int16Array(N * Nm).fill(-1), rank = new Uint8Array(N * Nm), phase = new Float32Array(N * Nm);
  const hd = new Map();   // курс тела от движка (охват, бегство своим курсом); иначе — курс отряда
  if(S.heads) S.heads.forEach((list, f) => { if(list) for(const [u2, id, a] of list) if(u2 === ui) hd.set(f * 65536 + id, a); });
  const headAt = (f, k) => { const h = hd.get(f * 65536 + k); return h !== undefined ? h : F[f][ui] ? F[f][ui][2] : 0; };
  const s8 = v => v > 127 ? v - 256 : v, s16 = v => v > 32767 ? v - 65536 : v, B = E.bytes;
  // отсчёт a, боец i: тело (или −1), смещение, курс к телу
  const rec = (a, i) => { const o = (a * Nm + i) * B, body = D[o] | D[o + 1] << 8; if(body === 65535) return null;
    return B === 7 ? [body, s16(D[o + 2] | D[o + 3] << 8) / 20, s16(D[o + 4] | D[o + 5] << 8) / 20, s8(D[o + 6]) * 2] : [body, s8(D[o + 2]) / 10, s8(D[o + 3]) / 10, s8(D[o + 4]) * 2]; };
  // смещение от тела в мире — в миг отсчёта, по курсу тела в этот миг; между отсчётами ведём уже его (курс бегущей
  // фигурки бывает перевёрнут на 180° — повернуть большое смещение чужим курсом значит пронести бойца сквозь тело)
  const off = (a, R) => { const f = Math.min(N - 1, a * every), h = headAt(f, R[0]) * Math.PI / 180, c = Math.cos(h), s = Math.sin(h);
    return [R[1] * c - R[2] * s, R[1] * s + R[2] * c, headAt(f, R[0]) + R[3]]; };
  const bodyAt = (f, k) => { const p = F[f][ui]; return p && p[4 + 2 * k] != null ? [p[4 + 2 * k], p[5 + 2 * k]] : null; };
  const seeds = new Array(Nm), ph = new Float64Array(Nm), px = new Float64Array(Nm).fill(NaN), py = new Float64Array(Nm);
  for(let i = 0; i < Nm; i++){ seeds[i] = u.id * 7919 + (i + 1) * 31; ph[i] = hash(seeds[i], 5); }
  for(let f = 0; f < N; f++){
    const a = Math.min(last, Math.floor(f / every)), b = Math.min(last, a + 1), q = b > a ? (f - a * every) / every : 0;
    for(let i = 0; i < Nm; i++){
      const o = f * Nm + i, A = rec(a, i);
      if(!A){ px[i] = NaN; continue; }
      const Bq = rec(b, i), Bs = Bq && Bq[0] === A[0] ? Bq : null;
      let k = A[0], w, h;
      const body = bodyAt(f, k), wa = off(a, A);
      if(Bs && body){
        const wb = off(b, Bs);
        w = [body[0] + wa[0] + (wb[0] - wa[0]) * q, body[1] + wa[1] + (wb[1] - wa[1]) * q];
        h = wa[2] + angD(wb[2] * Math.PI / 180, wa[2] * Math.PI / 180) * 180 / Math.PI * q;
      } else {
        // перешёл в другую фигурку (или тело пропало) — ведём по месту в мире от отсчёта к отсчёту
        const ba = bodyAt(Math.min(N - 1, a * every), A[0]), Bw = Bq || A, bb = bodyAt(Math.min(N - 1, b * every), Bw[0]), wb = off(b, Bw);
        const pa = ba && [ba[0] + wa[0], ba[1] + wa[1]], pb = bb && [bb[0] + wb[0], bb[1] + wb[1]];
        w = !Bq ? pa : pa && pb ? [pa[0] + (pb[0] - pa[0]) * q, pa[1] + (pb[1] - pa[1]) * q] : pa || pb;   // к следующему отсчёту его нет — стоит, где был
        if(!w){ px[i] = NaN; continue; }
        if(q >= 0.5 && Bq) k = Bq[0];
        h = q < 0.5 ? wa[2] : wb[2];
        if(!bodyAt(f, k)){ px[i] = NaN; continue; }   // тела уже нет — пал или ушёл
      }
      pos[o * 2] = w[0]; pos[o * 2 + 1] = w[1]; face[o] = h * Math.PI / 180; fig[o] = k;
      const g = u.figs[k], dy = Bs ? A[2] + (Bs[2] - A[2]) * q : A[2];
      rank[o] = Math.min(255, (g ? g[3] || 0 : 0) * fd + Math.max(0, Math.floor((dy + (g ? g[1] : 2) / 2) / rd)));
      // фаза шага — по пройденному
      if(!isNaN(px[i])){ const d = Math.hypot(w[0] - px[i], w[1] - py[i]), v = d / S.dt; ph[i] += d / (horse ? strideOf(v) : v > 2.6 ? 2.4 : 1.4); }
      px[i] = w[0]; py[i] = w[1]; phase[o] = ph[i];
    }
  }
  // павший — тот самый боец движка (Death.ManId): тело в его снаряжении
  const deadSeed = new Map();
  if(S.dead) S.dead.forEach((d, di) => { if(d[3] === ui && d[7] > 0 && d[7] <= Nm) deadSeed.set(di, seeds[d[7] - 1]); });
  // брошенное на бегу (В11): где побежали — там и бросили
  const dropped = [];
  for(let f = 1; f < N; f++) if(fleeing(stateAt(ui, f)) && !fleeing(stateAt(ui, f - 1)))
    for(let i = 0; i < Nm; i++){ const o = f * Nm + i; if(fig[o] >= 0 && drops(seeds[i])) dropped.push([pos[o * 2], pos[o * 2 + 1], face[o] + (hash(seeds[i], 22) - 0.5) * 2.5, seeds[i], f]); }
  let lastF = -1, lastL = null;
  const figMen = fi => {
    if(fi === lastF) return lastL;
    lastF = fi; lastL = [];
    for(let id = 0; id < Nm; id++){ const k = fig[fi * Nm + id]; if(k >= 0) (lastL[k] || (lastL[k] = [])).push(id); }
    return lastL;
  };
  return {n: Nm, pos, face, fig, rank, phase, seed: seeds, figMen, deadSeed, dropped, engine: true};
}
function buildAgents(ui){
  const u = S.units[ui], F = S.frames, N = F.length, pm = u.pm || 1, rd = u.rd || 1, {H, nb} = headsOf(ui);
  const horse = lookOf(u) === "lance" || lookOf(u) === "barded";
  const nf = Math.min(nb, u.figs.length);   // тела со старта; новые (редко) — без своих бойцов
  const fd = u.fd || (u.fd = Math.max(...u.figs.map(q => Math.round(q[1] / rd))));
  const G = u.figs.slice(0, nf).map(g => { const cols = Math.max(1, Math.round(g[0] / pm)), rows = Math.max(1, Math.round(g[1] / rd)); return {w: g[0], d: g[1], cols, cap: cols * rows, rank: g[3] || 0}; });
  const men = [], f0 = F[0][ui];
  for(let k = 0; k < nf; k++){
    if(!f0 || f0[4 + 2 * k] == null) continue;
    const n = Math.min(G[k].cap, Math.round(u.figs[k][2] ?? G[k].cap));
    for(let s = 0; s < n; s++){
      const seed = u.id * 7919 + k * 64 + Math.floor(s / G[k].cols) * 8 + s % G[k].cols;
      men.push({id: men.length, fig: k, slot: s, seed, x: 0, y: 0, face: 0, alive: true, jx: (hash(seed, 1) - 0.5) * 0.12 * pm, jy: (hash(seed, 2) - 0.5) * 0.12 * rd,
        v: 0, acc: (5 + 7 * hash(seed, 24)) * S.dt, tr: (1.4 + 1.8 * hash(seed, 25)) * S.dt});   // разгон, м/с за кадр; поворот, рад за кадр
    }
  }
  const Nm = men.length, pos = new Float32Array(N * Nm * 2), face = new Float32Array(N * Nm), fig = new Int16Array(N * Nm).fill(-1), rank = new Uint8Array(N * Nm);
  // фаза шага (В13), круги: путь ÷ длина круга — у пешего 1,4 м шагом и 2,4 м бегом, у коня — по аллюру
  const phase = new Float32Array(N * Nm);
  for(const m of men) m.ph = hash(m.seed, 5);
  const fs = G.map(() => []);   // фигурка → её бойцы по местам (место = номер в списке, без дыр)
  const full = G.map((g, k) => Math.min(g.cap, Math.round(u.figs[k][2] ?? g.cap)));   // сколько бойцов в фигурке по строю
  let dirty = false;            // были потери или пропали тела — строй смыкается, пока есть кому
  for(const m of men) fs[m.fig][m.slot] = m;
  const deadSeed = new Map();   // номер в S.dead → зерно павшего бойца: тело на земле — в его снаряжении
  // выбыл: на его место встаёт стоящий позади, и так до заднего ряда; дырку там занимает последний в фигурке
  function vacate(m){
    const k = m.fig; m.fig = -1;
    if(k < 0) return;
    const L = fs[k], cols = G[k].cols;
    let v = m.slot;
    while(v + cols < L.length){ L[v] = L[v + cols]; L[v].slot = v; v += cols; }
    const last = L.pop();
    if(v < L.length){ L[v] = last; last.slot = v; }
  }
  const join = (m, k) => { m.fig = k; m.slot = fs[k].length; fs[k].push(m); };
  // расталкивание (бойцы — круги по ширине плеч, конные — шире): каждый отходит на половину перекрытия
  const R0 = horse ? 0.85 : 0.52, cell = new Map();
  function separate(f){
    cell.clear();
    for(const m of men) if(m.alive && m.fig >= 0){ const key = Math.floor(m.x / R0) * 65536 + Math.floor(m.y / R0); let l = cell.get(key); if(!l) cell.set(key, l = []); l.push(m); }
    for(const m of men){
      if(!m.alive || m.fig < 0) continue;
      const cx = Math.floor(m.x / R0), cy = Math.floor(m.y / R0);
      for(let ax = -1; ax <= 1; ax++) for(let ay = -1; ay <= 1; ay++){
        const l = cell.get((cx + ax) * 65536 + cy + ay); if(!l) continue;
        for(const q of l){
          if(q.id <= m.id) continue;
          const dx = q.x - m.x, dy = q.y - m.y, d2 = dx * dx + dy * dy;
          if(d2 >= R0 * R0 || d2 < 1e-8) continue;
          const d = Math.sqrt(d2), push = (R0 - d) / 2 / d;
          m.x -= dx * push; m.y -= dy * push; q.x += dx * push; q.y += dy * push;
        }
      }
    }
    for(const m of men) if(m.alive && m.fig >= 0){ const o = f * Nm + m.id; pos[o * 2] = m.x; pos[o * 2 + 1] = m.y; }
  }
  const FC = new Float64Array(nf), FS = new Float64Array(nf), FH = new Float64Array(nf), FV = new Float64Array(nf);
  const dead = deadsOf(ui), vmax = Math.max(4, (u.norm || 100) / (S.turnSec || 15) * 1.6) * S.dt;
  const dropped = [];   // брошенное на бегу оружие: [x, y, поворот, зерно бойца, кадр]
  for(let f = 0; f < N; f++){
    const fr = F[f][ui], live = k => k >= 0 && k < nf && fr && fr[4 + 2 * k] != null;
    const st = stateAt(ui, f), pf0 = f ? F[f - 1][ui] : null;
    if(f && fleeing(st) && !fleeing(stateAt(ui, f - 1))) for(const m of men) if(m.alive && drops(m.seed)) dropped.push([m.x, m.y, m.face + (hash(m.seed, 22) - 0.5) * 2.5, m.seed, f]);
    for(let k = 0; k < nf; k++) if(fs[k].length && !live(k)){
      // бегущий отряд уходит за край: тело пропало у края карты — его бойцы ушли, а не встают в чужие фигурки (БД4)
      const lx = pf0 && pf0[4 + 2 * k], ly = pf0 && pf0[5 + 2 * k];
      const gone = st >= 1 && st <= 3 && lx != null && Math.min(lx, ly, S.w - lx, S.h - ly) < 30;
      for(const m of fs[k]){ m.fig = -1; if(gone) m.alive = false; }
      fs[k] = []; dirty = true;
    }
    // павший — ближайший к месту гибели боец из фигурок рядом
    for(const [x, y, i] of dead.get(f) || []){
      let best = null, bd = 1e18;
      for(let r = 15; !best && r < 1e5; r *= 4){
        for(let k = 0; k < nf; k++){
          if(!fs[k].length || !live(k) || Math.abs(fr[4 + 2 * k] - x) > r || Math.abs(fr[5 + 2 * k] - y) > r) continue;
          for(const m of fs[k]){ const d = (m.x - x) ** 2 + (m.y - y) ** 2; if(d < bd){ bd = d; best = m; } }
        }
      }
      if(!best) for(const m of men) if(m.alive && m.fig < 0){ const d = (m.x - x) ** 2 + (m.y - y) ** 2; if(d < bd){ bd = d; best = m; } }
      if(best){ vacate(best); best.alive = false; deadSeed.set(i, best.seed); dirty = true; }
    }
    if(fr) for(const o of men){   // без места — в ближайшую фигурку; в полную — лишним рядом, если свободная далеко
      if(!o.alive || o.fig >= 0) continue;
      let best = -1, bc = 1e18;
      for(let k = 0; k < nf; k++){
        if(!live(k)) continue;
        const over = fs[k].length - full[k], c = Math.hypot(fr[4 + 2 * k] - o.x, fr[5 + 2 * k] - o.y) + (over >= 0 ? 4 + 3 * over : 0);
        if(c < bc){ bc = c; best = k; }
      }
      if(best >= 0) join(o, best);
    }
    // смыкание между фигурками (Г30): где не хватает людей, туда переходит задний боец соседа (до 30 м), если у соседа
    // людей больше; брешь закрывают соседи, а нехватка по цепочке уходит туда, где движок убирает тела, — к краям
    if(fr && dirty){
      let moved = 0;
      for(let k = 0; k < nf; k++){
        if(!live(k) || fs[k].length >= full[k]) continue;
        let best = -1, bd = 900;
        for(let j = 0; j < nf; j++){
          if(j === k || !live(j) || !(fs[j].length > full[j] || fs[j].length >= fs[k].length + 2)) continue;
          const d = (fr[4 + 2 * j] - fr[4 + 2 * k]) ** 2 + (fr[5 + 2 * j] - fr[5 + 2 * k]) ** 2;
          if(d < bd){ bd = d; best = j; }
        }
        if(best >= 0){ join(fs[best].pop(), k); moved++; }
      }
      if(!moved) dirty = false;
    }
    // «кругом»: курс фигурки перевернулся — задний ряд стал передним; места зеркалятся, и каждый остаётся, где стоял
    if(f) for(let k = 0; k < nf; k++){
      const L = fs[k];
      if(L.length < 2 || !live(k) || isNaN(H[(f - 1) * nb + k]) || Math.abs(angD(H[f * nb + k], H[(f - 1) * nb + k])) < 2.3) continue;
      L.reverse(); L.forEach((m, i) => m.slot = i);
    }
    // по фигурке: курс, его синус и косинус, шаг тела за кадр (боец не медленнее своего тела) — раз на кадр
    const pf = f ? F[f - 1][ui] : null;
    for(let k = 0; k < nf; k++){
      if(!fs[k].length || !live(k)) continue;
      const h = H[f * nb + k], bx = fr[4 + 2 * k], by = fr[5 + 2 * k];
      FC[k] = Math.cos(h); FS[k] = Math.sin(h); FH[k] = h;
      const qx = pf && pf[4 + 2 * k] != null ? bx - pf[4 + 2 * k] : 0, qy = pf && pf[5 + 2 * k] != null ? by - pf[5 + 2 * k] : 0;
      FV[k] = Math.max(vmax, Math.sqrt(qx * qx + qy * qy) * 1.25 + 0.3);
    }
    for(const m of men){
      const o = f * Nm + m.id;
      if(!m.alive) continue;
      if(m.fig < 0){ pos[o * 2] = m.x; pos[o * 2 + 1] = m.y; face[o] = m.face; continue; }
      const k = m.fig, g = G[k], n = fs[k].length, h = FH[k], c = FC[k], s = FS[k];
      const row = Math.floor(m.slot / g.cols), inRow = Math.max(1, Math.min(g.cols, n - row * g.cols)), i = m.slot - row * g.cols;
      const lx = -g.w / 2 + ((g.cols - inRow) / 2 + i + 0.5) * pm + m.jx, ly = -g.d / 2 + (row + 0.5) * rd + m.jy;
      const tx = fr[4 + 2 * k] + lx * c - ly * s, ty = fr[5 + 2 * k] + lx * s + ly * c;
      if(f === 0){ m.x = tx; m.y = ty; m.face = h; }
      else {
        // каждый трогается и останавливается в своём темпе: шаг не больше, чем позволяет его разгон
        const dx = tx - m.x, dy = ty - m.y, d = Math.sqrt(dx * dx + dy * dy), vm = Math.min(FV[k], (m.v + m.acc) * S.dt), st = Math.min(d, vm);
        if(d > 1e-6){ m.x += dx / d * st; m.y += dy / d * st; }
        m.v = st / S.dt;
        m.ph += st / (horse ? strideOf(m.v) : m.v > 2.6 ? 2.4 : 1.4);
        // далеко от места — бежит туда и смотрит, куда бежит; на месте — как фигурка; поворачивается в своём темпе
        const want = d - st > 2.5 ? Math.atan2(dx, -dy) : h;
        if(want !== m.face) m.face = turnTo(m.face, want, m.tr);
      }
      pos[o * 2] = m.x; pos[o * 2 + 1] = m.y; face[o] = m.face; fig[o] = k; rank[o] = Math.min(255, g.rank * fd + row); phase[o] = m.ph;
    }
    if(f) separate(f);
  }
  let lastF = -1, lastL = null;
  const figMen = fi => {   // кто в какой фигурке в кадре fi
    if(fi === lastF) return lastL;
    lastF = fi; lastL = [];
    for(let id = 0; id < Nm; id++){ const k = fig[fi * Nm + id]; if(k >= 0) (lastL[k] || (lastL[k] = [])).push(id); }
    return lastL;
  };
  return {n: Nm, pos, face, fig, rank, phase, seed: men.map(m => m.seed), figMen, deadSeed, dropped};
}

// ── Фигурка: её бойцы — с их местами, курсом и снаряжением; анимация — по каждому ──
function drawMen(u, figShape, fx, fy, h, col, moving, t, k){
  const look = lookOf(u), horse = look === "lance" || look === "barded", rd = u.rd || 1, N = S.frames.length;
  setZoom(); prepFrameMen();
  const ui = S.units.indexOf(u), ag = agentsOf(ui), fi = Math.min(Math.floor(frame), N - 1), ids = ag.figMen(fi)[k];
  if(!ids || !ids.length) return;
  const f1 = Math.min(fi + 1, N - 1), q = frame - fi, Nm = ag.n, kits = kitsOf(u), parts = view.s >= PARTS_FROM;
  const ust = stateAt(ui, fi), flee = fleeing(ust);
  const eng = MEL.get(ui) && !flee ? MEL.get(ui).get(k) : undefined, fire = FIRE.has(ui * 100000 + k);
  ctx.save(); ctx.translate(X(0), Y(0)); ctx.scale(view.s, view.s);
  const T0 = ctx.getTransform(), B = [T0.a, T0.b, T0.c, T0.d, T0.e, T0.f];
  const M = ids.map(id => {
    const o0 = fi * Nm + id, alive1 = ag.fig[f1 * Nm + id] >= 0, o1 = alive1 ? f1 * Nm + id : o0;
    const x0 = ag.pos[o0 * 2], y0 = ag.pos[o0 * 2 + 1], x1 = ag.pos[o1 * 2], y1 = ag.pos[o1 * 2 + 1], a0 = ag.face[o0];
    return {x: x0 + (x1 - x0) * q, y: y0 + (y1 - y0) * q, face: a0 + angD(ag.face[o1], a0) * q, sp: Math.hypot(x1 - x0, y1 - y0) / S.dt,
      ph: ag.phase[o0] + (ag.phase[o1] - ag.phase[o0]) * q,
      seed: ag.seed[id], rank: ag.rank[o0], kit: kits[Math.floor(hash(ag.seed[id], 4) * kits.length)]};
  });
  // древки к бою (В13): копья и пики — стоймя на марше, опущены у врага; издали — готовый спрайт с той или другой позой
  const low = lowerAt(ui), poleRank = r => low >= 0.5 ? r : 99;
  // сплотились (БД4) — первые 4 с вскидывают оружие и кричат (В13)
  const cheer = ust === 4 && (fi - stateSince(ui, fi)) * S.dt < 4;
  if(view.s < 6){   // совсем издали (2–3 px на бойца): плечи и головы точками — две заливки на фигурку
    const pb = new Path2D(), ph = new Path2D();
    for(const m of M){ pb.moveTo(m.x + 0.27, m.y); pb.ellipse(m.x, m.y, 0.27, 0.15, m.face, 0, 6.283); ph.moveTo(m.x + 0.12, m.y); ph.arc(m.x, m.y, 0.12, 0, 6.283); }
    ctx.fillStyle = GREY ? mix(CLOTH[0], col, 0.3) : col; ctx.fill(pb); ctx.fillStyle = "#9a9a94"; ctx.fill(ph);
    ctx.restore(); return;
  }
  if(!parts){   // издали: готовый спрайт, лёгкое покачивание на ходу
    for(const m of M){ const bob = m.sp > 0.8 ? Math.sin(m.ph * 12.566) * 0.05 : 0; put(compSpr(m.kit, poleRank(m.rank)), mT(mR(mT(B, m.x, m.y), m.face), 0, bob)); }
    ctx.restore(); return;
  }
  // рукопашная: кто ближе к врагу — бьёт, остальные напирают и поворачиваются туда же
  if(eng !== undefined){
    const ca = Math.cos(eng), sa = Math.sin(eng), toward = eng + Math.PI / 2;
    let top = -1e9; for(const m of M) top = Math.max(top, m.x * ca + m.y * sa);
    for(const m of M){ m.atk = m.x * ca + m.y * sa > top - 1.6 * rd; m.face = m.atk ? toward : m.face + angD(toward, m.face) * 0.5; }
  }
  // стрельба (В6): стрелок, ближайший к точке вылета стрелы, натягивает лук перед ней и отпускает в миг вылета
  const shoot = look === "bow" || look === "crossbow";
  let active = false;
  if(shoot && S.arrows){
    const A = S.arrows, idx = S._arrU[ui], fr = Math.hypot(figShape[0], figShape[1]) / 2 + 3, back = look === "crossbow" ? 2.6 : 0.3;
    const j3 = lowerT0(A, idx, t - 3);
    active = j3 < idx.length && A[idx[j3]][0] < t + 3;
    for(let qq = lowerT0(A, idx, t - back); qq < idx.length && A[idx[qq]][0] <= t + 0.8; qq++){
      const a = A[idx[qq]];
      if(Math.abs(a[1] - fx) > fr || Math.abs(a[2] - fy) > fr) continue;
      let best = null, bd = 1.5;
      for(const m of M){ const dd = Math.hypot(m.x - a[1], m.y - a[2]); if(dd < bd){ bd = dd; best = m; } }
      if(best && (best.shot === undefined || Math.abs(t - a[0]) < Math.abs(best.shot))) best.shot = t - a[0];
    }
  }
  // тени — до бойцов, чтобы не ложились на соседа; смещение влево вниз в мире
  if(view.s >= 10){
    const so = horse ? 0.45 : 0.2, sw = 0.66, shh = horse ? 2.0 : 0.48, sy = horse ? 0.12 : 0.02;
    if(view.s < 20){   // одной заливкой на фигурку: перекрытия не темнеют
      const pth = new Path2D();
      for(const m of M){ const x = m.x + SH_X * so - Math.sin(m.face) * sy, y = m.y + SH_Y * so + Math.cos(m.face) * sy; pth.moveTo(x + sw * 0.4, y); pth.ellipse(x, y, sw * 0.4, shh * 0.4, m.face, 0, 6.283); }
      ctx.setTransform(B[0], B[1], B[2], B[3], B[4], B[5]); ctx.fillStyle = "rgba(24,18,8,.2)"; ctx.fill(pth);
    } else {
      const sp = {c: SHADOW, x: -sw / 2, y: -shh / 2, w: sw, h: shh};
      for(const m of M) put(sp, mT(mR(mT(B, m.x + SH_X * so, m.y + SH_Y * so), m.face), 0, sy));
    }
  }
  const sparks = [];   // вспышки ударов (В13) — поверх всех бойцов фигурки
  for(const m of M){
    const kit = m.kit, s = m.seed, ph0 = hash(s, 5), walking = m.sp > 0.8;
    let rot = 0, ox = 0, oy = 0, step = 0, ap = -1, blow = 0;   // ap — доля круга удара, blow — номер удара
    if(m.atk){ const per = 1.1 + 0.9 * hash(s, 7), u0 = t / per + hash(s, 8); ap = frac(u0); blow = Math.floor(u0); }
    if(walking && !m.atk){ step = Math.sin(m.ph * 6.283); rot += (m.sp > 2.6 ? 0.11 : 0.07) * step; }   // бегом плечи ходят сильнее
    else if(!m.atk){ rot += 0.035 * Math.sin(t * 0.9 + ph0 * 6.283) + (eng === undefined ? glance(t, s) : 0); ox = 0.02 * Math.sin(t * 0.6 + ph0 * 9); }
    if(eng !== undefined && !m.atk) oy = -0.03 - 0.03 * Math.sin(t * 3 + ph0 * 6.283);          // задние напирают
    const P = restPose(kit, m.rank, low), base = mR(mT(B, m.x, m.y), m.face);
    if(flee && !horse){   // бегство (В11): бегом, щит за спиной, оружие несут как придётся или бросили
      rot += lookBack(t, s);                                                                      // оглядываются на бегу (В13)
      const Mf = mR(mT(base, ox, -0.05), rot);
      if(step && view.s >= 14){ const bt = bootSpr(); put(bt, mT(Mf, -0.09, 0.03 + 0.14 * step)); put(bt, mT(Mf, 0.09, 0.03 - 0.14 * step)); }
      if(kit.shield && kit.shield.shape !== "buckler") put(shieldSpr(kit.shield), mP(Mf, [0, 0.24, 0, 0.85, 0.45]));
      put(bodySpr(kit), Mf);
      const w = kit.weapon;
      if(!drops(s)){
        if(w === "bow") put(bowSpr(0), mP(Mf, [0.05, 0.25, 0.4, 0.8, 0.5]));
        else if(w === "crossbow") put(xbowSpr(1), mP(Mf, [0.05, 0.3, 0.5, 1, 0.5]));
        else put(weapSpr(w, kit.col), mP(Mf, THRUST.has(w) ? [0.2, 0.05, 0.4 + 0.05 * step, 1, 0.25] : [0.2, 0.05, 0.6, 1, 0.5]));
      }
      continue;
    }
    if(horse){   // конь по частям (В13): аллюр по скорости, стоящий переступает и машет хвостом
      const run = walking || m.atk, HP = horsePose(m.sp, m.ph, t, s);
      putHorse(kit, base, HP);
      const Mr = mR(mT(base, ox, 0.02 + HP.bob), rot * 0.35);
      let W = P.W;
      if(kit.weapon === "lance"){ if(run) W = [0.2, 0.25 + (m.atk ? thrustOff(ap) : 0), -0.04, 1, 1]; }
      else if(m.atk){ const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
      const [hr, hl, showL] = handsOf(kit, W, P.Sh);
      if(hr) putArm(Mr, kit, 1, hr); if(hl) putArm(Mr, kit, -1, hl);
      put(bodySpr(kit), Mr);
      if(P.Sh) put(shieldSpr(kit.shield), mP(Mr, P.Sh));
      if(W) put(weapSpr(kit.weapon, kit.col), mP(Mr, W));
      if(hr) putHand(Mr, kit, hr); if(hl && showL) putHand(Mr, kit, hl);
      if(m.atk && ap >= 0.42 && ap < 0.5) sparks.push([m, kit.weapon === "lance" ? -2.6 : -0.9, ap]);
      continue;
    }
    // стрелок: состояние лука по времени до своего выстрела
    let bowSt = 0, xb = 0, lean = 0;
    if(shoot && !m.atk){
      if(m.shot !== undefined){
        const dt = m.shot;
        if(look === "bow") bowSt = dt < -0.45 ? 1 : dt < -0.2 ? 2 : dt < 0 ? 3 : dt < 0.22 ? 4 : 1;
        else xb = dt < 0 ? 0 : dt < 0.25 ? 1 : 2;
      } else if(active){ bowSt = hash(s, 14) < 0.5 ? 1 : 0; xb = hash(s, 14) < 0.5 ? 0 : 2; }
      if(bowSt >= 2) rot -= 0.14;
      // арбалет взводят через стремя (В13): нагнулся к стремени (2), тянет тетиву крюком к поясу (3) — и снова
      if(xb === 2){ xb = frac(t * 0.45 + ph0) < 0.5 ? 2 : 3; lean = xb === 2 ? 0.1 : 0.04; rot += 0.04 * Math.sin(t * 4 + ph0 * 6); }
      if(xb === 1) oy += 0.04;
    }
    // удар (В13): копья и пики колют; меч то рубит, то колет; топор, булава и дубина — то сбоку, то сверху.
    // Каждый удар выбирается заново (хешем по номеру удара) — у соседей разный порядок
    const wk = m.atk && shoot ? (kit.side !== "none" ? kit.side : null) : kit.weapon;
    const kind = !m.atk || !wk ? null : THRUST.has(wk) ? "thrust"
      : wk === "sword" || wk === "falchion" ? (hash(s * 7 + blow, 41) < 0.35 ? "thrust" : "swing") : hash(s * 7 + blow, 42) < 0.5 ? "chop" : "swing";
    if(m.atk && ap >= 0){ oy -= ap > 0.3 && ap < 0.55 ? 0.06 : 0; rot += kind === "swing" ? 0.18 * Math.sin(ap * 6.283) : kind === "chop" ? -0.08 * Math.sin(ap * 6.283) : 0; }
    if(cheer && !m.atk) oy -= 0.05 * Math.max(0, Math.sin(t * 9 + ph0 * 6.283));                  // ликуют — подпрыгивают
    const Mm = mR(mT(base, ox, oy - lean), rot);
    // ноги: на ходу и в бою шагают
    const st = step || (m.atk ? Math.sin(ap * 6.283) * 0.6 : 0);
    if(st && view.s >= 14){ const bt = bootSpr(); put(bt, mT(Mm, -0.09, 0.03 + 0.12 * st)); put(bt, mT(Mm, 0.09, 0.03 - 0.12 * st)); }
    // кто стоит или идёт и ничего не делает — один готовый спрайт; по частям — кто бьёт, стреляет, прикрывается щитом,
    // поднимает или опускает древко, ликует
    const raise = fire && !m.atk && P.Sh && kit.shield.shape !== "buckler" && hash(s, 13) < 0.85;
    const pole = (kit.weapon === "pike" || kit.weapon === "spear" || kit.weapon === "fork") && low > 0.02 && low < 0.98;
    if(!m.atk && !raise && !pole && !cheer && bowSt < 2 && bowSt !== 4 && !xb){ put(compSpr(kit, poleRank(m.rank)), Mm); continue; }
    // щит: под стрелами — над головой; в рукопашной — вперёд, навстречу удару врага (В13): прикрывается между своими ударами
    let Sh = P.Sh;
    if(Sh){
      if(raise) Sh = [-0.03, -0.05, -0.1, 1, 0.9];
      else if(m.atk){ const c = Math.max(0, Math.sin((ap + 0.5) * 6.283)); Sh = [Sh[0] + 0.04 + 0.05 * c, Sh[1] - 0.06 - 0.09 * c, Sh[2] + 0.15 + 0.2 * c, Sh[3], Sh[4]]; }
    }
    // оружие
    let wpn = wk, W = P.W;
    if(m.atk && shoot) W = wpn ? [0.22, -0.06, 0.3, 1, 0.65] : null;
    if(m.atk && wpn){
      if(kind === "thrust") W = THRUST.has(wpn) ? [W[0], (wpn === "pike" ? 0 : -0.1) + thrustOff(ap), 0, 1, 1] : [0.16, -0.14 + 0.8 * thrustOff(ap), 0.04, 1, 1];
      else if(kind === "chop"){ const [y2, sy2] = chopPose(ap); W = [0.2, y2, 0.1, 1, sy2]; }
      else { const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
      if(ap >= 0.42 && ap < 0.5) sparks.push([m, kind === "thrust" ? (wpn === "pike" ? -3.7 : THRUST.has(wpn) ? -1.35 : -0.85) : -0.75, ap]);
    } else if(cheer && W) W = [W[0], W[1] - 0.05, W[2] * 0.3 - 0.1, 1, Math.min(W[4], 0.3) + 0.08 * Math.sin(t * 9 + ph0 * 6.283)];   // вскинули оружие
    else if(W && step) W = [W[0], W[1], W[2] + 0.03 * step, W[3], W[4]];
    // руки (В15): предплечья под телом, кисти поверх оружия; стрелок в рукопашной держит запасное оружие одной рукой
    const hk = m.atk && shoot ? Object.assign({}, kit, {weapon: wpn || "none"}) : kit;
    const [hr, hl, showL] = handsOf(hk, wpn ? W : null, Sh, bowSt, xb);
    if(hr) putArm(Mm, kit, 1, hr); if(hl) putArm(Mm, kit, -1, hl);
    put(bodySpr(kit), Mm);
    if(Sh) put(shieldSpr(kit.shield), mP(Mm, Sh));
    if(wpn === "bow" && !m.atk) put(bowSpr(bowSt), Mm);
    else if(wpn === "crossbow" && !m.atk) put(xbowSpr(xb), Mm);
    else if(W && wpn) put(weapSpr(wpn, kit.col), mP(Mm, W));
    if(hr) putHand(Mm, kit, hr); if(hl && showL) putHand(Mm, kit, hl);
  }
  // вспышка удара (В13): в миг удара у острия — звёздочка, за 0,1 с вырастает и гаснет
  for(const [m, reach, ap] of sparks){
    const e = (ap - 0.42) / 0.08, base = mR(mT(B, m.x, m.y), m.face), k2 = 0.6 + 0.8 * e;
    ctx.globalAlpha = 1 - e; put(sparkSpr(), mS(mT(base, 0.2, reach), k2, k2)); ctx.globalAlpha = 1;
  }
  ctx.restore();
}

// ── Стрелы (В6): летят по дуге из данных движка, тень на земле отходит с высотой; упавшие торчат из земли ──
// Полёт между вылетом и концом восстановлен по краям: высота — парабола с той же начальной скоростью вверх, путь по
// земле — с замедлением (сопротивление воздуха), чтобы и точка, и время конца совпали с движком.
function arrowAt(a, t){
  const tau = Math.max(1e-3, a[7] - a[0]), u = Math.max(0, Math.min(1, (t - a[0]) / tau)), dx = a[8] - a[1], dy = a[9] - a[2], D = Math.hypot(dx, dy), vh = Math.hypot(a[4], a[5]);
  const kk = D > 0.01 ? Math.max(1, Math.min(2, vh * tau / D)) : 1, s = kk * u + (1 - kk) * u * u, tt = u * tau;
  const gz = 2 * (a[3] + a[6] * tau - a[10]) / (tau * tau), z = a[3] + a[6] * tt - 0.5 * gz * tt * tt;
  const vz = a[6] - gz * tt, vhh = D / tau * (kk + 2 * (1 - kk) * u);
  return [a[1] + dx * s, a[2] + dy * s, z, D > 0.01 ? Math.atan2(dy, dx) : Math.atan2(a[5], a[4]), Math.atan2(vz, vhh)];
}
function viewBox(r, pad){ return [(-view.ox) / view.s - pad, (-view.oy) / view.s - pad, (r.width - view.ox) / view.s + pad, (r.height - view.oy) / view.s + pad]; }
function drawArrows(r){
  if(!S.arrows || view.s < 1.2) return;
  prepMenScene();
  const t = frame * S.dt, A = S.arrows, s = view.s, [vx0, vy0, vx1, vy1] = viewBox(r, 3);
  const shd = new Path2D(), body = new Path2D(), trail = new Path2D(), fl = new Path2D(), detail = s >= 8;
  for(let i = lowerT0(A, null, t - S._maxFly); i < A.length && A[i][0] <= t; i++){
    const a = A[i]; if(a[7] <= t) continue;
    const [x, y, z, ang, pitch] = arrowAt(a, t);
    if(x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
    const L = 0.8 * Math.max(0.25, Math.cos(pitch)) * (1 + Math.max(0, z) * 0.012), cx = Math.cos(ang) * L / 2, cy = Math.sin(ang) * L / 2;
    const sx = SH_X * z * 0.55, sy = SH_Y * z * 0.55;
    shd.moveTo(X(x - cx + sx), Y(y - cy + sy)); shd.lineTo(X(x + cx + sx), Y(y + cy + sy));
    body.moveTo(X(x - cx), Y(y - cy)); body.lineTo(X(x + cx), Y(y + cy));
    const [px, py] = arrowAt(a, t - 0.06);
    trail.moveTo(X(px), Y(py)); trail.lineTo(X(x - cx), Y(y - cy));
    if(detail){ const nx = -Math.sin(ang) * 0.05, ny = Math.cos(ang) * 0.05; fl.moveTo(X(x - cx + nx), Y(y - cy + ny)); fl.lineTo(X(x - cx * 0.7), Y(y - cy * 0.7)); fl.lineTo(X(x - cx - nx), Y(y - cy - ny)); }
  }
  ctx.save(); ctx.lineCap = "round";
  ctx.strokeStyle = "rgba(20,16,10,.22)"; ctx.lineWidth = Math.max(1, 0.05 * s); ctx.stroke(shd);
  ctx.strokeStyle = "rgba(60,46,30,.2)"; ctx.lineWidth = Math.max(0.8, 0.03 * s); ctx.stroke(trail);
  ctx.strokeStyle = "#2a1f16"; ctx.lineWidth = Math.max(1, 0.045 * s); ctx.stroke(body);
  if(detail){ ctx.strokeStyle = "#e8e2d2"; ctx.lineWidth = Math.max(1, 0.03 * s); ctx.stroke(fl); }
  ctx.restore();
}
// упавшие стрелы: в землю — торчат назад по ходу полёта (чем круче падала, тем короче видна), в теле — короче,
// отскочившие от брони и щита — лежат рядом плашмя
function drawStuck(r, t){
  if(!S.arrows || view.s < 2.5) return;
  const A = S.arrows, [vx0, vy0, vx1, vy1] = viewBox(r, 1), body = new Path2D(), fl = new Path2D(), detail = view.s >= 10;
  for(const i of S._stuck){
    const a = A[i]; if(a[7] > t) break;
    let x = a[8], y = a[9];
    if(x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
    let ang = Math.atan2(a[9] - a[2], a[8] - a[1]), L;
    if(a[12] === 2){ x += (hash(i, 1) - 0.5) * 0.7; y += (hash(i, 2) - 0.5) * 0.7; ang += (hash(i, 3) - 0.5) * 2.5; L = 0.7; }
    else { const pitch = arrowAt(a, a[7])[4]; L = (a[12] === 1 ? 0.3 : 0.55) * Math.max(0.2, Math.cos(pitch)); }
    const tx = x - Math.cos(ang) * L, ty = y - Math.sin(ang) * L;
    body.moveTo(X(x), Y(y)); body.lineTo(X(tx), Y(ty));
    if(detail){ const nx = -Math.sin(ang) * 0.045, ny = Math.cos(ang) * 0.045; fl.moveTo(X(tx + nx), Y(ty + ny)); fl.lineTo(X(tx - nx), Y(ty - ny)); }
  }
  ctx.save(); ctx.lineCap = "round";
  ctx.strokeStyle = "#3b2b1d"; ctx.lineWidth = Math.max(0.8, 0.035 * view.s); ctx.stroke(body);
  if(detail){ ctx.strokeStyle = "#e8e2d2"; ctx.lineWidth = Math.max(1, 0.05 * view.s); ctx.stroke(fl); }
  ctx.restore();
}

// ── Павшие и кровь (Г67, В8): лужа, брызги по направлению удара; павший падает за треть секунды и лежит ──
// Тело ложится головой туда, куда толкнул удар (стрела — по ходу полёта), ноги — где стоял. Рядом — его оружие и щит.
// Конь, убитый под всадником, — на боку, всадник рядом.
function drawDead(){
  if(!S.dead) return;
  prepMenScene(); setZoom();
  const now = frame, near = view.s >= MEN_FROM, t = frame * S.dt, r = cv.getBoundingClientRect(), [vx0, vy0, vx1, vy1] = viewBox(r, 2);
  ctx.fillStyle = BLOOD;
  const pool = new Path2D();   // вся кровь — одной заливкой
  for(const [x, y, fi, ui, fc, dir, part] of S.dead){
    if(fi > now) continue;
    const px = X(x), py = Y(y);
    if(!near){ pool.rect(px - 0.8, py - 0.8, 1.6, 1.6); continue; }
    if(x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
    const seed = (fi * 131 + ui * 7919 + Math.round(x * 13) + Math.round(y * 7)) | 0;
    const g = Math.min(1, 0.25 + (now - fi) * S.dt / 1.2), a = dir * Math.PI / 180, ca = Math.cos(a), sa = Math.sin(a);
    const big = part === 3 ? 1.7 : part === 0 ? 0.65 : part === 2 ? 0.85 : 1;
    for(let k = 0; k < 3; k++){
      const rr = (0.15 + 0.13 * hash(seed, k)) * big * g * view.s, off = (0.3 + 0.35 * hash(seed, k + 7)) * g;   // лужа — под телом, ближе к груди
      const ox = (ca * off + (hash(seed, k + 3) - 0.5) * 0.3) * view.s, oy = (sa * off + (hash(seed, k + 5) - 0.5) * 0.3) * view.s;
      pool.moveTo(px + ox + rr, py + oy); pool.ellipse(px + ox, py + oy, rr, rr * (0.65 + 0.35 * hash(seed, k + 9)), a, 0, 6.283);
    }
    const n = 2 + Math.floor(hash(seed, 11) * 4);
    for(let k = 0; k < n; k++){
      const dd = (0.55 + 1.3 * hash(seed, 20 + k)) * g, sp = (hash(seed, 30 + k) - 0.5) * 0.8;
      const bx = px + Math.cos(a + sp) * dd * view.s, by = py + Math.sin(a + sp) * dd * view.s, br = Math.max(0.6, (0.05 + 0.08 * hash(seed, 40 + k)) * view.s);
      pool.moveTo(bx + br, by); pool.arc(bx, by, br, 0, 6.283);
    }
  }
  ctx.fill(pool);
  if(!near) return;
  drawDropped(r, t);
  if(view.s < 7){   // издали тела — пятна цвета своей стороны, одна заливка на отряд
    const by = new Map();
    for(const [x, y, fi, ui, fc, dir] of S.dead){
      if(fi > now || x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
      let p = by.get(ui); if(!p) by.set(ui, p = new Path2D());
      const a = dir * Math.PI / 180, cx = X(x + Math.cos(a) * 0.55), cy = Y(y + Math.sin(a) * 0.55);
      p.moveTo(cx + 0.8 * view.s, cy); p.ellipse(cx, cy, 0.8 * view.s, 0.24 * view.s, a, 0, 6.283);
    }
    by.forEach((p, ui) => { ctx.fillStyle = mix(S.units[ui].col, "#8b8781", 0.55); ctx.fill(p); });
    drawStuck(r, t);
    return;
  }
  S.dead.forEach((dd, di) => {
    const [x, y, fi, ui, fc, dir, part, , killed] = dd;
    if(fi > now || x < vx0 || x > vx1 || y < vy0 || y > vy1) return;
    const u = S.units[ui], kits = kitsOf(u), seed = (fi * 131 + ui * 7919 + Math.round(x * 13) + Math.round(y * 7)) | 0;
    const man = agentsOf(ui).deadSeed.get(di);   // павший — тот самый боец: тело в его снаряжении (В10)
    const kit = kits[Math.floor(hash(man ?? seed, man === undefined ? 50 : 4) * kits.length)], v = Math.floor(hash(seed, 51) * 4);
    const age = t - fi * S.dt, a = dir * Math.PI / 180 + (hash(seed, 52) - 0.5) * 0.5;
    const rider = part === 3 && kit.horse, wounded = killed === 0 && !rider;   // killed нет (старые данные) — убит
    // удар (В13): первые 0,15 с боец ещё стоит — его качнуло по удару; потом падает
    if(age < 0.15 && !rider){
      const k2 = age / 0.15, d = dir * Math.PI / 180, sp0 = compSpr(kit, 0);
      ctx.save(); ctx.translate(X(x + Math.cos(d) * 0.12 * k2), Y(y + Math.sin(d) * 0.12 * k2));
      ctx.rotate(fc * Math.PI / 180 + (hash(seed, 55) - 0.5) * 0.6 * k2); ctx.scale(view.s, view.s);
      ctx.drawImage(sp0.c, sp0.x, sp0.y, sp0.w, sp0.h); ctx.restore();
      return;
    }
    const p = Math.min(1, (age - 0.15) / 0.35), e = 1 - (1 - p) * (1 - p);
    ctx.save(); ctx.translate(X(x), Y(y)); ctx.rotate(a + Math.PI / 2); ctx.scale(view.s, view.s);
    if(rider){
      const he = 1 - Math.pow(1 - Math.min(1, age / 0.5), 2);
      const hs = deadHorseSpr(kit); ctx.save(); ctx.scale(1, 0.4 + 0.6 * he); ctx.drawImage(hs.c, hs.x, hs.y - 0.6, hs.w, hs.h); ctx.restore();
      // всадник слетает с коня (В13): за 0,55 с — в сторону от туши, в полёте крупнее (выше, ближе к нам)
      const fl = Math.min(1, age / 0.55), k3 = 1 + 0.3 * Math.sin(fl * Math.PI);
      ctx.translate(-0.9 * ease(fl), 0.3 * ease(fl)); ctx.scale(k3, k3);
    }
    let flip = 1;
    if(wounded){
      // раненый (Г39, В13) бросил оружие и щит там, где упал; дальше ползёт рывками прочь от врага (0,32 м за 0,9 с)
      // и оставляет кровавый след, или корчится на месте; через 4–12 с затихает
      const w = kit.weapon === "bow" || kit.weapon === "crossbow" ? kit.side : kit.weapon;
      if(w && w !== "none"){ const ws = weapSpr(w, kit.col), sc = w === "pike" || w === "lance" ? 0.4 : w === "spear" || w === "fork" ? 0.7 : 0.9;
        ctx.save(); ctx.translate(0.45, -0.5); ctx.rotate(hash(seed, 56) * 6.283); ctx.scale(sc, sc); ctx.drawImage(ws.c, ws.x, ws.y, ws.w, ws.h); ctx.restore(); }
      if(kit.shield){ const ss = shieldSpr(kit.shield); ctx.save(); ctx.translate(-0.5, -0.4); ctx.rotate(hash(seed, 57) * 6.283); ctx.scale(0.85, 0.85); ctx.drawImage(ss.c, ss.x, ss.y, ss.w, ss.h); ctx.restore(); }
      const life = Math.min(Math.max(0, age - 0.5), 4 + 8 * hash(seed, 54));
      if(hash(seed, 53) < 0.65){
        const n = life / 0.9, j = Math.floor(n);
        ctx.fillStyle = BLOOD; ctx.globalAlpha = 0.7;
        for(let q = 0; q < j; q++){ ctx.beginPath(); ctx.arc((hash(seed, 60 + q) - 0.5) * 0.14, -0.32 * q - 0.25, 0.035 + 0.03 * hash(seed, 80 + q), 0, 6.283); ctx.fill(); }
        ctx.globalAlpha = 1;
        ctx.translate(0, -0.32 * (j + ease(n - j))); flip = j % 2 ? -1 : 1;
      } else { ctx.rotate(0.12 * Math.sin(life * 2.3 + seed)); flip = Math.floor(life / 1.6) % 2 ? -1 : 1; }
    }
    // падение: тело «ложится» от ног — растягиваем вдоль оси от точки, где стоял
    ctx.scale(flip, 0.25 + 0.75 * e);
    const sp = wounded ? crawlSpr(kit) : corpseSpr(kit, v); ctx.drawImage(sp.c, sp.x, sp.y - 0.8, sp.w, sp.h);
    if(part === 0 && p >= 1 && !wounded){ ctx.fillStyle = BLOOD; ctx.globalAlpha = 0.8; ctx.beginPath(); ctx.arc(0, -1.36, 0.1, 0, 6.283); ctx.fill(); }
    ctx.restore();
  });
  drawStuck(r, t);
}

// Брошенное на бегу (В11): оружие там, где побежали; знамя упало там, где был отряд. Сплотились — знамя подняли
function drawDropped(r, t){
  if(!S.states || view.s < 7) return;
  const [vx0, vy0, vx1, vy1] = viewBox(r, 3), fi = Math.min(Math.floor(frame), S.frames.length - 1);
  S.units.forEach((u, ui) => {
    if(!S.states[ui] || S.states[ui].length < 4) return;
    const ag = agentsOf(ui), kits = kitsOf(u);
    ctx.save(); ctx.translate(X(0), Y(0)); ctx.scale(view.s, view.s);
    const T0 = ctx.getTransform(), B = [T0.a, T0.b, T0.c, T0.d, T0.e, T0.f];
    for(const [x, y, rot, seed, f] of ag.dropped){
      if(f > fi || x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
      const kit = kits[Math.floor(hash(seed, 4) * kits.length)], w = kit.weapon === "bow" || kit.weapon === "crossbow" ? kit.side : kit.weapon;
      if(w && w !== "none") put(weapSpr(w, kit.col), mS(mR(mT(B, x, y), rot), 0.9, w === "pike" ? 0.5 : 0.9));
      else if(kit.weapon === "bow") put(bowSpr(0), mR(mT(B, x, y), rot));
    }
    ctx.restore();
    // знамя лежит, пока отряд бежит или ушёл
    const st = stateAt(ui, fi);
    if(st === 1 || st === 2){
      const f0 = stateSince(ui, fi), F0 = S.frames[f0][ui], sc = Math.min(1.6, Math.max(0.8, view.s / 12));
      ctx.save(); ctx.translate(X(F0[0]), Y(F0[1])); ctx.rotate(hash(ui, f0) * 6.283); ctx.scale(sc, sc);
      ctx.lineCap = "round"; ctx.strokeStyle = INK; ctx.lineWidth = 2.6; ctx.beginPath(); ctx.moveTo(-14, 0); ctx.lineTo(14, 0); ctx.stroke();
      ctx.strokeStyle = WOOD; ctx.lineWidth = 1.4; ctx.stroke();
      ctx.beginPath(); ctx.moveTo(-13, 0); ctx.lineTo(-13, 15); ctx.lineTo(-7.5, 11); ctx.lineTo(-2, 15); ctx.lineTo(-2, 0); ctx.closePath();
      ctx.fillStyle = mix(u.col, "#7a7466", 0.35); ctx.fill(); ctx.strokeStyle = INK; ctx.lineWidth = 1.2; ctx.stroke();
      ctx.restore();
    }
  });
}

// Знамя отряда (Г7): в центре строя, ближе к первой шеренге; рисуется в пикселях — видно с любого приближения.
// Бегство (В11): знамя упало (лежит на земле); приказ «сплотить» ждёт или только что сплотились — знамя поднято и машет
function drawBanner(f, h, depth, col, ui){
  const fi = Math.min(Math.floor(frame), S.frames.length - 1), st = ui === undefined ? 0 : stateAt(ui, fi);
  if(st === 1 || st === 2) return;
  const wave = st === 3 || (st === 4 && (fi - stateSince(ui, fi)) * S.dt < 4), t = frame * S.dt;
  const bx = X(f[0] + Math.sin(h) * depth * 0.2), by = Y(f[1] - Math.cos(h) * depth * 0.2);
  const s = Math.min(1.6, Math.max(0.8, view.s / 12));
  ctx.save(); ctx.translate(bx, by); ctx.scale(s, s);
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  ctx.strokeStyle = INK; ctx.lineWidth = 2.6; ctx.beginPath(); ctx.moveTo(0, 2); ctx.lineTo(0, -26); ctx.stroke();
  ctx.strokeStyle = WOOD; ctx.lineWidth = 1.4; ctx.stroke();
  const wy = x => wave ? Math.sin(t * 7 - x * 0.35) * 2.2 * x / 17 : 0;   // полотнище машет, у древка неподвижно
  ctx.beginPath(); ctx.moveTo(0, -25); ctx.lineTo(8.5, -25 + wy(8.5)); ctx.lineTo(17, -25 + wy(17)); ctx.lineTo(13, -19.5 + wy(13)); ctx.lineTo(17, -14 + wy(17)); ctx.lineTo(8.5, -14 + wy(8.5)); ctx.lineTo(0, -14); ctx.closePath();
  ctx.fillStyle = col; ctx.fill(); ctx.strokeStyle = INK; ctx.lineWidth = 1.2; ctx.stroke();
  ctx.fillStyle = "rgba(255,255,255,.55)"; ctx.fillRect(1.5, -21, 9, 2.2);
  ctx.restore();
}
