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

---

## 7. 큰 파일 14개 분리 실행 계획 · DESIGN.md 및 문서 대조 · 현장 성능 및 순수 함수 테스트 점검 (기준 커밋 2a0428f) · 2026-10-16 · [Gemini]

- **읽은 범위**: 업로드된 저장소 압축본(`Astrophoto_Assistant-2a0428f286effa17d0a1dcf48dfa8bf61fc8636a`) 전체 — `AGENTS.md`, `DESIGN.md`, `HISTORY.md`, `docs/OPERATION_PROCEDURE.md`, `docs/codex/REVIEW_GEMINI.md`, `docs/codex/REVIEW_REQUEST_GEMINI.md`, `docs/implementation/{ARCHITECTURE,PREPARE,SHOOT,PRECHECK,POWER_WIRING}.md`, 백엔드 `src/Astro.Core` · `src/Astro.Nina` · `src/Astro.Server` · `src/Astro.Desktop` 전체 `.cs`/`.xaml` 파일, 프론트엔드 `web/src` 전체 `.tsx`/`.ts`/`.css`/`.svg` 파일, 테스트 `tests/Astro.Server.Tests` 및 `web/src/**/*.test.ts` 전체.
- **읽지 못한 파일**: **0개** (저장소 전체 열람 확인).

### A. 큰 파일 분리 실행 계획 (400줄 넘는 파일 14개 + `WrapDevices.cs` 폴더 이동 통합)

#### GM-7-A-01 · `ShootSession.cs` (1003줄) → 촬영 루프 · 가이드/이슬 감시 · 자오선 반전/자동초점 · 기록/투영 4단 분리

- **무엇**: `src/Astro.Server/Shoot/ShootSession.cs`(1003줄)는 촬영 상태 기계·SSE 알림(`ShootSession`), 한 장 노출/등급/제외/디더링 루프, 3초 주기 백그라운드 가이드·추적 감시(`WatchGuideAsync`) 및 복구(`RecoverGuideAsync`), 이슬·열선 점검(`CheckDewAsync`), 자오선 반전(`FlipIfDueAsync`/`RunFlipAsync`)과 기온·HFR 자동초점(`RefocusIfDueAsync`), 오늘 밤 결과 누적(`SaveResult`)과 요약(`Summary`)을 한 클래스 안에 모두 담고 있다.
- **왜**: `_gate` 잠금 안에서 갱신되는 촬영 상태(`_mode`, `_pause`, `_ask`, `_guideStopped`, `_mountStopped`)와 장비 제어 코루틴(`RunAsync` ↔ `WatchGuideAsync`)이 한 파일에서 뒤섞여 있어, 반전·가이드 복구·종료 정지 확인 중 한 곳을 손볼 때 잠금 경계와 취소 토큰(`_cts`, `_watchStop`, `exposeCts`)의 상호작용을 추적하기 어렵다.
- **근거**:
  - `src/Astro.Server/Shoot/ShootSession.cs:11-36`: `ShootView`, `NightShootResult`, `TargetShootSummary` 등 외부 공개 계약 레코드.
  - `src/Astro.Server/Shoot/ShootSession.cs:46-88`: `_gate`, `_waiters`, `_cts`, `_run`, `_tally`, `_pausedSeconds`, `_notMoved` 등 30여 개 상태 필드.
  - `src/Astro.Server/Shoot/ShootSession.cs:316-597`: `ShouldWatchGuide`, `WatchGuideAsync`, `CheckDewAsync`, `WatchTrackingOnlyAsync`, `RecoverGuideAsync` (가이드·추적·이슬 감시 및 복구 282줄).
  - `src/Astro.Server/Shoot/ShootSession.cs:666-856`: `NeedFlip`, `FlipMinutes`, `FlipIfDueAsync`, `RunFlipAsync`, `RefocusIfDueAsync` (반전·초점 191줄).
  - `src/Astro.Server/Shoot/ShootSession.cs:909-1002`: `SaveResult`, `Summary` (오늘 밤 누적 및 마무리 요약 생성 94줄).
- **제안**:
  1. **공개 계약 분리 (`ShootModels.cs`, 약 50줄)**: `ShootMode`, `ShootView`, `NightShootResult`, `TargetShootSummary`(`11-36줄`)를 별도 파일로 추출한다.
  2. **결과 누적·요약 분리 (`ShootLedger.cs` 또는 `partial class ShootSession.Summary.cs`, 약 120줄)**: `_tally`, `_pausedSeconds`, `_notMoved`, `_lastFolder`, `_good`, `_excluded` 집계와 `SaveResult`(`909-951줄`), `Summary`(`954-1002줄`), `PlanKey`(`953줄`)를 분리한다.
  3. **가이드·이슬·추적 감시 분리 (`ShootSession.GuideMonitor.cs` partial 또는 `ShootGuideMonitor`, 약 280줄)**: `ShouldWatchGuide`, `WatchGuideAsync`, `CheckDewAsync`, `WatchTrackingOnlyAsync`, `AddPaused`, `NotePaused`, `ClearPauseNote`, `NextExposureMs`, `RecoverGuideAsync`(`316-597줄`)를 분리한다.
     - **공유 상태/잠금 경계**: `_snrBaseline`, `_hfdBaseline`, `_lastGuideSteps`, `_faintRaised`, `_dewWarnedAt`, `_pauseStartedAt`은 감시 전용 필드로 두고, 화면 상태 변경은 `Set(Action)` 헬퍼와 `AbortCurrentExposure()`만 호출하게 한다.
  4. **자오선 반전·초점 재조정 분리 (`ShootSession.Maintenance.cs` partial, 약 200줄)**: `NeedFlip`, `FlipMinutes`, `FlipIfDueAsync`, `RunFlipAsync`, `RefocusIfDueAsync`(`666-856줄`)를 분리한다.
  5. **본체 (`ShootSession.cs`, 약 350줄)**: 시작/중단/종료 대기(`Start`, `Stop`, `RecheckStopAsync`, `StopForQuitAsync`, `BlocksNext`), SSE 대기열(`WatchAsync`, `View`, `Set`), 주 촬영 루프(`RunAsync`, `TakeFrameAsync`, `DitherIfDueAsync`)만 남긴다.
  - **단계별 순서**: ① `ShootModels.cs` 추출 → ② partial 클래스로 `ShootSession.GuideMonitor.cs` · `ShootSession.Maintenance.cs` · `ShootSession.Summary.cs` 파일 분리 (동작 변경 0) → ③ 감시 전용 필드를 내부 상태 구조체로 묶기.
  - **검증 테스트**: `tests/Astro.Server.Tests/Prepare/ShootAndWrapTests.cs`, `tests/Astro.Server.Tests/Prepare/ReviewFixTests.cs`, `tests/Astro.Server.Tests/Prepare/QuitSafetyTests.cs`, `tests/Astro.Server.Tests/Prepare/SelfAuditLowTests.cs`.
- **작업 크기**: 중간
- **실사용 등급**: 겪음 — 파일이 1,000줄을 넘어 촬영 중 가이드 복구·반전·정지 확인 수정을 할 때 잠금(`_gate`)과 취소 토큰 범위를 한눈에 파악하기 어렵다.

#### GM-7-A-02 · `PrepareRunner.cs` (995줄) → 묶음 설정/의존표 · `TaskRun` 컨텍스트 · 종료/일시중단/임시 건너뛰기 분리

