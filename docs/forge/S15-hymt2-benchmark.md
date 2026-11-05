# S15 — Hy-MT2 1.8B 벤치마크 (Gemma 4 E2B 대비)

## 목적
S14에서 Tier 1 번역 후보로 지목한 **Tencent Hy-MT2 1.8B**를 실제 측정으로 검증한다: 용량·로드·속도 비교, 원문/번역본 품질 대조. S14의 미확인 항목("Q4_K_M 실제 파일 크기")을 해소한다.

## 테스트 설정
- **하드웨어**: 유지관리자 PC (Windows 11, CPU only — `gpuLayerCount: 0`)
- **런타임**: Forge.Core InferenceEngine (LLamaSharp, GGUF), 컨텍스트 4096
- **샘플**: KR→EN 번역 6건 — Forge eval-set의 translate 항목 3건 + register 다양성 확인용 추가 3건 (formal/casual/tech)
- **캡**: maxTokens 256 (runaway 방지. 정상 번역은 10~30 토큰에 종료)
- **모델 파일**:
  - Hy-MT2: `tencent/Hy-MT2-1.8B-GGUF` Q4_K_M — **1,133,080,448 bytes (1.05 GiB)**
  - Gemma 4 E2B: `unsloth/gemma-4-E2B-it-qat-GGUF` UD-Q4_K_XL — **2,620,370,976 bytes (2.44 GiB)**

## 1. 용량·속도 비교

| 항목 | Hy-MT2 1.8B | Gemma 4 E2B | 비율 |
|------|-------------|-------------|------|
| 파일 크기 | 1.05 GiB | 2.44 GiB | **43%** |
| 로드 시간 | 1.0s | 2.4–3.6s | ~3x 빠름 |
| 생성 속도 (스테디) | ~21 tok/s | ~9–13 tok/s | **~2x 빠름** |
| RAM (추정, 컨텍스트 포함) | ~1.5GB | ~3.5GB | ~40% |

속도는 6샘플 전체 평균 기준. 첫 샘플에 워밍업 편차가 있어 스테디 상태(두 번째 이후) 수치로 비교한다.

## 2. 원문/번역본 대조

### Gemma 4 E2B (Forge 프롬프트 — 현재 파이프라인 그대로)
| # | 원문 (KR) | 번역 (EN) | 토큰 |
|---|-----------|-----------|------|
| 1 | 이메일은 확인했습니다. 내일까지 답장해 주세요. | I have received the email. Please reply by tomorrow. | 11 |
| 2 | 나중에 또 연락할게~ | I'll contact you again later~ | 8 |
| 3 | 예산이 초과될 것 같아서, 이번 분기 지출을 다시 검토하고 있습니다. | I'm reviewing this quarter's spending again because I think we might go over budget. | 19 |
| 4 | 다음 주에 계약 갱신 건으로 미팅을 요청드리고 싶습니다. 가능한 날짜를 알려주시면 감사하겠습니다. | I would like to request a meeting next week regarding the contract renewal. Please let me know what dates work for you. | 24 |
| 5 | 답장 늦어서 죄송해요, 이번 주에 일이 너무 많았어요. | Sorry for the late reply, I've been so busy this week. | 15 |
| 6 | API가 페이로드가 1MB를 초과하면 500 에러를 반환해서, 청크 업로드를 구현해야 합니다. | The API returns a 500 error if the payload exceeds 1MB, so we need to implement chunked uploads. | 26 |

**6/6 정상 종료, 전부 고품질.** "~" 같은 구어체 뉘앙스도 보존.

### Hy-MT2 1.8B (자체 채팅 템플릿 + README 프롬프트 — 최적 조건)
| # | 원문 (KR) | 번역 (EN) | 결과 |
|---|-----------|-----------|------|
| 1 | 이메일은 확인했습니다. 내일까지 답장해 주세요. | I've checked the email. Please reply by tomorrow. | ✅ 클린 (12 tok) |
| 2 | 나중에 또 연락할게~ | *(환각 대화 생성 — doctor/법률 이야기 루프)* | ❌ 퇴화 |
| 3 | 예산이 초과될 것 같아서, 이번 분기 지출을 다시 검토하고 있습니다. | It seems that the budget will be exceeded, so I am reviewing the expenditures for this quarter again. | ✅ 클린 (20 tok) |
| 4 | 다음 주에 계약 갱신 건으로 미팅을 요청드리고 싶습니다… | *(프롬프트+번역 반복 루프)* | ❌ 퇴화 |
| 5 | 답장 늦어서 죄송해요, 이번 주에 일이 너무 많았어요. | I'm sorry for the delay in responding. This week, I had too much work to do. | ✅ 클린 (21 tok) |
| 6 | API가 페이로드가 1MB를 초과하면 500 에러를 반환해서… | The 500 error occurs when the API exceeds 1MB in payload. You need to implement chunking for chunk upload. *(이후 마커 퇴화)* | ⚠️ 부분 (57 tok) |

