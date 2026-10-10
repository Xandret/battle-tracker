// ═══════════ OrderOverlay.cs — подсказки приказов на карте (И2, Г80) ═══════════
// Поверх отрядов: путь приказа, «призрак» строя там, где отряд встанет к концу хода, стрелка — куда лицом.
// Выбранные — ярко (золото), остальные своей стороны — блекло; натиск — зелёный (хватит разбега) или красный;
// стрелок — круг дальности; под выбранными и под мышью — рамка строя; ЛКМ по земле — рамка выбора.
// Только рисунок: данные — из Battle.Preview (у группы при протягивании путь движком — у главного, у прочих — призрак).
using System;
using System.Collections.Generic;
using BattleCore;
using UnityEngine;

namespace Journal.Play
{
    [RequireComponent(typeof(PlayController))]
    public sealed class OrderOverlay : MonoBehaviour
    {
        PlayController pc;
        Mesh mesh; Material mat;
        readonly List<Vector3> V = new List<Vector3>(); readonly List<Color32> C = new List<Color32>(); readonly List<int> I = new List<int>();
        float ppm = 1;   // пикселей на метр — толщина линий в пикселях

        static readonly Color32 Gold = new Color32(230, 186, 92, 255), GoldDim = new Color32(230, 186, 92, 110);
        static readonly Color32 White = new Color32(245, 240, 228, 200), Ok = new Color32(120, 205, 110, 255), Bad = new Color32(220, 80, 60, 255);
        static readonly Color32 Range = new Color32(240, 220, 160, 120), Ghost = new Color32(230, 186, 92, 70);

        void Start()
        {
            pc = GetComponent<PlayController>();
            if (GetComponent<FogOverlay>() == null) gameObject.AddComponent<FogOverlay>();   // туман войны (Г107)
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            mat = new Material(sh);
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.MarkDynamic();
            var go = new GameObject("Подсказки приказов");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.sortingOrder = 50;
        }

