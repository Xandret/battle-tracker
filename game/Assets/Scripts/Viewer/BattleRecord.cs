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
        public int Id, Faction, Commander; public string Name, Tpl, Type, Color, Style;   // Commander — номер полководца (0 — нет)   // Color — #rrggbb (сохранение трекера) или null; Style — стиль облика (В16), null — западный
        public double Men, PerMan, RankDepth, Front, Depth;
        public readonly List<double[]> Figs = new List<double[]>();   // по номеру тела: [ширина, глубина, бойцов, ряд]
    }
    // павший: часть 0 голова, 1 корпус, 2 ноги, 3 конь; Man — номер бойца (0 — неизвестен); Killed — убит (иначе ранен, Г39, В13);
    // T — когда пал (часы боя; Б2 — в миг удара), Frame — первый кадр, где его уже нет в строю
    public struct DeadRec { public float X, Y, Facing, Dir, T; public int Frame, Unit, Part, Man; public bool Killed; }
    // бойцы отряда в кадре (Г75): по номеру бойца — x, y, курс°; NaN — нет (пал, ушёл, ещё не было); фигурка и ряд;
    // Ph — фаза шага (В13): круги шага, набранные по пройденному пути — ноги не скользят при смене скорости
    // Рукопашная по бойцам (Б2, только при MenBodies): кто в схватке — Eng (номера бойцов), прошлый удар EngSw, следующий по ритму EngNx, удар на щит EngPa (часы боя; NaN — не было).
    // Только сцепившиеся — запись не растёт на всё войско
    // Сбитые с ног конём (Г90): Down — номера бойцов, DownAt — когда сбит, DownEnd — когда встанет (часы боя); только лежащие
    // EngFoe — его противник (парный поединок): номер отряда в записи × 2^20 + номер бойца; −1 — нет
    // Z — высота бойца над землёй (Г104: на стене 9 м, на башне 11); null — все на земле
    // Cmd — тело полководца (Г108, движок: Mover.CommanderMan), −1 — нет; Guard — тела стражи
    public sealed class MenFrame { public float[] Xyh, Ph; public short[] Fig; public byte[] Row; public int[] Eng, EngFoe; public float[] EngSw, EngNx, EngPa; public int[] Down; public float[] DownAt, DownEnd; public float[] Z; public int Cmd = -1; public int[] Guard; }
    // поединок полководцев (Г108): отряды (номера в записи), круг, когда шёл, удары (время, номер отряда бьющего, попал), итог
    public sealed class DuelRec
    {
        public int A, B, Winner = -1; public string NameA, NameB; public double ValorA, ValorB;
        public float X, Y, R, T0, StartT = float.NaN, EndT = float.NaN; public bool LoserKilled, Fighting, Over; public int WoundsA, WoundsB;
        public readonly List<(float t, int unit, bool hit)> Strikes = new List<(float, int, bool)>();
    }
    public struct ArrowRec { public float T0, X0, Y0, Z0, VX, VY, VZ, T1, X1, Y1, Z1; public int Unit; public byte End; }
    // ворота (Г104): середина группы клеток ворот, м; St — [кадр, состояние, …]: 0 — закрыты, 1 — открыты (или стены ничьи —
    // проход всем), 2 — в проходе свои (створки распахнуты, пока проходят; бит к «открыты»), 4 — выбиты (Г105: пролом);
    // Hp — [кадр, осталось, всего, …] прочности (Г105: враг рубит закрытые ворота), только при смене; пусто — не рубили
    public sealed class GateRec { public float X, Y; public readonly List<int> St = new List<int>(); public readonly List<float> Hp = new List<float>(); }

    public sealed class Recording
    {
        public string Name, Note;
        public TerrainMap Map; public double W, H;
        public byte[] Image;                                                                         // картинка карты (земля); null — по клеткам
        public double Dt = 0.2, TurnSec; public int Turns;
        public readonly List<UnitInfo> Units = new List<UnitInfo>();
        // кадр → отряд → [x, y, курс°, скорость нормы, тело₀ x, y, тело₁ x, y, …]; тела нет — NaN
        public readonly List<float[][]> Frames = new List<float[][]>();
        public readonly List<float[][]> Boxes = new List<float[][]>();                               // Г111 п.6: кадр → отряд → [фронт, глубина] строя сейчас (после потерь и перестроений), м
        public readonly List<Dictionary<int, float>> Heads = new List<Dictionary<int, float>>();   // кадр → отряд·65536+тело → курс°
        public readonly List<int[]> Fights = new List<int[]>();                                      // кадр → [a, b, a, b, …]
        public readonly List<int[]> Soldiers = new List<int[]>();                                    // кадр → в строю по отрядам
        public readonly List<MenFrame[]> Men = new List<MenFrame[]>();                               // кадр → отряд → бойцы (Г75)
        public List<int>[] States;                                                                   // отряд → [кадр, код, кадр, код, …]
        public readonly List<GateRec> Gates = new List<GateRec>();                                   // ворота карты (Г104)
        public readonly List<DuelRec> Duels = new List<DuelRec>();                                   // поединки полководцев (Г108)
        public readonly Dictionary<long, int> Charges = new Dictionary<long, int>();                 // пара отрядов (меньший << 32 | больший) → кто бил натиском
        public readonly Dictionary<int, bool[]> FogCells = new Dictionary<int, bool[]>();            // туман (Г107): сторона → видимые клетки, последние снятые
        // туман войны (Г18, Алекс 10.10.2026: «переключатель вида»): кадр → отряд → какие стороны его видят (бит 1 << сторона);
        // пусто — видимости в записи нет, видно всем
        public readonly List<byte[]> Seen = new List<byte[]>();
        // виден ли отряд стороне side в кадре: 0 — ГМ (видит всё); свои видны всегда
        public bool Visible(int ui, int frame, int side)
        {
            if (side <= 0 || ui < 0 || ui >= Units.Count || Units[ui].Faction == side || Seen.Count == 0) return true;
            var s = Seen[Math.Max(0, Math.Min(frame, Seen.Count - 1))];
            return s == null || ui >= s.Length || (s[ui] & (1 << side)) != 0;
        }
        public readonly List<DeadRec> Dead = new List<DeadRec>();
        public readonly List<ArrowRec> Arrows = new List<ArrowRec>();
        public readonly List<List<string>> Logs = new List<List<string>>();
        public double Seconds => (Frames.Count - 1) * Dt;
        public int Rewinds;                                                                          // сколько раз запись укорачивали (откат, приказы посреди хода) — кэшам смотрелки начать заново
        public bool Done;                                                                            // досчитана; иначе дописывается по ходу счёта
        public bool MenMelee;                                                                        // рукопашная по бойцам (Б2): удары — из движка

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
            if (f0 < Boxes.Count && ui < Boxes[f0].Length) { front = Boxes[f0][ui][0]; depth = Boxes[f0][ui][1]; }   // Г111 п.6: рамка этого кадра, не начала боя
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

        // прочность ворот к кадру: false — целы (не рубили)
        public bool GateHpAt(int g, int frame, out float hp, out float max)
        {
            var L = Gates[g].Hp; hp = max = 0; bool any = false;
            for (int i = 0; i < L.Count && L[i] <= frame; i += 3) { hp = L[i + 1]; max = L[i + 2]; any = true; }
            return any;
        }
        public int GateAt(int g, int frame)
        {
            var L = Gates[g].St; int st = 0;
            for (int i = 0; i < L.Count && L[i] <= frame; i += 2) st = L[i + 1];
            return st;
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
        readonly List<(int log, int rec)> pending = new List<(int, int)>();   // стрелы в полёте: номер в ArrowLog → в записи
        // фаза шага бойцов (В13): где боец был в прошлом кадре и сколько кругов шага набрал
        readonly Dictionary<Mover, (float[] xy, float[] ph)> gait = new Dictionary<Mover, (float[], float[])>();

        public Recorder(string name, string note, Geo geo, IList<Mover> movers, Func<Mover, string> tplOf, Battle battle, int turns, Func<Mover, string> colorOf = null, Func<Mover, string> styleOf = null)
        {
            var R = Rules.Base;
            ms = movers.ToList(); this.battle = battle;
            if (battle != null && battle.ArrowLog == null) battle.ArrowLog = new List<ArrowTrace>();
            Rec = new Recording { Name = name, Note = note, Map = geo.Map, W = geo.W, H = geo.H, TurnSec = R.Move.TurnSec, Turns = turns,
                MenMelee = battle != null && battle.R.Move.MenBodies };
            for (int i = 0; i < ms.Count; i++)
            {
                var m = ms[i]; var u = m.P.U; idx[u.Id] = i;
                var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
                var info = new UnitInfo { Id = u.Id, Faction = u.FactionId ?? 1, Commander = u.CommanderId ?? 0, Name = u.Name, Tpl = tplOf(m), Type = u.Type, Men = u.Soldiers, Color = colorOf?.Invoke(m), Style = styleOf?.Invoke(m),
                    PerMan = f.PerMan, RankDepth = f.RankDepth, Front = m.P.Fp.Front, Depth = m.P.Fp.Depth };
                foreach (var fig in m.P.Figs) info.Figs.Add(new[] { fig.Width, fig.Depth, fig.Men, fig.Rank });
                Rec.Units.Add(info);
            }
            Rec.States = ms.Select(m => new List<int> { 0, State(m) }).ToArray();
            sides = ms.Select(m => m.P.U.FactionId ?? 1).Where(f => f > 0 && f < 8).Distinct().ToArray();
            if (battle != null && geo.Map != null) FindGates(geo.Map);
        }

        // ── ворота (Г104): связные группы клеток «gate» карты; открыты ли — у движка (Battle.GateOpen), раз в 5 с и по RefreshGates;
        // свои в проходе — каждый кадр (по клетке бойца) ──
        int[] gateOf; bool[] gateOpen, gateBroken;
        // кто кого видит (Г18): сторона, отряд → виден ли; задаёт игра из движка; null — видимость не пишется
        public Func<int, Mover, bool> Sees;
        int[] sides;   // стороны на поле (1…7) — для видимости

        // ── Г112: инструменты ГМа и откат хода ──
        // последний кадр — заново (правка ГМа между ходами: численность, убран или добавлен отряд): время записи не сдвигается
        public void ResnapLast()
        {
            var rec = Rec; int n = rec.Frames.Count;
            if (n > 0)
            {
                rec.Frames.RemoveAt(n - 1); rec.Heads.RemoveAt(n - 1); rec.Soldiers.RemoveAt(n - 1); rec.Men.RemoveAt(n - 1);
                if (rec.Fights.Count == n) rec.Fights.RemoveAt(n - 1);
                if (rec.Seen.Count == n) rec.Seen.RemoveAt(n - 1);
            }
            Snap();
        }
        // отряды боя — новые объекты (Battle.Restore: откат хода, отмена правки ГМа): перепривязать по номеру отряда; кого в бою
        // больше нет (отменили «добавить») — прежний объект помечается ушедшим
        public void Rebind(IList<Mover> movers)
        {
            var byId = movers.ToDictionary(m => m.P.U.Id);
            var oldGait = gait.ToList(); gait.Clear();
            for (int i = 0; i < ms.Count; i++)
            {
                var old = ms[i];
                if (byId.TryGetValue(Rec.Units[i].Id, out var nm)) ms[i] = nm; else old.Gone = true;
                foreach (var kv in oldGait) if (kv.Key == old) gait[ms[i]] = kv.Value;
            }
        }
        // подкрепление посреди битвы (Г112 п.5): отряд — в запись; в прежних кадрах его нет (заглушка на месте появления, состояние «ушёл»)
        public void AddUnit(Mover m, string tpl, string color, string style)
        {
            var R = Rules.Base; var rec = Rec; var u = m.P.U;
            int i = ms.Count; ms.Add(m); idx[u.Id] = i;
            var f = R.Map.Formation.TryGetValue(u.Type, out var ff) ? ff : R.Map.Formation["infantry"];
            var info = new UnitInfo { Id = u.Id, Faction = u.FactionId ?? 1, Commander = u.CommanderId ?? 0, Name = u.Name, Tpl = tpl, Type = u.Type, Men = u.Soldiers, Color = color, Style = style,
                PerMan = f.PerMan, RankDepth = f.RankDepth, Front = m.P.Fp.Front, Depth = m.P.Fp.Depth };
            foreach (var fig in m.P.Figs) info.Figs.Add(new[] { fig.Width, fig.Depth, fig.Men, fig.Rank });
            rec.Units.Add(info);
            for (int k = 0; k < rec.Frames.Count; k++)
            {
                var a = rec.Frames[k]; Array.Resize(ref a, a.Length + 1); a[a.Length - 1] = new[] { (float)m.P.X, (float)m.P.Y, (float)m.P.Facing, 0f }; rec.Frames[k] = a;
                var mm = rec.Men[k]; Array.Resize(ref mm, mm.Length + 1); mm[mm.Length - 1] = new MenFrame { Xyh = new[] { float.NaN, float.NaN, float.NaN }, Ph = new float[1], Fig = new short[1], Row = new byte[1] }; rec.Men[k] = mm;
                var so = rec.Soldiers[k]; Array.Resize(ref so, so.Length + 1); rec.Soldiers[k] = so;
                if (k < rec.Seen.Count) { var se = rec.Seen[k]; Array.Resize(ref se, se.Length + 1); se[se.Length - 1] = 0xFE; rec.Seen[k] = se; }
            }
            var st = rec.States.ToList(); st.Add(new List<int> { 0, 2, Math.Max(0, rec.Frames.Count - 1), State(m) }); rec.States = st.ToArray();
            sides = ms.Select(x => x.P.U.FactionId ?? 1).Where(x => x > 0 && x < 8).Distinct().ToArray();
        }
        // откат хода: запись — назад к кадру начала хода; счётчики павших и стрел — по восстановленному бою (в записи павшие и стрелы
        // идут один к одному с Battle.Deaths и ArrowLog); стрелы, что на тот миг летели, — снова в полёте
        public void Rewind(int frame)
        {
            var rec = Rec; int keep = frame + 1; float tF = frame * (float)rec.Dt;
            void Cut<T>(List<T> L) { if (L.Count > keep) L.RemoveRange(keep, L.Count - keep); }
            Cut(rec.Frames); Cut(rec.Heads); Cut(rec.Soldiers); Cut(rec.Men); Cut(rec.Fights); Cut(rec.Seen);
            rec.Done = false; rec.Rewinds++;
            int D = battle.Deaths.Count; if (rec.Dead.Count > D) rec.Dead.RemoveRange(D, rec.Dead.Count - D); seenDead = Math.Min(D, rec.Dead.Count);
            var log = battle.ArrowLog; int L = log?.Count ?? 0;
            if (rec.Arrows.Count > L) rec.Arrows.RemoveRange(L, rec.Arrows.Count - L);
            seenArrow = Math.Min(L, rec.Arrows.Count); pending.Clear();
            for (int i = 0; i < seenArrow; i++)
                if (!(log[i].T1 > log[i].T0))
                {
                    var r = rec.Arrows[i]; r.T1 = float.PositiveInfinity; r.X1 = r.Y1 = r.Z1 = float.NaN; r.End = 255; rec.Arrows[i] = r;
                    pending.Add((i, i));
                }
            foreach (var S in rec.States) while (S.Count > 2 && S[S.Count - 2] > frame) S.RemoveRange(S.Count - 2, 2);
            for (int g = 0; g < rec.Gates.Count; g++)
            {
                var G = rec.Gates[g];
                while (G.St.Count >= 2 && G.St[G.St.Count - 2] > frame) G.St.RemoveRange(G.St.Count - 2, 2);
                while (G.Hp.Count >= 3 && G.Hp[G.Hp.Count - 3] > frame) G.Hp.RemoveRange(G.Hp.Count - 3, 3);
                if (gateBroken != null) gateBroken[g] = G.St.Count >= 2 && (G.St[G.St.Count - 1] & 4) != 0;
            }
            if (gateOpen != null) GateFlags();
            while (rec.Duels.Count > battle.Duels.Count) rec.Duels.RemoveAt(rec.Duels.Count - 1);
            foreach (var d in rec.Duels) d.Strikes.RemoveAll(s => s.t > tF + 1e-4f);
        }

        // поединки (Г108): из движка в запись — новые добавляются, идущие обновляются, удары дописываются
        void SnapDuels()
        {
            for (int i = 0; i < battle.Duels.Count; i++)
            {
                var d = battle.Duels[i];
                if (!idx.TryGetValue(d.A.P.U.Id, out var ia) || !idx.TryGetValue(d.B.P.U.Id, out var ib)) continue;
                while (Rec.Duels.Count <= i) Rec.Duels.Add(new DuelRec { A = ia, B = ib, NameA = d.CA?.Name, NameB = d.CB?.Name, ValorA = d.CA?.Valor ?? 10, ValorB = d.CB?.Valor ?? 10, T0 = (float)d.T0 });
                var r = Rec.Duels[i];
                r.X = (float)d.X; r.Y = (float)d.Y; r.R = (float)d.R; r.StartT = (float)d.StartT; r.EndT = (float)d.EndT;
                r.Fighting = d.Fighting; r.Over = d.Over; r.WoundsA = d.WoundsA; r.WoundsB = d.WoundsB; r.LoserKilled = d.LoserKilled;
                r.Winner = d.Winner == null ? -1 : d.Winner == d.A ? ia : ib;
                for (int k = r.Strikes.Count; k < d.Strikes.Count; k++)
                {
                    var s = d.Strikes[k];
                    r.Strikes.Add(((float)s.t, s.unitId == d.A.P.U.Id ? ia : ib, s.hit));
                }
            }
        }
        void FindGates(TerrainMap map)
        {
            byte gate = Terrain.Id("gate"); int W = map.W, H = map.H;
            for (int i = 0; i < map.T.Length; i++)
            {
                if (map.T[i] != gate || gateOf != null && gateOf[i] >= 0) continue;
                if (gateOf == null) { gateOf = new int[map.T.Length]; for (int k = 0; k < gateOf.Length; k++) gateOf[k] = -1; }
                int g = Rec.Gates.Count; double sx = 0, sy = 0; int n = 0;
                var q = new Queue<int>(); q.Enqueue(i); gateOf[i] = g;
                while (q.Count > 0)
                {
                    int c = q.Dequeue(), cx = c % W, cy = c / W; sx += cx + 0.5; sy += cy + 0.5; n++;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = cy + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int nb = ny * W + nx; if (map.T[nb] != gate || gateOf[nb] >= 0) continue;
                        gateOf[nb] = g; q.Enqueue(nb);
                    }
                }
                Rec.Gates.Add(new GateRec { X = (float)(sx / n * map.Cell), Y = (float)(sy / n * map.Cell) });
            }
            if (Rec.Gates.Count > 0) { gateOpen = new bool[Rec.Gates.Count]; gateBroken = new bool[Rec.Gates.Count]; GateFlags(); }
        }
        void GateFlags()
        {
            for (int g = 0; g < gateOpen.Length; g++) gateOpen[g] = !battle.FortOwner.HasValue || battle.GateOpen(Rec.Gates[g].X, Rec.Gates[g].Y);
        }
        // ворота открыли или закрыли (между ходами) — сразу в запись, на последний кадр
        public void RefreshGates()
        {
            if (gateOpen == null) return;
            GateFlags();
            if (Rec.Frames.Count > 0) GateSnap(Rec.Frames.Count - 1);
        }
        void GateSnap(int fr)
        {
            var map = Rec.Map; double cell = map.Cell; var st = new int[gateOpen.Length];
            byte gate = Terrain.Id("gate");
            for (int g = 0; g < st.Length; g++)
            {
                var G = Rec.Gates[g];
                // выбиты (Г105): клетка в середине ворот — уже не ворота (пролом); прочность — пока рубят
                int cc = (int)(G.Y / cell) * map.W + (int)(G.X / cell);
                if (!gateBroken[g] && cc >= 0 && cc < map.T.Length && map.T[cc] != gate && gateOf[cc] == g) gateBroken[g] = true;
                st[g] = gateBroken[g] ? 5 : gateOpen[g] ? 1 : 0;
                var hp = gateBroken[g] ? null : battle.GateHp(G.X, G.Y);
                float h = gateBroken[g] ? 0 : hp != null ? (float)hp.Value.hp : float.NaN, mx = hp != null ? (float)hp.Value.max : G.Hp.Count > 0 ? G.Hp[G.Hp.Count - 1] : 0;
                if (float.IsNaN(h) || G.Hp.Count == 0 && h >= mx) continue;   // целы — не пишем
                if (G.Hp.Count >= 3 && Math.Abs(G.Hp[G.Hp.Count - 2] - h) < 0.01f) continue;
                G.Hp.Add(fr); G.Hp.Add(h); G.Hp.Add(mx);
            }
            if (battle.FortOwner.HasValue)
                foreach (var m in ms)
                {
                    if (m.Gone || m.P.U.FactionId != battle.FortOwner) continue;
                    foreach (var man in m.Men)
                    {
                        if (!man.Alive) continue;
                        int cx = (int)(man.X / cell), cy = (int)(man.Y / cell);
                        if (cx < 0 || cy < 0 || cx >= map.W || cy >= map.H) continue;
                        int g = gateOf[cy * map.W + cx]; if (g >= 0) st[g] |= 2;
                    }
                }
            for (int g = 0; g < st.Length; g++)
            {
                var L = Rec.Gates[g].St;
                if (L.Count >= 2 && L[L.Count - 2] == fr) { L[L.Count - 1] = st[g]; continue; }   // тот же кадр — поправить
                if (L.Count >= 2 && L[L.Count - 1] == st[g]) continue;
                L.Add(fr); L.Add(st[g]);
            }
        }
        static int State(Mover m) => m.Gone ? 2 : m.Fleeing ? (m.RallyPending ? 3 : 1) : m.Rallied ? 4 : 0;
        // кто из бойцов отряда в схватке (Б2): с противником или только что бил либо принял удар на щит
        readonly List<int> eId = new List<int>(), eFoe = new List<int>(); readonly List<float> eSw = new List<float>(), eNx = new List<float>(), ePa = new List<float>();
        readonly Dictionary<Man, int> manOf = new Dictionary<Man, int>();   // боец сцепившихся отрядов → номер отряда в записи (на кадр)
        readonly HashSet<Mover> fightMovers = new HashSet<Mover>();
        void MeleeOf(Mover m, MenFrame mf, int n)
        {
            double now = m.Now;   // часы шага (Battle.Clock стоит на начале хода до его конца)
            eId.Clear(); eSw.Clear(); eNx.Clear(); ePa.Clear(); eFoe.Clear();
            foreach (var man in m.Men)
            {
                if (!man.Alive || man.Id >= n) continue;
                var foe = man.Foe != null && man.Foe.Alive ? man.Foe : null;
                if (foe == null && !(now - man.SwingAt < 0.6) && !(now - man.ParryAt < 0.6)) continue;
                eId.Add(man.Id);
                eSw.Add((float)man.SwingAt); eNx.Add(foe != null ? (float)man.NextSwing : float.NaN); ePa.Add((float)man.ParryAt);
                eFoe.Add(foe != null && manOf.TryGetValue(foe, out var fu) ? fu << 20 | foe.Id : -1);
            }
            if (eId.Count == 0) return;
            mf.Eng = eId.ToArray(); mf.EngSw = eSw.ToArray(); mf.EngNx = eNx.ToArray(); mf.EngPa = ePa.ToArray(); mf.EngFoe = eFoe.ToArray();
        }
        // сбитые с ног (Г90): лежат DownLeft с — когда встанут, по часам шага
        void DownOf(Mover m, MenFrame mf, int n)
        {
            eId.Clear(); eSw.Clear(); eNx.Clear();
            foreach (var man in m.Men)
            {
                if (!man.Alive || man.DownLeft <= 0 || man.Id >= n) continue;
                eId.Add(man.Id); eSw.Add((float)man.DownAt); eNx.Add((float)(m.Now + man.DownLeft));
            }
            if (eId.Count == 0) return;
            mf.Down = eId.ToArray(); mf.DownAt = eSw.ToArray(); mf.DownEnd = eNx.ToArray();
        }

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
            rec.Boxes.Add(ms.Select(m => new[] { (float)m.P.Fp.Front, (float)m.P.Fp.Depth }).ToArray());   // Г111 п.6
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
            // противники поединков: бойцы отрядов, что сейчас касаются врага, — к номеру их отряда в записи
            manOf.Clear(); fightMovers.Clear();
            if (rec.MenMelee && battle != null)
                foreach (var fg in battle.Fights)
                    if (!fg.Over && fg.Touching)
                        foreach (var mv in new[] { fg.A, fg.B })
                            if (fightMovers.Add(mv) && idx.TryGetValue(mv.P.U.Id, out var ui))
                                foreach (var man in mv.Men) manOf[man] = ui;
            rec.Men.Add(ms.Select(m =>
            {
                int n = m.NextManId + 1;
                var mf = new MenFrame { Xyh = new float[3 * n], Ph = new float[n], Fig = new short[n], Row = new byte[n] };
                for (int k = 0; k < mf.Xyh.Length; k++) mf.Xyh[k] = float.NaN;
                // фаза шага: путь с прошлого кадра ÷ длина круга — пеший 1,4 м шагом, 2,4 м бегом; конь — по аллюру
                // (шаг 1,7, рысь 2,8, галоп 5 м); начальная — своя у каждого бойца (как в полигоне)
                if (!gait.TryGetValue(m, out var g) || g.xy.Length < 2 * n)
                {
                    var nx = new float[2 * n]; var nph = new float[n];
                    for (int k = 0; k < nx.Length; k++) nx[k] = float.NaN;
                    if (g.xy != null) { Array.Copy(g.xy, nx, g.xy.Length); Array.Copy(g.ph, nph, g.ph.Length); }
                    gait[m] = g = (nx, nph);
                }
                bool horse = m.P.U.Type == "cavalry";
                foreach (var man in m.Men)
                {
                    if (!man.Alive || man.Id >= n) continue;
                    int id = man.Id;
                    mf.Xyh[3 * id] = (float)man.X; mf.Xyh[3 * id + 1] = (float)man.Y;
                    mf.Xyh[3 * id + 2] = (float)(horse && rec.Frames.Count > 1 ? man.BodyFacing : man.Facing);   // конь — по курсу своей капсулы (Г111 п.2): рисунок не въезжает в чужой строй
                    mf.Fig[id] = (short)(man.Fig?.Id ?? 0); mf.Row[id] = (byte)Math.Min(255, man.Row);
                    float px = g.xy[2 * id], py = g.xy[2 * id + 1];
                    if (float.IsNaN(px)) g.ph[id] = (float)Journal.Art.Kits.Hash(m.P.U.Id * 7919 + id, 5);
                    else
                    {
                        float d = (float)Math.Sqrt((man.X - px) * (man.X - px) + (man.Y - py) * (man.Y - py)), v = d / (float)rec.Dt;
                        float stride = horse ? (v < 2.3f ? 1.7f : v < 4.8f ? 2.8f : 5.0f) : v > 2.6f ? 2.4f : 1.4f;
                        g.ph[id] += d / stride;
                    }
                    g.xy[2 * id] = (float)man.X; g.xy[2 * id + 1] = (float)man.Y;
                    mf.Ph[id] = g.ph[id];
                    if (man.Z > 0) { if (mf.Z == null) mf.Z = new float[n]; mf.Z[id] = (float)man.Z; }   // на стене или башне (Г104)
                }
                // полководец и стража (Г108)
                if (m.CommanderMan != null && m.CommanderMan.Alive && m.CommanderMan.Id < n)
                {
                    mf.Cmd = m.CommanderMan.Id;
                    if (m.Guard.Count > 0) mf.Guard = m.Guard.Where(g => g.Alive && g.Id < n).Select(g => g.Id).ToArray();
                }
                if (rec.MenMelee) { MeleeOf(m, mf, n); DownOf(m, mf, n); }
                return mf;
            }).ToArray());
            int fr = rec.Frames.Count - 1;
            if (Sees != null)
            {
                var seen = new byte[ms.Count];
                for (int i = 0; i < ms.Count; i++)
                    foreach (int sd in sides) if (Sees(sd, ms[i])) seen[i] |= (byte)(1 << sd);
                rec.Seen.Add(seen);
            }
            if (battle != null) SnapDuels();
            if (battle == null) { rec.Fights.Add(Array.Empty<int>()); return; }
            if (gateOpen != null) { if (fr % 25 == 0) GateFlags(); GateSnap(fr); }
            rec.Fights.Add(battle.Fights.Where(f => !f.Over && f.Touching).SelectMany(f => new[] { idx[f.A.P.U.Id], idx[f.B.P.U.Id] }).ToArray());
            // натиск в схватке — для подписи «Натиск «A» на «B»!» (панели не лезут в бой, пока его считает другой поток)
            foreach (var f in battle.Fights)
                if (!f.Over && f.Touching && idx.TryGetValue(f.A.P.U.Id, out var fa) && idx.TryGetValue(f.B.P.U.Id, out var fb))
                {
                    long key = (long)Math.Min(fa, fb) << 32 | (uint)Math.Max(fa, fb);
                    if (!rec.Charges.ContainsKey(key) && f.Notes.Any(x => x.StartsWith("натиск «"))) rec.Charges[key] = fa;
                }
            // туман на карте (Г107): видимые клетки сторон — раз в секунду
            if (Sees != null && fr % 5 == 0)
                foreach (int sd in sides) { var c = battle.SeenCells(sd); if (c != null) rec.FogCells[sd] = (bool[])c.Clone(); }
            for (; seenDead < battle.Deaths.Count; seenDead++)
            {
                var d = battle.Deaths[seenDead];
                int part = d.Part == "head" ? 0 : d.Part == "legs" ? 2 : d.Part == "horse" ? 3 : 1;
                float dt0 = fr * (float)rec.Dt, dT = d.T > 0 && d.T <= dt0 + 1e-3 ? (float)d.T : dt0;   // время из движка, не позже кадра
                rec.Dead.Add(new DeadRec { X = (float)d.X, Y = (float)d.Y, T = dT, Frame = fr, Unit = idx[d.UnitId], Facing = (float)d.Facing, Dir = (float)d.Dir, Part = part, Man = d.ManId, Killed = d.Killed });
            }
            for (int i = 0; i < ms.Count; i++) if (rec.States[i][rec.States[i].Count - 1] != State(ms[i])) { rec.States[i].Add(fr); rec.States[i].Add(State(ms[i])); }
            // стрелы — в запись сразу на вылете (конец ещё не известен: T1 = ∞, смотрелка ведёт её по броску), долетела —
            // запись дополняется концом. Иначе в живой записи стрела видна только последние доли секунды полёта
            var log = battle.ArrowLog;
            for (; seenArrow < log.Count; seenArrow++)
            {
                var a = log[seenArrow];
                pending.Add((seenArrow, rec.Arrows.Count));
                rec.Arrows.Add(new ArrowRec { T0 = (float)a.T0, X0 = (float)a.X0, Y0 = (float)a.Y0, Z0 = (float)a.Z0, VX = (float)a.VX, VY = (float)a.VY, VZ = (float)a.VZ,
                    T1 = float.PositiveInfinity, X1 = float.NaN, Y1 = float.NaN, Z1 = float.NaN, Unit = idx[a.UnitId], End = 255 });
            }
            for (int q = pending.Count - 1; q >= 0; q--)
            {
                var (li, ri) = pending[q];
                var a = log[li];
                if (!(a.T1 > a.T0)) continue;
                var r = rec.Arrows[ri];
                r.T1 = (float)a.T1; r.X1 = (float)a.X1; r.Y1 = (float)a.Y1; r.Z1 = (float)a.Z1; r.End = a.End;
                rec.Arrows[ri] = r;
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
            var rc = new Recorder(sc.Name, sc.Note, sc.Geo, ms, m => sc.Tpl[m], sc.Battle, sc.Turns, m => sc.Color.TryGetValue(m, out var c) ? c : null,
                                  m => sc.Style.TryGetValue(m, out var st) ? st : null);
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