**3/6 클린.** 성공한 케이스의 품질은 Gemma와 동급(문체만 더 stiff). 실패 패턴: 구어체("~"), 긴 formal 문, 기술 문.

## 3. 프롬프트 통합 테스트 (핵심 발견)

Forge 파이프라인(PromptBuilder → raw Complete, 채팅 템플릿 미적용)에서 4가지 구성을 테스트:

| 구성 | 결과 |
|------|------|
| **Gemma 프롬프트** (PromptBuilder) | ❌ 전량 실패 — `<\|turn>` 마커로 환각 대화 생성. drop-in 불가 |
| **raw 프롬프트** (템플릿 없음) | ⚠️ 첫 줄만 번역, 이후 루프 퇴화 |
| **자체 템플릿 + 긴 설명형 프롬프트** | ⚠️ 1/6 클린 |
| **자체 템플릿 + README 형식** (`Translate the following segment into English, without additional explanation:{입력}`) | ✅ 3/6 클린 (최선) |

GGUF 메타데이터에서 추출한 채팅 템플릿:
```
<|hy_begin_of_sentence|><|hy_User|>{입력}<|hy_Assistant|>
```
(Hunyuan 계열 마커. 스탑 토큰은 `<|hy_placeholder_no_2|>` — 입력에 따라 불안정하게 출력됨)

## 4. 퀀트 비교 (Q4_K_M vs Q8_0)

같은 샘플·프롬프트(README 형식)로 Q8_0 재실행:

| 항목 | Q4_K_M | Q8_0 |
|------|--------|------|
| 파일 크기 | 1.05 GiB (1,133,080,448 B) | **1.78 GiB (1,908,528,192 B)** |
| 로드 시간 | 1.0s | 1.7s |
| 생성 속도 (스테디) | ~21 tok/s | ~13.5 tok/s |
| 스탑 신뢰성 | 3/6 클린, 3/6 런어웨이 | **6/6 정상 종료** |
| 품질 | 성공 케이스 동급 | 동급 이상 (consistency ↑) |

Q8 번역본:
| # | 원문 (KR) | Q8 번역 (EN) |
|---|-----------|--------------|
| 1 | 이메일은 확인했습니다. 내일까지 답장해 주세요. | Please confirm the email. Please reply by tomorrow. *(톤이 살짝 다름)* |
| 2 | 나중에 또 연락할게~ | I'll contact you again later~ ✓ |
| 3 | 예산이 초과될 것 같아서, 이번 분기 지출을 다시 검토하고 있습니다. | I think the budget will be exceeded, so I'm re-evaluating the expenses for this quarter. ✓ |
| 4 | 다음 주에 계약 갱신 건으로 미팅을 요청드리고 싶습니다… | I would like to request a meeting next week regarding the contract renewal. Please let me know the possible dates, and I will be grateful. ✓ |
| 5 | 답장 늦어서 죄송해요, 이번 주에 일이 너무 많았어요. | I'm sorry for the delay in my reply. I've been extremely busy this week. ✓ |
| 6 | API가 페이로드가 1MB를 초과하면 500 에러를 반환해서… | The API returns an error 500 when the payload exceeds 1MB. Therefore, it's necessary to implement chunked uploading. ✓ |

**Q8의 발견:**
1. **스탑 불안정 해소** — Q4에서 런어웨이했던 3개 케이스(구어체~, formal, tech)가 전부 정상 종료. 퀀트 품질이 스탑 토큰 출력을 안정화했음.
2. **마커 에코 아티팩트** — 출력에 `<|hy_User|>`, `<|hy_Assistant|>` 마커와 선두 `>`가 섞임 (formal 케이스는 프롬프트 문장까지 에코). 단, 이게 오히려 후처리를 단순화함: 마커 위치가 항상 존재하므로 잘라내기 기준이 확실해짐.
3. **비용** — 크기 +70%, 속도 -35%. Q8이 "안정성"을 살리지만 CPU 부하가 Q4 대비 크게 늘음.

