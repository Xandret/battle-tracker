// ═══════════ Figure.cs — стоящий боец (В17): косой вид, как figure() в core/Tests/polygon-men.js ═══════════
// Земля — сверху, высота уходит вверх по экрану: экран (метры карты) = (x, y − K·z). Боец — из частей атласа men:
// цилиндры (ноги, руки, шея, колчан), шары (суставы, голова, кисти), срезы корпуса (по краю — пояс, простёжка,
// заклёпки, кольчуга, сюрко с гербом — виден тот край, что к зрителю), сверху — плечи (рисунок вида сверху), лицо или
// забрало спереди головы, шлем сверху, щит, павеза и плащ — в вертикальной плоскости, оружие — по направлению из позы
// вида сверху (удары и движения переносятся как есть). Части — от дальних к ближним по глубине y·K + z.
// Ткань, кожа, кисти — белые части, умноженные на цвет (Men.shader, TEXCOORD1.w = 1); сюрко, плечи стороны, щит —
// перекраской пурпурного, как раньше. Меняется figure() в полигоне — меняем и здесь.
// Скорость: части атласа ищутся один раз (FigParts — на атлас, FigKit — на комплект), в кадре строк нет. Подробность —
// по приближению: 2 — вблизи всё; 1 — средний план (нога одной частью, реже срезы, без суставов, шеи и лица); 0 — издали
// (пара ног одной частью, два среза, без рук и сапог).
using System.Collections.Generic;
using Journal.Art;
using UnityEngine;

namespace Journal.Viewer
{
    // часть фигурки: что, где (в метрах карты), каким цветом, глубина внутри бойца
    public struct Prim { public Part P; public Aff M; public Color32 C; public Vector4 Prm; public float Key; }
    // поза бойца — как у drawMen (вид сверху): оружие и щит, шаг, лук, арбалет, щит над головой
    public struct FigPose { public string Weapon; public float[] W, Sh; public float Step; public int BowSt, Xb; public bool Raise; }

