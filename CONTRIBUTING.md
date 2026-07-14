# Contributing

Thank you for helping improve Codex Palette Overlay.

This project integrates with private Codex desktop renderer structures and Windows accessibility APIs. Small, well-tested changes are easier to review and safer for users than broad rewrites.

## Before opening an issue

Please include:

- the Codex desktop app version;
- Windows version and display scaling;
- the palette commit or release version;
- the visible model and reasoning labels;
- the exact reproduction steps;
- relevant terminal output with personal paths, task identifiers, and conversation content removed.

Never attach authentication data, CDP dumps, cookies, account tokens, API keys, or unredacted conversation content.

## Development setup

```powershell
git clone https://github.com/intelarti11/codex-palette.git
cd codex-palette
npm install
.\launch-silent.bat
```

The launcher restarts Codex with a random loopback-only CDP port. Keep its terminal open during development.

## Code guidelines

- Preserve silent operation: do not move the cursor, synthesize mouse clicks, or force Codex to the foreground.
- Prefer data and callbacks already exposed by the native Codex selector.
- Keep CDP expressions narrowly scoped and return only the minimum required data.
- Avoid continuous full accessibility-tree scans.
- Keep UI text in English unless it is read directly from Codex's localized interface.
- Preserve the context-isolated preload boundary.
- Add or update tests for behavior changes.

## Validation

Run both checks before submitting a pull request:

```powershell
npm test
npm run build
```

For visual changes, also test the expanded palette at its 640 px width and confirm that it remains aligned with the native selector and does not cover the microphone button.

## Pull requests

Keep each pull request focused. Describe:

- what changed;
- why the change is needed;
- which Codex desktop version was tested;
- how silent behavior was verified;
- which automated and manual checks passed.

By contributing, you agree that your contribution is licensed under the project's Apache License 2.0.
