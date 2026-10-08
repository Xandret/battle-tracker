// ═══════════ GameSettings.cs — настройки игры: экран, интерфейс, показ хода; хранятся в PlayerPrefs ═══════════
// Применяются сразу и при запуске. Экран (полный экран, разрешение) в редакторе не действует — только в сборке.
// Масштаб интерфейса — копия PanelSettings у UIDocument (сам ассет не трогаем, иначе правка осталась бы в проекте);
// панель масштабируется по размеру экрана от опорного разрешения — крупнее интерфейс = меньше опорное разрешение.
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public static class GameSettings
    {
        const string P = "journal.settings.";
        public static bool Fullscreen { get => PlayerPrefs.GetInt(P + "fullscreen", 1) == 1; set { PlayerPrefs.SetInt(P + "fullscreen", value ? 1 : 0); ApplyScreen(); } }
        public static string Resolution { get => PlayerPrefs.GetString(P + "resolution", ""); set { PlayerPrefs.SetString(P + "resolution", value); ApplyScreen(); } }
        public static bool VSync { get => PlayerPrefs.GetInt(P + "vsync", 1) == 1; set { PlayerPrefs.SetInt(P + "vsync", value ? 1 : 0); ApplyVSync(); } }
        public static int UiScale { get => PlayerPrefs.GetInt(P + "uiScale", 100); set { PlayerPrefs.SetInt(P + "uiScale", Mathf.Clamp(value, 70, 160)); ApplyUi(); } }
        public static float Speed { get => PlayerPrefs.GetFloat(P + "speed", 1); set => PlayerPrefs.SetFloat(P + "speed", value); }
        public static bool Tags { get => PlayerPrefs.GetInt(P + "tags", 1) == 1; set => PlayerPrefs.SetInt(P + "tags", value ? 1 : 0); }
        public static bool AutoPause { get => PlayerPrefs.GetInt(P + "autoPause", 0) == 1; set => PlayerPrefs.SetInt(P + "autoPause", value ? 1 : 0); }

        static UIDocument doc; static Vector2Int baseRef = new Vector2Int(1600, 900); static float baseScale = 1;

        // при запуске: экран, синхронизация, масштаб интерфейса
        public static void Init(UIDocument d)
        {
            doc = d;
            if (doc != null && doc.panelSettings != null) { doc.panelSettings = Object.Instantiate(doc.panelSettings); baseRef = doc.panelSettings.referenceResolution; baseScale = doc.panelSettings.scale; }
            ApplyScreen(); ApplyVSync(); ApplyUi();
        }

        // разрешения экрана без повторов (частоты обновления не различаем), от больших к меньшим
        public static string[] Resolutions() =>
            Screen.resolutions.Select(r => $"{r.width} × {r.height}").Distinct().Reverse().DefaultIfEmpty($"{Screen.width} × {Screen.height}").ToArray();

        static void ApplyScreen()
        {
            if (Application.isEditor) return;
            int w = Screen.width, h = Screen.height;
            var parts = Resolution.Split('×');
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var rw) && int.TryParse(parts[1].Trim(), out var rh)) { w = rw; h = rh; }
            Screen.SetResolution(w, h, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }
        static void ApplyVSync() { QualitySettings.vSyncCount = VSync ? 1 : 0; Application.targetFrameRate = VSync ? -1 : 144; }
        static void ApplyUi()
        {
            if (doc == null || doc.panelSettings == null) return;
            float k = UiScale / 100f;
            if (doc.panelSettings.scaleMode == PanelScaleMode.ScaleWithScreenSize)
                doc.panelSettings.referenceResolution = new Vector2Int(Mathf.RoundToInt(baseRef.x / k), Mathf.RoundToInt(baseRef.y / k));
            else doc.panelSettings.scale = baseScale * k;
        }
    }
}
