# S5–S8 — Run Log

Date: 2026-11-02
Runtime: .NET 10 (`net10.0-windows`), LLamaSharp 0.30.0, Windows 11, NVIDIA GeForce RTX 5070 Ti (Vulkan)

## Units completed

### S5 — WPF shell + tray
- `src/Forge.App/` created: `App.xaml` / `App.cs`, tray via **Hardcodet.NotifyIcon.Wpf** with state icons (loading / ready / error, `BitmapImage`).
- Context menu: Settings / Exit. No main form — single-window-less tray app.
- Model loads in the background at startup; tray state updates from InferenceEngine load/unload results.

### S6 — Task overlay + hotkeys
- `HotkeyManager`: `RegisterHotKey` via `HwndSource.AddHook` — callback fires **on the UI thread**.
- Popup hotkey (default Ctrl+E) opens the 4-task overlay (translate / rewrite / proofread / summarize); keyboard navigation (↑↓ + Enter), Esc cancels.
- Immediate hotkeys: config-driven from `%APPDATA%\Forge\hotkeys.json` — **no built-in defaults** (spec's Ctrl+E+1/2 example not implemented).
- Re-entry guard: `ProcessingGuard.TryBegin()` acquired at every task entry point (wired in review commit `68f09b1`).

### S7 — Result editor + settings
- `ResultEditorWindow`: split view, diff highlighting on completion (`DiffRenderer`), editable result text.
- Buttons: Replace (event exposed for S11), Copy, Regenerate (closes the current window, then fresh run — fixed in review commit `68f09b1`), Cancel.
- `SettingsWindow`: model / engine / hardware / prompts / hotkeys tabs; persists to `%LOCALAPPDATA%\Forge\settings.json`.
- **Spec drift:** the "precision" tab from the spec is not implemented in v1 (temperature etc. deferred).

### S8 — Hardware detection + idle unload
- `HardwareDetector`: Vulkan/CUDA probe → VRAM → recommended `GpuLayerCount` (999 when VRAM sufficient, else 0).
- Injected into InferenceEngine at startup; CPU-first default preserved (`gpu layer count: 0 (mode: Cpu)` in debug log).
- `IdleUnloadManager`: unloads after N seconds idle (settings), on-demand reload on next run.

## Review pass (commit `68f09b1`)

Three bugs found and fixed in `App.cs`:

1. **Re-entry guard was dead code** — `TryBegin()` was never called, so hotkeys fired during processing (S6 spec §4 violation). Now acquired at all three entry points (immediate hotkey, overlay selection, Regenerate).
2. **`--debug-overlay` flag discarded** — `ParseArgs` consumed the flag but never set `showOverlay = true`, so the debug hook never worked.
3. **Regenerate opened a second window** on top of the first; now closes the current window before starting the fresh run.

False alarm retracted: a cross-thread WPF access concern was based on a stale file cache; the actual `HotkeyManager` fires on the UI thread and needs no change.

## Verification evidence

- Build: Forge.App / Core / Cli — 0 errors.
- Tests: 92/92 passing (`Forge.Core.Tests`).
- Live smoke (`--demo --debug-overlay --debug-log`): hardware detection logged → hotkey registered → overlay shown (fix 2 verified live) → demo stream→diff completion.
- GPU offload: **passed per maintainer decision** — this machine runs on the GPU, and the load log confirms the Vulkan device is detected.

## Pending human verification

- [ ] S5: tray icon state transitions (loading → ready), context menu, "error" state on failed load.
- [ ] S6: Ctrl+E overlay keyboard navigation; hotkey ignored during processing (review fix 1).
- [ ] S7: diff highlight quality; Regenerate closes the old window (review fix 3); settings sync with S6/S8 state.
- [ ] S8: idle unload → memory reclaimed → reload on next run.

## Notes for later sessions (S9–S13)

- `ForgeSettings` (`src/Forge.Core/ForgeSettings.cs`) already holds model/engine/hardware/prompt settings — S9's ModelManager should write the selected model path through it, not invent a second config file.
- `ProcessingGuard`, `IdleUnloadManager`, `HardwareDetector` are in `Forge.Core`; app-side wiring lives in `App.cs`.
- Debug log: `--debug-log PATH` flag + `DebugLog.Write(...)`; keep using it for S9–S13 verification runs.
