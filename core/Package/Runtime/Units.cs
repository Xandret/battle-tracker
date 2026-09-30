// ═══════════ Units.cs — справочники и свойства отрядов (копия units.js) ═══════════
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public static class Modes
    {
        public const string MeleeForm = "melee_form", RangedForm = "ranged_form",
                            MeleeRough = "melee_rough", RangedRough = "ranged_rough";

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            [MeleeForm] = "Ближний бой · в строю",
            [RangedForm] = "Дальний бой · в строю",
            [MeleeRough] = "Ближний бой · пересечённая местность",
            [RangedRough] = "Дальний бой · пересечённая местность",
        };

        public static bool IsMelee(string m) => m == MeleeForm || m == MeleeRough;
    }

    public sealed class MoraleStageInfo
    {
        public string Label, Note, Color;
        public double Mult;
        public MoraleStageInfo(string label, string note, string color, double mult) { Label = label; Note = note; Color = color; Mult = mult; }
    }

    public static class Units
    {
        public static double AttackLimit(Unit u, Rules r) => u.Discipline >= r.Actions.EliteDisc ? 2 : 1;
        public static double CounterLimit(Unit u, Rules r) => u.Discipline >= r.Actions.EliteDisc ? 2 : 1;

        public static MoraleStageInfo MoraleStage(double bd)
        {
            if (bd < 20) return new MoraleStageInfo("Дрогнули", "бросок на побег с помехой", "#B0402E", 1);
            if (bd < 40) return new MoraleStageInfo("Колеблются", "проверка БД с помехой", "#C97B2E", 1);
            if (bd < 50) return new MoraleStageInfo("Безучастны", "нет эффектов", "#98A08F", 1);
            if (bd < 80) return new MoraleStageInfo("Рьяны", "могут атаковать сильнейшего", "#7FA05A", 1);
            if (bd < 120) return new MoraleStageInfo("Воодушевлены", "урон +20%", "#C9A227", 1.2);
            return new MoraleStageInfo("Геройство", "урон +50%", "#E0C34A", 1.5);
        }

        public static string DiscStage(double d)
        {
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

        public static double GraceByDisc(double d, Rules r)
        {
            foreach (var g in r.Breakdown.Grace) if (d >= g[0]) return g[1];
            return 0;
        }

        public static bool IsCav(Unit u) => u.Type == "cavalry";
        public static bool IsPike(Unit u) => u.Type == "pike";
        public static bool IsArcherType(Unit u) => u.Type == "archer";
        public static bool CanBeTargeted(Unit u) => u.Status == "active" || u.Status == "fled";

        // Сектор удара относительно фасинга цели: front / flank / rear.
        // Если хотя бы один из отрядов не на карте — удар считается фронтальным.
        public static string AttackSector(Unit att, Unit def, Rules r)
        {
            if (!att.OnMap || !def.OnMap) return "front";
            double dx = att.MapX - def.MapX, dy = att.MapY - def.MapY;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return "front";
            double ang = Math.Atan2(dx, -dy) * 180 / Math.PI;   // 0° — вверх, по часовой
            double rel = ang - def.Facing;
            while (rel > 180) rel -= 360;
            while (rel < -180) rel += 360;
            double a = Math.Abs(rel);
            if (a <= r.Sectors.FrontMax) return "front";
            if (a >= r.Sectors.RearMin) return "rear";
            return "flank";
        }

        public static readonly Dictionary<string, string> SectorRu = new Dictionary<string, string>
        {
            ["front"] = "во фронт", ["flank"] = "во фланг", ["rear"] = "в тыл",
        };
    }
}
