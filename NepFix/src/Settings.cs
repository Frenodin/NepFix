using BepInEx.Configuration;

namespace NepFix
{
    public enum PostAA { Off = 0, FXAA = 1, SMAA_Low = 2, SMAA_Medium = 3, SMAA_High = 4 }
    public enum Upscaler { Auto = 0, Bilinear = 1, Point = 2, FSR1 = 3 }

    public class Settings
    {
        // Display / FPS
        public ConfigEntry<int> FpsLimit;          // 0 = частота монитора
        public ConfigEntry<bool> VSync;
        public ConfigEntry<bool> FpsUnlock;        // false = не трогать поведение игры

        // Quality
        public ConfigEntry<int> QualityLevel;      // -1 = как в игре, 0..4
        public ConfigEntry<bool> FullResTextures;
        public ConfigEntry<int> Anisotropic;       // 0 = как в игре, 2..16
        public ConfigEntry<bool> FourBoneSkinning;

        // Anti-aliasing / resolution
        public ConfigEntry<int> Msaa;              // 1,2,4,8
        public ConfigEntry<PostAA> PostAa;
        public ConfigEntry<float> RenderScale;
        public ConfigEntry<Upscaler> UpscaleFilter;
        public ConfigEntry<int> FsrPreset;
        public ConfigEntry<float> SmallObjectDistance;
        public ConfigEntry<float> FsrSharpness;
        public ConfigEntry<bool> HdrRendering;

        // Shadows
        public ConfigEntry<bool> ShadowsOverride;
        public ConfigEntry<int> ShadowResolution;
        public ConfigEntry<float> ShadowDistanceMin;
        public ConfigEntry<float> ShadowDistanceMul;
        public ConfigEntry<int> ShadowCascades;
        public ConfigEntry<bool> SoftShadows;
        public ConfigEntry<bool> AdditionalLightShadows;
        public ConfigEntry<bool> Ssao;

        // Detail / distance
        public ConfigEntry<float> LodBias;
        public ConfigEntry<float> FarClipMul;
        public ConfigEntry<bool> DisableMapReduction;

        // Fixes (experimental)
        public ConfigEntry<bool> AdaptiveFrameTiming, UnitStuckFix;
        public ConfigEntry<bool> RigidbodyInterpolation;
        public ConfigEntry<bool> SyncPhysicsToFps;
        public ConfigEntry<bool> AnimatorAlwaysAnimate, FootIK;
        public ConfigEntry<int> ClothRate;
        public ConfigEntry<bool> WeaponSmooth, CharLightLimit, BikeProbe, MotionInterp;
        public ConfigEntry<float> MapVoiceInterval;
        public ConfigEntry<float> WeaponFade, AnimBlendMin, BikeHandling, BikeSpeed, BikeWallSoft, BikeLedgeSmooth;
        public ConfigEntry<bool> CustomIdles, SmoothUnits, IdleGestures, IdleBreath, IdleLook;
        public ConfigEntry<float> CustomShare, CustomDuration, IdleDelayMin, IdleDelayMax, IdleBlend, IdleBreathAmount, IdleLookDistance;
        public ConfigEntry<float> SmoothTime;

        // Performance
        public ConfigEntry<int> MaxQueuedFrames;
        public ConfigEntry<bool> FxEventMigrated, FxIndoorSunContact, ForceLightShadows, SsaoHighQuality, PipelineTest, FxEnabled, FxUseMotion, FxSplit, FxInMenus, FxIndoorShadows, FxOverheadAlways, FxSsr, FxSsrBlur;
        public ConfigEntry<float> FxSsrIntensity, FxSsrDistance, FxBlobStrength, FxIndoorLength, FxCharStrength, FxGiRadius, FxGiIntensity, FxAoIntensity, FxContactLength, FxContactIntensity, FxTemporal, FxFadeDistance;
        public ConfigEntry<int> FxSsrMode, FxRays, FxSteps, FxDebug, FxDepthSource, FxEvent;
        public ConfigEntry<int> MaxShadowedLights;
        public ConfigEntry<float> CharAmbient, AmbientMul, AmbientAdd, ReflectionMul, SsaoIntensityMul, SsaoRadiusMul;
        public ConfigEntry<bool> SkinOffscreenOff, AnimatorCulling, PhysicsNoAutoSync, FastLoading;
        public ConfigEntry<float> TextureSharpness;   // 0 = как в игре

