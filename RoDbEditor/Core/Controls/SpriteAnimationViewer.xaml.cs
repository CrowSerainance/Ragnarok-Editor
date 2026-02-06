using System;
using System.Collections.Generic;
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
            ClearDisplay();
            return;
        }

        try
        {
            _spr = new Spr(sprData);

            // Cache all sprite images as BitmapSource
            foreach (var img in _spr.Images)
            {
                var bmp = img.Cast<BitmapSource>();
                bmp?.Freeze();
                _spriteCache.Add(bmp!);
            }

            if (actData != null && actData.Length > 0)
            {
                _act = new Act(actData, sprData);
            }

            RenderCurrentFrame();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load sprite: {ex.Message}");
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
                SpriteImage.Source = _spriteCache[0];
                CenterImage(_spriteCache[0]);
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
        if (frame.Layers.Count == 0) return null;

        // Calculate bounds
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        var layerData = new List<(BitmapSource bmp, int ox, int oy, float sx, float sy, int angle)>();

        foreach (var layer in frame.Layers)
        {
            if (layer.SpriteIndex < 0 || layer.SpriteIndex >= _spriteCache.Count)
                continue;

            var bmp = _spriteCache[layer.SpriteIndex];
            if (bmp == null) continue;

            int w = (int)(bmp.PixelWidth * Math.Abs(layer.ScaleX));
            int h = (int)(bmp.PixelHeight * Math.Abs(layer.ScaleY));
            int x = layer.OffsetX - w / 2;
            int y = layer.OffsetY - h / 2;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);

            layerData.Add((bmp, layer.OffsetX, layer.OffsetY, layer.ScaleX, layer.ScaleY, layer.Rotation));
        }

        if (layerData.Count == 0) return null;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        if (width <= 0 || height <= 0) return null;

        // Create drawing visual
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            foreach (var (bmp, ox, oy, sx, sy, angle) in layerData)
            {
                double w = bmp.PixelWidth * Math.Abs(sx);
                double h = bmp.PixelHeight * Math.Abs(sy);
                double x = ox - minX - w / 2;
                double y = oy - minY - h / 2;

                var transform = new TransformGroup();

                // Scale (handle flipping)
                transform.Children.Add(new ScaleTransform(
                    sx < 0 ? -1 : 1,
                    sy < 0 ? -1 : 1,
                    w / 2, h / 2));

                // Rotation
                if (angle != 0)
                {
                    transform.Children.Add(new RotateTransform(angle, w / 2, h / 2));
                }

                // Translation
                transform.Children.Add(new TranslateTransform(x, y));

                dc.PushTransform(transform);
                dc.DrawImage(bmp, new Rect(0, 0, w, h));
                dc.Pop();
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
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
