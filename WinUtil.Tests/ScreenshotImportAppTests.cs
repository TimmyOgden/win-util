using System.Reflection;
using System.Windows.Media.Imaging;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.Views;

namespace WinUtil.Tests;

internal static partial class ScreenshotEditorWindowTests
{
    internal static void ExplicitImageImportOpensWhenAutomaticCaptureEditingIsDisabled() => RunOnStaThread(() =>
    {
        EnsureApplicationResources();
        var app = (App)System.Windows.Application.Current;
        var widgetField = typeof(App).GetField("widgetWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousWidget = widgetField.GetValue(app);
        var editorField = typeof(App).GetField("screenshotEditors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var editors = (HashSet<ScreenshotEditorWindow>)editorField.GetValue(app)!;
        var existing = editors.ToArray();
        var widget = new WidgetWindow(WidgetSettings.Default with { OpenScreenshotEditorAfterCapture = false },
            _ => { }, new ImportBatteryProvider(), new ImportAudioProvider());
        widgetField.SetValue(app, widget);
        try
        {
            var image = CreateImage(TestImageWidth, TestImageHeight, StandardDpi);
            InvokeImageMethod(app, "ShowScreenshotEditor", image);
            TestAssert.Equal(existing.Length, editors.Count);
            InvokeImageMethod(app, "OpenImageEditor", image);
            var editor = TestAssert.Single(editors.Except(existing).ToArray());
            TestAssert.True(editor.IsVisible);
            TestAssert.False(editor.HasUnsavedChanges);
            TestAssert.Equal(TestImageWidth, editor.RenderedImage.PixelWidth);
        }
        finally
        {
            foreach (var editor in editors.Except(existing).ToArray()) { editor.CloseAfterConfirmation(); }
            widgetField.SetValue(app, previousWidget);
            widget.AllowClose();
        }
    });

    private static void InvokeImageMethod(App app, string name, BitmapSource image) =>
        typeof(App).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [image]);

    private sealed class ImportBatteryProvider : IBatteryStatusProvider
    {
        public Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new BatterySnapshot([], null));
    }

    private sealed class ImportAudioProvider : IAudioDeviceProvider
    {
        public AudioDeviceSnapshot GetSnapshot() => new(null, null, null);
        public AudioOperationResult SetDefaultOutput(string deviceId) => new("Unavailable");
        public AudioOperationResult SetDefaultInput(string deviceId) => new("Unavailable");
        public AudioOperationResult SetOutputVolume(double percentage) => new("Unavailable");
        public AudioOperationResult SetMicrophoneMute(bool muted) => new("Unavailable");
    }
}
