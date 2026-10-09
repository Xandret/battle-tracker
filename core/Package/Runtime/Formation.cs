// ═══════════ Formation.cs — строй в метрах и раскладка на фигурки (И1) ═══════════
// Размер строя — копия battlemap.footprint. Фигурка — квадратик из N бойцов (Г24: масштаб под битву,
// по умолчанию 1:10; Г32: 5 по фронту × 2 в глубину, если шеренги так делятся). Координаты — в метрах,
// в системе отряда: x вдоль фронта (0 — середина), y вглубь (передний край — y = −глубина/2, как фасинг 0° «вверх»).
using System;
using System.Collections.Generic;

namespace BattleCore
{
    public sealed class Footprint
    {
        public double Front, Depth;
    }

    public sealed class Figure
    {
        public int UnitId;
        public int Rank, File;          // ряд квадратиков (0 — передний) и колонна (0 — левая)
        public double Men;              // сколько бойцов сейчас в фигурке
        public double X, Y, Width, Depth;   // центр и размеры, м
        public bool Engaged;            // касается врага (Г27)
    }

    public static class Formation
    {
        static Rules.FormationR For(Unit u, Rules r)
        {
            var f = r.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : r.Map.Formation["infantry"];
            // Г101: выбранное построение — своя глубина в шеренгах при том же шаге в строю; порог — не глубже RanksMax
            return u.Ranks > 0 && u.Ranks != f.Ranks ? new Rules.FormationR(f.PerMan, Math.Min(u.Ranks, r.Move.RanksMax), f.RankDepth) : f;
        }

        // Строй: фронт × глубина. Потери сужают фронт, а не глубину.
        public static Footprint Of(Unit u, Rules r)
        {
            var f = For(u, r);
            double n = Math.Max(1, Js.Round(u.Soldiers));
            double ranks = Math.Min(f.Ranks, n);
            return new Footprint { Front = Math.Max(1, Math.Ceiling(n / ranks) * f.PerMan), Depth = Math.Max(1, ranks * f.RankDepth) };
        }

        // Масштаб фигурок под битву (Г24): мелкая стычка — 1:1, сражение — 1:10, огромное — 1:20.
        public static double ScaleFor(double totalMen) => totalMen <= 3000 ? 1 : totalMen <= 60000 ? 10 : 20;

        // Форма фигурки (Г32): fw бойцов по фронту × fd шеренг, fw·fd = бойцов в фигурке.
        // Сначала — чтобы fd делило число шеренг, потом — ближе к квадрату в метрах, потом — шире.
        public static (int fw, int fd) Shape(Unit u, double menPerFigure, Rules r)
        {
            var f = For(u, r);
            int m = Math.Max(1, (int)Math.Round(menPerFigure));
            int ranks = (int)Math.Min(f.Ranks, Math.Max(1, Js.Round(u.Soldiers)));
            // Б1 (Г85): бойцы — тела, фигурка — колонна во всю глубину строя шириной в столько рядов, чтобы вышло около
            // 10 человек (пехота 1 × 8, пики 1 × 10, стрелки и конница 2 × 5): на место павшего — стоящий за ним,
            // охват — колоннами, фронт сужается крайними колоннами
            if (r.Move.MenBodies) return (Math.Max(1, (int)Math.Round(10.0 / ranks)), ranks);
            (int fw, int fd) best = (m, 1);
            (int div, double asp, int w) bestKey = (int.MaxValue, double.MaxValue, 0);
            for (int fd = 1; fd <= m; fd++)
            {
                if (m % fd != 0 || fd > ranks) continue;
                int fw = m / fd;
                var key = (ranks % fd == 0 ? 0 : 1, Math.Abs(Math.Log(fw * f.PerMan / (fd * f.RankDepth))), -fw);
                if (key.Item1 < bestKey.div
                    || key.Item1 == bestKey.div && key.Item2 < bestKey.asp - 1e-9
                    || key.Item1 == bestKey.div && Math.Abs(key.Item2 - bestKey.asp) <= 1e-9 && key.Item3 < -bestKey.w)
                {
                    best = (fw, fd);
                    bestKey = (key.Item1, key.Item2, fw);
                }
            }
            return best;
        }

        // Раскладка отряда на фигурки. Шеренги полные, кроме последней — она неполная и стоит по центру
        // (так потери сужают фронт с краёв, как фишка трекера, Г30). Квадратики режут сетку бойцов
        // на колонны по fw и ряды по fd; пустые не создаются.
        public static List<Figure> Layout(Unit u, double menPerFigure, Rules r)
        {
            var f = For(u, r);
            var list = new List<Figure>();
            int n = (int)Math.Max(0, Js.Round(u.Soldiers));
            if (n <= 0) return list;
            var fp = Of(u, r);
            var (fw, fd) = Shape(u, menPerFigure, r);
            int R = (int)Math.Min(f.Ranks, n);
            int P = (int)Math.Ceiling(n / (double)R);             // бойцов в полной шеренге
            int last = n - P * (R - 1);                           // в последней шеренге
            int off = (P - last) / 2;                             // последняя — по центру
            int cols = (P + fw - 1) / fw, rows = (R + fd - 1) / fd;
            for (int b = 0; b < rows; b++)
            {
                int r0 = b * fd, rIn = Math.Min(fd, R - r0);
                for (int c = 0; c < cols; c++)
                {
                    int f0 = c * fw, fIn = Math.Min(fw, P - f0);
                    int men = 0;
                    for (int rr = r0; rr < r0 + rIn; rr++)
                        men += rr < R - 1 ? fIn : Math.Max(0, Math.Min(f0 + fIn, off + last) - Math.Max(f0, off));
                    if (men == 0) continue;
                    list.Add(new Figure
                    {
                        UnitId = u.Id, Rank = b, File = c, Men = men,
                        Width = fIn * f.PerMan, Depth = rIn * f.RankDepth,
                        X = -fp.Front / 2 + (f0 + fIn / 2.0) * f.PerMan,
                        Y = -fp.Depth / 2 + (r0 + rIn / 2.0) * f.RankDepth,
                    });
                }
            }
            return list;
        }

        // Бойцы поимённо — места в строю (м, в системе отряда), та же сетка, что у Layout:
        // шеренги полные, последняя — по центру. Нужны баллистике (Г33): стрела бьёт конкретного человека.
        public static List<(double x, double y, int file, int rank)> MenPositions(Unit u, Rules r)
        {
            var f = For(u, r);
            var list = new List<(double x, double y, int file, int rank)>();
            int n = (int)Math.Max(0, Js.Round(u.Soldiers));
            if (n <= 0) return list;
            var fp = Of(u, r);
            int R = (int)Math.Min(f.Ranks, n);
            int P = (int)Math.Ceiling(n / (double)R);
            int last = n - P * (R - 1), off = (P - last) / 2;
            for (int rr = 0; rr < R; rr++)
                for (int i = 0; i < P; i++)
                    if (rr < R - 1 || (i >= off && i < off + last))
                        list.Add((-fp.Front / 2 + (i + 0.5) * f.PerMan, -fp.Depth / 2 + (rr + 0.5) * f.RankDepth, i, rr));
            return list;
        }
    }
}
