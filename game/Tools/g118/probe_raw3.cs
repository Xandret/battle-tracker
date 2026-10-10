// Г118: сырая запись без смотрелки — разгон каждого бойца по кадрам 0,2 с
// сырая запись (кадры по 0,2 с, без смотрелки): разгон каждого бойца — |Δv| между соседними кадрами / 0,2 с
var V = UnityEngine.Object.FindAnyObjectByType<Journal.Viewer.BattleViewer>();
var pc = UnityEngine.Object.FindAnyObjectByType<Journal.Play.PlayController>();
var rec = V.Rec; float dt = (float)rec.Dt;
var sb = new System.Text.StringBuilder("сырая запись: разгон (м/с²) по кадрам 0,2 с; предел — наибольшая / разгон отряда × 1,05 (мерило ядра)\n");
for (int ui = 0; ui < rec.Units.Count; ui++)
{
    var u = pc.Battle.Movers[ui].P.U;
    float norm = (float)BattleCore.MoveSim.TopSpeed(u, pc.Battle.R), asec = (float)BattleCore.MoveSim.AccelSec(u, pc.Battle.R);
    float lim = norm / System.Math.Max(0.2f, asec) * 1.05f;
    long n = 0, over = 0, over3 = 0; float mx = 0;
    for (int f = 2; f < rec.Men.Count; f++)
    {
        var a = rec.Men[f - 2][ui].Xyh; var b = rec.Men[f - 1][ui].Xyh; var c = rec.Men[f][ui].Xyh;
        int m = System.Math.Min(a.Length, System.Math.Min(b.Length, c.Length)) / 3;
        for (int id = 1; id < m; id++)
        {
            if (float.IsNaN(a[3 * id]) || float.IsNaN(b[3 * id]) || float.IsNaN(c[3 * id])) continue;
            float v1x = (b[3 * id] - a[3 * id]) / dt, v1y = (b[3 * id + 1] - a[3 * id + 1]) / dt, v2x = (c[3 * id] - b[3 * id]) / dt, v2y = (c[3 * id + 1] - b[3 * id + 1]) / dt;
            float acc = UnityEngine.Mathf.Sqrt((v2x - v1x) * (v2x - v1x) + (v2y - v1y) * (v2y - v1y)) / dt;
            n++; if (acc > lim) over++; if (acc > 3 * lim) over3++; if (acc > mx) mx = acc;
        }
    }
    sb.Append($"«{u.Name}» ({u.Type}) предел {lim:0.0}: сверх — {100.0 * over / System.Math.Max(1, n):0.0}%, втрое сверх — {100.0 * over3 / System.Math.Max(1, n):0.00}%, наиб. {mx:0}\n");
}
return sb.ToString();
