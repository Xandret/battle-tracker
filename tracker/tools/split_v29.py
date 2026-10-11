# ═══════════ split_v29.py — разовая миграция v29 → модульный проект ═══════════
# Берёт v29, вырезает из интерфейсного скрипта боевое ядро (оно теперь в src/engine),
# ставит на его место тонкие обёртки и раскладывает результат на шаблон и скрипт интерфейса.
# Запуск: python3 tools/split_v29.py path/to/tracker_boya_v29.html
import re, sys

src = open(sys.argv[1], encoding='utf-8').read()
i = src.index('<script>'); j = src.rindex('</script>')
head, js, tail = src[:i], src[i+8:j], src[j+9:]
lines = js.split('\n')

def find_block(start_pat):
    """Первая строка и строка после конца объявления — по балансу скобок."""
    for n, l in enumerate(lines):
        if re.match(start_pat, l):
            depth = 0; seen = False
            for m in range(n, len(lines)):
                for ch in lines[m]:
                    if ch in '{(': depth += 1; seen = True
                    elif ch in '})': depth -= 1
                if seen and depth == 0:
                    return n, m + 1
                if not seen and lines[m].rstrip().endswith(';'):
                    return n, m + 1
    raise SystemExit('not found: ' + start_pat)

REMOVE = [
    r'^const rnd = ', r'^const clamp = ', r'^const r1 = ',
    r'^const MODES = \{', r'^const isMeleeMode = ', r'^const attackLimit = ', r'^const counterLimit = ',
    r'^function moraleStage\(', r'^function discStage\(',
    r'^const isCav = ', r'^const isPike = ', r'^const isArcherType = ',
    r'^function attackSector\(', r'^const SECTOR_RU = ', r'^const graceByDisc = ',
    r'^function applyMoraleChange\(', r'^function effStats\(', r'^function computeStrike\(',
    r'^function applyCasualties\(', r'^const canBeTargeted = ',
]
REPLACE = {
    r'^function resolveBattle\(\)': '''function resolveBattle(){
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
}''',
    r'^function moraleCheck\(': '''function moraleCheck(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`проверка БД: ${u.name}`);
  const r = Engine.moraleCheck(u, engineCtx());
  updUnit(u.id, r.patch);
  addLog(r.title, r.lines, r.tone === "info" ? undefined : r.tone);
  renderAll(); saveState();
}''',
    r'^function fleeCheck\(': '''function fleeCheck(id){
  const u = units.find(x => x.id === id); if(!u) return;
  pushUndo(`проверка на побег: ${u.name}`);
  const r = Engine.fleeCheck(u, engineCtx());
  updUnit(u.id, r.patch);
  addLog(r.title, r.lines, r.tone === "info" ? undefined : r.tone);
  renderAll(); saveState();
}''',
    r'^function endTurn\(\)': '''function endTurn(){
  pushUndo(`конец хода ${turn}`);
  const r = Engine.endTurn(units, engineCtx());
  units = r.units;
  turn += 1;
  $("turnNum").textContent = turn;
  addLog(`— Конец хода ${turn - 1} —`, r.lines.length ? r.lines : ["Без изменений"]);
  renderAll(); saveState();
}''',
}

spans = [(*find_block(p), None) for p in REMOVE] + [(*find_block(p), new) for p, new in REPLACE.items()]
spans.sort(reverse=True)
for a, b, new in spans:
    lines[a:b] = [] if new is None else new.split('\n')
ui = '\n'.join(lines)

BRIDGE = '''// ═══════════ связь с движком ═══════════
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
'''
def sub(old, new, text, label):
    assert old in text, 'patch failed: ' + label
    return text.replace(old, new, 1)

ui = sub('"use strict";', '"use strict";\n' + BRIDGE, ui, 'bridge')
ui = sub('  return {factions, subfactions, commanders, units, log: log.slice(0,300), turn, nextId,',
         '  return {factions, subfactions, commanders, units, log: log.slice(0,300), turn, nextId, ruleset,', ui, 'stateObj')
ui = sub('  log = s.log || []; turn = s.turn || 1; nextId = s.nextId || 1;',
         '''  log = s.log || []; turn = s.turn || 1; nextId = s.nextId || 1;
  ruleset = RULESETS[s.ruleset] ? s.ruleset : "base";
  if($("rulesetSel")) $("rulesetSel").value = ruleset;''', ui, 'applyLoadedState')

