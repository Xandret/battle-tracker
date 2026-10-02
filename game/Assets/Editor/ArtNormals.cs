// ═══════════ ArtNormals.cs — карты объёма атласов (В15): men.png → men_n.png, рядом, в Resources/Art ═══════════
// Рисунок полигона плоский; объём ему даёт свет в шейдере Men по карте нормалей. Карта строится из самого рисунка:
// - каждая область между тёмными контурами — своя «подушечка»: от края к середине поднимается по четверти круга
//   радиусом ~5 см и дальше ровная — как отдельная выпуклая деталь раскрашенной фигурки (щит, наплечник, шлем);
// - поверх — пологий купол по всему силуэту части (радиус ~20 см): тело и круп коня круглятся целиком;
// - контур (цвет туши полигона) — бороздка: части отделяются друг от друга;
// - A — металл: серые прохладные цвета (сталь, кольчуга, блик) — шейдер даёт им блеск.
// Нормаль — в осях текстуры: x — вдоль u (вправо), y — вдоль v (вверх, к переду бойца), z — к зрителю.
// Строится сама после выгрузки атласов из полигона (импорт png в Resources/Art) и по меню «Журнал → Карты объёма атласов».
// Бойцы, кони и павшие с В18 плоские, как у образца (Iron Kings): им карты объёма не строятся — только природе.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public sealed class ArtNormals : AssetPostprocessor
{
    const string Dir = "Assets/Resources/Art/";
    static readonly string[] Lit = { "nature" };   // атласы со светом по карте объёма

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var todo = new List<string>();
        foreach (var p in imported)
        {
            var q = p.Replace('\\', '/');
            if (q.StartsWith(Dir) && q.EndsWith(".png") && !q.EndsWith("_n.png") && File.Exists(Path.ChangeExtension(q, ".json"))
                && System.Array.IndexOf(Lit, Path.GetFileNameWithoutExtension(q)) >= 0) todo.Add(q);
        }
        if (todo.Count == 0) return;
        // не внутри импорта: новые файлы — следующим кадром редактора
        EditorApplication.delayCall += () => { foreach (var q in todo) Build(q); AssetDatabase.Refresh(); };
    }

    [MenuItem("Журнал/Карты объёма атласов")]
    public static void BuildAll()
    {
        foreach (var name in Lit)
        {
            var p = Dir + name + ".png";
            if (File.Exists(p) && File.Exists(Dir + name + ".json")) Build(p);
        }
        AssetDatabase.Refresh();
    }

    public static void Build(string pngPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        src.LoadImage(File.ReadAllBytes(pngPath));
        int W = src.width, H = src.height;
        var px = src.GetPixels32();
        Object.DestroyImmediate(src);
        var json = File.ReadAllText(Path.ChangeExtension(pngPath, ".json"));
        var inv = CultureInfo.InvariantCulture;
        var mp = Regex.Match(json, "\"ppm\"\\s*:\\s*([0-9.]+)");
        float ppm = mp.Success ? float.Parse(mp.Groups[1].Value, inv) : 64;
        var outPx = new Color32[W * H];
        for (int i = 0; i < outPx.Length; i++) outPx[i] = new Color32(128, 128, 255, 0);
        int n = 0;
        foreach (Match s in Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\\[([^\\]]+)\\]"))
        {
            var v = s.Groups[2].Value.Split(',');
            if (v.Length != 8) continue;
            int x0 = (int)float.Parse(v[0], inv), y0 = (int)float.Parse(v[1], inv), w = (int)float.Parse(v[2], inv), h = (int)float.Parse(v[3], inv);
            // служебные (кровь, тени, белый квадрат, вспышка) — без объёма
            if (s.Groups[1].Value.StartsWith("util/")) continue;
            // у древков и стрел половина разрешения — свой масштаб; берём из рамки в метрах
            float mw = float.Parse(v[6], inv) - float.Parse(v[4], inv), sppm = mw > 0 ? w / mw : ppm;
            string name = s.Groups[1].Value;
            // стоящий боец (В17): цилиндр — объём только поперёк (по длине его растягивают), шар — точная сфера
            if (name.StartsWith("cyl/") || name.StartsWith("ball/"))
                Analytic(px, outPx, W, H, x0, y0, w, h, float.Parse(v[4], inv), float.Parse(v[5], inv), float.Parse(v[6], inv), float.Parse(v[7], inv), name.StartsWith("cyl/") ? 0.06f : 0.1f, name.StartsWith("ball/"));
            else Part(px, outPx, W, H, x0, y0, w, h, sppm);
            n++;
        }
        var dst = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
        dst.SetPixels32(outPx); dst.Apply(false);
        var outPath = pngPath.Substring(0, pngPath.Length - 4) + "_n.png";
        File.WriteAllBytes(outPath, dst.EncodeToPNG());
        Object.DestroyImmediate(dst);
        Debug.Log($"Карта объёма {Path.GetFileName(outPath)}: частей {n}, {sw.ElapsedMilliseconds} мс");
    }

    // цвет туши полигона (#2b2621) — контур, бороздка; тёмные волосы того же цвета — тоже (голова мелкая, не страшно)
    static bool Ink(Color32 c) { int dr = c.r - 43, dg = c.g - 38, db = c.b - 33; return dr * dr + dg * dg + db * db < 14 * 14 * 3; }
    // металл: серый (почти без цвета), не тёмный, прохладный — синий не меньше красного
    static bool Metal(Color32 c)
    {
        int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        return mx - mn < 20 && mx > 110 && c.b >= c.r - 3;
    }

    // одна часть: высота → нормали; строки прямоугольника — сверху вниз (как в json), в текстуре Unity — снизу вверх
    static void Part(Color32[] px, Color32[] outPx, int W, int H, int x0, int y0, int w, int h, float ppm)
    {
        if (w < 3 || h < 3) return;
        int N = w * h;
        var alpha = new bool[N]; var region = new bool[N];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[(H - 1 - (y0 + y)) * W + x0 + x];
                bool a = c.a > 110;
                alpha[y * w + x] = a; region[y * w + x] = a && !Ink(c);
            }
        var dReg = Chamfer(region, w, h); var dSil = Chamfer(alpha, w, h);
        float R = Mathf.Max(1.5f, 0.05f * ppm), R2 = Mathf.Max(3, 0.2f * ppm);
        var hgt = new float[N];
        for (int i = 0; i < N; i++)
        {
            if (!alpha[i]) continue;
            float d = Mathf.Min(dReg[i], R), bevel = Mathf.Sqrt(Mathf.Max(0, R * R - (R - d) * (R - d)));   // четверть круга
            float d2 = Mathf.Min(dSil[i], R2), dome = Mathf.Sqrt(Mathf.Max(0, R2 * R2 - (R2 - d2) * (R2 - d2)));
            hgt[i] = bevel + 0.35f * dome;
        }
        float H0(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : hgt[y * w + x];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!alpha[i]) continue;
                // Собель; y строк растёт вниз, v текстуры — вверх
                float gx = (H0(x + 1, y - 1) + 2 * H0(x + 1, y) + H0(x + 1, y + 1) - H0(x - 1, y - 1) - 2 * H0(x - 1, y) - H0(x - 1, y + 1)) / 8;
                float gy = (H0(x - 1, y + 1) + 2 * H0(x, y + 1) + H0(x + 1, y + 1) - H0(x - 1, y - 1) - 2 * H0(x, y - 1) - H0(x + 1, y - 1)) / 8;
                var nrm = new Vector3(-gx, gy, 1).normalized;   // dh/dv = −dh/dy(строк)
                var c = px[(H - 1 - (y0 + y)) * W + x0 + x];
                outPx[(H - 1 - (y0 + y)) * W + x0 + x] = new Color32((byte)(127.5f + 127.5f * nrm.x), (byte)(127.5f + 127.5f * nrm.y), (byte)(127.5f + 127.5f * nrm.z), (byte)(Metal(c) ? 255 : 0));
            }
    }

    // нормали по форме: цилиндр радиуса R вдоль v (вид сверху на лежащий цилиндр) или шар радиуса R с центром в (0, 0) метров
    static void Analytic(Color32[] px, Color32[] outPx, int W, int H, int x0, int y0, int w, int h, float mx0, float my0, float mx1, float my1, float R, bool ball)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int at = (H - 1 - (y0 + y)) * W + x0 + x; var c = px[at];
                if (c.a <= 110) continue;
                float xm = mx0 + (x + 0.5f) / w * (mx1 - mx0), ym = my0 + (y + 0.5f) / h * (my1 - my0);
                float nx = Mathf.Clamp(xm / R, -0.98f, 0.98f), ny = ball ? Mathf.Clamp(-ym / R, -0.98f, 0.98f) : 0;
                float q = nx * nx + ny * ny; if (q > 0.96f) { float k = Mathf.Sqrt(0.96f / q); nx *= k; ny *= k; q = 0.96f; }
                var n = new Vector3(nx, ny, Mathf.Sqrt(1 - q));
                outPx[at] = new Color32((byte)(127.5f + 127.5f * n.x), (byte)(127.5f + 127.5f * n.y), (byte)(127.5f + 127.5f * n.z), (byte)(Metal(c) ? 255 : 0));
            }
    }

    // расстояние до края маски (в пикселях), два прохода с весами 1 и √2; за прямоугольником — снаружи
    static float[] Chamfer(bool[] m, int w, int h)
    {
        const float A = 1, B = 1.4142f, Inf = 1e6f;
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = m[i] ? Inf : 0;
        float D(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : d[y * w + x];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x; if (d[i] == 0) continue;
                d[i] = Mathf.Min(d[i], Mathf.Min(Mathf.Min(D(x - 1, y) + A, D(x, y - 1) + A), Mathf.Min(D(x - 1, y - 1) + B, D(x + 1, y - 1) + B)));
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x; if (d[i] == 0) continue;
                d[i] = Mathf.Min(d[i], Mathf.Min(Mathf.Min(D(x + 1, y) + A, D(x, y + 1) + A), Mathf.Min(D(x + 1, y + 1) + B, D(x - 1, y + 1) + B)));
            }
        return d;
    }
}
