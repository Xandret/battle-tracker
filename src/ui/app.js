
"use strict";
// ═══════════ связь с движком ═══════════
// Всё боевое — в Engine (src/engine). Здесь только псевдонимы, чтобы интерфейсный код остался читаемым.
const { clamp, r1, MODES, isMeleeMode, moraleStage, discStage, isCav, isPike, isArcherType,
        canBeTargeted, SECTOR_RU, getRules, RULESETS, PLANNED_RULESETS } = Engine;
let ruleset = "base";
const currentRules = () => getRules(ruleset);
const attackLimit  = u => Engine.attackLimit(u, currentRules());
const counterLimit = u => Engine.counterLimit(u, currentRules());
const attackSector = (a, b) => Engine.attackSector(a, b, currentRules());
const graceByDisc  = d => Engine.graceByDisc(d, currentRules());
function engineCtx(){
  return {
    rules: currentRules(),
    rng: Math.random,
    commanderOf: u => getCmdr(u),
    factionName: id => factionName(id),
  };
}

// ═══════════ состояние ═══════════
let units = [], factions = [], subfactions = [], commanders = [], log = [];
let turn = 1, nextId = 1, editingId = null;
let collapsedGroups = {}, expandedUnits = {}, undoStack = [];
let mapImage = null, mapOpts = {grid:false, snap:false, cells:20, tokenSize:42};
let mapAttackerId = null, mapCharge = false, openMenuId = null, mapModeOverride = null;
let selectedTokens = {};
const LS_KEY = "battle_tracker_v13";
const UNDO_MAX = 30;

const esc = s => String(s).replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const $ = id => document.getElementById(id);
const DEFAULT_COLOR = "#C9A227";


const TYPE_NAMES = {infantry:"Пехота", cavalry:"Кавалерия", archer:"Лучники", pike:"Пикинёры"};
// ключевые слова для автоопределения типа войск по названию отряда
const TYPE_KEYWORDS = {
  archer: ["лучник","лучниц","стрелк","стрелец","стрельц","арбалетчик","арбалетч","арбалетр",
           "пращник","пращ","застрельщик","застрельщ","охотник","егер","мушкетёр","мушкетер",
           "аркебуз","снайпер","метател","дротикомет","самура","самурай","йомен","лонгбоу"],
  cavalry:["кавалер","конниц","конн","всадник","наездник","рыцар","драгун","гусар","улан",
           "кирасир","катафракт","жандарм","ездов","верхов","витяз","паладин","сипах","мамлюк","роххирим","рохирим","рохиррим","роханц","степняк","орда"],
  pike:   ["пикинёр","пикинер","пикейщ","копейщ","копьеносц","копьенос","сарисс","фаланг",
           "алебард","бердыш","протазан","гвардейц с пиками"],
  infantry:["пехот","ополчен","ополчение","мечник","дружин","стража","стражник","гвард","воин",
            "латник","секирщ","топорщ","щитоносц","легионер","берсерк","наёмник","наемник","солдат"],
};
const HORSE_ARCHERS = ["роххирим","рохирим","рохиррим","конные лучник","конных лучник","степняк","орда","всадники-лучник"];
function guessUnitType(name){
  const n = (name || "").toLowerCase().replace(/ё/g, "е");
  if(HORSE_ARCHERS.some(w => n.includes(w))) return {type:"cavalry", weapon:"ranged", why:"конные лучники"};
  const hit = key => TYPE_KEYWORDS[key].some(w => n.includes(w.replace(/ё/g, "е")));
  const isArcher = hit("archer"), isCav = hit("cavalry"), isPike = hit("pike");
  if(isCav && isArcher) return {type:"cavalry", weapon:"ranged", why:"конные стрелки"};
  if(isCav)    return {type:"cavalry",  weapon:"melee",  why:"кавалерия"};
  if(isPike)   return {type:"pike",     weapon:"melee",  why:"пикинёры"};
  if(isArcher) return {type:"archer",   weapon:"ranged", why:"лучники"};
  if(hit("infantry")) return {type:"infantry", weapon:"melee", why:"пехота"};
  return null;
}
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
  };
  return mapOpts;
}
function stateObj(){
  return {factions, subfactions, commanders, units, log: log.slice(0,300), turn, nextId, ruleset,
          mapImage, mapOpts: readMapOpts()};
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
     onMap:false, mapX:50, mapY:50, facing:0, breakPenalty:0, tokenScale:1}, u));
  log = s.log || []; turn = s.turn || 1; nextId = s.nextId || 1;
  ruleset = RULESETS[s.ruleset] ? s.ruleset : "base";
  if($("rulesetSel")) $("rulesetSel").value = ruleset;
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
  if(!confirm("Стереть все фракции, подфракции, полководцев, юниты и журнал?")) return;
  units = []; factions = []; subfactions = []; commanders = []; log = [];
  turn = 1; nextId = 1; undoStack = []; mapImage = null;
  localStorage.removeItem(LS_KEY);
  renderAll(); renderUndoBtn();
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
          type: d.type === "cavalry" ? "cavalry" : "infantry",
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

