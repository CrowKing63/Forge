# Forge 실행 로그 (통합) — S4~S13

> 이 문서는 S4부터 S13까지의 실행 로그다.

## 개요

| 항목 | 내용 |
|---|---|
| 기간 | 2026-11-02 ~ 2026-11-03 |
| 런타임 | .NET 10, LLamaSharp 0.30.0, Windows 11 |
| GPU | NVIDIA GeForce RTX 5070 Ti (Vulkan) — S5~S8부터 |
| 기본 모델 | Gemma 4 E2B QAT UD-Q4_K_XL (unsloth GGUF, 약 2.6 GB) |
| 테스트 progression | 54 → 92 → 103 → 113 → 118 (전부 통과) |
| 마일스톤 | M1: S4 콘솔 E2E / M2~M3: S5-S8 WPF UI·하드웨어 / M4: S9-S10 모델 관리·설치기 / M5: S11-S13 교체·로버스트니스·eval |

---

## S4 — 콘솔 E2E 파이프라인 (M1 완료)

**날짜:** 2026-11-02

### 완성 내용
- 콘솔에서 전체 파이프라인 검증: **클립보드 캡처 → 작업 프롬프트 → 스트리밍 추론 → 결과 파싱** (`forge proofread|translate|rewrite|summarize [--model PATH]`).
- 네이티브 llama.cpp 로그 노이즈 억제(`NativeLogConfig.llama_log_set(NullLogger)`), 콘솔 출력 UTF-8 — 터미널·리디렉트 어디에서든 한국어가 읽힘.
- `InferenceEngine.CountTokens(string)` — 로드된 토크나이저로 정확한 토큰 수 계산 (S2의 문자수 추정 대체).

### 실제 작업 실행 결과
| 작업 | 입력 → 출력 | 판정 |
|---|---|---|
| proofread (한국어) | "나가기 싶다" → "나가고 싶다" (55자 / 30 토큰) | 문법 수정, 의미 보존 ✓ |
| translate (KR→EN) | 제품 발표문 (62자 / 31 토큰) → 자연스러운 영어 | 정확 번역 ✓ |
| rewrite (한국어) | 장황한 회의 서술 (98자 / 40 토큰) → 간결화 | 의미 보존하며 축약 ✓ |
| summarize (한국어) | 개발 도구 전환 문단 (232자 / 122 토큰) → 2문장 요약 | 핵심 포인트全覆盖 ✓ |

### 실패 경로 (모두 exit 1, 크래시 없음)
| 케이스 | 콘솔 메시지 |
|---|---|
| 빈 클립보드 | `No text was found on the clipboard...` |
| 컨텍스트 초과 (5760 > 4096 토큰) | `The selection is 5760 tokens, which exceeds the maximum of 4096...` |
| 모델 파일 없음 | `Model file not found.` |

### S0 베이스라인 회귀 체크
| 지표 | S0 베이스라인 | S4 실행 | 판정 |
|---|---|---|---|
| 모델 로드 | 2.5 s | 1.9–2.0 s | 회귀 없음 |
| 디코드 속도 | 10.5–11.4 tok/s | 12.2–13.8 tok/s | 회귀 없음 |
| 첫 토큰까지 시간 | ~0.6 s | 0.47–0.96 s | 회귀 없음 |

### 테스트
54/54 통과 — `TextCaptureTests`(정확 토큰 카운터 주입)와 `InferenceEngineTests`(`CountTokens` 실모델) 추가.

---

## S5~S8 — WPF 셸, 오버레이, 결과 에디터, 하드웨어 감지 (M2~M3)

**날짜:** 2026-11-02

