# S9 — ModelManager (다운로드·재개·SHA256·카탈로그) (M4)

## 목표
GGUF를 **다운로드(재개+SHA256 검증)**하고, **다중 모델 카탈로그**(사용자 선택 로컬 GGUF)와 버전을 관리한다.

## 배경 (레포 기준)
- `src/Forge.Core/ModelManager.cs` 미생성. S1 전제(로드 대상 경로 제공).
- 근거: plan §5(ModelManager — 다중 카탈로그), §6(배포 — 재개+SHA256), §1(모델 사용자 선택).

## 결정
- 저장: `%LOCALAPPDATA%\Forge\Models\*.gguf`.
- 카탈로그: 기본 Gemma 4 E2B UD-Q4_K_XL + 추가 로컬 GGUF 목록(사용자 선택).
- 다운로드: 재개(resume) 지원, SHA256 체크섬 검증(plan §6).
- 버전 관리: 파일명·메타(모델 ID, 퀀트, 크기).

## 작업
1. `ModelManager`: 카탈로그 정의(기본 + 추가), 모델 메타 구조.
2. 다운로드: HTTP 재개 + SHA256 검증 + 디스크 공간 체크.
3. 선택된 모델 경로 → InferenceEngine(S1) 로드 대상 전달.
4. (UI 연동은 S7 설정 화면 — 여기서는 Core API만).

## 하지 말 것
- 설치기/온보딩 UI — S10.
- 모델 품질 eval — S13.
- HuggingFace 토큰/게이트된 모델 — 향후(공개 GGUF만 v1).

## 검증
- [ ] 기본 모델 다운로드 → SHA256 일치 확인, `%LOCALAPPDATA%\Forge\Models\`에 저장.
- [ ] 중단 후 재개 정상.
- [ ] 카탈로그에서 다른 GGUF 선택 → S1이 해당 경로 로드.
- [ ] 디스크 부족/체크섬 불일치 시 명확한 에러.

## 산출물
- `ModelManager.cs` (M4)
