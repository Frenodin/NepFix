using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Оптимизации CPU/загрузки и резкость текстур окружения.
    internal static class Optimize
    {
        static int gameAutoSync = -1; static bool? lastSync;
        static Settings S => Plugin.S;
        static readonly HashSet<IntPtr> texDone = new();
        static float appliedBias = float.NaN;
        public static int TexturesSharpened;
        static bool loadTuned, reuseSet;

        public static void ApplyGlobal()
        {
            if (S.FastLoading.Value && !loadTuned)
            {
                ICalls.Get<ICalls.SetInt>("UnityEngine.QualitySettings::set_asyncUploadTimeSlice")?.Invoke(4);
                ICalls.Get<ICalls.SetInt>("UnityEngine.QualitySettings::set_asyncUploadBufferSize")?.Invoke(64);
                ICalls.Get<ICalls.SetBool>("UnityEngine.QualitySettings::set_asyncUploadPersistentBuffer")?.Invoke(true);
                try { Application.backgroundLoadingPriority = ThreadPriority.High; } catch { }
                loadTuned = true;
                Plugin.L.LogInfo("Загрузка: asyncUpload 4 мс / 64 МБ, приоритет фоновой загрузки High");
            }
            // исходное значение игры запоминаем, чтобы вернуть при выключении
            if (gameAutoSync < 0) { var g = ICalls.Get<ICalls.GetBool>("UnityEngine.Physics::get_autoSyncTransforms"); gameAutoSync = g == null ? 1 : (g() ? 1 : 0); }
            bool wantSync = S.PhysicsNoAutoSync.Value ? false : gameAutoSync == 1;
            if (wantSync != lastSync)
            {
                ICalls.Get<ICalls.SetBool>("UnityEngine.Physics::set_autoSyncTransforms")?.Invoke(wantSync);
                lastSync = wantSync;
            }
            if (S.PhysicsNoAutoSync.Value && !reuseSet) { ICalls.Get<ICalls.SetBool>("UnityEngine.Physics::set_reuseCollisionCallbacks")?.Invoke(true); reuseSet = true; }
        }

        /// Раз в несколько секунд: скиннинг вне кадра, аниматоры. Записывается только то, что отличается.
        public static void ApplyObjects()
        {
            if (S.SkinOffscreenOff.Value)
            {
                var arr = Scan.All<SkinnedMeshRenderer>();
                if (arr != null) foreach (var o in arr) { var r = o.TryCast<SkinnedMeshRenderer>(); if (r != null && r.updateWhenOffscreen) r.updateWhenOffscreen = false; }
            }
            if (S.AnimatorCulling.Value && !S.AnimatorAlwaysAnimate.Value)
            {
                var arr = Scan.All<Animator>();
                if (arr != null) foreach (var o in arr) { var a = o.TryCast<Animator>(); if (a != null && a.cullingMode != AnimatorCullingMode.CullUpdateTransforms) a.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
            }
        }

        // ---- резкость текстур: обход всех загруженных текстур частями по бюджету времени ----
        // Раньше полный список текстур запрашивался каждые 15 секунд и обрабатывался в одном кадре.
        // Теперь только после смены сцены (текстуры локации догружаются) и при изменении настройки.
        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> texArr; static int texIdx;
        static readonly List<float> texDue = new();
        static int texScene = int.MinValue; static bool resetPending; static float texPeriodic;

        /// Каждый кадр из Update мода.
        public static void Tick()
        {
            float now = Time.unscaledTime;
            float bias = S.TextureSharpness.Value;
            int sc = Scan.Scene;
            if (sc != texScene) { texScene = sc; texDue.Clear(); texDue.Add(now + 3f); texDue.Add(now + 15f); }
            if (bias != appliedBias)
            {
                // при выключении (0) нужно вернуть 0 только если раньше что-то меняли
                bool touched = !float.IsNaN(appliedBias) && appliedBias != 0f;
                texDone.Clear(); TexturesSharpened = 0; appliedBias = bias; texArr = null;
                resetPending = bias == 0f && touched;
                texDue.Clear(); texDue.Add(now);
            }
            if (bias == 0f && !resetPending) { texDue.Clear(); texArr = null; return; }
            if (texArr == null)
            {
                if (texDue.Count == 0 && now >= texPeriodic) { texPeriodic = now + 60f; texDue.Add(now); } // новые модели и текстуры меню
                if (texDue.Count == 0 || now < texDue[0]) return;
                texDue.RemoveAt(0);
                try { texArr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>()); } catch { texArr = null; }
                texIdx = 0;
                if (texArr == null) return;
            }
            long end = Scan.Now + Scan.Ms(0.7);
            int n = texArr.Length;
            for (int c = 0; texIdx < n; texIdx++, c++)
            {
                if ((c & 31) == 31 && Scan.Now > end) break;
                try
                {
                    IntPtr k = Scan.Raw(texArr, texIdx);
                    if (k == IntPtr.Zero || !texDone.Add(k)) continue;
                    var t = texArr[texIdx]?.TryCast<Texture2D>();
                    if (t == null || t.mipmapCount <= 1) continue;
                    if (t.width < 256) continue; // иконки, интерфейс и мелочь не трогаем
                    t.mipMapBias = bias;
                    TexturesSharpened++;
                }
                catch { }
            }
            if (texIdx >= n) { texArr = null; if (bias == 0f) resetPending = false; if (texDone.Count > 100000) texDone.Clear(); }
        }

        public static void Preset(int p)
        {
            // 0 = максимальное качество, 1 = баланс, 2 = максимум FPS
            switch (p)
            {
                case 0:
                    S.QualityLevel.Value = 4; S.Msaa.Value = 4; S.PostAa.Value = PostAA.SMAA_High; S.RenderScale.Value = 1.25f;
                    S.ShadowResolution.Value = 4096; S.ShadowCascades.Value = 4; S.ShadowDistanceMin.Value = 60; S.ShadowDistanceMul.Value = 1.5f;
                    S.AdditionalLightShadows.Value = true; S.Ssao.Value = true; S.LodBias.Value = 3f; S.TextureSharpness.Value = -0.5f; break;
                case 1:
                    S.QualityLevel.Value = 4; S.Msaa.Value = 4; S.PostAa.Value = PostAA.SMAA_High; S.RenderScale.Value = 1f;
                    S.ShadowResolution.Value = 4096; S.ShadowCascades.Value = 3; S.ShadowDistanceMin.Value = 40; S.ShadowDistanceMul.Value = 1.25f;
                    S.AdditionalLightShadows.Value = false; S.Ssao.Value = true; S.LodBias.Value = 2f; S.TextureSharpness.Value = -0.5f; break;
                default:
                    S.QualityLevel.Value = 4; S.Msaa.Value = 2; S.PostAa.Value = PostAA.SMAA_Medium; S.RenderScale.Value = 1f;
                    S.ShadowResolution.Value = 2048; S.ShadowCascades.Value = 2; S.ShadowDistanceMin.Value = 30; S.ShadowDistanceMul.Value = 1f;
                    S.AdditionalLightShadows.Value = false; S.Ssao.Value = false; S.LodBias.Value = 1.5f; S.TextureSharpness.Value = -0.25f; break;
            }
            S.SkinOffscreenOff.Value = true; S.FastLoading.Value = true;
            S.FsrPreset.Value = 0; S.ShadowsOverride.Value = true; S.PhysicsNoAutoSync.Value = false;
        }

        /// Что ограничивает FPS сейчас.
        public static string Bottleneck()
        {
            float g = Telemetry.GpuLoad; float fps = FrameStats.Fps; int tgt = Gfx.TargetFps;
            if (fps >= tgt * 0.95f) return "упор в лимит FPS";
            if (g < 0) return "нет данных GPU";
            if (g >= 92) return "видеокарта";
            return "процессор, главный поток игры";
        }
    }
}
