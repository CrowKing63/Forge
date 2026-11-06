# S16 — 번역기 리팩토링 (컨셉 전환)

**날짜:** 2026-11-06
**전제:** S0~S13 완료. 본 유닛은 앱 컨셉을 "비영어권 사용자를 위한 로컬 KR→EN 번역기"로 축소한다.

## 목표

Forge의 기능 경로를 **다이렉트 영어 번역 1개**로 줄인다:

```
Ctrl+E → 선택 텍스트 캡처 → 로컬 모델 번역 → 원자리에 교체
         (+해머 인디케이터 + clang 사운드)
```

작업 오버레이(TaskOverlayWindow), 결과 에디터(ResultEditorWindow), proofread/rewrite/summarize 경로, `--demo` 경로를 전부 제거한다.

## 회귀 방지를 위한 격리 원칙 (핵심 요구)

해머 오버레이·사운드 효과·텍스트 교체 로직은 **각각 독립 모듈로 격리**하여, 이 리팩토링 이후의 어떤 수정도 그 내부에 닿을 수 없게 한다. App.cs는 각 모듈에 대한 1회 콜만 남기고 해당 로직을 소유하지 않는다.

| 모듈 | 파일 | 소유 내용 | 손 대지 않는 것 |
|---|---|---|---|
| 해머 인디케이터 | `HammerIndicator.cs` (신규) | show/complete/close lifecycle, rect 해석·스크린 중앙 폴백, try/catch 격리 | `HammerOverlayWindow.cs` (바이트 단위 무변경 — HammerProbe가 그대로 검증) |
| 사운드 | `ForgeSounds.cs` (신규) | `PlayClang()` 단독. best-effort, 절대 throw 안 함 | `Assets/clang.wav`, csproj Content 항목 |
| 텍스트 교체 | `SelectionReplacer.cs` (신규) | 캐스케이드 전 체: EM_REPLACESEL → UIA splice → 클립보드 폴백, outcome 기록 반환 | `UiaProbe.ReplaceSelectionText/ReplaceSelectionViaUia`, `ClipboardGuard`, `ReplaceAction.CopySelection/CaptureSourceWindow` 원시 함수 |

## 삭제 목록

| 대상 | 이유 |
|---|---|
| `src/Forge.App/TaskOverlayWindow.cs` | 오버레이 제거 |
| `src/Forge.App/ResultEditorWindow.cs` | 결과 에디터 제거 |
| `src/Forge.Core/DiffRenderer.cs` + `tests/.../DiffRendererTests.cs` | 결과 에디터 전용 |
| `verify.bat` | 존재 이유가 "오버레이를 시작 시 보여주기"였음. use.bat이 실플로우+디버그 로그를 커버 |
| `ForgeTask` enum | 단일 작업 → 파라미터 자체 제거 |
| `PromptBuilder`의 4개 프롬프트 + family 분기 | 번역 프롬프트 1개로 축소 (S15에서 Hy-MT2·Gemma4 둘 다 이 인스트럭션으로 벤치마크 검증됨) |
| `ResultParser`의 task 매칭 | "출력이 한국어면 경고" 단일 체크로 축소 |
| `ForgeSettings.Prompts` | **데드 컨픽** — 파이프라인이 읽지 않고 SettingsWindow에서만 편집됨 |
| `ReplaceAction.TryReplace/SendCtrlV/TypeViaSendInput` | 이번 리팩토링으로 고아가 된 Ctrl+V 경로 (RunTaskAsync 전용) |
| CLI의 proofread/rewrite/summarize | 기능 경로 제거. eval은 역사적 `eval-set.json`을 유지하되 translate 항목만 실행 (나머지 스킵 로그) |
| `--demo` 플래그 / `RunDemoAsync` | 결과 에디터 의존 |
| `--debug-overlay` 플래그 | 오버레이 의존 |

## 변경 목록

### Forge.Core
- `PromptBuilder.Build(string selectedText)` — 단일 (system, user) 반환. Hy-MT2 README 인스트럭션 유지(번역 경로).
- `ResultParser.Parse(string raw)` — 토큰/마크업 클리닝 + "출력이 한국어면 경고" 체크.
- `ForgeSettings` — `Prompts` 속성 제거 (저장/로드 라인 포함).
- `ReplaceAction` — `TryReplace`, `SendCtrlV`, `TypeViaSendInput` 제거. `CopySelection`, `CaptureSourceWindow`, 지원 매트릭스(`AppSupportEntry/Lookup`)는 유지(기존 데드 웨이트 — 이번 변경과 무관, 언급만).

### Forge.App
- `HammerIndicator.cs` (신규) — 해머 lifecycle facade. `Show(AutomationElement?) → handle`, `Complete()`, `Close()`. 절대 throw 안 함.
- `ForgeSounds.cs` (신규) — `PlayClang()`.
- `SelectionReplacer.cs` (신규) — `Replace(sourceWindow, sourceElement, selectedText, replacement) → ReplaceResult(outcome, message, diagnostics)`.
- `App.cs`:
  - `_overlay`, `ShowOverlay`, `OnTaskSelected`, `RunTaskAsync`, `RunDemoAsync`, `_immediateHotkeys` 제거.
  - `PopupHotkeyId` → `TranslateHotkeyId`. 즉시 핫키 등록 제거 — 번역 핫키 1개만.
  - `RunDirectAsync`: 캡처 → `PromptBuilder.Build` → `GenerateAsync` → `ResultParser.Parse` → `SelectionReplacer.Replace` → `HammerIndicator.Complete` + `ForgeSounds.PlayClang`. 교체/사운드/해머 로직을 App.cs가 소유하지 않음.
- `HotkeyConfig` — `ImmediateHotkeys` 제거, `Translate` 바인딩 1개. JSON 키 `translate` (레거시 `popup` 키 폴백 읽기).
- `SettingsWindow`:
  - Prompts 섹션 + `_promptBoxes` 제거. 사이드바: Model / Engine / Hardware / Hotkeys (4항목).
  - Hotkeys 섹션 → "Translate" 단일 필드 (`_translateBox`).

### tools
- `SettingsProbe`: nav 개수 5→4, "Prompts" 기대 제거, `_popupBox` → `_translateBox`.
- `HammerProbe`: **무변경** (HammerOverlayWindow API 불변).

### tests
- `PromptBuilderTests`, `ResultParserTests`, `ForgeSettingsTests`, `ReplaceActionTests` — 새 시그니처에 맞게 축소.

## 검증 게이트

1. `dotnet build` 전 프로젝트 오류 0.
2. `dotnet test` 전부 통과 (개수 변동: DiffRendererTests 제거분 반영).
3. CLI 스모크: 모델 없이 `dotnet run --project src/Forge.Cli` → "Model file not found" 에러 경로 정상 (exit 1, 크래시 없음).
4. HammerProbe: `tools/HammerProbe` 통과 (해머 창 무변경 확인).
5. 물리 검증(유저): use.bat 실행 → 트레이 ready → Ctrl+E → 선택 텍스트가 영어로 교체되고 해머·clang 동작.

## 범위 밖 (향후)

- 지원 매트릭스(`AppSupportEntry/Lookup`) 정리 — 데드 웨이트, 원할 때 별도 작업.
- eval-set.json의 비번역 항목 실제 삭제 — 역사적 벤치마크 기록이라 유지.
