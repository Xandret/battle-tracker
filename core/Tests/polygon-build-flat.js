// ═══════════ polygon-build-flat.js — крепости, осадные машины, лагерь, город «как у Iron Kings» (В19) ═══════════
// Проба для build-flat.html; подключается после polygon-men.js и polygon-men-flat.js (хэш, смешение цветов, толщина
// контура по масштабу — fScale). Рисунок — строго сверху и плоско, как у бойцов В18, но «тяжелее»: толстый неровный
// тёмный контур от руки, заливка с зерном бумаги, скаты крыш в два тона (свет справа сверху — грани туда светлее),
// мягкие тени влево вниз — тем длиннее, чем выше предмет. Всё в метрах карты; g уже переведён в метры.

const B_INK = "#22180f", B_INK_A = "rgba(34,24,15,.5)";
const B_STONE = "#d9d3c5", B_STONE_D = "#aaa18f", B_STONE_L = "#efeae0";
const B_WOOD = "#9a7448", B_WOOD_D = "#6b4d30", B_WOOD_L = "#bd9663", B_IRON = "#55575a", B_STEEL = "#9ea4a8", B_ROPE = "#cdb98f";
const B_ROOF = {tile: "#bd6440", slate: "#6e7a87", thatch: "#cfae5e", shingle: "#8d6741", lead: "#8e959b"};
const B_WATER = "#5f8fa0", B_CANVAS = "#e6dcc4", B_HIDE = "#8c6a48", B_BRONZE = "#a87d3e";
const bDpr = () => window.devicePixelRatio || 1;

// контур построек толще, чем у бойцов: ~1,3 px вдали, до ~4 px вплотную
function bLw(g, k = 1){ const S = fScale(g), css = S / bDpr(); return k * Math.max(1.3, Math.min(4.4, 0.8 + 0.04 * css)) * bDpr() / S; }
// неровная ломаная «от руки»: рёбра делятся на куски ~0,7 м, середины кусков сдвинуты поперёк на долю контура
function bRough(g, pts, closed = true, seed = 1, amp = null){
  const a = amp ?? bLw(g, 0.85), n = pts.length, out = [];
  for(let i = 0; i < (closed ? n : n - 1); i++){
    const [x0, y0] = pts[i], [x1, y1] = pts[(i + 1) % n], L = Math.hypot(x1 - x0, y1 - y0) || 1, m = Math.max(1, Math.round(L / 0.7));
    const nx = -(y1 - y0) / L, ny = (x1 - x0) / L;
    for(let k = 0; k < m; k++){ const u = k / m, j = k ? (hash(seed * 7 + i * 37, k) - 0.5) * a : 0; out.push([x0 + (x1 - x0) * u + nx * j, y0 + (y1 - y0) * u + ny * j]); }
  }
  if(!closed) out.push(pts[n - 1]);
  g.beginPath(); out.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); if(closed) g.closePath();
}
const bPoly = (g, pts) => { g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.closePath(); };
function bInk(g, k = 1, col = B_INK){ g.lineJoin = "round"; g.lineCap = "round"; g.strokeStyle = col; g.lineWidth = bLw(g, k); g.stroke(); }
// заливка многоугольника + зерно + неровный контур
function bShape(g, pts, fill, seed, k = 1, grain = 0.4){ bPoly(g, pts); g.fillStyle = fill; g.fill(); if(grain) bGrain(g, grain); bRough(g, pts, true, seed); bInk(g, k); }
const bRect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
const bCirc = (x, y, r, n = 20) => Array.from({length: n}, (_, i) => { const a = i / n * 6.283; return [x + Math.cos(a) * r, y + Math.sin(a) * r]; });
// повернуть точки вокруг (cx, cy)
const bRot = (pts, cx, cy, a) => { const c = Math.cos(a), s = Math.sin(a); return pts.map(([x, y]) => [cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c]); };

// зерно бумаги поверх текущего пути — по пикселю экрана, как у образца
let B_GRAIN = null;
function bGrain(g, alpha = 0.35){
  if(!B_GRAIN){
    const c = document.createElement("canvas"); c.width = c.height = 96; const q = c.getContext("2d");
    for(let i = 0; i < 1500; i++){ const x = hash(i, 301) * 96, y = hash(i, 302) * 96, s = hash(i, 303);
      q.fillStyle = s < 0.6 ? `rgba(40,30,16,${(0.1 + 0.22 * hash(i, 304)).toFixed(2)})` : `rgba(255,250,236,${(0.1 + 0.22 * hash(i, 305)).toFixed(2)})`; q.fillRect(x, y, s < 0.08 ? 2 : 1, 1); }
    B_GRAIN = c;
  }
  const p = g.createPattern(B_GRAIN, "repeat"); p.setTransform(new DOMMatrix().scale(1 / fScale(g)));
  g.save(); g.globalAlpha *= alpha; g.fillStyle = p; g.fill(); g.restore();
}
// мягкая тень влево вниз от предмета высотой h м: силуэт уносится далеко за край, на место приходит только его тень
function bShadow(g, path, h, alpha = 0.3){
  const S = fScale(g), d = bDpr(), M = g.getTransform();
  g.save(); g.setTransform(new DOMMatrix().translate(6000, 0).multiply(M));   // уносим в пикселях экрана — поворот предмета не мешает
  g.shadowColor = `rgba(28,22,12,${alpha})`; g.shadowBlur = Math.max(2, 0.45 * h * S); g.shadowOffsetX = -0.3 * h * S - 6000; g.shadowOffsetY = 0.24 * h * S;
  g.fillStyle = "#000"; path(); g.fill(); g.restore();
}
// грань светлее, если смотрит к солнцу (вправо вверх)
const bFacet = (base, nx, ny) => { const k = (nx * 0.7 - ny * 0.7) * 0.22; return k >= 0 ? mix(base, "#ffffff", k) : mix(base, "#000000", -k); };
// тонкие штрихи внутри текущего пути (обрезка), набор отрезков
function bLines(g, segs, col, k = 0.4){ g.beginPath(); for(const [x0, y0, x1, y1] of segs){ g.moveTo(x0, y0); g.lineTo(x1, y1); } g.strokeStyle = col; g.lineWidth = bLw(g, k); g.stroke(); }

// ═══ Полоса вдоль ломаной (стена, ров, частокол): левый и правый края со скосом в изломах ═══
function bBand(pts, w){
  const L = [], R = [], n = pts.length, closed = n > 2 && Math.hypot(pts[0][0] - pts[n - 1][0], pts[0][1] - pts[n - 1][1]) < 1e-6;   // замкнутая: углы и в начале
  for(let i = 0; i < n; i++){
    const p = pts[i], a = closed && i === 0 ? pts[n - 2] : pts[Math.max(0, i - 1)], b = closed && i === n - 1 ? pts[1] : pts[Math.min(n - 1, i + 1)];
    let tx = b[0] - a[0], ty = b[1] - a[1]; const tl = Math.hypot(tx, ty) || 1; tx /= tl; ty /= tl;
    let k = 1;
    if(closed || i > 0 && i < n - 1){ const ux = p[0] - a[0], uy = p[1] - a[1], ul = Math.hypot(ux, uy) || 1; k = 1 / Math.max(0.5, (ux / ul) * tx + (uy / ul) * ty); }
    L.push([p[0] - ty * w / 2 * k, p[1] + tx * w / 2 * k]); R.push([p[0] + ty * w / 2 * k, p[1] - tx * w / 2 * k]);
  }
  return {L, R, poly: L.concat(R.slice().reverse())};
}
// пройти по ломаной с шагом: cb(x, y, tx, ty, s)
function bWalk(pts, step, start, cb){
  let s0 = 0, next = start;
  for(let i = 0; i + 1 < pts.length; i++){
    const [x0, y0] = pts[i], [x1, y1] = pts[i + 1], L = Math.hypot(x1 - x0, y1 - y0); if(L < 1e-6) continue;
    const tx = (x1 - x0) / L, ty = (y1 - y0) / L;
    while(next <= s0 + L){ const u = next - s0; cb(x0 + tx * u, y0 + ty * u, tx, ty, next); next += step; }
    s0 += L;
  }
}

