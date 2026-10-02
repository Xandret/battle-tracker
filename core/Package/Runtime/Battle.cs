// ═══════════ Battle.cs — бой в движении (И1, БД1, БД3, БД4; Г62–Г64, Г68–Г74, Г44, Г29, Г30) — ЧЕРНОВИК ДО ГМа ═══════════
// Один ход боя — то же движение (MoveSim.Step), а между его шагами — рукопашная. Схватка (Fight) — пара
// врагов, чьи фигурки коснулись (не дальше MeleeGap): обмены ударами по кругу в 15 с от касания (Г62) —
// сначала атаки того, кто начал (Г44), на каждую — ответ, если удар пришёл во фронт; потом атаки второго.
// Бой идёт через границу хода без перерыва. Урон течёт каждый шаг по формуле стола, как в MeleeSim; в конце
// окна — округление, летальность, БД. Бьют колонны фигурок, что касаются врага (Г27). Натиск (Г29) — всплеск
// в первые 2 с касания, цель в это время не отвечает: нужен приказ «натиск», конница и разбег ≥ 50 м по
// чистому (К29); пики во фронт его гасят. Павшие фигурки выпадают из строя, строй смыкается с краёв (Г30).
// Атакованный во фланг сам не поворачивается — ждёт приказа (Г63). Свисающие колонны огибают врага перед
// фронтом — во фланг и в тыл (Г68); колонны делятся между врагами, ответы — общие на круг (Г69).
// БД и бегство в ходу (БД4, Г70–Г74): после каждого удара с потерями — проверки, как подсказывает журнал стола;
// провал — толпа бежит прочь от врага на норме, свои рядом бросают проверку (каскадная паника); «сплотить» — бросок.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Fight
    {
        public Mover A, B;                  // A начинает обмены (Г44)
        public double T0, LastTouch;        // когда коснулись; когда касались последний раз (часы боя)
        public double CycleEnd;             // конец текущего круга обменов
        public bool Over, Touching;         // Touching — касаются сейчас; SA, SB — как касались в последний раз
        public Side SA = new Side(), SB = new Side();
        public double LossA, LossB;         // потери за этот ход — для журнала (сколько бойцов выбыло, по шагам)
        public List<string> Notes = new List<string>();
        internal List<Win> Wins = new List<Win>();

        // Как отряд касается врага: доля бойцов в колоннах, что касаются (Г27), и куда приходится удар — во фронт,
        // фланг или тыл врага (доли от касающихся)
        public sealed class Side { public double Engaged, Front, Flank, Rear; }
        internal sealed class Win
        {
            public Mover Att, Def; public bool Counter, Charge;
            public double Extra = 1, Factor = 1, T0, T1, U, N0, Acc;
            public bool Open, Closed, Skipped;
        }
        public Side Of(Mover m) => m == A ? SA : SB;
        public Mover Other(Mover m) => m == A ? B : A;
    }

    public sealed partial class Battle
    {
        public Geo Geo; public Rules R; public EngineContext Ctx;
        public List<Mover> Movers = new List<Mover>();
        public List<Fight> Fights = new List<Fight>();
        public double Clock;                 // часы боя, с — с начала первого хода
        public double MenPerFigure = 10;
        public double BurstSec = 2, ContactEverySec = 0.5, FightEndSec = 2, ReplanSec = 1, ReplanMoveM = 5;
        public List<string> Details = new List<string>();   // окна ударов этого хода: «8,2 с · A → B (натиск): −40»
        public bool MoraleChecks = true;     // БД4: проверки БД и на побег в ходу; сверка обмена ударами со столом — без них
        public bool PanicMoraleLoss;         // переключатель трекера «−100 БД вместе с проверкой» (по умолчанию выключен)
        readonly List<string> events = new List<string>();   // проверки, бегство, паника, «сплотить» — в журнал хода
        readonly Dictionary<Mover, int> chargesLeft = new Dictionary<Mover, int>();
        readonly Dictionary<Mover, (double x, double y)> aimed = new Dictionary<Mover, (double x, double y)>();

        public Battle(Geo geo, Rules r, EngineContext ctx) { Geo = geo; R = r; Ctx = ctx; Ctx.Rules = r; }

        public Mover Add(Unit u, double x, double y, double facing)
        {
            var m = Mover.Place(u, x, y, facing, R, MenPerFigure);
            Movers.Add(m);
            return m;
        }
        public Mover ById(int id) => Movers.FirstOrDefault(m => m.P.U.Id == id);
        static bool Enemies(Unit a, Unit b) => !(a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId);
        static bool Alive(Mover m) => m.P.U.Status == "active" && m.P.U.Soldiers > 0 && m.P.Figs.Count > 0;
        // на поле: в строю или бежит — бегущего можно догнать и рубить, он не отвечает (Г70, как преследование за столом)
        static bool OnField(Mover m) => (m.P.U.Status == "active" || m.P.U.Status == "fled") && m.P.U.Soldiers > 0 && m.P.Figs.Count > 0 && !m.Gone;
        // начинает обмен (Г44) тот, у кого приказ «атаковать» или кто идёт; «держать позицию» и стоящий — отвечают
        static bool Attacking(Mover m) => m.Order != null && (m.Order.Kind == OrderKind.Attack || m.Order.Kind == OrderKind.Move && !m.Done);
        Fight FightOf(Mover x, Mover y) => Fights.FirstOrDefault(f => !f.Over && (f.A == x && f.B == y || f.A == y && f.B == x));

        // ── приказы (Г15) ──
        public void Order(Mover m, MoveOrder o)
        {
            if (m.Fleeing) { if (o.Kind == OrderKind.Rally) m.RallyPending = true; return; }   // бегущий слышит только «сплотить» (Г72)
            if (o.Kind == OrderKind.Rally) return;
            aimed.Remove(m);
            if (o.Kind == OrderKind.Hold) { m.Order = o; m.Track = null; m.Done = true; m.Vs = 0; return; }
            if (o.Kind == OrderKind.Attack)
            {
                var t = ById(o.TargetId);
                if (t != null) { if (Shooter(m)) AimShoot(m, o, t); else Aim(m, o, t); }
                return;
            }
            MoveSim.Give(m, o, Geo, R);
        }
        // идти на цель: в её центр, лицом к ней; у её строя остановят тела (Г58)
        void Aim(Mover m, MoveOrder o, Mover t)
        {
            o.X = t.P.X; o.Y = t.P.Y; o.Facing = MoveSim.HeadingOf(t.P.X - m.P.X, t.P.Y - m.P.Y);
            MoveSim.Give(m, o, Geo, R);
            aimed[m] = (t.P.X, t.P.Y);
        }
        // цель ушла дальше ReplanMoveM — путь заново (раз в ReplanSec); в схватке с ней — не дёргаемся
        void Replan(Mover m)
        {
            if (m.Order == null || m.Order.Kind != OrderKind.Attack || !Alive(m)) return;
            var t = ById(m.Order.TargetId);
            if (t == null || !OnField(t)) { m.Done = true; m.Vs = 0; return; }   // бегущего — преследует
            if (Shooter(m)) { ReplanShoot(m, t); return; }
            var f = FightOf(m, t);
            if (f != null && f.Of(m).Engaged > 0 && !t.Fleeing) return;   // бегущего — догоняет (Г70)
            if (aimed.TryGetValue(m, out var a) && JsMath.Hypot(a.x - t.P.X, a.y - t.P.Y) < ReplanMoveM) return;
            Aim(m, m.Order, t);
        }

        // ── ход ──
        public List<string> Turn(Action<double> frame = null)
        {
            var M = R.Move; double dt = M.Dt;
            int steps = MoveSim.StepsPerTurn(R);
            int contactEvery = Math.Max(1, (int)Math.Round(ContactEverySec / dt)), replanEvery = Math.Max(1, (int)Math.Round(ReplanSec / dt));
            double turnStart = Clock;
            MoveSim.BeginTurn(Movers);
            Details.Clear(); events.Clear();
            foreach (var f in Fights) { f.LossA = f.LossB = 0; f.Notes.Clear(); }
            foreach (var v in Volleys) { v.LossA = v.LossB = v.Friendly = 0; v.Arrows = 0; }
            Shots = new ShotStats();
            var shotThisTurn = new List<Volley>();
            foreach (var m in Movers) chargesLeft[m] = (int)Units.AttackLimit(m.P.U, R);   // Г29: 1 натиск за ход, 2 при дисциплине 80+
            for (int k = 0; k < steps; k++)
            {
                double t = turnStart + k * dt;
                if (k % replanEvery == 0) foreach (var m in Movers) Replan(m);
                var before = Movers.Select(m => (m.P.X, m.P.Y, m.WheelSec, m.Held && m.LastBlockerEnemy)).ToList();
                MoveSim.Step(Movers, Geo, R, k);
                for (int i = 0; i < Movers.Count; i++) RunUp(Movers[i], before[i]);
                foreach (var m in Movers) if (m.Fleeing) EdgeCheck(m, t + dt);
                if (k % contactEvery == 0) { Contacts(t); Envelop(); TryRally(t); foreach (var m in Movers) if (m.Fleeing) OwnFleeCourses(m); }
                Strike(t, dt);
                Shoot(t, dt);
                foreach (var v in Volleys) if (!shotThisTurn.Contains(v)) shotThisTurn.Add(v);
                if ((k + 1) % contactEvery == 0) foreach (var m in Movers) Relayout(m);
                frame?.Invoke((k + 1) * dt);
            }
            EndOfTurnTable(turnStart + M.TurnSec);
            Clock = turnStart + M.TurnSec;
            var L = MoveSim.EndTurn(Movers, R);
            foreach (var f in Fights)
            {
                if (f.LossA == 0 && f.LossB == 0 && f.Notes.Count == 0) continue;
                string since = f.T0 >= turnStart ? $" (с {Js.Num(Js.R1(f.T0 - turnStart))} с)" : "";
                var parts = new List<string> { $"«{f.A.P.U.Name}» −{Js.Num(Js.Round(f.LossA))}", $"«{f.B.P.U.Name}» −{Js.Num(Js.Round(f.LossB))}" };
                parts.AddRange(f.Notes);
                L.Add($"Схватка «{f.A.P.U.Name}» и «{f.B.P.U.Name}»{since}: {string.Join("; ", parts)}");
            }
            foreach (var v in shotThisTurn)
            {
                if (v.Arrows == 0 && v.LossA == 0 && v.LossB == 0) continue;
                var parts = new List<string> { $"стрел {v.Arrows}", $"«{v.B.P.U.Name}» −{Js.Num(v.LossB)}" };
                if (v.LossA > 0) parts.Add($"ответом «{v.A.P.U.Name}» −{Js.Num(v.LossA)}");
                if (v.Friendly > 0) parts.Add($"по своим −{Js.Num(v.Friendly)}");
                L.Add($"Стрельба «{v.A.P.U.Name}» по «{v.B.P.U.Name}»: {string.Join("; ", parts)}");
            }
            L.AddRange(events);
            foreach (var m in Movers)
                if (m.Fleeing && OnField(m)) L.Add($"«{m.P.U.Name}» бежит: прошёл {Js.Num(Js.R1(m.Moved))} м{(m.RallyPending ? ", ждёт, чтобы сплотиться (враг ближе " + Js.Num(R.Rally.FreeM) + " м)" : "")}");
            Fights.RemoveAll(f => f.Over);
            return L;
        }

        // Разбег для натиска (К29): метры по прямой по чистому (без леса, болота, брода, непроходимого).
        // Сбрасывается на повороте колесом и когда встал сам; упёрся во врага — разбег замер (им и бьёт).
        void RunUp(Mover m, (double x, double y, double wheel, bool enemyHeld) before)
        {
            var u = m.P.U;
            double d = JsMath.Hypot(m.P.X - before.x, m.P.Y - before.y);
            if (before.enemyHeld || m.Held && m.LastBlockerEnemy) return;
            if (d <= 1e-9 || m.WheelSec > before.wheel || !ChargeGround(m.P.X, m.P.Y)) { u.RunUpM = 0; return; }
            u.RunUpM += d;
        }
        Rules.TerrainR GroundOf(double x, double y)
        {
            var map = Geo?.Map;
            if (map == null) return null;
            var c = Terrain.CellAt(map, x / Geo.W, y / Geo.H);
            return c.T != 0 && Terrain.ById.TryGetValue(c.T, out var t) && R.Map.Terrain.TryGetValue(t.Key, out var tr) ? tr : null;
        }
        bool ChargeGround(double x, double y) { var tr = GroundOf(x, y); return tr == null || !tr.NoCharge && tr.Move != null; }
        int LevelOf(Mover m) => Geo?.Map == null ? 0 : Terrain.CellAt(Geo.Map, m.P.X / Geo.W, m.P.Y / Geo.H).Z;
        // режим боя — по местности под тем, по кому бьют (К24)
        string ModeAt(Mover def) => GroundOf(def.P.X, def.P.Y)?.Mode == "rough" ? Modes.MeleeRough : Modes.MeleeForm;

        // ── касание (Г27): колонна бьёт, если хоть одна её фигурка не дальше MeleeGap от фигурки врага ──
        readonly Dictionary<Mover, (List<int> figs, Mover foe)> frontFigs = new Dictionary<Mover, (List<int> figs, Mover foe)>();
        // Касания этого полушага: x → y, по фигуркам x; и чья колонна кому досталась (Г69)
        readonly Dictionary<(Mover x, Mover y), (int ky, double d)[]> touches = new Dictionary<(Mover x, Mover y), (int ky, double d)[]>();
        readonly Dictionary<Mover, Dictionary<int, (Mover foe, double d)>> colFoe = new Dictionary<Mover, Dictionary<int, (Mover foe, double d)>>();
        Fight.Side SideOf(Mover x, Mover y)
        {
            if (!touches.TryGetValue((x, y), out var touch)) return new Fight.Side();
            var mine = colFoe.TryGetValue(x, out var cf) ? cf : new Dictionary<int, (Mover foe, double d)>();
            var touching = new List<int>();
            for (int k = 0; k < touch.Length; k++) if (touch[k].ky >= 0) touching.Add(k);
            if (touching.Count > 0) frontFigs[x] = (touching, y);
            double total = 0;
            var colMen = new Dictionary<int, double>();
            var colSector = new Dictionary<int, (double d, string s)>();
            for (int k = 0; k < x.P.Figs.Count; k++)
            {
                var f = x.P.Figs[k];
                total += f.Men;
                colMen[f.File] = (colMen.TryGetValue(f.File, out var c) ? c : 0) + f.Men;
                if (touch[k].ky < 0) continue;
                if (!mine.TryGetValue(f.File, out var own) || own.foe != y) continue;   // колонна бьёт ближайшего врага (Г69)
                // сектор — по тому, где фигурка стоит относительно строя врага: прямо перед ним — фронт, прямо за — тыл
                y.P.ToLocal(x.Figs[k].X, x.Figs[k].Y, out var lx, out var ly);
                double ex = Math.Abs(lx) - y.P.Fp.Front / 2;
                string sec = ex <= 0 ? (ly < 0 ? "front" : "rear") : "flank";
                if (!colSector.TryGetValue(f.File, out var cs) || touch[k].d < cs.d) colSector[f.File] = (touch[k].d, sec);
            }
            double eng = 0, front = 0, flank = 0, rear = 0;
            foreach (var kv in colSector)
            {
                double men = colMen[kv.Key];
                eng += men;
                if (kv.Value.s == "front") front += men; else if (kv.Value.s == "flank") flank += men; else rear += men;
            }
            return eng <= 0 || total <= 0 ? new Fight.Side()
                : new Fight.Side { Engaged = eng / total, Front = front / eng, Flank = flank / eng, Rear = rear / eng };
        }

        void Contacts(double t)
        {
            frontFigs.Clear(); touches.Clear(); colFoe.Clear();
            foreach (var m in Movers) foreach (var s in m.Figs) s.Fighting = false;
            // 1) касания фигурок у всех пар врагов поблизости
            for (int i = 0; i < Movers.Count; i++)
                for (int j = i + 1; j < Movers.Count; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    if (!(OnField(x) && OnField(y) && (Alive(x) || Alive(y)) && Enemies(x.P.U, y.P.U))) continue;
                    double reach = (JsMath.Hypot(x.P.Fp.Front, x.P.Fp.Depth) + JsMath.Hypot(y.P.Fp.Front, y.P.Fp.Depth)) / 2 + R.Map.MeleeGap + 10 + WrapReach(x) + WrapReach(y);
                    if (JsMath.Hypot(x.P.X - y.P.X, x.P.Y - y.P.Y) > reach) continue;
                    touches[(x, y)] = Bodies.Touch(x, y, R.Map.MeleeGap);
                    touches[(y, x)] = Bodies.Touch(y, x, R.Map.MeleeGap);
                }
            // 2) колонна — тому врагу, которого касается ближе всех (Г69): сила отряда делится, а не растёт
            foreach (var kv in touches)
            {
                var (x, y) = kv.Key;
                if (Alive(x))
                    for (int k = 0; k < kv.Value.Length && k < x.Figs.Count; k++)
                    {
                        int ky = kv.Value[k].ky;
                        if (ky < 0 || ky >= y.Figs.Count) continue;
                        var s = x.Figs[k]; var q = y.Figs[ky];
                        double dx = q.X - s.X, dy = q.Y - s.Y, d = JsMath.Hypot(dx, dy);
                        if (d < 1e-9) continue;
                        s.Fighting = true; s.FightX = dx / d; s.FightY = dy / d; s.FoeX = q.X; s.FoeY = q.Y;   // для выпадов передних бойцов (Г78)
                    }
                if (!colFoe.TryGetValue(x, out var cf)) colFoe[x] = cf = new Dictionary<int, (Mover foe, double d)>();
                for (int k = 0; k < kv.Value.Length; k++)
                {
                    if (kv.Value[k].ky < 0) continue;
                    int file = x.P.Figs[k].File;
                    if (!cf.TryGetValue(file, out var o) || kv.Value[k].d < o.d) cf[file] = (y, kv.Value[k].d);
                }
            }
            // 3) схватки пар
            for (int i = 0; i < Movers.Count; i++)
                for (int j = i + 1; j < Movers.Count; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    var f = FightOf(x, y);
                    bool can = OnField(x) && OnField(y) && (Alive(x) || Alive(y)) && Enemies(x.P.U, y.P.U);
                    var sx = can ? SideOf(x, y) : new Fight.Side();
                    var sy = can ? SideOf(y, x) : new Fight.Side();
                    bool touch = sx.Engaged > 0 || sy.Engaged > 0;
                    if (touch)
                    {
                        if (f == null) Start(x, y, sx, sy, t);
                        else { if (f.A == x) { f.SA = sx; f.SB = sy; } else { f.SA = sy; f.SB = sx; } f.LastTouch = t; f.Touching = true; }
                    }
                    else if (f != null)
                    {
                        // касание пропало: удары не текут, но как касались — помним (ответ на удар не теряется
                        // из-за того, что фигурки на миг разошлись); разошлись надолго — схватка кончилась
                        f.Touching = false;
                        if (!can || t - f.LastTouch > FightEndSec) End(f);
                    }
                }
        }

        // Схватка началась (Г44): начинает атакующий; атакуют оба или никто — выше дисциплина, поровну — бросок
        void Start(Mover x, Mover y, Fight.Side sx, Fight.Side sy, double t)
        {
            bool ax = Attacking(x), ay = Attacking(y);
            Mover A = ax != ay ? (ax ? x : y)
                    : x.P.U.Discipline != y.P.U.Discipline ? (x.P.U.Discipline > y.P.U.Discipline ? x : y)
                    : (Ctx.Rng() < 0.5 ? x : y);
            Mover B = A == x ? y : x;
            var f = new Fight { A = A, B = B, T0 = t, LastTouch = t, Touching = true, SA = A == x ? sx : sy, SB = A == x ? sy : sx };
            // натиск (Г29, К29)
            bool burst = false;
            if (A.Order != null && A.Order.Charge && Units.IsCav(A.P.U))
            {
                string why = null;
                var gB = GroundOf(B.P.X, B.P.Y);
                if (chargesLeft.TryGetValue(A, out var left) && left <= 0) why = "натиск в этом ходу уже был";
                else if (BattleMap.RunUpBlock(A.P.U, R) is string run) why = run;
                else if (gB != null && gB.NoCharge) why = "под целью местность без натиска";
                else if (Units.IsPike(B.P.U) && f.SA.Front > 0) why = "пики во фронт гасят натиск";
                if (why == null) { burst = true; chargesLeft[A] = left - 1; f.Notes.Add($"натиск «{A.P.U.Name}»"); }
                else f.Notes.Add($"«{A.P.U.Name}» без натиска: {why}");
            }
            if (burst) f.Wins.Add(new Fight.Win { Att = A, Def = B, Charge = true, T0 = t, T1 = t + BurstSec });
            Schedule(f, burst ? t + BurstSec : t, burst ? 1 : 0, R.Move.TurnSec - (burst ? BurstSec : 0));
            Fights.Add(f);
        }

        // Круг обменов (Г62): атаки A, потом атаки B, за len секунд; ответы — на круг, как за ход стола
        void Schedule(Fight f, double start, int burstUsed, double len)
        {
            var ex = new List<(Mover x, Mover y)>();
            int atkA = (int)Units.AttackLimit(f.A.P.U, R) - burstUsed, atkB = (int)Units.AttackLimit(f.B.P.U, R);
            for (int i = 0; i < atkA; i++) ex.Add((f.A, f.B));
            for (int i = 0; i < atkB; i++) ex.Add((f.B, f.A));
            double w = len / Math.Max(1, ex.Count);
            for (int j = 0; j < ex.Count; j++) f.Wins.Add(new Fight.Win { Att = ex[j].x, Def = ex[j].y, T0 = start + j * w, T1 = start + (j + 1) * w });
            f.CycleEnd = start + len;
            Budget(f.A, start); Budget(f.B, start);
        }
        // Ответные удары — общий запас отряда на все его схватки и перестрелки за круг в 15 с (Г69), как за ход стола
        readonly Dictionary<Mover, (int left, double since)> counters = new Dictionary<Mover, (int left, double since)>();
        internal void Budget(Mover m, double t)
        {
            if (!counters.TryGetValue(m, out var c) || t - c.since >= R.Move.TurnSec - 1e-6) counters[m] = ((int)Units.CounterLimit(m.P.U, R), t);
        }
        internal bool TakeCounter(Mover m)
        {
            if (!counters.TryGetValue(m, out var c) || c.left <= 0) return false;
            counters[m] = (c.left - 1, c.since);
            return true;
        }

        void End(Fight f)
        {
            f.Over = true;
            foreach (var w in f.Wins) if (w.Open && !w.Closed) Close(f, w, Clock);
        }

        // ── удары: урон течёт каждый шаг, в конце окна — потери и БД, как после удара за столом ──
        void Strike(double t, double dt)
        {
            double t1 = t + dt;
            foreach (var f in Fights)
            {
                if (f.Over) continue;
                // круг кончился, а схватка идёт — следующий круг (Г62: бой идёт без перерыва)
                if (t1 > f.CycleEnd + 1e-9 && t - f.LastTouch <= FightEndSec) Schedule(f, f.CycleEnd, 0, R.Move.TurnSec);
                // открываем окна; на удар во фронт — ответ (удар во фланг и тыл глушит ответ, Г17)
                foreach (var w in f.Wins.ToList())
                {
                    if (w.Open || w.Skipped || w.T0 >= t1 - 1e-9) continue;
                    if (w.Att.P.U.Status != "active" || w.Def.P.U.Status == "destroyed" || w.Def.P.U.Soldiers <= 0) { w.Skipped = true; continue; }
                    if (w.Counter && w.Att.P.U.Morale < R.Morale.ShakenBelow) { w.Skipped = true; continue; }   // дрогнувший не отвечает
                    w.Open = true; w.Att.P.U.Acted = true;   // «походил» — для усталости в конце хода (стол)
                    w.U = MeleeSim.Fortune(w.Att.P.U, ModeAt(w.Def), Ctx);
                    w.N0 = w.Att.P.U.Soldiers;
                    if (!w.Counter && !w.Charge)
                    {
                        var sa = f.Of(w.Att);
                        if (w.Def.P.U.Status == "active" && f.Of(w.Def).Engaged > 0 && sa.Front > 0 && TakeCounter(w.Def))
                        {
                            bool pikeStop = Units.IsCav(w.Att.P.U) && Units.IsPike(w.Def.P.U);
                            var c = new Fight.Win
                            {
                                Att = w.Def, Def = w.Att, Counter = true, Factor = sa.Front, T0 = w.T0, T1 = w.T1,
                                Extra = pikeStop ? R.PikeCounterMult : 1, Open = true, N0 = w.Def.P.U.Soldiers,
                            };
                            c.U = MeleeSim.Fortune(c.Att.P.U, ModeAt(c.Def), Ctx);
                            f.Wins.Add(c);
                        }
                    }
                }
                // урон за шаг — от состояния на начало шага, обе стороны одновременно
                f.A.P.Level = LevelOf(f.A); f.B.P.Level = LevelOf(f.B);
                double toA = 0, toB = 0;
                var inc = new double[f.Wins.Count];
                for (int i = 0; i < f.Wins.Count; i++)
                {
                    var w = f.Wins[i];
                    if (!w.Open || w.Closed) continue;
                    double portion = Math.Max(0, Math.Min(w.T1, t1) - Math.Max(w.T0, t)) / (w.T1 - w.T0);
                    if (portion <= 0 || w.Att.P.U.Soldiers <= 0) continue;
                    var sa = f.Of(w.Att);
                    if (!f.Touching || sa.Engaged <= 0) continue;   // разошлись — не бьют
                    string mode = ModeAt(w.Def);
                    var At = Combat.Eff(w.Att.P.U, mode, null, Ctx); var Df = Combat.Eff(w.Def.P.U, mode, null, Ctx);
                    var opts = new BattleRequest { Mode = mode, FatigueMode = "percent" };
                    var H = R.Map.Height;
                    StrikeMod mod = w.Att.P.Level > w.Def.P.Level ? new StrikeMod { Mult = H.DownhillMelee }
                                  : w.Att.P.Level < w.Def.P.Level ? new StrikeMod { Mult = H.UphillMelee } : null;
                    double roll = w.U * w.N0 * sa.Engaged, dmg = 0;
                    if (w.Counter) dmg = Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, false, w.Extra, "", "front", mod, roll * w.Factor);
                    else
                    {
                        if (sa.Front > 0) dmg += sa.Front * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "front", mod, roll);
                        if (sa.Flank > 0) dmg += sa.Flank * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "flank", mod, roll);
                        if (sa.Rear > 0) dmg += sa.Rear * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "rear", mod, roll);
                    }
                    inc[i] = Math.Max(0, dmg) * portion;
                    if (w.Def == f.A) toA += inc[i]; else toB += inc[i];
                }
                double kA = toA > f.A.P.U.Soldiers && toA > 0 ? f.A.P.U.Soldiers / toA : 1;
                double kB = toB > f.B.P.U.Soldiers && toB > 0 ? f.B.P.U.Soldiers / toB : 1;
                for (int i = 0; i < inc.Length; i++)
                {
                    if (inc[i] <= 0) continue;
                    var w = f.Wins[i];
                    double take = inc[i] * (w.Def == f.A ? kA : kB);
                    w.Acc += take; w.Def.P.U.Soldiers -= take;
                    if (w.Def == f.A) f.LossA += take; else f.LossB += take;
                }
                foreach (var w in f.Wins) if (w.Open && !w.Closed && w.T1 <= t1 + 1e-9) Close(f, w, t1);
                f.Wins.RemoveAll(w => w.Closed || w.Skipped);
            }
        }

        // Конец окна: округление до солдат, летальность и штраф БД — как после удара за столом
        void Close(Fight f, Fight.Win w, double t)
        {
            w.Closed = true;
            var def = w.Def.P.U;
            double before = def.Soldiers + w.Acc;
            double cas = Math.Min(Js.Round(w.Acc), Math.Floor(before + 1e-9));
            var clone = def.Clone(); clone.Soldiers = before;
            var res = Combat.Casualties(clone, cas, null, Ctx);
            var patch = Combat.CasualtyPatch(clone, res, new List<string>(), Ctx, out _);
            patch.ApplyTo(def);
            double whole = Math.Round(def.Soldiers);
            if (Math.Abs(def.Soldiers - whole) < 1e-6) def.Soldiers = whole;
            Details.Add($"{Js.Num(Js.R1(t - (Clock)))} с · {w.Att.P.U.Name} → {def.Name}{(w.Charge ? " (натиск)" : w.Counter ? " (ответ)" : "")}: −{Js.Num(cas)}");
            if (cas > 0) AfterLoss(w.Def, t);
        }

        // ── БД и бегство в ходу (БД4, Г70–Г74) — ЧЕРНОВИК ДО ГМа ──
        string At(double t) => Js.Num(Js.R1(t - Clock));
        // Г74: после каждого удара с потерями — проверки, как подсказывает журнал стола: БД ≤ 40 — проверка БД
        // (провал роняет БД до нуля); БД на нуле — проверка на побег («стоять насмерть» гасит первый бросок)
        internal void AfterLoss(Mover m, double t)
        {
            var u = m.P.U;
            if (u.Soldiers <= 0) { m.Fleeing = false; m.RallyPending = false; return; }
            if (!MoraleChecks || u.Status != "active" || u.Soldiers <= 0 || m.Gone) return;
            if (u.Morale > 0 && u.Morale <= R.Morale.CheckAt) Check(t, MoraleRules.MoraleCheck(u, Ctx), u);
            if (u.Morale > 0 || u.Status != "active") return;
            Check(t, MoraleRules.FleeCheck(u, Ctx), u);
            if (u.Status == "fled") { Flee(m, t, "провалил проверку на побег"); PanicFrom(m, t); }
        }
        void Check(double t, ActionResult r, Unit u)
        {
            r.Patch.ApplyTo(u);
            events.Add($"{At(t)} с · {r.Title}: {string.Join("; ", r.Lines.Where(l => !l.Contains("требуется")))}");
        }

        // Г70, Г71: побежал — толпой прочь от врага (от тех, кто ближе FleeLookM: чем ближе, тем сильнее; никого —
        // назад от своего фронта) к краю карты, на норме; строй рассыпается, приказов не слушает, свои пропускают
        void Flee(Mover m, double t, string why)
        {
            if (m.Fleeing || m.Gone) return;
            var u = m.P.U; u.Status = "fled";
            double vx = 0, vy = 0;
            foreach (var e in Movers)
            {
                if (e == m || !OnField(e) || !Enemies(u, e.P.U)) continue;
                double dx = m.P.X - e.P.X, dy = m.P.Y - e.P.Y, d = JsMath.Hypot(dx, dy);
                if (d > R.Move.FleeLookM || d < 1e-9) continue;
                double w = 1 / Math.Max(d, 10);
                vx += dx / d * w; vy += dy / d * w;
            }
            double head = JsMath.Hypot(vx, vy) > 1e-12 ? MoveSim.HeadingOf(vx, vy) : MoveSim.Norm(m.P.Facing + 180);
            double hx = Math.Sin(head * Math.PI / 180), hy = -Math.Cos(head * Math.PI / 180), inset = 1, run = 1e9;
            if (hx > 1e-9) run = Math.Min(run, (Geo.W - inset - m.P.X) / hx); else if (hx < -1e-9) run = Math.Min(run, (inset - m.P.X) / hx);
            if (hy > 1e-9) run = Math.Min(run, (Geo.H - inset - m.P.Y) / hy); else if (hy < -1e-9) run = Math.Min(run, (inset - m.P.Y) / hy);
            run = Math.Max(0, run);
            m.FleeX = Math.Max(inset, Math.Min(Geo.W - inset, m.P.X + hx * run));
            m.FleeY = Math.Max(inset, Math.Min(Geo.H - inset, m.P.Y + hy * run));
            m.FleeHeading = head; m.Fleeing = true; m.FleeSince = t; m.RallyPending = false; m.Rallied = false;
            m.Order = null; m.Track = null; m.Done = false; m.Held = false; m.HoldLeft = 0; m.Vs = 0; aimed.Remove(m);
            m.Field = FlowField.Build(Geo, R, BattleMap.IsHorse(u), m.FleeX, m.FleeY);
            foreach (var s in m.Figs)
            {
                if (!s.Turned) { s.Turned = true; s.Axis = m.P.Facing; }
                s.Wrap = false; s.WFoe = null; s.Returning = false; s.FleeH = double.NaN;
            }
            m.P.Facing = head;
            OwnFleeCourses(m);
            events.Add($"{At(t)} с · 🏃 «{u.Name}» бежит ({why}) — толпой прочь от врага, к краю карты");
        }

        // Г73: каскадная паника — волна целиком в миг бегства, как за столом (Panic.Wave трекера): свои и союзники
        // в 150 м от края до края, первое кольцо — только кто видел; побежавший тянет соседей; каждый — раз за волну
        void PanicFrom(Mover src, double t)
        {
            var live = Movers.Where(m => m == src || OnField(m)).ToList();
            foreach (var m in live) { var u = m.P.U; u.OnMap = true; u.MapX = m.P.X / Geo.W * 100; u.MapY = m.P.Y / Geo.H * 100; u.Facing = m.P.Facing; }
            var r = Panic.Wave(live.Select(m => m.P.U).ToList(), src.P.U.Id, Geo, Ctx, PanicMoraleLoss);
            foreach (var p in r.Patches) { var m = ById(p.Id); if (m != null) p.Patch.ApplyTo(m.P.U); }
            if (r.Lines.Count > 0) events.Add($"{At(t)} с · 🏳 каскадная паника от «{src.P.U.Name}»: {string.Join("; ", r.Lines)}");
            foreach (int id in r.Fled) { var m = ById(id); if (m != null) Flee(m, t, $"паника — бежал «{src.P.U.Name}»"); }
        }

        // Г70: толпа дошла до края карты — отряд ушёл с поля боя (жив, но в этой битве его нет; вернуть — ГМ после боя)
        void EdgeCheck(Mover m, double t)
        {
            if (!m.Fleeing || m.Gone) return;
            double e = R.Move.FleeEdgeM; int left = 0;
            for (int k = m.Figs.Count - 1; k >= 0; k--)
            {
                var s = m.Figs[k];
                if (!(s.X < e || s.Y < e || s.X > Geo.W - e || s.Y > Geo.H - e)) continue;
                m.LeftMen += (int)m.P.Figs[k].Men; m.LaidMen -= (int)m.P.Figs[k].Men; left++;
                m.Men.RemoveAll(x => x.Fig == s); m.MenVersion++;
                m.Figs.RemoveAt(k); m.P.Figs.RemoveAt(k);
            }
            if (m.Figs.Count > 0) return;
            if (left == 0) { m.Fleeing = false; m.RallyPending = false; return; }   // никто не ушёл за край — вымерли на бегу
            m.Gone = true; m.Fleeing = false; m.RallyPending = false; m.Field = null; m.Vs = 0;
            events.Add($"{At(t)} с · «{m.P.U.Name}» бежал с поля боя ({Js.Num(Js.Round(m.P.U.Soldiers))} бойцов)");
        }
        // Потери бегущей толпы: строй заново не собирается — падают фигурки, что ближе всех к врагу (их и рубят)
        void RelayoutCrowd(Mover m)
        {
            int n = (int)Math.Max(0, Js.Round(m.P.U.Soldiers)) - m.LeftMen;
            if (n >= m.LaidMen) return;
            int melee = Math.Max(0, m.LaidMen - n - m.ShotDown);
            m.ShotDown = 0;
            if (melee > 0) MeleeDeaths(m, melee);
            m.LaidMen = n;
            int keep = n <= 0 ? 0 : (int)Math.Ceiling(n / MenPerFigure);
            var foes = Movers.Where(e => e != m && OnField(e) && Enemies(m.P.U, e.P.U)).ToList();
            double Danger(FigState s) => foes.Count == 0 ? 0 : -foes.Min(e => JsMath.Hypot(e.P.X - s.X, e.P.Y - s.Y));
            while (m.Figs.Count > keep)
            {
                int k = Enumerable.Range(0, m.Figs.Count).OrderByDescending(i => Danger(m.Figs[i])).ThenBy(i => m.Figs[i].Id).First();
                m.Fallen.Add((m.Figs[k].X, m.Figs[k].Y));
                m.Figs.RemoveAt(k); m.P.Figs.RemoveAt(k);
            }
            Soldiers.Assign(m, R);
        }

        // Окружённые (Г70): фигурка, которой общий путь толпы перекрывает враг (ближе FleeBlockM, в пределах
        // ±FleeBlockDeg от курса), обходит его сбоку — к тому краю вражеского строя, что ближе к ней (курс толпы ± 90°);
        // путь чист — снова в общем потоке. Раз в ContactEverySec
        void OwnFleeCourses(Mover m)
        {
            var M = R.Move;
            double hx = Math.Sin(m.FleeHeading * Math.PI / 180), hy = -Math.Cos(m.FleeHeading * Math.PI / 180);
            foreach (var s in m.Figs)
            {
                Mover by = null; double wd = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                    if (JsMath.Hypot(e.P.X - s.X, e.P.Y - s.Y) > M.FleeBlockM + e.P.Fp.Front) continue;
                    foreach (var q in e.Figs)
                    {
                        double d = JsMath.Hypot(q.X - s.X, q.Y - s.Y);
                        if (d > M.FleeBlockM || d >= wd) continue;
                        if (Math.Abs(MoveSim.AngleDiff(m.FleeHeading, MoveSim.HeadingOf(q.X - s.X, q.Y - s.Y))) > M.FleeBlockDeg) continue;
                        wd = d; by = e;
                    }
                }
                if (by == null) { s.FleeH = double.NaN; continue; }
                if (!double.IsNaN(s.FleeH)) continue;   // уже обходит — в ту же сторону, не мечется
                double side = (s.X - by.P.X) * hy - (s.Y - by.P.Y) * hx;   // справа (+) или слева (−) от середины врага по ходу бегства
                s.FleeH = MoveSim.Norm(m.FleeHeading + (side >= 0 ? -90 : 90));
                if (!s.Turned) { s.Turned = true; s.Axis = m.P.Facing; }   // тело разворачивается к своему курсу постепенно
            }
        }

        // Г72: «сплотить» — бегущий с этим приказом, у которого врага нет ближе Rally.FreeM (от края до края), бросает
        // d100 ≤ дисциплина, один раз на приказ: успех — БД не ниже Rally.Morale, снова в строю лицом к ближайшему врагу
        // (как «воспрял духом» за столом); провал — бежит дальше. Враг рядом — приказ ждёт
        void TryRally(double t)
        {
            foreach (var m in Movers)
            {
                if (!m.Fleeing || m.Gone || !m.RallyPending || m.Figs.Count == 0) continue;
                Mover near = null; double gap = double.MaxValue, far = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !Alive(e) || !Enemies(m.P.U, e.P.U)) continue;
                    double g = Bodies.MinGap(m, e);
                    if (g < gap) gap = g;
                    double c = JsMath.Hypot(e.P.X - m.P.X, e.P.Y - m.P.Y);
                    if (c < far) { far = c; near = e; }
                }
                if (gap <= R.Rally.FreeM) continue;
                m.RallyPending = false;
                var u = m.P.U; double roll = Dice.Roll(Ctx.Rng, 100);
                if (roll > u.Discipline) { events.Add($"{At(t)} с · «{u.Name}» сплотить не вышло: d100 = {Js.Num(roll)} > {Js.Num(u.Discipline)} (дисциплина) — бежит дальше"); continue; }
                u.Status = "active"; u.Broken = false; u.BreakGrace = 0; u.Morale = Math.Max(u.Morale, R.Rally.Morale);
                m.Fleeing = false; m.Rallied = true;
                if (m.LeftMen > 0) { events.Add($"{At(t)} с · «{u.Name}»: {m.LeftMen} бойцов ушли за край карты — в этой битве их нет"); u.Soldiers -= m.LeftMen; m.LeftMen = 0; }
                m.LaidMen = -1; Relayout(m);
                Reform(m, near != null ? MoveSim.HeadingOf(near.P.X - m.P.X, near.P.Y - m.P.Y) : MoveSim.Norm(m.FleeHeading + 180));
                events.Add($"{At(t)} с · ✦ «{u.Name}» сплотили: d100 = {Js.Num(roll)} ≤ {Js.Num(u.Discipline)} — БД {Js.Num(u.Morale)}, снова в строю");
            }
        }
        // Сплотились: строй заново там, где основная толпа (отбившиеся своим курсом — подходят), лицом к face;
        // места — ближайшим фигуркам спереди назад
        void Reform(Mover m, double face)
        {
            var P = m.P;
            if (m.Cols != m.NominalCols && m.NominalCols > 0) MoveSim.SetCols(m, m.NominalCols);
            var main = m.Figs.Where(s => double.IsNaN(s.FleeH)).ToList();
            if (main.Count == 0) main = m.Figs;
            P.X = main.Average(s => s.X); P.Y = main.Average(s => s.Y);
            foreach (var s in m.Figs) s.FleeH = double.NaN;
            foreach (var s in m.Figs) if (!s.Turned) { s.Turned = true; s.Axis = P.Facing; }
            P.Facing = MoveSim.Norm(face);
            var free = Enumerable.Range(0, m.Figs.Count).ToList();
            var bodies = new FigState[P.Figs.Count];
            foreach (int k in Enumerable.Range(0, P.Figs.Count).OrderBy(i => P.Figs[i].Y).ThenBy(i => P.Figs[i].X))
            {
                P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var wx, out var wy);
                int best = -1; double bd = double.MaxValue;
                for (int q = 0; q < free.Count; q++)
                {
                    var s = m.Figs[free[q]];
                    double d = (s.X - wx) * (s.X - wx) + (s.Y - wy) * (s.Y - wy);
                    if (d < bd) { bd = d; best = q; }
                }
                if (best >= 0) { bodies[k] = m.Figs[free[best]]; free.RemoveAt(best); }
                else bodies[k] = new FigState { Id = m.NextFigId++, X = wx, Y = wy };
            }
            m.Figs = bodies.ToList();
            // отбившиеся подходят к строю в обход врага: карта направлений к месту сбора, клетки под вражескими строями закрыты
            var F0 = m.Field;
            m.Field = F0 == null ? null : FlowField.Build(Geo, R, BattleMap.IsHorse(P.U), P.X, P.Y, 0, EnemyCells(m, F0));
            m.Track = null; m.Vs = 0;
            m.Order = new MoveOrder { Kind = OrderKind.Hold, X = P.X, Y = P.Y, Facing = P.Facing }; m.Done = true;
        }
        // Клетки под строями врагов (с запасом в полклетки) — для пути в обход них
        bool[] EnemyCells(Mover m, FlowField F)
        {
            var extra = new bool[F.W * F.H];
            foreach (var e in Movers)
            {
                if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                var B = e.P;
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
            }
            return extra;
        }

        // Конец хода стола (turn.js): усталость у тех, кто бился 4 хода подряд (элита — 8); сломленный (БД 0) теряет
        // дисциплину, иссякла — бежит без броска (и тянет соседей паникой)
        void EndOfTurnTable(double t)
        {
            var units = Movers.Select(m => m.P.U).ToList();
            var res = BattleCore.Turn.EndTurn(units, Ctx);
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i], n = res.Units[i];
                u.AttacksMade = n.AttacksMade; u.CountersMade = n.CountersMade; u.Acted = n.Acted; u.TurnsActive = n.TurnsActive; u.Fatigue = n.Fatigue;
                u.BreakPenalty = n.BreakPenalty; u.BreakGrace = n.BreakGrace; u.Discipline = n.Discipline;
                if (n.Status == "fled" && u.Status == "active")
                {
                    u.Status = "fled";
                    if (MoraleChecks) { Flee(Movers[i], t, "дисциплина иссякла"); PanicFrom(Movers[i], t); }
                }
            }
            foreach (var l in res.Lines) events.Add($"конец хода · {l}");
        }

        // Павшие в рукопашной (Г67): точного места нет — падают у переднего края схватки, в случайной точке
        // касающихся фигурок, лицом к врагу, кровь брызжет назад (от удара). Случайность — своя, только для рисунка.
        void MeleeDeaths(Mover m, int n)
        {
            // Г78: падают те, кто у врага — бойцы касающихся фигурок, ближние к фигурке врага (по тому, где стоят, а не по
            // месту в строю: пересаженный вперёд ещё идёт), чуть вперемешку (хешем); никто не касается — ближайшие к врагу
            var alive = m.Men.Where(x => x.Alive && x.Fig != null).ToList();
            if (alive.Count == 0) return;
            Mover foe = frontFigs.TryGetValue(m, out var fr) ? fr.foe : null;
            if (foe == null)
            {
                double best = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                    double d = JsMath.Hypot(e.P.X - m.P.X, e.P.Y - m.P.Y);
                    if (d < best) { best = d; foe = e; }
                }
            }
            double Score(Man x)
            {
                double jit = MoveSim.Hash01(m.P.U.Id, x.Id, 15) * 1.5;
                if (x.Fig.Fighting) return JsMath.Hypot(x.X - x.Fig.FoeX, x.Y - x.Fig.FoeY) + jit;
                return 1000 + (foe == null ? 0 : JsMath.Hypot(x.X - foe.P.X, x.Y - foe.P.Y)) + jit;
            }
            foreach (var x in alive.OrderBy(Score).ThenBy(x => x.Id).Take(n))
            {
                x.Alive = false;
                double dir = foe != null ? Math.Atan2(x.Y - foe.P.Y, x.X - foe.P.X) * 180 / Math.PI : (x.Facing + 90);   // от врага — за спину
                double u = look();
                Deaths.Add(new Death
                {
                    X = x.X, Y = x.Y, T = curT, Facing = x.Facing + (look() - 0.5) * 60, Dir = dir + (look() - 0.5) * 50,
                    UnitId = m.P.U.Id, ManId = x.Id, Part = u < 0.25 ? "head" : u < 0.8 ? "torso" : "legs", Killed = true,
                });
            }
        }

        // ── охват (Г68): свободные колонны огибают врага — на свободные места по его обводу: фланги, тыл, фронт ──
        // Колонна идёт целиком: голова — к врагу, остальные — за ней наружу; кто из фигурок колонны ближе к месту,
        // та и голова. Угол огибают через точки снаружи, а не сквозь врага. Место за колонной держится, пока она
        // к нему идёт и пока бьётся; мест не хватило — встаёт второй линией за своим. Кому охватывать нечего —
        // идут на свои места в строю.
        double WrapReach(Mover m) => m.P.Fp.Front;
        void Envelop()
        {
            var was = new HashSet<FigState>(Movers.SelectMany(m => m.Figs).Where(s => s.Wrap));
            // колонны каждого отряда в схватке: обернувшиеся, что уже бьются, держат место у врага (kept),
            // свободные (не бьются) — к ближайшему из врагов (free)
            var plan = new Dictionary<(Mover x, Mover y), (List<List<int>> kept, List<List<int>> free)>();
            (List<List<int>> kept, List<List<int>> free) Plan(Mover x, Mover y)
            {
                if (!plan.TryGetValue((x, y), out var p)) plan[(x, y)] = p = (new List<List<int>>(), new List<List<int>>());
                return p;
            }
            foreach (var x in Movers)
            {
                var foes = Fights.Where(f => !f.Over && f.Touching && (f.A == x || f.B == x)).Select(f => f.Other(x)).ToList();
                if (foes.Count == 0 || x.P.Figs.Count == 0 || !Alive(x))   // бегущий никого не охватывает (Г70)
                {
                    foreach (var s in x.Figs) { s.Wrap = false; s.WFoe = null; }
                    continue;
                }
                var engaged = colFoe.TryGetValue(x, out var cf) ? cf : new Dictionary<int, (Mover foe, double d)>();
                // огибают только того, кто перед фронтом: атакованный во фланг или в тыл сам не поворачивается (Г63)
                var ahead = foes.Where(q => Alive(q) && Math.Abs(MoveSim.AngleDiff(x.P.Facing, MoveSim.HeadingOf(q.P.X - x.P.X, q.P.Y - x.P.Y))) <= R.Sectors.FrontMax).ToList();
                foreach (var g in Enumerable.Range(0, x.P.Figs.Count).GroupBy(k => x.P.Figs[k].File))
                {
                    var col = g.ToList();
                    var head = x.Figs[col[0]];
                    if (engaged.ContainsKey(g.Key))
                    {
                        if (col.Any(k => x.Figs[k].Wrap) && head.WFoe != null && foes.Contains(head.WFoe))
                        {
                            // враг побежал — колонна стоит, где стояла, и держит кольцо, пока касается (окружённым не уйти)
                            if (Alive(head.WFoe)) Plan(x, head.WFoe).kept.Add(col);
                            continue;
                        }
                        foreach (int k in col) { x.Figs[k].Wrap = false; x.Figs[k].WFoe = null; }
                        continue;
                    }
                    foreach (int k in col) x.Figs[k].Wrap = false;
                    if (ahead.Count == 0) { foreach (int k in col) x.Figs[k].WFoe = null; continue; }
                    var y = ahead.OrderBy(q => JsMath.Hypot(q.P.X - head.X, q.P.Y - head.Y)).First();
                    Plan(x, y).free.Add(col);
                }
            }
            foreach (var kv in plan) WrapAround(kv.Key.x, kv.Key.y, kv.Value.kept, kv.Value.free);
            // отпущенные из охвата идут на свои места сквозь свой строй (Returning), пока не дойдут
            foreach (var m in Movers)
                for (int k = 0; k < m.Figs.Count; k++)
                {
                    var s = m.Figs[k];
                    if (s.Wrap) { s.Returning = false; continue; }
                    if (was.Contains(s)) s.Returning = true;
                    if (!s.Returning) continue;
                    m.P.ToWorld(m.P.Figs[k].X, m.P.Figs[k].Y, out var sx, out var sy);
                    if (JsMath.Hypot(s.X - sx, s.Y - sy) < ReturnedM) s.Returning = false;
                }
        }
        const double ReturnedM = 1;   // дошла до своего места в строю — снова твёрдая для своих

        // Места по всему обводу врага, снаружи, лицом к нему: обернувшиеся, что бьются, — при своих; кто шёл —
        // к своему (или соседнему, если строй врага сузился); остальные — по близости к врагу, ближайшее к голове
        void WrapAround(Mover x, Mover y, List<List<int>> kept, List<List<int>> cols)
        {
            var P = x.P; var Q = y.P;
            double figW = P.Figs.Max(q => q.Width), figD = P.Figs.Max(q => q.Depth), mg = 0.5;
            double F = Q.Fp.Front / 2, D = Q.Fp.Depth / 2, off = figD / 2 + mg;
            var slots = new List<(double lx, double ly, double nx, double ny, double face)>();
            void Edge(bool alongX, double fixedV, double half, double nx, double ny, double face)
            {
                if (2 * half <= figW) { slots.Add(alongX ? (0, fixedV, nx, ny, face) : (fixedV, 0, nx, ny, face)); return; }
                for (double v = -half + figW / 2; v <= half - figW / 2 + 1e-9; v += figW)
                    slots.Add(alongX ? (v, fixedV, nx, ny, face) : (fixedV, v, nx, ny, face));
            }
            Edge(true, -D - off, F, 0, -1, Q.Facing + 180);    // фронт врага
            Edge(true, D + off, F, 0, 1, Q.Facing);            // тыл
            Edge(false, -F - off, D, -1, 0, Q.Facing + 90);    // левый фланг
            Edge(false, F + off, D, 1, 0, Q.Facing - 90);      // правый фланг
            int Nearest(IEnumerable<int> from, double lx, double ly) =>
                from.OrderBy(i => JsMath.Hypot(slots[i].lx - lx, slots[i].ly - ly)).ThenBy(i => i).DefaultIfEmpty(-1).First();
            // занято: у места стоит чья-то фигурка (кроме самого врага и колонн, что здесь раздаются) —
            // каждая занимает одно место, ближайшее к себе
            var mine = new HashSet<FigState>(kept.Concat(cols).SelectMany(c => c.Select(k => x.Figs[k])));
            var busy = new bool[slots.Count];
            foreach (var m in Movers)
            {
                if (m == y) continue;
                foreach (var s in m.Figs)
                {
                    if (mine.Contains(s)) continue;
                    Q.ToLocal(s.X, s.Y, out var lx, out var ly);
                    int i = Nearest(Enumerable.Range(0, slots.Count), lx, ly);
                    if (i >= 0 && JsMath.Hypot(slots[i].lx - lx, slots[i].ly - ly) < figW * 0.75) busy[i] = true;
                }
            }
            var free = Enumerable.Range(0, slots.Count).Where(i => !busy[i]).ToList();
            void Take(List<int> col, int si, int behind)
            {
                var sl = slots[si];
                foreach (int k in col)
                {
                    var s = x.Figs[k];
                    s.Wrap = true; s.WFoe = y; s.WSlotX = sl.lx; s.WSlotY = sl.ly; s.WNx = sl.nx; s.WNy = sl.ny; s.WBehind = behind; s.WH = MoveSim.Norm(sl.face);
                }
                Route(x, col, figW, figD, mg);
            }
            // 1) бьются — при своём месте (ближайшем к прежнему: строй врага сужается и места сдвигаются)
            foreach (var col in kept)
            {
                var head = x.Figs[col[0]];
                int si = Nearest(free, head.WSlotX, head.WSlotY);
                if (si >= 0 && JsMath.Hypot(slots[si].lx - head.WSlotX, slots[si].ly - head.WSlotY) < figW) { free.Remove(si); Take(col, si, head.WBehind); }
                else Route(x, col, figW, figD, mg);
            }
            // 2) кто шёл к своему месту — держит его; 3) остальные — по близости к врагу
            var order = cols.OrderBy(c => x.Figs[c[0]].WFoe == y ? 0 : 1)
                            .ThenBy(c => JsMath.Hypot(x.Figs[c[0]].X - Q.X, x.Figs[c[0]].Y - Q.Y)).ToList();
            foreach (var col in order)
            {
                var head = x.Figs[col[0]];
                bool had = head.WFoe == y;
                double lx = head.WSlotX, ly = head.WSlotY;
                if (!had) Q.ToLocal(head.X, head.Y, out lx, out ly);
                int si = Nearest(free, lx, ly);
                if (si >= 0) { free.Remove(si); Take(col, si, 0); }
                else if (had) Take(col, Nearest(Enumerable.Range(0, slots.Count), lx, ly), col.Count);   // мест нет — второй линией за своим
                else foreach (int k in col) x.Figs[k].WFoe = null;
            }
        }

        // Колонна у своего места: голова — та фигурка, что ближе к врагу (по нормали места), остальные — за ней
        // наружу; каждой — первая точка пути в обход строя врага
        void Route(Mover x, List<int> col, double figW, double figD, double mg)
        {
            var h = x.Figs[col[0]]; var Q = h.WFoe.P;
            double sx = h.WSlotX, sy = h.WSlotY, nx = h.WNx, ny = h.WNy; int behind = h.WBehind;
            var byDepth = col.OrderBy(k =>
            {
                Q.ToLocal(x.Figs[k].X, x.Figs[k].Y, out var lx, out var ly);
                return (lx - sx) * nx + (ly - sy) * ny;
            }).ThenBy(k => k).ToList();
            for (int r = 0; r < byDepth.Count; r++)
            {
                var s = x.Figs[byDepth[r]];
                double tx = sx + nx * (behind + r) * figD, ty = sy + ny * (behind + r) * figD;
                var (gx, gy) = AroundCorner(Q, s, tx, ty, figW, figD, mg);
                Q.ToWorld(gx, gy, out var wx, out var wy);
                s.WX = wx; s.WY = wy;
            }
        }

        // Путь к месту у врага в обход его строя: кратчайший путь по углам снаружи (граф видимости: где стоит
        // фигурка, четыре угла, место), берём первую точку. Прямо видно место — сразу к нему; дошла до угла —
        // дальше к следующему углу или к месту, а не снова к тому же углу
        static (double x, double y) AroundCorner(Placed Q, FigState s, double tx, double ty, double figW, double figD, double mg)
        {
            double F = Q.Fp.Front / 2, D = Q.Fp.Depth / 2, a = figD / 2;
            Q.ToLocal(s.X, s.Y, out var cx, out var cy);
            if (Math.Abs(cx) < F + a && Math.Abs(cy) < D + a) return (tx, ty);   // уже вплотную — напрямую
            if (!SegHitsBox(cx, cy, tx, ty, F + a, D + a)) return (tx, ty);
            double c = Math.Max(figW, figD) / 2 + mg, ox = F + c, oy = D + c;
            var pt = new[] { (cx, cy), (-ox, -oy), (ox, -oy), (ox, oy), (-ox, oy), (tx, ty) };
            int n = pt.Length;
            var dist = new double[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
            dist[0] = 0;
            for (int it = 0; it < n; it++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < double.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || u == n - 1) break;
                done[u] = true;
                for (int v = 1; v < n; v++)
                {
                    if (done[v] || SegHitsBox(pt[u].Item1, pt[u].Item2, pt[v].Item1, pt[v].Item2, F + a, D + a)) continue;
                    double w = dist[u] + JsMath.Hypot(pt[v].Item1 - pt[u].Item1, pt[v].Item2 - pt[u].Item2);
                    if (w < dist[v]) { dist[v] = w; prev[v] = u; }
                }
            }
            if (prev[n - 1] < 0) return (tx, ty);
            int k = n - 1;
            while (prev[k] != 0) k = prev[k];
            return pt[k];
        }
        // Пересекает ли отрезок прямоугольник |x| < hx, |y| < hy (Лян — Барски)
        static bool SegHitsBox(double x0, double y0, double x1, double y1, double hx, double hy)
        {
            double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-12) return q > 0;
                double r = q / p;
                if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            return Clip(-dx, x0 + hx) && Clip(dx, hx - x0) && Clip(-dy, y0 + hy) && Clip(dy, hy - y0) && t1 - t0 > 1e-9;
        }

        // ── потери — фигурками (Г30): раскладка по нынешней численности, фронт сужается с краёв, как фишка
        // трекера; лишние фигурки падают на месте. Строй развёрнут — место в строю (колонна, шеренга) остаётся за
        // той же фигуркой: колонна не рвётся, даже если ушла в охват (Г68) далеко от своего места. Остальные места
        // (и всё в походной колонне) — ближайшим фигуркам спереди назад ──
        public void Relayout(Mover m)
        {
            if (m.Gone) return;   // ушёл с поля — раскладывать некого
            if (m.Fleeing) { RelayoutCrowd(m); return; }
            var P = m.P;
            int n = (int)Math.Max(0, Js.Round(P.U.Soldiers));
            if (n == m.LaidMen) return;
            int melee = Math.Max(0, m.LaidMen - n - m.ShotDown);
            m.ShotDown = 0;
            if (melee > 0) MeleeDeaths(m, melee);
            m.LaidMen = n;
            int oldCols = m.Cols, oldNominal = m.NominalCols;
            var figs = Formation.Layout(P.U, MenPerFigure, R);
            var free = Enumerable.Range(0, m.Figs.Count).ToList();
            var bodies = new FigState[figs.Count];
            if (oldCols == oldNominal && P.Figs.Count == m.Figs.Count)
            {
                var had = new Dictionary<(int file, int rank), int>();
                for (int q = 0; q < P.Figs.Count; q++) had[(P.Figs[q].File, P.Figs[q].Rank)] = q;
                for (int k = 0; k < figs.Count; k++)
                    if (had.TryGetValue((figs[k].File, figs[k].Rank), out var q)) { bodies[k] = m.Figs[q]; free.Remove(q); }
            }
            foreach (int k in Enumerable.Range(0, figs.Count).OrderBy(i => figs[i].Y).ThenBy(i => figs[i].X))
            {
                if (bodies[k] != null) continue;
                P.ToWorld(figs[k].X, figs[k].Y, out var wx, out var wy);
                int best = -1; double bd = double.MaxValue;
                for (int q = 0; q < free.Count; q++)
                {
                    var s = m.Figs[free[q]];
                    double d = (s.X - wx) * (s.X - wx) + (s.Y - wy) * (s.Y - wy);
                    if (d < bd) { bd = d; best = q; }
                }
                if (best >= 0) { bodies[k] = m.Figs[free[best]]; free.RemoveAt(best); }
                else bodies[k] = new FigState { Id = m.NextFigId++, X = wx, Y = wy };
            }
            foreach (int q in free) m.Fallen.Add((m.Figs[q].X, m.Figs[q].Y));
            P.Figs = figs; P.Fp = Formation.Of(P.U, R);
            m.Figs = bodies.ToList();
            m.Nominal = figs.Select(f => (f.X, f.Y, f.Rank, f.File)).ToList();
            m.NominalFp = new Footprint { Front = P.Fp.Front, Depth = P.Fp.Depth };
            m.NominalCols = m.Cols = figs.Count == 0 ? 0 : figs.Max(f => f.File) + 1;
            if (oldCols < oldNominal && m.NominalCols > 0) MoveSim.SetCols(m, Math.Min(oldCols, m.NominalCols));   // был в колонне — остаётся
            Soldiers.Assign(m, R);   // бойцы — на места новой раскладки: задние выходят вперёд (Г30, Г75)
        }
    }
}
