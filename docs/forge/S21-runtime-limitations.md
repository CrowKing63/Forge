# S21 — 런타임 한계: LLamaSharp 0.30.0과 수정 방향

## 배경

LFM3-1.7B(QAD-Q4_0, BF16) 테스트 중 로드 실패를 조사하면서 발견한 런타임 수준의
한계를 정리한다. LFM3 파일은 유효했으나 Forge(LLamaSharp 0.30.0)에서
`LoadWeightsFailedException`으로 로드 불가.

## 확인된 한계 (2026-11-08 실측)

### 1. 번들 llama.dll에 신형 아키텍처 부재

- LLamaSharp NuGet 패키지는 **릴리스 시점의 llama.cpp 스냅샷을 동결한 네이티브
  DLL**을 번들한다. C# API는 얇은 P/Invoke 래퍼에 불과.
- `llama.dll`(win-x64/avx2) 바이너리 grep: `lfm2` 있음, **`lfm3` 없음**
  (`gemma4`, `gemma3`, `qwen3`은 있음).
- LFM3 GGUF의 `general.architecture = "lfm3"` → 로드 불가. 파일 자체는 유효:
  로컬 새 빌드(`e71c8f673`)로 `llama-bench` 실측 성공 — CPU 8스레드
  **tg 29.4 t/s / pp 268 t/s**.

### 2. Gemma 4 채팅 템플릿 실행 불가 (S1에서 이미 확인)

- LLamaSharp 0.30.0의 번들 llama.cpp은 Gemma 4 내장 Jinja 템플릿을 해석하지 못함
  (템플릿 소스를 덤프하고 크래시). `ApplyTemplate`은 `false` 고정, 프롬프트
  조립은 `PromptBuilder`가 수동 마커로 담당.
- 결과: 12B 모델 출력 앞뒤에 `<|channel>thought ... <channel|>` 사고 채널 마커가
  스스로 붙는 현상 (S13에서 E2B는 잘 참아 클린 통과, 12B는 아님).

### 3. 업그레이드 경로 부재

- NuGet `llamasharp` 최신 버전 = **0.30.0** (38개 버전 중 마지막).
- 더 새로운 llama.cpp 스냅샷을 받을 공식 경로가 없음.

## 새 런타임이 할 수 있는 것 (e71c8f673 실측)

| 항목 | 결과 |
|---|---|
| LFM3 로드·추론 | ✅ `llama-bench` — tg 29.4 t/s |
| Gemma 4 Jinja 템플릿 실행 | ✅ `llama-server` chat completion 성공 — 크래시·누출 없음 |
| 사고(thinking) 채널 | 템플릿 파라미터(`enable_thinking`)로 제어 가능 — 서버 요청 필드 전달 세부 확인 필요 |

→ 새 런타임으로 교체하면 **LFM3 로딩과 Gemma 4 템플릿 문제가 동시에 해결**될
가능성 높음. `ApplyTemplate=true`로 전환 시 `PromptBuilder`의 수동 마커 조립을
대체 가능 (후속).

## 수정 방향

### Option 1: LLamaSharp 자체 빌드

SciSharp/LLamaSharp(오픈소스)를 새 llama.cpp 기준으로 빌드해 로컬 NuGet으로
사용. 불확실성: 그들의 네이티브 빌드 파이프라인, 0.30.0 → 최신 API drift.

### Option 2: 자체 얇은 FFI (추천)

자체 빌드 `llama.dll` 위에 Forge가 필요한 최소 표면(모델 로드/컨텍스트/
토크나이즈/스트리밍 디코드/언로드)만 P/Invoke로 작성.

- **범위**: `InferenceEngine`(208줄) 내부 교체 — API(LoadModel/Complete/Unload/
  CountTokens) 유지. PromptBuilder·ModelManager·UI·CLI 불변.
- **회귀 기준**: Gemma 4 E2B가 S13/S15 벤치마크 베이스라인과 일치 + LFM3 신규
  테스트 케이스.
- **CI/패키징**: 릴리스 portable zip에 네이티브 DLL 빌드 스텝 추가 (또는
  prebuilt DLL 체크인).
- **계속 비용**: llama.cpp 업데이트가 Forge의 책임(빌드·테스트·패키징).

### 프로토타입 (완료 — 2026-11-08 실측)

raw FFI 하네스(`scratch/ffi-proto`, ~300줄 C#)로 전 경로 검증:

| 항목 | 결과 |
|---|---|
| P/Invoke 모델 로드·토큰화·디코드·생성 | ✅ LFM3 QAD 1.7B 정상 추론 ("Paris" 답변) |
| `llama_chat_apply_template` (LFM3) | ✅ 적용 성공 — 단, **`parse_special=true` 토큰화 필수** |
| `llama_chat_apply_template` (Gemma 4) | ❌ 실패 — "jinja 파서 없음, 사전 정의 목록만 지원" (llama.h:1376) |
| greedy(argmax) + parse_special=false | ❌ 반복 루프 퇴화 (`<\|im_start\|>assistant` spam) |
| temp=0.8 샘플링 + parse_special=true | ✅ 정상 응답 |

**핵심 발견 — 템플릿 충실성:**
- C API의 내장 템플릿 적용은 jinja를 해석하지 않고 **사전 정의된 패턴 목록만**
  지원한다. LFM3은 목록에 있어(근사 적용), Gemma 4(18,808자 jinja 매크로)는
  목록에 없어 완전 실패.
- → Forge가 임의 GGUF 템플릿을 충실히 적용하려면 **실제 jinja 렌더러**가
  필요하다 (C# 내장 해석기 또는 NuGet). 이것이 gemma4 12B "채널" 사고 마커
  누출의 근인: 현재 LLamaSharp 경로는 Gemma 4 템플릿을 제대로 적용하지 못함.
- `parse_special=true`는 채팅 형식 프롬프트 토큰화의 전제 — 특수 마커가
  일반 문자로 쪼개지면 모델이 퇴화한다.

→ Option 2(자체 FFI)의 기술적 타당성 입증. `InferenceEngine` 스왑 시 포함
해야 할 것: jinja 렌더러 + parse_special=true + 샘플링 파라미터 노출.

## 결정 로그

- 2026-11-08: LFM3 두 모델 테스트 → 로드 불가 확인, 근인(번들 DLL 아키텍처 부재)
  규명. 수동 추가 개념 제거(카탈로그 전용) — `87e037e`.
- 2026-11-08: FFI 프로토타입 완료 — P/Invoke 전 경로 검증 성공, 템플릿
  충실성 한계(C API jinja 부재) 규명. Option 2 채택 근거 확보.
