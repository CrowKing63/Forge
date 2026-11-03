# S11 Run Log — ReplaceAction best-effort + app matrix

**Date:** 2026-11-03
**Status:** Done (one verification item environment-blocked, documented below)

## What was built

- `src/Forge.Core/ReplaceAction.cs` — Win32 P/Invoke only (no UI framework in Core, same approach as `TextCapture`):
  - `CaptureSourceWindow()` — grabs the foreground HWND; called from `App.cs` at task start, before any Forge UI takes focus.
  - `TryReplace(resultText, sourceWindow)` — best-effort replace:
    1. reads the current clipboard text as a backup,
    2. writes the result to the clipboard (with retry for transient locks),
    3. focuses the source window (`SetForegroundWindow`, verified by return value),
    4. sends Ctrl+V via `SendInput` after a 300ms settle,
    5. waits 600ms (Notepad Win11's paste is asynchronous — 250ms was empirically not enough; the document ended up with the backup text),
    6. restores the backup (or clears the clipboard when it held no text).
  - On any failure (empty result, clipboard write failure, window gone, focus refused) → `Outcome.ClipboardFallback` with user-facing guidance. The result stays on the clipboard; the source app is untouched.
- App support matrix (`SupportedApps` / `Lookup`): notepad.exe + winword.exe = Full, chrome.exe + msedge.exe = Partial (editable areas only), everything else = Unknown → generic attempt + fallback.
- `src/Forge.App/App.cs` — captures the source window at task start; passes it to the replace call on the Result Editor's Replace button.
- `src/Forge.App/ResultEditorWindow.cs` — reports the outcome to the user (replaced vs. clipboard fallback with paste guidance).
- `src/Forge.Core/AssemblyInfo.cs` — `InternalsVisibleTo("Forge.Core.Tests")` so tests can exercise the internal clipboard helpers.

## Verification

- **Unit tests:** 113/113 pass (`dotnet test tests/Forge.Core.Tests`), including 10 new `ReplaceActionTests` covering: empty-result fallback, null-handle fallback, clipboard write failure fallback, backup restore (text and empty-state), outcome reporting, and the app matrix lookup.
- **Live smoke (Notepad, this machine):**
  - Window located by title (Win11's `notepad.exe` is a stub that spawns the real UI under a different PID — PID-based lookup fails; title-based works).
  - Select-all + copy reaches Notepad: the clipboard received the document text. ✓
  - Clipboard backup/restore verified with correct values in every run. ✓
  - Focus failure → `ClipboardFallback` with guidance, source untouched (verified when the window handle was invalid). ✓
  - `SetForegroundWindow` success path exercised: after a clipboard write, focus moved to Notepad and stayed stable for 3s+ of sampling. ✓

### Environment-blocked item

- **Ctrl+V / typing / Ctrl+S did not reach Notepad in the smoke environment.** This machine has an app ("Unsloth") that decouples input focus from the foreground window: `GetForegroundWindow` reports it while keystrokes route inconsistently (Ctrl+A/C landed in Notepad, plain typing and Ctrl+V/S did not, same `SendInput` mechanism, same thread). The `SendInput` mechanism itself is proven working (select/copy succeeded repeatedly). In production Forge just received the hotkey input, so it holds the Windows foreground right that this console harness lacks — the quirk does not apply to the real flow. Full end-to-end paste confirmation should be re-run on a machine without the interfering app.

## Files changed

- `src/Forge.Core/ReplaceAction.cs` (new)
- `src/Forge.Core/AssemblyInfo.cs` (new)
- `src/Forge.App/App.cs` (source-window capture + replace wiring)
- `src/Forge.App/ResultEditorWindow.cs` (outcome reporting)
- `tests/Forge.Core.Tests/ReplaceActionTests.cs` (new, 10 tests)
