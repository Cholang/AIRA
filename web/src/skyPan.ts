/**
 * 배경 카메라 연출 (2026-10-11 사용자 결정): 엔진 켜기 → 장비 연결에서 태양이 왼쪽 위로 옮겨 갈 때 배경(성운·별·반짝이는 별)도 같은 만큼 밀어
 * 태양은 배경에 붙어 있고 화면이 움직이는 것처럼. 밀린 자리는 그대로 둔다(앱을 다시 켤 때까지). 혜성은 밀지 않는다(시간 표시 역할).
 * 값은 :root의 --sky-pan-x/y (px) — SkyBackdrop의 .pan이 0.9초에 걸쳐 따라간다. 배경 판은 창의 PAN배라 그 안에서만 민다
 */
export const PAN = 1.6

export function skyPan() {
  const st = document.documentElement.style
  return { x: parseFloat(st.getPropertyValue('--sky-pan-x')) || 0, y: parseFloat(st.getPropertyValue('--sky-pan-y')) || 0 }
}

/** 배경을 (x, y) px만큼 민 자리로 (왼쪽·위로만, 판 밖이 드러나지 않게) */
export function setSkyPan(x: number, y: number) {
  const W = window.innerWidth
  const H = window.innerHeight
  const clamp = (v: number, max: number) => Math.round(Math.min(0, Math.max(-max, v)))
  const st = document.documentElement.style
  st.setProperty('--sky-pan-x', `${clamp(x, W * (PAN - 1))}px`)
  st.setProperty('--sky-pan-y', `${clamp(y, H * (PAN - 1))}px`)
}
