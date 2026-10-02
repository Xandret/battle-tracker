// ═══════════ ArtAtlas.cs — атласы рисунка из полигона: Resources/Art/<имя>.png + .json (core/Tests/atlas-export.js) ═══════════
// Часть рисунка — прямоугольник в атласе и рамка в метрах в осях бойца полигона (вперёд — вверх, −y; начало — середина плеч).
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Journal.Art
{
    public struct Part
    {
        public float U0, V0, U1, V1;     // в атласе: левый край, низ, правый край, верх
        public float X0, Y0, X1, Y1;     // рамка в метрах: Y0 — передний (верхний) край
        public bool Ok;
    }

    public sealed class ArtAtlas
    {
        public Texture2D Tex;
        public float Ppm;
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        public int Count => parts.Count;

        public static ArtAtlas Load(string name)
        {
            var tex = Resources.Load<Texture2D>("Art/" + name);
            var json = Resources.Load<TextAsset>("Art/" + name);
            if (tex == null || json == null) { Debug.LogWarning($"нет атласа Art/{name} — выгрузи рисунок из полигона (game/Tools/art-server.mjs)"); return null; }
            var a = new ArtAtlas { Tex = tex };
            var inv = CultureInfo.InvariantCulture;
            var m = Regex.Match(json.text, "\"ppm\"\\s*:\\s*([0-9.]+)");
            a.Ppm = m.Success ? float.Parse(m.Groups[1].Value, inv) : 64;
            float W = tex.width, H = tex.height;
            foreach (Match s in Regex.Matches(json.text, "\"([^\"]+)\"\\s*:\\s*\\[([^\\]]+)\\]"))
            {
                var v = s.Groups[2].Value.Split(',');
                if (v.Length != 8) continue;
                float px = float.Parse(v[0], inv), py = float.Parse(v[1], inv), pw = float.Parse(v[2], inv), ph = float.Parse(v[3], inv);
                a.parts[s.Groups[1].Value] = new Part
                {
                    U0 = px / W, U1 = (px + pw) / W, V1 = 1 - py / H, V0 = 1 - (py + ph) / H,
                    X0 = float.Parse(v[4], inv), Y0 = float.Parse(v[5], inv), X1 = float.Parse(v[6], inv), Y1 = float.Parse(v[7], inv), Ok = true,
                };
            }
            return a;
        }
        public Part Get(string name) => parts.TryGetValue(name, out var p) ? p : default;
        public bool Has(string name) => parts.ContainsKey(name);
    }
}
