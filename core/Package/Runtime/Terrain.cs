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
        // участки укреплений (6б, Г46): номер участка в клетке (0 — нет) и их список; null — ещё не построены
        public ushort[] S;
        public List<FortSection> Forts;
        public TerrainMap Clone()
        {
            var c = new TerrainMap
            {
                V = V, Cell = Cell, W = W, H = H, T = (byte[])T.Clone(), Z = (byte[])Z.Clone(),
                Meta = new Dictionary<string, object>(Meta),
            };
            if (S != null && Forts != null)
            {
                c.S = (ushort[])S.Clone();
                c.Forts = Forts.ConvertAll(f => f.Copy());
            }
            return c;
        }
    }

    // Участок укреплений: в сохранении — номер, вид, урон и проломы; остальное размечается по слою S
    public sealed class FortSection
    {
        public int Id;
        public string Kind;
        public double Dmg;
        public int Breaches;
        public double? Holder;          // фракция, занявшая участок приступом (6б, Ш13); null — не занят
        public int N, Up, Len;          // клеток, из них целых, длина вдоль стены (клеток)
        public double Cx, Cy;           // центр, в клетках
        public FortSection Copy() => (FortSection)MemberwiseClone();
    }

    // Как карта лежит в сохранении трекера (battleMap). Forts — как пришло из JSON: список словарей
    // (числа — double) или что угодно другое у битого сохранения.
    public sealed class TerrainSave
    {
        public double V = 1, Cell, W, H;
        public string T, Z;
        public Dictionary<string, object> Meta;
        public string S;
        public object Forts;
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
        // Б5: деревья клетки леса — xs, ys (не меньше TreesPerCell); возвращает сколько. Положения — только от номера клетки
        public static int Trees(TerrainMap map, int cell, Rules r, double[] xs, double[] ys)
        {
            int n = Math.Min(r.Men.TreesPerCell, xs.Length); if (n <= 0) return 0;
            int cx = cell % map.W, cy = cell / map.W, side = (int)Math.Ceiling(Math.Sqrt(n));
            double q = CellM / side, j = r.Men.TreeJitterM;
            for (int t = 0; t < n; t++)
            {
                xs[t] = cx * CellM + (t % side + 0.5) * q + (MoveSim.Hash01(cell, t, 41) - 0.5) * 2 * j;
                ys[t] = cy * CellM + (t / side + 0.5) * q + (MoveSim.Hash01(cell, t, 42) - 0.5) * 2 * j;
            }
            return n;
        }
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

        public static string EncodeLayer(ushort[] arr)
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

        // Слой номеров участков: те же повторы, значения до 65535
        public static ushort[] DecodeLayer16(string str, int len, int max)
        {
            var arr = new ushort[len];
            if (string.IsNullOrEmpty(str)) return arr;
            int p = 0;
            foreach (var run in str.Split(','))
            {
                var dot = run.Split('.');
                if (dot.Length < 2 || !ParseB36(dot[0], out long v) || !ParseB36(dot[1], out long n) || n < 0 || p + n > len)
                    throw new FormatException("повреждён слой карты");
                ushort b = (ushort)Math.Min(Math.Max(v, 0), max);
                for (int i = 0; i < n; i++) arr[p + i] = b;
                p += (int)n;
            }
            if (p != len) throw new FormatException("повреждён слой карты: не хватает клеток");
            return arr;
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

        public static TerrainSave Serialize(TerrainMap m)
        {
            if (m == null) return null;
            var o = new TerrainSave
            {
                V = 1, Cell = m.Cell, W = m.W, H = m.H, T = EncodeLayer(m.T), Z = EncodeLayer(m.Z), Meta = new Dictionary<string, object>(m.Meta),
            };
            // участки укреплений (6б): слой номеров и состояние; до v30.8 их нет — построятся при надобности
            if (m.S != null && m.Forts != null)
            {
                o.S = EncodeLayer(m.S);
                o.Forts = m.Forts.ConvertAll(f =>
                {
                    var d = new Dictionary<string, object> { ["id"] = (double)f.Id, ["kind"] = f.Kind, ["dmg"] = f.Dmg, ["breaches"] = (double)f.Breaches };
                    if (f.Holder.HasValue) d["holder"] = f.Holder.Value;   // занят приступом (Ш13)
                    return (object)d;
                });
            }
            return o;
        }

        // Неизвестное или битое — null: партия всё равно откроется, просто без местности
        public static TerrainMap Deserialize(TerrainSave o)
        {
            try
            {
                if (o == null) return null;
                int w = (int)Js.Round(o.W), h = (int)Js.Round(o.H);
                if (!(w >= 4 && h >= 4 && (long)w * h <= MaxCells)) return null;
                int maxId = Types[Types.Length - 1].Id;
                var m = new TerrainMap
                {
                    Cell = o.Cell > 0 ? o.Cell : CellM, W = w, H = h,
                    T = DecodeLayer(o.T, w * h, maxId), Z = DecodeLayer(o.Z, w * h, MaxHeight),
                    Meta = o.Meta != null ? new Dictionary<string, object>(o.Meta) : new Dictionary<string, object>(),
                };
                ReadSections(m, o);
                return m;
            }
            catch (FormatException) { return null; }
        }

        // Битые участки не роняют карту: местность откроется, участки построятся заново
        static void ReadSections(TerrainMap m, TerrainSave o)
        {
            if (o.S == null || !(o.Forts is List<object> list)) return;
            try
            {
                var s = DecodeLayer16(o.S, m.W * m.H, MaxSections);
                var forts = new List<FortSection>();
                var seen = new HashSet<int>();
                foreach (var item in list)
                {
                    var f = item as Dictionary<string, object>;
                    double idv = Js.Round(JsNumber(f, "id"));
                    if (!(idv >= 1 && idv <= MaxSections)) continue;
                    int id = (int)idv;
                    if (seen.Contains(id) || !(f.TryGetValue("kind", out var k) && k is string kind && FortKinds.ContainsKey(kind))) continue;
                    seen.Add(id);
                    double dmg = JsNumber(f, "dmg"), br = Js.Round(JsNumber(f, "breaches"));
                    var fo = new FortSection { Id = id, Kind = kind, Dmg = double.IsFinite(dmg) && dmg > 0 ? dmg : 0, Breaches = br > 0 ? (int)br : 0 };
                    if (f.TryGetValue("holder", out var hv) && hv != null) { double hd = Js.Round(JsNumber(f, "holder")); if (hd >= 0) fo.Holder = hd; }
                    forts.Add(fo);
                }
                forts.Sort((a, b) => a.Id.CompareTo(b.Id));
                m.S = s; m.Forts = forts;
                IndexSections(m);
            }
            catch (FormatException) { m.S = null; m.Forts = null; }
        }
        // +x из JS для поля сохранения: нет поля — NaN, null — 0, строка — как Number("…")
        static double JsNumber(Dictionary<string, object> f, string key)
        {
            if (f == null || !f.TryGetValue(key, out var v)) return double.NaN;
            switch (v)
            {
                case null: return 0;
                case double d: return d;
                case bool b: return b ? 1 : 0;
                case string str:
                    str = str.Trim();
                    if (str.Length == 0) return 0;
                    return double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : double.NaN;
                default: return double.NaN;
            }
        }

        // ── участки укреплений (6б, Г46): слой S — номер участка в клетке (0 — нет), Forts — список ──
        // Здесь только данные: что лежит в карте и как это читать. Нарезка и урон — Fortify.cs (по правилам).
        public const int MaxSections = 65535;
        public static readonly Dictionary<string, string> FortKinds = new Dictionary<string, string>
        {
            ["palisade"] = "частокол", ["wall"] = "каменная стена", ["gateWood"] = "деревянные ворота",
            ["gateIron"] = "окованные ворота", ["tower"] = "башня",
        };
        // Код местности, которым стоит целая клетка участка (пролом — код breach)
        public static byte FortCode(string kind) => Id(kind == "gateWood" || kind == "gateIron" ? "gate" : kind);
        // восемь соседей клетки: сначала верхний ряд, потом свой, потом нижний
        public static readonly int[] N8X = { -1, 0, 1, -1, 1, -1, 0, 1 };
        public static readonly int[] N8Y = { -1, -1, -1, 0, 0, 1, 1, 1 };

        // Обход в ширину по восьми соседям внутри множества inSet(j); dist — расстояния в шагах (−1 — не дошли).
        // Возвращает самую дальнюю клетку (при равенстве — с меньшим номером) и её расстояние.
        public static (int far, int farD, int count) Bfs8(TerrainMap m, int from, Func<int, bool> inSet, int[] dist, int[] queue)
        {
            int head = 0, tail = 0, far = from, farD = 0;
            dist[from] = 0; queue[tail++] = from;
            while (head < tail)
            {
                int i = queue[head++], d = dist[i], x = i % m.W, y = i / m.W;
                if (d > farD || (d == farD && i < far)) { far = i; farD = d; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + N8X[k], ny = y + N8Y[k];
                    if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                    int j = ny * m.W + nx;
                    if (dist[j] != -1 || !inSet(j)) continue;
                    dist[j] = d + 1; queue[tail++] = j;
                }
            }
            return (far, farD, tail);
        }

        // Разметка участков: клеток N, целых Up, длина вдоль стены Len (клеток), центр Cx, Cy (в клетках)
        public static void IndexSections(TerrainMap m)
        {
            var byId = new Dictionary<int, FortSection>();
            var cells = new Dictionary<int, List<int>>();
            var sumX = new Dictionary<int, long>();
            var sumY = new Dictionary<int, long>();
            foreach (var f in m.Forts)
            {
                byId[f.Id] = f;
                f.N = 0; f.Up = 0; f.Len = 1; f.Cx = 0; f.Cy = 0;
                cells[f.Id] = new List<int>(); sumX[f.Id] = 0; sumY[f.Id] = 0;
            }
            for (int i = 0; i < m.S.Length; i++)
            {
                int id = m.S[i];
                if (id == 0) continue;
                if (!byId.TryGetValue(id, out var f)) { m.S[i] = 0; continue; }
                f.N++; sumX[id] += i % m.W; sumY[id] += i / m.W;
                if (m.T[i] == FortCode(f.Kind)) f.Up++;
                cells[id].Add(i);
            }
            var dist = new int[m.S.Length];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var queue = new int[m.S.Length];
            foreach (var f in m.Forts)
            {
                var list = cells[f.Id];
                if (f.N == 0) continue;
                f.Cx = (double)sumX[f.Id] / f.N + 0.5; f.Cy = (double)sumY[f.Id] / f.N + 0.5;
                // длина — поперечник участка: от самой дальней клетки до самой дальней от неё, +1
                int fid = f.Id;
                Func<int, bool> inSec = j => m.S[j] == fid;
                var a = Bfs8(m, list[0], inSec, dist, queue);
                foreach (int i in list) dist[i] = -1;
                var b = Bfs8(m, a.far, inSec, dist, queue);
                foreach (int i in list) dist[i] = -1;
                f.Len = b.farD + 1;
            }
            m.Forts = m.Forts.FindAll(f => f.N > 0);
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
            Touched(m);
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
            Touched(m);
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
        // Г104: есть ли на карте хоть одна клетка кода a или b (стены и башни — чтобы не искать их у каждого бойца на каждом шаге);
        // кэш по карте, кисти его сбрасывают
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainMap, Dictionary<int, bool>> hasCodes = new System.Runtime.CompilerServices.ConditionalWeakTable<TerrainMap, Dictionary<int, bool>>();
        public static bool HasAny(TerrainMap m, byte a, byte b)
        {
            var d = hasCodes.GetValue(m, _ => new Dictionary<int, bool>());
            int key = a << 8 | b;
            lock (d)   // два боя на одной карте в разных потоках (Г111 п.7)
            {
                if (!d.TryGetValue(key, out var any))
                {
                    any = false; foreach (var t in m.T) if (t == a || t == b) { any = true; break; }
                    d[key] = any;
                }
                return any;
            }
        }
        public static void Touched(TerrainMap m) { hasCodes.Remove(m); towerCircles.Remove(m); }
        // Г105 (Алекс 10.10.2026: стрелы втыкались в башню «по квадрату»): связная группа клеток башни — круг: центр — середина клеток,
        // радиус — по площади (r = √(N·25/π): 1 клетка 2,8 м, 3 × 3 — 8,5 м, 5 × 5 — 14,1 м; углы квадрата — снаружи). Так башню
        // рисует чат облика; стрелы, высота бойца и парапет считаются по кругу. false — клетка не башня
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainMap, Dictionary<int, double[]>> towerCircles = new System.Runtime.CompilerServices.ConditionalWeakTable<TerrainMap, Dictionary<int, double[]>>();
        public static bool TowerCircle(TerrainMap m, int cell, out double cx, out double cy, out double r)
        {
            cx = cy = r = 0;
            byte tower = Id("tower");
            if (cell < 0 || cell >= m.T.Length || m.T[cell] != tower) return false;
            var d = towerCircles.GetValue(m, _ => new Dictionary<int, double[]>());
            double[] c;
            lock (d) d.TryGetValue(cell, out c);
            if (c == null)
            {
                var cells = new List<int> { cell }; var q = new Queue<int>(); q.Enqueue(cell); var seen = new HashSet<int> { cell };
                while (q.Count > 0)
                {
                    int i = q.Dequeue(); int x = i % m.W, y = i / m.W;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                        int j = ny * m.W + nx;
                        if (m.T[j] == tower && seen.Add(j)) { cells.Add(j); q.Enqueue(j); }
                    }
                }
                double sx = 0, sy = 0;
                foreach (int i in cells) { sx += (i % m.W + 0.5) * CellM; sy += (i / m.W + 0.5) * CellM; }
                c = new[] { sx / cells.Count, sy / cells.Count, Math.Sqrt(cells.Count * CellM * CellM / Math.PI) };
                lock (d) foreach (int i in cells) d[i] = c;
            }
            cx = c[0]; cy = c[1]; r = c[2]; return true;
        }
        // на чём стоит точка (x, y): верх стены или башни (внутри её круга) над землёй, 0 — земля; tops — высоты по коду (Ranged.BuildingHeightM)
        public static double StandTop(TerrainMap m, double x, double y, double wallTop, double towerTop)
        {
            int cx = (int)(x / CellM), cy = (int)(y / CellM);
            if (cx < 0 || cy < 0 || cx >= m.W || cy >= m.H) return 0;
            int cell = cy * m.W + cx; byte t = m.T[cell];
            if (t == Id("wall")) return wallTop;
            if (t == Id("tower")) return TowerCircle(m, cell, out var tcx, out var tcy, out var r) && (x - tcx) * (x - tcx) + (y - tcy) * (y - tcy) > r * r ? 0 : towerTop;
            return 0;
        }
        public static int PaintRect(TerrainMap m, string layer, double x0, double y0, double x1, double y1, double value, double outline = 0)
        {
            var arr = LayerOf(m, layer); int v = ClampValue(layer, value); Touched(m);
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
