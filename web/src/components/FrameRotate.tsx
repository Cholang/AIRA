import { useEffect, useId, useRef, useState, type KeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react'
import Button from './Button'
import { scaleOf } from '../frame'
import { normDeg, turnText } from '../framing'
import styles from './FrameRotate.module.css'

/** 사진이 놓인 자리 (하늘 화면 기준 틀 px — LiveView의 FramedImage가 알려 줌) */
export type PhotoBox = { left: number; top: number; width: number; height: number }

/** 꼭짓점에서 이만큼(틀 px) 안에 커서가 오면 네 꼭짓점에 손잡이 */
const NEAR = 80
/** 손잡이: 꼭짓점을 중심으로 바깥쪽에 굽은 선 (반지름 R, 바깥 방향에서 ±A°) */
const R = 26
const A = 40

const r2 = (n: number) => Math.round(n * 100) / 100

/** 호 끝(theta°)에서 호를 따라 바깥으로 향한 화살촉. dir = 1이면 각이 커지는 쪽 */
function arrowHead(theta: number, dir: 1 | -1) {
  const t = (theta * Math.PI) / 180
  const tip = [R * Math.cos(t), R * Math.sin(t)]
  const along = [-Math.sin(t) * dir, Math.cos(t) * dir]
  const out = [Math.cos(t), Math.sin(t)]
  const back = [tip[0] - along[0] * 7, tip[1] - along[1] * 7]
  return `M${r2(back[0] + out[0] * 5)} ${r2(back[1] + out[1] * 5)} L${r2(tip[0])} ${r2(tip[1])} L${r2(back[0] - out[0] * 5)} ${r2(back[1] - out[1] * 5)}`
}

/** 굽은 양방향 화살표 (바깥 방향 = +x) */
const GLYPH = (() => {
  const a = (A * Math.PI) / 180
  const x = r2(R * Math.cos(a))
  const y = r2(R * Math.sin(a))
  return `M${x} ${-y} A${R} ${R} 0 0 1 ${x} ${y} ${arrowHead(A, 1)} ${arrowHead(-A, -1)}`
})()

/** 틀(사진과 같은 크기)을 가운데를 중심으로 deg° 돌린 네 꼭짓점 — 왼쪽 위부터 시계 방향 */
function cornersOf(deg: number, w: number, h: number) {
  const rad = (deg * Math.PI) / 180
  const cx = w / 2
  const cy = h / 2
  return [
    [0, 0],
    [w, 0],
    [w, h],
    [0, h],
  ].map(([x, y]) => [cx + (x - cx) * Math.cos(rad) - (y - cy) * Math.sin(rad), cy + (x - cx) * Math.sin(rad) + (y - cy) * Math.cos(rad)])
}

/**
 * 구도 회전 (DESIGN.md "하늘 화면" — 센터링 사진, 2026-10-16 사용자 결정): 센터링을 마친 사진 위의 틀(찍히는 범위)을 돌려 원하는 구도를 정한다.
 * 꼭짓점 가까이에 커서가 가면 네 꼭짓점에 굽은 양방향 화살표, 누른 채 돌리면 틀이 사진 가운데를 중심으로 돈다(사진은 그대로).
 * 틀 밖의 사진은 어둡게, 틀 안인데 사진이 없는 곳은 회색(--frame-void) — 이렇게 찍힌다는 것만 보이면 된다.
 * 놓으면 사진 가운데에 "카메라를 ○ 방향으로 N° 돌려 주세요" + 다시 찍기 · 되돌리기. 각도는 화면 시계 방향 +.
 * angle = 서버가 준 이 사진의 틀 각도(남은 회전) — 새 사진이면 화면이 key를 바꿔 처음부터 그린다
 */
export default function FrameRotate({
  box,
  angle,
  tolerance,
  onSet,
  onCheck,
}: {
  box: PhotoBox
  angle: number
  /** 이 안이면 돌리지 않은 것으로 (서버 Framing.ToleranceDeg) */
  tolerance: number
  onSet: (turnDeg: number) => Promise<string | null>
  onCheck: () => Promise<string | null>
}) {
  const ref = useRef<HTMLDivElement>(null)
  const clip = useId()
  const [deg, setDeg] = useState(angle)
  const [near, setNear] = useState(false)
  const [focused, setFocused] = useState(false)
  const [drag, setDrag] = useState<{ from: number; base: number } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const w = box.width
  const h = box.height
  const cx = w / 2
  const cy = h / 2
  const corners = cornersOf(deg, w, h)
  const frame = `M${corners.map(([x, y]) => `${r2(x)} ${r2(y)}`).join(' L')} Z`
  const photo = `M0 0 H${r2(w)} V${r2(h)} H0 Z`

  // 꼭짓점 가까이에 커서가 있는지 (사진 위를 막지 않게 창 전체에서 본다)
  useEffect(() => {
    const onMove = (e: PointerEvent) => {
      const el = ref.current
      if (!el) return
      const b = el.getBoundingClientRect()
      const s = scaleOf(el)
      const x = (e.clientX - b.left) / s
      const y = (e.clientY - b.top) / s
      const close = cornersOf(deg, w, h).some(([px, py]) => Math.hypot(x - px, y - py) < NEAR)
      setNear((prev) => (prev === close ? prev : close))
    }
    window.addEventListener('pointermove', onMove)
    return () => window.removeEventListener('pointermove', onMove)
  }, [deg, w, h])

  const save = async (d: number) => {
    const next = Math.abs(normDeg(d)) <= tolerance ? 0 : normDeg(d)
    setDeg(next)
    setBusy(true)
    setError(await onSet(next))
    setBusy(false)
  }
  const check = async () => {
    setBusy(true)
    setError(await onCheck())
    setBusy(false)
  }

  // 사진 가운데를 중심으로 커서의 각도 (화면 좌표 — 배율과 상관없음)
  const angleAt = (e: ReactPointerEvent) => {
    const b = ref.current!.getBoundingClientRect()
    return (Math.atan2(e.clientY - (b.top + b.height / 2), e.clientX - (b.left + b.width / 2)) * 180) / Math.PI
  }
  const down = (e: ReactPointerEvent<SVGGElement>) => {
    if (busy) return
    e.currentTarget.setPointerCapture(e.pointerId)
    setDrag({ from: angleAt(e), base: deg })
  }
  const move = (e: ReactPointerEvent<SVGGElement>) => {
    if (drag) setDeg(normDeg(drag.base + angleAt(e) - drag.from))
  }
  const up = () => {
    if (!drag) return
    setDrag(null)
    void save(deg)
  }
  // 키보드: ←/→ 1° (Shift 5°)
  const key = (e: KeyboardEvent<SVGGElement>) => {
    const step = e.shiftKey ? 5 : 1
    if (e.key === 'ArrowRight' || e.key === 'ArrowUp') void save(deg + step)
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') void save(deg - step)
    else return
    e.preventDefault()
  }

  const turned = Math.abs(deg) > tolerance
  const { way, amount } = turnText(deg)
  return (
    <div
      ref={ref}
      className={styles.layer}
      style={{ left: box.left, top: box.top, width: w, height: h }}
      data-show={near || focused || !!drag}
      data-drag={!!drag}
    >
      <svg viewBox={`0 0 ${r2(w)} ${r2(h)}`}>
        <defs>
          <clipPath id={`${clip}p`}>
            <path d={photo} />
          </clipPath>
          <clipPath id={`${clip}f`}>
            <path d={frame} />
          </clipPath>
        </defs>
        {deg !== 0 && (
          <>
            {/* 틀 밖의 사진은 어둡게, 틀 안인데 사진이 없는 곳은 회색 */}
            <path className={styles.dim} d={`${photo} ${frame}`} fillRule="evenodd" clipPath={`url(#${clip}p)`} />
            <path className={styles.void} d={`${photo} ${frame}`} fillRule="evenodd" clipPath={`url(#${clip}f)`} />
            <path className={styles.outline} d={frame} />
          </>
        )}
        {corners.map(([x, y], i) => (
          <g
            key={i}
            className={styles.handle}
            transform={`translate(${r2(x)} ${r2(y)}) rotate(${r2((Math.atan2(y - cy, x - cx) * 180) / Math.PI)})`}
            tabIndex={i === 1 ? 0 : -1}
            role={i === 1 ? 'slider' : undefined}
            aria-label={i === 1 ? '구도 돌리기 (화살표 키로 1°씩)' : undefined}
            aria-valuemin={i === 1 ? -180 : undefined}
            aria-valuemax={i === 1 ? 180 : undefined}
            aria-valuenow={i === 1 ? Math.round(deg) : undefined}
            onPointerDown={down}
            onPointerMove={move}
            onPointerUp={up}
            onPointerCancel={up}
            onKeyDown={key}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
          >
            <circle className={styles.hit} cx={R * 0.8} cy={0} r={22} />
            <path className={styles.halo} d={GLYPH} />
            <path className={styles.mark} d={GLYPH} />
          </g>
        ))}
      </svg>

      {drag && turned && (
        <div className={styles.live} style={{ left: cx, top: cy }}>
          {way} {amount}
        </div>
      )}
      {!drag && turned && (
        <div className={styles.card} style={{ left: cx, top: cy }} role="status">
          <p className={styles.turn}>
            카메라를 <b>{way}</b>으로 <b>{amount}</b> 돌려 주세요
          </p>
          <p className={styles.note}>카메라 뒤에서 볼 때</p>
          {error && <p className={styles.error}>{error}</p>}
          <div className={styles.buttons}>
            <Button surface="sky" size="sm" onClick={() => void check()} disabled={busy}>
              다시 찍기
            </Button>
            <Button surface="sky" size="sm" variant="ghost" onClick={() => void save(0)} disabled={busy}>
              되돌리기
            </Button>
          </div>
        </div>
      )}
      {!turned && error && (
        <p className={styles.card} style={{ left: cx, top: cy }} role="status">
          {error}
        </p>
      )}
    </div>
  )
}
