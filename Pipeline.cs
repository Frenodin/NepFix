using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NepFix
{
    /// Точка встраивания своих эффектов в конвейер игры (после рендера 3D-камеры, до интерфейса).
    internal static class Pipeline
    {
        static Settings S => Plugin.S;
        static bool hooked;
        static readonly HashSet<IntPtr> logged = new();
        static Il2CppSystem.Action<ScriptableRenderContext, Camera> del, delBegin;
        public static string Info = "";

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            try
            {
                del = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(new Action<ScriptableRenderContext, Camera>(OnEndCamera));
                RenderPipelineManager.add_endCameraRendering(del);
                delBegin = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(new Action<ScriptableRenderContext, Camera>((c, cam) => NepFX.CurrentCamera = cam));
                RenderPipelineManager.add_beginCameraRendering(delBegin);
                Plugin.L.LogInfo("Pipeline: подписка на endCameraRendering");
            }
            catch (Exception e) { Plugin.L.LogWarning("Pipeline hook: " + e); }
        }

        static string Tex(string n)
        {
            try { var t = Shader.GetGlobalTexture(n); return t == null ? "нет" : $"{t.name} {t.width}x{t.height}"; } catch (Exception e) { return "err " + e.Message; }
        }

        static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            try
            {
                if (cam == null) return;
                if (logged.Add(cam.Pointer) && S.VerboseLog.Value)
                {
                    var tt = cam.targetTexture;
                    string info = $"Pipeline: камера '{cam.name}' type={cam.cameraType} ortho={cam.orthographic} depth={cam.depth} target=" +
                        (tt == null ? "экран" : $"{tt.name} {tt.width}x{tt.height} {tt.format} aa={tt.antiAliasing}") +
                        $" | _CameraDepthTexture={Tex("_CameraDepthTexture")} _CameraNormalsTexture={Tex("_CameraNormalsTexture")} _CameraOpaqueTexture={Tex("_CameraOpaqueTexture")} _MotionVectorTexture={Tex("_MotionVectorTexture")}";
                    Plugin.L.LogInfo(info);
                    Info += info + "\n";
                }
                if (!S.PipelineTest.Value || cam.orthographic || cam.cameraType != CameraType.Game) return;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null && data.renderType != CameraRenderType.Base) return;

                // Тест: пурпурный квадрат в углу 3D-картинки. Если интерфейс рисуется ПОВЕРХ квадрата — точка встраивания верная.
                var cmd = new CommandBuffer();
                cmd.name = "NepFix.PipelineTest";
                var tgt = cam.targetTexture != null ? new RenderTargetIdentifier(cam.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                cmd.SetRenderTarget(tgt);
                cmd.SetViewport(new Rect(0, 0, 160, 160));
                cmd.ClearRenderTarget(false, true, new Color(1f, 0f, 1f, 1f));
                ctx.ExecuteCommandBuffer(cmd);
                ctx.Submit();
                cmd.Release();
            }
            catch (Exception e)
            {
                if (!errLogged) { errLogged = true; Plugin.L.LogWarning("Pipeline: " + e); }
            }
        }
        static bool errLogged;
    }
}
