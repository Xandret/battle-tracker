
"use strict";
// ═══════════ связь с движком ═══════════
// Всё боевое — в Engine (src/engine). Здесь только псевдонимы, чтобы интерфейсный код остался читаемым.
const { clamp, r1, MODES, isMeleeMode, moraleStage, discStage, isCav, isPike, isArcherType,
        canBeTargeted, SECTOR_RU, getRules, RULESETS, PLANNED_RULESETS, guessUnitType } = Engine;
let ruleset = "base";
const currentRules = () => getRules(ruleset);
const attackLimit  = u => Engine.attackLimit(u, currentRules());
const counterLimit = u => Engine.counterLimit(u, currentRules());
const attackSector = (a, b) => Engine.attackSector(a, b, currentRules());
const graceByDisc  = d => Engine.graceByDisc(d, currentRules());
function engineCtx(){
  const ctx = {
    rules: currentRules(),
    rng: Math.random,
    commanderOf: u => getCmdr(u),
    factionName: id => factionName(id),
  };
  // черновик карты (6а): на песке и снегу усталость копится быстрее — только при включённых правилах
  if(terrainActive()) ctx.fatigueMult = u => u.onMap ? Engine.fatigueMultFor(u, mapGeo(), currentRules()) : null;
  return ctx;
}

// ═══════════ состояние ═══════════
let units = [], factions = [], subfactions = [], commanders = [], log = [];
let turn = 1, nextId = 1, editingId = null;
let collapsedGroups = {}, expandedUnits = {}, undoStack = [];
let mapImage = null, mapOpts = {grid:false, snap:false, cells:20, tokenSize:42, widthM:2000};
// Правила карты (К10, К26): общий переключатель и по одному на правило; всё — черновик до ГМа
const MAP_RULES_DEFAULT = {on: false, terrain: true, move: true, range: true, panic: true, panicMorale: false};
let mapRules = Object.assign({}, MAP_RULES_DEFAULT);
let mapAttackerId = null, mapCharge = false, openMenuId = null, mapModeOverride = null;
let selectedTokens = {};
let templateOverrides = Engine.normalizeOverrides(null);   // правки шаблонов отрядов: {base, factions}
let importDraft = null;                                    // предпросмотр импорта армий (не сохраняется)
const LS_KEY = "battle_tracker_v13";
const UNDO_MAX = 30;

const esc = s => String(s).replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const $ = id => document.getElementById(id);
const DEFAULT_COLOR = "#C9A227";


const TYPE_NAMES = {infantry:"Пехота", cavalry:"Кавалерия", archer:"Лучники", pike:"Пикинёры"};
// тип войск по названию — Engine.guessUnitType (src/engine/units.js)
const TYPE_SHAPE = {infantry:"sq", cavalry:"rect", archer:"tri", pike:"pent"};
function tokenShape(u){
  return TYPE_SHAPE[u.type] || (u.weapon === "ranged" ? "tri" : "sq");
}
// сектор атаки относительно фасинга цели: front / flank / rear

const getFaction = id => factions.find(f => f.id === id) || null;
const factionName = id => (getFaction(id) || {}).name || "Без фракции";
const factionColor = id => (getFaction(id) || {}).color || DEFAULT_COLOR;
const getSub = id => subfactions.find(x => x.id === id) || null;
const subName = id => (getSub(id) || {}).name || "Без подфракции";
const getCmdr = u => commanders.find(c => c.id === u.commanderId) || null;
function strengthColor(u){
  const frac = u.initial > 0 ? u.soldiers / u.initial : 0;
  if(u.soldiers <= 0) return "#6B6B6B";
  if(frac >= 0.85) return "#7FD07A";
  if(frac >= 0.6)  return "#C9D06A";
  if(frac >= 0.4)  return "#E0B04A";
  if(frac >= 0.2)  return "#E08A46";
  return "#E0604A";
}

// ═══════════ сохранение и откат ═══════════
function readMapOpts(){
  if($("mapGrid")) mapOpts = {
    grid: $("mapGrid").checked, snap: $("mapSnap").checked,
    cells: clamp(+$("mapCells").value || 20, 4, 80),
    tokenSize: clamp(+$("tokenSize").value || 42, 16, 120),
    terrain: $("mapTerrain") ? $("mapTerrain").value : "form",
    widthM: clamp(+(mapOpts.widthM) || 2000, 50, 50000),
  };
  return mapOpts;
}
function stateObj(){
  return {factions, subfactions, commanders, units, log: log.slice(0,300), turn, nextId, ruleset,
          templateOverrides, battleMap: Engine.serializeTerrain(terrainMap), mapRules, mapImage, mapOpts: readMapOpts()};
}
function saveState(){
  try{ localStorage.setItem(LS_KEY, JSON.stringify(stateObj())); }
  catch(e){
    try{
      const lite = stateObj(); lite.mapImage = null;
      localStorage.setItem(LS_KEY, JSON.stringify(lite));
      console.warn("Карта слишком велика для автосохранения — сохранено без изображения.");
    }catch(e2){}
  }
}
function applyLoadedState(s){
  factions = (s.factions || []).map(f => Object.assign({color: DEFAULT_COLOR}, f));
  subfactions = s.subfactions || [];
  commanders = s.commanders || [];
  units = (s.units || []).map(u => Object.assign(
    {weapon:"melee", factionId:null, subfactionId:null, commanderId:null,
     acted:false, attacksMade:0, countersMade:0, totKilled:0, totWounded:0,
     onMap:false, mapX:50, mapY:50, facing:0, breakPenalty:0, tokenScale:1, movedM:0, runUpM:0, range:0}, u));
  log = s.log || []; turn = s.turn || 1; nextId = s.nextId || 1;
  ruleset = RULESETS[s.ruleset] ? s.ruleset : "base";
  if($("rulesetSel")) $("rulesetSel").value = ruleset;
  // до v30.1 шаблонов в сохранении нет — остаются базовые
  templateOverrides = Engine.normalizeOverrides(s.templateOverrides);
  // до v30.3 местности нет; битая — не мешает открыть партию
  terrainMap = Engine.deserializeTerrain(s.battleMap); terrainVersion++;
  // до v30.5 правил карты нет — выключены
  mapRules = Object.assign({}, MAP_RULES_DEFAULT, s.mapRules && typeof s.mapRules === "object" ? s.mapRules : {});
  mapImage = s.mapImage || null;
  if(s.mapOpts) mapOpts = Object.assign(mapOpts, s.mapOpts);
  if($("mapGrid")){
    $("mapGrid").checked = !!mapOpts.grid;
    $("mapSnap").checked = !!mapOpts.snap;
    $("mapCells").value = mapOpts.cells;
    $("tokenSize").value = mapOpts.tokenSize;
    if(mapOpts.terrain) $("mapTerrain").value = mapOpts.terrain;
  }
}
function loadState(){
  try{
    const raw = localStorage.getItem(LS_KEY) || localStorage.getItem("battle_tracker_v5");
    if(!raw) return;
    applyLoadedState(JSON.parse(raw));
  }catch(e){}
}
function pushUndo(label){
  undoStack.push({label, turn, snap: JSON.stringify(stateObj())});
  if(undoStack.length > UNDO_MAX) undoStack.shift();
  renderUndoBtn();
}
function undo(){
  if(!undoStack.length) return;
  const step = undoStack.pop();
  applyLoadedState(JSON.parse(step.snap));
  editingId = null;
  renderAll(); saveState(); renderUndoBtn();
  addLogNoUndo(`↶ Откат: ${step.label}`, ["Состояние возвращено к моменту до этого действия."]);
}
function renderUndoBtn(){
  const b = $("undoBtn");
  if(!b) return;
  b.disabled = undoStack.length === 0;
  const last = undoStack[undoStack.length - 1];
  b.textContent = undoStack.length ? `↶ Откатить (${undoStack.length})` : "↶ Откатить";
  b.title = last ? `Отменить: ${last.label}` : "Нечего откатывать";
}
function resetAll(){
  if(!confirm("Стереть все фракции, подфракции, полководцев, юниты и журнал? Шаблоны отрядов и их правки сохранятся.")) return;
  units = []; factions = []; subfactions = []; commanders = []; log = [];
  turn = 1; nextId = 1; undoStack = []; mapImage = null;
  terrainMap = null; terrainVersion++;
  localStorage.removeItem(LS_KEY);
  renderAll(); renderUndoBtn();
  saveState();   // шаблоны переживают сброс — это настройки, а не партия
}

// ═══════════ файлы ═══════════
function downloadText(filename, text, mime){
  const blob = new Blob([text], {type: (mime || "text/plain") + ";charset=utf-8"});
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}
function exportTxt(){
  downloadText(`armiya_hod${turn}.txt`, JSON.stringify(stateObj(), null, 2));
  addLog("Партия выгружена в TXT", [`Файл: armiya_hod${turn}.txt`]);
}
function importTxt(ev){
  const file = ev.target.files[0];
  if(!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    try{
      const s = JSON.parse(reader.result);
      if(!s.units) throw new Error("нет юнитов");
      if(!confirm(`Загрузить «${file.name}»? Текущее состояние будет заменено.`)) return;
      pushUndo("загрузка партии из файла");
      applyLoadedState(s);
      renderAll(); saveState();
      addLog("Партия загружена из TXT", [`Файл: ${file.name} · юнитов: ${units.length}`]);
    }catch(e){
      alert("Не удалось прочитать файл: это не выгрузка трекера.");
    }
    ev.target.value = "";
  };
  reader.readAsText(file);
}
function safeFileName(s){
  return s.replace(/[^\wа-яА-ЯёЁ\- ]/g, "").trim().replace(/\s+/g, "_") || "otryad";
}
function exportUnitJson(id){
  const u = units.find(x => x.id === id); if(!u) return;
  const data = Object.assign({}, u);
  delete data.id;
  downloadText(`otryad_${safeFileName(u.name)}.json`, JSON.stringify(data, null, 2), "application/json");
  addLog(`Отряд «${u.name}» выгружен в JSON`, [`Файл: otryad_${safeFileName(u.name)}.json`]);
}
function importUnitJson(ev){
  const file = ev.target.files[0];
  if(!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    try{
      const data = JSON.parse(reader.result);
      const list = Array.isArray(data) ? data : [data];
      const added = [];
      pushUndo("загрузка отряда из JSON");
      list.forEach(d => {
        if(!d || typeof d !== "object" || !d.name || d.soldiers === undefined) throw new Error("не отряд");
        const soldiers = Math.max(1, Math.round(+d.soldiers || 1));
        const u = {
          id: nextId++,
          name: String(d.name),
          type: TYPE_NAMES[d.type] ? d.type : "infantry",
          weapon: d.weapon === "ranged" ? "ranged" : "melee",
          factionId: getFaction(d.factionId) ? d.factionId : null,
          subfactionId: getSub(d.subfactionId) ? d.subfactionId : null,
          commanderId: commanders.some(c => c.id === d.commanderId) ? d.commanderId : null,
          soldiers,
          initial: Math.max(soldiers, Math.round(+d.initial || soldiers)),
          discipline: clamp(+d.discipline || 1, 1, 100),
          morale: clamp(+d.morale || 0, 0, 150),
          eqAtk: Math.max(0, +d.eqAtk || 0),
          eqDef: Math.max(0, +d.eqDef || 0),
          exp: clamp(+d.exp || 0, 0, 100),
          mastery: Math.max(0, +d.mastery || 0),
          fatigue: clamp(+d.fatigue || 0, 0, 100),
          range: Math.max(0, Math.round(+d.range || 0)),
          status: "active", turnsActive: 0, fleeChecks: 0, breakGrace: 0, broken: false,
          acted: false, attacksMade: 0, countersMade: 0,
          totKilled: Math.max(0, Math.round(+d.totKilled || 0)),
          totWounded: Math.max(0, Math.round(+d.totWounded || 0)),
        };
        units.push(u);
        added.push(u.name);
      });
      addLog(`Загружено отрядов из «${file.name}»: ${added.length}`, added.map(n => `«${n}» встал в строй`));
      renderAll(); saveState();
    }catch(e){
      alert("Не удалось прочитать файл: это не JSON-выгрузка отряда.");
    }
    ev.target.value = "";
  };
  reader.readAsText(file);
}

// ═══════════ сбор армий из текста ═══════════
// Разбор, подбор шаблонов и нарезка — в движке (Engine.parseArmyText → planMuster → expandMuster).
// Здесь только предпросмотр с правкой и создание фракций, подфракций, полководцев и отрядов.
const IMPORT_COLORS = ["#B0402E", "#5A8FB0", "#7FA05A", "#C97B2E", "#8E6FB0", "#3FA5A0", "#C9A227"];
const UNIT_KINDS = [["infantry/melee", "Пехота"], ["cavalry/melee", "Кавалерия"], ["cavalry/ranged", "Конные стрелки"],
                    ["archer/ranged", "Стрелки"], ["pike/melee", "Пикинёры"]];
const fmtN = n => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, " ");
const sameName = (a, b) => String(a).trim().toLowerCase() === String(b).trim().toLowerCase();
const findFactionByName = name => factions.find(f => sameName(f.name, name)) || null;
const optionsHtml = (list, value) => list.map(([v, n]) =>
  `<option value="${esc(v)}"${v === value ? " selected" : ""}>${esc(n)}</option>`).join("");

// Уже стоящие отряды по фракциям — чтобы нумерация «№N» продолжалась, а не повторялась
function existingNamesByFaction(){
  const out = {};
  units.forEach(u => {
    const f = getFaction(u.factionId); if(!f) return;
    const key = f.name.trim().toLowerCase();
    (out[key] = out[key] || []).push(u.name);
  });
  return out;
}
function importExpand(){
  return Engine.expandMuster(importDraft, {overrides: templateOverrides, existingNames: existingNamesByFaction()});
}
function parseImport(){
  const text = $("importText").value;
  if(!text.trim()){ $("importText").focus(); return; }
  importDraft = Engine.planMuster(Engine.parseArmyText(text), {overrides: templateOverrides});
  renderImportPreview();
}
function cancelImport(){ importDraft = null; renderImportPreview(); }
function clearImport(){ $("importText").value = ""; cancelImport(); }

function renderImportPreview(){
  const box = $("importPreview"); if(!box) return;
  if(!importDraft){ box.innerHTML = ""; renderTemplates(); return; }
  const d = importDraft;
  if(!d.sides.length){
    box.innerHTML = '<div class="hint">Войска не найдены. Нужны строки вида «18.000 всадников Красных Кольчуг».</div>';
    return;
  }
  const tplList = Engine.BASE_TEMPLATES.map(t => [t.id, t.name]);
  let html = d.title ? `<div class="imp-title">${esc(d.title)}</div>` : "";
  if(d.warnings.length) html += `<div class="imp-warn">${d.warnings.map(w =>
    `⚠ ${w.line ? "стр. " + w.line + ": " : ""}${esc(w.text)}`).join("<br>")}</div>`;
  d.sides.forEach((s, i) => {
    html += `<div class="imp-side">
      <label>Сторона ${i + 1} — фракция</label>
      <input value="${esc(s.faction)}" oninput="impSide(${i}, this.value)">
      <div class="hint" id="impFacNote_${i}" style="margin-top:3px"></div>`;
    s.contingents.forEach((c, j) => {
      html += `<div class="imp-cont">`;
      if(s.contingents.length > 1)
        html += `<label>Подфракция</label><input value="${esc(c.name)}" oninput="impCont(${i}, ${j}, this.value)">`;
      if(c.commanders.length) html += `<div class="imp-cmdr">Полководцы: ${c.commanders.map(esc).join(" · ")}</div>`;
      c.lines.forEach((l, k) => {
        const at = `${i}, ${j}, ${k}`, id = `${i}_${j}_${k}`;
        // Полководца строке назначает мастер (К1, К12): по умолчанию ближайший выше
        const cmdrList = [["", "— без полководца —"], ...c.commanders.map(n => [n, n])];
        html += `<div class="imp-line${l.fallback || l.cmdrCheck ? " guess" : ""}">
          <div class="imp-row1">
            <input value="${esc(l.name)}" title="Строка ${l.line}: ${esc(l.src)}" oninput="impLine(${at}, 'name', this.value)">
            <input type="number" min="0" value="${l.count || 0}" title="Численность" oninput="impLine(${at}, 'count', +this.value)">
          </div>
          <div class="imp-row2">
            <select title="Шаблон характеристик" onchange="impTemplate(${at}, this.value)">${optionsHtml(tplList, l.templateId)}</select>
            <select title="Тип войск" onchange="impKind(${at}, this.value)">${optionsHtml(UNIT_KINDS, l.type + "/" + l.weapon)}</select>
            <input type="number" min="1" id="impSize_${id}" value="${l.size || ""}" title="Размер отряда: пусто — из шаблона"
              oninput="impLine(${at}, 'size', +this.value || null)">
            <span class="imp-res" id="impRes_${id}"></span>
          </div>
          ${c.commanders.length ? `<select class="imp-cmdsel" title="Полководец этих отрядов" onchange="impCmdr(${at}, this.value)">${optionsHtml(cmdrList, l.commander || "")}</select>` : ""}
          ${l.cmdrCheck ? '<div class="hint imp-guess">В строке выше несколько полководцев — отряды отданы первому. Выбери нужного.</div>' : ""}
          ${l.fallback ? '<div class="hint imp-guess">Название не подсказало шаблон — взято ополчение. Проверь.</div>' : ""}
          ${l.special ? '<div class="hint imp-guess">Своей механики у пушек, катапульт и слонов пока нет (этап 6б) — встанет обычным отрядом.</div>' : ""}
        </div>`;
      });
      html += `</div>`;
    });
    html += `</div>`;
  });
  html += `<div class="imp-total" id="impTotal"></div>
    <div class="btnrow"><button class="gold" onclick="commitImport()">Собрать армии</button>
      <button onclick="cancelImport()">Отмена</button></div>`;
  box.innerHTML = html;
  updateImportResults();
  renderTemplates();   // стороны из предпросмотра появляются в «Шаблоны отрядов → Для кого»
}
function sizesText(sizes){
  if(sizes.length === 1) return `1 отряд · ${fmtN(sizes[0])}`;
  const same = sizes.filter(x => x === sizes[0]).length;
  if(same === sizes.length) return `${sizes.length} отр. по ${fmtN(sizes[0])}`;
  return `${sizes.length} отр.: ${same} по ${fmtN(sizes[0])} + ${sizes.slice(same).map(fmtN).join(" + ")}`;
}
// Пересчёт без перерисовки полей — чтобы ввод не терял фокус
function updateImportResults(){
  if(!importDraft) return;
  const r = importExpand();
  importDraft.sides.forEach((s, i) => {
    const note = $("impFacNote_" + i);
    if(note){
      const f = findFactionByName(s.faction || `Сторона ${i + 1}`);
      note.textContent = f ? `Фракция «${f.name}» уже есть — отряды добавятся к ней.` : "Будет создана новая фракция.";
    }
    s.contingents.forEach((c, j) => c.lines.forEach((l, k) => {
      const id = `${i}_${j}_${k}`, pl = r.perLine[`${i}.${j}.${k}`] || {units: 0, sizes: []};
      const res = $("impRes_" + id);
      if(res) res.textContent = pl.units ? "→ " + sizesText(pl.sizes) : "→ пропуск: нет численности или названия";
      const sz = $("impSize_" + id);
      if(sz) sz.placeholder = Engine.resolveTemplate(l.templateId, s.faction, templateOverrides).size;
    }));
  });
  const t = $("impTotal");
  if(t) t.innerHTML = `Будет собрано: <b>${r.unitsTotal}</b> отрядов · <b>${fmtN(r.soldiersTotal)}</b> солдат` +
    r.factions.map(F => `<br>${esc(F.name)}: ${F.units.length} отр.` +
      (F.subfactions.length ? ` · подфракций ${F.subfactions.length}` : "") +
      (F.commanders.length ? ` · полководцев ${F.commanders.length}` : "")).join("");
}
const impLineAt = (i, j, k) => importDraft && importDraft.sides[i].contingents[j].lines[k];
function impSide(i, v){ importDraft.sides[i].faction = v; updateImportResults(); renderTemplates(); }
function impCont(i, j, v){ importDraft.sides[i].contingents[j].name = v; updateImportResults(); }
function impLine(i, j, k, field, v){
  const l = impLineAt(i, j, k); if(!l) return;
  l[field] = v;
  updateImportResults();
}
function impCmdr(i, j, k, v){
  const l = impLineAt(i, j, k); if(!l) return;
  l.commander = v || null; l.cmdrCheck = false;
  renderImportPreview();
}
function impKind(i, j, k, v){
  const l = impLineAt(i, j, k); if(!l) return;
  [l.type, l.weapon] = v.split("/");
}
// Смена шаблона тянет за собой его тип войск: «Ополчение» → «Элитная конница» — это уже кавалерия
function impTemplate(i, j, k, id){
  const l = impLineAt(i, j, k), t = Engine.getTemplate(id); if(!l || !t) return;
  l.templateId = id; l.type = t.type; l.weapon = t.weapon; l.fallback = false;
  renderImportPreview();
}

