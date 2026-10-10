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

        public MainMenu(VisualElement parent, Action onContinue, Action onNew, Action onArmies, Action onMaps, Action onSettings, Action onExit)
        {
            root = new VisualElement(); root.AddToClassList("main-menu"); root.AddToClassList("hidden"); parent.Add(root);
            // заставка (Алекс 10.10.2026): за меню идёт бой сам по себе, по краям экрана — затемнение
            var vig = new VisualElement(); vig.AddToClassList("main-vignette"); vig.pickingMode = PickingMode.Ignore;
            vig.style.backgroundImage = new StyleBackground(Vignette()); root.Add(vig);
            var box = new VisualElement(); box.AddToClassList("main-box"); root.Add(box);
            var title = new Label("Журнал боевых действий"); title.AddToClassList("main-title"); box.Add(title);
            var orn = new VisualElement(); orn.AddToClassList("main-orn"); box.Add(orn);
            var l1 = new VisualElement(); l1.AddToClassList("main-orn-line"); orn.Add(l1);
            var dia = new VisualElement(); dia.AddToClassList("main-orn-dia"); orn.Add(dia);
            var l2 = new VisualElement(); l2.AddToClassList("main-orn-line"); orn.Add(l2);
            var sub = new Label("тактические сражения"); sub.AddToClassList("main-sub"); box.Add(sub);
            Button B(string text, Action act, string cls = null)
            {
                var b = new Button(act) { text = text }; b.AddToClassList("main-button"); if (cls != null) b.AddToClassList(cls); box.Add(b); return b;
            }
            continueButton = B("Продолжить битву", () => { Hide(); onContinue(); }, "is-gold");
            B("Новая битва", () => { Hide(); onNew(); });
            B("Армии", () => { Hide(); onArmies(); });
            B("Редактор карт", () => { Hide(); onMaps(); });
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
            root.EnableInClassList("is-splash", !canContinue);
            root.RemoveFromClassList("hidden");
        }
        public void Hide() => root.AddToClassList("hidden");

        // затемнение краёв: прозрачно в середине-справа (там бой), темно слева под меню и по краям
        static UnityEngine.Texture2D Vignette()
        {
            const int W = 128, H = 72;
            var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGBA32, false) { wrapMode = UnityEngine.TextureWrapMode.Clamp };
            var px = new UnityEngine.Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    float dx = (u - 0.6f) / 0.62f, dy = (v - 0.5f) / 0.62f, r = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy);
                    float a = UnityEngine.Mathf.SmoothStep(0.15f, 1.0f, r) * 0.85f + UnityEngine.Mathf.Clamp01(0.42f - u) / 0.42f * 0.55f;
                    px[y * W + x] = new UnityEngine.Color32(8, 6, 4, (byte)(255 * UnityEngine.Mathf.Clamp01(a)));
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }
    }
}
