using System;
using System.Collections.Generic;
using UnityEngine;

namespace NepFix
{
    /// Оверлей мониторинга (рисуется через IMGUI самой игры).
    internal static class Hud
    {
        static Settings S => Plugin.S;

        static string Gb(double mb) => mb < 0 ? "—" : (mb / 1024.0).ToString("0.0");
        static string V(float v, string fmt, string unit = "") => v < 0 ? "—" : v.ToString(fmt) + unit;

        public static string Api
        {
            get
            {
                if (ModsInfo.DxvkActive || Gfx.Backend.Contains("DXVK"))
                    return "Vulkan (DXVK" + (string.IsNullOrEmpty(ModsInfo.DxvkVersion) ? "" : " " + ModsInfo.DxvkVersion) + ")";
                return "Direct3D 11";
            }
        }

        static string AaText()
        {
            string msaa = S.Msaa.Value > 1 ? $"MSAA {S.Msaa.Value}x" : "без MSAA";
            string post = S.PostAa.Value switch
            {
                PostAA.Off => "", PostAA.FXAA => " + FXAA", PostAA.SMAA_Low => " + SMAA низк.", PostAA.SMAA_Medium => " + SMAA сред.", _ => " + SMAA выс."
            };
            return msaa + post;
        }

        public static List<string> Lines(bool full)
        {
            var l = new List<string>();
            var gname = string.IsNullOrEmpty(Telemetry.GpuName) ? SafeGpuName() : Telemetry.GpuName;
            if (!full)
            {
                l.Add($"{Api} | {FrameStats.Fps:0} FPS {FrameStats.AvgMs:0.0} мс | GPU {V(Telemetry.GpuLoad, "0", "%")} {V(Telemetry.GpuTemp, "0", "°C")} | VRAM {Gb(Telemetry.VramUsedMB)} ГБ | CPU {V(Telemetry.CpuLoad, "0", "%")}");
                return l;
            }
            l.Add($"NepFix {Plugin.Version} · API: {Api} · {Screen.width}×{Screen.height} @ {Gfx.Refresh} Гц");
            l.Add($"FPS {FrameStats.Fps:0}, цель {Gfx.TargetFps}{(S.VSync.Value ? ", VSync" : "")} · кадр {FrameStats.AvgMs:0.0} мс · 1% low {FrameStats.Low1:0} · мин/макс {FrameStats.MinMs:0.0}/{FrameStats.MaxMs:0.0} мс · ограничивает: {Optimize.Bottleneck()}");
            l.Add($"GPU {gname} · {V(Telemetry.GpuLoad, "0", "%")} · {V(Telemetry.GpuClock, "0", " МГц")} · {V(Telemetry.GpuTemp, "0", " °C")} · {V(Telemetry.GpuPower, "0")}/{V(Telemetry.GpuPowerLimit, "0", " Вт")} · вент. {V(Telemetry.GpuFan, "0", "%")}");
            l.Add($"VRAM {Gb(Telemetry.VramUsedMB)} / {Gb(Telemetry.VramTotalMB)} ГБ, игра {Gb(Telemetry.VramGameMB)} ГБ · шина памяти {V(Telemetry.GpuMemLoad, "0", "%")} · {V(Telemetry.GpuMemClock, "0", " МГц")}");
            l.Add($"CPU {Short(Telemetry.CpuName)} · {V(Telemetry.CpuLoad, "0", "%")}, игра {V(Telemetry.CpuGame, "0", "%")} · потоков игры {Telemetry.GameThreads} · ядер {Telemetry.CpuThreads}");
            l.Add($"RAM {Gb(Telemetry.RamUsedMB)} / {Gb(Telemetry.RamTotalMB)} ГБ, игра {Gb(Telemetry.RamGameMB)} ГБ, личная память {Gb(Telemetry.RamGamePrivateMB)} ГБ");
            l.Add($"Рендер: профиль {QualityName()} · масштаб {Gfx.EffectiveScale:0.00}{(S.FsrPreset.Value > 0 ? " FSR " + Gfx.PresetName[S.FsrPreset.Value] : Gfx.EffectiveScale < 1 ? " " + S.UpscaleFilter.Value : "")} · {AaText()} · тени {S.ShadowResolution.Value} · LOD {S.LodBias.Value:0.0}");
            float sf = -1; try { sf = GameTime.FrameRateSpeedFactor; } catch { }
            l.Add($"Игра: множитель времени {V(sf, "0.00")} · тайминг {(S.AdaptiveFrameTiming.Value ? "по реальному кадру" : "игры")} · драйвер {(string.IsNullOrEmpty(Telemetry.DriverVersion) ? "—" : Telemetry.DriverVersion)} · {DateTime.Now:HH:mm}");
            return l;
        }

