// ═══════════ atlas-export.js — рисунок полигона в атласы для Unity (И2, смотрелка боя) ═══════════
// Запускается на странице полигона (там уже есть все функции рисования): game/Tools/art-server.mjs отдаёт страницу и
// принимает файлы. exportAtlases() рисует каждую часть бойца, коня, павшего и природы теми же функциями, что полигон,
// раскладывает по атласам и шлёт PNG и JSON в game/Assets/Resources/Art.
// Цвет стороны в рисунке — пурпурный #ff00ff (вместе с его светлыми и тёмными оттенками): Unity перекрашивает его в цвет
// отряда шейдером (Men.shader), поэтому один атлас годится для любой стороны и любого отряда.
// JSON атласа: { ppm, w, h, sprites: { имя: [x, y, w, h — пиксели в атласе, x0, y0, x1, y1 — рамка в метрах] } };
// метры — оси бойца полигона: вперёд — вверх (−y), начало — середина плеч (у коня — середина спины).
(function(){
  const KEY = "#ff00ff";
  const C2 = DEVICE;                       // второй цвет герба: белый, золотой, чёрный
  const ARMOURS = ["cloth", "mail", "leather0", "leather1", "leather2", "plate"];
  const CLOTH_KEYS = ["f"].concat(CLOTHS.map((_, i) => "c" + i));   // f — цвет стороны, cN — своя одежда
  const clothOf = key => key === "f" ? KEY : CLOTHS[+key.slice(1)];
  const armourOf = a => a.startsWith("leather") ? ["leather", LEATHER[+a.slice(7)]] : [a, LEATHER[0]];

  // ── список частей: [атлас, имя, рамка в метрах, рисование] ──
  function parts(){
    const P = [];
    const add = (atlas, name, box, paint, res = 1) => P.push({atlas, name, box, paint, res});   // res — доля разрешения атласа
    // поклажа за спиной
    add("men", "back/cape", [-0.3, -0.02, 0.3, 0.42], g => backItem(g, {back: "cape", backCol: shade(KEY, -0.3)}));
    add("men", "back/roll", [-0.26, 0.05, 0.26, 0.28], g => backItem(g, {back: "roll"}));
    add("men", "back/bag", [-0.15, 0.03, 0.15, 0.3], g => backItem(g, {back: "bag"}));
    add("men", "back/quiver", [-0.04, -0.06, 0.3, 0.38], g => backItem(g, {back: "quiver"}));
    C2.forEach((c2, i) => add("men", "back/pavise/" + i, [-0.31, 0.09, 0.31, 0.33], g => backItem(g, {back: "pavise", backCol: KEY, c2})));
    // плечи с бронёй (без головы и поклажи)
    for(const ck of CLOTH_KEYS) for(const a of ARMOURS){
      const [armour, leather] = armourOf(a);
      add("men", `body/${ck}/${a}`, [-0.31, -0.16, 0.31, 0.2], g => paintBody(g, {back: "none", helm: "none", cloth: clothOf(ck), armour, leather}));
    }
    // головы и шлемы
    HAIRS.forEach((c, i) => add("men", "head/hair/" + i, [-0.15, -0.15, 0.15, 0.15], g => paintHead(g, {helm: "hair", helmCol: c})));
    for(const h of ["cap", "hood"]) for(const ck of ["f"].concat(CLOTHS.map((_, i) => "c" + i)))
      add("men", `head/${h}/${ck}`, [-0.16, -0.16, 0.16, 0.29], g => paintHead(g, {helm: h, helmCol: ck === "f" ? shade(KEY, -0.25) : clothOf(ck)}));
    for(const h of ["kettle", "capSteel", "nasal", "great", "bascinet", "morion"])
      add("men", "head/" + h, [-0.2, -0.24, 0.2, 0.24], g => paintHead(g, {helm: h}));
    add("men", "head/great/crest", [-0.2, -0.24, 0.2, 0.24], g => paintHead(g, {helm: "great", crest: KEY}));
    // сюрко поверх тела (В15): поле — сторона, герб — второй цвет; Unity кладёт между телом и головой
    for(const paint of ["plain", "halves", "quarters", "stripe", "cross", "chevron"])
      C2.forEach((c2, i) => add("men", `tabard/${paint}/${i}`, [-0.22, -0.15, 0.22, 0.2], g => paintTabard(g, paint, KEY, c2)));
    // руки (В15): предплечье — от локтя (0, 0) к кисти (0, −FA_LEN), Unity растягивает по длине; рукав — по доспеху
    for(const key of CLOTH_KEYS.concat(["mail", "plate", "leather0", "leather1", "leather2"])){
      const k = key === "mail" || key === "plate" ? {armour: key} : key.startsWith("leather") ? {armour: "leather", leather: LEATHER[+key.slice(7)]} : {armour: "cloth", cloth: clothOf(key)};
      add("men", "arm/" + key, [-0.06, -FA_LEN - 0.06, 0.06, 0.06], g => paintForearm(g, k));
    }
    for(const kind of ["skin", "glove", "plate"]) add("men", "hand/" + kind, [-0.05, -0.055, 0.05, 0.055], g => paintHand(g, kind));
    // стоящий боец (В17): цилиндры, шары, срезы корпуса — белые (Unity умножает на цвет ткани, кожи) или своего цвета
    // (кольчуга, латы, пояс); срезы сюрко — поле стороны и герб; лица и забрала, изнанки щитов, плащ и павеза стоймя
    for(const kind of ["cloth", "leather", "mail", "plate"]) add("men", "cyl/" + kind, [-CYL_R - 0.015, -CYL_L, CYL_R + 0.015, 0], g => paintCyl(g, kind));
    for(const kind of ["cloth", "mail", "steel"]) add("men", "ball/" + kind, [-BALL_R - 0.015, -BALL_R - 0.015, BALL_R + 0.015, BALL_R + 0.015], g => paintBall(g, kind));
    const SLICE_BOX = [-SLICE_RX - 0.015, -SLICE_RY - 0.03, SLICE_RX + 0.015, SLICE_RY + 0.015];
    for(const kind of ["cloth", "brig", "mail", "plate", "belt"]) add("men", "slice/" + kind, SLICE_BOX, g => paintSlice(g, kind));
    for(const paint of ["plain", "halves", "quarters", "stripe", "cross", "chevron"]) for(const band of ["u", "l"])
      C2.forEach((c2, i) => add("men", `slice/tab/${paint}/${band}/${i}`, SLICE_BOX, g => paintSlice(g, "tab", KEY, paint, band, c2)));
    for(const kind of ["open", "great", "bascinet"]) add("men", "face/" + kind, [-0.09, -0.1, 0.09, 0.1], g => paintFace(g, kind));
    for(const shape of ["round", "oval", "heater", "buckler"]) add("men", "shieldback/" + shape, [-0.25, -0.3, 0.25, 0.31], g => paintShieldBack(g, shape));
    add("men", "capev", [-0.26, -0.44, 0.26, 0.47], g => paintCapeV(g, KEY));
    add("men", "legs2", [-0.16, -0.45, 0.16, 0.45], g => paintLegs2(g));
    // конь стоймя (В17): срезы туловища (масть — умножением), попона с гербом, сёдла с чепраком
    const HSL_BOX = [-HSL_RX - 0.03, -HSL_RY - 0.03, HSL_RX + 0.03, HSL_RY + 0.03];
    add("men", "hslice/coat", HSL_BOX, g => paintHSlice(g, "coat"));
    C2.forEach((c2, i) => add("men", "hslice/bard/" + i, HSL_BOX, g => paintHSlice(g, "bard", KEY, c2)));
    for(const kind of ["none", "cloth"]) add("men", "saddle/" + kind, [-0.3, -0.33, 0.3, 0.35], g => paintSaddle(g, kind, KEY));
    C2.forEach((c2, i) => add("men", "pavisev/" + i, [-0.3, -0.47, 0.3, 0.47], g => paintPaviseV(g, KEY, c2)));
    // щиты: лицом, цвет поля — сторона, второй цвет — герб
    for(const shape of ["round", "oval", "heater"])
      for(const paint of ["plain", "halves", "quarters", "stripe", "cross", "chevron", "boss", "wood"])
        C2.forEach((c2, i) => add("men", `shield/${shape}/${paint}/${i}`, [-0.26, -0.31, 0.26, 0.31], g => paintShield(g, {shape, paint, c1: KEY, c2})));
    add("men", "shield/buckler/steel/0", [-0.15, -0.15, 0.15, 0.15], g => paintShield(g, {shape: "buckler", paint: "steel", c1: KEY, c2: C2[0]}));
    // оружие, лук и арбалет в разных состояниях, сапог
    // древки длинные и тонкие — им хватает половины разрешения, иначе одна полка атласа занимает полвысоты
    for(const w of Object.keys(WBOX)) add("men", "weapon/" + w, WBOX[w], g => paintWeapon(g, w, KEY), ["pike", "lance", "spear", "fork"].includes(w) ? 0.5 : 1);
    for(let st = 0; st <= 4; st++) add("men", "bow/" + st, [-0.36, -1.12, 0.36, 0.2], g => paintBow(g, st));
    for(let st = 0; st <= 3; st++) add("men", "xbow/" + st, [-0.3, -0.68, 0.38, 0.08], g => paintXbow(g, st));   // 2, 3 — взвод через стремя (В13)
    add("men", "boot", [-0.06, -0.08, 0.06, 0.08], g => ell(g, 0, 0, 0.045, 0.07, "#3a2c20", 0, false));
    // служебные: белый круг и мягкое пятно (кровь, тени — Unity красит цветом вершины), белый квадрат (стрелы)
    add("men", "util/disc", [-0.5, -0.5, 0.5, 0.5], g => { g.beginPath(); g.arc(0, 0, 0.48, 0, 6.283); g.fillStyle = "#fff"; g.fill(); });
    add("men", "util/soft", [-0.5, -0.5, 0.5, 0.5], g => { const gr = g.createRadialGradient(0, 0, 0, 0, 0, 0.5); gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.6, "rgba(255,255,255,.55)"); gr.addColorStop(1, "rgba(255,255,255,0)"); g.fillStyle = gr; g.fillRect(-0.5, -0.5, 1, 1); });
    add("men", "util/px", [-0.05, -0.05, 0.05, 0.05], g => { g.fillStyle = "#fff"; g.fillRect(-0.05, -0.05, 0.1, 0.1); });
    add("men", "util/spark", [-0.16, -0.16, 0.16, 0.16], paintSpark);   // вспышка удара (В13)
    // кони: масть × попона × кадр галопа (−1 — стоит)
    COATS.forEach((coat, ci) => {
      const bards = [["none", 0], ["cloth", 0], ["full", 0], ["full", 1], ["full", 2]];
      for(const [bard, c2i] of bards) for(let f = -1; f <= 3; f++)
        add("horses", `horse/${ci}/${bard}${bard === "full" ? "/" + c2i : ""}/${f}`, [-0.36, -1.36, 0.36, 1.37],
          g => paintHorse(g, {coat, bard, col: KEY, c2: C2[c2i]}, f));
      for(const bard of ["none", "cloth", "full"]) add("dead", `deadhorse/${ci}/${bard}`, [-0.5, -1.35, 0.75, 1.0], g => paintDeadHorse(g, {coat, bard, col: KEY}));
      // конь по частям (В13): ноги, хвост, туловище, голова (у полной барды — в стальном налобнике); места и походка —
      // HLEG, HTAIL, HNECK и horsePose в polygon-men.js
      const k = {coat, bard: "none", col: KEY};
      add("horses", `hrig/leg/${ci}`, HBOX.leg, g => paintHorseLeg(g, k));
      add("horses", `hrig/tail/${ci}`, HBOX.tail, g => paintHorseTail(g, k));
      add("horses", `hrig/body/${ci}`, HBOX.body, g => paintHorseBody(g, k));
      add("horses", `hrig/head/${ci}`, HBOX.head, g => paintHorseHead(g, k));
      add("horses", `hrig/head/${ci}/full`, HBOX.head, g => paintHorseHead(g, {coat, bard: "full", col: KEY}));
    });
    // попоны поверх туловища: седло, чепрак цвета стороны, полная барда с полосой герба
    add("horses", "hrig/cover/none", HBOX.cover, g => paintHorseCover(g, {bard: "none", col: KEY}));
    add("horses", "hrig/cover/cloth", HBOX.cover, g => paintHorseCover(g, {bard: "cloth", col: KEY}));
    C2.forEach((c2, i) => add("horses", "hrig/cover/full/" + i, HBOX.cover, g => paintHorseCover(g, {bard: "full", col: KEY, c2})));
    // павшие: лежит на спине, голова — к −y (её рисует Unity шлемом), ноги у начала
    for(const ck of CLOTH_KEYS) for(const a of ARMOURS) for(let v = 0; v < 2; v++){
      const [armour, leather] = armourOf(a);
      add("dead", `corpse/${ck}/${a}/${v}`, [-0.72, -0.5, 0.72, 0.95], g => corpseBody(g, clothOf(ck), armour, leather, v));
    }
    // раненый ползёт (Г39, В13): лицом вниз, без головы (её Unity кладёт шлемом), рывок за рывком — отражённый рисунок
    for(const ck of CLOTH_KEYS) for(const a of ARMOURS){
      const [armour, leather] = armourOf(a);
      add("dead", `crawl/${ck}/${a}`, [-0.5, -1.06, 0.45, 0.95], g => paintCrawlBody(g, clothOf(ck), armour, leather));
    }
    // природа: кроны деревьев (4 палитры × 6 форм), кусты, камни — радиус 1, Unity растягивает до нужного
    TREES.forEach((pal, pi) => { for(let s = 0; s < 6; s++) add("nature", `tree/${pi}/${s}`, [-1.2, -1.2, 1.2, 1.2], g => crownUnit(g, s * 977 + pi * 31, pal, 7)); });
    for(let s = 0; s < 6; s++) add("nature", "bush/" + s, [-1.2, -1.2, 1.2, 1.2], g => crownUnit(g, s * 571 + 5, BUSH, 5));
    for(let s = 0; s < 6; s++) add("nature", "boulder/" + s, [-1.25, -1.25, 1.25, 1.25], g => boulder(g, 0, 0, 1, s * 313 + 9, 0.06));
    return P;
  }
  // крона радиуса 1 м: та же функция полигона, крупно (детали — как вблизи)
  function crownUnit(g, s, pal, nb){ crown(g, 0, 0, 1, s, pal, 60, 0.07, nb); }
  // тело павшего без головы, оружия и щита (их Unity кладёт рядом сам) — как paintCorpse полигона
  function corpseBody(g, cloth, armour, leather, v){ paintLying(g, cloth, armour, leather, corpsePose(v)); }


  const PPM = {men: 128, horses: 72, dead: 72, nature: 64};
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
    for(const atlas of ["men", "horses", "dead", "nature"]){
      if(only && !only.includes(atlas)) continue;
      const r = await build(atlas, all.filter(p => p.atlas === atlas));
      await fetch(`/save?file=${atlas}.png`, {method: "POST", body: r.png});
      await fetch(`/save?file=${atlas}.json`, {method: "POST", body: r.json});
      out.push(`${atlas}: ${r.count} частей, ${r.W}×${r.H}`);
    }
    return out;
  };
})();
