using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinUtil.Models;
using WinUtil.Services;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WinUtil.Views;

public partial class ScreenshotEditorWindow
{
    private const double SelectionHandleSize = 8;
    private const double SelectionHitTolerance = 6;
    private const double SelectionOutlineWidth = 1;
    private const double MinimumViewScale = 0.01;
    private const double MinimumSelectionDragDistance = 1;
    private int? selectedAnnotationIndex;
    private int? editingAnnotationIndex;
    private bool isSelecting;
    private Point? selectionDragStart;
    private ScreenshotAnnotation? selectionDragOriginal;
    private ScreenshotAnnotation? selectionDragPreview;
    private ScreenshotResizeHandle selectionDragHandle;

    internal int? SelectedAnnotationIndex => selectedAnnotationIndex;
    internal IReadOnlyList<ScreenshotAnnotation> Annotations => document.Annotations;

    private ScreenshotAnnotation? SelectedAnnotation => selectedAnnotationIndex is int index && index < document.Annotations.Length
        ? document.Annotations[index] : null;

    private bool HasPendingTextChanges => textAnchor is Point anchor
        && (editingAnnotationIndex is int index
            ? CreateTextAnnotation(anchor) != document.Annotations[index]
            : !string.IsNullOrWhiteSpace(AnnotationTextBox.Text));

    internal int? SelectAnnotationAt(Point point)
    {
        ValidateSelectionPoint(point);
        FinishTextEntry(commit: true);
        CancelDrag();
        SelectTool.IsChecked = true;
        selectedAnnotationIndex = null;

        for (var index = document.Annotations.Length - 1; index >= 0; index--)
        {
            var annotation = document.Annotations[index];
            var transform = document.GetAnnotationToOutput(annotation);
            var inverse = transform;
            inverse.Invert();
            var tolerance = SelectionHitTolerance / ViewScale / MinimumAxisScale(transform);
            if (new Rect(0, 0, document.Width, document.Height).Contains(point)
                && ScreenshotAnnotationGeometry.HitTest(annotation, inverse.Transform(point), tolerance))
            {
                selectedAnnotationIndex = index;
                break;
            }
        }

        LoadSelectedOptions();
        UpdateToolOptions();
        DrawSelection();
        return selectedAnnotationIndex;
    }

    internal bool BeginSelectionDrag(Point point)
    {
        ValidateSelectionPoint(point);
        FinishTextEntry(commit: true);
        CancelDrag();
        var handle = HitTestSelectionHandle(point);

        if (handle is null)
        {
            SelectAnnotationAt(point);
            handle = ScreenshotResizeHandle.Move;
        }

        if (SelectedAnnotation is not { } annotation)
        {
            return false;
        }

        selectionDragStart = point;
        selectionDragOriginal = annotation;
        selectionDragPreview = annotation;
        selectionDragHandle = handle.Value;
        return true;
    }

    internal void PreviewSelectionDrag(Point point)
    {
        ValidateSelectionPoint(point);

        if (selectionDragStart is not Point start || selectionDragOriginal is not { } original || selectedAnnotationIndex is not int index)
        {
            return;
        }

        var offset = point - start;
        var inverse = document.GetAnnotationToOutput(original);
        inverse.Invert();
        var localOffset = inverse.Transform(point) - inverse.Transform(start);
        var limits = document.GetAnnotationLocalBounds(original);

        if (offset.Length * ViewScale < MinimumSelectionDragDistance)
        {
            selectionDragPreview = original;
        }
        else if (selectionDragHandle == ScreenshotResizeHandle.Move)
        {
            selectionDragPreview = ScreenshotAnnotationGeometry.Move(original, localOffset, limits);
        }
        else
        {
            var originalHandle = GetSelectionHandles(original).Single(handle => handle.Handle == selectionDragHandle).Position;
            selectionDragPreview = ScreenshotAnnotationGeometry.Resize(original, selectionDragHandle, inverse.Transform(originalHandle + offset), limits);
        }
        ScreenshotImage.Source = document.RenderReplacing(index, selectionDragPreview);
        DrawSelection(selectionDragPreview);
    }

    internal void CompleteSelectionDrag(Point point)
    {
        PreviewSelectionDrag(point);
        var preview = selectionDragPreview;
        var index = selectedAnnotationIndex;
        CancelDrag();

        if (index is int selectedIndex && preview is not null)
        {
            document.Replace(selectedIndex, preview);
            RefreshImage();
        }
    }

    internal void CancelSelectionDrag() => CancelDrag();

