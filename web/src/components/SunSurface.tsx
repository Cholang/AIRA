import { useEffect, useRef } from 'react'
import type { RingState } from './JarvisRing'

/**
 * 도는 태양 표면 (2026-10-11 사용자 결정 — 시안 mockups/aira-sun-rotation.html, 엔진 켜기 원과 장비 연결 태양 두 곳만):
 * 원 테두리가 도는 대신 흑점·옅은 줄무늬가 왼쪽 가장자리에서 들어와 가운데서 가장 넓고 오른쪽에서 납작해지며 사라진다.
 * 위도·경도를 가진 점을 정사영(가로 = sin 경도, 납작함 = cos 경도)으로 그려 평면이어도 공이 도는 것처럼. 가장자리는 어둡게(주연 감광).
 * 연결 중 = 돎, 완료·실패 = 0.6초쯤에 걸쳐 멈춤(테두리 반짝임·점멸은 JarvisRing). 초당 30번까지만 그리고, 멈추면 그리기도 멈춘다.
 * 동작 줄이기면 멈춘 한 장. 색은 --conn-rgb (테마를 바꾸면 따라감)
 */
const PERIOD_S = 14
const INK = 0.16

/** 지금 회전 각 — 화면이 바뀌어 원이 새로 그려져도(엔진 켜기 → 장비 연결) 무늬가 튀지 않고 이어지게 모든 원이 같이 쓴다 */
let sharedRot = 0

type Feature = { kind: 'spot' | 'band'; lat: number; lon: number; size: number }

function seeded(n: number) {
  let s = n
  return () => ((s = (s * 16807) % 2147483647) - 1) / 2147483646
}

const FEATURES: Feature[] = (() => {
  const r = seeded(7)
  return [
    ...Array.from({ length: 7 }, (): Feature => ({ kind: 'spot', lat: (r() * 2 - 1) * 38, lon: r() * Math.PI * 2, size: 0.035 + r() * 0.05 })),
    ...Array.from({ length: 9 }, (): Feature => ({ kind: 'band', lat: (r() * 2 - 1) * 55, lon: r() * Math.PI * 2, size: 0.18 + r() * 0.22 })),
  ]
})()

export default function SunSurface({ state, className }: { state: RingState; className?: string }) {
  const ref = useRef<HTMLCanvasElement>(null)
  const stateRef = useRef(state)
  stateRef.current = state
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
      g.clearRect(0, 0, size, size)
      const R = size / 2
      const c = R
      // 표면: 가운데 밝고 가장자리 어둡게
      const surf = g.createRadialGradient(c - R * 0.15, c - R * 0.12, R * 0.05, c, c, R)
      surf.addColorStop(0, `rgba(${rgb},${INK * 0.55})`)
      surf.addColorStop(0.7, `rgba(${rgb},${INK * 0.25})`)
      surf.addColorStop(1, `rgba(${rgb},0)`)
      g.fillStyle = surf
      g.beginPath()
      g.arc(c, c, R, 0, Math.PI * 2)
      g.fill()
      // 흑점·줄무늬: 앞면만, 정사영
      for (const f of FEATURES) {
        const lon = f.lon + rot
        const front = Math.cos(lon)
        if (front <= 0.02) continue
        const lat = (f.lat * Math.PI) / 180
        const x = c + R * Math.cos(lat) * Math.sin(lon)
        const y = c - R * Math.sin(lat)
        const fade = Math.pow(front, 0.6)
        g.save()
        g.translate(x, y)
        if (f.kind === 'spot') {
          const h = R * f.size
          const w = h * front
          const sg = g.createRadialGradient(0, 0, 0, 0, 0, h)
          sg.addColorStop(0, `rgba(${rgb},${INK * 1.6 * fade})`)
          sg.addColorStop(0.55, `rgba(${rgb},${INK * 0.9 * fade})`)
          sg.addColorStop(1, `rgba(${rgb},0)`)
          g.scale(Math.max(0.05, w / h), 1)
          g.fillStyle = sg
          g.beginPath()
          g.arc(0, 0, h, 0, Math.PI * 2)
          g.fill()
        } else {
          const len = R * f.size * front
          const bg = g.createLinearGradient(-len, 0, len, 0)
          bg.addColorStop(0, `rgba(${rgb},0)`)
          bg.addColorStop(0.5, `rgba(${rgb},${INK * 0.8 * fade})`)
          bg.addColorStop(1, `rgba(${rgb},0)`)
          g.fillStyle = bg
          g.beginPath()
          g.ellipse(0, 0, len, R * 0.012, 0, 0, Math.PI * 2)
          g.fill()
        }
        g.restore()
      }
    }

    const tick = (now: number) => {
      frame = 0
      const dt = Math.min(0.1, (now - last) / 1000)
      last = now
      const target = stateRef.current === 'working' && !still ? 1 : 0
      spin += (target - spin) * Math.min(1, dt / 0.25)
      rot += dt * spin * ((Math.PI * 2) / PERIOD_S)
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
  useEffect(() => kick.current(), [state])

  return <canvas ref={ref} className={className} aria-hidden="true" />
}
