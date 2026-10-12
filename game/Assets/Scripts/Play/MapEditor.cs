// ═══════════ MapEditor.cs — редактор карт в игре (Г102): кисть, ластик, пути, участки, постройки, генераторы ═══════════
// Раскладка — как в Cities: Skylines (Алекс 11.10: «описание инструментов не user friendly»): внизу — полоса инструментов
// значками с подписями и клавишами 1…9; над ней — карточка выбранного инструмента: его настройки и подсказки клавишами;
// справа сверху — карточка выбранного объекта; слева — карта (файл) и генератор. Esc — отмена, а если отменять нечего —
// меню редактора (продолжить, сохранить, испытать, выйти). ПКМ по объекту в «Правке» — его меню (перегенерировать,
// разобрать на дома, башни в точки, удалить).
// Инструменты: Кисть — вид местности по клеткам 5 м; Ластик — стереть кисть, высоту, постройки, точки линий и участков;
// Высота — уровни 0…3; Заливка — связная область; Путь — стена (башни на углах и через N м, или в каждой точке), частокол,
// река, дорога, ров, окоп; Участок — город, деревня, донжон, лес, поле, холм…; Ворота — щелчок по стене; Постройки —
// дом, амбар, церковь, ратуша, ряды, колодец, башня… поштучно; Правка — тянуть точки и объекты.
// Карты — файлы .map.json в Saves/Maps (сами, без вопросов «куда»); «Испытать в бою» — бой своими армиями на этой карте.
// Камера — колесо, средняя кнопка, WASD. Ctrl+Z / Ctrl+Y — отменить / вернуть, Ctrl+S — сохранить, Enter — закончить путь.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleCore;
using Journal.Maps;
using Journal.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Terrain = BattleCore.Terrain;

namespace Journal.Play
{
    public sealed class MapEditor
    {
        enum Tool { Brush, Eraser, Height, Fill, Path, Plot, Gate, Prop, Edit }
        static readonly (Tool T, string Name, string Icon, string Tip)[] Tools =
        {
            (Tool.Brush, "Кисть", "t-brush", "Рисовать вид местности: трава, лес, вода, скалы…"),
            (Tool.Eraser, "Ластик", "t-eraser", "Стереть: местность — в траву, высоту — в ноль, постройки и точки линий"),
            (Tool.Height, "Высота", "t-height", "Поднять или опустить землю: уровни 0…3"),
            (Tool.Fill, "Заливка", "t-fill", "Залить связную область одного вида"),
            (Tool.Path, "Стены и дороги", "t-path", "Стена, частокол, река, дорога, ров, окоп — ломаной"),
            (Tool.Plot, "Участки", "t-plot", "Город, деревня, лес, поле, холм… — обводкой"),
            (Tool.Gate, "Ворота", "t-gate", "Поставить или убрать ворота в стене"),
            (Tool.Prop, "Постройки", "t-prop", "Дом, амбар, церковь, ратуша, ряды, колодец, башня — по одной"),
            (Tool.Edit, "Правка", "t-edit", "Выбрать объект, тянуть точки и объекты; ПКМ — меню объекта"),
        };
        public static string MapsDir => Path.Combine(Journal.Armies.ArmyFile.SavesDir, "Maps");

        readonly PlayController pc; readonly BattleViewer viewer; readonly VisualElement uiRoot;
        readonly VisualElement root, opts, genParams, filePopup, toolbar, optCard, objCard, objBody, menu, ctx, genBox; readonly ScrollView panel;
        readonly Label status, toast, optTitle; readonly TextField nameField; readonly IntegerField wField, hField, seedField;
        readonly Dictionary<Tool, Button> toolBtn = new Dictionary<Tool, Button>();
        public bool Visible => !root.ClassListContains("hidden");
        public event Action Closed;
        public event Action<string> Test;   // файл карты — к бою своими армиями
        public SettingsPanel Settings;      // настройки игры — из меню редактора

        MapDoc doc; string file; bool dirty; TerrainMap baked;
        Tool tool = Tool.Brush; byte brushT = 6; int brushZ = 1, brushR = 2, hillLevel = 1; string pathKind = "wall", plotKind = "town", propKind = "house"; bool closeRing = true; int propRot;
        bool eraseGround = true, eraseHeight = true, eraseProps = true, eraseLines;
        readonly List<double[]> drawing = new List<double[]>();
        MapFeature sel; int dragPt = -1; bool dragAll, propsPushed; double dragX, dragY; List<double[]> dragOrig;
        readonly List<MapDoc> undo = new List<MapDoc>(), redo = new List<MapDoc>();
        bool needBake, painting; float bakeAt, toastUntil; double lastX = double.NaN, lastY = double.NaN; float lastClick; Vector2 lastClickPos;
        string genId = "x-castle"; readonly Dictionary<string, object> genInput = new Dictionary<string, object>(); uint genSeed = 7;
        // разметка поверх карты
        readonly Mesh mesh; readonly GameObject overlayGo;
        readonly List<Vector3> V = new List<Vector3>(); readonly List<Color32> C = new List<Color32>(); readonly List<int> I = new List<int>();
        float ppm = 1;

