using System.Windows;
using System.Windows.Shell;
using WinUtil.Views.Controls;
using Button = System.Windows.Controls.Button;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;

namespace WinUtil.Views;

internal enum ScreenshotCloseChoice
{
    Cancel,
    Save,
    Discard
}

internal sealed class ScreenshotCloseDialog : Window
{
    private const double DialogWidth = 440;
    private const double DialogPadding = 24;
    private const double ButtonSpacing = 8;

    internal ScreenshotCloseDialog(Window owner)
    {
        Owner = owner;
        Title = "Unsaved screenshot";
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
        panel.Children.Add(new TextBlock
        {
            Text = "Save your screenshot before closing?",
            Style = (Style)FindResource("SubtitleText"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Your latest edits have not been saved or copied.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, ButtonSpacing, 0, DialogPadding)
        });
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        foreach (var (label, choice) in new[]
        {
            ("Cancel", ScreenshotCloseChoice.Cancel),
            ("Discard", ScreenshotCloseChoice.Discard),
            ("Save", ScreenshotCloseChoice.Save)
        })
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource(choice == ScreenshotCloseChoice.Save ? "PrimaryButton" : "SecondaryButton"),
                Margin = new Thickness(ButtonSpacing, 0, 0, 0),
                IsCancel = choice == ScreenshotCloseChoice.Cancel,
                IsDefault = choice == ScreenshotCloseChoice.Save
            };
            button.Click += (_, _) =>
            {
                Choice = choice;
                DialogResult = choice != ScreenshotCloseChoice.Cancel;
            };
            buttons.Children.Add(button);
        }

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
    }

    internal ScreenshotCloseChoice Choice { get; private set; }
}
