# Forge — 로컬 LLM 텍스트 처리 앱: 실현 가능성 조사 & 설계 계획

## 1. 목표

Gemma 4 E2B QAT GGUF를 **기본 모델**로, 선택한 문장을 **변역 / 재작성 / 교정**해서 대체하는 경량 데스크톱 앱.
Handy(로컬 받아쓰기)와 유사한 UX: 글로벌 핫키 → 텍스트 캡처 → 로컬 LLM 처리 → 결과 확인 → 원본 교체.
완전 오프라인, 프라이버시 우선.

- **CPU 구동이 1순위.** CPU-only가 보장된 기본 경로이며, GPU(Vulkan/CUDA)는 선택적 가속.
- **모델은 사용자 선택 가능.** 기본은 Gemma 4 E2B(한국어 직접 테스트로 선별)이나, 영미권/원어 작업에는 더 가벼운 로컬 GGUF를 내려받아 전환할 수 있음.

---

## 2. 모델 분석: Gemma 4 E2B QAT GGUF (기본 모델)

| 항목 | 값 |
|------|-----|
| 총 파라미터 | 5.1B (유효 2.3B, PLE 구조) |
| 레이어 | 35 |
| 컨텍스트 | 128K tokens (앱은 `ctx_size`로 캡) |
| 어휘 | 262K |
| 언어 | 140+ (한국어 포함) |
| 멀티모달 | 텍스트 + 이미지 + 오디오 (E2B) |
| 라이선스 | Apache 2.0 |
| QAT | 양자화 인식 학습 — bfloat16과 유사한 품질 유지 |

**한국어 품질:** 이 무게급 모델 몇 개를 직접 써보고 선별한 결과로, 한국어 교정/변역 품질은 검증된 선택(리스크 낮음). 영미권/원어는 해당 용량이 다소 과한데, 이는 사용자 선택 경량 모델로 대응.

### GGUF 퀀트 옵션 (unsloth/gemma-4-E2B-it-qat-GGUF)

| 정밀도 | 파일 크기 | 메모리 (추정) | 비고 |
|--------|-----------|---------------|------|
| UD-Q2_K_XL | 2.19 GB | ~3.0 GB | 최소, 저사양 폴백 |
| **UD-Q4_K_XL** | **2.62 GB** | **~3.5–4.0 GB** | **권장 — 배포 파일 (Unsloth 권고)** |
| MTP Q4_0 (draft) | 59.2 MB | +~100 MB | 보류 — Gemma4용 MTP 아직 미지원 |
| MTP Q8_0 (draft) | 97.8 MB | +~150 MB | 보류 — 위와 동일 |

> Unsloth: "QAT GGUF는 **UD-Q4_K_XL 하나만** 권고 — 더 높은 정밀도는 오히려 정확도 저하, Q4_0은 크기가 더 큰데도 정확도 하락." MTP draft의 Gemma4 백본 지원은 llama.cpp에서 아직 planned.

### 성능 벤치마크 (llama.cpp, decode tok/s)

| 하드웨어 | 백엔드 | 정밀도 | decode tok/s | 메모리 |
|----------|--------|--------|-------------|--------|
| i7-1165G7 (6t) | CPU | Q3_K_S | 15.3 | 2,741 MB |
| i7-10750H @3.4GHz | CPU | UD-Q4_K_XL | ~18–20 | ~3,000 MB |
| GTX 1660 Ti | CUDA | UD-Q4_K_XL | ~99 | VRAM ~4 GB |
| RTX 3050 | CUDA | UD-Q4_K_XL | ~100 | VRAM ~4 GB |
| RTX 3060 12GB | Vulkan | Q4_K_M | ~76 | VRAM ~4 GB |
| Apple M3 Max | Metal | Q4_K_M | ~108 | 3.8 GB |

**결론:** **CPU-only가 1순위 보장 경로** — ~15–20 tok/s로 짧은 문장(50–200 token) 처리에 충분. GPU 있으면 60–100 tok/s로 체감 즉시 반응(선택적 가속).