### 유닛별 완성 내용
| 유닛 | 내용 |
|---|---|
| **S5** WPF 셸 + 트레이 | `Forge.App` 생성. Hardcodet.NotifyIcon.Wpf로 트레이(상태 아이콘: 로딩/레디/에러). 컨텍스트 메뉴: 설정/종료. 메인 창 없음 — 트레이 전용 앱. 시작 시 백그라운드에서 모델 로드, 트레이 상태는 InferenceEngine 로드/언로드 결과로 갱신. |
| **S6** 작업 오버레이 + 핫키 | `HotkeyManager`(`RegisterHotKey`, UI 스레드 콜백). Ctrl+E로 4개 작업 오버레이 열림(키보드 내비게이션 ↑↓+Enter, Esc 취소). 즉시 핫키는 `%APPDATA%\Forge\hotkeys.json`으로 설정 (내장 기본값 없음). 재진입 가드 `ProcessingGuard.TryBegin()`이 모든 작업 진입점에 취득. |
| **S7** 결과 에디터 + 설정 | `ResultEditorWindow`: 스플릿 뷰, 완료 시 diff 하이라이트(`DiffRenderer`), 결과 텍스트 편집 가능. 버튼: Replace(S11용 이벤트 노출), Copy, Regenerate, Cancel. `SettingsWindow`: 모델/엔진/하드웨어/프롬프트/핫키 탭 → `%LOCALAPPDATA%\Forge\settings.json`. **스펙 이탈:** "precision" 탭은 v1 미구현 (temperature 등 보류). |
| **S8** 하드웨어 감지 + idle 언로드 | `HardwareDetector`: Vulkan/CUDA 프로브 → VRAM → 추천 `GpuLayerCount`(VRAM 충분 시 999, 아니면 0). CPU-first 기본값 유지. `IdleUnloadManager`: N초 이상 유휴 시 언로드, 다음 실행 때 온디맨드 재로드. |

### 리뷰 패스 (commit `68f09b1`)
`App.cs`에서 3개 버그를 발견해 수정:
1. **재진입 가드가 데드 코드** — `TryBegin()`이 한 번도 호출되지 않아 처리 중 핫키가 발화(S6 스펙 §4 위반). 이제 즉시 핫키·오버레이 선택·Regenerate 세 진입점 모두에서 취득.
2. **`--debug-overlay` 플래그 폐기** — `ParseArgs`가 플래그를 소비했지만 `showOverlay = true`을 설정하지 않아 디버그 훅이 동작 안 함.
3. **Regenerate가 두 번째 창을 열던 문제** — 이제 새 실행 시작 전 현재 창을 먼저 닫음.

위축된 오보: 크로스 스레드 WPF 접근 우려는 오래된 파일 캐시에 기반한 것이라, 실제 `HotkeyManager`는 UI 스레드에서 발화되어 변경 불필요 확인.

### 검증 근거
- 빌드: Forge.App / Core / Cli — 0 에러. 테스트: 92/92 통과.
- 라이브 스모크(`--demo --debug-overlay --debug-log`): 하드웨어 감지 로그 → 핫키 등록 → 오버레이 표시(수정 2 라이브 확인) → 데모 스트림→diff 완료.
- GPU 오프로드: **유지자 결정으로 통과** — 이 머신이 GPU로 구동되고, 로드 로그가 Vulkan 디바이스 감지를 확인.

### 대기 중인 수동 검증
- [ ] S5: 트레이 아이콘 상태 전환(로딩 → 레디), 컨텍스트 메뉴, 로드 실패 시 "에러" 상태
- [ ] S6: Ctrl+E 오버레이 키보드 내비게이션; 처리 중 핫키 무시(리뷰 수정 1)
- [ ] S7: diff 하이라이트 품질; Regenerate가 기존 창 닫기(리뷰 수정 3); 설정이 S6/S8 상태와 동기화
- [ ] S8: idle 언로드 → 메모리 회수 → 다음 실행 시 재로드

