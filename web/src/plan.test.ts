import { describe, expect, it } from 'vitest'
import { clockOf, displayName, type Chart } from './plan'

describe('그래프 시각', () => {
  // 일몰 18:30부터 센 분
  const chart = { startClock: 18 * 60 + 30 } as Chart

  it('분 → HH:mm', () => {
    expect(clockOf(chart, 0)).toBe('18:30')
    expect(clockOf(chart, 95)).toBe('20:05')
  })

  it('자정을 넘기면 다음 날 시각', () => {
    expect(clockOf(chart, 400)).toBe('01:10')
  })

  it('반올림, 일몰 전(음수)도', () => {
    expect(clockOf(chart, 0.6)).toBe('18:31')
    expect(clockOf(chart, -40)).toBe('17:50')
  })
})

describe('대상 이름', () => {
  it('번호 + 한국어 통칭, 없으면 영문 통칭, 둘 다 없으면 번호만', () => {
    expect(displayName({ name: 'M31', koreanName: '안드로메다 은하' })).toBe('M31 안드로메다 은하')
    expect(displayName({ name: 'M101', koreanName: null, commonName: 'Pinwheel' })).toBe('M101 Pinwheel')
    expect(displayName({ name: 'NGC 891', koreanName: null, commonName: null })).toBe('NGC 891')
  })
})
