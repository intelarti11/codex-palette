# Codex Palette Overlay

An unofficial, movable model, reasoning, and speed palette for the official Codex desktop app on Windows.

Codex Palette Overlay does not replace Codex and does not run a second chat client. It displays a small transparent window above the native model selector and drives the selector through Windows UI Automation.

## Native Windows port

A WPF/.NET native implementation is available under [`native/`](native/README.md). It keeps the same silent UI Automation behavior while removing Electron, Chromium, Node.js, and the separate PowerShell helper process.

The native port currently lives alongside the proven Electron implementation so it can be validated on real Codex installations before becoming the default. The Windows workflow builds a self-contained, single-file `CodexPalette.exe` artifact.

## Features

- Visual matrix for model and reasoning-effort combinations.
- Collapses automatically after a confirmed selection, with a manual collapse control.
- Automatically aligns itself with the native Codex selector.
- Drag the grip to keep a custom offset.
- Invokes native model and effort menu entries directly and verifies the final selection.
- Uses UI Automation patterns only: it never moves the cursor, emits mouse clicks, sends keyboard input, or forces Codex to the foreground.
- Reads localized reasoning and speed labels from the running Codex app instead of maintaining its own translations.
- Adds a two-position speed control whose title, values, and selected state come directly from Codex accessibility controls.
- Appears only while Codex or the palette is active.
- Does not read or store OpenAI credentials.

## Compatibility

- Windows 10 or Windows 11
- The official Codex desktop app
- .NET 8 SDK for native development
- Node.js 22 or newer for Electron development

The integration depends on accessibility names exposed by the current Codex desktop UI. A future Codex UI update may require selector adjustments.

## Native build

From Windows PowerShell:

```powershell
dotnet restore .\native\CodexPalette.Native.sln
dotnet test .\native\CodexPalette.Native.sln -c Release
dotnet publish .\native\CodexPalette.Native\CodexPalette.Native.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\artifacts\native-win-x64
```

The portable executable is written to `artifacts\native-win-x64\CodexPalette.exe`.

## Electron build

The current community version on `main` remains available as the Electron implementation while the native port is validated.

```powershell
npm install
npm run dev
```

To validate and build it:

```powershell
npm test
npm run build
npm run dist
```

The NSIS installer is written to `release/`.

## How the native port works

```text
WPF transparent window
    | direct System.Windows.Automation calls
Official Codex model, reasoning, and speed selectors

WinEvent hooks
    | foreground / show / hide / move / restart updates
WPF window placement
```

The native service discovers the official Codex process, reads selector bounds and localized labels, finds the two-position speed selector structurally, invokes native accessibility actions, and confirms each resulting selection before reporting success.

## Privacy and security

- No API key is requested.
- No ChatGPT or Codex authentication token is accessed.
- No conversation content is sent anywhere by this project.
- UI automation is limited to the native model, reasoning, and speed controls.
- The native port does not inject code into Codex and does not modify the Codex executable.

## Contributing

Issues and pull requests are welcome, especially for accessibility-name changes introduced by new Codex desktop releases and reports from non-English Codex installations.

## Disclaimer

This independent project is not affiliated with, endorsed by, or sponsored by OpenAI. Codex and OpenAI are trademarks of OpenAI.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Silent selection

Neither implementation calls `SetCursorPos`, `mouse_event`, `SetForegroundWindow`, or `AttachThreadInput`. They open and select native Codex controls through `ExpandCollapsePattern`, `InvokePattern`, and `SelectionItemPattern`. If Codex stops exposing a usable accessibility action after an update, the operation fails instead of falling back to physical input.
