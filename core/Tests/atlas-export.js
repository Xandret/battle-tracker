// ═══════════ atlas-export.js — рисунок бойцов (В18) в атласы для Unity (И2, смотрелка боя) ═══════════
// Запускается на странице пробы core/Tests/men-flat.html (там рисовальщики polygon-men.js и polygon-men-flat.js):
// game/Tools/art-server.mjs отдаёт страницу и принимает файлы. exportAtlases(["men", "horses", "dead"]) рисует каждую
// часть теми же функциями, что проба, раскладывает по атласам и шлёт PNG и JSON в game/Assets/Resources/Art.
// Природа (кроны, кусты, камни) — на странице полигона (core/polygon/polygon.html): exportAtlases(["nature"]).
// Цвет в атласе и в Unity:
//   - маркерный пурпурный #ff00ff (с его светлыми и тёмными оттенками и смесями) Unity перекрашивает в цвет вершины —
//     шейдер Men: цвет стороны, у тела в своей одежде — цвет одежды, у щита — цвет поля;
//   - белые части (волосы, шапки, капюшоны, тюрбаны, предплечья) Unity умножает на цвет вершины (режим «нейтральная»).
// Так один атлас годится для любой стороны, стиля и одежды.
// JSON атласа: { ppm, w, h, sprites: { имя: [x, y, w, h — пиксели в атласе, x0, y0, x1, y1 — рамка в метрах] } };
// метры — оси бойца пробы: вперёд — вверх (−y), начало — середина плеч (у коня — седло).
(function(){
  const KEY = "#ff00ff", WHITE = "#ffffff";
  const C2 = DEVICE;                       // второй цвет герба: белый, золотой, чёрный
  const PAINTS = ["plain", "halves", "quarters", "stripe", "cross", "chevron"];
  const SH_PAINTS = PAINTS.concat(["boss", "wood"]);
  const SHAPES = ["round", "oval", "heater", "kite", "adarga"];
  // раскладки тела (fLayout): имя части → комплект для рисовальщика. Ключ — тот же, что Kit.LayoutKey в Unity (Kits.cs)
  function layouts(){
    const L = [["cloth", {armour: "cloth"}], ["leather", {armour: "leather"}], ["kaftan", {style: "east", armour: "lamellar"}], ["lacing", {armour: "oyoroi"}]];
    for(const a of ["mail", "plate"]) for(const p of PAINTS) C2.forEach((c2, i) => L.push([`tabard/${a}/${p}/${i}`, {armour: a, tabard: p, c2}]));
    for(const a of ["mail", "scale"]) L.push([`cloak/${a}`, {style: "north", armour: a}]);
    C2.forEach((c2, i) => L.push([`dou/${i}`, {armour: "dou", col: c2}]));   // у доу мон — второго цвета: пурпурный там — одежда
    return L.map(([key, k]) => [key, Object.assign({style: "west", cloth: KEY, col: KEY, leather: LEATHER[0], tabard: "plain", c2: C2[0]}, k)]);
  }

  // ── список частей: [атлас, имя, рамка в метрах, рисование] ──
  function parts(){
    const P = [];
    const add = (atlas, name, box, paint, res = 1, noShadow = false) => P.push({atlas, name, box, paint: noShadow ? g => withoutShadow(paint, g) : paint, res});   // res — доля разрешения атласа
    if(typeof fBody === "function"){
      // ── бойцы ──
      for(const [key, k] of layouts()) add("men", "body/" + key, [-0.34, -0.18, 0.34, 0.15], g => fBody(g, k));
      const HB = [-0.2, -0.27, 0.2, 0.26];
      for(const h of ["hair", "cap", "hood", "turban"]) add("men", "head/" + h, HB, g => fHead(g, {helm: h, helmCol: WHITE, col: KEY}));
      F_HAIRS.fareast.forEach((c, i) => add("men", "head/hachimaki/" + i, HB, g => fHead(g, {helm: "hachimaki", helmCol: c, col: KEY})));
      for(const h of ["kettle", "capSteel", "nasal", "shishak", "pointed", "great", "bascinet", "sallet", "barbute", "morion"]) add("men", "head/" + h, HB, g => fHead(g, {helm: h, col: KEY}));
      add("men", "head/great/crest", HB, g => fHead(g, {helm: "great", crest: KEY, col: KEY}));
      for(const h of ["kabuto", "jingasa"]) add("men", "head/" + h, HB, g => fHead(g, {helm: h, helmCol: F_LACQ, col: KEY}));
      // щиты: в руке — ребром (shtop), у павших — плашмя (shield); поле — пурпурное (цвет поля), второй цвет — герб
      for(const shape of SHAPES) for(const p of SH_PAINTS) C2.forEach((c2, i) => {
        const sh = {shape, paint: p, c1: KEY, c2};
        add("men", `shtop/${shape}/${p}/${i}`, [-0.26, -0.11, 0.26, 0.08], g => fShieldTop(g, sh));
        add("men", `shield/${shape}/${p}/${i}`, [-0.26, -0.31, 0.26, 0.37], g => fShield(g, sh));
      });
      const bk = {shape: "buckler", paint: "steel", c1: KEY, c2: C2[0]};
      add("men", "shtop/buckler/steel", [-0.26, -0.11, 0.26, 0.08], g => fShieldTop(g, bk));
      add("men", "shield/buckler/steel", [-0.15, -0.15, 0.15, 0.15], g => fShield(g, bk));
      // оружие; длинным древкам хватает половины разрешения
      for(const w of Object.keys(F_WBOX))
        add("men", "weapon/" + w, F_WBOX[w].map((v, i) => v + (i < 2 ? -0.03 : 0.03)), g => fWeapon(g, w, KEY), ["pike", "lance", "spear", "fork", "halberd", "naginata", "daneaxe", "javelin"].includes(w) ? 0.5 : 1);
      for(const kind of ["bow", "recurve", "yumi"]) for(let st = 0; st <= 4; st++) add("men", `bow/${kind}/${st}`, [-0.5, -1.14, 0.45, 0.22], g => fBow(g, st, kind));
      for(let st = 0; st <= 3; st++) add("men", "xbow/" + st, [-0.3, -0.68, 0.38, 0.08], g => fXbow(g, st));
      // предплечье — белое (Unity умножает на цвет рукава), от локтя (0, 0) к кисти (0, −FA_LEN); кисти
      add("men", "arm", [-0.07, -FA_LEN - 0.07, 0.07, 0.07], g => fStick(g, 0, 0, 0, -FA_LEN, 0.07, WHITE));
      for(const kind of ["skin", "glove", "plate"]) add("men", "hand/" + kind, [-0.05, -0.05, 0.05, 0.05], g => fHand(g, kind));
      // за спиной
      const BB = [-0.3, -0.05, 0.36, 0.36];
      add("men", "back/cape", BB, g => fBack(g, {back: "cape", backCol: shade(KEY, -0.3)}));
      for(const b of ["roll", "bag"]) add("men", "back/" + b, BB, g => fBack(g, {back: b}));
      add("men", "back/quiver", BB, g => fBack(g, {back: "quiver", style: "west"}));
      add("men", "back/quiver/lacq", BB, g => fBack(g, {back: "quiver", style: "fareast"}));
      C2.forEach((c2, i) => { for(const b of ["pavise", "sashimono"]) add("men", `back/${b}/${i}`, BB, g => fBack(g, {back: b, backCol: KEY, c2})); });
      add("men", "boot", [-0.06, -0.09, 0.06, 0.09], fBoot);
      // ноги всадника: штаны — пурпурные (цвет одежды); латы и о-ёрой — свои
      const RB = [-0.38, -0.16, 0.38, 0.14];
      add("men", "rider/n", RB, g => fRiderLegs(g, {armour: "cloth", cloth: KEY}));
      for(const a of ["plate", "oyoroi"]) add("men", "rider/" + a, RB, g => fRiderLegs(g, {armour: a, cloth: KEY}));
      // служебные: белый круг и мягкое пятно (кровь, тени — Unity красит цветом вершины), белый квадрат (стрелы, поводья)
      const util = atlas => {
        add(atlas, "util/disc", [-0.5, -0.5, 0.5, 0.5], g => { g.beginPath(); g.arc(0, 0, 0.48, 0, 6.283); g.fillStyle = WHITE; g.fill(); });
        add(atlas, "util/soft", [-0.5, -0.5, 0.5, 0.5], g => { const gr = g.createRadialGradient(0, 0, 0, 0, 0, 0.5); gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.6, "rgba(255,255,255,.55)"); gr.addColorStop(1, "rgba(255,255,255,0)"); g.fillStyle = gr; g.fillRect(-0.5, -0.5, 1, 1); });
      };
      util("men"); util("horses");
      add("men", "util/px", [-0.05, -0.05, 0.05, 0.05], g => { g.fillStyle = WHITE; g.fillRect(-0.05, -0.05, 0.1, 0.1); });
      add("men", "util/spark", [-0.16, -0.16, 0.16, 0.16], paintSpark);   // вспышка удара (В13)
      // ── кони: по масти — ноги (и в белом «чулке»), хвост, туловище, голова по отметине и броне; павший конь ──
      const HH = [-0.21, -0.83, 0.21, 0.14];
      COATS.forEach((coat, ci) => {
        const base = {coat, col: KEY, c2: C2[0], bard: "none", mark: "none", tabard: "plain", socks: 0};
        const with_ = o => Object.assign({}, base, o);
        add("horses", `hleg/${ci}`, [-0.07, -0.18, 0.07, 0.18], g => fHLeg(g, base, false));
        add("horses", `hleg/${ci}/s`, [-0.07, -0.18, 0.07, 0.18], g => fHLeg(g, base, true));
        add("horses", `htail/${ci}`, [-0.1, -0.02, 0.1, 0.59], g => fHTail(g, base));
        add("horses", `hbody/${ci}`, [-0.28, -0.72, 0.28, 0.98], g => fHBody(g, base));
        for(const mark of ["none", "star", "blaze", "snip"]) add("horses", `hhead/${ci}/${mark}`, HH, g => fHHead(g, with_({mark})));
        C2.forEach((c2, i) => add("horses", `hhead/${ci}/full/${i}`, HH, g => fHHead(g, with_({bard: "full", c2}))));
        add("horses", `hhead/${ci}/lamellar`, HH, g => fHHead(g, with_({bard: "lamellar"})));
        const dh = [["none", {}], ["cloth", {bard: "cloth"}], ["lamellar", {bard: "lamellar"}]].concat(C2.map((c2, i) => ["full/" + i, {bard: "full", c2}]));
        for(const [bkey, o] of dh) add("dead", `deadhorse/${ci}/${bkey}`, [-0.52, -1.68, 0.98, 1.58], g => fDeadHorse(g, with_(o), false), 0.75);
      });
      // попона и седло поверх туловища
      const HC = [-0.33, -0.8, 0.33, 1.1], hk = o => Object.assign({coat: COATS[0], col: KEY, c2: C2[0], bard: "none", tabard: "plain"}, o);
      add("horses", "hcover/none", HC, g => fHCover(g, hk({})));
      add("horses", "hcover/lamellar", HC, g => fHCover(g, hk({bard: "lamellar"})));
      C2.forEach((c2, i) => {
        add("horses", "hcover/cloth/" + i, HC, g => fHCover(g, hk({bard: "cloth", c2})));
        for(const p of PAINTS) add("horses", `hcover/full/${p}/${i}`, HC, g => fHCover(g, hk({bard: "full", tabard: p, c2})));
      });
      // ── павшие и раненые: тело без головы (её Unity кладёт шлемом), оружие и щит Unity кладёт рядом ──
      for(const [key, k] of layouts()){
        for(let v = 0; v < 2; v++) add("dead", `corpse/${key}/${v}`, [-0.75, -0.5, 0.75, 0.98], g => fLying(g, k, corpsePose(v)));
        add("dead", `crawl/${key}`, [-0.5, -1.06, 0.45, 0.95], g => fLying(g, k, CRAWL_POSE));
      }
    }
    // ── природа (страница полигона): кроны деревьев (4 палитры × 6 форм), кусты, камни — радиус 1, Unity растягивает ──
    if(typeof TREES !== "undefined"){
      TREES.forEach((pal, pi) => { for(let s = 0; s < 6; s++) add("nature", `tree/${pi}/${s}`, [-1.2, -1.2, 1.2, 1.2], g => crownUnit(g, s * 977 + pi * 31, pal, 7)); });
      for(let s = 0; s < 6; s++) add("nature", "bush/" + s, [-1.2, -1.2, 1.2, 1.2], g => crownUnit(g, s * 571 + 5, BUSH, 5));
      for(let s = 0; s < 6; s++) add("nature", "boulder/" + s, [-1.25, -1.25, 1.25, 1.25], g => boulder(g, 0, 0, 1, s * 313 + 9, 0.06));
    }
    // ── постройки (страница build-flat.html, В19): башни, вышка, ворота, обломки, мягкая тень; Unity ставит их по клеткам
    // карты (FortMap.cs). Каменная башня — радиусом 5 м (Unity растягивает под свой), ворота — проём 4,4 м сквозь стену 5 м
    // (частокол — 1,2 м), проход вдоль y, наружу — вверх (−y)
    if(typeof bWall === "function"){
      for(const roof of ["slate", "tile", "open"]) add("build", "tower/stone/" + roof, [-5.7, -5.7, 5.7, 5.7], g => bTowerRound(g, 0, 0, 5, {seed: 7, roof: roof === "open" ? "open" : null, roofCol: B_ROOF[roof]}), 1, true);
      add("build", "tower/wood", [-2.7, -2.7, 2.7, 2.7], g => bWoodTower(g, 0, 0, 4.6, {seed: 81}), 1, true);
      for(const st of ["closed", "open", "broken"]){
        add("build", "gate/stone/" + st, [-2.6, -2.9, 2.6, 2.9], g => bGate(g, 0, 0, 0, 4.4, 5, st, 11, -1.8));
        add("build", "gate/wood/" + st, [-2.6, -2.5, 2.6, 2.5], g => bGate(g, 0, 0, 0, 4.4, 1.2, st, 13));
      }
      for(let i = 0; i < 3; i++) add("build", "rubble/" + i, [-4.2, -4.2, 4.2, 4.2], g => bRubble(g, 0, 0, 3.2, 41 + i * 7));
      add("build", "shadow/disc", [-1.6, -1.6, 1.6, 1.6], g => { const gr = g.createRadialGradient(0, 0, 0.6, 0, 0, 1.6); gr.addColorStop(0, "rgba(0,0,0,1)"); gr.addColorStop(1, "rgba(0,0,0,0)"); g.fillStyle = gr; g.fillRect(-1.6, -1.6, 3.2, 3.2); });
    }
    return P;
  }
  // ленты построек (повтор вдоль u): стена 5 м — 4 зубца (5,8 м) на 60 px/м, наружу — вверх; частокол — 12 брёвен (5,76 м)
  async function exportTiles(){
    const tile = (len, ppm, y0, y1, paint) => { const c = document.createElement("canvas"); c.width = Math.round(len * ppm); c.height = Math.round((y1 - y0) * ppm);
      const g = c.getContext("2d"); g.scale(ppm, ppm); g.translate(0, -y0); g.lineJoin = "round"; g.lineCap = "round"; paint(g); return c; };
    const out = [];
    for(const [name, c] of [
      ["wall_tile", tile(5.8, 60, -2.85, 2.85, g => bWall(g, [[-11.6, 0], [17.4, 0]], {outer: -1, seed: 9, flat: true, slab: 1.16}))],
      ["palisade_tile", tile(5.76, 62.5, -1.0, 1.0, g => bPalisade(g, [[-11.52, 0], [17.28, 0]], {seed: 71}))],
      // фактура скатов — серая (Unity умножает на цвет ската), повтор по обеим осям: u — вдоль свеса, v — вверх по скату
      ...["tile", "slate", "shingle", "thatch"].map(k => ["roof_" + k + "_tile", roofTile(k)]),
    ]){
      const png = await new Promise(r => c.toBlob(r, "image/png"));
      await fetch(`/save?file=${name}.png`, {method: "POST", body: png});
      out.push(`${name}: ${c.width}×${c.height}`);
    }
    return out;
  }
  // кусок фактуры кровли: ряды вдоль свеса (u) с шагом по скату (v), у черепицы и тёса — стыки вразбежку; солома —
  // штрихи вдоль ската (с копиями через край — для повтора); белый фон с зерном, линии тёмные
  function roofTile(kind){
    const ppm = 64, [Lu, Lv] = {tile: [2.88, 2.7], slate: [3.0, 3.0], shingle: [2.8, 3.3], thatch: [3.0, 3.0]}[kind];
    const c = document.createElement("canvas"); c.width = Math.round(Lu * ppm); c.height = Math.round(Lv * ppm);
    const g = c.getContext("2d"); g.scale(ppm, ppm); g.lineCap = "round";
    g.fillStyle = "#ffffff"; g.fillRect(0, 0, Lu, Lv); g.beginPath(); g.rect(0, 0, Lu, Lv); bGrain(g, 0.5);
    const S = [];
    if(kind === "thatch"){
      for(let i = 0; i < 260; i++){ const x = hash(i, 501) * Lu, y = hash(i, 502) * Lv; for(const dx of [-Lu, 0, Lu]) for(const dy of [-Lv, 0, Lv]) S.push([x + dx, y + dy, x + dx + 0.06, y + dy + 0.35]); }
      bLines(g, S, "rgba(110,84,32,.55)", 0.5);
    } else {
      const step = {tile: 0.45, slate: 0.5, shingle: 0.55}[kind];
      for(let y = step; y <= Lv + 1e-6; y += step) S.push([0, y, Lu, y], [0, y - Lv, Lu, y - Lv]);
      bLines(g, S, kind === "slate" ? "rgba(30,36,44,.55)" : "rgba(60,30,16,.5)", 0.5);
      if(kind !== "slate"){ const T = [], t2 = kind === "tile" ? 0.32 : 0.7; let r = 0;
        for(let y = 0; y < Lv - 1e-6; y += step, r++) for(let x = (r % 2) * t2 / 2; x < Lu + t2; x += t2) T.push([x, y, x, y + step], [x - Lu, y, x - Lu, y + step]);
        bLines(g, T, "rgba(60,30,16,.38)", 0.42); }
    }
    return c;
  }
  // крона радиуса 1 м: та же функция полигона, крупно (детали — как вблизи)
  function crownUnit(g, s, pal, nb){ crown(g, 0, 0, 1, s, pal, 60, 0.07, nb); }

  // тень постройки Unity кладёт сама (мягкий круг) — в частях её не рисуем
  function withoutShadow(paint, g){ const keep = window.bShadow; window.bShadow = () => {}; try { paint(g); } finally { window.bShadow = keep; } }
  const PPM = {men: 128, horses: 96, dead: 72, nature: 64, build: 48};
  const PAD = 4;
  // рисуем часть в свой холст, раскладываем полками по атласу ширины 2048
  async function build(atlas, list){
    const items = list.map(p => {
      const [x0, y0, x1, y1] = p.box, ppm = PPM[atlas] * p.res;
      const w = Math.ceil((x1 - x0) * ppm), h = Math.ceil((y1 - y0) * ppm);
      const c = document.createElement("canvas"); c.width = w; c.height = h;
      const g = c.getContext("2d"); g.scale(ppm, ppm); g.translate(-x0, -y0); g.lineJoin = "round"; g.lineCap = "round";
      p.paint(g);
      return {p, c, w, h};
    }).sort((a, b) => b.h - a.h || b.w - a.w);
    const W = 2048; let x = PAD, y = PAD, shelf = 0;
    for(const it of items){
      if(x + it.w + PAD > W){ x = PAD; y += shelf + PAD; shelf = 0; }
      it.x = x; it.y = y; x += it.w + PAD; shelf = Math.max(shelf, it.h);
    }
    let H = 1; while(H < y + shelf + PAD) H *= 2;
    if(H > 2048) throw new Error(`атлас ${atlas} выше 2048 (${H}) — Unity его ужмёт`);
    const A = document.createElement("canvas"); A.width = W; A.height = H;
    const ga = A.getContext("2d");
    const sprites = {};
    for(const it of items){
      ga.drawImage(it.c, it.x, it.y);
      const [x0, y0, x1, y1] = it.p.box;
      sprites[it.p.name] = [it.x, it.y, it.w, it.h, x0, y0, x1, y1];
    }
    const png = await new Promise(r => A.toBlob(r, "image/png"));
    return {png, json: JSON.stringify({ppm: PPM[atlas], w: W, h: H, sprites}), count: items.length, W, H};
  }

  window.exportAtlases = async function(only){
    const all = parts(), out = [];
    for(const atlas of ["men", "horses", "dead", "nature", "build"]){
      if(only && !only.includes(atlas)) continue;
      const list = all.filter(p => p.atlas === atlas);
      if(!list.length){ out.push(`${atlas}: нет рисовальщиков на этой странице`); continue; }
      const r = await build(atlas, list);
      await fetch(`/save?file=${atlas}.png`, {method: "POST", body: r.png});
      await fetch(`/save?file=${atlas}.json`, {method: "POST", body: r.json});
      out.push(`${atlas}: ${r.count} частей, ${r.W}×${r.H}`);
      if(atlas === "build") out.push(...await exportTiles());
    }
    return out;
  };
})();
