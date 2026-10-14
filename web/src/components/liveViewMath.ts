// 진행 화면(LiveView)의 계산 — 화면 없이 시험할 수 있게 따로 둔다 (liveViewMath.test.ts)

export type Box = { x: number; y: number; w: number; h: number }

/** 사진을 상자 안에 비율 그대로 가운데 맞춤 */
export function fitInside(size: { w: number; h: number }, box: Box) {
  const k = Math.min(box.w / size.w, box.h / size.h)
  const width = size.w * k
  const height = size.h * k
  return { left: box.x + (box.w - width) / 2, top: box.y + (box.h - height) / 2, width, height }
}

/** 2차 곡선 맞춤 (초점 곡선): [a, b, c] — y = a x² + b x + c */
export function quadFit(p: [number, number][]): [number, number, number] | null {
  const n = p.length
  const mx = p.reduce((s, q) => s + q[0], 0) / n
  let s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0
  for (const [x0, y] of p) {
    const x = x0 - mx
    s0 += 1; s1 += x; s2 += x * x; s3 += x * x * x; s4 += x * x * x * x
    t0 += y; t1 += x * y; t2 += x * x * y
  }
  const det = (m: number[][]) =>
    m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0])
  const M = [[s4, s3, s2], [s3, s2, s1], [s2, s1, s0]]
  const D = det(M)
  if (Math.abs(D) < 1e-12) return null
  const a = det([[t2, s3, s2], [t1, s2, s1], [t0, s1, s0]]) / D
  const b = det([[s4, t2, s2], [s3, t1, s1], [s2, t0, s0]]) / D
  const c = det([[s4, s3, t2], [s3, s2, t1], [s2, s1, t0]]) / D
  if (a <= 0) return null
  // 가운데로 옮긴 x를 되돌린다
  return [a, b - 2 * a * mx, a * mx * mx - b * mx + c]
}