        public MapEditor(VisualElement parent, PlayController pc, BattleViewer viewer)
        {
            this.pc = pc; this.viewer = viewer; uiRoot = parent;
            root = new VisualElement(); root.AddToClassList("me-root"); root.AddToClassList("hidden"); root.pickingMode = PickingMode.Ignore; parent.Add(root);
            // ── слева: карта и генератор ──
            panel = new ScrollView(ScrollViewMode.Vertical); panel.AddToClassList("panel"); panel.AddToClassList("me-panel"); root.Add(panel);
            panel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var title = new Label("Редактор карт"); title.AddToClassList("me-title"); panel.Add(title);
            nameField = new TextField("Название"); nameField.AddToClassList("me-field");
            nameField.RegisterValueChangedCallback(e => { if (doc != null && doc.Name != e.newValue) { doc.Name = e.newValue; dirty = true; } });
            panel.Add(nameField);
            var files = Row(panel);
            Btn(files, "Новая", NewMap).tooltip = "Пустая карта размером, что ниже";
            Btn(files, "Открыть ▾", ToggleOpen).tooltip = "Карты из папки игры Saves/Maps";
            Btn(files, "Сохранить", () => Save(false)).tooltip = "В папку игры Saves/Maps под названием карты (Ctrl+S)";
            Btn(files, "Копия", () => Save(true)).tooltip = "Сохранить ещё одним файлом рядом";
            filePopup = new VisualElement(); filePopup.AddToClassList("me-files"); filePopup.AddToClassList("hidden"); panel.Add(filePopup);
            var size = Row(panel);
            wField = new IntegerField("Новая, м") { value = 1500 }; wField.AddToClassList("me-num"); size.Add(wField);
            hField = new IntegerField("×") { value = 1000 }; hField.AddToClassList("me-num"); hField.AddToClassList("me-num-short"); size.Add(hField);

            var gf = new Foldout { text = "Генератор", value = true }; gf.AddToClassList("me-fold"); panel.Add(gf);
            genBox = gf.contentContainer;
            var gens = MapMakers.All().ToList();
            var genDd = UiKit.Tip(new DropdownField("Шаблон", gens.Select(g => g.Name).ToList(), gens.FindIndex(g => g.Id == genId))); genDd.AddToClassList("me-field");
            genDd.RegisterValueChangedCallback(e => { int i = genDd.index; if (i >= 0) { genId = gens[i].Id; genInput.Clear(); BuildGenParams(); } });
            genBox.Add(genDd);
            genParams = new VisualElement(); genParams.AddToClassList("me-gen"); genBox.Add(genParams);
            var seedRow = Row(genBox);
            seedField = new IntegerField("Зерно") { value = (int)genSeed }; seedField.AddToClassList("me-num"); seedField.RegisterValueChangedCallback(e => genSeed = (uint)Math.Max(1, e.newValue)); seedRow.Add(seedField);
            Btn(seedRow, "🎲", () => { genSeed = (uint)UnityEngine.Random.Range(1, 999999); seedField.SetValueWithoutNotify((int)genSeed); }).tooltip = "Случайное зерно";
            var make = Btn(genBox, "Создать карту", Generate); make.AddToClassList("me-make");

            var sp = new VisualElement(); sp.style.flexGrow = 1; panel.Add(sp);
            var test = Btn(panel, "⚔  Испытать в бою", TestBattle); test.AddToClassList("me-test"); test.tooltip = "Сохранить и начать бой своими армиями на этой карте";
            var exit = Btn(panel, "Выйти из редактора", () => Close(false)); exit.AddToClassList("me-exit"); exit.tooltip = "В главное меню (Esc — меню редактора)";
            toast = new Label(); toast.AddToClassList("me-toast"); panel.Add(toast);
            status = new Label(); status.AddToClassList("me-status"); panel.Add(status);

            // ── внизу: карточка инструмента над полосой инструментов ──
            var bottom = new VisualElement(); bottom.AddToClassList("me-bottom"); bottom.pickingMode = PickingMode.Ignore; root.Add(bottom);
            optCard = new VisualElement(); optCard.AddToClassList("panel"); optCard.AddToClassList("me-optcard"); bottom.Add(optCard);
            optTitle = new Label(); optTitle.AddToClassList("me-opt-title"); optCard.Add(optTitle);
            opts = new VisualElement(); opts.AddToClassList("me-opts"); optCard.Add(opts);
            toolbar = new VisualElement(); toolbar.AddToClassList("panel"); toolbar.AddToClassList("me-toolbar"); bottom.Add(toolbar);
            int hot = 1;
            foreach (var (t, name, icon, tip) in Tools)
            {
                var tt = t; var b = new Button(() => SetTool(tt)); b.AddToClassList("me-tbtn"); b.tooltip = $"{tip} · клавиша {hot}";
                var ic = new Icon(icon); ic.AddToClassList("me-tbtn-icon"); b.Add(ic);
                var l = new Label(name); l.AddToClassList("me-tbtn-name"); b.Add(l);
                var k = new Label(hot.ToString()); k.AddToClassList("me-tbtn-key"); b.Add(k);
                toolbar.Add(b); toolBtn[t] = b; hot++;
            }
            var sep = new VisualElement(); sep.AddToClassList("me-tsep"); toolbar.Add(sep);
            foreach (var (icon, name, act, tip) in new (string, string, Action, string)[] { ("undo", "Отменить", Undo, "Ctrl+Z"), ("redo", "Вернуть", Redo, "Ctrl+Y"), ("menu", "Меню", ToggleMenu, "Esc") })
            {
                var b = new Button(act); b.AddToClassList("me-tbtn"); b.AddToClassList("is-small"); b.tooltip = $"{name} · {tip}";
                var ic = new Icon(icon); ic.AddToClassList("me-tbtn-icon"); b.Add(ic);
                var l = new Label(name); l.AddToClassList("me-tbtn-name"); b.Add(l);
                toolbar.Add(b);
            }
            // ── справа: выбранный объект ──
            objCard = new VisualElement(); objCard.AddToClassList("panel"); objCard.AddToClassList("me-objcard"); objCard.AddToClassList("hidden"); root.Add(objCard);
            objBody = new VisualElement(); objCard.Add(objBody);
            // ── меню по ПКМ и меню редактора ──
            ctx = new VisualElement(); ctx.AddToClassList("panel"); ctx.AddToClassList("me-ctx"); ctx.AddToClassList("hidden"); root.Add(ctx);
            menu = new VisualElement(); menu.AddToClassList("me-menu-veil"); menu.AddToClassList("hidden"); root.Add(menu);

            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.MarkDynamic();
            overlayGo = new GameObject("Редактор карт: разметка");
            overlayGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = overlayGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = new Material(sh); mr.sortingOrder = 60;
            overlayGo.SetActive(false);
            BuildGenParams(); SetTool(Tool.Brush);
        }

        // ── мелочи интерфейса ──
        static VisualElement Row(VisualElement p) { var r = new VisualElement(); r.AddToClassList("me-row"); p.Add(r); return r; }
        static Button Btn(VisualElement p, string text, Action act) { var b = new Button(act) { text = text }; b.AddToClassList("lineup-mini"); b.AddToClassList("me-btn"); p.Add(b); return b; }
        static Label Note(VisualElement p, string text) { var n = new Label(text); n.AddToClassList("me-note"); p.Add(n); return n; }
        // подсказка клавишей: [ЛКМ] рисовать
        static void Keys(VisualElement p, params (string key, string what)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("me-keys"); p.Add(row);
            foreach (var (k, w) in items)
            {
                var it = new VisualElement(); it.AddToClassList("me-key-item"); row.Add(it);
                var kl = new Label(k); kl.AddToClassList("me-key"); it.Add(kl);
                var wl = new Label(w); wl.AddToClassList("me-key-what"); it.Add(wl);
            }
        }
        void Say(string s) { toast.text = s; toastUntil = Time.unscaledTime + 4; }
        // в поле ввода печатают — клавиши карте не нужны
        public bool Typing
        {
            get
            {
                var f = uiRoot.panel?.focusController?.focusedElement as VisualElement;
                for (var e = f; e != null; e = e.parent) if (e.ClassListContains("unity-base-text-field")) return true;
                return false;
            }
        }
        bool OverPanel(Vector2 screen)
        {
            var p = uiRoot.panel; if (p == null) return false;
            var picked = p.Pick(RuntimePanelUtils.ScreenToPanel(p, new Vector2(screen.x, Screen.height - screen.y)));
            return picked != null && picked != root && root.Contains(picked);
        }
        bool MenuOpen => !menu.ClassListContains("hidden");

        // ── открыть, закрыть ──
        public void Show(MapDoc d = null)
        {
            if (d != null) { doc = d; file = null; dirty = false; }
            else if (doc == null) { doc = MapMakers.Make("x-castle", null, genSeed); file = null; dirty = false; }
            undo.Clear(); redo.Clear(); drawing.Clear(); sel = null;
            nameField.SetValueWithoutNotify(doc.Name);
            root.RemoveFromClassList("hidden"); overlayGo.SetActive(true); menu.AddToClassList("hidden"); ctx.AddToClassList("hidden");
            savedInsets = viewer.Insets; FitInsets();
            RefreshOpts(); Bake(true);
            root.schedule.Execute(() => { FitInsets(); viewer.ShowMap(baked, doc.Name, true); }).ExecuteLater(60);   // раскладка панелей готова — кадр по ней
        }
        public void Hide() { root.AddToClassList("hidden"); overlayGo.SetActive(false); mesh.Clear(); filePopup.AddToClassList("hidden"); viewer.Insets = savedInsets; }
        // кадр «вся карта» — справа от панели и над полосой инструментов: их размер в пикселях экрана
        Vector4 savedInsets;
        void FitInsets()
        {
            var vt = uiRoot.panel?.visualTree; float w = vt != null && vt.layout.width > 0 ? vt.layout.width : 1600, k = Screen.width / w;
            float right = float.IsNaN(panel.worldBound.xMax) || panel.worldBound.xMax <= 0 ? 340 : panel.worldBound.xMax;
            float bottom = vt != null && !float.IsNaN(toolbar.worldBound.yMin) && toolbar.worldBound.yMin > 0 ? vt.layout.height - toolbar.worldBound.yMin : 96;
            viewer.Insets = new Vector4(right * k + 12, 0, 0, bottom * k + 8);
        }
        bool exitAsked;
        void Close(bool force)
        {
            if (dirty && !force && !exitAsked) { exitAsked = true; ShowMenu(true); return; }
            exitAsked = false; menu.AddToClassList("hidden");
            Hide(); pc.ReShow(); Closed?.Invoke();
        }

