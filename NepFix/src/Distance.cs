using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NepFix
{
    /// Дальность появления мелких объектов: отсечение по слоям у камер и теней, отсечение в CombinedLODManager.
    internal static class Distance
    {
        static Settings S => Plugin.S;
        static readonly Dictionary<IntPtr, float[]> gameCam = new(), gameLight = new();
        static readonly Dictionary<IntPtr, float> ourCamSum = new(), ourLightSum = new();
        static readonly Dictionary<IntPtr, (float cull, float[] d)> gameLod = new();
        public static string Summary = "";
        static readonly Dictionary<IntPtr, (float orig, float mul, float set)> groupState = new();
        static string lastLogged = "";
        static readonly Dictionary<IntPtr, UnityEngine.Object> objs = new();

        static float Sum(float[] a) { float s = 0; foreach (var v in a) s += v; return s; }

        static float[] Scale(float[] src, float mul)
        {
            var r = new float[src.Length];
            for (int i = 0; i < src.Length; i++) r[i] = src[i] <= 0 ? 0 : (mul >= 7.9f ? 0 : src[i] * mul);
            return r;
        }

        // ---- группы LOD: сканирование частями, а не всей сцены раз в несколько секунд ----
        // На больших картах групп LOD десятки тысяч: полный проход через interop давал фриз каждые 5 секунд.
        static int lodScene = int.MinValue; static float lodMul = -1;
        static readonly List<float> lodDue = new();
        static Il2CppReferenceArray<UnityEngine.Object> lodArr; static int lodIdx;
        static int lodChangedCur, lodChangedLast, lodCountLast; static float lodCullMin = 1, lodCullMax = 0, curMin = 1, curMax = 0;
        /// Бюджет на кадр. Раньше было 800 групп за кадр: на тяжёлых картах GetLODs/SetLODs через interop давали фриз 90 мс.
        const double LodBudgetMs = 0.8;

        static void SchedulePlan(float mul)
        {
            float now = Time.unscaledTime;
            int sc = Scan.Scene;
            if (sc != lodScene)
            {
                lodScene = sc; groupState.Clear(); lodArr = null; lodDue.Clear();
                lodDue.Add(now + 1f); lodDue.Add(now + 6f); lodDue.Add(now + 20f);
            }
            if (Math.Abs(mul - lodMul) > 0.01f) { lodMul = mul; if (lodDue.Count == 0 || lodDue[0] > now + 0.2f) lodDue.Insert(0, now); }
        }

        /// Каждый кадр: обрабатывает группы LOD, пока не кончится бюджет времени.
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (lodArr == null)
            {
                if (lodDue.Count == 0 || now < lodDue[0]) return;
                lodDue.RemoveAt(0);
                // множитель 1 и мод ещё ничего не менял: обходить нечего
                if (Math.Abs(lodMul - 1f) < 0.01f && groupState.Count == 0) return;
                try { lodArr = Scan.All<LODGroup>(); } catch { lodArr = null; }
                lodIdx = 0; lodChangedCur = 0; curMin = 1; curMax = 0;
                if (lodArr == null) return;
            }
            float mul = lodMul;
            long end = Scan.Now + Scan.Ms(LodBudgetMs);
            int n = lodArr.Length;
            for (int c = 0; lodIdx < n; lodIdx++, c++)
            {
                if ((c & 15) == 15 && Scan.Now > end) break;
                try
                {
                    IntPtr k = Scan.Raw(lodArr, lodIdx);
                    if (k == IntPtr.Zero) continue;
                    if (groupState.TryGetValue(k, out var st) && Math.Abs(st.mul - mul) < 0.01f) { curMin = Math.Min(curMin, st.orig); curMax = Math.Max(curMax, st.orig); continue; }
                    var g = lodArr[lodIdx]?.TryCast<LODGroup>(); if (g == null) continue;
                    var lods = g.GetLODs();
                    if (lods == null || lods.Length == 0) continue;
                    int ln = lods.Length;
                    var last = lods[ln - 1];
                    float cur = last.screenRelativeTransitionHeight;
                    // игра могла сама поменять порог: тогда текущее значение и есть исходное
                    float orig = st.mul > 0 && Math.Abs(cur - st.set) < 1e-5f ? st.orig : cur;
                    float want = mul >= 7.9f ? 0f : orig / mul;
                    if (Math.Abs(cur - want) > 1e-5f)
                    {
                        last.screenRelativeTransitionHeight = want; lods[ln - 1] = last; g.SetLODs(lods); lodChangedCur++;
                    }
                    groupState[k] = (orig, mul, want);
                    curMin = Math.Min(curMin, orig); curMax = Math.Max(curMax, orig);
                }
                catch { }
            }
            if (lodIdx >= n)
            {
                lodCountLast = n; lodChangedLast = lodChangedCur; lodCullMin = curMin; lodCullMax = curMax;
                lodArr = null;
            }
        }

        public static void Apply()
        {
            float mul = Math.Clamp(S.SmallObjectDistance.Value, 1f, 8f);
            int camLayers = 0, lightLayers = 0, lodMgr = 0, lodGroups = 0;
            float camMin = float.MaxValue, camMax = 0;
            var sb = new StringBuilder();

            // камеры
            try
            {
                var cams = Camera.allCameras;
                foreach (var cam in cams)
                {
                    if (cam == null) continue;
                    IntPtr k = cam.Pointer;
                    var cur = (float[])cam.layerCullDistances;
                    if (cur == null) continue;
                    float cs = Sum(cur);
                    if (!gameCam.ContainsKey(k) || !ourCamSum.TryGetValue(k, out float os) || Math.Abs(cs - os) > 0.01f)
                        gameCam[k] = (float[])cur.Clone();
                    var g = gameCam[k];
                    for (int i = 0; i < g.Length; i++) if (g[i] > 0) { camLayers++; camMin = Math.Min(camMin, g[i]); camMax = Math.Max(camMax, g[i]); if (sb.Length < 400) sb.Append($" {LayerMask.LayerToName(i)}={g[i]:0}"); }
                    var want = Scale(g, mul);
                    ourCamSum[k] = Sum(want);
                    if (Math.Abs(Sum(want) - cs) > 0.01f) cam.layerCullDistances = want;
                    objs[k] = cam;
                }
            }
            catch (Exception e) { sb.Append(" камеры: " + e.Message); }

            // тени источников света
            try
            {
                var arr = Scan.All<Light>();
                if (arr != null) foreach (var o in arr)
                {
                    var l = o.TryCast<Light>(); if (l == null) continue;
                    IntPtr k = l.Pointer;
                    Il2CppStructArray<float> curI = null;
                    try { curI = l.layerShadowCullDistances; } catch { }
                    if (curI == null || curI.Length == 0) continue;
                    var cur = (float[])curI;
                    float cs = Sum(cur);
                    // без отсечения по слоям у игры нечего масштабировать; но если обнулил мод (без отсечения), это наша запись
                    if (cs <= 0 && !ourLightSum.ContainsKey(k)) continue;
                    objs[k] = l;
                    if (!gameLight.ContainsKey(k) || !ourLightSum.TryGetValue(k, out float os) || Math.Abs(cs - os) > 0.01f)
                        gameLight[k] = (float[])cur.Clone();
                    foreach (var v in gameLight[k]) if (v > 0) lightLayers++;
                    var want = Scale(gameLight[k], mul);
                    ourLightSum[k] = Sum(want);
                    if (Math.Abs(Sum(want) - cs) > 0.01f) l.layerShadowCullDistances = want;
                }
            }
            catch (Exception e) { sb.Append(" тени: " + e.Message); }

            // CombinedLODManager (объединённые меши окружения)
            try
            {
                var arr = Scan.All<MeshCombineStudio.CombinedLODManager>();
                if (arr != null) foreach (var o in arr)
                {
                    var m = o.TryCast<MeshCombineStudio.CombinedLODManager>(); if (m == null) continue;
                    lodMgr++;
                    IntPtr k = m.Pointer; objs[k] = m;
                    if (!gameLod.TryGetValue(k, out var g))
                    {
                        float[] d = m.distances != null ? (float[])m.distances : null;
                        g = (m.lodCullDistance, d == null ? null : (float[])d.Clone());
                        gameLod[k] = g;
                        if (sb.Length < 600) sb.Append($" CombinedLOD cull={(m.lodCulled ? g.cull.ToString("0") : "нет")}");
                    }
                    float wc = g.cull * mul;
                    if (Math.Abs(m.lodCullDistance - wc) > 0.01f) m.lodCullDistance = wc;
                    if (g.d != null)
                    {
                        var cur = m.distances;
                        float dm = Math.Min(mul, 7.8f);
                        if (cur != null && cur.Length == g.d.Length && Math.Abs(cur[0] - g.d[0] * dm) > 0.01f)
                            m.distances = Scale(g.d, dm);
                    }
                }
            }
            catch (Exception e) { sb.Append(" CombinedLOD: " + e.Message); }

            // группы LOD обрабатываются частями в Tick(), здесь только планирование пересканирования
            SchedulePlan(mul);
            int lodChanged = lodChangedLast; float cullMin = lodCullMin, cullMax = lodCullMax; lodGroups = lodCountLast;
            if (lodChanged > 0) sb.Append($" изменено групп LOD: {lodChanged}");

            Summary = $"Отсечение по слоям у камеры: {camLayers} слоёв" + (camLayers > 0 ? $", {camMin:0}–{camMax:0} м по игре" : "") +
                      $". Тени по слоям: {lightLayers}. Групп LOD: {lodGroups}" + (lodGroups > 0 ? $", по игре объект исчезает, когда занимает меньше {cullMin * 100:0.#}–{cullMax * 100:0.#}% высоты экрана" : "") + $". Объединённых LOD: {lodMgr}.";
            string key = Summary + sb;
            if (key != lastLogged) { lastLogged = key; Plugin.L.LogInfo("Дальность: " + Summary + " Подробно:" + sb); }
            // записи уничтоженных объектов убираем; живые не трогаем, иначе изменённое модом значение принялось бы за исходное
            Scan.PruneDestroyed(objs, gameCam, ourCamSum, gameLight, ourLightSum, gameLod);
        }
    }
}
