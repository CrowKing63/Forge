# Forge — 로컬 LLM 텍스트 처리 앱: 실현 가능성 조사 & 설계 계획

## 1. 목표

Gemma 4 E2B QAT GGUF 모델을 기반으로, 선택한 문장을 **변역 / 재작성 / 교정**해서 대체하는 경량 데스크톱 앱.
Handy(로컬 받아쓰기)와 유사한 UX: 글로벌 핫키 → 텍스트 캡처 → 로컬 LLM 처리 → 결과 확인 → 원본 교체.
완전 오프라인, 프라이버시 우선.

---

## 2. 모델 분석: Gemma 4 E2B QAT GGUF

| 항목 | 값 |
|------|-----|
| 총 파라미터 | 5.1B (유효 2.3B, PLE 구조) |
| 레이어 | 35 |
| 컨텍스트 | 128K tokens |
| 어휘 | 262K |
| 언어 | 140+ (한국어 포함) |
| 멀티모달 | 텍스트 + 이미지 + 오디오 (E2B) |
| 라이선스 | Apache 2.0 |
| QAT | 양자화 인식 학습 — bfloat16과 유사한 품질 유지 |

### GGUF 퀀트 옵션 (unsloth/gemma-4-E2B-it-qat-GGUF)

| 정밀도 | 파일 크기 | 메모리 (추정) | 비고 |
|--------|-----------|---------------|------|
| UD-Q2_K_XL | 2.19 GB | ~3.0 GB | 최소, 품질 약간 저하 |
| **UD-Q4_K_XL** | **2.62 GB** | **~3.5–4.0 GB** | **추천 — 속도/품질 균형** |
| MTP Q4_0 (draft) | 59.2 MB | +~100 MB | MTP 스펙ulative decoding용 drafter |
| MTP Q8_0 (draft) | 97.8 MB | +~150 MB | 고정밀 drafter |

### 성능 벤치마크 (llama.cpp, decode tok/s)

| 하드웨어 | 백엔드 | 정밀도 | decode tok/s | 메모리 |
|----------|--------|--------|-------------|--------|
| i7-1165G7 (6t) | CPU | Q3_K_S | 15.3 | 2,741 MB |
| i7-10750H @3.4GHz | CPU | Q4_0 | ~18–20 | ~3,000 MB |
| GTX 1660 Ti | CUDA | Q4_0 | ~99 | VRAM ~4 GB |
| RTX 3050 | CUDA | Q4_0 | ~100 | VRAM ~4 GB |
| RTX 3060 12GB | Vulkan | Q4_K_M | ~76 | VRAM ~4 GB |
| Apple M3 Max | Metal | Q4_K_M | ~108 | 3.8 GB |

**결론:** CPU-only에서도 ~15 tok/s로 짧은 문장(50–200 token) 처리에 충분. GPU 있으면 60–100 tok/s로 체감 즉시 반응.

---

## 3. 런타임 옵션 비교

### Option A: 인-프로세스 내장 (llama.cpp 라이브러리 직접 링크)

- **방식:** 앱 안에 `libllama`(C++) 또는 바인딩(Rust: `llama-cpp-rs`, .NET: LLamaSharp)을 내장 → 프로세스 내에서 모델 로드/추론/언로드
- **CPU/GPU/Auto:** `-ngl` 파라미터로 GPU 오프로드 층수 제어, `ggml_backend_dev_list()`로 하드웨어 감지
- **모델 라이프사이클:** `loadModel()` on-demand 로드, `unloadModel()` idle 시 언로드 — 앱이 전 과정 관리
- **Handy의 STT 모델 관리와 동일한 패턴** (`transcribe-cpp` 인-프로세스)
- **장점:** 프로세스/포트 없음, 완전 자급자족, 자유로운 로드/언로드, 배포 단순(단일 exe + GGUF)
- **단점:** MTP 스펙ulative decoding 아직 바인딩 미지원 (llama.cpp 최신만), Vulkan GPU는 C++ 직접 링크 필요

### Option B: LLamaSharp (.NET 네이티브 래퍼)

