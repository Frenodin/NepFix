using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NepFix
{
    /// Поиск фризов: сколько времени заняла каждая задача мода в кадре и была ли сборка мусора.
    internal static class Prof
    {
        static readonly Dictionary<string, double> cur = new(), prev = new();
        static int gcNet = -1; static long usedIl = -1;
        const int H = 120;
        static readonly float[] hist = new float[H], sortBuf = new float[H];
        static int histN, histHead;
        public static int Spikes;
        public static string Last = "";
        static double frameTotal, prevTotal;

        public static long Begin() => Stopwatch.GetTimestamp();
        public static void End(string name, long t0)
        {
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            cur.TryGetValue(name, out double v); cur[name] = v + ms; frameTotal += ms;
        }

        public static void Run(string name, Action a)
        {
            long t0 = Stopwatch.GetTimestamp();
            try { a(); } finally { End(name, t0); }
        }

        static float Median()
        {
            Array.Copy(hist, sortBuf, histN);
            Array.Sort(sortBuf, 0, histN);
            return sortBuf[histN / 2];
        }

        /// В начале Update: dt прошлого кадра.
        public static void Frame(float dt)
        {
            float ms = dt * 1000f;
            int gn = System.GC.CollectionCount(0); long ui = -1;
            try { ui = Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_get_used_size(); } catch { }
            // медиана считается только для подозрительно долгих кадров, а не каждый кадр
            if (histN > 30 && ms > 35f && Plugin.S.VerboseLog.Value)
            {
                float med = Median();
                if (ms > med * 2.5f)
                {
                    Spikes++;
                    bool ilGc = usedIl > 0 && ui >= 0 && ui < usedIl - 4 * 1048576;
                    float since = UnityEngine.Time.unscaledTime - Scan.SceneChangedAt;
                    string work(Dictionary<string, double> d, double total) => d.Count == 0 ? "нет" : $"всего {total:0.0}: " + string.Join(", ", d.Where(kv => kv.Value >= 0.3).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0.0}"));
                    Last = $"Фриз {ms:0} мс при обычном кадре {med:0.0} мс. Сборка мусора: мод {(gcNet >= 0 && gn != gcNet ? "да" : "нет")}, игра {(ilGc ? $"да, освобождено {(usedIl - ui) / 1048576} МБ" : "нет")}, куча игры {ui / 1048576} МБ." +
                           (since < 15f ? $" Смена сцены {since:0.0} с назад, игра догружает её." : "") +
                           $" Работа мода в кадре фриза, мс: {work(cur, frameTotal)}. В кадре до него: {work(prev, prevTotal)}.";
                    Plugin.L.LogInfo(Last);
                }
            }
            gcNet = gn; usedIl = ui;
            hist[histHead] = ms; histHead = (histHead + 1) % H; if (histN < H) histN++;
            prev.Clear(); foreach (var kv in cur) prev[kv.Key] = kv.Value; cur.Clear();
            prevTotal = frameTotal; frameTotal = 0;
        }
    }
}
