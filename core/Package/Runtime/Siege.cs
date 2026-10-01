// ═══════════ Siege.cs — осадные орудия (этап 6б, Ш3–Ш4, Г47, Г49) — ЧЕРНОВИК ДО ГМа (копия siege.js) ═══════════
// Машина — фишка-батарея из Count одинаковых орудий с расчётом Crew. Числа каждого вида — в Rules.Siege.Engines.
// Залп: стреляют орудия, на которые хватает расчёта; каждое — d100 на попадание по дистанции и выучке; порох —
// ещё d100 на разрыв ствола. По стене — фиксированная прочность; по людям — d(Die) за попадание ÷ броня, дальше
// потери как за столом (Combat.Casualties, CasualtyPatch), порох — шок БД. Маг-батарея — один бросок на залп,
// доля численности цели, брызги соседям. Таран — без броска. Сверяется с трекером по shared/golden/map.json (siege).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class Machine
    {
        public string Engine, Name;
        public double Count, CountFull, Crew, Exp = 50, MageSkill, Ready, DeployLeft, Dmg;
        public double MapX = 50, MapY = 50;   // место на карте, % (как у отряда)
        public bool OnMap;
        public double? FactionId;
        public Machine Clone() => (Machine)MemberwiseClone();
    }

    // Изменения машины: заданы только те поля, что меняются (как объект-патч в JS)
    public sealed class MachinePatch
    {
        public double? Count, Crew, Ready, DeployLeft, Dmg, MageSkill, FactionId;
    }

    public sealed class SiegeTarget
    {
        public TerrainMap Map;          // цель — участок стены, если Map задана
        public int SectionId;
        public double AtX = double.NaN, AtY = double.NaN;   // точка попадания в клетках; NaN — центр участка
        public Unit Unit;               // или отряд
        public List<(Unit Unit, double Gap)> Splash = new List<(Unit, double)>();   // соседи и метры от края цели
    }

    public sealed class SiegeOpts
    {
        public double Dist;
        public bool? Los;               // false — цели не видно (прямой наводке нельзя)
        public double CoverPct;
    }

    public sealed class VolleyResult
    {
        public bool Ok;
        public string Title;
        public List<string> Lines = new List<string>();
        public string Tone;
        public MachinePatch Machine;
        public List<UnitPatch> Patches = new List<UnitPatch>();
        public FortHit Section;
        public int Guns, Hits, Bursts;
    }

    public sealed class AimResult
    {
        public double Dist;
        public SightResult Los = new SightResult();
        public double CoverPct;
        public string CoverNote;
        public double AtX = double.NaN, AtY = double.NaN;   // точка попадания по участку, в клетках
    }

    public sealed class MachineHit
    {
        public MachinePatch Patch;
        public List<string> Lines = new List<string>();
        public int Lost;
        public bool Calm;
        public List<UnitPatch> Patches = new List<UnitPatch>();
    }

    public static class Siege
    {
        public static Rules.EngineR EngineOf(string key, Rules R) => key != null && R.Siege.Engines.TryGetValue(key, out var e) ? e : null;

        // Новая батарея: count орудий с полным расчётом, выучка 50; у маг-пушки — навык мага 10
        public static Machine MakeMachine(string key, double count, Rules R)
        {
            var e = EngineOf(key, R) ?? throw new ArgumentException("нет такого орудия: " + key);
            double v = Js.Round(count);
            double n = Math.Max(1, double.IsNaN(v) || v == 0 ? 1 : v);   // Math.max(1, Math.round(+count) || 1)
            return new Machine { Engine = key, Name = e.Name, Count = n, CountFull = n, Crew = n * e.Crew, Exp = 50, MageSkill = e.Magic ? 10 : 0 };
        }
        public static double FiringGuns(Machine m, Rules R)
        {
            var e = EngineOf(m.Engine, R);
            if (e == null || m.Count <= 0 || m.Crew <= 0) return 0;
            return Math.Min(m.Count, Math.Floor(m.Crew / e.MinCrew));
        }
        public static double MachineReload(Machine m, Rules R)
        {
            var e = EngineOf(m.Engine, R);
            if (e == null || e.Reload == 0 || m.Count <= 0 || m.Crew <= 0) return 0;
            return Math.Max(e.Reload, Math.Ceiling(e.Reload * m.Count * e.Crew / m.Crew));
        }
        static (double Base, double Bonus, double Chance)? ChanceParts(Machine m, string vs, double dist, Rules R)
        {
            var S = R.Siege; var e = EngineOf(m.Engine, R);
            if (e == null || e.Ram || e.Tower) return null;
            double a = e.Range[0], b = e.Range[1];
            if (!(dist >= a && dist <= b)) return null;
            var hit = vs == "wall" ? e.HitWall : e.HitTroops;
            double near = hit[0], far = hit[1];
            double bas = near + (far - near) * (b > a ? (dist - a) / (b - a) : 0);
            double bonus = e.Magic ? (m.MageSkill - 10) * S.Magic.SkillK : (m.Exp - 50) * S.SkillK;
            return (bas, bonus, Js.Clamp(Js.Round(bas + bonus), S.HitMin, S.HitMax));
        }
        public static double? HitChance(Machine m, string vs, double dist, Rules R) => ChanceParts(m, vs, dist, R)?.Chance;

        static string N(double v) => Js.Num(v);
        static string R1(double v) => Js.Num(Js.R1(v));

        // Залп по участку стены или по отряду (см. siegeVolley в siege.js)
        public static VolleyResult Volley(Machine m, SiegeTarget target, SiegeOpts opts, EngineContext ctx)
        {
            var R = ctx.Rules; var S = R.Siege; var e = EngineOf(m.Engine, R);
            VolleyResult Fail(string line) => new VolleyResult { Ok = false, Title = "Выстрел невозможен", Lines = new List<string> { line }, Tone = "danger" };
            if (e == null) return Fail($"Неизвестное орудие: {m.Engine}");
            string who = $"«{m.Name}»";
            if (e.Tower) return Fail($"{who} не стреляет: осадная башня ведёт на стену (бой на стене — следующим шагом)");
            if (m.Count <= 0) return Fail($"{who}: все орудия выбиты");
            if (e.Magic && !(m.MageSkill > 0)) return Fail($"{who}: нет мага — батарея молчит. Захваченной батарее мага назначает ГМ");
            double guns = FiringGuns(m, R);
            if (guns == 0) return Fail($"{who}: расчёта ({N(m.Crew)} чел.) не хватает даже на одно орудие — нужно {N(e.MinCrew)}");
            if (m.DeployLeft > 0) return Fail($"{who} разворачивается после марша — ещё {N(m.DeployLeft)} х.");
            if (m.Ready > 0) return Fail($"{who} перезаряжается — ещё {N(m.Ready)} х.");
            var map = target.Map;
            var sec = map != null ? Fortify.GetSection(map, target.SectionId) : null;
            var def = target.Unit;
            if (map != null && sec == null) return Fail("Нет такого участка стены");
            if (sec == null && def == null) return Fail("Нет цели");
            if (sec != null && sec.Up == 0) return Fail($"Участок №{sec.Id} ({Terrain.FortKinds[sec.Kind]}) уже разрушен");
            if (def != null && (def.Status == "destroyed" || def.Soldiers <= 0)) return Fail($"«{def.Name}» уже уничтожен");
            double dist = opts.Dist;
            (double Base, double Bonus, double Chance)? parts = null;
            if (e.Ram)
            {
                if (sec == null) return Fail($"{who}: таран бьёт только ворота и стены");
                if (!(dist <= e.Range[1])) return Fail($"{who}: таран должен стоять вплотную (до {N(e.Range[1])} м), до цели {N(Js.Round(dist))} м");
            }
            else
            {
                parts = ChanceParts(m, sec != null ? "wall" : "troops", dist, R);
                if (parts == null) return Fail($"{who}: цель вне дальности — {N(Js.Round(dist))} м, орудие бьёт от {N(e.Range[0])} до {N(e.Range[1])} м");
                if (!e.Indirect && opts.Los == false) return Fail($"{who}: цели не видно — навесом через стены бьют только катапульта, требушет и мортира");
            }

            var L = new List<string>();
            string targetName = sec != null ? $"участок №{sec.Id} ({Terrain.FortKinds[sec.Kind]})" : $"«{def.Name}»";
            var res = new VolleyResult { Ok = true, Title = $"{(e.Magic ? "🔮" : "⚙")} {m.Name} → {targetName}", Lines = L };
            L.Add($"{e.Name}{(m.Count > 1 ? $" ×{N(m.Count)}" : "")}: бьёт {N(guns)} из {N(m.Count)}{(guns < m.Count ? $" — расчёта {N(m.Crew)} из {N(m.Count * e.Crew)}" : "")} · до цели {N(Js.Round(dist))} м{(e.Indirect ? " · навесом" : "")}");
            if (parts != null)
            {
                double b = Js.R1(parts.Value.Bonus);
                L.Add($"Шанс попасть: {R1(parts.Value.Base)}% на {N(Js.Round(dist))} м{(b != 0 ? $", {(e.Magic ? "навык мага" : "выучка расчёта")} {(b > 0 ? "+" : "−")}{N(Math.Abs(b))}%" : "")} → {N(parts.Value.Chance)}%");
            }

            // броски на попадание (и на разрыв ствола у пороха)
            double hits = 0, bursts = 0;
            if (e.Ram) hits = guns;
            else if (e.Magic)
            {
                double r = Dice.Roll(ctx.Rng, 100);
                hits = r <= parts.Value.Chance ? guns : 0;
                L.Add($"Бросок d100: {N(r)} — {(hits > 0 ? "попадание" : "мимо")}");
            }
            else
            {
                var rolls = new List<string>();
                for (int g = 0; g < guns; g++)
                {
                    if (e.Burst > 0)
                    {
                        double bb = Dice.Roll(ctx.Rng, 100);
                        if (bb <= e.Burst) { bursts++; rolls.Add($"разрыв ({N(bb)})"); continue; }
                    }
                    double r = Dice.Roll(ctx.Rng, 100);
                    rolls.Add(N(r));
                    if (r <= parts.Value.Chance) hits++;
                }
                L.Add($"Броски d100: {string.Join(", ", rolls)} → попаданий {N(hits)} из {N(guns - bursts)}");
                if (bursts > 0) L.Add($"💥 Разрыв ствола (d100 ≤ {N(e.Burst)}): потеряно орудий {N(bursts)}, погибло {N(bursts * e.Crew)} из расчёта · черновик");
            }

            string tone = hits > 0 ? "attack" : "info";
            if (sec != null)
            {
                double dmg = hits * e.Wall;
                if (e.Ram)
                {
                    double k = sec.Kind == "gateWood" || sec.Kind == "gateIron" || sec.Kind == "palisade" ? 1 : S.RamWallK;
                    dmg = guns * e.Wall * k;
                    L.Add($"Таран: {N(guns)} × {N(e.Wall)}{(k != 1 ? $" × {N(k)} — по камню таран слаб" : "")} = {R1(dmg)} урона");
                }
                else if (hits > 0) L.Add($"Урон стене: {N(hits)} × {N(e.Wall)} = {N(hits * e.Wall)}");
                if (dmg > 0)
                {
                    res.Section = Fortify.DamageSection(map, sec.Id, dmg, target.AtX, target.AtY, R);
                    L.AddRange(res.Section.Lines);
                    if (res.Section.Opened > 0) tone = "danger";
                }
                else L.Add("Стена цела");
            }
            else if (e.Magic)
            {
                if (hits > 0) MagicFire(m, guns, def, target.Splash, L, res.Patches, ctx);
                else L.Add($"Огонь ушёл мимо — «{def.Name}» цел");
            }
            else if (hits == 0) L.Add($"Все мимо — «{def.Name}» без потерь");
            else
            {
                var dice = new List<string>();
                double sum = 0;
                for (int k = 0; k < hits; k++) { double v = Dice.Roll(ctx.Rng, e.Die); dice.Add(N(v)); sum += v; }
                L.Add($"Урон: {N(hits)} × d{N(e.Die)} = {(hits > 1 ? string.Join(" + ", dice) + " = " : "")}{N(sum)}");
                var Df = R.Defense;
                double divisor = Math.Max(Df.MinDivisor, def.EqDef / Df.RangedEqDiv);
                double dmg = sum / divisor;
                L.Add($"Защита цели: урон / ({N(def.EqDef)}/{N(Df.RangedEqDiv)}) → {N(sum)} / {R1(divisor)} = {R1(dmg)}");
                double cover = opts.CoverPct;
                if (cover > 0 && !e.Indirect)
                {
                    double before = dmg;
                    dmg *= 1 - cover / 100;
                    L.Add($"Укрытие цели: −{N(cover)}% ({R1(before)} → {R1(dmg)}) · черновик");
                }
                else if (cover > 0) L.Add($"Навесом — укрытие цели ({N(cover)}%) не спасает");
                var sr = Combat.Casualties(def, dmg, L, ctx);
                var patch = Combat.CasualtyPatch(def, sr, L, ctx, out _);
                if (e.Shock > 0 && patch.Status != "destroyed")
                {
                    var cur = def.Clone();
                    patch.ApplyTo(cur);
                    L.Add($"💥 Грохот и дым: БД «{def.Name}» −{N(e.Shock)} сверх потерь · черновик");
                    patch.Merge(MoraleRules.ApplyMoraleChange(cur, cur.Morale - e.Shock, L, R));
                }
                res.Patches.Add(new UnitPatch(def.Id, patch));
            }

            // машина после залпа: разрывы уносят орудия и людей, перезарядка — по оставшемуся расчёту
            double count = m.Count - bursts, crew = Math.Max(0, m.Crew - bursts * e.Crew);
            var after = m.Clone(); after.Count = count; after.Crew = crew;
            double ready = count > 0 ? MachineReload(after, R) : 0;
            if (count > 0) L.Add($"{who}: перезарядка {N(ready)} х.{(ready > e.Reload ? $" — людей мало ({N(crew)} из {N(count * e.Crew)})" : "")}");
            else L.Add($"☠ {who}: все орудия потеряны");
            if (bursts > 0) tone = "danger";
            res.Tone = tone;
            res.Machine = new MachinePatch { Count = count, Crew = crew, Ready = ready };
            res.Guns = (int)guns; res.Hits = (int)hits; res.Bursts = (int)bursts;
            return res;
        }

        // Маг-батарея: доля численности цели × доля живых орудий, броня — не больше −15%; брызги — соседям, своим тоже
        static void MagicFire(Machine m, double guns, Unit def, List<(Unit Unit, double Gap)> splash, List<string> L, List<UnitPatch> patches, EngineContext ctx)
        {
            var M = ctx.Rules.Siege.Magic;
            double share = guns / m.CountFull;
            double pct = M.KillPct[0] + Dice.Roll(ctx.Rng, M.KillPct[1] - M.KillPct[0] + 1) - 1;
            double armorK = 1 - Math.Min(M.ArmorCap, def.EqDef / M.ArmorDiv);
            double dmg = def.Soldiers * pct / 100 * share * armorK;
            L.Add($"Огонь батареи: {N(pct)}% численности × {N(guns)}/{N(m.CountFull)} орудий, броня −{R1((1 - armorK) * 100)}% → {R1(dmg)} · черновик");
            var r = Combat.Casualties(def, dmg, L, ctx);
            patches.Add(new UnitPatch(def.Id, Combat.CasualtyPatch(def, r, L, ctx, out _)));
            foreach (var (u, gap) in splash ?? new List<(Unit, double)>())
            {
                if (u == null || u.Id == def.Id || u.Status == "destroyed" || u.Soldiers <= 0 || !(gap <= M.SplashM)) continue;
                double sp = M.SplashPct[0] + Dice.Roll(ctx.Rng, M.SplashPct[1] - M.SplashPct[0] + 1) - 1;
                double d = u.Soldiers * sp / 100 * share;
                L.Add($"Брызги огня — «{u.Name}» в {N(Js.Round(gap))} м от цели: {N(sp)}% × {N(guns)}/{N(m.CountFull)} → {R1(d)}");
                var rs = Combat.Casualties(u, d, L, ctx);
                patches.Add(new UnitPatch(u.Id, Combat.CasualtyPatch(u, rs, L, ctx, out _)));
            }
        }

        // ── между залпами ──
        public static MachinePatch EndTurn(Machine m) => new MachinePatch { Ready = Math.Max(0, m.Ready - 1), DeployLeft = Math.Max(0, m.DeployLeft - 1) };
        public static MachinePatch Moved(Machine m, Rules R)
        {
            var e = EngineOf(m.Engine, R);
            return new MachinePatch { DeployLeft = e != null ? 1 + e.Deploy : 0 };
        }
        // Удар по самой машине: прочность одного орудия — Hp; выбитое орудие уносит свой расчёт
        public static MachineHit HitMachine(Machine m, double amount, Rules R)
        {
            var e = EngineOf(m.Engine, R);
            var h = new MachineHit();
            double dmg = m.Dmg + Math.Max(0, amount), count = m.Count;
            while (dmg >= e.Hp && count > 0) { dmg -= e.Hp; count--; h.Lost++; }
            double crew = Math.Min(m.Crew, count * e.Crew);
            if (count == 0) dmg = 0;
            h.Lines.Add($"⚙ «{m.Name}»: удар −{R1(amount)} прочности{(h.Lost > 0 ? $", выбито орудий {h.Lost}" : "")} · черновик");
            h.Lines.Add(count > 0 ? $"Осталось орудий {N(count)} из {N(m.CountFull)}, повреждение {R1(dmg)} из {N(e.Hp)}" : $"☠ «{m.Name}» уничтожена");
            h.Patch = new MachinePatch { Count = count, Crew = crew, Dmg = dmg };
            return h;
        }
        // Прямое попадание по маг-пушке: d20 ≤ навык мага — гаснет, иначе взрыв по всем ближе ExplodeM
        public static MachineHit MagicStrike(Machine m, List<(Unit Unit, double Gap)> nearby, EngineContext ctx)
        {
            var R = ctx.Rules; var M = R.Siege.Magic;
            var h = new MachineHit();
            double roll = Dice.Roll(ctx.Rng, 20);
            h.Calm = roll <= m.MageSkill;
            h.Lines.Add($"🔮 Попадание по «{m.Name}»: d20 = {N(roll)} {(h.Calm ? "≤" : ">")} навык мага {N(m.MageSkill)}");
            double count = Math.Max(0, m.Count - 1);
            if (h.Calm) h.Lines.Add("Маг удержал огонь — пушка погасла, взрыва нет");
            else
            {
                h.Lines.Add($"💥 Взрыв маг-пушки — всем в {N(M.ExplodeM)} м · черновик");
                foreach (var (u, gap) in nearby)
                {
                    if (u == null || u.Status == "destroyed" || u.Soldiers <= 0 || !(gap <= M.ExplodeM)) continue;
                    double p = M.ExplodePct[0] + Dice.Roll(ctx.Rng, M.ExplodePct[1] - M.ExplodePct[0] + 1) - 1;
                    h.Lines.Add($"«{u.Name}» в {N(Js.Round(gap))} м: {N(p)}% численности");
                    var r = Combat.Casualties(u, u.Soldiers * p / 100, h.Lines, ctx);
                    h.Patches.Add(new UnitPatch(u.Id, Combat.CasualtyPatch(u, r, h.Lines, ctx, out _)));
                }
            }
            var e = EngineOf(m.Engine, R);
            h.Patch = new MachinePatch { Count = count, Crew = Math.Min(m.Crew, count * e.Crew) };
            return h;
        }
        // ── прицел с карты (Ш4): как siegeAim в siege.js ──
        static SightResult SightLine(Geo geo, Rules R, double ax, double ay, double za, double bx, double by, double zb, Func<int, bool> skip)
        {
            double len = JsMath.Hypot(bx - ax, by - ay);
            int n = (int)Math.Max(2, Math.Ceiling(len / 5));
            double step = len / n;
            var through = new Dictionary<string, double>();
            for (int k = 1; k < n; k++)
            {
                var c = Terrain.CellAt(geo.Map, (ax + (bx - ax) * k / n) / geo.W, (ay + (by - ay) * k / n) / geo.H);
                if (skip(c.Y * geo.Map.W + c.X)) continue;
                if (c.Z > Math.Max(za, zb)) return new SightResult { Ok = false, Why = "холм" };
                if (c.T == 0) continue;
                var t = Terrain.ById[c.T];
                if (R.Map.Terrain.TryGetValue(t.Key, out var tr) && tr.Sight.HasValue)
                {
                    through[t.Key] = (through.TryGetValue(t.Key, out var was) ? was : 0) + step;
                    if (through[t.Key] > tr.Sight.Value) return new SightResult { Ok = false, Why = t.Name.ToLowerInvariant() };
                }
            }
            return new SightResult();
        }
        public static AimResult Aim(Machine m, SiegeTarget target, Geo geo, Rules R)
        {
            double mx = m.MapX / 100 * geo.W, my = m.MapY / 100 * geo.H;
            var map = geo.Map;
            var mc = map != null ? Terrain.CellAt(map, mx / geo.W, my / geo.H) : default;
            int own = map != null && map.S != null ? map.S[mc.Y * map.W + mc.X] : 0;
            if (target.Unit != null)
            {
                var u = target.Unit;
                double dist = BattleMap.PolyGap(new[] { new[] { mx, my } }, BattleMap.UnitCorners(u, geo, R));
                if (map == null) return new AimResult { Dist = dist };
                var g = BattleMap.GroundUnder(u, geo, R);
                var (bx, by) = BattleMap.UnitCenter(u, geo);
                var los = SightLine(geo, R, mx, my, mc.Z, bx, by, g.Z, i => own != 0 && map.S[i] == own);
                double cover = g.Key != null ? R.Map.Terrain[g.Key].Cover : 0;
                return new AimResult { Dist = dist, Los = los, CoverPct = cover, CoverNote = cover > 0 ? $"укрытие «{u.Name}»: {g.Name.ToLowerInvariant()}" : null };
            }
            var f = map != null && target.Map != null ? Fortify.GetSection(map, target.SectionId) : null;
            if (f == null) return null;
            double cw = geo.W / map.W, ch = geo.H / map.H;
            byte code = Terrain.FortCode(f.Kind);
            int best = -1; double bestD = double.PositiveInfinity;
            foreach (bool standing in new[] { true, false })
            {
                for (int i = 0; i < map.S.Length; i++)
                {
                    if (map.S[i] != f.Id || (standing && map.T[i] != code)) continue;
                    int x = i % map.W, y = i / map.W;
                    double dx = Math.Max(Math.Max(x * cw - mx, 0), mx - (x + 1) * cw), dy = Math.Max(Math.Max(y * ch - my, 0), my - (y + 1) * ch);
                    double d = JsMath.Hypot(dx, dy);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) break;
            }
            int bx0 = best % map.W, by0 = best / map.W;
            int fid = f.Id;
            var sl = SightLine(geo, R, mx, my, mc.Z, (bx0 + 0.5) * cw, (by0 + 0.5) * ch, map.Z[best], i => map.S[i] == fid || (own != 0 && map.S[i] == own));
            return new AimResult { Dist = bestD, Los = sl, AtX = bx0 + 0.5, AtY = by0 + 0.5 };
        }
        // Захват (Г49): маг-батарее нового мага назначает ГМ
        public static MachinePatch Capture(Machine m, double factionId, Rules R)
        {
            var e = EngineOf(m.Engine, R);
            return new MachinePatch { FactionId = factionId, MageSkill = e != null && e.Magic ? 0 : m.MageSkill, Ready = m.Ready, DeployLeft = m.DeployLeft };
        }
    }
}
