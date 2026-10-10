// Г118, шаг 2: мерило гладкости в показе — весь записанный бой рисуется шагом 1/30 с, MotionProbe меряет каждого бойца.
// Temp/g118/tag.txt — имя отчёта («имя old» — без ограничения отставания сглаживания, п. 1 отчёта). Отчёт — Temp/g118/probe_<имя>.txt (~3 мин на 100 с боя)
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var V = UnityEngine.Object.FindAnyObjectByType<Journal.Viewer.BattleViewer>();
string dir = "Temp/g118/"; System.IO.Directory.CreateDirectory(dir);
var targ = System.IO.File.ReadAllText(dir + "tag.txt").Trim().Split(' '); string tag = targ[0];
Journal.Viewer.MenView.LagClamp = targ.Length < 2 || targ[1] != "old";
var mv = typeof(Journal.Viewer.BattleViewer).GetField("menView", F).GetValue(V);
var cols = (UnityEngine.Color32[])typeof(Journal.Viewer.BattleViewer).GetField("unitColRaw", F).GetValue(V);
var draw = mv.GetType().GetMethod("Draw");
var rect = new UnityEngine.Rect(-1e5f, -1e5f, 2e5f, 2e5f);
var rec = V.Rec; float end = (rec.Frames.Count - 1) * (float)rec.Dt, step = 1f / 30;
Journal.Viewer.MotionProbe.Reset();
var pc = UnityEngine.Object.FindAnyObjectByType<Journal.Play.PlayController>();
var norms = new System.Collections.Generic.List<(float, float)>();
foreach (var m in pc.Battle.Movers) norms.Add(((float)BattleCore.MoveSim.TopSpeed(m.P.U, pc.Battle.R), (float)BattleCore.MoveSim.AccelSec(m.P.U, pc.Battle.R)));
Journal.Viewer.MotionProbe.FromRules(pc.Battle.R);   // пределы — мерила ядра (Rules.Smooth)
Journal.Viewer.MotionProbe.Norm = ui => ui < norms.Count ? norms[ui] : (6f, 1f);
Journal.Viewer.MotionProbe.UnitName = ui => ui < rec.Units.Count ? rec.Units[ui].Name : "#" + ui;
System.IO.File.Delete(dir + "probe_" + tag + ".txt");
V.enabled = false;   // обычный показ не рисует между кадрами замера (иначе сглаживание прыгает к концу хода)
float t = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
UnityEditor.EditorApplication.CallbackFunction cb = null;
cb = () =>
{
    var lap = System.Diagnostics.Stopwatch.StartNew();
    Journal.Viewer.MotionProbe.On = true;
    try { lock (rec) while (t <= end && lap.ElapsedMilliseconds < 200) { draw.Invoke(mv, new object[] { (double)t, cols, rect, 60f }); t += step; } }
    catch (System.Exception e) { UnityEditor.EditorApplication.update -= cb; V.enabled = true; System.IO.File.WriteAllText(dir + "probe_" + tag + ".txt", "ошибка: " + e); }
    Journal.Viewer.MotionProbe.On = false;
    if (t > end) { UnityEditor.EditorApplication.update -= cb; V.enabled = true; System.IO.File.WriteAllText(dir + "probe_" + tag + ".txt", $"показ 0…{end:0.0} с, шаг 1/30 с, {sw.Elapsed.TotalSeconds:0} с счёта\n" + Journal.Viewer.MotionProbe.Report()); }
};
UnityEditor.EditorApplication.update += cb;
return $"замер поставлен: 0…{end:0.0} с";
