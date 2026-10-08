using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NepFix
{
    /// Освещение движка: тени от всех источников, окружающий свет, отражения, SSAO.
    internal static class Lighting
    {
        static Settings S => Plugin.S;
        /// Исходный режим теней источника и то, что выставил мод. Если игра сама поменяла режим, новое значение становится исходным:
        /// раньше мод возвращал записанное в первый раз и отменял решения игры.
        static readonly Dictionary<IntPtr, (LightShadows orig, LightShadows set)> shadowState = new();
        static readonly Dictionary<IntPtr, UnityEngine.Object> lightObjs = new();
        static float gameRefl = -1, ourRefl = -1;
        static SphericalHarmonicsL2 baseProbe; static bool haveProbe;
        static (int, AmbientMode, float, Color, Color) probeKey; static float appliedMul = -1, appliedAdd = -1;
        static bool Sane(SphericalHarmonicsL2 p)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) { float v = p[c, k]; if (float.IsNaN(v) || Math.Abs(v) > 20f) return false; }
            return true;
        }
        static IntPtr probesObj; static SphericalHarmonicsL2[] baseBaked; static float bakedMul = -1, bakedAdd = -1;
        public static string SceneInfo = "";
        /// Над отрядом есть потолок: шахта, пещера, здание.
        public static bool Indoor; static int indoorVotes;
        /// Освещает ли солнце игры пол под отрядом. В пещерах оно часто светит только на персонажей.
        /// Пещера или шахта: запечённый свет и почти нет рассеянного света неба. Не зависит от лучей к потолку,
        /// поэтому работает и на боевой площадке, где коллайдера потолка нет. Запоминается для сцены.
        /// Направленных источников с тенями: если есть, у сцены настоящие тени от солнца.
        public static int SunShadows = -1;
        public static bool Cave; static int caveScene = int.MinValue; static bool caveSticky; static string lastCaveInfo = "";
        static float sceneInfoT;
        static readonly Dictionary<IntPtr, (float i, float r, float d)> origSsao = new();
        public static string Summary = "";
        static int lastLightCount = -1;

        public static void Apply()
        {
            var cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            var arr = Scan.All<Light>();
            var lights = new List<Light>();
            if (arr != null) foreach (var o in arr) { var l = o.TryCast<Light>(); if (l != null && l.enabled && l.gameObject.activeInHierarchy) lights.Add(l); }

            int dir = 0, dirSh = 0, loc = 0, locSh = 0;
            var local = new List<(Light l, float d)>();
            var origShadows = new Dictionary<IntPtr, LightShadows>();
            foreach (var l in lights)
            {
                IntPtr k = l.Pointer;
                var cur = l.shadows;
                LightShadows orig = shadowState.TryGetValue(k, out var stt) && cur == stt.set ? stt.orig : cur;
                origShadows[k] = orig; lightObjs[k] = l;
                if (l.type == LightType.Directional)
                {
                    dir++;
                    // направленный свет не трогаем: у второго солнца игра тени выключила специально,
                    // на аниме-шейдере персонажей его тени дают пятна на лицах
                    if (cur != orig) l.shadows = orig;
                    shadowState[k] = (orig, orig);
                    if (orig != LightShadows.None) dirSh++;
                }
                else if (l.type == LightType.Point || l.type == LightType.Spot)
                {
                    loc++;
                    float d = (l.transform.position - cp).magnitude;
                    local.Add((l, d));
                }
            }
            SunShadows = dirSh;
            // тени от ближайших точечных/прожекторных источников (каждый такой источник — до 6 проходов рендера)
            int n = S.ForceLightShadows.Value ? Math.Clamp(S.MaxShadowedLights.Value, 0, 16) : 0;
            var nearest = new HashSet<IntPtr>(local.Where(x => x.l.intensity > 0.05f && x.d < x.l.range * 1.5f + 5f).OrderBy(x => x.d).Take(n).Select(x => x.l.Pointer));
            foreach (var (l, d) in local)
            {
                IntPtr k = l.Pointer;
                var want = nearest.Contains(k) ? (origShadows[k] == LightShadows.None ? LightShadows.Soft : origShadows[k]) : origShadows[k];
                if (l.shadows != want) l.shadows = want;
                shadowState[k] = (origShadows[k], want);
                if (want != LightShadows.None) locSh++;
            }


            Summary = $"Источников света: направленных {dir}, с тенями {dirSh}; точечных и прожекторов {loc}, с тенями {locSh}. ";
            if (lights.Count != lastLightCount && S.VerboseLog.Value)
            {
                lastLightCount = lights.Count;
                var sb = new StringBuilder("Освещение сцены: " + Summary + "\n");
                foreach (var l in lights.Take(40))
                    sb.AppendLine($"  {l.type} '{l.name}' I={l.intensity:0.00} range={l.range:0.0} shadows={l.shadows} (исх. {(origShadows.TryGetValue(l.Pointer, out var os) ? os : l.shadows)}) mode={l.renderMode} color={l.color}");
                Plugin.L.LogInfo(sb.ToString());
            }
            Scan.PruneDestroyed(lightObjs, shadowState);
        }


        static bool Same(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k += 3) if (Math.Abs(a[c, k] - b[c, k]) > 1e-4f) return false;
            return true;
        }

        static SphericalHarmonicsL2 Mod(SphericalHarmonicsL2 p, float mul, float add)
        {
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) p[c, k] = p[c, k] * mul;
            // равномерная добавка: только нулевой коэффициент, слегка холодный оттенок неба
            p[0, 0] += add * 0.92f; p[1, 0] += add * 0.97f; p[2, 0] += add * 1.05f;
            return p;
        }

        /// Быстрая часть, раз в 0,5 с: окружающий свет, отражения, SSAO.
        public static void ApplyFast()
        {
            float mul = S.AmbientMul.Value, add = S.AmbientAdd.Value;
            // Пробу окружающего света нельзя читать обратно после записи: Unity хранит её в другой нормировке,
            // и множитель накапливается. Базу берём только при смене сцены или настроек неба самой игрой.
            try
            {
                var key = (Scan.Scene, RenderSettings.ambientMode, (float)Math.Round(RenderSettings.ambientIntensity, 3), RenderSettings.ambientSkyColor, RenderSettings.ambientLight);
                bool neutral = Math.Abs(mul - 1f) < 1e-3f && add < 1e-3f;
                if (key != probeKey || !haveProbe)
                {
                    var cur = RenderSettings.ambientProbe;
                    if (!Sane(cur))
                    {
                        // проба испорчена прошлыми версиями NepFix: заставляем Unity пересчитать её
                        RenderSettings.ambientIntensity = RenderSettings.ambientIntensity;
                        cur = RenderSettings.ambientProbe;
                    }
                    if (Sane(cur)) { baseProbe = cur; haveProbe = true; probeKey = key; appliedMul = -1; }
                }
                if (haveProbe && (Math.Abs(appliedMul - mul) > 1e-4f || Math.Abs(appliedAdd - add) > 1e-4f))
                {
                    RenderSettings.ambientProbe = neutral ? baseProbe : Mod(baseProbe, mul, add);
                    appliedMul = mul; appliedAdd = add;
                }
            }
            catch { }
            // световые пробы сцены: по ним освещаются персонажи и подвижные объекты
            try
            {
                var lp = LightmapSettings.lightProbes;
                if (lp == null || lp.count == 0) { probesObj = IntPtr.Zero; baseBaked = null; }
                else
                {
                    if (lp.Pointer != probesObj || baseBaked == null || baseBaked.Length != lp.count)
                    {
                        var arr = lp.bakedProbes; baseBaked = new SphericalHarmonicsL2[arr.Length];
                        for (int i = 0; i < arr.Length; i++) baseBaked[i] = arr[i];
                        probesObj = lp.Pointer; bakedMul = 1; bakedAdd = 0;
                    }
                    if (Math.Abs(bakedMul - mul) > 1e-4f || Math.Abs(bakedAdd - add) > 1e-4f)
                    {
                        var arr = lp.bakedProbes;
                        for (int i = 0; i < baseBaked.Length && i < arr.Length; i++) arr[i] = Mod(baseBaked[i], mul, add);
                        lp.bakedProbes = arr; bakedMul = mul; bakedAdd = add;
                    }
                }
            }
            catch { }
            try
            {
                float r = RenderSettings.reflectionIntensity;
                if (Math.Abs(r - ourRefl) > 1e-4f) gameRefl = r;
                ourRefl = Math.Clamp(gameRefl * S.ReflectionMul.Value, 0f, 2f);
                if (Math.Abs(r - ourRefl) > 1e-4f) RenderSettings.reflectionIntensity = ourRefl;
            }
            catch { }
            ApplySsao();
            try
            {
                var rs = Chars.Renderers;
                Renderer lead = null;
                foreach (var (r, _) in rs) if (r != null && r.isVisible) { lead = r; break; }
                if (lead != null)
                {
                    var c = lead.bounds.center + Vector3.up * 1.5f;
                    int hits = 0;
                    foreach (var o in rayOff) if (Physics.Raycast(c + o, Vector3.up, 150f)) hits++;
                    // сглаживание, чтобы не мигало под кронами деревьев
                    indoorVotes = Math.Clamp(indoorVotes + (hits >= 4 ? 1 : -1), -3, 3);
                    if (indoorVotes >= 2) Indoor = true; else if (indoorVotes <= -2) Indoor = false;
                }
            }
            catch { }
            try
            {
                int sc = Scan.Scene;
                if (sc != caveScene) { caveScene = sc; caveSticky = false; }
                int lm = 0; try { lm = LightmapSettings.lightmaps.Length; } catch { }
                float amb = haveProbe ? (baseProbe[0, 0] + baseProbe[1, 0] + baseProbe[2, 0]) / 3f : 1f;
                bool dark = lm > 0 && amb < 0.1f;
                if (Indoor || dark) caveSticky = true;
                Cave = caveSticky;
                if (!S.VerboseLog.Value && !Plugin.MenuOpen) return;
                string info = $"пещера {Cave} (потолок {Indoor}, карт освещения {lm}, рассеянный свет {amb:0.000}, солнц с тенями {SunShadows})";
                if (info != lastCaveInfo) { lastCaveInfo = info; if (S.VerboseLog.Value) Plugin.L.LogInfo("Свет: " + info); }
            }
            catch { }
            if (Plugin.MenuOpen && Time.unscaledTime - sceneInfoT > 2f) { sceneInfoT = Time.unscaledTime; SceneInfo = BuildSceneInfo(); }
        }

        static string ModeName(AmbientMode m) => m switch
        {
            AmbientMode.Skybox => "от неба", AmbientMode.Trilight => "три цвета", AmbientMode.Flat => "один цвет", _ => "свой"
        };

        static string BuildSceneInfo()
        {
            try
            {
                int lm = 0, lpc = 0, rp = 0;
                try { lm = LightmapSettings.lightmaps.Length; } catch { }
                try { var lp = LightmapSettings.lightProbes; lpc = lp == null ? 0 : lp.count; } catch { }
                try { var a = Scan.All<ReflectionProbe>(); rp = a == null ? 0 : a.Length; } catch { }
                var b = baseProbe;
                float amb = (b[0, 0] + b[1, 0] + b[2, 0]) / 3f;
                string s = $"Сцена: окружающий свет {ModeName(RenderSettings.ambientMode)}, яркость по игре {amb:0.00}, карт освещения {lm}, световых проб {lpc}, проб отражений {rp}.";
                if (amb < 0.02f && lpc == 0) s += " Рассеянного света в сцене нет, поэтому множитель ничего не меняет. Используйте «Добавка рассеянного света».";
                if (lm > 0) s += " Статичные объекты освещены запечёнными картами, окружающий свет влияет в основном на персонажей и подвижные объекты.";
                if (rp == 0) s += " Проб отражений нет, ползунок отражений влияет только на отражение неба.";
                return s;
            }
            catch (Exception e) { return "Сцена: " + e.Message; }
        }

        static void ApplySsao()
        {
            var a = Gfx.Urp();
            var list = a?.m_RendererDataList;
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                var feats = list[i]?.m_RendererFeatures;
                if (feats == null) continue;
                for (int j = 0; j < feats.Count; j++)
                {
                    var f = feats[j]?.TryCast<ScreenSpaceAmbientOcclusion>();
                    if (f == null) continue;
                    var st = f.m_Settings;
                    if (st == null) continue;
                    IntPtr k = f.Pointer;
                    if (!origSsao.ContainsKey(k)) origSsao[k] = (st.Intensity, st.Radius, st.DirectLightingStrength);
                    var o = origSsao[k];
                    float wi = o.i * S.SsaoIntensityMul.Value, wr = o.r * S.SsaoRadiusMul.Value;
                    if (Math.Abs(st.Intensity - wi) > 1e-4f) st.Intensity = wi;
                    if (Math.Abs(st.Radius - wr) > 1e-4f) st.Radius = wr;
                    if (S.SsaoHighQuality.Value) { if (st.Downsample) st.Downsample = false; if (st.SampleCount < 8) st.SampleCount = 8; }
                    if (Plugin.MenuOpen) SsaoInfo = $"SSAO: сила {o.i:0.00}→{st.Intensity:0.00}, радиус {o.r:0.00}→{st.Radius:0.00}, выборок {st.SampleCount}, пониж. разрешение {(st.Downsample ? "да" : "нет")}";
                }
            }
        }
        public static string SsaoInfo = "";
        static readonly Vector3[] rayOff = { Vector3.zero, new Vector3(2, 0, 0), new Vector3(-2, 0, 0), new Vector3(0, 0, 2), new Vector3(0, 0, -2) };
    }
}
