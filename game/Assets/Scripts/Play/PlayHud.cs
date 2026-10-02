// ═══════════ PlayHud.cs — панели игры (И2, Г80): как в Total War, а не отладочные окошки ═══════════
// Сверху — ход, фаза, «Ход!», скорость показа, силы сторон; снизу — панель приказов с клавишами, вкладки сторон
// и карточки отрядов (род войск, бойцы, потери, БД, приказ, состояние); слева — выбранный отряд и что выйдет из
// приказа; справа — журнал хода; над отрядом под мышью — подсказка; над каждым отрядом — табличка (сторона, род войск,
// бойцы, имя), как в Iron Kings. Камера кадрирует бой в часть экрана, свободную от панелей. Разметка — PlayHud.uxml, облик — PlayHud.uss.
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
        Journal.Viewer.BattleViewer viewer;
        PlayBattle fittedFor, shownGame;                        // для какой битвы выбран первый кадр; для какой собраны таблички и карточки
        VisualElement tags, topBar, bottomDock;
        readonly Dictionary<Mover, Tag> tagOf = new Dictionary<Mover, Tag>();
        float lastDockH; int dockStable;                        // первый кадр — когда низ панелей (карточки) устоялся
        readonly List<(Tag t, Rect r)> placed = new List<(Tag, Rect)>();
        VisualElement root, hud, progressFill, speedGroup, detail, detailRows, ordersBar, sideTabs, cardsRow, tip, toast, over, logPanel, powerSeg1, powerSeg2;
        Label turnNumber, phaseText, phaseSub, detailName, detailType, detailOrder, detailPlan, tipName, tipLine1, tipLine2, toastText, overTitle, overSub, logTitle, powerName1, powerName2;
        Button goButton, pauseButton, speed1, speed2, speed4, againButton, menuButton, menuClose;
        VisualElement menu, menuList; Label menuStatus;
        bool menuBusy;
        Icon detailIcon;
        readonly Dictionary<string, VisualElement> orderBtn = new Dictionary<string, VisualElement>();
        readonly Dictionary<Mover, Card> cards = new Dictionary<Mover, Card>();
        int cardsSide = -1, cardsCount = -1, logShown = -1;

        sealed class Tag { public VisualElement Root; public Label Men, Name; public Icon Kind, State; }
        sealed class Card
        {
            public VisualElement Root, HpFill, MoFill; public Label Men, Flag; public Icon Order, State;
        }

        // Start, а не OnEnable: корень UIDocument строится в его OnEnable — к Start он уже есть
        void Start()
        {
            pc = GetComponent<PlayController>();
            root = GetComponent<UIDocument>().rootVisualElement;
            hud = root.Q("hud"); tags = root.Q("tags"); topBar = root.Q("topBar"); bottomDock = root.Q("bottomDock");
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
            menu = root.Q("menu"); menuList = root.Q("menuList"); menuStatus = root.Q<Label>("menuStatus");
            menuButton = root.Q<Button>("menuButton"); menuClose = root.Q<Button>("menuClose");
            againButton.clicked += ShowMenu;
            menuButton.clicked += ShowMenu;
            menuClose.clicked += () => menu.AddToClassList("hidden");
            ShowMenu();   // в начале — выбор битвы (под меню уже стоит учебное поле)
            logTitle.RegisterCallback<ClickEvent>(_ => logPanel.ToggleInClassList("is-collapsed"));
            BuildOrders();
            pc.SetUiPicker(OverUi);
            viewer = FindAnyObjectByType<Journal.Viewer.BattleViewer>();
            if (viewer != null) viewer.OverExternalUi = OverUi;   // над панелями колесо и средняя кнопка камеру не двигают
        }

        // ── меню битв: учебное поле и сохранения трекера; большую битву строить ~секунду — сначала надпись, потом стройка ──
        void ShowMenu()
        {
            menuList.Clear(); menuStatus.text = "";
            foreach (var (name, note, make) in PlayScenarios.All())
            {
                var item = new VisualElement(); item.AddToClassList("menu-item");
                if (pc.Game != null && pc.Game.Name == name) item.AddToClassList("is-current");
                var n = new Label(name); n.AddToClassList("menu-item-name"); item.Add(n);
                var d = new Label(note); d.AddToClassList("menu-item-note"); item.Add(d);
                var mk = make; var nm = name;
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    if (menuBusy) return;
                    menuBusy = true; menuStatus.text = $"Строю «{nm}»…";
                    root.schedule.Execute(() =>
                    {
                        try { pc.NewBattle(mk); menu.AddToClassList("hidden"); }
                        catch (System.Exception e) { Debug.LogException(e); menuStatus.text = "Не вышло: " + e.Message; }
                        menuBusy = false;
                    }).ExecuteLater(30);
                });
                menuList.Add(item);
            }
            menuClose.EnableInClassList("hidden", pc.Game == null || pc.Phase == PlayPhase.Over);
            menu.RemoveFromClassList("hidden");
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
            pc.Blocked = !menu.ClassListContains("hidden");
            var s = pc.Session; var bt = pc.Battle;
            if (shownGame != pc.Game)   // новая битва: таблички, вкладки, карточки, журнал — заново; кадр — когда низ устоится
            {
                shownGame = pc.Game; tags.Clear(); tagOf.Clear(); sideTabs.Clear(); cardsCount = -1; logShown = -1;
                fittedFor = null; dockStable = 0; lastDockH = -1;
            }
            Frame();
            Tags();
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
            over.EnableInClassList("hidden", pc.Phase != PlayPhase.Over || !menu.ClassListContains("hidden"));
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
                c.Root.EnableInClassList("is-selected", pc.IsSelected(m));
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
            bool compact = units.Count > 10;   // много отрядов — мини-карточки в несколько рядов
            cardsRow.EnableInClassList("is-compact", compact); bottomDock.EnableInClassList("is-wide", compact);
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
                c.Root.RegisterCallback<ClickEvent>(e => { if (e.ctrlKey) pc.Toggle(mm); else pc.Select(mm); if (e.clickCount >= 2) pc.FocusOn(mm); });   // Ctrl — к группе; двойной — камера к отряду
                c.Root.RegisterCallback<PointerEnterEvent>(_ => pc.UiHover = mm);
                c.Root.RegisterCallback<PointerLeaveEvent>(_ => { if (pc.UiHover == mm) pc.UiHover = null; });
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
            int more = pc.Selection.Count - 1;
            detailType.text = $"{TypeName(u)} · {pc.Session.Name(PlayController.SideOf(m))}" + (more > 0 ? $" · и ещё {more} в группе" : "");
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
            bool can = pc.Phase == PlayPhase.Orders && pc.Selection.Count > 0;
            bool anyFlee = pc.Selection.Any(m => m.Fleeing), anyLine = pc.Selection.Any(m => !m.Fleeing);
            foreach (var kv in orderBtn)
            {
                bool on = can && (kv.Key == "rally" ? anyFlee : kv.Key == "cancel" || anyLine);
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

        // ── кадр: панели занимают верх и низ экрана — камера кадрирует бой в остаток; в начале битвы — оба войска ──
        void Frame()
        {
            if (viewer == null || root.panel == null || float.IsNaN(root.layout.height) || root.layout.height <= 0) return;
            float k = Screen.height / root.layout.height;   // пикселей экрана на единицу панели
            float top = topBar.worldBound.yMax * k, bottom = (root.layout.height - bottomDock.worldBound.yMin) * k;
            if (float.IsNaN(top) || float.IsNaN(bottom)) return;
            viewer.Insets = new Vector4(0, top + 8, 0, bottom + 8);
            if (fittedFor == pc.Game) return;
            // карточки и вкладки строятся в первых кадрах — ждём, пока высота низа не перестанет меняться
            float dh = bottomDock.layout.height;
            dockStable = cardsRow.childCount > 0 && Mathf.Abs(dh - lastDockH) < 0.5f ? dockStable + 1 : 0; lastDockH = dh;
            if (dockStable < 2) return;
            fittedFor = pc.Game;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var m in pc.Battle.Movers)
            {
                if (!PlayController.Present(m)) continue;
                float r = (float)System.Math.Max(m.P.Fp.Front, m.P.Fp.Depth) / 2;
                x0 = Mathf.Min(x0, (float)m.P.X - r); x1 = Mathf.Max(x1, (float)m.P.X + r);
                y0 = Mathf.Min(y0, (float)m.P.Y - r); y1 = Mathf.Max(y1, (float)m.P.Y + r);
            }
            if (x0 < x1) viewer.FitRect(x0 - 60, y0 - 60, x1 + 60, y1 + 60);
            else viewer.FitView();
        }

        // ── таблички над отрядами: где отряд нарисован (запись смотрелки в момент показа), над верхним краем строя ──
        void Tags()
        {
            var rec = viewer?.Rec;
            if (rec == null || rec.Frames.Count == 0 || root.panel == null) return;
            var movers = pc.Battle.Movers;
            int f0 = Mathf.Clamp((int)System.Math.Floor(viewer.T / rec.Dt), 0, rec.Frames.Count - 1);
            float ppm = Screen.height / (2 * viewer.Cam.orthographicSize);
            bool names = ppm >= 1.2f;   // имена — вблизи; издали — значок и бойцы (имя — у выбранного и под мышью)
            placed.Clear();
            for (int i = 0; i < movers.Count; i++)
            {
                var m = movers[i];
                if (!tagOf.TryGetValue(m, out var t)) tagOf[m] = t = BuildTag(m);
                bool show = PlayController.Present(m) && i < rec.Frames[f0].Length;
                t.Root.EnableInClassList("hidden", !show);
                if (!show) continue;
                // над серединой отряда, на верхней кромке его рамки (где отряд виден): вертикаль через середину выходит из
                // рамки через t = min(фронт/2 / |sin h|, глубина/2 / |cos h|)
                pc.BoxOf(m, out var x, out var y, out var hd, out var bw, out var bd);
                float h = hd * Mathf.Deg2Rad, sh = Mathf.Abs(Mathf.Sin(h)), ch = Mathf.Abs(Mathf.Cos(h));
                float tUp = Mathf.Min(sh > 1e-3f ? bw / 2 / sh : float.MaxValue, ch > 1e-3f ? bd / 2 / ch : float.MaxValue);
                var spt = viewer.MapToScreen(new Vector2(x, y - tUp));
                float sx = spt.x, top = spt.y;
                var pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sx, Screen.height - top));
                // таблички соседей не налезают: занятое место — выше, пока не свободно
                float w = float.IsNaN(t.Root.resolvedStyle.width) ? 60 : t.Root.resolvedStyle.width, th = 20, ty = pp.y - 4;
                for (int guard = 0; guard < 8; guard++)
                {
                    var r = new Rect(pp.x - w / 2, ty - th, w, th);
                    bool hit = false;
                    foreach (var (_, pr) in placed) if (pr.Overlaps(r)) { ty = pr.yMin - 2; hit = true; break; }
                    if (!hit) break;
                }
                placed.Add((t, new Rect(pp.x - w / 2, ty - th, w, th)));
                t.Root.style.left = pp.x; t.Root.style.top = ty;
                var u = m.P.U;
                t.Men.text = $"{u.Soldiers:0}";
                bool sel = pc.IsSelected(m), hov = m == pc.Hover;
                t.Name.EnableInClassList("hidden", !(names || sel || hov));
                t.Root.EnableInClassList("is-selected", sel);
                t.Root.EnableInClassList("is-hover", hov && !sel);
                t.Root.EnableInClassList("is-flee", m.Fleeing);
                bool fighting = m.Figs.Any(fg => fg.Fighting);
                t.State.Kind = m.Fleeing ? "flee" : fighting ? "fight" : "";
                t.State.EnableInClassList("hidden", t.State.Kind == "");
            }
        }
        Tag BuildTag(Mover m)
        {
            var t = new Tag { Root = new VisualElement() };
            t.Root.AddToClassList("unit-tag"); t.Root.AddToClassList("side-" + PlayController.SideOf(m));
            var stripe = new VisualElement(); stripe.AddToClassList("tag-stripe"); t.Root.Add(stripe);
            t.Kind = new Icon(Icon.OfType(pc.Game.Tpl.TryGetValue(m, out var tpl) ? tpl : "", m.P.U.Type)); t.Kind.AddToClassList("tag-icon"); t.Root.Add(t.Kind);
            t.Men = new Label(); t.Men.AddToClassList("tag-men"); t.Root.Add(t.Men);
            t.Name = new Label(m.P.U.Name); t.Name.AddToClassList("tag-name"); t.Root.Add(t.Name);
            t.State = new Icon(""); t.State.AddToClassList("tag-state"); t.Root.Add(t.State);
            var mm = m;
            t.Root.RegisterCallback<ClickEvent>(e => { if (e.ctrlKey) pc.Toggle(mm); else pc.Select(mm); if (e.clickCount >= 2) pc.FocusOn(mm); });
            t.Root.RegisterCallback<PointerEnterEvent>(_ => pc.UiHover = mm);
            t.Root.RegisterCallback<PointerLeaveEvent>(_ => { if (pc.UiHover == mm) pc.UiHover = null; });
            tags.Add(t.Root);
            return t;
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
