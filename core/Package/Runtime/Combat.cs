// ═══════════ Combat.cs — расчёт боя (копия combat.js) ═══════════
// Строки журнала совпадают с трекером до символа: их сверяет эталон v29.
using System;
using System.Collections.Generic;

namespace BattleCore
{
    // Модификаторы карты для одного удара (черновик 6а): высота в ближнем бою, укрытие от стрел
    public sealed class StrikeMod
    {
        public double? Mult;
        public string Note = "";
        public double CoverPct;
        public string CoverNote = "";
    }

    // battlemap.mapModsFor: режим боя по местности, модификаторы в обе стороны, причина «без натиска»
    public sealed class MapMods
    {
        public string Mode;
        public StrikeMod Ab = new StrikeMod(), Ba = new StrikeMod();
        public string NoCharge;
        public List<string> Notes = new List<string>();
    }

    public sealed class BattleRequest
    {
        public string Mode = Modes.MeleeForm;
        public double SitPct;
        public string FatigueMode = "percent";
        public bool Mutual, Charge, CounterCharge;
        public double? RollCap;   // приступ на стену (6б): минимум броска не больше бойцов в деле; null — как v29
        public MapMods MapMods;   // только при включённых «Правилах карты»
    }

    public sealed class EffStats
    {
        public double EqAtk, EqDef, Discipline, Exp, Mastery, Morale;
        public Commander Cmdr;
    }

    public sealed class StrikeResult
    {
        public double Casualties, Killed, Wounded;
    }

    public static class Combat
    {
        static string Signed(double v) => (v > 0 ? "+" : "") + Js.Num(v);

        // Эффективные параметры в данном типе боя: баффы полководца, штраф стрелков в рукопашной
        public static EffStats Eff(Unit u, string mode, List<string> L, EngineContext ctx)
        {
            var R = ctx.Rules;
            double eqAtk = u.EqAtk, eqDef = u.EqDef, disc = u.Discipline, exp = u.Exp, mast = u.Mastery, mor = u.Morale;
            var cmdr = ctx.CommanderOf(u);
            if (cmdr != null && (cmdr.BuffMorale != 0 || cmdr.BuffDisc != 0))
            {
                if (cmdr.BuffMorale != 0) mor = Js.Clamp(mor + cmdr.BuffMorale, 0, R.Morale.Max);
                if (cmdr.BuffDisc != 0) disc = Js.Clamp(disc + cmdr.BuffDisc, 1, 100);
                L?.Add($"⚜ {cmdr.Name} ведёт «{u.Name}»: " +
                      (cmdr.BuffMorale != 0 ? $"БД {Signed(cmdr.BuffMorale)} → {Js.Num(mor)}" : "") +
                      (cmdr.BuffMorale != 0 && cmdr.BuffDisc != 0 ? ", " : "") +
                      (cmdr.BuffDisc != 0 ? $"дисц {Signed(cmdr.BuffDisc)} → {Js.Num(disc)}" : ""));
            }
            if (u.Weapon == "ranged" && Modes.IsMelee(mode))
            {
                double k = R.ArcherMeleeMult;
                eqAtk = Js.R1(eqAtk * k); eqDef = Js.R1(eqDef * k); disc = Js.R1(disc * k);
                exp = Js.R1(exp * k); mast = Js.R1(mast * k); mor = Js.R1(mor * k);
                L?.Add($"«{u.Name}» — стрелки в ближнем бою: все параметры −{Js.Num(Js.Round((1 - k) * 100))}%");
            }
            return new EffStats { EqAtk = eqAtk, EqDef = eqDef, Discipline = disc, Exp = exp, Mastery = mast, Morale = mor, Cmdr = cmdr };
        }

