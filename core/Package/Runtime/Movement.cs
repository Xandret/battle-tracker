// ═══════════ Movement.cs — строй идёт фигурками (Г31, шаг 1; Г52–Г54) — ЧЕРНОВИК ДО ГМа ═══════════
// Центр строя идёт по пути (FlowField.Route → Track). Скорость — в метрах НОРМЫ в секунду: в лесу (×2)
// та же скорость нормы — вдвое меньше метров по земле. Поэтому за ход отряд тратит ровно норму, как за
// столом, а на экране в лесу идёт медленнее.
//   Разгон (Г53): наибольшая скорость = норма ÷ (15 с − время разгона): с места по полю отряд разгоняется,
//   идёт и тормозит у цели ровно за ход. Цель дальше нормы — не тормозит: скорость подбирается так, чтобы
//   к концу хода потратить ровно норму, и переходит в следующий ход.
//   Поворот (Г52): колесом вокруг центра, фланги — в WheelK раз быстрее марша; больше 135° — кругом:
//   каждый поворачивается на месте, задняя шеренга становится передней, фигурки никуда не идут.
//   На изломе пути строй тормозит заранее, встаёт, доворачивает колесом и идёт дальше.
//   Марш (Г54): цель ближе трети нормы — без поворота, боком и назад на половине скорости.
// Фигурки догоняют свои места в строю: скорость места + поправка на отставание, с пределом скорости
// и ускорения; в непроходимое не входят — скользят вдоль. Места перераспределяются (Reassign).
// Шаг 2: фигурки — твёрдые тела (Bodies.cs); упёрся строй — центр стоит, время пробки — в журнал.
// Шаг 3: путь держится от крупных препятствий на полфронта; в узости строй складывается в колонну (SetCols, Narrow),
// пока перестраивается — вдвое медленнее; свой стоит на пути — через 3 с обход (DetourCheck).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    // Приказ (Г15): двигаться в точку (X, Y, Facing — центр строя, м, и куда смотреть), атаковать отряд TargetId
    // (идти на него, преследуя; Charge — с натиском, Г29) или держать позицию (стоять, отвечать — Г44)
    public enum OrderKind { Move, Attack, Hold, Rally, Retreat }   // Rally — «сплотить» бегущих (Г72); Retreat — пятится (Г81)
    public sealed class MoveOrder
    {
        public double X, Y, Facing;
        public OrderKind Kind = OrderKind.Move;
        public int TargetId; public bool Charge;
    }

    // Фигурка в мире: где и как быстро, м и м/с. Dvx, Dvy, Vmax — куда хочет на этом шаге (после взгляда вперёд);
    // BlockedBy — номер чужого отряда, которому пришлось уступить на этом шаге (0 — никому)
    public sealed class FigState
    {
        public int Id;   // постоянный номер тела в отряде: место в строю (индекс) меняется — обмены, потери, — а тело то же
        public double Face;   // Г106: куда смотрит колонна относительно курса отряда (каре и круг — наружу)
        // касается врага (Г27) и в какую сторону он (единичный вектор) — для выпадов передних бойцов (Г78)
        public bool Fighting; public double FightX, FightY, FoeX, FoeY; public int FoeId;   // FoeX, FoeY — где касающаяся фигурка врага; FoeId — её отряд
        public double FightT = double.NegativeInfinity;   // когда колонна последний раз касалась врага (часы боя)
        public double WSeekT = double.NegativeInfinity;   // когда колонна в охвате в последний раз сама выбрала бойца врага целью (WrapToFoes)
        public Man WSeekMan;                               // тот боец врага: пока жив и близко, цель колонны — он, место у рамки её не перебивает
        public double Hc, Hs, Hd;   // курс фигурки на деле (Soldiers.FigHeading): косинус, синус, градусы — на шаг бойцов
        public long SeatKey = -1;   // В14: состав и размер, при которых бойцы рассажены, — не изменились, пересаживать незачем
        public double X, Y, Vx, Vy;
        public double Dvx, Dvy, Vmax;
        public int BlockedBy; public bool BlockedByEnemy, Slowed;
        // Охват (Г68): колонна огибает врага — фигурка идёт к (WX, WY) и смотрит по WH (градусы), а не по строю
        public bool Wrap; public double WX, WY, WH;
        // место у врага WFoe (в его системе), которое колонна заняла, — держится за ним, пока оно свободно
        public Mover WFoe; public double WSlotX, WSlotY, WNx, WNy; public int WBehind;
        // тело развёрнуто не по строю (Turned): ось капсулы Axis поворачивается к нужной постепенно (FigTurnDegPerSec)
        public bool Turned; public double Axis;
        // отпущена из охвата и идёт на своё место — свои её пропускают (на половине скорости, как Г56)
        public bool Returning;
        // бегство (Г70): свой курс, если общий путь толпы перекрыт врагом (окружённые разбегаются в открытую сторону)
        public double FleeH = double.NaN;
        // Б1 (MenBodies): якорь колонны — где она хочет быть (её ведут Desire и FleeDesire); X, Y — середина её живых бойцов
        public double AX, AY, AVx, AVy;
        public int MenN;   // живых бойцов в колонне на прошлом шаге (Б1)
        // Б1: где был бы якорь по бойцам (каждый: где стоит минус сдвиг его места); пересаживающийся (В14) не упёрся, а идёт
        // шагом — он за нынешнее место якоря. RefN — сколько бойцов
        public double RefX, RefY; public int RefN; public double RefAllX, RefAllY; public int LagN;   // RefAll — по всем бойцам, LagN — отставших дальше LagRefM
        public bool Moving; public double StartT, StopT = double.NegativeInfinity;   // якорь идёт; когда тронулся и когда встал (новая колонна стояла «всегда») — для старта волной
        public double GoalM;      // Б3: сколько якорю до места (в строю или в охвате) — далёкая колонна перестраивается сквозь своих
    }

    public sealed class Mover
    {
        public Placed P;                 // центр строя, курс (Facing), фигурки (их X, Y — места в строю)
        public List<FigState> Figs = new List<FigState>();
        public MoveOrder Order;
        public FlowField Field;
        public BattleMap.PassRules Pass;        // Г104: чем этому отряду можно пройти сверх местности (стены хозяина, открытые ворота); null — как всем
        public double LastActT = double.NaN;     // Г107: когда последний раз стрелял или касался врага (часы боя) — виден всем RevealSec
        public double DisorderUntil = double.NegativeInfinity;   // Г111 п.2: в беспорядке до этого шага (Steps; свои проходят сквозь строй) — для рисунка и удара
        public bool Disordered => Steps < DisorderUntil;
        public double AmbushFrom = double.NaN;   // Г107: приказ «атаковать» отдан, пока отряд был невидим цели (часы боя) — удар в Fog.AmbushSec — засада
        public Man CommanderMan;                 // Г108: тело полководца — ближайший к знамени (центр, на 1/5 глубины к фронту); пал — ближайший из стражи
        public List<Man> Guard = new List<Man>();   // Г108: стража — ближайшие к полководцу (в поединке держат кольцо)
        public bool Garrisoned;                  // Г104: стоит гарнизоном на стене — не разворачивается на цель (линия в 100 м слетела бы со стены), стреляет по всему впереди
        public Track Track;
        public bool Side;                // Г54: ближний ход — без поворота
        public double Vs;                // скорость по пути, м нормы в секунду; переходит в следующий ход
        public double Along;             // сколько нормы пройдено по пути с начала приказа
        public bool OnSpot, Done;        // дошёл до точки / ещё и встал к заданному направлению
        public double AboutLeft;         // сколько ещё длится разворот кругом, с
        public string Note;              // «пути нет», «цель непроходима» — в журнал
        public double Spent, Moved, WheelSec, AboutSec;   // за этот ход: нормы, метров по земле, секунд на повороты
        // Шаг 2: строй упёрся — центр стоит, пока фигурки не пройдут (Held, не меньше HoldSec подряд).
        // Blockers — кому за этот ход уступал и сколько секунд; Rights — очередь с чужими отрядами (Г57).
        public bool Held; public double HoldLeft, HeldSec;
        public Dictionary<int, (double sec, bool enemy, string name)> Blockers = new Dictionary<int, (double, bool, string)>();
        public Dictionary<int, (bool mine, int seen)> Rights = new Dictionary<int, (bool, int)>();
        public int LastBlocker; public bool LastBlockerEnemy; public string LastBlockerName;   // в кого упёрся последним
        public int Steps;                // шагов этого отряда с начала — часы для Rights
        // Шаг 3: узости (Г59, Г60) и обход своих (Г61)
        public List<(double X, double Y, int Rank, int File)> Nominal;   // места в линии — куда вернуться после узости
        public Footprint NominalFp;
        public int NominalCols, Cols, MinCols;   // колонн фигурок: в линии, сейчас, самое узкое за ход
        public bool Reforming; public double RegroupSec;
        public int FarLagN, LagColsN;            // для зондов: сколько колонн застряло / отстало на последнем шаге (Б5)
        public double NarrowSince;               // Б1: с последнего перестроения в узости, с
        public double TargetX, TargetY;          // куда идёт центр строя (внутри карты)
        public FlowField RouteField;             // карта для пути в обход своего (Г61); null — путь по Field
        public List<string> Detoured = new List<string>();
        public int HeldSameId; public double HeldSameSec, DetourCooldown;
        public int IgnoreHoldBy;                 // кого обходим — упор в него не держит центр строя
        // Бой в движении (БД1): сколько бойцов разложено на фигурки сейчас; где упали выбывшие фигурки (Г30)
        public int LaidMen = -1;
        public int NextFigId;                    // номер для следующего нового тела (FigState.Id)
        public List<Man> Men = new List<Man>();  // живые бойцы (Г75); MenVersion — меняется при каждой раскладке
        public int BodyK = 1;                    // Г87: людей в одном бойце-теле (ставит бой перед первым ходом)
        public bool InMelee;                     // отряд касается врага (ставит бой на шаге касаний) — колонны в охвате идут шагом
        public int NextManId, MenVersion;
        // Бегство (БД4, Г70–Г72): толпа без строя — каждая фигурка бежит сама к FleeX, FleeY (край карты прочь от врага)
        public bool Fleeing, Gone, RallyPending, Rallied;
        public int LeftMen;                      // ушли за край карты (живы, но в этой битве их нет); LaidMen — только те, кто на поле
        public double FleeHeading, FleeX, FleeY, FleeSince;
        public int ShotDown;                     // выбыло от стрел с последней раскладки — их место известно точно (Г67)
        public int StruckDown;                   // Б2: пало от ударов бойцов с последней раскладки — тоже уже не стоят (Г83)
        public bool ChargeReady;                 // Г90: натиск готов (ставит бой) — кони с разбега пешему врагу не уступают
        public double UnderFireUntil = double.NegativeInfinity;   // Г86: под стрелами до этого времени (ставит стрельба)
        public double Now;                       // часы боя на этом шаге (ставит бой) — для рисунка: когда сбит с ног
        public List<(double x, double y)> Fallen = new List<(double x, double y)>();

        public static Mover Place(Unit u, double x, double y, double facing, Rules r, double menPerFigure = 10)
        {
            var m = new Mover { P = new Placed { U = u } };
            m.Replace(x, y, facing, r, menPerFigure);
            return m;
        }
        // поставить заново (до первого хода, Г104): строй, колонны и бойцы — с нуля на новом месте
        public void Replace(double x, double y, double facing, Rules r, double menPerFigure = 10)
        {
            P.X = x; P.Y = y; P.Facing = MoveSim.Norm(facing);
            P.Relayout(r, menPerFigure, force: true);
            Figs.Clear();
            foreach (var f in P.Figs) { P.ToWorld(f.X, f.Y, out var wx, out var wy); Figs.Add(new FigState { Id = NextFigId++, X = wx, Y = wy, AX = wx, AY = wy, Face = f.Face }); }
            Nominal = P.Figs.Select(f => (f.X, f.Y, f.Rank, f.File)).ToList();
            NominalFp = new Footprint { Front = P.Fp.Front, Depth = P.Fp.Depth };
            NominalCols = Cols = MinCols = P.Figs.Count == 0 ? 0 : P.Figs.Max(f => f.File) + 1;
            LaidMen = (int)Math.Max(0, Js.Round(P.U.Soldiers));
            Soldiers.Assign(this, r, spawn: true);
        }
    }

    public static class MoveSim
    {
        public static double Norm(double deg) { deg %= 360; return deg < 0 ? deg + 360 : deg; }
        // от a до b по кратчайшей, градусы, −180…180
        public static double AngleDiff(double a, double b) { double d = Norm(b - a); return d > 180 ? d - 360 : d; }
        // курс отрезка: 0° — вверх (−y), по часовой
        public static double HeadingOf(double dx, double dy) => Norm(Math.Atan2(dx, -dy) * 180 / Math.PI);

        public static double AccelSec(Unit u, Rules r)
        {
            var M = r.Move;
            if (u.Type == "cavalry") return u.Weapon == "ranged" ? M.AccelHorseArcher : M.AccelCavalry;
            return u.Type == "archer" ? M.AccelArcher : u.Type == "pike" ? M.AccelPike : M.AccelInfantry;
        }
        // Наибольшая скорость, м нормы в секунду (Г53): с места, с разгоном и торможением — ровно норма за ход
        public static double TopSpeed(Unit u, Rules r) => BattleMap.UnitSpeed(u, r) / (r.Move.TurnSec - AccelSec(u, r));
        // Поворот колесом (Г52), °/с: фланг (полфронта от центра) идёт в WheelK раз быстрее марша по полю
        public static double WheelRate(Mover m, Rules r)
        {
            double march = BattleMap.UnitSpeed(m.P.U, r) / r.Move.TurnSec;
            return Math.Min(r.Move.WheelMaxDegPerSec, r.Move.WheelK * march / Math.Max(0.5, m.P.Fp.Front / 2) * 180 / Math.PI);
        }

        // Приказ: путь по карте направлений, режим марша (Г54). Цель — внутри карты; непроходимая — встаём рядом.
        public static void Give(Mover m, MoveOrder o, Geo geo, Rules r)
        {
            m.Order = o; m.OnSpot = m.Done = false; m.Along = 0; m.Note = null; m.Track = null; m.Field = null; m.RouteField = null;
            double tx = o.X, ty = o.Y;
            if (geo != null) { tx = Math.Max(0, Math.Min(geo.W - 1e-6, tx)); ty = Math.Max(0, Math.Min(geo.H - 1e-6, ty)); }
            m.TargetX = tx; m.TargetY = ty;
            m.IgnoreHoldBy = 0;
            // путь — для строя во всю ширину линии (Г59): от крупных препятствий — на полфронта, если есть место
            m.Field = FlowField.Build(geo, r, BattleMap.IsHorse(m.P.U), tx, ty, m.NominalFp.Front / 2, null, m.Pass);
            List<(double x, double y)> route;
            if (m.Field == null) route = new List<(double x, double y)> { (m.P.X, m.P.Y), (tx, ty) };
            else
            {
                if (m.Field.Target < 0) { m.Note = "на карте негде встать"; return; }
                route = m.Field.Route(m.P.X, m.P.Y, tx, ty);
                if (route == null)
                {
                    // Г105: цели не достичь (за закрытыми воротами, за стеной, за рекой) — идёт к ближайшей к ней достижимой клетке и
                    // встаёт там: у закрытых ворот враг их рубит (GateStrikes), откроют или выбьют — приказ даётся заново
                    var own = FlowField.Build(geo, r, BattleMap.IsHorse(m.P.U), m.P.X, m.P.Y, 0, null, m.Pass);
                    int bestC = -1; double bd = double.MaxValue;
                    for (int i = 0; i < own.W * own.H; i++)
                    {
                        if (!own.Passable(i) || double.IsInfinity(own.CostAt(i))) continue;
                        var (cx, cy) = own.CenterOf(i); double d = (cx - tx) * (cx - tx) + (cy - ty) * (cy - ty);
                        if (d < bd) { bd = d; bestC = i; }
                    }
                    if (bestC >= 0)
                    {
                        (tx, ty) = own.CenterOf(bestC); m.TargetX = tx; m.TargetY = ty;
                        m.Field = FlowField.Build(geo, r, BattleMap.IsHorse(m.P.U), tx, ty, m.NominalFp.Front / 2, null, m.Pass);
                        route = m.Field.Route(m.P.X, m.P.Y, tx, ty);
                    }
                    if (route == null) { m.Note = "пути нет"; return; }
                    m.Note = "цели не достичь — встаёт ближе";
                }
                else if (m.Field.CellOf(tx, ty) != m.Field.Target) m.Note = "цель непроходима — встаёт рядом";
            }
            m.Track = Track.Build(m.Field, route);
            if (m.Track == null) { m.Note = "пути нет"; return; }
            if (m.Track.Pieces.Count == 0) m.OnSpot = true;   // уже на месте — остаётся довернуться
            // атакующий идёт на врага лицом, а не боком (Г54 — только для приказа «двигаться»)
            m.Side = o.Kind == OrderKind.Move && m.Track.Cost <= r.Move.CloseShare * BattleMap.UnitSpeed(m.P.U, r)
                  || o.Kind == OrderKind.Retreat;   // Г81: отступает — пятится, не поворачиваясь, на половине нормы
            // разгон переходит в новый приказ, только если он ведёт туда же, куда отряд уже идёт
            if (m.Side || m.Track.Pieces.Count == 0 || Math.Abs(AngleDiff(m.P.Facing, LegHeading(m.Track, 0))) > r.Move.MarchAlignDeg) m.Vs = 0;
        }

        // Встать на месте и развернуться к o.Facing (колесом или кругом, Г52) — без поиска пути: стрелку,
        // который уже достаёт до цели, путь не нужен, а карта направлений — дорогая
        public static void TurnInPlace(Mover m, MoveOrder o)
        {
            m.Order = o; m.Note = null; m.Vs = 0; m.Along = 0;
            m.Track = Track.Build(null, new List<(double x, double y)> { (m.P.X, m.P.Y) });
            m.OnSpot = true; m.Done = false;
        }

        static double LegHeading(Track t, int leg) =>
            HeadingOf(t.Points[leg + 1].x - t.Points[leg].x, t.Points[leg + 1].y - t.Points[leg].y);
        // куда отряд хочет идти — единичный вектор текущего отрезка его пути; false — пути нет или уже на месте
        internal static bool WantDir(Mover m, out double ux, out double uy)
        {
            ux = uy = 0;
            if (m.Track == null || m.OnSpot || m.Track.Points.Count < 2) return false;
            var at = m.Track.At(m.Along);
            int leg = at.piece >= 0 ? m.Track.Pieces[at.piece].Leg : 0;
            if (leg + 1 >= m.Track.Points.Count) leg = m.Track.Points.Count - 2;
            double dx = m.Track.Points[leg + 1].x - m.Track.Points[leg].x, dy = m.Track.Points[leg + 1].y - m.Track.Points[leg].y, l = JsMath.Hypot(dx, dy);
            if (l < 1e-9) return false;
            ux = dx / l; uy = dy / l; return true;
        }
        // сколько нормы до конца отрезка ломаной leg
        static double LegEndCost(Track t, int leg)
        {
            for (int i = 0; i < t.Pieces.Count; i++) if (t.Pieces[i].Leg > leg) return t.Pieces[i].C0;
            return t.Cost;
        }

        // Один ход (TurnSec с шагом Dt) для всех отрядов разом. frame(t) — после каждого шага (запись хода).
        // Возвращает строки журнала: сколько прошёл, сколько нормы, сколько ушло на повороты.
        public static List<string> Turn(IList<Mover> ms, Geo geo, Rules r, Action<double> frame = null)
        {
            BeginTurn(ms);
            for (int k = 0; k < StepsPerTurn(r); k++)
            {
                Step(ms, geo, r, k);
                frame?.Invoke((k + 1) * r.Move.Dt);
            }
            return EndTurn(ms, r);
        }

        // Ход по частям — чтобы бой (Battle) вклинивался между шагами: начать, шаг за шагом, журнал
        public static int StepsPerTurn(Rules r) => (int)Math.Round(r.Move.TurnSec / r.Move.Dt);
        public static void BeginTurn(IList<Mover> ms)
        {
            foreach (var m in ms)
            {
                m.Spent = m.Moved = m.WheelSec = m.AboutSec = m.HeldSec = m.RegroupSec = 0; m.Blockers.Clear();
                m.MinCols = m.Cols; m.Detoured.Clear();
            }
        }
        // Шаг k хода: 1) центры строёв; 2) куда хочет каждая фигурка; 3) тела: взгляд вперёд, шаг, расталкивание
        public static void Step(IList<Mover> ms, Geo geo, Rules r, int k)
        {
            var M = r.Move;
            double t = k * M.Dt;
            int every = Math.Max(1, (int)Math.Round(M.ReassignEverySec / M.Dt)), narrowEvery = Math.Max(1, (int)Math.Round(M.NarrowCheckSec / M.Dt));
            var prev = new List<(double x, double y)[]>();
            foreach (var m in ms) m.NarrowSince += M.Dt;
            if (k % narrowEvery == 0) foreach (var m in ms) if (m.Track != null) Narrow(m, ms, r);   // узости впереди (Г59); Б5 — и проходы между своими
            foreach (var m in ms)
            {
                prev.Add(Slots(m.P));
                if (m.Order != null && m.Track != null && !m.Done && !m.Fleeing) Lead(m, t, M.Dt, r);
                m.Steps++;
            }
            for (int i = 0; i < ms.Count; i++) Desire(ms[i], prev[i], M.Dt, r);
            if (M.MenBodies) MenBodies.Step(ms, M.Dt, r, geo);   // Б1: тела — бойцы, фигурка — колонна за якорем; Б5 — деревья леса по карте
            else Bodies.Step(ms, M.Dt, r);
            foreach (var m in ms) if (m.Fleeing) FollowCrowd(m, M.Dt);
            if (!M.MenBodies) Soldiers.Step(ms, M.Dt, r);   // бойцы внутри фигурок (Г75)
            foreach (var m in ms) if (!m.Fleeing) DetourCheck(m, ms, geo, M.Dt, r);   // свой перегородил путь — обход (Г61)
            if ((k + 1) % every == 0) foreach (var m in ms) if (!m.Fleeing) Reassign(m, r);
        }
        // Журнал хода: сколько прошёл, сколько нормы, повороты, узости, обходы, пробки
        public static List<string> EndTurn(IList<Mover> ms, Rules r)
        {
            var L = new List<string>();
            foreach (var m in ms)
            {
                if (m.Order == null) continue;
                string name = $"«{m.P.U.Name}»";
                if (m.Track == null) { if (!string.IsNullOrEmpty(m.Note)) L.Add($"{name}: {m.Note}"); continue; }
                double norm = BattleMap.UnitSpeed(m.P.U, r);
                var parts = new List<string> { $"прошёл {Js.Num(Js.R1(m.Moved))} м по земле, нормы {Js.Num(Js.R1(m.Spent))} из {Js.Num(norm)}" };
                if (m.WheelSec > 0) parts.Add($"поворот колесом {Js.Num(Js.R1(m.WheelSec))} с");
                if (m.AboutSec > 0) parts.Add($"кругом {Js.Num(Js.R1(m.AboutSec))} с");
                if (m.MinCols < m.NominalCols) parts.Add($"в узости — колонна по {m.MinCols} фигурки");
                if (m.RegroupSec > 0) parts.Add($"перестроение {Js.Num(Js.R1(m.RegroupSec))} с");
                foreach (var d in m.Detoured) parts.Add($"обошёл «{d}»");
                // пробка (Г31): кому уступал и сколько стоял; упёрся во врага — так и пишем
                foreach (var b in m.Blockers)
                    parts.Add(b.Value.enemy ? $"упёрся во врага «{b.Value.name}» ({Js.Num(Js.R1(b.Value.sec))} с)"
                                            : $"пропускал «{b.Value.name}» {Js.Num(Js.R1(b.Value.sec))} с");
                if (m.Done) parts.Add("на месте");
                else if (m.OnSpot) parts.Add("на месте, доворачивается");
                else
                {
                    parts.Add($"до цели {Js.Num(Js.R1(m.Track.Length - m.Track.MetersAt(m.Along)))} м");
                    if (m.Spent < norm - 0.5) parts.Add($"недобрал {Js.Num(Js.R1(norm - m.Spent))} м нормы");
                }
                if (m.Note != null) parts.Add(m.Note);
                L.Add($"{name}: {string.Join("; ", parts)}");
            }
            return L;
        }

        // места в строю в мире — до шага центра; из них скорость мест для фигурок
        static (double x, double y)[] Slots(Placed P)
        {
            var s = new (double x, double y)[P.Figs.Count];
            for (int k = 0; k < s.Length; k++) { P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var wx, out var wy); s[k] = (wx, wy); }
            return s;
        }

        // Центр строя: курс, скорость по пути, шаг по пути
        static void Lead(Mover m, double t, double dt, Rules r)
        {
            var M = r.Move; var P = m.P; var u = P.U;
            // строй упёрся (шаг 2): центр стоит и не поворачивается, пока фигурки не пройдут; время — в журнал
            if (m.Held) { m.Vs = 0; m.HeldSec += dt; return; }
            double norm = BattleMap.UnitSpeed(u, r), ta = AccelSec(u, r);
            double top = norm / (M.TurnSec - ta), acc = top / ta;
            double budget = norm - m.Spent, tau = M.TurnSec - t;   // осталось нормы и времени в этом ходу
            double left = m.Track.Cost - m.Along;                  // осталось нормы до цели

            int leg = -1;
            double want;
            if (!m.OnSpot)
            {
                var at = m.Track.At(m.Along);
                leg = at.piece >= 0 ? m.Track.Pieces[at.piece].Leg : 0;
                want = m.Side ? P.Facing : LegHeading(m.Track, leg);
            }
            else want = m.Order.Facing;
            double diff = AngleDiff(P.Facing, want);

            // разворот кругом (Г52): стоя, каждый на месте — места в строю отражаются, фигурки не идут
            if (m.AboutLeft <= 1e-9 && Math.Abs(diff) > M.AboutFaceDeg && m.Vs <= 1e-9)
            {
                AboutFace(m);
                m.AboutLeft = M.AboutFaceSec;
            }
            if (m.AboutLeft > 1e-9) { m.AboutLeft -= dt; m.AboutSec += dt; return; }

            // поворот колесом: к нужному курсу, не быстрее WheelRate; сильно мимо курса — сначала встать
            bool wheel = Math.Abs(diff) > (m.OnSpot ? 1e-6 : M.MarchAlignDeg);
            if (Math.Abs(diff) > 1e-9) P.Facing = Norm(P.Facing + Math.Sign(diff) * Math.Min(Math.Abs(diff), WheelRate(m, r) * dt));
            if (wheel) m.WheelSec += dt;

            double target = 0;
            if (!m.OnSpot && !wheel)
            {
                double cap = top;
                if (m.Side && Math.Abs(AngleDiff(P.Facing, LegHeading(m.Track, leg))) > M.ForwardConeDeg) cap *= M.SideSpeed;
                target = left <= budget + 1e-9
                    ? Math.Min(cap, Math.Sqrt(2 * acc * left))           // дойдёт в этом ходу — тормозит к цели
                    : Math.Min(cap, budget / Math.Max(tau, dt));         // не дойдёт — тратит ровно норму к концу хода
                // излом пути впереди круче MarchAlignDeg — тормозит к нему, чтобы там довернуть
                if (!m.Side && leg + 1 < m.Track.Points.Count - 1
                    && Math.Abs(AngleDiff(LegHeading(m.Track, leg), LegHeading(m.Track, leg + 1))) > M.MarchAlignDeg)
                    target = Math.Min(target, Math.Sqrt(2 * acc * Math.Max(0, LegEndCost(m.Track, leg) - m.Along)));
            }
            // перестроение в колонну и обратно (Г60): пока многие фигурки далеко от новых мест — вдвое медленнее
            int lag = 0, farLag = 0;
            if (m.Reforming || M.MenBodies)   // Б5: у бойцов-тел отставание колонн смотрим всегда — рамка не уходит от застрявших одна
            {
                for (int k = 0; k < P.Figs.Count; k++)
                {
                    P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var sx, out var sy);
                    double fx = M.MenBodies ? m.Figs[k].AX : m.Figs[k].X, fy = M.MenBodies ? m.Figs[k].AY : m.Figs[k].Y;   // Б1: колонна — по якорю
                    // Б1: место в воде (хвост сложенной колонны ещё за рекой) — колонна стоит у ближайшего сухого, как и идёт (Desire)
                    if (M.MenBodies && m.Field != null && !Free(m.Field, sx, sy)) { int c = m.Field.NearestPassableCached(m.Field.CellOf(sx, sy)); if (c >= 0) (sx, sy) = m.Field.CenterOf(c); }
                    double lagD = JsMath.Hypot(sx - fx, sy - fy);
                    if (lagD > M.RegroupLagM) lag++;
                    // застряли ли сами бойцы: середина живых бойцов колонны далеко от места и почти не движется (догоняющие после
                    // поворота колесом идут быстро — их не ждём, Г60 лишь замедляет)
                    if (JsMath.Hypot(sx - m.Figs[k].X, sy - m.Figs[k].Y) > M.FrameWaitM && JsMath.Hypot(m.Figs[k].Vx, m.Figs[k].Vy) < M.StuckMps) farLag++;
                }
            }
            // Б5: колонны отстали далеко (бойцы застряли — лес, давка) — рамка стоит и ждёт, а не уходит к цели одна
            m.FarLagN = farLag; m.LagColsN = lag;
            if (!wheel && farLag > M.RegroupShare * P.Figs.Count) { target = 0; m.RegroupSec += dt; }   // при повороте колесом места сами уходят от якорей — это не застрявшие
            else if (m.Reforming)
            {
                if (lag > M.RegroupShare * P.Figs.Count) { target *= M.RegroupSpeed; m.RegroupSec += dt; }
                else m.Reforming = false;
            }
            m.Vs = Math.Max(0, m.Vs + Math.Max(-acc * dt, Math.Min(acc * dt, target - m.Vs)));
            if (m.OnSpot || m.Vs <= 0) { DoneCheck(m); return; }

            // шаг по пути; на изломе — не дальше излома, если там поворот
            double dc = Math.Min(m.Vs * dt, Math.Min(budget, left));
            if (!m.Side && leg + 1 < m.Track.Points.Count - 1
                && Math.Abs(AngleDiff(LegHeading(m.Track, leg), LegHeading(m.Track, leg + 1))) > M.MarchAlignDeg)
            {
                double corner = LegEndCost(m.Track, leg);
                if (m.Along + dc >= corner)
                {
                    // встаёт ровно в излом (присваиванием: сумма с плавающей точкой могла бы не дотянуть
                    // волосок, и отряд навсегда остался бы на старом отрезке со скоростью 0)
                    double s1 = m.Track.MetersAt(m.Along);
                    m.Spent += corner - m.Along; m.Along = corner; m.Vs = 0;
                    var c = m.Track.At(m.Along);
                    P.X = c.x; P.Y = c.y;
                    m.Moved += m.Track.MetersAt(m.Along) - s1;
                    DoneCheck(m);
                    return;
                }
            }
            double s0 = m.Track.MetersAt(m.Along);
            m.Along += dc; m.Spent += dc;
            var p = m.Track.At(m.Along);
            P.X = p.x; P.Y = p.y;
            m.Moved += m.Track.MetersAt(m.Along) - s0;
            if (m.Track.Cost - m.Along <= 1e-6) { m.Along = m.Track.Cost; m.OnSpot = true; m.Vs = 0; }
            else if (budget - dc <= 1e-9 && tau - dt > 1e-9) m.Vs = 0;   // норма кончилась раньше конца хода — стоит
            DoneCheck(m);
        }

        static void DoneCheck(Mover m)
        {
            if (m.OnSpot && Math.Abs(AngleDiff(m.P.Facing, m.Order.Facing)) <= 1e-6) m.Done = true;
        }

        // Кругом: курс +180°, места в строю отражаются — каждая фигурка остаётся там же, где стояла,
        // но задняя шеренга теперь передняя (и неполная шеренга — впереди, пока строй не перестроится)
        static void AboutFace(Mover m)
        {
            var P = m.P;
            int maxRank = 0, maxFile = 0;
            foreach (var f in P.Figs) { maxRank = Math.Max(maxRank, f.Rank); maxFile = Math.Max(maxFile, f.File); }
            foreach (var f in P.Figs) { f.X = -f.X; f.Y = -f.Y; f.Rank = maxRank - f.Rank; f.File = maxFile - f.File; }
            P.Facing = Norm(P.Facing + 180);
            // и места в линии — тоже: после узости строй развернётся уже лицом в новую сторону
            int nr = m.Nominal.Max(q => q.Rank), nf = m.Nominal.Max(q => q.File);
            m.Nominal = m.Nominal.Select(q => (-q.X, -q.Y, nr - q.Rank, nf - q.File)).ToList();
        }

        // Фигурки догоняют свои места: желаемая скорость = скорость места + отставание / SlotTau,
        // не быстрее FigureCatchUp × марш (местность под фигуркой замедляет), ускорение — FigureAccelK × отряда.
        // Место в воде или в стене — фигурка встаёт у ближайшего проходимого. Путь к месту закрыт (строй обходит
        // озеро, а фигурка на другом берегу) — идёт по карте направлений отряда, пока не увидит своё место.
        // Здесь — только «куда хочет» (Dvx, Dvy, Vmax); шаг и тела — Bodies.Step.
        // Бегущая толпа (Г70): центр отряда — середина фигурок, курс — прочь от врага; пройденное — в норму хода
        static void FollowCrowd(Mover m, double dt)
        {
            if (m.Figs.Count == 0) return;
            var main = m.Figs.Where(s => double.IsNaN(s.FleeH)).ToList();   // центр — по основной толпе, отбившиеся не в счёт
            if (main.Count == 0) main = m.Figs;
            double cx = main.Average(s => s.X), cy = main.Average(s => s.Y);
            double v = m.Figs.Average(s => JsMath.Hypot(s.Vx, s.Vy));   // путь — по скорости фигурок: центр прыгает, когда уходят за край
            m.Moved += v * dt; m.Spent += v * dt; m.Vs = v;
            m.P.X = cx; m.P.Y = cy; m.P.Facing = m.FleeHeading;
        }
        // Бегущая фигурка (Г70, Г71): сама по себе, по карте направлений к краю карты, на норме отряда — каждая
        // чуть со своим курсом и скоростью (толпа расходится веером и растягивается); строя и мест нет
        static void FleeDesire(Mover m, Rules r)
        {
            var M = r.Move; var u = m.P.U; var F = m.Field;
            double top = BattleMap.UnitSpeed(u, r) / M.TurnSec;   // ровно норма за ход (Г71); толпа — вокруг неё
            bool mb = r.Move.MenBodies;
            foreach (var s in m.Figs)
            {
                double h1 = Hash01(u.Id, s.Id, 1), h2 = Hash01(u.Id, s.Id, 2);
                double px = mb ? s.AX : s.X, py = mb ? s.AY : s.Y;   // Б1: колонну ведёт якорь
                double rho = F != null && F.Inside(px, py) ? F.Mult(F.CellOf(px, py)) ?? 1 : 1;
                double vmax = top * (1 + M.FleeSpeedJitter * (0.5 - h1)) / rho;
                double own = double.IsNaN(s.FleeH) ? m.FleeHeading : s.FleeH;
                double hx = Math.Sin(own * Math.PI / 180), hy = -Math.Cos(own * Math.PI / 180);
                if (double.IsNaN(s.FleeH) && F != null && F.Inside(px, py) && Free(F, px, py))
                {
                    int next = F.Next(F.CellOf(px, py));
                    if (next >= 0) { var c = F.CenterOf(next); double ex = c.x - px, ey = c.y - py, el = JsMath.Hypot(ex, ey); if (el > 1e-9) { hx = ex / el; hy = ey / el; } }
                }
                double a = (h2 * 2 - 1) * M.FleeSpreadDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
                s.Dvx = (hx * ca - hy * sa) * vmax; s.Dvy = (hx * sa + hy * ca) * vmax; s.Vmax = vmax; s.GoalM = 0;
            }
        }
        // Детерминированная «случайность» для вида толпы: не трогает генератор боя (исход не зависит от рисунка)
        public static double Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        static void Desire(Mover m, (double x, double y)[] prev, double dt, Rules r)
        {
            if (m.Fleeing) { FleeDesire(m, r); return; }
            var M = r.Move; var P = m.P; var u = P.U;
            double vmax0 = M.FigureCatchUp * TopSpeed(u, r);
            var F = m.Field; bool mb = M.MenBodies;
            for (int k = 0; k < P.Figs.Count; k++)
            {
                var s = m.Figs[k];
                double px = mb ? s.AX : s.X, py = mb ? s.AY : s.Y;   // Б1: колонну ведёт якорь, бойцы идут за ним
                P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var sx, out var sy);
                double gx = s.Wrap ? s.WX : sx, gy = s.Wrap ? s.WY : sy;
                double rho = F != null && F.Inside(px, py) ? F.Mult(F.CellOf(px, py)) ?? 1 : 1;
                double vmax = vmax0 / rho;
                double dvx, dvy;
                (double x, double y)? via = null;
                if (F != null)
                {
                    if (!Free(F, sx, sy)) { int c = F.NearestPassableCached(F.CellOf(sx, sy)); if (c >= 0) (gx, gy) = F.CenterOf(c); }
                    if (JsMath.Hypot(gx - px, gy - py) > F.CellW && Free(F, px, py) && double.IsInfinity(F.SegmentCost(px, py, gx, gy)))
                    {
                        int next = F.Next(F.CellOf(px, py));
                        if (next >= 0) via = F.CenterOf(next);
                    }
                }
                if (via.HasValue)
                {
                    double ex = via.Value.x - px, ey = via.Value.y - py, el = Math.Max(1e-9, JsMath.Hypot(ex, ey));
                    dvx = ex / el * vmax; dvy = ey / el * vmax;
                }
                else if (s.Wrap)
                {
                    // охват: к точке у врага, без скорости места в строю; отряд уже в схватке — шагом, не вскачь (иначе колонна,
                    // потерявшая касание, летит к новой цели галопом и мечется между бойцами врага)
                    dvx = (gx - px) / M.SlotTau; dvy = (gy - py) / M.SlotTau;
                    // планка — у цели: далеко от места идёт как шла, за метр до места — не быстрее планки плюс метр в секунду на метр пути
                    if (m.InMelee) vmax = Math.Min(vmax, (BattleMap.IsHorse(u) ? r.Men.WrapFightHorseMps : r.Men.WrapFightFootMps) + JsMath.Hypot(gx - px, gy - py) * r.Men.WrapFightSlope);
                }
                else
                {
                    dvx = (sx - prev[k].x) / dt + (gx - px) / M.SlotTau;
                    dvy = (sy - prev[k].y) / dt + (gy - py) / M.SlotTau;
                }
                double dv = JsMath.Hypot(dvx, dvy);
                if (dv > vmax) { dvx *= vmax / dv; dvy *= vmax / dv; }
                s.Dvx = dvx; s.Dvy = dvy; s.Vmax = vmax; s.GoalM = JsMath.Hypot(gx - px, gy - py);
            }
        }
        public static bool Free(FlowField f, double x, double y) => f.Inside(x, y) && f.Passable(f.CellOf(x, y));

        // ── узости (Г59, Г60) ──
        // Перестроиться в cols колонн фигурок: обратно в линию (места из Nominal) или в колонну — сетка с шагом
        // фигурки, неполный задний ряд — по центру. Места раздаются спереди назад ближайшим фигуркам: кто где
        // стоит, тот туда и встаёт. Пока фигурки далеко от новых мест — перестроение, вдвое медленнее (Г60).
        public static void SetCols(Mover m, int cols)
        {
            var P = m.P; int n = P.Figs.Count;
            cols = Math.Max(1, Math.Min(cols, m.NominalCols));
            if (n == 0 || cols == m.Cols) return;
            List<(double X, double Y, int Rank, int File)> slots;
            if (cols == m.NominalCols)
            {
                slots = m.Nominal;
                P.Fp = new Footprint { Front = m.NominalFp.Front, Depth = m.NominalFp.Depth };
            }
            else
            {
                double fw = P.Figs.Max(f => f.Width), fd = P.Figs.Max(f => f.Depth);
                int rows = (n + cols - 1) / cols, last = n - cols * (rows - 1);
                slots = new List<(double X, double Y, int Rank, int File)>();
                for (int rr = 0; rr < rows; rr++)
                {
                    int inRow = rr < rows - 1 ? cols : last; double shift = (cols - inRow) / 2.0;
                    for (int c = 0; c < inRow; c++) slots.Add((-cols * fw / 2 + (c + shift + 0.5) * fw, -rows * fd / 2 + (rr + 0.5) * fd, rr, c));
                }
                P.Fp = new Footprint { Front = cols * fw, Depth = rows * fd };
            }
            var free = Enumerable.Range(0, n).ToList();
            var assign = new (double X, double Y, int Rank, int File)[n];
            foreach (int si in Enumerable.Range(0, slots.Count).OrderBy(i => slots[i].Y).ThenBy(i => slots[i].X))
            {
                var sl = slots[si];
                P.ToWorld(sl.X, sl.Y, out var wx, out var wy);
                int best = 0; double bd = double.MaxValue;
                for (int q = 0; q < free.Count; q++)
                {
                    var s = m.Figs[free[q]];
                    double d = (s.X - wx) * (s.X - wx) + (s.Y - wy) * (s.Y - wy);
                    if (d < bd) { bd = d; best = q; }
                }
                assign[free[best]] = sl; free.RemoveAt(best);
            }
            for (int k = 0; k < n; k++) { var f = P.Figs[k]; (f.X, f.Y, f.Rank, f.File) = assign[k]; }
            m.Cols = cols; m.MinCols = Math.Min(m.MinCols, cols); m.Reforming = true;
        }

        // Взгляд вперёд (Г59): где по пути проход уже строя. Колонна должна сложиться, пока её голова не дошла до
        // узости: узость в q метрах впереди — в счёт, если q не больше полуглубины колонны + NarrowAheadM. Позади
        // центра — пока хвост строя в узости, строй не разворачивается. Шире — только если по всему окну хватает
        // места: между двумя близкими узостями колонна так и идёт, не разворачиваясь.
        // стоящие свои рядом — для узости между ними (Б5): не бегут, не в схватке, не мы
        [ThreadStatic] static List<Mover> friendsNear;   // по потокам (Г111 п.7)
        static void Narrow(Mover m, IList<Mover> ms, Rules r)
        {
            var F = m.Field; var T = m.Track; var M = r.Move; var P = m.P;
            if (F == null || T == null || T.Pieces.Count == 0 || P.Figs.Count == 0 || Formation.IsRing(m.P.U)) return;   // каре и круг рядов не сужают (Г106)
            int n = P.Figs.Count;
            double fw = P.Figs.Max(f => f.Width), fd = P.Figs.Max(f => f.Depth);
            friendsNear ??= new List<Mover>(); friendsNear.Clear();
            if (M.MenBodies)
            {
                double near = m.NominalFp.Front + M.NarrowAheadM + 60;
                foreach (var q in ms)
                {
                    if (q == m || q.Fleeing || q.P.Figs.Count == 0 || !MenBodies.SameSidePublic(m.P.U, q.P.U)) continue;
                    if (!(q.Order == null || q.Done || q.Vs < 0.1)) continue;   // идущий — не стена: с ним разберётся очередь (Г57)
                    if (JsMath.Hypot(q.P.X - m.P.X, q.P.Y - m.P.Y) > near + JsMath.Hypot(q.P.Fp.Front, q.P.Fp.Depth) / 2) continue;
                    friendsNear.Add(q);
                }
            }
            // свободно вбок от точки (x, y) поперёк (dx, dy) до стоящего своего — как Corridor по карте, шагом полметра
            double FriendProbe(double x, double y, double px, double py, double max)
            {
                for (double s = 0.5; s <= max; s += 0.5)
                {
                    double qx = x + px * s, qy = y + py * s;
                    foreach (var q in friendsNear)
                    {
                        q.P.ToLocal(qx, qy, out var lx, out var ly);
                        if (Math.Abs(lx) <= q.P.Fp.Front / 2 + M.FriendGapMarginM && Math.Abs(ly) <= q.P.Fp.Depth / 2 + M.FriendGapMarginM) return s - 0.5;
                    }
                }
                return max;
            }
            int ColsFor(double width) => Math.Max(1, Math.Min(m.NominalCols, (int)Math.Floor((width * (1 + M.NarrowSlack) - 2 * M.NarrowMarginM) / fw)));
            double LeadFor(int cols) => Math.Ceiling(n / (double)cols) * fd / 2 + M.NarrowAheadM;
            double s = T.MetersAt(m.Along), half = m.NominalFp.Front / 2 + M.NarrowMarginM;
            double from = Math.Max(0, s - P.Fp.Depth / 2), to = Math.Min(T.Length, s + LeadFor(1));
            int want = m.NominalCols;
            // Б1: колонны узкие (1 м) и чувствуют каждые полметра прохода — точки проверки привязаны к меткам пути, а не к
            // хвосту строя (иначе соседние проверки то попадают на узость, то проскакивают её)
            if (M.MenBodies) from = Math.Floor(from / 2.5) * 2.5;
            for (double q = from; q <= to + 1e-9; q += 2.5)
            {
                var (x, y, dx, dy) = T.AtMeters(Math.Min(q, T.Length));
                var (l, rr) = F.Corridor(x, y, dx, dy, half);
                if (friendsNear.Count > 0)
                {
                    // проход между стоящими своими: сужаемся, только если в него войдёт не меньше FriendGapCols колонн — иначе
                    // это стена, а не проход: ждём и обходим (Г61)
                    double fl = FriendProbe(x, y, -dy, dx, l), fr = FriendProbe(x, y, dy, -dx, rr);
                    if (fl + fr < l + rr && ColsFor(fl + fr) >= M.FriendGapCols) { l = fl; rr = fr; }
                }
                int c = ColsFor(l + rr);
                if (c < want && (q <= s || q - s <= LeadFor(c))) want = c;
            }
            // Б1: шире — не раньше NarrowWidenSec после перестроения и только если шире хотя бы на 2 колонны (или обратно в
            // линию): иначе строй дёргается между 6 и 7 колоннами на каждом бугорке берега
            if (M.MenBodies && want > m.Cols && want < m.NominalCols && (want - m.Cols < 2 || m.NarrowSince < M.NarrowWidenSec)) want = m.Cols;
            if (M.MenBodies && want > m.Cols && m.NarrowSince < M.NarrowWidenSec) want = m.Cols;
            if (want != m.Cols) { SetCols(m, want); m.NarrowSince = 0; }
        }

        // ── обход своих (Г61) ──
        // Свой перегородил путь (стоит или идёт, а мы уступаем по очереди Г57): ждём DetourWaitSec — вдруг пройдёт;
        // не прошёл — путь в обход: клетки под его строем — как непроходимые и крупные, от них — на полфронта,
        // если есть место. Упор в того, кого обходим, больше не держит центр строя: фигурки протискиваются вбок.
        static void DetourCheck(Mover m, IList<Mover> ms, Geo geo, double dt, Rules r)
        {
            var M = r.Move;
            m.DetourCooldown = Math.Max(0, m.DetourCooldown - dt);
            // стоящий в пробке строй то и дело «отпускает» на долю секунды — ожидание от этого не сбрасывается,
            // а убывает так же, как копится; сбрасывается, только если упёрлись в другого
            if (!(m.Held && m.LastBlocker != 0 && !m.LastBlockerEnemy)) { m.HeldSameSec = Math.Max(0, m.HeldSameSec - dt); return; }
            if (m.HeldSameId != m.LastBlocker) { m.HeldSameId = m.LastBlocker; m.HeldSameSec = 0; }
            m.HeldSameSec += dt;
            if (m.HeldSameSec < M.DetourWaitSec || m.DetourCooldown > 0 || m.Field == null || m.Order == null) return;
            var b = ms.FirstOrDefault(x => x.P.U.Id == m.LastBlocker);
            if (b == null) return;
            if (M.MenBodies && M.FriendSoft && !b.Fleeing && b.Vs < 0.1 && (b.Order == null || b.Done || b.Order.Kind == OrderKind.Hold)) { m.HeldSameSec = 0; return; }   // Г111 п.2: сквозь стоящего своего проходят, не обходят
            // проходит поперёк или уходит — пропускаем дальше, он сейчас освободит путь («вдруг пройдёт»);
            // обходим стоящего (без приказа, на месте или сам в пробке) и идущего навстречу
            if (b.Order != null && !b.Done && !b.Held && b.Vs > 0.1 && b.Track != null && b.Track.Pieces.Count > 0)
            {
                var (_, _, bdx, bdy) = b.Track.AtMeters(b.Track.MetersAt(b.Along));
                double tx = m.P.X - b.P.X, ty = m.P.Y - b.P.Y, tl = Math.Max(1e-9, JsMath.Hypot(tx, ty));
                if ((bdx * tx + bdy * ty) / tl < 0.5) return;   // не к нам — ждём
            }
            m.HeldSameSec = 0; m.DetourCooldown = M.DetourCooldownSec;
            var F = m.Field;
            var extra = new bool[F.W * F.H];
            var B = b.P;
            double hx = B.Fp.Front / 2 + F.CellW / 2, hy = B.Fp.Depth / 2 + F.CellH / 2, reach = JsMath.Hypot(hx, hy);
            int x0 = (int)Math.Floor((B.X - reach) / F.CellW), x1 = (int)Math.Floor((B.X + reach) / F.CellW);
            int y0 = (int)Math.Floor((B.Y - reach) / F.CellH), y1 = (int)Math.Floor((B.Y + reach) / F.CellH);
            for (int y = Math.Max(0, y0); y <= Math.Min(F.H - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(F.W - 1, x1); x++)
                {
                    var (cx, cy) = F.CenterOf(y * F.W + x);
                    B.ToLocal(cx, cy, out var lx, out var ly);
                    if (Math.Abs(lx) <= hx && Math.Abs(ly) <= hy) extra[y * F.W + x] = true;
                }
            // своя клетка и соседние — свободны, иначе путь не начнётся
            int me = F.CellOf(m.P.X, m.P.Y);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = me % F.W + dx, y = me / F.W + dy;
                    if (x >= 0 && y >= 0 && x < F.W && y < F.H) extra[y * F.W + x] = false;
                }
            var DF = FlowField.Build(geo, r, BattleMap.IsHorse(m.P.U), m.TargetX, m.TargetY, m.NominalFp.Front / 2, extra, m.Pass);
            if (DF.Target < 0) return;
            var route = DF.Route(m.P.X, m.P.Y, m.TargetX, m.TargetY);
            var tr = route == null ? null : Track.Build(DF, route);
            if (tr == null) return;   // в обход не пройти — ждём дальше
            m.RouteField = DF; m.Track = tr; m.Along = 0; m.OnSpot = false; m.Vs = 0;
            m.IgnoreHoldBy = b.P.U.Id; m.Held = false; m.HoldLeft = 0;
            m.Detoured.Add(b.P.U.Name);
        }
        public static double FigAccel(Mover m, Rules r) => r.Move.FigureAccelK * TopSpeed(m.P.U, r) / AccelSec(m.P.U, r);

        // Места перераспределяются (Iron Kings): две одинаковые фигурки меняются местами, если так обеим ближе
        // в сумме больше чем на ReassignGain — строй после брода или толчеи собирается быстрее, никто не ломится
        // к «своему» месту через весь строй. Меняются только фигурки одного размера и численности.
        static void Reassign(Mover m, Rules r)
        {
            var P = m.P; int n = P.Figs.Count;
            if (n < 2) return;
            var slot = Slots(P);
            // у бойцов-тел (Б1) колонна там, где якорь: середина неполной колонны смещена, и обмены по ней выходят ложными —
            // колонны гоняются за меняющимися местами
            bool mb = r.Move.MenBodies;
            double D(int fig, int sl) { var f = m.Figs[fig]; return JsMath.Hypot((mb ? f.AX : f.X) - slot[sl].x, (mb ? f.AY : f.Y) - slot[sl].y); }
            // несколько проходов, пока находятся выгодные обмены: сумма расстояний только убывает — раскачки нет
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        var a = P.Figs[i]; var b = P.Figs[j];
                        if (a.Width != b.Width || a.Depth != b.Depth || a.Men != b.Men) continue;
                        if (m.Figs[i].Wrap || m.Figs[j].Wrap) continue;   // в охвате (Г68) идут не к своим местам — не меняются
                        if (D(i, i) + D(j, j) - (D(j, i) + D(i, j)) > r.Move.ReassignGain)
                        { (m.Figs[i], m.Figs[j]) = (m.Figs[j], m.Figs[i]); any = true; }
                    }
                if (!any) break;
            }
        }
    }
}
