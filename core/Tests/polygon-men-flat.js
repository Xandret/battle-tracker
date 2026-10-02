// ═══════════ polygon-men-flat.js — бойцы «как у Iron Kings» (В18): строго сверху, плоско ═══════════
// Проба для men-flat.html. Подключается после polygon-men.js и берёт из него таблицы снаряжения (KIT), позы оружия
// (restPose, handsOf, удары), походку коня (horsePose), спрайты и матрицы (spr, put, mT/mR/mS/mP). Рисует по-своему:
// капсула плеч 2 : 1 с двумя линиями и косыми штрихами, круглая голова или шлем, тонкий тёмно-коричневый контур,
// плоская заливка, мягкая тень. Обмер образца — docs/iron-kings.md, раздел 8.
// Оси — как в polygon-men.js: метры, вперёд — −y; у бойца начало — середина плеч, у коня — седло.

const F_INK = "#2c1f19", F_INK_A = "rgba(44,31,25,.5)";
const F_A = 0.25, F_B = 0.125;                 // капсула: полудлина (плечи 0,5 м), полуглубина
const F_HEAD = [0, -0.03, 0.106];              // голова: центр (чуть вперёд) и радиус — 0,85 ширины капсулы
const F_STEEL = "#b4b9bc", F_STEEL_D = "#868c90", F_SHINE = "#f1f3f4", F_MAIL = "#a2a7aa", F_MAIL_D = "#777c80";
const F_WOOD = "#8f6c44", F_BOOT = "#3d2e23", F_BRASS = "#c9a24a", F_LACQ = "#2b2422";
const F_HAND = {skin: "#d6a67e", glove: "#6a5038", plate: F_STEEL};
const F_ARMC = {mail: F_MAIL, plate: F_STEEL, scale: "#a9a48d", lamellar: "#aeb3b5", oyoroi: "#2e2826", dou: "#3b302a"};

// ═══ Стили снаряжения (В16): у стиля — свои наборы; чего в стиле нет — как у западного (KIT в polygon-men.js) ═══
const F_STYLE = {
  west: {pike: {weapon: [["pike", 70], ["halberd", 30]]}},
  north: {   // Скандинавия, Русь, Балтика: шишаки, круглые и каплевидные щиты, кольчуга и чешуя, топоры, плащи
    militia: {helm: [["hair", 45], ["hood", 25], ["cap", 20], ["shishak", 10]], weapon: [["spear", 45], ["axe", 35], ["club", 10], ["sword", 10]],
      shield: [["round", 80], ["none", 20]], paint: [["wood", 40], ["plain", 30], ["halves", 15], ["boss", 15]]},
    spear: {helm: [["shishak", 45], ["nasal", 35], ["capSteel", 20]], weapon: [["spear", 75], ["axe", 25]], shield: [["round", 60], ["kite", 40]],
      paint: [["plain", 30], ["boss", 30], ["halves", 20], ["stripe", 20]], armour: [["mail", 45], ["scale", 25], ["cloth", 30]], back: [["none", 70], ["roll", 30]]},
    sword: {helm: [["shishak", 55], ["nasal", 45]], weapon: [["daneaxe", 45], ["sword", 35], ["axe", 20]], shield: [["kite", 50], ["round", 50]],
      paint: [["halves", 25], ["quarters", 25], ["cross", 25], ["stripe", 25]], armour: [["mail", 60], ["scale", 40]], back: [["none", 100]]},
    pike: {helm: [["shishak", 50], ["capSteel", 30], ["hair", 20]], armour: [["cloth", 50], ["scale", 25], ["mail", 25]]},
    bow: {helm: [["hood", 30], ["cap", 30], ["shishak", 20], ["hair", 20]], side: [["axe", 60], ["none", 40]]},
    crossbow: {helm: [["kettle", 40], ["shishak", 40], ["cap", 20]]},
    lance: {helm: [["shishak", 60], ["nasal", 40]], weapon: [["lance", 70], ["sword", 15], ["axe", 15]], shield: [["kite", 70], ["round", 30]],
      armour: [["mail", 60], ["scale", 40]], bard: [["none", 70], ["cloth", 30]]},
    barded: {helm: [["shishak", 100]], armour: [["scale", 100]], shield: [["kite", 100]], bard: [["full", 100]]},
  },
  east: {    // Византия, степь, Персия: ламелляр, остроконечные шлемы с бармицей, круглые щиты, сабли, составные луки
    militia: {helm: [["turban", 35], ["cap", 30], ["hair", 20], ["pointed", 15]], weapon: [["spear", 50], ["sabre", 20], ["mace", 15], ["club", 15]],
      shield: [["round", 60], ["none", 40]], paint: [["plain", 50], ["boss", 30], ["wood", 20]], back: [["none", 60], ["bag", 40]]},
    spear: {helm: [["pointed", 60], ["turban", 20], ["capSteel", 20]], weapon: [["spear", 80], ["sabre", 10], ["mace", 10]], shield: [["round", 100]],
      paint: [["boss", 40], ["plain", 30], ["stripe", 30]], armour: [["lamellar", 50], ["leather", 25], ["cloth", 25]]},
    sword: {helm: [["pointed", 100]], weapon: [["sabre", 60], ["mace", 40]], shield: [["round", 100]], paint: [["boss", 50], ["halves", 25], ["stripe", 25]],
      armour: [["lamellar", 70], ["mail", 30]], back: [["none", 100]]},
    pike: {helm: [["pointed", 50], ["turban", 30], ["cap", 20]], armour: [["cloth", 60], ["lamellar", 40]]},
    bow: {helm: [["pointed", 35], ["cap", 35], ["turban", 30]], weapon: [["recurve", 100]], side: [["sabre", 60], ["none", 40]], armour: [["cloth", 50], ["leather", 30], ["lamellar", 20]]},
    crossbow: {helm: [["pointed", 50], ["turban", 50]], back: [["pavise", 30], ["none", 70]], armour: [["cloth", 50], ["lamellar", 50]]},
    lance: {helm: [["pointed", 100]], weapon: [["lance", 50], ["sabre", 30], ["mace", 20]], shield: [["round", 100]], paint: [["boss", 50], ["plain", 50]],
      armour: [["lamellar", 70], ["mail", 30]], bard: [["none", 60], ["cloth", 40]]},
    barded: {helm: [["pointed", 100]], armour: [["lamellar", 100]], shield: [["round", 100]], paint: [["boss", 100]], bard: [["lamellar", 100]]},
  },
  south: {   // Италия, Иберия, мавры: бригантины, салады и барбюты, адарги, дротики
    militia: {helm: [["hair", 30], ["cap", 25], ["turban", 25], ["kettle", 20]], weapon: [["javelin", 35], ["spear", 30], ["fork", 10], ["club", 10], ["sword", 15]],
      shield: [["adarga", 35], ["round", 25], ["none", 40]], paint: [["plain", 50], ["halves", 25], ["wood", 25]]},
    spear: {helm: [["barbute", 35], ["kettle", 35], ["sallet", 30]], weapon: [["spear", 70], ["javelin", 30]], shield: [["adarga", 30], ["heater", 40], ["round", 30]],
      paint: [["plain", 30], ["halves", 25], ["stripe", 25], ["quarters", 20]], armour: [["leather", 55], ["mail", 25], ["cloth", 20]]},
    sword: {helm: [["sallet", 50], ["barbute", 50]], weapon: [["sword", 70], ["falchion", 30]], shield: [["heater", 50], ["adarga", 30], ["buckler", 20]],
      armour: [["leather", 50], ["plate", 50]], back: [["none", 100]]},
    pike: {helm: [["sallet", 40], ["kettle", 40], ["barbute", 20]], weapon: [["pike", 60], ["halberd", 40]], armour: [["leather", 50], ["cloth", 50]]},
    bow: {helm: [["cap", 40], ["turban", 30], ["kettle", 30]]},
    crossbow: {helm: [["kettle", 50], ["sallet", 50]], armour: [["leather", 50], ["mail", 30], ["cloth", 20]]},
    lance: {helm: [["sallet", 50], ["barbute", 30], ["bascinet", 20]], shield: [["heater", 70], ["adarga", 30]], armour: [["plate", 60], ["leather", 40]],
      bard: [["cloth", 80], ["none", 20]]},
    barded: {helm: [["sallet", 100]]},
  },
  fareast: { // Япония: о-ёрой, кабуто, нагината, юми, сасимоно; щитов в руке нет
    militia: {helm: [["jingasa", 60], ["hachimaki", 25], ["hair", 15]], weapon: [["spear", 60], ["naginata", 25], ["katana", 15]], shield: [["none", 100]],
      armour: [["cloth", 50], ["dou", 50]], back: [["sashimono", 50], ["none", 50]]},
    spear: {helm: [["jingasa", 80], ["hachimaki", 20]], weapon: [["spear", 100]], shield: [["none", 100]], armour: [["dou", 70], ["cloth", 30]],
      back: [["sashimono", 90], ["none", 10]]},
    sword: {helm: [["kabuto", 100]], weapon: [["katana", 50], ["naginata", 50]], shield: [["none", 100]], armour: [["oyoroi", 100]],
      back: [["sashimono", 70], ["none", 30]]},
    pike: {helm: [["jingasa", 100]], shield: [["none", 100]], armour: [["dou", 80], ["cloth", 20]], back: [["sashimono", 80], ["none", 20]]},
    bow: {helm: [["jingasa", 50], ["kabuto", 30], ["hachimaki", 20]], weapon: [["yumi", 100]], side: [["katana", 60], ["none", 40]],
      armour: [["dou", 50], ["cloth", 30], ["oyoroi", 20]]},
    crossbow: {helm: [["jingasa", 100]], weapon: [["yumi", 100]], side: [["katana", 50], ["none", 50]], back: [["quiver", 100]], armour: [["dou", 70], ["cloth", 30]]},
    lance: {helm: [["kabuto", 100]], weapon: [["yumi", 50], ["naginata", 30], ["katana", 20]], shield: [["none", 100]], armour: [["oyoroi", 100]],
      back: [["sashimono", 50], ["none", 50]], bard: [["none", 100]]},
    barded: {helm: [["kabuto", 100]], weapon: [["naginata", 60], ["spear", 40]], shield: [["none", 100]], armour: [["oyoroi", 100]],
      back: [["sashimono", 100]], bard: [["cloth", 100]]},
  },
};
const F_CLOTHS = {   // своя одежда — по стилю
  west: CLOTHS,
  north: ["#6b5a48", "#7a6a55", "#5a6470", "#8a7a5f", "#4f5a4a", "#8e8a7c", "#6e4a3a"],
  east: ["#b5462e", "#c9973a", "#2f5f8a", "#7b3b6b", "#3f7a5a", "#d8c8a0", "#8a2f2f"],
  south: ["#c8b48a", "#e2d6b8", "#9a5a2e", "#6e7d3a", "#b0823a", "#5a3a2a", "#3a5a7a"],
  fareast: ["#2c3550", "#4a3a30", "#6b2a2a", "#3a4a3a", "#7a6a4a", "#34302c", "#8a7a5a"],
};
const F_COATS = {west: [0, 1, 2, 3, 4, 5, 6], north: [1, 2, 4, 5, 6], east: [0, 2, 3, 4, 5], south: [0, 1, 2, 3, 6], fareast: [1, 2, 5, 6]};   // масти — номера в COATS
const F_HAIRS = {fareast: ["#1b1816", "#231d19", "#2b231d"]};
// новое оружие держат и бьют, как похожее старое: позы и кисти — от него
const F_BASE = {halberd: "spear", daneaxe: "spear", naginata: "spear", javelin: "spear", sabre: "sword", katana: "sword", recurve: "bow", yumi: "bow"};
const fBase = w => F_BASE[w] || w;
const fKB = k => k.kb || (k.kb = Object.assign({}, k, {weapon: fBase(k.weapon)}));

