// ═══════════ polygon-men-flat.js — бойцы «как у Iron Kings» (В18): строго сверху, плоско ═══════════
// Проба для men-flat.html. Подключается после polygon-men.js и берёт из него снаряжение (kitsOf), позы оружия (restPose,
// handsOf, удары), спрайты и матрицы (spr, put, mT/mR/mS/mP). Рисует по-своему: капсула плеч 2 : 1 с двумя линиями
// и косыми штрихами, круглая голова или шлем, тонкий тёмно-коричневый контур, плоская заливка, мягкая тень.
// Обмер образца — docs/iron-kings.md, раздел 8. Оси — как в polygon-men.js: метры, вперёд — −y, начало — середина плеч.

const F_INK = "#2c1f19", F_INK_A = "rgba(44,31,25,.5)";
const F_A = 0.25, F_B = 0.125;                 // капсула: полудлина (плечи 0,5 м), полуглубина
const F_HEAD = [0, -0.03, 0.106];              // голова: центр (чуть вперёд) и радиус — 0,85 ширины капсулы
const F_STEEL = "#b4b9bc", F_STEEL_D = "#868c90", F_SHINE = "#f1f3f4", F_MAIL = "#a2a7aa", F_MAIL_D = "#777c80";
const F_WOOD = "#8f6c44", F_BOOT = "#3d2e23";
const F_HAND = {skin: "#d6a67e", glove: "#6a5038", plate: F_STEEL};

// Контур — почти постоянной толщины на экране: 1 px вдали, до ~3 px вплотную (у образца так же). k — доля от контура тела.
function fLw(g, k = 1){
  const S = Math.abs(g.getTransform().a) || 40, d = window.devicePixelRatio || 1, css = S / d;
  return k * Math.max(1.05, Math.min(3.4, 0.4 + 0.0155 * css)) * d / S;
}
const fFine = g => Math.abs(g.getTransform().a) / (window.devicePixelRatio || 1) >= 20;   // мелочь (швы, штрихи, заклёпки) — ближе 20 px/м
function fEdge(g, k = 1, col = F_INK){ g.strokeStyle = col; g.lineWidth = fLw(g, k); g.stroke(); }
function fDisc(g, x, y, r, col, k = 1){ g.beginPath(); g.arc(x, y, r, 0, 6.283); g.fillStyle = col; g.fill(); if(k) fEdge(g, k); }
function fStick(g, x0, y0, x1, y1, w, col){   // древко, рукоять: контур — полоса шире на два контура
  g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.lineCap = "round";
  g.strokeStyle = F_INK; g.lineWidth = w + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = col; g.lineWidth = w; g.stroke();
}
function fCapsule(g, a = F_A, b = F_B){
  g.beginPath(); g.moveTo(-a + b, -b); g.lineTo(a - b, -b); g.arc(a - b, 0, b, -Math.PI / 2, Math.PI / 2);
  g.lineTo(-a + b, b); g.arc(-a + b, 0, b, Math.PI / 2, Math.PI * 1.5); g.closePath();
}
// «рукописная» тень: несколько коротких косых штрихов
function fHatch(g, x0, y0, n = 5, dir = 1){
  g.beginPath();
  for(let i = 0; i < n; i++){ const x = x0 + i * 0.021; g.moveTo(x, y0 + 0.02 * dir); g.lineTo(x + 0.02, y0 - 0.022 * dir); }
  g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.42); g.stroke();
}
function fSeams(g){   // две линии вдоль плеч чуть позади середины головы
  g.beginPath(); g.moveTo(-1, -0.011); g.lineTo(1, -0.011); g.moveTo(-1, 0.014); g.lineTo(1, 0.014);
  g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.45); g.stroke();
}

// образец по обмеру: капсула цвета стороны и голова того же цвета чуть темнее
function fSample(g, col){
  fCapsule(g); g.fillStyle = col; g.fill();
  if(fFine(g)){ g.save(); fCapsule(g); g.clip(); fSeams(g); fHatch(g, 0.07, -0.1); fHatch(g, -0.19, 0.1, 5, -1); g.restore(); }
  fCapsule(g); fEdge(g);
  fDisc(g, F_HEAD[0], F_HEAD[1], F_HEAD[2], shade(col, -0.05), 0.55);
}

