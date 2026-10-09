// ═══════════ MapEditor.cs — редактор карт в игре (Г102): кисть, пути и участки, генераторы ═══════════
// Панель слева, карта — в смотрелке (земля и крепости — тем же рисунком, что в бою). Инструменты:
//   Кисть — вид местности по клеткам 5 м; Высота — уровни 0…3; Заливка — связная область одного вида;
//   Путь — ломаная: стена (башни сами на углах и через N метров), частокол, река, дорога (через воду — мост), ров, окоп;
//   Участок — обводка: город (улицы, площадь, церковь, дома), деревня, донжон, лес, поле, болото, озеро, скалы, холм…;
//   Ворота — щелчок по стене ставит или убирает ворота; Правка — выбрать объект, тянуть точки и весь объект, Shift+ЛКМ
//   по линии — новая точка, ПКМ по точке — убрать, Delete — удалить объект, справа — его настройки.
// Генератор: поле, лес, холмы, река, пустыня (шаблоны движка) и замок, город, деревня — объектами, их можно править дальше.
// Карты — файлы .map.json в Saves/Maps; «Испытать в бою» — бой своими армиями на этой карте. Камера — колесо, средняя
// кнопка, WASD. Ctrl+Z / Ctrl+Y — отменить / вернуть, Ctrl+S — сохранить, Enter — закончить путь или участок, Esc — отмена.
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
        enum Tool { Brush, Height, Fill, Path, Plot, Gate, Edit }
        static readonly (Tool T, string Name, string Tip)[] Tools =
        {
            (Tool.Brush, "Кисть", "Вид местности по клеткам 5 м"), (Tool.Height, "Высота", "Уровни высоты 0…3"), (Tool.Fill, "Заливка", "Связная область одного вида"),
            (Tool.Path, "Путь", "Стена, частокол, река, дорога, ров, окоп — ломаной"), (Tool.Plot, "Участок", "Город, деревня, лес, поле, холм… — обводкой"),
            (Tool.Gate, "Ворота", "Щелчок по стене — поставить или убрать ворота"), (Tool.Edit, "Правка", "Выбрать объект, тянуть точки"),
        };
        public static string MapsDir => Path.Combine(Journal.Armies.ArmyFile.SavesDir, "Maps");

        readonly PlayController pc; readonly BattleViewer viewer; readonly VisualElement uiRoot;
        readonly VisualElement root, opts, genParams, filePopup; readonly ScrollView panel;
        readonly Label status, toast; readonly TextField nameField; readonly IntegerField wField, hField, seedField;
        readonly Dictionary<Tool, Button> toolBtn = new Dictionary<Tool, Button>();
        public bool Visible => !root.ClassListContains("hidden");
        public event Action Closed;
        public event Action<string> Test;   // файл карты — к бою своими армиями

        MapDoc doc; string file; bool dirty, closeAsked; TerrainMap baked;
        Tool tool = Tool.Brush; byte brushT = 6; int brushZ = 1, brushR = 2, hillLevel = 1; string pathKind = "wall", plotKind = "town"; bool closeRing = true;
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
            panel = new ScrollView(ScrollViewMode.Vertical); panel.AddToClassList("panel"); panel.AddToClassList("me-panel"); root.Add(panel);
            var title = new Label("Редактор карт"); title.AddToClassList("me-title"); panel.Add(title);
            nameField = new TextField("Название"); nameField.AddToClassList("me-field");
            nameField.RegisterValueChangedCallback(e => { if (doc != null && doc.Name != e.newValue) { doc.Name = e.newValue; dirty = true; } });
            panel.Add(nameField);
            var files = Row(panel);
            Btn(files, "Новая", NewMap); Btn(files, "Открыть…", ToggleOpen); Btn(files, "Сохранить", () => Save(false)); Btn(files, "Как новую", () => Save(true));
            filePopup = new VisualElement(); filePopup.AddToClassList("me-files"); filePopup.AddToClassList("hidden"); panel.Add(filePopup);
            var size = Row(panel);
            wField = new IntegerField("Новая, м") { value = 1500 }; wField.AddToClassList("me-num"); size.Add(wField);
            hField = new IntegerField("×") { value = 1000 }; hField.AddToClassList("me-num"); hField.AddToClassList("me-num-short"); size.Add(hField);

            Head(panel, "Инструменты");
            var tr = Row(panel); tr.AddToClassList("me-wrap");
            foreach (var (t, name, tip) in Tools) { var tt = t; var b = Btn(tr, name, () => SetTool(tt)); b.tooltip = tip; b.AddToClassList("me-tool"); toolBtn[t] = b; }
            opts = new VisualElement(); opts.AddToClassList("me-opts"); panel.Add(opts);

            Head(panel, "Генератор");
            var gens = MapMakers.All().ToList();
            var genBox = new DropdownField("Шаблон", gens.Select(g => g.Name).ToList(), gens.FindIndex(g => g.Id == genId)); genBox.AddToClassList("me-field");
            genBox.RegisterValueChangedCallback(e => { int i = genBox.index; if (i >= 0) { genId = gens[i].Id; genInput.Clear(); BuildGenParams(); } });
            panel.Add(genBox);
            genParams = new VisualElement(); genParams.AddToClassList("me-gen"); panel.Add(genParams);
            var seedRow = Row(panel);
            seedField = new IntegerField("Зерно") { value = (int)genSeed }; seedField.AddToClassList("me-num"); seedField.RegisterValueChangedCallback(e => genSeed = (uint)Math.Max(1, e.newValue)); seedRow.Add(seedField);
            Btn(seedRow, "Случайное", () => { genSeed = (uint)UnityEngine.Random.Range(1, 999999); seedField.SetValueWithoutNotify((int)genSeed); });
            var make = Btn(panel, "Создать карту", Generate); make.AddToClassList("go-button"); make.AddToClassList("me-make");

            Head(panel, "");
            var ur = Row(panel); Btn(ur, "↶ Отменить", Undo); Btn(ur, "↷ Вернуть", Redo);
            var end = Row(panel);
            var test = Btn(end, "Испытать в бою ⚔", TestBattle); test.AddToClassList("go-button"); test.AddToClassList("me-test");
            Btn(end, "Закрыть", Close);
            toast = new Label(); toast.AddToClassList("me-toast"); panel.Add(toast);
            status = new Label(); status.AddToClassList("me-status"); panel.Add(status);

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
        static void Head(VisualElement p, string text) { var h = new Label(text); h.AddToClassList("set-head"); p.Add(h); }
        static Label Note(VisualElement p, string text) { var n = new Label(text); n.AddToClassList("me-note"); p.Add(n); return n; }
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
            return picked != null && (picked == panel || panel.Contains(picked));
        }

        // ── открыть, закрыть ──
        public void Show(MapDoc d = null)
        {
            if (d != null) { doc = d; file = null; dirty = false; }
            else if (doc == null) { doc = MapMakers.Make("x-castle", null, genSeed); file = null; dirty = false; }
            undo.Clear(); redo.Clear(); drawing.Clear(); sel = null; closeAsked = false;
            nameField.SetValueWithoutNotify(doc.Name);
            root.RemoveFromClassList("hidden"); overlayGo.SetActive(true);
            savedInsets = viewer.Insets; FitInsets();
            RefreshOpts(); Bake(true);
        }
        public void Hide() { root.AddToClassList("hidden"); overlayGo.SetActive(false); mesh.Clear(); filePopup.AddToClassList("hidden"); viewer.Insets = savedInsets; }
        // кадр «вся карта» — справа от панели: её ширина в пикселях экрана
        Vector4 savedInsets;
        void FitInsets()
        {
            var vt = uiRoot.panel?.visualTree; float w = vt != null && vt.layout.width > 0 ? vt.layout.width : 1600;
            float right = float.IsNaN(panel.worldBound.xMax) || panel.worldBound.xMax <= 0 ? 380 : panel.worldBound.xMax;
            viewer.Insets = new Vector4(right * Screen.width / w + 12, 0, 0, 0);
        }
        void Close()
        {
            if (dirty && !closeAsked) { closeAsked = true; Say("Есть несохранённые правки. «Закрыть» ещё раз — выйти без сохранения."); return; }
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
            redo.Clear(); dirty = true; closeAsked = false;
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
        void AfterSwap() { sel = null; drawing.Clear(); dirty = true; nameField.SetValueWithoutNotify(doc.Name); RefreshOpts(); Bake(false); }

        // ── файлы ──
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
                dirty = false; closeAsked = false;
                Say("Сохранено: " + Path.GetFileName(file));
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
                    var f = new DropdownField(d.Name, d.Options.Select(o => o[1]).ToList(), Array.FindIndex(d.Options, o => o[0] == cur)); f.AddToClassList("me-field");
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
        void RefreshOpts()
        {
            opts.Clear(); propsPushed = false;
            switch (tool)
            {
                case Tool.Brush:
                case Tool.Fill:
                    Palette();
                    if (tool == Tool.Brush) SizeSlider();
                    Note(opts, tool == Tool.Brush ? "ЛКМ — рисовать, Shift — прямой линией от прошлой точки." : "ЛКМ — залить связную область одного вида.");
                    break;
                case Tool.Height:
                {
                    var r = Row(opts);
                    for (int z = 0; z <= Terrain.MaxHeight; z++) { int zz = z; var b = Btn(r, z == 0 ? "0 — низ" : z.ToString(), () => { brushZ = zz; RefreshOpts(); }); b.EnableInClassList("is-on", brushZ == z); }
                    SizeSlider();
                    Note(opts, "ЛКМ — поднять или опустить землю до уровня (уровень ~3 м). Холм обводкой — инструмент «Участок».");
                    break;
                }
                case Tool.Path:
                {
                    var kinds = MapFeature.PathKinds;
                    var dd = new DropdownField("Что", kinds.Select(k => k.Name).ToList(), Array.FindIndex(kinds, k => k.Kind == pathKind)); dd.AddToClassList("me-field");
                    dd.RegisterValueChangedCallback(e => { int i = dd.index; if (i >= 0) pathKind = kinds[i].Kind; });
                    opts.Add(dd);
                    var ring = new Toggle("Замкнуть в кольцо") { value = closeRing }; ring.AddToClassList("me-field"); ring.RegisterValueChangedCallback(e => closeRing = e.newValue); opts.Add(ring);
                    Note(opts, "ЛКМ — точка, ПКМ или Backspace — убрать последнюю, Enter или двойной щелчок — готово, Esc — отмена. У стены башни встают сами: на углах и через 45 м.");
                    break;
                }
                case Tool.Plot:
                {
                    var kinds = MapFeature.PlotKinds;
                    var dd = new DropdownField("Что", kinds.Select(k => k.Name).ToList(), Array.FindIndex(kinds, k => k.Kind == plotKind)); dd.AddToClassList("me-field");
                    dd.RegisterValueChangedCallback(e => { int i = dd.index; if (i >= 0) { plotKind = kinds[i].Kind; RefreshOpts(); } });
                    opts.Add(dd);
                    if (plotKind == "hill") { var s = new SliderInt("Высота холма", 1, Terrain.MaxHeight) { value = hillLevel, showInputField = true }; s.AddToClassList("me-field"); s.RegisterValueChangedCallback(e => hillLevel = e.newValue); opts.Add(s); }
                    Note(opts, "Обведи участок: ЛКМ — точка, Enter или двойной щелчок — замкнуть. Город и деревня раскладывают улицы и дома сами; другая раскладка — в «Правке».");
                    break;
                }
                case Tool.Gate:
                    Note(opts, "ЛКМ по стене или частоколу — ворота (проём 10 м, у каменной стены — надвратные башни); по воротам — убрать.");
                    break;
                case Tool.Edit:
                    Note(opts, "ЛКМ — выбрать объект; тянуть точку — двигать её, тянуть объект — двигать весь. Shift+ЛКМ по линии — новая точка, ПКМ по точке — убрать, Delete — удалить объект.");
                    Props();
                    break;
            }
        }
        void Palette()
        {
            var box = new VisualElement(); box.AddToClassList("me-palette"); opts.Add(box);
            foreach (var t in Terrain.Types.Where(t => !t.System))
            {
                var id = (byte)t.Id;
                var b = new Button(() => { brushT = id; RefreshOpts(); }); b.AddToClassList("me-swatch-btn"); b.EnableInClassList("is-on", brushT == id);
                var sw = new VisualElement(); sw.AddToClassList("me-swatch"); sw.style.backgroundColor = (Color)BattleViewer.GroundColor(t.Id); b.Add(sw);
                var l = new Label(t.Name); l.AddToClassList("me-swatch-name"); b.Add(l);
                b.tooltip = t.Group; box.Add(b);
            }
        }
        void SizeSlider()
        {
            var s = new SliderInt("Размер, клеток", 1, 20) { value = brushR, showInputField = true }; s.AddToClassList("me-field");
            s.RegisterValueChangedCallback(e => brushR = e.newValue); opts.Add(s);
        }
        // настройки выбранного объекта
        void Props()
        {
            if (sel == null) { Note(opts, "Ничего не выбрано."); return; }
            var h = new Label(MapFeature.NameOf(sel.Kind) + (sel.IsPath ? $" · {sel.Length():0} м" : "")); h.AddToClassList("me-sel"); opts.Add(h);
            void Edited() { if (!propsPushed) { Push(); propsPushed = true; } needBake = true; dirty = true; }
            if (sel.Kind == "wall" || sel.Kind == "palisade")
            {
                var every = new SliderInt("Башни через, м (0 — без)", 0, 150) { value = sel.E < 0 ? 0 : (int)sel.E, showInputField = true }; every.AddToClassList("me-field");
                every.RegisterValueChangedCallback(e => { Edited(); sel.Every = e.newValue == 0 ? -1 : e.newValue; }); opts.Add(every);
                Note(opts, $"Ворот: {sel.Gates.Count} (инструмент «Ворота»)");
            }
            if (sel.IsPath)
            {
                if (sel.Kind != "wall" && sel.Kind != "palisade")
                {
                    var w = new SliderInt("Ширина, м", 5, sel.Kind == "river" ? 120 : 40) { value = (int)sel.W, showInputField = true }; w.AddToClassList("me-field");
                    w.RegisterValueChangedCallback(e => { Edited(); sel.Width = e.newValue; }); opts.Add(w);
                }
                var ring = new Toggle("Кольцо") { value = sel.Closed }; ring.AddToClassList("me-field");
                ring.RegisterValueChangedCallback(e => { Edited(); sel.Closed = e.newValue && sel.Pts.Count > 2; }); opts.Add(ring);
            }
            if (sel.Kind == "hill")
            {
                var s = new SliderInt("Высота", 1, Terrain.MaxHeight) { value = sel.Level, showInputField = true }; s.AddToClassList("me-field");
                s.RegisterValueChangedCallback(e => { Edited(); sel.Level = e.newValue; }); opts.Add(s);
            }
            if (sel.Kind == "town" || sel.Kind == "village")
                Btn(opts, "Другая раскладка", () => { Edited(); sel.Seed = UnityEngine.Random.Range(1, 999999); Bake(false); });
            var del = Btn(opts, "Удалить объект", DeleteSel); del.AddToClassList("me-danger");
        }
        void DeleteSel()
        {
            if (sel == null) return;
            Push(); doc.Features.Remove(sel); sel = null; RefreshOpts(); Bake(false);
        }

        // ── кадр: клавиши, мышь, сборка, разметка ──
        public void Tick()
        {
            if (!Visible) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (mouse == null) return;
            var mp = mouse.position.ReadValue();
            bool overUi = OverPanel(mp) || mp.x < 0 || mp.y < 0 || mp.x >= Screen.width || mp.y >= Screen.height;
            var w = viewer.ScreenToMap(mp); double mx = w.x, my = w.y;
            ppm = Screen.height / (2 * viewer.Cam.orthographicSize);
            if (kb != null && !Typing) Keys(kb);
            if (!overUi) MouseInput(mouse, kb, mx, my, mp);
            if (mouse.leftButton.wasReleasedThisFrame) EndDrag();
            if (needBake && Time.unscaledTime - bakeAt > (painting || dragPt >= 0 || dragAll ? 0.12f : 0f)) Bake(false);
            if (Time.unscaledTime > toastUntil) toast.text = "";
            StatusLine(mx, my);
            Overlay(mx, my, overUi);
        }
        void Keys(Keyboard kb)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (kb.escapeKey.wasPressedThisFrame) { if (drawing.Count > 0) drawing.Clear(); else if (sel != null) { sel = null; RefreshOpts(); } else if (!filePopup.ClassListContains("hidden")) filePopup.AddToClassList("hidden"); }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Finish();
            if (kb.backspaceKey.wasPressedThisFrame && drawing.Count > 0) drawing.RemoveAt(drawing.Count - 1);
            if (kb.deleteKey.wasPressedThisFrame && sel != null) DeleteSel();
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) Save(false);
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
                case Tool.Edit:
                    EditMouse(mouse, shift, mx, my);
                    break;
            }
        }
        void Paint(double x0, double y0, double x1, double y1)
        {
            double r = Math.Max(0.5, brushR - 0.5);
            Terrain.PaintSegment(Base(), tool == Tool.Height ? "z" : "t", x0 / doc.Cell, y0 / doc.Cell, x1 / doc.Cell, y1 / doc.Cell, r, tool == Tool.Height ? brushZ : brushT);
            needBake = true; dirty = true;
        }
        void EndDrag()
        {
            if (painting) { painting = false; Bake(false); }
            if (dragPt >= 0 || dragAll) { dragPt = -1; dragAll = false; Bake(false); RefreshOpts(); }
        }
        void Finish()
        {
            if (tool != Tool.Path && tool != Tool.Plot || drawing.Count == 0) return;
            bool path = tool == Tool.Path;
            if (drawing.Count < (path ? 2 : 3)) { Say(path ? "Пути нужно хотя бы две точки" : "Участку нужно хотя бы три точки"); return; }
            Push();
            var f = new MapFeature { Kind = path ? pathKind : plotKind, Pts = drawing.Select(p => (double[])p.Clone()).ToList(), Seed = UnityEngine.Random.Range(1, 999999) };
            f.Closed = !path || closeRing && drawing.Count > 2 && (f.Kind == "wall" || f.Kind == "palisade" || f.Kind == "moat" || f.Kind == "trench");
            if (f.Kind == "hill") f.Level = hillLevel;
            doc.Features.Add(f); drawing.Clear(); sel = f;
            Bake(false);
            Say($"{MapFeature.NameOf(f.Kind)} — готово." + (f.Kind == "wall" || f.Kind == "palisade" ? " Ворота — инструментом «Ворота»." : ""));
        }
        (MapFeature f, double s, double d) NearestWall(double x, double y)
        {
            MapFeature best = null; double bs = 0, bd = double.MaxValue;
            foreach (var f in doc.Features.Where(f => (f.Kind == "wall" || f.Kind == "palisade") && f.Pts.Count >= 2))
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
            if (gi >= 0) { f.Gates.RemoveAt(gi); Say("Ворота убраны"); } else { f.Gates.Add(s); Say("Ворота поставлены"); }
            Bake(false);
        }
        (MapFeature f, int i) HitVertex(double x, double y, double tol)
        {
            var order = sel != null ? new[] { sel }.Concat(doc.Features.Where(f => f != sel).Reverse()) : ((IEnumerable<MapFeature>)doc.Features).Reverse();
            foreach (var f in order)
                for (int i = 0; i < f.Pts.Count; i++)
                    if (MapFeature.Hyp(f.Pts[i][0] - x, f.Pts[i][1] - y) <= tol) return (f, i);
            return (null, -1);
        }
        MapFeature HitFeature(double x, double y, double tol)
        {
            foreach (var f in ((IEnumerable<MapFeature>)doc.Features).Reverse().Where(f => f.IsPath))
                if (f.Nearest(x, y).d <= tol + f.W / 2) return f;
            MapFeature best = null; double area = double.MaxValue;
            foreach (var f in doc.Features.Where(f => !f.IsPath && f.Pts.Count >= 3))
            {
                if (!MapDoc.Inside(f.Pts, x, y)) continue;
                double a = Math.Abs(f.Pts.Select((p, i) => p[0] * f.Pts[(i + 1) % f.Pts.Count][1] - f.Pts[(i + 1) % f.Pts.Count][0] * p[1]).Sum()) / 2;
                if (a < area) { area = a; best = f; }   // вложенные: меньший сверху (донжон в замке, дома в городе)
            }
            return best;
        }
        void EditMouse(Mouse mouse, bool shift, double mx, double my)
        {
            double tol = 9 / ppm;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var (vf, vi) = HitVertex(mx, my, tol);
                if (vf != null) { if (sel != vf) { sel = vf; RefreshOpts(); } Push(); dragPt = vi; return; }
                if (shift && sel != null && sel.Pts.Count >= 2)
                {
                    // новая точка на ближайшем отрезке выбранного
                    int bi = -1; double bd = tol;
                    for (int i = 0; i < sel.Pts.Count; i++)
                    {
                        int j = i + 1; if (j == sel.Pts.Count) { if (!(sel.Closed || !sel.IsPath)) break; j = 0; }
                        var a = sel.Pts[i]; var b = sel.Pts[j];
                        double dx = b[0] - a[0], dy = b[1] - a[1], L2 = dx * dx + dy * dy, k = L2 > 0 ? Math.Max(0, Math.Min(1, ((mx - a[0]) * dx + (my - a[1]) * dy) / L2)) : 0;
                        double d = MapFeature.Hyp(mx - (a[0] + k * dx), my - (a[1] + k * dy));
                        if (d < bd) { bd = d; bi = i; }
                    }
                    if (bi >= 0) { Push(); sel.Pts.Insert(bi + 1, new[] { mx, my }); dragPt = bi + 1; needBake = true; return; }
                }
                var hit = HitFeature(mx, my, tol);
                if (hit != sel) { sel = hit; RefreshOpts(); }
                if (hit != null) { Push(); dragAll = true; dragX = mx; dragY = my; dragOrig = hit.Pts.Select(p => (double[])p.Clone()).ToList(); }
            }
            if (mouse.leftButton.isPressed && sel != null)
            {
                if (dragPt >= 0 && dragPt < sel.Pts.Count) { sel.Pts[dragPt] = new[] { Math.Round(mx, 1), Math.Round(my, 1) }; needBake = true; }
                else if (dragAll) { double dx = mx - dragX, dy = my - dragY; for (int i = 0; i < sel.Pts.Count; i++) sel.Pts[i] = new[] { dragOrig[i][0] + dx, dragOrig[i][1] + dy }; needBake = true; }
            }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                var (vf, vi) = HitVertex(mx, my, tol);
                if (vf != null && vf.Pts.Count > (vf.IsPath ? 2 : 3)) { Push(); vf.Pts.RemoveAt(vi); Bake(false); }
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
            _ => new Color32(235, 230, 215, 255),
        };
        void Overlay(double mx, double my, bool overUi)
        {
            V.Clear(); C.Clear(); I.Clear();
            var gold = new Color32(243, 210, 131, 255);
            foreach (var f in doc.Features)
            {
                bool on = f == sel; var c = KindCol(f.Kind); if (!on) c.a = 170;
                float px = on ? 3.5f : f.IsPath ? 2f : 1.5f;
                foreach (var (a, b) in f.Segs()) Line(a[0], a[1], b[0], b[1], c, px);
                if (!f.IsPath && f.Pts.Count >= 3) Line(f.Pts[f.Pts.Count - 1][0], f.Pts[f.Pts.Count - 1][1], f.Pts[0][0], f.Pts[0][1], c, px);
                if (on || tool == Tool.Edit) foreach (var p in f.Pts) Square(p[0], p[1], on ? 7 : 4, on ? gold : c);
                if (f.Kind == "wall" || f.Kind == "palisade")
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
                if (tool == Tool.Gate) { var (f, s, d) = NearestWall(mx, my); if (f != null && d < Math.Max(8, 14 / ppm)) { var (x, y, _, _) = f.At(s); Circle(x, y, 7, gold, 2.5f); } }
            }
            mesh.Clear();
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetTriangles(I, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
        }
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
}
