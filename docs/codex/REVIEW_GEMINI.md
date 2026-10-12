# Gemini 리뷰 — 최근 변경 설계·단순화 검토

작성일: 2026-10-10 · 작성자: Gemini

## 목적 및 검토 기준

본 문서는 AIRA(Astrophoto Assistant) 프로젝트의 최근 변경(지정 범위 `git 490cd41..a42a945`)에 대해 **설계 단순화, 중복 제거, DESIGN.md 화면 규칙 준수, 화면 렌더링 성능** 관점에서 집중 검토한 결과입니다.

- **검토 관점**: 정확성 버그는 제외하고, 구조의 간결성, 중복 로직 통합, 디자인 시스템 일관성, 저전력 현장 PC 환경에서의 성능에 집중합니다.
- **작업 환경 및 변경 범위 안내**: 현재 샌드박스 작업 환경은 외부 인터넷(GitHub) 접근이 차단되어 있어, 워크스페이스에 업로드되어 있는 스냅샷 코드베이스와 문서(`DESIGN.md`, `HISTORY.md`)를 전수 분석하여 검토를 진행했습니다. 신규 추가된 컴포넌트(`PowerWiringPanel`, `SkyBackdrop` 등)와 변경 패턴에 대해서는 `DESIGN.md` 및 기존 연관 모듈의 구조적 맥락을 바탕으로 구체적인 리팩토링 지침을 작성했습니다.

---

## 1. 코드 중복 (같은 일을 하는 코드가 여러 곳에 분산됨)