> **S0 실측 (2026-11-02, 타겟 머신 CPU-only, UD-Q4_K_XL):** decode **10.5 tok/s**(net8) / **11.4 tok/s**(net10), 첫 로드 **~2.5–2.7s**, unload 0.3s. Q4_K_XL이 벤치마크 행의 Q3_K_S보다 커서 수치 약간 낮음 — 예상 범위(~15–20) 내, 짧은 문장 교정에 충분. net8/net10 결과 동일(5/5). → M0 통과, **타겟 net10.0**.

---

## 3. 런타임 옵션 비교

### Option A: 인-프로세스 내장 (llama.cpp 라이브러리 직접 링크)

- **방식:** 앱 안에 `libllama`(C++) 또는 바인딩(Rust: `llama-cpp-rs`, .NET: LLamaSharp)을 내장 → 프로세스 내에서 모델 로드/추론/언로드
- **CPU/GPU/Auto:** `-ngl` 파라미터로 GPU 오프로드 층수 제어, `ggml_backend_dev_list()`로 하드웨어 감지
- **모델 라이프사이클:** `loadModel()` on-demand 로드, `unloadModel()` idle 시 언로드 — 앱이 전 과정 관리
- **Handy의 STT 모델 관리와 동일한 패턴** (`transcribe-cpp` 인-프로세스)
- **장점:** 프로세스/포트 없음, 완전 자급자족, 자유로운 로드/언로드, 배포 단순(단일 exe + GGUF)
- **단점:** MTP 스펙ulative decoding 아직 바인딩 미지원 (llama.cpp 최신만, Gemma4 백본 planned)

### Option B: LLamaSharp (.NET 네이티브 래퍼) — **WPF 확정 선택**

- **방식:** .NET 앱 안에 `llama.dll` 내장 → C#에서 직접 추론 호출
- **CPU/GPU/Auto:** CPU 기본. GPU 오프로드는 백엔드 NuGet 패키지(`LLamaSharp.Backend.Vulkan` / `Cuda11` / `Cuda12` / `OpenCL`)로 지원 — `GpuLayerCount`가 `-ngl`에 해당
- **TextRecast가 이 방식 사용** — WPF + LLamaSharp
- **장점:** 프로세스 없음, .NET 생태계 친화, 배포 단순(단일 exe), C#에서 명시적 load/unload, Vulkan/CUDA 백엔드 제공
- **단점:** llama.cpp 최신 기능(MTP) 반영 지연

### Option C: Ollama 외부 의존

- **방식:** 사용자가 Ollama 설치 → `ollama run gemma4:e2b` → 앱이 `localhost:11434` 호출
- **장점:** 모델 라이프사이클 자동 관리(idle 언로드), 커뮤니티 크고, OpenAI 호환 API
- **단점:** 외부 의존(설치/버전), "완전 단일 앱"이 아님, Ollama 자체 메모리 오버헤드

### Option D: llama-server 사이드카 프로세스

- **방식:** 앱이 `llama-server` 바이너리를 부트로 스폰 → OpenAI 호환 HTTP API로 호출
- **장점:** MTP 스펙ulative decoding 지원, 모든 GPU 백엔드(CUDA/Vulkan/DX12)
- **단점:** 별도 프로세스 관리, 포트 충돌, 모델 항상 로드 상태(자유 언로드 불가)

### 추천: **Option B (LLamaSharp, 인-프로세스 내장)**

WPF 확정으로 구체적 선택은 **Option B**. Handy가 STT에서 한 것과 동일한 패턴: 앱이 모델 다운로드·로드·추론·언로드를 전 과정 자체 관리.
외부 서버/프로세스 의존 없음. CPU/GPU/Auto는 `GpuLayerCount`(-ngl)로 앱이 직접 제어, Vulkan/CUDA 백엔드 패키지로 GPU 오프로드.
MTP는 2B 모델에서 체감 이득이 작아(~15→18 tok/s) + Gemma4용 아직 미지원 → 우선순위 보류.

---

## 4. UX/UX 설계 (Handy 패턴 적용)

### 핵심 워크플로

```
[핫키 A: 팝업형] → [작업 선택 오버레이] → [LLM 처리] → [결과 미리보기] → [교체/복사/취소]
[핫키 B: 즉시형] → [LLM 처리 (기본 작업)] → [결과 미리보기] → [교체/복사/취소]
```

