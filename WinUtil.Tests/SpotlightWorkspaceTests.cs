using System.Runtime.InteropServices;
using WinUtil.Models;
using WinUtil.Services;
using WinUtil.ViewModels;

namespace WinUtil.Tests;

internal static class SpotlightWorkspaceTests
{
    private const int ConfiguredPasswordLength = 32;
    private const int Encoded512BitSecretLength = 86;
    private const int UuidVersionCharacterIndex = 14;

    internal static void WorkspaceRunsEveryExistingTextTool()
    {
        var copied = new List<string>();
        var workspace = new SpotlightWorkspaceViewModel(SpotlightActionCatalog.All, copied.Add);
        var cases = new (string Id, string Input, string Output)[]
        {
            (SpotlightActionIds.FormatJson, "{\"name\":\"Ada\"}", $"{{{Environment.NewLine}  \"name\": \"Ada\"{Environment.NewLine}}}"),
            (SpotlightActionIds.MinifyJson, "{ \"name\": \"Ada\" }", "{\"name\":\"Ada\"}"),
            (SpotlightActionIds.Base64Encode, "Grüße", "R3LDvMOfZQ=="),
            (SpotlightActionIds.Base64Decode, "R3LDvMOfZQ==", "Grüße"),
            (SpotlightActionIds.UrlEncode, "Ada Lovelace", "Ada%20Lovelace"),
            (SpotlightActionIds.UrlDecode, "Ada%20Lovelace", "Ada Lovelace"),
            (SpotlightActionIds.Uppercase, "Hello\nworld", "HELLO\nWORLD"),
            (SpotlightActionIds.Lowercase, "Hello\nWORLD", "hello\nworld")
        };

        foreach (var (id, input, output) in cases)
        {
            SelectTool(workspace, id);
            workspace.InputText = input;
            workspace.Run();

            TestAssert.Equal(output, workspace.OutputText);
            TestAssert.True(workspace.RequiresInput);
            TestAssert.Equal("Run", workspace.RunLabel);
            TestAssert.True(workspace.CanCopy);
            TestAssert.False(workspace.HasError);
        }

        TestAssert.Empty(copied);
    }

    internal static void WorkspaceGeneratorsUseCurrentSavedSettings()
    {
        var settings = WidgetSettings.Default;
        var workspace = new SpotlightWorkspaceViewModel(SpotlightActionCatalog.Create(() => settings), _ => { });
        settings = settings with
        {
            UuidVersion = UuidVersion.Version7,
            PasswordLength = ConfiguredPasswordLength,
            PasswordIncludesDigits = true,
            PasswordIncludesLowercase = false,
            PasswordIncludesUppercase = false,
            PasswordIncludesSymbols = false,
            JwtSecretLengthBytes = WidgetSettings.JwtSecretBytesFor512Bits
        };

        SelectTool(workspace, SpotlightActionIds.Uuid);
        workspace.Run();
        TestAssert.True(Guid.TryParse(workspace.OutputText, out _));
        TestAssert.Equal('7', workspace.OutputText[UuidVersionCharacterIndex]);
        TestAssert.False(workspace.RequiresInput);
        TestAssert.Equal("Generate", workspace.RunLabel);

        SelectTool(workspace, SpotlightActionIds.Password);
        workspace.Run();
        TestAssert.Equal(ConfiguredPasswordLength, workspace.OutputText.Length);
        TestAssert.True(workspace.OutputText.All(char.IsAsciiDigit));

        SelectTool(workspace, SpotlightActionIds.JwtSecret);
        workspace.Run();
        TestAssert.Equal(Encoded512BitSecretLength, workspace.OutputText.Length);
        TestAssert.False(workspace.HasError);
    }

