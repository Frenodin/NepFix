using System;
using HarmonyLib;

namespace NepFix
{
    internal static class Patches
    {
        public static void Apply(Harmony h)
        {
            foreach (var t in new[] { typeof(PatchMapReduction), typeof(PatchTargetFrameRate), typeof(PatchGameTimeUpdate), typeof(PatchAddRenderPasses), typeof(PatchWeaponDraw), typeof(PatchUnitAnimBlend), typeof(PatchUnitAnimBlendEnd), typeof(PatchVoiceDungeonRandom), typeof(PatchVoiceDungeon), typeof(PatchMapVoicePlay), typeof(PatchBikeCrash), typeof(PatchBikeCrashSoft), typeof(PatchBikeWallSoft), typeof(PatchMovePointStuck), typeof(PatchBattleDamage), typeof(PatchBattleDamageEnemy), typeof(PatchEnemyAction), typeof(PatchFriendLostTeleport), typeof(PatchFriendTeleportEffect) })
            {
                try { h.CreateClassProcessor(t).Patch(); Plugin.L.LogInfo("Патч применён: " + t.Name); }
                catch (Exception e) { Plugin.L.LogWarning($"Патч {t.Name} не применён: {e.Message}"); }
            }
        }
    }

    // Игра переключает упрощённые модели/пороги LOD на картах — оставляем полную детализацию.
    [HarmonyPatch(typeof(MapReductionComponent), nameof(MapReductionComponent.ExecuteReduction))]
    internal static class PatchMapReduction
    {
        static void Prefix(ref bool is_reduct)
        {
            if (Plugin.S.DisableMapReduction.Value && is_reduct) is_reduct = false;
        }
    }

    // Если игра сама выставит лимит кадров — подменяем на выбранный пользователем.
    [HarmonyPatch(typeof(GameTime), nameof(GameTime.TargetFrameRate), MethodType.Setter)]
    internal static class PatchTargetFrameRate
    {
        static void Prefix(ref int value)
        {
            if (Plugin.S.FpsUnlock.Value && value != Gfx.TargetFps)
            {
                if (Plugin.S.VerboseLog.Value) Plugin.L.LogInfo($"Игра запросила {value} FPS -> {Gfx.TargetFps}");
                value = Gfx.TargetFps;
            }
        }
    }

    // Игра двигает часть объектов «за кадр» с множителем StandardFrameRate/targetFrameRate, а часть — по реальному времени.
    // Когда реальный FPS не совпадает с целевым, камера и персонажи расходятся — отсюда тряска.
    // Делаем множитель зависимым от реального времени кадра.
    [HarmonyPatch(typeof(GameTime), nameof(GameTime.Update))]
    internal static class PatchGameTimeUpdate
    {
        static float smooth = -1f;
        static void Postfix()
        {
            if (!Plugin.S.AdaptiveFrameTiming.Value) return;
            try
            {
                float dt = UnityEngine.Time.unscaledDeltaTime;
                if (dt <= 0f) return;
                dt = System.Math.Clamp(dt, 1f / 500f, 1f / 15f);
                // лёгкое сглаживание, чтобы единичные всплески не давали рывков
                smooth = smooth < 0 ? dt : smooth + (dt - smooth) * 0.5f;
                int std = GameTime.StandardFrameRate;
                if (std <= 0) std = 60;
                GameTime.FrameRateSpeedFactor = smooth * std;
            }
            catch { }
        }
    }
}
