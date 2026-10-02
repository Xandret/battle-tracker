// ═══════════ BattleRecord.cs — бой, посчитанный движком в Unity, кадр за кадром (как полигон, Г55) ═══════════
// Run берёт сцену, отыгрывает её ходы движком (Battle.Turn или MoveSim.Turn) и раз в 0,2 с снимает кадр:
// центр, курс и тела каждого отряда, курсы развёрнутых тел (охват, бегство), павших, полёты стрел, пары в рукопашной,
// состояние отряда (бежит, ушёл, сплотился). Всё — данные для рисунка, правила не трогаются.
// Работает без UnityEngine — его можно звать из фонового потока, пока на экране идёт прежняя сцена.
using System;
using System.Collections.Generic;
using System.Linq;
using BattleCore;

namespace Journal.Viewer
{
    public sealed class UnitInfo
    {
        public int Id, Faction; public string Name, Tpl, Type;
        public double Men, PerMan, RankDepth, Front, Depth;
        public readonly List<double[]> Figs = new List<double[]>();   // по номеру тела: [ширина, глубина, бойцов, ряд]
    }
    public struct DeadRec { public float X, Y, Facing, Dir; public int Frame, Unit, Part; }   // часть: 0 голова, 1 корпус, 2 ноги, 3 конь
    public struct ArrowRec { public float T0, X0, Y0, Z0, VX, VY, VZ, T1, X1, Y1, Z1; public int Unit; public byte End; }

    public sealed class Recording
    {
        public string Name, Note;
        public TerrainMap Map; public double W, H;
        public double Dt = 0.2, TurnSec; public int Turns;
        public readonly List<UnitInfo> Units = new List<UnitInfo>();
        // кадр → отряд → [x, y, курс°, скорость нормы, тело₀ x, y, тело₁ x, y, …]; тела нет — NaN
        public readonly List<float[][]> Frames = new List<float[][]>();
        public readonly List<Dictionary<int, float>> Heads = new List<Dictionary<int, float>>();   // кадр → отряд·65536+тело → курс°
        public readonly List<int[]> Fights = new List<int[]>();                                      // кадр → [a, b, a, b, …]
        public readonly List<int[]> Soldiers = new List<int[]>();                                    // кадр → в строю по отрядам
        public List<int>[] States;                                                                   // отряд → [кадр, код, кадр, код, …]
        public readonly List<DeadRec> Dead = new List<DeadRec>();
        public readonly List<ArrowRec> Arrows = new List<ArrowRec>();
        public readonly List<List<string>> Logs = new List<List<string>>();
        public double Seconds => (Frames.Count - 1) * Dt;

        public int StateAt(int ui, int frame)
        {
            var L = States?[ui]; if (L == null) return 0;
            int st = L[1];
            for (int i = 0; i < L.Count && L[i] <= frame; i += 2) st = L[i + 1];
            return st;
        }
    }

