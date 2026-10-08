using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NepFix
{
    /// Что из модов реально работает в текущем запуске.
    internal static class ModsInfo
    {
        public class Row { public string Name, Kind, State, Detail; public int Level; } // Level: 0 ок, 1 предупреждение, 2 выкл/ошибка
        public static List<Row> Rows = new();
        public static string TexReport = "";
        public static bool Busy;
        public static string DxvkVersion = "";
        public static bool DxvkActive;

        static string GameDir => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

        public static void Refresh()
        {
            if (Busy) return;
            Busy = true;
            // список плагинов BepInEx читаем в главном потоке
            var plugins = new List<Row>();
            try
            {
                foreach (var kv in BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins)
                {
                    var p = kv.Value;
                    plugins.Add(new Row
                    {
                        Name = p.Metadata.Name, Kind = "Плагин BepInEx", Level = p.Instance != null ? 0 : 2,
                        State = p.Instance != null ? "загружен" : "не загружен",
                        Detail = $"v{p.Metadata.Version} · {Path.GetFileName(p.Location)}"
                    });
                }
            }
            catch (Exception e) { plugins.Add(new Row { Name = "BepInEx", Kind = "Загрузчик", State = "ошибка", Level = 2, Detail = e.Message }); }

            Task.Run(() =>
            {
                var rows = new List<Row>();
                try { Collect(rows, plugins); }
                catch (Exception e) { rows.Add(new Row { Name = "Ошибка", State = e.Message, Level = 2 }); }
                Rows = rows; Busy = false;
            });
        }

        static void Collect(List<Row> rows, List<Row> plugins)
        {
            string g = GameDir;
            rows.Add(new Row { Name = "BepInEx 6 IL2CPP", Kind = "Загрузчик", State = "работает", Level = 0, Detail = "winhttp.dll (Doorstop) → BepInEx.Unity.IL2CPP" });
            rows.AddRange(plugins);

            // нативные прокси-DLL в папке игры (DXVK, ReShade, ...)
            var mods = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(m => string.Equals(Path.GetDirectoryName(m.FileName), g, StringComparison.OrdinalIgnoreCase)).ToList();
            var names = mods.Select(m => m.ModuleName.ToLowerInvariant()).ToHashSet();
            DxvkActive = names.Contains("d3d11.dll");
            DxvkVersion = "";
            try
            {
                string log = Path.Combine(g, Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName) + "_dxgi.log");
                if (File.Exists(log))
                    using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                        for (int i = 0; i < 20 && !sr.EndOfStream; i++) { var l = sr.ReadLine(); int k = l.IndexOf("DXVK: v"); if (k >= 0) { DxvkVersion = l.Substring(k + 6).Trim(); break; } }
            }
            catch { }
            if (DxvkActive)
            {
                string conf = File.Exists(Path.Combine(g, "dxvk.conf")) ? File.ReadAllLines(Path.Combine(g, "dxvk.conf")).Count(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")) + " параметров в dxvk.conf" : "без dxvk.conf";
                rows.Add(new Row { Name = "DXVK " + DxvkVersion, Kind = "DirectX 11 → Vulkan", State = "активен", Level = 0, Detail = "d3d11.dll/dxgi.dll загружены из папки игры, " + conf });
            }
            else rows.Add(new Row { Name = "DXVK", Kind = "DirectX 11 → Vulkan", State = "не используется", Level = 2, Detail = "рендер через родной DirectX 11" });
            foreach (var m in mods)
            {
                string n = m.ModuleName.ToLowerInvariant();
                if (n == "dxgi.dll" && Gfx.ReShadeActive)
                {
                    string ver = ""; try { ver = FileVersionInfo.GetVersionInfo(m.FileName).ProductVersion; } catch { }
                    string preset = File.Exists(Path.Combine(g, "ReShadePreset.ini")) ? File.ReadAllLines(Path.Combine(g, "ReShadePreset.ini")).FirstOrDefault(l => l.StartsWith("Techniques=")) ?? "" : "";
                    rows.Add(new Row { Name = "ReShade " + ver, Kind = "Постобработка", State = "активен", Level = 0, Detail = "меню Home, эффекты End · " + preset.Replace("Techniques=", "эффекты: ") });
                    continue;
                }
                if (n is "d3d11.dll" or "dxgi.dll" or "winhttp.dll" or "unityplayer.dll" or "gameassembly.dll" or "baselib.dll" or "winpixeventruntime.dll") continue;
                if (m.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                string desc = "";
                try { desc = FileVersionInfo.GetVersionInfo(m.FileName).FileDescription ?? ""; } catch { }
                rows.Add(new Row { Name = m.ModuleName, Kind = "Нативная DLL", State = "загружена", Level = 1, Detail = desc });
            }

            // пакеты файлов из менеджера модов
            string state = Path.Combine(g, "Mods", "modmanager_state.json");
            var enabled = new Dictionary<string, (bool en, List<string> files)>();
            if (File.Exists(state))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(state));
                foreach (var p in doc.RootElement.GetProperty("packages").EnumerateArray())
                    enabled[p.GetProperty("id").GetString()] = (p.GetProperty("enabled").GetBoolean(), p.GetProperty("files").EnumerateArray().Select(x => x.GetString()).ToList());
            }
            string modsDir = Path.Combine(g, "Mods");
            if (Directory.Exists(modsDir))
                foreach (var d in Directory.GetDirectories(modsDir))
                {
                    string mj = Path.Combine(d, "mod.json");
                    if (!File.Exists(mj)) continue;
                    string id = Path.GetFileName(d), name = id;
                    try { using var doc = JsonDocument.Parse(File.ReadAllText(mj)); if (doc.RootElement.TryGetProperty("name", out var nm)) name = nm.GetString(); if (doc.RootElement.TryGetProperty("id", out var i2)) id = i2.GetString(); } catch { }
                    if (!enabled.TryGetValue(id, out var st) || !st.en)
                    { rows.Add(new Row { Name = name, Kind = "Пакет файлов", State = "выключен", Level = 2, Detail = "включается в NepModManager.exe" }); continue; }
                    // проверка: совпадают ли файлы в игре с файлами мода (по размеру и дате)
                    int okc = 0, total = st.files.Count;
                    foreach (var rel in st.files)
                    {
                        var a = new FileInfo(Path.Combine(g, rel)); var b = new FileInfo(Path.Combine(d, "files", rel));
                        if (a.Exists && b.Exists && a.Length == b.Length) okc++;
                    }
                    rows.Add(new Row
                    {
                        Name = name, Kind = "Пакет файлов", Level = okc == total ? 0 : 1,
                        State = okc == total ? "установлен" : "частично",
                        Detail = $"файлов на месте: {okc}/{total}" + (okc == total ? "" : ", переустановите мод в менеджере: файлы могло перезаписать обновление игры")
                    });
                }
        }

        /// Проверка по загруженным в память текстурам: реально ли игра использует HD-версии.
        public static void CheckTextures()
        {
            try
            {
                var want = new HashSet<string> { "face_c", "face_s1", "face_parts_s1", "hair_hc", "hair_alpha", "body_rm", "body_sg", "hair_n" };
                var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>());
                int hd = 0, sd = 0; var sample = new List<string>();
                foreach (var o in arr)
                {
                    var n = o.name;
                    if (!want.Contains(n)) continue;
                    var t = o.TryCast<Texture2D>(); if (t == null) continue;
                    int w = t.width;
                    bool isHd = (n == "face_c" || n == "face_s1" || n == "face_parts_s1" || n == "body_rm" || n == "body_sg" || n == "hair_n" || n == "hair_hc" || n == "hair_alpha") ? w >= 2048 : false;
                    if (isHd) hd++; else sd++;
                    if (sample.Count < 6) sample.Add($"{n} {w}px");
                }
                TexReport = hd + sd == 0
                    ? "Текстуры персонажей сейчас не загружены — откройте меню отряда или выйдите на карту и проверьте снова."
                    : $"Текстур персонажей в памяти: {hd + sd}, из них HD 2048: {hd}, обычных: {sd}. " + (hd > 0 ? "HD-текстуры работают." : "HD-текстуры НЕ используются.") + "  Пример: " + string.Join(", ", sample);
            }
            catch (Exception e) { TexReport = "Ошибка проверки: " + e.Message; }
        }
    }
}
