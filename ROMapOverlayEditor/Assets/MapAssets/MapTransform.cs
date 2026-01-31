using System;

namespace ROMapOverlayEditor.MapAssets
{
    public sealed class MapTransform
    {
        public int GatWidthCells { get; init; }
        public int GatHeightCells { get; init; }

        public int ImageWidthPx { get; init; }
        public int ImageHeightPx { get; init; }

        public double PixelsPerTile { get; init; }

        public double PadX { get; init; }
        public double PadY { get; init; }

        public bool InvertY { get; init; } = true;

        public (double px, double py) TileToPixelCenter(int tileX, int tileY)
        {
            double x = (tileX + 0.5) * PixelsPerTile + PadX;

            double yTile = (tileY + 0.5);
            double y = InvertY
                ? (GatHeightCells - yTile) * PixelsPerTile + PadY
                : yTile * PixelsPerTile + PadY;

            return (x, y);
        }

        public bool IsSane()
        {
            if (GatWidthCells <= 0 || GatHeightCells <= 0) return false;
            if (ImageWidthPx <= 0 || ImageHeightPx <= 0) return false;
            if (PixelsPerTile <= 0.25 || PixelsPerTile > 128) return false;
            return true;
        }

        public override string ToString()
            => $"GAT={GatWidthCells}x{GatHeightCells}, IMG={ImageWidthPx}x{ImageHeightPx}, ppt={PixelsPerTile:0.###}, pad=({PadX:0.#},{PadY:0.#}), invertY={InvertY}";
    }
}
