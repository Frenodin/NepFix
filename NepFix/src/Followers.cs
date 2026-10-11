using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NepFix
{
    /// Спутники отряда на карте (MapUnitTypeFriendComponent).
    /// Игра ведёт их к точке строя прямо, без обхода препятствий: они упираются в камни и деревья, MapMovePoint
    /// через секунду сдаётся, спутник отстаёт и AiLostTeleport переносит его на место игрока со вспышкой.
    /// 1. Поиск пути: у MapMovePoint есть поиск маршрута по данным карты (им пользуются враги), спутникам игра его не включает.
    ///    RouteSearchMoveSet(true, 6, 2): маршрут строится, если до цели больше 6 м и прямой путь непроходим.
    /// 2. Тихий перенос: вместо вспышки на игроке спутник ставится на землю под камерой, вне кадра, и догоняет сам.
    internal static class Followers
    {
        static Settings S => Plugin.S;
        static float scanT = -100;
        static readonly List<MapUnitTypeFriendComponent> friends = new();
        static readonly HashSet<IntPtr> routeSet = new();
        public static int Count, Rerouted, QuietTeleports, Fallbacks;
        static int scene = int.MinValue;

        public static void Tick()
        {
            float now = Time.unscaledTime;
            int sc = Scan.Scene;
            if (sc != scene) { scene = sc; friends.Clear(); routeSet.Clear(); scanT = -100; }
            if (now - scanT < 1f) return;
            scanT = now;
            bool route = S.FollowerPathfinding.Value;
            friends.Clear();
            var arr = Scan.All<MapUnitTypeFriendComponent>();
            if (arr != null)
                for (int i = 0; i < arr.Length; i++)
                {
                    var f = arr[i]?.TryCast<MapUnitTypeFriendComponent>();
                    if (f != null) friends.Add(f);
                }
            Count = friends.Count;
            foreach (var f in friends)
            {
                try
                {
                    var mp = f.move_point_;
                    if (mp != null)
                    {
                        IntPtr k = mp.Pointer;
                        if (route && !mp.route_search_use_) { mp.RouteSearchMoveSet(true, 6f, 2f); routeSet.Add(k); Rerouted++; if (Rerouted <= 8) Plugin.L.LogInfo($"Спутники: поиск пути включён для {f.name}"); }
                        else if (!route && routeSet.Remove(k) && mp.route_search_use_) mp.RouteSearchMoveSet(false, 6f, 2f);
                    }
                }
                catch { }
            }
        }

        // ---- тихий перенос ----
        public static bool InLostTeleport;

        /// Точка на земле под камерой: она вне кадра, а камера игры держится над открытым местом.
        public static bool QuietPoint(Vector3 player, out Vector3 p)
        {
            p = player;
            try
            {
                var cam = Camera.main;
                if (cam == null) return false;
                Vector3 c = cam.transform.position;
                Vector3 flat = new Vector3(c.x - player.x, 0, c.z - player.z);
                float d = flat.magnitude;
                if (d < 1.5f || d > 15f) return false;
                // не дальше 6 м от игрока по горизонтали
                Vector3 dir = flat / d;
                float dist = Math.Min(d, 6f);
                Vector3 probe = new Vector3(player.x, player.y, player.z) + dir * dist;
                if (!Physics.Raycast(probe + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Math.Abs(hit.point.y - player.y) > 1.5f) return false;
                // между игроком и точкой нет стены
                Vector3 a = player + Vector3.up * 1f, b = hit.point + Vector3.up * 1f;
                if (Physics.Linecast(a, b, out RaycastHit wall, ~0, QueryTriggerInteraction.Ignore) && wall.distance < (b - a).magnitude - 0.3f) return false;
                p = hit.point;
                return true;
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(MapUnitTypeFriendComponent), "AiLostTeleport")]
    internal static class PatchFriendLostTeleport
    {
        static void Prefix() { Followers.InLostTeleport = Plugin.S.QuietFollowerTeleport.Value; }
        static void Postfix(MapUnitTypeFriendComponent __instance, bool __result)
        {
            bool quiet = Followers.InLostTeleport;
            Followers.InLostTeleport = false;
            if (!quiet || !__result || __instance == null) return;
            try
            {
                var ub = __instance.unit_base_;
                var mp = __instance.move_point_;
                if (ub == null || mp == null) return;
                Vector3 player = mp.point_goal_; // игра только что выставила цель переноса = позиция игрока
                if (!Followers.QuietPoint(player, out Vector3 p)) { Followers.Fallbacks++; if (Followers.Fallbacks <= 30) Plugin.L.LogInfo($"Спутники: места под камерой нет, перенос к игроку без вспышки ({__instance.name})"); return; }
                ub.MoveTeleportPosition(p);
                mp.point_goal_ = p;
                Followers.QuietTeleports++;
                if (Followers.QuietTeleports <= 30) Plugin.L.LogInfo($"Спутники: тихий перенос {__instance.name} на {Vector3.Distance(player, p):0.0} м от игрока");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(MapEffectManager), nameof(MapEffectManager.CreateSystemEffectPlay))]
    internal static class PatchFriendTeleportEffect
    {
        static bool Prefix() => !Followers.InLostTeleport;
    }
}
