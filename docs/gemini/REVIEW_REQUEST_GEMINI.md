# Gemini 리뷰 요청서

사용자가 Gemini에게 이 파일의 주소만 주면 되도록 Claude가 쓴다(2026-10-16 사용자 — 요청 글을 복사해 붙여 넣지 않고 저장소에서 바로 읽게).
요청할 때마다 아래 **이번 요청**을 새로 고친다. 지난 요청은 지운다(결과는 [REVIEW_GEMINI.md](REVIEW_GEMINI.md)에 남는다).

---

## 저장소 읽는 법

- 공개 저장소: **https://github.com/Cholang/Astrophoto_Assistant** (가지 `main`). 이 요청서가 들어간 커밋이 기준이다 — **답 첫머리에 실제로 읽은 커밋 해시**를 적어 줘.
- 인터넷으로 저장소를 읽지 못하면 그렇다고 먼저 말해 줘. 사용자가 ZIP(GitHub의 Code → Download ZIP)을 올린다. 읽지 못한 파일을 추측으로 채우지 않는다.
- Gemini는 이 PC에 파일을 쓰지 못한다. **답 전체를 마크다운 한 덩어리**로 주면 사용자가 Claude에게 전하고, Claude가 `REVIEW_GEMINI.md`에 "## 7." 절(작성자 `[Gemini]`)로 넣은 뒤 항목마다 판단을 붙인다.

## 프로젝트 한 줄

**아이라(AIRA)** — N.I.N.A. 위에서 천체사진 촬영을 처음부터 끝까지 안내하는 Windows 데스크톱 앱. C#/.NET 10 서버(`src/Astro.Server` · `Astro.Core` · `Astro.Nina`) + React/TypeScript 화면(`web/`, WPF + WebView2 창 `src/Astro.Desktop`). 사용자는 개발자가 아닌 천체사진가이고, Claude가 구현하고 Codex가 정확성을 리뷰한다. 문서 지도는 [AGENTS.md](../../AGENTS.md), 커밋마다의 요청·변경은 [HISTORY.md](../../HISTORY.md)(최신이 위).

---

## 이번 요청 (2026-10-16 · Claude 작성)

- **기준**: `main`의 HEAD(이 요청서가 들어간 커밋). 지난 Gemini 리뷰는 6절 `38e1744`(구조·단순화)이고, 그 뒤 HISTORY 맨 위부터 `52924a8` 항목까지가 새로 바뀐 것.
- **분량은 제한하지 않는다.** 길어도 좋으니 빠짐없이 — 다만 항목마다 **파일:줄 근거**를 달고, 중요한 것부터.
- **역할 나눔**: 같은 범위의 정확성·장비 안전 버그는 Codex가 본다([REVIEW_REQUEST_CODEX.md](../codex/REVIEW_REQUEST_CODEX.md)). Gemini는 아래 A~E. 정확성 버그가 눈에 띄면 짧게 적기만.

### A. 큰 클래스 쪼개기 실행 계획 — 가장 중요

내일(2026-10-17) Claude가 **이 계획을 그대로 따라** 쪼갠다. 그래서 "이렇게 하면 좋다"가 아니라 순서대로 실행할 수 있는 단계로 써 줘.

- **대상**: 지금 400줄이 넘는 파일(아래 표, `8d58434` 기준). 6절의 1-1~1-3 제안(EquipmentConnector · EquipmentScreen · PrepareRunner)은 그 뒤 코드가 바뀌었으니 **지금 코드로 다시**.

  | 줄 | 파일 |
  |---|---|
  | 1003 | `src/Astro.Server/Shoot/ShootSession.cs` |
  | 995 | `src/Astro.Server/Prepare/Flow/PrepareRunner.cs` |
  | 864 | `web/src/screens/EquipmentScreen.tsx` |
  | 853 | `src/Astro.Server/Engine/EquipmentConnector.cs` |
  | 699 | `web/src/components/RigScene.tsx` |
  | 688 | `web/src/components/LiveView.tsx` |
  | 656 | `web/src/screens/PlanScreen.tsx` |
  | 654 | `src/Astro.Server/AppServer.cs` |
  | 625 | `web/src/App.tsx` |
  | 570 | `src/Astro.Server/Assistant/PlanAssistant.cs` |
  | 482 | `src/Astro.Server/Prepare/Real/NinaRig.cs` |
  | 476 | `src/Astro.Server/Engine/NinaNotices.cs` |
  | 417 | `src/Astro.Server/Assistant/PlanTools.cs` |
  | 402 | `web/src/components/NightGraph.tsx` |

