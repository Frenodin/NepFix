using System;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NepFix
{
    /// Экранное освещение NepFX: отражённый свет (SSGI), затенение (AO), контактные тени.
    /// Шейдер собирается отдельно в Unity 2021.3.39f1 (проект NepFX_Unity) в пакет nepfx.bundle.
    internal static class NepFX
    {
        /// Тени от условного света сверху и пятна под персонажами. Под потолком всегда, если включены.
        /// На открытом воздухе только когда у сцены нет солнца с тенями: иначе к настоящим теням добавлялись
        /// поддельные, и на траве под деревьями и у персонажей появлялись тёмные пятна, как в Топях Ямиму.
        static bool Overhead => S.FxIndoorShadows.Value && (Lighting.Indoor || Lighting.Cave || (S.FxOverheadAlways.Value && Lighting.SunShadows <= 0));

        static Settings S => Plugin.S;
        public static string FogInfo = "";
        static Material mat;
        static bool triedLoad;
        public static string Status = "пакет шейдеров не загружен";
        static RenderTexture histA, histB;
        static Matrix4x4 prevVP;
        static bool hasPrev;
        static int frame;
        public static Camera CurrentCamera;
        public static bool Indoor;
        // диагностика для меню
        public static int ExecCount, SkipCount;
        public static string LastCamera = "", LastSkip = "", LastError = "";
        static int execWindow; static float windowStart; public static float ExecPerSec;
        public static string Diag
        {
            get
            {
                if (mat == null) return "материал не создан";
                if (ExecCount == 0) return "проход ни разу не выполнился" + (LastSkip != "" ? ": " + LastSkip : "");
                string s = $"выполняется {ExecPerSec:0} раз/с, камера {LastCamera}, глубина: {DepthInfo}" + (Lighting.Indoor ? $", над отрядом потолок, тени сверху включены, теней под персонажами {Blobs.Count}{(Blobs.Error != "" ? " (ошибка: " + Blobs.Error + ")" : "")}, моделей персонажей {Chars.Renderers.Count}" : "");
                if (FogInfo != "") s += $", туман сцены: {FogInfo}";
                if (LastSkip != "") s += $", пропуск: {LastSkip}";
                if (LastError != "") s += ". Ошибка: " + LastError;
                if (MsaaWatch.ErrorsPerSec > 0 || MsaaWatch.Verdict != "") s += $". Ошибок MSAA в секунду: {MsaaWatch.ErrorsPerSec}" + (MsaaWatch.Verdict != "" ? ", " + MsaaWatch.Verdict : "");
                return s;
            }
        }
        static readonly System.Collections.Generic.Dictionary<IntPtr, bool> camKind = new();
        static void Skip(string why) { SkipCount++; LastSkip = why; }

        static readonly int idSrc = Shader.PropertyToID("_NepSourceRT");
        static readonly int idTrace = Shader.PropertyToID("_NepTraceRT");
        static readonly int idBlurA = Shader.PropertyToID("_NepBlurART");
        static readonly int idBlurB = Shader.PropertyToID("_NepBlurBRT");
        static readonly int idSsrA = Shader.PropertyToID("_NepSsrART"), idSsrB = Shader.PropertyToID("_NepSsrBRT"), idGloss = Shader.PropertyToID("_NepGlossRT");
        public static string SsrInfo = "";
        static readonly int idDepth = Shader.PropertyToID("_NepDepthRT");
        static readonly int idMask = Shader.PropertyToID("_NepCharMaskRT");
        static readonly int idBlobs = Shader.PropertyToID("_NepBlobs");
        public static string DepthInfo = "";

        static Shader shader;
        static AssetBundle bundle;
        static float nextTry;

        /// Материал нужно держать от выгрузки: при смене сцены Unity удаляет «ничейные» ресурсы.
        public static bool Ready
        {
            get
            {
                if (mat != null) return true;
                if (Time.unscaledTime < nextTry) return false;
                nextTry = Time.unscaledTime + 2f;
                try
                {
                    if (shader == null)
                    {
                        string dir = Path.GetDirectoryName(typeof(NepFX).Assembly.Location);
                        string path = Path.Combine(dir, "nepfx.bundle");
                        if (!File.Exists(path)) { Status = "нет файла nepfx.bundle, соберите проект NepFX_Unity"; return false; }
                        if (bundle == null) bundle = AssetBundle.LoadFromFile(path);
                        if (bundle == null) { Status = "не удалось открыть nepfx.bundle"; return false; }
                        bundle.hideFlags = HideFlags.HideAndDontSave;
                        var arr = bundle.LoadAllAssets(Il2CppType.Of<Shader>());
                        if (arr != null) foreach (var o in arr) { shader = o.TryCast<Shader>(); if (shader != null) break; }
                        if (shader == null) { Status = "в пакете нет шейдера"; return false; }
                        shader.hideFlags = HideFlags.HideAndDontSave;
                        if (!shader.isSupported) { Status = "шейдер не поддерживается этой видеокартой"; return false; }
                    }
                    bool again = triedLoad;
                    mat = new Material(shader);
                    mat.hideFlags = HideFlags.HideAndDontSave;
                    triedLoad = true;
                    Status = $"загружен, {shader.name}, проходов {mat.passCount}";
                    Plugin.L.LogInfo("NepFX: " + Status + (again ? ", материал создан заново после смены сцены" : ""));
                    return true;
                }
                catch (Exception e) { Status = "ошибка загрузки: " + e.Message; Plugin.L.LogWarning("NepFX: " + e); return false; }
            }
        }

        static RenderTexture EnsureHist(ref RenderTexture rt, int w, int h)
        {
            if (rt != null && (rt.width != w || rt.height != h)) { RenderTexture.ReleaseTemporary(rt); rt = null; }
            if (rt == null)
            {
                rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0));
                hasPrev = false;
            }
            return rt;
        }

        static void Draw(CommandBuffer cmd, RenderTargetIdentifier target, int pass)
        {
            cmd.SetRenderTarget(target);
            cmd.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Triangles, 3);
        }

        public static void Execute(ScriptableRenderContext context, ScriptableRenderer r, ScriptableRenderPass pass, Camera cam, bool overlay, int descW, int descH, int msaa)
        {
            if (cam == null) cam = CurrentCamera;
            if (cam == null) { Skip("нет камеры"); return; }
            if (!overlay)
            {
                IntPtr k = cam.Pointer;
                if (!camKind.TryGetValue(k, out bool isOverlay))
                {
                    isOverlay = false;
                    try
                    {
                        var ad = cam.GetComponent<UniversalAdditionalCameraData>();
                        if (ad != null && ad.renderType != CameraRenderType.Base) isOverlay = true;
                    }
                    catch { }
                    if (cam.farClipPlane < 2f) isOverlay = true;
                    camKind[k] = isOverlay;
                    Plugin.L.LogInfo($"NepFX: камера '{cam.name}' {(isOverlay ? "пропускается, это наложение" : "обрабатывается")}, far {cam.farClipPlane}, {cam.pixelWidth}x{cam.pixelHeight}, MSAA по расчёту {msaa}, allowMSAA {cam.allowMSAA}, цель {(cam.targetTexture == null ? "экран" : cam.targetTexture.name + " aa " + cam.targetTexture.antiAliasing)}");
                    if (camKind.Count > 64) camKind.Clear();
                }
                overlay = isOverlay;
            }
            if (overlay) { Skip("камера-наложение " + cam.name); return; }
            if (cam.orthographic) { Skip("ортографическая камера " + cam.name); return; }
            if (cam.cameraType != CameraType.Game) { Skip("камера редактора/превью"); return; }
            if (cam.targetTexture != null && !S.FxInMenus.Value) { Skip("камера меню " + cam.name); return; }
            ExecCount++; execWindow++;
            float now = Time.unscaledTime;
            if (now - windowStart >= 1f) { ExecPerSec = execWindow / Math.Max(0.001f, now - windowStart); execWindow = 0; windowStart = now; }
            LastCamera = cam.name;
            float scale = 1f; try { scale = Gfx.Urp()?.renderScale ?? 1f; } catch { }
            int w = (int)(cam.pixelWidth * scale), h = (int)(cam.pixelHeight * scale);
            // дескриптор точнее, если он того же формата кадра, что и камера
            if (descW > 0 && descH > 0 && Math.Abs((float)descW / descH - (float)cam.pixelWidth / Math.Max(1, cam.pixelHeight)) < 0.02f) { w = descW; h = descH; }
            w = Math.Max(64, w); h = Math.Max(64, h);
            int hw = Math.Max(32, w / 2), hh = Math.Max(32, h / 2);
            frame++;

            // основной источник света
            Vector4 light = Vector4.zero;
            try
            {
                var sun = RenderSettings.sun;
                if (sun != null && sun.enabled && sun.intensity > 0.01f) { var d = -sun.transform.forward; light = new Vector4(d.x, d.y, d.z, 1); }
            }
            catch { }
            // Под потолком (пещеры, шахты) окружение освещено запечённым светом, а солнце игры светит только на персонажей.
            // Контактные тени от него на неровных скалах давали тёмные угловатые пятна там, куда это солнце вообще не светит.
            if ((Lighting.Indoor || Lighting.Cave) && S.FxIndoorSunContact.Value == false) light.w = 0;
            bool hasMotion = false; try { hasMotion = Shader.GetGlobalTexture("_MotionVectorTexture") != null; } catch { }

            Shader.SetGlobalVector("_NepParams", new Vector4(S.FxGiRadius.Value, S.FxGiIntensity.Value, S.FxAoIntensity.Value, frame % 64));
            float cLen = S.FxContactLength.Value;
            Indoor = light.w < 0.5f;
            Shader.SetGlobalVector("_NepParams2", new Vector4(cLen, S.FxContactIntensity.Value, hasPrev ? S.FxTemporal.Value : 0f, S.FxDebug.Value));
            Shader.SetGlobalVector("_NepParams3", new Vector4(S.FxRays.Value, S.FxSteps.Value, 0.6f, hasMotion && S.FxUseMotion.Value ? 1 : 0));
            Shader.SetGlobalVector("_NepLight", light);
            {
                float fe = Math.Max(5f, S.FxFadeDistance.Value);
                float fm = 0, fd = 0, fs = 0, fend = 0;
                try
                {
                    if (RenderSettings.fog)
                    {
                        var mode = RenderSettings.fogMode;
                        fm = mode == FogMode.Linear ? 1 : mode == FogMode.Exponential ? 2 : 3;
                        fd = RenderSettings.fogDensity; fs = RenderSettings.fogStartDistance; fend = RenderSettings.fogEndDistance;
                    }
                }
                catch { }
                Shader.SetGlobalVector("_NepFade", new Vector4(fe * 0.45f, fe, fm, fd));
                Shader.SetGlobalVector("_NepFog2", new Vector4(fs, fend, 0, 0));
                FogInfo = fm == 0 ? "нет" : fm == 1 ? $"линейный {fs:0}–{fend:0} м" : $"плотность {fd:0.000}";
            }
            Shader.SetGlobalVector("_NepSize", new Vector4(w, h, hw, hh));

            Shader.SetGlobalMatrix("_NepPrevVP", prevVP);

            var hPrev = EnsureHist(ref histA, hw, hh);
            var hCur = EnsureHist(ref histB, hw, hh);

            var cmd = new CommandBuffer();
            cmd.name = "NepFX";
            var color = r.cameraColorTarget;
            cmd.GetTemporaryRT(idSrc, new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idTrace, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idBlurA, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idBlurB, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);

            // глубина: своя копия из буфера глубины камеры или текстура игры
            int ds = S.FxDepthSource.Value;
            bool own = ds != 1;
            bool ms = ds == 2 || (ds == 0 && ((msaa > 1) ^ MsaaWatch.Flip(cam.name)));
            RenderTargetIdentifier depthSrc = default;
            if (own)
            {
                try { depthSrc = r.cameraDepthTarget; } catch { own = false; }
                if (own && depthSrc == new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)) own = false;
            }
            int pMs = mat.FindPass("CopyDepthMS");
            if (own && ms && pMs < 0) { LastError = "старый nepfx.bundle, запустите build_nepfx.bat"; own = false; }
            if (own)
            {
                cmd.GetTemporaryRT(idDepth, new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 0), FilterMode.Point);
                if (ms) { cmd.SetGlobalTexture("_NepDepthSrcMS", depthSrc); Draw(cmd, new RenderTargetIdentifier(idDepth), pMs); }
                else { cmd.SetGlobalTexture("_NepDepthSrc", depthSrc); Draw(cmd, new RenderTargetIdentifier(idDepth), 6); }
                cmd.SetGlobalTexture("_NepDepth", new RenderTargetIdentifier(idDepth));
                DepthInfo = ms ? $"своя копия, MSAA {msaa}x" : "своя копия, без MSAA";
            }
            else
            {
                cmd.SetGlobalTexture("_NepDepth", new RenderTargetIdentifier("_CameraDepthTexture"));
                DepthInfo = "текстура игры _CameraDepthTexture";
            }

            // цвет камеры при MSAA Unity сам сводит в обычную текстуру при чтении, поэтому хватает Blit
            cmd.Blit(color, new RenderTargetIdentifier(idSrc));

            // маска персонажей: их поверхность рисуется отдельно, чтобы ослабить на них затенение и шум
            bool mask = false;
            int pMask = mat.FindPass("CharMask");
            var chars = Chars.Renderers;
            if (pMask >= 0 && chars.Count > 0 && S.FxCharStrength.Value < 0.99f)
            {
                cmd.GetTemporaryRT(idMask, new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 24), FilterMode.Point);
                cmd.SetRenderTarget(new RenderTargetIdentifier(idMask));
                cmd.ClearRenderTarget(true, true, Color.clear);
                foreach (var (cr, subs) in chars)
                {
                    if (cr == null || !cr.enabled || !cr.isVisible) continue;
                    for (int sm = 0; sm < subs; sm++) cmd.DrawRenderer(cr, mat, sm, pMask);
                }
                cmd.SetGlobalTexture("_NepCharMask", new RenderTargetIdentifier(idMask));
                mask = true;
            }
            cmd.SetGlobalTexture("_NepSource", new RenderTargetIdentifier(idSrc));

            cmd.SetGlobalVector("_NepParams4", new Vector4(S.FxSplit.Value ? 1 : 0, S.FxCharStrength.Value, mask ? 1 : 0, Overhead ? S.FxIndoorLength.Value : 0f));
            bool blobsOn = Overhead && S.FxBlobStrength.Value > 0.001f;
            if (blobsOn) Blobs.Update(); else Blobs.Count = 0;
            cmd.SetGlobalVectorArray(idBlobs, Blobs.Data);
            cmd.SetGlobalVector("_NepBlobInfo", new Vector4(Blobs.Count, S.FxBlobStrength.Value, 0, 0));
            Draw(cmd, new RenderTargetIdentifier(idTrace), 0);                         // трассировка
            cmd.SetGlobalTexture("_NepTrace", new RenderTargetIdentifier(idTrace));
            cmd.SetGlobalTexture("_NepHistory", new RenderTargetIdentifier(hPrev));
            Draw(cmd, new RenderTargetIdentifier(hCur), 1);                            // накопление
            cmd.SetGlobalTexture("_NepBlurSrc", new RenderTargetIdentifier(hCur));
            Draw(cmd, new RenderTargetIdentifier(idBlurA), 2);                         // размытие
            cmd.SetGlobalTexture("_NepBlurSrc", new RenderTargetIdentifier(idBlurA));
            Draw(cmd, new RenderTargetIdentifier(idBlurB), 3);
            cmd.SetGlobalTexture("_NepGI", new RenderTargetIdentifier(idBlurB));

            // отражения по экрану
            int pSsr = mat.FindPass("SSR");
            bool ssr = S.FxSsr.Value && pSsr >= 0;
            SsrInfo = S.FxSsr.Value && pSsr < 0 ? "нужен новый nepfx.bundle, запустите build_nepfx.bat" : "";
            cmd.SetGlobalVector("_NepSsr", new Vector4(ssr ? 1 : 0, S.FxSsrDistance.Value, S.FxSsrMode.Value, S.FxSsrIntensity.Value));
            if (ssr)
            {
                // маска блестящих объектов: глубина + сила блеска
                Prof.Run("gloss", Gloss.Update);
                cmd.GetTemporaryRT(idGloss, new RenderTextureDescriptor(w, h, RenderTextureFormat.RGFloat, 24), FilterMode.Point);
                cmd.SetRenderTarget(new RenderTargetIdentifier(idGloss));
                cmd.ClearRenderTarget(true, true, Color.clear);
                if (pMask >= 0)
                    foreach (var (gr, sub, g) in Gloss.Items)
                    {
                        if (gr == null || !gr.isVisible) continue;
                        cmd.SetGlobalFloat("_NepGlossVal", g);
                        cmd.DrawRenderer(gr, mat, sub, pMask);
                    }
                cmd.SetGlobalTexture("_NepGlossMask", new RenderTargetIdentifier(idGloss));
                cmd.GetTemporaryRT(idSsrA, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
                Draw(cmd, new RenderTargetIdentifier(idSsrA), pSsr);
                if (S.FxSsrBlur.Value)
                {
                    cmd.GetTemporaryRT(idSsrB, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
                    cmd.SetGlobalTexture("_NepBlurSrc", new RenderTargetIdentifier(idSsrA));
                    Draw(cmd, new RenderTargetIdentifier(idSsrB), 2);
                    cmd.SetGlobalTexture("_NepBlurSrc", new RenderTargetIdentifier(idSsrB));
                    Draw(cmd, new RenderTargetIdentifier(idSsrA), 3);
                }
                cmd.SetGlobalTexture("_NepSSR", new RenderTargetIdentifier(idSsrA));
            }
            Draw(cmd, color, 4);                                                       // композит

            if (own) cmd.ReleaseTemporaryRT(idDepth);
            if (mask) cmd.ReleaseTemporaryRT(idMask);
            if (ssr) { cmd.ReleaseTemporaryRT(idGloss); cmd.ReleaseTemporaryRT(idSsrA); if (S.FxSsrBlur.Value) cmd.ReleaseTemporaryRT(idSsrB); }
            cmd.ReleaseTemporaryRT(idSrc); cmd.ReleaseTemporaryRT(idTrace); cmd.ReleaseTemporaryRT(idBlurA); cmd.ReleaseTemporaryRT(idBlurB);
            context.ExecuteCommandBuffer(cmd);
            cmd.Release();

            // история: текущий кадр станет «прошлым»
            var t = histA; histA = histB; histB = t;
            prevVP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            hasPrev = true;
        }
    }
}