    internal void DeleteSelectedAnnotation()
    {
        FinishTextEntry(commit: false);
        CancelDrag();

        if (selectedAnnotationIndex is not int index)
        {
            return;
        }

        document.Remove(index);
        selectedAnnotationIndex = null;
        UpdateToolOptions();
        RefreshImage();
        SetStatus("Annotation deleted. Ctrl+Z to undo.");
    }

    internal void BeginSelectedTextEdit()
    {
        FinishTextEntry(commit: true);

        if (selectedAnnotationIndex is not int index || SelectedAnnotation is not { Kind: ScreenshotAnnotationKind.Text } annotation)
        {
            return;
        }

        editingAnnotationIndex = index;
        var projected = document.GetAnnotationToOutput(annotation).Transform(annotation.Start);
        var visibleAnchor = new Point(Math.Clamp(projected.X, 0, document.Width - LastPixelOffset), Math.Clamp(projected.Y, 0, document.Height - LastPixelOffset));
        OpenTextEntry(visibleAnchor, annotation.Text);
        AnnotationTextBox.SelectAll();
    }

    private ScreenshotAnnotation CreateTextAnnotation(Point anchor)
    {
        var annotation = editingAnnotationIndex is int index ? document.Annotations[index] : new ScreenshotAnnotation(
            ScreenshotAnnotationKind.Text, anchor, anchor, (Color)ColorConverter.ConvertFromString(Options.Color))
            { LocalToSource = document.OutputToSource };
        return annotation with
        {
            Color = (Color)ColorConverter.ConvertFromString(Options.Color), StrokeWidth = Options.StrokeWidth,
            Text = AnnotationTextBox.Text, FontSize = Options.FontSize, BlurRadius = Options.BlurRadius
        };
    }

    private void ApplySelectedOptions()
    {
        if (!isSelecting || SelectedAnnotation is not { } annotation || selectedAnnotationIndex is not int index || editingAnnotationIndex is not null)
        {
            UpdateUnsavedTitle();
            return;
        }

        var updated = annotation.Kind switch
        {
            ScreenshotAnnotationKind.Blur => annotation with { BlurRadius = Options.BlurRadius },
            ScreenshotAnnotationKind.Text => annotation with { Color = (Color)ColorConverter.ConvertFromString(Options.Color), FontSize = Options.FontSize },
            _ => annotation with { Color = (Color)ColorConverter.ConvertFromString(Options.Color), StrokeWidth = Options.StrokeWidth }
        };
        document.Replace(index, updated);
        RefreshImage();
    }

    private void LoadSelectedOptions()
    {
        if (SelectedAnnotation is not { } annotation)
        {
            return;
        }

        isInitializing = true;

        try
        {
            Options = new ScreenshotEditorOptions
            {
                Color = annotation.Color.ToString(),
                StrokeWidth = annotation.StrokeWidth,
                FontSize = annotation.FontSize,
                BlurRadius = annotation.BlurRadius
            };

            if (!ColorComboBox.Items.Cast<ComboBoxItem>().Any(item => Equals(item.Tag, Options.Color)))
            {
                ColorComboBox.Items.Add(CreateColorOption("Custom", Options.Color));
            }

            ColorComboBox.SelectedValue = Options.Color;
            EnsureNumericOption(StrokeComboBox, Options.StrokeWidth);
            EnsureNumericOption(FontSizeComboBox, Options.FontSize);
            EnsureNumericOption(BlurComboBox, Options.BlurRadius);
            UpdateTextEntryStyle();
        }
        finally
        {
            isInitializing = false;
        }
    }

    private static void EnsureNumericOption(System.Windows.Controls.ComboBox comboBox, double value)
    {
        if (!comboBox.Items.Cast<ComboBoxItem>().Any(item => Equals(item.Tag, value)))
        {
            comboBox.Items.Add(new ComboBoxItem { Content = $"{value:0.##} px", Tag = value });
        }

        comboBox.SelectedValue = value;
    }

    private void UpdateToolOptions()
    {
        var kind = isCropping ? null : isSelecting ? SelectedAnnotation?.Kind : selectedTool;
        ColorOptionPanel.Visibility = kind is null or ScreenshotAnnotationKind.Blur ? Visibility.Collapsed : Visibility.Visible;
        StrokeOptionPanel.Visibility = kind is null or ScreenshotAnnotationKind.Text or ScreenshotAnnotationKind.Blur ? Visibility.Collapsed : Visibility.Visible;
        FontSizeOptionPanel.Visibility = kind == ScreenshotAnnotationKind.Text ? Visibility.Visible : Visibility.Collapsed;
        BlurOptionPanel.Visibility = kind == ScreenshotAnnotationKind.Blur ? Visibility.Visible : Visibility.Collapsed;
        SelectionCommandsPanel.Visibility = isSelecting ? Visibility.Visible : Visibility.Collapsed;
        CropCommandsPanel.Visibility = isCropping ? Visibility.Visible : Visibility.Collapsed;
        DeleteAnnotationButton.IsEnabled = SelectedAnnotation is not null;
        EditTextButton.Visibility = isSelecting && kind == ScreenshotAnnotationKind.Text ? Visibility.Visible : Visibility.Collapsed;
        ActiveToolText.Text = isCropping ? "Crop" : isSelecting ? "Select" : selectedTool == ScreenshotAnnotationKind.Blur ? "Blur area" : selectedTool.ToString();
    }

