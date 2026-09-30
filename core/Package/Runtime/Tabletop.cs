// ═══════════ Tabletop.cs — «ход рукопашной за столом»: мишень для поштучной модели (И1, Г9, Г21) ═══════════
// Один ход двух сошедшихся отрядов по правилам настолки: A тратит свои атаки (1 или 2), потом B — свои;
// каждый раз с ответным ударом, если он положен. Между ударами потери и БД меняются — как у ГМа за столом.
// Считается настоящим Combat.ResolveBattle, поэтому это ровно правила трекера, без упрощений.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public enum Ground { Field, Forest, Hill }   // холм: A выше B

    public sealed class TurnSetup
    {
        public Ground Ground = Ground.Field;
        public bool ChargeA;          // A — конница, первая атака с натиском (разбег есть)
        public string SectorA = "front";   // куда A бьёт B: front / flank / rear
    }

    public sealed class TurnOutcome
    {
        public double LossA, LossB;           // потери за ход (убитые + раненые)
        public double MoraleA, MoraleB;       // БД после хода
        public string StatusA, StatusB;
        public int Strikes;                   // сколько ударов было нанесено за ход
        public List<string> Log = new List<string>();
        public List<double[]> Timeline = new List<double[]>();   // поштучная модель: [секунда, бойцов A, бойцов B]
        public double EngagedA, EngagedB;                         // поштучная модель: доля бойцов в бою в начале хода
    }

    public static class Tabletop
    {
        // Г28: в контакте стрелки только рубятся (−50% ко всем параметрам) — мишень рукопашная для всех
        public static string ModeFor(Unit u, Ground g) => g == Ground.Forest ? Modes.MeleeRough : Modes.MeleeForm;

        // Высота (черновик 6а): A на холме — его удары сверху вниз, ответы B — снизу вверх
        public static MapMods Hill(bool attackerIsHigh, Rules r)
        {
            var H = r.Map.Height;
            var m = new MapMods();
            if (attackerIsHigh)
            {
                m.Ab = new StrikeMod { Mult = H.DownhillMelee, Note = "⛰ Удар сверху вниз" };
                m.Ba = new StrikeMod { Mult = H.UphillMelee, Note = "⛰ Ответ снизу вверх" };
            }
            else
            {
                m.Ab = new StrikeMod { Mult = H.UphillMelee, Note = "⛰ Удар снизу вверх" };
                m.Ba = new StrikeMod { Mult = H.DownhillMelee, Note = "⛰ Ответ сверху вниз" };
            }
            return m;
        }

        // Сектор задаём расстановкой на карте: B смотрит вверх (0°), A стоит спереди, сбоку или сзади
        static void Place(Unit a, Unit b, string sector)
        {
            if (sector == "front") { a.OnMap = b.OnMap = false; return; }
            a.OnMap = b.OnMap = true; b.MapX = 50; b.MapY = 50; b.Facing = 0;
            if (sector == "flank") { a.MapX = 60; a.MapY = 50; }
            else { a.MapX = 50; a.MapY = 60; }
        }

        public static TurnOutcome Turn(Unit a0, Unit b0, TurnSetup s, EngineContext ctx)
        {
            var R = ctx.Rules;
            var a = a0.Clone(); var b = b0.Clone();
            Place(a, b, s.SectorA);
            var outc = new TurnOutcome();
            double startA = a.Soldiers, startB = b.Soldiers;

            void Attack(Unit att, Unit def, bool charge, bool attIsA)
            {
                var req = new BattleRequest { Mode = ModeFor(att, s.Ground), Mutual = true, Charge = charge, FatigueMode = "percent" };
                if (s.Ground == Ground.Hill) req.MapMods = Hill(attIsA, R);
                var res = Combat.ResolveBattle(att, def, req, ctx);
                if (!res.Ok) return;
                outc.Strikes++;
                foreach (var p in res.Patches) p.Patch.ApplyTo(p.Id == a.Id ? a : b);
                outc.Log.Add(res.Title);
            }

            double limA = Units.AttackLimit(a, R);
            for (int k = 0; k < limA; k++) Attack(a, b, s.ChargeA && k == 0, true);
            // B отвечает на своём действии; удар во фланг и тыл B разворачивает не сразу — отвечает во фронт
            if (s.SectorA != "front") { a.OnMap = b.OnMap = false; }
            double limB = Units.AttackLimit(b, R);
            for (int k = 0; k < limB; k++) Attack(b, a, false, false);

            outc.LossA = startA - a.Soldiers; outc.LossB = startB - b.Soldiers;
            outc.MoraleA = a.Morale; outc.MoraleB = b.Morale;
            outc.StatusA = a.Status; outc.StatusB = b.Status;
            return outc;
        }
    }

    // Сводка по многим ходам: среднее, разброс, перцентили — ровно то, с чем будет сравниваться модель (Г21)
    public sealed class Stat
    {
        public double Mean, Sd, P10, P50, P90;
        public static Stat Of(IEnumerable<double> xs)
        {
            var v = xs.OrderBy(x => x).ToArray();
            if (v.Length == 0) return new Stat();
            double mean = v.Average();
            double sd = Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, v.Length - 1));
            double Q(double p) => v[Math.Min(v.Length - 1, (int)Math.Floor(p * (v.Length - 1) + 0.5))];
            return new Stat { Mean = mean, Sd = sd, P10 = Q(0.1), P50 = Q(0.5), P90 = Q(0.9) };
        }
    }
}