### 핫키 설계

| 유형 | 동작 | 예시 | 비고 |
|------|------|------|------|
| **팝업형** (기본) | 오버레이 표시 → 작업 선택 후 처리 | `Ctrl+E` | 작업 목록: 변역/재작성/교정/요약/톤 |
| **즉시형** (추가) | 팝업 없이 기본 작업 즉시 실행 | `Ctrl+E+1` | 설정에서 작업 지정 (예: 교정) |
| **즉시형** (추가) | 팝업 없이 특정 작업 즉시 실행 | `Ctrl+E+2` | 설정에서 작업 지정 (예: 변역 KR→EN) |

- 사용자가 설정창에서 추가 핫키를 자유롭게 등록/수정
- 각 즉시형 핫키에 어떤 작업이 바인딩되는지 사용자 지정
- Handy의 "포스트프로세싱 전용 핫키" 패턴 확장: 전용 핫키가 하나가 아니라 N개 가능
- **재진입 가드:** 추론 처리 중에는 핫키를 무효화(또는 큐잉) — 이중 추론 방지

### 화면 구성

1. **트레이 아이콘** (상시)
   - 모델 상태(로딩 중 / 준비됨 / 에러)
   - 설정 열기, 종료

2. **작업 선택 오버레이** (팝업형 핫키 시)
   - 소형 팝업: `변역(KR→EN)` / `재작성` / `교정` / `요약` / `톤 변경`
   - 키보드 내비게이션(↑↓ + Enter) 또는 마우스 클릭
   - 선택 즉시 처리 시작, Esc로 취소

3. **결과 에디터** (처리 완료 시 — 두 핫키 공통)
   - 원본 vs 결과 분할 뷰 (diff 하이라이트)
   - `[교체]` `[복사]` `[다시 생성]` `[취소]` 버튼
   - 편집 가능 (수동 수정 후 교체)
   - 스트리밍 중은 raw text 표시 → 완료 시점에 diff 렌더

4. **설정 화면** (트레이 메뉴)
   - 모델: **사용자 선택(카탈로그)** + 정밀도(Q2/Q4), 컨텍스트 크기
   - 하드웨어: CPU / GPU / Auto (자동 감지 결과 표시, CPU 기본)
   - 프롬프트: 작업별 시스템 프롬프트 편집
   - **핫키:** 팝업형 기본 핫키 + 즉시형 추가 핫키 등록/수정
     - 각 즉시형 핫키 → 작업 매핑 (드롭다운)
     - 충돌 감지 및 경고
   - 모델 다운로드/업데이트

### UX 원칙 (Handy에서 차용)

- **프라이버시:** 텍스트가 어디에도 전송되지 않음. 로컬 디스크 로그 없음(단, opt-in 최소 crash dump만 — 텍스트 내용 제외).
- **즉각성:** CPU(기본) ~15–20 tok/s → 200 token 문장 ≈ 10–13초 (진행 바 표시). GPU 있으면 ~100 tok/s → ≈ 2초.
- **비파괴적:** 교체 전까지 원본 건드리지 않음. 클립보드 백업/복원.
- **최소 UI:** 오버레이 하나 + 결과 에디터 하나. 설정은 트레이 메뉴.

---

## 5. 아키텍처 설계

```
┌─────────────────────────────────────────────────────┐
│  App (WPF .NET 10)                                    │
│  ┌───────────┐  ┌──────────────┐  ┌──────────┐     │
│  │ Tray/Overlay│  │ Result Editor │  │ Settings │    │
│  └─────┬─────┘  └──────┬───────┘  └────┬─────┘     │
│        │                │               │           │
│  ┌─────▼────────────────▼───────────────▼─────────┐ │
│  │  Core: TextCapture → PromptBuilder → Inference  │ │
│  │         → ResultParser → ReplaceAction          │ │
│  └─────────────────────┬───────────────────────────┘ │
│                         │                            │
│  ┌──────────────────────▼───────────────────────────┐ │
│  │  Inference Engine (인-프로세스, LLamaSharp)        │ │
│  │  · Vulkan/CUDA/CPU 백엔드 패키지                    │ │
│  │  · loadModel() on-demand                         │ │
│  │  · GpuLayerCount: CPU(0) / GPU(999) / Auto(감지)  │ │
│  │  · ctx_size: 8192                                │ │
│  │  · unloadModel() idle timeout 후                  │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘

  모델 파일: %LOCALAPPDATA%\Forge\Models\<model>.gguf
  (앱이 다운로드·검증·관리 — 다중 모델 카탈로그, Handy catalog 패턴)
```

