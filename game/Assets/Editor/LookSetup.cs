// ═══════════ LookSetup.cs — проход «миниатюры на столе» (TiltShift) в 2D-рендерере проекта (В15) ═══════════
// Один раз: «Журнал → Эффект миниатюр в рендерере» добавляет в Assets/Settings/Renderer2D.asset проход на весь экран
// с материалом Assets/Settings/TiltShift.mat (шейдер Journal/TiltShift), после постобработки. Повторно — не дублирует.
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class LookSetup
{
    const string RendererPath = "Assets/Settings/Renderer2D.asset", MatPath = "Assets/Settings/TiltShift.mat";

    [MenuItem("Журнал/Эффект миниатюр в рендерере")]
    public static string Install()
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
        if (data == null) return "нет " + RendererPath;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Journal/TiltShift")) { name = "TiltShift" };
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        var have = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.passMaterial == mat);
        if (have != null) return "уже есть";
        var feat = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        feat.name = "Миниатюры на столе";
        feat.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        feat.fetchColorBuffer = true;
        feat.passMaterial = mat;
        feat.passIndex = 0;
        AssetDatabase.AddObjectToAsset(feat, data);
        data.rendererFeatures.Add(feat);
        // список признаков рендерера хранит и их идентификаторы — пусть рендерер пересоберёт его сам
        var so = new SerializedObject(data);
        var map = so.FindProperty("m_RendererFeatureMap");
        if (map != null)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out _, out long id);
            map.arraySize = data.rendererFeatures.Count;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return "добавлен";
    }
}