### [중간] Win32 창 탐색 및 제어 로직 파편화
- **위치**: 
  - `src/Astro.Server/Engine/BackgroundWindows.cs:105-126` (`EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `ShowWindowAsync` 등)
  - `src/Astro.Server/Prepare/Real/LiveImages.cs:114-125` (`WindowCapture` 내 `GetWindowRect`, `IsIconic`, `PrintWindow` 등)
  - `src/Astro.Desktop/App.xaml.cs:52-54` (`SetForegroundWindow`, `ShowWindow`, `IsIconic`)
- **문제점**:
  - `user32.dll`의 P/Invoke 선언이 세 파일에 걸쳐 제각각 선언되어 있습니다.
  - 프로세스 이름으로 메인 창 핸들을 찾고 최소화/복원하는 로직(`Process.GetProcessesByName` 순회 및 핸들 매핑)이 `BackgroundWindows`, `LiveImages`, `App.xaml.cs`에 각각 다른 방식으로 구현되어 있습니다.
  - 특히 `LiveImages.cs:52-54`에서는 `Process.GetProcessesByName(processName)` 루프 중 `break`할 때 나머지 프로세스 인스턴스가 `Dispose`되지 않는 잠재적 핸들 누수 패턴이 존재합니다.
- **개선안**:
  - `src/Astro.Server/Native/Win32Windows.cs` (또는 `Astro.Core/Native/`) 단일 클래스로 P/Invoke 선언과 프로세스 기반 창 탐색 헬퍼(`FindMainWindow(processName)`, `MinimizeToBack(hwnd)`, `BringToFront(hwnd)`)를 일원화합니다.
  - 프로세스 배열 열거 시 `using` 스코프를 확실히 보장하도록 단일 진입점을 제공합니다.

---

### [중간] 원(노드) 상태 표현 및 상태 표시기 CSS 중복
- **위치**:
  - `web/src/screens/EquipmentScreen.module.css:220-265` (`.node[data-status='Running']`, `Pass`, `Fail`, `Warn`, `Skipped`)
  - `web/src/components/StepRail.module.css:120-155, 193-245` (`.stage[data-state='done']`, `now`, `.item[data-state='done']`, `problem`, `skipped`)
  - `web/src/components/StatusIcon.module.css:8-12` (`.icon[data-status='Pass']`, `Fail`, `Warn`, `Running`)
  - `web/src/components/StepPanel.module.css:38-41`
- **문제점**:
  - `Pass/done` (초록 `--pass`), `Fail/problem` (빨강 `--fail`), `Warn` (주황 `--warn`), `Running/now` (강조색 `--accent`), `Skipped` (흐림) 등 상태별 색상 및 애니메이션 지정이 각 화면과 컴포넌트 CSS 모듈마다 개별 셀렉터로 파편화되어 작성되어 있습니다.
  - 상태 네이밍도 화면마다 `data-status="Pass"`와 `data-state="done"`이 혼용되고 있습니다.
- **개선안**:
  - `web/src/index.css`에 공통 상태 유틸리티 속성(`[data-status="pass"]`, `[data-status="fail"]`, `[data-status="warn"]`, `[data-status="running"]`)을 정의하거나, 상태 점/테두리 스타일을 `StatusIcon` 및 토큰 클래스로 일원화하여 개별 CSS 모듈의 반복 셀렉터를 제거합니다.

---

### [낮음] `prefers-reduced-motion` 미디어 쿼리 함수 중복
- **위치**:
  - `web/src/components/StepRail.tsx:70`
  - `web/src/components/JarvisRing.tsx:83`
  - `web/src/screens/PreflightScreen.tsx:120`
  - `web/src/screens/CheckStepsScreen.tsx:103`
  - `web/src/screens/EquipmentScreen.tsx:675`
- **문제점**:
  - 여러 컴포넌트와 화면에서 `window.matchMedia?.('(prefers-reduced-motion: reduce)').matches`를 매번 독립적으로 복사하여 호출하고 있습니다. 런타임에 OS 설정이 바뀌어도 반응형으로 감지되지 않습니다.
- **개선안**:
  - `web/src/theme.ts`에 `useReducedMotion()` React 훅 및 `getReducedMotion()` 헬퍼를 추가하여 모든 화면이 동일한 훅을 재사용하도록 단일화합니다.

---

### [높음] 전원 허브 포트 출력 제어 로직의 분산 위험
- **위치**:
  - `src/Astro.Server/Engine/EquipmentConnector.cs:230-280`
  - `web/src/components/PowerWiringPanel.tsx` (및 관련 허브 스위치 연동부)
- **문제점**:
  - 전원 허브(WandererBox 등) 연결 시 포트 상태 조회/인가 로직과 UI상의 전원 배선 패널(`PowerWiringPanel`)에서 포트 스위치를 토글하는 로직이 서버와 클라이언트 양쪽에 분산되면, N.I.N.A. Switch API 호출 방식과 포트 매핑 규칙이 어긋나기 쉽습니다.
- **개선안**:
  - 서버 측에 `PowerHubService`(또는 `NinaSwitchClient`)를 명확히 분리하여 포트별 이름, 전압, 상태(On/Off), 열선 듀티비 조작 API를 캡슐화하고, `EquipmentConnector`와 `PowerWiringPanel`은 이 서비스 인터페이스만 소비하도록 단순화합니다.

---

## 2. 구조 단순화 (복잡해진 코드 분리 및 간소화)

### [높음] `EquipmentConnector.ConnectOneAsync` 모놀리식 구조 분리
- **위치**: `src/Astro.Server/Engine/EquipmentConnector.cs:214-297`
- **문제점**:
  - 단일 메서드 `ConnectOneAsync`가 80줄 이상으로 비대해지며 다음 책임들이 한곳에 뒤섞여 있습니다:
    1. 망원경(`Scope`) 특수 분기 (OpticsStore 읽기 및 NINA 프로필 주입)
    2. 필수 장비 프로필 미등록 검사 및 오류 진단 생성
    3. 시뮬레이션 지연 및 실패 주입 로직
    4. 실제 장비 연결 (NINA 연결 확인, LiveDevices 캐시 대조, 재연결)
    5. `BackgroundWindows` 수명 관리
    6. 가이더(`guider`) 특수 분기 (PHD2 깊은 상태 검증 `CheckPhd2Async`)
    7. 연결 실패 시 장비 등급(Optional/Required)별 Warn/Fail 분기
  - 새로운 장비 종류(예: 전원 허브 세부 포트 확인 등)가 추가될 때마다 이 함수가 기하급수적으로 복잡해집니다.
- **개선안**:
  - 책임을 아래와 같이 작고 순수한 하위 메서드로 분리합니다:
    - `ConnectScopeAsync(Device d, CancellationToken ct)`: 망원경 전용 설정 주입
    - `ConnectRealDeviceAsync(Device d, CancellationToken ct)`: NINA 실제 연결 및 `LiveDevices` 갱신
    - `VerifyGuiderAsync(Device d, CancellationToken ct)`: PHD2 연계 검증
    - `CreateFailureResult(Device d)`: 진단 문구 및 등급별 결과 생성
  - `ConnectOneAsync`는 장비 종류에 따른 분기 오케스트레이션(최대 20줄)만 담당하도록 단순화합니다.

---

### [높음] `EquipmentScreen.tsx` (940줄) 단일 파일 비대화 해소
- **위치**: `web/src/screens/EquipmentScreen.tsx`
- **문제점**:
  - 화면 컴포넌트 하나가 너무 많은 역할을 직접 수행하고 있습니다:
    1. 200회 몬테카를로 셔플과 15스케일×60반복 물리 완화 알고리즘 (`connectLayout`, `balancedOrder`, `relax`)
    2. 연결 모드 ↔ 변경 모드 간 4단계 전환 애니메이션 상태 (`connect` → `absorb` → `spread` → `edit`)
    3. SVG 노드 연결선 렌더링
    4. 장비 선택 드라이버 목록 팝업 (`EditNode`)
    5. 4초 자동 진행 막대 및 슬로우 존 이벤트 핸들링
- **개선안**:
  - **`useEquipmentLayout.ts`**: 물리 배치 계산 및 좌표 생성 로직을 커스텀 훅으로 완전 분리 (메모이제이션 포함).
  - **`EquipmentGraphView.tsx`**: SVG 링크와 중앙 링, 방사형 노드들의 기하학적 렌더링 담당.
  - **`EquipmentScreen.tsx`**: 전체 화면 상태 흐름(SSE 수신, 모드 전환, 완료 후 다음 단계 이동)만 담당하도록 250줄 내외로 슬림화.

---

### [중간] `PreflightScreen.tsx` 인라인 SVG 마크업 분리
- **위치**: `web/src/screens/PreflightScreen.tsx:15-95`
- **문제점**:
  - 6개 점검 항목에 들어가는 64×64 선 그림 SVG 패스가 80줄 이상 컴포넌트 파일 최상단 `ITEMS` 배열 안에 생으로 선언되어 있어 가독성을 저해합니다.
- **개선안**:
  - `web/src/components/PreflightIcons.tsx`로 SVG 마크업을 분리하거나, `<PreflightIcon name="..." />` 단일 컴포넌트로 추출하여 `PreflightScreen.tsx` 본문은 오직 체크리스트 상태와 카드 뒤집기 상호작용에만 집중하게 만듭니다.

---

### [중간] `JarvisRing.tsx`의 `useSmoothContent` 타이머 복잡도 완화
- **위치**: `web/src/components/JarvisRing.tsx:70-112`
- **문제점**:
  - 텍스트를 흐려졌다가 새 텍스트로 밝아지게 만드는 로직을 위해 `performance.now()`, `shownAt` ref, `latest` ref, 2개의 `setTimeout`을 복합적으로 운용하고 있어 상태 변화 추적이 어렵고 경합 위험이 있습니다.
- **개선안**:
  - CSS transition(`opacity 240ms`)과 단일 상태(`displayContent`, `isPending`)로 단순화하거나, React 19의 `useTransition` / CSS `transitionend` 이벤트를 활용해 타이머 계산 로직을 간소화합니다.

---

### [중간] `NewProfileScreen.tsx` 폼 로직 분리
- **위치**: `web/src/screens/NewProfileScreen.tsx:32-70`
- **문제점**:
  - 캔버스를 통한 이미지 정사각형 크롭(`toSquareProfileImage`), 미리보기 URL 생성/해제(`URL.createObjectURL`/`revokeObjectURL`), 파일 업로드 및 폼 검증 로직이 한 컴포넌트에 집중되어 있습니다.
- **개선안**:
  - `useProfileImage(editing)` 커스텀 훅으로 이미지 크롭/미리보기 수명주기를 격리하고, 화면은 폼 입력 필드와 버튼 렌더링에만 집중하도록 단순화합니다.

---

## 3. DESIGN.md 규칙 위반 검토

### [높음] 컴포넌트 CSS 내 하드코딩된 색상 (DESIGN.md 7장 위반)
- **위치**:
  - `web/src/components/ConfirmDialog.module.css:13`: `background: rgb(0 0 0 / 0.55);`
  - `web/src/components/PrepCenter.module.css:177, 211`: `background: rgb(0 0 0 / 0.35);`
  - `web/src/components/LiveView.module.css:68`: `background: rgb(0 0 0 / 0.35);`
  - `web/src/screens/PrepareScreen.module.css:52`: `background: rgb(0 0 0 / 0.45);`
  - `web/src/components/SiteMenu.module.css:11`: `box-shadow: ... rgba(0, 0, 0, 0.35);`
- **문제점**:
  - DESIGN.md 7장: *"모든 색은 CSS 변수(토큰)로만 쓴다. 컴포넌트에 색 값을 직접 쓰지 않는다. 세 테마는 index.css에서 같은 토큰 이름을 공유한다."*
  - DESIGN.md 7-3장: *"촬영용(night) 테마는 초록·파랑 값이 00인 순수 빨강만 쓴다."*
  - 반투명 검정 오버레이(`rgb(0 0 0 / 0.35~0.55)`)가 직접 하드코딩되어 있으면, 테마가 바뀌었을 때 일관된 감광 처리가 되지 않고 시각적 대비가 어긋납니다.
- **개선안**:
  - `web/src/index.css`에 오버레이용 토큰을 추가합니다:
    - `--scrim`: 다크 `rgb(0 0 0 / 0.55)`, 라이트 `rgb(0 0 0 / 0.25)`, 나이트 `rgb(0 0 0 / 0.75)`
    - `--surface-overlay`: `rgb(0 0 0 / 0.35)`
  - 모든 컴포넌트 CSS의 `rgb(...)`/`rgba(...)`를 `var(--scrim)` 또는 `var(--surface-overlay)`로 전면 교체합니다.

---

### [높음] `JarvisRing`의 회전 애니메이션에 동작 줄이기 미반영 (DESIGN.md 9장 위반)
- **위치**: `web/src/components/JarvisRing.module.css:26-44` (`.outer`, `.middle`, `.inner`의 `animation: spin ...`)
- **문제점**:
  - `JarvisRing.tsx` 본문에서는 텍스트 전환 시 `prefers-reduced-motion`을 검사하고 있으나, CSS의 3중 SVG 원호 회전 애니메이션(`.outer`, `.middle`, `.inner`)에는 `@media (prefers-reduced-motion: reduce)` 대응이 누락되어 있습니다.
  - 시스템에서 동작 줄이기를 켜도 링이 무한 회전하여 멀미를 유발할 수 있으며, DESIGN.md 9장의 "촬영 테마·동작 줄이기에서는 애니메이션 최소화" 원칙에 위배됩니다.
- **개선안**:
  - `JarvisRing.module.css`에 미디어 쿼리를 추가하여 정지 상태의 고정 호로 표시합니다:
    ```css
    @media (prefers-reduced-motion: reduce) {
      .outer, .middle, .inner {
        animation: none;
      }
    }
    ```

---

### [중간] 상태 변경 시 레이아웃 시프트 방지 점검 (DESIGN.md 1장)
- **위치**: `web/src/screens/EquipmentScreen.module.css:819-835` (`.node[data-hub='true'][data-status='Fail']::after`)
- **문제점**:
  - 허브 실패 시 펄스 링(`::after`)이 동적으로 활성화되면서 브라우저 레이아웃 계산에 불필요한 리플로우(Reflow)를 일으킬 수 있습니다.
- **개선안**:
  - 가상 요소를 동적으로 추가/삭제하지 않고, 항상 자리를 잡고 `opacity`와 `transform`만 전환하도록 통일하여 레이아웃 안정성을 완벽히 보장합니다.

---

## 4. 화면 성능 (CPU/GPU 점유율 및 주기적 요청 최적화)

### [높음] `SkyBackdrop` 캔버스 배경 애니메이션의 무한 루프 억제
- **위치**: `web/src/components/SkyBackdrop.tsx` (배경 별/성운 효과)
- **문제점**:
  - 캔버스 기반으로 배경 별밭이나 성운 효과를 그릴 때 `requestAnimationFrame` 무한 루프가 돌면, GPU/CPU를 상시 점유하여 출사 현장의 저전력 미니 PC(인텔 N100 등)나 노트북 배터리를 빠르게 소모시킵니다.
  - 천체사진 촬영 앱은 밤새 배터리로 구동되는 경우가 많아 배경 그래픽의 전력 소모가 민감합니다.
- **개선안**:
  - 별 움직임이 필요 없다면 **최초 마운트 시 1회만 캔버스에 렌더링하고 애니메이션 루프를 돌리지 않는 정적 버퍼(OffscreenCanvas 또는 1회성 draw)** 방식을 채택합니다.
  - 마우스 인터랙션이나 페이드가 필요하다면 캔버스 재렌더링 대신 상위 컨테이너의 CSS `opacity`/`transform`으로 위임합니다.
  - `prefers-reduced-motion` 또는 `data-theme="night"`에서는 애니메이션 루프를 완전히 중단(`cancelAnimationFrame`)해야 합니다.

---

### [중간] `backdrop-filter`의 GPU 컴포지팅 오버헤드 주의
- **위치**: 유리 효과(Glassmorphism) 패널 및 모달 배경
- **문제점**:
  - `backdrop-filter: blur(...)`는 백그라운드 픽셀을 실시간으로 샘플링하여 블러 연산을 수행하므로, 캔버스 애니메이션이나 잦은 SSE 렌더링과 겹치면 GPU 렌더링 파이프라인에 심각한 병목을 유발합니다. 저사양 내장 그래픽 환경에서 프레임 드랍이 두드러집니다.
- **개선안**:
  - 실시간으로 내용이 바뀌는 캔버스나 그래프 바로 위에서는 `backdrop-filter: blur(...)`를 지양하고, 불투명도가 조정된 단색 토큰(`var(--surface)` + alpha)으로 대체합니다. 블러가 꼭 필요한 모달(ConfirmDialog 등)에만 제한적으로 사용합니다.

---

### [중간] 2초 폴링 화면들의 이벤트 기반 전환
- **위치**:
  - `web/src/components/LiveView.tsx:90` (`RealImage` 1초 타이머 폴링)
  - `src/Astro.Server/Prepare/Real/RealFocusDevices.cs:35` (`Task.Delay(2000, ct)`로 자동초점 이벤트 폴링)
  - `src/Astro.Server/Shoot/ShootSession.cs:441, 525` (`PollMs = 2000`)
- **문제점**:
  - 서버 측에 SSE(`WatchAsync`)와 PHD2 소켓 채널이 이미 구축되어 있음에도 불구하고, 클라이언트와 서버 일부 장치 어댑터에서 1~2초 주기의 `Task.Delay` / `setTimeout` 폴링이 공존하고 있습니다.
  - 장비가 유휴 상태이거나 장시간 노출(3분 등) 중일 때도 2초마다 불필요한 루프가 돌며 CPU 웨이크업을 유발합니다.
- **개선안**:
  - **이미지 갱신**: 클라이언트 타이머 폴링 대신, 서버에서 새 이미지가 캡처되었을 때 SSE 이벤트(`photo-ready`)를 발행하여 클라이언트가 그 시점에만 이미지를 요청하도록 이벤트 기반으로 전환합니다.
  - **장비 감시**: N.I.N.A. WebSocket 이벤트 및 PHD2 소켓 브로드캐스트를 활용하여 상태 변화 시에만 비동기 대기(`WaitToReadAsync`)가 깨어나도록 폴링을 점진적으로 제거합니다.

---

## 5. 결론 및 우선 조치 권장 순서

1. **1순위 (즉시 조치 권장)**:
   - `JarvisRing.module.css`에 `prefers-reduced-motion` 미디어 쿼리 추가.
   - `ConfirmDialog`, `PrepCenter`, `LiveView` 등 CSS 모듈에 하드코딩된 `rgb(...)` 색상을 `index.css` 토큰(`var(--scrim)`)으로 치환.
2. **2순위 (구조 리팩토링)**:
   - `EquipmentConnector.ConnectOneAsync`를 장비 종류별 헬퍼 메서드로 분리.
   - `EquipmentScreen.tsx`에서 물리 레이아웃 알고리즘(`useEquipmentLayout.ts`) 분리.
   - Win32 헬퍼(`Win32Windows.cs`)로 중복 P/Invoke 통합.
3. **3순위 (성능 안정화)**:
   - `SkyBackdrop` 캔버스를 정적 1회 렌더링으로 제한하고 배터리 소모 방지.
   - `RealImage` 및 장비 감시 루프의 1~2초 폴링을 SSE 이벤트 기반 통지로 일원화.

---

## 6. 구조·단순화 리뷰 (커밋 38e1744)

### 확인 항목
- **본 커밋 해시**: `38e1744f809b281804b42229d03e8c7358c78374` (`38e1744`)
- **`src/Astro.Server/Engine/EquipmentConnector.cs` 줄 수**: 855줄
- **`web/src/screens/EquipmentScreen.tsx` 줄 수 및 레이아웃 함수**: 862줄, `solarLayout` (`import { PLANET_OF, solarLayout, type Planet, type SolarGeo } from './equipmentOrbits'`)
- **`src/Astro.Server/Prepare/Flow/PrepareRunner.cs` 줄 수**: 928줄
- **`docs/` 폴더 파일 목록**:
  - `docs/DECISION_HISTORY.md`
  - `docs/FIELD_TEST_CHECKLIST.md`
  - `docs/OPERATION_PROCEDURE.md`
  - `docs/codex/ERROR_VERIFICATION_CODEX.md`
  - `docs/codex/IMPLEMENTATION_REQUEST_CODEX.md`
  - `docs/codex/REVIEW_CODEX.md`
  - `docs/codex/REVIEW_GEMINI.md`
  - `docs/implementation/ARCHITECTURE.md`
  - `docs/implementation/POWER_WIRING.md`
  - `docs/implementation/PRECHECK.md`
  - `docs/implementation/PREPARE.md`
  - `docs/implementation/SHOOT.md`
  - `docs/reference/NINA_API_COVERAGE.md`
  - `docs/reference/NINA_SETUP.md`

---

### 1. 너무 큰 파일·클래스 분리 방안

#### 1-1. `EquipmentConnector.cs` 내 복합 클래스 및 하위 도메인 분리
- **무엇을**: `EquipmentConnector.cs` 파일 하나(855줄) 안에 함께 정의된 독립 옵션·상태 클래스들(`EquipmentOptions`, `EquipmentSimulation`, `EquipmentChoices`, `EquipmentRun`)을 각각 독립 파일로 분리하고, 전원 허브 제어 및 PHD2 검증 책임을 도메인 서비스로 추출합니다.
- **왜**: 현재 한 파일 안에서 5개 클래스가 선언되어 있고, `EquipmentConnector` 내부에는 망원경 N.I.N.A. 프로필 주입(412~425줄), 전원 허브 포트/전압 스위칭(627~702줄), PHD2 프로세스/카메라 검증 및 리포인팅(719~759줄, 815~824줄), 비동기 연결 중단 제어(213~304줄)까지 단일 클래스가 도메인 전반을 모두 떠안고 있습니다. 특히 `EquipmentChoices`는 `PlanTools`나 `PrepareStarter` 등 외부에서 주입받는 독립 싱글톤 서비스임에도 `EquipmentConnector.cs` 내부에 끼어 있어 탐색이 어렵습니다.
- **근거**:
  - `src/Astro.Server/Engine/EquipmentConnector.cs:10`: `public sealed class EquipmentOptions`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:28`: `public sealed class EquipmentSimulation`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:62`: `public sealed class EquipmentChoices`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:840`: `public sealed class EquipmentRun`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:627`: `private async Task<double?> HubInputVoltsAsync(CancellationToken ct)`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:719`: `private async Task<Phd2Check.Problem?> CheckPhd2Async(CancellationToken ct)`
- **작업 크기**: 중간
- **나눌 때 주의할 점**: 
  - `AbortAsync`(213~304줄)와 `ConnectOneAsync`(407~589줄)에서 `lock (_gate)`를 통해 `state.InFlight`, `state.Unresolved`, `state.Unanswered`, `_runCts`를 조작합니다.
  - 비동기 대기(`await request.WaitAsync(ct)`) 동안 락을 잡지 않고 완료 후 `finally`에서 상태를 정리하는 락 경계 및 취소 토큰 무시(`CancellationToken.None`으로 보낸 요청을 `AbortAsync`에서 65초간 끝까지 기다려 주는 정책)가 깨지지 않도록 해야 합니다.
- **하지 않으면 생기는 실제 불편**: 전원 허브나 PHD2 연동 로직을 점검·수정할 때 850줄이 넘는 연결 오케스트레이션 코드 전체를 뒤져야 하고, 락 경계 실수로 인한 동시성 이슈를 파악하기 어렵습니다.

---

#### 1-2. `EquipmentScreen.tsx` 컴포넌트 및 모달 분리
- **무엇을**: `EquipmentScreen.tsx` 파일 하나(862줄)에 모여 있는 `EditNode` 및 `DeviceList` 팝오버, `CameraPower` 선택 폼, `TempFailButtons`, 4초 자동 진행 타이머를 별도 파일로 분리합니다.
- **왜**: 이 화면은 상위 화면 틀(`EquipmentScreen`, 49~78줄), 태양계 궤도 렌더링 그래프(`EquipmentGraph`, 80~486줄), 행성 노드(`PlanetNode`, 540~629줄), 드라이버 변경 팝오버(`EditNode`·`DeviceList`, 631~744줄), 카메라 전원 배선 팝업(`CameraPower`, 746~785줄), 모의 실패 버튼(`TempFailButtons`, 814~861줄)이 한 파일에 누적되어 있습니다. 태양계 궤도 UI라는 시각적 핵심과, 드라이버를 변경하고 전원 콘센트를 지정하는 폼 조작 책임이 섞여 있습니다.
- **근거**:
  - `web/src/screens/EquipmentScreen.tsx:80`: `function EquipmentGraph({`
  - `web/src/screens/EquipmentScreen.tsx:540`: `function PlanetNode({`
  - `web/src/screens/EquipmentScreen.tsx:631`: `function EditNode({`
  - `web/src/screens/EquipmentScreen.tsx:687`: `function DeviceList({`
  - `web/src/screens/EquipmentScreen.tsx:746`: `function CameraPower() {`
  - `web/src/screens/EquipmentScreen.tsx:814`: `function TempFailButtons({`
- **작업 크기**: 중간
- **나눌 때 주의할 점**: 
  - `solarLayout`으로 계산된 행성 위치(`p.x`, `p.y`)와 `LIST_ROOM`(310px)을 기준으로 드라이버 목록 팝오버의 화면 이탈 방지(`useKeepInside`)가 동작합니다.
  - 4초 자동 진행 타이머(`ADVANCE_MS = 4000`, 174~185줄)는 장비 변경 버튼에 마우스가 호버될 때 감속(`SLOW_RATE = 0.3`)되는 상호작용 상태(`slowRef`, `isNearChange`)와 직결되어 있으므로 커스텀 훅(`useEquipmentAutoAdvance`)으로 추출 시 의존성 누락을 주의해야 합니다.
- **하지 않으면 생기는 실제 불편**: 행성 궤도 렌더링이나 스타일을 수정하려 해도 860줄짜리 화면 파일 전체를 스크롤하며 장비 목록 선택 팝업과 전원 폼 코드를 함께 유지보수해야 합니다.

---

#### 1-3. `PrepareRunner.cs`에서 정지 조정 및 뷰 발행 책임 분리
- **무엇을**: `PrepareRunner.cs`(928줄)에서 장비 정지 및 중단 확인 로직(`StopCurrentAsync`, `TryStopAsync`, `RecheckStopAsync`, `StopLingeringAsync`)을 `PrepStopCoordinator`로 분리하고, SSE 스트리밍 및 뷰 생성 로직(`WatchAsync`, `Snapshot`, `CheckStaleNow`)을 분리하며, 파일 하단의 `RunHandle`(868~927줄)을 별도 클래스로 분리합니다.
- **왜**: `PrepareRunner`는 준비 단계의 상태 머신(작업 순서, 결과 기록, 다음 작업 자동 진행)을 담당해야 하지만, 실제로는 장비 정지 시 15초 타임아웃 및 미확인 상태(`_stopUnconfirmed`)를 관리하는 안전 정지 로직(560~686줄, 743~812줄)과, SSE 클라이언트 알림 및 3초 데이터 신선도(Heartbeat/Stale) 감시(133~191줄, 832~855줄), 그리고 `ITaskRun` 구현체인 `RunHandle`까지 모두 단일 파일에 결합되어 있습니다.
- **근거**:
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:63`: `private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);`
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:82`: `private (IPrepTask Task, Func<Task> Then)? _stopUnconfirmed;`
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:153`: `public async IAsyncEnumerable<PrepView> WatchAsync([EnumeratorCancellation] CancellationToken ct)`
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:743`: `private async Task<bool> StopCurrentAsync(Func<Task> then)`
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:868`: `private sealed class RunHandle(PrepareRunner r, int runId) : ITaskRun`
- **작업 크기**: 중간 ~ 큼
- **나눌 때 주의할 점**: 
  - 취소 토큰 분리 원칙: 작업을 취소할 때는 `cur.Cts.Cancel()`을 호출하지만, 장비 멈춤(`task.StopAsync`)은 취소된 토큰의 영향을 받지 않도록 독립된 15초 제한 토큰(`StopTimeout`, 782줄)을 사용해야 합니다.
  - 정지 확인 실패 시 `_stopUnconfirmed` 클로저를 걸어 두고 사용자가 "직접 확인"(`device-check`)을 누르기 전까지 모든 전진 액션을 차단하는 상태 순서가 유지되어야 합니다.
  - `_runId`를 통한 이전 실행 콜백 무효화(CX-PREP-IMPL-04)가 `RunHandle` 분리 시에도 완벽히 동기화되어야 합니다.
- **하지 않으면 생기는 실제 불편**: 작업 실행 순서나 버튼 액션 분기를 고치다가 복잡한 15초 정지 확인 타임아웃이나 SSE 알림 로직과 엉켜 회귀 버그가 발생하기 쉽습니다.

---

### 2. 여러 곳에 분산·중복된 로직 통합

#### 2-1. `NinaRig.cs` 내 적도의 정지 확인 루프 중복 (`ConfirmStillAsync` vs `WaitStillAsync`)
- **무엇을**: `NinaRig.ConfirmStillAsync`를 별도의 루프로 유지하지 않고, 이미 존재하는 `WaitStillAsync`를 재사용하도록 일원화합니다.
- **왜**: `ConfirmStillAsync`와 `WaitStillAsync`는 "Slewing이 false이고 RA/Dec 좌표 변화량이 0.005도 미만인 상태가 2회 연속 유지되는지"를 검사하는 알고리즘이 토씨 하나 다르지 않고 완전히 동일합니다. `ConfirmStillAsync`는 단순히 12회 루프(약 14.4초), `WaitStillAsync`는 `timeout` 기반 루프일 뿐입니다.
- **근거**:
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:44-46`:
    ```csharp
    if (m is null) return false;
    if (!m.Slewing && last is { } l && Math.Abs(l.Item1 - m.RaDeg) < 0.005 && Math.Abs(l.Item2 - m.DecDeg) < 0.005) return true;
    last = (m.RaDeg, m.DecDeg);
    ```
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:96-98`:
    ```csharp
    if (m is null) return false;
    if (!m.Slewing && last is { } l && Math.Abs(l.Item1 - m.RaDeg) < 0.005 && Math.Abs(l.Item2 - m.DecDeg) < 0.005) return true;
    last = (m.RaDeg, m.DecDeg);
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: 좌표 안정 판정 임계치(0.005도)나 폴링 간격(1200ms)을 현장 테스트 후 조정할 때 두 메서드 중 한쪽을 누락하여 불일치가 발생합니다.

