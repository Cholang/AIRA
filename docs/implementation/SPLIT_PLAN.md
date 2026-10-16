# 큰 파일 분리 계획

작성: 2026-10-16 [Claude]. 기준 커밋 `52fad14` — 아래 줄 번호는 이 커밋 기준이다(Codex 리뷰를 반영하면 조금 밀린다, 함수 이름으로 찾을 것).
바탕: Gemini 리뷰 6·7절의 분리 제안(`docs/gemini/REVIEW_GEMINI.md`). 7절의 A 계획은 줄 수는 맞지만 함수 이름이 상당수 실제와 달라 실제 코드를 읽고 다시 나눴다.
**분리가 끝나면 이 문서는 지우고**, 새 파일 구조는 `ARCHITECTURE.md`·`PREPARE.md`·`SHOOT.md`에 반영한다.

## 원칙

- **동작을 바꾸지 않는다.** 코드를 옮기기만 한다 — 고침·이름 바꾸기·정리는 같은 커밋에 섞지 않는다(문제가 생겼을 때 옮김 탓인지 고침 탓인지 가리기 위해).
- **먼저 Codex 리뷰를 반영해 커밋한 뒤** 시작한다. 같은 파일(ShootSession · PrepareRunner · EquipmentConnector …)을 보므로, 먼저 옮기면 지적한 자리가 흩어진다.
- **서버는 `partial class`로 나눈다.** 같은 클래스라 잠금(`_gate`)·필드·취소 토큰이 그대로 하나이고, 기본 생성자 매개변수(`devices`·`log` 등)도 다른 파일에서 그대로 쓴다(2026-10-16 .NET 10 시험 프로젝트로 확인).
- **상태 필드는 본 파일 한곳에 모은다.** 잠금 아래 있는 값이 어디 있는지 한눈에 보이게 — 지금 중간에 흩어진 필드(아래 표의 "흩어진 필드")는 첫 커밋에서 위로 옮긴다(선언 위치만 바뀜).
- **화면(웹)은 컴포넌트·순수 데이터만 다른 파일로.** CSS 모듈은 그대로 같은 파일을 함께 import한다(클래스 이름·모양이 바뀌지 않게).
- 커밋은 파일 하나(또는 같은 종류 몇 개)씩. 400줄은 기준일 뿐 — 응집된 파일을 억지로 쪼개지 않는다.

## 확인 방법 (커밋마다)

- 서버: `dotnet build AA.slnx` 새 경고 0 · `dotnet test` 전부 통과(지금 295, 건너뜀 1) — 수가 같아야 한다.
- 웹: `npx tsc -b` · `npx oxlint`(새 경고 0) · `npx vitest run`(지금 52) · `npm run build`, 그리고 시험 서버(5211, 장비 흉내)에서 그 화면을 한 번 지나가 본다.
- 옮기기만 했는지: `git diff -M --color-moved=dimmed-zebra --stat` — 옮긴 줄 말고 바뀐 줄이 거의 없어야 한다.

## 순서

1. **1단계 — 독립된 타입·데이터만 새 파일로** (가장 안전, 같은 클래스 안을 건드리지 않음)
2. **2단계 — 서버 partial 분리** (파일마다 커밋, 끝날 때마다 시험 전체)
3. **3단계 — 웹 컴포넌트 분리** (화면마다 커밋, 시험 서버에서 화면 확인)
4. **(선택) 4단계 — 코드 모양을 바꾸는 정리**: 긴 함수의 블록을 메서드·훅으로 빼기. 동작은 같아도 코드가 바뀌므로 따로, 필요할 때만.

---

## 서버

### `Shoot/ShootSession.cs` (1003줄 → 약 380)

| 새 파일 | 옮길 것 (줄) | 줄 수 |
|---|---|---|
| `ShootModels.cs` | `TargetShots` · `NightShootResult` · `ShootView` · `enum ShootMode` (7–28) | 약 25 |
| `ShootSession.Control.cs` | 시작·멈춤: `Start` · `AbortForRestartAsync` · `StopForQuitAsync` · `TempSkipAsync` · `Stop` · `Answer` · `TakeAnswer` (185–364) / 끝·정지 확인: `EndAsync` · `TryStopGuidingAsync` · `RecheckStopAsync` · `MountUnconfirmed` · `BlocksNext` · `Record` (914–997) | 약 265 |
| `ShootSession.GuideWatch.cs` | 이슬·불안정: `CheckDewAsync` · `WaitUnstableAsync` (564–641) / 가이드 별 잃음: `CheckGuideAsync` · `DegradeAsync` · `StepGuideExposureAsync` · `ExposeWatchedAsync` · `HandleLossAsync` · `WaitForStarAsync` · `RecenterSafelyAsync` · `ExcludeFrame` · `MoveExcluded` · `Pause` · `Resume` · `PollGuideAsync` · `Median` (643–912) | 약 345 |
| 본 파일에 남김 | 상수·필드, `View` · `Snapshot` · `WatchAsync` · `Set` · `Note`, 주 고리 `RunAsync`(369–540) · `WakeableDelayAsync` · `EndReason` | 약 370 |

