# Codex Palette Native

This directory contains the native Windows port of Codex Palette.

## Architecture

- **WPF / .NET 8** for the transparent palette window.
- **System.Windows.Automation** for silent model, reasoning, and speed selection.
- **WinEvent hooks** for foreground, visibility, destruction, and location changes.
- A low-frequency two-second fallback check to recover after missed Windows events.
- JSON settings under `%LOCALAPPDATA%\CodexPalette\settings.json`.

The native build does not launch PowerShell, embed Chromium, use Node.js, move the mouse, send keyboard input, or change the foreground window to perform a selection.

## Build

From a Windows PowerShell terminal with the .NET 8 SDK:

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

The portable executable is written to:

```text
artifacts\native-win-x64\CodexPalette.exe
```

## Validation still required on a real Codex installation

The automated tests cover text normalization, localized speed labels, the model/effort support matrix, and window placement. The following integration checks require the official Codex Windows application:

1. Selector discovery at different display scaling values.
2. Silent model and reasoning selection.
3. Localized two-position speed discovery and selection.
4. Multiple monitors and taskbar placement.
5. Codex restart and application update behavior.
