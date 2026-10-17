# Codex 리뷰 요청서

사용자가 Codex에게 "리뷰 요청서 읽고 리뷰해 줘"라고만 말하면 되도록 Claude가 쓴다(2026-10-16 사용자 — 요청 글을 복사해 붙여 넣지 않고 저장소에서 바로 읽게).
요청할 때마다 아래 **이번 요청**을 새로 고친다. 지난 요청은 지운다(무엇을 요청했는지는 HISTORY.md, 결과는 REVIEW_CODEX.md에 남는다).
등급 규칙·문서 지도는 [AGENTS.md](../../AGENTS.md). 결과는 [REVIEW_CODEX.md](REVIEW_CODEX.md)에 쓴다.

---

## 이번 요청 (2026-10-18 · Claude 작성)

### 범위

- **`a7dff52..HEAD`** — 지난 리뷰(40절, 고정 HEAD `a7dff52`) 뒤의 모든 커밋. 시작할 때 `git rev-parse HEAD`를 절 첫머리에 적어 줘.
- 큰 덩어리: ① 1회차 실기(장비 연결, 낮) 뒤 고친 것 `f4e73dd` ② **화면 문구 키**(버튼·제목·상태 줄·레일 글자를 `src/Astro.Core/UiText.json`으로 — 동작은 그대로가 목표, 파일 수가 많음) ③ 40절 반영(이 요청서와 같은 커밋).
- 이번에도 **정확성과 장비 안전**만 봐 줘.

### 결과 쓰는 곳

1. `REVIEW_CODEX.md` 끝에 **`## 42. a7dff52..<HEAD 짧은 해시> — <한 줄 제목> (날짜)`** 절. 항목 ID는 `CX-42-01`부터.
2. **41절 표의 "Codex 재리뷰" 칸**(CX-40-01 ~ 06, 문서)을 채워 줘.
3. 처리 기록 절(43절)은 Claude가 쓴다. 리뷰만이므로 코드는 고치지 않는다.

### 읽는 순서

1. `AGENTS.md` — 실사용 등급, 문서 지도
2. `HISTORY.md` — 맨 위부터 `f200412`([Codex] 40절) 항목 위까지가 이번 범위
3. `docs/implementation/ARCHITECTURE.md` 5.4.1 "화면 문구 키"
4. `docs/OPERATION_PROCEDURE.md` — 아래 표의 "기준"에 적은 절
5. `docs/test/USER_TESTS.md`·`CLAUDE_TESTS.md` — 실기 미확인 항목은 결함으로 세지 않는다
6. `git diff a7dff52..HEAD`

### 변경 묶음과 의심해 볼 곳 (위가 먼저)

