// ═══════════ MainMenu.cs — главное меню игры и меню паузы (Esc в бою) ═══════════
// В начале — заставка: «Новая битва», «Армии», «Настройки», «Выход». В бою по Esc или кнопке «Меню» — то же меню
// поверх поля, сверху «Продолжить битву». Здесь только переходы между окнами; что делает каждое — у его владельца.
using System;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class MainMenu
    {
        readonly VisualElement root;
        readonly Button continueButton;
        readonly Label hint;
        public bool Visible => !root.ClassListContains("hidden");
        public bool CanContinue { get; private set; }

        public MainMenu(VisualElement parent, Action onContinue, Action onNew, Action onArmies, Action onSettings, Action onExit)
        {
            root = new VisualElement(); root.AddToClassList("main-menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("main-box"); root.Add(box);
            var title = new Label("Журнал боевых действий"); title.AddToClassList("main-title"); box.Add(title);
            var sub = new Label("тактические сражения"); sub.AddToClassList("main-sub"); box.Add(sub);
            Button B(string text, Action act, string cls = null)
            {
                var b = new Button(act) { text = text }; b.AddToClassList("main-button"); if (cls != null) b.AddToClassList(cls); box.Add(b); return b;
            }
            continueButton = B("Продолжить битву", () => { Hide(); onContinue(); }, "is-gold");
            B("Новая битва", () => { Hide(); onNew(); });
            B("Армии", () => { Hide(); onArmies(); });
            B("Настройки", () => { Hide(); onSettings(); });
            B("Выход", onExit, "is-quiet");
            hint = new Label(); hint.AddToClassList("main-hint"); box.Add(hint);
            var ver = new Label("версия " + UnityEngine.Application.version); ver.AddToClassList("main-hint"); ver.style.opacity = 0.6f; box.Add(ver);
        }

        // canContinue — идёт битва, в которую можно вернуться
        public void Show(bool canContinue)
        {
            CanContinue = canContinue;
            continueButton.EnableInClassList("hidden", !canContinue);
            hint.text = canContinue ? "Esc — вернуться к битве" : "Собери армию в «Армиях» или открой сохранение трекера в «Новой битве»";
            root.EnableInClassList("is-pause", canContinue);
            root.RemoveFromClassList("hidden");
        }
        public void Hide() => root.AddToClassList("hidden");
    }
}
