import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { sunSpot } from '../screens/equipmentOrbits'
import { PARALLAX, panSkyBy } from '../skyPan'
import JarvisRing from './JarvisRing'
import styles from './CornerSun.module.css'

/** 장비 연결 뒤로 넘어가며 보이는 크기 (장비 연결에서 보이던 부분의 몇 배) */
const SHOWN = 0.8

/**
 * 구석 태양 (2026-10-11 사용자 요청): 장비 연결의 항성을 이후 단계에서도 왼쪽 위에 그대로 둔다 — 화면 내용 아래 층이라 제목·내용은 그대로 위에 있다.
 * 장비 연결 자리(sunSpot)에서 시작해, 다음 화면이 나타날 때 더 밀려나 보이는 부분이 지금의 80%가 된다. 배경도 시차만큼 함께 민다(skyPan.ts).
 * 표면은 늘 돈다. 글자·진행 호는 없다. App이 화면 영역(진행 표시 왼쪽)에 깐다
 */
export default function CornerSun() {
  const ref = useRef<HTMLDivElement>(null)
  const [box, setBox] = useState({ w: 0, h: 0 })
  const [pushed, setPushed] = useState(false)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setBox((b) => (b.w === el.clientWidth && b.h === el.clientHeight ? b : { w: el.clientWidth, h: el.clientHeight }))
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const spot = box.w > 0 ? sunSpot(box.w, box.h) : null
  const R = spot ? spot.sunD / 2 : 0
  // 보이는 부분(구석에서 테두리까지)을 80%로: 그 20%만큼 왼쪽·위로
  const dx = spot ? -(spot.cx + R) * (1 - SHOWN) : 0
  const dy = spot ? -(spot.cy + R) * (1 - SHOWN) : 0

  // 한 번만 민다. 배경도 같은 방향으로 시차만큼 (앞 화면이 사라지는 동안은 기다렸다가 — CSS transition-delay)
  const push = useRef({ dx, dy })
  push.current = { dx, dy }
  const ready = spot !== null
  useEffect(() => {
    if (!ready) return
    // 처음 자리에서 한 번 그려진 뒤에 옮겨야 움직임이 보인다
    const frame = requestAnimationFrame(() => {
      setPushed(true)
      panSkyBy(push.current.dx * PARALLAX, push.current.dy * PARALLAX)
    })
    return () => cancelAnimationFrame(frame)
  }, [ready])

  return (
    <div ref={ref} className={styles.layer} aria-hidden="true">
      {spot && (
        <div
          className={styles.sun}
          data-pushed={pushed}
          style={{ left: spot.cx, top: spot.cy, width: spot.sunD, '--push-x': `${dx}px`, '--push-y': `${dy}px` } as CSSProperties}
        >
          <JarvisRing state="done" className={styles.ring} surface corona still spin="always">
            {null}
          </JarvisRing>
        </div>
      )}
    </div>
  )
}
