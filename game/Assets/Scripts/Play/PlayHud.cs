// ═══════════ PlayHud.cs — панели игры (И2, Г80): как в Total War, а не отладочные окошки ═══════════
// Сверху — ход, фаза, «Ход!», скорость показа, силы сторон; снизу — панель приказов с клавишами, вкладки сторон
// и карточки отрядов (род войск, бойцы, потери, БД, приказ, состояние); слева — выбранный отряд и что выйдет из
// приказа; справа — сводка хода (потери сторон и главные события, сырой журнал — под «Подробно»); слева во время хода —
// лента событий («бежит!», «натиск!», «сошлись») по мере показа; над отрядом под мышью — подсказка; над каждым отрядом — табличка (сторона, род войск,
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
        LineupPanel lineup;                 // состав битвы из сохранения трекера (Г98)
        MainMenu mainMenu; SettingsPanel settings; ArmyBattlePanel armyBattle;
        MapEditor mapEditor;                // редактор карт (Г102)   // главное меню, настройки, бой своими армиями
        VisualElement overTable; object overBuilt;   // итог битвы «кто сколько потерял» — строится раз на конец битвы
        ArmyEditor armies;                                      // редактор армий (Г93, шаг 1)
        bool menuBusy;
        Icon detailIcon;
        readonly Dictionary<string, VisualElement> orderBtn = new Dictionary<string, VisualElement>();
        readonly Dictionary<Mover, Card> cards = new Dictionary<Mover, Card>();
        int cardsSide = -1, cardsCount = -1, logShown = -1;
        VisualElement feed, summaryBox, detailsBox; Label detailsToggle; ScrollView detailsScroll;
        int feedFrame = -1; readonly Dictionary<long, float> pairShown = new Dictionary<long, float>();

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
            menuButton.clicked += ShowMain;
            menuButton.text = "Меню";
            menuClose.clicked += () => { menu.AddToClassList("hidden"); ShowMain(); };
            menuClose.text = "Назад";
            GameSettings.Init(GetComponent<UIDocument>()); GameSettings.ApplyAudio();
            armies = new ArmyEditor(hud);
            lineup = new LineupPanel(hud, (path, ids) => { pc.NewBattle(() => PlayScenarios.FromSave(path, ids)); menu.AddToClassList("hidden"); }, ShowMenu);
            armyBattle = new ArmyBattlePanel(hud, (a, b, map, seed) => { pc.NewBattle(() => PlayScenarios.FromArmies(a, b, map, seed)); menu.AddToClassList("hidden"); }, ShowMenu);
            settings = new SettingsPanel(hud, ShowMain);
            settings.Changed += ApplySettings;
            mapEditor = new MapEditor(hud, pc, FindAnyObjectByType<Journal.Viewer.BattleViewer>());
            mainMenu = new MainMenu(hud, () => { }, ShowMenu, () => armies.Show(), () => mapEditor.Show(), settings.Show, Quit);
            mapEditor.Closed += ShowMain;
            mapEditor.Test += f => armyBattle.Show(null, f);
            armies.Closed += ShowMain;
            armies.PlayFile += path => armyBattle.Show(path);
            pc.MenuRequested += () => { ShowMain(); escHandled = true; };   // тот же Esc не должен сразу закрыть меню
            ApplySettings();
            overTable = new ScrollView(ScrollViewMode.Vertical); overTable.AddToClassList("over-table");
            againButton.parent.Insert(againButton.parent.IndexOf(againButton), overTable);
            ShowMain();   // в начале — главное меню (под ним — поле-заставка)
            if (SmokeTest.On) StartCoroutine(SmokeTest.Run(pc, lineup, mainMenu));   // Journal.exe -smoke: проверка сборки без рук
            logTitle.RegisterCallback<ClickEvent>(_ => logPanel.ToggleInClassList("is-collapsed"));
            feed = root.Q("feed");
            summaryBox = new VisualElement(); summaryBox.AddToClassList("sum-box"); logPanel.Add(summaryBox);
            detailsToggle = new Label("Подробно ▸"); detailsToggle.AddToClassList("sum-toggle"); logPanel.Add(detailsToggle);
            detailsScroll = new ScrollView(ScrollViewMode.Vertical); detailsScroll.AddToClassList("sum-details"); detailsScroll.AddToClassList("hidden"); logPanel.Add(detailsScroll);
            detailsToggle.RegisterCallback<ClickEvent>(_ =>
            {
                bool open = detailsScroll.ClassListContains("hidden");
                detailsScroll.EnableInClassList("hidden", !open); detailsToggle.text = open ? "Подробно ▾" : "Подробно ▸";
            });
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
            // идёт битва — можно закончить её между ходами и посмотреть итог (Г98: сыграл 3 хода — увидел итог)
            if (pc.Game != null && pc.Phase == PlayPhase.Orders && pc.Summaries.Count > 0)
            {
                var end = new VisualElement(); end.AddToClassList("menu-item"); end.AddToClassList("menu-end");
                var en = new Label("⏹ Закончить битву — итог"); en.AddToClassList("menu-item-name"); end.Add(en);
                var ed = new Label($"«{pc.Game.Name}», сыграно ходов: {pc.Summaries.Count}. Кто сколько потерял — по сторонам и отрядам."); ed.AddToClassList("menu-item-note"); end.Add(ed);
                end.RegisterCallback<ClickEvent>(_ => { menu.AddToClassList("hidden"); pc.EndBattle(); });
                menuList.Insert(0, end);
            }
            // своя армия против армии: армии из редактора «Армии» (или из сохранений трекера), расстановка — сама
            var ab = new VisualElement(); ab.AddToClassList("menu-item"); ab.AddToClassList("menu-armies");
            var abn = new Label("⚔ Своя армия против армии"); abn.AddToClassList("menu-item-name"); ab.Add(abn);
            var abd = new Label("Армии из редактора «Армии»: на каждую сторону — файл и фракция, кто выйдет; карта — поле, лес, холмы, река, пустыня."); abd.AddToClassList("menu-item-note"); ab.Add(abd);
            ab.RegisterCallback<ClickEvent>(_ => { menu.AddToClassList("hidden"); armyBattle.Show(); });
            menuList.Insert(menuList.childCount > 0 && menuList[0].ClassListContains("menu-end") ? 1 : 0, ab);
            // сохранения трекера: открыть файл где угодно или из недавних — дальше выбор состава
            var open = new VisualElement(); open.AddToClassList("menu-item"); open.AddToClassList("menu-open");
            var on = new Label("📂 Открыть сохранение трекера…"); on.AddToClassList("menu-item-name"); open.Add(on);
            var od = new Label("armiya_hodN.txt из любой папки: расстановка как в нём, состав выбираешь сам (для первой битвы — малый)."); od.AddToClassList("menu-item-note"); open.Add(od);
            open.RegisterCallback<ClickEvent>(_ =>
            {
                var f = FileDialog.OpenSave(FileDialog.LastDir());
                if (f == null) return;
                OpenLineup(f);
            });
            menuList.Add(open);
            foreach (var f in PlayScenarios.Saves())
            {
                var item = new VisualElement(); item.AddToClassList("menu-item");
                var n = new Label(Journal.Viewer.SaveScene.Label(f)); n.AddToClassList("menu-item-name"); item.Add(n);
                var d = new Label(f); d.AddToClassList("menu-item-note"); item.Add(d);
                var ff = f;
                item.RegisterCallback<ClickEvent>(_ => OpenLineup(ff));
                menuList.Add(item);
            }
            menu.RemoveFromClassList("hidden");
        }

        // Esc в окне — на шаг назад: настройки, выбор битвы, состав → главное меню; главное меню в бою → к битве
        bool escHandled;
        void Back()
        {
            if (armies.Visible || mapEditor.Visible) return;   // у редакторов свои правки и свой Esc — закрываются своей кнопкой
            if (mainMenu.Visible) { if (mainMenu.CanContinue) mainMenu.Hide(); return; }
            ShowMain();
        }

        // главное меню: при запуске и по Esc / «Меню» в бою; «Продолжить» — когда идёт выбранная игроком битва
        void ShowMain()
        {
            menu.AddToClassList("hidden"); lineup.Hide(); armyBattle.Hide(); settings.Hide();
            mainMenu.Show(pc.Game != null && pc.Chosen && pc.Phase != PlayPhase.Over);
        }
        void ApplySettings()
        {
            tags.style.display = GameSettings.Tags ? DisplayStyle.Flex : DisplayStyle.None;
            if (pc.Phase == PlayPhase.Orders) pc.Speed = GameSettings.Speed;
        }
        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OpenLineup(string f)
        {
            if (lineup.Show(f)) { FileDialog.Remember(f); menu.AddToClassList("hidden"); }
            else menuStatus.text = $"Не читается как сохранение трекера: {System.IO.Path.GetFileName(f)}";
        }

        // итог битвы: по сторонам и отрядам — было, убито, ранено, бежало, в строю (разница с началом битвы)
        void BuildOver()
        {
            overTable.Clear();
            var s = pc.Session; var b = pc.Battle;
            Label Cell(VisualElement row, string t, string cls) { var l = new Label(t); l.AddToClassList(cls); row.Add(l); return l; }
            VisualElement Row(VisualElement parent, string cls) { var r = new VisualElement(); r.AddToClassList("over-row"); if (cls != null) r.AddToClassList(cls); parent.Add(r); return r; }
            foreach (int side in s.Sides.Take(2))
            {
                var units = b.Movers.Where(m => PlayController.SideOf(m) == side && pc.AtStart.ContainsKey(m)).OrderByDescending(m => pc.AtStart[m].Men).ToList();
                double start = 0, killed = 0, wounded = 0, fled = 0, line = 0;
                var rows = new List<string[]>();
                foreach (var m in units)
                {
                    var u = m.P.U; var a = pc.AtStart[m];
                    double k = System.Math.Max(0, u.TotKilled - a.Killed), w = System.Math.Max(0, u.TotWounded - a.Wounded), now = System.Math.Max(0, u.Soldiers);
                    bool dead = u.Status == "destroyed" || now <= 0, gone = m.Gone, flee = m.Fleeing;
                    double f = !dead && (gone || flee) ? now : 0, l = !dead && !gone && !flee ? now : 0;
                    start += a.Men; killed += k; wounded += w; fled += f; line += l;
                    rows.Add(new[] { u.Name, $"{a.Men:0}", $"{k:0}", $"{w:0}", f > 0 ? $"{f:0}" : "—", $"{l:0}", dead ? "уничтожен" : gone ? "ушёл с поля" : flee ? "бежит" : "в строю" });
                }
                var head = Row(overTable, side == 1 ? "is-side1" : "is-side2"); head.AddToClassList("over-side");
                Cell(head, s.Name(side), "over-side-name");
                Cell(head, $"было {start:0} · потеряно {start - line - fled:0} (убито {killed:0}, ранено {wounded:0}) · бежало {fled:0} · в строю {line:0}", "over-side-sum");
                var hr = Row(overTable, "over-hdr");
                foreach (var (t, c) in new[] { ("Отряд", "oc-name"), ("было", "oc-num"), ("убито", "oc-num"), ("ранено", "oc-num"), ("бежало", "oc-num"), ("в строю", "oc-num"), ("", "oc-state") }) Cell(hr, t, c);
                foreach (var r in rows)
                {
                    var row = Row(overTable, null);
                    Cell(row, r[0], "oc-name"); for (int i = 1; i <= 5; i++) Cell(row, r[i], "oc-num"); Cell(row, r[6], "oc-state");
                }
            }
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
        // строи (Г101 глубина, Г106 фигуры): одна кнопка «Строй» в панели — над ней ряд всех строев по группам
        VisualElement formPop; Icon formIcon;
        readonly Dictionary<string, VisualElement> formBtn = new Dictionary<string, VisualElement>();
        static readonly (string kind, string label, string group, string tip)[] Forms =
        {
            ("f-skirmish", "Цепь", "Глубина", "Вдвое мельче строя по столу: шире фронт, меньше шеренг"),
            ("f-line", "Линия", "Глубина", "Строй по столу"),
            ("f-deep", "Глубокий", "Глубина", "Глубокий строй: вдвое больше шеренг, уже фронт"),
            ("f-column", "Колонна", "Глубина", "Колонна: вчетверо глубже — для узостей и марша"),
            ("f-open", "Разомкнуть", "Ряды", "Разомкнутые ряды: шире интервалы — меньше потерь от стрел, слабее в рукопашной"),
            ("f-close", "Сомкнуть", "Ряды", "Сомкнутые ряды: плечом к плечу — крепче в рукопашной, уязвимее для стрел"),
            ("f-wedge", "Клин", "Фигура", "Клин: острие вперёд — пробить строй врага"),
            ("f-crescent", "Полумесяц", "Фигура", "Полумесяц: крылья вперёд — охватить врага с флангов"),
            ("f-square", "Каре", "Фигура", "Каре: лицом на все четыре стороны — против конницы"),
            ("f-circle", "Круг", "Фигура", "Круг: оборона со всех сторон, когда окружили"),
        };
        void ToggleForms(bool? show = null)
        {
            bool on = show ?? formPop.ClassListContains("hidden");
            formPop.EnableInClassList("hidden", !on);
        }
        void PickForm(string kind)
        {
            switch (kind)
            {
                case "f-skirmish": pc.SetFormation(0.5); break;
                case "f-line": pc.SetFormation(1); break;
                case "f-deep": pc.SetFormation(2); break;
                case "f-column": pc.SetFormation(4); break;
                default: pc.SetShape(kind.Substring(2)); break;
            }
            ToggleForms(false);
        }

        void BuildOrders()
        {
            ordersBar.Clear(); orderBtn.Clear(); formBtn.Clear();
            VisualElement Btn(string kind, string label, string key, System.Action act, string tipText)
            {
                var b = new VisualElement(); b.AddToClassList("order-btn");
                var ic = new Icon(kind); ic.AddToClassList("order-icon"); b.Add(ic);
                var l = new Label(label); l.AddToClassList("order-label"); b.Add(l);
                if (key != null) { var k = new Label(key); k.AddToClassList("order-key"); b.Add(k); }
                b.tooltip = tipText;
                if (act != null) b.RegisterCallback<ClickEvent>(_ => act());
                return b;
            }
            void Add(string kind, string label, string key, System.Action act, string tipText) { var b = Btn(kind, label, key, act, tipText); ordersBar.Add(b); orderBtn[kind] = b; }
            // ряд строев — над панелью приказов
            if (formPop == null)
            {
                formPop = new VisualElement(); formPop.AddToClassList("formation-pop"); formPop.AddToClassList("hidden");
                ordersBar.parent.Insert(ordersBar.parent.IndexOf(ordersBar), formPop);
            }
            formPop.Clear();
            string grp = null; VisualElement row = null;
            foreach (var f in Forms)
            {
                if (f.group != grp)
                {
                    if (grp != null) { var sep = new VisualElement(); sep.AddToClassList("formation-sep"); formPop.Add(sep); }
                    grp = f.group;
                    var col = new VisualElement(); col.AddToClassList("formation-col"); formPop.Add(col);
                    var gl = new Label(grp); gl.AddToClassList("formation-group"); col.Add(gl);
                    row = new VisualElement(); row.AddToClassList("formation-row"); col.Add(row);
                }
                string kind = f.kind;
                var b = Btn(kind, f.label, null, () => PickForm(kind), f.tip); b.AddToClassList("formation-btn");
                b.RegisterCallback<PointerEnterEvent>(_ => pc.FormHover = kind); b.RegisterCallback<PointerLeaveEvent>(_ => { if (pc.FormHover == kind) pc.FormHover = null; });
                row.Add(b); formBtn[kind] = b;
            }
            Add("move", "Идти", "ПКМ", null, "ПКМ по земле — идти; протянуть — куда встать лицом");
            Add("attack", "Атаковать", "ПКМ", null, "ПКМ по врагу — атаковать; стрелки — стрелять");
            Add("charge", "Натиск", "Alt", () => pc.ChargeMode = !pc.ChargeMode, "Следующая атака — с натиском (конница, разбег ≥ 50 м по чистому)");
            Add("hold", "Держать", "Д", () => pc.Hold(), "Стоять на месте");
            Add("retreat", "Отступить", "О", () => pc.Retreat(), "Пятиться лицом к врагу на половине нормы");
            Add("rally", "Сплотить", "С", () => pc.Rally(), "Бегущим: когда враг дальше 150 м — бросок d100 ≤ дисциплина");
            Add("cancel", "Отменить", "⌫", () => pc.Cancel(), "Снять новый приказ — отряд продолжит прежний");
            // построение (Г101, Г106): кнопка «Строй» — значок текущего строя, щелчок — ряд всех строев над панелью
            Add("formation", "Строй", "▴", () => ToggleForms(), "Строй отряда: глубина, ряды, фигура. Перестраиваются на месте, бойцы идут на новые места шагом");
            formIcon = orderBtn["formation"].Q<Icon>(); formIcon.Kind = "f-line";
            // Г104: гарнизон — пехота на ближайшую стену фронтом наружу (до первого хода — сразу, потом — идёт)
            Add("wall", "На стену", "Н", () => pc.WallSelected(), "Пехота — на ближайшую стену (до 60 м) фронтом наружу; кто не влез — во дворе. Можно и перетащить отряд на стену до первого хода");
            // Г108: поединок полководцев — потом ПКМ по вражескому отряду с полководцем
            Add("duel", "Поединок", "П", () => { if (pc.CommanderOf(pc.Selected) != null) pc.DuelMode = !pc.DuelMode; }, "Вызвать вражеского полководца на поединок: нажми, потом ПКМ по его отряду (до 80 м). Отказ бьёт по боевому духу его войска");
        }

        void LateUpdate()
        {
            var lk = pc?.ViewRec;
            if (lk == null) LateBody(); else lock (lk) LateBody();
        }
        void LateBody()
        {
            if (pc?.Session == null || turnNumber == null) return;
            pc.Blocked = !menu.ClassListContains("hidden") || armies.Visible || lineup.Visible || mainMenu.Visible || settings.Visible || armyBattle.Visible || mapEditor.Visible;
            mapEditor.Tick();
            hud.EnableInClassList("is-bare", !pc.Chosen || mapEditor.Visible);   // поле-заставка за главным меню — без панелей битвы
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && pc.Blocked && !escHandled) Back();
            escHandled = false;
            if (viewer != null) viewer.InputBlocked = pc.Blocked && !(mapEditor.Visible && !mapEditor.Typing);   // в редакторе карт камера своя   // набор текста в окнах не двигает камеру
            var s = pc.Session; var bt = pc.Battle;
            if (shownGame != pc.Game)   // новая битва: таблички, вкладки, карточки, журнал — заново; кадр — когда низ устоится
            {
                shownGame = pc.Game; tags.Clear(); tagOf.Clear(); sideTabs.Clear(); cardsCount = -1; logShown = -1;
                feed.Clear(); feedFrame = -1; pairShown.Clear();
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
            phaseSub.text = orders ? (pc.DeployWall ? "Отпусти — отряд встанет на стену фронтом наружу" : pc.GateHover >= 0 ? "Щелчок по воротам — открыть или закрыть" : pc.CanDeploy ? $"Расстановка: тяни свой отряд ЛКМ (можно на стену), Q/E — повернуть · приказов {s.Pending.Count} · Enter — «Ход!»" : $"Новых приказов: {s.Pending.Count} · Enter — «Ход!»") : showing ? (pc.Computing && GameSettings.ComputeFirst ? $"Считаю ход… {pc.ComputeProgress * 100:0}%" : $"{tin:0.0} с из {bt.R.Move.TurnSec:0} · пробел — пауза") : s.Outcome ?? "";
            progressFill.style.width = Length.Percent(showing ? (float)(100 * tin / bt.R.Move.TurnSec) : orders ? 0 : 100);
            goButton.EnableInClassList("hidden", !orders);
            speedGroup.EnableInClassList("hidden", !showing);
            pauseButton.text = pc.Paused ? "▶" : "❚❚";
            speed1.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 1)); speed2.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 2)); speed4.EnableInClassList("is-on", Mathf.Approximately(pc.Speed, 4));
            Power(s);
            Duels(s);
            Hints();
            SideTabs(s);
            Cards(s);
            Detail();
            Orders();
            Tip();
            Log(s);
            Feed();
            toast.EnableInClassList("hidden", Time.time > pc.ToastUntil || string.IsNullOrEmpty(pc.Toast));
            toastText.text = pc.Toast;
            over.EnableInClassList("hidden", pc.Phase != PlayPhase.Over || !menu.ClassListContains("hidden") || lineup.Visible || mainMenu.Visible || settings.Visible || armyBattle.Visible || armies.Visible);
            if (pc.Phase == PlayPhase.Over)
            {
                overTitle.text = pc.EndedByPlayer ? "Итог битвы" : s.Outcome ?? "Битва окончена";
                overSub.text = (pc.EndedByPlayer ? $"Битва остановлена после хода {pc.Summaries.Count}" : $"Ходов: {pc.Summaries.Count}") + $" · «{pc.Game.Name}»";
                if (overBuilt != (object)pc.Game) { overBuilt = pc.Game; BuildOver(); }
            }
        }

        void Power(BattleSession s)
        {
            var sides = s.Sides.Take(2).ToArray();
            if (sides.Length < 2) return;
            // туман (Г107): чужих — только кого видно, и словами-округлением («≈ 1 200»)
            double Sum(int sd) => s.UnitsOf(sd).Where(m => PlayController.Present(m) && pc.SeenNow(m)).Sum(m => m.P.U.Soldiers);
            double a = Sum(sides[0]), b = Sum(sides[1]);
            float share = (float)(a + b <= 0 ? 0.5 : a / (a + b));
            powerSeg1.style.width = Length.Percent(100 * share); powerSeg2.style.width = Length.Percent(100 * (1 - share));
            string N(int sd, double v) => pc.ViewSide > 0 && pc.ViewSide != sd ? $"видно ≈ {System.Math.Round(v / 100) * 100:0}" : $"{v:0}";
            powerName1.text = $"{s.Name(sides[0])} · {N(sides[0], a)}"; powerName2.text = $"{N(sides[1], b)} · {s.Name(sides[1])}";
        }

        // ── поединки (Г108): вызовы — «принять / отказаться»; идущий поединок — полоса сверху с ранами и «к поединку» ──
        VisualElement duelBox; int duelShown = -1; string duelKey;
        void Duels(BattleSession s)
        {
            if (duelBox == null)
            {
                duelBox = new VisualElement(); duelBox.AddToClassList("duel-box"); duelBox.pickingMode = PickingMode.Ignore;
                hud.Add(duelBox);
            }
            var bt = pc.Battle; var sb = new System.Text.StringBuilder();
            bool orders = pc.Phase == PlayPhase.Orders;
            if (orders) foreach (var c in bt.Challenges) sb.Append(c.from.P.U.Id).Append('>').Append(c.to.P.U.Id).Append(';');
            var d = pc.ActiveDuel();
            if (d != null) sb.Append($"d{d.A}-{d.B}-{d.WoundsA}-{d.WoundsB}-{d.Over}-{d.Winner}");
            string key = sb.ToString();
            if (key == duelKey) return;
            duelKey = key; duelBox.Clear();
            if (orders)
                foreach (var c in bt.Challenges.ToList())
                {
                    var ca = pc.CommanderOf(c.from); var cb = pc.CommanderOf(c.to);
                    var row = new VisualElement(); row.AddToClassList("duel-row"); row.AddToClassList("panel");
                    row.Add(new Label($"⚔ «{ca?.Name}» ({ca?.Valor:0}) вызывает «{cb?.Name}» ({cb?.Valor:0}) на поединок — ответ за «{s.Name(PlayController.SideOf(c.to))}»") { name = "duel-text" });
                    var to = c.to;
                    var yes = new Button(() => pc.AnswerDuel(to, true)) { text = "Принять" }; yes.AddToClassList("army-btn"); yes.AddToClassList("is-gold");
                    var no = new Button(() => pc.AnswerDuel(to, false)) { text = "Отказаться" }; no.AddToClassList("army-btn");
                    row.Add(yes); row.Add(no); duelBox.Add(row);
                }
            if (d != null)
            {
                var row = new VisualElement(); row.AddToClassList("duel-row"); row.AddToClassList("panel");
                string Pips(int w) => new string('●', w) + new string('○', Mathf.Max(0, 3 - w));
                string head = d.Over || d.Winner >= 0
                    ? $"⚔ Поединок окончен: «{(d.Winner == d.A ? d.NameA : d.NameB)}» одолел «{(d.Winner == d.A ? d.NameB : d.NameA)}»{(d.LoserKilled ? " — убит" : " — ранен")}"
                    : $"⚔ Поединок: «{d.NameA}» (доблесть {d.ValorA:0}, раны {Pips(d.WoundsA)})  против  «{d.NameB}» (доблесть {d.ValorB:0}, раны {Pips(d.WoundsB)})";
                row.Add(new Label(head));
                var go = new Button(() => pc.LookAtDuel(pc.ActiveDuel())) { text = "К поединку" }; go.AddToClassList("army-btn");
                row.Add(go); duelBox.Add(row);
            }
        }

        // ── Г116а: всплывающие подсказки — при наведении на приказ, кнопку или цифру отряда, через 0,4 с; текст — tooltip элемента
        // (в игре Unity сам его не показывает) или живой текст (hints) — для чисел, что меняются ──
        Label hintBox; VisualElement hintOn; float hintSince;
        readonly Dictionary<VisualElement, System.Func<string>> hints = new Dictionary<VisualElement, System.Func<string>>();
        static readonly Dictionary<string, string> DetailHints = new Dictionary<string, string>
        {
            ["Бойцов"] = "Сколько бойцов в строю сейчас и сколько было в начале битвы. Убитые и раненые выбывают из строя.",
            ["Боевой дух"] = "БД. Падает от потерь, удара во фланг или тыл, засады, проигранного поединка; растёт от победы и полководца. Чем ниже, тем ближе отряд к бегству.",
            ["Дисциплина"] = "Выучка: по ней бросают проверки на бегство и «сплотить» бегущих (d100 ≤ дисциплина).",
            ["Усталость"] = "Копится на марше, бегом и в рукопашной; отдых — стоя.",
            ["Норма хода"] = "Сколько метров отряд проходит за ход (15 с) по ровному полю; лес, холм, брод и строй в беспорядке — медленнее.",
        };
        void Hint(VisualElement e, System.Func<string> text) { hints[e] = text; }
        void Hints()
        {
            if (hintBox == null)
            {
                hintBox = new Label(); hintBox.AddToClassList("hint-box"); hintBox.AddToClassList("hidden"); hintBox.pickingMode = PickingMode.Ignore;
                root.Add(hintBox);
            }
            var mp = Mouse.current?.position.ReadValue() ?? Vector2.zero;
            var pos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mp.x, Screen.height - mp.y));
            VisualElement on = null; string text = null;
            for (var e = root.panel.Pick(pos); e != null && e != root; e = e.parent)
            {
                if (hints.TryGetValue(e, out var f)) { text = f(); on = e; break; }
                if (!string.IsNullOrEmpty(e.tooltip)) { text = e.tooltip; on = e; break; }
            }
            if (on != hintOn) { hintOn = on; hintSince = Time.unscaledTime; }
            bool show = on != null && !string.IsNullOrEmpty(text) && Time.unscaledTime - hintSince > 0.4f;
            hintBox.EnableInClassList("hidden", !show);
            if (!show) return;
            hintBox.text = text;
            float w = float.IsNaN(hintBox.resolvedStyle.width) ? 280 : hintBox.resolvedStyle.width, h = float.IsNaN(hintBox.resolvedStyle.height) ? 40 : hintBox.resolvedStyle.height;
            var size = root.layout.size;
            float x = Mathf.Min(pos.x + 16, size.x - w - 6), y = pos.y + 20 + h > size.y - 6 ? pos.y - h - 10 : pos.y + 20;
            hintBox.style.left = Mathf.Max(6, x); hintBox.style.top = Mathf.Max(6, y);
            hintBox.BringToFront();
        }

        // в схватке ли отряд сейчас — по записи (колонны боя меняет поток счёта хода, перебирать их отсюда нельзя)
        bool FightingNow(Mover m)
        {
            var rec = viewer?.Rec; int i = pc.Battle.Movers.IndexOf(m);
            if (rec == null || i < 0 || rec.Fights.Count == 0) return false;
            var pairs = rec.Fights[Mathf.Clamp((int)(viewer.T / rec.Dt), 0, rec.Fights.Count - 1)];
            foreach (var x in pairs) if (x == i) return true;
            return false;
        }

        Label viewTab, allTab;
        void SideTabs(BattleSession s)
        {
            if (sideTabs.childCount != s.Sides.Count() + 2)   // + «Все» и «Вид»
            {
                sideTabs.Clear();
                foreach (var side in s.Sides)
                {
                    var t = new Label(s.Name(side)); t.AddToClassList("side-tab"); t.AddToClassList("side-" + side);
                    int sd = side; t.RegisterCallback<ClickEvent>(_ => { pc.CardsAll = false; pc.ActiveSide = sd; pc.Select(null); if (pc.ViewSide > 0) pc.ViewSide = sd; });
                    t.userData = side; sideTabs.Add(t);
                }
                // ГМ (Г112): «Все» — карточки обеих сторон, приказы за любую без переключения
                allTab = new Label("Все"); allTab.AddToClassList("side-tab"); allTab.userData = 0; allTab.tooltip = "Карточки обеих сторон разом: выбирай любой отряд и отдавай приказ без переключения стороны (вид ГМа)";
                allTab.RegisterCallback<ClickEvent>(_ => pc.CardsAll = true); sideTabs.Add(allTab);
                // чьими глазами (Г18): ГМ — видно всё; сторона — туман войны для чужих
                viewTab = new Label(); viewTab.AddToClassList("side-tab"); viewTab.AddToClassList("view-tab");
                viewTab.tooltip = "Чьими глазами смотреть (М): ГМ видит всё, сторона — только тех чужих, кого видит";
                viewTab.RegisterCallback<ClickEvent>(_ => pc.CycleView()); viewTab.userData = -1; sideTabs.Add(viewTab);
            }
            foreach (var t in sideTabs.Children()) t.EnableInClassList("is-on", (int)t.userData == pc.ActiveSide && !(pc.CardsAll && pc.ViewSide == 0) || (int)t.userData == 0 && pc.CardsAll && pc.ViewSide == 0);
            if (allTab != null) allTab.EnableInClassList("hidden", pc.ViewSide != 0);
            if (viewTab != null) viewTab.text = pc.ViewSide == 0 ? "Вид: ГМ" : $"Вид: {s.Name(pc.ViewSide)}";
        }

        // ── карточки отрядов активной стороны ──
        void Cards(BattleSession s)
        {
            bool all = pc.CardsAll && pc.ViewSide == 0;
            var units = (all ? s.Battle.Movers.OrderBy(PlayController.SideOf).ToList() : s.UnitsOf(pc.ActiveSide).ToList());
            if (cardsSide != (all ? 0 : pc.ActiveSide) || cardsCount != units.Count) BuildCards(units);
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
                bool fighting = FightingNow(m);
                c.State.Kind = m.Fleeing ? "flee" : fighting ? "fight" : u.Morale <= 0 ? "broken" : "";
                c.State.EnableInClassList("hidden", c.State.Kind == "");
            }
        }
        void BuildCards(List<Mover> units)
        {
            foreach (var c0 in cards.Values) foreach (var e in c0.Root.Children()) hints.Remove(e);   // подсказки старых карточек
            foreach (var c0 in cards.Values) hints.Remove(c0.Root);
            cardsRow.Clear(); cards.Clear();
            bool compact = units.Count > 10;   // много отрядов — мини-карточки в несколько рядов
            cardsRow.EnableInClassList("is-compact", compact); bottomDock.EnableInClassList("is-wide", compact);
            cardsSide = pc.CardsAll && pc.ViewSide == 0 ? 0 : pc.ActiveSide; cardsCount = units.Count;
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
                Hint(c.Root, () => $"{mm.P.U.Name}: {mm.P.U.Soldiers:0} бойцов, БД {mm.P.U.Morale:0} — {Units.MoraleStage(mm.P.U.Morale).Label}.\nЩелчок — выбрать, Ctrl — добавить к группе, двойной — камера к отряду.");
                Hint(hp, () => $"Бойцы: {mm.P.U.Soldiers:0} из {(pc.Game.StartMen.TryGetValue(mm, out var s0) ? s0 : mm.P.U.Soldiers):0} в начале битвы");
                Hint(mo, () => $"Боевой дух: {mm.P.U.Morale:0} — {Units.MoraleStage(mm.P.U.Morale).Label}");
                Hint(c.Order, () => $"Приказ: {OrderText(pc.Session.OrderOf(mm))}" + (pc.Session.Pending.ContainsKey(mm) ? " (новый, уйдёт по «Ход!»)" : ""));
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
            GmBox();
            var p = pc.Dragging ? pc.DragPreview : pc.PreviewOf(m);
            detailPlan.RemoveFromClassList("is-bad"); detailPlan.RemoveFromClassList("is-good");
            detailPlan.text = p == null ? "" : PlanText(p);
            if (p?.ChargeOk == false || p?.Note != null && p.Note.Contains("нет")) detailPlan.AddToClassList("is-bad");
            else if (p?.ChargeOk == true || p?.InRange == true) detailPlan.AddToClassList("is-good");
        }
        // ── Г112: раздел ГМа под панелью отряда — «ГМ ▸» раскрывает: БД и усталость выбранных ±, модификаторы БД по таблице этапа 3
        // (отряду или всей его стороне), отмена правки, последние правки. Только в виде ГМа и между ходами ──
        VisualElement gmBox, gmBody, gmMenRow; Label gmHead, gmFatigue, gmMorale, gmMen, gmLogLabel; DropdownField gmMod, gmTpl, gmSide; bool gmOpen; string gmKey;
        Button gmRemove, gmAdd; IntegerField gmAddMen; TextField gmAddName;
        void GmBox()
        {
            if (gmBox == null)
            {
                gmBox = new VisualElement(); gmBox.AddToClassList("gm-box"); detail.Add(gmBox);
                gmHead = new Label("ГМ ▸"); gmHead.AddToClassList("gm-head"); gmHead.tooltip = "Инструменты ГМа: правка боевого духа и усталости, модификаторы БД по таблице, отмена";
                gmHead.RegisterCallback<ClickEvent>(_ => { gmOpen = !gmOpen; gmKey = null; }); gmBox.Add(gmHead);
                gmBody = new VisualElement(); gmBox.Add(gmBody);
                VisualElement Line2(string label, out Label val, System.Action<double> step, string tipText, double mul = 1)
                {
                    var r = new VisualElement(); r.AddToClassList("gm-row");
                    var k = new Label(label); k.AddToClassList("gm-key"); r.Add(k);
                    foreach (var d in new[] { -10.0, -5.0 }) { var dd = d * mul; var b = new Button(() => step(dd)) { text = $"{dd:0}" }; b.AddToClassList("gm-btn"); r.Add(b); }
                    val = new Label(); val.AddToClassList("gm-val"); r.Add(val);
                    foreach (var d in new[] { 5.0, 10.0 }) { var dd = d * mul; var b = new Button(() => step(dd)) { text = $"+{dd:0}" }; b.AddToClassList("gm-btn"); r.Add(b); }
                    r.tooltip = tipText; gmBody.Add(r); return r;
                }
                Line2("БД", out gmMorale, d => pc.GmMorale(pc.Selection, d), "Боевой дух выбранных отрядов — правка ГМа, с журналом и отменой");
                Line2("Усталость", out gmFatigue, d => pc.GmFatigue(pc.Selection, d), "Усталость выбранных, % — правка ГМа");
                gmMenRow = Line2("Бойцов", out gmMen, d => pc.GmSoldiers(pc.Selected, d), "Численность выбранного отряда ±50/±100 — до первого хода (строй раскладывается заново)", 10);
                gmMod = new DropdownField("Модификатор", PlayController.MoraleMods.Select(x => $"{x.Name} {(x.Value >= 0 ? "+" : "")}{x.Value:0}").ToList(), 0); gmMod.AddToClassList("gm-mod"); gmBody.Add(gmMod);
                var mr = new VisualElement(); mr.AddToClassList("gm-row");
                var toUnit = new Button(() => pc.GmMod(pc.Selection, gmMod.index)) { text = "Выбранным" }; toUnit.AddToClassList("army-btn"); toUnit.AddToClassList("small"); mr.Add(toUnit);
                var toSide = new Button(() => { if (pc.Selected != null) pc.GmMod(pc.SideUnits(PlayController.SideOf(pc.Selected)), gmMod.index); }) { text = "Всей стороне" }; toSide.AddToClassList("army-btn"); toSide.AddToClassList("small"); mr.Add(toSide);
                var undo = new Button(() => pc.GmUndo()) { text = "Отменить" }; undo.AddToClassList("army-btn"); undo.AddToClassList("small"); mr.Add(undo);
                gmBody.Add(mr);
                // состав (до первого хода): убрать выбранный, добавить новый — шаблон, сторона, бойцов, имя, потом щелчок по карте
                var rr = new VisualElement(); rr.AddToClassList("gm-row");
                gmRemove = new Button(() => pc.GmRemove(pc.Selected)) { text = "Убрать с поля" }; gmRemove.AddToClassList("army-btn"); gmRemove.AddToClassList("small"); rr.Add(gmRemove);
                gmBody.Add(rr);
                gmTpl = new DropdownField("Новый отряд", Templates.Base.Select(x => x.Name).ToList(), 1); gmTpl.AddToClassList("gm-mod"); gmBody.Add(gmTpl);
                gmSide = new DropdownField("Сторона", new List<string> { "1", "2" }, 0); gmSide.AddToClassList("gm-mod"); gmBody.Add(gmSide);
                gmAddMen = new IntegerField("Бойцов") { value = 500 }; gmAddMen.AddToClassList("gm-mod"); gmBody.Add(gmAddMen);
                gmAddName = new TextField("Имя") { value = "" }; gmAddName.AddToClassList("gm-mod"); gmBody.Add(gmAddName);
                gmAdd = new Button(() => pc.GmStartAdd(Templates.Base[System.Math.Max(0, gmTpl.index)].Id, System.Math.Max(0, gmSide.index) + 1, gmAddMen.value, gmAddName.value)) { text = "Поставить щелчком по карте" };
                gmAdd.AddToClassList("army-btn"); gmAdd.AddToClassList("small"); gmBody.Add(gmAdd);
                gmLogLabel = new Label(); gmLogLabel.AddToClassList("gm-log"); gmBody.Add(gmLogLabel);
            }
            bool can = pc.ViewSide == 0;
            gmBox.EnableInClassList("hidden", !can);
            if (!can) return;
            var m = pc.Selected; bool edit = pc.GmCanEdit;
            string key = $"{gmOpen}|{edit}|{m?.P.U.Morale}|{m?.P.U.Fatigue}|{m?.P.U.Soldiers}|{pc.GmLog.Count}|{pc.GmUndoCount}|{pc.Selection.Count}|{pc.GmRosterWhy}|{pc.Session.SideNames.Count}";
            if (key == gmKey) return;
            gmKey = key;
            gmHead.text = gmOpen ? "ГМ ▾" : "ГМ ▸";
            gmBody.EnableInClassList("hidden", !gmOpen);
            gmBody.SetEnabled(edit);
            if (m == null) return;
            string more = pc.Selection.Count > 1 ? $" (и ещё {pc.Selection.Count - 1})" : "";
            gmMorale.text = $"{m.P.U.Morale:0}{more}"; gmFatigue.text = $"{m.P.U.Fatigue:0}%"; gmMen.text = $"{m.P.U.Soldiers:0}";
            bool roster = pc.GmRosterWhy == null;
            gmMenRow.SetEnabled(roster); gmRemove.SetEnabled(roster); gmAdd.SetEnabled(roster);
            gmMenRow.tooltip = roster ? "Численность выбранного отряда ±50/±100 — строй раскладывается заново" : "Численность — " + pc.GmRosterWhy;
            gmSide.choices = pc.Session.Sides.Select(sd => pc.Session.Name(sd)).ToList();
            gmLogLabel.text = (edit ? "" : "Правки — между ходами.\n") + string.Join("\n", pc.GmLog.Skip(System.Math.Max(0, pc.GmLog.Count - 4)));
        }

        void Rows(params (string k, string v)[] rows)
        {
            while (detailRows.childCount < rows.Length)
            {
                var r = new VisualElement(); r.AddToClassList("detail-row");
                var k = new Label(); k.AddToClassList("detail-key"); var v = new Label(); v.AddToClassList("detail-val");
                r.Add(k); r.Add(v); detailRows.Add(r);
                Hint(r, () => DetailHints.TryGetValue(k.text, out var h) ? h : null);
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
            bool anyWall = pc.HasWalls && pc.Selection.Any(m => !m.Fleeing && m.P.U.Type != "cavalry");
            foreach (var kv in orderBtn)
            {
                bool on = can && (kv.Key == "rally" ? anyFlee : kv.Key == "wall" ? anyWall : kv.Key == "duel" ? pc.Selection.Count == 1 && pc.CommanderOf(pc.Selected) != null && !pc.Selected.Fleeing : kv.Key == "cancel" || anyLine);
                kv.Value.EnableInClassList("is-off", !on);
            }
            orderBtn["charge"].EnableInClassList("is-on", pc.ChargeMode);
            orderBtn["duel"].EnableInClassList("is-on", pc.DuelMode);
            string rn = pc.Selected != null && pc.Battle != null ? pc.Battle.RanksName(pc.Selected.P.U) : null;
            string cur = "f-line";
            foreach (var (k, name) in new[] { ("f-skirmish", "цепь"), ("f-line", "линия"), ("f-deep", "глубокий строй"), ("f-column", "колонна") })
            {
                bool on = rn == name && !pc.Selected.Fleeing;
                formBtn[k].EnableInClassList("is-on", on); if (on) cur = k;
            }
            string sh = pc.ShapeOf(pc.Selected);   // Г106: фигура строя (клин, каре…), null — нет
            foreach (var kv in formBtn)
            {
                bool shape = kv.Key != "f-skirmish" && kv.Key != "f-line" && kv.Key != "f-deep" && kv.Key != "f-column";
                if (shape) { bool on = sh != null && kv.Key == "f-" + sh; kv.Value.EnableInClassList("is-on", on); if (on) cur = kv.Key; }
                kv.Value.EnableInClassList("is-soon", shape && !pc.ShapesReady);
                kv.Value.EnableInClassList("is-off", !can || !anyLine);
            }
            if (formIcon != null && formIcon.Kind != cur) formIcon.Kind = cur;
            orderBtn["formation"].EnableInClassList("is-on", !formPop.ClassListContains("hidden"));
            if (!can && !formPop.ClassListContains("hidden")) ToggleForms(false);
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
                bool show = PlayController.Present(m) && i < rec.Frames[f0].Length && rec.Visible(i, f0, pc.ViewSide);   // туман (Г18)
                t.Root.EnableInClassList("hidden", !show);
                if (!show) continue;
                // над серединой отряда, на верхней кромке его рамки (где отряд виден): вертикаль через середину выходит из
                // рамки через t = min(фронт/2 / |sin h|, глубина/2 / |cos h|)
                pc.BoxOf(m, out var x, out var y, out var hd, out var bw, out var bd);
                float h = hd * Mathf.Deg2Rad, sh = Mathf.Abs(Mathf.Sin(h)), ch = Mathf.Abs(Mathf.Cos(h));
                float tUp = Mathf.Min(sh > 1e-3f ? bw / 2 / sh : float.MaxValue, ch > 1e-3f ? bd / 2 / ch : float.MaxValue);
                var spt = viewer.MapToScreen(new Vector2(x, y - tUp));
                float sx = spt.x, top = spt.y;
                // знамя (Banners) стоит в строю и торчит вверх на ~27 px — табличка над ним, а не на нём
                if (!m.Fleeing || m.RallyPending) top = Mathf.Max(top, viewer.MapToScreen(Journal.Viewer.Banners.Base(x, y, hd, Mathf.Min(bd, (float)m.P.Fp.Depth))).y + Journal.Viewer.Banners.TopPx(ppm) + 2);
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
                bool fighting = FightingNow(m);
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

        // ── сводка хода справа: потери сторон, главные события (щелчок — камера к отряду), сырой журнал — под «Подробно» ──
        void Log(BattleSession s)
        {
            int key = pc.Summaries.Count * 1000 + s.Logs.Count;
            if (logShown == key) return;
            logShown = key;
            summaryBox.Clear(); detailsScroll.Clear();
            var sum = pc.LastSummary;
            detailsToggle.EnableInClassList("hidden", s.Logs.Count == 0);
            if (sum == null) { logTitle.text = "Итогов пока нет"; summaryBox.Add(Line("Отдайте приказы и жмите «Ход!» — здесь будут потери и главные события хода.", "sum-hint")); return; }
            logTitle.text = $"Итоги хода {sum.Turn}";
            foreach (var side in s.Sides)
            {
                if (!sum.Sides.TryGetValue(side, out var v)) continue;
                var row = new VisualElement(); row.AddToClassList("sum-side");
                var stripe = new VisualElement(); stripe.AddToClassList("sum-stripe"); stripe.AddToClassList("side-" + side); row.Add(stripe);
                var name = new Label(s.Name(side)); name.AddToClassList("sum-side-name"); row.Add(name);
                var loss = new Label(v.lost >= 1 ? $"−{v.lost:0}" : "без потерь"); loss.AddToClassList("sum-loss"); loss.EnableInClassList("is-none", v.lost < 1); row.Add(loss);
                summaryBox.Add(row);
                // убитых и раненых стол пишет, когда окно удара закрылось; схватка идёт дальше — часть выбывших ещё без итога
                double open = v.lost - v.killed - v.wounded;
                if (v.lost >= 1) summaryBox.Add(Line($"убито {v.killed:0} · ранено {v.wounded:0}" + (open >= 1 ? $" · {open:0} — итог после схватки" : "") + $" · в строю {v.now:0}", "sum-sub"));
                else summaryBox.Add(Line($"в строю {v.now:0}", "sum-sub"));
            }
            int shown = 0;
            foreach (var e in sum.Events)
            {
                if (shown++ >= 6) break;
                var row = new VisualElement(); row.AddToClassList("sum-event"); row.AddToClassList("side-" + e.Side);
                var ic = new Icon(e.Icon); ic.AddToClassList("sum-icon"); row.Add(ic);
                var t = new Label(e.Text); t.AddToClassList("sum-text"); row.Add(t);
                var m = e.Unit;
                if (m != null) { row.RegisterCallback<ClickEvent>(_ => { pc.FocusOn(m); pc.Select(m); }); row.tooltip = "Показать отряд"; }
                summaryBox.Add(row);
            }
            if (sum.Events.Count == 0) summaryBox.Add(Line("Ход прошёл без боя: отряды шли и стояли.", "sum-hint"));
            if (sum.Arrows > 0) summaryBox.Add(Line($"Стрел за ход: {sum.Arrows}, выбыло от них: {sum.ArrowHits}", "sum-sub"));
            if (s.Logs.Count > 0) foreach (var line in s.Logs[s.Logs.Count - 1]) detailsScroll.Add(Line(line, "log-line"));
        }
        static Label Line(string text, string cls) { var l = new Label(text); l.AddToClassList(cls); return l; }

        // ── лента событий во время хода: по мере показа — бегство, сплочение, уход, новые схватки и натиск ──
        void Feed()
        {
            // отжившие — гаснут и уходят
            foreach (var it in feed.Children().ToList())
            {
                float born = (float)it.userData, age = Time.time - born;
                if (age > 6) it.RemoveFromHierarchy();
                else it.style.opacity = Mathf.Clamp01((6 - age) / 1.2f);
            }
            var rec = viewer?.Rec;
            if (rec == null || rec.Frames.Count == 0) return;
            int frame = Mathf.Clamp((int)System.Math.Floor(viewer.T / rec.Dt), 0, rec.Frames.Count - 1);
            if (feedFrame < 0 || pc.Phase != PlayPhase.Showing) { feedFrame = frame; return; }
            var movers = pc.Battle.Movers;
            for (int f = feedFrame + 1; f <= frame; f++)
            {
                for (int ui = 0; ui < rec.Units.Count && ui < movers.Count; ui++)
                {
                    var L = rec.States?[ui]; if (L == null) continue;
                    for (int i = 0; i < L.Count; i += 2)
                    {
                        if (L[i] != f) continue;
                        var m = movers[ui]; string n = $"«{m.P.U.Name}»";
                        switch (L[i + 1])
                        {
                            case 1: Toast("flee", $"{n} бежит!", m); break;
                            case 2: Toast("gone", $"{n} ушёл с поля", m); break;
                            case 3: Toast("rally", $"{n}: сплачивают", m); break;
                            case 4: Toast("rally", $"{n} сплотился!", m); break;
                        }
                    }
                }
                if (f < rec.Fights.Count)
                {
                    var now = rec.Fights[f]; var before = f > 0 ? rec.Fights[f - 1] : System.Array.Empty<int>();
                    for (int q = 0; q + 1 < now.Length; q += 2)
                    {
                        int a = now[q], b = now[q + 1];
                        bool was = false; for (int k = 0; k + 1 < before.Length && !was; k += 2) was = before[k] == a && before[k + 1] == b;
                        long key = (long)Mathf.Min(a, b) << 32 | (uint)Mathf.Max(a, b);
                        if (was || (pairShown.TryGetValue(key, out var tShown) && (float)viewer.T - tShown < 10)) continue;
                        pairShown[key] = (float)viewer.T;
                        if (a >= movers.Count || b >= movers.Count) continue;
                        var A = movers[a]; var B = movers[b];
                        // натиск — из записи (бой в это время считает другой поток)
                        bool charge = rec.Charges.TryGetValue(key, out var by);
                        var C = charge ? movers[by] : A; var D = charge ? (by == a ? B : A) : B;
                        Toast(charge ? "charge" : "fight", charge ? $"Натиск «{C.P.U.Name}» на «{D.P.U.Name}»!" : $"«{A.P.U.Name}» и «{B.P.U.Name}» сошлись", C);
                    }
                }
            }
            feedFrame = frame;
        }
        void Toast(string icon, string text, Mover m)
        {
            var row = new VisualElement(); row.AddToClassList("feed-row"); row.AddToClassList("side-" + PlayController.SideOf(m));
            var ic = new Icon(icon); ic.AddToClassList("feed-icon"); row.Add(ic);
            var t = new Label(text); t.AddToClassList("feed-text"); row.Add(t);
            row.userData = Time.time;
            row.RegisterCallback<ClickEvent>(_ => pc.FocusOn(m));
            feed.Insert(0, row);
            while (feed.childCount > 5) feed.RemoveAt(feed.childCount - 1);
        }
    }
}