        // Один удар att → def. Броски: d(численность атакующего), затем d(летальность), если есть потери.
        public static StrikeResult Strike(Unit att, Unit def, BattleRequest opts, List<string> L, EngineContext ctx,
                                          bool isCharge, double extraMult, string extraNote, string sector, StrikeMod mapMod)
        {
            var R = ctx.Rules;
            var A = Eff(att, opts.Mode, L, ctx);
            var D = Eff(def, opts.Mode, L, ctx);
            bool melee = Modes.IsMelee(opts.Mode);

            double roll = Dice.Roll(ctx.Rng, att.Soldiers);
            double floor0 = Js.Round(A.Discipline * R.RollFloorPerDisc);
            double rollFloor = opts.RollCap is double cap && cap != 0 ? Math.Min(floor0, cap) : floor0;
            string capped = rollFloor < floor0 ? $", не больше бойцов в деле — {Js.Num(opts.RollCap.Value)}" : "";
            if (roll < rollFloor)
            {
                L?.Add($"Бросок d{Js.Num(att.Soldiers)}: {Js.Num(roll)} → поднят до минимума {Js.Num(rollFloor)} (дисциплина {Js.Num(A.Discipline)} × {Js.Num(R.RollFloorPerDisc)}{capped})");
                roll = rollFloor;
            }
            else
            {
                L?.Add($"Бросок d{Js.Num(att.Soldiers)}: {Js.Num(roll)} (минимум по дисциплине: {Js.Num(rollFloor)}{capped})");
            }

            double dmg = StrikeDamage(att, def, A, D, opts, L, ctx, isCharge, extraMult, extraNote, sector, mapMod, roll);
            return Casualties(def, dmg, L, ctx);
        }

