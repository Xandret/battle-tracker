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
        public int Id, Faction; public string Name, Tpl, Type, Color;   // Color — #rrggbb (сохранение трекера) или null
        public double Men, PerMan, RankDepth, Front, Depth;
        public readonly List<double[]> Figs = new List<double[]>();   // по номеру тела: [ширина, глубина, бойцов, ряд]
    }
    public struct DeadRec { public float X, Y, Facing, Dir; public int Frame, Unit, Part, Man; }   // часть: 0 голова, 1 корпус, 2 ноги, 3 конь; Man — номер бойца (0 — неизвестен)
    // бойцы отряда в кадре (Г75): по номеру бойца — x, y, курс°; NaN — нет (пал, ушёл, ещё не было); фигурка и ряд
    public sealed class MenFrame { public float[] Xyh; public short[] Fig; public byte[] Row; }
    public struct ArrowRec { public float T0, X0, Y0, Z0, VX, VY, VZ, T1, X1, Y1, Z1; public int Unit; public byte End; }

    public sealed class Recording
    {
        public string Name, Note;
        public TerrainMap Map; public double W, H;
        public byte[] Image;                                                                         // картинка карты (земля); null — по клеткам
        public double Dt = 0.2, TurnSec; public int Turns;
        public readonly List<UnitInfo> Units = new List<UnitInfo>();
        // кадр → отряд → [x, y, курс°, скорость нормы, тело₀ x, y, тело₁ x, y, …]; тела нет — NaN
        public readonly List<float[][]> Frames = new List<float[][]>();
        public readonly List<Dictionary<int, float>> Heads = new List<Dictionary<int, float>>();   // кадр → отряд·65536+тело → курс°
        public readonly List<int[]> Fights = new List<int[]>();                                      // кадр → [a, b, a, b, …]
        public readonly List<int[]> Soldiers = new List<int[]>();                                    // кадр → в строю по отрядам
        public readonly List<MenFrame[]> Men = new List<MenFrame[]>();                               // кадр → отряд → бойцы (Г75)
        public List<int>[] States;                                                                   // отряд → [кадр, код, кадр, код, …]
        public readonly List<DeadRec> Dead = new List<DeadRec>();
        public readonly List<ArrowRec> Arrows = new List<ArrowRec>();
        public readonly List<List<string>> Logs = new List<List<string>>();
        public double Seconds => (Frames.Count - 1) * Dt;
        public bool Done;                                                                            // досчитана; иначе дописывается по ходу счёта

        // Где отряд нарисован в момент t: рамка вокруг его бойцов по осям курса (середина, курс°, фронт, глубина, м).
        // Отбившихся одиночек не считаем — 2–98% по каждой оси. Бойцов в записи нет — центр и строй из кадра.
        // Нужна панелям игры: рамка выбора и табличка — там, где отряд виден, а не где он в счёте (счёт идёт впереди показа,
        // а бойцы на скаку отстают от своих мест в строю).
        float[] boxL = new float[64], boxF = new float[64];
        public bool UnitBox(int ui, double t, out float x, out float y, out float facing, out float front, out float depth)
        {
            x = y = facing = front = depth = 0;
            if (Frames.Count == 0 || ui < 0 || ui >= Units.Count) return false;
            double ft = t / Dt; int f0 = Math.Max(0, Math.Min((int)Math.Floor(ft), Frames.Count - 1)), f1 = Math.Min(f0 + 1, Frames.Count - 1);
            float q = (float)Math.Max(0, Math.Min(1, ft - f0));
            var a = Frames[f0][ui]; var b = Frames[f1][ui];
            float dh = (float)(((b[2] - a[2]) % 360 + 540) % 360 - 180);
            x = a[0] + (b[0] - a[0]) * q; y = a[1] + (b[1] - a[1]) * q; facing = a[2] + dh * q;
            front = (float)Units[ui].Front; depth = (float)Units[ui].Depth;
            if (f0 >= Men.Count) return true;
            var m0 = Men[f0][ui]; var m1 = f1 < Men.Count ? Men[f1][ui] : m0;
            double h = facing * Math.PI / 180; float rx = (float)Math.Cos(h), ry = (float)Math.Sin(h), fx = (float)Math.Sin(h), fy = (float)-Math.Cos(h);
            int n = 0, cnt = m0.Xyh.Length / 3;
            if (boxL.Length < cnt) { boxL = new float[cnt]; boxF = new float[cnt]; }
            for (int id = 0; id < cnt; id++)
            {
                float px = m0.Xyh[3 * id];
                if (float.IsNaN(px)) continue;
                float py = m0.Xyh[3 * id + 1];
                if (3 * id + 1 < m1.Xyh.Length && !float.IsNaN(m1.Xyh[3 * id])) { px += (m1.Xyh[3 * id] - px) * q; py += (m1.Xyh[3 * id + 1] - py) * q; }
                float dx = px - x, dy = py - y;
                boxL[n] = dx * rx + dy * ry; boxF[n] = dx * fx + dy * fy; n++;
            }
            if (n == 0) return true;
            Array.Sort(boxL, 0, n); Array.Sort(boxF, 0, n);
            int lo = (int)(n * 0.02), hi = Math.Max(lo, (int)Math.Ceiling(n * 0.98) - 1);
            float l0 = boxL[lo], l1 = boxL[hi], d0 = boxF[lo], d1 = boxF[hi];
            x += rx * (l0 + l1) / 2 + fx * (d0 + d1) / 2; y += ry * (l0 + l1) / 2 + fy * (d0 + d1) / 2;
            front = l1 - l0 + 1.2f; depth = d1 - d0 + 1.6f;   // + место самого бойца
            return true;
        }

        public int StateAt(int ui, int frame)
        {
            var L = States?[ui]; if (L == null) return 0;
            int st = L[1];
            for (int i = 0; i < L.Count && L[i] <= frame; i += 2) st = L[i + 1];
            return st;
        }
    }

    // Снятие кадров боя в запись. Одно на сцену и на живую игру: BattleRecord.Run зовёт Snap раз в 0,2 с счёта, ход в игре
    // (Play/*) — так же, по ходу Battle.Step. Стрелы попадают в запись, когда долетят (порядок не важен — смотрелка
    // ищет летящие по времени). Snap и чтение записи — из одного потока (иначе — под lock(Rec)).
    public sealed class Recorder
    {
        public readonly Recording Rec;
        readonly List<Mover> ms; readonly Battle battle;
        readonly Dictionary<int, int> idx = new Dictionary<int, int>();
        int seenDead, seenArrow;
        readonly List<int> pending = new List<int>();   // стрелы, что ещё летят: номера в ArrowLog

        public Recorder(string name, string note, Geo geo, IList<Mover> movers, Func<Mover, string> tplOf, Battle battle, int turns, Func<Mover, string> colorOf = null)
        {
            var R = Rules.Base;
            ms = movers.ToList(); this.battle = battle;
            if (battle != null && battle.ArrowLog == null) battle.ArrowLog = new List<ArrowTrace>();
            Rec = new Recording { Name = name, Note = note, Map = geo.Map, W = geo.W, H = geo.H, TurnSec = R.Move.TurnSec, Turns = turns };
            for (int i = 0; i < ms.Count; i++)
            {
                var m = ms[i]; var u = m.P.U; idx[u.Id] = i;
                var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                var info = new UnitInfo { Id = u.Id, Faction = u.FactionId ?? 1, Name = u.Name, Tpl = tplOf(m), Type = u.Type, Men = u.Soldiers, Color = colorOf?.Invoke(m),
                    PerMan = f.PerMan, RankDepth = f.RankDepth, Front = m.P.Fp.Front, Depth = m.P.Fp.Depth };
                foreach (var fig in m.P.Figs) info.Figs.Add(new[] { fig.Width, fig.Depth, fig.Men, fig.Rank });
                Rec.Units.Add(info);
            }
            Rec.States = ms.Select(m => new List<int> { 0, State(m) }).ToArray();
        }
        static int State(Mover m) => m.Gone ? 2 : m.Fleeing ? (m.RallyPending ? 3 : 1) : m.Rallied ? 4 : 0;

        public void Snap()
        {
            var rec = Rec;
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
            rec.Men.Add(ms.Select(m =>
            {
                int n = m.NextManId + 1;
                var mf = new MenFrame { Xyh = new float[3 * n], Fig = new short[n], Row = new byte[n] };
                for (int k = 0; k < mf.Xyh.Length; k++) mf.Xyh[k] = float.NaN;
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Id >= n) continue;
                    mf.Xyh[3 * man.Id] = (float)man.X; mf.Xyh[3 * man.Id + 1] = (float)man.Y; mf.Xyh[3 * man.Id + 2] = (float)man.Facing;
                    mf.Fig[man.Id] = (short)(man.Fig?.Id ?? 0); mf.Row[man.Id] = (byte)Math.Min(255, man.Row);
                }
                return mf;
            }).ToArray());
            int fr = rec.Frames.Count - 1;
            if (battle == null) { rec.Fights.Add(Array.Empty<int>()); return; }
            rec.Fights.Add(battle.Fights.Where(f => !f.Over && f.Touching).SelectMany(f => new[] { idx[f.A.P.U.Id], idx[f.B.P.U.Id] }).ToArray());
            for (; seenDead < battle.Deaths.Count; seenDead++)
            {
                var d = battle.Deaths[seenDead];
                int part = d.Part == "head" ? 0 : d.Part == "legs" ? 2 : d.Part == "horse" ? 3 : 1;
                rec.Dead.Add(new DeadRec { X = (float)d.X, Y = (float)d.Y, Frame = fr, Unit = idx[d.UnitId], Facing = (float)d.Facing, Dir = (float)d.Dir, Part = part, Man = d.ManId });
            }
            for (int i = 0; i < ms.Count; i++) if (rec.States[i][rec.States[i].Count - 1] != State(ms[i])) { rec.States[i].Add(fr); rec.States[i].Add(State(ms[i])); }
            // стрелы: новые — в ожидание, долетевшие — в запись
            var log = battle.ArrowLog;
            for (; seenArrow < log.Count; seenArrow++) pending.Add(seenArrow);
            for (int q = pending.Count - 1; q >= 0; q--)
            {
                var a = log[pending[q]];
                if (!(a.T1 > a.T0)) continue;
                rec.Arrows.Add(new ArrowRec { T0 = (float)a.T0, X0 = (float)a.X0, Y0 = (float)a.Y0, Z0 = (float)a.Z0, VX = (float)a.VX, VY = (float)a.VY, VZ = (float)a.VZ,
                    T1 = (float)a.T1, X1 = (float)a.X1, Y1 = (float)a.Y1, Z1 = (float)a.Z1, Unit = idx[a.UnitId], End = a.End });
                pending.RemoveAt(q);
            }
        }
    }

    public static class BattleRecord
    {
        // Сцена целиком: все ходы движком, кадр раз в 0,2 с; запись готова (Done), когда досчитана
        public static Recording Run(SceneDef sc, Action<string> progress = null)
        {
            var R = Rules.Base;
            var ms = sc.Units.Select(u => u.M).ToList();
            if (sc.Battle == null) foreach (var (m, o) in sc.Units) if (o != null) MoveSim.Give(m, o, sc.Geo, R);
            var rc = new Recorder(sc.Name, sc.Note, sc.Geo, ms, m => sc.Tpl[m], sc.Battle, sc.Turns, m => sc.Color.TryGetValue(m, out var c) ? c : null);
            rc.Rec.Image = sc.Image;
            rc.Snap();
            for (int turn = 0; turn < sc.Turns; turn++)
            {
                progress?.Invoke($"ход {turn + 1} из {sc.Turns}");
                sc.Before?.Invoke(turn);
                int k = 0;
                Action<double> step = t => { if (++k % 4 == 0) rc.Snap(); };   // шаг движка 0,05 с — кадр раз в 0,2 с
                rc.Rec.Logs.Add(sc.Battle != null ? sc.Battle.Turn(step) : MoveSim.Turn(ms, sc.Geo, R, step));
            }
            rc.Rec.Done = true;
            return rc.Rec;
        }
    }
}
