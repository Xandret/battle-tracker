// ═══════════ BattleAi.cs — ИИ-помощник ГМа (Г113) и оценка обстановки для подсказок адъютанта (Г116) — ЧЕРНОВИК ДО ГМа ═══════════
// Поручения: «держать позицию», «наступать на…», «прикрыть», «фланговый манёвр». ГМ отдаёт поручение отряду (Assign) или стороне
// (AssignSide); в приказной фазе AiOrders раздаёт приказы отрядам с поручением (BattleSession.Go зовёт сам; приказ ГМа на этот ход
// важнее поручения). ИИ видит только то, что видит его сторона (туман, Г107): невидимую цель ищет там, где видел в последний раз.
// Решения пишутся в журнал хода строкой «ИИ: «Отряд» — …» и остаются в AiTask.Why.
// Оценка обстановки (Assess): «под угрозой», «обходят», «без прикрытия», «дрогнет», «врагов больше» — она же источник подсказок
// при отдаче приказа (Advise, Г116б) и разбора после хода (Review, Г116в). Все числа — Rules.Ai.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public enum AiKind { Hold, Advance, Cover, Flank }

    public sealed class AiTask
    {
        public AiKind Kind;
        public int TargetId;                 // Advance и Flank — враг; Cover — свой отряд, которого прикрывать
        public double X = double.NaN, Y = double.NaN, Facing = double.NaN;   // Hold — позиция и куда лицом; Advance без цели — точка
        public int Side;                     // Flank: сторона захода (−1 левый фланг врага, +1 правый), 0 — ещё не выбрана
        public bool Engaging;                // Advance и Flank: уже бьёт цель — приказ не трогать
        public string Why = "";              // последнее решение
    }

    // Обстановка у отряда — глазами его стороны
    public sealed class Situation
    {
        public Mover Unit, Threat;           // ближайший видимый враг, что рядом, идёт на отряд или достаёт стрелой
        public double ThreatM, ThreatTurns;  // до него (край до края) и за сколько ходов дойдёт
        public string ThreatSector = "";     // откуда: front, flank, rear
        public bool Threatened => Threat != null;
        public bool Flanked;                 // обходят: враг рядом сбоку или сзади
        public bool Uncovered;               // стрелок, между которым и ближайшим врагом нет своего рубящего отряда
        public bool Wavering;                // БД ниже Ai.LowMorale — первые потери приведут к проверке
        public bool Outnumbered;             // врагов рядом в Ai.OutnumberK раз больше, чем своих рядом
        public double FoeMenNear, OwnMenNear;
        public List<string> Hints = new List<string>();
    }

    public sealed partial class Battle
    {
        readonly Dictionary<Mover, AiTask> tasks = new Dictionary<Mover, AiTask>();
        public AiTask TaskOf(Mover m) => m != null && tasks.TryGetValue(m, out var t) ? t : null;
        public bool Unassign(Mover m) => m != null && tasks.Remove(m);
        public IEnumerable<Mover> Assigned => tasks.Keys;

        // Поручение отряду. Hold без точки — где стоит и куда смотрит; Advance — на врага (targetId) или в точку (x, y); Cover — свой
        // отряд targetId; Flank — враг targetId. null — отряда на поле нет или поручение невозможно
        public AiTask Assign(Mover m, AiKind kind, int targetId = 0, double x = double.NaN, double y = double.NaN, double facing = double.NaN)
        {
            if (m == null || !OnField(m)) return null;
            if (kind == AiKind.Advance && targetId == 0 && double.IsNaN(x)) return null;
            if ((kind == AiKind.Cover || kind == AiKind.Flank) && ById(targetId) == null) return null;
            if (kind == AiKind.Cover && Enemies(m.P.U, ById(targetId).P.U)) return null;
            if (kind == AiKind.Flank && !Enemies(m.P.U, ById(targetId).P.U)) return null;
            var t = new AiTask { Kind = kind, TargetId = targetId, X = x, Y = y, Facing = facing };
            if (kind == AiKind.Hold && double.IsNaN(x)) { t.X = m.P.X; t.Y = m.P.Y; }
            if (kind == AiKind.Hold && double.IsNaN(facing)) t.Facing = m.P.Facing;
            tasks[m] = t;
            return t;
        }
        // Поручение стороне целиком: «держать» — все где стоят; «наступать» — все на цель или в точку; «прикрыть» без цели — рубящие
        // прикрывают ближайших своих стрелков, стрелки держат позицию; «фланговый манёвр» — конница заходит с фланга, остальные
        // наступают на ту же цель. Возвращает, скольким отрядам дано поручение
        public int AssignSide(int faction, AiKind kind, int targetId = 0, double x = double.NaN, double y = double.NaN)
        {
            int n = 0;
            var mine = Movers.Where(m => OnField(m) && FactionOf(m) == faction).ToList();
            foreach (var m in mine)
            {
                AiTask t = null;
                switch (kind)
                {
                    case AiKind.Hold: t = Assign(m, AiKind.Hold); break;
                    case AiKind.Advance: t = Assign(m, AiKind.Advance, targetId, x, y); break;
                    case AiKind.Cover:
                        if (targetId != 0) t = Assign(m, AiKind.Cover, targetId);
                        else if (Shooter(m)) t = Assign(m, AiKind.Hold);
                        else
                        {
                            var ward = mine.Where(w => w != m && Shooter(w)).OrderBy(w => Dist(m, w)).FirstOrDefault();
                            t = ward != null ? Assign(m, AiKind.Cover, ward.P.U.Id) : Assign(m, AiKind.Hold);
                        }
                        break;
                    case AiKind.Flank:
                        t = Units.IsCav(m.P.U) && !Shooter(m) ? Assign(m, AiKind.Flank, targetId) : Assign(m, AiKind.Advance, targetId, x, y);
                        break;
                }
                if (t != null) n++;
            }
            return n;
        }

        // Приказная фаза: приказы отрядам с поручением. skip — кому не отдавать (у ГМа на этот ход свой приказ). Возвращает строки решений
        public List<string> AiOrders(Func<Mover, bool> skip = null)
        {
            var given = new List<string>();
            foreach (var kv in tasks.ToList())
            {
                var m = kv.Key; var t = kv.Value;
                if (!OnField(m)) { tasks.Remove(m); continue; }
                if (skip != null && skip(m)) continue;
                if (m.Fleeing)
                {
                    if (!m.RallyPending) { Order(m, new MoveOrder { Kind = OrderKind.Rally }); Say(m, t, "бежит — «сплотить»", given); }
                    continue;
                }
                if (InDuel(m)) continue;
                string why;
                switch (t.Kind)
                {
                    case AiKind.Hold: why = DoHold(m, t); break;
                    case AiKind.Advance: why = DoAdvance(m, t); break;
                    case AiKind.Cover: why = DoCover(m, t); break;
                    default: why = DoFlank(m, t); break;
                }
                if (why != null) Say(m, t, why, given);
            }
            return given;
        }
        void Say(Mover m, AiTask t, string why, List<string> given)
        {
            t.Why = why;
            string line = $"ИИ: «{m.P.U.Name}» — {why}";
            given.Add(line);
            if (R.Ai.Journal) events.Add(line);
        }

        // ── поручения ──
        string DoHold(Mover m, AiTask t)
        {
            var A = R.Ai;
            if (m.InMelee) return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "держит позицию в схватке" : null;
            // после разворота на месте (вокруг фланга центр уезжает, Г111 п.3) позиция — где встал, курс — куда развернулся
            if (m.Order != null && m.OnSpot && m.Order.Kind == OrderKind.Move && t.Why.StartsWith("разворачивается")) { t.X = m.P.X; t.Y = m.P.Y; t.Facing = m.Order.Facing; }
            double off = JsMath.Hypot(m.P.X - t.X, m.P.Y - t.Y);
            if (off > A.HoldOffM && !m.Garrisoned)
                return Ensure(m, new MoveOrder { X = t.X, Y = t.Y, Facing = t.Facing }) ? $"возвращается на позицию ({Js.Num(Js.Round(t.X))}, {Js.Num(Js.Round(t.Y))})" : null;
            var s = Assess(m);
            if (Shooter(m))
            {
                var target = InRangeFoes(m).OrderBy(e => e == s.Threat ? -1 : Gap(m, e)).FirstOrDefault();
                if (target != null) return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id }) ? $"стреляет по «{target.P.U.Name}»" : null;
            }
            if (!m.Garrisoned)
            {
                // к кому разворачиваться: кто рядом или идёт на отряд и дойдёт раньше, чем строй успеет довернуться (поворот строем
                // медленный, Г111 п.3: линия в 125 м на 90° — около хода), — лицом к нему заранее
                Mover face = null; double soonest = double.PositiveInfinity, faceH = 0;
                foreach (var e in Foes(m))
                {
                    double h = MoveSim.HeadingOf(e.P.X - m.P.X, e.P.Y - m.P.Y), diff = Math.Abs(MoveSim.AngleDiff(m.P.Facing, h));
                    if (diff <= A.HoldFaceDeg) continue;
                    double gap = Gap(m, e), turns = gap / Math.Max(1, BattleMap.UnitSpeed(e.P.U, R)), wheelTurns = diff / Math.Max(1e-6, MoveSim.WheelRate(m, R)) / R.Move.TurnSec;
                    bool soon = gap <= A.ThreatM || Coming(e, m) && turns <= A.ThreatTurns + wheelTurns;
                    if (soon && turns < soonest) { soonest = turns; face = e; faceH = h; }
                }
                if (face != null)
                {
                    MoveSim.TurnInPlace(m, new MoveOrder { X = m.P.X, Y = m.P.Y, Facing = faceH });
                    return $"разворачивается к «{face.P.U.Name}» ({Units.SectorRu[SectorFrom(m, face)]}, {Js.Num(Js.Round(Gap(m, face)))} м)";
                }
            }
            return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "держит позицию" : null;
        }

        string DoAdvance(Mover m, AiTask t)
        {
            var A = R.Ai; int f = FactionOf(m);
            var target = t.TargetId != 0 ? ById(t.TargetId) : null;
            if (t.TargetId != 0 && (target == null || !OnField(target)))
                return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "цели больше нет — стоит" : null;
            if (target != null && !Sees(f, target)) return SeekLastSeen(m, target);
            if (t.Engaging && target != null && m.Order != null && m.Order.Kind == OrderKind.Attack && m.Order.TargetId == target.P.U.Id) return null;
            // по пути — враг рядом: рубящий бьёт того, кто ближе EngageM; стрелок стреляет по тому, кто в дальности
            var foe = target;
            if (foe == null)
            {
                foe = Shooter(m) ? InRangeFoes(m).OrderBy(e => Gap(m, e)).FirstOrDefault()
                                 : Foes(m).Select(e => (e, g: Gap(m, e))).Where(p => p.g <= A.EngageM).OrderBy(p => p.g).Select(p => p.e).FirstOrDefault();
            }
            if (foe != null)
            {
                string name = foe.P.U.Name;
                if (Shooter(m))
                {
                    bool inRange = Gap(m, foe) <= RangeVs(m, foe);
                    return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = foe.P.U.Id }) ? (inRange ? $"стреляет по «{name}»" : $"подходит на выстрел к «{name}»") : null;
                }
                double gap = Gap(m, foe);
                if (Units.IsCav(m.P.U))
                {
                    var charge = new MoveOrder { Kind = OrderKind.Attack, TargetId = foe.P.U.Id, Charge = true };
                    var pv = Preview(m, charge);
                    if (pv.ChargeOk == true) { t.Engaging = true; Order(m, charge); return $"натиск на «{name}»"; }
                    if (gap <= A.ChargeFromM || m.InMelee || FightOf(m, foe) != null)
                    {
                        t.Engaging = true;
                        return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = foe.P.U.Id }) ? $"атакует «{name}» без натиска ({pv.ChargeWhy ?? "в упор"})" : null;
                    }
                    // далеко — выходит на дистанцию натиска, лицом к цели
                    var (px, py) = Toward(foe, m, A.ChargeFromM + (foe.P.Fp.Depth + m.P.Fp.Depth) / 2);
                    return Ensure(m, new MoveOrder { X = px, Y = py, Facing = MoveSim.HeadingOf(foe.P.X - px, foe.P.Y - py) }, 10) ? $"выходит на дистанцию натиска к «{name}» ({Js.Num(Js.Round(gap))} м)" : null;
                }
                t.Engaging = true;
                return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = foe.P.U.Id }) ? $"атакует «{name}»" : null;
            }
            // в точку
            if (JsMath.Hypot(m.P.X - t.X, m.P.Y - t.Y) <= A.HoldOffM)
                return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "дошёл — стоит" : null;
            double face = double.IsNaN(t.Facing) ? MoveSim.HeadingOf(t.X - m.P.X, t.Y - m.P.Y) : t.Facing;
            return Ensure(m, new MoveOrder { X = t.X, Y = t.Y, Facing = face }) ? $"наступает к точке ({Js.Num(Js.Round(t.X))}, {Js.Num(Js.Round(t.Y))})" : null;
        }

        string DoCover(Mover m, AiTask t)
        {
            var A = R.Ai;
            var ward = ById(t.TargetId);
            if (ward == null || !OnField(ward) || ward.Fleeing)
                return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "прикрывать некого — стоит" : null;
            string wname = ward.P.U.Name;
            // угроза прикрываемому: враг ближе CoverReactM к нему или идущий на него и успевающий за ход
            Mover threat = null; double best = double.PositiveInfinity;
            foreach (var e in Foes(m))
            {
                double g = Gap(ward, e), turns = g / Math.Max(1, BattleMap.UnitSpeed(e.P.U, R));
                bool fighting = FightOf(ward, e) != null;
                if (!(fighting || g <= A.CoverReactM || Coming(e, ward) && turns <= A.ThreatTurns)) continue;
                double score = fighting ? -1 : g;
                if (score < best) { best = score; threat = e; }
            }
            if (threat != null)
            {
                string name = threat.P.U.Name;
                if (Shooter(m)) return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = threat.P.U.Id }) ? $"стреляет по «{name}», что идёт на «{wname}»" : null;
                var o = new MoveOrder { Kind = OrderKind.Attack, TargetId = threat.P.U.Id, Charge = Units.IsCav(m.P.U) };
                if (o.Charge && Preview(m, o).ChargeOk != true) o.Charge = false;
                return Ensure(m, o) ? $"перехватывает «{name}» у «{wname}»{(o.Charge ? " с натиском" : "")}" : null;
            }
            // место — перед прикрываемым со стороны ближайшего врага (нет врагов — по его курсу), на CoverGapM от его строя
            var near = Foes(m).OrderBy(e => Dist(ward, e)).FirstOrDefault();
            double h = near != null ? MoveSim.HeadingOf(near.P.X - ward.P.X, near.P.Y - ward.P.Y) : ward.P.Facing;
            double hr = h * Math.PI / 180, d = (ward.P.Fp.Depth + m.P.Fp.Depth) / 2 + A.CoverGapM;
            double px = Clamp(ward.P.X + Math.Sin(hr) * d, Geo.W), py = Clamp(ward.P.Y - Math.Cos(hr) * d, Geo.H);
            bool there = JsMath.Hypot(m.P.X - px, m.P.Y - py) <= A.HoldOffM * 2 && Math.Abs(MoveSim.AngleDiff(m.P.Facing, h)) <= A.HoldFaceDeg;
            if (there) return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? $"прикрывает «{wname}»" : null;
            return Ensure(m, new MoveOrder { X = px, Y = py, Facing = h }, 10) ? $"встаёт перед «{wname}»{(near != null ? $" со стороны «{near.P.U.Name}»" : "")}" : null;
        }

        string DoFlank(Mover m, AiTask t)
        {
            var A = R.Ai; int f = FactionOf(m);
            var target = ById(t.TargetId);
            if (target == null || !OnField(target)) return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? "цели больше нет — стоит" : null;
            string name = target.P.U.Name;
            if (!Sees(f, target)) return SeekLastSeen(m, target);
            bool cav = Units.IsCav(m.P.U) && !Shooter(m);
            if (t.Engaging || m.InMelee || FightOf(m, target) != null)
            {
                t.Engaging = true;
                return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id }) ? $"бьёт «{name}»" : null;
            }
            target.P.ToLocal(m.P.X, m.P.Y, out var lx, out var ly);
            double gap = Gap(m, target);
            // сбоку от его строя (как считает сектор схватка) или за ним, но не впереди его передней шеренги — иначе удар придёт в угол,
            // во фронт. После поворота на месте строй уезжает вокруг фланга (Г111 п.3) — оттуда, где встал, и бьёт
            bool ahead = ly < -target.P.Fp.Depth / 2 - A.HoldOffM * 2;
            bool beside = !ahead && (Math.Abs(lx) > target.P.Fp.Front / 2 || ly > target.P.Fp.Depth / 2);
            if (beside && gap <= (cav ? A.ChargeFromM : A.FlankGapM) + A.HoldOffM * 2)
            {
                var o = new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id, Charge = cav };
                if (o.Charge && Preview(m, o).ChargeOk != true) o.Charge = false;
                t.Engaging = true; Order(m, o);
                return $"бьёт во фланг «{name}»{(o.Charge ? " с натиском" : "")}";
            }
            // точка захода: сбоку от строя врага, на дистанции натиска (коннице) или FlankGapM (пешим), лицом к его центру
            double d = target.P.Fp.Front / 2 + (cav ? A.ChargeFromM : A.FlankGapM) + m.P.Fp.Depth / 2;
            (double x, double y, double face) PointOn(int side)
            {
                target.P.ToWorld(side * d, 0, out var px, out var py);
                px = Clamp(px, Geo.W); py = Clamp(py, Geo.H);
                return (px, py, MoveSim.HeadingOf(target.P.X - px, target.P.Y - py));
            }
            bool Busy(int side)
            {
                var p = PointOn(side);
                return Foes(m).Any(e => e != target && JsMath.Hypot(e.P.X - p.x, e.P.Y - p.y) <= e.P.Fp.Front / 2 + m.P.Fp.Front / 2);
            }
            if (t.Side == 0)
            {
                int near = lx < 0 ? -1 : 1;
                t.Side = Busy(near) && !Busy(-near) ? -near : near;
            }
            var pt = PointOn(t.Side);
            var mv = new MoveOrder { X = pt.x, Y = pt.y, Facing = pt.face };
            var pv = Preview(m, mv);
            if (pv.Note == "пути нет")
            {
                t.Side = -t.Side; pt = PointOn(t.Side); mv = new MoveOrder { X = pt.x, Y = pt.y, Facing = pt.face }; pv = Preview(m, mv);
                if (pv.Note == "пути нет")
                {
                    t.Engaging = true;
                    return Ensure(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id }) ? $"обойти «{name}» нельзя — атакует в лоб" : null;
                }
            }
            string sideRu = t.Side < 0 ? "левый" : "правый";
            return Ensure(m, mv, 10) ? $"заходит в {sideRu} фланг «{name}»{(pv.ReachThisTurn ? "" : $" — ещё {Js.Num(TurnsOf(m, pv))} ход.")}" : null;
        }

        string SeekLastSeen(Mover m, Mover target)
        {
            int f = FactionOf(m);
            if (LastSeen.TryGetValue((f, target.P.U.Id), out var ls) && JsMath.Hypot(m.P.X - ls.x, m.P.Y - ls.y) > R.Ai.HoldOffM)
                return Ensure(m, new MoveOrder { X = ls.x, Y = ls.y, Facing = MoveSim.HeadingOf(ls.x - m.P.X, ls.y - m.P.Y) }, 10) ? $"«{target.P.U.Name}» не видно — идёт туда, где видел в последний раз" : null;
            return Ensure(m, new MoveOrder { Kind = OrderKind.Hold }) ? $"«{target.P.U.Name}» не видно — ждёт" : null;
        }

        // Отдать приказ, если у отряда ещё не такой же (тот же род, цель, натиск; точка — в пределах tolM). true — отдан
        bool Ensure(Mover m, MoveOrder o, double tolM = 1)
        {
            var c = m.Order;
            if (c != null && c.Kind == o.Kind && c.TargetId == o.TargetId && c.Charge == o.Charge)
            {
                if (o.Kind == OrderKind.Hold || o.Kind == OrderKind.Attack) return false;
                if (JsMath.Hypot(c.X - o.X, c.Y - o.Y) <= tolM && Math.Abs(MoveSim.AngleDiff(c.Facing, o.Facing)) <= R.Ai.HoldFaceDeg) return false;
            }
            Order(m, o);
            return true;
        }
        static double Clamp(double v, double max) => Math.Max(1, Math.Min(max - 1, v));
        double TurnsOf(Mover m, OrderPreview p) => Math.Ceiling(p.Cost / Math.Max(1, BattleMap.UnitSpeed(m.P.U, R)) - 1e-6);   // ходов на весь путь
        static double Dist(Mover a, Mover b) => JsMath.Hypot(a.P.X - b.P.X, a.P.Y - b.P.Y);
        // точка на линии от foe к m в dist м от центра foe
        static (double x, double y) Toward(Mover foe, Mover m, double dist)
        {
            double dx = m.P.X - foe.P.X, dy = m.P.Y - foe.P.Y, l = Math.Max(1e-9, JsMath.Hypot(dx, dy));
            return (foe.P.X + dx / l * dist, foe.P.Y + dy / l * dist);
        }
        // видимые стороне отряда враги на поле
        IEnumerable<Mover> Foes(Mover m) { int f = FactionOf(m); return Movers.Where(e => e != m && OnField(e) && Enemies(m.P.U, e.P.U) && Sees(f, e)); }
        IEnumerable<Mover> InRangeFoes(Mover m) => Foes(m).Where(e => Gap(m, e) <= RangeVs(m, e));
        // идёт на отряд: приказ «атаковать» его или идёт курсом на него
        bool Coming(Mover e, Mover m)
        {
            if (e.Order == null || e.Done || e.Fleeing) return false;
            if (e.Order.Kind == OrderKind.Attack) return e.Order.TargetId == m.P.U.Id;
            if (e.Order.Kind != OrderKind.Move) return false;
            return Math.Abs(MoveSim.AngleDiff(e.P.Facing, MoveSim.HeadingOf(m.P.X - e.P.X, m.P.Y - e.P.Y))) <= R.Ai.FlankDeg;
        }
        string SectorFrom(Mover m, Mover e)
        {
            double d = Math.Abs(MoveSim.AngleDiff(m.P.Facing, MoveSim.HeadingOf(e.P.X - m.P.X, e.P.Y - m.P.Y)));
            return d <= R.Ai.FlankDeg ? "front" : d >= R.Sectors.RearMin ? "rear" : "flank";
        }
        // Между стрелком w и врагом e стоит свой рубящий отряд (в полосе в полфронта обоих плюс CoverGapM от линии); moved/at — где считать
        // отряд moved (предпросмотр приказа: уйдёт — откроет ли)
        bool Covered(Mover w, Mover e, Mover moved = null, (double x, double y)? at = null)
        {
            double dx = e.P.X - w.P.X, dy = e.P.Y - w.P.Y, L = Math.Max(1e-9, JsMath.Hypot(dx, dy)); dx /= L; dy /= L;
            foreach (var o in Movers)
            {
                if (o == w || !OnField(o) || o.Fleeing || Shooter(o) || Enemies(w.P.U, o.P.U)) continue;
                double ox = (o == moved && at.HasValue ? at.Value.x : o.P.X) - w.P.X, oy = (o == moved && at.HasValue ? at.Value.y : o.P.Y) - w.P.Y;
                double along = ox * dx + oy * dy, across = Math.Abs(-ox * dy + oy * dx);
                if (along > 0 && along < L && across <= (w.P.Fp.Front + o.P.Fp.Front) / 4 + R.Ai.CoverGapM) return true;
            }
            return false;
        }

        // ── оценка обстановки (Г116) ──
        public Situation Assess(Mover m)
        {
            var s = new Situation { Unit = m };
            if (m == null || !OnField(m)) return s;
            var A = R.Ai; string name = m.P.U.Name;
            double best = double.PositiveInfinity;
            foreach (var e in Foes(m))
            {
                double gap = Gap(m, e), turns = gap / Math.Max(1, BattleMap.UnitSpeed(e.P.U, R));
                bool fighting = FightOf(m, e) != null;
                bool near = fighting || gap <= A.ThreatM || Coming(e, m) && turns <= A.ThreatTurns || Shooter(e) && gap <= RangeVs(e, m);
                if (near && gap <= A.ThreatM) s.FoeMenNear += e.P.U.Soldiers;
                if (!near) continue;
                string sec = SectorFrom(m, e);
                if (sec != "front" && !Shooter(e)) s.Flanked = true;
                double score = fighting ? gap - 1e6 : gap;
                if (score < best) { best = score; s.Threat = e; s.ThreatM = gap; s.ThreatTurns = turns; s.ThreatSector = sec; }
            }
            s.OwnMenNear = m.P.U.Soldiers + Movers.Where(o => o != m && OnField(o) && !o.Fleeing && !Enemies(m.P.U, o.P.U) && Gap(m, o) <= A.ThreatM).Sum(o => o.P.U.Soldiers);
            s.Outnumbered = s.FoeMenNear > s.OwnMenNear * A.OutnumberK;
            s.Wavering = m.P.U.Morale < A.LowMorale;
            if (Shooter(m) && s.Threat != null && !Shooter(s.Threat)) s.Uncovered = !Covered(m, s.Threat);
            if (s.Threat != null)
            {
                string e = s.Threat.P.U.Name, where = Units.SectorRu[s.ThreatSector];
                bool coming = Coming(s.Threat, m);
                s.Hints.Add($"«{name}» под угрозой: «{e}» {where} в {Js.Num(Js.Round(s.ThreatM))} м{(coming ? $", дойдёт за {Js.Num(Js.R1(s.ThreatTurns))} ход." : "")}");
                if (s.Flanked)
                {
                    var fl = Foes(m).Where(e2 => !Shooter(e2) && (Gap(m, e2) <= A.ThreatM || FightOf(m, e2) != null || Coming(e2, m)) && SectorFrom(m, e2) != "front").OrderBy(e2 => Gap(m, e2)).FirstOrDefault();
                    if (fl != null) s.Hints.Add($"обходят: «{fl.P.U.Name}» заходит {Units.SectorRu[SectorFrom(m, fl)]} «{name}»");
                }
            }
            if (s.Uncovered) s.Hints.Add($"лучники без прикрытия: «{s.Threat.P.U.Name}» в {Js.Num(Js.Round(s.ThreatM))} м от «{name}», своих между ними нет");
            if (s.Wavering) s.Hints.Add($"«{name}» дрогнет: БД {Js.Num(Js.Round(m.P.U.Morale))} — первые потери приведут к проверке");
            if (s.Outnumbered) s.Hints.Add($"врагов больше: у «{name}» рядом {Js.Num(Js.Round(s.FoeMenNear))} врагов против {Js.Num(Js.Round(s.OwnMenNear))} своих");
            return s;
        }

        // Подсказки адъютанта к приказу (Г116б) — до его отдачи. Пусто — замечаний нет
        public List<string> Advise(Mover m, MoveOrder o)
        {
            var h = new List<string>();
            if (m == null || o == null || !OnField(m)) return h;
            var A = R.Ai; string name = m.P.U.Name;
            var p = Preview(m, o);
            if (p.Note != null) h.Add(p.Note);
            if (o.Kind == OrderKind.Attack && o.Charge && p.ChargeOk == false) h.Add($"натиск не успеет разогнаться: {p.ChargeWhy}");
            if ((o.Kind == OrderKind.Move || o.Kind == OrderKind.Attack) && p.Note == null && !p.ReachThisTurn && !(o.Kind == OrderKind.Attack && Shooter(m) && p.InRange == true))
                h.Add($"за ход не дойдёт: ещё {Js.Num(TurnsOf(m, p))} ход.");
            if (o.Kind == OrderKind.Attack && Shooter(m) && p.InRange == false)
                h.Add($"стрелок не достаёт: до цели {Js.Num(Js.Round(p.Gap))} м при дальности {Js.Num(Js.Round(p.Range))} м — подойдёт");
            var s = Assess(m);
            if (o.Kind == OrderKind.Hold && s.Threat != null && s.ThreatSector != "front")
                h.Add($"обходят: «{s.Threat.P.U.Name}» {Units.SectorRu[s.ThreatSector]} в {Js.Num(Js.Round(s.ThreatM))} м — стоя «{name}» не ответит, развернуть");
            if (o.Kind == OrderKind.Attack && !Shooter(m))
            {
                var t = ById(o.TargetId);
                if (t != null && OnField(t) && t.P.U.Soldiers > m.P.U.Soldiers * A.OutnumberK)
                    h.Add($"враг сильнее числом: «{t.P.U.Name}» {Js.Num(Js.Round(t.P.U.Soldiers))} против {Js.Num(Js.Round(m.P.U.Soldiers))}");
                if (s.Wavering) h.Add($"«{name}» дрогнет: БД {Js.Num(Js.Round(m.P.U.Morale))} — первые потери приведут к проверке");
            }
            // уходя, откроет своих стрелков
            if ((o.Kind == OrderKind.Move || o.Kind == OrderKind.Attack || o.Kind == OrderKind.Retreat) && !Shooter(m) && p.Note == null)
            {
                foreach (var w in Movers.Where(w => w != m && OnField(w) && !w.Fleeing && Shooter(w) && !Enemies(m.P.U, w.P.U)))
                {
                    var sw = Assess(w);
                    if (sw.Threat == null || Shooter(sw.Threat) || sw.Uncovered) continue;
                    if (Covered(w, sw.Threat) && !Covered(w, sw.Threat, m, (p.EndX, p.EndY)))
                        h.Add($"лучники без прикрытия: уйдя, «{name}» откроет «{w.P.U.Name}» перед «{sw.Threat.P.U.Name}»");
                }
            }
            return h;
        }

        // Разбор после хода (Г116в) для стороны: обстановка у каждого отряда и схватки, что идут плохо
        public List<string> Review(int faction)
        {
            var L = new List<string>();
            foreach (var m in Movers.Where(x => OnField(x) && FactionOf(x) == faction && !x.Fleeing)) L.AddRange(Assess(m).Hints);
            foreach (var f in Fights)
            {
                if (f.Over) continue;
                var mine = FactionOf(f.A) == faction ? f.A : FactionOf(f.B) == faction ? f.B : null;
                if (mine == null) continue;
                var other = f.Other(mine);
                double my = mine == f.A ? f.LossA : f.LossB, his = mine == f.A ? f.LossB : f.LossA;
                if (my > 0 && my > his * R.Ai.LoseK) L.Add($"«{mine.P.U.Name}» проигрывает схватку с «{other.P.U.Name}»: −{Js.Num(Js.Round(my))} против −{Js.Num(Js.Round(his))} за ход");
            }
            return L;
        }
    }
}
