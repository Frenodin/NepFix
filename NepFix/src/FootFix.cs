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
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<CharaIK.FootIK>());
                Count = arr == null ? 0 : arr.Length;
                if (arr == null) return;
                // игра сама включает IK по ситуации, поэтому вмешиваемся только в режиме «выкл»
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
