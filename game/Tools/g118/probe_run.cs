// Г118, шаг 1 (Play mode, сцена Play): бой для замера. Temp/g118/step.txt — «training» (учебное поле, обе стороны под ИИ;
// «training body» — курс коня по капсуле, как до Г118), «save <путь>» (копия сохранения, все атакуют ближайшего), «go» — «Ход!»,
// «phase» — где бой. Запуск: unity command eval_file --file game/Tools/g118/probe_run.cs
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var hud = UnityEngine.Object.FindAnyObjectByType<Journal.Play.PlayHud>();
var pc = UnityEngine.Object.FindAnyObjectByType<Journal.Play.PlayController>();
var V = UnityEngine.Object.FindAnyObjectByType<Journal.Viewer.BattleViewer>();
string dir = "Temp/g118/"; System.IO.Directory.CreateDirectory(dir);
var arg = System.IO.File.ReadAllText(dir + "step.txt").Trim().Split(' ');
BattleCore.Mover Near(BattleCore.Mover m) { BattleCore.Mover best = null; double bd = 1e18; foreach (var e in pc.Battle.Movers) if (Journal.Play.PlayController.SideOf(e) != Journal.Play.PlayController.SideOf(m) && Journal.Play.PlayController.Present(e)) { double d = (e.P.X - m.P.X) * (e.P.X - m.P.X) + (e.P.Y - m.P.Y) * (e.P.Y - m.P.Y); if (d < bd) { bd = d; best = e; } } return best; }
if (arg[0] == "training")
{
    Journal.Viewer.Recorder.HorseBody = arg.Length > 1 && arg[1] == "body";
    ((Journal.Play.MainMenu)typeof(Journal.Play.PlayHud).GetField("mainMenu", F).GetValue(hud)).Hide(); pc.NewBattle(() => Journal.Play.PlayScenarios.Training());
    // обе стороны — ИИ: «Дарлтоны» обходят, «Пикшарп» наступают на их конницу — конница разворачивается и маневрирует
    var s1 = System.Linq.Enumerable.ToList(pc.SideUnits(1)); var s2 = System.Linq.Enumerable.ToList(pc.SideUnits(2));
    var cav2 = System.Linq.Enumerable.First(s2, m => m.P.U.Type == "cavalry");
    pc.Battle.AssignSide(2, BattleCore.AiKind.Flank, s1[0].P.U.Id);
    pc.Battle.AssignSide(1, BattleCore.AiKind.Advance, cav2.P.U.Id);
    return "учебное поле: обе стороны под ИИ";
}
if (arg[0] == "save")
{
    ((Journal.Play.MainMenu)typeof(Journal.Play.PlayHud).GetField("mainMenu", F).GetValue(hud)).Hide();
    string path = arg[1].Replace('|', ' ');
    pc.NewBattle(() => Journal.Play.PlayScenarios.FromSave(path));
    foreach (var m in pc.Battle.Movers) { var e = Near(m); if (e != null) pc.Order(m, new BattleCore.MoveOrder { Kind = BattleCore.OrderKind.Attack, TargetId = e.P.U.Id, Charge = m.P.U.Type == "cavalry" }, true); }
    return $"сохранение: {pc.Battle.Movers.Count} отр., все атакуют ближайшего";
}
if (arg[0] == "go") { pc.Speed = 4; pc.Go(); return "ход"; }
if (arg[0] == "phase") return $"{pc.Phase} ход {pc.Session.Turn} запись {V.Rec.Frames.Count} кадров, {V.Rec.Frames.Count * V.Rec.Dt:0.0} с";
if (arg[0] == "reset")
{
    Journal.Viewer.MotionProbe.Reset(); var rec = V.Rec;
    Journal.Viewer.MotionProbe.UnitName = ui => ui < rec.Units.Count ? rec.Units[ui].Name : "#" + ui;
    return "сброс";
}
if (arg[0] == "measure")
{
    float a = float.Parse(arg[1], System.Globalization.CultureInfo.InvariantCulture), b = float.Parse(arg[2], System.Globalization.CultureInfo.InvariantCulture), step = 1f / 30;
    var mv = typeof(Journal.Viewer.BattleViewer).GetField("menView", F).GetValue(V);
    var cols = (UnityEngine.Color32[])typeof(Journal.Viewer.BattleViewer).GetField("unitColRaw", F).GetValue(V);
    var draw = mv.GetType().GetMethod("Draw");
    var rect = new UnityEngine.Rect(-1e5f, -1e5f, 2e5f, 2e5f);
    var rec = V.Rec; float end = System.Math.Min(b, (rec.Frames.Count - 1) * (float)rec.Dt);
    Journal.Viewer.MotionProbe.On = true;
    var sw = System.Diagnostics.Stopwatch.StartNew(); int n = 0;
    lock (rec) for (float t = a; t <= end; t += step) { draw.Invoke(mv, new object[] { (double)t, cols, rect, 60f }); n++; }
    Journal.Viewer.MotionProbe.On = false;
    return $"показ {a:0.0}…{end:0.0} с, кадров {n}, {sw.ElapsedMilliseconds} мс";
}
if (arg[0] == "report") { var r = Journal.Viewer.MotionProbe.Report(); System.IO.File.WriteAllText(dir + "probe_" + (arg.Length > 1 ? arg[1] : "r") + ".txt", r); return r; }
return "?";