        void Bake(bool fit)
        {
            if (fit) FitInsets();
            baked = doc.Bake();
            viewer.ShowMap(baked, doc.Name, fit);
            needBake = false; bakeAt = Time.unscaledTime;
        }
        void Push()
        {
            undo.Add(doc.Clone()); if (undo.Count > 40) undo.RemoveAt(0);
            redo.Clear(); dirty = true; exitAsked = false;
        }
        void Undo()
        {
            if (undo.Count == 0) { Say("Отменять нечего"); return; }
            redo.Add(doc.Clone()); doc = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            AfterSwap();
        }
        void Redo()
        {
            if (redo.Count == 0) { Say("Возвращать нечего"); return; }
            undo.Add(doc.Clone()); doc = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            AfterSwap();
        }
        void AfterSwap() { Select(null); drawing.Clear(); dirty = true; nameField.SetValueWithoutNotify(doc.Name); RefreshOpts(); Bake(false); }

        // ── меню редактора (Esc) ──
        void ToggleMenu() { if (MenuOpen) menu.AddToClassList("hidden"); else ShowMenu(false); }
        void ShowMenu(bool leaving)
        {
            menu.Clear(); ctx.AddToClassList("hidden");
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("me-menu"); menu.Add(box);
            var t = new Label(leaving ? "Выйти из редактора?" : "Меню редактора"); t.AddToClassList("me-menu-title"); box.Add(t);
            if (dirty) Note(box, "Есть несохранённые правки" + (file != null ? $" в «{Path.GetFileName(file)}»" : " — карта ещё не сохранена") + ".");
            Button M(string text, Action act, string cls = null) { var b = new Button(() => { act(); }) { text = text }; b.AddToClassList("me-menu-btn"); if (cls != null) b.AddToClassList(cls); box.Add(b); return b; }
            M("Продолжить", () => { exitAsked = false; menu.AddToClassList("hidden"); });
            M("Сохранить", () => { Save(false); menu.AddToClassList("hidden"); });
            M("⚔  Испытать в бою", () => { menu.AddToClassList("hidden"); TestBattle(); }, "is-test");
            if (Settings != null) M("Настройки", () => { menu.AddToClassList("hidden"); Settings.Show(); });
            if (dirty) { M("Сохранить и выйти", () => { Save(false); if (!dirty) Close(true); }); M("Выйти без сохранения", () => Close(true), "is-exit"); }
            else M("Выйти в главное меню", () => Close(true), "is-exit");
            menu.RemoveFromClassList("hidden");
        }

