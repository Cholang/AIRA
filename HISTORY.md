# 변경 기록

커밋마다 한 항목. **최신이 위.** 리뷰(GPT Codex 등)가 "무엇을 요청했고 그래서 무엇을 바꿨는지"를 빠르게 보기 위한 요약이다.
세부는 커밋 diff, 화면 규칙은 [DESIGN.md](DESIGN.md), 대화 맥락은 [conversations/](conversations/)에 있다.

항목 형식: 날짜 · 커밋 제목 / **요청** / **변경** / **확인** / **남은 것**(있으면). 짧게.
작성자 표시: 제목에 `[Claude]`·`[Codex]`·`[Gemini]`(Gemini 답변은 Claude가 옮겨 넣음). 리뷰(REVIEW_CODEX.md)를 반영한 커밋은 반영한 리뷰 ID와 반영하지 않은 이유를 적는다.

---

## 2026-10-17 · [Claude] 노출 맞추기 · 후지 사진 밝기 읽기 고침 · 장 사이 쉬는 시간 10초

**요청**: 노출 맞추기(2026-10-15 합의한 시험 사진 절차 1~3)를 다운로드 폴더의 X-T5 FITS로 맞춰 구현. 가이드 없을 때 상한 60초(사용자). 장 사이 40초가 이상하다(사용자) — 실제 값으로.

**변경**:
- `ExposureMatch`(시험 사진): 배경이 바닥 위 5~10%(목표 7%)를 벗어나면 묻지 않고 노출을 바꿔 한 장 더(많아야 3장), 포화 1% 넘으면 줄임, 상한 가이드 300초·없으면 60초(`GuidingTask.UnguidedExposure`), 하한 10초, 늘리려다 상한에 걸려 줄이거나 하한 때문에 늘리지 않음. `PrepContext.ChangeExposure` — 계획에 반영하고 총 노출이 같게 장수도 다시(가이드 없이 줄일 때도). 예전 "{n}초로 다시 찍기 / 이대로 진행" 질문과 `TestShotRules`의 노출 제안·`BackgroundLimit` 지움. 모의 시험 사진은 배경이 노출에 비례.
- `Fits.Analyze`: 후지 원본에 14비트 위 값이 조금(1% 미만) 있으면 눈금 16383·바닥 1024 그대로, 그 위는 포화 — X-T5 사진(ISO 800·12초)이 65535까지 1936점 때문에 16비트·바닥 0으로 읽혀 배경 18%를 6%로 쟀다.
- `PlanRules.FrameOverheadSeconds` 40 → 10초: 2026-05-08 N.I.N.A. 연속 촬영 사진 번호·시각으로 6~10초. 40초(10-08)는 저장 폴더가 OneDrive 안이던 때.
- 문서: OPERATION_PROCEDURE 노출 맞추기(1~3 구현, 4 틸트 조절과 다크로 읽기 잡음은 [계획]), SHOOT.md·DECISION_HISTORY, FIELD_TEST_CHECKLIST에 아이라 촬영의 장 사이 시간 재기.

**확인**: 서버 시험 342 통과·1 건너뜀(`ExposureMatchTests` 새로 — 규칙 표·장수 다시·모의 밝은 하늘·X-T5 모양 FITS), 솔루션 빌드 경고 0. 받은 FITS 4장을 고친 코드로 읽어 배경 18~19% → 12초를 10초(하한)로 줄이자는 판단 확인. 실제 밤하늘에서 목표값은 아직.

**남은 것**: 아이라로 찍을 때의 장 사이 시간 실측, 마무리 다크로 ISO별 읽기 잡음, 틸트 조절 흐름.

---

## 2026-10-17 · b70b883 [Claude] 문구 검수 도구 docs/textedit — 편집 도구 하나 + 요청마다 JSON

**요청**: 앱 문구를 사용자가 직접 보며 고칠 수 있는 HTML 도구. 1열 앱에 쓰인 문구, 2열 바꿀 문구(비우면 그대로), 3열 언제 나오는지(내가 씀). 사용자가 저장하면 내가 보고 반영.

**변경**: `docs/textedit/editor.html`(JSON 열기·같은 파일에 저장(Edge·Chrome, 그 밖은 내려받기)·Ctrl+S·입력한 줄/반영 안 된 줄 거르기·바뀌는 자리 {…} 빠짐 경고·저장 안 함 경고), 첫 묶음 `2026-10-17-button-prompts.json`(버튼 레이아웃 영역 설명문 74줄 — 같은 문장은 한 줄로 묶고 상황을 모두 적음). CLAUDE.md "문서 위치"에 폴더와 반영 순서.

**확인**: 표본 데이터로 표·경고·입력칸 높이를 브라우저에서 봄. 파일 선택 창과 같은 파일에 덮어쓰기는 자동으로 누를 수 없어 확인 못 함.

---

## 2026-10-17 · d5273df [Claude] 버튼 단어 표준과 버튼 레이아웃 영역 적용 — 표준 단어 상수, 55:45 설명 | 버튼, 단계마다 설명문

**요청**: 시안 B(설명 굵게 없이, 3줄 자리, 설명·버튼 아래 맞춤, 상태 줄과 간격 1.5배)로 정하고 진행 — 버튼 레이아웃 수정, 설명문 수정, 버튼 모듈화, 모듈화된 버튼 적용. 보조 버튼은 시안보다 10% 작게, 글자 한 단계 작게.

**변경**:
- 서버: `ButtonText`(재시도·이대로 진행·건너뛰기·완료·다시 확인·다시 하기). `CenterView.Prompt`, `ITaskRun.AskAsync(prompt, …)`(시험용 가짜는 기본 구현으로 무시), `IPrepTask.StartPrompt`, `Completed.ExtraPrompt`, `RunnerSetup.FinishPrompt`, 러너 `Current.Show`(버튼을 바꾸면 설명도 지움)·`JoinPrompt`·`RetryPrompt`. 작업 9개(극축 정렬~장비 정리)의 버튼 문구를 표준·사용자 결정대로 바꾸고 묻는 곳마다 설명문(첫 줄 주 버튼을 누르면 무엇이 되는지 묻기, 다음 줄 보조 버튼) — 적도의가 움직이는 것은 설명에. 안내 문장·상태 줄에서 버튼 이름을 부르던 말을 뺌. `Josa.Reul` 추가.
- 웹: `buttonText.ts`(`BUTTON`), `PrepCenter` 버튼 레이아웃 영역(55fr/45fr, 높이 3줄, 아래 맞춤, margin-top 8px, 주 버튼 최소 200px, 보조 버튼 50px·최소 126px·`--size-body`), 촬영·요약 화면 설명과 문구(중단 후 종료·새 대상을 찾기·플랫 프레임 찍기), 그 밖의 재시도·완료·돌아가기·구도 회전 "다시 찍기".
- 고침: [임시] 건너뛰기 안내가 "을 시험용 …"으로 단계 이름이 빠지던 조사 오류.
- 문서: DESIGN(버튼 단어 표준 [계획] → 규칙, 버튼 레이아웃 영역), OPERATION_PROCEDURE·FIELD_TEST_CHECKLIST 버튼 이름, DECISION_HISTORY. 시안 B에 3줄 자리·아래 맞춤·간격·영역 테두리 보기.
- 사용자 결정이 없던 곳의 내 판단: "멈춘 것을 확인했어요"·"직접 확인했어요"(안전 확인)는 그대로, 다른 단계로 가는 버튼은 단계 이름을 남김(캘리브레이션·센터링·가이드 다시 하기, 초점 다시 맞추기), "다시 시작"은 그대로.

**확인**: 서버 시험 329 통과·1 건너뜀(새 `ButtonPromptTests` 3개), 솔루션 빌드 경고 0, 웹 tsc·lint(경고 38 그대로)·시험 52·빌드. 모의 서버(5211)로 장비 준비 끝 화면을 띄워 버튼 1개 배치를 보고, 3개는 화면에 버튼을 덧붙여 높이·간격·아래 맞춤·보조 버튼 크기를 잼. 실패 상황별 실제 화면은 못 봄.

**남은 것**: 설명문 검수(`docs/textedit/2026-10-17-button-prompts.json`).

---

## 2026-10-17 · 0641e48 [Claude] 버튼 배치 시안 B: 55% : 45%, 설명은 한 흐름의 문장으로

**요청**: 비율을 55:45로. 왼쪽 설명은 버튼마다 따로 설명하는 것이 아니라 "주 버튼을 누르면 이렇게 되는데 어떻게 할까요?" + 줄바꿈 + "(보조 버튼)을 누르면 이렇게 돼요"로.

**변경**: `mockups/aira-button-layouts.html` B — 55fr/45fr, 세 상황의 설명을 질문 + 보조 버튼 문장으로, 굵은 버튼 이름이 줄을 끊던 것(문단에 grid) 고침, 줄 높이 64px 고정에 한·두 줄 모두 가운데.

**확인**: 1600px 창에서 1개는 한 줄, 2·3개는 두 줄로 들어감.

---

## 2026-10-17 · 698b282 [Claude] 버튼 배치 시안 B를 65% : 35%로

**요청**: B안은 아래 영역을 65% : 35%로 나눠 왼쪽 설명은 오른쪽 정렬, 오른쪽 버튼은 왼쪽 정렬(설명과 버튼이 붙게). v2 없이 바로 수정.

**변경**: `mockups/aira-button-layouts.html` B — 65fr/35fr 격자, 설명 오른쪽 정렬, 버튼 왼쪽 정렬·주 버튼 맨 앞, 장단점 문구와 추천 문장 고침.

**확인**: 1600px 창에서 3개일 때 설명 끝 1115px · 버튼 시작 1143px · 버튼 끝 1667px(진행 표시 아래 도구 1788px과 떨어짐, 앱 틀 단위).

---

## 2026-10-17 · ba6ec66 [Claude] 버튼 단어 표준 [계획] · 버튼 배치 시안

**요청**: 버튼 문구를 짧은 표준 단어(재시도·이대로 진행·건너뛰기 …)로 정하고 따로 저장. 그러면 설명이 길어지니 아래 버튼 배치(1·2·3개) 시안 — 지금 배치 / 왼쪽 두 줄 설명 + 오른쪽 버튼 / 내 제안.

**변경**: DESIGN.md 5장에 "버튼 단어 표준 [계획]"(표준 단어, 예/아니오 금지, 사용자와 정한 버튼별 문구). 시안 `mockups/aira-button-layouts.html` — A 지금 · B 사용자 안(주 버튼 맨 오른쪽 고정) · C 버튼마다 아래 한 줄 설명, 버튼 수·테마 전환.

**확인**: 시안을 브라우저 창에서 1·2·3개 모두 열어 봄. 앱 코드는 바꾸지 않음.

**남은 것**: 배치를 정한 뒤 버튼 문구·배치를 앱에 적용.

---

## 2026-10-16 · 197910a [Claude] 종료 창: 떠 있는 프로그램을 모두 체크 목록으로, N.I.N.A.를 남기면 장비 연결부터 끊기

**요청**: 아이라를 껐는데 N.I.N.A.·PHD2·Wanderer Empire가 남음. 누가 켰는지 상관없이 종료 때 지금 켜져 있는 것을 닫을지 체크하게(처음엔 모두 체크). N.I.N.A.를 남길 땐 N.I.N.A.의 전체 연결 끊기를 먼저 하고 나머지를 끄면 되지 않나.

**변경**:
- `ProgramCloser`: `Targets(onlyOurs)`(종료는 false — 누가 켰든 전부), `NinaRunning`, `DisconnectNinaAsync`(가이더 → 카메라 → 포커서 → 필터휠 → 회전기 → 플랫 → 스위치 → 적도의 disconnect, 하나 실패해도 계속), `CloseOneAsync(ninaDisconnected)`로 N.I.N.A.가 떠 있어도 닫기 허용, Empire도 전부. 쓰지 않게 된 `LaunchedNina`·`NinaLaunched`(EngineStarter 인자) 지움.
- `AppServer`: `GET /engine/programs`(확인 창 목록), `/engine/close`의 "아이라가 켠 N.I.N.A." 조건 대신 전부, `POST /engine/close/{id}`는 N.I.N.A.를 남기면 먼저 `DisconnectNinaAsync`. 모의 장비 모드·촬영 중에는 목록 없음/닫지 않음.
- `QuitControl`: 확인 창을 열 때 목록을 받아 체크 칸으로(종료 중 표시 칸과 같은 자리), 체크한 것만 닫고 나머지는 "남겨 둠". 그 사이 새로 켜진 것은 닫지 않음.
- 시험 `ProgramCloserTests`(끊는 차례·하나 실패해도 계속). 문서: OPERATION_PROCEDURE "아이라 종료"·프로필 미리 켜기, DESIGN 종료 창, DECISION_HISTORY(CX-20-06을 대신함).

**확인**: 서버 시험 326 통과·1 건너뜀, 솔루션 빌드 경고 0, 웹 tsc·lint(경고 38 그대로)·시험 52·빌드. 체크 목록 화면과 실제 프로그램 닫기는 직접 보지 못함(모의 모드는 목록이 비므로 — 집에서 실기로).

---

## 2026-10-16 · 93e9aa5 [Claude] 주 버튼 글이 길면 한 줄로 넓게 · 이동 기다리기를 기다리는 화면으로 바로

**요청**(모의 장비로 끝까지 실행해 본 뒤): "캘리브레이션 시작 · 남쪽 하늘로 이동" 버튼 글이 두 줄. "기다리기"는 누르면 버튼만 보였다 사라지고 결국 할 수 있는 건 "지금 이동"뿐이라 의미가 없음.

**변경**:
- `PrepCenter.module.css`: 버튼 줄을 3칸 격자(빈 칸 · 주 버튼 · 보조 버튼)로 — 주 버튼은 가운데 고정, 최소 280px에 글이 길면 한 줄로 넓어짐, 보조 버튼은 그 오른쪽.
- `SlewTask.WaitUntilMoveAsync`: "기다리기 / 지금 이동"을 묻고 "기다리기 끝"으로 돌아오던 고리를 없애고 기다리는 화면으로 바로. 15° 아래면 버튼 없이(이동 막음), 시각만 이르면 "지금 이동" 하나, 기다리는 까닭이 바뀔 때만 안내·버튼을 바꿈(틱마다 다시 묻지 않음 — 깜빡이지 않게), 때가 되면 알리고 "○○로 이동"을 누른 뒤에 움직임. `WaitAsync` 지움.
- 시험 셋을 새 흐름에 맞춤(`PrepareFlowTests` 낮은 대상, `TaskIsolationTests` 지금 이동, `Review36Tests` CX-36-03 — 낮아지면 버튼 없이 다시 기다림). 문서: OPERATION_PROCEDURE "④ 이동", DECISION_HISTORY.

**확인**: 서버 시험 324 통과·1 건너뜀, 솔루션 빌드 경고 0, 웹 tsc·lint(경고 38 그대로)·시험 52·빌드. 화면은 직접 띄워 보지 않음(사용자 다음 실행에서).

---

## 2026-10-16 · 3e6f5de [Claude] 극축 정렬 정리가 정지 확인을 막지 않게 (PHD2 오류 창) · 계획 노출 − + 1초씩

**요청**: 아이라를 끄고 보니 PHD2 위에 "ASCOM driver problem during connection: Serial port is not connected" 오류 창 — 왜 떴나. 대상 고르기의 노출 − + 를 1 단위로.

**변경**:
- 원인(기록 21:44~45): [임시] 건너뛰기로 극축 정렬을 멈출 때 `RealPolarDevices.StopAsync`가 PHD2에 가이드 카메라를 돌려주며(`set_connected`) PHD2가 프로필의 적도의(ASCOM OnStep, 전원 꺼짐)까지 연결하려다 오류 창을 띄움. 창이 떠 있는 동안 PHD2가 답하지 않아 정지 확인이 시간 초과 예외로 실패("멈춘 것을 확인했어요"로 넘어감).
- `StopAsync`: 정리(SharpCap 닫기·PHD2에 돌려주기)를 정지 결과와 분리(시간 초과·PHD2 오류를 삼키고 적도의 정지 결과를 돌려줌), 이번 실행에서 적도의가 연결된 적이 없으면(`NinaRig.MountSeenConnected`) PHD2에 돌려주지 않음.
- `PlanRules.ExposureStepSeconds` 10 → 1(화면의 − + 단위는 서버가 정함). 총 노출은 10분 그대로.
- 문서: DECISION_HISTORY 두 줄.

**확인**: 서버 시험 324 통과·1 건너뜀, 솔루션 빌드 경고 0. 장비 없는 실제 실행으로 다시 보지는 않음(PHD2 오류 창 재현은 사용자 PC의 PHD2 프로필이 필요).

---

## 2026-10-16 · d137442 [Claude] 장비가 끊긴 채 건너뛰기가 막히던 것 — 적도의 정보 읽기 예외, "연결된 적 없음 = 멈춤", "멈춘 것을 확인했어요"

**요청**: 실행 중 극축 정렬에서 "멈췄지만 장비가 멈췄는지 확인하지 못했습니다"가 뜨고 [임시] 건너뛰기를 눌러도 넘어가지 않음 — 고치기(장비 전원은 꺼져 있었음).

**변경**:
- `NinaRig.MountAsync`: 적도의가 끊겨 있으면 N.I.N.A.가 `Coordinates`를 주지 않아 `GetProperty`가 예외 → 정지 확인이 통째로 실패했다. 없으면 NaN(모름). 연결된 것을 한 번이라도 보면 `_mountSeenConnected`.
- `NinaRig.StillAsync`(정지 확인): N.I.N.A.가 "연결 안 됨"이라고 분명히 답하면 — 이번 실행에서 연결된 적이 없으면 멈춤(아이라가 움직였을 리 없음), 연결됐다 끊겼으면 모름.
- 러너 정지 확인(`PrepareRunner.ShowStopUnconfirmed`): "장비 상태 다시 확인" 옆에 "멈춘 것을 확인했어요"(`device-confirm` → `ConfirmStopByUserAsync` — 앞 실행이 끝났을 때만 풀고 하려던 일을 이어서, 건너뛰기 포함). 적도의 홈의 "확인 필요"와 같은 방식.
- 시험: `MountStillTests` 3(좌표 칸 없음, 연결된 적 없음 = 멈춤, 연결됐다 끊김 = 모름), `TempSkipTests` 1(직접 확인하면 건너뛰기 이어짐). 문서: OPERATION_PROCEDURE "장비 안전", DECISION_HISTORY.

**확인**: 서버 시험 324 통과·1 건너뜀(전체 2번), 솔루션 빌드 경고 0. 사용자의 21:37 기록과 같은 상황(적도의 COM3 없음, 넘겨받기 중 건너뛰기)은 장비 없는 앱으로 다시 보지 않음 — 위 시험으로 같은 경로를 확인.

---

## 2026-10-16 · 999fcf4 [Claude] 9칸 확대 보기를 실장비에도 — 시험 사진의 귀퉁이·변·가운데 조각 + 칸마다 별 크기·길쭉함

**요청**: 실장비에서 숨겨져 있던 9칸 확대 보기 만들기(사용자 — 사진 종류와 상관없이 기능부터, 실제 사진은 만든 뒤 넣어 고치기).

**변경**:
- `Prepare/Real/TestTiles.cs`(새): 네 귀퉁이·네 변 가운데·한가운데에서 256px 정사각형을 원본 화소로 잘라 PNG. 색 무늬는 6×6 평균(3×3은 X-Trans에서 가로줄), 밝기는 아홉 칸이 같은 늘이기(6×6 평균값의 중앙값·퍼짐·최댓값, 배경 15%). `Png.FromGray16`은 조각마다 다시 늘려 쓰지 않음. ROWORDER BOTTOM-UP이면 위아래 뒤집기.
- `RealTestShotDevices`: 시험 사진을 받을 때 조각을 `LiveImages`의 `test-tile-0~8`로(사진 경로를 화면에 맡기지 않음), `ShotStats.Tiles`.
- `TestShotTask`: 하늘 화면 데이터에 `tiles`, 측정값에 칸마다 `tile{k}Hfd·Ecc·Stars`, 끝 안내의 "9칸 확대 보기를 누르세요"는 조각이 있을 때만(전엔 실장비에서 버튼이 없는데도 말함). 모의는 `Tiles: true`(그림).
- 웹: `LivePhoto.TileGrid`(사진 자리 안 3×3, 칸 아래에 자리 이름·"별 크기 · 길쭉함"·별이 모자라면 알림, 빛 테두리), 자리 재기는 `useSlotBox`로 `FramedImage`와 함께. `LiveView`가 실장비의 9칸이면 `TileGrid`. `PrepareScreen`의 버튼은 `live.data.tiles`가 있을 때만.
- 문서: DESIGN(9칸 확대 보기), DECISION_HISTORY, FIELD_TEST_CHECKLIST, implementation/PREPARE.

**확인**: 서버 시험 320 통과·1 건너뜀(새: 조각 크기·늘이기·뒤집기 2), 솔루션 빌드 경고 0, 웹 tsc·lint(경고 38 그대로)·시험 52·빌드. 보낸 X-T5 사진(80MB)으로 조각 만들기 0.2~0.3초, 임시 확인 페이지(가짜 서버가 실제 조각을 내줌, 지움)에서 1440×900로 보기 — 아홉 칸 밝기가 같고 별이 둥글게 보임. 1.3초 렌즈 사진이라 별이 작고 흐린 것은 사진 탓. 준비 화면 전체 흐름(실제 시험 사진 → 버튼 → 9칸)은 실장비가 없어 못 봄.

---

## 2026-10-16 · e9c30a9 [Claude] Codex 38절 반영 — 이어서 하기는 극축 먼저 홈으로 · 계획 고침 거절 뒤 다시 맞추기 · 확정은 모든 고침 뒤 · 기존 CSV 건드리지 않기

**요청**: Codex 38절 4건 반영(사용자 동의 — 장비 안전 1 · 겪음 2 · 이론상 1).

**변경**:
- CX-38-01 `NightSessionStore.RestoreAsync`: 되살린 기억은 늘 `PolarNeedsReturn = true`(꺼지기 전에 돌렸는지 알 수 없음).
- CX-38-02 `PlanScreen`: 고침이 거절되면 `resync`(`/api/plan/state`로 계획·그래프 다시 받기), 다시 받지 못했으면 `stale` — 확정 전에 다시 시도하고 실패하면 확정 안 함.
- CX-38-03 `PlanScreen.start`: 고침 사슬 끝이 더 바뀌지 않을 때까지 기다림, 그 사이 거절이 있으면 확정 안 함.
- CX-38-04 `StarShapes.ExtractAsync`: 실행 전에 같은 이름의 CSV가 있으면 건너뜀(읽지도 지우지도 않음).
- 시험 2개(`CX38_01_*` · `CX38_04_*`). 문서: OPERATION_PROCEDURE ①·⑦, DECISION_HISTORY, REVIEW_CODEX 39절.

**확인**: 서버 시험 318 통과·1 건너뜀, 솔루션 빌드 경고 0, 웹 tsc·lint(경고 38 그대로)·시험 52·빌드. 02·03은 화면에서 지연을 재현해 보지 않음(코드·타입 검사만).

---

## 2026-10-16 · [Codex] 2a0428f..4ed2348 재리뷰 — 36절 수정·별 모양·파일 분리

**요청**: Claude의 REVIEW_REQUEST_CODEX.md를 읽고 정확성·장비 안전을 검토. 결과는 37절 재리뷰 칸과 새 38절. 임시 건너뛰기·실기 미확인·임시 수치 기준은 제외.

**변경**: 고정 HEAD `4ed23481adaaf47118ca4663d8d3c608c4537b68` 기준으로 `docs/codex/REVIEW_CODEX.md` 37절 9칸을 채우고 38절을 추가했다. 기존 7건 수정 확인·2건 부분 반영. 새 지적은 P1 장비 안전 1건(재시작 시 극축 복귀 필요 기억 유실), P2 겪음 2건(연속 저장 실패 뒤 화면·서버 불일치, 확정 대기 중 추가 수정 누락), P2 이론상 1건(이번 실행이 만들지 않은 CSV 삭제). 파일 분리 자체에서 새 동작 결함은 확인하지 못했다.

**확인**: 저장소 밖 고정 커밋 복사본의 관련 서버 시험 39/39(기존 37·결함 재현 2), 추출한 실제 계획 함수의 가짜 응답 재현 3조건, 네트워크 차단 Edge의 Enter 4조건·Modal 배율 4조건. 실제 ASTAP·N.I.N.A. API·AI·실장비·천체사진 프로그램은 사용하지 않았다. 앱 코드·기존 테스트 수정 없음, 커밋·푸시 없음.

**남은 것**: CX-38-01~04의 반영 여부와 처리 기록(39절)은 Claude가 정리. 실기 미확인 목록은 그대로.

---

## 2026-10-16 · 4ed2348 [Claude] Codex 리뷰 요청서 — 2a0428f..HEAD (CX-36 반영 재확인 · 별 모양 · 분리)

**요청**: 오늘 바뀐 것을 Codex가 다시 보게 요청서 고치기.

**변경**: `docs/codex/REVIEW_REQUEST_CODEX.md`의 "이번 요청"을 새로 — 범위 `2a0428f..HEAD`, 결과는 38절·37절 재리뷰 칸. 묶음 다섯(CX-36 반영 · 시험 사진 별 모양 · Gemini 7절 반영 · 큰 파일 분리는 옮기기만인지 · 팝업·툴팁). 분리 커밋 보는 법(`git show -M --color-moved`)과 옮기기가 아닌 두 커밋을 적음.

**확인**: 문서만.

---

## 2026-10-16 · 244677b [Claude] 큰 파일 분리 마무리 — 계획서 지우고 구조 문서에 반영

**요청**: 분리가 끝나면 계획서를 지우고 ARCHITECTURE·PREPARE·SHOOT에 구조 반영(계획서의 약속).

**변경**: `docs/implementation/SPLIT_PLAN.md` 지움. ARCHITECTURE 5.3 아래 "큰 파일 나눔"(partial 규칙·나눈 파일), PREPARE 구조도(PrepareRunner partial·LiveView 파일들), SHOOT 구조(ShootSession 파일들).

**확인**: 문서만. 분리 전체 결과 — 서버: ShootSession 1003→360 · PrepareRunner 995→473 · EquipmentConnector 854→525 · PlanAssistant 570→243 · AppServer 654→287 · NinaNotices 476→198. 웹: LiveView 688→120 · PlanScreen 656→217 · EquipmentScreen 864→511 · RigScene 699→약 530 · App 625→약 555. 서버 시험 316 통과·1 건너뜀, 웹 시험 52, lint 경고 38, 빌드 경고 0. 시험 서버에서 부팅 → 프로필 → 점검 → 장비 연결·장비 변경 모드를 지나가 콘솔 오류 없음 확인. 계획·준비(LiveView) 화면은 N.I.N.A. 없이 관측지 저장에서 막혀 화면으로는 못 봄(타입 검사로만).

**남은 것**: 계획서와 다르게 하지 않은 것 — `RigScene`의 장비 선화(`RigArt`)는 장면 안의 상태·ref를 props로 넘겨야 해 코드 모양이 바뀌므로 4단계 몫, `AppServer`의 서비스 등록·`Build` 안 엔드포인트 묶기도 4단계 몫. 4단계(긴 함수의 블록을 메서드·훅으로)는 필요할 때만.

---

## 2026-10-16 · b0d33b0 [Claude] 빌드 경고 둘 정리 (쓰지 않는 _hubNoPower · SlewTask nullable)

**요청**: 분리 계획의 "분리 뒤 따로 정리".

**변경**: `EquipmentConnector._hubNoPower`(넣기만 하고 읽는 곳 없음 — CS0414) 필드와 대입 한 줄 지움. `SlewTask.MoveAsync`의 `Task<string?> stop` → `Task<string>`(`AskAsync`가 돌려주는 형 — CS8619).

**확인**: 솔루션 빌드 경고 0·오류 0, 서버 시험 316 통과·1 건너뜀.

---

## 2026-10-16 · 5569b15 [Claude] 큰 파일 분리 3단계 — EquipmentScreen의 부품을 따로

**요청**: 분리 계획 3단계 셋째 파일.

**변경** (옮기기만): `screens/EquipmentScreen.tsx`(864 → 511줄: 화면·태양계 그래프)에서 `EquipmentParts.tsx`(행성·편집 행성·드라이버 목록·카메라 전원·상태 글·[임시] 실패 버튼, 349줄). 두 파일이 함께 쓰는 `HUB`·`selectable`은 `equipmentOrbits.ts`로(컴포넌트 파일에서 상수·함수를 내보내면 lint 경고), `WAITING_FOR_HUB`는 부품 파일로.

**확인**: 바뀐 줄은 `export`·import·파일 설명 줄뿐(옮긴 줄 332). tsc·lint(경고 38 그대로)·웹 시험 52·웹 빌드.

---

## 2026-10-16 · 9b04203 [Claude] 큰 파일 분리 3단계 — PlanScreen을 대화·대상·설정으로

**요청**: 분리 계획 3단계 둘째 파일.

**변경** (옮기기만 — `export`·import만 바뀜): `screens/PlanScreen.tsx`(656 → 217줄: `PlanScreen`·`PlanBody`·`TARGET_ITEMS`)에서 `PlanChat.tsx`(AI 대화, 250줄) · `PlanTarget.tsx`(구도 미리보기·시간대 줄·화각 상자, 140줄) · `PlanSettings.tsx`(설정 줄과 `whyShort`, 157줄). CSS는 모두 `PlanScreen.module.css`.

**확인**: 바뀐 줄은 `export`·import·파일 설명 줄뿐(옮긴 줄 466). tsc·lint(경고 38 그대로)·웹 시험 52·웹 빌드. 화면 확인은 3단계 끝에.

---

## 2026-10-16 · 2258a5f [Claude] 큰 파일 분리 3단계 — LiveView를 사진·판·모의 그림으로

**요청**: 분리 계획 3단계(웹 컴포넌트) 첫 파일.

**변경** (옮기기만 — 옮긴 것에 `export`, import만 바뀜): `components/LiveView.tsx`(688 → 120줄: 하늘 화면 고르기)에서 `LivePhoto.tsx`(히스토그램·실제 사진·사진 틀·솔빙 사진·시험 사진·9칸, 253줄) · `LivePanels.tsx`(초점·SharpCap·가이드 사진·가이드 그래프·하늘 지도·초점 곡선, 283줄) · `liveSimFields.tsx`(좌표 VW·VH·FX·FY와 모의 별밭·은하, 59줄) · `useSlewTrack.ts`(이동 진행 훅 — 컴포넌트 파일에서 훅을 내보내면 lint 경고라 따로). CSS 모듈은 모두 `LiveView.module.css`를 함께 씀.

**확인**: 바뀐 줄은 `export`·import·파일 설명 줄뿐(옮긴 줄 548). tsc·lint(경고 38 그대로)·웹 시험 52·웹 빌드. 화면 확인은 3단계 끝에 한꺼번에.

---

## 2026-10-16 · 36c96e4 [Claude] 큰 파일 분리 2단계 — AppServer의 영역별 엔드포인트를 partial로

**요청**: 분리 계획 2단계 다섯째 파일.

**변경** (옮기기만): `AppServer.cs`(654 → 287줄: 서비스 등록과 `Build`)에서 `AppServer.Endpoints.cs`(이미 메서드로 나뉘어 있던 `MapShoot`·`MapPrepare`·`MapSettings`·`MapPlan`·`MapSession`·`MapProfiles`와 도우미·요청 레코드, 381줄), `static partial class`. **계획과 다름**: 서비스 등록을 `AddAiraServices`로, `Build` 안의 엔드포인트를 메서드로 묶는 것은 코드 모양을 바꾸는 일이라 하지 않음(4단계 몫). URL·등록 순서는 그대로.

**확인**: 바뀐 줄은 새 파일 머리와 `partial`뿐(옮긴 줄 367). 솔루션 빌드(경고 2개 그대로), 서버 시험 316 통과·1 건너뜀. 시험 서버(5211, 장비 흉내)를 띄워 `/`·프로필·촬영·준비·설정·이어서 하기 조회 응답 확인 후 끔.

---

## 2026-10-16 · a2a0bda [Claude] 큰 파일 분리 2단계 — PlanAssistant의 AI 대화를 partial로

**요청**: 분리 계획 2단계 넷째 파일.

**변경** (옮기기만): `Assistant/PlanAssistant.cs`(570 → 243줄: 필드·확정·복원·화면에서 고치기·보기·프로필·장비 반영)에서 `PlanAssistant.Chat.cs`(`SendAsync`·이벤트·할당량 시각·밤 요약·시스템 지시문, 222줄). 흩어진 필드 `_profileId`는 필드 자리로.

**확인**: 바뀐 줄은 새 파일 머리와 본 파일의 `partial`뿐(옮긴 줄 208). 솔루션 빌드(경고 2개 그대로), 서버 시험 316 통과·1 건너뜀.

---

## 2026-10-16 · e0d546a [Claude] 큰 파일 분리 2단계 — EquipmentConnector를 partial 셋으로

**요청**: 분리 계획 2단계 셋째 파일.

**변경** (옮기기만): `Engine/EquipmentConnector.cs`(854 → 525줄: 슬롯·실행·연결 `ConnectOneAsync`·결과 만들기)에서 `EquipmentConnector.Hub.cs`(허브 실패 때 PC에 보이는지·배선 학습·허브 전압·출력 켜기·목록 기다리기, 153줄) · `.Devices.cs`(PHD2 점검·프로필 장비 읽기·이름 줄이기·PHD2 카메라 다시 가리키기, 122줄). 흩어진 필드 `_timing`·`_learnOnPass`(설명 줄과 함께)는 필드 자리로.

**확인**: 바뀐 줄은 새 파일 머리와 본 파일의 `partial`뿐(옮긴 줄 255). 솔루션 빌드(경고 2개 그대로 — `_hubNoPower`는 분리 뒤 따로), 서버 시험 316 통과·1 건너뜀.

---

## 2026-10-16 · f816928 [Claude] 큰 파일 분리 2단계 — PrepareRunner를 partial 다섯으로

**요청**: 분리 계획 2단계 둘째 파일.

**변경** (옮기기만): `Prepare/Flow/PrepareRunner.cs`(995 → 473줄: 필드·상태 읽기·시작·실행 고리·끝·오래된 값 표시)에서 `PrepareRunner.Stop.cs`(준비 중단·종료 전 멈춤·다른 묶음에 넘기기·정지 확인, 214줄) · `.Actions.cs`(버튼·다시 하기·다시 시도, 108줄) · `.TempSkip.cs`([임시] 건너뛰기 — 지울 때 이 파일째, 114줄) · `.RunHandle.cs`(작업이 화면을 갱신하는 손잡이, 68줄). 흩어진 필드 `_pendingFinish`는 필드 묶음으로.

**확인**: 바뀐 줄은 새 파일 머리와 본 파일의 `partial`뿐(옮긴 줄 478). 솔루션 빌드(경고 2개 그대로), 서버 시험 316 통과·1 건너뜀.

---

## 2026-10-16 · 43ab528 [Claude] 큰 파일 분리 2단계 — ShootSession을 partial 셋으로

**요청**: 분리 계획 2단계(서버 partial) 첫 파일.

**변경** (옮기기만): `Shoot/ShootSession.cs`(1003 → 360줄: 필드·상태 읽기·주 고리 `RunAsync`·`EndReason`)에서 `ShootSession.Control.cs`(시작·중단·`[임시]` 건너뛰기·답·끝·정지 확인·기록, 274줄)·`ShootSession.GuideWatch.cs`(이슬·디더링 불안정·가이드 별 잃음·제외 사진·가이드 값 읽기, 358줄). 흩어져 있던 필드 넷(`_flipped`·`_lastFocusTemp`·`_hfrRecent`·`_recordIndex`)은 필드 묶음으로(선언 자리만).

**확인**: 바뀐 줄은 새 파일 머리(using·namespace·partial 선언·괄호)와 본 파일의 `partial`뿐. 솔루션 빌드(경고 2개 그대로), 서버 시험 316 통과·1 건너뜀.

---

## 2026-10-16 · 5edddf8 [Claude] 큰 파일 분리 1단계(웹) — 장면 데이터·앱 흐름 상수·궤도 계산을 새 파일로

**요청**: 분리 계획 1단계(웹).

**변경** (옮기기만 — 옮긴 것에 `export`, 쓰는 쪽 import만 바뀜): `RigScene.tsx` → `components/rigSceneData.ts`(점검 항목·하늘·별자리·땅·잔디·구체 회전·부팅·유성 상수, 안에서만 쓰는 것은 내보내지 않음). `App.tsx` → `appFlow.ts`(Phase·STAGE_OF·SCENE_OF·CORNER_SUN·NOTICE_HIDE·WATCHED·SITE_*·LABEL_OF·페이드 시간). `BOOT_PROMPT_MS`는 App이 `rigSceneData`에서 읽음. `EquipmentScreen.tsx`의 `orbitPath`·`sunArcPath` → 이미 있던 `screens/equipmentOrbits.ts`.

**확인**: 바뀐 줄을 세어 옮긴 줄 말고는 `export`·import 줄뿐임을 확인. tsc·lint(경고 38 그대로)·웹 시험 52·웹 빌드. 시험 서버 화면 확인은 3단계(컴포넌트 분리) 뒤에 한꺼번에.

---

## 2026-10-16 · 4d57178 [Claude] 다크플랫 옮기기를 ImageFolders.Move로 (Gemini A-14)

**요청**: 분리 계획의 1단계 끝 작은 정리.

**변경**: `RealWrapDevices.MoveToDarkFlatAsync`의 폴더 계산·옮기기 6줄을 `ImageFolders.Move(file, "DARK", "DARKFLAT")`로 — 같은 계산이라 동작 차이 없음(DARK 폴더면 옆, 아니면 아래, 같은 이름이면 덮지 않음).

**확인**: 솔루션 빌드(경고 2개 그대로), 서버 시험 316 통과·1 건너뜀. 실제 다크플랫 저장은 실기 미확인(전과 같음).

---

## 2026-10-16 · 6877fca [Claude] 큰 파일 분리 1단계(서버) — 독립된 타입·클래스를 새 파일로

**요청**: Codex 리뷰를 반영했으니 `docs/implementation/SPLIT_PLAN.md`대로 큰 파일 분리 시작.

**변경** (옮기기만): `ShootSession.cs` → `Shoot/ShootModels.cs`(TargetShots·NightShootResult·ShootView·ShootMode). `PrepareRunner.cs` → `Prepare/Flow/RunnerSetup.cs`(떠돌던 "일곱 작업" 설명은 `Single` 위로). `EquipmentConnector.cs` → `Engine/EquipmentSimulation.cs`(EquipmentOptions·EquipmentSimulation·EquipmentChoices). `PlanAssistant.cs` → `Assistant/ChatModelFactory.cs`(AssistantOptions·ChatModelFactory·DisplayMessage)·`Assistant/PlanView.cs`. `NinaNotices.cs` → `Engine/NinaLogRules.cs`·`Engine/NinaWindowHiders.cs`(NinaProcesses·창 감추기 둘). 쓰지 않게 된 using 몇 줄 지움.

**확인**: 바뀐 줄을 세어 옮긴 줄 말고는 새 파일의 using·namespace 머리뿐임을 확인. 솔루션 빌드(경고 2개 그대로 — `_hubNoPower`·SlewTask nullable), 서버 시험 316 통과·1 건너뜀(분리 전과 같음).

---

## 2026-10-16 · 7b3bd3e [Claude] Codex 36절 반영 — 극 찾기 재시작·이어서 하기 승인·잠금 뒤 고도·계획 복귀 추적, 계획 고침 순서·밝기 눈금·구도 수명·팝업 Enter

**요청**: Codex 36절 9건 모두 반영(사용자 동의, 장비 안전 4 · 겪음 4 · 이론상 1).

**변경**:
- CX-36-01·09 `PolarTask`: `IPrepMemory.PolarNeedsReturn`(돌리기 전에 켬, 홈 도착·"손으로 홈에 옮겼어요"로만 끔), 홈 실패 뒤 선택 "손으로 홈에 옮겼어요 / 다시 홈으로 보내기 / 건너뛰기".
- CX-36-02 `PrepareFlow.ResumeTargetAsync`: 이어서 하기는 확정을 쓴 것으로 둠.
- CX-36-03 `SlewTask.MoveAsync`: 잡은 직후 `TooLow` → 보내지 않고 기다리기로(`_lowAtLock`, 이동 시작 시각도 지움).
- CX-36-04 `MountHome.RestForPlanAsync`: 바쁜 때(홈·확인 필요·촬영·작업·잠금)만 넘기고, 나머지는 `TrackingOffKnownAsync`(종료 전 추적 끄기와 공용 — 미연결 null · 못 읽음/실패 false → 오류 줄).
- CX-36-05 `PlanScreen`: 고침을 `edits` 사슬로 차례로, 늦은 응답은 버림, `start`는 진행 중이던 고침을 기다리고 거절되면 확정 안 함.
- CX-36-06 `Fits.Analyze(img, knownBits)`: 후지 14비트·N.I.N.A. 비트 수를 눈금 바닥으로, 포화는 눈금 90% 위에서 몰린 최댓값만. `RealTestShotDevices`가 `CameraBitDepthAsync`를 넘김.
- CX-36-07 `FramingGoal.PlanKey`(확정 시각), 센터링이 다른 계획의 구도를 지움.
- CX-36-08 `RigScene`: 팝업이 열렸거나 처리된·반복 Enter는 무시.
- 시험: 새 `Review36Tests` 6 · `FitsTests` 3 · `FramingTests` 1. 가끔 실패하던 시험 둘(`FramingTests.UntilCenterDone`, `다크_장수는_5장씩…`)은 보고 누르는 사이 저절로 넘어가는 경우를 기다리게 고침(시험만).
- 문서: OPERATION_PROCEDURE(극 찾기 되돌리기 · 이동 다시 확인 · 계획 복귀 추적 · 구도 수명 · 밝기 눈금), DECISION_HISTORY, REVIEW_CODEX 37절 처리 기록.

**확인**: 서버 시험 316 통과·1 건너뜀(전체 3번 연속, 새 시험은 8번 연속), 웹 tsc·lint(경고 38 그대로)·시험 52·빌드, 솔루션 빌드 경고·오류 0. 05·08은 화면에서 직접 눌러 보지 않음(코드·타입 검사만). 실장비 미사용.

---

## 2026-10-16 · [Codex] 38e1744..2a0428f 리뷰 — 재시작·이동 승인·추적 정리, 계획·구도·밝기 눈금

**요청**: 저장소의 Codex 리뷰 요청서에 따라 정확성·장비 안전을 독립 검토하고 35절 재리뷰 칸·36절에 기록. 중단 전 고정한 `2a0428f286effa17d0a1dcf48dfa8bf61fc8636a`까지 유지. 사용자 추가 결정으로 장비 없이 화면 확인용인 `[임시] 건너뛰기` 관련 지적은 제외.

**변경**: `docs/codex/REVIEW_CODEX.md` — 35절의 3건·문서 2건 반영 확인, 36절에 P1 장비 안전 4건(극 찾기 전체 재시작의 복귀 누락·빠른 복원 시 이동 승인 재사용·잠금 대기 뒤 15° 재검사 누락·계획 복귀 상태 조회 실패 시 추적 정리 생략), P2 5건(계획 저장/확정 경쟁·사진 최대 밝기 오판·옛 구도 재등장·팝업 Enter의 배경 진행·수동 홈 복귀 안내 뒤 반복 실패). 코드·기존 테스트는 수정하지 않음.

**확인**: 고정 HEAD의 저장소 밖 복사본에서 기존 서버 시험 57건과 결함 재현 시험 7건, 총 64건 통과. 실제 화면 함수에 지연 응답 2건, headless Edge에서 팝업 Enter 전파와 전체 화면 사진 6조건 확인. 재현 시험의 통과는 지적한 잘못된 동작의 관찰을 뜻함. 실장비·외부 천체 프로그램·N.I.N.A./Gemini 호출 없음. 재현 자료·검증 한계는 36절.

**남은 것**: Claude가 항목별 수용 여부를 판단해 37절에 처리 기록. 중단 뒤 추가 커밋·작업 중 변경과 실기 체크리스트는 이번 검증에 포함하지 않음. 저장소 폴더 변경에 따라 `D:/MyNewApp/AIRA`에 문서만 기록. 커밋·푸시하지 않음.

---

## 2026-10-16 · 79ac4c0 [Claude] Gemini 리뷰 7 B-05·B-06 — 팝업 크기 한도를 틀 단위로, 죽은 DiagnosisCard 지움, 툴팁 세 곳을 공통 Tooltip으로

**요청**: 미뤄 둔 B-05·B-06을 지금 반영.

**변경**:
- B-05: `Modal` — `<dialog>`에 `--frame-scale`(앱 틀 배율), `Modal.module.css`의 너비·높이 한도를 창(vw·vh) ÷ 그 배율로 (카드에 배율이 다시 곱해지므로).
- B-06: `components/DiagnosisCard.tsx`·`.module.css` 지움(쓰는 곳 없음), `DiagnosisData`는 `checks.ts`로. `Tooltip`에 `placement="right"`·`open`(감쌀 수 없는 SVG 점용)·`text-shadow: none`. 촬영 등급 분류 기준(`ShootGrades` — 줄 오른쪽), 프로필 바꾸기 별명(`StatusBar` — 사진 아래), 밤 그래프 달 고도(`NightGraph` — 달 위 빈 기준점 + `open`)를 공통 `Tooltip`으로, 각자 쓰던 말풍선 CSS 지움.
- B-06 셋째(새 프로필·전원 설정의 글자 버튼에 포커스 표시 없음)는 바꾸지 않음 — 전역 `:focus-visible` 외곽선이 그 버튼들에도 보인다(사실 아님). DESIGN(팝업 크기 한도·툴팁 자리와 `open`), REVIEW_GEMINI 대조 메모.

**확인**: tsc·lint(경고 38개 그대로, 새 경고 없음)·웹 시험 52·웹 빌드·솔루션 빌드. 시험 서버(5211, 장비 흉내, 1440×900 = 배율 0.75): 설정 팝업 카드 높이 한도 880(틀) → 실제 660px(고치기 전 639px), 프로필 별명 툴팁이 사진 아래 가운데. 촬영 등급·밤 그래프는 그 화면까지 가려면 N.I.N.A.가 필요해(관측지 저장) 임시 확인 페이지(가짜 데이터, 지움)에서: 등급 툴팁이 줄 오른쪽 10px·세로 가운데, 달 툴팁이 달 위 8px 가운데(처음엔 빈 기준점이 글줄 높이만큼 내려앉아 달을 9px 덮음 → `.moonAt` flex로 고침), 마우스가 나가면 사라짐. 화면 캡처는 창이 가려져 시간 초과가 잦아 위치는 스크립트로 잼.

---

## 2026-10-16 · ba88a68 [Claude] 시험 사진 별 모양 검사 (ASTAP 별 찾기 · 이심률 · 아홉 칸) · N.I.N.A. 카메라 비트 수 14

**요청**: 별 모양 분석기 만들기(기준은 [임시]로 먼저 — 장비 없이, 보낸 X-T5 FITS로 확인). 사용자 N.I.N.A. 프로필의 카메라 비트 수를 지금 14로.

**변경**:
- `Prepare/Real/StarShapes.cs`(새): `astap_cli -extract`로 별 목록(사진 옆 .csv — `-o`가 안 먹어 이번에 쓴 것만 읽고 지움, 60초 제한), 별마다 3×3 평균 뒤 화소 2차 모멘트로 이심률·방향, 아홉 칸으로 모음(ROWORDER BOTTOM-UP이면 칸 위아래 뒤집음). 흐름 판정용 이심률은 가운데 칸(별이 적으면 전체), 방향 모임 정도(`Alignment`).
- `Prepare/Tasks/TestShot/StarField.cs`(새): `StarTile`·`StarField`, `StarFieldRules`(기울어짐 1.2배·귀퉁이 1.25배·칸마다 별 5개 — 모두 [임시]).
- `TestShotTask`: `ShotStats.Field`, `TestShotRules.Trailed`(가운데 이심률 0.6 넘고 방향 모임 [임시] 0.5 이상 — 방향을 모르는 모의는 이심률만), 검사 글에 아홉 칸 결과·"별 모양 제각각", 끝 안내에 기울어짐·귀퉁이 알림(촬영은 막지 않음 — 조절 순서는 [계획]).
- `RealTestShotDevices`: 시험 폴더로 옮기기 전에 별 모양을 재고 서버 기록에 한 줄(칸별 HFD). `NinaRig.AstapPathAsync`(프로필의 ASTAP 위치). `Fits.Open`·`Fits.Image`(한 번 읽어 히스토그램과 별 모양이 같이 씀, 덜 저장된 파일은 null). 모의 시험 사진에 고른 아홉 칸.
- N.I.N.A. 프로필 "유성철" 카메라 비트 수 16 → 14(사용자 요청, 원본은 scratchpad에 백업). FreeImage 변환이면 실제 비트 수를 넣는 것이 N.I.N.A. 안내이고, 비트 스케일링은 Touptek 계열 전용이라 저장되는 값은 그대로. `CameraScale` 주석 맞춤.
- 문서: OPERATION_PROCEDURE ⑦(별 모양·아홉 칸, [계획] 4는 조절 순서만), DECISION_HISTORY, FIELD_TEST_CHECKLIST(별 모양 실기 항목, "아직 만들지 않은 것"에서 뺌), implementation/PREPARE.

**확인**: 서버 시험 306 통과·1 건너뜀(새 `StarShapesTests` 11 — 늘인 가상 별 이심률 ±0.04·방향 모임, 위아래 뒤집기, 가장자리 별, 아홉 칸 판정, CSV, 설치된 ASTAP으로 가상 사진 별 찾기·.csv 지움). 전체 실행 3번 중 1번 `다크_장수는_5장씩…`이 실패 — 모의가 "여기까지만 찍기" 전에 다 찍어 버리는 전부터 있던 시간 경쟁(혼자 3번 통과, 이번 변경과 무관). 보낸 X-T5 FITS(80MB): ASTAP 0.7초 + 읽기·측정 0.1초, 별 99개, 가운데 이심률 0.33·방향 모임 0.21 → "별 둥긂", 왼쪽 위 칸 별 3개라 아홉 칸은 판정 못 함. 솔루션 빌드 경고·오류 0. 화면은 바뀐 글만이라 브라우저 확인은 안 함. N.I.N.A.로 저장한 FITS와 딥스카이 사진은 실기 미확인(체크리스트).

**남은 것**: 실장비의 9칸 확대 보기(실제 사진 자르기)는 그대로 숨김 — 끝 안내의 "9칸 확대 보기를 누르세요"는 실장비에서 버튼이 없다(전부터).

---

## 2026-10-16 · ca16de5 [Claude] 큰 파일 분리 계획서 (docs/implementation/SPLIT_PLAN.md)

**요청**: Gemini 7절의 분리 계획(A)이 실제 코드와 맞지 않으니, 실제 코드를 읽고 분리 계획서를 만들어 두기(코드는 건드리지 않음 — 분리는 Codex 리뷰 반영 뒤).

**변경**: `docs/implementation/SPLIT_PLAN.md` 새로 — 400줄 넘는 14개 파일의 실제 덩어리(함수·컴포넌트 이름과 줄 범위, 기준 52fad14), 서버는 `partial class`(잠금·필드·생성자 매개변수 그대로)·필드는 본 파일 한곳에, 웹은 컴포넌트·순수 데이터만, 단계(타입·데이터 → 서버 partial → 웹 컴포넌트 → 선택: 메서드·훅 빼기), 커밋마다 확인 방법, 나누지 않는 파일(NinaRig · PlanTools · NightGraph), Gemini 계획과 다른 점(ShootSession에 반전·초점 묶음 없음, EquipmentConnector에 진단 묶음 없음, RigScene·EquipmentScreen·App 설명이 다른 내용).

**확인**: 14개 파일을 읽어 함수 경계·흩어진 필드를 확인. partial 파일에서 기본 생성자 매개변수를 쓸 수 있는지는 .NET 10 시험 프로젝트로 빌드·실행해 확인. 계획의 줄 수는 어림(옮긴 뒤 실제와 다를 수 있음).

**남은 것**: Codex 리뷰(토큰이 다 돼 멈춤) 받기 → 반영·커밋 → 이 계획서대로 분리. 끝나면 계획서는 지우고 ARCHITECTURE·PREPARE·SHOOT에 구조 반영.

---

## 2026-10-16 · 52fad14 [Claude] Gemini 리뷰 7절 반영 — 적색 테마 누출 · 옛 이름 "AA" · 오류 줄 흔들림 · 계산 시험 · docs/gemini 폴더

**요청**: Gemini 문서를 `docs/codex/`에서 `docs/gemini/`로(사용자가 로컬에서 옮긴 구조대로) 하고 경로를 고치기. Gemini 리뷰 7절(34항목)을 검토한 뒤 B-01·B-02·B-03·B-04·B-07 수정, E-01·E-02·E-04 시험 추가, D-01은 실기 확인 목록에, 사실이 아니거나 이미 처리된 것은 무시.

**변경**:
- 폴더: `docs/gemini/`(REVIEW_GEMINI · REVIEW_REQUEST_GEMINI), AGENTS·CLAUDE·README·두 요청서의 경로. 7절은 REVIEW_GEMINI.md에 합치고 끝에 대조 메모.
- GM-7-B-01: `GlowFrame`의 `#fff` → `--glow-hi`(index.css, 촬영 테마 `#d00000`). B-02: `aira-mark.svg`·`aira-wordmark.svg`의 적색 팔레트를 `#xx0000`으로. B-03: `Settings` 찾아보기 — 촬영 테마에서 aria-disabled + 이유. B-04: `NewProfileScreen` 오류 줄을 주 버튼 위 빈 간격에 늘 둠(`.actions` 기준 절대 위치, 한 줄), `ScopeList` `.error:empty` 숨김 지움. B-07: "AA가 정함" 5곳·문장 속 "아이라" 4곳 → `Product.Ga/Neun/Name`, DESIGN "처음 배선으로" → "처음 설정으로".
- E-01 `AstronomyAndNightTests` · E-02 `PlanRulesAndOpticsTests` · E-04 `sites.test.ts`·`inlineSvg.test.ts`. D-01과 적색 테마 확인 항목은 FIELD_TEST_CHECKLIST.

**확인**: 서버 시험 295(새 25) · 웹 시험 52(새 11) · tsc · 웹/데스크톱 빌드. 시험 서버에서 새 프로필 오류를 넣어 주 버튼이 안 움직이고 위아래 4px 떨어짐, 촬영 테마에서 찾아보기 막힘. 로고·빛 테두리가 순수 빨강인지 화면 캡처 확인은 미확인(체크리스트).

**남은 것**: 반영 안 함 — A 분리 계획(줄 수는 맞지만 함수 이름 상당수가 실제와 달라 실행 계획으로 못 씀, 주말 분리는 실제 코드로 다시 나눔), B-05·C-01~04·D-02~04·E-03(사실 아님·이미 처리됨). 미룸 — A-14(동작 같은 정리), B-06(`DiagnosisCard` 삭제·툴팁 통일), D-05.

---

## 2026-10-16 · 2a0428f [Claude] 리뷰 요청서를 저장소에 (Codex · Gemini) · 도는 표시 넘침 규칙

**요청**: 종료 창 버그에서 나온 규칙("돌거나 커지는 표시는 칸 안에서만, 칸은 넘침을 자른다")을 DESIGN에. Codex·Gemini 리뷰 요청서를 각각 써서 함께 커밋 — 요청 글을 복사해 붙여 넣지 않고 모델이 저장소에서 바로 읽게. Gemini는 최근 업그레이드로 출력이 훨씬 길어졌으니 그걸 감안해서.

**변경**:
- `docs/codex/REVIEW_REQUEST_CODEX.md`(새): 범위 `38e1744..HEAD`(마지막 Codex 리뷰 34절 뒤 전부), 결과는 REVIEW_CODEX.md 36절 + 35절 재리뷰 칸, 변경 묶음 10개와 의심해 볼 곳(이동 시각·승인, 멈춤·종료, 구도 회전의 다시 센터링, 준비 시간 기록, 이어서 하기 순서, [임시] 건너뛰기의 장비 안전, 밝기 눈금, 계획 v3, 화면), 뺄 것, 쓰는 방식.
- `docs/codex/REVIEW_REQUEST_GEMINI.md`(새): 저장소 읽는 법(공개 GitHub, 못 읽으면 먼저 말하기, 답은 마크다운 한 덩어리 → Claude가 REVIEW_GEMINI.md 7절로). A 큰 클래스 쪼개기 실행 계획(400줄 넘는 14개 파일 — 내일 그대로 실행할 단계·잠금 경계·단계별 시험, 다크플랫 옮기기 합치기 포함), B 화면 규칙 전수, C 문서-코드 불일치, D 현장 노트북 성능, E 시험 없는 중요 논리. 분량 제한 없음.
- `AGENTS.md`: 리뷰 요청을 받으면 요청서부터, 문서 지도에 요청서. `CLAUDE.md` 리뷰 규칙에 요청서 쓰는 법과 역할 나눔.
- `DESIGN.md` 1장 레이아웃 안정성: 돌거나 커지는 표시는 칸 안의 층만 움직이고 칸은 넘침을 자른다. DECISION_HISTORY에 한 줄.

**확인**: 문서만. 요청서의 파일·줄 수·커밋·시험 이름을 저장소와 대조.

**남은 것**: 사용자가 Codex에 "리뷰 요청서 읽고 리뷰해 줘", Gemini에 요청서 주소(https://github.com/Cholang/Astrophoto_Assistant/blob/main/docs/codex/REVIEW_REQUEST_GEMINI.md)를 준다. 결과가 오면 항목별 판단(CLAUDE.md 리뷰 규칙) — Gemini의 A는 10-17 쪼개기 전에.

---

## 2026-10-16 · 8d58434 [Claude] 준비 시간 기록 · 센터링 사진에서 구도 회전 · 부팅 로고 차례로 · 확인 창 문구와 용어 · 종료 창 스크롤 깜빡임 · 잔디·유성·극축 점선

**요청** (집 PC):
- 잔디 조금 굵게·로고보다 짙은 보라, 극축 점선을 적도의에 맞추고 빠르게, 유성은 한 번에 하나·가끔 화면 왼쪽까지 길게.
- 준비 시간: 프로필마다 최근 5번(이동 시작 → 시험 사진 끝)의 중앙값, 기록이 없으면 20분(Claude 추천 채택).
- 확인 창 문구를 사용자가 직접 고침(줄 나눔 포함). 아이라를 "앱"이라 부르지 않기. 용어: 경통 → 망원경, 가이딩 → 가이드, 전원 배선 → 전원 설정(모두). 문구 규칙도 화면 규칙이므로 DESIGN에.
- 구도 회전: 센터링 사진 꼭짓점에 굽은 양방향 화살표 → 누른 채 틀을 돌림(사진 밖은 회색) → 놓으면 사진 가운데에 어디로 얼마나. 시계 방향 N° + 다시 찍어 확인, 2° 안이면 맞춤(사용자 동의).
- 부팅: AIRA → 아이라 → 풀이 → Press Any Key 차례로, 입력은 Press Any Key부터(1초 막기 없앰), 유성은 처음부터. 키를 누르면 Press Any Key 펄스, 아이라·풀이 바로 투명, AIRA는 한 번 반짝인 뒤 선 → 별 순서로. 장비가 들어오는 것은 그대로.
- 점검 화면에서 아이라를 종료할 때 종료 팝업에 스크롤바가 생겼다 사라지기를 되풀이(버그).

**변경**:
- 장면 `RigScene`: `--art-grass`, 극축 점선을 몸통 기울기에(`POLE_TILT`·`RAY_FROM`, 하늘 그림을 `SKY_SLIDE`만큼), 점선 2.2초, 유성 `METEOR_LONG`·`METEOR_SPEED`. 부팅 차례 `BOOT_AT`·`BOOT_PROMPT_MS`(App이 이때부터 입력 — `LOGO_DRAWN_MS`·`BOOT_INPUT_DELAY_MS` 없앰), 사라짐은 CSS `promptOut`·`lineOut`·`starOut`.
- 준비 시간: `Prepare/PrepTimes.cs`(`PrepTimes` — prep-times.json, `PrepTimeRecorder`), `PrepareRunner.TaskFinished`(실제로 끝난 작업만 — 건너뛰기·이어서 하기는 오지 않음), `SlewResult.MovedAt`, `PlanRules.DefaultPrepLead`·`PrepSamples`, `NightContext.PrepLead`, `ShootWindow.Lead`, `PreparationPlan.PrepMinutes`(이동 단계는 계획에 쓴 값으로). 모의 장비는 기록하지 않음.
- 구도 회전: `Prepare/Tasks/Center/Framing.cs`(`FramingGoal`, `Framing.Remaining`, `AnglePerTurn` [실기 확인]), `CenterTask.FramingCheck`(측정값에 cameraAngle·frameDeg·frameTolerance, 안내 문장에는 숫자 없음), `PrepareFlow.SetFraming`·`CheckFraming`, API `/prepare/target/framing`·`/framing/check`, 모의 `SimulatedCenterDevices.UserTurns`. 웹 `components/FrameRotate`, `framing.ts`(`CAMERA_TURN_SIGN` [실기 확인]), `LiveView`가 사진 자리를 알림(`onPhotoBox`), 모의 사진 `simPhoto.ts`, 토큰 `--frame-void`.
- 확인 창: `ConfirmDialog` 문장 속 줄바꿈, 이어서 하기·지난 촬영 중단·적도의 홈·종료·프로필 삭제 문구(`NightSession.Pending`은 요약만, 앞뒤 문장은 화면이). 화면 문구에 이름을 직접 쓴 2곳 → `PRODUCT`.
- 용어 일괄(화면 글자 + DESIGN·OPERATION_PROCEDURE·FIELD_TEST_CHECKLIST, 조사 맞춤): 가이딩 → 가이드 약 90곳, 경통 → 망원경, 전원 배선 → 전원 설정. DESIGN 5장에 규칙(용어 · 이름 · 확인 창은 "~합니다").
- 종료 창(`QuitControl.module.css`): 원째 돌리면 각도마다 넘침 높이가 바뀌어 마지막 줄일 때 팝업 글 칸(스크롤 영역)이 168↔170px로 바뀌었다 → 테두리는 `.mark::before`가 돌고 칸은 `overflow: clip`, 체크 튀어나옴은 제 크기까지만.
- 문서: DESIGN(부팅 · 구도 회전 · 확인 창 · 용어), OPERATION_PROCEDURE(준비 시간 · 구도 회전), DECISION_HISTORY, FIELD_TEST_CHECKLIST(구도 회전 방향·링 눈금 추가, 폰 화면·[임시] 지우기 뺌). [계획] 폰 화면 폐기(사용자).

**확인**: 서버 시험 271 통과·1 건너뜀(새 `PrepTimesTests` 10 · `FramingTests` 6, 계약 시험의 `SlewResult` 모양, `NightSessionTests` 알림 형식 갱신), 웹 41 통과, lint 오류 0, 웹·데스크톱 빌드. 시험 서버(5211 — N.I.N.A. 자동 실행 끔, 빈 데이터 폴더, 장비 흉내): 부팅 차례와 입력 시점(Press Any Key 전 키는 무시), 키를 누른 뒤 애니메이션 값. 종료 팝업은 가짜 응답으로 마지막 줄이 "닫는 중"에 머물게 재현 — 고치기 전 원 각도마다 글 칸 168/168 ↔ 170/168, 고친 뒤 0~345° 내내 168/168. 구도 회전은 임시 페이지에서 끌기·안내·다시 찍기·되돌리기·적색 테마. 앱 전체 흐름 안의 실제 센터링 화면, 실장비의 회전 방향은 확인 못 함. 전체 시험 중 기존의 `다크_장수는_5장씩…`이 한 번 실패(따로·다시 돌리면 통과, 이번 변경과 무관).

**남은 것**: 현장 확인 — 구도 회전 방향 부호 두 곳과 링 눈금 짝, 준비 시간 기록이 쌓이는지. 남은 일을 꼽을 때 [임시] 버튼 지우기와 폰 화면은 넣지 않는다(사용자 — 지울 때는 사용자가 말함, 폰은 충분히 써 본 뒤 사용자가 검토). DeepSeek 연결은 사용자 검토 중(키 미정). 10-17 회사 노트북에서 큰 클래스 쪼개기(xhigh 권장) — 다크플랫 옮기기를 `ImageFolders.Move`로 합치는 것도 함께(리뷰가 끝나 미룰 이유 없음).

---

## 2026-10-15 · 8a22b55 [Claude] 웹 개발 도구 보안 알림 해결 (source-map-js)

**요청**: `npm install`이 띄우는 높음 등급 알림 1개 해결(집 PC).

**변경**: `web/package-lock.json`만 — 사용자가 `web`에서 `npm.cmd audit fix` 실행(PowerShell이 npm.ps1 실행을 막아 npm.cmd로). `source-map-js`를 고쳐진 버전으로(빌드 도구 안의 패키지 — 앱에는 들어가지 않음, GHSA-68fv-2mgg-jv7q). 다른 패키지 큰 버전 변경 없음.

**확인**: `npm audit` 0건, 웹 빌드, 웹 단위 시험 41 통과.

**남은 것**: 회사 노트북은 pull 뒤 `npm install`.

---

## 2026-10-15 · d7eaf38 [Claude] 부팅 · 프로필 · 점검을 한 장면으로 — 땅(구체)이 돌며 장비가 들어옴, Press Any Key·유성, 점검 그림 다듬기

**요청**: 앱에서 점검 그림을 보며 고침 — 적도의를 UMi 17X처럼(받침·위도 기둥이 삼각대 가운데, 기운 몸통 위 경사면에 짧은 새들), 깊은 캡이 가이드경은 위·주경은 아래로 열림, 누를 때 검은 사각형, 다리 사이 받침·나사 표시 지움, 전원 상자는 오른쪽·선은 사용자 그림 경로, 노트북 선 지움, 전원 질문은 상자 위, 별은 배경 별처럼·작은곰·북두칠성·카시오페이아(30% 넓게, 반 개 아래로), 하늘은 옆으로 밀지 말고 왼쪽 아래 화면 밖을 중심으로 돌며 맞춤. 부팅 로고 → 프로필 → 점검을 같은 땅·장비 장면으로(진행 표시 밑에서 땅이 잘리지 않게): 부팅은 장비 없이 하늘에 AIRA(별·선이 무작위로 빛남)·아이라·풀이·Press Any Key(저절로 넘어가지 않음, 나타나고 1초 뒤부터 입력)·유성, 키를 누르면 땅(구체)이 돌며 카메라 뒤의 장비가 들어옴, 잔디는 로고 풀처럼. 장비·노트북은 불투명하게. 숨은 요소를 잘라 두는 규칙을 DESIGN에.

**변경**:
- `PreflightScreen` → `components/RigScene`: App이 화면 전환 밖, 상태 줄 아래 틀 너비 전체에 깐다(`SCENE_OF` — 넘어가는 중인 다음 화면으로 boot·profiles·dim·check·out). 점검 로직은 그대로 옮기고, 카메라가 내려오는 들어오기 대신 프로필을 고르면 별이 켜짐. 이어서 하기는 `dive`로 노트북 화면에 바로 확대. 점검 중 화면 영역은 누르기를 장면으로 넘김(`.body[data-pass]`).
- 부팅: `assets/aira-wordmark.svg`(로고 오른쪽 요소를 세로로 — 별 `.tw`, 선 `path[data-a·b]`), `BootScreen`은 오류·화면 읽기 문구만. App `bootPrompt`·`BOOT_INPUT_DELAY_MS`: 로고가 다 그려지고(`LOGO_DRAWN_MS`) 목록을 받으면 아무 키(단축키·수정 키만은 제외)·누름으로 프로필. 유성(`meteors`). `assets/aira-logo.svg` 지움(쓰는 곳 없음, 원본은 mockups).
- 구체 회전(`SPIN_VP`·`roll`, rAF 2.6초): 땅 면·가장자리는 고정, 잔디 줄(`GRASS`, 깊이마다 한 줄)과 장비가 깊이만큼 같은 배율로 흐르고, 가장자리를 넘은 풀은 `pf-beyond`로 잘려 가라앉음.
- 하늘: `skyPan.setSkyTurn` + `SkyBackdrop .turn`(왼쪽 아래 밖 중심 6°, 별자리 그림과 같은 2.8초) — `setSkyIntro` 지움.
- 프로필 목록은 하늘 오른쪽 가운데 칸(좁은 창은 가운데), 새 프로필은 장면을 흐리게. `--bar-h` 토큰. `--art-screen`·`--art-screen-on`은 불투명(color-mix).
- 덤으로 고침: 엔진 켜기의 처음 5배 태양 · 새 프로필의 숨은 배선 패널이 스크롤 막대를 잠깐 띄우던 것(`overflow: clip`).
- 문서: DESIGN(장면·부팅·프로필·점검 그림·넘어가는 방식·배경, 레이아웃 안정성에 숨은 요소 자르기), DECISION_HISTORY.

**확인**: tsc·oxlint(새 경고 없음)·웹 시험 41·웹/데스크톱 빌드. 시험 서버(장비 흉내, 1920×1200)에서 부팅 대기(너무 이른 키는 무시) → 키 → 프로필 → 점검 네 가지(하늘이 돌아 북극성이 점선 끝에) → 노트북 확대 → 엔진 켜기, 이어서 하기 확대, 새 프로필 흐림, 16:9 창에서 땅이 창 끝까지. 시험 창이 숨겨져 프레임이 드물어, 움직임(구체 회전·별 켜짐·유성·캡 열림)은 중간을 멈춰 놓고 자리·방향만 확인 — 실제 속도·부드러움은 아이라 창에서 미확인. 검은 사각형이 사라졌는지도 사용자 확인 전.

**남은 것**: 잔디 모양은 일단 이대로(사용자가 보고 판단). 북극성을 내리며 극축 점선이 약 2° 덜 기울었음(적도의 몸통 기울기와 맞출지).

---

## 2026-10-15 · b55de8a [Claude] 출발 전 점검을 맨 앞으로 — 장비 선화 화면(시안 그대로), "이어서 할까요?"는 프로필 직후 · flow.mmd 지움

**요청**: 점검 시안 마무리 — "모두 확인 완료" 버튼 없앰([임시] 건너뛰기-성공은 전체 규칙이라 그대로), 방향 질문은 두 번째 문구, "색칠한 그림 스타일" 문구 지움, 이어서 하기 질문을 프로필 쪽으로, 진행 표시 순서 변경. 그리고 앱에 구현. 필요 없으면 flow.mmd 지우기.

**변경**:
- 시안 `mockups/aira-preflight-motion.html`: 버튼·관련 코드 없앰, 방향 문구, 설명.
- `PreflightScreen`(새로 씀): 시안의 선화를 그대로 — 전환 뒤 배경이 위로(카메라가 내려옴) · 장비가 아래에서 올라옴(누르면 끝으로), 지금 항목만 숨 쉬고 그 부분을 눌러(Enter·Space) 확인, 드로튜브·전원 불·극축 점선과 북극성(하늘이 왼쪽으로)·캡, 다 하면 노트북이 빛나고 역삼각형 → 누르면(Enter) 노트북으로 확대되며 다음. 질문은 SVG foreignObject라 그림과 함께 커진다. 색 토큰 `--art-*`(index.css 두 테마).
- `skyPan.ts`: `setSkyIntro` — 점검 연출 몫을 장비 연결의 태양 이동(`setSkyPan`)과 따로 더함(장비 연결이 배경을 되돌리지 않게).
- `App`: 프로필 → (`pendingSession`이 있으면 "이어서 할까요?") → 점검 → 설치 확인(`toSetup`) → … → 장비 연결 → 이어서 하기(`runResume`, 실패하면 다시 시도·새로 시작) 또는 장비 준비. 점검 화면에서 구석 태양·관측지 표시·N.I.N.A. 감시를 뺌(장비 연결 전). `StepRail.STAGES` 점검 → 연결 순.
- 문서: DESIGN(넘어가는 방식, 진행 표시, "출발 전 점검: 장비 선화" 절로 바꿈), OPERATION_PROCEDURE(1장 순서, 4장 네 가지, 이어서 하기), DECISION_HISTORY(시안 마무리·구현, 카드덱 폐기), FIELD_TEST_CHECKLIST. flow.mmd 지우고 가리키던 곳(README·ARCHITECTURE·NINA_API_COVERAGE·Codex 구현 요청서·App 주석)은 운영 절차로.

**확인**: tsc·lint(새 경고 없음)·웹 시험 41·웹 빌드. 시험 서버(장비 흉내)에서 프로필 → 점검 들어오기 → 네 가지 확인(그림이 바뀌고 V, 북극성이 점선에 걸림) → 노트북 → 엔진 켜기, 진행 표시에 점검 완료, 적색 테마 선화 색. 노트북 확대 순간은 캡처가 시간 초과라 눈으로는 못 봄(다음 단계로 넘어감은 확인). "이어서 할까요?"가 프로필 직후 나오는 경로는 오늘 밤 기록이 있어야 해 미확인, 현장 노트북에서의 동작 부드러움도 미확인(체크리스트).

---

## 2026-10-15 · bdce26a [Claude] 밝기 눈금(14비트·바닥값)을 시험 사진에서 — 배경 판정·플랫 50% · 노출 맞추기·틸트 절차 합의 · ASTAP 별 목록 확인

**요청**: ASTAP 별 목록으로 틸트를 볼 수 있는지 사용자가 보낸 X-T5 원본 FITS로 확인. 시험 사진 → 노출 맞추기 → 아홉 칸 틸트 → 본 촬영 절차를 합의해 OPERATION_PROCEDURE에(구현은 나중). 확인 중 찾은 "눈금 문제"는 지금 고치기.

**변경**:
- `Prepare/Real/CameraScale.cs`(새): 카메라의 포화 값·바닥값(그날 밤). `Fits.Analyze`가 사진에서 알아낸다 — 가장 밝은 값에 점이 몰리면 그 값이 포화 값, 아니면 12·14·16비트 표준 눈금. 바닥값은 후지 14비트 1024(INSTRUME로), 모르면 [임시] 0. 배경 = 바닥 위 비율, 히스토그램 가로 = 바닥~포화, 포화 = 포화 값 바로 아래까지(넘친 곳이 없으면 0).
- `RealTestShotDevices`가 눈금을 `CameraScale`에 남기고, `RealWrapDevices.FindFlatExposureAsync`가 그 눈금으로 플랫 50%를 찾는다(시험 사진이 없는 밤만 N.I.N.A. 비트 수). `PrepareSetup`에 등록.
- 문서: OPERATION_PROCEDURE ⑦ 밝기 눈금 규칙, [계획] 노출 맞추기와 틸트 1~5(계산으로 2~3장, 늘릴 때 상한 가이딩 없으면 120초·있으면 [임시] 300초, 읽기 잡음을 덮는 노출 기준, 아홉 칸 판정은 아이라·조절은 사용자, 조절 뒤 초점 확인 → 센터링 확인 → 확인 사진, OAG면 캘리브레이션 [확인 필요]), ASTAP `astap_cli -extract` 결과. 플랫 문장 바닥~포화 50%. DECISION_HISTORY, implementation/PREPARE, FIELD_TEST_CHECKLIST(ASTAP 항목은 확인돼 뺌, 밝기 눈금 실기 항목 추가).

**확인**: ASTAP CLI가 X-T5 원본 FITS(X-Trans, Siril 변환본)를 읽고 별마다 x·y·HFD·SNR·밝기 CSV를 냄 — 1.3초 별자리 사진에서 별 99개, 아홉 칸 HFD 8.1~9.4. 같은 사진 값 942~6701·중앙값 1075 → 14비트, 바닥 1024. 사용자 N.I.N.A. 프로필은 FreeImage + 비트 수 16(N.I.N.A. 문서상 FreeImage면 실제 비트 수) — 그래서 전에는 X-T5 배경을 4배 낮게 읽어 "배경 밝음"이 걸리지 않고 플랫은 50%에 닿지 못했다. 서버 시험 254 통과(`FitsTests` 5: 14비트·바닥 1024, 표준 눈금, 16비트, 플랫 비율, 못 읽음). 빌드 오류 0. N.I.N.A.로 저장한 X-T5 FITS의 실제 포화 값·바닥값·INSTRUME과 플랫은 실기 미확인(체크리스트).

**남은 것**: N.I.N.A. 카메라 비트 수를 14로 바꿀지는 사용자 결정(후지 플러그인이 이 설정을 쓰는지 집에서 확인). 노출 맞추기·틸트 절차 구현은 맑은 밤 딥스카이 사진으로 기준을 정한 뒤.

---

## 2026-10-15 · cd0d686 [Claude] 첫 정지 선화 시안 지움

**요청**: 처음 만든 시안(`mockups/aira-preflight-lineart.html`)은 지금 시안에 다 들어 있어 필요 없으니 지울 것.

**변경**: `mockups/aira-preflight-lineart.html` 삭제. 출발 전 점검 시안은 `mockups/aira-preflight-motion.html` 하나.

**확인**: 다른 문서가 지운 파일을 가리키지 않음(HISTORY 지난 항목의 이름만 남음).

---

## 2026-10-15 · a776c5e [Claude] 출발 전 점검 개편 시안 — 장비 선화 한 장과 모션, 점검을 프로필 다음으로 / ASTAP 시험 사진 링크

**요청**: 집 PC에서 시안만. 제미나이가 그린 장비 그림을 우리 선화로 옮겨 출발 전 점검 화면으로(카드 대신 그림 한 장). 점검을 프로필 다음으로 옮기고, 프로필을 고르면 밤하늘에서 카메라가 내려와 장비가 나타나는 모션, 다 끝나면 노트북 화면으로 확대해 다음 단계로. 여러 차례 고침(땅을 화면 끝까지·우하향, 굵은 삼각대 다리, 전원 왼쪽·노트북 오른쪽, 화면 넓게, 질문은 연결선으로, 그림을 눌러서만 확인, 극축 점선과 북극성 맞추기, 장비 80%). 회사에서 ASTAP 별 목록을 볼 FITS 사진 링크를 남겨 달라고 함(커밋은 나중에 → 이번에).

**변경**: `mockups/aira-preflight-lineart.html`(첫 선화 — 정지 그림 세 항목), `mockups/aira-preflight-motion.html`(지금 시안 — 맨 위 주석 참고), `docs/DECISION_HISTORY.md` 출발 전 점검(2026-10-15 결정, 아직 구현 전), `docs/FIELD_TEST_CHECKLIST.md` ASTAP CSV 확인 항목 + 구글 드라이브 FITS 링크(X-T5 원본, 56mm, 위치 정보 없음 — 저장소가 공개라 링크도 공개됨).

**확인**: 브라우저 창에서 단계 진행(프로필 → 하늘 → 등장 → 네 항목 → 노트북 빛남 → 확대 → 다음 화면), 지금 항목이 아니면 눌러도 반응 없음, 그림 면 누름, 정렬 확인 뒤 북극성이 점선 끝, 동작 줄이기, 적색 테마 색. 움직임의 속도·느낌은 브라우저 창이 가려지면 애니메이션이 멈춰 못 봄(사용자가 봄).

**남은 것**: 시안 계속 다듬기(사용자가 이어서 의견). 확정되면 구현 — 앱 화면 순서(프로필 → 점검 → 설치 확인), 진행 표시 순서(점검 → 연결), 새 항목 "적도의 전원", OPERATION_PROCEDURE 4장·DESIGN "출발 전 점검". 회사 노트북: 체크리스트의 FITS로 `astap_cli -extract` 확인.

---

## 2026-10-14 · 6ee399d [Claude] 남은 계획 정리 · 시험 사진 포화 비율 · 모든 단계 [임시] 건너뛰기-성공·실패

**요청**: "정해 두고 아직 만들지 않은 기능" 목록을 지금 구현과 대조해 검토. 사용자 결정 — 스켈레톤·패럴랙스·상태 줄 날씨·"▸ 자세히"·AI 부르기 버튼(실패 때 AI 등장 포함)·lift 높이 단계·휴대폰에서 보낸 대상·음성 "멈춰"·극축 정렬 실제 모드 등급 말·가진 필터 목록은 **흔적 없이 지움**(남겨 두면 다시 꺼내게 됨). 틸트·백포커스는 N.I.N.A.로 보내지 말고 아이라에서. 시험 사진 포화 비율은 바로. 건너뛰기는 모든 단계로 넓혀 성공·실패 두 버튼(더미 데이터, 실패가 의미 없는 곳은 성공만). 휴대폰 보기는 실촬영·미니 PC 경험 뒤 다시.

**변경**:
- 문서: 위 항목을 DESIGN·OPERATION_PROCEDURE·DECISION_HISTORY·FIELD_TEST_CHECKLIST에서 지움. AI 비서 규칙은 "계획 화면의 대화에서만"(실제와 맞춤). ⑦에 [계획] 틸트·백포커스(아홉 칸 별 크기 — 아래 답 참고). 극축 정렬 실제 모드는 등급 말 없이 방향·픽셀 값만(`RealPolarDevices`·`PolarTask` 주석).
- 포화 비율: `Fits.Analyze`(히스토그램과 포화 %를 한 번에 — 가장 밝은 값의 99% 이상인 화소 비율, 센서 비트 수를 몰라도 됨). `RealTestShotDevices`가 씀. `TestShotRules.SaturatedLimit` [임시] 1% 넘으면 노출을 줄이자고 함. `ShotStats.Eccentricity`를 null 가능으로 — 실제 장비는 재지 않으니 판정 문구에 "별 둥긂"을 쓰지 않음(전에는 재지 않은 "별 둥긂"·"포화 0.0%"가 나왔다).
- [임시] 건너뛰기: `PrepareRunner.TempSkipAsync(success)` 모든 작업 — 진행 중이면 멈추고 확인한 뒤, 끝났으면 다음 작업을 시작하지 않고(적도의를 움직이지 않고) 넘김. 성공 = 더미 성공 결과, 실패 = 그 작업이 실패해 건너뛸 때와 같은 결과(캘리브레이션·가이딩 실패는 `GuiderSkipped`). 포커서 위치처럼 장비를 움직일 값은 넣지 않음. 초점 확인은 성공만. `PrepView.TempSkip`, `/prepare/{group}/temp-skip?ok=`. `ShootSession.TempSkipAsync`(다 찍은 것·오류로 끝난 것 — 가이딩 정지·추적 끄기는 평소처럼 시도하고 확인 못 하면 이유), `PlanAssistant.TempSkip`(대상이 없으면 M31·M45·M42… 중 오늘 밤 찍을 수 있는 것 하나를 골라 확정). 웹 `components/TempSkip`(진행 표시 아래 종료 줄 위, `StepRail`의 `temp` 자리): 설치 확인·엔진 켜기·장비 연결·출발 전 점검·계획은 App 기본(성공만), 준비 작업·촬영은 화면이 알림. 관측지 고르기는 사용자가 고르는 일이라 없음.
- 기억(메모리): AI는 계획 화면에만, "N.I.N.A.에서 되니 아이라는 안 해도 된다"는 판단 금지, 지운 계획은 흔적 없이.

**확인**: 서버 시험 252 통과·1 건너뜀(새 `TempSkipTests` 8 · `FitsTests` 3 · 판정 1). 웹 시험 41 통과, tsc·lint(기존 경고 + 훅 내보내기 경고 2 — `useRailExtra`와 같은 방식)·웹 빌드·데스크톱 빌드 오류 0. 시험 서버(장비 흉내, AI 끔)에서 엔진 켜기 → 장비 연결 건너뛰기로 넘어감을 봄. 관측지 화면부터는 시험 서버에 N.I.N.A.가 없어 못 봄 — 계획 건너뛰기와 그 뒤는 실제 앱에서 미확인(체크리스트에 추가). 포화 기준 1%는 맑은 밤 사진으로 다듬기.

**질문 답 (사용자 질문, 2026-10-14)**:
- *7번(틸트)을 N.I.N.A. 플러그인이 검출한 정보를 아이라가 보여 주기만 하는 중계로는 못 하나?* → 지금은 못 한다. Advanced API(2.2.15)가 다른 플러그인 결과를 넘겨주는 것은 TPPA(극축 정렬)·Livestack·Target Scheduler뿐이고, Hocus Focus의 수차 검사(Aberration Inspector) 결과는 N.I.N.A. 안의 창에서만 보인다(변경 기록·웹소켓 문서 확인). 대신 아이라가 이미 솔빙에 쓰는 ASTAP의 `astap_cli -extract`가 사진에서 찾은 별의 정보를 모두 CSV로 내보낸다(이 노트북의 ASTAP 도움말로 옵션만 확인) → 별마다 위치·크기(HFD)가 들어 있으면 아이라가 아홉 칸을 비교하는 "중계에 가까운" 방법이 된다. CSV 내용은 별이 찍힌 FITS(N.I.N.A. 저장 형식은 FITS로 설정됨)로 확인 필요 — 이 노트북에 남은 실내 RAF는 ASTAP CLI가 읽지 못했다.
- *남은 작업 중 집 컴퓨터에서 HTML만 고칠 만한 것은 점검 카드 화면 개편인가?* → 맞다. 출발 전 점검을 장비 그림 한 장으로 바꾸는 것(그림 구상 중)은 시안(mockups/)부터라 HTML만으로 된다. 그 밖에는 틸트 결과를 보여 줄 화면 시안 정도. 팝업 문구는 상황을 보고 정하기로 했고, 구도 회전·휴대폰은 실기·경험 뒤라 지금은 없다.

**남은 것**: 다크플랫 옮기기도 `ImageFolders.Move`로(Codex 리뷰 뒤). 이 커밋은 Codex가 리뷰 중인 파일(`PrepareRunner`·`ShootSession`·`AppServer`)도 고쳤다 — 이어서 하는 리뷰는 기준 HEAD 2695a0d 그대로.

---

## 2026-10-14 · 8cb97f3 [Claude] 밤 그래프 — 세 대상이 같은 시각에 시작하면 세 번째 이름표를 옆으로

**요청**: 이름표 겹침은 "가"(옆으로 비켜 놓기)로. 선이 수직일 필요는 없음.

**변경**: `nightGraphMath.layoutTags` — 두 줄이 다 차면 덜 비키는 줄에서 오른쪽으로 비켜 놓고(`cx`), 그래프 오른쪽 끝(`R`)을 넘지 않게. `NightGraph` — 이름표·그림·누르는 자리는 `cx`, 점선은 시작점에서 이름표까지 비스듬히. 끌기는 누른 곳이 아니라 움직인 만큼(`w.start + 움직인 분`) — 비켜 있는 이름표를 눌러도 시작이 튀지 않게.

**확인**: 웹 시험 41개 통과(새 2개: 세 대상 같은 시각, 오른쪽 끝), tsc·lint(기존 경고만)·웹 빌드. 실제 화면 모양은 미확인(장비 흉내로 계획 화면까지 가야 함).

---

## 2026-10-14 · 3133087 [Claude] 웹 계산 단위 시험(vitest)과 시험 사진 "시험" 폴더

**요청**: 웹 계산 함수 단위 시험 — 작으니 바로. 시험 사진을 같은 밤 폴더의 "시험" 폴더로([계획] 항목).

**변경**:
- 웹 시험: `vitest` 추가, `npm test`. 화면 파일 안에 있던 계산을 따로 옮김 — `components/nightGraphMath.ts`(시각의 고도·고도 곡선·이름표 줄), `components/liveViewMath.ts`(사진 맞춤·초점 곡선 2차 맞춤), `shoot.ts`(`guideWord`·`dewText`). 시험 7개 파일 39개: 조사(받침·로/으로·제품 이름), 글자 수 byte, 그래프 시각·대상 이름, 밤 그래프 계산, 초점 곡선, 가이딩 판정·이슬 문구·시간 표시, 전원 배선 요약.
- 시험이 찾은 결함 2개: `clampBytes`가 이모지를 반으로 잘라 깨진 글자를 남김 → 이모지 앞에서 자름. 밤 그래프 다른 대상 툴팁 "○○로 바꾸기"가 받침을 안 봄(예: "성운로") → `withRo`.
- 시험 사진: `Prepare/Real/ImageFolders.cs`(새) — 저장된 사진을 같은 날짜 폴더의 다른 폴더로(…\날짜\SNAPSHOT\파일 → …\날짜\시험\파일, 종류 폴더가 아니면 그 아래, 같은 이름은 덮지 않음). `RealTestShotDevices.DownloadAndAnalyzeAsync`가 히스토그램을 읽은 뒤 옮기고 옮긴 경로를 돌려줌, 못 옮기면 SNAPSHOT에 두고 경고 기록.
- 문서: OPERATION_PROCEDURE(⑦ [계획] → 지금 규칙), DECISION_HISTORY, FIELD_TEST_CHECKLIST, implementation/PREPARE, README(시험 명령).

**확인**: 웹 시험 39개 통과, tsc·lint(기존 경고만)·웹 빌드·데스크톱 빌드 오류 0. 서버 시험 245개 중 244 통과·1 건너뜀(새 `ImageFoldersTests` 4개 포함). 기존에 시간에 민감하던 `다크_장수는_5장씩…`이 전체 실행 중 가끔 실패(따로·다시 돌리면 통과, 이번 변경과 무관). 실제 N.I.N.A.로 옮기기는 실기 미확인(체크리스트에 추가).

**남은 것**: 다크플랫 옮기기(`RealWrapDevices.MoveToDarkFlatAsync`)도 `ImageFolders.Move`로 — Codex가 그 파일을 리뷰 중이라 리뷰 뒤에. 밤 그래프에 세 대상이 같은 시각에 시작하면 이름표 1·3번이 겹침(줄이 두 개뿐) — 사용자 결정 대기.

---

## 2026-10-14 · 2695a0d [Claude] 팝업 공통 부품 `Modal` — 확인 창·설정·새 버전 알림을 한 껍데기로

**요청**: 팝업이 예쁘지 않고 재사용 구조인지 확인 못 했으니 팝업 모듈을 하나 만들자. 시안(`mockups/aira-modal.html`)을 보고 글자 +1px·크기 맞춤·기본 외곽선 조금 더 보이게·모든 버튼 호버. 팝업 안 문구는 상황별로 나중에.

**변경**:
- `web/src/components/Modal.tsx`·`.module.css`(새): `<dialog>` showModal + 유리 카드(반투명·뒤 흐림·옅은 외곽선), 크기 sm 470 / md 600 / lg 800, 제목 줄(제목 + ×), 버튼 줄, `tone="device"`면 `GlowFrame` 빛 테두리(적색 테마는 `still`), `dismissable`, `initialFocus`. 맨 위 층이라 앱 틀 배율(`useFrame`)을 직접 건다. 토큰 `--modal-*`(index.css, 두 테마).
- `ConfirmDialog`를 `Modal` 위의 얇은 껍데기로(`tone`·`size` 추가). 종료·홈으로 보내기 확인은 device, 이어서 하기·카메라 전원 확인은 md.
- `Settings`는 `Modal` lg(닫기는 버튼 줄로), 문구 2곳을 지금 동작(연습 대화 없음 → 대상 이름 검색)에 맞춤. `UpdateNotice` 서랍 → `Modal` lg. `GlowFrame`에 `still`.
- 뒤 흐림이 꺼지던 두 원인: ① 빌드(lightningcss)가 `backdrop-filter`와 접두어 중 뒤의 것만 남김 → 접두어를 먼저. ② 카드에 투명도 애니메이션이 걸려 있으면 그동안 흐림이 꺼졌다 툭 켜짐 → 카드는 면·흐림·그림자를 키우고 안의 글자만 투명도로.
- 문서: DESIGN.md(공통 부품 `Modal`, 설정·새 버전 알림), DECISION_HISTORY.

**확인**: tsc·lint(기존 경고만)·웹 빌드·데스크톱 빌드 오류 0. 시험 서버(5211, 장비 흉내)에서 설정·새 버전 팝업을 열어 기본·적색 테마 모양, 나타난 뒤 흐림(18px) 켜짐, Esc·바깥 누름 닫힘·안쪽 누름 유지, 1536×960(배율 0.8)에서 크기 640px·가운데 확인. device 빛 테두리(종료·홈으로 보내기)는 데스크톱 창·장비 화면에서만 떠서 실제 앱으로는 못 봄(시안에서만 확인).

**남은 것**: 팝업 안 문구는 상황마다 정식 문구로. Codex 리뷰 요청(65c0007..HEAD).

---

## 2026-10-14 · 686c7b2 [Claude] 자체 리뷰 반영 2차 — 극 찾기 다시 시도는 홈부터, 멈춤 확인은 다른 갱신끼리, Windows 종료 때 추적 끄기

**요청**: 결정 2(극 찾기 "다시 시도"는 시작 위치로 되돌린 뒤) 제안대로. 2차도 바로 — 장비가 없으니 N.I.N.A.가 값을 주는 기존 방식에 근거한 추정값으로, 집에서 그 값만 고칠 수 있게.

**변경**:
- 극 찾기: `PoleFindResult.Rotated`, `IPolarDevices.ReturnToStartAsync`(`RealPolarDevices` — Go Home 뒤 추적 항성, 홈 실패면 멈추고 확인 → `PolarReturn`). `PolarTask`가 돌린 뒤 실패해 다시 시도하면 먼저 홈으로, 못 가면 정지 확인 → "손으로 홈 근처에 두고 다시 시도 / 건너뛰기".
- 멈춤·도착 판정: `Prepare/Real/MountTiming.cs`(새) — [실기 확인] 추정값 한곳(정보 갱신 2초, 읽는 간격 2.4초, 멈춤 0.005°, 도착 0.01°). `NinaRig.StillAsync`·`WaitSlewAsync`가 항성시가 같은 읽기(같은 갱신)는 건너뛰고, 적위 그대로 + 적경 또는 시간각 그대로면 멈춤(추적 끈 채 멈춤도).
- Windows 종료·재시작·로그오프(`MainWindow.OnSessionEnding`): `MountHome.ForceSafeAsync`를 최대 10초 시도, 그동안 `ShutdownBlockReasonCreate`로 종료 화면에 이유. 막지는 않음.
- 문서: OPERATION_PROCEDURE(극 찾기 다시 시도, Windows 종료), DECISION_HISTORY, FIELD_TEST_CHECKLIST(갱신 간격 확인 방법 포함 3개).

**확인**: 서버 시험 237 통과·1 건너뜀(새 `MountTimingTests` 4개). 한 번은 전체 실행 중 `다크_장수는…`이 시간에 민감하게 실패 → 따로 3번·전체 2번 다시 돌려 모두 통과(이번 변경과 무관한 시간 경쟁으로 봄). 데스크톱 빌드 오류 0. 실장비·N.I.N.A. 갱신 간격·Windows 종료 동작은 실기 미확인.

**남은 것**: 집에서 `MountTiming` 값 확인. 종료 때 N.I.N.A.가 적도의 미연결이라 답하면 조용히 통과(이론상). 팝업 공통 모듈(사용자 요청). Codex 리뷰 요청(65c0007..HEAD).

---

## 2026-10-14 · d268791 [Claude] 자체 리뷰 반영 1차 — 장비 안전(추적 끄기·멈춤 확인·이동 승인)과 계획 v3 결함

**요청**: 계획 v3 구현(65c0007)과 장비 안전 경로를 Claude가 먼저 리뷰(계획 v3는 /code-review xhigh, 장비 안전은 영역 셋을 나눠 동시에 + 대조 확인) → 등급별로 보고 → 1차(장비 안전·겪음·쉬운 이론상) 반영. 결정 1(다시 들어오면 이동을 다시 묻기)은 사용자 승인. Codex 리뷰는 이 커밋까지 포함해 다음에 요청.

**변경** (장비 안전):
- 촬영 끝(`ShootSession.EndAsync`): 가이딩 정지 뒤 **추적 끄기**(`IShootDevices.TrackingOffAsync`), 못 끄면 경고. 촬영 중 PC 절전 막기(`Engine/KeepAwake` — PowerCreateRequest).
- "그래도 종료"(`QuitControl`)·화면 응답 없음·화면 죽음(`MainWindow`): `MountHome.ForceSafeAsync`(`/api/quit/force-safe`)로 가이딩 멈춤·추적 끄기를 한 번 시도, 못 끄면 한 번 더 알림. 촬영이 정지 미확인으로 끝났으면 종료 절차가 멈춤(`ShootSession.MountUnconfirmed`).
- 극 찾기: `AscomAxis.RotateRaAsync`가 끝 멈춤이 안 들어가면 실패(`StopNotSent`), 회전 시간은 시작 명령 전부터. `RealPolarDevices`가 실패·정지 미확인이면 드라이버 비상 정지 + N.I.N.A. 정지, 그래도 모르면 `PoleFindResult.StopUnconfirmed` → `PolarTask`는 "장비 상태 다시 확인"만. 건너뛰기 실패 문구를 적도의 정지로.
- 이동(`SlewTask`): 큰 이동은 누른 뒤에만(`PrepContext.MoveApprovalUsed` — 계획 확정 2분 안 한 번만 승인, 다시 들어오면 "○○로 이동"), 적도의 잠금을 잡은 뒤 멈춤 버튼, 실패하면 멈추고 확인한 뒤에만 다시 시도, 누르는 사이 15° 아래면 다시 기다리기. `Core.Josa.Ro` 추가.
- 캘리브레이션·센터링: 이동 실패를 멈추고 확인(`SlewOutcome`/`CenterAttempt.StopUnconfirmed`), 적도의 멈춤을 카메라·PHD2 멈춤과 동시에(`RealCalibrationDevices`·`RealCenterDevices`·`WrapDevices.StopAsync`).
- 다른 대상으로 계획에 돌아오면 추적만 끔(`MountHome.RestForPlanAsync`, `/api/prepare/plan/rest`, App `toPlan`). 홈 실행은 한 번만(`MountHome.Begin` Interlocked).

**변경** (계획 v3 — 겪음·이론상): 고도 끝은 내려갈 때만·끝 시각이 고도 한계면 "low", 반전·재초점 뒤 끝 조건 다시 보기(`ShootSession`). 지는 쪽 30° 아래 시작은 15°까지(`ShootWindow`), 시간대가 없어도 시작 되돌리기(`startMoved`). 연습 대화 삭제(`ScriptedChatModel`) — AI 한도·키 문제면 `practice` 이벤트로 그 자리에서 이름 검색. 반전 쉼은 찍는 동안 자오선을 지날 때만. 1분 상태가 새 편집을 덮지 않게(`changes`), 거절된 숫자 되돌리기(`NumberField revision`), 끌기 pointercancel, 모르는 필터 오류, 밤 정보 없으면 편집 거절, 검색 실패 처리, 한국어 검색은 다 차면 멈춤.
- 문서: OPERATION_PROCEDURE(계획·이동·극 찾기·촬영 끝·종료), DECISION_HISTORY, FIELD_TEST_CHECKLIST(5개 추가), README(AI 없음).

**확인**: 서버 시험 231 통과·1 건너뜀(새 시험: 이동 다시 묻기, 지는 쪽 시작, 반전 쉼. CX34_01은 새 동작으로 기대값 바꿈). 웹 타입 검사·빌드, lint 새 오류 없음(경고만), 데스크톱 빌드. 실장비·실제 종료·절전·N.I.N.A. 응답 지연은 실기 미확인(체크리스트에 추가).

**남은 것**: 2차 — 극 찾기 "다시 시도"가 같은 방향으로 또 60°(결정 2: 시작 위치로 되돌린 뒤 다시 제안, 사용자 답 대기), 정지 확인이 N.I.N.A. 같은 갱신 값을 두 번 읽는 문제(실기 확인과 함께), Windows 종료·로그오프 때 종료 절차. 종료 때 N.I.N.A.가 적도의 미연결이라 답하면 조용히 통과(이론상). 팝업 공통 모듈(사용자 요청).

---

## 2026-10-14 · 65c0007 [Claude] 계획 화면 v3 구현 — 대상 + 촬영 설정, 시작점 끌기, 이동은 촬영 시작 − 준비 시간, 레일은 지금 작업만 글자

**요청**: v3 시안대로 계획 화면 구현(집 PC). 하드코딩하지 말고 규격화·모듈화, 다른 단계에 영향이 없게. 레일의 글자 생략·툴팁은 모든 단계에. 구현 전 결정: AI는 말로 부탁하면 설정도 바꿈(화면 값 기준) · AI 없으면 이름 검색만(장소·날짜상 못 찍는 대상 제외) · 필터 칸 유지 · 총 노출 기본 180분 고정 · 미리보기는 DSS2 사진 · 이동은 B안(촬영 시작 − 준비 시간, 15° 이상, 30° 이동 금지 폐기) · 기본 시작·끝 고도 30° · 대상 그림을 끌어도 시작이 옮겨짐.

**변경**:
- 서버: `PlanRules`(기준값 한곳 — 30°·15°·준비 20분·쉬는 40초·기본 노출 표·필터·ISO 단계), `ShootWindow`·`ShootAmount`(촬영 시간대·장수 계산), `PlanTools` 재작성(화면 API와 AI 도구가 같은 Pick·Remove·ChangeSettings·SetStart, 도구 pick_target·remove_target·set_settings·set_start, 옛 도구 이름은 호환), `PlanAssistant`(AI 없으면 인사 없음, 오늘 밤 한 줄, 장비 바뀌면 기본값 그대로이던 설정만 갱신, 계획 고치기 잠금, 새 지시문), `PlanView`·`NightChart` 새 모양(일몰~일출, 달 위상, 정각 구름, 고른 대상 셋의 시간대·끌기 범위), `DsoCatalog.Search`(번호 앞부분·한국어 이름 `KoreanNames`), 새 API `/plan/search·pick·remove·settings·start`. `ShootingPlan.Picks·StartOverrides`, `PlanSettings.TotalMinutes·FilterKey·EndAltitude`, `PreparationPlan.MinAltitude`(옛 기록은 30°).
- 이동·촬영: `SlewTask` — 촬영 시작 − 준비 시간 전이면 기다리기(+ "지금 이동"), 15° 아래만 막음. `ShootSession` 끝 고도 = 계획의 MinAltitude.
- 화면: `PlanScreen` 재작성(대화 30% — v2 투명 틀·흐려지는 지난 대화·검색 결과, 오른쪽 오늘 밤 한 줄·프레임 미리보기·설명 40/60, 설정 줄 + 이동 버튼), `NightGraph`(옛 `NightChart` 대체 — 전환 0.24초, 시작점·대상 그림 끌기, 달 툴팁), 공통 부품 `Tooltip`·`Field`(NumberField·SelectField·ResetMark·StepButton), `StepRail` 글자는 지금 하는 것 하나·나머지 원 호버 툴팁, 그래프·대상 색 토큰(`index.css`, 적색 테마 포함), `withRo` 조사.
- 문서: DESIGN.md(진행 표시 규칙, 공통 부품, 계획 절), OPERATION_PROCEDURE.md(계획·이동 절, 촬영 끝 고도), DECISION_HISTORY.md.

**확인**: 웹 타입 검사·빌드·lint(새 오류 없음), 서버·데스크톱 빌드, 서버 시험 230 통과·1 건너뜀(새 시험: 시간대·장수 8개, 이동 대기 1개). 첫 실행에서 1개가 한 번 실패, 다시 5번은 모두 통과 — 시간에 민감한 기존 시험으로 보이나 어느 것인지 못 잡음. 시험 서버(5211) + 가짜 N.I.N.A.(프로필·카메라만) + 계획 화면만 띄운 임시 페이지(끝나고 지움)로: 한국어·번호 검색, 고르기·두 번째 대상·이름표로 바꾸기·목록에서 제거·다 빼면 처음 상태, 필터 바꾸면 기본 노출·ISO 따라감, − + 와 칸별 되돌리기, 시작점 끌기·되돌리기, 확정 값(시작·끝·장수·끝 고도), 레일 툴팁, 그래프 글자 겹침 없음, 적색 테마에서 빨강 외 색 없음. 고친 것: 설정 칸 입력이 칸 밖으로 넘침, 빨리 끌고 놓으면 시작 값을 잃음.

**남은 것**: 확인 못 함 — 실제 AI 대화로 대상·설정 바꾸기(시험 환경에 키 없음), 실제 N.I.N.A.·데스크톱 창, 이동 단계의 "기다리기 / 지금 이동" 화면 모습. 준비 시간 20분은 [임시](기록 평균은 나중). 한국어 이름은 잘 알려진 약 40개만. 구도 조정(센터링 뒤) 논의. 가진 필터를 프로필에.

---

## 2026-10-14 · f1d0993 [Claude] 계획 시안 v3 집에서 다듬기 — 그래프 정리, 시작점 끌기, 설정 줄 재배치

**요청**: 집 PC에서 v3 시안을 이어서 다듬기(구현은 하지 않음). 그래프·레일·설정 칸·버튼을 여러 차례 고침. 지평선 파일은 만들지 않기로(수고에 비해 얻는 것이 적음 — 대신 시작점 끌기).

**변경**: `mockups/aira-plan-v3.html`만 (파일 맨 위 주석 "2026-10-14 다듬기"에 정리). 크게:
- 레일: 지금 작업 이름만 글자, 다른 단계는 원에 호버하면 툴팁 — **구현 때 모든 단계에 적용**(사용자 결정)
- 그래프: 위 100°·아래 변 = 산 바닥(-5°), 지금 선 강조점, 달 30분 위상, 구름 정각, 대상 바꿀 때 전환, "총 노출 계획" 막대, 빈 그래프 안내
- **시작점 끌기**: 지금 대상의 시작점을 곡선 따라 끌어 시작 시각·고도를 정함(15° 이상 ~ 끝나기 10분 전, 위쪽 한도 없음). 고도는 관측지가 아니라 대상마다 다르므로 관측지 최소 고도 숫자 대신 이것으로
- 설정: 필터 | 게인·노출·총 노출(분) | 촬영 횟수 | 이동 버튼. 총 노출 = 노출 × 장수(쉬는 시간은 끝 시각에만). 추천 태그 대신 기본값 + 칸별 되돌리기. 게인(X-T5)은 ISO 단계 목록, 노출 1초 단위(버튼은 10씩). 필터·게인 목록은 위로 펼침
- 이동 버튼 이름 "○○로 이동" 유지(지금 앱도 누르면 바로 Go-To), 기다려야 하면 "○○ 기다리기 (시각)", 누르면 일어나는 일은 툴팁

**확인**: 브라우저 창에서 단계마다 위치(겹침·정렬)와 계산(촬영 횟수·끌기 범위·전환)을 스크립트로 확인. 시각·고도는 근사 계산(예시 값).

**남은 것**: 계획 화면 실제 구현(시작 전 사용자 결정 받기). 구현 때 함께: 이동 단계의 30° 고정을 계획의 시작 고도·시각으로(`SlewTask.UsableAltitude`), OPERATION_PROCEDURE 계획 절의 옛 버튼 이름("이 계획으로 준비 시작")·계획 4칸 문장 정리, DESIGN.md 레일 규칙.

---

## 2026-10-13 · ab4e215 [Claude] 계획 시안 v3 다듬기 — 지평선 가림 빼기, 고른 대상 셋을 그래프 위에

**요청**: 그래프 높이 표시를 "고도" 한 단어로, 봉우리는 각도만 + 아래 시각까지 수직 점선. 그래프와 설정 사이 간격·구분선. 이동 버튼을 사진 오른쪽 아래로, 대상 이름·설명은 사진 윗선에 맞춤. 지평선 가림(대상마다 바뀌어 헷갈림, 자료도 지어낸 값) 빼고 0~10°는 대상과 상관없는 고정 꾸밈. 고른 대상 셋을 칩 대신 그래프 위 그림·이름으로 함께, 지금 대상만 밝게, 시작 시각 문구는 빼기.

**변경**: `mockups/aira-plan-v3.html`만. 지평선 가림은 그림과 촬영 가능 계산(okAt)에서 모두 뺌. 바닥 먼 산 능선 두 겹(고정). 그래프 위 대상 이름표는 촬영 시작점(못 찍으면 봉우리)에서 점선, 겹치면 아래 줄, 누르면 그 대상으로. 이미지 크기는 키웠다가 사용자 판단으로 원래(300×200)로.

**확인**: 브라우저 창에서 대상 셋 고름·이름 눌러 바꾸기·위치 맞춤·콘솔 오류 없음을 스크립트로 확인.

**남은 것**: 지평선 파일(만드는 법·관측지별 보관)은 집에서 이어서 이야기. 계획 화면 실제 구현.

---

## 2026-10-13 · f1a8d53 [Claude] 계획 화면 개편 시안 (지금 구현 · v2 대화창 · v3 무엇을·얼마나)

**요청**: 회사에서 계획 단계 화면 개선. 지금 구현대로 시안부터, 이어 논의한 개편을 시안으로.

**변경** (시안만, 앱 코드 변경 없음):
- `mockups/aira-plan-current.html` — 지금 PlanScreen을 CSS·문구(연습 대화 순서) 그대로 옮긴 기준 시안. 이전 `aa-screens.html`의 촬영 계획 시안은 옛 설계라 따로 만듦.
- `mockups/aira-plan-v2.html` — 대화창을 외곽선만 있는 투명 틀(구석 태양 위), 반투명 말풍선, 지난 대화는 단계적으로 흐리게.
- `mockups/aira-plan-v3.html` — 계획 = 대상 + 촬영 설정. 구도·끝난 뒤·끝나는 조건 칸 없앰. AI 도우미 + 이름 검색(N.I.N.A. 대상 목록), 담기는 크기(hips2fits DSS2 사진, 실패하면 형상 그림) + 이동 버튼, 일몰~일출 그래프(천문 박명·지금·구름 아이콘·달 고도·고른 대상 최대 3개 선입선출·봉우리 고도와 시각·지평선 가림(N.I.N.A. 지평선 파일이 있을 때)·준비 시간 뒤 촬영 범위·자오선 반전), 필터·노출·ISO·모을 시간 = 장수와 끝 무렵 시각.
- `docs/OPERATION_PROCEDURE.md` 계획 절: [계획] 휴대폰(Stellarium 등)에서 보내 둔 대상을 추천 목록 맨 위에.

**결정(사용자와 논의)**: 구도는 계획에서 빼고 센터링 뒤에 따로(위치 이동·회전 안내 — 더 논의). 다크·플랫 늘 찍음. 끝 = 목표 장수 + 물리 한계(30°·새벽)는 결과로만. 구름은 촬영 시간 안에 바뀔 때만 한 문장. 하늘 밝기 자동 측정·보틀 표시는 보류(Codex·Gemini 검토 — 현장 판단을 바꾸지 않음). 버튼은 "○○로 이동".

**확인**: 브라우저 창에서 시안 상태 전환·값 계산·위치(사진 윗선과 이름, 아랫선과 버튼)를 스크립트로 확인. 시각·고도는 근사 계산(예시 값).

**남은 것**: v3 피드백 반영 후 실제 구현(PlanScreen·PlanAssistant 도구 정리·NightChart 교체). 관측지 메모(대화 중 적기·돌아보기·밤 기록)는 별개 기능으로 [계획] 정리 예정. 구도 조정(센터링 뒤) 논의. DESIGN.md·OPERATION_PROCEDURE 계획 절은 구현 때 함께 고침.

---

## 2026-10-12 · eba9a71 [Claude] 전원 허브(WandererBox)가 PC에 없으면 연결 전에 바로 실패

**요청**: 회사(장비 없음)에서 실행 — Empire가 "No CH340 device found" 창을 띄운 뒤에도 장비 연결 화면이 계속 "연결 중". 바로 실패 처리할 것. Empire 로고가 보였다는 것도 함께.

**변경**: `DevicePrecheck.UsbVendor` — 허브가 WandererBox(드라이버 Id에 Wanderer)면 CH340(VID 1A86)이 PC에 있는지 연결 전에 확인. 없으면 N.I.N.A.에 맡기지 않고 "전원 허브가 PC에 보이지 않습니다" + 전원·USB 케이블 안내. 다른 CH340 장치가 꽂혀 있으면 통과(예전처럼 N.I.N.A. 결과를 기다림).

**확인**: 로그 — 20:20:11 연결 시작, N.I.N.A. 실패 알림 20:22:15(약 2분). 테스트 `WandererBox_CH340이_없으면_바로_실패` 추가, 서버 테스트 221 통과·1 건너뜀. 이 노트북에 CH340 장치 0개 확인. 화면에서 다시 실행은 아직(다음 날).

**남은 것**: Empire 로고가 보인 시점 확인(미리 켜기 뒤 약 1분 감시 범위 밖인지, 사용자가 창을 바꿔 감시가 쉰 것인지). 2-2 창 호출 정리 후 창 숨기기 실기 확인.

---

## 2026-10-12 · 48ae4e9 [Claude] Gemini 6절 작은 정리 — 정지 확인 하나로, 창 Win32 호출 한 곳에, N.I.N.A. 숫자 읽기 하나로, 안 쓰는 주소 삭제

**요청**: Gemini 6절 판단대로 작은 항목 반영(사용자: 판단은 Claude 제안대로).

**변경**:
- 2-1: `NinaRig.WaitStillAsync`·`ConfirmStillAsync`가 `StillAsync(시간, 횟수)` 하나를 씀(판정·간격 그대로).
- 2-2: `Engine/Win32Windows` 새로 — 보이는 창 찾기·제목·주인 창·항상 위 + 창 다루기 호출. `BackgroundWindows`·`NinaToastHider`·`NinaAutofocusWindowHider`·`Phd2Windows`의 중복 선언 삭제. 창 캡처(`LiveImages`)·데스크톱 쪽은 성격이 달라 그대로.
- 2-3: `EquipmentConnector`의 `Num`을 지우고 `NinaRig.Num`(문자열 "NaN"·숫자 문자열도 읽음)으로. `Text`는 빈 문자열을 null로 보는 차이가 있어 그대로. 자리를 잘못 잡은 허브 USB 설명 주석을 `EnsureHubUsbOnAsync` 위로.
- 4-2·4-3: `/nina/simulate-exit`(+`NinaWatcher.SimulateExit`)·`/prepare/sim/speed` 삭제.
- 3-1: ARCHITECTURE.md 3절에 "자동 진행 4초는 화면만 정한다" 한 줄. PREPARE.md 모의 주소 목록에서 `/sim/speed` 뺌.

**확인**: 서버 테스트 220 통과·1 건너뜀, 데스크톱 빌드 오류 0. 창 다루기(N.I.N.A. 알림·자동초점 창 숨기기, 미리 켤 때 뒤로 보내기, PHD2 연결 창 감지)는 실기로 다시 보지 않음 — 다음 실행 때 평소처럼 보이면 됨.

**남은 것**: 4-4(TempFailButtons·forceFail)는 배포 준비 때 개발 모드 전용으로. 1-1~1-3 큰 클래스 쪼개기는 2026-10-17~18. 4-1(`RunnerSetup.Single`은 생성자 기본값)·3-2는 그대로.

---

## 2026-10-12 · 52924a8 [Claude] Codex 34절 반영 — 남은 멈춤 명령 추적, 프로필 화면 × 종료 정리, 전체 화면 사진 잘림

**요청**: Codex 34절 리뷰 검토·반영(사용자 승인 전에 먼저 고쳐 보고 → 승인 후 커밋).

**변경**:
- CX-34-01(장비 안전): `AscomAxis.CommandAsync` — 멈춤 worker를 `_stops`에 기억, 남아 있으면 새 이동 거절. 멈춤 개수 제한은 비상 정지를 막을 수 있어 넣지 않음.
- CX-34-02(겪음): `App.tsx` `prelaunchedPhase` — 프로필·새 프로필 화면도 창 ×가 종료 절차. 미리 켜기와 종료의 직렬화는 넣지 않음(이론상, 요청이 화면 진입 즉시 끝남).
- CX-34-03(겪음): `PrepareScreen.module.css` `.fullPhoto` 트랙 minmax(0,1fr), 사진 100% + contain.
- 문서: OPERATION_PROCEDURE.md 허브 실패 보류 범위, 종료 창 "홈으로 보내기" 삭제, 프로필 화면 × 절차.

**확인**: 서버 테스트 220 통과·1 건너뜀(CX34_01 추가). 웹·데스크톱 빌드 오류 0. 실제 종료·사진 화면·실장비는 미확인.

---

## 2026-10-12 · b38880b [Claude] Gemini 6절 리뷰 넣기, 작성자 표시에 [Gemini]

**요청**: Gemini 구조 리뷰(38e1744 기준) 답변을 문서에 넣고 검토. 판단은 Claude 제안대로.

**변경**: `docs/codex/REVIEW_GEMINI.md` 6절 + Claude 대조 메모. HISTORY의 [Gemini] 항목은 Gemini 제안 문구 그대로. CLAUDE.md·HISTORY 머리말 작성자 표시에 `[Gemini]`. Codex 34절 리뷰(REVIEW_CODEX.md·[Codex] 항목)도 함께 커밋.

**확인**: Gemini가 인용한 줄 수·줄 번호를 코드와 대조 — 맞음. 틀린 곳: 4-1 `RunnerSetup.Single`은 생성자 기본값(유지), 3-1 인용 코드 모양, 안전 메모 2건(PHD2 쓰기는 이미 `_writeGate`, `OnClosing`은 취소 후 10초 제한 정지).

**남은 것**: 2-1·2-2·2-3·4-2·4-3 + 3-1 문서 한 줄은 다음 커밋. 4-4는 배포 준비 때(개발 모드에서만). 1-1~1-3 큰 클래스 쪼개기는 2026-10-17~18 주말. 3-2는 그대로.

---

## 2026-10-12 · [Codex] aa94dde..38e1744 리뷰 — 32절 수정 재확인·미리 켜기·사진 표시

**요청**: 새 문서 지도와 HISTORY를 먼저 읽고 `aa94dde..HEAD` 중 `0ba12fb`·`e8fe931`·`daa27c3`의 동작 변경 리뷰. 33절 재리뷰 칸·새 34절 작성, 코드·기존 테스트 수정 및 실장비 조작 없이 확인.

**변경**: `docs/codex/REVIEW_CODEX.md` 33절 4건 재리뷰 기록과 34절 추가. **P1/장비 안전 1건**(CX-34-01: 시간 초과된 정지 worker가 다음 회전에 끼어듦), **P2/겪음 2건**(CX-34-02: 프로필 화면 × 종료 시 미리 켠 프로그램 정리 누락, CX-34-03: 촬영 사진 전체 화면의 아래쪽 잘림). 운영 절차의 허브 실패 보류 범위·종료 홈 선택 문장도 문서 갱신 필요로 구분.

**확인**: HEAD `38e1744f809b281804b42229d03e8c7358c78374`를 저장소 밖에 내보냄. 기존 모의 테스트 32건 통과·웹 타입 오류 0. 실제 AscomAxis + 가짜 드라이버로 늦은 정지/새 회전 교차, host.ts + 가짜 브리지로 즉시 닫기 경로, 실제 CSS + headless Edge의 가짜 사진으로 화면 넘침 재현. 지난 32절 원 증상 검사 모두 통과하나 정지 worker 추적은 보완 필요. 앱 코드·기존 테스트·실장비·촬영 프로그램은 수정·조작하지 않음.

**남은 것**: 새 지적 3건의 반영 판단·수정. 실제 COM 지연, 외부 프로그램 종료, DWM·사진 수신·화면 움직임은 재시험하지 않았으며 미검증 자체를 결함으로 세지 않음.

---

## 2026-10-12 · [Gemini] 구조·단순화 리뷰 (커밋 38e1744 기준)

**요청**: 최신 코드(커밋 38e1744) 기준 대형 파일(EquipmentConnector·EquipmentScreen·PrepareRunner) 분리 방안, 분산 중복 로직 통합, OPERATION_PROCEDURE.md 대비 구조 정합성, 정리 대상 코드 검토.

**변경**: 리뷰 문서 작성(코드 수정 없음). 
- 대형 파일 분리: EquipmentConnector(독립 옵션/상태 클래스 4개 분리, 허브 전원·PHD2 검증 서비스 분리), EquipmentScreen(장비 목록/전원 모달 및 궤도 렌더링 분리), PrepareRunner(15초 정지 조정자 및 RunHandle 분리, 락/토큰 주의사항 명시).
- 중복 로직 통합: NinaRig 정지 확인 루프 단일화, Win32 창 열거 P/Invoke 통합, N.I.N.A. JSON 파싱 헬퍼 공통화.
- 미사용 코드 확인: RunnerSetup.Single 데드코드 발굴, /nina/simulate-exit 및 /sim/speed 미사용 엔드포인트 확인, TempFailButtons 및 forceFail 인자 연쇄 정리 제안.
- 4단계 점진적 안전 리팩터링 로드맵 수립.

**확인**: 실제 소스 파일 줄 수(EquipmentConnector 855줄, EquipmentScreen 862줄, PrepareRunner 928줄) 및 임포트 함수(solarLayout), docs 파일 14개 전수 대조 완료. 모든 지적에 파일 경로, 줄 번호, 코드 1~3줄 인용 근거 부합.

**남은 것**: 제안된 리팩터링 1단계(미사용 코드·임시 버튼 제거)부터 사용자 승인 후 순차 적용.

---

## 2026-10-12 · 38e1744 [Claude] docs/ 이름·폴더 정리(대문자 + 밑줄, implementation/·reference/), AGENTS.md 문서 지도

**요청**: 문서 이름을 검토해 알맞게 바꾸고 필요하면 폴더로 나누고 대소문자 규칙을 맞추기 → Codex가 헤매지 않게 알려 줄 것.

**변경**: `git mv` — DESIGN_HISTORY → DECISION_HISTORY(디자인·운영 결정 둘 다), PREPARE_IMPLEMENTATION·SHOOT_IMPLEMENTATION·PRECHECK_DESIGN·POWER_LAYOUT_PLAN·onboarding-and-architecture → `docs/implementation/`(PREPARE·SHOOT·PRECHECK·POWER_WIRING·ARCHITECTURE), api-coverage·nina-prerequisites → `docs/reference/`(NINA_API_COVERAGE·NINA_SETUP), codex/AIRA_ERROR_VERIFICATION → codex/ERROR_VERIFICATION_CODEX. 마크다운 링크 32개를 새 위치로 다시 계산, 글·코드 주석 속 옛 이름(27개 파일)을 새 경로로. 옮긴 문서의 제목을 새 역할에 맞춤(ARCHITECTURE는 "초기 결정 기록" 안내). README 문서 목록, CLAUDE.md `docs/` 구성. **AGENTS.md**: "문서 위치" → "문서 지도"(어떤 질문엔 어떤 문서, 문서와 코드가 다를 때의 기준, 옛 경로 대응), 개발 단계 문장을 "실장비 실내 시험 중"으로.

**확인**: 깨진 마크다운 링크 0개(docs/codex 제외 전체 검사), 경로 겹침 없음. 코드는 주석만 — 서버 빌드·웹 타입 검사 통과. **동작 변경 없음.** HISTORY.md 지난 항목·conversations/·Codex가 쓴 docs/codex 본문은 옛 경로 그대로(기록·Codex 소유).

---

## 2026-10-12 · 9b9cebf [Claude] DESIGN.md는 화면만, 아이라가 하는 일은 docs/OPERATION_PROCEDURE.md로 나눔

**요청**: DESIGN.md 구현 명세를 걷어 내다가(극축 정렬 시범) "DESIGN은 비주얼 문서가 아니었나" → DESIGN에는 비주얼만, 새 문서 이름은 operation procedure. 초기 기획서 docs/nina-ai-assistant-plan.md 지우기.

**변경**: DESIGN.md 다시 씀(313줄·약 47KB, 78KB에서) — 원칙 → 톤과 분위기 → 화면 틀(진행 표시·상태 줄·공통 부품·하늘 화면) → 화면별(부팅 → … → 종료 창) → 용어 → 테마 → 글꼴 → 움직임 → 하지 말 것. 새 `docs/OPERATION_PROCEDURE.md`(222줄) — 원칙·장비 안전 → 단계 → 시작·연결 → 관측지·망원경·프로필 데이터 → 출발 전 점검 → 장비 준비·대상 ①~⑦(계획 포함) → 촬영 → 마무리 → 적도의 홈 → 앱 전체(종료·다른 창·N.I.N.A. 알림·설정·새 버전·AI). 극축 정렬의 API 순서·이유는 PREPARE_IMPLEMENTATION.md로. 장 번호가 바뀐 곳과 옮긴 절을 가리키던 코드 주석 71곳을 새 위치로(화면 코드 → DESIGN, 서버 동작 → OPERATION_PROCEDURE, PRECHECK_DESIGN.md 인용은 그대로). README·CLAUDE.md·AGENTS.md·onboarding·api-coverage·구현 문서·체크리스트 링크 갱신, CLAUDE.md에 "동작을 바꾸면 OPERATION_PROCEDURE.md도". docs/nina-ai-assistant-plan.md 지움. DESIGN_HISTORY.md에 분리 기록.

**확인**: 코드는 주석만 바뀜 — 웹 타입 검사·서버 빌드 통과. 남은 [확인 필요]는 계획 화면 그래프(보류) 하나.

**남은 것**: 없음(내용 정리는 이번으로 일단 끝).

---

## 2026-10-12 · 91a72c3 [Claude] PREPARE_IMPLEMENTATION.md를 지금 코드 설명으로, 실기 확인 체크리스트 분리

**요청**: PREPARE_IMPLEMENTATION.md를 고치기(2026-10-05 "구현 계획 · 착수 전" 그대로였음) → 197줄도 많다 → 코드와 겹치는 세부는 줄이고 실기 확인은 체크리스트로.

**변경**: `docs/PREPARE_IMPLEMENTATION.md` 다시 씀(150줄) — 세 묶음(장비 준비·대상·마무리)과 의존표, 작업 계약(`IPrepTask`·`ITaskRun`)·끝 상태 약속·결과 기록, 공통 규칙(RunId·멈춤 확인·적도의 잠금·다시 하기·이어서 하기·[임시] 건너뛰기), API, 실제 어댑터(`NinaRig`·`Phd2Client`·`SharpCapBridge`·`AscomAxis`·`OasisSdk` 등), 작업별로는 코드만 봐서는 알기 어려운 결정만, 판정·저장·테스트. 새 `docs/FIELD_TEST_CHECKLIST.md`(장비 안전 / 실내에서도 되는 것 / 맑은 밤 장비 준비·대상·촬영·마무리 / 아직 만들지 않은 것). CLAUDE.md 문서 목록에 체크리스트.

**확인**: 코드와 대조해 옛 계획 문서의 틀린 설명을 바로잡음 — 센터링은 N.I.N.A. `slew?center=true`가 아니라 아이라가 사진 + 솔빙 → 동기화 → 다시 이동을 반복(`RealCenterDevices`), 어댑터 이름(`SharpCapController`·`AscomDirect` → `SharpCapBridge`·`AscomAxis`). 체크리스트는 HISTORY의 "확인 못 함"에서 모음. 코드 변경 없음.

**남은 것**: DESIGN.md 준비 화면 ①~⑦의 구현 명세 걷어 내기, DESIGN.md 위계 정리.

---

## 2026-10-12 · 7f50816 [Claude] DESIGN.md를 지금 유효한 규칙만으로 정리, 이력은 docs/DESIGN_HISTORY.md

**요청**: DESIGN.md가 길고(507줄·106KB, 날짜 기록 126곳) 지금 규칙과 이력이 섞여 있음 → 유효한 규칙만 남기고 이력·폐기된 규칙은 분리(이름 DESIGN_HISTORY, 위치 docs/). 애매한 6가지는 사용자 결정: 단계=행성 앱 전체 적용 폐기, 레일에서 완료 단계로 돌아가기 폐기, 계획 화면 그래프 재검토 보류, "정보는 오른쪽 맨 위 한 줄에만" 삭제, 자동초점 설정 계산·쓰기 계획 삭제, WindowChrome 규칙 삭제.

**변경**: DESIGN.md 다시 씀(464줄·약 78KB, 본문 날짜 0) — 옛 문장이 지금과 어긋난 곳은 코드와 대조해 지금 동작대로(망원경 아이콘, 장비 변경·관측지 한 줄, 다른 창은 "항상 위", 작업 순서·끝 버튼, 보라 강조색 등). 표시 [임시]·[계획]·[확인 필요]. 절 구조: 진행 표시·상태 줄·공통 부품·화면별·준비 ①~⑦·종료 등. 새 `docs/DESIGN_HISTORY.md`(같은 절 제목 아래 날짜·이유·폐기). 옛 절 이름을 가리키던 코드 주석("단계 재구성"·"촬영 준비"·"프로필 화면" 등)을 새 이름으로. CLAUDE.md에 "DESIGN.md는 현재 규칙만, 이력은 DESIGN_HISTORY" 한 줄과 문서 위치.

**확인**: 남은 [확인 필요]는 계획 화면 그래프(보류) 하나. 코드는 주석만 바뀜, 웹 타입 검사 통과. 정리 전 원문은 git daa27c3.

**남은 것**: 크기가 26%만 줄어든 것은 남은 내용이 구현 명세(API 순서·실패 예외·수치)에 가까워서 — 다음에 구현 명세를 docs/PREPARE_IMPLEMENTATION.md·SHOOT_IMPLEMENTATION.md로 옮기고 위계를 다시 정리하기로.

---

## 2026-10-12 · daa27c3 [Claude] N.I.N.A.가 아이라를 덮던 것, 미리 켜기는 프로필 화면에서, 출발 전 점검 3장(포커서 0점 매번), 케이블 안내는 회전 직전, 점검 카드 잔상 줄이기

**요청**: 아이라를 켜자 N.I.N.A.가 아이라 위에 떠 가림 → 고치거나, 어려우면 프로필을 고르면 켜고 뒤로 가리기. 점검 카드가 사라질 때 아이콘 잔상이 심함. 이어서 점검 항목 논의 — 케이블·밸런스·삼각대 수평은 장비를 세우며 빠질 수 없는 일이라 묻는 의미가 적다(사용자), 포커서는 늘 떼어 케이스에 넣고 WIDF 노브를 돌리면 드로튜브가 움직이므로 0점은 매번.

**변경**
- `BackgroundWindows` 지키기 루프: 아이라 "항상 위"를 내내 확인해 창이 잡히는 대로 건다 — 시작할 때 아이라 창을 아직 못 찾아(0) 아무것도 안 하던 것이 원인
- 미리 켜기: `App.tsx` 로고 대신 프로필 화면(profiles·newProfile)이 처음 뜰 때 `/api/engine/prelaunch`
- 점검 카드: 합성 층(`will-change`), 넘긴 카드의 그림은 곧바로 사라지고 V는 45% 안에(`vOut`)
- `PreflightScreen` 항목 3장(시작 위치 → 포커서 0점 → 망원경 캡), 질문 손봄, `/api/focuser/parked` 확인 없앰. 서버 `/prepare/focuser-zero`는 늘 0점 잡기, `/focuser/parked` 없앰(`FocuserPark.ReadyAsync`는 기록 확인용으로 남김)
- 케이블 안내: `PolarTask` 극 찾기, `CalibrationTask` 위치 이동, `ShootScreen` 자오선 반전 문장 (대상 이동은 원래 있음)
- DESIGN.md 점검 절(항목·뺀 이유·포커서 0점 매번·그림 시안 예정), 미리 켜기 시점

**확인**: 실기 — 아이라를 켜고 N.I.N.A.·PHD2·Empire가 켜지는 약 60초 동안 맨 앞 창·화면 가운데 창이 계속 아이라, N.I.N.A. 응답 뒤에도 아이라(끝난 뒤 모두 닫음). 메모리 — 아이라 묶음 약 2GB(Empire 709MB), 노트북은 아이라 전부터 15.7GB 중 12.8GB 사용. 서버 테스트 219 통과·1 건너뜀, 웹 타입 검사·빌드. 확인 못 함: 카드 잔상(사용자가 일단 보류), 3장 점검 화면 실물.

**남은 것**: 점검 화면은 사용자가 그려 올 장비 그림 시안으로 바뀔 수 있음. DESIGN.md 정리(현재 규칙만 남기고 이력은 따로 — 106KB, 날짜 기록 126곳) 제안함. Empire 연결 시간은 사용자가 데이터를 더 모음.

---

## 2026-10-11 · e8fe931 [Claude] 장비 연결 빠르게(N.I.N.A.·PHD2·Empire 미리 켜기), 태양·행성 불투명 회보라, 사진 틀·촬영 사진 전체 화면, 화면 다듬기

**요청**: 실내 확인 뒤 — 장비 연결 태양 외곽선 두 줄, SharpCap 영상에 "Snapshot captured" 줄이 보이고 중간에 내부 화면으로 바뀜, PHD2·파워박스 연결이 느림(COM 포트를 훑나?), 초점 "[임시] 건너뛰기", 배경 별이 순간적으로 여기저기 반짝임, 점검 카드가 사라질 때 아래 카드와 어색하게 겹침, 외부 영상 화면에서 진행 표시 영역만 색이 달라 잘려 보임, 초점·시험·촬영 사진이 화면을 다 덮음 → SharpCap 영상과 같은 크기로, 촬영 사진 누르면 전체 화면. 이어서: N.I.N.A. 켤 때 태양이 멀어지다 버벅임 → 흐림 끄기, 아이라를 켤 때 N.I.N.A.를 바로 켜기, 태양·행성을 투명 대신 색으로(시안 → 1안 회보라 단색, 행성 글로우는 1-a로 줄이되 연결 반짝임은 그대로).

**변경**
- 연결 속도: `EngineStarter.PreLaunch`(새 `POST /api/engine/prelaunch`, App이 로고 때 부름) — N.I.N.A.·PHD2·Empire를 꺼져 있을 때만 켜고 `NinaLaunched`로 표시(종료 때 함께 닫힘), 엔진 켜기는 "아이라가 켜질 때 함께 켰습니다"로 준비를 기다림. 엔진 켜기의 `WarmUp`(PHD2·Empire). `HubInputVoltsAsync` — 전압 칸이 없으면 바로(매번 2초 기다리던 것)
- 태양 외곽선 한 줄: `.sun .ring[data-surface][data-state=working]::before` inset 0
- SharpCap 썸네일: `WindowThumbnail` — 같은 창에서 영상 칸을 잠깐 못 찾으면 3초까지 앞 자리 유지(`_lostAt`). 바로 "안 보임"이면 화면이 대체 사진으로 바뀌고 스냅샷 저장마다 SharpCap이 "Snapshot captured" 줄을 띄웠다
- 초점 [임시] 건너뛰기: `PrepareRunner.TempSkippable`에 focus(결과는 손으로 맞춤과 같게, 위치 저장 없음), `PrepareScreen` `TEMP_SKIP`
- 반짝이는 별: `SkyBackdrop` — 한 번씩 돌고 `animationend`에서 새 자리로 다시 시작(도는 중 길이만 바꾸면 진행 위치가 다시 계산돼 중간 밝기로 툭 나타났다)
- 점검 카드: 0.64초·76px 위로, V가 먼저 사라짐(`vOut`), 아래 카드는 0.14초 뒤(`transition-delay`), `CARD_MS` 640
- 진행 표시 밑 어두운 띠: `App.module.css` `.body[data-sky] .rail` 그라데이션
- 사진 틀: `LiveView` `FramedImage`(`WindowView.slotOf`로 글자 사이 띠, 비율 유지, `GlowFrame`), 촬영 화면 `FullPhoto`(portal, 누르거나 Esc), `PrepareScreen .over` pointer-events none(아래 사진을 누를 수 있게)
- 엔진 켜기 버벅임: 멀어지는 동안 이글거림 SVG를 감췄다가 1.5초 뒤 나타남(흐림 필터 다시 그리기) — 원에는 원래 유리 흐림이 없었다(앞에서 흐림 탓이라 한 말 정정)
- 색: `index.css` `--sun-fill`·`--planet-fill(-strong)`·`--planet-line`·`--planet-glow`(두 테마), 장비 연결 항성·행성·`fromRing`·`CornerSun`·엔진 켜기 원(`padding` 지름 4% + content-box)에 적용, 행성 뒤 흐림 없앰. 시안 `mockups/aira-body-colors.html`
- DESIGN.md 원 색·연결됨 빛·카드 넘김·사진 자리·미리 켜기

**확인**: 실기 — 아이라만 켜자 N.I.N.A.·PHD2·Empire가 1초 안에 켜지고 25초 뒤 N.I.N.A. 응답, 장비 연결 55초 → 25초(파워박스 36 → 10초, 가이더 11 → 6.8초, 허브 전압 2초 → 8ms). 장비 연결 중 태양 외곽선 한 줄, 엔진 켜기 원·행성 불투명 회보라 화면 확인. 아이라 종료로 세 프로그램이 함께 닫힘. 서버 테스트 219 통과·1 건너뜀, 웹 타입 검사·빌드. 확인 못 함: SharpCap 썸네일 3초 유지, 반짝이는 별·카드 넘김·엔진 버벅임 체감, 사진 틀·전체 화면(별 사진이 있어야), 촬영 테마의 새 색.

**남은 것**: 실내 시험 중 홈 버튼은 준비 작업 중 잠금이 정상(준비 중단 뒤 풀림).

---

## 2026-10-11 · 0ba12fb [Claude] Codex 32절 반영, PHD2 카메라 자리 고치기 정규식, 태양 연출(늘 도는 표면·시차 이동·구석 태양), SharpCap 창 깜빡임

**요청**: 실내 장비 확인 — X-T5 정지(중단 없이 기다림) 실기 확인, "PHD2 또 안 됨", 장비 연결에서도 태양 회전·N.I.N.A. 실행 뒤 태양과 배경 속도를 다르게(배경이 더 느리게, 가속·감속)·이후 단계에도 왼쪽 위 태양 유지(제목 아래 층, 장비 연결 뒤 80%만 보이게), 엔진 켜기 원이 안 도는 듯·장비 연결 이글거림 사라짐·구석 태양 이글거림이 원 안쪽까지, SharpCap 켜질 때 하얀 창. Codex 32절 4건 모두 고치기.

**변경**
- CX-32-01(장비 안전, 반영): `AscomAxis.RotateRaAsync` — 시작 명령을 기다리다 포기하면 표시(`LateStart`)를 남겨, 늦게 풀린 시작 명령은 돌리지 않고 이미 돌렸으면 스스로 멈추고 드라이버를 놓음. 포기할 때 멈춤도 따로 보냄. `CommandAsync(stop: true)` — 멈춤·비상 정지는 앞 명령이 막혀 있어도 거절하지 않음. 시험용 `Factory`·`CommandLimit`
- CX-32-02(겪음, 반영 — 실기에서 먼저 겪음): `Phd2CameraRepoint.UsbId` 정규식의 `[^#\]` 이스케이프, `EquipmentConnector.TryRepoint`가 예외를 잡아 원래 안내로
- CX-32-03(이론상, 반영): `NinaRig.AbortExposureAsync` — 카메라 정보 답이 없으면 "모름"(`CameraInfoAsync` null)이라 정지로도 후지 아님으로도 보지 않음, 후지 판별은 첫 응답으로(`IsFujifilm`). 노출 끝 뒤 2초는 그대로(받기 끝을 알리는 값이 없음)
- CX-32-04(겪음, 반영): `FocusTask` — 넓게 찾기 실패 시 "지금 위치에서 자동초점"/"초점을 맞췄어요", 지금 위치에서는 다시 넓게 찾지 않고 손으로 맞춘 위치는 저장하지 않음
- 태양: `SunSurface`/`JarvisRing` `spin="always"`(엔진 켜기 원·장비 연결 항성·구석 태양), 새 `CornerSun`(App의 화면 영역 아래 층 `.sunLayer`, 다음 화면이 나타날 때 보이는 부분 80%로, `heading`으로 앞 화면이 사라질 때부터), `skyPan.ts` `PARALLAX`·`SUN_MOVE_MS`·`panSkyBy`(태양 1.1초·배경 0.6배 거리 1.6초, 둘 다 ease-in-out), `EquipmentScreen.module.css` `--sun-move`·`--sun-ease`, `equipmentOrbits.sunSpot`. 이글거림: 장비 연결에서 고리만 감추게(`svg[data-arcs]`), `SunCorona`는 테두리 바깥만 보이게 마스크 + `--corona-scale`
- SharpCap 창: `BackgroundWindows.KeepBehind`도 켜지는 동안 아이라를 "항상 위"로(N.I.N.A.·PHD2와 같은 방식)
- DESIGN.md 카메라 연출·태양 회전·구석 태양·첫 초점 실패 선택지

**확인**: X-T5 실기 — 5초·2초 노출을 중단 없이 연달아 정상(노출 중 표시가 꺼질 때쯤 N.I.N.A.가 RAW를 받음, 변환까지 약 10~20초). PHD2 — 기록에서 PHD2가 옛 자리(&0&2)를 찾고 카메라는 &0&1에 있음을 확인, 고친 빌드로 연결됨. 화면 — 실제 앱에서 구석 태양 자리·밀림, 장비 연결 이글거림, 태양 표면 회전(캔버스 변화) 확인. SharpCap이 가이드 카메라를 스스로 골라 열고 이름 기억(실기). 서버 테스트 219 통과·1 건너뜀(새: `AscomAxisLateStartTests` 2, `FujiStopTests` 2, `CX32_04`, PHD2 자리 번호). 웹 타입 검사·빌드. 확인 못 함: 구석 태양 이글거림 마스크 화면, 움직임 체감, SharpCap 하얀 창이 사라졌는지, 실제 드라이버 지연.

**남은 것**: 시험 중 아이라가 자동 진행으로 극축 정렬까지 가서 항성 추적이 약 2분 켜졌다(회전 없음, 꺼 둠) — 적도의가 홈에서 조금 벗어남. 극 찾기 화면에서 SharpCap 영상 자리가 비어 있던 것은 원인 미확인.

---

## 2026-10-11 · [Codex] dd428d8..aa94dde — 30절 수정·카메라 자동 선택·첫 초점 탐색 재리뷰 (32절)

**요청**: 마지막 리뷰 이후 수정과 추가 구현을 재검토.

**변경**: `docs/codex/REVIEW_CODEX.md` 32절 추가, 31절 Codex 재리뷰 열 갱신. 이전 8건 중 7건의 수정 경로 확인, CX-30-04는 부분 해결. **High(P1) 1·Med(P2) 3건** — 시간 초과 뒤 늦은 RA 회전과 정지 누락, PHD2 USB 보정 정규식 초기화 실패, 후지 카메라 정보 누락 시 정지 오판/중단 전송, 첫 초점 탐색 실패 뒤 자동초점 재시도 선택 누락.

**확인**: 기준 `aa94dded13fd4c047a9260097a4dab8c1bef9ccc`. 저장소 밖 아카이브에서 실장비·설치 시뮬레이터 검사 제외 **199 통과·1 건너뜀**, 웹 타입 검사 통과. 별도 가짜 드라이버·HTTP·모의 포커서와 정적 초기화로 4건 재현. ASCOM은 생성 함수만 가짜로 교체한 복사본에 실제 15초 제한을 유지. 이전 SSE 누수 재현 시험은 두 함수 모두 cleanup 이후 콜백·새 연결 0.

**남은 것**: CX-32-01~04 수정 검토. 실장비·외부 프로그램·레지스트리는 조작하지 않았고 실제 화면 렌더링은 미검증. 앱 코드·기존 테스트는 수정하지 않았으며 커밋·푸시하지 않음.

---

## 2026-10-11 · aa94dde [Claude] X-T5 노출 중단 정정 — 촬영 중 종료·가이드 별 잃음·준비 단계 멈추기는 노출을 끝까지 찍게

**요청**: 노출 중단 시험 뒤 X-T5 후면 LED가 주황·녹색으로 계속 깜빡이고, 전원을 끄면 "이미지를 PC로 전송"이 뜨며 꺼지지 않음 → N.I.N.A. 기록 확인 결과 중단이 실패한 것. 촬영 중 종료(30-01)는 사용자 기준대로 "끝까지 찍고 종료"로, 같은 이유로 가이드 별 잃음과 준비 단계 멈추기도 고치고 X-T5 이유를 주석에 남길 것.

**변경**: `ShootSession.StopForQuitAsync`는 중단 대신 `Stop()`(지금 사진까지) 후 노출+3분 기다림, `ExposeWatchedAsync`는 가이드 별을 잃어도 중단 명령 없이 끝까지 찍고 그 장을 버림, `NinaRig.AbortExposureAsync`는 후지 카메라(X-·GFX·Fuji)면 중단 명령 대신 노출이 끝나 N.I.N.A.가 사진을 받을 때까지 기다림(`IsFujifilmAsync`, `Text` 도우미). `QuitControl` 확인·진행 문구, stop-for-quit 안내, `SimulatedShootDevices.AbortCalls`(시험용), DESIGN.md 종료·멈추기 규칙, REVIEW_CODEX 31절 30-01 줄 정정.

**확인**: N.I.N.A. 기록(19:34) — 30초 노출이 셔터 코드 지정 일반 노출(Bulb=0)이었고 "Stop exposure (bulb safety) failed (result=-1)", N.I.N.A.는 "Exposure aborted"로 답함. 앞 항목(70bcb62)의 "X-T5 30초 노출이 중단 명령 1초 안에 멈춤"은 N.I.N.A. 쪽 표시만 본 잘못된 판단이라 여기서 정정. 서버 테스트 199 통과·1 건너뜀(새 `CX30_01`: 촬영 중 종료가 중단 명령 없이 끝남). 데스크톱 빌드. 확인 못 함: 실제 X-T5로 "멈추기 → 끝까지 기다림 → 다음 사진 정상", 촬영 중 종료.

**남은 것**: X-T5로 짧은 노출 멈추기 실기 확인. 긴 노출 중 준비 단계 멈추기는 정지 확인 한도(15초)를 넘어 "장비 상태 다시 확인"이 뜬다(노출이 끝난 뒤 누르면 풀림).

---

## 2026-10-11 · 70bcb62 [Claude] Codex 30절 반영, 화면 암전 없앰, PHD2·SharpCap 가이드 카메라 자동 선택, 처음 초점은 넓게 찾기

**요청**: 실내 장비 시험 중 발견 — PHD2가 가이드 카메라를 못 잡음(같은 G3M662M을 다른 USB 자리에 꽂아 PHD2가 건너뜀, "왜 아이라가 바꿔 주지 못하나", "다른 자리에 꽂혔다고 알려 줄 것"), SharpCap이 카메라 없이 켜짐, 자동초점이 0 근처에서 시작해 실패(넓은 탐색을 믿어 보기로), 단계 넘어갈 때 화면 전체 암전(늘 우주 위에서 작업하는 느낌 유지). Codex 30절 8건 — 30-01은 X-T5 노출 중단 실기 확인 뒤 "지금 사진 중단 후 평소 종료"로 결정

**변경**
- 암전: `App.module.css` `.body[data-sky]` 바탕, `PrepareScreen .stage`, `LiveView .view`의 하늘색 바탕 제거 — DESIGN.md 배경 규칙 바꿈
- 새 `Engine/Phd2CameraRepoint.cs`(PHD2 설정 cam_hash\*\whichCamera를 지금 꽂힌 같은 VID/PID 자리로, 하나만 꽂혀 있을 때만), `EquipmentConnector.CheckPhd2Async`가 고친 뒤 한 번 더 연결·실패 이유를 자리로(연결 안 됨·여러 대·다른 자리)
- `SharpCapBridge`: 스크립트 `pick_camera`(테스트·웹캠·ASCOM 제외), 열린 이름을 `sharpcap-camera.txt`에 기억해 다음 `/camera`로
- 초점: `RealFocusDevices.CoarseSearchAsync` 2단계(약 2,000걸음 → 400걸음, 별 수·HFR), `FocusTask`는 기억된 위치가 없으면 자동초점 전에 넓게 찾기, "AA 추천값" 문구 삭제
- 30-01 `ShootSession.StopForQuitAsync`·stop-for-quit·`QuitControl` 문구 · 30-02 `PolarTask` MountLock · 30-03 `PrepareRunner.Act`·`StopCurrentAsync`·`RecheckStopAsync` · 30-04 `AscomAxis` 다시 씀(뒤에서 하나씩 15초)·`RealPolarDevices.StopAsync` · 30-05 `MainWindow.OnWebProcessFailed` · 30-06 `Phd2Client.ReadLoopAsync` · 30-07 `prepare.ts`·`shoot.ts` · 30-08 `UserSettings.Update`
- DESIGN.md 종료(촬영 중)·초점 처음 위치

**확인**: 서버 테스트 198 통과·1 건너뜀(새: CX30_02·03·06·08, 바뀐 초점 테스트 문구). 웹 타입 검사·빌드, 데스크톱 빌드. 실기(집, 실내): X-T5 30초 노출을 5초에 중단 → N.I.N.A. "Exposure aborted", 1초 안에 노출 끝; PHD2 기록으로 USB 자리 차이(&0&2 → &0&1) 확인, 이 PC 설정에서 옛·새 자리 구분 확인. 확인 못 함: PHD2 설정 실제 고쳐 쓰기(사용자가 먼저 직접 고름), SharpCap 스크립트 카메라 고르기, 넓게 찾기(별 필요), 촬영 중 종료, 암전 없어진 전환 모습

**남은 것**: 맑은 밤에 넓게 찾기·SharpCap 카메라·촬영 중 종료 확인

---

## 2026-10-11 · [Codex] dd428d8 프로젝트 전반 독립 심층 감사 (30절)

**요청**: 클로드 자체 점검·수정 이후 7개 영역과 취소·재시도·재연결·종료 경계를 독립 검토. 코드 수정·실장비·외부 프로그램 조작 없이 검증하고 리뷰와 HISTORY에 기록.

**변경**: `docs/codex/REVIEW_CODEX.md` 30절 추가 — **High 4·Med 1·Low 3건**, 항목마다 실사용 등급·발생 경로·근거·수정 방향·검증 한계 표시. 우선 항목은 촬영 중 종료의 제어 주체 소멸, 극축 정렬의 적도의 안전 잠금 우회, 재시도 중복 실행, 동기 COM 정지 대기의 한계. 구조·최적화 제안은 결함과 분리. 29절의 CX-28-01~03은 정적 재확인 결과 기록.

**확인**: 기준 `dd428d88d9a4b3f1dfb132c0385500fe22b1f954`, 작업 시작·검증 후 같은 HEAD. 저장소 밖 아카이브에서 실장비/설치 시뮬레이터 테스트를 제외한 서버 검사 **194 통과·1 건너뜀**, 웹 타입 검사 통과. 가짜 작업·장비·PHD2와 임시 설정으로 재시도 겹침, 미확인 상태에서 극축 정렬 진입, 정지 대기, 저장 실패 불일치, 파싱 오류 후 구독/재접속을 확인. 실제 웹 함수에 지연 응답을 주입해 cleanup 후 SSE 생성 재현. 정상 회귀 검사 통과와 추가 결함 재현을 구분함.

**남은 것**: 30절 수정 검토·반영. 실제 WebView/DWM·COM·장비 정지와 장시간 CPU/GPU/핸들 검증은 하지 않음. 앱 코드·기존 테스트는 수정하지 않았고 커밋·푸시하지 않음.

---

## 2026-10-11 · dd428d8 [Claude] 자체 점검(Deep Self-Audit) High·Med·Low 15건 수정

**요청**: 프로세스 수명·장비 연동·동시성·SSE·화면 자원·오류 격리·스타일 토큰 7개 영역의 잠재 크래시·데드락·누수·상태 불일치 점검 → H(4)·M(5)·L(6) 차례로 모두 수정.

**변경**
- H1 `Phd2Client.CallAsync`: 답 시간 초과를 취소 예외가 아니라 `Phd2Exception`으로 — 촬영 감시가 PHD2 10초 무응답 한 번에 "예상하지 못한 오류"로 끝나던 것
- H2 `AppServer`: `BackgroundServiceExceptionBehavior.Ignore`, `NightSessionRecorder`·`NinaLogWatcher`·`NinaToastHider`·`NinaAutofocusWindowHider`는 예외를 기록하고 계속
- H3 새 `ErrorBoundary.tsx`(`ScreenError`·`AppError`): 화면 영역·상태 줄·진행 표시(실패해도 종료 버튼)·앱 전체(main.tsx, 다시 불러오기 + 종료 절차)
- H4 `MainWindow`: `ProcessFailed` → 다시 불러오기(1분에 세 번째·브라우저 전체면 다시 켜 달라는 안내 후 바로 닫을 수 있게), 창 ×에 3초 안에 화면 응답(`confirm-ack`, host.ts) 없으면 창이 직접 묻기
- M1 `Phd2Client`: 호출마다 자기 연결, `Drop(stream)`은 지금 연결일 때만, 끊기면 구독 끝냄(받는 쪽이 다시 구독), `_readerStop` 해제
- M2 `AscomAxis`: COM 해제(`FinalReleaseComObject`, 아이라가 연결했으면 연결 풀기), `Stop` 실제 결과, `EmergencyStopAsync`(AbortSlew + 두 축 멈춤, 15초 한도). `NinaRig.StopSlewAndConfirmAsync`는 N.I.N.A. 정지 실패 시 비상 정지, `EquipmentConnector`가 연결한 적도의 드라이버를 기억
- M3 `checks.ts`: 결과 전에 끊기면 남은 항목을 실패로
- M4 `NinaProcesses`(NinaNotices.cs): N.I.N.A. 프로세스·아이라 창을 2초에 한 번만 (측정: 목록 한 번 약 6ms × 초당 13번 ≈ 한 코어 8% → 약 0.3%)
- M5 `App.xaml.cs` 전역 예외 기록(창 예외는 앱을 끄지 않음), `WindowThumbnail.Stop`·상태 보고 보호
- L1 `PrepareRunner`: 다음 계열 버튼 받는 즉시 지움, 새 실행 전 앞 실행 취소 후 끝나면 정리 · L2 `UserSettings` 임시 파일 후 바꾸기 · L3 `NinaApiClient.MaxRequestTimeout`(3분) + HttpClient 안전선 · L4 `BackgroundWindows`가 캐시한 아이라 창 · L5 장비 변경 모드에서 태양 멈춤(`JarvisRing spin`), 폰·태블릿은 `--glass-blur: none` · L6 `--scrim-*`·`--label-shade` 토큰, 새 `render/sunSurface.ts`(그리기 순수 함수)
- DESIGN.md 화면이 고장 났을 때의 종료 규칙, 덧씌움 토큰·폰 흐림

**확인**: 서버 테스트 194 통과·1 건너뜀 — 새 `Phd2ClientTests` 3개(H1 시간 초과·취소 구분, M1 끊김→구독 끝·새 연결·옛 정리가 새 연결 안 끊음; 고치기 전 코드면 H1·M1 실패), `SelfAuditLowTests` 2개(L1은 동시 경합까지는 재현 못 함). 웹 타입 검사·빌드, 데스크톱 빌드, 아이라를 띄워 화면·토큰 확인. 확인 못 함: 화면 고장·WebView2 프로세스 죽음 실제 재현, 비상 정지 실장비, 폰 흐림 실기, 태양 표면(코드 이동만)

**남은 것**: 없음 (점검 보고는 대화에만 — 필요하면 docs/codex에 남김)

---

## 2026-10-11 · cc3ab99 [Claude] 도는 태양·이글거림·다가왔다 멀어지는 시작, 행성 차례로 나타나기, 연결 경과는 다 그린 뒤에, 배경 카메라 연출, 진행 호 끝

**요청**: 태양 자전 시안 적용(엔진 켜기 원·장비 연결 태양 두 곳). 장비 연결 끝 진행 호가 양 끝에 닿지 않음. 엔진 원·글자 20% 크게 → 이어서 엔진 원을 장비 태양과 같은 크기로, 처음엔 외곽선이 안 보일 만큼 가까이서 멀어지며 나타나기. 엔진 켜기부터 이글거림. 넘어갈 때 두 번 반짝임. 행성은 태양이 다 옮겨 간 뒤 수성→해왕성(소행성대 포함)→망원경 순서로(토성 고리가 먼저 나옴). 연결은 바로 시작하되 경과는 다 그린 뒤 표시. 태양 대신 화면이 움직이는 연출(혜성은 밀지 않음, 밀린 자리는 유지)

**변경**
- 새 `SunSurface.tsx`(캔버스, 시안 그대로, 30fps·멈추면 그리기 멈춤, 회전 각 공유), 새 `SunCorona.tsx`(장비 화면 `Corona`를 옮김), `JarvisRing` 옵션 `surface`·`corona`·`still`(끝 모습 이어 보이기 — 반짝임 안 함), `--conn-rgb` 토큰
- `EngineStartScreen`: 원 = 0.66 × min(높이, 폭÷1.97), `.arrive`(5배 → 1, 1.8초, 글자는 뒤에), 글자 19.2~24px
- `EquipmentScreen`: `sunArcPath`(위 가장자리 → 왼쪽 가장자리), `cascadeOrder`·`BELT_ORDER`·`SCOPE_ORDER`(.node·SVG 고리·위성 `.cascade`·`.beltShow`·`.scope`), `useRevealAfter`(약 2초 전엔 장비를 대기로, 끝·실패·자동 진행도 그 뒤), 소행성대는 흐리게 먼저 나타남
- 새 `skyPan.ts` + `SkyBackdrop` `.pan`(창의 1.6배 판, 성운 1.96배, 별 수 비례, 반짝이는 별은 보이는 자리에) — 태양이 옮겨 가는 만큼 함께 밀고 그대로 둠, 혜성 제외
- DESIGN.md 엔진 원·연결 상태 표현·나타나는 차례·경과 표시·배경 카메라 연출

**확인**: 웹 타입 검사·빌드, 데스크톱 빌드. 화면에서 직접 본 것 없음(사용자 확인 대기) — 특히 엔진 원 크기와 글자 자리, 확대 시작, 배경 밀림과 태양 이동이 맞는지, 성운이 커진 모습

**남은 것**: 사용자 문구 정리 반영

---

## 2026-10-11 · 1a2d950 [Claude] Codex 28절 반영 — 사용자가 켠 SharpCap은 정리에서 제외, 적색 테마·자리 부족이면 캡처 사진

**요청**: CX-28-01~03 모두 수정 후 커밋.

**변경**: 28-01 `SharpCapBridge.OursRunning` 추가, `CloseAsync`는 아이라가 켠 프로세스만(사용자 것은 강제 종료하지 않음), `RealPolarDevices.StopAsync`·`ReadEndStateAsync`도 그것만 본다. 28-02·03 `WindowView`: 비추지 않는 까닭을 감춤(대화 창·가려짐)과 fallback(적색 테마·자리 부족)으로 나눔 — fallback이면 빨강 필터 캡처, 빛 테두리 숨김. DESIGN.md 해당 줄.

**확인**: 서버 테스트 189 통과·1 건너뜀, 웹 타입 검사·빌드, 데스크톱 빌드. 실제 SharpCap 건너뛰기·적색 테마 전환은 미확인.

**남은 것**: 아이라가 켠 SharpCap이 열려 있는 채 아이라가 다시 켜지면 그 SharpCap은 사용자 것으로 본다(닫지 않음, 안전한 쪽).

---

## 2026-10-11 · [Codex] 13cf46f..ea5d2b6 리뷰 (28절)

**요청**: CX-25-01 재확인과 SharpCap DWM 영상·WantView·재실행·GlowFrame 신규 구현 검토, 실사용 등급 표시.

**변경**: `docs/codex/REVIEW_CODEX.md` 28절 추가·27절 재리뷰 칸 기록. CX-25-01 반영 확인. 사용자 SharpCap 건너뛰기의 강제 종료 경로(P1·겪음), DWM의 적색 테마 미적용·공간 부족 시 캡처 미전환(P2·겪음 2건)을 기록. 앱 코드 수정 없음.

**확인**: ea5d2b6 격리 복사본 서버 테스트 189 통과·1 건너뜀, 웹 TypeScript 통과. 가짜 HTTP로 반복 중단 미확인 유지·응답 형식 판정 확인, 실제 slotOf 함수로 공간 부족 분기 모의 확인. 프로세스 종료·테마는 코드 경로 대조. 실장비·외부 프로그램 조작 없음.

**남은 것**: CX-28-01~03 보완 여부 결정. DWM/UIA·DPI·장시간 효과 성능은 실제 화면 검증하지 않음. 커밋·푸시하지 않음.

---

## 2026-10-11 · ea5d2b6 [Claude] Codex 25절 반영 — 답 없는 연결 요청의 미확인을 중단 재시도에서 잃지 않게

**요청**: CX-25-01(P1·장비 안전) 수정 후 커밋·푸시.

**변경**: `EquipmentRun.Unanswered` 추가 — `EquipmentConnector.AbortAsync`가 답 없는 요청 장비를 여기 남기고, 이번 중단에서 연결된 것을 본 뒤 끊김을 확인했을 때만 지운다(그 전엔 매번 "(연결 요청이 끝나지 않음)"). 연결 쪽은 그 장비 요청이 답을 받으면 지운다. `BeginRun(only)`: 장비 하나 다시 연결은 그 장비의 Unresolved만. `NinaApiClient.RequestAsync`: `Answered`는 Success가 참/거짓일 때만.

**확인**: 새 `Engine/AbortUnansweredTests`(가짜 HTTP로 첫·두 번째 중단 모두 미확인, 연결된 뒤 끊으면 풀림) 포함 서버 테스트 189 통과·1 건너뜀, 데스크톱 빌드. 실장비 미확인.

**남은 것**: 답을 끝내 못 받은 장비가 실제로는 연결 실패로 끝났다면 종료 창이 계속 알린다 — 그때는 사용자가 "그래도 종료"로 정한다.

---

## 2026-10-11 · d72d1b4 [Claude] Codex 24절 반영, SharpCap 영상을 DWM 썸네일로 비추기, 빛 테두리 GlowFrame, 켜져 있던 SharpCap 닫고 다시 켜기

**요청**: Codex 24절 7건 모두 수정. SharpCap 화면이 깨져 보임 → 사용자 아이디어("아이라를 창문처럼 투명하게") → DWM 썸네일 + UI Automation으로 영상 칸만, 가운데에. 칼 같은 외곽선에 빛 테두리(참고 그림 "Glowing Border Animation V2", 공통 모듈로). 사용자가 켜 둔 SharpCap은 "닫고 다시 켜기"로(다른 프로그램도 같은 경우가 있으면 같이 — 없었음). 효과 요청은 코드·링크를 주면 더 정확하다고 답함

**변경**
- 24-01 `NinaApiClient.ConnectAnsweredAsync`·`NinaReply.Answered`, `EquipmentConnector` InFlight를 `Task<bool?>`로. 24-02 `EquipmentRun.Unresolved`, `/equipment/abort`는 촬영 중이면 끊지 않음. 24-03 `EquipmentScreen.select`. 24-04·07 `LiveView.RealImage`. 24-05 새 `keepInside.ts`(`ScopeList`·드라이버 목록). 24-06 `Diagnostics.Export`·`Settings` 버튼
- SharpCap 영상: 새 `Astro.Desktop/WindowThumbnail.cs`(UIA `WpfPreviewPanel`›`Image` 위치 → DWM 썸네일, 비춘 자리를 화면에 알림), `MainWindow` "window-view" 메시지, 새 `WindowView.tsx`(글자·버튼 `data-occludes`를 피한 가운데 띠, 못 비추면 예전 캡처), `host.onHostMessage`. 스크립트는 화면이 사진을 물을 때만 저장(`SharpCapBridge.WantView` — "Snapshot captured" 줄이 영상 위에 뜨던 것)
- 새 `GlowFrame.tsx`/`.module.css`(옅은 선 + 선 위 마주 보는 두 빛 + 바깥 후광, transform 회전만, 동작 줄이기면 멈춤). 화면 틀 배율(0.8)로 나눠 테두리 자리를 맞춤
- `PolarTask.HandOverAsync`: PHD2가 카메라를 놓기 전에 켜져 있던 SharpCap을 물음 → `SharpCapBridge.CloseUsersAsync`(창 닫기만, 20초)
- DESIGN.md: 장비 목록 위치·망원경 바로 확인, SharpCap 영상 비추기·이미 켜져 있을 때, GlowFrame

**확인**: 서버 테스트 188 통과·1 건너뜀, 웹 타입 검사·빌드, 데스크톱 빌드. 실기(집, 4K 250%): UIA로 영상 칸·Polar Align 패널 글자 읽기, 썸네일이 실제 가이드 카메라 영상을 즉시 갱신하며 비춤(사용자 확인), 첫 시도 때 테두리가 0.8배로 작게 그려진 것을 고침. GlowFrame은 헤드리스 Edge 그림으로. 확인 못 함: 고친 뒤 극축 정렬 화면의 영상 크기·테두리 맞음, "닫고 다시 켜기" 실제 흐름, 24-01·02 가짜 응답 시험

**남은 것**: Codex 25절 CX-25-01(중단 재시도에서 응답 유실 사유를 잃음) 판단 대기. Polar Align 패널 글자를 아이라 상태로 보여 주기(읽기만)는 다음

---

## 2026-10-11 · [Codex] 24절 수정분 재확인 (25절)

**요청**: Claude가 고친 24절 항목을 재리뷰.

**변경**: `docs/codex/REVIEW_CODEX.md` 25절 추가. HEAD 13cf46f 위 미커밋 수정분 기준으로 CX-24-03~07 반영 확인, 01·02 부분 반영. 응답 유실의 미확인 사유가 중단 재시도에서 사라지는 경로를 CX-25-01(P1·장비 안전)로 기록. 앱 코드 수정 없음.

**확인**: 격리 복사본 서버 테스트 188 통과·1 건너뜀·실패 0, 웹 TypeScript 검사 통과. 실제 클래스+가짜 HTTP로 첫 중단 실패 1건→재시도 실패 0건을 재현. LiveView effect 모의 시험에서 단일 사진 복원 요청 1회·라이브 stamp 변경 추가 요청 0회·ETag 유지 확인. 나머지는 코드 경로 확인이며 실제 장비·외부 프로그램 조작 없음.

**남은 것**: CX-25-01의 미확인 사유 보존·개별 재시도 시 전체 초기화·응답 형식 검증 보완. 전체 WebView 조작과 실제 진단 수집은 하지 않음. 커밋·푸시하지 않음.

---

## 2026-10-11 · [Codex] a42a945..13cf46f 재리뷰 — 21절 반영 확인·새 설정과 장비 화면

**요청**: 상단 [Claude] 세 항목을 먼저 읽고 CX-21-01~07 반영, 장비 태양계 화면·설정/키 암호화·진단·프로그램 경로·사진 ETag·배경을 검토. 항목마다 실사용 등급 표시.

**변경**: `docs/codex/REVIEW_CODEX.md` 24절 추가. 21-02~07 반영 확인, 21-01 부분 반영. 연결 응답 유실·중단 재시도의 미확인 장비 처리(P1·장비 안전 2건), 망원경 선택 후 확인 대기·숨김 중 시험 사진·선택 목록 잘림·진단 파일명 충돌(P2·겪음 4건), ETag 초기화(P3·이론상 1건). 앱 코드 수정 없음.

**확인**: 13cf46f 격리 복사본에서 서버 테스트 188 통과·1 건너뜀·실패 0, TypeScript 검사 통과. 실제 클래스와 가짜 응답으로 연결 중단·홈 정지·추적 조회 실패·망원경 선택·ETag, 임시 파일로 DPAPI·ZIP 충돌 확인. UI는 추출한 effect/배치 함수로 모의 검증. 실장비와 외부 프로그램을 조작하지 않음.

**남은 것**: 24절 지적 반영 여부 결정. 전체 WebView 조작·실장비 종료·장시간 배경 성능은 이번 검증 범위 밖. 커밋·푸시하지 않음.

---

## 2026-10-11 · 13cf46f [Claude] Codex 21절 반영(종료·홈·연결 중단의 미확인 경로), 리뷰 실사용 등급 규칙

**요청**: Codex 21절(20절 재리뷰) 7건 모두 진행. 리뷰 지적을 사용자가 일일이 추적하기 어려움 → 항목마다 실사용 등급(장비 안전 / 겪음 / 이론상)을 붙이는 규칙을 CLAUDE.md·AGENTS.md에. 바탕 화면 바로가기는 설정 기능이 아니라 이 PC에 하나 만들어 달라는 뜻이었음 → 설정 창 기능을 빼고(앞 커밋 전에 되돌림) 바로가기만 만듦

**변경**
- 21-04 `MountHome.TrackingOffForQuitAsync`: 조회 실패는 false. 21-02 `MountHome.StopForQuitAsync`·`Homing`, `/prepare/stop-for-quit`에 포함, `AppServer.PrepareBusy`가 이유 문장(홈 포함)·GET `/engine/close`에 `held`. 21-03 `MountLock.Unconfirmed`·`MountUnconfirmedException`, `PrepareRunner`가 이유 + 다시 시도로 보여 줌
- 21-01 `EquipmentConnector`: `EquipmentRun.InFlight`를 장비 → 연결 요청 Task로, 요청은 취소 토큰 없이 보내고 `AbortAsync`가 답을 기다림(최대 65초) 뒤 3초 조용함 확인, 확인한 장비만 Touched에서 뺌
- 21-05 `PrepareRunner.StopForQuitAsync`/`ShowQuitPaused`("다시 시작"). 21-06 `NinaNotices.Add(key)`·`NinaNotice.Key`. 21-07 `QuitControl` 목록 조회 실패·held 안내
- 테스트가 실제 기록 파일에 쓰던 문제: `FileLogProvider.Enabled`, 테스트 `TestSetup`(ModuleInitializer)에서 끔
- DESIGN.md 종료·적도의 확인 필요 규칙, CLAUDE.md·AGENTS.md 실사용 등급

**확인**: 서버 테스트 188 통과·1 건너뜀(새 `QuitSafetyTests` 3개 — 21-03·05·06), 테스트 뒤 기록 파일에 새 줄 없음. 웹·데스크톱 빌드 오류 0. 21-01·02·04는 가짜 N.I.N.A. 응답 시험을 만들지 않아 코드·빌드로만 확인. 실장비 종료·홈 실패·연결 중 종료는 미확인

**남은 것**: 연결 도중 종료하면 장비 연결 끊기 단계가 최대 1분쯤 걸릴 수 있음. 20-11의 시작 감시 중 포커스 되가져오기는 미룸. 기록 파일의 10-11 02:02·11:49 줄은 예전 테스트가 남긴 것

---

## 2026-10-11 · fbeb96c [Claude] Codex 20절 반영, 종료 때 추적 끄기, 라이브 사진·배경 효율, 설정 창

**요청**: Codex 20절 리뷰 검토 → 11건 모두 적용(20-06·20-07은 합의한 변형). Gemini 리뷰 중 화면 폴링·배경 비용 지적 반영(배경은 미리 그린 그림 + 합성기 애니메이션 방식). 실기: 극축 정렬 뒤 종료하자 적도의가 몇 시간 추적해 RA 90° 돎 → 종료 때 추적 끄기. 장비 변경 버튼·설명을 태양 오른쪽 상태 줄 아래로. Empire 로고가 두 번 뜸. 다크 테마도 배경 움직이기. 태양 자전 시안(의견 후 요청). 설정(AI 키·켜기, 프로그램 경로, 기록·진단). 바탕 화면 바로가기는 사용자 PC에만 만듦(앱 기능 아님 — 나중에 인스톨러에서 선택)

**변경**
- 20-01 `NinaApiClient.ConnectedStateAsync`(bool?), `EquipmentConnector` InFlight·조용함 확인. 20-02 종료 순서 `QuitControl`(작업 멈춤 `/prepare/stop-for-quit` → 추적 끄기 `/mount/tracking-off-for-quit` → 포커서 → 장비 → 프로그램), `PrepareRunner.StopForQuitAsync`, `ProgramCloser` 프로그램별·먼저 켜진 것 제외. 20-03 `MountHome` 다시 씀(홈 버튼·rig/rest 공용, Slewing, 실패 시 Stop, `/mount/home/confirm`). 20-04 FocusTask, 20-05 FocuserPark, 20-07 배선 학습, 20-08 Calibration/Guiding, 20-09 FinishTempSkip, 20-10 NinaNotices 큐, 20-11 BackgroundWindows 로고만
- 라이브 사진 ETag/304, 화면은 숨김 상태면 묻지 않음(`LiveView` RealImage). `SkyBackdrop` 다시 씀: 성운·별은 한 번 그리고 CSS transform·WAAPI(메인 스레드 10초 560ms → 약 90ms)
- `EquipmentScreen` sunBar(버튼 | 설명 | 관측지), 새 `Settings.tsx`·`Setup/UserSettings.cs`(키 DPAPI)·`Setup/Diagnostics.cs`, `MainWindow` pick-exe, `InstallLocator.UserNinaExe`, ChatModelFactory가 설정 키 우선. 시안 `mockups/aira-sun-rotation.html`. DESIGN.md 종료·홈·배경·설정 절
- Gemini 리뷰의 P/Invoke 중복·연결기 쪼개기는 미룸

**확인**: 웹·서버·데스크톱 빌드, 서버 테스트 199 통과·1 건너뜀(FocuserPark Blind, 알림 큐, UserSettings 2개 추가). 배경 성능은 헤드리스 Edge 측정. 확인 못 함: 실장비에서 종료 추적 끄기·홈 실패·작업 중 종료, 라이브 ETag, 찾아보기 창, 진단 내보내기(실제 바탕 화면에 씀), Empire 로고 한 번

**남은 것**: Codex 21절(CX-21-01~07) 검토. 단위 테스트가 실제 기록 폴더에 알림 줄을 씀. 태양 자전 시안 피드백 후 엔진 원·장비 태양에 적용

---

## 2026-10-11 · [Codex] 20절 수정분 재검증 (21절)

**요청**: Gemini가 수정한 이전 리뷰 항목 재확인. 코드 수정 없이 모의 응답·장비로 검증.

**변경**: `docs/codex/REVIEW_CODEX.md` 21절 추가. HEAD 4411d15 위 미커밋 작업 폴더를 기준으로 CX-20-01~11의 반영 여부와 한계를 기록. 남은 연결 중단·홈·종료·알림 경로를 CX-21-01~07(P1 4건·P2 3건)으로 정리.

**확인**: 저장소 밖 복사본에서 AA_REAL=0·AA_SIM=0 및 실장비/시뮬레이터 통합 시험 제외 필터로 서버 테스트 185 통과·1 건너뜀. 별도 가짜 HTTP·장비 시험으로 초점 수동 완료 차단, 잘못된 0점 기록 방지, 무가이드 미확인 차단, 임시 건너뛰기 복구를 확인. 12초 지연 연결, 홈 이동 중 종료·정지 미확인 뒤 잠금 재획득, 종료 추적 조회 실패, 종료 취소 뒤 버튼 소실, fallback 경고 원문 덮임을 재현. 실장비·외부 프로그램·사용자 창 조작 없음. 앱 코드·기존 테스트 수정, 커밋·푸시 없음.

**남은 것**: 21절의 P1 경로 우선 보완. 프로그램 소유 판정은 시작 시각 기반 한계가 남고, 실제 Windows 창·프로그램 종료 및 하드웨어 동작은 별도 검증 필요. 함께 있는 설정·태양계 UI 변경 전체는 이번 재리뷰 범위가 아님.

---

## 2026-10-11 · 4411d15 [Claude] 장비 연결 화면을 태양계로, 인라인 SVG 스타일이 앱 전체 아이콘 색을 바꾸던 버그

**요청**: 자비스 원을 태양, 장비를 행성으로 — 의견 → 시안 2개(비스듬한 3D 시안은 평면 글래스 원과 시점이 안 맞아 탑뷰로) → 여러 차례 조정(항성 왼쪽 위 1/4, 궤도 간격 안쪽 촘촘·바깥 넓게, 행성 크기 태양계 비례를 줄여서, 소행성대·고리·위성·대적점·항성 이글거림, 이웃 궤도 행성 간 지름 2배 이상, 장비↔행성 1:1 고정, 쓰지 않는 장비는 흐린 행성, 망원경은 제임스 웹 아이콘으로 늘 눌러 바꾸기, 장비 아이콘 + V 겹침, 궤도 밝기 0.03) → 구현. 결정: 변경 모드는 행성 제자리, 관측지 줄은 항성 안, 엔진 원은 항성으로 옮겨 감

**변경**
- 새 `screens/equipmentOrbits.ts`(solarLayout — 행성·고리·위성·소행성대·망원경 자리, 앱을 켤 때 씨앗으로 고정), 새 `components/JwstIcon.tsx`(mockups/aira-brand/jwst-icon-line.svg)
- `EquipmentScreen.tsx` 다시 씀: 방사형 배치(connectLayout·editLayout·relax·balancedOrder)·연결선·흡수/펼침·아래 진행 막대 없앰 → 항성(JarvisRing 테두리 빛 + Corona 빛줄기 + 테두리 진행 호) · PlanetNode · 변경 모드는 같은 자리의 EditNode · 망원경 버튼(목록 열린 동안 진행 멈춤) · 목록 바깥 누르면 닫힘. CSS도 새로(`--orbit-alpha` 0.03)
- 버그: 로고·상태 줄 별자리 글자 SVG를 인라인으로 넣으며 그 안의 `<style>svg{color:…;--accent:…}`·id가 문서 전체에 걸려 모든 아이콘이 연보라(다크 테마 포함) → 새 `inlineSvg.ts` `scopedSvg`로 선택자·id를 그림 안에 가둠(BrandMark·BootScreen)
- DESIGN.md 장비 연결 절·장비 변경 모드 절을 태양계 기준으로 다시 씀. 시안 `mockups/aira-equipment-orbits.html`(3D, 버림)·`aira-equipment-orbits-top.html`, `aira-brand/jwst-icon*.svg`
- Codex 리뷰(20절, HISTORY Codex 항목)도 이 커밋에 함께 들어감 — 판단은 다음 커밋

**확인**: 웹 타입 검사·빌드, 서버·데스크톱 빌드. 시험 서버(5211) + 임시 시험 페이지(엔진 켜기·장비 목록·연결 결과만 흉내, 끝나고 지움) + 헤드리스 Edge(DevTools 프로토콜)로 모두 연결·진행 호, 적도의·포커서 실패, 장비 변경 모드, 다크 테마를 그림으로 확인. 다크 테마 아이콘 색이 빨강으로 돌아온 것을 계산된 색으로 확인. 확인 못 함: 실제 N.I.N.A.·장비에서 연결·다시 연결·목록 바꾸기, 엔진 원이 항성으로 옮겨 가는 움직임

**남은 것**: 위성이 변경 모드 이름표와 살짝 겹칠 수 있음. 다른 화면 아이콘 색이 원래 정한 색으로 돌아온 것(인라인 SVG 버그 수정) 확인 필요

---

## 2026-10-10 · [Codex] 490cd41..a42a945 연결·종료·준비 흐름 리뷰 (20절)

**요청**: 지정한 22개 커밋에서 장비 연결·중단·종료, 임시 건너뛰기·무가이드, 알림·창 감시, 초점·홈 경로 검토. 재개 뒤 추가 커밋은 제외. 코드 수정 없이 리뷰 작성.

**변경**: `docs/codex/REVIEW_CODEX.md` 20절 추가. CX-20-01~11(P1 3건·P2 8건)에 a42a945 기준 파일·줄, 발생 조건, 근거와 수정 방향 기록. 연결 해제 오판, 준비 중 종료·홈 실패의 정지 확인, 수동 초점·0점 기록, 프로그램 소유 판정, 배선 학습, 무가이드 정지, 임시 건너뛰기 복구, 알림 누락·창 속성 변경을 지적.

**확인**: a42a945를 별도 폴더에 추출해 후속 변경과 분리. 실장비/N.I.N.A. 시뮬레이터 시험을 필터로 제외하고 AA_REAL=0·AA_SIM=0으로 서버 테스트 181 통과·1 건너뜀. 별도 모의 응답/장비로 지연 연결·조회 실패 중단, 이동 실패 뒤 수동 초점 완료, 조회 실패의 0점 기록, 임시 건너뛰기 정지 재확인 뒤 버튼 소실을 재현. 알림 fallback 조건도 별도 확인. 실장비 명령·외부 프로그램 종료·사용자 창 조작 없음. 앱 코드·후속 커밋·목업 수정, 커밋·푸시 없음.

**남은 것**: Claude가 항목별 판단 후 수정 여부 결정. 이후 커밋에서 이미 해결했는지는 이번 범위에서 평가하지 않음. Windows 창 동작과 실제 장비별 정지/연결 완료 시간은 별도 검증 필요.

---

## 2026-10-10 · dabbdb6 [Claude] 버튼 공통 Button으로 통일, 레일에서 홈·종료·배경 분리, 종료 창 프로그램 목록, 종료 지연, 테마 배경

**요청**: 레일에 종료가 엮여 관계없는 수정이 다른 기능에 번지고 버튼 모양을 바꿔도 하나만 바뀌던 문제 — 기능 단위로 나뉘지 않은 곳을 전체 코드에서 찾아 검토(A 버튼 중복, B 레일에 엮인 기능, C 서버 큰 클래스·창 제어 중복, D 웹 중복 보고) → A·B는 지금 고치기. 이어서 종료 창을 "앱을 종료 중입니다." + 아이라가 켠 프로그램 목록을 하나씩 체크, 마지막 체크 1초 뒤 종료. 버그: 테마를 바꾸면 배경이 한 박자 늦게 바뀜, 프로그램이 다 닫혔는데 아이라가 한참 늦게 꺼짐, 기본 테마에 붉은 성운. Gemini 리뷰 파일 보관

**변경**
- A 버튼: `Button`에 `size`(sm·md·lg)·`surface`(app·sky)·`variant` ghost·`aria-disabled` 추가, 모양은 `Button.module.css` 한 곳. 화면마다 따로 있던 `.primary`/`.quiet`/`.retry`/`.skip`/`.tool`(EquipmentScreen·PrepCenter·ScopeList·ConfirmDialog·EngineStartScreen·PreflightScreen·PrepareScreen)을 `Button`으로. `--state-disabled-opacity`·`--state-focus-ring`을 장비 화면 지역 값에서 `index.css`로
- B 레일: 적도의 홈 → `MountHomeButton`, 종료 → `QuitButton`(QuitControl), App이 `StepRail`의 `foot` 자리에 넣음. 화면 하늘 바탕은 `RailExtra.sky` 대신 `useScreenSky`(ScreenSky.tsx). `hideExit` 없앰 — 요약 화면 여부는 App이 phase로. `useRailExtra`는 값 전체(JSON)로 바뀜을 보고 onClick은 늘 최신 것을 부름
- 종료 목록: `ProgramCloser.Targets()`·`CloseOneAsync(id)`(N.I.N.A. → PHD2 → SharpCap → 아이라가 켠 Empire, Empire는 N.I.N.A.가 남아 있으면 안 닫음), `GET /api/engine/close`·`POST /api/engine/close/{id}`(예전 `POST /engine/close` 대체). `QuitControl`이 하나씩 닫으며 체크(`QuitControl.module.css`), 1초 뒤 `closeApp`. `ConfirmDialog`에 children
- 종료 지연: 원인 = 화면의 SSE 연결(EventSource)이 끝나지 않아 서버 멈춤이 한도(10초)까지 기다림 → `AppServer`에서 `ApplicationStopping` 때 `text/event-stream` 요청을 끊음, `MainWindow.OnClosing`은 멈추기 전에 `Hide()`
- 테마 배경: `SkyBackdrop`이 두 테마 성운을 미리 만들어 두고 바꿀 때 바로 교체, `useTheme`은 `useLayoutEffect`로 data-theme. (중간에 테마 감시 `observe` 줄을 실수로 지워 배경이 켤 때 테마로 고정됐던 것도 이 커밋에서 되살림)
- DESIGN.md: "버튼은 공통 Button 하나로" 절, 레일은 단계·작업 표시만, 종료 순서·목록. `docs/codex/REVIEW_GEMINI.md`(490cd41..a42a945 Gemini 리뷰 원문) 추가 — 판단은 Codex 리뷰와 함께

**확인**: 웹 타입 검사·빌드, 서버·데스크톱 빌드, 서버 테스트 195 통과·1 건너뜀. 시험 서버(5211)·브라우저: 엔진 화면 "다시 시도"·레일 아래 "적도의 홈"·확인 창이 공통 Button으로 나옴(DOM), 성운 색이 기본↔다크 전환 0.2초 안에 따라 바뀜(캔버스 픽셀). 사용자 실행: 종료 목록 4개 체크됨. 확인 못 함: 장비 준비·하늘 화면·점검·요약의 버튼 모양, 고친 뒤 실제 앱의 종료 속도

**남은 것**: 리뷰 C(EquipmentConnector 나누기·창 제어 Win32 모으기·프로그램 이름 상수)·D(API 호출 모듈, 동작 줄이기 확인 함수)는 Codex 리뷰와 함께 판단. 공통 버튼에서 긴 글자가 두 줄이 되는 곳은 사용자가 모아서 문구 조정 예정

---

## 2026-10-10 · a42a945 [Claude] 레일 보라, Empire 시작 로고 실측 고침, 알림 모두 숨김, 종료 때 N.I.N.A. 오류, [임시] 건너뛰기, 부팅 로고 천천히, 사진 없을 때 배경 유지

**요청**: 실행해 본 뒤 — 오른쪽 레일의 초록도 보라로, Empire 시작 로고가 아직 보임, N.I.N.A. 알림이 어떤 건 사라지고 어떤 건 남음, 극축 정렬 중 아이라를 끄자 N.I.N.A. 오류 창이 떴는데 꺼지는 중이라 내용을 못 봄(확인 요청), 극축 정렬·캘리브레이션을 바로 넘기는 임시 버튼(쉽게 지울 수 있게), 부팅 로고가 자연스럽게 투명해지며 사라지게, 극축 정렬로 넘어갈 때 갑자기 검은 배경 → 실제 카메라 화면이 아니면 기존 배경 유지. Windows MCP 권한 받음

**변경**
- 레일: `StepRail.module.css`의 `--pass` 5곳 → `--conn`
- Empire 로고: 직접 Empire를 켜 창을 0.2초마다 기록 — 로고는 제목 "startup"의 "항상 위" 창인데 숨은 창에 딸려(owner 있음) 감시의 `Windows()`(주인 없는 창만)에서 빠졌음 → 주인이 있어도 "항상 위"면 포함
- 종료 때 오류: 기록상 "PHD2 서버 연결이 끊어졌습니다" — `ProgramCloser`가 PHD2를 먼저 닫아 N.I.N.A.가 끊김을 오류로 띄움 → N.I.N.A. → PHD2 → SharpCap 순(극축 정렬 중이면 SharpCap도 닫음)
- 알림: 남은 것은 N.I.N.A. 로그에 오류·경고로 남지 않는 안내성 알림이라 원문이 없어 되돌려 보여 주던 것 → 되돌리지 않음(해석 못 한 오류·경고만 원문으로 아이라 알림), 아이라가 앞에 없어도 N.I.N.A. 창을 보고 있을 때만 빼고 숨김(`IsNinaForeground`)
- [임시] 건너뛰기: `PrepareRunner.TempSkipAsync`(극축 정렬·캘리브레이션만 — 멈춤 확인 뒤 Skipped 결과, 다음은 평소처럼 끝 버튼 — 캘리브레이션이 적도의를 움직이므로 바로 시작하지 않음), `POST /prepare/{group}/temp-skip`, 웹 `PrepareScreen` 왼쪽 아래 버튼(`TEMP_SKIP`). 지울 때 "[임시] 건너뛰기"로 찾기
- 부팅: 부팅에서 넘어갈 때만 페이드아웃 0.28 → 0.8초(`BOOT_FADE_MS`, `.screen[data-boot]`)
- 배경: 실장비에서 실제 사진(live url)이 있을 때만 하늘 바탕 — `PrepareScreen` `skyBg`(레일 sky·`.stage[data-sky]`·아래 그늘), `LiveView`는 사진 없고 그림도 없으면 아무것도 안 그림, 지도 등은 바탕 없이(`data-clear`)
- 기록에서 확인: 허브(WandererBox)가 N.I.N.A.에 주는 읽기 칸은 "Always On DC1: DC1", "PWM DC3: DC3" 둘뿐 — **입력 전압 칸이 없어** 허브 전원 확인(0V면 미룸)은 이 허브에서 동작하지 않음(막지 않고 지나감)

**확인**: 서버 테스트 195 통과·1 건너뜀, 웹 타입 검사·빌드, 서버·데스크톱 빌드, Empire 로고 창 정보 실측. 확인 못 함: 고친 뒤 실제 앱에서 Empire 로고·알림·종료·[임시] 건너뛰기·배경(장비 필요)

---

## 2026-10-10 · [Claude] 로고 v5(150%), 강조색 호박 → 보라

**요청**: 로고를 `mockups/aira-brand/aira-logo-v5.svg`로, 지금보다 150% 크기. 프로필 선택 호버 외곽선·다른 버튼 호버 외곽선·버튼의 노란색을 모두 장비 원의 보라 계열로

**변경**: `assets/aira-logo.svg` ← v5, `aira-mark.svg`(상태 줄 AIRA 별자리)를 v5에서 다시 떼어 냄(선 7). `BootScreen` 로고 너비 min(880px, 64cqw) → min(1320px, 100%)(96cqw로 했더니 좁은 창에서 가로 스크롤이 생겨 여백 안 폭까지만). `index.css` 기본 테마 `--accent`·`--focus` #f0a93b → #a9a6f7(`--conn`과 같음), `--on-accent` #17153a — 주 버튼·프로필 타일 호버·레일 현재 단계 등이 함께 바뀜, `--warn`은 호박 그대로. 강조색이 아닌 호버 외곽선 셋도 `--accent`로(Button quiet, 계획 칸, 점검 카드). DESIGN.md 7-2·부팅 화면

**확인**: 웹 타입 검사·빌드, 서버·데스크톱 빌드, 시험 서버·브라우저에서 부팅 로고 v5(좁은 창에서 가로 넘침 없음), 프로필 화면 `--accent`·`--focus` = #a9a6f7, 상태 줄 별자리 그림. 이어서 사용자 요청으로 확인됨 외곽선 `--step-done-line`(점검 카드·쉐브론 등) 초록 → 보라 rgb(169 166 247 / .55), 점검 카드 V·쉐브론 통과 체크도 `--conn`. 확인 못 함: 1920×1200 틀에서의 로고 크기(넓은 창)

---

## 2026-10-10 · [Claude] 장비 연결 기다림 줄이기 (한 번 확인 → 안 되면 실패 + 다시 연결)

**요청**: 연결 시간 기록(6장비 97초 중 꺼진 장비를 기다린 시간이 대부분)을 보고 — 기다린 시간 줄이기 적용. 허브가 "전원 있음"으로 통과했는데 적도의 포트가 없던 것은 사용자가 전원을 넣으며 실행해 선후 관계를 모름

**변경**: `EquipmentConnector` 연결 직전 "PC에 보이는지" 10초 기다림 → 한 번만 확인(`JustSwitchedWait` — 이번에 아이라가 허브 출력을 막 켰으면 켠 뒤 10초까지만 남은 만큼), 카메라 출력을 켠 뒤 기다림 30 → 20초(실기 14초). 다시 연결 버튼도 같은 길. `HubInputVoltsAsync`가 허브 읽기 전용 칸 이름·값 전부와 입력 전압을 기록 — 다음 실행에서 전압 칸 이름이 맞는지 확인하려고

**확인**: 서버 테스트 195 통과·1 건너뜀, 서버·데스크톱 빌드. 확인 못 함: 실장비에서 줄어든 시간(기록으로 다음에 비교), 허브 전압 칸 이름

---

## 2026-10-10 · [Claude] 연결된 원이 사라지던 버그, 원 표현을 모든 화면에, 실패는 점멸, 테마 버튼 빈 칸, N.I.N.A. 켤 때 Empire 로고, 모든 단계 기록 파일

**요청**: (실행 화면 캡처와 함께) 1 N.I.N.A. 켜기 단계 원에도 같은 표현, 2 연결된 장비 원이 사라짐(버그), 3 실패의 붉은 선이 어색하고 다크(빨강) 테마에서 성공·실패가 구분 안 됨 → 실패는 외곽선이 느리게 점멸, 4 테마 버튼이 하나 줄었는데 자리는 그대로(버그), 5 N.I.N.A. 켜는 단계에서 Wanderer Empire 시작 로고가 위에 뜸, 6 완성 전까지 모든 단계에 로그

**변경**
- 2 원인: 연결됨 반짝임(`animation: connFlash`)이 장비 원의 처음 나타나기(`appear` — 원은 투명에서 시작) 애니메이션을 덮어 원이 투명한 채로 남음 → 빛·반짝임은 `.node::after` 층에만
- 1: 테두리를 도는 빛·반짝임(ringFlash)·실패 점멸(ringFail)을 `JarvisRing.module.css`로 옮겨 모든 화면 같은 표현(도는 호 그림 숨김, 체크 보라). 장비 연결 화면은 글래스 가장자리에 맞추는 위치만 남김
- 3: `.node[data-status='Fail']::after` 외곽선 2.4초 점멸(고정 붉은 테두리 없앰), 허브 실패의 바깥 고리 깜빡임은 그대로
- 4: `ThemeSwitch` `repeat(3, 1fr)` → 옵션 수만큼(grid-auto-flow column)
- 5: `EngineStarter`가 N.I.N.A.를 켤 때 지켜보는 프로그램에 Wanderer Empire 추가, 끝난 뒤 20초 더(늦게 뜨는 로고)
- 6: 새 `FileLogProvider` — 서버의 모든 기록(아이라 코드는 Information부터, 나머지는 Warning부터)을 `logs/aira-날짜.log`에, 14일 지나면 지움. `POST /api/log` + 웹 `log.ts`(단계 이동·테마·누른 버튼 모두). 준비 작업 `RunHandle`이 세부 과정·안내·상태·결과(계속 오는 값 제외)·물음·고른 버튼을 기록, 장비 연결 결과, N.I.N.A. 알림
- 기록에서 찾은 버그: 업데이트 확인의 User-Agent에 한글 이름이 들어가 이름을 바꾼 뒤로 확인이 늘 실패 → `Product.Id`(AIRA)

**확인**: 서버 테스트 195 통과·1 건너뜀, 웹 타입 검사·빌드, 서버·데스크톱 빌드. 시험 서버·브라우저: 테마 버튼 묶음 너비 118px(56px × 2 + 여백 — 빈 칸 없음), 기록 파일에 "화면: 단계: boot → profiles"·"테마"가 남음. 사용자 실행(18:36)의 연결 시간 기록 확인 — 6장비 97초 중 대부분이 꺼진 장비를 기다린 시간(적도의·포커서·가이더 10초씩, 카메라 10 + 30초), 허브 N.I.N.A. 연결 21초. 확인 못 함: 실제 화면에서 원 표현·Empire 로고(N.I.N.A.·장비 필요)

---

## 2026-10-10 · [Claude] 장비 연결 원: 연결 중은 테두리를 도는 보라 빛, 연결되면 한 번 반짝인 뒤 은은한 빛

**요청**: 원 느낌 비교(글래스/카드/섞음)에서 지금 글래스가 낫고 투명감을 조금 더, 연결 중 노랑·연결됨 초록이 배경과 안 어울림 → 상태 비교(mockups/aira-circle-states.html)에서 B(보라 + 은은한 빛) 선택. 연결 중의 도는 보라는 원 안이 아니라 원 테두리의 글로우가 돌게, 연결된 장비는 한 번 강하게(과하지 않게) 반짝였다가 은은한 상태로

**변경**: `index.css` 토큰 `--conn`·`--conn-soft`·`--conn-glow`·`--conn-flash`(기본 = 로고 보라, 다크 = 순수 빨강), `--glass-bg` .38 → .24·`--glass-blur` 14 → 18px. `EquipmentScreen.module.css`: `.node::before`를 원뿔 그라데이션 + 테두리 띠 마스크로(Running이면 1.6초에 한 바퀴), Pass는 `connFlash`(0.9초) 뒤 보라 테두리·빛·보라 V, 연결선 `--conn-soft`. 가운데 `.ring`은 JarvisRing의 호 그림을 감추고 같은 테두리 빛(working)·반짝임(done)·실패색(failed). 동작 줄이기면 돌지·반짝이지 않음. DESIGN.md 장비 연결 절. 시안 `mockups/aira-circle-styles.html`·`aira-circle-states.html`

**확인**: 웹 타입 검사·빌드, 데스크톱 빌드, 같은 CSS로 대기·연결 중·반짝임 최고점·연결됨 네 장면을 Edge 헤드리스로 그려 확인. 확인 못 함: 실제 장비 연결 화면에서 도는 속도·반짝임의 느낌(N.I.N.A.를 켜야 해서), 다크(빨강) 테마

---

## 2026-10-10 · [Claude] 드롭다운 길게, N.I.N.A. 알림 돌려주지 않기, Empire 로고 가리기·종료 때 닫기, 테마 두 가지(기본·다크), 장비 연결 시간 로그, 시안 둘

**요청**: 사용자 질문 9개에 대한 답 뒤 — 8 장비 변경 드롭다운이 짧음(바닥까지는 말고), 4 N.I.N.A. 알림을 닫으면 N.I.N.A.가 아이라 위로 올라옴(돌려주지 않을 수 있으면), 1 Wanderer Empire 시작 로고가 장비 연결 화면을 가림, 2 아이라가 켠 Empire는 종료 때 닫기, 6 밝게 테마 없애고 기본(지금 다크)·다크(지금 촬영) 두 가지(자동 전환 없음), 3 장비 연결이 오래 걸림 → 로그를 남기고 보고 다시 이야기(한 번 해 보고 안 되면 실패 + 재시도 버튼 방향), 9 파워박스 포트를 장비 특성으로(정지 시안), 7 원을 점검 카드 느낌으로(미리보기)

**변경**
- 8: `.deviceList` max-height 300px → min(540px, 50cqh), 망원경 목록 `ScopeList` → min(480px, 44cqh)
- 4: `NinaNotices.NoteUnknown`·`AddFallback` — 해석 규칙이 없는 오류·경고 원문을 기억했다가, 숨긴 알림 창 뒤 5초 안에 아이라 알림이 없으면 원문으로 "N.I.N.A. 알림"을 만들고 창은 돌려주지 않음. 로그에도 없을 때만 돌려주되 `WS_EX_NOACTIVATE`(눌러도 앞으로 오지 않음)
- 1: `BackgroundWindows` 아이라를 앞에 두는 감시 중 새로 뜬 창이 스스로 "항상 위"면 그 표시를 풀고 아이라를 다시 맨 위로
- 2: `ProgramCloser`가 N.I.N.A.를 닫은 뒤 아이라(서버)가 켜진 뒤에 켜진 Wanderer Empire·ASCOM.WandererBox 서버를 닫음 (그 전부터 켜진 것은 그대로)
- 6: `theme.ts` Theme = dark·night, 이름 "기본"·"다크", 저장된 light는 기본으로. `index.css` light 블록, SkyBackdrop·App.module.css·MainWindow의 light 제거. DESIGN.md 7장
- 3: 새 `ConnectTimingLog` — 데이터 폴더 `logs\connect-날짜.log`에 연결 시작·끝 시간과 장비마다 한 줄(결과·총 시간·단계별: PC에 보이는지·카메라 전원 켜기·N.I.N.A. 목록·N.I.N.A. 연결·허브 전압·허브 USB 출력·PHD2 확인). `EquipmentConnector.ConnectTimedAsync`
- 시안: `mockups/aira-hub-ports.html`(파워박스 포트 — 적도의 먼저/파워박스 먼저/파워박스 없음), `mockups/aira-circle-styles.html`(원 — 글래스/카드/섞음)

**확인**: 서버 테스트 195 통과·1 건너뜀, 웹 타입 검사·빌드, 서버·데스크톱 빌드, 시안 둘은 Edge 헤드리스 PNG로. 확인 못 함: 실장비에서 Empire 로고·알림 창·Empire 닫기·연결 시간 로그(장비 꺼짐), 길어진 드롭다운이 화면 위아래 끝의 원에서 잘리지 않는지

---

## 2026-10-10 · [Claude] 한 줄 설명(tagline) 없애기, 상태 줄 이름을 AIRA 별자리 글자로

**요청**: 부팅 화면의 "천체사진 조수"를 지우고 product.json의 tagline도 지우기. 상태 줄의 "아이라"(글자였음)를 로고의 AIRA 별자리 글자로

**변경**: product.json `tagline` 삭제 + 읽던 곳 모두(`Directory.Build.props` AppProductTagline — 키가 없으면 정규식이 JSON 전체를 값으로 잡으므로 줄째 뺌, `Product.Tagline`, 창 제목은 이름만, 웹 `PRODUCT.tagline`), `BootScreen`에서 설명 줄 뺌. 새 `web/src/assets/aira-mark.svg`(로고의 aira-v3 무리만 떼어 viewBox를 맞추고 id에 brand- — 부팅 로고와 겹치지 않게, 선 3.3 → 7), `BrandMark`(테마 넣어 인라인), `StatusBar` 이름 자리. DESIGN.md 부팅·상태 줄

**확인**: 웹 타입 검사·빌드, 서버·데스크톱 빌드, 상태 줄 크기(22px, 200%)로 Edge 헤드리스 그림, 시험 서버(5211)·브라우저에서 부팅 화면(로고·버전만)과 프로필 화면 상태 줄 왼쪽에 별자리 글자 + 버전. 확인 못 함: 밝게·촬영 테마의 색

---

## 2026-10-10 · [Claude] 부팅 화면에 새 로고 (aira-logo-v2)

**요청**: 사용자 로고 `mockups/aira-brand/aira-logo-v2.svg`를 부팅 화면에

**변경**: `web/src/assets/aira-logo.svg`(복사본 — 웹이 쓰는 원본), `BootScreen`이 이름·영어 풀이 글자 대신 로고를 인라인 SVG로(`?raw`, 지금 앱 테마를 data-theme에 넣음 — img로는 테마가 안 먹음), 가운데 정렬 너비 min(880px, 64cqw), 아래 버전·오류 (tagline "천체사진 조수"는 사용자 요청으로 뺌). DESIGN.md "부팅 화면" 절

**확인**: 웹 타입 검사·빌드, 데스크톱 빌드, 같은 배치를 Edge 헤드리스로 1600×1000에 그려 로고·설명·버전 위치 확인(다크). 확인 못 함: 실제 앱의 부팅 순간(짧게 지나감), 밝게·촬영 테마에서의 로고

---

## 2026-10-10 · [Claude] 관측지 목록 화면에서 진행 표시(레일) 숨기기, 종료를 레일에서 분리

**요청**: 관측지 목록 설정 화면에서 단계 레일이 남아 화면을 가림 — 그 화면에서는 레일을 보이지 않게. 이어서 "레일과 종료 기능은 분리해야 하지 않나" — 맞음(종료 흐름이 레일 안에 있어 레일을 숨기거나 빼는 결정이 종료에 묶였음)

**변경**: 새 `components/QuitControl`(`QuitProvider` — 확인 창·포커서 0·연결 끊기·N.I.N.A. 닫기·창 × 확인을 StepRail에서 그대로 옮김, `useQuit`), `App`이 맨 바깥에서 감쌈(창 × 확인은 단계 화면에서만, 요약의 hideExit면 바로 닫음 — 전과 같음, 단계 전 화면 문구는 "아이라를 종료합니다."). `StepRail`은 종료 버튼이 `useQuit()`만 부름. 레일 칸에 `hidden={phase === 'site'}` — 빼지 않고 숨겨 단계별 작업 목록 기억을 지킴. DESIGN.md 관측지 절·종료 절

**확인**: 웹 타입 검사·빌드, 데스크톱 빌드, `.rail`에 display 규칙이 없어 hidden이 먹는 것. 확인 못 함: 관측지 화면 실제 모습과 데스크톱 창에서 종료 버튼·창 ×(브라우저에는 종료 버튼이 없고, 관측지 화면까지 가려면 N.I.N.A.·장비 연결 필요)

---

## 2026-10-10 · [Claude] 프로필 삭제

**요청**: 프로필을 지울 방법이 없음 — 편집 화면에 삭제 버튼, 확인 창에서 삭제하면 프로필 선택 화면으로

**변경**: `ProfileStore.Delete`(목록에서 빼고 사진도 지움), `DELETE /api/profiles/{id}`, `profiles.ts` `deleteProfile`, `NewProfileScreen` 편집 때만 맨 아래 "프로필 삭제"(작은 글자, 가리키면 실패색) → `ConfirmDialog`("삭제", 지우는 중 busy) → `onDeleted`, `App`이 목록에서 빼고 프로필 선택 화면으로. DESIGN.md 프로필 절

**확인**: 시험 1개(지우면 목록·사진 없음, 두 번째는 false), 서버 테스트 195 통과·1 건너뜀, 웹·데스크톱 빌드. 시험 서버(5211)·브라우저: 시험 프로필 "삭제시험"을 만들어 편집 → 프로필 삭제 → 확인 창 삭제 → "프로필 선택" 화면, 화면 목록·서버 목록 모두에서 사라짐

---

## 2026-10-10 · [Claude] 새 앱 아이콘 (aira-icon-v2)

**요청**: 사용자가 만든 아이콘 `mockups/aira-brand/aira-icon-v2.svg`를 앱에 적용 (로고는 나중에)

**변경**: `src/Astro.Desktop/app.ico` 교체 — 16·20·24·32·40·48·64·96·128·256px PNG를 담은 .ico (64px 이상은 v2, 48px 이하는 새 `mockups/aira-brand/aira-icon-v2-tiny.svg` — 200% 화면의 작업 표시줄이 48px를 씀. 사용자가 작업 표시줄에서 다른 앱보다 작아 보인다고 해서 SVG의 그림자 여백(칸의 약 6%)을 잘라 타일이 칸을 꽉 채우게 렌더링. tiny — 사용자 요청: A 선을 굵고 밝게·바탕 타일 조금 밝게·잔별과 지면 빼고 A를 조금 키우고 망원경과 겹치지 않게 오른쪽으로, 망원경은 남김). `web/public/favicon.png`(64px)·`favicon.svg`. csproj 주석에 원본 위치

**확인**: Edge 헤드리스로 크기마다 투명 배경 PNG(모서리 알파 0), .ico를 System.Drawing으로 읽기, 빌드한 AIRA.exe에서 아이콘을 뽑아 새 그림인 것. 데스크톱 빌드. 확인 못 함: 작업 표시줄·Alt+Tab의 실제 모습(탐색기 아이콘 캐시 때문에 바탕 화면 바로 가기는 한동안 예전 그림일 수 있음)

---

## 2026-10-10 · 60682c3 [Claude] 프로필의 전원 배선 — 아이라가 믿고 쓰고, 실제와 다르면 조용히 고침

**요청**: 장비 전원이 어떻게 들어가는지(적도의 먼저/파워박스 먼저, 나머지 장비는 적도의 새들 또는 파워박스, 선 없으면 자체 전원)를 프로필에서 선 잇기로 설정. 설정하면 믿고, 안 하면 기본값(적도의 먼저)을 믿고, 구동 중 다른 증거를 보면 조용히 고쳐 그 뒤로 믿기. 나중에 추가한 장비는 파워박스(있으면). 시안 mockups/aira-power-wiring.html을 여러 번 고쳐 확정

**변경**: `Profiles/PowerWiring`(종류 → dc·mount·switch·null, `Default`, `SourceOf`, `BehindHub`, `With` — 적도의 ↔ 파워박스를 뒤집으면 앞이 DC 전원, `Validate`), `PowerWiringStore`(`Current`, `Learn` — 다르면 저장·로그만), `Profile.Power`, `ProfileStore.SetPower`·`Current`(마지막으로 고른 프로필), `GET/PUT /api/profiles/{id}/power`. `EquipmentConnector`: 허브 실패 시 배선상 허브 뒤 장비만 보류(전에는 모두), 허브 뒤 장비라도 PC에 보이면(`DevicePrecheck.CanSee`) 연결하고 허브 0V였으면 배선 고침, 허브 0V 안내를 배선별로, 적도의 먼저인데 허브 0V에서 적도의 연결 → 파워박스 ← DC 전원, 카메라 출력을 켜자 나타남 → 카메라 ← 파워박스, 장비가 안 보일 때 배선에 맞춘 한 줄(`WiringHint`). 웹 `PowerWiringPanel`(시안 그대로 — 상자 10개, 어디서든 끌기·차례로 누르기, 선 누르면 끊김, 이을 수 없으면 한 줄), `NewProfileScreen` 메모 아래 "전원 배선" 버튼(요약 한 줄) → 오른쪽 패널, 손댄 경우만 프로필 저장 때 함께 저장. `profiles.ts` `getPower`·`savePower`·`DEFAULT_WIRING`. DESIGN.md 프로필 화면 절, docs/POWER_LAYOUT_PLAN.md 최종 규칙

**확인**: PowerWiringTests 4개(기본값·허브 뒤, 뒤집기, 잘못된 배선, 기록하고 계속 믿기), 서버 테스트 194 통과·1 건너뜀, 웹·데스크톱 빌드. 시험 서버(5211)·브라우저: 시험 프로필 편집 → 패널 열림(프로필은 왼쪽 가운데로), 파워박스→적도의 끌기 "파워박스 먼저", 카메라→파워박스(반대 방향) "카메라 파워박스", 장비끼리 막음, 적도의→파워박스 뒤집기, 저장 후 API에 custom=true로 남음. 확인 못 함: 패널 모양(미리보기 캡처가 크기 흉내에서 깨짐), 실장비에서 배선을 고치는 증거들(파워박스 0V 칸 이름도 아직 미확인)

---

## 2026-10-09 · [Codex] 부팅 SVG 로고 연결부·지면·별자리 보정

**요청**: 마운트 어색함, 삼각대 발과 지면 불일치, 외따로 있는 풀, 별과 연결선 겹침 수정.

**변경**: `mockups/aira-brand/aira-logo.svg` 받침·축·피벗과 다리 접점을 분리. 세 발 좌표를 바닥 베지어 곡선으로 계산. 오른쪽 풀에 두 획 추가. AIRA 별자리 연결선을 각 별 앞에서 짧게 끊고 3개 테마 미리보기 갱신.

**확인**: SVG XML 파싱 및 3개 미리보기 교체 확인. 발 좌표의 곡선 일치 계산 확인. 시각 검증 미실시.

---
## 2026-10-09 · [Codex] AIRA SVG 로고·아이콘 시안

**요청**: 촬영 망원경 로고를 테마 대응 SVG로 제작.

**변경**: `mockups/aira-brand/`에 부팅 로고·정사각형 아이콘 SVG, 3개 테마와 아이콘 크기를 비교하는 preview.html, 적용 설명 추가. 한글 이름은 경로로 작성, 영문 설명 첫 글자와 별 강조. 앱 연결은 미실시.

**확인**: 두 SVG XML 파싱 통과. 화면 렌더링 시각 검증 미실시. ICO 변환은 크기별 디자인 확정 후 진행.

---
## 2026-10-09 · 6acd549 [Codex] 성운 움직임을 마지막 두 설정의 중간으로 조정

**요청**: 성운 움직임을 최종본과 직전 본의 중간값으로.

**변경**: `mockups/aira-background.html` 성운 이동·회전·크기 변화 폭과 각속도를 두 설정의 평균으로 조정. 최신본의 두 층 방향 유지. 별빛·혜성 유지.

**확인**: JavaScript 구문 검사 통과. 시각 검증 미실시.

---
## 2026-10-09 · 6acd549 [Codex] 배경에 느린 혜성 한 개 추가

**요청**: 좌하단에서 우상단으로 약 30분에 걸쳐 이동하고, 사라진 뒤 비슷한 경로로 다시 등장하는 혜성.

**변경**: `mockups/aira-background.html`에 단일 혜성·희미한 꼬리 추가. 매 회 28~32분, 시작점·제어점·끝점을 작은 범위에서 무작위 지정한 곡선 경로. 화면 밖 끝점에 도달하면 재생성. 테마 색상 사용, 동작 줄이기에서는 생략.

**확인**: JavaScript 구문 검사 통과. 실제 30분 재생·시각 검증은 미실시.

---
## 2026-10-09 · 6acd549 [Codex] 성운 움직임 추가 강화

**요청**: 배경 성운의 움직임을 더 역동적으로.

**변경**: `mockups/aira-background.html` 성운 이동 주기를 줄이고 이동 폭·회전·확대 축소 폭을 확대. 두 층의 수평 이동과 회전을 반대 방향으로 구성. 별빛·색상·불투명도 유지.

**확인**: JavaScript 구문 및 변경값 검사 통과. 시각 검증 미실시.

---
## 2026-10-09 · 6acd549 [Codex] 배경 시안의 움직임·라이트 대비 조정

**요청**: 별 점멸을 더 자주·빠르게, 성운 움직임을 더 역동적으로, 라이트 테마 가시성 개선.

**변경**: 같은 `mockups/aira-background.html`에 별 생성 간격 1.4~2.6초, 지속 5~9초, 최대 6개 적용. 성운 두 층에 서로 다른 이동·회전·확대 축소 적용. 라이트 전용 청보라 색상과 성운 불투명도 강화.

**확인**: JavaScript 구문 검사 통과. 화면 시각 검증은 미실시(앞선 로컬 파일 브라우저 접근 제한).

---
## 2026-10-09 · 6acd549 [Codex] AIRA 배경 시안 — 희미한 성운과 드문 별빛

**요청**: 기존 배경 시안에 은은하게 움직이는 성운과 소수의 별이 천천히 밝아졌다 사라지는 효과 적용.

**변경**: `mockups/aira-background.html` 직접 수정. 절차적으로 생성한 성운 두 층을 느리게 이동, 별빛은 최대 3개·12~20초 주기. 테마·세기·화면 미리보기 유지. 30fps 제한, 숨겨진 탭에서 정지, 동작 줄이기 설정 대응.

**확인**: JavaScript 구문 검사 통과. 브라우저 도구가 file 프로토콜 접근을 차단해 실제 화면·애니메이션은 시각 검증하지 못함.

**남은 것**: 브라우저에서 밝기와 움직임 확인 후 앱 반영 여부 결정. 현재는 HTML 시안만 변경.

---
## 2026-10-09 · 6acd549 [Claude] 장비 전원이 꺼져 있으면 N.I.N.A.에 맡기기 전에 막기, 아이라가 켠 N.I.N.A.·PHD2는 종료 때 닫기

**요청**: 장비 전원을 모두 끈 채 앱을 켰다 끄자 앱이 꺼진 뒤 N.I.N.A. 오류·경고 토스트가 뜸 — 원인 확인. 가이드 카메라가 없으면 PHD2 연결 전에 막기, 아이라를 끌 때 N.I.N.A.·PHD2 닫기, 그리고 장비 전원 상태를 먼저 본 뒤 N.I.N.A. 연결을 확인하는 방법

**변경**: 원인(N.I.N.A. 로그) — 사용자 실행이 N.I.N.A.를 켜고(22:02:43) 허브는 USB만으로 연결, 가이더 연결에서 PHD2가 "equipment failed to connect: camera mount"(22:04:18), 종료로 PHD2 연결이 끊김(22:04:19). 이 둘은 아이라가 꺼진 뒤라 N.I.N.A. 토스트로 남음. `DevicePrecheck.GuideCameraVendor`·`IHostDevices.Phd2CameraName`(PHD2 레지스트리 currentProfile → camera\LastMenuchoice, 실기 "ToupTek Camera") — 가이더 연결 전에 그 USB가 없으면 PHD2를 부르지 않음. `EquipmentConnector.HubInputVoltsAsync`(허브 읽기 전용 칸 중 이름에 입력·전압 — 8초까지 다시 읽어 최대값) 5V 미만이면 허브 실패 → 뒤 장비 보류. `Engine/ProgramCloser`(`RealWrapDevices.CloseProgramsAsync` 내용을 옮김 + 아이라가 켠 N.I.N.A.인지 — `EngineStarter`가 표시), `POST /api/engine/close`(아이라가 켰고 촬영 중이 아니면 닫음), `StepRail` 종료 순서 포커서 0 → 연결 끊기 → "N.I.N.A.와 PHD2를 닫는 중입니다…". DESIGN.md 종료 절, docs/PRECHECK_DESIGN.md 진행 표

**확인**: DevicePrecheckTests(가이드 카메라 없음 → 막음, PHD2 카메라 모르면 통과, 입력 전압 칸 이름 5가지), 서버 테스트 190 통과·1 건너뜀, 웹·데스크톱 빌드. 확인 못 함: Empire가 N.I.N.A.에 주는 입력 전압 칸 이름과 DC를 뺐을 때의 값(이름이 안 맞으면 막지 않고 지금처럼 진행), 종료 때 N.I.N.A.가 닫히는 모습(장비가 연결된 채 닫을 때 N.I.N.A.가 묻는지)

---

## 2026-10-09 · 6acd549 [Claude] 장비 연결 화면의 원을 글래스로

**요청**: 배경 효과 위에서 자비스 원(가운데)과 장비 원의 채운 회색이 어색함 — 외곽선만 또는 글래스 효과 원이 낫지 않을까

**변경**: 글래스로 (외곽선만은 연결선·흡수되는 장비 원이 가운데 원 안에 비쳐 보여서 고르지 않음). `index.css` 테마마다 `--glass-bg`·`--glass-bg-strong`·`--glass-line`, 공통 `--glass-blur`(blur 14px). `EquipmentScreen.module.css` `.center`·`.fromRing`·`.node`(기본 테두리 `--glass-line`, 확인됨·가리킴·고름은 strong)·`.editCircle`. DESIGN.md 장비 연결 절

**확인**: 웹·데스크톱 빌드. 확인 못 함: 화면에서 보기(장비 연결 화면까지 가려면 N.I.N.A.를 켜야 해서 시험 서버로 가지 않음), backdrop-filter가 움직이는 배경 위에서 노트북 GPU에 주는 부담

---

## 2026-10-09 · 6acd549 [Claude] 앱 전체 배경에 성운과 별 (Codex 시안, 세기 1.1), 점검 카드 외곽선 조금 강조

**요청**: Codex가 만든 배경 시안(mockups/aira-background.html — Codex가 내 시안 파일을 "성운과 별"로 다시 씀)을 세기 1.1로 앱에 적용, 점검 카드 외곽선을 아주 조금 더 강조

**변경**: `components/SkyBackdrop`(시안의 그리기 코드를 그대로 옮김 — 성운 두 층·반짝이는 별·혜성, 테마는 `data-theme`을 지켜보다 바뀌면 성운 무늬를 다시 만듦, 무늬는 첫 화면 뒤 300ms에 만들고 1.6초 동안 나타남, 30fps·창 가려지면 멈춤·동작 줄이기면 정지). `App.tsx` 창(.window) 맨 아래 층에 두고 `.shell`(position: relative)·`.rail` 바탕을 투명으로. `PreflightScreen` 카드 외곽선 `--line` → `--line` 72% + `--text`. DESIGN.md 2장 배경 효과(그림 금지 규칙의 예외로 사용자 선택)

**확인**: 웹·데스크톱 빌드, 시험 서버(5211)·브라우저에서 프로필 화면 뒤로 성운·별이 깔리고 화면 글이 위에 보임(캔버스가 창 크기와 같음) 뒤 서버 종료. 확인 못 함: 1920×1200 틀(고정 배율)에서의 모습(미리보기 캡처가 크기 흉내에서 깨짐), 바탕색으로 속을 채운 요소(장비 연결 가운데 원 등)가 성운 위에서 구멍처럼 보이는지, 저사양 노트북 CPU 부담

---

## 2026-10-09 · 6acd549 [Claude] 포커서를 0에 두고 끝내기 — 다음 점검에서 노브를 손으로 맞추지 않게

**요청**: 포커서를 다시 달 때마다 노브를 손으로 0에 맞추는 과정을 없애고 싶음 (지난 위치를 그대로 쓰면 노브를 조금만 돌려도 어긋남). 의논 끝에 "끝낼 때 0으로 돌려놓고, 다음에 0에서 시작"으로

**변경**: `Prepare/FocuserPark`(`ParkAsync` — 촬영·다른 작업 중이 아니면 0으로 보내 도착 확인 뒤 기록, 90초 제한, 모의 모드는 기록 안 함 / `ReadyAsync` — 기록이 있고 포커서가 지금도 0), `FocuserParkStore`(focuser-parked.json — `RealFocusDevices`가 0이 아닌 곳으로 움직이거나 자동초점을 시작하면 지움). `IFocusDevices.ConnectedAsync`. `PackTask`가 연결 끊기 전에 0으로(못 해도 정리는 계속). `GET /api/focuser/parked`, `POST /api/focuser/park`, `/prepare/focuser-zero`는 ready면 0점을 잡지 않음. 웹: `StepRail` 종료 확인 → "포커서를 0으로 되돌리는 중입니다…" → 기존 장비 연결 중단·종료, `PreflightScreen`이 ready면 포커서 0점 카드를 빼고 5장(정하기 전에는 덱 숨김, 1.5초 넘으면 6장). DESIGN.md 점검 카드덱 절. FocuserPark는 PrepareFlow를 필요할 때 꺼냄(PackTask가 FocuserPark를 받아 생성자로 받으면 서로 기다림)

**확인**: FocuserParkTests 3개(보내고 기록 → ready, 기록 있어도 0이 아니면 묻기, 파일 기록 유지·지움), 서버 테스트 184 통과·1 건너뜀, 웹·데스크톱 빌드. 시험 서버(5211)에서 `/api/focuser/parked`·`/api/mount/home` 응답(의존성 순환 없음) 뒤 종료. 확인 못 함: 실장비에서 종료 때 0으로 가는 것과 다음 점검 5장(장비 꺼짐), Oasis가 전원을 껐다 켜도 위치를 기억하는지(사용자가 1,225에 둔 채 끔 — 다음에 켰을 때 위치로 확인)

---

## 2026-10-09 · 6acd549 [Claude] 출발 전 점검을 카드덱으로 (3×2 타일 대신)

**요청**: 점검 화면의 카드 6장이 널려 있는 배치를 바꾸기 — 정사각형 그림 카드 6장을 화면 가운데에 쌓아 첫 장부터 차례로, 덱 오른쪽에 지금 카드 설명, 누르면 V가 붙고 터치 피드백처럼 눌렸다가 떠오르며 투명해져 사라지고 아래 카드가 보이게. 이어서: 덱만 가운데면 오른쪽 글 때문에 치우쳐 보이니 덱과 글을 함께 가운데로, 사라질 때 부자연스러움(누를 때 눌림과 사라질 때 눌림이 겹침) 확인. 앱 전체 배경의 은은한 효과는 HTML 시안으로 먼저

**변경**: `PreflightScreen` — 체크 상태를 넘긴 장 수(`done`) 하나로, 카드 상태 top·under·gone(뒤 카드는 `--depth`만큼 오른쪽 아래로 비켜 어둡게), 넘긴 카드는 `away` 애니메이션 — 원인: 누를 때 `:active` 눌림 뒤 사라짐 애니메이션이 크기 1에서 다시 시작해 또 눌렸음 → 눌린 크기(0.96)에서 이어서 떠오르며 투명(520ms)으로. 덱 + 설명을 한 묶음으로 flex 가운데(설명 칸 448px = 가장 긴 질문 한 줄, 덱 왼쪽·설명 오른쪽 여백 같음), 설명 칸(n/6·이름·질문)은 크기 고정에 글만 투명도로 바뀜. 배경 효과 시안 `mockups/aira-background.html`(A 별의 일주운동 · B 궤도 · C 빛 · 모두, 세기, 테마 셋, 점검 화면을 올려 보기 — 앱에는 아직 넣지 않음). "모두 확인 완료"는 남은 카드를 220ms 간격으로 같은 동작으로 넘김, 6장 뒤 자동 진행은 그대로. 되돌리기(다시 눌러 취소)는 없앰. DESIGN.md 3장 "체크리스트: 카드덱"으로 바꿈

**확인**: 웹·데스크톱 빌드. 임시 미리보기 페이지(지운 상태)로 1920×1200 틀에서 덱이 가운데·설명이 오른쪽, 카드를 누르면 V·흐린 그림·초록 외곽선으로 눌렸다가 사라지고 다음 설명(2/6 망원경 캡)으로 바뀜, "모두 확인 완료" 뒤 다음 단계 호출까지. 가운데 정렬은 덱 왼쪽 468px·설명 오른쪽 468px로 잼. 확인 못 함: 실제 앱 창에서 움직임의 느낌(미리보기 창이 숨겨져 애니메이션은 멈춘 화면으로만 봄)

---

## 2026-10-09 · 6acd549 [Claude] 사전 점검 남은 것: PHD2 장비 연결 창 미리 확인(F03), 구름 예보 알림

**요청**: 하기로 했던 남은 작업 중 지금 할 수 있는 두 가지 — PHD2 장비 연결 창을 넘겨받기 전에 확인, 극축 정렬·캘리브레이션 전 구름 예보 알림

**변경**: `Prepare/Real/Phd2Windows`(phd2 프로세스의 보이는 창 제목 "장비 연결"/"Connect Equipment" — 이 창은 닫아도 숨겨진 채 남아 보이는 것만 셈), `IPolarDevices.Phd2DialogOpenAsync`, `PolarTask.HandOverAsync`가 열려 있으면 세 번 시도하지 않고 "창을 닫은 뒤 다시 확인"(다시 확인 / 극축 정렬 건너뛰기), 모의 고장 `polar.dialog`. `Flow/CloudNotice`(지금 시각 구름양 예보 70% 이상이면 문장 — 막지 않음, 예보를 못 받으면 말 안 함, 10초 제한), `PrepContext.Clouds`(`PrepareStarter`가 `TonightService.CloudsAsync`로), 극축 정렬 넘겨받기·극 찾기 안내, 극축 정렬 끝 안내(캘리브레이션 시작·가이딩 없이 진행 버튼 앞), 캘리브레이션 안내·별 선택 안내에 덧붙임. docs/PRECHECK_DESIGN.md 진행 표

**확인**: 시험 3개 추가(창이 열려 있으면 먼저 묻고 넘겨받기 시도 없음, 구름 90%면 안내·끝 문장에 알림, 시각 경계), 서버 테스트 181 통과·1 건너뜀, 웹·데스크톱 빌드. 실기: PHD2 한국어 번역(messages.mo)에서 "Connect Equipment" = "장비 연결", 실행 중 PHD2 창 목록에 이 창이 숨겨진 채 있는 것 확인. 확인 못 함: 장비 연결 창을 연 상태에서 아이라가 막는 모습, 구름 알림이 화면에 뜨는 모습(오늘 예보에 따라)

---

## 2026-10-09 · c60e4e2 [Claude] 자동초점 진행을 아이라 하늘 화면에 (N.I.N.A. 자동초점 창 대신)

**요청**: 초점 맞출 때 위에 뜨는 별도 창(N.I.N.A. 자동초점 창으로 보임)을 아이라가 대신 그려 주기

**변경**: `NinaRig.AutofocusPointsAsync`(event-history의 AUTOFOCUS-POINT-ADDED — 위치·HFR가 잴 때마다 남음, 실기 확인), `RealFocusDevices.AutofocusAsync`가 재는 동안 점을 하나씩 보냄(끝난 기록은 실시간으로 못 받았을 때만). `NinaApiClient.GetBytesAsync`, `LiveImages` "nina"(prepared-image — N.I.N.A.가 보여 주는 마지막 사진, JPEG, 2초에 한 번). `FocusTask` 하늘 화면 바탕을 그 사진으로. `NinaAutofocusWindowHider` — N.I.N.A. 자동초점 창(한국어 제목 "오토포커스", 본 창에 딸린 창, 끝나도 남음 — 실기로 제목 확인)을 아이라 창이 앞에 있을 때 숨김. 웹 `LiveView.FocusPanel`(오른쪽 세로 가운데 — 위치·HFR 점과 선, 가장 작은 별, 별 못 찾은 지점은 빈 점), 자동초점 중 사진 새로 받기

**확인**: 서버 테스트 178 통과·1 건너뜀, 웹·데스크톱 빌드. prepared-image가 JPEG(43KB)를 주는 것·event-history 측정점 형식 확인. 실기: 포커서를 1,225로 옮겨 N.I.N.A. 자동초점 실행 → 창 제목 "오토포커스", 측정점 이벤트 확인, 끝나고 1,225로 복귀(실내라 별 없음). 확인 못 함: 아이라 화면에서 사진·측정점이 뜨는 모습과 창 숨김(시험 중 아이라 창을 앞으로 가져올 수 없음)

---

## 2026-10-09 · 0ff765c [Claude] 자동초점이 포커서 끝에서 시작해 멈추던 문제, 초점 위치 기억, 적도의 홈 버튼

**요청**: 실내 실행에서 자동초점이 0에서 멈춰 반대로 움찔거림(비프음·촬영은 됨). 레일 아래 종료 왼쪽에 적도의 홈 버튼 — 움직이면 안 될 때는 비활성, 아이콘으로 구분

**변경**
- 원인(N.I.N.A. 로그): 기억한 초점이 없어 지금 위치 0(포커서 0점 = 안쪽 끝)에서 시작 → N.I.N.A. 자동초점이 500·375·250·125·0 뒤 0보다 안쪽으로 못 감. `IFocusDevices.AutofocusReachAsync`(N.I.N.A. 처음 칸 수 × 칸 크기 + 백래시 + 한 칸 — 지금 1,225), `FocusTask`가 시작점을 [최소+거리, 최대−거리]로 옮김
- `SavedFocus`/`SaveFocus` — 망원경별 맞은 초점 위치를 focus-memory.json에 (포커서 0점이 늘 안쪽 끝이라 숫자의 뜻이 밤마다 같음), 같은 밤 기억이 없으면 이 값에서 시작. `NinaRig.ProfileAsync`
- `Prepare/MountHome`(`GET/POST /api/mount/home` — 촬영 중·작업 진행 중·적도의 잠금·가이딩 중·미연결이면 이유와 함께 막음, 홈 뒤 추적 끔), `PrepareRunner.ActiveTaskTitle`, `ShootSession.Active`. `StepRail` `HomeButton`(집/자물쇠 아이콘, 확인 창, 2초마다 상태). DESIGN.md 레일 규칙

**확인**: 끝에서 시작하면 1,225에서 시작하는 시험, 서버 테스트 178 통과·1 건너뜀, 웹·데스크톱 빌드. 모의 서버·브라우저에서 홈 버튼이 장비 연결 화면에 "움직여도 됨"으로 뜸. 확인 못 함: 실장비에서 자동초점(실내라 별 없음 — 시작점만 확인 가능), 홈 버튼 화면 모양·잠김 상태를 눈으로

---

## 2026-10-09 · 62d5622 [Claude] 계획에서 카메라가 못 쓰는 ISO 막기 (Codex C06)

**요청**: 사전 점검 묶음의 남은 항목(C06) 마저

**변경**: `Sky.Rig`에 `IsoMin`/`IsoMax`(연결된 카메라의 N.I.N.A. GainMin·GainMax — `TonightService.ReadProfileAsync`), `PlanTools.SetSettings`가 범위 밖 ISO면 계획에 넣지 않고 범위를 알려 AI가 다시 고르게. docs/PRECHECK_DESIGN.md 진행 표

**확인**: 서버 테스트 177 통과·1 건너뜀, 데스크톱 빌드. 실장비 X-T5 범위 125~12800(N.I.N.A. 카메라 정보). 확인 못 함: 범위 밖 ISO를 AI가 실제로 다시 고르는 대화(Gemini 할당량 — 시험하지 않음), PlanTools 단위 시험 없음

---

## 2026-10-09 · 83e6139 [Claude] 사전 점검 마무리: 저장 폴더·솔버·시뮬레이터·관측지, 드라이버 없음·포트 안내·N.I.N.A. 여러 개, 포커서 일련번호, 캘리브레이션 버튼 옆 "가이딩 없이 진행"

**요청**: Codex 점검 항목과 사전 점검 설계 항목을 다 구현한 뒤 커밋(중간에 묻지 않기). 남쪽이 막힌 곳에서 캘리브레이션이 헛되이 움직이는 문제(G07)는 따로 묻지 말고 이미 있는 "캘리브레이션 시작" 버튼을 강화하자는 사용자 제안

**변경**: `DevicePrecheck.Storage`(저장 폴더 있음·쓰기·남은 공간 2GB 미만 막음·20GB 미만 경고·동기화 폴더 경고 — I01~I03), `Solver`(ASTAP 실행 파일과 별 목록 *.1476·*.290 — H01), `Simulator`(실장비 모드에서 시뮬레이터면 경고 — A06). `EquipmentConnector`: 망원경 칸에서 솔버, 연결된 장비마다 시뮬레이터, 카메라 칸에서 저장 폴더. docs/PRECHECK_DESIGN.md 진행 상황 표(C03은 연결 직후 알 수 없음 — N.I.N.A. 카메라 정보에 화질 없음). `IPrepTask.StartAlternatives`·`PrepContext.StartChoice`·러너 "next:선택" — 끝 버튼 옆에 다음 작업의 다른 시작을 둠. 캘리브레이션 시작 버튼을 "캘리브레이션 시작 · 남쪽 하늘로 이동"으로, 옆에 "가이딩 없이 진행"(움직이지 않고 그날 가이딩 없음). Codex 항목: **A05** `EngineStarter` N.I.N.A. 여러 개 경고(`CheckItem.Warn`), **B02** `IHostDevices.UsbSerialPorts` — COM 포트가 없으면 지금 보이는 USB 직렬 포트 안내, **B04** 포트 점유 알림 규칙, **B05** `WaitListedAsync`가 목록에 끝내 없으면 연결 시도 없이 원인 후보, **D02** `DevicePrecheck.MountSite` 적도의 관측지 비교, **E03** `OasisSdk.SetZero(serial)` 포커서가 여럿이면 드라이버 일련번호로만. C06(ISO 범위)은 남김 — 설계 문서 진행 상황 표

**확인**: PostConnectCheckTests 8개·포트 안내·가이딩 없이 진행 시 이동 0회 시험, 서버 테스트 177 통과·1 건너뜀. 실장비 장비 연결: 다섯 장비 통과(저장 D:\Photo\N.I.N.A, ASTAP·D50 있음 → 막지 않음)

---

## 2026-10-09 · f5dfb02 [Claude] 카메라 전원을 허브 출력으로 켜기, PHD2가 카메라를 못 찾을 때 정확한 안내

**요청**: 사전 점검 이어서 — 카메라가 허브 출력(DC2 8V)에서 전원을 받으면 아이라가 켜기(J05), PHD2 "선택한 ToupTek 카메라를 찾을 수 없습니다"를 정확히 짚기

**변경**
- `CameraPowerStore`(camera-power.json — 배터리 또는 허브 출력 이름), `GET/POST /api/equipment/camera-power`. 장비 변경의 카메라 드라이버 목록 아래 "카메라 전원: 배터리 / 허브 DC2…" — 출력을 고를 때 "Empire에서 그 출력 전압을 어댑터에 맞췄나요?" 확인(아이라는 전압을 읽거나 바꾸지 못함)
- `EquipmentConnector`: 카메라가 PC에 없고 카메라 전원이 허브 출력이면 그 출력을 켜고(`SetHubOutputAsync` — 목록 순서, 10초까지 다시 읽기, `EnsureHubUsbOnAsync`도 이것으로) 카메라를 30초 기다림. 못 켜거나 안 나타나면 할 일 안내
- `Phd2Check.ConnectEquipmentAsync`(set_connected + PHD2 Alert 받기)·`CameraNotFound`, 가이드 카메라가 꺼져 있으면 PHD2에 연결을 시켜 알림으로 원인 — 기억한 카메라를 못 찾으면 "다른 USB 자리에 꽂으면 PHD2가 다시 골라야 함" 안내, 다른 알림은 그 문장을 함께

**확인**: Phd2ConnectTests 3개, 서버 테스트 167 통과·1 건너뜀, 웹·데스크톱 빌드. 실장비: 카메라 전원 = DC2로 두고 N.I.N.A. 카메라 연결 끊기·DC2 끄기(14:56:41 카메라 USB 사라짐) → 장비 연결 실행 → 아이라가 DC2를 켜 14:57:05 카메라 나타남, 14:57:07 연결, 다섯 장비 모두 19초. 확인 못 함: 장비 변경 화면의 카메라 전원 고르기를 눈으로(시험은 API로 설정), PHD2 카메라 못 찾음 경로(사용자가 이미 다시 골라 재현 불가 — 가짜 시험만)

---

## 2026-10-09 · bd61eaa [Claude] 장비 연결 직전 점검 (사전 점검 1차)

**요청**: N.I.N.A.가 오류를 내기 전에 아이라가 먼저 확인해 막거나 준비시키기 — Codex 검증 목록과 docs/PRECHECK_DESIGN.md를 실장비가 연결된 지금 적용

**변경**: `Engine/DevicePrecheck` — 연결 직전에 드라이버(ASCOM 프로필)의 COM 포트가 PC에 있는지(B01), 아는 제조사 USB 장치가 있는지(카메라 후지·캐논·니콘·소니, Oasis 포커서 — C01·E01, 10초까지 기다림), Empire가 켜진 뒤 USB가 다시 꽂혔으면 Empire를 닫아 N.I.N.A. 연결이 새로 띄우게(J01·J02). 모르는 장비·확인 불가는 막지 않음. `EquipmentConnector`가 실패면 N.I.N.A.를 부르지 않고 할 일을 알림, 허브 연결 뒤 USB 출력을 켜고 다시 읽어 확인(`EnsureHubUsbOnAsync` — index는 목록 순서). `IHostDevices`(시험용 가짜). 출력을 바꾼 뒤 다시 읽기는 10초까지 1초마다(N.I.N.A.가 출력 값을 몇 초 늦게 갱신 — 켠 뒤 2초에는 아직 0). PHD2 연결 실패 알림은 connect 종류로(장비 연결 화면에서 겹치지 않게)·원인 문장 보강. System.Management 패키지

**확인**: DevicePrecheckTests 7개, 서버 테스트 164 통과·1 건너뜀, 데스크톱 빌드. 실장비(14:30, 시험 서버로 장비 연결): 허브·적도의·포커서 정상, 꺼진 카메라는 연결 전에 막혀 N.I.N.A. 오류 없음(로그 확인). 가이더는 PHD2가 USB 재연결로 기억한 카메라를 못 찾음("선택한 ToupTek 카메라를 찾을 수 없습니다") — 사용자가 다시 고른 뒤, DC2(8V)로 카메라를 켜고 다시 실행: 다섯 장비 모두 8초에 연결, 새 N.I.N.A. 오류 없음. 확인 못 함: COM 포트 없음·Empire 재시작 경로(실물 상황을 일부러 만들지 않음, 가짜 시험만)

**남은 것**: PHD2 Alert로 원인 짚기, 출력 전압–장비 범위 점검(J06+), 카메라 화질(C03), 점검 단계 항목(I·H)

---

## 2026-10-09 · 29d1541 [Codex] 오류 검증 목록의 읽기 조회·가짜 응답 검증

**요청**: Codex가 검증 가능한 부분을 실행하고 문서에 표시해 Claude와 중복 작업을 줄이기.

**변경**: `docs/codex/AIRA_ERROR_VERIFICATION.md` 120개 행에 검증 상태 열과 7절 증거/후속 항목 추가. 47개 행에 확인 범위 표시, 73개 미실시. API 응답 스키마·알림 폐기/중복 갱신 문제 입력과 정상 처리 범위를 구분.

**확인**: AA_REAL=0·AA_SIM=0 서버 테스트 155 통과·1 건너뜀(실장비/프로그램 시뮬레이터 opt-in 14개는 조기 반환). 실행 중 NINA Advanced API 2.2.15.2 읽기 조회: 장비 5종 미연결. 실제 로그 27개 재생, 별도 가짜 HTTP/로그/센서 테스트. 장비 연결·이동·촬영·설정 변경 및 앱 코드 수정 없음.

**남은 것**: 문서 7절의 수정 후보·UI 확인 및 항목별 남은 실기 조건. 기존 미커밋 변경 유지, 커밋·푸시하지 않음.

---

## 2026-10-09 · 29d1541 [Codex] AIRA 오류 예방·감지·복구 검증 목록

**요청**: 클로드가 N.I.N.A./Advanced API로 확인할 수 있도록 가능한 오류 사례와 사전 확인·복구 방법을 폭넓게 정리.

**변경**: `docs/codex/AIRA_ERROR_VERIFICATION.md` 추가. 장비 연결·촬영·마무리·알림 전달까지 12영역 120개 검증 시나리오, 읽기/가짜 응답/시뮬레이터/실기 구분, 조회 후보와 핵심 시험 12묶음·결과 기록 양식. 최신 미커밋 알림 처리도 검증 대상에 포함하되 확정 결함 목록과 구분.

**확인**: HEAD a652971과 현재 코드·HISTORY·API 대응표, 공식 Advanced API/PHD2/NINA 문서 대조. 문서 항목 수·ID 중복·diff 공백 검사. 실제 API 호출·장비 제어·앱 실행·코드 수정 없음.

**남은 것**: 항목별 실제 응답/이벤트/로그 및 복구 결과 검증. 원본 토스트 숨김 시 중요 경고 누락 여부 우선 확인. 기존 작업 트리 변경 유지, 커밋·푸시하지 않음.

---

## 2026-10-09 · 29d1541 [Claude] N.I.N.A. 오류를 아이라 말로, N.I.N.A. 알림 창 숨기기

**요청**: N.I.N.A. 오류·경고 알림(영어, "MISSING LABEL")을 보지 않게 아이라가 해석해서 알려 주기 — 2단계(N.I.N.A. 알림 숨기기)까지. 아이라 알림 자리는 N.I.N.A. 알림(오른쪽 아래)과 겹치지 않게

**변경**
- `Engine/NinaNotices.cs`: `NinaLogWatcher`(N.I.N.A. 로그를 1초마다 따라 읽음 — 켤 때는 파일 끝부터, 여러 줄 예외를 한 항목으로, 마지막 항목은 2초 기다림), `NinaLogRules.Interpret`(COM 포트 없음·Empire·적도의·카메라·포커서 연결, 적도의 통신, 솔빙, 가이드 별·PHD2 통신·안정화, 포커서 멈춤, 카메라 타임아웃 → 무슨 일/왜/이렇게, 소음은 버림), `NinaNotices`(1분 안 같은 알림은 횟수만), `NinaToastHider`(아이라 창이 앞일 때 N.I.N.A. "NotificationsWindow"만 숨김). `GET /api/nina/notices?after=`
- Codex 검증 목록(docs/codex/AIRA_ERROR_VERIFICATION.md) 반영: **A04** `NinaApiClient` — 객체가 아니거나 Success=true가 없으면 실패. **L04** "Sequence contains no matching element"는 Connection.cs DeviceConnect에서만 버림, "No switch found for index"는 버리지 않고 전원 허브 알림(아이라는 스위치를 바꾸지 않음 — 이전 주석이 틀림). **L06** 반복 알림은 30초마다 새 번호로 다시 전하고 화면은 같은 제목을 갱신. **L09·L03** N.I.N.A. 알림 창은 바로 숨기되 5초 안에 아이라 알림이 없으면 되돌려 보임(해석 못 한 알림이 사라지지 않게). **L05**(화면별 종류 숨김)는 실촬영에서 놓치는 경우를 본 뒤로 미룸
- docs/PRECHECK_DESIGN.md: 사전 점검 설계(사용자 요청 — N.I.N.A. 오류 전에 확인·막기·준비시키기), 낮 실장비 읽기 조사 기록(장치 식별, Empire 재연결, 출력 바꾸기 index는 목록 순서)
- 웹: 공통 `DiagnosticCard`(무슨 일/왜/이렇게/원본 접기, tone fail·warn) — `NinaLostCard`도 이것으로. `NinaNoticeCard`(2초마다, 화면이 스스로 보이는 종류는 받을 때 버림 — App `NOTICE_HIDE`). DESIGN.md "N.I.N.A. 오류 알림"

**확인**: 실제 N.I.N.A. 로그 전체(10월 1~9일)를 넣어 분류 — 일반 문장으로 빠지는 건 5건뿐. NinaLogRulesTests 8개, 서버 테스트 157 통과·1 건너뜀. 아이라를 켠 채 꺼진 카메라 연결로 N.I.N.A. 오류를 만들어 3초 안에 카드("카메라에 연결하지 못했습니다") 확인(창 캡처). 확인 못 함: N.I.N.A. 알림 창 숨기기 — 시험 중 아이라 창을 앞으로 가져올 수 없어(Windows가 백그라운드 프로그램의 창 전환을 막음) 사용자 확인 필요

---

## 2026-10-09 · a652971 [Claude] 제품 이름을 아이라(AIRA)로

**요청**: 가칭 AA 대신 정식 이름 — 사용자가 "아이라"로 정함(Claude·Codex·Gemini 후보 비교 뒤). 실행 파일·데이터 폴더 이름도 바꾸기

**변경**: product.json — name 아이라, id AIRA(새 키, 실행 파일 이름), fullName Astrophotography Imaging & Rig Assistant, tagline 천체사진 조수, dataFolder AIRA, previousDataFolder AA(새 키). Directory.Build.props가 id·previousDataFolder를 읽고 Astro.Desktop AssemblyName = id. `Product.Id`, `Product.MoveDataFromPreviousName`/`MoveData`(새 폴더가 없을 때만 예전 폴더를 통째로 옮김, 안 되면 복사) — 데스크톱 시작·서버 Build에서. README·CLAUDE.md 이름. 부팅 화면에 영어 풀이(낱말 첫 글자 강조 — AIRA가 되는 것이 보이게, 사용자 요청). `MainWindow` 시작 때 WebView2 디스크 캐시 비우기 — 예전에 받아 둔 index.html(no-cache 전)이 남아 새 빌드 대신 "AA v0.1.58" 화면이 뜸. `BackgroundWindows`: Alt·Windows 키가 눌리면 지키기(항상 위·포커스 되돌리기)를 그만두고 2분 동안 새로 시작하지 않음 — 장비 연결 중 종료 확인 창에서 Alt+Tab으로 고른 창이 다시 끌려 내려가던 것

**확인**: ProductDataTests 3개(옮김·새 폴더 있으면 그대로·예전 없음) 포함 서버 테스트 149 통과·1 건너뜀, 웹·데스크톱 빌드 — AIRA.exe, 창 제목 "아이라 · 천체사진 조수", 페이지 제목 아이라, 캐시 비운 뒤 상태 줄 "아이라 v0.1.59"(창 캡처). 확인 못 함: 사용자 실제 데이터 폴더 옮기기 — Claude 도구로 켠 앱은 Claude 앱의 가상 AppData(Packages\Claude_…\LocalCache)를 봐서 실제 AA 폴더는 건드리지 않았음. 사용자가 직접 AIRA.exe를 켤 때 옮겨짐

---

## 2026-10-09 · 9ace5dc [Claude] 시험 사진에 히스토그램

**요청**: 시험 사진 화면에서 사진 위 오른쪽 세로 가운데에 그 사진의 히스토그램 (N.I.N.A.에 있는 것처럼)

**변경**: N.I.N.A. API는 히스토그램을 주지 않아(통계만 — api_spec 확인) `Fits.Histogram`이 저장된 FITS를 읽어 256칸(약 200만 점 표본, 홀수 간격). `ShotStats.Histogram`, `RealTestShotDevices`가 저장 파일에서 계산, 모의는 `SimHistogram`. `TestShotTask`가 `run.Live("test-photo", url, { histogram, background })`. 웹 `LiveView`의 `Histogram`(배경 위치 강조선, "배경 N% · 포화 N%"). DESIGN.md 시험 사진 항목. 덤: `NightSessionTests.이어서_찍으면…`이 고정 날짜 계획 끝 + 실제 시계라 10-09 02:40 뒤 실패 → 끝 시각을 지금 기준으로

**확인**: 어제 180초 시험 사진 FITS(7704×5184)로 113ms, 마지막 칸 77.6%(완전 포화 — 140초도 같음). 모의 서버(5211)·브라우저로 시험 사진까지 진행해 1920×1200 배치에서 위치 확인. 서버 테스트 146 통과·1 건너뜀, 웹·데스크톱 빌드. 확인 못 함: 실장비에서 새 시험 사진으로(촬영 화면의 사진에는 아직 없음)

---

## 2026-10-09 · 9ee07cb [Claude] 장비 연결 중 종료가 실제로 동작하게 고침

**요청**: 지난 커밋의 실장비 확인을 Claude가 직접 하기

**변경**: `EquipmentConnector`는 요청마다 새로 만들어져(Transient) 종료 요청이 연결 중인 작업을 보지 못했음 → 진행 상태를 싱글턴 `EquipmentRun`으로. `AbortAsync`가 장비마다 차례로 3초씩 지켜보던 것(17초)을 모든 장비를 함께 1초마다 보도록(허브는 다른 장비가 끊긴 뒤) → 4초

**확인**: 시험 서버(5211)로 실장비(N.I.N.A.·WandererBox·Oasis, 적도의·카메라는 전원 꺼짐) — 연결 시작 2~4초 뒤 중단: 허브·포커서·가이더 모두 끊김, 10초 뒤에도 다시 붙지 않음. 포커서 0점 전체 순서: Oasis SDK로 위치를 1234로 바꿔 둔 뒤 `ZeroIfRequestedAsync`(N.I.N.A. 연결 끊기 → 0점 → 다시 연결) → N.I.N.A. 위치 0. 서버 테스트 146 통과·1 건너뜀, 데스크톱 빌드. 남음: 중단 때 N.I.N.A.가 켠 PHD2 프로그램은 열린 채로 둠

---

## 2026-10-09 · 0d72f9e [Claude] 장비 연결 중 종료하면 연결 끊고 닫기, 포커서 0점

**요청**: ① 장비 연결 중에 AA를 끄면 연결을 바로 멈추고 끊긴 것을 확인한 뒤 종료 (실기: AA가 꺼진 뒤 N.I.N.A.가 "스위치 연결됨") ② 포커서를 다시 달면 기어가 맞물리며 노브가 돌아감 → 출발 전 점검의 이슬 방지 열선 타일을 "포커서 0점"으로 바꾸고, AA가 0점을 잡기

**변경**
- `EquipmentConnector.AbortAsync`(연결 취소 → 이번에 시도한 장비를 허브 맨 나중으로 끊기 → 3초 동안 끊긴 채인지, 늦게 붙으면 다시 끊기, 장비마다 최대 30초), `POST /api/equipment/abort`. `StepRail` 종료 확인이 abort 뒤에 닫고, 못 끊은 장비가 있으면 알리고 "그래도 종료". `ConfirmDialog`에 `busy`
- `OasisSdk.SetZero`(드라이버와 함께 설치된 OasisFocuser64.dll — Scan·Open·SetZeroPosition·GetStatus, ASCOM·N.I.N.A.에는 0점 명령이 없음), `RealFocusDevices.ZeroIfRequestedAsync`(N.I.N.A. 포커서 끊기 → 0점 → 다시 연결 → 위치 0 확인), `FocuserZeroRequest`, `POST /api/prepare/focuser-zero`(점검을 마치면). `FocusTask.ZeroAsync` — 잡으면 지난 초점 기억을 버림, 실패면 다시 시도·직접 했음·0점 없이, Oasis가 아니면 직접 하라고 묻기
- `PreflightScreen` 타일 교체, DESIGN.md 점검 항목

**확인**: 서버 테스트 146 통과·1 건너뜀(0점 성공·실패 후 다시 시도·지원 안 됨 3개 추가), lint·웹·데스크톱 빌드. Oasis SDK를 64비트에서 불러 버전(2.0.2)·장치 찾기(1개) 확인 — 실제 0점 잡기는 하지 않음. 확인 못 함: 실장비에서 연결 중 종료, N.I.N.A.가 포커서를 끊은 뒤 SDK로 열리는지·0점 뒤 N.I.N.A. 위치가 0인지

---

## 2026-10-09 · 06332fb [Claude] 극 찾기 실패 안내를 조준부터

**요청**: SharpCap이 위치를 못 찾은 원인이 조준(북극에서 너무 멀리 향함)이었음 — 실패하면 망원경을 북극 쪽으로 더 가깝게 향하게 하라고 보여 주기

**변경**: `RealPolarDevices.SolveStageAsync` 실패 문구(배경 적당·최대 노출·직접 정한 노출·여러 번 바꿈)와 `PolarTask` 기본 실패 문구 맨 앞에 "삼각대 방향과 고도 나사로 북극 쪽으로 더 가깝게" 안내. 너무 밝을 때는 그대로(조준 탓이 아님)

**확인**: 서버 테스트 143 통과·1 건너뜀, 데스크톱 빌드. 실기: 사용자가 조준을 고치자 SharpCap 직접 실행(게인 800·노출 1.2초)에서 별 17개 검출·Solved. AA로 다시 해 보지는 못함

**남은 것**: 극축 정렬에 성공해도 캘리브레이션 전에 항상 물을지(남쪽이 막힌 곳) — 사용자 결정 대기. SharpCap 위치를 집 좌표로·굴절 보정 켜기(사용자)

---

## 2026-10-09 · 45b3148 [Claude] 첫 실장비 실내 한 바퀴에서 찾은 문제 고치기

**요청**: 2026-10-08 집에서 실장비로 처음 끝까지 돌려 본 뒤 나온 수정 목록을 고치기

**변경**
- PHD2·SharpCap: `Phd2Check` — 가이드 카메라가 PHD2 시뮬레이터면(N.I.N.A. 적도의는 실제) 연결 단계에서 막음. `RealPolarDevices.Phd2Words` — PHD2 장비 연결 창이 열려 있어 카메라를 못 놓을 때 할 일을 AA 말로. SharpCap 시작 전 카메라가 Simulator면 바로 안내
- SharpCap 극 찾기: 스크립트 처음 게인을 80% → 넓은 범위면 로그 눈금 25%(G3M662M 136500 → 약 640), 배경 밝기(히스토그램)·게인 보고. `SolveStageAsync` — 밝으면 노출 줄이고 어두우면 늘리고, 적당한데 못 찾으면 멈추고 안내. 사용자가 SharpCap에서 노출을 바꾸면(`UserChangedExposure`) AA는 바꾸지 않음
- SharpCap 중계: 창 캡처 대신 스크립트가 `SaveAsViewed`로 저장한 영상(`SharpCapBridge.LatestView`)을 보냄. 같은 이름이면 SharpCap이 덮어쓰기 확인 창을 띄우고 멈춰서 매번 새 이름 + 두 번 전 파일 삭제
- 캘리브레이션: 극축 정렬을 건너뛰었으면 움직이기 전에 "캘리브레이션 시작 / 가이딩 없이 진행" 묻기(`NoGuideAsync`), 이어서 하기 확인에서 추적 상태를 요구하지 않음
- 초점: 자동초점 측정점이 오기 전에도 "자동초점 중" 상태, 포커서 멈춤 판정 2분
- 리그 끝: 계획 화면으로 갈 때 `POST /api/prepare/rig/rest` — 적도의 홈 + 추적 끔 (캘리브레이션 위치에 머물지 않게)
- 시험 사진은 `imageType: SNAPSHOT` (LIGHT 폴더에 섞이지 않게)
- AI 대화: 도구 실행 전 "지금 하는 일" 상태 이벤트(`PlanAssistant.Doing`) → 계획 화면 입력 점 옆에 표시. 호출마다 보낸 글자 수·첫 응답·전체 시간 로그
- 데스크톱: 상태 줄에 창 내리기(최소화) 버튼, 전체화면을 최대화 대신 모니터 크기에 정확히 맞춤(`FitMonitor` — 오른쪽·아래가 화면 밖으로 밀리던 것)
- 촬영 화면 가운데 카운터: 쓸 사진 / 계획 옆에 "· 제외 N"을 작게 (있으면 경고색, 0이어도 자리 유지)
- 단계 레일: 마무리 작업이 펼쳐지면 마무리 점 아래 선을 잇고 마지막 작업 아래 선을 숨김
- AI 계획의 한 장당 쉬는 시간 `PlanTools.FrameOverheadSeconds` 6초 → 40초 (실측 약 38초 — 예상 장수가 너무 많이 나옴)
- docs/SHOOT_IMPLEMENTATION.md: 극 찾기 노출 규칙, 한 장마다 약 38초 추가(내려받기·저장)

**확인**: 서버 테스트 143 통과·1 건너뜀, lint(경고만, 바꾼 파일 아님)·웹·데스크톱 빌드. SharpCap 테스트 카메라로 새 스크립트 실행 — 보고 30번·노출 명령 반영·quit, 영상 파일 돌려 쓰기(2개만 남음), 영상은 화면 영상만(별 표시 포함). AA 창 0,0–1536×960 = 화면 크기. 확인 못 함: 실제 하늘에서 배경 밝기 기준(40%·2%)이 맞는지, 실장비 SharpCap에서 사용자 노출 변경 감지, 레일 선은 화면으로 직접 보지 못함, 캘리브레이션 질문·홈 복귀는 실장비에서

**남은 것**: 사용자가 N.I.N.A. 사진 저장 폴더를 OneDrive 밖(`D:\Photo\N.I.N.A`)으로 옮김 — 다음 촬영에서 한 장당 추가 시간을 다시 재서 40초를 맞출 것

---

## 2026-10-08 · c473900 [Claude] 이어서 하기 보강: 장비 멈춤 확인·끝낸 작업 다시 확인 후 건너뛰기·플랫/다크 이어 찍기

**요청**: 이어서 하기 빈틈 — ① AA만 꺼졌으면 노출·가이딩·이동이 계속 돌 수 있음 → 이어서 전에 멈춤 확인 ② 마무리 중 꺼지면 플랫·다크를 처음부터 다시 찍음 ③ 레일의 단계·작업 중 끝낸 것은 다시 켠 뒤 건너뛰기, 단 장비가 살아 있는지 확인이 필요한 것은 생략하지 않기

**변경**
- `PrepareStarter.StopForResumeAsync`(노출 중지·자동초점/포커서 멈춤·적도의 이동 멈춤 확인, 못 하면 이유), `ResumeAsync`(장비 준비를 먼저 이어서 — 다 확인되지 않으면 장비 준비로, 대상·촬영이면 대상 묶음, 마무리면 마무리)
- `IPrepTask.CheckResumeAsync`(기본 = 끝 상태 약속), `PrepareRunner.StartResumedAsync`(하던 작업·가이딩을 멈춘 뒤, 끝낸 작업은 결과가 있고 다시 확인이 통과하면 "이어서 · 전에 마침", 아니면 다시), `DoneIds`, `PrepResults.Has`, `PrepareFlow.ResumeRig/Target/WrapAsync`·`DoneIds`
- `WrapProgress`(플랫 장수·노출, 플랫 다크, 다크 장수·묶음) — 한 장마다 기록, 이어서 하면 플랫은 노출 찾기 없이 찍은 장수부터, 다크는 고른 장수·찍은 만큼부터. 마무리를 새로 시작하면 지움
- 기록(`SessionSnapshot`)에 끝낸 작업 목록과 이동·센터링·초점 확인·가이딩·시험 사진·플랫·다크·마무리 진행 결과. `/api/session/resume`이 멈춤 확인 → 되살림 → 다시 확인 순서
- 화면: 이어서 하는 동안 "확인하는 중", 장비를 멈추지 못하면 이유와 "다시 시도"

**확인**: 서버 테스트 143 통과·1 건너뜀 — 장비 준비 이어서(같은 장비면 모두 건너뜀, PHD2가 다시 켜져 보정값이 없으면 캘리브레이션만 다시), 플랫 25장→5장 더, 끝낸 플랫 건너뛰고 다크 15장→5장 더. 모의 서버로 기록→강제 종료→pending·resume 오류 없음. lint 0, 웹·데스크톱 빌드. 확인 못 함: 실장비에서 촬영 중 크래시 후 이어서(가이딩은 항상 다시 시작됨 — 멈춘 뒤 끝 상태가 맞지 않으므로)

---

## 2026-10-08 · 569c9f2 [Claude] AA가 중간에 꺼졌다 다시 켜면 이어서 하기

**요청**: 직접 종료·크래시·컴퓨터 꺼짐으로 AA가 중간에 꺼졌을 때 — 장비 연결 뒤 "이어서 할까요?", 이어서 하면 출발 전 점검을 건너뜀. 컴퓨터가 꺼졌던 경우는 이어서 하지 않고 중단된 촬영이 있었다고만 알림. 며칠 뒤 새 촬영에는 묻지 않기 (기준은 시간 대신 더 나은 것이 있으면 그것으로)

**변경**
- `Session/NightSession.cs`: `NightSessionStore`(session.json — 단계·프로필·확정 계획·계획 칸·촬영 기록·극축·캘리브레이션·초점·가이딩 없음·그날 밤 기억, 컴퓨터 켜진 시각), `NightSessionRecorder`(진행 단계 동안 20초마다, 단계가 바뀔 때 바로). 요약에 닿으면 끝난 밤
- 판정 `Pending`: 이어서 = 같은 밤 + 같은 프로필 + 2시간 안 + 그 사이 컴퓨터가 안 꺼짐(켜진 시각 비교) + 계획 끝 시각 전. 그 밖에 18시간 안의 끝나지 않은 기록은 알리기만. 갈 곳: 대상·촬영 중이었으면 대상(이동부터), 계획·마무리·장비 준비는 그 단계
- `PlanAssistant.RestoreAsync`, `TargetShots.PlanKey` + `ShootSession.Start`가 같은 계획의 줄에 이어서 셈. API `/api/session/phase·pending·resume·dismiss`
- 화면: `session.ts`, `App`이 단계를 알리고 장비 연결 뒤 기록을 물음(`goAfterEquipment`), `ConfirmDialog`에 `single`·`dismissable`(이어서 질문은 Esc로 넘어가지 않음)

**확인**: 서버 테스트 140 통과·1 건너뜀(`NightSessionTests` 6 — 이어서·컴퓨터 꺼짐·2시간/며칠·끝낸 밤·다른 프로필·새로 시작·이어서 세기). 서버를 임시 데이터 폴더로 켜서 단계 알림 → 강제 종료 → 다시 켜 pending(resume)·resume·dismiss 확인. lint 0, 웹·데스크톱 빌드. 확인 못 함: 화면에서 질문 창, 실제 대상·촬영 중 재개(실장비)

---

## 2026-10-08 · ce4c688 [Claude] 실행 시 창 깜빡임·전원 허브 연결 타이밍·× 버튼 확인

**요청**: 첫 실행에서 본 것 — N.I.N.A. 로고·PHD2가 최대화됐다 사라짐(문제처럼 보임) → 켜는 동안 AA를 잠깐 항상 위로, 최소화는 하지 않기. 전원 허브가 연결 안 됨. 창 × 버튼도 진행 중이면 확인. 빌드 경고 CS9124

**변경**
- `BackgroundWindows.Watch`: 최소화 대신 지켜보는 동안 AA 창 "항상 위"(`HoldTop`, 겹치면 마지막에 풂), 새 창이 포커스를 가져가면 AA로. SharpCap은 그대로 뒤로만
- `EquipmentConnector.WaitListedAsync`: 연결 전에 N.I.N.A. 장비 목록에 그 장비가 나타날 때까지(최대 30초) — 실기: N.I.N.A. 켜고 9초 뒤 연결이 "Sequence contains no matching element"로 실패, 목록은 11초쯤 완성
- `MainWindow.OnClosing`: 화면이 확인하기 전에는 "confirm-close"를 보내고 기다림, `host.ts` `onCloseRequest`, `StepRail`이 진행 중이면 종료 확인 창(요약 화면은 바로 닫음)
- `PrepContext.HasGuider`가 생성자 매개변수 대신 `Results` 속성 사용(CS9124)

**확인**: 서버·데스크톱 빌드 경고 0·오류 0, lint 0. 확인 못 함: 창이 실제로 AA 뒤에서 열리는지, × 확인 창, 전원 허브 재실행 연결(다음 실행 때)

---

## 2026-10-08 · 87284dc [Claude] 작업 실패 시 건너뛰기·제외 사진 연속 멈춤·핫픽셀 별 다시 고르기 (제미나이 의견 반영)

**요청**: 제미나이에게 물은 결정 3가지 — Claude가 항목별로 조정한 안(지난밤 보정값 안 씀, 노출 자동 변경 안 함, 재개 기준은 확인 사진, 30분 뒤 홈·파킹 안 함, find_star 대신 기존 별 다시 고르기)대로 사용자 승인

**변경**
- 결과 기록 `Skipped`(극축·캘리브레이션·센터링·가이딩·시험 사진), `SlewResult.AcceptedHere`, `GuiderSkipped` + `PrepContext.HasGuider`가 그것을 봄(캘리브레이션을 다시 하면 지움)
- `PolarTask.SkipAsync`(넘겨받기·극 찾기 실패 시, 카메라 돌려준 뒤), `CalibrationTask` "가이딩 없이 진행", `SlewTask` "지금 위치를 목표로" → `CenterTask`도 건너뜀(확정 질문은 남김), `CenterTask` "센터링 건너뛰기", `FocusTask` "지금 초점으로 진행"(포커서 오류 포함), `GuidingTask.SkipAsync`(노출 60초 넘으면 줄일지), `TestShotTask` "시험 사진 건너뛰기". 건너뛴 작업은 끝 상태 검사에서 멈춤만 봄
- `ShootSession`: 제외 사진 5장 연속 → 멈춤("frames"), 3분마다 한 장 확인·쓸 사진이면 재개, 30분이면 끝("frames"). `GuideLoss.HotPixel`(최근 "HFD가 낮음" 3번 이상·잃은 것의 절반 이상) → 바로 `ReselectStarAsync`, 10분에 두 번째면 가이드 노출 +1
- 화면: 촬영 멈춤 안내 frames·hotpixel, 끝 이유 frames, 요약 멈춤 이름·다크 라이브러리 팁, 모의 실패 shoot.hotpixel
- 모의 극축 정렬 장비의 멈춤이 실제처럼 SharpCap 닫기·카메라 돌려주기

**확인**: 서버 테스트 134 통과·1 건너뜀(두 번) — 건너뛰기 4개, 핫픽셀 1개, 제외 사진 멈춤·재개 1개, 결과 기록 모양 갱신. lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 화면에서 눌러 보기, 실장비

**남은 것**: 다크플랫 못 옮김 요약 표시 (DESIGN.md 촬영 준비 절에 건너뛰기 규칙 추가함)

---

## 2026-10-08 · 490cd41 [Claude] Codex 리뷰 18절 반영 (CX-NIGHT-01~06)

**요청**: Codex 18절 6건 모두 동의 — 판단을 말한 뒤 사용자 승인으로 적용 (02는 움직이는 동안 최대 4분 기다리기를 더하고, 05의 다크플랫 못 옮김 요약 표시는 다음으로)

**변경**
- 01·02: `NinaRig.FlipAsync` → `FlipResult(Flipped, Still)` — pierEast로 바뀌어야 반전(pierUnknown은 다시 조회), 이미 pierEast여도 멈춤 확인, 60초에 방향이 그대로여도 움직이는 중이면 최대 4분, 실패하면 `StopAfterFailAsync`(멈춤 명령·확인). `FlipOutcome.MountStill`, `ShootSession` `_mountStill`·`ShootView.MountStopped`, `BlocksNext`·`RecheckStopAsync`가 적도의 정지도, `IShootDevices.ConfirmMountStillAsync`. 화면 문구(적도의 정지 확인)
- 03: `ShootSession.EndReason`(중단·완료·고도·새벽) — 다음 사진이 필요할 때만 디더링, `WaitUnstableAsync`가 끝 조건·반전 시각이면 그만둠, 안정 관찰을 `_wake`(중단·답이 취소)로
- 04: `AscomWeather.ReadAsync`가 기다리지 않음 — 뒤에서 하나만 읽고 최근 값(5분 안)·null을 바로
- 05: `NinaRig.FindSavedFile` — 이미지 폴더 전체에서 이름 + 저장 시각 ±2분, 후보가 하나일 때만. 못 찾으면 빈 경로(이름만 돌려주지 않음) → 촬영은 `File = null`(옮기지 않고 "직접 옮겨 주세요")
- 06: `RealFocusDevices.CancelAndConfirmAsync`(카메라 쉼·포커서 멈춤 두 번), `AutofocusRun.StopUnconfirmed`·`RefocusOutcome.StopUnconfirmed` → 촬영은 "focus"로 끝냄, 준비 단계 초점은 "장비 상태 다시 확인"
- 시험: NIGHT02·NIGHT03 2개, `FindSavedFileTests` 3개, 모의 실패 shoot.flipmoving

**확인**: 서버 테스트 129 통과·1 건너뜀(두 번). N.I.N.A. 시뮬레이터(AA_SIM=1): 반전 15초 뒤 끝·pierEast, 저장 경로, DARKFLAT 폴더 통과. 처음엔 저장 경로가 실패 — 다크플랫 시험과 같은 초에 찍혀 같은 이름 파일이 두 폴더에 생겨 후보 둘 → null(의도대로), 시험에 1초 간격. N.I.N.A.가 켜지며 자체 오류 창(SimpleSequenceVM.get_Targets NullReference, 3.2 개발판) — AA 명령 전, API는 계속 동작. 확인 못 함: 실제 OnStepX 반전 시간·방향 갱신 시점, 자동초점 취소 실패 경로(코드·테스트로만), 센서 멈춤

**남은 것**: 다크플랫을 못 옮겼을 때 요약에 표시, 실장비 확인

---

## 2026-10-08 · [Codex] 931c64a 실장비 첫 촬영 전 재리뷰

**요청**: `ed4d353..931c64a`를 실제 장비에서 사고·촬영 중단으로 이어지는 경로 중심으로 검토. 코드 수정 없이 리뷰 18절과 이 기록만 작성.

**변경**: `docs/codex/REVIEW_CODEX.md` 18절 추가. CX-NIGHT-01~06(P1 5건·P2 1건): 반전 성공 판정, 반전 실패 후 정지 확인, 디더링 대기의 종료 조건, 환경 COM 조회 정체, 저장 파일 동일성, AF 취소 확인. 16절 수정의 재검토와 나머지 요청 범위·실기 한계도 기록.

**확인**: HEAD `931c64a`. `AA_REAL=0`, `AA_SIM=0` 서버 테스트 123 통과·1 건너뜀. 저장소 밖 가짜 장비/HTTP·임시 파일로 센서 취소 불응, 반전 판정 2개, 파일 검색 2개, 마지막 장 이후 대기 재현. 실제 장비 접근·앱 실행·실제 사진 이동 및 코드 변경 없음.

**남은 것**: 리뷰 지적 수정과 해당 실패 경계 회귀 검증, 실제 장비 프로필·반전·AF·별 있는 하늘에서의 검수. 커밋·푸시하지 않음.

---

## 2026-10-08 · 931c64a [Claude] AA 전체를 시뮬레이터에 붙여 찾은 문제 고치기

**요청**: 사무실에서 AA 전체를 실장비 모드로 시뮬레이터에 붙여 돌려 보고, 중대한 결정이 아닌 고칠 것·답이 분명한 것은 바로 고치기 (사용자 회의 중)

**변경**
- `Engine/Phd2Check`: "On Camera"(PHD2가 실제로 주는 이름)도 ST-4로 인정(`IsOnCamera` 띄어쓰기·하이픈 무시), N.I.N.A.·PHD2 적도의가 둘 다 시뮬레이터면 통과
- `GET /api/equipment/simulated` + 장비 화면 `TempFailButtons`는 모의일 때만
- `SharpCapBridge.LaunchAsync`: 실패하면 AA가 켠 SharpCap을 닫음
- `RealFocusDevices.AutofocusAsync`: 자동초점 진행 이벤트가 3분(`Stall`) 없으면 취소·실패 (`NinaRig.LastEventAtAsync`)
- `ShootSession`: 제외 사진 5장 이어질 때마다 알림(`ExcludedRunAlert`), 모의 실패 shoot.nostars
- 점검 화면 타일 `aria-label`
- 시험: `SimulatorRigTests`에 촬영 세션·마무리, `Phd2CheckTests` 3개, `ShootAndWrapTests` 1개

**확인**: 서버 테스트 122 통과·1 건너뜀(두 번), 시뮬레이터 시험(AA_SIM=1) 촬영 세션·마무리 실행, lint 오류 0, 웹·데스크톱 빌드. 화면은 장비 연결·점검·극축 정렬(실패·중단)까지 브라우저로. 확인 못 함: 극축 정렬 뒤 화면 흐름(사무실에 가이드 카메라 없음), 자동초점·솔빙 성공 경로(시뮬레이터 카메라 별 사진 설정 안 함)

**남은 것**: 결정 필요 — 작업 실패 시 건너뛰기, 제외 사진 연속 시 멈출지, "HFD가 낮음" 별 잃음 판정. 사용자 설정 `rig-overrides.json` 카메라가 시뮬레이터(X-T5로 바꿔야 함). 시험 중 바꾼 것: PHD2 AA-Simulator 프로필 적도의 On-camera로 되돌림, N.I.N.A. AstroAssistant 프로필 스위치는 없음으로 둠(실험용)

---

## 2026-10-08 · be79263 [Claude] 촬영 화면에 이슬 여유·열선 상태

**요청**: 열선은 Empire 자동에 맡기고 AA는 이슬 여유·열선 세기·자동 여부를 보여 주고 이상하면 알리기 (사용자 결정)

**변경**: `NinaRig.DewHeaterAsync`(N.I.N.A. 스위치의 PWM DC3 — 세기 0~1, 설명 "Automatic control"이면 자동), `IShootDevices.DewAsync`·`DewStatus`, `ShootSession.CheckDewAsync`(한 장마다, 여유 2°C 아래인데 열선 꺼짐 · 3°C 아래인데 자동 아님이면 10분에 한 번 알림), `ShootView.Dew`, 화면 `ShootPanels` 계기판 설명 줄 끝에 "이슬 여유 18°C · 열선 0% (자동)"

**확인**: 서버 테스트 117 통과·1 건너뜀(모의 이슬 표시 1개), 실기 `실장비_열선_상태` 통과. 실기에서 Empire 기준이 19°C로 남아 있어 0↔255 반복 → 5°C로 바꾸자 0 유지 — Empire 자동은 렌즈 프로브 − 이슬점으로 판단, 켜고 끄는 방식. lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 화면을 눈으로(실촬영·모의 모드에서)

**남은 것**: 장비 준비 화면에도 보일지(지금은 촬영만)

---

## 2026-10-08 · 4274927 [Claude] WandererBox 기온·습도·이슬점 읽기 (실기)

**요청**: WandererBox Plus V3 + 온도 프로브 + DHT22를 연결 — 이슬점·열선 확인. 열선은 Empire 자동에 맡기기로(사용자 결정, Empire 이슬점 온도 차이를 15→5°C로 사용자가 바꿈)

**변경**: `Prepare/Real/AscomWeather` 신규 — ASCOM ObservingConditions를 직접 읽어 기온·습도·이슬점(1분 캐시, 실패 시 5분 쉼), `RealShootDevices.GuideRawAsync`의 `DewMarginC` = 기온 − 이슬점(전에는 null). 문서: SHOOT_IMPLEMENTATION·api-coverage

**확인**: COM4 상태 줄 듣기(명령 없음), N.I.N.A. 스위치(유성철 프로필에 설정된 것) 연결해 DC3 읽기 — "Automatic control in progress", 설정 변경 뒤 255→0. 실기 시험 `실장비_기온_습도_이슬점`(AA_REAL=1) 통과(25.3°C·36.4%·이슬점 9.3°C), 서버 테스트 115 통과·1 건너뜀. 확인 못 함: 실제 열선 발열(열선 미장착), 이슬 판단이 촬영 화면에 뜨는지(실촬영 필요)

**남은 것**: 이슬 여유·DC3 세기·자동 여부를 화면에 보여 주고 이상하면 알리기, `DewHeaterBoostAsync`는 자동 모드면 알림만(지금도 false). Empire 자동이 상자에서 도는지(Empire 꺼도 유지되는지) 확인

---

## 2026-10-06 · 9c7dd2a [Claude] 제미나이 최적화 리뷰 반영

**요청**: 제미나이(GitHub 읽기)가 낸 최적화·안정성 제안을 검토해 동의하는 것만 고치고, 반대는 이유를 정리

**변경** (동의)
- `Phd2Client.CallAsync`: 보내기를 `_writeGate`(SemaphoreSlim)로 한 번에 하나 — 촬영 감시·디더링·조회가 겹쳐도 명령 줄이 섞이지 않게
- `MainWindow.OnClosing`: 닫기를 미루고 내부 서버 정지(최대 10초)를 기다린 뒤 닫음, 멈추는 중 다시 눌러도 먼저 닫히지 않음. 제미나이가 말한 "파킹·가이딩 정지"는 서버 정지에 없음 — 백그라운드 작업 정리가 잘리지 않게 하는 것
- `EquipmentScreen` 장비 그래프: `connectLayout`·`editLayout`을 `useMemo`로 (seed 난수라 결과 같음)
- 촬영·센터링·시험 사진 미리보기: `Convert.FromBase64String(GetString())` → `GetBytesFromBase64()`
- `UpdateChecker.FindUtf16Version`: phd2.exe를 1MB씩 읽어 UTF-16 글자를 찾음(전체 파일을 문자열로 만들지 않음, 예전 방식이 놓치던 홀수 자리도 찾음)
- `WindowCapture.Capture`: 찾은 뒤 남은 Process까지 Dispose

**반대·보류**: LST 미리 계산(계산 비용이 삼각함수 몇 번이라 이득 없음, 구조만 복잡), LiveView RealImage(16절 최적화 3에서 이미 처리), Fits·Png ArrayPool(제안도 조건부 — 측정 뒤)

**확인**: 서버 테스트 114 통과·1 건너뜀(새 `Setup/Phd2VersionTests` — 처음·홀수 자리·조각 경계·세 번째 조각·설치된 PHD2), lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 장비 화면·창 닫기를 눈으로(사용자 전체 확인 때)

---

## 2026-10-06 · d0d7327 [Claude] 시뮬레이터로 미리 확인한 실장비 코드 고치기 (저장 경로·반전·별 잃은 프레임·다크플랫·디더링 안정화)

**요청**: 실장비 없이 N.I.N.A.·PHD2 시뮬레이터로 실장비 연결 전에 확인할 수 있는 것을 확인하고, 찾은 문제 1~5와 6(디더링 안정화 실패: 30초 더 → 그대로 찍고 등급으로 → 연속 3번이면 멈추고 안정되면 자동 재개, 15분 넘으면 묻기 — 제미나이 의견 반영, 사용자 결정)을 고치기

**변경**
- `NinaRig`: `LastSavedAsync`가 전체 경로(`FindSavedFileAsync`·`ImageFolderAsync` — image-history는 이름만 줌), `MountState.Pier`, `FlipAsync`는 SideOfPier가 바뀐 뒤 `WaitStillAsync`(이미 pierEast면 성공)
- `RealWrapDevices.CaptureAsync`: DARKFLAT은 DARK로 찍고 `DARKFLAT` 폴더로 (API는 DARKFLAT을 SNAPSHOT 폴더에 저장)
- `RealShootDevices`: StarLost는 SNR·HFD 평균에서 빼고 `GuideRaw.LostRecent`·`LowHfdRecent`(ErrorCode 4)로 따로, `DitherAsync` 시간 초과면 `WaitSettledAsync`(10초 안정, 최대 30초)
- `ShootSession`: 디더링 실패 연속 3번 → `WaitUnstableAsync`(멈춤 원인 unstable, 1분 안정되면 재개, 15분 → `ShootView.Ask`), `Answer`(shoot·wait), "HFD가 낮음"이 잦으면 알림. `POST /api/shoot/answer`
- 화면: `ShootScreen` 멈춤 안내 unstable·"그대로 찍기 / 더 기다리기", `shoot.ts` `answerShoot`, 모의 실패 shoot.unstable·shoot.unstablelong
- 캘리브레이션 경고(5)는 이미 `RealCalibrationDevices`가 Alert를 모아 보여 줌 — 변경 없음

**확인**: 서버 테스트 104 통과·1 건너뜀(디더링 불안정 자동 재개·질문 2개 추가, 두 번 연속). 실장비 코드를 시뮬레이터에 붙인 `Real/SimulatorRigTests`(AA_SIM=1) 4개 통과 — 저장 경로, DARKFLAT 폴더, 반전(14초 뒤 끝), PHD2 디더링(13초 안정). lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 별 잃은 프레임 따로 세기는 이번 시뮬레이터 별이 밝아 잃은 프레임이 없었음(코드·단위 확인만), 불안정 질문 화면은 눈으로 안 봄, 자동초점·솔빙(시뮬레이터 카메라 별 사진 설정이 N.I.N.A. 화면에만 있음)

**남은 것**: 실장비 확인(남반구·OnStep의 SideOfPier 규약), 안정화 기준값(1.5픽셀·10초)은 실제 하늘로, AA 전체를 실장비 모드로 시뮬레이터에 붙여 끝까지

---

## 2026-10-07 · d3fb05c [Claude] Codex 리뷰 16절 반영 (CX-APP-R1~R5, 최적화 1~5)

**요청**: Codex 16절(재리뷰·앱 전체 검토) 검토 — R1~R5 모두 동의, 최적화 1~5 반영, 6(AI 입력량)·7(구조 분리)은 측정·실장비 확인 뒤. R4의 예기치 않은 N.I.N.A. 종료는 "촬영 중이면 대상 단계를 이동부터 다시, 마무리 중이면 마무리로" (사용자 동의)

**변경**
- R1: `PackTask` — 홈 실패 뒤 정지(`StopAsync`) 결과를 보고, 정지를 확인하기 전에는 다시 시도가 홈이 아니라 정지 확인부터. 안내 문구도 정지 미확인을 구분
- R2: `ShootSession.MoveExcluded` — 제외 폴더 이동 실패(null·예외)를 기록(`TargetShots.NotMoved`), 알림은 "옮기지 못했어요 (직접 옮겨 주세요)", 등급 F는 유지. 요약에 못 옮긴 장수
- R3: 가이더 없으면 `IShootDevices.MountTrackingAsync`만(PHD2 조회 없음), 실제 `FlipAsync`도 `ctx.HasGuider`일 때만 PHD2 정지·재개
- R4: `NinaWatcher.ExpectExit`·`NinaState.Closed` — 마무리에서 AA가 닫으면 경고 없음. 촬영 중 꺼지면 화면이 `POST /api/shoot/abort-for-restart`(`ShootSession.AbortForRestartAsync` + `PrepareFlow.RecheckTargetAsync`) → 다시 켠 뒤 대상 단계(이동부터), 마무리 중이면 마무리로 돌아감. 실제 가이딩 정지는 N.I.N.A.가 안 되면 PHD2 `stop_capture` 직접
- R5: 실장비 모드에서 "9칸 확대 보기" 숨김 (모의 그림만 있음)
- 최적화: PHD2 메시지 `JsonDocument` 닫기, `NinaWatcher` 대기자 정리, `LiveView.RealImage` 한 번에 하나·늦은 응답 버림, `RealShootDevices` 이벤트 수신을 촬영 끝에 정리(`EndSession`), 안 쓰는 `Term` 컴포넌트 삭제, SiteScreen 함수 이름(`use` → `applyChosen`)으로 lint 오류 해소
- 버전 `product.json` 0.0 → 0.1 (촬영·마무리까지 한 밤 흐름이 갖춰져서. 셋째 자리는 계속 커밋 수)
- 모의 실패 `shoot.movefail`, 시험용 `SimulatedWrapDevices.HomeCalls`·`SimulatedShootDevices.GuideRawCalls`

**확인**: 서버 테스트 102 통과·1 건너뜀(새로 R1·R2·R4, CX07에 무가이드 반전·PHD2 조회 0회, 세 번 연속 통과). lint 오류 0(경고만), 웹·데스크톱 빌드. R4 화면 전환·실제 N.I.N.A. 종료, 실제 PHD2 직접 정지는 실기 미확인

**남은 것**: 최적화 6(AI 입력량 측정)·7(App·촬영 세션 구조) 보류, 실제 사진 9칸 자르기(저장 형식 확인 뒤), 실장비 확인 목록, WandererBox 이슬점·열선, 틸트 점검·정비(냉각 카메라 대비, Hocus Focus 확인)

---

## 2026-10-06 · [Codex] ed4d353 재리뷰·앱 전체 사이클 최적화 검토

**요청**: CX-SHOOT-01~09 수정 재검토와 기본 사용 사이클을 완성한 앱 전체의 최적화·불필요한 요소 확인. 구현 수정 없이 리뷰.

**변경**: `docs/codex/REVIEW_CODEX.md` 15절 재검토 결과 갱신, 16절 추가. 기존 수정 대부분 확인. CX-APP-R1~R5(P1 1건·P2 4건): 홈 정지 실패 후 재이동, 제외 파일 이동 실패 누락, 무가이드 반전의 가이더 의존, NINA 종료/복구 흐름, 실제 9칸 확대의 모의 그림 표시. JSON·SSE 자원, 이미지 요청, 리스너 수명, 미사용 Term, lint, AI 대화 입력량, 상태 구조의 개선 순서 기록.

**확인**: HEAD `ed4d353`, 시작 시 작업 트리 깨끗함. `AA_REAL=0` 테스트 99 통과·1 건너뜀, 웹 빌드 통과. lint는 SiteScreen 로컬 `use` 함수의 Hook 이름 오인 오류로 실패(별도 React 경고 있음). 저장소 밖 임시 프로그램에서 가짜 장비 6개·가짜 HTTP 2개 사례 확인. 앱 실행·실장비 조회/명령·실제 파일 이동·AI 요청 없음. 실제 장시간 성능 측정은 하지 않음.

**남은 것**: R1~R5 처리와 단계별 회귀 검증. 최적화는 작은 자원/미사용 코드 정리부터, 성능 변경은 측정 후 진행. 구현 코드 수정·커밋·푸시 없음. 아래 기존 작성일은 유지.

---

## 2026-10-07 · ed4d353 [Claude] Codex 리뷰 14절 반영 (CX-SHOOT-01~09 + 동시성·표본 중복)

**요청**: Codex 리뷰(REVIEW_CODEX.md 14절) 검토 — 9건 모두 동의, 추가 의견(이전 실행 기다리기·가이딩 조회 하나씩·기준 표본 중복)도 반영

**변경**
- CX-SHOOT-01: `IShootDevices.StopGuidingAsync`는 PHD2 Stopped·Looping일 때만 true(LostLock·조회 실패 아님). `ShootSession` 끝 상태 `GuideStopped`, `RecheckStopAsync`(`POST /api/shoot/recheck-stop`), `BlocksNext` — `PrepareStarter.StartWrapAsync`·`StartTarget`이 막음. 화면: 확인 전 "장비 상태 다시 확인"만, 고른 곳(마무리·다른 대상)으로는 확인 뒤에 이동
- CX-SHOOT-02: `FlipOutcome(Flipped, Centered, Guiding)`, 반전 실패면 `EndAsync("flip")`, 반전은 `MountLock` 안에서. 실제 어댑터는 가이딩 정지 확인 뒤에만 반전
- CX-SHOOT-03: `PackTask` — 홈·추적 끄기 확인 전에는 연결 끊기·프로그램 닫기 없음, 홈 실패면 `StopAsync` 후 "다시 시도 / 직접 확인했어요". `PackResult`에 TrackingOff·UserConfirmed·SafeToPowerOff, 요약의 전원 안내는 SafeToPowerOff일 때만
- CX-SHOOT-04: `RealShootDevices.ExposeAsync` — 촬영 시작 뒤 저장 기록만(10초까지 다시 조회), 없으면 실패
- CX-SHOOT-05: `RecenterSafelyAsync`(가이딩 멈춤 확인 → 잠금 → 센터링 → 가이딩 재개, 실패면 촬영 끝), 바람·구름 경로가 사용
- CX-SHOOT-06: `CheckGuideAsync` — MountStopped·Disconnected는 Guiding보다 먼저. 실제 어댑터는 PHD2를 못 읽어도 적도의 추적은 따로
- CX-SHOOT-07: 가이더 없으면(`HasGuider=false`) 가이드 감시·디더링·정지 확인 건너뜀, 적도의 추적만
- CX-SHOOT-08: `DarkTask` 플랫 다크·다크 연속 3번 실패 → 이유 + 다시 시도 / 지금까지만 쓰기 / 다크 건너뛰기
- CX-SHOOT-09: 노출을 멈추지 못해 저장된 사진은 `ExcludeFrame`(제외 폴더 + F)
- 추가: 새 촬영은 이전 실행이 끝난 뒤 시작, `PollGuideAsync` 한 번에 하나, `GuideRaw.Steps`로 새 걸음만 기준 표본에
- 모의 실패: shoot.stopfail·flipfail·abortfail·reselectfail, wrap.home·tracking·dark (촬영 화면 모의 실패 버튼에도 일부)

**확인**: 서버 테스트 99 통과·1 건너뜀(새로 CX01·02·03·05·06·07·08·09, 세 번 연속 통과). 웹·데스크톱 빌드. CX-SHOOT-04는 실제 어댑터라 단위 테스트 없음(빌드·코드 확인). 실장비 미확인

**남은 것**: 실장비 확인(docs/SHOOT_IMPLEMENTATION.md), WandererBox 이슬점·열선, 노출 설계 A, 다크 "여기까지만"과 촬영 완료가 겹치는 경계 테스트

---

## 2026-10-06 · [Codex] 단계 재구성·촬영·마무리 리뷰 (`a20a1b5..73063b4`)

**요청**: `f52a805`·`73063b4`의 HISTORY와 최신 리뷰 요청 범위를 중심으로 코드 리뷰. 결과를 `docs/codex/REVIEW_CODEX.md`에 새 절로 기록.

**변경**: 리뷰 14절에 CX-SHOOT-01~09 기록(P1 6건, P2 3건). 가이딩 정지·반전·장비 정리 실패의 진행 차단 누락, 저장 파일 동일성 미확인, 가이딩 중 재센터링, 추적 중단 판정 소실, 무가이드 구성, 다크 무한 재시도, 중단 실패 후 저장 사진 누락. 동시성·Ask 취소 검토와 최적화 방향 포함. 구현 코드는 수정하지 않음.

**확인**: HEAD `73063b4`, 시작 시 작업 트리 깨끗함. `AA_REAL=0` 서버 테스트 91 통과·1 건너뜀, 웹 빌드 통과. 저장소 밖 임시 프로그램에서 현재 DLL과 가짜 장비로 6개 핵심 경로 재현. 앱 실행·실장비 조회/명령·실제 사진 이동 없음. 테스트 수는 실기 검증을 뜻하지 않음.

**남은 것**: 구현 담당의 리뷰 처리와 실패/경쟁 조건 회귀 테스트. 리뷰·HISTORY만 기록, 커밋·푸시하지 않음. 아래 구현 항목의 날짜(2026-10-07)는 원문 유지.

---

## 2026-10-07 · 73063b4 [Claude] 촬영 · 마무리 구현 (시안 v10), 가이드 별 잃음 원인별 대응, AA 종료 확인 창

**요청**: 촬영·마무리 흐름 합의 → 시안 v10 → 앱 구현(코덱스 리뷰 없이 먼저, 장비 연결 전제). 사진 등급 A+·A·B·C·F, F는 제외 폴더(지우지 않음), 등급은 왼쪽 아래(지금 등급 + 등급별 누적, 기준 툴팁). 디더링·자오선 반전·초점 다시·구름은 자동. "촬영 중단" → 끝내고 마무리 / 다른 대상으로 변경 / 계속 찍기. 마무리 = 플랫(수동 패널, 30장) → 뚜껑 → 플랫 다크·다크(기본 20, 5장씩 조절, 여기까지만) → 장비 정리(N.I.N.A.·PHD2 닫기) → 오늘 밤 요약. 레일 "앱 끄기" → "AA 종료" + 진행 중이면 확인 창, 요약에서는 숨김. 가이딩 중 별 잃음은 원인(빛·구름·이슬·가이드 초점·바람/케이블·적도의 멈춤·끊김)별 대응. X-T5는 N.I.N.A. gain = ISO(사용자 확인). 답변은 항상 한국어(CLAUDE.md)

**변경**
- 서버 `Shoot/`: `ShootSession`(끝 조건·반전·재초점·디더링·등급·제외 폴더·`NightShootResult` 기록, 찍기 전·찍는 동안 가이드 별 지켜보기 → `HandleLossAsync`·`WaitForStarAsync`, 원인별 멈춘 시간), `ShotGrader`(등급 규칙), `GuideWatch`(원인 가르기 규칙·이슬점), `IShootDevices` + `Simulated/RealShootDevices`(PHD2 이벤트 수신·디더·별 다시 고르기·가이더 다시 연결·다시 가운데). API `/api/shoot/*`, `/api/night/summary·open-folder`
- 서버 마무리 `Prepare/Tasks/Wrap/`: `FlatTask`·`DarkTask`·`PackTask` + `IWrapDevices`(모의·실제), `RunnerSetup.Wrap`, `PrepareFlow.Wrap`, `/api/prepare/wrap/start`. 러너: 마지막 작업이 AutoNext면 끝 버튼 없이 Ready. `NinaRig`: imageType·gain(ISO)·Home·Flip·Connect/Disconnect·BitDepth
- 화면: `ShootScreen`(+`ShootPanels` 계기판·등급·반전 단계, 원인별 멈춤 안내, 모의 실패), 마무리 = `PrepareScreen group="wrap"`(`PrepCenter` 다크 장수 고르기), `SummaryScreen`, `ConfirmDialog`, `StepRail`(`complete`·`hideExit`, "AA 종료"), App 흐름 target → shoot → wrap → summary, 촬영 들어가면 적색 테마
- 시안 `mockups/aa-shoot-wrap-v10.html`(새 파일), v9·v10 레일 세로 가운데 CSS 고침
- 문서: DESIGN.md "촬영"·"마무리" 절, `docs/SHOOT_IMPLEMENTATION.md`(구조·실장비 확인 목록·노출/별 잃음 설계와 PHD2 실기 결과), CLAUDE.md 한국어 규칙

**확인**: 서버 테스트 91 통과·1 건너뜀(새로 — 등급, 끝 조건, F 제외, 반전·재초점·멈춤 자동, 중단, 마무리 전체·다크 장수·여기까지만·건너뛰기·패널 밝음, 별 잃음 원인 가르기·원인별 흐름·적도의 멈춤). 화면은 서버 응답을 흉내 낸 임시 페이지에서 캡처(촬영·중단 질문·반전·재초점·멈춤·끝·다크 장수·요약, 적색 픽셀 검사). PHD2 실기(내장 Simulator + On-camera): GuideStep 필드, 가이딩 중 노출 바꾸기, LostLock·StarLost, 별이 돌아오면 PHD2가 스스로 재개, 가이딩 중 find_star 거절 → 멈춤·루프·고르기·가이딩 순서로 7초 재개. RPC에 게인 명령 없음. 실장비(N.I.N.A.·X-T5·적도의)로는 촬영·마무리 미확인, N.I.N.A.를 켠 앱 전체 흐름도 이 세션에서 직접 못 봄

**남은 것**: 실장비 확인 8가지(docs/SHOOT_IMPLEMENTATION.md), WandererBox 기온·습도(이슬점)·열선 제어, 노출 설계 A(시작 사다리·기억·SharpCap), 별 모양(이심률) 분석, 등급·SNR 기준값 실제 하늘로, AA 장비 확인에서 PHD2가 시뮬레이터 프로필이면 알리기

**리뷰 요청 범위(Codex)**: `src/Astro.Server/Shoot/*`, `Prepare/Tasks/Wrap/*`, `Prepare/Flow/PrepareRunner.cs`(AutoNext 끝·Wrap)·`PrepareFlow.cs`, `Prepare/Real/NinaRig.cs` 추가분, `AppServer.cs`(shoot·night·wrap 경로), web `screens/ShootScreen·SummaryScreen`, `components/ShootPanels·ConfirmDialog·StepRail·PrepCenter`, `App.tsx`. 특히: 촬영 루프의 동시성(찍는 동안 지켜보기·노출 멈춤·중단 요청), 끝날 때 가이딩 정지 확인, 다크 중 "여기까지만" Ask 취소 처리, 원인 판정 규칙의 오판 가능성, 실장비 경로의 위험(적도의 이동·프로그램 닫기·파일 옮기기)

---

## 2026-10-07 · f52a805 [Claude] 단계 재구성(장비 준비 · 대상) — 러너 둘, 초점 확인, 레일 움직임, 시안 v9

**요청**: 단계를 연결 → 점검 → 장비 준비(극축·캘리브레이션·초점) → 대상(계획·이동·센터링·초점 확인·가이딩·시험 사진) → 촬영 → 마무리로 재구성(Codex 의견 반영). v9 시안으로 흐름 확정 — 단계 끝 수동 확인이 곧 다음 단계로, 레일은 부드럽게 전환·초록 선이 한쪽 끝부터 자라게, 작업 간격 75px. 초점 확인은 기온 변화 + 센터링 사진 별 크기 두 근거. 그 뒤 앱 구현. 업데이트 알림은 완성 전까지 설치 프로그램 4개 모두 새 버전이 있는 것으로

**변경**
- 시안 `mockups/aa-prepare-arcs-v9.html`(새 파일): 장비 준비·대상 장면, "장비 준비 끝" 장면 없앰(초점 결과 → "대상 고르기"), 레일을 한 번 만들고 상태만 바꿈(반쪽 선 채움·묶음 접기/펴기), 초점 확인 두 근거
- 서버: `PrepareFlow`(러너 둘 + 그날 밤 결과 + 묶음 사이 멈춤·넘김 `BeforeRun`·`ForeignRedo`·`SuspendAsync`), `RunnerSetup.Rig/Target/Single`(의존표·끝 버튼), `Completed.AutoNext`, `PrepContext` 계획 없이 시작(`HasPlan`, `RefocusRequested`), 새 작업 `FocusCheckTask`, `CenterResult`·`CenterAttempt`에 HFR·별 수, `FocusCheckResult`. 센터링 끝 "이 대상으로 확정 / 다른 대상(flow:replan)", 센터링 실패·시험 사진 초점 문제는 초점 확인에서 다시 맞춤. 가이딩 → 캘리브레이션 다시는 대상을 멈추고 장비 준비로 넘긴 뒤 이어서. API `/api/prepare/{rig|target}/…`. 모의 실패 `focus.temp`·`center.hfr`·`guiding.calibration`
- 화면: 단계 이름(장비 준비·대상), 흐름 점검 → rig → plan → target → 촬영(`App.tsx`), `PrepareScreen`을 묶음 겸용으로, 계획 화면 버튼 "이 대상으로 이동"·레일에 대상 작업, `StepRail` 움직임(v9 방식, 화면이 넘긴 작업 목록은 `stage`가 맞을 때만 — 촬영으로 넘어갈 때 앞 단계 작업이 펼쳐지던 오류 고침)
- 문서: DESIGN.md "단계 재구성" 절, PREPARE_IMPLEMENTATION.md 재구성 계획·진행. appsettings `Updates:Pretend` 4개

**확인**: 서버 테스트 76 통과·1 건너뜀(두 묶음 흐름 13개 새로 — 대상을 바꿔도 장비 준비 결과 유지, 극축 다시 → 대상 전체 다시 확인, 가이딩 → 캘리브레이션 넘김·이어서, 초점 확인 두 근거). 레일은 임시 페이지에서 전환 중간 캡처로 확인. 웹·데스크톱 빌드. 앱 전체 흐름은 사용자가 모의 모드로 실행해 레일 오류를 찾음 → 고침(재확인은 임시 페이지). N.I.N.A.를 켠 상태의 전체 흐름은 이 세션에서 직접 확인 못 함

**남은 것**: 계획 화면 첫 인사에 장비 준비 요약(v9), 계획을 미리 정해 온 경우의 흐름, 계획 화면 구성 개편(사용자 예정), 실장비 센터링 사진 HFR·별 수, 출시 전 `Updates:Pretend` 비우기. DESIGN.md에 레일 움직임·단계 끝 버튼 규칙 문구 추가는 제안만 함

---

## 2026-10-06 · a20a1b5 [Claude] 준비 단계 실장비 구현(P3) — 7작업, 실내 실기 확인

**요청**: 어제 실기로 확인한 장비 동작이 앱에 들어가 있지 않았음 — 실내에서 더 확인할 것을 먼저 시험하고, 그 결과로 준비 단계 1~7작업을 실제 장비로 구현

**실내 실기 시험(구현 전)**: SharpCap 스크립트 → AA 통로(1초마다 단계·조절량, 위치를 못 찾으면 0,0 → 단계로 구분), X-T5 촬영(카메라가 JPEG로 설정돼 전부 0 → 사용자가 RAW로 바꿔 정상, 실패해도 이전 사진이 옴), 추적 켜고 끄기, N.I.N.A. 가이딩 시작·중지(별 없이도 "started"), 이동 중 멈춤(slew/stop). 결과는 docs/api-coverage.md

**변경**
- `Prepare/Real/`: `NinaRig`(이동·멈춤 확인·추적·촬영(이벤트로 새 사진 확인)·통계·동기화·포커서·자동초점·가이딩), `Phd2Client`(상시 JSON-RPC + 이벤트), `SharpCapBridge`(실행·스크립트 보고/명령·닫기), `AscomAxis`(RA MoveAxis), `LiveImages`·`WindowCapture`(PrintWindow, DPI 맞춤)·`Fits`·`Png`, 작업별 `Real{Polar,Calibration,Slew,Focus,Center,Guiding,TestShot}Devices`
- `PrepareSetup.AddPrepare(simulate)` — `Equipment:Simulate`에 따라 모의/실제, `PrepView.Simulated`. 서버 `GET /api/prepare/live/{kind}`, `POST /api/prepare/sharpcap/report`. `NinaApiClient.RequestAsync`(오류 문장 포함)
- `BackgroundWindows.KeepBehind` — SharpCap은 최소화하지 않고 AA 뒤로. 극축 정렬 중간 멈춤이면 SharpCap 닫고 카메라를 PHD2에 돌려줌
- 화면: 실장비면 하늘 화면에 실제 이미지(1초마다 새로, 다 받은 뒤 교체), [임시] 모의 실패는 모의일 때만. 극축 상태 줄에서 별 개수 뺌(SharpCap은 안 줌), 캘리브레이션 SNR 없으면 "별을 골랐습니다", 시험 사진 실패 안내에 RAW 확인
- 테스트 `Real/RealDeviceTests`(AA_REAL=1일 때만 장비를 움직임)

**확인**: 빌드 경고·오류 0, 테스트 66 통과·1 건너뜀(평소엔 실장비 시험이 아무것도 안 함). 사용자 허락 받고 실장비 시험 7개 — ⑦ 촬영·통계·저장, ④ 포커서(처음엔 백래시 중간 멈춤을 실패로 봐서 고침), ③ 이동 0.29°·멈춤 확인, ② 위치 A 0.37°·별 없음(PHD2 멈춤 확인을 기다리게 고침), ⑥ 별 없음 판정, ⑤ 솔빙 실패 판정, ① 앱 화면으로 넘겨주기 → SharpCap → 실패 → 중단하면 원래대로(창 캡처가 왼쪽 위만 확대되던 DPI 문제 고침). 적도의는 홈·추적 끔, 시험 서버 종료

**남은 것**: 맑은 날 별로 확인(docs/PREPARE_IMPLEMENTATION.md P3 목록 — SharpCap 조절 단계·단위, RA 회전 방향, 캘리브레이션·가이딩·자동초점·솔빙 결과), SharpCap "Allow smaller rotation angles" 켜기, 하늘 화면에 SharpCap 영상 부분만, 시험 사진의 별 모양·포화 분석

---

## 2026-10-06 · bd817f9 [Claude] Codex 코드 리뷰 반영(CX-PREP-CODE-01~06), 새 버전 확인 앞당김·동시 요청

**요청**: Codex 리뷰(REVIEW_CODEX.md 12절) 검토 — 6개 모두 동의, 최적화 제안 중 업데이트 동시 요청·방향 값도 Claude가 반영. 확인 중 프로필을 눌러 새 버전 소식을 놓치지 않게

**변경**
- CODE-01: `IPrepTask.KeepsRunning`(가이딩 true). 러너 `StopLingeringAsync` — 다시 하기(버튼·작업 요청 모두)로 무효가 되는 작업 중 계속 도는 것을 먼저 멈추고 확인, 그 작업·뒤 작업은 다시 확인 필요. 확인 못 하면 "장비 상태 다시 확인"
- CODE-02: `SlewTask` 멈춤 — 정지 명령을 이동이 끝나기 전에 바로, 정지 확인 전에는 "장비 상태 다시 확인"만(다시 이동 없음). 러너 예외 뒤 "다시 시도"도 정지 확인을 거침(`RetryAsync`), 정지 미확인 표시는 `ShowStopUnconfirmed` 하나로
- CODE-03: `AbortAsync` — 끝난 작업이어도 중단 상태(다음·촬영 시작 지움, Ready 해제), 계속 도는 가이딩도 멈춤. "다시 시작"(`restart`)은 다시 확인이 필요한 첫 작업부터
- CODE-04: `PolarTask` — 조절량을 못 받았거나 5초 넘게 갱신 없으면 "정렬 완료"를 받지 않고 기다리게(0·좋은 등급 저장 안 함). 측정값 관측 시각은 장비 시각(`ITaskRun.Readout`의 `observedAt`)
- CODE-05: `TestShotTask` — 노출 실패면 내려받기로 가지 않고 "다시 찍기". `DownloadAndAnalyzeAsync(since)`로 이번 노출 뒤 파일만. 모의 실패 `test.expose` 추가
- CODE-06: `IPrepMemory.LastPolarAlignedAt` — 극축 정렬을 마치면 기록, 그 뒤에 만든 캘리브레이션만 재사용
- 최적화: 캘리브레이션 측정값에 방향 값(`north`), `LiveView`가 문장 대신 값으로 그림. `UpdateChecker` 네 곳 동시 요청(`Task.WhenAll`), 앱이 켜질 때 바로 확인 시작(`AppServer`) — 프로필 화면에서 결과가 대부분 준비됨
- 테스트 `ReviewFixTests` 8개(Codex 후속 검증 기준), `FakeTask.KeepsRunning`

**확인**: 서버 테스트 60 통과·1 건너뜀(실기용), 15번 연속 통과(새 테스트의 경쟁 조건 하나는 기록을 기다리게 고침). 웹 빌드 통과, 시험 서버로 준비 7작업 처음부터 촬영 단계까지 JS 오류 없음, 시험 서버 종료, 데스크톱 앱 다시 빌드. 실장비 어댑터가 없어 실제 장비 정지 확인은 모의로만

**리뷰 처리**: CODE-01~06 반영. 최적화 2(화면 계산 재사용)·3(배경 분리)은 측정 뒤 판단 — 보류. 가이딩 그래프 데이터 상한·한 샘플 원자적 갱신은 실장비 어댑터(P3) 때. 실기 응답의 "NaN" 문자열·빈 축 속도·RA 시간/도는 P3 어댑터에 반영

---

## 2026-10-06 · [Codex] 99a494e 연결~준비 구현 리뷰

**요청**: 연결부터 준비까지 구현한 `99a494e` 리뷰와 코드 최적화 검토. 텍스트 폴리싱 제외, 구현 수정 없이 리뷰만.

**변경**: `docs/codex/REVIEW_CODEX.md` 12절에 CX-PREP-CODE-01~06과 최적화 의견 기록. 완료 후 중단, 재센터링 전 가이딩 정지, 정지 실패 후 재이동, 극축 측정 누락, 노출 실패 무시, 극축 재정렬 후 보정값 재사용을 지적. 앱·테스트 소스는 수정하지 않음.

**확인**: HEAD `99a494e`, 시작 시 작업 트리 깨끗함. 서버 테스트 53 통과·실기 1 건너뜀, 웹 빌드 통과. 저장소 밖 임시 C# 검증 프로그램에서 기존 빌드 DLL과 모의 장비로 6개 경로 재현. 앱 실행·실장비 명령 없음. 준비 모의 어댑터·P3 미구현·요청된 업데이트 알림 가짜 버전은 결함에서 제외.

**남은 것**: 리뷰 처리 판단과 구현은 후속 작업. 특히 정지·재실행 경계를 실장비 어댑터 연결 전에 보강. 커밋·푸시하지 않음.

**후속 실기 조회**: 사용자 요청으로 실제 NINA Advanced API 2.2.15.2의 version/장비 info/프로필 PHD2 설정과 PHD2 get_current_equipment만 읽음. On-Step·X-T5·Oasis·WandererBox 및 PHD2 내부 G3M662M/On-Step 연결 응답 확인. 실제 NinaApiClient·Phd2Check로도 정상 파싱 확인. 추적·가이딩·노출·포커서 이동은 정지로 보고됨. `"NaN"` 문자열, 비어 있는 축 속도 객체, RA 시간/도 구분을 리뷰 파일에 기록. 연결 변경·설정 변경·장비 동작 명령은 없음.

**후속 최적화 제안**: 사용자 요청으로 Codex의 담당 후보와 검증 방법을 리뷰에 추가. 업데이트 조회 병렬화를 우선하고, 화면 계산 재사용·정적 배경 분리는 측정 후 판단. 중단·재시도 등 제어 로직 수정과 분리하고 정상/실패/캐시의 전후 동작 비교 및 동시 편집 회피를 명시. 제안 기록만 했으며 구현 미착수.

---

## 2026-10-06 · 99a494e [Claude] 가이더 연결 때 PHD2 안의 카메라·적도의까지 확인

**요청**: N.I.N.A.는 "가이더가 연결됨"인데 PHD2는 카메라 연결 실패·적도의 시뮬레이터였던 일 — 가이더를 연결할 때 AA가 PHD2에 직접 물어 확인

**변경**: 새 `Engine/Phd2Check`(PHD2 JSON-RPC `get_current_equipment`, 이벤트 줄은 건너뛰고 같은 id의 답만): 카메라 연결 안 됨 / 적도의 연결 안 됨 / 적도의가 시뮬레이터이거나 N.I.N.A. 적도의와 다름(On-camera·ST4는 통과) → 문제 문장 + 할 일. PHD2에 닿지 못하면 막지 않음. `EquipmentConnector`가 실제 연결에서 가이더 연결 뒤 부름(주소·포트는 N.I.N.A. 프로필 GuiderSettings), 문제면 실패 칸(준필수라 "없이 진행" 가능). `NinaApiClient.GetInfoAsync`. DESIGN.md "장비 연결"에 가이딩 원 규칙

**확인**: 가짜 PHD2 서버 테스트 7개(정상·카메라 꺼짐·적도의 꺼짐·시뮬레이터·다른 적도의·On-camera·닿지 못함) 통과, 실제 PHD2(사용자가 고친 뒤 G3M662M·On-Step)에 읽기만 하는 확인 → 문제 없음(이 테스트는 평소 건너뜀). 전체 53개 통과·1개 건너뜀, 빌드 경고·오류 0. 앱 화면에서 실패 칸으로 보이는 것은 실기 확인 못 함

---

## 2026-10-06 · 99a494e [Claude] 진행 표시: 작업은 제목만, 지금 작업은 글로우

**요청**: 펄스는 선이 아니라 원 뒤에서 빛이 퍼지는 효과(글로우)로. 진행 표시에 작업 결과는 보이지 말고 제목만, 끝난 작업도 제목을 가리고 마우스를 올릴 때만 (처음 "꺼지지 않게"를 끝난 이름을 늘 보이게로 잘못 알아들어 되돌림)

**변경**: `StepRail` — 작업 줄에서 결과(`RailItem.result`)를 없앰(PrepareScreen도 넘기지 않음), 이름은 지금·문제 작업만 늘, 나머지는 올리거나 초점일 때. 지금 작업 점의 `::before` 테두리 펄스 → 점의 box-shadow 글로우. DESIGN.md 진행 표시 규칙

**확인**: 웹 빌드 통과, 시험 서버로 준비 ③ 진행 중 진행 표시 캡처(글로우·올렸을 때 제목), 시험 서버 종료. 데스크톱 앱 다시 빌드. PHD2 장비 경고 건: 사용자가 PHD2 장비 설정을 고친 뒤 PHD2에 물어 카메라 G3M662M·적도의 On-Step 연결, 배율 4.98″/px 확인(AA 코드 변경 없음)

---

## 2026-10-06 · 99a494e [Claude] 새 버전 서랍이 화면을 흔들던 것 고침, 다른 프로그램 창이 AA 위로 뜨지 않게

**요청**: 새 버전 서랍을 열면 화면이 갱신되는 것처럼 보임 — 화면은 그대로, 서랍만 올라오게. N.I.N.A.·PHD2 등이 켜질 때 AA 위로 떠서 Alt+Tab을 해야 함 — 뒤에서(최소화로) 켜지게

**변경**
- `UpdateNotice`: 서랍·버튼으로 초점을 옮길 때 `preventScroll` (화면 영역이 132px 스크롤됐다 돌아오던 것), `ProfileScreen` 무대 `overflow: clip`
- 새 `Engine/BackgroundWindows`: 지켜보는 동안 그 프로세스의 새 창을 활성화 없이 최소화(SW_SHOWMINNOACTIVE, 로딩 끝에 다시 뜨는 창도). `EngineStarter`(N.I.N.A. 켤 때 ~ 응답 + 8초, 시작 옵션도 최소화), `EquipmentConnector`·`RigSetup`(가이더 연결 → PHD2, 전원 허브 → Wanderer Empire, 연결 + 5초). 미리 열린 창·목록 밖 창은 건드리지 않음
- DESIGN.md 3장 "다른 프로그램 창이 AA 위로 뜨지 않게"

**확인**: 시험 서버에서 서랍을 열며 0.03~0.6초 동안 제목 위치·스크롤을 재어 움직임 없음(고치기 전엔 화면 영역이 132px 스크롤). 창 최소화는 임시 테스트로 메모장을 켜서 활성화 없이 최소화되는 것 확인(테스트는 지움). **N.I.N.A.·PHD2·Wanderer Empire로는 확인 못 함**(사용 중인 N.I.N.A.를 껐다 켜지 않음). 빌드 경고·오류 0, 테스트 46개 통과

**남은 것**: 실기에서 N.I.N.A. 시작·가이더·허브 연결 때 창이 내려가는지 확인

---

## 2026-10-06 · 99a494e [Claude] 프로필 화면에 새 버전 알림

**요청**: 설치된 프로그램의 최신 버전 확인 — 확인 단계를 따로 두지 말고(지금 업데이트하라는 느낌), 프로필 화면 구석의 버튼 → 아래에서 올라오는 서랍에 프로그램별 알림과 링크

**변경**
- 서버 `Setup/UpdateChecker.cs`: 설치된 버전(N.I.N.A.·ninaAPI.dll 파일 버전, ASCOM 레지스트리, PHD2는 실행 파일 안 글자) vs 최신 버전(N.I.N.A. 정식판 정보, N.I.N.A. 플러그인 목록의 Advanced API — 설치된 N.I.N.A.에서 쓸 수 있는 것만, GitHub 정식 릴리스 ASCOM·PHD2). 최신 버전은 하루 한 번 `updates.json`, 다시 알리지 않기는 `updates-dismissed.json`. 실패하면 빈 목록. `GET /api/updates`, `POST /api/updates/dismiss`
- [임시] `Updates:Pretend`(appsettings.json, 지금 {"nina": "3.3.0.1001"}): 인터넷에서 받은 최신 대신 이 버전이 나온 것으로 본다 — 이 PC가 모두 최신이라 화면을 보려고(사용자 요청). 확인이 끝나면 비운다
- 웹 `components/UpdateNotice`(왼쪽 아래 "새 버전 n개", 한 번 지나가는 빛, 아래 서랍), `ProfileScreen`에 붙임
- DESIGN.md "프로필 화면"에 새 버전 알림 규칙
- 테스트: 버전 비교 5개(`Setup/UpdateCheckerTests`). 준비 러너 테스트 하나가 가끔 실패하던 것(다시 시작 기록 전에 순서를 검사) — 기록을 기다리게 고침, 15번 연속 통과

**확인**: 이 PC는 네 프로그램 모두 최신이라 실제로는 버튼이 안 보임(N.I.N.A. 3.2.0.9001, Advanced API 2.2.15.2, ASCOM 7.1.3, PHD2 2.6.14 — 인터넷에서 받은 최신과 같음). 테스트 데이터 폴더에 더 새 버전 기록을 넣고 시험 서버로: 설치된 버전 읽기·ASCOM 같은 버전 제외·버튼·서랍·다시 알리지 않기(파일 기록, 개수 줄어듦)·Esc 닫기, JS 오류 없음. 시험 서버 종료, 테스트 46개 통과

**남은 것**: SharpCap 버전 확인. AA가 쓸 수 없는 옛 버전을 설치 확인에서 막는 기준(최소 버전)

---

## 2026-10-06 · 99a494e [Claude] 진행 표시 정리(작업만·폭 줄임), 장비 실제 연결로 전환

**요청**: 진행 표시에는 작업만(세부 과정 빼기), 진행 표시 왼쪽 여백을 1/3로(글자가 넘쳐도 됨). 어제 파워박스로 직접 연결해 본 장비로 앱에서 실제 연결해 진행(메인 카메라 제외)

**변경**
- `PrepareScreen`: 진행 표시 작업 줄에 세부 과정 이름을 넣지 않음(끝난 작업의 결과 한 줄은 그대로)
- `--rail-w` 240→150px, `StepRail` `overflow: visible`, App `.rail`을 화면 영역 위로(z-index) — 긴 작업 이름이 왼쪽으로 넘쳐 보임
- `appsettings.json`: `Equipment:Simulate` false(N.I.N.A. 활성 프로필의 장비를 실제 연결), `Setup:SimulateMissing` false(설치 확인도 실제). 메인 카메라 대신 사용자가 N.I.N.A.에서 N.I.N.A. Simulator Camera를 고름 — 처음 넣었던 `Equipment:Skip`(카메라를 "없이 진행"으로)은 필수 장비라 화면이 통과로 보지 않아 장비 연결에서 멈춰서 뺌
- DESIGN.md 진행 표시 규칙(폭·작업만)

**확인**: 솔루션 빌드 경고·오류 0, 테스트 41개 통과, 웹 빌드 통과. 모의 설정의 시험 서버(5211)로 준비 끝까지 둘러보기 — 진행 표시에 세부 과정 없음, 긴 이름이 왼쪽으로 넘침, JS 오류 없음, 시험 서버 종료. **실제 장비 연결은 돌려 보지 않음**(사용자가 직접 실행). 준비 7작업은 여전히 모의 장비(실장비 어댑터는 P3)

**남은 것**: 실기 연결 결과 확인(N.I.N.A. 활성 프로필 "유성철"로 연결됨, 적도의는 전원을 켜야 COM3이 잡힘). P3 실장비 어댑터

---

## 2026-10-05 · 99a494e [Claude] 촬영 준비 화면 v8 이식(P2)

**요청**: 앱의 준비 단계가 옛 쉐브론 화면 — 첫 실행부터 준비 단계까지 최신으로, 기능까지 붙여서

**변경**
- 새 공통 부품 `PrepGuide`(안내)·`PrepCenter`(중앙 정보: 측정값 → 상태 줄 → 버튼, ① 등급·나사 방향, 주 버튼 가운데 고정·보조 버튼 오른쪽)·`LiveView`(하늘 화면: SharpCap·가이드 카메라·하늘 지도·초점 곡선·솔빙 사진·시험 사진·9칸, 가이딩 그래프) — 모의 장비라 측정값·live.data로 그림을 흉내
- `PrepareScreen` 새로 씀: 하늘 화면 위에 안내·중앙 정보, 사진만 보기·9칸 확대 보기, [임시] 모의 실패는 왼쪽 아래 접힌 줄. 진행 표시에 지금 작업의 세부 과정 이름, 실패 상태면 문제 표시
- 토큰 `--sky`·`--star`·`--sky-muted`·`--sky-line`(세 테마), `RailExtra.sky`로 준비 단계에서 진행 표시를 하늘 위에(App `.body[data-sky]`, 점 속 `--rail-bg`)
- DESIGN.md "촬영 준비"에 하늘 화면 색·사진 보기 규칙, 구현 계획 P2 끝남 표시

**확인**: 웹 빌드 통과. 시험 서버(5211, 모의 장비)에서 헤드리스로 계획 → 준비 7작업을 주 버튼만 눌러 "촬영 시작" → 촬영 단계까지(장면 33장), 넘겨받기 실패 → 다시 시도, 같은 밤 캘리브레이션 재사용 질문, 밝게·촬영 테마, 9칸·사진만 보기 — JS 오류 없음. 시험 서버 종료, 데스크톱 앱 다시 빌드. 데스크톱 창에서 직접 보지는 못함

**남은 것**: 실장비 사진·창 캡처 엔드포인트(`/api/prepare/live/*`)와 어댑터(P3). 진행 표시에서 끝난 작업을 눌러 결과 보기·다시 하기(DESIGN.md "이전 단계로 돌아가기")는 아직 없음(마우스를 올리면 결과 한 줄만)

---

## 2026-10-05 · 99a494e [Claude] 촬영 준비 P1(작업별 서버 구조·모의 장비·테스트), 임시 준비 화면, 진행 표시 오른쪽 세로 열

**요청**: 시안 확정 후 개발 시작. 7작업을 잘 분리해 한 작업을 고쳐도 나머지가 정상 동작하는 구조로 (계획 문서 2.3). 이어서 "계획에서 다음 단계로 안 넘어간다" → 고침, "단계 레일을 우측으로 전부 옮겨 줘"

**변경**
- 옛 `Prepare/PrepareRunner.cs`·`PrepareDevices.cs`(한 클래스에 7단계, 장비 인터페이스 하나) 삭제
- `Prepare/Flow/`: `PrepareRunner`(순서·끝 버튼·다시 하기 의존표·끝 상태 약속 확인·멈춤 확인·실행 번호·오래된 값), `IPrepTask`·`ITaskRun`·`PrepContext`·`MountLock`·`IPrepMemory`, 결과 기록 7개(`PrepResults.cs`), 화면 모델(`PrepView.cs`: 안내·중앙 정보·하늘 화면·진행 표시)
- `Prepare/Tasks/{Polar,Calibration,Slew,Focus,Center,Guiding,TestShot}/`: 작업마다 클래스 + 좁은 장비 인터페이스 + 판정 규칙 + 모의 장비 (DESIGN.md ①~⑦의 흐름·실패 처리)
- `Prepare/Sim/`: 모의 속도·실패 걸기(`SimFaults.Known`), `Prepare/PrepareSetup.cs`(등록 순서 = 작업 순서, `PrepareStarter`)
- `AppServer`: `/api/prepare/start·state·watch·act·abort`, `/sim/fail-next/{작업.과정}`·`/sim/ignore-altitude`·`/sim/speed/{x}`
- `tests/Astro.Server.Tests`(xUnit, 솔루션에 추가), 서버에 `InternalsVisibleTo`, `.gitignore`에 `TestResults/`
- 웹 `prepare.ts`를 새 상태 모양으로, `PrepareScreen.tsx`는 [임시] 화면(옛 쉐브론 틀 + 안내·세부 과정·측정값·상태 줄·버튼 + 작업별 모의 실패 버튼) — 옛 화면이 `view.steps`를 읽다 죽어 계획 → 준비로 못 넘어가던 문제
- 진행 표시를 아래 가로 줄 → 오른쪽 세로 열(모든 단계): `StepRail` 다시 씀(단계 점·지금 단계 아래 작업 점·맨 아래 화면 버튼·앱 끄기, `RailProvider`·`useRailExtra`), `App.tsx`·`App.module.css`(`.body` 가로 배치, `.screen`이 크기 기준 틀), `--rail-w`. DESIGN.md 3장 규칙 갱신
- `AppServer`: `.html`에 `Cache-Control: no-cache` — 웹을 다시 빌드해도 데스크톱 창(WebView2)이 캐시된 옛 index.html로 아래 가로 레일을 보여 주던 문제

**확인**: 테스트 41개 통과 — 작업 단독 13(자기 모의 장비 + 끝 상태 약속), 러너 8(가짜 작업: 순서, 약속 어김이면 멈춤, 의존표, 정지 확인 후 재시작, 정지 확인 실패, 늦은 갱신 버림, 오래된 값 표시), 흐름 5(끝까지·가이더 없음·위치 B·센터링 실패→초점부터·낮은 대상), 규칙·결과 모양 15. 솔루션 빌드 경고 0. 시험 서버(5211)로 `/api/prepare/state`·`/start`(계획 없음 거절)·`/sim/fail-next` 확인 후 종료. 웹 빌드 통과, 헤드리스 둘러보기(설치 확인 → 장비 → 점검 → 계획 → 준비 극축 정렬, 작업 이름 올리기)에서 JS 오류 없음·화면 겹침 없음 확인 후 시험 서버 종료. 데스크톱 창(AA.exe)에서는 확인 못 함. 테스트 중 찾아 고친 버그 2개: 약속 어김 "다시 확인"이 안 되던 것, 정지 재확인 뒤 표시가 안 풀리던 것

**남은 것**: P2 화면(v8 이식: 안내·중앙 정보·하늘 화면, 임시 화면 대체). 실제 장비 어댑터(P3)

---

## 2026-10-05 · [Codex] v7 별자리 제거·버튼과 정보 배치 정리

**요청**: 별자리는 이후로 미루고 v7 그대로 배치 최적화. 주 버튼은 중앙, 재시도 등 보조 버튼은 오른쪽에 작게

**변경**: 별자리 표시·설명·확대 전환 제거. 버튼을 중앙 주 버튼 영역과 오른쪽 보조 버튼 영역으로 분리해 버튼 수에 따라 주 버튼이 밀리지 않게 함. 극축 조절 화살표 묶음 중앙 정렬. 버튼 아래 측정 영역 고정, 가이딩 그래프·이동 지도·9칸 사진이 버튼 영역과 겹치지 않도록 위치·크기 조정

**확인**: 문법·diff 검사, 모의 DOM/타이머로 7개 정상·7개 실패 흐름과 버튼 역할, 다음 작업 이동, 밝은 사진 재촬영 확인. 브라우저 도구가 file:// 접근을 보안 정책으로 차단하여 실제 렌더링 확인은 하지 못함. 다른 경로로 우회하지 않음

**남은 것**: 사용자 화면 확인 후 필요한 시각 보정. 커밋·푸시 없음

---

## 2026-10-05 · [Codex] 준비 7개 작업 공통 시안 v7

**요청**: v6 극축 정렬 화면의 구성과 방향을 이후 작업에도 적용. 모든 작업의 진행 버튼 중앙 배치, 세부 과정 수에 맞는 별자리 선정

**변경**: `mockups/aa-prepare-arcs-v7.html` 추가. 좌상단 작업·세부 과정명과 통합 설명, 중앙 하단 버튼·측정값, 넓은 배경과 좌우 페이드, 우측 레일·현재 작업 왼쪽 별자리를 공통 적용. 캘리브레이션 원형 가이드 화면 제거. 이동·초점·센터링 각 3과정, 시험 사진 4과정으로 시안 표시를 명시. 완료 확대 전환을 후속 작업에도 적용. 모호한 수동 대략 초점 건너뛰기 버튼 제거, 밝은 시험 사진 그대로 진행 시 경고 기록 유지

**별자리**: 극축 정렬=남십자성(4), 캘리브레이션=삼각형자리(3), 대상 이동=겨울 대삼각형(3), 초점=여름 대삼각형(3), 센터링=남쪽삼각형자리의 주요 세 별(3), 가이딩=오리온 허리띠(3), 시험 사진=페가수스 대사각형(4). 정식 별자리와 성군의 차이·단순화한 UI 도식임을 시안 설명에 명시. IAU·NASA 자료 링크 포함

**확인**: JavaScript 문법 검사와 모의 DOM·가상 타이머로 7개 정상/7개 실패 시나리오, 보정값 재사용, 밝은 사진 재촬영, 낮은 대상 대기 확인. 이후 짧은 과정 표시 시간 보완 및 문법 재확인. 브라우저 렌더링·실장비 검증은 미실시

**남은 것**: 사용자 시각 검토와 화면별 배치 조정. 앱 코드 변경·커밋·푸시 없음

---

## 2026-10-05 · [Codex] 극축 정렬 별자리 시안 v6

**요청**: 작업 화면 구성은 유지하고, 준비 하위 단계를 하단 레일에 통합. 극축 정렬 네 과정은 우측 남십자성으로 표시

**변경**: `mockups/aa-prepare-arcs-v6.html` 추가(v5 기반). 왼쪽 원호·상단 과정 표시 제거, 준비–촬영 사이 7개 점과 현재 과정의 점·캡슐 표시, 별 점등·완료 연결선 및 사용자 다음 버튼 후 확대 전환. 1단계 궤도·접근·수평 이동 제거. 2~7단계 본문은 기존 시안 유지

**확인**: JavaScript 문법 검사 통과. 실제 브라우저 렌더링·장비 검증은 하지 않음

**남은 것**: 사용자 화면 확인 후 크기·간격·전환 조정. 커밋·푸시하지 않음

---

## 2026-10-05 · c263e53 [Claude] 준비 구현 계획에 Codex 리뷰 반영·작업 분리 설계, 시안 v8 확정, 화면 영역 이름

**요청**: Codex 리뷰를 검토해 추천대로 반영하고 처리 표를 채우기. 극축 정렬 등급은 사용자 결정 — 실제 모드는 환산 확인 전까지 방향·픽셀 값만. 7단계를 잘 분리해 한 단계를 고쳐도 나머지가 정상 동작하는 구조로

**변경**: `docs/PREPARE_IMPLEMENTATION.md` — 2.1 실행 번호·측정 시각·유효 상태(IMPL-04), 2.2 어댑터 계약·멈춤 확인·적도의 명령 잠금(IMPL-01·02), 2.4 다시 하기 순서·서버 재시작 경계(IMPL-05), 2.5 ① 등급은 환산 확인 후(IMPL-03), 5절 P1 추가 검증 네 경우, **2.3 단계 분리 설계**(사용자 요청 — 한 단계를 고쳐도 다른 단계가 그대로: 단계마다 클래스·좁은 장비 인터페이스·모의·테스트, 단계 사이는 결과 기록만, 단계마다 끝 상태 약속(러너가 장비에서 확인), 러너는 순서·의존표·공통 규칙만, 화면도 단계별 조각). `docs/codex/REVIEW_CODEX.md` 11절 처리 표

**확인**: 다섯 항목 모두 동의·반영(반대·보류 없음). 문서만, 앱 코드·장비 실행 없음

- 시안: Codex와 사용자가 만든 v5~v7(오른쪽 세로 진행 표시 등)을 커밋해 남김. v7 직접 실행 검토 후 사용자 피드백으로 **`mockups/aa-prepare-arcs-v8.html`**: 같은 숫자는 중앙 정보에만, 끝난 작업 제목은 결과 말, 측정값 위·상태 줄·버튼 아래, 보조 버튼 높이 통일, ⑥ 그래프 위치, ① 등급 크게, 진행 표시 아래 "준비 중단·앱 끄기", 촬영 테마 흐린 글씨 밝게, 진행 상태 줄을 왼쪽 위 → 가운데로
- DESIGN.md "촬영 준비": v8 확정, 용어(단계·작업·세부 과정), 화면 영역 이름(안내·중앙 정보·하늘 화면·진행 표시·상태 바), 오른쪽 진행 표시 유지(개발 후 이전 단계에도 반영). 구현 계획 3절 이름 맞춤

**확인(시안)**: v7·v8을 1920×1200 헤드리스로 7작업·세 테마 캡처, 스크립트 오류 없음

**남은 것**: P1(서버 모델 + 모의 장비, 작업 분리) 착수. Codex 재리뷰

---

## 2026-10-05 · [Codex] 준비 구현 계획 리뷰 기록

**요청**: PREPARE_IMPLEMENTATION.md 검토 결과를 합의된 리뷰 파일에 기록하고 작성자 라벨을 포함해 히스토리 남기기

**변경**: `docs/codex/REVIEW_CODEX.md` 10~11절 추가(CX-PREP-IMPL-01~05). 장비 중단 확인, 제어 명령 충돌 방지, 극축 오차 환산 검증, 측정값 유효성·이전 실행 차단, 재실행·서버 재시작 복구와 구현 계획의 여섯 질문 답변을 기록. 복귀 흐름은 DESIGN.md에 이미 있다는 정정 포함

**확인**: 최신 HISTORY·DESIGN·구현 계획·준비 서버 코드 대조 및 ASCOM·Microsoft 공식 문서 확인. 문서 변경만, 장비 실행·앱 테스트 없음

**남은 것**: Claude의 검토·처리 결과 기록, P1 모의 구현에서 취소·지연 결과·갱신 중단·정지 확인 실패 검증. 커밋·푸시하지 않음

---

## 2026-10-05 · 612a5c5 [Claude] 준비 시안 정리: Codex v3 추가, 수식어 붙은 시안 삭제

**요청**: `aa-prepare-arcs`로 시작하는 시안 중 v2·v3·v4만 남기고 뒤에 수식어가 붙은 것은 저장소에서도 삭제

**변경**: `mockups/aa-prepare-arcs-codex.html` 삭제, 미추적이던 `aa-prepare-arcs-v2-orbit-codex.html` 삭제, Codex의 `aa-prepare-arcs-v3.html`(v4의 바탕) 추가. v1 `aa-prepare-arcs.html`은 수식어가 없어 남김

**확인**: 남은 시안 v1·v2·v3·v4. 다른 문서에서 삭제한 파일을 가리키는 곳은 과거 기록(대화·HISTORY·REVIEW_CODEX)뿐

---

## 2026-10-05 · 0668730 [Claude] 준비 7단계 검토·실기 시험, 시안 v4(7단계 새 흐름), 준비 구현 계획(Codex 리뷰용)

**요청**
- 준비 7단계를 하나씩 검토하고 결정마다 v4 시안에 반영 (Codex v3 바탕)
- 실제 장비(가이드 카메라·적도의 UMi17X·허브·포커서)로 확인 가능한 것은 지금 확인
- Codex 리뷰용으로 1~7단계를 앱에서 어떻게 만들지 정리 (새 파일은 꼭 필요할 때만)

**변경**
- `mockups/aa-prepare-arcs-v4.html`: ① 위성 4개(넘겨받기·극 찾기·정렬·돌려주기), 정렬 중 위성 안 SharpCap 화면 + AA 평가 ② 위치 A→B, 가이드 사진 위성 ③ 기다리기·이동 막음·멈춤 ④ 지난 위치에서 시작·넓게 훑기→손 초점 ⑤ 결과 말·카메라 방향 기억 ⑥ 기준 2.41″·세 등급 ⑦ 자동 검사·9칸 보기·노출 변경 묻기. 경우 버튼 "시험 사진 배경이 밝음" 추가
- DESIGN.md 3장 "촬영 준비" ①~⑦: 사용자 결정·실패 처리·실기 결과 (① 90° 회전은 goto 아닌 RA만, ② 캘리브레이션 위치 A/B, ③ 낮은 대상·자오선 반전은 계획 단계에서 알림, ④ 포커서 한계는 제조사 기능, ⑥ 판정 기준 등)
- `docs/api-coverage.md`: N.I.N.A. slew `ra`는 도 단위, 스위치 `switch/set`이 안 닿음 → ASCOM 직접 `SetSwitch`, WandererBox 직렬 규격 요약
- `docs/PREPARE_SCREEN_CONTENT.md` → `docs/PREPARE_IMPLEMENTATION.md`로 이어 씀 (화면 내용은 DESIGN.md로 옮겨졌으므로 구현 계획으로 교체)
- 대화 기록 `conversations/2026-10-02-work-laptop.md` 4~7절, 맑은 날 할 일

**확인**
- 실기(실내, 별 없음): PHD2 카메라 넘겨주기·SharpCap 자동 실행·스크립트 HTTP·창 캡처·재연결, N.I.N.A.·PHD2 적도의 동시 연결, Go Home, goto A/B 오차 0.38°, 펄스 가이드 4방향, 포커서 이동·온도, 스위치 직접 제어
- 처음 goto 이상은 Claude의 단위 실수(시간으로 보냄)였음 — 바로잡고 기록
- v4 시안은 브라우저에서 단계마다 정상·실패 흐름 확인. 앱 코드 변경 없음
- Codex의 미추적 시안(`aa-prepare-arcs-v2-orbit-codex.html`, `aa-prepare-arcs-v3.html`)은 이 커밋에 넣지 않음

**남은 것**: Codex와 v4·구현 계획 리뷰 → 앱 착수 결정. 극에서 RA 60° 멈춤 원인, 맑은 날 실기 목록

---

## 2026-10-03 · 22cac17 [Claude] 준비 시안 v2(큰 하늘 화면 + 글 얹기, 극축 정렬 위성), 준비 절차 결정 추가

**요청**
- 가운데 화면을 더 넉넉하게: 화면을 오른쪽에 매우 크게, 글은 그 위에 얹기 (v2로 별도 파일)
- 라이브뷰가 없는 단계에 항성계 위성 실험: 세부 과정 3개가 비스듬한 궤도를 돌고, 맨 앞 위성 안에 그 과정 정보만, 지난·다음은 과장되게 흐리게, 도형은 크게
- 자동초점 설정은 AA 추천값, 구도 회전·이전 단계로 돌아가기·시험 사진 용도 정리

**변경**
- `mockups/aa-prepare-arcs-v2.html` (v1 `aa-prepare-arcs.html`은 그대로)
- DESIGN.md "촬영 준비": ④ 자동초점 설정 추천값(N.I.N.A. 설정 불변의 예외), ⑤ [나중] 구도 회전, ⑦ 시험 사진 용도·첫 장으로 안 씀, 이전 단계로 돌아가기(결과 보기 + 다시 하기, 영향받는 뒤 단계만)
- `docs/codex/REVIEW_CODEX.md` 처리 표 커밋 해시(01e0268), 대화 기록 `conversations/2026-10-02-work-laptop.md` 3장

**확인**: v2를 헤드리스로 7단계·위성 전환·다시 보기(원이 끝까지 돌아온 뒤 펼침)까지 오류 없이 확인. 앱 코드 변경 없음

**남은 것**: 사용자가 Codex와 시안을 더 다듬은 뒤 확정 → 준비 화면 앱 코드 다시 만들기

---

## 2026-10-02 · 01e0268 [Claude] 촬영 준비 7단계 원호 시안, Codex 준비 리뷰 처리, 항성계·깊이감·스켈레톤 방향

**요청**
- Codex 원호 시안 스타일로 준비 1~6단계 시안 → Codex 리뷰(8절 CX-PREP-FLOW) 반영해 7단계로, 정보는 한 곳에만, 원호를 왼쪽으로 밀어 공간 확보
- 극축 정렬 절차 확정(단계 끝은 모두 수동, PHD2 카메라 해제 시간 초과 처리, 추적 속도 항성), 캘리브레이션 기본값(준비 시작마다 새로·같은 밤 재사용)
- 항성계 화면 비유·Z축 깊이감(패럴랙스)·스켈레톤 스크린은 방향만 기록(마감 때)

**변경**
- `mockups/aa-prepare-arcs.html`(Claude 7단계 시안), `mockups/aa-prepare-arcs-codex.html`(Codex 비교안, 리뷰 8절이 참조)
- DESIGN.md: 2장 "화면 비유: 항성계"(+ Z축 깊이감), 1장 "[마감 때] 스켈레톤 스크린", 3장 "촬영 준비" 다시 씀(7단계·단계 끝 수동·극축 정렬 세부·캘리브레이션·정보는 한 곳에만·핵심 수치)
- flow.mmd ③ 준비 7단계. `docs/codex/REVIEW_CODEX.md` 7절·9절 처리 표(Codex가 추가한 8절 리뷰 본문 포함). `.gitignore`에 `.obsidian/`. 대화 기록 `conversations/2026-10-02-work-laptop.md`

**확인**: 시안을 헤드리스로 7단계·실패·대상 낮음·캘리브레이션 재사용·크게 보기까지 오류 없이 확인, flow.mmd 문법 검사. 앱 코드 변경 없음

**남은 것**: (집) 사용자가 최신 시안을 보고 정리해 올 피드백 반영 → 시안 확정 뒤 준비 화면 앱 코드 다시 만들기

---

## 2026-10-02 · 915e558 [Claude] 촬영 준비 시안 세 가지와 집 PC 논의 기록

**요청**: 오늘 집 PC에서 한 준비 화면 시안·논의를 커밋·push (회사에서 Codex 원호 시안 기준으로 이어서 작업 예정)

**변경**: 시안 `mockups/aa-prepare.html`(왼쪽 목록 + 작업 영역, 탈락), `aa-prepare-step.html`(한 화면 한 목표 1단계, 탈락), `aa-prepare-jarvis.html`(자비스 원 + 단계 원 움직이는 시안 — Codex 원호 시안의 출발점). `conversations/2026-10-01-home-pc.md` 7장: 준비 화면 방향 변화, 6단계로 합침(가이드 연결 → 극축 정렬 안), 완료 표시(체크 그리기 + 느린 빛), 준비 중단 위치, SharpCap·TPPA 조사, 할 일(낮음: SharpCap 값 가져오기, TPPA 속도 시험, 나중: HUD 마감)

**확인**: 시안·문서만. 앱 코드 변경 없음. 움직이는 시안은 테스트 창에서 애니메이션이 멈춰 직접 보지 못함. Codex가 만든 탈락 시안 두 개(`aa-prepare-asymmetric-codex.html`, `aa-prepare-jarvis-codex.html`)는 Codex 기록대로 커밋에서 제외(파일은 남김)

**남은 것**: Codex 원호 시안 기준으로 준비 화면 구현, 단계 안 세부 과정 표시 방식(사용자 검토 중)

---

## 2026-10-02 · 9904446 [Codex] 촬영 준비 원호 시안과 디자인 논의 기록

**요청**: 오늘의 유효한 마지막 시안과 대화 기록만 커밋·push. 탈락 시안과 Claude 작업은 제외.

**변경**: `mockups/aa-prepare-edge-arcs-codex.html` 추가. “촬영을 준비합니다” 원이 확대되어 왼쪽의 둥근 여섯 원호 조각으로 이어지며 현재 단계만 강조. 큰 원 윤곽과 안팎의 미세한 배경색 차이를 유지. `conversations/2026-10-02-codex-design.md`에 사용자 결정·탈락 이유·남은 논의 기록.

**확인**: 원호 시안의 첫 안내 화면은 브라우저에서 확인. 마지막 윤곽·배경색 변경은 JavaScript 문법과 파일 검토까지 확인했으며 시각 재확인은 못 함. 실제 앱 코드·DESIGN.md 변경과 장비 실행 없음.

**남은 것**: 단계 내 세부 과정의 수와 진행을 보여주는 방식은 사용자 검토 중. 목업은 최종 확정 또는 앱 적용 완료가 아님.

---

## 2026-10-01 · 4cc0ce7 [Codex] 준비 화면 UI 리뷰와 합의 기록

**요청**: 합의한 준비 화면 UI 리뷰를 기존 리뷰 파일에 추가하고 커밋·푸시.

**변경**: `docs/codex/REVIEW_CODEX.md`에 CX-PREP-UI-01~07 추가. 세로 진행 목록과 큰 작업 영역, 단계별 사진·그래프, 핵심 수치, 사용자 행동, 과거 결과 조회, 데이터·중단 동작, 대표 화면 목업 방향을 기록. 기존 계획 리뷰와 처리 표는 유지하고 새 항목의 구현·재리뷰 상태를 분리.

**확인**: 문서 변경 범위와 `git diff --check` 확인. 앱 코드 변경 없음; 실행·빌드·실장비 검증은 하지 않음.

**남은 것**: 합의한 UI 구현, 데이터 제공 범위와 중단 동작 구체화, 구현 후 Codex 재리뷰.

---

## 2026-10-01 · [Claude] 문서 폴더 정리, 준비 화면에 필요한 내용 정리

**요청**
- 저장소 맨 위 md 파일이 너무 많다 → 문서 관리 폴더와 위치 정리
- 준비 화면 구성을 다른 도구·GPT와 검토할 수 있게 필요한 내용을 단계별로 정리 ("○○ 중입니다"만 있어 피드백이 부족 → 실제 장비가 보는 것·줄어드는 숫자를 보여 주는 방향)

**변경**
- 맨 위에는 README·CLAUDE·AGENTS·DESIGN·HISTORY만. `docs/`(기획서, 구조와 온보딩, NINA 사전 준비, API 대응표), `docs/codex/`(REVIEW_CODEX.md, IMPLEMENTATION_REQUEST_CODEX.md)로 옮김
- 링크 고침: README, CLAUDE.md, 대화 기록, 문서 간 링크, 코드 주석(EquipmentConnector, SetupChecker). REVIEW_CODEX.md는 "근거 문서" 링크 경로만, 처리 표 해시(c95e24d)
- CLAUDE.md·AGENTS.md에 "문서 위치" 절
- `docs/PREPARE_SCREEN_CONTENT.md`(임시): 단계별 실시간 피드백·목표 숫자·결과 한 줄·버튼·데이터 출처. 화면 구성이 정해지면 DESIGN.md에 합치고 지움

**확인**: 문서·링크만 변경 (옮긴 파일을 가리키는 경로를 저장소 전체에서 찾아 고침). HISTORY.md 과거 항목의 옛 파일 이름은 기록이라 그대로

**남은 것**: 준비 화면 구성 검토(사용자) 뒤 화면 다시 만들기

---

## 2026-10-01 · c95e24d [Claude] 계획 확정 구조, 촬영 준비 화면 골격(모의), 시퀀스 재개 확인, 버전 표시, 장비 바뀐 계획 갱신

**요청**
- Codex 리뷰(REVIEW_CODEX.md) 검토 후 결정: CX-PLAN-06 지금 고치기, 01 미리보기 유지, 진행 순서 06 → 시퀀스 실험 → 계획 전달 구조 → 준비
- Codex 요청서(IMPLEMENTATION_REQUEST_CODEX.md) 검토 후 결정: 준비는 단계별 API 호출, 극축정렬 생략 안 함, 이동 승인 한 번, 대상이 30° 아래면 알리고 멈춤, 시험 사진 항상, 가이딩은 픽셀 기준 규칙 판정 + (온라인이면) AI 설명 + 사용자 확정, SharpCap 설치 확인은 V0.2로
- 부팅 화면·상태 줄에 빌드 버전 표시. 장비 없이 안 되는 것은 성공 가정으로 진행

**변경**
- CX-PLAN-06: `PlanAssistant.EnsureStartedAsync`가 같은 밤 장비 변경을 감지 → `ApplyRig`(화각 재계산, 노출에 영향 주는 장비면 촬영 설정 칸 비움 + 안내), `ProfileSelected`(다른 프로필이면 새로)
- W2 계획 확정: `Plan.cs` — `PlanTarget` J2000 좌표, `PlanFraming.RotationDegrees` null = 방향 유지(CX-PLAN-02), `EndRule`(CX-PLAN-04), `PlanAfter` 구조화, `PreparationPlan.From`(실행 가능 여부 확인) + `POST /api/plan/confirm`·`GET /confirmed`. 30° 위로 안 오는 대상은 예상 0장·촬영 띠 없음. 첫 인사 문장, AI 지시(정해 온 대상 바로 받기, 회전은 말할 때만)
- W3 준비: `Prepare/PrepareRunner.cs`(7칸 상태 기계, SSE `/api/prepare/watch`, `/act`), `Prepare/PrepareDevices.cs`(`IPrepareDevices` + `SimulatedPrepareDevices`, [임시] fail-next·고도 무시), `PrepareScreen.tsx`(쉐브론 7칸 + 공통 영역), App에 촬영 자리(빈 화면)
- 버전: `product.json` version + 커밋 수 → `vite.config.ts` `__VERSION__`, `StatusBar`·`BootScreen`
- 문서: DESIGN.md(계획 화면 규칙, "촬영 준비" 절), flow.mmd ③ 7단계, api-coverage.md(재개 확인), README PowerShell 예시, REVIEW_CODEX.md 처리 표(01~07), 요청서 파일 함께 커밋

**확인**
- 시퀀스 재개 실험 (이 노트북 N.I.N.A., 실험용 프로필 AstroAssistant + 시뮬레이터 카메라): stop → start가 끝난 항목은 건너뛰고 완료 장수 유지(5장 중 3장 뒤 중지 → 다시 시작 → 파일 정확히 5장)
- 연습 대화 API: 망원경 바꾸면 화각 5.17° → 1.5°·채움 61% → 211%·설정 칸 비움, 같은 프로필 재선택 유지, 다른 프로필 새로. M31 확정 값, NGC 104 확정 거부·0장
- 헤드리스(1920×1200): 준비 7칸을 극축정렬 → 대상 낮음 멈춤 → 이동 → 초점 실패·재시도 → 가이딩 판정 → 시험 사진 → 촬영 시작까지. 버전 v0.0.26 표시
- 실제 AI 호출·실장비 없음. 실제 앱 창 확인은 사용자가 쉐브론 화면까지 봄. 흐름도 검사 때 실수로 Edge 헤드리스가 한 번 실행됨(스크립트 고침)

**남은 것**: 준비 화면을 세로 진행 목록 + 큰 작업 영역으로 바꾸기(사용자 확인, 쉐브론 반복이 식상), W4(SharpCap·PHD2 실제 연결) ~ W7(촬영·종료), 시험 사진 실제 표시, 가이딩 AI 설명, N.I.N.A. 이동 명령이 J2000을 받는지 시뮬레이터 적도의로 확인

---

## 2026-10-01 · 86afaff [Claude] 장비 연결 화면 다듬기, 경통 → 망원경, 리프트 공통 규칙

**요청**
- 장비 연결: 진행 막대는 모두 연결됐을 때만(변경 모드에서는 숨김), 멈춤 대신 "변경" 버튼을 가리키는 동안 0.3배속, 다시 연결 아이콘 두 배, 허브 문구 줄바꿈, 망원경 원은 이름만, 등록 안 된 작은 원에 종류 이름표 + 떠오름, 연결된 원도 떠오름
- 화면 문구 "경통"을 모두 "망원경"으로. 떠오름(lift)을 공통 규칙으로 하되 나중의 앱 전체 공간감 작업과 부딪히지 않게
- 규칙: 수정을 시작하면 실행 중인 AA를 먼저 종료

**변경**
- `EquipmentScreen.tsx`: 진행 막대를 CSS 애니메이션 → `requestAnimationFrame`으로 직접 그림(`elapsed`·`slow`·`SLOW_RATE`), `slowProps`를 "장비 변경"(감싼 `slowZone`)·관측지 "변경"에. 작은 원 `nodeTip`, `hoverBody[data-without]`
- `EquipmentScreen.module.css`: `.progress[data-shown]`, `.retryIcon` 크기, 연결된 원·작은 원 hover lift, `.nodeTip`
- `index.css`: 공통 토큰 `--lift`·`--lift-ms`, 테마별 `--lift-shadow`
- `EquipmentConnector.cs`: 슬롯 이름 "망원경", 칸 글씨는 이름만. 그 밖의 "경통" 문구·주석을 "망원경"으로 (ScopeList·PreflightScreen·OpticsStore·ScriptedChatModel·flow.mmd 등)
- DESIGN.md 3장(장비 연결·관측지 막대·망원경), 6장 "업계 용어보다 일상어", 9장 lift 공통 규칙. CLAUDE.md "실행 중인 앱"

**확인**
- 헤드리스 1920×1200(연습 대화·시뮬레이션): 연결 중 막대 숨김, 모두 연결 뒤 600ms에 15%·"장비 변경" 가리키면 4.6%(≈0.3배), 연결된 원·작은 원 lift(transform 확인), 작은 원 이름표 "필터휠", 실패 시 아이콘 99~112px로 원 안에 들어감, 허브 문구 두 줄, 점검 화면 "망원경 캡"·"망원경 밸런스" 안 잘림, 전 화면 스크롤 없음
- 실제 앱 창에서는 아직 못 봄

**남은 것**: 앱 전체 공간감(z축) 검토 때 lift를 그 체계로 흡수

---

## 2026-10-01 · ad79424 [Claude] 리뷰 처리 결과 기록 규칙

**요청**
- Codex 리뷰를 반영한 결과를 REVIEW_CODEX.md에도 남길지 → 짧게 남기기로

**변경**
- `CLAUDE.md` 리뷰 규칙: REVIEW_CODEX.md는 "처리 및 재리뷰 기록" 표만 한 줄씩(상태·근거·커밋 해시), 자세한 변경은 HISTORY.md에. Codex 본문·재리뷰 칸은 고치지 않음

**확인**: 문서만 변경

---

## 2026-10-01 · 9000332 [Claude] 기준 화면 16:10 (1920×1200), 달 이름표 잘림 수정, 리뷰 규칙

**요청**
- 현장 노트북 3840×2400을 배율 200%로 쓰기로 함 → 16:10에서 최적, 다른 비율에서도 깨지지 않게
- HISTORY.md 작성자 표시(`[Claude]`/`[Codex]`), Claude 구현 → Codex 리뷰(REVIEW_CODEX.md) → 사용자와 논의 후 반영하는 절차를 규칙으로

**변경**
- `frame.ts` `FRAME_H` 1080 → 1200, `App.module.css` `.shell` 높이 1200px. 나머지는 이미 `cqw`·`cqh`라 그대로
- `NightChart.tsx`: "달 ○○ 짐"이 그래프 오른쪽 밖으로 나가면 막대 왼쪽 "달 ○○ 뜸"으로
- DESIGN.md 1장 "기준 화면" 값·예시, `CLAUDE.md` 커밋 규칙·리뷰 규칙, 대화 기록 `conversations/2026-10-01-work-laptop.md`(집 PC 기록의 1920×1080 문구에 정정 줄)

**확인**
- 헤드리스로 프로필 → 점검 → 장비 → 출발 전 점검 → 계획 순회, 모두 스크롤 없음: 1920×1200·1536×960·1280×800 여백 없음, 1920×1080 좌우 96px, 3440×1440 좌우 568px
- 달 이름표는 1920×1200 계획 화면 스크린샷으로 확인. 실제 앱(AA.exe)·실제 노트북 200%에서는 아직 못 봄

**남은 것**: 16:10에서 0단계 점검 화면 패널 아래 빈 자리 처리(사용자와 상의), REVIEW_CODEX.md CX-PLAN-01~07 검토

---

## 2026-10-01 · c2f3b47 [Codex] 촬영 계획 설계 리뷰 기록

**요청**
- 다른 AI의 리뷰와 구분할 `REVIEW_CODEX.md`에 계획 단계 검토 내용을 남기고, 작성자 라벨이 있는 히스토리와 함께 커밋

**변경**
- `REVIEW_CODEX.md`: 계획 단계의 설계 검토 7항목, 정책 미결정 사항, Claude 처리 결과와 Codex 재리뷰 기록표
- 문서 작성 승인과 제안의 구현 승인을 구분하고, 시뮬레이션 개발 범위와 실기 검증 과제를 명시

**확인**
- Codex가 문서 및 작성 대화 기록을 확인하고 커밋 범위를 검토. 설계 검토는 정적 확인이며 앱 빌드·AI 호출·장비 제어는 수행하지 않음

**남은 것**: Claude의 항목별 재검토, 사용자 정책 결정, 반영 후 Codex 재리뷰

---

## 2026-10-01 · 28a5c68 Codex 촬영 계획 상담 기록

**요청**
- Claude의 작업·커밋·push가 끝난 뒤, 오늘 Codex와 논의한 내용을 기존 작업과 섞이지 않게 문서로 기록하고 커밋·push

**변경**
- `conversations/2026-10-01-codex-planning.md`: 장비별 질문, 로테이터 없는 구도 변경, 촬영 시간·종료 정책, 예상 화각에 필요한 광학·센서 정보, API 전달 구조와 Git 상담 요약
- 사용자 의도·이해 확인과 Codex의 미확정 제안을 구분. 같은 날 Claude가 구현한 AA 경통 목록과의 관계를 명시
- 코드·DESIGN.md·기존 Claude 대화 기록은 변경하지 않음

**확인**
- 작업 전 로컬 main과 GitHub main의 `684f431` 일치 및 깨끗한 작업 트리 확인
- 문서 내용과 변경 범위를 검토. 문서만 변경하여 앱 빌드·실장비 검증은 수행하지 않음

**남은 것**: 제안의 채택 범위와 구체적 정책은 후속 논의에서 결정, 실제 구현 상태는 구현 작업 시 확인

---

## 2026-10-01 · 684f431 기준 화면 1920×1080, 관측지·경통, 전환 중 조작 막기, 앱 아이콘

**요청** (집 PC)
- 사용자 4K 노트북(배율 200%) 기준으로 화면을 만들고 다른 해상도는 통째로 확대·축소, 울트라와이드는 좌우 여백
- 관측지: 프로필별 목록, 장비 연결 뒤 확인·변경, 관측지 고르기 화면(카카오맵, 두 번 눌러 설정, 이름만 수정), 상태 줄 관측지, 프로필 사진으로 프로필 전환, 편집은 시작 화면에서
- 경통: 연결 장비가 아니라 NINA가 모르는 광학 정보 → AA 경통 목록(수동 입력), 장비 연결 화면의 "경통" 원
- 화면 전환 중 클릭 막기(전체 규칙), 관측지 화면 진입 효과(카드 뒤집기·빛 효과), 1단계 → 장비 연결 원 크로스페이드, 앱 아이콘

**변경**
- `frame.ts`·`App.module.css` `.shell`: 1920×1080 틀 + scale, 크기 컨테이너. CSS의 `vw`/`vh` → `cqw`/`cqh`. `scaleOf`로 화면 좌표 보정. 장비 그래프 상한 1100×680 제거(높이 기준, 가로 1.6배까지)
- 서버: `ProfileStore`(Sites, Edit/AddSite/RenameSite/RemoveSite), `SiteService`(NINA 위치 읽기·쓰기 SSE, Open-Meteo 고도), `OpticsStore`(optics.json), `EquipmentConnector` 경통 슬롯(NINA에 초점거리·F값·이름 씀), `RigSetup` 경통 고르기, `TonightService` 경통 우선, API `/site/*`, `/profiles/{id}/sites`, `/optics`, `/config`(카카오 키)
- 화면: `SiteScreen`, `SiteMenu`, `KakaoMap`(틀 배율 되돌려 1:1), `UndoToast`, `ScopeList`, `StatusBar`(관측지·프로필 전환), `ProfileScreen`·`NewProfileScreen` 편집, `EquipmentScreen`(관측지 문장·가리키면 멈춤·버튼을 원 안으로·경통 원·크로스페이드), `JarvisRing` action, `screenReady.ts` + App `settled`(`inert`)
- `app.ico`(16~256px)·`favicon.png`, DESIGN.md(1·3·9·10장), README(카카오 키), flow.mmd(관측지 단계), 시안 `mockups/aa-sites.html`

**확인**
- 테스트 서버(임시 데이터): 관측지 없음 → 막대 → 관측지 고르기, 카카오 장소 검색·고도 자동·추가·저장 → NINA에 위도·경도·고도 저장 확인 후 0으로 되돌림, 상태 줄 목록·관측지 편집 이동
- 경통 API: 추가·배율 계산·지금 경통 삭제 거부·검증 문구, 연결 확인 시 NINA 초점거리 260·F3.8 저장 (사용자 NINA에 그대로 둠)
- 3440×1440·1920×1080·1280×720 틀 크기·배율 수치 확인. 사용자가 실제 앱에서 울트라와이드·관측지 흐름 확인
- 확인 못 함: 애니메이션(카드 뒤집기·빛·크로스페이드)과 경통 목록 모양은 테스트 창에서 멈춰 사용자 확인 필요. 실제 모드 적도의 위치 보내기 실기 미검증

**남은 것**: 모핑 효과(대화 기록 6장), ③ 준비 단계, 실기 확인

---

## 2026-10-01 · fcb0935 울트라와이드 화면 대응과 장비 연결 문장 사라짐 수정

**요청** (집 PC, 3440×1440 울트라와이드·배율 100%에서 처음 실행)
- 새 프로필 화면 요소가 세로로 벌어짐, 설치 확인에서 선택된 쉐브론이 옆 칸을 덮음, 장비 변경 모드 원들이 같은 x에 몰림
- 장비 연결에서 다시 연결이 실패하면 가운데 원 문장이 사라짐
- 울트라와이드에서는 억지로 늘리지 말고 표준 비율만큼 가운데에, 좌우는 여백으로 (사용자 결정)

**변경**
- `App.module.css` `.shell`: 최대 너비 = 화면 높이 × 16/9, 가운데 정렬. DESIGN.md 1장에 규칙
- `NewProfileScreen.module.css`: `align-content`로 위아래 가운데 (줄이 늘어나지 않게)
- `StepChevrons.module.css`: 칸 너비 상한 480px(확대 10% − 칸 사이 8px ≤ 화살표 홈 42px), 띠 가운데. `CheckStepsScreen.module.css`: 제목·아래 영역도 같은 최대 너비
- `EquipmentScreen.tsx` 변경 모드 가로 자리: 기준 너비를 min(창 너비, 그래프 너비 ÷ 0.8)로. 그래프(최대 1100px) 밖으로 나가 양 끝에 몰리던 문제
- `JarvisRing.tsx` `useSmoothContent`: 바뀌려던 글자가 원래 글자로 돌아오면 다시 밝게 (흐려진 채 남던 버그)

**확인**
- 임시 데이터 폴더·별도 서버(5211)로 3440×1440 화면 확인: 앱 영역 2560px 가운데, 새 프로필 모임, 쉐브론 확대 칸과 옆 칸 사이 약 3px, 카메라 다시 연결 실패 뒤 문장 유지
- 변경 모드 배치는 테스트 브라우저에서 펼침 애니메이션이 멈춰 좌표 계산으로만 확인 → 사용자가 실제 앱에서 확인함
- 사용자가 실제 데스크톱 앱에서 네 가지 모두 확인

**남은 것**
- 회사 노트북 창이 1375px보다 넓으면 변경 모드 원 간격이 이전보다 조금 좁아질 수 있음 → 노트북에서 확인
- 로컬 폴더 이름을 `AstrophotoAssistant`로 바꿈(집 PC). 코드 영향 없음

---

## 2026-09-30 · f75b786 Codex 리뷰 지침(AGENTS.md) 추가

**요청**
- GPT Codex가 리뷰 전에 HISTORY.md를 먼저 읽도록 사용자가 준 지침을 Codex가 `AGENTS.md`로 저장 → 다른 PC의 Codex도 같은 지침을 쓰게 저장소에 올리기

**변경**
- `AGENTS.md` 추가 (Codex 작성, 내용 그대로): 리뷰 시 HISTORY.md 먼저, 기록은 맥락·검증은 실제 코드와 diff, 시뮬레이션·미구현·실기 미검증은 결함으로 보지 않음, 리뷰만 요청받으면 수정하지 않음
- 앞 항목 제목에 커밋 해시 채움

**확인**
- 내용 검토: 개인 정보·키 없음, CLAUDE.md 지침과 충돌 없음. 코드 변경 없음

---

## 2026-09-30 · 6e747bc 0단계 전환 다듬기, N.I.N.A. 감시, 장비 변경 실패 처리

**요청**
- 0단계: 다시 확인(새로고침) 때 화면이 번쩍이며 갱신되지 않게, 새로고침 아이콘을 통과 체크 자리·크기로, "다음" 누르면 쉐브론이 왼쪽부터 차례로 사라지게
- N.I.N.A.가 실행 중 꺼지면 처리 (로컬 폴링 부담 질문 → 프로세스 종료 알림 + 느린 응답 확인으로)
- 커밋마다 요청·변경을 누적하는 기록 파일 (GPT Codex 리뷰용)
- Gemini 리뷰 반영: 실제 모드에서 장비 바꾸기·제거가 실패해도 선택이 저장되고, 연결 확인이 어떤 장비인지 보지 않아 "이름 B, 실제 A"가 연결 완료로 보이는 문제

**변경**
- `checks.ts` `recheckOne(quiet)`: 확인 중 표시 없이, 결과가 같으면 항목을 건드리지 않음. `CheckStepsScreen`: 재확인 중 아이콘만 회전(최소 0.7초), "다음" → 완료 문장·버튼 즉시 숨김 + 쉐브론 0.11초 간격 페이드 → 다음 단계. `StepChevrons`: 새로고침을 체크와 같은 그리드 칸에 겹쳐 배치. 1단계 화면 페이드인
- `NinaWatcher`(서버): 1단계 연결 뒤 `Process.Exited`로 종료 즉시 감지 + 10초마다 `/version`, 3회 무응답 = 멈춤. `GET /api/nina/watch`(SSE), `[임시] POST /api/nina/simulate-exit`
- 화면: 장비 연결 이후 단계에서 끊기면 `NinaLostCard`(모달 아님) + 상태 줄 장비 점 끔 → "다시 켜기" → 엔진 켜기 → 장비 연결 → 원래 화면으로 복귀(점검 생략, 계획 유지)
- `RigSetup.SelectAsync`(실제 모드): 바꾸기 = 지금 장비 끊기 → 새 장비 연결 → 확인될 때만 저장, 실패 시 원래 장비 재연결 + 오류 문장. 제거 = 해제 확인될 때만. 반환 `{ item, error }`
- `LiveDevices`: AA가 연결을 확인한 장비 Id. `EquipmentConnector` 연결 확인은 이 기록(또는 AA 선택이 없어 프로필 그대로인 경우)만 "이미 연결됨"으로 믿고, 아니면 끊고 새로 연결
- 변경 모드 원 아래 실패 문장 표시. DESIGN.md·onboarding 6장(실기 확인 항목)·대화 기록 갱신
- `HISTORY.md` 신설, CLAUDE.md에 "커밋 규칙"(커밋마다 이 파일에 항목 추가), README에서 연결

**확인**
- 헤드리스(시뮬레이션): 재확인 중 아래 영역 변화 없음, 아이콘 위치 = 체크 위치(55px), 쉐브론 순차 페이드, N.I.N.A. 종료 → 카드 12ms → 다시 켜기 → 계획 화면 복귀
- 모의 N.I.N.A.(가짜 HTTP) 12개 경우: 바꾸기 실패/성공, 재시작 뒤 다른 장비가 연결돼 있는 경우, 제거 실패/성공 모두 통과. 장비 변경 모드 흐름 회귀 통과

**남은 것**
- 실기 확인: `connect?to=`로 프로필 저장되는지, `profile/change-value` settingpath, `equipment/{종류}/info`에 장비 이름·Id가 나오는지(나오면 동일 장비 비교로 강화)
- 촬영 중 N.I.N.A. 복구, Advanced API WebSocket 이벤트 — 촬영 화면 만들 때

---

## 2026-09-30 · b11226d Planning screen with AI, equipment edit mode, step rail, preflight checklist

**요청** (집 PC 시안을 앱으로 + 장비 연결 화면 여러 차례 수정)
- 하단 단계 레일, 1단계 인터넷 확인, 출발 전 점검, 촬영 계획 화면(실제 AI, 나중에 다른 AI로 바꿀 수 있게)
- AI 한도가 없을 때도 시연 가능하게, 장비 등급(필수/준필수/선택/제외), AA 안에서 장비 등록·변경·제거
- 장비 연결 화면 다듬기(배치·크기·애니메이션·상태 표현 등), 전체화면 시작, 앱 끄기 버튼

**변경**
- `StepRail`, `InternetCheck`, `NinaApiClient` 요청별 대기 시간(조회 5초 / 연결 60초), `PreflightScreen`
- 계획: `Astro.Core/Sky`(천문 계산), `DsoCatalog`(NINA.sqlite), `TonightService`(프로필·Open-Meteo), `Astro.Core/Assistant`(회사 무관 대화 형식) + `GeminiChatModel` + `ScriptedChatModel`(연습 대화, 한도·키 문제 시 자동 전환), `PlanTools`/`PlanAssistant`, `PlanScreen`/`NightChart`. 키는 사용자 비밀 저장소·환경 변수만
- 장비: 등급·`Absent` 상태, "없이 진행", 장비 변경 모드(`RigSetup`, `rig-overrides.json`, list-devices), 4초 자동 진행 막대, 흡수·펼침, 균형 무작위 배치(앱 재시작까지 고정), 허브 깜빡임, 상호작용 상태 네 가지
- 데스크톱: 전체화면(F11), 단일 실행

**확인**: 헤드리스 흐름 테스트(시뮬레이션·연습 대화), 천문 계산은 서울 기준 공개 시각과 대조, Gemini 실제 대화 확인(이 과정에서 무료 한도 일부 소진 — 이후 AI 테스트는 사전 확인)

**남은 것**: 계획 화면 검토, ③ 준비 단계, 실기 테스트 목록(onboarding 6장)

---

이전 커밋(2026-09-29 이전)은 이 파일이 생기기 전이라 `git log`와 [conversations/](conversations/)를 본다.