    // части атласа для фигурок — найдены один раз
    public sealed class FigParts
    {
        public readonly ArtAtlas A;
        public readonly Part[] Cyl = new Part[4], Ball = new Part[3], Bow = new Part[5], Xbow = new Part[4], Pavise = new Part[3];
        public readonly Part[,,] Tab = new Part[6, 2, 3];
        public Part SlCloth, SlBrig, SlMail, SlPlate, SlBelt, Boot, Cape, Legs2, FaceOpen, FaceGreat, FaceBasc;
        public readonly Dictionary<string, Part> Weapon = new Dictionary<string, Part>(), ShieldBack = new Dictionary<string, Part>();
        public static readonly string[] CylKinds = { "cloth", "leather", "mail", "plate" }, BallKinds = { "cloth", "mail", "steel" },
            Paints = { "plain", "halves", "quarters", "stripe", "cross", "chevron" };
        public FigParts(ArtAtlas a)
        {
            A = a;
            for (int i = 0; i < 4; i++) Cyl[i] = a.Get("cyl/" + CylKinds[i]);
            for (int i = 0; i < 3; i++) { Ball[i] = a.Get("ball/" + BallKinds[i]); Pavise[i] = a.Get("pavisev/" + i); }
            for (int i = 0; i < 5; i++) Bow[i] = a.Get("bow/" + i);
            for (int i = 0; i < 4; i++) Xbow[i] = a.Get("xbow/" + i);
            for (int p = 0; p < 6; p++) for (int b = 0; b < 2; b++) for (int c = 0; c < 3; c++) Tab[p, b, c] = a.Get($"slice/tab/{Paints[p]}/{(b == 0 ? "u" : "l")}/{c}");
            SlCloth = a.Get("slice/cloth"); SlBrig = a.Get("slice/brig"); SlMail = a.Get("slice/mail"); SlPlate = a.Get("slice/plate"); SlBelt = a.Get("slice/belt");
            Boot = a.Get("boot"); Cape = a.Get("capev"); Legs2 = a.Get("legs2");
            FaceOpen = a.Get("face/open"); FaceGreat = a.Get("face/great"); FaceBasc = a.Get("face/bascinet");
            foreach (var w in new[] { "spear", "fork", "pike", "lance", "sword", "falchion", "axe", "mace", "club" }) Weapon[w] = a.Get("weapon/" + w);
            foreach (var s in new[] { "round", "oval", "heater", "buckler" }) ShieldBack[s] = a.Get("shieldback/" + s);
        }
    }
    // комплект бойца для фигурки — части и цвета, найдены один раз
    public sealed class FigKit
    {
        public Part Body, Head, Tabard, Shield, ShieldBack, Slice, TabU, TabL, Pavise, Face;
        public int SleeveCyl, LegCyl, SleeveBall, LegBall, Leather, OwnCloth = -1, HoodCloth = -2;   // HoodCloth: −2 — не капюшон, −1 — сторона
        public bool Metal, Tab, Closed, Hood, PlateHand, Glove;
        public FigKit(Kit k, FigParts P)
        {
            var a = P.A;
            Body = a.Get(k.Body); Head = a.Get(k.Head); Tabard = k.Tabard != null ? a.Get(k.Tabard) : default;
            Shield = k.Shield != null ? a.Get(k.Shield) : default;
            ShieldBack = k.ShieldShape != null && P.ShieldBack.TryGetValue(k.ShieldShape, out var sb) ? sb : default;
            string arm = k.Armour; Metal = arm == "mail" || arm == "plate"; Tab = k.TabPaint != null;
            Slice = arm == "leather" ? P.SlBrig : arm == "mail" ? P.SlMail : arm == "plate" ? P.SlPlate : P.SlCloth;
            if (Tab) { int pi = System.Array.IndexOf(FigParts.Paints, k.TabPaint); if (pi < 0) pi = 0; TabU = P.Tab[pi, 0, k.C2]; TabL = P.Tab[pi, 1, k.C2]; }
            SleeveCyl = arm == "mail" ? 2 : arm == "plate" ? 3 : arm == "leather" ? 1 : 0;
            SleeveBall = arm == "plate" ? 2 : arm == "mail" ? 1 : 0;
            LegCyl = arm == "plate" ? 3 : arm == "mail" && (k.Look == "sword" || k.Look == "lance" || k.Look == "barded") ? 2 : 0;
            LegBall = LegCyl == 3 ? 2 : LegCyl == 2 ? 1 : 0;
            Leather = k.Leather; if (k.ClothKey != "f") OwnCloth = int.Parse(k.ClothKey.Substring(1));
            Closed = k.Helm == "great" || k.Helm == "bascinet"; Hood = k.Helm == "hood";
            if (Hood) HoodCloth = k.HelmCloth == "f" ? -1 : int.Parse(k.HelmCloth.Substring(1));
            Face = k.Helm == "great" ? P.FaceGreat : k.Helm == "bascinet" ? P.FaceBasc : P.FaceOpen;
            Pavise = P.Pavise[k.C2]; PlateHand = k.Hand == "hand/plate"; Glove = k.Hand == "hand/glove";
        }
    }

    public static class Figure
    {
        public const float K = 0.6f, CylR = 0.06f, CylL = 0.3f, BallR = 0.1f, SliceRX = 0.2f, SliceRY = 0.12f;
        static readonly Color32[] Cloths = Hexes("#8b7a5c", "#7d6b4f", "#a08d6a", "#6f6a5e", "#9a8f7a", "#7a5c48", "#5f6650", "#a3977d");
        static readonly Color32[] Leathers = Hexes("#7a5634", "#6a4a2e", "#8a6440");
        static readonly Color32 Skin = Hex("#c49a74"), Glove = Hex("#5d4734"), BootC = Hex("#4a3828"), Quiver = Hex("#6e4c2e"), Fletch = Hex("#efe9dc"),
            Roll = Hex("#b09a72"), White = new Color32(255, 255, 255, 255), Dark = Hex("#3e3328");
        static Color32 Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Color32[] Hexes(params string[] h) { var r = new Color32[h.Length]; for (int i = 0; i < h.Length; i++) r[i] = Hex(h[i]); return r; }
        static Color32 Toned(Color32 c, float tone) => tone >= 0 ? Color32.Lerp(c, White, tone) : Color32.Lerp(c, new Color32(0, 0, 0, 255), -tone);
        static readonly Vector4 Tint = new Vector4(0, 0, 0, 1);
        static readonly float[] T1 = { 0, 0, 0, 1, 1 }, Top = { 0, 0, 0, 0.92f, 0.92f };

