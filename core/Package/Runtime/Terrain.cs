// ═══════════ Terrain.cs — местность карты: клетки, высота, правка (копия terrain.js) ═══════════
// Карта — сетка клеток по 5 м (К13). Два слоя одинакового размера:
//   T — вид местности (код из Types, 0 — «не задано»), Z — высота, уровни 0…MaxHeight (К16).
// Сохранение — тот же текст, что у трекера (повторы подряд: «значение.длина» в base36 через запятую),
// поэтому карта из партии трекера открывается в игре как есть.
using System;
using System.Collections.Generic;
using System.Text;

namespace BattleCore
{
    public sealed class TerrainType
    {
        public int Id;
        public string Key, Name, Group;
        public bool System;
        public TerrainType(int id, string key, string name, string group, bool system = false)
        { Id = id; Key = key; Name = name; Group = group; System = system; }
    }

    public sealed class TerrainMap
    {
        public int V = 1;
        public double Cell;
        public int W, H;
        public byte[] T, Z;
        public Dictionary<string, object> Meta = new Dictionary<string, object>();
        public TerrainMap Clone() => new TerrainMap
        {
            V = V, Cell = Cell, W = W, H = H, T = (byte[])T.Clone(), Z = (byte[])Z.Clone(),
            Meta = new Dictionary<string, object>(Meta),
        };
    }

    // Как карта лежит в сохранении трекера (battleMap)
    public sealed class TerrainSave
    {
        public double V = 1, Cell, W, H;
        public string T, Z;
        public Dictionary<string, object> Meta;
    }

    public struct CellInfo { public int X, Y, T, Z; }

    public static class Terrain
    {
        public const double CellM = 5;
        public const int MaxHeight = 3;
        public const int MaxCells = 2500000;

        // Коды не менять: они лежат в сохранениях. Новые виды — только в конец.
        public static readonly TerrainType[] Types =
        {
            new TerrainType(1, "field", "Поле", "Открытое"),
            new TerrainType(2, "road", "Дорога", "Открытое"),
            new TerrainType(3, "sand", "Песок", "Открытое"),
            new TerrainType(4, "snow", "Снег", "Открытое"),
            new TerrainType(5, "shrub", "Кустарник", "Заросли"),
            new TerrainType(6, "forest", "Лес", "Заросли"),
            new TerrainType(7, "water", "Глубокая вода", "Вода"),
            new TerrainType(8, "ford", "Брод", "Вода"),
            new TerrainType(9, "bridge", "Мост", "Вода"),
            new TerrainType(10, "swamp", "Болото", "Вода"),
            new TerrainType(11, "rocks", "Скалы", "Камень"),
            new TerrainType(12, "wall", "Стена", "Постройки"),
            new TerrainType(13, "gate", "Ворота", "Постройки"),
            new TerrainType(14, "tower", "Башня", "Постройки"),
            new TerrainType(15, "palisade", "Частокол", "Постройки"),
            new TerrainType(16, "moat", "Ров", "Постройки"),
            new TerrainType(17, "trench", "Окоп / вал", "Постройки"),
            new TerrainType(18, "building", "Здание", "Постройки"),
            new TerrainType(19, "pavement", "Мостовая", "Постройки"),
            new TerrainType(20, "breach", "Пролом", "Служебное", system: true),   // ставит трекер (6б)
        };
        public static readonly Dictionary<int, TerrainType> ById = new Dictionary<int, TerrainType>();
        public static readonly Dictionary<string, TerrainType> ByKey = new Dictionary<string, TerrainType>();
        static Terrain() { foreach (var t in Types) { ById[t.Id] = t; ByKey[t.Key] = t; } }
        public static byte Id(string key) => (byte)ByKey[key].Id;
        public static string NameOf(int id) => ById.TryGetValue(id, out var t) ? t.Name : "не задано";

        // ── создание ──
        public static TerrainMap Create(double widthM, double heightM, int fill = 0)
        {
            int w = (int)Math.Max(4, Js.Round(widthM / CellM)), h = (int)Math.Max(4, Js.Round(heightM / CellM));
            if ((long)w * h > MaxCells) throw new ArgumentException($"карта слишком велика: {w}×{h} клеток, предел {MaxCells}");
            var t = new byte[w * h];
            if (fill != 0) for (int i = 0; i < t.Length; i++) t[i] = (byte)fill;
            return new TerrainMap { Cell = CellM, W = w, H = h, T = t, Z = new byte[w * h] };
        }
        public static double WidthM(TerrainMap m) => m.W * m.Cell;
        public static double HeightM(TerrainMap m) => m.H * m.Cell;

