# S20 — 온보딩 위자드 + 자동 시작 (Auto-start)

## 목표
- 첫 실행 **4장 온보딩 위자드** (ForgeTheme — 해머 오버레이와 같은 재질, 코드온리 WPF).
- 모델 다운로드 페이지: **설치 중에도 다음 섹션으로 진행 가능**(비동기 다운로드).
- 핫키 설정 페이지 (선택적 — 안 하면 기본 Ctrl+E 유지).
- 로그인 시 자동 실행: **신규 기능** + 설정창 General 탭.
- 기존 미들플로우 `OnboardingWindow` 테마 통일(디자인 일치, 동작 불변).

## 배경 (레포 기준)
- S10의 `OnboardingWindow`는 plain WPF light 스타일(기본 버튼, DimGray 텍스트) — ForgeTheme와 불일치.
- S17부터 시작 시 모델 로드가 없음 → 첫 번역에서 온디맨드 로드. 위자드가 첫 실행 세팅의 자연스러운 슬롯.
- `HotkeyManager.UnregisterAll()` 존재 → 위자드 종료 후 핫키 재등록 가능.
- S9 다운로드 resume(`.part` + HTTP Range)는 그대로 재사용.

## 결정
1. **트리거**: `ForgeSettings.OnboardingCompleted == false`(신규 필드, 기본 false)이면 시작 시 위자드 표시(트레이·핫키 세팅 이후). 닫기(X 또는 Finish) = 완료 → 플래그 true + 저장. 기존 settings.json에 키 없음 → false로 역직렬화되어 위자드 한 번 표시(릴리스 전이라 무방).
2. **미들플로우 온보딩은 fallback으로 유지**: 위자드에서 다운로드를 건너뛰고 모델 없이 번역하면 기존 `OnboardingWindow`가 뜸(동작 불변, 테마만 통일).
3. **2장 비동기 다운로드**: App이 다운로드 태스크를 소유(`Task?` + 진행률 이벤트) — 창이 닫여도 다운로드는 백그라운드에서 계속. 위자드 페이지는 진행률 구독만. **Next 항상 활성**. Cancel → `.part` 유지(S9 resume).
4. **3장 핫키**: `HotkeyField` 재사용(WH_KEYBOARD_LL 캡처). 안 건드리면 기본 Ctrl+E. 변경 시 HotkeyConfig 저장 + `UnregisterAll()` + 재등록.
5. **자동 시작**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`의 "Forge" 값 → `Environment.ProcessPath`(dev·publish 모두, 관리자 불필요). 신규 `AutoStartManager`(IsEnabled/SetEnabled). `ForgeSettings.AutoStart` bool이 레지스트리를 미러.
6. **설정창 General 탭** (5번째 nav row): Auto-start 체크박스 + "Show onboarding again" 버튼(위자드 재오픈만 — 플래그 조작 없음, 위자드는 멱등).
7. **OnboardingWindow 테마 통일**: `Resources = ForgeTheme.Build()`, BackgroundBrush/CardBrush/MutedBrush, Download=AccentButton, Cancel=보통 Button. 동의 → 다운로드 → resume 로직은 그대로.

## 작업
1. `ForgeSettings`: + `OnboardingCompleted` (bool), + `AutoStart` (bool).
2. `src/Forge.App/AutoStartManager.cs` (신규): Run 키 읽기/쓰기/삭제.
3. `src/Forge.App/OnboardingWizardWindow.cs` (신규): 4장, Back/Next, "n of 4" 표시, ForgeTheme 카드.
4. `App.cs`: OnStartup 위자드 트리거, 다운로드 소유(진행률 이벤트), 핫키 재등록.
5. `SettingsWindow.cs`: General 섹션(Auto-start + 온보딩 재실행).
6. `OnboardingWindow.cs`: 테마 통일.
7. `tools/WizardProbe` (headless: 창 구성 + 페이지 이동 + 텍스트 어서션 — SettingsProbe 패턴).

## 하지 말 것
- 미들플로우 온보딩 **동작** 변경 금지(동의/재개 로직) — 테마만.
- 자동 시작에 관리자 권한·Startup 폴더·Task Scheduler 도입 금지.
- `OnboardingWindow` 삭제 금지(fallback).
- 신규 의존성 추가 금지.

## 검증
- [ ] 4 프로젝트 전체 빌드 (Forge.Core / Forge.App / Forge.Cli / tests)
- [ ] `dotnet test` (InferenceEngine 실패는 기존 환경 의존 — 회귀 아님)
- [ ] CLI smoke: 존재하지 않는 `--model` → not-found + exit 1
- [ ] HammerProbe 회귀 게이트
- [ ] WizardProbe (headless 레이아웃/텍스트 어서션)
- [ ] use.bat 물리 검증: 위자드 전체 플로우, 설치 중 진행률 + Next, 핫키 변경 후 실제 동작, 자동 시작 Run 키 생성/삭제
- [ ] 사용자 물리 검증 후에만 커밋

## 산출물
- S20: 온보딩 위자드 + 자동 시작 + General 탭 + 테마 통일된 OnboardingWindow
