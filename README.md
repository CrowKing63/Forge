# Forge

A local translator into English for Windows. Select text in any app, press **Ctrl+E**, and Forge translates it with a local AI model and replaces the selection in place — no cloud, no account, no telemetry. Built for anyone working in a language that isn't English.

## How it works

- A global hotkey (default **Ctrl+E**) captures the selected text from the focused app
- The text is translated by a local GGUF model running on your machine (CPU or GPU)
- The translation replaces the selection in place, with a small hammer indicator while the model works and a short clang on success

## Installation

### Installer (per-user, no admin required)

1. Download `ForgeSetup-<version>.exe` from the [Releases](https://github.com/CrowKing63/Forge/releases) page.
2. Run it — the app is installed to `%LOCALAPPDATA%\Programs\Forge`. Your settings and models live in `%LOCALAPPDATA%\Forge` and are kept across updates and uninstall.
3. If Windows shows a SmartScreen warning (the build is not code-signed), click **More info** → **Run**.

### Portable

Download the portable zip, extract it anywhere, and run `Forge.App.exe`. Add an empty `portable.txt` file next to the exe and Forge keeps its settings and models in that folder instead of `%LOCALAPPDATA%\Forge` — copy the folder to a USB drive and go.

## First run: model download

Forge ships without a model. On your first translation it asks whether to download the default model — **Gemma 4 E2B (QAT), UD-Q4_K_XL, about 2.6 GB** — from Hugging Face. The download is resumable and verified against a SHA-256 checksum. You can also drop any `.gguf` file into the models folder (`%LOCALAPPDATA%\Forge\Models`, or next to the exe in portable mode) and pick it in Settings.

## Usage

1. Select text in any Windows app.
2. Press **Ctrl+E** (rebindable in Settings).
3. A small hammer indicator appears over the selection while the model works; on success the translation replaces the selection and a short clang plays.
4. If Forge can't write back into the app, it puts the translation on your clipboard and tells you to press **Ctrl+V**.

To quit Forge: right-click the tray icon → **Exit**. Closing windows does not quit the app — it lives in the tray.

## Settings

Tray icon → **Settings**: model cards (with an in-app download section for catalog models), hotkey rebinding, hardware selection, and idle-unload choices (immediate / 1 min / 5 min / never). Changes save automatically — there is no Save button.

## Requirements

- Windows 10 or 11, x64
- About 2.6 GB of free disk space for the default model (more for larger models)
- No .NET install needed — the build is self-contained

## Supported apps

In-place replacement works on Win32 edit controls (Notepad, Word) and Chromium text fields (Edge, Chrome). For other apps Forge falls back to the clipboard with a paste prompt.

Note: on Windows 11 Notepad, turn off "Autocorrect typos" and "Check spelling as you type" if you ever use the clipboard fallback — Notepad's spellcheck can interrupt injected keystrokes.

## Privacy

Everything runs locally. The only network access is the model download you explicitly start; there is no telemetry and no other traffic.

## Model licensing

The default model (Gemma 4 E2B QAT GGUF) is downloaded from Hugging Face under Google's Gemma Terms of Use — that license governs the model file, not this software.

## Development

- `use.bat` — run the real flow from source (tray + Ctrl+E; debug log in `%TEMP%\forge-use.log`)
- Build each project separately (`src/Forge.App`, `src/Forge.Core`, `src/Forge.Cli`, `tests/Forge.Core.Tests`) — there is no solution file
- Installer: `installer/forge.isn` (Inno Setup 6); the header comments in that file describe the build order

## License

MIT — see [LICENSE](LICENSE).
