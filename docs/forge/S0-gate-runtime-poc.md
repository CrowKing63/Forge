# S0 — 게이트: 런타임 PoC (CPU + 실제 UD-Q4_K_XL)

## 목표
LLamaSharp가 **실제 `UD-Q4_K_XL.gguf` 파일을 CPU에서 로드·추론**할 수 있는지, 체감 가능 속도/로드시간을 측정해 전제를 검증한다. 이 유닛이 통과해야 나머지 전부 시작된다.

## 배경 (레포 기준)
- greenfield: `src/Forge.Cli/` 미생성.
- 근거: plan §2(모델·벤치마크), §3(Option B LLamaSharp), §8(M0).
- 참고: unsloth HF 카드(`unsloth/gemma-4-E2B-it-qat-GGUF`), LLamaSharp NuGet + `LLamaSharp.Backend.Cpu`.

## 결정
- 런타임: **LLamaSharp + `LLamaSharp.Backend.Cpu`** (GPU는 S8에서).
- 파일: **UD-Q4_K_XL.gguf만** (Q4_0 아님 — Unsloth 권고, plan §2).
- `ctx_size = 8192`, CPU-only(`GpuLayerCount=0`).
- 이 유닛은 **스파이크(throwaway)** — 생산 코드가 아니라 전제 검증.

## 작업
1. `%LOCALAPPDATA%\Forge\Models\` 에 UD-Q4_K_XL.gguf 다운로드 (HuggingFace).
2. `src/Forge.Cli/` 콘솔 앱 생성 (.NET 10, LLamaSharp + Backend.Cpu).
3. 모델 로드 → 한국어 교정 프롬프트(plan §5) 1개 실행 → 출력 표시.
4. 측정: (a) 첫 로드 시간, (b) decode tok/s(~50 token 프롬프트), (c) unload 후 메모리 회수 확인.
5. 5문장 한국어 교정 샘플로 출력 품질 육안 확인.

## 하지 말 것
- GPU 오프로드(CUDA/Vulkan) — S8에서 다룸.
- 스트리밍 UI, 설정 화면, 모델 카탈로그 — 이후 유닛.
- 생산 InferenceEngine API — S1에서 다룸(여기서는 최소 스파이크만).

## 검증 (통과 기준 — 전부 만족해야 다음 진행)
- [x] CPU에서 UD-Q4_K_XL 로드 성공 (크래시 없음)
- [x] decode **≥ 10 tok/s** (CPU, ~50 token 프롬프트) → 실측 **10.5 tok/s**
- [x] 첫 로드 ≤ 60초, unload 정상 (메모리 회수) → 실측 로드 **2.5s**, unload **0.3s**
- [x] ctx 8192가 ~4GB RAM 예산 내 로드 (KV cache 48+96 MiB + compute buffer 521 MiB, 모델 버퍼 ~1.5 GiB)
- [x] 한국어 교정 샘플 5문장: 일관된 한국어 출력, 영어 누출·절단 없음 (육안) → 5/5 정확

## 결과 — PASS (2026-11-02, CPU-only 실측)
- **통과.** `GpuLayerCount=0`으로 GPU 미사용 확인. 로드 2.5s, decode 10.5 tok/s, unload 0.3s.
- **한국어 품질:** 5문장 전부 정확히 교정 (어제 밤→어젯밤, 중요하다라고→중요하다고, 모인후→모인 후), 이미 맞는 문장은 그대로 유지. 영어 누출·절단 없음.
- **핵심 발견 (S1에 반영 필요):** **LLamaSharp 0.30.0은 Gemma 4의 내장 Jinja 채팅 템플릿을 실행하지 못함.** `ApplyTemplate=true`는 템플릿 소스를 출력으로 뱉고 크래시(exit 127), `ApplyTemplate=false` 생텍스트는 빈 출력·에코·영역 번역 혼재. → **앱이 Gemma 4 역할 마커로 프롬프트를 수동 구성해야 함:**
  ```
  <|turn>system\n{지시문}\n<turn|>\n<|turn>user\n{한국어 원문}\n<turn|>\n<|turn>model\n
  ```
- **.NET 타겟:** 스파이크를 **net8.0과 net10.0 모두로 검증** — 결과 동일(5/5 정확), decode 11.4 tok/s(net8 10.5, 노이즈 범위 내). **결정: net10.0** (현재 LTS, 2028-11까지 지원; .NET 8은 2026-11-10 EOL).
- **패키징 발견:** `LLamaSharp.Backend.Cpu`는 네이티브 DLL(`llama.dll`, `ggml*.dll`)을 출력 폴더에 자동 카피하지 않음 → csproj에서 명시 `<None Include>` + `CopyToOutputDirectory` 필요 (S1 패키징 과제).

## 산출물
- `src/Forge.Cli/` 스파이크 코드 + 실행 로그(로드시간·tok/s·메모리 수치) — `Program.cs`, `s0-results.txt`(5문장 IN/OUT + TIMING)
- 통과 결론: **PASS** (plan §8 M0 산출물로 병기)