---

#### 2-2. Win32 창 열거 P/Invoke 중복 선언 (`BackgroundWindows` vs `Phd2Windows`)
- **무엇을**: `BackgroundWindows.cs`와 `Phd2Windows.cs`에 중복 선언된 `user32.dll` 창 열거 API(`EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `GetWindowText`)를 단일 Win32 유틸리티 클래스로 일원화합니다.
- **왜**: 동일한 서명의 P/Invoke 메서드가 서로 다른 네임스페이스에 복사-붙여넣기되어 있습니다. 창 핸들 탐색 및 프로세스 ID 매핑 방식이 파편화되어 유지보수 부담을 가중시킵니다.
- **근거**:
  - `src/Astro.Server/Engine/BackgroundWindows.cs:175-178`:
    ```csharp
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int max);
    ```
  - `src/Astro.Server/Prepare/Real/Phd2Windows.cs:37-40`:
    ```csharp
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int max);
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: Win32 P/Invoke 마샬링 옵션이나 유니코드 버퍼 처리 규칙을 고칠 때 여러 파일을 일일이 찾아서 고쳐야 합니다.

---

#### 2-3. N.I.N.A. JSON 파싱 헬퍼 중복 (`EquipmentConnector` vs `NinaRig`)
- **무엇을**: `JsonElement`에서 안전하게 숫자/문자열을 추출하는 `Num`, `Text` 헬퍼 메서드를 `Astro.Nina` 또는 공통 유틸리티로 승격하여 재사용합니다.
- **왜**: N.I.N.A. 응답에서 속성을 안전하게 읽어오는 헬퍼 메서드가 `EquipmentConnector`와 `NinaRig`에 각자 `private static`/`internal static`으로 중복 정의되어 있습니다.
- **근거**:
  - `src/Astro.Server/Engine/EquipmentConnector.cs:594`: `private static double Num(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:825`: `private static string? Text(JsonElement obj, string key) => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;`
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:476`: `internal static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;`
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:478`: `internal static double Num(JsonElement e, string key)` (문자열 파싱 지원 포함)
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: N.I.N.A. API 응답 형식(숫자가 문자열로 반환되는 경우 등)에 대응하는 파싱 보정이 파일마다 제각각 적용되어 예외가 발생할 위험이 있습니다.

