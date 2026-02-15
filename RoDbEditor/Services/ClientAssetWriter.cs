using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Creates placeholder BMP files for items (inventory icon, collection) when missing.
/// Uses magenta (#FF00FF) background for transparency.
/// </summary>
public class ClientAssetWriter
{
    private readonly string _clientRoot;

    public ClientAssetWriter(string clientRoot)
    {
        _clientRoot = clientRoot ?? "";
    }

    /// <summary>Ensure 24x24 inventory icon BMP exists. Creates placeholder if missing.</summary>
    public bool EnsureItemIcon(ItemEntry item)
    {
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot))
            return false;

        var resourceName = string.IsNullOrWhiteSpace(item.ResourceName)
            ? (string.IsNullOrEmpty(item.AegisName) ? "Custom_Item" : item.AegisName)
            : item.ResourceName;
        var uiFolder = "\uC720\uC800\uC778\uD130\uD398\uC774\uC2A4";
        var dir = Path.Combine(_clientRoot, "data", "texture", uiFolder, "item");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { return false; }
        }

        var path = Path.Combine(dir, resourceName + ".bmp");
        if (File.Exists(path))
            return true;

        return CreatePlaceholderBmp(path, 24, 24);
    }

    /// <summary>Ensure 75x100 collection BMP exists. Creates placeholder if missing.</summary>
    public bool EnsureCollectionIcon(ItemEntry item)
    {
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot))
            return false;

        var resourceName = string.IsNullOrWhiteSpace(item.ResourceName)
            ? (string.IsNullOrEmpty(item.AegisName) ? "Custom_Item" : item.AegisName)
            : item.ResourceName;
        var uiFolder = "\uC720\uC800\uC778\uD130\uD398\uC774\uC2A4";
        var dir = Path.Combine(_clientRoot, "data", "texture", uiFolder, "collection");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { return false; }
        }

        var path = Path.Combine(dir, resourceName + ".bmp");
        if (File.Exists(path))
            return true;

        return CreatePlaceholderBmp(path, 75, 100);
    }

    private static bool CreatePlaceholderBmp(string path, int width, int height)
    {
        try
        {
            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            var magenta = Color.FromArgb(255, 0, 255);
            g.Clear(magenta);
            using var font = new Font("Arial", Math.Max(8, Math.Min(width, height) / 4));
            using var brush = new SolidBrush(Color.White);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("?", font, brush, new RectangleF(0, 0, width, height), sf);
            bmp.Save(path, ImageFormat.Bmp);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientAssetWriter] CreatePlaceholderBmp failed: {ex.Message}");
            return false;
        }
    }
}