        // состояние сборки одной фигурки (без замыканий — в кадре ничего не создаётся)
        static List<Prim> F; static float X, Y, Face, cf, sf;
        static Vector3 W3(Vector3 p) => new Vector3(X + p.x * cf - p.y * sf, Y + p.x * sf + p.y * cf, p.z);
        static float Dk(Vector3 P) => P.y * K + P.z;
        static void Push(in Part p, in Aff M, Color32 c, Vector4 prm, float key) { if (p.Ok) F.Add(new Prim { P = p, M = M, C = c, Prm = prm, Key = key }); }
        static void Cyl(in Part p, Vector3 a, Vector3 b, float r, Color32 col, float bias = 0)
        {
            Vector3 A = W3(a), B = W3(b); float ax = A.x, ay = A.y - K * A.z, dx = B.x - ax, dy = B.y - K * B.z - ay, L = Mathf.Sqrt(dx * dx + dy * dy);
            if (L > 1e-4f) Push(p, Aff.At(ax, ay).R(Mathf.Atan2(dx, -dy)).S(r / CylR, L / CylL), col, Tint, (Dk(A) + Dk(B)) / 2 + bias);
        }
        static void Ball(in Part p, Vector3 c, float r, Color32 col, float bias = 0)
        {
            var C = W3(c); float q = r / BallR;
            Push(p, new Aff { a = q, d = q, e = C.x, f = C.y - K * C.z }, col, Tint, Dk(C) + bias);
        }
        static void Flat(float z, float[] T, in Part p, Color32 col, Vector4 prm, float bias = 0)
            => Push(p, Aff.At(X, Y - K * z).R(Face).P(T[0], T[1], T[2], T[3], T[4]), col, prm, Dk(W3(new Vector3(T[0], T[1], z))) + bias);
        static void FlatS(float z, float tx, float ty, float sx, float sy, in Part p, Color32 col, Vector4 prm, float bias = 0)
            => Push(p, Aff.At(X, Y - K * z).R(Face).P(tx, ty, 0, sx, sy), col, prm, Dk(W3(new Vector3(tx, ty, z))) + bias);
        static void Vert(Vector3 c, float ux0, float uy0, in Part p, Color32 col, Vector4 prm, float sx = 1, float bias = 0)
        {
            var C = W3(c); float ux = ux0 * cf - uy0 * sf, uy = ux0 * sf + uy0 * cf;
            Push(p, new Aff { a = ux * sx, b = uy * sx, c = 0, d = K, e = C.x, f = C.y - K * C.z }, col, prm, Dk(C) + bias);
        }
        static void Wpn(Vector3 g, Vector3 d, in Part p, Color32 col, Vector4 prm, float bias = 0)
        {
            var G = W3(g); float dx = d.x * cf - d.y * sf, dy = d.x * sf + d.y * cf, px = dx, py = dy - K * d.z;
            Push(p, Aff.At(G.x, G.y - K * G.z).R(Mathf.Atan2(px, -py)).S(1, Mathf.Sqrt(px * px + py * py)), col, prm, Dk(G) + bias);
        }

