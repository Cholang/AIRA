import { useId, useMemo } from 'react'
import styles from './SunCorona.module.css'

/**
 * 태양 이글거림 (2026-10-11 — 장비 연결 항성에서 시작, 엔진 켜기 원에도 사용자 요청): 테두리 바깥 흐린 빛줄기 두 묶음이 서로 반대로 아주 천천히 돌며
 * 숨 쉬듯 커졌다 작아진다 + 옅은 후광. 원(JarvisRing, 지름 200 단위 기준 반지름 92)을 감싸는 SVG — 원과 함께 움직이고 커진다.
 * 회전·크기 애니메이션만(그래픽 하드웨어 합성), 동작 줄이기면 멈춤
 */
const R = 92 // JarvisRing 바깥 고리 반지름 (viewBox 200 기준)

export default function SunCorona() {
  const blur = useId()
  const rays = useMemo(() => {
    let s = 11
    const r = () => ((s = (s * 16807) % 2147483647) - 1) / 2147483646
    return [40, 30].map((n) =>
      Array.from({ length: n }, (_, k) => ({
        ang: (k / n) * 360 + r() * 9,
        len: R * (0.04 + r() * 0.09),
        wid: R * (0.015 + r() * 0.025),
        o: 0.07 + r() * 0.14,
      })),
    )
  }, [])
  return (
    <svg className={styles.corona} viewBox="-40 -40 280 280" aria-hidden="true">
      <defs>
        <filter id={blur} x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation={3.2} />
        </filter>
      </defs>
      <circle cx={100} cy={100} r={R * 1.03} className={styles.halo} strokeWidth={R * 0.06} filter={`url(#${blur})`} />
      {rays.map((set, gi) => (
        <g key={gi} className={styles.rays} data-dir={gi} filter={`url(#${blur})`}>
          {set.map((ray, k) => (
            <ellipse key={k} cx={100 + R + ray.len * 0.35} cy={100} rx={ray.len} ry={ray.wid} opacity={ray.o} transform={`rotate(${ray.ang} 100 100)`} />
          ))}
        </g>
      ))}
    </svg>
  )
}