// Комплекты отряда по стилю: тот же хэш и порядок, что у kitsOf, — отряд с тем же номером одет так же.
// u: {id, col, tpl, style}
function kitsF(u){
  const style = F_STYLE[u.style] ? u.style : "west", stamp = u.col + style;
  if(u.fk && u.fkStamp === stamp) return u.fk;
  const look = lookOf(u), K = Object.assign({}, KIT[look], F_STYLE[style][look] || {});
  const seed = u.id * 1013 + 7, N = K.uniform ? 6 : 12, horse = look === "lance" || look === "barded";
  const cloths = F_CLOTHS[style], coats = F_COATS[style], hairs = F_HAIRS[style] || HAIRS;
  const c2 = DEVICE[Math.floor(hash(seed, 2) * DEVICE.length)];
  const one = {helm: pickW(K.helm, hash(seed, 3)), paint: pickW(K.paint || [["plain", 1]], hash(seed, 4))};
  u.fk = Array.from({length: N}, (_, i) => {
    const s = seed * 31 + i * 7 + 1, R = j => hash(s, j), own = R(1) < K.own;
    const cloth = own ? cloths[Math.floor(R(2) * cloths.length)] : shade(u.col, (R(3) - 0.5) * 0.2);
    const helm = K.uniform ? one.helm : pickW(K.helm, R(4));
    const helmCol = helm === "hair" || helm === "hachimaki" ? hairs[Math.floor(R(5) * hairs.length)]
      : helm === "turban" ? (R(6) < 0.6 ? "#ebe3cf" : cloths[Math.floor(R(7) * cloths.length)])
      : helm === "cap" || helm === "hood" ? (R(6) < 0.5 ? cloths[Math.floor(R(7) * cloths.length)] : shade(u.col, -0.25))
      : helm === "jingasa" || helm === "kabuto" ? F_LACQ : STEEL;
    const shape = pickW(K.shield, R(8)), paint = K.uniform ? one.paint : pickW(K.paint || [["plain", 1]], R(9));
    const shield = shape === "none" ? null : {shape, paint: shape === "buckler" ? "steel" : paint,
      c1: own && R(10) < 0.5 ? cloth : u.col, c2: look === "militia" ? DEVICE[Math.floor(R(11) * 3)] : c2};
    if(shield) shield.key = [shield.shape, shield.paint, shield.c1, shield.c2].join(",");
    const back = pickW(K.back, R(12)), armour = pickW(K.armour, R(13));
    const k = {style, look, horse, cloth, armour, leather: LEATHER[Math.floor(R(14) * 3)], helm, helmCol,
      tabard: TABARD.has(paint) ? paint : shield && TABARD.has(shield.paint) ? shield.paint : "plain",
      crest: helm === "great" && R(15) < 0.35 ? u.col : null, back, backCol: back === "pavise" || back === "sashimono" ? u.col : shade(u.col, -0.3),
      weapon: pickW(K.weapon, R(16)), side: K.side ? pickW(K.side, R(17)) : "none", shield, col: u.col, c2,
      coat: COATS[coats[Math.floor(R(18) * coats.length)]], bard: K.bard ? pickW(K.bard, R(19)) : "none",
      mark: pickW([["none", 50], ["star", 15], ["blaze", 25], ["snip", 10]], R(20)), socks: R(21) < 0.55 ? 0 : 1 + Math.floor(R(22) * 15)};
    k.bodyKey = [style, k.cloth, k.armour, k.tabard, k.leather, c2, u.col].join(",");
    k.headKey = [k.helm, k.helmCol, k.crest, u.col].join(",");
    k.horseKey = [style, k.coat, k.bard, k.tabard, k.mark, k.socks, u.col, c2].join(",");
    return k;
  });
  u.fkStamp = stamp;
  return u.fk;
}

// ═══ Общие кисти ═══
// Контур — почти постоянной толщины на экране: 1 px вдали, до ~3 px вплотную (у образца так же). k — доля от контура тела.
const fScale = g => { const m = g.getTransform(); return Math.hypot(m.a, m.b) || 40; };   // px на метр холста, поворот не влияет
function fLw(g, k = 1){
  const S = fScale(g), d = window.devicePixelRatio || 1, css = S / d;
  return k * Math.max(1.05, Math.min(3.4, 0.4 + 0.0155 * css)) * d / S;
}
const fFine = g => fScale(g) / (window.devicePixelRatio || 1) >= 20;   // мелочь (швы, штрихи, заклёпки) — ближе 20 px/м
function fEdge(g, k = 1, col = F_INK){ g.strokeStyle = col; g.lineWidth = fLw(g, k); g.stroke(); }
function fDisc(g, x, y, r, col, k = 1){ g.beginPath(); g.arc(x, y, r, 0, 6.283); g.fillStyle = col; g.fill(); if(k) fEdge(g, k); }
function fLine(g, pts, col, k = 0.5){ g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.strokeStyle = col; g.lineWidth = fLw(g, k); g.stroke(); }
function fStick(g, x0, y0, x1, y1, w, col){   // древко, рукоять: контур — полоса шире на два контура
  g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.lineCap = "round";
  g.strokeStyle = F_INK; g.lineWidth = w + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = col; g.lineWidth = w; g.stroke();
}
function fPoly(g, pts, w, col){   // ломаная трубкой (руки и ноги лежащего)
  g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.lineCap = "round"; g.lineJoin = "round";
  g.strokeStyle = F_INK; g.lineWidth = w + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = col; g.lineWidth = w; g.stroke();
}
function fCapsule(g, a = F_A, b = F_B){
  g.beginPath(); g.moveTo(-a + b, -b); g.lineTo(a - b, -b); g.arc(a - b, 0, b, -Math.PI / 2, Math.PI / 2);
  g.lineTo(-a + b, b); g.arc(-a + b, 0, b, Math.PI / 2, Math.PI * 1.5); g.closePath();
}
function fHatch(g, x0, y0, n = 5, dir = 1){   // «рукописная» тень: несколько коротких косых штрихов
  g.beginPath();
  for(let i = 0; i < n; i++){ const x = x0 + i * 0.021; g.moveTo(x, y0 + 0.02 * dir); g.lineTo(x + 0.02, y0 - 0.022 * dir); }
  g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.42); g.stroke();
}
function fSeams(g){   // две линии вдоль плеч чуть позади середины головы
  g.beginPath(); g.moveTo(-1, -0.011); g.lineTo(1, -0.011); g.moveTo(-1, 0.014); g.lineTo(1, 0.014);
  g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.45); g.stroke();
}
function fShine(g, x, y, r){ g.beginPath(); g.arc(x, y, r, Math.PI * 1.08, Math.PI * 1.55); g.strokeStyle = F_SHINE; g.lineWidth = fLw(g, 0.9); g.stroke(); }

// образец по обмеру: капсула цвета стороны и голова того же цвета чуть темнее
function fSample(g, col){
  fCapsule(g); g.fillStyle = col; g.fill();
  if(fFine(g)){ g.save(); fCapsule(g); g.clip(); fSeams(g); fHatch(g, 0.07, -0.1); fHatch(g, -0.19, 0.1, 5, -1); g.restore(); }
  fCapsule(g); fEdge(g);
  fDisc(g, F_HEAD[0], F_HEAD[1], F_HEAD[2], shade(col, -0.05), 0.55);
}

// ═══ Тело: капсула плеч по доспеху ═══
// Раскладка цвета стороны по доспеху и стилю (сторону должно быть видно на любом бойце, как у образца):
//   cloth   — ткань целиком (своя одежда или цвет стороны); у бригантины — заклёпки;
//   tabard  — кольчуга или латы на плечах, посередине сюрко цвета стороны с гербом (запад, юг);
//   cloak   — доспех, спина и задняя половина плеч под плащом цвета стороны с фибулой (север);
//   kaftan  — ламелляр посередине, рукава кафтана цвета стороны (восток);
//   lacing  — о-ёрой: ряды лакированных пластин, шнуровка цвета стороны, большие наплечники-содэ (Япония);
//   dou     — лакированная кираса асигару с моном, плечи — ткань.
function fLayout(k){
  const a = k.armour;
  if(a === "cloth" || a === "leather") return "cloth";
  if(a === "oyoroi") return "lacing";
  if(a === "dou") return "dou";
  if(k.style === "north") return "cloak";
  if(a === "lamellar") return "kaftan";
  return "tabard";
}
function fSleeve(k){ const L = fLayout(k); return L === "kaftan" ? k.col : L === "lacing" ? "#3b3533" : L === "cloth" || L === "dou" ? k.cloth : F_ARMC[k.armour]; }
function fDevice(g, paint, c2){
  g.fillStyle = c2;
  switch(paint){
    case "halves": g.fillRect(0, -1, 1, 2); break;
    case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
    case "stripe": g.beginPath(); g.moveTo(-0.2, 0.13); g.lineTo(-0.12, 0.13); g.lineTo(0.2, -0.13); g.lineTo(0.12, -0.13); g.closePath(); g.fill(); break;
    case "cross": g.fillRect(-0.024, -1, 0.048, 2); g.fillRect(-1, 0.035, 2, 0.045); break;
    case "chevron": g.beginPath(); g.moveTo(-0.2, 0.13); g.lineTo(0, 0.02); g.lineTo(0.2, 0.13); g.lineTo(0.2, 0.2); g.lineTo(0, 0.085); g.lineTo(-0.2, 0.2); g.closePath(); g.fill(); break;
  }
}
// фактура доспеха в прямоугольнике (обрезка — у вызывающего): кольчуга — дужки, чешуя — подковки, ламелляр — ряды пластин
function fTex(g, a, x0, y0, x1, y1){
  if(!fFine(g)) return;
  g.beginPath();
  if(a === "mail"){
    for(let y = y0, row = 0; y < y1; y += 0.032, row++) for(let x = x0 + (row % 2) * 0.016; x < x1; x += 0.032){ g.moveTo(x - 0.011, y); g.arc(x, y, 0.011, Math.PI, 0, true); }
  } else if(a === "scale"){
    for(let y = y0, row = 0; y < y1; y += 0.034, row++) for(let x = x0 + (row % 2) * 0.018; x < x1; x += 0.036){ g.moveTo(x - 0.017, y); g.quadraticCurveTo(x, y + 0.034, x + 0.017, y); }
  } else if(a === "lamellar"){
    for(let y = y0; y < y1; y += 0.045){ g.moveTo(x0, y); g.lineTo(x1, y); for(let x = x0; x < x1; x += 0.021){ g.moveTo(x, y); g.lineTo(x, y + 0.03); } }
  } else return;
  g.strokeStyle = a === "scale" ? "#77725f" : a === "lamellar" ? "#7d8386" : F_MAIL_D; g.lineWidth = fLw(g, 0.4); g.stroke();
}
function fBody(g, k){
  const L = fLayout(k), a = k.armour, base = L === "cloth" || L === "dou" ? k.cloth : F_ARMC[a];
  if(L === "tabard" || L === "cloak" || L === "kaftan") fDisc(g, F_HEAD[0], F_HEAD[1], 0.135, F_MAIL, 0.5);   // бармица кольцом вокруг шлема
  if(L === "lacing") for(const s of [-1, 1]){   // содэ — большие наплечники-щитки, шнуровка цвета стороны
    g.save(); g.translate(s * 0.255, 0.005); g.rotate(s * 0.12);
    g.beginPath(); g.rect(-0.07, -0.115, 0.14, 0.23); g.fillStyle = F_ARMC.oyoroi; g.fill();
    g.save(); g.clip(); g.fillStyle = k.col; for(let y = -0.105; y < 0.12; y += 0.046) g.fillRect(-1, y, 2, 0.018); g.restore();
    g.beginPath(); g.rect(-0.07, -0.115, 0.14, 0.23); fEdge(g, 0.8); g.restore();
  }
  fCapsule(g); g.fillStyle = base; g.fill();
  g.save(); fCapsule(g); g.clip();
  switch(L){
    case "tabard": {
      const w = 0.14;
      fTex(g, a, -0.26, -0.12, 0.26, 0.13);
      if(a === "plate") for(const s of [-1, 1]){   // наплечник с ободом и бликом
        g.beginPath(); g.arc(s * 0.2, 0, 0.075, 0, 6.283); fEdge(g, 0.45, F_STEEL_D); fShine(g, s * 0.2, 0, 0.05);
      }
      g.save(); g.beginPath(); g.rect(-w, -1, 2 * w, 2); g.fillStyle = k.col; g.fill(); g.clip(); fDevice(g, k.tabard, k.c2); g.restore();
      g.beginPath(); g.moveTo(-w, -1); g.lineTo(-w, 1); g.moveTo(w, -1); g.lineTo(w, 1); fEdge(g, 0.5);
      break;
    }
    case "cloak":
      fTex(g, a, -0.26, -0.12, 0.26, 0.13);
      g.beginPath(); g.moveTo(-0.3, 0.012); g.quadraticCurveTo(0, -0.025, 0.3, 0.012); g.lineTo(0.3, 0.3); g.lineTo(-0.3, 0.3); g.closePath();
      g.fillStyle = k.col; g.fill(); fEdge(g, 0.6);
      fDisc(g, 0.135, 0.004, 0.022, F_BRASS, 0.45);   // фибула на правом плече
      break;
    case "kaftan":
      g.fillStyle = k.col; g.fillRect(-1, -1, 0.87, 2); g.fillRect(0.13, -1, 1, 2);
      fTex(g, a, -0.13, -0.12, 0.13, 0.13);
      g.beginPath(); g.moveTo(-0.13, -1); g.lineTo(-0.13, 1); g.moveTo(0.13, -1); g.lineTo(0.13, 1); fEdge(g, 0.5);
      break;
    case "lacing":
      g.fillStyle = k.col; for(let y = -0.112; y < 0.13; y += 0.042) g.fillRect(-1, y, 2, 0.015);
      break;
    case "dou":
      g.fillStyle = F_ARMC.dou; g.fillRect(-0.15, -1, 0.3, 2);
      g.beginPath(); g.moveTo(-0.15, -1); g.lineTo(-0.15, 1); g.moveTo(0.15, -1); g.lineTo(0.15, 1); fEdge(g, 0.5);
      fDisc(g, 0, 0.07, 0.03, k.col, 0.4);   // мон цвета стороны
      break;
    case "cloth":
      if(a === "leather" && fFine(g)){ g.fillStyle = "#e3dccb"; for(const y of [-0.07, 0.075]) for(let x = -0.19; x <= 0.19; x += 0.038){ g.beginPath(); g.arc(x, y, 0.008, 0, 6.283); g.fill(); } }
      break;
  }
  if(fFine(g)){ fSeams(g); fHatch(g, 0.07, -0.1); fHatch(g, -0.19, 0.1, 5, -1); }
  g.restore();
  fCapsule(g); fEdge(g);
}

