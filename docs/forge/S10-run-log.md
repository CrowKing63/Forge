# S10 — Run Log

Date: 2026-11-03
Runtime: .NET 10 (`net10.0-windows`), Inno Setup 6.7.3, Windows 11

## What was built

### `installer/forge.isn`
- Per-user install (`PrivilegesRequired=lowest`) → `%LOCALAPPDATA%\Programs\Forge`, no admin required.
- Bundles the self-contained `win-x64` publish (exe + .NET runtime + `llama.dll`/`ggml*.dll` + tray icons).
- Start menu + desktop shortcuts; post-install "Launch Forge" entry.
- SmartScreen guidance: post-install message explains the unsigned build and how to proceed ("More info" → "Run"). Guarded by `WizardSilent` so silent installs never block on the dialog.
- Uninstall removes only the app directory — user data (`%LOCALAPPDATA%\Forge\settings.json`, `Models\`) is preserved.

### First-run onboarding (`src/Forge.App/OnboardingWindow.cs`)
- Code-only WPF window (matches the S5–S7 convention): heading, progress bar, status line, Cancel button.
- Wired into `App.LoadModelAsync`: when the configured model file is missing, the window downloads it via `ModelManager` (S9) — resume from `.part` is automatic.
- A path with no catalog source (custom GGUF never downloaded) offers "Open Settings" instead of a download.

### Default model path moved to the managed directory
- `ForgeSettings.DefaultModelPath` is now `%LOCALAPPDATA%\Forge\Models\gemma-4-e2b-qat-ud-q4_k_xl.gguf` (was a maintainer-machine HuggingFace cache path). New installs get a path onboarding can actually download; existing settings files are unaffected.
- `App.cs` dropped its duplicate constant; `ParseArgs`/`ParseModelPath` take the fallback explicitly.

## Verification evidence

- Build: 0 errors. Tests: 103/103 passing.
- Installer compiled with ISCC 6.7.3 → `installer/Output/ForgeSetup-0.1.0.exe`.
- **Clean install/uninstall cycle (twice):** silent install placed exe + native DLLs + icons; uninstall removed the app dir and left `%LOCALAPPDATA%\Forge\Models` intact.
- **Onboarding live check:** ran the installed exe with `--debug-log` on a machine without the model file → log shows `onboarding shown (model file missing)` and a real download progressed (`.part` grew to ~408 MB before the test kill). The partial `.part` was left in place — the next run resumes from it by design.
- **Bug found & fixed during verification:** the post-install `MsgBox` blocked silent installs indefinitely; fixed with a `WizardSilent` guard and re-verified (silent install now exits cleanly).

## Notes for later sessions (S11–S13)

- The installer is unsigned; SmartScreen guidance lives in the post-install message. Code signing is explicitly deferred (spec "하지 말 것").
- `installer/publish/` and `installer/Output/` are gitignored; build order is documented in the `.isn` header.
- Onboarding is a thin UI over S9's `ModelManager` — no download logic of its own.
