using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using WinUtil.Services;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace WinUtil.Views;

public partial class ScreenshotEditorWindow
{
    private const int SingleImageFileCount = 1;
    private Action<BitmapSource> openImageEditor = null!;
    private Func<string?> selectOpenPath = null!;
    private Func<BitmapSource?> readClipboardImage = null!;

    private void InitializeImageImport(Action<BitmapSource>? openEditor, Func<string?>? selectPath, Func<BitmapSource?>? readImage)
    {
        openImageEditor = openEditor ?? (image => new ScreenshotEditorWindow(image, Options).Show());
        selectOpenPath = selectPath ?? SelectOpenImagePath;
        readClipboardImage = readImage ?? Clipboard.GetImage;
    }

    internal bool OpenImage()
    {
        try
        {
            var path = selectOpenPath();
            return path is not null && ImportImageFiles([path]);
        }
        catch (Exception exception) when (IsImageImportFailure(exception))
        {
            SetStatus($"Could not open the image. {exception.Message}", isError: true);
            return false;
        }
    }

    internal bool PasteImage()
    {
        try
        {
            var image = ScreenshotImageLoader.PrepareClipboardImage(readClipboardImage());
            openImageEditor(image);
            SetStatus("Pasted image opened in a new editor.");
            return true;
        }
        catch (Exception exception) when (IsImageImportFailure(exception))
        {
            SetStatus($"Could not paste an image. {exception.Message}", isError: true);
            return false;
        }
    }

    internal bool ImportImageFiles(IReadOnlyList<string>? paths)
    {
        try
        {
            if (paths is null || paths.Count != SingleImageFileCount)
            {
                throw new ArgumentException("Choose one PNG, JPEG, or BMP image at a time.");
            }
            var image = ScreenshotImageLoader.LoadFile(paths[0]);
            openImageEditor(image);
            SetStatus("Image opened in a new editor.");
            return true;
        }
        catch (Exception exception) when (IsImageImportFailure(exception))
        {
            SetStatus($"Could not open the image. {exception.Message}", isError: true);
            return false;
        }
    }

    private string? SelectOpenImagePath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open image in a new editor",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void HandleOpenImage(object sender, RoutedEventArgs e) => OpenImage();

    private void HandlePasteImage(object sender, RoutedEventArgs e) => PasteImage();

    private void HandleImageDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void HandleImageDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { return; }
        e.Handled = true;
        try { ImportImageFiles(e.Data.GetData(DataFormats.FileDrop) as string[]); }
        catch (Exception exception) when (IsImageImportFailure(exception))
        {
            SetStatus($"Could not read the dropped image. {exception.Message}", isError: true);
        }
    }

    private static bool IsImageImportFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or ExternalException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException;
}
