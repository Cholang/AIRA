import styles from './LiveView.module.css'

/** 하늘 화면(LiveView)의 좌표와 모의 그림 별밭·은하 (큰 파일 분리 2026-10-16 — 옮기기만) */

// 화면 영역 기준 좌표 (1920×1200 틀에서 상태 줄·진행 표시를 뺀 크기)
export const VW = 1680

export const VH = 1156

export const FX = 840 // 하늘에서 보는 가운데 (글 자리를 피한 높이)

export const FY = 470

export function clamp(v: number, a: number, b: number) {
  return Math.max(a, Math.min(b, v))
}

export function rng(seed: number) {
  return () => {
    seed |= 0
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

export function starfield(n: number, seed: number, opts: { dim?: number; scale?: number } = {}) {
  const r = rng(seed)
  const k = opts.scale ?? 1
  return (
    <g>
      {Array.from({ length: n }, (_, i) => {
        const m = r()
        const rad = (m > 0.985 ? 3.4 : m > 0.93 ? 2.1 : m > 0.7 ? 1.4 : 0.9) * k
        return <circle key={i} className={styles.star} cx={(r() * VW).toFixed(1)} cy={(r() * VH).toFixed(1)} r={rad.toFixed(2)} opacity={((opts.dim ?? 1) * (0.35 + m * 0.65)).toFixed(2)} />
      })}
    </g>
  )
}

export function polarStars() {
  const r = rng(77)
  return Array.from({ length: 46 }, () => {
    const m = r()
    return { u: r() * 2 - 1, v: r() * 2 - 1, r: m > 0.9 ? 4.2 : m > 0.6 ? 2.6 : 1.8, b: m > 0.45 }
  })
}

export function galaxy(cx: number, cy: number, k: number) {
  return (
    <g transform={`translate(${cx} ${cy}) rotate(-38) scale(${k})`} opacity={0.9}>
      <ellipse rx={150} ry={42} className={styles.star} opacity={0.08} />
      <ellipse rx={105} ry={28} className={styles.star} opacity={0.12} />
      <ellipse rx={60} ry={16} className={styles.star} opacity={0.2} />
      <ellipse rx={22} ry={8} className={styles.star} opacity={0.55} />
    </g>
  )
}
