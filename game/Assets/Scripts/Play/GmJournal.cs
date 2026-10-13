// ═══════════ GmJournal.cs — журнал ГМа (Алекс 13.10): все записи боя по ходам и за весь бой ═══════════
// Слева — «Весь бой» и ходы, справа — что было: потери сторон (сводка хода), расчёты, события, решения ИИ, правки ГМа.
// Расчёты — строки журнала движка, начинающиеся с «ƒ» (договор с чатом движка: разбор удара — тот же текст, что у стола,
// с настоящим броском и модификаторами), и строки с формулой («=», «×», d100). Сами числа игра не считает — только
// показывает, что записал движок. «В файл» — текстом рядом с игрой (папка «Журналы»), «Копировать» — в буфер.
// Журнал ГМа — в виде ГМа: глазами стороны он показал бы чужое.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BattleCore;
using UnityEngine;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class GmJournal
    {
        enum Kind { Head, Sum, Calc, CalcSub, Event, Ai, Gm, Note }
        static readonly (Kind k, string name)[] Filters = { (Kind.Calc, "Расчёты"), (Kind.Event, "События"), (Kind.Ai, "ИИ"), (Kind.Gm, "Правки ГМа") };
        const int MaxLines = 2500;   // больше — в файл: тысячи надписей UI Toolkit тянет плохо

        readonly PlayController pc;
        readonly VisualElement root, turnList;
        readonly ScrollView body;
        readonly Label status;
        readonly TextField search;
        readonly HashSet<Kind> on = new HashSet<Kind> { Kind.Calc, Kind.Event, Kind.Ai, Kind.Gm };
        int turn;          // 0 — весь бой
        string builtKey;
        public bool Visible => !root.ClassListContains("hidden");

        public GmJournal(VisualElement parent, PlayController pc)
        {
            this.pc = pc;
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("gmj-box"); root.Add(box);
            var head = new VisualElement(); head.AddToClassList("gmj-top"); box.Add(head);
            var title = new Label("Журнал ГМа"); title.AddToClassList("gmj-title"); head.Add(title);
            var close = new Button(Hide) { text = "✕" }; close.AddToClassList("gmj-close"); close.tooltip = "Закрыть (Esc, J)"; head.Add(close);

            var bar = new VisualElement(); bar.AddToClassList("gmj-bar"); box.Add(bar);
            foreach (var (k, name) in Filters)
            {
                var kk = k; Button b = null;
                b = new Button(() => { if (!on.Remove(kk)) on.Add(kk); b.EnableInClassList("is-on", on.Contains(kk)); builtKey = null; }) { text = name };
                b.AddToClassList("me-btn"); b.AddToClassList("is-on"); bar.Add(b);
            }
            search = new TextField { value = "" }; search.textEdition.placeholder = "поиск: отряд, слово…"; search.textEdition.hidePlaceholderOnFocus = true; search.AddToClassList("gmj-search"); search.tooltip = "Искать в журнале: имя отряда, слово";
            search.RegisterValueChangedCallback(_ => builtKey = null); bar.Add(search);
            var sp = new VisualElement(); sp.style.flexGrow = 1; bar.Add(sp);
            var copy = new Button(() => { GUIUtility.systemCopyBuffer = Text(); status.text = "Скопировано в буфер обмена"; }) { text = "Копировать" }; copy.AddToClassList("me-btn"); bar.Add(copy);
            var file = new Button(Save) { text = "В файл" }; file.AddToClassList("me-btn"); bar.Add(file);

            var row = new VisualElement(); row.AddToClassList("gmj-row"); box.Add(row);
            var turns = new ScrollView(ScrollViewMode.Vertical); turns.AddToClassList("gmj-turns"); row.Add(turns);
            turnList = turns.contentContainer;
            body = new ScrollView(ScrollViewMode.Vertical); body.AddToClassList("gmj-body"); row.Add(body);
            status = new Label(); status.AddToClassList("gmj-status"); box.Add(status);
        }

        public void Show() { turn = Math.Max(0, pc.Session?.Logs.Count ?? 0); builtKey = null; status.text = ""; root.RemoveFromClassList("hidden"); Tick(); }
        public void Hide() { search.Blur(); root.AddToClassList("hidden"); }
        public void Toggle() { if (Visible) Hide(); else Show(); }

        // каждый кадр, пока открыт: перестроить, если пришёл новый ход, правка ГМа или сменился фильтр
        public void Tick()
        {
            if (!Visible) return;
            var s = pc.Session;
            if (s == null) { Hide(); return; }
            string key = $"{s.Logs.Count}|{pc.Summaries.Count}|{pc.GmLog.Count}|{s.Outcome}|{turn}|{string.Join(",", on)}|{search.value}|{pc.ViewSide}";
            if (key == builtKey) return;
            builtKey = key;
            if (turn > s.Logs.Count) turn = s.Logs.Count;
            Turns(s);
            body.Clear();
            if (pc.ViewSide > 0)
            {
                body.Add(Line("Журнал ГМа — только в виде ГМа: глазами стороны он показал бы чужие отряды и броски. Вид меняется вкладкой «Вид» внизу.", "gmj-note"));
                return;
            }
            var lines = Lines(s, turn).Where(Pass).ToList();
            int n = 0;
            foreach (var (k, t) in lines)
            {
                if (n++ >= MaxLines) { body.Add(Line($"…ещё {lines.Count - MaxLines} строк — полностью «В файл»", "gmj-note")); break; }
                body.Add(Line(t, "gmj-" + k.ToString().ToLowerInvariant()));
            }
            if (lines.Count == 0) body.Add(Line(s.Logs.Count == 0 ? "Ходов ещё не было — жми «Ход!»." : "Ничего не подходит под фильтр.", "gmj-note"));
            body.scrollOffset = Vector2.zero;
        }

        void Turns(BattleSession s)
        {
            turnList.Clear();
            void T(int t, string name)
            {
                var b = new Label(name); b.AddToClassList("gmj-turn"); b.EnableInClassList("is-on", t == turn);
                b.RegisterCallback<ClickEvent>(_ => { turn = t; builtKey = null; }); turnList.Add(b);
            }
            T(0, "Весь бой");
            for (int t = s.Logs.Count; t >= 1; t--) T(t, $"Ход {t}");   // свежий — сверху
        }

        bool Pass((Kind k, string t) l)
        {
            var f = l.k == Kind.CalcSub ? Kind.Calc : l.k;
            if (f == Kind.Calc || f == Kind.Event || f == Kind.Ai || f == Kind.Gm) { if (!on.Contains(f)) return false; }
            var q = search.value?.Trim();
            return string.IsNullOrEmpty(q) || l.k == Kind.Head || l.t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Kind Classify(string line)
        {
            if (line.StartsWith("ƒ")) return Kind.Calc;
            if (line.StartsWith("ИИ:")) return Kind.Ai;
            if (line.Contains(" = ") || line.Contains("×") || line.Contains("d100") || line.Contains("Шанс попасть")) return Kind.Calc;
            return Kind.Event;
        }

        // строки журнала: t = 0 — весь бой (итог сверху, потом все ходы по порядку), иначе один ход
        IEnumerable<(Kind, string)> Lines(BattleSession s, int t)
        {
            if (t == 0)
            {
                yield return (Kind.Head, $"«{pc.Game?.Name}» — сыграно ходов: {s.Logs.Count}");
                if (!string.IsNullOrEmpty(s.Outcome)) yield return (Kind.Sum, "Итог: " + s.Outcome);
                foreach (var side in s.Sides)
                {
                    double lost = 0, killed = 0, wounded = 0, now = -1;
                    foreach (var sum in pc.Summaries) if (sum.Sides.TryGetValue(side, out var v)) { lost += v.lost; killed += v.killed; wounded += v.wounded; now = v.now; }
                    if (now >= 0) yield return (Kind.Sum, $"{s.Name(side)}: потеряно за бой {lost:0} (убито {killed:0}, ранено {wounded:0}) · в строю {now:0}");
                }
                foreach (var g in pc.GmLog.Where(g => !g.StartsWith("ход "))) yield return (Kind.Gm, "ГМ: " + g);
                for (int k = 1; k <= s.Logs.Count; k++) foreach (var l in Turn(s, k)) yield return l;
                string next = $"ход {s.Logs.Count + 1}: ";   // правки и итог после последнего хода
                var after = pc.GmLog.Where(g => g.StartsWith(next)).ToList();
                if (after.Count > 0) { yield return (Kind.Head, $"После хода {s.Logs.Count}"); foreach (var g in after) yield return (Kind.Gm, "ГМ: " + g.Substring(next.Length)); }
                yield break;
            }
            foreach (var l in Turn(s, t)) yield return l;
        }

        IEnumerable<(Kind, string)> Turn(BattleSession s, int t)
        {
            yield return (Kind.Head, $"Ход {t}");
            var sum = pc.Summaries.FirstOrDefault(x => x.Turn == t);
            if (sum != null)
                foreach (var side in s.Sides)
                    if (sum.Sides.TryGetValue(side, out var v))
                        yield return (Kind.Sum, $"{s.Name(side)}: " + (v.lost >= 1 ? $"−{v.lost:0} (убито {v.killed:0}, ранено {v.wounded:0})" : "без потерь") + $" · в строю {v.now:0}");
            string pre = $"ход {t}: ";
            foreach (var g in pc.GmLog.Where(g => g.StartsWith(pre))) yield return (Kind.Gm, "ГМ перед ходом: " + g.Substring(pre.Length));
            var log = t - 1 < s.Logs.Count ? s.Logs[t - 1] : null;
            if (log == null) yield break;
            bool calc = false;
            foreach (var line in log)
            {
                var k = Classify(line); calc |= k == Kind.Calc;
                // «ƒ   …» — строка внутри окна удара (отступ движка), «ƒ …» — заголовок окна, итог, проверка
                if (line.StartsWith("ƒ")) { string r = line.Substring(1); yield return (r.StartsWith("   ") ? Kind.CalcSub : Kind.Calc, r.Trim()); }
                else yield return (k, line);
            }
            if (!calc) yield return (Kind.Calc, "Разбора ударов по формуле движок в этот ход не записал — ход прошёл без ударов и залпов.");
        }

        static Label Line(string text, string cls) { var l = new Label(text); l.AddToClassList("gmj-line"); l.AddToClassList(cls); l.selection.isSelectable = true; return l; }

        // весь журнал текстом — по текущему выбору хода и фильтрам
        string Text()
        {
            var s = pc.Session; if (s == null) return "";
            var sb = new StringBuilder();
            foreach (var (k, t) in Lines(s, turn).Where(Pass))
            {
                if (k == Kind.Head) sb.AppendLine().AppendLine("══ " + t + " ══");
                else sb.AppendLine(k == Kind.Calc ? "  ƒ " + t : k == Kind.CalcSub ? "      " + t : k == Kind.Ai || k == Kind.Event || k == Kind.Gm ? "  " + t : t);
            }
            return sb.ToString().TrimStart();
        }

        void Save()
        {
            try
            {
                string dir = Path.Combine(Application.isEditor ? Path.Combine(Application.dataPath, "..", "Temp") : Path.GetDirectoryName(Application.dataPath), "Журналы");
                Directory.CreateDirectory(dir);
                string name = string.Concat((pc.Game?.Name ?? "битва").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                string f = Path.Combine(dir, $"{name} — {(turn == 0 ? "весь бой" : $"ход {turn}")} — {DateTime.Now:yyyy-MM-dd HH-mm}.txt");
                File.WriteAllText(f, Text(), new UTF8Encoding(true));
                status.text = "Записано: " + Path.GetFullPath(f);
                Application.OpenURL("file:///" + Path.GetFullPath(dir).Replace('\\', '/'));
            }
            catch (Exception e) { status.text = "Не записалось: " + e.Message; }
        }
    }

    // ── капитуляция (Алекс 13.10): одна из сторон сдаётся — битва кончается, победа — другой стороне ──
    public sealed class SurrenderDialog
    {
        readonly PlayController pc;
        readonly VisualElement root, list;
        readonly Label note;
        public bool Visible => !root.ClassListContains("hidden");

        public SurrenderDialog(VisualElement parent, PlayController pc)
        {
            this.pc = pc;
            root = new VisualElement(); root.AddToClassList("menu"); root.AddToClassList("hidden"); parent.Add(root);
            var box = new VisualElement(); box.AddToClassList("panel"); box.AddToClassList("menu-box"); root.Add(box);
            var title = new Label("🏳 Капитуляция"); title.AddToClassList("menu-title"); box.Add(title);
            note = new Label(); note.AddToClassList("menu-item-note"); note.style.marginBottom = 10; box.Add(note);
            list = new VisualElement(); box.Add(list);
            var cancel = new Button(Hide) { text = "Отмена" }; cancel.AddToClassList("menu-close"); box.Add(cancel);
        }

        public void Hide() => root.AddToClassList("hidden");
        public void Show()
        {
            var s = pc.Session; if (s == null) return;
            list.Clear();
            bool can = pc.CanSurrender;
            note.text = can ? "Какая сторона сдаётся? Битва кончится сразу; итог — как после боя. Передумали — «Откатить ход»."
                            : pc.Phase == PlayPhase.Over ? "Битва уже окончена." : "Сдаться можно между ходами — дождись конца хода.";
            if (can)
                foreach (var side in s.Sides)
                {
                    int sd = side;
                    double men = s.UnitsOf(sd).Where(PlayController.Present).Sum(m => m.P.U.Soldiers);
                    var item = new VisualElement(); item.AddToClassList("menu-item"); item.AddToClassList("side-" + sd);
                    var n = new Label($"🏳 Сдаётся «{s.Name(sd)}»"); n.AddToClassList("menu-item-name"); item.Add(n);
                    var d = new Label($"в строю {men:0}"); d.AddToClassList("menu-item-note"); item.Add(d);
                    bool asked = false;
                    item.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (!asked) { asked = true; d.text = "Точно? Нажми ещё раз — битва кончится"; item.AddToClassList("menu-end"); return; }
                        Hide(); pc.Surrender(sd);
                    });
                    list.Add(item);
                }
            root.RemoveFromClassList("hidden");
        }
    }
}
