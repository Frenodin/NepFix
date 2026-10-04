using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace NepFix
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "casimoy.nepfix";
        public const string Name = "NepFix";
        public static bool MenuOpen;
        public const string Version = "1.19.1";

        internal static ManualLogSource L;
        internal static Settings S;
        internal static Plugin Instance;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetConsoleOutputCP(uint cp);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetConsoleCP(uint cp);

        public override void Load()
        {
            // консоль BepInEx пишет UTF-8, а окно консоли Windows по умолчанию в кодировке 866: русский текст превращается в «╨║╨░»
            try { SetConsoleOutputCP(65001); SetConsoleCP(65001); } catch { }
            Instance = this;
            L = Log;
            S = new Settings(Config);
            try { Patches.Apply(new Harmony(Guid)); }
            catch (Exception e) { L.LogWarning("Harmony patches failed: " + e); }
            FrameRateHook.Install();
            MsaaWatch.Install();
            Telemetry.Start();
            AddComponent<NepFixBehaviour>();
            L.LogInfo($"{Name} {Version} loaded. Menu: {S.MenuKey.Value}");
        }
    }
}
