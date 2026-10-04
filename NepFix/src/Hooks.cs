using System;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Runtime;

namespace NepFix
{
    /// Нативный хук на Application.targetFrameRate: игра выставляет его в обход своих методов
    /// (инлайн-вызовы icall), из-за чего на доли секунды возвращался лимит 60 и менялась скорость игры.
    internal static class FrameRateHook
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetIntD(int v);
        static SetIntD detourDel, original;
        static INativeDetour detour;
        public static bool Active => detour != null;

        public static void Install()
        {
            try
            {
                IntPtr p = IL2CPP.il2cpp_resolve_icall("UnityEngine.Application::set_targetFrameRate");
                if (p == IntPtr.Zero) { Plugin.L.LogWarning("icall set_targetFrameRate не найден"); return; }
                detourDel = Detour;
                detour = INativeDetour.CreateAndApply(p, detourDel, out original);
                Plugin.L.LogInfo("Хук targetFrameRate установлен");
            }
            catch (Exception e) { Plugin.L.LogWarning("Хук targetFrameRate не установлен: " + e.Message); }
        }

        static int lastLogged = int.MinValue;
        static void Detour(int v)
        {
            try
            {
                if (Plugin.S.FpsUnlock.Value && Gfx.TargetFps > 0)
                {
                    if (v != Gfx.TargetFps && v != lastLogged && Plugin.S.VerboseLog.Value)
                    { lastLogged = v; Plugin.L.LogInfo($"Игра выставила targetFrameRate={v} -> {Gfx.TargetFps}"); }
                    v = Gfx.TargetFps;
                }
            }
            catch { }
            original(v);
        }

        /// Вызов оригинала напрямую (для применения нашего значения).
        public static void Set(int v) { if (original != null) original(v); else UnityEngine.Application.targetFrameRate = v; }
    }
}
