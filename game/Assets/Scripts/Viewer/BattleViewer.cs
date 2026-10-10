// ═══════════ BattleViewer.cs — смотрелка боя (И2, шаг 1): движок считает бой в Unity, сцена проигрывает ход ═══════════
// Сцену выбирают кнопкой сверху; бой считается в фоновом потоке (BattleRecord.Run), потом проигрывается: пробел — пауза,
// ← → — кадр, колесо — приближение к курсору, тянуть мышью — сдвиг, F — вся карта, 1…8 — сцены; F6 — стиль облика
// всем отрядам по кругу (В16), F7 — свет на природе, F8 — эффект миниатюр.
// Режим игры (SetLive): показывает чужую живую запись (ход с приказами, Play/*), сцены не трогает; время ставит игра
// (T, Playing, Speed); ЛКМ/ПКМ — игре (выбор, приказы), камера — средней кнопкой, WASD/стрелками, колесом, F.
// Шаг 1 — простой рисунок: земля по клеткам 5 м, тела — плашки цвета стороны, павшие — пятна, стрелы — чёрточки.
// Облик полигона (земля шейдером, бойцы из атласов) — следующими шагами, поверх этого же каркаса.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BattleCore;
using UnityEngine;
using UnityEngine.InputSystem;
using Terrain = BattleCore.Terrain;

namespace Journal.Viewer
{
    public sealed class BattleViewer : MonoBehaviour
    {
        // цвет стороны (В4): отряды одной фракции — оттенки её цвета
        static readonly Dictionary<int, string[]> Side = new Dictionary<int, string[]>
        {
            [1] = new[] { "#b5372b", "#c8662c", "#9b2d52", "#cf8f2e", "#8a2c2a", "#d4573c" },
            [2] = new[] { "#2f63a8", "#2c8b9e", "#5a4aa2", "#4583c8", "#27497f", "#5b7fb6" },
            [3] = new[] { "#2f7d4a", "#4f9a3a", "#24685e", "#6f9a2e" },
            [4] = new[] { "#c9a227", "#d9c04a", "#a8841c", "#e0b84e" },
        };
        // земля по коду клетки (Terrain.cs), как в полигоне
        static readonly Dictionary<int, string> Ground = new Dictionary<int, string>
        {
            [1] = "#9ca86c", [2] = "#b9a072", [3] = "#dccb98", [4] = "#eceff2", [5] = "#919f62", [6] = "#5f7444", [7] = "#5b8aa5",
            [8] = "#88aebd", [9] = "#9a7448", [10] = "#77885f", [11] = "#9a968b", [12] = "#c6bfae", [13] = "#8a6239", [14] = "#b0a898",
            [15] = "#946c43", [16] = "#587a7b", [17] = "#977d59", [18] = "#ad5a3c", [19] = "#b4ad9f", [20] = "#bdb5a5",
        };

        Recording rec;
        Task<Recording> job; string jobNote = "";
        int scene = -1, pendingScene = -1;
        double t; bool playing; float speed = 1;
        Camera cam;
        Material mat;
        GameObject groundGo; Mesh unitsMesh;
        Color32[] unitCol, unitColRaw;   // цвет стороны: для плашек (в линейном пространстве) и для шейдера бойцов (как есть)
        Color32[] unitEdge;              // обводка плашки: тёмная у светлых цветов, светлая у тёмных — чтобы отряд читался на любой земле
        MenView menView;
        FortView fortView;               // укрепления из клеток карты (В19): стены, башни, ворота, проломы
        Banners banners;                 // знамёна над отрядами (Г7, В11) — и издали, и вблизи
        const float MenFrom = 3;         // px на метр: ближе — бойцы из рисунка полигона, дальше — плашки
        readonly List<Rect> uiRects = new List<Rect>();
        bool dragging; Vector2 dragFrom; Vector3 camFrom;
        bool live;                       // режим игры: запись снаружи (SetLive)