- **방식:** .NET 앱 안에 `llama.dll` 내장 → C#에서 직접 추론 호출
- **CPU/GPU/Auto:** CPU 기본. GPU 오프로드는 CUDA 빌드 가능 (TextRecast는 CPU-only 사용)
- **TextRecast가 이 방식 사용** — WPF + LLamaSharp CPU
- **장점:** 프로세스 없음, .NET 생태계 친화, 배포 단순(단일 exe), C#에서 명시적 load/unload
- **단점:** GPU 오프로드 제한적, llama.cpp 최신 기능(MTP, Vulkan) 반영 지연

### Option C: Ollama 외부 의존

- **방식:** 사용자가 Ollama 설치 → `ollama run gemma4:e2b` → 앱이 `localhost:11434` 호출
- **장점:** 모델 라이프사이클 자동 관리(idle 언로드), 커뮤니티 크고, OpenAI 호환 API
- **단점:** 외부 의존(설치/버전), "완전 단일 앱"이 아님, Ollama 자체 메모리 오버헤드

### Option D: llama-server 사이드카 프로세스

- **방식:** 앱이 `llama-server` 바이너리를 부트로 스폰 → OpenAI 호환 HTTP API로 호출
- **장점:** MTP 스펙ulative decoding 지원, 모든 GPU 백엔드(CUDA/Vulkan/DX12)
- **단점:** 별도 프로세스 관리, 포트 충돌, 모델 항상 로드 상태(자유 언로드 불가)

### 추천: **Option A/B (인-프로세스 내장)**

Handy가 STT에서 한 것과 동일한 패턴: 앱이 모델 다운로드·로드·추론·언로드를 전 과정 자체 관리.
외부 서버/프로세스 의존 없음. CPU/GPU/Auto는 `-ngl` 파라미터로 앱이 직접 제어.
MTP는 2B 모델에서 체감 이득이 작아(~15→18 tok/s) 우선순위 양립.

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

4. **설정 화면** (트레이 메뉴)
   - 모델: 정밀도(Q2/Q4), 컨텍스트 크기
   - 하드웨어: CPU / GPU / Auto (자동 감지 결과 표시)
   - 프롬프트: 작업별 시스템 프롬프트 편집
   - **핫키:** 팝업형 기본 핫키 + 즉시형 추가 핫키 등록/수정
     - 각 즉시형 핫키 → 작업 매핑 (드롭다운)
     - 충돌 감지 및 경고
   - 모델 다운로드/업데이트

### UX 원칙 (Handy에서 차용)

- **프라이버시:** 텍스트가 어디에도 전송되지 않음. 로컬 디스크 로그 없음.
- **즉각성:** GPU ~100 tok/s → 200 token 문장 ≈ 2초. CPU ~15 tok/s → ≈ 13초 (진행 바 표시)
- **비파괴적:** 교체 전까지 원본 건드리지 않음. 클립보드 백업/복원.
- **최소 UI:** 오버레이 하나 + 결과 에디터 하나. 설정은 트레이 메뉴.

---

## 5. 아키텍처 설계

```
┌─────────────────────────────────────────────────────┐
│  App (WPF .NET 10 / Tauri Rust — 선택待定)           │
│  ┌───────────┐  ┌──────────────┐  ┌──────────┐     │
│  │ Tray/Overlay│  │ Result Editor │  │ Settings │     │
│  └─────┬─────┘  └──────┬───────┘  └────┬─────┘     │
│        │                │               │           │
│  ┌─────▼────────────────▼───────────────▼─────────┐ │
│  │  Core: TextCapture → PromptBuilder → Inference  │ │
│  │         → ResultParser → ReplaceAction          │ │
│  └─────────────────────┬───────────────────────────┘ │
│                         │                            │
│  ┌──────────────────────▼───────────────────────────┐ │
│  │  Inference Engine (인-프로세스)                    │ │
│  │  LLamaSharp / llama-cpp-rs / libllama            │ │
│  │  · loadModel() on-demand                         │ │
│  │  · ngl: CPU(0) / GPU(999) / Auto(감지)           │ │
│  │  · ctx_size: 8192                                │ │
│  │  · unloadModel() idle timeout 후                  │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘

  모델 파일: %LOCALAPPDATA%\Forge\Models\gemma-4-E2B-UD-Q4_K_XL.gguf
  (앱이 다운로드·검증·관리 — Handy catalog 패턴)
```

### 핵심 컴포넌트