        // ── файлы: всегда в Saves/Maps игры, без окна «куда» (Алекс 11.10) ──
        void NewMap()
        {
            Push();
            doc = MapDoc.Blank(Mathf.Clamp(wField.value, 200, 7000), Mathf.Clamp(hField.value, 200, 7000));
            file = null; AfterSwap(); Bake(true);
            Say($"Новая карта {doc.WM:0} × {doc.HM:0} м");
        }
        static string Safe(string name)
        {
            var s = new string((name ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            return s.Length == 0 ? "карта" : s;
        }
        void Save(bool asNew)
        {
            try
            {
                Directory.CreateDirectory(MapsDir);
                if (file == null || asNew)
                {
                    string b = Path.Combine(MapsDir, Safe(doc.Name)), f = b + ".map.json";
                    for (int k = 2; File.Exists(f) && (asNew || file == null); k++) f = $"{b} {k}.map.json";
                    file = f;
                }
                File.WriteAllText(file, doc.ToJson());
                dirty = false; exitAsked = false;
                Say("Сохранено: Saves/Maps/" + Path.GetFileName(file));
            }
            catch (Exception e) { Say("Не сохранилось: " + e.Message); }
        }
        void ToggleOpen()
        {
            if (!filePopup.ClassListContains("hidden")) { filePopup.AddToClassList("hidden"); return; }
            filePopup.Clear();
            var list = Directory.Exists(MapsDir) ? Directory.GetFiles(MapsDir, "*.map.json").OrderByDescending(File.GetLastWriteTime).ToList() : new List<string>();
            if (list.Count == 0) Note(filePopup, "Сохранённых карт ещё нет (папка Saves/Maps).");
            foreach (var f in list) { var ff = f; Btn(filePopup, Path.GetFileName(f).Replace(".map.json", ""), () => Load(ff)).AddToClassList("me-file"); }
            Btn(filePopup, "Из другой папки…", () =>
            {
                var f = FileDialog.Open(Directory.Exists(MapsDir) ? MapsDir : null, "Карта редактора", "Карта (*.json)", "json");
                if (f != null) Load(f);
            });
            filePopup.RemoveFromClassList("hidden");
        }
        void Load(string f)
        {
            try
            {
                var d = MapDoc.FromJson(File.ReadAllText(f));
                Push(); doc = d; file = f; AfterSwap(); dirty = false; Bake(true);
                filePopup.AddToClassList("hidden");
                Say("Открыта: " + Path.GetFileName(f));
            }
            catch (Exception e) { Say("Не открылась: " + e.Message); }
        }
        void TestBattle()
        {
            if (dirty || file == null) Save(false);
            if (file == null) return;
            Hide(); pc.ReShow(); Test?.Invoke(file);
        }

        // ── генератор ──
        void BuildGenParams()
        {
            genParams.Clear();
            var t = MapMakers.Get(genId); if (t == null) return;
            foreach (var d in t.Params)
            {
                var dd = d;
                if (d.Type == "number")
                {
                    var f = new IntegerField(d.Name) { value = (int)Convert.ToDouble(genInput.TryGetValue(d.Key, out var v) ? v : d.Def) }; f.AddToClassList("me-field");
                    f.RegisterValueChangedCallback(e => genInput[dd.Key] = (double)Mathf.Clamp(e.newValue, (float)dd.Min, (float)dd.Max));
                    genParams.Add(f);
                }
                else if (d.Type == "bool")
                {
                    var f = new Toggle(d.Name) { value = genInput.TryGetValue(d.Key, out var v) ? (bool)v : (bool)d.Def }; f.AddToClassList("me-field");
                    f.RegisterValueChangedCallback(e => genInput[dd.Key] = e.newValue);
                    genParams.Add(f);
                }
                else
                {
                    string cur = genInput.TryGetValue(d.Key, out var v) ? (string)v : (string)d.Def;
                    var f = UiKit.Tip(new DropdownField(d.Name, d.Options.Select(o => o[1]).ToList(), Array.FindIndex(d.Options, o => o[0] == cur))); f.AddToClassList("me-field");
                    f.RegisterValueChangedCallback(e => { int i = f.index; if (i >= 0) genInput[dd.Key] = dd.Options[i][0]; });
                    genParams.Add(f);
                }
            }
        }
        void Generate()
        {
            try
            {
                var d = MapMakers.Make(genId, genInput, genSeed);
                Push(); doc = d; file = null; AfterSwap(); Bake(true);
                Say($"Создано: {d.Name}, {d.WM:0} × {d.HM:0} м, объектов {d.Features.Count}. Всё можно править.");
            }
            catch (Exception e) { Debug.LogException(e); Say("Не вышло: " + e.Message); }
        }

        // ── инструменты ──
        void SetTool(Tool t)
        {
            if (tool != t) drawing.Clear();
            tool = t;
            foreach (var kv in toolBtn) kv.Value.EnableInClassList("is-on", kv.Key == t);
            RefreshOpts();
        }
        void Select(MapFeature f)
        {
            sel = f; RefreshObj();
        }
        void RefreshOpts()
        {
            opts.Clear(); propsPushed = false;
            var info = Tools.First(x => x.T == tool);
            optTitle.text = info.Name + (tool == Tool.Brush || tool == Tool.Fill ? " · " + Terrain.NameOf(brushT) : tool == Tool.Prop ? " · " + MapFeature.PropKinds[MapFeature.PropIndex(propKind)].Name : "");
            switch (tool)
            {
                case Tool.Brush:
                    Palette(); SizeSlider();
                    Keys(opts, ("ЛКМ", "рисовать"), ("Shift+ЛКМ", "прямой линией"), ("[  ]", "меньше / больше"));
                    break;
                case Tool.Eraser:
                {
                    var r = Row(opts);
                    Check(r, "местность", eraseGround, v => eraseGround = v, "Вид местности — обратно в траву");
                    Check(r, "высоту", eraseHeight, v => eraseHeight = v, "Высоту — в ноль");
                    Check(r, "постройки", eraseProps, v => eraseProps = v, "Постройки, чья середина под ластиком");
                    Check(r, "точки линий и участков", eraseLines, v => eraseLines = v, "Точки стен, дорог, участков под ластиком; осталось мало точек — объект целиком");
                    SizeSlider();
                    Keys(opts, ("ЛКМ", "стирать"), ("[  ]", "меньше / больше"), ("Del", "удалить выбранный объект"));
                    break;
                }
                case Tool.Fill:
                    Palette();
                    Keys(opts, ("ЛКМ", "залить связную область одного вида"));
                    break;
                case Tool.Height:
                {
                    var r = Row(opts);
                    for (int z = 0; z <= Terrain.MaxHeight; z++) { int zz = z; var b = Btn(r, z == 0 ? "0 — низ" : $"{z} — ~{z * 3} м", () => { brushZ = zz; RefreshOpts(); }); b.EnableInClassList("is-on", brushZ == z); }
                    SizeSlider();
                    Keys(opts, ("ЛКМ", "поднять / опустить до уровня"), ("[  ]", "меньше / больше"));
                    Note(opts, "Холм с ровными склонами — «Участки» → «Холм».");
                    break;
                }
                case Tool.Path:
                {
                    var kinds = MapFeature.PathKinds;
                    var r = Row(opts);
                    foreach (var (kind, name) in kinds) { var kk = kind; var b = Btn(r, name, () => { pathKind = kk; RefreshOpts(); }); b.EnableInClassList("is-on", pathKind == kind); }
                    var r2 = Row(opts);
                    Check(r2, "замкнуть в кольцо", closeRing, v => closeRing = v, "Стена, частокол, ров — кольцом: последняя точка соединится с первой");
                    Keys(opts, ("ЛКМ", "точка"), ("ПКМ / Backspace", "убрать последнюю"), ("Enter / двойной щелчок", "готово"), ("Esc", "отмена"));
                    if (pathKind == "wall" || pathKind == "palisade") Note(opts, "Башни — в каждой точке: тянешь точку в «Правке» — тянешь башню.");
                    break;
                }
                case Tool.Plot:
                {
                    var kinds = MapFeature.PlotKinds;
                    var r = Row(opts);
                    foreach (var (kind, name) in kinds) { var kk = kind; var b = Btn(r, name, () => { plotKind = kk; RefreshOpts(); }); b.EnableInClassList("is-on", plotKind == kind); }
                    if (plotKind == "hill") { var s = new SliderInt("Высота холма", 1, Terrain.MaxHeight) { value = hillLevel, showInputField = true }; s.AddToClassList("me-field"); s.RegisterValueChangedCallback(e => hillLevel = e.newValue); opts.Add(s); }
                    Keys(opts, ("ЛКМ", "точка обводки"), ("Enter / двойной щелчок", "замкнуть"), ("ПКМ", "убрать последнюю"), ("Esc", "отмена"));
                    if (plotKind == "town" || plotKind == "village") Note(opts, "Улицы и дома раскладываются сами; другая раскладка — ПКМ по участку в «Правке».");
                    break;
                }
                case Tool.Gate:
                    Keys(opts, ("ЛКМ по стене", "поставить ворота"), ("ЛКМ по воротам", "убрать"));
                    Note(opts, "Проём 10 м, у каменной стены — надвратные башни; у самого угла ворота сдвигаются на прямой участок.");
                    break;
                case Tool.Prop:
                {
                    var r = Row(opts); r.AddToClassList("me-props");
                    foreach (var k in MapFeature.PropKinds)
                    {
                        var id = k.Id; var b = Btn(r, k.Name, () => { propKind = id; RefreshOpts(); });
                        b.EnableInClassList("is-on", propKind == id); b.tooltip = $"{k.W * 5} × {k.H * 5} м" + (k.Cell == null ? " · не мешает проходу" : "");
                    }
                    Keys(opts, ("ЛКМ", "поставить"), ("R", "повернуть"), ("ПКМ", "убрать постройку под курсором"));
                    break;
                }
                case Tool.Edit:
                    Keys(opts, ("ЛКМ", "выбрать, тянуть точку или объект"), ("Shift+ЛКМ по линии", "новая точка"), ("ПКМ по точке", "убрать точку"), ("T над точкой стены", "снять / вернуть башню"), ("ПКМ по объекту", "меню объекта"), ("Del", "удалить"));
                    break;
            }
            RefreshObj();
        }
        void Check(VisualElement p, string name, bool v, Action<bool> set, string tip)
        {
            var t = new Toggle(name) { value = v }; t.AddToClassList("me-check"); t.tooltip = tip; t.RegisterValueChangedCallback(e => set(e.newValue)); p.Add(t);
        }
        void Palette()
        {
            var box = new VisualElement(); box.AddToClassList("me-palette"); opts.Add(box);
            foreach (var t in Terrain.Types.Where(t => !t.System))
            {
                var id = (byte)t.Id;
                // образцы квадратиками, имя — подсказкой и в заголовке карточки (полоса не заслоняет карту)
                var b = new Button(() => { brushT = id; RefreshOpts(); }); b.AddToClassList("me-swatch-btn"); b.EnableInClassList("is-on", brushT == id);
                var sw = new VisualElement(); sw.AddToClassList("me-swatch"); sw.style.backgroundColor = (Color)BattleViewer.GroundColor(t.Id); b.Add(sw);
                b.tooltip = $"{t.Name} · {t.Group}"; box.Add(b);
            }
        }
        SliderInt sizeSlider;
        void SizeSlider()
        {
            var s = new SliderInt("Размер, клеток", 1, 20) { value = brushR, showInputField = true }; s.AddToClassList("me-field"); s.AddToClassList("me-size");
            s.RegisterValueChangedCallback(e => brushR = e.newValue); opts.Add(s); sizeSlider = s;
        }

        // ── справа: карточка выбранного объекта ──
        void RefreshObj()
        {
            objBody.Clear();
            objCard.EnableInClassList("hidden", sel == null);
            if (sel == null) return;
            var h = new Label(sel.Title + (sel.IsPath ? $" · {sel.Length():0} м" : "")); h.AddToClassList("me-sel"); objBody.Add(h);
            void Edited() { if (!propsPushed) { Push(); propsPushed = true; } needBake = true; dirty = true; }
            if (sel.IsWall)
            {
                var pt = new Toggle("Башня в каждой точке") { value = sel.PtTowers }; pt.AddToClassList("me-field");
                pt.tooltip = "Каждая точка стены — башня: тянешь точку — тянешь башню; Shift+ЛКМ по стене — новая башня";
                pt.RegisterValueChangedCallback(e => { Edited(); if (e.newValue) doc.TowersToPoints(sel); else { sel.PtTowers = false; sel.Every = 0; } Bake(false); RefreshObj(); });
                objBody.Add(pt);
                if (!sel.PtTowers)
                {
                    var every = new SliderInt("Башни через, м (0 — без)", 0, 150) { value = sel.E < 0 ? 0 : (int)sel.E, showInputField = true }; every.AddToClassList("me-field");
                    every.RegisterValueChangedCallback(e => { Edited(); sel.Every = e.newValue == 0 ? -1 : e.newValue; }); objBody.Add(every);
                }
                Note(objBody, $"Ворот: {sel.Gates.Count} (инструмент «Ворота»)");
            }
            if (sel.IsPath)
            {
                if (!sel.IsWall)
                {
                    var w = new SliderInt("Ширина, м", 5, sel.Kind == "river" ? 120 : 40) { value = (int)sel.W, showInputField = true }; w.AddToClassList("me-field");
                    w.RegisterValueChangedCallback(e => { Edited(); sel.Width = e.newValue; }); objBody.Add(w);
                }
                var ring = new Toggle("Кольцо") { value = sel.Closed }; ring.AddToClassList("me-field");
                ring.RegisterValueChangedCallback(e => { Edited(); sel.Closed = e.newValue && sel.Pts.Count > 2; }); objBody.Add(ring);
            }
            if (sel.Kind == "hill")
            {
                var s = new SliderInt("Высота", 1, Terrain.MaxHeight) { value = sel.Level, showInputField = true }; s.AddToClassList("me-field");
                s.RegisterValueChangedCallback(e => { Edited(); sel.Level = e.newValue; }); objBody.Add(s);
            }
            if (sel.IsProp)
            {
                var kinds = MapFeature.PropKinds;
                var dd = UiKit.Tip(new DropdownField("Вид", kinds.Select(k => k.Name).ToList(), MapFeature.PropIndex(sel.Prop))); dd.AddToClassList("me-field");
                dd.RegisterValueChangedCallback(e => { int i = dd.index; if (i >= 0) { Edited(); sel.Prop = kinds[i].Id; sel.PW = sel.PH = 0; Bake(false); } }); objBody.Add(dd);
                var (_, _, pw, ph) = sel.PropCells(doc.Cell);
                Note(objBody, $"{pw * 5} × {ph * 5} м");
                Btn(objBody, "Повернуть (R)", () => { Edited(); sel.Rot ^= 1; Bake(false); RefreshObj(); });
            }
            if (sel.Kind == "town" || sel.Kind == "village")
            {
                Btn(objBody, "Перегенерировать", () => Regen(sel)).tooltip = "Другая раскладка улиц и домов";
                Btn(objBody, "Разобрать на дома", () => Explode(sel)).tooltip = "Каждый дом — отдельной постройкой: тянуть, вертеть, удалять";
            }
            var del = Btn(objBody, "Удалить объект", DeleteSel); del.AddToClassList("me-danger");
        }
        void Regen(MapFeature f) { Push(); f.Seed = UnityEngine.Random.Range(1, 999999); Bake(false); Say($"{f.Title}: новая раскладка"); }
        void Explode(MapFeature f)
        {
            Push();
            var pave = new List<int>();
            var houses = doc.HousesOf(f, pave);
            byte pv = Terrain.Id("pavement"); foreach (int i in pave) doc.T[i] = pv;   // улицы остаются — кистью в основе
            int at = doc.Features.IndexOf(f); doc.Features.Remove(f); doc.Features.InsertRange(Math.Max(0, at), houses);
            // участок остаётся землёй (поле), но без раскладки: дома — сами по себе
            Select(null); Bake(false);
            Say($"Разобрано: построек {houses.Count}. Каждую можно тянуть в «Правке».");
        }
        void DeleteSel()
        {
            if (sel == null) return;
            Push(); doc.Features.Remove(sel); Select(null); Bake(false);
        }
        // меню по ПКМ на объекте
        void ShowCtx(MapFeature f, Vector2 screen)
        {
            ctx.Clear();
            var t = new Label(f.Title); t.AddToClassList("me-ctx-title"); ctx.Add(t);
            void It(string text, Action act, string cls = null) { var b = new Button(() => { ctx.AddToClassList("hidden"); act(); }) { text = text }; b.AddToClassList("me-ctx-btn"); if (cls != null) b.AddToClassList(cls); ctx.Add(b); }
            if (f.Kind == "town" || f.Kind == "village") { It(f.Kind == "town" ? "Перегенерировать город" : "Перегенерировать деревню", () => Regen(f)); It("Разобрать на дома", () => Explode(f)); }
            if (f.IsWall && !f.PtTowers) It("Башни — в точки", () => { Push(); doc.TowersToPoints(f); Bake(false); RefreshObj(); Say("Теперь каждая башня — точка: тяни"); });
            if (f.IsWall && f.PtTowers)
            {
                It("Башни только на углах", () => { Push(); MapDoc.TowersOnCorners(f); Bake(false); Say("Башни остались на углах; T над точкой — снять или вернуть"); });
                It("Башни во всех точках", () => { Push(); for (int i = 0; i < f.Pts.Count; i++) MapDoc.SetTower(f, i, true); Bake(false); });
                It("Убрать все башни", () => { Push(); for (int i = 0; i < f.Pts.Count; i++) MapDoc.SetTower(f, i, false); Bake(false); });
            }
            if (f.IsProp) It("Повернуть", () => { Push(); f.Rot ^= 1; Bake(false); });
            It("Копия рядом", () => { Push(); var c = f.Copy(); foreach (var q in c.Pts) { q[0] += 20; q[1] += 20; } c.Seed = UnityEngine.Random.Range(1, 999999); doc.Features.Add(c); Select(c); Bake(false); });
            It("Удалить", () => { Select(f); DeleteSel(); }, "me-danger");
            var p = uiRoot.panel; if (p == null) return;
            var at = RuntimePanelUtils.ScreenToPanel(p, new Vector2(screen.x, Screen.height - screen.y));
            ctx.style.left = at.x + 4; ctx.style.top = at.y + 4;
            ctx.RemoveFromClassList("hidden"); ctx.BringToFront();
        }

        // ── кадр: клавиши, мышь, сборка, разметка ──
        public void Tick()
        {
            if (!Visible) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (mouse == null) return;
            var mp = mouse.position.ReadValue();
            if (Settings != null && Settings.Visible)   // поверх — настройки: карта не слушает, Esc — назад в редактор
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { PlayerPrefs.Save(); Settings.Hide(); }
                return;
            }
            bool overUi = OverPanel(mp) || MenuOpen || mp.x < 0 || mp.y < 0 || mp.x >= Screen.width || mp.y >= Screen.height;
            var w = viewer.ScreenToMap(mp); double mx = w.x, my = w.y;
            ppm = Screen.height / (2 * viewer.Cam.orthographicSize);
            if (kb != null && !Typing) KeysInput(kb);
            if (!ctx.ClassListContains("hidden") && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) && !OverPanel(mp)) ctx.AddToClassList("hidden");
            else if (!overUi) MouseInput(mouse, kb, mx, my, mp);
            if (mouse.leftButton.wasReleasedThisFrame) EndDrag();
            if (needBake && Time.unscaledTime - bakeAt > (painting || dragPt >= 0 || dragAll ? 0.12f : 0f)) Bake(false);
            if (Time.unscaledTime > toastUntil) toast.text = "";
            StatusLine(mx, my);
            Overlay(mx, my, overUi);
        }
        void KeysInput(Keyboard kb)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (MenuOpen) { exitAsked = false; menu.AddToClassList("hidden"); }
                else if (!ctx.ClassListContains("hidden")) ctx.AddToClassList("hidden");
                else if (drawing.Count > 0) drawing.Clear();
                else if (!filePopup.ClassListContains("hidden")) filePopup.AddToClassList("hidden");
                else if (sel != null) Select(null);
                else ShowMenu(false);   // отменять нечего — меню редактора (Алекс 11.10: «выход через Esc»)
                return;
            }
            if (MenuOpen) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Finish();
            if (kb.backspaceKey.wasPressedThisFrame && drawing.Count > 0) drawing.RemoveAt(drawing.Count - 1);
            if (kb.deleteKey.wasPressedThisFrame && sel != null) DeleteSel();
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) Save(false);
            if (!ctrl)
            {
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
                for (int i = 0; i < digits.Length && i < Tools.Length; i++) if (digits[i].wasPressedThisFrame) SetTool(Tools[i].T);
                if (kb.leftBracketKey.wasPressedThisFrame) { brushR = Math.Max(1, brushR - 1); sizeSlider?.SetValueWithoutNotify(brushR); }
                if (kb.rightBracketKey.wasPressedThisFrame) { brushR = Math.Min(20, brushR + 1); sizeSlider?.SetValueWithoutNotify(brushR); }
                if (kb.tKey.wasPressedThisFrame && tool == Tool.Edit)
                {
                    var mp = Mouse.current.position.ReadValue(); var w = viewer.ScreenToMap(mp);
                    var (vf, vi) = HitVertex(w.x, w.y, 12 / ppm);
                    if (vf != null && vf.IsWall)
                    {
                        Push(); if (!vf.PtTowers) doc.TowersToPoints(vf);
                        if (vi < vf.Pts.Count) { bool off = !MapDoc.NoTower(vf.Pts[vi]); MapDoc.SetTower(vf, vi, !off); Say(off ? "Башня снята — точка осталась" : "Башня поставлена"); }
                        Bake(false);
                    }
                }
                if (kb.rKey.wasPressedThisFrame)
                {
                    if (tool == Tool.Prop) propRot ^= 1;
                    else if (sel != null && sel.IsProp) { Push(); sel.Rot ^= 1; Bake(false); RefreshObj(); }
                }
            }
        }
        TerrainMap Base() => new TerrainMap { Cell = doc.Cell, W = doc.W, H = doc.H, T = doc.T, Z = doc.Z };
        void MouseInput(Mouse mouse, Keyboard kb, double mx, double my, Vector2 mp)
        {
            bool shift = kb != null && kb.shiftKey.isPressed;
            bool down = mouse.leftButton.wasPressedThisFrame, held = mouse.leftButton.isPressed;
            switch (tool)
            {
                case Tool.Brush:
                case Tool.Height:
                case Tool.Eraser:
                    if (down)
                    {
                        Push(); painting = true;
                        if (shift && !double.IsNaN(lastX)) Paint(lastX, lastY, mx, my); else Paint(mx, my, mx, my);
                        lastX = mx; lastY = my;
                    }
                    else if (painting && held) { Paint(lastX, lastY, mx, my); lastX = mx; lastY = my; }
                    break;
                case Tool.Fill:
                    if (down)
                    {
                        Push();
                        int n = Terrain.FloodFill(Base(), "t", mx / doc.Cell, my / doc.Cell, brushT);
                        Bake(false); Say(n > 0 ? $"Залито клеток: {n}" : "Здесь уже этот вид");
                    }
                    break;
                case Tool.Path:
                case Tool.Plot:
                    if (down)
                    {
                        bool dbl = Time.unscaledTime - lastClick < 0.3f && (mp - lastClickPos).magnitude < 6;
                        lastClick = Time.unscaledTime; lastClickPos = mp;
                        if (dbl) Finish();
                        else drawing.Add(new[] { Math.Round(mx, 1), Math.Round(my, 1) });
                    }
                    if (mouse.rightButton.wasPressedThisFrame && drawing.Count > 0) drawing.RemoveAt(drawing.Count - 1);
                    break;
                case Tool.Gate:
                    if (down) ToggleGate(mx, my);
                    break;
                case Tool.Prop:
                    if (down)
                    {
                        Push();
                        var f = new MapFeature { Kind = "prop", Prop = propKind, Rot = propRot, Pts = new List<double[]> { Snap(mx, my, propKind, propRot) }, Seed = UnityEngine.Random.Range(1, 999999) };
                        doc.Features.Add(f); Bake(false);
                    }
                    if (mouse.rightButton.wasPressedThisFrame)
                    {
                        var hit = PropAt(mx, my);
                        if (hit != null) { Push(); doc.Features.Remove(hit); if (sel == hit) Select(null); Bake(false); }
                    }
                    break;
                case Tool.Edit:
                    EditMouse(mouse, shift, mx, my, mp);
                    break;
            }
        }
        // середина постройки — так, чтобы она легла ровно по клеткам
        double[] Snap(double x, double y, string kind, int rot)
        {
            var k = MapFeature.PropKinds[MapFeature.PropIndex(kind)]; int w = rot == 1 ? k.H : k.W, h = rot == 1 ? k.W : k.H;
            double c = doc.Cell;
            double x0 = Math.Round(x / c - w / 2.0), y0 = Math.Round(y / c - h / 2.0);
            return new[] { (x0 + w / 2.0) * c, (y0 + h / 2.0) * c };
        }
        MapFeature PropAt(double x, double y)
        {
            foreach (var f in ((IEnumerable<MapFeature>)doc.Features).Reverse().Where(f => f.IsProp))
            {
                var (x0, y0, w, h) = f.PropCells(doc.Cell);
                if (x >= x0 * doc.Cell && x <= (x0 + w) * doc.Cell && y >= y0 * doc.Cell && y <= (y0 + h) * doc.Cell) return f;
            }
            return null;
        }
        void Paint(double x0, double y0, double x1, double y1)
        {
            double r = Math.Max(0.5, brushR - 0.5);
            if (tool == Tool.Eraser)
            {
                if (eraseGround) Terrain.PaintSegment(Base(), "t", x0 / doc.Cell, y0 / doc.Cell, x1 / doc.Cell, y1 / doc.Cell, r, Terrain.Id("field"));
                if (eraseHeight) Terrain.PaintSegment(Base(), "z", x0 / doc.Cell, y0 / doc.Cell, x1 / doc.Cell, y1 / doc.Cell, r, 0);
                double rm = r * doc.Cell;
                bool Near(double[] q) => SegDist(q[0], q[1], x0, y0, x1, y1) <= rm;
                if (eraseProps) { int n0 = doc.Features.Count; doc.Features.RemoveAll(f => f.IsProp && f.Pts.Count > 0 && Near(f.Pts[0])); if (sel != null && !doc.Features.Contains(sel)) Select(null); }
                if (eraseLines)
                {
                    foreach (var f in doc.Features.Where(f => !f.IsProp).ToList())
                    {
                        int before = f.Pts.Count; f.Pts.RemoveAll(Near);
                        if (f.Pts.Count == before) continue;
                        if (f.Pts.Count < (f.IsPath ? 2 : 3)) { doc.Features.Remove(f); if (sel == f) Select(null); }
                        else f.Gates.RemoveAll(g => g > f.Length());
                    }
                }
            }
            else Terrain.PaintSegment(Base(), tool == Tool.Height ? "z" : "t", x0 / doc.Cell, y0 / doc.Cell, x1 / doc.Cell, y1 / doc.Cell, r, tool == Tool.Height ? brushZ : brushT);
            needBake = true; dirty = true;
        }
        static double SegDist(double px, double py, double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0, L2 = dx * dx + dy * dy, k = L2 > 0 ? Math.Max(0, Math.Min(1, ((px - x0) * dx + (py - y0) * dy) / L2)) : 0;
            return MapFeature.Hyp(px - (x0 + k * dx), py - (y0 + k * dy));
        }
        void EndDrag()
        {
            if (painting) { painting = false; Bake(false); }
            if (dragPt >= 0 || dragAll) { dragPt = -1; dragAll = false; Bake(false); RefreshObj(); }
        }
        void Finish()
        {
            if (tool != Tool.Path && tool != Tool.Plot || drawing.Count == 0) return;
            bool path = tool == Tool.Path;
            if (drawing.Count < (path ? 2 : 3)) { Say(path ? "Пути нужно хотя бы две точки" : "Участку нужно хотя бы три точки"); return; }
            Push();
            var f = new MapFeature { Kind = path ? pathKind : plotKind, Pts = drawing.Select(p => (double[])p.Clone()).ToList(), Seed = UnityEngine.Random.Range(1, 999999) };
            f.Closed = !path || closeRing && drawing.Count > 2 && (f.Kind == "wall" || f.Kind == "palisade" || f.Kind == "moat" || f.Kind == "trench");
            if (f.IsWall) { f.PtTowers = true; f.Every = -1; }   // нарисованная стена — башня в каждой точке
            if (f.Kind == "hill") f.Level = hillLevel;
            doc.Features.Add(f); drawing.Clear(); Select(f);
            Bake(false);
            Say($"{MapFeature.NameOf(f.Kind)} — готово." + (f.IsWall ? " Ворота — инструментом «Ворота»." : ""));
        }
        (MapFeature f, double s, double d) NearestWall(double x, double y)
        {
            MapFeature best = null; double bs = 0, bd = double.MaxValue;
            foreach (var f in doc.Features.Where(f => f.IsWall && f.Pts.Count >= 2))
            {
                var (s, d) = f.Nearest(x, y);
                if (d < bd) { bd = d; bs = s; best = f; }
            }
            return (best, bs, bd);
        }
        void ToggleGate(double x, double y)
        {
            var (f, s, d) = NearestWall(x, y);
            if (f == null || d > Math.Max(8, 14 / ppm)) { Say("Ворота ставятся на стену или частокол — щёлкни по линии стены."); return; }
            Push();
            double L = f.Length();
            int gi = f.Gates.FindIndex(g => Math.Abs(g - s) < 9 || f.Closed && Math.Abs(Math.Abs(g - s) - L) < 9);
            if (gi >= 0) { f.Gates.RemoveAt(gi); Say("Ворота убраны"); }
            else
            {
                double s2 = f.AwayFromCorners(s, f.Kind == "wall" ? 28 : 16);
                f.Gates.Add(s2); Say(Math.Abs(s2 - s) > 3 ? "Ворота поставлены (сдвинуты от угла на прямой участок)" : "Ворота поставлены");
            }
            Bake(false);
        }
        (MapFeature f, int i) HitVertex(double x, double y, double tol)
        {
            var order = sel != null ? new[] { sel }.Concat(doc.Features.Where(f => f != sel).Reverse()) : ((IEnumerable<MapFeature>)doc.Features).Reverse();
            foreach (var f in order.Where(f => !f.IsProp))
                for (int i = 0; i < f.Pts.Count; i++)
                    if (MapFeature.Hyp(f.Pts[i][0] - x, f.Pts[i][1] - y) <= tol) return (f, i);
            return (null, -1);
        }
        MapFeature HitFeature(double x, double y, double tol)
        {
            var prop = PropAt(x, y); if (prop != null) return prop;
            foreach (var f in ((IEnumerable<MapFeature>)doc.Features).Reverse().Where(f => f.IsPath))
                if (f.Nearest(x, y).d <= tol + f.W / 2) return f;
            MapFeature best = null; double area = double.MaxValue;
            foreach (var f in doc.Features.Where(f => !f.IsPath && !f.IsProp && f.Pts.Count >= 3))
            {
                if (!MapDoc.Inside(f.Pts, x, y)) continue;
                double a = MapDoc.Area(f.Pts);
                if (a < area) { area = a; best = f; }   // вложенные: меньший сверху (донжон в замке, дома в городе)
            }
            return best;
        }
        void EditMouse(Mouse mouse, bool shift, double mx, double my, Vector2 mp)
        {
            double tol = 9 / ppm;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var (vf, vi) = HitVertex(mx, my, tol);
                if (vf != null) { if (sel != vf) Select(vf); Push(); dragPt = vi; return; }
                if (shift && sel != null && sel.Pts.Count >= 2 && !sel.IsProp)
                {
                    // новая точка на ближайшем отрезке выбранного
                    int bi = -1; double bd = tol;
                    for (int i = 0; i < sel.Pts.Count; i++)
                    {
                        int j = i + 1; if (j == sel.Pts.Count) { if (!(sel.Closed || !sel.IsPath)) break; j = 0; }
                        var a = sel.Pts[i]; var b = sel.Pts[j];
                        double d = SegDist(mx, my, a[0], a[1], b[0], b[1]);
                        if (d < bd) { bd = d; bi = i; }
                    }
                    if (bi >= 0) { Push(); sel.Pts.Insert(bi + 1, new[] { mx, my }); dragPt = bi + 1; needBake = true; return; }
                }
                var hit = HitFeature(mx, my, tol);
                if (hit != sel) Select(hit);
                if (hit != null) { Push(); dragAll = true; dragX = mx; dragY = my; dragOrig = hit.Pts.Select(p => (double[])p.Clone()).ToList(); }
            }
            if (mouse.leftButton.isPressed && sel != null)
            {
                if (dragPt >= 0 && dragPt < sel.Pts.Count) { bool nt = MapDoc.NoTower(sel.Pts[dragPt]); sel.Pts[dragPt] = nt ? new[] { Math.Round(mx, 1), Math.Round(my, 1), 1.0 } : new[] { Math.Round(mx, 1), Math.Round(my, 1) }; needBake = true; }
                else if (dragAll)
                {
                    double dx = mx - dragX, dy = my - dragY;
                    if (sel.IsProp) { var q = Snap(dragOrig[0][0] + dx, dragOrig[0][1] + dy, sel.Prop, sel.Rot); if (sel.PW > 0) q = new[] { dragOrig[0][0] + Math.Round(dx / doc.Cell) * doc.Cell, dragOrig[0][1] + Math.Round(dy / doc.Cell) * doc.Cell }; sel.Pts[0] = q; }
                    else for (int i = 0; i < sel.Pts.Count; i++) { var q = (double[])dragOrig[i].Clone(); q[0] += dx; q[1] += dy; sel.Pts[i] = q; }
                    needBake = true;
                }
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                var (vf, vi) = HitVertex(mx, my, tol);
                if (vf != null && vf.Pts.Count > (vf.IsPath ? 2 : 3)) { Push(); vf.Pts.RemoveAt(vi); Bake(false); return; }
                var hit = HitFeature(mx, my, tol);
                if (hit != null) { Select(hit); ShowCtx(hit, mp); }
            }
        }

        void StatusLine(double mx, double my)
        {
            if (baked == null) return;
            int cx = (int)Math.Floor(mx / doc.Cell), cy = (int)Math.Floor(my / doc.Cell);
            string here = cx >= 0 && cy >= 0 && cx < baked.W && cy < baked.H ? $"{Terrain.NameOf(baked.T[cy * baked.W + cx])}, высота {baked.Z[cy * baked.W + cx]}" : "за краем";
            status.text = $"{doc.WM:0} × {doc.HM:0} м · объектов {doc.Features.Count}{(dirty ? " · не сохранено" : "")}\nкурсор {mx:0}, {my:0} м — {here}";
        }

        // ── разметка: пути и обводки, точки выбранного, ворота, рисуемое, кисть ──
        static Color32 KindCol(string k) => k switch
        {
            "wall" => new Color32(240, 214, 150, 255), "palisade" => new Color32(200, 150, 90, 255), "river" => new Color32(120, 180, 230, 255),
            "road" => new Color32(230, 200, 140, 255), "moat" => new Color32(90, 140, 190, 255), "trench" => new Color32(170, 130, 90, 255),
            "town" => new Color32(240, 150, 90, 255), "village" => new Color32(230, 180, 110, 255), "keep" => new Color32(250, 120, 90, 255),
            "forest" => new Color32(110, 200, 110, 255), "hill" => new Color32(220, 200, 160, 255), "water" => new Color32(120, 180, 230, 255),
            "prop" => new Color32(250, 235, 200, 255),
            _ => new Color32(235, 230, 215, 255),
        };
        void Overlay(double mx, double my, bool overUi)
        {
            V.Clear(); C.Clear(); I.Clear();
            var gold = new Color32(243, 210, 131, 255);
            bool showProps = tool == Tool.Edit || tool == Tool.Prop || tool == Tool.Eraser;
            foreach (var f in doc.Features)
            {
                bool on = f == sel; var c = KindCol(f.Kind); if (!on) c.a = 170;
                if (f.IsProp)
                {
                    if (!on && !showProps) continue;
                    var (x0, y0, w, h) = f.PropCells(doc.Cell);
                    Rect(x0 * doc.Cell, y0 * doc.Cell, (x0 + w) * doc.Cell, (y0 + h) * doc.Cell, on ? gold : new Color32(c.r, c.g, c.b, 110), on ? 3 : 1.2f);
                    continue;
                }
                float px = on ? 3.5f : f.IsPath ? 2f : 1.5f;
                foreach (var (a, b) in f.Segs()) Line(a[0], a[1], b[0], b[1], c, px);
                if (!f.IsPath && f.Pts.Count >= 3) Line(f.Pts[f.Pts.Count - 1][0], f.Pts[f.Pts.Count - 1][1], f.Pts[0][0], f.Pts[0][1], c, px);
                if (on || tool == Tool.Edit)
                    foreach (var p in f.Pts)
                    {
                        if (f.PtTowers && f.IsWall && !MapDoc.NoTower(p)) Circle(p[0], p[1], f.Kind == "wall" ? 7.5 : 5, on ? gold : c, on ? 2.5f : 1.2f);   // башня-точка
                        Square(p[0], p[1], on ? 7 : 4, on ? gold : c);
                    }
                if (f.IsWall)
                    foreach (double g in f.Gates)
                    {
                        var (x, y, tx, ty) = f.At(g); double nx = -ty * 9, ny = tx * 9;
                        Line(x - nx, y - ny, x + nx, y + ny, gold, 4); Line(x - tx * 5, y - ty * 5, x + tx * 5, y + ty * 5, gold, 2);
                    }
            }
            if (drawing.Count > 0 && (tool == Tool.Path || tool == Tool.Plot))
            {
                var c = KindCol(tool == Tool.Path ? pathKind : plotKind);
                for (int i = 0; i + 1 < drawing.Count; i++) Line(drawing[i][0], drawing[i][1], drawing[i + 1][0], drawing[i + 1][1], c, 3);
                var last = drawing[drawing.Count - 1];
                if (!overUi) Line(last[0], last[1], mx, my, new Color32(c.r, c.g, c.b, 140), 2);
                if (tool == Tool.Plot && drawing.Count >= 2 && !overUi) Line(mx, my, drawing[0][0], drawing[0][1], new Color32(c.r, c.g, c.b, 90), 1.5f);
                foreach (var p in drawing) Square(p[0], p[1], 6, gold);
            }
            if (!overUi)
            {
                if (tool == Tool.Brush || tool == Tool.Height) Circle(mx, my, Math.Max(0.5, brushR - 0.5) * doc.Cell, new Color32(255, 255, 255, 180), 1.5f);
                if (tool == Tool.Eraser) Circle(mx, my, Math.Max(0.5, brushR - 0.5) * doc.Cell, new Color32(255, 120, 100, 220), 2f);
                if (tool == Tool.Gate) { var (f, s, d) = NearestWall(mx, my); if (f != null && d < Math.Max(8, 14 / ppm)) { var (x, y, _, _) = f.At(f.AwayFromCorners(s, f.Kind == "wall" ? 28 : 16)); Circle(x, y, 7, gold, 2.5f); } }
                if (tool == Tool.Prop)
                {
                    var k = MapFeature.PropKinds[MapFeature.PropIndex(propKind)]; int w = propRot == 1 ? k.H : k.W, h = propRot == 1 ? k.W : k.H;
                    var q = Snap(mx, my, propKind, propRot); double x0 = q[0] - w * doc.Cell / 2, y0 = q[1] - h * doc.Cell / 2;
                    Rect(x0, y0, x0 + w * doc.Cell, y0 + h * doc.Cell, gold, 2.5f);
                }
            }
            mesh.Clear();
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetTriangles(I, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
        }
        void Rect(double x0, double y0, double x1, double y1, Color32 c, float px) { Line(x0, y0, x1, y0, c, px); Line(x1, y0, x1, y1, c, px); Line(x1, y1, x0, y1, c, px); Line(x0, y1, x0, y0, c, px); }
        void Line(double x0, double y0, double x1, double y1, Color32 c, float px)
        {
            float w = px / ppm / 2, dx = (float)(x1 - x0), dy = (float)(y1 - y0), L = Mathf.Sqrt(dx * dx + dy * dy);
            if (L < 1e-4f) return;
            float nx = -dy / L * w, ny = dx / L * w; int b = V.Count;
            V.Add(new Vector3((float)x0 + nx, -((float)y0 + ny), 0)); V.Add(new Vector3((float)x1 + nx, -((float)y1 + ny), 0));
            V.Add(new Vector3((float)x1 - nx, -((float)y1 - ny), 0)); V.Add(new Vector3((float)x0 - nx, -((float)y0 - ny), 0));
            for (int k = 0; k < 4; k++) C.Add(c);
            I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3);
        }
        void Square(double x, double y, float px, Color32 c)
        {
            float h = px / ppm / 2; int b = V.Count;
            V.Add(new Vector3((float)x - h, -((float)y - h), 0)); V.Add(new Vector3((float)x + h, -((float)y - h), 0));
            V.Add(new Vector3((float)x + h, -((float)y + h), 0)); V.Add(new Vector3((float)x - h, -((float)y + h), 0));
            for (int k = 0; k < 4; k++) C.Add(c);
            I.Add(b); I.Add(b + 1); I.Add(b + 2); I.Add(b); I.Add(b + 2); I.Add(b + 3);
        }
        void Circle(double x, double y, double r, Color32 c, float px)
        {
            const int n = 40;
            for (int k = 0; k < n; k++)
            {
                double a0 = k * Math.PI * 2 / n, a1 = (k + 1) * Math.PI * 2 / n;
                Line(x + Math.Cos(a0) * r, y + Math.Sin(a0) * r, x + Math.Cos(a1) * r, y + Math.Sin(a1) * r, c, px);
            }
        }
    }

    // ── мелочи для выпадающих списков (Алекс 11.10: «полосы прокрутки убрать; не влезает — полный текст при наведении») ──
    public static class UiKit
    {
        public static DropdownField Tip(DropdownField d)
        {
            d.tooltip = d.value;
            d.RegisterValueChangedCallback(e => d.tooltip = e.newValue);
            return d;
        }
    }
}
