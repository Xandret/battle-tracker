// ═══════════ Unit.cs — отряд, полководец, патч, контекст ═══════════
// Поля и имена — как у отряда в трекере (сохранения TXT), чтобы партия и эталон читались один в один.
// Все числа — double: в JavaScript других чисел нет, и «83/2» там — 41.5, а не 41.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class Unit
    {
        public int Id;
        public string Name = "";
        public string Type = "infantry";      // infantry | cavalry | archer | pike
        public string Weapon = "melee";       // melee | ranged
        public int? FactionId, SubfactionId, CommanderId;
        public double Soldiers, Initial, Discipline, Morale, EqAtk, EqDef, Exp, Mastery, Fatigue;
        public string Status = "active";      // active | fled | destroyed
        public double TurnsActive, FleeChecks, BreakGrace, BreakPenalty;
        public bool Broken, Acted;
        public double AttacksMade, CountersMade, TotKilled, TotWounded;
        public bool OnMap;
        public double MapX = 50, MapY = 50, Facing, TokenScale = 1;
        public double MovedM, RunUpM, Range;
        public int Ranks;                     // выбранное построение — глубина строя в шеренгах (0 — по правилам стола для типа), Г101
        public string Shape = "";             // Г106: форма строя — "" или line (линия, глубина по Ranks), wedge (клин), crescent (полумесяц), square (каре), circle (круг)
        public bool Open;                     // Г106: ряды разомкнуты — шаг в строю ×Move.OpenK, глубина шеренги ×Move.OpenDepthK

        public Unit Clone() => (Unit)MemberwiseClone();
    }

    public sealed class Commander
    {
        public int Id;
        public string Name = "";
        public int? FactionId;
        public double BuffMorale, BuffDisc, BuffDmg, BuffDef;
    }

    // Изменения отряда: заданы только те поля, что меняются (как объект-патч в JS)
    public sealed class Patch
    {
        public double? Soldiers, TotKilled, TotWounded, Morale, BreakGrace, BreakPenalty, Discipline,
                       AttacksMade, CountersMade, FleeChecks, Fatigue, TurnsActive;
        public bool? Broken, Acted;
        public string Status;

        public bool IsEmpty =>
            Soldiers == null && TotKilled == null && TotWounded == null && Morale == null && BreakGrace == null &&
            BreakPenalty == null && Discipline == null && AttacksMade == null && CountersMade == null &&
            FleeChecks == null && Fatigue == null && TurnsActive == null && Broken == null && Acted == null && Status == null;

        // Object.assign(this, other): заданные поля other перекрывают наши
        public Patch Merge(Patch o)
        {
            if (o == null) return this;
            if (o.Soldiers != null) Soldiers = o.Soldiers;
            if (o.TotKilled != null) TotKilled = o.TotKilled;
            if (o.TotWounded != null) TotWounded = o.TotWounded;
            if (o.Morale != null) Morale = o.Morale;
            if (o.BreakGrace != null) BreakGrace = o.BreakGrace;
            if (o.BreakPenalty != null) BreakPenalty = o.BreakPenalty;
            if (o.Discipline != null) Discipline = o.Discipline;
            if (o.AttacksMade != null) AttacksMade = o.AttacksMade;
            if (o.CountersMade != null) CountersMade = o.CountersMade;
            if (o.FleeChecks != null) FleeChecks = o.FleeChecks;
            if (o.Fatigue != null) Fatigue = o.Fatigue;
            if (o.TurnsActive != null) TurnsActive = o.TurnsActive;
            if (o.Broken != null) Broken = o.Broken;
            if (o.Acted != null) Acted = o.Acted;
            if (o.Status != null) Status = o.Status;
            return this;
        }

        public void ApplyTo(Unit u)
        {
            if (Soldiers != null) u.Soldiers = Soldiers.Value;
            if (TotKilled != null) u.TotKilled = TotKilled.Value;
            if (TotWounded != null) u.TotWounded = TotWounded.Value;
            if (Morale != null) u.Morale = Morale.Value;
            if (BreakGrace != null) u.BreakGrace = BreakGrace.Value;
            if (BreakPenalty != null) u.BreakPenalty = BreakPenalty.Value;
            if (Discipline != null) u.Discipline = Discipline.Value;
            if (AttacksMade != null) u.AttacksMade = AttacksMade.Value;
            if (CountersMade != null) u.CountersMade = CountersMade.Value;
            if (FleeChecks != null) u.FleeChecks = FleeChecks.Value;
            if (Fatigue != null) u.Fatigue = Fatigue.Value;
            if (TurnsActive != null) u.TurnsActive = TurnsActive.Value;
            if (Broken != null) u.Broken = Broken.Value;
            if (Acted != null) u.Acted = Acted.Value;
            if (Status != null) u.Status = Status;
        }
    }

    public sealed class UnitPatch
    {
        public int Id;
        public Patch Patch;
        public UnitPatch(int id, Patch p) { Id = id; Patch = p; }
    }

    // Результат действия: заголовок и строки журнала, тон записи, изменения отрядов
    public sealed class ActionResult
    {
        public bool Ok = true;
        public string Title = "";
        public List<string> Lines = new List<string>();
        public string Tone = "info";
        public Patch Patch = new Patch();                      // для проверок БД и побега
        public List<UnitPatch> Patches = new List<UnitPatch>(); // для боя
    }

    // Всё, что движку нужно извне (ctx в JS): правила, генератор, полководцы, имена фракций
    public sealed class EngineContext
    {
        public Rules Rules = Rules.Base;
        public Func<double> Rng;
        public Func<Unit, Commander> CommanderOf = u => null;
        public Func<int?, string> FactionName = id => "Без фракции";
        // черновик карты (6а): во сколько раз быстрее копится усталость; null — как в v29
        public Func<Unit, FatigueMult> FatigueMult;
    }

    public sealed class FatigueMult
    {
        public double Mult;
        public string Name = "";
    }
}
