# Forge — 세션 단위 작업 지시서 (Index)

이 폴더는 `../local-llm-text-app-plan.md`를 **세션 단위(독립 실행·검증 가능한) 작업 지시서**로 분해한 것이다. 각 지시서는 하나의 세션에서 시작 → 완료 → 검증까지 자족적으로 끝나야 한다. 코드 없이 지시만이다.

## 언어 컨벤션 (중요)
범국가 지원 레포 — **README · 코드 주석 · 기타 문서 · UI 문자열은 기본적으로 영어**. (기존 기획 문서는 유지자 작업 노트로 한국어 유지; 신규·외부 문서만 영어 기본.)

## 레포 상태 (2026-11-03 기준)
**S0~S12 완료**, S13 미착수.
- 소스: `src/Forge.Cli`, `src/Forge.Core`, `src/Forge.App`(WPF), `tests/Forge.Core.Tests` — 빌드 오류 0, 테스트 118/118.
- 실행 로그: `S4-e2e-run-log.md`(M1), `S5-S8-run-log.md`(M2~M3 + 리뷰 수정), `S9-run-log.md`, `S10-run-log.md`, `S11-run-log.md`, `S12-run-log.md`.
- S13 착수 전 필독: `S5-S8-run-log.md`의 "Notes for later sessions" 섹션.

## 권장 프로젝트 구조 (생성 예정)
```
Forge/
├─ src/
│  ├─ Forge.Cli/            # M0·M1용 콘솔 하네스 (UI 이전 검증)
│  ├─ Forge.Core/           # 클래스 라이브러리: InferenceEngine, TextCapture, PromptBuilder, ResultParser, ModelManager, HardwareDetector, ReplaceAction
│  └─ Forge.App/            # WPF .NET 10 앱 (M2~): Tray, Overlay, Result Editor, Settings
├─ tests/Forge.Core.Tests/   # xunit (순수 함수·서비스 검증)
├─ installer/               # Inno Setup 스크립트 (.isn)
└─ docs/
   ├─ local-llm-text-app-plan.md
   └─ forge/                # 이 지시서 폴더
```
런타임 모델 저장: `%LOCALAPPDATA%\Forge\Models\*.gguf`

## 공유 범위 결정 (1차 vs 향후)
**1차(v1) 포함:**
- CPU 구동 1순위 (GPU는 선택적 가속)
- 기본 모델 Gemma 4 E2B UD-Q4_K_XL + **사용자 선택 로컬 GGUF**(다운로드·전환)
- 작업: 교정 / 변역(KR→EN) / 재작성 / 요약
- WPF UI: 트레이 + 작업 오버레이 + 결과 에디터 + 설정
- 텍스트 캡처: 선택지 클립보드(주) / UIA(보조), 최대 입력 토큰 제한
- 교체: best-effort (실패 시 클립보드 폴백)
- Inno Setup 설치기, idle 언로드 타이머

**향후(v1 제외):**
- MTP 스펙ulative decoding (Gemma4용 아직 미지원)
- Tauri/크로스 플랫폼 (WPF로 확정됨)
- Ollama / llama-server 사이드카 (인-프로세스로 확정됨)
- DX12 백엔드, 멀티모달(이미지·오디오) 입력
- Notepad/Word/브라우저 외의 앱별 교체 매트릭스 확장
- 텔레메트리

## 의존도 테이블
| 유닛 | 의존 | 비고 |
|------|------|------|
| S0 | — | **게이트** — 나머지 전부 전제 |
| S1 | S0 | |
| S2 | — (S4에서 통합) | 독립 검증 가능 |
| S3 | — | 순수 함수, 독립 |
| S4 | S1,S2,S3 | M1 완료 지점 |
| S5 | S4 | WPF 셸 |
| S6 | S5 | 오버레이·핫키 |
| S7 | S5, S4 | 결과 에디터 |
| S8 | S1 | 하드웨어·라이프사이클 |
| S9 | S1 | 모델 매니저 |
| S10 | S9 | 설치기 |
| S11 | S4 | 교체 |
| S12 | S5, S7 | 로버스트니스 |
| S13 | S4 | 프롬프트 튜닝 |

## 유닛 목록
- **S0** 게이트: 런타임 PoC (CPU + 실제 UD-Q4_K_XL 로드·추론) — 전 유닛의 전제
- **S1** InferenceEngine 서비스 (로드/언로드/스트리밍/설정)
- **S2** TextCapture (클립보드 주 + 입력 길이 가드)
- **S3** PromptBuilder + ResultParser (순수 함수)
- **S4** 코어 콘솔 E2E 파이프라인 (M1 완료)
- **S5** WPF 셸 + 트레이 아이콘 + 모델 상태
- **S6** 작업 선택 오버레이 + 핫키
- **S7** 결과 에디터 UI (diff·버튼·편집)
- **S8** 하드웨어 감지 + GpuLayerCount + idle 언로드 (M3)
- **S9** ModelManager (다운로드·재개·SHA256·카탈로그) (M4)
- **S10** 설치기 + 첫 실행 온보딩 (M4)
- **S11** ReplaceAction best-effort + 앱별 매트릭스 (M5)
- **S12** 로버스트니스: 재진입·에러·crash dump·클립보드 복원 (M5)
- **S13** 프롬프트 튜닝 + 한국어 eval 세트 (M5)

## 게이트 규칙
S0가 통과 기준을 만족해야만 S1~S13를 시작한다. S0 실패 시(예: CPU 토큰 속도 미달, 파일 로드 실패)는 모델·런타임 전제를 재평가한 뒤 유닛을 갱신한다. 이후 모든 유닛은 "S0 통과"를 전제로 한다.
