// ═══════════ LineupPanel.cs — состав битвы из сохранения трекера (Г98: первая битва малым составом) ═══════════
// Открыли armiya_hodN.txt — здесь видно, кто в нём стоит на карте, по сторонам (стороны — как у SaveScene: по логу
// боёв). Галочкой отмечаешь, кто выйдет на поле; расстановка — как в сохранении. «Малый состав» — по 3 отряда
// с каждой стороны, ближайших к врагу: сойдутся за пару ходов, ход считается быстро. Бежавшие в сохранении —
// снова в строю (как в смотрелке). Боевой математики здесь нет — только выбор и подписи.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleCore;
using Journal.Viewer;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class LineupPanel
    {
        readonly VisualElement root, cols;
        readonly Label title, sub, status;
        readonly Button startButton;
        readonly Action<string, HashSet<int>> start;
        readonly Action back;
        TrackerSave save; string path;
        readonly HashSet<int> chosen = new HashSet<int>();
        readonly Dictionary<int, Toggle> toggles = new Dictionary<int, Toggle>();
        readonly Dictionary<int, Label> heads = new Dictionary<int, Label>();
        public bool Visible => !root.ClassListContains("hidden");

        public LineupPanel(VisualElement parent, Action<string, HashSet<int>> start, Action back)
        {
            this.start = start; this.back = back;
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("lineup-box"); root.Add(box);
            title = new Label(); title.AddToClassList("menu-title"); box.Add(title);
            sub = new Label(); sub.AddToClassList("lineup-sub"); box.Add(sub);
            cols = new VisualElement(); cols.AddToClassList("lineup-cols"); box.Add(cols);
            status = new Label(); status.AddToClassList("menu-status"); box.Add(status);
            var foot = new VisualElement(); foot.AddToClassList("lineup-foot"); box.Add(foot);
            var backButton = new Button(() => { Hide(); back(); }) { text = "Назад к битвам" }; backButton.AddToClassList("menu-close"); foot.Add(backButton);
            var small = new Button(() => { Suggest(3); Refresh(); }) { text = "Малый состав: по 3" }; small.AddToClassList("menu-close"); foot.Add(small);
            startButton = new Button(() =>
            {
                status.text = "Строю битву…";
                root.schedule.Execute(() =>
                {
                    try { start(path, new HashSet<int>(chosen)); Hide(); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e); status.text = "Не вышло: " + e.Message; }
                }).ExecuteLater(30);
            }) { text = "НАЧАТЬ БИТВУ" };
            startButton.AddToClassList("go-button"); startButton.AddToClassList("lineup-start"); foot.Add(startButton);
        }

        public void Hide() => root.AddToClassList("hidden");

        // открыть сохранение: прочитать, разложить по сторонам, предложить малый состав
        public bool Show(string file)
        {
            try { save = SaveScene.Read(file); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); save = null; }
            if (save == null) return false;
            path = file; chosen.Clear(); toggles.Clear(); heads.Clear(); cols.Clear(); status.text = "";
            title.text = "Состав битвы";
            sub.text = $"{Path.GetFileName(file)} · ход {save.Turn} · карта {save.W:0} × {save.H:0} м. Отметь, кто выйдет на поле; стоят, как в сохранении.";
            foreach (int side in new[] { 1, 2 })
            {
                var col = new VisualElement(); col.AddToClassList("lineup-col"); cols.Add(col);
                var head = new Label(); head.AddToClassList("lineup-head"); head.AddToClassList(side == 1 ? "is-side1" : "is-side2"); col.Add(head); heads[side] = head;
                var bar = new VisualElement(); bar.AddToClassList("lineup-bar"); col.Add(bar);
                int sd = side;
                var all = new Button(() => { foreach (var u in Field(sd)) chosen.Add(u.Id); Refresh(); }) { text = "Все" }; all.AddToClassList("lineup-mini"); bar.Add(all);
                var none = new Button(() => { foreach (var u in Field(sd)) chosen.Remove(u.Id); Refresh(); }) { text = "Никого" }; none.AddToClassList("lineup-mini"); bar.Add(none);
                var list = new ScrollView(ScrollViewMode.Vertical); list.AddToClassList("lineup-list"); col.Add(list);
                foreach (var u in Field(side).OrderBy(u => FactionName(u)).ThenByDescending(u => u.Soldiers))
                {
                    var t = new Toggle { text = $"{u.Name} — {u.Soldiers:0}" + (u.Status == "fled" ? " (бежал, вернётся)" : "") + $" · {FactionName(u)}" };
                    t.AddToClassList("lineup-unit"); int id = u.Id;
                    t.RegisterValueChangedCallback(e => { if (e.newValue) chosen.Add(id); else chosen.Remove(id); Refresh(); });
                    toggles[id] = t; list.Add(t);
                }
            }
            Suggest(3); Refresh();
            root.RemoveFromClassList("hidden");
            return true;
        }

        IEnumerable<Unit> Field(int side) => save.Units.Where(u => SaveScene.OnField(u) && SideOf(u) == side);
        int SideOf(Unit u) => save.Side.TryGetValue(u.FactionId ?? 0, out var s) ? s : 1;
        string FactionName(Unit u) => save.Factions.TryGetValue(u.FactionId ?? 0, out var f) ? f.Name : "без фракции";
        string SideName(int side) => string.Join(", ", Field(side).Select(FactionName).Distinct());

        // малый состав: по n отрядов каждой стороны — ближайших к середине врага (сойдутся быстрее)
        void Suggest(int n)
        {
            chosen.Clear();
            foreach (int side in new[] { 1, 2 })
            {
                var foes = Field(3 - side).ToList(); if (foes.Count == 0) continue;
                double ex = foes.Average(u => u.MapX * save.W / 100), ey = foes.Average(u => u.MapY * save.H / 100);
                foreach (var u in Field(side).OrderBy(u => Math.Pow(u.MapX * save.W / 100 - ex, 2) + Math.Pow(u.MapY * save.H / 100 - ey, 2)).Take(n)) chosen.Add(u.Id);
            }
        }

        void Refresh()
        {
            foreach (var kv in toggles) kv.Value.SetValueWithoutNotify(chosen.Contains(kv.Key));
            int ready = 0;
            foreach (int side in new[] { 1, 2 })
            {
                var sel = Field(side).Where(u => chosen.Contains(u.Id)).ToList();
                heads[side].text = $"{SideName(side)}\nвыбрано {sel.Count} из {Field(side).Count()} · бойцов {sel.Sum(u => u.Soldiers):0}";
                if (sel.Count > 0) ready++;
            }
            startButton.SetEnabled(ready == 2);
            if (ready < 2) status.text = "Нужен хотя бы один отряд с каждой стороны"; else if (status.text.StartsWith("Нужен")) status.text = "";
        }
    }
}
