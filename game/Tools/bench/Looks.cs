// Сверка облика отряда (В16) с трекером: shared/golden/looks.json (пишет tracker/tools/export-look-golden.mjs).
// Styles.cs (KitSets.Guess, Styles.Guess) и ArmyFile.DefaultStyle обязаны совпасть с looks.js трекера.
// Запуск: dotnet run -c Release -- looks
using Journal.Armies;
using Journal.Art;
using Newtonsoft.Json.Linq;

static class Looks
{
    public static int Check()
    {
        string dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "shared", "golden", "looks.json"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) { Console.WriteLine("не найден shared/golden/looks.json"); return 2; }
        var g = JObject.Parse(File.ReadAllText(Path.Combine(dir, "shared", "golden", "looks.json")));
        int n = 0, bad = 0;
        void Cmp(string what, string want, string got)
        {
            n++;
            if (want == got) return;
            if (++bad <= 30) Console.WriteLine($"✘ {what}: трекер {want ?? "—"}, Unity {got ?? "—"}");
        }
        foreach (JArray r in g["kits"]) Cmp($"снаряжение «{r[0]}» {r[1]}/{r[2]}", (string)r[3], KitSets.Guess((string)r[0], (string)r[1], (string)r[2]));
        foreach (JArray r in g["styles"]) Cmp($"стиль «{r[0]}»", (string)r[1], Styles.Guess((string)r[0]));
        var f = ArmyFile.New();
        f.Root["units"] = g["units"].DeepClone();
        foreach (JArray r in g["defaults"])
        {
            int? fid = r[0].Type == JTokenType.Null ? null : (int)r[0];
            var except = r[2].Type == JTokenType.Null ? null : f.Unit((int)r[2]);
            Cmp($"стиль нового «{r[1]}» во фракции {r[0]}", (string)r[3], f.DefaultStyle(fid, (string)r[1], except));
        }
        Console.WriteLine(bad == 0 ? $"облик: все {n} случаев совпали с трекером" : $"облик: расхождений {bad} из {n}");
        return bad == 0 ? 0 : 1;
    }
}
