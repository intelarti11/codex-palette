# Codex Palette Overlay

An unofficial, movable model, reasoning, and speed palette for the official Codex desktop app on Windows.

Codex Palette Overlay does not replace Codex and does not run a second chat client. It displays a small transparent window above the native model selector and drives the selector through Windows UI Automation.

## Features

- Visual matrix for model and reasoning-effort combinations.
- Collapses automatically after a confirmed selection, with a manual collapse control.
- Automatically aligns itself with the native Codex selector.
- Drag from anywhere on the overlay to keep a custom offset.
- Uses the model, reasoning, and speed callbacks already wired to Codex's native selector when local CDP is enabled.
- Falls back to native model and effort menu actions and verifies the final selection.
- Uses UI Automation patterns only: it never moves the cursor, emits mouse clicks, or forces Codex to the foreground.
- Reads the model matrix from Codex's renderer harness without opening the native selector.
- Adds a two-position speed control whose title, values, and selected state come directly from Codex accessibility controls.
- Appears only while Codex or the overlay is active.
- Does not read or store OpenAI credentials.

## Compatibility

- Windows 10 or Windows 11
- The official Codex desktop app
- Node.js 22 or newer for development

The direct integration depends on private Codex renderer module names, and the fallback depends on accessibility names exposed by the current Codex desktop UI. A future Codex update may require adjustments.

## Optional direct renderer mode

Codex must expose a loopback-only Chrome DevTools Protocol port before the palette can call its internal next-turn settings action. Close Codex completely, then relaunch it from PowerShell:

```powershell
$codex = Get-AppxPackage OpenAI.Codex
$exe = Join-Path $codex.InstallLocation 'app\ChatGPT.exe'
$port = Get-Random -Minimum 41000 -Maximum 49000
Start-Process $exe -ArgumentList @(
  '--remote-debugging-address=127.0.0.1',
  "--remote-debugging-port=$port"
)
```

The palette discovers the port from the Codex process command line. During development, `CODEX_CDP_PORT` can override discovery. Native-menu scanning is disabled by default so ordinary startup never opens the selector. Set `CODEX_UIA_SCAN_FALLBACK=1` only to opt into the legacy UI Automation scan while troubleshooting a build without renderer access.

Model, reasoning, and speed changes use the renderer's next-turn settings action and do not open native menus. The legacy modifier is disabled by default; set `CODEX_UIA_MODIFIER_FALLBACK=1` only for explicit compatibility testing.

## Install

The current community version is available from the `main` branch. Older installers on the Releases page may not yet include silent selection and the localized speed control.

```powershell
git clone https://github.com/intelarti11/codex-palette.git
cd codex-palette
npm install
npm run dev
```

To build a Windows installer locally:

```powershell
npm run dist
```

The NSIS installer is written to `release/`. Windows SmartScreen may display a warning because community builds are not code-signed.

## Validate

```powershell
npm test
npm run build
```

## How it works

```text
React palette
    | context-isolated IPC
Electron transparent window
    | local CDP when available
Codex renderer internal settings bus
    | fallback: Windows UI Automation
Official Codex model selector / app-server
```

The PowerShell helper locates the official Codex process, reads the selector bounds and localized labels, discovers the native two-position speed selector structurally, invokes the requested native menu entries, and confirms each resulting selection before reporting success.

## Privacy and security

- No API key is requested.
- No ChatGPT or Codex authentication token is accessed.
- No conversation content is sent anywhere by this project.
- The direct path keeps the active task id inside the Codex renderer and returns only the selection result.
- UI automation is limited to the native model, reasoning, and speed controls.

## Contributing

Issues and pull requests are welcome, especially for accessibility-name changes introduced by new Codex desktop releases.

## Disclaimer

This independent project is not affiliated with, endorsed by, or sponsored by OpenAI. Codex and OpenAI are trademarks of OpenAI.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).

## Silent selection

This variant does not call `SetCursorPos`, `mouse_event`, `SetForegroundWindow`, or `AttachThreadInput`. It opens and selects the native Codex controls through `ExpandCollapsePattern`, `InvokePattern`, `SelectionItemPattern`, and `LegacyIAccessiblePattern`. If Codex stops exposing a usable accessibility action after an update, the operation fails instead of falling back to physical mouse input.
