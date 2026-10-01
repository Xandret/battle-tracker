// ═══════════ Battle.cs — бой в движении (И1, БД1, БД3; Г62–Г64, Г68–Г69, Г44, Г29, Г30) — ЧЕРНОВИК ДО ГМа ═══════════
// Один ход боя — то же движение (MoveSim.Step), а между его шагами — рукопашная. Схватка (Fight) — пара
// врагов, чьи фигурки коснулись (не дальше MeleeGap): обмены ударами по кругу в 15 с от касания (Г62) —
// сначала атаки того, кто начал (Г44), на каждую — ответ, если удар пришёл во фронт; потом атаки второго.
// Бой идёт через границу хода без перерыва. Урон течёт каждый шаг по формуле стола, как в MeleeSim; в конце
// окна — округление, летальность, БД. Бьют колонны фигурок, что касаются врага (Г27). Натиск (Г29) — всплеск
// в первые 2 с касания, цель в это время не отвечает: нужен приказ «натиск», конница и разбег ≥ 50 м по
// чистому (К29); пики во фронт его гасят. Павшие фигурки выпадают из строя, строй смыкается с краёв (Г30).
// Атакованный во фланг сам не поворачивается — ждёт приказа (Г63). Свисающие колонны огибают врага перед
// фронтом — во фланг и в тыл (Г68); колонны делятся между врагами, ответы — общие на круг (Г69).
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
        // начинает обмен (Г44) тот, у кого приказ «атаковать» или кто идёт; «держать позицию» и стоящий — отвечают
        static bool Attacking(Mover m) => m.Order != null && (m.Order.Kind == OrderKind.Attack || m.Order.Kind == OrderKind.Move && !m.Done);
        Fight FightOf(Mover x, Mover y) => Fights.FirstOrDefault(f => !f.Over && (f.A == x && f.B == y || f.A == y && f.B == x));

        // ── приказы (Г15) ──
        public void Order(Mover m, MoveOrder o)
        {
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
            if (t == null || !Alive(t)) { m.Done = true; m.Vs = 0; return; }
            if (Shooter(m)) { ReplanShoot(m, t); return; }
            var f = FightOf(m, t);
            if (f != null && f.Of(m).Engaged > 0) return;
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
            Details.Clear();
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
                if (k % contactEvery == 0) { Contacts(t); Envelop(); }
                Strike(t, dt);
                Shoot(t, dt);
                foreach (var v in Volleys) if (!shotThisTurn.Contains(v)) shotThisTurn.Add(v);
                if ((k + 1) % contactEvery == 0) foreach (var m in Movers) Relayout(m);
                frame?.Invoke((k + 1) * dt);
            }
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
            // 1) касания фигурок у всех пар врагов поблизости
            for (int i = 0; i < Movers.Count; i++)
                for (int j = i + 1; j < Movers.Count; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    if (!(Alive(x) && Alive(y) && Enemies(x.P.U, y.P.U))) continue;
                    double reach = (JsMath.Hypot(x.P.Fp.Front, x.P.Fp.Depth) + JsMath.Hypot(y.P.Fp.Front, y.P.Fp.Depth)) / 2 + R.Map.MeleeGap + 10 + WrapReach(x) + WrapReach(y);
                    if (JsMath.Hypot(x.P.X - y.P.X, x.P.Y - y.P.Y) > reach) continue;
                    touches[(x, y)] = Bodies.Touch(x, y, R.Map.MeleeGap);
                    touches[(y, x)] = Bodies.Touch(y, x, R.Map.MeleeGap);
                }
            // 2) колонна — тому врагу, которого касается ближе всех (Г69): сила отряда делится, а не растёт
            foreach (var kv in touches)
            {
                var (x, y) = kv.Key;
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
                    bool can = Alive(x) && Alive(y) && Enemies(x.P.U, y.P.U);
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
                    w.Open = true;
                    w.U = MeleeSim.Fortune(w.Att.P.U, ModeAt(w.Def), Ctx);
                    w.N0 = w.Att.P.U.Soldiers;
                    if (!w.Counter && !w.Charge)
                    {
                        var sa = f.Of(w.Att);
                        if (f.Of(w.Def).Engaged > 0 && sa.Front > 0 && TakeCounter(w.Def))
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
        }

        // Павшие в рукопашной (Г67): точного места нет — падают у переднего края схватки, в случайной точке
        // касающихся фигурок, лицом к врагу, кровь брызжет назад (от удара). Случайность — своя, только для рисунка.
        void MeleeDeaths(Mover m, int n)
        {
            if (m.Figs.Count == 0) return;
            List<int> figs; Mover foe = null;
            if (frontFigs.TryGetValue(m, out var fr) && fr.figs.Count > 0) { figs = fr.figs.Where(k => k < m.Figs.Count).ToList(); foe = fr.foe; }
            else figs = Enumerable.Range(0, m.Figs.Count).Where(k => m.P.Figs[k].Rank == 0).ToList();
            if (figs.Count == 0) figs = Enumerable.Range(0, m.Figs.Count).ToList();
            double h = m.P.Facing * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            for (int i = 0; i < n; i++)
            {
                int k = figs[(int)(look() * figs.Count) % figs.Count];
                var s = m.Figs[k]; var fg = m.P.Figs[k];
                double ox = (look() - 0.5) * fg.Width, oy = (look() - 0.5) * fg.Depth;
                double x = s.X + ox * rx - oy * fx, y = s.Y + ox * ry - oy * fy;
                double dir = foe != null ? Math.Atan2(y - foe.P.Y, x - foe.P.X) * 180 / Math.PI : (m.P.Facing + 90);   // от врага — за спину
                double u = look();
                Deaths.Add(new Death
                {
                    X = x, Y = y, T = curT, Facing = m.P.Facing + (look() - 0.5) * 60, Dir = dir + (look() - 0.5) * 50,
                    UnitId = m.P.U.Id, Part = u < 0.25 ? "head" : u < 0.8 ? "torso" : "legs", Killed = true,
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
                if (foes.Count == 0 || x.P.Figs.Count == 0)
                {
                    foreach (var s in x.Figs) { s.Wrap = false; s.WFoe = null; }
                    continue;
                }
                var engaged = colFoe.TryGetValue(x, out var cf) ? cf : new Dictionary<int, (Mover foe, double d)>();
                // огибают только того, кто перед фронтом: атакованный во фланг или в тыл сам не поворачивается (Г63)
                var ahead = foes.Where(q => Math.Abs(MoveSim.AngleDiff(x.P.Facing, MoveSim.HeadingOf(q.P.X - x.P.X, q.P.Y - x.P.Y))) <= R.Sectors.FrontMax).ToList();
                foreach (var g in Enumerable.Range(0, x.P.Figs.Count).GroupBy(k => x.P.Figs[k].File))
                {
                    var col = g.ToList();
                    var head = x.Figs[col[0]];
                    if (engaged.ContainsKey(g.Key))
                    {
                        if (col.Any(k => x.Figs[k].Wrap) && head.WFoe != null && foes.Contains(head.WFoe)) { Plan(x, head.WFoe).kept.Add(col); continue; }
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
                else bodies[k] = new FigState { X = wx, Y = wy };
            }
            foreach (int q in free) m.Fallen.Add((m.Figs[q].X, m.Figs[q].Y));
            P.Figs = figs; P.Fp = Formation.Of(P.U, R);
            m.Figs = bodies.ToList();
            m.Nominal = figs.Select(f => (f.X, f.Y, f.Rank, f.File)).ToList();
            m.NominalFp = new Footprint { Front = P.Fp.Front, Depth = P.Fp.Depth };
            m.NominalCols = m.Cols = figs.Count == 0 ? 0 : figs.Max(f => f.File) + 1;
            if (oldCols < oldNominal && m.NominalCols > 0) MoveSim.SetCols(m, Math.Min(oldCols, m.NominalCols));   // был в колонне — остаётся
        }
    }
}
