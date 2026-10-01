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
    const k = {look, horse, cloth, armour: pickW(K.armour, R(13)), leather: LEATHER[Math.floor(R(14) * 3)], helm, helmCol,
      crest: helm === "great" && R(15) < 0.35 ? u.col : null, back, backCol: back === "pavise" ? u.col : shade(u.col, -0.3),
      weapon: pickW(K.weapon, R(16)), side: K.side ? pickW(K.side, R(17)) : "none", shield, col: u.col, c2,
      coat: COATS[Math.floor(R(18) * COATS.length)], bard: K.bard ? pickW(K.bard, R(19)) : "none"};
    k.bodyKey = [k.cloth, k.armour, k.leather, k.helm, k.helmCol, k.crest, k.back, k.backCol, c2].join(",");
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
function pill(g, col){
  g.beginPath(); g.moveTo(-0.14, -0.11); g.lineTo(0.14, -0.11); g.arc(0.14, 0.02, 0.13, -Math.PI / 2, Math.PI / 2);
  g.lineTo(-0.14, 0.15); g.arc(-0.14, 0.02, 0.13, Math.PI / 2, Math.PI * 1.5); g.closePath();
  g.fillStyle = col; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.045; g.stroke();
}
function folds(g, col){   // складки рукавов — штрихи веером у концов плеч
  g.beginPath();
  for(const s of [-1, 1]) for(const a of [-0.65, 0, 0.65]){
    const c = Math.cos(a), sn = Math.sin(a);
    g.moveTo(s * (0.14 + c * 0.045), 0.02 + sn * 0.045); g.lineTo(s * (0.14 + c * 0.11), 0.02 + sn * 0.11);
  }
  g.strokeStyle = shade(col, -0.42); g.lineWidth = 0.022; g.stroke();
}
function paintHead(g, k){
  const c = k.helmCol;
  switch(k.helm){
    case "hair":
      ell(g, 0, 0, 0.112, 0.118, c);
      g.beginPath(); g.moveTo(0, -0.1); g.lineTo(0.01, 0.09); g.strokeStyle = shade(c, -0.35); g.lineWidth = 0.016; g.stroke(); break;
    case "cap":
      ell(g, 0, 0, 0.12, 0.12, c); ell(g, 0, -0.025, 0.06, 0.05, shade(c, 0.15), 0, false); break;
    case "hood":
      g.beginPath(); g.moveTo(0.122, 0); g.arc(0, 0, 0.122, 0, Math.PI, true); g.quadraticCurveTo(-0.08, 0.16, 0, 0.25); g.quadraticCurveTo(0.08, 0.16, 0.122, 0);
      g.fillStyle = c; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.035; g.stroke(); break;
    case "kettle":   // шапель: широкие поля, купол — кольцо, как у Iron Kings
      ell(g, 0, 0, 0.165, 0.16, STEEL); ell(g, 0, 0, 0.092, 0.092, shade(STEEL, 0.12));
      ell(g, -0.03, -0.03, 0.03, 0.03, SHINE, 0, false); break;
    case "capSteel":
      ell(g, 0, 0, 0.12, 0.12, STEEL);
      g.beginPath(); g.arc(0, 0, 0.072, 0, 6.283); g.strokeStyle = STEEL_D; g.lineWidth = 0.022; g.stroke();
      ell(g, -0.035, -0.035, 0.035, 0.035, SHINE, 0, false); break;
    case "nasal":
      ell(g, 0, 0, 0.118, 0.124, STEEL);
      g.beginPath(); g.moveTo(0, -0.12); g.lineTo(0, 0.11); g.moveTo(0, -0.125); g.lineTo(0, -0.16); g.strokeStyle = STEEL_D; g.lineWidth = 0.025; g.stroke();
      ell(g, -0.04, -0.03, 0.03, 0.035, SHINE, 0, false); break;
    case "great":    // топфхельм: плоский верх с ободом, у некоторых — гребень цвета отряда
      ell(g, 0, 0, 0.13, 0.13, "#8f9498");
      g.beginPath(); g.arc(0, 0, 0.098, 0, 6.283); g.strokeStyle = "#62676c"; g.lineWidth = 0.02; g.stroke();
      if(k.crest) ell(g, 0, 0, 0.032, 0.11, k.crest); else ell(g, -0.04, -0.04, 0.03, 0.03, "#c9cdd0", 0, false); break;
    case "bascinet": // бацинет с «клювом» забрала вперёд
      ell(g, 0, 0.01, 0.118, 0.13, STEEL); ell(g, 0, -0.14, 0.05, 0.065, STEEL_D);
      ell(g, -0.04, -0.02, 0.03, 0.035, SHINE, 0, false); break;
    case "morion":   // морион: поля лодочкой спереди назад, гребень
      ell(g, 0, 0, 0.125, 0.2, STEEL);
      g.beginPath(); g.moveTo(0, -0.19); g.lineTo(0, 0.19); g.strokeStyle = STEEL_D; g.lineWidth = 0.035; g.stroke();
      ell(g, -0.05, -0.05, 0.025, 0.04, SHINE, 0, false); break;
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
function paintBody(g, k){
  backItem(g, k);
  pill(g, k.cloth);
  if(k.armour === "mail"){
    for(const s of [-1, 1]){
      ell(g, s * 0.19, 0.02, 0.09, 0.122, "#9a9ea2");
      g.fillStyle = "#6f7377";
      for(let q = 0; q < 6; q++){ g.beginPath(); g.arc(s * (0.165 + 0.035 * (q % 2)), -0.07 + q * 0.035, 0.009, 0, 6.283); g.fill(); }
    }
  } else if(k.armour === "leather"){ for(const s of [-1, 1]) ell(g, s * 0.19, 0.02, 0.09, 0.122, k.leather); }
  else if(k.armour === "plate"){ for(const s of [-1, 1]){ ell(g, s * 0.2, 0.0, 0.1, 0.125, STEEL); ell(g, s * 0.2 - 0.025, -0.035, 0.035, 0.04, SHINE, 0, false); } }
  else folds(g, k.cloth);
  paintHead(g, k);
}
function shieldPath(g, shape){
  g.beginPath();
  if(shape === "heater"){ g.moveTo(-0.2, -0.24); g.lineTo(0.2, -0.24); g.quadraticCurveTo(0.21, 0.08, 0, 0.29); g.quadraticCurveTo(-0.21, 0.08, -0.2, -0.24); g.closePath(); }
  else if(shape === "oval") g.ellipse(0, 0, 0.19, 0.28, 0, 0, 6.283);
  else g.arc(0, 0, shape === "buckler" ? 0.13 : 0.24, 0, 6.283);
}
function paintShield(g, sh){
  shieldPath(g, sh.shape);
  g.fillStyle = sh.paint === "wood" ? "#9a7a50" : sh.paint === "steel" ? STEEL : sh.c1; g.fill();
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
  g.restore();
  shieldPath(g, sh.shape); g.strokeStyle = INK; g.lineWidth = 0.035; g.stroke();
  if(sh.shape !== "heater"){ ell(g, 0, 0, 0.05, 0.05, STEEL); ell(g, -0.012, -0.012, 0.018, 0.018, SHINE, 0, false); }
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
// арбалет — в осях бойца: 0 взведён, болт на ложе; 1 разряжен
function paintXbow(g, st){
  stick(g, 0.05, 0.02, 0.05, -0.5, 0.06, WOOD);
  g.beginPath(); g.moveTo(-0.22, -0.45); g.lineTo(0.05, st ? -0.45 : -0.3); g.lineTo(0.32, -0.45); g.strokeStyle = "#efe9dc"; g.lineWidth = 0.014; g.stroke();
  if(!st){ stick(g, 0.05, -0.29, 0.05, -0.6, 0.016, "#d8cdb5"); tip(g, 0.05, -0.6, 0.06); }
  g.beginPath(); g.moveTo(-0.22, -0.45); g.quadraticCurveTo(0.05, -0.58, 0.32, -0.45);
  g.strokeStyle = INK; g.lineWidth = 0.07; g.stroke(); g.strokeStyle = STEEL; g.lineWidth = 0.04; g.stroke();
}
// конь: кадр галопа 0…3 (ноги вперёд-назад, кивок головой, хвост), −1 — стоит
function paintHorse(g, k, f){
  const hc = k.coat, dark = shade(hc, -0.4), ph = f * Math.PI / 2, run = f >= 0 ? 1 : 0, nod = -0.05 * Math.cos(ph) * run;
  for(const [lx, ly, o] of [[-0.15, -0.44, 0], [0.15, -0.44, 1.2], [-0.15, 0.64, 2.6], [0.15, 0.64, 3.8]])
    ell(g, lx, ly - Math.sin(ph + o) * 0.17 * run, 0.055, 0.13, dark);                          // ноги из-под туловища
  g.beginPath(); for(const s of [-1, 0, 1]){ g.moveTo(s * 0.03, 0.84); g.quadraticCurveTo(s * 0.09 + Math.sin(ph) * 0.06 * run, 1.06, s * 0.06 + 0.02 + Math.sin(ph + 1) * 0.1 * run, 1.3); }
  g.strokeStyle = INK; g.lineWidth = 0.075; g.stroke(); g.strokeStyle = dark; g.lineWidth = 0.04; g.stroke();   // хвост
  ell(g, 0, 0.12, 0.22, 0.8, hc);                                                                // туловище
  const head = k.bard === "full" ? STEEL : hc;
  g.save(); g.translate(0, nod);
  g.beginPath(); g.moveTo(-0.1, -0.5); g.quadraticCurveTo(-0.13, -0.9, -0.065, -1.2);             // шея и голова
  g.quadraticCurveTo(0, -1.28, 0.065, -1.2); g.quadraticCurveTo(0.13, -0.9, 0.1, -0.5); g.closePath();
  g.fillStyle = head; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.04; g.stroke();
  ell(g, -0.06, -0.98, 0.025, 0.05, head); ell(g, 0.06, -0.98, 0.025, 0.05, head);               // уши
  if(k.bard !== "full") stick(g, 0, -0.48, 0, -0.94, 0.04, dark);                                // грива
  g.restore();
  if(k.bard === "full"){ ell(g, 0, 0.14, 0.26, 0.74, k.col); ell(g, 0, 0.14, 0.2, 0.66, shade(k.col, 0.12), 0, false);
    g.fillStyle = k.c2; g.fillRect(-0.025, -0.55, 0.05, 1.35); }                                   // попона до копыт, полоса герба
  else if(k.bard === "cloth") ell(g, 0, 0.08, 0.27, 0.34, shade(k.col, -0.15));                 // чепрак
  else ell(g, 0, 0.08, 0.17, 0.22, LEATHER[0]);                                                  // седло
}
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
  const sp = 0.05 + 0.12 * r(1);
  stick(g, -0.07, 0.1, -0.07 - sp, 0.78, 0.1, pants); ell(g, -0.07 - sp * 1.06, 0.83, 0.05, 0.07, boot);
  stick(g, 0.07, 0.1, 0.07 + sp * 0.7, 0.8, 0.1, pants); ell(g, 0.07 + sp * 0.74, 0.85, 0.05, 0.07, boot);
  const la = -0.3 - 1.2 * r(2), ra = 0.3 + 1.2 * r(3);
  for(const [sx, a] of [[-0.19, la], [0.19, ra]]){
    const hx = sx + Math.sin(a) * 0.46, hy = -0.3 + Math.cos(a) * 0.46;
    stick(g, sx, -0.3, hx, hy, 0.09, sleeve); ell(g, hx, hy, 0.035, 0.035, "#c49a74");
  }
  g.beginPath(); g.moveTo(-0.21, -0.36); g.quadraticCurveTo(-0.22, -0.44, -0.12, -0.44); g.lineTo(0.12, -0.44); g.quadraticCurveTo(0.22, -0.44, 0.21, -0.36);
  g.lineTo(0.17, 0.14); g.lineTo(-0.17, 0.14); g.closePath(); g.fillStyle = k.cloth; g.fill(); g.strokeStyle = INK; g.lineWidth = 0.04; g.stroke();
  g.save(); g.translate(0, -0.56); paintHead(g, k); g.restore();
  g.globalCompositeOperation = "source-atop"; g.fillStyle = "rgba(112,106,96,.38)"; g.fillRect(-2, -2, 4, 4); g.globalCompositeOperation = "source-over";
}
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
const bootSpr = () => spr("boot", [-0.06, -0.08, 0.06, 0.08], g => ell(g, 0, 0, 0.045, 0.07, "#3a2c20", 0, false));
const horseSpr = (k, f) => spr("h" + k.horseKey + "|" + f, [-0.36, -1.36, 0.36, 1.37], g => paintHorse(g, k, f));
const corpseSpr = (k, v) => spr("c" + k.bodyKey + (k.shield ? k.shield.key : "") + k.weapon + k.side + v, [-1.05, -1.0, 1.05, 1.0], g => paintCorpse(g, k, v));
const deadHorseSpr = k => spr("dh" + k.horseKey, [-0.5, -1.35, 0.75, 1.0], g => paintDeadHorse(g, k));
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
function restPose(k, rank){
  const w = k.weapon;
  let W = null, Sh = null;
  if(w === "spear" || w === "fork") W = rank < 2 ? [0.2, -0.1, 0, 1, 1] : [0.21, -0.06, 0.15, 1, 0.2];
  else if(w === "pike") W = rank < 4 ? [rank % 2 ? -0.3 : 0.12, 0, 0, 1, 1] : [0.15, -0.04, 0.1, 1, 0.12];
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
    paintBody(g, k);
    const at = (T, fn) => { g.save(); g.translate(T[0], T[1]); g.rotate(T[2]); g.scale(T[3], T[4]); fn(); g.restore(); };
    if(P.Sh) at(P.Sh, () => paintShield(g, k.shield));
    if(k.weapon === "bow") paintBow(g, 0); else if(k.weapon === "crossbow") paintXbow(g, 0);
    else if(P.W) at(P.W, () => paintWeapon(g, k.weapon, k.col));
  });
}

