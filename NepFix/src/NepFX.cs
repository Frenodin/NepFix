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
        /// Состояние каждой камеры: тип (проверяется раз в секунду, а не каждый кадр) и своя история накопления.
        /// Раньше история была общей на все камеры, и при эффекте в меню кадры разных камер смешивались.
        class CamState
        {
            public Camera cam; public string name = ""; public bool skip; public string why = ""; public float checkedAt = -100, usedAt, seenAt;
            public RenderTexture histA, histB; public Matrix4x4 prevVP; public bool hasPrev; public bool logged;
        }
        static readonly System.Collections.Generic.Dictionary<IntPtr, CamState> cams = new();
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
        static readonly int idParams = Shader.PropertyToID("_NepParams"), idParams2 = Shader.PropertyToID("_NepParams2"), idParams3 = Shader.PropertyToID("_NepParams3"),
            idParams4 = Shader.PropertyToID("_NepParams4"), idLight = Shader.PropertyToID("_NepLight"), idFade = Shader.PropertyToID("_NepFade"), idFog2 = Shader.PropertyToID("_NepFog2"),
            idSize = Shader.PropertyToID("_NepSize"), idPrevVP = Shader.PropertyToID("_NepPrevVP"), idDepthSrcMS = Shader.PropertyToID("_NepDepthSrcMS"),
            idDepthSrc = Shader.PropertyToID("_NepDepthSrc"), idDepthTex = Shader.PropertyToID("_NepDepth"), idCamDepth = Shader.PropertyToID("_CameraDepthTexture"),
            idCharMask = Shader.PropertyToID("_NepCharMask"), idSource = Shader.PropertyToID("_NepSource"), idBlobInfo = Shader.PropertyToID("_NepBlobInfo"),
            idTraceTex = Shader.PropertyToID("_NepTrace"), idHistory = Shader.PropertyToID("_NepHistory"), idBlurSrc = Shader.PropertyToID("_NepBlurSrc"),
            idGI = Shader.PropertyToID("_NepGI"), idSsr = Shader.PropertyToID("_NepSsr"), idGlossVal = Shader.PropertyToID("_NepGlossVal"),
            idGlossMask = Shader.PropertyToID("_NepGlossMask"), idSSRTex = Shader.PropertyToID("_NepSSR"), idMotion = Shader.PropertyToID("_MotionVectorTexture");
        static int pMs = -1, pMask = -1, pSsr = -1;
        static CommandBuffer cmdBuf;
        static RenderTargetIdentifier rtSrc = new(idSrc), rtTrace = new(idTrace), rtBlurA = new(idBlurA), rtBlurB = new(idBlurB), rtSsrA = new(idSsrA), rtSsrB = new(idSsrB),
            rtGloss = new(idGloss), rtDepth = new(idDepth), rtMask = new(idMask), rtCamDepth = new(idCamDepth);
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
                    pMs = mat.FindPass("CopyDepthMS"); pMask = mat.FindPass("CharMask"); pSsr = mat.FindPass("SSR");
                    triedLoad = true;
                    Status = $"загружен, {shader.name}, проходов {mat.passCount}";
                    Plugin.L.LogInfo("NepFX: " + Status + (again ? ", материал создан заново после смены сцены" : ""));
                    return true;
                }
                catch (Exception e) { Status = "ошибка загрузки: " + e.Message; Plugin.L.LogWarning("NepFX: " + e); return false; }
            }
        }

        static RenderTexture EnsureHist(CamState st, ref RenderTexture rt, int w, int h)
        {
            if (rt != null && (rt.width != w || rt.height != h)) { RenderTexture.ReleaseTemporary(rt); rt = null; }
            if (rt == null)
            {
                rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0));
                st.hasPrev = false;
            }
            return rt;
        }

        static void ReleaseHist(CamState st)
        {
            try { if (st.histA != null) RenderTexture.ReleaseTemporary(st.histA); } catch { }
            try { if (st.histB != null) RenderTexture.ReleaseTemporary(st.histB); } catch { }
            st.histA = st.histB = null; st.hasPrev = false;
        }

        /// Раз в кадр из Update: освобождает историю камер, которые давно не рисовались, и всё, если NepFX выключен.
        public static void Housekeeping()
        {
            if (cams.Count == 0) return;
            float now = Time.unscaledTime;
            bool off = !S.FxEnabled.Value;
            System.Collections.Generic.List<IntPtr> dead = null;
            foreach (var kv in cams)
            {
                var st = kv.Value;
                if (off || now - st.usedAt > 3f) { if (st.histA != null || st.histB != null) ReleaseHist(st); }
                if (st.cam == null || now - st.seenAt > 60f) (dead ??= new()).Add(kv.Key);
            }
            if (dead != null) foreach (var k in dead) { ReleaseHist(cams[k]); cams.Remove(k); }
        }

        static CamState State(Camera cam, bool overlayHint)
        {
            IntPtr k = cam.Pointer;
            if (!cams.TryGetValue(k, out var st)) { st = new CamState { cam = cam }; cams[k] = st; }
            float now = Time.unscaledTime;
            st.seenAt = now;
            if (now - st.checkedAt >= 1f)
            {
                st.checkedAt = now;
                st.name = cam.name ?? "";
                bool isOverlay = overlayHint;
                try
                {
                    var ad = cam.GetComponent<UniversalAdditionalCameraData>();
                    if (ad != null && ad.renderType != CameraRenderType.Base) isOverlay = true;
                }
                catch { }
                if (cam.farClipPlane < 2f) isOverlay = true;
                var tt = cam.targetTexture;
                st.skip = true;
                if (isOverlay) st.why = "камера-наложение " + st.name;
                else if (cam.orthographic) st.why = "ортографическая камера " + st.name;
                else if (cam.cameraType != CameraType.Game) st.why = "камера редактора/превью";
                else if (tt != null && !S.FxInMenus.Value) st.why = "камера меню " + st.name;
                else st.skip = false;
                if (!st.logged)
                {
                    st.logged = true;
                    Plugin.L.LogInfo($"NepFX: камера '{st.name}' {(st.skip ? "пропускается: " + st.why : "обрабатывается")}, far {cam.farClipPlane}, {cam.pixelWidth}x{cam.pixelHeight}, allowMSAA {cam.allowMSAA}, цель {(tt == null ? "экран" : tt.name + " aa " + tt.antiAliasing)}");
                }
            }
            return st;
        }

        /// Нужен ли проход этой камере. Вызывается при постановке прохода в очередь: камерам без эффекта он не нужен,
        /// а с ним URP готовил бы для них текстуру глубины зря.
        public static bool Accept(Camera cam)
        {
            if (cam == null) return true;
            try { return !State(cam, false).skip; } catch { return true; }
        }

        static void Draw(CommandBuffer cmd, RenderTargetIdentifier target, int pass)
        {
            cmd.SetRenderTarget(target);
            cmd.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Triangles, 3);
        }

        static int blobsFrame = -1;

        public static void Execute(ScriptableRenderContext context, ScriptableRenderer r, ScriptableRenderPass pass, Camera cam, bool overlay, int descW, int descH, int msaa)
        {
            if (cam == null) cam = CurrentCamera;
            if (cam == null) { Skip("нет камеры"); return; }
            var st = State(cam, overlay);
            if (st.skip) { Skip(st.why); return; }
            float now = Time.unscaledTime;
            st.usedAt = now;
            ExecCount++; execWindow++;
            if (now - windowStart >= 1f) { ExecPerSec = execWindow / Math.Max(0.001f, now - windowStart); execWindow = 0; windowStart = now; }
            bool menu = Plugin.MenuOpen;
            if (menu) LastCamera = st.name;
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
            bool hasMotion = false;
            if (S.FxUseMotion.Value) try { hasMotion = Shader.GetGlobalTexture(idMotion) != null; } catch { }

            Shader.SetGlobalVector(idParams, new Vector4(S.FxGiRadius.Value, S.FxGiIntensity.Value, S.FxAoIntensity.Value, frame % 64));
            float cLen = S.FxContactLength.Value;
            Indoor = light.w < 0.5f;
            Shader.SetGlobalVector(idParams2, new Vector4(cLen, S.FxContactIntensity.Value, st.hasPrev ? S.FxTemporal.Value : 0f, S.FxDebug.Value));
            Shader.SetGlobalVector(idParams3, new Vector4(S.FxRays.Value, S.FxSteps.Value, 0.6f, hasMotion ? 1 : 0));
            Shader.SetGlobalVector(idLight, light);
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
                Shader.SetGlobalVector(idFade, new Vector4(fe * 0.45f, fe, fm, fd));
                Shader.SetGlobalVector(idFog2, new Vector4(fs, fend, 0, 0));
                if (menu) FogInfo = fm == 0 ? "нет" : fm == 1 ? $"линейный {fs:0}–{fend:0} м" : $"плотность {fd:0.000}";
            }
            Shader.SetGlobalVector(idSize, new Vector4(w, h, hw, hh));
            Shader.SetGlobalMatrix(idPrevVP, st.prevVP);

            var hPrev = EnsureHist(st, ref st.histA, hw, hh);
            var hCur = EnsureHist(st, ref st.histB, hw, hh);

            // один буфер команд на всё время работы: раньше он создавался и удалялся каждый кадр
            var cmd = cmdBuf ??= new CommandBuffer { name = "NepFX" };
            cmd.Clear();
            var color = r.cameraColorTarget;
            cmd.GetTemporaryRT(idSrc, new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idTrace, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idBlurA, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
            cmd.GetTemporaryRT(idBlurB, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);

            // глубина: своя копия из буфера глубины камеры или текстура игры
            int ds = S.FxDepthSource.Value;
            bool own = ds != 1;
            bool ms = ds == 2 || (ds == 0 && ((msaa > 1) ^ MsaaWatch.Flip(st.name)));
            RenderTargetIdentifier depthSrc = default;
            if (own)
            {
                try { depthSrc = r.cameraDepthTarget; } catch { own = false; }
                if (own && depthSrc == new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget)) own = false;
            }
            if (own && ms && pMs < 0) { LastError = "старый nepfx.bundle, запустите build_nepfx.bat"; own = false; }
            if (own)
            {
                cmd.GetTemporaryRT(idDepth, new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 0), FilterMode.Point);
                if (ms) { cmd.SetGlobalTexture(idDepthSrcMS, depthSrc); Draw(cmd, rtDepth, pMs); }
                else { cmd.SetGlobalTexture(idDepthSrc, depthSrc); Draw(cmd, rtDepth, 6); }
                cmd.SetGlobalTexture(idDepthTex, rtDepth);
                if (menu) DepthInfo = ms ? $"своя копия, MSAA {msaa}x" : "своя копия, без MSAA";
            }
            else
            {
                cmd.SetGlobalTexture(idDepthTex, rtCamDepth);
                DepthInfo = "текстура игры _CameraDepthTexture";
            }

            // цвет камеры при MSAA Unity сам сводит в обычную текстуру при чтении, поэтому хватает Blit
            cmd.Blit(color, rtSrc);

            // маска персонажей: их поверхность рисуется отдельно, чтобы ослабить на них затенение и шум
            bool mask = false;
            var chars = Chars.Renderers;
            if (pMask >= 0 && chars.Count > 0 && S.FxCharStrength.Value < 0.99f)
            {
                cmd.GetTemporaryRT(idMask, new RenderTextureDescriptor(w, h, RenderTextureFormat.RFloat, 24), FilterMode.Point);
                cmd.SetRenderTarget(rtMask);
                cmd.ClearRenderTarget(true, true, Color.clear);
                foreach (var (cr, subs) in chars)
                {
                    if (cr == null || !cr.enabled || !cr.isVisible) continue;
                    for (int sm = 0; sm < subs; sm++) cmd.DrawRenderer(cr, mat, sm, pMask);
                }
                cmd.SetGlobalTexture(idCharMask, rtMask);
                mask = true;
            }
            cmd.SetGlobalTexture(idSource, rtSrc);

            cmd.SetGlobalVector(idParams4, new Vector4(S.FxSplit.Value ? 1 : 0, S.FxCharStrength.Value, mask ? 1 : 0, Overhead ? S.FxIndoorLength.Value : 0f));
            bool blobsOn = Overhead && S.FxBlobStrength.Value > 0.001f;
            // пятна под персонажами считаются один раз за кадр, а не для каждой камеры
            if (!blobsOn) Blobs.Count = 0;
            else if (Time.frameCount != blobsFrame) { blobsFrame = Time.frameCount; Blobs.Update(); }
            cmd.SetGlobalVectorArray(idBlobs, Blobs.Data);
            cmd.SetGlobalVector(idBlobInfo, new Vector4(Blobs.Count, S.FxBlobStrength.Value, 0, 0));
            Draw(cmd, rtTrace, 0);                                                     // трассировка
            cmd.SetGlobalTexture(idTraceTex, rtTrace);
            cmd.SetGlobalTexture(idHistory, new RenderTargetIdentifier(hPrev));
            Draw(cmd, new RenderTargetIdentifier(hCur), 1);                            // накопление
            cmd.SetGlobalTexture(idBlurSrc, new RenderTargetIdentifier(hCur));
            Draw(cmd, rtBlurA, 2);                                                     // размытие
            cmd.SetGlobalTexture(idBlurSrc, rtBlurA);
            Draw(cmd, rtBlurB, 3);
            cmd.SetGlobalTexture(idGI, rtBlurB);

            // отражения по экрану
            bool ssr = S.FxSsr.Value && pSsr >= 0;
            SsrInfo = S.FxSsr.Value && pSsr < 0 ? "нужен новый nepfx.bundle, запустите build_nepfx.bat" : "";
            cmd.SetGlobalVector(idSsr, new Vector4(ssr ? 1 : 0, S.FxSsrDistance.Value, S.FxSsrMode.Value, S.FxSsrIntensity.Value));
            if (ssr)
            {
                // маска блестящих объектов: глубина + сила блеска. Список готовит Gloss.Update в обычном Update.
                cmd.GetTemporaryRT(idGloss, new RenderTextureDescriptor(w, h, RenderTextureFormat.RGFloat, 24), FilterMode.Point);
                cmd.SetRenderTarget(rtGloss);
                cmd.ClearRenderTarget(true, true, Color.clear);
                if (pMask >= 0)
                {
                    float lastG = -1;
                    foreach (var (gr, sub, g) in Gloss.Items)
                    {
                        if (gr == null || !gr.isVisible) continue;
                        if (g != lastG) { cmd.SetGlobalFloat(idGlossVal, g); lastG = g; }
                        cmd.DrawRenderer(gr, mat, sub, pMask);
                    }
                }
                cmd.SetGlobalTexture(idGlossMask, rtGloss);
                cmd.GetTemporaryRT(idSsrA, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
                Draw(cmd, rtSsrA, pSsr);
                if (S.FxSsrBlur.Value)
                {
                    cmd.GetTemporaryRT(idSsrB, new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0), FilterMode.Bilinear);
                    cmd.SetGlobalTexture(idBlurSrc, rtSsrA);
                    Draw(cmd, rtSsrB, 2);
                    cmd.SetGlobalTexture(idBlurSrc, rtSsrB);
                    Draw(cmd, rtSsrA, 3);
                }
                cmd.SetGlobalTexture(idSSRTex, rtSsrA);
            }
            Draw(cmd, color, 4);                                                       // композит

            if (own) cmd.ReleaseTemporaryRT(idDepth);
            if (mask) cmd.ReleaseTemporaryRT(idMask);
            if (ssr) { cmd.ReleaseTemporaryRT(idGloss); cmd.ReleaseTemporaryRT(idSsrA); if (S.FxSsrBlur.Value) cmd.ReleaseTemporaryRT(idSsrB); }
            cmd.ReleaseTemporaryRT(idSrc); cmd.ReleaseTemporaryRT(idTrace); cmd.ReleaseTemporaryRT(idBlurA); cmd.ReleaseTemporaryRT(idBlurB);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // история: текущий кадр станет «прошлым»
            var t = st.histA; st.histA = st.histB; st.histB = t;
            st.prevVP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            st.hasPrev = true;
        }
    }
}
