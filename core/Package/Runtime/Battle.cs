// ═══════════ Battle.cs — бой в движении (И1, БД1, БД3, БД4; Г62–Г64, Г68–Г74, Г44, Г29, Г30) — ЧЕРНОВИК ДО ГМа ═══════════
// Один ход боя — то же движение (MoveSim.Step), а между его шагами — рукопашная. Схватка (Fight) — пара
// врагов, чьи фигурки коснулись (не дальше MeleeGap): обмены ударами по кругу в 15 с от касания (Г62) —
// сначала атаки того, кто начал (Г44), на каждую — ответ, если удар пришёл во фронт; потом атаки второго.
// Бой идёт через границу хода без перерыва. Урон течёт каждый шаг по формуле стола, как в MeleeSim; в конце
// окна — округление, летальность, БД. Бьют колонны фигурок, что касаются врага (Г27). Натиск (Г29) — всплеск
// в первые 2 с касания, цель в это время не отвечает: нужен приказ «натиск», конница и разбег ≥ 50 м по
// чистому (К29); пики во фронт его гасят. Павшие фигурки выпадают из строя, строй смыкается с краёв (Г30).
// Атакованный во фланг сам не поворачивается — ждёт приказа (Г63). Свисающие колонны огибают врага перед
// фронтом — во фланг и в тыл (Г68); колонны делятся между врагами, ответы — общие на круг (Г69).
// БД и бегство в ходу (БД4, Г70–Г74): после каждого удара с потерями — проверки, как подсказывает журнал стола;
// провал — толпа бежит прочь от врага на норме, свои рядом бросают проверку (каскадная паника); «сплотить» — бросок.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Fight
    {
        public Mover A, B;                  // A начинает обмены (Г44)
        public double T0, LastTouch;        // когда коснулись; когда касались последний раз (часы боя)
        public double CycleEnd;             // конец текущего круга обменов
        public bool Over, Touching;         // Touching — касаются сейчас; SA, SB — как касались в последний раз
        public Side SA = new Side(), SB = new Side();
        public double LossA, LossB;         // потери за этот ход — для журнала (сколько бойцов выбыло, по шагам)
        public List<string> Notes = new List<string>();
        internal List<Win> Wins = new List<Win>();

        // Как отряд касается врага: доля бойцов в колоннах, что касаются (Г27), и куда приходится удар — во фронт,
        // фланг или тыл врага (доли от касающихся)
        public sealed class Side { public double Engaged, Front, Flank, Rear; }
        internal sealed class Win
        {
            public Mover Att, Def; public bool Counter, Charge;
            public double Extra = 1, Factor = 1, T0, T1, U, N0, Acc;
            public bool Open, Closed, Skipped;
        }
        public Side Of(Mover m) => m == A ? SA : SB;
        public Mover Other(Mover m) => m == A ? B : A;
    }

    public sealed partial class Battle
    {
        public Geo Geo; public Rules R; public EngineContext Ctx;
        public List<Mover> Movers = new List<Mover>();
        public List<Fight> Fights = new List<Fight>();
        public double Clock;                 // часы боя, с — с начала первого хода
        public double MenPerFigure = 10;
        public int BodyK;                    // Г87: людей в бойце-теле; 0 — выбрать по численности на поле перед первым ходом
        bool bodyKFixed;
        public double BurstSec = 2, ContactEverySec = 0.5, FightEndSec = 2, ReplanSec = 1, ReplanMoveM = 5;
        public List<string> Details = new List<string>();   // окна ударов этого хода: «8,2 с · A → B (натиск): −40»
        public bool MoraleChecks = true;     // БД4: проверки БД и на побег в ходу; сверка обмена ударами со столом — без них
        public bool PanicMoraleLoss;         // переключатель трекера «−100 БД вместе с проверкой» (по умолчанию выключен)
        readonly List<string> events = new List<string>();   // проверки, бегство, паника, «сплотить» — в журнал хода
        readonly Dictionary<Mover, int> chargesLeft = new Dictionary<Mover, int>();
        readonly Dictionary<Mover, (double x, double y)> aimed = new Dictionary<Mover, (double x, double y)>();

        public Battle(Geo geo, Rules r, EngineContext ctx) { Geo = geo; R = r; Ctx = ctx; Ctx.Rules = r; }

        // ── Г104: стены и ворота по стороне ──
        // Чьи стены: пехота этой фракции ходит по стенам и башням и сквозь ворота; остальным стены непроходимы, ворота — только открытые.
        // null — ничьи (как раньше: постройки непроходимы для всех). Задать до расстановки; Garrison ставит сам, если не задано
        public int? FortOwner { get => fortOwner; set { fortOwner = value; RefreshPass(); } }
        int? fortOwner; bool started;
        readonly BattleMap.PassRules ownerPass = new BattleMap.PassRules { Walls = true }, enemyPass = new BattleMap.PassRules();
        readonly HashSet<int> openGates = new HashSet<int>();   // клетки открытых ворот; остальные ворота закрыты
        public bool IsOwner(Unit u) => fortOwner.HasValue && u.FactionId == fortOwner;
        BattleMap.PassRules PassOf(Unit u) => IsOwner(u) ? ownerPass : enemyPass;
        void RefreshPass()
        {
            var map = Geo?.Map;
            if (map != null)
            {
                byte gate = Terrain.Id("gate"); bool any = false;
                var blocked = new bool[map.T.Length];
                for (int i = 0; i < map.T.Length; i++) if (map.T[i] == gate) { any = true; blocked[i] = !openGates.Contains(i); }
                enemyPass.Gates = any; enemyPass.Blocked = any ? blocked : null;
            }
            foreach (var m in Movers)
            {
                var p = PassOf(m.P.U);
                if (m.Pass == p && p != enemyPass) continue;
                m.Pass = p;
                if (!MenMode || map == null) continue;
                // карта проходимости заново: до первого хода — к себе; на ходу — приказ заново (путь с учётом ворот)
                if (!started || m.Order == null) m.Field = FlowField.Build(Geo, R, BattleMap.IsHorse(m.P.U), m.P.X, m.P.Y, 0, null, p);
                else if (!m.Done && !m.Fleeing) Order(m, m.Order);
            }
        }
        // ворота у точки (x, y): открыть или закрыть всю связную группу клеток ворот; враг идёт только в открытые. false — ворот там нет
        public bool SetGate(double x, double y, bool open)
        {
            var cells = GateAt(x, y); if (cells == null) return false;
            bool changed = false;
            foreach (int c in cells) changed |= open ? openGates.Add(c) : openGates.Remove(c);
            if (changed) { RefreshPass(); events.Add($"ворота ({x:0}, {y:0}): {(open ? "открыты" : "закрыты")}"); }
            return true;
        }
        public bool GateOpen(double x, double y) { var cells = GateAt(x, y); return cells != null && openGates.Contains(cells[0]); }
        List<int> GateAt(double x, double y)
        {
            var map = Geo?.Map; if (map == null) return null;
            byte gate = Terrain.Id("gate"); int W = map.W, H = map.H;
            int best = -1; double bd = Terrain.CellM * 2;
            for (int i = 0; i < map.T.Length; i++)
            {
                if (map.T[i] != gate) continue;
                double d = JsMath.Hypot((i % W + 0.5) * Terrain.CellM - x, (i / W + 0.5) * Terrain.CellM - y);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return null;
            var seen = new List<int> { best }; var q = new Queue<int>(); q.Enqueue(best);
            while (q.Count > 0)
            {
                int c = q.Dequeue(); int cx = c % W, cy = c / W;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = cx + dx, ny = cy + dy; if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int n = ny * W + nx; if (map.T[n] != gate || seen.Contains(n)) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            return seen;
        }
        // на чём стоят в точке: верх стены или башни над землёй клетки (0 — земля)
        public double StandZ(double x, double y)
        {
            var map = Geo?.Map; if (map == null) return 0;
            var c = Terrain.CellAt(map, x / Geo.W, y / Geo.H);
            return c.T == Terrain.Id("wall") && R.Ranged.BuildingHeightM.TryGetValue("wall", out var w) ? w
                 : c.T == Terrain.Id("tower") && R.Ranged.BuildingHeightM.TryGetValue("tower", out var t) ? t : 0;
        }

        // Б5: весь строй отряда на проходимом (дома, стены, вода — нельзя), точки через 2,5 м по рамке строя; pass — чем отряду можно
        // пройти сверх местности (Г104: стены хозяина, открытые ворота), null — по стороне отряда
        public bool Fits(Unit u, double x, double y, double facing, BattleMap.PassRules pass = null)
        {
            if (Geo?.Map == null) return true;
            pass = pass ?? PassOf(u);
            var fp = Formation.Of(u, R); double h = facing * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            bool horse = BattleMap.IsHorse(u);
            for (double a = -fp.Front / 2; a <= fp.Front / 2 + 1e-9; a += 2.5)
                for (double b = -fp.Depth / 2; b <= fp.Depth / 2 + 1e-9; b += 2.5)
                {
                    double px = x + a * rx + b * fx, py = y + a * ry + b * fy;
                    if (px < 0 || py < 0 || px >= Geo.W || py >= Geo.H) return false;
                    int c = (int)(py / Terrain.CellM) * Geo.Map.W + (int)(px / Terrain.CellM);
                    if (BattleMap.MoveMult(Geo.Map, c, horse, R, pass) == null) return false;
                }
            return true;
        }
        // ближайшее место не дальше maxM, где строй помещается (кольцами по 2,5 м); null — нет такого
        public (double x, double y)? NearestFit(Unit u, double x, double y, double facing, double maxM = 60, BattleMap.PassRules pass = null)
        {
            if (Fits(u, x, y, facing, pass)) return (x, y);
            for (double rr = 2.5; rr <= maxM; rr += 2.5)
            {
                int n = Math.Max(8, (int)(2 * Math.PI * rr / 2.5));
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n, px = x + rr * Math.Cos(a), py = y + rr * Math.Sin(a);
                    if (Fits(u, px, py, facing, pass)) return (px, py);
                }
            }
            return null;
        }

        public Mover Add(Unit u, double x, double y, double facing)
        {
            var pass = PassOf(u);
            // Б5: отряд нельзя поставить на дом, стену или воду (Алекс 08.10.2026) — рамка сдвигается в ближайшее место, где строй помещается
            if (MenMode && !Fits(u, x, y, facing, pass)) { var p = NearestFit(u, x, y, facing, 60, pass); if (p != null) { events.Add($"«{u.Name}»: поставлен на непроходимое — сдвинут на {JsMath.Hypot(p.Value.x - x, p.Value.y - y):0} м"); (x, y) = p.Value; } }
            var m = Mover.Place(u, x, y, facing, R, MenPerFigure);
            m.Pass = pass;
            Movers.Add(m);
            if (bodyKFixed) ApplyBodyK(m);
            // Б5: карта проходимости есть у отряда с первого шага (цель — он сам, дорогой поиск не идёт): отряд, поставленный на дом,
            // стену или воду, сходит с них — якоря колонн к ближайшей проходимой клетке, тела в непроходимое не ступают
            if (MenMode && m.Field == null && Geo?.Map != null) m.Field = FlowField.Build(Geo, R, BattleMap.IsHorse(u), x, y, 0, null, pass);
            foreach (var man in m.Men) man.Z = StandZ(man.X, man.Y);
            return m;
        }
        // Г104: переставить отряд до первого хода — строй, колонны и бойцы заново на новом месте; на непроходимом — сдвиг, как в Add.
        // false — ход уже был, или места нет
        public bool Relocate(Mover m, double x, double y, double facing)
        {
            if (started || TurnRunning || m.Gone || !Movers.Contains(m)) return false;
            var u = m.P.U; var pass = PassOf(u);
            if (MenMode && !Fits(u, x, y, facing, pass))
            {
                var p = NearestFit(u, x, y, facing, 60, pass); if (p == null) return false;
                events.Add($"«{u.Name}»: переставлен на непроходимое — сдвинут на {JsMath.Hypot(p.Value.x - x, p.Value.y - y):0} м"); (x, y) = p.Value;
            }
            m.Replace(x, y, facing, R, MenPerFigure);
            m.Pass = pass;
            if (bodyKFixed) ApplyBodyK(m);
            if (MenMode && Geo?.Map != null) m.Field = FlowField.Build(Geo, R, BattleMap.IsHorse(u), x, y, 0, null, pass);
            foreach (var man in m.Men) man.Z = StandZ(man.X, man.Y);
            return true;
        }
        // Г104: поставить отряд на стену — вдоль прямого ряда клеток стены и башен через ближайшую к (x, y), фронтом наружу (от точки,
        // куда ставят: она во дворе), колонн — сколько входит в длину ряда, глубина — остальное: передние шеренги на боевом ходе,
        // что не влезло — во дворе за стеной. До первого хода — переставляет, на ходу — даёт приказ идти. Конница на стену не идёт.
        // Хозяин стен, если не задан, — фракция этого отряда. false — стены рядом нет, отряд не хозяин, места нет
        public bool Garrison(Mover m, double x, double y)
        {
            var map = Geo?.Map; var u = m.P.U;
            if (map == null || m.Gone || m.Fleeing || BattleMap.IsHorse(u) || u.Soldiers <= 0) return false;
            byte wall = Terrain.Id("wall"), tower = Terrain.Id("tower"); int W = map.W, H = map.H;
            bool Walk(int i) => map.T[i] == wall || map.T[i] == tower;
            // ближайшие клетки стены и башен — среди тех, что не дальше ближайшей + RowPickM, берётся та, через которую прямой ряд длиннее
            // (ближайшая может быть краем башни 3 × 3: ряд через неё — три клетки, и строй встал бы колонной во двор; а дальше
            // RowPickM не смотрим, чтобы не увести отряд на другую, более длинную стену; чат облика, 10.10.2026)
            var near = new List<(double d, int i)>();
            for (int i = 0; i < map.T.Length; i++)
            {
                if (!Walk(i)) continue;
                double d = JsMath.Hypot((i % W + 0.5) * Terrain.CellM - x, (i / W + 0.5) * Terrain.CellM - y);
                if (d < R.Garrison.SeekM) near.Add((d, i));
            }
            if (near.Count == 0) return false;
            if (!fortOwner.HasValue && u.FactionId.HasValue && u.FactionId != 0) FortOwner = u.FactionId;
            if (!IsOwner(u)) return false;
            near.Sort((p, q) => p.d.CompareTo(q.d));
            int bx = 0, by = 0;
            int Run(int dx, int dy, out int lo)
            {
                int hi = 0; lo = 0;
                for (int k = 1; bx + dx * k >= 0 && by + dy * k >= 0 && bx + dx * k < W && by + dy * k < H && Walk((by + dy * k) * W + bx + dx * k); k++) hi = k;
                for (int k = 1; bx - dx * k >= 0 && by - dy * k >= 0 && bx - dx * k < W && by - dy * k < H && Walk((by - dy * k) * W + bx - dx * k); k++) lo = k;
                return lo + hi + 1;
            }
            int best = -1, bestLen = 0;
            for (int c = 0; c < near.Count && near[c].d <= near[0].d + R.Garrison.RowPickM; c++)
            {
                bx = near[c].i % W; by = near[c].i / W;
                int len = Math.Max(Run(1, 0, out _), Run(0, 1, out _));
                if (len > bestLen) { bestLen = len; best = near[c].i; }
            }
            // прямой ряд через выбранную клетку: вдоль x или вдоль y — что длиннее
            bx = best % W; by = best / W;
            int nx = Run(1, 0, out int xlo), ny = Run(0, 1, out int ylo);
            double dx, dy, lenM, cxM, cyM;
            if (nx >= ny) { dx = 1; dy = 0; lenM = nx * Terrain.CellM; cxM = (bx - xlo + nx / 2.0) * Terrain.CellM; cyM = (by + 0.5) * Terrain.CellM; }
            else { dx = 0; dy = 1; lenM = ny * Terrain.CellM; cxM = (bx + 0.5) * Terrain.CellM; cyM = (by - ylo + ny / 2.0) * Terrain.CellM; }
            // наружу — от точки, куда ставят; точка на самой стене — от середины всех стен карты
            double px = -dy, py = dx, side = (x - cxM) * px + (y - cyM) * py;
            if (Math.Abs(side) < Terrain.CellM / 2)
            {
                double gx = 0, gy = 0; int gn = 0;
                for (int i = 0; i < map.T.Length; i++) if (Walk(i)) { gx += i % W + 0.5; gy += i / W + 0.5; gn++; }
                side = (gx / gn * Terrain.CellM - cxM) * px + (gy / gn * Terrain.CellM - cyM) * py;
            }
            if (side > 0) { px = -px; py = -py; }
            // строй: колонн — сколько входит в ряд с отступом от краёв, глубина — остальное (Г101)
            var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
            int n = (int)Math.Max(1, Js.Round(u.Soldiers)), cols = Math.Max(1, (int)((lenM - 2 * R.Garrison.EdgeM) / f.PerMan));
            u.Ranks = Math.Min(R.Move.RanksMax, Math.Max(1, (int)Math.Ceiling((double)n / cols)));
            var fp = Formation.Of(u, R);
            double ox = cxM + px * Terrain.CellM / 2, oy = cyM + py * Terrain.CellM / 2;   // наружный край ряда
            double ux = ox - px * (R.Garrison.EdgeM + fp.Depth / 2), uy = oy - py * (R.Garrison.EdgeM + fp.Depth / 2);
            double facing = Math.Atan2(px, -py) * 180 / Math.PI;
            if (!started) { if (!Relocate(m, ux, uy, facing)) return false; }
            else { m.LaidMen = -1; Relayout(m); Order(m, new MoveOrder { X = ux, Y = uy, Facing = facing }); }
            m.Garrisoned = true;
            int onWall = m.Men.Count(mm => StandZ(mm.X, mm.Y) > 0);
            events.Add(started ? $"«{u.Name}»: на стену — идёт, строй {fp.Front:0} × {fp.Depth:0} м" : $"«{u.Name}»: гарнизон — {onWall} на стене, {m.Men.Count - onWall} во дворе, строй {fp.Front:0} × {fp.Depth:0} м");
            return true;
        }
        // Г87: одно тело = k человек, k = ⌈всего людей на поле / Men.BodyThresholdMen⌉ — одно на битву, решается перед первым
        // ходом по всем добавленным отрядам (или задано BodyK); отряд, добавленный позже, получает то же k
        void FixBodyK()
        {
            bodyKFixed = true;
            if (!MenMode) return;
            if (BodyK <= 0) BodyK = Math.Max(1, (int)Math.Ceiling(Movers.Sum(m => Math.Max(0, m.P.U.Soldiers)) / Math.Max(1, R.Men.BodyThresholdMen)));
            foreach (var m in Movers) ApplyBodyK(m);
        }
        void ApplyBodyK(Mover m)
        {
            if (m.BodyK == BodyK || BodyK <= 0) return;
            m.BodyK = BodyK;
            Soldiers.Assign(m, R, spawn: true);
        }
        public Mover ById(int id) => Movers.FirstOrDefault(m => m.P.U.Id == id);
        static bool Enemies(Unit a, Unit b) => !(a.FactionId.HasValue && a.FactionId.Value != 0 && a.FactionId == b.FactionId);
        static bool Alive(Mover m) => m.P.U.Status == "active" && m.P.U.Soldiers > 0 && m.P.Figs.Count > 0;
        // на поле: в строю или бежит — бегущего можно догнать и рубить, он не отвечает (Г70, как преследование за столом)
        static bool OnField(Mover m) => (m.P.U.Status == "active" || m.P.U.Status == "fled") && m.P.U.Soldiers > 0 && m.P.Figs.Count > 0 && !m.Gone;
        // начинает обмен (Г44) тот, у кого приказ «атаковать» или кто идёт; «держать позицию» и стоящий — отвечают
        static bool Attacking(Mover m) => m.Order != null && (m.Order.Kind == OrderKind.Attack || m.Order.Kind == OrderKind.Move && !m.Done);
        Fight FightOf(Mover x, Mover y) => Fights.FirstOrDefault(f => !f.Over && (f.A == x && f.B == y || f.A == y && f.B == x));

        // ── приказы (Г15) ──
        public void Order(Mover m, MoveOrder o)
        {
            if (m.Fleeing) { if (o.Kind == OrderKind.Rally) m.RallyPending = true; return; }   // бегущий слышит только «сплотить» (Г72)
            if (o.Kind == OrderKind.Rally) return;
            aimed.Remove(m);
            if (o.Kind == OrderKind.Move || o.Kind == OrderKind.Retreat || o.Kind == OrderKind.Attack && !Shooter(m)) m.Garrisoned = false;   // Г104: уходит со стены
            if (o.Kind == OrderKind.Retreat)
            {
                // Г81: пятится лицом туда же, куда смотрел; точки нет — прямо назад на полнормы
                if (double.IsNaN(o.X) || double.IsNaN(o.Y)) (o.X, o.Y) = RetreatPoint(m);
                o.Facing = m.P.Facing;
                MoveSim.Give(m, o, Geo, R);
                return;
            }
            if (o.Kind == OrderKind.Hold) { m.Order = o; m.Track = null; m.Done = true; m.Vs = 0; return; }
            if (o.Kind == OrderKind.Attack)
            {
                var t = ById(o.TargetId);
                if (t != null) { if (Shooter(m)) AimShoot(m, o, t); else Aim(m, o, t); }
                return;
            }
            MoveSim.Give(m, o, Geo, R);
        }
        // идти на цель: в её центр, лицом к ней; у её строя остановят тела (Г58)
        void Aim(Mover m, MoveOrder o, Mover t)
        {
            o.X = t.P.X; o.Y = t.P.Y; o.Facing = MoveSim.HeadingOf(t.P.X - m.P.X, t.P.Y - m.P.Y);
            MoveSim.Give(m, o, Geo, R);
            aimed[m] = (t.P.X, t.P.Y);
        }
        // цель ушла дальше ReplanMoveM — путь заново (раз в ReplanSec); в схватке с ней — не дёргаемся
        void Replan(Mover m)
        {
            if (m.Order == null || m.Order.Kind != OrderKind.Attack || !Alive(m)) return;
            var t = ById(m.Order.TargetId);
            if (t == null || !OnField(t)) { m.Done = true; m.Vs = 0; return; }   // бегущего — преследует
            if (Shooter(m)) { ReplanShoot(m, t); return; }
            var f = FightOf(m, t);
            if (f != null && f.Of(m).Engaged > 0 && !t.Fleeing) return;   // бегущего — догоняет (Г70)
            if (aimed.TryGetValue(m, out var a) && JsMath.Hypot(a.x - t.P.X, a.y - t.P.Y) < ReplanMoveM) return;
            Aim(m, m.Order, t);
        }

        // ── ход ──
        // Целиком (тесты, полигон) или по шагам (игра, Г43: ход считается во время показа): BeginTurn — Step, пока
        // не вернёт false, — EndTurn. StepTime — секунды хода, отсчитанные к этому шагу
        public List<string> Turn(Action<double> frame = null)
        {
            BeginTurn();
            while (Step(frame)) { }
            return EndTurn();
        }
        int stepK = -1, stepsInTurn;
        double turnStart;
        List<Volley> shotThisTurn = new List<Volley>();
        public bool TurnRunning => stepK >= 0 && stepK < stepsInTurn;
        public double StepTime => Math.Max(0, stepK) * R.Move.Dt;
        public void BeginTurn()
        {
            started = true;
            if (!bodyKFixed) FixBodyK();
            stepsInTurn = MoveSim.StepsPerTurn(R);
            turnStart = Clock;
            MoveSim.BeginTurn(Movers);
            Details.Clear(); events.Clear();
            foreach (var f in Fights) { f.LossA = f.LossB = 0; f.Notes.Clear(); }
            foreach (var v in Volleys) { v.LossA = v.LossB = v.Friendly = 0; v.Arrows = 0; }
            Shots = new ShotStats();
            shotThisTurn = new List<Volley>();
            foreach (var m in Movers) chargesLeft[m] = (int)Units.AttackLimit(m.P.U, R);   // Г29: 1 натиск за ход, 2 при дисциплине 80+
            stepK = 0;
        }
        // Один шаг Dt; false — ход кончился (шагов больше нет)
        public bool Step(Action<double> frame = null)
        {
            if (stepK < 0 || stepK >= stepsInTurn) return false;
            var M = R.Move; double dt = M.Dt; int k = stepK;
            int contactEvery = Math.Max(1, (int)Math.Round(ContactEverySec / dt)), replanEvery = Math.Max(1, (int)Math.Round(ReplanSec / dt));
            double t = turnStart + k * dt;
            if (k % replanEvery == 0) foreach (var m in Movers) Replan(m);
            var before = Movers.Select(m => (m.P.X, m.P.Y, m.WheelSec, m.Held && m.LastBlockerEnemy)).ToList();
            foreach (var m in Movers) { m.Now = t; m.ChargeReady = MenMode && ChargeReadyOf(m); }   // Г90: натиск телами
            long pb = Prof.Now();
            MoveSim.Step(Movers, Geo, R, k);
            Prof.Add(0, ref pb);
            for (int i = 0; i < Movers.Count; i++) RunUp(Movers[i], before[i]);
            foreach (var m in Movers) if (m.Fleeing) EdgeCheck(m, t + dt);
            if (k % contactEvery == 0) { Contacts(t); Prof.Add(1, ref pb); Envelop(); TryRally(t); foreach (var m in Movers) if (m.Fleeing) OwnFleeCourses(m); }
            Prof.Add(2, ref pb);
            Strike(t, dt);
            if (MenMode) MenSwings(t, dt);   // Б2: павшие — от ударов бойцов
            Prof.Add(3, ref pb);
            Shoot(t, dt);
            Prof.Add(4, ref pb);
            foreach (var v in Volleys) if (!shotThisTurn.Contains(v)) shotThisTurn.Add(v);
            if ((k + 1) % contactEvery == 0) foreach (var m in Movers) Relayout(m);
            Prof.Add(5, ref pb);
            stepK = k + 1;
            frame?.Invoke(stepK * dt);
            return stepK < stepsInTurn;
        }
        public List<string> EndTurn()
        {
            var M = R.Move;
            while (Step()) { }   // недосчитанные шаги — досчитать (ход всегда целиком)
            stepK = -1;
            EndOfTurnTable(turnStart + M.TurnSec);
            Clock = turnStart + M.TurnSec;
            var L = MoveSim.EndTurn(Movers, R);
            foreach (var f in Fights)
            {
                if (f.LossA == 0 && f.LossB == 0 && f.Notes.Count == 0) continue;
                string since = f.T0 >= turnStart ? $" (с {Js.Num(Js.R1(f.T0 - turnStart))} с)" : "";
                var parts = new List<string> { $"«{f.A.P.U.Name}» −{Js.Num(Js.Round(f.LossA))}", $"«{f.B.P.U.Name}» −{Js.Num(Js.Round(f.LossB))}" };
                parts.AddRange(f.Notes);
                L.Add($"Схватка «{f.A.P.U.Name}» и «{f.B.P.U.Name}»{since}: {string.Join("; ", parts)}");
            }
            foreach (var v in shotThisTurn)
            {
                if (v.Arrows == 0 && v.LossA == 0 && v.LossB == 0) continue;
                var parts = new List<string> { $"стрел {v.Arrows}", $"«{v.B.P.U.Name}» −{Js.Num(v.LossB)}" };
                if (v.LossA > 0) parts.Add($"ответом «{v.A.P.U.Name}» −{Js.Num(v.LossA)}");
                if (v.Friendly > 0) parts.Add($"по своим −{Js.Num(v.Friendly)}");
                L.Add($"Стрельба «{v.A.P.U.Name}» по «{v.B.P.U.Name}»: {string.Join("; ", parts)}");
            }
            L.AddRange(events);
            foreach (var m in Movers)
                if (m.Fleeing && OnField(m)) L.Add($"«{m.P.U.Name}» бежит: прошёл {Js.Num(Js.R1(m.Moved))} м{(m.RallyPending ? ", ждёт, чтобы сплотиться (враг ближе " + Js.Num(R.Rally.FreeM) + " м)" : "")}");
            Fights.RemoveAll(f => f.Over);
            return L;
        }

        // Разбег для натиска (К29): метры по прямой по чистому (без леса, болота, брода, непроходимого).
        // Сбрасывается на повороте колесом и когда встал сам; упёрся во врага — разбег замер (им и бьёт).
        void RunUp(Mover m, (double x, double y, double wheel, bool enemyHeld) before)
        {
            var u = m.P.U;
            double d = JsMath.Hypot(m.P.X - before.x, m.P.Y - before.y);
            if (before.enemyHeld || m.Held && m.LastBlockerEnemy) return;
            if (d <= 1e-9 || m.WheelSec > before.wheel || !ChargeGround(m.P.X, m.P.Y)) { u.RunUpM = 0; return; }
            u.RunUpM += d;
        }
        Rules.TerrainR GroundOf(double x, double y)
        {
            var map = Geo?.Map;
            if (map == null) return null;
            var c = Terrain.CellAt(map, x / Geo.W, y / Geo.H);
            return c.T != 0 && Terrain.ById.TryGetValue(c.T, out var t) && R.Map.Terrain.TryGetValue(t.Key, out var tr) ? tr : null;
        }
        bool ChargeGround(double x, double y) { var tr = GroundOf(x, y); return tr == null || !tr.NoCharge && tr.Move != null; }
        int LevelOf(Mover m) => Geo?.Map == null ? 0 : Terrain.CellAt(Geo.Map, m.P.X / Geo.W, m.P.Y / Geo.H).Z;
        // режим боя — по местности под тем, по кому бьют (К24)
        string ModeAt(Mover def) => GroundOf(def.P.X, def.P.Y)?.Mode == "rough" ? Modes.MeleeRough : Modes.MeleeForm;

        // ── касание (Г27): колонна бьёт, если хоть одна её фигурка не дальше MeleeGap от фигурки врага ──
        readonly Dictionary<Mover, (List<int> figs, Mover foe)> frontFigs = new Dictionary<Mover, (List<int> figs, Mover foe)>();
        // Касания этого полушага: x → y, по фигуркам x; и чья колонна кому досталась (Г69)
        readonly Dictionary<(Mover x, Mover y), (int ky, double d)[]> touches = new Dictionary<(Mover x, Mover y), (int ky, double d)[]>();
        readonly Dictionary<Mover, Dictionary<int, (Mover foe, double d)>> colFoe = new Dictionary<Mover, Dictionary<int, (Mover foe, double d)>>();
        Fight.Side SideOf(Mover x, Mover y)
        {
            if (!touches.TryGetValue((x, y), out var touch)) return new Fight.Side();
            var mine = colFoe.TryGetValue(x, out var cf) ? cf : new Dictionary<int, (Mover foe, double d)>();
            var touching = new List<int>();
            for (int k = 0; k < touch.Length; k++) if (touch[k].ky >= 0) touching.Add(k);
            if (touching.Count > 0) frontFigs[x] = (touching, y);
            double total = 0;
            var colMen = new Dictionary<int, double>();
            var colSector = new Dictionary<int, (double d, string s)>();
            // Б2: доли сектора колонны — по её касающимся бойцам (сколько стоят перед строем врага, сбоку, сзади)
            var colCount = new Dictionary<int, (double front, double flank, double rear)>();
            menSec.TryGetValue((x, y), out var msec);
            for (int k = 0; k < x.P.Figs.Count; k++)
            {
                var f = x.P.Figs[k];
                total += f.Men;
                colMen[f.File] = (colMen.TryGetValue(f.File, out var c) ? c : 0) + f.Men;
                if (touch[k].ky < 0) continue;
                if (!mine.TryGetValue(f.File, out var own) || own.foe != y) continue;   // колонна бьёт ближайшего врага (Г69)
                if (msec != null && k < msec.Length)
                {
                    var was = colCount.TryGetValue(f.File, out var cc) ? cc : (0, 0, 0);
                    colCount[f.File] = (was.front + msec[k].front, was.flank + msec[k].flank, was.rear + msec[k].rear);
                    continue;
                }
                // сектор — по тому, где фигурка стоит относительно строя врага: прямо перед ним — фронт, прямо за — тыл
                y.P.ToLocal(x.Figs[k].X, x.Figs[k].Y, out var lx, out var ly);
                double ex = Math.Abs(lx) - y.P.Fp.Front / 2;
                string sec = ex <= 0 ? (ly < 0 ? "front" : "rear") : "flank";
                if (!colSector.TryGetValue(f.File, out var cs) || touch[k].d < cs.d) colSector[f.File] = (touch[k].d, sec);
            }
            double eng = 0, front = 0, flank = 0, rear = 0;
            foreach (var kv in colCount)
            {
                double men = colMen[kv.Key], all = kv.Value.front + kv.Value.flank + kv.Value.rear;
                if (all <= 0) continue;
                eng += men;
                front += men * kv.Value.front / all; flank += men * kv.Value.flank / all; rear += men * kv.Value.rear / all;
            }
            foreach (var kv in colSector)
            {
                double men = colMen[kv.Key];
                eng += men;
                if (kv.Value.s == "front") front += men; else if (kv.Value.s == "flank") flank += men; else rear += men;
            }
            return eng <= 0 || total <= 0 ? new Fight.Side()
                : new Fight.Side { Engaged = eng / total, Front = front / eng, Flank = flank / eng, Rear = rear / eng };
        }

        void Contacts(double t)
        {
            frontFigs.Clear(); touches.Clear(); colFoe.Clear();
            foreach (var m in Movers) foreach (var s in m.Figs) s.Fighting = false;
            // 1) касания фигурок у всех пар врагов поблизости; при бойцах-телах — касания бойцов (Б2)
            if (MenMode) MenTouches();
            else for (int i = 0; i < Movers.Count; i++)
                for (int j = i + 1; j < Movers.Count; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    if (!(OnField(x) && OnField(y) && (Alive(x) || Alive(y)) && Enemies(x.P.U, y.P.U))) continue;
                    double reach = (JsMath.Hypot(x.P.Fp.Front, x.P.Fp.Depth) + JsMath.Hypot(y.P.Fp.Front, y.P.Fp.Depth)) / 2 + R.Map.MeleeGap + 10 + WrapReach(x) + WrapReach(y);
                    if (JsMath.Hypot(x.P.X - y.P.X, x.P.Y - y.P.Y) > reach) continue;
                    touches[(x, y)] = Bodies.Touch(x, y, R.Map.MeleeGap);
                    touches[(y, x)] = Bodies.Touch(y, x, R.Map.MeleeGap);
                }
            // 2) колонна — тому врагу, которого касается ближе всех (Г69): сила отряда делится, а не растёт
            foreach (var kv in touches)
            {
                var (x, y) = kv.Key;
                if (Alive(x))
                    for (int k = 0; k < kv.Value.Length && k < x.Figs.Count; k++)
                    {
                        int ky = kv.Value[k].ky;
                        if (ky < 0 || ky >= y.Figs.Count) continue;
                        var s = x.Figs[k]; var q = y.Figs[ky];
                        double dx = q.X - s.X, dy = q.Y - s.Y, d = JsMath.Hypot(dx, dy);
                        if (d < 1e-9) continue;
                        s.Fighting = true; s.FightX = dx / d; s.FightY = dy / d; s.FoeX = q.X; s.FoeY = q.Y; s.FoeId = y.P.U.Id; s.FightT = t;   // для выпадов передних бойцов (Г78)
                    }
                if (!colFoe.TryGetValue(x, out var cf)) colFoe[x] = cf = new Dictionary<int, (Mover foe, double d)>();
                for (int k = 0; k < kv.Value.Length; k++)
                {
                    if (kv.Value[k].ky < 0) continue;
                    int file = x.P.Figs[k].File;
                    if (!cf.TryGetValue(file, out var o) || kv.Value[k].d < o.d) cf[file] = (y, kv.Value[k].d);
                }
            }
            // 3) схватки пар
            for (int i = 0; i < Movers.Count; i++)
                for (int j = i + 1; j < Movers.Count; j++)
                {
                    Mover x = Movers[i], y = Movers[j];
                    var f = FightOf(x, y);
                    bool can = OnField(x) && OnField(y) && (Alive(x) || Alive(y)) && Enemies(x.P.U, y.P.U);
                    var sx = can ? SideOf(x, y) : new Fight.Side();
                    var sy = can ? SideOf(y, x) : new Fight.Side();
                    bool touch = sx.Engaged > 0 || sy.Engaged > 0;
                    if (touch)
                    {
                        if (f == null) Start(x, y, sx, sy, t);
                        else { if (f.A == x) { f.SA = sx; f.SB = sy; } else { f.SA = sy; f.SB = sx; } f.LastTouch = t; f.Touching = true; }
                    }
                    else if (f != null)
                    {
                        // касание пропало: удары не текут, но как касались — помним (ответ на удар не теряется
                        // из-за того, что фигурки на миг разошлись); разошлись надолго — схватка кончилась
                        f.Touching = false;
                        if (!can || t - f.LastTouch > FightEndSec) End(f);
                    }
                }
        }

        // Схватка началась (Г44): начинает атакующий; атакуют оба или никто — выше дисциплина, поровну — бросок
        void Start(Mover x, Mover y, Fight.Side sx, Fight.Side sy, double t)
        {
            bool ax = Attacking(x), ay = Attacking(y);
            Mover A = ax != ay ? (ax ? x : y)
                    : x.P.U.Discipline != y.P.U.Discipline ? (x.P.U.Discipline > y.P.U.Discipline ? x : y)
                    : (Ctx.Rng() < 0.5 ? x : y);
            Mover B = A == x ? y : x;
            var f = new Fight { A = A, B = B, T0 = t, LastTouch = t, Touching = true, SA = A == x ? sx : sy, SB = A == x ? sy : sx };
            // натиск (Г29, К29)
            bool burst = false;
            if (A.Order != null && A.Order.Charge && Units.IsCav(A.P.U))
            {
                string why = null;
                var gB = GroundOf(B.P.X, B.P.Y);
                if (chargesLeft.TryGetValue(A, out var left) && left <= 0) why = "натиск в этом ходу уже был";
                else if (BattleMap.RunUpBlock(A.P.U, R) is string run) why = run;
                else if (gB != null && gB.NoCharge) why = "под целью местность без натиска";
                else if (Units.IsPike(B.P.U) && f.SA.Front > 0) why = "пики во фронт гасят натиск";
                if (why == null) { burst = true; chargesLeft[A] = left - 1; f.Notes.Add($"натиск «{A.P.U.Name}»"); }
                else f.Notes.Add($"«{A.P.U.Name}» без натиска: {why}");
            }
            if (burst) f.Wins.Add(new Fight.Win { Att = A, Def = B, Charge = true, T0 = t, T1 = t + BurstSec });
            Schedule(f, burst ? t + BurstSec : t, burst ? 1 : 0, R.Move.TurnSec - (burst ? BurstSec : 0));
            Fights.Add(f);
        }

        // Круг обменов (Г62): атаки A, потом атаки B, за len секунд; ответы — на круг, как за ход стола
        void Schedule(Fight f, double start, int burstUsed, double len)
        {
            var ex = new List<(Mover x, Mover y)>();
            int atkA = (int)Units.AttackLimit(f.A.P.U, R) - burstUsed, atkB = (int)Units.AttackLimit(f.B.P.U, R);
            for (int i = 0; i < atkA; i++) ex.Add((f.A, f.B));
            for (int i = 0; i < atkB; i++) ex.Add((f.B, f.A));
            double w = len / Math.Max(1, ex.Count);
            for (int j = 0; j < ex.Count; j++) f.Wins.Add(new Fight.Win { Att = ex[j].x, Def = ex[j].y, T0 = start + j * w, T1 = start + (j + 1) * w });
            f.CycleEnd = start + len;
            Budget(f.A, start); Budget(f.B, start);
        }
        // Ответные удары — общий запас отряда на все его схватки и перестрелки за круг в 15 с (Г69), как за ход стола
        readonly Dictionary<Mover, (int left, double since)> counters = new Dictionary<Mover, (int left, double since)>();
        internal void Budget(Mover m, double t)
        {
            if (!counters.TryGetValue(m, out var c) || t - c.since >= R.Move.TurnSec - 1e-6) counters[m] = ((int)Units.CounterLimit(m.P.U, R), t);
        }
        internal bool TakeCounter(Mover m)
        {
            if (!counters.TryGetValue(m, out var c) || c.left <= 0) return false;
            counters[m] = (c.left - 1, c.since);
            return true;
        }

        void End(Fight f)
        {
            f.Over = true;
            foreach (var w in f.Wins) if (w.Open && !w.Closed) Close(f, w, Clock);
        }

        // ── удары: урон течёт каждый шаг, в конце окна — потери и БД, как после удара за столом ──
        void Strike(double t, double dt)
        {
            double t1 = t + dt;
            foreach (var f in Fights)
            {
                if (f.Over) continue;
                // круг кончился, а схватка идёт — следующий круг (Г62: бой идёт без перерыва)
                if (t1 > f.CycleEnd + 1e-9 && t - f.LastTouch <= FightEndSec) Schedule(f, f.CycleEnd, 0, R.Move.TurnSec);
                // открываем окна; на удар во фронт — ответ (удар во фланг и тыл глушит ответ, Г17)
                foreach (var w in f.Wins.ToList())
                {
                    if (w.Open || w.Skipped || w.T0 >= t1 - 1e-9) continue;
                    if (w.Att.P.U.Status != "active" || w.Def.P.U.Status == "destroyed" || w.Def.P.U.Soldiers <= 0) { w.Skipped = true; continue; }
                    if (w.Counter && w.Att.P.U.Morale < R.Morale.ShakenBelow) { w.Skipped = true; continue; }   // дрогнувший не отвечает
                    w.Open = true; w.Att.P.U.Acted = true;   // «походил» — для усталости в конце хода (стол)
                    w.U = MeleeSim.Fortune(w.Att.P.U, ModeAt(w.Def), Ctx);
                    w.N0 = w.Att.P.U.Soldiers;
                    if (!w.Counter && !w.Charge)
                    {
                        var sa = f.Of(w.Att);
                        if (w.Def.P.U.Status == "active" && f.Of(w.Def).Engaged > 0 && sa.Front > 0 && TakeCounter(w.Def))
                        {
                            bool pikeStop = Units.IsCav(w.Att.P.U) && Units.IsPike(w.Def.P.U);
                            var c = new Fight.Win
                            {
                                Att = w.Def, Def = w.Att, Counter = true, Factor = sa.Front, T0 = w.T0, T1 = w.T1,
                                Extra = pikeStop ? R.PikeCounterMult : 1, Open = true, N0 = w.Def.P.U.Soldiers,
                            };
                            c.U = MeleeSim.Fortune(c.Att.P.U, ModeAt(c.Def), Ctx);
                            f.Wins.Add(c);
                        }
                    }
                }
                // урон за шаг — от состояния на начало шага, обе стороны одновременно
                f.A.P.Level = LevelOf(f.A); f.B.P.Level = LevelOf(f.B);
                double toA = 0, toB = 0;
                var inc = new double[f.Wins.Count];
                for (int i = 0; i < f.Wins.Count; i++)
                {
                    var w = f.Wins[i];
                    if (!w.Open || w.Closed) continue;
                    double portion = Math.Max(0, Math.Min(w.T1, t1) - Math.Max(w.T0, t)) / (w.T1 - w.T0);
                    if (portion <= 0 || w.Att.P.U.Soldiers <= 0) continue;
                    var sa = f.Of(w.Att);
                    if (!f.Touching || sa.Engaged <= 0) continue;   // разошлись — не бьют
                    string mode = ModeAt(w.Def);
                    var At = Combat.Eff(w.Att.P.U, mode, null, Ctx); var Df = Combat.Eff(w.Def.P.U, mode, null, Ctx);
                    var opts = new BattleRequest { Mode = mode, FatigueMode = "percent" };
                    var H = R.Map.Height;
                    StrikeMod mod = w.Att.P.Level > w.Def.P.Level ? new StrikeMod { Mult = H.DownhillMelee }
                                  : w.Att.P.Level < w.Def.P.Level ? new StrikeMod { Mult = H.UphillMelee } : null;
                    double roll = w.U * w.N0 * sa.Engaged, dmg = 0;
                    if (w.Counter) dmg = Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, false, w.Extra, "", "front", mod, roll * w.Factor);
                    else
                    {
                        if (sa.Front > 0) dmg += sa.Front * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "front", mod, roll);
                        if (sa.Flank > 0) dmg += sa.Flank * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "flank", mod, roll);
                        if (sa.Rear > 0) dmg += sa.Rear * Combat.StrikeDamage(w.Att.P.U, w.Def.P.U, At, Df, opts, null, Ctx, w.Charge, 1, "", "rear", mod, roll);
                    }
                    inc[i] = Math.Max(0, dmg) * portion;
                    if (w.Def == f.A) toA += inc[i]; else toB += inc[i];
                }
                double kA = toA > f.A.P.U.Soldiers && toA > 0 ? f.A.P.U.Soldiers / toA : 1;
                double kB = toB > f.B.P.U.Soldiers && toB > 0 ? f.B.P.U.Soldiers / toB : 1;
                for (int i = 0; i < inc.Length; i++)
                {
                    if (inc[i] <= 0) continue;
                    var w = f.Wins[i];
                    double take = inc[i] * (w.Def == f.A ? kA : kB);
                    w.Acc += take; w.Def.P.U.Soldiers -= take;
                    if (w.Def == f.A) f.LossA += take; else f.LossB += take;
                }
                foreach (var w in f.Wins) if (w.Open && !w.Closed && w.T1 <= t1 + 1e-9) Close(f, w, t1);
                f.Wins.RemoveAll(w => w.Closed || w.Skipped);
            }
        }

        // Конец окна: округление до солдат, летальность и штраф БД — как после удара за столом
        void Close(Fight f, Fight.Win w, double t)
        {
            w.Closed = true;
            var def = w.Def.P.U;
            double before = def.Soldiers + w.Acc;
            double cas = Math.Min(Js.Round(w.Acc), Math.Floor(before + 1e-9));
            var clone = def.Clone(); clone.Soldiers = before;
            var res = Combat.Casualties(clone, cas, null, Ctx);
            var patch = Combat.CasualtyPatch(clone, res, new List<string>(), Ctx, out _);
            patch.ApplyTo(def);
            double whole = Math.Round(def.Soldiers);
            if (Math.Abs(def.Soldiers - whole) < 1e-6) def.Soldiers = whole;
            Details.Add($"{Js.Num(Js.R1(t - (Clock)))} с · {w.Att.P.U.Name} → {def.Name}{(w.Charge ? " (натиск)" : w.Counter ? " (ответ)" : "")}: −{Js.Num(cas)}");
            if (cas > 0) AfterLoss(w.Def, t);
        }

        // ── БД и бегство в ходу (БД4, Г70–Г74) — ЧЕРНОВИК ДО ГМа ──
        string At(double t) => Js.Num(Js.R1(t - Clock));
        // Г74: после каждого удара с потерями — проверки, как подсказывает журнал стола: БД ≤ 40 — проверка БД
        // (провал роняет БД до нуля); БД на нуле — проверка на побег («стоять насмерть» гасит первый бросок)
        internal void AfterLoss(Mover m, double t)
        {
            var u = m.P.U;
            if (u.Soldiers <= 0) { m.Fleeing = false; m.RallyPending = false; return; }
            if (!MoraleChecks || u.Status != "active" || u.Soldiers <= 0 || m.Gone) return;
            if (u.Morale > 0 && u.Morale <= R.Morale.CheckAt) Check(t, MoraleRules.MoraleCheck(u, Ctx), u);
            if (u.Morale > 0 || u.Status != "active") return;
            Check(t, MoraleRules.FleeCheck(u, Ctx), u);
            if (u.Status == "fled") { Flee(m, t, "провалил проверку на побег"); PanicFrom(m, t); }
        }
        void Check(double t, ActionResult r, Unit u)
        {
            r.Patch.ApplyTo(u);
            events.Add($"{At(t)} с · {r.Title}: {string.Join("; ", r.Lines.Where(l => !l.Contains("требуется")))}");
        }

        // Г70, Г71: побежал — толпой прочь от врага (от тех, кто ближе FleeLookM: чем ближе, тем сильнее; никого —
        // назад от своего фронта) к краю карты, на норме; строй рассыпается, приказов не слушает, свои пропускают
        void Flee(Mover m, double t, string why)
        {
            if (m.Fleeing || m.Gone) return;
            var u = m.P.U; u.Status = "fled";
            double vx = 0, vy = 0;
            foreach (var e in Movers)
            {
                if (e == m || !OnField(e) || !Enemies(u, e.P.U)) continue;
                double dx = m.P.X - e.P.X, dy = m.P.Y - e.P.Y, d = JsMath.Hypot(dx, dy);
                if (d > R.Move.FleeLookM || d < 1e-9) continue;
                double w = 1 / Math.Max(d, 10);
                vx += dx / d * w; vy += dy / d * w;
            }
            double head = JsMath.Hypot(vx, vy) > 1e-12 ? MoveSim.HeadingOf(vx, vy) : MoveSim.Norm(m.P.Facing + 180);
            double hx = Math.Sin(head * Math.PI / 180), hy = -Math.Cos(head * Math.PI / 180), inset = 1, run = 1e9;
            if (hx > 1e-9) run = Math.Min(run, (Geo.W - inset - m.P.X) / hx); else if (hx < -1e-9) run = Math.Min(run, (inset - m.P.X) / hx);
            if (hy > 1e-9) run = Math.Min(run, (Geo.H - inset - m.P.Y) / hy); else if (hy < -1e-9) run = Math.Min(run, (inset - m.P.Y) / hy);
            run = Math.Max(0, run);
            m.FleeX = Math.Max(inset, Math.Min(Geo.W - inset, m.P.X + hx * run));
            m.FleeY = Math.Max(inset, Math.Min(Geo.H - inset, m.P.Y + hy * run));
            m.FleeHeading = head; m.Fleeing = true; m.FleeSince = t; m.RallyPending = false; m.Rallied = false;
            m.Order = null; m.Track = null; m.Done = false; m.Held = false; m.HoldLeft = 0; m.Vs = 0; aimed.Remove(m);
            m.Field = FlowField.Build(Geo, R, BattleMap.IsHorse(u), m.FleeX, m.FleeY, 0, null, m.Pass);
            foreach (var s in m.Figs)
            {
                if (!s.Turned) { s.Turned = true; s.Axis = m.P.Facing; }
                s.Wrap = false; s.WFoe = null; s.Returning = false; s.FleeH = double.NaN;
            }
            m.P.Facing = head;
            OwnFleeCourses(m);
            events.Add($"{At(t)} с · 🏃 «{u.Name}» бежит ({why}) — толпой прочь от врага, к краю карты");
        }

        // Г73: каскадная паника — волна целиком в миг бегства, как за столом (Panic.Wave трекера): свои и союзники
        // в 150 м от края до края, первое кольцо — только кто видел; побежавший тянет соседей; каждый — раз за волну
        void PanicFrom(Mover src, double t)
        {
            var live = Movers.Where(m => m == src || OnField(m)).ToList();
            foreach (var m in live) { var u = m.P.U; u.OnMap = true; u.MapX = m.P.X / Geo.W * 100; u.MapY = m.P.Y / Geo.H * 100; u.Facing = m.P.Facing; }
            var r = Panic.Wave(live.Select(m => m.P.U).ToList(), src.P.U.Id, Geo, Ctx, PanicMoraleLoss);
            foreach (var p in r.Patches) { var m = ById(p.Id); if (m != null) p.Patch.ApplyTo(m.P.U); }
            if (r.Lines.Count > 0) events.Add($"{At(t)} с · 🏳 каскадная паника от «{src.P.U.Name}»: {string.Join("; ", r.Lines)}");
            foreach (int id in r.Fled) { var m = ById(id); if (m != null) Flee(m, t, $"паника — бежал «{src.P.U.Name}»"); }
        }

        // Г70: толпа дошла до края карты — отряд ушёл с поля боя (жив, но в этой битве его нет; вернуть — ГМ после боя)
        void EdgeCheck(Mover m, double t)
        {
            if (!m.Fleeing || m.Gone) return;
            double e = R.Move.FleeEdgeM; int left = 0;
            for (int k = m.Figs.Count - 1; k >= 0; k--)
            {
                var s = m.Figs[k];
                // у бойцов-тел колонна там, где её якорь: середина бойцов отстаёт от него на полколонны и края не достаёт
                double px = MenMode ? s.AX : s.X, py = MenMode ? s.AY : s.Y;
                if (!(px < e || py < e || px > Geo.W - e || py > Geo.H - e)) continue;
                m.LeftMen += (int)m.P.Figs[k].Men; m.LaidMen -= (int)m.P.Figs[k].Men; left++;
                m.Men.RemoveAll(x => x.Fig == s); m.MenVersion++;
                m.Figs.RemoveAt(k); m.P.Figs.RemoveAt(k);
            }
            if (m.Figs.Count > 0) return;
            if (left == 0) { m.Fleeing = false; m.RallyPending = false; return; }   // никто не ушёл за край — вымерли на бегу
            m.Gone = true; m.Fleeing = false; m.RallyPending = false; m.Field = null; m.Vs = 0;
            events.Add($"{At(t)} с · «{m.P.U.Name}» бежал с поля боя ({Js.Num(Js.Round(m.P.U.Soldiers))} бойцов)");
        }
        // Потери бегущей толпы: строй заново не собирается — падают фигурки, что ближе всех к врагу (их и рубят)
        void RelayoutCrowd(Mover m)
        {
            int n = (int)Math.Max(0, Js.Round(m.P.U.Soldiers)) - m.LeftMen;
            if (n >= m.LaidMen) return;
            int melee = Math.Max(0, m.LaidMen - n - m.ShotDown - m.StruckDown);
            m.ShotDown = 0; m.StruckDown = 0;
            if (melee > 0) MeleeDeaths(m, melee);
            m.LaidMen = n;
            int keep = n <= 0 ? 0 : (int)Math.Ceiling(n / MenPerFigure);
            // Б3: у бойцов-тел колонна вмещает не MenPerFigure, а сколько её раскладка (пехота — 8): иначе колонн остаётся мало,
            // бойцы убранных набиваются в оставшиеся, и хвост колонны тянется назад к врагу
            if (MenMode && n > 0 && m.P.Figs.Count > 0) keep = (int)Math.Ceiling(n / Math.Max(1, m.P.Figs.Average(f => f.Men)));
            var foes = Movers.Where(e => e != m && OnField(e) && Enemies(m.P.U, e.P.U)).ToList();
            double Danger(FigState s) => foes.Count == 0 ? 0 : -foes.Min(e => JsMath.Hypot(e.P.X - s.X, e.P.Y - s.Y));
            while (m.Figs.Count > keep)
            {
                int k = Enumerable.Range(0, m.Figs.Count).OrderByDescending(i => Danger(m.Figs[i])).ThenBy(i => m.Figs[i].Id).First();
                m.Fallen.Add((m.Figs[k].X, m.Figs[k].Y));
                m.Figs.RemoveAt(k); m.P.Figs.RemoveAt(k);
            }
            Soldiers.Assign(m, R);
        }

        // Окружённые (Г70): фигурка, которой общий путь толпы перекрывает враг (ближе FleeBlockM, в пределах
        // ±FleeBlockDeg от курса), обходит его сбоку — к тому краю вражеского строя, что ближе к ней (курс толпы ± 90°);
        // путь чист — снова в общем потоке. Раз в ContactEverySec
        void OwnFleeCourses(Mover m)
        {
            var M = R.Move;
            double hx = Math.Sin(m.FleeHeading * Math.PI / 180), hy = -Math.Cos(m.FleeHeading * Math.PI / 180);
            foreach (var s in m.Figs)
            {
                Mover by = null; double wd = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                    if (JsMath.Hypot(e.P.X - s.X, e.P.Y - s.Y) > M.FleeBlockM + e.P.Fp.Front) continue;
                    foreach (var q in e.Figs)
                    {
                        double d = JsMath.Hypot(q.X - s.X, q.Y - s.Y);
                        if (d > M.FleeBlockM || d >= wd) continue;
                        if (Math.Abs(MoveSim.AngleDiff(m.FleeHeading, MoveSim.HeadingOf(q.X - s.X, q.Y - s.Y))) > M.FleeBlockDeg) continue;
                        wd = d; by = e;
                    }
                }
                if (by == null) { s.FleeH = double.NaN; continue; }
                if (!double.IsNaN(s.FleeH)) continue;   // уже обходит — в ту же сторону, не мечется
                double side = (s.X - by.P.X) * hy - (s.Y - by.P.Y) * hx;   // справа (+) или слева (−) от середины врага по ходу бегства
                s.FleeH = MoveSim.Norm(m.FleeHeading + (side >= 0 ? -M.FleeDodgeDeg : M.FleeDodgeDeg));
                if (!s.Turned) { s.Turned = true; s.Axis = m.P.Facing; }   // тело разворачивается к своему курсу постепенно
            }
        }

        // Г72: «сплотить» — бегущий с этим приказом, у которого врага нет ближе Rally.FreeM (от края до края), бросает
        // d100 ≤ дисциплина, один раз на приказ: успех — БД не ниже Rally.Morale, снова в строю лицом к ближайшему врагу
        // (как «воспрял духом» за столом); провал — бежит дальше. Враг рядом — приказ ждёт
        void TryRally(double t)
        {
            foreach (var m in Movers)
            {
                if (!m.Fleeing || m.Gone || !m.RallyPending || m.Figs.Count == 0) continue;
                Mover near = null; double gap = double.MaxValue, far = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !Alive(e) || !Enemies(m.P.U, e.P.U)) continue;
                    double g = Bodies.MinGap(m, e);
                    if (g < gap) gap = g;
                    double c = JsMath.Hypot(e.P.X - m.P.X, e.P.Y - m.P.Y);
                    if (c < far) { far = c; near = e; }
                }
                if (gap <= R.Rally.FreeM) continue;
                m.RallyPending = false;
                var u = m.P.U; double roll = Dice.Roll(Ctx.Rng, 100);
                if (roll > u.Discipline) { events.Add($"{At(t)} с · «{u.Name}» сплотить не вышло: d100 = {Js.Num(roll)} > {Js.Num(u.Discipline)} (дисциплина) — бежит дальше"); continue; }
                u.Status = "active"; u.Broken = false; u.BreakGrace = 0; u.Morale = Math.Max(u.Morale, R.Rally.Morale);
                m.Fleeing = false; m.Rallied = true;
                if (m.LeftMen > 0) { events.Add($"{At(t)} с · «{u.Name}»: {m.LeftMen} бойцов ушли за край карты — в этой битве их нет"); u.Soldiers -= m.LeftMen; m.LeftMen = 0; }
                m.LaidMen = -1; Relayout(m);
                Reform(m, near != null ? MoveSim.HeadingOf(near.P.X - m.P.X, near.P.Y - m.P.Y) : MoveSim.Norm(m.FleeHeading + 180));
                events.Add($"{At(t)} с · ✦ «{u.Name}» сплотили: d100 = {Js.Num(roll)} ≤ {Js.Num(u.Discipline)} — БД {Js.Num(u.Morale)}, снова в строю");
            }
        }
        // Сплотились: строй заново там, где основная толпа (отбившиеся своим курсом — подходят), лицом к face;
        // места — ближайшим фигуркам спереди назад
        void Reform(Mover m, double face)
        {
            var P = m.P;
            if (m.Cols != m.NominalCols && m.NominalCols > 0) MoveSim.SetCols(m, m.NominalCols);
            var main = m.Figs.Where(s => double.IsNaN(s.FleeH)).ToList();
            if (main.Count == 0) main = m.Figs;
            P.X = main.Average(s => s.X); P.Y = main.Average(s => s.Y);
            foreach (var s in m.Figs) s.FleeH = double.NaN;
            foreach (var s in m.Figs) if (!s.Turned) { s.Turned = true; s.Axis = P.Facing; }
            P.Facing = MoveSim.Norm(face);
            var free = Enumerable.Range(0, m.Figs.Count).ToList();
            var bodies = new FigState[P.Figs.Count];
            foreach (int k in Enumerable.Range(0, P.Figs.Count).OrderBy(i => P.Figs[i].Y).ThenBy(i => P.Figs[i].X))
            {
                P.ToWorld(P.Figs[k].X, P.Figs[k].Y, out var wx, out var wy);
                int best = -1; double bd = double.MaxValue;
                for (int q = 0; q < free.Count; q++)
                {
                    var s = m.Figs[free[q]];
                    double d = (s.X - wx) * (s.X - wx) + (s.Y - wy) * (s.Y - wy);
                    if (d < bd) { bd = d; best = q; }
                }
                if (best >= 0) { bodies[k] = m.Figs[free[best]]; free.RemoveAt(best); }
                else bodies[k] = new FigState { Id = m.NextFigId++, X = wx, Y = wy, AX = wx, AY = wy };
            }
            m.Figs = bodies.ToList();
            // отбившиеся подходят к строю в обход врага: карта направлений к месту сбора, клетки под вражескими строями закрыты
            var F0 = m.Field;
            m.Field = F0 == null ? null : FlowField.Build(Geo, R, BattleMap.IsHorse(P.U), P.X, P.Y, 0, EnemyCells(m, F0), m.Pass);
            m.Track = null; m.Vs = 0;
            m.Order = new MoveOrder { Kind = OrderKind.Hold, X = P.X, Y = P.Y, Facing = P.Facing }; m.Done = true;
        }
        // Клетки под строями врагов (с запасом в полклетки) — для пути в обход них
        bool[] EnemyCells(Mover m, FlowField F)
        {
            var extra = new bool[F.W * F.H];
            foreach (var e in Movers)
            {
                if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                var B = e.P;
                double hx = B.Fp.Front / 2 + F.CellW / 2, hy = B.Fp.Depth / 2 + F.CellH / 2, reach = JsMath.Hypot(hx, hy);
                int x0 = (int)Math.Floor((B.X - reach) / F.CellW), x1 = (int)Math.Floor((B.X + reach) / F.CellW);
                int y0 = (int)Math.Floor((B.Y - reach) / F.CellH), y1 = (int)Math.Floor((B.Y + reach) / F.CellH);
                for (int y = Math.Max(0, y0); y <= Math.Min(F.H - 1, y1); y++)
                    for (int x = Math.Max(0, x0); x <= Math.Min(F.W - 1, x1); x++)
                    {
                        var (cx, cy) = F.CenterOf(y * F.W + x);
                        B.ToLocal(cx, cy, out var lx, out var ly);
                        if (Math.Abs(lx) <= hx && Math.Abs(ly) <= hy) extra[y * F.W + x] = true;
                    }
            }
            return extra;
        }

        // Конец хода стола (turn.js): усталость у тех, кто бился 4 хода подряд (элита — 8); сломленный (БД 0) теряет
        // дисциплину, иссякла — бежит без броска (и тянет соседей паникой)
        void EndOfTurnTable(double t)
        {
            var units = Movers.Select(m => m.P.U).ToList();
            var res = BattleCore.Turn.EndTurn(units, Ctx);
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i], n = res.Units[i];
                u.AttacksMade = n.AttacksMade; u.CountersMade = n.CountersMade; u.Acted = n.Acted; u.TurnsActive = n.TurnsActive; u.Fatigue = n.Fatigue;
                u.BreakPenalty = n.BreakPenalty; u.BreakGrace = n.BreakGrace; u.Discipline = n.Discipline;
                if (n.Status == "fled" && u.Status == "active")
                {
                    u.Status = "fled";
                    if (MoraleChecks) { Flee(Movers[i], t, "дисциплина иссякла"); PanicFrom(Movers[i], t); }
                }
            }
            foreach (var l in res.Lines) events.Add($"конец хода · {l}");
        }

        // Павшие в рукопашной (Г67): точного места нет — падают у переднего края схватки, в случайной точке
        // касающихся фигурок, лицом к врагу, кровь брызжет назад (от удара). Случайность — своя, только для рисунка.
        // Г39, В13: кто из павших убит, а кто ранен — по итогам удара стола: доля убитых среди потерь отряда с прошлого раза.
        // Численность в схватке тает по ходу окна удара, а убитые и раненые пишутся, когда окно закрылось, — пока итогов
        // нет, доля ожидаемая: летальность стола со средним броском (Base + (Die + 1)/2 ÷ (опыт ÷ ExpDiv)).
        // Только для рисунка (раненый ползёт): численность и правила не меняются
        readonly Dictionary<Mover, (double k, double w, double share)> meleeSeen = new Dictionary<Mover, (double k, double w, double share)>();
        // Доля убитых среди павших: по итогам последнего закрытого окна (прирост убитых и раненых отряда); итогов ещё
        // не было — ожидаемая летальность стола
        double KilledShare(Mover m)
        {
            var mu = m.P.U; var Lt = R.Lethality;
            if (!meleeSeen.TryGetValue(m, out var seen))
                seen = (0, 0, Js.Clamp(Lt.Base + (Lt.Die + 1) / 2.0 / (Math.Max(1, mu.Exp) / Lt.ExpDiv), 0, 100) / 100);
            double dk = mu.TotKilled - seen.k, dw = mu.TotWounded - seen.w;
            if (dk + dw > 1e-9) seen = (mu.TotKilled, mu.TotWounded, Math.Max(0, Math.Min(1, dk / (dk + dw))));
            meleeSeen[m] = seen;
            return seen.share;
        }
        void MeleeDeaths(Mover m, int n)
        {
            double killedShare = KilledShare(m);
            // Г78: падают те, кто у врага — бойцы касающихся фигурок, ближние к фигурке врага (по тому, где стоят, а не по
            // месту в строю: пересаженный вперёд ещё идёт), чуть вперемешку (хешем); никто не касается — ближайшие к врагу
            var alive = m.Men.Where(x => x.Alive && x.Fig != null).ToList();
            if (alive.Count == 0) return;
            Mover foe = frontFigs.TryGetValue(m, out var fr) ? fr.foe : null;
            if (foe == null)
            {
                double best = double.MaxValue;
                foreach (var e in Movers)
                {
                    if (e == m || !OnField(e) || !Enemies(m.P.U, e.P.U)) continue;
                    double d = JsMath.Hypot(e.P.X - m.P.X, e.P.Y - m.P.Y);
                    if (d < best) { best = d; foe = e; }
                }
            }
            // Б2: ударов не хватило — первыми те, кого бьют, потом касающиеся врага
            var hit = MenMode ? StruckAt(m) : null;
            if (MenMode) MenMelee.Fallback += n;
            double Score(Man x)
            {
                double jit = MoveSim.Hash01(m.P.U.Id, x.Id, 15) * 1.5;
                if (hit != null)
                {
                    if (hit.Contains(x)) return jit;
                    if (x.Foe != null && x.Foe.Alive) return 10 + jit;
                }
                if (x.Fig.Fighting) return JsMath.Hypot(x.X - x.Fig.FoeX, x.Y - x.Fig.FoeY) + jit;
                return 1000 + (foe == null ? 0 : JsMath.Hypot(x.X - foe.P.X, x.Y - foe.P.Y)) + jit;
            }
            int left = n;
            foreach (var x in alive.OrderBy(Score).ThenBy(x => x.Id))
            {
                if (left <= 0) break;
                // Г87: тело из k человек принимает потери по одной, пока не падёт; каждая — павший на месте тела
                while (left > 0 && x.Alive)
                {
                    x.Wound(); left--;
                    double dir = foe != null ? Math.Atan2(x.Y - foe.P.Y, x.X - foe.P.X) * 180 / Math.PI : (x.Facing + 90);   // от врага — за спину
                    double u = look();
                    Deaths.Add(new Death
                    {
                        X = x.X, Y = x.Y, T = curT, Facing = x.Facing + (look() - 0.5) * 60, Dir = dir + (look() - 0.5) * 50,
                        UnitId = m.P.U.Id, ManId = x.Id, Part = u < 0.25 ? "head" : u < 0.8 ? "torso" : "legs",
                        Killed = MoveSim.Hash01(m.P.U.Id, x.Id, 16) < killedShare,
                    });
                }
            }
        }

        // ── охват (Г68): свободные колонны огибают врага — на свободные места по его обводу: фланги, тыл, фронт ──
        // Колонна идёт целиком: голова — к врагу, остальные — за ней наружу; кто из фигурок колонны ближе к месту,
        // та и голова. Угол огибают через точки снаружи, а не сквозь врага. Место за колонной держится, пока она
        // к нему идёт и пока бьётся; мест не хватило — встаёт второй линией за своим. Кому охватывать нечего —
        // идут на свои места в строю.
        double WrapReach(Mover m) => m.P.Fp.Front;
        void Envelop()
        {
            var was = new HashSet<FigState>(Movers.SelectMany(m => m.Figs).Where(s => s.Wrap));
            // колонны каждого отряда в схватке: обернувшиеся, что уже бьются, держат место у врага (kept),
            // свободные (не бьются) — к ближайшему из врагов (free)
            var plan = new Dictionary<(Mover x, Mover y), (List<List<int>> kept, List<List<int>> free)>();
            (List<List<int>> kept, List<List<int>> free) Plan(Mover x, Mover y)
            {
                if (!plan.TryGetValue((x, y), out var p)) plan[(x, y)] = p = (new List<List<int>>(), new List<List<int>>());
                return p;
            }
            foreach (var x in Movers)
            {
                var foes = Fights.Where(f => !f.Over && f.Touching && (f.A == x || f.B == x)).Select(f => f.Other(x)).ToList();
                x.InMelee = foes.Count > 0;
                // Г81: отступающий не охватывает и кольца не держит — колонны к своим местам, иначе (при бойцах-телах, где
                // бьющаяся колонна держит место у врага) отряд не оторвался бы от врага
                bool leaving = x.Order != null && x.Order.Kind == OrderKind.Retreat && !x.Done;
                if (foes.Count == 0 || leaving || x.P.Figs.Count == 0 || !Alive(x))   // бегущий никого не охватывает (Г70)
                {
                    foreach (var s in x.Figs) { s.Wrap = false; s.WFoe = null; }
                    continue;
                }
                var engaged = colFoe.TryGetValue(x, out var cf) ? cf : new Dictionary<int, (Mover foe, double d)>();
                // огибают только того, кто перед фронтом: атакованный во фланг или в тыл сам не поворачивается (Г63)
                var ahead = foes.Where(q => Alive(q) && Math.Abs(MoveSim.AngleDiff(x.P.Facing, MoveSim.HeadingOf(q.P.X - x.P.X, q.P.Y - x.P.Y))) <= R.Sectors.FrontMax).ToList();
                foreach (var g in Enumerable.Range(0, x.P.Figs.Count).GroupBy(k => x.P.Figs[k].File))
                {
                    var col = g.ToList();
                    var head = x.Figs[col[0]];
                    if (engaged.ContainsKey(g.Key))
                    {
                        if (col.Any(k => x.Figs[k].Wrap) && head.WFoe != null && foes.Contains(head.WFoe))
                        {
                            // враг побежал — колонна стоит, где стояла, и держит кольцо, пока касается (окружённым не уйти)
                            if (Alive(head.WFoe)) Plan(x, head.WFoe).kept.Add(col);
                            continue;
                        }
                        foreach (int k in col) { x.Figs[k].Wrap = false; x.Figs[k].WFoe = null; }
                        continue;
                    }
                    foreach (int k in col) x.Figs[k].Wrap = false;
                    if (ahead.Count == 0) { foreach (int k in col) x.Figs[k].WFoe = null; continue; }
                    var y = ahead.OrderBy(q => JsMath.Hypot(q.P.X - head.X, q.P.Y - head.Y)).First();
                    Plan(x, y).free.Add(col);
                }
            }
            foreach (var kv in plan) WrapAround(kv.Key.x, kv.Key.y, kv.Value.kept, kv.Value.free);
            if (MenMode) WrapToFoes();
            // отпущенные из охвата идут на свои места сквозь свой строй (Returning), пока не дойдут
            foreach (var m in Movers)
                for (int k = 0; k < m.Figs.Count; k++)
                {
                    var s = m.Figs[k];
                    if (s.Wrap) { s.Returning = false; continue; }
                    if (was.Contains(s)) s.Returning = true;
                    if (!s.Returning) continue;
                    m.P.ToWorld(m.P.Figs[k].X, m.P.Figs[k].Y, out var sx, out var sy);
                    // у бойцов-тел место держит якорь (середина неполной колонны смещена)
                    double px = MenMode ? s.AX : s.X, py = MenMode ? s.AY : s.Y;
                    if (JsMath.Hypot(px - sx, py - sy) < ReturnedM) s.Returning = false;
                }
        }
        const double ReturnedM = 1;   // дошла до своего места в строю — снова твёрдая для своих

        // Б3: колонна бойцов в охвате дошла до своего места у врага, а врага там нет (его фронт сузился — крайние колонны
        // убраны, Г30): место привязано к рамке врага, а бьются бойцы с бойцами. Такая колонна подходит к ближнему живому врагу
        // (не дальше WrapSeekM) — встаёт головой к нему, стена остановит (Г89). Фигурки касались за 5 м и этого не замечали
        const double WrapSeekM = 8;
        // колонна в охвате держится за выбранного бойца врага (WrapToFoes), пока он жив и не дальше WrapSeekM + 2 м от якоря
        bool SeekHolds(FigState s) => R.Men.WrapSeekSticky && s.WSeekMan != null && s.WSeekMan.Alive && s.Wrap && JsMath.Hypot(s.WSeekMan.X - s.AX, s.WSeekMan.Y - s.AY) <= WrapSeekM + 2
                                      && curT - Math.Max(s.WSeekT, s.FightT) < R.Men.WrapSeekHoldSec;
        void WrapToFoes()
        {
            foreach (var x in Movers)
            {
                if (!Alive(x) || x.P.Figs.Count == 0) continue;
                var f = R.Map.Formation.TryGetValue(x.P.U.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                double rad = R.Men.BodyShare * Math.Min(f.PerMan, f.RankDepth) + Soldiers.BodyHalf(x, R);   // Г87: голова капсулы
                for (int k = 0; k < x.Figs.Count && k < x.P.Figs.Count; k++)
                {
                    var s = x.Figs[k];
                    if (!s.Wrap || s.Fighting || s.WFoe == null || s.MenN == 0 || JsMath.Hypot(s.WX - s.AX, s.WY - s.AY) > 1.5) continue;
                    if (curT - s.FightT < R.Men.WrapRetargetSec) continue;   // только что билась — касание вернётся само, нового врага не ищет
                    if (SeekHolds(s)) continue;   // выбранный боец врага жив и рядом — цель та же (иначе колонна мечется между бойцами врага)
                    Man best = null; double bd = WrapSeekM;
                    foreach (var e in s.WFoe.Men)
                    {
                        if (!e.Alive || Math.Abs(e.X - s.X) > bd || Math.Abs(e.Y - s.Y) > bd) continue;
                        double d = JsMath.Hypot(e.X - s.X, e.Y - s.Y);
                        if (d < bd) { bd = d; best = e; }
                    }
                    if (best == null) continue;
                    double ux = (best.X - s.AX) / Math.Max(1e-9, JsMath.Hypot(best.X - s.AX, best.Y - s.AY)), uy = (best.Y - s.AY) / Math.Max(1e-9, JsMath.Hypot(best.X - s.AX, best.Y - s.AY));
                    double back = x.P.Figs[k].Depth / 2 + 2 * rad + 0.2;   // голова колонны — вплотную к врагу
                    s.WX = best.X - ux * back; s.WY = best.Y - uy * back; s.WH = MoveSim.HeadingOf(ux, uy); s.WSeekT = curT; s.WSeekMan = best;
                }
            }
        }

        // Места по всему обводу врага, снаружи, лицом к нему: обернувшиеся, что бьются, — при своих; кто шёл —
        // к своему (или соседнему, если строй врага сузился); остальные — по близости к врагу, ближайшее к голове
        void WrapAround(Mover x, Mover y, List<List<int>> kept, List<List<int>> cols)
        {
            var P = x.P; var Q = y.P;
            double figW = P.Figs.Max(q => q.Width), figD = P.Figs.Max(q => q.Depth), mg = 0.5;
            double F = Q.Fp.Front / 2, D = Q.Fp.Depth / 2, off = figD / 2 + mg;
            // Б2: колонна бойцов бьётся, только касаясь врага. Место — чтобы передний боец колонны касался врага, а не давил в него
            // (08.10.2026: место на метр внутри строя врага тянуло переднего коня пружиной в стену, стена выталкивала — кони качались
            // туда-сюда раз в 4 с): середина колонны от края врага = полглубины − полшеренги (до середины переднего бойца) − полудлина
            // капсулы коня (она тянется к врагу; у тела из k человек — то же, его середина глубже ровно на столько же) + радиус тела
            // + полдосягаемости
            if (MenMode && !R.Men.WrapStandoffTouch) off = figD / 2 - R.Men.ReachM;
            else if (MenMode)
            {
                var fx = R.Map.Formation.TryGetValue(x.P.U.Type, out var fxx) ? fxx : R.Map.Formation["infantry"];
                off = figD / 2 - fx.RankDepth / 2 - Soldiers.BodyHalf(x, R, 1) + R.Men.BodyShare * Math.Min(fx.PerMan, fx.RankDepth) + R.Men.ReachM / 2;
            }
            var slots = new List<(double lx, double ly, double nx, double ny, double face)>();
            void Edge(bool alongX, double fixedV, double half, double nx, double ny, double face)
            {
                if (2 * half <= figW) { slots.Add(alongX ? (0, fixedV, nx, ny, face) : (fixedV, 0, nx, ny, face)); return; }
                for (double v = -half + figW / 2; v <= half - figW / 2 + 1e-9; v += figW)
                    slots.Add(alongX ? (v, fixedV, nx, ny, face) : (fixedV, v, nx, ny, face));
            }
            Edge(true, -D - off, F, 0, -1, Q.Facing + 180);    // фронт врага
            Edge(true, D + off, F, 0, 1, Q.Facing);            // тыл
            Edge(false, -F - off, D, -1, 0, Q.Facing + 90);    // левый фланг
            Edge(false, F + off, D, 1, 0, Q.Facing - 90);      // правый фланг
            int Nearest(IEnumerable<int> from, double lx, double ly) =>
                from.OrderBy(i => JsMath.Hypot(slots[i].lx - lx, slots[i].ly - ly)).ThenBy(i => i).DefaultIfEmpty(-1).First();
            // занято: у места стоит чья-то фигурка (кроме самого врага и колонн, что здесь раздаются) —
            // каждая занимает одно место, ближайшее к себе
            var mine = new HashSet<FigState>(kept.Concat(cols).SelectMany(c => c.Select(k => x.Figs[k])));
            var busy = new bool[slots.Count];
            double ox = F + off + figW, oy = D + off + figW;
            foreach (var m in Movers)
            {
                if (m == y) continue;
                foreach (var s in m.Figs)
                {
                    if (mine.Contains(s)) continue;
                    Q.ToLocal(s.X, s.Y, out var lx, out var ly);
                    if (Math.Abs(lx) > ox || Math.Abs(ly) > oy) continue;   // вне обвода с запасом в фигурку — ни одного места не занимает
                    int i = Nearest(Enumerable.Range(0, slots.Count), lx, ly);
                    if (i >= 0 && JsMath.Hypot(slots[i].lx - lx, slots[i].ly - ly) < figW * 0.75) busy[i] = true;
                }
            }
            var free = Enumerable.Range(0, slots.Count).Where(i => !busy[i]).ToList();
            void Take(List<int> col, int si, int behind)
            {
                var sl = slots[si];
                foreach (int k in col)
                {
                    var s = x.Figs[k];
                    if (s.WFoe != y) s.WSeekMan = null;   // другой враг — прежняя цель-боец не в счёт
                    s.Wrap = true; s.WFoe = y; s.WSlotX = sl.lx; s.WSlotY = sl.ly; s.WNx = sl.nx; s.WNy = sl.ny; s.WBehind = behind; s.WH = MoveSim.Norm(sl.face);
                }
                Route(x, col, figW, figD, mg);
            }
            // 1) бьются — при своём месте (ближайшем к прежнему: строй врага сужается и места сдвигаются)
            foreach (var col in kept)
            {
                var head = x.Figs[col[0]];
                int si = Nearest(free, head.WSlotX, head.WSlotY);
                if (si >= 0 && JsMath.Hypot(slots[si].lx - head.WSlotX, slots[si].ly - head.WSlotY) < figW) { free.Remove(si); Take(col, si, head.WBehind); }
                else Route(x, col, figW, figD, mg);
            }
            // 2) кто шёл к своему месту — держит его; 3) остальные — по близости к врагу
            var order = cols.OrderBy(c => x.Figs[c[0]].WFoe == y ? 0 : 1)
                            .ThenBy(c => JsMath.Hypot(x.Figs[c[0]].X - Q.X, x.Figs[c[0]].Y - Q.Y)).ToList();
            // сперва те, чьё прежнее место ещё свободно, — остаются на нём (иначе соседи меняются местами каждые полсекунды)
            var later = new List<List<int>>();
            foreach (var col in order)
            {
                var head = x.Figs[col[0]];
                if (head.WFoe != y) { later.Add(col); continue; }
                int same = Nearest(free, head.WSlotX, head.WSlotY);
                if (same >= 0 && JsMath.Hypot(slots[same].lx - head.WSlotX, slots[same].ly - head.WSlotY) < figW * 0.5) { free.Remove(same); Take(col, same, 0); }
                else later.Add(col);
            }
            foreach (var col in later)
            {
                var head = x.Figs[col[0]];
                bool had = head.WFoe == y;
                double lx = head.WSlotX, ly = head.WSlotY;
                if (!had) Q.ToLocal(head.X, head.Y, out lx, out ly);
                int si = Nearest(free, lx, ly);
                if (si >= 0) { free.Remove(si); Take(col, si, 0); }
                else if (had) Take(col, Nearest(Enumerable.Range(0, slots.Count), lx, ly), col.Count);   // мест нет — второй линией за своим
                else foreach (int k in col) x.Figs[k].WFoe = null;
            }
        }

        // Колонна у своего места: голова — та фигурка, что ближе к врагу (по нормали места), остальные — за ней
        // наружу; каждой — первая точка пути в обход строя врага
        void Route(Mover x, List<int> col, double figW, double figD, double mg)
        {
            var h = x.Figs[col[0]]; var Q = h.WFoe.P;
            double sx = h.WSlotX, sy = h.WSlotY, nx = h.WNx, ny = h.WNy; int behind = h.WBehind;
            var byDepth = col.OrderBy(k =>
            {
                Q.ToLocal(x.Figs[k].X, x.Figs[k].Y, out var lx, out var ly);
                return (lx - sx) * nx + (ly - sy) * ny;
            }).ThenBy(k => k).ToList();
            for (int r = 0; r < byDepth.Count; r++)
            {
                var s = x.Figs[byDepth[r]];
                if (SeekHolds(s)) continue;   // сама выбрала бойца врага целью, он жив и рядом — место у рамки её не перебивает (иначе цель прыгает туда-сюда)
                double tx = sx + nx * (behind + r) * figD, ty = sy + ny * (behind + r) * figD;
                var (gx, gy) = AroundCorner(Q, s, tx, ty, figW, figD, mg);
                Q.ToWorld(gx, gy, out var wx, out var wy);
                s.WX = wx; s.WY = wy;
            }
        }

        // Путь к месту у врага в обход его строя: кратчайший путь по углам снаружи (граф видимости: где стоит
        // фигурка, четыре угла, место), берём первую точку. Прямо видно место — сразу к нему; дошла до угла —
        // дальше к следующему углу или к месту, а не снова к тому же углу
        static (double x, double y) AroundCorner(Placed Q, FigState s, double tx, double ty, double figW, double figD, double mg)
        {
            double F = Q.Fp.Front / 2, D = Q.Fp.Depth / 2, a = figD / 2;
            Q.ToLocal(s.X, s.Y, out var cx, out var cy);
            if (Math.Abs(cx) < F + a && Math.Abs(cy) < D + a) return (tx, ty);   // уже вплотную — напрямую
            if (!SegHitsBox(cx, cy, tx, ty, F + a, D + a)) return (tx, ty);
            double c = Math.Max(figW, figD) / 2 + mg, ox = F + c, oy = D + c;
            var pt = new[] { (cx, cy), (-ox, -oy), (ox, -oy), (ox, oy), (-ox, oy), (tx, ty) };
            int n = pt.Length;
            var dist = new double[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
            dist[0] = 0;
            for (int it = 0; it < n; it++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < double.MaxValue && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || u == n - 1) break;
                done[u] = true;
                for (int v = 1; v < n; v++)
                {
                    if (done[v] || SegHitsBox(pt[u].Item1, pt[u].Item2, pt[v].Item1, pt[v].Item2, F + a, D + a)) continue;
                    double w = dist[u] + JsMath.Hypot(pt[v].Item1 - pt[u].Item1, pt[v].Item2 - pt[u].Item2);
                    if (w < dist[v]) { dist[v] = w; prev[v] = u; }
                }
            }
            if (prev[n - 1] < 0) return (tx, ty);
            int k = n - 1;
            while (prev[k] != 0) k = prev[k];
            return pt[k];
        }
        // Пересекает ли отрезок прямоугольник |x| < hx, |y| < hy (Лян — Барски)
        static bool SegHitsBox(double x0, double y0, double x1, double y1, double hx, double hy)
        {
            double t0 = 0, t1 = 1, dx = x1 - x0, dy = y1 - y0;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-12) return q > 0;
                double r = q / p;
                if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            return Clip(-dx, x0 + hx) && Clip(dx, hx - x0) && Clip(-dy, y0 + hy) && Clip(dy, hy - y0) && t1 - t0 > 1e-9;
        }

        // ── потери — фигурками (Г30): раскладка по нынешней численности, фронт сужается с краёв, как фишка
        // трекера; лишние фигурки падают на месте. Строй развёрнут — место в строю (колонна, шеренга) остаётся за
        // той же фигуркой: колонна не рвётся, даже если ушла в охват (Г68) далеко от своего места. Остальные места
        // (и всё в походной колонне) — ближайшим фигуркам спереди назад ──
        // Г101: перестроиться — глубина строя в шеренгах (0 — по столу). Строй переразмечается на месте, бойцы идут на новые места
        // шагом (В14); бегущий не перестраивается. Возвращает false, если нечего менять
        public bool SetRanks(Mover m, int ranks)
        {
            ranks = Math.Max(0, Math.Min(ranks, R.Move.RanksMax));
            if (m.Gone || m.Fleeing || m.P.Figs.Count == 0 || m.P.U.Ranks == ranks) return false;
            var was = Formation.Of(m.P.U, R);
            m.P.U.Ranks = ranks;
            var now = Formation.Of(m.P.U, R);
            if (Math.Abs(was.Depth - now.Depth) < 1e-9 && Math.Abs(was.Front - now.Front) < 1e-9) return false;
            m.LaidMen = -1;   // раскладка заново, потерь не было
            Relayout(m);
            m.Reforming = true;   // пока бойцы идут на новые места — без жёстких (Г86) и вполсилы на ходу (Г60)
            events.Add($"«{m.P.U.Name}»: перестроение — {RanksName(m.P.U)} ({now.Front:0} × {now.Depth:0} м)");
            return true;
        }
        public string RanksName(Unit u)
        {
            var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
            double k = u.Ranks <= 0 ? 1 : u.Ranks / f.Ranks;
            return k < 0.75 ? "цепь" : k < 1.5 ? "линия" : k < 3 ? "глубокий строй" : "колонна";
        }

        public void Relayout(Mover m)
        {
            if (m.Gone) return;   // ушёл с поля — раскладывать некого
            if (m.Fleeing) { RelayoutCrowd(m); return; }
            var P = m.P;
            int n = (int)Math.Max(0, Js.Round(P.U.Soldiers));
            if (n == m.LaidMen) return;
            int melee = Math.Max(0, m.LaidMen - n - m.ShotDown - m.StruckDown);
            m.ShotDown = 0; m.StruckDown = 0;
            if (melee > 0) MeleeDeaths(m, melee);
            m.LaidMen = n;
            int oldCols = m.Cols, oldNominal = m.NominalCols;
            var figs = Formation.Layout(P.U, MenPerFigure, R);
            var free = Enumerable.Range(0, m.Figs.Count).ToList();
            var bodies = new FigState[figs.Count];
            // колонны — по номеру файла и ряда (раскладка убирает файлы с края, остальные остаются своими), даже если колонн стало
            // меньше: по ближайшему телу широкие фигурки (2–3 файла) при сдвиге строя к центру цеплялись за соседние, и бойцы
            // сползали к краю (Г104, гарнизон в 3 шеренги). Сужен в колонну (Г59) — файлы другие, тогда по ближайшему
            if (oldCols == oldNominal)
            {
                var had = new Dictionary<(int file, int rank), int>();
                for (int q = 0; q < P.Figs.Count; q++) had[(P.Figs[q].File, P.Figs[q].Rank)] = q;
                for (int k = 0; k < figs.Count; k++)
                    if (had.TryGetValue((figs[k].File, figs[k].Rank), out var q)) { bodies[k] = m.Figs[q]; free.Remove(q); }
            }
            foreach (int k in Enumerable.Range(0, figs.Count).OrderBy(i => figs[i].Y).ThenBy(i => figs[i].X))
            {
                if (bodies[k] != null) continue;
                P.ToWorld(figs[k].X, figs[k].Y, out var wx, out var wy);
                int best = -1; double bd = double.MaxValue;
                for (int q = 0; q < free.Count; q++)
                {
                    var s = m.Figs[free[q]];
                    double d = (s.X - wx) * (s.X - wx) + (s.Y - wy) * (s.Y - wy);
                    if (d < bd) { bd = d; best = q; }
                }
                if (best >= 0) { bodies[k] = m.Figs[free[best]]; free.RemoveAt(best); }
                else bodies[k] = new FigState { Id = m.NextFigId++, X = wx, Y = wy, AX = wx, AY = wy };
            }
            foreach (int q in free) m.Fallen.Add((m.Figs[q].X, m.Figs[q].Y));
            P.Figs = figs; P.Fp = Formation.Of(P.U, R);
            m.Figs = bodies.ToList();
            m.Nominal = figs.Select(f => (f.X, f.Y, f.Rank, f.File)).ToList();
            m.NominalFp = new Footprint { Front = P.Fp.Front, Depth = P.Fp.Depth };
            m.NominalCols = m.Cols = figs.Count == 0 ? 0 : figs.Max(f => f.File) + 1;
            if (oldCols < oldNominal && m.NominalCols > 0) MoveSim.SetCols(m, Math.Min(oldCols, m.NominalCols));   // был в колонне — остаётся
            Soldiers.Assign(m, R);   // бойцы — на места новой раскладки: задние выходят вперёд (Г30, Г75)
        }
    }
}
