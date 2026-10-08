using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Персонажей игра прижимает к земле в FixedUpdate 50 раз в секунду, а кадров 165:
    /// модель подпрыгивает по высоте на каждом шаге физики, отсюда дрожь ног и волос при медленной ходьбе.
    /// Сглаживаем только высоту модели относительно корня персонажа.
    internal static class Smooth
    {
        class Unit { public Transform root, model; public Vector3 baseLocal, applied, lastSet; public float y; public bool init, hasSet; }
        static readonly Dictionary<IntPtr, Unit> units = new();
        /// Контроллеры без отдельной модели (например, служебные): не перепроверяются каждую секунду.
        static readonly HashSet<IntPtr> noModel = new();
        static readonly List<IntPtr> dead = new();
        static int scanVersion = -1; static float noModelT;
        public static int Count;

        public static void LateUpdate()
        {
            if (!Plugin.S.SmoothUnits.Value) { Restore(); return; }
            var ccs = Scan.Controllers();
            float now = Time.unscaledTime;
            // модель могла подгрузиться позже контроллера: изредка проверяем такие ещё раз
            if (now - noModelT > 10f) { noModelT = now; if (noModel.Count > 0) { noModel.Clear(); scanVersion = -1; } }
            if (Scan.ControllersVersion != scanVersion) { scanVersion = Scan.ControllersVersion; Sync(ccs); }
            float dt = Math.Max(1e-4f, Time.unscaledDeltaTime);
            float tau = Math.Max(0.005f, Plugin.S.SmoothTime.Value);
            float k = 1f - (float)Math.Exp(-dt / tau);
            dead.Clear();
            foreach (var kv in units)
            {
                var u = kv.Value;
                try
                {
                    if (u.root == null || u.model == null) { dead.Add(kv.Key); continue; }
                    float ry = u.root.position.y;
                    if (!u.init || Math.Abs(ry - u.y) > 0.25f) { u.y = ry; u.init = true; }
                    else u.y += (ry - u.y) * k;
                    // Игра сама двигает модель в сценках и суперприёмах (EXE-драйв ставит героиню перед камерой).
                    // Раньше мод каждый кадр возвращал модель на исходное место и ломал эти постановки.
                    // Теперь берём положение, которое выставила игра, и добавляем только своё смещение по высоте.
                    var cur = u.model.localPosition;
                    Vector3 gameLocal = (u.hasSet && (cur - u.lastSet).sqrMagnitude < 1e-10f) ? cur - u.applied : cur;
                    if ((gameLocal - u.baseLocal).sqrMagnitude > 0.0025f) { u.y = ry; } // модель двигает игра: не сглаживаем
                    u.baseLocal = gameLocal;
                    float off = u.y - ry;
                    var add = Math.Abs(off) < 1e-6f ? Vector3.zero : u.root.InverseTransformVector(new Vector3(0, off, 0));
                    var want = gameLocal + add;
                    // запись только при изменении: лишняя запись положения заставляет Unity пересчитывать всю иерархию модели
                    if ((want - cur).sqrMagnitude > 1e-12f) u.model.localPosition = want;
                    u.applied = add; u.lastSet = want; u.hasSet = true;
                }
                catch { dead.Add(kv.Key); }
            }
            foreach (var d in dead) units.Remove(d);
            if (dead.Count > 0) scanVersion = -1; // модель могли пересоздать под тем же контроллером: при следующем кадре найдём заново
            Count = units.Count;
        }

        static void Sync(List<CharacterController> ccs)
        {
            foreach (var cc in ccs)
            {
                try
                {
                    if (cc == null) continue;
                    IntPtr key = cc.Pointer;
                    if (units.ContainsKey(key) || noModel.Contains(key)) continue;
                    var anims = cc.gameObject.GetComponentsInChildren(Il2CppType.Of<Animator>(), true);
                    Transform model = null;
                    if (anims != null) foreach (var a in anims)
                    {
                        var an = a.TryCast<Animator>();
                        if (an != null && an.transform.Pointer != cc.transform.Pointer) { model = an.transform; break; }
                    }
                    if (model == null) { noModel.Add(key); continue; }
                    units[key] = new Unit { root = cc.transform, model = model, baseLocal = model.localPosition };
                }
                catch (Exception e) { Plugin.L.LogWarning("Smooth: " + e.Message); }
            }
            if (noModel.Count > 2000) noModel.Clear();
        }

        static void Restore()
        {
            if (units.Count == 0) return;
            foreach (var u in units.Values) try { if (u.model != null && u.hasSet) u.model.localPosition -= u.applied; } catch { }
            units.Clear(); noModel.Clear(); scanVersion = -1; Count = 0;
        }
    }
}