// ═══════════ КРЕПОСТЬ ═══════════
// Каменная стена: лента светлого камня, снаружи зубцы-мерлоны с тёмными бойницами между ними, вдоль — боевой ход
// двумя линиями, поперёк — швы кладки; outer = +1 — зубцы с левой стороны по ходу ломаной, −1 — с правой
function bWall(g, pts, o = {}){
  const w = o.w ?? 2.6, seed = o.seed ?? 1, side = o.outer ?? 1, band = bBand(pts, w);
  bShadow(g, () => bPoly(g, band.poly), 6);
  bPoly(g, band.poly); g.fillStyle = B_STONE; g.fill(); bGrain(g, 0.45);
  // швы кладки и боевой ход — внутри ленты
  g.save(); bPoly(g, band.poly); g.clip();
  const segs = [];
  bWalk(pts, 0.85, 0.4, (x, y, tx, ty, s) => { const nx = -ty * side, ny = tx * side, row = Math.round(s / 0.85) % 2;
    const a = row ? 0.05 : -0.2, b = row ? -0.25 : -0.5; segs.push([x + nx * w * a, y + ny * w * a, x + nx * w * b, y + ny * w * b]); });
  bLines(g, segs, "rgba(96,86,70,.35)", 0.35);
  for(const off of [-0.12, -0.36]){ const bb = bBand(pts, Math.abs(off) * 2 * w), line = side > 0 ? bb.R : bb.L; bRough(g, line, false, seed + 9); g.strokeStyle = "rgba(60,52,40,.55)"; g.lineWidth = bLw(g, 0.45); g.stroke(); }
  g.restore();
  bRough(g, band.poly, true, seed); bInk(g, 1.15);
  // зубцы снаружи: мерлон выступает за край, между мерлонами — тёмная бойница
  const edge = side > 0 ? band.L : band.R, M = [], C = [];
  bWalk(edge, 1.35, 0.7, (x, y, tx, ty) => {
    const nx = -ty * side, ny = tx * side, q = (u, v) => [x + tx * u + nx * v, y + ty * u + ny * v];
    M.push([q(-0.42, 0.3), q(0.42, 0.3), q(0.42, -0.42), q(-0.42, -0.42)]);
    C.push([q(0.5, 0.02), q(0.85, 0.02), q(0.85, -0.38), q(0.5, -0.38)]);
  });
  for(const c of C){ bPoly(g, c); g.fillStyle = "rgba(34,24,15,.82)"; g.fill(); }
  M.forEach((m, i) => bShape(g, m, B_STONE_L, seed + i, 0.8, 0.3));
}
// круглая башня: зубцы по кругу; сверху — шатёр из восьми граней с навершием или открытая площадка с настилом
function bTowerRound(g, x, y, r, o = {}){
  const seed = o.seed ?? 3, base = o.roofCol ?? B_ROOF.slate;
  bShadow(g, () => { g.beginPath(); g.arc(x, y, r, 0, 6.283); }, o.roof === "open" ? 8 : 11);
  bShape(g, bCirc(x, y, r, 26), B_STONE, seed, 1.15, 0.45);
  const N = Math.max(8, Math.round(6.283 * r / 1.35));
  for(let i = 0; i < N; i++){   // бойницы, потом мерлоны
    const a = (i + 0.5) / N * 6.283, d = 0.42 / r, p = (rr, aa) => [x + Math.cos(aa) * rr, y + Math.sin(aa) * rr];
    bPoly(g, [p(r - 0.38, a - d * 0.5), p(r + 0.02, a - d * 0.5), p(r + 0.02, a + d * 0.5), p(r - 0.38, a + d * 0.5)]); g.fillStyle = "rgba(34,24,15,.82)"; g.fill();
  }
  for(let i = 0; i < N; i++){
    const a = i / N * 6.283, d = 0.42 / r, p = (rr, aa) => [x + Math.cos(aa) * rr, y + Math.sin(aa) * rr];
    bShape(g, [p(r - 0.42, a - d), p(r + 0.3, a - d), p(r + 0.3, a + d), p(r - 0.42, a + d)], B_STONE_L, seed + i, 0.8, 0.3);
  }
  const rr = r - 0.75;
  if(o.roof === "open"){   // площадка: дощатый настил, люк
    bShape(g, bCirc(x, y, rr, 22), B_WOOD_L, seed + 5, 0.8, 0.35);
    g.save(); bPoly(g, bCirc(x, y, rr, 22)); g.clip(); const S = []; for(let u = -rr; u < rr; u += 0.42) S.push([x + u, y - rr, x + u, y + rr]); bLines(g, S, "rgba(70,48,26,.45)", 0.4); g.restore();
    bShape(g, bRect(x - 0.5, y - 0.5, x + 0.5, y + 0.5), B_WOOD_D, seed + 6, 0.7, 0);
    return;
  }
  for(let k = 0; k < 8; k++){
    const a0 = k * Math.PI / 4, a1 = a0 + Math.PI / 4, am = (a0 + a1) / 2;
    g.beginPath(); g.moveTo(x, y); g.arc(x, y, rr, a0, a1); g.closePath(); g.fillStyle = bFacet(base, Math.cos(am), Math.sin(am)); g.fill(); bGrain(g, 0.3);
  }
  const S = []; for(let k = 0; k < 8; k++){ const a = k * Math.PI / 4; S.push([x, y, x + Math.cos(a) * rr, y + Math.sin(a) * rr]); } bLines(g, S, "rgba(30,20,12,.45)", 0.6);
  bRough(g, bCirc(x, y, rr, 22), true, seed + 7); bInk(g, 0.9);
  bShape(g, bCirc(x, y, 0.35, 10), "#d6b75a", seed + 8, 0.7, 0);
  if(o.flag) bFlag(g, x, y, o.flag, o.c2, o.t || 0, 2.4);
}
// квадратная башня: зубцы по периметру, внутри — вальмовая крыша
function bTowerSquare(g, x, y, a, o = {}){
  const h = a / 2, seed = o.seed ?? 4;
  bShadow(g, () => bPoly(g, bRect(x - h, y - h, x + h, y + h)), 10);
  const ring = [[x - h, y - h], [x + h, y - h], [x + h, y + h], [x - h, y + h], [x - h, y - h]];
  bShape(g, bRect(x - h, y - h, x + h, y + h), B_STONE, seed, 1.15, 0.45);
  const C = [], M = [];
  bWalk(ring, 1.35, 0.65, (px, py, tx, ty) => { const nx = ty, ny = -tx, q = (u, v) => [px + tx * u + nx * v, py + ty * u + ny * v];
    M.push([q(-0.42, 0.3), q(0.42, 0.3), q(0.42, -0.42), q(-0.42, -0.42)]); C.push([q(0.5, 0.02), q(0.85, 0.02), q(0.85, -0.38), q(0.5, -0.38)]); });
  for(const c of C){ bPoly(g, c); g.fillStyle = "rgba(34,24,15,.82)"; g.fill(); }
  M.forEach((m, i) => bShape(g, m, B_STONE_L, seed + i, 0.8, 0.3));
  bRoofHip(g, x - h + 0.8, y - h + 0.8, x + h - 0.8, y + h - 0.8, o.roofCol ?? B_ROOF.slate, "slate", seed + 3);
  if(o.flag) bFlag(g, x, y, o.flag, o.c2, o.t || 0, 2.4);
}
// ворота в проёме: тёмный проход, две створки из досок с железными полосами; state — closed, open, broken
function bGate(g, x, y, ang, w = 4.2, d = 2.8, state = "closed", seed = 11){
  g.save(); g.translate(x, y); g.rotate(ang);
  bShape(g, bRect(-w / 2, -d / 2, w / 2, d / 2), "#2c241c", seed, 1.0, 0);
  const leaf = (sx, rot, brk) => {
    g.save(); g.translate(sx * w / 2, 0); g.rotate(sx * rot);
    const L = w / 2 - 0.1, pts = bRect(sx < 0 ? 0 : -L, -0.2, sx < 0 ? L : 0, 0.2);
    bShape(g, brk ? bRot(pts, 0, 0, sx * 0.25) : pts, B_WOOD, seed + (sx > 0 ? 3 : 1), 0.8, 0.3);
    const S = []; for(let u = 0.35; u < L; u += 0.35) S.push([sx < 0 ? u : -u, -0.2, sx < 0 ? u : -u, 0.2]); bLines(g, S, "rgba(60,40,20,.5)", 0.35);
    g.fillStyle = B_IRON; for(const u of [0.25, 0.75]) g.fillRect(sx < 0 ? L * u - 0.07 : -L * u - 0.07, -0.2, 0.14, 0.4);
    g.restore();
  };
  if(state === "open"){ leaf(-1, -1.35); leaf(1, -1.35); }
  else if(state === "broken"){ leaf(-1, -0.5, true); bRubble(g, 0.9, 0.4, 1.0, seed + 9, B_WOOD_L); }
  else { leaf(-1, 0); leaf(1, 0); }
  g.restore();
}
// надвратная башня: две квадратные башни по бокам прохода и крытый переход над воротами
function bGatehouse(g, x, y, ang, o = {}){
  const seed = o.seed ?? 21, gw = o.gw ?? 4.2;
  g.save(); g.translate(x, y); g.rotate(ang);
  bGate(g, 0, 0, 0, gw, 3.2, o.state ?? "closed", seed);
  bShadow(g, () => bPoly(g, bRect(-gw / 2, -2.2, gw / 2, -0.9)), 8);
  bShape(g, bRect(-gw / 2 - 0.2, -2.3, gw / 2 + 0.2, -0.9), B_STONE, seed + 1, 1.0, 0.4);
  bRoofGable(g, -gw / 2, -2.2, gw / 2, -1.0, B_ROOF.slate, "slate", true, seed + 2);
  for(const s of [-1, 1]) bTowerSquare(g, s * (gw / 2 + 2.6), -0.4, 5.2, {seed: seed + 5 + s, flag: s > 0 ? o.flag : null, c2: o.c2, t: o.t});
  g.restore();
}
// донжон: каменный квадрат с зубцами, внутри — вальмовая крыша, по углам — башенки с шатрами
function bKeep(g, x, y, a, o = {}){
  bTowerSquare(g, x, y, a, {seed: o.seed ?? 31, roofCol: o.roofCol ?? B_ROOF.slate});
  for(const [sx, sy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) bTowerRound(g, x + sx * a / 2, y + sy * a / 2, 2.0, {seed: (o.seed ?? 31) + sx * 3 + sy, roofCol: o.roofCol ?? B_ROOF.slate});
  if(o.flag) bFlag(g, x, y, o.flag, o.c2, o.t || 0, 3.2);
}
// пролом и обломки: груда камней разного тона
function bRubble(g, x, y, r, seed = 41, col = null){
  for(let q = 0; q < Math.round(10 + 8 * r); q++){
    const s = seed * 31 + q, a = hash(s, 1) * 6.283, d = r * (hash(s, 2) + hash(s, 3)) / 2, rr = 0.18 + 0.4 * hash(s, 4);
    const pts = Array.from({length: 5}, (_, v) => { const b = v * 1.2566 + hash(s, 5 + v) * 0.6, e = rr * (0.7 + 0.4 * hash(s, 10 + v)); return [x + Math.cos(a) * d + Math.cos(b) * e, y + Math.sin(a) * d + Math.sin(b) * e]; });
    bShape(g, pts, col ?? (hash(s, 15) < 0.5 ? B_STONE : B_STONE_D), s, 0.7, 0.3);
  }
}
// ров: вода вдоль ломаной, тёмная кромка, светлая отмель, рябь
function bMoat(g, pts, w = 6, seed = 51){
  const band = bBand(pts, w), inner = bBand(pts, w - 1.2);
  bPoly(g, band.poly); g.fillStyle = "#56705a"; g.fill();
  bPoly(g, inner.poly); g.fillStyle = B_WATER; g.fill(); bGrain(g, 0.25);
  g.save(); bPoly(g, inner.poly); g.clip(); const S = [];
  bWalk(pts, 2.2, 1, (x, y, tx, ty, s) => { const o = (hash(seed, Math.round(s)) - 0.5) * (w - 2), cx = x - ty * o, cy = y + tx * o; S.push([cx - tx * 0.7, cy - ty * 0.7, cx + tx * 0.7, cy + ty * 0.7]); });
  bLines(g, S, "rgba(226,238,242,.6)", 0.5); g.restore();
  bRough(g, band.poly, true, seed); bInk(g, 0.8, "rgba(34,24,15,.7)");
}
// вал с окопом: земляная насыпь штрихами поперёк, канава тёмная
function bRampart(g, pts, w = 4, seed = 61){
  const band = bBand(pts, w);
  bShadow(g, () => bPoly(g, band.poly), 1.5);
  bShape(g, band.poly, "#9a8156", seed, 0.9, 0.5);
  g.save(); bPoly(g, band.poly); g.clip(); const S = [];
  bWalk(pts, 0.5, 0.2, (x, y, tx, ty) => S.push([x - ty * w * 0.45, y + tx * w * 0.45, x + ty * w * 0.1, y - tx * w * 0.1])); bLines(g, S, "rgba(70,52,30,.4)", 0.4); g.restore();
  const ditch = bBand(pts.map(([x, y]) => [x, y]), w * 0.25); bPoly(g, ditch.poly); g.fillStyle = "rgba(60,46,28,.55)"; g.fill();
}
// частокол: брёвна торцами (сверху — кружки с заострённой серединой), за ними — помост
function bPalisade(g, pts, o = {}){
  const seed = o.seed ?? 71, walk = bBand(pts, 1.6);
  bShadow(g, () => { g.beginPath(); bWalk(pts, 0.4, 0, (x, y) => { g.moveTo(x + 0.24, y); g.arc(x, y, 0.24, 0, 6.283); }); }, 3.5);
  if(o.walk !== false){ const side = o.inner ?? 1, wb = side > 0 ? bBand(pts.map(p => p), 1.4) : walk; bShape(g, bBand(pts, 1.3).poly.map(([x, y], i, a) => [x, y]), B_WOOD_L, seed, 0.6, 0.3); }
  bWalk(pts, 0.48, 0, (x, y, tx, ty, s) => {
    const r = 0.25 + 0.03 * hash(seed, Math.round(s * 10));
    g.beginPath(); g.arc(x, y, r, 0, 6.283); g.fillStyle = B_WOOD; g.fill(); bInk(g, 0.7);
    g.beginPath(); g.arc(x + 0.03, y - 0.03, r * 0.45, 0, 6.283); g.fillStyle = B_WOOD_L; g.fill();
  });
}
// вышка острога: квадрат из брёвен, по углам торцы, крыша из тёса
function bWoodTower(g, x, y, a = 4.4, o = {}){
  const h = a / 2, seed = o.seed ?? 81;
  bShadow(g, () => bPoly(g, bRect(x - h, y - h, x + h, y + h)), 8);
  bShape(g, bRect(x - h, y - h, x + h, y + h), B_WOOD, seed, 1.1, 0.4);
  for(const [sx, sy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]){ g.beginPath(); g.arc(x + sx * (h - 0.2), y + sy * (h - 0.2), 0.32, 0, 6.283); g.fillStyle = B_WOOD_L; g.fill(); bInk(g, 0.6); }
  bRoofHip(g, x - h + 0.5, y - h + 0.5, x + h - 0.5, y + h - 0.5, B_ROOF.shingle, "shingle", seed + 2);
  if(o.flag) bFlag(g, x, y, o.flag, o.c2, o.t || 0, 2);
}
// знамя на древке: сверху — полотнище, вьётся по ветру (t — время)
function bFlag(g, x, y, col, c2 = "#ece6d6", t = 0, len = 2.2){
  const wv = k => Math.sin(t * 4 + k * 2.2 + x) * 0.18 * k;
  g.beginPath(); g.moveTo(x, y); for(let k = 0; k <= 4; k++) g.lineTo(x + len * k / 4, y - 0.05 + wv(k / 4 * 1.0));
  for(let k = 4; k >= 0; k--) g.lineTo(x + len * k / 4, y + 0.55 + wv(k / 4 * 1.0));
  g.closePath(); g.fillStyle = col; g.fill();
  g.save(); g.clip(); g.fillStyle = c2; g.fillRect(x + len * 0.45, y - 1, len * 0.18, 3); g.restore();
  bInk(g, 0.7);
  g.beginPath(); g.arc(x, y + 0.25, 0.16, 0, 6.283); g.fillStyle = "#d6b75a"; g.fill(); bInk(g, 0.6);
}

// ═══════════ КРЫШИ И ДОМА ═══════════
// фактура ската: черепица — ряды и полукружия, сланец — ряды прямых плит, солома — частые штрихи, тёс — ряды досок
function bRoofTex(g, kind, x0, y0, x1, y1, along){
  const S = [];
  if(kind === "thatch"){ for(let i = 0; i < (x1 - x0) * (y1 - y0) * 3; i++){ const x = x0 + hash(i, 501) * (x1 - x0), y = y0 + hash(i, 502) * (y1 - y0), l = 0.35; S.push(along ? [x, y, x + 0.06, y + l] : [x, y, x + l, y + 0.06]); } bLines(g, S, "rgba(110,84,32,.4)", 0.35); return; }
  const step = kind === "tile" ? 0.45 : kind === "slate" ? 0.5 : 0.55;
  if(along){ for(let x = x0 + step; x < x1; x += step) S.push([x, y0, x, y1]); } else for(let y = y0 + step; y < y1; y += step) S.push([x0, y, x1, y]);
  bLines(g, S, kind === "slate" ? "rgba(30,36,44,.35)" : "rgba(60,30,16,.33)", 0.38);
  if(kind === "tile" || kind === "shingle"){   // поперечные стыки вразбежку
    const T = [], t2 = kind === "tile" ? 0.32 : 0.7;
    if(along){ let r = 0; for(let x = x0; x < x1; x += step, r++) for(let y = y0 + (r % 2) * t2 / 2; y < y1; y += t2) T.push([x, y, x + step, y]); }
    else { let r = 0; for(let y = y0; y < y1; y += step, r++) for(let x = x0 + (r % 2) * t2 / 2; x < x1; x += t2) T.push([x, y, x, y + step]); }
    bLines(g, T, "rgba(60,30,16,.25)", 0.32);
  }
}
// вальмовая крыша: конёк вдоль длинной стороны, четыре ската в два тона, фактура по материалу
function bRoofHip(g, x0, y0, x1, y1, base, kind, seed = 91){
  const w = x1 - x0, h = y1 - y0, wide = w >= h, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hl = Math.abs(w - h) / 2;
  const ra = wide ? [cx - hl, cy] : [cx, cy - hl], rb = wide ? [cx + hl, cy] : [cx, cy + hl];
  const F = wide ? [[[[x0, y0], [x1, y0], rb, ra], 0, -1, false], [[[x1, y0], [x1, y1], rb], 1, 0, true], [[[x1, y1], [x0, y1], ra, rb], 0, 1, false], [[[x0, y1], [x0, y0], ra], -1, 0, true]]
                 : [[[[x0, y0], [x1, y0], ra], 0, -1, false], [[[x1, y0], [x1, y1], rb, ra], 1, 0, true], [[[x1, y1], [x0, y1], rb], 0, 1, false], [[[x0, y1], [x0, y0], ra, rb], -1, 0, true]];
  for(const [pts, nx, ny, along] of F){
    bPoly(g, pts); g.fillStyle = bFacet(base, nx, ny); g.fill(); bGrain(g, 0.35);
    g.save(); bPoly(g, pts); g.clip(); bRoofTex(g, kind, x0, y0, x1, y1, along); g.restore();
  }
  g.beginPath(); g.moveTo(x0, y0); g.lineTo(ra[0], ra[1]); g.lineTo(rb[0], rb[1]); g.lineTo(x1, y1);
  g.moveTo(x1, y0); g.lineTo(wide ? rb[0] : ra[0], wide ? rb[1] : ra[1]); g.moveTo(x0, y1); g.lineTo(wide ? ra[0] : rb[0], wide ? ra[1] : rb[1]);
  g.strokeStyle = "rgba(30,20,12,.55)"; g.lineWidth = bLw(g, 0.6); g.stroke();
  bRough(g, bRect(x0, y0, x1, y1), true, seed, kind === "thatch" ? bLw(g, 2.2) : null); bInk(g, 1.1);
}
// двускатная крыша: конёк вдоль оси (alongX — вдоль x), скаты в два тона
function bRoofGable(g, x0, y0, x1, y1, base, kind, alongX, seed = 95){
  const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
  const F = alongX ? [[bRect(x0, y0, x1, cy), 0, -1], [bRect(x0, cy, x1, y1), 0, 1]] : [[bRect(x0, y0, cx, y1), -1, 0], [bRect(cx, y0, x1, y1), 1, 0]];
  for(const [pts, nx, ny] of F){ bPoly(g, pts); g.fillStyle = bFacet(base, nx, ny); g.fill(); bGrain(g, 0.35); g.save(); bPoly(g, pts); g.clip(); bRoofTex(g, kind, x0, y0, x1, y1, !alongX); g.restore(); }
  g.beginPath(); alongX ? (g.moveTo(x0, cy), g.lineTo(x1, cy)) : (g.moveTo(cx, y0), g.lineTo(cx, y1)); g.strokeStyle = "rgba(30,20,12,.6)"; g.lineWidth = bLw(g, 0.7); g.stroke();
  bRough(g, bRect(x0, y0, x1, y1), true, seed, kind === "thatch" ? bLw(g, 2.2) : null); bInk(g, 1.1);
}
// дом: тень по высоте, крыша, у некоторых — труба
function bHouse(g, x0, y0, x1, y1, o = {}){
  const kind = o.roof ?? "tile", seed = o.seed ?? 101;
  bShadow(g, () => bPoly(g, bRect(x0, y0, x1, y1)), o.h ?? 5);
  bRoofHip(g, x0, y0, x1, y1, o.col ?? B_ROOF[kind], kind, seed);
  if(o.chimney ?? hash(seed, 7) < 0.6){
    const u = hash(seed, 8), cx = x0 + (x1 - x0) * (0.25 + 0.5 * u), cy = y0 + (y1 - y0) * (hash(seed, 9) < 0.5 ? 0.3 : 0.7);
    bShape(g, bRect(cx - 0.4, cy - 0.4, cx + 0.4, cy + 0.4), "#9a8a78", seed + 3, 0.8, 0.3);
    g.fillStyle = "#2b231c"; g.fillRect(cx - 0.22, cy - 0.22, 0.44, 0.44);
  }
}
// дом буквой Г: два крыла
function bHouseL(g, x, y, o = {}){
  bHouse(g, x, y, x + 10, y + 5, o);
  bHouse(g, x, y + 5, x + 5, y + 11, Object.assign({}, o, {seed: (o.seed ?? 101) + 5, chimney: false}));
}
// церковь: неф с двускатной крышей, трансепт поперёк, апсида с веером скатов, колокольня со шпилем и крестом
function bChurch(g, x, y, o = {}){
  const seed = o.seed ?? 111, rc = o.roofCol ?? B_ROOF.slate;
  g.save(); g.translate(x, y); g.rotate(o.ang ?? 0);
  bShadow(g, () => { bPoly(g, bRect(-4, -9, 4, 12)); bPoly(g, bRect(-9, -4, 9, 2)); g.moveTo(4, -9); g.arc(0, -9, 4, 0, -Math.PI, true); bPoly(g, bRect(-3.2, 12, 3.2, 18.4)); }, 9);
  // апсида: полукруг со скатами веером
  for(let k = 0; k < 5; k++){ const a0 = Math.PI + k * Math.PI / 5, a1 = a0 + Math.PI / 5, am = (a0 + a1) / 2;
    g.beginPath(); g.moveTo(0, -9); g.arc(0, -9, 4, a0, a1); g.closePath(); g.fillStyle = bFacet(rc, Math.cos(am), Math.sin(am)); g.fill(); bGrain(g, 0.3); }
  g.beginPath(); g.moveTo(4, -9); g.arc(0, -9, 4, 0, -Math.PI, true); bInk(g, 1.0);
  bRoofGable(g, -4, -9, 4, 12, rc, "slate", false, seed);
  bRoofGable(g, -9, -4, 9, 2, rc, "slate", true, seed + 2);
  bShape(g, bRect(-1, -2, 1, 0), "#d6b75a", seed + 3, 0.6, 0);   // фонарик над средокрестием
  // колокольня: квадрат, шпиль из четырёх граней, крест
  const t0 = 12, a = 6.4;
  bShape(g, bRect(-a / 2, t0, a / 2, t0 + a), B_STONE, seed + 4, 1.1, 0.4);
  const cx = 0, cy = t0 + a / 2, h = a / 2 - 0.6;
  for(const [pts, nx, ny] of [[[[cx - h, cy - h], [cx + h, cy - h], [cx, cy]], 0, -1], [[[cx + h, cy - h], [cx + h, cy + h], [cx, cy]], 1, 0], [[[cx + h, cy + h], [cx - h, cy + h], [cx, cy]], 0, 1], [[[cx - h, cy + h], [cx - h, cy - h], [cx, cy]], -1, 0]]){
    bPoly(g, pts); g.fillStyle = bFacet(B_ROOF.lead, nx, ny); g.fill(); bGrain(g, 0.3); }
  bRough(g, bRect(cx - h, cy - h, cx + h, cy + h), true, seed + 5); bInk(g, 0.9);
  g.beginPath(); g.moveTo(cx - h, cy - h); g.lineTo(cx + h, cy + h); g.moveTo(cx + h, cy - h); g.lineTo(cx - h, cy + h); g.strokeStyle = "rgba(30,20,12,.5)"; g.lineWidth = bLw(g, 0.6); g.stroke();
  g.fillStyle = "#d6b75a"; g.fillRect(cx - 0.1, cy - 0.6, 0.2, 1.2); g.fillRect(cx - 0.4, cy - 0.3, 0.8, 0.2);
  g.restore();
}
// лавка на рынке: полосатый навес, спереди — корзины с товаром
function bStall(g, x, y, ang, col, seed = 121){
  g.save(); g.translate(x, y); g.rotate(ang);
  bShadow(g, () => bPoly(g, bRect(-1.6, -1.1, 1.6, 1.1)), 2.2);
  bPoly(g, bRect(-1.6, -1.1, 1.6, 1.1)); g.fillStyle = "#efe6d2"; g.fill();
  g.save(); bPoly(g, bRect(-1.6, -1.1, 1.6, 1.1)); g.clip(); g.fillStyle = col; for(let u = -1.6; u < 1.6; u += 0.8) g.fillRect(u, -1.1, 0.4, 2.2); g.restore();
  g.beginPath(); g.moveTo(-1.6, 0); g.lineTo(1.6, 0); g.strokeStyle = "rgba(30,20,12,.4)"; g.lineWidth = bLw(g, 0.5); g.stroke();
  bRough(g, bRect(-1.6, -1.1, 1.6, 1.1), true, seed); bInk(g, 0.9);
  const fruit = ["#c8452e", "#e0a63a", "#7a9a3a", "#8a4a6a"];
  for(let i = 0; i < 3; i++){ const cx = -1 + i, cy = 1.55; g.beginPath(); g.arc(cx, cy, 0.36, 0, 6.283); g.fillStyle = B_WOOD_L; g.fill(); bInk(g, 0.6);
    for(let k = 0; k < 4; k++){ g.beginPath(); g.arc(cx + (hash(seed + i, k) - 0.5) * 0.36, cy + (hash(seed + i, k + 9) - 0.5) * 0.36, 0.1, 0, 6.283); g.fillStyle = fruit[(i + k) % 4]; g.fill(); } }
  g.restore();
}
// колодец: каменное кольцо, тёмная вода, ворот на двух стойках
function bWell(g, x, y, seed = 131){
  bShadow(g, () => { g.beginPath(); g.arc(x, y, 1.1, 0, 6.283); }, 1.6);
  bShape(g, bCirc(x, y, 1.1, 16), B_STONE, seed, 0.9, 0.4);
  g.beginPath(); g.arc(x, y, 0.65, 0, 6.283); g.fillStyle = "#2f4a56"; g.fill(); bInk(g, 0.6);
  for(const s of [-1, 1]) bShape(g, bRect(x + s * 1.05 - 0.15, y - 0.15, x + s * 1.05 + 0.15, y + 0.15), B_WOOD_D, seed + s, 0.6, 0);
  g.beginPath(); g.moveTo(x - 1.05, y); g.lineTo(x + 1.05, y); g.strokeStyle = B_WOOD; g.lineWidth = 0.16; g.stroke(); bInk(g, 0.5);
}
// мельница: каменная башенка с шатром, четыре крыла-решётки крутятся
function bWindmill(g, x, y, t = 0, seed = 141){
  bShadow(g, () => { g.beginPath(); g.arc(x, y, 2.4, 0, 6.283); }, 9);
  bShape(g, bCirc(x, y, 2.4, 20), B_STONE, seed, 1.1, 0.4);
  for(let k = 0; k < 8; k++){ const a0 = k * Math.PI / 4, a1 = a0 + Math.PI / 4, am = (a0 + a1) / 2;
    g.beginPath(); g.moveTo(x, y); g.arc(x, y, 1.8, a0, a1); g.closePath(); g.fillStyle = bFacet(B_ROOF.thatch, Math.cos(am), Math.sin(am)); g.fill(); }
  bRough(g, bCirc(x, y, 1.8, 16), true, seed + 1); bInk(g, 0.8);
  const a = t * 0.8;
  for(let k = 0; k < 4; k++){
    const b = a + k * Math.PI / 2, pts = bRot(bRect(x - 0.45, y - 6.5, x + 0.45, y - 0.6), x, y, b);
    bPoly(g, pts); g.fillStyle = "rgba(236,226,206,.9)"; g.fill();
    g.save(); bPoly(g, pts); g.clip(); const S = []; for(let u = 0.9; u < 6.5; u += 0.6){ const p = bRot([[x - 0.45, y - u], [x + 0.45, y - u]], x, y, b); S.push([p[0][0], p[0][1], p[1][0], p[1][1]]); } bLines(g, S, "rgba(90,60,30,.6)", 0.4); g.restore();
    bRough(g, pts, true, seed + k); bInk(g, 0.8);
  }
  bShape(g, bCirc(x, y, 0.45, 10), B_WOOD_D, seed + 9, 0.7, 0);
}
// огород за плетнём: грядки рядами
function bGarden(g, x0, y0, x1, y1, seed = 151){
  bPoly(g, bRect(x0, y0, x1, y1)); g.fillStyle = "#7d6a42"; g.fill(); bGrain(g, 0.5);
  g.save(); bPoly(g, bRect(x0, y0, x1, y1)); g.clip();
  for(let y = y0 + 0.7; y < y1; y += 1.1){ const S = []; for(let x = x0 + 0.4; x < x1; x += 0.6) S.push([x, y - 0.2, x + 0.1, y + 0.2]); bLines(g, S, hash(seed, Math.round(y)) < 0.5 ? "#6f9a3c" : "#8fae4a", 1.6); }
  g.restore();
  const P = []; bWalk([[x0, y0], [x1, y0], [x1, y1], [x0, y1], [x0, y0]], 1.0, 0, (x, y) => P.push([x, y]));
  g.beginPath(); bRough(g, bRect(x0, y0, x1, y1), true, seed); g.strokeStyle = B_WOOD_D; g.lineWidth = bLw(g, 0.9); g.stroke();
  for(const [x, y] of P){ g.beginPath(); g.arc(x, y, 0.1, 0, 6.283); g.fillStyle = B_WOOD_D; g.fill(); }
}
// дерево сверху: облачко крон с тёмным контуром, светлее к солнцу
function bTree(g, x, y, r, seed = 161, pal = ["#2e4a22", "#4f7a34", "#6e9a44", "#9cc060"]){
  bShadow(g, () => { g.beginPath(); g.arc(x, y, r * 0.9, 0, 6.283); }, 7, 0.28);
  const blob = (cx, cy, rr, n, grow) => { g.beginPath(); g.moveTo(cx + rr * 0.7 + grow, cy); g.arc(cx, cy, rr * 0.7 + grow, 0, 6.283);
    for(let k = 0; k < n; k++){ const a = k * 6.283 / n + hash(seed, k) * 0.5, br = rr * (0.36 + 0.1 * hash(seed, 20 + k)) + grow, bx = cx + Math.cos(a) * rr * 0.6, by = cy + Math.sin(a) * rr * 0.6; g.moveTo(bx + br, by); g.arc(bx, by, br, 0, 6.283); } };
  blob(x, y, r, 7, bLw(g, 1.1)); g.fillStyle = B_INK; g.fill();
  blob(x, y, r, 7, 0); g.fillStyle = pal[1]; g.fill();
  blob(x + r * 0.15, y - r * 0.15, r * 0.7, 6, 0); g.fillStyle = pal[2]; g.fill();
  g.beginPath(); g.arc(x + r * 0.3, y - r * 0.3, r * 0.28, 0, 6.283); g.fillStyle = pal[3]; g.fill();
}

// ═══════════ ОСАДНЫЕ МАШИНЫ (оси машины: вперёд — −y, как у бойцов) ═══════════
// брус: полоса с контуром «от руки»
function bBeam(g, x0, y0, x1, y1, w, col = B_WOOD, seed = 171){
  const L = Math.hypot(x1 - x0, y1 - y0) || 1, nx = -(y1 - y0) / L * w / 2, ny = (x1 - x0) / L * w / 2;
  bShape(g, [[x0 + nx, y0 + ny], [x1 + nx, y1 + ny], [x1 - nx, y1 - ny], [x0 - nx, y0 - ny]], col, seed, 0.85, 0.3);
}
function bWheel(g, x, y, ang, len = 1.2, seed = 175){ g.save(); g.translate(x, y); g.rotate(ang); bShape(g, bRect(-0.11, -len / 2, 0.11, len / 2), B_WOOD_D, seed, 0.8, 0); g.fillStyle = B_IRON; g.fillRect(-0.11, -len / 2, 0.22, 0.12); g.fillRect(-0.11, len / 2 - 0.12, 0.22, 0.12); g.restore(); }
const bAt = (g, x, y, face, fn) => { g.save(); g.translate(x, y); g.rotate(face); fn(); g.restore(); };
// ядра, камни и бочки рядом с машиной
function bAmmo(g, x, y, kind, seed = 181){
  if(kind === "stones") for(let i = 0; i < 7; i++){ const a = i * 2.4, d = i ? 0.45 + 0.1 * (i % 2) : 0; bShape(g, bCirc(x + Math.cos(a) * d, y + Math.sin(a) * d, 0.28, 9), B_STONE_D, seed + i, 0.6, 0.3); }
  else if(kind === "balls") for(let i = 0; i < 6; i++){ const cx = x + (i % 3) * 0.32 - 0.32, cy = y + Math.floor(i / 3) * 0.3; g.beginPath(); g.arc(cx, cy, 0.15, 0, 6.283); g.fillStyle = "#2f3033"; g.fill(); bInk(g, 0.5); }
  else if(kind === "powder") for(let i = 0; i < 3; i++){ const cx = x + i * 0.75; g.beginPath(); g.arc(cx, y, 0.34, 0, 6.283); g.fillStyle = B_WOOD; g.fill(); bInk(g, 0.7); g.beginPath(); g.arc(cx, y, 0.22, 0, 6.283); g.strokeStyle = B_IRON; g.lineWidth = 0.05; g.stroke(); }
  else if(kind === "bolts"){ for(let i = 0; i < 5; i++) bBeam(g, x - 0.8, y + i * 0.12, x + 0.8, y + i * 0.12, 0.06, B_WOOD_L, seed + i); }
}
// требушет: сани из брусьев, стойки, ось; рычаг с противовесом и пращой. a — угол рычага: 0 — взведён (длинное плечо
// назад, праща на земле), π — выстрел (плечо вперёд); сверху видна проекция плеча
function bTrebuchet(g, x, y, face, a = 0, o = {}){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.8, -3.2, 1.8, 3.2)), 4);
    for(const s of [-1, 1]) bBeam(g, s * 1.4, -3.2, s * 1.4, 3.2, 0.45, B_WOOD, 181 + s);
    for(const y0 of [-2.6, 2.6]) bBeam(g, -1.8, y0, 1.8, y0, 0.4, B_WOOD_D, 185 + y0);
    for(const s of [-1, 1]){ bBeam(g, s * 1.4, -2.2, s * 1.4, 2.2, 0.32, B_WOOD_D, 189 + s); bShape(g, bRect(s * 1.4 - 0.35, -0.35, s * 1.4 + 0.35, 0.35), B_WOOD_L, 191 + s, 0.8, 0.3); }
    bBeam(g, -1.75, 0, 1.75, 0, 0.22, B_IRON, 193);
    const c = Math.cos(a), long = 5.4 * c, short = -1.8 * c;
    // праща: на земле позади, когда взведён; в полёте — у конца плеча
    if(c > 0.6){ g.beginPath(); g.moveTo(0, long); g.lineTo(0.2, long + 1.6); g.strokeStyle = B_ROPE; g.lineWidth = 0.07; g.stroke(); bShape(g, bCirc(0.2, long + 1.8, 0.4, 10), B_HIDE, 195, 0.7, 0); bShape(g, bCirc(0.2, long + 1.8, 0.25, 9), B_STONE_D, 196, 0.5, 0); }
    bBeam(g, 0, short, 0, long, 0.5, B_WOOD_L, 197);
    // противовес: ящик с камнями на коротком плече
    bShape(g, bRect(-1.0, short - 0.8, 1.0, short + 0.8), B_WOOD_D, 199, 1.0, 0.3);
    for(let i = 0; i < 4; i++) bShape(g, bCirc(-0.45 + (i % 2) * 0.9, short - 0.35 + Math.floor(i / 2) * 0.7, 0.3, 8), B_STONE_D, 200 + i, 0.5, 0);
    if(o.flag) bFlag(g, 1.4, -3.2, o.flag, o.c2, o.t || 0, 1.4);
  });
}
// онагр: рама, у переднего края — упор с подушкой, рычаг с чашей на скрученном жгуте; a 0 — взведён, 1 — выстрел
function bOnager(g, x, y, face, a = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.2, -2.4, 1.2, 2.4)), 2.5);
    for(const s of [-1, 1]) bBeam(g, s * 0.95, -2.4, s * 0.95, 2.4, 0.38, B_WOOD, 211 + s);
    for(const y0 of [-2.0, 1.9]) bBeam(g, -1.15, y0, 1.15, y0, 0.32, B_WOOD_D, 213 + y0);
    bBeam(g, -1.2, -1.3, 1.2, -1.3, 0.45, B_WOOD_D, 215); bShape(g, bRect(-0.5, -1.55, 0.5, -1.05), B_HIDE, 216, 0.7, 0.3);   // упор и подушка
    bShape(g, bRect(-0.95, 0.75, 0.95, 1.35), "#4d3a28", 217, 0.8, 0); bLines(g, [[-0.8, 0.85, 0.8, 1.25], [-0.8, 1.05, 0.8, 0.9]], "rgba(200,180,140,.5)", 0.5);   // жгут
    const tip = 1.05 - (a ? 2.2 : -1.4);
    bBeam(g, 0, 1.05, 0, tip, 0.3, B_WOOD_L, 218); bShape(g, bCirc(0, tip, 0.38, 10), B_HIDE, 219, 0.7, 0);
    if(!a) bShape(g, bCirc(0, tip, 0.24, 9), B_STONE_D, 220, 0.5, 0);
  });
}
// баллиста: станина, ложе, плечи лука с тетивой, болт на ложе; a 0 — взведена, 1 — выстрел
function bBallista(g, x, y, face, a = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.6, -2.2, 1.6, 1.4)), 1.8);
    for(const [x0, y0, x1, y1] of [[-0.9, 1.2, 0, 0.2], [0.9, 1.2, 0, 0.2], [0, 1.4, 0, 0.2]]) bBeam(g, x0, y0, x1, y1, 0.22, B_WOOD_D, 231);
    bBeam(g, 0, 1.0, 0, -2.0, 0.34, B_WOOD, 232);
    const ny = a ? -1.35 : 0.6;
    g.beginPath(); g.moveTo(-1.55, -1.15); g.lineTo(0, ny); g.lineTo(1.55, -1.15); g.strokeStyle = "#efe9dc"; g.lineWidth = bLw(g, 0.55); g.stroke();
    g.beginPath(); g.moveTo(-1.6, -1.15); g.quadraticCurveTo(-0.8, -1.6, 0, -1.45); g.quadraticCurveTo(0.8, -1.6, 1.6, -1.15);
    g.strokeStyle = B_INK; g.lineWidth = 0.18 + bLw(g, 1.2); g.stroke(); g.strokeStyle = B_WOOD_D; g.lineWidth = 0.18; g.stroke();
    bShape(g, bRect(-0.3, -1.65, 0.3, -1.3), B_IRON, 233, 0.7, 0);
    if(!a){ bBeam(g, 0, 0.55, 0, -2.2, 0.09, B_WOOD_L, 234); fTip(g, -2.2, 0.28, 0.07); }
  });
}
// бомбарда: деревянное ложе, огромный ствол в железных обручах, жерло вперёд
function bBombard(g, x, y, face, fire = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.2, -2.8, 1.2, 2.6)), 1.8);
    for(const s of [-1, 1]) bBeam(g, s * 0.95, -2.6, s * 0.95, 2.5, 0.36, B_WOOD, 241 + s);
    for(const y0 of [-2.2, 0, 2.1]) bBeam(g, -1.15, y0, 1.15, y0, 0.3, B_WOOD_D, 243 + y0);
    bShape(g, bRect(-0.42, 0.4, 0.42, 2.0), "#4a4c50", 246, 1.0, 0.3);                        // зарядная каморa
    bShape(g, [[-0.62, 0.4], [0.62, 0.4], [0.7, -2.6], [-0.7, -2.6]], "#5c5e62", 247, 1.1, 0.35); // ствол
    bLines(g, [-2.3, -1.8, -1.3, -0.8, -0.3, 0.15].map(yy => [-0.66, yy, 0.66, yy]), "rgba(20,20,22,.6)", 0.8);
    g.beginPath(); g.ellipse(0, -2.6, 0.7, 0.16, 0, 0, 6.283); g.fillStyle = "#1c1c1e"; g.fill(); bInk(g, 0.8);
    if(fire > 0) bSmoke(g, 0, -3.1, fire, 249);
  });
}
// пушка: лафет на двух колёсах, хобот назад, бронзовый ствол с дульным кольцом
function bCannon(g, x, y, face, fire = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-0.9, -1.8, 0.9, 2.4)), 1.4);
    bBeam(g, 0, 0.2, 0, 2.4, 0.32, B_WOOD_D, 251);
    for(const s of [-1, 1]) bWheel(g, s * 0.78, 0, 0, 1.3, 252 + s);
    bBeam(g, -0.78, 0, 0.78, 0, 0.14, B_IRON, 254);
    bShape(g, [[-0.26, 0.9], [0.26, 0.9], [0.18, -1.7], [-0.18, -1.7]], B_BRONZE, 255, 1.0, 0.3);
    bShape(g, bRect(-0.22, -1.75, 0.22, -1.55), "#8a6430", 256, 0.7, 0); bShape(g, bCirc(0, 1.0, 0.16, 8), B_BRONZE, 257, 0.6, 0);
    if(fire > 0) bSmoke(g, 0, -2.1, fire, 259);
  });
}
// мортира: квадратное ложе, короткий толстый ствол стоймя — сверху жерло кругом, цапфы по бокам
function bMortar(g, x, y, face, fire = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.0, -1.0, 1.0, 1.0)), 1.4);
    bShape(g, bRect(-1.0, -1.0, 1.0, 1.0), B_WOOD, 261, 1.0, 0.4);
    bLines(g, [[-1, -0.35, 1, -0.35], [-1, 0.35, 1, 0.35]], "rgba(60,40,20,.5)", 0.4);
    bBeam(g, -0.95, 0, 0.95, 0, 0.18, B_IRON, 262);
    bShape(g, bCirc(0, -0.15, 0.62, 16), "#56585c", 263, 1.1, 0.3); g.beginPath(); g.arc(0, -0.15, 0.4, 0, 6.283); g.fillStyle = "#1c1c1e"; g.fill(); bInk(g, 0.6);
    if(fire > 0) bSmoke(g, 0, -0.4, fire, 264);
  });
}
// рибодекин: двуколка, на платформе — ряд тонких стволов, спереди — дощатый щит цвета стороны
function bRibauldequin(g, x, y, face, col = "#b0302a", fire = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.3, -1.3, 1.3, 2.4)), 1.6);
    bBeam(g, -0.25, 1.0, -0.25, 2.4, 0.16, B_WOOD_D, 271); bBeam(g, 0.25, 1.0, 0.25, 2.4, 0.16, B_WOOD_D, 272);
    bShape(g, bRect(-1.1, -0.9, 1.1, 1.1), B_WOOD, 273, 1.0, 0.4);
    for(const s of [-1, 1]) bWheel(g, s * 1.25, 0.2, 0, 1.3, 274 + s);
    for(let i = 0; i < 7; i++){ const xx = -0.9 + i * 0.3; bShape(g, bRect(xx - 0.1, -1.0, xx + 0.1, 0.8), "#56585c", 276 + i, 0.6, 0); }
    bShape(g, bRect(-1.2, -1.35, 1.2, -1.0), col, 284, 1.0, 0.3);
    if(fire > 0) bSmoke(g, 0, -1.8, fire, 285, 1.6);
  });
}
// маг-пушка: каменное кольцо с рунами, бронзовая тренога, кристалл светится цветом стороны и пульсирует
function bMagic(g, x, y, face, col = "#7a5ad0", t = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => { g.beginPath(); g.arc(0, 0, 1.25, 0, 6.283); }, 1.4);
    bShape(g, bCirc(0, 0, 1.25, 18), B_STONE_D, 291, 1.1, 0.4);
    g.beginPath(); g.arc(0, 0, 0.95, 0, 6.283); g.strokeStyle = "rgba(34,24,15,.45)"; g.lineWidth = bLw(g, 0.5); g.stroke();
    for(let i = 0; i < 8; i++){ const a = i / 8 * 6.283, rx = Math.cos(a) * 1.08, ry = Math.sin(a) * 1.08; g.save(); g.translate(rx, ry); g.rotate(a); bLines(g, [[-0.08, -0.1, 0.08, 0.1], [0.08, -0.1, -0.08, 0.1], [0, -0.12, 0, 0.12]], col, 0.5); g.restore(); }
    for(let i = 0; i < 3; i++){ const a = i / 3 * 6.283 - 1.57; bBeam(g, 0, 0, Math.cos(a) * 0.95, Math.sin(a) * 0.95, 0.12, B_BRONZE, 293 + i); }
    const pulse = 0.75 + 0.25 * Math.sin(t * 5), gr = g.createRadialGradient(0, -0.1, 0, 0, -0.1, 1.6 * pulse);
    gr.addColorStop(0, mix(col, "#ffffff", 0.6) + "cc"); gr.addColorStop(0.4, col + "66"); gr.addColorStop(1, col + "00");
    g.fillStyle = gr; g.beginPath(); g.arc(0, -0.1, 1.6 * pulse, 0, 6.283); g.fill();
    const hx = Array.from({length: 6}, (_, i) => { const a = i / 6 * 6.283; return [Math.cos(a) * 0.38, -0.1 + Math.sin(a) * 0.38]; });
    bShape(g, hx, mix(col, "#ffffff", 0.35), 297, 0.9, 0); bPoly(g, hx.slice(0, 3).concat([[0, -0.1]])); g.fillStyle = mix(col, "#ffffff", 0.7); g.fill();
  });
}
// таран: крыша из досок, обтянутая шкурами (двускатная, конёк вдоль), колёса по бокам, окованный лоб бревна торчит вперёд
function bRam(g, x, y, face, push = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.7, -4.6, 1.7, 4.0)), 3);
    for(const [sx, sy] of [[-1, -2.8], [1, -2.8], [-1, 2.4], [1, 2.4]]) bWheel(g, sx * 1.75, sy, 0, 1.2, 301 + sy);
    const head = -4.2 - 0.6 * push;
    bBeam(g, 0, 3.4, 0, head + 0.4, 0.55, B_WOOD_D, 305);
    bShape(g, [[-0.45, head + 0.5], [0.45, head + 0.5], [0.3, head - 0.15], [-0.3, head - 0.15]], B_IRON, 306, 1.0, 0.3);
    bRoofGable(g, -1.6, -3.8, 1.6, 3.6, B_HIDE, "shingle", false, 307);
    g.save(); bPoly(g, bRect(-1.6, -3.8, 1.6, 3.6)); g.clip();   // заплаты шкур
    for(let i = 0; i < 6; i++){ const cx = -1.1 + hash(309, i) * 2.2, cy = -3.3 + hash(310, i) * 6.4; g.beginPath(); g.ellipse(cx, cy, 0.5, 0.7, hash(311, i), 0, 6.283); g.fillStyle = "rgba(60,40,24,.35)"; g.fill(); }
    g.restore();
  });
}
// осадная башня: площадка из досок, по краям — щиты из досок и шкур с прорезями, спереди — откидной мост (down —
// опущен вперёд), люк лестницы; колёса по углам
function bSiegeTower(g, x, y, face, down = 0, col = "#b0302a", t = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-2.7, -2.9, 2.7, 2.9)), 14);
    for(const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) bWheel(g, sx * 2.85, sy * 2.2, 0, 1.3, 321 + sx + sy * 3);
    bShape(g, bRect(-2.7, -2.9, 2.7, 2.9), B_WOOD_L, 325, 1.2, 0.4);
    g.save(); bPoly(g, bRect(-2.7, -2.9, 2.7, 2.9)); g.clip(); const S = []; for(let u = -2.7; u < 2.7; u += 0.45) S.push([u, -2.9, u, 2.9]); bLines(g, S, "rgba(70,48,26,.45)", 0.4); g.restore();
    // щиты по трём сторонам: шкуры на досках, прорези бойниц
    for(const [pts, nx, ny] of [[bRect(-2.7, 2.35, 2.7, 2.9), 0, 1], [bRect(-2.7, -2.9, -2.15, 2.9), -1, 0], [bRect(2.15, -2.9, 2.7, 2.9), 1, 0]]){
      bShape(g, pts, bFacet(B_HIDE, nx, ny), 327 + nx + ny, 0.9, 0.4);
    }
    for(const [x0, y0, x1, y1] of [[-1.5, 2.55, -1.1, 2.7], [1.1, 2.55, 1.5, 2.7], [-2.5, -1.2, -2.35, -0.8], [2.35, 0.8, 2.5, 1.2]]){ g.fillStyle = B_INK; g.fillRect(x0, y0, x1 - x0, y1 - y0); }
    bShape(g, bRect(-0.6, 0.8, 0.6, 1.8), B_WOOD_D, 331, 0.8, 0);   // люк
    // мост: поднят — полоса у переднего края; опущен — вперёд на 4 м
    const L = 0.6 + 3.6 * down;
    bShape(g, bRect(-1.6, -2.9 - L, 1.6, -2.9), B_WOOD, 333, 1.0, 0.4);
    g.save(); bPoly(g, bRect(-1.6, -2.9 - L, 1.6, -2.9)); g.clip(); const P = []; for(let u = -2.9 - L + 0.4; u < -2.9; u += 0.4) P.push([-1.6, u, 1.6, u]); bLines(g, P, "rgba(70,48,26,.5)", 0.4); g.restore();
    bFlag(g, 2.4, 2.5, col, "#ece6d6", t, 1.6);
  });
}
// лестница: две тетивы и перекладины
function bLadder(g, x, y, ang, len = 6){
  bAt(g, x, y, ang, () => {
    bShadow(g, () => bPoly(g, bRect(-0.3, -len / 2, 0.3, len / 2)), 0.6);
    for(const s of [-1, 1]) bBeam(g, s * 0.25, -len / 2, s * 0.25, len / 2, 0.1, B_WOOD, 341 + s);
    for(let u = -len / 2 + 0.3; u < len / 2; u += 0.35) bBeam(g, -0.25, u, 0.25, u, 0.06, B_WOOD_L, 343);
  });
}
// щит на колёсах (мантелет): доски в ряд, подпорки сзади
function bMantlet(g, x, y, face, col){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.4, -0.3, 1.4, 0.3)), 2);
    for(const s of [-1, 1]) bBeam(g, s * 0.9, 0.2, s * 0.6, 1.0, 0.1, B_WOOD_D, 351 + s);
    bShape(g, bRect(-1.4, -0.3, 1.4, 0.15), B_WOOD, 353, 1.0, 0.4);
    g.save(); bPoly(g, bRect(-1.4, -0.3, 1.4, 0.15)); g.clip(); bLines(g, [-0.9, -0.45, 0, 0.45, 0.9].map(u => [u, -0.3, u, 0.15]), "rgba(60,40,20,.5)", 0.4); if(col){ g.fillStyle = col; g.fillRect(-0.2, -0.3, 0.4, 0.45); } g.restore();
  });
}
// дым выстрела: клубы растут и тают (p — доля 0…1 после выстрела)
function bSmoke(g, x, y, p, seed, k = 1){
  for(let i = 0; i < 5; i++){ const a = hash(seed, i) * 6.283, d = (0.3 + 1.4 * p) * k * hash(seed, i + 5), r = (0.4 + 0.9 * p) * k * (0.6 + 0.4 * hash(seed, i + 9));
    g.beginPath(); g.arc(x + Math.cos(a) * d, y - 0.8 * p * k + Math.sin(a) * d, r, 0, 6.283); g.fillStyle = `rgba(232,228,218,${(0.7 * (1 - p)).toFixed(2)})`; g.fill(); }
  if(p < 0.15){ g.beginPath(); g.arc(x, y, 0.5 * k, 0, 6.283); g.fillStyle = `rgba(255,200,90,${(1 - p / 0.15).toFixed(2)})`; g.fill(); }
}