    private void HandleDeleteAnnotation(object sender, RoutedEventArgs e) => DeleteSelectedAnnotation();
    private void HandleEditSelectedText(object sender, RoutedEventArgs e) => BeginSelectedTextEdit();

    private double ViewScale
    {
        get
        {
            if (!IsLoaded)
            {
                return 1;
            }

            var transform = AnnotationCanvas.TransformToAncestor(ImageViewport);
            return Math.Max(MinimumViewScale, (transform.Transform(new Point(1, 0)) - transform.Transform(new Point())).Length);
        }
    }

    private IEnumerable<(ScreenshotResizeHandle Handle, Point Position)> GetSelectionHandles(ScreenshotAnnotation annotation)
    {
        var transform = document.GetAnnotationToOutput(annotation);
        return GetLocalSelectionHandles(annotation).Select(handle => (handle.Handle, transform.Transform(handle.Position)));
    }

    private static IEnumerable<(ScreenshotResizeHandle Handle, Point Position)> GetLocalSelectionHandles(ScreenshotAnnotation annotation)
    {
        if (annotation.Kind == ScreenshotAnnotationKind.Text)
        {
            yield break;
        }

        if (annotation.Kind == ScreenshotAnnotationKind.Arrow)
        {
            yield return (ScreenshotResizeHandle.Start, annotation.Start);
            yield return (ScreenshotResizeHandle.End, annotation.End);
            yield break;
        }

        var bounds = ScreenshotAnnotationGeometry.GetBounds(annotation);
        yield return (ScreenshotResizeHandle.TopLeft, bounds.TopLeft);
        yield return (ScreenshotResizeHandle.TopRight, bounds.TopRight);
        yield return (ScreenshotResizeHandle.BottomLeft, bounds.BottomLeft);
        yield return (ScreenshotResizeHandle.BottomRight, bounds.BottomRight);
    }

    private ScreenshotResizeHandle? HitTestSelectionHandle(Point point)
    {
        if (SelectedAnnotation is not { } annotation)
        {
            return null;
        }

        var tolerance = SelectionHitTolerance / ViewScale;

        foreach (var (handle, position) in GetSelectionHandles(annotation))
        {
            if (Math.Abs(point.X - position.X) <= tolerance && Math.Abs(point.Y - position.Y) <= tolerance)
            {
                return handle;
            }
        }

        return null;
    }

    private void DrawSelection(ScreenshotAnnotation? preview = null)
    {
        if (isCropping)
        {
            DrawCropSelection();
            return;
        }

        AnnotationCanvas.Children.Clear();

        if (!isSelecting || (preview ?? SelectedAnnotation) is not { } annotation)
        {
            return;
        }

        var scale = ViewScale;
        var bounds = new MatrixTransform(document.GetAnnotationToOutput(annotation)).TransformBounds(ScreenshotAnnotationGeometry.GetBounds(annotation));
        var brush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var outline = new Rectangle
        {
            Width = Math.Max(bounds.Width, SelectionOutlineWidth / scale),
            Height = Math.Max(bounds.Height, SelectionOutlineWidth / scale),
            Stroke = brush,
            StrokeThickness = SelectionOutlineWidth / scale,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, bounds.Left);
        Canvas.SetTop(outline, bounds.Top);
        AnnotationCanvas.Children.Add(outline);
        var handleSize = SelectionHandleSize / scale;

        foreach (var (_, position) in GetSelectionHandles(annotation))
        {
            var handle = new Rectangle
            {
                Width = handleSize,
                Height = handleSize,
                Fill = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                Stroke = brush,
                StrokeThickness = SelectionOutlineWidth / scale,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, position.X - handleSize / 2);
            Canvas.SetTop(handle, position.Y - handleSize / 2);
            AnnotationCanvas.Children.Add(handle);
        }
    }

    private static void ValidateSelectionPoint(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Selection coordinates must be finite.");
        }
    }

    private static double MinimumAxisScale(Matrix transform) => Math.Min(
        new Vector(transform.M11, transform.M12).Length, new Vector(transform.M21, transform.M22).Length);
}