    internal static void WorkspaceCopyUsesDisplayedResultWithoutRegenerating()
    {
        var generations = 0;
        var copied = new List<string>();
        var generator = SpotlightAction.CreateGenerator("counter", "Test", "Counter", "", [], () => $"value-{++generations}");
        var workspace = new SpotlightWorkspaceViewModel([generator], copied.Add);

        workspace.Copy();
        TestAssert.Empty(copied);
        workspace.Run();
        var displayed = workspace.OutputText;
        workspace.Copy();
        workspace.Copy();

        TestAssert.Equal(1, generations);
        TestAssert.SequenceEqual(new[] { displayed, displayed }, copied);
        TestAssert.Equal(displayed, workspace.OutputText);
        TestAssert.Equal("Result copied to the clipboard.", workspace.StatusMessage);
    }

    internal static void WorkspaceInvalidatesResultsWhenInputOrToolChanges()
    {
        var workspace = new SpotlightWorkspaceViewModel(SpotlightActionCatalog.All, _ => { });
        SelectTool(workspace, SpotlightActionIds.Uppercase);
        workspace.InputText = "first";
        workspace.Run();
        workspace.InputText = "second";
        TestAssert.Equal(string.Empty, workspace.OutputText);
        TestAssert.False(workspace.CanCopy);
        TestAssert.Equal(string.Empty, workspace.StatusMessage);

        workspace.Run();
        SelectTool(workspace, SpotlightActionIds.Lowercase);
        TestAssert.Equal(string.Empty, workspace.OutputText);
        TestAssert.False(workspace.CanCopy);

        SelectTool(workspace, SpotlightActionIds.FormatJson);
        workspace.Run();
        TestAssert.True(workspace.HasError);
        workspace.InputText = "{}";
        TestAssert.False(workspace.HasError);
    }

    internal static void WorkspaceReportsEmptyInvalidAndOversizedInput()
    {
        var workspace = new SpotlightWorkspaceViewModel(SpotlightActionCatalog.All, _ => { });
        SelectTool(workspace, SpotlightActionIds.Uppercase);

        foreach (var input in new string?[] { null, string.Empty })
        {
            workspace.InputText = input!;
            workspace.Run();
            TestAssert.Equal("Enter text before running this tool.", workspace.ErrorMessage);
            TestAssert.False(workspace.CanCopy);
        }

        workspace.InputText = new string('a', SpotlightWorkspaceViewModel.MaximumInputLength);
        workspace.Run();
        TestAssert.False(workspace.HasError);
        TestAssert.Equal(SpotlightWorkspaceViewModel.MaximumInputLength, workspace.OutputText.Length);
        TestAssert.True(workspace.OutputText.All(character => character == 'A'));

        workspace.InputText += "a";
        workspace.Run();
        TestAssert.True(workspace.HasError);
        TestAssert.Equal(string.Empty, workspace.OutputText);

        SelectTool(workspace, SpotlightActionIds.FormatJson);
        workspace.InputText = "not JSON";
        workspace.Run();
        TestAssert.Equal("The input is not valid JSON.", workspace.ErrorMessage);

        SelectTool(workspace, SpotlightActionIds.Base64Decode);
        workspace.InputText = "not Base64";
        workspace.Run();
        TestAssert.Equal("The input is not valid Base64.", workspace.ErrorMessage);
        TestAssert.False(workspace.CanCopy);

        SelectTool(workspace, SpotlightActionIds.Base64Encode);
        workspace.InputText = "\uD800";
        workspace.Run();
        TestAssert.True(workspace.HasError);
        TestAssert.False(workspace.CanCopy);
    }