function commitImport(){
  if(!importDraft) return;
  const r = importExpand();
  if(!r.unitsTotal && !r.factions.some(F => F.commanders.length)){
    alert("Нечего собирать: нет ни одной строки с численностью."); return;
  }
  pushUndo("сбор армий из текста");
  const usedColors = new Set(factions.map(f => f.color));
  const lines = [];
  r.factions.forEach(F => {
    let fac = findFactionByName(F.name);
    const isNew = !fac;
    if(isNew){
      const color = IMPORT_COLORS.find(c => !usedColors.has(c)) || IMPORT_COLORS[factions.length % IMPORT_COLORS.length];
      usedColors.add(color);
      fac = {id: nextId++, name: F.name, color};
      factions.push(fac);
    }
    const subIds = {}, cmdIds = {};
    F.subfactions.forEach(name => {
      let sf = subfactions.find(x => x.factionId === fac.id && sameName(x.name, name));
      if(!sf){ sf = {id: nextId++, name, factionId: fac.id}; subfactions.push(sf); }
      subIds[name] = sf.id;
    });
    F.commanders.forEach(name => {
      let c = commanders.find(x => x.name === name && (x.factionId === fac.id || x.factionId === null));
      if(!c){ c = {id: nextId++, name, factionId: fac.id, buffMorale: 0, buffDisc: 0, buffDmg: 0, buffDef: 0}; commanders.push(c); }
      cmdIds[name] = c.id;
    });
    F.units.forEach(U => units.push(makeUnit({
      name: U.name, type: U.type, weapon: U.weapon,
      factionId: fac.id, subfactionId: U.subfaction ? subIds[U.subfaction] : null,
      commanderId: U.commander ? cmdIds[U.commander] : null,
      soldiers: U.soldiers, discipline: U.discipline, morale: U.morale,
      eqAtk: U.eqAtk, eqDef: U.eqDef, exp: U.exp, mastery: U.mastery, fatigue: 0,
    })));
    const byTpl = {};
    F.units.forEach(u => { byTpl[u.templateId] = (byTpl[u.templateId] || 0) + 1; });
    lines.push(`${F.name}${isNew ? "" : " (пополнение)"}: ${F.units.length} отрядов · ${fmtN(F.units.reduce((a, u) => a + u.soldiers, 0))} солдат`);
    if(F.units.length) lines.push("  по шаблонам: " + Object.entries(byTpl).map(([id, n]) => `${Engine.getTemplate(id).name} ${n}`).join(" · "));
    if(F.subfactions.length) lines.push("  подфракции: " + F.subfactions.join(", "));
    if(F.commanders.length) lines.push("  полководцы: " + F.commanders.join(", "));
  });
  importDraft.warnings.forEach(w => lines.push("⚠ " + w.text));
  r.warnings.forEach(w => lines.push("⚠ " + w));
  lines.push("Характеристики — из шаблонов отрядов (черновик до ГМа).");
  addLog(`Армии собраны из текста${importDraft.title ? ": «" + importDraft.title + "»" : ""}`, lines);
  importDraft = null;
  renderImportPreview(); renderAll(); saveState();
}

// ═══════════ шаблоны отрядов ═══════════
// База — черновик в Engine.BASE_TEMPLATES; правки партии — templateOverrides (общие и по фракциям).
const TPL_COLS = [["size", "Отряд"], ["discipline", "Дисц"], ["morale", "БД"], ["eqAtk", "Атк"],
                  ["eqDef", "Защ"], ["exp", "Опыт"], ["mastery", "ЭМ"]];
const TPL_FIELD_RU = {size: "размер отряда", discipline: "дисциплина", morale: "БД", eqAtk: "снар. атака",
                      eqDef: "снар. защита", exp: "опыт", mastery: "мастерство ЭМ"};
// Для кого можно править: фракции партии, стороны из предпросмотра импорта и уже заведённые правки
function tplScopeNames(){
  const names = [];
  const add = n => { if(n && String(n).trim() && !names.some(x => sameName(x, n))) names.push(String(n).trim()); };
  factions.forEach(f => add(f.name));
  if(importDraft) importDraft.sides.forEach(s => add(s.faction));
  Object.keys(templateOverrides.factions).forEach(add);
  return names;
}
const scopeOverrides = scope => scope ? Engine.factionOverrides(templateOverrides, scope) : templateOverrides.base;
function renderTemplates(){
  const sel = $("tplScope"), box = $("tplTable"); if(!sel || !box) return;
  const prev = sel.value, names = tplScopeNames();
  sel.innerHTML = '<option value="">Все фракции — база партии</option>' + names.map(n =>
    `<option value="${esc(n)}">${esc(n)}${Object.keys(Engine.factionOverrides(templateOverrides, n)).length ? " ✎" : ""}</option>`).join("");
  sel.value = names.includes(prev) ? prev : "";
  const scope = sel.value, own = scopeOverrides(scope);
  let html = `<div class="tpl-row tpl-head"><span>Шаблон</span>${TPL_COLS.map(([, n]) => `<span>${n}</span>`).join("")}</div>`;
  Engine.BASE_TEMPLATES.forEach(t => {
    const eff = Engine.resolveTemplate(t.id, scope, templateOverrides);
    const parent = scope ? Engine.resolveTemplate(t.id, "", templateOverrides) : t;
    const mine = own[t.id] || {};
    html += `<div class="tpl-row"><span class="tpl-name" title="${esc(t.source)}">${esc(t.name)}<em>${TYPE_NAMES[t.type]}</em></span>` +
      TPL_COLS.map(([k]) => {
        const changed = k in mine;
        return `<input type="number" class="${changed ? "ovr" : ""}" value="${eff[k]}"
          title="${esc(TPL_FIELD_RU[k])}${changed ? ` · изменено, ${scope ? "база партии" : "черновик"}: ${parent[k]}` : ""}"
          onchange="setTplValue('${t.id}', '${k}', this.value)">`;
      }).join("") + `</div>`;
  });
  box.innerHTML = html;
  $("tplResetBtn").disabled = !Object.keys(own).length;
}
function editOverrides(scope, fn){
  const ov = JSON.parse(JSON.stringify(templateOverrides));
  let bucket = ov.base;
  if(scope){
    const key = Object.keys(ov.factions).find(k => sameName(k, scope)) || scope;
    bucket = ov.factions[key] = ov.factions[key] || {};
  }
  fn(bucket, ov);
  templateOverrides = Engine.normalizeOverrides(ov);   // заодно выбрасывает пустые правки
  renderTemplates(); saveState();
  if(importDraft) updateImportResults();
}
// Пустое поле или значение, равное базе, снимает правку
function setTplValue(id, field, raw){
  const t = Engine.getTemplate(id); if(!t) return;
  const scope = $("tplScope").value;
  const parent = scope ? Engine.resolveTemplate(id, "", templateOverrides) : t;
  const v = String(raw).trim() === "" ? parent[field] : Engine.clampField(field, +raw);
  pushUndo(`шаблон «${t.name}»${scope ? " для «" + scope + "»" : ""}: ${TPL_FIELD_RU[field]}`);
  editOverrides(scope, bucket => {
    const patch = bucket[id] = bucket[id] || {};
    if(v === parent[field]) delete patch[field]; else patch[field] = v;
  });
}
function resetTplScope(){
  const scope = $("tplScope").value;
  if(!confirm(scope ? `Сбросить правки шаблонов для «${scope}»?` : "Вернуть общие шаблоны партии к черновику?")) return;
  pushUndo(scope ? `сброс шаблонов для «${scope}»` : "сброс общих шаблонов");
  editOverrides(scope, (bucket, ov) => {
    if(scope) Object.keys(ov.factions).filter(k => sameName(k, scope)).forEach(k => delete ov.factions[k]);
    else ov.base = {};
  });
}

// ═══════════ журнал ═══════════
function addLogNoUndo(title, lines, tone){
  log.unshift({id: Date.now()+Math.random(), turn, title, lines: lines||[], tone: tone||"info"});
  renderLog();
  saveState();
}
const addLog = addLogNoUndo;

const DETAIL_RE = /^(Бросок d|Атака:|Усталость |Боевой дух |🐎 Натиск|Кавалерия без натиска|⚜ |Защита цели|Ситуативный модификатор|Помеха|⛰ |Укрытие «)|— стрелки в ближнем бою/;
const BAD_RE = /^(☠|💥|⚠|✘)|уничтожен|Штраф БД|требуется проверка|обращён в бегство|падает до нуля/;
const BIG_RE = /^(Потери «|Из них:|☠)/;
function markNums(t){
  return esc(t).replace(/\d+([.,]\d+)?/g, m => `<b class="num">${m}</b>`);
}
function renderLog(){
  const box = $("logList");
  if(!box) return;
  if(!log.length){ box.innerHTML = '<div class="hint">Здесь появится подробный разбор каждого боя и каждой проверки.</div>'; return; }
  box.innerHTML = log.map(e => {
    let html = "", pending = [], hasDetails = false;
    const flush = () => {
      if(!pending.length) return;
      hasDetails = true;
      html += `<div class="ldetails">${pending.map(l => `<div class="lline">${esc(l)}</div>`).join("")}</div>`;
      pending = [];
    };
    e.lines.forEach(l => {
      if(l.startsWith("——")){ flush(); html += `<div class="lline sep">${esc(l)}</div>`; }
      else if(DETAIL_RE.test(l)){ pending.push(l); }
      else {
        flush();
        const cls = (BAD_RE.test(l) ? "bad " : "") + (BIG_RE.test(l) ? "big" : "");
        html += `<div class="lresult ${cls}">${markNums(l)}</div>`;
      }
    });
    flush();
    return `<div class="lentry ${e.tone} ${hasDetails ? "hasdetails" : ""}" onclick="this.classList.toggle('open')">
       <div class="ltitle">${esc(e.title)} <span class="lturn">· ход ${e.turn}</span></div>
       ${html}
     </div>`;
  }).join("");
}

// ═══════════ фракции и подфракции ═══════════
function addFaction(){
  const name = $("fx_name").value.trim();
  if(!name) return;
  pushUndo(`создание фракции «${name}»`);
  factions.push({id: nextId++, name, color: $("fx_color").value || DEFAULT_COLOR});
  $("fx_name").value = "";
  addLog(`Фракция «${name}» основана`, []);
  renderAll(); saveState();
}
function addSubfaction(){
  const name = $("sub_name").value.trim();
  const fid = +$("sub_faction").value || null;
  if(!name) return;
  if(!fid){ alert("Сначала выбери фракцию, в которую входит подфракция."); return; }
  pushUndo(`создание подфракции «${name}»`);
  subfactions.push({id: nextId++, name, factionId: fid});
  $("sub_name").value = "";
  addLog(`Подфракция «${name}» сформирована`, [`Входит в состав: ${factionName(fid)}`]);
  renderAll(); saveState();
}
function setFactionColor(id, color){
  factions = factions.map(f => f.id === id ? Object.assign({}, f, {color}) : f);
  renderAll(); saveState();
}
function removeFaction(id){
  const f = getFaction(id); if(!f) return;
  if(!confirm(`Удалить фракцию «${f.name}»? Её подфракции тоже будут удалены, юниты останутся без фракции.`)) return;
  pushUndo(`удаление фракции «${f.name}»`);
  const subIds = subfactions.filter(x => x.factionId === id).map(x => x.id);
  factions = factions.filter(x => x.id !== id);
  subfactions = subfactions.filter(x => x.factionId !== id);
  units = units.map(u => u.factionId === id || subIds.includes(u.subfactionId)
    ? Object.assign({}, u, {factionId: u.factionId === id ? null : u.factionId,
                            subfactionId: subIds.includes(u.subfactionId) ? null : u.subfactionId}) : u);
  commanders = commanders.map(c => c.factionId === id ? Object.assign({}, c, {factionId: null}) : c);
  addLog(`Фракция «${f.name}» распущена`, []);
  renderAll(); saveState();
}
function removeSubfaction(id){
  const sf = getSub(id); if(!sf) return;
  if(!confirm(`Удалить подфракцию «${sf.name}»? Её отряды останутся во фракции без подфракции.`)) return;
  pushUndo(`удаление подфракции «${sf.name}»`);
  subfactions = subfactions.filter(x => x.id !== id);
  units = units.map(u => u.subfactionId === id ? Object.assign({}, u, {subfactionId: null}) : u);
  addLog(`Подфракция «${sf.name}» расформирована`, []);
  renderAll(); saveState();
}
function renderFactions(){
  const box = $("factionList");
  if(factions.length){
    let html = "";
    factions.forEach(f => {
      const n = units.filter(u => u.factionId === f.id).length;
      html += `<div>
        <span class="fleft">
          <input type="color" class="fx-color" value="${f.color || DEFAULT_COLOR}"
            onchange="setFactionColor(${f.id}, this.value)" title="Цвет армии">
          ${esc(f.name)} · отрядов: ${n}
        </span>
        <button class="sm red" onclick="removeFaction(${f.id})">✕</button></div>`;
      subfactions.filter(x => x.factionId === f.id).forEach(sf => {
        const sn = units.filter(u => u.subfactionId === sf.id).length;
        html += `<div class="subline"><span class="fleft">↳ ${esc(sf.name)} · отрядов: ${sn}</span>
          <button class="sm red" onclick="removeSubfaction(${sf.id})">✕</button></div>`;
      });
    });
    box.innerHTML = html;
  } else {
    box.innerHTML = '<div class="hint">Фракций нет. Добавь хотя бы одну, чтобы формировать армии.</div>';
  }
  renderFactionSelects();
}
function renderFactionSelects(){
  const opts = '<option value="">— без фракции —</option>' +
    factions.map(f => `<option value="${f.id}">${esc(f.name)}</option>`).join("");
  ["f_faction","c_faction"].forEach(id => {
    const sel = $(id); if(!sel) return;
    const prev = sel.value; sel.innerHTML = opts; sel.value = prev;
  });
  const ss = $("sub_faction");
  if(ss){
    const prev = ss.value;
    ss.innerHTML = '<option value="">— фракция —</option>' +
      factions.map(f => `<option value="${f.id}">${esc(f.name)}</option>`).join("");
    ss.value = prev;
  }
  refreshUnitSubSelect();
}
function refreshUnitSubSelect(){
  const sel = $("f_sub"); if(!sel) return;
  const prev = sel.value;
  const fid = +$("f_faction").value || null;
  const pool = subfactions.filter(x => x.factionId === fid);
  sel.innerHTML = '<option value="">— без подфракции —</option>' +
    pool.map(x => `<option value="${x.id}">${esc(x.name)}</option>`).join("");
  sel.value = pool.some(x => String(x.id) === prev) ? prev : "";
}

// ═══════════ полководцы ═══════════
function cmdrBuffText(c){
  const parts = [];
  if(+c.buffMorale) parts.push(`БД ${c.buffMorale > 0 ? "+" : ""}${c.buffMorale}`);
  if(+c.buffDisc) parts.push(`дисц ${c.buffDisc > 0 ? "+" : ""}${c.buffDisc}`);
  if(+c.buffDmg) parts.push(`урон ${c.buffDmg > 0 ? "+" : ""}${c.buffDmg}%`);
  if(+c.buffDef) parts.push(`защита ${c.buffDef > 0 ? "+" : ""}${c.buffDef}%`);
  return parts.length ? parts.join(", ") : "без баффов";
}
function renderCmdrs(){
  const box = $("cmdrList");
  box.innerHTML = commanders.length
    ? commanders.map(c => {
        const n = units.filter(u => u.commanderId === c.id).length;
        return `<div><span class="fleft"><span class="fdot" style="background:${factionColor(c.factionId)}"></span>${esc(c.name)} (${esc(factionName(c.factionId))}) · ${esc(cmdrBuffText(c))} · отрядов: ${n}</span></div>`;
      }).join("")
    : '<div class="hint">Полководцев нет.</div>';
  const sel = $("c_edit"); const prev = sel.value;
  sel.innerHTML = '<option value="">— новый полководец —</option>' +
    commanders.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join("");
  sel.value = prev;
  refreshUnitCmdrSelect();
}
function loadCmdrForm(){
  const c = commanders.find(x => x.id === +$("c_edit").value);
  if(c){
    $("c_name").value = c.name; $("c_faction").value = c.factionId || "";
    $("c_bMorale").value = c.buffMorale; $("c_bDisc").value = c.buffDisc;
    $("c_bDmg").value = c.buffDmg; $("c_bDef").value = c.buffDef;
    $("cmdrSaveBtn").textContent = "Сохранить";
    $("cmdrDelBtn").classList.remove("hidden");
  } else {
    $("c_name").value = ""; $("c_bMorale").value = 0; $("c_bDisc").value = 0;
    $("c_bDmg").value = 0; $("c_bDef").value = 0;
    $("cmdrSaveBtn").textContent = "Добавить";
    $("cmdrDelBtn").classList.add("hidden");
  }
}
function saveCmdr(){
  const name = $("c_name").value.trim();
  if(!name){ $("c_name").focus(); return; }
  const data = {
    name,
    factionId: +$("c_faction").value || null,
    buffMorale: +$("c_bMorale").value || 0,
    buffDisc: +$("c_bDisc").value || 0,
    buffDmg: +$("c_bDmg").value || 0,
    buffDef: +$("c_bDef").value || 0,
  };
  const editId = +$("c_edit").value;
  pushUndo(editId ? `правка полководца «${name}»` : `создание полководца «${name}»`);
  if(editId){
    commanders = commanders.map(c => c.id === editId ? Object.assign({}, c, data) : c);
    addLog(`Полководец «${name}» изменён`, [cmdrBuffText(data)]);
  } else {
    commanders.push(Object.assign({id: nextId++}, data));
    addLog(`Полководец «${name}» принял командование`, [cmdrBuffText(data)]);
  }
  $("c_edit").value = ""; loadCmdrForm();
  renderAll(); saveState();
}
function deleteCmdr(){
  const editId = +$("c_edit").value; if(!editId) return;
  const c = commanders.find(x => x.id === editId); if(!c) return;
  if(!confirm(`Удалить полководца «${c.name}»? Его юниты останутся без командира.`)) return;
  pushUndo(`удаление полководца «${c.name}»`);
  commanders = commanders.filter(x => x.id !== editId);
  units = units.map(u => u.commanderId === editId ? Object.assign({}, u, {commanderId: null}) : u);
  addLog(`Полководец «${c.name}» отстранён от командования`, []);
  $("c_edit").value = ""; loadCmdrForm();
  renderAll(); saveState();
}
function refreshUnitCmdrSelect(){
  const sel = $("f_cmdr"); if(!sel) return;
  const prev = sel.value;
  const fid = +$("f_faction").value || null;
  const pool = fid ? commanders.filter(c => c.factionId === fid || c.factionId === null) : commanders;
  sel.innerHTML = '<option value="">— без полководца —</option>' +
    pool.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join("");
  sel.value = prev;
}
function onUnitFactionChange(){
  refreshUnitSubSelect();
  refreshUnitCmdrSelect();
}

