// ═══════════ Ranged.cs — стрельба по баллистике за ход (И1; Г33, Г37–Г41) ═══════════
// Сколько стрел — по формуле стола (Г39): урон удара, как если бы у цели не было защиты, × VolleyK лука
// (подобран на опорной стычке, Г37). Стреляют окнами-обменами, как в рукопашной: атаки стрелка, ответ
// цели, если она сама стрелок. Каждая стрела летит сама (Ballistics); кого она встретит первым — врага
// или своего (Г40), — того и задела. Задетый выбывает с шансом брони стола 1 / (защита ÷ 10); часть тела
// сдвигает долю убитых (Г39). Лес — ветви на пути под кронами; холм — земля выше (Г41).
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class RangedOptions
    {
        public double TurnSec = 15, Dt = 0.1;
        public FortuneMode Fortune = FortuneMode.PerStrike;
        public Func<double, double, double> GroundZ;   // высота земли в точке, м; null — ровное поле
        public Func<double, double, bool> Forest;      // есть ли кроны над точкой; null — леса нет
    }

    // Что произошло со стрелами за ход — для отчёта и нормировки частей тела
    public sealed class ShotStats
    {
        public long Arrows, Hits, HitsFriendly, Out, Killed, Blocked, Ground, Unreachable, LowShots, HighShots;
        public long Building;   // Г103: воткнулись в постройку или вал
        public Dictionary<string, long> Parts = new Dictionary<string, long> { ["head"] = 0, ["torso"] = 0, ["legs"] = 0, ["horse"] = 0 };
        public double OutPartWeight;   // Σ веса части тела по выбывшим (Г39)
    }

    public static class RangedSim
    {
        sealed class Win
        {
            public Placed Att, Def;
            public bool Counter;
            public double T0, T1, U, N0, LRoll;
            public int Planned, Launched, Landed;
            public bool Open, Closed, Skipped;
            public List<Body> Shooters;
            public string Bow;
            public Dictionary<Placed, (int cas, int killed)> Victims = new Dictionary<Placed, (int, int)>();
        }

        sealed class Arrow
        {
            public Win W;
            public int Index;
            public Body Shooter;
            public double LaunchT, X, Y, Z, VX, VY, VZ, CanopyLeft, LX, LY;   // LX, LY — точка вылета
            public bool Flying, Done;
        }

        // Расстановка: B в (0, 0) лицом вверх, A — лицом к нему, передние края в s.Distance метрах; холм — A выше
        public static (Placed a, Placed b) Setup(Unit a0, Unit b0, TurnSetup s, Rules r)
        {
            var a = new Placed { U = a0.Clone() };
            var b = new Placed { U = b0.Clone() };
            a.U.OnMap = b.U.OnMap = false;
            a.Relayout(r, 10); b.Relayout(r, 10);
            a.X = 0; a.Y = -(b.Fp.Depth / 2 + s.Distance + a.Fp.Depth / 2); a.Facing = 180;
            if (s.Ground == Ground.Hill) a.Level = 1;
            return (a, b);
        }

        static List<Body> BodiesOf(Placed p, Rules r, Func<double, double, double> groundZ)
        {
            var list = new List<Body>();
            var fp = Formation.Of(p.U, r);
            foreach (var (lx, ly, file, rank) in Formation.MenPositions(p.U, r))
            {
                p.ToWorld(lx, ly, out var wx, out var wy);
                list.Add(new Body
                {
                    Owner = p, X = wx, Y = wy, Facing = p.Facing, Horse = Units.IsCav(p.U),
                    Ground = groundZ != null ? groundZ(wx, wy) : p.Level * r.Ranged.MetersPerLevel,
                    FrontDist = ly + fp.Depth / 2, File = file, Rank = rank,
                });
            }
            return list;
        }

        public static TurnOutcome Turn(Placed a, Placed b, EngineContext ctx, RangedOptions o, ShotStats st = null)
        {
            var R = ctx.Rules; var RR = R.Ranged;
            st ??= new ShotStats();
            var outc = new TurnOutcome();
            double startA = a.U.Soldiers, startB = b.U.Soldiers;
            const string mode = Modes.RangedForm;   // местность — физикой (Г38), а не режимом стола
            var opts = new BattleRequest { Mode = mode, FatigueMode = "percent" };
            double GroundAt(double x, double y) => o.GroundZ != null ? o.GroundZ(x, y) : 0;

            // ── тела и сетка ──
            var bodies = new Dictionary<Placed, List<Body>> { [a] = BodiesOf(a, R, o.GroundZ), [b] = BodiesOf(b, R, o.GroundZ) };
            var grid = new BodyGrid();
            void Rebuild()
            {
                grid.Clear();
                foreach (var kv in bodies) foreach (var x in kv.Value) if (x.Alive) grid.Add(x);
            }
            Rebuild();

            // ── обмены: атаки A (B отвечает, если стрелок), потом атаки B, если стрелок (A отвечает) ──
            var wins = new List<Win>();
            var exchanges = new List<(Placed x, Placed y)>();
            bool Shooter(Placed p) => p.U.Weapon == "ranged";
            if (Shooter(a)) for (int i = 0; i < Units.AttackLimit(a.U, R); i++) exchanges.Add((a, b));
            if (Shooter(b)) for (int i = 0; i < Units.AttackLimit(b.U, R); i++) exchanges.Add((b, a));
            var countersLeft = new Dictionary<Placed, int> { [a] = (int)Units.CounterLimit(a.U, R), [b] = (int)Units.CounterLimit(b.U, R) };
            double len = o.TurnSec / Math.Max(1, exchanges.Count);
            for (int j = 0; j < exchanges.Count; j++)
            {
                var (x, y) = exchanges[j];
                wins.Add(new Win { Att = x, Def = y, T0 = j * len, T1 = (j + 1) * len });
                if (Shooter(y) && countersLeft[y] > 0)
                {
                    countersLeft[y]--;
                    wins.Add(new Win { Att = y, Def = x, Counter = true, T0 = j * len, T1 = (j + 1) * len });
                }
            }

            var unitU = new Dictionary<Placed, double>();
            double Fortune(Unit u)
            {
                double n = Math.Max(1, Js.Round(u.Soldiers));
                var A = Combat.Eff(u, mode, null, ctx);
                return Math.Max(Dice.Roll(ctx.Rng, n), Js.Round(A.Discipline * R.RollFloorPerDisc)) / n;
            }
            if (o.Fortune == FortuneMode.PerTurn) { unitU[a] = Fortune(a.U); unitU[b] = Fortune(b.U); }

            // стрелы раздаются по всему строю поровну — а не передней шеренге первой
            Body ShooterFor(Win w, int i) => w.Shooters.Count == 0 ? null : w.Shooters[(int)((long)i * w.Shooters.Count / Math.Max(1, w.Planned)) % w.Shooters.Count];
            double RankDepthOf(Unit u) => R.Map.Formation.TryGetValue(u.Type, out var f) ? f.RankDepth : 1;

            var arrows = new List<Arrow>();     // ждут выстрела, по времени
            var flying = new List<Arrow>();
            int nextArrow = 0;
            var near = new List<Body>();
            var scratch = new List<string>();
            double subDt = RR.Dt;
            int sub = Math.Max(1, (int)Math.Round(o.Dt / subDt));
            subDt = o.Dt / sub;
            var effCache = new Dictionary<Placed, EffStats>();
            double partNorm = RR.PartNorm;

            void Launch(Arrow ar)
            {
                var w = ar.W;
                ar.Done = true;
                // сила залпа — по численности в начале окна, как в рукопашной: стрелу павшего выпускает живой сосед
                if (ar.Shooter != null && !ar.Shooter.Alive)
                    for (int step = 1; step < w.Shooters.Count && !ar.Shooter.Alive; step++)
                        ar.Shooter = w.Shooters[(ar.Index + step) % w.Shooters.Count];
                if (ar.Shooter == null || !ar.Shooter.Alive) { w.Landed++; return; }
                // цель — живой враг перед стрелком (до TargetSideM вбок); если таких нет — ближайший вбок из попробованных
                var targets = bodies[w.Def];
                var sh = ar.Shooter;
                double srx = Math.Cos(sh.Facing * Math.PI / 180), sry = Math.Sin(sh.Facing * Math.PI / 180);
                Body tgt = null, fallback = null; double fbSide = double.MaxValue;
                for (int tries = 0; tries < 40 && tgt == null; tries++)
                {
                    var c = targets[(int)Math.Floor(ctx.Rng() * targets.Count)];
                    if (!c.Alive) continue;
                    double side = Math.Abs((c.X - sh.X) * srx + (c.Y - sh.Y) * sry);
                    if (side <= RR.TargetSideM) tgt = c;
                    else if (side < fbSide) { fbSide = side; fallback = c; }
                }
                tgt ??= fallback;
                if (tgt == null) { w.Landed++; return; }
                var bow = RR.Bows[w.Bow];
                double sx = ar.Shooter.X, sy = ar.Shooter.Y;
                double sz = ar.Shooter.Ground + (ar.Shooter.Horse ? RR.HorseHeight + 0.7 : RR.LaunchHeight);   // конный стреляет с седла
                double dx = tgt.X - sx, dy = tgt.Y - sy, d = Math.Sqrt(dx * dx + dy * dy);
                double dz = tgt.Ground + RR.AimHeight - sz;
                var aim = Ballistics.AimCached(bow, RR, d, dz, ar.Shooter.FrontDist, RankDepthOf(w.Att.U), sz - ar.Shooter.Ground);
                if (aim == null) { st.Unreachable++; w.Landed++; return; }
                if (aim.Value.high) st.HighShots++; else st.LowShots++;
                double th = aim.Value.theta + Ballistics.Gauss(ctx.Rng) * bow.SigmaElevDeg * Math.PI / 180;
                double az = Math.Atan2(dy, dx) + Ballistics.Gauss(ctx.Rng) * bow.SigmaAzDeg * Math.PI / 180;
                double v = Ballistics.V0(bow) * aim.Value.speed * (1 + Ballistics.Gauss(ctx.Rng) * bow.SigmaSpeed);
                ar.X = sx; ar.Y = sy; ar.Z = sz; ar.LX = sx; ar.LY = sy;
                ar.VX = v * Math.Cos(th) * Math.Cos(az); ar.VY = v * Math.Cos(th) * Math.Sin(az); ar.VZ = v * Math.Sin(th);
                ar.CanopyLeft = o.Forest != null ? -Math.Log(1 - ctx.Rng()) / RR.TreeBlockPerM : double.PositiveInfinity;
                ar.Flying = true; ar.Done = false;
                st.Arrows++;
            }

            // Стрела задела тело: выбыл ли человек, убит ли
            void Struck(Arrow ar, Body body, string part)
            {
                var w = ar.W; var victim = body.Owner;
                st.Hits++; st.Parts[part]++;
                if (victim != w.Def) st.HitsFriendly++;
                if (!effCache.TryGetValue(victim, out var D)) effCache[victim] = D = Combat.Eff(victim.U, mode, null, ctx);
                double eq = D.EqDef;
                // стрела пришла сзади (≥ 135° от фасинга) — защита вполовину, как удар в тыл за столом
                double th = body.Facing * Math.PI / 180, fx = Math.Sin(th), fy = -Math.Cos(th);
                double hv = Math.Sqrt(ar.VX * ar.VX + ar.VY * ar.VY);
                if (hv > 1e-9 && (-ar.VX * fx - ar.VY * fy) / hv <= Math.Cos(R.Sectors.RearMin * Math.PI / 180)) eq *= R.Defense.RearEqMult;
                double pOut = 1 / Math.Max(R.Defense.MinDivisor, eq / R.Defense.RangedEqDiv);
                if (D.Cmdr != null && D.Cmdr.BuffDef != 0) pOut *= Math.Max(0, 1 - D.Cmdr.BuffDef / 100);
                if (ctx.Rng() >= pOut) return;
                body.Alive = false;
                victim.U.Soldiers -= 1;
                StepUp(body);
                double wPart = RR.PartLethality[part];
                st.Out++; st.OutPartWeight += wPart;
                var Lt = R.Lethality;
                double pct = Js.Clamp(Lt.Base + w.LRoll / (Math.Max(1, victim.U.Exp) / Lt.ExpDiv), 0, 100);
                bool killed = ctx.Rng() * 100 < Math.Min(100, pct * wPart * partNorm);
                if (killed) st.Killed++;
                w.Victims.TryGetValue(victim, out var vv);
                w.Victims[victim] = (vv.cas + 1, vv.killed + (killed ? 1 : 0));
            }

            void Fly(Arrow ar)
            {
                double x0 = ar.X, y0 = ar.Y, z0 = ar.Z;
                var bow = RR.Bows[ar.W.Bow];
                Ballistics.Step(ref ar.X, ref ar.Y, ref ar.Z, ref ar.VX, ref ar.VY, ref ar.VZ, Ballistics.DragK(bow, RR), RR.Gravity, subDt);
                double x1 = ar.X, y1 = ar.Y, z1 = ar.Z;
                double g1 = GroundAt(x1, y1);
                double tEnd = 1;
                bool ground = false;
                if (z1 <= g1)
                {
                    double g0 = GroundAt(x0, y0);
                    tEnd = z0 - g0 > 1e-9 ? Math.Min(1, Math.Max(0, (z0 - g0) / ((z0 - g0) - (z1 - g1)))) : 0;
                    ground = true;
                }
                // ветви под кронами: шанс удара растёт с длиной пути в лесу ниже крон
                if (o.Forest != null && z1 < g1 + RR.CanopyHeight && o.Forest(x1, y1))
                {
                    double seg = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0)) * tEnd;
                    ar.CanopyLeft -= seg;
                    if (ar.CanopyLeft <= 0) { st.Blocked++; End(ar); return; }
                }
                if (Math.Min(z0, z1) <= Math.Max(GroundAt(x0, y0), g1) + RR.RiderTop + 0.1)
                {
                    grid.Near(x0, y0, x1, y1, RR.HorseLength / 2 + 0.2, near);
                    Body best = null; string bestPart = null; double bestT = tEnd + 1e-9;
                    foreach (var body in near)
                    {
                        if (!body.Alive || body == ar.Shooter) continue;
                        // первые 2 м полёта свои не задеваются: на место павшего впереди шагают мгновенно (Г30),
                        // и шагнувший встал бы в точку вылета; путь над своими рядами проверен прицелом (Г40)
                        if (body.Owner == ar.W.Att && (x0 - ar.LX) * (x0 - ar.LX) + (y0 - ar.LY) * (y0 - ar.LY) < 4) continue;
                        if (Ballistics.Hit(body, RR, x0, y0, z0, x1, y1, z1, out var t, out var part) && t < bestT)
                        { bestT = t; best = body; bestPart = part; }
                    }
                    if (best != null) { Struck(ar, best, bestPart); End(ar); return; }
                }
                if (ground) { st.Ground++; End(ar); }
            }

            void End(Arrow ar) { ar.Flying = false; ar.Done = true; ar.W.Landed++; }

            void Close(Win w)
            {
                w.Closed = true;
                foreach (var kv in w.Victims)
                {
                    var def = kv.Key.U;
                    int cas = kv.Value.cas;
                    var clone = def.Clone(); clone.Soldiers = def.Soldiers + cas;
                    var res = new StrikeResult { Casualties = cas, Killed = kv.Value.killed, Wounded = cas - kv.Value.killed };
                    scratch.Clear();
                    Combat.CasualtyPatch(clone, res, scratch, ctx, out _).ApplyTo(def);
                    outc.Log.Add($"{Js.Num(Js.R1(w.T1))} с · {w.Att.U.Name} → {def.Name}{(w.Counter ? " (ответ)" : "")}: стрел {w.Planned}, −{cas}");
                }
                if (w.Victims.Count == 0) outc.Log.Add($"{Js.Num(Js.R1(w.T1))} с · {w.Att.U.Name} → {w.Def.U.Name}{(w.Counter ? " (ответ)" : "")}: стрел {w.Planned}, −0");
                outc.Strikes++;
                foreach (var kv in w.Victims) Reform(kv.Key);
                if (w.Victims.Count > 0) Rebuild();
            }

            // Г30: на место павшего шагает стоящий за ним в той же колонне, за тем — следующий; дыра уходит в хвост колонны
            void StepUp(Body dead)
            {
                var p = dead.Owner;
                var slot = new Dictionary<(int, int), Body>();
                foreach (var x in bodies[p]) if (x.Alive) slot[(x.File, x.Rank)] = x;
                double sx = dead.X, sy = dead.Y, sf = dead.FrontDist; int r = dead.Rank;
                while (slot.TryGetValue((dead.File, r + 1), out var next))
                {
                    double nx = next.X, ny = next.Y, nf = next.FrontDist;
                    grid.Move(next, sx, sy); next.FrontDist = sf; next.Rank = r;
                    sx = nx; sy = ny; sf = nf; r++;
                }
            }

            // Строй смыкается (Г30): тела заново по числу живых — задние встают на места павших, фронт сужается.
            // Ещё не выпущенные стрелы отряда переходят к новым телам — иначе стрела вылетит из чужого тела.
            void Reform(Placed p)
            {
                bodies[p] = BodiesOf(p, R, o.GroundZ);
                foreach (var ow in wins)
                    if (ow.Open && !ow.Closed && ow.Att == p) ow.Shooters = bodies[p].FindAll(x => x.Alive);
                for (int ai = nextArrow; ai < arrows.Count; ai++)
                    if (arrows[ai].W.Att == p) arrows[ai].Shooter = ShooterFor(arrows[ai].W, arrows[ai].Index);
            }

            outc.Timeline.Add(new[] { 0, a.U.Soldiers, b.U.Soldiers });
            int maxTicks = (int)Math.Round((o.TurnSec + 30) / o.Dt);
            for (int k = 0; k < maxTicks; k++)
            {
                double t0 = k * o.Dt, t1 = t0 + o.Dt;
                foreach (var w in wins)
                {
                    if (w.Open || w.Skipped || w.T0 >= t1 - 1e-9) continue;
                    if (w.Att.U.Status != "active" || w.Att.U.Soldiers <= 0 || w.Def.U.Soldiers <= 0) { w.Skipped = true; continue; }
                    if (w.Counter && w.Att.U.Morale < R.Morale.ShakenBelow) { w.Skipped = true; continue; }
                    w.Open = true;
                    w.U = o.Fortune == FortuneMode.PerTurn ? unitU[w.Att] : Fortune(w.Att.U);
                    w.N0 = w.Att.U.Soldiers;
                    w.LRoll = Dice.Roll(ctx.Rng, R.Lethality.Die);
                    w.Bow = Ballistics.BowKeyFor(w.Att.U);
                    w.Shooters = bodies[w.Att].FindAll(x => x.Alive);
                    // стрел в окне: урон удара без защиты цели × VolleyK лука (Г39)
                    var A = Combat.Eff(w.Att.U, mode, null, ctx);
                    var bare = new EffStats();
                    double dmg = Combat.StrikeDamage(w.Att.U, w.Def.U, A, bare, opts, null, ctx, false, 1, "", "front", null, w.U * w.N0);
                    w.Planned = (int)Js.Round(Math.Max(0, dmg) * RR.Bows[w.Bow].VolleyK);
                    for (int i = 0; i < w.Planned; i++)
                        arrows.Add(new Arrow
                        {
                            W = w, Index = i, Shooter = ShooterFor(w, i),
                            LaunchT = w.T0 + (i + 0.5) / w.Planned * (w.T1 - w.T0),
                        });
                    // очередь по времени выстрела; при равенстве — порядок добавления (сортировка устойчивая)
                    var rest = arrows.GetRange(nextArrow, arrows.Count - nextArrow);
                    var sorted = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(rest, x => x.LaunchT));
                    arrows.RemoveRange(nextArrow, arrows.Count - nextArrow);
                    arrows.AddRange(sorted);
                }
                effCache.Clear();
                for (int s = 0; s < sub; s++)
                {
                    double ts = t0 + s * subDt;
                    while (nextArrow < arrows.Count && arrows[nextArrow].LaunchT < ts + subDt)
                    {
                        var ar = arrows[nextArrow++];
                        ar.W.Launched++;
                        Launch(ar);
                        if (ar.Flying) flying.Add(ar);
                    }
                    foreach (var ar in flying) if (!ar.Done) Fly(ar);
                    flying.RemoveAll(x => x.Done);
                }
                if (nextArrow > 4096) { arrows.RemoveRange(0, nextArrow); nextArrow = 0; }
                foreach (var w in wins)
                    if (w.Open && !w.Closed && w.T1 <= t1 + 1e-9 && w.Launched >= w.Planned && w.Landed >= w.Planned) Close(w);
                if (Math.Abs(t1 - Math.Round(t1)) < 1e-9)
                {
                    outc.Timeline.Add(new[] { Math.Round(t1), a.U.Soldiers, b.U.Soldiers });
                }
                bool pending = false;
                foreach (var w in wins) if (!w.Skipped && !w.Closed) { pending = true; break; }
                if (t1 >= o.TurnSec - 1e-9 && !pending) break;
            }

            outc.LossA = startA - a.U.Soldiers; outc.LossB = startB - b.U.Soldiers;
            outc.MoraleA = a.U.Morale; outc.MoraleB = b.U.Morale;
            outc.StatusA = a.U.Status; outc.StatusB = b.U.Status;
            outc.Shots = st;
            return outc;
        }
    }
}
