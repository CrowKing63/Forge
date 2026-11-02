# S3 — PromptBuilder + ResultParser (순수 함수)

## 목표
작업별 시스템 프롬프트를 조립하는 `PromptBuilder`와, 모델 출력을 정리/검증하는 `ResultParser`라는 **순수 함수** 두 개를 만든다. UI·엔진 없이 xunit으로 독립 검증.

## 배경 (레포 기준)
- `src/Forge.Core/PromptBuilder.cs`, `src/Forge.Core/ResultParser.cs` 미생성.
- 근거: plan §5(프롬프트 설계 — 교정/변역/재작성/요약), §4(작업 목록).

## 결정
- `PromptBuilder.Build(task, selectedText) → string` — **Gemma 4 역할 마커 포함 전체 프롬프트**를 직접 조립. (S0 확인: LLamaSharp가 채팅 템플릿을 실행하지 못하므로 `(system, user)`만 반환하는 구조 불가.)
- 포맷 (S0 실측 검증): `<|turn>system\n{작업별 지시문}\n<turn|>\n<|turn>user\n{선택 텍스트}\n<turn|>\n<|turn>model\n`
- `ResultParser.Parse(raw) → Result` — 앞뒤 공백·코드펜스 제거, 빈/에러/언어 누출 감지.
- 순수 함수: I/O 없음, 결정적. 테스트로만 검증.

## 작업
1. `PromptBuilder`: 4작업(교정/변역KR→EN/재작성/요약) 시스템 프롬프트 매핑 + **역할 마커 조립** (위 포맷).
2. `ResultParser`: 출력 정제(trim, 코드펜스 제거), 빈 결과·오류 플래그, (선택) 언어 일치 체크.
3. xunit: 각 작업별 입력→기대 프롬프트 문자열(마커 포함), ResultParser 정제·엣지 케이스(빈 텍스트,超长, 영어 누출).

## 하지 말 것
- 온도/샘플링 파라미터 노출 — S1에 위임.
- 프롬프트 품질 튜닝 — S13.
- diff 계산 — S7(UI).

## 검증
- [ ] xunit 전부 통과: 4작업 조립, ResultParser 정제·엣지 케이스.
- [ ] 결정적: 같은 입력 → 같은 출력.

## 산출물
- `PromptBuilder.cs`, `ResultParser.cs` + xunit 테스트
