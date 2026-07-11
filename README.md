# Codex Palette Overlay

An unofficial, movable model, reasoning, and speed palette for the official Codex desktop app on Windows.

Codex Palette Overlay does not replace Codex and does not run a second chat client. It displays a small transparent window above the native model selector and drives the selector through Windows UI Automation.

## Features

- Visual matrix for model and reasoning-effort combinations.
- Collapses automatically after a confirmed selection, with a manual collapse control.
- Automatically aligns itself with the native Codex selector.
- Drag from anywhere on the overlay to keep a custom offset.
- Invokes native model and effort menu entries directly and verifies the final selection.
- Uses UI Automation patterns only: it never moves the cursor, emits mouse clicks, or forces Codex to the foreground.
- Reads localized reasoning and speed labels from the running Codex app instead of maintaining its own translations.
- Adds a two-position speed control whose title, values, and selected state come directly from Codex accessibility controls.
- Appears only while Codex or the overlay is active.
- Does not read or store OpenAI credentials.

## Compatibility

- Windows 10 or Windows 11
- The official Codex desktop app
- Node.js 22 or newer for development

The integration depends on accessibility names exposed by the current Codex desktop UI. A future Codex UI update may require selector adjustments.

## Install

1. Open the [latest GitHub Release](https://github.com/intelarti11/codex-palette/releases/latest).
2. Download `Codex.Palette.Overlay.Setup.0.4.0.exe`.
3. Run the installer, then launch **Codex Palette Overlay** from the Windows Start menu.
4. Open the official Codex app; the overlay will align itself with the native selector.

Windows SmartScreen may display a warning because the community installer is not code-signed yet.

## Run from source

```powershell
npm install
npm run dev
```

Keep the official Codex app open. The capsule will align itself with the native selector. Click the capsule to open the matrix, or hold and drag anywhere on the overlay to move it.

## Validate

```powershell
npm test
npm run build
```

## Build the Windows installer

```powershell
npm run dist
```

The NSIS installer is written to `release/`.

## How it works

```text
React palette
    | context-isolated IPC
Electron transparent window
    | Windows UI Automation
Official Codex model selector
```

The PowerShell helper locates the official Codex process, reads the selector bounds and localized labels, discovers the native two-position speed selector structurally, invokes the requested native menu entries, and confirms each resulting selection before reporting success.

## Privacy and security

- No API key is requested.
- No ChatGPT or Codex authentication token is accessed.
- No conversation content is sent anywhere by this project.
- UI automation is limited to the native model, reasoning, and speed controls.

## Contributing

Issues and pull requests are welcome, especially for accessibility-name changes introduced by new Codex desktop releases.

## Disclaimer

This independent project is not affiliated with, endorsed by, or sponsored by OpenAI. Codex and OpenAI are trademarks of OpenAI.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Silent selection

This variant does not call `SetCursorPos`, `mouse_event`, `SetForegroundWindow`, or `AttachThreadInput`. It opens and selects the native Codex controls through `ExpandCollapsePattern`, `InvokePattern`, `SelectionItemPattern`, and `LegacyIAccessiblePattern`. If Codex stops exposing a usable accessibility action after an update, the operation fails instead of falling back to physical mouse input.