// ═══ Голова и шлемы (начало осей — центр головы) ═══
function fHead(g, k){
  const r = F_HEAD[2], c = k.helmCol, S = F_STEEL;
  const rays = (n, r0, r1, col, kk = 0.45, a0 = 0) => { g.beginPath(); for(let i = 0; i < n; i++){ const a = a0 + i * 6.283 / n; g.moveTo(Math.sin(a) * r0, -Math.cos(a) * r0); g.lineTo(Math.sin(a) * r1, -Math.cos(a) * r1); } g.strokeStyle = col; g.lineWidth = fLw(g, kk); g.stroke(); };
  switch(k.helm){
    case "hair":
      fDisc(g, 0, 0.005, r * 0.95, c, 0.55);
      g.beginPath(); for(const x of [-0.04, 0, 0.04]){ g.moveTo(x * 0.6, -0.07); g.quadraticCurveTo(x * 1.3, 0, x, 0.08); }
      g.strokeStyle = shade(c, -0.35); g.lineWidth = fLw(g, 0.45); g.stroke(); break;
    case "hachimaki":   // волосы и белая повязка с узлом на затылке
      fDisc(g, 0, 0.005, r * 0.95, c, 0.55);
      g.beginPath(); g.arc(0, 0.005, r * 0.78, 0, 6.283); g.strokeStyle = "#f1ece0"; g.lineWidth = Math.max(0.018, fLw(g, 1.4)); g.stroke();
      fLine(g, [[0, r * 0.8], [-0.04, r + 0.05]], "#f1ece0", 1.4); fLine(g, [[0, r * 0.8], [0.035, r + 0.06]], "#f1ece0", 1.4); break;
    case "cap":
      fDisc(g, 0, 0, r, c, 0.55); g.beginPath(); g.arc(0, 0, r * 0.7, 0, 6.283); fEdge(g, 0.4, shade(c, -0.35)); break;
    case "hood":   // капюшон с хвостиком на спину
      g.beginPath(); g.moveTo(r, 0); g.arc(0, 0, r, 0, Math.PI, true); g.quadraticCurveTo(-0.07, 0.14, 0, 0.21); g.quadraticCurveTo(0.07, 0.14, r, 0);
      g.fillStyle = c; g.fill(); fEdge(g, 0.55);
      g.beginPath(); g.arc(0, 0, r * 0.68, Math.PI * 1.15, Math.PI * 1.85); fEdge(g, 0.4, shade(c, -0.35)); break;
    case "turban":   // тюрбан: витки ткани, макушка
      fDisc(g, 0, 0, 0.125, c, 0.55);
      g.beginPath(); g.arc(0, 0, 0.095, -2.4, 0.7); g.moveTo(0.06 * Math.cos(0.5), 0.06 * Math.sin(0.5)); g.arc(0, 0, 0.06, 0.5, 3.7);
      g.strokeStyle = shade(c, -0.3); g.lineWidth = fLw(g, 0.45); g.stroke();
      fDisc(g, 0, 0, 0.03, shade(c, -0.08), 0.4); break;
    case "kettle":   // шапель: широкие поля и купол
      fDisc(g, 0, 0, 0.155, S, 0.55); fDisc(g, 0, 0, 0.09, shade(S, 0.1), 0.45); fShine(g, 0, 0, 0.06); break;
    case "capSteel":
      fDisc(g, 0, 0, r, S, 0.55); g.beginPath(); g.arc(0, 0, 0.068, 0, 6.283); fEdge(g, 0.45, F_STEEL_D); fShine(g, 0, 0, 0.05); break;
    case "nasal":    // наносник вперёд, гребень-шов
      g.beginPath(); g.rect(-0.016, -r - 0.035, 0.032, 0.05); g.fillStyle = S; g.fill(); fEdge(g, 0.5);
      fDisc(g, 0, 0, r, S, 0.55); fLine(g, [[0, -r], [0, r]], F_STEEL_D, 0.7); fShine(g, 0, 0, 0.065); break;
    case "shishak":  // шишак: бармица, конус гранями к навершию, наносник
      fDisc(g, 0, 0.01, 0.132, F_MAIL, 0.45);
      g.beginPath(); g.rect(-0.014, -r - 0.03, 0.028, 0.045); g.fillStyle = S; g.fill(); fEdge(g, 0.5);
      fDisc(g, 0, 0, r, S, 0.55); rays(4, 0.02, r * 0.97, F_STEEL_D, 0.5, Math.PI / 4);
      fDisc(g, 0, 0, 0.022, F_BRASS, 0.45); fShine(g, 0, 0, 0.065); break;
    case "pointed":  // восточный остроконечный: бармица, позолоченный обод, рёбра, навершие
      fDisc(g, 0, 0.012, 0.136, F_MAIL, 0.45);
      fDisc(g, 0, 0, r, S, 0.55); g.beginPath(); g.arc(0, 0, r * 0.86, 0, 6.283); g.strokeStyle = F_BRASS; g.lineWidth = fLw(g, 1.0); g.stroke();
      rays(8, 0.03, r * 0.8, F_STEEL_D, 0.4); fDisc(g, 0, 0, 0.026, F_BRASS, 0.45); fShine(g, 0, 0, 0.06); break;
    case "great":    // топфхельм: плоский верх с ободом, спереди — тёмная щель, у некоторых — гребень цвета отряда
      fDisc(g, 0, 0, 0.12, "#a1a6aa", 0.6); g.beginPath(); g.arc(0, 0, 0.086, 0, 6.283); fEdge(g, 0.45, "#737a7f");
      g.beginPath(); g.arc(0, 0, 0.12, -Math.PI * 0.75, -Math.PI * 0.25); g.strokeStyle = F_INK; g.lineWidth = fLw(g, 1.3); g.stroke();
      if(k.crest){ g.beginPath(); g.ellipse(0, 0.01, 0.028, 0.1, 0, 0, 6.283); g.fillStyle = k.crest; g.fill(); fEdge(g, 0.5); }
      else fShine(g, 0, 0, 0.07); break;
    case "bascinet": // бацинет с «клювом» забрала вперёд
      fDisc(g, 0, 0.01, r, S, 0.55);
      g.beginPath(); g.moveTo(-0.065, -0.065); g.quadraticCurveTo(-0.035, -0.17, 0, -0.195); g.quadraticCurveTo(0.035, -0.17, 0.065, -0.065); g.closePath();
      g.fillStyle = F_STEEL_D; g.fill(); fEdge(g, 0.55);
      g.fillStyle = F_INK; for(const x of [-0.018, 0.018]){ g.beginPath(); g.arc(x, -0.13, 0.008, 0, 6.283); g.fill(); }
      fShine(g, 0, 0.01, 0.065); break;
    case "sallet":   // салад: купол, длинный хвост назад, гребень, щель спереди
      g.beginPath(); g.moveTo(-r, 0); g.arc(0, 0, r, Math.PI, 0); g.quadraticCurveTo(r, 0.15, 0.045, 0.235); g.lineTo(-0.045, 0.235); g.quadraticCurveTo(-r, 0.15, -r, 0); g.closePath();
      g.fillStyle = S; g.fill(); fEdge(g, 0.55); fLine(g, [[0, -r], [0, 0.2]], F_STEEL_D, 0.7);
      g.beginPath(); g.arc(0, 0, r, -Math.PI * 0.72, -Math.PI * 0.28); g.strokeStyle = F_INK; g.lineWidth = fLw(g, 1.1); g.stroke(); fShine(g, 0, 0, 0.065); break;
    case "barbute":  // барбют: глухой купол с гребнем, спереди — вырез буквой Т
      fDisc(g, 0, 0.005, 0.11, S, 0.55); fLine(g, [[0, -0.11], [0, 0.1]], F_STEEL_D, 0.7);
      g.beginPath(); g.rect(-0.013, -0.118, 0.026, 0.032); g.fillStyle = F_INK; g.fill(); fShine(g, 0, 0, 0.065); break;
    case "kabuto": { // кабуто: широкий назатыльник-сикоро в шнуровке цвета стороны, отвороты, купол с рёбрами, рога-кувагата
      g.beginPath(); g.ellipse(0, 0.035, 0.175, 0.15, 0, 0, 6.283); g.fillStyle = c; g.fill();
      g.save(); g.clip(); g.strokeStyle = k.col; g.lineWidth = Math.max(0.012, fLw(g, 1.1));
      for(const rr of [0.125, 0.155]){ g.beginPath(); g.ellipse(0, 0.035, rr, rr * 0.86, 0, 0, 6.283); g.stroke(); } g.restore();
      g.beginPath(); g.ellipse(0, 0.035, 0.175, 0.15, 0, 0, 6.283); fEdge(g, 0.6);
      for(const s of [-1, 1]){ g.save(); g.translate(s * 0.115, -0.085); g.rotate(s * 0.6); g.beginPath(); g.rect(-0.03, -0.022, 0.06, 0.044); g.fillStyle = c; g.fill(); fEdge(g, 0.5, F_BRASS); g.restore(); }
      fDisc(g, 0, 0, 0.098, "#4a4440", 0.55); rays(12, 0.022, 0.094, "#221d1a", 0.4);
      fDisc(g, 0, 0, 0.02, F_BRASS, 0.4);
      for(const s of [-1, 1]){ g.beginPath(); g.moveTo(s * 0.025, -0.09); g.quadraticCurveTo(s * 0.03, -0.17, s * 0.085, -0.245); g.strokeStyle = F_INK; g.lineWidth = 0.018 + 2 * fLw(g, 0.6); g.stroke(); g.strokeStyle = F_BRASS; g.lineWidth = 0.018; g.stroke(); }
      break;
    }
    case "jingasa":  // дзингаса: широкая плоская шляпа-конус, мон цвета стороны
      fDisc(g, 0, 0, 0.19, c, 0.6); rays(8, 0.04, 0.18, "#4a403a", 0.4); g.beginPath(); g.arc(0, 0, 0.12, 0, 6.283); fEdge(g, 0.4, "#4a403a");
      fDisc(g, 0, -0.085, 0.034, k.col, 0.45); fDisc(g, 0, 0, 0.018, F_BRASS, 0.35); break;
    case "morion":   // поля лодочкой спереди назад, гребень
      g.beginPath(); g.ellipse(0, 0, 0.115, 0.185, 0, 0, 6.283); g.fillStyle = S; g.fill(); fEdge(g, 0.55);
      fLine(g, [[0, -0.175], [0, 0.175]], F_STEEL_D, 1.3); fShine(g, 0, 0, 0.07); break;
    default: fDisc(g, 0, 0, r, c, 0.55);
  }
}

