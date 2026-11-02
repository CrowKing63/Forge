# S11 — ReplaceAction best-effort + 앱별 매트릭스 (M5)

## 목표
결과를 원본 앱에 **best-effort로 교체**하고, 실패 시 **클립보드 자동 폴백**을 구현한다. 앱별 지원 매트릭스를 세운다.

## 배경 (레포 기준)
- `src/Forge.Core/ReplaceAction.cs` 미생성. S4 코어·S7(결과 에디터 교체 버튼) 전제.
- 근거: plan §5(ReplaceAction — best-effort), §7(텍스트 캡처/교체 실패, IME).

## 결정
- **best-effort**: UI Automation SetFocus + SendKeys로 선택 범위 교체. 성공 보장 없음(plan §5).
- 실패 시 **클립보드 자동 폴백**: 결과를 클립보드로 복사 + "붙여넣기" 안내.
- 앱별 지원 매트릭스: Notepad/Word/브라우저 등 핵심 앱 우선, 그 외는 폴백.
- 교체 전 **클립보드 백업**, 교체 후 복원(plan §4 비파괴적).

## 작업
1. `ReplaceAction`: 활성 컨트롤 포커스 + 선택 범위 교체(SendKeys/UIA).
2. 클립보드 백업/복원 로직.
3. 실패 감지 → 클립보드 폴백 + 안내.
4. 앱별 지원 매트릭스 정의(핵심 앱 우선).

## 하지 말 것
- 모든 앱 보장을 약속 — 안 함(best-effort, plan §5).
- IME 조합 완벽 처리 — 핵심 앱 중심, 그 외 폴백.
- 원본 손상 — 교체 전 백업 필수.

## 검증
- [ ] Notepad/Word에서 선택 → 교체 성공(표시 확인).
- [ ] 지원 안 되는 앱에서 실패 → 클립보드에 결과 + 안내(원본 무사).
- [ ] 교체 전후 클립보드 백업/복원 정상.

## 산출물
- `ReplaceAction.cs` + 앱별 매트릭스 (M5)
