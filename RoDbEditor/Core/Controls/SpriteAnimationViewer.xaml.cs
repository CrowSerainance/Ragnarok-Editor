using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.Image;

// Alias to avoid ambiguity with System.Windows.Controls.Frame
using ActFrame = GRF.FileFormats.ActFormat.Frame;

namespace RoDbEditor.Core.Controls;

/// <summary>
/// Animated sprite viewer for ACT/SPR files.
/// Matches GRF Editor: Image preview (frame-by-frame strip) and Animation preview (ACT playback).
/// </summary>
public partial class SpriteAnimationViewer : System.Windows.Controls.UserControl
{
    public enum ViewerBackgroundMode
    {
        SolidDark,
        Checkered
    }

    private Act? _act;
    private Spr? _spr;
    private List<BitmapSource?> _spriteCache = new();
    private bool _suppressComboEvents;

    private int _currentAction;
    private int _currentFrame;
    private bool _isPlaying;
    private DispatcherTimer? _timer;
    public bool LastLoadSucceeded { get; private set; }

    private bool _imagePreviewMode = true;

    public SpriteAnimationViewer()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += Timer_Tick;
        SetBackgroundMode(ViewerBackgroundMode.SolidDark);

        TxtZoomValue.TextChanged += (s, e) =>
        {
            if (double.TryParse(TxtZoomValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var zoom) && zoom > 0)
                ApplyFrameStripZoom(zoom);
        };
    }

    public void SetBackgroundMode(ViewerBackgroundMode mode)
    {
        if (mode == ViewerBackgroundMode.Checkered)
        {
            // 2x2 checker texture (light/dark) scaled for readability.
            var tile = new DrawingGroup();
            using (var dc = tile.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45)), null, new Rect(0, 0, 16, 16));
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58)), null, new Rect(0, 0, 8, 8));
                dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58)), null, new Rect(8, 8, 8, 8));
            }
            var brush = new DrawingBrush(tile)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 16, 16),
                TileMode = TileMode.Tile,
                Stretch = Stretch.None
            };
            SpriteHostBorder.Background = brush;
        }
        else
        {
            SpriteHostBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48));
        }
    }

    /// <summary>
    /// Load sprite from raw ACT and SPR byte data.
    /// </summary>
    public void LoadFromData(byte[]? actData, byte[]? sprData)
    {
        LastLoadSucceeded = false;
        Stop();
        _act = null;
        _spr = null;
        _spriteCache.Clear();
        _currentAction = 0;
        _currentFrame = 0;

        if (sprData == null || sprData.Length == 0)
        {
            System.Diagnostics.Debug.WriteLine("[SpriteAnimationViewer] No SPR data provided");
            ClearDisplay();
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Loading SPR data: {sprData.Length} bytes, ACT: {actData?.Length ?? 0} bytes");

        try
        {
            _spr = new Spr(sprData);
            var imageCount = _spr.Images?.Count ?? 0;
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] SPR loaded: {imageCount} images");

            // Cache all sprite images as BitmapSource
            int successCount = 0;
            int failCount = 0;
            foreach (var img in _spr.Images ?? Enumerable.Empty<GrfImage>())
            {
                try
                {
                    var bmp = img.Cast<BitmapSource>();
                    if (bmp != null)
                    {
                        bmp.Freeze();
                        _spriteCache.Add(bmp);
                        successCount++;
                    }
                    else
                    {
                        _spriteCache.Add(null);
                        failCount++;
                    }
                }
                catch (Exception imgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Failed to convert image: {imgEx.Message}");
                    _spriteCache.Add(null);
                    failCount++;
                }
            }
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Cached {successCount} images, {failCount} failed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Failed to load sprite: {ex.Message}\n{ex.StackTrace}");
            ClearDisplay($"Load error: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // ACT parsing is optional. If ACT fails, keep SPR-only preview.
        if (actData != null && actData.Length > 0)
        {
            try
            {
                _act = new Act(actData, sprData);
                System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] ACT loaded: {_act.NumberOfActions} actions");
            }
            catch (Exception actEx)
            {
                _act = null;
                System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] ACT load failed, using SPR-only preview: {actEx.Message}");
            }
        }

        try
        {
            RenderCurrentFrame();
        }
        catch (Exception renderEx)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Render failed, retrying SPR-only: {renderEx.Message}");
            _act = null;
            try
            {
                RenderCurrentFrame();
            }
            catch (Exception fallbackEx)
            {
                System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] SPR-only render failed: {fallbackEx.Message}");
                ClearDisplay($"Load error: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
                return;
            }
        }

        // Build frame strip for Image preview (SPR frames side-by-side, like GRF Editor)
        RenderFrameStrip();

        // Populate Animation/Action dropdowns
        PopulateAnimationCombos();

        // Ensure correct view visibility
        FrameStripScroll.Visibility = _imagePreviewMode ? Visibility.Visible : Visibility.Collapsed;
        SpriteCanvas.Visibility = _imagePreviewMode ? Visibility.Collapsed : Visibility.Visible;
        TxtNoSprite.Visibility = _spriteCache.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        // Auto-play only when ACT is valid and in Animation preview
        if (_act != null && _act.NumberOfActions > 0 && !_imagePreviewMode)
        {
            Play();
        }
        LastLoadSucceeded = true;
    }

    /// <summary>
    /// Load sprite from SPR data only (no animation).
    /// </summary>
    public void LoadFromSprData(byte[] sprData)
    {
        LoadFromData(null, sprData);
    }

    private void ClearDisplay(string? message = null)
    {
        SpriteImage.Source = null;
        FrameStripImage.Source = null;
        TxtNoSprite.Text = message ?? "No sprite";
        TxtNoSprite.Visibility = Visibility.Visible;
        ComboAnimation.ItemsSource = null;
        ComboAction.ItemsSource = null;
    }

    private void RenderFrameStrip()
    {
        if (_spr == null || _spriteCache.Count == 0)
        {
            FrameStripImage.Source = null;
            return;
        }

        try
        {
            // Use Spr.Image for composite (all frames side-by-side, like GRF Editor)
            var composite = _spr.Image;
            if (composite != null)
            {
                var bmp = composite.Cast<BitmapSource>();
                if (bmp != null)
                {
                    bmp.Freeze();
                    FrameStripImage.Source = bmp;
                    ApplyFrameStripZoom(double.TryParse(TxtZoomValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var z) ? z : 1.0);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Spr.Image failed: {ex.Message}");
        }

        // Fallback: build strip from cached bitmaps
        var valid = _spriteCache.Where(s => s != null).Cast<BitmapSource>().ToList();
        if (valid.Count == 0)
        {
            FrameStripImage.Source = null;
            return;
        }

        int totalW = valid.Sum(b => b.PixelWidth);
        int maxH = valid.Max(b => b.PixelHeight);
        if (totalW <= 0 || maxH <= 0)
        {
            FrameStripImage.Source = valid[0];
            return;
        }

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            int x = 0;
            foreach (var b in valid)
            {
                dc.DrawImage(b, new Rect(x, 0, b.PixelWidth, b.PixelHeight));
                x += b.PixelWidth;
            }
        }
        var rtb = new RenderTargetBitmap(totalW, maxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        FrameStripImage.Source = rtb;
        ApplyFrameStripZoom(double.TryParse(TxtZoomValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var z2) ? z2 : 1.0);
    }

    private void ApplyFrameStripZoom(double zoom)
    {
        if (FrameStripImage.Source is BitmapSource bmp)
        {
            FrameStripImage.Width = bmp.PixelWidth * zoom;
            FrameStripImage.Height = bmp.PixelHeight * zoom;
        }
    }

    private void PopulateAnimationCombos()
    {
        _suppressComboEvents = true;
        try
        {
            if (_act == null || _act.NumberOfActions == 0)
            {
                ComboAnimation.ItemsSource = new[] { "0" };
                ComboAction.ItemsSource = new[] { 0 };
                ComboAnimation.SelectedIndex = 0;
                ComboAction.SelectedIndex = 0;
                return;
            }

            var animStrings = _act.GetAnimationStrings();
            ComboAnimation.ItemsSource = animStrings;
            ComboAction.ItemsSource = Enumerable.Range(0, _act.NumberOfActions).ToList();

            _currentAction = Math.Clamp(_currentAction, 0, _act.NumberOfActions - 1);
            ComboAnimation.SelectedIndex = Math.Min(_currentAction / 8, animStrings.Count - 1);
            ComboAction.SelectedIndex = _currentAction;
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private void TabImagePreview_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _imagePreviewMode = true;
        Stop();
        FrameStripScroll.Visibility = Visibility.Visible;
        SpriteCanvas.Visibility = Visibility.Collapsed;
        TxtNoSprite.Visibility = _spriteCache.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void TabAnimationPreview_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _imagePreviewMode = false;
        FrameStripScroll.Visibility = Visibility.Collapsed;
        SpriteCanvas.Visibility = Visibility.Visible;
        RenderCurrentFrame();
        if (_act != null && _act.NumberOfActions > 0)
            Play();
    }

    private void ComboAnimation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvents || _act == null || ComboAnimation.SelectedIndex < 0) return;

        int animIdx = ComboAnimation.SelectedIndex;
        int dir = _currentAction % 8;
        int newAction = Math.Min(animIdx * 8 + dir, _act.NumberOfActions - 1);
        _currentAction = Math.Max(0, newAction);
        _suppressComboEvents = true;
        ComboAction.SelectedIndex = _currentAction;
        _suppressComboEvents = false;
        _currentFrame = 0;
        RenderCurrentFrame();
    }

    private void ComboAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvents || _act == null || ComboAction.SelectedIndex < 0) return;

        _currentAction = ComboAction.SelectedIndex;
        _currentFrame = 0;
        RenderCurrentFrame();
    }

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        _imagePreviewMode = false;
        TabAnimationPreview.IsChecked = true;
        TabImagePreview_Checked(sender, e);
        TabAnimationPreview_Checked(sender, e);
        Play();
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        Stop();
    }

    private void RenderCurrentFrame()
    {
        if (_spr == null || _spriteCache.Count == 0)
        {
            ClearDisplay();
            return;
        }

        // If no ACT, just show first sprite
        if (_act == null || _act.NumberOfActions == 0)
        {
            var firstSprite = _spriteCache.FirstOrDefault(s => s != null);
            if (firstSprite != null)
            {
                SpriteImage.Source = firstSprite;
                CenterImage(firstSprite);
                TxtNoSprite.Visibility = Visibility.Collapsed;
                return;
            }

            ClearDisplay("No renderable sprite frame");
            return;
        }

        // Clamp action/frame indices
        _currentAction = Math.Clamp(_currentAction, 0, _act.NumberOfActions - 1);
        var action = _act[_currentAction];

        if (action.Frames.Count == 0)
        {
            ClearDisplay();
            return;
        }

        _currentFrame = Math.Clamp(_currentFrame, 0, action.Frames.Count - 1);
        var frame = action.Frames[_currentFrame];

        // Composite all layers
        BitmapSource? composited = null;
        try
        {
            composited = CompositeFrame(frame);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] CompositeFrame failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (composited == null)
        {
            var firstSprite = _spriteCache.FirstOrDefault(s => s != null);
            if (firstSprite != null)
            {
                SpriteImage.Source = firstSprite;
                CenterImage(firstSprite);
                TxtNoSprite.Visibility = Visibility.Collapsed;
                return;
            }

            ClearDisplay("No renderable sprite frame");
            return;
        }

        SpriteImage.Source = composited;

        if (composited != null)
            CenterImage(composited);

        TxtNoSprite.Visibility = Visibility.Collapsed;

        // Update timer interval based on action's animation speed
        if (_timer != null)
        {
            int interval = action.Interval;
            if (interval <= 0) interval = 100;
            _timer.Interval = TimeSpan.FromMilliseconds(interval);
        }
    }

    private BitmapSource? CompositeFrame(ActFrame frame)
    {
        if (frame == null || frame.Layers == null || frame.Layers.Count == 0 || _spr == null) return null;

        var layerData = new List<(BitmapSource bmp, int imgW, int imgH, int ox, int oy, float sx, float sy, int angle, int mirrorOffset)>();

        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        foreach (var layer in frame.Layers)
        {
            try
            {
            int absIdx = layer.GetAbsoluteSpriteId(_spr);
            if (_spr.Images == null || absIdx < 0 || absIdx >= _spr.Images.Count)
                continue;

            var grfImg = _spr.Images[absIdx];
            if (grfImg == null) continue;

            int imgW = grfImg.Width;
            int imgH = grfImg.Height;

            // Apply layer tint/alpha (ActImaging pattern)
            var img = grfImg.Copy();
            try
            {
                img.ApplyChannelColor(layer.Color);
            }
            catch
            {
                // Some extracted ACT files have incomplete color data; render without tint.
            }
            var bmp = img.Cast<BitmapSource>();
            if (bmp == null) continue;

            // Mirror: effective scale and offset correction (per ActImaging)
            float effectiveScaleX = layer.ScaleX * (layer.Mirror ? -1f : 1f);
            int mirrorOffset = layer.Mirror ? -(imgW + 1) % 2 : 0;

            // Rotation-aware bounds: transform corners by scale → mirror → rotate → translate
            GetTransformedCorners(imgW, imgH, layer.OffsetX + mirrorOffset, layer.OffsetY, effectiveScaleX, layer.ScaleY, layer.Rotation, out int lminX, out int lminY, out int lmaxX, out int lmaxY);
            minX = Math.Min(minX, lminX);
            minY = Math.Min(minY, lminY);
            maxX = Math.Max(maxX, lmaxX);
            maxY = Math.Max(maxY, lmaxY);

            layerData.Add((bmp, imgW, imgH, layer.OffsetX, layer.OffsetY, effectiveScaleX, layer.ScaleY, layer.Rotation, mirrorOffset));
            }
            catch (Exception layerEx)
            {
                System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Skipping bad layer: {layerEx.GetType().Name}: {layerEx.Message}");
            }
        }

        if (layerData.Count == 0) return null;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0) return null;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            foreach (var (bmp, imgW, imgH, ox, oy, sx, sy, angle, mirrorOffset) in layerData)
            {
                // ActImaging transform order: center (with mirror offset) → scale → rotate → translate
                var transform = new TransformGroup();
                transform.Children.Add(new TranslateTransform(-(imgW + 1) / 2.0 + mirrorOffset, -(imgH + 1) / 2.0));
                transform.Children.Add(new ScaleTransform(sx, sy));
                transform.Children.Add(new RotateTransform(angle, 0, 0));
                transform.Children.Add(new TranslateTransform(ox - minX, oy - minY));

                dc.PushTransform(transform);
                dc.DrawImage(bmp, new Rect(0, 0, imgW, imgH));
                dc.Pop();
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void GetTransformedCorners(int w, int h, int offsetX, int offsetY, float scaleX, float scaleY, int angleDeg,
        out int minX, out int minY, out int maxX, out int maxY)
    {
        double rad = angleDeg * Math.PI / 180;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        double cx = -(w + 1) / 2.0;
        double cy = -(h + 1) / 2.0;

        double Tx(double x, double y)
        {
            double sx = (x + cx) * scaleX;
            double sy = (y + cy) * scaleY;
            return sx * cos - sy * sin + offsetX;
        }
        double Ty(double x, double y)
        {
            double sx = (x + cx) * scaleX;
            double sy = (y + cy) * scaleY;
            return sx * sin + sy * cos + offsetY;
        }

        var corners = new[] { (0, 0), (w, 0), (w, h), (0, h) };
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        foreach (var (px, py) in corners)
        {
            double fx = Tx(px, py);
            double fy = Ty(px, py);
            minX = Math.Min(minX, (int)Math.Floor(fx));
            minY = Math.Min(minY, (int)Math.Floor(fy));
            maxX = Math.Max(maxX, (int)Math.Ceiling(fx));
            maxY = Math.Max(maxY, (int)Math.Ceiling(fy));
        }
    }

    private void CenterImage(BitmapSource bmp)
    {
        double canvasW = SpriteCanvas.ActualWidth;
        double canvasH = SpriteCanvas.ActualHeight;

        if (canvasW <= 0) canvasW = 256;
        if (canvasH <= 0) canvasH = 256;

        System.Windows.Controls.Canvas.SetLeft(SpriteImage, (canvasW - bmp.PixelWidth) / 2);
        System.Windows.Controls.Canvas.SetTop(SpriteImage, (canvasH - bmp.PixelHeight) / 2);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_act == null || !_isPlaying) return;

        var action = _act[_currentAction];
        _currentFrame++;

        if (_currentFrame >= action.Frames.Count)
            _currentFrame = 0;

        RenderCurrentFrame();
    }

    public void Play()
    {
        _isPlaying = true;
        _timer?.Start();
    }

    public void Stop()
    {
        _isPlaying = false;
        _timer?.Stop();
    }
}
