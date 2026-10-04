using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NepFix
{
    /// Какие объекты сцены блестят: вода и мокрые поверхности по шейдерам игры.
    /// Сцена просматривается частями по несколько сотен объектов за кадр: полный проход по десяткам тысяч
    /// объектов разом давал заметный фриз.
    internal static class Gloss
    {
        public static List<(Renderer r, int sub, float g)> Items = new();
        public static string Info = "";
        static int idWaterSm = -1, idWaterSm2, idTopSm;
        static int scene = int.MinValue, lastFrame = -1;
        static readonly List<float> due = new();
        static float nextPeriodic;
        static Il2CppReferenceArray<UnityEngine.Object> arr; static int idx;
        static List<(Renderer, int, float)> building;
        static int water, wet, smooth;
        static readonly Dictionary<IntPtr, (float g, int kind)> matCache = new();
        const int PerFrame = 600;

        static (float g, int kind) Classify(Material m)
        {
            IntPtr k = m.Pointer;
            if (matCache.TryGetValue(k, out var c)) return c;
            c = (0, 0);
            var sh = m.shader;
            if (sh != null)
            {
                string n = sh.name;
                if (n.Contains("WaterWave")) c = (0.35f, 1);
                else if (n.Contains("RainDrop"))
                {
                    float v = m.HasProperty(idWaterSm) ? ICalls.MaterialGetFloat(k, idWaterSm) : m.HasProperty(idWaterSm2) ? ICalls.MaterialGetFloat(k, idWaterSm2) : 0.6f;
                    if (!float.IsNaN(v) && v > 0.3f) c = (Math.Clamp(v, 0f, 1f) * 0.8f, 2);
                }
                else if (n.Contains("Triplanar") && m.HasProperty(idTopSm))
                {
                    float v = ICalls.MaterialGetFloat(k, idTopSm);
                    if (!float.IsNaN(v) && v > 0.6f) c = (v - 0.4f, 3);
                }
            }
            matCache[k] = c;
            return c;
        }

        public static void Update()
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (idWaterSm < 0) { idWaterSm = Shader.PropertyToID("_WaterSmoothness"); idWaterSm2 = Shader.PropertyToID("_Water_Smoothness"); idTopSm = Shader.PropertyToID("_TopSmoothness"); }
            float now = Time.unscaledTime;
            try
            {
                int sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (sc != scene)
                {
                    scene = sc; due.Clear(); arr = null; matCache.Clear(); Items = new();
                    due.Add(now + 2f); due.Add(now + 8f);
                    nextPeriodic = now + 60f;
                }
                if (arr == null)
                {
                    bool go = due.Count > 0 && now >= due[0];
                    if (go) due.RemoveAt(0);
                    
                    if (!go) return;
                    arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MeshRenderer>());
                    idx = 0; building = new(); water = wet = smooth = 0;
                    if (arr == null) return;
                }
                int end = Math.Min(arr.Length, idx + PerFrame);
                for (; idx < end; idx++)
                {
                    try
                    {
                        var r = arr[idx]?.TryCast<MeshRenderer>();
                        if (r == null || !r.enabled) continue;
                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i]; if (m == null) continue;
                            var c = Classify(m);
                            if (c.g > 0.01f)
                            {
                                building.Add((r, i, c.g));
                                if (c.kind == 1) water++; else if (c.kind == 2) wet++; else smooth++;
                            }
                        }
                    }
                    catch { }
                }
                if (idx >= arr.Length)
                {
                    Items = building; arr = null;
                    Info = $"Блестящих объектов в сцене: вода {water}, мокрые {wet}, гладкие {smooth}.";
                    if (matCache.Count > 20000) matCache.Clear();
                }
            }
            catch (Exception e) { Info = "Блеск: " + e.Message; arr = null; }
        }
    }
}
