using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace NepFix
{
    /// Плавное движение отряда и мотоцикла. Игра двигает их только в шагах физики, а шагов меньше, чем кадров:
    /// по замеру в трети кадров мотоцикл стоял, в остальных прыгал почти на двойное расстояние, камера повторяла это.
    /// На время отрисовки каждой камеры ставим модели (и камеру) между двумя последними шагами физики,
    /// после отрисовки возвращаем как было. Игровая логика и физика настоящих положений не видят.
    internal static class Interp
    {
        class Unit
        {
            public Transform t; public bool player;
            public Vector3 prevP, curP; public Quaternion prevR, curR; public bool valid;
            public Vector3 savedP; public Quaternion savedR; public bool moved;
            public float sy; public bool syValid; public float extUntil;
        }
        static readonly List<Unit> units = new();
        static float nextScan, lastFixed = -1;
        static bool hooked, camMoved;
        static Transform camT; static Vector3 camSaved;
        static Il2CppSystem.Action<ScriptableRenderContext, Camera> dBegin, dEnd;
        public static int Count, Applied;

        static bool On => Plugin.S.MotionInterp.Value;

        public static void Hook()
        {
            if (hooked) return; hooked = true;
            try
            {
                dBegin = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(new Action<ScriptableRenderContext, Camera>(Begin));
                dEnd = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(new Action<ScriptableRenderContext, Camera>(End));
                RenderPipelineManager.add_beginCameraRendering(dBegin);
                RenderPipelineManager.add_endCameraRendering(dEnd);
            }
            catch (Exception e) { Plugin.L.LogWarning("Interp: " + e.Message); }
        }

        /// Каждый кадр из Update: к этому моменту все шаги физики кадра уже прошли.
        public static void Update()
        {
            if (!On) { units.Clear(); Count = 0; return; }
            Hook();
            float now = Time.unscaledTime;
            if (now >= nextScan) { nextScan = now + 1f; Scan(); }
            bool stepped = Time.fixedTime != lastFixed;
            lastFixed = Time.fixedTime;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                try
                {
                    if (u.t == null || !u.t.gameObject.activeInHierarchy) { units.RemoveAt(i); continue; }
                    Vector3 p = u.t.position; Quaternion r = u.t.rotation;
                    if (!u.valid) { u.prevP = u.curP = p; u.prevR = u.curR = r; u.valid = true; continue; }
                    if (stepped) { u.prevP = u.curP; u.prevR = u.curR; u.curP = p; u.curR = r; }
                    else if ((p - u.curP).sqrMagnitude > 1e-8f) { u.prevP = u.curP = p; u.prevR = u.curR = r; u.extUntil = now + 1.5f; } // сдвинули вне физики: сценка или суперприём, сглаживание на время выключаем
                    if ((u.curP - u.prevP).sqrMagnitude > 4f) { u.prevP = u.curP; u.prevR = u.curR; u.extUntil = now + 1.5f; } // телепорт
                }
                catch { units.RemoveAt(i); }
            }
            // мотоцикл на уступах: игра поднимает его на высоту уступа за один шаг, видно как подскок
            float tau = Plugin.S.BikeLedgeSmooth.Value;
            float dt = Math.Max(1e-4f, Time.unscaledDeltaTime);
            foreach (var u in units)
            {
                if (!u.valid) continue;
                if (!u.player || !Bike.Riding || tau < 0.005f) { u.syValid = false; continue; }
                float y = u.curP.y;
                if (!u.syValid || Math.Abs(y - u.sy) > 1.5f) { u.sy = y; u.syValid = true; continue; }
                float t = y > u.sy ? tau : tau * 0.35f;
                u.sy += (y - u.sy) * (1f - (float)Math.Exp(-dt / t));
            }
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
                    var t = cc.transform; bool have = false;
                    foreach (var u in units) if (u.t != null && u.t.Pointer == t.Pointer) { have = true; break; }
                    if (have) continue;
                    units.Add(new Unit { t = t, player = t.name.Contains("PLAYER") });
                }
            }
            catch { }
        }

        static bool Target(Camera cam) => cam != null && cam.cameraType == CameraType.Game && cam.targetTexture == null && !cam.orthographic && cam.farClipPlane > 2f;

        static void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!On || !Target(cam) || units.Count == 0) return;
            // замедление и стоп-кадры во время суперприёмов и сценок: игра двигает героинь сама, не трогаем
            if (Math.Abs(Time.timeScale - 1f) > 0.01f) return;
            try
            {
                float now = Time.unscaledTime;
                var main = Camera.main;
                bool mainCam = main != null && main.Pointer == cam.Pointer;
                float fdt = Math.Max(1e-4f, Time.fixedDeltaTime);
                float a = Mathf.Clamp01((Time.time - Time.fixedTime) / fdt);
                Vector3 camOff = Vector3.zero; Applied = 0;
                foreach (var u in units)
                {
                    u.moved = false;
                    if (!u.valid || u.t == null || now < u.extUntil) continue;
                    Vector3 p = u.t.position;
                    if ((p - u.curP).sqrMagnitude > 1e-8f) continue;
                    Vector3 ip = Vector3.LerpUnclamped(u.prevP, u.curP, a);
                    Quaternion ir = Quaternion.Slerp(u.prevR, u.curR, a);
                    if (u.syValid) ip.y = u.sy + (ip.y - u.curP.y);
                    u.savedP = p; u.savedR = u.t.rotation;
                    u.t.SetPositionAndRotation(ip, ir);
                    u.moved = true; Applied++;
                    if (u.player) camOff = ip - p;
                }
                camMoved = false;
                if (mainCam && camOff.sqrMagnitude > 1e-10f && camOff.sqrMagnitude < 1f)
                {
                    camT = cam.transform; camSaved = camT.position;
                    camT.position = camSaved + camOff; camMoved = true;
                }
            }
            catch { }
        }

        static void End(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Target(cam)) return;
            try
            {
                foreach (var u in units)
                {
                    if (!u.moved) continue;
                    u.moved = false;
                    try { u.t.SetPositionAndRotation(u.savedP, u.savedR); } catch { }
                }
                if (camMoved && camT != null) { camT.position = camSaved; camMoved = false; }
            }
            catch { }
        }
    }
}