    public static class BattleRecord
    {
        public static Recording Run(SceneDef sc, Action<string> progress = null)
        {
            var R = Rules.Base;
            var ms = sc.Units.Select(u => u.M).ToList();
            if (sc.Battle == null) foreach (var (m, o) in sc.Units) if (o != null) MoveSim.Give(m, o, sc.Geo, R);
            if (sc.Battle != null) sc.Battle.ArrowLog = new List<ArrowTrace>();
            var rec = new Recording { Name = sc.Name, Note = sc.Note, Map = sc.Geo.Map, W = sc.Geo.W, H = sc.Geo.H, TurnSec = R.Move.TurnSec, Turns = sc.Turns };
            var idx = new Dictionary<int, int>();
            for (int i = 0; i < ms.Count; i++)
            {
                var m = ms[i]; var u = m.P.U; idx[u.Id] = i;
                var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                var info = new UnitInfo { Id = u.Id, Faction = u.FactionId ?? 1, Name = u.Name, Tpl = sc.Tpl[m], Type = u.Type, Men = u.Soldiers,
                    PerMan = f.PerMan, RankDepth = f.RankDepth, Front = m.P.Fp.Front, Depth = m.P.Fp.Depth };
                foreach (var fig in m.P.Figs) info.Figs.Add(new[] { fig.Width, fig.Depth, fig.Men, fig.Rank });
                rec.Units.Add(info);
            }
            int State(Mover m) => m.Gone ? 2 : m.Fleeing ? (m.RallyPending ? 3 : 1) : m.Rallied ? 4 : 0;
            rec.States = ms.Select(m => new List<int> { 0, State(m) }).ToArray();
            int seenDead = 0;
            void Snap()
            {
                rec.Frames.Add(ms.Select(m =>
                {
                    var a = new float[4 + 2 * m.NextFigId];
                    for (int k = 4; k < a.Length; k++) a[k] = float.NaN;
                    a[0] = (float)m.P.X; a[1] = (float)m.P.Y; a[2] = (float)m.P.Facing; a[3] = (float)m.Vs;
                    foreach (var s in m.Figs) { a[4 + 2 * s.Id] = (float)s.X; a[5 + 2 * s.Id] = (float)s.Y; }
                    return a;
                }).ToArray());
                var heads = new Dictionary<int, float>();
                for (int i = 0; i < ms.Count; i++)
                    foreach (var s in ms[i].Figs)
                    {
                        if (!s.Turned) continue;
                        double want = s.Wrap ? s.WH : ms[i].Fleeing && !double.IsNaN(s.FleeH) ? s.FleeH : ms[i].P.Facing, a = s.Axis;
                        if (Math.Abs(MoveSim.AngleDiff(a, want)) > 90) a += 180;
                        heads[i * 65536 + s.Id] = (float)MoveSim.Norm(a);
                    }
                rec.Heads.Add(heads);
                rec.Soldiers.Add(ms.Select(m => (int)Math.Round(m.P.U.Soldiers)).ToArray());
                int fr = rec.Frames.Count - 1;
                if (sc.Battle != null)
                {
                    rec.Fights.Add(sc.Battle.Fights.Where(f => !f.Over && f.Touching).SelectMany(f => new[] { idx[f.A.P.U.Id], idx[f.B.P.U.Id] }).ToArray());
                    for (; seenDead < sc.Battle.Deaths.Count; seenDead++)
                    {
                        var d = sc.Battle.Deaths[seenDead];
                        int part = d.Part == "head" ? 0 : d.Part == "legs" ? 2 : d.Part == "horse" ? 3 : 1;
                        rec.Dead.Add(new DeadRec { X = (float)d.X, Y = (float)d.Y, Frame = fr, Unit = idx[d.UnitId], Facing = (float)d.Facing, Dir = (float)d.Dir, Part = part });
                    }
                    for (int i = 0; i < ms.Count; i++) if (rec.States[i][rec.States[i].Count - 1] != State(ms[i])) { rec.States[i].Add(fr); rec.States[i].Add(State(ms[i])); }
                }
                else rec.Fights.Add(Array.Empty<int>());
            }
            Snap();
            for (int turn = 0; turn < sc.Turns; turn++)
            {
                progress?.Invoke($"ход {turn + 1} из {sc.Turns}");
                sc.Before?.Invoke(turn);
                int k = 0;
                Action<double> step = t => { if (++k % 4 == 0) Snap(); };   // шаг движка 0,05 с — кадр раз в 0,2 с
                rec.Logs.Add(sc.Battle != null ? sc.Battle.Turn(step) : MoveSim.Turn(ms, sc.Geo, R, step));
            }
            if (sc.Battle != null)
                foreach (var a in sc.Battle.ArrowLog.Where(a => a.T1 > a.T0).OrderBy(a => a.T0))
                    rec.Arrows.Add(new ArrowRec { T0 = (float)a.T0, X0 = (float)a.X0, Y0 = (float)a.Y0, Z0 = (float)a.Z0, VX = (float)a.VX, VY = (float)a.VY, VZ = (float)a.VZ,
                        T1 = (float)a.T1, X1 = (float)a.X1, Y1 = (float)a.Y1, Z1 = (float)a.Z1, Unit = idx[a.UnitId], End = a.End });
            return rec;
        }
    }
}
