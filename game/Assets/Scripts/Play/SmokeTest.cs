// ═══════════ SmokeTest.cs — проверка сборки без рук: Journal.exe -smoke -logFile run.log ═══════════
// Берёт сохранение из Saves (ход 5, если есть), малый состав по 3 отряда, всем — «атаковать ближайшего врага», один ход
// на ×4 и пишет в журнал итог: «SMOKE OK …» или «SMOKE FAIL …», потом выходит. Нужна, чтобы сборку для ГМа проверить
// целиком — загрузка сохранения, расстановка, счёт хода движком и показ — без редактора.
using System;
using System.Collections;
using System.Linq;
using BattleCore;
using UnityEngine;

namespace Journal.Play
{
    public static class SmokeTest
    {
        public static bool On => Environment.GetCommandLineArgs().Contains("-smoke");

        public static IEnumerator Run(PlayController pc, LineupPanel lineup, MainMenu menu)
        {
            yield return null;
            var saves = PlayScenarios.Saves();
            string path = saves.FirstOrDefault(p => p.Contains("hod5")) ?? saves.FirstOrDefault();
            Debug.Log($"SMOKE: сохранений {saves.Count}, беру {path}");
            if (path == null) { Fail("нет сохранений в Saves"); yield break; }
            menu.Hide();
            if (!lineup.Show(path)) { Fail("сохранение не открылось"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            try { lineup.StartNow(); } catch (Exception e) { Fail("битва не построилась: " + e.Message); yield break; }
            if (pc.Game == null) { Fail("битвы нет"); yield break; }
            var ms = pc.Battle.Movers; int orders = 0;
            foreach (var m in ms)
            {
                if (!PlayController.Present(m)) continue;
                var foe = ms.Where(e => PlayController.Present(e) && PlayController.SideOf(e) != PlayController.SideOf(m))
                            .OrderBy(e => (e.P.X - m.P.X) * (e.P.X - m.P.X) + (e.P.Y - m.P.Y) * (e.P.Y - m.P.Y)).FirstOrDefault();
                if (foe != null && pc.Order(m, new MoveOrder { Kind = OrderKind.Attack, TargetId = foe.P.U.Id }, true)) orders++;
            }
            double before = ms.Sum(m => m.P.U.Soldiers);
            pc.Speed = 4; pc.Go();
            Debug.Log($"SMOKE: «{pc.Game.Name}», отрядов {ms.Count}, бойцов {before:0}, приказов {orders}; ход пошёл");
            float tg = Time.realtimeSinceStartup;
            while (pc.Phase == PlayPhase.Showing && Time.realtimeSinceStartup - tg < 180) yield return null;
            if (pc.Phase == PlayPhase.Showing) { Fail("ход не кончился за 180 с"); yield break; }
            double after = ms.Sum(m => m.P.U.Soldiers);
            Debug.Log($"SMOKE OK: ход за {Time.realtimeSinceStartup - tg:0.0} с, выбыло {before - after:0} из {before:0}, кадров записи {pc.ComputedTime / 0.2 + 1:0}; всего {Time.realtimeSinceStartup - t0:0.0} с");
            Application.Quit();
        }
        static void Fail(string why) { Debug.LogError("SMOKE FAIL: " + why); Application.Quit(1); }
    }
}
