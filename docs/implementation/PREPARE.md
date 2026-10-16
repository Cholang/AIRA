# 준비 구현 — 장비 준비 · 대상 · 마무리 (서버 러너와 작업)

**지금 코드를 설명하는 문서**다(2026-10-12 정리 — 그 전엔 2026-10-05의 "구현 계획 · 착수 전" 문서였다). 코드를 바꾸면 이 문서도 함께 고친다.

| 무엇을 보면 되나 | 문서 |
|---|---|
| 아이라가 **하는 일**(순서·실패 처리·사용자 결정) | [OPERATION_PROCEDURE.md](../OPERATION_PROCEDURE.md) 5장 ①~⑦, 7장 "마무리" |
| 화면이 **어떻게 보이는지** | [DESIGN.md](../../DESIGN.md) "하늘 화면" |
| **어떻게 만들었나**(구조·계약·장비 어댑터·실패 처리 순서) | 이 문서 |
| 촬영 단계의 구조 | [docs/implementation/SHOOT.md](SHOOT.md) |
| 쓰는 N.I.N.A. API | [api-coverage.md](../reference/NINA_API_COVERAGE.md) |
| 바뀐 이력 | [HISTORY.md](../../HISTORY.md), 결정 이력은 [DECISION_HISTORY.md](../DECISION_HISTORY.md) |

## 0. 원칙

- **서버가 상태를 갖고 화면은 그리기만** 한다. 화면을 다시 열어도 이어서.
- **장비 동작은 작업마다 좁은 장비 인터페이스 뒤에**. 작업은 모의인지 실제인지 모른다.
- **AI는 판정 고리 밖**: 등급·재시도·멈춤은 규칙(코드)이 정하고, AI는 설명 문구만.
- **실패는 아이라가 먼저 처리**: 정해진 순서로 다시 시도하거나 대안으로 바꾸고, 끝내 안 될 때만 원인과 할 일을 한 줄로.
- **작업 끝은 사용자 버튼**, 끝 버튼이 다음 작업 시작(이동 승인도 이 버튼).
- v1은 사용자 장비 기준: Proxisky UMi17X(OnStepX) · ToupTek G3M662M(가이드) · Fujifilm X-T5(주) · Pleiades68(260mm F3.8) · Oasis Focuser · WandererBox Plus V3 · N.I.N.A. 3.2 · PHD2 · SharpCap 4.1.

## 1. 구조

```
src/Astro.Server/Prepare/
  Flow/      PrepareFlow     — 러너 셋(장비 준비·대상·마무리) + 그날 밤 결과 + 묶음 사이 멈춤·넘김
             PrepareRunner   — 순서·끝 버튼·다시 하기 의존표·RunId·끝 상태 확인·정지 확인·SSE. 작업별 로직 없음
             PrepTask.cs     — 작업 계약(IPrepTask·ITaskRun·PrepContext·MountLock·IPrepMemory)
             PrepResults.cs  — 결과 기록(작업 사이는 이것만으로)
             PrepView.cs     — 화면에 보내는 상태 모델
  Tasks/<작업>/  작업 클래스 + 그 작업의 장비 인터페이스 + 모의 장비 (Polar·Calibration·Focus·Slew·Center·FocusCheck·Guiding·TestShot·Wrap)
  Real/      실제 장비 — 공통 어댑터(NinaRig·Phd2Client·SharpCapBridge·AscomAxis·OasisSdk·LiveImages·Fits·Png·Phd2Windows·AscomWeather)
             + 작업별 Real*Devices(공통 어댑터를 조합)
  Sim/       모의 공통(SimOptions 속도·SimFaults 실패 걸기)
  PrepareSetup.cs — 등록(Equipment:Simulate면 모의, 아니면 Real), PrepareStarter(묶음 시작·이어서 하기)
  MountHome.cs · FocuserPark.cs — 적도의 홈 버튼, 끝낼 때 포커서 0
web/src/  screens/PrepareScreen.tsx · components/PrepGuide·PrepCenter·LiveView·WindowView·StepRail · prepare.ts
```

### 1.1 묶음과 작업 (`PrepareFlow`, `RunnerSetup`)

