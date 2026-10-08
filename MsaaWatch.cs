using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;

namespace NepFix
{
    /// Следит за ошибкой Unity «multisampled texture being bound to a non-multisampled sampler»
    /// и сам подбирает для каждой камеры правильный способ чтения глубины.
    internal class MsaaWatch : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.Error | LogLevel.Warning;
        static int errors;
        public static int ErrorsPerSec;
        public static int ErrorsWhileOff;
        public static string Verdict = "";

        public void LogEvent(object sender, LogEventArgs e)
        {
            try
            {
                if (e.Level != LogLevel.Error) return;
                var m = e.Data as string ?? e.Data?.ToString();
                if (m != null && m.Contains("multisampled texture being bound")) Interlocked.Increment(ref errors);
            }
            catch { }
        }
        public void Dispose() { }

        public static void Install()
        {
            try { Logger.Listeners.Add(new MsaaWatch()); } catch (Exception e) { Plugin.L.LogWarning("MsaaWatch: " + e.Message); }
        }

        // камера -> инвертировать ли решение по MSAA; сколько раз уже пробовали
        static readonly Dictionary<string, bool> flip = new();
        static readonly Dictionary<string, int> tries = new();
        static readonly Dictionary<string, float> lastSeen = new();
        static float windowStart, lastFlip;
        static int quietWindows;

        /// Выученный способ чтения глубины помним между запусками, иначе каждый старт давал пачку ошибок MSAA.
        static bool loaded;
        static void Load()
        {
            if (loaded) return; loaded = true;
            try
            {
                foreach (var c in (Plugin.S.MsaaFlipCams.Value ?? "").Split('|'))
                    if (c.Length > 0) { flip[c] = true; tries[c] = 1; }
            }
            catch { }
        }
        static void Save()
        {
            try { var l = new List<string>(); foreach (var kv in flip) if (kv.Value) l.Add(kv.Key); Plugin.S.MsaaFlipCams.Value = string.Join("|", l); } catch { }
        }

        public static bool Flip(string cam) { Load(); lastSeen[cam] = UnityEngine.Time.unscaledTime; return flip.TryGetValue(cam, out bool f) && f; }

        /// Раз в кадр из Update.
        public static void Tick()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now - windowStart < 1f) return;
            windowStart = now;
            int n = Interlocked.Exchange(ref errors, 0);
            ErrorsPerSec = n;
            bool fxOn = Plugin.S.FxEnabled.Value;
            if (n == 0) { quietWindows++; if (quietWindows > 3 && Verdict.StartsWith("подбираю")) Verdict = "ошибок нет"; return; }
            quietWindows = 0;
            if (!fxOn) { ErrorsWhileOff += n; Verdict = "ошибки идут при выключенном NepFX, их вызывает сама игра"; return; }
            if (now - lastFlip < 2f) return;

            // камера, которую NepFX обрабатывал последней и которую ещё не перебрали
            string pick = null; float best = -1;
            foreach (var kv in lastSeen)
            {
                if (now - kv.Value > 1.5f) continue;
                tries.TryGetValue(kv.Key, out int t);
                if (t >= 2) continue;
                if (kv.Value > best) { best = kv.Value; pick = kv.Key; }
            }
            if (pick == null) { Verdict = "перебрал оба способа, ошибки не от чтения глубины NepFX"; return; }
            flip.TryGetValue(pick, out bool f);
            flip[pick] = !f;
            tries[pick] = (tries.TryGetValue(pick, out int tt) ? tt : 0) + 1;
            lastFlip = now;
            Save();
            Verdict = $"подбираю чтение глубины для камеры {pick}";
            Plugin.L.LogInfo($"NepFX: ошибки MSAA {n}/с, для камеры '{pick}' переключено чтение глубины, попытка {tries[pick]}");
        }
    }
}
