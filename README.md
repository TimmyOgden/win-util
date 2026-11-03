# WinUtil

Windows desktop widget and Spotlight utilities. Requires Windows 10 or later (x64).

## Screenshots

Captured from the native Windows UI with sample device and editor data.

### Desktop widget

![Desktop widget showing the clock, device batteries, and current audio input and output](docs/screenshots/desktop-widget.png)

### Spotlight

![Spotlight searching for JSON formatting and minification tools](docs/screenshots/spotlight.png)

### Settings

![Desktop widget settings with startup controls, opacity sliders, and a live appearance preview](docs/screenshots/settings.png)

### Screenshot editor

![Screenshot editor demonstrating rectangle, arrow, text, and blur annotations](docs/screenshots/screenshot-editor.png)

## Install

Download the installer from the [latest release](https://github.com/thechandanbhagat/win-util/releases/latest), or run `artifacts\installer\WinUtil-Setup-0.3.0-win-x64.exe` after building. Setup installs for the current user, adds a Start menu shortcut, and configures launch at Windows sign-in. The .NET runtime is included; administrator access is not required.

Change **Settings → Desktop widget → Run at startup** and save to enable or disable launch at sign-in. Upgrades preserve this preference. Exit WinUtil from its tray menu before upgrading or uninstalling. Uninstall through Windows Settings → Apps; saved app preferences remain in `%APPDATA%\WinUtil`.

## Battery

The widget shows reported charging and plugged-in status. **Settings → Battery** enables low-battery notifications and sets the threshold (20% by default). Each device alerts once while low; it can alert again after a reading above the threshold. Missing readings, reconnects, and charging do not reset that alert history. Bluetooth charging status is shown only when available from Windows.

## Audio

Use the widget's output and input selectors to change the default Windows devices for general audio and communications. Adjust output volume with the slider and toggle microphone mute with the button. Controls refresh after changes and every three seconds, including when devices are connected or removed. Failed changes display an error and refresh the actual Windows state.

## Spotlight

Press **Alt+2** to open Spotlight and search for a tool. Change the opening shortcut in **Settings → Spotlight → General**. Each tool can also have its own shortcut. Shortcut conflicts are reported when saving; a failed change keeps the previous opening shortcut active.

In the command palette, Enter runs the selected tool against the original app. Choose **Workspace** to use a tool without an editable target: enter text, choose **Run** (or **Generate** for a generator), inspect the result, then **Copy** it. **Ctrl+Enter** runs the workspace tool; ordinary Enter and arrow keys edit the input. Copy uses the displayed result, including generated passwords and secrets. Workspace input and output stay in memory and are cleared when the workspace is reset or closed.

UUID, password, and JWT secret generation use their saved Settings options in both modes.

Mark tools with **Favorite** beside each result. An empty search shows favorites followed by the last ten successfully used commands, with duplicates removed. Search ranks exact names, prefixes, and contained text before fuzzy matches. Favorites and recent use break ties. **Settings → Spotlight → General** lets you disable or clear history; only command identifiers are saved, never entered text or generated results.

## Screenshot editor

While WinUtil is running, press **Win+Shift+S** and capture a screenshot. The image opens in WinUtil with arrow, rectangle, ellipse, text, and selected-area blur tools. Use Undo/Redo, copy the edited image, or save it as a PNG. Copy and save flatten the edits into the image at its current pixel dimensions.

Use **Open image** or **Paste image** from the tray, or **Open**, **Paste**, or file drop in an editor to import a PNG, JPEG, or BMP. Each import opens a new editor, including when automatic screenshot editing is disabled. **Ctrl+O** opens a file; **Ctrl+V** pastes an image unless you are editing text.

Use **Crop** to select the area to keep, **Resize** to set linked width and height, and the rotation controls for quarter turns. These operations retain editable annotations and share Undo/Redo with annotation edits. Export uses the resulting dimensions; saving or copying records the complete document state for close protection.

Choose a tool from the left rail; its colour, line width, text size, or blur strength appears above the canvas. With Text selected, click the image and type in place. Press **Ctrl+Enter** to place the text or **Esc** to cancel it. Use **Fit** to see the whole screenshot or **100%** to inspect and scroll its pixels.

Choose **Select** to select an existing annotation, drag it to move it, or drag its handles to resize it. Edit selected text and styles, or press **Delete** to remove the selection. Undo/Redo includes these edits; Esc cancels an unfinished drag. Selection handles are never included in copied or saved images.

Closing an editor with unexported edits offers **Save**, **Discard**, or **Cancel**. Exiting WinUtil checks every open editor before shutting down; Cancel keeps them open. Saving or copying the current image clears the warning until it is edited again. This protects normal closing; screenshot drafts are not restored after a crash.

Automatic opening can be changed in **Settings → Screenshots**. Cancelled snips and ordinary copied images do not open the editor. Editor colour, line width, text size, and blur strength are remembered when the editor closes.

Open **Screenshot history** from the tray or the editor toolbar to browse all recorded images, newest first, preview them, and reopen one in a new editor. Captures, imported images, and saved or copied exports are kept as PNGs in `%LocalAppData%\WinUtil\ScreenshotHistory` across restarts. Reopening history does not create a duplicate. History starts when this feature is enabled; earlier screenshots are not imported automatically.

Turn off **Settings → Screenshots → Keep screenshot history** to stop recording new images. Existing history remains available. Recording captures also works with automatic editor opening turned off. History keeps original images as well as exported edits; unexported annotations are not restored.

## Build

Install the .NET 9 SDK and [Inno Setup 6 or 7](https://jrsoftware.org/isdl.php), then run from PowerShell:

```powershell
.\scripts\build-installer.ps1
```

For a custom compiler location:

```powershell
.\scripts\build-installer.ps1 -InnoSetupCompiler 'C:\Tools\Inno Setup\ISCC.exe'
```

The script runs the test suite, publishes a self-contained single EXE to `artifacts\publish\win-x64\WinUtil.exe`, and builds the versioned installer in `artifacts\installer`. Generated artifacts are ignored by Git.

To run only the tests or publish only the portable EXE:

```powershell
dotnet run --project .\WinUtil.Tests\WinUtil.Tests.csproj --configuration Release
dotnet publish .\WinUtil.csproj --configuration Release -p:PublishProfile=win-x64 --source https://api.nuget.org/v3/index.json
```
