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
// и ускорения; в непроходимое не входят — скользят вдоль. Толкотня тел — шаг 2, узости — шаг 3.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class MoveOrder { public double X, Y, Facing; }   // куда встать центру строя (м) и куда смотреть

    public sealed class FigState { public double X, Y, Vx, Vy; }    // фигурка в мире: где и как быстро, м и м/с

    public sealed class Mover
    {
        public Placed P;                 // центр строя, курс (Facing), фигурки (их X, Y — места в строю)
        public List<FigState> Figs = new List<FigState>();
        public MoveOrder Order;
        public FlowField Field;
        public Track Track;
        public bool Side;                // Г54: ближний ход — без поворота
        public double Vs;                // скорость по пути, м нормы в секунду; переходит в следующий ход
        public double Along;             // сколько нормы пройдено по пути с начала приказа
        public bool OnSpot, Done;        // дошёл до точки / ещё и встал к заданному направлению
        public double AboutLeft;         // сколько ещё длится разворот кругом, с
        public string Note;              // «пути нет», «цель непроходима» — в журнал
        public double Spent, Moved, WheelSec, AboutSec;   // за этот ход: нормы, метров по земле, секунд на повороты

        public static Mover Place(Unit u, double x, double y, double facing, Rules r, double menPerFigure = 10)
        {
            var m = new Mover { P = new Placed { U = u, X = x, Y = y, Facing = MoveSim.Norm(facing) } };
            m.P.Relayout(r, menPerFigure);
            foreach (var f in m.P.Figs) { m.P.ToWorld(f.X, f.Y, out var wx, out var wy); m.Figs.Add(new FigState { X = wx, Y = wy }); }
            return m;
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
            m.Order = o; m.OnSpot = m.Done = false; m.Along = 0; m.Note = null; m.Track = null; m.Field = null;
            double tx = o.X, ty = o.Y;
            if (geo != null) { tx = Math.Max(0, Math.Min(geo.W - 1e-6, tx)); ty = Math.Max(0, Math.Min(geo.H - 1e-6, ty)); }
            m.Field = FlowField.Build(geo, r, BattleMap.IsHorse(m.P.U), tx, ty);
            List<(double x, double y)> route;
            if (m.Field == null) route = new List<(double x, double y)> { (m.P.X, m.P.Y), (tx, ty) };
            else
            {
                if (m.Field.Target < 0) { m.Note = "на карте негде встать"; return; }
                route = m.Field.Route(m.P.X, m.P.Y, tx, ty);
                if (route == null) { m.Note = "пути нет"; return; }
                if (m.Field.CellOf(tx, ty) != m.Field.Target) m.Note = "цель непроходима — встаёт рядом";
            }
            m.Track = Track.Build(m.Field, route);
            if (m.Track == null) { m.Note = "пути нет"; return; }
            if (m.Track.Pieces.Count == 0) m.OnSpot = true;   // уже на месте — остаётся довернуться
            m.Side = m.Track.Cost <= r.Move.CloseShare * BattleMap.UnitSpeed(m.P.U, r);
            // разгон переходит в новый приказ, только если он ведёт туда же, куда отряд уже идёт
            if (m.Side || m.Track.Pieces.Count == 0 || Math.Abs(AngleDiff(m.P.Facing, LegHeading(m.Track, 0))) > r.Move.MarchAlignDeg) m.Vs = 0;
        }

        static double LegHeading(Track t, int leg) =>
            HeadingOf(t.Points[leg + 1].x - t.Points[leg].x, t.Points[leg + 1].y - t.Points[leg].y);
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
            var M = r.Move;
            int steps = (int)Math.Round(M.TurnSec / M.Dt);
            foreach (var m in ms) m.Spent = m.Moved = m.WheelSec = m.AboutSec = 0;
            for (int k = 0; k < steps; k++)
            {
                foreach (var m in ms) Step(m, k * M.Dt, M.Dt, r);
                frame?.Invoke((k + 1) * M.Dt);
            }
            var L = new List<string>();
            foreach (var m in ms)
            {
                if (m.Order == null) continue;
                string name = $"«{m.P.U.Name}»";
                if (m.Track == null) { L.Add($"{name}: {m.Note}"); continue; }
                double norm = BattleMap.UnitSpeed(m.P.U, r);
                var parts = new List<string> { $"прошёл {Js.Num(Js.R1(m.Moved))} м по земле, нормы {Js.Num(Js.R1(m.Spent))} из {Js.Num(norm)}" };
                if (m.WheelSec > 0) parts.Add($"поворот колесом {Js.Num(Js.R1(m.WheelSec))} с");
                if (m.AboutSec > 0) parts.Add($"кругом {Js.Num(Js.R1(m.AboutSec))} с");
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

        static void Step(Mover m, double t, double dt, Rules r)
        {
            var P = m.P;
            // места в строю до шага — из них скорость мест для фигурок
            var prev = new (double x, double y)[P.Figs.Count];
            for (int k = 0; k < prev.Length; k++) { P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var wx, out var wy); prev[k] = (wx, wy); }
            if (m.Order != null && m.Track != null && !m.Done) Lead(m, t, dt, r);
            Follow(m, prev, dt, r);
        }

        // Центр строя: курс, скорость по пути, шаг по пути
        static void Lead(Mover m, double t, double dt, Rules r)
        {
            var M = r.Move; var P = m.P; var u = P.U;
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
                AboutFace(P);
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
        static void AboutFace(Placed P)
        {
            int maxRank = 0, maxFile = 0;
            foreach (var f in P.Figs) { maxRank = Math.Max(maxRank, f.Rank); maxFile = Math.Max(maxFile, f.File); }
            foreach (var f in P.Figs) { f.X = -f.X; f.Y = -f.Y; f.Rank = maxRank - f.Rank; f.File = maxFile - f.File; }
            P.Facing = Norm(P.Facing + 180);
        }

        // Фигурки догоняют свои места: желаемая скорость = скорость места + отставание / SlotTau,
        // не быстрее FigureCatchUp × марш (местность под фигуркой замедляет), ускорение — FigureAccelK × отряда.
        // Место в воде или в стене — фигурка встаёт у ближайшего проходимого. Путь к месту закрыт (строй обходит
        // озеро, а фигурка на другом берегу) — идёт по карте направлений отряда, пока не увидит своё место.
        static void Follow(Mover m, (double x, double y)[] prev, double dt, Rules r)
        {
            var M = r.Move; var P = m.P; var u = P.U;
            double top = TopSpeed(u, r), vmax0 = M.FigureCatchUp * top, amax = M.FigureAccelK * top / AccelSec(u, r) * dt;
            var F = m.Field;
            for (int k = 0; k < P.Figs.Count; k++)
            {
                var s = m.Figs[k];
                P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var sx, out var sy);
                double gx = sx, gy = sy;
                double rho = F != null && F.Inside(s.X, s.Y) ? F.Mult(F.CellOf(s.X, s.Y)) ?? 1 : 1;
                double vmax = vmax0 / rho;
                double dvx, dvy;
                (double x, double y)? via = null;
                if (F != null)
                {
                    if (!Free(F, sx, sy)) { int c = F.NearestPassableCached(F.CellOf(sx, sy)); if (c >= 0) (gx, gy) = F.CenterOf(c); }
                    if (JsMath.Hypot(gx - s.X, gy - s.Y) > F.CellW && Free(F, s.X, s.Y) && double.IsInfinity(F.SegmentCost(s.X, s.Y, gx, gy)))
                    {
                        int next = F.Next(F.CellOf(s.X, s.Y));
                        if (next >= 0) via = F.CenterOf(next);
                    }
                }
                if (via.HasValue)
                {
                    double ex = via.Value.x - s.X, ey = via.Value.y - s.Y, el = Math.Max(1e-9, JsMath.Hypot(ex, ey));
                    dvx = ex / el * vmax; dvy = ey / el * vmax;
                }
                else
                {
                    dvx = (sx - prev[k].x) / dt + (gx - s.X) / M.SlotTau;
                    dvy = (sy - prev[k].y) / dt + (gy - s.Y) / M.SlotTau;
                }
                double dv = JsMath.Hypot(dvx, dvy);
                if (dv > vmax) { dvx *= vmax / dv; dvy *= vmax / dv; }
                double ax = dvx - s.Vx, ay = dvy - s.Vy, a = JsMath.Hypot(ax, ay);
                if (a > amax) { ax *= amax / a; ay *= amax / a; }
                s.Vx += ax; s.Vy += ay;
                double nx = s.X + s.Vx * dt, ny = s.Y + s.Vy * dt;
                if (F == null || Free(F, nx, ny)) { s.X = nx; s.Y = ny; }
                else if (Free(F, nx, s.Y)) { s.X = nx; s.Vy = 0; }
                else if (Free(F, s.X, ny)) { s.Y = ny; s.Vx = 0; }
                else { s.Vx = 0; s.Vy = 0; }
            }
        }
        static bool Free(FlowField f, double x, double y) => f.Inside(x, y) && f.Passable(f.CellOf(x, y));
    }
}
