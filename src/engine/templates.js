// ═══════════ templates.js — шаблоны отрядов ═══════════
// ЧЕРНОВИК ДО ГМа (SPEC, этап 3, Q17 — А+В).
// Базовые профили посчитаны по сохранению Алекса armiya_hod1.txt (81 отряд, 6 фракций, до боя):
// берётся по одному отряду на название (клоны «№2…№9» не перевешивают), медиана по категории,
// округление до десятков. Размер отряда — 1000: так у Алекса почти везде.
// Поверх базы — правки из партии: общие (для всех фракций) и фракционные (по названию фракции).
import { guessUnitType } from "./units.js";

export const TEMPLATE_STATS = ["discipline", "morale", "eqAtk", "eqDef", "exp", "mastery"];
// Что можно переопределить в партии и в каких пределах (те же, что у формы отряда)
export const TEMPLATE_LIMITS = {
  size:       [1, 100000],
  discipline: [1, 100],
  morale:     [0, 150],
  eqAtk:      [0, 1000],
  eqDef:      [0, 1000],
  exp:        [0, 100],
  mastery:    [0, 1000],
};

export const BASE_TEMPLATES = [
  { id: "militia", name: "Ополчение", type: "infantry", weapon: "melee", size: 1000,
    discipline: 40, morale: 70, eqAtk: 40, eqDef: 40, exp: 0, mastery: 0,
    source: "медиана 9 отрядов ополчения (Вестгейт, Лоутайд, Миствелл, Гринпорт, Саммерхол, демониты, Данмир)" },
  { id: "infantry", name: "Пехота", type: "infantry", weapon: "melee", size: 1000,
    discipline: 50, morale: 70, eqAtk: 60, eqDef: 60, exp: 20, mastery: 10,
    source: "в сохранениях нет ни одного отряда — середина между ополчением и пешими рыцарями (К2)" },
  { id: "guard", name: "Гвардия", type: "infantry", weapon: "melee", size: 1000,
    discipline: 80, morale: 120, eqAtk: 90, eqDef: 90, exp: 70, mastery: 60,
    source: "один отряд: Королевская стража Данмира" },
  { id: "militia_archers", name: "Лучники ополчения", type: "archer", weapon: "ranged", size: 1000,
    discipline: 30, morale: 70, eqAtk: 30, eqDef: 30, exp: 0, mastery: 0,
    source: "медиана 3 отрядов: лучники-ополченцы Гринпорта, Миствелла, Саммерхола" },
  { id: "archers", name: "Лучники", type: "archer", weapon: "ranged", size: 1000,
    discipline: 70, morale: 80, eqAtk: 70, eqDef: 30, exp: 20, mastery: 40,
    source: "медиана 4 отрядов: лучники Лоутайда, Миствуда, Сокола, охотники Королевского леса" },
  { id: "crossbowmen", name: "Арбалетчики", type: "archer", weapon: "ranged", size: 1000,
    discipline: 60, morale: 70, eqAtk: 60, eqDef: 60, exp: 30, mastery: 20,
    source: "медиана 4 отрядов: наёмные, Кристофаль, Вестстейр, Данмир" },
  { id: "pikemen", name: "Пикинёры", type: "pike", weapon: "melee", size: 1000,
    discipline: 60, morale: 80, eqAtk: 30, eqDef: 70, exp: 20, mastery: 0,
    source: "среднее 2 отрядов: пикинёры Лэйкмура и Вестстейра" },
  { id: "foot_knights", name: "Пешие рыцари", type: "infantry", weapon: "melee", size: 1000,
    discipline: 60, morale: 60, eqAtk: 60, eqDef: 60, exp: 10, mastery: 20,
    source: "один отряд: рыцари Аринфура (единственные рыцари, оставшиеся пехотой к ходу 16)" },
  { id: "knights", name: "Конные рыцари", type: "cavalry", weapon: "melee", size: 1000,
    discipline: 40, morale: 80, eqAtk: 80, eqDef: 80, exp: 20, mastery: 20,
    source: "медиана 5 отрядов рыцарей-кавалерии: Лоутайд, Миствелл, Лэйкмур, Артедайн, Саммерхолл" },
  { id: "elite_cavalry", name: "Элитная конница", type: "cavalry", weapon: "melee", size: 1000,
    discipline: 80, morale: 100, eqAtk: 80, eqDef: 80, exp: 60, mastery: 40,
    source: "один отряд: рыцари серого Ордена" },
];