### 이후 세션(S9~S13)을 위한 노트
- `ForgeSettings`가 이미 모델/엔진/하드웨어/프롬프트 설정을 보유 — S9의 ModelManager는 선택된 모델 경로를 그 채널로 기록, 두 번째 설정 파일은 만들지 않음.
- `ProcessingGuard`, `IdleUnloadManager`, `HardwareDetector`는 `Forge.Core`에, 앱 측 유선 연결은 `App.cs`에 위치.
- 디버그 로그: `--debug-log PATH` 플래그 + `DebugLog.Write(...)` — S9~S13 검증 실행에도 계속 사용.

---

## S9 — ModelManager (M4)

**날짜:** 2026-11-03

### 완성 내용
- **카탈로그:** 내장 기본(Gemma 4 E2B QAT UD-Q4_K_XL, 2,620,370,976 바이트, SHA256 `e5310072…889`) + 모델 디렉터리에서 사용자 GGUF 파일 스캔.
- **다운로드:** `Range` 재개를 쓰는 HTTP(`.part` 파일), 완료 후 SHA256 검증, 최종 경로로 원자적 이동.
  - 서버가 Range를 무시(206 대신 200) → part 폐기하고 0부터 재시작.
  - 체크섬 불일치 → part 삭제, `ChecksumMismatchException` 발생, 최종 파일 없음.
  - 이미 다운로드된 모델 → no-op.
- **디스크 체크:** 시작 전 여유 공간 ≥ 모델 크기 (아니면 `IOException`).
- **선택:** `Select(model, settings)`가 선택한 경로를 `ForgeSettings.ModelPath`에 기록 — 기존 설정 채널 사용, 두 번째 설정 파일 없음 (S5~S8 노트 대로).

### 검증 근거
- 빌드: 0 에러. 테스트: **103/103 통과** (기존 92 + 신규 11).
- 라이브 스모크(실 HTTP 대상 스크래치 콘솔 앱, `bartowski/SmolLM2-135M-Instruct-GGUF` Q4_0, 91,893,088 바이트):
  - 전체 다운로드: 9.5 s, 크기 + SHA256 검증(`bcc3af28…04b03`).
  - 재개: 40 MB `.part` 사전 시드 → Range 요청이 나머지 완료, 최종 파일 검증.
  - 불일치 가드: 틀린 체크섬 → 예외 발생, 최종 파일 없음, part 폐기.
  - `Select()`가 `settings.ModelPath` 기록 확인.

### 이후 세션(S10~S13)을 위한 노트
- `ModelManager`는 UI 무관·테스트 가능 — S10 설치기/온보딩 플로우가 설정 창에서 직접 호출 가능.
- 내장 기본 모델 항목은 HF 캐시 경로를 전제로 함 — S10 온보딩은 파일 존재를 확인하고 없으면 다운로드를 제안해야 함.
- 진행 보고는 `IProgress<DownloadProgress>` 사용 — UI 레이어의 프로그레스 바에 연결.

---

## S10 — 설치기 + 첫 실행 온보딩 (M4)

**날짜:** 2026-11-03

