// ═══════════ Rules.cs — наборы правил (копия rules.js) ═══════════
// Каждое число формулы — здесь, а не в самой формуле (правило 6 в CLAUDE.md).
// Набор 1 («base») заморожен: он обязан отыграть эталон v29 строка в строку, как и трекер.
// Меняешь число в rules.js — меняешь его здесь, в том же коммите (правило 7).
using System.Collections.Generic;

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

        // ── Карта в метрах (черновик 6а) — пока перенесены строй и высота; остальное — вместе с battlemap.js ──
        public sealed class FormationR
        {
            public double PerMan, Ranks, RankDepth;
            public FormationR(double perMan, double ranks, double rankDepth) { PerMan = perMan; Ranks = ranks; RankDepth = rankDepth; }
        }
        public sealed class HeightR { public double DownhillMelee = 1.2, UphillMelee = 0.9, RangePerLevel = 0.1, ClimbCost = 1.5; }
        // Местность (К24): режим боя под целью ("form" / "rough"); движение ×[пехота, конница], null — непроходимо;
        // укрытие от стрел, % от итогового урона; усталость ×; натиск невозможен; Sight — сколько метров этой
        // местности пропускает взгляд (null — не мешает вовсе)
        public sealed class TerrainR
        {
            public string Mode;
            public double[] Move;
            public double Cover, Fatigue;
            public bool NoCharge;
            public double? Sight;
            public TerrainR(string mode, double[] move, double cover = 0, double fatigue = 0, bool noCharge = false, double? sight = null)
            { Mode = mode; Move = move; Cover = cover; Fatigue = fatigue; NoCharge = noCharge; Sight = sight; }
        }
        public sealed class SpeedR { public double Infantry = 100, Archer = 100, Pike = 80, Cavalry = 250, HorseArcher = 300; }
        public sealed class RangeR { public double Archer = 200, HorseArcher = 150, Other = 150; }
        public sealed class PanicR { public double Radius = 150, MoraleLoss = 100; }
        public sealed class MapR
        {
            public bool Draft = true;
            // строй (К21): метров по фронту на бойца, шеренг, метров на шеренгу вглубь
            public System.Collections.Generic.Dictionary<string, FormationR> Formation = new System.Collections.Generic.Dictionary<string, FormationR>
            {
                ["infantry"] = new FormationR(1, 8, 1),     // 1000 → 125 × 8 м
                ["pike"] = new FormationR(1, 10, 1),        // 1000 → 100 × 10 м
                ["archer"] = new FormationR(1, 5, 1),       // 1000 → 200 × 5 м
                ["cavalry"] = new FormationR(1.5, 5, 3),    // 1000 → 300 × 15 м
            };
            static double[] M(double inf, double cav) => new[] { inf, cav };
            public Dictionary<string, TerrainR> Terrain = new Dictionary<string, TerrainR>
            {
                ["field"] = new TerrainR("form", M(1, 1)),
                ["road"] = new TerrainR("form", M(0.7, 0.7)),
                ["sand"] = new TerrainR("form", M(1.5, 2), fatigue: 2),
                ["snow"] = new TerrainR("form", M(1.5, 1.5), fatigue: 1.5),
                ["shrub"] = new TerrainR("rough", M(1.5, 2), cover: 15),
                ["forest"] = new TerrainR("rough", M(2, 3), cover: 30, noCharge: true, sight: 100),
                ["water"] = new TerrainR("rough", null),
                ["ford"] = new TerrainR("rough", M(2, 2), noCharge: true),
                ["bridge"] = new TerrainR("form", M(1, 1)),
                ["swamp"] = new TerrainR("rough", M(3, 4), noCharge: true),
                ["rocks"] = new TerrainR("rough", null, sight: 0),
                ["wall"] = new TerrainR("rough", null, cover: 50, sight: 0),
                ["gate"] = new TerrainR("rough", null, cover: 50, sight: 0),
                ["tower"] = new TerrainR("rough", null, cover: 50, sight: 0),
                ["palisade"] = new TerrainR("rough", M(3, 4), cover: 30),
                ["moat"] = new TerrainR("rough", M(3, 4)),
                ["trench"] = new TerrainR("rough", M(3, 4), cover: 30),
                ["building"] = new TerrainR("rough", null, cover: 50, sight: 0),
                ["pavement"] = new TerrainR("form", M(1, 1)),
                ["breach"] = new TerrainR("rough", M(3, 4)),
            };
            public HeightR Height = new HeightR();
            public SpeedR Speed = new SpeedR();     // движение за ход, м (К22)
            public RangeR Range = new RangeR();     // дальность стрельбы, м (К23)
            public double MeleeGap = 5;             // «вплотную» — края строя ближе одной клетки (К23)
            public double ChargeRunUp = 50;         // натиску нужен разбег по чистой местности (К29)
            public PanicR Panic = new PanicR();     // каскадная паника (К11, К25)
        }
        public MapR Map = new MapR();

        // ── Баллистика игры (Г33, Г37–Г41) — ЧЕРНОВИК, только для игры; трекер стрелы не считает ──
        // Лук: сила натяжения (Н) × длина натяжения (м) / 2 × КПД = энергия стрелы → скорость.
        // Ошибки — стандартные отклонения угла возвышения, направления (градусы) и силы выстрела (доля).
        // VolleyK — сколько стрел выпускает отряд на единицу урона формулы стола; подобран калибровкой
        // на опорной стычке (Г37: поле, 100 м, пехота в строю) — см. shared/calibration/ranged.md.
        public sealed class BowR
        {
            public string Name;
            public double DrawN, DrawM, ArrowKg, ArrowDiamM, Efficiency, Cd, SigmaElevDeg, SigmaAzDeg, SigmaSpeed, VolleyK;
            public BowR(string name, double drawN, double drawM, double arrowKg, double diamM, double eff, double cd,
                        double sElev, double sAz, double sSpeed, double volleyK)
            {
                Name = name; DrawN = drawN; DrawM = drawM; ArrowKg = arrowKg; ArrowDiamM = diamM; Efficiency = eff; Cd = cd;
                SigmaElevDeg = sElev; SigmaAzDeg = sAz; SigmaSpeed = sSpeed; VolleyK = volleyK;
            }
        }
        public sealed class RangedR
        {
            public double Gravity = 9.81, AirDensity = 1.225, Dt = 0.02;
            public double PartNorm = 0.763;   // нормировка PartLethality: на опорной стычке (лучники → пехота, 100 м) в среднем ×1 (Г39)
            public double LaunchHeight = 1.5, AimHeight = 1.1;         // стреляют с плеча, целятся в грудь (1,1 м)
            public double TargetSideM = 20;                            // стрелок бьёт по врагам перед собой: до 20 м вбок
            // сила выстрела, если на полной дуга не проходит над своими: ослабленный — круче (как у Iron Kings), потом навес
            public double[] SpeedSteps = { 1, 0.9, 0.8, 0.72, 0.66, 0.52, 0.40, 0.30 };
            // тело: пеший — цилиндр 0,5 м × 1,75 м; конный — конь (коробка) и всадник над ним
            public double BodyRadius = 0.25, BodyHeight = 1.75, HeadFrom = 1.45, TorsoFrom = 0.95;
            public double HorseLength = 2.2, HorseWidth = 0.7, HorseHeight = 1.6, RiderTop = 2.5, RiderHeadFrom = 2.2;
            // Г39: часть тела сдвигает долю убитых; нормируется так, чтобы на опорной стычке в среднем было ×1
            public Dictionary<string, double> PartLethality = new Dictionary<string, double>
            {
                ["head"] = 2, ["torso"] = 1, ["legs"] = 0.5, ["horse"] = 0.5,
            };
            public double MetersPerLevel = 5;                          // Г41: по умолчанию; у карты — своё
            public double CanopyHeight = 12, TreeBlockPerM = 0.02;     // лес: кроны 12 м, шанс удара о ветку на метр пути под кроной
            public Dictionary<string, BowR> Bows = new Dictionary<string, BowR>
            {
                // VolleyK подобран на опорной стычке шаблона: лучники / лучники ополчения / арбалетчики → пехота, 100 м, поле
                ["longbow"] = new BowR("Боевой лук", 450, 0.72, 0.070, 0.009, 0.70, 2.0, 1.0, 0.8, 0.03, 2.86),
                ["shortbow"] = new BowR("Простой лук", 280, 0.65, 0.050, 0.008, 0.65, 2.0, 1.6, 1.3, 0.05, 3.91),
                ["crossbow"] = new BowR("Арбалет", 2500, 0.15, 0.080, 0.012, 0.50, 1.2, 0.6, 0.6, 0.02, 2.00),
                ["horsebow"] = new BowR("Конный лук", 350, 0.70, 0.060, 0.008, 0.75, 2.0, 1.4, 1.2, 0.04, 2.86),   // как боевой — своего опорного шаблона пока нет
            };
        }
        public RangedR Ranged = new RangedR();

        // ── Движение фигурками (Г31, Г52–Г54) — ЧЕРНОВИК, только для игры; в трекере фигурок нет ──
        // Норма хода — Map.Speed (как за столом), местность — Map.Terrain. Здесь — как строй её проходит.
        public sealed class MoveR
        {
            public double TurnSec = 15, Dt = 0.05;
            // Г53: разгон и торможение, с — от места до полной скорости и обратно
            public double AccelInfantry = 1, AccelArcher = 1, AccelPike = 1.5, AccelCavalry = 3, AccelHorseArcher = 2;
            // Г52: поворот колесом — фланги идут во столько раз быстрее марша; кругом — каждый на месте
            public double WheelK = 3, WheelMaxDegPerSec = 180;
            public double AboutFaceDeg = 135, AboutFaceSec = 1;
            public double MarchAlignDeg = 20;     // курс разошёлся с путём сильнее — стоп и поворот колесом
            // Г54: цель ближе этой доли нормы — без поворота: боком и назад — на доле скорости
            public double CloseShare = 1.0 / 3, SideSpeed = 0.5, ForwardConeDeg = 45;
            // Фигурка догоняет своё место в строю: быстрее марша, резвее отряда, отставание выбирает за SlotTau с
            public double FigureCatchUp = 1.3, FigureAccelK = 2, SlotTau = 0.5;
            // Шаг 2 — тела (Г56–Г58): фигурка — капсула размером с квадратик; перекрытие меньше BodyTol не считается
            public double BodyTol = 0.05, LookAheadSec = 0.6, YieldMargin = 0.5;
            public double PassThroughSpeed = 0.5;      // Г56: стрелки и свои сквозь друг друга
            public double TieSec = 0.5;                // Г57: «одновременно» — разница прихода меньше этого
            public double RightsForgetSec = 2;         // очередь забывается, если отряды разошлись
            // упёрлась доля HeldShare фигурок — центр строя встаёт; трогается, когда упираются меньше HeldKeepShare
            // (не меньше 2 фигурок) HoldSec подряд — иначе строй дёргается: тронулся, упёрся, тронулся
            public double HeldShare = 0.05, HeldKeepShare = 0.02, HoldSec = 0.5;
            public double ReassignEverySec = 1, ReassignGain = 0.1;   // м: обмен местами, только если в сумме ближе
            // Шаг 3 — узости и обход (Г59–Г61)
            public double SmallObstacleM = 20;      // непроходимое пятно не больше этого — мелкое: фигурки огибают, строй не обходит
            public double ClearancePenalty = 2;     // путь ближе полфронта к крупному препятствию — до ×(1 + это) дороже (только выбор пути)
            public double NarrowMarginM = 1;        // зазор строя до края прохода с каждой стороны
            public double NarrowSlack = 0.1;        // проход уже строя меньше чем на эту долю — не сужается: крайние прижмутся
            public double NarrowAheadM = 30;        // колонна должна сложиться за столько до узости
            public double NarrowCheckSec = 0.5;
            public double RegroupLagM = 3, RegroupShare = 0.2, RegroupSpeed = 0.5;   // Г60: перестраиваются — вдвое медленнее
            public double DetourWaitSec = 3, DetourCooldownSec = 10;                 // Г61: ждёт 3 с, потом обходит своего
        }
        public MoveR Move = new MoveR();

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
