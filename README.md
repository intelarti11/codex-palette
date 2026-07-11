# Codex Palette

A clean, unofficial desktop client for Codex App Server, featuring a visual model and reasoning-effort selector, inspired by an idea from Karol ([@KarolCodes](https://x.com/KarolCodes)).

> **Status:** early MVP. The interface and model picker are functional; packaging and broader App Server coverage are still evolving.

## What it does

- Starts `codex app-server` locally over its default JSONL/stdio transport.
- Discovers the models and reasoning efforts available to the signed-in Codex account through `model/list`.
- Presents every supported model/effort pair in a visual matrix.
- Opens a local project folder and starts Codex threads in `workspace-write` mode.
- Streams assistant output and command activity.
- Surfaces command and file-change approval requests.
- Falls back to a safe preview catalog when Codex CLI is not available, so the interface can still be explored.

## Requirements

- Node.js 22 or newer
- npm
- Codex CLI installed and available as `codex` in your `PATH`
- A valid Codex login (`codex login`)

## Run locally

```bash
npm install
npm run dev
```

The app attempts to start:

```bash
codex app-server --listen stdio://
```

When the CLI is missing or unavailable, the UI opens in **Preview mode** without sending prompts to a model.

## Validate

```bash
npm test
npm run build
```

## Package the desktop app

```bash
npm run dist
```

Electron Builder writes platform packages to `release/`.

## Architecture

```text
React renderer
    │ secure IPC through contextBridge
Electron main process
    │ newline-delimited JSON over stdin/stdout
codex app-server
```

The renderer never receives ChatGPT tokens or API keys. Authentication is owned by the locally installed Codex CLI.

## Security defaults

- Electron `contextIsolation` and renderer sandbox are enabled.
- Node integration is disabled in the renderer.
- App Server uses local stdio rather than an exposed network port.
- New threads use `workspace-write`, limited to the selected project.
- Command and file-change approvals are shown in the UI.

## Roadmap

- Thread history and resume/fork controls
- Rich command and diff views
- Permission-profile selector
- Images and file mentions
- Automatic protocol type generation from the installed Codex version
- Signed release builds for macOS, Windows, and Linux

## Disclaimer

This is an independent, unofficial project. It is not affiliated with, endorsed by, or sponsored by OpenAI.

Codex and OpenAI are trademarks of OpenAI. This project does not use OpenAI logos or claim to be an official client.

## License

Apache License 2.0. See [`LICENSE`](LICENSE).
