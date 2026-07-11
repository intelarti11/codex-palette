# Codex Palette Overlay

An unofficial, movable model-and-reasoning palette for the official Codex desktop app on Windows.

Codex Palette Overlay does not replace Codex and does not run a second chat client. It displays a small transparent window above the native model selector and drives the selector through Windows UI Automation.

## Features

- Visual matrix for model and reasoning-effort combinations.
- Automatically aligns itself with the native Codex selector.
- Drag from anywhere on the overlay to keep a custom offset.
- Invokes native model and effort menu entries directly and verifies the final selection.
- Reads localized effort labels from the running Codex app instead of maintaining its own translations.
- Appears only while Codex or the overlay is active.
- Does not read or store OpenAI credentials.

## Compatibility

- Windows 10 or Windows 11
- The official Codex desktop app
- Node.js 22 or newer for development

The integration depends on accessibility names exposed by the current Codex desktop UI. A future Codex UI update may require selector adjustments.

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

The PowerShell helper locates the official Codex process, reads the selector bounds and localized labels, invokes the requested native menu entries, and confirms the resulting selector text before reporting success.

## Privacy and security

- No API key is requested.
- No ChatGPT or Codex authentication token is accessed.
- No conversation content is sent anywhere by this project.
- UI automation is limited to the native model and reasoning controls.

## Contributing

Issues and pull requests are welcome, especially for accessibility-name changes introduced by new Codex desktop releases.

## Disclaimer

This independent project is not affiliated with, endorsed by, or sponsored by OpenAI. Codex and OpenAI are trademarks of OpenAI.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).
