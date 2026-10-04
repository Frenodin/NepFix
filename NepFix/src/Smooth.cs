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
        static float nextScan;
        public static int Count;

        public static void LateUpdate()
        {
            if (!Plugin.S.SmoothUnits.Value) { Restore(); return; }
            float now = Time.unscaledTime;
            if (now >= nextScan) { nextScan = now + 1f; Scan(); }
            float dt = Math.Max(1e-4f, Time.unscaledDeltaTime);
            float tau = Math.Max(0.005f, Plugin.S.SmoothTime.Value);
            float k = 1f - (float)Math.Exp(-dt / tau);
            var dead = new List<IntPtr>();
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
                    var add = u.root.InverseTransformVector(new Vector3(0, off, 0));
                    u.model.localPosition = gameLocal + add;
                    u.applied = add; u.lastSet = u.model.localPosition; u.hasSet = true;
                }
                catch { dead.Add(kv.Key); }
            }
            foreach (var d in dead) units.Remove(d);
            Count = units.Count;
        }

        static void Scan()
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CharacterController>());
                if (arr == null) return;
                foreach (var o in arr)
                {
                    var cc = o.TryCast<CharacterController>(); if (cc == null) continue;
                    IntPtr key = cc.Pointer;
                    if (units.ContainsKey(key)) continue;
                    var anims = cc.gameObject.GetComponentsInChildren(Il2CppType.Of<Animator>(), true);
                    Transform model = null;
                    if (anims != null) foreach (var a in anims)
                    {
                        var an = a.TryCast<Animator>();
                        if (an != null && an.transform.Pointer != cc.transform.Pointer) { model = an.transform; break; }
                    }
                    if (model == null) continue;
                    units[key] = new Unit { root = cc.transform, model = model, baseLocal = model.localPosition };
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Smooth: " + e.Message); }
        }

        static void Restore()
        {
            if (units.Count == 0) return;
            foreach (var u in units.Values) try { if (u.model != null && u.hasSet) u.model.localPosition -= u.applied; } catch { }
            units.Clear(); Count = 0;
        }
    }
}
