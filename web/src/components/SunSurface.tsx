import { useEffect, useRef } from 'react'
import { drawSunSurface, SUN_PERIOD_S } from '../render/sunSurface'
import type { RingState } from './JarvisRing'

/**
 * 도는 태양 표면 (2026-10-11 사용자 결정 — 시안 mockups/aira-sun-rotation.html, 엔진 켜기 원과 장비 연결 태양 두 곳만):
 * 원 테두리가 도는 대신 흑점·옅은 줄무늬가 왼쪽 가장자리에서 들어와 가운데서 가장 넓고 오른쪽에서 납작해지며 사라진다.
 * 위도·경도를 가진 점을 정사영(가로 = sin 경도, 납작함 = cos 경도)으로 그려 평면이어도 공이 도는 것처럼. 가장자리는 어둡게(주연 감광).
 * 연결 중 = 돎, 완료·실패 = 0.6초쯤에 걸쳐 멈춤(테두리 반짝임·점멸은 JarvisRing). 초당 30번까지만 그리고, 멈추면 그리기도 멈춘다.
 * 동작 줄이기면 멈춘 한 장. 색은 --conn-rgb (테마를 바꾸면 따라감). 이 컴포넌트는 수명(돌기·멈추기·크기·테마)만 맡고 그리기는 render/sunSurface.ts
 */
/** 지금 회전 각 — 화면이 바뀌어 원이 새로 그려져도(엔진 켜기 → 장비 연결) 무늬가 튀지 않고 이어지게 모든 원이 같이 쓴다 */
let sharedRot = 0

export default function SunSurface({ state, spin = true, className }: { state: RingState; spin?: boolean | 'always'; className?: string }) {
  const ref = useRef<HTMLCanvasElement>(null)
  // spin=false: 진행 중이어도 돌지 않는다 (장비 변경 모드 — 고르는 동안 계속 그리지 않게, 자체 점검 L5)
  const stateRef = useRef(state)
  // spin='always': 상태와 상관없이 늘 돈다 (장비 연결 이후의 태양 — 2026-10-11 사용자 요청)
  stateRef.current = spin === 'always' ? 'working' : spin ? state : 'done'
  const kick = useRef<() => void>(() => {})

  useEffect(() => {
    const cv = ref.current
    if (!cv) return
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    let rgb = '169,166,247'
    const readColor = () => {
      rgb = getComputedStyle(cv).getPropertyValue('--conn-rgb').trim().split(/\s+/).join(',') || rgb
    }
    readColor()
    let rot = sharedRot
    let spin = stateRef.current === 'working' ? 1 : 0
    let last = performance.now()
    let lastDraw = 0
    let frame = 0

    const draw = () => {
      const size = cv.clientWidth
      if (size === 0) return
      const d = Math.min(window.devicePixelRatio || 1, 2)
      if (cv.width !== Math.round(size * d)) {
        cv.width = Math.round(size * d)
        cv.height = Math.round(size * d)
      }
      const g = cv.getContext('2d')
      if (!g) return
      g.setTransform(d, 0, 0, d, 0, 0)
      drawSunSurface(g, size, rot, rgb)
    }

    const tick = (now: number) => {
      frame = 0
      const dt = Math.min(0.1, (now - last) / 1000)
      last = now
      const target = stateRef.current === 'working' && !still ? 1 : 0
      spin += (target - spin) * Math.min(1, dt / 0.25)
      rot += dt * spin * ((Math.PI * 2) / SUN_PERIOD_S)
      sharedRot = rot
      if (now - lastDraw >= 33) {
        lastDraw = now
        draw()
      }
      // 멈췄으면 그리기도 멈춘다 (다시 돌 때 kick)
      if (target === 0 && spin < 0.002) {
        spin = 0
        draw()
        return
      }
      frame = requestAnimationFrame(tick)
    }
    const start = () => {
      if (frame) return
      last = performance.now()
      frame = requestAnimationFrame(tick)
    }
    kick.current = start
    draw()
    start()

    // 테마를 바꾸면 색을 다시 읽고 다시 그린다
    const themeWatch = new MutationObserver(() => {
      readColor()
      draw()
    })
    themeWatch.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    const resize = new ResizeObserver(() => draw())
    resize.observe(cv)
    return () => {
      cancelAnimationFrame(frame)
      themeWatch.disconnect()
      resize.disconnect()
    }
  }, [])

  // 상태가 바뀌면(다시 연결 중으로 등) 멈춰 있던 그리기를 깨운다
  useEffect(() => kick.current(), [state, spin])

  return <canvas ref={ref} className={className} aria-hidden="true" />
}