| 묶음 | 작업(차례) | 끝 버튼 | 계획 |
|---|---|---|---|
| **장비 준비** `rig` | polar 극축 정렬 → calibration 캘리브레이션 → focus 초점 | "대상 고르기" | 없이 시작(그날 밤 유지) |
| **대상** `target` | slew 이동 → center 센터링(탐색) → focuscheck 초점 확인("이 대상으로 확정") → guiding 가이딩 → test 시험 사진 | "촬영 시작" | 확정 계획 하나 |
| **마무리** `wrap` | flat 플랫 → dark 다크 → pack 장비 정리 | 끝 버튼 없이 요약으로 | 없이 |

- 러너 코드는 같고 작업 목록·의존표만 다르다. 작업을 더하거나 순서를 바꾸면 등록(`PrepareSetup`)과 의존표만 고친다.
- **묶음 안 의존표**(다시 하면 "다시 확인 필요"가 되는 뒤 작업): polar → calibration·focus / slew → 대상 묶음 나머지 전부 / center → focuscheck·guiding·test / focuscheck → test / guiding → test.
- **묶음 사이**(`PrepareFlow.CrossDependents`): polar·calibration을 다시 하면 대상 묶음 전부, focus를 다시 하면 focuscheck·test. 장비 준비를 다시 시작하면 대상 묶음을 먼저 멈춘다(넘김 `handoff: "rig"`). 대상을 바꾸면 계속 도는 가이딩을 먼저 멈춘다.
- 장비 준비가 끝나면 계획으로 가고 그동안 적도의는 홈에서 추적을 끄고 기다린다(`/api/prepare/rig/rest`).

### 1.2 작업 계약 (`IPrepTask`)

- `Id` · `Title` · `StartLabel`(앞 작업의 끝 버튼 이름) · `StartAlternatives`(끝 버튼 옆 다른 선택, 예: "가이딩 없이 진행") · `SubSteps`(세부 과정).
- `AppliesTo(ctx)` — 이번 준비에서 할 작업인가(예: 가이더 없음 → 캘리브레이션·가이딩 건너뜀).
- `RunAsync(ITaskRun, ct)` → `Completed`(결과 기록 + 결과 말 + 보조 버튼, `AutoNext`면 묻지 않고 다음) 또는 `RedoRequest`(다른 작업부터 다시 — 러너가 의존표대로).
- `CheckEndStateAsync` — **끝 상태 약속**을 실제 장비에서 읽어 확인(아래 1.3). `CheckResumeAsync` — 다시 켜 이어서 할 때 건너뛰어도 되는가(기본은 끝 상태 그대로).
- `StopAsync` — 움직이던 장비를 멈추고 **멈춘 것을 확인**(못 하면 false). `KeepsRunning` — 끝난 뒤에도 장비가 도는 작업(가이딩).
- 작업은 화면 갱신을 `ITaskRun`으로만 한다: `SubStep` · `Guide`(제목·안내) · `Readout`(측정값: 종류·큰 값·설명·판정 색·값들·실시간·미확인·측정 시각) · `Status` · `Live`(하늘 화면 종류·이미지 주소·데이터) · `AskAsync`(버튼을 내고 기다림). **문장은 서버가 만든다**(화면에 문장 조립 없음).
- 작업끼리 서로 부르지 않는다. 앞 작업 결과는 클래스가 아니라 **결과 기록**(`PrepResults`)으로만 읽는다: `PolarResult` · `CalibrationResult` · `FocusResult` · `SlewResult` · `CenterResult`(오차·카메라 방향·HFR·별 수) · `FocusCheckResult` · `GuidingResult` · `GuiderSkipped` · `TestShotResult`. 기록 모양을 바꾸지 않는 한 작업 안을 고쳐도 다른 작업은 영향이 없다.

### 1.3 끝 상태 약속 (러너가 다음 작업 전에 확인)

장비는 모든 작업이 같이 쓰므로, 작업마다 끝날 때 장비가 있어야 할 상태를 실제로 읽어 확인한다. 어긋나면 그 작업 끝에서 바로 알리고("○○이 끝났는데 추적이 꺼져 있습니다") 다음으로 넘기지 않는다.

