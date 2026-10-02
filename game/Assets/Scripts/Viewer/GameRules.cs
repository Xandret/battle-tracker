// ═══════════ GameRules.cs — по каким правилам игра (Play) и смотрелка считают бой ═══════════
// Набор 1 (Rules.Base) — эталон стола, в нём движение фигурками. Игре нужно движение по бойцам-телам (Б1–Б4, MenBodies):
// каждый боец идёт своим телом и занимает место шагом, а не прыжком; конь разворачивается, а не едет задом (Г94);
// старт волной, натиск телами. Так Алекс видит бой в игре (жалоба по записи хода 9–13, 03.10.2026).
// Когда ядро сделает MenBodies умолчанием (Г92), здесь останется просто Rules.Base.
using BattleCore;

namespace Journal.Viewer
{
    public static class GameRules
    {
        public static readonly Rules Game = Make();
        static Rules Make() { var r = new Rules(); r.Move.MenBodies = true; return r; }
    }
}
