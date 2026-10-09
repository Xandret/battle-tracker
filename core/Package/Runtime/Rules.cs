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
            // Г103 (Алекс 09.10.2026: «чтобы стрелы с неким шансом не перелетали» постройки): постройка — сплошное тело своей высоты
            // над землёй клетки; стрела ниже верха втыкается в неё (End 5). «Шанс» даёт траектория: навесная перелетает, настильная
            // бьёт в стену. Окоп/вал — бруствер ParapetHeightM по краю клетки: стрела, входящая в клетку сбоку ниже него, бьёт в вал,
            // сверху и из соседнего окопа — свободно. Числа — черновик рисунка В19 (вместо черновика Г41) до ГМа; нет в списке — не держит
            public Dictionary<string, double> BuildingHeightM = new Dictionary<string, double>
            {
                ["wall"] = 9, ["tower"] = 11, ["gate"] = 9, ["palisade"] = 4, ["building"] = 6,
            };
            public double ParapetHeightM = 1.5;
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
            public double FrameWaitM = 8, StuckMps = 0.5;
            public bool MoatImpassable = true;   // Г105 (Алекс 10.10.2026: «солдаты спокойно заходят на ров»): в бою ров непроходим, как вода (стол даёт ему цену — вопрос ГМу)
            public bool PalisadeImpassable = true;   // Г105: и частокол в бою — стена (стол: цена 3; перелезть — приступ, И5)
            // Г101 (Алекс 09.10.2026, «стоит добавить возможность выбора построения»): глубина строя — на выбор игрока, не глубже RanksMax
            // шеренг; наборы для кнопок — RanksPresets (доли от глубины стола: цепь ½, линия 1, глубокий строй 2, колонна 4)
            public int RanksMax = 50; public double[] RanksPresets = { 0.5, 1, 2, 4 };   // Б5: доля RegroupShare колонн отстала от мест дальше FrameWaitM и движется медленнее StuckMps (застряла) — рамка отряда стоит и ждёт
            public double DetourWaitSec = 3, DetourCooldownSec = 10;                 // Г61: ждёт 3 с, потом обходит своего
            public double PushSpeedK = 1.5;         // расталкивание двигает фигурку за шаг не дальше её предела скорости × это: в давке у моста не протаскивает рывком
            public double FigTurnDegPerSec = 45;    // Г68: фигурка в охвате разворачивается к врагу не быстрее — капсула не сметает соседей
            // Г70, Г71: бегство — толпой прочь от врага на норме отряда; фигурки бегут каждая сама, разбегаясь веером
            public double FleeSpreadDeg = 12;       // разброс курса фигурок в толпе, ±
            public double FleeSpeedJitter = 0.1;    // фигурка бежит на норме ± половина этого — толпа растягивается, в среднем ровно норма
            public double FleeLookM = 400;          // от кого бежать: враги ближе этого (от центра до центра)
            public double FleeEdgeM = 3;            // фигурка у края карты ближе этого — отряд ушёл с поля боя
            // Г100 (Алекс 09.10.2026: бегущий отряд занимал пятую часть карты): уклонение от врага на пути бегства — мягкое. Было 25 м,
            // ±70°, сворот на 90°: колонны бежали поперёк, толпа 1000 пехоты растягивалась до 400 × 230 м и крутилась у преследующей
            // конницы (выживало 309); стало — толпа 121 × 83 м при строе 125 м, выживает 584
            public double FleeBlockM = 15, FleeBlockDeg = 30;   // враг ближе этого и в пределах ± этого от курса — путь перекрыт: бежит прочь от него
            public double FleeDodgeDeg = 30;   // на сколько сворачивает колонна, обходя врага на пути бегства (Г70)
            // Б1 (Г82, Г85, Г92): бойцы — тела. Фигурка — колонна во всю глубину строя (около 10 человек), у неё якорь —
            // куда её ведут строй, узости, охват и бегство; толкаются, уступают и упираются бойцы поштучно; положение
            // колонны для боя — середина её живых бойцов. С 03.10.2026 (Г92, Г97) — умолчание. Выключено — по-старому:
            // фигурки-капсулы и живые бойцы внутри них (Г56–Г58, Г75–Г78) — набор `Rules.Figures`, до удаления (Г97)
            public bool MenBodies = true;
            public double AnchorLeadM = 1.5;        // якорь уходит от середины своих бойцов не дальше: упёрлись — встаёт и он
            // тело конного — капсула вдоль курса: полудлина — эта доля глубины шеренги. 0,25: при шеренге 3 м и ширине 1,5 м капсула
            // 2,85 × 1,35 м — кони в строю не внахлёст (при 0,4 было 3,75 м: строй всё время расталкивал сам себя)
            public double HorseHalfShare = 0.25;
            public double NarrowWidenSec = 2;       // после перестроения в узости шире — не раньше
            // Б5 (Iron Kings: «сквозь другие отряды — сужают ряды»): проход между стоящими своими — тоже узость, если в него
            // помещается не меньше FriendGapCols колонн; уже — ждут и обходят, как раньше (Г61)
            public int FriendGapCols = 2; public double FriendGapMarginM = 0.5;
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
            // Б2 (Г83): рукопашная по бойцам при MenBodies. Касается врага — зазор между телами не больше ReachM; пикинёр
            // достаёт из PikeRanks шеренг (вперёд, не шире FrontMax от своего курса). Бьёт раз в SwingSec, у каждого свой
            // ритм ±25%. Удар — попадание, если у врага есть неотданные потери окна стола; иначе принят на щит, и ударенный
            // отшатывается со скоростью RecoilMps
            public double ReachM = 1.0, SwingSec = 1.6, RecoilMps = 0.6; public int PikeRanks = 4;
            // Г87 (Б4): укрупнение — людей на поле больше BodyThresholdMen → одно тело (боец) = k человек одного ряда в глубину,
            // k = ⌈всего на поле / порог⌉, одно на всю битву (Г24); фронт и строй в метрах те же, тело — капсула вдоль колонны.
            // Потери стол считает людьми: тело «ранится» и падает после k потерь. Мишень для стрел — коробка длиной в его ряды
            // (та же площадь, что у k одиночных мишеней), тело бьёт за своих людей (ритм ударов × k)
            public int BodyThresholdMen = 40000;
            // Схватка без рывков (08.10.2026, по замеру чата облика: кони в схватке «телепортируются вперёд-назад»): отряд в касании
            // с врагом — его колонны в охвате идут к местам у врага не быстрее WrapFight*Mps (в схватке не скачут); колонна, что
            // билась меньше WrapRetargetSec назад, нового врага не ищет (иначе цель прыгает между бойцами врага и колонна мечется);
            // конь не выпадает всем телом — выпад коня HorseLungeK от пешего
            public double WrapFightHorseMps = 3, WrapFightFootMps = 2, WrapRetargetSec = 1.0, HorseLungeK = 0.3;
            public bool WrapSeekSticky = true, LagMajorityHold = false;   // колонна держится за выбранного бойца врага; якорь не быстрее отставшего большинства (выключено: −3…5 % потерь в контакте; застрявших держит рамка, FrameWaitM)
            public double LagCrawlMps = 0.5;   // …но не медленнее этого (ждать-то надо, а стоять намертво — нет); прыжок якоря назад к бойцам пробовали — хуже
            public double WrapSeekHoldSec = 2.0;   // …но не дольше этого без касания: не достать — ищет другого
            public double WrapFightSlope = 1.0;
            public bool WrapStandoffTouch = true, BalanceStillOnly = true;
            // Б5 (Г84, Iron Kings): густой лес — деревья как неподвижные тела: в каждой клетке леса (5 м) TreesPerCell стволов радиусом
            // TreeRadiusM, по квадрантам клетки со сдвигом до TreeJitterM (хеш клетки — движок и рисунок считают одинаково). Бойцы
            // обходят стволы поштучно — строй рассыпается между деревьями и смыкается за лесом; жёстких (Г86) в лесу нет
            public int TreesPerCell = 3; public double TreeRadiusM = 0.35, TreeJitterM = 0.5;   // 4 и 0,7 — пехота проходит, конница вязнет у опушки
            public double TreeLookSec = 0.2;
            public bool TreesStopHorses = false;   // конь обходит стволы взглядом, но не упирается в них телом: капсула 2,4 м между стволами с шагом 2,5 м вязла намертво (отряд не входил в лес за 8 ходов)   // на ствол смотрят лишь на шаг вперёд: взгляд на 10 м (конь на галопе) видел бы стволы во всех направлениях разом и вставал   // место охвата — касание, не давление; смыкание не трогает колонны в манёвре (переключатели на время подбора)    // планка скорости у места охвата растёт на столько м/с за метр до места (далеко — идёт как шла)
            // Г94 (Алекс): у бойца свой курс тела — поворачивается не быстрее TurnDegPerSec; вбок и назад относительно курса — не
            // быстрее Side/Back (конь назад почти не ходит — сперва развернётся). Дальше FaceMoveM от места или быстрее FaceMoveMps —
            // лицом по ходу; у места — по колонне; в схватке — на противника; бегущий — по ходу
            public double FootTurnDegPerSec = 360, HorseTurnDegPerSec = 120;
            public double FootSideMps = 1.5, FootBackMps = 1.0, HorseSideMps = 0.5, HorseBackMps = 0.3;
            public double FaceMoveM = 1.5, FaceMoveMps = 1.5;
            // колонна стоит, боец ближе StandShuffleM к месту — держит курс строя и подтягивается на место шагом (конь вбок как пеший,
            // назад до StandBackMps): колонна пришла сжатой (ряды — вразнобой), разворачиваться к месту в сжатой колонне — столкновения
            public double StandShuffleM = 5, StandBackMps = 1.0;
            // колонна встала (не в схватке), а кто-то дальше SettleFarM от места (пришли вразнобой): места колонны раздаются заново —
            // каждому ближайшее свободное, если общий путь до мест короче не меньше чем на SettleGainM; никто не бежит через строй
            public double SettleFarM = 0.75, SettleGainM = 0.3;
            // Б5 (Iron Kings: «места не закреплены — боец занимает свободное, если строю так удобнее»): стоящие соседние колонны
            // меняют бойцов местами парами, если обоим в сумме ближе не меньше чем на SwapGainM; колонны в манёвре не трогаем
            public double SwapGainM = 1.0;
            // Г104: осиротевшие при перераскладке бойцы — сначала в ближайшую колонну с недобором; колонна с недобором берёт бойца
            // из ближайшей с избытком по всему строю, не только у соседки (иначе линия в 3 шеренги растёт хвостом с края) — не в схватке
            public bool OrphansToLacking = true, BalanceAcross = true;
            // Г86 (Б4): отряд вдали от врага (дальше FarEnemyM от его строя) и от других своих (дальше FarFriendM), не под стрелами
            // (UnderFireSec после последней стрелы по отряду), не бегущий — его бойцы, что у своих мест (ближе RigidSnapM), не в
            // схватке и не в охвате, идут одним телом с якорем: замораживаются там, где стоят (сдвиг от якоря), ни на кого не
            // смотрят и не толкаются между собой; для обычных бойцов рядом они — неподвижные тела. Иначе — поштучно
            public double FarEnemyM = 60, FarFriendM = 20, RigidSnapM = 1.0, UnderFireSec = 2;
            public double PushMaxMps = 6;
            // Г90 (Б3): натиск телами. Конь с разбега (натиск готов —
            // Mover.ChargeReady) пешему врагу не уступает: тот сбит с ног на DownSecMin…DownSecMax с и отброшен, конь теряет ChargeLoss
            // своего хода на каждом сбитом; медленнее ChargeMinMps — натиск его кончился, дальше стена (Г89). Убивает только стол (К29).
            // Пики во фронт: острия на PikeTipM впереди первой шеренги — конь встаёт у острия, пики достают из PikeRanks шеренг
            public double ChargeMinMps = 3, ChargeLoss = 0.3, DownSecMin = 1, DownSecMax = 2, PikeTipM = 2.5;
            public int ChargeKnocks = 2;   // конь за натиск сбивает не больше стольких — вламывается на 1–2 шеренги
            // Старт волной (Iron Kings): колонна тронулась с места — передний ряд первым, каждый следующий через WaveRowSec
            public double WaveRowSec = 0.1, WaveAfterStopSec = 1;   // волна — только после стоянки не короче WaveAfterStopSec (не на каждом рывке)
            // Г84: бегство рассыпается за FleeScatterMin…Max с (свой срок по хешу): задний ряд бежит сразу, передний — последним;
            // до своего срока боец стоит, где стоял, потом места толпы расходятся за FleeScatterRampSec
            public double FleeScatterMin = 2, FleeScatterMax = 4, FleeScatterRampSec = 1.5;   // Г94: толкотня двигает бойца не быстрее этого — без рывков
        }
        public MenR Men = new MenR();
        public GarrisonR Garrison = new GarrisonR();
        // Г104 (Алекс 09.10.2026: «занимаемся гарнизоном… возможностью разместить на стенах отряды»): стены и башни проходимы для пехоты
        // стороны-хозяина (Battle.FortOwner), врагу — нет (до приступа); ворота — свои проходят всегда, враг — только открытые.
        // Боец на стене стоит на её верху (Man.Z): толкотня, касания и удары — только между теми, кто на одной высоте (разница не больше
        // StepM), стрела с земли достаёт того, кто над парапетом. Числа — черновик до ГМа
        public sealed class GarrisonR
        {
            public double WalkMult = 0.7;   // шаг по боевому ходу и в воротах — как по пересечённой
            public double StepM = 1.5;      // больше этого перепада — не достать ни рукой, ни плечом
            public double EdgeM = 0.5;      // передняя шеренга гарнизона — на столько от наружного края стены
            public double SeekM = 60;       // «поставить на стену»: стена ищется не дальше этого от точки
            public double MerlonShare = 0.5; // зубцы: стрела, прошедшая сбоку над бруствером стены, с этим шансом бьёт в зубец (укрытие стола К16 — 50 %)
            public bool AutoShoot = true;    // стрелки гарнизона без приказа «атаковать» сами бьют по ближайшему врагу в дальности (стена отстреливается)
            public double GateHitPerSwing = 0.03;   // Г105: боец врага у закрытых ворот рубит их — столько прочности за удар на человека (деревянные 40, окованные 80: Siege.Hp)
            public double RowPickM = 7.5;    // «на стену»: среди клеток стены не дальше ближайшей + столько берётся та, через которую ряд длиннее (край башни 3 × 3 — не ряд; на дальнюю стену не уводит)
        }

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
        // старая модель тел — фигурки-капсулы (Г56–Г58, Г75–Г78): только для сравнений и тестов старого режима, пока его код не убран (Г97)
        public static readonly Rules Figures = MakeFigures();
        static Rules MakeFigures() { var r = new Rules(); r.Move.MenBodies = false; return r; }
        public static Rules Get(string id) => Base;   // наборы 2 и 3 — после согласования с ГМом
    }
}
