// ═══════════ Icon.cs — значки интерфейса игры, нарисованные кодом (Painter2D) ═══════════
// Без файлов картинок: контуры в поле 24 × 24, цвет — из USS (color), размер — из USS (width, height).
// Приказы: move, attack, charge, hold, retreat, rally, cancel; рода войск: infantry, pike, archer, crossbow, cavalry,
// militia, guard; состояния: fight, flee, broken, gone.
using UnityEngine;
using UnityEngine.UIElements;

namespace Journal.Play
{
    [UxmlElement]
    public partial class Icon : VisualElement
    {
        string kind = "move";
        [UxmlAttribute]
        public string Kind { get => kind; set { kind = value; MarkDirtyRepaint(); } }

        public Icon() { generateVisualContent += Draw; AddToClassList("icon"); pickingMode = PickingMode.Ignore; }
        public Icon(string kind) : this() { Kind = kind; }

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1 || h < 1) return;
            float s = Mathf.Min(w, h) / 24f, ox = (w - 24 * s) / 2, oy = (h - 24 * s) / 2;
            var p = ctx.painter2D;
            var col = resolvedStyle.color;
            p.strokeColor = col; p.fillColor = col;
            p.lineWidth = 1.8f * s; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            Vector2 P(float x, float y) => new Vector2(ox + x * s, oy + y * s);
            void Line(params float[] xy) { p.BeginPath(); p.MoveTo(P(xy[0], xy[1])); for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1])); p.Stroke(); }
            void Poly(params float[] xy) { p.BeginPath(); p.MoveTo(P(xy[0], xy[1])); for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1])); p.ClosePath(); p.Fill(); }
            void Dot(float x, float y, float r) { p.BeginPath(); p.Arc(P(x, y), r * s, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
            void Ring(float x, float y, float r, float a0 = 0, float a1 = 360) { p.BeginPath(); p.Arc(P(x, y), r * s, Angle.Degrees(a0), Angle.Degrees(a1)); p.Stroke(); }
            switch (kind)
            {
                case "f-skirmish":   // цепь: редкий ряд
                    foreach (float x in new[] { 3f, 9f, 15f, 21f }) Dot(x, 12, 1.6f); break;
                case "f-line":       // линия: два плотных ряда
                    for (float x = 4; x <= 20; x += 4) { Dot(x, 10, 1.4f); Dot(x, 14, 1.4f); } break;
                case "f-deep":       // глубокий строй: квадрат 4 × 4
                    for (float x = 6; x <= 18; x += 4) for (float y = 6; y <= 18; y += 4) Dot(x, y, 1.4f); break;
                case "f-column":     // колонна: узко и длинно
                    for (float y = 3; y <= 21; y += 4) { Dot(10, y, 1.4f); Dot(14, y, 1.4f); } break;
                case "wall":      // стена с зубцами
                    Line(3, 21, 3, 7, 6, 7, 6, 10, 9, 10, 9, 7, 15, 7, 15, 10, 18, 10, 18, 7, 21, 7, 21, 21, 3, 21); Line(3, 15, 21, 15); Line(12, 15, 12, 21); break;
                case "move":      // стрелка вперёд со следом
                    Line(5, 19, 17, 7); Poly(19, 5, 11, 7, 17, 13); Line(4, 13, 8, 17); break;
                case "attack":    // скрещённые мечи
                    Line(5, 5, 18, 18); Line(19, 5, 6, 18); Line(15, 19, 19, 15); Line(5, 15, 9, 19); break;
                case "charge":    // копьё и полосы скорости
                    Line(4, 20, 18, 6); Poly(21, 3, 14, 6, 18, 10); Line(3, 11, 9, 11); Line(5, 15, 11, 15); break;
                case "hold":      // щит
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.LineTo(P(20, 6)); p.LineTo(P(19, 13)); p.BezierCurveTo(P(18, 17), P(15, 20), P(12, 21));
                    p.BezierCurveTo(P(9, 20), P(6, 17), P(5, 13)); p.LineTo(P(4, 6)); p.ClosePath(); p.Stroke(); Line(12, 7, 12, 17); break;
                case "retreat":   // назад, лицом вперёд: стрелка вниз под чертой фронта
                    Line(5, 6, 19, 6); Line(12, 9, 12, 19); Poly(12, 22, 8, 16, 16, 16); break;
                case "rally":     // знамя на древке
                    Line(6, 21, 6, 3); Poly(6, 4, 19, 6, 15, 9, 19, 12, 6, 12); break;
                case "cancel":
                    Line(6, 6, 18, 18); Line(18, 6, 6, 18); break;
                case "infantry":  // меч остриём вверх
                    Line(12, 3, 12, 17); Line(8, 15, 16, 15); Line(12, 17, 12, 21); Poly(12, 2, 10.5f, 5, 13.5f, 5); break;
                case "guard":     // шлем
                    p.BeginPath(); p.MoveTo(P(5, 18)); p.LineTo(P(5, 11)); p.BezierCurveTo(P(5, 6), P(9, 4), P(12, 4)); p.BezierCurveTo(P(15, 4), P(19, 6), P(19, 11));
                    p.LineTo(P(19, 18)); p.ClosePath(); p.Stroke(); Line(12, 4, 12, 18); Line(8, 12, 16, 12); break;
                case "pike":      // длинная пика
                    Line(5, 21, 18, 4); Poly(20, 2, 15, 4, 18, 7); Line(7, 15, 11, 19); break;
                case "archer":    // лук со стрелой
                    p.BeginPath(); p.MoveTo(P(7, 3)); p.BezierCurveTo(P(17, 7), P(17, 17), P(7, 21)); p.Stroke(); Line(7, 3, 7, 21); Line(4, 12, 19, 12); Poly(21, 12, 17, 10, 17, 14); break;
                case "crossbow":  // арбалет
                    p.BeginPath(); p.MoveTo(P(4, 9)); p.BezierCurveTo(P(8, 5), P(16, 5), P(20, 9)); p.Stroke(); Line(12, 6, 12, 21); Line(4, 9, 12, 13, 20, 9); Line(10, 18, 14, 18); break;
                case "cavalry":   // подкова
                    p.BeginPath(); p.Arc(P(12, 11), 7 * s, Angle.Degrees(140), Angle.Degrees(400)); p.Stroke(); Line(6.6f, 15.5f, 6, 20); Line(17.4f, 15.5f, 18, 20);
                    Dot(7, 9, 0.9f); Dot(17, 9, 0.9f); Dot(12, 5, 0.9f); break;
                case "militia":   // вилы
                    Line(12, 10, 12, 22); Line(7, 3, 7, 8); Line(12, 3, 12, 8); Line(17, 3, 17, 8); Line(7, 8, 9, 10, 15, 10, 17, 8); break;
                case "fight":     // искра схватки
                    Line(12, 3, 12, 9); Line(12, 15, 12, 21); Line(3, 12, 9, 12); Line(15, 12, 21, 12); Line(6, 6, 9, 9); Line(18, 18, 15, 15); Line(18, 6, 15, 9); Line(6, 18, 9, 15); break;
                case "flee":      // сломанное знамя
                    Line(6, 21, 9, 12); Line(10, 10, 12, 3); Poly(12, 4, 20, 7, 12, 10); break;
                case "broken":    // треснувший щит
                    p.BeginPath(); p.MoveTo(P(12, 3)); p.LineTo(P(20, 6)); p.LineTo(P(19, 13)); p.BezierCurveTo(P(18, 17), P(15, 20), P(12, 21));
                    p.BezierCurveTo(P(9, 20), P(6, 17), P(5, 13)); p.LineTo(P(4, 6)); p.ClosePath(); p.Stroke(); Line(12, 3, 10, 9, 14, 13, 11, 21); break;
                case "gone":      // уход за край
                    Line(4, 12, 15, 12); Poly(19, 12, 13, 8, 13, 16); Line(20, 4, 20, 20); break;
                default:
                    Ring(12, 12, 8); break;
            }
        }

        public static string OfType(string tpl, string type)
        {
            switch (tpl)
            {
                case "guard": return "guard";
                case "militia": return "militia";
                case "pikemen": return "pike";
                case "crossbowmen": return "crossbow";
                case "archers": return "archer";
            }
            if (type == "cavalry") return "cavalry";
            if (type == "pike") return "pike";
            if (type == "archer") return "archer";
            return "infantry";
        }
    }
}
