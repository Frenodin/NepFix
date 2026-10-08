using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NepFix
{
    /// Плавные удары вне боя: мягкие переходы анимаций и плавное появление и исчезновение оружия.
    internal static class CombatSmooth
    {
        static Settings S => Plugin.S;

        class Fade { public Transform t; public Vector3 orig; public float start; public bool grow; }
        static readonly List<Fade> fades = new();
        class Pending { public DbModelChara chara; public int state; public float at; }
        static readonly List<Pending> pending = new();
        internal static bool Bypass;
        public static int Count;

        public static int[] Indices(int state) => state switch
        {
            1 or 2 => new[] { 0, 1, 2, 3 },
            3 or 4 => new[] { 0 },
            5 or 6 => new[] { 1 },
            7 or 8 => new[] { 2 },
            9 or 10 => new[] { 3 },
            _ => Array.Empty<int>()
        };

        public static Transform Weapon(DbModelChara c, int idx)
        {
            try
            {
                var a = c.GetAttach((DbModel.ModelAttach.AttachIndex)idx);
                var go = a?.GetObject();
                return go != null ? go.transform : null;
            }
            catch { return null; }
        }

        static Fade Find(Transform t) { foreach (var f in fades) if (f.t != null && f.t.Pointer == t.Pointer) return f; return null; }

        public static void StartGrow(Transform t)
        {
            var f = Find(t);
            var orig = f != null ? f.orig : t.localScale;
            if (f == null) { f = new Fade { t = t }; fades.Add(f); }
            f.orig = orig; f.start = Time.unscaledTime; f.grow = true;
            t.localScale = orig * 0.001f;
        }

        public static void StartShrink(Transform t)
        {
            var f = Find(t);
            if (f != null && !f.grow) return;
            var orig = f != null ? f.orig : t.localScale;
            if (f == null) { f = new Fade { t = t }; fades.Add(f); }
            f.orig = orig; f.start = Time.unscaledTime; f.grow = false;
        }

        public static void Defer(DbModelChara c, int state)
        {
            foreach (var p in pending) if (p.chara.Pointer == c.Pointer && p.state == state) return;
            pending.Add(new Pending { chara = c, state = state, at = Time.unscaledTime + Duration });
        }

        /// Игра снова достала оружие, пока ещё шло отложенное скрытие: скрытие отменяем, а уменьшение разворачиваем
        /// в появление с того же размера. Раньше отложенное скрытие срабатывало позже и прятало уже нужное оружие.
        public static void CancelHide(DbModelChara c, int drawState)
        {
            var idx = Indices(drawState);
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (p.chara.Pointer != c.Pointer) continue;
                // здесь остаются только скрытия, целиком входящие в доставаемое (частичные выполнены в FlushPartial)
                foreach (int a in Indices(p.state)) if (Array.IndexOf(idx, a) >= 0) { pending.RemoveAt(i); break; }
            }
            float now = Time.unscaledTime, d = Duration;
            foreach (int a in idx)
            {
                var t = Weapon(c, a); if (t == null) continue;
                var f = Find(t);
                if (f == null || f.grow) continue;
                float k = Math.Clamp((now - f.start) / d, 0f, 1f);
                f.grow = true; f.start = now - (1f - k) * d; // плавная кривая симметрична: продолжаем с текущего размера
            }
        }

        /// Перед тем как игра достанет часть оружия: если отложено скрытие большего набора (например, всего оружия),
        /// выполняем его сразу, иначе остальное оружие осталось бы видимым в крошечном размере.
        public static void FlushPartial(DbModelChara c, int drawState)
        {
            var idx = Indices(drawState);
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (p.chara.Pointer != c.Pointer) continue;
                var pi = Indices(p.state);
                bool overlap = false, subset = true;
                foreach (int a in pi) { if (Array.IndexOf(idx, a) >= 0) overlap = true; else subset = false; }
                if (!overlap || subset) continue;
                pending.RemoveAt(i);
                Execute(p);
            }
        }

        static void Execute(Pending p)
        {
            try { Bypass = true; p.chara.SetAnimationWeaponDrawState(p.state); }
            catch (Exception e) { Plugin.L.LogWarning("Оружие: " + e.Message); }
            finally { Bypass = false; }
            // после скрытия возвращаем исходный размер, чтобы следующее появление было правильным
            foreach (int idx in Indices(p.state))
            {
                var t = Weapon(p.chara, idx); if (t == null) continue;
                var f = Find(t);
                if (f != null && !f.grow) { t.localScale = f.orig; fades.Remove(f); }
            }
        }

        public static float Duration => Math.Clamp(S.WeaponFade.Value, 0.02f, 0.5f);

        /// Каждый кадр после анимации.
        public static void LateUpdate()
        {
            float now = Time.unscaledTime, d = Duration;
            for (int i = fades.Count - 1; i >= 0; i--)
            {
                var f = fades[i];
                try
                {
                    if (f.t == null) { fades.RemoveAt(i); continue; }
                    float k = Math.Clamp((now - f.start) / d, 0f, 1f);
                    // плавный разгон и торможение
                    float e = k * k * (3f - 2f * k);
                    float s = f.grow ? e : 1f - e;
                    // лёгкий «перелёт» при появлении, чтобы оружие не выглядело вставленным
                    if (f.grow && k < 1f) s *= 1f + 0.08f * (float)Math.Sin(k * Math.PI);
                    f.t.localScale = f.orig * Math.Max(0.001f, s);
                    if (k >= 1f && f.grow) { f.t.localScale = f.orig; fades.RemoveAt(i); }
                }
                catch { fades.RemoveAt(i); }
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (now < p.at) continue;
                pending.RemoveAt(i);
                Execute(p);
            }
            Count = fades.Count;
        }
    }

    [HarmonyPatch(typeof(DbModelChara), "SetAnimationWeaponDrawState")]
    internal static class PatchWeaponDraw
    {
        static void Prefix(DbModelChara __instance, int state, out bool[] __state)
        {
            __state = new bool[4];
            if (!Plugin.S.WeaponSmooth.Value || CombatSmooth.Bypass) return;
            try
            {
                if (state % 2 == 1) CombatSmooth.FlushPartial(__instance, state);
                for (int i = 0; i < 4; i++) { var t = CombatSmooth.Weapon(__instance, i); __state[i] = t != null && t.gameObject.activeInHierarchy; }
            }
            catch { }
        }

        static void Postfix(DbModelChara __instance, int state, bool[] __state)
        {
            if (!Plugin.S.WeaponSmooth.Value || CombatSmooth.Bypass) return;
            try
            {
                bool on = state % 2 == 1;
                if (on) CombatSmooth.CancelHide(__instance, state);
                foreach (int idx in CombatSmooth.Indices(state))
                {
                    var t = CombatSmooth.Weapon(__instance, idx);
                    if (t == null) continue;
                    bool now = t.gameObject.activeInHierarchy;
                    if (on && now && !__state[idx]) CombatSmooth.StartGrow(t);
                    if (!on && !now && __state[idx])
                    {
                        // игра уже спрятала оружие: показываем обратно, плавно уменьшаем и прячем по-настоящему позже
                        t.gameObject.SetActive(true);
                        CombatSmooth.StartShrink(t);
                        CombatSmooth.Defer(__instance, state);
                    }
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Оружие: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MapUnitBaseComponent), nameof(MapUnitBaseComponent.SetAnimation))]
    internal static class PatchUnitAnimBlend
    {
        static void Prefix(ref float fix_time)
        {
            float m = Plugin.S.AnimBlendMin.Value;
            if (m > 0.001f && fix_time > 0f && fix_time < m) fix_time = m;
        }
    }

    [HarmonyPatch(typeof(MapUnitBaseComponent), nameof(MapUnitBaseComponent.SetAnimationEnd))]
    internal static class PatchUnitAnimBlendEnd
    {
        static void Prefix(ref float fix_time)
        {
            float m = Plugin.S.AnimBlendMin.Value;
            if (m > 0.001f && fix_time > 0f && fix_time < m) fix_time = m;
        }
    }
}
