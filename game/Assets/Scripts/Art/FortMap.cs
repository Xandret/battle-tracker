// ═══════════ FortMap.cs — укрепления из клеток карты (В19): что рисовать стенами, башнями, воротами, обломками ═══════════
// Перенос разбора карты полигона (core/Tests/polygon.html, buildMap): клетки стены, частокола и вала — в линии по серединам
// клеток, дважды сглаженные по соседям (лесенка клеток становится плавной стеной); ворота — по клетке, вдоль стены или
// поперёк; башни — круг на скопление клеток (слипшиеся делятся); пролом — груда обломков на клетку.
// Сверх полигона: где у стены наружа. Заливкой от края карты по всему, что не укрепление: до чего не дошла — двор.
// Зубцы ставятся наружу, створки ворот — у наружного края. Рисует FortView (Viewer), рисунок — атлас build и ленты
// wall_tile / palisade_tile из пробы core/Tests/build-flat.html (atlas-export.js). UnityEngine не нужен.
using System;
using System.Collections.Generic;
using BattleCore;

namespace Journal.Art
{
    public sealed class FortMap
    {
        public const int Wall = 1, Palisade = 2, Trench = 3;            // материал линии (как mat в полигоне)
        public sealed class Line { public int Mat; public List<(float x, float y)> P = new List<(float, float)>(); public bool Closed; public List<int> Out = new List<int>(); }   // Out — наружа по точкам: +1 слева по ходу, −1 справа, 0 — не понять
        public struct Gate { public float X, Y, W, Ax, Ay; public bool Horiz; public int Mat, Out; }   // W — ширина проёма вдоль стены (клеток × клетку); (Ax, Ay) — ось прохода (поперёк стены); Out: наружа по этой оси (+1 — по ней, −1 — против)
        public struct Tower { public float X, Y, R; public int Mat; public int Seed; }
        public struct Rubble { public float X, Y; public int Seed; }
        public struct House { public float X0, Y0, X1, Y1; public int Seed; }   // прямоугольник клеток «здание», метры

        public readonly List<Line> Lines = new List<Line>();
        public readonly List<Gate> Gates = new List<Gate>();
        public readonly List<Tower> Towers = new List<Tower>();
        public readonly List<Rubble> Rubbles = new List<Rubble>();
        public readonly List<House> Houses = new List<House>();
        public bool Rustic;                                              // есть частокол — острог: кровли из соломы и тёса
        public bool[] Inside;                                            // клетка во дворе (не дошла заливка снаружи)
        public int Cols, Rows; public float C;

        static readonly byte KWall = Terrain.Id("wall"), KGate = Terrain.Id("gate"), KTower = Terrain.Id("tower"), KPal = Terrain.Id("palisade"),
            KTrench = Terrain.Id("trench"), KBreach = Terrain.Id("breach");
        static bool IsFort(int k) => k == KWall || k == KGate || k == KTower || k == KPal || k == KBreach;

