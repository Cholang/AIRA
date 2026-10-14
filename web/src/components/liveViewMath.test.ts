import { describe, expect, it } from 'vitest'
import { fitInside, quadFit } from './liveViewMath'

describe('사진 맞춤', () => {
  it('비율을 지켜 상자 가운데', () => {
    expect(fitInside({ w: 200, h: 100 }, { x: 10, y: 20, w: 100, h: 100 })).toEqual({ left: 10, top: 45, width: 100, height: 50 })
    expect(fitInside({ w: 100, h: 200 }, { x: 0, y: 0, w: 100, h: 100 })).toEqual({ left: 25, top: 0, width: 50, height: 100 })
  })
})

describe('초점 곡선 맞춤 (y = a x² + b x + c)', () => {
  // 포커서 위치는 수만 단위 — 가운데로 옮겨 계산해도 원래 위치로 돌아와야 한다
  const curve = (x: number) => 0.0001 * (x - 30000) ** 2 + 2
  const pts = [29000, 29500, 30000, 30500, 31000].map((x): [number, number] => [x, curve(x)])

  it('정확한 2차 곡선이면 그대로, 가장 낮은 곳은 30000', () => {
    const fit = quadFit(pts)
    expect(fit).not.toBeNull()
    const [a, b, c] = fit!
    expect(a).toBeCloseTo(0.0001, 10)
    expect(b).toBeCloseTo(-6, 6)
    expect(c).toBeCloseTo(90002, 2)
    expect(-b / (2 * a)).toBeCloseTo(30000, 3)
  })

  it('측정이 흔들려도 가장 낮은 곳 근처', () => {
    const noisy = pts.map(([x, y], i): [number, number] => [x, y + (i % 2 ? 0.3 : -0.2)])
    const [a, b] = quadFit(noisy)!
    expect(Math.abs(-b / (2 * a) - 30000)).toBeLessThan(50)
  })

  it('위로 볼록하면(초점 곡선이 아님) 맞추지 않음', () => {
    expect(quadFit(pts.map(([x, y]): [number, number] => [x, -y]))).toBeNull()
  })

  it('위치가 하나뿐이면 맞추지 않음', () => {
    expect(
      quadFit([
        [30000, 3],
        [30000, 2.5],
        [30000, 2.8],
      ]),
    ).toBeNull()
  })
})
