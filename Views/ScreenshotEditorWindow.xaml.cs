using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Services;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Border = System.Windows.Controls.Border;
using Cursors = System.Windows.Input.Cursors;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using Rectangle = System.Windows.Shapes.Rectangle;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Shape = System.Windows.Shapes.Shape;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace WinUtil.Views;

public partial class ScreenshotEditorWindow : Window
{
    private const int ClipboardAttempts = 5;
    private const int ClipboardRetryDelayMilliseconds = 80;
    private const double MinimumDragDistance = 1;
    private const double ArrowHeadStrokeMultiplier = 4;
    private const double MinimumArrowHeadLength = 12;
    private const double ArrowHeadLengthRatio = 0.5;
    private const double ArrowHeadWidthRatio = 0.5;
    private const double PreviewOpacity = 0.8;
    private const double LastPixelOffset = 1;
    private const double TextEditorMaximumWidth = 360;
    private const double TextEditorMaximumHeight = 240;
    private const double TextEditorPadding = 8;
    private const double TextEditorInset = TextEditorPadding / 2;
    private const double ColorSwatchSize = 12;
    private const double ColorSwatchSpacing = 8;
    private const double ReadableOnDarkThreshold = 0.55;
    private const double RedBrightnessWeight = 0.299;
    private const double GreenBrightnessWeight = 0.587;
    private const double BlueBrightnessWeight = 0.114;
    private const string DefaultInstruction = "Drag on the screenshot to add an arrow.";

    private readonly ScreenshotDocument document;
    private ScreenshotAnnotationKind selectedTool = ScreenshotAnnotationKind.Arrow;
    private Point? dragStart;
    private Point? textAnchor;
    private bool isInitializing = true;
    private bool isCopying;
    private bool isClosed;

    internal ScreenshotEditorWindow(
        BitmapSource image,
        ScreenshotEditorOptions? options = null,
        Func<ScreenshotCloseChoice>? promptClose = null,
        Func<string?>? selectSavePath = null,
        Action<BitmapSource>? copyImage = null,
        Action<BitmapSource>? openImageEditor = null,
        Func<string?>? selectOpenPath = null,
        Func<BitmapSource?>? readClipboardImage = null,
        Action? showHistory = null)
    {
        document = new ScreenshotDocument(image);
        exportedState = document.State;
        this.promptClose = promptClose ?? PromptClose;
        this.selectSavePath = selectSavePath ?? SelectSavePath;
        this.copyImage = copyImage ?? Clipboard.SetImage;
        InitialOptions = (options ?? new ScreenshotEditorOptions()).Normalize();
        Options = InitialOptions;
        InitializeComponent();
        this.showHistory = showHistory;
        HistoryButton.IsEnabled = showHistory is not null;
        InitializeImageImport(openImageEditor, selectOpenPath, readClipboardImage);
        AnnotationTextBox.MaxLength = ScreenshotDocument.MaximumTextLength;
        PopulateOptions();
        ImageSurface.Width = document.Width;
        ImageSurface.Height = document.Height;
        AnnotationCanvas.Width = document.Width;
        AnnotationCanvas.Height = document.Height;
        ImageDetailsText.Text = $"{document.Width:N0} × {document.Height:N0} px";
        isInitializing = false;
        RefreshImage();
        SetStatus(DefaultInstruction);
        Closed += (_, _) => isClosed = true;
        AnnotationTextBox.TextChanged += (_, _) => UpdateUnsavedTitle();
        ImageViewbox.SizeChanged += (_, _) => DrawSelection();
    }

    internal ScreenshotEditorOptions Options { get; private set; }

    private readonly Action? showHistory;

    internal event Action<BitmapSource>? ImageExported;

    private void HandleShowHistory(object sender, RoutedEventArgs e) => showHistory?.Invoke();

    internal ScreenshotEditorOptions InitialOptions { get; }

    internal BitmapSource RenderedImage => (BitmapSource)ScreenshotImage.Source;

