using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinUtil.Models;
using WinUtil.Services;

namespace WinUtil.ViewModels;

internal sealed class SpotlightWorkspaceViewModel : INotifyPropertyChanged
{
    internal const int MaximumInputLength = 262_144;
    internal const int MaximumOutputLength = 1_048_576;

    private readonly Action<string> copyText;
    private readonly Action<string>? recordSuccessfulAction;
    private SpotlightAction? selectedTool;
    private string inputText = string.Empty;
    private string outputText = string.Empty;
    private string errorMessage = string.Empty;
    private string statusMessage = string.Empty;

    internal SpotlightWorkspaceViewModel(IReadOnlyList<SpotlightAction> tools, Action<string> copyText, Action<string>? recordSuccessfulAction = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(copyText);
        Tools = tools;
        this.copyText = copyText;
        this.recordSuccessfulAction = recordSuccessfulAction;
        selectedTool = tools.FirstOrDefault();
    }

    public IReadOnlyList<SpotlightAction> Tools { get; }

    public SpotlightAction? SelectedTool
    {
        get => selectedTool;
        set
        {
            if (value is not null && !Tools.Contains(value))
            {
                throw new ArgumentException("The selected tool must belong to this workspace.", nameof(value));
            }

            if (selectedTool == value)
            {
                return;
            }

            selectedTool = value;
            ClearResult();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RequiresInput));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(RunLabel));
        }
    }

    public string InputText
    {
        get => inputText;
        set
        {
            var nextValue = value ?? string.Empty;
            if (inputText == nextValue)
            {
                return;
            }

            inputText = nextValue;
            ClearResult();
            OnPropertyChanged();
        }
    }

    public string OutputText => outputText;
    public string ErrorMessage => errorMessage;
    public string StatusMessage => statusMessage;
    public bool HasError => errorMessage.Length > 0;
    public bool CanCopy => outputText.Length > 0;
    public bool CanRun => selectedTool is not null;
    public bool RequiresInput => selectedTool?.RequiresSelectedText == true;
    public string RunLabel => RequiresInput ? "Run" : "Generate";

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Run()
    {
        ClearResult();

        if (selectedTool is null)
        {
            SetError("Choose a tool first.");
            return;
        }

        if (RequiresInput && inputText.Length == 0)
        {
            SetError("Enter text before running this tool.");
            return;
        }

        if (RequiresInput && inputText.Length > MaximumInputLength)
        {
            SetError($"Input cannot exceed {MaximumInputLength:N0} characters.");
            return;
        }

        try
        {
            var result = RequiresInput
                ? selectedTool.TransformSelectedText(inputText)
                : selectedTool.GenerateOutput();

            if (result.Length > MaximumOutputLength)
            {
                SetError($"Output cannot exceed {MaximumOutputLength:N0} characters. Use a smaller input.");
                return;
            }

            outputText = result;
            OnPropertyChanged(nameof(OutputText));
            OnPropertyChanged(nameof(CanCopy));
            SetStatus(result.Length == 0 ? "The result is empty." : "Result ready to copy.");
        }
        catch (JsonException)
        {
            SetError("The input is not valid JSON.");
        }
        catch (TextTransformationException exception)
        {
            SetError(exception.Message.Replace("selected text", "input", StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            SetError("The input could not be processed. Check its text and encoding.");
        }

        if (!HasError)
        {
            recordSuccessfulAction?.Invoke(selectedTool.Id);
        }
    }

    internal void Copy()
    {
        if (!CanCopy)
        {
            return;
        }

        SetError(string.Empty);
        SetStatus(string.Empty);

        try
        {
            copyText(outputText);
            SetStatus("Result copied to the clipboard.");
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            SetError("WinUtil could not access the clipboard. Close any app using it and try again.");
        }
    }

    internal void Reset(SpotlightAction? tool = null)
    {
        SelectedTool = tool ?? Tools.FirstOrDefault();
        InputText = string.Empty;
        ClearResult();
    }

    internal void ShowDirectActionError(string message)
    {
        SetStatus(string.Empty);
        SetError($"Direct shortcut failed. {message}");
    }

    internal void ShowPreferenceError(string message)
    {
        SetStatus(string.Empty);
        SetError(message);
    }

    private void ClearResult()
    {
        outputText = string.Empty;
        OnPropertyChanged(nameof(OutputText));
        OnPropertyChanged(nameof(CanCopy));
        SetError(string.Empty);
        SetStatus(string.Empty);
    }

    private void SetError(string message)
    {
        errorMessage = message;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    private void SetStatus(string message)
    {
        statusMessage = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
