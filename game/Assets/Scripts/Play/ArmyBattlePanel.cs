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
            public string Path; public ArmyFile File; public readonly HashSet<int?> Facs = new HashSet<int?>(); public readonly HashSet<int> Chosen = new HashSet<int>();
            public DropdownField FileBox; public VisualElement FacList; public ScrollView List; public Label Sum;
            public int? Fac => Facs.FirstOrDefault();
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
        readonly DropdownField mapBox, placeBox; readonly Label seedLabel, status; readonly Button startButton;
        readonly Action<PlayScenarios.ArmyPick, PlayScenarios.ArmyPick, string, uint, string> start;
        List<string> files = new List<string>();
        uint seed = 1;
        public bool Visible => !root.ClassListContains("hidden");

        public ArmyBattlePanel(VisualElement parent, Action<PlayScenarios.ArmyPick, PlayScenarios.ArmyPick, string, uint, string> start, Action back)
        {
            this.start = start;
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("lineup-box"); root.Add(box);
            var title = new Label("Своя армия против армии"); title.AddToClassList("menu-title"); box.Add(title);
            var sub = new Label("Армии — из файлов редактора «Армии» (и сохранений трекера): на каждую сторону файл и одна или несколько фракций-союзников, галочками — кто выйдет. Расстановка — сама, потом её можно поправить."); sub.AddToClassList("lineup-sub"); box.Add(sub);
            var cols = new VisualElement(); cols.AddToClassList("lineup-cols"); box.Add(cols);
            for (int k = 0; k < 2; k++) BuildSide(cols, k);
            var mapRow = new VisualElement(); mapRow.AddToClassList("ab-map"); box.Add(mapRow);
            mapBox = new DropdownField("Карта", Maps.Select(m => m.Name).ToList(), 0); mapBox.AddToClassList("ab-drop"); mapRow.Add(mapBox);
            seedLabel = new Label(); seedLabel.AddToClassList("ab-seed"); mapRow.Add(seedLabel);
            var reroll = new Button(() => { NewSeed(); }) { text = "Другая карта" }; reroll.AddToClassList("lineup-mini"); mapRow.Add(reroll);
            placeBox = UiKit.Tip(new DropdownField("Где кто", PlayScenarios.Places.Select(p => p.Name).ToList(), 0)); placeBox.AddToClassList("ab-drop"); placeBox.AddToClassList("ab-place"); mapRow.Add(placeBox);
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
            var fh = new Label("Фракции стороны — союзники вместе"); fh.AddToClassList("ab-fac-head"); col.Add(fh);
            sd.FacList = new VisualElement(); sd.FacList.AddToClassList("ab-facs"); col.Add(sd.FacList);
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

        // одна фракция (по умолчанию) — остальные галочками
        void SetFac(int k, int? id)
        {
            var sd = sides[k]; sd.Facs.Clear(); if (Facs(sd).Any(f => f.Id == id)) sd.Facs.Add(id);
            FacToggles(k); FillUnits(k, true);
        }
        void FacToggles(int k)
        {
            var sd = sides[k]; sd.FacList.Clear();
            var facs = Facs(sd);
            if (facs.Count == 0) { var n = new Label("— в файле нет отрядов в строю —"); n.AddToClassList("ab-sum"); sd.FacList.Add(n); return; }
            foreach (var (id, name, count) in facs)
            {
                var fid = id;
                var row = new VisualElement(); row.AddToClassList("ab-fac"); sd.FacList.Add(row);
                var sw = new VisualElement(); sw.AddToClassList("ab-fac-swatch");
                if (UnityEngine.ColorUtility.TryParseHtmlString((string)sd.File.Faction(id)?["color"] ?? "#6e6a62", out var c)) sw.style.backgroundColor = c;
                var t = new Toggle { text = $"{name} — {count} отр." }; t.AddToClassList("lineup-unit"); t.SetValueWithoutNotify(sd.Facs.Contains(id));
                // фракция другой стороны того же файла — не взять (иначе бьётся сама с собой)
                var other = sides[1 - k]; bool taken = other.File != null && string.Equals(other.Path, sd.Path, StringComparison.OrdinalIgnoreCase) && other.Facs.Contains(id);
                t.SetEnabled(!taken); if (taken) t.tooltip = "Эта фракция уже воюет на другой стороне";
                t.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) { sd.Facs.Add(fid); foreach (var u in sd.File.UnitsOf(fid).Where(Alive)) sd.Chosen.Add((int)u["id"]); }
                    else { sd.Facs.Remove(fid); foreach (var u in sd.File.UnitsOf(fid)) sd.Chosen.Remove((int)u["id"]); }
                    FillUnits(k, false); FacToggles(1 - k);
                });
                row.Add(sw); row.Add(t);
            }
        }

        // фракции файла, где есть отряды в строю (и «без фракции», если такие отряды есть)
        static List<(int? Id, string Name, int Count)> Facs(Side sd)
        {
            if (sd.File == null) return new List<(int?, string, int)>();
            var ids = sd.File.Factions.OfType<JObject>().Select(f => (int?)(int)f["id"]).Concat(new int?[] { null });
            return ids.Select(id => (id, sd.File.FactionName(id), sd.File.UnitsOf(id).Count(Alive))).Where(f => f.Item3 > 0).ToList();
        }
        static bool Alive(JObject u) => (string)u["status"] != "destroyed" && ((double?)u["soldiers"] ?? 0) >= 1;
        static IEnumerable<JObject> Units(Side sd) => sd.File == null ? Enumerable.Empty<JObject>() : sd.Facs.SelectMany(f => sd.File.UnitsOf(f)).Where(Alive);

        void FillUnits(int k, bool selectAll)
        {
            var sd = sides[k];
            if (selectAll) { sd.Chosen.Clear(); foreach (var u in Units(sd)) sd.Chosen.Add((int)u["id"]); }
            sd.List.Clear();
            int? lastFac = int.MinValue;
            foreach (var u in Units(sd).OrderBy(u => ArmyFile.Id(u["factionId"]) ?? -1).ThenByDescending(u => (double?)u["soldiers"] ?? 0))
            {
                var uf = ArmyFile.Id(u["factionId"]);
                if (sd.Facs.Count > 1 && uf != lastFac) { var h = new Label(sd.File.FactionName(uf)); h.AddToClassList("ab-fac-group"); sd.List.Add(h); lastFac = uf; }
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
            bool same = sides[0].Path != null && sides[1].Path != null && string.Equals(sides[0].Path, sides[1].Path, StringComparison.OrdinalIgnoreCase) && sides[0].Facs.Overlaps(sides[1].Facs);
            startButton.SetEnabled(ok && !same);
            status.text = same ? "Одна и та же фракция на обеих сторонах — сними её с одной из них" : !ok ? "Нужен хотя бы один отряд с каждой стороны" : "";
        }

        void Start()
        {
            var a = new PlayScenarios.ArmyPick { Path = sides[0].Path, FactionId = sides[0].Fac, FactionIds = sides[0].Facs.ToList(), Units = new HashSet<int>(sides[0].Chosen) };
            var b = new PlayScenarios.ArmyPick { Path = sides[1].Path, FactionId = sides[1].Fac, FactionIds = sides[1].Facs.ToList(), Units = new HashSet<int>(sides[1].Chosen) };
            string map = Maps[Math.Max(0, mapBox.index)].Id, place = PlayScenarios.Places[Math.Max(0, placeBox.index)].Id;
            status.text = "Строю битву…";
            root.schedule.Execute(() =>
            {
                try { start(a, b, map, seed, place); Hide(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); status.text = "Не вышло: " + e.Message; }
            }).ExecuteLater(30);
        }
    }
}
