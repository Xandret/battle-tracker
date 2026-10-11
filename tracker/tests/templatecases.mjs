// ═══════════ templatecases.mjs — общие сценарии шаблонов отрядов для двух реализаций (правило 7) ═══════════
// Что считают units.js (guessUnitType) и templates.js (matchTemplate, normalizeOverrides, resolveTemplate),
// на названиях из сохранений Алекса и на крайних случаях. Результат замораживается в shared/golden/templates.json
// (tools/export-template-golden.mjs); движок на C# (core/Tests/TemplateCases.cs) обязан совпасть.
// Тест templategolden.test.mjs ловит, если сам JS ушёл от замороженного.
import { guessUnitType } from "../src/engine/units.js";
import { BASE_TEMPLATES, matchTemplate, normalizeOverrides, resolveTemplate, clampField } from "../src/engine/templates.js";

// Названия отрядов из armiya_hod1, armiya_hod5 и armiya_hod16 (клоны «№2…№14» убраны — подбор их не различает)
export const SAVE_NAMES = [
  "Ополчение Вестгейта", "Наёмные арбалетчики", "Лоутайдское ополчение с мечами", "Лоутайдское ополчение",
  "Лоутайдские лучники", "Рыцари Лоутайда", "Рыцари серого Ордена", "Рыцари Миствелла", "Ополчение Миствелла",
  "Лучники Миствуда", "Охотники Королевского леса", "Рыцари Лэйкмура", "Пикинёры Лэйкмура",
  "Ополчение Гринпорта с Алебардами", "Ополчение Гринпорта", "Лучники ополченцы Гринпорта",
  "Лучники ополченцы Миствелла", "Рыцари Артедайна", "Рыцари Саммерхолла", "Ополчение Саммерхола",
  "Лучники ополченцы Саммерхола", "Ополчение демонитов", "Рыцари Аринфура", "Самураи", "Лучники Сокола",
  "Арбалетчики", "Роххирим", "Пикинёры Вестстейра", "Арбалетчики Вестстейра", "Королевская стража Данмира",
  "Арбалетчики Данмира", "Данмирское ополчение", "Элитные всадники Бладколла", "Всадники Бладколла",
  "Рыцари Серого Ордена", "Ополчение Эштауна", "Пикинёры Эштауна", "Эштаунские рыцари", "Всадники Грейфельдов",
  "Лучники Эштауна", "Самураи 2", "Ополчение Эштауна №14",
];
// Крайние случаи: «пеш…» только с начала слова, ё, регистр, конные стрелки, пустое и чужое название
export const EDGE_NAMES = [
  "Пешие рыцари", "пешая стража", "Рыцари Цепешей", "Спешенные рыцари", "Рыцари-пешие", "ПЕШИЕ солдаты",
  "Конные лучники", "Всадники-лучники", "Рыцари с арбалетами", "Конные арбалетчики", "Роханцы", "Орда степи",
  "Степняки", "Конная гвардия", "Гвардейская конница Ордена", "Лейб-гвардия", "Телохранители князя",
  "Стражники ворот", "Наемники", "Наёмники", "НАЁМНЫЕ МЕЧНИКИ", "Горцы", "Дружина", "Легионеры", "Берсерки",
  "Латники", "Секирщики", "Топорщики", "Щитоносцы", "Воины света", "Солдаты", "Пехота", "Крестьяне",
  "Крестьяне-лучники", "Ополченцы с луками", "Копейщики", "Алебардщики", "Бердышники", "Фаланга",
  "Стражники с алебардами", "Пращники", "Мушкетёры", "Егеря", "Йомены", "Застрельщики", "Метатели дротиков",
  "Драгуны", "Гусары", "Уланы", "Кирасиры", "Катафракты", "Паладины", "Витязи", "Мамлюки", "Сипахи",
  "Жандармы", "Отряд №5", "Банда Хрюка", "Pikemen", "Knights", "", "   ", "ЁЖИКИ", "Самурай",
  "Рыцари-ополченцы", "Ополчение рыцарей", "Гвардия арбалетчиков", "Королевские лучники-гвардейцы",
];