| 작업 | 약속 |
|---|---|
| 극축 정렬 | SharpCap 닫힘 · 가이드 카메라가 PHD2에 연결·사진 안 찍음 · N.I.N.A.–PHD2 연결 · 적도의 멈춤 |
| 캘리브레이션 | PHD2 보정값 있음 · 가이딩·루프 안 함 · 적도의 멈춤 |
| 초점 | 포커서 멈춤 · 가장 좋은 위치 · 멈춤 감지 오류 없음(손으로 맞춤이면 위치는 보지 않고 멈춤만) |
| 이동 | 적도의 멈춤 · 추적 켜짐 |
| 센터링 | 적도의 멈춤 · 추적 켜짐 · 카메라 노출 중 아님 · 목표에서 벗어나지 않음 |
| 초점 확인 | 포커서 멈춤 · 새로 맞췄으면 그 위치 |
| 가이딩 | PHD2 가이딩 중 · 안정 · 방금 별을 잃지 않음 |
| 시험 사진 | 카메라 노출 중 아님 · 가이딩 유지 · 사진 저장됨 |

### 1.4 공통 규칙 (러너·공통 어댑터에 한 번만 — 작업은 쓰기만)

- **실행 번호 `RunId`**(Codex CX-PREP-IMPL-04): 작업을 (다시) 시작할 때마다 새로 매기고, 지난 실행에서 늦게 온 갱신은 버린다. 측정값에는 측정 시각과 상태(fresh · stale · unverified)를 붙여 끊긴 값을 정상처럼 보이지 않게 한다. 애니메이션이 끝난 것을 장비 작업 완료의 근거로 쓰지 않는다.
- **멈춤 확인**(IMPL-01): 움직이는 동작(RA 회전·위치 이동·대상 이동·포커서 이동)은 취소·시간 초과·예외 때 정지 명령을 보내고, **정지를 확인하기 전엔 다음 명령을 보내지 않는다**. 정지 명령은 일반 대기열을 거치지 않고, 취소된 작업의 토큰과 무관하게 보낸다. 확인하지 못하면 "중단 완료"가 아니라 **"장비 상태 다시 확인"**(정지 확인 한도 15초).
- **적도의 잠금**(IMPL-02, `MountLock`): 적도의를 움직이는 동작(N.I.N.A. slew·home, ASCOM `MoveAxis`, PHD2 캘리브레이션·가이딩 시작)은 잠금을 잡은 작업만. 끝나면 정지·추적 상태를 확인하고 푼다. 정지를 확인하지 못한 채 남으면 `MountLock.Unconfirmed` — 사용자가 "멈춘 것을 확인했어요"를 누를 때까지 적도의를 쓰는 모든 작업이 거부한다(`MountUnconfirmedException`, CX-30-02). 가이딩 중 PHD2의 보정 펄스는 PHD2 몫. 사용자가 다른 앱에서 직접 조작하는 것까지 막는다고 가정하지 않는다.
- **다시 하기 순서**(IMPL-05): 진행 중 작업 중단 → 정지 확인 → 이전 실행 결과 차단(RunId) → 영향받는 결과 무효화 → 재실행. 앞 실행이 아직 정리 중이면 새 실행을 시작하지 않고 "확인 필요"(CX-30-03). 버튼(다음·다시 시도·다시 하기)은 받는 즉시 지워 두 번 눌러도 한 번만.
- **화면 재접속 ≠ 서버 재시작**: 화면을 다시 열면 그대로 이어서. 아이라를 다시 켜 이어서 하면(`PrepareStarter.StopForResumeAsync`) 먼저 노출·포커서·적도의를 멈추고 확인한 뒤, 끝낸 작업은 `CheckResumeAsync`로 장비가 그대로인지 보고 건너뛴다.
- **[임시] 건너뛰기**(`TempSkipAsync(success)`, 모든 작업): 진행 중인 작업은 멈추고(정지 확인) 넘기고, 끝났으면 다음 작업을 시작하지 않고 넘긴다. 성공 = 더미 성공 결과, 실패 = 그 작업이 실패해 건너뛸 때와 같은 결과(캘리브레이션·가이딩은 `GuiderSkipped`). 포커서 위치처럼 장비를 움직일 값은 넣지 않는다. 초점 확인은 성공만. 촬영(`ShootSession.TempSkipAsync`)·계획(`PlanAssistant.TempSkip`)도 같은 버튼. 지울 때 `/temp-skip` 세 곳·`PrepView.TempSkip`·웹 `TempSkip`을 함께.

### 1.5 화면에 보내는 것 (`PrepView`)과 API