// ═══════════ форма юнита ═══════════
function buffCell(base, buff, cmdrName){
  if(!buff) return `<b>${base}</b>`;
  const sign = buff > 0 ? "+" : "";
  return `<b>${base}</b><em class="${buff < 0 ? "neg" : ""}">(${sign}${buff} от ${esc(cmdrName)})</em>`;
}
function formHtml(isNew){
  return `<div class="editbox" id="editBox">
    <h3>${isNew ? "Новый юнит" : "Изменить юнит"}</h3>
    <div class="frow">
      <div><label>Название</label><input id="f_name" placeholder="Копейщики Данмиров" oninput="autoDetectType()"></div>
      <div><label>Тип войск</label>
        <select id="f_type" onchange="typeTouched = true; renderTypeHint()">
          <option value="infantry">Пехота</option>
          <option value="cavalry">Кавалерия</option>
          <option value="archer">Лучники</option>
          <option value="pike">Пикинёры</option>
        </select>
      </div>
      <div><label>Тип атаки</label>
        <select id="f_weapon" onchange="typeTouched = true; renderTypeHint()">
          <option value="melee">Ближний бой</option>
          <option value="ranged">Дальний бой (стрелки)</option>
        </select>
      </div>
    </div>
    <div class="frow">
      <div><label>Фракция</label><select id="f_faction" onchange="onUnitFactionChange()"></select></div>
      <div><label>Подфракция</label><select id="f_sub"></select></div>
      <div><label>Полководец</label><select id="f_cmdr"></select></div>
    </div>
    <div class="frow">
      <div><label>Солдаты</label><input id="f_soldiers" type="number" value="300"></div>
      <div><label>Дисциплина 1–100</label><input id="f_disc" type="number" value="50"></div>
      <div><label>БД 0–150</label><input id="f_morale" type="number" value="60"></div>
    </div>
    <div class="frow">
      <div><label>Снар. атака</label><input id="f_eqAtk" type="number" value="30"></div>
      <div><label>Снар. защита</label><input id="f_eqDef" type="number" value="30"></div>
      <div><label>Опыт 0–100</label><input id="f_exp" type="number" value="20"></div>
    </div>
    <div class="frow">
      <div><label>Мастерство ЭМ</label><input id="f_mastery" type="number" value="0"></div>
      <div><label>Усталость</label><input id="f_fatigue" type="number" value="0"></div>
      <div><label>Дальность, м</label><input id="f_range" type="number" min="0" placeholder="по типу" title="Для стрелков при правилах карты; пусто — по типу войск"></div>
    </div>
    <div class="hint" id="typeHint" style="margin:0 0 8px"></div>
    <div class="btnrow" style="display:flex">
      <button class="gold" onclick="saveUnit()">${isNew ? "Добавить" : "Сохранить"}</button>
      <button onclick="hideForm()">Отмена</button>
    </div>
  </div>`;
}
const FORM_IDS = ["f_name","f_type","f_weapon","f_faction","f_sub","f_cmdr","f_soldiers","f_disc",
                  "f_morale","f_eqAtk","f_eqDef","f_exp","f_mastery","f_fatigue","f_range"];
function snapshotForm(){
  if(!$("editBox")) return null;
  const o = {};
  FORM_IDS.forEach(id => { const el = $(id); if(el) o[id] = el.value; });
  return o;
}
function fillForm(snap){
  if(!$("editBox")) return;
  renderFactionSelects();
  if(snap){
    FORM_IDS.forEach(id => { const el = $(id); if(el && snap[id] !== undefined) el.value = snap[id]; });
    onUnitFactionChange();
    if(snap.f_sub !== undefined) $("f_sub").value = snap.f_sub;
    if(snap.f_cmdr !== undefined) $("f_cmdr").value = snap.f_cmdr;
    return;
  }
  const u = units.find(x => x.id === editingId);
  if(u){
    $("f_name").value=u.name; $("f_type").value=u.type; $("f_weapon").value=u.weapon || "melee";
    $("f_faction").value=u.factionId || "";
    onUnitFactionChange();
    $("f_sub").value=u.subfactionId || ""; $("f_cmdr").value=u.commanderId || "";
    $("f_soldiers").value=u.soldiers; $("f_disc").value=u.discipline; $("f_morale").value=u.morale;
    $("f_eqAtk").value=u.eqAtk; $("f_eqDef").value=u.eqDef; $("f_exp").value=u.exp;
    $("f_mastery").value=u.mastery; $("f_fatigue").value=u.fatigue; $("f_range").value=u.range || "";
  } else {
    $("f_name").value=""; $("f_type").value="infantry"; $("f_weapon").value="melee";
    $("f_faction").value=""; onUnitFactionChange();
    $("f_sub").value=""; $("f_cmdr").value="";
    $("f_soldiers").value=300; $("f_disc").value=50; $("f_morale").value=60;
    $("f_eqAtk").value=30; $("f_eqDef").value=30; $("f_exp").value=20;
    $("f_mastery").value=0; $("f_fatigue").value=0; $("f_range").value="";
    if($("f_name").focus) $("f_name").focus();
  }
}
let typeTouched = false;
function renderTypeHint(text){
  const h = $("typeHint"); if(!h) return;
  h.textContent = text !== undefined ? text
    : (typeTouched ? "Тип задан вручную — по названию больше не подставляется." : "");
}
function autoDetectType(){
  if(typeTouched) return;
  const g = guessUnitType($("f_name").value);
  if(!g){ renderTypeHint(""); return; }
  $("f_type").value = g.type;
  $("f_weapon").value = g.weapon;
  renderTypeHint(`Тип определён по названию: ${TYPE_NAMES[g.type]} · ${g.weapon === "ranged" ? "дальний бой" : "ближний бой"}. Можно поменять вручную.`);
}
function autoTypeAll(){
  const changes = [];
  units.forEach(u => {
    const g = guessUnitType(u.name);
    if(g && (g.type !== u.type || g.weapon !== u.weapon))
      changes.push({u, g, from: `${TYPE_NAMES[u.type] || u.type}/${u.weapon === "ranged" ? "дальний" : "ближний"}`});
  });
  if(!changes.length){
    alert("Названия ничего нового не подсказывают: у всех отрядов тип уже соответствует названию (или название не распознано).");
    return;
  }
  const preview = changes.slice(0, 12).map(c =>
    `${c.u.name}: ${c.from} → ${TYPE_NAMES[c.g.type]}/${c.g.weapon === "ranged" ? "дальний" : "ближний"}`).join("\n");
  if(!confirm(`Определить тип войск по названию для ${changes.length} отрядов?\n\n${preview}${changes.length > 12 ? `\n… и ещё ${changes.length - 12}` : ""}\n\nДействие можно откатить кнопкой «Откатить».`)) return;
  pushUndo("определение типов по названиям");
  changes.forEach(c => updUnit(c.u.id, {type: c.g.type, weapon: c.g.weapon}));
  addLog(`Типы войск определены по названиям: ${changes.length} отрядов`,
    changes.slice(0, 30).map(c => `${c.u.name}: ${c.from} → ${TYPE_NAMES[c.g.type]}`));
  renderAll(); saveState();
}
function newUnit(){
  editingId = "new";
  typeTouched = false;
  renderUnits();
  const box = $("editBox");
  if(box && box.scrollIntoView) box.scrollIntoView({block: "nearest"});
}
function startEdit(id){
  const u = units.find(x => x.id === id); if(!u) return;
  typeTouched = true;
  collapsedGroups["f" + (u.factionId || null)] = false;
  collapsedGroups["s" + (u.subfactionId || null)] = false;
  editingId = id;
  renderUnits();
  const box = $("editBox");
  if(box && box.scrollIntoView) box.scrollIntoView({block: "nearest"});
}
function hideForm(){ editingId = null; renderUnits(); }
function saveUnit(){
  const name = $("f_name").value.trim();
  if(!name){ $("f_name").focus(); return; }
  const clean = {
    name, type: $("f_type").value, weapon: $("f_weapon").value,
    factionId: +$("f_faction").value || null,
    subfactionId: +$("f_sub").value || null,
    commanderId: +$("f_cmdr").value || null,
    soldiers: Math.max(1, Math.round(+$("f_soldiers").value || 1)),
    discipline: clamp(+$("f_disc").value || 1, 1, 100),
    morale: clamp(+$("f_morale").value || 0, 0, 150),
    eqAtk: Math.max(0, +$("f_eqAtk").value || 0),
    eqDef: Math.max(0, +$("f_eqDef").value || 0),
    exp: clamp(+$("f_exp").value || 0, 0, 100),
    mastery: Math.max(0, +$("f_mastery").value || 0),
    fatigue: clamp(+$("f_fatigue").value || 0, 0, 100),
    range: Math.max(0, Math.round(+$("f_range").value || 0)),
  };
  const editing = editingId && editingId !== "new";
  pushUndo(editing ? `правка отряда «${name}»` : `создание отряда «${name}»`);
  if(editing){
    updUnit(editingId, clean);
    const u = units.find(x => x.id === editingId);
    if(u && u.status === "destroyed" && u.soldiers > 0){
      updUnit(editingId, {status: "active"});
      addLog(`Юнит «${clean.name}» восстановлен и возвращён в строй`, []);
    } else {
      addLog(`Юнит «${clean.name}» изменён`, []);
    }
  } else {
    units.push(makeUnit(clean));
    addLog(`Юнит «${clean.name}» встал в строй`,
      [`${clean.soldiers} солдат · дисц ${clean.discipline} · БД ${clean.morale} · ${factionName(clean.factionId)}${clean.subfactionId ? " / " + subName(clean.subfactionId) : ""}`]);
  }
  editingId = null; renderAll(); saveState();
}

// ═══════════ юниты: операции ═══════════
// Новый отряд в строю: служебные поля по умолчанию + то, что пришло из формы или импорта
function makeUnit(clean){
  return Object.assign({id: nextId++, initial: clean.soldiers, status:"active",
    turnsActive:0, fleeChecks:0, breakGrace:0, broken:false,
    acted:false, attacksMade:0, countersMade:0, totKilled:0, totWounded:0,
    onMap:false, mapX:50, mapY:50, facing:0, movedM:0, runUpM:0}, clean);
}
function updUnit(id, patch){
  units = units.map(u => u.id === id ? Object.assign({}, u, patch) : u);
}
function unitIcons(u){
  return (u.type === "cavalry" ? "🐎" : "") + (u.weapon === "ranged" ? "🏹" : "");
}
function toggleActed(id){
  const u = units.find(x => x.id === id); if(!u) return;
  updUnit(id, {acted: !u.acted});
  renderUnits(); saveState();
}
function cloneName(base){
  const m = base.match(/^(.*?)(?:\s№(\d+))?$/);
  const root = m ? m[1] : base;
  let maxN = 1;
  units.forEach(u => {
    const um = u.name.match(/^(.*?)\s№(\d+)$/);
    if(um && um[1] === root) maxN = Math.max(maxN, +um[2]);
    if(u.name === root) maxN = Math.max(maxN, 1);
  });
  return `${root} №${maxN + 1}`;
}
function cloneUnit(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`клонирование отряда «${u.name}»`);
  const c = Object.assign({}, u, {
    id: nextId++,
    name: cloneName(u.name),
    initial: u.soldiers > 0 ? u.soldiers : u.initial,
    soldiers: u.soldiers > 0 ? u.soldiers : u.initial,
    status: "active", turnsActive: 0, fleeChecks: 0, breakGrace: 0, broken: false,
    acted: false, attacksMade: 0, countersMade: 0, totKilled: 0, totWounded: 0,
    onMap: false, mapX: 50, mapY: 50, facing: 0,
  });
  units.push(c);
  addLog(`Юнит «${c.name}» встал в строй (клон «${u.name}»)`,
    [`${c.soldiers} солдат · дисц ${c.discipline} · БД ${c.morale} · ${factionName(c.factionId)}`]);
  renderAll(); saveState();
}
function markFled(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`«${u.name}» покинул поле боя`);
  updUnit(id, {status: "fled"});
  addLog(`«${u.name}» покинул поле боя`, ["Отмечен мастером как сбежавший."], "danger");
  runPanic([id]);
  renderAll(); saveState();
}
function ralliedUnit(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`возвращение «${u.name}» в строй`);
  const patch = {status: "active", broken: false, breakGrace: 0, morale: Math.max(u.morale, 40)};
  updUnit(id, patch);
  addLog(`✦ «${u.name}» воспрял духом и вернулся в строй`,
    [`БД поднят до ${patch.morale} (минимум «безучастны»).`]);
  renderAll(); saveState();
}
function removeUnit(id){
  const u = units.find(x => x.id === id);
  if(u) pushUndo(`удаление отряда «${u.name}»`);
  units = units.filter(x => x.id !== id);
  if(u) addLog(`Юнит «${u.name}» убран с карты`, []);
  renderAll(); saveState();
}

// ═══════════ отрисовка списка ═══════════
function toggleUnitCard(id, evt){
  if(evt && evt.stopPropagation) evt.stopPropagation();
  expandedUnits[id] = !expandedUnits[id];
  renderUnits();
}
function toggleGroup(key){
  collapsedGroups[key] = !collapsedGroups[key];
  renderUnits();
}
function unitCardHtml(u){
  const st = moraleStage(u.morale);
  const fcolor = factionColor(u.factionId);
  const cmdr = getCmdr(u);
  let tag;
  if(u.status === "fled") tag = '<span class="tag" style="background:#B0402E;color:#fff">Бежал</span>';
  else if(u.status === "destroyed") tag = '<span class="tag" style="background:#555;color:#fff">Уничтожен</span>';
  else tag = `<span class="tag" style="background:${st.color};color:#14181A">${st.label}</span>`;

  let checks = "";
  if(u.status === "active"){
    if(u.morale > 0 && u.morale <= 40)
      checks += `<button class="check" onclick="moraleCheck(${u.id})">⚑ Проверка БД — боевой дух ${u.morale} (порог 40)</button>`;
    if(u.morale === 0)
      checks += `<button class="check" onclick="fleeCheck(${u.id})">🏃 Проверка на побег — БД на нуле</button>`;
  }

  let btns = "";
  if(u.status === "active"){
    btns += `<button class="sm ${u.acted ? "green" : ""}" onclick="toggleActed(${u.id})">${u.acted ? "✓ Походил" : "Не походил"}</button>`;
  }
  btns += `<button class="sm" onclick="${u.onMap ? `removeFromMap(${u.id})` : `placeOnMap(${u.id})`}">${u.onMap ? "С карты" : "На карту"}</button>
           <button class="sm" onclick="startEdit(${u.id})">Изменить</button>
           <button class="sm" onclick="cloneUnit(${u.id})">Клонировать</button>
           <button class="sm" onclick="exportUnitJson(${u.id})">JSON</button>`;
  if(u.status === "active"){
    btns += `<button class="sm" onclick="markFled(${u.id})">Сбежал</button>`;
  } else if(u.status === "fled"){
    btns += `<button class="sm blue" onclick="ralliedUnit(${u.id})">Воспряли духом</button>`;
  }
  btns += `<button class="sm red" onclick="removeUnit(${u.id})">Убрать</button>`;
  if(u.status === "active"){
    btns = `<button class="sm gold" onclick="setSide('att',${u.id})" title="Поставить стороной А">⚔ В атаку</button>
            <button class="sm blue" onclick="setSide('def',${u.id})" title="Поставить стороной Б">🛡 Под удар</button>` + btns;
  }

  const dim = u.status === "destroyed" ? "#555" : u.status === "fled" ? "#B0402E" : fcolor;
  const atkInfo = u.status === "active"
    ? ` · атак: ${u.attacksMade || 0}/${attackLimit(u)} · ответных: ${u.countersMade || 0}/${counterLimit(u)}`
    : "";
  const cmdrExtra = cmdr && (cmdr.buffDmg || cmdr.buffDef)
    ? ` (${[cmdr.buffDmg ? `урон ${cmdr.buffDmg > 0 ? "+" : ""}${cmdr.buffDmg}%` : "", cmdr.buffDef ? `защита ${cmdr.buffDef > 0 ? "+" : ""}${cmdr.buffDef}%` : ""].filter(Boolean).join(", ")})`
    : "";
  const open = editingId === u.id || !!expandedUnits[u.id];
  return `<div class="ucard ${u.status !== "active" ? u.status : ""} ${open ? "reveal" : ""}"
      style="border-left-color:${dim}" draggable="true" data-uid="${u.id}">
    <div class="uname" onclick="toggleUnitCard(${u.id}, event)"><span>${esc(u.name)} <span class="ucount" style="color:${strengthColor(u)}">— ${u.soldiers}</span> ${unitIcons(u)}</span><span>${tag}</span></div>
    <div class="stats">
      <div><span>Солдаты</span><b class="hp" style="color:${strengthColor(u)}">${u.soldiers}</b>/${u.initial}</div>
      <div><span>Дисц</span>${buffCell(u.discipline, cmdr ? cmdr.buffDisc : 0, cmdr ? cmdr.name : "")}</div>
      <div><span>БД</span>${buffCell(u.morale, cmdr ? cmdr.buffMorale : 0, cmdr ? cmdr.name : "")}/150</div>
      <div><span>Усталость</span><b>${u.fatigue}</b></div>
      <div><span>Снар. атк</span><b>${u.eqAtk}</b></div>
      <div><span>Снар. защ</span><b>${u.eqDef}</b></div>
      <div><span>Опыт</span><b>${u.exp}</b></div>
      <div><span>Мастерство</span><b>${u.mastery}</b></div>
    </div>
    <div class="stageline">${cmdr ? "⚜ " + esc(cmdr.name) + cmdrExtra + " · " : ""}${u.weapon === "ranged" ? "Стрелки" : "Ближний бой"} · ${discStage(u.discipline)} · ${st.note}${atkInfo} · потери за кампанию: ${u.totKilled} уб. / ${u.totWounded} ран.</div>
    ${checks}
    <div class="btnrow">${btns}</div>
  </div>`;
}
function renderUnits(){
  const snap = snapshotForm();
  $("unitCount").textContent = units.length;
  const box = $("unitsList");
  let html = "";
  if(!units.length){
    html = '<div class="hint" style="margin-bottom:10px">Армии пусты. Добавь первый юнит, чтобы начать бой.</div>';
  } else {
    const facGroups = [...factions, {id: null, name: "Без фракции", color: DEFAULT_COLOR}];
    facGroups.forEach(f => {
      const fu = units.filter(u => (u.factionId || null) === f.id);
      if(!fu.length) return;
      const fkey = "f" + f.id;
      const fHidden = !!collapsedGroups[fkey];
      const col = f.color || DEFAULT_COLOR;
      html += `<div class="fhead" style="color:${col}" onclick="toggleGroup('${fkey}')"
        data-drop-fac="${f.id === null ? "" : f.id}" data-drop-sub="">
        <span class="fleftgrp"><span class="fdot" style="background:${col}"></span>${esc(f.name)} (${fu.length})</span>
        <span class="fcaret">${fHidden ? "показать ▸" : "скрыть ▾"}</span></div>`;
      if(fHidden) return;
      const subGroups = [...subfactions.filter(x => x.factionId === f.id), {id: null, name: "Без подфракции"}];
      subGroups.forEach(sf => {
        const su = fu.filter(u => (u.subfactionId || null) === sf.id);
        if(!su.length) return;
        const skey = "s" + sf.id;
        const sHidden = !!collapsedGroups[skey];
        const showSubHead = sf.id !== null || subGroups.length > 1;
        if(showSubHead){
          html += `<div class="shead" onclick="toggleGroup('${skey}')"
            data-drop-fac="${f.id === null ? "" : f.id}" data-drop-sub="${sf.id === null ? "" : sf.id}">
            <span class="fleftgrp"><span class="sdot" style="background:${col}"></span>${esc(sf.name)} (${su.length})</span>
            <span class="fcaret">${sHidden ? "▸" : "▾"}</span></div>`;
        }
        if(sHidden) return;
        su.forEach(u => {
          html += unitCardHtml(u);
          if(editingId === u.id) html += formHtml(false);
        });
      });
    });
  }
  if(editingId === "new") html += formHtml(true);
  box.innerHTML = html;
  fillForm(snap);
  renderTypeHint();
  renderSelects();
  renderSummary(); renderQueue();
}
function renderSummary(){
  const box = $("summaryStrip");
  if(!box) return;
  const groups = [...factions, {id: null, name: "Без фракции", color: DEFAULT_COLOR}];
  let html = "";
  groups.forEach(f => {
    const fu = units.filter(u => (u.factionId || null) === f.id);
    if(!fu.length) return;
    const col = f.color || DEFAULT_COLOR;
    const active = fu.filter(u => u.status === "active");
    const fled = fu.filter(u => u.status === "fled").length;
    const dead = fu.filter(u => u.status === "destroyed").length;
    const soldiers = fu.reduce((s,u) => s + u.soldiers, 0);
    const initial = fu.reduce((s,u) => s + u.initial, 0);
    const pct = initial ? Math.round(soldiers / initial * 100) : 0;
    const avgM = active.length ? Math.round(active.reduce((s,u) => s + u.morale, 0) / active.length) : 0;
    const avgD = active.length ? Math.round(active.reduce((s,u) => s + u.discipline, 0) / active.length) : 0;
    const ready = active.filter(u => (u.attacksMade || 0) < attackLimit(u)).length;
    const k = fu.reduce((s,u) => s + (u.totKilled||0), 0);
    const wnd = fu.reduce((s,u) => s + (u.totWounded||0), 0);
    const barCol = pct >= 85 ? "#7FD07A" : pct >= 60 ? "#C9D06A" : pct >= 40 ? "#E0B04A" : pct >= 20 ? "#E08A46" : "#E0604A";
    html += `<div class="scard" style="border-left-color:${col}">
      <div class="sname" style="color:${col}"><span class="fdot" style="background:${col}"></span>${esc(f.name)}</div>
      <div class="srow"><span>В строю</span><b>${soldiers}</b></div>
      <div class="bar"><i style="width:${pct}%;background:${barCol}"></i></div>
      <div class="srow"><span>от начальных ${initial}</span><b>${pct}%</b></div>
      <div class="srow"><span>Отряды</span><b>${active.length} в бою${fled ? " · " + fled + " бежало" : ""}${dead ? " · " + dead + " уничтожено" : ""}</b></div>
      <div class="srow"><span>Средний БД / дисц</span><b>${avgM} / ${avgD}</b></div>
      <div class="srow"><span>Могут атаковать</span><b>${ready} из ${active.length}</b></div>
      <div class="srow"><span>Потери за кампанию</span><b>${k} уб. / ${wnd} ран.</b></div>
    </div>`;
  });
  box.innerHTML = html;
}
function renderQueue(){
  const box = $("queueBox");
  if(!box) return;
  const pending = units.filter(u => u.status === "active" && !u.acted);
  const done = units.filter(u => u.status === "active" && u.acted).length;
  if(!units.filter(u => u.status === "active").length){
    box.innerHTML = '<div class="hint">Активных отрядов нет.</div>';
    return;
  }
  if(!pending.length){
    box.innerHTML = `<div class="hint">Все отряды отходили в этом ходу (${done}). Можно завершать ход.</div>`;
    return;
  }
  box.innerHTML = `<div class="hint" style="margin:0 0 6px">Ещё не ходили: ${pending.length} · отходили: ${done}</div>` +
    pending.map(u => {
      const spent = (u.attacksMade || 0) >= attackLimit(u);
      return `<div class="qrow">
        <span class="qname" style="color:${strengthColor(u)}">${esc(u.name)} — ${u.soldiers}</span>
        <span style="color:var(--muted)">${esc(factionName(u.factionId))}${spent ? " · атаки исчерпаны" : ""}</span>
        <button class="sm gold" onclick="setSide('att',${u.id})">⚔</button>
        <button class="sm blue" onclick="setSide('def',${u.id})">🛡</button>
        <button class="sm" onclick="toggleActed(${u.id})">✓</button>
      </div>`;
    }).join("");
}
function renderAll(){
  $("turnNum").textContent = turn;
  renderFactions(); renderCmdrs(); renderUnits(); renderLog(); renderNecro(); renderTemplates();
  renderSummary(); renderQueue(); renderUndoBtn(); renderMap();
  if(ed.open) refreshEditor();
}

