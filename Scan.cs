using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace NepFix
{
    /// Общие средства обхода сцены. Каждый вызов через interop создаёт объекты в куче игры и подталкивает её сборку мусора,
    /// поэтому обходы делаются реже, без сортировки, по бюджету времени и с общими списками для нескольких модулей.
    internal static class Scan
    {
        static bool byTypeBroken;

        static class TypeOf<T> where T : Il2CppObjectBase { public static readonly Il2CppSystem.Type V = Il2CppType.Of<T>(); }

        /// Все активные объекты типа. Без сортировки по идентификатору: FindObjectsOfType сортирует результат, а это лишняя работа.
        public static Il2CppReferenceArray<UnityEngine.Object> All<T>() where T : Il2CppObjectBase
        {
            if (!byTypeBroken)
            {
                try { return UnityEngine.Object.FindObjectsByType(TypeOf<T>.V, FindObjectsSortMode.None); }
                catch (Exception e) { byTypeBroken = true; Plugin.L.LogWarning("FindObjectsByType недоступен, обычный поиск: " + e.Message); }
            }
            return UnityEngine.Object.FindObjectsOfType(TypeOf<T>.V);
        }

        /// Указатель элемента массива без создания обёртки (заголовок массива IL2CPP: 4 указателя).
        public static IntPtr Raw(Il2CppObjectBase a, int i) => Marshal.ReadIntPtr(a.Pointer, IntPtr.Size * (4 + i));

        public static long Now => Stopwatch.GetTimestamp();
        public static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

        // ---- активная сцена, один раз за кадр ----
        static int sceneFrame = -1, scene;
        public static float SceneChangedAt = -100;
        public static int Scene
        {
            get
            {
                int f = Time.frameCount;
                if (f != sceneFrame)
                {
                    sceneFrame = f;
                    int s = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                    if (s != scene) { scene = s; SceneChangedAt = Time.unscaledTime; }
                }
                return scene;
            }
        }

        // ---- общие списки: юниты карты и контроллеры персонажей, обновляются не чаще раза в секунду ----
        static readonly List<MapUnitBaseComponent> mapUnits = new();
        static readonly List<CharacterController> controllers = new();
        static readonly Dictionary<IntPtr, MapUnitBaseComponent> mapUnitByPtr = new();
        static readonly Dictionary<IntPtr, CharacterController> ccByPtr = new();
        static float mapUnitsT = -100, ccT = -100; static int mapUnitsScene, ccScene;
        public static int MapUnitsVersion, ControllersVersion;

        public static List<MapUnitBaseComponent> MapUnits(float maxAge = 1f)
        {
            float now = Time.unscaledTime; int sc = Scene;
            if (now - mapUnitsT < maxAge && sc == mapUnitsScene) return mapUnits;
            mapUnitsT = now; mapUnitsScene = sc;
            Refresh(mapUnits, mapUnitByPtr, ref MapUnitsVersion);
            return mapUnits;
        }

        public static List<CharacterController> Controllers(float maxAge = 1f)
        {
            float now = Time.unscaledTime; int sc = Scene;
            if (now - ccT < maxAge && sc == ccScene) return controllers;
            ccT = now; ccScene = sc;
            Refresh(controllers, ccByPtr, ref ControllersVersion);
            return controllers;
        }

        /// Юнит, о котором игра сообщила сама (например, из патча движения): попадает в список сразу, без ожидания обхода.
        public static void AddMapUnit(MapUnitBaseComponent u)
        {
            if (u == null || mapUnitByPtr.ContainsKey(u.Pointer)) return;
            mapUnitByPtr[u.Pointer] = u; mapUnits.Add(u); MapUnitsVersion++;
        }

        static readonly HashSet<IntPtr> seenTmp = new();
        static void Refresh<T>(List<T> list, Dictionary<IntPtr, T> byPtr, ref int version) where T : UnityEngine.Object
        {
            try
            {
                var arr = All<T>();
                if (arr == null) return;
                seenTmp.Clear();
                bool changed = false;
                int n = arr.Length;
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = Raw(arr, i);
                    if (p == IntPtr.Zero) continue;
                    seenTmp.Add(p);
                    if (byPtr.ContainsKey(p)) continue;
                    var o = arr[i]?.TryCast<T>();
                    if (o == null) continue;
                    byPtr[p] = o; changed = true;
                }
                if (byPtr.Count != seenTmp.Count)
                {
                    var dead = new List<IntPtr>();
                    foreach (var k in byPtr.Keys) if (!seenTmp.Contains(k)) dead.Add(k);
                    foreach (var k in dead) byPtr.Remove(k);
                    changed |= dead.Count > 0;
                }
                if (changed) { list.Clear(); list.AddRange(byPtr.Values); version++; }
            }
            catch (Exception e) { Plugin.L.LogWarning("Обход сцены: " + e.Message); }
        }

        /// Убирает записи объектов, которые Unity уже уничтожила (обёртка сравнивается с null по правилам Unity).
        public static void PruneDestroyed(Dictionary<IntPtr, UnityEngine.Object> objs, params System.Collections.IDictionary[] others)
        {
            List<IntPtr> dead = null;
            foreach (var kv in objs) if (kv.Value == null) (dead ??= new()).Add(kv.Key);
            if (dead == null) return;
            foreach (var k in dead) { objs.Remove(k); foreach (var d in others) d.Remove(k); }
        }

        /// Удаляет из словаря ключи, которых нет в наборе живых объектов. Раньше словари просто очищались по размеру,
        /// и уже изменённые модом значения принимались за исходные: множители накапливались.
        public static void Prune<V>(Dictionary<IntPtr, V> d, HashSet<IntPtr> alive)
        {
            if (d.Count <= alive.Count) { bool any = false; foreach (var k in d.Keys) if (!alive.Contains(k)) { any = true; break; } if (!any) return; }
            var dead = new List<IntPtr>();
            foreach (var k in d.Keys) if (!alive.Contains(k)) dead.Add(k);
            foreach (var k in dead) d.Remove(k);
        }
    }
}
