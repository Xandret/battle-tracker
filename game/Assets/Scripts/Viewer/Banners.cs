// ═══════════ Banners.cs — знамёна над отрядами (Г7, В11), как drawBanner / drawDropped полигона ═══════════
// Знамя — в середине строя, чуть ближе к первой шеренге (на пятую глубины): древко и полотнище «ласточкин хвост» цвета
// отряда со светлой полосой. Размер — в пикселях экрана (древко 28 px при 900 px высоты; ×1…1,5 по приближению):
// видно и издали.
// Стоит — там, где отряд виден (Recording.UnitBox: рамка вокруг его бойцов), а не где он в счёте: на скаку бойцы
// отстают от мест в строю, и знамя убегало бы вперёд.
// Бегство (В11): знамя падает и лежит там, где отряд побежал, пока бежит или ушёл; «сплотить» ждёт или только что
// сплотились (первые 4 с) — знамя поднято и машет. Рисуется сеткой: цвет вершин, слой 30 (над бойцами и стрелами).
using System.Collections.Generic;
using UnityEngine;

namespace Journal.Viewer
{
    public sealed class Banners
    {
        readonly Mesh mesh;
        readonly List<Vector3> V = new List<Vector3>(); readonly List<Color32> C = new List<Color32>(); readonly List<int> I = new List<int>();
        static readonly Color32 Ink = new Color32(43, 38, 33, 255), Wood = new Color32(122, 90, 54, 255), Shine = new Color32(255, 255, 255, 140);

        public Banners(Transform parent)
        {
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.MarkDynamic();
            var go = new GameObject("Знамёна"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = new Material(sh); r.sortingOrder = 30;
        }

        // приближение → размер знамени (как в полигоне: view.s / 12, но не мельче 1 — издали и посреди строя его иначе не
        // видно; не крупнее 1,5) × масштаб экрана, как у панелей игры (эталон — 900 px высоты, мельче не делаем)
        public static float Scale(float ppm) => Mathf.Clamp(ppm / 12, 1f, 1.5f) * Mathf.Max(1f, Screen.height / 900f);
        // верх знамени над его основанием, px экрана — панелям игры: табличку отряда ставить выше
        public static float TopPx(float ppm) => 27 * Scale(ppm);
        // основание знамени (точка карты): середина видимой рамки отряда, на пятую глубины к фронту
        public static Vector2 Base(float x, float y, float facingDeg, float depth)
        {
            float h = facingDeg * Mathf.Deg2Rad;
            return new Vector2(x + Mathf.Sin(h) * depth * 0.2f, y - Mathf.Cos(h) * depth * 0.2f);
        }

        public void Hide() => mesh.Clear();

        // все знамёна кадра; cols — цвет отряда (sRGB, как у бойцов), lin — перевод в цвет вершины проекта
        public void Draw(Recording rec, double t, Color32[] cols, System.Func<Color32, Color32> lin, Rect view, float ppm)
        {
            V.Clear(); C.Clear(); I.Clear();
            if (rec == null || rec.Frames.Count == 0 || ppm < 0.25f) { mesh.Clear(); return; }
            float k = 1 / ppm, s = Scale(ppm) * k;   // метров на пиксель знамени
            int fi = Mathf.Clamp((int)System.Math.Floor(t / rec.Dt), 0, rec.Frames.Count - 1);
            float pad = 34 * s;                      // знамя целиком: древко 28 px вверх, полотнище и лежащее — до 17 px вбок
            for (int ui = 0; ui < rec.Units.Count; ui++)
            {
                int st = rec.StateAt(ui, fi), since = Since(rec, ui, fi);
                var col = lin(cols[ui]);
                if (st == 1 || st == 2)
                {
                    // упало там, где отряд побежал; лежит, пока бежит или ушёл
                    var f0 = rec.Frames[Mathf.Min(since, rec.Frames.Count - 1)][ui];
                    if (!In(view, f0[0], f0[1], pad)) continue;
                    var dim = lin(Mix(cols[ui], new Color32(122, 116, 102, 255), 0.35f));
                    Fallen(new Vector2(f0[0], f0[1]), (float)Journal.Art.Kits.Hash(ui, since) * 6.283f, s, dim);
                    continue;
                }
                if (!rec.UnitBox(ui, t, out var x, out var y, out var face, out _, out var depth)) continue;
                // к фронту — на пятую глубины строя, не больше: у рассыпанных (сплочение, бегство) рамка бойцов глубока
                var b = Base(x, y, face, Mathf.Min(depth, (float)rec.Units[ui].Depth));
                if (!In(view, b.x, b.y, pad)) continue;
                bool wave = st == 3 || (st == 4 && (fi - since) * rec.Dt < 4);
                Standing(b, s, col, wave, (float)t + ui * 0.37f);
            }
            mesh.Clear();
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetTriangles(I, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10));
        }