- 상태: 작업 목록(상태·결과 한 줄) · 지금 작업(세부 과정·안내·측정값·상태·버튼·`Live`) · 실행 번호. SSE `/api/prepare/{rig|target|wrap}/watch`, 한 번 읽기 `/state`.
- 버튼 `/act`(next · retry · redo:작업 · next:선택 · device-check · 작업별 답), 중단 `/abort`, 시작 `/rig/start` · `/target/start` · `/wrap/start`, 장비 준비 뒤 쉬기 `/rig/rest`, [임시] `/temp-skip`, 아이라 종료 전 정지 `/prepare/stop-for-quit`, 점검 뒤 포커서 0점 요청 `/prepare/focuser-zero`.
- 이미지 `/api/prepare/live/{kind}`(SharpCap 저장 영상·가이드 사진·N.I.N.A. 마지막 사진 — 같은 내용이면 ETag로 304). SharpCap 스크립트 보고 `/api/prepare/sharpcap/report`(localhost만).
- 모의 전용: `/sim/fail-next/{key}`(다음 동작 하나 실패, 키 목록 `SimFaults.Known`), `/sim/ignore-altitude`.

## 2. 실제 장비 어댑터 (`Real/`)

| 어댑터 | 경로 | 하는 일 |
|---|---|---|
| `NinaRig` | N.I.N.A. Advanced API :1888 (`NinaApiClient`) | slew(**RA는 도 단위**)·멈춤 확인·홈·추적, 포커서 이동·자동초점·상태, 카메라 촬영·정보·마지막 사진 통계, 솔빙·센터링, 가이더, 동기화, 이슬 열선(허브 스위치). 노출 멈춤 `AbortExposureAsync` — **후지 카메라는 중단 명령 대신 노출이 끝나기를 기다림**, 카메라 정보가 안 오면 "모름"(CX-32-03). 멈춤 확인이 N.I.N.A.로 안 되면 `AscomAxis` 비상 정지 |
| `Phd2Client` | PHD2 JSON-RPC :4400 | 이벤트 스트림(AppState·Calibration*·GuideStep·StarLost·SettleDone), stop_capture → Stopped 기다림 → set_connected, loop·find_star·guide·보정값·노출·save_image·get_pixel_scale. 호출마다 연결, 끊기면 구독을 끝내고 다음 호출이 새로 연결. 답이 없으면 `Phd2Exception`(취소 예외가 아니라) |
| `SharpCapBridge` | 프로세스 + 스크립트 + 보고 | `SharpCap.exe /camera "<이름>" /runscript sharpcap-polar.py` — 스크립트가 노출·게인을 맞추고 극축 정렬을 켜 1초마다 상태를 보고, 명령(노출·다음 단계)을 받는다. 카메라 이름은 지난번 실제로 연 이름(`sharpcap-camera.txt`) → PHD2 이름 → 스크립트가 목록에서 고름. 화면이 원할 때만 영상 저장(`WantView`). 닫기는 창 닫기(아이라가 켠 것만), 이미 켜져 있으면 묻기 |
| `AscomAxis` | ASCOM COM(N.I.N.A.와 같은 적도의에 동시 연결) | RA축 `MoveAxis` 회전(극 찾기), 축 멈춤, 비상 정지(AbortSlew + 두 축 멈춤). 명령은 뒤에서 하나씩 15초 한도 — 시작 명령을 포기한 뒤 늦게 풀리면 돌리지 않거나 바로 멈추고, 멈춤·비상 정지는 앞 명령이 막혀도 보냄(CX-32-01). 드라이버 객체는 쓰고 바로 놓음. 드라이버 Id는 연결된 적도의에서 배움 |
| `OasisSdk` | Oasis 제조사 DLL | 포커서 0점 잡기 전용(ASCOM·N.I.N.A.에 Set Zero가 없음). 일련번호가 맞는 포커서만 |
| `LiveImages` | 위 셋 + 파일 | 하늘 화면 이미지(SharpCap 저장 영상 · PHD2 가이드 사진(FITS → PNG) · N.I.N.A. 마지막 사진) |
| `Fits` · `Png` · `CameraScale` | — | FITS 읽기(가이드 사진, 시험 사진 히스토그램·포화·배경·카메라 눈금), PNG 만들기, 그날 밤 카메라 눈금 |
| `Phd2Windows` | Windows 창 | PHD2 장비 연결 창이 열려 있는지(열려 있으면 PHD2가 가이드 카메라를 놓지 못함) |
| `AscomWeather` | ASCOM ObservingConditions | WandererBox 환경 센서의 기온·습도·이슬점 직접 읽기(N.I.N.A.와 동시에) |

