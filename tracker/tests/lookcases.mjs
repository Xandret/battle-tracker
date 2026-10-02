// ═══════════ lookcases.mjs — общие сценарии облика отряда (В16) для трекера и игры ═══════════
// Что считает looks.js (guessKit, guessStyle, defaultStyle) на названиях из сохранений Алекса и крайних случаях,
// при каждом роде войск. Результат замораживается в shared/golden/looks.json (tools/export-look-golden.mjs);
// Unity (game/Assets/Scripts/Art/Styles.cs, ArmyFile.DefaultStyle) обязана совпасть — сверка:
// cd game/Tools/bench && dotnet run -c Release -- looks. Тест lookgolden.test.mjs ловит, если сам JS ушёл.
import { guessKit, guessStyle, defaultStyle } from "../src/engine/looks.js";
import { SAVE_NAMES, EDGE_NAMES } from "./templatecases.mjs";

// названия, по которым угадывается стиль края
export const STYLE_NAMES = [
  "Викинги", "Хускарлы ярла", "Варяжская стража", "Дружинники", "Мамлюки", "Конные лучники степи", "Татарская конница",
  "Янычары", "Византийские катафракты", "Генуэзские арбалетчики", "Кондотьеры", "Мавры", "Альмогавары", "Асигару",
  "Ронины", "Сохеи", "Самураи с нагинатами", "КАТАНЫ", "Норды", "Берсерки", "Сипахи", "Печенеги",
];
// род войск: не задан, по умолчанию (так трекер создаёт любой отряд), и каждый заданный
export const KINDS = [[null, null], ["infantry", "melee"], ["infantry", "ranged"], ["archer", "ranged"],
                      ["archer", "melee"], ["pike", "melee"], ["cavalry", "melee"], ["cavalry", "ranged"]];

// стиль нового отряда: прошлый отряд фракции с известным стилем, у первого — по названию; сам отряд не в счёт
export const UNITS = [
  {id: 1, factionId: 1, style: "north"}, {id: 2, factionId: 1, style: "чепуха"}, {id: 3, factionId: 2, style: "east"},
  {id: 4, factionId: null, style: "south"}, {id: 5, factionId: 2}, {id: 6, factionId: 1, style: "fareast"},
];
export const DEFAULTS = [
  [1, "Пехота", null], [1, "Пехота", 6], [1, "Викинги", null], [2, "Отряд", null], [2, "Отряд", 3],
  [null, "Отряд", null], [null, "Отряд", 4], [3, "Отряд", null], [3, "Викинги", null], [1, "", 1],
];

export function buildLookCases(){
  const names = [...new Set([...SAVE_NAMES, ...EDGE_NAMES, ...STYLE_NAMES])];
  return {
    // [название, род войск, оружие, снаряжение]
    kits: names.flatMap(n => KINDS.map(([t, w]) => [n, t, w, guessKit(n, t, w)])),
    // [название, стиль или null]
    styles: names.map(n => [n, guessStyle(n)]),
    units: UNITS,
    // [фракция, название, кроме отряда №, стиль]
    defaults: DEFAULTS.map(([f, n, ex]) => [f, n, ex, defaultStyle(UNITS, f, n, ex)]),
  };
}
