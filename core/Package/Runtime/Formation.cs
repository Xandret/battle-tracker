// ═══════════ Formation.cs — строй в метрах и раскладка на фигурки (И1) ═══════════
// Размер строя — копия battlemap.footprint. Фигурка — N бойцов подряд в одной шеренге (Г24: масштаб
// под битву, по умолчанию 1:10). Шеренга 0 — передняя; координаты — в метрах, в системе отряда:
// x вдоль фронта (0 — середина), y вглубь (передний край строя — y = −глубина/2, как фасинг 0° «вверх»).
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
        public int UnitId, Rank, File;
        public double Men;          // сколько бойцов сейчас в фигурке (0 — фигурка пала)
        public double X, Y, Width;  // центр и ширина по фронту, м
    }

    public static class Formation
    {
        static Rules.FormationR For(Unit u, Rules r) =>
            r.Map.Formation.TryGetValue(u.Type, out var f) ? f : r.Map.Formation["infantry"];

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

        // Раскладка отряда на фигурки по N бойцов: шеренги заполняются спереди назад,
        // в шеренге — слева направо; последняя фигурка шеренги может быть неполной.
        public static List<Figure> Layout(Unit u, double menPerFigure, Rules r)
        {
            var f = For(u, r);
            var fp = Of(u, r);
            var list = new List<Figure>();
            double n = Math.Max(0, Js.Round(u.Soldiers));
            if (n <= 0) return list;
            double ranks = Math.Min(f.Ranks, n);
            double perRank = Math.Ceiling(n / ranks);
            double left = n;
            for (int rank = 0; rank < ranks && left > 0; rank++)
            {
                double inRank = Math.Min(perRank, left);
                left -= inRank;
                int file = 0;
                for (double placed = 0; placed < inRank; placed += menPerFigure, file++)
                {
                    double men = Math.Min(menPerFigure, inRank - placed);
                    double x0 = placed * f.PerMan - fp.Front / 2;
                    list.Add(new Figure
                    {
                        UnitId = u.Id, Rank = rank, File = file, Men = men,
                        Width = men * f.PerMan,
                        X = x0 + men * f.PerMan / 2,
                        Y = -fp.Depth / 2 + (rank + 0.5) * f.RankDepth,
                    });
                }
            }
            return list;
        }
    }
}
