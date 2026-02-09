using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace RoDbEditor.UI;

/// <summary>
/// Text marker service for AvalonEdit, adapted from ILSpy.
/// Handles creation and rendering of text markers (highlights, underlines, etc.)
/// </summary>
public sealed class TextMarkerService : DocumentColorizingTransformer, IBackgroundRenderer
{
    private readonly TextDocument _document;
    private readonly List<TextMarker> _markers = new();

    public TextMarkerService(TextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView == null) throw new ArgumentNullException(nameof(textView));
        if (drawingContext == null) throw new ArgumentNullException(nameof(drawingContext));

        if (_markers.Count == 0 || !textView.VisualLinesValid)
            return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
            return;

        int viewStart = visualLines.First().FirstDocumentLine.Offset;
        int viewEnd = visualLines.Last().LastDocumentLine.EndOffset;

        foreach (var marker in _markers)
        {
            if (marker.EndOffset < viewStart || marker.StartOffset > viewEnd)
                continue;

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = marker.StartOffset, EndOffset = marker.EndOffset }))
            {
                var drawingRect = new Rect(rect.Location, rect.Size);
                
                if (marker.BackgroundColor != null)
                {
                    drawingContext.DrawRectangle(new SolidColorBrush(marker.BackgroundColor.Value), null, drawingRect);
                }

                if (marker.ForegroundColor != null)
                {
                    // Foreground color is handled by ColorizeLine
                }
            }
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_markers.Count == 0)
            return;

        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        foreach (var marker in _markers.Where(m => m.StartOffset < lineEnd && m.EndOffset > lineStart))
        {
            int start = Math.Max(marker.StartOffset, lineStart);
            int end = Math.Min(marker.EndOffset, lineEnd);

            ChangeLinePart(start, end, element =>
            {
                if (marker.ForegroundColor != null)
                {
                    element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(marker.ForegroundColor.Value));
                }
            });
        }
    }

    public TextMarker Create(int startOffset, int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be non-negative");

        int endOffset = startOffset + length;
        var marker = new TextMarker(this, startOffset, endOffset);
        _markers.Add(marker);
        Redraw();
        return marker;
    }

    public void Remove(TextMarker marker)
    {
        if (marker != null && _markers.Remove(marker))
        {
            Redraw();
        }
    }

    public void RemoveAll(Predicate<TextMarker> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        int removed = _markers.RemoveAll(predicate);
        if (removed > 0)
        {
            Redraw();
        }
    }

    public void Clear()
    {
        if (_markers.Count > 0)
        {
            _markers.Clear();
            Redraw();
        }
    }

    private void Redraw()
    {
        // Trigger redraw by invalidating the document
        _document.UndoStack.ClearAll();
    }

    public IEnumerable<TextMarker> GetMarkersAtOffset(int offset)
    {
        return _markers.Where(m => m.StartOffset <= offset && offset < m.EndOffset);
    }
}

public sealed class TextMarker
{
    private readonly TextMarkerService _service;

    internal TextMarker(TextMarkerService service, int startOffset, int endOffset)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    public int StartOffset { get; }
    public int EndOffset { get; }
    public int Length => EndOffset - StartOffset;

    public System.Windows.Media.Color? BackgroundColor { get; set; }
    public System.Windows.Media.Color? ForegroundColor { get; set; }
    public string ToolTip { get; set; }

    public void Delete()
    {
        _service.Remove(this);
    }
}
