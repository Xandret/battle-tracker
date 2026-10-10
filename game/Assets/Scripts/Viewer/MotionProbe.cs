// ═══════════ MotionProbe.cs — мерило гладкого движения в показе (Г118) ═══════════
// Меряет то, что видно на экране: место и курс каждого бойца и коня после смотрелки (между кадрами записи — плавно,
// сглаживание MenView), на каждом шаге показа. По каждому: скорость — не выше своей наибольшей больше чем на 10 %; конь не
// едет назад и вбок быстрее шага; курс — не быстрее предела; скорость меняется не быстрее разгона и тормоза. Пределы —
// черновик Г118 (уточняются по ощущению Алекса). Это проверка, не правило боя: в бой не входит.
// Сброс сглаживания (боец за кадр сместился от середины отряда больше 6 м — рисуется сразу на новом месте) — тоже рывок.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Journal.Viewer
{
    public static class MotionProbe
    {
        public static bool On;
        // черновик Г118: м/с, °/с, м/с²
        public static float MaxHorse = 12, MaxFoot = 6, Over = 1.1f, HorseSideBack = 2, TurnHorse = 90, TurnFoot = 180, AccHorse = 6, AccFoot = 9;
        // пределы по норме отряда (м/с — норма хода за ход; разгон — с от места до нормы): числа Г118 (конь 10–12, пеший 5–6 м/с)
        // ниже норм движка (пехота ~100 м за 15 с — 6,7 м/с, конница 250 м — 16,7 м/с), поэтому скорость — от нормы отряда:
        // боец не быстрее нормы × SpeedK (1,4, Rules.Men) — и ещё 10 %; разгон — норма / AccelSec × AccelK (1,5) — и ещё 10 %
        public static Func<int, (float norm, float accelSec)> Norm;
        public static float SpeedK = 1.4f, AccelK = 1.5f;
        static float VMax(int ui, bool horse) => Norm != null ? Norm(ui).norm * SpeedK * Over : (horse ? MaxHorse : MaxFoot) * Over;
        static float AMax(int ui, bool horse) { if (Norm == null) return (horse ? AccHorse : AccFoot) * Over; var (v, a) = Norm(ui); return v / Mathf.Max(0.2f, a) * AccelK * Over; }
        const float Win = 0.1f;   // окно оценки скорости для разгона, с

        sealed class Track { public float T = -1, X, Y, F, WT = -1, WX, WY, VX, VY, VT = -1; }
        public sealed class Case { public string Metric; public bool Horse, Melee; public int Unit, Man; public float T, Value, Limit; public string Name; }
        sealed class Stat { public long N, Bad; public float Max; }

        static readonly Dictionary<long, Track> tracks = new Dictionary<long, Track>();
        static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        // эпизод — (мерило, отряд, секунда): худший случай, какие бойцы, кто из них в схватке
        sealed class Ep { public Case Worst; public readonly HashSet<int> Men = new HashSet<int>(); public readonly HashSet<int> Melee = new HashSet<int>(); }
        static readonly Dictionary<(string, int, int), Ep> eps = new Dictionary<(string, int, int), Ep>();
        public static int Snaps;
        static void Episode(Case c)
        {
            var k = (c.Metric, c.Unit, (int)c.T);
            if (!eps.TryGetValue(k, out var e)) eps[k] = e = new Ep();
            if (e.Worst == null || c.Value / c.Limit > e.Worst.Value / e.Worst.Limit) e.Worst = c;
            e.Men.Add(c.Man); if (c.Melee) e.Melee.Add(c.Man);
        }
        public static Func<int, string> UnitName = ui => $"#{ui}";

        public static void Reset() { tracks.Clear(); stats.Clear(); eps.Clear(); Snaps = 0; }

        static void Count(string metric, bool horse, float v, float limit, int ui, int id, float t, bool melee)
        {
            string k = (horse ? "конь " : "пеший ") + metric;
            if (!stats.TryGetValue(k, out var s)) stats[k] = s = new Stat();
            s.N++; if (v > s.Max) s.Max = v;
            if (v <= limit) return;
            s.Bad++;
            Episode(new Case { Metric = metric, Horse = horse, Melee = melee, Unit = ui, Man = id, T = t, Value = v, Limit = limit });
        }

        // нарисованный боец: место (м), курс (рад), в схватке ли
        public static void Add(int ui, int id, bool horse, float t, float x, float y, float face, bool melee)
        {
            long key = (long)ui << 32 | (uint)id;
            if (!tracks.TryGetValue(key, out var r)) tracks[key] = r = new Track();
            float f = face * Mathf.Rad2Deg, dt = t - r.T;
            if (r.T < 0 || dt <= 0 || dt > 0.1f) { r.T = t; r.X = x; r.Y = y; r.F = f; r.WT = t; r.WX = x; r.WY = y; r.VT = -1; return; }
            float dx = x - r.X, dy = y - r.Y, v = Mathf.Sqrt(dx * dx + dy * dy) / dt;
            Count("скорость", horse, v, VMax(ui, horse), ui, id, t, melee);
            if (horse)
            {
                // курс коня: вперёд — (sin h, −cos h); назад и вбок — составляющие скорости поперёк и против курса
                float h = r.F * Mathf.Deg2Rad, fx = Mathf.Sin(h), fy = -Mathf.Cos(h);
                float vf = (dx * fx + dy * fy) / dt, vs = Mathf.Abs(-dx * fy + dy * fx) / dt;
                Count("вбок", true, vs, HorseSideBack * Over, ui, id, t, melee);
                Count("назад", true, Mathf.Max(0, -vf), HorseSideBack * Over, ui, id, t, melee);
            }
            Count("поворот", horse, Mathf.Abs(Mathf.DeltaAngle(r.F, f)) / dt, horse ? TurnHorse : TurnFoot, ui, id, t, melee);
            // разгон: скорость по окнам 0,1 с, изменение между соседними окнами
            if (t - r.WT >= Win)
            {
                float wdt = t - r.WT, vx = (x - r.WX) / wdt, vy = (y - r.WY) / wdt, mid = (t + r.WT) / 2;
                if (r.VT >= 0) { float a = Mathf.Sqrt((vx - r.VX) * (vx - r.VX) + (vy - r.VY) * (vy - r.VY)) / (mid - r.VT); Count("разгон", horse, a, AMax(ui, horse), ui, id, t, melee); }
                r.VX = vx; r.VY = vy; r.VT = mid; r.WT = t; r.WX = x; r.WY = y;
            }
            r.T = t; r.X = x; r.Y = y; r.F = f;
        }
        // сглаживание сброшено скачком места (MenView.Smooth)
        public static void Snap(int ui, int id, float t, float jump, bool horse)
        {
            Snaps++;
            Episode(new Case { Metric = "сброс сглаживания", Horse = horse, Unit = ui, Man = id, T = t, Value = jump, Limit = 6 });
        }

        public static string Report(int top = 12)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Norm != null
                ? $"Пределы: скорость — норма отряда × {SpeedK} × {Over}; разгон — норма / разгон отряда × {AccelK} × {Over}; конь вбок и назад — {HorseSideBack} м/с × {Over}; поворот — конь {TurnHorse} °/с, пеший {TurnFoot} °/с (черновик Г118)."
                : $"Пределы (черновик Г118): скорость — конь {MaxHorse} м/с, пеший {MaxFoot} м/с, ×{Over}; конь вбок и назад — {HorseSideBack} м/с; поворот — конь {TurnHorse} °/с, пеший {TurnFoot} °/с; разгон — конь {AccHorse}, пеший {AccFoot} м/с² ×{Over}.");
            sb.AppendLine("Мерило | замеров | сверх предела | % | наибольшее");
            foreach (var kv in stats.OrderBy(k => k.Key))
                sb.AppendLine($"{kv.Key} | {kv.Value.N} | {kv.Value.Bad} | {(kv.Value.N == 0 ? 0 : 100.0 * kv.Value.Bad / kv.Value.N):0.00} | {kv.Value.Max:0.0}");
            sb.AppendLine($"сбросов сглаживания (боец рисуется сразу на новом месте, скачок > 6 м): {Snaps}");
            // эпизод — (мерило, отряд, секунда); крупнейшие — по числу бойцов (рывок всего отряда заметнее одиночки), потом по величине
            foreach (var grp in eps.Values.GroupBy(e => e.Worst.Metric))
            {
                var ep = grp.OrderByDescending(e => e.Men.Count).ThenByDescending(e => e.Worst.Value / e.Worst.Limit).Take(top).ToList();
                sb.AppendLine($"\n— {grp.Key}: эпизодов (отряд × секунда) {grp.Count()}, крупнейшие:");
                foreach (var e in ep)
                {
                    var w = e.Worst;
                    sb.AppendLine($"  {w.T,6:0.0} с  «{UnitName(w.Unit)}» ({(w.Horse ? "конь" : "пеший")}): бойцов {e.Men.Count}{(e.Melee.Count > 0 ? $" (в схватке {e.Melee.Count})" : "")}; худший {w.Man}: {w.Value:0.0} при пределе {w.Limit:0.0}");
                }
            }
            return sb.ToString();
        }
    }
}
