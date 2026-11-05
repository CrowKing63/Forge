# S14 — 모델 카탈로그 조사 (CPU 텍스트 처리 모델)

## 목적
Forge(S9) 모델 카탈로그에 넣을 후보를 정리한다. 기준: **CPU에서 무리 없이 도면서 텍스트 처리(번역·교정·재작성·요약)에 능한, 유명한 모델**.

## 선정 기준
- CPU 구동 부담 낮음 (Q4 퀀트 기준 RAM ~2–3GB 이하; 특화용은 sub-1GB)
- 텍스트 처리 품질: 번역(KR→EN 중심), 교정(GEC), 재작성, 요약
- 공개 GGUF + llama.cpp 호환 (Forge 런타임 전제)
- 라이선스가 상업 배포에 안전

## Tier 1 — 번역 전문
| 모델 | 크기 (Q4) | 라이선스 | 비고 |
|------|-----------|----------|------|
| **Tencent Hy-MT2 1.8B** | ~1.3GB (추정) | 확인 필요 | 번역 전용 "fast-thinking" MT 모델. 한국어 포함 20개 언어, llama.cpp CPU 공식 지원(`-ngl 0`). 자사 벤치마크: 1.8B가 Microsoft·Doubao API를 전체적으로 상회. Arm 2bit 에지 버전도 공개 |
| **Gemma 4 E2B** (현재 기본) | ~2.6GB (UD-Q4_K_XL, 레포 검증됨) | Gemma ToU | 일반용 다국어 최상위권. 번역 특화 모델 대비 전방위형 |

> 참고: "비영어→영어로 Gemma가 독보적"은 **번역 전용 모델 기준으로는 구식**. Hy-MT2 1.8B는 더 작으면서 번역 도메인에서 앞서는 케이스.

## Tier 2 — 교정(GEC) 전문 (영어권 사용자 대상)
| 모델 | 크기 | 비고 |
|------|------|------|
| **SmolLM2-360M-GEC** | 368MB (Q8_0) | 문장당 CPU ~1초. 과수정 방지 훈련(이미 맞는 문장은 안 건드림). 커뮤니티 파인튜닝 |
| **Qwen3.5-0.8B GEC** (KAZ-RUS-ENG) | ~537MB (Q4_0) | CPU ~44 tok/s, 단어를 보존하는 교정. 개인 릴리스(loqira) — 공식 모델 아님 |
| quill-fix-v1 / dyslexic-writer-qwen3 등 | ~500MB | 같은 계열 커뮤니티 파인튜닝. 벤치마크 주장만 있고 독립 검증 없음 |

> 교정 도메인은 "작을수록 정밀"이 테마 (360M~0.8B급). 참고: GECToR 계열 26M 에디트 태거가 소형 생성 LLM보다 교정 정확도가 높다는 사례도 있음(Mindscend blog) — 단, GGUF/llama.cpp이 아니라 Forge 범위 밖.

## Tier 3 — 일반용 소형
| 모델 | 크기 (Q4) | 라이선스 | 비고 |
|------|-----------|----------|------|
| **Phi-4-mini 3.8B** | 2.49GB | MIT | CPU 전용 최강 평가. ARC-C 83.7%(크기당 최고), MMLU 67.3%. 단, 영어 편중 — 다국어 약함 |
| **Qwen3.5-4B** | ~2.5GB | Apache 2.0 | 119개 언어, 262K 컨텍스트, thinking mode. 가장 균형 잡힌 4B |
| **Gemma 3 4B IT** | ~2.5GB | Gemma ToU | HumanEval 71.3%, GSM8K 89.2%, 128K 컨텍스트. 텍스트 벤치는 Qwen3-4B에 살짝 밀림 |
| **SmolLM3-3B** | ~1.9GB | Apache 2.0 | 3B급 1~2위, 완전 투명(데이터·코드 공개). 유럽어 6개 — 한국어 약함 |
| **Llama 3.2 3B** | ~2GB | Llama Community | 가장 가벼운 3B, 커뮤니티 최대. 영어 편중 |
| **Ministral 3 3B/8B** | — | Apache 2.0 | 256K 컨텍스트, 에지 배포 지향 |

## 추천 (카탈로그 반영 방향)
1. **기본 유지**: Gemma 4 E2B (이미 번들·검증됨)
2. **번역용 추천 추가**: Hy-MT2 1.8B — 공식 릴리스 + GGUF 공개 + CPU 벤치마크 있음. 라이선스 확인이 선행 조건
3. **교정용**: 공식 베이스(SmolLM2-360M 계열)는 후보, 커뮤니티 파인튜닝은 품질 미검증이라 카탈로그 정식 포함 보류
4. **일반 대안**: Phi-4-mini (MIT), Qwen3.5-4B (Apache 2.0) — 라이선스 전부 상업 안전

## 미확인 항목
- [ ] Hy-MT2 라이선스 (Tencent 커스텀?) — 번들 전 확인 필요
- [ ] Hy-MT2 1.8B Q4_K_M 실제 파일 크기 (현재 추정치)
- [ ] 커뮤니티 GEC 파인튜닝 품질 독립 검증 (주장만 존재)
- [ ] 벤치마크 수치는 전부 벤더/커뮤니티 자보고 — 독립 검증 안 됨

## 출처 (2026-11-05 조사)
- Hy-MT2: huggingface.co/tencent/Hy-MT2-1.8B-GGUF (+ Arm/hy-mt2-1-8b-2bit-translation-gguf)
- GEC: huggingface.co/SupratimRK/SmolLM2-360M-GEC, huggingface.co/loqira/Qwen3.5-0.8B-GEC-KAZ-RUS-ENG, huggingface.co/Hanish/quill-fix-v1
- 소형 모델 종합: kdnuggets.com (Best Small LMs on HF), turingpost.com/slmstlist, promptquorum.com/local-llms/best-local-llms-2026
