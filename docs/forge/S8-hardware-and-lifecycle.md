# S8 — 하드웨어 감지 + GpuLayerCount + idle 언로드 (M3)

## 목표
**GPU를 자동 감지**해 `GpuLayerCount`를 정하고, **idle 시 모델 언로드** 타이머를 구현한다. CPU 1순위 기본 유지.

## 배경 (레포 기준)
- `src/Forge.Core/HardwareDetector.cs` 미생성. S1(InferenceEngine) 전제.
- 근거: plan §5(HardwareDetector), §3(GpuLayerCount, Vulkan/CUDA 백엔드), §8(M3).

## 결정
- CPU 기본(`GpuLayerCount=0`). GPU 감지 시 오프로드(999 또는 VRAM 기반 층수).
- 감지: LLamaSharp CUDA·Vulkan 초기화 시 탐지(plan §5).
- idle 언로드: 마지막 추론 후 N초(설정 가능) 경과 시 `Unload()`. 재호출 시 재로드.
- CPU 1순위 전제 유지 — GPU 실패 시 CPU로 폴백.

## 작업
1. `HardwareDetector`: Vulkan/CUDA 존재·VRAM 탐지 → 권장 `GpuLayerCount`.
2. InferenceEngine에 `gpuLayerCount` 주입(S1 설정).
3. idle 언로드 타이머(설정값), 재호출 시 on-demand 재로드.
4. GPU 로드 실패 → CPU 폴백 + 상태 표시.

## 하지 말 것
- MTP 스펙ulative decoding — 향후.
- DX12 백엔드 — 향후.
- 모델 다운로드 — S9.

## 검증
- [ ] GPU 있는 기기: 감지 → 오프로드 층수 설정, 속도 향상 확인(S0 대비).
- [ ] CPU-only 기기: `GpuLayerCount=0`, 정상 구동.
- [ ] idle N초 후 언로드(메모리 회수), 재호출 시 재로드.

## 산출물
- `HardwareDetector.cs` + idle 언로드 (M3)
