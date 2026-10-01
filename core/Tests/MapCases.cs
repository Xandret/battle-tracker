// ═══════════ MapCases.cs — карта 6а: общие сценарии с трекером (правило 7) ═══════════
// Отыгрывает shared/golden/map.json — его пишет tracker/tools/export-map-golden.mjs из tracker/tests/mapcases.mjs:
// генераторы карт, сохранение слоёв, кисти, геометрию строя, местность под отрядом, досягаемость и панику.
// Движок на C# обязан совпасть с трекером бит в бит. Результат собирается в JSON той же формы, что у трекера,
// и сравнивается поэлементно; при расхождении в сообщении — путь до него (например geo[3].pairs[7].gap).
using System.Text.Json;
using System.Text.Json.Nodes;
using BattleCore;

static class MapCases
{
    public static IEnumerable<(string Name, Action Run)> All(JsonElement g)
    {
        var R = Rules.Base;
        foreach (var c in g.GetProperty("maps").EnumerateArray())
            yield return ($"карта 6а: {Title(c)} — генератор", () => Maps(c));
        var codec = g.GetProperty("codec");
        foreach (var c in codec.GetProperty("full").EnumerateArray())
            yield return ($"карта 6а: {Title(c)} — сохранение слоёв", () => CodecFull(c));
        int k = 0;
        foreach (var c in codec.GetProperty("corrupt").EnumerateArray())
        {
            int n = ++k;
            yield return ($"карта 6а: битое сохранение №{n}", () => CodecCorrupt(c, $"codec.corrupt[{n - 1}]"));
        }
        var paint = g.GetProperty("paint");
        yield return ("карта 6а: кисти, линия, прямоугольник, заливка", () => Paint(paint));
        k = 0;
        foreach (var c in g.GetProperty("geo").EnumerateArray())
        {
            int n = k++;
            yield return ($"карта 6а: {Title(c)} — строй, местность под отрядом, досягаемость", () => Geo(c, R, $"geo[{n}]"));
        }
        k = 0;
        foreach (var c in g.GetProperty("panic").EnumerateArray())
        {
            int n = k++;
            string loss = c.GetProperty("moraleLoss").GetBoolean() ? ", −100 БД" : "";
            yield return ($"карта 6а: {Title(c)} — паника, генератор {c.GetProperty("rngSeed").GetDouble()}{loss}", () => PanicCase(c, R, $"panic[{n}]"));
        }
    }

    static string Title(JsonElement c)
    {
        string input = c.GetProperty("input").GetRawText();
        return $"{c.GetProperty("template").GetString()}{(input == "{}" ? "" : " " + input)}, зерно {c.GetProperty("seed").GetDouble()}";
    }

    // ── сценарии ──
    static TerrainMap Generate(JsonElement c) =>
        MapGen.Generate(c.GetProperty("template").GetString(), Input(c.GetProperty("input")), (uint)c.GetProperty("seed").GetDouble());

    static void Maps(JsonElement c)
    {
        var m = Generate(c);
        var counts = new JsonObject();
        foreach (var grp in m.T.GroupBy(v => v).OrderBy(x => x.Key)) counts[grp.Key.ToString()] = grp.Count();
        var z = new int[4];
        foreach (var v in m.Z) z[v]++;
        Check(c, new JsonObject
        {
            ["w"] = m.W, ["h"] = m.H, ["t"] = Fnv(m.T), ["z"] = Fnv(m.Z), ["counts"] = counts,
            ["zCounts"] = new JsonArray(z.Select(v => (JsonNode)v).ToArray()),
        }, "maps");
    }

    static void CodecFull(JsonElement c)
    {
        var s = Terrain.Serialize(Generate(c));
        Check(c, new JsonObject { ["w"] = s.W, ["h"] = s.H, ["t"] = s.T, ["z"] = s.Z }, "codec.full");
    }

    static void CodecCorrupt(JsonElement c, string where)
    {
        var i = c.GetProperty("input");
        var m = Terrain.Deserialize(new TerrainSave
        {
            V = i.GetProperty("v").GetDouble(), Cell = i.GetProperty("cell").GetDouble(),
            W = i.GetProperty("w").GetDouble(), H = i.GetProperty("h").GetDouble(),
            T = i.GetProperty("t").GetString(), Z = i.GetProperty("z").GetString(),
        });
        Check(c, new JsonObject
        {
            ["result"] = m == null ? null : new JsonObject
            {
                ["cell"] = m.Cell, ["w"] = m.W, ["h"] = m.H, ["t"] = Terrain.EncodeLayer(m.T), ["z"] = Terrain.EncodeLayer(m.Z),
            },
        }, where);
    }

