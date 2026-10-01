// ═══════════ BattleShoot.cs — стрельба в бою (И1, БД2; Г65, Г66, Г36, Г40, Г62, Г67) — ЧЕРНОВИК ДО ГМа ═══════════
// Та же баллистика, что в RangedSim (Г33, Г37–Г41): стрел за окно — урон удара стола без защиты цели × VolleyK лука,
// каждая летит сама, попадание — в того, кого встретит (врага или своего, Г40), выбыл — с шансом брони стола.
// Отличие — бойцы привязаны к фигуркам и идут вместе с ними: каждый шаг тела переезжают за своей фигуркой.
// Перестрелка (Volley) — пара «стрелок → цель» по приказу (Г65): обмены по кругу в 15 с от первого выстрела (Г62),
// цель отвечает, если сама стрелок и достаёт. Пешие стреляют только стоя и лицом к цели, конные — и на ходу, хуже
// (Г36). В схватке (Г28) и по сцепившимся с нашими (Г40) не стреляют. Упреждение (Г66): целят туда, где цель будет
// к прилёту стрелы, с ошибкой тем больше, чем быстрее цель. Каждый павший записывается — место, направление удара,
// часть тела (Г67, только для рисунка).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Volley
    {
        public Mover A, B;                  // A стреляет по B (приказ, Г65); B отвечает, если сам стрелок
        public double T0, CycleEnd, LastOk;
        public bool Over;
        public double LossA, LossB, Friendly; public long Arrows;   // за этот ход — для журнала
        internal List<VWin> Wins = new List<VWin>();
        internal Dictionary<Mover, int> CountersLeft = new Dictionary<Mover, int>();
    }
    internal sealed class VWin
    {
        public Volley V; public Mover Att, Def; public bool Counter;
        public double T0, T1, U, N0, LRoll; public int Planned, Launched, Landed;
        public bool Open, Closed, Skipped; public string Bow;
        public Dictionary<Mover, (int cas, int killed)> Victims = new Dictionary<Mover, (int cas, int killed)>();
    }
    internal sealed class Shot
    {
        public VWin W; public int Index; public double LaunchT;
        public double X, Y, Z, VX, VY, VZ, CanopyLeft, LX, LY;
        public Body Shooter; public bool Flying, Done;
    }
    // Павший (Г67): где упал, когда (часы боя), чей, куда смотрел, откуда пришёл удар, во что попало
    public struct Death { public double X, Y, T, Facing, Dir; public int UnitId; public string Part; public bool Killed; }

    public sealed partial class Battle
    {
        public List<Volley> Volleys = new List<Volley>();
        public ShotStats Shots = new ShotStats();            // стрелы за этот ход
        public List<Death> Deaths = new List<Death>();       // павшие с начала боя — для рисунка (Г67)
        public double LeadErrK = 0.35, MovingShotSigmaK = 1.5, FaceTolDeg = 30, ShootStopShare = 0.85;

        readonly List<Shot> queue = new List<Shot>(), flying = new List<Shot>();
        int nextShot;
        readonly Dictionary<Mover, Troop> troops = new Dictionary<Mover, Troop>();
        readonly BodyGrid grid = new BodyGrid();
        readonly Dictionary<(Rules.BowR, int, int, int), double> flightCache = new Dictionary<(Rules.BowR, int, int, int), double>();
        readonly Func<double> look = new Mulberry32(20261001u).Next;   // случайность только для рисунка (Г67)

        // Бойцы отряда поимённо: тело, номер фигурки, место в фигурке (её система координат)
        sealed class Troop
        {
            public int Laid = -1, Cols = -1, Figs = -1;
            public List<Body> Bodies = new List<Body>();
            public List<int> Fig = new List<int>();
            public List<double> Ox = new List<double>(), Oy = new List<double>();
        }

        static bool Shooter(Mover m) => m.P.U.Weapon == "ranged";
        static double Speed(FigState s) => JsMath.Hypot(s.Vx, s.Vy);
        bool InMelee(Mover m) => Fights.Any(f => !f.Over && f.Touching && (f.A == m || f.B == m));
        // Г40: по врагу, сцепившемуся с нашими, не стреляют
        bool TangledWithFriends(Mover target, Mover shooter) =>
            Fights.Any(f => !f.Over && f.Touching && (f.A == target && !Enemies(f.B.P.U, shooter.P.U) || f.B == target && !Enemies(f.A.P.U, shooter.P.U)));

        // Края строя в мире и расстояние между ними — как BattleMap.UnitGap, только по живым координатам
        static double[][] Corners(Placed p)
        {
            double f = p.Fp.Front / 2, d = p.Fp.Depth / 2;
            var res = new double[4][];
            var loc = new[] { (-f, -d), (f, -d), (f, d), (-f, d) };
            for (int i = 0; i < 4; i++) { p.ToWorld(loc[i].Item1, loc[i].Item2, out var x, out var y); res[i] = new[] { x, y }; }
            return res;
        }
        double Gap(Mover a, Mover b) => BattleMap.PolyGap(Corners(a.P), Corners(b.P));
        // Дальность стрелка (К23): своя или по типу; с высоты — дальше (К16)
        double RangeVs(Mover s, Mover t)
        {
            double max = BattleMap.RangeOf(s.P.U, R);
            double dz = LevelOf(s) - LevelOf(t);
            return dz > 0 ? Js.Round(max * (1 + R.Map.Height.RangePerLevel * dz)) : max;
        }
        bool Standing(Mover m) => m.Vs < 0.05 && m.AboutLeft <= 1e-9;
        bool Facing(Mover s, Mover t) => Math.Abs(MoveSim.AngleDiff(s.P.Facing, MoveSim.HeadingOf(t.P.X - s.P.X, t.P.Y - s.P.Y))) <= FaceTolDeg;
        // Может ли s сейчас стрелять по t
        bool CanShoot(Mover s, Mover t) =>
            Alive(s) && Alive(t) && Shooter(s) && Enemies(s.P.U, t.P.U) && !InMelee(s) && !TangledWithFriends(t, s)
            && (Units.IsCav(s.P.U) || Standing(s) && Facing(s, t)) && Gap(s, t) <= RangeVs(s, t);

        // ── приказ «стрелять по цели» (Г65): вне дальности — подойти на долю дальности и встать лицом к цели ──
        void AimShoot(Mover m, MoveOrder o, Mover t)
        {
            double gap = Gap(m, t), range = RangeVs(m, t);
            double hx = t.P.X - m.P.X, hy = t.P.Y - m.P.Y, hl = Math.Max(1e-9, JsMath.Hypot(hx, hy));
            if (gap <= range)
            {
                o.X = m.P.X; o.Y = m.P.Y;   // уже достаёт — стоит и разворачивается к цели
            }
            else
            {
                double stand = range * ShootStopShare + (t.P.Fp.Depth + m.P.Fp.Depth) / 2;
                o.X = t.P.X - hx / hl * stand; o.Y = t.P.Y - hy / hl * stand;
            }
            o.Facing = MoveSim.HeadingOf(hx, hy);
            if (gap <= range) MoveSim.TurnInPlace(m, o); else MoveSim.Give(m, o, Geo, R);
            aimed[m] = (t.P.X, t.P.Y);
        }
        void ReplanShoot(Mover m, Mover t)
        {
            bool inRange = Gap(m, t) <= RangeVs(m, t);
            bool moved = !aimed.TryGetValue(m, out var a) || JsMath.Hypot(a.x - t.P.X, a.y - t.P.Y) >= ReplanMoveM;
            // достаёт и смотрит на цель — стоим; ушла из дальности или сильно сдвинулась — заново
            if (inRange && Facing(m, t) && (m.Done || m.Track == null)) return;
            if (inRange && !moved && m.Order != null && !m.Done) return;
            if (!inRange && !moved && m.Order != null && !m.Done) return;
            AimShoot(m, m.Order, t);
        }

        // ── тела бойцов (Г33) — по фигуркам; перестраиваются, когда меняется раскладка ──
        Troop TroopOf(Mover m)
        {
            if (!troops.TryGetValue(m, out var tr)) troops[m] = tr = new Troop();
            if (tr.Laid == m.LaidMen && tr.Cols == m.Cols && tr.Figs == m.P.Figs.Count) return tr;
            var P = m.P; var f = R.Map.Formation.TryGetValue(P.U.Type, out var ff) ? ff : R.Map.Formation["infantry"];
            tr.Bodies.Clear(); tr.Fig.Clear(); tr.Ox.Clear(); tr.Oy.Clear();
            bool horse = Units.IsCav(P.U);
            for (int k = 0; k < P.Figs.Count; k++)
            {
                var fg = P.Figs[k];
                int cols = Math.Max(1, (int)Math.Round(fg.Width / f.PerMan)), rows = Math.Max(1, (int)Math.Round(fg.Depth / f.RankDepth));
                int left = (int)Math.Round(fg.Men);
                for (int j = 0; j < rows && left > 0; j++)
                {
                    int n = Math.Min(cols, left); double off = (cols - n) / 2.0; left -= n;
                    for (int i = 0; i < n; i++)
                    {
                        double ox = -fg.Width / 2 + (off + i + 0.5) * f.PerMan, oy = -fg.Depth / 2 + (j + 0.5) * f.RankDepth;
                        tr.Bodies.Add(new Body
                        {
                            Owner = P, Horse = horse, Facing = P.Facing, File = fg.File, Rank = fg.Rank * rows + j,
                            FrontDist = fg.Y + oy + P.Fp.Depth / 2,
                        });
                        tr.Fig.Add(k); tr.Ox.Add(ox); tr.Oy.Add(oy);
                    }
                }
            }
            tr.Laid = m.LaidMen; tr.Cols = m.Cols; tr.Figs = P.Figs.Count;
            return tr;
        }
        void PlaceBodies(Mover m, Troop tr)
        {
            var P = m.P;
            double h = P.Facing * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            for (int i = 0; i < tr.Bodies.Count; i++)
            {
                int k = tr.Fig[i];
                if (k >= m.Figs.Count) continue;
                var s = m.Figs[k]; var b = tr.Bodies[i];
                b.X = s.X + tr.Ox[i] * rx - tr.Oy[i] * fx;
                b.Y = s.Y + tr.Ox[i] * ry - tr.Oy[i] * fy;
                b.Facing = P.Facing;
                b.Ground = GroundZ(b.X, b.Y);
            }
        }
        double GroundZ(double x, double y) => Geo?.Map == null ? 0 : Terrain.CellAt(Geo.Map, x / Geo.W, y / Geo.H).Z * R.Ranged.MetersPerLevel;
        bool Forest(double x, double y) => Geo?.Map != null && Terrain.CellAt(Geo.Map, x / Geo.W, y / Geo.H).T == Terrain.Id("forest");

        // ── шаг стрельбы: перестрелки, окна, выстрелы, полёт ──
        void Shoot(double t, double dt)
        {
            var RR = R.Ranged;
            // приказ «стрелять» — перестрелка с целью, пока может стрелять (Г65)
            foreach (var s in Movers)
            {
                if (s.Order == null || s.Order.Kind != OrderKind.Attack || !Shooter(s)) continue;
                var tgt = ById(s.Order.TargetId);
                if (tgt == null) continue;
                var v = Volleys.FirstOrDefault(x => !x.Over && x.A == s && x.B == tgt);
                if (CanShoot(s, tgt))
                {
                    if (v == null) Volleys.Add(v = StartVolley(s, tgt, t));
                    v.LastOk = t;
                }
            }
            foreach (var v in Volleys)
            {
                if (v.Over) continue;
                bool ordered = v.A.Order != null && v.A.Order.Kind == OrderKind.Attack && v.A.Order.TargetId == v.B.P.U.Id;
                if (!ordered || t - v.LastOk > FightEndSec) { v.Over = true; continue; }
                if (t + dt > v.CycleEnd + 1e-9) ScheduleVolley(v, v.CycleEnd);
                foreach (var w in v.Wins.ToList())
                {
                    if (w.Open || w.Skipped || w.T0 >= t + dt - 1e-9) continue;
                    if (!Alive(w.Att) || !Alive(w.Def)) { w.Skipped = true; continue; }
                    if (w.Counter && (w.Att.P.U.Morale < R.Morale.ShakenBelow || !CanShoot(w.Att, w.Def))) { w.Skipped = true; continue; }
                    OpenVolleyWin(w);
                }
            }
            // тела — за фигурками; сетка — только пока что-то летит или вылетает
            bool busy = flying.Count > 0 || nextShot < queue.Count && queue[nextShot].LaunchT < t + dt;
            if (busy)
            {
                grid.Clear();
                foreach (var m in Movers)
                {
                    if (!Alive(m)) continue;
                    var tr = TroopOf(m);
                    PlaceBodies(m, tr);
                    foreach (var b in tr.Bodies) if (b.Alive) grid.Add(b);
                }
            }
            int sub = Math.Max(1, (int)Math.Ceiling(dt / RR.Dt - 1e-9));
            double subDt = dt / sub;
            for (int k = 0; k < sub; k++)
            {
                double ts = t + k * subDt;
                curT = ts;
                while (nextShot < queue.Count && queue[nextShot].LaunchT < ts + subDt)
                {
                    var sh = queue[nextShot++];
                    sh.W.Launched++;
                    Launch(sh);
                    if (sh.Flying) flying.Add(sh);
                }
                foreach (var sh in flying) if (!sh.Done) Fly(sh, subDt);
                flying.RemoveAll(x => x.Done);
            }
            if (nextShot > 4096) { queue.RemoveRange(0, nextShot); nextShot = 0; }
            foreach (var v in Volleys)
                foreach (var w in v.Wins)
                    if (w.Open && !w.Closed && w.T1 <= t + dt + 1e-9 && w.Launched >= w.Planned && w.Landed >= w.Planned) CloseVolleyWin(w, t + dt);
            foreach (var v in Volleys) v.Wins.RemoveAll(w => w.Closed || w.Skipped);
            Volleys.RemoveAll(v => v.Over && v.Wins.Count == 0);
        }

        Volley StartVolley(Mover a, Mover b, double t)
        {
            var v = new Volley { A = a, B = b, T0 = t, LastOk = t };
            ScheduleVolley(v, t);
            return v;
        }
        // Круг (Г62): атаки стрелка за 15 с; на каждую — ответ цели, если она стрелок (решается в момент окна)
        void ScheduleVolley(Volley v, double start)
        {
            int n = (int)Units.AttackLimit(v.A.P.U, R);
            double len = R.Move.TurnSec / Math.Max(1, n);
            v.CountersLeft[v.B] = (int)Units.CounterLimit(v.B.P.U, R);
            for (int j = 0; j < n; j++)
            {
                double t0 = start + j * len, t1 = start + (j + 1) * len;
                v.Wins.Add(new VWin { V = v, Att = v.A, Def = v.B, T0 = t0, T1 = t1 });
                if (Shooter(v.B) && v.CountersLeft[v.B] > 0)
                {
                    v.CountersLeft[v.B]--;
                    v.Wins.Add(new VWin { V = v, Att = v.B, Def = v.A, Counter = true, T0 = t0, T1 = t1 });
                }
            }
            v.CycleEnd = start + R.Move.TurnSec;
        }

        void OpenVolleyWin(VWin w)
        {
            var RR = R.Ranged;
            const string mode = Modes.RangedForm;   // местность — физикой (Г38), а не режимом стола
            w.Open = true;
            w.U = MeleeSim.Fortune(w.Att.P.U, mode, Ctx);
            w.N0 = w.Att.P.U.Soldiers;
            w.LRoll = Dice.Roll(Ctx.Rng, R.Lethality.Die);
            w.Bow = Ballistics.BowKeyFor(w.Att.P.U);
            var A = Combat.Eff(w.Att.P.U, mode, null, Ctx);
            var opts = new BattleRequest { Mode = mode, FatigueMode = "percent" };
            double dmg = Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, A, new EffStats(), opts, null, Ctx, false, 1, "", "front", null, w.U * w.N0);
            w.Planned = (int)Js.Round(Math.Max(0, dmg) * RR.Bows[w.Bow].VolleyK);
            var fresh = new List<Shot>();
            for (int i = 0; i < w.Planned; i++)
                fresh.Add(new Shot { W = w, Index = i, LaunchT = w.T0 + (i + 0.5) / w.Planned * (w.T1 - w.T0) });
            // очередь по времени выстрела; при равенстве — порядок добавления (сортировка устойчивая)
            var rest = queue.GetRange(nextShot, queue.Count - nextShot);
            rest.AddRange(fresh);
            queue.RemoveRange(nextShot, queue.Count - nextShot);
            queue.AddRange(rest.OrderBy(x => x.LaunchT));
        }

        void Launch(Shot ar)
        {
            var w = ar.W; var RR = R.Ranged;
            ar.Done = true;
            bool ok = w.Counter ? CanShoot(w.Att, w.Def) : CanShoot(w.Att, w.Def) && w.V.A.Order != null && w.V.A.Order.Kind == OrderKind.Attack;
            if (!ok || !Alive(w.Att) || !Alive(w.Def)) { w.Landed++; return; }
            // стрелы — по всему строю поровну; стрелу павшего выпускает живой сосед
            var shooters = TroopOf(w.Att).Bodies;
            PlaceBodies(w.Att, troops[w.Att]);
            Body sh = null;
            for (int step = 0; step < shooters.Count && sh == null; step++)
            {
                var c = shooters[(int)(((long)ar.Index * shooters.Count / Math.Max(1, w.Planned) + step) % shooters.Count)];
                if (c.Alive) sh = c;
            }
            if (sh == null) { w.Landed++; return; }
            // цель — живой враг перед стрелком (до TargetSideM вбок); нет таких — ближайший вбок из попробованных
            var tt = TroopOf(w.Def); PlaceBodies(w.Def, tt);
            var targets = tt.Bodies;
            if (targets.Count == 0) { w.Landed++; return; }
            double srx = Math.Cos(sh.Facing * Math.PI / 180), sry = Math.Sin(sh.Facing * Math.PI / 180);
            Body tgt = null, fallback = null; int tgtIdx = -1, fbIdx = -1; double fbSide = double.MaxValue;
            for (int tries = 0; tries < 40 && tgt == null; tries++)
            {
                int ci = (int)Math.Floor(Ctx.Rng() * targets.Count);
                var c = targets[ci];
                if (!c.Alive) continue;
                double side = Math.Abs((c.X - sh.X) * srx + (c.Y - sh.Y) * sry);
                if (side <= RR.TargetSideM) { tgt = c; tgtIdx = ci; }
                else if (side < fbSide) { fbSide = side; fallback = c; fbIdx = ci; }
            }
            if (tgt == null) { tgt = fallback; tgtIdx = fbIdx; }
            if (tgt == null) { w.Landed++; return; }
            var bow = RR.Bows[w.Bow];
            double sx = sh.X, sy = sh.Y, sz = sh.Ground + (sh.Horse ? RR.HorseHeight + 0.7 : RR.LaunchHeight);
            double rankDepth = R.Map.Formation.TryGetValue(w.Att.P.U.Type, out var ff) ? ff.RankDepth : 1;
            // упреждение (Г66): куда цель придёт к прилёту стрелы; ошибка — от скорости цели
            var tfs = w.Def.Figs[Math.Min(w.Def.Figs.Count - 1, tt.Fig[tgtIdx])];
            double vx = tfs.Vx, vy = tfs.Vy, px = tgt.X, py = tgt.Y;
            (double theta, double speed, bool high)? aim = null;
            double tof = 0;
            for (int it = 0; it < 3; it++)
            {
                double ddx = px - sx, ddy = py - sy, dd = Math.Sqrt(ddx * ddx + ddy * ddy);
                aim = Ballistics.AimCached(bow, RR, dd, tgt.Ground + RR.AimHeight - sz, sh.FrontDist, rankDepth, sz - sh.Ground);
                if (aim == null) break;
                tof = Flight(bow, dd, aim.Value);
                px = tgt.X + vx * tof; py = tgt.Y + vy * tof;
                if (vx == 0 && vy == 0) break;
            }
            if (aim == null) { Shots.Unreachable++; w.Landed++; return; }
            double tv = Math.Sqrt(vx * vx + vy * vy);
            if (tv > 0.1)
            {
                double e = LeadErrK * tv * tof;   // разброс точки упреждения, м
                px += Ballistics.Gauss(Ctx.Rng) * e; py += Ballistics.Gauss(Ctx.Rng) * e;
            }
            double dx = px - sx, dy = py - sy, d = Math.Sqrt(dx * dx + dy * dy);
            var aim2 = Ballistics.AimCached(bow, RR, d, tgt.Ground + RR.AimHeight - sz, sh.FrontDist, rankDepth, sz - sh.Ground) ?? aim;
            if (aim2.Value.high) Shots.HighShots++; else Shots.LowShots++;
            // конный стрелок на ходу — разброс шире (Г36)
            double sk = Units.IsCav(w.Att.P.U) && w.Att.Figs.Count > 0 && Speed(w.Att.Figs[0]) > 1 ? MovingShotSigmaK : 1;
            double th = aim2.Value.theta + Ballistics.Gauss(Ctx.Rng) * bow.SigmaElevDeg * sk * Math.PI / 180;
            double az = Math.Atan2(dy, dx) + Ballistics.Gauss(Ctx.Rng) * bow.SigmaAzDeg * sk * Math.PI / 180;
            double v = Ballistics.V0(bow) * aim2.Value.speed * (1 + Ballistics.Gauss(Ctx.Rng) * bow.SigmaSpeed);
            ar.Shooter = sh;
            ar.X = sx; ar.Y = sy; ar.Z = sz; ar.LX = sx; ar.LY = sy;
            ar.VX = v * Math.Cos(th) * Math.Cos(az); ar.VY = v * Math.Cos(th) * Math.Sin(az); ar.VZ = v * Math.Sin(th);
            ar.CanopyLeft = -Math.Log(1 - Ctx.Rng()) / RR.TreeBlockPerM;
            ar.Flying = true; ar.Done = false;
            Shots.Arrows++; w.V.Arrows++;
        }
        double Flight(Rules.BowR bow, double d, (double theta, double speed, bool high) aim)
        {
            var key = (bow, (int)Math.Round(d), (int)Math.Round(aim.speed * 100), (int)Math.Round(aim.theta * 1000));
            if (!flightCache.TryGetValue(key, out var t))
                flightCache[key] = t = Ballistics.FlightTime(Ballistics.V0(bow) * aim.speed, aim.theta, Ballistics.DragK(bow, R.Ranged), R.Ranged.Gravity, d, R.Ranged.Dt);
            return t;
        }

        void Fly(Shot ar, double subDt)
        {
            var RR = R.Ranged;
            double x0 = ar.X, y0 = ar.Y, z0 = ar.Z;
            var bow = RR.Bows[ar.W.Bow];
            Ballistics.Step(ref ar.X, ref ar.Y, ref ar.Z, ref ar.VX, ref ar.VY, ref ar.VZ, Ballistics.DragK(bow, RR), RR.Gravity, subDt);
            double x1 = ar.X, y1 = ar.Y, z1 = ar.Z;
            double g1 = GroundZ(x1, y1), tEnd = 1;
            bool ground = false;
            if (z1 <= g1)
            {
                double g0 = GroundZ(x0, y0);
                tEnd = z0 - g0 > 1e-9 ? Math.Min(1, Math.Max(0, (z0 - g0) / ((z0 - g0) - (z1 - g1)))) : 0;
                ground = true;
            }
            if (Geo != null && (x1 < 0 || y1 < 0 || x1 > Geo.W || y1 > Geo.H)) { Shots.Ground++; EndShot(ar); return; }   // улетела с карты
            // ветви под кронами (Г38)
            if (z1 < g1 + RR.CanopyHeight && Forest(x1, y1))
            {
                double seg = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0)) * tEnd;
                ar.CanopyLeft -= seg;
                if (ar.CanopyLeft <= 0) { Shots.Blocked++; EndShot(ar); return; }
            }
            if (Math.Min(z0, z1) <= Math.Max(GroundZ(x0, y0), g1) + RR.RiderTop + 0.1)
            {
                var near = new List<Body>();
                grid.Near(x0, y0, x1, y1, RR.HorseLength / 2 + 0.2, near);
                Body best = null; string bestPart = null; double bestT = tEnd + 1e-9;
                foreach (var body in near)
                {
                    if (!body.Alive || body == ar.Shooter) continue;
                    // первые 2 м полёта свои не задеваются (как в RangedSim): путь над своими проверен прицелом (Г40)
                    if (body.Owner == ar.W.Att.P && (x0 - ar.LX) * (x0 - ar.LX) + (y0 - ar.LY) * (y0 - ar.LY) < 4) continue;
                    if (Ballistics.Hit(body, RR, x0, y0, z0, x1, y1, z1, out var tt, out var part) && tt < bestT)
                    { bestT = tt; best = body; bestPart = part; }
                }
                if (best != null) { Struck(ar, best, bestPart); EndShot(ar); return; }
            }
            if (ground) { Shots.Ground++; EndShot(ar); }
        }
        void EndShot(Shot ar) { ar.Flying = false; ar.Done = true; ar.W.Landed++; }

        // Стрела задела тело: выбыл ли человек (броня стола), убит ли (часть тела, Г39); павший — в Deaths (Г67)
        void Struck(Shot ar, Body body, string part)
        {
            var w = ar.W; var RR = R.Ranged;
            var victim = Movers.First(m => m.P == body.Owner);
            Shots.Hits++; Shots.Parts[part]++;
            if (victim != w.Def) Shots.HitsFriendly++;
            var D = Combat.Eff(victim.P.U, Modes.RangedForm, null, Ctx);
            double eq = D.EqDef;
            double th = body.Facing * Math.PI / 180, fx = Math.Sin(th), fy = -Math.Cos(th);
            double hv = Math.Sqrt(ar.VX * ar.VX + ar.VY * ar.VY);
            if (hv > 1e-9 && (-ar.VX * fx - ar.VY * fy) / hv <= Math.Cos(R.Sectors.RearMin * Math.PI / 180)) eq *= R.Defense.RearEqMult;
            double pOut = 1 / Math.Max(R.Defense.MinDivisor, eq / R.Defense.RangedEqDiv);
            if (D.Cmdr != null && D.Cmdr.BuffDef != 0) pOut *= Math.Max(0, 1 - D.Cmdr.BuffDef / 100);
            if (Ctx.Rng() >= pOut) return;
            body.Alive = false;
            victim.P.U.Soldiers -= 1;
            victim.ShotDown++;
            double wPart = RR.PartLethality[part];
            Shots.Out++; Shots.OutPartWeight += wPart;
            var Lt = R.Lethality;
            double pct = Js.Clamp(Lt.Base + w.LRoll / (Math.Max(1, victim.P.U.Exp) / Lt.ExpDiv), 0, 100);
            bool killed = Ctx.Rng() * 100 < Math.Min(100, pct * wPart * RR.PartNorm);
            if (killed) Shots.Killed++;
            w.Victims.TryGetValue(victim, out var vv);
            w.Victims[victim] = (vv.cas + 1, vv.killed + (killed ? 1 : 0));
            if (victim == w.V.A) w.V.LossA++; else if (victim == w.V.B) w.V.LossB++;
            if (victim != w.Def) w.V.Friendly++;
            Deaths.Add(new Death
            {
                X = body.X, Y = body.Y, T = curT, Facing = body.Facing + (look() - 0.5) * 40, Dir = Math.Atan2(ar.VY, ar.VX) * 180 / Math.PI,
                UnitId = victim.P.U.Id, Part = part, Killed = killed,
            });
        }
        double curT;

        // Конец окна: БД по потерям — как после удара за столом
        void CloseVolleyWin(VWin w, double t)
        {
            w.Closed = true;
            int total = 0;
            foreach (var kv in w.Victims)
            {
                var def = kv.Key.P.U;
                int cas = kv.Value.cas; total += cas;
                var clone = def.Clone(); clone.Soldiers = def.Soldiers + cas;
                var res = new StrikeResult { Casualties = cas, Killed = kv.Value.killed, Wounded = cas - kv.Value.killed };
                Combat.CasualtyPatch(clone, res, new List<string>(), Ctx, out _).ApplyTo(def);
            }
            Details.Add($"{Js.Num(Js.R1(t - Clock))} с · {w.Att.P.U.Name} → {w.Def.P.U.Name}{(w.Counter ? " (ответ)" : "")}: стрел {w.Planned}, −{total}");
        }
    }
}