// ═══ Щиты ═══
// плашмя (у павших): форма, поле, герб, обод, умбон
function fShieldPath(g, shape){
  if(shape === "kite"){ g.beginPath(); g.moveTo(-0.17, -0.12); g.quadraticCurveTo(-0.17, -0.3, 0, -0.3); g.quadraticCurveTo(0.17, -0.3, 0.17, -0.12); g.quadraticCurveTo(0.13, 0.15, 0, 0.36); g.quadraticCurveTo(-0.13, 0.15, -0.17, -0.12); g.closePath(); }
  else if(shape === "adarga"){ g.beginPath(); g.ellipse(-0.1, 0, 0.14, 0.24, 0, 0, 6.283); g.moveTo(0.24, 0); g.ellipse(0.1, 0, 0.14, 0.24, 0, 0, 6.283); }
  else shieldPath(g, shape);
}
function fShield(g, sh){
  const field = sh.paint === "wood" ? "#a8865a" : sh.paint === "steel" ? F_STEEL : sh.c1;
  fShieldPath(g, sh.shape); g.fillStyle = field; g.fill();
  g.save(); g.clip();
  if(sh.paint === "boss"){ g.beginPath(); g.arc(0, 0, 0.15, 0, 6.283); g.lineWidth = 0.045; g.strokeStyle = sh.c2; g.stroke(); }
  else if(sh.paint === "wood"){ g.beginPath(); for(let x = -0.18; x < 0.3; x += 0.09){ g.moveTo(x, -0.3); g.lineTo(x, 0.3); } g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.45); g.stroke(); }
  else if(sh.paint !== "steel" && sh.paint !== "plain"){
    g.fillStyle = sh.c2;
    switch(sh.paint){
      case "halves": g.fillRect(0, -1, 1, 2); break;
      case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
      case "stripe": g.fillRect(-1, -0.06, 2, 0.12); break;
      case "cross": g.fillRect(-0.045, -1, 0.09, 2); g.fillRect(-1, -0.1, 2, 0.09); break;
      case "chevron": g.beginPath(); g.moveTo(-0.3, 0.12); g.lineTo(0, -0.12); g.lineTo(0.3, 0.12); g.lineTo(0.3, 0.24); g.lineTo(0, 0); g.lineTo(-0.3, 0.24); g.closePath(); g.fill(); break;
    }
  }
  if(fFine(g)) fHatch(g, 0.04, -0.13, 4);
  g.restore();
  if(sh.paint !== "steel"){ fShieldPath(g, sh.shape); g.strokeStyle = sh.shape === "heater" || sh.shape === "kite" ? "#6a4a2c" : F_STEEL_D; g.lineWidth = 0.035; g.stroke(); }
  fShieldPath(g, sh.shape); fEdge(g);
  if(sh.shape === "round" || sh.shape === "oval" || sh.shape === "buckler"){ fDisc(g, 0, 0, sh.shape === "buckler" ? 0.05 : 0.06, F_STEEL, 0.6); fShine(g, 0, 0, 0.035); }
}
// в руке (В18): держат перед собой стоймя — сверху видно ребро: узкая линза цвета поля, герб — полосами поперёк,
// у круглых — умбон вперёд; адарга — две линзы рядом
function fShieldTop(g, sh){
  const w = sh.shape === "buckler" ? 0.13 : sh.shape === "round" ? 0.24 : sh.shape === "oval" ? 0.19 : sh.shape === "kite" ? 0.18 : sh.shape === "adarga" ? 0.12 : 0.2, d = 0.055;
  const field = sh.paint === "wood" ? "#a8865a" : sh.paint === "steel" ? F_STEEL : sh.c1;
  const lens = () => { g.beginPath(); if(sh.shape === "adarga"){ g.ellipse(-0.1, 0, w, d, 0, 0, 6.283); g.moveTo(0.1 + w, 0); g.ellipse(0.1, 0, w, d, 0, 0, 6.283); } else g.ellipse(0, 0, w, d, 0, 0, 6.283); };
  if(sh.shape === "round" || sh.shape === "oval" || sh.shape === "buckler") fDisc(g, 0, -d, 0.04, F_STEEL, 0.55);   // умбон
  lens(); g.fillStyle = field; g.fill();
  g.save(); lens(); g.clip(); g.fillStyle = sh.c2;
  switch(sh.paint){
    case "halves": g.fillRect(0, -1, 1, 2); break;
    case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
    case "cross": case "stripe": g.fillRect(-0.03, -1, 0.06, 2); break;
    case "chevron": g.beginPath(); g.moveTo(-0.25, 1); g.lineTo(0, -0.02); g.lineTo(0.25, 1); g.lineTo(0.25, -1); g.lineTo(-0.25, -1); g.closePath(); g.fill(); break;
    case "boss": g.fillRect(-w * 0.62, -1, 0.035, 2); g.fillRect(w * 0.62 - 0.035, -1, 0.035, 2); break;
    case "wood": g.beginPath(); for(let x = -0.18; x < 0.25; x += 0.09){ g.moveTo(x, -1); g.lineTo(x, 1); } g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.45); g.stroke(); break;
  }
  g.restore();
  lens(); fEdge(g, 0.9);
}

// ═══ Оружие: в своих осях, рукоять в начале (там кисть), острие вперёд (−y) ═══
const F_WBOX = Object.assign({}, WBOX, {halberd: [-0.12, -1.8, 0.18, 0.65], daneaxe: [-0.06, -1.32, 0.27, 0.45], sabre: [-0.09, -0.76, 0.13, 0.13],
  katana: [-0.07, -0.8, 0.1, 0.2], javelin: [-0.05, -1.0, 0.05, 0.45], naginata: [-0.06, -1.68, 0.12, 0.65]});