// ── Тело: капсула плеч по доспеху. Ткань — цвет одежды (у своих — цвет стороны); бригантина — ткань с заклёпками;
// кольчуга и латы — на плечах, посередине сюрко цвета стороны с гербом; вокруг головы — бармица ──
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
function fBody(g, k){
  const metal = k.armour === "mail" || k.armour === "plate";
  if(metal) fDisc(g, F_HEAD[0], F_HEAD[1], 0.135, F_MAIL, 0.5);   // бармица — кольцом вокруг шлема
  fCapsule(g); g.fillStyle = k.armour === "mail" ? F_MAIL : k.armour === "plate" ? F_STEEL : k.cloth; g.fill();
  g.save(); fCapsule(g); g.clip();
  if(metal){
    const w = 0.14;
    if(k.armour === "mail" && fFine(g)){   // кольчуга: ряды дужек на плечах
      g.beginPath();
      for(let y = -0.11, row = 0; y < 0.13; y += 0.032, row++)
        for(let x = -0.25 + (row % 2) * 0.016; x < 0.25; x += 0.032){ if(Math.abs(x) < w) continue; g.moveTo(x - 0.011, y); g.arc(x, y, 0.011, Math.PI, 0, true); }
      g.strokeStyle = F_MAIL_D; g.lineWidth = fLw(g, 0.4); g.stroke();
    } else for(const s of [-1, 1]){   // латы: наплечник с ободом и бликом
      g.beginPath(); g.arc(s * 0.2, 0, 0.075, 0, 6.283); fEdge(g, 0.45, F_STEEL_D);
      g.beginPath(); g.arc(s * 0.2, 0, 0.05, Math.PI * 1.1, Math.PI * 1.6); g.strokeStyle = F_SHINE; g.lineWidth = fLw(g, 0.8); g.stroke();
    }
    g.save(); g.beginPath(); g.rect(-w, -1, 2 * w, 2); g.fillStyle = k.col; g.fill(); g.clip(); fDevice(g, k.tabard, k.c2); g.restore();
    g.beginPath(); g.moveTo(-w, -1); g.lineTo(-w, 1); g.moveTo(w, -1); g.lineTo(w, 1); fEdge(g, 0.5);
  } else if(k.armour === "leather" && fFine(g)){   // бригантина: два ряда заклёпок
    g.fillStyle = "#e3dccb";
    for(const y of [-0.07, 0.075]) for(let x = -0.19; x <= 0.19; x += 0.038){ g.beginPath(); g.arc(x, y, 0.008, 0, 6.283); g.fill(); }
  }
  if(fFine(g)){ fSeams(g); fHatch(g, 0.07, -0.1); fHatch(g, -0.19, 0.1, 5, -1); }
  g.restore();
  fCapsule(g); fEdge(g);
}

