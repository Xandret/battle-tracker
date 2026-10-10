// Г118: сырая запись без смотрелки — конь вбок и назад от своего курса, поворот, скорость
// сырая запись (кадры по 0,2 с, без смотрелки): конь вбок и назад относительно своего курса в записи, поворот, скорость
var V = UnityEngine.Object.FindAnyObjectByType<Journal.Viewer.BattleViewer>();
var pc = UnityEngine.Object.FindAnyObjectByType<Journal.Play.PlayController>();
var rec = V.Rec; float dt = (float)rec.Dt;
var sb = new System.Text.StringBuilder("сырая запись, кадр 0,2 с\n");
for (int ui = 0; ui < rec.Units.Count; ui++)
{
    if (rec.Units[ui].Type != "cavalry") continue;
    float norm = (float)BattleCore.MoveSim.TopSpeed(pc.Battle.Movers[ui].P.U, pc.Battle.R);
    long n = 0, side = 0, back = 0, turn = 0, fast = 0, sideMelee = 0; float ms = 0, mb = 0, mt = 0;
    var worst = new System.Collections.Generic.List<(float v, int f, int id, string k)>();
    for (int f = 1; f < rec.Men.Count; f++)
    {
        var a = rec.Men[f - 1][ui].Xyh; var b = rec.Men[f][ui].Xyh;
        var eng = new System.Collections.Generic.HashSet<int>(rec.Men[f][ui].Eng ?? new int[0]);
        int m = System.Math.Min(a.Length, b.Length) / 3;
        for (int id = 1; id < m; id++)
        {
            if (float.IsNaN(a[3 * id]) || float.IsNaN(b[3 * id])) continue;
            float dx = b[3 * id] - a[3 * id], dy = b[3 * id + 1] - a[3 * id + 1];
            // курс — средний между кадрами
            float h0 = a[3 * id + 2], h1 = b[3 * id + 2], h = (h0 + UnityEngine.Mathf.DeltaAngle(h0, h1) / 2) * UnityEngine.Mathf.Deg2Rad;
            float fx = UnityEngine.Mathf.Sin(h), fy = -UnityEngine.Mathf.Cos(h);
            float vf = (dx * fx + dy * fy) / dt, vs = UnityEngine.Mathf.Abs(-dx * fy + dy * fx) / dt, v = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy) / dt;
            float tr = UnityEngine.Mathf.Abs(UnityEngine.Mathf.DeltaAngle(h0, h1)) / dt;
            n++;
            if (vs > 2.2f) { side++; if (eng.Contains(id)) sideMelee++; } if (-vf > 2.2f) back++; if (tr > 94.5f) turn++; if (v > norm * 1.1f) fast++;
            if (vs > ms) ms = vs; if (-vf > mb) mb = -vf; if (tr > mt) mt = tr;
            if (-vf > 10) worst.Add((-vf, f, id, "назад")); else if (vs > 10) worst.Add((vs, f, id, "вбок"));
        }
    }
    sb.Append($"«{rec.Units[ui].Name}» наибольшая {norm:0.0} м/с: замеров {n}; вбок > 2,2 м/с — {100.0 * side / n:0.0}% (в схватке {100.0 * sideMelee / System.Math.Max(1, side):0}% из них), наиб. {ms:0.0}; назад > 2,2 — {100.0 * back / n:0.0}%, наиб. {mb:0.0}; поворот > 94,5°/с — {100.0 * turn / n:0.0}%, наиб. {mt:0}; быстрее наибольшей×1,1 — {100.0 * fast / n:0.00}%\n");
    // худшие эпизоды по секундам
    foreach (var g in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(System.Linq.Enumerable.GroupBy(worst, w => w.f / 5), g => System.Linq.Enumerable.Count(g)), 6))
    {
        var w0 = System.Linq.Enumerable.First(System.Linq.Enumerable.OrderByDescending(g, w => w.v));
        sb.Append($"   {g.Key * 5 * dt:0}–{(g.Key + 1) * 5 * dt:0} с: коней быстрее 10 м/с вбок/назад — {System.Linq.Enumerable.Count(g)} замеров; худший боец {w0.id} {w0.k} {w0.v:0.0} м/с (кадр {w0.f})\n");
    }
}
// пункт а: опорная точка отряда (m.P.X/Y — середина в записи) быстрее 2 × наибольшей
for (int ui = 0; ui < rec.Units.Count; ui++)
{
    float top = (float)BattleCore.MoveSim.TopSpeed(pc.Battle.Movers[ui].P.U, pc.Battle.R); int jumps = 0; float mj = 0; int fj = -1;
    for (int f = 1; f < rec.Frames.Count; f++)
    {
        var a = rec.Frames[f - 1][ui]; var b = rec.Frames[f][ui];
        float d = UnityEngine.Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
        if (d / dt > 2 * top) { jumps++; if (d > mj) { mj = d; fj = f; } }
    }
    if (jumps > 0) sb.Append($"опорная точка «{rec.Units[ui].Name}»: скачков {jumps}, наибольший {mj:0.0} м за кадр (кадр {fj})\n");
}
return sb.ToString();