function fTip(g, y, len, w = 0.032, x = 0){ g.beginPath(); g.moveTo(x - w, y); g.quadraticCurveTo(x - w * 0.8, y - len * 0.6, x, y - len); g.quadraticCurveTo(x + w * 0.8, y - len * 0.6, x + w, y); g.lineTo(x, y + 0.03); g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); }
function fBlade(g, pts){ g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); }
function fWeapon(g, w, col){
  switch(w){
    case "spear": fStick(g, 0, 0.55, 0, -1.25, 0.03, F_WOOD); fTip(g, -1.25, 0.2); break;
    case "pike": fStick(g, 0, 0.7, 0, -3.8, 0.034, F_WOOD); fTip(g, -3.8, 0.22, 0.028); break;
    case "javelin": fStick(g, 0, 0.4, 0, -0.85, 0.022, "#a07c52"); fTip(g, -0.85, 0.11, 0.02); break;
    case "halberd":   // древко, наконечник, топор вправо, клюв влево
      fStick(g, 0, 0.6, 0, -1.5, 0.032, F_WOOD); fTip(g, -1.55, 0.2, 0.025);
      fBlade(g, [[0.016, -1.62], [0.14, -1.67], [0.155, -1.54], [0.14, -1.42], [0.016, -1.48]]);
      fBlade(g, [[-0.016, -1.57], [-0.1, -1.6], [-0.016, -1.52]]); break;
    case "daneaxe":   // длинная секира: широкое бородатое лезвие
      fStick(g, 0, 0.4, 0, -1.12, 0.032, F_WOOD);
      g.beginPath(); g.moveTo(0.016, -1.2); g.lineTo(0.16, -1.27); g.quadraticCurveTo(0.25, -1.06, 0.16, -0.87); g.lineTo(0.016, -1.02); g.closePath();
      g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); break;
    case "naginata":  // древко в лаке, изогнутый клинок
      fStick(g, 0, 0.6, 0, -1.15, 0.03, "#3e2c22"); fStick(g, -0.03, -1.15, 0.03, -1.15, 0.02, F_BRASS);
      g.beginPath(); g.moveTo(-0.018, -1.17); g.quadraticCurveTo(-0.03, -1.45, 0.06, -1.64); g.quadraticCurveTo(0.03, -1.4, 0.022, -1.17); g.closePath();
      g.fillStyle = F_SHINE; g.fill(); fEdge(g, 0.6); break;
    case "fork":
      fStick(g, 0, 0.4, 0, -1.0, 0.03, F_WOOD);
      g.beginPath(); g.moveTo(-0.06, -1.18); g.lineTo(-0.06, -1.03); g.quadraticCurveTo(-0.06, -0.98, 0, -0.98); g.quadraticCurveTo(0.06, -0.98, 0.06, -1.03); g.lineTo(0.06, -1.18);
      g.strokeStyle = F_INK; g.lineWidth = 0.022 + 2 * fLw(g, 0.6); g.stroke(); g.strokeStyle = F_STEEL; g.lineWidth = 0.022; g.stroke(); break;
    case "lance":
      fStick(g, 0, 0.6, 0, -2.7, 0.042, "#a07c52");
      g.beginPath(); g.ellipse(0, -0.06, 0.07, 0.028, 0, 0, 6.283); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6);
      fTip(g, -2.7, 0.2, 0.026);
      g.beginPath(); g.moveTo(0.02, -2.46); g.lineTo(0.24, -2.38); g.lineTo(0.02, -2.29); g.closePath(); g.fillStyle = col; g.fill(); fEdge(g, 0.6); break;
    case "sword": case "falchion":
      fStick(g, 0, 0.09, 0, -0.03, 0.028, "#6a4a2e"); fStick(g, -0.07, -0.035, 0.07, -0.035, 0.022, F_STEEL_D);
      g.beginPath();
      if(w === "sword"){ g.moveTo(-0.018, -0.045); g.lineTo(-0.014, -0.66); g.lineTo(0, -0.73); g.lineTo(0.014, -0.66); g.lineTo(0.018, -0.045); }
      else { g.moveTo(-0.02, -0.045); g.lineTo(-0.022, -0.46); g.quadraticCurveTo(-0.01, -0.64, 0.07, -0.5); g.lineTo(0.03, -0.045); }
      g.closePath(); g.fillStyle = F_SHINE; g.fill(); fEdge(g, 0.6);
      fDisc(g, 0, 0.1, 0.022, F_STEEL_D, 0.5); break;
    case "sabre":     // сабля: короткая гарда, клинок изогнут к острию
      fStick(g, 0, 0.09, 0.005, -0.03, 0.026, "#5a3a24"); fStick(g, -0.05, -0.035, 0.05, -0.035, 0.018, F_BRASS);
      g.beginPath(); g.moveTo(-0.016, -0.045); g.quadraticCurveTo(-0.03, -0.45, 0.08, -0.7); g.quadraticCurveTo(0.025, -0.42, 0.018, -0.045); g.closePath();
      g.fillStyle = F_SHINE; g.fill(); fEdge(g, 0.6); break;
    case "katana":    // катана: длинная рукоять, цуба, клинок с лёгким изгибом
      fStick(g, 0, 0.17, 0, -0.03, 0.026, "#2a2420"); fDisc(g, 0, -0.04, 0.034, F_LACQ, 0.5);
      g.beginPath(); g.moveTo(-0.014, -0.07); g.quadraticCurveTo(-0.02, -0.5, 0.04, -0.76); g.quadraticCurveTo(0.012, -0.48, 0.014, -0.07); g.closePath();
      g.fillStyle = F_SHINE; g.fill(); fEdge(g, 0.6); break;
    case "axe":
      fStick(g, 0, 0.12, 0, -0.6, 0.032, F_WOOD);
      g.beginPath(); g.moveTo(0.012, -0.64); g.lineTo(0.12, -0.69); g.quadraticCurveTo(0.17, -0.56, 0.12, -0.43); g.lineTo(0.012, -0.5); g.closePath();
      g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); break;
    case "mace":
      fStick(g, 0, 0.1, 0, -0.45, 0.03, F_WOOD);
      g.beginPath(); for(let q = 0; q < 6; q++){ const a = q * Math.PI / 3; g.lineTo(Math.cos(a) * 0.08, -0.5 + Math.sin(a) * 0.08); g.lineTo(Math.cos(a + 0.52) * 0.05, -0.5 + Math.sin(a + 0.52) * 0.05); }
      g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); break;
    case "club":
      g.beginPath(); g.moveTo(-0.02, 0.1); g.lineTo(-0.055, -0.5); g.quadraticCurveTo(0, -0.62, 0.055, -0.5); g.lineTo(0.02, 0.1); g.closePath();
      g.fillStyle = "#7a5634"; g.fill(); fEdge(g, 0.6); break;
  }
}
// лук — в осях бойца, ступени как у paintBow: 0 опущен, 1 стрела на тетиве, 2 вполовину, 3 до щеки, 4 выстрел.
// kind: bow — прямой, recurve — составной короче с загнутыми концами, yumi — длинный, хват ниже середины
function fBow(g, st, kind = "bow"){
  const d = [0, 0.15, 0.55, 1, 0][st], L = kind === "recurve" ? 0.82 : kind === "yumi" ? 1.35 : 1;
  const ty = -0.2 - 0.02 * d, cy = -0.56 - 0.1 * d, ny = ty + 0.36 * d, tx = (0.3 - 0.06 * d) * L;
  const xl = kind === "yumi" ? -tx * 1.18 : -tx, xr = kind === "yumi" ? tx * 0.82 : tx, cx = kind === "yumi" ? -0.06 : 0;
  g.beginPath(); g.moveTo(xl, ty); g.lineTo(0.02, ny); g.lineTo(xr, ty); g.strokeStyle = "#efe9dc"; g.lineWidth = Math.max(0.01, fLw(g, 0.5)); g.stroke();
  if(st >= 1 && st <= 3){ fStick(g, 0.02, ny + 0.02, 0.02, ny - 0.72, 0.012, "#e2d6bd"); fTip(g, ny - 0.72, 0.07, 0.022, 0.02); }
  g.beginPath(); g.moveTo(xl, ty); g.quadraticCurveTo(cx, cy, xr, ty);
  if(kind === "recurve"){ g.moveTo(xl, ty); g.quadraticCurveTo(xl - 0.04, ty + 0.01, xl - 0.06, ty - 0.05); g.moveTo(xr, ty); g.quadraticCurveTo(xr + 0.04, ty + 0.01, xr + 0.06, ty - 0.05); }
  const lc = kind === "yumi" ? "#3a2c24" : kind === "recurve" ? "#a0763e" : F_WOOD;
  g.strokeStyle = F_INK; g.lineWidth = 0.04 + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = lc; g.lineWidth = 0.04; g.stroke();
  if(kind === "yumi"){ g.beginPath(); g.moveTo(cx * 0.5 - 0.05, (ty + cy) / 2 - 0.02); g.lineTo(cx * 0.5 + 0.05, (ty + cy) / 2 - 0.02); g.strokeStyle = "#e8e0cc"; g.lineWidth = 0.03; g.stroke(); }   // обмотка хвата
}
function fXbow(g, st){
  fStick(g, 0.05, 0.02, 0.05, -0.5, 0.055, F_WOOD);
  g.beginPath(); g.moveTo(-0.22, -0.45); g.lineTo(0.05, st ? -0.45 : -0.3); g.lineTo(0.32, -0.45); g.strokeStyle = "#efe9dc"; g.lineWidth = Math.max(0.01, fLw(g, 0.5)); g.stroke();
  if(!st){ fStick(g, 0.05, -0.29, 0.05, -0.58, 0.014, "#e2d6bd"); fTip(g, -0.58, 0.06, 0.02, 0.05); }
  g.beginPath(); g.moveTo(-0.22, -0.45); g.quadraticCurveTo(0.05, -0.58, 0.32, -0.45);
  g.strokeStyle = F_INK; g.lineWidth = 0.04 + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = F_STEEL; g.lineWidth = 0.04; g.stroke();
}
// рука: предплечье от локтя к кисти (под телом), кисть — поверх оружия
function fArm(g, k){ fStick(g, 0, 0, 0, -FA_LEN, 0.07, fSleeve(k)); }
function fHand(g, kind){ fDisc(g, 0, 0, 0.038, F_HAND[kind], 0.55); }
function fBoot(g){ g.beginPath(); g.ellipse(0, 0, 0.048, 0.075, 0, 0, 6.283); g.fillStyle = F_BOOT; g.fill(); fEdge(g, 0.5); }
// поклажа выглядывает сзади
function fBack(g, k){
  switch(k.back){
    case "cape":
      g.beginPath(); g.moveTo(-0.24, 0.02); g.lineTo(0.24, 0.02); g.lineTo(0.21, 0.3); g.quadraticCurveTo(0, 0.35, -0.21, 0.3); g.closePath();
      g.fillStyle = k.backCol; g.fill(); fEdge(g); break;
    case "roll":
      g.beginPath(); g.ellipse(0, 0.15, 0.21, 0.06, 0, 0, 6.283); g.fillStyle = "#c4ad82"; g.fill(); fEdge(g, 0.7);
      g.beginPath(); for(const x of [-0.08, 0.08]){ g.moveTo(x, 0.095); g.lineTo(x, 0.205); } fEdge(g, 0.6, "#5a4630"); break;
    case "bag": g.beginPath(); g.rect(-0.1, 0.06, 0.2, 0.18); g.fillStyle = "#7a5634"; g.fill(); fEdge(g, 0.7); break;
    case "quiver":
      g.save(); g.translate(0.13, 0.16); g.rotate(0.35);
      g.beginPath(); g.rect(-0.05, -0.15, 0.1, 0.29); g.fillStyle = k.style === "fareast" ? F_LACQ : "#74512f"; g.fill(); fEdge(g, 0.7);
      for(const x of [-0.022, 0.004, 0.028]){ g.beginPath(); g.ellipse(x, -0.165, 0.014, 0.024, 0, 0, 6.283); g.fillStyle = "#f2ede2"; g.fill(); }
      g.restore(); break;
    case "pavise":   // павеза на спине стоит ребром — сверху тонкая полоса
      g.beginPath(); g.rect(-0.26, 0.11, 0.52, 0.075); g.fillStyle = k.backCol; g.fill();
      g.fillStyle = k.c2; g.fillRect(-0.04, 0.11, 0.08, 0.075); g.beginPath(); g.rect(-0.26, 0.11, 0.52, 0.075); fEdge(g, 0.8); break;
    case "sashimono":   // флажок на древке за спиной: сверху — узкое полотнище, отведённое ветром
      fStick(g, 0.04, 0.08, 0.04, 0.2, 0.02, "#3a2a20");
      g.save(); g.translate(0.04, 0.2); g.rotate(0.35);
      g.beginPath(); g.rect(-0.02, 0, 0.27, 0.075); g.fillStyle = k.backCol; g.fill(); fEdge(g, 0.7);
      fDisc(g, 0.125, 0.0375, 0.022, k.c2, 0.35); g.restore(); break;
  }
}

