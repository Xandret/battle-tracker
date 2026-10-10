// ═══════════ ArmyPreview.cs — вид отряда в редакторе армий (Алекс 10.10.2026: «видеть стилистику отряда, его полководца») ═══════════
// Маленький бой движка: один отряд по шаблону (несколько рядов) стоит лицом к зрителю; запись — как в бою (Recorder),
// рисует та же смотрелка (MenView — бойцы и кони в снаряжении и стиле, Banners — знамя и личный стяг полководца)
// своей камерой в текстуру. Сцена — далеко от любого поля (−60 км), главная камера её не видит. Перестраивается,
// только когда меняется облик: шаблон, стиль, цвет, полководец.
using System;
using BattleCore;
using Journal.Viewer;
using UnityEngine;
using Terrain = BattleCore.Terrain;

namespace Journal.Play
{
    public sealed class ArmyPreview
    {
        public readonly RenderTexture Tex;
        readonly GameObject stage; readonly Camera cam; readonly MenView men; readonly Banners banners;
        string key;
        const float Ox = -60000, Oy = 60000;   // где сцена в мире

        public ArmyPreview(int w, int h)
        {
            Tex = new RenderTexture(w, h, 16) { name = "Вид отряда" }; Tex.Create();
            stage = new GameObject("Вид отряда (редактор армий)"); stage.transform.position = new Vector3(Ox, Oy, 0);
            men = new MenView(stage.transform); banners = new Banners(stage.transform);
            var cg = new GameObject("Камера вида отряда"); cg.transform.SetParent(stage.transform, false);
            cam = cg.AddComponent<Camera>(); cam.orthographic = true; cam.targetTexture = Tex;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(96, 104, 64, 255);   // трава
            cam.enabled = false;
        }
        // показывать (камера рисует, только пока окно открыто)
        public bool Active { set => cam.enabled = value; }

        // отряд: шаблон (вид бойцов), род войск, стиль, цвет фракции "#rrggbb", полководец (null — без него)
        public void Show(string tpl, string style, string color, string cmdName, double valor = 10, float shown = 0, bool card = false)
        {
            string k = $"{tpl}|{style}|{color}|{cmdName}|{shown:0.00}|{card}";
            if (k == key || !men.Ok) return;
            key = k;
            var R = Rules.Base;
            var map = Terrain.Create(240, 240, Terrain.Id("field"));
            var geo = new Geo { Map = map, W = Terrain.WidthM(map), H = Terrain.HeightM(map) };
            var c = cmdName != null ? new Commander { Id = 1, Name = cmdName, Valor = valor, FactionId = 1 } : null;
            var bt = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(7).Next, CommanderOf = u => u.CommanderId == 1 ? c : null });
            var t = Templates.Get(tpl) ?? Templates.Get("infantry");
            bool horse = t.Type == "cavalry";
            var unit = t.Make(1, "вид", horse ? 30 : 48, 1);
            if (c != null) unit.CommanderId = 1;
            var m = bt.Add(unit, 120, 120, 180);   // лицом на юг — к зрителю
            bt.Order(m, new MoveOrder { Kind = OrderKind.Hold, X = m.P.X, Y = m.P.Y, Facing = 180 });
            var rcd = new Recorder("вид", "", geo, bt.Movers, _ => tpl, bt, 1, _ => color, _ => style);
            rcd.Snap();
            var rec = rcd.Rec;
            men.SetRecording(rec);
            var raw = new[] { Hex(color) }; var lin = new[] { Lin(raw[0]) };
            // кадр — крупно передние ряды и полководец (он у знамени, на пятую глубины к фронту), сверху — место знамени;
            // shown — соотношение сторон окна в интерфейсе: картинка обрезается по нему
            float depth = (float)m.P.Fp.Depth, aspect = shown > 0.2f ? shown : (float)Tex.width / Tex.height;
            bool small = card;   // карточка — ещё ближе
            float size = (horse ? (small ? 5.2f : 7.5f) : (small ? 3.0f : 4.6f)) * Mathf.Clamp(1.9f / aspect, 0.8f, 1.6f);
            cam.orthographicSize = size;
            cg(cam).localPosition = new Vector3((float)m.P.X, -((float)m.P.Y + depth * 0.2f) + size * 0.22f, -10);
            float ppm = Tex.height / (2 * size);
            var view = new Rect(-1e4f, -1e4f, 2e4f, 2e4f);
            // подсветка выбранного в бою и туман — не отсюда
            var lit = MenView.Lit; int side = BattleViewer.ViewSide; MenView.Lit = null; BattleViewer.ViewSide = 0;
            try { men.Draw(0, raw, view, ppm); banners.Draw(rec, 0, lin, Lin, view, ppm, men.CmdAt); }
            finally { MenView.Lit = lit; BattleViewer.ViewSide = side; }
        }
        // картинка на карточку отряда: та же сцена, снимок в маленькую текстуру; одинаковый облик — один снимок
        readonly System.Collections.Generic.Dictionary<string, Texture2D> thumbs = new System.Collections.Generic.Dictionary<string, Texture2D>();
        public Texture2D Cached(string tpl, string style, string color) => thumbs.TryGetValue($"{tpl}|{style}|{color}", out var t) ? t : null;
        public Texture2D Thumb(string tpl, string style, string color)
        {
            string k = $"{tpl}|{style}|{color}";
            if (thumbs.TryGetValue(k, out var have)) return have;
            const int W = 192, H = 96;
            Show(tpl, style, color, null, 10, (float)W / H, true);
            var rt = RenderTexture.GetTemporary(W, H, 16);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = Tex;
            var was = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "карточка " + k };
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
            key = null;   // большой вид — перерисовать при следующем показе
            return thumbs[k] = tex;
        }
        static Transform cg(Camera c) => c.transform;
        static Color32 Hex(string h) => ColorUtility.TryParseHtmlString(h ?? "", out var c) ? (Color32)c : new Color32(110, 106, 98, 255);
        static Color32 Lin(Color32 c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? (Color32)((Color)c).linear : c;
    }
}