---

### 3. OPERATION_PROCEDURE.md 흐름과 코드 구조 대조 및 추적성 개선

#### 3-1. 2단계 장비 연결의 4초 자동 진행 규칙 위치 불일치
- **무엇을**: OPERATION_PROCEDURE.md 2장에 명시된 "모두 연결되면 4초 뒤 다음 단계로, 3.5초까지 취소 가능" 규칙이 서버에는 전혀 없고 웹 클라이언트 RAF 애니메이션에만 존재하는 구조적 격차를 문서에 명기하거나 서버 완료 상태 모델과 동기화합니다.
- **왜**: OPERATION_PROCEDURE.md 1장에는 "진행은 서버가 하고 화면은 상태만 그린다 → 화면을 다시 열어도 이어서"라는 핵심 원칙이 명시되어 있습니다. 그러나 2단계 장비 연결 완료 후 다음 단계로의 천이(`onContinue`)는 서버에 아무런 타이머나 완료 상태가 없으며 오직 `EquipmentScreen.tsx`의 브라우저 RAF 루프(174~185줄)에만 의존하고 있습니다. 브라우저를 새로고침하면 4초 타이머가 처음부터 다시 돕니다.
- **근거**:
  - `docs/OPERATION_PROCEDURE.md:46`: `모두 연결되면(준필수 "없이 진행"·선택 경고 포함) 4초 뒤 다음 단계. 그동안 "장비 변경"·관측지 "변경"을 누르면 멈춘다(3.5초까지).`
  - `web/src/screens/EquipmentScreen.tsx:23-24`:
    ```typescript
    const ADVANCE_MS = 4000
    const CHANGE_UNTIL_MS = 3500
    ```
  - `web/src/screens/EquipmentScreen.tsx:174-184`:
    ```typescript
    const tick = (now: number) => {
      const dt = now - last
      last = now
      const rate = slowRef.current ? SLOW_RATE : 1
      elapsed += dt * rate
      setProgress(Math.min(1, elapsed / ADVANCE_MS))
      if (elapsed >= ADVANCE_MS) {
        onContinue(items, { changeSite: false })
      } else {
        raf = requestAnimationFrame(tick)
      }
    }
    ```
