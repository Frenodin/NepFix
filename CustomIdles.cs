using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Сторонние idle-анимации из nepidle.bundle. Проигрываются через подмену клипа в «пустом» состоянии аниматора игры.
    internal static class CustomIdles
    {
        public static readonly List<AnimationClip> Clips = new();
        public static string Status = "не загружены";
        static bool tried;
        static AssetBundle bundle;

        // состояния-носители: петли эмоций, которые игра почти не использует на карте
        public static readonly string[] Carriers = { "40092_GN_Sad_Loop", "40082_GN_Angry_Loop", "40052_GN_Scary_Loop", "40072_GN_Surprise_Loop" };

        public static void Load()
        {
            if (tried) return;
            tried = true;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(CustomIdles).Assembly.Location), "nepidle.bundle");
                if (!File.Exists(path)) { Status = "нет файла nepidle.bundle: положите FBX в NepFX_Unity\\Assets\\Idles и запустите build_nepfx.bat"; return; }
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) { Status = "не удалось открыть nepidle.bundle"; return; }
                bundle.hideFlags = HideFlags.HideAndDontSave;
                var arr = bundle.LoadAllAssets(Il2CppType.Of<AnimationClip>());
                if (arr != null) foreach (var o in arr)
                {
                    var c = o.TryCast<AnimationClip>();
                    if (c == null || c.name.StartsWith("__preview__")) continue;
                    if (!c.humanMotion) { Plugin.L.LogWarning($"Анимации: клип {c.name} не Humanoid, пропущен"); continue; }
                    c.hideFlags = HideFlags.HideAndDontSave;
                    Clips.Add(c);
                }
                Status = Clips.Count == 0 ? "в пакете нет Humanoid-анимаций" : $"загружено {Clips.Count}: " + string.Join(", ", Clips.ConvertAll(x => x.name));
                Plugin.L.LogInfo("Анимации: сторонние " + Status);
            }
            catch (Exception e) { Status = "ошибка: " + e.Message; Plugin.L.LogWarning("CustomIdles: " + e); }
        }
    }
}
