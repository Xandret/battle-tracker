// ═══════════ MenBodies.cs — бойцы — тела (Б1: Г82, Г85, Г89; Г56–Г58 поштучно) — ЧЕРНОВИК ДО ГМа ═══════════
// Включается Rules.Move.MenBodies (Г92: старые фигурки-капсулы работают, пока новое их не догонит). Фигурка — колонна
// во всю глубину строя (Г85); её ведёт якорь (AX, AY): туда его тянет MoveSim.Desire — место в строю, охват, обход,
// бегство. Телами толкаются бойцы. Каждый шаг:
//   1) якорь идёт к желаемой скорости колонны с пределом разгона, в непроходимое не входит; от середины своих бойцов
//      дальше AnchorLeadM не уходит — бойцы упёрлись, встаёт и он;
//   2) каждый боец хочет к своему месту у якоря: скорость якоря + отставание за Tau (Г76), выпады передних в схватке
//      (Г78), толпа при бегстве (Г70), шаг на новое место (В14);
//   3) взгляд вперёд — с бойцами чужих отрядов, как было у фигурок (Г56–Г58): свой чужой отряд — уступает тот, кто
//      позже у места встречи (очередь отрядов, Г57); врагу уступают оба — стена о стену (Г89); стрелки и бегущие —
//      сквозь своих на половине скорости (Г56);
//   4) шаг: предел скорости и разгона, в непроходимое не входит — скользит вдоль;
//   5) перекрывшихся расталкивает (два прохода): свой отряд — пополам (толкотня, Г77), свой чужой — уступающего,
//      враг — того, кто шёл на другого (стоящего не теснят, Г58); за шаг — не дальше предела скорости × PushSpeedK;
//   6) колонна для боя — середина и скорость её живых бойцов (Г91); упёрлась доля бойцов HeldShare — центр строя
//      стоит (пробка), кому уступали — в журнал (как Bodies, шаг 4).
// Тело пешего — круг радиусом BodyShare шага в строю; конного — капсула вдоль курса, полудлина — HorseHalfShare глубины
// шеренги. Случайности нет — порядок обхода задан списками отрядов и бойцов.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public static class MenBodies
    {
        // тела шага — массивы на поток: смотрелка считает бой в фоне, пока игра считает свой ход в главном потоке
        [ThreadStatic] static Man[] tMan; [ThreadStatic] static int[] tMi; [ThreadStatic] static Mover[] tM;
        [ThreadStatic] static double[] tUx, tUy, tHalf, tRad, tX0, tY0, tDvx, tDvy, tVmax, tCap, tReach;
        [ThreadStatic] static double[] tX, tY;       // где боец сейчас — плотным массивом (сетка и расталкивание)
        [ThreadStatic] static double[] tLx, tLy;     // сдвиг места бойца от якоря на этом шаге (с растяжкой бегущей толпы) — для RefX, RefY
        [ThreadStatic] static List<int> gUsed;       // занятые клетки сетки
        [ThreadStatic] static List<int> leaving;   // Г81: враги, от которых отряд уходит из схватки (на шаг, по отряду); по потокам (Г111 п.7)
        [ThreadStatic] static byte[] tFlag;          // 1 — стрелок, 2 — сквозь своих (медленнее), 4 — сдвинут расталкиванием, 8 — сквозь свой строй (Through),
                                                     // 16 — конь в натиске (Г90), 32 — лежит (сбит с ног), 64 — конь, 128 — ждёт своей очереди (старт волной, Г84)
        [ThreadStatic] static Rules.MenR tMR;
        [ThreadStatic] static double tStepM;   // Г104: больше этого перепада высот — тела не толкаются
        [ThreadStatic] static bool[] unitStill;   // Г111 п.2: отряд стоит (нет приказа, дошёл, «держать») — свои сквозь него проходят, он прогибается
        [ThreadStatic] static bool tFriendSoft; [ThreadStatic] static double tDisorderSec;
        static bool StillUnit(Mover m) => !m.Fleeing && m.Vs < 0.1 && (m.Order == null || m.Done || m.Order.Kind == OrderKind.Hold);
        static readonly byte idWall = Terrain.Id("wall"), idTower = Terrain.Id("tower");
        [ThreadStatic] static bool[] tRigid;         // Г86: боец на жёстком месте — в сетку, взгляд вперёд и толкотню не входит
        public static int RigidMen, RigidUnits;      // Г86: сколько бойцов на жёстких местах и отрядов «вдали» на последнем шаге (для тестов и замеров)
        [ThreadStatic] static int[] tBlocked;        // номер отряда, которому уступил на этом шаге (0 — никому)
        [ThreadStatic] static bool[] tBlockedEnemy;
        [ThreadStatic] static int[] gNext, gHead;
        const double Cell = 1.5;                     // сетка соседей, м: запросы — по размеру тел, клетка — около шага в строю
        const int ViaEvery = 5;                      // как часто боец проверяет, пройти ли к месту напрямик, шагов

        enum Rel { Same, Ghost, Friend, Enemy, Tree }   // Tree — дерево (Б5): неподвижное, толкает только бойца, упором не считается
        // Сквозь свой строй (Г68): колонна, что возвращается из охвата или идёт в охват к своему месту у врага (дальше
        // WrapSolidM от него и ещё не бьётся), и колонна, чьё место в строю дальше ReformThroughM (после «сплотить» место
        // может оказаться на другом краю строя). Иначе такие колонны продираются сквозь свои же ряды — бойцов в десять раз
        // больше, чем было фигурок, и колонна ползёт по полметра в секунду
        const double WrapSolidM = 3, WrapLeadM = 6, ReformThroughM = 2;
        const double LagRefM = 3;   // боец дальше этого от своего места якорь не держит (догоняет, а не упёрся)
        static bool Through(FigState s) => s.Returning || (s.Wrap ? !s.Fighting && s.GoalM > WrapSolidM : !s.Fighting && s.GoalM > ReformThroughM);
        static bool SameSide(Unit a, Unit b) => a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId;
        internal static bool SameSidePublic(Unit a, Unit b) => SameSide(a, b);
        static Rel RelOf(int i, int j)
        {
            if (tMi[i] < 0 || tMi[j] < 0) return Rel.Tree;
            Mover a = tM[i], b = tM[j];
            if (((tFlag[i] | tFlag[j]) & 32) != 0) return Rel.Ghost;   // лежачего перешагивают (Г90)
            if (a == b) return (tFlag[i] & 8) != 0 || (tFlag[j] & 8) != 0 ? Rel.Ghost : Rel.Same;
            if (!SameSide(a.P.U, b.P.U)) return Rel.Enemy;
            return (tFlag[i] & 1) != 0 || (tFlag[j] & 1) != 0 || a.Fleeing || b.Fleeing ? Rel.Ghost : Rel.Friend;
        }

        [ThreadStatic] static HashSet<int> treeCells; [ThreadStatic] static double[] treeXs, treeYs;
        // Г108: круги, которые бойцы обходят и из которых их выталкивает (поединок командиров): (x, y, r); ставит бой перед шагом
        [ThreadStatic] static List<(double x, double y, double r)> obstacles;
        public static List<(double x, double y, double r)> Obstacles => obstacles ??= new List<(double x, double y, double r)>();   // по потокам (Г111 п.7: два боя могут шагать параллельно)
        public static void Step(IList<Mover> ms, double dt, Rules r, Geo geo = null)
        {
            var M = r.Move; var MR = r.Men; tMR = MR; tStepM = r.Garrison.StepM; tFriendSoft = M.FriendSoft; tDisorderSec = M.DisorderSec;
            if (unitStill == null || unitStill.Length < ms.Count) unitStill = new bool[Math.Max(16, ms.Count * 2)];
            for (int q = 0; q < ms.Count; q++) unitStill[q] = StillUnit(ms[q]);
            // Б5: клетки леса рядом с бойцами — их деревья войдут в тела этого шага
            var map = geo?.Map; int forestId = Terrain.Id("forest"); int nt = 0;
            if (map != null && MR.TreesPerCell > 0)
            {
                treeCells ??= new HashSet<int>(); treeCells.Clear();
                foreach (var m in ms)
                    foreach (var man in m.Men)
                    {
                        if (!man.Alive) continue;
                        int cx = (int)(man.X / Terrain.CellM), cy = (int)(man.Y / Terrain.CellM);
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int x = cx + dx, y = cy + dy;
                                if (x < 0 || y < 0 || x >= map.W || y >= map.H) continue;
                                int cell = y * map.W + x;
                                if (map.T[cell] == forestId) treeCells.Add(cell);
                            }
                    }
                nt = treeCells.Count * MR.TreesPerCell;
            }
            nt += Obstacles.Count;
            long pt = Prof.Now();
            Bodies.TurnAxes(ms, dt, r);   // колонна в охвате разворачивается лицом к врагу постепенно (Г68), как фигурка
            int every = Math.Max(1, (int)Math.Round(MR.BalanceSec / dt));
            int n = 0;
            foreach (var m in ms)
            {
                if (m.Men.Count > 0 && m.Steps % every == 0) { Soldiers.Balance(m, r); Soldiers.Settle(m, r); }   // смыкание между колоннами (В14); обмен мест в стоящей колонне
                foreach (var s in m.Figs) { double hd = Soldiers.FigHeading(m, s), h = hd * Math.PI / 180; s.Hc = Math.Cos(h); s.Hs = Math.Sin(h); s.Hd = hd; }
                foreach (var man in m.Men) if (man.Alive && man.Fig != null) n++;
            }

            Prof.Add(10, ref pt);
            // 1) якоря колонн
            foreach (var m in ms)
            {
                double amax = MoveSim.FigAccel(m, r) * dt; var F = m.Field;
                foreach (var s in m.Figs)
                {
                    double vmax = s.Vmax, dvx = s.Dvx, dvy = s.Dvy, dv = JsMath.Hypot(dvx, dvy);
                    if (dv > vmax) { dvx *= vmax / dv; dvy *= vmax / dv; }
                    double ax = dvx - s.AVx, ay = dvy - s.AVy, a = JsMath.Hypot(ax, ay);
                    if (a > amax) { ax *= amax / a; ay *= amax / a; }
                    double vx = s.AVx + ax, vy = s.AVy + ay;
                    // отстало большинство (бойцы застряли в толпе или в лесу) — якорь не быстрее них вдоль своего хода, иначе колонна
                    // растягивается на десятки метров; назад не прыгает (так было хуже: прыжок якоря — прыжок мест)
                    if (r.Men.LagMajorityHold && s.LagN * 2 > s.RefN && s.MenN > 0)
                    {
                        double vl = JsMath.Hypot(vx, vy);
                        if (vl > 1e-9) { double cap = Math.Max(r.Men.LagCrawlMps, (s.Vx * vx + s.Vy * vy) / vl); if (vl > cap) { vx *= cap / vl; vy *= cap / vl; } }
                    }
                    double nx = s.AX + vx * dt, ny = s.AY + vy * dt;
                    if (F != null && !MoveSim.Free(F, nx, ny) && MoveSim.Free(F, s.AX, s.AY))   // уже в непроходимом (поставлен на дом) — выходит
                    {
                        if (MoveSim.Free(F, nx, s.AY)) ny = s.AY;
                        else if (MoveSim.Free(F, s.AX, ny)) nx = s.AX;
                        else { nx = s.AX; ny = s.AY; }
                    }
                    bool held = false;
                    if (s.RefN > 0)
                    {
                        // где был бы якорь по бойцам на их местах (а не середина бойцов) — иначе колонна с бойцами только спереди
                        // (потери) или с лишними рядами сзади (перебор) сама себя тащит, а пересаживающиеся шагом держат её на месте
                        // отстало большинство (толпа не пускает) — якорь ждёт всех, а не бежит один: иначе колонна в охвате растягивается
                        // на десятки метров, бойцы гонятся за якорем сквозь схватку и мечутся
                        double bx = s.RefX, by = s.RefY;
                        // колонна на пути в охват или из него (сквозь свой строй) — манёвр, а не стена: якорь отрывается дальше,
                        // иначе колонна ползёт со скоростью, с какой бойцы догоняют якорь
                        double lead = Through(s) ? WrapLeadM : M.AnchorLeadM;
                        double ex = nx - bx, ey = ny - by, el = JsMath.Hypot(ex, ey);
                        if (el > lead) { nx = bx + ex * lead / el; ny = by + ey * lead / el; held = true; }
                    }
                    double avx = (nx - s.AX) / dt, avy = (ny - s.AY) / dt;
                    if (held)
                    {
                        // якорь держат бойцы: бойцам передаём только ту часть его хода, что идёт куда он сам хочет. Иначе петля:
                        // бойцов отжали назад — якорь за ними — бойцы прибавляют его ход к своему — колонна разгоняется прочь
                        double il = JsMath.Hypot(vx, vy), along = il > 1e-9 ? (avx * vx + avy * vy) / il : 0;
                        along = Math.Max(0, Math.Min(il, along));
                        avx = il > 1e-9 ? vx / il * along : 0; avy = il > 1e-9 ? vy / il * along : 0;
                    }
                    s.AVx = avx; s.AVy = avy; s.AX = nx; s.AY = ny;
                    bool mv = avx * avx + avy * avy > 0.25;
                    double tnow = m.Steps * dt;
                    // тронулась после стоянки не короче WaveAfterStopSec — волна; после рывка на миг — нет (иначе бой дёргал бы строй)
                    if (mv && !s.Moving) s.StartT = tnow - s.StopT >= r.Men.WaveAfterStopSec ? tnow : double.NegativeInfinity;
                    if (!mv && s.Moving) s.StopT = tnow;
                    s.Moving = mv;
                }
            }
            Prof.Add(11, ref pt);
            if (n == 0) { Finish(ms, dt, r, 0, geo); return; }

            // Г86: отряд вдали от врага и от других своих, не под стрелами, не бежит — его бойцы у своих мест идут одним телом
            var unitRigid = new bool[ms.Count];
            for (int i = 0; i < ms.Count; i++)
            {
                var a = ms[i];
                if (a.Fleeing || a.Men.Count == 0 || a.Now < a.UnderFireUntil) continue;
                double ra = JsMath.Hypot(a.P.Fp.Front, a.P.Fp.Depth) / 2; bool ok = true;
                for (int j = 0; j < ms.Count && ok; j++)
                {
                    if (j == i) continue;
                    var b = ms[j];
                    if (b.Figs.Count == 0) continue;
                    double d = JsMath.Hypot(a.P.X - b.P.X, a.P.Y - b.P.Y) - ra - JsMath.Hypot(b.P.Fp.Front, b.P.Fp.Depth) / 2;
                    if (d < (SameSide(a.P.U, b.P.U) ? MR.FarFriendM : MR.FarEnemyM)) ok = false;
                }
                unitRigid[i] = ok && !a.Reforming;   // перестраивается (узость, выбор построения Г101) — жёстких нет: вставшие на места стеной не пускали бы идущих
            }
            RigidUnits = 0; foreach (var ok in unitRigid) if (ok) RigidUnits++;
            RigidMen = 0;
            // 2) тела и желание каждого бойца
            if (tMan == null || tMan.Length < n + nt)
            {
                int cap = (n + nt) * 2;
                tMan = new Man[cap]; tMi = new int[cap]; tM = new Mover[cap];
                tUx = new double[cap]; tUy = new double[cap]; tHalf = new double[cap]; tRad = new double[cap]; tX0 = new double[cap]; tY0 = new double[cap];
                tDvx = new double[cap]; tDvy = new double[cap]; tVmax = new double[cap]; tCap = new double[cap]; tReach = new double[cap];
                tFlag = new byte[cap]; tBlocked = new int[cap]; tBlockedEnemy = new bool[cap]; gNext = new int[cap];
                tX = new double[cap]; tY = new double[cap]; tLx = new double[cap]; tLy = new double[cap]; tRigid = new bool[cap];
            }
            int c = 0; double maxBody = 0;
            double laF = M.LookAheadSec;
            for (int mi = 0; mi < ms.Count; mi++)
            {
                var m = ms[mi];
                if (m.Men.Count == 0) continue;
                var f = r.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : r.Map.Formation["infantry"];
                bool horse = BattleMap.IsHorse(m.P.U), archer = m.P.U.Type == "archer";
                double rad = MR.BodyShare * Math.Min(f.PerMan, f.RankDepth), halfBase = Soldiers.BodyHalf(m, r, 1);   // Г87: k человек — капсула вдоль колонны (по людям тела)
                double time = m.Steps * dt;
                // Г81: отступающая пехота пятится лицом к врагу; конь назад не пятится — развернётся (Г94)
                bool retreating = m.Order != null && m.Order.Kind == OrderKind.Retreat && !m.Done, backing = !horse && retreating;
                double turn = (horse ? MR.HorseTurnDegPerSec : MR.FootTurnDegPerSec) * dt;
                double backMax = horse ? MR.HorseBackMps : MR.FootBackMps, sideMax = horse ? MR.HorseSideMps : MR.FootSideMps;
                bool uRigid = unitRigid[mi];
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Fig == null) continue;
                    var s = man.Fig;
                    bool down = man.DownLeft > 0;
                    if (down) man.DownLeft = Math.Max(0, man.DownLeft - dt);
                    double lx = man.Lx, ly = man.Ly, hu = man.Hu, half = halfBase + (man.Men - 1) * f.RankDepth / 2;
                    bool inForest = map != null && map.T[Math.Min(map.W * map.H - 1, Math.Max(0, (int)(man.Y / Terrain.CellM) * map.W + (int)(man.X / Terrain.CellM)))] == forestId;   // Б5: в лесу жёстких нет
                    // старт волной (Iron Kings): колонна тронулась — первыми те, чьи места ближе к цели хода (вперёд — передний ряд,
                    // задом или вбок — соответствующий край), каждый следующий ряд через WaveRowSec; ждущий стоит. Считать от ряда 0
                    // нельзя: на отступлении передний ряд врезался бы в ещё ждущие задние и толкотня отбрасывала его назад
                    bool waiting = false;
                    if (!m.Fleeing && s.Moving)
                    {
                        double al = Math.Max(1e-9, Math.Sqrt(s.AVx * s.AVx + s.AVy * s.AVy));
                        double ahead = ((lx * s.Hc - ly * s.Hs) * s.AVx + (lx * s.Hs + ly * s.Hc) * s.AVy) / al;   // место бойца впереди якоря по ходу, м
                        double wave = Math.Max(0, (f.Ranks * f.RankDepth / 2 - ahead) / f.RankDepth) * MR.WaveRowSec;
                        waiting = time - s.StartT < wave;
                    }
                    if (m.Fleeing)
                    {
                        // Г84: толпа рассыпается за FleeScatterMin…Max с — задний ряд бежит сразу, передний последним; до срока боец стоит,
                        // где стоял (передние ещё держат строй), потом места толпы расходятся плавно
                        double scatter = MR.FleeScatterMin + (MR.FleeScatterMax - MR.FleeScatterMin) * hu;
                        double release = m.FleeSince + scatter * (1 - (man.Row + 0.5) / Math.Max(1, f.Ranks));
                        double ramp = Js.Clamp((m.Now - release) / MR.FleeScatterRampSec, 0, 1);
                        if (ramp <= 0) waiting = true;
                        double sp = 1 + (MR.FleeSpread - 1) * ramp;
                        lx *= sp; ly *= sp;
                        lx += Math.Sin(time * 2 * Math.PI / (2 + hu) + hu * 6.283) * MR.FleeWanderM * ramp;
                    }
                    double hx = s.AX + lx * s.Hc - ly * s.Hs, hy = s.AY + lx * s.Hs + ly * s.Hc;
                    // Б5: место в непроходимом (хвост колонны в доме, в воде) — ближайшая точка ближайшей проходимой клетки: боец не лезет
                    // в стену и не стоит в доме, куда его поставили
                    var Fm = m.Field;
                    if (Fm != null && !MoveSim.Free(Fm, hx, hy))
                    {
                        int pc = Fm.NearestPassableCached(Fm.CellOf(hx, hy));
                        if (pc >= 0)
                        {
                            var cc = Fm.CenterOf(pc); double hw = Fm.CellW / 2 - rad, hh = Fm.CellH / 2 - rad;
                            hx = Math.Max(cc.x - hw, Math.Min(cc.x + hw, hx)); hy = Math.Max(cc.y - hh, Math.Min(cc.y + hh, hy));
                        }
                    }
                    if (!double.IsNaN(man.PostX)) { hx = man.PostX; hy = man.PostY; }   // Г108: пост (поединок) — вместо места в строю
                    // выпады (Г78): у кого свой противник (Б2) — к нему; передние бьющейся колонны без него — к врагу колонны.
                    // Отступающий (Г81) не выпадает: шаг к врагу — это упор в него, и отряд сам себя держал бы в схватке
                    var foe = man.Foe;
                    if (!m.Fleeing && !retreating && (foe != null && foe.Alive || s.Fighting && man.Row == 0))
                    {
                        double lunge = (horse ? MR.HorseLungeK : 1) * (MR.LungeM + MR.LungeAmpM * Math.Sin(time * 2 * Math.PI / MR.LungeSec + hu * 6.283));   // конь всем телом не выпадает
                        double ux = s.FightX, uy = s.FightY;
                        if (foe != null && foe.Alive)
                        {
                            double fx = foe.X - man.X, fy = foe.Y - man.Y, fl = JsMath.Hypot(fx, fy);
                            if (fl > 1e-9) { ux = fx / fl; uy = fy / fl; }
                        }
                        hx += ux * lunge; hy += uy * lunge;
                    }
                    double cx = (hx - man.X) / MR.Tau, cy = (hy - man.Y) / MR.Tau;
                    // отступающий (Г81) к врагу не шагает, даже если место чуть впереди (якорь ушёл за выпадами): шаг к врагу —
                    // упор в него, и отряд держал бы сам себя в схватке
                    if (retreating && s.Fighting) { double ad = cx * s.FightX + cy * s.FightY; if (ad > 0) { cx -= ad * s.FightX; cy -= ad * s.FightY; } }
                    // В14: новое или далёкое место — шагом или трусцой сверх хода колонны
                    double far = JsMath.Hypot(hx - man.X, hy - man.Y);
                    // к месту напрямик не пройти (строй сложился у моста, а боец за водой) — по карте направлений отряда,
                    // как шла фигурка (MoveSim.Desire); проверка — раз в ViaEvery шагов, у каждого бойца в свой шаг
                    var F = m.Field;
                    if (F != null && far > 2 && (m.Steps + man.Id) % ViaEvery == 0)
                    {
                        man.ViaX = double.NaN;
                        if (MoveSim.Free(F, man.X, man.Y) && double.IsInfinity(F.SegmentCost(man.X, man.Y, hx, hy)))
                        {
                            int next = F.Next(F.CellOf(man.X, man.Y));
                            if (next >= 0) { var cc = F.CenterOf(next); man.ViaX = cc.x; man.ViaY = cc.y; }
                        }
                    }
                    else if (far <= 2) man.ViaX = double.NaN;
                    if (!double.IsNaN(man.ViaX))
                    {
                        double ex = man.ViaX - man.X, ey = man.ViaY - man.Y, el = Math.Max(1e-9, JsMath.Hypot(ex, ey)), vv = Math.Max(s.Vmax, MR.WalkMin);
                        cx = ex / el * vv - s.AVx; cy = ey / el * vv - s.AVy;
                    }
                    // шагом — только на новое место после потерь (Reseat ставит раскладка, В14); перестроение строя и отставание —
                    // в полную силу: мгновенного подтягивания у тел нет, так что «далёкий — трусцой» тут не нужен
                    // колонна в охвате идёт к своему месту у врага — бойцы бегут с ней, пересадка шагом тут не к месту: якорь
                    // впереди на AnchorLeadM, и «далеко от места» не кончалось бы, пока колонна идёт
                    if (man.Reseat && (s.Wrap || s.Returning)) man.Reseat = false;
                    if (man.Reseat)
                    {
                        if (far < MR.ReseatM / 2) man.Reseat = false;
                        else
                        {
                            double lim = m.Fleeing || far > MR.ReseatRunM ? MR.ReseatRunMps : MR.ReseatMps, cc = Math.Sqrt(cx * cx + cy * cy);
                            if (cc > lim) { cx *= lim / cc; cy *= lim / cc; }
                        }
                    }
                    // Г86: одним телом с колонной — замораживается там, где стоит (сдвиг от якоря в осях колонны), без прыжка на место;
                    // место догонит, когда оттает. Курс — по Г94, как у всех
                    bool atHome = far < MR.RigidSnapM && (F == null || MoveSim.Free(F, hx, hy));
                    bool rigid = uRigid && atHome && !s.Fighting && !s.Wrap && !s.Returning && !man.Reseat && !down && !waiting && !man.Thaw && !inForest && double.IsNaN(man.PostX);
                    man.Thaw = false;
                    if (rigid)
                    {
                        // сдвиг — от якоря до его хода в этом шаге (якорь уже сдвинут на AV·dt): иначе боец на шаг застывал бы на месте
                        if (!man.WasRigid) { double dx = man.X - (s.AX - s.AVx * dt), dy = man.Y - (s.AY - s.AVy * dt); man.RLx = dx * s.Hc + dy * s.Hs; man.RLy = -dx * s.Hs + dy * s.Hc; }
                        // замороженная точка должна быть на суше (место — рядом, но не то же)
                        if (F != null && !MoveSim.Free(F, s.AX + man.RLx * s.Hc - man.RLy * s.Hs, s.AY + man.RLx * s.Hs + man.RLy * s.Hc)) rigid = false;
                        else { lx = man.RLx; ly = man.RLy; cx = 0; cy = 0; }
                    }
                    man.WasRigid = rigid;
                    double vx = s.AVx + cx, vy = s.AVy + cy, vmax = Math.Max(s.Vmax, MR.WalkMin) * MR.SpeedK, v = JsMath.Hypot(vx, vy);
                    if (v > vmax) { vx *= vmax / v; vy *= vmax / v; v = vmax; }
                    if (waiting) { vx = 0; vy = 0; v = 0; }
                    // Г94: курс тела — к нужному не быстрее turn за шаг; вбок и назад относительно курса — медленно. Колонна стоит, боец
                    // у места — переступает (конь вбок и назад как пеший), не разворачиваясь: иначе пробка у мест не рассасывается
                    bool standing = !m.Fleeing && !s.Moving && far <= MR.StandShuffleM;
                    double want;
                    if (m.Fleeing) want = v > 0.5 ? MoveSim.HeadingOf(vx, vy) : man.Facing;
                    else if (foe != null && foe.Alive) want = MoveSim.HeadingOf(foe.X - man.X, foe.Y - man.Y);
                    else if (waiting) want = man.Facing;   // ждёт своей очереди — стоит как стоял
                    else if (man.Vx * man.Vx + man.Vy * man.Vy > MR.FaceMoveMps * MR.FaceMoveMps && Math.Abs(MoveSim.AngleDiff(man.Facing, s.Hd)) > 20 && !(far > MR.FaceMoveM || v > MR.FaceMoveMps))
                    {
                        // у места, но ещё идёт (не толкотня — быстрее FaceMoveMps) не по курсу строя: сперва тормозит, держа курс,
                        // развернётся, сбавив ход (Г94); при толкотне доворот не ждёт — иначе в плотном строю не развернуться
                        want = man.Facing; vx = 0; vy = 0; v = 0;
                    }
                    else if (!backing && ((far > MR.FaceMoveM || v > MR.FaceMoveMps) && !standing || man.Vx * man.Vx + man.Vy * man.Vy > MR.FaceMoveMps * MR.FaceMoveMps))
                    {
                        // по ходу. Ещё бежит, а хочет стоять или назад от своего хода — сперва тормозит, глядя по своему ходу, потом
                        // разворачивается: иначе конь на скаку смотрел бы уже назад, а нёсся вперёд — «задом»
                        bool braking = man.Vx * man.Vx + man.Vy * man.Vy > MR.FaceMoveMps * MR.FaceMoveMps && (v < 0.5 || vx * man.Vx + vy * man.Vy < 0);
                        want = braking ? MoveSim.HeadingOf(man.Vx, man.Vy) : MoveSim.HeadingOf(vx, vy);
                    }
                    else want = double.IsNaN(man.PostFacing) ? s.Hd : man.PostFacing;   // Г108: на посту смотрит, куда велено
                    double dh = MoveSim.AngleDiff(man.Facing, want);
                    man.Facing = MoveSim.Norm(Math.Abs(dh) <= turn ? want : man.Facing + Math.Sign(dh) * turn);
                    double fh = man.Facing * Math.PI / 180, ufx = Math.Sin(fh), ufy = -Math.Cos(fh);
                    double fwd = vx * ufx + vy * ufy, side = -vx * ufy + vy * ufx;   // вперёд и вправо (вправо — (−ufy, ufx))
                    double back = backing ? vmax : standing ? Math.Max(backMax, MR.StandBackMps) : backMax, sideLim = standing ? Math.Max(sideMax, MR.FootSideMps) : sideMax;
                    if (inForest) { back = Math.Max(back, MR.FootBackMps); sideLim = Math.Max(sideLim, MR.FootSideMps); }   // Б5: в лесу конь переступает между стволами, как пеший
                    if (fwd < -back) fwd = -back;
                    if (side > sideLim) side = sideLim; else if (side < -sideLim) side = -sideLim;
                    vx = ufx * fwd - ufy * side; vy = ufy * fwd + ufx * side;
                    if (down) { vx = 0; vy = 0; }   // лежит: не идёт (Г90)
                    tMan[c] = man; tMi[c] = mi; tM[c] = m; tLx[c] = lx; tLy[c] = ly; tRigid[c] = rigid; if (rigid) RigidMen++;
                    // капсула коня для толкотни лежит вдоль колонны (бегущего — по курсу): развернуть разом длинные тела в плотном
                    // строю — значит раскидать соседей; курс тела (Г94) — для хода и рисунка
                    double ph = m.Fleeing || inForest || horse && (M.HorseBodyOwnCourse || M.HorseBodyOwnCourseInMelee && (man.Foe != null || s.Fighting)) ? fh : s.Hd * Math.PI / 180;   // Б5: в лесу — по своему курсу: иначе конь не повернётся между стволами
                    tUx[c] = Math.Sin(ph); tUy[c] = -Math.Cos(ph); tHalf[c] = half; tRad[c] = rad; man.BodyFacing = ph * 180 / Math.PI;
                    tX0[c] = man.X; tY0[c] = man.Y; tX[c] = man.X; tY[c] = man.Y; tDvx[c] = vx; tDvy[c] = vy; tVmax[c] = vmax;
                    // расталкивание — не быстрее PushMaxMps, как бы ни был скор сам боец: иначе конь на полном ходу, врезавшись,
                    // отлетал на 2,8 м за шаг — рывок
                    tCap[c] = Math.Min(Math.Max(vmax, 1), MR.PushMaxMps) * dt * M.PushSpeedK;
                    if (!m.ChargeReady) man.Knocks = 0;   // натиска нет — счёт сбитых заново
                    // вламывается передний ряд колонны (Г90: на 1–2 шеренги — колонной, а не каждый конь по своей паре: иначе задние
                    // ряды брали бы шеренгу за шеренгой и прошли строй насквозь); задние — за ним, у стены
                    bool charge = horse && m.ChargeReady && !m.Fleeing && man.Row == 0 && man.Knocks < MR.ChargeKnocks && man.Vx * man.Vx + man.Vy * man.Vy >= MR.ChargeMinMps * MR.ChargeMinMps;
                    tFlag[c] = (byte)((archer ? 1 : 0) | (Through(s) ? 8 : 0) | (charge ? 16 : 0) | (down ? 32 : 0) | (horse ? 64 : 0) | (waiting ? 128 : 0)); tBlocked[c] = 0; tBlockedEnemy[c] = false;
                    // конь смотрит дальше на длину острия пики — иначе медленно подходя, острий не видел бы (Г90)
                    tReach[c] = half + rad + Math.Max(JsMath.Hypot(vx, vy), JsMath.Hypot(man.Vx, man.Vy)) * laF + M.MenYieldM + (horse ? MR.PikeTipM : 0);
                    if (half + rad > maxBody) maxBody = half + rad;
                    c++;
                }
            }

            // Б5: деревья — неподвижные тела после бойцов (индексы c…ct): в сетках взгляда и толкотни, в счёте бойцов — нет
            int ct = c;
            if (nt > 0)
            {
                if (treeXs == null || treeXs.Length < MR.TreesPerCell) { treeXs = new double[Math.Max(16, MR.TreesPerCell)]; treeYs = new double[treeXs.Length]; }
                foreach (int cell in treeCells)
                {
                    int k = Terrain.Trees(map, cell, r, treeXs, treeYs);
                    for (int t = 0; t < k; t++)
                    {
                        tX[ct] = tX0[ct] = treeXs[t]; tY[ct] = tY0[ct] = treeYs[t]; tRad[ct] = MR.TreeRadiusM; tHalf[ct] = 0; tUx[ct] = 1; tUy[ct] = 0;
                        tFlag[ct] = 0; tRigid[ct] = true; tMi[ct] = -3; tM[ct] = null; tMan[ct] = null; tBlocked[ct] = 0; tBlockedEnemy[ct] = false;
                        tReach[ct] = MR.TreeRadiusM; tVmax[ct] = 0; tCap[ct] = 0; tDvx[ct] = 0; tDvy[ct] = 0; tLx[ct] = 0; tLy[ct] = 0;
                        ct++;
                    }
                }
                if (MR.TreeRadiusM > maxBody) maxBody = MR.TreeRadiusM;
            }
            foreach (var (ox, oy, orad) in Obstacles)
            {
                // Г108: круг поединка — как ствол, только большой и держит и коней
                tX[ct] = tX0[ct] = ox; tY[ct] = tY0[ct] = oy; tRad[ct] = orad; tHalf[ct] = 0; tUx[ct] = 1; tUy[ct] = 0;
                tFlag[ct] = 0; tRigid[ct] = true; tMi[ct] = -4; tM[ct] = null; tMan[ct] = null; tBlocked[ct] = 0; tBlockedEnemy[ct] = false;
                tReach[ct] = orad; tVmax[ct] = 0; tCap[ct] = 0; tDvx[ct] = 0; tDvy[ct] = 0; tLx[ct] = 0; tLy[ct] = 0;
                ct++;
                if (orad > maxBody) maxBody = orad;
            }
            Prof.Add(12, ref pt);
            // 3) взгляд вперёд — с бойцами чужих отрядов: каждый смотрит на свой ход вперёд (кто быстрее — увидит сам).
            // Клетки, где только свои, пропускаются целиком — в глубине строя взгляд вперёд ничего не стоит
            Grid(ct, Cell);
            Coarse(ct);
            for (int i = 0; i < c; i++)
            {
                if (tRigid[i]) continue;   // Г86: на жёстком месте — ни на кого не смотрит
                double reach = tReach[i] + maxBody, xi = tX[i], yi = tY[i]; int own = tMi[i];
                if (OnlyOwn(xi, yi, reach, own)) continue;   // в округе — только свои: взгляд вперёд не нужен
                Prof.N[5]++; long seen = 0;
                int cx0 = Math.Max(0, (int)((xi - reach - gx0) / gCell)), cx1 = Math.Min(gW - 1, (int)((xi + reach - gx0) / gCell));
                int cy0 = Math.Max(0, (int)((yi - reach - gy0) / gCell)), cy1 = Math.Min(gH - 1, (int)((yi + reach - gy0) / gCell));
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        int k = cy * gW + cx, o = gOwner[k];
                        if (o == -1 || o == own) continue;
                        for (int j = gHead[k]; j >= 0; j = gNext[j])
                        {
                            if (tMi[j] == own) continue;
                            // сблизиться за взгляд могут лишь те, кто сейчас не дальше суммы досягаемостей (тела + ход за взгляд + зазор)
                            double ddx = tX[j] - xi, ddy = tY[j] - yi, rr = tReach[i] + tReach[j];
                            if (ddx * ddx + ddy * ddy > rr * rr) continue;
                            seen++;
                            var rel = RelOf(i, j);
                            if (rel == Rel.Ghost)
                            {
                                if (Dist(i, xi, yi, j, tX[j], tY[j], out _, out _) < 0) tFlag[i] |= 2;
                                continue;
                            }
                            if (rel == Rel.Tree)
                            {
                                if (tMi[j] == -4 && tMan[i] != null && tMan[i].InDuel) continue;   // Г108: полководец идёт в круг поединка, не обтекает его
                                // дерево (Б5): шаг, что ведёт в ствол, убирается — боец обтекает его; упором не считается
                                double dT = Dist(i, xi + tDvx[i] * MR.TreeLookSec, yi + tDvy[i] * MR.TreeLookSec, j, tX[j], tY[j], out double tnx, out double tny);
                                if (dT < M.MenYieldM) Yield(i, tnx, tny, j, false, false);
                                continue;
                            }
                            bool enemy = rel == Rel.Enemy;
                            if (enemy && (tFlag[i] & 16) != 0 && (tFlag[j] & 64) == 0) continue;   // натиск (Г90): пешему не уступает
                            double la = enemy ? M.EnemyLookAheadSec : laF;
                            double ax = xi + tDvx[i] * la, ay = yi + tDvy[i] * la, bx = tX[j] + tDvx[j] * la, by = tY[j] + tDvy[j] * la;
                            double d = Dist(i, ax, ay, j, bx, by, out double nx, out double ny);   // n — от j к i
                            double yieldM = M.MenYieldM;
                            if (enemy && (tFlag[i] & 64) != 0 && PikeAt(j, i))
                            {
                                // пики во фронт — конь встаёт у острия (Г90): у острия гасит свой ход к пикинёру разом, а не тормозит
                                yieldM += MR.PikeTipM;
                                double d0 = Dist(i, xi, yi, j, tX[j], tY[j], out double qx, out double qy);
                                var hm = tMan[i]; double ap = hm.Vx * qx + hm.Vy * qy;   // q — от пикинёра к коню
                                if (d0 < MR.PikeTipM + M.MenYieldM && ap < 0) { hm.Vx -= ap * qx; hm.Vy -= ap * qy; }
                            }
                            if (d >= yieldM) continue;
                            if (!enemy && tFriendSoft && unitStill[tMi[j]] && !unitStill[tMi[i]])
                            {
                                // Г111 п.2: идущий сквозь стоящий свой строй не уступает — просачивается на половине хода (как сквозь свой, Г56)
                                tFlag[i] |= 2;
                                continue;
                            }
                            if (enemy || !First(ms, i, j, ax, ay, bx, by, dt, M)) Yield(i, nx, ny, j, enemy);
                        }
                    }
                Prof.N[6] += seen;
            }

            Prof.Add(13, ref pt);
            // 4) шаг: предел скорости (сквозь своих — медленнее) и разгона, непроходимое
            var amaxOf = new double[ms.Count];
            for (int mi = 0; mi < ms.Count; mi++) amaxOf[mi] = MR.AccelK * MoveSim.FigAccel(ms[mi], r) * dt;
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i];
                if (tRigid[i])
                {
                    // Г86: ровно на своём месте у якоря, ход якоря
                    var s0 = man.Fig;
                    man.Vx = s0.AVx; man.Vy = s0.AVy;
                    man.X = s0.AX + tLx[i] * s0.Hc - tLy[i] * s0.Hs; man.Y = s0.AY + tLx[i] * s0.Hs + tLy[i] * s0.Hc;
                    tX[i] = man.X; tY[i] = man.Y;
                    continue;
                }
                double vmax = tVmax[i] * ((tFlag[i] & 2) != 0 ? M.PassThroughSpeed : 1), dvx = tDvx[i], dvy = tDvy[i], dv = JsMath.Hypot(dvx, dvy);
                if (dv > vmax) { dvx *= vmax / dv; dvy *= vmax / dv; }
                double ax = dvx - man.Vx, ay = dvy - man.Vy, a = JsMath.Hypot(ax, ay), amax = amaxOf[tMi[i]];
                if (a > amax) { ax *= amax / a; ay *= amax / a; }
                man.Vx += ax; man.Vy += ay;
                double nx = man.X + man.Vx * dt, ny = man.Y + man.Vy * dt;
                var F = tM[i].Field;
                if (F == null || MoveSim.Free(F, nx, ny) || !MoveSim.Free(F, man.X, man.Y)) { man.X = nx; man.Y = ny; }
                else if (MoveSim.Free(F, nx, man.Y)) { man.X = nx; man.Vy = 0; }
                else if (MoveSim.Free(F, man.X, ny)) { man.Y = ny; man.Vx = 0; }
                else { man.Vx = 0; man.Vy = 0; }
                tX[i] = man.X; tY[i] = man.Y;
            }

            Prof.Add(14, ref pt);
            // 5) расталкивание перекрывшихся — два прохода; пары — клетка с собой и с четырьмя соседними впереди (каждая
            // пара — один раз), только занятые клетки; клетка — не меньше двух самых больших тел. (Клетка по пешему телу с
            // кольцами соседей у коней давала столько же проверок пар — 230 млн за ход на 60 тыс.: выигрыш внутри клетки съедали
            // кольца; рычаг — тела, отсортированные по клеткам в сплошном массиве, не размер клетки.)
            long pp = Prof.Now();
            Grid(ct, Math.Max(Cell, 2 * maxBody + M.BodyTol));
            Prof.Add(18, ref pp);
            for (int iter = 0; iter < 2; iter++)
                foreach (int k in gUsed)
                {
                    int cx = k % gW, cy = k / gW; bool soft = gSoft[k];
                    if (iter == 0 && soft) { long n0 = 0; for (int i = gHead[k]; i >= 0; i = gNext[i]) n0++; Prof.N[7] += n0 * (n0 - 1) / 2; }
                    if (soft)   // Г86: клетка из одних жёстких — толкаться некому
                        for (int i = gHead[k]; i >= 0; i = gNext[i])
                            for (int j = gNext[i]; j >= 0; j = gNext[j]) Pair(ms, i, j, dt, M);
                    for (int q = 0; q < 4; q++)
                    {
                        int nx = cx + NX[q], ny = cy + NY[q];
                        if (nx < 0 || ny < 0 || nx >= gW || ny >= gH) continue;
                        int nk = ny * gW + nx, h = gHead[nk];
                        if (h < 0 || !(soft || gSoft[nk])) continue;
                        for (int i = gHead[k]; i >= 0; i = gNext[i])
                            for (int j = h; j >= 0; j = gNext[j]) Pair(ms, i, j, dt, M);
                    }
                }
            Prof.Add(19, ref pp);
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i];
                man.X = tX[i]; man.Y = tY[i];
                if ((tFlag[i] & 4) != 0) { man.Vx = (man.X - tX0[i]) / dt; man.Vy = (man.Y - tY0[i]) / dt; }
                // курс тела (Г94) — уже повёрнут в шаге 2, не прыгает
            }
            Prof.Add(20, ref pp);
            Finish(ms, dt, r, c, geo);
            Prof.Add(15, ref pt);
        }

        // 6) колонны — середина и скорость живых бойцов; пробка — по доле упёршихся бойцов
        static void Finish(IList<Mover> ms, double dt, Rules r, int c, Geo geo)
        {
            var M = r.Move;
            // Г104: на чём стоит боец — верх стены или башни по клетке (только если на карте они есть)
            var map = geo?.Map; bool zOn = map != null && Terrain.HasAny(map, idWall, idTower);
            double wallTop = 0, towerTop = 0;
            if (zOn) { r.Ranged.BuildingHeightM.TryGetValue("wall", out wallTop); r.Ranged.BuildingHeightM.TryGetValue("tower", out towerTop); }
            foreach (var m in ms) foreach (var s in m.Figs) { s.X = 0; s.Y = 0; s.Vx = 0; s.Vy = 0; s.MenN = 0; s.RefX = 0; s.RefY = 0; s.RefN = 0; s.RefAllX = 0; s.RefAllY = 0; s.LagN = 0; s.BlockedBy = 0; s.BlockedByEnemy = false; s.Slowed = false; }
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i]; var s = man.Fig;
                if (zOn) man.Z = Terrain.StandTop(map, man.X, man.Y, wallTop, towerTop);
                s.X += man.X; s.Y += man.Y; s.Vx += man.Vx; s.Vy += man.Vy; s.MenN++;
                // где был бы якорь по этому бойцу; пересаживающийся (В14) и отставший дальше LagRefM якорь не тянут — они не упёрлись,
                // а догоняют: «стоят» за нынешнее место якоря (один отставший на сотню метров иначе держал бы всю колонну)
                double ix = man.X - (tLx[i] * s.Hc - tLy[i] * s.Hs), iy = man.Y - (tLx[i] * s.Hs + tLy[i] * s.Hc);
                s.RefAllX += ix; s.RefAllY += iy;
                if (man.Reseat || (tFlag[i] & 128) != 0 || (ix - s.AX) * (ix - s.AX) + (iy - s.AY) * (iy - s.AY) > LagRefM * LagRefM) { ix = s.AX; iy = s.AY; if ((tFlag[i] & 128) == 0) s.LagN++; }   // ждущий (старт волной, Г84) якорь не держит
                s.RefX += ix; s.RefY += iy; s.RefN++;
                if (tBlocked[i] != 0) { s.BlockedBy = tBlocked[i]; s.BlockedByEnemy = tBlockedEnemy[i]; }
                if ((tFlag[i] & 2) != 0) s.Slowed = true;
            }
            foreach (var m in ms)
                foreach (var s in m.Figs)
                {
                    if (s.MenN > 0) { s.X /= s.MenN; s.Y /= s.MenN; s.Vx /= s.MenN; s.Vy /= s.MenN; }
                    if (s.RefN > 0) { s.RefX /= s.RefN; s.RefY /= s.RefN; s.RefAllX /= s.RefN; s.RefAllY /= s.RefN; }
                    else { s.X = s.AX; s.Y = s.AY; s.Vx = s.AVx; s.Vy = s.AVy; }
                }
            // пробка: как у фигурок (Bodies, шаг 4) — доля упёршихся колонн. Колонна упёрлась, если упёрся хоть один её боец
            // или она бьётся (Fighting — касается врага, Б2): по доле бойцов удар во фланг не останавливал строй — упираются
            // только головы колонн, а колонны в охвате стоят на своих местах у врага, ни во что не упираясь
            Dictionary<int, (int n, bool enemy)>[] count = null;
            for (int i = 0; i < c; i++)
            {
                int mi = tMi[i];
                int by = tBlocked[i];
                if (by == 0 || by == tM[i].IgnoreHoldBy) continue;   // кого обходим (Г61) — не держит
                if (count == null) count = new Dictionary<int, (int n, bool enemy)>[ms.Count];
                var cm = count[mi] ?? (count[mi] = new Dictionary<int, (int n, bool enemy)>());
                cm[by] = ((cm.TryGetValue(by, out var e) ? e.n : 0) + 1, tBlockedEnemy[i]);
            }
            for (int mi = 0; mi < ms.Count; mi++)
            {
                var m = ms[mi];
                int nb = 0, nm = 0;
                bool active = m.Order != null && m.Track != null && !m.Done;
                // схватка держит отряд, только пока он идёт на врага (атака, подход); отступающий (Г81) или уходящий от врага,
                // с которым бьётся, не держится ни схваткой, ни упором в этого врага (тот теснит его, а не стоит на пути) —
                // как фигурки (Г58), его держат лишь тела на пути: свои и другой враг
                double wx = 0, wy = 0; bool wantKnown = active && MoveSim.WantDir(m, out wx, out wy);
                leaving ??= new List<int>(); leaving.Clear();
                if (wantKnown) foreach (var s in m.Figs) if (s.Fighting && s.FightX * wx + s.FightY * wy < -0.3 && !leaving.Contains(s.FoeId)) leaving.Add(s.FoeId);
                foreach (var s in m.Figs)
                {
                    if (s.MenN == 0) continue;
                    nm++;
                    bool fightHolds = s.Fighting && !(wantKnown && s.FightX * wx + s.FightY * wy < -0.3);
                    bool blockHolds = s.BlockedBy != 0 && s.BlockedBy != m.IgnoreHoldBy && !(s.BlockedByEnemy && leaving.Contains(s.BlockedBy));
                    if (fightHolds || blockHolds) nb++;
                }
                if (nb > 0 && count?[mi] != null)
                {
                    int main = 0, best = -1; bool enemy = false;
                    foreach (var kv in count[mi]) if (kv.Value.n > best || kv.Value.n == best && kv.Key < main) { best = kv.Value.n; main = kv.Key; enemy = kv.Value.enemy; }
                    m.LastBlocker = main; m.LastBlockerEnemy = enemy; m.LastBlockerName = "?";
                    foreach (var o in ms) if (o.P.U.Id == main) { m.LastBlockerName = o.P.U.Name; break; }
                }
                double need = m.Held ? Math.Max(2, M.HeldKeepShare * nm) : M.HeldShare * nm;
                if (active && nm > 0 && nb > 0 && nb >= need) m.HoldLeft = M.HoldSec;
                else m.HoldLeft = Math.Max(0, m.HoldLeft - dt);
                m.Held = active && m.HoldLeft > 1e-9;
                if (m.Held && m.LastBlocker != 0)
                    m.Blockers[m.LastBlocker] = ((m.Blockers.TryGetValue(m.LastBlocker, out var was) ? was.sec : 0) + dt, m.LastBlockerEnemy, m.LastBlockerName);
            }
        }

        // ── сетка соседей: голова списка в клетке и «следующий» у тела; хозяин клетки — отряд всех её бойцов
        // (−1 — пусто, −2 — разные отряды) ──
        [ThreadStatic] static double gx0, gy0, gCell; [ThreadStatic] static int gW, gH; [ThreadStatic] static int[] gOwner;
        [ThreadStatic] static bool[] gSoft;   // в клетке есть обычный (не жёсткий) боец — только такие клетки толкаются (Г86)
        static void Grid(int c, double cell)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < c; i++)
            {
                double x = tX[i], y = tY[i];
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            gx0 = x0; gy0 = y0; gCell = cell; gW = (int)((x1 - x0) / cell) + 1; gH = (int)((y1 - y0) / cell) + 1;
            int cells = gW * gH;
            // пустая клетка — −1; новый массив заполняем целиком, старый — чистим только занятые прошлый раз
            if (gUsed == null) gUsed = new List<int>();
            if (gHead == null || gHead.Length < cells)
            {
                gHead = new int[Math.Max(cells, 1024)]; gOwner = new int[gHead.Length]; gSoft = new bool[gHead.Length];
                for (int k = 0; k < gHead.Length; k++) { gHead[k] = -1; gOwner[k] = -1; }
            }
            else foreach (int k in gUsed) { gHead[k] = -1; gOwner[k] = -1; gSoft[k] = false; }
            gUsed.Clear();
            for (int i = c - 1; i >= 0; i--)   // с конца — в клетке по возрастанию номера
            {
                int cx = Math.Min(gW - 1, Math.Max(0, (int)((tX[i] - x0) / cell))), cy = Math.Min(gH - 1, Math.Max(0, (int)((tY[i] - y0) / cell))), k = cy * gW + cx;
                if (gHead[k] < 0) gUsed.Add(k);
                gNext[i] = gHead[k]; gHead[k] = i;
                gOwner[k] = gOwner[k] == -1 || gOwner[k] == tMi[i] ? tMi[i] : -2;
                if (!tRigid[i]) gSoft[k] = true;
            }
        }

        static readonly int[] NX = { 1, -1, 0, 1 }, NY = { 0, 1, 1, 1 };   // соседние клетки «впереди» — каждая пара клеток один раз
        // Грубая сетка хозяев клеток (CoarseCell м): чей отряд в клетке, −2 — разные отряды. Боец, вокруг которого только
        // свой отряд, на чужих не смотрит — в глубине строя и на марше вдали от всех это почти весь отряд
        const double CoarseCell = 6;
        [ThreadStatic] static int[] cOwner; [ThreadStatic] static double cx0, cy0; [ThreadStatic] static int cW, cH;
        static void Coarse(int c)
        {
            cx0 = gx0; cy0 = gy0;
            cW = (int)(gW * gCell / CoarseCell) + 2; cH = (int)(gH * gCell / CoarseCell) + 2;
            int cells = cW * cH;
            if (cOwner == null || cOwner.Length < cells) cOwner = new int[Math.Max(cells, 256)];
            for (int k = 0; k < cells; k++) cOwner[k] = -1;
            for (int i = 0; i < c; i++)
            {
                int k = Math.Min(cH - 1, (int)((tY[i] - cy0) / CoarseCell)) * cW + Math.Min(cW - 1, (int)((tX[i] - cx0) / CoarseCell));
                cOwner[k] = cOwner[k] == -1 || cOwner[k] == tMi[i] ? tMi[i] : -2;
            }
        }
        static bool OnlyOwn(double x, double y, double reach, int own)
        {
            int ax = Math.Max(0, (int)((x - reach - cx0) / CoarseCell)), bx = Math.Min(cW - 1, (int)((x + reach - cx0) / CoarseCell));
            int ay = Math.Max(0, (int)((y - reach - cy0) / CoarseCell)), by = Math.Min(cH - 1, (int)((y + reach - cy0) / CoarseCell));
            for (int cy = ay; cy <= by; cy++)
                for (int cx = ax; cx <= bx; cx++) { int o = cOwner[cy * cW + cx]; if (o != -1 && o != own) return false; }
            return true;
        }
        // Пара бойцов перекрылась — развести: свой отряд — пополам, свой чужой — уступающего, враг — того, кто шёл на другого
        static void Pair(IList<Mover> ms, int i, int j, double dt, Rules.MoveR M)
        {
            Prof.N[8]++;
            double ex = tX[i] - tX[j], ey = tY[i] - tY[j], lim = tHalf[i] + tRad[i] + tHalf[j] + tRad[j] + M.BodyTol;
            if (ex * ex + ey * ey >= lim * lim) return;
            var rel = RelOf(i, j);
            if (rel == Rel.Ghost) return;
            if (rel == Rel.Tree)
            {
                // дерево (Б5): выталкивает бойца целиком, само не двигается; два дерева — не пара
                if (tMi[i] < 0 && tMi[j] < 0) return;
                int mi = tMi[i] < 0 ? j : i;
                if (tMi[mi == i ? j : i] == -4 && tMan[mi] != null && tMan[mi].InDuel) return;   // Г108: полководец в круге — круг его не выталкивает
                if ((tFlag[mi] & 64) != 0 && !tMR.TreesStopHorses && tMi[mi == i ? j : i] != -4) return;   // конь: стволы только обходит (взгляд), не упирается — иначе строй конницы вязнет в лесу намертво; круг поединка (−4) держит и коня
                double dT = Dist(i, tX[i], tY[i], j, tX[j], tY[j], out double tnx, out double tny), penT = -dT;
                if (penT <= M.BodyTol) return;
                Prof.N[9]++;
                if (tMi[i] < 0) Push(j, -tnx * penT, -tny * penT, -1, false); else Push(i, tnx * penT, tny * penT, -1, false);
                return;
            }
            if (Math.Abs(tMan[i].Z - tMan[j].Z) > tStepM) return;   // Г104: один на стене, другой под ней — не достают друг друга
            bool ri = tRigid[i], rj = tRigid[j];
            if (ri && rj) return;   // Г86: два жёстких не толкаются
            double d = Dist(i, tX[i], tY[i], j, tX[j], tY[j], out double nx, out double ny);
            double pen = -d;
            if (pen <= M.BodyTol) return;
            Prof.N[9]++;
            double wa;
            // свои: пополам; кто упёрся во врага — того свои не двигают, сдвигается напирающий: давка сзади не продавливает
            // передних во врага (Г89, Г90: в строй вламывается только натиск, а не задние ряды на полном ходу)
            if (rel == Rel.Same) wa = tBlockedEnemy[i] == tBlockedEnemy[j] ? 0.5 : tBlockedEnemy[i] ? 0 : 1;
            // свой чужой отряд: уступающий сдвигается на FriendYieldShare, идущий первым — на остаток: сквозь плотный строй
            // своих не продавливаются насквозь, а пробираются, замедляясь
            else if (rel == Rel.Friend && tFriendSoft && unitStill[tMi[i]] != unitStill[tMi[j]])
            {
                // Г111 п.2: стоящий уступает идущему целиком — строй прогибается и потом возвращается на места; оба в беспорядке
                wa = unitStill[tMi[i]] ? 1 : 0;
                double until = tM[i].Steps + tDisorderSec / dt;
                if (until > tM[i].DisorderUntil) tM[i].DisorderUntil = until;
                if (until > tM[j].DisorderUntil) tM[j].DisorderUntil = until;
                // стоящий расступается вбок от хода идущего, а не катится перед ним: толчок — поперёк хода идущего (в ту сторону, куда
                // стоящий и так смещён от его оси), иначе его несло бы перед строем до самой цели
                var mv = tMan[unitStill[tMi[i]] ? j : i]; double vl = JsMath.Hypot(mv.Vx, mv.Vy);
                if (vl > 0.1)
                {
                    double ux = mv.Vx / vl, uy = mv.Vy / vl, along = nx * ux + ny * uy, px = nx - along * ux, py = ny - along * uy, pl = JsMath.Hypot(px, py);
                    if (pl < 0.2) { double sgn = (tMan[i].Id + tMan[j].Id) % 2 == 0 ? 1 : -1; px = -uy * sgn; py = ux * sgn; pl = 1; }
                    nx = px / pl; ny = py / pl;
                }
            }
            else if (rel == Rel.Friend) wa = First(ms, i, j, tX[i], tY[i], tX[j], tY[j], dt, M) ? 1 - M.FriendYieldShare : M.FriendYieldShare;
            else if ((tFlag[i] & 16) != 0 && (tFlag[j] & 64) == 0) { Knock(j, i); wa = 0; }   // натиск (Г90): пеший отброшен и сбит
            else if ((tFlag[j] & 16) != 0 && (tFlag[i] & 64) == 0) { Knock(i, j); wa = 1; }
            else
            {
                // враг: сдвигается тот, кто шёл на другого; оба стоят — пополам (Г58: в контакте никто никого не теснит — по массе
                // разводить нельзя, конь отжимал бы пехоту; ломится только натиск)
                double pa = Math.Max(0, -(tMan[i].Vx * nx + tMan[i].Vy * ny)), pb = Math.Max(0, tMan[j].Vx * nx + tMan[j].Vy * ny);
                wa = pa + pb < 0.05 ? 0.5 : pa / (pa + pb);
            }
            if (ri) wa = 0; else if (rj) wa = 1;   // жёсткий — неподвижное тело: сдвигается другой
            if (rel != Rel.Same) { if (ri) tMan[i].Thaw = true; else if (rj) tMan[j].Thaw = true; }   // чужой налез — жёсткий оттаивает
            double capI = tCap[i], capJ = tCap[j];
            if (rel == Rel.Enemy && M.EnemySolid && (tFlag[i] & 16) == 0 && (tFlag[j] & 16) == 0)
            {
                // Г111 п.2: враг твёрдый — ход на врага гасится у касания, перекрытие разводится целиком (а не по кэпу, под который конь полз в строй)
                var mi = tMan[i]; var mj = tMan[j];
                double ai = mi.Vx * nx + mi.Vy * ny; if (ai < 0) { mi.Vx -= ai * nx; mi.Vy -= ai * ny; }   // n — от j к i: ход i к j — отрицательная проекция
                double aj = mj.Vx * nx + mj.Vy * ny; if (aj > 0) { mj.Vx -= aj * nx; mj.Vy -= aj * ny; }
                capI = Math.Max(capI, pen * wa); capJ = Math.Max(capJ, pen * (1 - wa));
            }
            Push(i, nx * pen * wa, ny * pen * wa, rel != Rel.Same ? j : -1, rel == Rel.Enemy, capI);
            Push(j, -nx * pen * (1 - wa), -ny * pen * (1 - wa), rel != Rel.Same ? i : -1, rel == Rel.Enemy, capJ);
        }

        // Г90: конь j в натиске сбил пешего i — лежит DownSecMin…DownSecMax с (свой ритм по хешу), конь теряет ChargeLoss хода
        static void Knock(int i, int j)
        {
            var man = tMan[i]; var horse = tMan[j];
            if ((tFlag[i] & 32) != 0) return;
            man.DownLeft = tMR.DownSecMin + (tMR.DownSecMax - tMR.DownSecMin) * MoveSim.Hash01(tM[i].P.U.Id, man.Id, 21);
            man.DownAt = tM[i].Now; man.Foe = null;
            tFlag[i] |= 32;
            horse.Vx *= 1 - tMR.ChargeLoss; horse.Vy *= 1 - tMR.ChargeLoss; horse.Knocks++;
            // встал или сбил своё — натиск этого коня кончился, дальше стена (Г89)
            if (horse.Knocks >= tMR.ChargeKnocks || horse.Vx * horse.Vx + horse.Vy * horse.Vy < tMR.ChargeMinMps * tMR.ChargeMinMps) tFlag[j] = (byte)(tFlag[j] & ~16);
        }
        // Пикинёр j (первые PikeRanks рядов, не бегущий, не лежит) смотрит на i — тот перед остриями
        static bool PikeAt(int j, int i)
        {
            var m = tM[j]; var p = tMan[j];
            if (m.Fleeing || (tFlag[j] & 32) != 0 || !Units.IsPike(m.P.U) || p.Row >= tMR.PikeRanks) return false;
            double h = p.Facing * Math.PI / 180, dx = tX[i] - tX[j], dy = tY[i] - tY[j], dl = Math.Sqrt(dx * dx + dy * dy);
            return dl > 1e-9 && (dx * Math.Sin(h) - dy * Math.Cos(h)) / dl >= 0.7;   // в пределах ~45° от его курса
        }

        // Уступить: убрать из желаемой скорости шаг навстречу (n — от другого к себе)
        static void Yield(int i, double nx, double ny, int j, bool enemy, bool mark = true)
        {
            double ap = tDvx[i] * nx + tDvy[i] * ny;
            if (ap >= 0) return;
            tDvx[i] -= ap * nx; tDvy[i] -= ap * ny;
            if (mark && ap < -0.1) { tBlocked[i] = tM[j].P.U.Id; tBlockedEnemy[i] = enemy; }
        }
        static void Push(int i, double dx, double dy, int other, bool enemy, double cap = double.NaN)
        {
            if (dx == 0 && dy == 0) return;
            if (double.IsNaN(cap)) cap = tCap[i];
            double nx = tX[i] + dx, ny = tY[i] + dy;
            double ox = nx - tX0[i], oy = ny - tY0[i], ol = JsMath.Hypot(ox, oy);
            if (ol > cap) { nx = tX0[i] + ox * cap / ol; ny = tY0[i] + oy * cap / ol; }   // давка: перекрытие рассосётся за несколько шагов
            var F = tM[i].Field;
            if (F != null && !MoveSim.Free(F, nx, ny) && MoveSim.Free(F, tX[i], tY[i])) return;   // в воду и в стену не выталкиваем
            tX[i] = nx; tY[i] = ny; tFlag[i] |= 4;
            // упёрся — если толкнули против его хода; толчок стоящего или туда, куда он и шёл (враг напирает на отступающего,
            // Г81), — не упор
            if (other >= 0 && dx * tDvx[i] + dy * tDvy[i] < 0) { tBlocked[i] = tM[other].P.U.Id; tBlockedEnemy[i] = enemy; }
        }

        // Г57 поштучно: идёт ли первым отряд бойца i перед своим отрядом бойца j — как Bodies.First: решается при первой
        // встрече отрядов и помнится, пока они не разойдутся на RightsForgetSec; кто раньше у места встречи — тот первый
        static bool First(IList<Mover> ms, int i, int j, double ax, double ay, double bx, double by, double dt, Rules.MoveR M)
        {
            Mover A = tM[i], B = tM[j];
            int idA = A.P.U.Id, idB = B.P.U.Id;
            if (A.Rights.TryGetValue(idB, out var e) && (A.Steps - e.seen) * dt <= M.RightsForgetSec)
            {
                A.Rights[idB] = (e.mine, A.Steps); B.Rights[idA] = (!e.mine, B.Steps);
                return e.mine;
            }
            double cx = (ax + bx) / 2, cy = (ay + by) / 2;
            double tA = TimeTo(i, cx, cy), tB = TimeTo(j, cx, cy);
            bool mine;
            if (Math.Abs(tA - tB) > M.TieSec) mine = tA < tB;
            else if (A.P.U.Discipline != B.P.U.Discipline) mine = A.P.U.Discipline > B.P.U.Discipline;
            else mine = tMi[i] < tMi[j];
            A.Rights[idB] = (mine, A.Steps); B.Rights[idA] = (!mine, B.Steps);
            return mine;
        }
        static double TimeTo(int i, double cx, double cy)
        {
            var man = tMan[i];
            double v = Math.Max(JsMath.Hypot(tDvx[i], tDvy[i]), JsMath.Hypot(man.Vx, man.Vy));
            if (v < 0.2) return 0;   // стоит — уже на месте
            return JsMath.Hypot(cx - tX[i], cy - tY[i]) / v;
        }

        // Расстояние между телами (минус — перекрытие) и направление n от j к i
        static double Dist(int i, double ax, double ay, int j, double bx, double by, out double nx, out double ny)
        {
            double d;
            if (tHalf[i] == 0 && tHalf[j] == 0)
            {
                double ex = ax - bx, ey = ay - by; d = JsMath.Hypot(ex, ey);
                if (d > 1e-9) { nx = ex / d; ny = ey / d; } else { nx = 1; ny = 0; }
                return d - tRad[i] - tRad[j];
            }
            double d2 = Bodies.SegSeg(ax - tUx[i] * tHalf[i], ay - tUy[i] * tHalf[i], ax + tUx[i] * tHalf[i], ay + tUy[i] * tHalf[i],
                                      bx - tUx[j] * tHalf[j], by - tUy[j] * tHalf[j], bx + tUx[j] * tHalf[j], by + tUy[j] * tHalf[j],
                                      out double c1x, out double c1y, out double c2x, out double c2y);
            d = Math.Sqrt(d2);
            if (d > 1e-9) { nx = (c1x - c2x) / d; ny = (c1y - c2y) / d; }
            else
            {
                double ex = ax - bx, ey = ay - by, el = JsMath.Hypot(ex, ey);
                if (el > 1e-9) { nx = ex / el; ny = ey / el; } else { nx = 1; ny = 0; }
            }
            return d - tRad[i] - tRad[j];
        }
    }
}
