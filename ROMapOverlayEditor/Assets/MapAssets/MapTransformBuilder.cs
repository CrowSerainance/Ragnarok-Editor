using System;

namespace ROMapOverlayEditor.MapAssets
{
    public static class MapTransformBuilder
    {
        public static MapTransform Build(int imgW, int imgH, int gatW, int gatH)
        {
            if (imgW <= 0 || imgH <= 0) throw new ArgumentException("Invalid minimap size.");
            if (gatW <= 0 || gatH <= 0) throw new ArgumentException("Invalid GAT dimensions.");

            double pptX = (double)imgW / gatW;
            double pptY = (double)imgH / gatH;
            double ppt = Math.Min(pptX, pptY);

            ppt = Math.Max(1.0, Math.Min(64.0, ppt));

            double gridW = gatW * ppt;
            double gridH = gatH * ppt;

            double padX = (imgW - gridW) * 0.5;
            double padY = (imgH - gridH) * 0.5;

            if (padX < 0) padX = 0;
            if (padY < 0) padY = 0;

            return new MapTransform
            {
                GatWidthCells = gatW,
                GatHeightCells = gatH,
                ImageWidthPx = imgW,
                ImageHeightPx = imgH,
                PixelsPerTile = ppt,
                PadX = padX,
                PadY = padY,
                InvertY = true
            };
        }
    }
}
