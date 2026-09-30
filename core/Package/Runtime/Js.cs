// ═══════════ Js.cs — арифметика и печать чисел ровно как в JavaScript ═══════════
// Эталон v29 записан JavaScript'ом: строки журнала вроде «1323.9 / 5.8 = 226.7» должны совпасть
// до символа. Поэтому округление и печать чисел здесь повторяют JS, а не привычки C#:
// Math.round в JS округляет .5 вверх (к +∞), а Math.Round в C# — к чётному.
using System;
using System.Globalization;

namespace BattleCore
{
    public static class Js
    {
        // Math.round из JS: .5 — вверх, даже для отрицательных (−2.5 → −2)
        public static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        // r1 из util.js: до одного знака после запятой
        public static double R1(double v) => Round(v * 10) / 10;

        public static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

        // String(n) из JS: целые без «.0», дробные — кратчайшая запись, минус ноль — «0»
        public static string Num(double v)
        {
            if (v == 0) return "0";
            if (Math.Abs(v) < 1e21 && v == Math.Floor(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    // Генератор с зерном mulberry32 — тот же, что в util.js. Одно зерно — одна последовательность
    // и в трекере, и в игре: так одна и та же битва воспроизводится где угодно.
    public sealed class Mulberry32
    {
        uint a;
        public Mulberry32(uint seed) { a = seed; }
        public Mulberry32(double seed) { a = unchecked((uint)(long)seed); }

        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    public static class Dice
    {
        // Бросок dN: Math.floor(rng() * n) + 1
        public static double Roll(Func<double> rng, double n) => Math.Floor(rng() * n) + 1;
    }
}