        public static FortMap Build(TerrainMap m)
        {
            var f = new FortMap { Cols = m.W, Rows = m.H, C = (float)m.Cell };
            int cols = m.W, rows = m.H, n = cols * rows; float c = f.C;
            int At(int x, int y) => x < 0 || y < 0 || x >= cols || y >= rows ? 0 : m.T[y * cols + x];
            // материал: стена 1, частокол 2, вал 3; ворота и башни — по большинству укреплений вокруг (5 × 5)
            var mat = new byte[n];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int i = y * cols + x, k = m.T[i];
                    if (k == KWall) mat[i] = Wall; else if (k == KPal) mat[i] = Palisade; else if (k == KTrench) mat[i] = Trench;
                    else if (k == KGate || k == KTower)
                    {
                        int s = 0, p = 0;
                        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) { int q = At(x + dx, y + dy); if (q == KWall) s++; else if (q == KPal) p++; }
                        mat[i] = (byte)(p > s ? Palisade : Wall);
                    }
                }
            int MatAt(int x, int y) => x < 0 || y < 0 || x >= cols || y >= rows ? 0 : mat[y * cols + x];
            // середины клеток, дважды сглаженные по соседям того же материала
            var px = new float[n]; var py = new float[n];
            for (int i = 0; i < n; i++) { px[i] = (i % cols + 0.5f) * c; py[i] = (i / cols + 0.5f) * c; }
            for (int it = 0; it < 2; it++)
            {
                var nx = (float[])px.Clone(); var ny = (float[])py.Clone();
                for (int i = 0; i < n; i++)
                {
                    int mm = mat[i]; if (mm == 0) continue;
                    int x = i % cols, y = i / cols; float sx = 2 * px[i], sy = 2 * py[i], w = 2;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && MatAt(x + dx, y + dy) == mm) { int j = (y + dy) * cols + x + dx; sx += px[j]; sy += py[j]; w++; }
                    nx[i] = sx / w; ny[i] = sy / w;
                }
                px = nx; py = ny;
            }
            // связи: вправо, вниз и по диагонали (диагональ — если угол не закрыт клеткой того же материала)
            var adj = new List<int>[n];
            void Link(int a, int b) { (adj[a] ??= new List<int>()).Add(b); (adj[b] ??= new List<int>()).Add(a); }
            for (int i = 0; i < n; i++)
            {
                int mm = mat[i]; if (mm == 0 || m.T[i] == KGate) continue;   // ворота — проём: лента стены через них не идёт
                int x = i % cols, y = i / cols;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (-1, 1) })
                {
                    if (MatAt(x + dx, y + dy) != mm || At(x + dx, y + dy) == KGate) continue;
                    if (dx != 0 && dy != 0 && (MatAt(x + dx, y) == mm || MatAt(x, y + dy) == mm)) continue;
                    Link(i, (y + dy) * cols + x + dx);
                }
            }
            // заливка снаружи: от края карты по клеткам без укреплений (диагональ не пускает сквозь лесенку стены)
            var outside = new bool[n]; var st = new Stack<int>();
            for (int x = 0; x < cols; x++) { Seed(x, 0); Seed(x, rows - 1); }
            for (int y = 0; y < rows; y++) { Seed(0, y); Seed(cols - 1, y); }
            void Seed(int x, int y) { int i = y * cols + x; if (!outside[i] && !IsFort(m.T[i])) { outside[i] = true; st.Push(i); } }
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % cols, y = i / cols;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= cols || yy >= rows) continue;
                    int j = yy * cols + xx; if (!outside[j] && !IsFort(m.T[j])) { outside[j] = true; st.Push(j); }
                }
            }
            f.Inside = new bool[n]; for (int i = 0; i < n; i++) f.Inside[i] = !outside[i] && !IsFort(m.T[i]);
            // наружа у точки (x, y) по нормали (nx, ny): смотрим клетки на 0,9 и 1,6 клетки в обе стороны
            int OutSide(float x, float y, float nx, float ny)
            {
                int score = 0;
                foreach (float d in new[] { 0.9f, 1.6f })
                {
                    int Cell(float s) { int cx = (int)Math.Floor((x + nx * s * c) / c), cy = (int)Math.Floor((y + ny * s * c) / c); return cx < 0 || cy < 0 || cx >= cols || cy >= rows ? -1 : cy * cols + cx; }
                    int a = Cell(d), b = Cell(-d);
                    bool ao = a < 0 || outside[a], bo = b < 0 || outside[b], ai = a >= 0 && f.Inside[a], bi = b >= 0 && f.Inside[b];
                    if (ao && bi) score++; else if (bo && ai) score--;
                }
                return Math.Sign(score);
            }
            // цепочки: от концов и развилок вдоль узлов степени 2; остаток — замкнутые кольца
            var used = new HashSet<long>();
            long Key(int a, int b) => a < b ? (long)a * n + b : (long)b * n + a;
            int Deg(int i) => adj[i]?.Count ?? 0;
            void Walk(int s, int nb)
            {
                var L = new Line { Mat = mat[s] }; L.P.Add((px[s], py[s]));
                int prev = s, cur = nb; used.Add(Key(s, nb));
                while (true)
                {
                    L.P.Add((px[cur], py[cur]));
                    if (cur == s) { L.Closed = true; break; }
                    if (Deg(cur) != 2) break;
                    int nxt = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0];
                    if (used.Contains(Key(cur, nxt))) break;
                    used.Add(Key(cur, nxt)); prev = cur; cur = nxt;
                }
                f.Lines.Add(L);
            }
            for (int i = 0; i < n; i++) if (adj[i] != null && Deg(i) != 2) foreach (int j in adj[i]) if (!used.Contains(Key(i, j))) Walk(i, j);
            for (int i = 0; i < n; i++) if (adj[i] != null) foreach (int j in adj[i]) if (!used.Contains(Key(i, j))) Walk(i, j);
            for (int i = 0; i < n; i++) if (mat[i] != 0 && adj[i] == null && m.T[i] != KTower && m.T[i] != KGate)   // одиночная клетка — коротыш
                {
                    var L = new Line { Mat = mat[i] }; L.P.Add((px[i] - c * 0.3f, py[i])); L.P.Add((px[i] + c * 0.3f, py[i])); f.Lines.Add(L);
                }
            foreach (var L in f.Lines)
            {
                // наружа по точкам — по соседним отрезкам, потом большинством вдоль линии (короткие провалы заливки не путают)
                int sum = 0;
                for (int k = 0; k < L.P.Count; k++)
                {
                    var a = L.P[Math.Max(0, k - 1)]; var b = L.P[Math.Min(L.P.Count - 1, k + 1)];
                    float tx = b.x - a.x, ty = b.y - a.y, tl = (float)Math.Sqrt(tx * tx + ty * ty); if (tl < 1e-4f) { L.Out.Add(0); continue; }
                    int o = OutSide(L.P[k].x, L.P[k].y, -ty / tl, tx / tl); L.Out.Add(o); sum += o;
                }
                int all = Math.Sign(sum);
                for (int k = 0; k < L.Out.Count; k++) L.Out[k] = all != 0 ? all : 1;
            }
            // ворота: соседние клетки ворот — один проём (по середине клеток, без сглаживания); проломы — груда на клетку
            var gseen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int x = i % cols, y = i / cols, k = m.T[i];
                if (k == KBreach) f.Rubbles.Add(new Rubble { X = (x + 0.5f) * c, Y = (y + 0.5f) * c, Seed = i });
                if (k != KGate || gseen[i]) continue;
                var cells = new List<int>(); var q = new Stack<int>(); q.Push(i); gseen[i] = true;
                while (q.Count > 0)
                {
                    int j = q.Pop(), jx = j % cols, jy = j / cols; cells.Add(j);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (At(jx + dx, jy + dy) == KGate) { int t2 = (jy + dy) * cols + jx + dx; if (!gseen[t2]) { gseen[t2] = true; q.Push(t2); } }
                }
                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                foreach (int j in cells) { x0 = Math.Min(x0, j % cols); x1 = Math.Max(x1, j % cols); y0 = Math.Min(y0, j / cols); y1 = Math.Max(y1, j / cols); }
                float gx = (x0 + x1 + 1) * 0.5f * c, gy = (y0 + y1 + 1) * 0.5f * c;
                // вдоль стены: где по бокам укрепления (стена, башни) — или где проём шире
                int side = 0; foreach (int j in cells) { int jx = j % cols, jy = j / cols; side += (MatAt(jx - 1, jy) != 0 ? 1 : 0) + (MatAt(jx + 1, jy) != 0 ? 1 : 0) - (MatAt(jx, jy - 1) != 0 ? 1 : 0) - (MatAt(jx, jy + 1) != 0 ? 1 : 0); }
                bool horiz = side != 0 ? side > 0 : x1 - x0 >= y1 - y0;
                float ax = horiz ? 0 : 1, ay = horiz ? 1 : 0;   // ось прохода
                // косая стена (редактор карт): ось прохода — поперёк ближайшего куска стены, а не по сторонам света
                float bestD = 2.2f * c;
                foreach (var Lw in f.Lines)
                {
                    if (Lw.Mat != mat[i] || Lw.P.Count < 2) continue;
                    float lwLen = 0; for (int q2 = 0; q2 + 1 < Lw.P.Count; q2++) lwLen += Dist(Lw.P[q2], Lw.P[q2 + 1]);
                    if (lwLen < 3 * c) continue;   // обрывки внутри башен ось не задают
                    for (int q2 = 0; q2 + 1 < Lw.P.Count; q2++)
                    {
                        var a = Lw.P[q2]; var b = Lw.P[q2 + 1]; float sx = b.x - a.x, sy = b.y - a.y, sl = (float)Math.Sqrt(sx * sx + sy * sy);
                        if (sl < 1e-3f) continue;
                        float tt = Math.Max(0, Math.Min(1, ((gx - a.x) * sx + (gy - a.y) * sy) / (sl * sl)));
                        float d = Dist((gx, gy), (a.x + sx * tt, a.y + sy * tt));
                        if (d < bestD) { bestD = d; ax = -sy / sl; ay = sx / sl; }
                    }
                }
                f.Gates.Add(new Gate { X = gx, Y = gy, W = (Math.Max(x1 - x0, y1 - y0) + 1) * c, Horiz = horiz, Mat = mat[i], Ax = ax, Ay = ay, Out = OutSide(gx, gy, ax, ay) is int o && o != 0 ? o : -1 });
            }
            // башни: связные куски (по 8 соседям); кусок вдвое больше обычного — несколько башен по скоплениям клеток
            var seen = new bool[n]; var comps = new List<List<int>>();
            for (int s = 0; s < n; s++)
            {
                if (m.T[s] != KTower || seen[s]) continue;
                var cells = new List<int>(); var q = new Stack<int>(); q.Push(s); seen[s] = true;
                while (q.Count > 0)
                {
                    int i = q.Pop(), x = i % cols, y = i / cols; cells.Add(i);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            if ((dx == 0 && dy == 0) || At(x + dx, y + dy) != KTower) continue;
                            int j = (y + dy) * cols + x + dx; if (!seen[j]) { seen[j] = true; q.Push(j); }
                        }
                }
                comps.Add(cells);
            }
            var sizes = new List<int>(); foreach (var cc in comps) sizes.Add(cc.Count); sizes.Sort();
            int usual = sizes.Count > 0 ? sizes[sizes.Count / 2] : 1;
            foreach (var cells in comps)
            {
                var P = cells.ConvertAll(i => ((i % cols + 0.5f) * c, (i / cols + 0.5f) * c));
                int kk = Math.Max(1, (int)Math.Round((double)P.Count / usual));
                var C = new List<(float x, float y)> { P[0] };
                while (C.Count < kk)
                {
                    var best = P[0]; float bd = -1;
                    foreach (var p in P) { float d = float.MaxValue; foreach (var q2 in C) d = Math.Min(d, Dist(p, q2)); if (d > bd) { bd = d; best = p; } }
                    C.Add(best);
                }
                for (int it = 0; it < 6; it++)
                {
                    var acc = new (float x, float y, int n)[C.Count];
                    foreach (var p in P) { int j = 0; for (int jj = 1; jj < C.Count; jj++) if (Dist(p, C[jj]) < Dist(p, C[j])) j = jj; acc[j] = (acc[j].x + p.Item1, acc[j].y + p.Item2, acc[j].n + 1); }
                    for (int j = 0; j < C.Count; j++) if (acc[j].n > 0) C[j] = (acc[j].x / acc[j].n, acc[j].y / acc[j].n);
                }
                float r = (float)Math.Sqrt((double)P.Count / kk) * c * 0.52f + 0.3f;
                for (int j = 0; j < C.Count; j++) f.Towers.Add(new Tower { X = C[j].x, Y = C[j].y, R = r, Mat = mat[cells[0]], Seed = cells[0] + j });
            }
            // дома: связный кусок клеток «здание» режется на прямоугольники — от верхней левой клетки вправо, потом вниз
            // полными рядами (как в полигоне)
            byte KHouse = Terrain.Id("building");
            var hseen = new bool[n];
            for (int s0 = 0; s0 < n; s0++)
            {
                if (m.T[s0] != KHouse || hseen[s0]) continue;
                var left = new SortedSet<int>(); var q = new Stack<int>(); q.Push(s0); hseen[s0] = true;
                while (q.Count > 0)
                {
                    int i = q.Pop(), x = i % cols, y = i / cols; left.Add(i);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (At(x + dx, y + dy) == KHouse) { int j = (y + dy) * cols + x + dx; if (!hseen[j]) { hseen[j] = true; q.Push(j); } }
                }
                while (left.Count > 0)
                {
                    int s = left.Min, x0 = s % cols, y0 = s / cols, x1 = x0, y1 = y0;
                    while (left.Contains(y0 * cols + x1 + 1)) x1++;
                    while (true) { bool ok = true; for (int x = x0; x <= x1; x++) if (!left.Contains((y1 + 1) * cols + x)) { ok = false; break; } if (!ok) break; y1++; }
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) left.Remove(y * cols + x);
                    f.Houses.Add(new House { X0 = x0 * c, Y0 = y0 * c, X1 = (x1 + 1) * c, Y1 = (y1 + 1) * c, Seed = s });
                }
            }
            for (int i = 0; i < n && !f.Rustic; i++) if (m.T[i] == KPal) f.Rustic = true;
            return f;
        }
        static float Dist((float x, float y) a, (float x, float y) b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }
}
