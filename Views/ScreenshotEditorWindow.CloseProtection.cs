using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace WinUtil.Views;

public partial class ScreenshotEditorWindow
{
    private readonly Func<ScreenshotCloseChoice> promptClose;
    private readonly Func<string?> selectSavePath;
    private readonly Action<BitmapSource> copyImage;
    private ScreenshotDocumentState exportedState;
    private bool closeConfirmed;

    internal bool HasUnsavedChanges => !document.HasSameContent(exportedState) || HasPendingTextChanges;

    internal bool ConfirmClose()
    {
        if (isClosed || !HasUnsavedChanges)
        {
            return true;
        }

        return promptClose() switch
        {
            ScreenshotCloseChoice.Save => TrySaveImage(),
            ScreenshotCloseChoice.Discard => true,
            ScreenshotCloseChoice.Cancel => false,
            _ => throw new InvalidOperationException("Unsupported screenshot close choice.")
        };
    }

    internal void CloseAfterConfirmation()
    {
        if (isClosed)
        {
            return;
        }

        closeConfirmed = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var alreadyConfirmed = closeConfirmed;
        closeConfirmed = false;

        if (!alreadyConfirmed && !ConfirmClose())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private ScreenshotCloseChoice PromptClose()
    {
        var dialog = new ScreenshotCloseDialog(this);
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private string? SelectSavePath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save edited screenshot",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private bool TrySaveImage()
    {
        FinishTextEntry(commit: true);
        CancelDrag();

        try
        {
            var filePath = selectSavePath();

            if (filePath is null)
            {
                return false;
            }

            SavePng(filePath);
            SetStatus($"Saved {Path.GetFileName(filePath)}.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException or NotSupportedException or ArgumentException)
        {
            SetStatus($"Could not save the image. Choose another location and try again. {exception.Message}", isError: true);
            return false;
        }
    }

    private void MarkExported(ScreenshotDocumentState snapshot)
    {
        exportedState = snapshot;
        UpdateUnsavedTitle();
    }

    private void UpdateUnsavedTitle() => Title = HasUnsavedChanges ? "Screenshot editor — Unsaved changes" : "Screenshot editor";
}
