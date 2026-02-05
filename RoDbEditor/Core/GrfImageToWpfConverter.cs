using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GRF.Image;
using Utilities;

namespace RoDbEditor.Core;

/// <summary>
/// Converts GRF GrfImage to WPF BitmapSource. Register with ImageConverterManager at startup.
/// </summary>
public class GrfImageToWpfConverter : AbstractImageConverter
{
    private static readonly object[] ReturnTypesArray = { typeof(BitmapSource), typeof(ImageSource) };

    public override object[] ReturnTypes => ReturnTypesArray;

    public override object Convert(GrfImage image)
    {
        switch (image.GrfImageType)
        {
            case GrfImageType.Bgra32:
                return ToBgra32(image);
            case GrfImageType.Bgr32:
                return ToBgr32(image);
            case GrfImageType.Bgr24:
                return ToBgr24(image);
            case GrfImageType.Indexed8:
                return ToIndexed8(image);
            case GrfImageType.NotEvaluated:
            case GrfImageType.NotEvaluatedBmp:
            case GrfImageType.NotEvaluatedPng:
            case GrfImageType.NotEvaluatedJpg:
                return ReadAsCommonFormat(image);
            default:
                return ReadAsCommonFormat(image);
        }
    }

    public override GrfImage ConvertToSelf(GrfImage image)
    {
        if (image.GrfImageType is GrfImageType.Bgra32 or GrfImageType.Bgr32 or GrfImageType.Bgr24 or GrfImageType.Indexed8)
            return image;
        var bit = (BitmapSource)ReadAsCommonFormat(image);
        return image; // Self conversion not needed for browser
    }

    private static WriteableBitmap ToBgra32(GrfImage image)
    {
        var bit = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);
        bit.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);
        bit.Freeze();
        return bit;
    }

    private static WriteableBitmap ToBgr32(GrfImage image)
    {
        var bit = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null);
        bit.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);
        bit.Freeze();
        return bit;
    }

    private static WriteableBitmap ToBgr24(GrfImage image)
    {
        var bit = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgr24, null);
        bit.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 3, 0);
        bit.Freeze();
        return bit;
    }

    private static BitmapSource ToIndexed8(GrfImage image)
    {
        var colors = LoadColors(image.Palette);
        if (Methods.CanUseIndexed8)
        {
            var bit = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Indexed8, new BitmapPalette(colors));
            bit.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width, 0);
            bit.Freeze();
            return bit;
        }
        return ToBgra32FromIndexed8(image.Pixels, colors, image.Width, image.Height);
    }

    private static List<System.Windows.Media.Color> LoadColors(byte[]? palette)
    {
        if (palette == null)
            return new List<System.Windows.Media.Color>(256);
        var colors = new List<System.Windows.Media.Color>(256);
        for (int i = 0, count = Math.Min(palette.Length, 256 * 4); i < count; i += 4)
        {
            if (i + 3 < palette.Length)
                colors.Add(System.Windows.Media.Color.FromArgb(palette[i + 3], palette[i], palette[i + 1], palette[i + 2]));
        }
        while (colors.Count < 256)
            colors.Add(System.Windows.Media.Color.FromArgb(255, 0, 0, 0));
        return colors;
    }

    private static WriteableBitmap ToBgra32FromIndexed8(byte[] frameData, IList<System.Windows.Media.Color> colors, int width, int height)
    {
        var newData = new byte[width * height * 4];
        for (int j = 0; j < frameData.Length && j < width * height; j++)
        {
            var c = colors.Count > frameData[j] ? colors[frameData[j]] : System.Windows.Media.Colors.Transparent;
            int index = j * 4;
            newData[index] = c.B;
            newData[index + 1] = c.G;
            newData[index + 2] = c.R;
            newData[index + 3] = c.A;
        }
        var bit = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bit.WritePixels(new Int32Rect(0, 0, width, height), newData, width * 4, 0);
        bit.Freeze();
        return bit;
    }

    private static BitmapSource ReadAsCommonFormat(GrfImage image)
    {
        if (image.Pixels.Length > 4)
        {
            if (Methods.ByteArrayCompare(image.Pixels, 0, 4, GrfImage.PngHeader, 0))
            {
                var decoder = new PngBitmapDecoder(new MemoryStream(image.Pixels), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                return decoder.Frames[0];
            }
            if (image.Pixels.Length > 2 && Methods.ByteArrayCompare(image.Pixels, 0, 2, GrfImage.BmpHeader, 0))
            {
                var decoder = new BmpBitmapDecoder(new MemoryStream(image.Pixels), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                return decoder.Frames[0];
            }
        }
        var bitmapImage = new BitmapImage { CreateOptions = BitmapCreateOptions.PreservePixelFormat, CacheOption = BitmapCacheOption.Default };
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = new MemoryStream(image.Pixels);
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }
}