export const getTemplate = id => BASE_TEMPLATES.find(t => t.id === id) || null;

// Профессиональная пехота: названия, по которым отряд — не ополчение
const PRO_INFANTRY = /мечник|горц|латник|секирщ|топорщ|щитонос|легионер|берсерк|наемник|наёмник|дружин|воин/;

// Какой шаблон подходит отряду. g — результат guessUnitType (можно не передавать).
// fallback: true — название ничего не подсказало, взято ополчение; в предпросмотре это видно.
export function matchTemplate(name, g = guessUnitType(name)){
  const n = (name || "").toLowerCase().replace(/ё/g, "е");
  const type = g ? g.type : null;
  if(/арбалет/.test(n)) return {id: "crossbowmen", fallback: false};
  if(type === "archer") return {id: /ополч|крестьян/.test(n) ? "militia_archers" : "archers", fallback: false};
  if(type === "pike") return {id: "pikemen", fallback: false};
  if(type === "cavalry") return {id: /элит|гвард|орден/.test(n) ? "elite_cavalry" : "knights", fallback: false};
  if(/рыцар/.test(n)) return {id: "foot_knights", fallback: false};
  if(/ополч|крестьян/.test(n)) return {id: "militia", fallback: false};
  if(/гвард|страж|телохран|лейб/.test(n)) return {id: "guard", fallback: false};
  if(PRO_INFANTRY.test(n)) return {id: "infantry", fallback: false};
  if(type === "infantry") return {id: "militia", fallback: false};
  return {id: "militia", fallback: true};
}

// Правки шаблонов хранятся в партии: {base: {id: {поле: число}}, factions: {"Фракция": {id: {…}}}}
export function normalizeOverrides(o){
  const out = {base: {}, factions: {}};
  if(!o || typeof o !== "object") return out;
  const clean = src => {
    const r = {};
    for(const [id, patch] of Object.entries(src || {})){
      if(!getTemplate(id) || !patch || typeof patch !== "object") continue;
      const p = {};
      for(const [k, v] of Object.entries(patch))
        if(TEMPLATE_LIMITS[k] && Number.isFinite(+v)) p[k] = clampField(k, +v);
      if(Object.keys(p).length) r[id] = p;
    }
    return r;
  };
  out.base = clean(o.base);
  for(const [name, src] of Object.entries(o.factions || {})){
    const c = clean(src);
    if(name.trim() && Object.keys(c).length) out.factions[name.trim()] = c;
  }
  return out;
}
export function clampField(k, v){
  const [lo, hi] = TEMPLATE_LIMITS[k] || [-Infinity, Infinity];
  return Math.min(hi, Math.max(lo, Math.round(v)));
}
// Правки фракции ищутся по названию без учёта регистра
export function factionOverrides(overrides, factionName){
  const f = (overrides && overrides.factions) || {};
  const key = Object.keys(f).find(k => k.toLowerCase() === String(factionName || "").trim().toLowerCase());
  return key ? f[key] : {};
}
// Итоговый профиль: база → общие правки → правки фракции
export function resolveTemplate(id, factionName, overrides){
  const base = getTemplate(id) || BASE_TEMPLATES[0];
  const common = ((overrides && overrides.base) || {})[base.id] || {};
  const own = factionOverrides(overrides, factionName)[base.id] || {};
  return Object.assign({}, base, common, own, {id: base.id, name: base.name});
}