        static int Since(Recording rec, int ui, int f)
        {
            var L = rec.States?[ui]; if (L == null) return 0;
            int since = 0; for (int i = 0; i < L.Count && L[i] <= f; i += 2) since = L[i];
            return since;
        }
        static bool In(Rect v, float x, float y, float pad) => x > v.xMin - pad && x < v.xMax + pad && y > v.yMin - pad && y < v.yMax + pad;
        static Color32 Mix(Color32 a, Color32 b, float k) => new Color32((byte)(a.r + (b.r - a.r) * k), (byte)(a.g + (b.g - a.g) * k), (byte)(a.b + (b.b - a.b) * k), 255);

        // ── знамя стоит: точки — в пикселях знамени (x вправо, y вниз, как в canvas полигона), s — метров на пиксель ──
        Vector2 o; float sc; float rotC = 1, rotS = 0;
        Vector3 P(float px, float py)
        {
            float rx = px * rotC - py * rotS, ry = px * rotS + py * rotC;   // поворот (у лежащего знамени)
            return new Vector3(o.x + rx * sc, -(o.y + ry * sc), 0);        // карта (y вниз) → мир Unity
        }
        void Standing(Vector2 at, float s, Color32 col, bool wave, float t)
        {
            o = at; sc = s; rotC = 1; rotS = 0;
            float Wy(float x) => wave ? Mathf.Sin(t * 7 - x * 0.35f) * 2.2f * x / 17 : 0;   // полотнище машет, у древка неподвижно
            Line(0, 2, 0, -26, 2.6f, Ink); Line(0, 2, 0, -26, 1.4f, Wood);
            var p = new[] { new Vector2(0, -25), new Vector2(8.5f, -25 + Wy(8.5f)), new Vector2(17, -25 + Wy(17)), new Vector2(13, -19.5f + Wy(13)),
                            new Vector2(17, -14 + Wy(17)), new Vector2(8.5f, -14 + Wy(8.5f)), new Vector2(0, -14) };
            Cloth(p, col);
            // светлая полоса у древка
            Quad(new Vector2(1.5f, -21), new Vector2(10.5f, -21 + Wy(10.5f) * 0.6f), new Vector2(10.5f, -18.8f + Wy(10.5f) * 0.6f), new Vector2(1.5f, -18.8f), Shine);
        }
        // знамя лежит: древко поперёк, полотнище свисает набок (как drawDropped полигона)
        void Fallen(Vector2 at, float rot, float s, Color32 col)
        {
            o = at; sc = s; rotC = Mathf.Cos(rot); rotS = Mathf.Sin(rot);
            Line(-14, 0, 14, 0, 2.6f, Ink); Line(-14, 0, 14, 0, 1.4f, Wood);
            var p = new[] { new Vector2(-13, 0), new Vector2(-13, 15), new Vector2(-7.5f, 11), new Vector2(-2, 15), new Vector2(-2, 0) };
            // ласточкин хвост вниз: два треугольника и прямоугольник
            Tri(p[0], p[1], p[2], col); Tri(p[0], p[2], p[4], col); Tri(p[4], p[2], p[3], col);
            Outline(p, 1.2f);
        }
        // полотнище «ласточкин хвост»: P0…P6 по контуру, вырез — P3
        void Cloth(Vector2[] p, Color32 col)
        {
            Tri(p[0], p[1], p[5], col); Tri(p[0], p[5], p[6], col);
            Tri(p[1], p[2], p[3], col); Tri(p[1], p[3], p[5], col); Tri(p[5], p[3], p[4], col);
            Outline(p, 1.2f);
        }
        void Outline(Vector2[] p, float w) { for (int i = 0; i < p.Length; i++) { var a = p[i]; var b = p[(i + 1) % p.Length]; Line(a.x, a.y, b.x, b.y, w, Ink); } }

        void Line(float x0, float y0, float x1, float y1, float w, Color32 col)
        {
            var a = new Vector2(x0, y0); var b = new Vector2(x1, y1); var d = b - a; float len = d.magnitude; if (len < 1e-4f) return;
            var n = new Vector2(-d.y, d.x) / len * (w / 2); var e = d / len * (w / 2);   // концы — с запасом на полтолщины (круглые)
            Quad(a - e - n, b + e - n, b + e + n, a - e + n, col);
        }
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i0 = V.Count;
            V.Add(P(a.x, a.y)); V.Add(P(b.x, b.y)); V.Add(P(c.x, c.y)); V.Add(P(d.x, d.y));
            for (int k = 0; k < 4; k++) C.Add(col);
            I.Add(i0); I.Add(i0 + 1); I.Add(i0 + 2); I.Add(i0); I.Add(i0 + 2); I.Add(i0 + 3);
            I.Add(i0); I.Add(i0 + 2); I.Add(i0 + 1); I.Add(i0); I.Add(i0 + 3); I.Add(i0 + 2);   // обе стороны: поворот не важен
        }
        void Tri(Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i0 = V.Count;
            V.Add(P(a.x, a.y)); V.Add(P(b.x, b.y)); V.Add(P(c.x, c.y));
            for (int k = 0; k < 3; k++) C.Add(col);
            I.Add(i0); I.Add(i0 + 1); I.Add(i0 + 2); I.Add(i0); I.Add(i0 + 2); I.Add(i0 + 1);
        }
    }
}
