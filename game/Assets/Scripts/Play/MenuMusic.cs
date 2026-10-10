// ═══════════ MenuMusic.cs — музыка главного меню (Алекс 10.10.2026: «средневековую музыку в заставку») ═══════════
// Своя мелодия, собирается при запуске (ничего не скачано): дорийский лад от ре, 6/8, около 70 с по кругу. Голоса — бурдон
// колёсной лиры (ре и ля), мелодия блокфлейты, лютня (щипок — Karplus–Strong), бубен; поверх — простая реверберация
// (зал). Счёт — в отдельном потоке (~1 с), потом AudioClip. Играет, пока открыто меню или битва не выбрана; в бою — гаснет.
// Громкость — «Музыка» в настройках.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Journal.Play
{
    public sealed class MenuMusic : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource src; Task<float[]> job; float level;
        public bool Want;                     // играть сейчас (задаёт интерфейс)
        public static float Volume = 0.7f;    // настройки, 0…1

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>(); src.loop = true; src.playOnAwake = false; src.spatialBlend = 0; src.volume = 0;
            job = Task.Run(Compose);
        }
        void Update()
        {
            if (job != null && job.IsCompleted)
            {
                if (job.Exception == null)
                {
                    var data = job.Result;
                    var clip = AudioClip.Create("Заставка", data.Length, 1, Rate, false); clip.SetData(data, 0);
                    src.clip = clip;
                }
                else Debug.LogException(job.Exception);
                job = null;
            }
            if (src == null || src.clip == null) return;
            // вступает за 2 с, гаснет за 1,5 с
            level = Mathf.MoveTowards(level, Want ? 1 : 0, Time.unscaledDeltaTime / (Want ? 2f : 1.5f));
            src.volume = level * level * Volume * 0.55f;
            if (level > 0 && !src.isPlaying) src.Play();
            else if (level <= 0 && src.isPlaying) src.Pause();
        }

        // ── партитура: восьмые в такте 6/8; ноты — MIDI (ре первой октавы — 62) ──
        static readonly (int n, int len)[][] Melody =
        {
            // A
            new[] { (69, 2), (67, 1), (65, 2), (64, 1) }, new[] { (62, 3), (69, 3) },
            new[] { (72, 2), (71, 1), (69, 2), (67, 1) }, new[] { (69, 6) },
            new[] { (74, 2), (72, 1), (69, 2), (67, 1) }, new[] { (65, 2), (67, 1), (69, 3) },
            new[] { (67, 2), (65, 1), (64, 2), (60, 1) }, new[] { (62, 6) },
            // B
            new[] { (65, 1), (67, 1), (69, 1), (72, 3) }, new[] { (74, 2), (76, 1), (74, 3) },
            new[] { (72, 2), (69, 1), (67, 2), (69, 1) }, new[] { (65, 3), (64, 3) },
            new[] { (65, 1), (67, 1), (69, 1), (74, 3) }, new[] { (72, 2), (69, 1), (71, 2), (67, 1) },
            new[] { (69, 2), (67, 1), (65, 2), (64, 1) }, new[] { (62, 6) },
        };
        // аккорд такта: основной тон (MIDI второй октавы) и мажор ли
        static readonly (int root, bool major)[] Chords =
        {
            (50, false), (50, false), (48, true), (45, false), (50, false), (41, true), (48, true), (50, false),
            (41, true), (50, false), (48, true), (41, true), (41, true), (48, true), (45, false), (50, false),
        };

        static double Hz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

        static float[] Compose()
        {
            double eighth = 60.0 / (66 * 3);                 // пунктирная четверть — 66 в минуту
            int bars = Melody.Length * 2;                    // A B A B; во второй раз мелодия на октаву выше в B
            double L = bars * 6 * eighth;                    // длина круга, с
            int N = (int)(L * Rate), tail = 3 * Rate;
            var mix = new float[N + tail];
            var rng = new System.Random(1066);

            // ── блокфлейта ──
            double t0 = 0;
            for (int b = 0; b < bars; b++)
            {
                var bar = Melody[b % Melody.Length]; bool second = b >= Melody.Length;
                double at = t0;
                foreach (var (n0, len) in bar)
                {
                    int n = second && b % Melody.Length >= 8 && b % Melody.Length < 15 ? n0 + 12 : n0;   // B во второй раз — выше
                    Recorder(mix, at, len * eighth * 0.96, Hz(n), second ? 0.17 : 0.2, rng);
                    at += len * eighth;
                }
                t0 += 6 * eighth;
            }
            // ── лютня: бас на «раз», аккорд на «три» и «шесть»; бубен ──
            for (int b = 0; b < bars; b++)
            {
                var (root, major) = Chords[b % Chords.Length];
                double bt = b * 6 * eighth;
                int third = root + (major ? 4 : 3), fifth = root + 7;
                Pluck(mix, bt, Hz(root - 12 + 12), 0.30, rng);
                Pluck(mix, bt + 2 * eighth, Hz(fifth + 12), 0.14, rng); Pluck(mix, bt + 2 * eighth + 0.012, Hz(third + 12), 0.12, rng);
                Pluck(mix, bt + 3 * eighth, Hz(fifth), 0.22, rng);
                Pluck(mix, bt + 5 * eighth, Hz(root + 12), 0.13, rng); Pluck(mix, bt + 5 * eighth + 0.012, Hz(third + 12), 0.11, rng);
                if (b >= 2)   // бубен вступает с третьего такта
                {
                    Drum(mix, bt, 0.34, true, rng); Drum(mix, bt + 3 * eighth, 0.22, true, rng);
                    Drum(mix, bt + 5 * eighth, 0.09, false, rng);
                    if (b % 4 == 3) Drum(mix, bt + 4 * eighth, 0.08, false, rng);
                }
            }
            // ── зал: четыре гребёнки и два всепропускающих (Шрёдер), хвост за кругом — в начало ──
            var wet = Reverb(mix);
            for (int i = 0; i < mix.Length; i++) mix[i] = mix[i] * 0.82f + wet[i] * 0.35f;
            var outp = new float[N];
            for (int i = 0; i < N; i++) outp[i] = mix[i];
            for (int i = N; i < mix.Length; i++) outp[i - N] += mix[i];
            // ── бурдон: строго периодичен на круге — шов без щелчка ──
            Drone(outp, L, Hz(50), 0.075); Drone(outp, L, Hz(57), 0.05); Drone(outp, L, Hz(38), 0.04);
            // уровень: пик — −3 дБ
            float peak = 1e-6f; foreach (var v in outp) peak = Math.Max(peak, Math.Abs(v));
            float k = 0.7f / peak; for (int i = 0; i < N; i++) outp[i] *= k;
            return outp;
        }

        // блокфлейта: основной тон и обертоны, дыхание, мягкая атака, вибрато после начала ноты
        static void Recorder(float[] mix, double at, double dur, double f, double amp, System.Random rng)
        {
            int s0 = (int)(at * Rate), n = (int)((dur + 0.12) * Rate);
            double ph = 0, noise = 0;
            for (int i = 0; i < n && s0 + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double env = t < 0.045 ? t / 0.045 : t < dur ? 1 - 0.15 * (t - 0.045) / Math.Max(0.05, dur) : Math.Max(0, 1 - (t - dur) / 0.12) * 0.85;
                double vib = t > 0.18 ? 1 + 0.0045 * Math.Sin(2 * Math.PI * 5.3 * t) * Math.Min(1, (t - 0.18) / 0.3) : 1;
                ph += 2 * Math.PI * f * vib / Rate;
                noise = noise * 0.86 + (rng.NextDouble() * 2 - 1) * 0.14;
                double v = Math.Sin(ph) + 0.22 * Math.Sin(2 * ph) + 0.07 * Math.Sin(3 * ph) + 0.035 * noise * (t < 0.08 ? 3 : 1);
                mix[s0 + i] += (float)(v * env * amp);
            }
        }
        // лютня: Karplus–Strong — шум в линии задержки, усреднение гасит верха
        static void Pluck(float[] mix, double at, double f, double amp, System.Random rng)
        {
            int s0 = (int)(at * Rate), len = Math.Max(2, (int)(Rate / f)), n = (int)(2.2 * Rate);
            var buf = new double[len];
            for (int i = 0; i < len; i++) buf[i] = rng.NextDouble() * 2 - 1;
            double prev = 0;
            for (int i = 0; i < n && s0 + i < mix.Length; i++)
            {
                int j = i % len; double cur = buf[j];
                buf[j] = 0.4985 * (cur + buf[(j + 1) % len]);
                double v = 0.6 * cur + 0.4 * prev; prev = cur;   // мягче — меньше звона
                mix[s0 + i] += (float)(v * amp * Math.Min(1, i / 40.0));
            }
        }
        // бубен: низкий удар (тон падает), шлепок кожи — короткий шум
        static void Drum(float[] mix, double at, double amp, bool low, System.Random rng)
        {
            int s0 = (int)(at * Rate), n = (int)((low ? 0.45 : 0.12) * Rate);
            double ph = 0;
            for (int i = 0; i < n && s0 + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double f = low ? 58 + 50 * Math.Exp(-t * 30) : 190;
                ph += 2 * Math.PI * f / Rate;
                double body = Math.Sin(ph) * Math.Exp(-t * (low ? 7 : 28));
                double slap = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * (low ? 80 : 55)) * (low ? 0.35 : 0.8);
                mix[s0 + i] += (float)((body + slap) * amp);
            }
        }
        // бурдон колёсной лиры: обертоны 1/n (как пила, но мягче), медленное «дыхание» колеса; частоты — целое число
        // периодов на круге, чтобы круг сходился без щелчка
        static void Drone(float[] outp, double L, double f, double amp)
        {
            int N = outp.Length;
            double fc = Math.Round(f * L) / L, sw = Math.Round(0.11 * L) / L;
            for (int i = 0; i < N; i++)
            {
                double t = (double)i / Rate, v = 0;
                for (int h = 1; h <= 9; h++) v += Math.Sin(2 * Math.PI * fc * h * t) / (h * (1 + 0.25 * h));
                double swell = 0.85 + 0.15 * Math.Sin(2 * Math.PI * sw * t);
                outp[i] += (float)(v * amp * swell);
            }
        }
        static float[] Reverb(float[] x)
        {
            int[] combs = { 1557, 1617, 1491, 1422 }; int[] aps = { 225, 556 };
            var y = new float[x.Length];
            foreach (var d in combs)
            {
                var buf = new float[d]; int p = 0; float lp = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    float o = buf[p]; lp = o * 0.6f + lp * 0.4f;
                    buf[p] = x[i] + lp * 0.8f; p = (p + 1) % d;
                    y[i] += o * 0.25f;
                }
            }
            foreach (var d in aps)
            {
                var buf = new float[d]; int p = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    float b = buf[p], o = -y[i] + b;
                    buf[p] = y[i] + b * 0.5f; p = (p + 1) % d;
                    y[i] = o;
                }
            }
            return y;
        }
    }
}
