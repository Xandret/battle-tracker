// ═══════════ Fortify.cs — укрепления: участки стен с прочностью (этап 6б, Г46) — ЧЕРНОВИК ДО ГМа (копия fortify.js) ═══════════
// Стена, частокол, ворота и башни — не просто клетки местности, а участки с прочностью. Слой участков
// (TerrainMap.S, Forts) лежит в карте и в сохранении (Terrain.cs); в карте — только полученный урон,
// сама прочность — из Rules.Siege.Hp: поправка чисел ГМом не требует пересобирать карты.
//
// Нарезка (Ш1): связный кусок стены или частокола режется по пути вдоль него на равные участки не длиннее
// Siege.SectionM; башни и ворота — каждые отдельным участком. Пролом (Ш2): прочность обнулилась — BreachM метров
// стены у точки попадания становятся проломом; остаток удара идёт дальше. Ворота выбиваются, башня рушится целиком.
// Сверяется с трекером бит в бит по shared/golden/map.json (раздел forts).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class FortHit
    {
        public List<string> Lines = new List<string>();
        public List<int> Cells = new List<int>();
        public int Opened;
        public bool Destroyed;
    }

    public static class Fortify
    {
        static readonly byte Wall = Terrain.Id("wall"), Palisade = Terrain.Id("palisade"), Gate = Terrain.Id("gate"),
                             Tower = Terrain.Id("tower"), Breach = Terrain.Id("breach");
        static bool IsFort(byte t) => t == Wall || t == Palisade || t == Gate || t == Tower;
        static int[] N8X => Terrain.N8X;
        static int[] N8Y => Terrain.N8Y;
        const int GateLook = 4;   // ворота: смотрим стену и частокол не дальше 4 клеток

        sealed class Piece { public string Kind; public List<int> Cells; }

        // Строит участки заново. prevS, prevForts — прежние участки (после правки в редакторе): клетки-проломы
        // прежних участков остаются в своих участках, урон и проломы переходят к новому участку, если большая
        // часть его клеток была в одном прежнем участке того же вида.
        public static List<FortSection> BuildSections(TerrainMap m, Rules R, ushort[] prevS = null, List<FortSection> prevForts = null)
        {
            int n = m.W * m.H;
            Dictionary<int, FortSection> prevById = prevS != null && prevForts != null ? prevForts.ToDictionary(f => f.Id) : null;
            var grp = new byte[n];
            for (int i = 0; i < n; i++)
            {
                byte t = m.T[i];
                if (IsFort(t)) grp[i] = t;
                else if (t == Breach && prevById != null && prevS[i] != 0 && prevById.TryGetValue(prevS[i], out var pf)) grp[i] = Terrain.FortCode(pf.Kind);
            }
            var comp = Filled(n, -1);
            var own = Filled(n, -1);
            var dist = Filled(n, -1);
            var bucket = new int[n];
            var queue = new int[n];
            int L = (int)Math.Max(1, Js.Round(R.Siege.SectionM / m.Cell));
            var pieces = new List<Piece>();
            int token = 0;

            // Кусок стены режется по пути вдоль него: от дальней клетки (дальней от первой) — отрезки на равные части
            // не длиннее L; связная часть одного отрезка — участок. Кольцо сходится «рукавами» в начале и в конце
            // пути — такой участок длиннее L и режется тем же способом ещё раз.
            void Chunk(List<int> cells, string kind)
            {
                int tag = ++token;
                foreach (int i in cells) own[i] = tag;
                Func<int, bool> inSet = j => own[j] == tag;
                cells.Sort();
                var a = Terrain.Bfs8(m, cells[0], inSet, dist, queue);
                foreach (int i in cells) dist[i] = -1;
                var b = Terrain.Bfs8(m, a.far, inSet, dist, queue);
                int D = b.farD;
                if (D + 1 <= L) { foreach (int i in cells) dist[i] = -1; pieces.Add(new Piece { Kind = kind, Cells = cells }); return; }
                int parts = (D + 1 + L - 1) / L;
                foreach (int i in cells) { bucket[i] = dist[i] * parts / (D + 1); dist[i] = -1; }
                var split = new List<List<int>>();
                foreach (int i in cells)
                {
                    if (own[i] != tag) continue;
                    int ptag = ++token;
                    var piece = new List<int> { i };
                    own[i] = ptag;
                    for (int head = 0; head < piece.Count; head++)
                    {
                        int p = piece[head], x = p % m.W, y = p / m.W;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = x + N8X[k], ny = y + N8Y[k];
                            if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                            int j = ny * m.W + nx;
                            if (own[j] == tag && bucket[j] == bucket[i]) { own[j] = ptag; piece.Add(j); }
                        }
                    }
                    split.Add(piece);
                }
                foreach (var piece in split) Chunk(piece, kind);
            }

            int compId = 0;
            for (int i0 = 0; i0 < n; i0++)
            {
                byte g = grp[i0];
                if (g == 0 || comp[i0] != -1) continue;
                // связный кусок одного вида (восемь соседей)
                int c = compId++;
                var cells = new List<int> { i0 };
                comp[i0] = c;
                for (int head = 0; head < cells.Count; head++)
                {
                    int i = cells[head], x = i % m.W, y = i / m.W;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + N8X[k], ny = y + N8Y[k];
                        if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                        int j = ny * m.W + nx;
                        if (grp[j] == g && comp[j] == -1) { comp[j] = c; cells.Add(j); }
                    }
                }
                if (g == Tower) { cells.Sort(); pieces.Add(new Piece { Kind = "tower", Cells = cells }); }
                else if (g == Gate) { string kind = GateKind(m, cells); cells.Sort(); pieces.Add(new Piece { Kind = kind, Cells = cells }); }
                else Chunk(cells, g == Palisade ? "palisade" : "wall");
            }
            // номера — по первой клетке участка: не зависят от порядка нарезки
            pieces.Sort((p, q) => p.Cells[0].CompareTo(q.Cells[0]));
            var s = new ushort[n];
            var forts = new List<FortSection>();
            foreach (var p in pieces)
            {
                if (forts.Count >= Terrain.MaxSections) break;
                int id = forts.Count + 1;
                forts.Add(new FortSection { Id = id, Kind = p.Kind });
                foreach (int i in p.Cells) s[i] = (ushort)id;
            }
            if (prevById != null)
            {
                // урон и проломы — от прежнего участка, где лежит большинство клеток (при равенстве — меньший номер)
                var votes = forts.Select(_ => new Dictionary<int, int>()).ToList();
                for (int i = 0; i < n; i++)
                    if (s[i] != 0 && prevS[i] != 0)
                    {
                        var v = votes[s[i] - 1];
                        v[prevS[i]] = (v.TryGetValue(prevS[i], out int cnt) ? cnt : 0) + 1;
                    }
                for (int k = 0; k < forts.Count; k++)
                {
                    int best = 0, bestN = 0;
                    foreach (var kv in votes[k]) if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best)) { best = kv.Key; bestN = kv.Value; }
                    if (best != 0 && prevById.TryGetValue(best, out var old) && old.Kind == forts[k].Kind)
                    {
                        forts[k].Dmg = old.Dmg; forts[k].Breaches = old.Breaches;
                    }
                }
            }
            m.S = s; m.Forts = forts;
            Terrain.IndexSections(m);
            return m.Forts;
        }

        static int[] Filled(int n, int v) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        // Ворота в частоколе — деревянные, в каменной стене — окованные: по клеткам стены и частокола не дальше
        // GateLook клеток от ворот (у замка по бокам ворот надвратные башни — стена начинается за ними)
        static string GateKind(TerrainMap m, List<int> cells)
        {
            int x0 = m.W, y0 = m.H, x1 = -1, y1 = -1;
            foreach (int i in cells) { int x = i % m.W, y = i / m.W; x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            int pal = 0, wall = 0;
            for (int y = Math.Max(0, y0 - GateLook); y <= Math.Min(m.H - 1, y1 + GateLook); y++)
                for (int x = Math.Max(0, x0 - GateLook); x <= Math.Min(m.W - 1, x1 + GateLook); x++)
                {
                    byte t = m.T[y * m.W + x];
                    if (t == Palisade) pal++; else if (t == Wall) wall++;
                }
            return wall > pal ? "gateIron" : "gateWood";
        }

        // Участков ещё нет (старая партия, новая карта) — построить
        public static TerrainMap EnsureSections(TerrainMap m, Rules R)
        {
            if (m != null && (m.S == null || m.Forts == null)) BuildSections(m, R);
            return m;
        }

        // ── чтение ──
        public static double SectionMax(FortSection f, Rules R) => R.Siege.Hp[f.Kind];
        public static double SectionHp(FortSection f, Rules R) => f.Up > 0 ? Math.Max(0, SectionMax(f, R) - f.Dmg) : 0;
        public static string SectionName(FortSection f) => $"участок №{f.Id} ({Terrain.FortKinds[f.Kind]})";
        public static FortSection GetSection(TerrainMap m, int id) => m?.Forts?.Find(f => f.Id == id);
        // Участок под точкой (fx, fy — доли карты 0…1) или null
        public static FortSection SectionAt(TerrainMap m, double fx, double fy)
        {
            if (m?.S == null) return null;
            int x = (int)Math.Min(m.W - 1, Math.Max(0, Math.Floor(fx * m.W))), y = (int)Math.Min(m.H - 1, Math.Max(0, Math.Floor(fy * m.H)));
            return GetSection(m, m.S[y * m.W + x]);
        }

        // ── урон и починка: меняют карту на месте (как кисти), возвращают строки журнала ──
        // atX, atY — точка попадания в клетках; NaN — центр участка.
        public static FortHit DamageSection(TerrainMap m, int id, double amount, double atX, double atY, Rules R)
        {
            var f = GetSection(m, id);
            if (f == null) return null;
            double max = SectionMax(f, R);
            string name = SectionName(f);
            var hit = new FortHit();
            byte code = Terrain.FortCode(f.Kind);
            var up = new List<int>();
            for (int i = 0; i < m.S.Length; i++) if (m.S[i] == id && m.T[i] == code) up.Add(i);
            if (up.Count == 0) { hit.Lines.Add($"{Cap(name)} уже разрушен"); hit.Destroyed = true; return hit; }
            amount = Math.Max(0, amount);
            double hp0 = max - f.Dmg;
            f.Dmg += amount;
            bool whole = f.Kind == "tower" || f.Kind == "gateWood" || f.Kind == "gateIron";
            double ax = double.IsNaN(atX) ? f.Cx : atX, ay = double.IsNaN(atY) ? f.Cy : atY;
            while (f.Dmg >= max && up.Count > 0)
            {
                List<int> take = up;
                if (!whole)
                {
                    // пролом: BreachM метров вдоль стены × толщина стены — ближайшие к попаданию целые клетки
                    int k = (int)(Math.Ceiling(R.Siege.BreachM / m.Cell) * Math.Max(1, Js.Round((double)f.N / f.Len)));
                    take = up.Select(i => { double dx = i % m.W + 0.5 - ax, dy = i / m.W + 0.5 - ay; return (i, d: dx * dx + dy * dy); })
                             .OrderBy(p => p.d).ThenBy(p => p.i).Take(k).Select(p => p.i).ToList();
                }
                foreach (int i in take) m.T[i] = Breach;
                hit.Cells.AddRange(take);
                hit.Opened++; f.Breaches++;
                f.Dmg -= max;
                up = up.Where(i => m.T[i] != Breach).ToList();
            }
            f.Up = up.Count;
            if (f.Up == 0) f.Dmg = 0;
            double hp1 = SectionHp(f, R);
            hit.Lines.Add($"🏰 {Cap(name)}: удар −{Fmt(amount)} прочности, было {Fmt(hp0)} из {Js.Num(max)} · черновик");
            if (hit.Opened > 0)
            {
                double meters = Js.Round(hit.Cells.Count / Math.Max(1, Js.Round((double)f.N / f.Len)) * m.Cell);
                hit.Lines.Add(f.Kind == "tower" ? "💥 Башня рухнула — на её месте пролом"
                            : whole ? "💥 Ворота выбиты — проход открыт"
                            : $"💥 Пролом{(hit.Opened > 1 ? $" ×{hit.Opened}" : "")}: {Js.Num(meters)} м стены обрушено");
            }
            hit.Lines.Add(f.Up > 0 ? $"Прочность участка №{f.Id}: {Fmt(hp1)} из {Js.Num(max)}{(f.Breaches > 0 ? $" · проломов {f.Breaches}" : "")}"
                                   : $"☠ {Cap(name)} разрушен целиком");
            hit.Destroyed = f.Up == 0;
            return hit;
        }

        // Починка: прочность снова полная, проломы остаются (их заделывает осада — этап 6в)
        public static List<string> RepairSection(TerrainMap m, int id, Rules R)
        {
            var f = GetSection(m, id);
            if (f == null || f.Up == 0) return null;
            double before = SectionHp(f, R);
            f.Dmg = 0;
            return new List<string> { $"🔧 {Cap(SectionName(f))}: прочность {Fmt(before)} → {Js.Num(SectionMax(f, R))} · черновик" };
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);
        static string Fmt(double v) => Js.Num(Js.Round(v * 10) / 10).Replace(".", ",");
    }
}