// ═══════════ перетаскивание ═══════════
let dragUid = null;
document.addEventListener("dragstart", e => {
  const card = e.target.closest && e.target.closest(".ucard");
  if(!card) return;
  dragUid = +card.dataset.uid;
  card.classList.add("dragging");
  e.dataTransfer.effectAllowed = "move";
  try{ e.dataTransfer.setData("text/plain", String(dragUid)); }catch(err){}
});
document.addEventListener("dragend", () => {
  document.querySelectorAll(".ucard").forEach(c => c.classList.remove("dragging"));
  clearDropMarks();
  dragUid = null;
});
function clearDropMarks(){
  document.querySelectorAll(".ucard").forEach(c => c.classList.remove("drop-before","drop-after"));
  document.querySelectorAll(".fhead,.shead").forEach(h => h.classList.remove("drop-target"));
}
document.addEventListener("dragover", e => {
  if(dragUid === null || !e.target.closest) return;
  const head = e.target.closest(".fhead,.shead");
  if(head && head.dataset.dropFac !== undefined){
    e.preventDefault();
    clearDropMarks();
    head.classList.add("drop-target");
    return;
  }
  const card = e.target.closest(".ucard");
  if(!card) return;
  const target = units.find(u => u.id === +card.dataset.uid);
  const dragged = units.find(u => u.id === dragUid);
  if(!target || !dragged || target.id === dragged.id) return;
  e.preventDefault();
  const rect = card.getBoundingClientRect();
  const before = e.clientY < rect.top + rect.height / 2;
  clearDropMarks();
  card.classList.add(before ? "drop-before" : "drop-after");
});
function reassign(dragged, facId, subId, note){
  const changed = (dragged.factionId || null) !== facId || (dragged.subfactionId || null) !== subId;
  if(changed){
    pushUndo(`перевод отряда «${dragged.name}»`);
    dragged.factionId = facId;
    dragged.subfactionId = subId;
    addLog(`«${dragged.name}» переведён`, [note], "info");
  }
  return changed;
}
document.addEventListener("drop", e => {
  if(dragUid === null || !e.target.closest) return;
  const dragged = units.find(u => u.id === dragUid);
  if(!dragged) return;

  const head = e.target.closest(".fhead,.shead");
  if(head && head.dataset.dropFac !== undefined){
    e.preventDefault();
    const facId = head.dataset.dropFac === "" ? null : +head.dataset.dropFac;
    const subId = head.dataset.dropSub === "" ? null : +head.dataset.dropSub;
    reassign(dragged, facId, subId,
      `Теперь в составе: ${facId === null ? "без фракции" : factionName(facId)}${subId ? " / " + subName(subId) : " / без подфракции"}`);
    dragUid = null; clearDropMarks();
    renderAll(); saveState();
    return;
  }

  const card = e.target.closest(".ucard");
  if(!card) return;
  const targetId = +card.dataset.uid;
  const target = units.find(u => u.id === targetId);
  if(!target || target.id === dragged.id) return;
  e.preventDefault();
  const rect = card.getBoundingClientRect();
  const before = e.clientY < rect.top + rect.height / 2;
  reassign(dragged, target.factionId || null, target.subfactionId || null,
    `Теперь в составе: ${target.factionId ? factionName(target.factionId) : "без фракции"}${target.subfactionId ? " / " + subName(target.subfactionId) : " / без подфракции"}`);
  units = units.filter(u => u.id !== dragUid);
  let idx = units.findIndex(u => u.id === targetId);
  if(!before) idx += 1;
  units.splice(idx, 0, dragged);
  dragUid = null; clearDropMarks();
  renderAll(); saveState();
});

// ═══════════ выбор сторон ═══════════
function facOptions(exceptId){
  return '<option value="">— выбрать фракцию —</option>' +
    factions.filter(f => exceptId === undefined || f.id !== exceptId)
      .map(f => `<option value="${f.id}">${esc(f.name)}</option>`).join("");
}
function subOptionsFor(facVal){
  if(facVal === "") return '<option value="">— сначала фракция —</option>';
  const fid = +facVal;
  const subs = subfactions.filter(x => x.factionId === fid);
  const hasLoose = units.some(u => u.factionId === fid && !u.subfactionId && canBeTargeted(u));
  return '<option value="">— выбрать подфракцию —</option>' +
    subs.map(x => `<option value="${x.id}">${esc(x.name)}</option>`).join("") +
    (hasLoose ? '<option value="none">Без подфракции</option>' : "");
}
function poolFor(facVal, subVal, side){
  if(facVal === "" || subVal === "") return [];
  const fid = +facVal;
  const ok = side === "def" ? canBeTargeted : (u => u.status === "active");
  return units.filter(u => ok(u) && u.factionId === fid &&
      (subVal === "none" ? !u.subfactionId : u.subfactionId === +subVal))
    .sort((a, b) => a.name.localeCompare(b.name, "ru"));
}
function renderSelects(){
  const attFac = $("attFac"), defFac = $("defFac"), attSub = $("attSub"), defSub = $("defSub");
  if(!attFac) return;
  const pfa = attFac.value, pfd = defFac.value, psa = attSub.value, psd = defSub.value;
  attFac.innerHTML = facOptions();
  attFac.value = pfa;
  // сторона Б не может быть той же фракцией, что и сторона А
  defFac.innerHTML = facOptions(attFac.value === "" ? undefined : +attFac.value);
  defFac.value = [...defFac.options].some(o => o.value === pfd) ? pfd : "";
  attSub.innerHTML = subOptionsFor(attFac.value); defSub.innerHTML = subOptionsFor(defFac.value);
  attSub.value = [...attSub.options].some(o => o.value === psa) ? psa : "";
  defSub.value = [...defSub.options].some(o => o.value === psd) ? psd : "";

  const att = $("attSel"), def = $("defSel");
  const pa = att.value, pd = def.value;
  const attPool = poolFor(attFac.value, attSub.value, "att");
  const defPool = poolFor(defFac.value, defSub.value, "def");
  att.innerHTML = '<option value="">— выбрать отряд —</option>' + attPool.map(u => {
    const spent = (u.attacksMade || 0) >= attackLimit(u);
    return `<option value="${u.id}" ${spent ? "disabled" : ""}>${esc(u.name)} (${u.soldiers})${spent ? " · уже атаковал" : ""}</option>`;
  }).join("");
  const attUnitId = +pa;
  def.innerHTML = '<option value="">— выбрать отряд —</option>' + defPool
    .filter(u => u.id !== attUnitId)
    .map(u => `<option value="${u.id}">${esc(u.name)} (${u.soldiers})${u.status === "fled" ? " · бежит" : ""}</option>`).join("");
  att.value = attPool.some(u => u.id === +pa) ? pa : "";
  def.value = defPool.some(u => u.id === +pd) && +pd !== attUnitId ? pd : "";

  const attReady = attSub.value !== "";
  const defReady = defSub.value !== "";
  $("attUnitBox").classList.toggle("hidden", !attReady);
  $("defUnitBox").classList.toggle("hidden", !defReady);
  $("battleOpts").classList.toggle("hidden", !(attReady && defReady));
  $("battleHint").textContent = (attReady && defReady)
    ? "1 атака на отряд за ход (2 при дисциплине 80+). После боя дравшимся ставится «Походил». Бои внутри одной фракции запрещены."
    : "Выбери фракцию и подфракцию для обеих сторон — списки отрядов и параметры боя появятся ниже.";
  updateChargeBox();
}
function setSide(side, unitId){
  const u = units.find(x => x.id === unitId); if(!u || !u.factionId) {
    if(u && !u.factionId) alert("У отряда нет фракции — назначь её, чтобы выставить в бой.");
    return;
  }
  const fac = side === "att" ? $("attFac") : $("defFac");
  const sub = side === "att" ? $("attSub") : $("defSub");
  const sel = side === "att" ? $("attSel") : $("defSel");
  if(side === "att"){
    const dFac = $("defFac");
    if(dFac.value === String(u.factionId)){ dFac.value = ""; $("defSub").value = ""; $("defSel").value = ""; }
  }
  fac.value = String(u.factionId);
  renderSelects();
  sub.value = u.subfactionId ? String(u.subfactionId) : "none";
  renderSelects();
  sel.value = String(u.id);
  updateChargeBox();
  const panel = $("battlePanel");
  if(panel && panel.scrollIntoView) panel.scrollIntoView({block: "nearest"});
}
function onSideChange(){ renderSelects(); }
function onModeChange(){
  const m = $("modeSel").value;
  $("sitBox").classList.toggle("hidden", isMeleeMode(m));
  updateChargeBox();
}
function updateChargeBox(){
  const A = units.find(u => u.id === +$("attSel").value);
  const B = units.find(u => u.id === +$("defSel").value);
  const melee = isMeleeMode($("modeSel").value);
  const showA = !!A && A.type === "cavalry" && melee;
  $("chargeBox").classList.toggle("hidden", !showA);
  if(!showA) $("charge").checked = false;
  const showB = !!B && B.type === "cavalry" && melee && B.status === "active";
  $("counterChargeBox").classList.toggle("hidden", !showB);
  if(!showB) $("counterCharge").checked = false;
}

// ═══════════ боевая математика ═══════════

// ═══════════ бой ═══════════
function resolveBattle(){
  const A = units.find(u => u.id === +$("attSel").value);
  const B = units.find(u => u.id === +$("defSel").value);
  const req = {
    mode: $("modeSel").value,
    sitPct: $("sitPct").value,
    fatigueMode: $("fatigueMode").value,
    mutual: $("mutual").checked,
    charge: !$("chargeBox").classList.contains("hidden") && $("charge").checked,
    counterCharge: !$("counterChargeBox").classList.contains("hidden") && $("counterCharge").checked,
  };
  const onMapBoth = A && B && A.onMap && B.onMap;
  let mm = null;
  const blankMods = () => ({ab: {}, ba: {}, notes: [], noCharge: null});
  if(terrainActive() && onMapBoth){
    mm = Engine.mapModsFor(A, B, mapGeo(), currentRules());
    if(mm && mm.mode){ req.mode = (isMeleeMode(req.mode) ? "melee_" : "ranged_") + mm.mode; $("modeSel").value = req.mode; }
  }
  if(rangeActive() && onMapBoth){
    const reach = Engine.attackReach(A, B, isMeleeMode(req.mode), mapGeo(), currentRules());
    if(!reach.ok){
      if(!confirm(`${reach.text}.\n\nЦель вне досягаемости. Провести атаку всё равно — решение мастера?`)) return;
      mm = mm || blankMods();
      mm.notes.push(`⚠ Вне досягаемости: ${reach.text} — решение мастера · черновик`);
    }
  }
  if(moveActive() && onMapBoth && req.charge && A.type === "cavalry"){
    const block = Engine.runUpBlock(A, currentRules());
    if(block){ mm = mm || blankMods(); mm.noCharge = mm.noCharge || block; }
  }
  if(mm) req.mapMods = mm;
  const r = Engine.resolveBattle(A, B, req, engineCtx());
  if(!r.ok){ addLog(r.title, r.lines, r.tone); return; }
  pushUndo(`бой ${A.name} → ${B.name}`);
  r.patches.forEach(p => updUnit(p.id, p.patch));
  if(moveActive() && A.onMap) updUnit(A.id, {runUpM: 0});   // разбег истрачен на удар
  addLog(r.title, r.lines, r.tone);
  if($("counterCharge")) $("counterCharge").checked = false;
  renderAll(); saveState();
}

// ═══════════ проверки ═══════════
function moraleCheck(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`проверка БД: ${u.name}`);
  const r = Engine.moraleCheck(u, engineCtx());
  updUnit(u.id, r.patch);
  addLog(r.title, r.lines, r.tone === "info" ? undefined : r.tone);
  renderAll(); saveState();
}
function fleeCheck(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`проверка на побег: ${u.name}`);
  const r = Engine.fleeCheck(u, engineCtx());
  updUnit(u.id, r.patch);
  addLog(r.title, r.lines, r.tone === "info" ? undefined : r.tone);
  if(r.patch.status === "fled") runPanic([u.id]);
  renderAll(); saveState();
}

// ═══════════ конец хода ═══════════
function endTurn(){
  pushUndo(`конец хода ${turn}`);
  const wasActive = new Set(units.filter(u => u.status === "active").map(u => u.id));
  const r = Engine.endTurn(units, engineCtx());
  units = r.units.map(u => (u.movedM || u.runUpM) ? Object.assign({}, u, {movedM: 0, runUpM: 0}) : u);
  turn += 1;
  $("turnNum").textContent = turn;
  addLog(`— Конец хода ${turn - 1} —`, r.lines.length ? r.lines : ["Без изменений"]);
  // побег без броска (дисциплина иссякла) тоже запускает волну
  runPanic(units.filter(u => u.status === "fled" && wasActive.has(u.id)).map(u => u.id));
  renderAll(); saveState();
}

// ═══════════ некролог ═══════════
function necroText(){
  const lines = [];
  lines.push(`НЕКРОЛОГ КАМПАНИИ — ход ${turn}`);
  lines.push("═".repeat(46));
  const facGroups = [...factions, {id: null, name: "Без фракции"}];
  let grandK = 0, grandW = 0;
  facGroups.forEach(f => {
    const fu = units.filter(u => (u.factionId || null) === f.id);
    if(!fu.length) return;
    const k = fu.reduce((s,u) => s + (u.totKilled||0), 0);
    const w = fu.reduce((s,u) => s + (u.totWounded||0), 0);
    grandK += k; grandW += w;
    lines.push("");
    lines.push(`▪ ${f.name}: убито ${k}, ранено ${w}, всего потерь ${k+w}`);
    const subGroups = [...subfactions.filter(x => x.factionId === f.id), {id: null, name: "Без подфракции"}];
    subGroups.forEach(sf => {
      const su = fu.filter(u => (u.subfactionId || null) === sf.id);
      if(!su.length) return;
      const sk = su.reduce((s,u) => s + (u.totKilled||0), 0);
      const sw = su.reduce((s,u) => s + (u.totWounded||0), 0);
      lines.push(`  ↳ ${sf.name}: убито ${sk}, ранено ${sw}`);
      su.forEach(u => {
        const mark = u.status === "destroyed" ? " [УНИЧТОЖЕН]" : u.status === "fled" ? " [бежал]" : "";
        lines.push(`      ${u.name}${mark}: ${u.soldiers}/${u.initial} в строю · убито ${u.totKilled||0} · ранено ${u.totWounded||0}`);
      });
    });
  });
  const withCmdr = commanders.filter(c => units.some(u => u.commanderId === c.id));
  if(withCmdr.length){
    lines.push("");
    lines.push("ПО ПОЛКОВОДЦАМ");
    lines.push("─".repeat(46));
    withCmdr.forEach(c => {
      const cu = units.filter(u => u.commanderId === c.id);
      const k = cu.reduce((s,u) => s + (u.totKilled||0), 0);
      const w = cu.reduce((s,u) => s + (u.totWounded||0), 0);
      lines.push(`⚜ ${c.name} (${factionName(c.factionId)}): убито ${k}, ранено ${w}, отрядов ${cu.length}`);
    });
  }
  lines.push("");
  lines.push("═".repeat(46));
  lines.push(`ИТОГО ПО ВСЕМ АРМИЯМ: убито ${grandK}, ранено ${grandW}, всего ${grandK + grandW}`);
  return lines.join("\n");
}
function renderNecro(){
  const b = $("necroBox");
  if(b) b.textContent = units.length ? necroText() : "Потерь пока нет — армии не сформированы.";
}
function downloadNecro(){
  downloadText(`nekrolog_hod${turn}.txt`, necroText());
  addLog("Некролог выгружен в TXT", [`Файл: nekrolog_hod${turn}.txt`]);
}

