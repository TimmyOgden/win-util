using System.Windows;
using System.Windows.Media;
using WinUtil.Services;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WinUtil.Views;

public partial class ScreenshotEditorWindow
{
    private const double CropMaskOpacity = 0.45;
    private const double CropOutlineWidth = 1;
    private bool isCropping;
    private Point? cropDragStart;
    private Rect? cropSelection;

    internal Int32Rect? CropSelectionBounds
    {
        get
        {
            if (cropSelection is not Rect { Width: > 0, Height: > 0 } bounds)
            {
                return null;
            }

            var left = (int)Math.Floor(bounds.Left);
            var top = (int)Math.Floor(bounds.Top);
            var right = (int)Math.Ceiling(bounds.Right);
            var bottom = (int)Math.Ceiling(bounds.Bottom);
            return new Int32Rect(left, top, right - left, bottom - top);
        }
    }

    internal void BeginCropSelection(Point point)
    {
        ValidateSelectionPoint(point);
        CropTool.IsChecked = true;
        CancelCropSelection();
        cropDragStart = ClampPoint(point);
        cropSelection = new Rect(cropDragStart.Value, cropDragStart.Value);
        DrawCropSelection();
    }

    internal void PreviewCropSelection(Point point)
    {
        ValidateSelectionPoint(point);
        if (cropDragStart is not Point start)
        {
            return;
        }

        cropSelection = new Rect(start, ClampPoint(point));
        DrawCropSelection();
    }

    internal void CompleteCropSelection(Point point)
    {
        PreviewCropSelection(point);
        cropDragStart = null;
        if (AnnotationCanvas.IsMouseCaptured)
        {
            AnnotationCanvas.ReleaseMouseCapture();
        }

        DrawCropSelection();
    }

    internal bool ApplyCropSelection()
    {
        if (CropSelectionBounds is not Int32Rect bounds)
        {
            SetStatus("Select a nonempty area before applying the crop.", isError: true);
            return false;
        }

        return CropImage(bounds);
    }

    internal void CancelCropMode()
    {
        CancelCropSelection();
        if (isCropping)
        {
            SelectTool.IsChecked = true;
        }

        SetStatus("Crop cancelled.");
    }

    internal bool CropImage(Int32Rect bounds) => ApplyImageTransformation(() => document.Crop(bounds), "Image cropped. Ctrl+Z to undo.");

    internal bool ResizeImage(int width, int height) => ApplyImageTransformation(() => document.Resize(width, height), "Image resized. Ctrl+Z to undo.");

    internal bool RotateImageClockwise() => ApplyImageTransformation(document.RotateClockwise, "Image rotated clockwise. Ctrl+Z to undo.");

    internal bool RotateImageCounterclockwise() => ApplyImageTransformation(document.RotateCounterclockwise, "Image rotated counterclockwise. Ctrl+Z to undo.");

    private bool ApplyImageTransformation(Func<bool> transform, string status)
    {
        FinishTextEntry(commit: true);
        CancelDrag();
        CancelCropSelection();
        if (isCropping)
        {
            SelectTool.IsChecked = true;
        }

        var changed = transform();
        selectedAnnotationIndex = null;
        UpdateToolOptions();
        RefreshImage();
        ImageScrollViewer.ScrollToHome();
        SetStatus(changed ? status : "Image dimensions are unchanged.");
        return changed;
    }

    private void CancelCropSelection()
    {
        cropDragStart = null;
        cropSelection = null;
        ApplyCropButton.IsEnabled = false;
        CropSizeText.Text = string.Empty;
        AnnotationCanvas.Children.Clear();
        if (AnnotationCanvas.IsMouseCaptured)
        {
            AnnotationCanvas.ReleaseMouseCapture();
        }
    }

    private void DrawCropSelection()
    {
        if (!isCropping)
        {
            return;
        }

        AnnotationCanvas.Children.Clear();
        ApplyCropButton.IsEnabled = CropSelectionBounds.HasValue;
        CropSizeText.Text = CropSelectionBounds is Int32Rect pixels ? $"{pixels.Width:N0} × {pixels.Height:N0} px" : string.Empty;
        if (cropSelection is not Rect { Width: > 0, Height: > 0 } bounds)
        {
            return;
        }

        AddCropMask(new Rect(0, 0, document.Width, bounds.Top));
        AddCropMask(new Rect(0, bounds.Bottom, document.Width, document.Height - bounds.Bottom));
        AddCropMask(new Rect(0, bounds.Top, bounds.Left, bounds.Height));
        AddCropMask(new Rect(bounds.Right, bounds.Top, document.Width - bounds.Right, bounds.Height));
        var outline = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Stroke = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            StrokeThickness = CropOutlineWidth / ViewScale,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, bounds.Left);
        Canvas.SetTop(outline, bounds.Top);
        AnnotationCanvas.Children.Add(outline);
    }

    private void AddCropMask(Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var mask = new Rectangle { Width = bounds.Width, Height = bounds.Height, Fill = System.Windows.Media.Brushes.Black, Opacity = CropMaskOpacity, IsHitTestVisible = false };
        Canvas.SetLeft(mask, bounds.Left);
        Canvas.SetTop(mask, bounds.Top);
        AnnotationCanvas.Children.Add(mask);
    }

    private void HandleApplyCrop(object sender, RoutedEventArgs e) => ApplyCropSelection();

    private void HandleCancelCrop(object sender, RoutedEventArgs e) => CancelCropMode();

    private void HandleRotateLeft(object sender, RoutedEventArgs e) => RotateImageCounterclockwise();

    private void HandleRotateRight(object sender, RoutedEventArgs e) => RotateImageClockwise();

    private void HandleResizeImage(object sender, RoutedEventArgs e)
    {
        var dialog = new ScreenshotResizeDialog(this, document.Width, document.Height);
        if (dialog.ShowDialog() == true)
        {
            ResizeImage(dialog.PixelWidth, dialog.PixelHeight);
        }
    }
}
