// ═══════════ PlayController.cs — режим игры (И2, Г79–Г81): приказы и ход ═══════════
// Фаза приказов: выбираешь отряд, отдаёшь приказ (мышью как в Total War, клавишами, кнопками панели), подсказка на
// карте показывает путь и где отряд встанет к концу хода. «Ход!» — приказы обеих сторон уходят разом (WEGO), ход
// считается движком шаг за шагом во время показа (Г43): показ не обгоняет счёт — не успевает машина, показ ждёт.
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
        public Mover Selected { get; private set; }
        public Mover Hover { get; private set; }
        public bool ChargeMode { get; set; }                 // кнопка «Натиск»: следующая атака — с натиском
        public float Speed { get; set; } = 1;
        public bool Paused { get; set; }
        public double ShowTime { get; private set; }         // время показа, с от начала битвы
        public double TurnStartTime { get; private set; }
        public double ComputedTime => recorder == null ? 0 : (recorder.Rec.Frames.Count - 1) * recorder.Rec.Dt;
        public string Toast { get; private set; } = ""; public float ToastUntil;
        public event Action Changed;                         // приказ, выбор, фаза — панели перерисоваться

        BattleViewer viewer;
        Recorder recorder;
        int stepInTurn;
        // подсказки приказов: отряд → предпросмотр его приказа (новый или прежний)
        readonly Dictionary<Mover, OrderPreview> previews = new Dictionary<Mover, OrderPreview>();
        // приказ, который тянут ПКМ прямо сейчас
        public bool Dragging { get; private set; }
        public Vector2 DragFrom { get; private set; }        // точки карты, м
        public Vector2 DragTo { get; private set; }
        public OrderPreview DragPreview { get; private set; }
        public MoveOrder DragOrder { get; private set; }
        Vector2 dragScreen; float previewAt;
        Func<Vector2, bool> overUi = _ => false;

        public void SetUiPicker(Func<Vector2, bool> f) => overUi = f;

        // Awake, а не Start: живая запись должна попасть в смотрелку раньше её Start — иначе она начнёт считать свою сцену
        void Awake()
        {
            viewer = FindFirstObjectByType<BattleViewer>();
            NewBattle();
        }

        public void NewBattle()
        {
            Game = PlayScenarios.Training();
            recorder = new Recorder(Game.Name, Game.Note, Game.Geo, Game.Battle.Movers, m => Game.Tpl[m], Game.Battle, 99);
            recorder.Snap();
            Phase = PlayPhase.Orders; Selected = null; Hover = null; ChargeMode = false; Paused = false;
            ShowTime = TurnStartTime = 0; stepInTurn = 0;
            previews.Clear();
            if (viewer != null) { viewer.ShowGui = false; viewer.SetLive(recorder.Rec); viewer.Playing = false; }
            RefreshPreviews();
            Changed?.Invoke();
        }

        // ── ход ──
        public void Go()
        {
            if (Phase != PlayPhase.Orders) return;
            CancelDrag();
            Session.Go();
            Phase = PlayPhase.Showing; Paused = false;
            TurnStartTime = ShowTime; stepInTurn = 0;
            previews.Clear();
            Changed?.Invoke();
        }

        void Update()
        {
            if (Game == null) return;
            if (Phase == PlayPhase.Showing) AdvanceTurn();
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
                if (Session.Phase == BattleCore.Phase.Over) { Phase = PlayPhase.Over; recorder.Rec.Done = true; }
                else Phase = PlayPhase.Orders;
                if (Selected != null && !Present(Selected)) Selected = null;
                RefreshPreviews();
                Changed?.Invoke();
            }
        }

        // ── приказы ──
        public static bool Present(Mover m) => (m.P.U.Status == "active" || m.P.U.Status == "fled") && m.P.U.Soldiers > 0 && m.Figs.Count > 0 && !m.Gone;
        public static int SideOf(Mover m) => BattleSession.SideOf(m);
        public OrderPreview PreviewOf(Mover m) => previews.TryGetValue(m, out var p) ? p : null;

        public void Select(Mover m)
        {
            Selected = m != null && Present(m) ? m : null;
            if (Selected != null) ActiveSide = SideOf(Selected);
            ChargeMode = false;
            Changed?.Invoke();
        }

        public bool Order(Mover m, MoveOrder o)
        {
            if (Phase != PlayPhase.Orders) { Say("Приказы — между ходами"); return false; }
            string why = Session.SetOrder(m, o);
            if (why != null) { Say(why); return false; }
            previews[m] = Battle.Preview(m, o);
            Changed?.Invoke();
            return true;
        }
        public void Hold() { if (Selected != null) Order(Selected, new MoveOrder { Kind = OrderKind.Hold, X = Selected.P.X, Y = Selected.P.Y, Facing = Selected.P.Facing }); }
        public void Retreat() { if (Selected != null) Order(Selected, new MoveOrder { Kind = OrderKind.Retreat, X = double.NaN, Y = double.NaN }); }
        public void Rally() { if (Selected != null) Order(Selected, new MoveOrder { Kind = OrderKind.Rally }); }
        public void Cancel() { if (Selected != null && Session.Pending.Remove(Selected)) { RefreshPreview(Selected); Changed?.Invoke(); } }

        void RefreshPreviews()
        {
            previews.Clear();
            foreach (var m in Battle.Movers) RefreshPreview(m);
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

        // ── ввод (Г80): ЛКМ — выбрать; ПКМ по земле — идти, протянуть — куда встать лицом; ПКМ по врагу — атаковать,
        // с Alt (или кнопкой «Натиск») — натиск; клавиши: Д держать, О отступить, С сплотить, Enter — «Ход!», пробел — пауза ──
        public Vector2 MapPoint(Vector2 screen)
        {
            if (viewer != null) return viewer.ScreenToMap(screen);
            var w = Camera.main.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            return new Vector2(w.x, -w.y);
        }
        public Mover UnitAt(Vector2 p)
        {
            Mover best = null; double bd = double.MaxValue;
            foreach (var m in Battle.Movers)
            {
                if (!Present(m)) continue;
                for (int k = 0; k < m.Figs.Count && k < m.P.Figs.Count; k++)
                {
                    var s = m.Figs[k]; var f = m.P.Figs[k];
                    double d = JsMath.Hypot(s.X - p.x, s.Y - p.y) - Math.Max(f.Width, f.Depth) / 2;
                    if (d < 1.5 && d < bd) { bd = d; best = m; }
                }
            }
            return best;
        }

        void HandleInput()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Go();
                if (kb.spaceKey.wasPressedThisFrame && Phase == PlayPhase.Showing) Paused = !Paused;
                if (kb.escapeKey.wasPressedThisFrame) { if (Dragging) CancelDrag(); else Select(null); }
                if (Selected != null && Phase == PlayPhase.Orders)
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
            bool ui = overUi(sp);
            var mp = MapPoint(sp);
            var newHover = ui ? null : UnitAt(mp);
            if (newHover != Hover) { Hover = newHover; Changed?.Invoke(); }
            if (mouse.leftButton.wasPressedThisFrame && !ui) Select(Hover);
            if (Phase != PlayPhase.Orders || Selected == null) { if (Dragging) CancelDrag(); return; }
            if (mouse.rightButton.wasPressedThisFrame && !ui)
            {
                Dragging = true; dragScreen = sp; DragFrom = DragTo = mp; previewAt = 0; DragPreview = null;
            }
            if (!Dragging) return;
            DragTo = mp;
            bool alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            DragOrder = DragOrderFor(Selected, alt);
            if (Time.unscaledTime >= previewAt && DragOrder != null) { DragPreview = Battle.Preview(Selected, DragOrder); previewAt = Time.unscaledTime + 0.12f; }
            if (mouse.rightButton.wasReleasedThisFrame)
            {
                var o = DragOrder;
                CancelDrag();
                if (o != null) Order(Selected, o);
            }
        }
        void CancelDrag() { Dragging = false; DragPreview = null; DragOrder = null; }

        // Что значит ПКМ сейчас: по врагу — атаковать (натиск — с Alt или кнопкой); по земле — идти: протянул — встать
        // лицом поперёк протянутой линии (как в Total War: линия — фронт), не протянул — лицом по ходу
        MoveOrder DragOrderFor(Mover m, bool alt)
        {
            var target = UnitAt(DragFrom);
            if (target != null && target != m && SideOf(target) != SideOf(m))
                return new MoveOrder { Kind = OrderKind.Attack, TargetId = target.P.U.Id, Charge = alt || ChargeMode };
            if (m.Fleeing) return null;
            double fx = DragTo.x - DragFrom.x, fy = DragTo.y - DragFrom.y, len = Math.Sqrt(fx * fx + fy * fy);
            double facing;
            if (len > 4)
            {
                // фронт — линия от DragFrom к DragTo; лицом — в ту сторону от неё, что дальше от того места, откуда идёт отряд
                double nx = -fy / len, ny = fx / len;
                double cx = (DragFrom.x + DragTo.x) / 2 - m.P.X, cy = (DragFrom.y + DragTo.y) / 2 - m.P.Y;
                if (nx * cx + ny * cy < 0) { nx = -nx; ny = -ny; }
                facing = MoveSim.HeadingOf(nx, ny);
                return new MoveOrder { Kind = OrderKind.Move, X = (DragFrom.x + DragTo.x) / 2, Y = (DragFrom.y + DragTo.y) / 2, Facing = facing };
            }
            facing = MoveSim.HeadingOf(DragFrom.x - m.P.X, DragFrom.y - m.P.Y);
            return new MoveOrder { Kind = OrderKind.Move, X = DragFrom.x, Y = DragFrom.y, Facing = facing };
        }
    }
}
