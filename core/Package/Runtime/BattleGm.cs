// ═══════════ BattleGm.cs — инструменты ГМа (Г112): численность, убрать и добавить отряд, модификаторы БД ═══════════
// Правки ГМа не считаются боем: павших не пишут, проверок не зовут; в журнал хода — строка.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed partial class Battle
    {
        // Численность отряда — рукой ГМа: бойцы раскладываются заново по новой численности (меньше — лишние убираются из задних
        // рядов без павших; больше — строй собирается заново на месте). 0 и меньше — отряд убирается с поля. false — отряда нет
        public bool SetSoldiers(Mover m, double n)
        {
            if (m == null || m.Gone || !Movers.Contains(m)) return false;
            var u = m.P.U; double was = u.Soldiers; n = Math.Max(0, Js.Round(n));
            if (n <= 0) { Remove(m, "численность 0"); return true; }
            if (Math.Abs(n - was) < 0.5) return false;
            u.Soldiers = n; u.Initial = Math.Max(u.Initial, n);
            if (MenMode)
            {
                int alive = m.Men.Count(x => x.Alive);
                if (n < alive)
                {
                    // лишние — с хвоста строя (дальние от фронта), без павших
                    foreach (var x in m.Men.Where(x => x.Alive).OrderByDescending(x => x.Row).ThenByDescending(x => Math.Abs(x.Lx)).Take(alive - (int)n)) x.Alive = false;
                    m.LaidMen = -1; Relayout(m);
                }
                else
                {
                    m.LaidMen = -1; Relayout(m);
                    Soldiers.Assign(m, R, spawn: true);   // прибавить бойцов можно только пересобрав строй на месте
                    foreach (var man in m.Men) man.Z = StandZ(man.X, man.Y);
                }
            }
            else { m.LaidMen = -1; Relayout(m); }
            events.Add($"ГМ: «{u.Name}» — численность {Js.Num(was)} → {Js.Num(n)}");
            return true;
        }

        // Убрать отряд с поля: не бегство и не разгром — просто нет; схватки с ним кончаются, перестрелки по нему и его — тоже
        public bool Remove(Mover m, string why = null)
        {
            if (m == null || m.Gone || !Movers.Contains(m)) return false;
            var u = m.P.U;
            m.Gone = true; m.Fleeing = false; m.RallyPending = false; m.Order = null; m.Track = null; m.Field = null; m.Vs = 0; m.Done = true;
            u.Status = "destroyed"; u.OnMap = false;
            foreach (var f in Fights) if (!f.Over && (f.A == m || f.B == m)) { f.Over = true; f.Touching = false; }
            foreach (var v in Volleys) if (!v.Over && (v.A == m || v.B == m)) v.Over = true;
            foreach (var d in Duels) if (!d.Over && d.Has(m)) Finish(d, null, Clock);
            Challenges.RemoveAll(c => c.from == m || c.to == m);
            aimed.Remove(m);
            foreach (var x in m.Men) { x.Alive = false; x.Foe = null; }
            m.Men.Clear(); m.Figs.Clear(); m.P.Figs.Clear();
            foreach (var o in Movers) foreach (var x in o.Men) if (x.Foe != null && !x.Foe.Alive) x.Foe = null;
            events.Add($"ГМ: «{u.Name}» убран с поля{(why != null ? $" ({why})" : "")}");
            return true;
        }

        // Модификатор БД из таблицы (Rules.MoraleMods) на отряды: БД += значение (0…Morale.Max), строка в журнал. БД упал до нуля — сразу
        // проверка на побег, как после удара (Г74); провал — отряд бежит. Возвращает значение; NaN — ключа нет. t — часы боя для строки
        // (NaN — приказная фаза, часы — текущие)
        public double ApplyMorale(IEnumerable<Mover> units, string key, double t = double.NaN)
        {
            var mod = R.MoraleMods.FirstOrDefault(x => x.key == key);
            if (mod.key == null) return double.NaN;
            var names = new List<string>(); var zero = new List<Mover>();
            foreach (var m in units)
            {
                if (m == null || !OnField(m)) continue;
                var u = m.P.U; double was = u.Morale;
                u.Morale = Math.Max(0, Math.Min(R.Morale.Max, u.Morale + mod.value));
                names.Add($"«{u.Name}» {Js.Num(was)} → {Js.Num(u.Morale)}");
                if (u.Morale <= 0 && was > 0 && u.Status == "active" && !m.Fleeing) zero.Add(m);
            }
            if (names.Count > 0) events.Add($"{(double.IsNaN(t) ? "" : At(t) + " с · ")}{mod.name} ({(mod.value >= 0 ? "+" : "")}{Js.Num(mod.value)} БД): {string.Join(", ", names)}");
            double tc = double.IsNaN(t) ? Clock : t;
            foreach (var m in zero)
            {
                var u = m.P.U;
                Check(tc, MoraleRules.FleeCheck(u, Ctx), u);
                if (u.Status == "fled") { Flee(m, tc, "БД упал до нуля — провалил проверку на побег"); PanicFrom(m, tc); }
            }
            return mod.value;
        }
    }
}
