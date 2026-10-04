using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Настройки мотоцикла: управляемость, скорость, мягкость ударов о стену.
    /// Меняются данные игры (MapManagerDataMoveUnitBikePad), исходные значения запоминаются и возвращаются.
    internal static class Bike
    {
        static Settings S => Plugin.S;
        static MapManagerDataMoveUnitBikePad data;
        static float oHandling, oQuick, oSpeedMax, oWallDec, oSpeedWall;
        static float next;
        public static string Info = "";
        public static bool Riding;
        static float gameFdt = -1;

        /// На высокой скорости мотоцикл за один шаг физики проезжает больше полуметра и проскакивает сквозь тонкие
        /// стены и заборы. Пока едем, учащаем шаги физики ровно настолько, чтобы за шаг было не больше 0,4 м.
        static void PhysicsStep(MapMoveBikePad bp, MapManagerDataMoveUnitBikePad d)
        {
            if (S.SyncPhysicsToFps.Value) { gameFdt = -1; return; }
            if (bp == null || d == null)
            {
                if (gameFdt > 0) { Time.fixedDeltaTime = gameFdt; gameFdt = -1; }
                return;
            }
            if (gameFdt < 0) gameFdt = Time.fixedDeltaTime;
            float vmax = Math.Max(Math.Abs(bp.speed_), d.speed_maximum_ * 1.3f);
            float want = Math.Clamp(0.4f / Math.Max(vmax, 1f), 1f / 120f, gameFdt);
            if (Math.Abs(Time.fixedDeltaTime - want) > 1e-5f) Time.fixedDeltaTime = want;
        }

        public static void Update()
        {
            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 0.5f;
            try
            {
                MapMoveBikePad bp = null;
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MapUnitBaseComponent>());
                if (arr != null) foreach (var o in arr)
                {
                    var u = o.TryCast<MapUnitBaseComponent>(); if (u == null) continue;
                    try { var b = u.map_move_bike_pad_; if (b != null) { bp = b; break; } } catch { }
                }
                Riding = bp != null;
                if (bp == null) PhysicsStep(null, null);
                if (bp == null) { Info = "Мотоцикл сейчас не используется."; next = now + 2f; return; }
                var d = bp.data_;
                if (d == null) return;
                if (data == null || data.Pointer != d.Pointer)
                {
                    data = d;
                    oHandling = d.handling_; oQuick = d.handling_quick_; oSpeedMax = d.speed_maximum_; oWallDec = d.wall_decelerate_; oSpeedWall = d.speed_wall_;
                }
                float h = S.BikeHandling.Value, sp = S.BikeSpeed.Value, wall = S.BikeWallSoft.Value;
                Set(v => d.handling_ = v, d.handling_, oHandling * h);
                Set(v => d.handling_quick_ = v, d.handling_quick_, oQuick * h);
                Set(v => d.speed_maximum_ = v, d.speed_maximum_, oSpeedMax * sp);
                // мягкость: 0 как в игре, 1 скорость у стены почти не теряется
                Set(v => d.wall_decelerate_ = v, d.wall_decelerate_, oWallDec * (1f - 0.8f * wall));
                Set(v => d.speed_wall_ = v, d.speed_wall_, Math.Max(oSpeedWall, oSpeedWall + (oSpeedMax * sp - oSpeedWall) * 0.6f * wall));
                PhysicsStep(bp, d);
                Info = $"Шаг физики {1f / Time.fixedDeltaTime:0} в секунду. Сейчас поворот {d.handling_:0}°/с, в игре {oHandling:0}. Скорость до {d.speed_maximum_:0.#}, в игре {oSpeedMax:0.#}. Торможение о стену {d.wall_decelerate_:0}, в игре {oWallDec:0}.";
            }
            catch (Exception e) { Info = "Мотоцикл: " + e.Message; }
        }

        static void Set(Action<float> set, float cur, float want) { if (Math.Abs(cur - want) > 1e-4f) set(want); }

        // ---- удары о стену и столкновения: не даём скорости обнулиться мгновенно ----
        internal static float Keep => Math.Clamp(S.BikeWallSoft.Value, 0f, 1f) * 0.7f;
    }

    [HarmonyPatch(typeof(MapMoveBikePad), nameof(MapMoveBikePad.Crash))]
    internal static class PatchBikeCrashSoft
    {
        static void Prefix(MapMoveBikePad __instance, out float __state) { __state = __instance.speed_; }
        static void Postfix(MapMoveBikePad __instance, float __state)
        {
            float k = Bike.Keep;
            if (k > 0.001f && __instance.speed_ < __state * k) __instance.speed_ = __state * k;
        }
    }

    [HarmonyPatch(typeof(MapMoveBikePad), "WallDecelerateSpeed")]
    internal static class PatchBikeWallSoft
    {
        static void Prefix(MapMoveBikePad __instance, out float __state) { __state = __instance.speed_; }
        static void Postfix(MapMoveBikePad __instance, float __state)
        {
            float k = Bike.Keep;
            if (k > 0.001f && __instance.speed_ < __state * k) __instance.speed_ = __state * k;
        }
    }
}