// ── Голова и шлемы (центр головы — начало осей) ──
function fHead(g, k){
  const r = F_HEAD[2], c = k.helmCol;
  const shine = (x, y, rad) => { g.beginPath(); g.arc(x, y, rad, Math.PI * 1.08, Math.PI * 1.55); g.strokeStyle = F_SHINE; g.lineWidth = fLw(g, 0.9); g.stroke(); };
  const line = (x0, y0, x1, y1, col, k2 = 0.6) => { g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.strokeStyle = col; g.lineWidth = fLw(g, k2); g.stroke(); };
  switch(k.helm){
    case "hair":
      fDisc(g, 0, 0.005, r * 0.95, c, 0.55);
      g.beginPath(); for(const x of [-0.04, 0, 0.04]){ g.moveTo(x * 0.6, -0.07); g.quadraticCurveTo(x * 1.3, 0, x, 0.08); }
      g.strokeStyle = shade(c, -0.35); g.lineWidth = fLw(g, 0.45); g.stroke(); break;
    case "cap":
      fDisc(g, 0, 0, r, c, 0.55); g.beginPath(); g.arc(0, 0, r * 0.7, 0, 6.283); fEdge(g, 0.4, shade(c, -0.35)); break;
    case "hood":   // капюшон с хвостиком на спину
      g.beginPath(); g.moveTo(r, 0); g.arc(0, 0, r, 0, Math.PI, true); g.quadraticCurveTo(-0.07, 0.14, 0, 0.21); g.quadraticCurveTo(0.07, 0.14, r, 0);
      g.fillStyle = c; g.fill(); fEdge(g, 0.55);
      g.beginPath(); g.arc(0, 0, r * 0.68, Math.PI * 1.15, Math.PI * 1.85); fEdge(g, 0.4, shade(c, -0.35)); break;
    case "kettle":   // шапель: широкие поля и купол
      fDisc(g, 0, 0, 0.155, F_STEEL, 0.55); fDisc(g, 0, 0, 0.09, shade(F_STEEL, 0.1), 0.45); shine(0, 0, 0.06); break;
    case "capSteel":
      fDisc(g, 0, 0, r, F_STEEL, 0.55); g.beginPath(); g.arc(0, 0, 0.068, 0, 6.283); fEdge(g, 0.45, F_STEEL_D); shine(0, 0, 0.05); break;
    case "nasal":    // наносник вперёд, гребень-шов
      g.beginPath(); g.rect(-0.016, -r - 0.035, 0.032, 0.05); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.5);
      fDisc(g, 0, 0, r, F_STEEL, 0.55); line(0, -r, 0, r, F_STEEL_D, 0.7); shine(0, 0, 0.065); break;
    case "great":    // топфхельм: плоский верх с ободом, спереди — тёмная щель, у некоторых — гребень цвета отряда
      fDisc(g, 0, 0, 0.12, "#a1a6aa", 0.6); g.beginPath(); g.arc(0, 0, 0.086, 0, 6.283); fEdge(g, 0.45, "#737a7f");
      g.beginPath(); g.arc(0, 0, 0.12, -Math.PI * 0.75, -Math.PI * 0.25); g.strokeStyle = F_INK; g.lineWidth = fLw(g, 1.3); g.stroke();
      if(k.crest){ g.beginPath(); g.ellipse(0, 0.01, 0.028, 0.1, 0, 0, 6.283); g.fillStyle = k.crest; g.fill(); fEdge(g, 0.5); }
      else shine(0, 0, 0.07); break;
    case "bascinet": // бацинет с «клювом» забрала вперёд
      fDisc(g, 0, 0.01, r, F_STEEL, 0.55);
      g.beginPath(); g.moveTo(-0.065, -0.065); g.quadraticCurveTo(-0.035, -0.17, 0, -0.195); g.quadraticCurveTo(0.035, -0.17, 0.065, -0.065); g.closePath();
      g.fillStyle = F_STEEL_D; g.fill(); fEdge(g, 0.55);
      g.fillStyle = F_INK; for(const x of [-0.018, 0.018]){ g.beginPath(); g.arc(x, -0.13, 0.008, 0, 6.283); g.fill(); }
      shine(0, 0.01, 0.065); break;
    case "morion":   // поля лодочкой спереди назад, гребень
      g.beginPath(); g.ellipse(0, 0, 0.115, 0.185, 0, 0, 6.283); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.55);
      line(0, -0.175, 0, 0.175, F_STEEL_D, 1.3); shine(0, 0, 0.07); break;
    default: fDisc(g, 0, 0, r, c, 0.55);
  }
}

// ── Щит: плоское поле, герб, обод; у круглых и овальных — умбон ──
function fShield(g, sh){
  const field = sh.paint === "wood" ? "#a8865a" : sh.paint === "steel" ? F_STEEL : sh.c1;
  shieldPath(g, sh.shape); g.fillStyle = field; g.fill();
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
  if(sh.paint !== "steel"){ shieldPath(g, sh.shape); g.strokeStyle = sh.shape === "heater" ? "#6a4a2c" : F_STEEL_D; g.lineWidth = 0.035; g.stroke(); }
  shieldPath(g, sh.shape); fEdge(g);
  if(sh.shape !== "heater"){ fDisc(g, 0, 0, sh.shape === "buckler" ? 0.05 : 0.06, F_STEEL, 0.6); g.beginPath(); g.arc(0, 0, 0.035, Math.PI * 1.1, Math.PI * 1.6); g.strokeStyle = F_SHINE; g.lineWidth = fLw(g, 0.8); g.stroke(); }
}

