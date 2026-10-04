using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace NepFix
{
    /// Картинки-примеры к параметрам освещения. Отрисованы заранее трассировкой лучей на простой сцене
    /// и вшиты в NepFix.dll как ресурс previews.bin.
    internal static class Previews
    {
        public class Info { public string Title, Left, Right, Text; }

        static Dictionary<string, (int w, int h, byte[] z)> raw;
        static readonly Dictionary<string, Texture2D> tex = new();

        public static readonly Dictionary<string, Info> Texts = new()
        {
            ["ForceLightShadows"] = new() { Title = "Тени от всех источников света", Left = "выкл", Right = "вкл",
                Text = "Лампы и фонари начинают отбрасывать тени. Солнце не трогается: у второго солнца игра тени отключила специально. Сфера и куб закрывают свет лампы, за ними появляется тёмная область." },
            ["MaxShadowedLights"] = new() { Title = "Ламп с тенями", Left = "0", Right = "4",
                Text = "Сколько ближайших к камере ламп получают тени. Каждая лампа с тенями заново рисует сцену до шести раз, на слабом процессоре ставьте 2." },
            ["AmbientMul"] = new() { Title = "Окружающий свет", Left = "меньше", Right = "больше",
                Text = "Яркость рассеянного света неба. Больше значение, светлее стороны объектов, куда не попадает солнце, и мягче тени." },
            ["CharAmbient"] = new() { Title = "Окружающий свет на персонажах", Left = "0", Right = "1",
                Text = "Шейдер персонажей сам решает, сколько рассеянного света принять. При нуле теневая сторона персонажа не зависит от окружения, при единице подкрашивается цветом неба и сцены." },
            ["AmbientAdd"] = new() { Title = "Добавка рассеянного света", Left = "0", Right = "0.5",
                Text = "Нужна там, где игра обнулила окружающий свет и множитель ничего не меняет. Тени перестают быть чёрными провалами." },
            ["ReflectionMul"] = new() { Title = "Отражения окружения", Left = "0x", Right = "1.6x",
                Text = "Сила отражений на глянцевых поверхностях. Работает только если в сцене есть пробы отражений или отражение неба." },
            ["Ssao"] = new() { Title = "SSAO", Left = "выкл", Right = "вкл",
                Text = "Затемнение в углах, щелях и под предметами. Действует на окружение, шейдер персонажей его не поддерживает." },
            ["SsaoIntensityMul"] = new() { Title = "Сила SSAO", Left = "0", Right = "высокая",
                Text = "Насколько сильно темнеют углы, стыки стен с полом и места под предметами." },
            ["SsaoRadiusMul"] = new() { Title = "Радиус SSAO", Left = "маленький", Right = "большой",
                Text = "Маленький радиус даёт тонкую тёмную кромку только в стыках. Большой даёт широкое мягкое затемнение вокруг предметов." },
            ["SsaoHighQuality"] = new() { Title = "SSAO высокого качества", Left = "выкл", Right = "вкл",
                Text = "Полное разрешение и больше выборок: затенение без зерна и ступенек по краям, но немного дороже." },
            ["FxEnabled"] = new() { Title = "NepFX", Left = "выкл", Right = "вкл",
                Text = "Экранная трассировка лучей: отсвет от цветных стен на соседние предметы, затенение в углах и точные тени у основания предметов." },
            ["FxSplit"] = new() { Title = "Сравнение половинами экрана", Left = "сравнение", Right = "эффект целиком",
                Text = "Левая половина экрана рисуется без NepFX, правая с ним. По белой линии видно разницу прямо в игре." },
            ["FxGiIntensity"] = new() { Title = "Сила отражённого света", Left = "0", Right = "2.2",
                Text = "Цветной отсвет: красная стена подкрашивает сферу и пол рядом, освещённый пол подсвечивает низ предметов." },
            ["FxGiRadius"] = new() { Title = "Радиус GI", Left = "0.4 м", Right = "3 м",
                Text = "Как далеко ищется отражённый свет. Маленький радиус даёт отсвет только вплотную к поверхности, большой красит всю комнату." },
            ["FxAoIntensity"] = new() { Title = "Затенение AO", Left = "0", Right = "1.2",
                Text = "Затемнение, рассчитанное трассировкой. Глубже и точнее, чем SSAO игры, работает и на персонажах." },
            ["FxContactLength"] = new() { Title = "Длина контактных теней", Left = "0", Right = "0.5 м",
                Text = "Тень у самого основания предметов, которую карта теней теряет. Жёлтый кубик перестаёт висеть над полом." },
            ["FxSsr"] = new() { Title = "Отражения по экрану", Left = "выкл", Right = "вкл",
                Text = "Вода и мокрые поверхности отражают то, что видно на экране: героинь, стены, предметы. Под острым углом отражение сильнее, как в жизни. Чего нет в кадре, отразиться не может, поэтому у края экрана отражение плавно исчезает." },
            ["FxSsrIntensity"] = new() { Title = "Сила отражений", Left = "слабо", Right = "сильно",
                Text = "Насколько заметны отражения. При 100% пол похож на зеркало, для каменных полов и земли лучше 30–50%." },
            ["FxSsrDistance"] = new() { Title = "Дальность отражений", Left = "близко", Right = "далеко",
                Text = "Как далеко ищется отражаемый объект. Больше значение, дальше видны отражения, но ниже FPS." },
            ["FxSsrFloorsOnly"] = new() { Title = "Что отражает", Left = "без отражений", Right = "с отражениями",
                Text = "По умолчанию отражают только вода и мокрые поверхности: их мод находит по шейдерам игры. «Плюс весь пол» добавляет любой горизонтальный пол, включая песок и землю. «Все поверхности» включает и стены." },
            ["FxSsrBlur"] = new() { Title = "Размытые отражения", Left = "чёткие", Right = "размытые",
                Text = "Размытие делает отражение похожим на полированный камень, а не на зеркало, и скрывает зубчатые края." },
            ["FxIndoorShadows"] = new() { Title = "Тени от света сверху", Left = "выкл", Right = "вкл",
                Text = "В шахтах и пещерах освещение запечено заранее, а солнце игры светит только на персонажей, поэтому на полу нет их теней. NepFX добавляет тень от условного света сверху: под ногами и предметами появляется мягкое тёмное пятно." },
            ["FxContactIntensity"] = new() { Title = "Сила контактных теней", Left = "слабо", Right = "полностью",
                Text = "Насколько тёмной будет контактная тень." },
            ["FxTemporal"] = new() { Title = "Накопление кадров", Left = "0", Right = "0.9",
                Text = "Результат нескольких кадров усредняется, зерно пропадает. Слишком большое значение даёт шлейфы за движущимися персонажами." },
            ["FxRays"] = new() { Title = "Лучей на пиксель", Left = "1", Right = "4",
                Text = "Больше лучей, меньше шума, но ниже FPS." },
            ["FxDebug1"] = new() { Title = "Отладка: только отражённый свет", Left = "обычный вид", Right = "отладка",
                Text = "Показывает только найденный отсвет. Чёрное это места без отсвета. Небо закрашено синим." },
            ["FxDebug2"] = new() { Title = "Отладка: только затенение", Left = "обычный вид", Right = "отладка",
                Text = "Белое не затенено, тёмное затенено. Углы и места под предметами должны быть темнее." },
            ["FxDebug3"] = new() { Title = "Отладка: контактные тени", Left = "обычный вид", Right = "отладка",
                Text = "Тёмными пятнами видны только контактные тени у основания предметов." },
            ["FxDebug4"] = new() { Title = "Отладка: нормали", Left = "обычный вид", Right = "отладка",
                Text = "Пол зелёный, стены красноватые и синеватые, сфера переливается. Если так, буфер глубины читается правильно." },
            ["FxDebug5"] = new() { Title = "Отладка: проверка вывода", Left = "обычный вид", Right = "отладка",
                Text = "Вся 3D-сцена окрашивается в красный. Не окрасилась, значит проход не попадает в кадр." },
            ["FxDebug6"] = new() { Title = "Отладка: глубина", Left = "обычный вид", Right = "отладка",
                Text = "Близкое светлое, далёкое тёмное, небо синее. Если весь экран синий, игра не отдаёт буфер глубины." },
        };

        static void Load()
        {
            raw = new();
            try
            {
                using var s = typeof(Previews).Assembly.GetManifestResourceStream("previews.bin");
                if (s == null) { Plugin.L.LogWarning("Превью: ресурс не найден"); return; }
                using var br = new BinaryReader(s);
                int n = br.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string key = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
                    int w = br.ReadInt32(), h = br.ReadInt32(), len = br.ReadInt32();
                    raw[key] = (w, h, br.ReadBytes(len));
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Превью: " + e.Message); }
        }

        public static Texture2D Get(string key)
        {
            if (key == null) return null;
            if (tex.TryGetValue(key, out var t) && t != null) return t;
            if (raw == null) Load();
            if (!raw.TryGetValue(key, out var r)) return null;
            try
            {
                byte[] px;
                using (var ms = new MemoryStream(r.z))
                using (var z = new ZLibStream(ms, CompressionMode.Decompress))
                using (var o = new MemoryStream()) { z.CopyTo(o); px = o.ToArray(); }
                t = new Texture2D(r.w, r.h, TextureFormat.RGBA32, false);
                t.hideFlags = HideFlags.HideAndDontSave;
                t.filterMode = FilterMode.Bilinear;
                t.LoadRawTextureData((Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)px);
                t.Apply(false, true);
                tex[key] = t;
                return t;
            }
            catch (Exception e) { Plugin.L.LogWarning("Превью " + key + ": " + e.Message); tex[key] = null; return null; }
        }
    }
}
