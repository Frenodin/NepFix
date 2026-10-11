using System;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace NepFix
{
    public class NepFixBehaviour : MonoBehaviour
    {
        public NepFixBehaviour(IntPtr ptr) : base(ptr) { }

        static Settings S => Plugin.S;

        float tFast, tSlow, fpsAcc; int fpsFrames; float fpsShown;
        bool keyWasDown, menuOpen, cursorWasVisible, hudKeyWas;
        int tab;
        float scroll;
        string status = "";
        bool diagOnce, modsInit;

        bool guiInit;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Prof.Frame(dt);
            // все элементы меню размечаются вручную через GUI.*, без GUILayout: служебный проход разметки IMGUI не нужен
            if (!guiInit) { guiInit = true; try { useGUILayout = false; } catch { } }
            tFast += dt; tSlow += dt;
            if (tFast >= 0.5f) { tFast = 0; Gfx.Enforce(); }
            if (SlowDue(tSlow))
            {
                tSlow = 0; Gfx.EnforceObjects();
                if (!diagOnce && Time.realtimeSinceStartup > 30f) { diagOnce = true; if (S.VerboseLog.Value) Plugin.L.LogInfo(Gfx.Diagnostics()); }
            }

            Gfx.EnforceObjectsTick();
            Step("lod-tick", Distance.Tick);
            Step("gloss", Gloss.Update);
            Step("textures", Optimize.Tick);
            Step("battle", BattleWatch.Tick);
            Step("unit-stuck", UnitStuck.Tick);
            Step("followers", Followers.Tick);
            Step("interp", Interp.Update);
            Step("bike", Bike.Update);
            try { NepFX.Housekeeping(); } catch { }
            FrameStats.Push(dt);
            MsaaWatch.Tick();
            // IdleLife отключён до доработки: жесты, сторонние анимации, дыхание, взгляд
            if (!modsInit && Time.realtimeSinceStartup > 5f) { modsInit = true; ModsInfo.Refresh(); Pipeline.Hook(); }
            bool focused = Application.isFocused;
            bool hk = focused && (Win32.GetAsyncKeyState(Win32.KeyToVk(S.HudKey.Value)) & 0x8000) != 0;
            if (hk && !hudKeyWas) S.HudMode.Value = (S.HudMode.Value + 1) % 4;
            hudKeyWas = hk;
            fpsAcc += dt; fpsFrames++;
            if (fpsAcc >= 0.5f) { fpsShown = fpsFrames / fpsAcc; fpsAcc = 0; fpsFrames = 0; }

            bool down = focused && (Win32.GetAsyncKeyState(Win32.KeyToVk(S.MenuKey.Value)) & 0x8000) != 0;
            if (down && !keyWasDown) Toggle();
            keyWasDown = down;
        }

        [HideFromIl2Cpp]
        static void Step(string name, Action a)
        {
            long t = Prof.Begin();
            try { a(); } catch { }
            Prof.End(name, t);
        }

        static int slowScene = int.MinValue; static float slowSceneT;
        static bool SlowDue(float t)
        {
            float now = Time.unscaledTime;
            int sc = Scan.Scene;
            if (sc != slowScene) { slowScene = sc; slowSceneT = now; }
            float since = now - slowSceneT;
            // сразу после смены сцены почаще, пока она догружается; дальше раз в 30 секунд: полный обход объектов давал фриз
            return since < 12f ? t >= 2f : t >= 30f;
        }

        void FixedUpdate() { BikeProbe.FixedTick(); }

        void LateUpdate()
        {
            Step("chars-quick", Chars.Quick);
            Step("smooth", Smooth.LateUpdate);
            Step("weapon", CombatSmooth.LateUpdate);
            if (S.BikeProbe.Value || BikeProbe.Pending) Step("bike-probe", BikeProbe.LateUpdate);
        }

        [HideFromIl2Cpp]
        void Toggle()
        {
            menuOpen = !menuOpen;
            try
            {
                Plugin.MenuOpen = menuOpen;
                if (menuOpen) { cursorWasVisible = Cursor.visible; Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
                else Cursor.visible = cursorWasVisible;
            }
            catch { }
        }

        // ---------- IMGUI (в сборке игры есть только GUI.*, без GUILayout — раскладка вручную) ----------
        const float W = 760, RowH = 30, Pad = 12, LabelW = 330;
        float y, scale = 1f;

        void OnGUI()
        {
            int hud = S.HudMode.Value;
            if (hud == 0 && !menuOpen) return;
            long t0 = Prof.Begin();
            try { DrawGui(hud); } finally { Prof.End("gui", t0); }
        }

        [HideFromIl2Cpp]
        void DrawGui(int hud)
        {
            var ev = Event.current;
            bool repaint = ev != null && ev.type == EventType.Repaint;
            if (!menuOpen && !repaint) return; // оверлей только рисуется, остальные события IMGUI ему не нужны
            scale = Math.Max(1f, Screen.height / 1080f);
            var m = new Matrix4x4();
            m.m00 = scale; m.m11 = scale; m.m22 = 1; m.m33 = 1;
            GUI.matrix = m;

            if (hud > 0 && repaint) Hud.Draw(scale, hud);
            if (!menuOpen) return;

            float h = Math.Min(Screen.height / scale - 40, 760);
            HandleDrag(h);
            var win = new Rect(px, py, W, h);
            {
                var oc = GUI.color;
                GUI.color = new Color(0.07f, 0.075f, 0.09f, Math.Clamp(S.MenuOpacity.Value, 0f, 1f));
                GUI.DrawTexture(win, Texture2D.whiteTexture);
                GUI.color = oc;
            }
            for (int i = 0; i < 4; i++) GUI.Box(win, "");
            GUI.Label(new Rect(win.x + Pad, win.y + 8, W - 2 * Pad - 130, 24), Loc.T($"≡ NepFix {Plugin.Version} — графика и исправления   [{S.MenuKey.Value} — закрыть, тянуть за заголовок]"));
            if (GUI.Button(new Rect(win.xMax - Pad - 124, win.y + 6, 124, 24), Loc.En ? "Language: EN" : "Язык: RU"))
                S.Language.Value = Loc.En ? "ru" : "en";

            string[] tabs = { "Экран", "Качество", "Сглаживание", "Тени", "Дальность", "Персонажи", "Исправления", "Мониторинг", "Моды", "Оверлей", "Оптимизация", "Освещение" }; // вкладка «Анимации» (case 12) скрыта до доработки
            float tw = (W - 2 * Pad) / 7;
            for (int i = 0; i < tabs.Length; i++)
                if (GUI.Button(new Rect(win.x + Pad + (i % 7) * tw, win.y + 36 + (i / 7) * 32, tw - 4, 28), (i == tab ? "» " : "") + Loc.T(tabs[i])))
                {
                    tab = i; scroll = 0;
                    if (i == 8) { ModsInfo.Refresh(); ModsInfo.CheckTextures(); }
                }

            var area = new Rect(win.x + Pad, win.y + 104, W - 2 * Pad - 18, h - 104 - 48);
            areaRect = area; hoverKey = null;
            mouseOk = Win32.MouseClient(out mouseX, out mouseY); mouseX /= scale; mouseY /= scale;
            GUI.BeginGroup(area, new GUIContent(""), GUIStyle.none);
            y = -scroll;
            drawn.Clear();
            DrawTab();
            tabEntries.Clear(); tabEntries.AddRange(drawn);
            float contentH = y + scroll;
            GUI.EndGroup();
            if (contentH > area.height)
                scroll = GUI.VerticalScrollbar(new Rect(area.xMax + 2, area.y, 16, area.height), scroll, area.height, 0, contentH);
            else scroll = 0;

            DrawPreview(win, h);
            float by = win.y + h - 40;
            if (GUI.Button(new Rect(win.x + Pad, by, 180, 30), Loc.T("Диагностика в лог"))) { Plugin.L.LogInfo(Gfx.Diagnostics()); status = "Записано в LogOutput.log"; }
            if (tabEntries.Count > 0)
            {
                if (GUI.Button(new Rect(win.x + Pad + 188, by, 180, 30), Loc.T("Сбросить вкладку"))) { ResetTab(); status = "Вкладка сброшена"; }
            }
            if (GUI.Button(new Rect(win.x + Pad + 376, by, 150, 30), Loc.T("Сбросить всё"))) { ResetDefaults(); status = "Все настройки сброшены"; }
            GUI.Label(new Rect(win.x + Pad + 536, by + 5, 200, 24), Loc.T(status));
        }

        Rect areaRect; string hoverKey; bool mouseOk; float mouseX, mouseY;

        [HideFromIl2Cpp]
        void Hover(string key, float rowH)
        {
            if (key == null || !mouseOk) return;
            float lx = mouseX - areaRect.x, ly = mouseY - areaRect.y;
            if (ly < 0 || ly > areaRect.height || lx < 0 || lx > areaRect.width) return;
            if (ly >= y && ly < y + rowH) hoverKey = key;
        }

        [HideFromIl2Cpp]
        void DrawPreview(Rect win, float h)
        {
            if (hoverKey == null) return;
            var t = Previews.Get(hoverKey);
            if (t == null || !Previews.Texts.TryGetValue(hoverKey, out var info)) return;
            float iw = t.width, ih = t.height;
            float pw = iw + 20, ph = ih + 118;
            float sw = Screen.width / scale, sh = Screen.height / scale;
            float x = win.xMax + 8;
            if (x + pw > sw) x = win.x - pw - 8;
            if (x < 0) x = win.xMax - pw - 8;
            float yy = Math.Clamp(mouseY - 40, 0, Math.Max(0, sh - ph));
            var r = new Rect(x, yy, pw, ph);
            { var oc = GUI.color; GUI.color = new Color(0.07f, 0.075f, 0.09f, Math.Clamp(S.MenuOpacity.Value, 0f, 1f)); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = oc; }
            for (int i = 0; i < 5; i++) GUI.Box(r, "");
            GUI.Label(new Rect(x + 10, yy + 6, pw - 20, 22), Loc.T(info.Title));
            GUI.DrawTexture(new Rect(x + 10, yy + 30, iw, ih), t);
            float half = (iw - 4) / 2;
            GUI.Label(new Rect(x + 10, yy + 32 + ih, half, 22), Loc.T(info.Left));
            GUI.Label(new Rect(x + 14 + half, yy + 32 + ih, half, 22), Loc.T(info.Right));
            GUI.Label(new Rect(x + 10, yy + 56 + ih, pw - 20, 60), Loc.T(info.Text));
        }

        bool dragging; float dragDX, dragDY; bool lmbWas; float px = -1, py = -1;

        [HideFromIl2Cpp]
        void HandleDrag(float h)
        {
            if (px < 0) { px = S.MenuX.Value; py = S.MenuY.Value; }
            bool lmb = (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (Win32.MouseClient(out float mx, out float my))
            {
                mx /= scale; my /= scale;
                float x = px, y = py;
                if (lmb && !lmbWas && mx >= x && mx <= x + W && my >= y && my <= y + 32)
                { dragging = true; dragDX = mx - x; dragDY = my - y; }
                if (dragging && lmb)
                {
                    float sw = Screen.width / scale, sh = Screen.height / scale;
                    float nx = Math.Clamp(mx - dragDX, 0, Math.Max(0, sw - W));
                    float ny = Math.Clamp(my - dragDY, 0, Math.Max(0, sh - 60));
                    px = (float)Math.Round(nx); py = (float)Math.Round(ny);
                }
            }
            if (!lmb && dragging) { dragging = false; S.MenuX.Value = px; S.MenuY.Value = py; }
            lmbWas = lmb;
        }

        [HideFromIl2Cpp]
        void DrawTab()
        {
            switch (tab)
            {
                case 0:
                    Cycle(Loc.En ? "Language / Язык" : "Язык / Language", S.Language.Value == "ru" ? "Русский" : S.Language.Value == "en" ? "English" : (Loc.En ? "auto: English" : "авто: русский"),
                        () => S.Language.Value = S.Language.Value == "ru" ? "auto" : S.Language.Value == "en" ? "ru" : "en",
                        () => S.Language.Value = S.Language.Value == "ru" ? "en" : S.Language.Value == "en" ? "auto" : "ru");
                    Toggle("Управлять FPS, снять лимит 60", S.FpsUnlock);
                    IntChoice("Лимит FPS", S.FpsLimit, new[] { 0, 30, 60, 90, 120, 144, 165, 240 },
                        new[] { $"как монитор, {Gfx.Refresh}", "30", "60", "90", "120", "144", "165", "240" });
                    IntChoice("Лимит FPS в бою", S.BattleFpsCap, new[] { 0, 60, 90, 120 }, new[] { "без ограничения", "60, как задумано игрой", "90", "120" });
                    Note("Если в бою боссы атакуют слишком часто и бьют слишком больно, поставьте здесь 60: игра рассчитана на 60 кадров. Переключать можно прямо во время боя.");
                    Toggle("Вертикальная синхронизация", S.VSync);
                    IntChoice("Кадров в очереди GPU", S.MaxQueuedFrames, new[] { 0, 1, 2, 3 }, new[] { "как в игре", "1, минимальная задержка", "2", "3" });
                    IntChoice("Оверлей мониторинга", S.HudMode, new[] { 0, 1, 2, 3 }, new[] { "выкл", "компактный", "подробный", "подробный + график" });
                    Note(Loc.T("Рендер: ") + Loc.T(Gfx.Backend) + ".");
                    Note($"Текущая цель: {Gfx.TargetFps} FPS. Скорость игры привязана к целевому FPS: ставьте значение, которое ПК стабильно держит.");
                    break;
                case 1:
                    IntChoice("Профиль Unity", S.QualityLevel, new[] { -1, 0, 1, 2, 3, 4 }, new[] { "как в игре", "Normal", "Low", "Medium", "High", "Ultra" });
                    Toggle("Текстуры в полном разрешении", S.FullResTextures);
                    IntChoice("Анизотропная фильтрация", S.Anisotropic, new[] { 0, 2, 4, 8, 16 }, new[] { "как в игре", "2x", "4x", "8x", "16x" });
                    Toggle("Скиннинг по 4 костям, плавнее суставы", S.FourBoneSkinning);
                    Slider("Резкость текстур", S.TextureSharpness, -1.5f, 0f, 0.25f, v => v == 0 ? "как в игре" : $"{v:0.00}");
                    Toggle("HDR-рендеринг", S.HdrRendering);
                    Note("Профиль Normal, который стоит в игре по умолчанию, грузит текстуры в половинном разрешении и считает скиннинг по 2 костям.");
                    break;
                case 2:
                    if (Gfx.ReShadeActive) Note("ReShade активен, поэтому MSAA выключен: ReShade нужен буфер глубины. Используйте SMAA и масштаб рендера.");
                    IntChoice("MSAA", S.Msaa, new[] { 1, 2, 4, 8 }, new[] { "выкл", "2x", "4x", "8x" });
                    EnumChoice("Пост-сглаживание", S.PostAa, new[] { "выкл", "FXAA", "SMAA низк.", "SMAA сред.", "SMAA выс." });
                    IntChoice("Режим FSR", S.FsrPreset, new[] { 0, 1, 2, 3, 4, 5 }, Gfx.PresetName);
                    if (S.FsrPreset.Value > 0)
                        Info($"Внутреннее разрешение {(int)(Screen.width * Gfx.EffectiveScale)}x{(int)(Screen.height * Gfx.EffectiveScale)}, {Gfx.EffectiveScale * 100:0.0}% от экрана, растягивает AMD FSR 1.0.");
                    else
                    {
                        Slider("Масштаб рендера", S.RenderScale, 0.5f, 2f, 0.05f, v => $"{v:0.00}x, {(int)(Screen.width * v)}x{(int)(Screen.height * v)}");
                        EnumChoice("Апскейлер при масштабе меньше 1", S.UpscaleFilter, new[] { "авто", "билинейный", "точечный", "AMD FSR 1.0" });
                    }
                    Slider("Резкость FSR", S.FsrSharpness, 0f, 1f, 0.05f, v => $"{v:0.00}");
                    Note("Против лесенок на персонажах: MSAA 4x, SMAA и масштаб 1.25–1.5. Режимы FSR уменьшают внутреннее разрешение ради FPS. Native AA рисует в полном разрешении и только повышает резкость.");
                    break;
                case 3:
                    Toggle("Управлять тенями", S.ShadowsOverride);
                    IntChoice("Разрешение теней", S.ShadowResolution, new[] { 1024, 2048, 4096, 8192 }, new[] { "1024", "2048", "4096", "8192" });
                    Slider("Мин. дальность теней", S.ShadowDistanceMin, 0f, 200f, 5f, v => $"{v:0} м");
                    Slider("Множитель дальности", S.ShadowDistanceMul, 0.5f, 4f, 0.1f, v => $"{v:0.0}x");
                    IntChoice("Каскады", S.ShadowCascades, new[] { 1, 2, 3, 4 }, new[] { "1", "2", "3", "4" });
                    Toggle("Мягкие тени", S.SoftShadows);
                    Toggle("Тени от дополнительных источников света", S.AdditionalLightShadows);
                    Toggle("SSAO, затенение в углах и под ногами", S.Ssao);
                    break;
                case 4:
                    Slider("LOD Bias", S.LodBias, 1f, 6f, 0.25f, v => $"{v:0.00}");
                    Slider("Дальность растительности и мелких объектов", S.SmallObjectDistance, 1f, 8f, 0.5f, v => v >= 7.9f ? "без отсечения" : v <= 1.01f ? "как в игре" : $"{v:0.0}x");
                    Note(Distance.Summary);
                    Slider("Дальность отсечения камеры", S.FarClipMul, 1f, 4f, 0.25f, v => $"{v:0.00}x");
                    Toggle("Отключить упрощение объектов карты", S.DisableMapReduction);
                    Note("Изменение дальности и упрощения карты применяется при следующей загрузке локации.");
                    break;
                case 5:
                    Slider("Толщина контура", S.OutlineWidth, 0f, 3f, 0.05f, v => v <= 0.001f ? $"авто, {Chars.AutoOutline:0.00}x" : $"{v:0.00}x");
                    Toggle("Принудительно: тени от всех частей моделей", S.CharacterShadows);
                    Header("Удары вне боя");
                    Toggle("Плавное появление оружия", S.WeaponSmooth);
                    Slider("Длительность появления", S.WeaponFade, 0.05f, 0.4f, 0.01f, v => $"{v * 1000:0} мс");
                    Slider("Плавность переходов анимаций", S.AnimBlendMin, 0f, 0.4f, 0.01f, v => v <= 0.001f ? "как в игре, 100 мс" : $"{v * 1000:0} мс");
                    Header("Мотоцикл");
                    Toggle("Плавное движение отряда и мотоцикла", S.MotionInterp);
                    if (S.MotionInterp.Value && S.SyncPhysicsToFps.Value) Note("Сейчас включён и «Шаг физики под FPS» на вкладке «Исправления». Лучше выключить его: вместе они не нужны и нагружают процессор.");
                    Note($"Игра двигает героинь и мотоцикл только на шагах физики, из-за этого рывки. Сглаживается объектов: {Interp.Count}.");
                    Slider("Управляемость", S.BikeHandling, 0.7f, 2f, 0.05f, v => Math.Abs(v - 1) < 0.001f ? "как в игре" : $"{v:0.00}x");
                    Slider("Максимальная скорость", S.BikeSpeed, 0.6f, 1.2f, 0.05f, v => Math.Abs(v - 1) < 0.001f ? "как в игре" : $"{v * 100:0}%");
                    Slider("Мягкость ударов о стену", S.BikeWallSoft, 0f, 1f, 0.05f, v => v <= 0.001f ? "как в игре" : $"{v * 100:0}%");
                    Slider("Плавный подъём на уступы", S.BikeLedgeSmooth, 0f, 0.3f, 0.01f, v => v <= 0.004f ? "как в игре" : $"{v * 1000:0} мс");
                    Note(Bike.Info);
                    Toggle("Замер езды на мотоцикле", S.BikeProbe);
                    Note(S.BikeProbe.Value ? BikeProbe.Info : "Включите, сядьте на мотоцикл и поездите минуту: повороты, ускорение, пару столкновений. Каждый замер пишется в отдельный файл BepInEx\\NepFix_bike_дата_время.csv.");
                    Header("Голос");
                    Slider("Пауза между фразами на карте", S.MapVoiceInterval, 0f, 120f, 5f, v => v <= 0.01f ? "как в игре" : $"{v:0} с");
                    Note(Loc.T(Voices.Info) + Loc.T(" Разговоры в сценах и по кнопке взаимодействия не затрагиваются."));
                    Note($"Обработано материалов персонажей: {Chars.Count}. Контур в игре рассчитан на 1080p, на 1440p он тоньше и рвётся; «авто» масштабирует по разрешению.");
                    break;
                case 6:
                    Toggle("Тайминг по реальному времени кадра", S.AdaptiveFrameTiming);
                    Toggle("Враги не застревают при высоком FPS", S.UnitStuckFix);
                    Toggle("Спутники обходят препятствия", S.FollowerPathfinding);
                    Toggle("Спутники догоняют без вспышки", S.QuietFollowerTeleport);
                    Note($"Юнит бросал путь, считая что упёрся: {UnitStuck.GiveUps} раз. Мод вернул на путь: {UnitStuck.Rescued} раз.");
                    Toggle("Интерполяция физики против дёрганья", S.RigidbodyInterpolation);
                    Toggle("Шаг физики под FPS, эксперимент", S.SyncPhysicsToFps);
                    Note("Не включайте вместе с «Плавным движением отряда и мотоцикла» на вкладке «Персонажи»: сглаживание уже убирает рывки, а частая физика только нагружает процессор и даёт фризы.");
                    Toggle("Аниматоры не засыпают вне кадра, эксперимент", S.AnimatorAlwaysAnimate);
                    Toggle("Плавное движение персонажей", S.SmoothUnits);
                    Slider("Сглаживание высоты", S.SmoothTime, 0.01f, 0.1f, 0.005f, v => $"{v * 1000:0} мс");
                    Note($"Сглаживается персонажей: {Smooth.Count}. Игра прижимает персонажей к земле 50 раз в секунду, а кадров больше, поэтому без сглаживания модель подпрыгивает по высоте.");
                    Toggle("Постановка ступней по земле", S.FootIK);
                    IntChoice("Физика волос и одежды", S.ClothRate, new[] { 0, 1, 2 }, new[] { "как в игре", "частота под FPS", "раз в кадр" });
                    Note(Loc.T(ClothFix.Info) + Loc.T(". Если волосы дрожат, попробуйте «раз в кадр»."));
                    Note($"Компонентов постановки ступней в сцене: {FootFix.Count}. Если ноги дрожат при медленной ходьбе, выключите: ступни перестанут подстраиваться под неровности.");
                    Toggle("Подробный лог", S.VerboseLog);
                    break;
                case 7:
                    foreach (var line in Hud.Lines(true)) Info(line);
                    Info("");
                    Info($"Источник данных GPU: {(Telemetry.Source == "NVML" ? "NVIDIA NVML" : "счётчики Windows (PDH)")}. Обновление раз в 0,5 с в фоновом потоке.");
                    break;
                case 8:
                    if (GUI.Button(new Rect(0, y, 220, 26), Loc.T(ModsInfo.Busy ? "Проверка…" : "Обновить список"))) { ModsInfo.Refresh(); ModsInfo.CheckTextures(); }
                    y += RowH + 4;
                    foreach (var r in ModsInfo.Rows)
                    {
                        var c = GUI.color;
                        GUI.color = r.Level == 0 ? new Color(0.55f, 1f, 0.55f, 1f) : r.Level == 1 ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.75f, 0.75f, 0.75f, 1f);
                        GUI.Label(new Rect(0, y, 30, RowH), r.Level == 0 ? "●" : r.Level == 1 ? "◐" : "○");
                        GUI.color = c;
                        GUI.Label(new Rect(22, y, 330, RowH), Loc.T(r.Name));
                        GUI.Label(new Rect(355, y, 160, RowH), Loc.T(r.State));
                        GUI.Label(new Rect(520, y, 200, RowH), Loc.T(r.Kind));
                        y += 20;
                        if (!string.IsNullOrEmpty(r.Detail)) { GUI.Label(new Rect(22, y, W - 70, RowH), "   " + Loc.T(r.Detail)); y += 24; }
                        y += 4;
                    }
                    Note(Loc.T("HD-текстуры по данным из памяти игры: ") + Loc.T(ModsInfo.TexReport));
                    break;
                case 10:
                    Note($"Сейчас FPS ограничивает: {Optimize.Bottleneck()}. GPU {(Telemetry.GpuLoad < 0 ? "—" : Telemetry.GpuLoad.ToString("0") + "%")}, FPS {FrameStats.Fps:0} из {Gfx.TargetFps}.");
                    GUI.Label(new Rect(0, y, 200, RowH), Loc.T("Готовые пресеты:"));
                    if (GUI.Button(new Rect(170, y, 170, 26), Loc.T("Макс. качество"))) Optimize.Preset(0);
                    if (GUI.Button(new Rect(350, y, 170, 26), Loc.T("Баланс, рекомендуется"))) Optimize.Preset(1);
                    if (GUI.Button(new Rect(530, y, 170, 26), Loc.T("Макс. FPS"))) Optimize.Preset(2);
                    y += RowH + 6;
                    Toggle("Не считать скиннинг вне кадра", S.SkinOffscreenOff);
                    Toggle("Физика без авто-синхронизации, могут дрожать ноги", S.PhysicsNoAutoSync);
                    Toggle("Ускоренная загрузка ресурсов", S.FastLoading);
                    Toggle("Аниматоры вне кадра не обновляются, эксперимент", S.AnimatorCulling);
                    IntChoice("Кадров в очереди GPU", S.MaxQueuedFrames, new[] { 0, 1, 2, 3 }, new[] { "как в игре", "1, минимальная задержка", "2", "3" });
                    Note($"Резкость применена к {Optimize.TexturesSharpened} текстурам. Дороже всего для процессора каскады теней, каждый заново рисует сцену, тени от точечных источников, до 6 проходов на каждый, LOD Bias выше 2 и дальность теней больше 100 м.");
                    Note("DXVK на видеокартах NVIDIA обычно медленнее родного DirectX 11 в играх, упирающихся в процессор: трансляция добавляет работу CPU. Для максимума FPS выключите DXVK в менеджере.");
                    break;
                case 11:
                    Info("Наведите курсор на параметр, справа появится пример того, как он должен выглядеть.");
                    Header("Свет движка");
                    Toggle("Тени от всех источников света", S.ForceLightShadows, "ForceLightShadows");
                    IntChoice("Ламп с тенями рядом с камерой", S.MaxShadowedLights, new[] { 0, 2, 4, 6, 8 }, new[] { "0", "2", "4", "6", "8" }, "MaxShadowedLights");
                    Slider("Окружающий свет", S.AmbientMul, 0.3f, 2f, 0.05f, v => $"{v:0.00}x", "AmbientMul");
                    Slider("Окружающий свет на персонажах", S.CharAmbient, -0.05f, 1f, 0.05f, v => v < 0 ? $"как в игре{(Chars.GiMax >= 0 ? ", " + Chars.GiMax.ToString("0.00") : "")}" : $"{v:0.00}", "CharAmbient");
                    Slider("Добавка рассеянного света", S.AmbientAdd, 0f, 1f, 0.05f, v => v <= 0.001f ? "нет" : $"{v:0.00}", "AmbientAdd");
                    Slider("Отражения окружения", S.ReflectionMul, 0f, 2f, 0.05f, v => $"{v:0.00}x", "ReflectionMul");
                    Toggle("SSAO", S.Ssao, "Ssao");
                    Slider("Сила SSAO", S.SsaoIntensityMul, 0f, 4f, 0.1f, v => $"{v:0.0}x", "SsaoIntensityMul");
                    Slider("Радиус SSAO", S.SsaoRadiusMul, 0.25f, 3f, 0.05f, v => $"{v:0.00}x", "SsaoRadiusMul");
                    Toggle("SSAO высокого качества", S.SsaoHighQuality, "SsaoHighQuality");
                    Note(Lighting.SceneInfo);
                    Note(Loc.T(Lighting.Summary) + " " + Loc.T(Lighting.SsaoInfo));
                    Header("NepFX, трассировка лучей по экрану");
                    Toggle("Включить NepFX", S.FxEnabled, "FxEnabled");
                    Note(Loc.T("Состояние: ") + Loc.T(NepFX.Status) + Loc.T(". Работа: ") + Loc.T(NepFX.Diag) + ".");
                    Toggle("Сравнение половинами экрана", S.FxSplit, "FxSplit");
                    Toggle("Применять в меню и на портретах отряда", S.FxInMenus);
                    Slider("Эффект на персонажах", S.FxCharStrength, 0f, 1f, 0.05f, v => v <= 0.001f ? "нет" : $"{v * 100:0}%", "FxCharStrength");
                    Slider("Сила отражённого света", S.FxGiIntensity, 0f, 3f, 0.05f, v => $"{v:0.00}", "FxGiIntensity");
                    Slider("Радиус GI", S.FxGiRadius, 0.5f, 6f, 0.25f, v => $"{v:0.00} м", "FxGiRadius");
                    Slider("Затенение AO", S.FxAoIntensity, 0f, 1.5f, 0.05f, v => $"{v:0.00}", "FxAoIntensity");
                    Slider("Дальность эффекта", S.FxFadeDistance, 15f, 150f, 5f, v => $"{v:0} м");
                    Slider("Длина контактных теней", S.FxContactLength, 0f, 1.5f, 0.05f, v => $"{v:0.00} м", "FxContactLength");
                    Slider("Сила контактных теней", S.FxContactIntensity, 0f, 1f, 0.05f, v => $"{v:0.00}", "FxContactIntensity");
                    Header("Отражения по экрану");
                    Toggle("Включить отражения", S.FxSsr, "FxSsr");
                    Slider("Сила отражений", S.FxSsrIntensity, 0f, 1f, 0.05f, v => $"{v * 100:0}%", "FxSsrIntensity");
                    Slider("Дальность отражений", S.FxSsrDistance, 3f, 40f, 1f, v => $"{v:0} м", "FxSsrDistance");
                    IntChoice("Что отражает", S.FxSsrMode, new[] { 0, 1, 2 }, new[] { "вода и мокрое", "плюс весь пол", "все поверхности" }, "FxSsrFloorsOnly");
                    Note(Gloss.Info);
                    Toggle("Размытые отражения", S.FxSsrBlur, "FxSsrBlur");
                    if (NepFX.SsrInfo != "") Note(NepFX.SsrInfo);
                    Header("Тени");
                    Toggle("Тени от света сверху", S.FxIndoorShadows, "FxIndoorShadows");
                    Toggle("Тени сверху и на открытом воздухе без солнца", S.FxOverheadAlways);
                    Slider("Тень под персонажами в помещениях", S.FxBlobStrength, 0f, 1f, 0.05f, v => v <= 0.001f ? "нет" : $"{v * 100:0}%", "FxIndoorShadows");
                    Slider("Длина теней сверху", S.FxIndoorLength, 0.3f, 3f, 0.1f, v => $"{v:0.0} м", "FxIndoorShadows");
                    Toggle("Контактные тени от солнца в пещерах", S.FxIndoorSunContact);
                    Header("Качество эффекта");
                    Slider("Накопление кадров", S.FxTemporal, 0f, 0.97f, 0.01f, v => $"{v:0.00}", "FxTemporal");
                    IntChoice("Лучей на пиксель", S.FxRays, new[] { 1, 2, 3, 4 }, new[] { "1, быстро", "2", "3", "4, качество" }, "FxRays");
                    IntChoice("Отладочный вид", S.FxDebug, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, new[] { "выкл", "только отражённый свет", "только затенение", "контактные тени", "нормали", "проверка вывода", "глубина", "маска персонажей", "только отражения" }, "FxDebug" + Math.Max(1, S.FxDebug.Value));
                    Header("Для диагностики");
                    IntChoice("Источник глубины", S.FxDepthSource, new[] { 0, 1, 2, 3 }, new[] { "авто", "текстура игры", "своя копия, MSAA", "своя копия, без MSAA" });
                    IntChoice("Момент в кадре", S.FxEvent, new[] { 0, 1, 2, 3 }, new[] { "перед постобработкой", "после прозрачных", "до прозрачных", "после постобработки" });
                    Toggle("Тест встраивания в рендер", S.PipelineTest);
                    Note("Если в режиме «глубина» экран целиком синий, эффекту не на чем считать. Переключайте «Источник глубины» и «Момент в кадре», пока не появится картинка: близкое светлое, далёкое тёмное.");
                    break;
                case 12:
                    Header("Жесты в простое");
                    Toggle("Жесты, когда героиня стоит", S.IdleGestures);
                    Slider("Первый жест через, минимум", S.IdleDelayMin, 3f, 30f, 1f, v => $"{v:0} с");
                    Slider("Первый жест через, максимум", S.IdleDelayMax, 4f, 60f, 1f, v => $"{v:0} с");
                    Slider("Плавность переходов", S.IdleBlend, 0.1f, 1f, 0.05f, v => $"{v:0.00} с");
                    Header("Сторонние анимации");
                    Toggle("Использовать сторонние анимации", S.CustomIdles);
                    Slider("Доля среди жестов", S.CustomShare, 0f, 1f, 0.05f, v => $"{v * 100:0}%");
                    Slider("Длительность", S.CustomDuration, 3f, 20f, 1f, v => $"{v:0} с");
                    Note(Loc.T("Сторонние: ") + Loc.T(CustomIdles.Status));
                    Header("Поверх любой анимации");
                    Toggle("Дыхание", S.IdleBreath);
                    Slider("Сила дыхания", S.IdleBreathAmount, 0f, 2.5f, 0.1f, v => $"{v:0.0}");
                    Toggle("Взгляд на камеру", S.IdleLook);
                    Slider("Смотрит в камеру ближе чем", S.IdleLookDistance, 2f, 15f, 0.5f, v => $"{v:0.0} м");
                    Note($"Героинь под управлением: {IdleLife.Count}, сейчас играют жест: {IdleLife.Playing}. {IdleLife.Last}");
                    Note(IdleLife.Diag);
                    Note("Жесты берутся из анимаций самой игры: ожидание, руки на поясе, кивок, покачивание головой, радость. Если героиня сдвинется с места, жест прервётся.");
                    break;
                case 9:
                    IntChoice("Режим оверлея", S.HudMode, new[] { 0, 1, 2, 3 }, new[] { "выкл", "компактный", "подробный", "подробный + график" });
                    IntChoice("Положение", S.HudCorner, new[] { 0, 1, 2, 3 }, new[] { "слева сверху", "справа сверху", "слева снизу", "справа снизу" });
                    Slider("Непрозрачность меню", S.MenuOpacity, 0f, 1f, 0.05f, v => $"{v * 100:0}%");
                    Slider("Прозрачность фона", S.HudOpacity, 0f, 1f, 0.05f, v => $"{v * 100:0}%");
                    Note($"Клавиша переключения режимов: {S.HudKey.Value}. Оверлей рисуется самой игрой и не конфликтует с DXVK, в отличие от RivaTuner.");
                    break;
            }
        }

        // ---------- controls ----------
        [HideFromIl2Cpp]
        Rect LabelRect() => new Rect(0, y, LabelW, RowH);
        [HideFromIl2Cpp]
        Rect CtrlRect() => new Rect(LabelW, y, W - 2 * Pad - 18 - LabelW, RowH - 4);

        [HideFromIl2Cpp]
        void Toggle(string label, ConfigEntry<bool> e, string pv = null)
        {
            drawn.Add(e);
            Hover(pv, RowH);
            GUI.Label(LabelRect(), Loc.T(label));
            bool v = GUI.Toggle(CtrlRect(), e.Value, Loc.T(e.Value ? " Вкл" : " Выкл"));
            if (v != e.Value) e.Value = v;
            y += RowH;
            RecLine(e);
        }

        [HideFromIl2Cpp]
        void Cycle(string label, string shown, Action prev, Action next, string pv = null)
        {
            Hover(pv, RowH);
            GUI.Label(LabelRect(), Loc.T(label));
            var r = CtrlRect();
            if (GUI.Button(new Rect(r.x, r.y, 30, r.height), "<")) prev();
            GUI.Label(new Rect(r.x + 38, r.y + 3, r.width - 76, r.height), Loc.T(shown));
            if (GUI.Button(new Rect(r.xMax - 30, r.y, 30, r.height), ">")) next();
            y += RowH;
        }

        [HideFromIl2Cpp]
        void IntChoice(string label, ConfigEntry<int> e, int[] vals, string[] names, string pv = null)
        {
            drawn.Add(e);
            int idx = Array.IndexOf(vals, e.Value);
            string shown = idx >= 0 ? names[idx] : e.Value.ToString();
            Cycle(label, shown,
                () => e.Value = vals[idx <= 0 ? vals.Length - 1 : idx - 1],
                () => e.Value = vals[idx < 0 || idx >= vals.Length - 1 ? 0 : idx + 1], pv);
            RecLine(e);
        }

        [HideFromIl2Cpp]
        void EnumChoice<T>(string label, ConfigEntry<T> e, string[] names) where T : Enum
        {
            drawn.Add(e);
            var vals = (T[])Enum.GetValues(typeof(T));
            int idx = Array.IndexOf(vals, e.Value);
            Cycle(label, idx >= 0 && idx < names.Length ? names[idx] : e.Value.ToString(),
                () => e.Value = vals[idx <= 0 ? vals.Length - 1 : idx - 1],
                () => e.Value = vals[idx >= vals.Length - 1 ? 0 : idx + 1]);
            RecLine(e);
        }

        [HideFromIl2Cpp]
        void Slider(string label, ConfigEntry<float> e, float min, float max, float step, Func<float, string> fmt, string pv = null)
        {
            drawn.Add(e);
            Hover(pv, RowH);
            GUI.Label(LabelRect(), Loc.T(label) + ": " + Loc.T(fmt(e.Value)));
            float v = GUI.HorizontalSlider(new Rect(LabelW, y + 8, W - 2 * Pad - 18 - LabelW, RowH - 8), e.Value, min, max);
            v = (float)Math.Round(v / step) * step;
            if (Math.Abs(v - e.Value) > step * 0.5f) e.Value = v;
            y += RowH;
            RecLine(e);
        }

        [HideFromIl2Cpp]
        void RecLine(ConfigEntryBase e)
        {
            string r = Rec.Get(e);
            if (r == null) return;
            var c = GUI.color; GUI.color = new Color(0.62f, 0.66f, 0.72f, 1f);
            GUI.Label(new Rect(LabelW, y - 9, W - 2 * Pad - 18 - LabelW, 20), Loc.T("рекомендуется: ") + Loc.T(r));
            GUI.color = c;
            y += 10;
        }

        [HideFromIl2Cpp]
        void Header(string text)
        {
            y += 4;
            var c = GUI.color; GUI.color = new Color(1f, 0.85f, 0.45f, 1f);
            GUI.Label(new Rect(0, y, W - 2 * Pad - 18, RowH), Loc.T(text));
            GUI.color = c;
            y += 26;
        }

        [HideFromIl2Cpp]
        void Info(string text)
        {
            GUI.Label(new Rect(0, y, W - 2 * Pad - 18, RowH), Loc.T(text));
            y += 22;
        }

        [HideFromIl2Cpp]
        void Note(string text)
        {
            y += 6;
            GUI.Label(new Rect(0, y, W - 2 * Pad - 18, RowH * 2), Loc.T(text));
            y += RowH * 2;
        }

        static readonly System.Collections.Generic.List<ConfigEntryBase> drawn = new(), tabEntries = new();

        [HideFromIl2Cpp]
        void ResetTab()
        {
            // сбрасываются только настройки, показанные на текущей вкладке
            var seen = new System.Collections.Generic.HashSet<ConfigEntryBase>();
            foreach (var e in tabEntries) if (seen.Add(e)) e.BoxedValue = e.DefaultValue;
        }

        [HideFromIl2Cpp]
        void ResetDefaults()
        {
            foreach (var kv in Plugin.Instance.Config)
                kv.Value.BoxedValue = kv.Value.DefaultValue;
        }
    }
}
