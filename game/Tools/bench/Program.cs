// Замер скорости движка на сохранении трекера: ход за ходом — время счёта, приказов и снятия кадров (см. bench.csproj)
using System.Diagnostics;
using BattleCore;
using Journal.Viewer;

if (args.Length == 0) { Console.WriteLine("dotnet run -c Release -- <сохранение armiya_hodN.txt> [ходов=2] [ширина карты, м]"); return; }
string path = args[0];
int turns = args.Length > 1 ? int.Parse(args[1]) : 2;
double width = args.Length > 2 ? double.Parse(args[2]) : 0;
var sw = Stopwatch.StartNew();
var sc = SaveScene.Make(path, turns, widthM: width);
Console.WriteLine($"сцена построена за {sw.ElapsedMilliseconds} мс");
Console.WriteLine(sc.Note);
var ms = sc.Units.Select(u => u.M).ToList();
var rc = new Recorder(sc.Name, sc.Note, sc.Geo, ms, m => sc.Tpl[m], sc.Battle, sc.Turns);
var snap = new Stopwatch();
snap.Start(); rc.Snap(); snap.Stop();
int men = ms.Sum(m => m.Men.Count);
Console.WriteLine($"карта {sc.Geo.W:0} × {sc.Geo.H:0} м, клеток {sc.Geo.Map.W}×{sc.Geo.Map.H}; бойцов в Mover.Men: {men}");
for (int turn = 0; turn < sc.Turns; turn++)
{
    var t0 = sw.Elapsed;
    sc.Before?.Invoke(turn);
    var tOrd = sw.Elapsed - t0;
    int k = 0;
    sc.Battle.Turn(t => { if (++k % 4 == 0) { snap.Start(); rc.Snap(); snap.Stop(); } });
    var dt = sw.Elapsed - t0;
    int alive = ms.Sum(m => m.Men.Count(x => x.Alive)), fights = sc.Battle.Fights.Count(f => !f.Over);
    Console.WriteLine($"ход {turn + 1}: {dt.TotalSeconds:0.0} с (приказы {tOrd.TotalMilliseconds:0} мс), шагов {k}, живых {alive}, схваток {fights}, павших всего {sc.Battle.Deaths.Count}");
}
Console.WriteLine($"снятие кадров всего: {snap.Elapsed.TotalSeconds:0.00} с; всё: {sw.Elapsed.TotalSeconds:0.0} с на {sc.Turns * Rules.Base.Move.TurnSec:0} с боя");
