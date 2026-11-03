using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace WinUtil.Views;

public partial class ScreenshotHistoryWindow : Window
{
    private readonly ScreenshotHistoryStore store;
    private readonly Action<BitmapSource> openEditor;

    internal ScreenshotHistoryWindow(ScreenshotHistoryStore store, Action<BitmapSource> openEditor)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(openEditor);
        this.store = store;
        this.openEditor = openEditor;
        InitializeComponent();
        RefreshHistory();
    }

    internal void RefreshHistory()
    {
        try
        {
            var selectedName = (HistoryList.SelectedItem as ScreenshotHistoryEntry)?.FileName;
            var entries = store.List();
            SetStatus("Captures, imports and exported images are saved locally. Manage recording in Settings → Screenshots.");
            HistoryList.ItemsSource = entries;
            CountText.Text = entries.Count == 1 ? "1 saved image · Newest first" : $"{entries.Count:N0} saved images · Newest first";
            EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryList.SelectedItem = entries.FirstOrDefault(entry => entry.FileName == selectedName) ?? entries.FirstOrDefault();
            if (entries.Count == 0) { PreviewImage.Source = null; OpenEditorButton.IsEnabled = false; }
        }
        catch (Exception exception) when (IsHistoryFailure(exception))
        {
            SetStatus($"Could not load screenshot history. Check the history folder and refresh. {exception.Message}", isError: true);
        }
    }

    internal bool OpenSelectedImage()
    {
        if (HistoryList.SelectedItem is not ScreenshotHistoryEntry entry) { return false; }
        try
        {
            openEditor(store.Load(entry));
            SetStatus("Image opened in a new editor.");
            return true;
        }
        catch (Exception exception) when (IsHistoryFailure(exception))
        {
            SetStatus($"Could not open this history image. Refresh and select another image. {exception.Message}", isError: true);
            return false;
        }
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PreviewImage.Source = null;
        OpenEditorButton.IsEnabled = false;
        if (HistoryList.SelectedItem is not ScreenshotHistoryEntry entry) { return; }
        try
        {
            PreviewImage.Source = store.LoadPreview(entry);
            OpenEditorButton.IsEnabled = true;
            SetStatus(entry.CreatedText);
        }
        catch (Exception exception) when (IsHistoryFailure(exception))
        {
            SetStatus($"Could not preview this history image. Refresh and select another image. {exception.Message}", isError: true);
        }
    }

    private void HandleRefresh(object sender, RoutedEventArgs e) => RefreshHistory();

    private void HandleOpenEditor(object sender, RoutedEventArgs e) => OpenSelectedImage();

    private void HandleDoubleClick(object sender, MouseEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(HistoryList, e.OriginalSource as DependencyObject) is ListBoxItem)
        {
            OpenSelectedImage();
        }
    }

    private void HandleListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) { return; }
        e.Handled = true;
        OpenSelectedImage();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(isError ? "ErrorBrush" : "TextSecondaryBrush");
    }

    private static bool IsHistoryFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or ExternalException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException;
}
