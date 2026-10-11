import type { CSSProperties, ReactNode } from 'react'
import styles from './GlowFrame.module.css'

/**
 * 빛 테두리 (2026-10-11 사용자 요청 — 칼 같은 외곽선을 부드럽게, 참고 그림 "Glowing Border Animation V2"): 옅은 테두리 선 위를
 * 마주 보는 두 짧은 빛이 천천히 돌고, 그 빛에서 바깥으로 넓은 후광이 서서히 사라지며 번진다. 어디든 쓸 수 있게 따로 둔 모듈 —
 * 자리는 className/style로, 빛 색 `--glow-color`(기본 --accent), 한 바퀴 `--glow-period`(14초), 선 `--glow-width`, 후광 거리 `--glow-reach`,
 * 모서리 `--glow-radius`. 빛은 transform 회전만 써서 그래픽 하드웨어가 합성한다. 동작 줄이기면 멈춘 빛
 */
export default function GlowFrame({ className, style, children }: { className?: string; style?: CSSProperties; children?: ReactNode }) {
  return (
    <div className={`${styles.frame} ${className ?? ''}`} style={{ position: 'relative', ...style }}>
      <span className={styles.halo} aria-hidden="true">
        <span className={styles.spin} />
      </span>
      {children}
      <span className={styles.ring} aria-hidden="true">
        <span className={styles.spin} />
      </span>
    </div>
  )
}
