using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NepFix
{
    /// Враги на карте застревали и шли на месте при высоком FPS.
    /// Игра считает скорость юнита как сглаженное расстояние за один кадр (MapUnitBaseComponent.move_speed_)
    /// и сравнивает его с порогом move_use_distance_: у врагов 0,03 м за кадр, у остальных 0,01.
    /// Если юнит секунду «движется медленнее порога», MapMovePoint считает, что он упёрся, и сдаётся.
    /// Порог рассчитан на 60 кадров: при 165 кадрах за кадр проходится втрое меньше, и медленные враги
    /// на патруле постоянно «упирались». Пересчитываем порог под реальное время кадра, в метрах в секунду он прежний.
    internal static class UnitStuck
    {
        static readonly Dictionary<IntPtr, (float orig, float set)> seen = new();

        /// Порог в метрах в секунду, как задумано игрой: порог за кадр при её стандартной частоте.
        public static float ThresholdSpeed(MapUnitBaseComponent u)
        {
            float orig = seen.TryGetValue(u.Pointer, out var s) ? s.orig : u.move_use_distance_;
            int std = 60; try { std = GameTime.StandardFrameRate; } catch { }
            if (std <= 0) std = 60;
            return orig * std;
        }

        // Скорость по реальному перемещению за последние ~0,3 с: не зависит ни от FPS, ни от того,
        // что физика двигает юнита не в каждом кадре.
        class Track { public Vector3 p0; public float t0; public Vector3 p1; public float t1; public float speed = -1; public bool gaveUp; }
        static readonly Dictionary<IntPtr, Track> tracks = new();
        public static int GiveUps, Rescued;
        static int logged;

        public static void MoveBefore(MapMovePoint mp, MapUnitBaseComponent u)
        {
            if (!Plugin.S.UnitStuckFix.Value || u == null) return;
            try
            {
                float now = Time.time;
                var p = u.GetPosition(); p.y = 0;
                if (tracks.Count > 2000) tracks.Clear();
                if (!tracks.TryGetValue(u.Pointer, out var t)) { t = new Track { p0 = p, t0 = now, p1 = p, t1 = now }; tracks[u.Pointer] = t; return; }
                if (now - t1Gap(t) > 1f) { t.p0 = p; t.t0 = now; t.speed = -1; }
                t.p1 = p; t.t1 = now;
                float win = t.t1 - t.t0;
                if (win >= 0.3f)
                {
                    t.speed = (t.p1 - t.p0).magnitude / win;
                    t.p0 = p; t.t0 = now;
                }
                // игрок в паузе или сценке: время стоит, решать не по чему
                if (t.speed < 0) { mp.time_out_now_ = 1f; return; }
                if (t.speed >= ThresholdSpeed(u) * 0.7f && mp.time_out_now_ < 1f) { mp.time_out_now_ = 1f; Rescued++; }
            }
            catch { }
        }
        static float t1Gap(Track t) => t.t1;

        public static void MoveAfter(MapMovePoint mp, MapUnitBaseComponent u)
        {
            if (u == null) return;
            try
            {
                bool g = mp.is_give_up_;
                if (!tracks.TryGetValue(u.Pointer, out var t)) return;
                if (g && !t.gaveUp)
                {
                    GiveUps++;
                    if (Plugin.S.VerboseLog.Value && logged < 40)
                    {
                        logged++;
                        Plugin.L.LogInfo($"Юнит '{u.name}' бросил путь: скорость по факту {Math.Max(0, t.speed):0.00} м/с, порог {ThresholdSpeed(u):0.00} м/с, заданная {mp.speed_:0.00}, до цели {mp.goal_distance_:0.0} м, FPS {1f / Math.Max(1e-4f, Time.unscaledDeltaTime):0}");
                    }
                }
                t.gaveUp = g;
            }
            catch { }
        }
        public static int Units, Fixed;
        static int frame = -1;

        public static void Before(MapUnitBaseComponent u)
        {
            if (!Plugin.S.UnitStuckFix.Value) return;
            try
            {
                if (Time.frameCount != frame)
                {
                    frame = Time.frameCount;
                    if (seen.Count > 2000) seen.Clear();
                    Units = seen.Count;
                }
                float cur = u.move_use_distance_;
                IntPtr k = u.Pointer;
                float orig;
                if (seen.TryGetValue(k, out var s) && Math.Abs(cur - s.set) < 1e-7f) orig = s.orig;
                else orig = cur; // новый юнит или игра сама поменяла порог
                float dt = Time.deltaTime;
                int std = 60; try { std = GameTime.StandardFrameRate; } catch { } if (std <= 0) std = 60;
                float f = dt > 0f ? Math.Clamp(dt * std, 0.05f, 1f) : 1f;
                float want = orig * f;
                if (Math.Abs(cur - want) > 1e-7f) { u.move_use_distance_ = want; Fixed++; }
                seen[k] = (orig, want);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(MapMovePoint), "DelegateMove")]
    internal static class PatchMovePointStuck
    {
        static void Prefix(MapMovePoint __instance, MapUnitBaseComponent unit_base) { UnitStuck.MoveBefore(__instance, unit_base); }
        static void Postfix(MapMovePoint __instance, MapUnitBaseComponent unit_base) { UnitStuck.MoveAfter(__instance, unit_base); }
    }

    [HarmonyPatch(typeof(MapUnitBaseComponent), "Update")]
    internal static class PatchUnitStuck
    {
        static void Prefix(MapUnitBaseComponent __instance) { UnitStuck.Before(__instance); }
    }
}