### 핵심 컴포넌트

| 컴포넌트 | 역할 | 구현 참고 |
|----------|------|-----------|
| **TextCapture** | 활성 윈도우 선택 텍스트 읽기 + 최대 입력 토큰 제한 | **선택지 클립보드(주)** / UI Automation(보조). TextRecast 방식. IME/특수 앱 대비 클립보드 우선 |
| **PromptBuilder** | 작업별 시스템 프롬프트 + 사용자 텍스트 조립 | Handy `${output}` 템플릿 패턴 |
| **InferenceEngine** | 인-프로세스 모델 로드/추론/언로드, 스트리밍 토큰 생성 | LLamaSharp `Llama` — `loadModel()`, `decode()`, `unloadModel()` |
| **HardwareDetector** | GPU 감지 → `GpuLayerCount` 자동 설정 | CPU 1순위 기본. Vulkan/CUDA 백엔드 탐지 시 오프로드 |
| **ModelManager** | GGUF 다운로드(재개+SHA256), 체크섬 검증, **다중 모델 카탈로그**, 버전 관리, idle 언로드 타이머 | TextRecast 모델 카탈로그 + Handy catalog 패턴 |
| **ReplaceAction** | 원본 텍스트 교체 + 클립보드 복원 | **best-effort**: UI Automation SetFocus + SendKeys. 실패 시 클립보드 자동 폴백. 앱별 지원 매트릭스 |

### 프롬프트 설계 (작업별)

```
[교정]
system: "Fix grammar, spelling, and punctuation. Do not change meaning, tone, or language. Output only the corrected text."
user: {selected_text}

[변역 KR→EN]
system: "Translate the following Korean text to natural English. Preserve meaning and tone. Output only the translation."
user: {selected_text}

[재작성]
system: "Rewrite the following text for clarity and conciseness. Keep the same language and tone. Output only the rewritten text."
user: {selected_text}

[요약]
system: "Summarize the following text in 2-3 sentences. Same language. Output only the summary."
user: {selected_text}
```

---

## 6. 기술 스택 결정 (권장)

| 레이어 | 선택 | 이유 |
|--------|------|------|
| **UI** | WPF (.NET 10) | Windows 전용 타겟(UIA/SendKeys/MSIX), TextRecast 검증 패턴, 기존 .NET 스택 일치. (Tauri 크로스플랫폼 이득은 무력) |
| **추론** | LLamaSharp (llama.cpp 인-프로세스) | 자유 로드/언로드, 프로세스 없음, Handy STT와 동일 패턴. Vulkan/CUDA/CPU 백엔드 패키지 |
| **모델** | 기본 Gemma 4 E2B (UD-Q4_K_XL) + 사용자 선택 로컬 모델 | 한국어 직접 테스트 선별; 영미권/원어는 경량 GGUF로 용량 절감 |
| **배포** | 단일 설치기 (Inno Setup / MSIX) | exe + llama.dll 번들, GGUF는 첫 실행 다운로드(재개+SHA256) |

### Windows 특이사항

- **UI Automation:** 관리자 권한 앱의 텍스트 캡처 불가 → 클립보드 폴백 필요
- **클립보드 복원:** 교체 전 클립보드 내용 저장, 교체 후 복원 (TextRecast 패턴)
- **SmartScreen:** 서명 없는 설치기 → 첫 실행 "고급 옵션" 클릭 필요
- **GPU 드라이버:** Vulkan(AMD/Intel iGPU) 또는 CUDA(NVIDIA) — LLamaSharp 백엔드 패키지 지원. CPU가 기본, GPU는 가속
- **crash dump:** opt-in 최소 로그(스택+모델+타임스탬프, 텍스트 내용 제외)로 네이티브 크래시 디버깅