// ═══════════ карта боя ═══════════
function loadMapImage(ev){
  const file = ev.target.files[0];
  if(!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    const img = new Image();
    img.onload = () => {
      // уменьшаем большие карты, чтобы они помещались в автосохранение
      const MAXW = 1600;
      let w = img.width, h = img.height;
      if(w > MAXW){ h = Math.round(h * MAXW / w); w = MAXW; }
      try{
        const cv = document.createElement("canvas");
        cv.width = w; cv.height = h;
        cv.getContext("2d").drawImage(img, 0, 0, w, h);
        mapImage = cv.toDataURL("image/jpeg", 0.82);
      }catch(err){
        mapImage = reader.result;
      }
      imageAspect = h / w;
      pushUndo("загрузка карты боя");
      renderMap(); saveState();
      addLog("Карта боя загружена", [`Файл: ${file.name} · ${w}×${h}`]);
    };
    img.onerror = () => alert("Не удалось прочитать изображение.");
    img.src = reader.result;
  };
  reader.readAsDataURL(file);
  ev.target.value = "";
}
function clearMapImage(){
  if(!mapImage) return;
  if(!confirm("Убрать изображение карты? Фишки останутся на своих местах.")) return;
  pushUndo("удаление карты боя");
  mapImage = null;
  renderMap(); saveState();
}
function placeOnMap(id, x, y){
  const u = units.find(z => z.id === id); if(!u) return;
  updUnit(id, {onMap: true, mapX: x !== undefined ? x : 50, mapY: y !== undefined ? y : 50});
  renderMap(); renderUnits(); saveState();
}
function removeFromMap(id){
  const u = units.find(z => z.id === id); if(!u) return;
  updUnit(id, {onMap: false});
  renderMap(); renderUnits(); saveState();
}
function clearTokens(){
  if(!units.some(u => u.onMap)) return;
  if(!confirm("Снять с карты все фишки?")) return;
  pushUndo("очистка фишек с карты");
  units = units.map(u => Object.assign({}, u, {onMap: false}));
  renderMap(); renderUnits(); saveState();
}
function isValidTarget(att, def){
  return canBeTargeted(def) && att.status === "active" && def.id !== att.id
      && def.factionId && att.factionId && def.factionId !== att.factionId;
}
function mapMode(att){
  if(mapModeOverride === "auto") return (att.weapon === "ranged" ? "ranged_" : "melee_") + readMapOpts().terrain;
  if(mapModeOverride) return mapModeOverride;
  const terrain = $("mapTerrain") ? $("mapTerrain").value : "form";
  return (att.weapon === "ranged" ? "ranged_" : "melee_") + terrain;
}
function tokenMenuHtml(u){
  const st = moraleStage(u.morale);
  const canAttack = u.status === "active" && (u.attacksMade || 0) < attackLimit(u);
  const ranged = u.weapon === "ranged";
  const formMode = ranged ? "ranged_form" : "melee_form";
  const roughMode = ranged ? "ranged_rough" : "melee_rough";
  let items = "";
  if(u.status === "active"){
    if(canAttack && terrainActive()){
      items += `<button class="gold" onclick="startTargeting(${u.id}, false, 'auto')">⚔ Атаковать · режим по местности</button>`;
      if(u.type === "cavalry" && !ranged)
        items += `<button class="gold" onclick="startTargeting(${u.id}, true, 'auto')">🐎 Натиск · режим по местности</button>`;
    } else if(canAttack){
      items += `<button class="gold" onclick="startTargeting(${u.id}, false, '${formMode}')">⚔ ${esc(MODES[formMode])}</button>`;
      items += `<button class="gold" onclick="startTargeting(${u.id}, false, '${roughMode}')">⚔ ${esc(MODES[roughMode])}</button>`;
      if(u.type === "cavalry" && !ranged){
        items += `<button class="gold" onclick="startTargeting(${u.id}, true, 'melee_form')">🐎 Натиск · в строю</button>`;
        items += `<button class="gold" onclick="startTargeting(${u.id}, true, 'melee_rough')">🐎 Натиск · пересечённая</button>`;
      }
    } else {
      items += `<button disabled>⚔ Атаки исчерпаны (${u.attacksMade}/${attackLimit(u)})</button>`;
    }
    items += `<button class="${u.acted ? "green" : ""}" onclick="menuAct(${u.id})">${u.acted ? "✓ Походил — отменить" : "✓ Отметить: походил"}</button>`;
    if(u.morale > 0 && u.morale <= 40)
      items += `<button class="red" onclick="menuCheck('morale',${u.id})">⚑ Проверка БД (${u.morale})</button>`;
    if(u.morale === 0)
      items += `<button class="red" onclick="menuCheck('flee',${u.id})">🏃 Ролл на побег</button>`;
    items += `<button class="red" onclick="menuFled(${u.id})">🏳 Отметить: сбежал</button>`;
  } else if(u.status === "fled"){
    items += `<button class="blue" onclick="menuRally(${u.id})">✦ Воспряли духом</button>`;
  } else {
    items += `<button disabled>☠ Отряд уничтожен</button>`;
  }
  items += `<div style="display:flex;gap:5px;margin-bottom:5px">
      <button style="flex:1" onclick="rotateUnit(${u.id},-45);openTokenMenu(${u.id})">↺ 45°</button>
      <button style="flex:1" onclick="setFacing(${u.id},0);openTokenMenu(${u.id})">↑ 0°</button>
      <button style="flex:1" onclick="rotateUnit(${u.id},45);openTokenMenu(${u.id})">↻ 45°</button>
    </div>`;
  if(!mapActive()) items += `<div style="display:flex;gap:5px;margin-bottom:5px;align-items:center">
      <button style="flex:1" onclick="scaleToken(${u.id},0.8);openTokenMenu(${u.id})">− Меньше</button>
      <button style="flex:1" onclick="resetTokenScale(${u.id});openTokenMenu(${u.id})">${Math.round((u.tokenScale || 1) * 100)}%</button>
      <button style="flex:1" onclick="scaleToken(${u.id},1.25);openTokenMenu(${u.id})">+ Больше</button>
    </div>`;
  items += `<button onclick="menuEdit(${u.id})">✎ Открыть карточку</button>`;
  items += `<button onclick="menuOff(${u.id})">✕ Снять с карты</button>`;
  items += `<button onclick="closeTokenMenu()">Закрыть</button>`;
  const left = clamp(u.mapX, 6, 82), top = clamp(u.mapY + 4, 0, 88);
  return `<div class="tmenu" style="left:${left}%;top:${top}%" onclick="event.stopPropagation()">
    <div class="tm-head">${esc(u.name)}</div>
    <div class="tm-sub">${esc(factionName(u.factionId))} · ${esc(TYPE_NAMES[u.type] || "Пехота")} · ${u.soldiers}/${u.initial} солдат<br>
      БД ${u.morale} (${esc(st.label)}) · дисц ${u.discipline} · усталость ${u.fatigue} · фасинг ${Math.round(u.facing || 0)}° · размер ${Math.round((u.tokenScale || 1) * 100)}%</div>
    ${items}</div>`;
}
function openTokenMenu(id){
  openMenuId = id;
  renderMap();
}
function closeTokenMenu(){
  openMenuId = null;
  renderMap();
}
function startTargeting(id, charge, mode){
  openMenuId = null;
  mapAttackerId = id;
  mapCharge = !!charge;
  mapModeOverride = mode || null;
  setSide("att", id);
  renderMap();
}
function cancelTargeting(){
  mapAttackerId = null; mapCharge = false; mapModeOverride = null;
  renderMap();
}
function scaleToken(id, factor){
  const u = units.find(z => z.id === id); if(!u) return;
  const cur = u.tokenScale || 1;
  const next = Math.round(clamp(cur * factor, 0.4, 3) * 100) / 100;
  updUnit(id, {tokenScale: next});
  renderMap(); saveState();
}
function resetTokenScale(id){
  updUnit(id, {tokenScale: 1});
  renderMap(); saveState();
}
function selectedIds(){
  return Object.keys(selectedTokens).map(Number)
    .filter(id => { const u = units.find(z => z.id === id); return u && u.onMap; });
}
function clearSelection(){
  selectedTokens = {};
  renderMap();
}
function renderSelBar(){
  const bar = $("selBar"); if(!bar) return;
  const ids = selectedIds();
  if(ids.length < 1){ bar.classList.add("hidden"); bar.innerHTML = ""; return; }
  bar.classList.remove("hidden");
  const men = ids.reduce((s, id) => s + (units.find(u => u.id === id) || {soldiers:0}).soldiers, 0);
  bar.innerHTML = `<span class="selcount">Выделено: ${ids.length} отр · ${men} чел</span>
    <button class="sm" onclick="groupRotate(-45)">↺ 45°</button>
    <button class="sm" onclick="groupRotate(45)">↻ 45°</button>
    <button class="sm" onclick="groupFacing(0)">↑ 0°</button>
    <button class="sm" onclick="groupScale(0.8)">− Меньше</button>
    <button class="sm" onclick="groupScale(1.25)">+ Больше</button>
    <button class="sm" onclick="groupLine()">⇔ Выстроить в линию</button>
    <button class="sm green" onclick="groupActed(true)">✓ Походили</button>
    <button class="sm" onclick="groupActed(false)">✗ Не ходили</button>
    <button class="sm red" onclick="groupOffMap()">✕ Снять с карты</button>
    <button class="sm" onclick="clearSelection()">Снять выделение</button>`;
}
function groupRotate(delta){
  const ids = selectedIds(); if(!ids.length) return;
  pushUndo(`поворот группы (${ids.length} отр.)`);
  ids.forEach(id => {
    const u = units.find(z => z.id === id);
    let f = ((u.facing || 0) + delta) % 360; if(f < 0) f += 360;
    updUnit(id, {facing: f});
  });
  renderMap(); saveState();
}
function groupFacing(deg){
  const ids = selectedIds(); if(!ids.length) return;
  pushUndo(`разворот группы на ${deg}°`);
  ids.forEach(id => updUnit(id, {facing: ((deg % 360) + 360) % 360}));
  renderMap(); saveState();
}
function groupScale(factor){
  const ids = selectedIds(); if(!ids.length) return;
  ids.forEach(id => {
    const u = units.find(z => z.id === id);
    const next = Math.round(clamp((u.tokenScale || 1) * factor, 0.4, 3) * 100) / 100;
    updUnit(id, {tokenScale: next});
  });
  renderMap(); saveState();
}
function groupActed(val){
  const ids = selectedIds(); if(!ids.length) return;
  pushUndo(`отметка хода группы (${ids.length} отр.)`);
  ids.forEach(id => updUnit(id, {acted: !!val}));
  renderAll(); saveState();
}
function groupOffMap(){
  const ids = selectedIds(); if(!ids.length) return;
  if(!confirm(`Снять с карты ${ids.length} отрядов?`)) return;
  pushUndo(`снятие группы с карты (${ids.length} отр.)`);
  ids.forEach(id => updUnit(id, {onMap: false}));
  selectedTokens = {};
  renderAll(); saveState();
}
function groupLine(){
  const ids = selectedIds(); if(ids.length < 2) return;
  pushUndo(`построение группы в линию (${ids.length} отр.)`);
  const list = ids.map(id => units.find(u => u.id === id));
  const cx = list.reduce((s,u) => s + u.mapX, 0) / list.length;
  const cy = list.reduce((s,u) => s + u.mapY, 0) / list.length;
  const facing = list[0].facing || 0;
  const rad = (facing + 90) * Math.PI / 180;      // линия перпендикулярна направлению взгляда
  const step = 6;
  const sorted = list.slice().sort((a,b) => (a.mapX - b.mapX) || (a.mapY - b.mapY));
  sorted.forEach((u, i) => {
    const off = (i - (sorted.length - 1) / 2) * step;
    updUnit(u.id, {
      mapX: clamp(cx + Math.sin(rad) * off, 2, 98),
      mapY: clamp(cy - Math.cos(rad) * off, 2, 98),
    });
  });
  renderMap(); saveState();
}
function rotateUnit(id, delta){
  const u = units.find(z => z.id === id); if(!u) return;
  let f = ((u.facing || 0) + delta) % 360;
  if(f < 0) f += 360;
  updUnit(id, {facing: f});
  renderMap(); saveState();
}
function setFacing(id, deg){
  updUnit(id, {facing: ((deg % 360) + 360) % 360});
  renderMap(); saveState();
}
function menuAct(id){
  openMenuId = null;
  toggleActed(id);
  renderMap();
}
function menuCheck(kind, id){
  openMenuId = null;
  if(kind === "morale") moraleCheck(id); else fleeCheck(id);
}
function menuFled(id){ openMenuId = null; markFled(id); }
function menuRally(id){ openMenuId = null; ralliedUnit(id); }
function menuOff(id){ openMenuId = null; removeFromMap(id); }
function menuEdit(id){ openMenuId = null; startEdit(id); renderMap(); }
function mapAttack(targetId){
  const att = units.find(u => u.id === mapAttackerId);
  const def = units.find(u => u.id === targetId);
  if(!att || !def || !isValidTarget(att, def)){ cancelTargeting(); return; }
  const mode = mapMode(att);
  setSide("att", att.id);
  setSide("def", def.id);
  $("modeSel").value = mode;
  onModeChange();
  updateChargeBox();
  if(mapCharge && !$("chargeBox").classList.contains("hidden")) $("charge").checked = true;
  updateChargeBox();
  if(mapCharge && !$("chargeBox").classList.contains("hidden")) $("charge").checked = true;
  if(!$("counterChargeBox").classList.contains("hidden") && def.type === "cavalry"
     && def.status === "active" && isMeleeMode(mode)){
    if(confirm(`«${def.name}» — кавалерия. Идёт ли она во встречный натиск?\n\nДа — обе стороны бьют с натиском (+50% каждой), Б отвечает ударом.\nНет — ${mapCharge ? "цель принимает удар и не отвечает" : "обычный ответный удар"}.`)){
      $("counterCharge").checked = true;
    }
  }
  mapAttackerId = null; mapCharge = false; mapModeOverride = null;
  resolveBattle();
  if($("charge")) $("charge").checked = false;
  renderMap();
}
function tokenClick(id){
  if(mapAttackerId){
    if(id === mapAttackerId){ cancelTargeting(); return; }
    const att = units.find(u => u.id === mapAttackerId);
    const def = units.find(u => u.id === id);
    if(att && def && isValidTarget(att, def)) mapAttack(id);
    else cancelTargeting();
    return;
  }
  openTokenMenu(id);
}
// ═══════════ вид карты: приближение, сдвиг, местность (К20, К30) ═══════════
// Карта — «окно» (#mapWrap) и «сцена» внутри него: сцена = окно × приближение. Фишки стоят на сцене
// в процентах, как и раньше, поэтому вся логика перетаскивания и выделения не меняется.
// Местность рисуется холстом размером с окно между картинкой и фишками: фишки и меню не раздуваются.
let terrainMap = null;       // местность партии: Engine.createTerrain / deserializeTerrain
let terrainVersion = 0;      // растёт при каждой правке — по нему перестраивается растр
let imageAspect = null;      // высота / ширина загруженной картинки
let spaceDown = false;       // пробел зажат — левая кнопка сдвигает карту
const ZOOM_MAX = 40;

// Цвет и узор каждого вида местности. Что клетка значит в бою — не здесь, а в движке и rules.js.
const TERRAIN_STYLE = {
  field:    {c: "#6F7F4A"},
  road:     {c: "#A08D63"},
  sand:     {c: "#CDB27A", p: "stipple", pc: "#A48A56"},
  snow:     {c: "#DCE3E6", p: "stipple", pc: "#FFFFFF"},
  shrub:    {c: "#5D7A3E", p: "bush",    pc: "#3C5626"},
  forest:   {c: "#35583A", p: "tree",    pc: "#1C3620"},
  water:    {c: "#2E5A7A", p: "wave",    pc: "#79A8C6"},
  ford:     {c: "#5C8CA8", p: "wave",    pc: "#A4CAE0"},
  bridge:   {c: "#8A6A45", p: "plank",   pc: "#5A4226"},
  swamp:    {c: "#4E5E3E", p: "reed",    pc: "#26321C"},
  rocks:    {c: "#716C66", p: "rock",    pc: "#46423E"},
  wall:     {c: "#8E8E8A", p: "brick",   pc: "#55554F"},
  gate:     {c: "#7A5230", p: "plank",   pc: "#42290F"},
  tower:    {c: "#A8A8A2", p: "brick",   pc: "#63635D"},
  palisade: {c: "#7A5A38", p: "stake",   pc: "#43301B"},
  moat:     {c: "#3B4C58", p: "wave",    pc: "#62808F"},
  trench:   {c: "#6B5B45", p: "dash",    pc: "#3E3426"},
  building: {c: "#8A4B3A", p: "roof",    pc: "#56291F"},
  pavement: {c: "#8E8878", p: "stipple", pc: "#666052"},
  breach:   {c: "#6E5E50", p: "rock",    pc: "#443A31"},
};
const EMPTY_RGB = [32, 40, 42];
const HEIGHT_RGB = [124, 104, 74];
const hexRgb = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const heightColor = z => ["#6F7F4A", "#86925C", "#9EA56E", "#B8B982"][z] || "#6F7F4A";

function makeView(ids){ return {ids, z: 1, ox: 0, oy: 0, vw: 0, vh: 0, bw: 0, bh: 0, sw: 0, sh: 0}; }
const battleView = makeView({wrap: "mapWrap", stage: "mapStage", top: "mapTop", canvas: "terrainCanvas"});
const editorView = makeView({wrap: "edWrap", stage: "edStage", top: "edTop", canvas: "edCanvas"});

const hasMapSurface = () => !!mapImage || !!terrainMap;
function mapAspect(){
  if(mapImage) return imageAspect || (terrainMap ? terrainMap.h / terrainMap.w : 0.6);
  return terrainMap ? terrainMap.h / terrainMap.w : 0.6;
}
// Размеры окна и сцены; для поля боя высота окна = ширина × пропорции карты
function layoutView(v){
  const wrap = $(v.ids.wrap); if(!wrap) return;
  const full = v === editorView;
  const aspect = mapAspect();
  v.vw = wrap.clientWidth || 800;
  if(full) v.vh = wrap.clientHeight || 600;
  else {
    v.vh = Math.round(v.vw * aspect);
    wrap.style.height = hasMapSurface() ? v.vh + "px" : "";
  }
  // при приближении 1 карта целиком вписана в окно
  v.bw = Math.min(v.vw, v.vh / aspect); v.bh = v.bw * aspect;
  v.sw = v.bw * v.z; v.sh = v.bh * v.z;
  clampView(v);
}
function clampView(v){
  v.ox = v.sw <= v.vw ? (v.vw - v.sw) / 2 : Math.min(0, Math.max(v.vw - v.sw, v.ox));
  v.oy = v.sh <= v.vh ? (v.vh - v.sh) / 2 : Math.min(0, Math.max(v.vh - v.sh, v.oy));
}
function applyView(v){
  if(v === battleView) drawReach();
  const geo = `left:${v.ox}px;top:${v.oy}px;width:${v.sw}px;height:${v.sh}px`;
  [v.ids.stage, v.ids.top].forEach(id => { const el = $(id); if(el) el.style.cssText = hasMapSurface() || v === editorView ? geo : ""; });
  drawTerrain(v);
  const zl = v === battleView ? $("mapZoom") : null;
  if(zl){ zl.classList.toggle("hidden", v.z <= 1.001); zl.textContent = "×" + (Math.round(v.z * 10) / 10); }
}
// Приближение к точке (cx, cy — пиксели окна): точка карты под курсором остаётся на месте
function zoomView(v, factor, cx, cy){
  const z2 = clamp(v.z * factor, 1, ZOOM_MAX);
  if(z2 === v.z) return;
  if(cx === undefined){ cx = v.vw / 2; cy = v.vh / 2; }
  const fx = (cx - v.ox) / v.sw, fy = (cy - v.oy) / v.sh;
  v.z = z2; v.sw = v.bw * z2; v.sh = v.bh * z2;
  v.ox = cx - fx * v.sw; v.oy = cy - fy * v.sh;
  clampView(v); applyView(v);
}
function panView(v, dx, dy){ v.ox += dx; v.oy += dy; clampView(v); applyView(v); }
function zoomBattle(f){ if(hasMapSurface()) zoomView(battleView, f); }
function fitBattle(){ battleView.z = 1; layoutView(battleView); applyView(battleView); }