- 흩어진 필드: `_flipped` · `_lastFocusTemp`(366–367), `_hfrRecent`(542), `_recordIndex`(999) → 필드 묶음으로.
- `RunAsync` 안의 자오선 반전(384–401)·초점 다시 맞추기(405–427)·제외 사진 멈춤(437–457) 블록을 메서드로 빼는 것은 **4단계**(블록 안에 `return`·`continue`가 있어 빼면 반환값으로 바꿔야 함 — 코드 변경).
- 시험: `ShootAndWrapTests` · `ReviewFixTests` · `QuitSafetyTests` · `SelfAuditLowTests` · `TempSkipTests`.

### `Prepare/Flow/PrepareRunner.cs` (995줄 → 약 470)

| 새 파일 | 옮길 것 (줄) | 줄 수 |
|---|---|---|
| `RunnerSetup.cs` | `RunnerSetup` 레코드 (5–48). 29줄의 떠도는 설명("한 묶음에 일곱 작업")은 `Single` 위로 — 설명 자리만 | 약 45 |
| `PrepareRunner.Stop.cs` | `AbortAsync` · `StopForQuitAsync` · `ShowQuitPaused` · `SuspendAsync` · `MarkAborted` · `StopLingeringAsync` (565–692), `StopCurrentAsync` · `ShowStopUnconfirmed` · `TryStopAsync` · `RecheckStopAsync` (810–880) | 약 200 |
| `PrepareRunner.Actions.cs` | 버튼: `Act` · `RedoAsync` (469–564), `RetryAsync` (693–707) | 약 115 |
| `PrepareRunner.TempSkip.cs` | [임시] `TempSkipTarget` · `TempSkipAsync` · `FinishTempSkip` · `TempSkipResults` (708–809) — 나중에 [임시]를 걷을 때 파일 하나만 지우면 됨 | 약 100 |
| `PrepareRunner.RunHandle.cs` | 작업이 화면에 쓰는 손잡이 `private sealed class RunHandle` (935–995, Gemini가 `TaskRun`이라 부른 것) | 약 65 |
| 본 파일에 남김 | 필드·`Current`, 상태 읽기·`View`·`WatchAsync`, `Start` · `StartResumedAsync` · `StartFreshAsync`, 실행 고리 `RunNext` · `RunTask` · `OnOutcomeAsync` · `FinishAsync` · `AutoNextAsync`, `ResultType` · `Heartbeat` · `CheckStaleNow` · `Update` | 약 470 |

- 흩어진 필드: `_pendingFinish`(464).
- 시험: `RunnerTests` · `PrepareFlowTests` · `TempSkipTests` · `TaskIsolationTests` · `QuitSafetyTests` · `ReviewFixTests`.

### `Engine/EquipmentConnector.cs` (854줄 → 약 480)

| 새 파일 | 옮길 것 (줄) | 줄 수 |
|---|---|---|
| `EquipmentSimulation.cs` | `EquipmentOptions` · `EquipmentSimulation` · `EquipmentChoices` (앞쪽 별도 클래스들, 1–96) | 약 95 |
| `EquipmentConnector.Hub.cs` | 전원 허브·배선 학습: `PoweredAnywayAsync`(149–162), `ShortOutlet` · `LearnIfConnected` · `WiringHint` · `HubInputVoltsAsync` · `IsInputVoltage` · `EnsureHubUsbOnAsync` · `HubOutputsAsync` · `SetHubOutputAsync` · `WaitListedAsync` (592–717) | 약 140 |
| `EquipmentConnector.Devices.cs` | PHD2 점검 `CheckPhd2Async`(718–759), 프로필 장비 읽기 `Device` · `ReadDevicesAsync` · `ShortName` · `TryRepoint` · `Text`(760–826) | 약 110 |
| 본 파일에 남김 | `Slots` · `Slot`, 실행 `PlanAsync` · `BeginRun` · `EndRun` · `AbortAsync` · `RunAsync` · `RetryAsync` · `GoWithoutAsync`, 연결 `ConnectTimedAsync` · `ConnectOneAsync`(408–590, 큰 함수 그대로), `Make`·`Pending`·조사 도우미 | 약 480 |