        // Misc
        public ConfigEntry<string> MenuKey;
        public ConfigEntry<float> MenuX, MenuY;
        public ConfigEntry<float> OutlineWidth;
        public ConfigEntry<bool> CharacterShadows;
        public ConfigEntry<bool> ShowFps;
        public ConfigEntry<int> HudMode, HudCorner;
        public ConfigEntry<string> HudKey, MsaaFlipCams;
        public ConfigEntry<float> HudOpacity;
        public ConfigEntry<bool> VerboseLog;

        public Settings(ConfigFile c)
        {
            FpsLimit = c.Bind("1.Экран", "FpsLimit", 0, "Лимит FPS. 0 = частота обновления монитора. Не ставьте выше, чем может выдать ПК: игра считает время от целевого FPS.");
            VSync = c.Bind("1.Экран", "VSync", false, "Вертикальная синхронизация.");
            FpsUnlock = c.Bind("1.Экран", "FpsUnlock", true, "Управлять частотой кадров (иначе игра держит 60).");

            QualityLevel = c.Bind("2.Качество", "QualityLevel", 4, "Базовый профиль Unity: -1 как в игре, 0 Normal, 1 Low, 2 Medium, 3 High, 4 Ultra.");
            FullResTextures = c.Bind("2.Качество", "FullResTextures", true, "Текстуры в полном разрешении (профиль игры Normal грузит их в 1/2).");
            Anisotropic = c.Bind("2.Качество", "Anisotropic", 16, "Анизотропная фильтрация (0 = как в игре, 2-16).");
            FourBoneSkinning = c.Bind("2.Качество", "FourBoneSkinning", true, "Скиннинг 4+ кости на вершину (игра: 2) — плавнее сгибы суставов.");

            Msaa = c.Bind("3.Сглаживание", "MSAA", 4, "MSAA: 1 (выкл), 2, 4, 8.");
            PostAa = c.Bind("3.Сглаживание", "PostAA", PostAA.SMAA_High, "Пост-сглаживание камеры.");
            RenderScale = c.Bind("3.Сглаживание", "RenderScale", 1.0f, "Масштаб рендера 0.5–2.0. >1 = суперсэмплинг (лучшее против лесенок), <1 = апскейл.");
            FsrPreset = c.Bind("3.Сглаживание", "FsrPreset", 0, "Режим FSR: 0 выкл, 1 Native AA, 2 Quality 1.5x, 3 Balanced 1.7x, 4 Performance 2x, 5 Ultra Performance 3x.");
            SmallObjectDistance = c.Bind("5.Детализация", "SmallObjectDistance", 3f, "Множитель дальности появления растительности и мелких объектов, 8 = без отсечения.");
            UpscaleFilter = c.Bind("3.Сглаживание", "Upscaler", Upscaler.FSR1, "Фильтр при RenderScale < 1: FSR1 — AMD FidelityFX Super Resolution 1.0.");
            FsrSharpness = c.Bind("3.Сглаживание", "FsrSharpness", 0.8f, "Резкость FSR (0–1).");
            HdrRendering = c.Bind("3.Сглаживание", "HDR", true, "HDR-рендеринг (внутренний буфер).");

            ShadowsOverride = c.Bind("4.Тени", "Override", true, "Управлять тенями из мода.");
            ShadowResolution = c.Bind("4.Тени", "Resolution", 4096, "Разрешение карты теней: 1024/2048/4096/8192.");
            ShadowDistanceMin = c.Bind("4.Тени", "MinDistance", 60f, "Минимальная дальность теней (карты игры местами ставят почти 0 — тени пропадают).");
            ShadowDistanceMul = c.Bind("4.Тени", "DistanceMultiplier", 1.5f, "Множитель дальности теней, заданной картой.");
            ShadowCascades = c.Bind("4.Тени", "Cascades", 4, "Каскады теней 1–4.");
            SoftShadows = c.Bind("4.Тени", "SoftShadows", true, "Мягкие тени.");
            AdditionalLightShadows = c.Bind("4.Тени", "AdditionalLightShadows", true, "Тени от точечных/прожекторных источников.");
            Ssao = c.Bind("4.Тени", "SSAO", true, "Ambient occlusion (если есть в рендерере игры).");

            LodBias = c.Bind("5.Детализация", "LodBias", 2.5f, "LOD Bias: выше = детальные модели дальше (1 = как в игре).");
            FarClipMul = c.Bind("5.Детализация", "FarClipMultiplier", 1.0f, "Множитель дальности отсечения камеры.");
            DisableMapReduction = c.Bind("5.Детализация", "DisableMapReduction", true, "Отключить упрощение объектов карты вблизи (MapReduction).");

            UnitStuckFix = c.Bind("6.Исправления", "UnitStuckFix", true, "Враги не застревают на месте при высоком FPS: порог «юнит упёрся» пересчитывается под время кадра.");
            AdaptiveFrameTiming = c.Bind("6.Исправления", "AdaptiveFrameTiming", true, "Скорость игровых процессов по реальному времени кадра (против тряски камеры/персонажей при просадках FPS).");
            RigidbodyInterpolation = c.Bind("6.Исправления", "RigidbodyInterpolation", true, "Интерполяция физических тел (против дёрганья при беге).");
            SyncPhysicsToFps = c.Bind("6.Исправления", "SyncPhysicsToFps", false, "Шаг физики = 1/FPS (эксперимент).");
            FootIK = c.Bind("6.Исправления", "FootIK", true, "Постановка ступней по земле. Выключение убирает дрожание ног при медленной ходьбе, но на склонах ступни могут чуть уходить в землю или висеть.");
            ClothRate = c.Bind("6.Исправления", "ClothRate", 0, "Физика волос и одежды: 0 как в игре, 1 частота под FPS, 2 раз в кадр.");
            IdleGestures = c.Bind("11.Анимации", "Gestures", false, "Жесты из анимаций игры, когда героиня стоит без дела.");
            IdleDelayMin = c.Bind("11.Анимации", "DelayMin", 7f, "Через сколько секунд простоя может начаться жест, минимум.");
            IdleDelayMax = c.Bind("11.Анимации", "DelayMax", 14f, "То же, максимум.");
            IdleBlend = c.Bind("11.Анимации", "Blend", 0.35f, "Длительность плавного перехода между анимациями, секунды.");
            CustomIdles = c.Bind("11.Анимации", "Custom", false, "Сторонние idle-анимации из nepidle.bundle.");
            CustomShare = c.Bind("11.Анимации", "CustomShare", 0.6f, "Доля сторонних анимаций среди жестов.");
            CustomDuration = c.Bind("11.Анимации", "CustomDuration", 8f, "Сколько секунд длится сторонняя анимация, округляется до целых повторов.");
            IdleBreath = c.Bind("11.Анимации", "Breath", false, "Дыхание: грудная клетка и плечи чуть поднимаются.");
            IdleBreathAmount = c.Bind("11.Анимации", "BreathAmount", 1f, "Сила дыхания.");
            IdleLook = c.Bind("11.Анимации", "Look", false, "Героиня поворачивает голову к камере, когда стоит рядом.");
            IdleLookDistance = c.Bind("11.Анимации", "LookDistance", 6f, "На каком расстоянии от камеры героиня смотрит в неё, метры.");
            WeaponSmooth = c.Bind("8.Персонажи", "WeaponSmooth", true, "Оружие при ударе вне боя появляется и исчезает плавно, а не мгновенно.");
            WeaponFade = c.Bind("8.Персонажи", "WeaponFade", 0.14f, "Длительность появления и исчезновения оружия, секунды.");
            MotionInterp = c.Bind("6.Исправления", "MotionInterp", true, "Плавное движение отряда и мотоцикла между шагами физики игры.");
            BikeHandling = c.Bind("8.Персонажи", "BikeHandling", 1f, "Управляемость мотоцикла: множитель скорости поворота. 1 как в игре.");
            BikeSpeed = c.Bind("8.Персонажи", "BikeSpeed", 1f, "Максимальная скорость мотоцикла: множитель. 1 как в игре.");
            BikeWallSoft = c.Bind("8.Персонажи", "BikeWallSoft", 0f, "Мягкость ударов о стену: 0 как в игре, 1 мотоцикл почти не теряет скорость.");
            BikeLedgeSmooth = c.Bind("8.Персонажи", "BikeLedgeSmooth", 0.12f, "Сглаживание подъёма мотоцикла на уступы, секунды. 0 как в игре.");
            BikeProbe = c.Bind("8.Персонажи", "BikeProbe", false, "Замер езды на мотоцикле в BepInEx\\NepFix_bike.csv. Выключается сам после записи.");
            CharLightLimit = c.Bind("8.Персонажи", "CharLightLimit", true, "Ограничить яркость цветных ламп на персонажах (_Is_Filter_LightColor шейдера Toon). В пещерах лампы игры светят с силой 10 и засвечивают героинь.");
            MapVoiceInterval = c.Bind("8.Персонажи", "MapVoiceInterval", 40f, "Минимальная пауза между фразами героинь на карте вне боя, секунды. 0 как в игре.");
            AnimBlendMin = c.Bind("8.Персонажи", "AnimBlendMin", 0.18f, "Минимальная длительность перехода между анимациями на карте, секунды. У игры 0.1.");
            SmoothUnits = c.Bind("6.Исправления", "SmoothUnits", true, "Сглаживание высоты моделей персонажей между шагами физики: убирает дрожь ног и волос при ходьбе на высоком FPS.");
            SmoothTime = c.Bind("6.Исправления", "SmoothTime", 0.04f, "Время сглаживания высоты, секунды.");
            AnimatorAlwaysAnimate = c.Bind("6.Исправления", "AnimatorAlwaysAnimate", false, "Аниматоры не отключаются вне кадра (эксперимент).");

            MaxQueuedFrames = c.Bind("7.Производительность", "MaxQueuedFrames", 0, "Кадров в очереди GPU (0 = как в игре, 1 = меньше задержка, 2–3 = ровнее).");

            SkinOffscreenOff = c.Bind("7.Производительность", "SkinOffscreenOff", true, "Не пересчитывать скиннинг моделей вне кадра.");
            AnimatorCulling = c.Bind("7.Производительность", "AnimatorCulling", false, "Аниматоры вне кадра не обновляют кости (эксперимент, экономит CPU при толпе NPC).");
            PhysicsNoAutoSync = c.Bind("7.Производительность", "PhysicsNoAutoSync", false, "Физика без авто-синхронизации трансформов: меньше нагрузка на CPU, но у персонажей может дрожать постановка ног.");
            FastLoading = c.Bind("7.Производительность", "FastLoading", true, "Ускоренная загрузка ресурсов: больше времени и буфера на загрузку текстур/мешей в GPU, высокий приоритет фоновой загрузки.");
            TextureSharpness = c.Bind("2.Качество", "TextureSharpness", -0.5f, "Mip bias текстур: отрицательное = резче вдали (земля, стены), 0 = как в игре.");
            FxEnabled = c.Bind("10.NepFX", "Enabled", true, "Экранное освещение NepFX (нужен nepfx.bundle).");
            FxGiIntensity = c.Bind("10.NepFX", "GiIntensity", 1.0f, "Сила отражённого света.");
            FxGiRadius = c.Bind("10.NepFX", "GiRadius", 2.0f, "Радиус трассировки GI, метры.");
            FxAoIntensity = c.Bind("10.NepFX", "AoIntensity", 0.6f, "Сила затенения (AO) от трассировки.");
            FxContactLength = c.Bind("10.NepFX", "ContactLength", 0.35f, "Длина контактных теней, метры.");
            FxContactIntensity = c.Bind("10.NepFX", "ContactIntensity", 0.7f, "Сила контактных теней.");
            FxTemporal = c.Bind("10.NepFX", "Temporal", 0.85f, "Вес истории кадров (меньше = меньше шлейфов, больше шума).");
            FxRays = c.Bind("10.NepFX", "Rays", 2, "Лучей на пиксель (половинное разрешение).");
            FxSteps = c.Bind("10.NepFX", "Steps", 10, "Шагов на луч.");
            FxUseMotion = c.Bind("10.NepFX", "UseMotionVectors", true, "Использовать векторы движения игры.");
            FxDebug = c.Bind("10.NepFX", "Debug", 0, "Отладка: 0 выкл, 1 GI, 2 AO, 3 контактные тени, 4 нормали, 5 проверка вывода, 6 глубина.");
            FxDepthSource = c.Bind("10.NepFX", "DepthSource", 0, "Откуда брать глубину: 0 авто, 1 текстура игры, 2 своя копия с MSAA, 3 своя копия без MSAA.");
            FxEvent = c.Bind("10.NepFX", "Event", 2, "Момент в кадре: 0 перед постобработкой, 1 после прозрачных, 2 после неба, 3 после постобработки.");
            FxInMenus = c.Bind("10.NepFX", "InMenus", false, "Применять NepFX к камерам, которые рисуют в текстуру: портреты отряда и сцены в меню.");
            FxCharStrength = c.Bind("10.NepFX", "CharacterStrength", 0.15f, "Сила NepFX на персонажах: затенение, отсвет и контактные тени. 0 персонажи без эффекта, 1 как на окружении.");
            FxIndoorShadows = c.Bind("10.NepFX", "IndoorShadows", true, "Если в сцене нет солнца, контактные тени строятся от условного света сверху: тени под персонажами и предметами в шахтах и пещерах.");
            FxOverheadAlways = c.Bind("10.NepFX", "OverheadAlways", false, "Тени от света сверху и на открытом воздухе, если в сцене нет солнца с тенями. По умолчанию только под потолком: в шахтах, пещерах, зданиях.");
            FxSsr = c.Bind("10.NepFX", "SSR", false, "Отражения по экрану: вода, пол, глянцевые поверхности.");
            FxSsrIntensity = c.Bind("10.NepFX", "SSRIntensity", 0.5f, "Сила отражений.");
            FxSsrDistance = c.Bind("10.NepFX", "SSRDistance", 15f, "Дальность отражений, метры.");
            FxSsrMode = c.Bind("10.NepFX", "SSRMode", 0, "Что отражает: 0 вода и мокрые поверхности, 1 плюс весь пол, 2 все поверхности.");
            FxSsrBlur = c.Bind("10.NepFX", "SSRBlur", true, "Размытые отражения, как у неровной поверхности.");
            FxBlobStrength = c.Bind("10.NepFX", "BlobStrength", 0.55f, "Сила мягкой тени на полу под персонажами в помещениях.");
            FxFadeDistance = c.Bind("10.NepFX", "FadeDistance", 40f, "Дальше этого расстояния затенение и отсвет плавно сходят на нет. Вдали картинка в тумане, и эффект темнил туман вокруг деревьев.");
            FxIndoorLength = c.Bind("10.NepFX", "IndoorLength", 0.8f, "Длина теней от условного света сверху, метры.");
            FxSplit = c.Bind("10.NepFX", "Split", false, "Сравнение: левая половина экрана без NepFX, правая с ним.");
            PipelineTest = c.Bind("9.Освещение", "PipelineTest", false, "Тест встраивания в рендер: пурпурный квадрат в левом нижнем углу 3D-сцены (под интерфейсом).");
            MsaaFlipCams = c.Bind("10.NepFX", "MsaaFlipCams", "", "Служебное: камеры, для которых глубина читается другим способом, чтобы не было ошибок MSAA.");
            FxEventMigrated = c.Bind("10.NepFX", "EventMigrated114", false, "Служебное: момент эффекта перенесён до прозрачных объектов.");
            if (!FxEventMigrated.Value) { FxEvent.Value = 2; FxEventMigrated.Value = true; }
            FxIndoorSunContact = c.Bind("10.NepFX", "IndoorSunContact", false, "Контактные тени от солнца в пещерах и шахтах. Солнце там светит только на персонажей, поэтому по умолчанию выключено.");
            ForceLightShadows = c.Bind("9.Освещение", "ForceLightShadows", true, "Тени от ламп и фонарей, у которых игра их отключила. Направленный свет не трогается.");
            MaxShadowedLights = c.Bind("9.Освещение", "MaxShadowedLights", 4, "Сколько ближайших ламп/прожекторов отбрасывают тени (каждый — до 6 проходов рендера).");
            AmbientMul = c.Bind("9.Освещение", "AmbientMul", 1.0f, "Множитель окружающего (рассеянного) света сцены.");
            CharAmbient = c.Bind("9.Освещение", "CharAmbient", -1f, "Сколько окружающего света принимают персонажи, параметр _GI_Intensity шейдера Toon. -1 как в игре.");
            AmbientAdd = c.Bind("9.Освещение", "AmbientAdd", 0.0f, "Добавка рассеянного света, если в сцене его нет совсем.");
            ReflectionMul = c.Bind("9.Освещение", "ReflectionMul", 1.0f, "Множитель отражений окружения.");
            SsaoIntensityMul = c.Bind("9.Освещение", "SsaoIntensityMul", 1.5f, "Сила SSAO относительно игры.");
            SsaoRadiusMul = c.Bind("9.Освещение", "SsaoRadiusMul", 1.0f, "Радиус SSAO относительно игры.");
            SsaoHighQuality = c.Bind("9.Освещение", "SsaoHighQuality", true, "SSAO в полном разрешении и минимум 8 выборок.");
            MenuKey = c.Bind("0.Меню", "Hotkey", "F10", "Клавиша меню (F1–F12).");
            MenuX = c.Bind("0.Меню", "PosX", 20f, "Положение окна меню (перетаскивается за заголовок).");
            MenuY = c.Bind("0.Меню", "PosY", 20f, "Положение окна меню.");
            OutlineWidth = c.Bind("8.Персонажи", "OutlineWidth", 0f, "Множитель толщины контура персонажей (0 = авто по разрешению: высота/1080).");
            CharacterShadows = c.Bind("8.Персонажи", "ForceCastShadows", false, "Принудительно включить отбрасывание теней всеми частями моделей персонажей (выкл = как задумано игрой).");
            HudMode = c.Bind("0.Меню", "HudMode", 2, "Оверлей мониторинга: 0 выкл, 1 компактный, 2 подробный, 3 подробный + график.");
            HudCorner = c.Bind("0.Меню", "HudCorner", 1, "Угол оверлея: 0 слева сверху, 1 справа сверху, 2 слева снизу, 3 справа снизу.");
            HudKey = c.Bind("0.Меню", "HudHotkey", "F11", "Клавиша переключения режимов оверлея.");
            HudOpacity = c.Bind("0.Меню", "HudOpacity", 0.6f, "Непрозрачность фона оверлея (0–1).");
            ShowFps = c.Bind("0.Меню", "ShowFps", false, "Счётчик FPS в углу.");
            VerboseLog = c.Bind("0.Меню", "VerboseLog", true, "Подробный лог диагностики.");
        }
    }
}