    internal void SavePng(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FinishTextEntry(commit: true);
        CancelDrag();
        var snapshot = document.State;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderedImage));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        File.WriteAllBytes(filePath, stream.ToArray());
        MarkExported(snapshot);
        ImageExported?.Invoke(RenderedImage);
    }

    internal void CommitAnnotation(Point start, Point end)
    {
        var first = selectedTool == ScreenshotAnnotationKind.Text
            ? new Point(Math.Clamp(start.X, 0, document.Width - LastPixelOffset), Math.Clamp(start.Y, 0, document.Height - LastPixelOffset))
            : ClampPoint(start);
        var last = ClampPoint(end);

        if (selectedTool == ScreenshotAnnotationKind.Text && string.IsNullOrWhiteSpace(AnnotationTextBox.Text))
        {
            SetStatus("Enter text on the screenshot, then press Ctrl+Enter to place it.", isError: true);
            AnnotationTextBox.Focus();
            return;
        }

        if (selectedTool != ScreenshotAnnotationKind.Text && (last - first).Length < MinimumDragDistance)
        {
            return;
        }

        if (selectedTool is ScreenshotAnnotationKind.Rectangle or ScreenshotAnnotationKind.Ellipse or ScreenshotAnnotationKind.Blur
            && (Math.Abs(last.X - first.X) < MinimumDragDistance || Math.Abs(last.Y - first.Y) < MinimumDragDistance))
        {
            return;
        }

        document.Add(new ScreenshotAnnotation(
            selectedTool,
            first,
            last,
            (Color)ColorConverter.ConvertFromString(Options.Color),
            Options.StrokeWidth,
            AnnotationTextBox.Text,
            Options.FontSize,
            Options.BlurRadius) { LocalToSource = document.OutputToSource });
        RefreshImage();
        SetStatus(selectedTool == ScreenshotAnnotationKind.Blur
            ? "Area blurred. Copy or save to export the edited image."
            : "Annotation added. Ctrl+Z to undo.");
    }

    internal void BeginTextEntry(Point point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Text coordinates must be finite.");
        }

        TextTool.IsChecked = true;
        FinishTextEntry(commit: true);
        editingAnnotationIndex = null;
        var anchor = new Point(
            Math.Clamp(point.X, 0, document.Width - LastPixelOffset),
            Math.Clamp(point.Y, 0, document.Height - LastPixelOffset));
        OpenTextEntry(anchor, string.Empty);
    }

    private void OpenTextEntry(Point anchor, string text)
    {
        textAnchor = anchor;
        AnnotationTextBox.Text = text;
        AnnotationTextBox.Width = Math.Max(LastPixelOffset, Math.Min(TextEditorMaximumWidth, document.Width - anchor.X) - TextEditorPadding);
        AnnotationTextBox.MaxHeight = Math.Max(LastPixelOffset, Math.Min(TextEditorMaximumHeight, document.Height - anchor.Y) - TextEditorPadding);
        UpdateTextEntryStyle();
        Canvas.SetLeft(TextInputPanel, anchor.X - TextEditorInset);
        Canvas.SetTop(TextInputPanel, anchor.Y - TextEditorInset);
        TextInputPanel.Visibility = Visibility.Visible;
        AnnotationTextBox.Focus();
        SetStatus("Type here. Ctrl+Enter to place text; Esc to cancel.");
    }

    internal void FinishTextEntry(bool commit)
    {
        if (textAnchor is not Point anchor)
        {
            return;
        }

        textAnchor = null;
        TextInputPanel.Visibility = Visibility.Collapsed;

        if (commit)
        {
            if (editingAnnotationIndex is int index)
            {
                if (string.IsNullOrWhiteSpace(AnnotationTextBox.Text))
                {
                    document.Remove(index);
                    selectedAnnotationIndex = null;
                    UpdateToolOptions();
                }
                else
                {
                    document.Replace(index, CreateTextAnnotation(anchor));
                }

                RefreshImage();
            }
            else if (!string.IsNullOrWhiteSpace(AnnotationTextBox.Text))
            {
                CommitAnnotation(anchor, anchor);
            }
        }

        editingAnnotationIndex = null;
        AnnotationTextBox.Text = string.Empty;
        UpdateUnsavedTitle();
    }

    private void UpdateTextEntryStyle()
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Options.Color));
        AnnotationTextBox.Foreground = brush;
        AnnotationTextBox.CaretBrush = brush;
        AnnotationTextBox.FontSize = Options.FontSize;
        var brightness = (brush.Color.R * RedBrightnessWeight + brush.Color.G * GreenBrightnessWeight + brush.Color.B * BlueBrightnessWeight) / byte.MaxValue;
        TextInputPanel.Background = brightness >= ReadableOnDarkThreshold
            ? (System.Windows.Media.Brush)FindResource("SurfaceBrush")
            : Brushes.White;
    }

    private void PopulateOptions()
    {
        foreach (var (name, color) in new[]
        {
            ("Red", ScreenshotEditorOptions.DefaultColor),
            ("Blue", "#FF52A8FF"),
            ("Yellow", "#FFFFD952"),
            ("Green", "#FF71E5A0"),
            ("White", "#FFFFFFFF"),
            ("Black", "#FF111827")
        })
        {
            ColorComboBox.Items.Add(CreateColorOption(name, color));
        }

        if (!ColorComboBox.Items.Cast<ComboBoxItem>().Any(item => Equals(item.Tag, Options.Color)))
        {
            ColorComboBox.Items.Add(CreateColorOption("Custom", Options.Color));
        }

        ColorComboBox.SelectedValue = Options.Color;
        PopulateNumericOptions(StrokeComboBox, new double[] { 2, 4, 6, 8, 12, 24, 48, 100 }, Options.StrokeWidth);
        PopulateNumericOptions(FontSizeComboBox, new double[] { 8, 16, 24, 32, 48, 64, 100, 200 }, Options.FontSize);
        PopulateNumericOptions(BlurComboBox, new double[] { 4, 8, 12, 20, 32, 48, 64 }, Options.BlurRadius);
    }

    private static ComboBoxItem CreateColorOption(string name, string color)
    {
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new Border
        {
            Width = ColorSwatchSize,
            Height = ColorSwatchSize,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, ColorSwatchSpacing, 0)
        });
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Content = content, Tag = color };
        System.Windows.Automation.AutomationProperties.SetName(item, name);
        return item;
    }

    private static void PopulateNumericOptions(ComboBox comboBox, IEnumerable<double> values, double selected)
    {
        foreach (var value in values.Append(selected).Distinct().Order())
        {
            comboBox.Items.Add(new ComboBoxItem { Content = $"{value:0.##} px", Tag = value });
        }

        comboBox.SelectedValue = selected;
    }

    private void HandleOptionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (isInitializing)
        {
            return;
        }

        Options = new ScreenshotEditorOptions
        {
            Color = (string)ColorComboBox.SelectedValue,
            StrokeWidth = (double)StrokeComboBox.SelectedValue,
            FontSize = (double)FontSizeComboBox.SelectedValue,
            BlurRadius = (double)BlurComboBox.SelectedValue
        }.Normalize();
        UpdateTextEntryStyle();
        ApplySelectedOptions();
    }

    private void HandleToolChecked(object sender, RoutedEventArgs e)
    {
        if (isInitializing || sender is not RadioButton { Tag: string toolName })
        {
            return;
        }

        FinishTextEntry(commit: true);
        CancelDrag();
        CancelCropSelection();
        isCropping = toolName == "Crop";
        isSelecting = toolName == "Select";
        selectedAnnotationIndex = null;

        if (isCropping)
        {
            UpdateToolOptions();
            AnnotationCanvas.Cursor = Cursors.Cross;
            SetStatus("Drag to select a crop area, then choose Apply crop. Escape cancels.");
            AnnotationCanvas.Focus();
            return;
        }

        if (isSelecting)
        {
            UpdateToolOptions();
            DrawSelection();
            AnnotationCanvas.Cursor = Cursors.Arrow;
            SetStatus("Select an annotation to move, resize or delete it. Double-click text to edit.");
            AnnotationCanvas.Focus();
            return;
        }

        selectedTool = Enum.Parse<ScreenshotAnnotationKind>(toolName);
        UpdateToolOptions();
        DrawSelection();
        AnnotationCanvas.Cursor = selectedTool == ScreenshotAnnotationKind.Text ? Cursors.IBeam : Cursors.Cross;
        SetStatus(selectedTool switch
        {
            ScreenshotAnnotationKind.Text => "Click the screenshot to type text. Ctrl+Enter to place it.",
            ScreenshotAnnotationKind.Blur => "Drag over the area to blur. The change is applied to copied and saved images.",
            ScreenshotAnnotationKind.Rectangle => "Drag on the screenshot to add a rectangle.",
            ScreenshotAnnotationKind.Ellipse => "Drag on the screenshot to add an ellipse.",
            _ => DefaultInstruction
        });

        AnnotationCanvas.Focus();
    }

    private void HandleCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        AnnotationCanvas.Focus();

        if (isCropping)
        {
            BeginCropSelection(e.GetPosition(AnnotationCanvas));
            AnnotationCanvas.CaptureMouse();
        }
        else if (isSelecting)
        {
            if (e.ClickCount == 2)
            {
                SelectAnnotationAt(e.GetPosition(AnnotationCanvas));
                BeginSelectedTextEdit();
            }
            else if (BeginSelectionDrag(e.GetPosition(AnnotationCanvas)))
            {
                AnnotationCanvas.CaptureMouse();
            }
        }
        else if (selectedTool == ScreenshotAnnotationKind.Text)
        {
            BeginTextEntry(e.GetPosition(AnnotationCanvas));
        }
        else
        {
            dragStart = ClampPoint(e.GetPosition(AnnotationCanvas));
            AnnotationCanvas.CaptureMouse();
        }

        e.Handled = true;
    }

    private void HandleTextEntryKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FinishTextEntry(commit: false);
            SetStatus("Text cancelled. Click the screenshot to type again.");
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FinishTextEntry(commit: true);
        }
        else
        {
            return;
        }

        AnnotationCanvas.Focus();
        e.Handled = true;
    }

    private void HandleWorkspaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (textAnchor is null || TextInputPanel.IsMouseOver)
        {
            return;
        }

        FinishTextEntry(commit: true);
        AnnotationCanvas.Focus();
        e.Handled = true;
    }

    private void HandleZoomChanged(object sender, RoutedEventArgs e)
    {
        if (isInitializing || sender is not RadioButton { Tag: string zoom })
        {
            return;
        }

        CancelDrag();
        var actualSize = zoom == "Actual";
        ImageViewbox.Stretch = actualSize ? Stretch.None : Stretch.Uniform;
        ImageScrollViewer.HorizontalScrollBarVisibility = actualSize ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ImageScrollViewer.VerticalScrollBarVisibility = actualSize ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ImageScrollViewer.ScrollToHome();
    }

    private void HandleCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (cropDragStart is not null)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                PreviewCropSelection(e.GetPosition(AnnotationCanvas));
            }
            else
            {
                CancelCropSelection();
            }

            return;
        }

        if (selectionDragStart is not null)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                PreviewSelectionDrag(e.GetPosition(AnnotationCanvas));
            }
            else
            {
                CancelDrag();
            }

            return;
        }

        if (dragStart is not Point start)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDrag();
            return;
        }

        DrawPreview(start, ClampPoint(e.GetPosition(AnnotationCanvas)));
    }

    private void HandleCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (cropDragStart is not null)
        {
            CompleteCropSelection(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
            return;
        }

        if (selectionDragStart is not null)
        {
            CompleteSelectionDrag(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
            return;
        }

        if (dragStart is not Point start)
        {
            return;
        }

        var end = ClampPoint(e.GetPosition(AnnotationCanvas));
        CancelDrag();
        CommitAnnotation(start, end);
        e.Handled = true;
    }

    private void HandleLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (cropDragStart is not null)
        {
            CancelCropSelection();
        }

        CancelDrag();
        DrawCropSelection();
    }

    private void CancelDrag()
    {
        var hadSelectionPreview = selectionDragStart is not null;
        selectionDragStart = null;
        selectionDragOriginal = null;
        selectionDragPreview = null;
        dragStart = null;
        AnnotationCanvas.Children.Clear();

        if (hadSelectionPreview)
        {
            ScreenshotImage.Source = document.Render();
        }

        if (AnnotationCanvas.IsMouseCaptured)
        {
            AnnotationCanvas.ReleaseMouseCapture();
        }

        DrawSelection();
    }

    private void DrawPreview(Point start, Point end)
    {
        AnnotationCanvas.Children.Clear();
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Options.Color));
        Shape preview;

        if (selectedTool == ScreenshotAnnotationKind.Arrow)
        {
            var direction = end - start;

            if (direction.Length < MinimumDragDistance)
            {
                return;
            }

            var headLength = Math.Min(Math.Max(Options.StrokeWidth * ArrowHeadStrokeMultiplier, MinimumArrowHeadLength), direction.Length * ArrowHeadLengthRatio);
            direction.Normalize();
            var headBase = end - direction * headLength;
            var perpendicular = new Vector(-direction.Y, direction.X) * headLength * ArrowHeadWidthRatio;
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.LineTo(end, true, false);
                context.BeginFigure(headBase + perpendicular, false, false);
                context.LineTo(end, true, false);
                context.LineTo(headBase - perpendicular, true, false);
            }

            preview = new System.Windows.Shapes.Path { Data = geometry };
        }
        else
        {
            preview = selectedTool == ScreenshotAnnotationKind.Ellipse ? new Ellipse() : new Rectangle();
            preview.Width = Math.Abs(end.X - start.X);
            preview.Height = Math.Abs(end.Y - start.Y);
            Canvas.SetLeft(preview, Math.Min(start.X, end.X));
            Canvas.SetTop(preview, Math.Min(start.Y, end.Y));

            if (selectedTool == ScreenshotAnnotationKind.Blur)
            {
                preview.Fill = (System.Windows.Media.Brush)FindResource("AccentMutedBrush");
                preview.StrokeDashArray = new DoubleCollection { 4, 4 };
                brush = (SolidColorBrush)FindResource("AccentBrush");
            }
        }

        preview.Stroke = brush;
        preview.StrokeThickness = Options.StrokeWidth;
        preview.Opacity = PreviewOpacity;
        preview.IsHitTestVisible = false;
        AnnotationCanvas.Children.Add(preview);
    }

    private Point ClampPoint(Point point) => new(
        Math.Clamp(point.X, 0, document.Width),
        Math.Clamp(point.Y, 0, document.Height));

    private void RefreshImage()
    {
        ImageSurface.Width = document.Width;
        ImageSurface.Height = document.Height;
        AnnotationCanvas.Width = document.Width;
        AnnotationCanvas.Height = document.Height;
        ImageDetailsText.Text = $"{document.Width:N0} × {document.Height:N0} px";
        ScreenshotImage.Source = document.Render();
        UndoButton.IsEnabled = document.CanUndo;
        RedoButton.IsEnabled = document.CanRedo;
        DrawSelection();
        UpdateUnsavedTitle();
    }

    private void HandleUndo(object sender, RoutedEventArgs e)
    {
        FinishTextEntry(commit: true);
        CancelDrag();
        CancelCropSelection();
        document.Undo();
        selectedAnnotationIndex = null;
        UpdateToolOptions();
        RefreshImage();
        SetStatus("Change undone.");
    }

    private void HandleRedo(object sender, RoutedEventArgs e)
    {
        FinishTextEntry(commit: true);
        CancelDrag();
        CancelCropSelection();
        document.Redo();
        selectedAnnotationIndex = null;
        UpdateToolOptions();
        RefreshImage();
        SetStatus("Change restored.");
    }

    private async void HandleCopy(object sender, RoutedEventArgs e) => await CopyImageAsync();

    internal async Task CopyImageAsync()
    {
        if (isCopying)
        {
            return;
        }

        FinishTextEntry(commit: true);
        CancelDrag();
        isCopying = true;
        CopyButton.IsEnabled = false;

        try
        {
            var image = RenderedImage;
            var snapshot = document.State;

            for (var attempt = 0; attempt < ClipboardAttempts; attempt++)
            {
                if (isClosed)
                {
                    return;
                }

                try
                {
                    copyImage(image);
                    MarkExported(snapshot);
                    ImageExported?.Invoke(image);
                    SetStatus("Edited image copied to the clipboard.");
                    return;
                }
                catch (ExternalException) when (attempt < ClipboardAttempts - 1)
                {
                    await Task.Delay(ClipboardRetryDelayMilliseconds);
                }
            }
        }
        catch (ExternalException exception)
        {
            SetStatus($"Could not copy the image. Try again after closing other clipboard tools. {exception.Message}", isError: true);
        }
        finally
        {
            isCopying = false;
            CopyButton.IsEnabled = true;
        }
    }

    private void HandleSave(object sender, RoutedEventArgs e) => TrySaveImage();

    private async void HandlePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (HandleImageImportKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && isCropping)
        {
            CancelCropMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && isCropping && Keyboard.FocusedElement is not TextBoxBase)
        {
            ApplyCropSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && textAnchor is not null)
        {
            FinishTextEntry(commit: false);
            AnnotationCanvas.Focus();
            SetStatus("Text cancelled. Click the screenshot to type again.");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && (dragStart is not null || selectionDragStart is not null))
        {
            CancelDrag();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBoxBase and not ComboBox)
        {
            if (isSelecting && e.Key == Key.Delete)
            {
                DeleteSelectedAnnotation();
                e.Handled = true;
                return;
            }

            if (isSelecting && e.Key == Key.Enter)
            {
                BeginSelectedTextEdit();
                e.Handled = true;
                return;
            }

            var tool = e.Key switch
            {
                Key.V => SelectTool,
                Key.A => ArrowTool,
                Key.R => RectangleTool,
                Key.E => EllipseTool,
                Key.T => TextTool,
                Key.B => BlurTool,
                Key.C => CropTool,
                _ => null
            };

            if (tool is not null)
            {
                tool.IsChecked = true;
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control || Keyboard.FocusedElement is TextBoxBase && e.Key != Key.S)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when document.CanUndo:
                HandleUndo(this, e);
                break;
            case Key.Y when document.CanRedo:
                HandleRedo(this, e);
                break;
            case Key.C:
                e.Handled = true;
                await CopyImageAsync();
                break;
            case Key.S:
                HandleSave(this, e);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    internal bool HandleImageImportKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control)
        {
            return false;
        }

        if (key == Key.O)
        {
            OpenImage();
            return true;
        }

        if (key == Key.V && Keyboard.FocusedElement is not TextBoxBase)
        {
            PasteImage();
            return true;
        }

        return false;
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ErrorBrush" : "TextSecondaryBrush");
    }
}
