using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ROMapOverlayEditor; // <-- MUST match your project namespace

public sealed class ZoomPanCanvas : Canvas
{
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly TranslateTransform _translate = new(0.0, 0.0);
    private Point _panStart;
    private bool _panning;

    public double MinZoom { get; set; } = 0.1;
    public double MaxZoom { get; set; } = 10.0;

    public double Zoom
    {
        get => _scale.ScaleX;
        set
        {
            var z = Math.Clamp(value, MinZoom, MaxZoom);
            _scale.ScaleX = z;
            _scale.ScaleY = z;
        }
    }

    public double OffsetX
    {
        get => _translate.X;
        set => _translate.X = value;
    }

    public double OffsetY
    {
        get => _translate.Y;
        set => _translate.Y = value;
    }

    public ZoomPanCanvas()
    {
        ClipToBounds = true;

        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        tg.Children.Add(_translate);
        RenderTransform = tg;
        RenderTransformOrigin = new Point(0, 0);

        Background = Brushes.Transparent;

        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
    }

    public void ResetView()
    {
        Zoom = 1.0;
        OffsetX = 0.0;
        OffsetY = 0.0;
    }

    public void FitToView(Size contentSize, Size viewportSize, double padding = 20)
    {
        if (contentSize.Width <= 0 || contentSize.Height <= 0 ||
            viewportSize.Width <= 0 || viewportSize.Height <= 0) return;

        var sx = (viewportSize.Width - padding) / contentSize.Width;
        var sy = (viewportSize.Height - padding) / contentSize.Height;
        var z = Math.Clamp(Math.Min(sx, sy), MinZoom, MaxZoom);

        Zoom = z;

        // Center content
        var scaledW = contentSize.Width * z;
        var scaledH = contentSize.Height * z;

        OffsetX = (viewportSize.Width - scaledW) / 2.0;
        OffsetY = (viewportSize.Height - scaledH) / 2.0;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panStart = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;

        var p = e.GetPosition(this);
        var delta = p - _panStart;
        _panStart = p;

        OffsetX += delta.X;
        OffsetY += delta.Y;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);

        var oldZoom = Zoom;
        var zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newZoom = Math.Clamp(oldZoom * zoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 1e-6) return;

        // zoom towards cursor:
        // Translate so that point under cursor stays under cursor.
        // World -> screen: screen = world*zoom + offset
        // Keep screen constant while zoom changes.
        var wx = (pos.X - OffsetX) / oldZoom;
        var wy = (pos.Y - OffsetY) / oldZoom;

        Zoom = newZoom;

        OffsetX = pos.X - wx * newZoom;
        OffsetY = pos.Y - wy * newZoom;

        e.Handled = true;
    }
}
