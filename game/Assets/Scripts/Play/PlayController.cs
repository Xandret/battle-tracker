// ═══════════ PlayController.cs — режим игры (И2, Г79–Г81): приказы и ход ═══════════
// Фаза приказов: выбираешь отряд или группу, отдаёшь приказ (мышью как в Total War, клавишами, кнопками панели),
// подсказка на карте показывает путь и где отряд встанет к концу хода. «Ход!» — приказы обеих сторон уходят разом
// (WEGO), ход считается движком шаг за шагом во время показа (Г43): показ не обгоняет счёт — не успевает машина, ждёт.
// Рисунок отрядов — смотрелка (BattleViewer, живая запись), подсказки приказов — OrderOverlay, панели — PlayHud.
// Боевой математики здесь нет: только ввод, вызовы движка и что показать.
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;
using Journal.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;

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
            NewBattle();
        }

        Func<PlayBattle> lastMake = () => PlayScenarios.Training();
        // новая битва: из меню — выбранная; «ещё раз» — та же
        public void NewBattle(Func<PlayBattle> make = null)
        {
            if (make != null) lastMake = make;
            Game = lastMake();
            recorder = new Recorder(Game.Name, Game.Note, Game.Geo, Game.Battle.Movers, m => Game.Tpl[m], Game.Battle, 99,
                                    m => Game.Color.TryGetValue(m, out var c) ? c : null, m => Game.Style.TryGetValue(m, out var st) ? st : null);
            recorder.Rec.Image = Game.Image;
            recorder.Snap();
            Phase = PlayPhase.Orders; Selection.Clear(); Selected = null; Hover = null; ChargeMode = false; Paused = false;
            Summaries.Clear(); current = null; EndedByPlayer = false;
            AtStart.Clear(); foreach (var m in Game.Battle.Movers) AtStart[m] = (m.P.U.Soldiers, m.P.U.TotKilled, m.P.U.TotWounded);
            ShowTime = TurnStartTime = 0; stepInTurn = 0;
            previews.Clear(); previewQueue.Clear();
            if (viewer != null) { viewer.ShowGui = false; viewer.SetLive(recorder.Rec); viewer.Playing = false; }
            RefreshPreviews();
            Changed?.Invoke();
        }

        // ── ход ──
        public void Go()
        {
            if (Phase != PlayPhase.Orders) return;
            CancelDrag();
            current = TurnSummary.Begin(Battle, Session.Turn);
            Session.Go();
            Phase = PlayPhase.Showing; Paused = false;
            TurnStartTime = ShowTime; stepInTurn = 0;
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

        void Update()
        {
            if (Game == null) return;
            if (Phase == PlayPhase.Showing) AdvanceTurn();
            else if (Phase == PlayPhase.Orders) DrainPreviews();
            HandleInput();
            if (viewer != null) viewer.T = ShowTime;
        }

        // Счёт идёт впереди показа на ~0,6 с, не дольше 12 мс за кадр; показ не обгоняет досчитанное (Г43)
        void AdvanceTurn()
        {
            double turnEnd = TurnStartTime + Battle.R.Move.TurnSec;
            float budget = Time.realtimeSinceStartup + 0.012f;
            while (Session.Phase == BattleCore.Phase.Playing && ComputedTime < Math.Min(turnEnd, ShowTime + 0.6) && Time.realtimeSinceStartup < budget)
                Session.Step(_ => { if (++stepInTurn % 4 == 0) recorder.Snap(); });
            if (!Paused) ShowTime = Math.Min(ShowTime + Time.deltaTime * Speed, Math.Min(ComputedTime, turnEnd));
            if (Session.Phase != BattleCore.Phase.Playing && ShowTime >= turnEnd - 1e-6)
            {
                ShowTime = turnEnd;
                if (current != null) { current.End(Battle); Summaries.Add(current); current = null; }
                if (Session.Phase == BattleCore.Phase.Over) { Phase = PlayPhase.Over; recorder.Rec.Done = true; }
                else Phase = PlayPhase.Orders;
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
                if (why == null) { QueuePreview(m); ok++; } else last = $"«{m.P.U.Name}»: {why}";
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
        public Mover UnitAt(Vector2 p)
        {
            Mover best = null; double bd = double.MaxValue, tol = Math.Max(1.5, 8 / PixelsPerMeter);
            foreach (var m in Battle.Movers)
            {
                if (!Present(m)) continue;
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
                if (kb.escapeKey.wasPressedThisFrame) { if (Dragging) CancelDrag(); else Select(null); }
                if (ctrl && kb.aKey.wasPressedThisFrame) SelectMany(Battle.Movers.Where(m => SideOf(m) == ActiveSide));
                if (Selection.Count > 0 && Phase == PlayPhase.Orders && !ctrl)
                {
                    if (kb.lKey.wasPressedThisFrame) Hold();      // Д
                    if (kb.jKey.wasPressedThisFrame) Retreat();   // О
                    if (kb.cKey.wasPressedThisFrame) Rally();     // С
                    if (kb.backspaceKey.wasPressedThisFrame) Cancel();
                }
                if (kb.tabKey.wasPressedThisFrame) { ActiveSide = Session.Sides.SkipWhile(s => s != ActiveSide).Skip(1).DefaultIfEmpty(Session.Sides.First()).First(); Select(null); }
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

            // ЛКМ: по отряду — выбрать (Ctrl — добавить); по земле — рамка; отпустил без рамки — снять выбор
            if (mouse.leftButton.wasPressedThisFrame && !ui)
            {
                if (Hover != null) { if (ctrl) Toggle(Hover); else Select(Hover); }
                else { leftDown = true; BoxA = BoxB = sp; }
            }
            if (leftDown)
            {
                BoxB = sp;
                if (!BoxSelecting && (BoxB - BoxA).magnitude > 6) BoxSelecting = true;
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    if (BoxSelecting) SelectBox(ctrl);
                    else if (!ctrl) Select(null);
                    leftDown = BoxSelecting = false;
                }
            }

            if (Phase != PlayPhase.Orders || Selection.Count == 0) { CancelDrag(); return; }
            // ПКМ по земле — от точки под мышью; по табличке отряда — от самого отряда (атака)
            if (mouse.rightButton.wasPressedThisFrame && (!ui || UiHover != null))
            {
                var from = ui ? new Vector2((float)UiHover.P.X, (float)UiHover.P.Y) : mp;
                Dragging = true; DragFrom = DragTo = from; previewAt = 0; DragPreview = null;
            }
            if (!Dragging) return;
            if (!ui) DragTo = mp;
            bool alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            DragOrdersFor(alt);
            DragOrder = Selected != null && DragOrders.TryGetValue(Selected, out var mo) ? mo : null;
            if (Time.unscaledTime >= previewAt && DragOrder != null) { DragPreview = Battle.Preview(Selected, DragOrder); previewAt = Time.unscaledTime + 0.12f; }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                var orders = DragOrders.ToList();
                CancelDrag();
                foreach (var kv in orders) Order(kv.Key, kv.Value);
            }
        }
        void CancelDrag() { Dragging = false; DragPreview = null; DragOrder = null; DragOrders.Clear(); }

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
            foreach (var kv in orders) Order(kv.Key, kv.Value);
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
        // фронт; группа — по порядку слева направо, каждому — доля линии по ширине его строя)
        void DragOrdersFor(bool alt)
        {
            DragOrders.Clear();
            var group = Selection.Where(Present).ToList();
            if (group.Count == 0) return;
            var target = UnitAt(DragFrom);
            if (target != null && !group.Contains(target) && SideOf(target) != SideOf(group[0]))
            {
                foreach (var m in group) DragOrders[m] = new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id, Charge = alt || ChargeMode };
                return;
            }
            var movers = group.Where(m => !m.Fleeing).ToList();
            if (movers.Count == 0) return;
            double fx = DragTo.x - DragFrom.x, fy = DragTo.y - DragFrom.y, len = Math.Sqrt(fx * fx + fy * fy);
            double cx = movers.Average(m => m.P.X), cy = movers.Average(m => m.P.Y);
            if (len > 4)
            {
                // фронт — линия от DragFrom к DragTo; лицом — в ту сторону от неё, что дальше от того места, откуда идёт группа
                double ux = fx / len, uy = fy / len, nx = -uy, ny = ux;
                double mx = (DragFrom.x + DragTo.x) / 2, my = (DragFrom.y + DragTo.y) / 2;
                if (nx * (mx - cx) + ny * (my - cy) < 0) { nx = -nx; ny = -ny; }
                double facing = MoveSim.HeadingOf(nx, ny);
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
            double head = MoveSim.HeadingOf(DragFrom.x - cx, DragFrom.y - cy);
            if (movers.Count == 1)
            {
                var m = movers[0];
                DragOrders[m] = new MoveOrder { Kind = OrderKind.Move, X = DragFrom.x, Y = DragFrom.y, Facing = MoveSim.HeadingOf(DragFrom.x - m.P.X, DragFrom.y - m.P.Y) };
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