// ── растр местности: одна точка на клетку, высота светлее, край холма темнее ──
let canvasBroken = false;   // нет холста (например, в проверке без браузера) — местность просто не рисуется
function canvasCtx(cv){
  if(canvasBroken || !cv) return null;
  try{ const c = cv.getContext("2d"); if(!c) canvasBroken = true; return c; }
  catch(e){ canvasBroken = true; return null; }
}
let terrainCache = {key: "", cv: null};
function terrainRaster(){
  const m = terrainMap; if(!m) return null;
  const key = terrainVersion + "|" + !!mapImage;
  if(terrainCache.key === key && terrainCache.map === m) return terrainCache.cv;
  const cv = document.createElement("canvas");
  cv.width = m.w; cv.height = m.h;
  const ctx = canvasCtx(cv); if(!ctx) return null;
  const img = ctx.createImageData(m.w, m.h), d = img.data;
  const rgb = [];
  Engine.TERRAIN.forEach(t => { rgb[t.id] = hexRgb(TERRAIN_STYLE[t.key].c); });
  const see = !!mapImage;   // поверх картинки «не задано» прозрачно
  for(let y = 0; y < m.h; y++) for(let x = 0; x < m.w; x++){
    const i = y * m.w + x, t = m.t[i], z = m.z[i], o = i * 4;
    const c = t ? rgb[t] : z ? HEIGHT_RGB : EMPTY_RGB;
    let k = 1 + 0.1 * z;
    if(z && ((x > 0 && m.z[i - 1] < z) || (x < m.w - 1 && m.z[i + 1] < z) ||
             (y > 0 && m.z[i - m.w] < z) || (y < m.h - 1 && m.z[i + m.w] < z))) k *= 0.6;   // горизонталь
    d[o] = Math.min(255, c[0] * k); d[o + 1] = Math.min(255, c[1] * k); d[o + 2] = Math.min(255, c[2] * k);
    d[o + 3] = t ? 255 : z ? (see ? 130 : 255) : (see ? 0 : 255);
  }
  ctx.putImageData(img, 0, 0);
  terrainCache = {key, map: m, cv};
  return cv;
}
function hash2(x, y){
  let h = Math.imul(x, 73856093) ^ Math.imul(y, 19349663);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return (h ^ (h >>> 16)) >>> 0;
}
function drawTerrain(v){
  const cv = $(v.ids.canvas); if(!cv) return;
  const ctx = canvasCtx(cv); if(!ctx) return;
  const dpr = window.devicePixelRatio || 1;
  const W = Math.round(v.vw * dpr), H = Math.round(v.vh * dpr);
  if(cv.width !== W || cv.height !== H){ cv.width = W; cv.height = H; }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, v.vw, v.vh);
  const r = terrainRaster();
  if(r){
    const m = terrainMap, cellPx = v.sw / m.w;
    ctx.imageSmoothingEnabled = cellPx >= 1.5 && cellPx < 6;   // издали — мягкие края, вблизи — чёткие клетки
    ctx.globalAlpha = mapImage ? 0.6 : 1;
    ctx.drawImage(r, v.ox, v.oy, v.sw, v.sh);
    ctx.globalAlpha = mapImage ? 0.75 : 1;
    drawPatterns(ctx, v, cellPx);
    ctx.globalAlpha = 1;
  }
  if(v === editorView) drawUnitDots(ctx, v);
}
// Узор с постоянной плотностью на экране (~ каждые 13 px), привязан к клеткам — при сдвиге не «плывёт»
function drawPatterns(ctx, v, cellPx){
  const m = terrainMap;
  const s = Math.max(1, Math.ceil(13 / cellPx));
  const size = Math.min(s * cellPx * 0.42, 9);
  if(size < 1.3) return;
  const x0 = Math.max(0, Math.floor(-v.ox / cellPx / s) * s), x1 = Math.min(m.w, Math.ceil((v.vw - v.ox) / cellPx));
  const y0 = Math.max(0, Math.floor(-v.oy / cellPx / s) * s), y1 = Math.min(m.h, Math.ceil((v.vh - v.oy) / cellPx));
  for(let y = y0; y < y1; y += s) for(let x = x0; x < x1; x += s){
    const h = hash2(x, y);
    const jx = (h & 1023) / 1024 * s, jy = ((h >>> 10) & 1023) / 1024 * s;
    const cx = Math.min(m.w - 1, Math.floor(x + jx)), cy = Math.min(m.h - 1, Math.floor(y + jy));
    const t = m.t[cy * m.w + cx]; if(!t) continue;
    const st = TERRAIN_STYLE[Engine.TERRAIN_BY_ID[t].key]; if(!st.p) continue;
    drawSymbol(ctx, st.p, v.ox + (x + jx) * cellPx, v.oy + (y + jy) * cellPx, size, st.pc, h, cellPx);
  }
}
function drawSymbol(ctx, p, x, y, s, col, h, cellPx){
  ctx.fillStyle = col; ctx.strokeStyle = col;
  ctx.lineWidth = Math.max(0.8, s * 0.14);
  ctx.beginPath();
  switch(p){
    case "tree":
      ctx.arc(x, y, s * 0.55, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = "rgba(255,255,255,.12)"; ctx.beginPath(); ctx.arc(x - s * 0.15, y - s * 0.15, s * 0.25, 0, Math.PI * 2); ctx.fill();
      return;
    case "bush":
      ctx.arc(x - s * 0.2, y, s * 0.3, 0, Math.PI * 2); ctx.arc(x + s * 0.22, y + s * 0.1, s * 0.26, 0, Math.PI * 2); ctx.fill();
      return;
    case "stipple": {
      const r = Math.max(0.6, s * 0.11);
      [[0, 0], [0.5, 0.3], [-0.4, 0.45]].forEach(([a, b]) => { ctx.moveTo(x + a * s + r, y + b * s); ctx.arc(x + a * s, y + b * s, r, 0, Math.PI * 2); });
      ctx.fill(); return;
    }
    case "wave":
      ctx.moveTo(x - s * 0.6, y); ctx.quadraticCurveTo(x - s * 0.3, y - s * 0.35, x, y); ctx.quadraticCurveTo(x + s * 0.3, y + s * 0.35, x + s * 0.6, y);
      ctx.stroke(); return;
    case "reed":
      for(const k of [-0.35, 0, 0.35]){ ctx.moveTo(x + k * s, y + s * 0.3); ctx.lineTo(x + k * s + s * 0.08, y - s * 0.35); }
      ctx.stroke(); return;
    case "rock":
      ctx.moveTo(x - s * 0.45, y + s * 0.3); ctx.lineTo(x, y - s * 0.4); ctx.lineTo(x + s * 0.45, y + s * 0.3); ctx.closePath();
      ctx.fill(); return;
    case "brick":
      if(cellPx < 2.5) return;
      ctx.moveTo(x - s * 0.5, y); ctx.lineTo(x + s * 0.5, y); ctx.moveTo(x + ((h & 1) ? -0.2 : 0.2) * s, y - s * 0.35); ctx.lineTo(x + ((h & 1) ? -0.2 : 0.2) * s, y);
      ctx.stroke(); return;
    case "plank":
      ctx.moveTo(x - s * 0.5, y - s * 0.2); ctx.lineTo(x + s * 0.5, y - s * 0.2); ctx.moveTo(x - s * 0.5, y + s * 0.2); ctx.lineTo(x + s * 0.5, y + s * 0.2);
      ctx.stroke(); return;
    case "stake":
      ctx.moveTo(x, y + s * 0.35); ctx.lineTo(x, y - s * 0.35); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(x - s * 0.15, y - s * 0.3); ctx.lineTo(x, y - s * 0.5); ctx.lineTo(x + s * 0.15, y - s * 0.3); ctx.fill();
      return;
    case "dash":
      ctx.moveTo(x - s * 0.45, y); ctx.lineTo(x + s * 0.45, y); ctx.stroke(); return;
    case "roof":
      ctx.moveTo(x - s * 0.45, y + s * 0.3); ctx.lineTo(x, y - s * 0.3); ctx.lineTo(x + s * 0.45, y + s * 0.3); ctx.stroke(); return;
  }
}
function drawUnitDots(ctx, v){
  units.filter(u => u.onMap && u.status !== "destroyed").forEach(u => {
    ctx.beginPath();
    ctx.arc(v.ox + u.mapX / 100 * v.sw, v.oy + u.mapY / 100 * v.sh, 4, 0, Math.PI * 2);
    ctx.fillStyle = factionColor(u.factionId); ctx.fill();
    ctx.lineWidth = 1; ctx.strokeStyle = "#14181A"; ctx.stroke();
  });
}
// Местность под фишкой — для подсказки на карте
function terrainUnder(u){
  if(!terrainMap) return null;
  const c = Engine.cellAt(terrainMap, u.mapX / 100, u.mapY / 100);
  return c.t || c.z ? Engine.terrainName(c.t) + (c.z ? ` · высота ${c.z}` : "") : null;
}

// Пропорции картинки известны только после загрузки — тогда и пересчитываем окно
(function(){
  const img = $("mapImg");
  if(img) img.addEventListener("load", () => {
    const a = img.naturalWidth ? img.naturalHeight / img.naturalWidth : null;
    if(a && a !== imageAspect){ imageAspect = a; layoutView(battleView); applyView(battleView); }
  });
})();

function renderMap(){
  const wrap = $("mapWrap"); if(!wrap) return;
  const o = readMapOpts();
  const img = $("mapImg"), empty = $("mapEmpty"), gover = $("gridOver");
  if(mapImage){
    if(img.getAttribute("src") !== mapImage) img.src = mapImage;
    img.classList.remove("hidden");
  } else {
    img.classList.add("hidden"); img.removeAttribute("src");
  }
  empty.classList.toggle("hidden", hasMapSurface());
  layoutView(battleView); applyView(battleView);
  renderMapScale();
  gover.classList.toggle("hidden", !o.grid);
  if(o.grid){
    const step = 100 / o.cells;
    gover.style.backgroundSize = `${step}% ${step}%`;
  }
  const attId = +$("attSel").value, defId = +$("defSel").value;
  const layer = $("tokenLayer");
  const attacker = mapAttackerId ? units.find(u => u.id === mapAttackerId) : null;
  wrap.classList.toggle("targeting", !!attacker);
  const scaled = mapActive();
  const ppm = scaled ? battleView.sw / mapWidthMeters() : 0;   // пикселей на метр
  layer.innerHTML = units.filter(u => u.onMap).map(u => {
    const col = factionColor(u.factionId);
    const sel = u.id === attId ? "sel-a" : u.id === defId ? "sel-b" : "";
    const stateCls = u.status === "fled" ? "fled" : u.status === "destroyed" ? "dead" : "";
    const size = Math.round(o.tokenSize * (u.tokenScale || 1));
    let shape = tokenShape(u);
    let tw = shape === "rect" ? Math.round(size * 1.45) : shape === "tri" ? Math.round(size * 1.2) : size;
    let th = shape === "rect" ? Math.round(size * 0.72) : size;
    // правила карты: прямоугольник строя в настоящем размере (видимый минимум — 6 × 3 px)
    const fp = scaled ? Engine.footprint(u, currentRules()) : null;
    if(fp){ shape = "scaled"; tw = Math.max(6, Math.round(fp.front * ppm)); th = Math.max(3, Math.round(fp.depth * ppm)); }
    const fsz = Math.round((shape === "tri" ? size * 0.3 : size * 0.38));
    const fac = Math.round(u.facing || 0);
    const initials = u.name.split(/\s+/).slice(0,2).map(x => x[0] || "").join("").toUpperCase();
    let targetCls = "", farText = "";
    if(attacker){
      if(u.id === attacker.id) targetCls = "attacker";
      else if(isValidTarget(attacker, u)){
        targetCls = "valid-target";
        if(rangeActive()){
          const rr = Engine.attackReach(attacker, u, isMeleeMode(mapMode(attacker)), mapGeo(), currentRules());
          if(!rr.ok){ targetCls += " far-target"; farText = rr.text; }
        }
      }
    }
    const st = moraleStage(u.morale);
    const cmdr = getCmdr(u);
    const under = terrainUnder(u);
    const statusWord = u.status === "fled" ? "БЕЖАЛ" : u.status === "destroyed" ? "УНИЧТОЖЕН" : st.label;
    const selCls = selectedTokens[u.id] ? "selected" : "";
    return `<div class="token ${sel} ${stateCls} ${targetCls} ${selCls}" data-tid="${u.id}"
        style="left:${u.mapX}%;top:${u.mapY}%" title="${esc(u.name)}">
      <div class="ttip">
        <div class="tt-name">${esc(u.name)} ${unitIcons(u)}</div>
        <div class="tt-line">${esc(factionName(u.factionId))}${u.subfactionId ? " / " + esc(subName(u.subfactionId)) : ""}${cmdr ? " · ⚜ " + esc(cmdr.name) : ""}</div>
        <div class="tt-line">Солдаты <b>${u.soldiers}</b>/${u.initial} · ${esc(statusWord)}</div>
        <div class="tt-line">БД <b>${u.morale}</b> · дисц <b>${u.discipline}</b> · усталость <b>${u.fatigue}</b></div>
        <div class="tt-line">Снар. <b>${u.eqAtk}</b>/<b>${u.eqDef}</b> · опыт <b>${u.exp}</b> · мастерство <b>${u.mastery}</b></div>
        ${under ? `<div class="tt-line">Местность: <b>${esc(under)}</b></div>` : ""}
        ${fp ? `<div class="tt-line">Строй: <b>${fp.front}</b> × <b>${fp.depth}</b> м</div>` : ""}
        ${moveActive() && u.onMap ? `<div class="tt-line">Прошёл за ход: <b>${Math.round(u.movedM || 0)}</b> / ${Engine.unitSpeed(u, currentRules())} м${u.type === "cavalry" ? ` · разбег ${Math.round(u.runUpM || 0)} м` : ""}</div>` : ""}
        ${rangeActive() && u.weapon === "ranged" ? `<div class="tt-line">Дальность: <b>${Engine.rangeOf(u, currentRules())}</b> м</div>` : ""}
        ${farText ? `<div class="tt-line" style="color:#E08A76">Вне досягаемости: ${esc(farText)}</div>` : ""}
        <div class="tt-line">${esc(TYPE_NAMES[u.type] || "Пехота")} · ${u.weapon === "ranged" ? "дальний бой" : "ближний бой"} · смотрит на ${Math.round(u.facing || 0)}° · атак ${u.attacksMade || 0}/${attackLimit(u)} · ответных ${u.countersMade || 0}/${counterLimit(u)}${u.acted ? " · походил" : ""}</div>
      </div>
      <div class="tbody" style="transform:rotate(${fac}deg)">
        <div class="tarrow"></div>
        <div class="tdisc ${shape}" style="width:${tw}px;height:${th}px;background:${col};font-size:${fsz}px"><span class="tinit" style="transform:rotate(${-fac}deg)">${esc(initials)}</span></div>
      </div>
      <div class="tlabel" style="max-width:${Math.max(size*2.2,90)}px;overflow:hidden;text-overflow:ellipsis">${esc(u.name)}</div>
      <div class="tcount" style="color:${u.status === "fled" ? "#E08A76" : strengthColor(u)}">${u.soldiers}${u.acted ? " ✓" : ""}</div>
    </div>`;
  }).join("");
  // подсказка выбора цели — в окне, а не на сцене: при приближении она не уезжает за край
  if(!moveZone) $("mapHint").innerHTML = attacker
    ? `<div class="targethint">Выбери цель для «${esc(attacker.name)}» · ${esc(mapModeOverride === "auto" ? "режим по местности" : MODES[mapMode(attacker)])}${mapCharge ? " · натиск" : ""} · Esc — отмена</div>`
    : calib ? `<div class="targethint">${calib.a ? "Теперь вторую точку" : "Отметь на карте первую точку"} · Esc — отмена</div>` : "";
  if(openMenuId){
    const mu = units.find(u => u.id === openMenuId);
    if(mu && mu.onMap) layer.insertAdjacentHTML("beforeend", tokenMenuHtml(mu));
    else openMenuId = null;
  }

  renderSelBar();
  const unplaced = units.filter(u => !u.onMap && u.status === "active");
  $("unplacedRow").innerHTML = unplaced.length
    ? `<span class="hint" style="margin:0 6px 0 0">Не на карте:</span>` + unplaced.map(u =>
        `<span class="chip" style="border-color:${factionColor(u.factionId)}"
          onclick="placeOnMap(${u.id})">${esc(u.name)} — ${u.soldiers}</span>`).join("")
    : '<span class="hint" style="margin:0">Все активные отряды расставлены на карте.</span>';
}
// перетаскивание фишек, рамка выделения, приближение и сдвиг карты
// Проценты считаются от сцены (#mapTop), а не от окна: при приближении сцена больше окна.
(function(){
  let tok = null, tid = null, moved = false, groupStart = null, dragStart = null;
  let marquee = null, mqStart = null, mqShift = false;
  let pan = null, overMap = false;
  document.addEventListener("mouseover", e => { overMap = !!(e.target.closest && e.target.closest("#mapWrap")); });
  const topRect = () => $("mapTop").getBoundingClientRect();
  const pct = (cx, cy, r) => ({x: (cx - r.left) / r.width * 100, y: (cy - r.top) / r.height * 100});
  const typing = e => e.target && /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName);

  document.addEventListener("mousedown", e => {
    const wrap = $("mapWrap"); if(!wrap || ed.open) return;
    // сдвиг карты: правая кнопка или пробел + левая
    if(wrap.contains(e.target) && (e.button === 2 || (e.button === 0 && spaceDown)) && !e.target.closest(".tmenu")){
      pan = {x: e.clientX, y: e.clientY}; wrap.classList.add("panning"); e.preventDefault();
      return;
    }
    if(e.button !== 0) return;
    const t = e.target.closest && e.target.closest(".token");
    if(t){
      tok = t; tid = +t.dataset.tid; moved = false;
      t.classList.add("dragging");
      dragStart = pct(e.clientX, e.clientY, topRect());
      // тянем всю выделенную группу, если фишка входит в выделение
      groupStart = selectedTokens[tid] && selectedIds().length > 1
        ? selectedIds().map(id => { const u = units.find(z => z.id === id); return {id, x: u.mapX, y: u.mapY}; })
        : null;
      if(!groupStart) beginMoveZone(tid);
      e.preventDefault();
      return;
    }
    if(e.target.closest(".tmenu") || e.target.closest(".selbar")) return;
    if(!wrap.contains(e.target)) return;
    if(calib){ const q = pct(e.clientX, e.clientY, topRect()); calibrateClick(q.x / 100, q.y / 100); e.preventDefault(); return; }
    // рамка выделения по пустому месту карты (в пикселях окна)
    const r = wrap.getBoundingClientRect();
    mqShift = e.shiftKey;
    mqStart = {x: e.clientX - r.left, y: e.clientY - r.top};
    marquee = document.createElement("div");
    marquee.className = "marquee";
    marquee.style.left = mqStart.x + "px"; marquee.style.top = mqStart.y + "px";
    marquee.style.width = "0px"; marquee.style.height = "0px";
    wrap.appendChild(marquee);
    e.preventDefault();
  });

  document.addEventListener("mousemove", e => {
    const wrap = $("mapWrap"); if(!wrap) return;
    if(pan){ panView(battleView, e.clientX - pan.x, e.clientY - pan.y); pan = {x: e.clientX, y: e.clientY}; return; }
    const r = wrap.getBoundingClientRect();
    if(marquee && mqStart){
      const x = clamp(e.clientX - r.left, 0, r.width), y = clamp(e.clientY - r.top, 0, r.height);
      marquee.style.left = Math.min(x, mqStart.x) + "px";
      marquee.style.top = Math.min(y, mqStart.y) + "px";
      marquee.style.width = Math.abs(x - mqStart.x) + "px";
      marquee.style.height = Math.abs(y - mqStart.y) + "px";
      return;
    }
    if(!tok) return;
    const p = pct(e.clientX, e.clientY, topRect());
    let x = clamp(p.x, 0, 100), y = clamp(p.y, 0, 100);
    const o = readMapOpts();
    if(o.snap && !groupStart){
      const step = 100 / o.cells;
      x = Math.round((x - step/2) / step) * step + step/2;
      y = Math.round((y - step/2) / step) * step + step/2;
    }
    if(groupStart){
      const dx = x - dragStart.x, dy = y - dragStart.y;
      groupStart.forEach(g => {
        const el = document.querySelector(`.token[data-tid="${g.id}"]`);
        if(el){ el.style.left = clamp(g.x + dx, 0, 100) + "%"; el.style.top = clamp(g.y + dy, 0, 100) + "%"; }
      });
    } else {
      tok.style.left = x + "%"; tok.style.top = y + "%";
      moveZoneHint(tid, x / 100, y / 100);
    }
    moved = true;
  });

  document.addEventListener("mouseup", e => {
    const wrap = $("mapWrap");
    if(pan){ pan = null; if(wrap) wrap.classList.remove("panning"); return; }
    if(marquee && mqStart && wrap){
      const r = wrap.getBoundingClientRect(), tr = topRect();
      const x2 = clamp(e.clientX - r.left, 0, r.width), y2 = clamp(e.clientY - r.top, 0, r.height);
      const a = pct(r.left + Math.min(mqStart.x, x2), r.top + Math.min(mqStart.y, y2), tr);
      const b = pct(r.left + Math.max(mqStart.x, x2), r.top + Math.max(mqStart.y, y2), tr);
      const box = {l: a.x, t: a.y, rt: b.x, b: b.y};
      marquee.remove(); marquee = null; mqStart = null;
      const tiny = (box.rt - box.l) < 0.7 && (box.b - box.t) < 0.7;
      if(tiny){
        if(!mqShift) selectedTokens = {};
      } else {
        if(!mqShift) selectedTokens = {};
        units.filter(u => u.onMap).forEach(u => {
          if(u.mapX >= box.l && u.mapX <= box.rt && u.mapY >= box.t && u.mapY <= box.b)
            selectedTokens[u.id] = true;
        });
      }
      renderMap();
      return;
    }
    if(!tok) return;
    tok.classList.remove("dragging");
    if(moved){
      if(groupStart){
        pushUndo(`перемещение группы (${groupStart.length} отр.)`);
        const moves = [];
        groupStart.forEach(g => {
          const el = document.querySelector(`.token[data-tid="${g.id}"]`);
          if(el) moves.push({id: g.id, from: [g.x / 100, g.y / 100], to: [parseFloat(el.style.left) / 100, parseFloat(el.style.top) / 100]});
        });
        moves.forEach(mv => updUnit(mv.id, {mapX: mv.to[0] * 100, mapY: mv.to[1] * 100}));
        accountMoves(moves);
      } else {
        const u0 = units.find(z => z.id === tid);
        const mv = {id: tid, from: [u0.mapX / 100, u0.mapY / 100], to: [parseFloat(tok.style.left) / 100, parseFloat(tok.style.top) / 100]};
        if(moveActive()) pushUndo(`перемещение «${u0.name}»`);
        updUnit(tid, {mapX: mv.to[0] * 100, mapY: mv.to[1] * 100});
        accountMoves([mv], moveZone);
      }
      endMoveZone();
      saveState(); renderMap();
    } else {
      endMoveZone();
      if(e.shiftKey){
        if(selectedTokens[tid]) delete selectedTokens[tid]; else selectedTokens[tid] = true;
        renderMap();
      } else {
        tokenClick(tid);
      }
    }
    tok = null; tid = null; moved = false; groupStart = null; dragStart = null;
  });
  document.addEventListener("dblclick", e => {
    const t = e.target.closest && e.target.closest(".token");
    if(t) removeFromMap(+t.dataset.tid);
  });
  // колесо: над фишкой — поворот, над пустым местом — приближение к курсору
  document.addEventListener("wheel", e => {
    if(ed.open) return;
    const t = e.target.closest && e.target.closest(".token");
    if(t){
      e.preventDefault();
      const id = +t.dataset.tid;
      const u = units.find(z => z.id === id); if(!u) return;
      const step = e.shiftKey ? 5 : 15;
      rotateUnit(id, e.deltaY > 0 ? step : -step);
      return;
    }
    const wrap = $("mapWrap");
    if(!wrap || !wrap.contains(e.target) || !hasMapSurface() || e.target.closest(".tmenu")) return;
    e.preventDefault();
    const r = wrap.getBoundingClientRect();
    zoomView(battleView, e.deltaY > 0 ? 1 / 1.2 : 1.2, e.clientX - r.left, e.clientY - r.top);
  }, {passive:false});
  document.addEventListener("contextmenu", e => {
    const inMap = ["mapWrap", "edWrap"].some(id => { const w = $(id); return w && w.contains(e.target); });
    if(inMap) e.preventDefault();
  });
  document.addEventListener("keydown", e => {
    // пробел над картой — сдвиг, а не прокрутка страницы
    if(e.code === "Space" && !typing(e)){ spaceDown = true; if(ed.open || overMap) e.preventDefault(); }
    if(e.key === "Escape"){
      if(calib){ calib = null; document.querySelectorAll(".calibdot").forEach(el => el.remove()); renderMap(); }
      else if(mapAttackerId) cancelTargeting();
      else if(openMenuId) closeTokenMenu();
      else if(selectedIds().length) clearSelection();
    }
  });
  document.addEventListener("keyup", e => { if(e.code === "Space") spaceDown = false; });
  document.addEventListener("mousedown", e => {
    if(!e.target.closest) return;
    if(e.target.closest(".tmenu") || e.target.closest(".token") || e.target.closest(".selbar")) return;
    if(openMenuId) closeTokenMenu();
    else if(mapAttackerId && e.target.closest("#mapWrap")) cancelTargeting();
  });
})();

