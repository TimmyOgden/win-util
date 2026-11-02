using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Shell;
using WinUtil.Services;
using WinUtil.Views.Controls;
using Button = System.Windows.Controls.Button;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using Orientation = System.Windows.Controls.Orientation;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace WinUtil.Views;

internal sealed class ScreenshotResizeDialog : Window
{
    private const double DialogWidth = 390;
    private const double DialogPadding = 24;
    private const double ControlSpacing = 8;
    private readonly int originalWidth;
    private readonly int originalHeight;
    private bool updatingFields;

    internal ScreenshotResizeDialog(Window owner, int currentWidth, int currentHeight)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var initial = ScreenshotImageSize.ResizeToWidth(currentWidth, currentHeight, currentWidth);
        originalWidth = currentWidth;
        originalHeight = currentHeight;
        PixelWidth = initial.Width;
        PixelHeight = initial.Height;
        Owner = owner;
        Title = "Resize image";
        Width = DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Icon = (System.Windows.Media.ImageSource)FindResource("ScreenshotWindowIcon");
        ShowInTaskbar = false;
        Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
        var panel = new StackPanel { Margin = new Thickness(DialogPadding) };
        panel.Children.Add(new TextBlock { Text = "Resize image", Style = (Style)FindResource("SubtitleText") });
        panel.Children.Add(new TextBlock
        {
            Text = "Width and height stay in proportion.",
            Style = (Style)FindResource("SecondaryText"),
            Margin = new Thickness(0, ControlSpacing, 0, ControlSpacing)
        });
        WidthInput = CreateInput("Width in pixels", currentWidth, panel);
        HeightInput = CreateInput("Height in pixels", currentHeight, panel);
        ErrorText = new TextBlock
        {
            Style = (Style)FindResource("ErrorText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, ControlSpacing, 0, ControlSpacing),
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(ErrorText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("SecondaryButton") };
        ApplyButton = new Button
        {
            Content = "Resize",
            IsDefault = true,
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(ControlSpacing, 0, 0, 0)
        };
        ApplyButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ApplyButton);
        panel.Children.Add(buttons);
        var titleBar = new ScreenshotTitleBar();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = titleBar.Height,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        var root = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);
        root.Children.Add(panel);
        Content = root;
        WidthInput.TextChanged += (_, _) => UpdateSize(fromWidth: true);
        HeightInput.TextChanged += (_, _) => UpdateSize(fromWidth: false);
    }

    internal int PixelWidth { get; private set; }
    internal int PixelHeight { get; private set; }
    internal TextBox WidthInput { get; }
    internal TextBox HeightInput { get; }
    internal Button ApplyButton { get; }
    internal TextBlock ErrorText { get; }

    private TextBox CreateInput(string label, int value, StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, ControlSpacing, 0, ControlSpacing) });
        var input = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Style = (Style)FindResource("SettingsTextBox") };
        AutomationProperties.SetName(input, label);
        panel.Children.Add(input);
        return input;
    }

    private void UpdateSize(bool fromWidth)
    {
        if (updatingFields) { return; }
        updatingFields = true;
        try
        {
            var input = fromWidth ? WidthInput : HeightInput;
            if (!int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var dimension))
            {
                throw new ArgumentException("Enter a whole number of pixels.");
            }
            var size = fromWidth ? ScreenshotImageSize.ResizeToWidth(originalWidth, originalHeight, dimension)
                : ScreenshotImageSize.ResizeToHeight(originalWidth, originalHeight, dimension);
            PixelWidth = size.Width;
            PixelHeight = size.Height;
            if (fromWidth) { HeightInput.Text = size.Height.ToString(CultureInfo.CurrentCulture); }
            else { WidthInput.Text = size.Width.ToString(CultureInfo.CurrentCulture); }
            ErrorText.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = true;
        }
        catch (ArgumentException exception)
        {
            ErrorText.Text = exception.Message;
            ErrorText.Visibility = Visibility.Visible;
            ApplyButton.IsEnabled = false;
        }
        finally { updatingFields = false; }
    }
}
