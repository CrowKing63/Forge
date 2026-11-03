# S13 Run Log — Korean eval set + prompt tuning

**Date:** 2026-11-03
**Status:** Done (one documented model limitation, below)

## What was built

- `src/Forge.Cli/eval-set.json` (new) — the Korean eval set: 3 items per task (proofread / translate / rewrite / summarize), each with a Korean input and a **manually labeled** expected output (plan §7). Covers typos, spacing, tone preservation, cross-language leakage, verbosity, and person shifts.
- `src/Forge.Cli/Program.cs` — new `eval` mode: `dotnet run -- eval [--model <path>] [--gpu] [--out <file>]`. Loads the model once, runs every item through `PromptBuilder` → `InferenceEngine` → `ResultParser`, and applies mechanical gates per item:
  - non-empty output,
  - no refusal/error markers,
  - no language mismatch (via `ResultParser`),
  - summaries ≤ 3 sentences,
  - translations contain no Hangul.
  The report pairs every actual output with its manual label for human review. Exit code 0 only when all gates pass — this is the regression gate for future prompt changes.
- `src/Forge.Cli/Forge.Cli.csproj` — copies `eval-set.json` next to the exe.
- `src/Forge.Core/PromptBuilder.cs` — two tuned prompts (below).
- `tests/Forge.Core.Tests/PromptBuilderTests.cs` — pinned prompt strings updated to the S13-tuned text.

## Tuning iterations

Baseline run (original prompts): 12/12 mechanical pass; manual review found two quality problems:

1. **proofread-01** — input `그건 내 생각엔 좀 안되겟다.` came out as `...안 되겠다.` instead of the intended `...안 될 것 같다.`: a misspelling was "corrected" into a different word with a different meaning (intention vs. supposition).
2. **summarize-03** — a first-person blog-style source was summarized in the third person (`글쓴이는 ...`).

Changes applied and re-verified:

- **Proofread:** added *"When a word is misspelled, correct it to the intended word — do not replace it with a different word that changes the sentence's meaning."*
- **Summarize:** iterated twice. First attempt (*"same person (first person stays first person)"*) fixed summarize-03 but **regressed summarize-02** — a third-person source (`회사는 ...`) came out first person (`저는 ...`). Final wording: *"Same language and same person as the source text."* — verified correct in both directions.

Final run: 12/12 mechanical pass; manual review clean on all items except the one documented below.

## Documented limitation

- **proofread-01 still drifts** (`안 되겠다` instead of `안 될 것 같다`). The typo `안되겟다` is genuinely ambiguous — it reads as both `안 되겠다` and `안 될 것 같다` — and the 2B model picks the former regardless of prompt wording. This is a model-size limitation (plan §7 keeps Gemma 4 E2B for v1; a larger model is future work), not a prompt defect. The eval set keeps the item so any future model swap is checked against it.

## Verification

- [x] **Quality criteria across the whole eval set** (meaning preserved, no truncation, consistent language): 12/12 mechanical gates pass in every run; manual review of each report confirms meaning/tone/language on all items except the documented limitation.
- [x] **Prompt changes cause no regression** (eval re-passes): after each prompt change the full eval was re-run — 12/12 every time, including catching and fixing the intermediate summarize regression.
- [x] **Representative samples per task improved**: summarize person handling verified in both directions; proofread typo-handling instruction added (class-level improvement; the one ambiguous case documented above).
- Unit tests: 118/118 pass after updating the pinned prompt strings.

## Files changed

- `src/Forge.Cli/eval-set.json` (new)
- `src/Forge.Cli/Program.cs` (eval mode)
- `src/Forge.Cli/Forge.Cli.csproj` (eval set copy)
- `src/Forge.Core/PromptBuilder.cs` (tuned Proofread + Summarize prompts)
- `tests/Forge.Core.Tests/PromptBuilderTests.cs` (pinned text updated)