// Правки шаблонов: как их пишет трекер, и как они могут прийти из старой или поправленной руками партии
export const OVERRIDES = [
  null,
  "строка",
  [1, 2, 3],
  {},
  {base: null, factions: null},
  {
    base: {
      militia: {discipline: 55.5, morale: "90", eqAtk: "", exp: null, mastery: "abc", size: 1e9, foo: 5},
      archers: {eqDef: -0.4, eqAtk: 2.5, morale: -2.5, mastery: 1000.4999},
      unknown: {discipline: 10},
      guard: {},
      pikemen: {foo: 1, bar: 2},
      knights: [70, 80],
      crossbowmen: "60",
      infantry: null,
    },
    factions: {
      " Гринпорт ": {archers: {eqAtk: 99.5, eqDef: -3}, militia: {morale: 151}},
      "": {militia: {morale: 1}},
      "   ": {militia: {morale: 2}},
      "Пусто": {militia: {bar: 1}},
      "Данмир": {knights: {discipline: true, morale: [70], eqAtk: [1, 2], eqDef: {}, exp: " 0x10 ", mastery: "1e2"},
                 guard: {discipline: false, morale: "Infinity", eqAtk: "-0x10", eqDef: "  12.6\n", exp: ".5", mastery: "5."}},
      "Лоутайд": {foot_knights: {size: 0.4, exp: 101}},
      "лоутайд": {foot_knights: {size: 7}},
      "ВЕСТСТЕЙР": {pikemen: {size: "1 000", discipline: "+7", morale: "-7", eqAtk: "1_0", eqDef: "0b11", exp: "0o7"}},
    },
  },
  {base: {elite_cavalry: {size: 2000, morale: 110}}},
  {factions: {"Эштаун": {militia: {size: 1500, discipline: 45}}, "Бладколл": {elite_cavalry: {eqAtk: 95}}}},
  // Фракции списком (руками поправленная партия): Object.entries даёт номера — фракция «0»
  {base: [{discipline: 5}], factions: [{militia: {morale: 99}}, "лучники"]},
];
// Что спрашиваем у каждой правки: шаблон и фракция (фракция ищется без учёта регистра, по краям обрезается)
export const RESOLVE = [
  ["militia", ""], ["militia", null], ["militia", "гринпорт"], ["archers", "ГРИНПОРТ"], ["archers", "  Гринпорт  "],
  ["knights", "Данмир"], ["guard", "данмир"], ["elite_cavalry", "Данмир"], ["foot_knights", "Лоутайд"],
  ["foot_knights", "ЛОУТАЙД "], ["pikemen", "Вестстейр"], ["militia", "Эштаун"], ["elite_cavalry", "Бладколл"],
  ["elite_cavalry", "Чужая"], ["no_such_id", "Данмир"], ["infantry", "Пусто"], ["crossbowmen", ""], ["militia", "0"],
];

// Профиль шаблона в сравнении — только данные, без подписи об источнике
const FIELDS = ["id", "name", "type", "weapon", "size", "discipline", "morale", "eqAtk", "eqDef", "exp", "mastery"];
const pick = t => Object.fromEntries(FIELDS.map(k => [k, t[k]]));

export function buildTemplateCases(){
  const names = [...SAVE_NAMES, ...EDGE_NAMES, null].map(name => {
    const guess = guessUnitType(name);
    return {name, guess, match: matchTemplate(name, guess)};
  });
  const overrides = OVERRIDES.map(input => {
    const normalized = normalizeOverrides(input);
    return {input, normalized, resolve: RESOLVE.map(([id, faction]) => ({id, faction, out: pick(resolveTemplate(id, faction, normalized))}))};
  });
  const clamp = [];
  for(const k of ["size", "discipline", "morale", "eqAtk", "exp", "nope"])
    for(const v of [-1e9, -0.5, 0, 0.49, 0.5, 1.5, 2.5, 99.5, 100.5, 1000.5, 1e9])
      clamp.push({k, v, out: clampField(k, v)});
  return {base: BASE_TEMPLATES.map(pick), names, overrides, clamp};
}
