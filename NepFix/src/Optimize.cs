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
        static bool loadTuned;
        static float lastTex = -100;

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
            if (S.PhysicsNoAutoSync.Value) ICalls.Get<ICalls.SetBool>("UnityEngine.Physics::set_reuseCollisionCallbacks")?.Invoke(true);
        }

        /// Раз в несколько секунд: скиннинг вне кадра, аниматоры, резкость текстур.
        public static void ApplyObjects()
        {
            if (S.SkinOffscreenOff.Value)
            {
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<SkinnedMeshRenderer>());
                if (arr != null) foreach (var o in arr) { var r = o.TryCast<SkinnedMeshRenderer>(); if (r != null) r.updateWhenOffscreen = false; }
            }
            if (S.AnimatorCulling.Value && !S.AnimatorAlwaysAnimate.Value)
            {
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Animator>());
                if (arr != null) foreach (var o in arr) { var a = o.TryCast<Animator>(); if (a != null) a.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
            }
            if (UnityEngine.Time.realtimeSinceStartup - lastTex > 15f || S.TextureSharpness.Value != appliedBias) { lastTex = UnityEngine.Time.realtimeSinceStartup; ApplyMipBias(); }
        }

        static void ApplyMipBias()
        {
            float bias = S.TextureSharpness.Value;
            if (bias != appliedBias) { texDone.Clear(); appliedBias = bias; TexturesSharpened = 0; }
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>());
            if (arr == null) return;
            int budget = 3000;
            foreach (var o in arr)
            {
                if (budget-- <= 0) break;
                IntPtr k = o.Pointer;
                if (texDone.Contains(k)) continue;
                texDone.Add(k);
                var t = o.TryCast<Texture2D>();
                if (t == null || t.mipmapCount <= 1) continue;
                if (t.width < 256) continue; // иконки/UI/мелочь не трогаем
                string n = t.name ?? "";
                // окружение/объекты/персонажи: имена карт вида m12_*, chara, obj; UI-атласы без мипов уже отсеяны
                t.mipMapBias = bias;
                TexturesSharpened++;
            }
            if (texDone.Count > 60000) texDone.Clear();
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
