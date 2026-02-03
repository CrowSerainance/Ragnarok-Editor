using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Controls;
using HelixToolkit.Wpf;

namespace ROMapOverlayEditor.Tools
{
    public static class HelixHitCompat
    {
        public static bool TryGetFirstHitPoint(Viewport3D viewport, Point mouse, out Point3D hit)
        {
            hit = default;

            if (viewport == null) return false;

            var hits = Viewport3DHelper.FindHits(viewport, mouse);
            if (hits == null || hits.Count == 0) return false;

            // pick nearest (smallest distance), then extract a hit-point with reflection
            var best = hits.OrderBy(h => GetDistanceSafe(h)).FirstOrDefault();
            if (best == null) return false;

            if (TryExtractPoint(best, out hit))
                return true;

            return false;
        }

        private static double GetDistanceSafe(object hit)
        {
            try
            {
                var p = hit.GetType().GetProperty("Distance", BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(double))
                    return (double)p.GetValue(hit)!;
            }
            catch { }
            return double.MaxValue;
        }

        private static bool TryExtractPoint(object hit, out Point3D point)
        {
            point = default;

            // HelixToolkit variants have used different property names; try the common ones.
            // - PointHit (often)
            // - Point (sometimes)
            // - Position (rare)
            var t = hit.GetType();
            foreach (var name in new[] { "PointHit", "Point", "Position" })
            {
                try
                {
                    var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) continue;

                    var v = p.GetValue(hit);
                    if (v is Point3D p3d)
                    {
                        point = p3d;
                        return true;
                    }

                    // nullable Point3D case
                    if (v != null && v.GetType().IsGenericType && v.GetType().GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        var hasValue = (bool)v.GetType().GetProperty("HasValue")!.GetValue(v)!;
                        if (hasValue)
                        {
                            point = (Point3D)v.GetType().GetProperty("Value")!.GetValue(v)!;
                            return true;
                        }
                    }
                }
                catch { }
            }

            return false;
        }
    }
}