| # | 묶음 | 의심해 볼 곳 | 기준 |
|---|---|---|---|
| 1 | **종료 때 홈** — `MountHome.OfferForQuitAsync`·`HomeForQuitAsync`, `/mount/home-for-quit`, `stop-for-quit?keepHome`, `QuitControl` | 체크한 채 종료: 진행 중인 홈 이동(장비 준비 끝의 자동 홈)을 멈추지 않고 기다리는 것이 다른 작업 정지 확인을 건너뛰지 않는지, 홈 도착·정지를 확인하지 못하면 프로그램을 닫지 않고 "그래도 종료"로 가는지, 체크를 끄면 홈 이동을 그대로 멈추는지, 미연결·이미 홈이면 건너뛰는지, "그래도 종료"·Windows 종료는 홈을 보내지 않는지, 촬영 중 종료 순서 | OPERATION_PROCEDURE "아이라 종료" · "8. 적도의 홈 버튼" |
| 2 | **OnStep 홈 이동 판정** — `MountTiming.HomingLikely`·`MountState.Moving` | OnStep은 홈 이동 중 좌표를 처음부터 홈 값(적위 90°·pierUnknown)·Slewing=false로 준다(2026-10-17 실기). 이를 움직이는 중으로 보는 것이 정지 확인·끝 상태·홈 버튼에 빠짐없이 쓰이는지, 반대로 실제로 멈춘 적도의를 영원히 "움직이는 중"으로 가두는 경로(그때는 사용자 직접 확인으로 풀리는지) | OPERATION_PROCEDURE 공통 "OnStep(UMi17X)의 홈 이동" |
| 3 | **추적 켜기·끄기 판단** — `NinaRig.SetTrackingAsync` | 요청이 실패로 와도 상태로 판단·2초 뒤 한 번 더 — 상태를 못 읽으면 끈 것으로 치지 않는지, 켜기(극축 정렬·이동 뒤)에도 같은 판단이 맞는지 | 공통 "추적" |
| 4 | **40절 반영** | CX-40-01(이어서 하기의 정지 확인 — `trustNeverConnected`·`afterPreviousRun`), 02(N.I.N.A. 끊기 확인), 03(촬영 중 종료 목록·목록 받기 전 잠금), 04(바꾼 노출을 확정 계획에), 05(직접 확인을 다음 정지 확인 한 번에), 06(확정 뒤 거절 누적) — 41절 표 | 41절 |
| 5 | **센터링 사진 먼저** — `NinaRig.CaptureAsync(preview)`·`PreparedImageSinceAsync` | 솔빙 중 `IMAGE-PREPARED` 이벤트 뒤 `prepared-image`를 받아 보이는 것: 이번 사진이 아닌 이전 사진을 보일 수 있는지(이벤트 기준 시각), 받기 실패가 촬영 결과를 망치지 않는지 | OPERATION_PROCEDURE "⑤ 센터링" |
| 6 | **SharpCap 화면** — `LiveImages`(창 캡처 대체 뺌)·`SharpCapBridge.StopView`·`WindowThumbnail` waiting·`WindowView` | 데스크톱 창이 찾는 중일 때 대체 사진을 묻지 않음(15초), 비추기 시작하면 저장 멈춤 — 극축 정렬 측정(스크립트 보고)에 영향이 없는지, 적색 테마·자리 부족 때 대체 사진이 여전히 나오는지 | DESIGN "하늘 화면" |
| 7 | **모의 모드·N.I.N.A. 알림·허브 창** — `EngineStarter`, `AppServer` 종료 목록(모의는 아이라 뒤 켜진 것만), `NinaLogRules`("unexpected value" 버림), `BackgroundWindows.HubWindows` | 모의 모드 종료가 실제 장비를 쓰던 N.I.N.A.를 닫는 경우, "unexpected value"를 버려 진짜 통신 문제를 숨기는 경우 | OPERATION_PROCEDURE "아이라 종료" · "N.I.N.A. 오류 알림" |
| 8 | **화면 문구 키 — 겪음만** — `UiText`·`uiText.ts`·`ButtonText`·`BUTTON`·웹 `Stage` id | 키로 바꾸며 버튼 id·분기·비교(예: 단계 이름으로 비교하던 곳 → `Stage` id)가 바뀐 곳, `UiText.Format`의 조사·자리 채우기가 틀린 문장을 만드는 경우. 문구 자체는 보지 않아도 된다 | ARCHITECTURE 5.4.1 |

### 이미 확인한 것 (다시 하지 않아도 됨)

- 서버 시험 402 통과 · 1 건너뜀, 웹 시험 52, lint 경고 38(새 경고 0), 솔루션 빌드 경고 0. `UiTextTests`가 코드의 키가 모두 표에 있고 표의 키를 모두 쓰는지 본다.
- 2026-10-17 밤 실기: N.I.N.A. API로 적도의를 홈에 보내 AtHome·추적 꺼짐 확인(홈 이동 중 좌표 모습을 이때 기록), N.I.N.A. 로그로 홈 이동 중 적경 "unexpected value"와 종료 때 추적 끄기 요청 0.4초 실패를 확인.
- 모의 서버로 종료 홈 API·저장 멈춤·숨쉬기 투명도·화면 문구(프로필~장비 준비).

### 빼는 것

- [임시] 건너뛰기·모의 실패 버튼, 폰 화면, `docs/test/`의 실기 미확인 항목, [임시]·[확인 필요] 기준값, 문구·모양 취향, 시안(`mockups/`)·UI 텍스트 검수 도구(`docs/textedit/`).

### 쓰는 방식 (기존 절과 같게)

- 절 첫머리: 범위 · 기준 HEAD · 읽은 문서 · **결과 요약**(P1/P2 개수와 실사용 등급).
- 항목: `### CX-42-NN · P1|P2 · 장비 안전|겪음|이론상 — 한 줄 제목` 아래에 **파일:줄** · **변경 커밋** · **사용자에게 보이는 상황**(한 문장) · **근거** · **검증**(재현했으면 방법) · **수정 방향** · **검증 한계**.
- 이론상 항목은 짧게 묶어도 된다.
