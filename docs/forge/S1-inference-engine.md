# S1 — InferenceEngine 서비스

## 목표
LLamaSharp를 감싸 **로드/언로드/스트리밍/설정**을 제공하는 재사용 `InferenceEngine` 클래스를 만든다. UI 이전(콘솔)에서 독립 검증된다.

## 배경 (레포 기준)
- `src/Forge.Core/InferenceEngine.cs` 미생성.
- 전제: **S0 통과**(CPU 구동·파일 로드 확인됨).
- 근거: plan §5(InferenceEngine), §3(Option B, GpuLayerCount), §7(크래시 대응).

## 결정
- `Forge.Core` 클래스 라이브러리. **타겟 .NET: net10.0 확정** — S0에서 net8/net10 모두 검증(결과 동일, decode 11.4 tok/s). 현재 LTS로 2028-11까지 지원; .NET 8은 2026-11-10 EOL이라 출시 기반 부적합.
- API: `LoadModel(path)`, `Unload()`, `Complete(prompt) → IAsyncEnumerable<string>` (토큰 스트리밍), 설정(`ctxSize`, `gpuLayerCount`).
- 로드/언로드는 on-demand; idle 언로드 타이머는 S8.
- 네이티브 하드크래시는 구제 불가 전제(plan §7) — 이 유닛은 .NET 측 예외만 try/catch, 하드크래시는 S12의 crash dump 영역.

## S0에서 확인된 제약 (반영 필수)
- **LLamaSharp 0.30.0은 Gemma 4 채팅 템플릿을 실행하지 못함** → `InferenceEngine`은 템플릿 적용을 책임지지 않고, **프롬프트 조립(S3)이 Gemma 4 역할 마커를 수동 구성**함을 전제로 설계. `ApplyTemplate`에 의존하는 API는 금지.
- **네이티브 DLL 패키징:** `LLamaSharp.Backend.Cpu`가 `llama.dll`/`ggml*.dll`을 출력 폴더에 자동 카피하지 않음 → csproj에서 `<None Include="...">` + `CopyToOutputDirectory=PreserveNewest` 명시.
- **API 시그니처 (0.30.0 실측):** `LLama.LLamaWeights.LoadFromFile(ModelParams)`, `new StatelessExecutor(weights, params, logger) { SystemMessage=null, ApplyTemplate=false }`, `executor.InferAsync(prompt, InferenceParams, ct) → IAsyncEnumerable<string>`. `InteractiveExecutor.Execute`는 0.30.0에 없음.

## 작업
1. `src/Forge.Core/` 프로젝트 + `tests/Forge.Core.Tests/`(xunit) 생성.
2. `InferenceEngine`: LLamaSharp `LlamaWeights.LoadFromFile` / `CreateContext` / 토큰 디코딩 감싸기.
3. 스트리밍: 토큰 단위 `IAsyncEnumerable<string>` 노출.
4. 설정: `ctxSize`(기본 8192), `gpuLayerCount`(기본 0=CPU).
5. `Unload()`으로 리소스 해제.

## 하지 말 것
- GPU 자동 감지 — S8.
- idle 언로드 타이머 — S8.
- 모델 다운로드/카탈로그 — S9.
- 프롬프트 조립 — S3.

## 검증
- [ ] 콘솔 하네스: `LoadModel` → `Complete(prompt)` 스트리밍 출력 → `Unload()` 정상 종료.
- [ ] xunit: 빈 prompt/오류 경로, unload 후 재로드 가능.
- [ ] 로드→첫 토큰 지연이 S0 수치와 일치(회귀 없음).

## 산출물
- `src/Forge.Core/InferenceEngine.cs` + 테스트
- 콘솔 검증 로그
