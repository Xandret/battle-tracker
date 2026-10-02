// ═══════════ looks.js — облик отряда: стиль и снаряжение (В16) ═══════════
// Не правила боя: только как отряд выглядит в игре (Unity) — на счёт боя не влияет. Поля отряда `style` и `kit`
// выбираются при создании; нет (старые партии) — угадываются по названию. Та же логика — в
// game/Assets/Scripts/Art/Styles.cs; сверка обеих — shared/golden/looks.json (tracker/tools/export-look-golden.mjs).
import { guessUnitType } from "./units.js";
import { matchTemplate } from "./templates.js";

// Стили снаряжения XIV–XV веков
export const LOOK_STYLES = [
  {id: "west",    name: "Западный",        note: "Франция, Англия, Империя: бацинеты, топфхелмы, треугольные щиты, сюрко, арбалеты и длинные луки"},
  {id: "north",   name: "Северный",        note: "Скандинавия, Русь, Балтика: шишаки, круглые и каплевидные щиты, кольчуга и чешуя, топоры, плащи"},
  {id: "east",    name: "Восточный",       note: "Византия, степь, Персия: ламелляр, остроконечные шлемы с бармицей, круглые щиты, сабли, составные луки"},
  {id: "south",   name: "Южный",           note: "Италия, Иберия, мавры: бригантины, салады и барбюты, адарги, дротики"},
  {id: "fareast", name: "Дальневосточный", note: "Япония: о-ёрой, кабуто, нагината, юми, сасимоно"},
];
export const LOOK_STYLE_DEFAULT = "west";
// Наборы снаряжения; tpl — шаблон, чей это облик
export const LOOK_KITS = [
  {id: "militia",  name: "Ополчение",      tpl: "militia"},
  {id: "spear",    name: "Копейщики",      tpl: "infantry"},
  {id: "sword",    name: "Мечники",        tpl: "guard"},
  {id: "pike",     name: "Пикинёры",       tpl: "pikemen"},
  {id: "bow",      name: "Лучники",        tpl: "archers"},
  {id: "crossbow", name: "Арбалетчики",    tpl: "crossbowmen"},
  {id: "lance",    name: "Рыцари",         tpl: "knights"},
  {id: "barded",   name: "Тяжёлые рыцари", tpl: "elite_cavalry"},
];
// облик каждого шаблона (Kits.LookByTpl в Unity)
export const KIT_BY_TEMPLATE = {
  militia: "militia", infantry: "spear", guard: "sword", foot_knights: "sword", pikemen: "pike",
  militia_archers: "bow", archers: "bow", crossbowmen: "crossbow", knights: "lance", elite_cavalry: "barded",
};

const STYLE_HINTS = [
  ["fareast", ["самура", "ронин", "асигару", "сохей", "ниндзя", "катан", "нагинат"]],
  ["north",   ["викинг", "хускарл", "дружин", "варяг", "ярл", "хирд", "берсерк", "норд"]],
  ["east",    ["мамлюк", "гулям", "степ", "кочев", "катафракт", "визант", "печенег", "половц", "монгол", "татар", "янычар", "сипах", "конные лучники"]],
  ["south",   ["кондотьер", "мавр", "альмогавар", "генуэз", "венеци", "арагон", "кастил", "иберий", "андалус"]],
];
const nameKey = name => (name || "").toLowerCase().replace(/ё/g, "е");
export const isLookStyle = id => LOOK_STYLES.some(s => s.id === id);
export const isLookKit = id => LOOK_KITS.some(k => k.id === id);

// Стиль по названию; не угадали — null
export function guessStyle(name){
  const n = nameKey(name);
  for(const [id, words] of STYLE_HINTS) if(words.some(w => n.includes(w))) return id;
  return null;
}

// Снаряжение: облик шаблона, который подбирается по названию (matchTemplate), но род войск отряда важнее угаданного,
// если он не стоит по умолчанию (пехота ближнего боя — так создаётся любой отряд); верхом — только конница: пеший
// отряд «Рыцари Лоутайда» — пешие рыцари. Что название называет прямо (арбалетчики, мечники, латники, тяжёлая
// конница) — поверх шаблона.
export function guessKit(name, type, weapon){
  const n = nameKey(name);
  if(n.includes("арбалет")) return "crossbow";
  const byDefault = type == null || (type === "infantry" && weapon !== "ranged");
  let g = byDefault ? guessUnitType(name) : {type: type === "infantry" ? "archer" : type, weapon: weapon || "melee"};
  if(type != null && type !== "cavalry" && g && g.type === "cavalry") g = {type: "infantry", weapon: "melee"};
  const m = matchTemplate(name, g || null);
  const kind = g ? g.type : type;
  // ополчение шаблон даёт и любой нераспознанной пехоте — облик ополчения только тем, кто так и назван
  const militia = /ополч|крестьян|новобран/.test(n);
  let look = !m.fallback && (m.id !== "militia" || militia) && KIT_BY_TEMPLATE[m.id] ? KIT_BY_TEMPLATE[m.id]
    : kind === "cavalry" ? "lance" : kind === "pike" ? "pike" : kind === "archer" ? "bow" : "spear";
  if(look === "spear" && /мечник|латник/.test(n)) return "sword";
  if(look === "lance" && n.includes("тяжел")) return "barded";
  return look;
}

// Стиль нового отряда (В16, решение Алекса): как у прошлого отряда той же фракции; у первого — по названию, иначе
// западный. Сам отряд не в счёт
export function defaultStyle(units, factionId, name, exceptId){
  const fid = factionId ?? null;
  const same = (units || []).filter(u => (u.factionId ?? null) === fid && u.id !== exceptId && isLookStyle(u.style));
  if(same.length) return same[same.length - 1].style;
  return guessStyle(name) || LOOK_STYLE_DEFAULT;
}

// Облик отряда: записанный, а нет (старые партии) — угаданный, без записи
export const styleOf = u => isLookStyle(u.style) ? u.style : guessStyle(u.name) || LOOK_STYLE_DEFAULT;
export const kitOf = u => isLookKit(u.kit) ? u.kit : guessKit(u.name, u.type, u.weapon);
