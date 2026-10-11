// ═══════════ TurnSummary.cs — короткая сводка хода (И2): потери сторон и главные события ═══════════
// Вместо сырого журнала движка: что было в начале хода (снимок на «Ход!») и что стало в конце — по данным, а не по
// тексту журнала. Потери стороны — убито и ранено (Unit.TotKilled / TotWounded за ход). События с весом — в сводку
// идут самые важные: уничтожен, ушёл, бежит, сплотился, натиск (вышел или нет и почему), рукопашная, обстрел,
// тяжёлые потери, дрогнул. Сырой журнал остаётся — под кнопкой «Подробно».
using System.Collections.Generic;
using System.Linq;
using BattleCore;

namespace Journal.Play
{
    public sealed class SummaryEvent
    {
        public int Weight, Side; public string Icon, Text; public Mover Unit;
    }

    public sealed class TurnSummary
    {
        public int Turn;
        public readonly Dictionary<int, (double lost, double killed, double wounded, double now)> Sides = new Dictionary<int, (double, double, double, double)>();
        public readonly List<SummaryEvent> Events = new List<SummaryEvent>();   // по важности, самые важные — первыми
        public long Arrows, ArrowHits;

        // снимок отряда на начале хода
        sealed class Snap { public double Soldiers, Killed, Wounded, Morale; public bool Fleeing, Gone, Rallied, Out; }
        readonly Dictionary<Mover, Snap> start = new Dictionary<Mover, Snap>();

        public static TurnSummary Begin(Battle b, int turn)
        {
            var s = new TurnSummary { Turn = turn };
            foreach (var m in b.Movers)
            {
                var u = m.P.U;
                s.start[m] = new Snap { Soldiers = u.Soldiers, Killed = u.TotKilled, Wounded = u.TotWounded, Morale = u.Morale,
                    Fleeing = m.Fleeing, Gone = m.Gone, Rallied = m.Rallied, Out = Out(m) };
            }
            return s;
        }
        static bool Out(Mover m) => m.P.U.Status == "destroyed" || m.P.U.Soldiers <= 0;
        static string N(Mover m) => $"«{m.P.U.Name}»";

        // конец хода: сравнить со снимком; схватки, натиски и залпы — из данных боя за этот ход
        public void End(Battle b)
        {
            Events.Clear(); Sides.Clear();
            foreach (var m in b.Movers)
            {
                if (!start.TryGetValue(m, out var s0)) continue;
                var u = m.P.U; int side = BattleSession.SideOf(m);
                Sides.TryGetValue(side, out var sd);
                double lost = System.Math.Max(0, s0.Soldiers - u.Soldiers);
                bool inLine = !Out(m) && !m.Gone && !m.Fleeing;
                Sides[side] = (sd.lost + lost, sd.killed + System.Math.Max(0, u.TotKilled - s0.Killed), sd.wounded + System.Math.Max(0, u.TotWounded - s0.Wounded), sd.now + (inLine ? u.Soldiers : 0));
                if (Out(m) && !s0.Out) Add(100, side, "broken", $"{N(m)} уничтожен", m);
                else if (m.Gone && !s0.Gone) Add(90, side, "gone", $"{N(m)} ушёл с поля", m);
                else if (m.Fleeing && !s0.Fleeing) Add(85, side, "flee", $"{N(m)} бежит" + (lost >= 1 ? $" (−{lost:0})" : ""), m);
                else if (m.Rallied && !s0.Rallied || !m.Fleeing && s0.Fleeing && !m.Gone) Add(70, side, "rally", $"{N(m)} сплотился", m);
                else if (!m.Fleeing && s0.Soldiers > 0 && lost / s0.Soldiers >= 0.15) Add(45 + (int)(20 * lost / s0.Soldiers), side, "fight", $"{N(m)} потерял {lost:0} ({100 * lost / s0.Soldiers:0}%)", m);
                // боевой дух упал на ступень ниже «держатся» — дрогнул
                if (!m.Fleeing && !Out(m) && Units.MoraleStage(u.Morale).Label != Units.MoraleStage(s0.Morale).Label && u.Morale < s0.Morale && u.Morale < 40)
                    Add(25, side, "broken", $"{N(m)}: {Units.MoraleStage(u.Morale).Label.ToLowerInvariant()}", m);
            }
            foreach (var f in b.Fights)
            {
                if (f.LossA == 0 && f.LossB == 0 && f.Notes.Count == 0) continue;
                int sa = BattleSession.SideOf(f.A);
                var charge = f.Notes.FirstOrDefault(n => n.StartsWith("натиск «"));
                var noCharge = f.Notes.FirstOrDefault(n => n.Contains("без натиска"));
                if (charge != null) Add(60, sa, "charge", $"Натиск {N(f.A)} на {N(f.B)}: −{f.LossB:0}", f.A);
                else if (noCharge != null) Add(40, sa, "charge", $"{N(f.A)} — натиск не вышел: {noCharge.Substring(noCharge.IndexOf(':') + 1).Trim()}", f.A);
                bool fresh = f.T0 >= TurnStart(b);   // сошлись в этом ходу
                Add(fresh ? 50 : 30 + (int)System.Math.Min(15, (f.LossA + f.LossB) / 20), sa, "fight",
                    (fresh ? $"{N(f.A)} и {N(f.B)} сошлись" : $"Рубка {N(f.A)} и {N(f.B)}") + $": −{f.LossA:0} / −{f.LossB:0}", f.A);
            }
            foreach (var v in b.Volleys)
            {
                if (v.Arrows <= 0) continue;
                Add(32 + (int)System.Math.Min(10, v.LossB / 10), BattleSession.SideOf(v.A), "archer", $"{N(v.A)} по {N(v.B)}: {v.Arrows} стрел, −{v.LossB:0}", v.A);
            }
            Arrows = b.Shots.Arrows; ArrowHits = b.Shots.Out;
            Events.Sort((x, y) => y.Weight.CompareTo(x.Weight));
        }
        // начало этого хода по часам боя: конец хода минус длина хода
        static double TurnStart(Battle b) => b.Clock - b.R.Move.TurnSec;
        void Add(int w, int side, string icon, string text, Mover m) => Events.Add(new SummaryEvent { Weight = w, Side = side, Icon = icon, Text = text, Unit = m });
    }
}
