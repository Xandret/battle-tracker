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
            // Г75: живой строй ловит больше стрел, чем ровная сетка калибровки (ряды не стоят затылок в затылок — сквозных
            // коридоров нет): в бою стрел столько × это. Подгонка под стол на опорной стычке (Г33, Г37) — черновик ГМу
            public double LiveRanksK = 0.88;   // 100 боёв: без поправки +18% к столу, с ней +4,5%
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
            public double EnemyLookAheadSec = 0.1;     // врагу уступают в последний миг: врезаются с разгона и упираются (Г58, Г29)
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
            public double PushSpeedK = 1.5;         // расталкивание двигает фигурку за шаг не дальше её предела скорости × это: в давке у моста не протаскивает рывком
            public double FigTurnDegPerSec = 45;    // Г68: фигурка в охвате разворачивается к врагу не быстрее — капсула не сметает соседей
            // Г70, Г71: бегство — толпой прочь от врага на норме отряда; фигурки бегут каждая сама, разбегаясь веером
            public double FleeSpreadDeg = 12;       // разброс курса фигурок в толпе, ±
            public double FleeSpeedJitter = 0.1;    // фигурка бежит на норме ± половина этого — толпа растягивается, в среднем ровно норма
            public double FleeLookM = 400;          // от кого бежать: враги ближе этого (от центра до центра)
            public double FleeEdgeM = 3;            // фигурка у края карты ближе этого — отряд ушёл с поля боя
            public double FleeBlockM = 25, FleeBlockDeg = 70;   // враг ближе этого и в пределах ± этого от курса — путь перекрыт: бежит прочь от него
            // Б1 (Г82, Г85, Г92): бойцы — тела. Фигурка — колонна во всю глубину строя (около 10 человек), у неё якорь —
            // куда её ведут строй, узости, охват и бегство; толкаются, уступают и упираются бойцы поштучно; положение
            // колонны для боя — середина её живых бойцов. Выключено — по-старому: фигурки-капсулы (Г56–Г58)
            public bool MenBodies = false;
            public double AnchorLeadM = 1.5;        // якорь уходит от середины своих бойцов не дальше: упёрлись — встаёт и он
            public double HorseHalfShare = 0.4;     // тело конного — капсула вдоль курса: полудлина — эта доля глубины шеренги
            public double NarrowWidenSec = 2;       // после перестроения в узости шире — не раньше
            public double MenYieldM = 0.25;
            public double FriendYieldShare = 0.8;   // свои разных отрядов перекрылись — уступающий отходит на эту долю, идущий первым — на остаток         // взгляд вперёд бойца: ближе этого к чужому через LookAheadSec — уступает
        }
        public MoveR Move = new MoveR();

        // Г75–Г78: живые бойцы внутри фигурок — ЧЕРНОВИК ДО ГМа (на правила боя не влияют, только на вид и на то, куда летят стрелы)
        public sealed class MenR
        {
            public double Tau = 0.5;          // Г76: боец догоняет своё место за ~Tau с — строй «дышит» на поворотах и в давке
            public double SpeedK = 1.4, WalkMin = 2;   // предел скорости бойца — скорость фигурки (не меньше WalkMin) × SpeedK
            public double AccelK = 1.5;       // разгон и тормоз бойца — резвее его фигурки во столько раз (иначе всадник пролетает мимо остановившейся фигурки)
            public double MaxLagM = 2.5;      // дальше этого от своего места боец не отстаёт и не вылетает
            public double Jitter = 0.15;      // личное смещение места — доля шага в строю
            public double BodyShare = 0.45;   // Г77: радиус тела — доля шага в строю; ближе — расходятся (с любым бойцом)
            public double PushShare = 0.5;    // перекрытие убирается за шаг на эту долю
            public double LungeM = 0.4, LungeAmpM = 0.35, LungeSec = 1.4;   // Г78: передние в схватке — шаг к врагу и выпады
            public double FleeSpread = 1.6, FleeWanderM = 0.5;               // бегущая толпа: места шире, бойцы виляют
            // В14: новое место дальше ReseatM — боец идёт к нему шагом: не быстрее ReseatMps сверх хода своей фигурки и без
            // подтягивания MaxLagM (иначе дальний перескакивал бы)
            public double ReseatM = 1.2, ReseatMps = 1.4;
            public double ReseatRunM = 6, ReseatRunMps = 4;   // отстал от места дальше ReseatRunM — догоняет трусцой; бегущая толпа бежит как бежала
            // В14: смыкание между фигурками — раз в BalanceSec каждая фигурка, где излишек (бойцов сверх раскладки стола) меньше,
            // чем у соседки, на BalanceDiff и больше, берёт у неё одного ближнего бойца; одна потеря строй не дёргает
            public double BalanceSec = 1.0; public int BalanceDiff = 2;
        }
        public MenR Men = new MenR();

        // Г72: приказ «сплотить» — в игре (за столом решает мастер кнопкой «воспрял духом»). ЧЕРНОВИК ДО ГМа
        public sealed class RallyR { public double FreeM = 150, Morale = 40; }   // врага нет ближе FreeM (от края до края) — бросок d100 ≤ дисциплина; успех — БД не ниже Morale
        public RallyR Rally = new RallyR();

        // ── Штурм (этап 6б, Г46–Г51) — ЧЕРНОВИК ДО ГМа; как siege в rules.js ──
        public sealed class SiegeR
        {
            public bool Draft = true;
            public double SectionM = 25;   // стена режется на участки не длиннее (Г46, Ш1)
            public double BreachM = 10;    // пролом за каждое обнуление прочности участка (Г46, Ш2)
            // прочность участка (Г46): частокол, каменная стена, ворота деревянные / окованные, башня
            public Dictionary<string, double> Hp = new Dictionary<string, double>
            {
                ["palisade"] = 30, ["wall"] = 100, ["gateWood"] = 40, ["gateIron"] = 80, ["tower"] = 150,
            };

            // Орудия (Ш3, Г47, Г49) — как siege.engines в rules.js; смысл полей — там же.
            public double SkillK = 0.2, HitMin = 5, HitMax = 95;
            public Dictionary<string, EngineR> Engines = new Dictionary<string, EngineR>
            {
                ["ballista"] = new EngineR("Баллиста", 3, 1, 1, 60, 0, 0, 350, 80, 45, 60, 25, 3, 30, 20),
                ["catapult"] = new EngineR("Катапульта (онагр)", 6, 2, 1, 40, 0, 50, 300, 65, 30, 45, 15, 10, 40, 30) { Indirect = true },
                ["trebuchet"] = new EngineR("Требушет", 12, 4, 2, 0, 0, 100, 350, 60, 30, 35, 10, 25, 80, 50) { Indirect = true },
                ["bombard"] = new EngineR("Бомбарда", 10, 2, 3, 15, 1, 30, 400, 75, 40, 35, 10, 40, 60, 60) { Burst = 3, Shock = 15 },
                ["cannon"] = new EngineR("Пушка", 6, 2, 2, 50, 0, 0, 500, 70, 40, 55, 20, 15, 80, 40) { Burst = 2, Shock = 10 },
                ["mortar"] = new EngineR("Мортира", 6, 2, 2, 20, 1, 50, 300, 55, 25, 40, 15, 10, 60, 50) { Burst = 2, Shock = 15, Indirect = true },
                ["ribauldequin"] = new EngineR("Рибодекин", 4, 1, 3, 80, 0, 0, 150, 70, 40, 75, 35, 1, 150, 25) { Burst = 3, Shock = 10 },
                ["magic"] = new EngineR("Маг-пушка", 3, 1, 2, 60, 1, 0, 600, 85, 45, 85, 45, 50, 0, 40) { Magic = true },
                ["ram"] = new EngineR("Таран", 12, 4, 1, 40, 0, 0, 10, 0, 0, 0, 0, 25, 0, 60) { Ram = true },   // от центра фишки: таран ~10 м длиной
                ["tower"] = new EngineR("Осадная башня", 20, 8, 0, 25, 0, 0, 0, 0, 0, 0, 0, 0, 0, 80) { Tower = true, Capacity = 40 },
            };
            public MagicR Magic = new MagicR();
            public double RamWallK = 0.25;   // таран по камню (стене, башне); ворота и частокол — полным уроном
            public AssaultR Assault = new AssaultR();
        }
        // Приступ на стену (Г48, Г50, Ш10–Ш14) — как siege.assault в rules.js
        public sealed class AssaultR
        {
            public double LaddersPer = 50, PerLadder = 10, ReachM = 10, PushPct = 15, FallMen = 5,
                          TowerReachM = 10, MaxUnits = 2, BreachPerUnitM = 10, DefenderReachM = 5;
        }
        public sealed class EngineR
        {
            public string Name;
            public double Crew, MinCrew, Reload, Move, Deploy, Wall, Die, Hp, Burst, Shock, Capacity;
            public double[] Range, HitWall, HitTroops;
            public bool Indirect, Magic, Ram, Tower;
            public EngineR(string name, double crew, double minCrew, double reload, double move, double deploy, double r0, double r1,
                           double wallNear, double wallFar, double troopsNear, double troopsFar, double wall, double die, double hp)
            {
                Name = name; Crew = crew; MinCrew = minCrew; Reload = reload; Move = move; Deploy = deploy;
                Range = new[] { r0, r1 }; HitWall = new[] { wallNear, wallFar }; HitTroops = new[] { troopsNear, troopsFar };
                Wall = wall; Die = die; Hp = hp;
            }
        }
        // Маг-батарея (SPEC 6б): как siege.magic в rules.js
        public sealed class MagicR
        {
            public double SkillK = 3, ArmorCap = 0.15, ArmorDiv = 1000, SplashM = 30, ExplodeM = 40;
            public double[] KillPct = { 70, 100 }, SplashPct = { 5, 15 }, ExplodePct = { 20, 50 };
        }
        public SiegeR Siege = new SiegeR();

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
