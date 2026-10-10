// ═══════════ PlayController.cs — режим игры (И2, Г79–Г81): приказы и ход ═══════════
// Фаза приказов: выбираешь отряд или группу, отдаёшь приказ (мышью как в Total War, клавишами, кнопками панели),
// подсказка на карте показывает путь и где отряд встанет к концу хода. «Ход!» — приказы обеих сторон уходят разом
// (WEGO), ход считается движком шаг за шагом во время показа (Г43): показ не обгоняет счёт — не успевает машина, ждёт.
// Рисунок отрядов — смотрелка (BattleViewer, живая запись), подсказки приказов — OrderOverlay, панели — PlayHud.
// Боевой математики здесь нет: только ввод, вызовы движка и что показать.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BattleCore;
using Journal.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
using Terrain = BattleCore.Terrain;

namespace Journal.Play
{
    public enum PlayPhase { Orders, Showing, Over }

    public sealed class PlayController : MonoBehaviour
    {
        public PlayBattle Game { get; private set; }
        public BattleSession Session => Game?.Session;
        public Battle Battle => Game?.Battle;
        public PlayPhase Phase { get; private set; } = PlayPhase.Orders;
        public int ActiveSide { get; set; } = 1;             // чьи карточки внизу (Г79: обе стороны с одного экрана)
        // выбор: группа отрядов одной стороны; Selected — главный (последний выбранный) — его карточка слева
        public readonly List<Mover> Selection = new List<Mover>();
        public Mover Selected { get; private set; }
        public bool IsSelected(Mover m) => m != null && Selection.Contains(m);
        public Mover Hover { get; private set; }
        public Mover UiHover { get; set; }                   // отряд под мышью на панели (табличка над ним, карточка)
        public bool ChargeMode { get; set; }                 // кнопка «Натиск»: следующая атака — с натиском
        public bool Blocked { get; set; }                    // открыто меню — битва ввод не получает
        public float Speed { get; set; } = 1;
        public bool Paused { get; set; }
        public double ShowTime { get; private set; }         // время показа, с от начала битвы
        public double TurnStartTime { get; private set; }
        public double ComputedTime => recorder == null ? 0 : (recorder.Rec.Frames.Count - 1) * recorder.Rec.Dt;
        public string Toast { get; private set; } = ""; public float ToastUntil;
        public event Action Changed;                         // приказ, выбор, фаза — панели перерисоваться
        // сводки ходов (TurnSummary): снимок на «Ход!», итог — когда показ хода кончился; последняя — в панели справа
        public readonly List<TurnSummary> Summaries = new List<TurnSummary>();
        // итог битвы (Г98: «кто сколько потерял»): отряд на начало битвы — бойцов, убитых и раненых всего (счётчики
        // отряда копятся с прошлых битв кампании — итог считает разницу); EndedByPlayer — битву остановили кнопкой
        public readonly Dictionary<Mover, (double Men, double Killed, double Wounded)> AtStart = new Dictionary<Mover, (double, double, double)>();
        public bool EndedByPlayer { get; private set; }
        public bool Chosen { get; private set; }             // битву выбрал игрок (а не поле-заставка при запуске)
        public event Action MenuRequested;                   // Esc без выбора и без протяжки — главное меню (пауза)
        public TurnSummary LastSummary => Summaries.Count > 0 ? Summaries[Summaries.Count - 1] : null;
        TurnSummary current;

        BattleViewer viewer;
        Recorder recorder;
        int stepInTurn;
        // подсказки приказов: отряд → предпросмотр его приказа (новый или прежний). Предпросмотр — путь движком, 25–50 мс
        // на отряд: главному — сразу, остальным — очередью, не дольше 6 мс за кадр (иначе приказ сорока отрядам — стоп-кадр)
        readonly Dictionary<Mover, OrderPreview> previews = new Dictionary<Mover, OrderPreview>();
        readonly List<Mover> previewQueue = new List<Mover>();
        // приказ, который тянут ПКМ прямо сейчас: каждому выбранному — свой; путь движком — только главному (дорого)
        public bool Dragging { get; private set; }
        public Vector2 DragFrom { get; private set; }        // точки карты, м
        public Vector2 DragTo { get; private set; }
        public OrderPreview DragPreview { get; private set; }
        public MoveOrder DragOrder { get; private set; }
        public readonly Dictionary<Mover, MoveOrder> DragOrders = new Dictionary<Mover, MoveOrder>();
        // Г111 п.1: прочим в группе, пока тянут, — тоже место на конец хода: путь движком, когда мышь остановилась (по одному
        // отряду за кадр: предпросмотр — 25–50 мс), а пока тянут — по прямой на норму хода (OrderOverlay)
        public readonly Dictionary<Mover, (MoveOrder o, OrderPreview p)> DragPreviews = new Dictionary<Mover, (MoveOrder, OrderPreview)>();
        Vector2 dragSeen; float dragStillAt;
        public static bool SameOrder(MoveOrder a, MoveOrder b) => a != null && b != null && a.Kind == b.Kind && a.TargetId == b.TargetId && a.Charge == b.Charge
            && Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(MoveSim.AngleDiff(a.Facing, b.Facing)) < 2;
        float previewAt;
        // рамка выбора ЛКМ по земле (экранные точки)
        public bool BoxSelecting { get; private set; }
        public Vector2 BoxA { get; private set; }
        public Vector2 BoxB { get; private set; }
        bool leftDown;
        Func<Vector2, bool> overUi = _ => false;

        public void SetUiPicker(Func<Vector2, bool> f) => overUi = f;

        // Awake, а не Start: живая запись должна попасть в смотрелку раньше её Start — иначе она начнёт считать свою сцену
        void Awake()
        {
            viewer = FindAnyObjectByType<BattleViewer>();
            // Г121: полководец со свитой подсвечен, когда его отряд под мышью или выбран
            MenView.Lit = ui => { var ms = Battle?.Movers; if (ms == null || ui >= ms.Count) return 0; var m = ms[ui]; return IsSelected(m) ? 2 : m == Hover ? 1 : 0; };
            NewBattle();
        }

        Func<PlayBattle> lastMake = () => PlayScenarios.Training();
        // новая битва: из меню — выбранная; «ещё раз» — та же
        public void NewBattle(Func<PlayBattle> make = null)
        {
            if (make != null) { lastMake = make; Chosen = true; }
            StopCompute();   // ход прежней битвы ещё считается — остановить
            Game = lastMake();
            Rerecord();
            Phase = PlayPhase.Orders; Selection.Clear(); Selected = null; Hover = null; ChargeMode = false; Paused = false; Speed = GameSettings.Speed;
            Summaries.Clear(); current = null; EndedByPlayer = false; BattleViewer.ViewSide = 0; GmLog.Clear(); gmUndo.Clear(); CardsAll = false; GmPlacing = null; turnSnaps.Clear(); MidTurn = false;
            AiPicking = null; Reviews.Clear(); Advice.Clear(); adviceNew.Clear(); adviceSeen.Clear(); DragAdvice.Clear();
            AtStart.Clear(); foreach (var m in Game.Battle.Movers) AtStart[m] = (m.P.U.Soldiers, m.P.U.TotKilled, m.P.U.TotWounded);
            ShowTime = TurnStartTime = 0; stepInTurn = 0;
            previews.Clear(); previewQueue.Clear();
            if (viewer != null) { viewer.ShowGui = false; viewer.SetLive(recorder.Rec); viewer.Playing = false; }
            if (Chosen) StartDeploy();   // выбранная битва — с расстановки (заставка — сразу в бой)
            RefreshPreviews();
            Changed?.Invoke();
        }

        // ── расстановка (Алекс 10.10.2026: «нужен этап расстановки»): до первого хода — фаза Deploy сессии. Свои отряды тянешь ЛКМ,
        // Q/E — повернуть, ходов не тратит, приказов не даёт (BattleSession.Place); зона стороны — своя половина поля, если войска
        // стоят врозь (из сохранения трекера вперемешку — где угодно). «К бою» — к приказам первого хода ──
        public bool Deploying => Game != null && Phase == PlayPhase.Orders && Session?.Phase == BattleCore.Phase.Deploy;
        void StartDeploy()
        {
            // сценарий выдал всем «держать» — BeginDeploy ждёт отряды без приказов: снять, на «К бою» — вернуть (держат, где встали)
            foreach (var m in Battle.Movers) if (m.Order != null && m.Order.Kind == OrderKind.Hold) m.Order = null;
            if (!Session.BeginDeploy()) return;
            Session.Zones.Clear();
            var sides = Session.Sides.ToList();
            if (sides.Count == 2)
            {
                Vector2 C(int sd) { var L = Battle.Movers.Where(m => SideOf(m) == sd && Present(m)).ToList(); return L.Count == 0 ? Vector2.zero : new Vector2((float)L.Average(m => m.P.X), (float)L.Average(m => m.P.Y)); }
                Vector2 a = C(sides[0]), c = C(sides[1]); float dist = (a - c).magnitude;
                if (dist > 200)
                {
                    double W = Game.Geo.W, H = Game.Geo.H, gap = Math.Max(30, Math.Min(150, dist * 0.15));
                    bool alongY = Math.Abs(a.y - c.y) >= Math.Abs(a.x - c.x);
                    double mid = alongY ? (a.y + c.y) / 2 : (a.x + c.x) / 2;
                    bool firstLow = alongY ? a.y < c.y : a.x < c.x;
                    var low = alongY ? (0.0, 0.0, W, mid - gap) : (0.0, 0.0, mid - gap, H);
                    var high = alongY ? (0.0, mid + gap, W, H) : (mid + gap, 0.0, W, H);
                    Session.Zones[sides[0]] = firstLow ? low : high; Session.Zones[sides[1]] = firstLow ? high : low;
                }
            }
        }
        void EndDeployNow()
        {
            Session.EndDeploy();
            foreach (var m in Battle.Movers) if (Present(m) && m.Order == null) Battle.Order(m, new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = m.P.Facing });
            RefreshPreviews();
            Say("К бою! Приказы первого хода — и «Ход!»"); Changed?.Invoke();
        }
        public bool InZone(int side, double x, double y) => Session == null || !Session.Zones.TryGetValue(side, out var z) || x >= z.x0 && x <= z.x1 && y >= z.y0 && y <= z.y1;
        void Rerecord()
        {
            recorder = new Recorder(Game.Name, Game.Note, Game.Geo, Game.Battle.Movers, m => Game.Tpl[m], Game.Battle, 99,
                                    m => Game.Color.TryGetValue(m, out var c) ? c : null, m => Game.Style.TryGetValue(m, out var st) ? st : null);
            recorder.Rec.Image = Game.Image;
            var bt = Game.Battle; recorder.Sees = (sd, m) => bt.Sees(sd, m);   // туман (Г107)
            recorder.Snap();
        }

