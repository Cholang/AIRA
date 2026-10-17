# Codex 작업 지침

## 코드 리뷰

- 코드 리뷰 요청을 받으면 먼저 `docs/codex/REVIEW_REQUEST_CODEX.md`의 **이번 요청**(범위 · 중점 · 결과 쓰는 곳)을 읽는다. 사용자는 요청 내용을 따로 붙여 넣지 않는다(2026-10-16).
- 그다음 저장소 루트의 `HISTORY.md`를 읽는다. 최신 항목과 리뷰 대상 변경에 해당하는 항목에서 요청 / 변경 / 확인 / 남은 것을 파악한다.
- 기록은 의도와 맥락을 이해하는 자료로 사용하고, 실제 최신 코드와 Git diff를 대조해 문제를 검증한다. 이전 대화의 코드가 그대로라고 가정하지 않는다.
- 사용자가 지정한 변경 범위와 심각도 기준을 우선한다. 변경 부분에 연결된 코드도 필요한 만큼 확인한다.
- 지금은 시뮬레이션으로 종료 단계까지 만든 뒤 **실장비로 실내 시험 중**이다(2026-10-12). 하늘이 필요한 것은 아직 실기 미확인(`docs/test/` — 사용자·Claude 확인 목록)이고, 의도된 시뮬레이션·미구현·실기 미확인 자체는 결함으로 취급하지 않는다. 실제 모드의 결함을 발견하면 발생 조건과 현재 개발 단계에 미치는 영향을 구분해서 설명한다.
- 리뷰만 요청받았을 때는 코드를 임의로 수정하지 않는다.
- 지적 항목마다 P1/P2와 별도로 **실사용 등급**을 붙인다 (2026-10-11 사용자 결정 — 사용자가 모든 항목을 추적하기 어렵다):
  - **장비 안전**: 장비가 사람 없이 움직이거나, 움직이는지·멈췄는지 모른 채 남는다. 드물어도 해당.
  - **겪음**: 평소 사용에서 화면·동작으로 드러난다 (발생 조건이 흔한 조작 한두 개).
  - **이론상**: 실패가 여러 개 겹쳐야 하거나, 일어나도 사용자가 알아채기 어렵다.
  - 등급마다 "사용자가 무엇을 하면 어떻게 보이는지"를 한 문장으로 쓴다. 이론상 항목은 길게 쓰지 않는다.

## 문서 지도 (2026-10-12 정리)

- 저장소 맨 위에는 README.md · CLAUDE.md · AGENTS.md · DESIGN.md · HISTORY.md만 둔다. `docs/`의 파일 이름은 대문자 + 밑줄.
- 사용자에게 **보이는 것**(배치·색·움직임·문구): `DESIGN.md`
- 아이라가 **하는 일**(단계·작업 순서, 장비 동작, 실패 처리, 안전 규칙, 사용자 결정): `docs/OPERATION_PROCEDURE.md`
- **코드 구조**: `docs/implementation/`(PREPARE · SHOOT · PRECHECK · POWER_WIRING · ARCHITECTURE)
- **왜 이렇게 됐나**: `docs/DECISION_HISTORY.md`(결정), `HISTORY.md`(커밋)
- **실기로 아직 확인 못 한 것**: `docs/test/USER_TESTS.md`·`docs/test/CLAUDE_TESTS.md` — 여기 있는 항목은 결함으로 세지 않는다
- 외부 참고: `docs/reference/`(N.I.N.A. API 대응표, N.I.N.A. 사전 준비). 리뷰·요청서: Codex는 `docs/codex/`(요청서 `REVIEW_REQUEST_CODEX.md`, 결과 `REVIEW_CODEX.md`), Gemini는 `docs/gemini/`(요청서 `REVIEW_REQUEST_GEMINI.md`, 결과 `REVIEW_GEMINI.md`). 대화 기록: `conversations/`
- **문서와 코드가 다를 때**: DESIGN·OPERATION_PROCEDURE는 사용자 결정의 기준이라 코드가 다르면 결함 후보. implementation 문서는 코드 설명이라 문서 쪽이 낡았을 수 있다(그때는 "문서 갱신 필요"로).
- **2026-10-12 전의 리뷰·기록에 나오는 옛 경로**: DESIGN_HISTORY → DECISION_HISTORY · PREPARE_IMPLEMENTATION, SHOOT_IMPLEMENTATION, PRECHECK_DESIGN, POWER_LAYOUT_PLAN, onboarding-and-architecture → implementation/(PREPARE, SHOOT, PRECHECK, POWER_WIRING, ARCHITECTURE) · api-coverage, nina-prerequisites → reference/(NINA_API_COVERAGE, NINA_SETUP) · codex/AIRA_ERROR_VERIFICATION → codex/ERROR_VERIFICATION_CODEX · nina-ai-assistant-plan(초기 기획서)은 지움. 그 전 DESIGN.md의 "3장 촬영 준비 ①~⑦"·종료 순서 같은 동작 규칙은 지금 OPERATION_PROCEDURE.md에 있다.
- 새 문서가 필요하면 위 위치에 맞춰 만든다. 검토가 끝난 임시 문서는 내용을 기준 문서에 합치고 지운다.
