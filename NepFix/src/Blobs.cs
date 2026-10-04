using System;
using System.Collections.Generic;
using UnityEngine;

namespace NepFix
{
    /// Точки на полу под персонажами для мягкой тени в помещениях.
    internal static class Blobs
    {
        public static readonly Vector4[] Data = new Vector4[8];
        public static int Count;
        public static string Error = "";
        static readonly List<Bounds> clusters = new();

        public static void Update()
        {
            Count = 0;
            clusters.Clear();
            try
            {
                // части одной героини (тело, волосы, лицо) объединяем по близости на плоскости
                foreach (var (r, _) in Chars.Renderers)
                {
                    if (r == null || !r.isVisible) continue;
                    var b = r.bounds;
                    bool merged = false;
                    for (int i = 0; i < clusters.Count; i++)
                    {
                        var c = clusters[i];
                        var d = new Vector2(c.center.x - b.center.x, c.center.z - b.center.z);
                        if (d.magnitude < 0.5f) { c.Encapsulate(b); clusters[i] = c; merged = true; break; }
                    }
                    if (!merged) clusters.Add(b);
                }
                foreach (var b in clusters)
                {
                    if (Count >= Data.Length) break;
                    if (b.size.y < 0.3f) continue;
                    var c = b.center;
                    float floor = b.min.y;
                    if (Physics.Raycast(new Vector3(c.x, b.min.y + 0.5f, c.z), Vector3.down, out RaycastHit hit, 2f)) floor = hit.point.y;
                    float rad = Math.Clamp(Math.Max(b.extents.x, b.extents.z) * 0.8f, 0.3f, 0.6f);
                    Data[Count++] = new Vector4(c.x, floor, c.z, rad);
                }
                Error = "";
            }
            catch (Exception e) { Error = e.Message; }
        }
    }
}
