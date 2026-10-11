// ═══════════ PlaySceneBuilder.cs — сборка сцены игры (Assets/Scenes/Play.unity) ═══════════
// Меню «Журнал → Собрать сцену игры». Сцена — копия Battle.unity (камера, свет, смотрелка) плюс объект «Игра»:
// PlayController (ход и приказы), OrderOverlay (подсказки приказов на карте), UIDocument + PlayHud (интерфейс).
// Для UIDocument нужны настройки панели (PlayPanel.asset) с темой (PlayTheme.tss) — создаются здесь же, если их нет.
// Сборку можно повторять: сцена пересобирается заново из Battle.unity.
using System.IO;
using Journal.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class PlaySceneBuilder
{
    const string Dir = "Assets/UI/Play";
    const string ThemePath = Dir + "/PlayTheme.tss";
    const string PanelPath = Dir + "/PlayPanel.asset";
    const string HudPath = Dir + "/PlayHud.uxml";
    const string FromScene = "Assets/Scenes/Battle.unity";
    const string ScenePath = "Assets/Scenes/Play.unity";

    [MenuItem("Журнал/Собрать сцену игры")]
    public static void Build()
    {
        Panel();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(FromScene, ScenePath)) { Debug.LogError("Не скопировать " + FromScene); return; }
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // ассеты грузим после открытия сцены: OpenScene выгружает неиспользуемые, и ссылка, взятая до него, сохранилась бы пустой
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);

        var game = new GameObject("Игра");
        game.AddComponent<PlayController>();
        game.AddComponent<OrderOverlay>();
        var doc = game.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
        game.AddComponent<PlayHud>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Сцена игры собрана: {ScenePath} (интерфейс {(doc.visualTreeAsset ? "есть" : "НЕ НАЙДЕН")}, панель {(panel ? "есть" : "НЕТ")})");
    }

    // Настройки панели: масштаб под экран от 1600 × 900, тема по умолчанию (без неё UI Toolkit не рисует стандартные элементы)
    static void Panel()
    {
        if (!File.Exists(ThemePath))
        {
            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath);
        }
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, PanelPath); }
        ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1600, 900);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;
        EditorUtility.SetDirty(ps);
        AssetDatabase.SaveAssets();
    }
}