- 흩어진 필드: `_timing`(384), `_learnOnPass`(605).
- Gemini가 말한 "진단(`Diagnose`·`MountProblem`) 묶음"은 이 파일에 없다.
- 덤으로 보이는 것(이번엔 손대지 않음): `_hubNoPower`(143)는 넣기만 하고 읽는 곳이 없어 빌드 경고 — 분리 뒤 따로 정리.
- 시험: `AbortUnansweredTests` · `DevicePrecheckTests` · `Phd2CheckTests` · `Phd2ConnectTests` · `PostConnectCheckTests` · `PowerWiringTests`.

### `Assistant/PlanAssistant.cs` (570줄 → 약 240)

| 새 파일 | 옮길 것 (줄) | 줄 수 |
|---|---|---|
| `ChatModelFactory.cs` | `AssistantOptions` · `ChatModelFactory` · `DisplayMessage` (14–67) | 약 55 |
| `PlanView.cs` | `static class PlanView` (507–570) | 약 65 |
| `PlanAssistant.Chat.cs` | AI 대화: `SendAsync` · `Event` · `NextQuotaReset` · `Hm` · `NightSummary` · `MoonLine` · `CloudLine` · `SystemPrompt` (301–505) | 약 205 |
| 본 파일에 남김 | 필드, 확정·복원·건너뛰기, 화면 직접 조작(`Pick` · `Remove` · `ChangeSettings` · `SetStart` · `Edit`), `View` · `Chart` · `Search`, 프로필·장비 반영 | 약 240 |

- 흩어진 필드: `_profileId`(264).

### `AppServer.cs` (654줄 → 약 200) — `static partial class`

- 이미 `MapShoot` · `MapPrepare` · `MapSettings` · `MapPlan` · `MapSession` · `MapProfiles`로 나뉘어 있다. 남은 큰 덩어리는 `Build`(24–288) 안의 서비스 등록과 직접 단 엔드포인트.
- `AppServer.Services.cs`: 서비스 등록(약 33–105)을 `AddAiraServices(builder)`로.
- `AppServer.Endpoints.cs`: `Build` 안의 설치 확인·엔진·N.I.N.A.·새 버전·장비 연결·종료(엔진 close · stop-for-quit · 포커서 park · 적도의 홈)·로그 엔드포인트(127–288)를 `MapSetupAndEngine` · `MapEquipment` · `MapQuit`로, 기존 `Map*` 메서드와 요청 레코드(456–546)도 이 파일(또는 영역별 파일)로.
- **URL·등록 순서는 그대로.** 시험: 서버 시험 전체 + 시험 서버에서 부팅 → 장비 연결 → 계획까지 한 번.

### `Engine/NinaNotices.cs` (476줄 → 약 200)

| 새 파일 | 옮길 것 (줄) |
|---|---|
| `NinaWindowHiders.cs` | `NinaProcesses` · `NinaToastHider` · `NinaAutofocusWindowHider` (350–476) — 창 감추기(Win32) |
| `NinaLogRules.cs` | `static class NinaLogRules` (116–261) — 로그 줄 번역(순수 함수) |
| 본 파일에 남김 | `NinaNotice` · `NinaNotices` · `NinaLogWatcher` |

- 시험: `NinaLogRulesTests`.

### 나누지 않음

- `Prepare/Real/NinaRig.cs`(482): N.I.N.A. 장비 명령을 감싼 얇은 한 덩어리 — 그대로(Gemini와 같은 판단).
- `Assistant/PlanTools.cs`(417): 이미 `ShootWindow`·`PlanRules`·`NightChart`로 나눠져 있다 — 그대로.
- 함께 하면 좋은 작은 정리(1단계 끝에 따로 커밋): `WrapDevices.MoveToDarkFlatAsync`의 폴더 계산을 `ImageFolders.Move`로(Gemini A-14 — 같은 코드라 동작 차이 없음).

---

## 웹

### `screens/EquipmentScreen.tsx` (864줄 → 약 520, 4단계까지 하면 약 430)

