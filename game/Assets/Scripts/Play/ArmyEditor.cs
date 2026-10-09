// ═══════════ ArmyEditor.cs — редактор армий (Г93, перенос трекера, шаг 1): фракции, полководцы, отряды ═══════════
// Окно поверх игры: слева — фракции (цвет, подфракции) и их полководцы; посередине — отряды выбранной фракции
// карточками; справа — свойства выбранного отряда или полководца. Файл — сохранение трекера (ArmyFile): открыть
// из game/Saves или с рабочего стола, править, сохранить (первая запись поверх — с копией .bak). Создание отряда —
// по шаблону, облик (стиль и снаряжение, В16) выбирается сразу; клон — как в трекере. Правка — сразу в файл в памяти,
// на диск — по «Сохранить». Боевой математики нет: только данные армий.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleCore;
using Journal.Armies;
using Journal.Art;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Journal.Play
{
    public sealed class ArmyEditor
    {
        // папка сохранений игры — как её назвать игроку: в редакторе game/Saves, в сборке — Saves рядом с exe
        static string SavesName => UnityEngine.Application.isEditor ? "game/Saves" : "Saves (рядом с игрой)";
        public readonly VisualElement Root;
        public bool Visible => !Root.ClassListContains("hidden");
        public event Action<string> PlayFile;        // «В бой ⚔» с этим файлом — выбор противника и карты (ArmyBattlePanel)
        public event Action Closed;                   // окно закрыли — игра возвращается в главное меню

        ArmyFile file;
        int? factionId;                               // выбранная фракция (null — без фракции)
        bool factionChosen;
        JObject unit, cmdr;                           // что открыто справа
        bool unitChanged;
        readonly List<JObject> fresh = new List<JObject>();   // созданы в этом окне — в журнал, когда уже названы
        // выбрано руками — по названию больше не подставлять: облик, род войск, числа, численность
        readonly HashSet<JObject> stylePicked = new HashSet<JObject>(), kitPicked = new HashSet<JObject>(), typePicked = new HashSet<JObject>(), numsPicked = new HashSet<JObject>(), menPicked = new HashSet<JObject>();
        string pendingConfirm; float confirmUntil;    // «нажми ещё раз» для опасных действий

        readonly Label fileLabel, status, unitsTitle;
        readonly VisualElement factionList, factionBox, cmdrList, cards, form, filePopup;
        readonly TextField saveAsName;

        static readonly (string Id, string Name)[] Types = { ("infantry", "Пехота"), ("cavalry", "Конница"), ("archer", "Стрелки"), ("pike", "Пикинёры") };
        static readonly (string Id, string Name)[] Weapons = { ("melee", "Ближний бой"), ("ranged", "Стрелковое") };
        static readonly (string Id, string Name)[] Statuses = { ("active", "В строю"), ("fled", "Бежал"), ("destroyed", "Уничтожен") };
        static readonly string[] Palette = { "#b5372b", "#2f63a8", "#2f7d4a", "#c9a227", "#8300b3", "#1d8dd3", "#707070", "#ff5c5c", "#574672", "#e0e0e0", "#202020", "#c8662c" };

        public ArmyEditor(VisualElement parent)
        {
            Root = new VisualElement(); Root.AddToClassList("army"); Root.AddToClassList("hidden");
            parent.Add(Root);
            // верх: файл
            var top = Div("army-top", Root);
            var title = new Label("Армии"); title.AddToClassList("army-title"); top.Add(title);
            fileLabel = new Label(); fileLabel.AddToClassList("army-file"); top.Add(fileLabel);
            Btn(top, "Открыть ▾", ToggleFiles);
            Btn(top, "Новый файл", () => Confirm("new", "Есть несохранённые правки — нажми «Новый файл» ещё раз, чтобы начать без них", () => SetFile(ArmyFile.New())));
            Btn(top, "Сохранить", Save, "is-gold");
            saveAsName = new TextField { isDelayed = false }; saveAsName.AddToClassList("army-saveas"); saveAsName.tooltip = "Имя файла в " + SavesName; top.Add(saveAsName);
            Btn(top, "Сохранить как", SaveAs);
            Btn(top, "В бой ⚔", ToBattle, "is-gold");
            var sp = new VisualElement(); sp.style.flexGrow = 1; top.Add(sp);
            Btn(top, "Закрыть ✕", () => Confirm("close", "Есть несохранённые правки — «Сохранить» или нажми «Закрыть» ещё раз, чтобы выйти без них", Hide));
            filePopup = Div("army-files hidden", Root);
            // тело: три колонки
            var body = Div("army-body", Root);
            var left = Div("army-col army-left", body);
            var hf = Div("army-head", left); Lbl(hf, "Фракции", "army-h"); Btn(Div("army-head-btns", hf), "+ Фракция", AddFaction, "small");
            factionList = Div("army-list", left);
            factionBox = Div("army-faction", left);
            var hc = Div("army-head", left); Lbl(hc, "Полководцы", "army-h"); Btn(Div("army-head-btns", hc), "+ Полководец", AddCommander, "small");
            cmdrList = Div("army-list", left);
            var mid = Div("army-col army-mid", body);
            var hu = Div("army-head", mid); unitsTitle = Lbl(hu, "Отряды", "army-h");
            var hb = Div("army-head-btns", hu);
            Btn(hb, "+ Отряд", AddUnit, "small is-gold"); Btn(hb, "Клон", CloneUnit, "small"); Btn(hb, "Удалить", RemoveUnit, "small is-bad");
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("army-scroll"); mid.Add(scroll);
            cards = Div("army-cards", scroll.contentContainer);
            var right = Div("army-col army-right", body);
            var fs = new ScrollView(ScrollViewMode.Vertical); fs.AddToClassList("army-scroll"); right.Add(fs);
            form = Div("army-form", fs.contentContainer);
            status = new Label(); status.AddToClassList("army-status"); Root.Add(status);
        }

        // ── открыть / закрыть ──
        public void Show(string path = null)
        {
            Root.RemoveFromClassList("hidden");
            if (path != null) TryOpen(path);
            else if (file == null) SetFile(ArmyFile.New());
            Rebuild();
        }
        public void Hide() { FlushUnitLog(); Root.AddToClassList("hidden"); filePopup.AddToClassList("hidden"); Closed?.Invoke(); }
        // в бой этой армией: сначала на диск (бой читает файл), потом выбор противника и карты
        void ToBattle()
        {
            if (file.Path == null) { Say("Сначала «Сохранить как» — бой берёт армию из файла", true); return; }
            if (file.Dirty) { try { file.Save(); } catch (Exception e) { Say("Не сохранилось: " + e.Message, true); return; } }
            FlushUnitLog(); Root.AddToClassList("hidden"); filePopup.AddToClassList("hidden");
            PlayFile?.Invoke(file.Path);
        }

        void SetFile(ArmyFile f)
        {
            file = f; unit = null; cmdr = null; factionChosen = false; unitChanged = false;
            var first = file.Factions.OfType<JObject>().FirstOrDefault();
            factionId = first != null ? (int?)(int)first["id"] : null;
            saveAsName.SetValueWithoutNotify(file.Path != null ? Path.GetFileNameWithoutExtension(file.Path) : "армия");
            filePopup.AddToClassList("hidden");
            Rebuild();
            Say(file.Path != null ? $"Открыт «{Path.GetFileName(file.Path)}»: фракций {file.Factions.Count}, отрядов {file.Units.Count}" : "Новый файл армий");
        }
        void TryOpen(string path)
        {
            try { SetFile(ArmyFile.Load(path)); }
            catch (Exception e) { Say("Не открылся: " + e.Message, true); }
        }
        void ToggleFiles()
        {
            if (!filePopup.ClassListContains("hidden")) { filePopup.AddToClassList("hidden"); return; }
            filePopup.Clear();
            var pick = Div("army-file-row", filePopup); Lbl(pick, "📂 Открыть файл…", "army-file-name"); Lbl(pick, "любая папка", "army-file-where");
            pick.RegisterCallback<ClickEvent>(_ => Confirm("pick", "Есть несохранённые правки — нажми ещё раз, чтобы открыть другой файл без них", () =>
            {
                var f = FileDialog.OpenSave(FileDialog.LastDir());
                if (f != null) { FileDialog.Remember(f); TryOpen(f); }
            }));
            var list = ArmyFile.Find(FileDialog.Recent());
            if (list.Count == 0) Lbl(filePopup, $"Файлов нет: положи сохранения трекера в {SavesName} или на рабочий стол (armiya_hodN.txt)", "army-note");
            foreach (var p in list)
            {
                var row = Div("army-file-row", filePopup);
                Lbl(row, Path.GetFileName(p), "army-file-name");
                Lbl(row, p.StartsWith(ArmyFile.SavesDir) ? SavesName : Path.GetDirectoryName(p), "army-file-where");
                var pp = p;
                row.RegisterCallback<ClickEvent>(_ => Confirm("open:" + pp, "Есть несохранённые правки — нажми на файл ещё раз, чтобы открыть без них", () => TryOpen(pp)));
            }
            filePopup.RemoveFromClassList("hidden");
        }
        void Save()
        {
            if (file.Path == null) { SaveAs(); return; }
            FlushUnitLog();
            try { file.Save(); Say($"Сохранено: {Path.GetFileName(file.Path)} (прежний — рядом, .bak)"); }
            catch (Exception e) { Say("Не сохранилось: " + e.Message, true); }
            Rebuild();
        }
        void SaveAs()
        {
            var name = (saveAsName.value ?? "").Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name == "") { Say("Впиши имя файла", true); return; }
            if (!name.EndsWith(".txt")) name += ".txt";
            FlushUnitLog();
            try { file.Save(Path.Combine(ArmyFile.SavesDir, name)); Say($"Сохранено: {SavesName}/{name}"); }
            catch (Exception e) { Say("Не сохранилось: " + e.Message, true); }
            Rebuild();
        }
        // опасное действие при несохранённых правках — по второму нажатию
        void Confirm(string key, string warn, Action act)
        {
            if (file == null || !file.Dirty || pendingConfirm == key && Time.unscaledTime < confirmUntil) { pendingConfirm = null; act(); return; }
            pendingConfirm = key; confirmUntil = Time.unscaledTime + 4; Say(warn, true);
        }
        void Say(string s, bool bad = false) { status.text = s; status.EnableInClassList("is-bad", bad); }

        // ── перерисовать всё ──
        void Rebuild()
        {
            if (file == null) return;
            fileLabel.text = (file.Path != null ? Path.GetFileName(file.Path) : "новый, не сохранён") + (file.Dirty ? " ●" : "") + $" · ход {file.Turn}";
            fileLabel.EnableInClassList("is-dirty", file.Dirty);
            Factions(); FactionBox(); Commanders(); Cards(); Form();
        }

        void Factions()
        {
            factionList.Clear();
            var rows = file.Factions.OfType<JObject>().Select(f => ((int?)(int)f["id"], (string)f["name"], (string)f["color"])).ToList();
            if (file.UnitsOf(null).Any() || rows.Count == 0) rows.Add((null, "Без фракции", "#6e6a62"));
            foreach (var (id, name, color) in rows)
            {
                var row = Div("army-row", factionList);
                var sw = Div("army-swatch", row); sw.style.backgroundColor = Hex(color);
                Lbl(row, name, "army-row-name");
                Lbl(row, $"{file.UnitsOf(id).Count()} отр. · {file.UnitsOf(id).Sum(u => (double?)u["soldiers"] ?? 0):0}", "army-row-sub");
                row.EnableInClassList("is-on", id == factionId);
                var fid = id;
                row.RegisterCallback<ClickEvent>(_ => { FlushUnitLog(); factionId = fid; factionChosen = true; unit = null; cmdr = null; Rebuild(); });
            }
        }
        void FactionBox()
        {
            factionBox.Clear();
            var f = file.Faction(factionId); if (f == null) return;
            var name = new TextField("Название") { isDelayed = true, value = (string)f["name"] };
            name.RegisterValueChangedCallback(e => { var v = e.newValue.Trim(); if (v == "") return; f["name"] = v; file.Dirty = true; Rebuild(); });
            factionBox.Add(name);
            var pal = Div("army-palette", factionBox);
            foreach (var c in Palette)
            {
                var sw = Div("army-swatch big", pal); sw.style.backgroundColor = Hex(c);
                sw.EnableInClassList("is-on", string.Equals((string)f["color"], c, StringComparison.OrdinalIgnoreCase));
                var cc = c; sw.RegisterCallback<ClickEvent>(_ => { f["color"] = cc; file.Dirty = true; Rebuild(); });
            }
            var hex = new TextField("Цвет #") { isDelayed = true, value = (string)f["color"] };
            hex.RegisterValueChangedCallback(e => { if (ColorUtility.TryParseHtmlString(e.newValue, out _)) { f["color"] = e.newValue.StartsWith("#") ? e.newValue : "#" + e.newValue; file.Dirty = true; Rebuild(); } });
            factionBox.Add(hex);
            // подфракции
            var subs = file.SubfactionsOf(factionId).ToList();
            if (subs.Count > 0) Lbl(factionBox, "Подфракции: " + string.Join(", ", subs.Select(s => (string)s["name"])), "army-note");
            var addSub = new TextField("+ Подфракция") { isDelayed = true };
            addSub.RegisterValueChangedCallback(e => { var v = e.newValue.Trim(); if (v == "") return; file.AddSubfaction((int)f["id"], v); Rebuild(); });
            factionBox.Add(addSub);
            Btn(factionBox, "Распустить фракцию", () => ConfirmAlways("delf", $"Распустить «{(string)f["name"]}»? Отряды и полководцы останутся без фракции. Нажми ещё раз.",
                () => { file.RemoveFaction((int)f["id"]); factionId = null; unit = null; Rebuild(); }), "small is-bad");
        }
        // подтверждение вторым нажатием — всегда (удаление)
        void ConfirmAlways(string key, string warn, Action act)
        {
            if (pendingConfirm == key && Time.unscaledTime < confirmUntil) { pendingConfirm = null; act(); return; }
            pendingConfirm = key; confirmUntil = Time.unscaledTime + 4; Say(warn, true);
        }
        void Commanders()
        {
            cmdrList.Clear();
            foreach (var c in file.CommandersOf(factionId))
            {
                var row = Div("army-row", cmdrList);
                Lbl(row, (string)c["name"], "army-row-name");
                Lbl(row, BuffText(c), "army-row-sub");
                row.EnableInClassList("is-on", c == cmdr);
                var cc = c; row.RegisterCallback<ClickEvent>(_ => { FlushUnitLog(); cmdr = cc; unit = null; Rebuild(); });
            }
        }
        static string BuffText(JObject c)
        {
            var parts = new List<string>();
            void B(string k, string n) { var v = (double?)c[k] ?? 0; if (Math.Abs(v) > 1e-9) parts.Add($"{n} {(v > 0 ? "+" : "")}{v:0.#}"); }
            B("buffMorale", "БД"); B("buffDisc", "дисц"); B("buffDmg", "урон"); B("buffDef", "защ");
            return parts.Count > 0 ? string.Join(" · ", parts) : "без бонусов";
        }

        void Cards()
        {
            cards.Clear();
            unitsTitle.text = $"Отряды · {file.FactionName(factionId)}";
            foreach (var u in file.UnitsOf(factionId))
            {
                var card = Div("army-card", cards);
                card.style.borderTopColor = Hex((string)file.Faction(factionId)?["color"] ?? "#6e6a62");   // кромка — цвет фракции
                string kit = ArmyFile.KitOf(u), style = ArmyFile.StyleOf(u);
                var ic = new Icon(Icon.OfType(KitSets.TplOf(kit), (string)u["type"])); ic.AddToClassList("army-card-icon"); card.Add(ic);
                Lbl(card, (string)u["name"], "army-card-name");
                Lbl(card, $"{(double?)u["soldiers"] ?? 0:0}", "army-card-men");
                Lbl(card, $"{KitSets.NameOf(kit)} · {Styles.NameOf(style)}", "army-card-sub");
                var st = (string)u["status"];
                if (st == "fled" || st == "destroyed") Lbl(card, st == "fled" ? "БЕЖАЛ" : "УНИЧТОЖЕН", "army-card-flag");
                if (u["style"] == null || u["kit"] == null) card.tooltip = "Облик угадан по имени — выбери справа, чтобы записать";
                card.EnableInClassList("is-on", u == unit);
                var uu = u; card.RegisterCallback<ClickEvent>(_ => { FlushUnitLog(); unit = uu; cmdr = null; Rebuild(); });
            }
            if (!file.UnitsOf(factionId).Any()) Lbl(cards, "Отрядов нет — «+ Отряд» создаст по шаблону", "army-note");
        }

        // ── справа: отряд или полководец ──
        void Form()
        {
            form.Clear();
            if (unit != null) UnitForm(unit);
            else if (cmdr != null) CommanderForm(cmdr);
            else Lbl(form, "Выбери отряд или полководца. Новый отряд: «+ Отряд» — по шаблону, облик выбирается тут же.", "army-note");
        }
        void UnitForm(JObject u)
        {
            Lbl(form, "Отряд", "army-h");
            Text("Имя", (string)u["name"], v => { u["name"] = v; if (fresh.Contains(u)) Reguess(u); Changed(); });
            var tpls = new List<string> { "— шаблон: заполнить числа —" }; tpls.AddRange(Templates.Base.Select(t => t.Name));
            var tdd = new DropdownField("Шаблон", tpls, 0);
            tdd.RegisterValueChangedCallback(e =>
            {
                var b = Templates.Base.FirstOrDefault(x => x.Name == e.newValue); if (b == null) return;
                var t = file.Resolve(b.Id, ArmyFile.Id(u["factionId"]));   // с правками партии и фракции, как в трекере
                ArmyFile.Apply(u, t); numsPicked.Add(u); typePicked.Add(u);
                if (Kits.LookByTpl.TryGetValue(t.Id, out var look)) { u["kit"] = look; kitPicked.Add(u); }
                bool own = Templates.Stats.Any(k => Math.Abs(t[k] - b[k]) > 1e-9);
                Changed(); Say($"Числа и снаряжение — по шаблону «{t.Name}»" + (own ? " с правками фракции" : ""));
            });
            form.Add(tdd);
            Choice("Род войск", Types, (string)u["type"], v => { u["type"] = v; typePicked.Add(u); Changed(); });
            Choice("Оружие", Weapons, (string)u["weapon"], v => { u["weapon"] = v; typePicked.Add(u); Changed(); });
            Lbl(form, "Облик", "army-h2");
            Choice("Снаряжение", KitSets.All.Select(k => (k.Id, k.Name)).ToArray(), ArmyFile.KitOf(u), v => { u["kit"] = v; kitPicked.Add(u); Changed(); });
            Choice("Стиль", Styles.All.Select(s => (s.Id, s.Name)).ToArray(), ArmyFile.StyleOf(u), v => { u["style"] = v; stylePicked.Add(u); Changed(); });
            Lbl(form, Styles.All.First(s => s.Id == ArmyFile.StyleOf(u)).Note, "army-note");
            if (u["style"] == null || u["kit"] == null) Lbl(form, "В файле облика нет — показан угаданный по имени; выбери, чтобы записать", "army-note warn");
            Lbl(form, "Числа", "army-h2");
            Number("Бойцов", u, "soldiers", 1, 1e6);
            Number("Дисциплина", u, "discipline", 1, 100);
            Number("Боевой дух", u, "morale", 0, 150);
            Number("Атака снаряжения", u, "eqAtk", 0, 1000);
            Number("Защита снаряжения", u, "eqDef", 0, 1000);
            Number("Опыт", u, "exp", 0, 100);
            Number("Мастерство", u, "mastery", 0, 1000);
            Number("Усталость, %", u, "fatigue", 0, 100);
            if ((string)u["weapon"] == "ranged") Number("Дальность, м (0 — по роду)", u, "range", 0, 2000);
            Number("Лестниц", u, "ladders", 0, 100);
            Lbl(form, "Принадлежность", "army-h2");
            var facs = new List<(string, string)> { ("", "Без фракции") }; facs.AddRange(file.Factions.OfType<JObject>().Select(f => (((int)f["id"]).ToString(), (string)f["name"])));
            Choice("Фракция", facs.ToArray(), u["factionId"]?.Type == JTokenType.Integer ? ((int)u["factionId"]).ToString() : "", v =>
            {
                u["factionId"] = v == "" ? null : (JToken)int.Parse(v); u["subfactionId"] = null; u["commanderId"] = null;
                Changed(); factionId = v == "" ? null : int.Parse(v); Rebuild();
            });
            var subs = new List<(string, string)> { ("", "нет") }; subs.AddRange(file.SubfactionsOf(factionId).Select(s => (((int)s["id"]).ToString(), (string)s["name"])));
            if (subs.Count > 1) Choice("Подфракция", subs.ToArray(), u["subfactionId"]?.Type == JTokenType.Integer ? ((int)u["subfactionId"]).ToString() : "", v => { u["subfactionId"] = v == "" ? null : (JToken)int.Parse(v); Changed(); });
            var cmds = new List<(string, string)> { ("", "нет") }; cmds.AddRange(file.CommandersOf(factionId).Select(c => (((int)c["id"]).ToString(), (string)c["name"])));
            Choice("Полководец", cmds.ToArray(), u["commanderId"]?.Type == JTokenType.Integer ? ((int)u["commanderId"]).ToString() : "", v => { u["commanderId"] = v == "" ? null : (JToken)int.Parse(v); Changed(); });
            Choice("Состояние", Statuses, (string)u["status"] ?? "active", v => { u["status"] = v; Changed(); });
        }
        void CommanderForm(JObject c)
        {
            Lbl(form, "Полководец", "army-h");
            Text("Имя", (string)c["name"], v => { c["name"] = v; file.Dirty = true; Rebuild(); });
            Number("Доблесть (поединок, 1–20)", c, "valor", 1, 20, false);   // личная сила в поединке командиров (как в Three Kingdoms)
            Lbl(form, "Бонусы отрядам под его началом", "army-h2");
            Number("Боевой дух", c, "buffMorale", -1000, 1000, false);
            Number("Дисциплина", c, "buffDisc", -1000, 1000, false);
            Number("Урон, %", c, "buffDmg", -100, 1000, false);
            Number("Защита, %", c, "buffDef", -100, 1000, false);
            Btn(form, "Снять с командования", () => ConfirmAlways("delc", $"Снять «{(string)c["name"]}»? Отряды останутся без полководца. Нажми ещё раз.",
                () => { file.RemoveCommander((int)c["id"]); cmdr = null; Rebuild(); }), "small is-bad");
        }

        // ── поля ──
        void Text(string label, string value, Action<string> set)
        {
            var f = new TextField(label) { isDelayed = true, value = value ?? "" };
            f.RegisterValueChangedCallback(e => { var v = e.newValue.Trim(); if (v == "") { f.SetValueWithoutNotify(e.previousValue); return; } set(v); });
            form.Add(f);
        }
        void Number(string label, JObject o, string key, double min, double max, bool unitField = true)
        {
            var f = new FloatField(label) { isDelayed = true, value = (float)((double?)o[key] ?? 0) };
            f.RegisterValueChangedCallback(e =>
            {
                double v = Math.Max(min, Math.Min(max, Math.Round(e.newValue, 2)));
                if (key == "soldiers") v = Math.Max(1, Math.Round(v));
                f.SetValueWithoutNotify((float)v);
                o[key] = ArmyFile.Num(v);
                if (key == "soldiers" && ArmyFile.Fresh(o)) o["initial"] = ArmyFile.Num(v);   // не воевал — это его полный состав
                if (unitField) (key == "soldiers" ? menPicked : numsPicked).Add(o);
                if (unitField) Changed(); else { file.Dirty = true; Rebuild(); }
            });
            form.Add(f);
        }
        void Choice(string label, (string Id, string Name)[] items, string current, Action<string> set)
        {
            var names = items.Select(i => i.Name).ToList();
            int idx = Math.Max(0, Array.FindIndex(items, i => i.Id == current));
            var d = new DropdownField(label, names, idx);
            d.RegisterValueChangedCallback(e => { var it = items.FirstOrDefault(i => i.Name == e.newValue); set(it.Id); });
            form.Add(d);
        }
        void Changed() { unitChanged = true; file.Dirty = true; Rebuild(); }
        // новый отряд назвали (при создании имя временное) — по настоящему имени заново: шаблон, как его подбирает трекер
        // (Templates.Match, с правками фракции), и облик (В16); что выбрано руками, не трогаем
        void Reguess(JObject u)
        {
            string name = (string)u["name"], style = (string)u["style"], kit = (string)u["kit"];
            var fid = ArmyFile.Id(u["factionId"]);
            var m = Templates.Match(name);
            string tpl = null;
            if (!m.Fallback)
            {
                // род войск — как autoDetectType трекера, пока его не выбрали руками; числа — шаблона, пока их не трогали
                var t = file.Resolve(m.Id, fid);
                if (!typePicked.Contains(u)) { u["type"] = m.Type ?? t.Type; u["weapon"] = m.Weapon ?? t.Weapon; }
                if (!numsPicked.Contains(u))
                {
                    foreach (var k in Templates.Stats) u[k] = ArmyFile.Num(t[k]);
                    if (!menPicked.Contains(u)) { u["soldiers"] = ArmyFile.Num(t.Size); u["initial"] = ArmyFile.Num(t.Size); }
                    tpl = $"шаблон «{t.Name}»" + (m.Why != null ? $" ({m.Why})" : "");
                }
                else if (!typePicked.Contains(u) && m.Why != null) tpl = $"род войск — {m.Why}";
            }
            if (!stylePicked.Contains(u)) u["style"] = file.DefaultStyle(fid, name, except: u);
            if (!kitPicked.Contains(u)) u["kit"] = KitSets.Guess(name, (string)u["type"], (string)u["weapon"]);
            // ни прошлого отряда фракции, ни подсказки в названии — Западный по умолчанию, пусть выберут (В16)
            bool unguessed = !stylePicked.Contains(u) && Styles.Guess(name) == null && !file.UnitsOf(fid).Any(x => x != u && Styles.Known((string)x["style"]));
            if (tpl != null || unguessed || (string)u["style"] != style || (string)u["kit"] != kit)
                Say($"По названию: {(tpl != null ? tpl + " · " : "")}{KitSets.NameOf((string)u["kit"])} · {Styles.NameOf((string)u["style"])}"
                    + (unguessed ? " — край по названию не угадан, выбери стиль справа" : " — можно сменить справа"));
        }
        // журнал: созданное — «основана», «принял командование», «встал в строй» уже с настоящим именем; правка отряда —
        // одной записью «изменён», когда уходим с него (как в трекере)
        void FlushUnitLog()
        {
            foreach (var o in fresh) if (o.Parent != null) file.LogCreated(o);
            if (unit != null && unitChanged && unit.Parent != null && !fresh.Contains(unit)) file.Log($"Юнит «{(string)unit["name"]}» изменён");
            fresh.Clear(); unitChanged = false;
        }

        // ── действия ──
        void AddFaction()
        {
            int n = file.Factions.Count + 1;
            FlushUnitLog();
            var f = file.AddFaction($"Фракция {n}", Palette[(n - 1) % Palette.Length], log: false); fresh.Add(f);
            factionId = (int)f["id"]; unit = null; cmdr = null;
            Rebuild(); Say("Фракция создана — впиши название и выбери цвет");
        }
        void AddCommander()
        {
            FlushUnitLog(); var c = file.AddCommander(factionId, "Полководец", log: false); fresh.Add(c); cmdr = c; unit = null;
            Rebuild(); Say("Полководец создан — впиши имя и бонусы");
        }
        void AddUnit()
        {
            FlushUnitLog();
            var t = file.Resolve("infantry", factionId);
            string name = $"Отряд {file.Units.Count + 1}";
            unit = file.AddUnit(name, t, factionId, t.Size, log: false); fresh.Add(unit); cmdr = null;
            Rebuild(); Say("Отряд создан — впиши название: шаблон и облик подберутся по нему; поменять можно справа");
        }
        void CloneUnit()
        {
            if (unit == null) { Say("Выбери отряд, который клонировать", true); return; }
            FlushUnitLog(); unit = file.CloneUnit(unit); Rebuild(); Say($"Клон: «{(string)unit["name"]}»");
        }
        void RemoveUnit()
        {
            if (unit == null) { Say("Выбери отряд, который удалить", true); return; }
            var u = unit;
            ConfirmAlways("delu:" + (int)u["id"], $"Удалить «{(string)u["name"]}»? Нажми «Удалить» ещё раз.", () => { file.RemoveUnit((int)u["id"]); unit = null; unitChanged = false; Rebuild(); });
        }

        // ── мелочи ──
        static VisualElement Div(string cls, VisualElement parent) { var d = new VisualElement(); foreach (var c in cls.Split(' ')) d.AddToClassList(c); parent.Add(d); return d; }
        static Label Lbl(VisualElement parent, string text, string cls) { var l = new Label(text); foreach (var c in cls.Split(' ')) l.AddToClassList(c); parent.Add(l); return l; }
        static Button Btn(VisualElement parent, string text, Action act, string cls = "")
        {
            var b = new Button(act) { text = text }; b.AddToClassList("army-btn");
            foreach (var c in cls.Split(' ')) if (c != "") b.AddToClassList(c);
            parent.Add(b); return b;
        }
        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h ?? "", out var c) ? c : new Color(0.43f, 0.42f, 0.38f);
    }
}
