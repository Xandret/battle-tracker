// ═══════════ ArmyBattlePanel.cs — бой своими армиями: файл и фракция на каждую сторону, отряды, карта ═══════════
// Армии собираются в редакторе «Армии» и лежат файлами (формат трекера). Здесь на каждую сторону — файл армий и
// фракция в нём, галочками — какие отряды выйдут; ниже — карта (шаблон генератора) и её зерно. «В бой» строит битву
// (PlayScenarios.FromArmies): расстановка сама, дальше — приказы и ходы. Боевой математики нет.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleCore;
using Journal.Armies;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class ArmyBattlePanel
    {
        sealed class Side
        {
            public string Path; public ArmyFile File; public int? Fac; public readonly HashSet<int> Chosen = new HashSet<int>();
            public DropdownField FileBox, FacBox; public ScrollView List; public Label Sum;
        }
        static readonly (string Id, string Name)[] BuiltIn = { ("field", "Поле"), ("forest", "Лес"), ("hills", "Холмы"), ("river", "Река"), ("desert", "Пустыня") };
        // шаблоны движка и карты редактора (Saves/Maps, Г102): у них Id «file:путь»
        List<(string Id, string Name)> Maps = BuiltIn.ToList();
        void RefreshMaps(string pick)
        {
            int keep = Math.Max(0, mapBox.index);
            Maps = BuiltIn.ToList();
            if (Directory.Exists(MapEditor.MapsDir))
                foreach (var f in Directory.GetFiles(MapEditor.MapsDir, "*.map.json").OrderByDescending(File.GetLastWriteTime))
                    Maps.Add(("file:" + f, "Карта: " + Path.GetFileName(f).Replace(".map.json", "")));
            if (pick != null) keep = Math.Max(0, Maps.FindIndex(m => string.Equals(m.Id, "file:" + pick, StringComparison.OrdinalIgnoreCase)));
            mapBox.choices = Maps.Select(m => m.Name).ToList();
            mapBox.index = Math.Min(keep, Maps.Count - 1);
        }

        readonly VisualElement root;
        readonly Side[] sides = { new Side(), new Side() };
        readonly DropdownField mapBox; readonly Label seedLabel, status; readonly Button startButton;
        readonly Action<PlayScenarios.ArmyPick, PlayScenarios.ArmyPick, string, uint> start;
        List<string> files = new List<string>();
        uint seed = 1;
        public bool Visible => !root.ClassListContains("hidden");

        public ArmyBattlePanel(VisualElement parent, Action<PlayScenarios.ArmyPick, PlayScenarios.ArmyPick, string, uint> start, Action back)
        {
            this.start = start;
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("lineup-box"); root.Add(box);
            var title = new Label("Своя армия против армии"); title.AddToClassList("menu-title"); box.Add(title);
            var sub = new Label("Армии — из файлов редактора «Армии» (и сохранений трекера): на каждую сторону файл и фракция, галочками — кто выйдет. Расстановка — сама."); sub.AddToClassList("lineup-sub"); box.Add(sub);
            var cols = new VisualElement(); cols.AddToClassList("lineup-cols"); box.Add(cols);
            for (int k = 0; k < 2; k++) BuildSide(cols, k);
            var mapRow = new VisualElement(); mapRow.AddToClassList("ab-map"); box.Add(mapRow);
            mapBox = new DropdownField("Карта", Maps.Select(m => m.Name).ToList(), 0); mapBox.AddToClassList("ab-drop"); mapRow.Add(mapBox);
            seedLabel = new Label(); seedLabel.AddToClassList("ab-seed"); mapRow.Add(seedLabel);
            var reroll = new Button(() => { NewSeed(); }) { text = "Другая карта" }; reroll.AddToClassList("lineup-mini"); mapRow.Add(reroll);
            status = new Label(); status.AddToClassList("menu-status"); box.Add(status);
            var foot = new VisualElement(); foot.AddToClassList("lineup-foot"); box.Add(foot);
            var backButton = new Button(() => { Hide(); back(); }) { text = "Назад" }; backButton.AddToClassList("menu-close"); foot.Add(backButton);
            startButton = new Button(Start) { text = "В БОЙ" }; startButton.AddToClassList("go-button"); startButton.AddToClassList("lineup-start"); foot.Add(startButton);
        }

        void BuildSide(VisualElement cols, int k)
        {
            var sd = sides[k];
            var col = new VisualElement(); col.AddToClassList("lineup-col"); cols.Add(col);
            var head = new Label(k == 0 ? "Сторона 1 — ты" : "Сторона 2 — противник"); head.AddToClassList("lineup-head"); head.AddToClassList(k == 0 ? "is-side1" : "is-side2"); col.Add(head);
            var fileRow = new VisualElement(); fileRow.AddToClassList("ab-row"); col.Add(fileRow);
            sd.FileBox = new DropdownField("Файл", new List<string>(), -1); sd.FileBox.AddToClassList("ab-drop"); fileRow.Add(sd.FileBox);
            var open = new Button(() =>
            {
                var f = FileDialog.OpenSave(FileDialog.LastDir());
                if (f == null) return;
                FileDialog.Remember(f); RefreshFiles(); SetFile(k, f);
            }) { text = "Открыть…" };
            open.AddToClassList("lineup-mini"); fileRow.Add(open);
            sd.FileBox.RegisterValueChangedCallback(e => { int i = sd.FileBox.index; if (i >= 0 && i < files.Count && files[i] != sd.Path) SetFile(k, files[i]); });
            sd.FacBox = new DropdownField("Фракция", new List<string>(), -1); sd.FacBox.AddToClassList("ab-drop"); col.Add(sd.FacBox);
            sd.FacBox.RegisterValueChangedCallback(e => { var facs = Facs(sd); int i = sd.FacBox.index; if (i >= 0 && i < facs.Count) { sd.Fac = facs[i].Id; FillUnits(k, true); } });
            var bar = new VisualElement(); bar.AddToClassList("lineup-bar"); col.Add(bar);
            var all = new Button(() => { foreach (var u in Units(sd)) sd.Chosen.Add((int)u["id"]); FillUnits(k, false); }) { text = "Все" }; all.AddToClassList("lineup-mini"); bar.Add(all);
            var none = new Button(() => { sd.Chosen.Clear(); FillUnits(k, false); }) { text = "Никого" }; none.AddToClassList("lineup-mini"); bar.Add(none);
            sd.Sum = new Label(); sd.Sum.AddToClassList("ab-sum"); bar.Add(sd.Sum);
            sd.List = new ScrollView(ScrollViewMode.Vertical); sd.List.AddToClassList("lineup-list"); col.Add(sd.List);
        }

        public void Hide() => root.AddToClassList("hidden");

        // path — армия стороны 1 (из редактора «В бой»); противник — вторая фракция того же файла или другой файл
        public void Show(string path = null, string mapFile = null)
        {
            RefreshFiles(); RefreshMaps(mapFile); status.text = "";
            if (path != null) SetFile(0, path);
            else if (sides[0].File == null && files.Count > 0) SetFile(0, files[0]);
            if (sides[1].File == null || path != null)
            {
                var f0 = sides[0].File;
                if (f0 != null && Facs(sides[0]).Count >= 2) { SetFile(1, sides[0].Path); var other = Facs(sides[1]).FirstOrDefault(x => x.Id != sides[0].Fac); SetFac(1, other.Id); }
                else { var otherFile = files.FirstOrDefault(p => p != sides[0].Path) ?? sides[0].Path; if (otherFile != null) SetFile(1, otherFile); }
            }
            NewSeed(); Check();
            root.RemoveFromClassList("hidden");
        }

        void RefreshFiles()
        {
            files = ArmyFile.Find(FileDialog.Recent());
            var names = files.Select(p => Path.GetFileName(p)).ToList();
            foreach (var sd in sides)
            {
                sd.FileBox.choices = names;
                int i = sd.Path == null ? -1 : files.FindIndex(p => string.Equals(p, sd.Path, StringComparison.OrdinalIgnoreCase));
                sd.FileBox.SetValueWithoutNotify(i >= 0 ? names[i] : (sd.Path != null ? Path.GetFileName(sd.Path) : "— выбери файл —"));
            }
        }

        void SetFile(int k, string path)
        {
            var sd = sides[k];
            try { sd.File = ArmyFile.Load(path); sd.Path = path; }
            catch (Exception e) { status.text = $"Не открылся {Path.GetFileName(path)}: {e.Message}"; return; }
            if (!files.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) { files.Add(path); sd.FileBox.choices = files.Select(p => Path.GetFileName(p)).ToList(); }
            sd.FileBox.SetValueWithoutNotify(Path.GetFileName(path));
            var facs = Facs(sd);
            SetFac(k, facs.Count > 0 ? facs[0].Id : null);
        }

        void SetFac(int k, int? id)
        {
            var sd = sides[k]; sd.Fac = id;
            var facs = Facs(sd);
            sd.FacBox.choices = facs.Select(f => $"{f.Name} — {f.Count} отр.").ToList();
            int i = facs.FindIndex(f => f.Id == id);
            sd.FacBox.SetValueWithoutNotify(i >= 0 ? sd.FacBox.choices[i] : "— нет отрядов —");
            FillUnits(k, true);
        }

        // фракции файла, где есть отряды в строю (и «без фракции», если такие отряды есть)
        static List<(int? Id, string Name, int Count)> Facs(Side sd)
        {
            if (sd.File == null) return new List<(int?, string, int)>();
            var ids = sd.File.Factions.OfType<JObject>().Select(f => (int?)(int)f["id"]).Concat(new int?[] { null });
            return ids.Select(id => (id, sd.File.FactionName(id), sd.File.UnitsOf(id).Count(Alive))).Where(f => f.Item3 > 0).ToList();
        }
        static bool Alive(JObject u) => (string)u["status"] != "destroyed" && ((double?)u["soldiers"] ?? 0) >= 1;
        static IEnumerable<JObject> Units(Side sd) => sd.File == null ? Enumerable.Empty<JObject>() : sd.File.UnitsOf(sd.Fac).Where(Alive);

        void FillUnits(int k, bool selectAll)
        {
            var sd = sides[k];
            if (selectAll) { sd.Chosen.Clear(); foreach (var u in Units(sd)) sd.Chosen.Add((int)u["id"]); }
            sd.List.Clear();
            foreach (var u in Units(sd).OrderByDescending(u => (double?)u["soldiers"] ?? 0))
            {
                int id = (int)u["id"];
                var t = new Toggle { text = $"{(string)u["name"]} — {(double?)u["soldiers"] ?? 0:0}" + ((string)u["status"] == "fled" ? " (бежал, вернётся)" : "") };
                t.AddToClassList("lineup-unit"); t.SetValueWithoutNotify(sd.Chosen.Contains(id));
                t.RegisterValueChangedCallback(e => { if (e.newValue) sd.Chosen.Add(id); else sd.Chosen.Remove(id); Check(); });
                sd.List.Add(t);
            }
            Check();
        }

        void NewSeed() { seed = (uint)new Random().Next(1, 1000000); seedLabel.text = $"зерно {seed}"; }

        void Check()
        {
            bool ok = true;
            foreach (var sd in sides)
            {
                var sel = Units(sd).Where(u => sd.Chosen.Contains((int)u["id"])).ToList();
                if (sd.Sum != null) sd.Sum.text = $"выбрано {sel.Count} · бойцов {sel.Sum(u => (double?)u["soldiers"] ?? 0):0}";
                if (sel.Count == 0) ok = false;
            }
            bool same = sides[0].Path != null && sides[1].Path != null && string.Equals(sides[0].Path, sides[1].Path, StringComparison.OrdinalIgnoreCase) && sides[0].Fac == sides[1].Fac;
            startButton.SetEnabled(ok && !same);
            status.text = same ? "Обе стороны — одна и та же фракция одного файла: выбери противнику другую" : !ok ? "Нужен хотя бы один отряд с каждой стороны" : "";
        }

        void Start()
        {
            var a = new PlayScenarios.ArmyPick { Path = sides[0].Path, FactionId = sides[0].Fac, Units = new HashSet<int>(sides[0].Chosen) };
            var b = new PlayScenarios.ArmyPick { Path = sides[1].Path, FactionId = sides[1].Fac, Units = new HashSet<int>(sides[1].Chosen) };
            string map = Maps[Math.Max(0, mapBox.index)].Id;
            status.text = "Строю битву…";
            root.schedule.Execute(() =>
            {
                try { start(a, b, map, seed); Hide(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); status.text = "Не вышло: " + e.Message; }
            }).ExecuteLater(30);
        }
    }
}
