// ═══════════ SaveScene.cs — бой из сохранения трекера (armiya_hodN.txt): отладка смотрелки на настоящем масштабе ═══════════
// Сохранение — JSON трекера: фракции, отряды (mapX/mapY в % картинки, facing), картинка карты (mapImage), лог.
// Клеток местности в таких сохранениях нет: карта — поле шириной mapOpts.widthM (в старых сохранениях её нет — 2000 м,
// как в трекере), высота — по пропорциям картинки; картинка ложится землёй. Фишки стоят теснее строёв (в 50–60 м при
// фронте 100–300 м) — отряды налезают, движок их расталкивает. Шире карта — меньше налезают, но счёт дороже: пути
// (FlowField) строятся по всей карте, 4000 м считается в 2,5 раза дольше 2000 м. Стороны движку — по логу боёв («A → B», «A ⇄ B»):
// фракции, что дрались друг с другом, — враги, остальное — раскраска графа в два цвета. Цвет отряда — цвет фракции.
// Сохранение — конец хода: бежавшие отряды возвращаются в строй, уничтоженных нет. Приказ на ход — бить ближайшего врага.
// Без UnityEngine: строится в фоновом потоке, как и остальные сцены. Разбор — Newtonsoft (пакет com.unity.nuget.newtonsoft-json).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BattleCore;
using Newtonsoft.Json.Linq;

namespace Journal.Viewer
{
    public sealed class TrackerSave
    {
        public string File; public int Turn;
        public double W, H;                                   // м
        public string Scale;                                  // как выбрана ширина карты (для подписи)
        public byte[] Image;                                  // картинка карты (JPEG/PNG) или null
        public readonly List<Unit> Units = new List<Unit>();
        public readonly Dictionary<int, (string Name, string Color)> Factions = new Dictionary<int, (string, string)>();
        public readonly Dictionary<int, int> Side = new Dictionary<int, int>();   // фракция → сторона 1 / 2
    }

