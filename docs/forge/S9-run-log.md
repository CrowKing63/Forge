# S9 — Run Log

Date: 2026-11-03
Runtime: .NET 10 (`net10.0`), Windows 11

## What was built

### `src/Forge.Core/ModelManager.cs`
- **Catalog**: built-in default (Gemma 4 E2B QAT UD-Q4_K_XL, 2,620,370,976 bytes, SHA256 `e5310072…889`) + scan of the models directory for user GGUF files.
- **Download**: HTTP with `Range` resume (`.part` file), SHA256 verification after completion, atomic move to final path.
  - Server ignores Range (200 instead of 206) → part discarded, restart from zero.
  - Checksum mismatch → part deleted, `ChecksumMismatchException` thrown, no final file.
  - Already-downloaded model → no-op.
- **Disk check**: free space ≥ model size before starting (`IOException` otherwise).
- **Selection**: `Select(model, settings)` writes the chosen path into `ForgeSettings.ModelPath` — the existing config channel, no second config file (per S5–S8 run-log note).

### `tests/Forge.Core.Tests\ModelManagerTests.cs`
11 unit tests with a fake `HttpMessageHandler`: full download, resume from part, restart on ignored Range, checksum mismatch, corrupt part rejected, progress reporting, disk-full rejection, no-op re-download, catalog scan, selection.

## Verification evidence

- Build: 0 errors.
- Tests: **103/103 passing** (92 pre-existing + 11 new).
- Live smoke (scratch console app against real HTTP, `bartowski/SmolLM2-135M-Instruct-GGUF` Q4_0, 91,893,088 bytes):
  - Full download: 9.5 s, size + SHA256 verified (`bcc3af28…04b03`).
  - Resume: 40 MB `.part` pre-seeded → Range request completed the remainder, final file verified.
  - Mismatch guard: wrong checksum → exception, no final file, part discarded.
  - `Select()` wrote `settings.ModelPath`.

## Notes for later sessions (S10–S13)

- `ModelManager` is UI-free and testable; the S10 installer/onboarding flow can call it directly from a settings window.
- The built-in default model entry assumes the HF cache path (`%USERPROFILE%\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-UD-Q4_K_XL/snapshots/<rev>/gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf`) — S10 onboarding should verify the file exists and, if not, offer the download.
- Progress reporting uses `IProgress<DownloadProgress>`; wire it to a progress bar in the UI layer.
