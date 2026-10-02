// ═══════════ ArmyFile.cs — армии в формате сохранения трекера (Г93: один формат для трекера и игры) ═══════════
// Файл трекера (armiya_hodN.txt) — JSON: фракции, подфракции, полководцы, отряды, журнал, ход, nextId и всё прочее
// (карта, правила карты, машины, штурмы, шаблоны). Unity правит только армии и пишет журнал; остальное — как было,
// поэтому файл после игры снова открывается трекером целиком. Отряд создаётся и клонируется, как в трекере
// (makeUnit, cloneUnit в tracker/src/ui/app.js), плюс поля облика `style` и `kit` (В16).
// Первая запись поверх существующего файла — сначала копия <файл>.bak (это твои настоящие партии).
// Без UnityEngine — проверяется и вне редактора.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Journal.Art;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Journal.Armies
{
    public sealed class ArmyFile
    {
        public string Path;                    // где лежит; null — ещё не сохранён
        public JObject Root;
        public bool Dirty;
        bool backedUp;

        public const string DefaultColor = "#C9A227";   // как DEFAULT_COLOR трекера

        // ── файлы ──
        public static ArmyFile Load(string path)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (!(root["units"] is JArray) && !(root["factions"] is JArray)) throw new InvalidDataException("это не сохранение трекера: нет ни фракций, ни отрядов");
            var f = new ArmyFile { Path = path, Root = root };
            f.Normalize();
            return f;
        }
        public static ArmyFile New()
        {
            var f = new ArmyFile { Root = new JObject { ["factions"] = new JArray(), ["subfactions"] = new JArray(), ["commanders"] = new JArray(), ["units"] = new JArray(), ["log"] = new JArray(), ["turn"] = 1, ["nextId"] = 1 } };
            f.Normalize();
            return f;
        }
        // как applyLoadedState трекера: недостающие поля — по умолчанию (в файле они появятся при записи)
        void Normalize()
        {
            foreach (var k in new[] { "factions", "subfactions", "commanders", "units", "log" }) if (!(Root[k] is JArray)) Root[k] = new JArray();
            if (Root["turn"] == null || Root["turn"].Type == JTokenType.Null) Root["turn"] = 1;
            int maxId = AllIds().DefaultIfEmpty(0).Max();
            if (Root["nextId"] == null || Root["nextId"].Type != JTokenType.Integer || (int)Root["nextId"] <= maxId) Root["nextId"] = maxId + 1;
            foreach (JObject f in Factions) if (f["color"] == null) f["color"] = DefaultColor;
        }
        IEnumerable<int> AllIds() => new[] { Factions, Subfactions, Commanders, Units }.SelectMany(a => a).Select(t => (int?)t["id"] ?? 0);

        public void Save(string path = null)
        {
            path ??= Path ?? throw new InvalidOperationException("куда сохранять — не задано");
            if (File.Exists(path) && !(backedUp && path == Path))
            {
                File.Copy(path, path + ".bak", true);
                backedUp = path == Path || Path == null;
            }
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // как JSON.stringify(state, null, 2) трекера: отступ 2 пробела, русские буквы как есть
            File.WriteAllText(path, Root.ToString(Formatting.Indented));
            if (Path != path) backedUp = true;
            Path = path; Dirty = false;
        }

        // Где искать: game/Saves (в .gitignore) и рабочий стол — сохранения трекера (armiya*.txt) и всё .txt/.json в Saves
        public static string SavesDir => System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Saves");
        public static List<string> Find()
        {
            var list = new List<string>();
            if (Directory.Exists(SavesDir)) list.AddRange(Directory.GetFiles(SavesDir).Where(p => p.EndsWith(".txt") || p.EndsWith(".json")));
            var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (Directory.Exists(desk)) list.AddRange(Directory.GetFiles(desk, "armiya*.txt"));
            return list.Distinct().OrderBy(p => p.StartsWith(SavesDir) ? 0 : 1).ThenBy(System.IO.Path.GetFileName).ToList();
        }

        // ── данные ──
        public JArray Factions => (JArray)Root["factions"];
        public JArray Subfactions => (JArray)Root["subfactions"];
        public JArray Commanders => (JArray)Root["commanders"];
        public JArray Units => (JArray)Root["units"];
        public int Turn => (int?)Root["turn"] ?? 1;
        int NextId() { int id = (int)Root["nextId"]; Root["nextId"] = id + 1; return id; }
        public static int? Id(JToken t) => t == null || t.Type == JTokenType.Null ? (int?)null : (int)t;
        // число как в трекере: целое — без «.0»
        public static JToken Num(double v) => Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15 ? (JToken)(long)Math.Round(v) : v;

        public JObject Faction(int? id) => id == null ? null : Factions.OfType<JObject>().FirstOrDefault(f => (int)f["id"] == id);
        public JObject Unit(int id) => Units.OfType<JObject>().FirstOrDefault(u => (int)u["id"] == id);
        public JObject Commander(int id) => Commanders.OfType<JObject>().FirstOrDefault(c => (int)c["id"] == id);
        public IEnumerable<JObject> UnitsOf(int? factionId) => Units.OfType<JObject>().Where(u => Id(u["factionId"]) == factionId);
        public IEnumerable<JObject> CommandersOf(int? factionId) => Commanders.OfType<JObject>().Where(c => Id(c["factionId"]) == factionId);
        public IEnumerable<JObject> SubfactionsOf(int? factionId) => Subfactions.OfType<JObject>().Where(s => Id(s["factionId"]) == factionId);
        public string FactionName(int? id) => (string)Faction(id)?["name"] ?? "Без фракции";

        // ── журнал: как addLog трекера — сверху, не больше 300 записей ──
        static long logSeq;
        public void Log(string title, params string[] lines)
        {
            var log = (JArray)Root["log"];
            double id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (++logSeq % 1000) / 1000.0;
            log.Insert(0, new JObject { ["id"] = id, ["turn"] = Turn, ["title"] = title, ["lines"] = new JArray(lines), ["tone"] = "info" });
            while (log.Count > 300) log.RemoveAt(log.Count - 1);
            Dirty = true;
        }

        // ── фракции ──
        public JObject AddFaction(string name, string color = DefaultColor, bool log = true)
        {
            var f = new JObject { ["id"] = NextId(), ["name"] = name, ["color"] = color };
            Factions.Add(f); Dirty = true; if (log) LogCreated(f); return f;
        }
        public void RemoveFaction(int id)
        {
            var f = Faction(id); if (f == null) return;
            var subIds = SubfactionsOf(id).Select(s => (int)s["id"]).ToList();
            f.Remove();
            foreach (var s in Subfactions.OfType<JObject>().Where(s => Id(s["factionId"]) == id).ToList()) s.Remove();
            foreach (JObject u in Units)
            {
                if (Id(u["factionId"]) == id) u["factionId"] = null;
                if (Id(u["subfactionId"]) is int sid && subIds.Contains(sid)) u["subfactionId"] = null;
            }
            foreach (JObject c in Commanders) if (Id(c["factionId"]) == id) c["factionId"] = null;
            Log($"Фракция «{(string)f["name"]}» распущена");
        }
        public JObject AddSubfaction(int factionId, string name)
        {
            var s = new JObject { ["id"] = NextId(), ["name"] = name, ["factionId"] = factionId };
            Subfactions.Add(s); Log($"Подфракция «{name}» сформирована", $"Входит в состав: {FactionName(factionId)}"); return s;
        }

        // ── полководцы ──
        public JObject AddCommander(int? factionId, string name, bool log = true)
        {
            var c = new JObject { ["id"] = NextId(), ["name"] = name, ["factionId"] = factionId, ["buffMorale"] = 0, ["buffDisc"] = 0, ["buffDmg"] = 0, ["buffDef"] = 0 };
            Commanders.Add(c); Dirty = true; if (log) LogCreated(c); return c;
        }
        public void RemoveCommander(int id)
        {
            var c = Commander(id); if (c == null) return;
            c.Remove();
            foreach (JObject u in Units) if (Id(u["commanderId"]) == id) u["commanderId"] = null;
            Log($"Полководец «{(string)c["name"]}» снят с командования");
        }

        // ── отряды ──
        // как makeUnit трекера: поля строя и счётчики — с нуля; облик — выбран или угадан (В16)
        public JObject AddUnit(string name, BattleCore.UnitTemplate t, int? factionId, double soldiers, string style = null, string kit = null, bool log = true)
        {
            var u = new JObject
            {
                ["id"] = NextId(), ["initial"] = Num(soldiers), ["status"] = "active",
                ["turnsActive"] = 0, ["fleeChecks"] = 0, ["breakGrace"] = 0, ["broken"] = false,
                ["acted"] = false, ["attacksMade"] = 0, ["countersMade"] = 0, ["totKilled"] = 0, ["totWounded"] = 0,
                ["onMap"] = false, ["mapX"] = 50, ["mapY"] = 50, ["facing"] = 0, ["movedM"] = 0, ["runUpM"] = 0, ["ladders"] = 0,
                ["name"] = name, ["type"] = t.Type, ["weapon"] = t.Weapon,
                ["factionId"] = factionId, ["subfactionId"] = null, ["commanderId"] = null,
                ["soldiers"] = Num(soldiers), ["discipline"] = Num(t.Discipline), ["morale"] = Num(t.Morale), ["eqAtk"] = Num(t.EqAtk), ["eqDef"] = Num(t.EqDef),
                ["exp"] = Num(t.Exp), ["mastery"] = Num(t.Mastery), ["fatigue"] = 0, ["range"] = 0,
                ["style"] = style ?? DefaultStyle(factionId, name), ["kit"] = kit ?? KitSets.Guess(name, t.Type, t.Weapon),
            };
            Units.Add(u); Dirty = true;
            if (log) LogCreated(u);
            return u;
        }
        // запись о создании — как в трекере; редактор пишет её, когда новое уже названо (а не под временным именем)
        public void LogCreated(JObject o)
        {
            if (Factions.Contains(o)) Log($"Фракция «{(string)o["name"]}» основана");
            else if (Commanders.Contains(o)) Log($"Полководец «{(string)o["name"]}» принял командование", BuffLine(o));
            else if (Units.Contains(o)) Log($"Юнит «{(string)o["name"]}» встал в строй", UnitLine(o));
        }
        public string UnitLine(JObject u) =>
            $"{(double?)u["soldiers"] ?? 0} солдат · дисц {(double?)u["discipline"] ?? 0} · БД {(double?)u["morale"] ?? 0} · {FactionName(Id(u["factionId"]))}";
        static string BuffLine(JObject c)
        {
            var parts = new List<string>();
            void B(string k, string n) { var v = (double?)c[k] ?? 0; if (Math.Abs(v) > 1e-9) parts.Add($"{n} {(v > 0 ? "+" : "")}{v}"); }
            B("buffMorale", "БД"); B("buffDisc", "дисц"); B("buffDmg", "урон"); B("buffDef", "защ");
            return parts.Count > 0 ? string.Join(" · ", parts) : "без бонусов";
        }
        // свежий отряд (ещё не воевал): начальная численность — вместе с текущей
        public static bool Fresh(JObject u) => ((double?)u["totKilled"] ?? 0) + ((double?)u["totWounded"] ?? 0) == 0 && ((double?)u["turnsActive"] ?? 0) == 0 && (string)u["status"] == "active";
        // как cloneUnit трекера: имя «… №N», полный состав, в строю, не на карте; облик — тот же
        public JObject CloneUnit(JObject src)
        {
            var c = (JObject)src.DeepClone();
            double men = (double?)src["soldiers"] > 0 ? (double)src["soldiers"] : (double?)src["initial"] ?? 0;
            c["id"] = NextId(); c["name"] = CloneName((string)src["name"]);
            c["initial"] = Num(men); c["soldiers"] = Num(men);
            c["status"] = "active"; c["turnsActive"] = 0; c["fleeChecks"] = 0; c["breakGrace"] = 0; c["broken"] = false;
            c["acted"] = false; c["attacksMade"] = 0; c["countersMade"] = 0; c["totKilled"] = 0; c["totWounded"] = 0;
            c["onMap"] = false; c["mapX"] = 50; c["mapY"] = 50; c["facing"] = 0;
            Units.Add(c);
            Log($"Юнит «{(string)c["name"]}» встал в строй (клон «{(string)src["name"]}»)",
                $"{men} солдат · дисц {(double?)c["discipline"] ?? 0} · БД {(double?)c["morale"] ?? 0} · {FactionName(Id(c["factionId"]))}");
            return c;
        }
        string CloneName(string name)
        {
            var m = Regex.Match(name ?? "", @"^(.*?)(?:\s№(\d+))?$");
            string root = m.Success ? m.Groups[1].Value : name;
            int maxN = 1;
            foreach (JObject u in Units)
            {
                var un = (string)u["name"] ?? "";
                var um = Regex.Match(un, @"^(.*?)\s№(\d+)$");
                if (um.Success && um.Groups[1].Value == root) maxN = Math.Max(maxN, int.Parse(um.Groups[2].Value));
            }
            return $"{root} №{maxN + 1}";
        }
        public void RemoveUnit(int id)
        {
            var u = Unit(id); if (u == null) return;
            u.Remove(); Log($"Юнит «{(string)u["name"]}» убран с карты");
        }

        // ── шаблоны: правки партии (templateOverrides трекера) — общие и по фракциям ──
        public BattleCore.TemplateOverrides Overrides() => BattleCore.Templates.NormalizeOverrides(Plain(Root["templateOverrides"]));
        // дерево JSON в виде, который ждёт движок: словари, списки, строки, числа, bool, null
        static object Plain(JToken t)
        {
            switch (t?.Type)
            {
                case JTokenType.Object: return ((JObject)t).Properties().ToDictionary(p => p.Name, p => Plain(p.Value));
                case JTokenType.Array: return t.Select(Plain).ToList();
                case null: case JTokenType.Null: case JTokenType.Undefined: return null;
                default: return (t as JValue)?.Value;
            }
        }
        // итоговый профиль для фракции: база → общие правки → правки фракции (как resolveTemplate трекера)
        public BattleCore.UnitTemplate Resolve(string tplId, int? factionId) => BattleCore.Templates.Resolve(tplId, (string)Faction(factionId)?["name"] ?? "", Overrides());
        // числа и род войск отряда — по профилю; род войск и оружие, угаданные по названию, важнее шаблонных
        public static void Apply(JObject u, BattleCore.UnitTemplate t, string type = null, string weapon = null)
        {
            u["type"] = type ?? t.Type; u["weapon"] = weapon ?? t.Weapon;
            foreach (var k in BattleCore.Templates.Stats) u[k] = Num(t[k]);
        }

        // Облик по умолчанию (В16): стиль — угаданный по имени, иначе как у прошлого отряда фракции, иначе Западный
        public string DefaultStyle(int? factionId, string name, JObject except = null)
        {
            var g = Styles.Guess(name);
            if (g != null) return g;
            var last = UnitsOf(factionId).LastOrDefault(u => u != except && Styles.Known((string)u["style"]));
            return (string)last?["style"] ?? Styles.Default;
        }
        // облик отряда, у которого полей нет (старые сохранения): угадать, не записывая
        public static string StyleOf(JObject u) => Styles.Known((string)u["style"]) ? (string)u["style"] : Styles.Guess((string)u["name"]) ?? Styles.Default;
        public static string KitOf(JObject u) => KitSets.Known((string)u["kit"]) ? (string)u["kit"] : KitSets.Guess((string)u["name"], (string)u["type"], (string)u["weapon"]);
    }
}
