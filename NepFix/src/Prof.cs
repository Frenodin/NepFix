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
        static readonly Stopwatch sw = new();
        static int gcNet = -1; static long usedIl = -1;
        static readonly Queue<float> hist = new();
        public static int Spikes;
        public static string Last = "";

        public static void Run(string name, Action a)
        {
            long t0 = Stopwatch.GetTimestamp();
            try { a(); } finally
            {
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                cur.TryGetValue(name, out double v); cur[name] = v + ms;
            }
        }

        /// В начале Update: dt прошлого кадра.
        public static void Frame(float dt)
        {
            float ms = dt * 1000f;
            float med = hist.Count > 10 ? hist.OrderBy(x => x).ElementAt(hist.Count / 2) : ms;
            int gn = System.GC.CollectionCount(0); long ui = -1;
            try { ui = Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_get_used_size(); } catch { }
            bool ilGc = usedIl > 0 && ui >= 0 && ui < usedIl - 4 * 1048576;
            bool spike = hist.Count > 30 && ms > 35f && ms > med * 2.5f;
            if (spike && Plugin.S.VerboseLog.Value)
            {
                Spikes++;
                string work(Dictionary<string, double> d) => d.Count == 0 ? "нет" : string.Join(", ", d.Where(kv => kv.Value >= 0.5).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0.0}"));
                Last = $"Фриз {ms:0} мс при обычном кадре {med:0.0} мс. Сборка мусора: мод {(gcNet >= 0 && gn != gcNet ? "да" : "нет")}, игра {(ilGc ? $"да, освобождено {(usedIl - ui) / 1048576} МБ" : "нет")}, куча игры {ui / 1048576} МБ. Работа мода в кадре фриза, мс: {work(cur)}. В кадре до него: {work(prev)}.";
                Plugin.L.LogInfo(Last);
            }
            gcNet = gn; usedIl = ui;
            hist.Enqueue(ms); while (hist.Count > 120) hist.Dequeue();
            prev.Clear(); foreach (var kv in cur) prev[kv.Key] = kv.Value; cur.Clear();
        }
    }
}
