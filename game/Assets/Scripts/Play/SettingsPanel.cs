// ═══════════ SettingsPanel.cs — окно настроек (GameSettings): экран, интерфейс, показ хода ═══════════
// Всё применяется сразу и запоминается. Экран (полный экран, разрешение) действует только в собранной игре.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class SettingsPanel
    {
        readonly VisualElement root;
        public bool Visible => !root.ClassListContains("hidden");
        public event Action Changed;   // подписи, скорость — HUD перечитает

        static readonly (float V, string Name)[] Speeds = { (1, "×1 — как в жизни"), (2, "×2"), (4, "×4") };

        public SettingsPanel(VisualElement parent, Action back)
        {
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("settings-box"); root.Add(box);
            var title = new Label("Настройки"); title.AddToClassList("menu-title"); box.Add(title);

            Head(box, "Экран");
            var full = new Toggle("Полный экран") { value = GameSettings.Fullscreen }; full.AddToClassList("set-field"); box.Add(full);
            full.RegisterValueChangedCallback(e => GameSettings.Fullscreen = e.newValue);
            var res = GameSettings.Resolutions().ToList();
            string cur = string.IsNullOrEmpty(GameSettings.Resolution) ? $"{Screen.width} × {Screen.height}" : GameSettings.Resolution;
            if (!res.Contains(cur)) res.Insert(0, cur);
            var resBox = new DropdownField("Разрешение", res, res.IndexOf(cur)); resBox.AddToClassList("set-field"); box.Add(resBox);
            resBox.RegisterValueChangedCallback(e => GameSettings.Resolution = e.newValue);
            var vs = new Toggle("Вертикальная синхронизация") { value = GameSettings.VSync }; vs.AddToClassList("set-field"); box.Add(vs);
            vs.RegisterValueChangedCallback(e => GameSettings.VSync = e.newValue);
            if (Application.isEditor) { var n = new Label("В редакторе экран не меняется — только в собранной игре."); n.AddToClassList("set-note"); box.Add(n); }

            Head(box, "Интерфейс");
            var scale = new SliderInt("Масштаб", 70, 160) { value = GameSettings.UiScale, showInputField = false }; scale.AddToClassList("set-field"); box.Add(scale);
            var scaleLabel = new Label($"{GameSettings.UiScale}%"); scaleLabel.AddToClassList("set-value"); scale.Add(scaleLabel);
            // применять, когда отпустили: пока тянешь, панель масштабируется под рукой
            scale.RegisterValueChangedCallback(e => scaleLabel.text = $"{e.newValue / 10 * 10}%");
            scale.RegisterCallback<PointerCaptureOutEvent>(_ => { GameSettings.UiScale = scale.value / 10 * 10; scale.SetValueWithoutNotify(GameSettings.UiScale); });
            var tags = new Toggle("Подписи над отрядами") { value = GameSettings.Tags }; tags.AddToClassList("set-field"); box.Add(tags);
            tags.RegisterValueChangedCallback(e => { GameSettings.Tags = e.newValue; Changed?.Invoke(); });

            Head(box, "Звук");
            var vol = new SliderInt("Громкость", 0, 100) { value = GameSettings.Volume, showInputField = false }; vol.AddToClassList("set-field"); box.Add(vol);
            var volLabel = new Label($"{GameSettings.Volume}%"); volLabel.AddToClassList("set-value"); vol.Add(volLabel);
            vol.RegisterValueChangedCallback(e => { GameSettings.Volume = e.newValue; volLabel.text = $"{e.newValue}%"; });
            var gore = new Toggle("Жестокие звуки (крики павших)") { value = GameSettings.Gore }; gore.AddToClassList("set-field"); box.Add(gore);
            gore.RegisterValueChangedCallback(e => GameSettings.Gore = e.newValue);

            Head(box, "Битва");
            int si = Array.FindIndex(Speeds, s => Mathf.Approximately(s.V, GameSettings.Speed));
            var speed = new DropdownField("Скорость показа хода", Speeds.Select(s => s.Name).ToList(), Math.Max(0, si)); speed.AddToClassList("set-field"); box.Add(speed);
            speed.RegisterValueChangedCallback(e => { GameSettings.Speed = Speeds[Math.Max(0, speed.index)].V; Changed?.Invoke(); });
            var keys = new Label("Клавиши: Enter — «Ход!», пробел — пауза, 1/2/3 — скорость, Tab — другая сторона, Ctrl+A — все отряды, Д/О/С — держать, отступить, сплотить, ⌫ — отменить приказ, F — вся карта, Esc — меню.");
            keys.AddToClassList("set-note"); box.Add(keys);

            var foot = new VisualElement(); foot.AddToClassList("lineup-foot"); box.Add(foot);
            var done = new Button(() => { PlayerPrefs.Save(); Hide(); back(); }) { text = "Готово" }; done.AddToClassList("go-button"); done.AddToClassList("lineup-start"); foot.Add(done);
        }

        static void Head(VisualElement box, string text) { var h = new Label(text); h.AddToClassList("set-head"); box.Add(h); }
        public void Show() => root.RemoveFromClassList("hidden");
        public void Hide() => root.AddToClassList("hidden");
    }
}