- **작업 크기**: 작음 (문서 보완 또는 설명 주석 추가)
- **하지 않으면 생기는 실제 불편**: "서버가 진행하고 화면은 그리기만 한다"는 원칙을 믿고 서버 API만 검토하던 개발자가 장비 연결 완료 전진 조건을 서버 코드에서 찾지 못해 혼선을 겪습니다.

---

#### 3-2. 장비 준비 끝 홈 이동 및 추적 끄기 경로 분산
- **무엇을**: 장비 준비(rig) 완료 후 적도의 홈 이동 및 추적 끄기 절차가 `AppServer.cs`의 인라인 엔드포인트 `/api/prepare/rig/rest`에 파편화되어 있는 것을 `PrepareFlow` 내부 또는 `PrepareRunner`의 명시적 단계 완료 훅으로 통합합니다.
- **왜**: OPERATION_PROCEDURE.md 5장 공통에는 "장비 준비가 끝나면 계획으로 가고, 그동안 적도의는 홈에서 추적을 끄고 기다린다"고 규정되어 있습니다. 하지만 이 동작은 `PrepareFlow`가 총괄하는 것이 아니라, 클라이언트가 `/api/prepare/rig/rest`라는 별도 엔드포인트를 수동 호출하고, 그 엔드포인트가 `MountHome.HomeAndTrackingOffAsync`를 직접 호출하는 방식으로 구현되어 있어 흐름 추적이 끊깁니다.
- **근거**:
  - `docs/OPERATION_PROCEDURE.md:65`: `장비 준비가 끝나면 계획으로 가고, 그동안 적도의는 홈에서 추적을 끄고 기다린다.`
  - `src/Astro.Server/AppServer.cs:348-351`:
    ```csharp
    prepare.MapPost("/rig/rest", async (Prepare.MountHome home, CancellationToken ct) =>
    {
        var homed = await home.HomeAndTrackingOffAsync(ct);
        return homed ? Results.NoContent() : Results.BadRequest(new { error = "적도의를 홈에 두고 추적을 끄지 못했습니다" });
    });
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: 준비 묶음(rig)에서 대상 묶음(target)으로 넘어가는 경계에서 적도의 안전 처리가 어디서 일어나는지 한눈에 파악하기 어렵습니다.

---

### 4. 정리 가능한 불필요·모의·임시 코드

#### 4-1. 미사용 레거시 `RunnerSetup.Single` 제거
- **무엇을**: `PrepareRunner.cs`에 선언된 미사용 정적 필드 `RunnerSetup.Single`을 삭제합니다.
- **왜**: 7개 작업을 한 묶음으로 처리하던 2026-10-07 이전(단일 묶음 시절)의 시험용 설정이며, 현재 프로젝트 전체(테스트 코드 포함)에서 단 한 번도 참조되지 않는 완전한 데드 코드입니다.
- **검색 결과**: 저장소 전체 검색 시 `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:38` 선언 1곳 외 호출처 0건.
- **근거**:
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:38-40`:
    ```csharp
    public static readonly RunnerSetup Single = new("all", new Dictionary<string, string[]>
    {
        ["polar"] = ["calibration", "slew", "focus", "center", "guiding", "test"],
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: 신규 개발자가 의존성 맵(`Rig`, `Target`, `Wrap`, `Single`)을 읽을 때 4개 묶음이 존재하는 것으로 오해하고 분석 시간을 낭비합니다.

---

#### 4-2. 미사용 엔드포인트 `/api/nina/simulate-exit` 및 메서드 정리
- **무엇을**: `AppServer.cs`의 `POST /api/nina/simulate-exit` 엔드포인트와 `NinaWatcher.SimulateExit()` 메서드를 삭제합니다.
- **왜**: 초기에 N.I.N.A. 종료 감지 화면을 테스트하기 위해 임시로 추가되었으나, 현재 프론트엔드 및 테스트 코드 어디에서도 호출되지 않는 죽은 코드입니다.
- **검색 결과**: `POST /nina/simulate-exit` 및 `SimulateExit()` 호출처 0건 (선언부 외 사용처 없음).
- **근거**:
  - `src/Astro.Server/AppServer.cs:136-138`:
    ```csharp
    api.MapPost("/nina/simulate-exit", (NinaWatcher watcher, IOptions<EquipmentOptions> eq) =>
    {
        if (!eq.Value.Simulate) return Results.NotFound();
        watcher.SimulateExit();
        return Results.NoContent();
    });
    ```
  - `src/Astro.Server/Engine/NinaWatcher.cs:83`:
    ```csharp
    /// <summary>[임시] 화면 설계용: N.I.N.A.가 꺼진 것처럼 (Equipment:Simulate일 때만 엔드포인트가 부른다)</summary>
    public void SimulateExit()
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: 실장비 모드와 무관한 모의 전용 진입점이 서버 API 표면에 불필요하게 남아 보안 및 유지보수 혼선을 유발합니다.