    internal static void WorkspaceHandlesEmptyOutputAndBoundsOutputLength()
    {
        var maximum = SpotlightAction.CreateGenerator("maximum", "Test", "Maximum", "", [],
            () => new string('x', SpotlightWorkspaceViewModel.MaximumOutputLength));
        var oversized = SpotlightAction.CreateGenerator("oversized", "Test", "Oversized", "", [],
            () => new string('x', SpotlightWorkspaceViewModel.MaximumOutputLength + 1));
        var empty = SpotlightAction.CreateGenerator("empty", "Test", "Empty", "", [], () => string.Empty);
        var copied = new List<string>();
        var workspace = new SpotlightWorkspaceViewModel([maximum, oversized, empty], copied.Add);

        workspace.Run();
        TestAssert.Equal(SpotlightWorkspaceViewModel.MaximumOutputLength, workspace.OutputText.Length);
        TestAssert.True(workspace.CanCopy);

        workspace.SelectedTool = oversized;
        workspace.Run();
        TestAssert.True(workspace.HasError);
        TestAssert.Equal(string.Empty, workspace.OutputText);
        TestAssert.False(workspace.CanCopy);

        workspace.SelectedTool = empty;
        workspace.Run();
        workspace.Copy();
        TestAssert.False(workspace.HasError);
        TestAssert.Equal("The result is empty.", workspace.StatusMessage);
        TestAssert.False(workspace.CanCopy);
        TestAssert.Empty(copied);
    }

    internal static void WorkspaceReportsClipboardFailureAndAllowsRetry()
    {
        var clipboardAvailable = false;
        var copied = new List<string>();
        var workspace = new SpotlightWorkspaceViewModel(SpotlightActionCatalog.All, text =>
        {
            if (!clipboardAvailable)
            {
                throw new ExternalException("Clipboard is busy.");
            }

            copied.Add(text);
        });
        SelectTool(workspace, SpotlightActionIds.Uppercase);
        workspace.InputText = "hello";
        workspace.Run();
        workspace.Copy();
        TestAssert.True(workspace.HasError);
        TestAssert.Equal("HELLO", workspace.OutputText);
        TestAssert.True(workspace.CanCopy);

        clipboardAvailable = true;
        workspace.Copy();
        TestAssert.False(workspace.HasError);
        TestAssert.SequenceEqual(new[] { "HELLO" }, copied);
    }

    internal static void WorkspaceResetAndPaletteCloseClearSensitiveContent()
    {
        var palette = new SpotlightViewModel(SpotlightActionCatalog.All, _ => { }) { Query = "password" };
        palette.OpenWorkspace();
        var workspace = palette.Workspace;
        TestAssert.True(palette.IsWorkspaceOpen);
        TestAssert.False(palette.IsPaletteOpen);
        TestAssert.Equal(SpotlightActionIds.Password, workspace.SelectedTool?.Id);
        workspace.InputText = "sensitive input";
        workspace.Run();
        TestAssert.True(workspace.CanCopy);
        palette.CloseWorkspace();
        TestAssert.True(palette.IsPaletteOpen);
        TestAssert.Equal(string.Empty, workspace.InputText);
        TestAssert.Equal(string.Empty, workspace.OutputText);

        palette.OpenWorkspace();
        workspace.InputText = "another value";
        workspace.Run();
        palette.Reset();
        TestAssert.False(palette.IsWorkspaceOpen);
        TestAssert.Equal(string.Empty, workspace.InputText);
        TestAssert.Equal(string.Empty, workspace.OutputText);
        TestAssert.Equal(string.Empty, workspace.StatusMessage);
        TestAssert.False(workspace.HasError);
    }

    internal static void WorkspaceHandlesMissingToolsAndRejectsForeignTools()
    {
        var workspace = new SpotlightWorkspaceViewModel([], _ => { });
        TestAssert.False(workspace.CanRun);
        workspace.Run();
        TestAssert.Equal("Choose a tool first.", workspace.ErrorMessage);
        workspace.Reset();
        TestAssert.False(workspace.HasError);

        try
        {
            workspace.SelectedTool = SpotlightActionCatalog.All.First();
        }
        catch (ArgumentException)
        {
            TestAssert.Equal<SpotlightAction?>(null, workspace.SelectedTool);
            return;
        }

        throw new InvalidOperationException("Expected an unregistered workspace tool to be rejected.");
    }

    private static void SelectTool(SpotlightWorkspaceViewModel workspace, string actionId) =>
        workspace.SelectedTool = workspace.Tools.Single(action => action.Id == actionId);
}
