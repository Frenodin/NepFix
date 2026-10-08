using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NepFix
{
    /// Какие объекты сцены блестят: вода и мокрые поверхности по шейдерам игры.
    /// Обход идёт в обычном Update по бюджету времени, а не внутри прохода рендера: раньше он стоял прямо в NepFX
    /// и на больших картах давал фриз 45–53 мс в момент рендера.
    internal static class Gloss
    {
        /// Отсортированы по силе блеска, чтобы в рендере значение блеска выставлялось только при смене.
        public static List<(Renderer r, int sub, float g)> Items = new();
        public static string Info = "";
        static int idWaterSm = -1, idWaterSm2, idTopSm;
        static int scene = int.MinValue;
        static readonly List<float> due = new();
        static float nextPeriodic;
        static Il2CppReferenceArray<UnityEngine.Object> arr; static int idx;
        static List<(Renderer, int, float)> building;
        static int water, wet, smooth;
        static readonly Dictionary<IntPtr, (float g, int kind)> matCache = new();
        /// Вид шейдера по указателю: имя шейдера читается один раз, а не для каждого материала.
        static readonly Dictionary<IntPtr, int> shaderKind = new();
        const double BudgetMs = 0.8;

        static int ShaderKind(Shader sh)
        {
            IntPtr k = sh.Pointer;
            if (shaderKind.TryGetValue(k, out int kind)) return kind;
            string n = sh.name ?? "";
            kind = n.Contains("WaterWave") ? 1 : n.Contains("RainDrop") ? 2 : n.Contains("Triplanar") ? 3 : 0;
            shaderKind[k] = kind;
            return kind;
        }

        static (float g, int kind) Classify(Material m)
        {
            IntPtr k = m.Pointer;
            if (matCache.TryGetValue(k, out var c)) return c;
            c = (0, 0);
            var sh = m.shader;
            if (sh != null)
            {
                switch (ShaderKind(sh))
                {
                    case 1: c = (0.35f, 1); break;
                    case 2:
                    {
                        float v = m.HasProperty(idWaterSm) ? ICalls.MaterialGetFloat(k, idWaterSm) : m.HasProperty(idWaterSm2) ? ICalls.MaterialGetFloat(k, idWaterSm2) : 0.6f;
                        if (!float.IsNaN(v) && v > 0.3f) c = (Math.Clamp(v, 0f, 1f) * 0.8f, 2);
                        break;
                    }
                    case 3:
                        if (m.HasProperty(idTopSm))
                        {
                            float v = ICalls.MaterialGetFloat(k, idTopSm);
                            if (!float.IsNaN(v) && v > 0.6f) c = (v - 0.4f, 3);
                        }
                        break;
                }
            }
            matCache[k] = c;
            return c;
        }

        static bool Needed => Plugin.S.FxEnabled.Value && Plugin.S.FxSsr.Value;

        /// Каждый кадр из Update мода.
        public static void Update()
        {
            if (!Needed) { if (arr != null) arr = null; return; }
            if (idWaterSm < 0) { idWaterSm = Shader.PropertyToID("_WaterSmoothness"); idWaterSm2 = Shader.PropertyToID("_Water_Smoothness"); idTopSm = Shader.PropertyToID("_TopSmoothness"); }
            float now = Time.unscaledTime;
            try
            {
                int sc = Scan.Scene;
                if (sc != scene)
                {
                    scene = sc; due.Clear(); arr = null; matCache.Clear(); shaderKind.Clear(); Items = new();
                    due.Add(now + 2f); due.Add(now + 8f);
                    nextPeriodic = now + 30f;
                }
                if (arr == null)
                {
                    bool go = due.Count > 0 && now >= due[0];
                    if (go) due.RemoveAt(0);
                    // объекты карты подгружаются и позже: изредка пересматриваем сцену
                    else if (now >= nextPeriodic) { go = true; nextPeriodic = now + 30f; }
                    if (!go) return;
                    arr = Scan.All<MeshRenderer>();
                    idx = 0; building = new(); water = wet = smooth = 0;
                    if (arr == null) return;
                }
                long end = Scan.Now + Scan.Ms(BudgetMs);
                int n = arr.Length;
                for (int c = 0; idx < n; idx++, c++)
                {
                    if ((c & 15) == 15 && Scan.Now > end) break;
                    try
                    {
                        // выключенные тоже берём: игра включает их позже, а в рендере всё равно проверяется видимость
                        var r = arr[idx]?.TryCast<MeshRenderer>();
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i]; if (m == null) continue;
                            var cl = Classify(m);
                            if (cl.g > 0.01f)
                            {
                                building.Add((r, i, cl.g));
                                if (cl.kind == 1) water++; else if (cl.kind == 2) wet++; else smooth++;
                            }
                        }
                    }
                    catch { }
                }
                if (idx >= n)
                {
                    building.Sort((a, b) => a.Item3.CompareTo(b.Item3));
                    Items = building; arr = null;
                    Info = $"Блестящих объектов в сцене: вода {water}, мокрые {wet}, гладкие {smooth}.";
                    if (matCache.Count > 20000) matCache.Clear(); // кэш только классификации, исходных значений тут нет
                }
            }
            catch (Exception e) { Info = "Блеск: " + e.Message; arr = null; }
        }
    }
}