    public static class SaveScene
    {
        // где искать сохранения: game/Saves (в .gitignore) и рабочий стол
        public static List<string> Find()
        {
            var dirs = new[] { Path.Combine(Directory.GetCurrentDirectory(), "Saves"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            var files = new List<string>();
            foreach (var d in dirs)
                if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "armiya_hod*.txt"));
            return files.GroupBy(Path.GetFileName).Select(g => g.First())
                .Where(f => { try { return Read(f, headerOnly: true) != null; } catch { return false; } })
                .OrderBy(f => TurnOf(f)).ToList();
        }
        public static string Label(string f) => $"Сохранение: ход {TurnOf(f)}";
        static int TurnOf(string f) { var m = Regex.Match(Path.GetFileName(f), @"hod(\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }

        // headerOnly — только проверить, что в сохранении есть расстановка на карте (для списка сцен); widthM > 0 — ширина карты явно
        public static TrackerSave Read(string path, bool headerOnly = false, double widthM = 0)
        {
            var o = JObject.Parse(System.IO.File.ReadAllText(path));
            var units = (JArray)o["units"];
            if (units == null || !units.Any(u => (bool?)u["onMap"] == true)) return null;
            var s = new TrackerSave { File = path, Turn = (int?)o["turn"] ?? 0 };
            if (headerOnly) return s;
            foreach (var f in (JArray)o["factions"] ?? new JArray())
                s.Factions[(int)f["id"]] = ((string)f["name"] ?? "", (string)f["color"] ?? "#888888");
            // карта: ширина из mapOpts.widthM (нет — 2000 м), высота — по картинке
            double aspect = 0.75;
            var img = (string)o["mapImage"];
            if (img != null && img.StartsWith("data:image/") && img.Contains(","))
            {
                s.Image = Convert.FromBase64String(img.Substring(img.IndexOf(',') + 1));
                var (iw, ih) = ImageSize(s.Image);
                if (iw > 0 && ih > 0) aspect = (double)ih / iw;
            }
            foreach (var e in units) s.Units.Add(UnitOf(e));
            var wm = (double?)o["mapOpts"]?["widthM"];
            if (widthM > 0) { s.W = widthM; s.Scale = $"ширина {s.W:0} м задана"; }
            else if (wm.HasValue) { s.W = wm.Value; s.Scale = $"ширина {s.W:0} м из сохранения"; }
            else { s.W = 2000; s.Scale = "ширины в сохранении нет — 2000 м, как в трекере; строи налезают"; }
            s.H = s.W * aspect;
            Sides(s, (JArray)o["log"]);
            return s;
        }

        static double Num(JToken e, string k, double def = 0) { var v = e[k]; return v != null && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (double)v : def; }
        static int? Int(JToken e, string k) { var v = e[k]; return v != null && v.Type == JTokenType.Integer ? (int)v : (int?)null; }
        static Unit UnitOf(JToken e) => new Unit
        {
            Id = (int)e["id"], Name = (string)e["name"] ?? "", Type = (string)e["type"] ?? "infantry", Weapon = (string)e["weapon"] ?? "melee",
            FactionId = Int(e, "factionId"), SubfactionId = Int(e, "subfactionId"), CommanderId = Int(e, "commanderId"),
            Soldiers = Num(e, "soldiers"), Initial = Num(e, "initial"), Discipline = Num(e, "discipline"), Morale = Num(e, "morale"),
            EqAtk = Num(e, "eqAtk"), EqDef = Num(e, "eqDef"), Exp = Num(e, "exp"), Mastery = Num(e, "mastery"), Fatigue = Num(e, "fatigue"),
            Status = (string)e["status"] ?? "active", TurnsActive = Num(e, "turnsActive"), FleeChecks = Num(e, "fleeChecks"),
            BreakGrace = Num(e, "breakGrace"), BreakPenalty = Num(e, "breakPenalty"), Broken = (bool?)e["broken"] ?? false,
            OnMap = (bool?)e["onMap"] ?? false, MapX = Num(e, "mapX", 50), MapY = Num(e, "mapY", 50), Facing = Num(e, "facing"), TokenScale = Num(e, "tokenScale", 1),
        };

        // стороны по логу: пары «A → B · …» и «A ⇄ B · …» — фракции A и B враги; граф красим в два цвета обходом в ширину
        static void Sides(TrackerSave s, JArray log)
        {
            var facOf = new Dictionary<string, int>();
            foreach (var u in s.Units) if (u.FactionId.HasValue) facOf[u.Name] = u.FactionId.Value;
            var foes = new Dictionary<int, HashSet<int>>();
            var re = new Regex(@"^(.+?) (?:→|⇄) (.+?)(?: · .*)?$");
            foreach (var l in log ?? new JArray())
            {
                var m = re.Match((string)l["title"] ?? "");
                if (!m.Success || !facOf.TryGetValue(m.Groups[1].Value.Trim(), out int a) || !facOf.TryGetValue(m.Groups[2].Value.Trim(), out int b) || a == b) continue;
                (foes.TryGetValue(a, out var fa) ? fa : foes[a] = new HashSet<int>()).Add(b);
                (foes.TryGetValue(b, out var fb) ? fb : foes[b] = new HashSet<int>()).Add(a);
            }
            // сначала фракции с боями (больше врагов — раньше), потом остальные — к стороне 1
            foreach (var f0 in foes.Keys.OrderByDescending(f => foes[f].Count).Concat(s.Factions.Keys))
            {
                if (s.Side.ContainsKey(f0)) continue;
                s.Side[f0] = 1;
                var q = new Queue<int>(); q.Enqueue(f0);
                while (q.Count > 0)
                {
                    int f = q.Dequeue();
                    if (!foes.TryGetValue(f, out var fs)) continue;
                    foreach (var g in fs) if (!s.Side.ContainsKey(g)) { s.Side[g] = 3 - s.Side[f]; q.Enqueue(g); }
                }
            }
        }

        // размер картинки из заголовка (JPEG — маркер SOF, PNG — IHDR), без декодирования
        static (int w, int h) ImageSize(byte[] b)
        {
            if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50) return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
            for (int i = 2; i + 9 < b.Length;)
            {
                if (b[i] != 0xFF) { i++; continue; }
                int mk = b[i + 1], len = (b[i + 2] << 8) | b[i + 3];
                if (mk >= 0xC0 && mk <= 0xCF && mk != 0xC4 && mk != 0xC8 && mk != 0xCC) return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                if (mk == 0xD8 || mk == 0x01 || (mk >= 0xD0 && mk <= 0xD7)) { i += 2; continue; }
                i += 2 + len;
            }
            return (0, 0);
        }

        // облик по имени и роду войск — в сохранении шаблона нет
        static string TplOf(Unit u)
        {
            string n = u.Name.ToLowerInvariant();
            if (n.Contains("арбалет")) return "crossbowmen";
            if (n.Contains("лучник") || n.Contains("охотник") || u.Type == "archer") return n.Contains("ополч") ? "militia_archers" : "archers";
            if (n.Contains("пикин") || n.Contains("алебард") || u.Type == "pike") return "pikemen";
            if (u.Type == "cavalry") return n.Contains("элит") ? "elite_cavalry" : "knights";
            if (n.Contains("страж") || n.Contains("гвард")) return "guard";
            if (n.Contains("ополч")) return "militia";
            return "infantry";
        }

        public static SceneDef Make(string path, int turns = 4, uint seed = 16, double widthM = 0)
        {
            var R = Rules.Base;
            var s = Read(path, widthM: widthM);
            var geo = SceneDef.Open(s.W, s.H);
            var sc = new SceneDef { Name = $"Сохранение: ход {s.Turn}", Turns = turns, Geo = geo, Image = s.Image };
            sc.Battle = new Battle(geo, R, new EngineContext { Rng = new Mulberry32(seed).Next });
            int placed = 0, back = 0; double men = 0;
            foreach (var u0 in s.Units)
            {
                if (u0.Status == "destroyed" || u0.Soldiers < 1 || !u0.OnMap) continue;
                var u = u0.Clone();
                int fac = u.FactionId ?? 0;
                if (u.Status == "fled") { back++; u.Status = "active"; u.Broken = false; }
                u.FactionId = s.Side.TryGetValue(fac, out var side) ? side : 1;   // движку — сторона, смотрелке — цвет фракции
                double x = Math.Max(20, Math.Min(s.W - 20, u.MapX / 100 * s.W)), y = Math.Max(20, Math.Min(s.H - 20, u.MapY / 100 * s.H));
                var m = sc.Battle.Add(u, x, y, u.Facing);
                sc.Units.Add((m, null)); sc.Tpl[m] = TplOf(u);
                sc.Color[m] = s.Factions.TryGetValue(fac, out var fi) ? fi.Color : null;
                placed++; men += u.Soldiers;
            }
            string sideNames(int k) => string.Join(", ", s.Side.Where(p => p.Value == k && s.Units.Any(u => u.FactionId == p.Key && u.Status != "destroyed"))
                .Select(p => s.Factions.TryGetValue(p.Key, out var f) ? f.Name : "№" + p.Key));
            sc.SideNames[1] = sideNames(1); sc.SideNames[2] = sideNames(2);
            sc.Note = $"{Path.GetFileName(path)}: {placed} отрядов, {men:0} бойцов; {sideNames(1)} — против — {sideNames(2)}. " +
                      $"Карта {s.W:0} × {s.H:0} м ({s.Scale}). Бежавшие ({back}) — снова в строю; приказ — бить ближайшего врага.";
            sc.Before = turn => OrderNearest(sc);
            return sc;
        }

        // каждому, кто в строю и не в схватке, — «атаковать» ближайшего живого врага
        static void OrderNearest(SceneDef sc)
        {
            var all = sc.Battle.Movers;
            bool Up(Mover m) => !m.Gone && !m.Fleeing && m.P.U.Status == "active" && m.P.U.Soldiers > 0;
            var busy = new HashSet<Mover>(sc.Battle.Fights.Where(f => !f.Over).SelectMany(f => new[] { f.A, f.B }));
            foreach (var m in all)
            {
                if (!Up(m) || busy.Contains(m)) continue;
                Mover best = null; double bd = double.MaxValue;
                foreach (var e in all)
                {
                    if (!Up(e) || e.P.U.FactionId == m.P.U.FactionId) continue;
                    double d = (e.P.X - m.P.X) * (e.P.X - m.P.X) + (e.P.Y - m.P.Y) * (e.P.Y - m.P.Y);
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null && (m.Order == null || m.Order.Kind != OrderKind.Attack || m.Order.TargetId != best.P.U.Id))
                    sc.Order(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = best.P.U.Id });
            }
        }
    }
}
