// ═══════════ Assault.cs — приступ на стену (этап 6б, Г48, Г50, Ш10–Ш14) — ЧЕРНОВИК ДО ГМа (копия assault.js) ═══════════
// По лестницам — в бой за ход «лестниц × 10» бойцов, защитник бьёт сверху вниз ×1,2, штурмующие снизу вверх ×0,9,
// ответным ударом защитник сбрасывает лестницы; через осадную башню — «башен × вместимость», без высоты; в пролом —
// весь отряд, 1 отряд на каждые 10 м. На участок за ход — не больше 2 отрядов. Защитников нет — участок занят (Holder).
// Минимум броска штурмующих — не больше бойцов в деле (BattleRequest.RollCap). Сверяется по shared/golden/map.json.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class AssaultOpts
    {
        public string Via = "ladders";   // ladders | tower | breach
        public double Gap, Ladders, Towers;
        public List<int> Engaged = new List<int>();
        public string FatigueMode = "percent";
    }

    public sealed class AssaultResult
    {
        public bool Ok;
        public string Title, Tone;
        public List<string> Lines = new List<string>();
        public List<UnitPatch> Patches = new List<UnitPatch>();
        public double? Ladders;
        public bool Captured;
    }

    public static class Assault
    {
        static string N(double v) => Js.Num(v);

        public static double LaddersFor(Unit u, Rules R) => Math.Floor(Math.Max(0, u.Soldiers) / R.Siege.Assault.LaddersPer);

        // От края строя до ближайшей клетки участка, м
        public static double SectionGap(Unit u, int secId, Geo geo, Rules R)
        {
            var map = geo.Map;
            double cw = geo.W / map.W, ch = geo.H / map.H;
            var P = BattleMap.UnitCorners(u, geo, R);
            double best = double.PositiveInfinity;
            for (int i = 0; i < map.S.Length; i++)
            {
                if (map.S[i] != secId) continue;
                int x = i % map.W, y = i / map.W;
                var Q = new[] { new[] { x * cw, y * ch }, new[] { (x + 1) * cw, y * ch }, new[] { (x + 1) * cw, (y + 1) * ch }, new[] { x * cw, (y + 1) * ch } };
                double d = BattleMap.PolyGap(P, Q);
                if (d < best) best = d;
            }
            return best;
        }

        static bool SameSide(Unit a, Unit u) => a.FactionId.HasValue && a.FactionId.Value != 0 && u.FactionId == a.FactionId;

        // Защитники участка: чужие отряды в строю на карте, чей строй ближе DefenderReachM к стене — ближние первыми
        public static List<(Unit Unit, double Gap)> DefendersOf(int secId, IList<Unit> units, Unit attacker, Geo geo, Rules R)
        {
            double reach = R.Siege.Assault.DefenderReachM;
            var list = units
                .Where(u => u.OnMap && u.Status == "active" && u.Soldiers > 0 && u.Id != attacker.Id && !SameSide(attacker, u))
                .Select(u => (Unit: u, Gap: SectionGap(u, secId, geo, R)))
                .Where(x => x.Gap <= reach)
                .ToList();
            list.Sort((a, b) => a.Gap != b.Gap ? a.Gap.CompareTo(b.Gap) : a.Unit.Id.CompareTo(b.Unit.Id));
            return list;
        }

        // Сколько своих осадных башен у участка: башня не дальше TowerReachM от стены и от строя отряда
        public static double TowersAt(int secId, IList<Machine> machines, Unit A, Geo geo, Rules R)
        {
            double reach = R.Siege.Assault.TowerReachM, n = 0;
            foreach (var m in machines)
            {
                var e = Siege.EngineOf(m.Engine, R);
                if (e == null || !e.Tower || !(m.Count > 0) || !m.OnMap) continue;
                if (A.FactionId.HasValue && A.FactionId.Value != 0 && m.FactionId != A.FactionId.Value) continue;
                var wall = Siege.Aim(m, new SiegeTarget { Map = geo.Map, SectionId = secId }, geo, R);
                if (wall == null || wall.Dist > reach) continue;
                if (Siege.Aim(m, new SiegeTarget { Unit = A }, geo, R).Dist > reach) continue;
                n += m.Count;
            }
            return n;
        }

        // Приступ (см. assaultWall в assault.js)
        public static AssaultResult AssaultWall(Unit A, TerrainMap map, int secId, List<(Unit Unit, double Gap)> defenders, AssaultOpts opts, EngineContext ctx)
        {
            var R = ctx.Rules; var S = R.Siege; var Q = S.Assault; var H = R.Map.Height;
            AssaultResult Fail(string line) => new AssaultResult { Ok = false, Title = "Приступ невозможен", Tone = "danger", Lines = new List<string> { line } };
            var sec = Fortify.GetSection(map, secId);
            if (sec == null) return Fail("Нет такого участка стены");
            string name = $"участок №{sec.Id} ({Terrain.FortKinds[sec.Kind]})", via = opts.Via;
            if (A.Status != "active" || !(A.Soldiers > 0)) return Fail($"«{A.Name}» не в строю");
            double lim = Units.AttackLimit(A, R);
            if (A.AttacksMade >= lim) return Fail($"«{A.Name}» уже израсходовал атаки в этом ходу ({N(A.AttacksMade)}/{N(lim)})");
            if (via != "breach" && Units.IsCav(A)) return Fail($"«{A.Name}» — конница: по лестницам и с башни на стену не лезет, в пролом — можно");
            if (via == "breach") { if (sec.Breaches == 0) return Fail($"В участке №{sec.Id} нет пролома"); }
            else if (sec.Up == 0) return Fail($"Участок №{sec.Id} разрушен целиком — лезть некуда, бой идёт в проломе");
            if (via == "ladders" && !(opts.Ladders > 0)) return Fail($"У отряда «{A.Name}» нет лестниц");
            if (via == "tower" && !(opts.Towers > 0))
                return Fail($"У отряда «{A.Name}» нет своей осадной башни у этого участка — башня и строй должны стоять не дальше {N(Q.TowerReachM)} м от стены и друг от друга");
            if (via != "tower" && !(opts.Gap <= Q.ReachM)) return Fail($"«{A.Name}» далеко от стены: {N(Js.Round(opts.Gap))} м, нужно не дальше {N(Q.ReachM)} м");
            var engaged = opts.Engaged.Where(id => id != A.Id).ToList();
            double limit = via == "breach" ? Math.Max(1, Math.Floor(sec.Breaches * S.BreachM / Q.BreachPerUnitM)) : Q.MaxUnits;
            if (engaged.Count >= limit)
                return Fail($"На участке №{sec.Id} в этом ходу уже бились {engaged.Count} отр. — больше не влезет ({(via == "breach" ? $"в пролом — 1 отряд на каждые {N(Q.BreachPerUnitM)} м" : $"не больше {N(Q.MaxUnits)} отрядов")}, Г50)");

            double cap = Siege.EngineOf("tower", R).Capacity;
            double men = via == "ladders" ? Math.Min(A.Soldiers, opts.Ladders * Q.PerLadder)
                       : via == "tower" ? Math.Min(A.Soldiers, opts.Towers * cap) : A.Soldiers;
            string how = via == "ladders" ? $"по лестницам ({N(opts.Ladders)} шт. × {N(Q.PerLadder)} бойцов)"
                       : via == "tower" ? $"через осадную башню ({N(opts.Towers)} шт. × {N(cap)} бойцов)" : $"в пролом ({N(sec.Breaches * S.BreachM)} м)";
            var L = new List<string> { $"🪜 «{A.Name}» идёт на {name} {how}: в бой вступают {N(men)} из {N(A.Soldiers)} · черновик" };
            var res = new AssaultResult { Ok = true, Title = $"🪜 Приступ: {A.Name} → {name}", Tone = "attack", Lines = L };
            var D = defenders.Count > 0 ? defenders[0].Unit : null;
            if (D == null)
            {
                sec.Holder = A.FactionId.HasValue && A.FactionId.Value != 0 ? A.FactionId.Value : 0;
                L.Add($"Защитников на стене нет — участок №{sec.Id} занят");
                res.Patches.Add(new UnitPatch(A.Id, new Patch { AttacksMade = A.AttacksMade + 1, Acted = true }));
                res.Ladders = via == "ladders" ? opts.Ladders : (double?)null;
                res.Captured = true;
                return res;
            }
            L.Add($"Участок держит «{D.Name}» ({N(Js.Round(defenders[0].Gap))} м от стены)");
            var A2 = A.Clone(); A2.Soldiers = men;
            bool up = via == "ladders";
            var ab = up ? new StrikeMod { Mult = H.UphillMelee, Note = "🪜 Снизу вверх по лестницам" } : null;
            var ba = up ? new StrikeMod { Mult = H.DownhillMelee, Note = "🏰 Со стены сверху вниз" } : null;
            L.Add($"—— Приступ: {A.Name} → {D.Name} ——");
            var resD = Combat.Strike(A2, D, new BattleRequest { Mode = Modes.MeleeRough, SitPct = 0, FatigueMode = opts.FatigueMode, RollCap = men }, L, ctx, false, 1, "", "front", ab);

            bool counter = true; string why = "";
            if (D.Morale == 0) { counter = false; why = $"«{D.Name}» сломлен (БД на нуле) — ответного удара нет"; }
            else if (D.Morale < R.Morale.ShakenBelow) { counter = false; why = $"«{D.Name}» дрогнул (БД ниже {N(R.Morale.ShakenBelow)}) — ответного удара нет"; }
            double cl = Units.CounterLimit(D, R);
            if (counter && D.CountersMade >= cl) { counter = false; why = $"«{D.Name}» уже израсходовал ответные удары в этом ходу ({N(D.CountersMade)}/{N(cl)})"; }
            var resA = new StrikeResult();
            if (counter)
            {
                L.Add($"—— Ответ со стены: {D.Name} → {A.Name} (по численности до потерь) ——");
                resA = Combat.Strike(D, A2, new BattleRequest { Mode = Modes.MeleeRough, SitPct = 0, FatigueMode = opts.FatigueMode }, L, ctx, false, 1, "", "front", ba);
            }
            else { L.Add("—— Без ответного удара ——"); L.Add(why); }

            double? ladders = via == "ladders" ? opts.Ladders : (double?)null;
            if (up && counter)
            {
                double used = Math.Min(opts.Ladders, Math.Ceiling(men / Q.PerLadder));
                var rolls = new List<string>();
                double thrown = 0;
                for (int k = 0; k < used; k++) { double r = Dice.Roll(ctx.Rng, 100); rolls.Add(N(r)); if (r <= Q.PushPct) thrown++; }
                L.Add($"Сброс лестниц (d100 ≤ {N(Q.PushPct)}): {string.Join(", ", rolls)} → сброшено {N(thrown)} из {N(used)} · черновик");
                ladders -= thrown;
                double fallen = Math.Min(Math.Max(0, A.Soldiers - resA.Casualties), thrown * Q.FallMen);
                if (fallen > 0)
                {
                    resA = new StrikeResult { Casualties = resA.Casualties + fallen, Killed = resA.Killed, Wounded = resA.Wounded + fallen };
                    L.Add($"Со сброшенных лестниц упали {N(fallen)} — выбыли ранеными");
                }
            }
            var pd = Combat.CasualtyPatch(D, resD, L, ctx, out _);
            if (counter) pd.CountersMade = D.CountersMade + 1;
            var pA = Combat.CasualtyPatch(A, resA, L, ctx, out _);
            pA.AttacksMade = A.AttacksMade + 1; pA.Acted = true;
            if (pd.Status == "destroyed") L.Add($"На участке №{sec.Id} не осталось «{D.Name}» — если других защитников нет, следующий приступ его займёт");
            res.Patches.Add(new UnitPatch(D.Id, pd));
            res.Patches.Add(new UnitPatch(A.Id, pA));
            res.Ladders = ladders;
            return res;
        }
    }
}
