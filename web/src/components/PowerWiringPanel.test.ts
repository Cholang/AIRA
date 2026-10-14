import { describe, expect, it } from 'vitest'
import { wiringSummary } from './PowerWiringPanel'

describe('전원 배선 한 줄 요약', () => {
  it('손대지 않으면 기본 배선 (적도의 먼저, 카메라 자체 전원)', () => {
    expect(wiringSummary({})).toBe('적도의 먼저 · 카메라 자체 전원')
  })

  it('켜는 순서', () => {
    expect(wiringSummary({ switch: 'dc', mount: 'switch' })).toBe('파워박스 먼저 · 카메라 자체 전원')
    expect(wiringSummary({ switch: 'dc', mount: 'dc' })).toBe('적도의·파워박스 따로 · 카메라 자체 전원')
  })

  it('카메라 전원', () => {
    expect(wiringSummary({ camera: 'switch' })).toBe('적도의 먼저 · 카메라 파워박스')
    expect(wiringSummary({ camera: 'mount' })).toBe('적도의 먼저 · 카메라 적도의')
  })
})
