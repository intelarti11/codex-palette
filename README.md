# Codex Palette Overlay

[![CI](https://github.com/intelarti11/codex-palette/actions/workflows/ci.yml/badge.svg)](https://github.com/intelarti11/codex-palette/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

An unofficial visual model and reasoning selector for the official Codex desktop app on Windows.

Codex Palette Overlay places a compact color matrix over Codex's native model selector. It reads the models, reasoning levels, speed choices, current task selection, selector geometry, visual style, and keyboard shortcut from the running Codex app. Selection changes are sent through the callbacks already wired to the native selector, so native menus remain closed.

> [!IMPORTANT]
> This is an independent community project. It is not affiliated with, endorsed by, or supported by OpenAI.

## Highlights

- Visual matrix for every supported model and reasoning-effort combination.
- Silent model, effort, and speed changes through Codex's existing renderer callbacks.
- Automatic synchronization with the active Codex task.
- Exact alignment with the native selector without covering the microphone button.
- Model-specific color treatment while native selector text and icons remain visible.
- Uses Codex's configured selector shortcut; the default is `Ctrl+Shift+M`.
- Right-click menu for closing the palette.
- Cached Windows UI Automation geometry watcher with no continuous menu scanning.
- Localized model capabilities and labels read from the running Codex app.
- No API key, account token, or conversation content is requested or stored.

## Requirements

- Windows 10 or Windows 11
- The official Codex desktop app for Windows
- Node.js 22 or newer
- Git, when installing from source

The current integration depends on private Codex renderer structures and Windows accessibility information. A future Codex desktop update can require a compatibility update in this project.

## Quick start

```powershell
git clone https://github.com/intelarti11/codex-palette.git
cd codex-palette
npm install
.\launch-silent.bat
```

`launch-silent.bat` performs the complete development launch sequence:

1. Stops palette processes started from the current project directory.
2. Closes the Codex desktop app.
3. Restarts Codex with a random loopback-only CDP port.
4. Starts the palette through Vite and Electron.

Keep the terminal open while using the development build. Run the batch file again after changing Electron main-process or preload code.

## Usage

- Click the native Codex selector area to open the palette.
- Press the model-selector shortcut to open or close it. The palette reads the shortcut configured by Codex when it starts; the default is `Ctrl+Shift+M`.
- Select a cell to apply its model and reasoning level. The palette closes after Codex confirms the selection.
- Use the segmented control at the bottom to switch between standard and fast service tiers when both are available.
- Drag the palette header to move it temporarily. Use the reset button to return to native alignment.
- Right-click the palette to access the close command.

The shortcut is registered only while Codex or the palette is in the foreground, so it does not intercept the same key combination in unrelated applications.

## How silent mode works

Codex is launched with a Chrome DevTools Protocol endpoint bound to `127.0.0.1` on a random high port. The palette discovers that port from the Codex process command line and evaluates small, purpose-built expressions inside the Codex renderer.

Those expressions:

- read the visible model catalog and supported reasoning levels;
- read the selector's current task state and configured shortcut;
- read the native selector's computed presentation;
- invoke `onSelectModel` and `onSelectServiceTier`, which are already connected to Codex's selector.

The palette does not guess task identifiers and does not call thread-update APIs directly.

Windows UI Automation remains responsible for locating the selector rectangle and determining whether Codex is visible. The watcher caches the Codex process and selector element, so the full accessibility tree is not scanned every 350 ms.

Legacy menu scanning and modification are disabled by default. They can be enabled explicitly for compatibility testing:

```powershell
$env:CODEX_UIA_SCAN_FALLBACK = '1'
$env:CODEX_UIA_MODIFIER_FALLBACK = '1'
npm run dev
```

## Development

Install dependencies and start the development build:

```powershell
npm install
npm run dev
```

Run the automated checks:

```powershell
npm test
npm run build
```

Build an unsigned NSIS installer:

```powershell
npm run dist
```

The installer is written to `release/`. Windows SmartScreen may warn about community builds because they are not code-signed.

## Privacy and security

- CDP listens on the loopback interface only.
- A random port is selected for each launch.
- No OpenAI API key or Codex authentication token is accessed.
- No conversation text is read, stored, or transmitted by this project.
- The active task identifier remains inside the Codex renderer.
- The palette stores only the last visual selection in its own local storage.

CDP is a powerful local debugging interface. Only run software you trust on the same Windows account while Codex is running with CDP enabled. Closing Codex removes the endpoint.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Contributing](CONTRIBUTING.md)

## Project status

This project is experimental and Windows-only. The source on `main` is the authoritative community build. Releases may lag behind current source features.

## License

Apache License 2.0. See [LICENSE](LICENSE).

Codex and OpenAI are trademarks of OpenAI.
