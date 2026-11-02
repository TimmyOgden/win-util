using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static class ScreenshotShutdownTests
{
    private const int ImageWidth = 32;
    private const int ImageHeight = 24;
    private const int BytesPerPixel = 4;
    private const double StandardDpi = 96;
    private static readonly Point AnnotationStart = new(2, 2);
    private static readonly Point AnnotationEnd = new(16, 16);

    internal static void EmptyEditorCollectionAllowsExitAndNullIsRejected()
    {
        TestAssert.True(App.TryCloseScreenshotEditors([]));

        try
        {
            App.TryCloseScreenshotEditors(null!);
        }
        catch (ArgumentNullException)
        {
            return;
        }

        throw new InvalidOperationException("Expected a null editor collection to be rejected.");
    }

    internal static void CancelledExitKeepsEveryEditorOpenAndPromptsAgain() => RunOnStaThread(() =>
    {
        var discardPrompts = 0;
        var cancelPrompts = 0;
        var secondChoice = ScreenshotCloseChoice.Cancel;
        var first = CreateEditedWindow(() =>
        {
            discardPrompts++;
            return ScreenshotCloseChoice.Discard;
        });
        var second = CreateEditedWindow(() =>
        {
            cancelPrompts++;
            return secondChoice;
        });
        var editors = new List<ScreenshotEditorWindow> { first, second };
        first.Closed += (_, _) => editors.Remove(first);
        second.Closed += (_, _) => editors.Remove(second);

        try
        {
            TestAssert.False(App.TryCloseScreenshotEditors(editors));
            TestAssert.Equal(2, editors.Count);
            TestAssert.True(first.HasUnsavedChanges);
            TestAssert.True(second.HasUnsavedChanges);
            TestAssert.Equal(1, discardPrompts);
            TestAssert.Equal(1, cancelPrompts);

            secondChoice = ScreenshotCloseChoice.Discard;

            TestAssert.True(App.TryCloseScreenshotEditors(editors));
            TestAssert.Empty(editors);
            TestAssert.Equal(2, discardPrompts);
            TestAssert.Equal(2, cancelPrompts);
        }
        finally
        {
            first.CloseAfterConfirmation();
            second.CloseAfterConfirmation();
        }
    });

    internal static void FailedOrCancelledSavePreventsClosingAnyEditor() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();

        foreach (var savePath in new[] { null, directory.PathFor() })
        {
            var first = CreateEditedWindow(() => ScreenshotCloseChoice.Discard);
            var second = CreateEditedWindow(() => ScreenshotCloseChoice.Save, () => savePath);
            var firstClosed = false;
            var secondClosed = false;
            first.Closed += (_, _) => firstClosed = true;
            second.Closed += (_, _) => secondClosed = true;

            try
            {
                TestAssert.False(App.TryCloseScreenshotEditors([first, second]));
                TestAssert.False(firstClosed);
                TestAssert.False(secondClosed);
                TestAssert.True(first.HasUnsavedChanges);
                TestAssert.True(second.HasUnsavedChanges);
            }
            finally
            {
                first.CloseAfterConfirmation();
                second.CloseAfterConfirmation();
            }
        }
    });

    internal static void SavingDuringExitExportsBeforeClosingEditors() => RunOnStaThread(() =>
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathFor("screenshot.png");
        var window = CreateEditedWindow(() => ScreenshotCloseChoice.Save, () => path);
        var exportedBeforeClose = false;
        window.Closed += (_, _) => exportedBeforeClose = File.Exists(path) && !window.HasUnsavedChanges;

        try
        {
            TestAssert.True(App.TryCloseScreenshotEditors([window]));
            TestAssert.True(exportedBeforeClose);
        }
        finally
        {
            window.CloseAfterConfirmation();
        }
    });

    private static ScreenshotEditorWindow CreateEditedWindow(Func<ScreenshotCloseChoice> promptClose, Func<string?>? selectSavePath = null)
    {
        var pixels = new byte[ImageWidth * ImageHeight * BytesPerPixel];
        var image = BitmapSource.Create(ImageWidth, ImageHeight, StandardDpi, StandardDpi, PixelFormats.Bgra32, null, pixels, ImageWidth * BytesPerPixel);
        image.Freeze();
        var window = new ScreenshotEditorWindow(image, promptClose: promptClose, selectSavePath: selectSavePath);
        window.CommitAnnotation(AnnotationStart, AnnotationEnd);
        return window;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.InitializeComponent();
                }

                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
