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
        [ThreadStatic] static List<int> gUsed;       // занятые клетки сетки
        [ThreadStatic] static byte[] tFlag;          // 1 — стрелок, 2 — сквозь своих (медленнее), 4 — сдвинут расталкиванием, 8 — сквозь свой строй (Through)
        [ThreadStatic] static int[] tBlocked;        // номер отряда, которому уступил на этом шаге (0 — никому)
        [ThreadStatic] static bool[] tBlockedEnemy;
        [ThreadStatic] static int[] gNext, gHead;
        const double Cell = 1.5;                     // сетка соседей, м: запросы — по размеру тел, клетка — около шага в строю
        const int ViaEvery = 5;                      // как часто боец проверяет, пройти ли к месту напрямик, шагов

        enum Rel { Same, Ghost, Friend, Enemy }
        // Сквозь свой строй (Г68): колонна, что возвращается из охвата или идёт в охват к своему месту у врага (дальше
        // WrapSolidM от него и ещё не бьётся). Иначе колонны охвата сбиваются в давку у углов строя врага — бойцов в десять раз
        // больше, чем было фигурок
        const double WrapSolidM = 3;
        static bool Through(FigState s) => s.Returning || s.Wrap && !s.Fighting && JsMath.Hypot(s.WX - s.AX, s.WY - s.AY) > WrapSolidM;
        static bool SameSide(Unit a, Unit b) => a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId;
        static Rel RelOf(int i, int j)
        {
            Mover a = tM[i], b = tM[j];
            if (a == b) return (tFlag[i] & 8) != 0 || (tFlag[j] & 8) != 0 ? Rel.Ghost : Rel.Same;
            if (!SameSide(a.P.U, b.P.U)) return Rel.Enemy;
            return (tFlag[i] & 1) != 0 || (tFlag[j] & 1) != 0 || a.Fleeing || b.Fleeing ? Rel.Ghost : Rel.Friend;
        }

        public static void Step(IList<Mover> ms, double dt, Rules r)
        {
            var M = r.Move; var MR = r.Men;
            int every = Math.Max(1, (int)Math.Round(MR.BalanceSec / dt));
            int n = 0;
            foreach (var m in ms)
            {
                if (m.Men.Count > 0 && m.Steps % every == 0) Soldiers.Balance(m, r);   // смыкание между колоннами (В14)
                foreach (var s in m.Figs) { double hd = Soldiers.FigHeading(m, s), h = hd * Math.PI / 180; s.Hc = Math.Cos(h); s.Hs = Math.Sin(h); s.Hd = hd; }
                foreach (var man in m.Men) if (man.Alive && man.Fig != null) n++;
            }

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
                    double vx = s.AVx + ax, vy = s.AVy + ay, nx = s.AX + vx * dt, ny = s.AY + vy * dt;
                    if (F != null && !MoveSim.Free(F, nx, ny))
                    {
                        if (MoveSim.Free(F, nx, s.AY)) ny = s.AY;
                        else if (MoveSim.Free(F, s.AX, ny)) nx = s.AX;
                        else { nx = s.AX; ny = s.AY; }
                    }
                    bool held = false;
                    if (s.MenN > 0)
                    {
                        // где был бы якорь при этих бойцах на их местах: середина бойцов минус средний сдвиг их мест — иначе
                        // колонна с бойцами только спереди (потери) или с лишними рядами сзади (перебор) сама себя тащит
                        double bx = s.X - (s.MLx * s.Hc - s.MLy * s.Hs), by = s.Y - (s.MLx * s.Hs + s.MLy * s.Hc);
                        double ex = nx - bx, ey = ny - by, el = JsMath.Hypot(ex, ey);
                        if (el > M.AnchorLeadM) { nx = bx + ex * M.AnchorLeadM / el; ny = by + ey * M.AnchorLeadM / el; held = true; }
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
                }
            }
            if (n == 0) { Finish(ms, dt, r, 0); return; }

            // 2) тела и желание каждого бойца
            if (tMan == null || tMan.Length < n)
            {
                int cap = n * 2;
                tMan = new Man[cap]; tMi = new int[cap]; tM = new Mover[cap];
                tUx = new double[cap]; tUy = new double[cap]; tHalf = new double[cap]; tRad = new double[cap]; tX0 = new double[cap]; tY0 = new double[cap];
                tDvx = new double[cap]; tDvy = new double[cap]; tVmax = new double[cap]; tCap = new double[cap]; tReach = new double[cap];
                tFlag = new byte[cap]; tBlocked = new int[cap]; tBlockedEnemy = new bool[cap]; gNext = new int[cap];
                tX = new double[cap]; tY = new double[cap];
            }
            int c = 0; double maxBody = 0;
            double laF = M.LookAheadSec;
            for (int mi = 0; mi < ms.Count; mi++)
            {
                var m = ms[mi];
                if (m.Men.Count == 0) continue;
                var f = r.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : r.Map.Formation["infantry"];
                bool horse = BattleMap.IsHorse(m.P.U), archer = m.P.U.Type == "archer";
                double rad = MR.BodyShare * Math.Min(f.PerMan, f.RankDepth), half = horse ? M.HorseHalfShare * f.RankDepth : 0;
                double time = m.Steps * dt;
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Fig == null) continue;
                    var s = man.Fig;
                    double lx = man.Lx, ly = man.Ly, hu = man.Hu;
                    if (m.Fleeing)
                    {
                        lx *= MR.FleeSpread; ly *= MR.FleeSpread;
                        lx += Math.Sin(time * 2 * Math.PI / (2 + hu) + hu * 6.283) * MR.FleeWanderM;
                    }
                    double hx = s.AX + lx * s.Hc - ly * s.Hs, hy = s.AY + lx * s.Hs + ly * s.Hc;
                    // выпады (Г78): у кого свой противник (Б2) — к нему; передние бьющейся колонны без него — к врагу колонны
                    var foe = man.Foe;
                    if (!m.Fleeing && (foe != null && foe.Alive || s.Fighting && man.Row == 0))
                    {
                        double lunge = MR.LungeM + MR.LungeAmpM * Math.Sin(time * 2 * Math.PI / MR.LungeSec + hu * 6.283);
                        double ux = s.FightX, uy = s.FightY;
                        if (foe != null && foe.Alive)
                        {
                            double fx = foe.X - man.X, fy = foe.Y - man.Y, fl = JsMath.Hypot(fx, fy);
                            if (fl > 1e-9) { ux = fx / fl; uy = fy / fl; }
                        }
                        hx += ux * lunge; hy += uy * lunge;
                    }
                    double cx = (hx - man.X) / MR.Tau, cy = (hy - man.Y) / MR.Tau;
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
                    double vx = s.AVx + cx, vy = s.AVy + cy, vmax = Math.Max(s.Vmax, MR.WalkMin) * MR.SpeedK, v = JsMath.Hypot(vx, vy);
                    if (v > vmax) { vx *= vmax / v; vy *= vmax / v; }
                    double fh = man.Facing * Math.PI / 180;
                    tMan[c] = man; tMi[c] = mi; tM[c] = m;
                    tUx[c] = Math.Sin(fh); tUy[c] = -Math.Cos(fh); tHalf[c] = half; tRad[c] = rad;
                    tX0[c] = man.X; tY0[c] = man.Y; tX[c] = man.X; tY[c] = man.Y; tDvx[c] = vx; tDvy[c] = vy; tVmax[c] = vmax;
                    tCap[c] = Math.Max(vmax, 1) * dt * M.PushSpeedK;
                    tFlag[c] = (byte)((archer ? 1 : 0) | (Through(s) ? 8 : 0)); tBlocked[c] = 0; tBlockedEnemy[c] = false;
                    tReach[c] = half + rad + Math.Max(JsMath.Hypot(vx, vy), JsMath.Hypot(man.Vx, man.Vy)) * laF + M.MenYieldM;
                    if (half + rad > maxBody) maxBody = half + rad;
                    c++;
                }
            }

            // 3) взгляд вперёд — с бойцами чужих отрядов: каждый смотрит на свой ход вперёд (кто быстрее — увидит сам).
            // Клетки, где только свои, пропускаются целиком — в глубине строя взгляд вперёд ничего не стоит
            Grid(c, Cell);
            Coarse(c);
            for (int i = 0; i < c; i++)
            {
                double reach = tReach[i] + maxBody, xi = tX[i], yi = tY[i]; int own = tMi[i];
                if (OnlyOwn(xi, yi, reach, own)) continue;   // в округе — только свои: взгляд вперёд не нужен
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
                            var rel = RelOf(i, j);
                            if (rel == Rel.Ghost)
                            {
                                if (Dist(i, xi, yi, j, tX[j], tY[j], out _, out _) < 0) tFlag[i] |= 2;
                                continue;
                            }
                            bool enemy = rel == Rel.Enemy;
                            double la = enemy ? M.EnemyLookAheadSec : laF;
                            double ax = xi + tDvx[i] * la, ay = yi + tDvy[i] * la, bx = tX[j] + tDvx[j] * la, by = tY[j] + tDvy[j] * la;
                            double d = Dist(i, ax, ay, j, bx, by, out double nx, out double ny);   // n — от j к i
                            if (d >= M.MenYieldM) continue;
                            if (enemy || !First(ms, i, j, ax, ay, bx, by, dt, M)) Yield(i, nx, ny, j, enemy);
                        }
                    }
            }

            // 4) шаг: предел скорости (сквозь своих — медленнее) и разгона, непроходимое
            var amaxOf = new double[ms.Count];
            for (int mi = 0; mi < ms.Count; mi++) amaxOf[mi] = MR.AccelK * MoveSim.FigAccel(ms[mi], r) * dt;
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i];
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

            // 5) расталкивание перекрывшихся — два прохода; пары — клетка с собой и с четырьмя соседними впереди (каждая
            // пара — один раз), только занятые клетки; клетка — не меньше двух самых больших тел
            Grid(c, Math.Max(Cell, 2 * maxBody + M.BodyTol));
            for (int iter = 0; iter < 2; iter++)
                foreach (int k in gUsed)
                {
                    int cx = k % gW, cy = k / gW;
                    for (int i = gHead[k]; i >= 0; i = gNext[i])
                        for (int j = gNext[i]; j >= 0; j = gNext[j]) Pair(ms, i, j, dt, M);
                    for (int q = 0; q < 4; q++)
                    {
                        int nx = cx + NX[q], ny = cy + NY[q];
                        if (nx < 0 || ny < 0 || nx >= gW || ny >= gH) continue;
                        int h = gHead[ny * gW + nx];
                        if (h < 0) continue;
                        for (int i = gHead[k]; i >= 0; i = gNext[i])
                            for (int j = h; j >= 0; j = gNext[j]) Pair(ms, i, j, dt, M);
                    }
                }
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i];
                man.X = tX[i]; man.Y = tY[i];
                if ((tFlag[i] & 4) != 0) { man.Vx = (man.X - tX0[i]) / dt; man.Vy = (man.Y - tY0[i]) / dt; }
                man.Facing = tM[i].Fleeing && man.Vx * man.Vx + man.Vy * man.Vy > 0.25 ? MoveSim.HeadingOf(man.Vx, man.Vy) : man.Fig.Hd;
            }
            Finish(ms, dt, r, c);
        }

        // 6) колонны — середина и скорость живых бойцов; пробка — по доле упёршихся бойцов
        static void Finish(IList<Mover> ms, double dt, Rules r, int c)
        {
            var M = r.Move;
            foreach (var m in ms) foreach (var s in m.Figs) { s.X = 0; s.Y = 0; s.Vx = 0; s.Vy = 0; s.MenN = 0; s.MLx = 0; s.MLy = 0; s.BlockedBy = 0; s.BlockedByEnemy = false; s.Slowed = false; }
            for (int i = 0; i < c; i++)
            {
                var man = tMan[i]; var s = man.Fig;
                s.X += man.X; s.Y += man.Y; s.Vx += man.Vx; s.Vy += man.Vy; s.MenN++; s.MLx += man.Lx; s.MLy += man.Ly;
                if (tBlocked[i] != 0) { s.BlockedBy = tBlocked[i]; s.BlockedByEnemy = tBlockedEnemy[i]; }
                if ((tFlag[i] & 2) != 0) s.Slowed = true;
            }
            foreach (var m in ms)
                foreach (var s in m.Figs)
                {
                    if (s.MenN > 0) { s.X /= s.MenN; s.Y /= s.MenN; s.Vx /= s.MenN; s.Vy /= s.MenN; s.MLx /= s.MenN; s.MLy /= s.MenN; }
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
                foreach (var s in m.Figs)
                {
                    if (s.MenN == 0) continue;
                    nm++;
                    if (s.Fighting || s.BlockedBy != 0 && s.BlockedBy != m.IgnoreHoldBy) nb++;
                }
                bool active = m.Order != null && m.Track != null && !m.Done;
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
                gHead = new int[Math.Max(cells, 1024)]; gOwner = new int[gHead.Length];
                for (int k = 0; k < gHead.Length; k++) { gHead[k] = -1; gOwner[k] = -1; }
            }
            else foreach (int k in gUsed) { gHead[k] = -1; gOwner[k] = -1; }
            gUsed.Clear();
            for (int i = c - 1; i >= 0; i--)   // с конца — в клетке по возрастанию номера
            {
                int cx = Math.Min(gW - 1, Math.Max(0, (int)((tX[i] - x0) / cell))), cy = Math.Min(gH - 1, Math.Max(0, (int)((tY[i] - y0) / cell))), k = cy * gW + cx;
                if (gHead[k] < 0) gUsed.Add(k);
                gNext[i] = gHead[k]; gHead[k] = i;
                gOwner[k] = gOwner[k] == -1 || gOwner[k] == tMi[i] ? tMi[i] : -2;
            }
        }

        static readonly int[] NX = { 1, -1, 0, 1 }, NY = { 0, 1, 1, 1 };
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
            double ex = tX[i] - tX[j], ey = tY[i] - tY[j], lim = tHalf[i] + tRad[i] + tHalf[j] + tRad[j] + M.BodyTol;
            if (ex * ex + ey * ey >= lim * lim) return;
            var rel = RelOf(i, j);
            if (rel == Rel.Ghost) return;
            double d = Dist(i, tX[i], tY[i], j, tX[j], tY[j], out double nx, out double ny);
            double pen = -d;
            if (pen <= M.BodyTol) return;
            double wa;
            if (rel == Rel.Same) wa = 0.5;
            // свой чужой отряд: уступающий сдвигается на FriendYieldShare, идущий первым — на остаток: сквозь плотный строй
            // своих не продавливаются насквозь, а пробираются, замедляясь
            else if (rel == Rel.Friend) wa = First(ms, i, j, tX[i], tY[i], tX[j], tY[j], dt, M) ? 1 - M.FriendYieldShare : M.FriendYieldShare;
            else
            {
                // враг: сдвигается тот, кто шёл на другого; оба стоят — пополам (Г58)
                double pa = Math.Max(0, -(tMan[i].Vx * nx + tMan[i].Vy * ny)), pb = Math.Max(0, tMan[j].Vx * nx + tMan[j].Vy * ny);
                wa = pa + pb < 0.05 ? 0.5 : pa / (pa + pb);
            }
            Push(i, nx * pen * wa, ny * pen * wa, rel != Rel.Same ? j : -1, rel == Rel.Enemy);
            Push(j, -nx * pen * (1 - wa), -ny * pen * (1 - wa), rel != Rel.Same ? i : -1, rel == Rel.Enemy);
        }

        // Уступить: убрать из желаемой скорости шаг навстречу (n — от другого к себе)
        static void Yield(int i, double nx, double ny, int j, bool enemy)
        {
            double ap = tDvx[i] * nx + tDvy[i] * ny;
            if (ap >= 0) return;
            tDvx[i] -= ap * nx; tDvy[i] -= ap * ny;
            if (ap < -0.1) { tBlocked[i] = tM[j].P.U.Id; tBlockedEnemy[i] = enemy; }
        }
        static void Push(int i, double dx, double dy, int other, bool enemy)
        {
            if (dx == 0 && dy == 0) return;
            double nx = tX[i] + dx, ny = tY[i] + dy;
            double ox = nx - tX0[i], oy = ny - tY0[i], ol = JsMath.Hypot(ox, oy);
            if (ol > tCap[i]) { nx = tX0[i] + ox * tCap[i] / ol; ny = tY0[i] + oy * tCap[i] / ol; }   // давка: перекрытие рассосётся за несколько шагов
            var F = tM[i].Field;
            if (F != null && !MoveSim.Free(F, nx, ny) && MoveSim.Free(F, tX[i], tY[i])) return;   // в воду и в стену не выталкиваем
            tX[i] = nx; tY[i] = ny; tFlag[i] |= 4;
            if (other >= 0) { tBlocked[i] = tM[other].P.U.Id; tBlockedEnemy[i] = enemy; }
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