        // ── расстановка руками до первого хода (Алекс 09.10.2026, п. 1: «возможность разместить отряды»): ЛКМ по своему отряду и
        // тянуть — рамка строя за мышью (зелёная — встанет, красная — на дом, стену или воду: сдвинется к ближайшему месту, где
        // помещается, Б5), Q/E — повернуть на 15°, отпустить — отряд переставлен (Battle.Relocate, Г104). Отпустить пехоту на
        // стене или башне — гарнизон: встаёт вдоль стены фронтом наружу (Battle.Garrison)
        public bool HasWalls => Game?.Geo?.Map != null && Game.Geo.Map.T.Any(t => t == WallId || t == TowerId);   // есть ли на карте стены
        static readonly byte WallId = Terrain.Id("wall"), TowerId = Terrain.Id("tower");
        // ── Г18: туман войны — переключатель вида (Алекс 10.10.2026): ГМ видит всё; сторона — своих и тех чужих, кого видит
        // (видимость — из движка, пока его нет — видно всех). Вид стороны — её же приказы: активная сторона следует за видом ──
        public int ViewSide
        {
            get => BattleViewer.ViewSide;
            set { BattleViewer.ViewSide = value; if (value > 0 && ActiveSide != value) { ActiveSide = value; Select(null); } Changed?.Invoke(); }
        }
        public void CycleView()
        {
            var order = new List<int> { 0 }; order.AddRange(Session.Sides);
            int i = order.IndexOf(ViewSide); ViewSide = order[(i + 1) % order.Count];
            Say(ViewSide == 0 ? "Вид: ГМ — видно всё" : $"Вид: {Session.Name(ViewSide)} — чужие, только кого видит");
        }
        // виден ли отряд сейчас тому, чьими глазами смотрим
        public bool SeenNow(Mover m)
        {
            var rec = viewer?.Rec; int i = Battle.Movers.IndexOf(m);
            if (rec == null || i < 0) return true;
            return rec.Visible(i, Mathf.Clamp((int)(viewer.T / rec.Dt), 0, Mathf.Max(0, rec.Frames.Count - 1)), ViewSide);
        }
        public bool CanDeploy => Deploying;   // переставлять отряды — только в расстановке
        bool BeforeFirstTurn => Game != null && Phase == PlayPhase.Orders && Session != null && Session.Turn <= 1 && ShowTime <= 0;
        public bool DeployDragging { get; private set; }
        public Mover DeployUnit { get; private set; }
        public double DeployX, DeployY, DeployFacing; public bool DeployOk;
        public bool DeployWall; public Vector2 DeployWallA, DeployWallB;   // тянут на стену: ряд стены, вдоль которого встанут
        Vector2 deployFrom, deployCursor; double grabX, grabY;
        public bool Redeploy(Mover m, double x, double y, double facing)
        {
            if (!CanDeploy || !Battle.Movers.Contains(m)) return false;
            var why = Session.Place(m, x, y, facing);
            if (why != null) { Say($"«{m.P.U.Name}»: {why}"); return false; }
            m.Garrisoned = false;   // сняли со стены
            Deployed(m);
            double moved = Math.Sqrt((m.P.X - x) * (m.P.X - x) + (m.P.Y - y) * (m.P.Y - y));
            Say(moved > 1 ? $"«{m.P.U.Name}»: там строй не помещается — встал в {moved:0} м рядом" : $"«{m.P.U.Name}» переставлен");
            return true;
        }
        // переставлен до первого хода: новый приказ снят — стоит, где встал; запись боя — заново (кадр 0 — с новыми местами)
        // расстановка протяжкой ПКМ (Алекс 10.10.2026: «нельзя свободно повернуть, только перетаскивать»): как приказ «идти» —
        // линия фронта и угол, Q/E — доворот, — но отряды встают туда сразу (Session.Place), ходов не тратя
        void DeployTo(List<KeyValuePair<Mover, MoveOrder>> orders)
        {
            int ok = 0; string last = null;
            foreach (var kv in orders)
            {
                var o = kv.Value;
                if (o.Kind != OrderKind.Move || double.IsNaN(o.X)) { last = "в расстановке — только встать (ПКМ по земле)"; continue; }
                var why = Session.Place(kv.Key, o.X, o.Y, double.IsNaN(o.Facing) ? kv.Key.P.Facing : o.Facing);
                if (why == null) { kv.Key.Garrisoned = false; ok++; } else last = $"«{kv.Key.P.U.Name}»: {why}";
            }
            if (ok > 0) Redrawn();
            Say(last != null ? last + (ok > 0 ? $" (встали {ok})" : "") : ok == 1 ? "Встал" : $"Встали: {ok}");
        }
        // Q/E в расстановке без протяжки — выбранные поворачиваются на месте
        void DeployTurn(double by)
        {
            int ok = 0; string last = null;
            foreach (var m in Selection.ToList())
            {
                var why = Session.Place(m, m.P.X, m.P.Y, MoveSim.Norm(m.P.Facing + by));
                if (why == null) ok++; else last = $"«{m.P.U.Name}»: {why}";
            }
            if (ok > 0) Redrawn();
            if (last != null) Say(last);
        }
        // переставлены в расстановке: запись — заново (кадр 0 — с новыми местами), подсказки — заново
        void Redrawn()
        {
            Rerecord();
            if (viewer != null) viewer.SetLive(recorder.Rec, fit: false);
            RefreshPreviews(); Changed?.Invoke();
        }
        void Deployed(Mover m)
        {
            Session.Pending.Remove(m);
            if (!Deploying) Battle.Order(m, new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = m.P.Facing });   // в расстановке приказов нет — «держать» даст «К бою»
            previews.Remove(m);
            Rerecord();
            if (viewer != null) viewer.SetLive(recorder.Rec, fit: false);
            RefreshPreviews(); Changed?.Invoke();
        }

        // ── Г104: гарнизон на стенах (Алекс 09.10.2026, п. 1) ──
        // «На стену» (Н): пехота встаёт вдоль ближайшего прямого ряда стены и башен (не дальше 60 м) фронтом наружу, кто не влез —
        // во дворе за стеной. До первого хода — переставляется сразу, потом — идёт туда (приказ движка, «Ход!» его не сменит).
        // Чьи стены — решает первый гарнизон или щелчок по воротам (Battle.FortOwner): пехота хозяина ходит по стенам и сквозь
        // свои ворота, врагу стены непроходимы, ворота — только открытые. Конница на стену не идёт
        string WallWhy(Mover m)
        {
            if (Phase != PlayPhase.Orders) return "приказы — между ходами";
            if (!Present(m) || m.Fleeing) return "бежит — не до стен";
            if (m.P.U.Type == "cavalry") return "конница на стену не идёт";
            var own = Battle.FortOwner;
            if (own.HasValue && own.Value != SideOf(m)) return $"стены держит {Session.Name(own.Value)}";
            return null;
        }
        // onWall — отпустили на стене (мышь у клетки стены, ± клетка): точку — на саму клетку, тогда «наружу» движок считает
        // от середины всех стен; иначе (x, y) — двор, наружу — от него. Ряд вдоль стены выбирает движок (Garrison.RowPick)
        public bool ToWall(Mover m, double x, double y, bool quiet = false, bool onWall = false)
        {
            string why = WallWhy(m);
            if (why == null && onWall && WallCell(x, y, 8, out var wx, out var wy)) { x = wx; y = wy; }
            if (why == null && !Battle.Garrison(m, x, y)) why = "стены ближе 60 м нет или на ней нет места";
            if (why != null) { if (!quiet) Say($"«{m.P.U.Name}»: {why}"); return false; }
            if (CanDeploy) Deployed(m);
            else { Session.Pending.Remove(m); QueuePreview(m); Changed?.Invoke(); }
            if (!quiet)
            {
                int all = m.Men.Count(mm => mm.Alive), up = m.Men.Count(mm => mm.Alive && mm.Z > 0);
                double men = m.P.U.Soldiers, atop = all > 0 ? men * up / all : 0;
                Say(CanDeploy ? $"«{m.P.U.Name}» на стене: {atop:0} на боевом ходу, {men - atop:0} во дворе" : $"«{m.P.U.Name}» идёт на стену");
            }
            return true;
        }
        public void WallSelected()
        {
            if (Selection.Count == 1) { var m = Selection[0]; ToWall(m, m.P.X, m.P.Y); return; }
            int ok = 0; string last = null;
            foreach (var m in Selection.ToList())
            {
                if (ToWall(m, m.P.X, m.P.Y, quiet: true)) ok++;
                else last = WallWhy(m) is string w ? $"«{m.P.U.Name}»: {w}" : $"«{m.P.U.Name}»: стены рядом нет или нет места";
            }
            Say(ok > 0 ? $"На стену: {ok} отр." + (last != null ? $" ({last})" : "") : last ?? "Некого ставить на стену");
        }
        // клетка стены или башни у точки (± клетка — тонкую стену иначе не попасть мышью)
        bool WallNear(double x, double y)
        {
            var map = Game.Geo.Map; if (map == null) return false;
            byte wall = Terrain.Id("wall"), tower = Terrain.Id("tower");
            int cx = (int)(x / map.Cell), cy = (int)(y / map.Cell);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy; if (nx < 0 || ny < 0 || nx >= map.W || ny >= map.H) continue;
                    int t = map.T[ny * map.W + nx]; if (t == wall || t == tower) return true;
                }
            return false;
        }
        // для подсказки: прямой ряд клеток стены и башен через ближайшую к точке — концы, м (как его выберет Battle.Garrison)
        void WallRun(double x, double y, out Vector2 a, out Vector2 b)
        {
            var map = Game.Geo.Map; byte wall = Terrain.Id("wall"), tower = Terrain.Id("tower"); int W = map.W, H = map.H; float c = (float)map.Cell;
            bool Walk(int i) => map.T[i] == wall || map.T[i] == tower;
            a = b = new Vector2((float)x, (float)y);
            if (!WallCell(x, y, 12, out var wx, out var wy)) return;
            int bx = (int)(wx / c), by = (int)(wy / c);
            int Len(int dx, int dy) { int k = 0; while (bx + dx * (k + 1) >= 0 && by + dy * (k + 1) >= 0 && bx + dx * (k + 1) < W && by + dy * (k + 1) < H && Walk((by + dy * (k + 1)) * W + bx + dx * (k + 1))) k++; return k; }
            int xl = Len(-1, 0), xh = Len(1, 0), yl = Len(0, -1), yh = Len(0, 1);
            if (xl + xh >= yl + yh) { a = new Vector2((bx - xl) * c, (by + 0.5f) * c); b = new Vector2((bx + xh + 1) * c, (by + 0.5f) * c); }
            else { a = new Vector2((bx + 0.5f) * c, (by - yl) * c); b = new Vector2((bx + 0.5f) * c, (by + yh + 1) * c); }
        }

        // ближайшая клетка стены (башни — только если стены рядом нет) не дальше maxM — её середина, м
        bool WallCell(double x, double y, double maxM, out double cx, out double cy)
        {
            var map = Game.Geo.Map; cx = x; cy = y; if (map == null) return false;
            byte wall = Terrain.Id("wall"), tower = Terrain.Id("tower"); double c = map.Cell; int r = (int)Math.Ceiling(maxM / c) + 1;
            int x0 = (int)(x / c), y0 = (int)(y / c); double bw = maxM * maxM, bt = maxM * maxM; int best = -1, bestT = -1;
            for (int yy = Math.Max(0, y0 - r); yy <= Math.Min(map.H - 1, y0 + r); yy++)
                for (int xx = Math.Max(0, x0 - r); xx <= Math.Min(map.W - 1, x0 + r); xx++)
                {
                    int i = yy * map.W + xx, t = map.T[i]; if (t != wall && t != tower) continue;
                    double d = ((xx + 0.5) * c - x) * ((xx + 0.5) * c - x) + ((yy + 0.5) * c - y) * ((yy + 0.5) * c - y);
                    if (t == wall && d < bw) { bw = d; best = i; } else if (t == tower && d < bt) { bt = d; bestT = i; }
                }
            if (best < 0) best = bestT; if (best < 0) return false;
            cx = (best % map.W + 0.5) * c; cy = (best / map.W + 0.5) * c; return true;
        }

        // ворота: щелчок между ходами — открыть или закрыть (только хозяин стен; ничьи стены станут твоими — ворота закрыты)
        public int GateHover { get; private set; } = -1;
        public Recording ViewRec => viewer?.Rec;             // что показано и на какое время — для подсказок по записи (прочность ворот)
        public double ViewT => viewer != null ? viewer.T : ShowTime;
        public GateRec GateOf(int g) => recorder != null && g >= 0 && g < recorder.Rec.Gates.Count ? recorder.Rec.Gates[g] : null;
        int GateAt(Vector2 p)
        {
            var gs = recorder?.Rec.Gates; if (gs == null || gs.Count == 0) return -1;
            int best = -1; float bd = Mathf.Max(6, 12 / PixelsPerMeter);
            for (int g = 0; g < gs.Count; g++) { float d = Vector2.Distance(p, new Vector2(gs[g].X, gs[g].Y)); if (d < bd) { bd = d; best = g; } }
            return best;
        }
        bool ToggleGate(int g)
        {
            if (g < 0 || Phase != PlayPhase.Orders) return false;
            var gr = recorder.Rec.Gates[g]; var own = Battle.FortOwner;
            if (own.HasValue && own.Value != ActiveSide) { Say($"Ворота держит {Session.Name(own.Value)} — открыть их может только хозяин стен"); return true; }
            if (!own.HasValue) { Battle.FortOwner = ActiveSide; Say($"Крепость — {Session.Name(ActiveSide)}: ворота закрыты, свои проходят, враг — нет"); }
            else
            {
                bool open = !Battle.GateOpen(gr.X, gr.Y);
                Battle.SetGate(gr.X, gr.Y, open);
                Say(open ? "Ворота открыты — войти может и враг" : "Ворота закрыты — свои проходят, враг — нет");
            }
            recorder.RefreshGates();
            RefreshPreviews(); Changed?.Invoke();
            return true;
        }

        // Г101: построение выбранных — глубина строя от стола: цепь ½, линия 1 (по столу), глубокий строй 2, колонна 4.
        // Перестраиваются на месте (Battle.SetRanks): бойцы идут на новые места шагом; бегущий не перестраивается
        public void SetFormation(double share)
        {
            if (Phase != PlayPhase.Orders || Battle == null || Selection.Count == 0) return;
            int n = 0;
            foreach (var m in Selection)
            {
                if (!Present(m) || m.Fleeing) continue;
                var f = Battle.R.Map.Formation.TryGetValue(m.P.U.Type, out var ff) ? ff : Battle.R.Map.Formation["infantry"];
                if (Battle.ShapeKey(m.P.U) != "line" && Battle.ShapeWhy(m, "line") == null) Battle.SetShape(m, "line");   // глубина — у линии (Г106)
                int ranks = Math.Abs(share - 1) < 1e-9 ? 0 : Math.Max(1, (int)Math.Round(f.Ranks * share));
                if (Battle.SetRanks(m, ranks)) n++;
            }
            var lead = Selected ?? Selection[0];
            Say(n > 0 ? $"Перестроение: {Battle.RanksName(lead.P.U)} — бойцы идут на новые места (на это уйдёт ход-другой)" : "Строй уже такой");
            RefreshPreviews(); Changed?.Invoke();
        }

        // Г106: фигура строя — клин, полумесяц, каре, круг, разомкнуть и сомкнуть ряды (Battle.SetShape). Перестраиваются на месте,
        // бойцы идут на новые места шагом; кто не может — пропускается с причиной
        public bool ShapesReady => true;
        public string ShapeOf(Mover m)
        {
            if (m == null || Battle == null) return null;
            string k = Battle.ShapeKey(m.P.U);
            return k != null && k != "line" ? k : m.P.U.Open ? "open" : null;
        }
        public string FormHover { get; set; }   // строй под мышью в ряду строев — призрак его контура у выбранных
        public void SetShape(string shape)
        {
            if (Phase != PlayPhase.Orders || Battle == null || Selection.Count == 0) return;
            int n = 0; string last = null;
            foreach (var m in Selection)
            {
                if (!Present(m)) continue;
                var why = Battle.ShapeWhy(m, shape);
                if (why == null && Battle.SetShape(m, shape)) n++; else last = $"«{m.P.U.Name}»: {why ?? "не вышло"}";
            }
            var lead = Selected ?? Selection[0];
            Say(n > 0 ? $"Строй: {Battle.ShapeName(lead.P.U)} — бойцы идут на новые места" + (last != null && Selection.Count > 1 ? $" ({last})" : "") : last ?? "Строй уже такой");
            RefreshPreviews(); Changed?.Invoke();
        }

        // ── Г108: поединок полководцев (Алекс 10.10.2026, как в Three Kingdoms): «Поединок» (П), потом ПКМ по вражескому отряду
        // с полководцем — вызов; вызванная сторона в эту же фазу приказов принимает или отказывается (отказ — её войску −БД);
        // принят — со следующего хода оба отряда стоят, стража держит кольцо, полководцы бьются ──
        public bool DuelMode { get; set; }
        public Commander CommanderOf(Mover m) => m == null ? null : Battle?.Ctx?.CommanderOf?.Invoke(m.P.U);
        // Г121: кнопка и П не молчат — нельзя вызвать, значит сказать почему (нет полководца, бежит, уже в поединке, враг дальше 80 м)
        public string DuelWhy(Mover m)
        {
            if (Phase != PlayPhase.Orders) return "вызов — между ходами";
            if (m == null) return "выбери отряд с полководцем";
            if (Selection.Count > 1) return "вызывает один отряд — выбери один";
            var c = CommanderOf(m);
            if (c == null || c.Dead) return $"в отряде «{m.P.U.Name}» нет полководца";
            if (m.Fleeing) return $"«{m.P.U.Name}» бежит";
            if (Battle.Challenges.Any(x => x.from == m || x.to == m) || Battle.Duels.Any(d => !d.Over && (d.A == m || d.B == m))) return $"«{c.Name}» уже в поединке или вызван";
            var foes = Battle.Movers.Where(e => SideOf(e) != SideOf(m) && Present(e) && SeenNow(e) && CommanderOf(e) is Commander ce && !ce.Dead)
                                    .Select(e => (e, d: Math.Sqrt((e.P.X - m.P.X) * (e.P.X - m.P.X) + (e.P.Y - m.P.Y) * (e.P.Y - m.P.Y)))).OrderBy(x => x.d).ToList();
            if (foes.Count == 0) return "у врага на поле не видно полководцев";
            double range = Battle.R.Duel.RangeM;
            if (foes[0].d > range) return $"ближайший вражеский полководец «{CommanderOf(foes[0].e).Name}» («{foes[0].e.P.U.Name}») в {foes[0].d:0} м — вызвать можно не дальше {range:0} м";
            return null;
        }
        public void ToggleDuel()
        {
            if (DuelMode) { DuelMode = false; Say("Вызов на поединок — отменён"); return; }
            var why = DuelWhy(Selected);
            if (why != null) { Say("Поединок: " + why); return; }
            DuelMode = true;
            Say($"«{CommanderOf(Selected).Name}» ищет противника: ПКМ по вражескому отряду с полководцем (до {Battle.R.Duel.RangeM:0} м), Esc — отмена");
        }
        public void Challenge(Mover target)
        {
            DuelMode = false;
            if (Selected == null || target == null) return;
            var why = Battle.Challenge(Selected, target);
            Say(why == null ? $"«{CommanderOf(Selected)?.Name}» вызывает «{CommanderOf(target)?.Name}» — ответ до «Ход!»" : "Поединок: " + why);
            Changed?.Invoke();
        }
        public void AnswerDuel(Mover b, bool accept)
        {
            if (Phase != PlayPhase.Orders || !Battle.Answer(b, accept)) return;
            Say(accept ? "Вызов принят — поединок со следующего хода" : "Отказ: войску этой стороны −БД");
            RefreshPreviews(); Changed?.Invoke();
        }
        public DuelRec ActiveDuel()
        {
            var rec = viewer?.Rec; if (rec == null) return null;
            float t = (float)ViewT;
            foreach (var d in rec.Duels) if (t >= d.T0 && (float.IsNaN(d.EndT) || t <= d.EndT + 2)) return d;
            return null;
        }
        public void LookAtDuel(DuelRec d) { if (d != null && viewer != null) viewer.LookAt(d.X, d.Y, 14); }

        // ── Г112: инструменты ГМа (Алекс 10.10.2026) — правки между ходами: БД, усталость, модификаторы БД по таблице этапа 3
        // (SPEC, Q5; числа — пока здесь, уйдут в Rules), всё с журналом и отменой. Численность, убрать и добавить отряд —
        // с API движка (SetSoldiers, Remove, Add на ходу) ──
        // таблица модификаторов БД — движка (Rules.MoraleMods, черновик до ГМа)
        public (string key, string name, double value)[] MoraleMods => Battle?.R.MoraleMods ?? Rules.Base.MoraleMods;
        public readonly List<string> GmLog = new List<string>();
        // отмена правок ГМа, по шагу назад: БД и усталость — поля отрядов (по номеру отряда), состав — снимок боя (Battle.Snapshot)
        readonly Stack<(string what, Action undo)> gmUndo = new Stack<(string, Action)>();
        public bool GmCanEdit => Phase == PlayPhase.Orders && Battle != null && !MidTurn;
        public int GmUndoCount => gmUndo.Count;
        void GmDone(string what, Action undo, IEnumerable<string> lines)
        {
            gmUndo.Push((what, undo));
            foreach (var l in lines) GmLog.Add($"ход {Session.Turn}: {l}");
            RefreshPreviews(); Changed?.Invoke();
        }
        Unit UnitById(int id) => Battle.Movers.FirstOrDefault(x => x.P.U.Id == id)?.P.U;
        // БД: ±правка ГМа — значение без бросков; модификатор по таблице — движком (Battle.ApplyMorale: при нуле — проверка на побег)
        void GmUnits(IEnumerable<Mover> ms, Action<List<Mover>, List<string>> change, string note)
        {
            if (!GmCanEdit) { Say("Правки ГМа — между ходами"); return; }
            var list = ms.Where(Present).ToList(); if (list.Count == 0) return;
            var saved = list.Select(m => (id: m.P.U.Id, was: m.P.U.Clone())).ToList();
            var extra = new List<string>(); change(list, extra);
            var lines = new List<string>();
            foreach (var (id, was) in saved)
            {
                var u = UnitById(id);
                lines.Add($"«{u.Name}» {note}: БД {was.Morale:0} → {u.Morale:0}" + (Math.Abs(was.Fatigue - u.Fatigue) > 0.5 ? $", усталость {was.Fatigue:0} → {u.Fatigue:0}" : ""));
            }
            lines.AddRange(extra);
            Say(saved.Count == 1 ? "ГМ: " + lines[0] : $"ГМ: {saved.Count} отр. — {note}");
            GmDone(note, () =>
            {
                foreach (var (id, was) in saved)
                {
                    var u = UnitById(id); if (u == null) continue;
                    u.Morale = was.Morale; u.Fatigue = was.Fatigue; u.Broken = was.Broken; u.BreakGrace = was.BreakGrace; u.BreakPenalty = was.BreakPenalty;
                }
            }, lines);
        }
        public void GmMorale(IEnumerable<Mover> ms, double delta) => GmUnits(ms, (L, x) =>
        {
            foreach (var m in L) m.P.U.Morale = Math.Max(0, Math.Min(Battle.R.Morale.Max, Math.Round(m.P.U.Morale + delta)));   // правка ГМа — без бросков
        }, $"БД {(delta >= 0 ? "+" : "")}{delta:0}");
        public void GmFatigue(IEnumerable<Mover> ms, double delta) => GmUnits(ms, (L, x) =>
        {
            foreach (var m in L) m.P.U.Fatigue = Math.Max(0, Math.Min(100, Math.Round(m.P.U.Fatigue + delta)));
        }, $"усталость {(delta >= 0 ? "+" : "")}{delta:0}");
        // модификатор БД по таблице — движком (Battle.ApplyMorale: строка в журнал хода), слом — по правилам стола
        public void GmMod(IEnumerable<Mover> ms, int mod)
        {
            // движком (Battle.ApplyMorale): упал до нуля — проверка на побег, отряд может побежать; отмена — снимком боя
            var (key, name, v) = MoraleMods[mod];
            var list = ms.Where(Present).ToList(); if (list.Count == 0) return;
            var lines = new List<string>(); string what = $"{name} {(v >= 0 ? "+" : "")}{v:0}" + (list.Count > 1 ? $" — {list.Count} отр." : $" — «{list[0].P.U.Name}»");
            GmRoster(what, () =>
            {
                var was = list.Select(m => (m.P.U.Name, m.P.U.Morale)).ToList();
                Battle.ApplyMorale(list, key);
                for (int i = 0; i < list.Count; i++) lines.Add($"«{was[i].Name}» {name}: БД {was[i].Morale:0} → {list[i].P.U.Morale:0}" + (list[i].Fleeing ? " — бежит!" : ""));
                return true;
            }, lines);
        }
        public IEnumerable<Mover> SideUnits(int side) => Battle.Movers.Where(m => SideOf(m) == side);
        public void GmUndo()
        {
            if (!GmCanEdit || gmUndo.Count == 0) return;
            var (what, undo) = gmUndo.Pop(); undo();
            GmLog.Add($"ход {Session.Turn}: отменено — {what}");
            Say($"Отменено: {what}"); RefreshPreviews(); Changed?.Invoke();
        }

        // ── состав (Г112 п.3, п.5): численность, убрать, добавить — движком (SetSoldiers, Remove, Add), в любой ход между ходами;
        // отмена — снимком боя ──
        public string GmRosterWhy => GmCanEdit ? null : "между ходами";
        // всё, что игра держит об отрядах по ссылке на Mover (облик, численность на начало, итог), — по номеру отряда
        sealed class GameRefs { public Dictionary<int, (string tpl, string style, string color, double men, (double, double, double) at)> U = new Dictionary<int, (string, string, string, double, (double, double, double))>(); public List<int> Sel = new List<int>(); }
        GameRefs SaveRefs()
        {
            var g = new GameRefs();
            foreach (var m in Battle.Movers)
                g.U[m.P.U.Id] = (Game.Tpl.TryGetValue(m, out var t) ? t : null, Game.Style.TryGetValue(m, out var st) ? st : null, Game.Color.TryGetValue(m, out var c) ? c : null,
                                 Game.StartMen.TryGetValue(m, out var n) ? n : m.P.U.Soldiers, AtStart.TryGetValue(m, out var a) ? a : (m.P.U.Soldiers, 0, 0));
            g.Sel.AddRange(Selection.Select(m => m.P.U.Id));
            return g;
        }
        // после Battle.Restore отряды — новые объекты: словари игры, выбор и запись — к ним
        void LoadRefs(GameRefs g)
        {
            Game.Tpl.Clear(); Game.Style.Clear(); Game.Color.Clear(); Game.StartMen.Clear(); AtStart.Clear();
            foreach (var m in Battle.Movers)
            {
                if (!g.U.TryGetValue(m.P.U.Id, out var v)) continue;
                if (v.tpl != null) Game.Tpl[m] = v.tpl; if (v.style != null) Game.Style[m] = v.style; if (v.color != null) Game.Color[m] = v.color;
                Game.StartMen[m] = v.men; AtStart[m] = v.at;
            }
            Selection.Clear(); foreach (var id in g.Sel) { var m = Battle.Movers.FirstOrDefault(x => x.P.U.Id == id); if (m != null && Present(m)) Selection.Add(m); }
            Hover = null; UiHover = null; previews.Clear(); previewQueue.Clear(); Session.Pending.Clear();
            recorder.Rebind(Battle.Movers);
            AfterSelect();
        }
        // правка состава: снимок до неё — для отмены
        void GmRoster(string what, Func<bool> act, IEnumerable<string> lines)
        {
            if (!GmCanEdit) { Say("Правки ГМа — между ходами"); return; }
            var snap = Battle.Snapshot(); var refs = SaveRefs(); bool pre = BeforeFirstTurn;
            if (!act()) return;
            AfterRoster(pre);
            GmDone(what, () => { Battle.Restore(snap); LoadRefs(refs); AfterRoster(pre); }, lines);
            Say("ГМ: " + what);
        }
        // запись — к составу: до первого хода — заново; потом — последний кадр заново (время записи не двигается)
        void AfterRoster(bool pre)
        {
            if (pre) { Rerecord(); if (viewer != null) viewer.SetLive(recorder.Rec, fit: false); }
            else recorder.ResnapLast();
            RefreshPreviews(); Changed?.Invoke();
        }
        public void GmSoldiers(Mover m, double delta)
        {
            if (m == null) return;
            var u = m.P.U; double was = u.Soldiers, now = Math.Max(1, Math.Round(was + delta));
            if (now == was) return;
            GmRoster($"«{u.Name}» бойцов {was:0} → {now:0}", () =>
            {
                if (!Battle.SetSoldiers(m, now)) return false;
                Game.StartMen[m] = Math.Max(Game.StartMen.TryGetValue(m, out var s0) ? s0 : 0, now);
                return true;
            }, new[] { $"«{u.Name}»: бойцов {was:0} → {now:0}" });
        }
        public void GmRemove(Mover m)
        {
            if (m == null) return;
            string name = m.P.U.Name;
            GmRoster($"«{name}» убран с поля", () => { if (!Battle.Remove(m)) return false; Selection.Remove(m); AfterSelect(); return true; }, new[] { $"«{name}» убран с поля" });
        }
        // добавить: шаблон, сторона, численность, имя — потом щелчок по карте
        public (string tpl, int side, double men, string name)? GmPlacing { get; private set; }
        public void GmStartAdd(string tpl, int side, double men, string name)
        {
            if (!GmCanEdit) { Say("Правки ГМа — между ходами"); return; }
            GmPlacing = (tpl, side, Math.Max(1, Math.Round(men)), string.IsNullOrWhiteSpace(name) ? Templates.Get(tpl)?.Name ?? tpl : name.Trim());
            Say("Щёлкни по карте, где поставить отряд (Esc — отмена)");
        }
        void GmPlace(Vector2 at)
        {
            var (tpl, side, men, name) = GmPlacing.Value; GmPlacing = null;
            var t = Templates.Get(tpl); if (t == null) return;
            bool pre = BeforeFirstTurn;
            GmRoster($"добавлен «{name}» ({men:0}) за {Session.Name(side)}", () =>
            {
                int id = Battle.Movers.Count == 0 ? 1 : Battle.Movers.Max(x => x.P.U.Id) + 1;
                var u = t.Make(id, name, men, side);
                var foes = Battle.Movers.Where(x => SideOf(x) != side && Present(x)).ToList();   // лицом к середине чужих
                double facing = foes.Count == 0 ? (side == 1 ? 0 : 180) : MoveSim.HeadingOf(foes.Average(x => x.P.X) - at.x, foes.Average(x => x.P.Y) - at.y);
                var m = Battle.Add(u, at.x, at.y, facing);
                Battle.Order(m, new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = m.P.Facing });
                Game.Tpl[m] = tpl; Game.StartMen[m] = u.Soldiers; AtStart[m] = (u.Soldiers, 0, 0);
                if (!pre) { recorder.AddUnit(m, tpl, null, null); if (viewer != null) viewer.SetLive(recorder.Rec, fit: false); }   // подкрепление: в запись
                Selection.Clear(); Selection.Add(m); AfterSelect();
                return true;
            }, new[] { $"добавлен «{name}» ({men:0}) за {Session.Name(side)}" });
        }

        // ── заставка главного меню (Алекс 10.10.2026): пока битва не выбрана — учебный бой идёт сам, обе стороны под ИИ (Г113);
        // камера сама: от схватки к схватке, раз в несколько секунд — общий план; после 8 ходов — новый бой с другим зерном ──
        public bool DemoHold { get; set; }   // открыт редактор карт или армий — заставка стоит и камеру не трогает
        bool demoHeld;
        object demoGame; uint demoSeed = 2026; float shotUntil, shotSize = 60; int shotA = -1, shotB = -1; Vector2 camAt; float camSize = 120;
        void Demo()
        {
            if (Game == null || viewer == null) return;
            if (demoGame != Game)
            {
                demoGame = Game; shotUntil = 0;
                var s1 = SideUnits(1).ToList(); var s2 = SideUnits(2).ToList();
                if (s1.Count > 0 && s2.Count > 0)
                {
                    var r = new System.Random((int)demoSeed);
                    // кто обходит, кто наступает — по зерну: бои на заставке разные
                    int flank = r.Next(2) + 1, adv = 3 - flank;
                    var foes = flank == 1 ? s2 : s1; var mine = flank == 1 ? s1 : s2;
                    Battle.AssignSide(flank, AiKind.Flank, foes[r.Next(foes.Count)].P.U.Id);
                    Battle.AssignSide(adv, r.Next(2) == 0 ? AiKind.Advance : AiKind.Cover, r.Next(2) == 0 ? mine[r.Next(mine.Count)].P.U.Id : 0);
                    if (Battle.Assigned.Count(m => SideOf(m) == adv) == 0) Battle.AssignSide(adv, AiKind.Advance, mine[0].P.U.Id);
                }
                var c0 = Centre(Battle.Movers.Where(Present)); camAt = c0; camSize = 150;
            }
            if (Phase == PlayPhase.Orders && Session.Turn <= 8) { Speed = 1; Go(); }
            else if (Phase == PlayPhase.Over || Phase == PlayPhase.Orders)
            {
                demoSeed++; uint sd = demoSeed; lastMake = () => PlayScenarios.Training(sd); NewBattle();
                return;
            }
            // камера: схватка — ближе; нет схваток — общий план над серединой войск
            if (Time.unscaledTime > shotUntil) PickShot();
            Vector2 aim; float size;
            var ms = Battle.Movers;
            if (shotA >= 0 && shotA < ms.Count && shotB < ms.Count && Present(ms[shotA]))
            {
                BoxOf(ms[shotA], out var ax, out var ay, out _, out _, out _);
                if (shotB >= 0 && Present(ms[shotB])) { BoxOf(ms[shotB], out var bx, out var by, out _, out _, out _); aim = new Vector2((ax + bx) / 2, (ay + by) / 2); }
                else aim = new Vector2(ax, ay);
                size = shotSize;
            }
            else { aim = Centre(ms.Where(Present)); size = 170; }
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime / 2.2f);   // плывёт, а не прыгает
            camAt += (aim - camAt) * k; camSize += (size - camSize) * k;
            // кадр — правее середины экрана: слева меню
            float aspect = viewer.Cam != null ? viewer.Cam.aspect : 16f / 9;
            viewer.LookAt(camAt.x - camSize * aspect * 0.28f, camAt.y, camSize);
        }
        Vector2 Centre(IEnumerable<Mover> ms)
        {
            float x = 0, y = 0; int n = 0;
            foreach (var m in ms) { BoxOf(m, out var mx, out var my, out _, out _, out _); x += mx; y += my; n++; }
            return n == 0 ? camAt : new Vector2(x / n, y / n);
        }
        void PickShot()
        {
            var rec = viewer.Rec; var r = new System.Random((int)(Time.unscaledTime * 1000));
            shotUntil = Time.unscaledTime + 7 + (float)r.NextDouble() * 4;
            shotA = shotB = -1;
            if (rec != null && rec.Fights.Count > 0 && r.Next(4) > 0)
            {
                var f = rec.Fights[Mathf.Clamp((int)(viewer.T / rec.Dt), 0, rec.Fights.Count - 1)];
                if (f.Length >= 2) { int q = 2 * r.Next(f.Length / 2); shotA = f[q]; shotB = f[q + 1]; shotSize = 26 + (float)r.NextDouble() * 18; return; }
            }
            // нет схватки — за отрядом на марше (конница — чаще: она красивее идёт), иначе — за любым
            var live = Battle.Movers.Select((m, i) => (m, i)).Where(p => Present(p.m) && p.m.Order != null && p.m.Order.Kind != OrderKind.Hold).ToList();
            var cav = live.Where(p => p.m.P.U.Type == "cavalry").ToList();
            var pick = cav.Count > 0 && r.Next(2) == 0 ? cav : live.Count > 0 ? live : Battle.Movers.Select((m, i) => (m, i)).Where(p => Present(p.m)).ToList();
            if (pick.Count > 0) { shotA = pick[r.Next(pick.Count)].i; shotSize = 32 + (float)r.NextDouble() * 22; }
        }

        // ── Г113: поручения ИИ (вид ГМа, между ходами) — отряду, группе или всей стороне: держать, наступать, прикрыть, фланговый
        // манёвр. Приказы по ним раздаёт движок в «Ход!» (BattleSession.Go → Battle.AiOrders), глазами стороны (туман);
        // отряд, которому на этот ход отдан приказ, ИИ не трогает. Цель — щелчком после кнопки; отмена — как у прочих правок ГМа ──
        public static readonly (AiKind kind, string name, string tip)[] AiKinds =
        {
            (AiKind.Hold, "Держать", "Стоять, где стоит, лицом к угрозе; отнесло — вернуться. Бегущих — сплотить"),
            (AiKind.Advance, "Наступать", "Идти на врага (щелчок по отряду) или к точке (щелчок по земле); враг рядом — атаковать, конница — с натиском"),
            (AiKind.Cover, "Прикрыть", "Встать перед своим отрядом (щелчок по нему) со стороны врага и перехватывать тех, кто к нему идёт. Всей стороне — рубящие прикрывают ближайших стрелков, стрелки держат"),
            (AiKind.Flank, "Обход", "Фланговый манёвр на врага (щелчок по отряду): зайти сбоку и ударить. Всей стороне — конница в обход, остальные наступают на ту же цель"),
        };
        public (AiKind kind, List<int> ids, int side)? AiPicking { get; private set; }
        public AiTask AiTaskOf(Mover m) => m == null ? null : Battle?.TaskOf(m);
        public string AiText(AiTask t)
        {
            if (t == null) return "";
            string Of(int id) { var u = Battle.ById(id); return u == null ? "?" : $"«{u.P.U.Name}»"; }
            return t.Kind switch
            {
                AiKind.Hold => "держать позицию",
                AiKind.Advance => t.TargetId != 0 ? $"наступать на {Of(t.TargetId)}" : "наступать к точке",
                AiKind.Cover => $"прикрыть {Of(t.TargetId)}",
                _ => $"обход {Of(t.TargetId)}",
            };
        }
        public void AiStart(int kindIndex, IEnumerable<Mover> ms, int side)
        {
            if (!GmCanEdit) { Say("Поручения ИИ — между ходами"); return; }
            var kind = AiKinds[Mathf.Clamp(kindIndex, 0, AiKinds.Length - 1)].kind;
            var ids = (ms ?? Enumerable.Empty<Mover>()).Where(Present).Select(m => m.P.U.Id).ToList();
            if (side == 0 && ids.Count == 0) return;
            if (kind == AiKind.Hold || kind == AiKind.Cover && side > 0) { AiApply(kind, ids, side, 0, double.NaN, double.NaN); return; }
            AiPicking = (kind, ids, side); GmPlacing = null;
            Say(kind == AiKind.Cover ? "Щёлкни по своему отряду, которого прикрывать (Esc — отмена)"
              : kind == AiKind.Advance ? "Щёлкни по врагу или по земле — куда наступать (Esc — отмена)"
              : "Щёлкни по врагу, которого обойти с фланга (Esc — отмена)");
        }
        void AiPick(Mover t, Vector2 at, bool ui)
        {
            var (kind, ids, side) = AiPicking.Value; AiPicking = null;
            if (t == null && kind == AiKind.Advance && !ui) AiApply(kind, ids, side, 0, at.x, at.y);
            else if (t == null) Say("Нужен отряд — поручение не дано");
            else AiApply(kind, ids, side, t.P.U.Id, double.NaN, double.NaN);
        }
        static AiTask CopyTask(AiTask t) => t == null ? null : new AiTask { Kind = t.Kind, TargetId = t.TargetId, X = t.X, Y = t.Y, Facing = t.Facing, Side = t.Side, Engaging = t.Engaging, Why = t.Why };
        void AiApply(AiKind kind, List<int> ids, int side, int target, double x, double y)
        {
            var scope = (side > 0 ? SideUnits(side) : ids.Select(id => Battle.Movers.FirstOrDefault(m => m.P.U.Id == id))).Where(m => m != null).ToList();
            var was = scope.Select(m => (id: m.P.U.Id, t: CopyTask(Battle.TaskOf(m)))).ToList();
            int n = 0;
            if (side > 0) n = Battle.AssignSide(side, kind, target, x, y);
            else foreach (var m in scope) if (Battle.Assign(m, kind, target, x, y) != null) n++;
            string name = AiKinds.First(k => k.kind == kind).name.ToLowerInvariant();
            string whom = side > 0 ? $"вся сторона «{Session.Name(side)}»" : scope.Count == 1 ? $"«{scope[0].P.U.Name}»" : $"{scope.Count} отр.";
            string tgt = target != 0 ? $" → «{Battle.ById(target)?.P.U.Name}»" : !double.IsNaN(x) ? " → к точке" : "";
            if (n == 0) { Say($"ИИ: поручение «{name}» не дано — {(kind == AiKind.Cover ? "прикрывать можно только своих" : kind == AiKind.Flank ? "обходить можно только врага" : "нет отрядов на поле")}"); return; }
            string what = $"ИИ: {whom} — {name}{tgt}" + (n < scope.Count ? $" ({n} из {scope.Count})" : "");
            GmDone(what, () => AiRestore(was), new[] { what });
            Say(what + " — приказы раздаст по «Ход!»");
        }
        void AiRestore(List<(int id, AiTask t)> was)
        {
            foreach (var (id, t) in was)
            {
                var m = Battle.Movers.FirstOrDefault(x => x.P.U.Id == id); if (m == null) continue;
                Battle.Unassign(m);
                if (t == null) continue;
                var nt = Battle.Assign(m, t.Kind, t.TargetId, t.X, t.Y, t.Facing);
                if (nt != null) { nt.Side = t.Side; nt.Engaging = t.Engaging; nt.Why = t.Why; }
            }
        }
        public void AiClear(IEnumerable<Mover> ms, int side)
        {
            if (!GmCanEdit) { Say("Поручения ИИ — между ходами"); return; }
            var scope = (side > 0 ? SideUnits(side) : ms).Where(m => Battle.TaskOf(m) != null).ToList();
            if (scope.Count == 0) { Say("Поручений ИИ нет"); return; }
            var was = scope.Select(m => (id: m.P.U.Id, t: CopyTask(Battle.TaskOf(m)))).ToList();
            foreach (var m in scope) Battle.Unassign(m);
            string what = $"ИИ: снято поручение — " + (side > 0 ? $"вся сторона «{Session.Name(side)}»" : scope.Count == 1 ? $"«{scope[0].P.U.Name}»" : $"{scope.Count} отр.");
            GmDone(what, () => AiRestore(was), new[] { what });
            Say(what);
        }

        // ── Г116б: подсказки адъютанта к приказу — движком (Battle.Advise), глазами стороны отряда. Пока тянешь ПКМ — живые
        // (у главного отряда, в панели отряда); отдал приказ — всплывают один раз: та же фраза тому же отряду за ход не повторяется.
        // Настройка: все / только важные / выключены ──
        public List<string> DragAdvice { get; private set; } = new List<string>();
        float adviseAt;
        public readonly List<(Mover m, string text)> Advice = new List<(Mover, string)>();   // всплывшие по последним приказам
        public float AdviceUntil;
        readonly List<(Mover m, MoveOrder o)> adviceNew = new List<(Mover, MoveOrder)>();
        readonly HashSet<string> adviceSeen = new HashSet<string>();
        static readonly string[] ImportantAdvice = { "обходят", "без прикрытия", "дрогнет", "сильнее числом", "врагов больше", "проигрывает", "натиск не успеет", "не достаёт" };
        public static bool IsImportant(string s) => ImportantAdvice.Any(s.Contains);
        public static List<string> AdviceFilter(List<string> L) =>
            GameSettings.Adjutant == 2 || L == null ? new List<string>() : GameSettings.Adjutant == 1 ? L.Where(IsImportant).ToList() : L;
        void FlushAdvice()
        {
            if (adviceNew.Count == 0) return;
            var batch = adviceNew.ToList(); adviceNew.Clear();
            if (GameSettings.Adjutant == 2) return;
            var fresh = new List<(Mover, string)>();
            foreach (var (m, o) in batch)
            {
                if (o == null || !Present(m)) continue;
                foreach (var h in AdviceFilter(Battle.Advise(m, o)))
                    if (adviceSeen.Add($"{Session.Turn}|{m.P.U.Id}|{h}")) fresh.Add((m, h));
            }
            if (fresh.Count == 0) return;
            Advice.Clear(); Advice.AddRange(fresh.Take(5));
            AdviceUntil = Time.time + 8; Changed?.Invoke();
        }

        // ── Г116в: разбор после хода — движком (Battle.Review), для каждой стороны её глазами; показ — в журнале хода ──
        public readonly Dictionary<int, List<string>> Reviews = new Dictionary<int, List<string>>();
        public int ReviewTurn { get; private set; }
        void MakeReviews()
        {
            Reviews.Clear();
            foreach (var sd in Session.Sides) Reviews[sd] = Battle.Review(sd);
            ReviewTurn = Session.Turn;
        }
        // отряд по первому «имени» в строке разбора (щелчок по строке — камера к нему)
        public Mover UnitNamed(string line, int side)
        {
            int a = line.IndexOf('«'), b = a < 0 ? -1 : line.IndexOf('»', a + 1);
            if (b < 0) return null;
            string n = line.Substring(a + 1, b - a - 1);
            return Battle.Movers.Where(m => m.P.U.Name == n && Present(m)).OrderBy(m => SideOf(m) == side ? 0 : 1).FirstOrDefault();
        }

        // ── Г112 п.1: пауза посреди хода и приказы, что действуют сразу (вид ГМа). Счёт идёт впереди показа — поэтому бой
        // возвращается снимком к началу хода и тихо досчитывается до кадра паузы (бой детерминирован: тот же, что показан, —
        // с теми же приказами, в том числе прежними приказами посреди этого хода); запись обрезается по этот кадр. Приказы —
        // сразу отрядам; «Ход!» — досчитать ход с этого мига. Правки ГМа посреди хода — нельзя (их не повторить при пересчёте) ──
        public bool MidTurn { get; private set; }
        int midStep;
        public bool CanMidOrders => Phase == PlayPhase.Showing && Paused && ViewSide == 0 && turnSnaps.Count > 0 && !MidTurn && Battle != null;
        void ApplyNow(List<(int id, MoveOrder o)> ords)
        {
            foreach (var (id, o0) in ords)
            {
                var m = Battle.Movers.FirstOrDefault(x => x.P.U.Id == id); if (m == null || !Present(m)) continue;
                var o = CopyOrder(o0);
                if (o.Kind == OrderKind.Retreat && (double.IsNaN(o.X) || double.IsNaN(o.Y))) (o.X, o.Y) = Battle.RetreatPoint(m);
                Battle.Order(m, o);
            }
        }
        public void EnterMidOrders()
        {
            if (!CanMidOrders) return;
            var ts = turnSnaps[turnSnaps.Count - 1]; var rec = recorder.Rec;
            int f = Mathf.Clamp((int)Math.Floor(ShowTime / rec.Dt + 1e-6), ts.Frame, rec.Frames.Count - 1), steps = (f - ts.Frame) * 4;
            StopCompute();
            float t0 = Time.realtimeSinceStartup;
            Battle.Restore(ts.Snap); LoadRefs(ts.Refs);
            Session.Turn = ts.Turn; Session.Phase = BattleCore.Phase.Orders;
            foreach (var (id, o) in ts.Orders) { var m = Battle.Movers.FirstOrDefault(x => x.P.U.Id == id); if (m != null) Session.Pending[m] = CopyOrder(o); }
            Session.Go();
            for (int k = 0; k < steps; k++)
            {
                foreach (var (st, ords) in ts.Mid) if (st == k) ApplyNow(ords);
                if (!Session.Step()) break;
            }
            recorder.Rewind(f); recorder.Rebind(Battle.Movers);
            stepInTurn = steps; midStep = steps;
            ShowTime = f * rec.Dt; if (viewer != null) viewer.T = ShowTime;
            Session.Phase = BattleCore.Phase.Orders;   // приказы — через сессию, как между ходами
            MidTurn = true; Phase = PlayPhase.Orders; Paused = false;
            RefreshPreviews();
            Say($"Пауза на {ShowTime - TurnStartTime:0.0} с хода: приказы подействуют с этого мига — «Ход!», чтобы продолжить (пересчёт {Time.realtimeSinceStartup - t0:0.0} с)");
            Changed?.Invoke();
        }
        // «Ход!» посреди хода: приказы — сразу отрядам (запомнены для будущих пересчётов этого хода), счёт — дальше с этого шага
        void ResumeMid()
        {
            var ts = turnSnaps[turnSnaps.Count - 1];
            var ords = PendingCopy();
            if (ords.Count > 0) ts.Mid.Add((midStep, ords));
            ApplyNow(ords);
            Session.Pending.Clear(); Session.Phase = BattleCore.Phase.Playing;
            MidTurn = false; Phase = PlayPhase.Showing; Paused = false;
            previews.Clear(); previewQueue.Clear();
            StartCompute();
            Changed?.Invoke();
        }

        // ── Г112 п.2: откат хода — снимок боя перед каждым «Ход!» (последние 5); откат возвращает бой, запись, журнал, сводки
        // и время показа к началу прошлого хода; приказы того хода снимаются — отдать заново ──
        // Orders — приказы, ушедшие по «Ход!»; Mid — приказы ГМа посреди хода: (шаг хода, приказы) — для пересчёта до паузы
        sealed class TurnSnap
        {
            public object Snap; public GameRefs Refs; public int Turn, Frame, Summaries, Logs; public double Show;
            public List<(int id, MoveOrder o)> Orders = new List<(int, MoveOrder)>();
            public List<(int step, List<(int id, MoveOrder o)> orders)> Mid = new List<(int, List<(int, MoveOrder)>)>();
        }
        static MoveOrder CopyOrder(MoveOrder o) => (MoveOrder)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(o, null);
        List<(int id, MoveOrder o)> PendingCopy() => Session.Pending.Select(kv => (kv.Key.P.U.Id, CopyOrder(kv.Value))).ToList();
        readonly List<TurnSnap> turnSnaps = new List<TurnSnap>();
        public int TurnSnapCount => turnSnaps.Count;
        void SaveTurn()
        {
            turnSnaps.Add(new TurnSnap { Snap = Battle.Snapshot(), Refs = SaveRefs(), Turn = Session.Turn, Frame = recorder.Rec.Frames.Count - 1, Summaries = Summaries.Count, Logs = Session.Logs.Count, Show = ShowTime, Orders = PendingCopy() });
            if (turnSnaps.Count > 5) turnSnaps.RemoveAt(0);
        }
        public void RollbackTurn()
        {
            if ((Phase != PlayPhase.Orders && Phase != PlayPhase.Over) || Battle == null || turnSnaps.Count == 0) return;
            var ts = turnSnaps[turnSnaps.Count - 1]; turnSnaps.RemoveAt(turnSnaps.Count - 1);
            CancelDrag();
            Battle.Restore(ts.Snap);
            Session.Turn = ts.Turn; Session.Phase = BattleCore.Phase.Orders; Session.Outcome = null; Session.Winner = -1;
            while (Session.Logs.Count > ts.Logs) Session.Logs.RemoveAt(Session.Logs.Count - 1);
            while (Summaries.Count > ts.Summaries) Summaries.RemoveAt(Summaries.Count - 1);
            current = null; EndedByPlayer = false; MidTurn = false; Reviews.Clear(); ReviewTurn = 0;
            recorder.Rewind(ts.Frame);
            LoadRefs(ts.Refs);
            ShowTime = TurnStartTime = ts.Show; Phase = PlayPhase.Orders;
            gmUndo.Clear();   // правки до отката — к другому бою
            GmLog.Add($"откат к началу хода {ts.Turn}");
            if (viewer != null) viewer.T = ShowTime;
            RefreshPreviews();
            Say($"Ход {ts.Turn} откатан — отдайте приказы заново"); Changed?.Invoke();
        }
        public bool CardsAll { get; set; }   // ГМ: карточки обеих сторон разом — приказы за любую сторону без переключения

        // вернуть смотрелке эту битву (после редактора карт, который показывал свою карту)
        public void ReShow() { if (viewer != null && recorder != null) { viewer.SetLive(recorder.Rec); viewer.T = ShowTime; } }

        // ── ход ──
        public void Go()
        {
            if (Phase != PlayPhase.Orders) return;
            CancelDrag();
            if (MidTurn) { ResumeMid(); return; }
            if (Deploying) { EndDeployNow(); return; }   // «К бою»
            SaveTurn();   // снимок для отката хода (Г112 п.2)
            current = TurnSummary.Begin(Battle, Session.Turn);
            Session.Go();
            Phase = PlayPhase.Showing; Paused = false;
            TurnStartTime = ShowTime; stepInTurn = 0; showRate = GameSettings.ComputeFirst ? 1 : 0.3f;
            StartCompute();
            previews.Clear(); previewQueue.Clear();
            Changed?.Invoke();
        }

        // закончить битву между ходами и показать итог (битва не обязана дойти до бегства одной из сторон)
        public void EndBattle()
        {
            if (Phase != PlayPhase.Orders || Game == null) return;
            CancelDrag(); EndedByPlayer = true; Phase = PlayPhase.Over; recorder.Rec.Done = true;
            Selection.Clear(); Selected = null; previews.Clear(); previewQueue.Clear();
            Changed?.Invoke();
        }

        void OnDestroy() => StopCompute();
        void Update()
        {
            var lk = recorder?.Rec;
            if (lk == null) UpdateBody(); else lock (lk) UpdateBody();
        }
        void UpdateBody()
        {
            if (Game == null) return;
            if (!Chosen) { if (DemoHold) Paused = true; else { if (Phase == PlayPhase.Showing && demoHeld) Paused = false; Demo(); } demoHeld = DemoHold; }
            if (Phase == PlayPhase.Showing) AdvanceTurn();
            else if (Phase == PlayPhase.Orders) { DrainPreviews(); FlushAdvice(); }
            HandleInput();
            if (viewer != null) viewer.T = ShowTime;
        }

        // Счёт идёт впереди показа на ~0,6 с, не дольше 12 мс за кадр; показ не обгоняет досчитанное (Г43)
        // ── Г111 п.7: ход считается в отдельном потоке (Session.Step до конца хода), показ идёт следом. Запас счёта меньше
        // секунды — показ плавно замедляется, а не прыгает; «досчитать ход, потом показать» — показ ждёт конца счёта.
        // Кадры записи пишутся под lock(запись), смотрелка и панели читают её под тем же замком; к самому бою во время
        // счёта экран почти не обращается (StepLock — для редких чтений: схватки, туман) ──
        public readonly object StepLock = new object();
        Task computeTask; volatile bool computeDone = true, computeCancel; Exception computeError;
        public bool Computing => !computeDone;
        public double ComputeProgress { get { double ts = Battle.R.Move.TurnSec; return ts <= 0 ? 1 : Math.Max(0, Math.Min(1, (ComputedTime - TurnStartTime) / ts)); } }
        float showRate = 1;
        void StartCompute()
        {
            computeDone = false; computeCancel = false; computeError = null;
            var sess = Session; var rcd = recorder; var rec = rcd.Rec;
            computeTask = Task.Run(() =>
            {
                try
                {
                    while (!computeCancel)
                    {
                        bool more;
                        lock (StepLock) more = sess.Step(_ => { if (++stepInTurn % 4 == 0) lock (rec) rcd.Snap(); });
                        if (!more) break;
                    }
                }
                catch (Exception e) { computeError = e; }
                finally { computeDone = true; }
            });
        }
        public void StopCompute() { computeCancel = true; try { computeTask?.Wait(2000); } catch { } }

        void AdvanceTurn()
        {
            double turnEnd = TurnStartTime + Battle.R.Move.TurnSec;
            if (computeError != null) { Debug.LogException(computeError); Say("Ошибка в счёте хода — подробности в журнале Unity"); computeError = null; }
            bool done = computeDone;
            double avail = done ? turnEnd : Math.Min(turnEnd, ComputedTime);
            if (!Paused && (done || !GameSettings.ComputeFirst))
            {
                // запас счёта ≥ 1 с — полная скорость, меньше — медленнее, у края — стоп; скорость показа меняется плавно
                float want = done ? 1 : Mathf.Clamp01((float)((avail - ShowTime) / 1.0));
                showRate = Mathf.MoveTowards(showRate, want, Time.deltaTime * 2.5f);
                ShowTime = Math.Min(ShowTime + Time.deltaTime * Speed * Math.Max(showRate, want < 0.05f ? 0 : 0.05f), avail);
            }
            if (done && ShowTime >= turnEnd - 1e-6)
            {
                ShowTime = turnEnd;
                if (current != null) { current.End(Battle); Summaries.Add(current); current = null; }
                if (Session.Phase == BattleCore.Phase.Over) { Phase = PlayPhase.Over; recorder.Rec.Done = true; }
                else Phase = PlayPhase.Orders;
                MakeReviews();
                Selection.RemoveAll(m => !Present(m)); AfterSelect();
                RefreshPreviews();
                Changed?.Invoke();
            }
        }

        // ── выбор ──
        public static bool Present(Mover m) => (m.P.U.Status == "active" || m.P.U.Status == "fled") && m.P.U.Soldiers > 0 && m.Figs.Count > 0 && !m.Gone;
        public static int SideOf(Mover m) => BattleSession.SideOf(m);
        public OrderPreview PreviewOf(Mover m) => previews.TryGetValue(m, out var p) ? p : null;

        public void Select(Mover m)
        {
            Selection.Clear();
            if (m != null && Present(m)) Selection.Add(m);
            AfterSelect();
        }
        // Ctrl+щелчок: добавить или убрать; отряд другой стороны начинает новый выбор (группа — только своих)
        public void Toggle(Mover m)
        {
            if (m == null || !Present(m)) return;
            if (Selection.Count > 0 && SideOf(Selection[0]) != SideOf(m)) Selection.Clear();
            if (!Selection.Remove(m)) Selection.Add(m);
            AfterSelect();
        }
        public void SelectMany(IEnumerable<Mover> ms)
        {
            Selection.Clear();
            foreach (var m in ms) if (Present(m) && !Selection.Contains(m)) Selection.Add(m);
            AfterSelect();
        }
        void AfterSelect()
        {
            Selected = Selection.Count > 0 ? Selection[Selection.Count - 1] : null;
            if (Selected != null) ActiveSide = SideOf(Selected);
            ChargeMode = false;
            Changed?.Invoke();
        }

        // ── приказы ──
        public bool Order(Mover m, MoveOrder o, bool quiet = false)
        {
            if (Phase != PlayPhase.Orders) { Say("Приказы — между ходами"); return false; }
            string why = Session.SetOrder(m, o);
            if (why != null) { if (!quiet) Say(Selection.Count > 1 ? $"«{m.P.U.Name}»: {why}" : why); return false; }
            QueuePreview(m);
            if (!quiet && adviceNew.Count < 8) adviceNew.Add((m, o));
            Changed?.Invoke();
            return true;
        }
        // кнопки и клавиши — всем выбранным; кто не может (бегущий не держит строй) — пропускается с подсказкой
        public void Hold() => ForGroup(m => new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = m.P.Facing });
        public void Retreat() => ForGroup(m => new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN });
        public void Rally() => ForGroup(m => new MoveOrder { Kind = OrderKind.Rally });
        public void Cancel()
        {
            bool any = false;
            foreach (var m in Selection) if (Session.Pending.Remove(m)) { QueuePreview(m); any = true; }
            if (any) Changed?.Invoke();
        }
        void ForGroup(Func<Mover, MoveOrder> make)
        {
            int ok = 0; string last = null;
            foreach (var m in Selection.ToList())
            {
                var why = Phase == PlayPhase.Orders ? Session.SetOrder(m, make(m)) : "приказы — между ходами";
                if (why == null) { QueuePreview(m); ok++; if (adviceNew.Count < 8) adviceNew.Add((m, Session.OrderOf(m))); } else last = $"«{m.P.U.Name}»: {why}";
            }
            if (last != null) Say(Selection.Count > 1 && ok > 0 ? $"{last} (остальные — приняли)" : last);
            Changed?.Invoke();
        }

        // конец хода — подсказки всем заново: сначала своей стороне, остальным — следом
        void RefreshPreviews()
        {
            previews.Clear(); previewQueue.Clear();
            foreach (var m in Battle.Movers.OrderBy(m => SideOf(m) == ActiveSide ? 0 : 1)) previewQueue.Add(m);
        }
        // новый приказ: старая подсказка неверна — убрать; главному отряду — сразу, прочим — в очередь
        void QueuePreview(Mover m)
        {
            previews.Remove(m); previewQueue.Remove(m);
            if (m == Selected) RefreshPreview(m); else previewQueue.Insert(0, m);
        }
        void DrainPreviews()
        {
            if (previewQueue.Count == 0) return;
            float until = Time.realtimeSinceStartup + 0.006f;
            while (previewQueue.Count > 0 && Time.realtimeSinceStartup < until)
            {
                var m = previewQueue[0]; previewQueue.RemoveAt(0);
                RefreshPreview(m);
            }
            Changed?.Invoke();
        }
        void RefreshPreview(Mover m)
        {
            previews.Remove(m);
            if (!Present(m)) return;
            var o = Session.OrderOf(m);
            if (o == null || o.Kind == OrderKind.Hold || m.Done && o.Kind != OrderKind.Attack) return;   // стоит — подсказывать нечего
            previews[m] = Battle.Preview(m, o);
        }

        public void Say(string s) { Toast = s; ToastUntil = Time.time + 2.5f; Changed?.Invoke(); }

        // ── где отряд и что под мышью ──
        public Vector2 MapPoint(Vector2 screen)
        {
            if (viewer != null) return viewer.ScreenToMap(screen);
            var w = Camera.main.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            return new Vector2(w.x, -w.y);
        }
        // где отряд виден сейчас (рамка вокруг его бойцов в записи на время показа); нет записи — где он в счёте
        public bool BoxOf(Mover m, out float x, out float y, out float facing, out float front, out float depth)
        {
            var rec = viewer?.Rec; int i = Battle.Movers.IndexOf(m);
            if (rec != null && i >= 0 && rec.UnitBox(i, viewer.T, out x, out y, out facing, out front, out depth)) return true;
            x = (float)m.P.X; y = (float)m.P.Y; facing = (float)m.P.Facing; front = (float)m.P.Fp.Front; depth = (float)m.P.Fp.Depth;
            return false;
        }
        // камера — на отряд (двойной щелчок по карточке или табличке)
        public void FocusOn(Mover m) { if (m != null && viewer != null) { BoxOf(m, out var x, out var y, out _, out _, out _); viewer.Focus(x, y); } }
        float PixelsPerMeter => viewer != null && viewer.Cam != null ? Screen.height / (2 * viewer.Cam.orthographicSize) : 1;

        // Отряд под точкой карты: внутри рамки, где он виден, или рядом — не дальше 1,5 м или 8 px (издали строй — полоска)
        // enemyOf > 0 — сперва враги этой стороны (ПКМ по свалке: «атаковать», а не «идти» к своему, что рубится с ним), нет — любой
        public Mover UnitAt(Vector2 p, int enemyOf = 0)
        {
            if (enemyOf > 0) { var e = UnitAtWhere(p, m => SideOf(m) != enemyOf); if (e != null) return e; }
            return UnitAtWhere(p, null);
        }
        Mover UnitAtWhere(Vector2 p, Func<Mover, bool> ok)
        {
            Mover best = null; double bd = double.MaxValue, tol = Math.Max(1.5, 8 / PixelsPerMeter);
            foreach (var m in Battle.Movers)
            {
                if (!Present(m) || !SeenNow(m) || ok != null && !ok(m)) continue;
                BoxOf(m, out var x, out var y, out var f, out var w, out var d);
                double h = f * Math.PI / 180, dx = p.x - x, dy = p.y - y;
                double lx = Math.Abs(dx * Math.Cos(h) + dy * Math.Sin(h)) - w / 2, ly = Math.Abs(dx * Math.Sin(h) - dy * Math.Cos(h)) - d / 2;
                double gap = Math.Max(lx, ly);
                if (gap < tol && gap < bd) { bd = gap; best = m; }
            }
            return best;
        }

        // ── ввод (Г80): ЛКМ — выбрать (Ctrl — добавить/убрать, рамкой по земле — несколько, Ctrl+A — все своей стороны);
        // ПКМ по земле — идти (группа — сохраняя расстановку), протянуть — встать фронтом вдоль линии; ПКМ по врагу —
        // атаковать, с Alt (или кнопкой «Натиск») — натиск; Д держать, О отступить, С сплотить, ⌫ отменить,
        // Enter — «Ход!», пробел — пауза, Tab — другая сторона, Esc — снять выбор ──
        // мышь — только при фокусе окна игры и внутри него (иначе нажатие в другом окне начинало приказ)
        static bool InScreen(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x < Screen.width && p.y < Screen.height;
        void HandleInput()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (!Application.isFocused || Blocked) { CancelDrag(); BoxSelecting = leftDown = false; return; }
            bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            if (kb != null)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Go();
                if (kb.spaceKey.wasPressedThisFrame && Phase == PlayPhase.Showing) Paused = !Paused;
                if (kb.escapeKey.wasPressedThisFrame && DuelMode) { DuelMode = false; Say("Вызов на поединок — отменён"); }
                else if (kb.escapeKey.wasPressedThisFrame && GmPlacing == null && AiPicking == null) { if (Dragging) CancelDrag(); else if (Selection.Count > 0) Select(null); else MenuRequested?.Invoke(); }
                if (ctrl && kb.aKey.wasPressedThisFrame) SelectMany(Battle.Movers.Where(m => SideOf(m) == ActiveSide));
                if (Selection.Count > 0 && Phase == PlayPhase.Orders && !ctrl)
                {
                    if (kb.lKey.wasPressedThisFrame) Hold();      // Д
                    if (kb.jKey.wasPressedThisFrame) Retreat();   // О
                    if (kb.cKey.wasPressedThisFrame) Rally();     // С
                    if (kb.backspaceKey.wasPressedThisFrame) Cancel();
                    if (kb.yKey.wasPressedThisFrame) WallSelected();   // Н — на стену
                    if (kb.gKey.wasPressedThisFrame) ToggleDuel();   // П — поединок (нельзя — скажет почему)
                    if (Deploying && !Dragging && DeployUnit == null) { if (kb.qKey.wasPressedThisFrame) DeployTurn(-15); if (kb.eKey.wasPressedThisFrame) DeployTurn(15); }
                }
                if (kb.tabKey.wasPressedThisFrame) { ActiveSide = Session.Sides.SkipWhile(s => s != ActiveSide).Skip(1).DefaultIfEmpty(Session.Sides.First()).First(); Select(null); if (ViewSide > 0) ViewSide = ActiveSide; }
                if (kb.vKey.wasPressedThisFrame) CycleView();   // М — чьими глазами
                if (kb.digit1Key.wasPressedThisFrame) Speed = 1;
                if (kb.digit2Key.wasPressedThisFrame) Speed = 2;
                if (kb.digit3Key.wasPressedThisFrame) Speed = 4;
            }
            if (mouse == null) return;
            Vector2 sp = mouse.position.ReadValue();
            if (!InScreen(sp)) { if (mouse.rightButton.wasReleasedThisFrame) CancelDrag(); if (mouse.leftButton.wasReleasedThisFrame) BoxSelecting = leftDown = false; return; }
            bool ui = overUi(sp);
            var mp = MapPoint(sp);
            var newHover = ui ? UiHover : UnitAt(mp);
            if (newHover != Hover) { Hover = newHover; Changed?.Invoke(); }
            GateHover = !ui && newHover == null && Phase == PlayPhase.Orders ? GateAt(mp) : -1;

            // ГМ ставит новый отряд: щелчок по карте — сюда (Г112)
            if (GmPlacing != null)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { GmPlacing = null; Say("Отменено"); }
                else if (mouse.leftButton.wasPressedThisFrame && !ui) { GmPlace(mp); return; }
            }
            // ГМ даёт поручение ИИ: щелчок — цель (отряд или, для «наступать», точка на земле) (Г113)
            if (AiPicking != null)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { AiPicking = null; Say("Отменено"); }
                else if (mouse.leftButton.wasPressedThisFrame && (!ui || UiHover != null)) { AiPick(ui ? UiHover : Hover, mp, ui); return; }
            }
            // ЛКМ: по отряду — выбрать (Ctrl — добавить); по земле — рамка; отпустил без рамки — снять выбор
            if (mouse.leftButton.wasPressedThisFrame && !ui)
            {
                if (Hover != null)
                {
                    if (ctrl) Toggle(Hover); else Select(Hover);
                    // до первого хода свой отряд можно перетащить
                    if (!ctrl && CanDeploy && SideOf(Hover) == ActiveSide && Present(Hover))
                    { DeployUnit = Hover; deployFrom = sp; grabX = Hover.P.X - mp.x; grabY = Hover.P.Y - mp.y; DeployFacing = Hover.P.Facing; }
                }
                else { leftDown = true; BoxA = BoxB = sp; }
            }
            if (DeployUnit != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    if (!DeployDragging && (sp - deployFrom).magnitude > 6) DeployDragging = true;
                    if (DeployDragging)
                    {
                        if (kb != null && kb.qKey.wasPressedThisFrame) DeployFacing = MoveSim.Norm(DeployFacing - 15);
                        if (kb != null && kb.eKey.wasPressedThisFrame) DeployFacing = MoveSim.Norm(DeployFacing + 15);
                        DeployX = mp.x + grabX; DeployY = mp.y + grabY; deployCursor = mp;
                        DeployWall = DeployUnit.P.U.Type != "cavalry" && WallNear(mp.x, mp.y);
                        if (DeployWall) { WallRun(mp.x, mp.y, out DeployWallA, out DeployWallB); DeployOk = WallWhy(DeployUnit) == null; }
                        else DeployOk = Battle.Fits(DeployUnit.P.U, DeployX, DeployY, DeployFacing) && InZone(SideOf(DeployUnit), DeployX, DeployY);
                    }
                }
                else
                {
                    if (DeployDragging && DeployWall) ToWall(DeployUnit, deployCursor.x, deployCursor.y, onWall: true);
                    else if (DeployDragging) Redeploy(DeployUnit, DeployX, DeployY, DeployFacing);
                    DeployUnit = null; DeployDragging = DeployWall = false;
                }
            }
            if (leftDown)
            {
                BoxB = sp;
                if (!BoxSelecting && (BoxB - BoxA).magnitude > 6) BoxSelecting = true;
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    if (BoxSelecting) SelectBox(ctrl);
                    else if (!ctrl && !ToggleGate(GateAt(mp))) Select(null);
                    leftDown = BoxSelecting = false;
                }
            }

            if (Phase != PlayPhase.Orders || Selection.Count == 0) { CancelDrag(); return; }
            // поединок: ПКМ по вражескому отряду — вызов его полководцу
            if (DuelMode && mouse.rightButton.wasPressedThisFrame && (!ui || UiHover != null)) { Challenge(ui ? UiHover : Hover); return; }
            // ПКМ по земле — от точки под мышью; по табличке отряда — от самого отряда (атака)
            if (mouse.rightButton.wasPressedThisFrame && (!ui || UiHover != null))
            {
                var from = ui ? new Vector2((float)UiHover.P.X, (float)UiHover.P.Y) : mp;
                Dragging = true; DragFrom = DragTo = from; previewAt = 0; DragPreview = null; DragTurn = 0;
            }
            if (!Dragging) return;
            if (!ui) DragTo = mp;
            // Q/E при зажатой ПКМ — курс по 15° (встать лицом куда угодно, хоть назад от пути: дойдёт и развернётся)
            if (kb != null && kb.qKey.wasPressedThisFrame) DragTurn = MoveSim.Norm(DragTurn - 15);
            if (kb != null && kb.eKey.wasPressedThisFrame) DragTurn = MoveSim.Norm(DragTurn + 15);
            bool alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            DragOrdersFor(alt);
            DragOrder = Selected != null && DragOrders.TryGetValue(Selected, out var mo) ? mo : null;
            if (Time.unscaledTime >= previewAt && DragOrder != null) { DragPreview = Battle.Preview(Selected, DragOrder); previewAt = Time.unscaledTime + 0.12f; }
            if (Time.unscaledTime >= adviseAt && DragOrder != null) { DragAdvice = AdviceFilter(Battle.Advise(Selected, DragOrder)); adviseAt = Time.unscaledTime + 0.3f; }
            // прочие в группе: мышь стоит 0,15 с — путь движком одному из тех, чей предпросмотр устарел
            if ((DragTo - dragSeen).sqrMagnitude > 0.25f) { dragSeen = DragTo; dragStillAt = Time.unscaledTime; }
            else if (Time.unscaledTime - dragStillAt > 0.15f)
                foreach (var kv in DragOrders)
                {
                    if (kv.Key == Selected || DragPreviews.TryGetValue(kv.Key, out var dp) && SameOrder(dp.o, kv.Value)) continue;
                    DragPreviews[kv.Key] = (kv.Value, Battle.Preview(kv.Key, kv.Value));
                    break;
                }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                var orders = DragOrders.ToList();
                CancelDrag();
                if (Deploying) DeployTo(orders);
                else foreach (var kv in orders) Order(kv.Key, kv.Value);
            }
        }
        public double DragTurn { get; private set; }   // доворот курса приказа клавишами Q/E, °
        void CancelDrag() { Dragging = false; DragPreview = null; DragOrder = null; DragOrders.Clear(); DragPreviews.Clear(); DragAdvice.Clear(); adviseAt = 0; }

        // для проверки из CLI: протянуть ПКМ от точки до точки карты (как мышью) и отпустить
        public void DragFor(Vector2 from, Vector2 to, bool alt = false)
        {
            if (Phase != PlayPhase.Orders || Selection.Count == 0) return;
            Dragging = true; DragFrom = from; DragTo = to;
            DragOrdersFor(alt);
            DragOrder = Selected != null && DragOrders.TryGetValue(Selected, out var mo) ? mo : null;
            DragPreview = DragOrder != null ? Battle.Preview(Selected, DragOrder) : null;
        }
        public void ReleaseDrag()
        {
            var orders = DragOrders.ToList();
            CancelDrag();
            if (Deploying) DeployTo(orders);
            else foreach (var kv in orders) Order(kv.Key, kv.Value);
        }

        // рамка: отряды стороны выбранного (или активной) с серединой внутри; Ctrl — к уже выбранным
        void SelectBox(bool add)
        {
            var a = MapPoint(BoxA); var b = MapPoint(BoxB);
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
            int side = add && Selection.Count > 0 ? SideOf(Selection[0]) : ActiveSide;
            var inside = Battle.Movers.Where(m => Present(m) && SideOf(m) == side).Where(m =>
            {
                BoxOf(m, out var x, out var y, out _, out _, out _);
                return x >= x0 && x <= x1 && y >= y0 && y <= y1;
            }).ToList();
            if (add) inside = Selection.Concat(inside).ToList();
            SelectMany(inside);
        }

        // Что значит ПКМ сейчас — каждому выбранному свой приказ:
        // по врагу — все атакуют (натиск — с Alt или кнопкой); по земле, не протянув — идти, один — лицом по ходу, группа —
        // сохраняя расстановку, повернувшись лицом по ходу; протянув — встать фронтом вдоль линии (как в Total War: линия —
        // фронт, тянешь от левого фланга к правому — лицом «вперёд» от линии; справа налево — кругом; группа — по порядку
        // слева направо, каждому — доля линии по ширине его строя). Q/E при зажатой ПКМ — доворот курса по 15° (Алекс
        // 10.10.2026: встать «в наблюдение на юг», когда точка севернее, — раньше курс брался от пути и назад не ставился)
        void DragOrdersFor(bool alt)
        {
            DragOrders.Clear();
            var group = Selection.Where(Present).ToList();
            if (group.Count == 0) return;
            var target = UnitAt(DragFrom, SideOf(group[0]));
            if (target != null && !group.Contains(target) && SideOf(target) != SideOf(group[0]))
            {
                foreach (var m in group) DragOrders[m] = new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id, Charge = (alt || ChargeMode) && Units.IsCav(m.P.U) };   // натиск — только конница
                return;
            }
            var movers = group.Where(m => !m.Fleeing).ToList();
            if (movers.Count == 0) return;
            double fx = DragTo.x - DragFrom.x, fy = DragTo.y - DragFrom.y, len = Math.Sqrt(fx * fx + fy * fy);
            double cx = movers.Average(m => m.P.X), cy = movers.Average(m => m.P.Y);
            if (len > 4)
            {
                // фронт — линия от DragFrom (левый фланг) к DragTo (правый): лицом — вперёд от неё (правый фланг — справа от
                // смотрящего: для курса h вправо — (cos h, sin h), вперёд — (sin h, −cos h))
                double ux = fx / len, uy = fy / len, nx = uy, ny = -ux;
                double mx = (DragFrom.x + DragTo.x) / 2, my = (DragFrom.y + DragTo.y) / 2;
                double facing = MoveSim.Norm(MoveSim.HeadingOf(nx, ny) + DragTurn);
                // по порядку вдоль линии — как стоят сейчас (так строи не пересекают друг друга); не хватает линии — шире
                var order = movers.OrderBy(m => (m.P.X - mx) * ux + (m.P.Y - my) * uy).ToList();
                const double gap = 6;
                double need = order.Sum(m => m.P.Fp.Front) + gap * (order.Count - 1), scale = Math.Max(1, len / need), at = -Math.Max(len, need) / 2;
                foreach (var m in order)
                {
                    double w = m.P.Fp.Front * scale, s = at + w / 2;
                    DragOrders[m] = new MoveOrder { Kind = OrderKind.Move, X = mx + ux * s, Y = my + uy * s, Facing = facing };
                    at += w + gap * scale;
                }
                return;
            }
            double head = MoveSim.Norm(MoveSim.HeadingOf(DragFrom.x - cx, DragFrom.y - cy) + DragTurn);
            if (movers.Count == 1)
            {
                var m = movers[0];
                DragOrders[m] = new MoveOrder { Kind = OrderKind.Move, X = DragFrom.x, Y = DragFrom.y, Facing = MoveSim.Norm(MoveSim.HeadingOf(DragFrom.x - m.P.X, DragFrom.y - m.P.Y) + DragTurn) };
                return;
            }
            // группа: расстановка поворачивается вместе с ней — с общего курса на курс движения
            double sx = movers.Sum(m => Math.Sin(m.P.Facing * Math.PI / 180)), sy = movers.Sum(m => Math.Cos(m.P.Facing * Math.PI / 180));
            double mean = Math.Atan2(sx, sy) * 180 / Math.PI, turn = MoveSim.AngleDiff(mean, head) * Math.PI / 180;
            double c = Math.Cos(turn), s2 = Math.Sin(turn);
            foreach (var m in movers)
            {
                double ox = m.P.X - cx, oy = m.P.Y - cy;
                DragOrders[m] = new MoveOrder { Kind = OrderKind.Move, X = DragFrom.x + ox * c - oy * s2, Y = DragFrom.y + ox * s2 + oy * c, Facing = head };
            }
        }
    }
}