// Щит в руке сверху (В18): держат перед собой стоймя — сверху видно ребро: узкая линза цвета поля, герб — полосами
// поперёк, у круглых — умбон вперёд. Плашмя щит лежит только у павших.
function fShieldTop(g, sh){
  const w = sh.shape === "buckler" ? 0.13 : sh.shape === "round" ? 0.24 : sh.shape === "oval" ? 0.19 : 0.2, d = 0.055;
  const field = sh.paint === "wood" ? "#a8865a" : sh.paint === "steel" ? F_STEEL : sh.c1;
  const lens = () => { g.beginPath(); g.ellipse(0, 0, w, d, 0, 0, 6.283); };
  if(sh.shape !== "heater") fDisc(g, 0, -d, 0.04, F_STEEL, 0.55);   // умбон
  lens(); g.fillStyle = field; g.fill();
  g.save(); lens(); g.clip(); g.fillStyle = sh.c2;
  switch(sh.paint){
    case "halves": g.fillRect(0, -1, 1, 2); break;
    case "quarters": g.fillRect(0, -1, 1, 1); g.fillRect(-1, 0, 1, 1); break;
    case "cross": case "stripe": g.fillRect(-0.03, -1, 0.06, 2); break;
    case "chevron": g.beginPath(); g.moveTo(-w, 1); g.lineTo(0, -0.02); g.lineTo(w, 1); g.lineTo(w, -1); g.lineTo(-w, -1); g.closePath(); g.fill(); break;
    case "boss": g.fillRect(-w * 0.62, -1, 0.035, 2); g.fillRect(w * 0.62 - 0.035, -1, 0.035, 2); break;
    case "wood": g.beginPath(); for(let x = -0.18; x < 0.25; x += 0.09){ g.moveTo(x, -1); g.lineTo(x, 1); } g.strokeStyle = F_INK_A; g.lineWidth = fLw(g, 0.45); g.stroke(); break;
  }
  g.restore();
  lens(); fEdge(g, 0.9);
}

// ── Оружие: в своих осях, рукоять в начале (там кисть), острие вперёд (−y) ──
function fTip(g, y, len, w = 0.032){ g.beginPath(); g.moveTo(-w, y); g.quadraticCurveTo(-w * 0.8, y - len * 0.6, 0, y - len); g.quadraticCurveTo(w * 0.8, y - len * 0.6, w, y); g.lineTo(0, y + 0.03); g.closePath(); g.fillStyle = F_STEEL; g.fill(); fEdge(g, 0.6); }
function fWeapon(g, w, col){
  switch(w){
    case "spear": fStick(g, 0, 0.55, 0, -1.25, 0.03, F_WOOD); fTip(g, -1.25, 0.2); break;
    case "pike": fStick(g, 0, 0.7, 0, -3.8, 0.034, F_WOOD); fTip(g, -3.8, 0.22, 0.028); break;
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
// лук и арбалет — в осях бойца, ступени как в polygon-men.js (paintBow, paintXbow)
function fBow(g, st){
  const d = [0, 0.15, 0.55, 1, 0][st], tx = 0.3 - 0.06 * d, ty = -0.2 - 0.02 * d, cy = -0.56 - 0.1 * d, ny = ty + 0.36 * d;
  g.beginPath(); g.moveTo(-tx, ty); g.lineTo(0.02, ny); g.lineTo(tx, ty); g.strokeStyle = "#efe9dc"; g.lineWidth = Math.max(0.01, fLw(g, 0.5)); g.stroke();
  if(st >= 1 && st <= 3){ fStick(g, 0.02, ny + 0.02, 0.02, ny - 0.72, 0.012, "#e2d6bd"); fTip(g, ny - 0.72, 0.07, 0.022); }
  g.beginPath(); g.moveTo(-tx, ty); g.quadraticCurveTo(0, cy, tx, ty);
  g.strokeStyle = F_INK; g.lineWidth = 0.04 + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = F_WOOD; g.lineWidth = 0.04; g.stroke();
}
function fXbow(g, st){
  fStick(g, 0.05, 0.02, 0.05, -0.5, 0.055, F_WOOD);
  g.beginPath(); g.moveTo(-0.22, -0.45); g.lineTo(0.05, st ? -0.45 : -0.3); g.lineTo(0.32, -0.45); g.strokeStyle = "#efe9dc"; g.lineWidth = Math.max(0.01, fLw(g, 0.5)); g.stroke();
  if(!st){ fStick(g, 0.05, -0.29, 0.05, -0.58, 0.014, "#e2d6bd"); fTip(g, -0.58, 0.06, 0.02); }
  g.beginPath(); g.moveTo(-0.22, -0.45); g.quadraticCurveTo(0.05, -0.58, 0.32, -0.45);
  g.strokeStyle = F_INK; g.lineWidth = 0.04 + 2 * fLw(g, 0.7); g.stroke(); g.strokeStyle = F_STEEL; g.lineWidth = 0.04; g.stroke();
}
// рука: предплечье от локтя к кисти (под телом), кисть — поверх оружия
function fArm(g, k){ fStick(g, 0, 0, 0, -FA_LEN, 0.07, k.armour === "mail" ? F_MAIL : k.armour === "plate" ? F_STEEL : k.armour === "leather" ? k.leather : k.cloth); }
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
      g.beginPath(); g.rect(-0.05, -0.15, 0.1, 0.29); g.fillStyle = "#74512f"; g.fill(); fEdge(g, 0.7);
      for(const x of [-0.022, 0.004, 0.028]){ g.beginPath(); g.ellipse(x, -0.165, 0.014, 0.024, 0, 0, 6.283); g.fillStyle = "#f2ede2"; g.fill(); }
      g.restore(); break;
    case "pavise":
      g.beginPath(); g.rect(-0.26, 0.11, 0.52, 0.075); g.fillStyle = k.backCol; g.fill();
      g.fillStyle = k.c2; g.fillRect(-0.04, 0.11, 0.08, 0.075); g.beginPath(); g.rect(-0.26, 0.11, 0.52, 0.075); fEdge(g, 0.8); break;
  }
}