---

## 7. 리스크 & 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| CPU-only에서 느림 (~15–20 tok/s) | UX 체감 지연 | **CPU가 1순위 보장 경로** — 진행 바 + 진행 표시. GPU(Vulkan/CUDA)는 선택적 가속. MTP는 Gemma4 지원 시 추가 |
| 4GB RAM 미만 기기 | 모델 로드 실패 | Q2_K_XL(2.2 GB) 폴백 옵션, 로드 전 메모리 체크 |
| 한국어 품질 (직접 테스트로 선별 — 낮음) | 교정/변역 품질 | 사용자 피드백 루프. 영미권/원어는 경량 모델 선택으로 대응 |
| llama.dll 네이티브 크래시 (OOM, GPU 드라이버) | 앱 전체 중단 | try/catch로 네이티브 하드크래시 구제 불가(=앱 종료 전제). (a) 보수적 메모리/ctx 설정, (b) 로컬 파일이라 재로드 수초, (c) opt-in 최소 crash dump. worker 프로세스 격리는 인-프로세스 원칙과 상충 → 채택 안 함 |
| 입력 길이 오버플로 (ctx 초과) | 추론 실패/오류 | 최대 입력 토큰 제한 + 명확한 에러 메시지 |
| 텍스트 캡처/교체 실패 (특수 앱, IME) | 워크플로 차단 | 클립보드 폴백(주) + "복사해서 붙여넣기" 수동 모드. 교체는 best-effort |

---

## 8. 마일스톤 (제안)

| 단계 | 내용 | 산출물 |
|------|------|--------|
| **M0: 프로토타입** ✅ (2026-11-02) | LLamaSharp + Gemma E2B UD-Q4_K_XL 인-프로세스 로드, CLI로 프롬프트 → 출력 확인. **CPU 구동 1순위 확인** (로드/언로드 시간 포함) | 스크립트(`src/Forge.Cli/`) + 벤치마크 수치(10.5 tok/s) — `docs/forge/S0-gate-runtime-poc.md` |
| **M1: 코어** | TextCapture(클립보드 주) + InferenceEngine + 결과 파싱 (CLI 또는 콘솔 앱) | 동작하는 엔드투엔드 파이프라인 |
| **M2: UI** | 트레이 + 오버레이 + 결과 에디터 (WPF) | GUI 프로토타입 |
| **M3: 하드웨어** | GPU 감지(Vulkan/CUDA), CPU/GPU/Auto 전환, idle 언로드 타이머 | 설정 화면 + 자동 구성 |
| **M4: 배포** | 모델 다운로드(다중 카탈로그), 설치기, 첫 실행 온보딩 | 릴리스 빌드 |
| **M5: 정교화** | 프롬프트 튜닝, 에러 처리, 클립보드 복원, 교체 앱별 지원, 성능 최적화 | 안정 버전 |

---

## 9. 참고 리소스

- [Gemma 4 E2B QAT GGUF (HuggingFace)](https://huggingface.co/unsloth/gemma-4-E2B-it-qat-GGUF)
- [llama.cpp 빌드 가이드 (Windows CUDA/Vulkan)](https://github.com/ggml-org/llama.cpp/blob/master/docs/build.md)
- [LLamaSharp (.NET llama.cpp 바인딩)](https://github.com/SciSharp/LLamaSharp)
- [TextRecast (WPF + LLamaSharp, 선택 텍스트 로컬 재작성)](https://github.com/snss10/TextRecast)
- [ZyntaxAI (Tauri, 핫키로 교정/변역/재작성, Ollama 오프라인)](https://github.com/TheHolyOneZ/ZyntaxAI)
- [Handy (Tauri, 로컬 받아쓰기 — STT 모델 인-프로세스 관리)](https://github.com/cjpais/Handy)
- [Handy Post-Processing 문서](https://handy.computer/docs/post-processing.html)
- [Gemma 4 E2B 벤치마크 (CPU/GPU, llama.cpp)](https://pt13762104.github.io/2026/06/06/taking-a-look-at-gemma-4-e2b-qat/)
