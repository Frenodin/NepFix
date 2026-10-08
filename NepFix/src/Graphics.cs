using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NepFix
{
    /// Применение и поддержание графических настроек (игра периодически сбрасывает часть из них).
    internal static class Gfx
    {
        static Settings S => Plugin.S;

        // Отслеживание «игровых» значений, чтобы множители не накапливались.
        static readonly Dictionary<IntPtr, float> gameShadowDist = new();
        static readonly Dictionary<IntPtr, float> ourShadowDist = new();
        static readonly Dictionary<IntPtr, float> gameFar = new();
        static readonly Dictionary<IntPtr, float> ourFar = new();
        static readonly HashSet<IntPtr> featuresLogged = new();
        static readonly Dictionary<IntPtr, string> featName = new();
        static float qsShadowSet = -1, qsShadowT = -100;
        static readonly Dictionary<IntPtr, UnityEngine.Object> camObjs = new();
        public static int TargetFps { get; private set; } = 60;
        static int lastTarget = -1;
        public static int Refresh { get; private set; } = 60;

        static string backend;
        public static string Backend
        {
            get
            {
                if (backend != null) return backend;
                try
                {
                    bool dxvk = false;
                    foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                        if (m.ModuleName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase) &&
                            System.IO.Path.GetDirectoryName(m.FileName).Equals(System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), StringComparison.OrdinalIgnoreCase))
                            dxvk = true;
                    backend = dxvk ? "DirectX 11 → Vulkan через DXVK" : "родной DirectX 11";
                }
                catch { backend = "неизвестно"; }
                return backend;
            }
        }


        static int reshade = -1;
        /// ReShade загружен (dxgi.dll/d3d11.dll из папки игры с описанием ReShade): ему нужен буфер глубины без MSAA.
        public static bool ReShadeActive
        {
            get
            {
                if (reshade >= 0) return reshade == 1;
                reshade = 0;
                try
                {
                    var exeDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                    {
                        if (!string.Equals(System.IO.Path.GetDirectoryName(m.FileName), exeDir, StringComparison.OrdinalIgnoreCase)) continue;
                        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(m.FileName);
                        if ((vi.ProductName ?? "").Contains("ReShade") || (vi.FileDescription ?? "").Contains("ReShade")) { reshade = 1; Plugin.L.LogInfo("Обнаружен ReShade: MSAA принудительно выключен (нужен буфер глубины)"); break; }
                    }
                }
                catch { }
                return reshade == 1;
            }
        }

        public static readonly float[] PresetScale = { 0f, 1f, 1f / 1.5f, 1f / 1.7f, 0.5f, 1f / 3f };
        public static readonly string[] PresetName = { "выкл, масштаб вручную", "Native AA 1.0x", "Quality 1.5x", "Balanced 1.7x", "Performance 2.0x", "Ultra Performance 3.0x" };
        public static float EffectiveScale
        {
            get
            {
                int p = S.FsrPreset.Value;
                if (p > 0 && p < PresetScale.Length) return PresetScale[p];
                return Math.Clamp(S.RenderScale.Value, 0.5f, 2f);
            }
        }

        static UniversalRenderPipelineAsset urp; static int urpFrame = -100;
        /// Ассет URP. Запрашивается у Unity не чаще раза в 30 кадров: каждый запрос создаёт объекты-обёртки.
        public static UniversalRenderPipelineAsset Urp()
        {
            int f = Time.frameCount;
            if (urp != null && f - urpFrame < 30) return urp;
            urpFrame = f;
            try
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                if (rp == null) { urp = null; return null; }
                if (urp == null || urp.Pointer != rp.Pointer) urp = rp.TryCast<UniversalRenderPipelineAsset>();
                return urp;
            }
            catch { urp = null; return null; }
        }

        /// Лёгкие проверки — каждые ~0.5 с.
        public static void Enforce()
        {
            ICalls.Try(ApplyQuality, "quality");
            ICalls.Try(ApplyFps, "fps");
            ICalls.Try(ApplyUrp, "urp");
            ICalls.Try(Optimize.ApplyGlobal, "optimize");
            ICalls.Try(Lighting.ApplyFast, "lighting-fast");
            ICalls.Try(ClothFix.Apply, "cloth");
        }

        /// Более тяжёлые — сканирование объектов, каждые ~2 с.
        /// Задачи раньше шли все в одном кадре и при загрузке сцены давали фриз 30+ мс.
        /// Теперь по одной задаче за кадр: обход растягивается на 7 кадров.
        static int objStep = -1;
        public static void EnforceObjects() { if (objStep < 0) objStep = 0; }
        public static void EnforceObjectsTick()
        {
            if (objStep < 0) return;
            switch (objStep++)
            {
                case 0: ICalls.Try(ApplyCameras, "cameras"); break;
                case 1: if (S.RigidbodyInterpolation.Value) ICalls.Try(ApplyRigidbodies, "rigidbodies"); break;
                case 2: if (S.AnimatorAlwaysAnimate.Value) ICalls.Try(ApplyAnimators, "animators"); break;
                case 3: ICalls.Try(Distance.Apply, "distance"); break;
                case 4: ICalls.Try(FootFix.Apply, "footik"); break;
                case 5: ICalls.Try(Optimize.ApplyObjects, "objects"); break;
                default: ICalls.Try(Lighting.Apply, "lighting"); objStep = -1; break;
            }
        }

        static void ApplyQuality()
        {
            int want = S.QualityLevel.Value;
            if (want >= 0 && want <= 4)
            {
                int cur = ICalls.QualityLevel();
                if (cur >= 0 && cur != want)
                {
                    ICalls.SetQualityLevel(want);
                    Plugin.L.LogInfo($"Профиль качества {cur} -> {want}");
                }
            }
            if (S.FullResTextures.Value && ICalls.MasterTextureLimit() > 0) ICalls.SetMasterTextureLimit(0);
            if (S.Anisotropic.Value >= 2)
            {
                ICalls.SetAnisotropic(2); // ForceEnable
                ICalls.SetAnisoLimits(S.Anisotropic.Value, 16);
            }
            if (S.FourBoneSkinning.Value)
            {
                int sw = ICalls.SkinWeights();
                if (sw >= 0 && sw < 4) ICalls.SetSkinWeights(4);
            }
            float lb = ICalls.LodBias();
            if (lb >= 0 && Math.Abs(lb - S.LodBias.Value) > 0.001f) ICalls.SetLodBias(S.LodBias.Value);
            if (S.MaxQueuedFrames.Value > 0 && ICalls.MaxQueuedFrames() != S.MaxQueuedFrames.Value)
                ICalls.SetMaxQueuedFrames(S.MaxQueuedFrames.Value);
        }

        static float refreshT = -100;
        static void ApplyFps()
        {
            float nowT = Time.unscaledTime;
            if (nowT - refreshT > 5f) { refreshT = nowT; Refresh = Win32.RefreshRate(); }
            int target = S.FpsLimit.Value > 0 ? S.FpsLimit.Value : Refresh;
            if (S.VSync.Value) target = Math.Min(target, Refresh);
            target = Math.Clamp(target, 30, 500);
            TargetFps = target;

            if (S.FpsUnlock.Value)
            {
                int vs = S.VSync.Value ? 1 : 0;
                if (ICalls.VSyncCount() != vs) QualitySettings.vSyncCount = vs;
                if (Application.targetFrameRate != target || target != lastTarget)
                {
                    // Сеттер игры также пересчитывает её внутренний шаг времени (GameTime).
                    try { GameTime.TargetFrameRate = target; }
                    catch { FrameRateHook.Set(target); }
                    if (target != lastTarget) Plugin.L.LogInfo($"FPS-цель: {target} (монитор {Refresh} Гц, VSync {(S.VSync.Value ? "вкл" : "выкл")})");
                    lastTarget = target;
                }
            }

            ApplyFixedDt();
        }

        // ---- шаг физики: один владелец ----
        // Раньше его меняли и «Шаг физики под FPS», и мотоцикл, каждый со своей копией исходного значения.
        // При переключении во время езды одна из копий оказывалась уже изменённой, и шаг физики оставался чужим.
        static float fdtOrig = -1, fdtSet = -1;
        /// Желаемый мотоциклом шаг физики, 0 если не едем.
        public static float BikeFdt;
        public static void ApplyFixedDt()
        {
            float cur = Time.fixedDeltaTime;
            bool ours = fdtSet > 0 && Math.Abs(cur - fdtSet) < 1e-6f;
            if (!ours) { fdtOrig = cur; fdtSet = -1; } // значение игры (или игра поменяла его сама)
            float want = -1;
            // выше 90 Гц физика на больших картах съедает процессор и сама даёт фризы
            if (S.SyncPhysicsToFps.Value) want = 1f / Math.Clamp(TargetFps, 50, 90);
            else if (BikeFdt > 0) want = Math.Min(fdtOrig, BikeFdt);
            if (want > 0 && Math.Abs(want - fdtOrig) > 1e-6f)
            {
                if (Math.Abs(cur - want) > 1e-6f) Time.fixedDeltaTime = want;
                fdtSet = want;
            }
            else if (ours) { Time.fixedDeltaTime = fdtOrig; fdtSet = -1; }
        }

        static void ApplyUrp()
        {
            var a = Urp();
            if (a == null) return;
            IntPtr key = a.Pointer;

            int msaa = ReShadeActive ? 1 : S.Msaa.Value;
            if (a.msaaSampleCount != msaa) a.msaaSampleCount = msaa;
            float rs = EffectiveScale;
            if (Math.Abs(a.renderScale - rs) > 0.001f) a.renderScale = rs;
            var uf = S.FsrPreset.Value > 0 ? UpscalingFilterSelection.FSR : (UpscalingFilterSelection)(int)S.UpscaleFilter.Value;
            if (a.upscalingFilter != uf) a.upscalingFilter = uf;
            if (!a.fsrOverrideSharpness) a.fsrOverrideSharpness = true;
            if (Math.Abs(a.fsrSharpness - S.FsrSharpness.Value) > 0.001f) a.fsrSharpness = S.FsrSharpness.Value;
            if (a.supportsHDR != S.HdrRendering.Value) a.supportsHDR = S.HdrRendering.Value;

            if (S.ShadowsOverride.Value)
            {
                if (!a.supportsMainLightShadows) a.supportsMainLightShadows = true;
                if (a.mainLightShadowmapResolution != S.ShadowResolution.Value) a.mainLightShadowmapResolution = S.ShadowResolution.Value;
                if (a.shadowCascadeCount != S.ShadowCascades.Value) a.shadowCascadeCount = Math.Clamp(S.ShadowCascades.Value, 1, 4);
                if (a.supportsSoftShadows != S.SoftShadows.Value) a.supportsSoftShadows = S.SoftShadows.Value;
                if (S.AdditionalLightShadows.Value && !a.supportsAdditionalLightShadows) a.supportsAdditionalLightShadows = true;

                // Дальность: карта игры задаёт своё значение (MapSceneURPSettings) — масштабируем его.
                float cur = a.shadowDistance;
                if (!ourShadowDist.TryGetValue(key, out float ours) || Math.Abs(cur - ours) > 0.01f)
                    gameShadowDist[key] = cur;
                float baseD = gameShadowDist[key];
                float want = Math.Max(baseD * S.ShadowDistanceMul.Value, S.ShadowDistanceMin.Value);
                want = Math.Min(want, 400f);
                if (Math.Abs(cur - want) > 0.01f) a.shadowDistance = want;
                ourShadowDist[key] = want;
                if (Math.Abs(qsShadowSet - want) > 0.01f || Time.unscaledTime - qsShadowT > 10f) { QualitySettings.shadowDistance = want; qsShadowSet = want; qsShadowT = Time.unscaledTime; }
            }

            ApplyFeatures(a);
        }

        static void ApplyFeatures(UniversalRenderPipelineAsset a)
        {
            var list = a.m_RendererDataList;
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                var rd = list[i];
                if (rd == null) continue;
                var feats = rd.m_RendererFeatures;
                if (feats == null) continue;
                bool log = featuresLogged.Add(rd.Pointer) && S.VerboseLog.Value;
                for (int j = 0; j < feats.Count; j++)
                {
                    var f = feats[j];
                    if (f == null) continue;
                    if (!featName.TryGetValue(f.Pointer, out string tn)) { tn = f.GetIl2CppType().Name; featName[f.Pointer] = tn; }
                    if (log) Plugin.L.LogInfo($"  Renderer '{rd.name}': feature '{f.name}' ({tn}) active={f.isActive}");
                    if (tn.Contains("AmbientOcclusion") && f.isActive != S.Ssao.Value) f.SetActive(S.Ssao.Value);
                }
            }
        }

        static Il2CppReferenceArray<Camera> camBuf = new(32);

        static void ApplyCameras()
        {
            int n = Camera.allCamerasCount;
            if (n > camBuf.Length) camBuf = new Il2CppReferenceArray<Camera>(n + 8);
            n = Camera.GetAllCameras(camBuf);
            for (int i = 0; i < n; i++)
            {
                var cam = camBuf[i];
                if (cam == null) continue;
                bool am = S.Msaa.Value > 1 && !ReShadeActive;
                if (cam.allowMSAA != am) cam.allowMSAA = am;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null && data.renderType == CameraRenderType.Base)
                {
                    var pa = S.PostAa.Value;
                    var am2 = pa switch
                    {
                        PostAA.FXAA => AntialiasingMode.FastApproximateAntialiasing,
                        PostAA.Off => AntialiasingMode.None,
                        _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing
                    };
                    var aq = pa switch
                    {
                        PostAA.SMAA_Low => AntialiasingQuality.Low,
                        PostAA.SMAA_Medium => AntialiasingQuality.Medium,
                        _ => AntialiasingQuality.High
                    };
                    if (data.antialiasing != am2) data.antialiasing = am2;
                    if (data.antialiasingQuality != aq) data.antialiasingQuality = aq;
                }
                if (!cam.orthographic)
                {
                    IntPtr k = cam.Pointer;
                    float cur = cam.farClipPlane;
                    if (!ourFar.TryGetValue(k, out float ours) || Math.Abs(cur - ours) > 0.01f) gameFar[k] = cur;
                    float want = gameFar[k] * Math.Clamp(S.FarClipMul.Value, 1f, 5f);
                    if (Math.Abs(cur - want) > 0.01f) cam.farClipPlane = want;
                    ourFar[k] = want; camObjs[k] = cam;
                }
            }
            Scan.PruneDestroyed(camObjs, gameFar, ourFar);
        }

        /// Интерполяция только для предметов, которые двигает физика. Героинь и врагов двигает сама игра:
        /// в бою и в суперприёмах она переносит их в нужную точку сцены, а интерполяция тянула тело назад,
        /// и модель оказывалась сбоку или сверху кадра. Таких не трогаем.
        static readonly Dictionary<IntPtr, bool> rbChecked = new(); static int rbScene = int.MinValue;
        public static int RbInterpolated, RbSkipped;
        static void ApplyRigidbodies()
        {
            var arr = Scan.All<Rigidbody>();
            if (arr == null) return;
            int sc = Scan.Scene;
            if (sc != rbScene || rbChecked.Count > 8000) { rbScene = sc; rbChecked.Clear(); }
            int on = 0, skip = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                IntPtr p = Scan.Raw(arr, i);
                if (p == IntPtr.Zero) continue;
                if (!rbChecked.TryGetValue(p, out bool ok))
                {
                    var rb = arr[i]?.TryCast<Rigidbody>();
                    if (rb == null) continue;
                    ok = true;
                    try
                    {
                        if (rb.GetComponentInParent(Il2CppType.Of<RpgUnit>(), true) != null) ok = false;
                        else if (rb.GetComponentInParent(Il2CppType.Of<Animator>(), true) != null) ok = false;
                        else if (rb.GetComponentInParent(Il2CppType.Of<CharacterController>(), true) != null) ok = false;
                    }
                    catch { ok = false; }
                    rbChecked[p] = ok;
                    if (ok) rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                if (ok) on++; else skip++;
            }
            RbInterpolated = on; RbSkipped = skip;
        }

        static void ApplyAnimators()
        {
            var arr = Scan.All<Animator>();
            if (arr == null) return;
            foreach (var o in arr)
            {
                var an = o.TryCast<Animator>();
                if (an != null && an.cullingMode != AnimatorCullingMode.AlwaysAnimate) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        public static string Diagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== NepFix диагностика ===");
            sb.AppendLine($"Quality level: {ICalls.QualityLevel()}  masterTextureLimit: {ICalls.MasterTextureLimit()}  skinWeights: {ICalls.SkinWeights()}  lodBias: {ICalls.LodBias()}  maxQueuedFrames: {ICalls.MaxQueuedFrames()}  vSync: {ICalls.VSyncCount()}");
            sb.AppendLine($"Интерполяция физики: предметов {RbInterpolated}, персонажей пропущено {RbSkipped}");
            sb.AppendLine($"targetFrameRate: {Application.targetFrameRate}  монитор: {Refresh} Гц  fixedDeltaTime: {Time.fixedDeltaTime}");
            try { sb.AppendLine($"GameTime: Standard={GameTime.StandardFrameRate} Target={GameTime.TargetFrameRate} SpeedFactor={GameTime.FrameRateSpeedFactor} FPS={GameTime.FPS} Speed={GameTime.Speed}"); }
            catch (Exception e) { sb.AppendLine("GameTime: " + e.Message); }
            var a = Urp();
            if (a != null)
                sb.AppendLine($"URP '{a.name}': MSAA {a.msaaSampleCount}, scale {a.renderScale}, upscale {a.upscalingFilter}, HDR {a.supportsHDR}, shadows {a.supportsMainLightShadows} res {a.mainLightShadowmapResolution} dist {a.shadowDistance} (игра {(gameShadowDist.TryGetValue(a.Pointer, out var g) ? g : -1)}) cascades {a.shadowCascadeCount} soft {a.supportsSoftShadows}, SRPBatcher {a.useSRPBatcher}");
            int n = Camera.GetAllCameras(camBuf);
            for (int i = 0; i < n; i++)
            {
                var c = camBuf[i];
                if (c == null) continue;
                var d = c.GetComponent<UniversalAdditionalCameraData>();
                sb.AppendLine($"  Camera '{c.name}' far={c.farClipPlane} msaa={c.allowMSAA} " + (d != null ? $"type={d.renderType} aa={d.antialiasing}/{d.antialiasingQuality} post={d.renderPostProcessing}" : "(нет URP data)"));
            }
            featuresLogged.Clear();
            return sb.ToString();
        }
    }
}
