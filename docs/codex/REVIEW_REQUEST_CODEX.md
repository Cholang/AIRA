# Codex 리뷰 요청서

사용자가 Codex에게 "리뷰 요청서 읽고 리뷰해 줘"라고만 말하면 되도록 Claude가 쓴다(2026-10-16 사용자 — 요청 글을 복사해 붙여 넣지 않고 저장소에서 바로 읽게).
요청할 때마다 아래 **이번 요청**을 새로 고친다. 지난 요청은 지운다(무엇을 요청했는지는 HISTORY.md, 결과는 REVIEW_CODEX.md에 남는다).
등급 규칙·문서 지도는 [AGENTS.md](../../AGENTS.md). 결과는 [REVIEW_CODEX.md](REVIEW_CODEX.md)에 쓴다.

---

## 이번 요청 (2026-10-17 · Claude 작성)

### 범위

- **`4ed2348..HEAD`** — 지난 리뷰(38절, 고정 HEAD `4ed2348`) 뒤의 모든 커밋(16개 + 이 요청서 커밋). 시작할 때 `git rev-parse HEAD`를 절 첫머리에 적어 줘.
- 코드 변경은 `src`·`web/src`·`tests` 기준 약 95개 파일, +2,300/−470줄. 큰 덩어리는 **장비 트리**(X-T5·UMi17X·Oasis·G3M662M·WandererBox에 묶인 값을 `src/Astro.Server/Equipment/`의 모델로 옮김 — 동작은 그대로가 목표)와 **버튼 문구·설명**(문구가 대부분).
- 이번에도 **정확성과 장비 안전**만 봐 줘. Gemini에는 따로 요청하지 않는다.

### 결과 쓰는 곳

1. `REVIEW_CODEX.md` 끝에 **`## 40. 4ed2348..<HEAD 짧은 해시> — <한 줄 제목> (날짜)`** 절. 항목 ID는 `CX-40-01`부터.
2. **39절 표의 "Codex 재리뷰" 칸**(CX-38-01 ~ 04 — `e9c30a9`에서 반영)을 채워 줘.
3. 처리 기록 절(41절)은 Claude가 쓴다. 리뷰만이므로 코드는 고치지 않는다.

### 읽는 순서

1. `AGENTS.md` — 실사용 등급(장비 안전 · 겪음 · 이론상), 문서 지도
2. `HISTORY.md` — 맨 위부터 `e9c30a9` 항목까지가 이번 범위
3. `docs/implementation/ARCHITECTURE.md` 4.8 "장비 트리"(용어: 장비 → 종류 → 특성·모델·기본 모델, 배운 값·지금 장비)
4. `docs/OPERATION_PROCEDURE.md` — 아래 표의 "기준"에 적은 절
5. `docs/FIELD_TEST_CHECKLIST.md` — 실기 미확인 항목은 결함으로 세지 않는다
6. `git diff 4ed2348..HEAD`

### 변경 묶음과 의심해 볼 곳 (위가 먼저)