// ═══ Конь сверху (В18): ноги, хвост, шея с головой, туловище, попона или броня, седло; поводья — от удил к рукам ═══
// Оси и места частей — как у коня polygon-men.js (HLEG, HTAIL, HNECK), походка — horsePose. Голова рисуется раньше
// туловища: основание шеи уходит под грудь. Отметины: звёздочка, проточина, пятно на храпе, белые «чулки».
const fHair = k => mix(k.coat, "#1e1a17", 0.6);
// замкнутая гладкая кривая через точки (Катмулл — Ром)
function fSmooth(g, pts){
  const n = pts.length; g.beginPath(); g.moveTo(pts[0][0], pts[0][1]);
  for(let i = 0; i < n; i++){
    const p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
    g.bezierCurveTo(p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6, p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6, p2[0], p2[1]);
  }
  g.closePath();
}
// симметричный контур по точкам [y, полуширина] спереди назад; у первой и последней полуширина 0
function fSym(g, P){ const pts = P.map(([y, w]) => [w, y]); for(let i = P.length - 2; i >= 1; i--) pts.push([-P[i][1], P[i][0]]); fSmooth(g, pts); }
// туловище: грудь, плечи, чуть уже в подпруге, шире в маклаках, круглый круп
const F_HBODY = [[-0.7, 0], [-0.64, 0.14], [-0.5, 0.226], [-0.22, 0.238], [0.1, 0.222], [0.4, 0.248], [0.62, 0.262], [0.83, 0.212], [0.935, 0.12], [0.965, 0]];
// шея и голова от основания шеи: шея сужается к затылку, щёки шире, к храпу уже
const F_HHEAD = [[-0.81, 0], [-0.785, 0.052], [-0.72, 0.088], [-0.6, 0.116], [-0.5, 0.138], [-0.42, 0.122], [-0.3, 0.126], [-0.1, 0.165], [0.06, 0.19], [0.12, 0]];
const F_WHITE = "#efe9dd", F_TACK = "#4a3020";
function fHLeg(g, k, sock){   // нога из-под туловища; на ходу выглядывает копытом вперёд
  const p = () => { g.beginPath(); g.ellipse(0, 0, 0.058, 0.17, 0, 0, 6.283); };
  p(); g.fillStyle = shade(k.coat, -0.22); g.fill();
  if(sock){ g.save(); p(); g.clip(); g.fillStyle = F_WHITE; g.fillRect(-1, -1, 2, 0.95); g.restore(); }
  p(); fEdge(g, 0.6);
  g.beginPath(); g.ellipse(0, -0.13, 0.046, 0.034, 0, 0, 6.283); g.fillStyle = "#2a2420"; g.fill(); fEdge(g, 0.45);
}
function fHTail(g, k){   // от корня назад (+y): пучок шире к концу, пряди
  const h = fHair(k);
  g.beginPath(); g.moveTo(-0.04, 0); g.quadraticCurveTo(-0.075, 0.2, -0.085, 0.42); g.quadraticCurveTo(-0.05, 0.57, 0, 0.55);
  g.quadraticCurveTo(0.05, 0.57, 0.085, 0.42); g.quadraticCurveTo(0.075, 0.2, 0.04, 0); g.closePath();
  g.fillStyle = h; g.fill(); fEdge(g, 0.6);
  if(fFine(g)){ g.beginPath(); for(const x of [-0.035, 0, 0.035]){ g.moveTo(x * 0.4, 0.06); g.quadraticCurveTo(x * 1.2, 0.3, x * 1.5, 0.5); } g.strokeStyle = shade(h, 0.25); g.lineWidth = fLw(g, 0.4); g.stroke(); }
}
function fHBody(g, k){
  fSym(g, F_HBODY); g.fillStyle = k.coat; g.fill();
  if(fFine(g)){
    g.save(); fSym(g, F_HBODY); g.clip();
    const ln = shade(k.coat, -0.25);
    fLine(g, [[0, -0.52], [0, 0.88]], ln, 0.45);   // хребет
    g.beginPath();
    for(const s of [-1, 1]){ g.moveTo(s * 0.2, -0.6); g.quadraticCurveTo(s * 0.1, -0.48, s * 0.13, -0.28); g.moveTo(s * 0.23, 0.42); g.quadraticCurveTo(s * 0.1, 0.55, s * 0.14, 0.8); }   // лопатки, маклаки
    g.strokeStyle = ln; g.lineWidth = fLw(g, 0.45); g.stroke();
    fHatch(g, 0.08, -0.5); fHatch(g, -0.2, 0.75, 5, -1);
    g.restore();
  }
  fSym(g, F_HBODY); fEdge(g);
}
function fHHead(g, k){   // шея и голова: уши, чёлка, грива набок, глаза, ноздри, отметина, уздечка; у брони — сукно или ламелляр и налобник
  const armoured = k.bard === "full" || k.bard === "lamellar", h = fHair(k), path = () => fSym(g, F_HHEAD);
  path(); g.fillStyle = k.coat; g.fill();
  g.save(); path(); g.clip();
  g.beginPath(); g.ellipse(0, -0.76, 0.075, 0.065, 0, 0, 6.283); g.fillStyle = shade(k.coat, -0.22); g.fill();   // храп темнее
  if(k.mark === "blaze"){ g.beginPath(); g.ellipse(0, -0.6, 0.026, 0.17, 0, 0, 6.283); g.fillStyle = F_WHITE; g.fill(); }
  else if(k.mark === "star"){ g.beginPath(); g.moveTo(0, -0.535); g.lineTo(0.026, -0.5); g.lineTo(0, -0.465); g.lineTo(-0.026, -0.5); g.closePath(); g.fillStyle = F_WHITE; g.fill(); }
  else if(k.mark === "snip"){ g.beginPath(); g.ellipse(0, -0.745, 0.022, 0.032, 0, 0, 6.283); g.fillStyle = F_WHITE; g.fill(); }
  if(k.bard === "full"){ g.fillStyle = k.col; g.fillRect(-1, -0.4, 2, 1); g.fillStyle = k.c2; g.fillRect(-0.022, -0.4, 0.044, 1); }   // сукно на шее
  if(k.bard === "lamellar"){ g.fillStyle = F_ARMC.lamellar; g.fillRect(-1, -0.4, 2, 1); fTex(g, "lamellar", -0.2, -0.4, 0.2, 0.12); }
  if(fFine(g)){   // уздечка: налобный и щёчные ремни, капсюль
    g.beginPath(); g.moveTo(-0.13, -0.46); g.lineTo(0.13, -0.46);
    for(const s of [-1, 1]){ g.moveTo(s * 0.125, -0.46); g.lineTo(s * 0.1, -0.69); }
    g.moveTo(-0.1, -0.69); g.lineTo(0.1, -0.69); g.strokeStyle = F_TACK; g.lineWidth = fLw(g, 0.7); g.stroke();
  }
  g.restore();
  path(); fEdge(g);
  for(const s of [-1, 1]){   // уши торчат над затылком
    g.beginPath(); g.ellipse(s * 0.07, -0.415, 0.026, 0.056, s * 0.35, 0, 6.283); g.fillStyle = k.bard === "full" ? k.col : k.coat; g.fill(); fEdge(g, 0.5);
    if(fFine(g)) fLine(g, [[s * 0.068, -0.445], [s * 0.074, -0.39]], shade(k.coat, -0.4), 0.4);
  }
  if(!armoured){   // грива лежит на правой стороне шеи рваной прядью, чёлка между ушами
    g.beginPath(); g.moveTo(-0.01, 0.03);
    for(let i = 0; i <= 9; i++){ const y = 0.03 - i * 0.047; g.lineTo(0.045 + (i % 2 ? 0.035 : 0) + 0.035 * (1 - i / 9), y); }
    g.lineTo(0.0, -0.41); g.closePath(); g.fillStyle = h; g.fill(); fEdge(g, 0.45);
    g.beginPath(); g.ellipse(0, -0.47, 0.03, 0.052, 0, 0, 6.283); g.fillStyle = h; g.fill(); fEdge(g, 0.45);
  }
  g.fillStyle = F_INK;
  for(const s of [-1, 1]){ g.beginPath(); g.arc(s * 0.122, -0.53, 0.012, 0, 6.283); g.fill(); g.beginPath(); g.arc(s * 0.03, -0.778, 0.009, 0, 6.283); g.fill(); }   // глаза, ноздри
  if(fFine(g)) for(const s of [-1, 1]) fDisc(g, s * 0.1, -0.69, 0.016, F_STEEL, 0.4);   // кольца удил
  if(armoured){ g.beginPath(); g.moveTo(-0.07, -0.43); g.lineTo(0.07, -0.43); g.lineTo(0.05, -0.72); g.lineTo(-0.05, -0.72); g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); fShine(g, 0, -0.52, 0.04); }   // налобник
}
function fHCover(g, k){
  const armoured = k.bard === "full" || k.bard === "lamellar";
  if(armoured){   // попона до копыт с гербом и каймой — или ламелляр катафракта: по туловищу, чуть шире
    const P = F_HBODY.map(([y, w]) => [y, w * 1.13 + 0.01]), p = () => fSym(g, P);
    p(); g.fillStyle = k.bard === "full" ? k.col : F_ARMC.lamellar; g.fill();
    g.save(); p(); g.clip();
    if(k.bard === "full"){
      g.save(); g.translate(0, 0.14); g.scale(1.3, 3.6); fDevice(g, k.tabard, k.c2); g.restore();
      p(); g.strokeStyle = k.c2; g.lineWidth = 0.05; g.stroke();
      if(fFine(g)){ g.setLineDash([0.035, 0.03]); p(); g.strokeStyle = shade(k.col, -0.35); g.lineWidth = 0.022; g.stroke(); g.setLineDash([]); }   // фестоны
    } else fTex(g, "lamellar", -0.34, -0.75, 0.34, 1.0);
    g.restore(); p(); fEdge(g);
  } else if(k.bard === "cloth"){   // чепрак цвета стороны с каймой второго цвета
    g.beginPath(); g.roundRect(-0.27, -0.2, 0.54, 0.58, 0.08); g.fillStyle = shade(k.col, -0.08); g.fill(); fEdge(g, 0.8);
    g.beginPath(); g.roundRect(-0.235, -0.165, 0.47, 0.51, 0.06); g.strokeStyle = k.c2; g.lineWidth = 0.02; g.stroke();
  }
  if(!armoured) for(const s of [-1, 1]) fLine(g, [[s * 0.15, 0.0], [s * 0.245, 0.0]], F_TACK, 1.6);   // подпруга по бокам
  // седло: сиденье, передняя и задняя луки
  g.beginPath(); g.roundRect(-0.15, -0.16, 0.3, 0.37, 0.08); g.fillStyle = "#6e4b2c"; g.fill(); fEdge(g, 0.8);
  g.beginPath(); g.moveTo(-0.12, -0.12); g.quadraticCurveTo(0, -0.2, 0.12, -0.12); g.moveTo(-0.12, 0.17); g.quadraticCurveTo(0, 0.245, 0.12, 0.17);
  g.strokeStyle = F_TACK; g.lineWidth = 0.028; g.stroke();
}
// ноги всадника свешиваются по бокам коня: путлище, бедро наружу-вперёд, сапог в стремени
function fRiderLegs(g, k){
  const pants = k.armour === "plate" ? F_STEEL : k.armour === "oyoroi" ? "#3b3533" : mix(k.cloth, "#3e3328", 0.45);
  for(const s of [-1, 1]){
    fLine(g, [[s * 0.15, 0.0], [s * 0.3, -0.08]], F_TACK, 0.9);
    fStick(g, s * 0.1, 0.06, s * 0.27, -0.05, 0.1, pants);
    g.beginPath(); g.ellipse(s * 0.3, -0.04, 0.05, 0.085, 0, 0, 6.283); g.fillStyle = k.armour === "plate" ? F_STEEL : F_BOOT; g.fill(); fEdge(g, 0.55);
    g.beginPath(); g.moveTo(s * 0.255, -0.1); g.quadraticCurveTo(s * 0.3, -0.14, s * 0.345, -0.1); g.strokeStyle = F_STEEL_D; g.lineWidth = 0.02; g.stroke();   // стремя
  }
}

