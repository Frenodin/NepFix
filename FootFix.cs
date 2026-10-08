using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Постановка ступней по земле (компонент игры FootIK).
    internal static class FootFix
    {
        public static int Count;
        static bool? applied;

        public static void Apply()
        {
            bool want = Plugin.S.FootIK.Value;
            // игра сама включает IK по ситуации, поэтому вмешиваемся только в режиме «выкл» и один раз при включении обратно.
            // В остальное время сцену не обходим, разве что для счётчика в открытом меню.
            if (want && applied != false && !Plugin.MenuOpen) return;
            try
            {
                var arr = Scan.All<CharaIK.FootIK>();
                Count = arr == null ? 0 : arr.Length;
                if (arr == null) return;
                if (want && applied != false) return;
                foreach (var o in arr)
                {
                    var f = o.TryCast<CharaIK.FootIK>(); if (f == null) continue;
                    f.SetUseIK(want, true);
                }
                applied = want;
            }
            catch (Exception e) { Plugin.L.LogWarning("FootIK: " + e.Message); }
        }
    }
}