- 허브 스위치(USB 출력·카메라 전원 출력)는 N.I.N.A. `switch/set`으로 — **index는 출력 Id가 아니라 쓰기 가능한 출력 목록의 순서**, N.I.N.A.는 실패해도 성공이라 답하므로 다시 읽어 확인(`EquipmentConnector`).
- **SharpCap 영상 비추기**는 서버가 아니라 데스크톱 창이 한다: `src/Astro.Desktop/WindowThumbnail.cs`(DWM 썸네일 + UI Automation `WpfPreviewPanel` › `Image`), 화면 `web/src/components/WindowView.tsx`가 비출 자리를 넘김. 영상 칸을 잠깐 못 찾아도 3초까지 앞 자리 유지.
- 다른 프로그램 창이 아이라 위로 뜨지 않게: `Engine/BackgroundWindows`(켜지는 동안 아이라 "항상 위").

## 3. 작업별 구현 — 코드만 봐서는 알기 어려운 결정

하는 일은 OPERATION_PROCEDURE.md, 실패 처리 순서 같은 세부는 각 작업 클래스(`Tasks/<작업>/`)의 주석과 코드가 기준이다. 여기에는 왜 그렇게 만들었는지만 둔다.

- **극축 정렬**: 순서 = 추적 항성 → PHD2 `stop_capture` → Stopped 기다림 → `set_connected false` → SharpCap 실행 → (스크립트가 노출·게인·극축 정렬 켬) → 극 찾기 → 정렬 → SharpCap 닫기 → PHD2 `set_connected true` → N.I.N.A.–PHD2 확인. 넘겨받기·극 찾기는 적도의 잠금 안에서(추적 설정 포함). RA 회전은 goto가 아니라 `AscomAxis`의 `MoveAxis`(goto는 극 근처에서 Dec축까지 움직이고 덜 돈다) 60°, SharpCap "Allow smaller rotation angles" 필요, 둘째 사진 뒤 `PolarAlignment.Advance()`. 정렬 평가는 SharpCap의 Good·Excellent 글자를 스크립트로 못 받아 아이라가 직접: 조절량(픽셀) × PHD2 `get_pixel_scale`. 실제 모드에서는 이 환산을 실기로 확인하기 전까지 등급 말 대신 방향·픽셀 값만(IMPL-03). SharpCap은 최소화하지 않는다(최소화하면 Windows가 창을 그리지 않아 영상을 비출 수 없다) — 켜지는 동안 아이라 "항상 위". 이미 켜진 SharpCap은 스크립트로 조종할 수 없어 닫고 다시 켜야 하고, 아이라가 정리하며 닫는 것은 아이라가 켠 SharpCap뿐.
- **캘리브레이션**: 장비 준비를 시작할 때는 항상 새로(조립·극축 정렬로 가이드 카메라를 만졌으므로), 같은 밤 대상만 바꿀 때는 `IPrepMemory.LastCalibration` 재사용. "가이딩 없이 진행"은 시작 선택(`StartAlternatives`)이라 움직이기 전에 고른다.
- **초점**: 기억된 위치가 없으면 자동초점 전에 넓게 찾기(자동초점은 시작점 앞뒤 수백 걸음만 봄). 손으로 맞춘 위치는 저장하지 않는다. 점검을 마쳤으면 먼저 0점(`FocuserZeroRequest`).
- **센터링**: N.I.N.A.의 센터링 명령이 아니라 아이라가 사진 + 솔빙 → 거리 → 동기화 → 다시 이동을 반복(`RealCenterDevices`) — 사진의 HFR·별 수를 초점 확인에 넘기려고.
- **초점 확인**: 괜찮으면 `AutoNext`(근거만 잠깐 보여 주고 가이딩으로).
- **가이딩**: N.I.N.A. 가이더 명령으로 시작해야 N.I.N.A.가 가이딩 상태를 안다(디더링·반전 후 재시작). `KeepsRunning`이라 앞 작업을 다시 하거나 중단할 때 러너가 먼저 멈춘다.
- **시험 사진**: 히스토그램·배경·포화는 N.I.N.A.가 주지 않아 저장된 FITS를 직접 읽는다(`Fits.Analyze`). 밝기는 사진에서 알아낸 카메라 눈금(포화 값·바닥값)으로 읽고 `CameraScale`에 남겨 그날 밤 플랫 노출 찾기(`RealWrapDevices`)도 쓴다 — N.I.N.A. 비트 수는 사용자 설정이라 틀릴 수 있다. 별 모양은 `StarShapes`: ASTAP `astap_cli -extract`(N.I.N.A. 프로필의 ASTAP 폴더, `NinaRig.AstapPathAsync`)로 별을 찾고 별마다 화소 2차 모멘트로 이심률·방향 → 아홉 칸 `StarField`, 판정은 `StarFieldRules`(아홉 칸)·`TestShotRules.Trailed`(가운데 칸 + 방향 모임). 읽은 뒤 같은 날짜 폴더의 "시험" 폴더로 옮긴다(`ImageFolders.Move` — SNAPSHOT 폴더면 그 옆, 아니면 그 아래).
- **노출 멈추기**(센터링·시험 사진·마무리·이어서 하기): X-T5는 일반 노출을 중단할 수 없어 중단 명령 대신 끝나기를 기다린다(`NinaRig.AbortExposureAsync`). 노출이 정지 확인 한도(15초)보다 길면 "장비 상태 다시 확인"이 뜨고, 끝난 뒤 누르면 풀린다.
- **마무리**: 같은 러너를 쓰는 작업 셋. 장비 정리 뒤 프로그램 닫기는 `ProgramCloser`(아이라 종료와 같은 차례).

