# S12 Run Log — re-entry guard, error UX, crash dump, clipboard restoration

**Date:** 2026-11-03
**Status:** Done

## What was built

- `src/Forge.Core/ClipboardGuard.cs` (new) — the single shared OS-clipboard primitive (`WriteText`, `ReadText`, `Clear`). Every replace/copy/read path in Core and App goes through it, so backup/restore behavior is uniform (plan §4 non-destructive).
- `src/Forge.Core/CrashDump.cs` (new) — opt-in crash dump writer. Records timestamp, .NET runtime, model path, GPU layer count, and the exception with stack trace to `%LOCALAPPDATA%\Forge\CrashDumps\crash-<timestamp>.log`. **Structurally cannot carry text content** — its API accepts no text (plan §4 privacy). Returns `null` if writing fails; a dump must never take down the app.
- `src/Forge.Core/ForgeSettings.cs` — new `CrashDumpEnabled` setting (default `false`, opt-in per plan §7).
- `src/Forge.Core/TextCapture.cs` — clipboard reads now go through `ClipboardGuard`; capture failures throw `TextCaptureException` with a user-facing message that names the recovery path.
- `src/Forge.Core/ReplaceAction.cs` — replaced its internal clipboard helpers with `ClipboardGuard` (write, backup read, restore/clear).
- `src/Forge.App/App.cs`:
  - Global exception handlers: `DispatcherUnhandledException` + `AppDomain.UnhandledException`. A fatal exception writes the crash dump (when opt-in is on), shows a MessageBox with the dump path and "Restart Forge to continue", then shuts down — no WPF crash dialog, text and clipboard untouched.
  - Error UX per path: capture failure → status bar message; model load failure → tooltip with "Open Settings to check the model path"; replace failure → S11 fallback guidance (unchanged).
  - `--smoke-crash` debug hook (same pattern as `--demo`): after startup settles, raises an unhandled dispatcher exception and forces the dump opt-in on — used for end-to-end verification of the handler.
- `src/Forge.App/SettingsWindow.cs` — Engine tab gains "Write crash dumps on fatal errors (no text content is recorded)" checkbox, wired to load/save.
- `src/Forge.App/ResultEditorWindow.cs` — Copy button now goes through `ClipboardGuard.WriteText` with a failure status.
- Re-entry guard: no new code — `ProcessingGuard` (S6) is already wired into every entry point (hotkey press, tray task selection, Regenerate); verified by reading the call sites and its unit tests.

## Verification

- **Unit tests:** 118/118 pass (`dotnet test tests/Forge.Core.Tests`), including:
  - `CrashDumpTests` (new, 3): diagnostic file contents (exception, model path, gpu layer count, runtime header), no-text-payload contract, timestamped file name.
  - `ClipboardGuardTests` (new, 2): write→read round trip, clear→no text. Runs in the `"clipboard"` xunit collection together with `ReplaceActionTests` — the OS clipboard is a single shared resource, so all clipboard-touching tests serialize there.
  - `ForgeSettingsTests`: `CrashDumpEnabled` defaults to `false` and round-trips through save/load.
  - `ReplaceActionTests`: updated to the new `ClipboardGuard` API; all outcomes unchanged.
- **Live smoke (this machine):**
  - `Forge.App.exe --smoke-crash` started, loaded the real model (RTX 5070 Ti detected), then raised the hooked exception at +1.5s.
  - Crash dump written: `crash-20261103-092811.log` contains timestamp, `.NET 10.0.14`, the real model path, `gpu layer count: 0`, and the exception with stack trace pointing at the hook — **no text payload** (the hook's message is the only string in it). ✓
  - The MessageBox guidance box was shown: the process stayed alive in the modal loop until killed manually — proving the handler reached the user-facing step before shutdown. ✓
  - Settings file untouched by the smoke run (the opt-in force was in-memory only; no `settings.json` existed to modify). ✓

## Files changed

- `src/Forge.Core/ClipboardGuard.cs` (new)
- `src/Forge.Core/CrashDump.cs` (new)
- `src/Forge.Core/ForgeSettings.cs` (CrashDumpEnabled)
- `src/Forge.Core/TextCapture.cs` (shared clipboard primitive, TextCaptureException message)
- `src/Forge.Core/ReplaceAction.cs` (uses ClipboardGuard)
- `src/Forge.App/App.cs` (global handlers, crash dump wiring, error UX, --smoke-crash hook)
- `src/Forge.App/SettingsWindow.cs` (crash dump checkbox)
- `src/Forge.App/ResultEditorWindow.cs` (copy via guard)
- `tests/Forge.Core.Tests/CrashDumpTests.cs` (new)
- `tests/Forge.Core.Tests/ClipboardGuardTests.cs` (new)
- `tests/Forge.Core.Tests/ReplaceActionTests.cs` (updated API, collection)
- `tests/Forge.Core.Tests/ForgeSettingsTests.cs` (CrashDumpEnabled assertions)