// ═══ Лежащие: павший, раненый, павший конь, кровь ═══
// Лежащий виден сверху во весь рост: руки и ноги — трубки, кисти и сапоги, корпус по раскладке доспеха, пояс.
// P.legs — ломаные от бедра к ступне, P.arms — от плеча к кисти (позы — corpsePose и CRAWL_POSE из polygon-men.js).
function fLying(g, k, P){
  const L = fLayout(k), pants = k.armour === "plate" ? F_STEEL : mix(k.cloth, "#3e3328", 0.5), sleeve = fSleeve(k);
  if(L === "cloak"){ g.beginPath(); g.moveTo(-0.27, -0.44); g.lineTo(0.27, -0.44); g.quadraticCurveTo(0.42, 0.0, 0.36, 0.4); g.quadraticCurveTo(0, 0.48, -0.36, 0.4); g.quadraticCurveTo(-0.42, 0.0, -0.27, -0.44); g.closePath(); g.fillStyle = k.col; g.fill(); fEdge(g, 0.8); }
  for(const Lg of P.legs){
    fPoly(g, Lg, 0.1, pants);
    const [fx, fy] = Lg[Lg.length - 1], [px, py] = Lg[Lg.length - 2], a = Math.atan2(fx - px, -(fy - py));
    g.save(); g.translate(fx, fy); g.rotate(a + Math.PI); g.translate(0, -0.04); fBoot(g); g.restore();
  }
  for(const A of P.arms){ fPoly(g, A, 0.085, sleeve); const [hx, hy] = A[A.length - 1]; g.save(); g.translate(hx, hy); fHand(g, handKind(k)); g.restore(); }
  const torso = () => { g.beginPath(); g.moveTo(-0.21, -0.36); g.quadraticCurveTo(-0.23, -0.45, -0.12, -0.45); g.lineTo(0.12, -0.45); g.quadraticCurveTo(0.23, -0.45, 0.21, -0.36);
    g.lineTo(0.18, 0.12); g.quadraticCurveTo(0, 0.17, -0.18, 0.12); g.closePath(); };
  const base = L === "cloth" || L === "dou" ? k.cloth : F_ARMC[k.armour];
  torso(); g.fillStyle = base; g.fill();
  g.save(); torso(); g.clip();
  switch(L){
    case "tabard": fTex(g, k.armour, -0.25, -0.46, 0.25, 0.18); g.save(); g.beginPath(); g.rect(-0.13, -1, 0.26, 2); g.fillStyle = k.col; g.fill(); g.clip(); g.translate(0, -0.15); g.scale(1, 1.6); fDevice(g, k.tabard, k.c2); g.restore(); break;
    case "cloak": fTex(g, k.armour, -0.25, -0.46, 0.25, 0.18); break;
    case "kaftan": g.fillStyle = k.col; g.fillRect(-1, -1, 0.87, 2); g.fillRect(0.13, -1, 1, 2); fTex(g, "lamellar", -0.13, -0.46, 0.13, 0.18); break;
    case "lacing": g.fillStyle = k.col; for(let y = -0.44; y < 0.15; y += 0.05) g.fillRect(-1, y, 2, 0.016); break;
    case "dou": g.fillStyle = F_ARMC.dou; g.fillRect(-0.16, -1, 0.32, 2); fDisc(g, 0, -0.18, 0.035, k.col, 0.4); break;
    case "cloth": if(k.armour === "leather" && fFine(g)){ g.fillStyle = "#e3dccb"; for(let y = -0.38; y < 0.12; y += 0.08) for(let x = -0.15; x <= 0.15; x += 0.06){ g.beginPath(); g.arc(x, y, 0.009, 0, 6.283); g.fill(); } } break;
  }
  g.fillStyle = "#5a4028"; g.fillRect(-0.2, 0.0, 0.4, 0.04);
  g.restore(); torso(); fEdge(g, 0.9);
}
const fMute = g => { g.globalCompositeOperation = "source-atop"; g.fillStyle = "rgba(112,106,96,.32)"; g.fillRect(-3, -3, 6, 6); g.globalCompositeOperation = "source-over"; };
// павший: на спине, голова к −y, ноги у начала; рядом плашмя — щит и оружие; краски приглушены (В8)
function fCorpse(g, k, v){
  const r = j => hash(v * 131 + 7, j);
  if(k.shield){ g.save(); g.translate(-0.5 - 0.15 * r(8), -0.1 + 0.4 * r(9)); g.rotate(r(10) * 6.283); g.scale(0.85, 0.85); fShield(g, k.shield); g.restore(); }
  const bw = fBase(k.weapon) === "bow", w = bw || k.weapon === "crossbow" ? k.side : k.weapon;
  g.save(); g.translate(0.45 + 0.15 * r(5), -0.25 + 0.5 * r(6)); g.rotate(r(7) * 6.283);
  if(w && w !== "none"){ const sc = w === "pike" || w === "lance" ? 0.4 : fBase(w) === "spear" ? 0.7 : 0.9; g.scale(sc, sc); fWeapon(g, w, k.col); }
  else if(bw) fBow(g, 0, k.weapon === "bow" ? "bow" : k.weapon);
  g.restore();
  fLying(g, k, corpsePose(v));
  g.save(); g.translate(0, -0.56); g.rotate((r(11) - 0.5) * 0.8); fHead(g, k); g.restore();
  fMute(g);
}
// раненый: лицом вниз, правая рука вперёд, левая согнута, правая нога подтянута — так ползёт; краски живые
function fCrawl(g, k){ fLying(g, k, CRAWL_POSE); g.save(); g.translate(0, -0.56); fHead(g, k); g.restore(); }
// павший конь на боку: сверху виден его профиль — туловище, шея и голова вытянуты по земле, ноги в сторону (ближние —
// поверх, дальние — из-под туловища темнее), хвост и грива на земле; седло с подпругой или попона, раны; лужа — отдельно
const F_DHORSE = [[-0.3, 0.92], [-0.36, 0.6], [-0.34, 0.15], [-0.37, -0.3], [-0.33, -0.6], [-0.25, -0.9], [-0.17, -1.12], [-0.15, -1.3], [-0.09, -1.5],
  [-0.04, -1.6], [0.04, -1.58], [0.08, -1.45], [0.12, -1.28], [0.08, -1.12], [0.14, -0.88], [0.27, -0.62], [0.32, -0.35], [0.34, 0.1], [0.32, 0.55], [0.24, 0.85], [0.05, 1.0]];
function fDeadHorse(g, k){
  const h = fHair(k), armoured = k.bard === "full" || k.bard === "lamellar";
  const leg = (x0, y0, a1, a2, col, sock) => {   // от туловища к колену, к путовому суставу, копыто
    const kx = x0 + Math.cos(a1) * 0.3, ky = y0 + Math.sin(a1) * 0.3, fx = kx + Math.cos(a2) * 0.27, fy = ky + Math.sin(a2) * 0.27;
    fStick(g, x0, y0, kx, ky, 0.11, col); fStick(g, kx, ky, fx, fy, 0.07, sock ? F_WHITE : col);
    g.beginPath(); g.ellipse(fx + Math.cos(a2) * 0.035, fy + Math.sin(a2) * 0.035, 0.05, 0.036, a2, 0, 6.283); g.fillStyle = "#2a2420"; g.fill(); fEdge(g, 0.5);
  };
  // хвост и грива лежат на земле
  g.beginPath(); g.moveTo(-0.22, 0.9); g.quadraticCurveTo(-0.45, 1.15, -0.4, 1.45); g.quadraticCurveTo(-0.25, 1.52, -0.12, 1.4); g.quadraticCurveTo(-0.1, 1.15, -0.06, 0.95); g.closePath();
  g.fillStyle = h; g.fill(); fEdge(g, 0.6);
  if(!armoured){ g.beginPath(); g.moveTo(-0.33, -0.55); for(let i = 0; i <= 8; i++){ const u = i / 8; g.lineTo(-0.33 + 0.16 * u - 0.07 - (i % 2) * 0.045, -0.55 - 0.57 * u); } g.lineTo(-0.17, -1.12); g.closePath(); g.fillStyle = h; g.fill(); fEdge(g, 0.5); }
  const far = shade(k.coat, -0.42), near = shade(k.coat, -0.18), socks = k.socks || 0;
  leg(0.25, -0.38, 0.35, 0.95, far, socks & 1); leg(0.26, 0.64, 0.2, 0.7, far, socks & 4);
  fSmooth(g, F_DHORSE); g.fillStyle = k.coat; g.fill();
  g.save(); fSmooth(g, F_DHORSE); g.clip();
  g.beginPath(); g.ellipse(-0.02, -1.55, 0.085, 0.075, 0.3, 0, 6.283); g.fillStyle = shade(k.coat, -0.22); g.fill();   // храп
  if(k.mark === "blaze"){ g.beginPath(); g.ellipse(-0.12, -1.4, 0.022, 0.15, -0.35, 0, 6.283); g.fillStyle = F_WHITE; g.fill(); }
  else if(k.mark === "star") fDisc(g, -0.14, -1.27, 0.022, F_WHITE, 0);
  if(armoured){
    g.beginPath(); g.rect(-1, -1.1, 2, 2.3); g.fillStyle = k.bard === "full" ? k.col : F_ARMC.lamellar; g.fill();
    if(k.bard === "full"){ g.save(); g.scale(1.8, 3.4); fDevice(g, k.tabard, k.c2); g.restore(); fSmooth(g, F_DHORSE); g.strokeStyle = k.c2; g.lineWidth = 0.05; g.stroke(); }
    else fTex(g, "lamellar", -0.4, -1.1, 0.4, 1.0);
  } else {
    if(k.bard === "cloth"){ g.beginPath(); g.roundRect(-0.42, -0.42, 0.36, 0.62, 0.05); g.fillStyle = shade(k.col, -0.08); g.fill(); fEdge(g, 0.7); }
    g.beginPath(); g.roundRect(-0.42, -0.3, 0.2, 0.4, 0.05); g.fillStyle = "#6e4b2c"; g.fill(); fEdge(g, 0.7);   // седло
    g.beginPath(); g.rect(-0.22, -0.07, 0.6, 0.045); g.fillStyle = F_TACK; g.fill();   // подпруга
  }
  if(fFine(g)){ g.beginPath(); g.moveTo(0.3, -0.3); g.quadraticCurveTo(0.2, 0.1, 0.28, 0.5); g.moveTo(0.1, 0.55); g.quadraticCurveTo(0.0, 0.75, 0.12, 0.92); g.strokeStyle = shade(k.coat, -0.3); g.lineWidth = fLw(g, 0.45); g.stroke(); fHatch(g, -0.25, 0.4, 5, -1); }
  g.fillStyle = BLOOD; g.globalAlpha = 0.85;   // раны
  for(const [x, y, rr] of [[0.06, -0.38, 0.055], [0.16, 0.22, 0.042], [0.0, -0.7, 0.035]]){ g.beginPath(); g.ellipse(x, y, rr, rr * 1.5, 0.4, 0, 6.283); g.fill(); }
  g.globalAlpha = 1;
  g.restore();
  fSmooth(g, F_DHORSE); fEdge(g);
  g.beginPath(); g.ellipse(-0.24, -1.15, 0.03, 0.078, 0.9, 0, 6.283); g.fillStyle = k.bard === "full" ? k.col : k.coat; g.fill(); fEdge(g, 0.5);   // ухо
  fDisc(g, -0.075, -1.32, 0.018, F_INK, 0); fDisc(g, -0.035, -1.56, 0.012, F_INK, 0);   // глаз, ноздря
  if(armoured){ g.beginPath(); g.moveTo(-0.16, -1.2); g.lineTo(-0.06, -1.22); g.lineTo(-0.02, -1.48); g.lineTo(-0.1, -1.5); g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); }
  leg(0.28, -0.52, -0.1, 0.5, near, socks & 2); leg(0.29, 0.5, 0.05, 0.4, near, socks & 8);
  fMute(g);
}
// лужа крови: несколько слившихся пятен (В8)
function fBlood(g, v){
  g.fillStyle = BLOOD; g.globalAlpha = 0.82;
  for(let i = 0; i < 5; i++){ const r = j => hash(v * 17 + i, j); g.beginPath(); g.ellipse((r(1) - 0.5) * 0.35, (r(2) - 0.5) * 0.5, 0.14 + 0.12 * r(3), 0.1 + 0.1 * r(4), r(5) * 3, 0, 6.283); g.fill(); }
  g.globalAlpha = 1;
}