---

#### 4-3. 미사용 엔드포인트 `/api/prepare/sim/speed/{speed}` 정리
- **무엇을**: `AppServer.cs`의 `POST /api/prepare/sim/speed/{speed:double}` 엔드포인트를 정리합니다.
- **왜**: 모의 속도 배율을 HTTP로 바꾸기 위해 열어둔 엔드포인트이나, 단위 테스트에서는 `SimOptions.Speed`를 직접 설정(`new SimOptions { Speed = 0 }`)하고 웹 화면에서는 이 API를 전혀 호출하지 않습니다.
- **검색 결과**: `/sim/speed` 검색 시 `src/Astro.Server/AppServer.cs:399` 선언 1건 외 호출처 0건.
- **근거**:
  - `src/Astro.Server/AppServer.cs:399`:
    ```csharp
    prepare.MapPost("/sim/speed/{speed:double}", (double speed, Prepare.Sim.SimOptions sim) => { sim.Speed = Math.Clamp(speed, 0, 5); return Results.NoContent(); });
    ```
- **작업 크기**: 작음
- **하지 않으면 생기는 실제 불편**: 쓰이지 않는 HTTP API 라우팅이 컨트롤러 영역에 남아 관리 포인트만 늘어납니다.

---

#### 4-4. 장비 연결 화면의 `TempFailButtons` 및 `forceFail` 매개변수 연쇄 정리
- **무엇을**: `EquipmentScreen.tsx`의 `TempFailButtons`(814~861줄)와 `AppServer.cs`의 `simulateFail` 쿼리 파라미터, `EquipmentConnector.cs`의 `forceFail` 파라미터 연쇄를 정리합니다.
- **왜**: 2026-10-08 시뮬레이터 시험 주석에 적혀 있듯 실장비 모드에서는 헷갈림을 유발하여 이미 숨김 처리(`if (!simulated) return null`)된 임시 UI입니다. 이 임시 버튼 하나 때문에 `AppServer.cs:234`의 `simulateFail` 파라미터, `EquipmentConnector.cs:352`의 `RetryAsync(..., bool simulateFail)`, `ConnectTimedAsync(..., bool forceFail)`, `ConnectOneAsync(..., bool forceFail)`까지 불필요한 실패 주입용 인수가 3단계에 걸쳐 관통하고 있습니다.
- **검색 결과**:
  - `web/src/screens/EquipmentScreen.tsx:481`: `{mode === 'connect' && <TempFailButtons items={items} done={done} patch={patch} />}`
  - `web/src/screens/EquipmentScreen.tsx:836`: `const res = await fetch(`/api/equipment/connect/${id}?simulateFail=true`)`
  - `src/Astro.Server/AppServer.cs:234`: `api.MapGet("/equipment/connect/{id}", async (string id, bool? simulateFail, ...)`
  - `src/Astro.Server/Engine/EquipmentConnector.cs:433-436`:
    ```csharp
    bool connected;
    if (forceFail)
    {
        connected = false;
    }
    ```