- **파일마다**:
  1. 지금 맡은 일 목록(줄 범위와 함께)
  2. 나눌 경계와 새 파일 이름 — 기존 폴더·이름 규칙을 따른다(서버는 `Prepare/Flow`, `Prepare/Tasks/<작업>`, `Engine` …, 웹은 `components/`, `screens/`, 순수 계산은 `*Math.ts`처럼 시험 가능한 파일로)
  3. 무엇을 옮기고 무엇을 남기는지
  4. **경계를 넘는 공유 상태** — `lock`과 그 안에서 바뀌는 필드, 취소 토큰, 이벤트·콜백(`TaskFinished`, `BeforeRun`, `ForeignRedo` 등), React 상태·ref·effect 순서. 옮기면서 잠금 범위가 달라질 위험이 있는 곳은 꼭 짚어 줘
  5. **옮기는 순서** — 단계마다 빌드와 시험이 통과하는 작은 단계로(한 번에 한 파일)
  6. 단계마다 돌릴 시험 — `tests/Astro.Server.Tests`의 시험 클래스 이름, 웹은 `npm test`(vitest)·`npm run lint`·`npm run build`
  7. 하지 말 것 — 동작 변경, HTTP 경로·JSON 모양 변경, 이름만 바꾸기, 주석 속 "왜"(한국어 설명·날짜·사용자 결정)를 잃기
- 쪼갤 필요가 없다고 보는 파일은 **이유와 함께 "그대로"**.
- 함께 할 작은 정리: 다크플랫 옮기기(`Prepare/Tasks/Wrap/WrapDevices.cs`의 `MoveToDarkFlatAsync`)가 `Prepare/Real/ImageFolders.Move`와 같은 일을 따로 한다 — 합치는 자리를 계획에 넣어 줘.
- 마지막에 **전체 작업 순서**(어느 파일부터, 예상 단계 수)를 한 표로.

### B. 화면 규칙(DESIGN.md) 전수 검토 — `web/src` 전체

`DESIGN.md`가 기준이다. 특히:

- 색·글꼴·간격은 CSS 변수(토큰, `web/src/index.css`)만 — 컴포넌트에 색 값을 직접 쓴 곳
- **촬영용 적색 테마**(`data-theme='night'`)에서 빨강이 아닌 색이 보이는 곳 — 흰 초점 테두리, 그림자, 이미지·SVG 안의 고정 색, `color-mix`
- 공통 부품(`Button` · `Modal` · `ConfirmDialog` · `Tooltip` · `InfoTip` · `Field` · `GlowFrame` · `StepRail`)을 쓰지 않고 따로 만든 곳
- **레이아웃 안정성**(1장): 나타날 때 주변이 밀림, 화면 밖에 숨긴 요소나 **돌거나 커지는 표시가 스크롤 막대를 띄움**(2026-10-16에 종료 창의 도는 원이 실제로 그랬다 — 같은 구조가 다른 곳에 있는지)
- 숫자는 중앙 정보에만(예외: 구도 회전 각도), 한 화면에 주 버튼 하나
- 용어(5장): 가이드 · 망원경 · 전원 설정, 아이라를 "앱"이라 부르지 않고 이름은 `PRODUCT`로, 확인 창 문장은 "~합니다"
- 위반마다: 파일:줄 · 어긴 규칙(DESIGN.md 절) · 사용자에게 보이는 모습 · 고치는 방법

### C. 문서와 코드가 다른 곳

- `DESIGN.md`·`docs/OPERATION_PROCEDURE.md`는 **사용자 결정의 기준** — 코드가 다르면 결함 후보로 적는다.
- `docs/implementation/`(PREPARE · SHOOT · PRECHECK · POWER_WIRING · ARCHITECTURE)은 코드 설명 — 낡은 문장을 "어느 문장을 → 무엇으로" 목록으로.
- 최근에 바뀌어 낡았을 가능성이 큰 것: 준비 시간(기록의 중앙값), 구도 회전, 부팅 순서, 출발 전 점검이 프로필 바로 다음, 용어 일괄 변경.

### D. 현장 노트북 성능

밤새 켜 두는 저전력 노트북 앱이다. 주기적 요청(폴링 간격·중복), 계속 도는 애니메이션(장면의 `requestAnimationFrame`, 배경 별·성운, 태양, 빛 테두리, 유성), 불필요한 다시 그리기, 큰 이미지 받기(ETag), 서버의 타이머·백그라운드 작업. 측정하지 않고 판단한 것은 "추정"이라고 적어 줘.

### E. 여유가 있으면 — 시험이 없는 중요한 논리

장비를 움직이거나 판정(성공·실패, 시각, 각도, 밝기)하는 순수 계산 중 시험이 없는 것 목록 + 시험 이름 제안(서버 xUnit은 한국어 메서드 이름 관례).

---

## 빼는 것

- `[임시]` 건너뛰기·모의 실패 버튼(사용자가 지울 때를 정한다), 폰 화면(계획에서 빠짐), `docs/FIELD_TEST_CHECKLIST.md`의 실기 미확인 항목, 새 기능 제안, 디자인 취향.

## 답 형식

- 첫머리: `## 7. <한 줄 제목> (기준 커밋 <해시>) · <날짜> · [Gemini]` + 읽은 범위, 저장소를 실제로 읽었는지(읽지 못한 파일이 있으면 그 목록).
- 항목 ID: A는 `GM-7-A-01`…, B는 `GM-7-B-01`…, C·D·E도 같게.
- 항목마다: **무엇** / **왜** / **근거**(파일:줄, 짧은 인용) / **제안** / **작업 크기**(작음·중간·큼) / **실사용 등급**(장비 안전 · 겪음 · 이론상 — AGENTS.md).
- 마지막에 **우선 조치 순서**(10개 이내).
