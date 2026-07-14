# Codex Palette

An unofficial model, reasoning, and speed palette for the official Codex desktop app on Windows.

Codex Palette does not replace Codex or run a second chat client. It places a small transparent window over the native selector, reads the live model catalog from the Codex renderer, and applies choices through the callbacks already wired to the active task.

## Features

- Visual matrix for every available model and reasoning-effort combination.
- Reads the live Codex model catalog without opening native menus.
- Uses exact internal model identifiers instead of matching shortened display names.
- Tracks the model, reasoning level, and speed of the active task.
- Applies model, reasoning, and speed changes without opening the native selector.
- Aligns the collapsed palette with the native selector and preserves its native appearance.
- Supports the shortcut configured by Codex (`Ctrl+Shift+M` by default).
- Collapses automatically after a confirmed selection.
- Includes a right-click command to close the palette.
- Never moves the pointer, emits physical mouse clicks, or forces Codex to the foreground.
- Does not request or store OpenAI credentials.

## Compatibility

- Windows 10 or Windows 11
- The official Codex desktop app
- Node.js 22 or newer only when building from source

Codex Palette relies on private renderer properties and Windows accessibility information exposed by the current Codex desktop build. A future Codex update may require an adjustment.

## Quick start

Download the latest portable executable from [GitHub Releases](https://github.com/intelarti11/codex-palette/releases/latest), then run it. No installation or npm server is required.

If Codex was not started in silent mode, the collapsed palette displays **Restart required**. Activate it once: Codex Palette closes Codex, relaunches it with a loopback-only CDP port, and reconnects automatically.

Click the native selector or use the Codex model-selector shortcut to open the palette. If you change that shortcut in Codex settings, restart Codex Palette so it can register the new value.

Windows SmartScreen may display a warning because community builds are not code-signed.

## Build from source

```powershell
git clone https://github.com/intelarti11/codex-palette.git
cd codex-palette
npm ci
.\launch-silent.bat
```

To build both the Windows installer and portable executable:

```powershell
npm run dist
```

To build only the installation-free portable executable:

```powershell
npm run dist:portable
```

Artifacts are written to `release/` and are intentionally excluded from Git.

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
    | loopback-only CDP
Codex renderer model catalog and native callbacks
    |
Official Codex selector / app-server

Windows UI Automation
    | selector position, size, visibility
Electron transparent window
```

The renderer path reads the model catalog, current selection, native styling, and configured shortcut. Selection is resolved by exact model identifier and sent through the current selector callback. When Codex exposes a native `powerSelection` object, the palette passes that object directly.

The PowerShell helper follows the native selector's position and Codex window visibility. It uses cached accessibility elements and does not repeatedly scan the model catalog. Legacy UI Automation scanning and selection are disabled by default.

For compatibility testing, set `CODEX_UIA_SCAN_FALLBACK=1` to allow native-menu catalog discovery or `CODEX_UIA_MODIFIER_FALLBACK=1` to allow silent accessibility actions. These fallbacks use `ExpandCollapsePattern`, `InvokePattern`, `SelectionItemPattern`, and `LegacyIAccessiblePattern`; they never generate physical mouse input.

During development, `CODEX_CDP_PORT` can override automatic port discovery.

## Privacy and security

- The CDP endpoint listens only on `127.0.0.1` and uses a random local port.
- No API key, authentication token, or conversation content is accessed by the project.
- No telemetry or external service is added.
- Model changes stay inside the Codex renderer and its existing callbacks.
- The official Codex installation is not patched or redistributed.

## Contributing

Issues and pull requests are welcome, especially for changes introduced by new Codex desktop releases.

## Disclaimer

This independent project is not affiliated with, endorsed by, or sponsored by OpenAI. Codex and OpenAI are trademarks of OpenAI.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).
