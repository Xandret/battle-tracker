// ═══════════ BattleAudio.cs — звук боя по записи (Алекс 09.10.2026, п. 3) ═══════════
// Всё — по записи боя (Recording) и времени показа: звук идёт вместе с картинкой, на паузе стихает, при перемотке не
// «выстреливает» пропущенным. Два слоя:
//   фон — петли, громкость по силе боя в кадре камеры: рубка (бойцы в схватке), топот конницы (кони на рыси и галопе),
//         паника (бегущие), гул войска (идущие строем); каждая сходится к цели за ~0,6 с;
//   события — разовые звуки с места: удар на щит — звон металла, удар оружием — свист и стук, павший — удар по телу,
//         иногда крик (если включены «жестокие звуки»), падение; залп — свист стрел, стрела в постройку — стук по дереву;
//         сшибка конницы — ржание и тяжёлый удар; «сплотились» — клич. Сторона — по месту на экране (стерео), громкость —
//         от середины кадра и от приближения: издали слышен фон, вблизи — каждый удар. Одновременно — не больше Voices.
// Звуки — Resources/Audio/Battle (CC0, откуда — Audio/ИСТОЧНИКИ.txt).
using System.Collections.Generic;
using UnityEngine;

namespace Journal.Viewer
{
    public sealed class BattleAudio : MonoBehaviour
    {
        public static float Volume = 0.8f;       // общая громкость (настройки)
        public static float Duck = 1;            // под музыкой меню бой тише (заставка)
        public static bool Gore = true;          // «жестокие звуки»: крики павших

        BattleViewer viewer;
        const int Voices = 24;
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource melee, horses, panic, army;
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        Recording rec; double lastT = -1; int deadIdx, arrowIdx;
        float clashBudget, screamAt, whinnyAt, volleyAt, cheerAt, thudBudget;
        readonly List<int> landing = new List<int>();   // стрелы в полёте: номера в rec.Arrows — ждём T1
        // длинные записи (в файле ржания — несколько подряд, падение тела — 12 с): не дольше стольких секунд, в конце — затухание
        static readonly Dictionary<string, float> MaxLen = new Dictionary<string, float>
        { ["scream"] = 1.8f, ["whinny"] = 2.2f, ["cheer"] = 4f, ["volley"] = 3f, ["impact"] = 1.6f, ["fall"] = 1.4f, ["hit"] = 1.2f, ["wood"] = 0.8f, ["swing"] = 0.8f };
        readonly List<(AudioSource s, float end, float vol)> fading = new List<(AudioSource, float, float)>();

