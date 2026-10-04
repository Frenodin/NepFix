using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace NepFix
{
    /// Настройки моделей персонажей (Unity Toon Shader): толщина контура и тени.
    internal static class Chars
    {
        static Settings S => Plugin.S;
        static readonly Dictionary<IntPtr, float> origWidth = new();
        static readonly Dictionary<IntPtr, float> applied = new();
        static int idWidth = -1, idGi = -1, idFilter = -1;
        static readonly Dictionary<IntPtr, float> origFilter = new();
        static readonly Dictionary<IntPtr, float> appliedFilter = new();
        public static int FilterFound, FilterOrigOn;
        static bool dumped;
        static readonly Dictionary<IntPtr, float> origGi = new();
        static readonly Dictionary<IntPtr, float> appliedGi = new();
        public static float GiMin = -1, GiMax = -1;
        /// Рендереры персонажей (материалы Toon) для маски NepFX.
        public static List<(Renderer r, int subs)> Renderers = new();
        public static int Count => origWidth.Count;
        public static float AutoOutline => Math.Clamp(Screen.height / 1080f, 1f, 2.5f);

        static long lastSig;
        static float lastQuick;

        /// Быстрая проверка раз в 0.1 с: появилась ли новая модель (смена героини в меню, подгрузка отряда).
        /// Без неё новая модель до двух секунд не попадала в маску NepFX и была шумной.
        public static void Quick()
        {
            float now = Time.unscaledTime;
            if (now - lastQuick < 0.1f) return;
            lastQuick = now;
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<SkinnedMeshRenderer>());
            if (arr == null) return;
            long sig = arr.Length;
            foreach (var o in arr) sig = sig * 31 + o.Pointer.ToInt64();
            if (sig == lastSig) return;
            Apply(arr);
        }

        public static void Apply() => Apply(null);

        static void Apply(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> arr)
        {
            if (idWidth < 0) { idWidth = Shader.PropertyToID("_Outline_Width"); idGi = Shader.PropertyToID("_GI_Intensity"); idFilter = Shader.PropertyToID("_Is_Filter_LightColor"); }
            int ff = 0, fon = 0;
            float giWant = S.CharAmbient.Value;
            float gmin = 9, gmax = -1;
            float mul = S.OutlineWidth.Value <= 0.001f ? AutoOutline : S.OutlineWidth.Value;
            arr ??= UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<SkinnedMeshRenderer>());
            if (arr == null) return;
            { long sg = arr.Length; foreach (var o in arr) sg = sg * 31 + o.Pointer.ToInt64(); lastSig = sg; }
            var list = new List<(Renderer, int)>();
            foreach (var o in arr)
            {
                var r = o.TryCast<SkinnedMeshRenderer>();
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                bool toon = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(idWidth)) continue;
                    toon = true;
                    IntPtr k = m.Pointer;
                    if (!origWidth.TryGetValue(k, out float orig))
                    {
                        orig = ICalls.MaterialGetFloat(k, idWidth);
                        if (float.IsNaN(orig)) continue;
                        origWidth[k] = orig;
                    }
                    if (m.HasProperty(idGi))
                    {
                        if (!origGi.TryGetValue(k, out float og)) { og = ICalls.MaterialGetFloat(k, idGi); if (!float.IsNaN(og)) origGi[k] = og; }
                        if (!float.IsNaN(og)) { gmin = Math.Min(gmin, og); gmax = Math.Max(gmax, og); }
                        float gw = giWant < 0 ? og : giWant;
                        if (!float.IsNaN(gw) && (!appliedGi.TryGetValue(k, out float gc) || Math.Abs(gc - gw) > 1e-4f))
                        { m.SetFloat("_GI_Intensity", gw); appliedGi[k] = gw; }
                    }
                    if (!dumped) { dumped = true; Dump(m); }
                    if (m.HasProperty(idFilter))
                    {
                        if (!origFilter.TryGetValue(k, out float of)) { of = ICalls.MaterialGetFloat(k, idFilter); if (float.IsNaN(of)) of = 0; origFilter[k] = of; }
                        ff++; if (of > 0.5f) fon++;
                        float fw = S.CharLightLimit.Value ? 1f : of;
                        if (!appliedFilter.TryGetValue(k, out float fc) || Math.Abs(fc - fw) > 1e-4f)
                        { m.SetFloat("_Is_Filter_LightColor", fw); appliedFilter[k] = fw; }
                    }
                    if (orig <= 0f) continue;
                    float want = orig * mul;
                    if (!applied.TryGetValue(k, out float cur) || Math.Abs(cur - want) > 1e-4f)
                    {
                        m.SetFloat("_Outline_Width", want);
                        applied[k] = want;
                    }
                }
                if (toon && r.enabled && r.gameObject.activeInHierarchy) list.Add((r, mats.Length));
                if (toon && S.CharacterShadows.Value)
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
            }
            if (gmax >= 0) { GiMin = gmin; GiMax = gmax; }
            if (ff > 0) { FilterFound = ff; FilterOrigOn = fon; }
            Renderers = list;
            if (origWidth.Count > 4000) { origGi.Clear(); appliedGi.Clear(); origFilter.Clear(); appliedFilter.Clear(); }
            if (origWidth.Count > 4000) { origWidth.Clear(); applied.Clear(); }
        }

        /// Один раз пишет в лог свойства шейдера персонажей, чтобы знать, какие настройки у него есть.
        static void Dump(Material m)
        {
            try
            {
                var sh = m.shader;
                int n = sh.GetPropertyCount();
                var sb = new System.Text.StringBuilder($"Персонажи: шейдер '{sh.name}', свойств {n}:");
                for (int i = 0; i < n; i++)
                {
                    string name = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range)
                        sb.Append($" {name}={ICalls.MaterialGetFloat(m.Pointer, Shader.PropertyToID(name)):0.##}");
                }
                Plugin.L.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.L.LogInfo("Персонажи: свойства шейдера не прочитаны, " + e.Message); }
        }
    }
}
