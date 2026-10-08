using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NepFix
{
    /// Реже фразы героинь на карте вне боя: прыжки, удары, «пойдёмте», «чего стоим».
    internal static class Voices
    {
        static float lastTime = -999f;
        static int lastFrame = -1;
        public static int Played, Skipped;
        public static string Last = "";
        static readonly Dictionary<int, int> seen = new();

        // категории болтовни на карте
        static bool IsBark(int c) => c == 502 || c == 503 || c == 504 || c == 505 || c == 516;

        public static string Name(int c) => c switch
        {
            502 => "болтовня в пути",
            503 => "нашла механизм",
            504 => "прыжок",
            505 => "удар по врагу на карте",
            516 => "стоим без дела",
            _ => "категория " + c
        };

        /// true, если фразу можно проигрывать.
        public static bool Allow(int cat, string from)
        {
            float gap = Plugin.S.MapVoiceInterval.Value;
            bool bark = IsBark(cat);
            bool ok = true;
            if (bark && gap > 0.01f)
            {
                float now = Time.unscaledTime;
                // вложенный вызов в том же кадре, что и разрешённый, пропускаем дальше
                if (Time.frameCount == lastFrame) ok = true;
                else if (now - lastTime < gap) ok = false;
                else { lastTime = now; lastFrame = Time.frameCount; }
            }
            if (bark) { if (ok) Played++; else Skipped++; }
            Last = Name(cat) + (ok ? "" : ", пропущена");
            seen.TryGetValue(cat, out int n);
            seen[cat] = n + 1;
            if (n < 3 && Plugin.S.VerboseLog.Value)
                Plugin.L.LogInfo($"Голос: {from}, {Name(cat)} ({cat}), {(ok ? "звучит" : "пропущена, пауза не прошла")}");
            return ok;
        }

        public static string Info => $"Фраз звучало {Played}, пропущено {Skipped}. Последняя: {(Last == "" ? "нет" : Last)}.";
    }

    [HarmonyPatch(typeof(RpgUnit), nameof(RpgUnit.PlayVoiceDungeonRandom))]
    internal static class PatchVoiceDungeonRandom
    {
        static bool Prefix(DefDatabase.CharaVoiceCategory category, ref bool __result)
        {
            try { if (!Voices.Allow((int)category, "RpgUnit")) { __result = false; return false; } } catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(RpgUnit), nameof(RpgUnit.PlayVoiceDungeon))]
    internal static class PatchVoiceDungeon
    {
        static bool Prefix(DefDatabase.CharaVoiceCategory category, ref bool __result)
        {
            try { if (!Voices.Allow((int)category, "RpgUnit")) { __result = false; return false; } } catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(MapUnitRuntime), nameof(MapUnitRuntime.VoicePlay))]
    internal static class PatchMapVoicePlay
    {
        static bool Prefix(DefDatabase.CharaVoiceCategory chara_voice_category)
        {
            try { return Voices.Allow((int)chara_voice_category, "карта"); } catch { return true; }
        }
    }
}