// ── Спрайты частей ──
const fBodySpr = k => spr("fb" + k.bodyKey, [-0.27, -0.18, 0.27, 0.15], g => fBody(g, k));
const fHeadSpr = k => spr("fh" + [k.helm, k.helmCol, k.crest].join(), [-0.17, -0.22, 0.17, 0.22], g => fHead(g, k));
const fShieldSpr = sh => spr("fs" + sh.key, [-0.26, -0.31, 0.26, 0.32], g => fShield(g, sh));
const fShieldTopSpr = sh => spr("ft" + sh.key, [-0.26, -0.11, 0.26, 0.08], g => fShieldTop(g, sh));
const fWeapSpr = (w, col) => spr("fw" + w + (w === "lance" ? col : ""), WBOX[w].map((v, i) => v + (i < 2 ? -0.03 : 0.03)), g => fWeapon(g, w, col));
const fBowSpr = st => spr("fbow" + st, [-0.38, -1.14, 0.38, 0.22], g => fBow(g, st));
const fXbowSpr = st => spr("fxb" + st, [-0.3, -0.68, 0.38, 0.08], g => fXbow(g, st));
const fArmSpr = k => spr("fa" + [k.armour, k.leather, k.cloth].join(), [-0.07, -FA_LEN - 0.07, 0.07, 0.07], g => fArm(g, k));
const fHandSpr = kind => spr("fhd" + kind, [-0.05, -0.05, 0.05, 0.05], g => fHand(g, kind));
const fBackSpr = k => spr("fk" + [k.back, k.backCol, k.c2].join(), [-0.3, -0.05, 0.32, 0.36], g => fBack(g, k));
const fBootSpr = () => spr("fboot", [-0.06, -0.09, 0.06, 0.09], fBoot);
const fSampleSpr = col => spr("fsm" + col, [-0.27, -0.16, 0.27, 0.15], g => fSample(g, col));

function fImg(img, m, x, y, w, h){ ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]); ctx.drawImage(img, x, y, w, h); }
// мягкая тень: форма — по телу, сдвиг — в мире (свет один для всех)
function fShadow(B, x, y, face, a = 1){
  ctx.globalAlpha = 0.75 * a; fImg(SHADOW, mR(mT(B, x + 0.045, y + 0.065), face), -0.36, -0.24, 0.72, 0.48); ctx.globalAlpha = 1;
}

