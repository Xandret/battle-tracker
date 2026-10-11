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

        // ── Значения из сохранений: дерево как после JSON (словари, списки, строки, числа, true/false, null) ──

        // Пробелы, которые срезает String.prototype.trim (char.IsWhiteSpace берёт другой набор)
        public static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r') || c == '\u00A0' || c == '\u1680'
            || (c >= '\u2000' && c <= '\u200A') || c == '\u2028' || c == '\u2029' || c == '\u202F' || c == '\u205F'
            || c == '\u3000' || c == '\uFEFF';

        public static string Trim(string s)
        {
            if (s == null) return "";
            int a = 0, b = s.Length;
            while (a < b && IsSpace(s[a])) a++;
            while (b > a && IsSpace(s[b - 1])) b--;
            return s.Substring(a, b - a);
        }

        // Унарный плюс из JS (+v): null → 0, true → 1, строка — по правилам Number("…"), список — через запятые,
        // словарь — NaN
        public static double ToNumber(object v)
        {
            switch (v)
            {
                case null: return 0;
                case bool b: return b ? 1 : 0;
                case string s: return StrToNumber(s);
                case System.Collections.IDictionary _: return double.NaN;
                case System.Collections.IList l: return StrToNumber(ListToString(l));
                case IConvertible c: return c.ToDouble(CultureInfo.InvariantCulture);
                default: return double.NaN;
            }
        }

        static string ListToString(System.Collections.IList l)
        {
            var parts = new string[l.Count];
            for (int i = 0; i < l.Count; i++)
                parts[i] = l[i] switch
                {
                    null => "",
                    string s => s,
                    bool b => b ? "true" : "false",
                    System.Collections.IDictionary _ => "[object Object]",
                    System.Collections.IList inner => ListToString(inner),
                    IConvertible c => Num(c.ToDouble(CultureInfo.InvariantCulture)),
                    _ => "",
                };
            return string.Join(",", parts);
        }

        // Number("…"): пробелы по краям, пустая строка — 0, Infinity, 0x / 0b / 0o, десятичная запись с порядком
        static double StrToNumber(string raw)
        {
            string s = Trim(raw);
            if (s.Length == 0) return 0;
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s.Length > 2 && s[0] == '0')
            {
                char p = char.ToLowerInvariant(s[1]);
                int radix = p == 'x' ? 16 : p == 'b' ? 2 : p == 'o' ? 8 : 0;
                if (radix > 0)
                {
                    double r = 0;
                    for (int i = 2; i < s.Length; i++)
                    {
                        int d = s[i] >= '0' && s[i] <= '9' ? s[i] - '0' : s[i] >= 'a' && s[i] <= 'f' ? s[i] - 'a' + 10
                              : s[i] >= 'A' && s[i] <= 'F' ? s[i] - 'A' + 10 : 99;
                        if (d >= radix) return double.NaN;
                        r = r * radix + d;
                    }
                    return r;
                }
            }
            // [+-] цифры [. цифры] [e[+-]цифры] — хотя бы одна цифра в мантиссе
            int k = 0, digits = 0;
            if (s[k] == '+' || s[k] == '-') k++;
            while (k < s.Length && s[k] >= '0' && s[k] <= '9') { k++; digits++; }
            if (k < s.Length && s[k] == '.') { k++; while (k < s.Length && s[k] >= '0' && s[k] <= '9') { k++; digits++; } }
            if (digits == 0) return double.NaN;
            if (k < s.Length && (s[k] == 'e' || s[k] == 'E'))
            {
                k++;
                if (k < s.Length && (s[k] == '+' || s[k] == '-')) k++;
                int e = 0;
                while (k < s.Length && s[k] >= '0' && s[k] <= '9') { k++; e++; }
                if (e == 0) return double.NaN;
            }
            if (k != s.Length) return double.NaN;
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    // Генератор с зерном mulberry32 — тот же, что в util.js. Одно зерно — одна последовательность
    // и в трекере, и в игре: так одна и та же битва воспроизводится где угодно.
    public sealed class Mulberry32
    {
        uint a;
        public uint State { get => a; set => a = value; }   // Г112 п.2: снимок и откат — состояние генератора
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
