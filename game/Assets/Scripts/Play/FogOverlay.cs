// ═══════════ FogOverlay.cs — туман войны на карте (Г107) ═══════════
// Смотрим глазами стороны (PlayController.ViewSide > 0): клетки, которых она не видит (Battle.SeenCells — блоки 5 × 5 м от
// своих отрядов с прямой видимостью), темнеют; края мягкие (текстура по клеткам с билинейным сглаживанием). Вид ГМа — без
// тумана. Обновляется раз в 0,5 с. Слой 8 — над землёй, павшими и стенами, под конями и бойцами (свои видны всегда, чужих
// невидимых и так не рисуем).
using BattleCore;
using UnityEngine;

namespace Journal.Play
{
    [RequireComponent(typeof(PlayController))]
    public sealed class FogOverlay : MonoBehaviour
    {
        PlayController pc;
        GameObject go; Mesh mesh; Material mat; Texture2D tex; Color32[] px;
        TerrainMap shownMap; float nextAt;

        void Start()
        {
            pc = GetComponent<PlayController>();
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            mat = new Material(sh);
            mesh = new Mesh();
            go = new GameObject("Туман войны"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = 8;
            go.SetActive(false);
        }

        void LateUpdate()
        {
            var bt = pc?.Battle; var map = pc?.Game?.Geo?.Map;
            bool on = bt != null && map != null && pc.ViewSide > 0;
            if (!on) { if (go.activeSelf) go.SetActive(false); return; }
            if (map != shownMap) Build(map, pc.Game.Geo);
            if (Time.unscaledTime < nextAt && go.activeSelf) return;
            nextAt = Time.unscaledTime + 0.5f;
            // туман пересчитывает поток счёта хода — берём снятое в запись (Recorder, раз в секунду), под её замком
            bool[] cells = null; var rec = pc.ViewRec;
            if (rec != null) lock (rec) rec.FogCells.TryGetValue(pc.ViewSide, out cells);
            if (cells == null && !pc.Computing) cells = bt.SeenCells(pc.ViewSide);
            if (cells == null || cells.Length != px.Length) { go.SetActive(false); return; }
            var dark = new Color32(14, 16, 22, 150); var clear = new Color32(14, 16, 22, 0);
            for (int i = 0; i < px.Length; i++) px[i] = cells[i] ? clear : dark;
            tex.SetPixels32(px); tex.Apply(false);
            if (!go.activeSelf) go.SetActive(true);
        }

        // квадрат во всю карту: мир Unity X = x, Y = −y; клетка (x, y) текстуры — клетка карты
        void Build(TerrainMap map, Geo geo)
        {
            shownMap = map;
            if (tex != null) Destroy(tex);
            tex = new Texture2D(map.W, map.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[map.W * map.H];
            mat.mainTexture = tex;
            float W = (float)geo.W, H = (float)geo.H;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(W, 0, 0), new Vector3(W, -H, 0), new Vector3(0, -H, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
        }
    }
}