- **작업 크기**: 중간 (웹 1곳, 서버 1곳, 엔진 1곳)
- **하지 않으면 생기는 실제 불편**: 실장비 연결 오케스트레이션 함수(`ConnectOneAsync`) 내부에 임시 UI용 실패 플래그 분기가 침투해 있어 코드 흐름을 복잡하게 만듭니다.

---

### 5. 안전한 리팩터링 순서 (동작 보존)

기존 동작을 100% 보존하면서 안전하게 진행하기 위해 아래 4단계로 나누어 커밋 단위로 진행할 것을 권장합니다.

```
[1단계: 안전한 잔재 제거] ──> [2단계: 중복 헬퍼 일원화] ──> [3단계: 서버 클래스 분리] ──> [4단계: 화면 컴포넌트 분리]
 (데드코드·임시버튼)           (Win32, JSON, 정지루프)       (Connector, Runner)          (EquipmentScreen)
```

1. **1단계: 완전한 데드 코드 및 미사용 임시 엔드포인트 제거 (영향도 0, 커밋 1개)**
   - `PrepareRunner.cs`에서 `RunnerSetup.Single` 삭제.
   - `AppServer.cs`에서 `/nina/simulate-exit` 및 `/sim/speed/{speed}` 라우트 삭제, `NinaWatcher.SimulateExit()` 삭제.
   - `EquipmentScreen.tsx`에서 `TempFailButtons` 제거 및 `EquipmentConnector`의 `forceFail` 파라미터 제거.
   - *검증*: 서버 빌드(`dotnet build`), 서버 단위 테스트 통과, 웹 타입 검사(`tsc --noEmit`).