// ═══════════ журнал ═══════════
function addLogNoUndo(title, lines, tone){
  log.unshift({id: Date.now()+Math.random(), turn, title, lines: lines||[], tone: tone||"info"});
  renderLog();
  saveState();
}
const addLog = addLogNoUndo;

const DETAIL_RE = /^(Бросок d|Атака:|Усталость |Боевой дух |🐎 Натиск|Кавалерия без натиска|⚜ |Защита цели|Ситуативный модификатор|Помеха)|— стрелки в ближнем бою/;
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
    <div class="frow2">
      <div><label>Мастерство ЭМ</label><input id="f_mastery" type="number" value="0"></div>
      <div><label>Усталость</label><input id="f_fatigue" type="number" value="0"></div>
    </div>
    <div class="hint" id="typeHint" style="margin:0 0 8px"></div>
    <div class="btnrow" style="display:flex">
      <button class="gold" onclick="saveUnit()">${isNew ? "Добавить" : "Сохранить"}</button>
      <button onclick="hideForm()">Отмена</button>
    </div>
  </div>`;
}
const FORM_IDS = ["f_name","f_type","f_weapon","f_faction","f_sub","f_cmdr","f_soldiers","f_disc",
                  "f_morale","f_eqAtk","f_eqDef","f_exp","f_mastery","f_fatigue"];
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
    $("f_mastery").value=u.mastery; $("f_fatigue").value=u.fatigue;
  } else {
    $("f_name").value=""; $("f_type").value="infantry"; $("f_weapon").value="melee";
    $("f_faction").value=""; onUnitFactionChange();
    $("f_sub").value=""; $("f_cmdr").value="";
    $("f_soldiers").value=300; $("f_disc").value=50; $("f_morale").value=60;
    $("f_eqAtk").value=30; $("f_eqDef").value=30; $("f_exp").value=20;
    $("f_mastery").value=0; $("f_fatigue").value=0;
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
    units.push(Object.assign({id: nextId++, initial: clean.soldiers, status:"active",
      turnsActive:0, fleeChecks:0, breakGrace:0, broken:false,
      acted:false, attacksMade:0, countersMade:0, totKilled:0, totWounded:0,
      onMap:false, mapX:50, mapY:50, facing:0}, clean));
    addLog(`Юнит «${clean.name}» встал в строй`,
      [`${clean.soldiers} солдат · дисц ${clean.discipline} · БД ${clean.morale} · ${factionName(clean.factionId)}${clean.subfactionId ? " / " + subName(clean.subfactionId) : ""}`]);
  }
  editingId = null; renderAll(); saveState();
}

// ═══════════ юниты: операции ═══════════
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
  renderFactions(); renderCmdrs(); renderUnits(); renderLog(); renderNecro();
  renderSummary(); renderQueue(); renderUndoBtn(); renderMap();
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
  const r = Engine.resolveBattle(A, B, req, engineCtx());
  if(!r.ok){ addLog(r.title, r.lines, r.tone); return; }
  pushUndo(`бой ${A.name} → ${B.name}`);
  r.patches.forEach(p => updUnit(p.id, p.patch));
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
  renderAll(); saveState();
}

// ═══════════ конец хода ═══════════
function endTurn(){
  pushUndo(`конец хода ${turn}`);
  const r = Engine.endTurn(units, engineCtx());
  units = r.units;
  turn += 1;
  $("turnNum").textContent = turn;
  addLog(`— Конец хода ${turn - 1} —`, r.lines.length ? r.lines : ["Без изменений"]);
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
    if(canAttack){
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
  items += `<div style="display:flex;gap:5px;margin-bottom:5px;align-items:center">
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
function renderMap(){
  const wrap = $("mapWrap"); if(!wrap) return;
  const o = readMapOpts();
  const img = $("mapImg"), empty = $("mapEmpty"), gover = $("gridOver");
  if(mapImage){
    img.src = mapImage; img.classList.remove("hidden"); empty.classList.add("hidden");
  } else {
    img.classList.add("hidden"); img.removeAttribute("src"); empty.classList.remove("hidden");
  }
  gover.classList.toggle("hidden", !o.grid);
  if(o.grid){
    const step = 100 / o.cells;
    gover.style.backgroundSize = `${step}% ${step}%`;
  }
  const attId = +$("attSel").value, defId = +$("defSel").value;
  const layer = $("tokenLayer");
  const attacker = mapAttackerId ? units.find(u => u.id === mapAttackerId) : null;
  wrap.classList.toggle("targeting", !!attacker);
  layer.innerHTML = units.filter(u => u.onMap).map(u => {
    const col = factionColor(u.factionId);
    const sel = u.id === attId ? "sel-a" : u.id === defId ? "sel-b" : "";
    const stateCls = u.status === "fled" ? "fled" : u.status === "destroyed" ? "dead" : "";
    const size = Math.round(o.tokenSize * (u.tokenScale || 1));
    const shape = tokenShape(u);
    const tw = shape === "rect" ? Math.round(size * 1.45) : shape === "tri" ? Math.round(size * 1.2) : size;
    const th = shape === "rect" ? Math.round(size * 0.72) : size;
    const fsz = Math.round((shape === "tri" ? size * 0.3 : size * 0.38));
    const fac = Math.round(u.facing || 0);
    const initials = u.name.split(/\s+/).slice(0,2).map(x => x[0] || "").join("").toUpperCase();
    let targetCls = "";
    if(attacker){
      if(u.id === attacker.id) targetCls = "attacker";
      else if(isValidTarget(attacker, u)) targetCls = "valid-target";
    }
    const st = moraleStage(u.morale);
    const cmdr = getCmdr(u);
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
  if(attacker){
    layer.insertAdjacentHTML("beforeend",
      `<div class="targethint">Выбери цель для «${esc(attacker.name)}» · ${esc(MODES[mapMode(attacker)])}${mapCharge ? " · натиск" : ""} · Esc — отмена</div>`);
  }
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
// перетаскивание фишек
(function(){
  let tok = null, tid = null, moved = false, groupStart = null, dragStart = null;
  let marquee = null, mqStart = null, mqShift = false;

  document.addEventListener("mousedown", e => {
    if(e.button !== 0) return;
    const wrap = $("mapWrap"); if(!wrap) return;
    const t = e.target.closest && e.target.closest(".token");
    if(t){
      tok = t; tid = +t.dataset.tid; moved = false;
      t.classList.add("dragging");
      const r = wrap.getBoundingClientRect();
      dragStart = {x: (e.clientX - r.left) / r.width * 100, y: (e.clientY - r.top) / r.height * 100};
      // тянем всю выделенную группу, если фишка входит в выделение
      groupStart = selectedTokens[tid] && selectedIds().length > 1
        ? selectedIds().map(id => { const u = units.find(z => z.id === id); return {id, x: u.mapX, y: u.mapY}; })
        : null;
      e.preventDefault();
      return;
    }
    if(e.target.closest(".tmenu") || e.target.closest(".selbar")) return;
    if(!wrap.contains(e.target)) return;
    // рамка выделения по пустому месту карты
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
    let x = clamp((e.clientX - r.left) / r.width * 100, 0, 100);
    let y = clamp((e.clientY - r.top) / r.height * 100, 0, 100);
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
    }
    moved = true;
  });

  document.addEventListener("mouseup", e => {
    const wrap = $("mapWrap");
    if(marquee && mqStart && wrap){
      const r = wrap.getBoundingClientRect();
      const x2 = clamp(e.clientX - r.left, 0, r.width), y2 = clamp(e.clientY - r.top, 0, r.height);
      const x1 = mqStart.x, y1 = mqStart.y;
      const box = {l: Math.min(x1,x2)/r.width*100, t: Math.min(y1,y2)/r.height*100,
                   rt: Math.max(x1,x2)/r.width*100, b: Math.max(y1,y2)/r.height*100};
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
        groupStart.forEach(g => {
          const el = document.querySelector(`.token[data-tid="${g.id}"]`);
          if(el) updUnit(g.id, {mapX: parseFloat(el.style.left), mapY: parseFloat(el.style.top)});
        });
      } else {
        updUnit(tid, {mapX: parseFloat(tok.style.left), mapY: parseFloat(tok.style.top)});
      }
      saveState(); renderMap();
    } else {
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
  document.addEventListener("wheel", e => {
    const t = e.target.closest && e.target.closest(".token");
    if(!t) return;
    e.preventDefault();
    const id = +t.dataset.tid;
    const u = units.find(z => z.id === id); if(!u) return;
    if(e.ctrlKey || e.altKey){
      scaleToken(id, e.deltaY > 0 ? 0.9 : 1.111);
      return;
    }
    const step = e.shiftKey ? 5 : 15;
    rotateUnit(id, e.deltaY > 0 ? step : -step);
  }, {passive:false});
  document.addEventListener("keydown", e => {
    if(e.key === "Escape"){
      if(mapAttackerId) cancelTargeting();
      else if(openMenuId) closeTokenMenu();
      else if(selectedIds().length) clearSelection();
    }
  });
  document.addEventListener("mousedown", e => {
    if(!e.target.closest) return;
    if(e.target.closest(".tmenu") || e.target.closest(".token") || e.target.closest(".selbar")) return;
    if(openMenuId) closeTokenMenu();
    else if(mapAttackerId && e.target.closest("#mapWrap")) cancelTargeting();
  });
})();

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
