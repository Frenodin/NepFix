using System;
using MagicaCloth;

namespace NepFix
{
    /// Физика волос и одежды (MagicaCloth). По умолчанию она считается 90 раз в секунду:
    /// при 165 FPS часть кадров получает шаг симуляции, часть нет, и волосы дрожат.
    internal static class ClothFix
    {
        public static string Info = "";
        static bool saved; static UpdateTimeManager.UpdateCount gameCount; static UpdateTimeManager.UpdateMode gameMode;

        public static void Apply()
        {
            try
            {
                if (!MagicaPhysicsManager.IsInstance()) { Info = "физика волос ещё не запущена"; return; }
                var m = MagicaPhysicsManager.Instance;
                if (m == null) return;
                if (!saved) { gameCount = m.UpdatePerSeccond; gameMode = m.UpdateMode; saved = true; Plugin.L.LogInfo($"MagicaCloth: по игре {(int)gameCount} Гц, режим {gameMode}"); }
                int mode = Plugin.S.ClothRate.Value;
                var wantCount = gameCount; var wantMode = gameMode;
                if (mode == 1)
                {
                    int fps = Math.Max(60, Gfx.TargetFps);
                    // частота симуляции не ниже частоты кадров, чтобы каждый кадр получал шаг
                    wantCount = fps <= 60 ? UpdateTimeManager.UpdateCount._60 : fps <= 90 ? UpdateTimeManager.UpdateCount._90_Default
                              : fps <= 120 ? UpdateTimeManager.UpdateCount._120 : fps <= 150 ? UpdateTimeManager.UpdateCount._150 : UpdateTimeManager.UpdateCount._180;
                    if (wantMode == UpdateTimeManager.UpdateMode.OncePerFrame) wantMode = UpdateTimeManager.UpdateMode.UnscaledTime;
                }
                else if (mode == 2) wantMode = UpdateTimeManager.UpdateMode.OncePerFrame;
                if (m.UpdatePerSeccond != wantCount) m.UpdatePerSeccond = wantCount;
                if (m.UpdateMode != wantMode) m.UpdateMode = wantMode;
                if (Plugin.MenuOpen) Info = $"Физика волос и одежды: по игре {(int)gameCount} Гц, {gameMode}; сейчас {(int)m.UpdatePerSeccond} Гц, {m.UpdateMode}";
            }
            catch (Exception e) { Info = "Физика волос: " + e.Message; }
        }
    }
}
