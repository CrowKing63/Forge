# S4 — 코어 콘솔 E2E 파이프라인 (M1 완료)

## 목표
S1~S3을 **콘솔 앱 하나로 이어** "선택 텍스트 → 작업 → 결과"를 끝까지 돌리는 엔드투엔드 파이프라인을 만든다. M1의 완료 지점.

## 배경 (레포 기준)
- `src/Forge.Cli/` 존재(S0 스파이크). S1(InferenceEngine), S2(TextCapture), S3(PromptBuilder/ResultParser) 완성 전제.
- 근거: plan §8(M1 — TextCapture + InferenceEngine + 결과 파싱, CLI/콘솔).

## 결정
- 콘솔 워크플로: (a) 클립보드에서 선택 텍스트 읽기(S2), (b) 작업 입력(교정/변역/재작성/요약), (c) PromptBuilder(S3) → InferenceEngine(S1) 스트리밍 → ResultParser(S3), (d) 결과 표시.
- 토큰 카운트를 S1의 tokenizer로 정밀화(S2의 추정 교체).
- 이 지점에서 **GUI 없음** — 콘솔이 검증 매체.

## 작업
1. `Forge.Cli`에 E2E 루프 연결 (S1·S2·S3 조합).
2. 정밀 토큰 카운트(S2 가드) — 엔진 tokenizer 사용.
3. 실패 경로: 캡처 실패, ctx 초과, 빈 결과 → 명확한 콘솔 에러.
4. 4작업 각각 실제 한국어/영문 샘플로 돌림.

## 하지 말 것
- GUI(트레이/오버레이/에디터) — S5~S7.
- 교체(SendKeys) — S11.
- 하드웨어 자동 감지 — S8.
- 설치기 — S10.

## 검증
- [ ] 콘솔에서 Notepad 선택 → Ctrl+C → 작업 지정 → 결과 출력, 4작업 전부 동작.
- [ ] 캡처 실패/ctx 초과/빈 결과 시 명확한 에러(크래시 없음).
- [ ] S0 수치 회귀 없음.

## 산출물
- 동작하는 `Forge.Cli` E2E (M1 완료)
- 4작업 샘플 실행 로그