| 컴포넌트 | 역할 | 구현 참고 |
|----------|------|-----------|
| **TextCapture** | 활성 윈도우 선택 텍스트 읽기 | Windows UI Automation / 클립보드 폴백 (TextRecast 방식) |
| **PromptBuilder** | 작업별 시스템 프롬프트 + 사용자 텍스트 조립 | Handy `${output}` 템플릿 패턴 |
| **InferenceEngine** | 인-프로세스 모델 로드/추론/언로드, 스트리밍 토큰 생성 | LLamaSharp `Llama` / `llama-cpp-rs` — `loadModel()`, `decode()`, `unloadModel()` |
| **HardwareDetector** | GPU 감지 → `-ngl` 자동 설정 | `ggml_backend_dev_list()` 또는 CUDA/Vulkan 초기화 시 탐지 |
| **ModelManager** | GGUF 다운로드, 체크섬 검증, 버전 관리, idle 언로드 타이머 | TextRecast 모델 카탈로그 + Handy catalog 패턴 |
| **ReplaceAction** | 원본 텍스트 교체 + 클립보드 복원 | UI Automation SetFocus + SendKeys / 클립보드 |

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
| **UI** | WPF (.NET 10) 또는 Tauri (Rust+React) | WPF: TextRecast 검증된 패턴, Windows 네이티브. Tauri: Handy 패턴, 크로스 플랫폼 |
| **추론** | llama.cpp 인-프로세스 내장 (LLamaSharp / llama-cpp-rs) | 자유 로드/언로드, 프로세스 없음, Handy STT와 동일 패턴 |
| **모델** | Gemma 4 E2B QAT GGUF UD-Q4_K_XL | 2.6 GB, 한국어 포함 140+ 언어, Apache 2.0 |
| **배포** | 단일 설치기 (Inno Setup / MSIX) | exe + llama.dll 번들, GGUF는 첫 실행 다운로드 |

### Windows 특이사항

- **UI Automation:** 관리자 권한 앱의 텍스트 캡처 불가 → 클립보드 폴백 필요
- **클립보드 복원:** 교체 전 클립보드 내용 저장, 교체 후 복원 (TextRecast 패턴)
- **SmartScreen:** 서명 없는 설치기 → 첫 실행 "고급 옵션" 클릭 필요
- **GPU 드라이버:** Vulkan(AMD/Intel iGPU) 또는 CUDA(NVIDIA) — 둘 다 지원하면 `-ngl` 자동

---

## 7. 리스크 & 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| CPU-only에서 느림 (~15 tok/s) | UX 체감 지연 | 진행 바 + "GPU 권장" 안내. MTP는 바인딩 지원 시 추가 |
| 4GB RAM 미만 기기 | 모델 로드 실패 | Q2_K_XL(2.2 GB) 폴백 옵션, 로드 전 메모리 체크 |
| Gemma 4 E2B의 한국어 품질 | 교정/변역 품질 불안 | 프롬프트 튜닝 + 사용자 피드백 루프. E4B(4.5B) 업그레이드 경로 |
| llama.dll 네이티브 크래시 (OOM, GPU 드라이버) | 앱 전체 중단 | 프로세스 격리(별도 worker 프로세스) 또는 try/catch + 자동 재로드 |
| 텍스트 캡처 실패 (특수 앱) | 워크플로 차단 | 클립보드 폴백 + "복사해서 붙여넣기" 수동 모드 |

---

## 8. 마일스톤 (제안)

| 단계 | 내용 | 산출물 |
|------|------|--------|
| **M0: 프로토타입** | LLamaSharp/llama-cpp-rs + Gemma E2B Q4_0 인-프로세스 로드, CLI로 프롬프트 → 출력 확인 | 스크립트 + 벤치마크 수치 (로드/언로드 시간 포함) |
| **M1: 코어** | TextCapture + InferenceEngine + 결과 파싱 (CLI 또는 콘솔 앱) | 동작하는 엔드투엔드 파이프라인 |
| **M2: UI** | 트레이 + 오버레이 + 결과 에디터 (WPF/Tauri) | GUI 프로토타입 |
| **M3: 하드웨어** | GPU 감지, CPU/GPU/Auto 전환, idle 언로드 타이머 | 설정 화면 + 자동 구성 |
| **M4: 배포** | 모델 다운로드, 설치기, 첫 실행 온보딩 | 릴리스 빌드 |
| **M5: 정교화** | 프롬프트 튜닝, 에러 처리, 클립보드 복원, 성능 최적화 | 안정 버전 |

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
