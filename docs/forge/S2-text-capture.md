# S2 — TextCapture (클립보드 주 + 입력 길이 가드)

## 목표
활성 윈도우의 **선택 텍스트를 클립보드(주)로** 읽고, 최대 입력 토큰 제한을 거치는 `TextCapture`를 만든다.

## 배경 (레포 기준)
- `src/Forge.Core/TextCapture.cs` 미생성.
- 근거: plan §5(TextCapture — 선택지 클립보드 주/UIA 보조), §7(IME·특수 앱).
- TextRecast 패턴 참고(plan §9).

## 결정
- **primary = 현재 선택지의 클립보드** (Ctrl+C 결과 읽기). UIA range 읽기는 보조/폴백만.
- 최대 입력 토큰 제한: `ctxSize` 대비 안전 마진(예: 4096 token) 초과 시 거부 + 명확한 에러.
- 토큰 수는 간단한 추정으로 구현, 정밀 tokenizer 카운트는 S4에서 엔진으로 교체.

## 작업
1. `src/Forge.Core/TextCapture.cs`: 클립보드 텍스트 읽기(빈·이미지일 때 처리 포함).
2. 최대 입력 길이 가드 + 에러 타입.
3. (선택) UIA 폴백 스텁 — 실제 UIA 구현은 S4/S12에서 완성.

## 하지 말 것
- SendKeys/교체 로직 — S11.
- 핫키 등록 — S6.
- 정밀 토큰 카운트 최적화 — S4에서 엔진 tokenizer로 교체.

## 검증
- [x] Notepad에서 텍스트 선택 → Ctrl+C → `TextCapture`가 정확한 선택 문자열 반환. (실 클립보드 probe: 알려진 문자열을 P/Invoke로 정확히 읽음)
- [x] 클립보드 비어있을 때 / 이미지일 때 명확한 에러. (`EmptyClipboard` — 빈·비텍스트 페이로드 모두 null 경로로 동일 처리)
- [x] 최대 길이 초과 입력 → 거부 + 에러 메시지. (xunit: `InputTooLong`, 경계 포함 15개 테스트 통과)

## 산출물
- `src/Forge.Core/TextCapture.cs` + 테스트(가드·에러 경로)