        public void Init(BattleViewer v)
        {
            viewer = v;
            AudioClip[] Load(params string[] names)
            {
                var l = new List<AudioClip>();
                foreach (var n in names)
                {
                    if (n.EndsWith("*")) { var pre = n.TrimEnd('*'); for (int k = 0; k < 10; k++) { var c = Resources.Load<AudioClip>($"Audio/Battle/{pre}{k:000}"); if (c != null) l.Add(c); } }
                    else { var c = Resources.Load<AudioClip>("Audio/Battle/" + n); if (c != null) l.Add(c); }
                }
                return l.ToArray();
            }
            clips["clash"] = Load("impactMetal_heavy_*", "impactMetal_medium_*", "impactPlate_heavy_*", "impactPlate_medium_*");
            clips["clink"] = Load("impactMetal_light_*", "impactPlate_light_*", "metalClick");
            clips["hit"] = Load("impactPunch_heavy_*", "impactPunch_medium_*", "impactSoft_heavy_*", "body_hit", "punch_fall");
            clips["swing"] = Load("knifeSlice", "knifeSlice2", "chop");
            clips["scream"] = Load("death_scream", "male_scream", "grunt_fall");
            clips["fall"] = Load("bodyfall_dirt", "impactSoft_medium_*");
            clips["wood"] = Load("arrow_thud_soft", "impactWood_light_*", "impactWood_medium_*");
            clips["volley"] = Load("arrows_whoosh");
            clips["whinny"] = Load("horse_whinny", "horse_whinny_ex");
            clips["impact"] = Load("sword_hit_big", "impactPlate_heavy_*");
            clips["cheer"] = Load("cheer_men", "charge_call");
            AudioSource Bed(string name)
            {
                var s = gameObject.AddComponent<AudioSource>(); s.clip = Resources.Load<AudioClip>("Audio/Battle/" + name); s.loop = true; s.volume = 0; s.spatialBlend = 0;
                if (s.clip != null) { s.time = Random.Range(0, s.clip.length * 0.9f); s.Play(); }
                return s;
            }
            melee = Bed("melee_crowd_out"); horses = Bed("horse_gallop_by"); panic = Bed("panic"); army = Bed("army_angry");
            for (int i = 0; i < Voices; i++) { var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0; pool.Add(s); }
            if (FindAnyObjectByType<AudioListener>() == null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        // разовый звук: место (м) → стерео и громкость от середины кадра
        void Play(string kind, float x, float y, float vol, Rect view, float pitchJitter = 0.08f)
        {
            if (!clips.TryGetValue(kind, out var set) || set.Length == 0) return;
            float cx = view.center.x, cy = view.center.y, hw = view.width / 2, hh = view.height / 2;
            float dx = (x - cx) / Mathf.Max(1, hw), dy = (y - cy) / Mathf.Max(1, hh), d = Mathf.Sqrt(dx * dx + dy * dy);
            float v = vol * Volume * Duck * Mathf.Clamp01(1.25f - 0.6f * d);
            if (v < 0.02f) return;
            AudioSource src = null;
            foreach (var s in pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) { float best = float.MaxValue; foreach (var s in pool) if (s.volume < best) { best = s.volume; src = s; } if (best > v) return; }
            src.clip = set[Random.Range(0, set.Length)];
            src.volume = v; src.panStereo = Mathf.Clamp(dx * 0.8f, -0.9f, 0.9f); src.pitch = 1 + Random.Range(-pitchJitter, pitchJitter);
            src.Play();
            fading.RemoveAll(f => f.s == src);
            if (MaxLen.TryGetValue(kind, out var ml) && src.clip.length > ml) fading.Add((src, Time.unscaledTime + ml, v));
        }

        void Update()
        {
            var lk = viewer?.Rec;
            if (lk == null) UpdateBody(); else lock (lk) UpdateBody();
        }
        void UpdateBody()
        {
            if (viewer == null) return;
            // обрезка длинных: последние 0,3 с — на нет, потом стоп
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                var (fs, end, fv) = fading[i]; float left = end - Time.unscaledTime;
                if (left <= 0 || !fs.isPlaying) { fs.Stop(); fading.RemoveAt(i); continue; }
                if (left < 0.3f) fs.volume = fv * left / 0.3f;
            }
            var r = viewer.Rec; var cam = viewer.Cam;
            float vh = cam.orthographicSize, vw = vh * cam.aspect; var cp = cam.transform.position;
            var view = new Rect(cp.x - vw, -cp.y - vh, 2 * vw, 2 * vh);
            // приближение: 1 — видно отдельных бойцов (≲ 60 м по высоте кадра), 0 — карта целиком
            float near = Mathf.Clamp01(Mathf.InverseLerp(400, 40, vh));
            double t = viewer.T;
            if (r != rec || r == null || t < lastT - 1e-6 || t - lastT > 2) { rec = r; lastT = t; Resync(t); Beds(0, 0, 0, 0); return; }
            float dt = (float)(t - lastT);
            if (dt <= 0 || r.Frames.Count == 0) { Beds(0, 0, 0, 0); return; }
            int f0 = Mathf.Clamp((int)(t / r.Dt), 0, r.Frames.Count - 1), f1 = Mathf.Min(f0 + 1, r.Frames.Count - 1);
            Rect wide = new Rect(view.x - vw * 0.5f, view.y - vh * 0.5f, view.width * 2, view.height * 2);
            // ── фон: сколько людей в схватке, конных на ходу, бегущих, идущих — в кадре и около ──
            float fight = 0, gallop = 0, flee = 0, march = 0;
            for (int u = 0; u < r.Units.Count && f0 < r.Men.Count; u++)
            {
                var a = r.Frames[f0][u]; var b = r.Frames[f1][u];
                if (!wide.Contains(new Vector2(a[0], a[1]))) continue;
                int st = r.StateAt(u, f0); if (st == 2) continue;
                float men = (float)r.Units[u].Men, sp = Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1])) / (float)r.Dt;
                var mf = r.Men[f0][u];
                if (mf?.Eng != null) fight += mf.Eng.Length * (float)r.Units[u].PerMan;
                if (st == 1 || st == 3) flee += men;
                else if (r.Units[u].Type == "cavalry" && sp > 2.5f) gallop += men * Mathf.Clamp01(sp / 8);
                else if (sp > 0.6f) march += men;
            }
            float zoomK = 0.35f + 0.65f * near;
            Beds(Sat(fight, 60) * zoomK, Sat(gallop, 150) * zoomK, Sat(flee, 300) * zoomK, Sat(march, 1500) * 0.35f * zoomK);
            // ── события между прошлым и нынешним мигом ──
            clashBudget = Mathf.Min(14, clashBudget + dt * 14); thudBudget = Mathf.Min(6, thudBudget + dt * 6);
            float lo = (float)lastT, hi = (float)t; float evK = 0.25f + 0.75f * near;
            for (int ff = f0; ff <= f1 && ff < r.Men.Count; ff++)
                for (int u = 0; u < r.Men[ff].Length; u++)
                {
                    var mf = r.Men[ff][u]; if (mf?.Eng == null) continue;
                    for (int k = 0; k < mf.Eng.Length && clashBudget >= 1; k++)
                    {
                        int id = mf.Eng[k]; float x = mf.Xyh[3 * id], y = mf.Xyh[3 * id + 1];
                        if (float.IsNaN(x) || !view.Contains(new Vector2(x, y))) continue;
                        float pa = mf.EngPa[k], sw = mf.EngSw[k];
                        if (pa > lo && pa <= hi && ff == f0) { Play("clash", x, y, 0.55f * evK, view); clashBudget -= 1; }
                        else if (sw > lo && sw <= hi && ff == f0 && Random.value < 0.35f) { Play("swing", x, y, 0.3f * evK, view, 0.15f); clashBudget -= 0.5f; }
                    }
                }
            // павшие
            while (deadIdx < r.Dead.Count && r.Dead[deadIdx].T <= hi)
            {
                var dd = r.Dead[deadIdx++];
                if (dd.T <= lo || !view.Contains(new Vector2(dd.X, dd.Y))) continue;
                if (thudBudget >= 1) { Play("hit", dd.X, dd.Y, 0.45f * evK, view, 0.12f); thudBudget -= 1; }
                if (Gore && dd.Killed && Time.unscaledTime > screamAt && near > 0.3f) { Play("scream", dd.X, dd.Y, 0.32f, view, 0.12f); screamAt = Time.unscaledTime + Random.Range(1.2f, 3f); }
                if (dd.Part == 3 && Time.unscaledTime > whinnyAt) { Play("whinny", dd.X, dd.Y, 0.4f, view); whinnyAt = Time.unscaledTime + 2.5f; }
            }
            // стрелы: залп — свист (раз в 1,2 с, если в кадре вылетело хоть пять), в постройку — стук
            int launched = 0; float lx = 0, ly = 0;
            while (arrowIdx < r.Arrows.Count && r.Arrows[arrowIdx].T0 <= hi)
            {
                var ar = r.Arrows[arrowIdx];
                if (ar.T0 > lo && wide.Contains(new Vector2(ar.X0, ar.Y0))) { launched++; lx += ar.X0; ly += ar.Y0; }
                landing.Add(arrowIdx); arrowIdx++;
            }
            if (launched >= 5 && Time.unscaledTime > volleyAt) { Play("volley", lx / launched, ly / launched, 0.5f, view, 0.1f); volleyAt = Time.unscaledTime + 1.2f; }
            for (int i = landing.Count - 1; i >= 0; i--)
            {
                var ar = r.Arrows[landing[i]];
                if (float.IsInfinity(ar.T1)) continue;
                if (ar.T1 > hi) continue;
                landing.RemoveAt(i);
                if (ar.T1 <= lo || ar.End != 5 || thudBudget < 1 || !view.Contains(new Vector2(ar.X1, ar.Y1))) continue;
                Play("wood", ar.X1, ar.Y1, 0.35f * evK, view, 0.15f); thudBudget -= 1;
            }
            if (landing.Count > 20000) landing.RemoveRange(0, landing.Count - 20000);
            // сшибка: новая пара в схватке, где один — конница; «сплотились» — клич
            if (f0 > 0 && f0 < r.Fights.Count && (int)(lo / r.Dt) < f0)
            {
                var now = r.Fights[f0]; var was = r.Fights[f0 - 1];
                for (int q = 0; q + 1 < now.Length; q += 2)
                {
                    int a = now[q], b = now[q + 1]; bool seen = false;
                    for (int p = 0; p + 1 < was.Length && !seen; p += 2) seen = was[p] == a && was[p + 1] == b || was[p] == b && was[p + 1] == a;
                    if (seen || (r.Units[a].Type != "cavalry" && r.Units[b].Type != "cavalry")) continue;
                    var fa = r.Frames[f0][a];
                    if (!wide.Contains(new Vector2(fa[0], fa[1]))) continue;
                    Play("impact", fa[0], fa[1], 0.7f, view, 0.05f);
                    if (Time.unscaledTime > whinnyAt) { Play("whinny", fa[0], fa[1], 0.55f, view); whinnyAt = Time.unscaledTime + 2.5f; }
                }
                for (int u = 0; u < r.Units.Count; u++)
                    if (r.StateAt(u, f0) == 4 && r.StateAt(u, f0 - 1) != 4 && Time.unscaledTime > cheerAt)
                    {
                        var fu = r.Frames[f0][u];
                        if (wide.Contains(new Vector2(fu[0], fu[1]))) { Play("cheer", fu[0], fu[1], 0.6f, view, 0.03f); cheerAt = Time.unscaledTime + 4; }
                    }
            }
            lastT = t;
        }
        static float Sat(float v, float half) => v / (v + half);
        void Beds(float m, float h, float p, float a)
        {
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime / 0.6f);
            void To(AudioSource s, float target, float max) { if (s != null) s.volume += (target * max * Volume * Duck - s.volume) * k; }
            To(melee, m, 0.8f); To(horses, h, 0.7f); To(panic, p, 0.55f); To(army, a, 0.4f);
        }
        // перемотка или новая запись: указатели — на нынешний миг, без звуков за пропущенное
        void Resync(double t)
        {
            landing.Clear(); deadIdx = 0; arrowIdx = 0;
            if (rec == null) return;
            while (deadIdx < rec.Dead.Count && rec.Dead[deadIdx].T <= t) deadIdx++;
            while (arrowIdx < rec.Arrows.Count && rec.Arrows[arrowIdx].T0 <= t) arrowIdx++;
        }
    }
}
