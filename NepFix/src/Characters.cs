using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NepFix
{
    /// Настройки моделей персонажей (Unity Toon Shader): толщина контура, окружающий свет, тени.
    /// Обработка постепенная: новые модели обрабатываются по мере появления, уже обработанные не трогаются,
    /// пока не изменятся настройки. Раньше при каждой смене состава сцены все модели обрабатывались заново (фризы 27–55 мс).
    internal static class Chars
    {
        static Settings S => Plugin.S;
        static int idWidth = -1, idGi = -1, idFilter = -1;

        /// Состояние материала: исходное значение игры и то, что выставил мод. Если текущее значение не совпадает
        /// с выставленным, его поменяла игра (или это новый материал на том же адресе), и оно становится исходным.
        class Prop { public float orig = float.NaN, set = float.NaN; }
        class MatRec { public Material m; public Prop width, gi, filter; }
        static readonly Dictionary<IntPtr, MatRec> mats = new();

        class RendRec { public SkinnedMeshRenderer r; public int subs; public bool toon; public IntPtr[] matPtrs; public MatRec[] m; }
        /// Исходные настройки теней рендерера. Хранятся отдельно от списка сцены: рендерер, ушедший и вернувшийся,
        /// иначе записался бы заново уже с тенями, включёнными модом.
        class ShadowRec { public SkinnedMeshRenderer r; public ShadowCastingMode cast; public bool recv, set; }
        static readonly Dictionary<IntPtr, ShadowRec> shadows = new();
        static float verifyT;
        static readonly Dictionary<IntPtr, RendRec> rends = new();

        public static int FilterFound, FilterOrigOn;
        static bool dumped;
        public static float GiMin = -1, GiMax = -1;
        /// Рендереры персонажей (материалы Toon) для маски NepFX.
        public static List<(Renderer r, int subs)> Renderers = new();
        public static int Count => mats.Count;
        public static float AutoOutline => Math.Clamp(Screen.height / 1080f, 1f, 2.5f);

        static long lastSig;
        static float lastQuick;
        static bool dirty = true, hooked;
        static int lastHeight;

        static void HookSettings()
        {
            if (hooked) return; hooked = true;
            EventHandler h = (_, _) => dirty = true;
            S.OutlineWidth.SettingChanged += h; S.CharAmbient.SettingChanged += h;
            S.CharLightLimit.SettingChanged += h; S.CharacterShadows.SettingChanged += h;
        }

        /// Раз в 0,2 с: появилась ли новая модель (смена героини в меню, подгрузка отряда).
        /// Сравниваются только указатели, без создания объектов-обёрток.
        public static void Quick()
        {
            float now = Time.unscaledTime;
            if (now - lastQuick < 0.2f) return;
            lastQuick = now;
            HookSettings();
            int hgt = Screen.height;
            if (hgt != lastHeight) { lastHeight = hgt; if (S.OutlineWidth.Value <= 0.001f) dirty = true; }
            var arr = Scan.All<SkinnedMeshRenderer>();
            if (arr == null) return;
            int n = arr.Length;
            long sig = n;
            for (int i = 0; i < n; i++) sig = sig * 31 + Scan.Raw(arr, i).ToInt64();
            // игра может сменить материалы у той же модели (костюм, превращение): изредка сверяем их
            bool verify = now - verifyT > 3f;
            if (sig == lastSig && !dirty && !verify) return;
            if (verify) verifyT = now;
            lastSig = sig;
            Sync(arr, verify);
        }

        public static void Apply() { dirty = true; lastQuick = -1; }

        static readonly HashSet<IntPtr> alive = new();
        static void Sync(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> arr, bool verify)
        {
            if (idWidth < 0) { idWidth = Shader.PropertyToID("_Outline_Width"); idGi = Shader.PropertyToID("_GI_Intensity"); idFilter = Shader.PropertyToID("_Is_Filter_LightColor"); }
            bool all = dirty; dirty = false;
            alive.Clear();
            int n = arr.Length;
            for (int i = 0; i < n; i++)
            {
                IntPtr p = Scan.Raw(arr, i);
                if (p == IntPtr.Zero) continue;
                alive.Add(p);
                if (rends.TryGetValue(p, out var rec))
                {
                    bool changed = false;
                    if (verify) try { changed = MaterialsChanged(rec); } catch { }
                    if (!changed)
                    {
                        if (all && rec.toon) try { ApplyRenderer(rec); } catch { }
                        continue;
                    }
                }
                try
                {
                    var r = rec != null ? rec.r : arr[i]?.TryCast<SkinnedMeshRenderer>();
                    if (r == null) continue;
                    rec = new RendRec { r = r };
                    var sm = r.sharedMaterials;
                    if (sm != null)
                    {
                        rec.subs = sm.Length;
                        rec.matPtrs = new IntPtr[sm.Length];
                        var list = new List<MatRec>();
                        for (int j = 0; j < sm.Length; j++)
                        {
                            rec.matPtrs[j] = Scan.Raw(sm, j);
                            var m = sm[j];
                            if (m == null || !m.HasProperty(idWidth)) continue;
                            if (!mats.TryGetValue(m.Pointer, out var mr))
                            {
                                mr = new MatRec { m = m, width = new Prop() };
                                if (m.HasProperty(idGi)) mr.gi = new Prop();
                                if (m.HasProperty(idFilter)) mr.filter = new Prop();
                                mats[m.Pointer] = mr;
                                if (!dumped) { dumped = true; Dump(m); }
                            }
                            list.Add(mr);
                        }
                        rec.m = list.ToArray();
                        rec.toon = rec.m.Length > 0;
                    }
                    if (rec.toon)
                    {
                        if (!shadows.ContainsKey(p)) shadows[p] = new ShadowRec { r = r, cast = r.shadowCastingMode, recv = r.receiveShadows };
                        ApplyRenderer(rec);
                    }
                    rends[p] = rec;
                }
                catch { }
            }
            // ушедшие со сцены рендереры
            if (rends.Count != alive.Count)
            {
                var dead = new List<IntPtr>();
                foreach (var k in rends.Keys) if (!alive.Contains(k)) dead.Add(k);
                foreach (var k in dead) rends.Remove(k);
            }
            if (shadows.Count > 256) { var dd = new List<IntPtr>(); foreach (var kv in shadows) if (kv.Value.r == null) dd.Add(kv.Key); foreach (var k in dd) shadows.Remove(k); }
            // материалы уничтоженных моделей
            if (mats.Count > 512)
            {
                var dead = new List<IntPtr>();
                foreach (var kv in mats) if (kv.Value.m == null) dead.Add(kv.Key);
                foreach (var k in dead) mats.Remove(k);
            }
            var outList = new List<(Renderer, int)>();
            float gmin = 9, gmax = -1; int ff = 0, fon = 0;
            foreach (var rec in rends.Values) if (rec.toon) outList.Add((rec.r, rec.subs));
            foreach (var mr in mats.Values)
            {
                if (mr.gi != null && !float.IsNaN(mr.gi.orig)) { gmin = Math.Min(gmin, mr.gi.orig); gmax = Math.Max(gmax, mr.gi.orig); }
                if (mr.filter != null && !float.IsNaN(mr.filter.orig)) { ff++; if (mr.filter.orig > 0.5f) fon++; }
            }
            if (gmax >= 0) { GiMin = gmin; GiMax = gmax; }
            if (ff > 0) { FilterFound = ff; FilterOrigOn = fon; }
            Renderers = outList;
        }

        static void ApplyRenderer(RendRec rec)
        {
            float mul = S.OutlineWidth.Value <= 0.001f ? AutoOutline : S.OutlineWidth.Value;
            float giWant = S.CharAmbient.Value;
            bool limit = S.CharLightLimit.Value;
            foreach (var mr in rec.m)
            {
                var m = mr.m;
                if (m == null) continue;
                IntPtr k = m.Pointer;
                Set(m, k, idWidth, mr.width, o => o <= 0f ? o : o * mul);
                if (mr.gi != null) Set(m, k, idGi, mr.gi, o => giWant < 0 ? o : giWant);
                if (mr.filter != null) Set(m, k, idFilter, mr.filter, o => limit ? 1f : o);
            }
            var r = rec.r;
            if (r == null || !shadows.TryGetValue(r.Pointer, out var sh)) return;
            if (S.CharacterShadows.Value)
            {
                if (r.shadowCastingMode != ShadowCastingMode.On) r.shadowCastingMode = ShadowCastingMode.On;
                if (!r.receiveShadows) r.receiveShadows = true;
                sh.set = true;
            }
            else if (sh.set)
            {
                // выключили настройку: возвращаем как было у игры
                r.shadowCastingMode = sh.cast; r.receiveShadows = sh.recv; sh.set = false;
            }
        }

        static bool MaterialsChanged(RendRec rec)
        {
            var sm = rec.r.sharedMaterials;
            if (sm == null) return rec.matPtrs != null;
            if (rec.matPtrs == null || sm.Length != rec.matPtrs.Length) return true;
            for (int j = 0; j < sm.Length; j++) if (Scan.Raw(sm, j) != rec.matPtrs[j]) return true;
            return false;
        }

        static void Set(Material m, IntPtr k, int id, Prop p, Func<float, float> want)
        {
            float cur = ICalls.MaterialGetFloat(k, id);
            if (float.IsNaN(cur)) return;
            if (float.IsNaN(p.set) || Math.Abs(cur - p.set) > 1e-5f) p.orig = cur;
            float w = want(p.orig);
            if (Math.Abs(cur - w) > 1e-5f) m.SetFloat(id, w);
            p.set = w;
        }

        /// Один раз пишет в лог свойства шейдера персонажей, чтобы знать, какие настройки у него есть.
        static void Dump(Material m)
        {
            try
            {
                var sh = m.shader;
                int n = sh.GetPropertyCount();
                var sb = new System.Text.StringBuilder($"Персонажи: шейдер '{sh.name}', свойств {n}:");
                for (int i = 0; i < n; i++)
                {
                    string name = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range)
                        sb.Append($" {name}={ICalls.MaterialGetFloat(m.Pointer, Shader.PropertyToID(name)):0.##}");
                }
                Plugin.L.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.L.LogInfo("Персонажи: свойства шейдера не прочитаны, " + e.Message); }
        }
    }
}
