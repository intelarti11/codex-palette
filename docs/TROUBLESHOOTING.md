# Troubleshooting

## The palette does not appear

1. Confirm that the official Codex desktop app is open.
2. Bring Codex to the foreground. The palette is hidden while unrelated applications are active.
3. Close duplicate development terminals.
4. Run `launch-silent.bat` again from the project root.

The launcher must remain open because it owns the Vite/Electron development process.

## Codex cannot be restarted

Close every Codex window and wait a few seconds, then run the launcher again. If a background process remains, end only the `ChatGPT.exe` processes whose path contains `OpenAI.Codex_` and retry.

## The CDP port is unavailable

The launcher chooses a random port between 41000 and 49000 and binds it to `127.0.0.1`. A security product can still block local debugging endpoints. Confirm that the launcher prints a port and that Codex was started by that launcher.

For development, an explicit port can be provided before `npm run dev`:

```powershell
$env:CODEX_CDP_PORT = '45000'
```

Do not bind the endpoint to a non-loopback address.

## Vite reports that port 5173 is already in use

Another palette development process is probably still running. The launcher stops Node and Electron processes started from the current project directory before starting a new session. Close older terminals and run the launcher again if Vite continues choosing additional ports.

## Electron reports cache access errors

Cache errors usually mean that duplicate Electron palette processes are running. Close old palette terminals or run `launch-silent.bat`, which removes project-local Node and Electron processes before startup.

## The shortcut does not open the palette

- The shortcut is active only while Codex or the palette is in the foreground.
- The default Codex selector shortcut is `Ctrl+Shift+M`.
- The palette reads the configured shortcut when it starts. Restart the palette after changing the shortcut in Codex.
- Another application can reserve the same global key combination. Change the Codex shortcut and restart the palette if registration fails.

## Native Codex menus open unexpectedly

The current default path does not open menus. Make sure legacy fallback flags are not set:

```powershell
Remove-Item Env:CODEX_UIA_SCAN_FALLBACK -ErrorAction SilentlyContinue
Remove-Item Env:CODEX_UIA_MODIFIER_FALLBACK -ErrorAction SilentlyContinue
```

Then restart the palette.

## The selected check does not match the active task

Switch to another task and back once. The watcher synchronizes selection when the native selector state changes. If the problem persists, include the Codex version, model label, reasoning level, and sanitized terminal output in a bug report.

## The palette is misplaced or covers the microphone

Use the reset button in the palette header. The overlay should match the native selector's bottom-right anchor and preserve the exact selector cutout. Include Windows display scaling in any placement bug report.

## Collecting safe diagnostic information

Useful information includes:

- Codex desktop version;
- Windows version and display scaling;
- palette commit;
- visible model and effort labels;
- sanitized terminal errors.

Do not publish task identifiers, local authentication data, cookies, API keys, CDP dumps, or conversation content.
