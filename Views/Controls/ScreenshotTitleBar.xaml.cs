using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace WinUtil.Views.Controls;

public partial class ScreenshotTitleBar : UserControl
{
    public ScreenshotTitleBar() => InitializeComponent();

    private void HandleMinimize(object sender, RoutedEventArgs e) => Window.GetWindow(this).WindowState = WindowState.Minimized;

    private void HandleMaximize(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void HandleClose(object sender, RoutedEventArgs e) => Window.GetWindow(this).Close();
}
