# S10 — 설치기 + 첫 실행 온보딩 (M4)

## 목표
**Inno Setup 설치기**를 만들고, 첫 실행 **온보딩**(모델 다운로드 안내)을 제공한다.

## 배경 (레포 기준)
- `installer/` 미생성. S9(모델 다운로드)·S5~S7(앱) 전제.
- 근거: plan §6(배포 — Inno Setup, exe+llama.dll 번들, GGUF 첫 실행 다운로드), §6(SmartScreen).

## 결정
- Inno Setup 6 (`.isn`): `Forge.App` exe + LLamaSharp 네이티브 dll(CPU + 선택 CUDA/Vulkan) 번들.
- GGUF는 설치 시가 아닌 **첫 실행에 다운로드**(plan §6) — 온보딩에서 안내.
- SmartScreen: 서명 없으면 "고급 옵션" 클릭 안내(plan §6).

## 작업
1. `installer/forge.isn` 작성: 파일 번들, 시작 메뉴, 트레이 등록.
2. 첫 실행 온보딩: 모델 다운로드 진행 바 + 재개(S9 연동).
3. SmartScreen 안내 문구.
4. 설치/제거(유지 관리) 검증.

## 하지 말 것
- MSIX/스토어 배포 — 향후(Inno Setup 우선, plan §6).
- 자동 업데이트 채널 — 향후.
- 코드 서명 인증서 — 향후(현재 서명 없음 전제).

## 검증
- [ ] 설치기 빌드 → 클린 VM/폴더에 설치 성공.
- [ ] 첫 실행 온보딩에서 모델 다운로드 진행·재개 확인.
- [ ] 제거(유지 관리) 정상.

## 산출물
- `installer/forge.isn` + 온보딩 (M4 완료)
