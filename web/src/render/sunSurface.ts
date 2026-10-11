/**
 * 태양 표면 그리기 — React와 상관없는 순수 그리기 함수 (2026-10-11 자체 점검 L6: 화면 수명(SunSurface.tsx)과 그리기를 나눔).
 * 흑점·옅은 줄무늬는 위도·경도를 가진 점이고, rot(라디안)만큼 돈 모습을 정사영(가로 = sin 경도, 납작함 = cos 경도)으로 그린다 —
 * 가운데서 가장 넓고 가장자리에서 납작해지며 뒤로 넘어가면 사라진다. 가장자리는 어둡게(주연 감광). 시안 mockups/aira-sun-rotation.html
 */

/** 한 바퀴 도는 시간(초) */
export const SUN_PERIOD_S = 14
/** 무늬 진하기 */
const INK = 0.16

type Feature = { kind: 'spot' | 'band'; lat: number; lon: number; size: number }

function seeded(n: number) {
  let s = n
  return () => ((s = (s * 16807) % 2147483647) - 1) / 2147483646
}

/** 늘 같은 무늬 (씨앗 고정) — 흑점 7개 · 줄무늬 9개 */
const FEATURES: Feature[] = (() => {
  const r = seeded(7)
  return [
    ...Array.from({ length: 7 }, (): Feature => ({ kind: 'spot', lat: (r() * 2 - 1) * 38, lon: r() * Math.PI * 2, size: 0.035 + r() * 0.05 })),
    ...Array.from({ length: 9 }, (): Feature => ({ kind: 'band', lat: (r() * 2 - 1) * 55, lon: r() * Math.PI * 2, size: 0.18 + r() * 0.22 })),
  ]
})()

/**
 * size×size(CSS 픽셀) 원 하나를 그린다. g는 이미 화면 배율이 적용된 상태(setTransform)여야 한다.
 * rgb = "169,166,247" 꼴 (테마의 --conn-rgb)
 */
export function drawSunSurface(g: CanvasRenderingContext2D, size: number, rot: number, rgb: string) {
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
