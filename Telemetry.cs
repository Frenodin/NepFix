using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NepFix
{
    /// Сбор системной статистики в фоне (NVML для NVIDIA, PDH для остальных GPU, WinAPI для CPU/RAM).
    internal static class Telemetry
    {
        // ---- публичные значения (читаются из GUI) ----
        public static string GpuName = "", CpuName = "", DriverVersion = "";
        public static float GpuLoad = -1, GpuMemLoad = -1, GpuTemp = -1, GpuClock = -1, GpuMemClock = -1, GpuPower = -1, GpuPowerLimit = -1, GpuFan = -1;
        public static double VramUsedMB = -1, VramTotalMB = -1, VramGameMB = -1;
        public static float CpuLoad = -1, CpuGame = -1;
        public static int CpuThreads = Environment.ProcessorCount, GameThreads;
        public static double RamUsedMB = -1, RamTotalMB = -1, RamGameMB = -1, RamGamePrivateMB = -1, ManagedMB = -1;
        public static string Source = "";

        static Thread thread;
        static volatile bool run;

        public static void Start()
        {
            if (thread != null) return;
            run = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "NepFix.Telemetry", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        static void Loop()
        {
            try { CpuName = CpuModel(); } catch { }
            bool nv = Nvml.Init();
            Source = nv ? "NVML" : "PDH";
            Pdh.Init();
            var proc = Process.GetCurrentProcess(); int pid = proc.Id;
            TimeSpan lastProc = proc.TotalProcessorTime; long lastIdle = 0, lastKernel = 0, lastUser = 0; GetTimes(ref lastIdle, ref lastKernel, ref lastUser);
            var sw = Stopwatch.StartNew(); double lastT = 0;
            int tick = 0; bool wasIdle = false, prevDetail = false;
            while (run)
            {
                Thread.Sleep(500);
                try
                {
                    // Без оверлея и меню данные никто не видит, а счётчики GPU Windows (PDH) опрашивают драйвер по всем процессам
                    // и сами могут давать подёргивания. Поэтому в это время ничего не замеряем.
                    int mode = 0; try { mode = Plugin.S.HudMode.Value; } catch { }
                    bool menu = Plugin.MenuOpen;
                    if (mode == 0 && !menu) { wasIdle = true; continue; }
                    bool detail = mode >= 2 || menu;
                    tick++;
                    // CPU
                    double now = sw.Elapsed.TotalSeconds, dt = now - lastT; lastT = now;
                    long idle = 0, kernel = 0, user = 0; GetTimes(ref idle, ref kernel, ref user);
                    long di = idle - lastIdle, dk = kernel - lastKernel, du = user - lastUser;
                    if (!wasIdle && dk + du > 0) CpuLoad = (float)(100.0 * (dk + du - di) / (dk + du));
                    lastIdle = idle; lastKernel = kernel; lastUser = user;
                    if (detail)
                    {
                        proc.Refresh();
                        var pt = proc.TotalProcessorTime;
                        if (!wasIdle && prevDetail && dt > 0) CpuGame = (float)(100.0 * (pt - lastProc).TotalSeconds / (dt * Environment.ProcessorCount));
                        lastProc = pt;
                        if (tick % 4 == 0 || GameThreads == 0) GameThreads = proc.Threads.Count; // перечисление потоков дорогое
                        RamGameMB = proc.WorkingSet64 / 1048576.0;
                        RamGamePrivateMB = proc.PrivateMemorySize64 / 1048576.0;
                        ManagedMB = GC.GetTotalMemory(false) / 1048576.0;
                        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                        if (GlobalMemoryStatusEx(ref ms)) { RamTotalMB = ms.ullTotalPhys / 1048576.0; RamUsedMB = (ms.ullTotalPhys - ms.ullAvailPhys) / 1048576.0; }
                    }
                    wasIdle = false; prevDetail = detail;

                    // GPU
                    if (nv) Nvml.Sample();
                    // PDH нужен для загрузки GPU без NVIDIA и для памяти GPU самой игры (только в подробном режиме), не чаще раза в секунду
                    if ((!nv || detail) && tick % 2 == 0) Pdh.Sample(pid, !nv);
                }
                catch (Exception e) { Plugin.L.LogWarning("Telemetry: " + e.Message); Thread.Sleep(2000); }
            }
        }

        static string CpuModel()
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (k?.GetValue("ProcessorNameString") as string ?? "").Trim();
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        static void GetTimes(ref long i, ref long k, ref long u) { if (GetSystemTimes(out var a, out var b, out var c)) { i = a; k = b; u = c; } }

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        // ---------------- NVML ----------------
        static class Nvml
        {
            const string DLL = "nvml.dll";
            [DllImport(DLL)] static extern int nvmlInit_v2();
            [DllImport(DLL)] static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out IntPtr dev);
            [DllImport(DLL)] static extern int nvmlDeviceGetName(IntPtr dev, byte[] name, uint len);
            [DllImport(DLL)] static extern int nvmlSystemGetDriverVersion(byte[] v, uint len);
            [StructLayout(LayoutKind.Sequential)] struct Util { public uint gpu, memory; }
            [StructLayout(LayoutKind.Sequential)] struct Mem { public ulong total, free, used; }
            [DllImport(DLL)] static extern int nvmlDeviceGetUtilizationRates(IntPtr dev, out Util u);
            [DllImport(DLL)] static extern int nvmlDeviceGetMemoryInfo(IntPtr dev, out Mem m);
            [DllImport(DLL)] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint t);
            [DllImport(DLL)] static extern int nvmlDeviceGetClockInfo(IntPtr dev, int type, out uint mhz);
            [DllImport(DLL)] static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint mw);
            [DllImport(DLL)] static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr dev, out uint mw);
            [DllImport(DLL)] static extern int nvmlDeviceGetFanSpeed(IntPtr dev, out uint pct);
            static IntPtr dev;

            static string Str(byte[] b) { int n = Array.IndexOf(b, (byte)0); return Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n); }

            public static bool Init()
            {
                try
                {
                    if (!NativeLibrary.TryLoad(DLL, out _) &&
                        !NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, DLL), out _) &&
                        !NativeLibrary.TryLoad(@"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll", out _)) return false;
                    if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out dev) != 0) return false;
                    var b = new byte[96];
                    if (nvmlDeviceGetName(dev, b, 96) == 0) GpuName = Str(b);
                    b = new byte[80];
                    if (nvmlSystemGetDriverVersion(b, 80) == 0) DriverVersion = Str(b);
                    if (nvmlDeviceGetEnforcedPowerLimit(dev, out uint pl) == 0) GpuPowerLimit = pl / 1000f;
                    return true;
                }
                catch { return false; }
            }

            public static void Sample()
            {
                if (nvmlDeviceGetUtilizationRates(dev, out var u) == 0) { GpuLoad = u.gpu; GpuMemLoad = u.memory; }
                if (nvmlDeviceGetMemoryInfo(dev, out var m) == 0) { VramTotalMB = m.total / 1048576.0; VramUsedMB = m.used / 1048576.0; }
                if (nvmlDeviceGetTemperature(dev, 0, out uint t) == 0) GpuTemp = t;
                if (nvmlDeviceGetClockInfo(dev, 0, out uint c) == 0) GpuClock = c;
                if (nvmlDeviceGetClockInfo(dev, 2, out uint mc) == 0) GpuMemClock = mc;
                if (nvmlDeviceGetPowerUsage(dev, out uint p) == 0) GpuPower = p / 1000f;
                if (nvmlDeviceGetFanSpeed(dev, out uint f) == 0) GpuFan = f;
            }
        }

        // ---------------- PDH (счётчики Windows: работают с любым GPU) ----------------
        static class Pdh
        {
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQueryW(string src, IntPtr user, out IntPtr q);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounterW(IntPtr q, string path, IntPtr user, out IntPtr c);
            [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr q);
            [DllImport("pdh.dll")] static extern int PdhRemoveCounter(IntPtr c);
            [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArrayW(IntPtr c, uint fmt, ref uint size, out uint count, IntPtr buf);
            const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;

            static IntPtr q, cGpuProcMem, cGpu3D;
            static int pidAdded = -1;
            static bool ok;

            public static void Init()
            {
                try { ok = PdhOpenQueryW(null, IntPtr.Zero, out q) == 0; } catch { ok = false; }
            }

            public static void Sample(int pid, bool gpuLoadToo)
            {
                if (!ok) return;
                if (pidAdded != pid)
                {
                    pidAdded = pid;
                    PdhAddEnglishCounterW(q, $@"\GPU Process Memory(pid_{pid}_*)\Dedicated Usage", IntPtr.Zero, out cGpuProcMem);
                    if (gpuLoadToo) PdhAddEnglishCounterW(q, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out cGpu3D);
                    PdhCollectQueryData(q);
                    return;
                }
                if (PdhCollectQueryData(q) != 0) return;
                double mem = Sum(cGpuProcMem, false);
                if (mem >= 0) VramGameMB = mem / 1048576.0;
                if (gpuLoadToo && cGpu3D != IntPtr.Zero) { double g = Sum(cGpu3D, false); if (g >= 0) GpuLoad = (float)Math.Min(100, g); }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct ITEM { public IntPtr name; public uint status; public uint pad; public double value; }

            static double Sum(IntPtr c, bool max)
            {
                if (c == IntPtr.Zero) return -1;
                uint size = 0;
                int r = PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE, ref size, out uint n, IntPtr.Zero);
                if ((uint)r != PDH_MORE_DATA || size == 0) return n == 0 && r == 0 ? 0 : -1;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE, ref size, out n, buf) != 0) return -1;
                    double s = 0; int sz = Marshal.SizeOf<ITEM>();
                    for (int i = 0; i < n; i++)
                    {
                        var it = Marshal.PtrToStructure<ITEM>(buf + i * sz);
                        if (it.status == 0) s = max ? Math.Max(s, it.value) : s + it.value;
                    }
                    return s;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
    }

    /// Статистика кадров (главный поток).
    internal static class FrameStats
    {
        public const int N = 240;
        public static readonly float[] Ms = new float[N];
        public static int Head;
        public static float Fps, AvgMs, Low1, MaxMs, MinMs;
        static float acc; static int frames;
        static readonly float[] tmp = new float[N];

        public static void Push(float dt)
        {
            Ms[Head] = dt * 1000f; Head = (Head + 1) % N;
            acc += dt; frames++;
            if (acc >= 0.5f)
            {
                Fps = frames / acc; AvgMs = acc * 1000f / frames; acc = 0; frames = 0;
                Array.Copy(Ms, tmp, N); Array.Sort(tmp);
                int cnt = 0; float s = 0; MaxMs = 0; MinMs = float.MaxValue;
                for (int i = 0; i < N; i++) { if (tmp[i] <= 0) continue; MaxMs = Math.Max(MaxMs, tmp[i]); MinMs = Math.Min(MinMs, tmp[i]); }
                // 1% low: средний FPS худшего 1% кадров
                for (int i = N - 1; i >= 0 && cnt < Math.Max(1, N / 100 * 1 + 2); i--) { if (tmp[i] <= 0) continue; s += tmp[i]; cnt++; }
                Low1 = cnt > 0 ? 1000f / (s / cnt) : 0;
            }
        }
    }
}
