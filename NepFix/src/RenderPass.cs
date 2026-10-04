using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NepFix
{
    /// Собственный проход рендера внутри конвейера URP игры (класс внедряется в IL2CPP).
    public class NepRenderPass : ScriptableRenderPass
    {
        public NepRenderPass(IntPtr ptr) : base(ptr) { }
        public NepRenderPass() : base(ClassInjector.DerivedConstructorPointer<NepRenderPass>()) { ClassInjector.DerivedConstructorBody(this); }

        internal static ScriptableRenderer CurrentRenderer;
        static bool loggedExec, errExec, errFx;

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            try
            {
                var r = CurrentRenderer;
                if (r == null) return;
                if (!loggedExec)
                {
                    loggedExec = true;
                    Plugin.L.LogInfo("NepRenderPass.Execute работает. Текстуры: depth=" + Tex("_CameraDepthTexture") + " normals=" + Tex("_CameraNormalsTexture") + " motion=" + Tex("_MotionVectorTexture") + " opaque=" + Tex("_CameraOpaqueTexture"));
                }
                if (Plugin.S.FxEnabled.Value && NepFX.Ready)
                {
                    Camera cam = null; bool overlay = false; int dw = 0, dh = 0, msaa = 1;
                    try
                    {
                        var cd = renderingData.cameraData;
                        cam = cd.camera;
                        // остальные поля CameraData через interop читаются неверно, берём всё из камеры и настроек URP
                        if (cam != null)
                        {
                            var tt = cam.targetTexture;
                            if (tt != null) msaa = Math.Max(1, tt.antiAliasing);
                            else if (cam.allowMSAA) msaa = Math.Max(1, Gfx.Urp()?.msaaSampleCount ?? 1);
                        }
                    }
                    catch (Exception e) { NepFX.LastError = "cameraData: " + e.Message; }
                    try { Prof.Run("nepfx", () => NepFX.Execute(context, r, this, cam, overlay, dw, dh, msaa)); }
                    catch (Exception e) { NepFX.LastError = e.GetType().Name + ": " + e.Message; if (!errFx) { errFx = true; Plugin.L.LogWarning("NepFX.Execute: " + e); } }
                    return;
                }
                if (!Plugin.S.PipelineTest.Value) return;
                var cmd = new CommandBuffer();
                cmd.name = "NepFix.Test";
                cmd.SetRenderTarget(r.cameraColorTarget);
                cmd.SetViewport(new Rect(0, 0, 160, 160));
                cmd.ClearRenderTarget(false, true, new Color(0f, 1f, 1f, 1f));
                context.ExecuteCommandBuffer(cmd);
                cmd.Release();
            }
            catch (Exception e) { if (!errExec) { errExec = true; Plugin.L.LogWarning("NepRenderPass.Execute: " + e); } }
        }

        static string Tex(string n)
        {
            try { var t = Shader.GetGlobalTexture(n); return t == null ? "нет" : $"{t.width}x{t.height}"; } catch { return "?"; }
        }
    }

    /// Добавление нашего прохода в очередь каждого рендерера URP.
    [HarmonyPatch(typeof(ScriptableRenderer), "AddRenderPasses")]
    internal static class PatchAddRenderPasses
    {
        static NepRenderPass pass;
        static bool failed, logged;

        static void Postfix(ScriptableRenderer __instance)
        {
            if (failed || !(Plugin.S.PipelineTest.Value || (Plugin.S.FxEnabled.Value && NepFX.Ready))) return;
            try
            {
                if (pass == null)
                {
                    if (!ClassInjector.IsTypeRegisteredInIl2Cpp<NepRenderPass>()) ClassInjector.RegisterTypeInIl2Cpp<NepRenderPass>();
                    pass = new NepRenderPass();
                    pass.ConfigureInput(ScriptableRenderPassInput.Depth);
                    Plugin.L.LogInfo("NepRenderPass создан");
                }
                pass.renderPassEvent = Plugin.S.FxEvent.Value switch
                {
                    1 => RenderPassEvent.AfterRenderingTransparents,
                    2 => RenderPassEvent.AfterRenderingSkybox,
                    3 => RenderPassEvent.AfterRenderingPostProcessing,
                    _ => RenderPassEvent.BeforeRenderingPostProcessing
                };
                NepRenderPass.CurrentRenderer = __instance;
                __instance.EnqueuePass(pass);
                if (!logged) { logged = true; Plugin.L.LogInfo("NepRenderPass поставлен в очередь рендерера"); }
            }
            catch (Exception e) { failed = true; Plugin.L.LogWarning("NepRenderPass: " + e); }
        }
    }
}