    static void Paint(JsonElement c)
    {
        var m = Terrain.Create(c.GetProperty("widthM").GetDouble(), c.GetProperty("depthM").GetDouble());
        int k = 0;
        foreach (var s in c.GetProperty("steps").EnumerateArray())
        {
            string op = s.GetProperty("op").GetString(), layer = s.GetProperty("layer").GetString();
            var a = s.GetProperty("args").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            double v = s.GetProperty("value").GetDouble(), outline = s.GetProperty("outline").GetDouble();
            int n = op == "disc" ? Terrain.PaintDisc(m, layer, a[0], a[1], a[2], v)
                  : op == "segment" ? Terrain.PaintSegment(m, layer, a[0], a[1], a[2], a[3], a[4], v)
                  : op == "rect" ? Terrain.PaintRect(m, layer, a[0], a[1], a[2], a[3], v, outline)
                  : Terrain.FloodFill(m, layer, a[0], a[1], v);
            Check(s, new JsonObject { ["n"] = n, ["t"] = Terrain.EncodeLayer(m.T), ["z"] = Terrain.EncodeLayer(m.Z) }, $"paint.steps[{k++}] ({op})");
        }
    }

    static void Geo(JsonElement c, Rules R, string where)
    {
        var m = Generate(c);
        var geo = new Geo { Map = m, W = Terrain.WidthM(m) * c.GetProperty("stretch").GetDouble(), H = Terrain.HeightM(m) };
        var units = c.GetProperty("units").EnumerateArray().Select(UnitOf).ToList();
        var per = new JsonArray();
        foreach (var u in units)
        {
            var fp = Formation.Of(u, R);
            per.Add(new JsonObject
            {
                ["footprint"] = new JsonObject { ["front"] = fp.Front, ["depth"] = fp.Depth },
                ["corners"] = new JsonArray(BattleMap.UnitCorners(u, geo, R).Select(p => (JsonNode)new JsonArray(p[0], p[1])).ToArray()),
                ["ground"] = Ground(BattleMap.GroundUnder(u, geo, R)),
                ["ground53"] = Ground(BattleMap.GroundUnder(u, geo, R, 3, 2)),
                ["fatigue"] = BattleMap.FatigueMultFor(u, geo, R) is FatigueMult f ? new JsonObject { ["mult"] = f.Mult, ["name"] = f.Name } : null,
                ["speed"] = BattleMap.UnitSpeed(u, R), ["range"] = BattleMap.RangeOf(u, R), ["runUp"] = BattleMap.RunUpBlock(u, R),
            });
        }
        var pairs = new JsonArray();
        for (int i = 0; i < units.Count; i++)
            foreach (int j in new[] { (i + 1) % units.Count, (i + 3) % units.Count })
            {
                Unit a = units[i], b = units[j];
                var los = Panic.LineOfSight(a, b, geo, R);
                var run = BattleMap.RunOver(a, new[] { a.MapX / 100, a.MapY / 100 }, new[] { b.MapX / 100, b.MapY / 100 }, geo, R);
                pairs.Add(new JsonObject
                {
                    ["a"] = i, ["b"] = j, ["gap"] = BattleMap.UnitGap(a, b, geo, R), ["mods"] = Mods(BattleMap.MapModsFor(a, b, geo, R)),
                    ["melee"] = Reach(BattleMap.AttackReach(a, b, true, geo, R)), ["ranged"] = Reach(BattleMap.AttackReach(a, b, false, geo, R)),
                    ["los"] = new JsonObject { ["ok"] = los.Ok, ["why"] = los.Why },
                    ["run"] = new JsonObject { ["len"] = run.len, ["clear"] = run.clear },
                });
            }
        var reach = new JsonArray();
        for (int k = 0; k < 3; k++)
        {
            var u = units[k];
            double limit = BattleMap.UnitSpeed(u, R) * 1.5;
            var rm = BattleMap.Reach(u, geo, R, limit);
            var pts = new[] { new[] { 0.5, 0.5 }, new[] { 0.1, 0.9 }, new[] { u.MapX / 100 + 0.02, u.MapY / 100 }, new[] { u.MapX / 100, u.MapY / 100 - 0.03 },
                              new[] { u.MapX / 100 + 0.05, u.MapY / 100 + 0.05 }, new[] { 0.99, 0.01 } };
            var bytes = new byte[rm.Cost.Length * 8];
            Buffer.BlockCopy(rm.Cost, 0, bytes, 0, bytes.Length);
            reach.Add(new JsonObject
            {
                ["unit"] = k, ["limit"] = limit, ["hash"] = Fnv(bytes), ["finite"] = rm.Cost.Count(double.IsFinite),
                ["samples"] = new JsonArray(pts.Select(p => (JsonNode)new JsonArray(p[0], p[1], N(BattleMap.PathCost(rm, u, p[0], p[1], geo)))).ToArray()),
            });
        }
        Check(c, new JsonObject { ["W"] = geo.W, ["H"] = geo.H, ["per"] = per, ["pairs"] = pairs, ["reach"] = reach }, where);
    }

