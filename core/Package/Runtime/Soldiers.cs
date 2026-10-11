// ═══════════ Soldiers.cs — живые бойцы внутри фигурок (Г75–Г78) — ЧЕРНОВИК ДО ГМа ═══════════
// Фигурка — тело строя: столкновения отрядов, касания и бой идут по ней (Г5, Г27), правила и сверка со столом — те же.
// Внутри фигурки — бойцы поимённо. У каждого место в фигурке (её оси) с небольшим личным смещением; боец догоняет
// место с запаздыванием ~Tau (Г76), со своим пределом скорости и разгона; расталкивается с любым бойцом рядом — своим
// и чужим (Г77), но фигурки этим не двигает. В схватке передняя шеренга касающихся фигурок делает шаг к врагу и
// бьётся выпадами (Г78). Стрела бьёт в бойца там, где он стоит (Man.Body, Г33); в рукопашной падают те, кто у врага;
// при раскладке (Г30, В14) на место павшего выходит стоящий за ним; между фигурками строй смыкается понемногу — по одному
// бойцу в секунду к соседке; на новое место идут шагом, далёкое — догоняют трусцой, без мгновенных переносов.
// Случайность только хешем от номеров — генератор боя не трогаем.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Man
    {
        public int Id;               // постоянный номер бойца в отряде (с 1)
        public FigState Fig;         // чья фигурка
        public double Lx, Ly;        // место в фигурке, её оси: x — вдоль фронта вправо, y — вглубь (минус — вперёд)
        public int Row;              // ряд в фигурке (0 — передний)
        public double X, Y, Vx, Vy, Facing;
        public double LastVx, LastVy;   // Г118: скорость тела на прошлом шаге по сдвигу — для предела разгона
        public int LastFlag, LastBlocked;   // Г118: флаги шага тел и в кого упёрся на прошлом шаге — для зондов
        public bool Pushed;                 // Г118: на прошлом шаге сдвинут толчком — следующий шаг без предела разгона и тормоза
        public double BodyFacing;    // Г111 п.2: курс капсулы тела в толкотне (у коня в строю — по колонне, не по его взгляду); рисовать коня по нему — тогда рисунок не въезжает в чужой строй
        public double Z;             // Г104: на чём стоит над землёй клетки — верх стены или башни (0 — земля); для рисунка и стрел
        public double PostX = double.NaN, PostY, PostFacing = double.NaN;   // Г108: пост вместо места в строю (полководец в круге, стража по кольцу); NaN — нет
        public bool InDuel;          // Г108: полководец в поединке — круг его не выталкивает
        public bool Guard;           // Г121: в свите полководца (Mover.Guard) — для рисунка
        public bool Alive = true;
        public double Hu;            // своя доля 0…1 (хеш номера): фаза выпадов и виляния
        public Body Body;            // мишень для стрел (Г33) — та же точка
        public bool Reseat;          // В14: идёт на новое место (дальше ReseatM) — шагом, без подтягивания
        public double ViaX = double.NaN, ViaY;   // Б1: к месту напрямик не пройти (вода, стена) — идёт к этой клетке по карте отряда
        // Б2 (Г83): с кем бьётся (враг в досягаемости, null — ни с кем), когда последний раз ударил и когда принял удар на
        // щит (часы боя, NaN — не было) — для рисунка; NextSwing, SwingN — ритм ударов
        public Man Foe;
        public double SwingAt = double.NaN, ParryAt = double.NaN, NextSwing = double.NaN;
        public int SwingN;
        // Г90: сбит с ног натиском — лежит ещё DownLeft с (не идёт, не бьёт, его перешагивают); DownAt — когда сбит (часы боя)
        public double DownLeft, DownAt = double.NaN;
        // Г86: заморожен на своём сдвиге от якоря (оси колонны) — идёт одним телом с колонной; WasRigid — был заморожен на прошлом шаге
        public double RLx, RLy; public bool WasRigid;
        public bool Thaw;   // Г86: на жёсткого налез чужой боец — на следующем шаге оттаивает (иначе, как стена, уносит застрявшего с собой)
        public int Knocks;   // Г90: скольких сбил конь за этот натиск (больше ChargeKnocks — натиск его кончился)
        // Г87: тело из Men человек (одного ряда в глубину); Lost — сколько из них выбыло. Потеря — Wound(): падает всё тело,
        // когда выбыл последний. Рисунок: Men − Lost спутников за телом
        public int Men = 1, Lost;
        public bool Wound()
        {
            Lost++;
            if (Lost < Men) return false;
            Alive = false; Foe = null;
            if (Body != null) Body.Alive = false;
            return true;
        }
    }

    public static class Soldiers
    {
        // Курс фигурки на деле: развёрнута не по строю (охват, бегство своим курсом) — по её оси, повёрнутой туда,
        // куда она смотрит; иначе — по строю
        public static double FigHeading(Mover m, FigState s)
        {
            if (!s.Turned) return s.Face == 0 ? m.P.Facing : MoveSim.Norm(m.P.Facing + s.Face);   // Г106: грань каре и круга смотрит наружу
            double want = s.Wrap ? s.WH : m.Fleeing && !double.IsNaN(s.FleeH) ? s.FleeH : m.P.Facing, a = s.Axis;
            if (Math.Abs(MoveSim.AngleDiff(a, want)) > 90) a += 180;
            return MoveSim.Norm(a);
        }

        // Место бойца в мире (без выпадов и толпы) — куда он стремится в строю
        public static (double x, double y) HomeOf(Mover m, Man man)
        {
            if (!double.IsNaN(man.PostX)) return (man.PostX, man.PostY);   // Г108
            World(m, man.Fig, man.Lx, man.Ly, out var x, out var y);
            return (x, y);
        }

        static Rules.FormationR Grid(Unit u, Rules r) => Formation.For(u, r);   // Г106: с разомкнутыми рядами — шаг и глубина шире
        // Г87: тел на men человек при k человек в теле; полудлина капсулы тела вдоль колонны (конь — своя, плюс k − 1 рядов)
        internal static int BodiesOf(double men, int k) => (int)Math.Ceiling(Math.Max(0, Math.Round(men)) / Math.Max(1, k));
        internal static double BodyHalf(Mover m, Rules r) => BodyHalf(m, r, m.BodyK);   // самое длинное тело отряда
        internal static double BodyHalf(Mover m, Rules r, int men)   // тело из men человек (остаток короче)
        {
            var f = Grid(m.P.U, r);
            return (BattleMap.IsHorse(m.P.U) ? r.Move.HorseHalfShare * f.RankDepth : 0) + (Math.Max(1, men) - 1) * f.RankDepth / 2;
        }

        // Места бойцов в фигурке k на count бойцов: сетка шагом PerMan × RankDepth, ряды спереди назад, неполный ряд —
        // по центру; бойцов больше, чем сетка, — лишние встают рядами сзади. Г87: тело из kk человек занимает kk рядов —
        // стоит посередине них, его ряд — номер первого из них (ряды в людях: пики, волна, бегство считают так же)
        static List<(double ox, double oy, int row)> SlotsOf(Mover m, int k, Rules r, int count)
        {
            var fg = m.P.Figs[k]; var f = Grid(m.P.U, r); int kk = Math.Max(1, m.BodyK);
            int cols = Math.Max(1, (int)Math.Round(fg.Width / f.PerMan));
            int left = count;
            var list = new List<(double, double, int)>();
            for (int j = 0; left > 0; j++)
            {
                int n = Math.Min(cols, left); double off = kk > 1 ? 0 : (cols - n) / 2.0; left -= n;   // тела (kk > 1) — в своих файлах, без центровки
                for (int i = 0; i < n; i++) list.Add((-fg.Width / 2 + (off + i + 0.5) * f.PerMan, -fg.Depth / 2 + (j * kk + (kk - 1) / 2.0 + 0.5) * f.RankDepth, j * kk));
            }
            return list;
        }

        // Г87: место тела в файле file, блок рядов block (центр блока из k рядов; остаток тело сдвинет само в Seat)
        static (double ox, double oy, int row) SlotAt(Mover m, int k, Rules r, int file, int block)
        {
            var fg = m.P.Figs[k]; var f = Grid(m.P.U, r); int kk = Math.Max(1, m.BodyK);
            return (-fg.Width / 2 + (file + 0.5) * f.PerMan, -fg.Depth / 2 + (block * kk + (kk - 1) / 2.0 + 0.5) * f.RankDepth, block * kk);
        }

        static void World(Mover m, FigState s, double lx, double ly, out double x, out double y)
        {
            double h = FigHeading(m, s) * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            x = s.X + lx * rx - ly * fx; y = s.Y + lx * ry - ly * fy;
        }

        static void Seat(Mover m, Man man, FigState s, (double ox, double oy, int row) sl, Rules r)
        {
            var f = Grid(m.P.U, r); var M = r.Men;
            if (man.Fig != s) man.WasRigid = false;   // другая колонна — замороженный сдвиг от прежнего якоря (Г86) не годится: иначе прыжок на шаг колонн
            man.Fig = s; man.Row = sl.row;
            man.Lx = sl.ox + (MoveSim.Hash01(m.P.U.Id, man.Id, 11) - 0.5) * M.Jitter * f.PerMan;
            man.Ly = sl.oy + (MoveSim.Hash01(m.P.U.Id, man.Id, 12) - 0.5) * M.Jitter * f.RankDepth;
            man.Ly += (man.Men - Math.Max(1, m.BodyK)) / 2.0 * f.RankDepth;   // Г87: остаточное тело (людей меньше k) — посередине своих рядов
            World(m, s, man.Lx, man.Ly, out var hx, out var hy);
            if (JsMath.Hypot(hx - man.X, hy - man.Y) > M.ReseatM) man.Reseat = true;
        }

        // Разложить бойцов по фигуркам. spawn — новый отряд: бойцы встают ровно на места. Иначе (после потерь, Г30, В14):
        // бойцы остаются в своей фигурке; бойцы исчезнувших фигурок — к ближайшей. Внутри фигурки места спереди назад —
        // ближайшим: на место павшего шагает стоящий за ним, дыра уходит в задний ряд. Между фигурками после потерь
        // никто не бегает — строй смыкается постепенно (Balance в Step: по одному бойцу в секунду, к соседке)
        public static void Assign(Mover m, Rules r, bool spawn = false)
        {
            var P = m.P;
            if (spawn)
            {
                m.Men.Clear(); int kk = Math.Max(1, m.BodyK);
                for (int k = 0; k < P.Figs.Count; k++)
                {
                    // Г87: люди фигурки по файлам (полные ряды, неполный последний — по центру, как MenPositions); в каждом файле
                    // тела по kk человек рядами, последнее — остаток. Места — по файлу и блоку рядов, в порядке SlotsOf
                    var fgr = Grid(m.P.U, r); int cols = Math.Max(1, (int)Math.Round(P.Figs[k].Width / fgr.PerMan));
                    int menTotal = (int)Math.Round(P.Figs[k].Men), full = menTotal / cols, rem = menTotal % cols, off = (cols - rem) / 2;
                    var inFile = new int[cols];
                    for (int i = 0; i < cols; i++) inFile[i] = full + (i >= off && i < off + rem ? 1 : 0);
                    var bodies = new List<(int file, int block, int men)>();
                    for (int block = 0; ; block++)
                    {
                        bool any = false;
                        for (int i = 0; i < cols; i++) { int left = inFile[i] - block * kk; if (left > 0) { bodies.Add((i, block, Math.Min(kk, left))); any = true; } }
                        if (!any) break;
                    }
                    var slots = SlotsOf(m, k, r, bodies.Count);
                    for (int bi = 0; bi < bodies.Count; bi++)
                    {
                        var sl = kk > 1 ? SlotAt(m, k, r, bodies[bi].file, bodies[bi].block) : slots[bi];
                        var man = new Man { Id = ++m.NextManId, Men = bodies[bi].men };
                        man.Hu = MoveSim.Hash01(m.P.U.Id, man.Id, 13);
                        Seat(m, man, m.Figs[k], sl, r);
                        World(m, m.Figs[k], man.Lx, man.Ly, out man.X, out man.Y);
                        man.Reseat = false;
                        man.Facing = FigHeading(m, m.Figs[k]);
                        m.Men.Add(man);
                    }
                }
                m.MenVersion++;
                return;
            }
            m.Men.RemoveAll(x => !x.Alive);
            int nk = P.Figs.Count;
            if (nk == 0) { m.Men.Clear(); m.MenVersion++; return; }
            var idx = new Dictionary<FigState, int>();
            for (int k = 0; k < nk; k++) idx[m.Figs[k]] = k;
            var members = Enumerable.Range(0, nk).Select(_ => new List<Man>()).ToList();
            // сироты — в недобор только у пехоты и не в схватке: конь через строй к недобору идёт задом (Г94), а в схватке идущий
            // через строй выпадает из боя — потери стола падают (Г62, Г68); там — к ближайшей, как раньше
            bool toLacking = r.Men.OrphansToLacking && !BattleMap.IsHorse(P.U) && !m.InMelee;
            foreach (var man in m.Men)
            {
                if (man.Fig != null && idx.TryGetValue(man.Fig, out int k)) { members[k].Add(man); continue; }
                // фигурки нет — к ближайшей; у бойцов-тел (Б1) — сначала к ближайшей с недобором (павшие проредили колонны по всему
                // строю — осиротевшие заполняют их, а не встают хвостом за крайней: Г104, гарнизон в 3 шеренги вырастал хвостом во
                // двор), иначе к ближайшей, где бойцов меньше двух её норм: иначе колонна, рядом с которой убрали несколько крайних,
                // набирает сотню бойцов в хвост
                int best = -1, lack = -1, any = 0; double bd = double.MaxValue, ld = double.MaxValue, ad = double.MaxValue;
                for (int q = 0; q < nk; q++)
                {
                    double d = (m.Figs[q].X - man.X) * (m.Figs[q].X - man.X) + (m.Figs[q].Y - man.Y) * (m.Figs[q].Y - man.Y);
                    if (d < ad) { ad = d; any = q; }
                    int norm = Math.Max(1, BodiesOf(P.Figs[q].Men, m.BodyK));
                    if (r.Move.MenBodies && toLacking && members[q].Count < norm && d < ld) { ld = d; lack = q; }
                    if (r.Move.MenBodies && members[q].Count >= 2 * norm) continue;
                    if (d < bd) { bd = d; best = q; }
                }
                members[lack >= 0 ? lack : best >= 0 ? best : any].Add(man);
            }
            for (int k = 0; k < nk; k++) SeatFigure(m, k, members[k], r);
            m.MenVersion++;
        }

        // Места внутри фигурки (В14): каждый держит своё место. Ряды — спереди назад: в ряду остаются его бойцы, на пустое
        // место выходит ближайший по фронту из ряда позади (дальше — из следующих рядов, последними — пришедшие из других
        // фигурок); в ряду бойцы встают по порядку слева направо, никто никого не обходит. Неполный задний ряд — по
        // середине: при потере его бойцы сдвигаются на полшага
        static void SeatFigure(Mover m, int k, List<Man> members, Rules r)
        {
            var s = m.Figs[k];
            long seatKey = members.Count;
            foreach (var man in members) seatKey = seatKey * 1000003 + man.Id * 7 + (man.Fig == s ? 1 : 0);
            seatKey = seatKey * 31 + (long)Math.Round(m.P.Figs[k].Width * 100) * 7919 + (long)Math.Round(m.P.Figs[k].Depth * 100);
            if (seatKey == s.SeatKey && members.All(x => x.Fig == s)) return;   // состав и размер те же — места те же
            s.SeatKey = seatKey;
            double h = FigHeading(m, s) * Math.PI / 180, c = Math.Cos(h), sn = Math.Sin(h);
            // где боец сейчас в осях фигурки: свой — по своему месту, пришлый — по тому, где стоит; пришлые — за всеми рядами
            var buckets = new SortedDictionary<int, List<(Man man, double lx)>>();
            foreach (var man in members)
            {
                int row; double lx;
                if (man.Fig == s) { row = man.Row; lx = man.Lx; }
                else { double dx = man.X - s.X, dy = man.Y - s.Y; row = int.MaxValue; lx = dx * c + dy * sn; }
                if (!buckets.TryGetValue(row, out var b)) buckets[row] = b = new List<(Man, double)>();
                b.Add((man, lx));
            }
            var rows = SlotsOf(m, k, r, members.Count).GroupBy(q => q.row).OrderBy(g => g.Key).Select(g => g.OrderBy(q => q.ox).ToList()).ToList();
            var carry = new List<(Man man, double lx)>();
            for (int j = 0; j < rows.Count; j++)
            {
                var slots = rows[j]; int rowKey = slots[0].row;   // Г87: ряд тела — номер его первого ряда в людях
                var pool = new List<(Man man, double lx)>(carry); carry.Clear();
                if (buckets.TryGetValue(rowKey, out var own)) { pool.AddRange(own); buckets.Remove(rowKey); }
                if (pool.Count < slots.Count)
                {
                    // пустые места: каждый из ряда занимает ближайшее к себе свободное
                    var taken = new bool[slots.Count];
                    foreach (var p in pool.OrderBy(p => p.lx))
                    {
                        int bi = -1; double bd = double.MaxValue;
                        for (int q = 0; q < slots.Count; q++) if (!taken[q] && Math.Abs(slots[q].ox - p.lx) < bd) { bd = Math.Abs(slots[q].ox - p.lx); bi = q; }
                        if (bi >= 0) taken[bi] = true;
                    }
                    // на пустые — ближайший по фронту из ближайшего ряда позади
                    for (int q = 0; q < slots.Count && pool.Count < slots.Count; q++)
                    {
                        if (taken[q]) continue;
                        foreach (var key in buckets.Keys.ToList())
                        {
                            var b = buckets[key]; if (b.Count == 0) continue;
                            int bi = 0; double bd = double.MaxValue;
                            for (int i = 0; i < b.Count; i++) { double d = Math.Abs(b[i].lx - slots[q].ox); if (d < bd) { bd = d; bi = i; } }
                            pool.Add((b[bi].man, slots[q].ox)); b.RemoveAt(bi);
                            if (b.Count == 0) buckets.Remove(key);
                            taken[q] = true;
                            break;
                        }
                    }
                }
                if (pool.Count > slots.Count)
                {
                    // ряд стал короче — лишние (пришлые и самые задние, потом крайние) уходят в ряд позади
                    var keep = pool.OrderBy(p => p.man.Fig == s ? 0 : 1).ThenBy(p => p.man.Fig == s ? p.man.Row : 0)
                        .ThenBy(p => Math.Abs(p.lx)).Take(slots.Count).ToList();
                    foreach (var p in pool) if (!keep.Contains(p)) carry.Add(p);
                    pool = keep;
                }
                pool = pool.OrderBy(p => p.lx).ThenBy(p => p.man.Id).ToList();
                for (int i = 0; i < pool.Count && i < slots.Count; i++) Seat(m, pool[i].man, s, slots[i], r);
            }
        }

        // В14: смыкание между фигурками. Излишек фигурки — бойцов в ней сверх раскладки стола (Г30: фронт сужается с краёв,
        // и раскладка велит, где сколько). Фигурка, у которой излишек меньше, чем у соседки (ближе полутора размеров),
        // на BalanceDiff и больше, берёт у неё одного бойца — ближнего к себе; за раз — не больше одного на фигурку.
        // Так строй смыкается от краёв понемногу, каждый идёт только к соседней фигурке, а одна потеря строй не дёргает
        internal static void Balance(Mover m, Rules r)
        {
            var P = m.P; int nk = m.Figs.Count;
            if (nk < 2 || m.Fleeing) return;   // бегущая толпа строя не держит
            var idx = new Dictionary<FigState, int>();
            for (int k = 0; k < nk; k++) idx[m.Figs[k]] = k;
            var members = Enumerable.Range(0, nk).Select(_ => new List<Man>()).ToList();
            foreach (var man in m.Men) if (man.Alive && man.Fig != null && idx.TryGetValue(man.Fig, out int k)) members[k].Add(man);
            int Surplus(int k) => members[k].Count - BodiesOf(P.Figs[k].Men, m.BodyK);
            bool Still(int k) => !r.Men.BalanceStillOnly || !(m.Figs[k].Wrap || m.Figs[k].Returning);   // колонна в манёвре (охват, возврат) бойцами не меняется — иначе их места прыгают
            bool moved = false;
            foreach (int d in Enumerable.Range(0, nk).OrderBy(k => P.Figs[k].Rank).ThenBy(k => P.Figs[k].Y).ThenBy(k => P.Figs[k].X))
            {
                int best = -1; double bd = double.MaxValue;
                // Г104: у колонны недобор (павшие) — берёт из ближайшей с избытком по всему строю, не только от соседки: иначе бойцы
                // убранных крайних колонн идут к прореженному краю конвейером по одному в секунду через каждую колонну. Не в схватке:
                // там идущий через строй выпадает из боя, и потери стола падают на 8–10 % (Г62)
                bool lacking = r.Move.MenBodies && r.Men.BalanceAcross && !m.InMelee && Surplus(d) < 0;
                for (int q = 0; q < nk; q++)
                {
                    if (q == d || members[q].Count == 0 || Surplus(q) - Surplus(d) < r.Men.BalanceDiff || !Still(q) || !Still(d)) continue;
                    double lim = 1.6 * Math.Max(Math.Max(P.Figs[q].Width, P.Figs[q].Depth), Math.Max(P.Figs[d].Width, P.Figs[d].Depth));
                    double dist = JsMath.Hypot(m.Figs[q].X - m.Figs[d].X, m.Figs[q].Y - m.Figs[d].Y);
                    if ((dist <= lim || lacking && Surplus(q) > 0) && dist < bd) { bd = dist; best = q; }
                }
                if (best < 0) continue;
                var to = m.Figs[d];
                var man0 = members[best].OrderBy(x => (x.X - to.X) * (x.X - to.X) + (x.Y - to.Y) * (x.Y - to.Y)).ThenBy(x => x.Id).First();
                members[best].Remove(man0); members[d].Add(man0);
                SeatFigure(m, best, members[best], r); SeatFigure(m, d, members[d], r);
                moved = true;
            }
            if (moved) m.MenVersion++;
        }

        // Б3: колонна встала (не в схватке), а бойцы пришли вразнобой (старт волной, пробка у мест) и кто-то дальше SettleFarM от
        // места: места колонны раздаются заново — каждому ближайшее свободное (ближние пары первыми), если общий путь до мест короче
        // не меньше чем на SettleGainM. Никто не бежит через строй — каждый берёт место, у которого стоит. В ходу не трогаем: место
        // держит (В14). Между колоннами не меняем: раздача по отряду ломала возврат из охвата и бегство — снос в чужой ряд остаётся (Б4)
        internal static void Settle(Mover m, Rules r)
        {
            if (m.Fleeing) return;
            bool any = false;
            var mem = new List<Man>();
            foreach (var s in m.Figs)
            {
                if (s.Moving || s.Fighting) continue;
                mem.Clear();
                foreach (var man in m.Men) if (man.Alive && man.Fig == s && !man.Reseat) mem.Add(man);
                int n = mem.Count;
                if (n < 2) continue;
                var hx = new double[n]; var hy = new double[n]; var slot = new (double lx, double ly, int row)[n];
                double was = 0, maxFar = 0;
                for (int i = 0; i < n; i++)
                {
                    slot[i] = (mem[i].Lx, mem[i].Ly, mem[i].Row);
                    World(m, s, mem[i].Lx, mem[i].Ly, out hx[i], out hy[i]);
                    double d = JsMath.Hypot(mem[i].X - hx[i], mem[i].Y - hy[i]); was += d; if (d > maxFar) maxFar = d;
                }
                if (maxFar < r.Men.SettleFarM) continue;
                var take = new int[n]; var used = new bool[n];
                for (int i = 0; i < n; i++) take[i] = -1;
                double now = 0;
                for (int step = 0; step < n; step++)
                {
                    int bi = -1, bj = -1; double bd = double.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (take[i] >= 0) continue;
                        for (int j = 0; j < n; j++)
                        {
                            if (used[j]) continue;
                            double d = JsMath.Hypot(mem[i].X - hx[j], mem[i].Y - hy[j]);
                            if (d < bd) { bd = d; bi = i; bj = j; }
                        }
                    }
                    take[bi] = bj; used[bj] = true; now += bd;
                }
                if (was - now < r.Men.SettleGainM) continue;
                for (int i = 0; i < n; i++)
                {
                    var sl = slot[take[i]]; mem[i].Lx = sl.lx; mem[i].Ly = sl.ly; mem[i].Row = sl.row;
                    if (JsMath.Hypot(hx[take[i]] - mem[i].X, hy[take[i]] - mem[i].Y) > r.Men.ReseatM) mem[i].Reseat = true;   // далёкое новое место — шагом, не прыжком
                }
                any = true;
            }
            if (SwapAcross(m, r)) any = true;
            if (any) m.MenVersion++;
        }

        // Б5: обмен местами между соседними стоящими колоннами — боец, которому ближе место в соседней колонне, меняется с тем,
        // кому ближе его место (парами, численности колонн не меняются). Колонны в схватке, в манёвре и на ходу не трогаем
        static bool SwapAcross(Mover m, Rules r)
        {
            var P = m.P; int nk = m.Figs.Count; if (nk < 2 || m.Fleeing) return false;
            bool Still(FigState s) => !(s.Moving || s.Fighting || s.Wrap || s.Returning);
            var byFile = new Dictionary<int, List<int>>();
            for (int k = 0; k < nk && k < P.Figs.Count; k++) { if (!byFile.TryGetValue(P.Figs[k].File, out var l)) byFile[P.Figs[k].File] = l = new List<int>(); l.Add(k); }
            bool any = false; double gain = r.Men.SwapGainM;
            foreach (var man in m.Men)
            {
                if (!man.Alive || man.Fig == null || man.Reseat || !Still(man.Fig)) continue;
                World(m, man.Fig, man.Lx, man.Ly, out var hx, out var hy);
                double dA = JsMath.Hypot(hx - man.X, hy - man.Y);
                if (dA < r.Men.SettleFarM) continue;
                int ka = m.Figs.IndexOf(man.Fig); if (ka < 0 || ka >= P.Figs.Count) continue;
                int file = P.Figs[ka].File;
                Man best = null; double bestGain = gain;
                for (int df = -1; df <= 1; df += 2)
                {
                    if (!byFile.TryGetValue(file + df, out var cols)) continue;
                    foreach (int kb in cols)
                    {
                        var sb = m.Figs[kb]; if (!Still(sb)) continue;
                        foreach (var other in m.Men)
                        {
                            if (!other.Alive || other.Fig != sb || other.Reseat || other.Men != man.Men) continue;
                            World(m, sb, other.Lx, other.Ly, out var ox, out var oy);
                            double dB = JsMath.Hypot(ox - other.X, oy - other.Y);
                            double g = dA + dB - (JsMath.Hypot(ox - man.X, oy - man.Y) + JsMath.Hypot(hx - other.X, hy - other.Y));
                            if (g > bestGain) { bestGain = g; best = other; }
                        }
                    }
                }
                if (best == null) continue;
                (man.Fig, best.Fig) = (best.Fig, man.Fig); man.WasRigid = false; best.WasRigid = false;
                (man.Lx, best.Lx) = (best.Lx, man.Lx); (man.Ly, best.Ly) = (best.Ly, man.Ly); (man.Row, best.Row) = (best.Row, man.Row);
                // к новому месту — шагом (В14), не прыжком: жёсткий (Г86) стоит ровно на месте, и без этого он перескочил бы на новое
                World(m, man.Fig, man.Lx, man.Ly, out var nx1, out var ny1); man.Reseat = JsMath.Hypot(nx1 - man.X, ny1 - man.Y) > r.Men.ReseatM;
                World(m, best.Fig, best.Lx, best.Ly, out var nx2, out var ny2); best.Reseat = JsMath.Hypot(nx2 - best.X, ny2 - best.Y) > r.Men.ReseatM;
                any = true;
            }
            return any;
        }

        // Шаг бойцов: к своему месту с запаздыванием, выпады в схватке, толкотня со всеми. Фигурки не двигает.
        // Курс фигурки (синус, косинус) — один раз на фигурку за шаг; соседи для толкотни — сетка на массивах без выделений
        public static void Step(IList<Mover> ms, double dt, Rules r)
        {
            var M = r.Men;
            foreach (var m in ms)
            {
                if (m.Men.Count == 0) continue;
                int every = Math.Max(1, (int)Math.Round(M.BalanceSec / dt));
                if (m.Steps % every == 0) Balance(m, r);
                foreach (var s in m.Figs) { double h = FigHeading(m, s) * Math.PI / 180; s.Hc = Math.Cos(h); s.Hs = Math.Sin(h); s.Hd = FigHeading(m, s); }
                double amax = M.AccelK * MoveSim.FigAccel(m, r) * dt, time = m.Steps * dt;
                var F = m.Field;
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Fig == null) continue;
                    var s = man.Fig;
                    double lx = man.Lx, ly = man.Ly, hu = man.Hu;
                    if (m.Fleeing)
                    {
                        // толпа (Г70): места шире, каждый чуть виляет
                        lx *= M.FleeSpread; ly *= M.FleeSpread;
                        lx += Math.Sin(time * 2 * Math.PI / (2 + hu) + hu * 6.283) * M.FleeWanderM;
                    }
                    // место в мире: x = X + lx·(cos, sin) − ly·(sin, −cos)
                    double hx = s.X + lx * s.Hc - ly * s.Hs, hy = s.Y + lx * s.Hs + ly * s.Hc;
                    if (s.Fighting && man.Row == 0 && !m.Fleeing)
                    {
                        // Г78: передние касающихся фигурок — шаг к врагу и выпады вперёд-назад
                        double lunge = M.LungeM + M.LungeAmpM * Math.Sin(time * 2 * Math.PI / M.LungeSec + hu * 6.283);
                        hx += s.FightX * lunge; hy += s.FightY * lunge;
                    }
                    double cx = (hx - man.X) / M.Tau, cy = (hy - man.Y) / M.Tau;
                    // В14: место далеко (дальше ReseatRunM) — никогда не подтягивается, а догоняет; новое место (Reseat) —
                    // идёт шагом сверх хода фигурки, далёкое — трусцой; в бегущей толпе — трусцой
                    double far = JsMath.Hypot(hx - man.X, hy - man.Y);
                    if (far > M.ReseatRunM) man.Reseat = true;
                    if (man.Reseat)
                    {
                        if (far < M.ReseatM / 2) man.Reseat = false;
                        else
                        {
                            double lim = m.Fleeing || far > M.ReseatRunM ? M.ReseatRunMps : M.ReseatMps, c = Math.Sqrt(cx * cx + cy * cy);
                            if (c > lim) { cx *= lim / c; cy *= lim / c; }
                        }
                    }
                    double vx = s.Vx + cx, vy = s.Vy + cy;
                    double vmax = Math.Max(s.Vmax, M.WalkMin) * M.SpeedK, v2 = vx * vx + vy * vy;
                    if (v2 > vmax * vmax) { double k = vmax / Math.Sqrt(v2); vx *= k; vy *= k; }
                    double ax = vx - man.Vx, ay = vy - man.Vy, a2 = ax * ax + ay * ay;
                    if (a2 > amax * amax) { double k = amax / Math.Sqrt(a2); ax *= k; ay *= k; }
                    man.Vx += ax; man.Vy += ay;
                    double nx = man.X + man.Vx * dt, ny = man.Y + man.Vy * dt;
                    double ex = nx - hx, ey = ny - hy, e2 = ex * ex + ey * ey;
                    if (!man.Reseat && e2 > M.MaxLagM * M.MaxLagM) { double k = M.MaxLagM / Math.Sqrt(e2); nx = hx + ex * k; ny = hy + ey * k; }   // не отрывается от места
                    if (F == null || MoveSim.Free(F, nx, ny) || !MoveSim.Free(F, man.X, man.Y)) { man.X = nx; man.Y = ny; }
                    else { man.Vx = 0; man.Vy = 0; }
                    man.Facing = m.Fleeing && man.Vx * man.Vx + man.Vy * man.Vy > 0.25 ? MoveSim.HeadingOf(man.Vx, man.Vy) : s.Hd;
                }
            }
            Jostle(ms, r);
        }

        // Г77: толкотня — любые два бойца ближе суммы радиусов расходятся (поровну); радиус — доля шага в строю.
        // Сетка клеток 2 м по рамке всех бойцов: голова списка в клетке и «следующий» у бойца; пары — своя клетка и
        // четыре соседние (каждая пара — один раз)
        // свои у каждого потока: смотрелка считает бой в фоне, пока игра считает свой ход в главном потоке
        [ThreadStatic] static Man[] gMen; [ThreadStatic] static Mover[] gOwn; [ThreadStatic] static double[] gRad;
        [ThreadStatic] static int[] gNext, gHead;
        static void Jostle(IList<Mover> ms, Rules r)
        {
            var M = r.Men;
            const double cell = 2;
            int n = 0;
            foreach (var m in ms) n += m.Men.Count;
            if (n < 2) return;
            if (gMen == null || gMen.Length < n) { gMen = new Man[n * 2]; gOwn = new Mover[n * 2]; gRad = new double[n * 2]; gNext = new int[n * 2]; }
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int c = 0;
            foreach (var m in ms)
            {
                if (m.Men.Count == 0) continue;
                var f = Grid(m.P.U, r);
                double rad = M.BodyShare * Math.Min(f.PerMan, f.RankDepth);
                foreach (var man in m.Men)
                {
                    if (!man.Alive) continue;
                    gMen[c] = man; gOwn[c] = m; gRad[c] = rad; c++;
                    if (man.X < x0) x0 = man.X; if (man.X > x1) x1 = man.X;
                    if (man.Y < y0) y0 = man.Y; if (man.Y > y1) y1 = man.Y;
                }
            }
            if (c < 2) return;
            int W = (int)((x1 - x0) / cell) + 1, H = (int)((y1 - y0) / cell) + 1;
            if ((long)W * H > 4_000_000) return;   // бойцы разбросаны на десятки километров — толкаться некому
            if (gHead == null || gHead.Length < W * H) gHead = new int[W * H];
            Array.Fill(gHead, -1, 0, W * H);
            for (int i = 0; i < c; i++)
            {
                int cx = (int)((gMen[i].X - x0) / cell), cy = (int)((gMen[i].Y - y0) / cell), k = cy * W + cx;
                gNext[i] = gHead[k]; gHead[k] = i;
            }
            for (int cy = 0; cy < H; cy++)
                for (int cx = 0; cx < W; cx++)
                {
                    int k0 = cy * W + cx;
                    for (int i = gHead[k0]; i >= 0; i = gNext[i])
                    {
                        for (int j = gNext[i]; j >= 0; j = gNext[j]) Pair(i, j, M);   // своя клетка
                        // соседи: справа, снизу-слева, снизу, снизу-справа — каждая пара клеток один раз
                        if (cx + 1 < W) for (int j = gHead[k0 + 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                        if (cy + 1 < H)
                        {
                            int kb = k0 + W;
                            if (cx > 0) for (int j = gHead[kb - 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                            for (int j = gHead[kb]; j >= 0; j = gNext[j]) Pair(i, j, M);
                            if (cx + 1 < W) for (int j = gHead[kb + 1]; j >= 0; j = gNext[j]) Pair(i, j, M);
                        }
                    }
                }
        }
        static void Pair(int i, int j, Rules.MenR M)
        {
            var p = gMen[i]; var q = gMen[j];
            double dx = q.X - p.X, dy = q.Y - p.Y, need = gRad[i] + gRad[j], d2 = dx * dx + dy * dy;
            if (d2 >= need * need) return;
            double d = Math.Sqrt(d2);
            if (d < 1e-9) { double ang = MoveSim.Hash01(p.Id, q.Id, 14) * 6.283; dx = Math.Cos(ang); dy = Math.Sin(ang); d = 1; }
            double push = (need - Math.Min(d, need)) * M.PushShare / 2, ux = dx / d, uy = dy / d;
            Nudge(gOwn[i], p, -ux * push, -uy * push);
            Nudge(gOwn[j], q, ux * push, uy * push);
        }
        static void Nudge(Mover m, Man man, double dx, double dy)
        {
            double nx = man.X + dx, ny = man.Y + dy;
            if (m.Field != null && !MoveSim.Free(m.Field, nx, ny) && MoveSim.Free(m.Field, man.X, man.Y)) return;   // в воду и в стену не выталкиваем
            man.X = nx; man.Y = ny;
        }
    }
}
