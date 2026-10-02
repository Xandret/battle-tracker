// ═══════════ Ballistics.cs — полёт стрелы и попадание в тело (Г33, Г39) ═══════════
// Стрела — точка с сопротивлением воздуха, пропорциональным квадрату скорости; шаг — Рунге — Кутта
// 4-го порядка. Угол подбирается под точку прицела; при стрельбе из глубины своего строя — так, чтобы
// стрела прошла над головами своих (Г40): сначала ослабленным выстрелом, в крайнем случае — навесом.
// Тела: пеший — цилиндр, конный — конь (коробка по фасингу) и всадник над ним.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class Body
    {
        public Placed Owner;
        public double X, Y, Ground, Facing;
        public double FrontDist;        // сколько метров до переднего края своего строя (для выстрела поверх своих)
        public int File, Rank;          // место в строю: колонна и шеренга
        public bool Horse, Alive = true;
        public Man Man;                 // живой боец (Г75), если мишень — он
    }

    public static class Ballistics
    {
        // Скорость стрелы: энергия натяжения F·d/2 × КПД → v = √(2E/m)
        public static double V0(Rules.BowR b) => Math.Sqrt(2 * 0.5 * b.DrawN * b.DrawM * b.Efficiency / b.ArrowKg);
        // Сопротивление: a = −k·|v|·v, k = ½ρ·Cd·S / m
        public static double DragK(Rules.BowR b, Rules.RangedR r) =>
            0.5 * r.AirDensity * b.Cd * Math.PI * b.ArrowDiamM * b.ArrowDiamM / 4 / b.ArrowKg;

        // Какой лук у отряда: по типу и названию (черновик, пока у отряда нет своего поля «лук»)
        public static string BowKeyFor(Unit u)
        {
            string n = (u.Name ?? "").ToLowerInvariant();
            if (u.Type == "cavalry") return "horsebow";
            if (n.Contains("арбалет")) return "crossbow";
            if (n.Contains("ополч")) return "shortbow";
            return "longbow";
        }

        static void Acc(double vx, double vy, double vz, double k, double g, out double ax, out double ay, out double az)
        {
            double v = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            ax = -k * v * vx; ay = -k * v * vy; az = -g - k * v * vz;
        }

        public static void Step(ref double x, ref double y, ref double z, ref double vx, ref double vy, ref double vz,
                                double k, double g, double dt)
        {
            Acc(vx, vy, vz, k, g, out var a1x, out var a1y, out var a1z);
            double v2x = vx + a1x * dt / 2, v2y = vy + a1y * dt / 2, v2z = vz + a1z * dt / 2;
            Acc(v2x, v2y, v2z, k, g, out var a2x, out var a2y, out var a2z);
            double v3x = vx + a2x * dt / 2, v3y = vy + a2y * dt / 2, v3z = vz + a2z * dt / 2;
            Acc(v3x, v3y, v3z, k, g, out var a3x, out var a3y, out var a3z);
            double v4x = vx + a3x * dt, v4y = vy + a3y * dt, v4z = vz + a3z * dt;
            Acc(v4x, v4y, v4z, k, g, out var a4x, out var a4y, out var a4z);
            x += dt / 6 * (vx + 2 * v2x + 2 * v3x + v4x);
            y += dt / 6 * (vy + 2 * v2y + 2 * v3y + v4y);
            z += dt / 6 * (vz + 2 * v2z + 2 * v3z + v4z);
            vx += dt / 6 * (a1x + 2 * a2x + 2 * a3x + a4x);
            vy += dt / 6 * (a1y + 2 * a2y + 2 * a3y + a4y);
            vz += dt / 6 * (a1z + 2 * a2z + 2 * a3z + a4z);
        }

        // Высота стрелы над точкой вылета на горизонтальном расстоянии d (угол theta — в радианах)
        public static double HeightAt(double v0, double theta, double k, double g, double d, double dt)
        {
            double x = 0, y = 0, z = 0, vx = v0 * Math.Cos(theta), vy = 0, vz = v0 * Math.Sin(theta);
            if (d <= 0) return 0;
            for (int i = 0; i < 100000; i++)
            {
                double px = x, pz = z;
                Step(ref x, ref y, ref z, ref vx, ref vy, ref vz, k, g, dt);
                if (x >= d) return pz + (z - pz) * (d - px) / (x - px);
                if (z < -300 || vx <= 1e-6) break;
            }
            return double.NegativeInfinity;
        }

        // Сколько секунд стрела летит до горизонтального расстояния d — для упреждения по идущей цели (Г66)
        public static double FlightTime(double v0, double theta, double k, double g, double d, double dt)
        {
            double x = 0, y = 0, z = 0, vx = v0 * Math.Cos(theta), vy = 0, vz = v0 * Math.Sin(theta), t = 0;
            if (d <= 0) return 0;
            for (int i = 0; i < 100000; i++)
            {
                double px = x;
                Step(ref x, ref y, ref z, ref vx, ref vy, ref vz, k, g, dt);
                if (x >= d) return t + dt * (d - px) / (x - px);
                t += dt;
                if (z < -300 || vx <= 1e-6) break;
            }
            return t;
        }

        // Угол, при котором стрела выше всего проходит над точкой d (верх настильной ветви)
        static double PeakAngle(double v0, double k, double g, double d, double dt)
        {
            double lo = -0.2, hi = 1.45;
            for (int i = 0; i < 40; i++)
            {
                double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                if (HeightAt(v0, m1, k, g, d, dt) < HeightAt(v0, m2, k, g, d, dt)) lo = m1; else hi = m2;
            }
            return (lo + hi) / 2;
        }

        // Угол возвышения, чтобы пройти через точку (d, dz) над вылетом: настильно или навесом; null — не достать
        public static double? Aim(double v0, double k, double g, double d, double dz, double dt, bool high = false)
        {
            double peak = PeakAngle(v0, k, g, d, dt);
            if (HeightAt(v0, peak, k, g, d, dt) < dz) return null;
            double lo = high ? peak : -0.2, hi = high ? 1.45 : peak;
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) / 2;
                bool above = HeightAt(v0, mid, k, g, d, dt) > dz;
                // настильная ветвь: выше угол — выше стрела; навесная — наоборот
                if (above ^ high) hi = mid; else lo = mid;
            }
            return (lo + hi) / 2;
        }

        // Прицел с кэшем (угол, доля силы, навес). Ответ зависит только от лука, дистанции, перепада высот
        // и места стрелка в своём строю, поэтому кэш общий для всех ходов. Стрелок не в первой шеренге
        // должен пройти над головами своих (Г40): у первого своего впереди (через шеренгу) и у переднего края
        // стрела выше роста + 15 см. Сначала полная сила, потом ослабленные выстрелы (круче дуга), потом навес.
        static readonly Dictionary<(Rules.BowR, int, int, int, int), (double theta, double speed, bool high)?> aimCache =
            new Dictionary<(Rules.BowR, int, int, int, int), (double, double, bool)?>();
        public static (double theta, double speed, bool high)? AimCached(Rules.BowR bow, Rules.RangedR r, double d, double dz,
                                                                         double front, double rankDepth, double launchH)
        {
            var key = (bow, (int)Math.Round(d * 2), (int)Math.Round(dz * 4), (int)Math.Round(front * 2), (int)Math.Round(rankDepth * 2));
            lock (aimCache)
                if (aimCache.TryGetValue(key, out var hit)) return hit;
            double dq = key.Item2 / 2.0, dzq = key.Item3 / 4.0, fq = key.Item4 / 2.0, rq = key.Item5 / 2.0;
            double v0 = V0(bow), k = DragK(bow, r);
            double clearDz = r.BodyHeight + 0.15 - launchH;
            bool Clears(double v, double th) =>
                fq < 0.6 || HeightAt(v, th, k, r.Gravity, Math.Min(fq, rq), r.Dt) >= clearDz && HeightAt(v, th, k, r.Gravity, fq + 0.5, r.Dt) >= clearDz;
            (double, double, bool)? res = null;
            foreach (var sf in r.SpeedSteps)
            {
                var th = Aim(v0 * sf, k, r.Gravity, dq, dzq, r.Dt);
                if (th == null) break;
                if (Clears(v0 * sf, th.Value)) { res = (th.Value, sf, false); break; }
            }
            if (res == null)
            {
                var th = Aim(v0, k, r.Gravity, dq, dzq, r.Dt, high: true);
                if (th != null) res = (th.Value, 1.0, true);
            }
            lock (aimCache) aimCache[key] = res;
            return res;
        }

        // Нормальное распределение из равномерного генератора (Бокс — Мюллер)
        public static double Gauss(Func<double> rng)
        {
            double u1 = 1 - rng(), u2 = rng();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        // ── попадание отрезка пути стрелы P0→P1 в тело ──
        static bool Circle(double x0, double y0, double x1, double y1, double cx, double cy, double r, out double tA, out double tB)
        {
            double dx = x1 - x0, dy = y1 - y0, fx = x0 - cx, fy = y0 - cy;
            double a = dx * dx + dy * dy, c = fx * fx + fy * fy - r * r;
            tA = 0; tB = 1;
            if (a < 1e-12) return c <= 0;
            double b = 2 * (fx * dx + fy * dy), disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            double s = Math.Sqrt(disc);
            tA = Math.Max(0, (-b - s) / (2 * a)); tB = Math.Min(1, (-b + s) / (2 * a));
            return tA <= tB;
        }

        static bool Slab(double p0, double p1, double lo, double hi, ref double tA, ref double tB)
        {
            double d = p1 - p0;
            if (Math.Abs(d) < 1e-12) return p0 >= lo && p0 <= hi;
            double t1 = (lo - p0) / d, t2 = (hi - p0) / d;
            if (t1 > t2) { var t = t1; t1 = t2; t2 = t; }
            tA = Math.Max(tA, t1); tB = Math.Min(tB, t2);
            return tA <= tB;
        }

        // Первая точка на [tA, tB], где высота z(t) = z0 + (z1 − z0)·t попадает в [lo, hi]
        static bool ZEnter(double tA, double tB, double z0, double z1, double lo, double hi, out double t)
        {
            double dz = z1 - z0, zA = z0 + dz * tA, zB = z0 + dz * tB;
            t = tA;
            if (Math.Max(zA, zB) < lo || Math.Min(zA, zB) > hi) return false;
            if (zA >= lo && zA <= hi) return true;
            if (Math.Abs(dz) < 1e-12) return false;
            t = ((zA > hi ? hi : lo) - z0) / dz;
            return t >= tA - 1e-12 && t <= tB + 1e-12;
        }

        // Попадание в тело: доля пути t и часть тела (head / torso / legs / horse)
        public static bool Hit(Body b, Rules.RangedR r, double x0, double y0, double z0, double x1, double y1, double z1,
                               out double t, out string part)
        {
            t = 2; part = null;
            double g = b.Ground;
            if (!b.Horse)
            {
                if (!Circle(x0, y0, x1, y1, b.X, b.Y, r.BodyRadius, out var tA, out var tB)) return false;
                if (!ZEnter(tA, tB, z0, z1, g, g + r.BodyHeight, out t)) return false;
                double h = z0 + (z1 - z0) * t - g;
                part = h >= r.HeadFrom ? "head" : h >= r.TorsoFrom ? "torso" : "legs";
                return true;
            }
            bool hit = false;
            // конь — коробка вдоль фасинга
            double th = b.Facing * Math.PI / 180, rx = Math.Cos(th), ry = Math.Sin(th), fx = Math.Sin(th), fy = -Math.Cos(th);
            double u0 = (x0 - b.X) * rx + (y0 - b.Y) * ry, u1 = (x1 - b.X) * rx + (y1 - b.Y) * ry;
            double w0 = (x0 - b.X) * fx + (y0 - b.Y) * fy, w1 = (x1 - b.X) * fx + (y1 - b.Y) * fy;
            double sA = 0, sB = 1;
            if (Slab(u0, u1, -r.HorseWidth / 2, r.HorseWidth / 2, ref sA, ref sB) &&
                Slab(w0, w1, -r.HorseLength / 2, r.HorseLength / 2, ref sA, ref sB) &&
                ZEnter(sA, sB, z0, z1, g, g + r.HorseHeight, out var th1))
            { t = th1; part = "horse"; hit = true; }
            // всадник — цилиндр над конём
            if (Circle(x0, y0, x1, y1, b.X, b.Y, r.BodyRadius, out var cA, out var cB) &&
                ZEnter(cA, cB, z0, z1, g + r.HorseHeight, g + r.RiderTop, out var th2) && th2 < t)
            {
                t = th2; hit = true;
                part = z0 + (z1 - z0) * t - g >= r.RiderHeadFrom ? "head" : "torso";
            }
            return hit;
        }
    }

    // Сетка тел по клеткам 2 м — чтобы стрела проверяла только тех, кто рядом
    public sealed class BodyGrid
    {
        const double Cell = 2;
        readonly Dictionary<long, List<Body>> map = new Dictionary<long, List<Body>>();
        static long Key(int ix, int iy) => ((long)ix << 32) ^ (uint)iy;

        public void Clear() => map.Clear();
        public void Move(Body b, double x, double y)
        {
            long k = Key((int)Math.Floor(b.X / Cell), (int)Math.Floor(b.Y / Cell));
            if (map.TryGetValue(k, out var l)) l.Remove(b);
            b.X = x; b.Y = y;
            Add(b);
        }
        public void Add(Body b)
        {
            long k = Key((int)Math.Floor(b.X / Cell), (int)Math.Floor(b.Y / Cell));
            if (!map.TryGetValue(k, out var l)) map[k] = l = new List<Body>();
            l.Add(b);
        }
        public void Near(double x0, double y0, double x1, double y1, double margin, List<Body> into)
        {
            into.Clear();
            int ax = (int)Math.Floor((Math.Min(x0, x1) - margin) / Cell), bx = (int)Math.Floor((Math.Max(x0, x1) + margin) / Cell);
            int ay = (int)Math.Floor((Math.Min(y0, y1) - margin) / Cell), by = (int)Math.Floor((Math.Max(y0, y1) + margin) / Cell);
            for (int ix = ax; ix <= bx; ix++)
                for (int iy = ay; iy <= by; iy++)
                    if (map.TryGetValue(Key(ix, iy), out var l)) into.AddRange(l);
        }
    }
}
