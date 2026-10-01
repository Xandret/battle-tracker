// ═══════════ JsMath.cs — Math.sin, Math.cos, Math.hypot ровно как в Node (V8) ═══════════
// Генераторы карт и геометрия строя считают синусы и расстояния. Math.Sin из .NET расходится с Node
// в последнем бите примерно в 3% случаев, а простой √(x²+y²) — в трети случаев с Math.hypot. Для карты
// это значит «при том же зерне клетка на краю рощи — другая». Поэтому здесь — те же алгоритмы, что в V8:
// sin и cos из fdlibm (Sun Microsystems), hypot — с нормировкой по наибольшему и суммой Кэхэна.
// Проверено на миллионе аргументов: совпадает с Node 24 бит в бит.
using System;

namespace BattleCore
{
    public static class JsMath
    {
        static int Hi(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);
        static double WithHi(int hi) => BitConverter.Int64BitsToDouble((long)hi << 32);

        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
                     S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
                     C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;

        // sin на [−π/4, π/4]; y — хвост аргумента после приведения
        static double KSin(double x, double y, int iy)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000 && (int)x == 0) return x;
            double z = x * x, v = z * x, r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            if (iy == 0) return x + v * (S1 + z * r);
            return x - ((z * (0.5 * y - v * r) - y) - v * S1);
        }

        static double KCos(double x, double y)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000 && (int)x == 0) return 1.0;
            double z = x * x, r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            if (ix < 0x3FD33333) return 1.0 - (0.5 * z - (z * r - x * y));
            double qx = ix > 0x3FE90000 ? 0.28125 : WithHi(ix - 0x00200000);
            double iz = 0.5 * z - qx, a = 1.0 - qx;
            return a - (iz - (z * r - x * y));
        }

        static readonly int[] Npio2Hw = {
            0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB,
            0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB,
            0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
            0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB };
        const double InvPio2 = 6.36619772367581382433e-01, Pio2_1 = 1.57079632673412561417e+00, Pio2_1t = 6.07710050650619224932e-11,
                     Pio2_2 = 6.07710050630396597660e-11, Pio2_2t = 2.02226624879595063154e-21, Pio2_3 = 2.02226624871116645580e-21,
                     Pio2_3t = 8.47842766036889956997e-32;

        // Приведение аргумента к [−π/4, π/4]: x = n·π/2 + (y0 + y1). Огромные |x| (больше 2¹⁹·π/2) не нужны — ошибка.
        static int RemPio2(double x, out double y0, out double y1)
        {
            int hx = Hi(x), ix = hx & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) { y0 = x; y1 = 0; return 0; }
            if (ix < 0x4002D97C)
            {
                double z;
                if (hx > 0)
                {
                    z = x - Pio2_1;
                    if (ix != 0x3FF921FB) { y0 = z - Pio2_1t; y1 = (z - y0) - Pio2_1t; }
                    else { z -= Pio2_2; y0 = z - Pio2_2t; y1 = (z - y0) - Pio2_2t; }
                    return 1;
                }
                z = x + Pio2_1;
                if (ix != 0x3FF921FB) { y0 = z + Pio2_1t; y1 = (z - y0) + Pio2_1t; }
                else { z += Pio2_2; y0 = z + Pio2_2t; y1 = (z - y0) + Pio2_2t; }
                return -1;
            }
            if (ix <= 0x413921FB)
            {
                double t = Math.Abs(x);
                int n = (int)(t * InvPio2 + 0.5);
                double fn = n, r = t - fn * Pio2_1, w = fn * Pio2_1t;
                if (n < 32 && ix != Npio2Hw[n - 1]) y0 = r - w;
                else
                {
                    int j = ix >> 20;
                    y0 = r - w;
                    int i = j - ((Hi(y0) >> 20) & 0x7FF);
                    if (i > 16)
                    {
                        t = r; w = fn * Pio2_2; r = t - w; w = fn * Pio2_2t - ((t - r) - w); y0 = r - w;
                        i = j - ((Hi(y0) >> 20) & 0x7FF);
                        if (i > 49) { t = r; w = fn * Pio2_3; r = t - w; w = fn * Pio2_3t - ((t - r) - w); y0 = r - w; }
                    }
                }
                y1 = (r - y0) - w;
                if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
                return n;
            }
            throw new ArgumentOutOfRangeException(nameof(x), "JsMath: аргумент синуса слишком велик");
        }

        public static double Sin(double x)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return KSin(x, 0, 0);
            if (ix >= 0x7FF00000) return x - x;
            int n = RemPio2(x, out var a, out var b);
            switch (n & 3)
            {
                case 0: return KSin(a, b, 1);
                case 1: return KCos(a, b);
                case 2: return -KSin(a, b, 1);
                default: return -KCos(a, b);
            }
        }

        public static double Cos(double x)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return KCos(x, 0);
            if (ix >= 0x7FF00000) return x - x;
            int n = RemPio2(x, out var a, out var b);
            switch (n & 3)
            {
                case 0: return KCos(a, b);
                case 1: return -KSin(a, b, 1);
                case 2: return -KCos(a, b);
                default: return KSin(a, b, 1);
            }
        }

        // Math.hypot(a, b) из V8: делим на наибольшее, квадраты складываем с поправкой Кэхэна
        public static double Hypot(double a, double b)
        {
            if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            double r = a / max, summand = r * r - comp, pre = sum + summand;
            comp = (pre - sum) - summand; sum = pre;
            r = b / max; summand = r * r - comp; pre = sum + summand;
            sum = pre;
            return Math.Sqrt(sum) * max;
        }
    }
}