// ── Боец целиком. o: step — фаза шага −1…1 (0 — стоит), ap — доля удара 0…1 (нет — не бьёт), kind — укол/рубка,
// low — опущены ли древки (0 — стоймя), rank — шеренга, bowSt и xb — ступени лука и арбалета, sparks — куда класть вспышки ──
function fMan(B, x, y, face, k, o = {}){
  const st = o.step || 0, M0 = mR(mT(B, x, y), face), atk = o.ap !== undefined && o.ap >= 0, ap = o.ap;
  // издали (меньше 16 px/м) — как у образца: капсула со шлемом; из оружия — только древки передних шеренг
  if(view.s < 16){
    fShadow(B, x, y, face);
    const pole = THRUST.has(k.weapon) && (o.rank || 0) < 2, P = pole && restPose(k, o.rank || 0, o.low ?? 1);
    put(fBodySpr(k), M0); put(fHeadSpr(k), mT(M0, F_HEAD[0], F_HEAD[1]));
    if(pole && P.W) put(fWeapSpr(k.weapon, k.col), mP(M0, P.W));
    return;
  }
  const sway = 0.065 * st + (atk ? (o.kind === "swing" ? 0.16 : o.kind === "chop" ? -0.06 : 0.05) * Math.sin(ap * 6.283) : 0);
  const lunge = atk && ap > 0.3 && ap < 0.55 ? -0.05 : 0;
  const Mm = mR(mT(M0, 0, lunge), sway);
  fShadow(B, x, y, face);
  // ступни: на ходу выглядывают из-под плеч вперёд и назад
  const fs = st || (atk ? 0.6 * Math.sin(ap * 6.283) : 0);
  if(fs && view.s >= 14){ const bt = fBootSpr(); put(bt, mT(M0, -0.085, 0.02 + 0.15 * fs)); put(bt, mT(M0, 0.085, 0.02 - 0.15 * fs)); }
  // поза оружия и щита
  const P = restPose(k, o.rank || 0, o.low ?? 1);
  let W = P.W, Sh = P.Sh && (k.shield.shape === "buckler" ? [-0.25, -0.12, -0.2, 1, 1] : [-0.24, -0.15, -0.6, 0.9, 1]);
  if(Sh && atk){ const c = Math.max(0, Math.sin((ap + 0.5) * 6.283)); Sh = [Sh[0] + 0.04 + 0.05 * c, Sh[1] - 0.06 - 0.09 * c, Sh[2] + 0.15 + 0.2 * c, Sh[3], Sh[4]]; }
  if(atk && W){
    const wpn = k.weapon;
    if(o.kind === "thrust") W = THRUST.has(wpn) ? [W[0], (wpn === "pike" ? 0 : -0.1) + thrustOff(ap), 0, 1, 1] : [0.16, -0.14 + 0.8 * thrustOff(ap), 0.04, 1, 1];
    else if(o.kind === "chop"){ const [y2, sy2] = chopPose(ap); W = [0.2, y2, 0.1, 1, sy2]; }
    else { const [r2, sy2] = swingAng(ap); W = [0.22, -0.08, r2, 1, sy2]; }
    if(o.sparks && ap >= 0.42 && ap < 0.5) o.sparks.push([Mm, o.kind === "thrust" ? (wpn === "pike" ? -3.7 : THRUST.has(wpn) ? -1.35 : -0.85) : -0.75, ap]);
  } else if(W && st) W = [W[0], W[1], W[2] + 0.03 * st, W[3], W[4]];
  // лук в покое — опущен у левого бока вдоль тела; поперёк — только когда стреляет
  const bowRest = k.weapon === "bow" && !(o.bowSt > 0);
  const [hr, hl, showL] = bowRest ? [null, [-0.36, 0.02], true] : handsOf(k, W, Sh, o.bowSt || 0, o.xb || 0);
  if(k.back !== "none") put(fBackSpr(k), Mm);
  const arm = fArmSpr(k);
  for(const [side, h] of [[1, hr], [-1, hl]]) if(h){
    const ex = side * ELB[0], ey = ELB[1], dx = h[0] - ex, dy = h[1] - ey, L = Math.hypot(dx, dy);
    if(L >= 0.02) put(arm, mS(mR(mT(Mm, ex, ey), Math.atan2(dx, -dy)), 1, L / FA_LEN));
  }
  put(fBodySpr(k), Mm);
  if(Sh) put(fShieldTopSpr(k.shield), mP(Mm, Sh));
  put(fHeadSpr(k), mR(mT(Mm, F_HEAD[0], F_HEAD[1]), -0.4 * sway));
  if(bowRest) put(fBowSpr(0), mS(mR(mT(Mm, -0.08, 0.02), -Math.PI / 2), 0.8, 0.8));
  else if(k.weapon === "bow") put(fBowSpr(o.bowSt || 0), Mm);
  else if(k.weapon === "crossbow") put(fXbowSpr(o.xb || 0), Mm);
  else if(W) put(fWeapSpr(k.weapon, k.col), mP(Mm, W));
  const hk = fHandSpr(handKind(k));
  if(hr) put(hk, mT(Mm, hr[0], hr[1])); if(hl && showL) put(hk, mT(Mm, hl[0], hl[1]));
}
function fSparks(list){
  for(const [Mm, reach, ap] of list){ const e = (ap - 0.42) / 0.08, k2 = 0.6 + 0.8 * e; ctx.globalAlpha = 1 - e; put(sparkSpr(), mS(mT(Mm, 0.2, reach), k2, k2)); ctx.globalAlpha = 1; }
  list.length = 0;
}
