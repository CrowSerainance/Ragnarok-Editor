using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
/// Based on ActEditor's FrameRenderer pattern.
/// </summary>
public partial class SpriteAnimationViewer : System.Windows.Controls.UserControl
{
    private Act? _act;
    private Spr? _spr;
    private List<BitmapSource> _spriteCache = new();

    private int _currentAction;
    private int _currentFrame;
    private bool _isPlaying;
    private DispatcherTimer? _timer;

    public SpriteAnimationViewer()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += Timer_Tick;
    }

    /// <summary>
    /// Load sprite from raw ACT and SPR byte data.
    /// </summary>
    public void LoadFromData(byte[]? actData, byte[]? sprData)
    {
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
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] SPR loaded: {_spr.Images.Count} images");

            // Cache all sprite images as BitmapSource
            int successCount = 0;
            int failCount = 0;
            foreach (var img in _spr.Images)
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
                        _spriteCache.Add(null!);
                        failCount++;
                    }
                }
                catch (Exception imgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Failed to convert image: {imgEx.Message}");
                    _spriteCache.Add(null!);
                    failCount++;
                }
            }
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Cached {successCount} images, {failCount} failed");

            if (actData != null && actData.Length > 0)
            {
                _act = new Act(actData, sprData);
                System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] ACT loaded: {_act.NumberOfActions} actions");
            }

            RenderCurrentFrame();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteAnimationViewer] Failed to load sprite: {ex.Message}\n{ex.StackTrace}");
            ClearDisplay();
        }
    }

    /// <summary>
    /// Load sprite from SPR data only (no animation).
    /// </summary>
    public void LoadFromSprData(byte[] sprData)
    {
        LoadFromData(null, sprData);
    }

    private void ClearDisplay()
    {
        SpriteImage.Source = null;
        TxtInfo.Text = "No sprite";
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
            if (_spriteCache.Count > 0)
            {
                // Find first non-null sprite
                var firstSprite = _spriteCache.FirstOrDefault(s => s != null);
                if (firstSprite != null)
                {
                    SpriteImage.Source = firstSprite;
                    CenterImage(firstSprite);
                }
            }
            TxtInfo.Text = $"Sprite 0/{_spriteCache.Count}";
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
        var composited = CompositeFrame(frame);
        SpriteImage.Source = composited;

        if (composited != null)
            CenterImage(composited);

        TxtInfo.Text = $"Action {_currentAction}/{_act.NumberOfActions} Frame {_currentFrame}/{action.Frames.Count}";

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
        if (frame.Layers.Count == 0 || _spr == null) return null;

        var layerData = new List<(BitmapSource bmp, int imgW, int imgH, int ox, int oy, float sx, float sy, int angle, int mirrorOffset)>();

        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        foreach (var layer in frame.Layers)
        {
            int absIdx = layer.GetAbsoluteSpriteId(_spr);
            if (absIdx < 0 || absIdx >= _spr.Images.Count)
                continue;

            var grfImg = _spr.Images[absIdx];
            if (grfImg == null) continue;

            int imgW = grfImg.Width;
            int imgH = grfImg.Height;

            // Apply layer tint/alpha (ActImaging pattern)
            var img = grfImg.Copy();
            img.ApplyChannelColor(layer.Color);
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
        BtnPlayPause.Content = "⏸";
    }

    public void Stop()
    {
        _isPlaying = false;
        _timer?.Stop();
        BtnPlayPause.Content = "▶";
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) Stop();
        else Play();
    }

    private void BtnPrevFrame_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        _currentFrame--;
        if (_currentFrame < 0 && _act != null)
            _currentFrame = _act[_currentAction].Frames.Count - 1;
        if (_currentFrame < 0) _currentFrame = 0;
        RenderCurrentFrame();
    }

    private void BtnNextFrame_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        _currentFrame++;
        if (_act != null && _currentFrame >= _act[_currentAction].Frames.Count)
            _currentFrame = 0;
        RenderCurrentFrame();
    }

    private void BtnPrevAction_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        _currentAction--;
        if (_currentAction < 0 && _act != null)
            _currentAction = _act.NumberOfActions - 1;
        if (_currentAction < 0) _currentAction = 0;
        _currentFrame = 0;
        RenderCurrentFrame();
    }

    private void BtnNextAction_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        _currentAction++;
        if (_act != null && _currentAction >= _act.NumberOfActions)
            _currentAction = 0;
        _currentFrame = 0;
        RenderCurrentFrame();
    }
}
