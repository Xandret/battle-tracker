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
// мягкая тень влево вниз от предмета высотой h м: силуэт уносится далеко за край, на место приходит только его тень;
// размытие растёт с высотой, но не больше 1,4 м — иначе у стены и башни вблизи тень расплывается в пятно
function bShadow(g, path, h, alpha = 0.3){
  const S = fScale(g), d = bDpr(), M = g.getTransform();
  g.save(); g.setTransform(new DOMMatrix().translate(6000, 0).multiply(M));   // уносим в пикселях экрана — поворот предмета не мешает
  g.shadowColor = `rgba(28,22,12,${alpha})`; g.shadowBlur = Math.max(2, Math.min(0.45 * h, 1.4) * S); g.shadowOffsetX = -0.3 * h * S - 6000; g.shadowOffsetY = 0.24 * h * S;
  g.fillStyle = "#000"; path(); g.fill(); g.restore();
}
// ═══ Высота ═══
// Тень точки на высоте z м падает влево вниз на (−0,3; 0,24)·z — так же, как у bShadow. Что выше, то сверху чуть
// крупнее (камера ближе): +2% на метр. По этим двум приметам видно, что рычаг требушета поднялся, камень уходит
// вверх, а бойцы стоят на стене, а не у её подножия.
const B_SUN = [-0.3, 0.24], bUp = z => 1 + 0.02 * z;
// вектор тени на 1 м высоты в осях предмета, повёрнутого на face (как в bAt)
function bSun(face){ const c = Math.cos(face), s = Math.sin(face); return [B_SUN[0] * c + B_SUN[1] * s, B_SUN[1] * c - B_SUN[0] * s]; }
// сторона к солнцу (вправо вверх) в осях предмета: доля по x, −1…1 — блик на стволах
const bLit = face => 0.707 * (Math.cos(face) - Math.sin(face));
// тени частей с высотой одним путём: fn добавляет контуры (bSub, bDot) одного обхода — заливка разом, без двойного затемнения
function bCast(g, fn, alpha = 0.26){ g.beginPath(); fn(); g.fillStyle = `rgba(28,22,12,${alpha})`; g.fill(); }
const bArea = P => { let a = 0; for(let i = 0; i < P.length; i++){ const [x0, y0] = P[i], [x1, y1] = P[(i + 1) % P.length]; a += x0 * y1 - x1 * y0; } return a; };
const bSub = (g, pts) => { (bArea(pts) < 0 ? pts.slice().reverse() : pts).forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.closePath(); };
const bDot = (g, x, y, r) => { g.moveTo(x + r, y); g.arc(x, y, r, 0, 6.283); };
// выпуклая оболочка точек (брус, который поворачивается в высоту; ящик; ствол)
function bHull(P){
  const p = P.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]), cr = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]), lo = [], up = [];
  for(const q of p){ while(lo.length > 1 && cr(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop(); lo.push(q); }
  for(let i = p.length - 1; i >= 0; i--){ const q = p[i]; while(up.length > 1 && cr(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
  return lo.slice(0, -1).concat(up.slice(0, -1));
}
// точки [x, y, z] (ось предмета — x = 0) → вид сверху (поперёк ширина растёт с высотой) и тень на земле (sun — из bSun)
const bTop = P => bHull(P.map(([x, y, z]) => [x * bUp(z), y]));
const bShd = (P, sun) => bHull(P.map(([x, y, z]) => [x + sun[0] * z, y + sun[1] * z]));
// полоса ширины w вдоль отрезка — тени тонких частей
const bSeg = (a, b, w) => { const L = Math.hypot(b[0] - a[0], b[1] - a[1]) || 1e-6, nx = -(b[1] - a[1]) / L * w / 2, ny = (b[0] - a[0]) / L * w / 2; return [[a[0] + nx, a[1] + ny], [b[0] + nx, b[1] + ny], [b[0] - nx, b[1] - ny], [a[0] - nx, a[1] - ny]]; };
// цилиндр вдоль y сверху: тёмный край от солнца, блик ближе к солнцу (lit — из bLit)
function bCyl(g, r, base, lit){
  const gr = g.createLinearGradient(-r, 0, r, 0), hp = 0.5 + 0.26 * lit;
  gr.addColorStop(0, mix(base, "#000000", 0.32 + 0.2 * lit)); gr.addColorStop(Math.max(0.06, hp - 0.24), base);
  gr.addColorStop(hp, mix(base, "#ffffff", 0.45)); gr.addColorStop(Math.min(0.94, hp + 0.2), base); gr.addColorStop(1, mix(base, "#000000", 0.32 - 0.2 * lit));
  return gr;
}
const bRgba = (hex, a) => `rgba(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(",")},${Math.max(0, Math.min(1, a)).toFixed(3)})`;
// точка и направление на s метров вдоль ломаной
function bPointAt(pts, s){
  let s0 = 0;
  for(let i = 0; i + 1 < pts.length; i++){
    const [x0, y0] = pts[i], [x1, y1] = pts[i + 1], L = Math.hypot(x1 - x0, y1 - y0); if(L < 1e-6) continue;
    if(s <= s0 + L || i + 2 === pts.length){ const u = Math.min(L, Math.max(0, s - s0)); return {x: x0 + (x1 - x0) / L * u, y: y0 + (y1 - y0) / L * u, tx: (x1 - x0) / L, ty: (y1 - y0) / L}; }
    s0 += L;
  }
  return {x: pts[0][0], y: pts[0][1], tx: 1, ty: 0};
}
// грань светлее, если смотрит к солнцу (вправо вверх)
const bFacet = (base, nx, ny) => { const k = (nx * 0.7 - ny * 0.7) * 0.22; return k >= 0 ? mix(base, "#ffffff", k) : mix(base, "#000000", -k); };
// тонкие штрихи внутри текущего пути (обрезка), набор отрезков
function bLines(g, segs, col, k = 0.4){ g.beginPath(); for(const [x0, y0, x1, y1] of segs){ g.moveTo(x0, y0); g.lineTo(x1, y1); } g.strokeStyle = col; g.lineWidth = bLw(g, k); g.stroke(); }

// ═══ Полоса вдоль ломаной (стена, ров, частокол): левый и правый края со скосом в изломах ═══
function bBand(pts, w){
  const L = [], R = [], n = pts.length, closed = n > 2 && Math.hypot(pts[0][0] - pts[n - 1][0], pts[0][1] - pts[n - 1][1]) < 1e-6;   // замкнутая: углы и в начале
  for(let i = 0; i < n; i++){
    // в изломе — по биссектрисе соседних отрезков (не по хорде: у отрезков разной длины хорда косит и лента сужается)
    const p = pts[i], a = closed && i === 0 ? pts[n - 2] : pts[Math.max(0, i - 1)], b = closed && i === n - 1 ? pts[1] : pts[Math.min(n - 1, i + 1)];
    const nrm = (x, y) => { const l = Math.hypot(x, y); return l > 1e-9 ? [x / l, y / l] : [0, 0]; };
    const d1 = nrm(p[0] - a[0], p[1] - a[1]), d2 = nrm(b[0] - p[0], b[1] - p[1]);
    let [tx, ty] = nrm(d1[0] + d2[0], d1[1] + d2[1]); if(!tx && !ty) [tx, ty] = d1[0] || d1[1] ? d1 : d2;
    const k = (d1[0] || d1[1]) && (d2[0] || d2[1]) ? 1 / Math.max(0.5, d1[0] * tx + d1[1] * ty) : 1;
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
// Каменная стена во всю клетку карты (5 м, Terrain.CellM): по ней ходят и на ней дерутся. Снаружи — бруствер 0,75 м:
// мерлоны (светлые плиты, чуть выступают за край) и проёмы бойниц между ними (подоконник ниже — темнее); за ним —
// боевой ход из плит ~3,8 м (7 бойцов в ряд); изнутри — низкий парапет 0,4 м. Бруствер и парапет бросают тень на ход,
// если солнце с их стороны. Высота стены h (9 м) — в длинной мягкой тени. stairs: [{s, len, dir}] — марши во двор вдоль
// внутренней стороны: с какого метра ломаной, длина, куда спуск (+1 — по ходу ломаной). outer = +1 — зубцы слева
// по ходу ломаной, −1 — справа. Зубцы 0,8 м через 1,45 м: проём бойницы 0,65 м — в него ставят лестницу и через
// него перелезают. Возвращает {crenels: [{x, y, tx, ty, nx, ny}]} — середины проёмов на наружном краю, n — наружу.
const B_WALK = "#cdc5b4", B_SILL = "#8a8170";
function bWall(g, pts, o = {}){
  const w = o.w ?? 5, seed = o.seed ?? 1, side = o.outer ?? 1, h = o.h ?? 9, PAR = 0.75, INN = 0.4, a = w / 2;
  const band = bBand(pts, w);
  const off = d => { if(Math.abs(d) < 1e-6) return pts.map(p => p.slice()); const b = bBand(pts, 2 * Math.abs(d)); return (d > 0) === (side > 0) ? b.L : b.R; };
  const strip = (d0, d1) => off(d0).concat(off(d1).reverse());
  bShadow(g, () => bPoly(g, band.poly), h, 0.42);
  for(const st of o.stairs || []) bStairs(g, pts, st, w, side, h, seed + 40);
  bPoly(g, band.poly); g.fillStyle = B_WALK; g.fill(); bGrain(g, 0.45);
  g.save(); bPoly(g, band.poly); g.clip();
  // плиты хода: продольные швы и поперечные вразбежку
  const ww = w - PAR - INN, rows = Math.max(2, Math.round(ww / 0.95)), rw = ww / rows, segs = [];
  for(let r = 1; r < rows; r++){ const L = off(a - PAR - r * rw); for(let i = 0; i + 1 < L.length; i++) segs.push([L[i][0], L[i][1], L[i + 1][0], L[i + 1][1]]); }
  for(let r = 0; r < rows; r++){ const d0 = a - PAR - r * rw, d1 = d0 - rw;
    bWalk(pts, 1.15, 0.35 + 0.55 * (r % 2), (x, y, tx, ty) => { const nx = -ty * side, ny = tx * side; segs.push([x + nx * d0, y + ny * d0, x + nx * d1, y + ny * d1]); }); }
  bLines(g, segs, "rgba(96,86,70,.38)", 0.4);
  // тень бруствера (1,8 м над ходом) и парапета (1 м) на ход — по отрезкам, где солнце с их стороны
  g.beginPath();
  for(let i = 0; i + 1 < pts.length; i++){
    const [x0, y0] = pts[i], [x1, y1] = pts[i + 1], L = Math.hypot(x1 - x0, y1 - y0); if(L < 1e-6) continue;
    const tx = (x1 - x0) / L, ty = (y1 - y0) / L, nx = -ty * side, ny = tx * side, k = B_SUN[0] * nx + B_SUN[1] * ny;   // k > 0 — тени наружу
    const q = (d0, d1) => bSub(g, [[x0 + nx * d0, y0 + ny * d0], [x1 + nx * d0, y1 + ny * d0], [x1 + nx * d1, y1 + ny * d1], [x0 + nx * d1, y0 + ny * d1]]);
    if(k < 0) q(a - PAR, a - PAR + 1.8 * k); else q(-a + INN, -a + INN + 1.0 * k);
  }
  g.fillStyle = "rgba(40,30,18,.2)"; g.fill();
  g.restore();
  // внутренний парапет; над лестницами — проход в нём
  bShape(g, strip(-a + INN, -a), B_STONE, seed + 3, 0.7, 0.35);
  for(const st of o.stairs || []){
    const at = bPointAt(pts, st.s), dir = st.dir ?? 1, wd = st.w ?? 1.5, tx = at.tx * dir, ty = at.ty * dir, nx = -at.ty * side, ny = at.tx * side;
    const q = (u, d) => [at.x + tx * u + nx * d, at.y + ty * u + ny * d];
    bPoly(g, [q(0, -a + INN + 0.03), q(wd, -a + INN + 0.03), q(wd, -a - 0.04), q(0, -a - 0.04)]); g.fillStyle = B_WALK; g.fill();
    bLines(g, [[...q(0, -a + INN), ...q(0, -a)], [...q(wd, -a + INN), ...q(wd, -a)]], B_INK, 0.8);
  }
  // бруствер: подоконники бойниц, край хода, контур стены, сверху — мерлоны
  bPoly(g, strip(a, a - PAR)); g.fillStyle = B_SILL; g.fill(); bGrain(g, 0.35);
  bRough(g, off(a - PAR), false, seed + 9); bInk(g, 0.6);
  bRough(g, band.poly, true, seed); bInk(g, 1.15);
  const M = [], crenels = [];
  bWalk(off(a), 1.45, 0.7, (x, y, tx, ty) => { const nx = -ty * side, ny = tx * side, q = (u, v) => [x + tx * u + nx * v, y + ty * u + ny * v];
    M.push([q(-0.4, 0.14), q(0.4, 0.14), q(0.4, -PAR), q(-0.4, -PAR)]); const [cx, cy] = q(0.725, 0); crenels.push({x: cx, y: cy, tx, ty, nx, ny}); });
  M.forEach((m, i) => bShape(g, m, B_STONE_L, seed + i, 0.75, 0.3));
  return {crenels};
}
// марш во двор вдоль внутренней стороны стены: площадка у прохода в парапете, ступени вниз — темнее к земле; тень
// клином: у верха длинная, у земли сходит на нет
function bStairs(g, pts, st, w, side, h, seed){
  const at = bPointAt(pts, st.s), dir = st.dir ?? 1, len = st.len ?? 7, wd = st.w ?? 1.5, a = w / 2;
  const tx = at.tx * dir, ty = at.ty * dir, nx = at.ty * side, ny = -at.tx * side;   // u — вдоль, по спуску; v — внутрь двора
  const q = (u, v) => [at.x + tx * u + nx * v, at.y + ty * u + ny * v], top = [q(0, a), q(0, a + wd), q(wd, a + wd), q(wd, a)];
  bCast(g, () => bSub(g, bHull([q(len, a), q(len, a + wd)].concat(top, top.map(([x, y]) => [x + B_SUN[0] * h, y + B_SUN[1] * h])))), 0.22);
  const n = Math.max(3, Math.round((len - wd) / 0.32));
  for(let i = 0; i < n; i++){ const u0 = wd + (len - wd) * i / n, u1 = wd + (len - wd) * (i + 1) / n;
    bPoly(g, [q(u0, a), q(u1, a), q(u1, a + wd), q(u0, a + wd)]); g.fillStyle = mix(B_WALK, "#3a3024", 0.04 + 0.3 * i / n); g.fill(); }
  bPoly(g, top); g.fillStyle = B_WALK; g.fill(); bGrain(g, 0.3);
  const S = []; for(let i = 0; i <= n; i++){ const u = wd + (len - wd) * i / n; S.push([...q(u, a), ...q(u, a + wd)]); }
  bLines(g, S, "rgba(50,40,28,.5)", 0.45);
  bRough(g, [q(0, a), q(len, a), q(len, a + wd), q(0, a + wd)], true, seed); bInk(g, 0.9);
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
// ворота в проёме: тёмный проход сквозь стену (d — её толщина), две створки из досок с железными полосами на линии
// leafY поперёк прохода; state — closed, open, broken
function bGate(g, x, y, ang, w = 4.2, d = 5, state = "closed", seed = 11, leafY = 0){
  g.save(); g.translate(x, y); g.rotate(ang);
  bShape(g, bRect(-w / 2, -d / 2, w / 2, d / 2), "#2c241c", seed, 1.0, 0);
  const leaf = (sx, rot, brk) => {
    g.save(); g.translate(sx * w / 2, leafY); g.rotate(sx * rot);
    const L = w / 2 - 0.1, pts = bRect(sx < 0 ? 0 : -L, -0.2, sx < 0 ? L : 0, 0.2);
    bShape(g, brk ? bRot(pts, 0, 0, sx * 0.25) : pts, B_WOOD, seed + (sx > 0 ? 3 : 1), 0.8, 0.3);
    const S = []; for(let u = 0.35; u < L; u += 0.35) S.push([sx < 0 ? u : -u, -0.2, sx < 0 ? u : -u, 0.2]); bLines(g, S, "rgba(60,40,20,.5)", 0.35);
    g.fillStyle = B_IRON; for(const u of [0.25, 0.75]) g.fillRect(sx < 0 ? L * u - 0.07 : -L * u - 0.07, -0.2, 0.14, 0.4);
    g.restore();
  };
  if(state === "open"){ leaf(-1, -1.35); leaf(1, -1.35); }
  else if(state === "broken"){ leaf(-1, -0.5, true); bRubble(g, 0.9, leafY + 0.4, 1.0, seed + 9, B_WOOD_L); }
  else { leaf(-1, 0); leaf(1, 0); }
  g.restore();
}
// надвратная башня: проход сквозь всю толщину стены (d), ворота у наружного края, над внутренней частью — крытый
// переход; по бокам — квадратные башни, выступают наружу. Оси: −y — наружу
function bGatehouse(g, x, y, ang, o = {}){
  const seed = o.seed ?? 21, gw = o.gw ?? 4.4, d = o.d ?? 5.4, ta = o.ta ?? 6.4, y0 = -d / 2 + 1.7, y1 = d / 2;
  g.save(); g.translate(x, y); g.rotate(ang);
  bGate(g, 0, 0, 0, gw, d, o.state ?? "closed", seed, -d / 2 + 0.7);
  bShadow(g, () => bPoly(g, bRect(-gw / 2, y0, gw / 2, y1)), 10);
  bShape(g, bRect(-gw / 2 - 0.3, y0, gw / 2 + 0.3, y1), B_STONE, seed + 1, 1.0, 0.4);
  bRoofGable(g, -gw / 2, y0 + 0.15, gw / 2, y1 - 0.15, B_ROOF.slate, "slate", true, seed + 2);
  for(const s of [-1, 1]) bTowerSquare(g, s * (gw / 2 + ta / 2 - 0.3), -0.9, ta, {seed: seed + 5 + s, flag: s > 0 ? o.flag : null, c2: o.c2, t: o.t});
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
// мельница-башня: каменная башня, вокруг на середине высоты — галерея из досок с перилами; сверху — шатёр «лодкой»
// на поворотном круге, повёрнут к ветру (o.face). Спереди из шатра выходит вал, на нём четыре крыла: мах, решётка
// с ведомой стороны и парусина; крутятся в отвесной плоскости на высоте 12 м. Сзади от шатра до земли — водило
// с подкосами и воротом: им поворачивают шатёр. Крылья сверху видны почти ребром (их чуть развернуло ветром), их
// «крест» читается по тени, которая крутится на земле, — так строго сверху. o.lean > 0 — условность: плоскость
// крыльев завалена к зрителю на эту долю, и крест виден сам
function bWindmill(g, x, y, t = 0, seed = 141, o = {}){
  const face = o.face ?? -0.4, lean = o.lean ?? 0, sun = bSun(face), HUB = 12, HY = -3.9, L = 9, W = 2.0, TW = 0.3, rot = t * 0.9;
  const top = ([px, py, pz]) => [px * bUp(pz), py - lean * (pz - HUB)], shd = ([px, py, pz]) => [px + sun[0] * pz, py + sun[1] * pz];
  // точка крыла: r — вдоль маха от вала, n — поперёк, на ведомую сторону (решётка там); решётка развёрнута на TW назад
  const sail = (i, map) => {
    const th = rot + i * Math.PI / 2, c = Math.cos(th), s = Math.sin(th), Q = (r, n) => map([c * r + s * n, HY + TW * n, HUB + s * r - c * n]);
    return {s, cloth: [Q(2, 0.15), Q(L, 0.15), Q(L, W), Q(2, W)], stock: [Q(0.5, -0.14), Q(L + 0.3, -0.14), Q(L + 0.3, 0.14), Q(0.5, 0.14)],
      bars: [0.15, W / 2, W].map(n => [...Q(2, n), ...Q(L, n)]).concat(Array.from({length: 13}, (_, k) => { const r = 2 + (L - 2) * k / 12; return [...Q(r, 0), ...Q(r, W)]; }))};
  };
  const drawSail = S => {
    bPoly(g, S.cloth); g.fillStyle = "rgba(234,224,200,.94)"; g.fill(); bGrain(g, 0.25);
    bLines(g, S.bars, B_WOOD_D, 0.5); bRough(g, S.cloth, true, seed + 3, bLw(g, 0.3)); bInk(g, 0.6);
    bShape(g, S.stock, B_WOOD, seed + 4, 0.7, 0);
  };
  const ring = (r0, r1) => { g.beginPath(); g.arc(0, 0, r1, 0, 6.283); g.moveTo(r0, 0); g.arc(0, 0, r0, 0, 6.283, true); };
  bAt(g, x, y, face, () => {
    // двор: утоптанная земля, мешки у двери, запасной жёрнов
    g.beginPath(); g.ellipse(0, 1.5, 6.2, 6.8, 0, 0, 6.283); g.fillStyle = "rgba(170,150,110,.55)"; g.fill(); bGrain(g, 0.3);
    bSack(g, -3.4, 4.4, 0.5, seed + 11); bSack(g, -2.6, 5.0, -0.2, seed + 12); bSack(g, -3.7, 5.4, 1.2, seed + 13);
    bShadow(g, () => { g.beginPath(); g.arc(3.9, 4.6, 0.75, 0, 6.283); }, 0.4);
    g.beginPath(); g.arc(3.9, 4.6, 0.75, 0, 6.283); g.fillStyle = B_STONE_D; g.fill(); bGrain(g, 0.4); bInk(g, 0.8);
    g.beginPath(); g.arc(3.9, 4.6, 0.16, 0, 6.283); g.fillStyle = "#4a4136"; g.fill(); bLines(g, [0, 1, 2, 3].map(k => { const a = k * 0.785; return [3.9 + Math.cos(a) * 0.25, 4.6 + Math.sin(a) * 0.25, 3.9 + Math.cos(a) * 0.68, 4.6 + Math.sin(a) * 0.68]; }), "rgba(60,52,40,.45)", 0.4);
    // тени: башня; водило с подкосами и крылья — по высоте
    bShadow(g, () => { g.beginPath(); g.arc(0, 0, 3.4, 0, 6.283); }, 10);
    const S = [0, 1, 2, 3].map(i => sail(i, shd));
    bCast(g, () => { bSub(g, bSeg(shd([0, 2.6, 10]), [0, 9.6], 0.36)); for(const sx of [-1, 1]) bSub(g, bSeg(shd([sx * 1.8, 1.8, 10]), shd([0, 6.4, 4.5]), 0.2)); for(const q of S){ bSub(g, q.cloth); bSub(g, q.stock); } }, 0.3);
    bLines(g, S.flatMap(q => q.bars), "rgba(28,22,12,.2)", 0.5);
    // галерея: доски по кругу, перила
    ring(3.05, 4.15); g.fillStyle = B_WOOD_L; g.fill("evenodd"); bGrain(g, 0.35);
    bLines(g, Array.from({length: 40}, (_, k) => { const a = k / 40 * 6.283; return [Math.cos(a) * 3.05, Math.sin(a) * 3.05, Math.cos(a) * 4.15, Math.sin(a) * 4.15]; }), "rgba(70,48,26,.45)", 0.35);
    bRough(g, bCirc(0, 0, 4.15, 36), true, seed + 5); bInk(g, 0.9);
    g.beginPath(); g.arc(0, 0, 3.95, 0, 6.283); g.strokeStyle = B_WOOD_D; g.lineWidth = bLw(g, 0.6); g.stroke();
    // башня
    bShape(g, bCirc(0, 0, 3.05, 28), B_STONE, seed + 6, 1.0, 0.45);
    const T = [0, 1, 2, 3].map(i => sail(i, top));
    if(lean > 0) T.filter(q => q.s < 0).forEach(drawSail);
    // шатёр «лодкой»: два ската (светлее к солнцу), дранка поперёк, конёк вдоль вала
    const cap = Array.from({length: 28}, (_, k) => { const a = k / 28 * 6.283, c = Math.cos(a), s = Math.sin(a); return [2.45 * Math.sign(c) * Math.pow(Math.abs(c), 0.8), (s < 0 ? 2.95 : 2.7) * s]; });
    for(const sx of [-1, 1]){ g.save(); g.beginPath(); g.rect(sx < 0 ? -3 : 0, -3.2, 3, 6.4); g.clip(); bPoly(g, cap); g.fillStyle = bFacet(B_ROOF.shingle, sx * Math.cos(face), sx * Math.sin(face)); g.fill(); bGrain(g, 0.3); g.restore(); }
    g.save(); bPoly(g, cap); g.clip(); bRoofTex(g, "shingle", -2.5, -3, 2.5, 2.8, false); g.restore();
    bLines(g, [[0, -2.95, 0, 2.7]], "rgba(30,20,12,.55)", 0.8);
    bRough(g, cap, true, seed + 7); bInk(g, 1.0);
    // водило: от шатра к земле — сужается (ниже — дальше от камеры), подкосы, ворот и столбики
    const tp = (yy, z) => [0, yy, z];
    for(const sx of [-1, 1]){ const a = top([sx * 1.8, 1.8, 10]), b = top([0, 6.4, 4.5]); bShape(g, bSeg(a, b, 0.2), B_WOOD_D, seed + 8 + sx, 0.6, 0); }
    { const a = top(tp(2.6, 10)), b = top(tp(9.6, 0.4)); bShape(g, [[a[0] - 0.21, a[1]], [a[0] + 0.21, a[1]], [b[0] + 0.15, b[1]], [b[0] - 0.15, b[1]]], B_WOOD, seed + 10, 0.8, 0.2); }
    bShape(g, bRect(-0.75, 9.45, 0.75, 9.75), B_WOOD_D, seed + 14, 0.7, 0);
    for(const sx of [-1, 1]) bShape(g, bRect(sx * 0.75 - 0.08, 9.3, sx * 0.75 + 0.08, 9.9), B_WOOD_D, seed + 15 + sx, 0.5, 0);
    for(const [px, py] of [[-2.2, 10.4], [2.3, 10.2]]){ g.beginPath(); g.arc(px, py, 0.17, 0, 6.283); g.fillStyle = B_WOOD; g.fill(); bInk(g, 0.6); }
    // вал и крылья; на конце вала — железная головка
    bShape(g, bSeg(top([0, -2.6, 12]), top([0, HY, HUB]), 0.55), B_WOOD_D, seed + 16, 0.8, 0.2);
    (lean > 0 ? T.filter(q => q.s >= 0) : T.slice().sort((a, b) => a.s - b.s)).forEach(drawSail);
    { const [hx, hy] = top([0, HY, HUB]); bShape(g, bRect(hx - 0.5, hy - 0.35, hx + 0.5, hy + 0.25), B_IRON, seed + 17, 0.8, 0); }
  });
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
// требушет: сани из брусьев, две А-образные стойки с подкосами, ось на высоте 4,2 м; рычаг — длинное плечо 5,4 м,
// короткое 1,6 — с ящиком противовеса и пращой 3,2 м; сзади ворот. p — доля цикла 0…1:
//   до 0,26 — взведён: длинное плечо внизу сзади, противовес поднят над осью, праща с камнем лежит в жёлобе;
//   0,26–0,38 — взмах: противовес падает, плечо идёт вверх и вперёд, праща захлёстывает через верх;
//   0,38 — камень сорвался: уходит вперёд и вверх — растёт, тень отстаёт и бежит по земле; рычаг перемахивает и
//   качается, пока не встанет стоймя (сверху — короткий торец и длинная тень-шест);
//   0,65–0,92 — ворот тянет плечо вниз; с 0,96 — в праще новый камень.
// Высоту выдают тени частей и рост того, что выше (bUp). o.per — длина цикла в секундах (для полёта камня).
const TREB = {H: 4.2, L: 5.4, S: 1.6, sling: 3.2, a0: -Math.acos(-3.9 / 5.4), a1: 0.75, P1: 0.26, P2: 0.38, P3: 0.66, P4: 0.92};
function trebPose(p, per){
  const T = TREB, dir = (a, l) => [-Math.sin(a) * l, Math.cos(a) * l];   // угол от «стоймя», плюс — вперёд: [y, z]
  let a, swing = -1, tau = -1;
  if(p < T.P1 || p >= T.P4) a = T.a0;
  else if(p < T.P2){ swing = (p - T.P1) / (T.P2 - T.P1); a = T.a0 + (T.a1 - T.a0) * Math.pow(swing, 1.7); }
  else if(p < T.P3){ const v = (p - T.P2) / (T.P3 - T.P2); tau = (p - T.P2) * per;
    a = v < 0.12 ? T.a1 + (1.75 - T.a1) * (1 - Math.pow(1 - v / 0.12, 2)) : 1.75 * Math.exp(-3.5 * (v - 0.12)) * Math.cos(9 * (v - 0.12)); }
  else a = T.a0 * ease((p - T.P3) / (T.P4 - T.P3));
  const [ty, tz] = dir(a, T.L), tip = [0, ty, T.H + tz], [ky, kz] = dir(a, -T.S), cw = [0, ky, T.H + kz - 0.95];
  // праща лежит: от конца плеча вниз до земли, остальное — по жёлобу вперёд, чуть сбоку; иначе висит
  const lying = q => { const rest = T.sling - Math.max(0, q[2] - 0.25); return rest > 0 ? [0.5 * Math.min(1, rest / 1.5), q[1] - rest, 0.25] : [0, q[1], q[2] - T.sling]; };
  let pouch, stone = null, fly = null;
  if(swing >= 0){ const [dy, dz] = dir(Math.PI / 2 + 5.5 * Math.pow(swing, 2.2), T.sling); pouch = [0.5 * (1 - swing), tip[1] + dy, Math.max(0.25, tip[2] + dz)]; stone = pouch; }
  else if(tau >= 0){
    const [ry, rz] = dir(T.a1, T.L), [dy, dz] = dir(Math.PI / 2 + 5.5, T.sling), at = q => [0, ry + dy - 19.9 * q, T.H + rz + dz + 16.7 * q - 4.9 * q * q];
    fly = at(tau).concat(Math.max(0, Math.min(1, 1 - (tau - 0.35) / 0.25))); fly.trail = [1, 2, 3, 4].map(i => at(Math.max(0, tau - 0.035 * i)));
    const [ey, ez] = dir(Math.PI + 0.7 * Math.sin(tau * 9) * Math.exp(-2 * tau), T.sling);
    pouch = tip[2] + ez < 0.25 ? lying(tip) : [0, tip[1] + ey, tip[2] + ez];
  } else { pouch = lying(tip); if(p < T.P1 || p >= 0.96) stone = pouch; }
  return {a, tip, cw, pouch, stone, fly, rope: p >= T.P3 || p < T.P1};
}
function bTrebuchet(g, x, y, face, p = 0, o = {}){
  const T = TREB, sun = bSun(face), P = trebPose(frac(p), o.per ?? 6), sh = q => [q[0] + sun[0] * q[2], q[1] + sun[1] * q[2]];
  // рычаг — брус в плоскости качания: углы сбоку (толщина 0,46) на ширину 0,5
  const c = Math.cos(P.a), s = Math.sin(P.a), py = c * 0.23, pz = s * 0.23, K = [T.S * s, T.H - T.S * c], arm = [], box = [];
  for(const [yy, zz] of [[K[0] + py, K[1] + pz], [K[0] - py, K[1] - pz], [P.tip[1] + py, P.tip[2] + pz], [P.tip[1] - py, P.tip[2] - pz]]) arm.push([-0.25, yy, zz], [0.25, yy, zz]);
  for(const dx of [-1, 1]) for(const dy of [-0.65, 0.65]) for(const dz of [-0.65, 0.65]) box.push([dx, P.cw[1] + dy, P.cw[2] + dz]);
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.8, -3.2, 1.8, 3.5)), 0.8);
    bCast(g, () => {
      for(const s2 of [-1, 1]){ const ap = sh([s2 * 1.4, 0, T.H]); bSub(g, bSeg([s2 * 1.4, -2.2], ap, 0.3)); bSub(g, bSeg([s2 * 1.4, 2.2], ap, 0.3)); }
      bSub(g, bSeg(sh([-1.85, 0, T.H]), sh([1.85, 0, T.H]), 0.22));
      bSub(g, bShd(arm, sun)); bSub(g, bShd(box, sun));
      if(P.pouch[2] > 0.3){ bSub(g, bSeg(sh(P.tip), sh(P.pouch), 0.07)); bDot(g, ...sh(P.pouch), 0.36); }
    });
    // сани, поперечины, ворот с вымбовками
    for(const s2 of [-1, 1]) bBeam(g, s2 * 1.4, -3.2, s2 * 1.4, 3.4, 0.45, B_WOOD, 181 + s2);
    for(const y0 of [-2.6, 2.6]) bBeam(g, -1.8, y0, 1.8, y0, 0.4, B_WOOD_D, 185 + y0);
    bBeam(g, -1.15, 3.05, 1.15, 3.05, 0.34, B_WOOD_L, 187);
    for(const s2 of [-1, 1]){ bBeam(g, s2 * 0.95, 2.6, s2 * 0.95, 3.5, 0.08, B_WOOD_D, 188 + s2); bBeam(g, s2 * 0.6, 3.05, s2 * 1.3, 3.05, 0.08, B_WOOD_D, 190 + s2); }
    const low = P.pouch[2] < 1.2, sling = () => {
      const k = bUp(P.pouch[2]), px = P.pouch[0] * k;
      g.beginPath(); g.moveTo(0, P.tip[1]); g.lineTo(px, P.pouch[1]); g.strokeStyle = B_INK; g.lineWidth = bLw(g, 1.0); g.stroke(); g.strokeStyle = B_ROPE; g.lineWidth = bLw(g, 0.45); g.stroke();
      bShape(g, bCirc(px, P.pouch[1], 0.38 * k, 10), B_HIDE, 195, 0.7, 0);
      if(P.stone) bShape(g, bCirc(px, P.pouch[1], 0.27 * k, 9), B_STONE_D, 196, 0.6, 0.2);
    };
    if(P.rope){ g.beginPath(); g.moveTo(0, 3.05); g.lineTo(0, P.tip[1]); g.strokeStyle = B_INK; g.lineWidth = bLw(g, 1.0); g.stroke(); g.strokeStyle = B_ROPE; g.lineWidth = bLw(g, 0.5); g.stroke(); }
    if(low) sling();
    const cwLow = P.cw[2] + 0.65 < T.H, drawBox = () => {
      const k = bUp(P.cw[2] + 0.65), cy = P.cw[1];
      bShape(g, bRect(-1.0 * k, cy - 0.65 * k, 1.0 * k, cy + 0.65 * k), B_WOOD_D, 199, 1.0, 0.3);
      for(let i = 0; i < 4; i++) bShape(g, bCirc((-0.45 + (i % 2) * 0.9) * k, cy + (-0.3 + Math.floor(i / 2) * 0.6) * k, 0.27 * k, 8), B_STONE_D, 200 + i, 0.5, 0);
      bBeam(g, -1.08 * k, cy, 1.08 * k, cy, 0.1, B_IRON, 204);
    };
    if(cwLow) drawBox();
    // стойки: ноги А-рам, подкосы, подушки оси; ось
    for(const s2 of [-1, 1]){
      bBeam(g, s2 * 1.4, -2.2, s2 * 1.4, 2.2, 0.32, B_WOOD_D, 189 + s2);
      for(const e of [-1, 1]) bBeam(g, s2 * 1.4, e * 1.2, s2 * 1.8, e * 2.5, 0.15, B_WOOD, 192 + s2 + e);
      bShape(g, bRect(s2 * 1.4 - 0.36, -0.36, s2 * 1.4 + 0.36, 0.36), B_WOOD_L, 191 + s2, 0.8, 0.3);
    }
    bBeam(g, -1.85, 0, 1.85, 0, 0.22, B_IRON, 193);
    // рычаг: оболочка с высотой; железные обоймы у оси и крюк для пращи на конце
    const armTop = bTop(arm); bShape(g, armTop, B_WOOD_L, 197, 1.0, 0.3);
    g.save(); bPoly(g, armTop); g.clip(); bLines(g, [-0.35, 0.35].map(u => { const yy = -Math.sin(P.a) * u; return [-0.4, yy, 0.4, yy]; }), B_IRON, 1.4); g.restore();
    { const k = bUp(P.tip[2]); g.beginPath(); g.arc(0, P.tip[1], 0.09 * k, 0, 6.283); g.fillStyle = B_IRON; g.fill(); }
    if(!cwLow) drawBox();
    if(!low) sling();
    if(P.fly && P.fly[3] > 0){
      const [fx, fy, fz, al] = P.fly, k = bUp(fz), [qx, qy] = sh(P.fly);
      g.save(); g.globalAlpha *= al;
      g.beginPath(); g.arc(qx, qy, 0.3, 0, 6.283); g.fillStyle = "rgba(28,22,12,.26)"; g.fill();
      P.fly.trail.forEach((q, i) => { g.beginPath(); g.arc(q[0], q[1], 0.27 * bUp(q[2]) * (1 - 0.15 * i), 0, 6.283); g.fillStyle = `rgba(236,232,222,${(0.45 - 0.1 * i).toFixed(2)})`; g.fill(); });   // след
      bShape(g, bCirc(fx, fy, 0.3 * k, 9), B_STONE_D, 198, 0.7, 0.2);
      g.restore();
    }
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
// ═══ Пороховые орудия ═══
// бомбарда (XV в.): ствол из железных полос (продольные швы) в частых обручах, дуло раструбом; позади — камора уже,
// с гнёздами под рычаги (её свинчивают) и запалом; лежит в колоде на шпалах под железными хомутами, под дулом — клинья;
// сзади — упорный брус на кольях, вбитых в землю. fire — доля после выстрела: короткий откат, вспышка, дым из дула и
// струйка из запала
function bBombard(g, x, y, face, fire = 0){
  const rec = fire > 0 ? 0.3 * (fire < 0.05 ? fire / 0.05 : 1 - ease(Math.min(1, (fire - 0.05) / 0.5))) : 0, lit = bLit(face), IRON = "#5d5f63";
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.55, -2.95, 1.55, 3.55)), 1.5);
    for(let i = 0; i < 5; i++){ const sx = -1.2 + i * 0.6; bShape(g, bCirc(sx, 4.0, 0.17, 9), B_WOOD_D, 240 + i, 0.7, 0); g.beginPath(); g.arc(sx, 4.0, 0.08, 0, 6.283); g.fillStyle = B_WOOD_L; g.fill(); }
    for(const s of [-1, 1]) bBeam(g, s * 0.95, 3.3, s * 1.25, 3.95, 0.16, B_WOOD, 238 + s);
    for(const y0 of [-2.4, -0.7, 1.0, 2.6]) bBeam(g, -1.6, y0, 1.6, y0, 0.32, B_WOOD_D, 243 + y0);
    bShape(g, bRect(-0.92, -2.8, 0.92, 2.95), B_WOOD, 245, 0.9, 0.35);                          // колода
    for(const s of [-1, 1]) bBeam(g, s * 1.1, -2.9, s * 1.1, 3.0, 0.4, B_WOOD_L, 246 + s);
    bBeam(g, -1.5, 3.28, 1.5, 3.28, 0.55, B_WOOD_D, 248);                                       // упорный брус
    for(const s of [-1, 1]) bShape(g, [[s * 0.66, -2.5], [s * 0.9, -2.5], [s * 0.9, -2.0]], B_WOOD_L, 249 + s, 0.6, 0);   // клинья
    g.save(); g.translate(0, rec);
    // камора: обручи, гнёзда под рычаги, запал
    const ch = [[-0.46, 0.4], [0.46, 0.4], [0.46, 2.12], [0.34, 2.32], [-0.34, 2.32], [-0.46, 2.12]], chH = [0.62, 1.0, 1.38, 1.76, 2.1];
    bPoly(g, ch); g.fillStyle = bCyl(g, 0.46, IRON, lit); g.fill(); bGrain(g, 0.3);
    g.save(); bPoly(g, ch); g.clip();
    g.fillStyle = bCyl(g, 0.46, mix(IRON, "#ffffff", 0.14), lit); for(const yy of chH) g.fillRect(-0.5, yy - 0.05, 1, 0.1);
    bLines(g, chH.flatMap(yy => [[-0.5, yy - 0.05, 0.5, yy - 0.05], [-0.5, yy + 0.05, 0.5, yy + 0.05]]), "rgba(20,20,22,.55)", 0.4);
    g.restore();
    bRough(g, ch, true, 251); bInk(g, 1.0);
    g.fillStyle = "#18181a"; for(const yy of [0.81, 1.57]) for(const xx of [-0.27, 0, 0.27]) g.fillRect(xx - 0.06, yy - 0.06, 0.12, 0.12);
    g.beginPath(); g.arc(0, 1.93, 0.04, 0, 6.283); g.fill();
    // ствол: швы полос, обручи, раструб
    const br = [[-0.66, 0.5], [0.66, 0.5], [0.64, -2.35], [0.75, -2.45], [0.75, -2.74], [-0.75, -2.74], [-0.75, -2.45], [-0.64, -2.35]], hoops = [];
    for(let yy = 0.28; yy > -2.3; yy -= 0.31) hoops.push(yy);
    bPoly(g, br); g.fillStyle = bCyl(g, 0.75, IRON, lit); g.fill(); bGrain(g, 0.3);
    g.save(); bPoly(g, br); g.clip();
    bLines(g, [-0.48, -0.24, 0, 0.24, 0.48].map(xx => [xx, 0.5, xx, -2.35]), "rgba(20,20,22,.3)", 0.4);
    g.fillStyle = bCyl(g, 0.75, mix(IRON, "#ffffff", 0.14), lit); for(const yy of hoops) g.fillRect(-0.8, yy - 0.055, 1.6, 0.11);
    g.fillStyle = bCyl(g, 0.75, mix(IRON, "#ffffff", 0.1), lit); g.fillRect(-0.8, -2.74, 1.6, 0.3);
    bLines(g, hoops.flatMap(yy => [[-0.8, yy - 0.055, 0.8, yy - 0.055], [-0.8, yy + 0.055, 0.8, yy + 0.055]]).concat([[-0.8, -2.44, 0.8, -2.44]]), "rgba(20,20,22,.55)", 0.4);
    g.restore();
    bRough(g, br, true, 252); bInk(g, 1.1);
    bShape(g, bRect(-0.7, 0.36, 0.7, 0.56), mix(IRON, "#ffffff", 0.08), 253, 0.9, 0);          // стык с каморой
    g.beginPath(); g.ellipse(0, -2.74, 0.75, 0.13, 0, 0, 6.283); g.fillStyle = mix(IRON, "#ffffff", 0.25); g.fill(); bInk(g, 0.8);
    g.beginPath(); g.ellipse(0, -2.74, 0.5, 0.085, 0, 0, 6.283); g.fillStyle = "#151517"; g.fill();
    g.restore();
    // хомуты через ствол к колоде — держат на месте, ствол ходит под ними
    for(const yy of [-1.62, -0.3]){ bShape(g, bRect(-1.12, yy - 0.08, 1.12, yy + 0.08), "#3e4044", 254, 0.7, 0); for(const s of [-1, 1]){ g.beginPath(); g.arc(s * 1.1, yy, 0.05, 0, 6.283); g.fillStyle = B_STEEL; g.fill(); } }
    if(fire > 0){
      if(fire < 0.4){ const v = fire / 0.4; g.beginPath(); g.arc(0.15 + 0.5 * v, 1.93 + rec - 0.4 * v, 0.12 + 0.35 * v, 0, 6.283); g.fillStyle = `rgba(232,228,218,${(0.7 * (1 - v)).toFixed(2)})`; g.fill(); }
      bGunFx(g, fire, -2.8, 1.7, 249);
    }
  });
}
// ствол пушки сверху: [y, r] от казны к дулу (вперёд — минус); где радиус скачет — пояса
const GUN = [[1.13, 0.05], [1.05, 0.05], [1.04, 0.25], [0.96, 0.25], [0.95, 0.225], [0.08, 0.212], [0.07, 0.235], [0, 0.235], [-0.01, 0.2], [-0.55, 0.19], [-0.56, 0.21], [-0.62, 0.21], [-0.63, 0.175], [-1.45, 0.155], [-1.46, 0.172], [-1.54, 0.172], [-1.55, 0.16], [-1.68, 0.205], [-1.76, 0.205], [-1.78, 0.185]];
const GUN_RINGS = [1.04, 0.96, 0.075, 0, -0.56, -0.62, -1.46, -1.54, -1.68], GUN_MUZZLE = -1.78;
// колесо сверху: обод из косяков с железной шиной и гвоздями; ступица с обоймами, конец оси и чека — наружу (out — ±1)
function bWheelTop(g, x, y, out, len = 1.4, seed = 175){
  const R = (u0, u1, y0, y1) => bRect(Math.min(x + out * u0, x + out * u1), y0, Math.max(x + out * u0, x + out * u1), y1);
  bShape(g, bRect(x - 0.08, y - len / 2, x + 0.08, y + len / 2), B_WOOD_D, seed, 0.85, 0.25);
  g.fillStyle = B_IRON; g.fillRect(x - 0.045, y - len * 0.36, 0.09, len * 0.72);
  g.fillStyle = "#c9c2b0"; for(let i = 0; i < 6; i++) g.fillRect(x - 0.013, y - len * 0.3 + i * len * 0.12 - 0.013, 0.026, 0.026);
  bShape(g, R(0.08, 0.36, y - 0.17, y + 0.17), B_WOOD, seed + 1, 0.7, 0.2);
  g.fillStyle = B_IRON; for(const u of [0.14, 0.3]) g.fillRect(x + out * u - 0.02, y - 0.17, 0.04, 0.34);
  bShape(g, R(0.36, 0.46, y - 0.06, y + 0.06), B_IRON, seed + 2, 0.6, 0);
  g.beginPath(); g.arc(x + out * 0.42, y, 0.025, 0, 6.283); g.fillStyle = "#c9c2b0"; g.fill();
}
// бронзовый ствол: цапфы, винград, пояса, дельфины, запал; base — металл
function bGunBarrel(g, lit, base){
  const R = GUN.map(([yy, r]) => [r, yy]), poly = R.concat(R.slice().reverse().map(([r, yy]) => [-r, yy]));
  for(const s of [-1, 1]) bShape(g, bRect(Math.min(s * 0.2, s * 0.4), -0.14, Math.max(s * 0.2, s * 0.4), 0.06), base, 263 + s, 0.6, 0);
  bShape(g, bCirc(0, 1.2, 0.085, 10), base, 265, 0.7, 0);
  bPoly(g, poly); g.fillStyle = bCyl(g, 0.25, base, lit); g.fill(); bGrain(g, 0.25);
  g.save(); bPoly(g, poly); g.clip();
  bLines(g, GUN_RINGS.map(yy => [-0.3, yy, 0.3, yy]), "rgba(40,26,10,.6)", 0.55);
  bLines(g, GUN_RINGS.map(yy => [-0.3, yy - 0.02, 0.3, yy - 0.02]), "rgba(255,236,190,.45)", 0.35);
  g.restore();
  bRough(g, poly, true, 266, bLw(g, 0.4)); bInk(g, 0.95);
  for(const s of [-1, 1]){
    g.beginPath(); g.moveTo(s * 0.04, 0.02); g.bezierCurveTo(s * 0.2, 0.04, s * 0.2, 0.24, s * 0.04, 0.27); g.strokeStyle = B_INK; g.lineWidth = 0.04 + bLw(g, 0.8); g.stroke(); g.strokeStyle = mix(base, "#ffffff", 0.3); g.lineWidth = 0.04; g.stroke();
    bShape(g, bRect(Math.min(s * 0.3, s * 0.44), -0.17, Math.max(s * 0.3, s * 0.44), 0.09), B_IRON, 267 + s, 0.6, 0);
  }
  g.fillStyle = "#1c1c1e"; g.beginPath(); g.arc(0, 0.86, 0.03, 0, 6.283); g.fill();
  g.beginPath(); g.ellipse(0, GUN_MUZZLE + 0.01, 0.11, 0.035, 0, 0, 6.283); g.fill();
}
// пушка (XVI в.): лафет — две станины, сходятся к хоботу, поперечины и оковка, на хоботе — плита и кольцо; ось и два
// больших колеса; бронзовый ствол с поясами, цапфами под накладками, двумя «дельфинами» и запалом; под казной —
// подушка и клин. На земле — банник и ведро (o.kit === false — без них). fire — доля 0…1 после выстрела: лафет
// откатывается на 1,1 м и его накатывают обратно; ядро, вспышка, дым. o.fx(g, fire) — свой выстрел (маг-пушка)
function bCannon(g, x, y, face, fire = 0, o = {}){
  const rec = fire > 0 ? 1.1 * (fire < 0.07 ? fire / 0.07 : 1 - ease(Math.min(1, (fire - 0.07) / 0.8))) : 0, lit = bLit(face);
  bAt(g, x, y, face, () => {
    if(o.kit !== false){
      bShadow(g, () => bPoly(g, bRect(1.33, -1.62, 1.57, 1.72)), 0.25);
      bBeam(g, 1.45, -1.35, 1.45, 1.5, 0.06, B_WOOD_L, 260);
      bShape(g, bRect(1.33, -1.62, 1.57, -1.33), "#4a3f35", 261, 0.7, 0.3);
      bShape(g, bRect(1.36, 1.5, 1.54, 1.72), B_WOOD, 262, 0.7, 0);
      bShadow(g, () => { g.beginPath(); g.arc(-1.4, 1.5, 0.24, 0, 6.283); }, 0.5);
      g.beginPath(); g.arc(-1.4, 1.5, 0.24, 0, 6.283); g.fillStyle = B_WOOD; g.fill(); bInk(g, 0.7);
      g.beginPath(); g.arc(-1.4, 1.5, 0.17, 0, 6.283); g.fillStyle = "#4d6a72"; g.fill(); g.strokeStyle = B_IRON; g.lineWidth = bLw(g, 0.6); g.stroke();
      g.beginPath(); g.moveTo(-1.63, 1.5); g.quadraticCurveTo(-1.4, 1.18, -1.17, 1.5); g.stroke();
    }
    g.save(); g.translate(0, rec);
    bShadow(g, () => bPoly(g, [[-1.05, -0.75], [1.05, -0.75], [1.05, 0.75], [0.3, 2.8], [-0.3, 2.8], [-1.05, 0.75]]), 1.3);
    bShadow(g, () => bPoly(g, bRect(-0.22, GUN_MUZZLE, 0.22, -0.7)), 1.2, 0.22);
    bBeam(g, -1.08, 0, 1.08, 0, 0.2, B_WOOD_D, 251);
    for(const s of [-1, 1]) bWheelTop(g, s * 0.84, 0, s, 1.4, 252 + s);
    for(const s of [-1, 1]) bShape(g, [[s * 0.29, -0.82], [s * 0.43, -0.82], [s * 0.43, 0.45], [s * 0.24, 2.62], [s * 0.1, 2.58], [s * 0.29, 0.45]], B_WOOD, 254 + s, 0.9, 0.35);
    for(const [yy, hw] of [[-0.66, 0.29], [1.25, 0.21], [2.2, 0.14]]) bBeam(g, -hw, yy, hw, yy, 0.13, B_WOOD_D, 256);
    g.fillStyle = B_IRON; for(const s of [-1, 1]) for(const yy of [-0.55, 0.42, 1.5, 2.3]){ const xx = yy < 0.45 ? 0.36 : 0.36 - (yy - 0.45) * 0.0884; g.fillRect(s * xx - 0.08, yy - 0.03, 0.16, 0.06); }
    bShape(g, [[-0.25, 2.48], [0.25, 2.48], [0.2, 2.7], [-0.2, 2.7]], B_IRON, 257, 0.8, 0);
    g.beginPath(); g.arc(0, 2.82, 0.11, 0, 6.283); g.strokeStyle = B_INK; g.lineWidth = 0.045 + bLw(g, 0.8); g.stroke(); g.strokeStyle = B_STEEL; g.lineWidth = 0.045; g.stroke();
    bShape(g, bRect(-0.22, 0.75, 0.22, 1.62), B_WOOD_D, 258, 0.7, 0.3);                                         // подушка
    bShape(g, [[-0.17, 1.1], [0.17, 1.1], [0.15, 1.55], [-0.15, 1.55]], B_WOOD_L, 259, 0.7, 0.3);                // клин
    g.beginPath(); g.arc(0, 1.6, 0.05, 0, 6.283); g.fillStyle = B_WOOD_D; g.fill();
    bGunBarrel(g, lit, o.metal ?? B_BRONZE);
    g.restore();
    if(o.fx) o.fx(g, fire); else bGunFx(g, fire, GUN_MUZZLE, 1);
  });
}
// пороховой выстрел у дула (y0): ядро — короткий след вперёд, вспышка звездой, клубы дыма вперёд
function bGunFx(g, fire, y0, k = 1, seed = 259){
  if(fire <= 0) return;
  if(fire < 0.05){ const L = 10 * k, gr = g.createLinearGradient(0, y0, 0, y0 - L); gr.addColorStop(0, "rgba(60,60,64,0)"); gr.addColorStop(1, "rgba(50,50,54,.85)");
    g.beginPath(); g.moveTo(-0.06 * k, y0 - 0.4); g.lineTo(0.06 * k, y0 - 0.4); g.lineTo(0.1 * k, y0 - L); g.lineTo(-0.1 * k, y0 - L); g.closePath(); g.fillStyle = gr; g.fill(); }
  bSmoke(g, 0, y0 - (0.8 + 2.4 * fire) * k, fire, seed, 1.15 * k, {flash: false});
  if(fire < 0.14) bFlash(g, 0, y0 - 0.1, 1 - fire / 0.14, k);
}
// вспышка: лучи веером вперёд (−y; fan — ширина веера, 2π — во все стороны), белое ядро; col — цвет пламени
function bFlash(g, x, y, a, k = 1, col = "#ffb347", fan = 2.6){
  const N = 15; g.beginPath();
  for(let i = 0; i <= N * 2; i++){ const u = i / (N * 2), th = -Math.PI / 2 + (u - 0.5) * fan, r = (i % 2 ? 0.35 : 1.1 - Math.abs(u - 0.5) * (fan > 6 ? 0 : 1)) * k * (0.7 + 0.3 * a), px = x + Math.cos(th) * r, py = y + Math.sin(th) * r; i ? g.lineTo(px, py) : g.moveTo(px, py); }
  g.closePath();
  const gr = g.createRadialGradient(x, y, 0, x, y, 1.1 * k); gr.addColorStop(0, "#fffbe8"); gr.addColorStop(0.35, mix(col, "#ffffff", 0.4)); gr.addColorStop(1, col);
  g.save(); g.globalAlpha *= Math.min(1, a * 1.4); g.fillStyle = gr; g.fill(); g.restore();
}
// искры фитиля: лучики мерцают
function bSpark(g, x, y, s, t){
  g.beginPath(); for(let i = 0; i < 6; i++){ const a = i / 6 * 6.283 + t * 7, r = s * (0.6 + 0.4 * Math.sin(t * 23 + i * 2)); g.moveTo(x, y); g.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r); }
  g.strokeStyle = "#ffd36a"; g.lineWidth = bLw(g, 0.7); g.stroke(); g.beginPath(); g.arc(x, y, s * 0.3, 0, 6.283); g.fillStyle = "#fff4c8"; g.fill();
}
// мортира: короткий толстый ствол задран на 45° — сверху наклонный цилиндр: жерло овалом к небу (внутри — бомба,
// перед выстрелом искрит фитиль, o.fuse), пояса — дугами по верху ствола; цапфы под железными накладками на двух
// массивных станинах без колёс, по углам — кольца, чтобы тащить. fire — доля после выстрела: вспышка во все стороны,
// бомба уходит вверх — растёт, тень отстаёт; дым столбом — клубы растут с высотой, их тени отходят
const MORT = {y: 0.1, z: 0.65, e: Math.PI / 4};
function bMortar(g, x, y, face, fire = 0, o = {}){
  const sun = bSun(face), lit = bLit(face), IRON = "#55575b", ce = Math.cos(MORT.e), se = Math.sin(MORT.e);
  const rec = fire > 0 && fire < 0.3 ? 0.12 * Math.sin(fire / 0.3 * Math.PI) : 0;
  const st = t => [MORT.y - ce * t, MORT.z + se * t];   // точка оси ствола t м от цапф: [y, z]
  const ring = (t, r, n = 18, half = false) => { const [yy, zz] = st(t); return Array.from({length: n + 1}, (_, i) => { const f = (half ? Math.PI : 6.283) * i / n; return [Math.cos(f) * r * bUp(zz), yy + Math.sin(f) * r * se]; }); };
  const part = (t0, t1, r0, r1) => bHull(ring(t0, r0).concat(ring(t1, r1)));
  const [my, mz] = st(0.78);
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-0.9, -1.0, 0.9, 1.05)), 0.9);
    bCast(g, () => bSub(g, bShd([-0.5, 0.5].flatMap(xx => [[xx, ...st(-0.58)], [xx * 1.05, ...st(0.78)]]), sun)), 0.22);
    for(const s of [-1, 1]) bShape(g, bRect(s * 0.44, -0.95, s * 0.8, 1.0), B_WOOD, 261 + s, 1.0, 0.4);
    for(const yy of [-0.78, 0.82]) bBeam(g, -0.45, yy, 0.45, yy, 0.3, B_WOOD_D, 263);
    g.fillStyle = B_IRON; for(const s of [-1, 1]) for(const yy of [-0.62, 0.6]) g.fillRect(Math.min(s * 0.44, s * 0.8), yy - 0.035, 0.36, 0.07);
    for(const s of [-1, 1]) for(const yy of [-0.82, 0.86]){ g.beginPath(); g.arc(s * 0.88, yy, 0.08, 0, 6.283); g.strokeStyle = B_INK; g.lineWidth = 0.03 + bLw(g, 0.7); g.stroke(); g.strokeStyle = B_STEEL; g.lineWidth = 0.03; g.stroke(); }
    g.save(); g.translate(0, rec);
    for(const s of [-1, 1]) bShape(g, bRect(s * 0.38, MORT.y - 0.12, s * 0.6, MORT.y + 0.12), IRON, 265 + s, 0.6, 0);
    [part(-0.58, -0.22, 0.3, 0.3), part(-0.22, 0.62, 0.44, 0.44), part(0.6, 0.78, 0.52, 0.52)].forEach((P, i) => {
      bPoly(g, P); g.fillStyle = bCyl(g, 0.52, i === 1 ? IRON : mix(IRON, "#ffffff", 0.1), lit); g.fill(); bGrain(g, 0.3); bRough(g, P, true, 267 + i); bInk(g, 0.9); });
    for(const [t, r] of [[-0.22, 0.47], [0.05, 0.46], [0.35, 0.46]]){
      const A = ring(t, r, 14, true); g.beginPath(); A.forEach(([px, py], i) => i ? g.lineTo(px, py) : g.moveTo(px, py));
      g.strokeStyle = B_INK; g.lineWidth = bLw(g, 1.1); g.stroke(); g.strokeStyle = "rgba(220,224,228,.5)"; g.lineWidth = bLw(g, 0.4); g.stroke(); }
    for(const s of [-1, 1]) bShape(g, bRect(s * 0.42, MORT.y - 0.15, s * 0.82, MORT.y + 0.15), "#3e4044", 270 + s, 0.6, 0);
    const mk = bUp(mz);
    g.beginPath(); g.ellipse(0, my, 0.52 * mk, 0.52 * se, 0, 0, 6.283); g.fillStyle = mix(IRON, "#ffffff", 0.3); g.fill(); bInk(g, 0.9);
    g.beginPath(); g.ellipse(0, my, 0.36 * mk, 0.36 * se, 0, 0, 6.283); g.fillStyle = "#141416"; g.fill();
    if(!(fire > 0)){ g.beginPath(); g.ellipse(0, my + 0.03, 0.25, 0.19, 0, 0, 6.283); g.fillStyle = "#2c2d30"; g.fill(); g.beginPath(); g.arc(0.07, my - 0.02, 0.06, 0, 6.283); g.fillStyle = "rgba(220,224,228,.45)"; g.fill();
      if(o.fuse) bSpark(g, -0.04, my + 0.02, 0.17, o.t || 0); }
    g.restore();
    if(fire > 0) bMortarFx(g, fire, my, mz, sun);
  });
}
function bMortarFx(g, fire, my, mz, sun){
  const tau = fire * 1.5;
  // дым столбом: сначала тени клубов, потом клубы
  const P = Array.from({length: 6}, (_, i) => { const h = hash(271, i), z = mz + (1.5 + 9 * fire) * (0.3 + 0.7 * h), k = bUp(z);
    return [(hash(273, i) - 0.5) * 0.8 + 0.9 * fire * h, my - 0.3 - 0.8 * fire * h, z, (0.35 + 0.9 * fire) * (0.6 + 0.4 * hash(272, i)), k]; });
  g.beginPath(); for(const [px, py, z, r] of P) bDot(g, px + sun[0] * z, py + sun[1] * z, r); g.fillStyle = `rgba(28,22,12,${(0.12 * (1 - fire)).toFixed(3)})`; g.fill();
  if(tau < 0.6){
    const by = my - 6 * tau, bz = mz + 24 * tau - 4.9 * tau * tau, k = bUp(bz);
    g.save(); g.globalAlpha *= Math.min(1, 1 - (tau - 0.35) / 0.25);
    g.beginPath(); g.arc(sun[0] * bz, by + sun[1] * bz, 0.2, 0, 6.283); g.fillStyle = "rgba(28,22,12,.26)"; g.fill();
    g.beginPath(); g.arc(0, by, 0.2 * k, 0, 6.283); g.fillStyle = "#2c2d30"; g.fill(); bInk(g, 0.7);
    bSpark(g, 0.13 * k, by + 0.12 * k, 0.13 * k, tau * 10);
    g.restore();
  }
  for(const [px, py, z, r, k] of P){ g.beginPath(); g.arc(px, py, r * k, 0, 6.283); g.fillStyle = `rgba(232,228,218,${(0.72 * (1 - fire)).toFixed(2)})`; g.fill(); }
  if(fire < 0.12) bFlash(g, 0, my, 1 - fire / 0.12, 0.9, "#ffb347", 6.283);
}
// рибодекин: двуколка, на платформе — ряд тонких стволов, спереди — дощатый щит цвета стороны
function bRibauldequin(g, x, y, face, col = "#b0302a", fire = 0){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-1.3, -1.3, 1.3, 2.4)), 1.6);
    bBeam(g, -0.25, 1.0, -0.25, 2.4, 0.16, B_WOOD_D, 271); bBeam(g, 0.25, 1.0, 0.25, 2.4, 0.16, B_WOOD_D, 272);
    bShape(g, bRect(-1.1, -0.9, 1.1, 1.1), B_WOOD, 273, 1.0, 0.4);
    for(const s of [-1, 1]) bWheelTop(g, s * 1.25, 0.2, s, 1.3, 274 + s);
    for(let i = 0; i < 7; i++){ const xx = -0.9 + i * 0.3; bShape(g, bRect(xx - 0.1, -1.0, xx + 0.1, 0.8), "#56585c", 276 + i, 0.6, 0); }
    bShape(g, bRect(-1.2, -1.35, 1.2, -1.0), col, 284, 1.0, 0.3);
    if(fire > 0) bSmoke(g, 0, -1.8, fire, 285, 1.6);
  });
}
// маг-пушка: та же пушка, другой выстрел. u — доля цикла, 0 — выстрел. Выхлоп — как у пушки: вспышка пороха и дым;
// своё — светящийся снаряд с хвостом, кольцо волны, искры; к концу цикла — накачка: огоньки по спирали стягиваются к
// дулу, пояса ствола загораются, у дула растёт свечение
function bMagic(g, x, y, face, col = "#7a5ad0", u = 0, o = {}){
  u = frac(u);
  const fire = u < 0.45 ? u / 0.45 : 0, charge = u > 0.5 ? (u - 0.5) / 0.5 : 0;
  bCannon(g, x, y, face, fire, Object.assign({}, o, {fx: (g, f) => bMagicFx(g, f, charge, col)}));
}
function bMagicFx(g, fire, charge, col){
  const y0 = GUN_MUZZLE, lt = mix(col, "#ffffff", 0.55);
  if(charge > 0){
    g.save(); g.globalCompositeOperation = "lighter";
    bLines(g, GUN_RINGS.map(yy => [-0.21, yy, 0.21, yy]), bRgba(lt, 0.9 * charge), 0.9);
    for(let i = 0; i < 14; i++){ const h = hash(611, i), q = frac(charge * 2.2 + h), r = 0.15 + 2.2 * (1 - q), an = h * 6.283 + q * 3.5;
      g.beginPath(); g.arc(Math.cos(an) * r, y0 - 0.2 + Math.sin(an) * r * 0.8, 0.05 + 0.07 * q, 0, 6.283); g.fillStyle = bRgba(mix(col, "#ffffff", 0.25), Math.min(1, charge * 2) * (0.35 + 0.65 * q)); g.fill(); }
    const R = 0.3 + 0.9 * charge, gr = g.createRadialGradient(0, y0 - 0.15, 0, 0, y0 - 0.15, R); gr.addColorStop(0, bRgba(lt, 0.9 * charge)); gr.addColorStop(1, bRgba(col, 0));
    g.fillStyle = gr; g.beginPath(); g.arc(0, y0 - 0.15, R, 0, 6.283); g.fill();
    g.restore();
  }
  if(fire <= 0) return;
  bSmoke(g, 0, y0 - (0.8 + 2.4 * fire), fire, 619, 1.15, {flash: false});   // выхлоп — пороховой, как у пушки
  g.save(); g.globalCompositeOperation = "lighter";
  if(fire < 0.35){ const v = fire / 0.35; g.beginPath(); g.arc(0, y0 - 0.4, 0.4 + 3.4 * v, 0, 6.283); g.strokeStyle = bRgba(lt, 0.8 * (1 - v)); g.lineWidth = bLw(g, 2.2) * (1 - 0.5 * v); g.stroke(); }
  if(fire < 0.07){ const v = fire / 0.07, head = y0 - 1 - 16 * v, L = 6, gr = g.createLinearGradient(0, head + L, 0, head);
    gr.addColorStop(0, bRgba(col, 0)); gr.addColorStop(1, bRgba(lt, 0.95));
    g.beginPath(); g.moveTo(-0.1, head + L); g.lineTo(0.1, head + L); g.lineTo(0.22, head); g.lineTo(-0.22, head); g.closePath(); g.fillStyle = gr; g.fill();
    g.beginPath(); g.arc(0, head, 0.55, 0, 6.283); g.fillStyle = bRgba(col, 0.45); g.fill(); g.beginPath(); g.arc(0, head, 0.3, 0, 6.283); g.fillStyle = "rgba(255,255,255,.95)"; g.fill(); }
  for(let i = 0; i < 9; i++){ const h = hash(613, i), an = -Math.PI / 2 + (h - 0.5) * 2.6, d = 0.4 + 3 * fire * (0.6 + 0.4 * hash(614, i)), s = 0.18 * (1 - fire);
    const px = Math.cos(an) * d, py = y0 - 0.3 + Math.sin(an) * d - 0.6 * fire;
    g.beginPath(); g.moveTo(px, py - s); g.quadraticCurveTo(px, py, px + s, py); g.quadraticCurveTo(px, py, px, py + s); g.quadraticCurveTo(px, py, px - s, py); g.quadraticCurveTo(px, py, px, py - s);
    g.fillStyle = bRgba(lt, 1 - fire * 0.8); g.fill(); }
  g.restore();
  if(fire < 0.14) bFlash(g, 0, y0 - 0.1, 1 - fire / 0.14, 1);
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
// опущен вперёд), люк лестницы; колёса по углам. Площадка на высоте z (под стену, 10 м) — сверху она крупнее (bUp)
function bSiegeTower(g, x, y, face, down = 0, col = "#b0302a", t = 0, z = 10){
  bAt(g, x, y, face, () => {
    bShadow(g, () => bPoly(g, bRect(-2.7, -2.9, 2.7, 2.9)), z + 3);
    for(const [sx, sy] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) bWheel(g, sx * 2.85, sy * 2.2, 0, 1.3, 321 + sx + sy * 3);
    const k = bUp(z); g.scale(k, k);
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
// приставленная лестница: пята на земле (fx, fy), верх лёг на стену (tx, ty) на высоте z. Сверху короткая и частая —
// перекладины через 0,4 м настоящей длины; к верху шире (ближе к камере); тень — от пяты до тени верха
function bLadderUp(g, fx, fy, tx, ty, z, seed = 345){
  const L = Math.hypot(tx - fx, ty - fy) || 1e-6, nx = -(ty - fy) / L, ny = (tx - fx) / L, n = Math.max(4, Math.round(Math.hypot(L, z) / 0.4));
  const P = (u, v) => { const k = bUp(z * u); return [fx + (tx - fx) * u + nx * v * k, fy + (ty - fy) * u + ny * v * k]; };
  bCast(g, () => bSub(g, bSeg([fx, fy], [tx + B_SUN[0] * z, ty + B_SUN[1] * z], 0.62)), 0.2);
  const R = []; for(let i = 1; i < n; i++) R.push([...P(i / n, -0.27), ...P(i / n, 0.27)]);
  bLines(g, R, B_INK, 1.0); bLines(g, R, B_WOOD_L, 0.45);
  for(const s of [-1, 1]) bShape(g, [P(0, s * 0.27 - 0.05), P(1, s * 0.27 - 0.05), P(1, s * 0.27 + 0.05), P(0, s * 0.27 + 0.05)], B_WOOD, seed + s, 0.7, 0);
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
// дым выстрела: клубы растут и тают (p — доля 0…1 после выстрела); o.rgb — цвет дыма, o.flash === false — без вспышки
function bSmoke(g, x, y, p, seed, k = 1, o = {}){
  const rgb = o.rgb ?? "232,228,218";
  for(let i = 0; i < 6; i++){ const a = hash(seed, i) * 6.283, d = (0.3 + 1.4 * p) * k * hash(seed, i + 5), r = (0.4 + 0.9 * p) * k * (0.6 + 0.4 * hash(seed, i + 9));
    g.beginPath(); g.arc(x + Math.cos(a) * d, y - 0.8 * p * k + Math.sin(a) * d, r, 0, 6.283); g.fillStyle = `rgba(${rgb},${(0.7 * (1 - p)).toFixed(2)})`; g.fill(); }
  if(o.flash !== false && p < 0.15){ g.beginPath(); g.arc(x, y, 0.5 * k, 0, 6.283); g.fillStyle = `rgba(255,200,90,${(1 - p / 0.15).toFixed(2)})`; g.fill(); }
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
