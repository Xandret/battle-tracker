// ═══════════ units.js — справочники и свойства отрядов ═══════════
import { BASE_RULES } from "./rules.js";

export const MODES = {
  melee_form: "Ближний бой · в строю",
  ranged_form: "Дальний бой · в строю",
  melee_rough: "Ближний бой · пересечённая местность",
  ranged_rough: "Дальний бой · пересечённая местность",
};
export const isMeleeMode = m => m === "melee_form" || m === "melee_rough";

export const attackLimit  = (u, rules = BASE_RULES) => u.discipline >= rules.actions.eliteDisc ? 2 : 1;
export const counterLimit = (u, rules = BASE_RULES) => u.discipline >= rules.actions.eliteDisc ? 2 : 1;

export function moraleStage(bd){
  if (bd < 20)  return {label:"Дрогнули",     note:"бросок на побег с помехой",   color:"#B0402E", mult:1};
  if (bd < 40)  return {label:"Колеблются",   note:"проверка БД с помехой",        color:"#C97B2E", mult:1};
  if (bd < 50)  return {label:"Безучастны",   note:"нет эффектов",                 color:"#98A08F", mult:1};
  if (bd < 80)  return {label:"Рьяны",        note:"могут атаковать сильнейшего",  color:"#7FA05A", mult:1};
  if (bd < 120) return {label:"Воодушевлены", note:"урон +20%",                    color:"#C9A227", mult:1.2};
  return               {label:"Геройство",    note:"урон +50%",                    color:"#E0C34A", mult:1.5};
}
export function discStage(d){
  if (d < 10) return "Сброд: побег до боя, неуправляем";
  if (d < 20) return "Побег до боя, неуправляем, штраф осады";
  if (d < 30) return "Побег до боя, неуправляем";
  if (d < 40) return "Побег при низком БД, команды с трудом";
  if (d < 50) return "Осада без штрафов";
  if (d < 60) return "Может ретироваться";
  if (d < 70) return "Стоит насмерть, отмена атаки";
  if (d < 80) return "Свободный манёвр, перестроение";
  if (d < 90) return "Две атаки за ход";
  return "Элита: 2 атаки, усталость с 8-го хода";
}
export function graceByDisc(d, rules = BASE_RULES){
  for(const [from, turns] of rules.breakdown.grace) if(d >= from) return turns;
  return 0;
}

// ── Тип войск по названию отряда ──
// Раньше жило в интерфейсе; перенесено в движок, потому что им пользуется импорт армий из текста,
// а импорт должен проверяться тестами без браузера.
export const TYPE_KEYWORDS = {
  archer: ["лучник","лучниц","стрелк","стрелец","стрельц","арбалетчик","арбалетч","арбалетр",
           "пращник","пращ","застрельщик","застрельщ","охотник","егер","мушкетёр","мушкетер",
           "аркебуз","снайпер","метател","дротикомет","самура","самурай","йомен","лонгбоу"],
  cavalry:["кавалер","конниц","конн","всадник","наездник","рыцар","драгун","гусар","улан",
           "кирасир","катафракт","жандарм","ездов","верхов","витяз","паладин","сипах","мамлюк","роххирим","рохирим","рохиррим","роханц","степняк","орда"],
  pike:   ["пикинёр","пикинер","пикейщ","копейщ","копьеносц","копьенос","сарисс","фаланг",
           "алебард","бердыш","протазан","гвардейц с пиками"],
  infantry:["пехот","ополчен","ополчение","мечник","дружин","стража","стражник","гвард","воин",
            "латник","секирщ","топорщ","щитоносц","легионер","берсерк","наёмник","наемник","солдат",
            "крестьян","горц"],
};
export const HORSE_ARCHERS = ["роххирим","рохирим","рохиррим","конные лучник","конных лучник","степняк","орда","всадники-лучник"];
// «Пешие рыцари», «пешие солдаты»: слово «пеш…» отменяет кавалерию, которую подсказал бы «рыцарь».
// Только с начала слова — иначе «Рыцари Цепешей» стали бы пехотой.
export const FOOT_RE = /(^|[^а-яa-z])пеш/;
export function guessUnitType(name){
  const n = (name || "").toLowerCase().replace(/ё/g, "е");
  if(HORSE_ARCHERS.some(w => n.includes(w))) return {type:"cavalry", weapon:"ranged", why:"конные лучники"};
  const hit = key => TYPE_KEYWORDS[key].some(w => n.includes(w.replace(/ё/g, "е")));
  const isFoot = FOOT_RE.test(n);
  const isArcher = hit("archer"), isCav = !isFoot && hit("cavalry"), isPike = hit("pike");
  if(isCav && isArcher) return {type:"cavalry", weapon:"ranged", why:"конные стрелки"};
  if(isCav)    return {type:"cavalry",  weapon:"melee",  why:"кавалерия"};
  if(isPike)   return {type:"pike",     weapon:"melee",  why:"пикинёры"};
  if(isArcher) return {type:"archer",   weapon:"ranged", why:"лучники"};
  if(isFoot || hit("infantry")) return {type:"infantry", weapon:"melee", why: isFoot ? "пешие" : "пехота"};
  return null;
}

export const isCav = u => u.type === "cavalry";
export const isPike = u => u.type === "pike";
export const isArcherType = u => u.type === "archer";
export const canBeTargeted = u => u.status === "active" || u.status === "fled";

// Сектор удара относительно фасинга цели: front / flank / rear.
// Если хотя бы один из отрядов не на карте — удар считается фронтальным.
export function attackSector(att, def, rules = BASE_RULES){
  if(!att.onMap || !def.onMap) return "front";
  const dx = (att.mapX - def.mapX), dy = (att.mapY - def.mapY);
  if(Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) return "front";
  let ang = Math.atan2(dx, -dy) * 180 / Math.PI;      // 0° — вверх, по часовой
  let rel = ang - (def.facing || 0);
  while(rel > 180) rel -= 360;
  while(rel < -180) rel += 360;
  const a = Math.abs(rel);
  if(a <= rules.sectors.frontMax) return "front";
  if(a >= rules.sectors.rearMin) return "rear";
  return "flank";
}
export const SECTOR_RU = {front:"во фронт", flank:"во фланг", rear:"в тыл"};
