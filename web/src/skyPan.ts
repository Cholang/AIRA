/**
 * 배경 카메라 연출 (2026-10-11 사용자 결정): 엔진 켜기 → 장비 연결에서 태양이 왼쪽 위로 옮겨 갈 때 배경(성운·별·반짝이는 별)도 민다.
 * 태양보다 덜·더 천천히 민다 — 태양이 가깝고 배경이 먼 시차 (같은 만큼·같은 속도는 어색했다, 2026-10-11 사용자). 둘 다 천천히 출발해 천천히 멈춘다.
 * 밀린 자리는 그대로 둔다(앱을 다시 켤 때까지). 혜성은 밀지 않는다(시간 표시 역할).
 * 출발 전 점검(2026-10-15)은 따로 더한다: 프로필을 고르면 카메라가 밤하늘에서 내려오듯 배경이 위로, 극축 방향을 확인하면 왼쪽으로(setSkyIntro).
 * 값은 :root의 --sky-pan-x/y (px) — SkyBackdrop의 .pan이 1.6초에 걸쳐 따라간다. 배경 판은 창의 PAN배라 그 안에서만 민다
 */
export const PAN = 1.6
/** 태양이 옮겨 가는 시간 (EquipmentScreen.module.css --sun-move와 같게) */
export const SUN_MOVE_MS = 1100
/** 배경이 태양이 간 거리의 몇 배만큼 가는지 */
export const PARALLAX = 0.6

/** 출발 전 점검 연출의 몫 — 장비 연결의 태양 이동(setSkyPan)과 따로 더한다 */
let intro = { x: 0, y: 0 }

export function skyPan() {
  const st = document.documentElement.style
  return { x: parseFloat(st.getPropertyValue('--sky-pan-x')) || 0, y: parseFloat(st.getPropertyValue('--sky-pan-y')) || 0 }
}

/** 배경을 (x, y) px 자리로 (왼쪽·위로만, 판 밖이 드러나지 않게) */
function write(x: number, y: number) {
  const W = window.innerWidth
  const H = window.innerHeight
  const clamp = (v: number, max: number) => Math.round(Math.min(0, Math.max(-max, v)))
  const st = document.documentElement.style
  st.setProperty('--sky-pan-x', `${clamp(x, W * (PAN - 1))}px`)
  st.setProperty('--sky-pan-y', `${clamp(y, H * (PAN - 1))}px`)
}

/** 장비 연결의 태양 이동: 배경을 (x, y) px만큼 민 자리로 (출발 전 점검 몫은 그 위에 더한다) */
export function setSkyPan(x: number, y: number) {
  write(x + intro.x, y + intro.y)
}

/** 지금 자리에서 (dx, dy) px만큼 더 민다 */
export function panSkyBy(dx: number, dy: number) {
  const now = skyPan()
  write(now.x + dx, now.y + dy)
}

/** 출발 전 점검 연출의 몫을 (x, y) px로 (위·왼쪽은 음수). 다른 몫은 그대로 둔다 */
export function setSkyIntro(x: number, y: number) {
  const now = skyPan()
  write(now.x - intro.x + x, now.y - intro.y + y)
  intro = { x, y }
}
