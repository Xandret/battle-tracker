// ═══════════ FlowField.cs — карта направлений и путь строя (Г31, шаг 1) ═══════════
// Карта направлений — цена пути до цели из каждой клетки 5 м: та же дейкстра и те же цены шага, что у зоны
// досягаемости трекера (BattleMap.Reach), только от цели назад. Из любой клетки видно, куда шагать.
// Путь центра строя — спуск по карте до цели, «натянутый как нить»: где прямой отрезок не дороже пути
// по клеткам, клетки срезаются (по открытому полю — одна прямая). Track — этот путь, промеренный по
// местности точно, клетка за клеткой: сколько метров он идёт по каждой клетке и сколько это стоит нормы.
// Скорость (карта 4000 × 3000 м — 480 тыс. клеток, а карта направлений строится сотни раз за ход):
//   • местность для путей — множители клеток, крупные препятствия, расстояние до них — от цели не зависит: она одна
//     на карту и род войск (Ground) и сверяется с картой при каждой постройке (пролом в стене, другие правила — заново);
//   • дейкстра ленивая (Search): идёт от цели, пока не осядет клетка, чью цену спросили, — до отряда, а не по всей
//     карте; спросят дальнюю клетку — пойдёт дальше с того места, где встала. Осевшая цена — та же, что при обходе
//     всей карты (при равных ценах порядок обхода на неё не влияет), поэтому путь и шаги фигурок не меняются;
//   • одинаковые карты направлений (та же клетка цели, род войск, полуширина) — один поиск на всех, пока они живы.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BattleCore
{
    public sealed class FlowField
    {
        public TerrainMap Map; public Geo Geo; public Rules R; public bool Horse;
        public int W, H, Target;
        public double CellW, CellH;
        // Цена пути до цели (для выбора пути); бесконечность — не дойти. CostAt(i) — цена клетки: поиск идёт ровно
        // до неё. Cost — цены всех клеток (поиск доводится до конца карты, дорого): для тестов и отладки. Не менять —
        // массив общий у одинаковых карт направлений.
        public double[] Cost { get { search.Complete(); return search.Cost; } }
        public double CostAt(int i) => search.At(i);
        Search search;
        // Шаг 3 (Г59): строй шириной 2 × HalfWidth держится от крупных препятствий на полфронта, если место есть.
        // Крупное — непроходимое пятно больше SmallObstacleM или край карты; мелкое (дом) фигурки огибают сами.
        // Clearance — от центра клетки до ближайшего крупного препятствия, м. Для выбора пути клетка ближе
        // HalfWidth дороже (Pen), а норма за ход по-прежнему — по местности (Track), как за столом.
        // Clearance, large и mult — общие для всех карт направлений этой местности: не менять.
        public double HalfWidth;
        public double[] Clearance;
        bool[] large;
        double penHalf, penK, penEdge;   // полуширина, ClearancePenalty и полклетки — на момент постройки
        double Pen(int i) => PenAt(Clearance, i, penHalf, penK, penEdge);
        static double PenAt(double[] d, int i, double half, double K, double edge) =>
            half > 0 ? 1 + K * Math.Max(0, half - Math.Max(0, d[i] - edge)) / half : 1;
        public bool Large(int i) => large[i];

        // Соседи — в том же порядке, что у BattleMap.Reach
        static readonly int[] DX = { -1, 0, 1, -1, 1, -1, 0, 1 }, DY = { -1, -1, -1, 0, 0, 1, 1, 1 };

        // множители местности по клеткам (NaN — непроходимо) — общие для карты и рода войск (Ground), кроме клеток
        // в обход (extraBlocked): тогда своя копия. Фигурок сотни, шагов сотни
        double[] mult;
        public double? Mult(int i) { double v = mult[i]; return double.IsNaN(v) ? (double?)null : v; }
        public bool Passable(int i) => !double.IsNaN(mult[i]);
        readonly Dictionary<int, int> nearCache = new Dictionary<int, int>();
        public int NearestPassableCached(int i)
        {
            if (!nearCache.TryGetValue(i, out var j)) nearCache[i] = j = NearestPassable(i);
            return j;
        }
        public int CellOf(double x, double y)
        {
            int cx = (int)Math.Min(W - 1, Math.Max(0, Math.Floor(x / CellW)));
            int cy = (int)Math.Min(H - 1, Math.Max(0, Math.Floor(y / CellH)));
            return cy * W + cx;
        }
        public bool Inside(double x, double y) => x >= 0 && y >= 0 && x < W * CellW && y < H * CellH;
        public (double x, double y) CenterOf(int i) => ((i % W + 0.5) * CellW, (i / W + 0.5) * CellH);

        // Цена шага из клетки i в соседнюю j — как в BattleMap.Reach; null — шагнуть нельзя
        public double? Step(int i, int j)
        {
            var mult = Mult(j);
            if (mult == null) return null;
            int x = i % W, y = i / W, nx = j % W, ny = j / W, dx = nx - x, dy = ny - y;
            if (dx != 0 && dy != 0 && Mult(y * W + nx) == null && Mult(ny * W + x) == null) return null;
            int up = Map.Z[j] - Map.Z[i];
            double step = dx != 0 && dy != 0 ? JsMath.Hypot(CellW, CellH) : dx != 0 ? CellW : CellH;
            return step * mult.Value * (up > 0 ? Math.Pow(R.Map.Height.ClimbCost, up) : 1);
        }

        // Ближайшая проходимая клетка (цель в воде или в стене — встаём рядом); −1 — на карте пройти негде
        public int NearestPassable(int i)
        {
            if (Passable(i)) return i;
            int x0 = i % W, y0 = i / W;
            for (int r = 1; r < Math.Max(W, H); r++)
            {
                int best = -1; double bd = double.MaxValue;
                for (int y = y0 - r; y <= y0 + r; y++)
                    for (int x = x0 - r; x <= x0 + r; x++)
                    {
                        if (Math.Max(Math.Abs(x - x0), Math.Abs(y - y0)) != r || x < 0 || y < 0 || x >= W || y >= H) continue;
                        int j = y * W + x;
                        double d = (x - x0) * (x - x0) * CellW * CellW + (y - y0) * (y - y0) * CellH * CellH;
                        if (d < bd && Passable(j)) { bd = d; best = j; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        // Карта направлений к точке (tx, ty), м. Без местности — null: путь по прямой.
        // halfWidth > 0 — путь для строя такой полуширины (Г59); extraBlocked — клетки, которые обходить
        // как непроходимые (свой стоящий отряд, Г61). Без них цена — ровно зона досягаемости трекера.
        // Сама постройка дешёвая: местность — готовая (Ground), цены считаются по мере спроса (Search).
        public static FlowField Build(Geo geo, Rules r, bool horse, double tx, double ty, double halfWidth = 0, bool[] extraBlocked = null, BattleMap.PassRules pass = null)
        {
            var m = geo?.Map;
            if (m == null) return null;
            long t0 = Stopwatch.GetTimestamp();
            var f = new FlowField { Map = m, Geo = geo, R = r, Horse = horse, W = m.W, H = m.H, CellW = geo.W / m.W, CellH = geo.H / m.H, HalfWidth = halfWidth };
            var g = Ground.Of(m, r, horse, f.CellW, f.CellH, pass);
            // Г104: закрытые ворота для врага — как клетки в обход
            if (pass?.Blocked != null)
            {
                if (extraBlocked == null) extraBlocked = (bool[])pass.Blocked.Clone();
                else { extraBlocked = (bool[])extraBlocked.Clone(); for (int i = 0; i < extraBlocked.Length; i++) extraBlocked[i] |= pass.Blocked[i]; }
            }
            if (extraBlocked == null) { f.mult = g.Mult; f.large = g.Large; f.Clearance = g.Clear; }
            else
            {
                // клетки в обход — на копии общей местности; крупные препятствия и расстояние до них — заново
                var mult = (double[])g.Mult.Clone();
                for (int i = 0; i < mult.Length; i++) if (extraBlocked[i]) mult[i] = double.NaN;
                f.mult = mult;
                ClearanceOf(f.W, f.H, f.CellW, f.CellH, mult, extraBlocked, r.Move.SmallObstacleM, out f.large, out f.Clearance);
            }
            f.penHalf = halfWidth; f.penK = r.Move.ClearancePenalty; f.penEdge = Math.Min(f.CellW, f.CellH) / 2;   // от центра клетки до края препятствия
            f.Target = f.NearestPassable(f.CellOf(tx, ty));
            f.search = Search.For(f, g, r.Map.Height.ClimbCost, extraBlocked == null);
            Interlocked.Increment(ref StatBuilds);
            Interlocked.Add(ref StatBuildTicks, Stopwatch.GetTimestamp() - t0);
            return f;
        }

        // Счётчики для замера (bench-paths), на пути не влияют: построек карт направлений и время на них; время поиска
        // цен (он идёт и после постройки — когда спрашивают цену клетки) и сколько клеток осело; сколько раз поиск взят
        // у такой же живой карты направлений; сколько раз местность для путей считалась заново
        public static long StatBuilds, StatBuildTicks, StatSearchTicks, StatSettled, StatShared, StatGrounds;

        // Крупные препятствия и расстояние до них (Г59): связные пятна непроходимого (по 8 соседям); пятно, чья
        // рамка не больше SmallObstacleM, — мелкое. Свой стоящий отряд при обходе (extra) — всегда крупный.
        // Расстояние — фаской в два прохода (соседи по стороне и по диагонали), край карты — тоже препятствие.
        static void ClearanceOf(int W, int H, double CellW, double CellH, double[] mult, bool[] extra, double small, out bool[] large, out double[] clearance)
        {
            int n = W * H;
            var big = new bool[n];
            var seen = new bool[n];
            var comp = new List<int>(); var stack = new Stack<int>();
            for (int s0 = 0; s0 < n; s0++)
            {
                if (seen[s0] || !double.IsNaN(mult[s0])) continue;
                comp.Clear(); stack.Push(s0); seen[s0] = true;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue; bool anyExtra = false;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); comp.Add(i);
                    int x = i % W, y = i / W;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    if (extra != null && extra[i]) anyExtra = true;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + DX[k], ny = y + DY[k];
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int j = ny * W + nx;
                        if (!seen[j] && double.IsNaN(mult[j])) { seen[j] = true; stack.Push(j); }
                    }
                }
                if (anyExtra || (x1 - x0 + 1) * CellW > small || (y1 - y0 + 1) * CellH > small)
                    foreach (int i in comp) big[i] = true;
            }
            var d = new double[n];
            double dg = JsMath.Hypot(CellW, CellH);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    d[y * W + x] = big[y * W + x] ? 0
                        : Math.Min(Math.Min((x + 0.5) * CellW, (W - x - 0.5) * CellW), Math.Min((y + 0.5) * CellH, (H - y - 0.5) * CellH));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; double v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + CellW);
                    if (y > 0) v = Math.Min(v, d[i - W] + CellH);
                    if (x > 0 && y > 0) v = Math.Min(v, d[i - W - 1] + dg);
                    if (x < W - 1 && y > 0) v = Math.Min(v, d[i - W + 1] + dg);
                    d[i] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x; double v = d[i];
                    if (x < W - 1) v = Math.Min(v, d[i + 1] + CellW);
                    if (y < H - 1) v = Math.Min(v, d[i + W] + CellH);
                    if (x < W - 1 && y < H - 1) v = Math.Min(v, d[i + W + 1] + dg);
                    if (x > 0 && y < H - 1) v = Math.Min(v, d[i + W - 1] + dg);
                    d[i] = v;
                }
            large = big; clearance = d;
        }
        static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        // Местность для путей одной карты и рода войск — от цели не зависит, поэтому общая для всех карт направлений:
        // множители клеток (NaN — непроходимо), крупные препятствия, расстояние до них. Держится при карте (по ссылке на
        // TerrainMap, не мешая ей уйти из памяти) и перед каждой постройкой сверяется с ней: слои T и Z те же (пролом
        // в стене меняет T), множители видов местности по правилам те же, размер клетки и SmallObstacleM те же.
        // Не сошлось — считается заново; прежние виды карты (до пролома, другие правила) держатся, пока их не вытеснят.
        sealed class Ground
        {
            public readonly bool Horse, Battle, Walls, Gates; public readonly int W, H; public readonly double CellW, CellH, Small;
            readonly BattleMap.PassRules pass;                    // Г104: только флаги (и сам факт боя — ров, Г105) — закрытые ворота в карту кладёт Build
            public readonly byte[] T, Z;                          // снимок слоёв карты, по которому всё посчитано
            readonly double[] codeMult = new double[256];         // множитель по коду местности
            readonly int[] codeAt = new int[256];                 // первая клетка с этим кодом (−1 — кода на карте нет)
            public readonly double[] Mult, Clear; public readonly bool[] Large;
            public readonly double MinMult = double.PositiveInfinity, MaxMult = double.NegativeInfinity;   // по проходимым
            public readonly int ZSpan;                            // перепад высот на карте, уровней

            static readonly ConditionalWeakTable<TerrainMap, List<Ground>> byMap = new ConditionalWeakTable<TerrainMap, List<Ground>>();

            public static Ground Of(TerrainMap m, Rules r, bool horse, double cw, double ch, BattleMap.PassRules pass = null)
            {
                bool battle = pass != null, walls = battle && pass.Walls, gates = battle && pass.Gates;
                var list = byMap.GetValue(m, _ => new List<Ground>());
                lock (list)
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        var g = list[k];
                        if (!g.Fits(m, r, horse, cw, ch, battle, walls, gates)) continue;
                        if (k > 0) { list.RemoveAt(k); list.Insert(0, g); }
                        return g;
                    }
                    var ng = new Ground(m, r, horse, cw, ch, battle, walls, gates);
                    list.Insert(0, ng);
                    if (list.Count > 4) list.RemoveAt(list.Count - 1);
                    Interlocked.Increment(ref StatGrounds);
                    return ng;
                }
            }

            Ground(TerrainMap m, Rules r, bool horse, double cw, double ch, bool battle, bool walls, bool gates)
            {
                Horse = horse; Battle = battle; Walls = walls; Gates = gates; pass = battle ? new BattleMap.PassRules { Walls = walls, Gates = gates } : null;
                W = m.W; H = m.H; CellW = cw; CellH = ch; Small = r.Move.SmallObstacleM;
                T = (byte[])m.T.Clone(); Z = (byte[])m.Z.Clone();
                for (int c = 0; c < 256; c++) codeAt[c] = -1;
                // множитель клетки зависит только от её кода местности (BattleMap.MoveMult) — считается раз на код
                int n = W * H;
                Mult = new double[n];
                for (int i = 0; i < n; i++)
                {
                    int c = T[i];
                    if (codeAt[c] < 0) { codeAt[c] = i; codeMult[c] = BattleMap.MoveMult(m, i, horse, r, pass) ?? double.NaN; }
                    Mult[i] = codeMult[c];
                }
                for (int c = 0; c < 256; c++)
                    if (codeAt[c] >= 0 && !double.IsNaN(codeMult[c])) { MinMult = Math.Min(MinMult, codeMult[c]); MaxMult = Math.Max(MaxMult, codeMult[c]); }
                int zMin = 255, zMax = 0;
                for (int i = 0; i < n; i++) { zMin = Math.Min(zMin, Z[i]); zMax = Math.Max(zMax, Z[i]); }
                ZSpan = Math.Max(0, zMax - zMin);
                ClearanceOf(W, H, cw, ch, Mult, null, Small, out Large, out Clear);
            }

            bool Fits(TerrainMap m, Rules r, bool horse, double cw, double ch, bool battle, bool walls, bool gates)
            {
                if (Battle != battle || Walls != walls || Gates != gates) return false;
                if (Horse != horse || W != m.W || H != m.H || !SameBits(CellW, cw) || !SameBits(CellH, ch) || !SameBits(Small, r.Move.SmallObstacleM)) return false;
                if (!new ReadOnlySpan<byte>(T).SequenceEqual(m.T) || !new ReadOnlySpan<byte>(Z).SequenceEqual(m.Z)) return false;
                for (int c = 0; c < 256; c++)
                    if (codeAt[c] >= 0 && !SameBits(BattleMap.MoveMult(m, codeAt[c], horse, r, pass) ?? double.NaN, codeMult[c])) return false;
                return true;
            }
        }

        // Дейкстра от цели — по мере спроса. Спросили цену клетки — поиск идёт, пока она не станет окончательной.
        // Осевшие цены — те же, что при обходе всей карты: при равных ценах порядок обхода на них не влияет (прибавка
        // шага с плавающей точкой цену не уменьшает, и наименьшая по путям сумма у клетки одна). Шаг из i в j — ровно
        // Step(i, j) × Pen(j), как в BattleMap.Reach; слой высот и ClimbCost — на момент постройки.
        // Очередь — корзины по цене шириной delta, по кругу (Дейкстра — Дайал): delta не больше половины самого дешёвого
        // шага, поэтому клетки одной корзины друг друга удешевить не могут — их можно брать в любом порядке, цены от
        // этого не меняются; корзин в круге хватает на самый дорогой шаг. Если шаг может стоить ноль или корзин нужно
        // слишком много (странные правила) — двоичная куча.
        // Одинаковые карты направлений (без клеток в обход) делят один поиск, пока хоть одна из них жива.
        sealed class Search
        {
            public readonly double[] Cost;
            readonly Ground g; readonly int target; readonly double half, K, edge, climbCost;   // чем задан (для общего поиска)
            readonly int W, H;
            readonly double[] mult, clear; readonly byte[] Z;
            readonly double cw, ch, cd;
            readonly double[] climb = new double[256];   // ClimbCost ^ подъём
            struct Node { public double C; public int I; }
            readonly bool dial;                          // корзины (иначе куча)
            readonly double delta, inv; readonly int mask;
            Node[][] bucket; int[] bn; long cur;         // корзина b — в bucket[b & mask]; cur — нижняя непустая
            Node[] heap;
            int count;                                   // записей в очереди
            // Цены не больше top — окончательные, и у всех клеток, что на деле не дороже top, цена уже точная.
            // Куча: top — её наименьшая цена; корзины: нижний край текущей корзины. +∞ — поиск кончился
            double top;
            readonly object gate = new object();

            static readonly List<WeakReference<Search>> recent = new List<WeakReference<Search>>();
            public static Search For(FlowField f, Ground g, double climbCost, bool share)
            {
                if (!share) return new Search(f, g, climbCost);
                lock (recent)
                {
                    for (int k = recent.Count - 1; k >= 0; k--)
                    {
                        if (!recent[k].TryGetTarget(out var s)) { recent.RemoveAt(k); continue; }
                        if (s.g == g && s.target == f.Target && SameBits(s.half, f.penHalf) && SameBits(s.K, f.penK)
                            && SameBits(s.edge, f.penEdge) && SameBits(s.climbCost, climbCost))
                        {
                            Interlocked.Increment(ref StatShared);
                            return s;
                        }
                    }
                    var ns = new Search(f, g, climbCost);
                    recent.Add(new WeakReference<Search>(ns));
                    if (recent.Count > 32) recent.RemoveAt(0);
                    return ns;
                }
            }

            Search(FlowField f, Ground g, double climbCost)
            {
                this.g = g; target = f.Target; half = f.penHalf; K = f.penK; edge = f.penEdge; this.climbCost = climbCost;
                W = f.W; H = f.H; mult = f.mult; clear = f.Clearance; Z = g.Z;
                cw = f.CellW; ch = f.CellH; cd = JsMath.Hypot(cw, ch);
                for (int up = 1; up < 256; up++) climb[up] = Math.Pow(climbCost, up);
                Cost = new double[W * H];
                Array.Fill(Cost, double.PositiveInfinity);
                top = double.PositiveInfinity;
                if (target < 0) return;
                // самый дешёвый и самый дорогой шаг: длина × множитель клетки × подъём × близость к препятствию
                double c1 = g.ZSpan > 0 ? climb[1] : 1, cz = g.ZSpan > 0 ? climb[g.ZSpan] : 1;
                double climbLo = Math.Min(1, Math.Min(c1, cz)), climbHi = Math.Max(1, Math.Max(c1, cz));
                double penLo = half > 0 ? Math.Min(1, 1 + K) : 1, penHi = half > 0 ? Math.Max(1, 1 + K) : 1;
                double wMin = Math.Min(cw, ch) * g.MinMult * climbLo * penLo, wMax = cd * g.MaxMult * climbHi * penHi;
                double span = wMax / (wMin / 2);
                if (wMin > 0 && span < 1 << 16)
                {
                    dial = true; delta = wMin / 2; inv = 1 / delta;
                    int size = 4;
                    while (size < span + 4) size *= 2;
                    mask = size - 1; bucket = new Node[size][]; bn = new int[size];
                }
                else heap = new Node[256];
                Cost[target] = 0; Push(0, target); top = 0;
            }

            public double Frontier => Volatile.Read(ref top);
            // Окончательная цена клетки i
            public double At(int i)
            {
                double fr = Volatile.Read(ref top), c = Cost[i];
                if (c <= fr) return c;
                lock (gate) Run(i);
                return Cost[i];
            }
            public void Complete()
            {
                if (Volatile.Read(ref top) == double.PositiveInfinity) return;
                lock (gate) Run(-1);
            }
            // Поиск — пока клетка i не станет окончательной (i < 0 — до конца)
            void Run(int i)
            {
                long t0 = Stopwatch.GetTimestamp(), settled = 0;
                double t = top;
                while (count > 0 && (i < 0 || !(Cost[i] <= t)))
                {
                    var e = Take();
                    if (Settle(e.C, e.I)) settled++;
                    t = count == 0 ? double.PositiveInfinity : dial ? cur * delta : heap[0].C;
                }
                if (count == 0) { t = double.PositiveInfinity; bucket = null; bn = null; heap = null; }
                Volatile.Write(ref top, t);
                Interlocked.Add(ref StatSettled, settled);
                Interlocked.Add(ref StatSearchTicks, Stopwatch.GetTimestamp() - t0);
            }

            // Клетка j с ценой c из очереди: пересчитать соседей — шаг из соседа i в j (путь идёт к цели);
            // false — запись устарела (клетку уже нашли дешевле)
            bool Settle(double c, int j)
            {
                if (c > Cost[j]) return false;
                int jy = j / W, jx = j - jy * W;
                double mj = mult[j], pj = PenAt(clear, j, half, K, edge);
                double sx = cw * mj, sy = ch * mj, sd = cd * mj;   // длина шага × множитель клетки, куда шагают (как Step)
                int zj = Z[j];
                if (jx > 0 && jy > 0 && jx < W - 1 && jy < H - 1)
                {
                    // соседи в порядке DX, DY; по диагонали — с проверкой двух клеток по сторонам
                    int north = j - W, south = j + W;
                    Diag(c, pj, zj, sd, north - 1, north, j - 1);
                    Side(c, pj, zj, sy, north);
                    Diag(c, pj, zj, sd, north + 1, north, j + 1);
                    Side(c, pj, zj, sx, j - 1);
                    Side(c, pj, zj, sx, j + 1);
                    Diag(c, pj, zj, sd, south - 1, south, j - 1);
                    Side(c, pj, zj, sy, south);
                    Diag(c, pj, zj, sd, south + 1, south, j + 1);
                    return true;
                }
                for (int k = 0; k < 8; k++)
                {
                    int dx = DX[k], dy = DY[k], nx = jx + dx, ny = jy + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    if (dx != 0 && dy != 0) Diag(c, pj, zj, sd, ny * W + nx, ny * W + jx, jy * W + nx);
                    else Side(c, pj, zj, dx != 0 ? sx : sy, ny * W + nx);
                }
                return true;
            }
            // по диагонали нельзя протиснуться между двумя непроходимыми (a, b — соседние по сторонам)
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Diag(double c, double pj, int zj, double s, int i, int a, int b)
            {
                if (double.IsNaN(mult[a]) && double.IsNaN(mult[b])) return;
                Side(c, pj, zj, s, i);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Side(double c, double pj, int zj, double s, int i)
            {
                if (double.IsNaN(mult[i])) return;
                int up = zj - Z[i];
                if (up > 0) s *= climb[up];
                double nc = c + s * pj;
                if (nc < Cost[i]) { Cost[i] = nc; Push(nc, i); }
            }

            void Push(double c, int i)
            {
                count++;
                if (dial)
                {
                    int s = (int)((long)(c * inv) & mask);
                    var a = bucket[s]; int k = bn[s];
                    if (a == null) bucket[s] = a = new Node[16];
                    else if (k == a.Length) { Array.Resize(ref a, k * 2); bucket[s] = a; }
                    a[k].C = c; a[k].I = i; bn[s] = k + 1;
                    return;
                }
                if (count > heap.Length) Array.Resize(ref heap, heap.Length * 2);
                int h = count - 1;
                while (h > 0)
                {
                    int p = (h - 1) >> 1;
                    if (heap[p].C <= c) break;
                    heap[h] = heap[p]; h = p;
                }
                heap[h].C = c; heap[h].I = i;
            }
            // Запись с наименьшей ценой (из корзин — любая из нижней непустой корзины)
            Node Take()
            {
                count--;
                if (dial)
                {
                    int s;
                    while (bn[s = (int)(cur & mask)] == 0) cur++;
                    return bucket[s][--bn[s]];
                }
                var top0 = heap[0]; var last = heap[count];
                int k = 0;
                while (true)
                {
                    int l = 2 * k + 1;
                    if (l >= count) break;
                    if (l + 1 < count && heap[l + 1].C < heap[l].C) l++;
                    if (!(heap[l].C < last.C)) break;
                    heap[k] = heap[l]; k = l;
                }
                heap[k] = last;
                return top0;
            }
        }

        // Ширина прохода поперёк направления (dx, dy) — единичный вектор — в точке (x, y): сколько свободно влево
        // и вправо до крупного препятствия или края карты, но не дальше maxHalf в каждую сторону (Г59)
        public (double left, double right) Corridor(double x, double y, double dx, double dy, double maxHalf, double slowK = 0) =>
            (Probe(x, y, -dy, dx, maxHalf, slowK), Probe(x, y, dy, -dx, maxHalf, slowK));
        // Грубо — шагом в полклетки, потом делением пополам до 5 см: мост в 15 м должен мериться как 15, а не как 10.
        // slowK > 0 (Г111 п.5): клетка дороже клетки оси в slowK раз (брод рядом с мостом, лес у дороги) — тоже стенка
        double Probe(double x, double y, double px, double py, double max, double slowK = 0)
        {
            double axisMult = Inside(x, y) ? mult[CellOf(x, y)] : 1; if (double.IsNaN(axisMult)) slowK = 0;
            bool Blocked(double s)
            {
                double qx = x + px * s, qy = y + py * s;
                if (!Inside(qx, qy)) return true;
                int c = CellOf(qx, qy);
                return large[c] || slowK > 0 && !double.IsNaN(mult[c]) && mult[c] > axisMult * slowK;
            }
            double step = Math.Min(CellW, CellH) / 2;
            for (double s = step; s <= max; s += step)
            {
                if (!Blocked(s)) continue;
                double lo = s - step, hi = s;
                while (hi - lo > 0.05) { double mid = (lo + hi) / 2; if (Blocked(mid)) hi = mid; else lo = mid; }
                return lo;
            }
            return Blocked(max) ? max - step : max;
        }

        // Куда шагать из клетки i: сосед, через которого путь до цели дешевле всего; −1 — это цель или не дойти
        // Г111 п.5: шаг от клетки from к клетке to в обход непроходимого — поиском в ширину в окне ±maxR клеток (боец идёт к своему
        // месту в строю, а не к цели отряда, куда ведёт вся карта); −1 — не найти (дальше окна или отрезано)
        public int LocalStep(int from, int to, int maxR)
        {
            if (from == to || from < 0 || to < 0) return -1;
            int fx = from % W, fy = from / W, tx = to % W, ty = to / W;
            if (Math.Abs(tx - fx) > maxR || Math.Abs(ty - fy) > maxR) return -1;
            int x0 = Math.Max(0, Math.Min(fx, tx) - maxR), x1 = Math.Min(W - 1, Math.Max(fx, tx) + maxR), y0 = Math.Max(0, Math.Min(fy, ty) - maxR), y1 = Math.Min(H - 1, Math.Max(fy, ty) + maxR);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var prev = new int[w * h]; for (int k = 0; k < prev.Length; k++) prev[k] = -2;
            var q = new Queue<int>(); int Loc(int c) => (c / W - y0) * w + (c % W - x0);
            prev[Loc(to)] = -1; q.Enqueue(to);   // от цели к бойцу: первый шаг — сосед бойца, что ближе к цели
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int cx = c % W, cy = c / W;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Terrain.N8X[d], ny = cy + Terrain.N8Y[d];
                    if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                    int n = ny * W + nx, l = (ny - y0) * w + (nx - x0);
                    if (prev[l] != -2 || !Passable(n)) continue;
                    if (Terrain.N8X[d] != 0 && Terrain.N8Y[d] != 0 && !Passable(cy * W + nx) && !Passable(ny * W + cx)) continue;   // по диагонали между двумя непроходимыми — нет
                    prev[l] = c;
                    if (n == from) return c;
                    q.Enqueue(n);
                }
            }
            return -1;
        }
        public int Next(int i)
        {
            if (i == Target) return -1;
            double ci = search.At(i);
            if (double.IsInfinity(ci)) return -1;
            // клетка, чья цена пока выше fr, ещё не досчитана, но на деле дороже fr ≥ ci — к цели через неё не ближе,
            // её пропускаем, как пропустил бы и полный обход карты; остальные — уже с точной ценой
            double fr = search.Frontier;
            var cost = search.Cost;
            int ix = i % W, iy = i / W, best = -1;
            double bv = double.PositiveInfinity;
            for (int k = 0; k < 8; k++)
            {
                int nx = ix + DX[k], ny = iy + DY[k];
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                int j = ny * W + nx;
                double cj = cost[j];
                if (cj > fr || !(cj < ci)) continue;
                var s = Step(i, j);
                if (s == null) continue;
                double v = s.Value * Pen(j) + cj;
                if (v < bv) { bv = v; best = j; }
            }
            return best;
        }

        // Цепочка клеток от start до цели по карте направлений; null — не дойти
        public List<int> Chain(int start)
        {
            if (double.IsInfinity(CostAt(start))) return null;
            var chain = new List<int> { start };
            for (int i = start; i != Target;)
            {
                i = Next(i);
                if (i < 0) return null;
                chain.Add(i);
            }
            return chain;
        }

        // Путь центра строя из (sx, sy) к (tx, ty), м: ломаная. Цепочка клеток натягивается как нить:
        // от текущей точки — к самому дальнему узлу, прямой отрезок до которого не дороже цепочки.
        public List<(double x, double y)> Route(double sx, double sy, double tx, double ty)
        {
            int s = CellOf(sx, sy);
            var chain = Passable(s) ? Chain(s) : null;
            if (chain == null) return null;
            var end = CellOf(tx, ty) == Target && Inside(tx, ty) ? (tx, ty) : CenterOf(Target);
            var nodes = new List<(double x, double y, double c)>();
            for (int k = 1; k < chain.Count; k++) { var (x, y) = CenterOf(chain[k]); nodes.Add((x, y, CostAt(chain[k]))); }
            if (nodes.Count == 0) nodes.Add((end.Item1, end.Item2, 0));
            else nodes[nodes.Count - 1] = (end.Item1, end.Item2, 0);

            // цепочка считается от центров клеток, а строй стоит и встаёт не в центре: на концах пути —
            // поправка на этот сдвиг (по неравенству треугольника прямая не длиннее «до центра + цепочка»)
            var (scx, scy) = CenterOf(s); var (tcx, tcy) = CenterOf(Target);
            double startSlack = JsMath.Hypot(sx - scx, sy - scy) * (Mult(s) ?? 1) * Pen(s);
            double endSlack = JsMath.Hypot(end.Item1 - tcx, end.Item2 - tcy) * (Mult(Target) ?? 1) * Pen(Target);
            var route = new List<(double x, double y)> { (sx, sy) };
            double cx = sx, cy = sy, cc = CostAt(s) + startSlack;
            for (int idx = -1; idx < nodes.Count - 1;)
            {
                int best = idx + 1;
                for (int k = idx + 2; k < nodes.Count; k++)
                {
                    double slack = k == nodes.Count - 1 ? endSlack : 0;
                    if (SegmentCost(cx, cy, nodes[k].x, nodes[k].y, routing: true) <= cc - nodes[k].c + slack + 1e-6) best = k;
                    else break;
                }
                idx = best; cx = nodes[idx].x; cy = nodes[idx].y; cc = nodes[idx].c;
                route.Add((cx, cy));
            }
            // натянутая нить зацепила угол непроходимой клетки — идём по цепочке целиком
            if (Track.Build(this, route) == null)
            {
                route = new List<(double x, double y)> { (sx, sy), CenterOf(s) };
                foreach (var n in nodes) route.Add((n.x, n.y));
            }
            return route;
        }

        // Отрезок по клеткам: куски «одна клетка — один кусок». Цена куска = длина × множитель местности,
        // в клетку, куда поднялись (выше предыдущей), — ещё × ClimbCost на каждый уровень, как шаг дейкстры.
        // Диагональ точно через угол — как шаг дейкстры: нельзя, если обе соседние по сторонам непроходимы.
        // false — отрезок упёрся в непроходимое или ушёл с карты. cost — сюда прибавляется цена отрезка:
        // норма (routing = false) или цена для выбора пути — с ценой близости к крупным препятствиям (Г59).
        public bool Walk(double ax, double ay, double bx, double by, ref int prevCell, int leg, List<Track.Piece> outp, ref double cost, bool routing = false)
        {
            double dx = bx - ax, dy = by - ay, len = JsMath.Hypot(dx, dy);
            if (len == 0) return true;
            if (!Inside(ax, ay) || !Inside(bx, by)) return false;
            int cx = (int)Math.Min(W - 1, Math.Floor(ax / CellW)), cy = (int)Math.Min(H - 1, Math.Floor(ay / CellH));
            int stepX = Math.Sign(dx), stepY = Math.Sign(dy);
            double tMaxX = dx != 0 ? ((stepX > 0 ? (cx + 1) * CellW : cx * CellW) - ax) / dx : double.PositiveInfinity;
            double tMaxY = dy != 0 ? ((stepY > 0 ? (cy + 1) * CellH : cy * CellH) - ay) / dy : double.PositiveInfinity;
            double tdX = dx != 0 ? CellW / Math.Abs(dx) : double.PositiveInfinity, tdY = dy != 0 ? CellH / Math.Abs(dy) : double.PositiveInfinity;
            double t = 0;
            while (true)
            {
                double tNext = Math.Min(1, Math.Min(tMaxX, tMaxY));
                int cell = cy * W + cx;
                if (tNext - t > 1e-12)
                {
                    var mult = Mult(cell);
                    if (mult == null) return false;
                    int up = prevCell >= 0 ? Map.Z[cell] - Map.Z[prevCell] : 0;
                    double rho = mult.Value * (up > 0 ? Math.Pow(R.Map.Height.ClimbCost, up) : 1);
                    // «поднялись в клетку» — на всём куске в ней; дальше по той же клетке — уже без подъёма
                    var last = outp != null && outp.Count > 0 ? outp[outp.Count - 1] : null;
                    if (cell == prevCell && last != null && last.Cell == cell) rho = last.Rho;
                    outp?.Add(new Track.Piece
                    {
                        X0 = ax + dx * t, Y0 = ay + dy * t, X1 = ax + dx * tNext, Y1 = ay + dy * tNext,
                        Len = (tNext - t) * len, Rho = rho, Cell = cell, Leg = leg,
                    });
                    cost += (tNext - t) * len * rho * (routing ? Pen(cell) : 1);
                    prevCell = cell;
                }
                if (tNext >= 1) return true;
                if (Math.Abs(tMaxX - tMaxY) < 1e-12)
                {
                    int ox = cx + stepX, oy = cy + stepY;
                    if (ox < 0 || oy < 0 || ox >= W || oy >= H) return false;
                    if (Mult(cy * W + ox) == null && Mult(oy * W + cx) == null) return false;
                    cx = ox; cy = oy; t = tMaxX; tMaxX += tdX; tMaxY += tdY;
                }
                else if (tMaxX < tMaxY) { cx += stepX; t = tMaxX; tMaxX += tdX; }
                else { cy += stepY; t = tMaxY; tMaxY += tdY; }
                if (cx < 0 || cy < 0 || cx >= W || cy >= H) return false;
            }
        }
        // Цена прямого отрезка: м нормы или (routing) цена для выбора пути; бесконечность — не пройти
        public double SegmentCost(double ax, double ay, double bx, double by, bool routing = false)
        {
            double cost = 0;
            int prev = CellOf(ax, ay);
            return Walk(ax, ay, bx, by, ref prev, 0, null, ref cost, routing) ? cost : double.PositiveInfinity;
        }
    }

    // Путь, промеренный по местности: куски по клеткам, от каждого — сколько метров и сколько нормы
    public sealed class Track
    {
        public sealed class Piece
        {
            public double X0, Y0, X1, Y1, Len, Rho, S0, C0;   // Rho — сколько нормы стоит метр; S0, C0 — метров и нормы до куска
            public int Cell, Leg;
        }
        public List<Piece> Pieces = new List<Piece>();
        public List<(double x, double y)> Points;
        public double Length, Cost;

        // Без карты (f == null) — по прямой, метр за метр нормы
        public static Track Build(FlowField f, List<(double x, double y)> route)
        {
            // повторы подряд — вон: у отрезка нулевой длины нет направления
            var pts = new List<(double x, double y)> { route[0] };
            foreach (var q in route)
                if (JsMath.Hypot(q.x - pts[pts.Count - 1].x, q.y - pts[pts.Count - 1].y) > 1e-9) pts.Add(q);
            var tr = new Track { Points = pts };
            int prev = f != null ? f.CellOf(pts[0].x, pts[0].y) : -1;
            double cost = 0;
            for (int k = 1; k < pts.Count; k++)
            {
                var (ax, ay) = pts[k - 1]; var (bx, by) = pts[k];
                if (f == null)
                {
                    double len = JsMath.Hypot(bx - ax, by - ay);
                    if (len > 0) tr.Pieces.Add(new Piece { X0 = ax, Y0 = ay, X1 = bx, Y1 = by, Len = len, Rho = 1, Cell = -1, Leg = k - 1 });
                }
                else if (!f.Walk(ax, ay, bx, by, ref prev, k - 1, tr.Pieces, ref cost)) return null;
            }
            foreach (var p in tr.Pieces) { p.S0 = tr.Length; p.C0 = tr.Cost; tr.Length += p.Len; tr.Cost += p.Len * p.Rho; }
            return tr;
        }

        // Точка пути, до которой потрачено cost нормы, и номер куска
        public (double x, double y, int piece) At(double cost)
        {
            if (Pieces.Count == 0) return (Points[0].x, Points[0].y, -1);
            int lo = 0, hi = Pieces.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Pieces[mid].C0 <= cost) lo = mid; else hi = mid - 1; }
            var p = Pieces[lo];
            double k = Math.Max(0, Math.Min(1, (cost - p.C0) / (p.Len * p.Rho)));
            return (p.X0 + (p.X1 - p.X0) * k, p.Y0 + (p.Y1 - p.Y0) * k, lo);
        }
        // Точка пути в s метрах от начала, номер куска и направление (единичный вектор) — для взгляда вперёд (Г59)
        public (double x, double y, double dx, double dy) AtMeters(double s)
        {
            if (Pieces.Count == 0) return (Points[0].x, Points[0].y, 0, -1);
            int lo = 0, hi = Pieces.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Pieces[mid].S0 <= s) lo = mid; else hi = mid - 1; }
            var p = Pieces[lo];
            double k = Math.Max(0, Math.Min(1, (s - p.S0) / p.Len));
            return (p.X0 + (p.X1 - p.X0) * k, p.Y0 + (p.Y1 - p.Y0) * k, (p.X1 - p.X0) / p.Len, (p.Y1 - p.Y0) / p.Len);
        }
        public double MetersAt(double cost)
        {
            var (_, _, i) = At(cost);
            if (i < 0) return 0;
            var p = Pieces[i];
            return p.S0 + Math.Max(0, Math.Min(p.Len, (cost - p.C0) / p.Rho));
        }
    }
}
