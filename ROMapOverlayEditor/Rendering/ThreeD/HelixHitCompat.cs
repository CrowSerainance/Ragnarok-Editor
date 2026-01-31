//
// HelixHitCompat.cs — HelixToolkit HitResult uses Position, not PointHit
//
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace ROMapOverlayEditor.ThreeD
{
    public static class HelixHitCompat
    {
        public static bool TryGetFirstHitPoint(Viewport3D viewport, System.Windows.Point screenPoint, out Point3D hitPoint)
        {
            hitPoint = new Point3D();

            IList<Viewport3DHelper.HitResult> hits = Viewport3DHelper.FindHits(viewport, screenPoint);
            if (hits == null || hits.Count == 0) return false;

            hitPoint = hits[0].Position;
            return true;
        }
    }
}