        // ── сохранение ──
        const string Digits36 = "0123456789abcdefghijklmnopqrstuvwxyz";
        static string B36(long v)
        {
            if (v == 0) return "0";
            var sb = new StringBuilder();
            while (v > 0) { sb.Insert(0, Digits36[(int)(v % 36)]); v /= 36; }
            return sb.ToString();
        }
        // parseInt(s, 36): пробелы и знак в начале, дальше — цифры до первого чужого знака; ни одной цифры — NaN
        static bool ParseB36(string s, out long v)
        {
            v = 0;
            if (s == null) return false;
            s = s.Trim();
            int i = 0; bool neg = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
            int start = i;
            for (; i < s.Length; i++)
            {
                int d = Digits36.IndexOf(char.ToLowerInvariant(s[i]));
                if (d < 0) break;
                v = v * 36 + d;
            }
            if (i == start) return false;
            if (neg) v = -v;
            return true;
        }

        public static string EncodeLayer(byte[] arr)
        {
            var parts = new List<string>();
            if (arr.Length == 0) return "";
            int v = arr[0], n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == v) { n++; continue; }
                parts.Add(B36(v) + "." + B36(n));
                v = arr[i]; n = 1;
            }
            parts.Add(B36(v) + "." + B36(n));
            return string.Join(",", parts);
        }

        public static byte[] DecodeLayer(string str, int len, int max)
        {
            var arr = new byte[len];
            if (string.IsNullOrEmpty(str)) return arr;
            int p = 0;
            foreach (var run in str.Split(','))
            {
                var dot = run.Split('.');
                if (dot.Length < 2 || !ParseB36(dot[0], out long v) || !ParseB36(dot[1], out long n) || n < 0 || p + n > len)
                    throw new FormatException("повреждён слой карты");
                byte b = (byte)Math.Min(Math.Max(v, 0), max);
                for (int i = 0; i < n; i++) arr[p + i] = b;
                p += (int)n;
            }
            if (p != len) throw new FormatException("повреждён слой карты: не хватает клеток");
            return arr;
        }

        public static TerrainSave Serialize(TerrainMap m) => m == null ? null : new TerrainSave
        {
            V = 1, Cell = m.Cell, W = m.W, H = m.H, T = EncodeLayer(m.T), Z = EncodeLayer(m.Z), Meta = new Dictionary<string, object>(m.Meta),
        };

        // Неизвестное или битое — null: партия всё равно откроется, просто без местности
        public static TerrainMap Deserialize(TerrainSave o)
        {
            try
            {
                if (o == null) return null;
                int w = (int)Js.Round(o.W), h = (int)Js.Round(o.H);
                if (!(w >= 4 && h >= 4 && (long)w * h <= MaxCells)) return null;
                int maxId = Types[Types.Length - 1].Id;
                return new TerrainMap
                {
                    Cell = o.Cell > 0 ? o.Cell : CellM, W = w, H = h,
                    T = DecodeLayer(o.T, w * h, maxId), Z = DecodeLayer(o.Z, w * h, MaxHeight),
                    Meta = o.Meta != null ? new Dictionary<string, object>(o.Meta) : new Dictionary<string, object>(),
                };
            }
            catch (FormatException) { return null; }
        }

        // ── чтение: fx, fy — доли карты 0…1 (так фишки и хранят положение: mapX/100) ──
        public static CellInfo CellAt(TerrainMap m, double fx, double fy)
        {
            int x = (int)Math.Min(m.W - 1, Math.Max(0, Math.Floor(fx * m.W)));
            int y = (int)Math.Min(m.H - 1, Math.Max(0, Math.Floor(fy * m.H)));
            int i = y * m.W + x;
            return new CellInfo { X = x, Y = y, T = m.T[i], Z = m.Z[i] };
        }
        public static bool HasTerrain(TerrainMap m)
        {
            if (m == null) return false;
            foreach (var v in m.T) if (v != 0) return true;
            return false;
        }

        // ── правка: координаты и радиус — в клетках (дробные); возвращают число изменённых клеток ──
        static byte[] LayerOf(TerrainMap m, string layer) => layer == "z" ? m.Z : m.T;
        static int ClampValue(string layer, double value) =>
            layer == "z" ? Math.Min(MaxHeight, Math.Max(0, (int)value)) : (int)value;
        static int SetCell(TerrainMap m, byte[] arr, int x, int y, int v)
        {
            if (x < 0 || y < 0 || x >= m.W || y >= m.H) return 0;
            int i = y * m.W + x;
            if (arr[i] == (byte)v) return 0;
            arr[i] = (byte)v;
            return 1;
        }

        public static int PaintDisc(TerrainMap m, string layer, double cx, double cy, double r, double value)
        {
            var arr = LayerOf(m, layer); int v = ClampValue(layer, value);
            double rr = Math.Max(0.5, r), r2 = rr * rr;
            int n = 0;
            for (int y = (int)Math.Floor(cy - rr); y <= Math.Ceiling(cy + rr); y++)
                for (int x = (int)Math.Floor(cx - rr); x <= Math.Ceiling(cx + rr); x++)
                {
                    double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                    if (dx * dx + dy * dy <= r2) n += SetCell(m, arr, x, y, v);
                }
            return n;
        }

        // Толстая линия — «капсула»: все клетки не дальше r от отрезка (стены, дороги, реки)
        public static int PaintSegment(TerrainMap m, string layer, double x0, double y0, double x1, double y1, double r, double value)
        {
            var arr = LayerOf(m, layer); int v = ClampValue(layer, value);
            double rr = Math.Max(0.5, r), r2 = rr * rr;
            double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            int n = 0;
            for (int y = (int)Math.Floor(Math.Min(y0, y1) - rr); y <= Math.Ceiling(Math.Max(y0, y1) + rr); y++)
                for (int x = (int)Math.Floor(Math.Min(x0, x1) - rr); x <= Math.Ceiling(Math.Max(x0, x1) + rr); x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double k = len2 != 0 ? Math.Max(0, Math.Min(1, ((px - x0) * dx + (py - y0) * dy) / len2)) : 0;
                    double ex = px - (x0 + k * dx), ey = py - (y0 + k * dy);
                    if (ex * ex + ey * ey <= r2) n += SetCell(m, arr, x, y, v);
                }
            return n;
        }

        // Прямоугольник по двум углам; outline > 0 — только контур такой толщины в клетках (стены замка)
        public static int PaintRect(TerrainMap m, string layer, double x0, double y0, double x1, double y1, double value, double outline = 0)
        {
            var arr = LayerOf(m, layer); int v = ClampValue(layer, value);
            int l = (int)Math.Floor(Math.Min(x0, x1)), r = (int)Math.Floor(Math.Max(x0, x1));
            int t = (int)Math.Floor(Math.Min(y0, y1)), b = (int)Math.Floor(Math.Max(y0, y1));
            int th = (int)Math.Max(0, Js.Round(outline));
            int n = 0;
            for (int y = t; y <= b; y++)
                for (int x = l; x <= r; x++)
                {
                    if (th != 0 && x >= l + th && x <= r - th && y >= t + th && y <= b - th) continue;
                    n += SetCell(m, arr, x, y, v);
                }
            return n;
        }

        // Заливка связной области одного значения (соседи по сторонам)
        public static int FloodFill(TerrainMap m, string layer, double fx, double fy, double value)
        {
            var arr = LayerOf(m, layer); int v = ClampValue(layer, value);
            int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
            if (x < 0 || y < 0 || x >= m.W || y >= m.H) return 0;
            int from = arr[y * m.W + x];
            if (from == (byte)v) return 0;
            var stack = new Stack<int>();
            stack.Push(y * m.W + x);
            int n = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (arr[i] != from) continue;
                arr[i] = (byte)v; n++;
                int cx = i % m.W, cy = (i - cx) / m.W;
                if (cx > 0) stack.Push(i - 1);
                if (cx < m.W - 1) stack.Push(i + 1);
                if (cy > 0) stack.Push(i - m.W);
                if (cy < m.H - 1) stack.Push(i + m.W);
            }
            return n;
        }
    }
}
