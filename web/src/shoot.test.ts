import { describe, expect, it } from 'vitest'
import { dewText, guideWord, hm } from './shoot'

describe('가이딩 판정 (주 카메라 한 픽셀 기준, 준비 ⑥과 같음)', () => {
  const pixel = 2.41
  it('한 픽셀 이하 충분', () => {
    expect(guideWord(0.8, pixel)).toBe('충분해요')
    expect(guideWord(pixel, pixel)).toBe('충분해요')
  })
  it('1.5배 이하 지켜보기', () => {
    expect(guideWord(3.0, pixel)).toBe('지켜볼게요')
    expect(guideWord(pixel * 1.5, pixel)).toBe('지켜볼게요')
  })
  it('그 위 아쉬움', () => {
    expect(guideWord(3.7, pixel)).toBe('아쉬워요')
  })
})

describe('이슬·열선 문구', () => {
  it('둘 다 있으면 가운뎃점으로', () => {
    expect(dewText({ marginC: 16.4, heaterPower: 0, heaterAuto: true })).toBe('이슬 여유 16°C · 열선 0% (자동)')
  })
  it('있는 것만', () => {
    expect(dewText({ marginC: null, heaterPower: 0.35, heaterAuto: false })).toBe('열선 35%')
    expect(dewText({ marginC: 3.6, heaterPower: null, heaterAuto: false })).toBe('이슬 여유 4°C')
  })
  it('아무것도 없으면 숨김', () => {
    expect(dewText(null)).toBeNull()
    expect(dewText({ marginC: null, heaterPower: null, heaterAuto: false })).toBeNull()
  })
})

describe('시간 표시', () => {
  it('정각은 "n시간", 아니면 "n시간 m분", 1시간 아래는 "m분"', () => {
    expect(hm(3600)).toBe('1시간')
    expect(hm(5400)).toBe('1시간 30분')
    expect(hm(1200)).toBe('20분')
    expect(hm(0)).toBe('0분')
  })
  it('분으로 반올림', () => {
    expect(hm(3570)).toBe('1시간')
  })
})
