import { describe, expect, it } from 'vitest'
import type { ChartPick } from '../plan'
import { altitudeOn, layoutTags, trackPath } from './nightGraphMath'

describe('시각의 고도 (5분 자료 사이를 잇기)', () => {
  const track: [number, number][] = [
    [0, 10],
    [5, 20],
    [10, 30],
  ]
  it('자료 사이는 직선으로', () => {
    expect(altitudeOn(track, 2.5)).toBe(15)
    expect(altitudeOn(track, 5)).toBe(20)
  })
  it('자료 밖은 양 끝 값', () => {
    expect(altitudeOn(track, -5)).toBe(10)
    expect(altitudeOn(track, 99)).toBe(30)
  })
  it('자료가 없으면 0', () => {
    expect(altitudeOn([], 3)).toBe(0)
  })
})

describe('고도 곡선', () => {
  const x = (t: number) => t
  const y = (a: number) => 100 - a
  it('지평선 아래로 내려가면 끊고, 다시 올라오면 새로 시작', () => {
    expect(
      trackPath(
        [
          [0, 10],
          [5, -2],
          [10, 5],
        ],
        x,
        y,
      ),
    ).toBe('M0.0,90.0M10.0,95.0')
  })
  it('앞뒤 점을 붙여 잇는다 (촬영 구간을 정확한 시작·끝까지)', () => {
    expect(trackPath([[5, 20]], x, y, [0, 10], [10, 30])).toBe('M0.0,90.0L5.0,80.0L10.0,70.0')
  })
})

describe('대상 이름표 줄', () => {
  const pick = (id: string, name: string) => ({ id, name, koreanName: null }) as ChartPick
  const at = (xs: Record<string, number>) => (p: ChartPick) => ({ x: xs[p.id], y: 0 })

  it('멀리 떨어지면 모두 첫 줄, 왼쪽부터', () => {
    const tags = layoutTags([pick('b', 'M33'), pick('a', 'M31')], at({ a: 100, b: 600 }))
    expect(tags.map((t) => [t.p.id, t.row])).toEqual([
      ['a', 0],
      ['b', 0],
    ])
  })

  it('앞 이름표와 겹치면 아래 줄', () => {
    const tags = layoutTags([pick('a', 'M31'), pick('b', 'M33')], at({ a: 100, b: 120 }))
    expect(tags.map((t) => t.row)).toEqual([0, 1])
  })

  it('이름표 너비는 글자 수에 비례', () => {
    const [t] = layoutTags([pick('a', 'NGC 7000')], at({ a: 0 }))
    expect(t.w).toBe(8 * 13.5)
  })
})