// ── Матрицы: [a, b, c, d, e, f], как у canvas ──
const mT = (m, x, y) => [m[0], m[1], m[2], m[3], m[4] + m[0] * x + m[2] * y, m[5] + m[1] * x + m[3] * y];
function mR(m, r){ const c = Math.cos(r), s = Math.sin(r); return [m[0] * c + m[2] * s, m[1] * c + m[3] * s, m[2] * c - m[0] * s, m[3] * c - m[1] * s, m[4], m[5]]; }
const mS = (m, x, y) => [m[0] * x, m[1] * x, m[2] * y, m[3] * y, m[4], m[5]];
const mP = (m, T) => mS(mR(mT(m, T[0], T[1]), T[2]), T[3], T[4]);
function put(sp, m){ ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]); ctx.drawImage(sp.c, sp.x, sp.y, sp.w, sp.h); }

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
const THRUST = new Set(["spear", "fork", "pike", "lance"]);
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

// ── Фигурка: бойцы по сетке pm × rd, спереди назад; в неполном ряду — по центру ──
function drawMen(u, [w, d, men, rank], fx, fy, h, col, moving, t, k){
  const pm = u.pm || 1, rd = u.rd || 1, look = lookOf(u), horse = look === "lance" || look === "barded";
  const cols = Math.max(1, Math.round(w / pm)), rows = Math.max(1, Math.round(d / rd));
  if(!u.fd) u.fd = Math.max(...u.figs.map(q => Math.round(q[1] / rd)));
  setZoom(); prepFrameMen();
  const kits = kitsOf(u), parts = view.s >= PARTS_FROM, ui = S.units.indexOf(u);
  const eng = MEL.get(ui) ? MEL.get(ui).get(k) : undefined, fire = FIRE.has(ui * 100000 + k);
  ctx.save(); ctx.translate(X(fx), Y(fy)); ctx.rotate(h); ctx.scale(view.s, view.s);
  const T0 = ctx.getTransform(), B = [T0.a, T0.b, T0.c, T0.d, T0.e, T0.f];
  const M = [];
  let left = Math.round(men ?? cols * rows);
  for(let j = 0; j < rows && left > 0; j++){
    const n = Math.min(cols, left), off = (cols - n) / 2; left -= n;
    const manRank = (rank ?? 0) * u.fd + j;
    for(let i = 0; i < n; i++){
      const seed = u.id * 7919 + k * 64 + j * 8 + i;
      const r1 = hash(seed, 1), r2 = hash(seed, 2);
      M.push({x: -w / 2 + (off + i + 0.5) * pm + (r1 - 0.5) * 0.12 * pm, y: -d / 2 + (j + 0.5) * rd + (r2 - 0.5) * 0.12 * rd,
        seed, rank: manRank, kit: kits[Math.floor(hash(seed, 4) * kits.length)]});
    }
  }
  if(!parts){   // издали: готовый спрайт, лёгкое покачивание на ходу
    for(const m of M){ const bob = moving ? Math.sin(t * 11 + hash(m.seed, 3) * 6.283) * 0.05 : 0; put(compSpr(m.kit, m.rank), mT(B, m.x, m.y + bob)); }
    ctx.restore(); return;
  }
  // рукопашная: кто ближе к врагу — бьёт, остальные напирают и смотрят туда же
  if(eng !== undefined){
    const al = eng - h, ca = Math.cos(al), sa = Math.sin(al);
    let top = -1e9; for(const m of M) top = Math.max(top, m.x * ca + m.y * sa);
    const face = Math.atan2(Math.sin(al + Math.PI / 2), Math.cos(al + Math.PI / 2));
    for(const m of M){ m.atk = m.x * ca + m.y * sa > top - 1.6 * rd; m.face = m.atk ? face : face * 0.5; }
  }
  // стрельба (В6): стрелок, ближайший к точке вылета стрелы, натягивает лук перед ней и отпускает в миг вылета
  const shoot = look === "bow" || look === "crossbow";
  let active = false;
  if(shoot && S.arrows){
    const A = S.arrows, idx = S._arrU[ui], ch = Math.cos(h), sh = Math.sin(h), fr = Math.hypot(w, d) / 2 + 1.5;
    const back = look === "crossbow" ? 2.6 : 0.3;
    const i0 = lowerT0(A, idx, t - back);
    active = lowerT0(A, idx, t - 3) < idx.length && A[idx[lowerT0(A, idx, t - 3)]][0] < t + 3;
    for(let q = i0; q < idx.length && A[idx[q]][0] <= t + 0.8; q++){
      const a = A[idx[q]], dx = a[1] - fx, dy = a[2] - fy;
      if(Math.abs(dx) > fr || Math.abs(dy) > fr) continue;
      const lx = dx * ch + dy * sh, ly = -dx * sh + dy * ch;
      let best = null, bd = 1.5;
      for(const m of M){ const dd = Math.hypot(m.x - lx, m.y - ly); if(dd < bd){ bd = dd; best = m; } }
      if(best && (best.shot === undefined || Math.abs(t - a[0]) < Math.abs(best.shot))) best.shot = t - a[0];
    }
  }
  // тени — до бойцов, чтобы не ложились на соседа; смещение влево вниз в мире, в осях строя — повёрнуто обратно
  {
    const so = horse ? 0.45 : 0.2, wx = SH_X * so, wy = SH_Y * so, ch = Math.cos(h), sh = Math.sin(h);
    const lx = wx * ch + wy * sh, ly = -wx * sh + wy * ch, sw = 0.66, shh = horse ? 2.0 : 0.48, sy = horse ? 0.12 : 0.02;
    for(const m of M) put({c: SHADOW, x: -sw / 2, y: -shh / 2, w: sw, h: shh}, mT(B, m.x + lx, m.y + sy + ly));
  }
  const freq = 1.6 + Math.min(1, (u.norm || 100) / 250);   // шагов в секунду: конница чаще
  for(const m of M){
    const kit = m.kit, s = m.seed, ph0 = hash(s, 5);
    let rot = (m.face || 0), ox = 0, oy = 0, step = 0;
    let ap = -1;   // доля круга удара
    if(m.atk){ ap = frac(t / (1.1 + 0.9 * hash(s, 7)) + hash(s, 8)); }
    if(moving && !m.atk){ const ph = (t * freq + ph0) * 6.283; step = Math.sin(ph); rot += 0.07 * step; }
    else if(!m.atk){ rot += 0.035 * Math.sin(t * 0.9 + ph0 * 6.283) + (m.face === undefined ? glance(t, s) : 0); ox = 0.02 * Math.sin(t * 0.6 + ph0 * 9); }
    if(eng !== undefined && !m.atk) oy = -0.03 - 0.03 * Math.sin(t * 3 + ph0 * 6.283);          // задние напирают
    const P = restPose(kit, m.rank);
    if(horse){
      const run = moving || m.atk, gp = t * 2.4 + ph0;
      const Mm = mR(mT(B, m.x, m.y), 0);
      put(horseSpr(kit, run ? Math.floor(frac(gp) * 4) : -1), Mm);
      const Mr = mR(mT(Mm, ox, 0.02 + (run ? Math.sin(gp * 12.566) * 0.03 : 0)), rot * 0.35);
      put(bodySpr(kit), Mr);
      if(P.Sh) put(shieldSpr(kit.shield), mP(Mr, P.Sh));
      let W = P.W;
      if(kit.weapon === "lance"){ if(run) W = [0.2, 0.25 + (m.atk ? thrustOff(ap) : 0), -0.04, 1, 1]; }
      else if(m.atk){ const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
      if(W) put(weapSpr(kit.weapon, kit.col), mP(Mr, W));
      continue;
    }
    // стрелок: состояние лука по времени до своего выстрела
    let bowSt = 0, xb = 0, lean = 0;
    if(shoot && !m.atk){
      if(m.shot !== undefined){
        const dt = m.shot;
        if(look === "bow"){ bowSt = dt < -0.45 ? 1 : dt < -0.2 ? 2 : dt < 0 ? 3 : dt < 0.22 ? 4 : 1; }
        else { xb = dt < 0 ? 0 : dt < 0.25 ? 1 : 2; }
      } else if(active){ bowSt = hash(s, 14) < 0.5 ? 1 : 0; xb = hash(s, 14) < 0.5 ? 0 : 2; }
      if(bowSt >= 2) rot -= 0.14;
      if(xb === 2){ lean = 0.05; rot += 0.15 * Math.sin(t * 5 + ph0 * 6); }
      if(xb === 1) oy += 0.04;
    }
    if(m.atk && ap >= 0){ const strike = ap > 0.3 && ap < 0.55 ? 1 : 0; oy -= 0.06 * strike; rot += THRUST.has(kit.weapon) ? 0 : 0.18 * Math.sin(ap * 6.283); }
    const Mm = mR(mT(B, m.x + ox, m.y + oy - lean), rot);
    // ноги: на ходу и в бою шагают
    const st = step || (m.atk ? Math.sin(ap * 6.283) * 0.6 : 0);
    if(st && view.s >= 10){ const bt = bootSpr(); put(bt, mT(Mm, -0.09, 0.03 + 0.12 * st)); put(bt, mT(Mm, 0.09, 0.03 - 0.12 * st)); }
    put(bodySpr(kit), Mm);
    // щит: под стрелами — над головой
    if(P.Sh){
      let Sh = P.Sh;
      if(fire && !m.atk && kit.shield.shape !== "buckler" && hash(s, 13) < 0.85) Sh = [-0.03, -0.05, -0.1, 1, 0.9];
      else if(m.atk) Sh = [Sh[0] + 0.04, Sh[1] - 0.06, Sh[2] + 0.15, Sh[3], Sh[4]];
      put(shieldSpr(kit.shield), mP(Mm, Sh));
    }
    // оружие
    let wpn = kit.weapon, W = P.W;
    if(m.atk && shoot){ wpn = kit.side !== "none" ? kit.side : null; W = wpn ? [0.22, -0.06, 0.3, 1, 0.65] : null; }
    if(m.atk && wpn){
      if(THRUST.has(wpn)) W = [W[0], (wpn === "pike" ? 0 : -0.1) + thrustOff(ap), 0, 1, 1];
      else { const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
    } else if(W && step) W = [W[0], W[1], W[2] + 0.03 * step, W[3], W[4]];
    if(wpn === "bow" && !m.atk) put(bowSpr(bowSt), Mm);
    else if(wpn === "crossbow" && !m.atk) put(xbowSpr(xb ? 1 : 0), xb === 2 ? mP(Mm, [0.0, 0.22, 0.5, 1, 0.55]) : Mm);
    else if(W && wpn) put(weapSpr(wpn, kit.col), mP(Mm, W));
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
  for(const [x, y, fi, ui, fc, dir, part] of S.dead){
    if(fi > now) continue;
    const px = X(x), py = Y(y);
    if(!near){ ctx.fillRect(px - 0.8, py - 0.8, 1.6, 1.6); continue; }
    if(x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
    const seed = (fi * 131 + ui * 7919 + Math.round(x * 13) + Math.round(y * 7)) | 0;
    const g = Math.min(1, 0.25 + (now - fi) * S.dt / 1.2), a = dir * Math.PI / 180, ca = Math.cos(a), sa = Math.sin(a);
    const big = part === 3 ? 1.7 : part === 0 ? 0.65 : part === 2 ? 0.85 : 1;
    for(let k = 0; k < 3; k++){
      const rr = (0.15 + 0.13 * hash(seed, k)) * big * g * view.s, off = (0.3 + 0.35 * hash(seed, k + 7)) * g;   // лужа — под телом, ближе к груди
      const ox = (ca * off + (hash(seed, k + 3) - 0.5) * 0.3) * view.s, oy = (sa * off + (hash(seed, k + 5) - 0.5) * 0.3) * view.s;
      ctx.beginPath(); ctx.ellipse(px + ox, py + oy, rr, rr * (0.65 + 0.35 * hash(seed, k + 9)), a, 0, 6.283); ctx.fill();
    }
    const n = 2 + Math.floor(hash(seed, 11) * 4);
    for(let k = 0; k < n; k++){
      const dd = (0.55 + 1.3 * hash(seed, 20 + k)) * g, sp = (hash(seed, 30 + k) - 0.5) * 0.8;
      ctx.beginPath(); ctx.arc(px + Math.cos(a + sp) * dd * view.s, py + Math.sin(a + sp) * dd * view.s, Math.max(0.6, (0.05 + 0.08 * hash(seed, 40 + k)) * view.s), 0, 6.283); ctx.fill();
    }
  }
  if(!near) return;
  for(const [x, y, fi, ui, fc, dir, part] of S.dead){
    if(fi > now || x < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
    const u = S.units[ui], kits = kitsOf(u), seed = (fi * 131 + ui * 7919 + Math.round(x * 13) + Math.round(y * 7)) | 0;
    const kit = kits[Math.floor(hash(seed, 50) * kits.length)], v = Math.floor(hash(seed, 51) * 4);
    const p = Math.min(1, (t - fi * S.dt) / 0.35), e = 1 - (1 - p) * (1 - p), a = dir * Math.PI / 180 + (hash(seed, 52) - 0.5) * 0.5;
    ctx.save(); ctx.translate(X(x), Y(y)); ctx.rotate(a + Math.PI / 2); ctx.scale(view.s, view.s);
    if(part === 3 && kit.horse){
      const hs = deadHorseSpr(kit); ctx.save(); ctx.scale(1, 0.4 + 0.6 * e); ctx.drawImage(hs.c, hs.x, hs.y - 0.6, hs.w, hs.h); ctx.restore();
      ctx.translate(-0.9, 0.3);
    }
    // падение: тело «ложится» от ног — растягиваем вдоль оси от точки, где стоял
    ctx.scale(1, 0.25 + 0.75 * e);
    const sp = corpseSpr(kit, v); ctx.drawImage(sp.c, sp.x, sp.y - 0.8, sp.w, sp.h);
    if(part === 0 && p >= 1){ ctx.fillStyle = BLOOD; ctx.globalAlpha = 0.8; ctx.beginPath(); ctx.arc(0, -1.36, 0.1, 0, 6.283); ctx.fill(); }
    ctx.restore();
  }
  drawStuck(r, t);
}

// Знамя отряда (Г7): в центре строя, ближе к первой шеренге; рисуется в пикселях — видно с любого приближения
function drawBanner(f, h, depth, col){
  const bx = X(f[0] + Math.sin(h) * depth * 0.2), by = Y(f[1] - Math.cos(h) * depth * 0.2);
  const s = Math.min(1.6, Math.max(0.8, view.s / 12));
  ctx.save(); ctx.translate(bx, by); ctx.scale(s, s);
  ctx.lineCap = "round"; ctx.lineJoin = "round";
  ctx.strokeStyle = INK; ctx.lineWidth = 2.6; ctx.beginPath(); ctx.moveTo(0, 2); ctx.lineTo(0, -26); ctx.stroke();
  ctx.strokeStyle = WOOD; ctx.lineWidth = 1.4; ctx.stroke();
  ctx.beginPath(); ctx.moveTo(0, -25); ctx.lineTo(17, -25); ctx.lineTo(13, -19.5); ctx.lineTo(17, -14); ctx.lineTo(0, -14); ctx.closePath();
  ctx.fillStyle = col; ctx.fill(); ctx.strokeStyle = INK; ctx.lineWidth = 1.2; ctx.stroke();
  ctx.fillStyle = "rgba(255,255,255,.55)"; ctx.fillRect(1.5, -21, 9, 2.2);
  ctx.restore();
}
