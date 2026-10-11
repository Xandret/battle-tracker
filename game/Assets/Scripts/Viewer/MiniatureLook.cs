// ═══════════ MiniatureLook.cs — «миниатюры на столе» (В15): цветокоррекция, тёплый свет, виньетка, размытие краёв ═══════════
// Глобальный объём постобработки URP, созданный в коде (сцены не трогаем): чуть больше контраста и насыщенности,
// тёплый баланс белого, мягкая виньетка. Размытие верха и низа кадра — проход TiltShift в 2D-рендерере (LookSetup),
// его сила — глобальная _JTilt. Вкл/выкл — On (клавиша F8 в смотрелке и в игре); свет на бойцах — F7 (MenView.Light).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Journal.Viewer
{
    public static class MiniatureLook
    {
        static Volume volume;
        static bool on = true;

        public static bool On
        {
            get => on;
            set { on = value; Apply(); }
        }

        // камера — с постобработкой; объём — один на сцену
        public static void Setup(Camera cam)
        {
            var cd = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            if (volume == null)
            {
                var go = new GameObject("Облик: миниатюры");
                volume = go.AddComponent<Volume>();
                volume.isGlobal = true; volume.priority = 10;
                var p = ScriptableObject.CreateInstance<VolumeProfile>();
                var ca = p.Add<ColorAdjustments>(true);
                ca.contrast.Override(12f); ca.saturation.Override(5f); ca.postExposure.Override(0.03f);
                var wb = p.Add<WhiteBalance>(true);
                wb.temperature.Override(4f); wb.tint.Override(3f);   // теплее, но трава не в жёлтое
                var vg = p.Add<Vignette>(true);
                vg.intensity.Override(0.3f); vg.smoothness.Override(0.5f); vg.rounded.Override(false);
                volume.sharedProfile = p;
            }
            Apply();
        }

        static void Apply()
        {
            if (volume != null) volume.weight = on ? 1 : 0;
            Shader.SetGlobalFloat("_JTilt", on ? 1 : 0);
            Shader.SetGlobalFloat("_JTiltPx", 5f);
        }
    }
}
