using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace NepFix
{
    /// Прямые вызовы нативных функций Unity (icall), вырезанных из IL2CPP-сборки игры.
    internal static class ICalls
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetInt(int v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetInt();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetFloat(float v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate float GetFloat();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetInt2(int a, int b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetBool([MarshalAs(UnmanagedType.U1)] bool v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SetIntBool(int a, bool b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] public delegate bool GetBool();

        static readonly Dictionary<string, Delegate> cache = new();
        static readonly HashSet<string> missing = new();

        public static T Get<T>(string name) where T : Delegate
        {
            if (cache.TryGetValue(name, out var d)) return (T)d;
            if (missing.Contains(name)) return null;
            try
            {
                var ptr = IL2CPP.il2cpp_resolve_icall(name);
                if (ptr == IntPtr.Zero) throw new Exception("not found");
                var del = Marshal.GetDelegateForFunctionPointer<T>(ptr);
                cache[name] = del;
                return del;
            }
            catch (Exception e)
            {
                missing.Add(name);
                Plugin.L.LogWarning($"icall {name} недоступен: {e.Message}");
                return null;
            }
        }

        const string QS = "UnityEngine.QualitySettings::";
        public static void Try(Action a, string what)
        {
            try { Prof.Run(what, a); } catch (Exception e) { Plugin.L.LogWarning($"{what}: {e.Message}"); }
        }

        public static int QualityLevel() => Get<GetInt>(QS + "GetQualityLevel")?.Invoke() ?? -1;
        public static void SetQualityLevel(int lvl) => Get<SetIntBool>(QS + "SetQualityLevel")?.Invoke(lvl, true);
        public static int MasterTextureLimit() => Get<GetInt>(QS + "get_masterTextureLimit")?.Invoke() ?? -1;
        public static void SetMasterTextureLimit(int v) => Get<SetInt>(QS + "set_masterTextureLimit")?.Invoke(v);
        public static void SetAnisotropic(int mode) => Get<SetInt>(QS + "set_anisotropicFiltering")?.Invoke(mode);
        public static void SetAnisoLimits(int min, int max) => Get<SetInt2>("UnityEngine.Texture::SetGlobalAnisotropicFilteringLimits")?.Invoke(min, max);
        public static float LodBias() => Get<GetFloat>(QS + "get_lodBias")?.Invoke() ?? -1f;
        public static void SetLodBias(float v) => Get<SetFloat>(QS + "set_lodBias")?.Invoke(v);
        public static int SkinWeights() => Get<GetInt>(QS + "get_skinWeights")?.Invoke() ?? -1;
        public static void SetSkinWeights(int v) => Get<SetInt>(QS + "set_skinWeights")?.Invoke(v);
        public static int MaxQueuedFrames() => Get<GetInt>(QS + "get_maxQueuedFrames")?.Invoke() ?? -1;
        public static void SetMaxQueuedFrames(int v) => Get<SetInt>(QS + "set_maxQueuedFrames")?.Invoke(v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate float GetFloatImplD(IntPtr self, int id);
        public static float MaterialGetFloat(IntPtr mat, int id)
        {
            var d = Get<GetFloatImplD>("UnityEngine.Material::GetFloatImpl");
            return d == null ? float.NaN : d(mat, id);
        }
        public static int VSyncCount() => Get<GetInt>(QS + "get_vSyncCount")?.Invoke() ?? -1;
    }

    internal static class Win32
    {
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
        static uint myPid;
        /// Раньше на каждый вызов создавался объект Process: в меню это было несколько раз за кадр.
        static uint MyPid => myPid != 0 ? myPid : (myPid = GetCurrentProcessId());

        /// Позиция курсора в клиентских координатах окна игры (пиксели, начало — левый верхний угол).
        public static bool MouseClient(out float x, out float y)
        {
            x = y = 0;
            IntPtr w = GetForegroundWindow();
            if (w == IntPtr.Zero) return false;
            GetWindowThreadProcessId(w, out uint pid);
            if (pid != MyPid) return false;
            if (!GetCursorPos(out var p) || !ScreenToClient(w, ref p)) return false;
            x = p.X; y = p.Y; return true;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsW(string dev, int mode, ref DEVMODE dm);

        public static int RefreshRate()
        {
            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettingsW(null, -1, ref dm) && dm.dmDisplayFrequency > 1) return dm.dmDisplayFrequency;
            }
            catch { }
            return 60;
        }

        static string vkKey1, vkKey2; static int vkVal1, vkVal2;
        /// Код клавиши по названию из настроек; два последних запроса запоминаются, разбор строки не каждый кадр.
        public static int KeyToVk(string key)
        {
            if (ReferenceEquals(key, vkKey1)) return vkVal1;
            if (ReferenceEquals(key, vkKey2)) return vkVal2;
            int v = ParseVk(key);
            vkKey2 = vkKey1; vkVal2 = vkVal1; vkKey1 = key; vkVal1 = v;
            return v;
        }

        static int ParseVk(string key)
        {
            key = (key ?? "").Trim().ToUpperInvariant();
            if (key.StartsWith("F") && int.TryParse(key.Substring(1), out int n) && n >= 1 && n <= 12) return 0x70 + n - 1;
            if (key == "INSERT") return 0x2D;
            if (key == "HOME") return 0x24;
            return 0x79; // F10
        }
    }
}