NEWS = '''function toggleNews(){ $("newsPanel").classList.toggle("hidden"); }
function renderPatchNotes(){
  const src = ($("patchnotes") || {}).textContent || "";
  const fmt = t => esc(t).replace(/\\*\\*(.+?)\\*\\*/g, "<b>$1</b>").replace(/`([^`]+)`/g, "<code>$1</code>");
  let html = "", inCode = false, inList = false;
  src.split("\\n").forEach(l => {
    if(l.startsWith("```")){ if(inList){ html += "</ul>"; inList = false; } html += inCode ? "</pre>" : "<pre>"; inCode = !inCode; return; }
    if(inCode){ html += esc(l) + "\\n"; return; }
    if(/^\\s*- /.test(l)){ if(!inList){ html += "<ul>"; inList = true; } html += "<li>" + fmt(l.replace(/^\\s*- /, "")) + "</li>"; return; }
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
function toggleRef(){'''
ui = sub('function toggleRef(){', NEWS, ui, 'news')
ui = sub('loadState();\nrenderAll();', 'loadState();\nrenderRulesetSel();\nrenderPatchNotes();\nrenderAll();', ui, 'startup')

h = head
h = sub('<title>Журнал боевых действий — тактический трекер v29</title>',
        '<title>Журнал боевых действий — v__VERSION__ «__PATCHNAME__»</title>', h, 'title')
h, cnt = re.subn(r'<div class="sub">Тактический трекер v29[^<]*</div>',
                 '<div class="sub">Тактический трекер v__VERSION__ · обновление «__UPDATE__» · патч «__PATCHNAME__»</div>', h)
assert cnt == 1, 'subtitle'
h = sub('    <button class="gold" onclick="endTurn()">Конец хода</button>',
        '    <select id="rulesetSel" onchange="onRulesetChange()" title="Набор правил" style="width:auto"></select>\n'
        '    <button class="gold" onclick="endTurn()">Конец хода</button>', h, 'ruleset select')
h = sub('    <button onclick="toggleRef()">Справка</button>',
        '    <button onclick="toggleNews()">Что нового</button>\n    <button onclick="toggleRef()">Справка</button>', h, 'news button')
h = sub('<div class="panel hidden mb8" id="refPanel"',
        '<div class="panel hidden" id="newsPanel" style="margin-bottom:16px">\n  <h2>Что нового</h2>\n'
        '  <div class="news" id="newsBody"></div>\n</div>\n\n<div class="panel hidden mb8" id="refPanel"', h, 'news panel')
h = sub('  .hidden{display:none;}', '''  .hidden{display:none;}
  .news{max-height:60vh;overflow-y:auto;font-size:13px;line-height:1.55;color:#C8C2AE;}
  .news h2{font-size:18px;color:var(--brass);margin:6px 0 8px;border:none;padding:0;}
  .news h3{font-family:'Arial Narrow',sans-serif;font-size:15px;color:var(--brass);margin:14px 0 6px;text-transform:uppercase;}
  .news h4{font-family:'Arial Narrow',sans-serif;font-size:13px;color:var(--text);margin:10px 0 4px;}
  .news pre{background:#14181A;border:1px solid var(--line);border-radius:4px;padding:8px 10px;
    font-family:'Courier New',monospace;font-size:12px;overflow-x:auto;white-space:pre;}
  .news code{background:#14181A;padding:1px 4px;border-radius:3px;font-family:'Courier New',monospace;}
  .news ul{margin:4px 0 8px;padding-left:20px;}
  .news hr{border:none;border-top:1px solid var(--line);margin:14px 0;}''', h, 'news css')

template = (h + '<script type="text/plain" id="patchnotes">__PATCHNOTES__</script>\n'
            '<script>\n"use strict";\n/*__ENGINE__*/\n</script>\n<script>\n/*__APP__*/\n</script>' + tail)
open('src/ui/app.js', 'w', encoding='utf-8').write(ui)
open('src/ui/template.html', 'w', encoding='utf-8').write(template)
print(f"интерфейс: {ui.count(chr(10))} строк · шаблон: {template.count(chr(10))} строк")
