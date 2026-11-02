# S4 — E2E Run Log (M1 complete)

Date: 2026-11-02
Model: `gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf` (unsloth, HF cache)
Runtime: .NET 10, LLamaSharp 0.30.0, Windows 11

## Pipeline verified end-to-end

Clipboard capture → task prompt → streaming inference → parsed result, driven from the console:

```
forge proofread|translate|rewrite|summarize [--model PATH]
```

Native llama.cpp log noise is suppressed (`NativeLogConfig.llama_log_set(NullLogger)`) and
console output is UTF-8, so Korean text stays readable in any terminal or redirect.

## Task runs (real samples)

### 1. proofread (Korean)

Input (55 chars / 30 tokens):
> 오늘 날씨가 너무 더워서 밖에 나가기 싶다. 어제부터 계속 비가 오고 있었는데 오늘만 이렇게 덥다.

Output:
> 오늘 날씨가 너무 더워서 밖에 나가고 싶다. 어제부터 계속 비가 오고 있었는데 오늘만 이렇게 덥다.

`나가기 싶다 → 나가고 싶다` — grammar fixed, meaning preserved. ✓

### 2. translate (Korean → English)

Input (62 chars / 31 tokens):
> 우리 회사는 다음 주에 새로운 제품을 발표할 계획이다. 이 제품은 기존 제품보다 30% 더 빠르고 가격은 같다.

Output:
> Our company plans to announce a new product next week. This product is 30% faster than the existing one and has the same price.

Accurate, natural English. ✓

### 3. rewrite (Korean)

Input (98 chars / 40 tokens):
> 그는 어제 회의에서 프로젝트의 진행 상황에 대해 많은 이야기를 했다. 그리고 그 이야기들은 대부분 이미 알려진 내용들이었다. 하지만 그는 다시 한 번 그것들을 반복해서 말했다.

Output:
> 그는 어제 회의에서 프로젝트 진행 상황에 대해 많은 이야기를 했지만, 대부분 이미 알려진 내용이었다. 그럼에도 그는 그것들을 다시 반복했다.

Verbose → concise, meaning preserved. ✓

### 4. summarize (Korean)

Input (232 chars / 122 tokens): a paragraph about a team switching development tools
(maintenance cost, build time halved, next-quarter rollout plan).

Output:
> 팀은 유지보수 비용과 생산성 저하 문제를 해결하기 위해 새로운 개발 도구로 전환했습니다. 이 새로운 도구는 설정이 간단하고 자동화 기능이 강력하여, 빌드 시간 절반 감소와 같은 긍정적인 성과를 보였습니다. 다음 분기에는 이 도구를 다른 팀원들에게도 확대할 계획입니다.

Covers all key points in two sentences. ✓

## Failure paths (all exit 1, no crash)

| Case | Console message |
|---|---|
| Empty clipboard | `No text was found on the clipboard. Select some text and copy it (Ctrl+C), then try again.` |
| Context overflow (5760 tokens > 4096) | `The selection is 5760 tokens, which exceeds the maximum of 4096. Select a shorter passage and try again.` |
| Missing model file | `Model file not found.` |

Token counts in the guard message come from the engine's own tokenizer
(`InferenceEngine.CountTokens`), replacing S2's char-based estimate.

## S0 baseline regression check

| Metric | S0 baseline | S4 runs | Verdict |
|---|---|---|---|
| Model load | 2.5 s | 1.9–2.0 s | no regression |
| Decode speed | 10.5–11.4 tok/s | 12.2–13.8 tok/s | no regression |
| Time to first token | ~0.6 s | 0.47–0.96 s | no regression |
| Unload | 0.3 s | n/a (process exit) | n/a |

## Test suite

54/54 xunit tests pass, including the new S4 tests:
- `TextCaptureTests` — injected exact-token counter (accept / reject / empty / null-injection).
- `InferenceEngineTests` — `CountTokens` on a real model (sane counts for known strings).

## Changes in this step

- `src/Forge.Cli/Program.cs` — E2E loop wiring S1+S2+S3, failure paths, clean console output.
- `src/Forge.Core/InferenceEngine.cs` — `CountTokens(string)` via the loaded tokenizer.
- `src/Forge.Core/TextCapture.cs` — optional exact token counter injection (replaces char estimate).
- `tests/Forge.Core.Tests/*` — new tests for both.