// ═══════════ редактор карты (К18) ═══════════
// Полноэкранный режим поверх трекера. Все правки клеток — функции движка (Engine.paintDisc и др.),
// здесь только мышь, панель и отрисовка. Каждый мазок — один шаг отката.
const ED_TOOLS = [["brush", "Кисть"], ["erase", "Ластик"], ["fill", "Заливка"], ["line", "Линия"],
                  ["rect", "Прямоугольник"], ["pick", "Пипетка"]];
const ed = {open: false, tool: "brush", layer: "t", value: 1, zValue: 1, sizeM: 20, outline: true,
            stroke: null, pan: null, changed: 0, showNew: false, newW: 2000, newH: 1500, raf: 0,
            gen: {id: "field", params: {}, seed: 0}, startVersion: 0, notes: []};

function openEditor(){
  ed.open = true;
  $("mapEditor").classList.remove("hidden");
  document.body.classList.add("noscroll");
  editorView.z = 1;
  ed.startVersion = terrainVersion; ed.notes = [];
  if(!ed.gen.seed) ed.gen.seed = newSeed();
  refreshEditor();
  // окно редактора только что показано — на следующем кадре размеры точно устоялись
  if(window.requestAnimationFrame) requestAnimationFrame(() => { if(ed.open){ layoutView(editorView); applyView(editorView); } });
}
function closeEditor(){
  ed.open = false; ed.stroke = null; ed.pan = null;
  $("mapEditor").classList.add("hidden");
  document.body.classList.remove("noscroll");
  if(terrainVersion !== ed.startVersion){
    addLog(terrainMap ? "Карта местности обновлена" : "Карта местности убрана",
           terrainMap ? [mapDescription(terrainMap), ...ed.notes] : []);
    saveState();
  }
  renderMap();
}
const newSeed = () => Math.floor(Math.random() * 2147483646) + 1;   // зерно — из интерфейса; сам генератор случайности не берёт
function mapDescription(m){
  const what = m.meta && m.meta.template ? `«${m.meta.name}» · зерно ${m.meta.seed}${m.meta.edited ? " · правлена вручную" : ""}` : "нарисована вручную";
  return `${fmtN(Engine.mapWidthM(m))} × ${fmtN(Engine.mapHeightM(m))} м · ${what}`;
}
function refreshEditor(){
  if(!ed.open) return;
  renderEditorPanel();
  const img = $("edImg");
  if(mapImage){ img.src = mapImage; img.classList.remove("hidden"); }
  else { img.classList.add("hidden"); img.removeAttribute("src"); }
  layoutView(editorView); applyView(editorView);
}
function edRedraw(){
  if(!ed.open || ed.raf) return;
  const run = () => { ed.raf = 0; drawTerrain(editorView); };
  ed.raf = window.requestAnimationFrame ? requestAnimationFrame(run) : (run(), 0);
}

// ── панель ──
function renderEditorPanel(){
  const m = terrainMap;
  let html = "";
  if(m){
    html += `<div class="ed-info">${esc(mapDescription(m))}<br>${m.w} × ${m.h} клеток по ${m.cell} м</div>`;
    if(!ed.showNew) html += `<div class="btnrow" style="margin-top:0"><button class="sm" onclick="ed.showNew = true; renderEditorPanel()">Новая карта…</button>
      <button class="sm red" onclick="edDeleteTerrain()">Убрать местность</button></div>`;
  } else {
    html += `<div class="hint" style="margin-top:0">Местности ещё нет. Задай размер поля — клетки по 5 м${mapImage ? "; слой ляжет поверх загруженной картинки" : ""}.</div>`;
  }
  if(!m || ed.showNew){
    const aspect = mapImage ? (imageAspect || 0.6) : null;
    const h = aspect ? Math.round(ed.newW * aspect) : ed.newH;
    html += `<div class="frow2" style="margin-top:8px">
        <div><label>Ширина, м</label><input id="edNewW" type="number" min="50" step="50" value="${ed.newW}" oninput="edNewSize()"></div>
        <div><label>Глубина, м</label><input id="edNewH" type="number" min="50" step="50" value="${h}"
          ${aspect ? 'disabled title="по пропорциям картинки"' : ""} oninput="edNewSize()"></div>
      </div>
      <label class="chk"><input type="checkbox" id="edNewFill" ${mapImage ? "" : "checked"}> Залить полем${mapImage ? " (без галочки видна картинка)" : ""}</label>
      <div class="hint" id="edNewInfo"></div>
      <div class="btnrow"><button class="gold sm" onclick="edCreate()">Создать</button>
        ${m ? '<button class="sm" onclick="ed.showNew = false; renderEditorPanel()">Отмена</button>' : ""}</div>`;
  }
  $("edMapInfo").innerHTML = html;
  edNewSize();
  renderGen();

  $("edTools").innerHTML = ED_TOOLS.map(([k, n]) =>
    `<button class="sm ${ed.tool === k ? "gold" : ""}" onclick="edSetTool('${k}')">${n}</button>`).join("");
  $("edSize").value = Math.min(300, ed.sizeM); $("edSizeNum").value = ed.sizeM;
  $("edOutline").checked = ed.outline;
  $("edOutlineRow").style.display = ed.tool === "rect" ? "" : "none";

  $("edLayers").innerHTML = [["t", "Местность"], ["z", "Высота"]].map(([k, n]) =>
    `<button class="sm ${ed.layer === k ? "gold" : ""}" onclick="edSetLayer('${k}')">${n}</button>`).join("");
  $("edPalette").innerHTML = ed.layer === "z" ? heightsHtml() : paletteHtml();
  renderLibrary();
}
function paletteHtml(){
  const groups = {};
  Engine.TERRAIN.filter(t => !t.system).forEach(t => { (groups[t.group] = groups[t.group] || []).push(t); });
  return Object.entries(groups).map(([g, list]) => `<div class="ed-grp">${esc(g)}</div><div class="ed-pal">` +
    list.map(t => `<button class="ed-sw ${ed.value === t.id ? "on" : ""}" onclick="edSetValue(${t.id})">
      <i style="background:${TERRAIN_STYLE[t.key].c}"></i>${esc(t.name)}</button>`).join("") + "</div>").join("");
}
function heightsHtml(){
  return '<div class="ed-pal" style="margin-top:6px">' + [0, 1, 2, 3].map(z =>
    `<button class="ed-sw ${ed.zValue === z ? "on" : ""}" onclick="edSetZ(${z})"><i style="background:${heightColor(z)}"></i>Уровень ${z}</button>`).join("") +
    '</div><div class="hint">0 — равнина. Край холма рисуется тёмной линией. Высота не стирает местность под собой.</div>';
}
function edNewSize(){
  const wEl = $("edNewW"); if(!wEl) return;
  ed.newW = Math.max(50, +wEl.value || 2000);
  const aspect = mapImage ? (imageAspect || 0.6) : null;
  if(aspect) $("edNewH").value = Math.round(ed.newW * aspect);
  else ed.newH = Math.max(50, +$("edNewH").value || 1500);
  const H = aspect ? Math.round(ed.newW * aspect) : ed.newH;
  const cells = Math.round(ed.newW / Engine.CELL_M) * Math.round(H / Engine.CELL_M);
  $("edNewInfo").textContent = cells > Engine.MAX_CELLS ? `Слишком много клеток (${fmtN(cells)}), предел ${fmtN(Engine.MAX_CELLS)}.`
    : `${fmtN(Math.round(ed.newW / Engine.CELL_M))} × ${fmtN(Math.round(H / Engine.CELL_M))} клеток`;
}
function edSetTool(k){ ed.tool = k; renderEditorPanel(); }
function edSetLayer(k){ ed.layer = k; if(ed.tool === "pick") ed.tool = "brush"; renderEditorPanel(); }
function edSetValue(id){ ed.value = id; if(ed.tool === "erase" || ed.tool === "pick") ed.tool = "brush"; renderEditorPanel(); }
function edSetZ(z){ ed.zValue = z; if(ed.tool === "erase" || ed.tool === "pick") ed.tool = "brush"; renderEditorPanel(); }
function edSetSize(v){ ed.sizeM = clamp(Math.round(+v || 5), 5, 1000); $("edSize").value = Math.min(300, ed.sizeM); $("edSizeNum").value = ed.sizeM; }

function edCreate(){
  const aspect = mapImage ? (imageAspect || 0.6) : null;
  const W = ed.newW, H = aspect ? Math.round(W * aspect) : ed.newH;
  let m;
  try{ m = Engine.createTerrain(W, H, $("edNewFill").checked ? Engine.TERRAIN_BY_KEY.field.id : 0); }
  catch(err){ alert(err.message); return; }
  if(terrainMap && !confirm("Заменить текущую местность новой пустой картой?")) return;
  pushUndo("новая карта местности");
  terrainMap = m; terrainVersion++;
  ed.showNew = false; editorView.z = 1;
  refreshEditor(); saveState();
}
function edDeleteTerrain(){
  if(!terrainMap || !confirm("Убрать слой местности с карты? Фишки и картинка останутся.")) return;
  pushUndo("удаление местности");
  terrainMap = null; terrainVersion++;
  refreshEditor(); saveState();
}

// ── мазки ──
const edToolName = () => (ED_TOOLS.find(t => t[0] === ed.tool) || ["", ""])[1].toLowerCase();
const edRadius = () => Math.max(0.5, ed.sizeM / 2 / terrainMap.cell);
const edValue = () => ed.tool === "erase" ? 0 : ed.layer === "z" ? ed.zValue : ed.value;
function edPoint(clientX, clientY){
  const r = $("edWrap").getBoundingClientRect(), v = editorView, m = terrainMap;
  const fx = (clientX - r.left - v.ox) / v.sw, fy = (clientY - r.top - v.oy) / v.sh;
  return {x: fx * m.w, y: fy * m.h, fx, fy, sx: clientX - r.left, sy: clientY - r.top};
}
function edBegin(p){
  const m = terrainMap; if(!m) return;
  if(ed.tool === "pick"){
    const c = Engine.cellAt(m, p.fx, p.fy);
    if(ed.layer === "z") ed.zValue = c.z; else if(c.t) ed.value = c.t;
    ed.tool = "brush"; renderEditorPanel();
    return;
  }
  pushUndo(`редактор карты: ${edToolName()}`);
  ed.changed = 0;
  if(ed.tool === "fill"){
    ed.changed = Engine.floodFill(m, ed.layer, p.x, p.y, edValue());
    edTouched(); edEnd();
    return;
  }
  ed.stroke = {start: p, last: p, end: p};
  if(ed.tool === "brush" || ed.tool === "erase"){
    ed.changed += Engine.paintDisc(m, ed.layer, p.x, p.y, edRadius(), edValue());
    edTouched();
  } else edPreview();
}
function edMove(p){
  const s = ed.stroke, m = terrainMap; if(!s || !m) return;
  if(ed.tool === "brush" || ed.tool === "erase"){
    ed.changed += Engine.paintSegment(m, ed.layer, s.last.x, s.last.y, p.x, p.y, edRadius(), edValue());
    s.last = p; edTouched();
  } else { s.end = p; edPreview(); }
}
function edEnd(){
  const s = ed.stroke, m = terrainMap;
  if(s && m){
    const a = s.start, b = s.end;
    if(ed.tool === "line") ed.changed += Engine.paintSegment(m, ed.layer, a.x, a.y, b.x, b.y, edRadius(), edValue());
    if(ed.tool === "rect") ed.changed += Engine.paintRect(m, ed.layer, a.x, a.y, b.x, b.y, edValue(),
                                                           ed.outline ? Math.max(1, Math.round(ed.sizeM / m.cell)) : 0);
    if(ed.tool === "line" || ed.tool === "rect") edTouched();
  }
  ed.stroke = null;
  $("edShape").style.display = "none";
  if(!ed.changed){ undoStack.pop(); renderUndoBtn(); }   // пустой мазок не засоряет откат
  else saveState();
  ed.changed = 0;
}
function edTouched(){ terrainVersion++; if(terrainMap) terrainMap.meta.edited = true; edRedraw(); }
function noteAspect(m){
  if(mapImage && imageAspect && Math.abs(imageAspect - m.h / m.w) > 0.05)
    ed.notes.push("⚠ Пропорции карты и картинки разные — местность растянута под картинку.");
}

// ── шаблоны карт (К8, К27): генератор в движке, здесь — выбор, настройки, зерно ──
function renderGen(){
  const box = $("edGen"); if(!box) return;
  const t = Engine.getMapTemplate(ed.gen.id);
  const groups = {};
  Engine.MAP_TEMPLATES.forEach(x => { (groups[x.group] = groups[x.group] || []).push(x); });
  const p = Engine.mapParams(t.id, ed.gen.params);
  const field = d => {
    const v = p[d.key];
    if(d.type === "bool") return `<label class="chk"><input type="checkbox" ${v ? "checked" : ""} onchange="edGenSet('${d.key}', this.checked)"> ${esc(d.name)}</label>`;
    if(d.type === "select") return `<div><label>${esc(d.name)}</label><select onchange="edGenSet('${d.key}', this.value)">${optionsHtml(d.options, v)}</select></div>`;
    return `<div><label>${esc(d.name)}</label><input type="number" min="${d.min}" max="${d.max}" step="${d.step}" value="${v}" onchange="edGenSet('${d.key}', this.value)"></div>`;
  };
  box.innerHTML = `<select onchange="edGenPick(this.value)">` + Object.entries(groups).map(([g, list]) =>
      `<optgroup label="${esc(g)}">${list.map(x => `<option value="${x.id}"${x.id === t.id ? " selected" : ""}>${esc(x.name)}</option>`).join("")}</optgroup>`).join("") + `</select>
    <div class="ed-genp">${t.params.map(field).join("")}
      <div><label>Зерно</label><input type="number" id="edSeed" min="1" value="${ed.gen.seed}" onchange="ed.gen.seed = Math.max(1, Math.round(+this.value) || 1)"></div></div>
    <div class="btnrow" style="margin-top:0"><button class="gold sm" onclick="edGenerate(false)">Создать</button>
      <button class="sm" onclick="edGenerate(true)" title="Новое зерно — другая карта с теми же настройками">Ещё вариант</button></div>
    <div class="hint">С тем же зерном и настройками карта повторится — так её можно воспроизвести у игроков.</div>`;
}
function edGenPick(id){ ed.gen.id = id; ed.gen.params = {}; renderGen(); }
function edGenSet(key, v){ ed.gen.params[key] = v; }
function edGenerate(another){
  const t = Engine.getMapTemplate(ed.gen.id);
  if(another) ed.gen.seed = newSeed();
  const handMade = terrainMap && (!terrainMap.meta.template || terrainMap.meta.edited);
  if(handMade && !confirm("Текущая карта нарисована или поправлена вручную. Заменить её картой по шаблону?")) return;
  let m;
  try{ m = Engine.generateMap(t.id, ed.gen.params, ed.gen.seed); }
  catch(err){ alert(err.message); return; }
  pushUndo(`карта по шаблону «${t.name}»`);
  terrainMap = m; terrainVersion++; editorView.z = 1;
  noteAspect(m);
  refreshEditor(); saveState();
}
// Предпросмотр линии и прямоугольника
function edPreview(){
  const s = ed.stroke, el = $("edShape"), v = editorView, m = terrainMap;
  const px = p => v.ox + p.x / m.w * v.sw, py = p => v.oy + p.y / m.h * v.sh;
  const cellPx = v.sw / m.w;
  el.style.display = "block";
  if(ed.tool === "line"){
    const x1 = px(s.start), y1 = py(s.start), x2 = px(s.end), y2 = py(s.end);
    const len = Math.hypot(x2 - x1, y2 - y1), th = Math.max(2, edRadius() * 2 * cellPx);
    el.style.cssText = `display:block;left:${x1}px;top:${y1 - th / 2}px;width:${len}px;height:${th}px;` +
      `transform-origin:0 50%;transform:rotate(${Math.atan2(y2 - y1, x2 - x1)}rad)`;
  } else {
    const l = Math.min(px(s.start), px(s.end)), t = Math.min(py(s.start), py(s.end));
    el.style.cssText = `display:block;left:${l}px;top:${t}px;width:${Math.abs(px(s.end) - px(s.start))}px;height:${Math.abs(py(s.end) - py(s.start))}px`;
  }
}
function edCursor(p){
  const cur = $("edCursor"), m = terrainMap;
  const show = m && ["brush", "erase", "line", "rect"].includes(ed.tool) && !ed.pan;
  cur.style.display = show ? "block" : "none";
  if(show){
    const d = Math.max(4, ed.sizeM / m.cell * (editorView.sw / m.w));
    cur.style.left = p.sx + "px"; cur.style.top = p.sy + "px"; cur.style.width = d + "px"; cur.style.height = d + "px";
  }
  if(m && p.fx >= 0 && p.fx <= 1 && p.fy >= 0 && p.fy <= 1){
    const c = Engine.cellAt(m, p.fx, p.fy);
    $("edStatus").textContent = `${Math.round(p.fx * Engine.mapWidthM(m))} × ${Math.round(p.fy * Engine.mapHeightM(m))} м · ${Engine.terrainName(c.t)}${c.z ? " · высота " + c.z : ""} · ×${Math.round(editorView.z * 10) / 10}`;
  }
}
(function(){
  document.addEventListener("mousedown", e => {
    if(!ed.open) return;
    const wrap = $("edWrap"); if(!wrap || !wrap.contains(e.target)) return;
    if(e.button === 2 || (e.button === 0 && spaceDown)){ ed.pan = {x: e.clientX, y: e.clientY}; wrap.classList.add("panning"); e.preventDefault(); return; }
    if(e.button !== 0 || !terrainMap) return;
    e.preventDefault();
    edBegin(edPoint(e.clientX, e.clientY));
  });
  document.addEventListener("mousemove", e => {
    if(!ed.open) return;
    if(ed.pan){ panView(editorView, e.clientX - ed.pan.x, e.clientY - ed.pan.y); ed.pan = {x: e.clientX, y: e.clientY}; return; }
    if(!terrainMap) return;
    const p = edPoint(e.clientX, e.clientY);
    edCursor(p);
    if(ed.stroke) edMove(p);
  });
  document.addEventListener("mouseup", () => {
    if(!ed.open) return;
    if(ed.pan){ ed.pan = null; $("edWrap").classList.remove("panning"); return; }
    if(ed.stroke) edEnd();
  });
  document.addEventListener("wheel", e => {
    if(!ed.open) return;
    const wrap = $("edWrap"); if(!wrap || !wrap.contains(e.target)) return;
    e.preventDefault();
    const r = wrap.getBoundingClientRect();
    zoomView(editorView, e.deltaY > 0 ? 1 / 1.2 : 1.2, e.clientX - r.left, e.clientY - r.top);
  }, {passive: false});
  document.addEventListener("keydown", e => {
    if(!ed.open) return;
    if((e.ctrlKey || e.metaKey) && (e.key === "z" || e.key === "я")){ e.preventDefault(); undo(); }
  });
  window.addEventListener("resize", () => { if(ed.open) refreshEditor(); else renderMap(); });
})();