- 실제 구성: `EquipmentScreen`(50–79, 목록 받는 동안 원) → `EquipmentGraph`(81–488, 태양계 — 상태·자동 진행·편집 225줄 + 그림 180줄) → 부품들.
- `screens/EquipmentParts.tsx`(약 280): `PlanetNode`(541–631) · `EditNode`(632–687) · `DeviceList`(688–746) · `CameraPower`(747–788) · `statusText` · `selectable` · `TempFailButtons`(816–864). `EquipmentScreen.module.css`를 함께 import.
- `screens/equipmentOrbits.ts`(있는 파일)로: `orbitPath`(489) · `sunArcPath`(528) — 궤도 계산끼리.
- 4단계(선택): 자동 진행(149–186) → `useAutoAdvance`, 장비 변경 모드(187–239) → `useEquipmentEdit` 훅.
- Gemini가 말한 "출발 전 점검 3항목 `PRECHECKS`·`CameraPowerDialog`"는 이 파일에 없다.

### `components/RigScene.tsx` (699줄 → 약 300)

- Gemini는 옛 장비 화면(궤도·행성)으로 착각했다. 실제는 부팅·프로필·점검 장면.
- `components/rigSceneData.ts`(약 190): 점검 항목 `ITEMS`, 별·별자리·`place`·`spread`, 땅 `GROUND` · `groundTop`, 잔디 `GRASS`, 구체 회전·유성 상수(1–199). 순수 데이터라 `groundTop` 등에 시험을 붙일 수 있다.
- `components/RigArt.tsx`(약 140): 장비 선화(삼각대·적도의·망원경·포커서·캡·전원·노트북, 514–643)와 `Badge`(692–). `part()` 결과·`screenRect` ref·노트북 누름을 props로 받는다.
- 4단계(선택): 부팅 별 반짝임(334–369) → `useBootSparkle`, 유성(370–410) → `useMeteors`.

### `components/LiveView.tsx` (688줄 → 약 150)

- `components/LivePhoto.tsx`: `Histogram`(178) · `RealImage`(230) · `FramedImage`(311) · `SolvedPhoto`(583) · `TestPhoto`(603) · `NineGrid`(613).
- `components/LivePanels.tsx`: `FocusPanel`(126) · `SharpCap`(362) · `GuideImage`(399) · `GuidingGraph`(437) · `useSlewTrack`(473) · `SkyMap`(492) · `FocusCurve`(533).
- `components/liveSimFields.tsx`: 모의 화면 별밭 `clamp` · `rng` · `starfield` · `polarStars` · `galaxy` (643–688).
- `LiveView.module.css`를 함께 import. 시험: `liveViewMath.test.ts`(있음).

### `screens/PlanScreen.tsx` (656줄 → 약 200)

- `screens/PlanChat.tsx`(약 220): 대화 `PlanChat`(198–418).
- `screens/PlanTarget.tsx`(약 110): `FramePreview`(419–476) · `WhenLines`(477–502) · `FovBox`(503–529).
- `screens/PlanSettings.tsx`(약 125): `SettingsRow`(530–649) · `whyShort`(650–).
- 본 파일: `PlanScreen` · `TARGET_ITEMS` · `PlanBody`. 시험: `plan.test.ts`.

### `App.tsx` (625줄 → 약 480)

- 하나의 큰 컴포넌트라 1~3단계로는 상수만 뺀다: `appFlow.ts` — `Phase` · `STAGE_OF` · `CORNER_SUN` · `NOTICE_HIDE` · `WATCHED` · `SITE_SHOWN` · `LABEL_OF` · `SCENE_OF` · 시간 상수(41–107).
- 4단계(선택): 부팅 키 대기 → `useBootPrompt`, N.I.N.A. 감시 → `useNinaWatch`, 관측지 → `useSite`.
- Gemini가 말한 `QuitDialog`는 이미 `components/QuitControl.tsx`에 있다.

### 나누지 않음

- `components/NightGraph.tsx`(402): 계산은 이미 `nightGraphMath.ts`로 나눠져 있고 남은 것은 SVG 하나 — 그대로.
- 함께 하면 좋은 작은 정리(3단계 끝에 따로 커밋): 쓰지 않는 `components/DiagnosisCard.tsx`·`.module.css` 지우고 `DiagnosisData` 타입은 `DiagnosticCard.tsx`로(Gemini B-06).