        public static void Build(List<Prim> list, FigParts P, Kit k, float x, float y, float face, FigPose o, Color32 side, float tone, int detail)
        {
            var fk = k.Fig as FigKit; if (fk == null) k.Fig = fk = new FigKit(k, P);
            F = list; X = x; Y = y; Face = face; cf = Mathf.Cos(face); sf = Mathf.Sin(face);
            var keyPrm = new Vector4(fk.OwnCloth < 0 ? tone : 0, 0, 0, 0);
            Color32 cloth = fk.OwnCloth < 0 ? Toned(side, tone) : Cloths[fk.OwnCloth];
            Color32 sleeveC = fk.SleeveCyl == 1 ? Leathers[fk.Leather] : cloth, legC = Color32.Lerp(cloth, Dark, 0.55f);
            Part sleeveCyl = P.Cyl[fk.SleeveCyl], legCyl = P.Cyl[fk.LegCyl];

            // ноги: издали — пара ног одной частью (стоймя, к зрителю); иначе ступни, сапоги, голени, колени, бёдра; на ходу — шаг
            if (detail == 0 && P.Legs2.Ok) Vert(new Vector3(0, 0, 0.45f), cf, -sf, P.Legs2, legC, Tint, 1, -0.2f);   // u — вдоль экрана при любом курсе
            else
                for (int s = -1; s <= 1; s += 2)
                {
                    float fx = s * 0.09f, fy = -0.02f + s * 0.13f * o.Step;
                    var hip = new Vector3(s * 0.085f, 0, 0.86f);
                    FlatS(0.01f, fx, fy, 1, 1, P.Boot, White, Vector4.zero, -0.3f);
                    if (detail == 1) { Cyl(legCyl, new Vector3(fx, fy + 0.02f, 0.06f), hip, 0.068f, legC); continue; }
                    var ank = new Vector3(fx, fy + 0.02f, 0.1f); var knee = new Vector3((hip.x + ank.x) / 2, (hip.y + ank.y) / 2 - 0.05f, 0.48f);
                    Cyl(P.Cyl[1], new Vector3(fx, fy + 0.02f, 0.04f), new Vector3(ank.x, ank.y, 0.3f), 0.063f, BootC);
                    Cyl(legCyl, new Vector3(ank.x, ank.y, 0.28f), knee, 0.058f, legC);
                    Ball(P.Ball[fk.LegBall], knee, 0.064f, legC);
                    Cyl(legCyl, knee, hip, 0.07f, legC);
                }
            // корпус: срезы от низа полы (у сюрко длиннее) до плеч, пояс; сверху — плечи вида сверху и сюрко
            float z0 = fk.Tab ? 0.62f : 0.76f, dz = detail == 2 ? 0.055f : detail == 1 ? 0.12f : 0.3f;
            for (float z = z0; z <= 1.385f; z += dz)
            {
                float w = z < 0.95f ? 0.17f + (0.95f - z) * 0.12f : z < 1.2f ? 0.165f + (z - 0.95f) * 0.14f : 0.2f + (z - 1.2f) * 0.06f, d = w * 0.6f;
                if (detail > 0 && Mathf.Abs(z - 0.94f) < dz / 2) FlatS(z, 0, 0.01f, w / SliceRX, d / SliceRY, P.SlBelt, White, Tint);
                else if (fk.Tab) FlatS(z, 0, 0.01f, w / SliceRX, d / SliceRY, z < 1.1f ? fk.TabL : fk.TabU, side, Vector4.zero);
                else FlatS(z, 0, 0.01f, w / SliceRX, d / SliceRY, fk.Slice, fk.Metal ? White : cloth, Tint);
            }
            Flat(1.41f, Top, fk.Body, side, keyPrm);
            if (fk.Tab) Flat(1.415f, Top, fk.Tabard, side, Vector4.zero);
            // поклажа за спиной
            if (detail > 0)
                switch (k.BackKind)
                {
                    case "quiver": Cyl(P.Cyl[1], new Vector3(0.1f, 0.13f, 0.82f), new Vector3(0.15f, 0.2f, 1.48f), 0.048f, Quiver, -0.05f); Ball(P.Ball[0], new Vector3(0.155f, 0.205f, 1.52f), 0.045f, Fletch, -0.05f); break;
                    case "pavise": Vert(new Vector3(0, 0.19f, 1.0f), 1, 0, fk.Pavise, side, Vector4.zero, 1, -0.1f); break;
                    case "cape": Vert(new Vector3(0, 0.165f, 0.98f), 1, 0, P.Cape, side, new Vector4(-0.3f, 0, 0, 0), 1, -0.08f); break;
                    case "roll": Cyl(P.Cyl[0], new Vector3(-0.17f, 0.13f, 1.44f), new Vector3(0.17f, 0.13f, 1.44f), 0.055f, Roll, 0.02f); break;
                    case "bag": Ball(P.Ball[0], new Vector3(0.09f, 0.17f, 1.0f), 0.08f, Leathers[1], -0.05f); break;
                }
            // руки: плечо → локоть → кисть; кисти — где держат (handsOf), по высоте — у пояса, занесённое оружие — выше
            string wpn = o.Weapon; var Wp = o.W; var sh = o.Sh;
            var (hr, hl, showL) = MenView.HandsOf(wpn, Wp, sh, o.BowSt, o.Xb);
            bool thrust = wpn != null && MenView.Thrust(wpn), oneHand = Wp != null && !thrust;
            float up = Wp != null ? Mathf.Min(1, Mathf.Abs(Wp[4])) : 1;
            float hz = wpn == "bow" ? 1.3f : wpn == "crossbow" ? 1.2f : oneHand ? 1.05f + 0.4f * (1 - up) : 1.03f;
            var dir = Wp != null ? new Vector3(Mathf.Sin(Wp[2]) * up, -Mathf.Cos(Wp[2]) * up, Mathf.Sqrt(Mathf.Max(0, 1 - up * up))) : Vector3.zero;
            Vector3? H3r = hr != null ? new Vector3(hr.Value.x, hr.Value.y, hz) : (Vector3?)null;
            Vector3? H3l = hl != null ? new Vector3(hl.Value.x, hl.Value.y, o.Raise ? 1.75f : sh != null ? 1.08f : hz) : (Vector3?)null;
            if (H3l != null && H3r != null && Wp != null && sh == null && thrust) H3l = H3r.Value + dir * 0.28f;
            if (detail > 0)
            {
                for (int s = 1; s >= -1; s -= 2)
                {
                    var H = s > 0 ? H3r ?? new Vector3(0.235f, -0.02f, 0.95f) : H3l ?? new Vector3(-0.235f, -0.02f, 0.95f);   // пустая рука — опущена
                    var S = new Vector3(s * 0.205f, 0.01f, 1.36f);
                    var E = new Vector3((S.x + H.x) / 2 + s * 0.06f, (S.y + H.y) / 2 + 0.06f, (S.z + H.z) / 2 - 0.12f);
                    Cyl(sleeveCyl, S, E, 0.058f, sleeveC);
                    if (detail == 2) Ball(P.Ball[fk.SleeveBall], E, 0.058f, sleeveC);
                    Cyl(sleeveCyl, E, H, 0.052f, sleeveC);
                }
                // кисти — поверх рукояти; левая со щитом — за щитом
                var hp = P.Ball[fk.PlateHand ? 2 : 0]; Color32 hc = fk.Glove ? Glove : Skin;
                if (H3r != null) Ball(hp, H3r.Value, 0.046f, hc, 0.03f);
                if (H3l != null && (showL || sh == null)) Ball(hp, H3l.Value, 0.046f, hc, 0.03f);
            }
            // шея, голова, лицо или забрало, шлем
            Color32 headC = fk.Closed ? White : fk.Hood ? (fk.HoodCloth < 0 ? Toned(side, -0.25f) : Cloths[fk.HoodCloth]) : Skin;
            if (detail == 2) Cyl(P.Cyl[fk.Metal ? 2 : 0], new Vector3(0, 0, 1.36f), new Vector3(0, -0.015f, 1.5f), 0.048f, Skin);
            Ball(P.Ball[fk.Closed ? 2 : 0], new Vector3(0, -0.03f, 1.6f), 0.112f, headC);
            if (detail == 2 && -cf > -0.25f) Vert(new Vector3(0, -0.122f, 1.59f), 1, 0, fk.Face, White, Vector4.zero, 1, 0.25f);
            FlatS(1.675f, 0, -0.03f, 1, 1, fk.Head, side, keyPrm, 0.3f);
            // оружие: от кисти по направлению позы (сверху — поворот W[2]; W[4] — насколько наклонено к нам — уходит вверх)
            if (wpn == "bow") Flat(hz, T1, P.Bow[o.BowSt], side, Vector4.zero, 0.02f);
            else if (wpn == "crossbow") Flat(hz, T1, P.Xbow[o.Xb], side, Vector4.zero, 0.02f);
            else if (Wp != null && H3r != null && wpn != null && P.Weapon.TryGetValue(wpn, out var wp)) Wpn(H3r.Value, dir, wp, side, Vector4.zero, 0.01f);
            // щит: на левой руке — в вертикальной плоскости, лицом вперёд (повёрнут на Sh[2]); над головой — плашмя
            if (sh != null && fk.Shield.Ok)
            {
                if (o.Raise) Flat(1.95f, new[] { sh[0], sh[1], sh[2], 1, 1 }, fk.Shield, side, Vector4.zero, 0.5f);
                else if (H3l != null)
                {
                    float ux = Mathf.Cos(sh[2]), uy = Mathf.Sin(sh[2]), nx = Mathf.Sin(sh[2]), ny0 = -Mathf.Cos(sh[2]);
                    float ny = nx * sf + ny0 * cf;   // нормаль в осях карты: к зрителю — y > 0
                    var c = H3l.Value + new Vector3(nx * 0.05f, ny0 * 0.05f, 0.02f);
                    Vert(c, ux, uy, ny > 0 ? fk.Shield : fk.ShieldBack, side, Vector4.zero, Mathf.Abs(sh[3]), 0.06f);
                }
            }
            F = null;
        }
    }
}