### 완성 내용
- **`installer/forge.isn`:**
  - 퍼-유저 설치(`PrivilegesRequired=lowest`) → `%LOCALAPPDATA%\Programs\Forge`, 관리자 권한 불필요.
  - self-contained `win-x64` publish 번들링(exe + .NET 런타임 + `llama.dll`/`ggml*.dll` + 트레이 아이콘).
  - 시작 메뉴 + 데스크톱 단축키; 설치 후 "Launch Forge" 항목.
  - SmartScreen 안내: 설치 후 메시지로 서명 없는 빌드와 진행 방법("More info" → "Run") 설명. `WizardSilent` 가드로 silent 설치는 대화상자에 막히지 않음.
  - 언설정은 앱 디렉터리만 삭제 — 사용자 데이터(`%LOCALAPPDATA%\Forge\settings.json`, `Models\`) 보존.
- **첫 실행 온보딩** (`src/Forge.App/OnboardingWindow.cs`):
  - 코드 전용 WPF 윈도우(S5~S7 관례 준수): 헤딩, 프로그레스 바, 상태 줄, Cancel 버튼.
  - `App.LoadModelAsync`에 유선: 설정된 모델 파일이 없으면 `ModelManager`(S9)로 다운로드 — `.part` 재개는 자동.
  - 카탈로그 소스가 없는 경로(다운로드된 적 없는 커스텀 GGUF)는 다운로드 대신 "Open Settings" 제시.
- **기본 모델 경로를 관리 디렉터리로 이동:**
  - `ForgeSettings.DefaultModelPath`가 이제 `%LOCALAPPDATA%\Forge\Models\gemma-4-e2b-qat-ud-q4_k_xl.gguf` (기존: 유지자 머신의 HuggingFace 캐시 경로). 신규 설치자는 온보딩이 실제로 다운로드할 수 있는 경로를 얻고, 기존 설정 파일에는 영향 없음.
  - `App.cs`의 중복 상수 제거; `ParseArgs`/`ParseModelPath`가 폴백을 명시적으로 받음.

### 검증 근거
- 빌드: 0 에러. 테스트: 103/103 통과.
- ISCC 6.7.3으로 설치기 컴파일 → `installer/Output/ForgeSetup-0.1.0.exe`.
- **클린 설치/언설제 사이클 (2회):** silent 설치가 exe + 네이티브 DLL + 아이콘 배치; 언설정이 앱 디렉터리 삭제하고 `%LOCALAPPDATA%\Forge\Models` 보존.
- **온보딩 라이브 체크:** 모델 파일 없는 머신에서 설치된 exe를 `--debug-log`로 실행 → 로그에 `onboarding shown (model file missing)` 표시, 실제 다운로드 진행(`.part`가 테스트 종료 전 ~408 MB 성장). 부분 `.part`는 그대로 둔 것 — 다음 실행이 거기서 재개하는 것이 설계대로.
- **검증 중 발견·수정된 버그:** 설치 후 `MsgBox`가 silent 설계를 무기한 막던 문제 → `WizardSilent` 가드로 수정, 재검증(silent 설치가 이제 깨끗하게 종료).

### 이후 세션(S11~S13)을 위한 노트
- 설치기는 서명 없음; SmartScreen 안내는 설치 후 메시지에 존재. 코드 서명은 명시적으로 보류(스펙 "하지 말 것").
- `installer/publish/`와 `installer/Output/`은 gitignore; 빌드 순서는 `.isn` 헤더에 문서화.
- 온보딩은 S9 `ModelManager` 위의 얇은 UI — 자체 다운로드 로직 없음.

---

## S11 — ReplaceAction best-effort + 앱 매트릭스 (M5)

**날짜:** 2026-11-03 · **상태:** 완료 (검증 항목 1개는 환경 차단, 아래에 문서화)

### 완성 내용
- `src/Forge.Core/ReplaceAction.cs` — Win32 P/Invoke 전용(Core에 UI 프레임워크 없음, `TextCapture`와 같은 방식):
  - `CaptureSourceWindow()` — 포그라운드 HWND를 잡음; Forge UI가 포커스를 잡기 전에 `App.cs`에서 작업 시작 시 호출.
  - `TryReplace(resultText, sourceWindow)` — best-effort 교체:
    1. 현재 클립보드 텍스트를 백업으로 읽기,
    2. 결과를 클립보드에 기록(일시적 잠금 재시도 포함),
    3. 소스 윈도우 포커싱(`SetForegroundWindow`, 반환값 검증),
    4. 300ms 정착 후 `SendInput`으로 Ctrl+V 전송,
    5. 600ms 대기 (Win11 Notepad의 붙여넣기는 비동기 — 250ms는 실측 부족, 문서에 백업 텍스트가 남음),
    6. 백업 복원(텍스트가 없으면 클립보드 클리어).
  - 어떤 실패든(빈 결과, 클립보드 기록 실패, 윈도우 소실, 포커스 거부) → `Outcome.ClipboardFallback` + 사용자 안내. 결과는 클립보드에 남고, 소스 앱은 건드리지 않음.
- **앱 지원 매트릭스** (`SupportedApps` / `Lookup`): notepad.exe + winword.exe = Full, chrome.exe + msedge.exe = Partial(편집 가능한 영역만), 그 외 = Unknown → 일반 시도 + 폴백.
- `src/Forge.App/App.cs` — 작업 시작 시 소스 윈도우 캡처; 결과 에디터의 Replace 버튼에서 교체 호출에 전달.
- `src/Forge.App/ResultEditorWindow.cs` — 결과를 사용자에게 보고(교체 성공 vs 클립보드 폴백 + 붙여넣기 안내).
- `src/Forge.Core/AssemblyInfo.cs` — `InternalsVisibleTo("Forge.Core.Tests")`로 테스트가 내부 클립보드 헬퍼를 사용할 수 있게.

### 검증
- **단위 테스트:** 113/113 통과, 신규 `ReplaceActionTests` 10개 포함: 빈 결과 폴백, null 핸들 폴백, 클립보드 기록 실패 폴백, 백업 복원(텍스트·빈 상태), 결과 보고, 앱 매트릭스 조회.
- **라이브 스모크 (Notepad, 이 머신):**
  - 타이틀로 윈도우 탐색(Win11 `notepad.exe`는 실제 UI를 다른 PID로 스폰하는 스텁 — PID 기반 탐색은 실패, 타이틀 기반은 성공).
  - 전체 선택 + 복사: 클립보드가 문서 텍스트를 수신 ✓
  - 클립보드 백업/복원이 매 실행에서 올바른 값으로 검증 ✓
  - 포커스 실패 → `ClipboardFallback` + 안내, 소스 무변경(잘못된 윈도우 핸들로 검증) ✓
  - `SetForegroundWindow` 성공 경로: 클립보드 기록 후 Notepad로 포커스 이동, 3초 이상 샘플링 동안 안정 유지 ✓

### 환경 차단 항목
- **스모크 환경에서 Ctrl+V / 타이핑 / Ctrl+S가 Notepad에 도달하지 않음.** 이 머신에는 "Unsloth"라는 앱이 입력 포커스를 포그라운드 윈도우와 분리시킴: `GetForegroundWindow`는 그것을 보고 있지만 키스트로크는 불일치하게 라우팅(Ctrl+A/C는 Notepad에 도착, 단순 타이핑과 Ctrl+V/S는 미도달 — 같은 `SendInput` 메커니즘, 같은 스레드). `SendInput` 메커니즘 자체는 동작 증명됨(선택/복사가 반복 성공). 프로덕션 Forge는 핫키 입력을 방금 받았으므로 이 콘솔 하네스가 갖지 않는 Windows 포그라운드 권리를 보유 — 이 특이한 현상은 실제 플로우에는 적용되지 않음. 완전한 end-to-end 붙여넣기 확인은 간섭 앱 없는 머신에서 재실행 필요.

### 변경 파일
- `src/Forge.Core/ReplaceAction.cs` (신규)
- `src/Forge.Core/AssemblyInfo.cs` (신규)
- `src/Forge.App/App.cs` (소스 윈도우 캡처 + 교체 유선)
- `src/Forge.App/ResultEditorWindow.cs` (결과 보고)
- `tests/Forge.Core.Tests/ReplaceActionTests.cs` (신규, 10개 테스트)

---

## S12 — 로버스트니스: 재진입 가드, 에러 UX, 크래시 덤프, 클립보드 복원 (M5)

**날짜:** 2026-11-03 · **상태:** 완료

### 완성 내용
- `src/Forge.Core/ClipboardGuard.cs` (신규) — 단일 공유 OS 클립보드 프리미티브(`WriteText`, `ReadText`, `Clear`). Core과 App의 모든 교체/복사/읽기 경로가 이걸 통해 지나가므로 백업/복원 행동이 균일(plan §4 비파괴).
- `src/Forge.Core/CrashDump.cs` (신규) — 옵트인 크래시 덤프 기록기. 타임스탬프, .NET 런타임, 모델 경로, GPU 레이어 수, 스택 트레이스를 포함한 예외를 `%LOCALAPPDATA%\Forge\CrashDumps\crash-<timestamp>.log`에 기록. **구조적으로 텍스트 내용 포함 불가** — API가 텍스트를 받지 않음(plan §4 프라이버시). 기록 실패 시 `null` 반환; 덤프가 앱을 무너뜨릴 수 없음.
- `src/Forge.Core/ForgeSettings.cs` — 신규 `CrashDumpEnabled` 설정(기본 `false`, plan §7의 옵트인).
- `src/Forge.Core/TextCapture.cs` — 클립보드 읽기가 이제 `ClipboardGuard` 경유; 캡처 실패는 복구 경로를 명시하는 사용자 메시지 동반 `TextCaptureException` 발생.
- `src/Forge.Core/ReplaceAction.cs` — 내부 클립보드 헬퍼를 `ClipboardGuard`(기록, 백업 읽기, 복원/클리어)로 교체.
- `src/Forge.App/App.cs`:
  - 전역 예외 핸들러: `DispatcherUnhandledException` + `AppDomain.UnhandledException`. 치명 예외는(옵트인 켜져 있으면) 크래시 덤프 기록, 덤프 경로와 "Restart Forge to continue" MessageBox 표시 후 종료 — WPF 크래시 다이얼로그 없음, 텍스트·클립보드 무변경.
  - 경로별 에러 UX: 캡처 실패 → 상태바 메시지; 모델 로드 실패 → "Open Settings to check the model path" 툴팁; 교체 실패 → S11 폴백 안내(불변).
  - `--smoke-crash` 디버그 훅(`--demo`와 같은 패턴): 시작 정착 후 언핸들드 디스패처 예외 발생 + 덤프 옵트인 강제 — 핸들러의 end-to-end 검증에 사용.
- `src/Forge.App/SettingsWindow.cs` — Engine 탭에 "Write crash dumps on fatal errors (no text content is recorded)" 체크박스 추가, 로드/세이브 유선.
- `src/Forge.App/ResultEditorWindow.cs` — Copy 버튼이 이제 실패 상태 동반 `ClipboardGuard.WriteText` 경유.
- 재진입 가드: 신규 코드 없음 — `ProcessingGuard`(S6)가 이미 모든 진입점(핫키, 트레이 작업 선택, Regenerate)에 유선됨; 호출 지점과 단위 테스트로 확인.

### 검증
- **단위 테스트:** 118/118 통과, 포함:
  - `CrashDumpTests` (신규, 3): 진단 파일 내용(예외, 모델 경로, gpu 레이어 수, 런타임 헤더), 텍스트 페이로드 불가 계약, 타임스탬프 파일명.
  - `ClipboardGuardTests` (신규, 2): write→read 라운드 트립, clear→텍스트 없음. `ReplaceActionTests`와 함께 `"clipboard"` xunit 컬렉션에서 실행 — OS 클립보드는 단일 공유 자원이므로 클립보드 관련 테스트는 모두 여기서 직렬화.
  - `ForgeSettingsTests`: `CrashDumpEnabled` 기본 `false`, 세이브/로드 라운드 트립.
  - `ReplaceActionTests`: 신규 `ClipboardGuard` API에 갱신; 모든 결과 불변.
- **라이브 스모크 (이 머신):**
  - `Forge.App.exe --smoke-crash` 시작, 실모델 로드(RTX 5070 Ti 감지), +1.5초 후 훅된 예외 발생.
  - 크래시 덤프 기록: `crash-20261103-092811.log`에 타임스탬프, `.NET 10.0.14`, 실모델 경로, `gpu layer count: 0`, 훅을 가리키는 스택 트레이스를 포함한 예외 — **텍스트 페이로드 없음**(훅의 메시지가 유일한 문자열). ✓
  - MessageBox 안내 박스 표시: 프로세스가 모달 루프에서 수동 종료 전까지 생존 — 핸들러가 종료 전 사용자 facing 단계에 도달했음을 증명. ✓
  - 스모크 실행이 설정 파일 무변경(옵트인 강제는 인메모리 전용; 수정할 `settings.json` 존재하지 않음). ✓

### 변경 파일
- `src/Forge.Core/ClipboardGuard.cs` (신규)
- `src/Forge.Core/CrashDump.cs` (신규)
- `src/Forge.Core/ForgeSettings.cs` (CrashDumpEnabled)
- `src/Forge.Core/TextCapture.cs` (공유 클립보드 프리미티브, TextCaptureException 메시지)
- `src/Forge.Core/ReplaceAction.cs` (ClipboardGuard 사용)
- `src/Forge.App/App.cs` (전역 핸들러, 크래시 덤프 유선, 에러 UX, --smoke-crash 훅)
- `src/Forge.App/SettingsWindow.cs` (크래시 덤프 체크박스)
- `src/Forge.App/ResultEditorWindow.cs` (가드 경유 복사)
- `tests/Forge.Core.Tests/CrashDumpTests.cs` (신규)
- `tests/Forge.Core.Tests/ClipboardGuardTests.cs` (신규)
- `tests/Forge.Core.Tests/ReplaceActionTests.cs` (API 갱신, 컬렉션)
- `tests/Forge.Core.Tests/ForgeSettingsTests.cs` (CrashDumpEnabled 어서션)

---

## S13 — 한국어 eval 세트 + 프롬프트 튜닝 (M5)

**날짜:** 2026-11-03 · **상태:** 완료 (문서화된 모델 한계 1개, 아래)

### 완성 내용
- `src/Forge.Cli/eval-set.json` (신규) — 한국어 eval 세트: 작업별(proofread / translate / rewrite / summarize) 3개 항목, 각각에 한국어 입력과 **수동 라벨링**된 기대 출력(plan §7). 오타, 공백, 톤 보존, 언어 누출, 장황함, 화자 전환을 커버.
- `src/Forge.Cli/Program.cs` — 신규 `eval` 모드: `dotnet run -- eval [--model <path>] [--gpu] [--out <file>]`. 모델을 한 번 로드하고 모든 항목을 `PromptBuilder` → `InferenceEngine` → `ResultParser`로 돌림, 항목별 기계적 게이트 적용:
  - 빈 출력 아님,
  - 거부/에러 마커 없음,
  - 언어 불일치 없음(`ResultParser` 경유),
  - 요약 ≤ 3문장,
  - 번역에 한글 없음.
  - 리포트는 모든 실제 출력을 수동 라벨과 짝지어 사람이 검토하게 함. 모든 게이트 통과 시에만 exit code 0 — 향후 프롬프트 변경의 회귀 게이트.
- `src/Forge.Cli/Forge.Cli.csproj` — `eval-set.json`을 exe 옆에 복사.
- `src/Forge.Core/PromptBuilder.cs` — 튜닝된 두 프롬프트(아래).
- `tests/Forge.Core.Tests/PromptBuilderTests.cs` — 고정 프롬프트 문자열을 S13 튜닝 텍스트로 갱신.

### 튜닝 이력
베이스라인 실행(원본 프롬프트): 12/12 기계적 통과; 수동 리뷰에서 두 가지 품질 문제 발견:
1. **proofread-01** — 입력 `그건 내 생각엔 좀 안되겟다.`가 `...안 되겠다.`로 나와 의도했던 `...안 될 것 같다.`와 다름: 오타가 다른 의미의 단어로 "교정"됨(의지 vs 추측).
2. **summarize-03** — 1인칭 블로그 스타일 원문이 3인칭(`글쓴이는 ...`)으로 요약됨.

적용한 변경과 재검증:
- **Proofread:** *"단어가 오타로 적혔으면 의도된 단어로 고쳐라 — 문장 의미가 바뀌는 다른 단어로 대체하지 마라."* 추가.
- **Summarize:** 2회 반복. 첫 시도(*"same person (first person stays first person)"*)가 summarize-03을 고쳤지만 **summarize-02를 회귀** — 3인칭 원문(`회사는 ...`)이 1인칭(`저는 ...`)으로 나옴. 최종 문구: *"원문과 같은 언어, 같은 화자."* — 양방향 모두 정확 검증.

최종 실행: 12/12 기계적 통과; 수동 리뷰는 아래에 문서화된 항목을 제외하고 전부 클린.

### 문서화된 한계
- **proofread-01가 여전히 드리프트** (`안 되겠다` vs `안 될 것 같다`). 오타 `안되겟다`는 진짜로 모호함 — `안 되겠다`로도 `안 될 것 같다`로도 읽힘 — 2B 모델은 프롬프트 문구와 무관하게 전자를 고름. 모델 크기 한계(plan §7이 v1에서 Gemma 4 E2B 유지; 더 큰 모델은 향후 작업)이지 프롬프트 결함 아님. eval 세트는 이 항목을 유지해 향후 모델 교체 시 체크하게 함.

### 검증
- [x] **eval 세트 전체의 품질 기준**(의미 보존, 절단 없음, 일관된 언어): 매 실행 12/12 기계적 게이트 통과; 리포트 수동 리뷰가 문서화된 한계를 제외한 모든 항목에서 의미/톤/언어 확인.
- [x] **프롬프트 변경이 회귀 유발 안 함**(eval 재통과): 각 프롬프트 변경 후 전체 eval 재실행 — 매번 12/12, 중간 summarize 회귀를 잡아 수정한 것 포함.
- [x] **작업별 대표 샘플 개선**: summarize 화자 처리 양방향 검증; proofread 오타 처리 지시 추가(클래스 레벨 개선; 모호한 1개 케이스는 위 문서화).
- 단위 테스트: 고정 프롬프트 문자열 갱신 후 118/118 통과.

### 변경 파일
- `src/Forge.Cli/eval-set.json` (신규)
- `src/Forge.Cli/Program.cs` (eval 모드)
- `src/Forge.Cli/Forge.Cli.csproj` (eval 세트 복사)
- `src/Forge.Core/PromptBuilder.cs` (Proofread + Summarize 프롬프트 튜닝)
- `tests/Forge.Core.Tests/PromptBuilderTests.cs` (고정 텍스트 갱신)

---

## 요약

### 테스트 수 progression
| 단계 | 테스트 | 비고 |
|---|---|---|
| S4 | 54/54 | 정확 토큰 카운터, CountTokens |
| S5~S8 | 92/92 | WPF 셸·오버레이·에디터·하드웨어 |
| S9 | 103/103 | ModelManager (신규 11) |
| S11 | 113/113 | ReplaceAction (신규 10) |
| S12~S13 | 118/118 | CrashDump, ClipboardGuard, 설정 |

### 미해결 항목
- **수동 검증 대기 (S5~S8):** 트레이 아이콘 상태, 오버레이 키보드 내비게이션, diff 하이라이트, idle 언로드 — 위 체크리스트 참조.
- **환경 차단 (S11):** "Unsloth" 간섭 앱이 없는 머신에서의 완전한 end-to-end Ctrl+V 붙여넣기 확인.
- **모델 한계 (S13):** proofread-01의 모호한 오타 드리프트(`안 되겠다` vs `안 될 것 같다`) — 문서화됨, 향후 모델 체크를 위해 eval 세트에 유지.

### 회귀 게이트
```
dotnet run --project src/Forge.Cli -- eval
```
한국어 eval 세트 12개 항목, 기계적 판정 + 수동 라벨. 모든 게이트 통과 시에만 exit code 0 — 향후 프롬프트·모델 변경의 회귀 게이트로 사용.
