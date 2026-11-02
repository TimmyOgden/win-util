using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinUtil.Models;
using WinUtil.Views;

namespace WinUtil.Services;

internal sealed class SpotlightController : IDisposable
{
    private readonly Func<WidgetSettings> settingsProvider;
    private readonly IForegroundTextInjector textInjector;
    private readonly SpotlightWindow spotlightWindow;
    private bool isActionRunning;
    private IntPtr insertionTarget;

    internal SpotlightController(
        SpotlightWindow spotlightWindow,
        IForegroundTextInjector textInjector,
        Func<WidgetSettings> settingsProvider)
    {
        this.spotlightWindow = spotlightWindow;
        this.textInjector = textInjector;
        this.settingsProvider = settingsProvider;
        spotlightWindow.ActionRequested += HandleActionRequested;
    }

    public void Dispose() => spotlightWindow.ActionRequested -= HandleActionRequested;

    internal void Toggle()
    {
        if (spotlightWindow.IsVisible)
        {
            spotlightWindow.ClosePalette();
            return;
        }

        insertionTarget = textInjector.CaptureForegroundWindow();
        spotlightWindow.ShowPalette();
    }

    internal void ExecuteAction(string actionId)
    {
        var action = SpotlightActionCatalog.Create(settingsProvider)
            .SingleOrDefault(candidate => candidate.Id == actionId)
            ?? throw new ArgumentOutOfRangeException(nameof(actionId), actionId, "Unsupported Spotlight action.");
        insertionTarget = textInjector.CaptureForegroundWindow();
        ExecuteActionAsync(action);
    }

    private void HandleActionRequested(object? sender, SpotlightAction action) => ExecuteActionAsync(action);

    private async void ExecuteActionAsync(SpotlightAction action)
    {
        if (isActionRunning)
        {
            return;
        }

        isActionRunning = true;
        var completed = false;

        try
        {
            if (action.RequiresSelectedText)
            {
                await textInjector.ReplaceSelectedTextAsync(insertionTarget, action.TransformSelectedText);
            }
            else
            {
                textInjector.InsertText(insertionTarget, action.GenerateOutput());
            }

            completed = true;
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or InvalidOperationException or JsonException or TextTransformationException)
        {
            spotlightWindow.RestoreAfterInsertionFailure(CreateInsertionFailureMessage(exception));
        }
        finally
        {
            isActionRunning = false;
        }

        if (completed)
        {
            spotlightWindow.RecordSuccessfulAction(action.Id);
        }
    }

    private static string CreateInsertionFailureMessage(Exception exception) => exception switch
    {
        SelectedTextUnavailableException => "Select text in the original app before running this tool.",
        JsonException => "The selected text is not valid JSON, so nothing was replaced.",
        TextTransformationException => exception.Message,
        ArgumentException => "The original app is no longer available. Return to it and try again.",
        Win32Exception => "Windows could not insert the generated text. Return to the original app and try again.",
        ExternalException => "WinUtil could not access the clipboard. Close any app using it and try again.",
        InvalidOperationException => "Windows prevented WinUtil from returning to the original app. Try again after selecting its text field.",
        _ => throw new ArgumentOutOfRangeException(nameof(exception), exception, "Unsupported insertion failure.")
    };
}
