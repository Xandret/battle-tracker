// ═══════════ BuildGame.cs — сборка игры для Windows (exe), меню «Журнал → Собрать игру (Windows)» ═══════════
// Сцена одна — Play (главное меню, битвы, армии). Шейдеры, которые код ищет по имени (Shader.Find), Unity в сборку сам не
// берёт — добавляем их в «всегда включённые». Итог — game/Build/Journal_<версия>/ (вне git): Journal.exe, данные и папка
// Saves рядом с exe (игра ищет сохранения в ней и открывает любые через окно «Открыть»). Version — номер сборки.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Journal.EditorTools
{
    public static class BuildGame
    {
        public const string Version = "0.3";
        static readonly string[] Scenes = { "Assets/Scenes/Play.unity" };
        static readonly string[] FoundByName = { "Journal/Men", "Journal/Ground", "Journal/MapImage", "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            // пост-обработка 2D-рендерера (свечение «миниатюры», MiniatureLook): без них в сборке Bloom молча не работает
            "Hidden/Universal Render Pipeline/Bloom", "Hidden/Universal Render Pipeline/BokehDepthOfField", "Hidden/Universal Render Pipeline/GaussianDepthOfField",
            "Hidden/Universal Render Pipeline/CameraMotionBlur", "Hidden/Universal Render Pipeline/PaniniProjection" };
        public static string LastReport = "";

        [MenuItem("Журнал/Собрать игру (Windows)")]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            IncludeShaders();
            PlayerSettings.productName = "Журнал боевых действий";
            PlayerSettings.companyName = "Tracker_Boya";
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            EditorBuildSettings.scenes = Scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Build", "Journal_" + Version));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes, locationPathName = Path.Combine(dir, "Journal.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
            });
            var s = rep.summary;
            LastReport = $"{s.result}: {s.totalErrors} ошибок, {s.totalWarnings} предупреждений, {s.totalSize / 1048576.0:0} МБ, {s.totalTime.TotalSeconds:0} с → {dir}";
            if (s.result == BuildResult.Succeeded) Directory.CreateDirectory(Path.Combine(dir, "Saves"));
            Debug.Log("Сборка: " + LastReport);
            return LastReport;
        }

        // шейдеры по имени — в «всегда включённые» (GraphicsSettings), если их там ещё нет
        static void IncludeShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in FoundByName)
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning("Сборка: нет шейдера " + name); continue; }
                bool has = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
                if (has) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
