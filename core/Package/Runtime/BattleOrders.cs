// ═══════════ BattleOrders.cs — предпросмотр приказа и сессия боя (И2, Г79–Г81) — ЧЕРНОВИК ДО ГМа ═══════════
// Предпросмотр: что будет, если отдать отряду приказ, — путь, где он встанет к концу этого хода, хватит ли разбега
// на натиск (К29), достаёт ли стрелок. Отряд не меняется: путь считается так же, как в MoveSim.Give, но в сторону.
// Сессия (WEGO, Г15): фаза приказов — приказы копятся; «Ход!» — все приказы разом уходят отрядам, ход считается
// шагами (Г43: во время показа), в конце — журнал и проверка, не кончилась ли битва.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class OrderPreview
    {
        public List<(double x, double y)> Route = new List<(double x, double y)>();   // путь центра строя
        public double EndX, EndY, EndFacing;     // где встанет к концу этого хода (примерно: без пробок, повороты не в счёт)
        public double Cost, ThisTurn;            // нормы на весь путь; сколько пройдёт за этот ход
        public bool ReachThisTurn;
        public bool? ChargeOk; public string ChargeWhy;   // натиск: хватит ли разбега по чистому (К29)
        public bool? InRange; public double Range, Gap;   // стрелок: достаёт ли до цели сейчас
        public string Note;                      // почему нельзя или что не так («пути нет», «бежит — слушает только «сплотить»»)
    }

    public sealed partial class Battle
    {
        // Точка отхода по умолчанию (Г81): прямо назад на столько, сколько за ход пятится (половина нормы)
        public (double x, double y) RetreatPoint(Mover m)
        {
            double back = R.Move.SideSpeed * BattleMap.UnitSpeed(m.P.U, R), h = m.P.Facing * Math.PI / 180;
            return (Math.Max(1, Math.Min(Geo.W - 1, m.P.X - Math.Sin(h) * back)), Math.Max(1, Math.Min(Geo.H - 1, m.P.Y + Math.Cos(h) * back)));
        }

        public OrderPreview Preview(Mover m, MoveOrder o)
        {
            var p = new OrderPreview { EndX = m.P.X, EndY = m.P.Y, EndFacing = m.P.Facing };
            p.Route.Add((m.P.X, m.P.Y));
            var u = m.P.U;
            if (!OnField(m)) { p.Note = "отряда на поле нет"; return p; }
            if (m.Fleeing && o.Kind != OrderKind.Rally) { p.Note = "бежит — слышит только «сплотить»"; return p; }
            if (o.Kind == OrderKind.Hold || o.Kind == OrderKind.Rally) { p.ReachThisTurn = true; return p; }
            double norm = BattleMap.UnitSpeed(u, R);
            double tx = o.X, ty = o.Y, face = o.Facing, stop = 0;
            Mover t = null;
            bool side = false;
            if (o.Kind == OrderKind.Retreat)
            {
                if (double.IsNaN(tx) || double.IsNaN(ty)) (tx, ty) = RetreatPoint(m);
                face = m.P.Facing; side = true;
            }
            else if (o.Kind == OrderKind.Attack)
            {
                t = ById(o.TargetId);
                if (t == null || !OnField(t)) { p.Note = "цели нет"; return p; }
                if (!Enemies(u, t.P.U)) { p.Note = "это свои"; return p; }
                face = MoveSim.HeadingOf(t.P.X - m.P.X, t.P.Y - m.P.Y);
                if (Shooter(m))
                {
                    p.Gap = Gap(m, t); p.Range = RangeVs(m, t); p.InRange = p.Gap <= p.Range;
                    if (p.InRange == true) { p.EndFacing = face; p.ReachThisTurn = true; return p; }   // стоит и стреляет
                    double hx = t.P.X - m.P.X, hy = t.P.Y - m.P.Y, hl = Math.Max(1e-9, JsMath.Hypot(hx, hy));
                    double standAt = p.Range * ShootStopShare + (t.P.Fp.Depth + m.P.Fp.Depth) / 2;
                    tx = t.P.X - hx / hl * standAt; ty = t.P.Y - hy / hl * standAt;
                }
                else { tx = t.P.X; ty = t.P.Y; stop = (t.P.Fp.Depth + m.P.Fp.Depth) / 2; }
            }
            tx = Math.Max(0, Math.Min(Geo.W - 1e-6, tx)); ty = Math.Max(0, Math.Min(Geo.H - 1e-6, ty));
            var F = FlowField.Build(Geo, R, BattleMap.IsHorse(u), tx, ty, m.NominalFp.Front / 2, null, m.Pass);
            List<(double x, double y)> route;
            if (F == null) route = new List<(double x, double y)> { (m.P.X, m.P.Y), (tx, ty) };
            else
            {
                if (F.Target < 0) { p.Note = "на карте негде встать"; return p; }
                route = F.Route(m.P.X, m.P.Y, tx, ty);
                if (route == null) { p.Note = "пути нет"; return p; }
                if (F.CellOf(tx, ty) != F.Target) p.Note = "цель непроходима — встанет рядом";
            }
            var tr = Track.Build(F, route);
            if (tr == null) { p.Note = "пути нет"; return p; }
            p.Route = tr.Points;
            if (o.Kind == OrderKind.Move) side = tr.Cost <= R.Move.CloseShare * norm;   // Г54: близко — боком и назад, без поворота
            // за ход: норма; пятится или идёт боком/назад — половина (Г54, Г81); до врага — не ближе касания
            bool back = side && tr.Pieces.Count > 0 && Math.Abs(MoveSim.AngleDiff(m.P.Facing, MoveSim.HeadingOf(tr.Pieces[0].X1 - tr.Pieces[0].X0, tr.Pieces[0].Y1 - tr.Pieces[0].Y0))) > R.Move.ForwardConeDeg;
            double budget = back ? norm * R.Move.SideSpeed : norm;
            double lastRho = tr.Pieces.Count > 0 ? tr.Pieces[tr.Pieces.Count - 1].Rho : 1;
            double need = Math.Max(0, tr.Cost - stop * lastRho);
            p.Cost = need; p.ThisTurn = Math.Min(need, budget); p.ReachThisTurn = need <= budget + 1e-6;
            var (ex, ey, _) = tr.At(p.ThisTurn);
            p.EndX = ex; p.EndY = ey;
            p.EndFacing = p.ReachThisTurn ? MoveSim.Norm(face) : side ? m.P.Facing : tr.Pieces.Count > 0 ? MoveSim.HeadingOf(tr.Pieces[Math.Max(0, tr.At(p.ThisTurn).piece)].X1 - tr.Pieces[Math.Max(0, tr.At(p.ThisTurn).piece)].X0, tr.Pieces[Math.Max(0, tr.At(p.ThisTurn).piece)].Y1 - tr.Pieces[Math.Max(0, tr.At(p.ThisTurn).piece)].Y0) : face;
            // натиск (К29): конница, разбег по прямой по чистому не меньше ChargeRunUp до касания, и дойти за этот ход
            if (o.Kind == OrderKind.Attack && o.Charge && t != null && !Shooter(m))
            {
                if (!Units.IsCav(u)) { p.ChargeOk = false; p.ChargeWhy = "натиск — только конница"; }
                else
                {
                    var pts = tr.Points;
                    var (ax, ay) = pts.Count >= 2 ? pts[pts.Count - 2] : (m.P.X, m.P.Y);
                    double dx = tx - ax, dy = ty - ay, dl = Math.Max(1e-9, JsMath.Hypot(dx, dy));
                    double cx = tx - dx / dl * stop, cy = ty - dy / dl * stop;   // точка касания на последнем прямом отрезке
                    var (len, clear) = BattleMap.RunOver(u, new[] { ax / Geo.W, ay / Geo.H }, new[] { cx / Geo.W, cy / Geo.H }, Geo, R);
                    double run = Math.Min(len, JsMath.Hypot(cx - m.P.X, cy - m.P.Y));
                    if (!p.ReachThisTurn) { p.ChargeOk = false; p.ChargeWhy = "не доскачет за ход"; }
                    else if (!clear) { p.ChargeOk = false; p.ChargeWhy = "на разбеге лес, болото или брод"; }
                    else if (run < R.Map.ChargeRunUp) { p.ChargeOk = false; p.ChargeWhy = $"разбег {Js.Num(Js.Round(run))} м из {Js.Num(R.Map.ChargeRunUp)}"; }
                    else p.ChargeOk = true;
                }
            }
            return p;
        }
    }

    public enum Phase { Orders, Playing, Over }

    // Сессия боя (WEGO): приказы обеим сторонам с одного экрана (Г79) — хотсит и туман войны позже
    public sealed class BattleSession
    {
        public readonly Battle Battle;
        public int Turn = 1;
        public Phase Phase = Phase.Orders;
        public readonly Dictionary<Mover, MoveOrder> Pending = new Dictionary<Mover, MoveOrder>();
        public readonly List<List<string>> Logs = new List<List<string>>();
        public readonly Dictionary<int, string> SideNames = new Dictionary<int, string>();
        public string Outcome;            // итог, когда битва кончилась
        public int Winner = -1;           // сторона-победитель (FactionId); 0 — ничья
        public int MaxTurns;              // 0 — без ограничения

        public BattleSession(Battle battle) { Battle = battle; }

        public static int SideOf(Mover m) => m.P.U.FactionId ?? 0;
        public IEnumerable<int> Sides => Battle.Movers.Select(SideOf).Distinct().OrderBy(s => s);
        public IEnumerable<Mover> UnitsOf(int side) => Battle.Movers.Where(m => SideOf(m) == side);
        static bool InLine(Mover m) => m.P.U.Status == "active" && m.P.U.Soldiers > 0 && m.Figs.Count > 0 && !m.Gone;
        static bool Present(Mover m) => (m.P.U.Status == "active" || m.P.U.Status == "fled") && m.P.U.Soldiers > 0 && m.Figs.Count > 0 && !m.Gone;

        // Приказ на этот ход (уйдёт отряду по «Ход!»); null — принят, иначе — почему нет
        public string SetOrder(Mover m, MoveOrder o)
        {
            if (Phase != Phase.Orders) return "сейчас идёт ход — приказы между ходами";
            if (!Present(m)) return "отряда на поле нет";
            if (m.Fleeing && o.Kind != OrderKind.Rally) return "бежит — слышит только «сплотить»";
            if (!m.Fleeing && o.Kind == OrderKind.Rally) return "в строю — сплачивать некого";
            if (o.Kind == OrderKind.Attack)
            {
                var t = Battle.ById(o.TargetId);
                if (t == null || !Present(t)) return "цели нет";
                if (SideOf(t) == SideOf(m) && SideOf(m) != 0) return "это свои";
            }
            Pending[m] = o;
            return null;
        }
        public void ClearOrder(Mover m) => Pending.Remove(m);
        // Какой приказ у отряда будет в этом ходу: новый или прежний (идёт дальше)
        public MoveOrder OrderOf(Mover m) => Pending.TryGetValue(m, out var o) ? o : m.Order;

        // «Ход!»: приказы разом уходят отрядам, ход начинается
        public void Go()
        {
            if (Phase != Phase.Orders) return;
            Battle.AiOrders(m => Pending.ContainsKey(m));   // Г113: отряды с поручением ИИ — приказы сами; приказ ГМа на этот ход важнее
            foreach (var kv in Pending)
            {
                var o = kv.Value;
                if (o.Kind == OrderKind.Retreat && (double.IsNaN(o.X) || double.IsNaN(o.Y))) (o.X, o.Y) = Battle.RetreatPoint(kv.Key);
                Battle.Order(kv.Key, o);
            }
            Pending.Clear();
            Battle.BeginTurn();
            Phase = Phase.Playing;
        }
        // Шаг хода; false — ход кончился (журнал — в Logs, фаза — снова приказы или конец битвы)
        public bool Step(Action<double> frame = null)
        {
            if (Phase != Phase.Playing) return false;
            if (Battle.Step(frame)) return true;
            Logs.Add(Battle.EndTurn());
            Turn++;
            CheckOver();
            if (Phase == Phase.Playing) Phase = Phase.Orders;
            return false;
        }
        // Конец битвы: у стороны не осталось отрядов в строю (бежали, ушли, уничтожены) — она проиграла
        void CheckOver()
        {
            var alive = Sides.Where(s => UnitsOf(s).Any(InLine)).ToList();
            if (alive.Count > 1 && (MaxTurns <= 0 || Turn <= MaxTurns)) return;
            Phase = Phase.Over;
            if (alive.Count == 1) { Winner = alive[0]; Outcome = $"Победа: {Name(Winner)}"; }
            else if (alive.Count == 0) { Winner = 0; Outcome = "Обе стороны бежали или уничтожены"; }
            else { Winner = 0; Outcome = "Неясный исход — ходы кончились, у обеих сторон есть отряды в строю"; }
        }
        public string Name(int side) => SideNames.TryGetValue(side, out var n) ? n : $"сторона {side}";
    }
}