| # | 묶음 · 커밋 | 의심해 볼 곳 | 기준 |
|---|---|---|---|
| 1 | **아이라 종료 — 켜진 프로그램 고르기** — `197910a` | 누가 켰든 떠 있는 프로그램을 모두 보여 주고(처음엔 모두 체크) 고른 것만 닫는다. N.I.N.A.를 남기고 다른 것을 닫으면 `ProgramCloser.DisconnectNinaAsync`로 N.I.N.A. 장비 연결을 먼저 끊는다(적도의 마지막) — 추적 끄기·정지 확인이 앞 단계에서 끝난 뒤인지, 끊기가 실패·시간 초과일 때 그래도 PHD2·허브 프로그램을 닫아 N.I.N.A. 오류 창이 뜨거나 장비를 놓치는 경로, `CloseOneAsync(ninaDisconnected)`가 "N.I.N.A.가 떠 있으면 다른 것은 닫지 않음"을 너무 넓게 풀지 않는지, 모의 장비 모드·촬영 중에는 닫지 않는지, 확인 창 목록과 실제 닫는 목록의 차이(그 사이 켜진 것은 닫지 않음) | OPERATION_PROCEDURE "아이라 종료" |
| 2 | **장비 트리 — 적도의·카메라 모델** — `51a568c` · `71d1dac` | `MountCatalog.Match`(이름·드라이버에 "OnStep"이면 UMi17X 60°)·기본 모델 45°·홈 센서 없음: `RealPolarDevices.FindPoleAsync`가 회전 각도·속도를 모델에서 읽는 것, 적도의 정보를 못 읽으면 기본 모델(45°)로 도는 것, 홈 센서가 없으면 `ReturnToStartAsync`가 홈 명령 없이 멈춤만 확인하고 `PolarReturn.Stopped`를 돌려 사용자가 옮기게 하는 흐름이 "같은 방향으로 또 돌리지 않기"(CX-36-01)를 지키는지. 노출 중단: `NinaRig.AbortExposureAsync`가 `CameraTraits.CanAbortExposure`로 갈리는 것(후지 = 중단 안 보냄 — X-T5에 중단을 보내는 경로가 새로 생기지 않았는지), 카메라 정보를 못 읽으면 "모름"으로 남는지 | OPERATION_PROCEDURE "① 극축 정렬" · 공통 "X-T5는 노출을 중단하지 않는다" · ARCHITECTURE 4.8 |
| 3 | **노출 맞추기** — `d6d5c52` | `ExposureMatch.Next`(배경 5~10%, 포화 1%, 가이드 300초·없으면 60초, 하한 10초): 늘리려다 상한에 걸려 줄이거나 하한 때문에 늘리는 경우가 없는지, 많아야 3장. `PrepContext.ChangeExposure`로 계획 노출·장수를 바꾸는 것이 촬영(`ShootSession`)·끝 조건·이어서 하기(`NightSession`)에 일관되게 쓰이는지. `Fits.Analyze`: 후지 원본에 14비트 위 값이 조금 있으면 16383 눈금·위 값은 포화로 — 후지가 아닌 카메라의 눈금이 바뀌지 않는지 | OPERATION_PROCEDURE "⑦ 시험 사진"(노출 맞추기) |
| 4 | **장 사이 쉬는 시간 · 배운 값** — `d6d5c52` · `51a568c` · `71d1dac` | `PlanRules.FrameOverheadSeconds` 40 → 10, 계획은 `Rig.FrameOverheadSeconds`(배운 값 → 모델 값). `Shoot/FrameGap`: 반전·초점·가이드 별 잃음·사진 실패·멈춤이 낀 간격을 빠짐없이 버리는지, 끝날 때만 기록(모의 장비 제외). `EquipmentLearned`(`equipment.json`) — 등록 안 된 장비는 `SetAbsent`, 없거나 모르면 없는 것으로 | OPERATION_PROCEDURE 계획 "장수" · ARCHITECTURE 4.8 |
| 5 | **전원 허브 모델** — `3c4b630` | 하드코딩된 Wanderer Empire를 `HubCatalog`로: 미리 켜기(`EngineStarter.WarmUp` — 지금 허브가 없거나 모르면 켜지 않음), 종료 목록·닫기(`ProgramCloser` 이름표 "empire"), USB를 다시 꽂았을 때 다시 켜기(`HubAppStale`·`CloseHubApp`), 날씨 센서(`AscomWeather.DriverId` null이면 읽지 않음), 열선 자동 표시. 동작이 예전과 같은지, 이름을 바꾼 메서드의 호출이 빠진 곳 | ARCHITECTURE 4.8 |
| 6 | **포커서·가이드 카메라 모델** — `71d1dac` | `RealFocusDevices`: 0점 잡기는 모델의 `ZeroBySdk`(Oasis), 최대 위치 = 설정 Prepare:FocuserMax → 모델 → 56000 — `MoveAsync`가 매번 이 값으로 자르는지. `DevicePrecheck.UsbVendor`·`GuideCameraVendor`가 모델의 USB 번호를 쓰고, G3M662M(PHD2 이름에 제조사 없음)도 이제 0547로 확인 — 잘못 막을 수 있는 경우 | ARCHITECTURE 4.8 |
| 7 | **이동 기다리기 · 정지 확인** — `93e9aa5` · `d137442` · `3e6f5de` | `SlewTask.WaitUntilMoveAsync`: 낮은 동안 버튼 없음, 이르기만 하면 "지금 이동", 때가 되면 "○○로 이동"을 눌러야 움직임 — 누르지 않았는데 움직이는 경로. `NinaRig.MountSeenConnected`·정지 확인에서 "이번 실행에 연결된 적 없는 적도의는 멈춤"이 맞는지, "멈춘 것을 확인했어요"(`device-confirm`). `RealPolarDevices.StopAsync` 정리 분리 | OPERATION_PROCEDURE "④ 이동" · 공통 "멈추기" |
| 8 | **버튼 문구·설명 — 겪음만** — `d5273df` | `ITaskRun.AskAsync(prompt, …)`·`Current.Show`(버튼을 바꾸면 설명도 지움)로 설명이 다른 질문의 버튼에 남지 않는지, 문구를 바꾸며 버튼 id나 분기가 바뀐 곳이 없는지(문구만 바뀌어야 함) | DESIGN "버튼 단어 표준" · "버튼 레이아웃 영역" |

### 이미 확인한 것 (다시 하지 않아도 됨)

- `90301ba` 기준: 서버 시험 387 통과 · 1 건너뜀(전체 3번 연속), 웹 시험 52, lint 경고 38(새 경고 0), 솔루션 빌드 경고 0.
- 2026-10-17 낮 집에서 읽기만(적도의 움직이지 않음): 적도의 "On-Step"→UMi17X, 카메라 "X-T5"→후지 X, 포커서 "Oasis Focuser", PHD2 G3M662M·On-Step 연결, N.I.N.A. 적도의 정보 갱신 약 2초 · 멈춤 흔들림 0.0002°(`MountTiming`).
- 받은 X-T5 FITS 4장을 고친 `Fits.Analyze`로 읽어 배경 18~19%(전에는 6%로 잘못 읽음).
- 간헐적으로 실패하던 `PrepareFlowTests` 이동 기다리기 시험은 시험만 고침(`90301ba`, 앱 코드 아님).

### 빼는 것

- [임시] 건너뛰기·모의 실패 버튼, 폰 화면, `FIELD_TEST_CHECKLIST.md`의 실기 미확인 항목, [임시]·[확인 필요] 기준값(노출 맞추기 목표 7%, 모르는 적도의 45° 등), 문구·모양 취향, 시안(`mockups/`)·UI 텍스트 검수 도구(`docs/textedit/`).

### 쓰는 방식 (기존 절과 같게)

- 절 첫머리: 범위 · 기준 HEAD · 읽은 문서 · **결과 요약**(P1/P2 개수와 실사용 등급).
- 항목: `### CX-40-NN · P1|P2 · 장비 안전|겪음|이론상 — 한 줄 제목` 아래에 **파일:줄** · **변경 커밋** · **사용자에게 보이는 상황**(한 문장) · **근거** · **검증**(재현했으면 방법) · **수정 방향** · **검증 한계**.
- 이론상 항목은 짧게 묶어도 된다.