        static string QualityName()
        {
            int q = ICalls.QualityLevel();
            return q switch { 0 => "Normal", 1 => "Low", 2 => "Medium", 3 => "High", 4 => "Ultra", _ => "—" };
        }

        static string gpuFallback;
        static string SafeGpuName()
        {
            if (gpuFallback != null) return gpuFallback;
            try { gpuFallback = SystemInfo.graphicsDeviceName; } catch { gpuFallback = "—"; }
            return gpuFallback;
        }

        static string Short(string cpu)
        {
            if (string.IsNullOrEmpty(cpu)) return "—";
            foreach (var junk in new[] { "(R)", "(TM)", " CPU", " Processor", "-Core" }) cpu = cpu.Replace(junk, "");
            int at = cpu.IndexOf(" @"); if (at > 0) cpu = cpu.Substring(0, at);
            return cpu.Length > 34 ? cpu.Substring(0, 34) : cpu;
        }

        static List<string> cache; static int cacheMode; static float cacheT;

        public static void Draw(float scale, int mode)
        {
            if (cache == null || cacheMode != mode || Time.unscaledTime - cacheT > 0.25f) { cache = Lines(mode >= 2); cacheMode = mode; cacheT = Time.unscaledTime; }
            var lines = cache;
            float lh = 20, gh = mode >= 3 ? 60 : 0;
            int maxLen = 0; foreach (var s in lines) maxLen = Math.Max(maxLen, s.Length);
            float w = Math.Max(300, maxLen * 6.9f + 20);
            float h = lines.Count * lh + 10 + (gh > 0 ? gh + 8 : 0);
            float sw = Screen.width / scale, sh = Screen.height / scale;
            int c = S.HudCorner.Value;
            float x = (c == 1 || c == 3) ? sw - w - 8 : 8;
            float y = (c >= 2) ? sh - h - 8 : 8;

            var old = GUI.color;
            var tex = Texture2D.whiteTexture;
            GUI.color = new Color(0, 0, 0, Math.Clamp(S.HudOpacity.Value, 0f, 1f));
            GUI.DrawTexture(new Rect(x, y, w, h), tex);
            GUI.color = old;

            for (int i = 0; i < lines.Count; i++)
            {
                if (i == 1 || mode == 1) GUI.color = FpsColor();
                GUI.Label(new Rect(x + 8, y + 5 + i * lh, w - 12, lh + 2), lines[i]);
                GUI.color = old;
            }

            if (gh > 0)
            {
                float gy = y + 5 + lines.Count * lh + 4, gx = x + 8, gw = w - 16;
                GUI.color = new Color(1, 1, 1, 0.08f);
                GUI.DrawTexture(new Rect(gx, gy, gw, gh), tex);
                float target = 1000f / Math.Max(30, Gfx.TargetFps);
                float scaleMs = Math.Max(target * 2.5f, 20f);
                // линия цели
                GUI.color = new Color(0.4f, 0.8f, 1f, 0.6f);
                GUI.DrawTexture(new Rect(gx, gy + gh - gh * target / scaleMs, gw, 1), tex);
                int n = FrameStats.N; float bw = gw / n;
                for (int i = 0; i < n; i++)
                {
                    float ms = FrameStats.Ms[(FrameStats.Head + i) % n];
                    if (ms <= 0) continue;
                    float bh = Math.Min(gh, gh * ms / scaleMs);
                    GUI.color = ms <= target * 1.15f ? new Color(0.45f, 1f, 0.45f, 0.9f) : ms <= target * 2f ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1f, 0.35f, 0.35f, 0.95f);
                    GUI.DrawTexture(new Rect(gx + i * bw, gy + gh - bh, Math.Max(1, bw), bh), tex);
                }
                GUI.color = old;
                GUI.Label(new Rect(gx + 4, gy, 300, 20), $"время кадра, шкала {scaleMs:0} мс");
            }
        }

        static Color FpsColor()
        {
            float r = FrameStats.Fps / Math.Max(1, Gfx.TargetFps);
            return r >= 0.95f ? new Color(0.55f, 1f, 0.55f, 1f) : r >= 0.75f ? new Color(1f, 0.9f, 0.4f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
        }
    }
}