// ═══ Спрайты частей ═══
const fBodySpr = k => spr("fb" + k.bodyKey, [-0.34, -0.18, 0.34, 0.15], g => fBody(g, k));
const fHeadSpr = k => spr("fh" + k.headKey, [-0.2, -0.27, 0.2, 0.26], g => fHead(g, k));
const fShieldSpr = sh => spr("fs" + sh.key, [-0.26, -0.31, 0.26, 0.37], g => fShield(g, sh));
const fShieldTopSpr = sh => spr("ft" + sh.key, [-0.26, -0.11, 0.26, 0.08], g => fShieldTop(g, sh));
const fWeapSpr = (w, col) => spr("fw" + w + (w === "lance" ? col : ""), F_WBOX[w].map((v, i) => v + (i < 2 ? -0.03 : 0.03)), g => fWeapon(g, w, col));
const fBowSpr = (st, kind) => spr("fbow" + st + kind, [-0.5, -1.14, 0.45, 0.22], g => fBow(g, st, kind));
const fXbowSpr = st => spr("fxb" + st, [-0.3, -0.68, 0.38, 0.08], g => fXbow(g, st));
const fArmSpr = k => spr("fa" + fSleeve(k), [-0.07, -FA_LEN - 0.07, 0.07, 0.07], g => fArm(g, k));
const fHandSpr = kind => spr("fhd" + kind, [-0.05, -0.05, 0.05, 0.05], g => fHand(g, kind));
const fBackSpr = k => spr("fk" + [k.back, k.backCol, k.c2, k.style === "fareast"].join(), [-0.3, -0.05, 0.36, 0.36], g => fBack(g, k));
const fBootSpr = () => spr("fboot", [-0.06, -0.09, 0.06, 0.09], fBoot);
const fSampleSpr = col => spr("fsm" + col, [-0.27, -0.16, 0.27, 0.15], g => fSample(g, col));
const fHLegSpr = (k, sock) => spr("fhl" + k.coat + (sock ? "s" : ""), [-0.07, -0.18, 0.07, 0.18], g => fHLeg(g, k, sock));
const fHTailSpr = k => spr("fht" + k.coat, [-0.1, -0.02, 0.1, 0.59], g => fHTail(g, k));
const fHBodySpr = k => spr("fhb" + k.coat, [-0.28, -0.72, 0.28, 0.98], g => fHBody(g, k));
const fHHeadSpr = k => spr("fhh" + k.horseKey, [-0.21, -0.83, 0.21, 0.14], g => fHHead(g, k));
const fHCoverSpr = k => spr("fhc" + k.horseKey, [-0.33, -0.8, 0.33, 1.1], g => fHCover(g, k));
const fRiderSpr = k => spr("frl" + [k.armour, k.cloth].join(), [-0.38, -0.16, 0.38, 0.14], g => fRiderLegs(g, k));
const fCorpseSpr = (k, v) => spr("fc" + k.bodyKey + k.headKey + (k.shield ? k.shield.key : "") + k.weapon + k.side + v, [-1.05, -1.0, 1.05, 1.0], g => fCorpse(g, k, v));
const fCrawlSpr = k => spr("fcr" + k.bodyKey + k.headKey, [-0.5, -1.06, 0.45, 0.95], g => fCrawl(g, k));
const fDeadHorseSpr = k => spr("fdh" + k.horseKey, [-0.52, -1.68, 0.98, 1.58], g => fDeadHorse(g, k));
const fBloodSpr = v => spr("fbl" + v, [-0.5, -0.6, 0.5, 0.6], g => fBlood(g, v));

function fImg(img, m, x, y, w, h){ ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]); ctx.drawImage(img, x, y, w, h); }
// мягкая тень: форма — по телу, сдвиг — в мире (свет один для всех)
function fShadow(B, x, y, face, a = 1){
  ctx.globalAlpha = 0.75 * a; fImg(SHADOW, mR(mT(B, x + 0.045, y + 0.065), face), -0.36, -0.24, 0.72, 0.48); ctx.globalAlpha = 1;
}

// ═══ Боец целиком ═══
// o: step — фаза шага −1…1 (0 — стоит), ap — доля удара 0…1 (нет — не бьёт), kind — thrust/swing/chop, low — опущены ли
// древки (0 — стоймя), rank — шеренга, bowSt и xb — ступени лука и арбалета, sparks — куда класть вспышки, lean — отшатнулся
// от удара 0…1; mounted — в седле (B — оси коня), run — конь скачет (копьё опущено)
function fMan(B, x, y, face, k, o = {}){
  const st = o.step || 0, M0 = mR(mT(B, x, y), face), atk = o.ap !== undefined && o.ap >= 0, ap = o.ap, kb = fKB(k), wb = kb.weapon;
  // издали (меньше 16 px/м) — как у образца: капсула со шлемом; из оружия — только древки передних шеренг и опущенное копьё
  if(view.s < 16){
    if(!o.mounted) fShadow(B, x, y, face);
    const P = restPose(kb, o.rank || 0, o.low ?? 1), pole = THRUST.has(wb) && (o.mounted ? o.run : (o.rank || 0) < 2);
    put(fBodySpr(k), M0); put(fHeadSpr(k), mT(M0, F_HEAD[0], F_HEAD[1]));
    if(pole && P.W) put(fWeapSpr(k.weapon, k.col), mP(M0, o.mounted ? [0.2, 0.25, -0.04, 1, 1] : P.W));
    return;
  }
  const sway = (o.mounted ? 0 : 0.065 * st) + (atk ? (o.kind === "swing" ? 0.16 : o.kind === "chop" ? -0.06 : 0.05) * Math.sin(ap * 6.283) : 0);
  const lunge = (atk && ap > 0.3 && ap < 0.55 ? -0.05 : 0) + 0.16 * (o.lean || 0);
  const Mm = mR(mT(M0, 0, lunge), sway + 0.25 * (o.lean || 0));
  if(o.mounted) put(fRiderSpr(k), M0);
  else {
    fShadow(B, x, y, face);
    const fs = st || (atk ? 0.6 * Math.sin(ap * 6.283) : 0);   // ступни: на ходу выглядывают из-под плеч вперёд и назад
    if(fs){ const bt = fBootSpr(); put(bt, mT(M0, -0.085, 0.02 + 0.15 * fs)); put(bt, mT(M0, 0.085, 0.02 - 0.15 * fs)); }
  }
  // поза оружия и щита: в руке щит — ребром перед собой, у всадника — вдоль левого бока
  const P = restPose(kb, o.rank || 0, o.low ?? 1);
  let W = P.W, Sh = P.Sh && (o.mounted ? [-0.29, 0.05, Math.PI / 2, 1, 1] : k.shield.shape === "buckler" ? [-0.25, -0.12, -0.2, 1, 1] : [-0.24, -0.15, -0.6, 0.9, 1]);
  if(Sh && atk && !o.mounted){ const c = Math.max(0, Math.sin((ap + 0.5) * 6.283)); Sh = [Sh[0] + 0.04 + 0.05 * c, Sh[1] - 0.06 - 0.09 * c, Sh[2] + 0.15 + 0.2 * c, Sh[3], Sh[4]]; }
  if(o.mounted && wb === "lance"){ if(o.run) W = [0.2, 0.25 + (atk ? thrustOff(ap) : 0), -0.04, 1, 1]; }
  else if(atk && W){
    if(o.kind === "thrust") W = THRUST.has(wb) ? [W[0], (wb === "pike" ? 0 : -0.1) + thrustOff(ap), 0, 1, 1] : [0.16, -0.14 + 0.8 * thrustOff(ap), 0.04, 1, 1];
    else if(o.kind === "chop"){ const [y2, sy2] = chopPose(ap); W = [0.2, y2, 0.1, 1, sy2]; }
    else { const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
  } else if(W && st) W = [W[0], W[1], W[2] + 0.03 * st, W[3], W[4]];
  if(atk && o.sparks && ap >= 0.42 && ap < 0.5 && (W || o.mounted))
    o.sparks.push([Mm, o.kind === "thrust" || (o.mounted && wb === "lance") ? (wb === "pike" ? -3.7 : wb === "lance" ? -2.6 : THRUST.has(wb) ? -1.35 : -0.85) : -0.75, ap]);
  // лук в покое — опущен у левого бока вдоль тела; поперёк — только когда стреляет
  const bowRest = wb === "bow" && !(o.bowSt > 0), bowKind = k.weapon === "bow" ? "bow" : k.weapon;
  const [hr, hl, showL] = bowRest ? [null, [-0.36, 0.02], true] : handsOf(kb, W, Sh, o.bowSt || 0, o.xb || 0);
  if(k.back !== "none") put(fBackSpr(k), Mm);
  const arm = fArmSpr(k);
  for(const [side, h] of [[1, hr], [-1, hl]]) if(h){
    const ex = side * ELB[0], ey = ELB[1], dx = h[0] - ex, dy = h[1] - ey, L = Math.hypot(dx, dy);
    if(L >= 0.02) put(arm, mS(mR(mT(Mm, ex, ey), Math.atan2(dx, -dy)), 1, L / FA_LEN));
  }
  put(fBodySpr(k), Mm);
  if(Sh) put(fShieldTopSpr(k.shield), mP(Mm, Sh));
  put(fHeadSpr(k), mR(mT(Mm, F_HEAD[0], F_HEAD[1]), -0.4 * sway));
  if(bowRest) put(fBowSpr(0, bowKind), mS(mR(mT(Mm, -0.08, 0.02), -Math.PI / 2), 0.8, 0.8));
  else if(wb === "bow") put(fBowSpr(o.bowSt || 0, bowKind), Mm);
  else if(k.weapon === "crossbow") put(fXbowSpr(o.xb || 0), Mm);
  else if(W) put(fWeapSpr(k.weapon, k.col), mP(Mm, W));
  const hk = fHandSpr(handKind(k));
  if(hr) put(hk, mT(Mm, hr[0], hr[1])); if(hl && showL) put(hk, mT(Mm, hl[0], hl[1]));
}
// всадник на коне. o: v — скорость, м/с; ph — фаза аллюра (пройденный путь ÷ длина круга); t — время; seed; остальное — как у fMan
function fCav(B, x, y, face, k, o = {}){
  const P = horsePose(o.v || 0, o.ph || 0, o.t || 0, o.seed || 0), base = mR(mT(B, x, y), face);
  ctx.globalAlpha = 0.7; fImg(SHADOW, mR(mT(B, x + 0.08, y + 0.1), face), -0.45, -1.05, 0.9, 2.25); ctx.globalAlpha = 1;
  if(view.s >= 16){
    HLEG.forEach(([lx, ly], i) => put(fHLegSpr(k, k.socks & (1 << i)), mT(base, lx, ly + P.legs[i])));
    put(fHTailSpr(k), mR(mT(base, HTAIL[0], HTAIL[1]), P.tail));
  }
  put(fHHeadSpr(k), mT(base, HNECK[0], HNECK[1] + P.nod));   // шея уходит под грудь
  put(fHBodySpr(k), base);
  put(fHCoverSpr(k), base);
  if(view.s >= 30){   // поводья: от колец удил к рукам всадника — две тонкие линии над шеей
    ctx.setTransform(base[0], base[1], base[2], base[3], base[4], base[5]); ctx.beginPath();
    for(const s of [-1, 1]){ ctx.moveTo(s * 0.1, HNECK[1] + P.nod - 0.69); ctx.quadraticCurveTo(s * 0.1, -0.5, s * 0.07, -0.13); }
    ctx.strokeStyle = F_TACK; ctx.lineWidth = 0.9 / view.s; ctx.stroke();
  }
  fMan(base, 0, 0.03 - (P.bob || 0) * 0.5, 0, k, Object.assign({}, o, {mounted: true}));
}
// падение: 0…0,3 — отшатнулся от удара; дальше фигурка «разворачивается» в лежащую — от ступней назад, головой от удара
function fFall(B, x, y, face, k, u, v){
  if(u < 0.3){ fMan(B, x, y, face, k, {lean: u / 0.3, low: 0}); return; }
  const e = ease(Math.min(1, (u - 0.3) / 0.45)), M0 = mR(mT(B, x, y), face);
  put(fCorpseSpr(k, v), mT(mS(mR(M0, Math.PI), 1, 0.3 + 0.7 * e), 0, -0.8));
}
function fSparks(list){
  for(const [Mm, reach, ap] of list){ const e = (ap - 0.42) / 0.08, k2 = 0.6 + 0.8 * e; ctx.globalAlpha = 1 - e; put(sparkSpr(), mS(mT(Mm, 0.2, reach), k2, k2)); ctx.globalAlpha = 1; }
  list.length = 0;
}
// какой удар: колющие колют; алебарда и нагината — то сбоку, то сверху; мечи и сабли — чаще рубят; топоры, булавы — сверху и сбоку
function fKind(k, n, s){
  const w = k.weapon, b = fBase(w);
  if(w === "halberd" || w === "naginata" || w === "daneaxe") return hash(s * 7 + n, 42) < 0.5 ? "swing" : "chop";
  if(THRUST.has(b)) return "thrust";
  if(b === "sword") return hash(s * 7 + n, 41) < 0.35 ? "thrust" : "swing";
  return hash(s * 7 + n, 42) < 0.5 ? "chop" : "swing";
}