## 4. 판정 규칙 (순수 함수, 단위 테스트)

| 작업 | 입력 | 결과 |
|---|---|---|
| 극축 정렬 | 조절량(px) × 가이드 화면 배율, 계획의 초점거리·한 장 노출 | 멀어요 / 아쉬워요 / 충분해요 / 완벽해요(실제 모드는 확인 전까지 방향·픽셀만) |
| 캘리브레이션 | 직교 오차, RA·Dec 속도 비, PHD2 경고 | 좋아요 / 다시 하는 게 좋아요 |
| 초점 | 곡선 맞춤 정도, 최소 HFR | 좋아요 / 다시 |
| 가이딩 | 전체 RMS ÷ 주 카메라 한 픽셀(사용자 장비 2.41″) | 충분해요 / 괜찮아요 / 아쉬워요 |
| 시험 사진 | HFR(초점 대비), 가운데 이심률·방향 모임, 아홉 칸 별 크기, 배경, 포화, 가이딩 RMS | 좋아요(아홉 칸 결과는 알림) / 문제 + 할 일 |

## 5. 저장 (아이라 데이터 폴더 `%LOCALAPPDATA%\AIRA`)

- 망원경별 지난 초점 위치와 기온 `focus-memory.json` · SharpCap 카메라 이름 `sharpcap-camera.txt` · 끝낼 때 포커서 0 기록(`FocuserParkStore`) · 그날 밤 결과·진행(`NightSessionStore` — 2시간 안에 다시 켜면 이어서 할지 묻기).
- 같은 밤 안에서만 쓰는 것(`IPrepMemory`, 메모리): 지난 초점·캘리브레이션·극축 정렬 시각·대상별 카메라 방향.

## 6. 테스트

- 작업마다 자기 모의 장비로 단독 테스트(`tests/Astro.Server.Tests/Prepare/TaskIsolationTests.cs` 등, 정상·실패 경우).
- 러너는 가짜 작업(정해진 결과를 내는 작업)으로 순서·다시 하기·재시작·두 번 누르기(`Harness`, `SelfAuditLowTests`).
- 결과 기록 모양 고정(계약 테스트), 묶음 사이 흐름(대상을 바꿔도 장비 준비 유지, 극축 정렬 다시 → 대상 묶음 무효, 대상 변경 때 가이딩 정지 확인).
- 실제 장비: `tests/Astro.Server.Tests/Real/RealDeviceTests.cs`(`AA_REAL=1`일 때만), 어댑터의 실패 주입은 가짜 HTTP·가짜 드라이버(`FujiStopTests`, `AscomAxisLateStartTests`, `Phd2ClientTests`).

## 7. 실기 확인과 남은 것

맑은 날·실기로 확인할 것과 아직 만들지 않은 것은 [FIELD_TEST_CHECKLIST.md](../FIELD_TEST_CHECKLIST.md)에 모은다.
