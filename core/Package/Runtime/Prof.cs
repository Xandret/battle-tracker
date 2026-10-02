// ═══════════ Prof.cs — счётчики времени по фазам шага боя (для замеров bench-*) ═══════════
// Всегда включены: Stopwatch.GetTimestamp — десятки наносекунд, вызовов — около дюжины на шаг.
// 0 — движение (MoveSim.Step), 1 — касания, 2 — охват и прочее раз в полсекунды, 3 — удары, 4 — стрельба, 5 — раскладка;
// 10–15 — внутри MenBodies.Step: смыкание и курсы, якоря, желания бойцов, взгляд вперёд, шаг, расталкивание (+ Finish)
        // 16 — стрельба: тела и сетка, 17 — пуск и полёт стрел. Счётчики N: 0 — вызовов полёта (подшаг × стрела), 1 — поисков тел рядом, 2 — тел проверено
using System.Diagnostics;

namespace BattleCore
{
    public static class Prof
    {
        public static readonly long[] T = new long[24];
        public static readonly long[] N = new long[8];
        public static long Now() => Stopwatch.GetTimestamp();
        public static void Add(int i, ref long t) { long n = Stopwatch.GetTimestamp(); T[i] += n - t; t = n; }
        public static double Sec(int i) => T[i] / (double)Stopwatch.Frequency;
        public static void Reset() { System.Array.Clear(T, 0, T.Length); System.Array.Clear(N, 0, N.Length); }
    }
}