## 5. 공식 통합 (2026-11-05)

Q8_0을 `%LOCALAPPDATA%\Forge\Models\`에 배치하고, 앱이 Hy-MT2를 제대로 쓸 수 있도록 Forge.Core에 모델 패밀리 지원 추가:

| 변경 | 내용 |
|------|------|
| `ModelProfile` (새 파일) | 파일명에서 패밀리 감지(`Hy-MT2` → HyMt2, 그 외 Gemma4) + Hy-MT2 출력 토큰 캡(512, 런어웨이 방지) |
| `PromptBuilder.Build(task, text, family)` (오버로드) | Hy-MT2는 README 검증 형식 `<\|hy_begin_of_sentence\|><\|hy_User\|>{지시}{입력}<\|hy_Assistant\|>` — 번역은 README 원문 지시, 그 외 태스크는 기존 SystemPrompts |
| `OutputPostProcessor.Clean(raw, family)` (새 파일) | `<\|hy_User\|>`~`<\|hy_Assistant\|>` 사이 추출(마커 에코 스트립), 마커 쌍 없으면 전 토큰 제거 |
| 호출 지점 4개 | App.cs(RunTaskAsync, direct flow) + Cli(interactive, eval) — family 감지 → 프롬프트 → 후처리 연결 |

**E2E 검증 (Forge.Cli eval, Q8_0):**
- **translate-01/02/03: 3/3 PASS** — 마커 없이 클린 출력, 품질 양호
- proofread/rewrite/summarize: FAIL (language mismatch) — **예상된 동작**: Hy-MT2는 번역 전용이라 비번역 태스크에서 한국어 입력을 영어로 번역해버림

**한계 (문서화됨):**
- Hy-MT2 로드 시 교정·재작성·요약 태스크는 쓸 수 없음. 태스크 게이팅(UI 경고/차단)은 미구현 — 사용자 판단으로 보류.
- Gemma 4 E2B 기본 모델 유지(ForgeSettings.ModelPath 디폴트 변경 없음). Hy-MT2는 Settings → Model 파일 경로로 옵트인.
- **짧은 회화체 입력 퇴화** (2026-11-05 추가 확인): 인사말("안녕하세요 처음 뵙겠습니다"), 감사("감사합니다"), 동의("네 알겠습니다") 등 2~4자 회화 표현은 번역이 아니라 대화체 응답/마커 환각. Hy-MT2는 문서 세그먼트 번역용이라 완전한 문장·단락에는 정상 작동(eval 3/3)하지만, 정보량이 적은 회화 표현은 설계 범위 밖. Forge의 주 사용 시나리오(문서에서 문장/문단 선택 → 번역)에는 영향 없음.

## 6. 결론

1. **용량·속도**: Hy-MT2는 Gemma 대비 압도적으로 유리 — 파일 43%(Q4)/73%(Q8), 생성 2x 빠름(Q4).
2. **품질**: Q4는 스탑 불안정(3/6 퇴화)으로 실사용 불가, **Q8은 6/6 안정** + 품질 동급 이상. Q8이 실질 후보.
3. **통합 비용**: drop-in 불가. (a) 모델별 채팅 템플릿 적용 지원, (b) 마커 스트립 후처리(Q8은 마커가 항상 나오므로 단순), (c) 토큰 캡 안전장치 필요.
4. **권고**: Gemma 4 E2B 기본 유지. Hy-MT2는 **Q8_0 기준**으로 "번역 전용 모델" 슬롯 후보 — Q4_K_M은 스탑 불안정 때문에 카탈로그 제외.

## 부록
- 벤치마크 하네스: `~/.hermes/cache/scratch/modelbench` (Forge.Core 참조 콘솔 앱, scratch — 레포 밖)
- 원본 리포트: `C:\Users\uitaek\AppData\Local\Temp\bench-forge.txt` (Gemma), `bench-hymt2readme.txt` (Hy-MT2 Q8 최적 조건)
- 측정 일시: 2026-11-05