// ═══════════ ЛАГЕРЬ ═══════════
// шатёр-колокол: полотно клиньями от столба, светлее к солнцу; верх цвета стороны; растяжки с кольями; вход — тёмный клин
function bBellTent(g, x, y, r, col, o = {}){
  const seed = o.seed ?? 401, cloth = o.cloth ?? B_CANVAS, n = 10;
  g.beginPath(); for(let k = 0; k < 8; k++){ const a = k / 8 * 6.283 + 0.2; g.moveTo(x + Math.cos(a) * r, y + Math.sin(a) * r); g.lineTo(x + Math.cos(a) * r * 1.55, y + Math.sin(a) * r * 1.55); }
  g.strokeStyle = "rgba(90,70,40,.6)"; g.lineWidth = bLw(g, 0.4); g.stroke();
  for(let k = 0; k < 8; k++){ const a = k / 8 * 6.283 + 0.2; g.beginPath(); g.arc(x + Math.cos(a) * r * 1.55, y + Math.sin(a) * r * 1.55, 0.07, 0, 6.283); g.fillStyle = B_WOOD_D; g.fill(); }
  bShadow(g, () => { g.beginPath(); g.arc(x, y, r, 0, 6.283); }, 3);
  for(let k = 0; k < n; k++){ const a0 = k / n * 6.283, a1 = a0 + 6.283 / n, am = (a0 + a1) / 2;
    g.beginPath(); g.moveTo(x, y); g.arc(x, y, r, a0, a1); g.closePath(); g.fillStyle = bFacet(k % 2 && o.stripes ? col : cloth, Math.cos(am), Math.sin(am)); g.fill(); bGrain(g, 0.3); }
  const door = (o.door ?? 1.57); g.beginPath(); g.moveTo(x, y); g.arc(x, y, r, door - 0.18, door + 0.18); g.closePath(); g.fillStyle = "rgba(40,30,20,.55)"; g.fill();
  const S = []; for(let k = 0; k < n; k++){ const a = k / n * 6.283; S.push([x, y, x + Math.cos(a) * r, y + Math.sin(a) * r]); } bLines(g, S, "rgba(60,46,30,.4)", 0.45);
  bRough(g, bCirc(x, y, r, 22), true, seed); bInk(g, 1.0);
  bShape(g, bCirc(x, y, r * 0.3, 12), col, seed + 1, 0.8, 0);
  g.beginPath(); g.arc(x, y, 0.12, 0, 6.283); g.fillStyle = "#d6b75a"; g.fill();
}
// палатка двускатная: конёк вдоль, скаты в два тона, торцы с тёмным входом, растяжки
function bRidgeTent(g, x, y, ang, w = 2.4, l = 3.2, o = {}){
  bAt(g, x, y, ang, () => {
    g.beginPath(); for(const s of [-1, 1]) for(const u of [-l / 2 + 0.3, l / 2 - 0.3]){ g.moveTo(s * w / 2, u); g.lineTo(s * (w / 2 + 0.9), u); } g.strokeStyle = "rgba(90,70,40,.6)"; g.lineWidth = bLw(g, 0.4); g.stroke();
    bShadow(g, () => bPoly(g, bRect(-w / 2, -l / 2, w / 2, l / 2)), 1.8);
    bRoofGable(g, -w / 2, -l / 2, w / 2, l / 2, o.cloth ?? B_CANVAS, "none", false, o.seed ?? 411);
    g.fillStyle = "rgba(40,30,20,.55)"; g.beginPath(); g.moveTo(-0.35, -l / 2); g.lineTo(0, -l / 2 + 0.6); g.lineTo(0.35, -l / 2); g.fill();
    if(o.col){ g.fillStyle = o.col; g.fillRect(-0.12, -l / 2 - 0.5, 0.24, 0.5); }
  });
}
// ставка полководца: большой круглый шатёр, клинья цвета стороны и второго цвета, фестоны по краю, знамя на столбе
function bPavilion(g, x, y, r, col, c2 = "#ece6d6", t = 0){
  bBellTent(g, x, y, r, col, {cloth: c2, stripes: true, seed: 421});
  g.save(); g.beginPath(); g.arc(x, y, r + 0.05, 0, 6.283); g.clip();
  for(let k = 0; k < 28; k++){ const a = k / 28 * 6.283; g.beginPath(); g.arc(x + Math.cos(a) * r, y + Math.sin(a) * r, 0.36, 0, 6.283); g.fillStyle = k % 2 ? col : c2; g.fill(); }
  g.restore(); g.beginPath(); g.arc(x, y, r, 0, 6.283); bInk(g, 1.0);
  bFlag(g, x, y, col, c2, t, 2.6);
}
// костёр: кольцо камней, поленья крест-накрест, пламя мерцает, тёплое пятно света
function bFire(g, x, y, t = 0, seed = 431){
  const gl = g.createRadialGradient(x, y, 0, x, y, 3.2); gl.addColorStop(0, "rgba(255,190,90,.35)"); gl.addColorStop(1, "rgba(255,190,90,0)"); g.fillStyle = gl; g.beginPath(); g.arc(x, y, 3.2, 0, 6.283); g.fill();
  for(let k = 0; k < 9; k++){ const a = k / 9 * 6.283; bShape(g, bCirc(x + Math.cos(a) * 0.75, y + Math.sin(a) * 0.75, 0.17, 7), B_STONE_D, seed + k, 0.5, 0); }
  bBeam(g, x - 0.55, y - 0.3, x + 0.55, y + 0.3, 0.16, B_WOOD_D, seed + 11); bBeam(g, x - 0.5, y + 0.35, x + 0.5, y - 0.35, 0.16, B_WOOD_D, seed + 12);
  const fl = 0.85 + 0.15 * Math.sin(t * 13 + x) + 0.08 * Math.sin(t * 29);
  for(const [r, c] of [[0.55, "#e2602a"], [0.38, "#f2a23a"], [0.2, "#ffe08a"]]){
    g.beginPath(); for(let k = 0; k < 10; k++){ const a = k / 10 * 6.283, rr = r * fl * (k % 2 ? 0.55 : 1) * (0.85 + 0.3 * hash(seed + Math.floor(t * 8), k)); k ? g.lineTo(x + Math.cos(a) * rr, y + Math.sin(a) * rr) : g.moveTo(x + Math.cos(a) * rr, y + Math.sin(a) * rr); }
    g.closePath(); g.fillStyle = c; g.fill();
  }
}
// повозка: кузов, полог на обручах (дугами поперёк), четыре колеса, оглобли вперёд
function bWagon(g, x, y, ang, o = {}){
  bAt(g, x, y, ang, () => {
    bShadow(g, () => bPoly(g, bRect(-1.0, -1.8, 1.0, 1.8)), 2.4);
    for(const s of [-1, 1]) bBeam(g, s * 0.35, -1.8, s * 0.5, -3.6, 0.1, B_WOOD_D, 441 + s);
    for(const [sx, sy] of [[-1, -1.1], [1, -1.1], [-1, 1.1], [1, 1.1]]) bWheel(g, sx * 1.05, sy, 0, 1.1, 443 + sy);
    bShape(g, bRect(-0.9, -1.75, 0.9, 1.75), B_WOOD, 447, 1.0, 0.4);
    if(o.cover !== false){
      bShape(g, bRect(-0.95, -1.5, 0.95, 1.5), o.cloth ?? B_CANVAS, 448, 1.0, 0.35);
      g.save(); bPoly(g, bRect(-0.95, -1.5, 0.95, 1.5)); g.clip(); const S = []; for(let u = -1.2; u <= 1.2; u += 0.6) S.push([-0.95, u, 0.95, u]); bLines(g, S, "rgba(80,60,36,.5)", 0.5);
      g.fillStyle = "rgba(255,255,255,.18)"; g.fillRect(0.1, -1.5, 0.85, 3); g.restore();
    } else for(let i = 0; i < 4; i++) bShape(g, bRect(-0.7 + (i % 2) * 0.75, -1.4 + Math.floor(i / 2) * 1.2, -0.05 + (i % 2) * 0.75, -0.4 + Math.floor(i / 2) * 1.2), B_WOOD_L, 449 + i, 0.7, 0.3);
  });
}
// ящики, бочки, мешки, поленница, стойка с копьями
function bCrate(g, x, y, a = 0.9, seed = 451){ bShadow(g, () => bPoly(g, bRect(x - a / 2, y - a / 2, x + a / 2, y + a / 2)), 0.9); bShape(g, bRect(x - a / 2, y - a / 2, x + a / 2, y + a / 2), B_WOOD_L, seed, 0.8, 0.3); bLines(g, [[x - a / 2, y - a / 2, x + a / 2, y + a / 2], [x + a / 2, y - a / 2, x - a / 2, y + a / 2]], "rgba(70,48,26,.5)", 0.45); }
function bBarrel(g, x, y, r = 0.38, seed = 461){ bShadow(g, () => { g.beginPath(); g.arc(x, y, r, 0, 6.283); }, 1); g.beginPath(); g.arc(x, y, r, 0, 6.283); g.fillStyle = B_WOOD; g.fill(); bInk(g, 0.7); g.beginPath(); g.arc(x, y, r * 0.66, 0, 6.283); g.strokeStyle = B_IRON; g.lineWidth = bLw(g, 0.8); g.stroke(); }
function bSack(g, x, y, ang, seed = 471){ bAt(g, x, y, ang, () => { bShadow(g, () => { g.beginPath(); g.ellipse(0, 0, 0.35, 0.5, 0, 0, 6.283); }, 0.6); g.beginPath(); g.ellipse(0, 0, 0.35, 0.5, 0, 0, 6.283); g.fillStyle = "#c9b48a"; g.fill(); bInk(g, 0.7); g.beginPath(); g.moveTo(-0.1, -0.45); g.lineTo(0.1, -0.45); g.strokeStyle = B_WOOD_D; g.lineWidth = bLw(g, 0.8); g.stroke(); }); }
function bLogs(g, x, y, ang, seed = 481){ bAt(g, x, y, ang, () => { for(let i = 0; i < 5; i++){ const yy = -0.6 + i * 0.3; bBeam(g, -0.9, yy, 0.9, yy, 0.26, i % 2 ? B_WOOD : B_WOOD_D, seed + i); g.beginPath(); g.arc(0.9, yy, 0.13, 0, 6.283); g.fillStyle = B_WOOD_L; g.fill(); bInk(g, 0.5); } }); }
function bRack(g, x, y, ang, seed = 491){ bAt(g, x, y, ang, () => { bBeam(g, -1.0, 0, 1.0, 0, 0.12, B_WOOD_D, seed); for(let i = 0; i < 6; i++){ const xx = -0.85 + i * 0.34; bBeam(g, xx, 0.15, xx + 0.05, -1.6, 0.05, B_WOOD, seed + i); fTip(g, -1.6, 0.18, 0.045, xx + 0.05); } }); }
// конь у коновязи (без всадника): части коня бойцов (В18)
function bHorse(B, x, y, face, k, t, seed){
  const P = horsePose(0, 0, t, seed), base = mR(mT(B, x, y), face);
  ctx.globalAlpha = 0.7; fImg(SHADOW, mR(mT(B, x - 0.1, y + 0.1), face), -0.45, -1.05, 0.9, 2.25); ctx.globalAlpha = 1;
  HLEG.forEach(([lx, ly], i) => put(fHLegSpr(k, k.socks & (1 << i)), mT(base, lx, ly + P.legs[i])));
  put(fHTailSpr(k), mR(mT(base, HTAIL[0], HTAIL[1]), P.tail));
  put(fHHeadSpr(k), mT(base, HNECK[0], HNECK[1] + P.nod));
  put(fHBodySpr(k), base); put(fHCoverSpr(k), base);
}
