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
        class Track { public Vector3 p0; public float t0; public Vector3 p1; public float t1; public float speed = -1; public bool gaveUp; public int kind = -1; public float stopUntil, releasedAt, pushT, gdT, gd0, stT, scT, holdUntil; public int sc; public bool goodValid, goodNextValid; public Vector3 goal0, good, goodNext; }
        public static int FriendStops, NearGoals, ForcedGiveUps, Unwedged;
        static readonly Dictionary<IntPtr, Track> tracks = new();
        public static int GiveUps, Rescued;
        static int logged;

        public static void MoveBefore(MapMovePoint mp, MapUnitBaseComponent u)
        {
            if (!Plugin.S.UnitStuckFix.Value || u == null) return;
            try
            {
                Scan.AddMapUnit(u);
                float now = Time.time;
                var pFull = u.GetPosition(); var p = pFull; p.y = 0;
                if (tracks.Count > 4000) tracks.Clear(); // только скорости за последние доли секунды, их не жалко
                if (!tracks.TryGetValue(u.Pointer, out var t)) { t = new Track { p0 = p, t0 = now, p1 = p, t1 = now }; tracks[u.Pointer] = t; return; }
                if (now - t1Gap(t) > 1f) { t.p0 = p; t.t0 = now; t.speed = -1; }
                t.p1 = p; t.t1 = now;
                float win = t.t1 - t.t0;
                if (win >= 0.3f)
                {
                    t.speed = (t.p1 - t.p0).magnitude / win;
                    t.p0 = p; t.t0 = now;
                    // последнее место, где враг шёл нормально (за прошедшие 0,3 с сдвинулся заметно)
                    if (t.speed > 0.6f) { t.good = t.goodNext; t.goodValid = t.goodNextValid; }
                    t.goodNext = pFull; t.goodNextValid = true;
                }
                // игрок в паузе или сценке: время стоит, решать не по чему
                if (t.speed < 0) { mp.time_out_now_ = 1f; return; }
                if (t.kind < 0) { string n = u.name ?? ""; t.kind = n.Contains("FRIEND") ? 1 : n.Contains("ENEMY") ? 2 : 0; }
                Vector3 g = mp.point_goal_; g.y = 0;
                float dist = (g - p).magnitude;
                // Спутник упёрся (бежит в стену за игроком): игра ставит ему цель каждый кадр, сдаться он не может
                // и бежит в стену бесконечно. Останавливаем его на секунду, потом пробует снова.
                if (t.kind == 1 && Plugin.S.FollowerPathfinding.Value)
                {
                    if (now < t.stopUntil) { mp.is_goal_ = true; return; }
                    bool pushing = mp.speed_ > 1f && t.speed < 0.4f && dist > 1f && dist < 6f;
                    if (!pushing) t.pushT = now;
                    else if (now - t.pushT > 0.5f && now - t.releasedAt > 0.5f)
                    {
                        t.stopUntil = now + 1f; t.releasedAt = now + 1f; t.pushT = now + 1f; FriendStops++;
                        mp.is_goal_ = true; return;
                    }
                }
                // Враг почти дошёл до точки, но топчется или кружит вокруг неё на 0,3–1 м: игра через секунду сдаётся и шлёт его
                // туда же снова, со стороны это тряска на месте. Если за 0,4 с расстояние до точки почти не изменилось, засчитываем приход.
                // Враг упёрся (склон, край ямы, другой враг) и дёргается на месте. Игра считает «движение» по сумме
                // покадровых сдвигов, и дёрганье для неё выглядит как ход: на высоком FPS порог за кадр меньше, она не сдаётся
                // никогда и враг толкается в одну точку бесконечно. Смотрим чистое смещение за 0,3 с: если его почти нет
                // больше секунды, сдаёмся за игру, и враг выбирает новую точку.
                if (t.kind == 2)
                {
                    // после «упёрся» враг полторы секунды стоит спокойно, а не дёргается: игра за это время выберет новую точку
                    if (now < t.holdUntil) { mp.is_goal_ = true; return; }
                    bool stuck = t.speed >= 0f && t.speed < 0.3f && dist > 0.5f && mp.speed_ > 0.5f;
                    if (!stuck) t.stT = 0;
                    else if (t.stT <= 0) t.stT = now;
                    else if (now - t.stT > 0.3f)
                    {
                        t.stT = 0; mp.is_goal_ = true; mp.is_give_up_ = true; ForcedGiveUps++; t.holdUntil = now + 1.5f;
                        // дальше враг пойдёт по маршруту в обход (поиск пути игры), а не напрямую в тот же склон
                        try { if (!mp.route_search_use_) mp.RouteSearchMoveSet(true, 2f, 1f); } catch { }
                        if (now - t.scT > 15f) t.sc = 0;
                        t.sc++; t.scT = now;
                        // третий раз подряд: враг зажат геометрией, возвращаем его туда, где он последний раз нормально шёл
                        if (t.sc >= 3 && t.goodValid && (t.good - pFull).magnitude > 0.5f && (t.good - pFull).magnitude < 8f)
                        {
                            u.MoveTeleportPosition(t.good); t.sc = 0; Unwedged++;
                            if (Plugin.S.VerboseLog.Value && Unwedged <= 20) Plugin.L.LogInfo($"Враг зажат: возвращён на {(t.good - pFull).magnitude:0.0} м назад, где шёл нормально");
                        }
                        if (Plugin.S.VerboseLog.Value && ForcedGiveUps <= 30)
                        {
                            var pos = u.GetPosition();
                            Plugin.L.LogInfo($"Враг упёрся и дёргался на месте: сдаёмся за игру. {pos.x:0.0} {pos.y:0.0} {pos.z:0.0}, до точки {dist:0.0} м, смещение {t.speed:0.00} м/с, заданная {mp.speed_:0.0}");
                        }
                        return;
                    }
                }
                if (t.kind == 2)
                {
                    if (dist < 1.5f && dist > 0.001f)
                    {
                        // только неподвижная точка (патруль); погоня за игроком сюда не попадает
                        if (t.gdT <= 0 || (g - t.goal0).sqrMagnitude > 0.04f) { t.gdT = now; t.gd0 = dist; t.goal0 = g; }
                        else if (now - t.gdT > 0.4f)
                        {
                            if (Math.Abs(t.gd0 - dist) < 0.1f) { mp.is_goal_ = true; NearGoals++; t.gdT = 0; return; }
                            t.gdT = now; t.gd0 = dist;
                        }
                    }
                    else t.gdT = 0;
                }
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
        static int tickScene = int.MinValue;
        static float smoothDt = -1;
        static int std = 60; static float stdT = -100, pruneT;
        static readonly Dictionary<IntPtr, UnityEngine.Object> objs = new();

        /// Раз в кадр из Update мода. Раньше это делал патч Harmony на MapUnitBaseComponent.Update: вызов через
        /// interop на каждого юнита в каждом кадре. Теперь один проход по общему списку юнитов, а запись в игру
        /// только когда множитель заметно изменился.
        public static void Tick()
        {
            if (!Plugin.S.UnitStuckFix.Value) { if (seen.Count > 0) Restore(); return; }
            try
            {
                int sc = Scan.Scene;
                float now = Time.unscaledTime;
                // записи не очищаются при смене сцены: юниты, пережившие её, хранят уже изменённый порог,
                // и он принялся бы за исходный. Убираются только записи уничтоженных юнитов.
                if (sc != tickScene || now - pruneT > 5f) { if (sc != tickScene) tracks.Clear(); tickScene = sc; pruneT = now; Scan.PruneDestroyed(objs, seen); }
                if (now - stdT > 5f) { stdT = now; try { std = GameTime.StandardFrameRate; } catch { } if (std <= 0) std = 60; }
                float dt = Time.deltaTime;
                if (dt <= 0f) return; // пауза: порог не нужен
                smoothDt = smoothDt < 0 ? dt : smoothDt + (dt - smoothDt) * 0.2f;
                float f = Math.Clamp(smoothDt * std, 0.05f, 1f);
                var units = Scan.MapUnits();
                Units = units.Count;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    try
                    {
                        IntPtr k = u.Pointer;
                        float cur = u.move_use_distance_;
                        float orig;
                        if (seen.TryGetValue(k, out var s) && cur == s.set) orig = s.orig;
                        else orig = cur; // новый юнит или игра сама поменяла порог
                        float want = orig * f;
                        // запись только при заметной разнице: мелкие колебания времени кадра порогу не важны
                        if (Math.Abs(cur - want) > want * 0.03f + 1e-7f) { u.move_use_distance_ = want; Fixed++; seen[k] = (orig, want); }
                        else seen[k] = (orig, cur);
                        objs[k] = u;
                    }
                    catch { }
                }
            }
            catch { }
        }

        static void Restore()
        {
            foreach (var u in Scan.MapUnits(5f))
                try { if (seen.TryGetValue(u.Pointer, out var s) && u.move_use_distance_ == s.set) u.move_use_distance_ = s.orig; } catch { }
            seen.Clear(); objs.Clear();
        }
    }

    [HarmonyPatch(typeof(MapMovePoint), "DelegateMove")]
    internal static class PatchMovePointStuck
    {
        static void Prefix(MapMovePoint __instance, MapUnitBaseComponent unit_base) { UnitStuck.MoveBefore(__instance, unit_base); }
        static void Postfix(MapMovePoint __instance, MapUnitBaseComponent unit_base) { UnitStuck.MoveAfter(__instance, unit_base); }
    }

}
