import type { SceneMode } from './components/RigScene'
import type { Stage } from './components/StepRail'

// 앱 흐름 (OPERATION_PROCEDURE.md 1장):
// 부팅 로고 → 프로필 선택 (없으면 새 프로필 만들기) → (오늘 밤 기록이 있으면 "이어서 할까요?") → 출발 전 점검(2026-10-15 맨 앞, 이어서 하면 건너뜀)
// → 0단계 설치 확인 → 1단계 엔진 켜기 → 장비 연결 → 장비 준비(rig) → 대상(계획 → target) → 촬영 → 마무리. OPERATION_PROCEDURE.md 1장
export type Phase = 'boot' | 'profiles' | 'newProfile' | 'check' | 'engine' | 'equipment' | 'site' | 'preflight' | 'rig' | 'plan' | 'target' | 'shoot' | 'wrap' | 'summary'

/** 화면 → 하단 단계 레일의 단계. 부팅·프로필 화면에는 레일이 없다 (DESIGN.md "진행 표시") */
/** 장비 연결 뒤 화면들: 왼쪽 위에 구석 태양을 둔다 (2026-10-11 사용자 요청, CornerSun) */
export const CORNER_SUN: Phase[] = ['site', 'rig', 'plan', 'target', 'shoot', 'wrap', 'summary']

/**
 * 땅과 장비 선화 장면(RigScene)을 까는 화면과 그때의 모습 (2026-10-15 사용자): 부팅 로고 → 프로필 → 출발 전 점검이 같은 장면 위에서 이어진다.
 * 넘어가는 중인 다음 화면(heading)으로 정해, 앞 화면이 사라지기 시작할 때 장면도 바뀐다. 목록에 없는 화면으로 가면 사라진다
 */
export const SCENE_OF: Partial<Record<Phase, SceneMode>> = { boot: 'boot', profiles: 'profiles', newProfile: 'dim', preflight: 'check' }

/** 프로필 화면: 레일은 없지만 N.I.N.A.·PHD2·Empire를 미리 켰으니 창 ×도 종료 절차(켠 프로그램 닫기)를 거친다 (CX-34-02) */
export const prelaunchedPhase = (p: Phase) => p === 'profiles' || p === 'newProfile'

export const STAGE_OF: Partial<Record<Phase, Stage>> = {
  check: '연결',
  engine: '연결',
  equipment: '연결',
  site: '연결',
  preflight: '점검',
  rig: '장비 준비',
  plan: '대상',
  target: '대상',
  shoot: '촬영',
  wrap: '마무리',
  summary: '마무리',
}

/**
 * N.I.N.A. 오류 알림 중 그 화면이 스스로 보여 주는 종류 (겹쳐 띄우지 않음): 장비 연결 화면은 연결 실패를 장비 원에,
 * 준비·대상·촬영은 솔빙·가이딩 실패를 작업 안내에서 보여 준다. 마무리는 아이라가 일부러 연결을 끊어 생기는 적도의·가이더 오류
 */
export const NOTICE_HIDE: Partial<Record<Phase, string[]>> = {
  equipment: ['connect'],
  rig: ['solve', 'guide'],
  target: ['solve', 'guide'],
  shoot: ['solve', 'guide'],
  wrap: ['mount', 'guide'],
}
export const NO_HIDE: string[] = []

/** N.I.N.A.가 켜져 있어야 하는 화면 (1단계 엔진 켜기 이후). 여기서 N.I.N.A.가 꺼지면 알린다 */
export const WATCHED: Phase[] = ['equipment', 'site', 'rig', 'plan', 'target', 'shoot', 'wrap']

/** 상태 줄에 관측지를 보여 주는 화면 (장비 연결 이후). 연필은 장비 연결·관측지 고르기 화면에서는 숨긴다 */
export const SITE_SHOWN: Phase[] = ['equipment', 'site', 'rig', 'plan', 'target', 'shoot', 'wrap', 'summary']
export const SITE_EDITABLE: Phase[] = ['rig', 'plan', 'target']

/** 상단 상태 줄의 지금 단계 이름 */
export const LABEL_OF: Partial<Record<Phase, string>> = {
  equipment: '장비 연결',
  site: '관측지',
  preflight: '출발 전 점검',
  rig: '장비 준비',
  plan: '대상 · 계획',
  target: '대상',
  shoot: '촬영',
  wrap: '마무리',
  summary: '오늘 밤 요약',
}

/** 화면 전환 페이드 한쪽 시간. App.module.css의 .screen transition과 같은 값 */
export const FADE_MS = 280
// 부팅 로고는 더 천천히 투명해진다 (2026-10-10 사용자 — 큰 로고가 0.28초에 사라지면 뚝 끊겨 보임). App.module.css .screen[data-boot]와 같게
export const BOOT_FADE_MS = 800