    static void PanicCase(JsonElement c, Rules R, string where)
    {
        var m = Generate(c);
        var geo = new Geo { Map = m, W = Terrain.WidthM(m), H = Terrain.HeightM(m) };
        var units = c.GetProperty("units").EnumerateArray().Select(UnitOf).ToList();
        var ctx = new EngineContext { Rules = R, Rng = new Mulberry32((uint)c.GetProperty("rngSeed").GetDouble()).Next };
        var res = Panic.Wave(units, c.GetProperty("source").GetInt32(), geo, ctx, c.GetProperty("moraleLoss").GetBoolean());
        Check(c, new JsonObject
        {
            ["lines"] = new JsonArray(res.Lines.Select(l => (JsonNode)l).ToArray()),
            ["patches"] = new JsonArray(res.Patches.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["patch"] = PatchOf(p.Patch) }).ToArray()),
            ["fled"] = new JsonArray(res.Fled.Select(id => (JsonNode)id).ToArray()),
        }, where);
    }

    // ── как трекер выводит результаты (mapcases.mjs: groundOut, modsOut, reachOut) ──
    static JsonNode Ground(GroundInfo g) => g == null ? null : new JsonObject
    {
        ["id"] = g.Id, ["key"] = g.Key, ["name"] = g.Name, ["z"] = g.Z, ["share"] = g.Share,
    };
    static string OrNull(string s) => string.IsNullOrEmpty(s) ? null : s;   // у StrikeMod «не задано» — пустая строка
    static JsonNode Strike(StrikeMod x) => new JsonObject
    {
        ["mult"] = x.Mult, ["note"] = OrNull(x.Note), ["coverPct"] = x.CoverPct, ["coverNote"] = OrNull(x.CoverNote),
    };
    static JsonNode Mods(MapMods m) => m == null ? null : new JsonObject
    {
        ["mode"] = m.Mode, ["noCharge"] = m.NoCharge, ["notes"] = new JsonArray(m.Notes.Select(s => (JsonNode)s).ToArray()),
        ["ab"] = Strike(m.Ab), ["ba"] = Strike(m.Ba),
    };
    static JsonNode Reach(ReachResult r) => new JsonObject { ["dist"] = r.Dist, ["max"] = r.Max, ["ok"] = r.Ok, ["text"] = r.Text };
    static JsonNode PatchOf(Patch p)
    {
        var o = new JsonObject();
        void Add(string k, JsonNode v) { if (v != null) o[k] = v; }
        Add("soldiers", p.Soldiers); Add("totKilled", p.TotKilled); Add("totWounded", p.TotWounded); Add("morale", p.Morale);
        Add("breakGrace", p.BreakGrace); Add("breakPenalty", p.BreakPenalty); Add("discipline", p.Discipline);
        Add("attacksMade", p.AttacksMade); Add("countersMade", p.CountersMade); Add("fleeChecks", p.FleeChecks);
        Add("fatigue", p.Fatigue); Add("turnsActive", p.TurnsActive); Add("broken", p.Broken); Add("acted", p.Acted); Add("status", p.Status);
        return o;
    }
    // JSON не знает бесконечности — трекер пишет словом
    static JsonNode N(double v) => double.IsFinite(v) ? v : v > 0 ? "inf" : v < 0 ? "-inf" : "nan";

    // FNV-1a (32 бита) по байтам — как fnv() в mapcases.mjs
    static string Fnv(byte[] bytes)
    {
        uint h = 0x811c9dc5;
        foreach (var b in bytes) { h ^= b; h = unchecked(h * 0x01000193); }
        return h.ToString("x8");
    }

    // ── ввод из JSON ──
    static Dictionary<string, object> Input(JsonElement e)
    {
        var d = new Dictionary<string, object>();
        foreach (var p in e.EnumerateObject())
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        return d;
    }
    static double Num(JsonElement e, string k, double def = 0) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    static Unit UnitOf(JsonElement e) => new Unit
    {
        Id = e.GetProperty("id").GetInt32(), Name = e.GetProperty("name").GetString(), Type = e.GetProperty("type").GetString(),
        Weapon = e.GetProperty("weapon").GetString(), Soldiers = Num(e, "soldiers"),
        FactionId = e.TryGetProperty("factionId", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : (int?)null,
        Status = e.GetProperty("status").GetString(), OnMap = e.GetProperty("onMap").GetBoolean(),
        MapX = Num(e, "mapX", 50), MapY = Num(e, "mapY", 50), Facing = Num(e, "facing"), Range = Num(e, "range"), RunUpM = Num(e, "runUpM"),
        Morale = Num(e, "morale"), Discipline = Num(e, "discipline"), FleeChecks = Num(e, "fleeChecks"),
        Broken = e.TryGetProperty("broken", out var br) && br.ValueKind == JsonValueKind.True,
        BreakGrace = Num(e, "breakGrace"), BreakPenalty = Num(e, "breakPenalty"),
    };

    // ── сравнение: каждое посчитанное поле — с тем же полем эталона, вглубь, числа — точно ──
    static void Check(JsonElement expected, JsonObject actual, string where)
    {
        var diffs = new List<string>();
        var act = JsonDocument.Parse(actual.ToJsonString(Readable)).RootElement;
        foreach (var p in act.EnumerateObject())
            Compare(expected.TryGetProperty(p.Name, out var e) ? e : default, p.Value, $"{where}.{p.Name}", diffs);
        if (diffs.Count > 0)
            throw new Exception($"расхождений {(diffs.Count >= MaxDiffs ? MaxDiffs + "+" : diffs.Count.ToString())}:\n      " + string.Join("\n      ", diffs));
    }
    const int MaxDiffs = 6;
    // кириллица в сообщениях — буквами, а не И
    static readonly JsonSerializerOptions Readable = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static bool IsNull(JsonElement e) => e.ValueKind == JsonValueKind.Undefined || e.ValueKind == JsonValueKind.Null;
    static string Show(JsonElement e)
    {
        if (IsNull(e)) return "null";
        var s = e.GetRawText();
        return s.Length > 120 ? s.Substring(0, 117) + "…" : s;
    }
    static void Compare(JsonElement e, JsonElement a, string path, List<string> diffs)
    {
        if (diffs.Count >= MaxDiffs) return;
        // undefined в JS выпадает из JSON — отсутствующее поле равно null
        if (IsNull(e) && IsNull(a)) return;
        void Diff() => diffs.Add($"{path}: трекер {Show(e)}, C# {Show(a)}");
        if (e.ValueKind != a.ValueKind) { Diff(); return; }
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var keys = e.EnumerateObject().Select(p => p.Name).Union(a.EnumerateObject().Select(p => p.Name));
                foreach (var k in keys)
                    Compare(e.TryGetProperty(k, out var ev) ? ev : default, a.TryGetProperty(k, out var av) ? av : default, $"{path}.{k}", diffs);
                break;
            case JsonValueKind.Array:
                if (e.GetArrayLength() != a.GetArrayLength())
                {
                    diffs.Add($"{path}: длина {e.GetArrayLength()} у трекера, {a.GetArrayLength()} у C#");
                    if (diffs.Count >= MaxDiffs) return;
                }
                for (int i = 0; i < Math.Min(e.GetArrayLength(), a.GetArrayLength()); i++) Compare(e[i], a[i], $"{path}[{i}]", diffs);
                break;
            case JsonValueKind.Number:
                if (e.GetDouble() != a.GetDouble()) Diff();
                break;
            case JsonValueKind.String:
                if (e.GetString() != a.GetString()) Diff();
                break;
        }
    }
}
