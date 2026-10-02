// ═══════════ PlayHud.cs — панели игры (И2, Г80): как в Total War, а не отладочные окошки ═══════════
// Сверху — ход, фаза, «Ход!», скорость показа, силы сторон; снизу — панель приказов с клавишами, вкладки сторон
// и карточки отрядов (род войск, бойцы, потери, БД, приказ, состояние); слева — выбранный отряд и что выйдет из
// приказа; справа — журнал хода; над отрядом под мышью — подсказка. Разметка — PlayHud.uxml, облик — PlayHud.uss.
using System.Collections.Generic;
using System.Linq;
using BattleCore;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Journal.Play
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayHud : MonoBehaviour
    {
        PlayController pc;
        VisualElement root, hud, progressFill, speedGroup, detail, detailRows, ordersBar, sideTabs, cardsRow, tip, toast, over, logPanel, powerSeg1, powerSeg2;
        Label turnNumber, phaseText, phaseSub, detailName, detailType, detailOrder, detailPlan, tipName, tipLine1, tipLine2, toastText, overTitle, overSub, logTitle, powerName1, powerName2;
        Button goButton, pauseButton, speed1, speed2, speed4, againButton;
        Icon detailIcon;
        readonly Dictionary<string, VisualElement> orderBtn = new Dictionary<string, VisualElement>();
        readonly Dictionary<Mover, Card> cards = new Dictionary<Mover, Card>();
        int cardsSide = -1, cardsCount = -1, logShown = -1;

        sealed class Card
        {
            public VisualElement Root, HpFill, MoFill; public Label Men, Flag; public Icon Order, State;
        }

        // Start, а не OnEnable: корень UIDocument строится в его OnEnable — к Start он уже есть
        void Start()
        {
            pc = GetComponent<PlayController>();
            root = GetComponent<UIDocument>().rootVisualElement;
            hud = root.Q("hud");
            ApplyFont();
            turnNumber = root.Q<Label>("turnNumber"); phaseText = root.Q<Label>("phaseText"); phaseSub = root.Q<Label>("phaseSub");
            progressFill = root.Q("progressFill"); goButton = root.Q<Button>("goButton"); speedGroup = root.Q("speedGroup");
            pauseButton = root.Q<Button>("pauseButton"); speed1 = root.Q<Button>("speed1"); speed2 = root.Q<Button>("speed2"); speed4 = root.Q<Button>("speed4");
            powerSeg1 = root.Q("powerSeg1"); powerSeg2 = root.Q("powerSeg2"); powerName1 = root.Q<Label>("powerName1"); powerName2 = root.Q<Label>("powerName2");
            logPanel = root.Q("logPanel"); logTitle = root.Q<Label>("logTitle");
            detail = root.Q("detail"); detailRows = root.Q("detailRows"); detailIcon = root.Q<Icon>("detailIcon");
            detailName = root.Q<Label>("detailName"); detailType = root.Q<Label>("detailType"); detailOrder = root.Q<Label>("detailOrder"); detailPlan = root.Q<Label>("detailPlan");
            ordersBar = root.Q("ordersBar"); sideTabs = root.Q("sideTabs"); cardsRow = root.Q("cardsRow");
            tip = root.Q("tip"); tipName = root.Q<Label>("tipName"); tipLine1 = root.Q<Label>("tipLine1"); tipLine2 = root.Q<Label>("tipLine2");
            toast = root.Q("toast"); toastText = root.Q<Label>("toastText");
            over = root.Q("over"); overTitle = root.Q<Label>("overTitle"); overSub = root.Q<Label>("overSub"); againButton = root.Q<Button>("againButton");

            goButton.clicked += () => pc.Go();
            pauseButton.clicked += () => pc.Paused = !pc.Paused;
            speed1.clicked += () => pc.Speed = 1; speed2.clicked += () => pc.Speed = 2; speed4.clicked += () => pc.Speed = 4;
            againButton.clicked += () => pc.NewBattle();
            logTitle.RegisterCallback<ClickEvent>(_ => logPanel.ToggleInClassList("is-collapsed"));
            BuildOrders();
            pc.SetUiPicker(OverUi);
            var viewer = FindFirstObjectByType<Journal.Viewer.BattleViewer>();
            if (viewer != null) viewer.OverExternalUi = OverUi;   // над панелями колесо и средняя кнопка камеру не двигают
        }

        // Шрифт с кириллицей и засечками из системы (Palatino, Georgia…); нет — шрифт темы
        void ApplyFont()
        {
            foreach (var fam in new[] { "Palatino Linotype", "Book Antiqua", "Georgia", "Cambria" })
            {
                FontAsset f = null;
                try { f = FontAsset.CreateFontAsset(fam, "Regular"); } catch { }
                if (f == null) continue;
                hud.style.unityFontDefinition = FontDefinition.FromSDFFont(f);
                return;
            }
        }

        // Мышь над панелью — карте клики не достаются
        bool OverUi(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            var pos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(pos);
            return picked != null && picked != hud && picked != root;
        }

        // ── панель приказов: значок, подпись, клавиша (Г80) ──
        void BuildOrders()
        {
            ordersBar.Clear(); orderBtn.Clear();
            void Add(string kind, string label, string key, System.Action act, string tipText)
            {
                var b = new VisualElement(); b.AddToClassList("order-btn");
                var ic = new Icon(kind); ic.AddToClassList("order-icon"); b.Add(ic);
                var l = new Label(label); l.AddToClassList("order-label"); b.Add(l);
                var k = new Label(key); k.AddToClassList("order-key"); b.Add(k);
                b.tooltip = tipText;
                if (act != null) b.RegisterCallback<ClickEvent>(_ => act());
                ordersBar.Add(b); orderBtn[kind] = b;
            }
            Add("move", "Идти", "ПКМ", null, "ПКМ по земле — идти; протянуть — куда встать лицом");
            Add("attack", "Атаковать", "ПКМ", null, "ПКМ по врагу — атаковать; стрелки — стрелять");
            Add("charge", "Натиск", "Alt", () => pc.ChargeMode = !pc.ChargeMode, "Следующая атака — с натиском (конница, разбег ≥ 50 м по чистому)");
            Add("hold", "Держать", "Д", () => pc.Hold(), "Стоять на месте");
            Add("retreat", "Отступить", "О", () => pc.Retreat(), "Пятиться лицом к врагу на половине нормы");
            Add("rally", "Сплотить", "С", () => pc.Rally(), "Бегущим: когда враг дальше 150 м — бросок d100 ≤ дисциплина");
            Add("cancel", "Отменить", "⌫", () => pc.Cancel(), "Снять новый приказ — отряд продолжит прежний");
        }

        void LateUpdate()
        {
            if (pc?.Session == null || turnNumber == null) return;
            var s = pc.Session; var bt = pc.Battle;
            bool orders = pc.Phase == PlayPhase.Orders, showing = pc.Phase == PlayPhase.Showing;
            // верх
            int turn = showing ? s.Turn - (s.Phase == BattleCore.Phase.Playing ? 0 : 1) : s.Turn;
            turnNumber.text = Mathf.Max(1, turn).ToString();
            double tin = pc.ShowTime - pc.TurnStartTime;
            phaseText.text = pc.Phase == PlayPhase.Over ? "Битва окончена" : orders ? "Приказы" : pc.Paused ? "Пауза" : "Идёт ход";
            phaseSub.text = orders ? $"Новых приказов: {s.Pending.Count} · Enter — «Ход!»" : showing ? $"{tin:0.0} с из {bt.R.Move.TurnSec:0} · пробел — пауза" : s.Outcome ?? "";
            progressFill.style.width = Length.Percent(showing ? (float)(100 * tin / bt.R.Move.TurnSec) : orders ? 0 : 100);
            goButton.EnableInClassList("hidden", !orders);
            speedGroup.EnableInClassList("hidden", !showing);
            pauseButton.text = pc.Paused ? "▶" : "❚❚";
            speed1.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 1)); speed2.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 2)); speed4.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 4));
            Power(s);
            SideTabs(s);
            Cards(s);
            Detail();
            Orders();
            Tip();
            Log(s);
            toast.EnableInClassList("hidden", Time.time > pc.ToastUntil || string.IsNullOrEmpty(pc.Toast));
            toastText.text = pc.Toast;
            over.EnableInClassList("hidden", pc.Phase != PlayPhase.Over);
            if (pc.Phase == PlayPhase.Over) { overTitle.text = s.Outcome ?? "Битва окончена"; overSub.text = $"Ходов: {s.Turn - 1}"; }
        }

        void Power(BattleSession s)
        {
            var sides = s.Sides.Take(2).ToArray();
            if (sides.Length < 2) return;
            double a = s.UnitsOf(sides[0]).Where(PlayController.Present).Sum(m => m.P.U.Soldiers);
            double b = s.UnitsOf(sides[1]).Where(PlayController.Present).Sum(m => m.P.U.Soldiers);
            float share = (float)(a + b <= 0 ? 0.5 : a / (a + b));
            powerSeg1.style.width = Length.Percent(100 * share); powerSeg2.style.width = Length.Percent(100 * (1 - share));
            powerName1.text = $"{s.Name(sides[0])} · {a:0}"; powerName2.text = $"{b:0} · {s.Name(sides[1])}";
        }

        void SideTabs(BattleSession s)
        {
            if (sideTabs.childCount != s.Sides.Count())
            {
                sideTabs.Clear();
                foreach (var side in s.Sides)
                {
                    var t = new Label(s.Name(side)); t.AddToClassList("side-tab"); t.AddToClassList("side-" + side);
                    int sd = side; t.RegisterCallback<ClickEvent>(_ => { pc.ActiveSide = sd; pc.Select(null); });
                    t.userData = side; sideTabs.Add(t);
                }
            }
            foreach (var t in sideTabs.Children()) t.EnableInClassList("is-on", (int)t.userData == pc.ActiveSide);
        }

        // ── карточки отрядов активной стороны ──
        void Cards(BattleSession s)
        {
            var units = s.UnitsOf(pc.ActiveSide).ToList();
            if (cardsSide != pc.ActiveSide || cardsCount != units.Count) BuildCards(units);
            foreach (var kv in cards)
            {
                var m = kv.Key; var c = kv.Value; var u = m.P.U;
                double start = pc.Game.StartMen.TryGetValue(m, out var sm) ? sm : u.Soldiers;
                float hp = (float)(start <= 0 ? 0 : u.Soldiers / start);
                c.Men.text = $"{u.Soldiers:0}";
                c.HpFill.style.width = Length.Percent(100 * Mathf.Clamp01(hp));
                c.HpFill.EnableInClassList("is-low", hp < 0.5f);
                c.MoFill.style.width = Length.Percent((float)(100 * Mathf.Clamp01((float)(u.Morale / 100))));
                c.Root.EnableInClassList("is-selected", m == pc.Selected);
                bool present = PlayController.Present(m);
                c.Root.EnableInClassList("is-out", !present);
                string flag = m.Gone ? "УШЁЛ" : u.Status == "destroyed" || u.Soldiers <= 0 ? "УНИЧТОЖЕН" : m.Fleeing ? (m.RallyPending ? "СПЛАЧИВАЮТ" : "БЕЖИТ") : u.Morale <= 0 ? "СЛОМЛЕН" : u.Morale < 20 ? "ДРОГНУЛ" : "";
                c.Flag.text = flag; c.Flag.EnableInClassList("hidden", flag == "");
                var o = s.OrderOf(m);
                c.Order.Kind = o == null ? "hold" : OrderIcon(o);
                c.Order.EnableInClassList("is-new", s.Pending.ContainsKey(m));
                bool fighting = m.Figs.Any(f => f.Fighting);
                c.State.Kind = m.Fleeing ? "flee" : fighting ? "fight" : u.Morale <= 0 ? "broken" : "";
                c.State.EnableInClassList("hidden", c.State.Kind == "");
            }
        }
        void BuildCards(List<Mover> units)
        {
            cardsRow.Clear(); cards.Clear();
            cardsSide = pc.ActiveSide; cardsCount = units.Count;
            foreach (var m in units)
            {
                var c = new Card { Root = new VisualElement() };
                c.Root.AddToClassList("card");
                var stripe = new VisualElement(); stripe.AddToClassList("card-stripe"); stripe.AddToClassList("side-" + PlayController.SideOf(m)); c.Root.Add(stripe);
                var ic = new Icon(Icon.OfType(pc.Game.Tpl.TryGetValue(m, out var tpl) ? tpl : "", m.P.U.Type)); ic.AddToClassList("card-icon"); c.Root.Add(ic);
                var name = new Label(m.P.U.Name); name.AddToClassList("card-name"); c.Root.Add(name);
                c.Men = new Label(); c.Men.AddToClassList("card-men"); c.Root.Add(c.Men);
                var hp = new VisualElement(); hp.AddToClassList("bar"); c.HpFill = new VisualElement(); c.HpFill.AddToClassList("bar-fill"); hp.Add(c.HpFill); c.Root.Add(hp);
                var mo = new VisualElement(); mo.AddToClassList("bar"); c.MoFill = new VisualElement(); c.MoFill.AddToClassList("bar-fill"); c.MoFill.AddToClassList("morale"); mo.Add(c.MoFill); c.Root.Add(mo);
                c.Order = new Icon("hold"); c.Order.AddToClassList("card-order"); c.Root.Add(c.Order);
                c.State = new Icon(""); c.State.AddToClassList("card-state"); c.Root.Add(c.State);
                c.Flag = new Label(); c.Flag.AddToClassList("card-flag"); c.Root.Add(c.Flag);
                var mm = m;
                c.Root.RegisterCallback<ClickEvent>(_ => pc.Select(mm));
                c.Root.tooltip = m.P.U.Name;
                cardsRow.Add(c.Root); cards[m] = c;
            }
        }
        static string OrderIcon(MoveOrder o) => o.Kind switch
        {
            OrderKind.Attack => o.Charge ? "charge" : "attack",
            OrderKind.Hold => "hold",
            OrderKind.Retreat => "retreat",
            OrderKind.Rally => "rally",
            _ => "move",
        };

        // ── выбранный отряд и что выйдет из приказа ──
        void Detail()
        {
            var m = pc.Selected;
            detail.EnableInClassList("hidden", m == null);
            if (m == null) return;
            var u = m.P.U;
            detailIcon.Kind = Icon.OfType(pc.Game.Tpl.TryGetValue(m, out var tpl) ? tpl : "", u.Type);
            detailName.text = u.Name;
            detailType.text = $"{TypeName(u)} · {pc.Session.Name(PlayController.SideOf(m))}";
            double start = pc.Game.StartMen.TryGetValue(m, out var sm) ? sm : u.Soldiers;
            var st = Units.MoraleStage(u.Morale);
            Rows(("Бойцов", $"{u.Soldiers:0} из {start:0}"), ("Боевой дух", $"{u.Morale:0} — {st.Label}"), ("Дисциплина", $"{u.Discipline:0}"), ("Усталость", $"{u.Fatigue:0}%"),
                 ("Норма хода", $"{BattleMap.UnitSpeed(u, pc.Battle.R):0} м"));
            var o = pc.Session.OrderOf(m);
            detailOrder.text = "Приказ: " + OrderText(o) + (pc.Session.Pending.ContainsKey(m) ? " (новый)" : "");
            var p = pc.Dragging ? pc.DragPreview : pc.PreviewOf(m);
            detailPlan.RemoveFromClassList("is-bad"); detailPlan.RemoveFromClassList("is-good");
            detailPlan.text = p == null ? "" : PlanText(p);
            if (p?.ChargeOk == false || p?.Note != null && p.Note.Contains("нет")) detailPlan.AddToClassList("is-bad");
            else if (p?.ChargeOk == true || p?.InRange == true) detailPlan.AddToClassList("is-good");
        }
        void Rows(params (string k, string v)[] rows)
        {
            while (detailRows.childCount < rows.Length)
            {
                var r = new VisualElement(); r.AddToClassList("detail-row");
                var k = new Label(); k.AddToClassList("detail-key"); var v = new Label(); v.AddToClassList("detail-val");
                r.Add(k); r.Add(v); detailRows.Add(r);
            }
            for (int i = 0; i < rows.Length; i++)
            {
                var r = detailRows[i];
                ((Label)r[0]).text = rows[i].k; ((Label)r[1]).text = rows[i].v;
            }
        }
        string OrderText(MoveOrder o)
        {
            if (o == null) return "стоять";
            switch (o.Kind)
            {
                case OrderKind.Attack: { var t = pc.Battle.ById(o.TargetId); return (o.Charge ? "натиск на " : "атаковать ") + $"«{t?.P.U.Name}»"; }
                case OrderKind.Hold: return "держать позицию";
                case OrderKind.Retreat: return "отступить";
                case OrderKind.Rally: return "сплотиться";
                default: return "идти";
            }
        }
        static string PlanText(OrderPreview p)
        {
            var parts = new List<string>();
            if (p.Note != null) parts.Add(p.Note);
            if (p.InRange != null) parts.Add(p.InRange == true ? $"достаёт: {p.Gap:0} из {p.Range:0} м" : $"не достаёт: {p.Gap:0} м при дальности {p.Range:0} — подойдёт");
            if (p.Cost > 0) parts.Add(p.ReachThisTurn ? $"дойдёт за ход ({p.Cost:0} м нормы)" : $"за ход {p.ThisTurn:0} из {p.Cost:0} м нормы");
            if (p.ChargeOk == true) parts.Add("натиск: разбега хватит");
            else if (p.ChargeOk == false) parts.Add("натиск: " + p.ChargeWhy);
            return string.Join(" · ", parts);
        }
        static string TypeName(Unit u) => u.Type switch
        {
            "cavalry" => "конница", "archer" => "стрелки", "pike" => "пикинёры", _ => "пехота",
        };

        void Orders()
        {
            bool can = pc.Phase == PlayPhase.Orders && pc.Selected != null;
            bool fleeing = pc.Selected != null && pc.Selected.Fleeing;
            foreach (var kv in orderBtn)
            {
                bool on = can && (kv.Key == "rally" ? fleeing : !fleeing || kv.Key == "cancel");
                kv.Value.EnableInClassList("is-off", !on);
            }
            orderBtn["charge"].EnableInClassList("is-on", pc.ChargeMode);
        }

        // ── подсказка над отрядом под мышью ──
        void Tip()
        {
            var m = pc.Hover;
            tip.EnableInClassList("hidden", m == null);
            if (m == null) return;
            var u = m.P.U;
            tipName.text = u.Name;
            tipLine1.text = $"{pc.Session.Name(PlayController.SideOf(m))} · {u.Soldiers:0} бойцов";
            tipLine2.text = m.Fleeing ? "бежит" : $"БД {u.Morale:0} — {Units.MoraleStage(u.Morale).Label}";
            var mp = Mouse.current?.position.ReadValue() ?? Vector2.zero;
            var pos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mp.x, Screen.height - mp.y));
            tip.style.left = pos.x + 18; tip.style.top = pos.y + 14;
        }

        void Log(BattleSession s)
        {
            if (logShown == s.Logs.Count) return;
            logShown = s.Logs.Count;
            foreach (var l in logPanel.Query<Label>(className: "log-line").ToList()) l.RemoveFromHierarchy();
            if (s.Logs.Count == 0) { logTitle.text = "Журнал — пока пуст"; return; }
            logTitle.text = $"Журнал · ход {s.Logs.Count}";
            foreach (var line in s.Logs[s.Logs.Count - 1].Take(14))
            {
                var l = new Label(line); l.AddToClassList("log-line"); logPanel.Add(l);
            }
        }
    }
}