        // Урон одного удара при заданном броске — до округления и без бросков (И1: поштучная модель
        // зовёт её каждый шаг с L = null; Strike — один раз с журналом)
        public static double StrikeDamage(Unit att, Unit def, EffStats A, EffStats D, BattleRequest opts, List<string> L,
                                          EngineContext ctx, bool isCharge, double extraMult, string extraNote, string sector,
                                          StrikeMod mapMod, double roll)
        {
            var R = ctx.Rules;
            bool melee = Modes.IsMelee(opts.Mode);
            double div = R.ModeDivFor(opts.Mode);
            double dmg;
            if (opts.Mode == Modes.MeleeForm)
            {
                double f = A.EqAtk + A.Discipline / 2 + A.Exp / 2;
                dmg = roll * f / div;
                L?.Add($"Атака: {Js.Num(roll)} × ({Js.Num(A.EqAtk)} + {Js.Num(A.Discipline)}/2 + {Js.Num(A.Exp)}/2) / {Js.Num(div)} = {Js.Num(Js.R1(dmg))}");
            }
            else if (opts.Mode == Modes.RangedForm)
            {
                double f = A.EqAtk + A.Exp / 2 + A.Mastery / 2;
                dmg = roll * f / div;
                L?.Add($"Атака: {Js.Num(roll)} × ({Js.Num(A.EqAtk)} + {Js.Num(A.Exp)}/2 + {Js.Num(A.Mastery)}/2) / {Js.Num(div)} = {Js.Num(Js.R1(dmg))}");
            }
            else if (opts.Mode == Modes.MeleeRough)
            {
                double f = A.Exp / 2 + A.Morale / 2 + A.Mastery + A.EqAtk;
                dmg = roll * f / div;
                L?.Add($"Атака: {Js.Num(roll)} × ({Js.Num(A.Exp)}/2 + {Js.Num(A.Morale)}/2 + {Js.Num(A.Mastery)} + {Js.Num(A.EqAtk)}) / {Js.Num(div)} = {Js.Num(Js.R1(dmg))}");
            }
            else
            {
                double f = A.EqAtk + A.Exp / 2 + A.Mastery / 2;
                dmg = roll * f / div;
                L?.Add($"Атака: {Js.Num(roll)} × ({Js.Num(A.EqAtk)} + {Js.Num(A.Exp)}/2 + {Js.Num(A.Mastery)}/2) / {Js.Num(div)} = {Js.Num(Js.R1(dmg))}");
            }

            if (att.Fatigue > 0)
            {
                if (opts.FatigueMode == "percent")
                {
                    double before = dmg;
                    dmg *= 1 - att.Fatigue / 100;
                    L?.Add($"Усталость {Js.Num(att.Fatigue)}: −{Js.Num(att.Fatigue)}% урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
                }
                else
                {
                    dmg -= att.Fatigue;
                    L?.Add($"Усталость {Js.Num(att.Fatigue)}: урон −{Js.Num(att.Fatigue)} = {Js.Num(Js.R1(dmg))}");
                }
            }

            var st = Units.MoraleStage(A.Morale);
            if (st.Mult > 1)
            {
                double before = dmg;
                dmg *= st.Mult;
                L?.Add($"Боевой дух «{st.Label}»: ×{Js.Num(st.Mult)} ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
            }

            if (Units.IsCav(att) && melee)
            {
                double before = dmg;
                if (isCharge)
                {
                    dmg *= R.Cavalry.ChargeMult;
                    L?.Add($"🐎 Натиск: +{Js.Num(Js.Round((R.Cavalry.ChargeMult - 1) * 100))}% урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
                }
                else
                {
                    dmg *= R.Cavalry.NoChargeMult;
                    L?.Add($"Кавалерия без натиска: −{Js.Num(Js.Round((1 - R.Cavalry.NoChargeMult) * 100))}% урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
                }
            }

            if (extraMult != 0 && extraMult != 1)
            {
                double before = dmg;
                dmg *= extraMult;
                L?.Add($"{(string.IsNullOrEmpty(extraNote) ? "Особый модификатор" : extraNote)}: ×{Js.Num(extraMult)} ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
            }

            if (mapMod != null && mapMod.Mult.HasValue && mapMod.Mult.Value != 0 && mapMod.Mult.Value != 1 && melee)
            {
                double before = dmg, m = mapMod.Mult.Value;
                dmg *= m;
                L?.Add($"{mapMod.Note}: {(m > 1 ? "+" : "−")}{Js.Num(Js.Round(Math.Abs(m - 1) * 100))}% урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))}) · черновик");
            }

            if (A.Cmdr != null && A.Cmdr.BuffDmg != 0)
            {
                double before = dmg;
                dmg *= 1 + A.Cmdr.BuffDmg / 100;
                L?.Add($"⚜ {A.Cmdr.Name}: урон {Signed(A.Cmdr.BuffDmg)}% ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
            }

            {
                var Df = R.Defense;
                bool ranged = !melee;
                double defEq = D.EqDef;
                if (sector == "rear")
                {
                    double was = defEq;
                    defEq = Js.R1(defEq * Df.RearEqMult);
                    L?.Add($"Удар в тыл: снаряжение защиты «{def.Name}» −{Js.Num(Js.Round((1 - Df.RearEqMult) * 100))}% на эту атаку ({Js.Num(was)} → {Js.Num(defEq)})");
                }
                double dv = ranged ? defEq / Df.RangedEqDiv : defEq / Df.MeleeEqDiv + D.Discipline / Df.MeleeDiscDiv;
                double divisor = Math.Max(Df.MinDivisor, dv);
                double before = dmg;
                dmg = dmg / divisor;
                L?.Add(ranged
                    ? $"Защита цели: урон / ({Js.Num(defEq)}/{Js.Num(Df.RangedEqDiv)}) → {Js.Num(Js.R1(before))} / {Js.Num(Js.R1(divisor))} = {Js.Num(Js.R1(dmg))} (дисциплина от стрел не спасает){(dv < Df.MinDivisor ? " · делитель не ниже 1" : "")}"
                    : $"Защита цели: урон / ({Js.Num(defEq)}/{Js.Num(Df.MeleeEqDiv)} + {Js.Num(D.Discipline)}/{Js.Num(Df.MeleeDiscDiv)}) → {Js.Num(Js.R1(before))} / {Js.Num(Js.R1(divisor))} = {Js.Num(Js.R1(dmg))}{(dv < Df.MinDivisor ? " (делитель не ниже 1)" : "")}");
            }

            if (D.Cmdr != null && D.Cmdr.BuffDef != 0)
            {
                double before = dmg;
                dmg *= Math.Max(0, 1 - D.Cmdr.BuffDef / 100);
                L?.Add($"⚜ {D.Cmdr.Name} прикрывает «{def.Name}»: урон −{Js.Num(D.Cmdr.BuffDef)}% ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
            }

            if (!melee && opts.SitPct > 0)
            {
                double before = dmg;
                dmg *= 1 - opts.SitPct / 100;
                L?.Add($"Ситуативный модификатор: −{Js.Num(opts.SitPct)}% от итогового урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))})");
            }

            if (mapMod != null && mapMod.CoverPct > 0 && !melee)
            {
                double before = dmg;
                dmg *= 1 - mapMod.CoverPct / 100;
                L?.Add($"{mapMod.CoverNote}: −{Js.Num(mapMod.CoverPct)}% от итогового урона ({Js.Num(Js.R1(before))} → {Js.Num(Js.R1(dmg))}) · черновик");
            }

            return dmg;
        }

        // Потери от урона: округление до солдат и бросок летальности (убитые / раненые)
        public static StrikeResult Casualties(Unit def, double dmg, List<string> L, EngineContext ctx)
        {
            var R = ctx.Rules;
            double casualties = Math.Min(def.Soldiers, Math.Max(0, Js.Round(dmg)));
            L?.Add($"Потери «{def.Name}»: {Js.Num(casualties)} солдат");
            double killed = 0, wounded = 0;
            if (casualties > 0)
            {
                var Lt = R.Lethality;
                double lroll = Dice.Roll(ctx.Rng, Lt.Die);
                double expDiv = Math.Max(1, def.Exp) / Lt.ExpDiv;
                double pct = Js.Clamp(Lt.Base + lroll / expDiv, 0, 100);
                killed = Js.Round(casualties * pct / 100);
                wounded = casualties - killed;
                L?.Add($"Из них: {Js.Num(killed)} убитых, {Js.Num(wounded)} раненых (летальность {Js.Num(Lt.Base)}% + d{Js.Num(Lt.Die)}({Js.Num(lroll)}) / ({Js.Num(Math.Max(1, def.Exp))}/{Js.Num(Lt.ExpDiv)}) = {Js.Num(Js.R1(pct))}%)");
            }
            return new StrikeResult { Casualties = casualties, Killed = killed, Wounded = wounded };
        }

        // Патч отряда после понесённых потерь + признак «тяжёлого» исхода
        public static Patch CasualtyPatch(Unit def, StrikeResult res, List<string> L, EngineContext ctx, out bool broke)
        {
            var R = ctx.Rules; var M = R.MoraleLoss;
            var patch = new Patch
            {
                Soldiers = def.Soldiers - res.Casualties,
                TotKilled = def.TotKilled + res.Killed,
                TotWounded = def.TotWounded + res.Wounded,
            };
            broke = false;
            if (res.Casualties > 0)
            {
                double frac = res.Casualties / def.Soldiers;
                double moraleLoss;
                if (frac > M.CrushingFrac)
                {
                    moraleLoss = M.CrushingLoss;
                    L?.Add($"💥 Сокрушительные потери «{def.Name}» (>{Js.Num(Js.Round(M.CrushingFrac * 100))}% за одно действие): БД −{Js.Num(M.CrushingLoss)}");
                }
                else
                {
                    moraleLoss = Math.Min(M.Cap, Js.Round(res.Casualties / M.PerCasualties));
                    L?.Add($"Штраф БД «{def.Name}» за потери: {Js.Num(res.Casualties)} / {Js.Num(M.PerCasualties)} = −{Js.Num(moraleLoss)}");
                }
                patch.Merge(MoraleRules.ApplyMoraleChange(def, def.Morale - moraleLoss, L, R));
                if (patch.Broken == true) broke = true;
            }
            if (patch.Soldiers <= 0)
            {
                patch.Status = "destroyed"; patch.Soldiers = 0;
                L?.Add($"☠ Юнит «{def.Name}» уничтожен полностью");
                broke = true;
            }
            else
            {
                double m = patch.Morale ?? def.Morale;
                if (m <= R.Morale.CheckAt && m > 0) L?.Add($"БД «{def.Name}» ≤ {Js.Num(R.Morale.CheckAt)} — требуется проверка БД");
            }
            return patch;
        }

        // Полный бой между A и B
        public static ActionResult ResolveBattle(Unit A, Unit B, BattleRequest req, EngineContext ctx)
        {
            var R = ctx.Rules;
            ActionResult Fail(params string[] lines) => new ActionResult { Ok = false, Title = "Бой невозможен", Lines = new List<string>(lines), Tone = "danger" };

            if (A == null || B == null || A.Id == B.Id || A.Status != "active" || !Units.CanBeTargeted(B))
                return Fail("Атакующий должен быть в строю, а цель — не уничтожена.");
            if (A.FactionId.HasValue && A.FactionId == B.FactionId)
                return Fail($"«{A.Name}» и «{B.Name}» — одна фракция ({ctx.FactionName(A.FactionId)}). Свои по своим не бьют.");
            double limit = Units.AttackLimit(A, R);
            if (A.AttacksMade >= limit)
                return Fail($"«{A.Name}» уже израсходовал атаки в этом ходу ({Js.Num(A.AttacksMade)}/{Js.Num(limit)}).{(limit == 1 ? $" Дисциплина {Js.Num(R.Actions.EliteDisc)}+ даёт 2 атаки за ход." : "")}");

            var opts = new BattleRequest { Mode = req.Mode, SitPct = Js.Clamp(req.SitPct, 0, 100), FatigueMode = req.FatigueMode };
            bool mutual = req.Mutual;
            var MM = req.MapMods;
            bool chargeBlocked = MM != null && !string.IsNullOrEmpty(MM.NoCharge);
            bool melee = Modes.IsMelee(opts.Mode);
            bool charge = req.Charge && !chargeBlocked && A.Type == "cavalry" && melee;
            bool counterCharge = req.CounterCharge && !chargeBlocked && B.Type == "cavalry" && melee && B.Status == "active";

            var A0 = A.Clone(); var B0 = B.Clone();
            var L = new List<string>();
            string tone = "attack";

            string sector = Units.AttackSector(A, B, R);
            bool flanked = sector != "front";
            bool pikeStop = Units.IsPike(B) && Units.IsCav(A) && melee && sector == "front" && B.Status == "active";
            if (charge && counterCharge)
            {
                L?.Add("—— Встречный натиск: обе конницы идут навстречу ——");
                L?.Add($"«{A0.Name}» и «{B0.Name}» сшибаются на полном ходу — натиск считается обеим сторонам");
            }
            if (A.OnMap && B.OnMap)
                L?.Add($"Направление удара: {Units.SectorRu[sector]} (фасинг «{B0.Name}»: {Js.Num(Js.Round(B0.Facing))}°)");
            if (MM != null)
            {
                foreach (var n in MM.Notes) L?.Add(n);
                if (chargeBlocked && (req.Charge || req.CounterCharge) && melee)
                    L?.Add($"🐎 Натиск невозможен — {MM.NoCharge} · черновик");
            }
            bool effCharge = charge;
            if (pikeStop)
            {
                if (charge)
                {
                    effCharge = false;
                    L?.Add($"🛡 Пикинёры «{B0.Name}» встречают конницу копьями во фронт — натиск остановлен");
                }
                else
                {
                    L?.Add($"🛡 Конница входит в лоб на копья «{B0.Name}» — останавливать нечего, контрудар будет тройным");
                }
            }

            L?.Add($"—— Удар: {A0.Name} → {B0.Name} ——");
            var resB = Strike(A0, B0, opts, L, ctx, effCharge, 1, "", sector, MM?.Ab);

            bool counter = mutual;
            string noCounterReason = "";
            if (counter && flanked)
            {
                counter = false;
                noCounterReason = $"Удар {Units.SectorRu[sector]}: «{B0.Name}» не успевает развернуться и не отвечает";
            }
            if (counter && charge && !counterCharge && !pikeStop)
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» смят натиском — ответного удара нет";
            }
            if (counter && !melee && B0.Weapon != "ranged")
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» — отряд ближнего боя под обстрелом: ответить нечем";
            }
            if (counter && B0.Status == "fled")
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» бежит с поля боя — отбиваться некому";
            }
            if (counter && B0.Morale == 0)
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» сломлен (БД на нуле) — ответного удара нет";
            }
            else if (counter && B0.Morale < R.Morale.ShakenBelow)
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» дрогнул (БД ниже {Js.Num(R.Morale.ShakenBelow)}) — ответного удара нет";
            }
            double cLimit = Units.CounterLimit(B0, R);
            if (counter && B0.CountersMade >= cLimit)
            {
                counter = false;
                noCounterReason = $"«{B0.Name}» уже израсходовал ответные удары в этом ходу ({Js.Num(B0.CountersMade)}/{Js.Num(cLimit)}){(cLimit == 1 ? $" — дисциплина {Js.Num(R.Actions.EliteDisc)}+ даёт 2 ответных удара" : "")}";
            }

            var resA = new StrikeResult();
            if (counter)
            {
                L?.Add(pikeStop
                    ? $"—— Контрудар пикинёров: {B0.Name} → {A0.Name} (по численности до потерь) ——"
                    : counterCharge
                        ? $"—— Встречный удар с натиском: {B0.Name} → {A0.Name} (по численности до потерь) ——"
                        : $"—— Ответный удар: {B0.Name} → {A0.Name} (по численности до потерь) ——");
                resA = Strike(B0, A0, opts, L, ctx, counterCharge, pikeStop ? R.PikeCounterMult : 1,
                    pikeStop ? $"🛡 Копья против конского строя: +{Js.Num((R.PikeCounterMult - 1) * 100)}% урона" : "", "front", MM?.Ba);
            }
            else if (mutual && noCounterReason != "")
            {
                L?.Add("—— Без ответного удара ——");
                L?.Add(noCounterReason);
            }

            if (B0.Status == "fled")
                L?.Add($"Преследование: «{B0.Name}» бежит и не оказывает сопротивления");
            L?.Add("—— Итоги боя ——");
            var patches = new List<UnitPatch>();
            var cb = CasualtyPatch(B0, resB, L, ctx, out bool brokeB);
            patches.Add(new UnitPatch(B.Id, cb));
            if (brokeB) tone = "danger";
            if (counter)
            {
                var ca = CasualtyPatch(A0, resA, L, ctx, out bool brokeA);
                patches.Add(new UnitPatch(A.Id, ca));
                if (brokeA) tone = "danger";
            }

            patches.Add(new UnitPatch(A.Id, new Patch { AttacksMade = A.AttacksMade + 1, Acted = true }));
            L?.Add($"«{A0.Name}»: атака засчитана ({Js.Num(A.AttacksMade + 1)}/{Js.Num(limit)}), отмечен «Походил»");
            if (counter)
            {
                patches.Add(new UnitPatch(B.Id, new Patch { Acted = true, CountersMade = B.CountersMade + 1 }));
                L?.Add($"«{B0.Name}»: дрался в ответ (ответных ударов: {Js.Num(B.CountersMade + 1)}/{Js.Num(cLimit)}) — отмечен «Походил»");
            }

            string chargeTag = (pikeStop ? (charge ? " · натиск остановлен пиками" : " · лоб на копья") : "")
                             + (charge && counterCharge ? " · встречный натиск"
                                : charge && !pikeStop ? " · натиск"
                                : counterCharge ? " · контратака с натиском" : "")
                             + (flanked ? " · удар " + Units.SectorRu[sector] : "");
            string title = (counter ? $"{A.Name} ⇄ {B.Name}" : $"{A.Name} → {B.Name}") + $" · {Modes.Names[opts.Mode]}{chargeTag}";
            return new ActionResult { Ok = true, Title = title, Lines = L, Tone = tone, Patches = patches };
        }
    }
}
