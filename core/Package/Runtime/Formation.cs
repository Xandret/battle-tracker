// ═══════════ Formation.cs — строй в метрах и раскладка на фигурки (И1) ═══════════
// Размер строя — копия battlemap.footprint. Фигурка — квадратик из N бойцов (Г24: масштаб под битву,
// по умолчанию 1:10; Г32: 5 по фронту × 2 в глубину, если шеренги так делятся). Координаты — в метрах,
// в системе отряда: x вдоль фронта (0 — середина), y вглубь (передний край — y = −глубина/2, как фасинг 0° «вверх»).
using System;
using System.Collections.Generic;
using System.Linq;

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
        public double Face;             // Г106: куда смотрит колонна относительно курса отряда, градусы (каре и круг: 0, 90, 180, 270)
    }

    public enum Form { Line, Wedge, Crescent, Square, Circle }

    public static class Formation
    {
        public static Rules.FormationR For(Unit u, Rules r)
        {
            var f = r.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : r.Map.Formation["infantry"];
            // Г101: выбранное построение — своя глубина в шеренгах при том же шаге в строю; порог — не глубже RanksMax
            double ranks = u.Ranks > 0 ? Math.Min(u.Ranks, r.Move.RanksMax) : f.Ranks;
            // Г106: разомкнутые ряды — шире шаг и глубже шеренга
            double per = u.Open ? f.PerMan * r.Move.OpenK : f.PerMan, dep = u.Open ? f.RankDepth * r.Move.OpenDepthK : f.RankDepth;
            return ranks == f.Ranks && per == f.PerMan && dep == f.RankDepth ? f : new Rules.FormationR(per, ranks, dep);
        }

        // Г106: форма строя отряда
        public static Form FormOf(Unit u) => u.Shape switch { "wedge" => Form.Wedge, "crescent" => Form.Crescent, "square" => Form.Square, "circle" => Form.Circle, _ => Form.Line };
        public static string FormKey(Form f) => f switch { Form.Wedge => "wedge", Form.Crescent => "crescent", Form.Square => "square", Form.Circle => "circle", _ => "line" };
        public static bool IsRing(Unit u) { var f = FormOf(u); return f == Form.Square || f == Form.Circle; }

        // Г106: клетки строя-формы — сетка P × R с шагом (PerMan, RankDepth), левый передний угол в (−Front/2, −Depth/2); занятые
        // клетки в порядке заполнения (при потерях пустеют последние) с гранью: 0 вперёд, 1 вправо, 2 назад, 3 влево. null — линия
        public sealed class Cells { public int P, R; public List<(int row, int col, int face)> List = new List<(int, int, int)>(); }
        static readonly Dictionary<(int id, string type, int n, string shape, bool open, int ranks), Cells> cellsCache = new Dictionary<(int, string, int, string, bool, int), Cells>();
        public static Cells CellsOf(Unit u, Rules r)
        {
            var form = FormOf(u);
            if (form == Form.Line) return null;
            int n = (int)Math.Max(1, Js.Round(u.Soldiers));
            var key = (u.Id, u.Type, n, u.Shape, u.Open, u.Ranks);
            lock (cellsCache)
            {
                if (cellsCache.TryGetValue(key, out var hit)) return hit;
                if (cellsCache.Count > 256) cellsCache.Clear();
                var c = MakeCells(form, u, r, n);
                cellsCache[key] = c;
                return c;
            }
        }
        static IEnumerable<int> CenterOut(int start, int w) { for (int k = 0; k < w; k++) { int i = (w - 1) / 2 + (k % 2 == 0 ? k / 2 : -(k + 1) / 2); yield return start + i; } }
        static Cells MakeCells(Form form, Unit u, Rules r, int n)
        {
            var f = For(u, r); var c = new Cells();
            int R0 = (int)Math.Max(1, Math.Min(f.Ranks, n));
            switch (form)
            {
                case Form.Wedge:
                {
                    // клин: фронт линии, глубина вдвое; ряд r от острия шириной P·(r+1)/R, по центру; лишние — полными рядами сзади
                    int P = (int)Math.Ceiling(n / (double)R0), Rw = (int)Math.Min(2 * R0, r.Move.RanksMax);
                    for (int row = 0; c.List.Count < n; row++)
                    {
                        int w = row < Rw ? Math.Max(1, Math.Min(P, (int)Math.Round(P * row / (double)Math.Max(1, Rw - 1)))) : P;   // остриё — один, основание — фронт линии
                        foreach (int col in CenterOut((P - w) / 2, w)) { if (c.List.Count >= n) break; c.List.Add((row, col, 0)); }
                        c.R = row + 1;
                    }
                    c.P = P; return c;
                }
                case Form.Crescent:
                {
                    // полумесяц: та же линия, середина отнесена назад на CrescentBulge фронта, рога вперёд (вогнут к врагу)
                    int P = (int)Math.Ceiling(n / (double)R0);
                    int B = Math.Max(1, (int)Math.Round(P * f.PerMan * r.Move.CrescentBulge / f.RankDepth));
                    var back = new int[P];
                    for (int i = 0; i < P; i++) { double x = (i + 0.5 - P / 2.0) / (P / 2.0); back[i] = (int)Math.Round(B * (1 - x * x)); }
                    for (int k = 0; k < R0 && c.List.Count < n; k++)
                        foreach (int col in CenterOut(0, P)) { if (c.List.Count >= n) break; c.List.Add((back[col] + k, col, 0)); }
                    c.P = P; c.R = R0 + B; return c;
                }
                case Form.Square:
                case Form.Circle:
                {
                    // кольцо толщиной t рядов; сторона (радиус) — наименьшая, где клеток кольца хватает на всех; слои снаружи внутрь,
                    // в слое — по углу от переднего центра по часовой; грань — ближайшая сторона (круг — четверть)
                    int t = Math.Max(1, (int)Math.Ceiling(R0 / 2.0));
                    for (int sx = 2 * t + 1; ; sx++)
                    {
                        int sy = Math.Max(2 * t + 1, (int)Math.Round(sx * f.PerMan / f.RankDepth));
                        double W = sx * f.PerMan, H = sy * f.RankDepth, rad = Math.Min(W, H) / 2, thick = t * Math.Min(f.PerMan, f.RankDepth);
                        var cells = new List<(int layer, double ang, int row, int col, int face)>();
                        for (int row = 0; row < sy; row++)
                            for (int col = 0; col < sx; col++)
                            {
                                double x = (col + 0.5) * f.PerMan - W / 2, y = (row + 0.5) * f.RankDepth - H / 2;
                                int layer, face;
                                if (form == Form.Square)
                                {
                                    int dTop = row, dBot = sy - 1 - row, dL = col, dR = sx - 1 - col;
                                    layer = Math.Min(Math.Min(dTop, dBot), Math.Min(dL, dR));
                                    if (layer >= t) continue;
                                    int dv = Math.Min(dTop, dBot), dh = Math.Min(dL, dR);
                                    face = dv <= dh ? (dTop <= dBot ? 0 : 2) : (dR <= dL ? 1 : 3);
                                }
                                else
                                {
                                    double d = Math.Sqrt(x * x + y * y);
                                    if (d > rad || d < rad - thick) continue;
                                    layer = (int)((rad - d) / Math.Min(f.PerMan, f.RankDepth));
                                    face = Math.Abs(x) > Math.Abs(y) ? (x > 0 ? 1 : 3) : (y < 0 ? 0 : 2);
                                }
                                double ang = Math.Atan2(x, -y); if (ang < 0) ang += 2 * Math.PI;   // 0 — передний центр, по часовой
                                cells.Add((layer, ang, row, col, face));
                            }
                        if (cells.Count < n) continue;
                        cells.Sort((p, q) => p.layer != q.layer ? p.layer.CompareTo(q.layer) : p.ang.CompareTo(q.ang));
                        for (int k = 0; k < n; k++) c.List.Add((cells[k].row, cells[k].col, cells[k].face));
                        c.P = sx; c.R = sy; return c;
                    }
                }
            }
            return null;
        }

        // Строй: фронт × глубина. Потери сужают фронт, а не глубину. Формы (Г106) — габарит сетки клеток
        public static Footprint Of(Unit u, Rules r)
        {
            var f = For(u, r);
            var cells = CellsOf(u, r);
            if (cells != null) return new Footprint { Front = Math.Max(1, cells.P * f.PerMan), Depth = Math.Max(1, cells.R * f.RankDepth) };
            double n = Math.Max(1, Js.Round(u.Soldiers));
            double ranks = Math.Min(f.Ranks, n);
            return new Footprint { Front = Math.Max(1, Math.Ceiling(n / ranks) * f.PerMan), Depth = Math.Max(1, ranks * f.RankDepth) };
        }

        // Г106: контур строя для рамки-призрака — точки (x, y) в метрах относительно центра и курса (x вправо, y назад), замкнутый
        public static double[] Outline(Unit u, Rules r, string shape = null)
        {
            var uu = u.Clone(); if (shape != null) uu.Shape = shape == "line" ? "" : shape;
            var fp = Of(uu, r); double hw = fp.Front / 2, hd = fp.Depth / 2;
            switch (FormOf(uu))
            {
                case Form.Wedge: return new[] { 0, -hd, hw, hd, -hw, hd };
                case Form.Crescent:
                {
                    var cells = CellsOf(uu, r); var f = For(uu, r); var pts = new List<double>();
                    int B = cells.R - (int)Math.Max(1, Math.Min(f.Ranks, Math.Max(1, Js.Round(uu.Soldiers))));
                    for (int i = 0; i <= 8; i++) { double x = -1 + i / 4.0; pts.Add(x * hw); pts.Add(-hd + B * (1 - x * x) * f.RankDepth); }
                    for (int i = 8; i >= 0; i--) { double x = -1 + i / 4.0; pts.Add(x * hw); pts.Add(hd - (B * (x * x)) * f.RankDepth); }
                    return pts.ToArray();
                }
                case Form.Circle:
                {
                    var pts = new List<double>(); double rr = Math.Min(hw, hd);
                    for (int i = 0; i < 24; i++) { double a = 2 * Math.PI * i / 24; pts.Add(rr * Math.Sin(a)); pts.Add(-rr * Math.Cos(a)); }
                    return pts.ToArray();
                }
                default: return new[] { -hw, -hd, hw, -hd, hw, hd, -hw, hd };
            }
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
            var cellsF = CellsOf(u, r);
            if (cellsF != null)
            {
                // Г106: форма — колонны по файлам в системе своей грани: клетки грани поворачиваются так, чтобы её «вперёд» стало −y;
                // колонна — непрерывный отрезок рядов одного файла; каждая колонна — своя (File — сквозной номер)
                (int fr, int fc) Face(int row, int col, int face) => face == 0 ? (row, col) : face == 2 ? (cellsF.R - 1 - row, cellsF.P - 1 - col) : face == 1 ? (cellsF.P - 1 - col, row) : (col, cellsF.R - 1 - row);
                var groups = new Dictionary<(int face, int fc), List<(int fr, int row, int col)>>();
                foreach (var (row, col, face) in cellsF.List)
                {
                    var (fr, fc) = Face(row, col, face);
                    if (!groups.TryGetValue((face, fc), out var g)) groups[(face, fc)] = g = new List<(int, int, int)>();
                    g.Add((fr, row, col));
                }
                int fileNo = 0;
                foreach (var key in groups.Keys.OrderBy(k => k.face).ThenBy(k => k.fc))
                {
                    var g = groups[key]; g.Sort((p, q) => p.fr.CompareTo(q.fr));
                    int start = 0;
                    for (int i = 1; i <= g.Count; i++)
                    {
                        if (i < g.Count && g[i].fr == g[i - 1].fr + 1) continue;
                        double sx = 0, sy = 0; int men = i - start;
                        for (int k = start; k < i; k++) { sx += -fp.Front / 2 + (g[k].col + 0.5) * f.PerMan; sy += -fp.Depth / 2 + (g[k].row + 0.5) * f.RankDepth; }
                        list.Add(new Figure { UnitId = u.Id, Rank = 0, File = fileNo++, Men = men, Width = f.PerMan, Depth = men * f.RankDepth, X = sx / men, Y = sy / men, Face = key.face * 90 });
                        start = i;
                    }
                }
                return list;
            }
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
            var cellsF = CellsOf(u, r);
            if (cellsF != null)
            {
                foreach (var (row, col, face) in cellsF.List) list.Add((-fp.Front / 2 + (col + 0.5) * f.PerMan, -fp.Depth / 2 + (row + 0.5) * f.RankDepth, col, row));
                return list;
            }
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