- **무엇**: `src/Astro.Server/Prepare/Flow/PrepareRunner.cs`(995줄)에 `RunnerSetup` 설정 레코드(`rig`/`target`/`wrap` 문구), 작업 의존표와 재실행/일시중단(`SuspendAsync`, `RedoAsync`), 이어서 하기 복원(`StartResumedAsync`), 임시 건너뛰기/모의 실패(`TempSkipAsync`, `TempDone`), 종료용 정지(`StopForQuitAsync`), 주 실행 루프(`LoopAsync`, `VerifyEndStateAsync`), 화면 스냅샷 생성(`View`, `CurrentView`, `FreshnessOf`), 내부 클래스 `TaskRun`(`ITaskRun` 구현)이 한 파일에 모여 있다.
- **왜**: 핵심 실행 엔진(`LoopAsync` + `VerifyEndStateAsync` + `StopTasksAsync`)은 약 350줄인데, 임시 건너뛰기 가짜 결과 생성(`TempSkipAsync`/`TempDone` 145줄)과 `TaskRun` 구현(165줄), `RunnerSetup`(45줄)이 함께 있어 정작 중요한 작업 전환·정지 확인 흐름을 읽기 어렵다.
- **근거**:
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:7-50`: `RunnerSetup` (`Rig`, `Target`, `Wrap` 정적 설정).
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:270-413`: `CanTempSkip`, `TempSkipTarget`, `TempSkipAsync`, `TempDone` (임시 건너뛰기·모의 실패 144줄).
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:829-994`: `private sealed class TaskRun` (`ITaskRun` 구현 166줄).
- **제안**:
  1. **`RunnerSetup.cs` 분리 (약 55줄)**: `RunnerSetup` 레코드(`7-50줄`)와 `Dependents` 의존표(`59-72줄`)를 `src/Astro.Server/Prepare/Flow/RunnerSetup.cs`로 옮긴다.
  2. **`PrepareRunner.TempSkip.cs` partial 분리 (약 150줄)**: `CanTempSkip`, `TempSkipTarget`, `TempSkipAsync`, `TempDone`(`270-413줄`)을 별도 partial 파일로 격리한다 (추후 `[임시]` 기능 제거 시 파일 하나만 지우면 되도록).
  3. **`PrepareRunner.TaskRun.cs` partial 분리 (약 175줄)**: `TaskRun` 내부 클래스(`829-994줄`)를 별도 partial 파일로 분리한다 (`_gate`, `_currentRunId`, `Bump()`만 공유).
  4. **본체 (`PrepareRunner.cs`, 약 600줄)**: 시작/이어서 하기/중단/종료 정지/재실행과 `LoopAsync` · `VerifyEndStateAsync` · `View()`만 유지한다.
  - **단계별 순서**: ① `RunnerSetup.cs` 분리 → ② `PrepareRunner.TempSkip.cs` partial 분리 → ③ `PrepareRunner.TaskRun.cs` partial 분리.
  - **검증 테스트**: `tests/Astro.Server.Tests/Prepare/RunnerTests.cs`, `PrepareFlowTests.cs`, `TempSkipTests.cs`, `TaskIsolationTests.cs`, `QuitSafetyTests.cs`.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 특히 `[임시]` 건너뛰기/모의 실패 코드가 본체 한가운데에 145줄을 차지해 향후 실기 후 걷어낼 때 본체 수정 범위를 넓힌다.

#### GM-7-A-03 · `EquipmentScreen.tsx` (864줄) → 출발 전 점검 · 카메라 전원 모달 · 장비 목록/안내 영역 분리

- **무엇**: `web/src/screens/EquipmentScreen.tsx`(864줄) 한 파일 안에 ① 장비 연결/편집 상태와 태양 이동·시차 연출(`SUN_MOVE_MS`), ② 출발 전 점검(`PRECHECKS` 3항목 정의, 별자리 회전 각도 계산, 점검 진행 UI), ③ 카메라 전원 허브 출력 선택·전압 확인 대화상자(`CameraPowerDialog`), ④ 오른쪽 안내 영역(`RightContent`: 성공/편집/진단/망원경 목록 `ScopeList` 분기)이 모두 들어 있다.
- **왜**: 장비 연결 단계(`phase === 'connect'`)와 출발 전 점검 단계(`phase === 'precheck'`)는 같은 배경(`RigScene`)만 공유할 뿐 상태와 버튼·안내 구조가 완전히 다른데 한 컴포넌트에 묶여 있어 줄 수가 864줄에 달한다.
- **근거**:
  - `web/src/screens/EquipmentScreen.tsx:26-49`: `PRECHECKS` 3개 항목 정의(`tripod`, `balance`, `zero`).
  - `web/src/screens/EquipmentScreen.tsx:287-331`: `useEffect` 안의 출발 전 점검 북극성 회전 각도 계산(`setSkyTurn(turn - 180, ox, oy)`).
  - `web/src/screens/EquipmentScreen.tsx:419-469`: 출발 전 점검 전용 렌더링 분기(`if (phase === 'precheck')`).
  - `web/src/screens/EquipmentScreen.tsx:669-778`: `RightContent` (장비 편집 목록, `ScopeList`, 진단 카드, 출발 전 점검 시작 안내).
  - `web/src/screens/EquipmentScreen.tsx:780-863`: `CameraPowerDialog`, `OutletRow`, `outletLabel` (카메라 전원 출력 선택 모달 84줄).
- **제안**:
  1. **`web/src/components/CameraPowerDialog.tsx` 분리 (약 95줄)**: `CameraPowerDialog`, `OutletRow`, `outletLabel`, `SwitchPort` 타입(`56-62줄`, `780-863줄`)을 독립 컴포넌트로 추출한다.
  2. **`web/src/components/PrecheckStage.tsx` 분리 (약 140줄)**: `PRECHECKS` 상수(`26-49줄`), 북극성 회전 계산 `useEffect`(`287-331줄`), `phase === 'precheck'`일 때의 안내·진행 점·버튼 UI(`419-469줄`)를 분리한다. 부모(`EquipmentScreen`)로부터는 `rigRef`(망원경 그림 위치 측정용), `step`, `onStepChange`, `onComplete`만 props로 받는다.
  3. **`web/src/components/EquipmentRightPane.tsx` 분리 (약 130줄)**: `RightContent`(`669-778줄`)와 `withoutLabel`(`660-666줄`)을 분리한다.
  4. **본체 (`EquipmentScreen.tsx`, 약 480줄)**: 장비 스트림(`useCheckStream`), 태양 이동/시차 연출, 궤도(`RigScene`)와 왼쪽 장비 목록, 하단 진행률/재시도 바만 담당한다.
  - **단계별 순서**: ① `CameraPowerDialog.tsx` 추출 → ② `PrecheckStage.tsx` 추출 → ③ `EquipmentRightPane.tsx` 추출.
  - **검증 테스트**: `npm run lint && npm test && npm run build`.
- **작업 크기**: 중간
- **실사용 등급**: 겪음 — 출발 전 점검 항목이나 카메라 전원 모달을 손볼 때 장비 연결 SSE/궤도 연출 상태까지 함께 읽어야 한다.

#### GM-7-A-04 · `EquipmentConnector.cs` (853줄) → 슬롯/선택 상태 · 연결 전후 점검 · 전원 배선 추론 분리

- **무엇**: `src/Astro.Server/Engine/EquipmentConnector.cs`(853줄)에 `EquipmentOptions`, `EquipmentChoices`(준필수 장비 '없이 진행' 저장), `EquipmentConnector`의 슬롯 정의(`Slots`)와 시뮬레이션/실장비 연결 오케스트레이션, 연결 중단(`AbortConnectAsync`), 카메라 전원 허브 출력 제어(`CameraPowerAsync`, `SwitchOutletsAsync`), 전원 배선 자동 학습(`LearnWiringFromFailure`, `LearnCameraBatteryAsync`), N.I.N.A. 프로필 값 읽기/쓰기 및 연결 직후 점검(`PostConnectCheckAsync`), 진단 메시지 변환(`Diagnose`, `MountProblem`, `CameraProblem`)이 모두 들어 있다.
- **왜**: 장비 연결 오케스트레이션과 전원 배선 추론(`PowerWiringStore.Learn`), 연결 후 환경 점검(`PostConnectCheckAsync`), 장비별 실패 진단 사유 생성이 한 클래스에 섞여 있어 단일 책임 원칙이 흐려져 있다.
- **근거**:
  - `src/Astro.Server/Engine/EquipmentConnector.cs:11-50`: `EquipmentOptions`, `EquipmentChoices`.
  - `src/Astro.Server/Engine/EquipmentConnector.cs:204-288`: `AbortConnectAsync`, `WaitUnansweredConnectsAsync`, `DisconnectAnsweredAsync`.
  - `src/Astro.Server/Engine/EquipmentConnector.cs:465-558`: `SwitchOutletsAsync`, `SetCameraOutletAsync`, `CameraPowerAsync`, `LearnCameraBatteryAsync`, `LearnWiringFromFailure`.
  - `src/Astro.Server/Engine/EquipmentConnector.cs:563-665`: `PostConnectCheckAsync` 및 `CheckPostConnect` (저장 폴더·솔버·적도의 관측지·시뮬레이터·PHD2 창 점검).
  - `src/Astro.Server/Engine/EquipmentConnector.cs:697-852`: `Diagnose`, `MountProblem`, `CameraProblem`, `WithoutHint`, `IsOptional`.
- **제안**:
  1. **`EquipmentChoices.cs` 분리 (약 50줄)**: `EquipmentOptions`와 `EquipmentChoices`(`11-50줄`)를 독립 파일로 추출한다.
  2. **`EquipmentConnector.Power.cs` partial 또는 `EquipmentPowerHelper` 분리 (약 110줄)**: `SwitchOutletsAsync`, `SetCameraOutletAsync`, `CameraPowerAsync`, `LearnCameraBatteryAsync`, `LearnWiringFromFailure`(`465-558줄`)를 분리한다.
  3. **`EquipmentConnector.Diagnostics.cs` partial 분리 (약 260줄)**: `PostConnectCheckAsync`, `CheckPostConnect`(`563-665줄`) 및 `Diagnose`, `MountProblem`, `CameraProblem`(`697-852줄`)을 분리한다.
  4. **본체 (`EquipmentConnector.cs`, 약 430줄)**: `Slots`, `RunAsync`, `ConnectRealAsync`, `AbortConnectAsync`, `CheckOneAsync`, `DescribeAsync`만 남긴다.
  - **단계별 순서**: ① `EquipmentChoices.cs` 분리 → ② `EquipmentConnector.Power.cs` partial 분리 → ③ `EquipmentConnector.Diagnostics.cs` partial 분리.
  - **검증 테스트**: `tests/Astro.Server.Tests/Engine/AbortUnansweredTests.cs`, `DevicePrecheckTests.cs`, `Phd2CheckTests.cs`, `Phd2ConnectTests.cs`, `PostConnectCheckTests.cs`, `PowerWiringTests.cs`.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 연결 실패 문구나 연결 후 점검 규칙을 추가할 때 연결 중단(`_connectGate`·`_unanswered`) 동기화 코드와 섞여 있어 수정 부담이 크다.

#### GM-7-A-05 · `RigScene.tsx` (699줄) → 궤도 수학/상수 · 행성/위성 팝오버 · 출발 전 점검/장비 일러스트 분리

- **무엇**: `web/src/components/RigScene.tsx`(699줄)에 궤도 배치 상수(`ORBITS`)와 위성/슬롯 매핑, 궤도 화면(`RigScene`)과 행성(`Planet` + 드라이버 선택 팝오버 `pickBox`)·위성(`Moon`), 망원경 중앙 그림(`TelescopeRig`), 출발 전 점검 오버레이(`PrecheckOverlay`: 북극성 별자리 SVG, 극축 점선, 나침반, 삼각대, 균형추 화살표, 포커서 노브), 장비 행성 아이콘(`RigGlyph`)이 한 파일에 모여 있다.
- **왜**: `PrecheckOverlay`(120줄)와 `TelescopeRig`/`RigGlyph`(200줄)는 순수 SVG 일러스트 컴포넌트인데, 궤도 회전 애니메이션(`requestAnimationFrame` 루프) 및 드라이버 변경 팝오버 상태와 한 파일에 묶여 있다.
- **근거**:
  - `web/src/components/RigScene.tsx:26-102`: `ORBITS`, `MOONS`, `MoonParent`, `slotState`, `allConnected`.
  - `web/src/components/RigScene.tsx:222-377`: `Planet` 및 `Moon` 컴포넌트(드라이버 목록 펼침 `useKeepInside`, 키보드 Escape 닫기).
  - `web/src/components/RigScene.tsx:380-492`: `TelescopeRig` SVG 일러스트.
  - `web/src/components/RigScene.tsx:498-617`: `PrecheckOverlay` (카시오페이아·북두칠성·북극성 SVG, 삼각대·균형·포커서 노브 표시).
  - `web/src/components/RigScene.tsx:620-699`: `RigGlyph` (장비 종류별 SVG 아이콘).
- **제안**:
  1. **`web/src/components/RigIllustrations.tsx` 분리 (약 320줄)**: `TelescopeRig`(`380-492줄`), `PrecheckOverlay`(`498-617줄`), `RigGlyph`(`620-699줄`)를 별도 파일로 분리한다. 모두 내부 상태를 거의 갖지 않는 순수 SVG 렌더링 컴포넌트(단 `PrecheckOverlay`는 `useTheme` + `scopedSvg`만 사용)다.
  2. **본체 (`RigScene.tsx`, 약 380줄)**: `ORBITS` 배치, `RigScene`의 `requestAnimationFrame` 공전 루프(`134-169줄`), `Planet`·`Moon` 인터랙션만 유지한다.
  - **단계별 순서**: ① `RigIllustrations.tsx`로 `TelescopeRig`, `PrecheckOverlay`, `RigGlyph` 이동 및 export → ② `RigScene.tsx`에서 import.
  - **검증 테스트**: `npm run lint && npm test && npm run build`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 상태 공유 없이 순수 SVG 컴포넌트만 떼어 내면 되어 위험 없이 반으로 줄일 수 있다.

#### GM-7-A-06 · `LiveView.tsx` (688줄) → 액자/공통 훅 · 작업별 실시간 뷰어 · 모의 하늘 그림 분리

- **무엇**: `web/src/components/LiveView.tsx`(688줄)에 `LiveView` 분기 스위치, `FramedImage`(실제 사진 비율 측정 및 `GlowFrame` 래핑), `SharpCapLive`, `GuideLive` + `GuideSpark`(가이드 오차 미니 그래프), `SolvedLive`(솔빙 사진 + 구도 회전 `FrameRotate`), `TestShotLive` + `Histogram`(히스토그램 오버레이) + `NineGridModal`(9칸 확대 보기 모달), `SkyMap`(적경·적위 하늘 지도), `FocusCurve`(2차 곡선 적합 V-커브), 그리고 모의 모드용 SVG 배경(`SimSharpCap`, `SimGuideField`, `SimFocusField`, `SimTargetPhoto`, `STARS`)이 모두 들어 있다.
- **왜**: 실장비에서 쓰는 핵심 뷰어(`FramedImage`, `GuideSpark`, `Histogram`, `NineGridModal`, `FocusCurve`, `SkyMap`)와 모의 모드 전용 별밭 SVG(`Sim*`, 135줄)가 섞여 있고, 특히 `TestShotLive` + `Histogram` + `NineGridModal`(165줄)은 독립적인 기능이 크다.
- **근거**:
  - `web/src/components/LiveView.tsx:46-119`: `FramedImage` (이미지 로드·리사이즈 측정 + `GlowFrame`).
  - `web/src/components/LiveView.tsx:214-380`: `TestShotLive`, `Histogram`, `GRID_TILES`, `NineGridModal` (시험 사진·히스토그램·9칸 확대 보기 167줄).
  - `web/src/components/LiveView.tsx:383-544`: `SkyMap`, `FocusCurve` (하늘 지도·초점 곡선 162줄).
  - `web/src/components/LiveView.tsx:553-688`: `SimSharpCap`, `SimGuideField`, `SimFocusField`, `SimTargetPhoto`, `STARS` (모의 전용 SVG 136줄).
- **제안**:
  1. **`web/src/components/LiveSimViews.tsx` 분리 (약 145줄)**: `SimSharpCap`, `SimGuideField`, `SimFocusField`, `SimTargetPhoto`, `STARS`(`553-688줄`)를 분리한다.
  2. **`web/src/components/TestShotView.tsx` 분리 (약 175줄)**: `TestShotLive`, `Histogram`, `GRID_TILES`, `NineGridModal`(`214-380줄`)을 분리한다 (`FramedImage`를 import하여 사용).
  3. **본체 (`LiveView.tsx`, 약 370줄)**: `LiveView` 라우터, `FramedImage`, `SharpCapLive`, `GuideLive`/`GuideSpark`, `SolvedLive`, `SkyMap`, `FocusCurve`를 둔다.
  - **단계별 순서**: ① `LiveSimViews.tsx` 분리 → ② `TestShotView.tsx` 분리.
  - **검증 테스트**: `web/src/components/liveViewMath.test.ts` 및 `npm run lint && npm test && npm run build`.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 9칸 확대 보기나 히스토그램, 구도 회전 오버레이를 다듬을 때 모의 SVG와 다른 단계 뷰어까지 한 파일에서 오가야 한다.

#### GM-7-A-07 · `PlanScreen.tsx` (656줄) → 대상 검색/칩 바 · 촬영 설정 줄(`SettingsBar`) · 채팅 영역 분리

- **무엇**: `web/src/screens/PlanScreen.tsx`(656줄)에 계획 불러오기/확정/이동 승인 흐름과 함께, 대상 검색 자동완성 드롭다운(`searchOpen`, `found`, 키보드 화살표 이동 `movePick`), 촬영 설정 조작 바(`SettingsBar` + `SpinInput` + `WindowLine`, 215줄), 오른쪽 AI 비서 채팅창(`messages`, `send`, 연습 모드 검색 안내)이 한 파일에 들어 있다.
- **왜**: `SettingsBar`·`SpinInput`·`WindowLine`(`442-655줄`)은 이미 내부에서 별도 함수 컴포넌트로 나뉘어 있고 부모의 `busy`와 `onChange`, `onResetStart`만 받는 자족적인 구조이므로 파일만 분리해도 `PlanScreen.tsx`가 440줄로 줄어든다.
- **근거**:
  - `web/src/screens/PlanScreen.tsx:82-151`: 대상 검색 입력·디바운스(120ms)·외부 클릭 닫기·키보드 화살표 선택 로직.
  - `web/src/screens/PlanScreen.tsx:442-558`: `SettingsBar` (필터 세그먼트, ISO 드롭다운/입력, 노출·총노출 `SpinInput`, 기본값 되돌리기).
  - `web/src/screens/PlanScreen.tsx:560-613`: `SpinInput` (숫자 직접 입력 + `−`/`+` 스텝 버튼).
  - `web/src/screens/PlanScreen.tsx:615-655`: `WindowLine` (촬영 시간대·장수·자오선 반전·시작점 되돌리기 표시).
- **제안**:
  1. **`web/src/components/PlanSettingsBar.tsx` 분리 (약 225줄)**: `SettingsBar`, `SpinInput`, `WindowLine`(`442-655줄`)을 별도 컴포넌트 파일로 추출한다. 스타일은 기존 `PlanScreen.module.css`를 공유하거나 `PlanSettingsBar.module.css`로 해당 클래스(`.settingsBlock`, `.settingGroup`, `.segGroup`, `.spin`, `.windowLine` 등)를 옮긴다.
  2. **본체 (`PlanScreen.tsx`, 약 435줄)**: 계획 상태 조회/확정, 대상 칩·검색창, `NightGraph`, AI 비서 대화창에 집중한다 (`400줄` 아래로 더 줄이려면 대상 검색 드롭다운 70줄을 `TargetSearchBox.tsx`로 함께 분리하면 약 360줄이 된다).
  - **단계별 순서**: ① `PlanSettingsBar.tsx` 추출 → ② (선택) `TargetSearchBox.tsx` 추출.
  - **검증 테스트**: `web/src/plan.test.ts` 및 `npm run lint && npm test && npm run build`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 이미 파일 하단에 독립 컴포넌트로 작성되어 있어 부작용 없이 깔끔하게 분리된다.

#### GM-7-A-08 · `AppServer.cs` (654줄) → 도메인별 엔드포인트 매핑 확장 메서드 분리

- **무엇**: `src/Astro.Server/AppServer.cs`(654줄)의 `Build` 메서드 하나에 DI 서비스 등록(`39-125줄`), 정적 파일·SPA 폴백(`132-160줄`), 그리고 설정·프로필·관측지·엔진·장비·계획·준비·촬영·밤 요약·세션·종료까지 약 65개의 Minimal API 엔드포인트(`162-635줄`)와 DTO 레코드(`639-653줄`)가 일렬로 정의되어 있다.
- **왜**: 새 API를 추가하거나 요청/응답 형식을 확인할 때 500줄짜리 `Build` 메서드를 위아래로 스크롤해야 하며, 도메인별 라우트 그룹 경계가 코드 구조로 드러나지 않는다.
- **근거**:
  - `src/Astro.Server/AppServer.cs:167-267`: `/api/config`, `/api/settings/*`, `/api/updates/*`, `/api/profiles/*`, `/api/site/*` (설정·프로필·관측지 101줄).
  - `src/Astro.Server/AppServer.cs:269-413`: `/api/setup/*`, `/api/engine/*`, `/api/nina/*`, `/api/equipment/*`, `/api/optics/*` (설치·엔진·장비·망원경 145줄).
  - `src/Astro.Server/AppServer.cs:414-465`: `/api/plan/*` (계획 52줄).
  - `src/Astro.Server/AppServer.cs:467-635`: `/api/prepare/*`, `/api/shoot/*`, `/api/night/summary`, `/api/session/*`, `/api/quit/*` (준비·촬영·세션·종료 169줄).
- **제안**:
  - `AppServer.cs`를 partial 클래스 또는 엔드포인트 매핑 확장 메서드로 나눈다:
    1. **`AppServer.cs` (약 180줄)**: `Build` (DI 등록, 미들웨어, 정적 파일, 각 `Map*Endpoints` 호출) 및 `StreamChecksAsync` 공통 헬퍼.
    2. **`Endpoints/ProfileAndSiteEndpoints.cs` (약 120줄)**: `/api/config`, `/api/log`, `/api/settings/*`, `/api/updates/*`, `/api/profiles/*`, `/api/site/*` + 관련 요청 레코드(`SettingsUpdate`, `DismissUpdateRequest`, `PowerUpdate`).
    3. **`Endpoints/EquipmentEndpoints.cs` (약 155줄)**: `/api/setup/*`, `/api/engine/*`, `/api/nina/*`, `/api/equipment/*`, `/api/optics/*` + `RigSelectRequest`, `WithoutRequest`, `CameraPowerRequest`.
    4. **`Endpoints/PlanAndNightEndpoints.cs` (약 220줄)**: `/api/plan/*`, `/api/prepare/*`, `/api/shoot/*`, `/api/night/summary`, `/api/session/*`, `/api/quit/*` + `ChatRequest`, `PickRequest`, `StartRequest`, `SettingsRequest`, `FramingRequest`, `PrepActRequest`, `ShootAnswerRequest`, `SessionPhaseRequest`.
  - **단계별 순서**: 라우트 URL이나 DI 등록을 일절 바꾸지 않고 `private static void Map...Endpoints(WebApplication app)` partial 메서드로 파일만 나눈다.
  - **검증 테스트**: 전체 백엔드 테스트(`dotnet test`).
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 동작 변경 없이 라우트 정의만 도메인별로 정리하는 작업이다.

#### GM-7-A-09 · `App.tsx` (625줄) → 종료 대화상자(`QuitDialog`) 및 이어서 하기 배너/모달 분리

- **무엇**: `web/src/App.tsx`(625줄)에 화면 전환 상태 기계(`Screen`, 페이드 전환, `ScreenReady`), 프로필·장비·계획·준비 완료 상태 동기화, 이어서 하기(`pending`, `resumeConfirm`, `resuming`), 그리고 하단 240줄 분량의 앱 종료 절차 대화상자(`QuitDialog`, `QuitPhase`, `QuitStep`, 적도의 홈 정지 확인·추적 끄기·포커서 0 이동·프로그램 순차 닫기·강제 종료 확인)가 함께 들어 있다.
- **왜**: `QuitDialog`(`386-624줄`)는 독립된 상태 기계(`checking` → `stopping-home` → `home-unconfirmed` → `confirm` → `closing` → `tracking-failed` → `failed`)와 전용 API 호출을 가진 240줄짜리 모달인데 `App.tsx`에 인라인되어 있어 최상위 라우터 파일이 비대해졌다.
- **근거**:
  - `web/src/App.tsx:287-332`: 이어서 하기 확인(`ConfirmDialog`) 및 복원 중 모달(`Modal`).
  - `web/src/App.tsx:386-624`: `QuitTarget`, `QuitPhase`, `QuitStep`, `QuitDialog` (종료 절차 전체 239줄).
- **제안**:
  1. **`web/src/components/QuitDialog.tsx` 분리 (약 250줄)**: `QuitTarget`, `QuitPhase`, `QuitStep`, `QuitDialog`(`386-624줄`)를 별도 파일로 추출한다. `App.tsx`에서는 `<QuitDialog open={confirmQuit} active={...} onCancel={...} onQuit={closeApp} />` 한 줄로 렌더링한다.
  2. **본체 (`App.tsx`, 약 385줄)**: 최상위 화면 전환, 상태바, 단계 레일, 이어서 하기 확인만 담당하여 400줄 아래로 내려간다.
  - **단계별 순서**: ① `QuitDialog.tsx` 추출 및 `App.module.css`의 `.quitList`/`.quitRow`/`.quitMark` 참조 유지(또는 `QuitDialog.module.css`로 분리).
  - **검증 테스트**: `npm run lint && npm test && npm run build`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — `QuitDialog`는 이미 `App` 바깥의 별도 함수 컴포넌트로 작성되어 있어 파일 분리만으로 즉시 해결된다.

#### GM-7-A-10 · `PlanAssistant.cs` (570줄) → 계획 상태/뷰 빌더(`PlanView`)와 AI 대화/프롬프트 실행기 분리

- **무엇**: `src/Astro.Server/Assistant/PlanAssistant.cs`(570줄)가 ① 오늘 밤 문맥(`NightContext`) 로딩과 화면 직접 조작(`PickTargetAsync`, `RemovePickAsync`, `SetStartAsync`, `ChangeSettingsAsync`, `ConfirmAsync`, `RestoreAsync`), ② 화면 JSON 뷰 생성(`StateAsync`, `PlanView`, `WindowView`, `NightLine`), ③ AI 비서 시스템 프롬프트(`SystemPrompt`), 대화 루프(`ChatAsync`), 연습 모드(`PracticeChat`), 한 줄 인사(`Greeting`, `EnsureStartedAsync`)를 모두 맡고 있다.
- **왜**: 사용자가 화면에서 직접 대상을 고르고 설정을 바꾸는 순수 계획 상태 관리와, LLM(`IChatModel`) 스트리밍·함수 호출(`ToolCallPart`)·프롬프트 생성이 같은 클래스에 묶여 있다.
- **근거**:
  - `src/Astro.Server/Assistant/PlanAssistant.cs:49-203`: `EnsureNightAsync`, `EnsureStartedAsync`, `Greeting`, `NightLine`, `StateAsync`, `PickTargetAsync`, `RemovePickAsync`, `SetStartAsync`, `ChangeSettingsAsync`, `EditAsync`.
  - `src/Astro.Server/Assistant/PlanAssistant.cs:208-295`: `PlanView`, `WindowView` (화면 전송용 익명 객체 조립 88줄).
  - `src/Astro.Server/Assistant/PlanAssistant.cs:356-475`: `ChatAsync`, `PracticeChat` (LLM 스트리밍 및 도구 호출 루프 120줄).
  - `src/Astro.Server/Assistant/PlanAssistant.cs:477-561`: `SystemPrompt` (프롬프트 템플릿 및 야간/장비 요약 생성 85줄).
- **제안**:
  1. **`PlanAssistant.View.cs` partial 또는 `PlanViewBuilder.cs` 분리 (약 130줄)**: `NightLine`(`117-128줄`), `PlanView`(`208-262줄`), `WindowView`(`264-295줄`), `Greeting`(`87-115줄`)을 정적 빌더 또는 partial 파일로 분리한다.
  2. **`PlanAssistant.Chat.cs` partial 분리 (약 210줄)**: `ChatAsync`, `PracticeChat`, `SystemPrompt`, `ChoicesTag`(`19-21줄`, `356-561줄`)를 분리한다.
  3. **본체 (`PlanAssistant.cs`, 약 230줄)**: `_gate` 잠금, `_night`/`_plan`/`_confirmed` 상태, `EnsureNightAsync`, `EditAsync` 계열(화면 직접 조작), `ConfirmAsync`, `RestoreAsync`만 남긴다.
  - **단계별 순서**: ① `PlanAssistant.View.cs` partial 분리 → ② `PlanAssistant.Chat.cs` partial 분리.
  - **검증 테스트**: `tests/Astro.Server.Tests/Plan/ShootWindowTests.cs`, `tests/Astro.Server.Tests/Engine/NightSessionTests.cs`, `tests/Astro.Server.Tests/Prepare/ReviewFixTests.cs`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — partial 분리만으로도 화면 조작/뷰 조립과 AI 스트리밍/프롬프트가 명확히 나뉜다.

#### GM-7-A-11 · `NinaRig.cs` (482줄) → 적도의/포커서/카메라 영역별 partial 또는 정적 파서 분리 여부 판단

- **무엇**: `src/Astro.Server/Prepare/Real/NinaRig.cs`(482줄)는 N.I.N.A. Advanced API v2의 실장비 호출(`MountAsync`, `StartSlewAsync`, `WaitSlewAsync`, `StopSlewAndConfirmAsync`, `ConfirmStillAsync`, `HomeAsync`, `FlipAsync`, `FocuserAsync`, `MoveFocuserAsync`, `StartAutofocusAsync`, `CaptureAsync`, `AbortExposureAsync`, `LastSavedAsync`, `FindSavedFile`, `EventsSinceAsync`)을 감싼 어댑터다.
- **왜**: 482줄로 400줄을 조금 넘지만, 모든 메서드가 `NinaApiClient nina`와 `ILogger<NinaRig> log`(및 카메라 노출 중단 시 `_exposingUntil`)만 공유하는 얇은 장비 명령 래퍼여서 억지로 여러 서비스 클래스로 쪼개면 DI 주입 인자만 늘어난다(`RealPolarDevices`, `RealCenterDevices`, `RealFocusDevices`, `RealShootDevices`, `RealWrapDevices`가 모두 `NinaRig` 하나를 주입받음).
- **근거**:
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:13-22`: `MountState`, `FocuserState`, `ImageStats`, `Shot` 레코드.
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:24-179`: 적도의 조회·이동·정지 확인·홈·반전 (156줄).
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:181-251`: 포커서·자동초점·가이더·이슬 열선 (71줄).
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:253-427`: 카메라 촬영·중단(후지 X-T5 대기 포함)·저장 파일 찾기 (175줄).
  - `src/Astro.Server/Prepare/Real/NinaRig.cs:429-481`: 이벤트 기록 및 JSON 숫자/불리언 헬퍼 (53줄).
- **제안**:
  - **DI 클래스는 `NinaRig` 하나로 유지**하되, 가독성을 위해 파일만 `NinaRig.Mount.cs` / `NinaRig.Camera.cs` / `NinaRig.FocuserAndEvents.cs` partial로 나누거나, **현재 482줄 그대로 유지**해도 무방하다. 만약 단일 파일로 둔다면 `FindSavedFile`(`399-417줄`, 이미 `static`)을 `ImageFolders.cs`로 옮기면 약 460줄이 된다.
  - **검증 테스트**: `tests/Astro.Server.Tests/Real/RealDeviceTests.cs`, `SimulatorRigTests.cs`, `MountTimingTests.cs`, `FujiStopTests.cs`, `FindSavedFileTests.cs`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 응집도가 높은 단일 어댑터이므로 DI 타입을 쪼개지 않는 편이 낫다.

#### GM-7-A-12 · `NinaNotices.cs` (476줄) → 창 감추기(`NinaToastHider` · `NinaAutofocusWindowHider` · `NinaProcesses`)와 로그 번역(`NinaLogWatcher` · `NinaLogRules`) 분리

- **무엇**: `src/Astro.Server/Engine/NinaNotices.cs`(476줄) 한 파일 안에 성격이 다른 두 계층이 함께 들어 있다:
  1. Win32 창 제어 백그라운드 서비스(`NinaToastHider`, `NinaAutofocusWindowHider`, `NinaProcesses`, `11-162줄`)
  2. N.I.N.A. 로그 파일(`%LOCALAPPDATA%\NINA\Logs\*.log`) 실시간 감시(`NinaLogWatcher`)와 한국어 진단 규칙(`NinaLogRules`, `164-476줄`).
- **왜**: Win32 창 위치 이동(`SetWindowPos`로 토스트/자동초점 창을 화면 밖 `-32000, -32000`으로 치우기)과 N.I.N.A. 텍스트 로그 정규식 파싱(`NinaLogRules.Translate`)은 공유하는 상태가 전혀 없다.
- **근거**:
  - `src/Astro.Server/Engine/NinaNotices.cs:11-76`: `NinaToastHider` (100ms 주기 토스트 창 감추기).
  - `src/Astro.Server/Engine/NinaNotices.cs:82-135`: `NinaAutofocusWindowHider` (300ms 주기 자동초점 자식 창 감추기).
  - `src/Astro.Server/Engine/NinaNotices.cs:137-162`: `NinaProcesses` (N.I.N.A. PID 및 자체 창 핸들 캐시).
  - `src/Astro.Server/Engine/NinaNotices.cs:164-322`: `NinaNotice`, `NinaLogWatcher` (로그 파일 tail 및 SSE 발행).
  - `src/Astro.Server/Engine/NinaNotices.cs:328-476`: `NinaLogRules` (순수 함수 `ParseLine`, `Translate`, `FriendlyDevice`).
- **제안**:
  1. **`src/Astro.Server/Engine/NinaWindowHiders.cs` 분리 (약 165줄)**: `NinaToastHider`, `NinaAutofocusWindowHider`, `NinaProcesses`(`11-162줄`)를 이동한다.
  2. **`src/Astro.Server/Engine/NinaNotices.cs` 유지 (약 315줄)**: `NinaNotice`, `NinaLogWatcher`, `NinaLogRules`(`164-476줄`)만 남긴다.
  - **단계별 순서**: 파일 두 개로 클래스만 나누어 이동(네임스페이스 `Astro.Server.Engine` 동일).
  - **검증 테스트**: `tests/Astro.Server.Tests/Engine/NinaLogRulesTests.cs`.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 서로 완전히 독립된 클래스들이 한 파일에 묶여 있던 것이라 즉시 분리할 수 있다.

#### GM-7-A-13 · `PlanTools.cs` (417줄) & `NightGraph.tsx` (402줄) — 현행 유지 권고 ("그대로")

- **무엇**: `src/Astro.Server/Assistant/PlanTools.cs`(417줄)와 `web/src/components/NightGraph.tsx`(402줄)는 400줄 경계선에 걸쳐 있다.
- **왜**:
  - `PlanTools.cs`(417줄)는 AI 도구 스펙(`Specs`), 도구 실행(`Execute`), 계획 상태 순수 갱신(`Recommend`, `Inspect`, `ApplyTarget`, `ApplySettings`, `RefreshSettings`, `WindowOf`)을 담고 있으며, 이미 시간대 수학은 `ShootWindow.cs`(137줄)로, 기준값은 `PlanRules.cs`(106줄)로, 차트 빌더는 `NightChart.cs`(97줄)로 분리되어 있다.
  - `NightGraph.tsx`(402줄)도 커밋 `3133087`에서 이미 순수 수학 함수들이 `nightGraphMath.ts`(97줄)로 분리되었고, 남은 402줄은 단일 `<svg>` 안에 그리드·구름·달·대상 곡선·드래그 핸들을 순서대로 그리는 JSX와 드래그 포인터 이벤트 핸들러다.
- **근거**:
  - `src/Astro.Server/Assistant/PlanTools.cs:14-63` (`Specs`), `65-232` (`Execute`, `Recommend`, `Inspect`), `234-416` (`ApplyTarget`, `ApplySettings`, `RefreshSettings`).
  - `web/src/components/NightGraph.tsx:1-402` 및 `web/src/components/nightGraphMath.ts:1-97`.
- **제안**: 두 파일 모두 **그대로 유지**한다. 억지로 쪼개면 SVG 좌표 변환(`xOf`, `yOf`)이나 `NightContext`/`ShootingPlan` 인자를 여러 파일로 넘겨야 해서 오히려 가독성이 떨어진다.
- **작업 크기**: 작음 (조치 불필요)
- **실사용 등급**: 이론상 — 이미 앞선 리팩터링으로 책임 분리가 잘 되어 있다.

#### GM-7-A-14 · `WrapDevices.cs`의 `MoveToDarkFlatAsync`를 `ImageFolders.Move`로 통합 (`REVIEW_REQUEST_GEMINI.md` A절 지정 항목)

- **무엇**: `src/Astro.Server/Prepare/Tasks/Wrap/WrapDevices.cs`의 `RealWrapDevices.MoveToDarkFlatAsync`(`105-118줄`)가 디렉터리 이름을 비교해 `DARK` 폴더의 파일을 `DARKFLAT` 폴더로 옮기는 로직을 직접 구현하고 있어, `src/Astro.Server/Prepare/Real/ImageFolders.cs`의 `ImageFolders.Move`(`14-23줄`)와 중복된다. 또한 `src/Astro.Server/Shoot/RealShootDevices.cs`의 `MoveToExcluded`(`313-328줄`)도 유사한 폴더 이동을 수행한다.
- **왜**: `RealTestShotDevices.MoveToTestFolder`(`RealTestShotDevices.cs:45-53`)는 이미 `ImageFolders.Move(file, "SNAPSHOT", ImageFolders.Test)`를 쓰고 있는데, `RealWrapDevices.MoveToDarkFlatAsync`만 별도 경로 조작 코드를 가지고 있어 폴더 규칙 변경 시 두 곳을 고쳐야 한다.
- **근거**:
  - `src/Astro.Server/Prepare/Real/ImageFolders.cs:11-23`:
    ```csharp
    public const string Test = "시험";
    public static string Move(string file, string typeFolder, string target)
    ```
  - `src/Astro.Server/Prepare/Tasks/Wrap/WrapDevices.cs:110-115`:
    ```csharp
    var dir = Path.GetDirectoryName(s.File) ?? "";
    var to = string.Equals(Path.GetFileName(dir), "DARK", StringComparison.OrdinalIgnoreCase)
        ? Path.Combine(Path.GetDirectoryName(dir) ?? dir, "DARKFLAT") : Path.Combine(dir, "DARKFLAT");
    Directory.CreateDirectory(to);
    File.Move(s.File, Path.Combine(to, Path.GetFileName(s.File)), overwrite: false);
    ```
- **제안**:
  1. `ImageFolders.cs`에 `public const string DarkFlat = "DARKFLAT";` 상수를 추가한다.
  2. `WrapDevices.cs:110-115`의 6줄을 `ImageFolders.Move(s.File, "DARK", ImageFolders.DarkFlat);` 한 줄로 바꾼다.
  3. `tests/Astro.Server.Tests/Real/ImageFoldersTests.cs`에 `DARK` → `DARKFLAT` 이동 케이스를 한 줄 추가한다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 동작은 완전히 같으며 중복 경로 계산만 제거한다.

### B. `DESIGN.md` 기준 프론트엔드(`web/src`) 전수 점검

#### GM-7-B-01 · [다크(적색) 테마] `GlowFrame.module.css`의 `#fff` 혼합으로 인한 비적색(분홍·흰빛) 누출

- **무엇**: `web/src/components/GlowFrame.module.css`의 `.spin` 그라데이션이 `color-mix(in srgb, var(--glow-color) 70%, #fff)`를 사용하여 다크(적색) 테마에서도 녹색(G)·파란색(B) 채널이 섞인 밝은 분홍빛 테두리를 그린다. 또한 `.frame::before`는 `var(--text, #fff)` 폴백을 사용한다.
- **왜**: `DESIGN.md` 6-2절("촬영용(적색) 테마에서는 화면의 모든 요소에서 적색 채널(R)만 쓰고 G=0, B=0을 지킨다. 흰색·회색·파란색·초록색·노란색이 한 픽셀이라도 나오면 눈의 암적응이 깨진다")을 직접 위반한다. `GlowFrame`은 `night` 테마에서도 `still={theme === 'night'}`로 회전만 멈출 뿐 `.spin` 요소 자체는 `opacity: 0.5`(`GlowFrame.module.css:73-76`)로 준비·촬영 화면의 사진 액자(`FramedImage`)와 모달(`tone="device"`) 둘레에 계속 렌더링된다. `--glow-color`가 `#c81e1e`(200, 30, 30)일 때 `#fff`가 30% 섞이면 `rgb(217, 98, 98)`이 되어 G=98, B=98이 방출된다.
- **근거**:
  - `web/src/components/GlowFrame.module.css:22`:
    ```css
    background: color-mix(in srgb, var(--text, #fff) 9%, transparent);
    ```
  - `web/src/components/GlowFrame.module.css:57,60`:
    ```css
    color-mix(in srgb, var(--glow-color) 70%, #fff) 36deg,
    ...
    color-mix(in srgb, var(--glow-color) 70%, #fff) 216deg,
    ```
- **제안**: `GlowFrame.module.css:57,60`의 `#fff`를 `var(--text)`로 바꾸고(`:root[data-theme='night']`에서 `--text`는 `#d42b2b`로 G=27, B=27이 조금 섞이므로, 완전한 적색을 원하면 `--glow-hi` 토큰을 `index.css`에 추가해 기본 테마는 `#ffffff`, `night` 테마는 `#ff0000`으로 정의), `22줄`의 `var(--text, #fff)`에서도 `#fff` 폴백을 제거한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 야간 촬영·준비 화면에서 사진 액자(`FramedImage`) 둘레에 항상 켜져 있는 테두리여서 암적응에 영향을 준다.

#### GM-7-B-02 · [다크(적색) 테마] 인라인 SVG 로고(`aira-mark.svg` · `aira-wordmark.svg`)와 테마 색상 토큰의 G·B 채널 비영(Non-zero) 값

- **무엇**:
  1. `web/src/assets/aira-mark.svg`와 `web/src/assets/aira-wordmark.svg`는 `<img>` 태그가 아니라 `BrandMark.tsx`와 `RigScene.tsx`에서 `dangerouslySetInnerHTML`로 인라인 삽입된다. 따라서 `index.css:220`의 `:root[data-theme='night'] img { filter: url(#red-only); }` 필터가 적용되지 않는데, 두 SVG 파일 내부의 `svg[data-theme="night"]` 스타일에 정의된 모든 색상(`#a62626`, `#d04444`, `#df5555` 등)에 G=0x16~0x55, B=0x16~0x55가 들어 있다.
  2. `web/src/index.css:184-213`의 `:root[data-theme='night']` 토큰(`--text: #d42b2b`, `--ok: #b82828`, `--warn: #e04848`, `--fail: #ff3b3b` 등)과 `Modal.module.css:154`의 `:global(:root[data-theme='night']) .dialog[data-tone='device'] { --glow-color: #c81e1e; }`도 G·B 채널이 `00`이 아니라 `0x12~0x48`을 포함하고 있다.
- **왜**: `DESIGN.md` 6-2절은 `G=0, B=0`을 명시하고 있으며, 실제 카메라 사진(`<img>`)에는 `App.tsx:249-256`의 `#red-only` SVG 컬러 행렬(`0 0 0 0 0`으로 G·B를 완전히 0으로 제거)을 걸어 두면서 정작 상단 상태바에 밤새 떠 있는 `BrandMark` 로고와 UI 글자에는 G·B 성분이 남아 있다.
- **근거**:
  - `web/src/assets/aira-mark.svg:4` (및 `aira-wordmark.svg:4`):
    ```css
    svg[data-theme="night"]{--ink:#a62626;--accent:#d04444;--ink-hi:#b92e2e;--ink-low:#741616;--gold-hi:#ce4141;--gold-low:#902121;--shine:#df5555;--constellation-hi:#d33b3b;--constellation-mid:#bd3030;--constellation-low:#9e2424}
    ```
  - `web/src/index.css:187-206`:
    ```css
    --text: #d42b2b; ... --warn: #e04848; --fail: #ff3b3b;
    ```
  - `web/src/index.css:220`:
    ```css
    :root[data-theme='night'] img { filter: url(#red-only); }
    ```
- **제안**:
  - `aira-mark.svg`와 `aira-wordmark.svg`의 `svg[data-theme="night"]` 팔레트에서 G·B 바이트를 `00`으로 맞춘다(예: `#a60000`, `#d00000`, `#df0000` 등).
  - `index.css:184-213`의 `:root[data-theme='night']` 토큰들과 `Modal.module.css:154`의 `#c81e1e`도 `G=0, B=0`(`#d40000`, `#e00000`, `#ff0000`, `#c80000`)으로 맞출지, 아니면 의도적으로 약간의 채도 완화를 허용한 것인지 결정해 `index.css` 또는 `DESIGN.md` 6-2절을 일치시킨다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — `BrandMark`는 촬영 화면(`ShootScreen`) 상단 상태바 왼쪽에 밤새 노출된다.

#### GM-7-B-03 · [다크(적색) 테마] 촬영·준비 화면에서도 열리는 설정 창(`Settings`)의 Windows 네이티브 파일 선택 대화상자(`OpenFileDialog`)

- **무엇**: 상단 상태바(`StatusBar`)의 설정 버튼(`SettingsButton`)은 촬영 화면(`ShootScreen`)과 준비 화면(`PrepareScreen`)에서도 항상 열 수 있는데, 그 안의 N.I.N.A.·SharpCap 경로 `"찾아보기"` 버튼(`Settings.tsx:138,162`)을 누르면 `pickExe(name)`을 통해 WPF `Microsoft.Win32.OpenFileDialog`(`MainWindow.xaml.cs:117-123`)가 열린다.
- **왜**: `DESIGN.md` 6-2절("OS 파일 선택 창·메시지 박스·윈도우 알림은 적색이 안 되므로 촬영용에서는 앱 내부 UI로 대신하거나 쓰지 않는다")에 어긋난다. 야간(`data-theme='night'`)에 실수로 설정 → 찾아보기를 누르면 하얀 Windows 탐색기 창이 전체 화면 위에 뜬다.
- **근거**:
  - `DESIGN.md:198`: `OS 파일 선택 창·메시지 박스·윈도우 알림은 적색이 안 되므로 촬영용에서는 앱 내부 UI로 대신하거나 쓰지 않는다.`
  - `web/src/components/Settings.tsx:137-139`:
    ```tsx
    <Button variant="secondary" onClick={() => void browse()}>찾아보기</Button>
    ```
  - `src/Astro.Desktop/MainWindow.xaml.cs:120-121`:
    ```csharp
    var dialog = new Microsoft.Win32.OpenFileDialog { Title = $"{name} 고르기", Filter = $"{name}|{name}", CheckFileExists = true };
    var picked = dialog.ShowDialog(this) == true ? dialog.FileName : null;
    ```
- **제안**: 다크(적색) 테마(`document.documentElement.dataset.theme === 'night'`)이거나 촬영/준비 진행 중일 때는 `"찾아보기"` 버튼을 비활성화(또는 숨김)하고 텍스트 입력칸으로만 경로를 넣게 하거나, 확인 대화상자(`ConfirmDialog`)로 "Windows 파일 선택 창은 밝은 흰색으로 열립니다. 그래도 열까요?"라고 한 번 막아 준다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 야간 현장에서 SharpCap 경로를 확인하려다 `찾아보기`를 누르면 밝은 흰색 OS 창이 눈부심을 유발한다.

#### GM-7-B-04 · [영역 크기 고정 · 레이아웃 흔들림] `NewProfileScreen` 및 `ScopeList`의 오류 문구 조건부 마운트

- **무엇**:
  1. `web/src/screens/NewProfileScreen.tsx:167-171`에서 `{error && <p className={styles.error} role="alert">{error}</p>}`로 오류 문구를 조건부 렌더링한다.
  2. `web/src/components/ScopeList.module.css:139-141`에서 `.error:empty { display: none; }`로 오류 줄을 숨긴다.
- **왜**: `DESIGN.md` 1장("상태가 바뀌어도 다른 요소가 밀리지 않는다. 진행 문구·경고·오류 줄은 없을 때도 자리를 비워 두어(고정 높이), 문구가 나타나거나 사라질 때 위아래 요소가 덜컥거리며 움직이지 않게 한다")을 위반한다. `PlanScreen.tsx:379`와 `SiteScreen.tsx:266`은 이미 항상 `<p className={styles.error}>`를 렌더링하고 `min-height`를 잡아 두고 있으나, `NewProfileScreen`과 `ScopeList`만 조건부 마운트/`display: none`을 쓰고 있다.
- **근거**:
  - `web/src/screens/NewProfileScreen.tsx:167-171`:
    ```tsx
    {error && (
      <p className={styles.error} role="alert">
        {error}
      </p>
    )}
    ```
  - `web/src/components/ScopeList.module.css:139-141`:
    ```css
    .error:empty {
      display: none;
    }
    ```
- **제안**:
  - `NewProfileScreen.tsx:167-171`을 `<p className={styles.error} role={error ? 'alert' : undefined}>{error}</p>`로 항상 렌더링하고, `NewProfileScreen.module.css`의 `.error`에 `min-height: 20px; margin: 0;`을 준다.
  - `ScopeList.module.css:139-141`의 `.error:empty { display: none; }`를 제거하고 `.error`에 `min-height: 18px;`을 부여한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 별명 미입력이나 망원경 입력값 검증 오류가 뜰 때 아래쪽 확인/취소 버튼 자리가 순간적으로 아래로 밀린다.

#### GM-7-B-05 · [기준 틀 `1920×1200` 배율 호환] `Modal.module.css`의 `100vw` · `100vh` 사용으로 인한 배율(`frame.scale`) 불일치

- **무엇**: `web/src/components/Modal.tsx:96`은 `<dialog>`에 `style={{ scale: frame.scale }}`을 적용하는데, `web/src/components/Modal.module.css:5,28`은 모달의 너비와 최대 높이를 `calc(100vw - 32px)`와 `min(880px, calc(100vh - 48px))`로 계산한다.
- **왜**: `DESIGN.md` 1장("창 크기에 따른 배치는 `vw`·`vh` 대신 틀 기준 단위(`cqw`·`cqh`)를 쓴다") 규칙과 어긋날 뿐 아니라, `<dialog>`는 top-layer에 렌더링되어 CSS `scale: frame.scale`이 곱해진다. 예를 들어:
  - 1440×900 창(`frame.scale = 0.75`)에서 `100vh - 48px = 852px`로 잡히는데 여기에 `scale: 0.75`가 또 곱해져 모달이 불필요하게 작아지고,
  - 반대로 3840×2160 창(`frame.scale = 1.8`)에서 `max-height: min(880px, calc(100vh - 48px))`는 `880px`이 되는데 여기에 `scale: 1.8`이 곱해지면 실제 화면 높이가 `880 × 1.8 = 1584px`이 되는 등 기준 틀(`1920×1200`) 안의 비율과 어긋난다.
- **근거**:
  - `web/src/components/Modal.tsx:96`:
    ```tsx
    style={{ ...(width ? { '--w': `${width}px` } : undefined), scale: frame.scale } as CSSProperties}
    ```
  - `web/src/components/Modal.module.css:5,28`:
    ```css
    width: min(var(--w), calc(100vw - 32px));
    ...
    max-height: min(880px, calc(100vh - 48px));
    ```
- **제안**: `frame.fixed === true`일 때 `<dialog>`는 이미 `scale: frame.scale`로 기준 틀(`1920×1200`)과 똑같은 배율로 확대·축소되므로, `Modal.module.css`의 데스크톱 기본 규칙은 기준 틀 크기(`1920px`, `1200px`)를 기준으로 `width: min(var(--w), 1856px); max-height: 880px;`로 고정하고, `@media (max-width: 1151px), (max-height: 719px)`(`frame.fixed === false`인 작은 창)에서만 `calc(100vw - 32px)` / `calc(100vh - 48px)`를 적용한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 16:10이 아닌 해상도나 고배율/저배율 창에서 모달의 최대 크기 제한이 기준 화면(`1920×1200`) 비율과 다르게 작동한다.

#### GM-7-B-06 · [공통 부품 일관성] 미사용 중복 컴포넌트 `DiagnosisCard.tsx` 잔존 및 `Tooltip`·`Button` 공통 부품 우회

- **무엇**:
  1. **죽은 중복 파일**: `web/src/components/DiagnosisCard.tsx`와 `DiagnosisCard.module.css`는 어디서도 렌더링되지 않는 미사용 파일이다. 실제 화면(`NinaLostCard`, `NinaNoticeCard`)은 `DiagnosticCard.tsx`를 쓰고 있으며, `checks.ts:2`만 `DiagnosisData` 타입을 `DiagnosisCard.tsx`에서 가져오고 있다.
  2. **`Tooltip` 공통 부품 우회 3곳**: `DESIGN.md` 3장에 공통 `Tooltip` 부품이 정의되어 있으나, ① `ShootPanels.tsx:86`(`ShootGrades`의 `data-tip` + `ShootPanels.module.css:156-178` `::after` 툴팁), ② `StatusBar.tsx:194`(`ProfileSwitcher`의 `<span className={styles.tip}>` + `StatusBar.module.css:99-121`), ③ `NightGraph.tsx:166-167,305-309`(`moonTip` 커스텀 div — 게다가 마우스 호버만 있고 키보드 포커스 처리가 없음)가 각각 독자 툴팁을 구현하고 있다.
  3. **`Button` 공통 부품 우회**: `PowerWiringPanel.tsx:167-169,224-226`("닫기", "처음 설정으로")과 `NewProfileScreen.tsx:177-184`("프로필 목록으로 돌아가기", "프로필 삭제")가 공통 `<Button>` 대신 원시 `<button>`을 쓰며, `NewProfileScreen.module.css:187-214`의 `.backLink`·`.deleteLink`에는 `:focus-visible` 링 스타일도 빠져 있다(`DESIGN.md` 3장 "키보드 포커스 링은 모든 부품에 일관되게").
- **왜**: `DESIGN.md` 3장("화면마다 따로 만들지 않고 앱 전체가 같은 부품을 쓴다. 모양·간격·동작을 부품 한 곳에서 바꾸면 모든 화면이 함께 바뀐다") 원칙에 어긋나며, 죽은 컴포넌트(`DiagnosisCard.tsx`)는 유지보수 시 어느 파일이 진짜인지 혼란을 준다.
- **근거**:
  - `web/src/checks.ts:2`: `import type { DiagnosisData } from './components/DiagnosisCard'` (`DiagnosisBody` 컴포넌트는 전체 저장소에서 호출 0회).
  - `web/src/components/ShootPanels.tsx:86` 및 `ShootPanels.module.css:156-178` (`data-tip` CSS `::after` 툴팁).
  - `web/src/components/StatusBar.tsx:194` 및 `StatusBar.module.css:99-121` (`.profileBtn .tip`).
  - `web/src/screens/NewProfileScreen.module.css:187-214` (`.backLink`, `.deleteLink`에 `:focus-visible` 없음).
- **제안**:
  1. `DiagnosisCard.tsx`와 `DiagnosisCard.module.css`를 삭제하고 `DiagnosisData` 인터페이스는 `checks.ts`(또는 `DiagnosticCard.tsx`)로 옮긴다.
  2. `ShootGrades`와 `ProfileSwitcher`의 자체 CSS 툴팁을 공통 `<Tooltip>` 부품으로 교체한다.
  3. `NewProfileScreen.module.css`의 `.backLink`·`.deleteLink`(및 `PowerWiringPanel.module.css`의 `.close`·`.reset`)에 `:focus-visible { outline: none; box-shadow: var(--focus-ring); }`를 추가하거나 `<Button variant="ghost">`를 사용한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 죽은 파일이 남아 있고, 키보드 탭 이동 시 프로필 편집 화면의 하단 버튼에 포커스 표시가 나타나지 않는다.

#### GM-7-B-07 · [문구 및 용어 규칙] `DESIGN.md` 4·5장 용어·어미 불일치 ("처음 설정으로" vs "처음 배선으로", 하드코딩된 "아이라", `~합니다` 어미 혼용)

- **무엇**:
  1. `web/src/components/PowerWiringPanel.tsx:225`: 버튼 이름이 `"처음 설정으로"`로 되어 있으나 `DESIGN.md` 4장(`129줄`)과 `docs/implementation/POWER_WIRING.md`(`87줄`)에는 `"처음 배선으로"`로 명시되어 있다.
  2. `DESIGN.md` 5장(`158줄`)은 안내·상태 문구 어미를 `"~해요"`로 통일하도록 규정하고 있으나, `src/Astro.Server/Prepare/Tasks/` 전반(`PolarTask.cs:115,125,131,138,141`, `CalibrationTask.cs:64,97,98,105,144`, `CenterTask.cs:43,53,76,113`, `FocusTask.cs:89,129,160,168,202,208,226`, `GuidingTask.cs:47,56,64,73,80`, `TestShotTask.cs:42,52,59,69,94,114`, `WrapTasks.cs:72,89,93`)에서 `"~합니다"`와 `"~해요"`가 같은 작업 안에서도 한 줄 걸러 섞여 쓰인다(예: `CalibrationTask.cs:70` `"같은 밤 보정값을 그대로 씁니다. 다음은 이 근처 별로 초점을 맞춥니다"` vs `78줄` `"보정값을 만들어요"`).
  3. `DESIGN.md` 5장(`159줄`, "제품 이름은 문장에 박아 넣지 않는다. 코드는 `PRODUCT`(웹) · `Product`(서버)의 이름과 조사 도우미를 쓴다")에도 불구하고, 서버 사용자 대면 문구 곳곳에 `"아이라"` 또는 `"AA"`가 문자열 리터럴로 직접 박혀 있다:
     - `"AA"`: `CalibrationTask.cs:179` (`"(별 신호를 보고 AA가 정함)"`), `GuidingTask.cs:53` (`"(AA가 정함)"`), `PolarTask.cs:201` (`"AA가 정함"`), `WrapTasks.cs:67` (`"노출은 AA가 정해요"`).
     - `"아이라"`: `PolarTask.cs:143` (`"아이라는 극축 정렬용 설정으로..."`), `FocusTask.cs:202` (`"이 포커서는 AA가 0점을..."`), `DevicePrecheck.cs:112,113` (`"아이라 {profileLat}..."`), `NinaNotices.cs:378,399,405,412,418`.
- **왜**: 제품 이름을 `product.json`에서 바꿨을 때 `"AA"`와 `"아이라"`가 섞여 노출되며, 특히 사용자가 현장에서 가장 오래 보는 준비·촬영 화면에서 `"AA가 정함"`(옛 약칭)과 `"아이라"`가 동시에 나타난다.
- **근거**:
  - `web/src/components/PowerWiringPanel.tsx:225`:
    ```tsx
    <button type="button" className={styles.reset} onClick={reset}>처음 설정으로</button>
    ```
  - `src/Astro.Server/Prepare/Tasks/Guiding/GuidingTask.cs:53`:
    ```csharp
    run.Status($"가이드 별을 고르는 중입니다 · 가이드 노출 {exposure:0.#}초 (AA가 정함)");
    ```
  - `src/Astro.Server/Prepare/Tasks/Polar/PolarTask.cs:143`:
    ```csharp
    run.Status("SharpCap이 이미 켜져 있습니다. 아이라는 극축 정렬용 설정으로 SharpCap을 켜야 조종할 수 있어, 닫고 다시 켜야 합니다", Tone.Warn);
    ```
- **제안**:
  1. `PowerWiringPanel.tsx:225`와 `DESIGN.md` 4장의 문구를 하나로 맞춘다(전원 설정으로 용어를 통일했으므로 `DESIGN.md`와 `POWER_WIRING.md`를 `"처음 설정으로"`로 고치거나 버튼을 `"처음 배선으로"`로 변경).
  2. 사용자에게 표시되는 문자열의 `"AA가"` / `"아이라는"` / `"아이라"`를 `Product.Ga` / `Product.Neun` / `Product.Name`으로 치환한다 (`CalibrationTask.cs:179`, `GuidingTask.cs:53`, `PolarTask.cs:143,201`, `FocusTask.cs:202`, `WrapTasks.cs:67`, `DevicePrecheck.cs:112-113`, `NinaNotices.cs:378,399,405,412,418`).
  3. 준비·마무리 작업의 `run.Guide`·`run.Status` 문구 어미를 `DESIGN.md` 5장에 맞춰 정리한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 현재 앱 이름이 `아이라`인데 준비·마무리 화면 곳곳에 옛 이름 `"AA가 정함"`이 그대로 표시된다.

### C. 문서(`DESIGN.md` · `OPERATION_PROCEDURE.md` · `docs/implementation/*.md`)와 실제 코드의 어긋남

#### GM-7-C-01 · `DESIGN.md` 4장 준비·촬영·마무리 작업 이름 표기("가이딩")와 실제 코드("가이드") 불일치

- **무엇**: `DESIGN.md` 5장 어휘 표(`177줄`)에는 `"가이드 (가이딩 X)"`로 정해져 있고 실제 코드(`GuidingTask.cs:33` `Title => "가이드"`, `StartLabel => "가이드 시작"`, `ShootScreen.tsx:203` `"가이드"`, `StepRail.tsx`)도 모두 `"가이드"`로 바뀌었는데, 정작 같은 `DESIGN.md` 4장(`93, 97, 100, 108, 109, 111줄`)과 `docs/OPERATION_PROCEDURE.md`(`98, 224, 304줄 등`)에는 여전히 `"⑥ 가이딩"`, `"가이딩 오차"`, `"가이딩 재시작"`으로 적혀 있다.
- **왜**: 한 문서(`DESIGN.md`) 안에서 4장과 5장이 서로 다른 용어를 쓰고 있어, 새 문구를 작성할 때 어느 쪽을 따라야 할지 혼선을 준다.
- **근거**:
  - `DESIGN.md:177`: `| 가이딩 | **가이드** | "가이드 카메라", "가이드 별", "가이드 없이 진행" |`
  - `DESIGN.md:93`: `- **두 묶음**: **장비 준비**(극축 정렬 · 캘리브레이션 · 초점, 대상과 무관 — 밤에 한 번)와 **대상**(이동 · 센터링 · 초점 확인 · 가이딩 · 시험 사진, 대상마다)은 같은 화면 구성을 쓴다.`
  - `src/Astro.Server/Prepare/Tasks/Guiding/GuidingTask.cs:33-34`: `public string Title => "가이드"; public string StartLabel => "가이드 시작";`
- **제안**: `DESIGN.md` 4장(`93, 97, 100, 108, 109, 111줄`)의 `"가이딩"`을 `"가이드"`로 고쳐 5장 어휘 표 및 실제 코드와 일치시킨다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 문서 내부 불일치 정리다.

#### GM-7-C-02 · `docs/OPERATION_PROCEDURE.md` "촬영"의 총 노출 기본값 설명("대상에 따라 1~3시간")과 실제 코드("늘 180분") 불일치

- **무엇**: `docs/OPERATION_PROCEDURE.md`의 "계획" 장(`100줄`)은 2026-10-14 결정에 맞춰 `"총 노출 시간: 기본 180분(대상과 상관없이 같음)"`으로 고쳐졌으나, 같은 문서 뒤쪽의 "촬영" 장(`335줄`)에는 옛 규칙인 `"기본값은 대상에 따라 1~3시간이고 계획에서 바꿀 수 있다"`가 그대로 남아 있다.
- **왜**: 코드(`PlanRules.DefaultTotalMinutes = 180`)와 계획 장은 180분 고정인데 촬영 장만 옛 설명을 유지하고 있어 문서 앞뒤가 모순된다.
- **근거**:
  - `docs/OPERATION_PROCEDURE.md:100`: `- **총 노출 시간**: 기본 **180분**(대상과 상관없이 같음). 10분 단위, 직접 입력 가능.`
  - `docs/OPERATION_PROCEDURE.md:335`: `- **계획한 장수를 다 채움**: 총 노출 시간 ÷ 한 장 노출로 계산한 목표 장수(예: 총 2시간, 180초 노출 → 40장). 기본값은 대상에 따라 1~3시간이고 계획에서 바꿀 수 있다.`
  - `src/Astro.Server/Assistant/PlanRules.cs:38`: `public const int DefaultTotalMinutes = 180;`
- **제안**: `docs/OPERATION_PROCEDURE.md:335`의 `"기본값은 대상에 따라 1~3시간이고"`를 `"기본값은 180분(3시간)이고"`로 수정한다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 운영 절차 문서 앞뒤 설명 일관성 수정이다.

#### GM-7-C-03 · `docs/implementation/PREPARE.md` 2.4절 의존표에 `focuscheck` 작업 누락 및 옛 단일 묶음 기준 기술

- **무엇**: `docs/implementation/PREPARE.md` 2.4절(`139-149줄`)의 작업 의존표에 `focuscheck`(대상 묶음의 초점 확인)가 빠져 있고, 장비 준비(`rig`)와 대상(`target`)이 분리되기 전의 단일 러너 의존표로 적혀 있다. 실제 코드는 `PrepareRunner.Dependents`(`PrepareRunner.cs:59-72`)와 `PrepareFlow.CrossDependents`(`PrepareFlow.cs:15-20`)로 나뉘어 동작한다.
- **왜**: `PREPARE.md`를 보고 재실행(`redo`) 무효화 범위를 이해하려는 개발자나 AI가 `focuscheck`와 묶음 간 의존(`CrossDependents`)을 놓칠 수 있다.
- **근거**:
  - `docs/implementation/PREPARE.md:139-147`: `polar`, `calibration`, `slew`, `focus`, `center`, `guiding`, `test` 7개만 나열 (`focuscheck` 없음, `focus`가 `slew` 뒤에 있던 옛 순서).
  - `src/Astro.Server/Prepare/Flow/PrepareRunner.cs:59-72` 및 `PrepareFlow.cs:15-20`:
    ```csharp
    ["slew"] = ["center", "focuscheck", "guiding", "test"],
    ["center"] = ["focuscheck", "guiding", "test"],
    ["focuscheck"] = ["test"],
    ```
- **제안**: `docs/implementation/PREPARE.md` 2.4절의 의존표를 현재 코드(`PrepareFlow.CrossDependents` + `PrepareRunner.Dependents`)와 같게 갱신한다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 구현 문서와 실제 코드의 동기화다.

#### GM-7-C-04 · `docs/implementation/ARCHITECTURE.md`의 프로젝트 트리 및 설정 설명이 초기 스캐폴딩 상태에 머물러 있음

- **무엇**: `docs/implementation/ARCHITECTURE.md`(`21-38줄`, `84-93줄`)가 2026-09-29 초기 스캐폴딩 당시의 파일 목록(`EngineCheck.cs` 등 이미 삭제된 파일 표기, `Assistant/`·`Prepare/`·`Shoot/`·`Profiles/`·`Sky/`·`Session/` 누락)과 옛 설정(`Astro.Server/appsettings.json`에만 넣는다는 설명 — 실제로는 `UserSettings`를 통해 `%LOCALAPPDATA%\AIRA\settings.json`에 DPAPI 암호화 저장 가능)으로 적혀 있다.
- **왜**: 저장소 구조를 처음 파악할 때 참고하는 아키텍처 문서가 현재 폴더 구조와 맞지 않는다.
- **근거**:
  - `docs/implementation/ARCHITECTURE.md:23`: `Astro.Core/ # 공유 모델 (지금은 Setup/EngineCheck.cs)` (실제 파일은 `Setup/SetupCheck.cs`, `Sky/Astronomy.cs`, `Sky/Night.cs`, `Assistant/ChatModel.cs`, `Product.cs`).
  - `docs/implementation/ARCHITECTURE.md:88`: `키와 모델 이름은 코드에 넣지 않고 Astro.Server/appsettings.json에만 둔다.` (실제로는 설정 화면 `UserSettings.cs` 지원).
- **제안**: `docs/implementation/ARCHITECTURE.md`의 디렉터리 트리와 설정 저장 위치 설명을 현재 구조에 맞게 업데이트한다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 문서 최신화 항목이다.

### D. 현장 노트북 성능 · 배터리 · 응답성 점검

#### GM-7-D-01 · `CornerSun` + `SunSurface` 30fps 캔버스 루프와 `SkyBackdrop` 애니메이션이 불투명한 밤 화면(계획·촬영·마무리) 뒤에서도 밤새 실행됨

- **무엇**:
  1. `web/src/App.tsx:257-262`에서 `<SkyBackdrop />`과 `<CornerSun visible={...} />`은 화면(`screen`)과 상관없이 항상 마운트되어 있다.
  2. `CornerSun`은 `visible`이 `false`일 때(프로필·관측지·계획·촬영·마무리·요약 화면)도 언마운트되지 않고 CSS `opacity: 0`(`CornerSun.module.css:23-25`)만 적용되므로, 그 안의 `<SunSurface size={200} />`(`SunSurface.tsx:24-40`)가 **밤새 초당 30회(`33ms`)씩 200×200 캔버스에 방사형·선형 그라데이션 17개를 다시 그린다**.
  3. 게다가 `PlanScreen`(`background: var(--bg)`), `ShootScreen`(`background: var(--bg)`), `SummaryScreen`(`background: var(--bg)`)은 배경이 완전 불투명하여 `SkyBackdrop`이 1픽셀도 보이지 않는데도, `SkyBackdrop`의 거대 레이어(`160vw × 160vh`) 드리프트·별 반짝임·28초 주기 유성우 타이머가 뒤에서 계속 돈다.
- **왜**: 야외 현장에서 노트북을 배터리나 파워뱅크로 4~6시간 운용할 때, 눈에 전혀 보이지 않는 `opacity: 0` 캔버스와 불투명 화면 뒤의 GPU 합성 애니메이션이 CPU/GPU를 깨워 배터리를 소모한다.
- **근거**:
  - `web/src/App.tsx:258-262`:
    ```tsx
    <CornerSun
      visible={screen === 'setup-check' || screen === 'engine' || (screen === 'equipment' && sunInCorner) || screen === 'rig' || screen === 'target'}
    />
    ```
  - `web/src/components/CornerSun.module.css:23-25`:
    ```css
    .sun[data-visible='false'] {
      opacity: 0;
    }
    ```
  - `web/src/components/SunSurface.tsx:31-35`:
    ```tsx
    const loop = (now: number) => {
      if (now - last >= 33) {
        last = now
        drawSunSurface(g, size, ((now / 1000) * Math.PI * 2) / SUN_PERIOD_S, rgb)
      }
      raf = requestAnimationFrame(loop)
    }
    ```
- **제안**:
  1. `SunSurface.tsx`에 `paused?: boolean` prop을 추가하고, `CornerSun`에서 `visible === false`일 때(페이드아웃 350ms 뒤) 또는 `document.hidden`일 때 `requestAnimationFrame` 루프를 멈춘다.
  2. `App.tsx`에서 배경이 완전 불투명한 화면(`screen === 'plan' || screen === 'shoot' || screen === 'summary'`)일 때는 `SkyBackdrop`에 `paused`(또는 `display: none`)를 주어 보이지 않는 별/성운/유성 애니메이션 합성을 멈춘다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 가장 오래 켜 두는 촬영 화면(`shoot`)에서 밤새 불투명 배경 뒤로 30fps 캔버스 렌더링과 CSS 애니메이션이 헛돈다.

#### GM-7-D-02 · `LiveView.tsx`의 `FramedImage`가 `ResizeObserver`가 있는데도 300ms마다 `setInterval`로 DOM 크기를 폴링함

- **무엇**: `web/src/components/LiveView.tsx:66-72`의 `FramedImage`가 `ResizeObserver`를 등록해 두고도 동시에 `window.setInterval(measure, 300)`을 돌려 초당 3.3회씩 `getBoundingClientRect()`/`clientWidth`/`naturalWidth`를 재고 `setBox`를 호출한다. 마찬가지로 `WindowView.tsx:41-46`도 `ResizeObserver`와 함께 `window.setInterval(send, 250)`을 돌린다.
- **왜**: `FramedImage`의 `measure()`는 오직 컨테이너 크기(`el.clientWidth`, `el.clientHeight`)와 이미지 원본 크기(`img.naturalWidth`, `img.naturalHeight`)만 계산한다(`LiveView.tsx:54-62`). 컨테이너 크기 변화는 `ResizeObserver`가 즉시 잡고, 이미지 원본 크기 변화는 `<img onLoad={measure}>`(`108줄`)가 즉시 잡으므로 300ms `setInterval`은 아무 역할도 하지 않으면서 타이머만 깨운다. (`WindowView.tsx`는 위치 이동 감지 목적이 있으나 `frame.scale`과 `ResizeObserver`만으로도 충분하거나 주기를 1000ms로 늦출 수 있다.)
- **근거**:
  - `web/src/components/LiveView.tsx:66-72`:
    ```tsx
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    const id = window.setInterval(measure, 300)
    return () => {
      ro.disconnect()
      window.clearInterval(id)
    }
    ```
- **제안**: `LiveView.tsx:69,71`의 `window.setInterval(measure, 300)`을 제거한다 (`ResizeObserver` + `<img onLoad={measure}>`로 100% 커버됨).
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 준비·촬영 화면에서 사진이 떠 있는 내내 300ms마다 불필요한 레이아웃 측정을 반복한다.

#### GM-7-D-03 · `FrameRotate.tsx`가 드래그 중이 아닐 때도 전역 `window`에 `pointermove` · `pointerup` 리스너를 상시 등록하고 매 렌더링마다 재부착함

- **무엇**: `web/src/components/FrameRotate.tsx:60-80`의 `useEffect`가 드래그 여부(`drag.current`)와 상관없이 `window.addEventListener('pointermove', move)`와 `window.addEventListener('pointerup', up)`를 항상 걸어 두고 있으며, 의존성 배열이 `[drag, angle, settled]`여서 마우스를 움직여 `angle`이 바뀔 때마다 리스너를 해지하고 다시 등록한다.
- **왜**: 센터링 완료 화면에서 마우스를 움직이기만 해도 전역 `pointermove`가 매번 실행되고, 드래그 중에는 매 프레임마다 `angle` state가 바뀌면서 `window.removeEventListener` / `window.addEventListener`가 초당 60회씩 반복된다.
- **근거**:
  - `web/src/components/FrameRotate.tsx:60-80`:
    ```tsx
    useEffect(() => {
      const move = (e: PointerEvent) => {
        const d = drag.current
        if (!d) return
        ...
      }
      ...
      window.addEventListener('pointermove', move)
      window.addEventListener('pointerup', up)
      window.addEventListener('pointercancel', up)
      return () => { ... }
    }, [drag, angle, settled])
    ```
- **제안**: `dragging` 불리언 state(또는 포인터 캡처 `onPointerMove`/`onPointerUp`를 `.handle` 요소에 직접 바인딩: 이미 `54줄`에서 `(e.currentTarget as Element).setPointerCapture(e.pointerId)`를 호출하고 있으므로!)를 사용한다. `setPointerCapture`가 걸려 있으면 `window` 리스너 없이 `.handle`의 `onPointerMove` / `onPointerUp` / `onPointerCancel`만으로도 창 밖까지 드래그를 완벽히 받는다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 이미 `setPointerCapture`를 쓰고 있어 `window` 전역 리스너와 `useEffect` 자체를 없앨 수 있다.

#### GM-7-D-04 · `NinaToastHider`(100ms)와 `NinaAutofocusWindowHider`(300ms)가 각자 따로 `Process.GetProcessesByName("NINA")`와 `EnumWindows`를 호출함

- **무엇**: `src/Astro.Server/Engine/NinaNotices.cs`에서 `NinaToastHider`는 100ms마다(`27줄`), `NinaAutofocusWindowHider`는 300ms마다(`90줄`) 각각 `NinaProcesses.Pids()`(`Process.GetProcessesByName("NINA")` 호출 후 핸들 생성·해제)와 `Win32Windows.Visible(pids)`(시스템 전체 최상위 창 `EnumWindows` 열거 + `GetWindowText`)를 따로 실행한다.
- **왜**: `NinaProcesses.OwnWindow()`는 2초 캐시(`149-160줄`)가 적용되어 있지만, 정작 초당 13회(100ms + 300ms) 불리언 `NinaProcesses.Pids()`(`141-147줄`)에는 캐시가 없어 매초 13번씩 커널 프로세스 스냅샷(`GetProcessesByName`)과 `EnumWindows`를 돌린다. 게다가 N.I.N.A.가 아직 켜지지 않은 상태에서도 서버 시작 직후부터 계속 100ms마다 `GetProcessesByName("NINA")`를 호출한다.
- **근거**:
  - `src/Astro.Server/Engine/NinaNotices.cs:141-147`:
    ```csharp
    public static HashSet<uint> Pids()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("NINA"))
            using (p)
                pids.Add((uint)p.Id);
        return pids;
    }
    ```
- **제안**:
  1. `NinaProcesses.Pids()`도 `OwnWindow()`처럼 1~2초 동안 캐시한다(프로세스 ID는 자주 바뀌지 않으므로 1초 캐시만 두어도 `GetProcessesByName("NINA")` 호출이 초당 13회에서 1회로 92% 줄어든다).
  2. 또는 `NinaToastHider`와 `NinaAutofocusWindowHider`를 하나의 `NinaWindowHider` 루프로 합쳐 `Visible(pids)`를 한 번만 열거한 뒤 토스트 창과 자동초점 창을 함께 검사한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 앱이 켜져 있는 내내 초당 13회씩 Win32 프로세스 스냅샷을 생성하는 백그라운드 오버헤드를 즉시 1/10로 줄일 수 있다.

#### GM-7-D-05 · `Fits.ToPng` 및 `Fits.Analyze`가 76~84MB FITS 파일 전체를 `File.ReadAllBytes`로 한 번에 LOH(Large Object Heap)에 할당함

- **무엇**: `src/Astro.Server/Prepare/Real/Fits.cs`의 `ToPng`(`14줄`)와 `Analyze`(`51줄`)가 `File.ReadAllBytes(path)`로 FITS 파일 전체를 단일 `byte[]` 배열로 메모리에 읽는다.
- **왜**: 후지 X-T5 기준 RAW(FITS) 한 장은 약 76~84MB(`RealTestShotDevices.cs:8`, `DevicePrecheck.cs:60`)다. .NET에서 85,000바이트 이상 배열은 LOH(Large Object Heap)에 바로 할당되는데, `Analyze`는 4천만 화소 중 약 200만 점만 건너뛰며(`step = 20`) 읽는데도 84MB 배열 전체를 매번 LOH에 할당한다.
- **근거**:
  - `src/Astro.Server/Prepare/Real/Fits.cs:14`: `var bytes = File.ReadAllBytes(path);`
  - `src/Astro.Server/Prepare/Real/Fits.cs:51`: `var bytes = File.ReadAllBytes(path);`
- **제안**: `FileStream`으로 헤더 블록(2880바이트 단위)만 먼저 읽고, 픽셀 영역은 작은 버퍼(예: 한 줄 또는 64KB `Span<byte>`)나 `MemoryMappedFile.CreateFromFile`로 읽으면 84MB LOH 할당이 0이 된다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 시험 사진과 가이드 사진을 반복해서 읽을 때 불필요한 80MB급 LOH 메모리 스파이크를 막는다.

### E. 순수 함수 · 계산 · 변환 중 단위 테스트가 비어 있는 곳

#### GM-7-E-01 · `Astro.Core.Sky.Astronomy` 및 `Astro.Core.Sky.Night` 천문 계산 테스트 전무

- **무엇**: `src/Astro.Core/Sky/Astronomy.cs`(율리우스일 `JulianDate`, 항성시 `LocalSiderealDegrees`, 고도 `Altitude`, 방위각 `Azimuth`, 시간각 `HourAngle`, 태양/달 위치 `Sun`/`Moon`/`MoonAltitude`/`MoonIllumination`, 각거리 `Separation`)와 `src/Astro.Core/Sky/Night.cs`(일몰·일출·천문 박명·항해 박명 폴백 `Window`, 달 구간 `Moon`, 대상 고도/자오선 통과 `Target`)에 직접적인 단위 테스트가 하나도 없다.
- **왜**: `ShootWindowTests.cs`는 가짜 람다 함수(`t => 45`)만 넘겨 테스트하므로, 실제 천문 좌표·시간대·자오선 통과·박명 시각 계산(`Astronomy` + `Night`)이 틀어져도 잡아내는 테스트가 없다.
- **근거**:
  - `src/Astro.Core/Sky/Astronomy.cs:14-118` 및 `src/Astro.Core/Sky/Night.cs:28-130`.
  - `tests/Astro.Server.Tests/Plan/ShootWindowTests.cs:16`: `private static Func<DateTimeOffset, double> Flat(double alt) => _ => alt;` (실제 `Astronomy`/`Night` 미호출).
- **제안**: `tests/Astro.Server.Tests/Plan/AstronomyAndNightTests.cs`를 추가하여 ① 서울(`37.5665, 126.9780`, KST) 기준 특정 날짜(예: 2026-10-16)의 `Night.Window`가 올바른 일몰·천문 박명 시작·천문 박명 끝·일출 순서(`Sunset < DarkStart < DarkEnd < Sunrise`)를 반환하는지, ② 고위도 하지(백야)에서 `AstronomicalDark == false`로 항해 박명(`-12°`) 폴백이 동작하는지, ③ `Separation` 경계값(동일 좌표 `0°`, 반대편 `180°`, 적경 `359°`↔`1°` 경계)과 `HourAngle` 범위(`-180°~180°`)를 검증한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 관측지 위도·경도로 오늘 밤 박명과 자오선 통과를 계산하는 핵심 수식이다.

#### GM-7-E-02 · `PlanRules.DefaultExposure` · `PlanRules.IsoOptions` · `Rig` 화각/픽셀 스케일 · `Scope` 유효 초점거리/F값 계산 테스트 부재

- **무엇**:
  1. `src/Astro.Server/Assistant/PlanRules.cs`의 `DefaultExposure`(`73-88줄`: 필터·F값·가이더 유무·밝은 달 여부에 따른 기본 노출/ISO 계산)와 `IsStepIso`/`IsoOptions`(`97-105줄`: 카메라 GainMin/GainMax에 따른 ISO 단계 필터링).
  2. `src/Astro.Server/Sky/Tonight.cs`의 `Rig.FieldOfView`와 `Rig.PixelScale`(`14-20줄`).
  3. `src/Astro.Server/Engine/OpticsStore.cs`의 `Scope.EffectiveFocalLength` · `EffectiveFocalRatio` · `Optics`(`12-16줄`: 리듀서·플래트너 배율 적용 및 구경/초점거리로 F값 환산)와 `OpticsStore.Validate`(`113-122줄`).
- **왜**: 모두 외부 의존성이 전혀 없는 순수 계산 함수이며, 계획 단계의 노출 추천과 가이드 판정 기준(`MainPixelScaleArcsec`)에 직접 쓰이는데 단위 테스트가 없다.
- **근거**:
  - `src/Astro.Server/Assistant/PlanRules.cs:73-105`.
  - `src/Astro.Server/Sky/Tonight.cs:14-20`: `public double PixelScale => 206.265 * PixelSize / FocalLength;`
  - `src/Astro.Server/Engine/OpticsStore.cs:12-16`: `public double EffectiveFocalLength => FocalLength * Factor;`
- **제안**: `tests/Astro.Server.Tests/Plan/PlanRulesAndOpticsTests.cs`를 만들어 ① 필터별(`none`, `light_pollution`, `dual_narrowband`)·가이드 유무·달 밝기 조합에 따른 `DefaultExposure` 반환값, ② 전용 천체 카메라(`min=0, max=400` → `IsStepIso == false`) vs 미러리스(`min=100, max=12800` → `IsStepIso == true`), ③ 리듀서 `0.8x` 장착 시 `EffectiveFocalLength`·`EffectiveFocalRatio`·`PixelScale` 계산을 테스트한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 망원경 배율과 픽셀 스케일은 가이드 오차 합격선(`limitArcsec`)과 계획 기본 노출을 결정한다.

#### GM-7-E-03 · `FocusCheckTask.Evidence` 초점 재조정 판정식 단위 테스트 부재

- **무엇**: `src/Astro.Server/Prepare/Tasks/FocusCheck/FocusCheckTask.cs:30-37`의 `FocusCheckTask.Evidence` 레코드(`TemperatureBad`, `HfrComparable`, `HfrBad`, `GrowthPercent`)에 대한 순수 판정 단위 테스트가 `RulesAndContractTests.cs`에 빠져 있다(`PolarRules`, `CalibrationRules`, `GuidingRules`, `TestShotRules`만 테스트됨).
- **왜**: 별 수가 `MinStars(10)` 미만일 때는 HFR이 커져도 비교하지 않는 규칙(`HfrComparable`), 기온 변화 절대값이 `2.0°C` 이상일 때 `TemperatureBad`가 켜지는 경계값(`-2.0°C`, `+1.9°C`), `GrowthPercent` 반올림 등을 검증해 두면 안전하다.
- **근거**:
  - `src/Astro.Server/Prepare/Tasks/FocusCheck/FocusCheckTask.cs:30-37`:
    ```csharp
    public sealed record Evidence(double? TemperatureNowC, double? TemperatureChangeC, double? CenterHfr, double? FocusHfr, int? Stars)
    {
        public bool TemperatureBad => TemperatureChangeC is { } d && Math.Abs(d) >= TemperatureLimitC;
        public bool HfrComparable => CenterHfr is > 0 && FocusHfr is > 0 && Stars >= MinStars;
        public bool HfrBad => HfrComparable && CenterHfr > FocusHfr * HfrGrowthLimit;
        public int? GrowthPercent => HfrComparable ? (int)Math.Round((CenterHfr!.Value / FocusHfr!.Value - 1) * 100) : null;
    }
    ```
- **제안**: `tests/Astro.Server.Tests/Prepare/RulesAndContractTests.cs`에 `FocusCheck_Evidence_Judges_Temperature_And_Hfr_With_Star_Count_Guard` 테스트를 추가한다.
- **작업 크기**: 작음
- **실사용 등급**: 이론상 — 다른 모든 준비 단계 판정 규칙(`PolarRules`, `CalibrationRules`, `GuidingRules`, `TestShotRules`)이 모인 테스트 파일에 바로 추가할 수 있다.

#### GM-7-E-04 · 프론트엔드 순수 파서·변환 함수 `sites.ts`(`parseCoords` · `describeSite`) 및 `inlineSvg.ts`(`scopedSvg`) 테스트 부재

- **무엇**:
  1. `web/src/sites.ts:50-72`의 `parseCoords(text)`: 소수점 위경도(`"37.6231, 128.7432"`), 공백 구분, 영문/한글 방위 표기가 섞인 도·분·초(`"N 37° 37' 23.2\", E 128° 44' 35.5\""`, `"동 128° 44' 35.5\", 북 37° 37' 23.2\""` 순서 뒤집힘 포함), 범위 밖 좌표(`latitude > 90`, `0, 0` 거부)를 파싱하는 정규식 함수인데 테스트가 없다.
  2. `web/src/inlineSvg.ts:9-35`의 `scopedSvg(raw, scope, theme)`: SVG 내부의 `id`, `url(#id)`, `href="#id"`, `<style>` 선택자를 스코프 격리하는 정규식 변환 함수(2026-10-11 스타일 누출 버그 수정으로 도입)인데 테스트가 없다.
- **왜**: 둘 다 복잡한 정규식에 의존하는 순수 함수여서 테스트를 붙여 두면 지도 좌표 붙여넣기나 SVG 자산 수정 시 회귀를 바로 잡을 수 있다.
- **근거**:
  - `web/src/sites.ts:50-76` (`parseCoords`, `valid`, `sameSpot`, `describeSite`).
  - `web/src/inlineSvg.ts:9-35` (`scopedSvg`).
- **제안**: `web/src/sites.test.ts`와 `web/src/inlineSvg.test.ts`를 추가하여 DMS/소수점/동서남북 순서 뒤집힘/`(0,0)` 거부 케이스와 SVG `<style>`/`id` 스코핑 결과를 검증한다.
- **작업 크기**: 작음
- **실사용 등급**: 겪음 — 사용자가 네이버·카카오·구글 지도에서 복사한 다양한 형식의 좌표를 붙여 넣을 때 동작하는 핵심 파서다.

### 우선 조치 순서 (10개)

1. **GM-7-B-01 & GM-7-B-02 (작음 · 겪음)**: 다크(적색) 테마 비적색 누출 제거 — `GlowFrame.module.css`의 `#fff` 혼합 제거 및 인라인 로고 SVG(`aira-mark.svg`, `aira-wordmark.svg`)의 `night` 테마 색상에서 G·B 채널을 `0`으로 정리.
2. **GM-7-B-03 (작음 · 겪음)**: 다크(적색) 테마 또는 촬영·준비 중일 때 설정 화면(`Settings.tsx`)의 `"찾아보기"`(Windows 흰색 파일 열기 창) 호출 방지 또는 확인 창 추가.
3. **GM-7-B-04 & GM-7-B-05 (작음 · 겪음)**: `NewProfileScreen.tsx`·`ScopeList.module.css` 오류 줄 고정 높이 확보(레이아웃 덜컥거림 방지) 및 `Modal.module.css`의 `100vw`/`100vh` 배율 기준 정리.
4. **GM-7-B-06 & GM-7-B-07 (작음 · 겪음)**: 미사용 중복 파일 `DiagnosisCard.tsx` 삭제, `NewProfileScreen` 포커스 링·공통 `Tooltip` 정리, 준비·마무리 문구에 하드코딩된 `"AA"`/`"아이라"`를 `Product` 도우미로 통일.
5. **GM-7-A-14 (작음 · 이론상)**: `WrapDevices.cs`의 `MoveToDarkFlatAsync`를 `ImageFolders.Move`로 통합.
6. **GM-7-D-01 & GM-7-D-02 & GM-7-D-04 (작음 · 겪음)**: 보이지 않을 때 `CornerSun`(`SunSurface`) 30fps 캔버스 루프와 `SkyBackdrop` 일시 정지, `LiveView.tsx`의 중복 300ms `setInterval` 제거, `NinaProcesses.Pids()` 1초 캐시 추가.
7. **GM-7-E-01 & GM-7-E-02 & GM-7-E-03 & GM-7-E-04 (작음 · 겪음)**: `Astronomy`/`Night`, `PlanRules`/`OpticsStore`/`Rig`, `FocusCheckTask.Evidence`, `sites.ts`(`parseCoords`)/`inlineSvg.ts` 순수 함수 단위 테스트 추가.
8. **GM-7-A-08 & GM-7-A-09 & GM-7-A-12 (작음 · 이론상)**: 위험도가 가장 낮은 큰 파일 3개(`AppServer.cs` 엔드포인트 분리, `App.tsx`에서 `QuitDialog.tsx` 분리, `NinaNotices.cs`에서 `NinaWindowHiders.cs` 분리) 먼저 실행.
9. **GM-7-A-02 & GM-7-A-04 & GM-7-A-10 (작음 · 겪음)**: 백엔드 핵심 파일(`PrepareRunner.cs`, `EquipmentConnector.cs`, `PlanAssistant.cs`)의 partial/보조 파일 분리.
10. **GM-7-A-01 & GM-7-A-03 & GM-7-A-05 & GM-7-A-06 & GM-7-A-07 (중간 · 겪음)**: `ShootSession.cs`(1003줄)와 프론트엔드 대형 화면/뷰어(`EquipmentScreen.tsx`, `RigScene.tsx`, `LiveView.tsx`, `PlanScreen.tsx`) 단계별 분리 및 문서(`GM-7-C-01~04`) 최신화.


#### Claude 대조 메모 (2026-10-16, 기준 2a0428f)

- **A 큰 파일 분리 계획은 실행 계획으로 쓰지 않음**: 14개 파일의 줄 수는 정확하나 안에 있다는 함수·컴포넌트가 상당수 없다 — `ShootSession`의 `WatchGuideAsync`·`FlipIfDueAsync`·`SaveResult`, `PrepareRunner`의 `class TaskRun`, `EquipmentConnector`의 `AbortConnectAsync`·`PostConnectCheckAsync`, `EquipmentScreen`의 `PRECHECKS`, `RigScene`의 `ORBITS`·`Planet`(장비 연결 화면 내용과 혼동), `PlanScreen`의 `SettingsBar`, `App.tsx`의 `QuitDialog`(이미 `QuitControl.tsx`). 맞게 짚은 것은 A-08·A-11·A-12·A-13·A-14. 주말 분리는 실제 코드를 읽어 다시 나눈다.
- **반영(사용자 결정)**: B-01(`GlowFrame`의 `#fff` → `--glow-hi`, 촬영 테마 순수 빨강) · B-02(상태 줄 로고·부팅 로고 적색 팔레트를 순수 빨강으로 — "index.css 토큰도 섞였다"는 사실 아님, 모두 `#xx0000`) · B-03(촬영 테마에서 설정 "찾아보기" 막음) · B-04(새 프로필 오류 줄은 주 버튼 위 빈 간격에 겹쳐 늘 두기, 망원경 목록 오류 줄 숨김 지움) · B-07("AA가 정함" 5곳·문장 속 "아이라" → `Product`, DESIGN의 "처음 배선으로" → "처음 설정으로". "~해요 어미 통일" 규칙은 DESIGN에 없어 뺌) · E-01·E-02·E-04 시험 추가 · D-01은 실기 확인 목록에(현장 노트북 배터리로 CPU·GPU 측정).
- **사실 아님·이미 처리됨(무시)**: B-05(높이 928px 미만 창에서만 아주 조금) · C-01~C-04(이미 맞음) · D-01의 근거(구석 태양은 필요한 화면에만 그리고 촬영 화면에서도 보이도록 둔 것, 촬영 화면은 불투명하지 않음) · D-02(크기 감시가 없고 300ms 확인이 유일한 수단) · D-03(꼭짓점 근처 감지용, 드래그 리스너 재등록 아님) · D-04(프로세스 목록은 이미 2초 기억) · E-03(이미 있음).
- **미룸**: A-14(다크플랫 이동을 `ImageFolders.Move`로 — 같은 코드라 동작 차이 없음) · B-06(죽은 `DiagnosisCard`·툴팁 통일) · D-05(사진 통째 읽기, 몇 분에 한 장이라 부담 작음).