        // ── для режима игры (Play/*) ──
        // Слои рисунка: земля 0, кровь 4, павшие 5–6, кони 9, бойцы и плашки 10, стрелы в полёте 20; 21–39 — за смотрелкой
        // (знамёна, дым, подсветка), 50+ — подсказки приказов игры.
        public bool ShowGui = true;                       // false — без OnGUI (кнопок сцен и нижней полосы)
        public bool InputBlocked;                         // окно игры поверх (меню, редактор армий) — камера клавиш и мыши не слушает
        public bool PlayInput;                            // управление игры: ЛКМ/ПКМ — игре, камера — средней кнопкой и клавишами (SetLive включает)
        public Func<Vector2, bool> OverExternalUi;        // экранная точка над интерфейсом игры — колесо и сдвиг камеры её не трогают
        public Vector4 Insets;                            // px экрана под панелями игры: слева, сверху, справа, снизу — кадр (F) в остаток
        public bool Live => live;
        public Recording Rec => rec;
        public Camera Cam { get { Init(); return cam; } }
        // чьими глазами смотрим (Г18, туман войны): 0 — ГМ, видно всё; иначе — сторона: чужих, кого она не видит, не рисуем
        public static int ViewSide;
        public double T { get => t; set => t = rec == null ? 0 : Math.Max(0, Math.Min(rec.Seconds, value)); }
        public bool Playing { get => playing; set => playing = value; }
        public float Speed { get => speed; set => speed = Mathf.Max(0, value); }
        // Живая запись (Recorder.Rec): кадры дописываются по ходу счёта, показ идёт до последнего готового кадра.
        // Recorder.Snap и показ — в главном потоке (или Snap под lock(rec), см. Recorder).
        public void SetLive(Recording r, bool fit = true)
        {
            Init();
            // сцена смотрелки ещё считается в фоне — дождаться: два боя разом движок пока не считает (Soldiers.Jostle)
            if (job != null) { try { job.Wait(); } catch (AggregateException) { } job = null; }
            live = true; PlayInput = true; pendingScene = -1; scene = -1;
            rec = r; t = 0; playing = false;
            BuildGround(); if (fit) FitView();
        }
        // карта без отрядов (редактор карт, Г102): земля и укрепления, камера — как в игре; fit — показать всю карту
        public void ShowMap(TerrainMap m, string name, bool fit)
        {
            Init();
            if (job != null) { try { job.Wait(); } catch (AggregateException) { } job = null; }
            var r = new Recording { Name = name, Map = m, W = Terrain.WidthM(m), H = Terrain.HeightM(m), Done = true, States = new List<int>[0] };
            r.Frames.Add(new float[0][]); r.Men.Add(new MenFrame[0]); r.Soldiers.Add(new int[0]); r.Heads.Add(new Dictionary<int, float>()); r.Fights.Add(new int[0]);
            live = true; PlayInput = true; pendingScene = -1; scene = -1;
            rec = r; t = 0; playing = false;
            BuildGround(); if (fit) FitView();
        }
        // экран (px) ↔ карта (м, y вниз)
        public Vector2 ScreenToMap(Vector2 screen) { Init(); var w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10)); return new Vector2(w.x, -w.y); }
        public Vector2 MapToScreen(Vector2 map) { Init(); var s = cam.WorldToScreenPoint(new Vector3(map.x, -map.y, 0)); return new Vector2(s.x, s.y); }

        // для проверки из CLI: бой посчитан и показан; что сейчас на экране
        public bool Ready => rec != null && job == null;
        public int SceneIndex => scene;
        public string Status => job != null ? "считаю: " + jobNote : rec == null ? "пусто" : $"{rec.Name}: {t:0.0} из {rec.Seconds:0.0} с, кадров {rec.Frames.Count}, павших {rec.Dead.Count}, стрел {rec.Arrows.Count}";
        public void Show(int i, double at = 0) { Load(i); t = at; }
        public void Seek(double at) { if (rec != null) { t = Math.Max(0, Math.Min(rec.Seconds, at)); playing = false; } }
        public void LookAt(float x, float y, float size) { cam.transform.position = new Vector3(x, -y, -10); cam.orthographicSize = size; }

        void Init()
        {
            if (cam != null) return;
            cam = Camera.main;
            cam.orthographic = true;
            cam.backgroundColor = new Color32(23, 22, 26, 255);
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            mat = new Material(sh);
            var units = new GameObject("Отряды");
            unitsMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            unitsMesh.MarkDynamic();
            units.AddComponent<MeshFilter>().sharedMesh = unitsMesh;
            var ur = units.AddComponent<MeshRenderer>(); ur.sharedMaterial = mat; ur.sortingOrder = 10;
            menView = new MenView(transform);
            fortView = new FortView(transform);
            banners = new Banners(transform);
            MiniatureLook.Setup(cam);   // облик «миниатюры на столе» (В15)
            gameObject.AddComponent<BattleAudio>().Init(this);   // звук боя по записи
        }
        void Start()
        {
            Init();
            if (!live) Load(0);
        }

        void Load(int i)
        {
            if (live) return;                                 // в режиме игры сцен нет
            if (job != null) { pendingScene = i; return; }   // бой ещё считается — выберем, когда досчитает
            scene = i; playing = false; t = 0; jobNote = "строю сцену";
            var (name, make) = ViewerScenes.All[i];
            job = Task.Run(() => BattleRecord.Run(make(), s => jobNote = s));
        }

        // запись дописывается в другом потоке (ход игры, Г111 п.7) — читать её под lock(запись)
        void Update()
        {
            var lk = rec;
            if (lk == null) UpdateBody(); else lock (lk) UpdateBody();
        }
        void UpdateBody()
        {
            if (job != null && job.IsCompleted)
            {
                if (job.IsFaulted) { Debug.LogException(job.Exception); jobNote = "ошибка: " + job.Exception.InnerException?.Message; }
                else if (!live) { rec = job.Result; BuildGround(); FitView(); playing = true; }
                job = null;
                if (pendingScene >= 0) { int p = pendingScene; pendingScene = -1; Load(p); }
            }
            HandleInput();
            if (rec == null || rec.Frames.Count == 0) { unitsMesh.Clear(); menView?.Hide(); banners?.Hide(); return; }
            // живая запись (ход ещё считается) — играем до последнего готового кадра и ждём; досчитанная — стоп в конце
            if (playing) { t += Time.deltaTime * speed; if (t >= rec.Seconds) { t = rec.Seconds; if (rec.Done) playing = false; } }
            DrawUnits();
        }

        // ── ввод: камера и проигрывание ──
        // мышь — только когда окно игры в фокусе и курсор внутри него: нажатие в другом окне (или на другом мониторе)
        // иначе начинало «перетаскивание», которое не заканчивалось и держало камеру на месте — приблизил, а отряда нет
        static bool InScreen(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x < Screen.width && p.y < Screen.height;
        bool OverUI(Vector2 screen)
        {
            if (OverExternalUi != null && OverExternalUi(screen)) return true;
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            foreach (var r in uiRects) if (r.Contains(gui)) return true;
            return false;
        }
        void HandleInput()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (!Application.isFocused || InputBlocked) { dragging = false; return; }
            // облик: F6 — стиль всем отрядам по кругу (В16), F7 — свет на природе (бойцы с В18 плоские), F8 — эффект миниатюр
            if (kb != null && kb.f6Key.wasPressedThisFrame) CycleStyle();
            if (kb != null && kb.f7Key.wasPressedThisFrame) MenView.Light = !MenView.Light;
            if (kb != null && kb.f8Key.wasPressedThisFrame) MiniatureLook.On = !MiniatureLook.On;
            if (PlayInput) { GameInput(mouse, kb); return; }
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame && rec != null) { if (t >= rec.Seconds) t = 0; playing = !playing; }
                if (kb.rightArrowKey.wasPressedThisFrame && rec != null) { playing = false; t = Math.Min(rec.Seconds, Math.Floor(t / rec.Dt + 1e-6) * rec.Dt + rec.Dt); }
                if (kb.leftArrowKey.wasPressedThisFrame && rec != null) { playing = false; t = Math.Max(0, Math.Ceiling(t / rec.Dt - 1e-6) * rec.Dt - rec.Dt); }
                if (kb.fKey.wasPressedThisFrame) FitView();
                for (int k = 0; k < ViewerScenes.All.Length && k < 9; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame) Load(k);
            }
            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            Zoom(mouse, mp);
            if ((mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) && InScreen(mp) && !OverUI(mp)) { dragging = true; dragFrom = mp; camFrom = cam.transform.position; }
            if (dragging)
            {
                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) dragging = false;
                else
                {
                    float k = 2 * cam.orthographicSize / Screen.height;
                    Vector2 d = (mp - dragFrom) * k;
                    cam.transform.position = camFrom - new Vector3(d.x, d.y, 0);
                }
            }
        }
        // колесо — приближение к точке под курсором
        void Zoom(Mouse mouse, Vector2 mp)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) < 0.01f || !InScreen(mp) || OverUI(mp)) return;
            Vector3 before = cam.ScreenToWorldPoint(mp);
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * Mathf.Pow(0.85f, Mathf.Sign(wheel)), 3f, 4000f);
            Vector3 after = cam.ScreenToWorldPoint(mp);
            cam.transform.position += before - after;
        }
        // режим игры: ЛКМ/ПКМ — игре; камера — средняя кнопка, WASD/стрелки (быстрее с Shift), колесо, F
        void GameInput(Mouse mouse, Keyboard kb)
        {
            if (kb != null)
            {
                if (kb.fKey.wasPressedThisFrame) FitView();
                bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;   // Ctrl+A — «выбрать всех» в игре, не сдвиг
                var d = Vector2.zero;
                if (!ctrl)
                {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) d.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) d.x += 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) d.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) d.y -= 1;
                }
                if (d != Vector2.zero)
                {
                    float v = cam.orthographicSize * 1.4f * (kb.shiftKey.isPressed ? 2.5f : 1);   // ~0,7 экрана в секунду
                    cam.transform.position += (Vector3)(d.normalized * v * Time.unscaledDeltaTime);
                }
            }
            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            Zoom(mouse, mp);
            if (mouse.middleButton.wasPressedThisFrame && InScreen(mp) && !OverUI(mp)) { dragging = true; dragFrom = mp; camFrom = cam.transform.position; }
            if (dragging)
            {
                if (!mouse.middleButton.isPressed) dragging = false;
                else cam.transform.position = camFrom - (Vector3)((mp - dragFrom) * (2 * cam.orthographicSize / Screen.height));
            }
        }
        // ── кадр: вся карта, рамка или точка — в части экрана, свободной от интерфейса игры (Insets) ──
        public void FitView() { if (rec != null) FitRect(0, 0, (float)rec.W, (float)rec.H); }
        public void FitRect(float x0, float y0, float x1, float y1)
        {
            Init();
            var (fw, fh) = FreeSize();
            float hw = Mathf.Max(1, x1 - x0) / 2, hh = Mathf.Max(1, y1 - y0) / 2;
            cam.orthographicSize = Mathf.Clamp(Mathf.Max(hh * Screen.height / fh, hw * Screen.height / fw) * 1.06f, 3f, 4000f);
            Focus((x0 + x1) / 2, (y0 + y1) / 2);
        }
        // точку карты — в середину свободной части экрана, приближение прежнее
        public void Focus(float x, float y)
        {
            Init();
            var (fw, fh) = FreeSize();
            float k = 2 * cam.orthographicSize / Screen.height;   // метров на пиксель
            float dx = Insets.x + fw / 2 - Screen.width / 2f, dy = Insets.w + fh / 2 - Screen.height / 2f;
            cam.transform.position = new Vector3(x - dx * k, -y - dy * k, -10);
        }
        (float w, float h) FreeSize() => (Mathf.Max(64, Screen.width - Insets.x - Insets.z), Mathf.Max(64, Screen.height - Insets.y - Insets.w));

        // неровность края по виду земли (0 — край ровно по клеткам), как в полигоне; постройки — ровно
        static readonly Dictionary<int, float> Amp = new Dictionary<int, float>
        {
            [1] = .28f, [2] = .16f, [3] = .3f, [4] = .3f, [5] = .34f, [6] = .3f, [7] = .2f, [8] = .14f, [10] = .3f, [11] = .3f, [16] = .06f, [17] = .05f,
        };

        // ── земля (В1): шейдер Ground — попиксельный проход полигона на видеокарте ──
        // Клетки карты — в текстуру (R вид, G высота·16, B высота, сглаженная 3 × 3, ·16), цвета видов — в палитру 32 × 1
        void BuildGround()
        {
            if (groundGo) Destroy(groundGo);
            var m = rec.Map;
            var cells = new Texture2D(m.W, m.H, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[m.W * m.H];
            // под укреплениями (стена, ворота, башня, частокол, пролом) и домами — земля соседних клеток: их рисует FortView поверх,
            // а земля не должна обводить клетки укреплений своим контуром
            bool forts = fortView != null && fortView.Ok;
            bool Fort(int k) => k == 12 || k == 13 || k == 14 || k == 15 || k == 18 || k == 20;   // и дома: крыши тоже рисует FortView
            int Under(int x, int y)
            {
                // сначала — суша: башня у рва стоит на берегу (10.10.2026: под угловой башней был квадрат воды с кромкой берега)
                var cnt = new Dictionary<int, int>(); int best = 1, bn = 0;
                for (int pass = 0; pass < 2 && bn == 0; pass++)
                for (int r = 1; r <= 3 && bn == 0; r++)
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= m.W || yy >= m.H) continue;
                            int k = m.T[yy * m.W + xx]; if (k == 0) k = 1; if (Fort(k) || pass == 0 && (k == 7 || k == 16)) continue;
                            cnt.TryGetValue(k, out int c0); cnt[k] = ++c0; if (c0 > bn) { bn = c0; best = k; }
                        }
                return best;
            }
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    int i = y * m.W + x, sum = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < m.W && yy < m.H) { sum += m.Z[yy * m.W + xx]; n++; }
                        }
                    int kind = m.T[i] == 0 ? 1 : m.T[i];
                    if (forts && Fort(kind)) kind = Under(x, y);
                    px[i] = new Color32((byte)kind, (byte)Mathf.Min(255, m.Z[i] * 16), (byte)Mathf.Min(255, Mathf.RoundToInt(16f * sum / n)), 255);
                }
            cells.SetPixels32(px); cells.Apply(false, true);
            var palTex = new Texture2D(32, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pc = new Color32[32];
            for (int k = 0; k < 32; k++)
            {
                var c = Hex(Ground.TryGetValue(k, out var s) ? s : Ground[1]);
                c.a = (byte)Mathf.RoundToInt((Amp.TryGetValue(k, out var a) ? a : 0) / 0.5f * 255);
                pc[k] = c;
            }
            palTex.SetPixels32(pc); palTex.Apply(false, true);
            groundGo = new GameObject("Земля");
            float W = (float)rec.W, H = (float)rec.H;
            Material gm;
            if (rec.Image != null)
            {
                // карта из сохранения трекера — картинкой (sRGB, сглаженная, с мипами)
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                tex.LoadImage(rec.Image);
                gm = new Material(Shader.Find("Journal/MapImage")) { mainTexture = tex };   // непрозрачная, под всеми слоями
            }
            else
            {
                gm = new Material(Shader.Find("Journal/Ground"));
                gm.SetTexture("_Cells", cells); gm.SetTexture("_Pal", palTex);
                gm.SetVector("_Info", new Vector4(m.W, m.H, (float)(rec.W / m.W), 1));
            }
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0, -H, 1), new Vector3(W, -H, 1), new Vector3(W, 0, 1), new Vector3(0, 0, 1) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            groundGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = groundGo.AddComponent<MeshRenderer>(); r.sharedMaterial = gm; r.sortingOrder = 0;
            // цвет отряда: оттенок стороны по порядку внутри фракции
            unitCol = new Color32[rec.Units.Count]; unitColRaw = new Color32[rec.Units.Count];
            var seen = new Dictionary<int, int>();
            for (int i = 0; i < rec.Units.Count; i++)
            {
                var info = rec.Units[i];
                if (info.Color != null)
                {
                    // цвет фракции из сохранения; отряды одного цвета — чуть светлее/темнее по порядку
                    seen.TryGetValue(info.Color.GetHashCode(), out int j); seen[info.Color.GetHashCode()] = j + 1;
                    Color.RGBToHSV(Hex(info.Color), out float hh, out float ss, out float vv);
                    unitColRaw[i] = Color.HSVToRGB(hh, ss, Mathf.Clamp01(vv * (1 - 0.07f * (j % 4)) + (vv < 0.15f ? 0.05f * (j % 4) : 0)));
                    unitCol[i] = Lin(unitColRaw[i]);
                    continue;
                }
                int f = info.Faction; seen.TryGetValue(f, out int k); seen[f] = k + 1;
                var pal = Side.TryGetValue(f, out var p) ? p : Side[1];
                unitColRaw[i] = Hex(pal[k % pal.Length]); unitCol[i] = Lin(unitColRaw[i]);
            }
            unitEdge = new Color32[rec.Units.Count];
            for (int i = 0; i < rec.Units.Count; i++)
            {
                var c = unitColRaw[i]; float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                unitEdge[i] = Lin(lum < 110 ? new Color32(232, 226, 210, 190) : new Color32(16, 13, 10, 170));
            }
            menView?.SetRecording(rec);
            fortView?.SetMap(rec.Map);
        }

        // ── отряды: тело — плашка ширина × глубина, повёрнутая по курсу; павшие — пятна; стрелы в полёте — чёрточки ──
        readonly List<Vector3> V = new List<Vector3>(); readonly List<Color32> C = new List<Color32>(); readonly List<int> I = new List<int>();
        void Quad(float cx, float cy, float w, float d, float headDeg, Color32 c)
        {
            // мир Unity: X = x, Y = −y; курс 0° — вверх по карте, по часовой
            float a = headDeg * Mathf.Deg2Rad, s = Mathf.Sin(a), co = Mathf.Cos(a);
            Vector2 fwd = new Vector2(s, co), right = new Vector2(co, -s);
            Vector2 p = new Vector2(cx, -cy);
            int b = V.Count;
            V.Add(p - right * (w / 2) - fwd * (d / 2)); V.Add(p + right * (w / 2) - fwd * (d / 2));
            V.Add(p + right * (w / 2) + fwd * (d / 2)); V.Add(p - right * (w / 2) + fwd * (d / 2));
            for (int k = 0; k < 4; k++) C.Add(c);
            I.Add(b); I.Add(b + 2); I.Add(b + 1); I.Add(b); I.Add(b + 3); I.Add(b + 2);
        }
        void DrawUnits()
        {
            V.Clear(); C.Clear(); I.Clear();
            // близко — бойцы из рисунка полигона (MenView); плашки, павшие и стрелы ниже — только издали
            float ppm = Screen.height / (2 * cam.orthographicSize);
            var cp = cam.transform.position; float vh = cam.orthographicSize, vw = vh * cam.aspect;
            var viewRect = new Rect(cp.x - vw, -cp.y - vh, 2 * vw, 2 * vh);
            fortView?.Gates(rec, t);
            bool near = menView != null && menView.Ok && ppm >= MenFrom;
            if (near) menView.Draw(t, unitColRaw, viewRect, ppm);
            banners?.Draw(rec, t, unitColRaw, Lin, viewRect, ppm, near ? menView.CmdAt : (System.Func<int, Vector3?>)null);   // личный стяг — где полководец
            if (near) { unitsMesh.Clear(); return; }
            menView?.Hide();
            double ft = t / rec.Dt; int f0 = Math.Min((int)Math.Floor(ft), rec.Frames.Count - 1), f1 = Math.Min(f0 + 1, rec.Frames.Count - 1); float q = (float)(ft - f0);
            // павшие — под отрядами
            var blood = Lin(new Color32(110, 22, 18, 255));
            foreach (var dd in rec.Dead) if (dd.Frame <= f0) Quad(dd.X, dd.Y, 0.9f, 1.6f, dd.Dir + 90, blood);   // удар (угол на карте) → курс: +90°
            var A = rec.Frames[f0]; var B = rec.Frames[f1];
            float edge = 1.5f / ppm;   // обводка ~1,5 px
            for (int u = 0; u < A.Length; u++)
            {
                var a = A[u]; var b = B[u]; var info = rec.Units[u];
                if (rec.StateAt(u, f0) == 2 || !rec.Visible(u, f0, ViewSide)) continue;   // ушёл с поля или не виден (туман)
                var col = unitCol[u]; var ec = unitEdge[u];
                float h0 = a[2], dh = Mathf.DeltaAngle(a[2], b[2]);
                // бегущий (Г100): не плашки колонн, а его бойцы — точками не меньше ~2,5 px
                int stU = rec.StateAt(u, f0);
                if ((stU == 1 || stU == 3) && f0 < rec.Men.Count)
                {
                    var m0 = rec.Men[f0][u]; var m1 = f1 < rec.Men.Count ? rec.Men[f1][u] : m0; float dot = Mathf.Max(1.1f, 2.5f / ppm);
                    for (int pass = 0; pass < 2; pass++)
                        for (int id = 1; id < m0.Xyh.Length / 3; id++)
                        {
                            float x = m0.Xyh[3 * id]; if (float.IsNaN(x)) continue;
                            float y = m0.Xyh[3 * id + 1];
                            if (3 * id + 1 < m1.Xyh.Length && !float.IsNaN(m1.Xyh[3 * id])) { x += (m1.Xyh[3 * id] - x) * q; y += (m1.Xyh[3 * id + 1] - y) * q; }
                            float e = pass == 0 ? 2 * edge : 0;
                            Quad(x, y, dot + e, dot + e, 0, pass == 0 ? ec : col);
                        }
                    continue;
                }
                // два прохода: сначала обводки всех тел отряда, потом заливки — иначе между соседними телами видны швы
                for (int pass = 0; pass < 2; pass++)
                    for (int k = 0; 5 + 2 * k < a.Length; k++)
                    {
                        float x = a[4 + 2 * k], y = a[5 + 2 * k];
                        if (float.IsNaN(x)) continue;
                        if (5 + 2 * k < b.Length && !float.IsNaN(b[4 + 2 * k])) { x += (b[4 + 2 * k] - x) * q; y += (b[5 + 2 * k] - y) * q; }
                        float head = rec.Heads[f0].TryGetValue(u * 65536 + k, out var hd) ? hd : h0 + dh * q;
                        var fig = k < info.Figs.Count ? info.Figs[k] : info.Figs[0];
                        float e = pass == 0 ? 2 * edge : 0;
                        Quad(x, y, (float)fig[0] + e, (float)fig[1] + e, head, pass == 0 ? ec : col);
                    }
            }
            // стрелы в полёте — чёрточки по ходу (полёт — как у бойцов вблизи: MenView.ArrowAt, конец может быть ещё не известен)
            var ink = Lin(new Color32(30, 24, 18, 255));
            foreach (var ar in rec.Arrows)
            {
                if (ar.T0 > t || ar.T1 <= t || !MenView.ArrowAt(ar, (float)t, out var x, out var y, out _, out var ang, out _)) continue;
                Quad(x, y, 0.12f, 1.2f, ang * Mathf.Rad2Deg + 90, ink);
            }
            unitsMesh.Clear();
            unitsMesh.SetVertices(V); unitsMesh.SetColors(C); unitsMesh.SetTriangles(I, 0);
            unitsMesh.RecalculateBounds();
        }

        // ── интерфейс ──
        // стиль облика всем отрядам по кругу (В16): как в данных → западный → … → дальневосточный; подпись — на 2,5 с
        static readonly string[] StyleCycle = { null, "west", "north", "east", "south", "fareast" };
        static readonly string[] StyleNames = { "как в данных", "западный", "северный", "восточный", "южный", "дальневосточный" };
        int styleIdx; float styleToast;
        void CycleStyle() { styleIdx = (styleIdx + 1) % StyleCycle.Length; MenView.ForceStyle = StyleCycle[styleIdx]; menView?.Restyle(); styleToast = Time.unscaledTime + 2.5f; }

        void OnGUI()
        {
            if (Time.unscaledTime < styleToast)
                GUI.Label(new Rect(Screen.width - 330, 52, 320, 26), $"Стиль облика: {StyleNames[styleIdx]} (F6)",
                    new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.98f, 0.95f, 0.85f) } });
            uiRects.Clear();
            if (!ShowGui) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.92f, 0.9f, 0.85f) } };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            // сцены
            float x = 10, y = 8;
            for (int i = 0; i < ViewerScenes.All.Length && !live; i++)
            {
                string label = $"{i + 1}. {ViewerScenes.All[i].Name}";
                float w = btn.CalcSize(new GUIContent(label)).x + 12;
                if (x + w > Screen.width - 10) { x = 10; y += 30; }
                var r = new Rect(x, y, w, 26); uiRects.Add(r);
                var old = GUI.backgroundColor; if (i == scene) GUI.backgroundColor = new Color(0.9f, 0.65f, 0.3f);
                if (GUI.Button(r, label, btn)) Load(i);
                GUI.backgroundColor = old;
                x += w + 6;
            }
            // низ: проигрывание
            float by = Screen.height - 40;
            var bar = new Rect(0, by - 6, Screen.width, 46); uiRects.Add(bar);
            GUI.Box(bar, GUIContent.none);
            if (job != null && !live) { GUI.Label(new Rect(12, by, 600, 28), $"Считаю бой «{ViewerScenes.All[scene].Name}»: {jobNote}…", st); return; }
            if (rec == null || rec.Frames.Count == 0) return;
            if (GUI.Button(new Rect(10, by, 44, 28), playing ? "❚❚" : "▶", btn)) { if (t >= rec.Seconds) t = 0; playing = !playing; }
            float sx = 60;
            foreach (var sp in new[] { 1f, 2f, 4f })
            {
                var old = GUI.backgroundColor; if (Mathf.Approximately(speed, sp)) GUI.backgroundColor = new Color(0.9f, 0.65f, 0.3f);
                if (GUI.Button(new Rect(sx, by, 40, 28), "×" + sp, btn)) speed = sp;
                GUI.backgroundColor = old; sx += 44;
            }
            int turn = Math.Min(rec.Turns, (int)Math.Floor(t / rec.TurnSec + 1e-9) + 1);
            double tin = t - (turn - 1) * rec.TurnSec;
            GUI.Label(new Rect(sx + 8, by + 3, 130, 24), $"ход {turn} · {tin:0.0} с", st);
            float nt = GUI.HorizontalSlider(new Rect(sx + 140, by + 9, Screen.width - sx - 160, 20), (float)t, 0, (float)rec.Seconds);
            if (Math.Abs(nt - t) > 1e-3) { t = nt; playing = false; }
            // подпись сцены
            GUI.Label(new Rect(12, by - 30, Screen.width - 24, 24), rec.Note, st);
        }

        static Color32 Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        public static Color32 GroundColor(int kind) => Hex(Ground.TryGetValue(kind, out var s) ? s : Ground[1]);   // палитра редактора карт
        // цвет вершин сетки Unity не переводит из sRGB — в линейном проекте переводим сами
        static Color32 Lin(Color32 c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? (Color32)((Color)c).linear : c;
    }
}