// ── свои шаблоны карт (К19): в браузере, отдельно от партии, и файлом ──
// Отдельный ключ: библиотека не должна попадать в каждый снимок отката и переживает «Сбросить всё».
const MAPLIB_KEY = "battle_tracker_maps_v1";
function libRead(){
  try{ const a = JSON.parse(localStorage.getItem(MAPLIB_KEY) || "[]"); return Array.isArray(a) ? a : []; }
  catch(e){ return []; }
}
function libWrite(list){
  try{ localStorage.setItem(MAPLIB_KEY, JSON.stringify(list)); return true; }
  catch(e){ alert("Не удалось сохранить шаблон: в браузере кончилось место. Выгрузи шаблоны файлами и удали лишние."); return false; }
}
const libSize = e => e.map ? `${fmtN(e.map.w * (e.map.cell || 5))} × ${fmtN(e.map.h * (e.map.cell || 5))} м` : "";
function renderLibrary(){
  const box = $("edLibrary"); if(!box) return;
  const list = libRead();
  box.innerHTML = (list.length ? list.map(e => `<div class="ed-librow"><span>${esc(e.name)}<em>${libSize(e)} · ${esc(e.created || "")}</em></span>
      <button class="sm" onclick="libApply(${e.id})">Взять</button>
      <button class="sm" onclick="libExport(${e.id})" title="Выгрузить файлом">Файл</button>
      <button class="sm red" onclick="libDelete(${e.id})">✕</button></div>`).join("")
    : '<div class="hint" style="margin-top:0">Пока пусто. Нарисуй карту и сохрани — например, «Тринидар».</div>') +
    `<div class="ed-libsave"><input id="edLibName" placeholder="Название шаблона"${terrainMap ? "" : " disabled"}>
      <button class="gold sm" onclick="libSaveCurrent()"${terrainMap ? "" : " disabled"}>Сохранить</button></div>
    <div class="btnrow"><button class="sm" onclick="document.getElementById('edLibFile').click()">Загрузить из файла</button></div>`;
}
function libSaveCurrent(){
  if(!terrainMap) return;
  const name = ($("edLibName").value || "").trim() || "Карта " + new Date().toLocaleDateString("ru-RU");
  const list = libRead();
  list.push({id: Date.now() + list.length, name, created: new Date().toISOString().slice(0, 10), map: Engine.serializeTerrain(terrainMap)});
  if(libWrite(list)) renderLibrary();
}
function libApply(id){
  const e = libRead().find(x => x.id === id);
  const m = e && Engine.deserializeTerrain(e.map);
  if(!m){ alert("Шаблон повреждён — открыть не получилось."); return; }
  if(terrainMap && !confirm(`Заменить текущую местность шаблоном «${e.name}»?`)) return;
  pushUndo(`карта из шаблона «${e.name}»`);
  terrainMap = m; terrainVersion++; editorView.z = 1;
  m.meta = Object.assign({}, m.meta, {library: e.name});
  ed.notes.push(`из своего шаблона «${e.name}»`);
  noteAspect(m);
  refreshEditor(); saveState();
}
function libDelete(id){
  const e = libRead().find(x => x.id === id); if(!e) return;
  if(!confirm(`Удалить шаблон «${e.name}» из браузера? Если нужен — сначала выгрузи его файлом.`)) return;
  libWrite(libRead().filter(x => x.id !== id)); renderLibrary();
}
function libExport(id){
  const e = libRead().find(x => x.id === id); if(!e) return;
  downloadText(`karta_${safeFileName(e.name)}.json`, JSON.stringify({kind: "battle-map", v: 1, name: e.name, map: e.map}), "application/json");
}
function libImport(ev){
  const file = ev.target.files[0]; if(!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    try{
      const d = JSON.parse(reader.result);
      if(d.kind !== "battle-map" || !Engine.deserializeTerrain(d.map)) throw new Error("не карта");
      const list = libRead();
      list.push({id: Date.now() + list.length, name: String(d.name || file.name), created: new Date().toISOString().slice(0, 10), map: d.map});
      if(libWrite(list)) renderLibrary();
    }catch(e){ alert("Не удалось прочитать файл: это не выгрузка карты трекера."); }
    ev.target.value = "";
  };
  reader.readAsText(file);
}

// ═══════════ движение по карте (К22, К29) — черновик ═══════════
// Зона досягаемости и цена пути — движок (Engine.reachMap, pathCost); здесь только показ и учёт.
let moveZone = null;   // {id, u, speed, remaining, reach, bitmap} — пока тянем одну фишку
function beginMoveZone(id){
  moveZone = null;
  if(!moveActive()) return;
  const u = units.find(z => z.id === id); if(!u || u.status !== "active") return;
  const R = currentRules(), speed = Engine.unitSpeed(u, R), remaining = Math.max(0, speed - (u.movedM || 0));
  moveZone = {id, u: Object.assign({}, u), speed, remaining, reach: Engine.reachMap(u, mapGeo(), R, remaining * 2 + 100)};
  drawReach();
}
function moveZoneHint(id, fx, fy){
  if(!moveZone || moveZone.id !== id) return;
  const c = Engine.pathCost(moveZone.reach, moveZone.u, fx, fy, mapGeo());
  const ok = isFinite(c) && c <= moveZone.remaining + 0.5;
  const txt = isFinite(c) ? `${Math.round(c)} м из ${Math.round(moveZone.remaining)}${ok ? "" : " — сверх нормы"}` : "дальше вдвое нормы или непроходимо";
  $("mapHint").innerHTML = `<div class="targethint${ok ? " ok" : ""}">«${esc(moveZone.u.name)}»: ${txt}</div>`;
}
function endMoveZone(){
  if(!moveZone) return;
  moveZone = null; drawReach(); $("mapHint").innerHTML = "";
}
function drawReach(){
  const cv = $("reachCanvas"); if(!cv) return;
  const ctx = canvasCtx(cv); if(!ctx) return;
  const v = battleView, dpr = window.devicePixelRatio || 1;
  const W = Math.round(v.vw * dpr), H = Math.round(v.vh * dpr);
  if(cv.width !== W || cv.height !== H){ cv.width = W; cv.height = H; }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, v.vw, v.vh);
  const z = moveZone; if(!z) return;
  if(z.reach){
    if(!z.bitmap){
      const r = z.reach, bm = document.createElement("canvas");
      bm.width = r.w; bm.height = r.h;
      const bctx = canvasCtx(bm); if(!bctx) return;
      const img = bctx.createImageData(r.w, r.h), d = img.data;
      const inside = i => r.cost[i] <= z.remaining;
      for(let y = 0; y < r.h; y++) for(let x = 0; x < r.w; x++){
        const i = y * r.w + x; if(!inside(i)) continue;
        const edge = (x > 0 && !inside(i - 1)) || (x < r.w - 1 && !inside(i + 1)) || (y > 0 && !inside(i - r.w)) || (y < r.h - 1 && !inside(i + r.w));
        d[i * 4] = 224; d[i * 4 + 1] = 195; d[i * 4 + 2] = 74; d[i * 4 + 3] = edge ? 200 : 55;
      }
      bctx.putImageData(img, 0, 0);
      z.bitmap = bm;
    }
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(z.bitmap, v.ox, v.oy, v.sw, v.sh);
  } else {
    // без местности — круг по прямой
    const cx = v.ox + z.u.mapX / 100 * v.sw, cy = v.oy + z.u.mapY / 100 * v.sh;
    ctx.beginPath(); ctx.arc(cx, cy, z.remaining * v.sw / mapWidthMeters(), 0, Math.PI * 2);
    ctx.fillStyle = "rgba(224,195,74,.15)"; ctx.fill();
    ctx.strokeStyle = "rgba(224,195,74,.85)"; ctx.lineWidth = 1.5; ctx.stroke();
  }
}
// Учёт хода: сколько прошёл (по местности, а без неё — по прямой), разбег для натиска; сверх нормы — в журнал
function accountMoves(moves, zone){
  if(!moveActive()) return;
  const R = currentRules(), geo = mapGeo(), over = [];
  moves.forEach(mv => {
    const u = units.find(z => z.id === mv.id); if(!u || u.status !== "active") return;
    const at = Object.assign({}, u, {mapX: mv.from[0] * 100, mapY: mv.from[1] * 100});
    const speed = Engine.unitSpeed(at, R), before = u.movedM || 0;
    const reach = zone && zone.id === mv.id ? zone.reach : Engine.reachMap(at, geo, R, Math.max(0, speed - before) * 2 + 100);
    const straight = Engine.pathCost(null, at, mv.to[0], mv.to[1], geo);
    let cost = Engine.pathCost(reach, at, mv.to[0], mv.to[1], geo);
    // зона считалась от остатка хода; ушёл дальше — пересчитываем от длины самого хода (местность до ×4, подъём)
    if(!isFinite(cost) && reach) cost = Engine.pathCost(Engine.reachMap(at, geo, R, Math.min(straight * 6 + 100, 5000)), at, mv.to[0], mv.to[1], geo);
    const lost = !isFinite(cost);
    if(lost) cost = straight;
    const run = Engine.runOver(at, mv.from, mv.to, geo, R);
    const after = before + cost;
    updUnit(u.id, {movedM: Math.round(after), runUpM: run.clear ? Math.round((u.runUpM || 0) + run.len) : 0});
    if(after > speed + 0.5 || lost)
      over.push(`«${u.name}»: ${lost ? "по местности пути нет (непроходимо), по прямой " : ""}${Math.round(cost)} м — за ход ${Math.round(after)} из ${speed} м`);
  });
  if(over.length) addLog("Движение сверх нормы — решение мастера", over.concat(["Черновик до ГМа: скорости и местность — SPEC, раздел 6а."]));
}

// ═══════════ правила карты и масштаб (К10, К26, К31, К32) ═══════════
const mapWidthMeters = () => terrainMap ? Engine.mapWidthM(terrainMap) : (mapOpts.widthM || 2000);
const mapHeightMeters = () => terrainMap ? Engine.mapHeightM(terrainMap) : mapWidthMeters() * mapAspect();
const mapGeo = () => ({map: terrainMap, W: mapWidthMeters(), H: mapHeightMeters()});
// Правила карты работают, когда включены и есть поверхность карты (картинка или местность)
const mapActive = () => mapRules.on && hasMapSurface();
const terrainActive = () => mapActive() && mapRules.terrain && !!terrainMap;
const MAP_RULE_NAMES = {on: "правила карты", terrain: "местность и высота в бою", move: "движение", range: "дальности",
                        panic: "каскадная паника", panicMorale: "−100 БД вместе с проверкой"};
const panicActive = () => mapActive() && mapRules.panic;
// Каскадная паника (К25): после каждого побега — волна; в том же шаге отката, одной записью на источник
function runPanic(ids){
  if(!panicActive()) return;
  ids.forEach(id => {
    const src = units.find(u => u.id === id);
    if(!src || !src.onMap || src.status !== "fled") return;
    const r = Engine.panicWave(units, id, mapGeo(), engineCtx(), {moraleLoss: !!mapRules.panicMorale});
    if(!r.lines.length) return;
    r.patches.forEach(p => updUnit(p.id, p.patch));
    addLog(`🏳 Каскадная паника: бегство «${src.name}»`,
      r.lines.concat(["Черновик до ГМа: радиус 150 м, первое кольцо — по видимости (SPEC, 6а)."]), r.fled.length ? "danger" : undefined);
  });
}
const moveActive = () => mapActive() && mapRules.move;
const rangeActive = () => mapActive() && mapRules.range;
function toggleMapRules(){ $("mapRulesPanel").classList.toggle("hidden"); renderMapRules(); }
function renderMapRules(){
  const b = $("mapRulesBtn"); if(!b) return;
  b.textContent = "🗺 Правила карты: " + (mapRules.on ? "вкл (черновик)" : "выкл");
  b.classList.toggle("on", mapRules.on);
  $("mr_on").checked = mapRules.on;
  $("mr_terrain").checked = !!mapRules.terrain;
  $("mr_move").checked = !!mapRules.move;
  $("mr_range").checked = !!mapRules.range;
  $("mr_panic").checked = !!mapRules.panic;
  $("mr_panicMorale").checked = !!mapRules.panicMorale;
  $("mrSub").classList.toggle("off", !mapRules.on);
}
function setMapRule(key, val){
  if(!!mapRules[key] === !!val) return;
  pushUndo(`правила карты: ${MAP_RULE_NAMES[key]}`);
  mapRules[key] = !!val;
  addLog(key === "on" ? `Правила карты ${val ? "включены" : "выключены"}` : `Правило карты «${MAP_RULE_NAMES[key]}»: ${val ? "вкл" : "выкл"}`,
    val ? ["Черновик до ГМа: числа — в SPEC, раздел 6а."] : []);
  renderAll(); saveState();
}
function renderMapScale(){
  const inp = $("mapWidthM"); if(!inp) return;
  inp.value = Math.round(mapWidthMeters());
  inp.disabled = !!terrainMap;
  inp.title = terrainMap ? "Масштаб задан картой местности (клетки по 5 м)" : "Ширина картинки в метрах";
  $("calibBtn").disabled = !!terrainMap || !mapImage;
  renderMapRules();
}
function setMapWidth(v){
  if(terrainMap) return;
  const w = clamp(Math.round(+v || 2000), 50, 50000);
  if(w === mapOpts.widthM) return;
  pushUndo("ширина карты");
  mapOpts.widthM = w;
  addLog(`Масштаб карты: ширина ${fmtN(w)} м`, []);
  renderMap(); saveState();
}
// Калибровка (К32 в): две точки на карте и расстояние между ними в метрах
let calib = null;
function startCalibrate(){
  if(terrainMap || !mapImage) return;
  calib = {a: null};
  $("mapHint").innerHTML = '<div class="targethint">Отметь на карте первую точку · Esc — отмена</div>';
}
function calibrateClick(fx, fy){
  if(!calib) return false;
  if(!calib.a){
    calib.a = [fx, fy];
    $("mapHint").innerHTML = '<div class="targethint">Теперь вторую точку</div>';
    $("mapTop").insertAdjacentHTML("beforeend", `<div class="calibdot" style="left:${fx * 100}%;top:${fy * 100}%"></div>`);
    return true;
  }
  const a = calib.a; calib = null;
  const m = prompt("Сколько метров между этими точками?", "300");
  applyCalibration(a, [fx, fy], +m);
  return true;
}
function applyCalibration(a, b, meters){
  calib = null;
  document.querySelectorAll(".calibdot").forEach(el => el.remove());
  const d = Math.hypot(b[0] - a[0], (b[1] - a[1]) * mapAspect());   // в долях ширины карты
  if(!(meters > 0) || d < 0.005){ renderMap(); return; }
  const w = clamp(Math.round(meters / d), 50, 50000);
  pushUndo("калибровка масштаба");
  mapOpts.widthM = w;
  addLog(`Масштаб карты по двум точкам: ширина ${fmtN(w)} м`, [`Между точками — ${fmtN(meters)} м`]);
  renderMap(); saveState();
}

function toggleNews(){ $("newsPanel").classList.toggle("hidden"); }
function renderPatchNotes(){
  const src = ($("patchnotes") || {}).textContent || "";
  const fmt = t => esc(t).replace(/\*\*(.+?)\*\*/g, "<b>$1</b>").replace(/`([^`]+)`/g, "<code>$1</code>");
  let html = "", inCode = false, inList = false;
  src.split("\n").forEach(l => {
    if(l.startsWith("```")){ if(inList){ html += "</ul>"; inList = false; } html += inCode ? "</pre>" : "<pre>"; inCode = !inCode; return; }
    if(inCode){ html += esc(l) + "\n"; return; }
    if(/^\s*- /.test(l)){ if(!inList){ html += "<ul>"; inList = true; } html += "<li>" + fmt(l.replace(/^\s*- /, "")) + "</li>"; return; }
    if(inList){ html += "</ul>"; inList = false; }
    if(l.startsWith("# ")) html += "<h2>" + fmt(l.slice(2)) + "</h2>";
    else if(l.startsWith("## ")) html += "<h3>" + fmt(l.slice(3)) + "</h3>";
    else if(l.startsWith("### ")) html += "<h4>" + fmt(l.slice(4)) + "</h4>";
    else if(l.startsWith("-# ")) html += '<div class="hint">' + fmt(l.slice(3)) + "</div>";
    else if(l.trim() === "---") html += "<hr>";
    else if(l.trim()) html += "<p>" + fmt(l) + "</p>";
  });
  if(inList) html += "</ul>";
  $("newsBody").innerHTML = html;
}
function renderRulesetSel(){
  const sel = $("rulesetSel"); if(!sel) return;
  sel.innerHTML = Object.values(RULESETS).map(r => `<option value="${r.id}">${esc(r.name)}</option>`).join("")
    + PLANNED_RULESETS.map(r => `<option disabled>${esc(r.name)} — ${esc(r.status)}</option>`).join("");
  sel.value = ruleset;
}
function onRulesetChange(){
  const v = $("rulesetSel").value;
  if(!RULESETS[v] || v === ruleset) return;
  pushUndo("смена набора правил");
  ruleset = v; saveState();
  addLog(`Набор правил: ${getRules(v).name}`, []);
}
function toggleRef(){ $("refPanel").classList.toggle("hidden"); }

// ═══════════ старт ═══════════
loadState();
renderRulesetSel();
renderPatchNotes();
renderAll();
onModeChange();
loadCmdrForm();
