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

## 4. 결론

1. **용량·속도**: Hy-MT2는 압도적으로 유리 — 파일 43%, 로드 3x 빠름, 생성 2x 빠름. CPU 부담이 절반 이하.
2. **품질**: 번역 자체의 품질은 Gemma와 동급(성공 케이스 기준). 단, 스탑 불안정성이 치명적 — 6개 중 3개에서 퇴화.
3. **통합 비용**: drop-in 불가. 카탈로그에 넣으려면 (a) 모델별 채팅 템플릿 적용 지원, (b) 스탑 토큰/마커 후처리(출력을 `<|hy_` 마커에서 잘라내기), (c) 퇴화 입력 재시도 로직이 필요.
4. **권고**: Gemma 4 E2B 기본 유지는 변함없음. Hy-MT2는 "번역 전용 모델" 슬롯을 만들 때만 후보 — 그전까지 S14의 Tier 1 지위는 유지하되 **통합 전제사항(템플릿+후처리) 명시** 필요.

## 부록
- 벤치마크 하네스: `~/.hermes/cache/scratch/modelbench` (Forge.Core 참조 콘솔 앱, scratch — 레포 밖)
- 원본 리포트: `C:\Users\uitaek\AppData\Local\Temp\bench-forge.txt` (Gemma), `bench-hymt2readme.txt` (Hy-MT2 최적 조건)
- 측정 일시: 2026-11-05