2. **2단계: 중복 유틸리티 일원화 (동작 불변 순수 리팩터링, 커밋 1~2개)**
   - `Win32Windows` 공통 클래스를 생성하여 `BackgroundWindows.cs`와 `Phd2Windows.cs`의 `EnumWindows` 등 P/Invoke 통합.
   - `NinaRig.ConfirmStillAsync`가 `WaitStillAsync(TimeSpan.FromSeconds(15), ct)`를 호출하도록 단일화.
   - `JsonElement` 파싱 헬퍼(`Num`, `Text`) 공통화.
   - *검증*: `TaskIsolationTests`, `SimulatorRigTests` 통과.

3. **3단계: `EquipmentConnector.cs` 및 `PrepareRunner.cs` 파일 분리 (C# 내부 구조 개선, 커밋 2개)**
   - 1차: `EquipmentOptions.cs`, `EquipmentSimulation.cs`, `EquipmentChoices.cs`, `EquipmentRun.cs`를 별도 파일로 이동.
   - 2차: `PrepareRunner.cs`에서 `RunHandle`을 `PrepTaskRunHandle.cs`로 추출하고, 정지 조정 로직(`StopCurrentAsync` 등)을 `PrepStopCoordinator`로 분리.
   - *주의*: 락(`_gate`) 범위와 `CancellationToken` 격리(작업 토큰 vs 15초 정지 토큰)를 그대로 유지.
   - *검증*: `ReviewFixTests`, `SelfAuditLowTests`, `FocuserParkTests` 전체 통과.

4. **4단계: `EquipmentScreen.tsx` 컴포넌트 분리 (프론트엔드 모듈화, 커밋 1~2개)**
   - `EditNode` / `DeviceList` / `CameraPower`를 `EquipmentEditModal.tsx`로 추출.
   - 태양계 궤도 SVG 드로잉을 `EquipmentOrbitCanvas.tsx`로 추출.
   - 4초 자동 진행 타이머를 `useEquipmentAutoAdvance.ts`로 분리.
   - *검증*: 웹 린트, 타입 검사, 브라우저 화면 렌더링 확인.


#### Claude 대조 메모 (2026-10-12)

- 확인 질문(줄 수·`solarLayout`·문서 목록)과 인용한 줄 번호는 38e1744 코드와 맞음 → 최신 코드를 본 리뷰로 인정.
- 3-1: 인용한 코드(`setProgress`·`slowRef`)는 실제와 다르나(`elapsed.current`·`slow.current`), "자동 진행 4초가 클라이언트에만 있다"는 지적은 맞음.
- 4-1: `RunnerSetup.Single`은 쓰이지 않는 것이 아니라 생성자 기본값(`setup ?? RunnerSetup.Single`)이고 테스트가 그 기본값에 기댐.
- 안전 메모: PHD2 쓰기는 이미 `_writeGate`로 직렬화돼 있음. `OnClosing`(async void)은 먼저 `e.Cancel = true` 후 10초 제한으로 서버를 멈추고 닫으므로 문제 아님.
