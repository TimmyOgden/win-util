using System.Drawing;
using System.Windows.Forms;

namespace WinUtil.Services;

internal sealed class TrayIconController : IDisposable
{
    private const int WarningDisplayDurationMilliseconds = 5000;

    private readonly ContextMenuStrip contextMenu;
    private readonly NotifyIcon notificationIcon;
    private readonly Icon trayIcon;
    private bool disposed;

    internal TrayIconController(Action showWidget, Action showSettings, Action exitApplication, Action openImage, Action pasteImage, Action showScreenshotHistory)
    {
        contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Show widget", null, (_, _) => showWidget());
        contextMenu.Items.Add("Settings", null, (_, _) => showSettings());
        contextMenu.Items.Add("Open image…", null, (_, _) => openImage());
        contextMenu.Items.Add("Paste image", null, (_, _) => pasteImage());
        contextMenu.Items.Add("Screenshot history", null, (_, _) => showScreenshotHistory());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => exitApplication());

        trayIcon = WinUtilTrayIcon.Create();
        notificationIcon = new NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = trayIcon,
            Text = "WinUtil",
            Visible = true
        };
        notificationIcon.DoubleClick += HandleDoubleClick;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        notificationIcon.DoubleClick -= HandleDoubleClick;
        notificationIcon.Visible = false;
        notificationIcon.Dispose();
        trayIcon.Dispose();
        contextMenu.Dispose();
        disposed = true;
    }

    internal void ShowWarning(string title, string message) =>
        notificationIcon.ShowBalloonTip(WarningDisplayDurationMilliseconds, title, message, ToolTipIcon.Warning);

    private void HandleDoubleClick(object? sender, EventArgs e)
    {
        contextMenu.Items[0].PerformClick();
    }
}
