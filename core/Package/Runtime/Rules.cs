// ═══════════ Rules.cs — наборы правил (копия rules.js) ═══════════
// Каждое число формулы — здесь, а не в самой формуле (правило 6 в CLAUDE.md).
// Набор 1 («base») заморожен: он обязан отыграть эталон v29 строка в строку, как и трекер.
// Меняешь число в rules.js — меняешь его здесь, в том же коммите (правило 7).
namespace BattleCore
{
    public sealed class Rules
    {
        public string Id = "base";
        public string Name = "Набор 1 — наши формулы";

        public sealed class ActionsR { public double EliteDisc = 80; }
        public sealed class ModeDivR { public double MeleeForm = 50, RangedForm = 50, MeleeRough = 25, RangedRough = 25; }
        public sealed class CavalryR { public double ChargeMult = 1.5, NoChargeMult = 0.7; }
        public sealed class DefenseR
        {
            public double MeleeEqDiv = 20, MeleeDiscDiv = 10, RangedEqDiv = 10, MinDivisor = 1, RearEqMult = 0.5;
        }
        public sealed class LethalityR { public double Base = 10, Die = 60, ExpDiv = 20; }
        public sealed class MoraleLossR { public double PerCasualties = 15, Cap = 50, CrushingFrac = 0.5, CrushingLoss = 150; }
        public sealed class MoraleR
        {
            public double Max = 150, CheckAt = 40, CheckMult = 2, WaverFrom = 20, WaverTo = 40, ShakenBelow = 20;
        }
        public sealed class BreakdownR
        {
            public double DelayTurns = 3, DiscPenalty = 20, PassiveDiscLoss = 10;
            // [дисциплина от, ходов отсрочки]
            public double[][] Grace = { new double[] { 80, 3 }, new double[] { 70, 2 }, new double[] { 60, 1 } };
        }
        public sealed class FleeR { public double StandFastDisc = 60; }
        public sealed class FatigueR { public double Step = 10, Max = 100, Threshold = 4, EliteThreshold = 8, EliteDisc = 90; }
        public sealed class SectorsR { public double FrontMax = 45, RearMin = 135; }

        public ActionsR Actions = new ActionsR();
        public double RollFloorPerDisc = 5;
        public ModeDivR ModeDiv = new ModeDivR();
        public double ArcherMeleeMult = 0.5;
        public CavalryR Cavalry = new CavalryR();
        public double PikeCounterMult = 3;
        public DefenseR Defense = new DefenseR();
        public LethalityR Lethality = new LethalityR();
        public MoraleLossR MoraleLoss = new MoraleLossR();
        public MoraleR Morale = new MoraleR();
        public BreakdownR Breakdown = new BreakdownR();
        public FleeR Flee = new FleeR();
        public FatigueR Fatigue = new FatigueR();
        public SectorsR Sectors = new SectorsR();

        public double ModeDivFor(string mode)
        {
            switch (mode)
            {
                case Modes.MeleeForm: return ModeDiv.MeleeForm;
                case Modes.RangedForm: return ModeDiv.RangedForm;
                case Modes.MeleeRough: return ModeDiv.MeleeRough;
                default: return ModeDiv.RangedRough;
            }
        }

        public static readonly Rules Base = new Rules();
        public static Rules Get(string id) => Base;   // наборы 2 и 3 — после согласования с ГМом
    }
}
