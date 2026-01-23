using System;

namespace ROMapOverlayEditor.Gat
{
    public static class GatPainter
    {
        public static void PaintCircle(GatFile gf, int cx, int cy, int radius, GatCellType type)
        {
            radius = Math.Max(0, radius);
            int r2 = radius * radius;

            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (!gf.InBounds(x, y)) continue;

                    int dx = x - cx;
                    int dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;

                    gf.SetType(x, y, type);
                }
            }
        }
    }
}
