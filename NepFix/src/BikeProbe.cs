using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Замер езды на мотоцикле: каждый кадр пишет положение, поворот и шаги физики в BepInEx\NepFix_bike.csv.
    /// Нужен, чтобы понять, откуда рывки, прежде чем что-то менять.
    internal static class BikeProbe
    {
        static MapUnitBaseComponent unit; static MapMoveBikePad bike; static Transform mover; static string fileName = "";
        static Vector3 lastCam; static float lastCamYaw;
        static float nextScan;
        public static int FixedSteps;
        static readonly StringBuilder sb = new();
        static int rows; static bool headerDone, dataDumped;
        static Vector3 lastRoot, lastModel; static float lastYaw, lastModelYaw; static bool hasLast;
        public static int Crashes, WallHits;
        public static string Info = "";
        const int MaxRows = 20000;

        public static void FixedTick() { FixedSteps++; }

        static bool wasOn;
        /// Замер только что выключили: нужно дописать файл.
        public static bool Pending => wasOn || sb.Length > 0;
        public static void LateUpdate()
        {
            bool on = Plugin.S.BikeProbe.Value;
            if (on && !wasOn) Start();
            wasOn = on;
            if (!Plugin.S.BikeProbe.Value) { if (sb.Length > 0) Flush(); return; }
            float now = Time.unscaledTime;
            if (now >= nextScan) { nextScan = now + 1f; Find(); }
            int fs = FixedSteps; FixedSteps = 0;
            if (bike == null || unit == null) { hasLast = false; return; }
            try
            {
                float spd = bike.speed_;
                var root = mover != null ? mover : unit.transform;
                var camT = Camera.main != null ? Camera.main.transform : null;
                Vector3 cp = camT != null ? camT.position : Vector3.zero; float cyaw = camT != null ? camT.eulerAngles.y : 0;
                Transform model = null; try { model = bike.wheelie_transform_; } catch { }
                Vector3 rp = root.position; float yaw = root.eulerAngles.y;
                Vector3 mp = model != null ? model.position : rp; float myaw = model != null ? model.eulerAngles.y : yaw;
                Vector3 mloc = model != null ? model.localPosition : Vector3.zero;
                if (!dataDumped) { dataDumped = true; DumpData(); }
                if (!headerDone)
                {
                    headerDone = true;
                    sb.AppendLine("t;dt_ms;fixed;speed;move_speed;root_dx;root_dy;root_dz;root_step_m;root_yaw;root_dyaw;model_step_m;model_dyaw;model_local_y;wheelie;handle;boost_mode;smooth_units;cam_step_m;cam_dyaw");
                }
                if (hasLast)
                {
                    float dt = Time.unscaledDeltaTime * 1000f;
                    var d = rp - lastRoot;
                    sb.Append($"{now:0.000};{dt:0.00};{fs};{spd:0.00};{bike.move_speed_:0.00};{d.x:0.0000};{d.y:0.0000};{d.z:0.0000};{d.magnitude:0.0000};{yaw:0.00};{Mathf.DeltaAngle(lastYaw, yaw):0.000};{(mp - lastModel).magnitude:0.0000};{Mathf.DeltaAngle(lastModelYaw, myaw):0.000};{mloc.y:0.0000};{bike.wheelie_degree_now_:0.00};{bike.handle_rate_:0.000};{(int)bike.boost_mode_};{Smooth.Count};{(cp - lastCam).magnitude:0.0000};{Mathf.DeltaAngle(lastCamYaw, cyaw):0.000}");
                    sb.AppendLine();
                    rows++;
                    if (rows % 600 == 0) Flush();
                    if (rows >= MaxRows) { Flush(); Plugin.S.BikeProbe.Value = false; Plugin.L.LogInfo("Мотоцикл: замер закончен, записано строк " + rows); }
                }
                lastCam = cp; lastCamYaw = cyaw; lastRoot = rp; lastModel = mp; lastYaw = yaw; lastModelYaw = myaw; hasLast = true;
                Info = $"Замер идёт: строк {rows}, столкновений {Crashes}, скорость {spd:0.0}.";
            }
            catch (Exception e) { Info = "Замер: " + e.Message; bike = null; }
        }

        static void Find()
        {
            try
            {
                MapUnitBaseComponent best = null; MapMoveBikePad bp = null; int count = 0;
                foreach (var u in Scan.MapUnits())
                {
                    MapMoveBikePad b = null; try { b = u.map_move_bike_pad_; } catch { }
                    if (b == null) continue;
                    count++;
                    if (best == null || Math.Abs(b.speed_) > Math.Abs(bp.speed_)) { best = u; bp = b; }
                }
                if (bp == null && bike != null) { Flush(); hasLast = false; }
                if (best != null && (unit == null || unit.Pointer != best.Pointer))
                {
                    mover = null;
                    try { var cc = best.gameObject.GetComponentInChildren(Il2CppType.Of<CharacterController>(), true); if (cc != null) mover = cc.TryCast<CharacterController>()?.transform; } catch { }
                    Plugin.L.LogInfo($"Мотоцикл: замер следит за '{best.name}', движется '{(mover != null ? mover.name : best.name)}', всего мотоциклов {count}");
                    hasLast = false;
                }
                unit = best; bike = bp;
                if (bike == null) Info = "Замер включён: сядьте на мотоцикл.";
            }
            catch (Exception e) { Info = "Замер: " + e.Message; }
        }

        static string PathCsv => Path.Combine(BepInEx.Paths.BepInExRootPath, fileName);

        static void Flush()
        {
            try { if (sb.Length > 0) { File.AppendAllText(PathCsv, sb.ToString()); sb.Clear(); } } catch (Exception e) { Plugin.L.LogWarning("Мотоцикл: " + e.Message); }
        }

        public static void Start()
        {
            fileName = $"NepFix_bike_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            Plugin.L.LogInfo("Мотоцикл: замер начат, файл " + fileName);
            rows = 0; headerDone = false; dataDumped = false; Crashes = 0; WallHits = 0; hasLast = false;
        }

        static void DumpData()
        {
            try
            {
                var data = bike.data_;
                var s = new StringBuilder("Мотоцикл, настройки игры:");
                foreach (var p in typeof(MapManagerDataMoveUnitBikePad).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.PropertyType == typeof(float) || p.PropertyType == typeof(bool)) { try { s.Append($" {p.Name}={p.GetValue(data)}"); } catch { } }
                foreach (var p in typeof(MapMoveBikePad).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.PropertyType == typeof(float) && p.Name.EndsWith("_") && !p.Name.StartsWith("radial")) { try { s.Append($" {p.Name}={p.GetValue(bike)}"); } catch { } }
                s.Append($" fixedDeltaTime={Time.fixedDeltaTime}");
                Plugin.L.LogInfo(s.ToString());
            }
            catch (Exception e) { Plugin.L.LogInfo("Мотоцикл: настройки не прочитаны, " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MapMoveBikePad), nameof(MapMoveBikePad.Crash))]
    internal static class PatchBikeCrash
    {
        static void Postfix()
        {
            BikeProbe.Crashes++;
            if (Plugin.S.BikeProbe.Value) Plugin.L.LogInfo($"Мотоцикл: столкновение в {Time.unscaledTime:0.00}");
        }
    }
}
