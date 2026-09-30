// ═══════════ Templates.cs — шаблоны отрядов (данные из templates.js) ═══════════
// ЧЕРНОВИК ДО ГМа: медианы по сохранению armiya_hod1 (v30.1–v30.2). Подбор шаблона по названию
// (matchTemplate) и правки по фракциям перенесём вместе со сбором армий; здесь пока только профили.
using System.Collections.Generic;
using System.Linq;

namespace BattleCore
{
    public sealed class UnitTemplate
    {
        public string Id, Name, Type, Weapon;
        public double Size, Discipline, Morale, EqAtk, EqDef, Exp, Mastery;

        public UnitTemplate(string id, string name, string type, string weapon, double size,
                            double disc, double morale, double atk, double def, double exp, double mastery)
        {
            Id = id; Name = name; Type = type; Weapon = weapon; Size = size;
            Discipline = disc; Morale = morale; EqAtk = atk; EqDef = def; Exp = exp; Mastery = mastery;
        }

        // Отряд по шаблону: soldiers человек, в строю, не на карте
        public Unit Make(int id, string name, double soldiers, int? factionId) => new Unit
        {
            Id = id, Name = name, Type = Type, Weapon = Weapon, FactionId = factionId,
            Soldiers = soldiers, Initial = soldiers, Discipline = Discipline, Morale = Morale,
            EqAtk = EqAtk, EqDef = EqDef, Exp = Exp, Mastery = Mastery,
        };
    }

    public static class Templates
    {
        public static readonly List<UnitTemplate> Base = new List<UnitTemplate>
        {
            new UnitTemplate("militia", "Ополчение", "infantry", "melee", 1000, 40, 70, 40, 40, 0, 0),
            new UnitTemplate("infantry", "Пехота", "infantry", "melee", 1000, 50, 70, 60, 60, 20, 10),
            new UnitTemplate("guard", "Гвардия", "infantry", "melee", 1000, 80, 120, 90, 90, 70, 60),
            new UnitTemplate("militia_archers", "Лучники ополчения", "archer", "ranged", 1000, 30, 70, 30, 30, 0, 0),
            new UnitTemplate("archers", "Лучники", "archer", "ranged", 1000, 70, 80, 70, 30, 20, 40),
            new UnitTemplate("crossbowmen", "Арбалетчики", "archer", "ranged", 1000, 60, 70, 60, 60, 30, 20),
            new UnitTemplate("pikemen", "Пикинёры", "pike", "melee", 1000, 60, 80, 30, 70, 20, 0),
            new UnitTemplate("foot_knights", "Пешие рыцари", "infantry", "melee", 1000, 60, 60, 60, 60, 10, 20),
            new UnitTemplate("knights", "Конные рыцари", "cavalry", "melee", 1000, 40, 80, 80, 80, 20, 20),
            new UnitTemplate("elite_cavalry", "Элитная конница", "cavalry", "melee", 1000, 80, 100, 80, 80, 60, 40),
        };

        public static UnitTemplate Get(string id) => Base.FirstOrDefault(t => t.Id == id);
    }
}