        void LateUpdate()
        {
            var lk = pc?.ViewRec;
            if (lk == null) LateBody(); else lock (lk) LateBody();
        }
        void LateBody()
        {
            V.Clear(); C.Clear(); I.Clear();
            if (pc?.Battle == null) { mesh.Clear(); return; }
            var cam = Camera.main;
            ppm = Screen.height / (2 * cam.orthographicSize);
            bool orders = pc.Phase == PlayPhase.Orders;
            // рамки выбора и наведения — где отряд виден (по его бойцам в записи), а не где он в счёте
            if (pc.Hover != null && pc.Hover != pc.Selected && PlayController.Present(pc.Hover) && !pc.Hover.Fleeing) { pc.BoxOf(pc.Hover, out var hx, out var hy, out var hf, out var hw, out var hd); Footprint(hx, hy, hf, hw, hd, White, 1.5f); }
            if (orders)
                foreach (var m in pc.Battle.Movers)
                {
                    if (pc.IsSelected(m) || PlayController.SideOf(m) != pc.ActiveSide) continue;
                    var p = pc.PreviewOf(m);
                    if (p != null) Plan(m, p, GoldDim, 1.5f, false);
                }
            foreach (var s in pc.Selection)
            {
                if (!PlayController.Present(s)) continue;
                bool main = s == pc.Selected;
                pc.BoxOf(s, out var sx, out var sy, out var sf, out var sw, out var sd);
                if (!s.Fleeing) Footprint(sx, sy, sf, sw, sd, Gold, main ? 2.5f : 2f);   // бегущий — толпа, не строй (Г100)
                if (!orders) continue;
                if (pc.Dragging && !main)
                {
                    // прочие в группе, пока тянут (Г111 п.1): путь движком, если досчитан для этого приказа; иначе — место на конец
                    // хода по прямой на норму хода (как у главного — конец хода, а не точка приказа)
                    if (pc.DragOrders.TryGetValue(s, out var o))
                    {
                        if (pc.DragPreviews.TryGetValue(s, out var dp) && PlayController.SameOrder(dp.o, o) && dp.p != null) Plan(s, dp.p, Gold, 2f, true);
                        else GroupGhost(s, o);
                    }
                }
                else
                {
                    var p = pc.Dragging ? pc.DragPreview : pc.PreviewOf(s);
                    if (p != null) Plan(s, p, Gold, main ? 2.5f : 2f, true);
                }
                double range = main && s.P.U.Weapon == "ranged" ? BattleMap.RangeOf(s.P.U, pc.Battle.R) : 0;
                if (range > 0) Circle(s.P.X, s.P.Y, range + s.P.Fp.Depth / 2, Range, 1.5f, true);
            }
            if (orders && pc.Dragging && pc.DragOrder != null && pc.DragOrder.Kind == OrderKind.Move && (pc.DragTo - pc.DragFrom).magnitude > 4)
                Line(pc.DragFrom.x, pc.DragFrom.y, pc.DragTo.x, pc.DragTo.y, White, 2);   // протянутая линия фронта
            // расстановка: рамка строя там, куда тянут (зелёная — встанет, красная — нет места), стрелка — куда лицом
            if (pc.DeployDragging && pc.DeployUnit != null && pc.DeployWall)
            {
                // на стену: ряд стены, вдоль которого встанут (зелёный — можно, красный — стены чужие)
                var col = pc.DeployOk ? Ok : Bad;
                Line(pc.DeployWallA.x, pc.DeployWallA.y, pc.DeployWallB.x, pc.DeployWallB.y, col, 6);
            }
            else if (pc.DeployDragging && pc.DeployUnit != null)
            {
                var col = pc.DeployOk ? Ok : Bad;
                Footprint(pc.DeployX, pc.DeployY, pc.DeployFacing, pc.DeployUnit.P.Fp.Front, pc.DeployUnit.P.Fp.Depth, col, 2.5f);
                double h = pc.DeployFacing * Math.PI / 180, fx = Math.Sin(h), fy = -Math.Cos(h), L = pc.DeployUnit.P.Fp.Depth / 2 + 12;
                Line(pc.DeployX, pc.DeployY, pc.DeployX + fx * L, pc.DeployY + fy * L, col, 2);
            }
            // прочность ворот, которые рубят (Г105): полоска над проходом — что осталось; выбиты — полоски нет
            var vrec = pc.ViewRec;
            if (vrec != null)
                for (int g = 0; g < vrec.Gates.Count; g++)
                {
                    int fr = Mathf.Clamp((int)(pc.ViewT / vrec.Dt), 0, Mathf.Max(0, vrec.Frames.Count - 1));
                    if ((vrec.GateAt(g, fr) & 4) != 0 || !vrec.GateHpAt(g, fr, out var hp, out var mx) || mx <= 0) continue;
                    float k = Mathf.Clamp01(hp / mx), hw = 5, gx = vrec.Gates[g].X, gy = vrec.Gates[g].Y - 6;
                    var fill = k > 0.5f ? Ok : k > 0.25f ? new Color32(230, 186, 92, 255) : Bad;
                    Line(gx - hw - 0.3f, gy, gx + hw + 0.3f, gy, new Color32(20, 16, 10, 220), 7);
                    if (k > 0) Line(gx - hw, gy, gx - hw + 2 * hw * k, gy, fill, 4);
                }
            // строй под мышью в ряду строев (Г106): контур, каким встанет каждый выбранный
            if (orders && pc.FormHover != null && pc.FormHover.Length > 2)
            {
                string sh = pc.FormHover.Substring(2);
                if (sh == "wedge" || sh == "crescent" || sh == "square" || sh == "circle" || sh == "open" || sh == "close")
                    foreach (var s in pc.Selection)
                    {
                        if (!PlayController.Present(s) || s.Fleeing) continue;
                        var pts = pc.Battle.Outline(s.P.U, sh == "open" || sh == "close" ? null : sh);
                        if (pts == null || pts.Length < 6) continue;
                        double h = s.P.Facing * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), bx = -Math.Sin(h), by = Math.Cos(h);   // вправо и назад
                        int n = pts.Length / 2;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            double x0 = s.P.X + rx * pts[2 * i] + bx * pts[2 * i + 1], y0 = s.P.Y + ry * pts[2 * i] + by * pts[2 * i + 1];
                            double x1 = s.P.X + rx * pts[2 * j] + bx * pts[2 * j + 1], y1 = s.P.Y + ry * pts[2 * j] + by * pts[2 * j + 1];
                            Line(x0, y0, x1, y1, White, 2, true);
                        }
                    }
            }
            // туман (Г107): чужой отряд, которого сейчас не видно, — пунктирный круг там, где его видели в последний раз
            if (pc.ViewSide > 0 && orders)   // в ходе LastSeen пишет поток счёта
                foreach (var m in pc.Battle.Movers)
                {
                    if (PlayController.SideOf(m) == pc.ViewSide || !PlayController.Present(m) || pc.SeenNow(m)) continue;
                    if (!pc.Battle.LastSeen.TryGetValue((pc.ViewSide, m.P.U.Id), out var ls)) continue;
                    Circle(ls.x, ls.y, Math.Max(8, m.P.Fp.Front / 2), new Color32(200, 196, 186, 120), 1.5f, true);
                }
            // ворота под мышью — их можно открыть или закрыть щелчком
            if (orders && pc.GateHover >= 0 && pc.Battle != null)
            {
                var g = pc.GateOf(pc.GateHover);
                if (g != null) Circle(g.X, g.Y, 6, White, 2, false);
            }
            if (pc.BoxSelecting)
            {
                // рамка выбора: углы экрана → точки карты
                var a = pc.MapPoint(pc.BoxA); var b = pc.MapPoint(pc.BoxB);
                var c1 = pc.MapPoint(new Vector2(pc.BoxA.x, pc.BoxB.y)); var c2 = pc.MapPoint(new Vector2(pc.BoxB.x, pc.BoxA.y));
                Quad(a, c2, b, c1, new Color32(245, 240, 228, 28));
                Line(a.x, a.y, c2.x, c2.y, White, 1.5f); Line(c2.x, c2.y, b.x, b.y, White, 1.5f); Line(b.x, b.y, c1.x, c1.y, White, 1.5f); Line(c1.x, c1.y, a.x, a.y, White, 1.5f);
            }
            mesh.Clear();
            mesh.SetVertices(V); mesh.SetColors(C); mesh.SetTriangles(I, 0);
            mesh.RecalculateBounds();
        }

        // Путь, призрак строя в конце хода, стрелка курса; натиск — цветом пути до цели
        void Plan(Mover m, OrderPreview p, Color32 col, float px, bool full)
        {
            var route = p.Route;
            var pathCol = p.ChargeOk == true ? Ok : p.ChargeOk == false ? Bad : col;
            for (int k = 1; k < route.Count; k++) Line(route[k - 1].x, route[k - 1].y, route[k].x, route[k].y, pathCol, px, dashed: !p.ReachThisTurn && full);
            if (route.Count > 1 || Math.Abs(MoveSim.AngleDiff(p.EndFacing, m.P.Facing)) > 1)
            {
                Footprint(p.EndX, p.EndY, p.EndFacing, m.P.Fp.Front, m.P.Fp.Depth, full ? Ghost : col, 1, fill: full);
                Arrow(p.EndX, p.EndY, p.EndFacing, m.P.Fp.Depth / 2 + 6, pathCol, px);
            }
        }

        // приказ группе, пока его тянут: атака — линия к цели; движение — линия к месту и призрак строя с курсом
        void GroupGhost(Mover m, MoveOrder o)
        {
            if (o.Kind == OrderKind.Attack)
            {
                var t = pc.Battle.ById(o.TargetId);
                if (t != null) Line(m.P.X, m.P.Y, t.P.X, t.P.Y, o.Charge ? Ok : Gold, 1.5f, dashed: true);
                return;
            }
            // конец хода по прямой: не дальше нормы хода; дальше — пунктир до точки приказа
            double dx = o.X - m.P.X, dy = o.Y - m.P.Y, len = Math.Sqrt(dx * dx + dy * dy), norm = BattleMap.UnitSpeed(m.P.U, pc.Battle.R);
            double k = len > norm && len > 1e-6 ? norm / len : 1, ex = m.P.X + dx * k, ey = m.P.Y + dy * k;
            Line(m.P.X, m.P.Y, ex, ey, Gold, 1.5f);
            if (k < 1) Line(ex, ey, o.X, o.Y, Gold, 1.5f, dashed: true);
            double face = k < 1 ? MoveSim.HeadingOf(dx, dy) : o.Facing;
            Footprint(ex, ey, face, m.P.Fp.Front, m.P.Fp.Depth, Ghost, 1, fill: true);
            Arrow(ex, ey, face, m.P.Fp.Depth / 2 + 6, Gold, 1.5f);
        }

        // ── примитивы: мир Unity — (x, −y); толщина — в пикселях экрана ──
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i0 = V.Count;
            V.Add(new Vector3(a.x, -a.y, -1)); V.Add(new Vector3(b.x, -b.y, -1)); V.Add(new Vector3(c.x, -c.y, -1)); V.Add(new Vector3(d.x, -d.y, -1));
            for (int k = 0; k < 4; k++) C.Add(Lin(col));
            I.Add(i0); I.Add(i0 + 1); I.Add(i0 + 2); I.Add(i0); I.Add(i0 + 2); I.Add(i0 + 3);
            I.Add(i0); I.Add(i0 + 2); I.Add(i0 + 1); I.Add(i0); I.Add(i0 + 3); I.Add(i0 + 2);   // обе стороны — не важно, куда смотрит
        }
        void Line(double x0, double y0, double x1, double y1, Color32 col, float px, bool dashed = false)
        {
            var a = new Vector2((float)x0, (float)y0); var b = new Vector2((float)x1, (float)y1);
            float len = (b - a).magnitude; if (len < 1e-4f) return;
            var dir = (b - a) / len; var nrm = new Vector2(-dir.y, dir.x) * (px / ppm / 2);
            if (!dashed) { Quad(a - nrm, b - nrm, b + nrm, a + nrm, col); return; }
            float dash = 10 / ppm * 1.5f, gap = 7 / ppm * 1.5f;
            for (float s = 0; s < len; s += dash + gap)
            {
                var p0 = a + dir * s; var p1 = a + dir * Mathf.Min(len, s + dash);
                Quad(p0 - nrm, p1 - nrm, p1 + nrm, p0 + nrm, col);
            }
        }
        void Footprint(double cx, double cy, double headDeg, double front, double depth, Color32 col, float px, bool fill = false)
        {
            double h = headDeg * Math.PI / 180, rx = Math.Cos(h), ry = Math.Sin(h), fx = Math.Sin(h), fy = -Math.Cos(h);
            Vector2 P(double lx, double ly) => new Vector2((float)(cx + lx * rx - ly * fx), (float)(cy + lx * ry - ly * fy));
            var a = P(-front / 2, -depth / 2); var b = P(front / 2, -depth / 2); var c = P(front / 2, depth / 2); var d = P(-front / 2, depth / 2);
            if (fill) { Quad(a, b, c, d, col); col = Gold; }
            Line(a.x, a.y, b.x, b.y, col, px + 1); Line(b.x, b.y, c.x, c.y, col, px); Line(c.x, c.y, d.x, d.y, col, px); Line(d.x, d.y, a.x, a.y, col, px);
        }
        void Arrow(double cx, double cy, double headDeg, double ahead, Color32 col, float px)
        {
            double h = headDeg * Math.PI / 180, fx = Math.Sin(h), fy = -Math.Cos(h), rx = Math.Cos(h), ry = Math.Sin(h);
            double tipX = cx + fx * (ahead + 8 / ppm * 2), tipY = cy + fy * (ahead + 8 / ppm * 2);
            double bx = cx + fx * ahead, by = cy + fy * ahead, w = 7 / ppm * 2;
            Line(cx, cy, bx, by, col, px);
            var t = new Vector2((float)tipX, (float)tipY);
            var l = new Vector2((float)(bx - rx * w), (float)(by - ry * w)); var r = new Vector2((float)(bx + rx * w), (float)(by + ry * w));
            Quad(t, l, r, t, col);
        }
        void Circle(double cx, double cy, double rad, Color32 col, float px, bool dashed)
        {
            int n = 96;
            for (int k = 0; k < n; k++)
            {
                if (dashed && k % 2 == 1) continue;
                double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                Line(cx + Math.Cos(a0) * rad, cy + Math.Sin(a0) * rad, cx + Math.Cos(a1) * rad, cy + Math.Sin(a1) * rad, col, px);
            }
        }
        // цвет вершин сетки Unity не переводит из sRGB — в линейном проекте переводим сами (как в смотрелке)
        static Color32 Lin(Color32 c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? (Color32)((Color)c).linear : c;
    }
}
