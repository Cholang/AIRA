import { describe, expect, it } from 'vitest'
import { hasFinalConsonant, PRODUCT, withRo } from './product'

describe('받침 (조사 고르기)', () => {
  it('한글은 마지막 글자의 받침으로', () => {
    expect(hasFinalConsonant('성운')).toBe(true)
    expect(hasFinalConsonant('플레이아데스')).toBe(false)
  })

  it('숫자는 읽는 소리로: 영·일·삼·육·칠·팔은 받침 있음', () => {
    for (const w of ['NGC 7000', 'M31', 'M33', 'M16', 'M27', 'M8']) expect(hasFinalConsonant(w), w).toBe(true)
    for (const w of ['M42', 'M45', 'M2', 'IC 1805', 'M9']) expect(hasFinalConsonant(w), w).toBe(false)
  })

  it('영문은 엘·엠·엔·알만 받침 있음', () => {
    expect(hasFinalConsonant('Veil')).toBe(true)
    expect(hasFinalConsonant('Orion')).toBe(true)
    expect(hasFinalConsonant('Heart')).toBe(false)
    expect(hasFinalConsonant('NGC')).toBe(false)
  })

  it('뒤 공백은 무시, 빈 문자열은 받침 없음', () => {
    expect(hasFinalConsonant('M31  ')).toBe(true)
    expect(hasFinalConsonant('')).toBe(false)
  })
})

describe('로·으로', () => {
  it('받침이 없거나 ㄹ이면 "로"', () => {
    expect(withRo('M42')).toBe('M42로')
    expect(withRo('M31')).toBe('M31로') // 일
    expect(withRo('M27')).toBe('M27로') // 칠
    expect(withRo('M8')).toBe('M8로') // 팔
    expect(withRo('서울')).toBe('서울로')
    expect(withRo('Veil')).toBe('Veil로')
  })

  it('ㄹ이 아닌 받침이면 "으로"', () => {
    expect(withRo('NGC 7000')).toBe('NGC 7000으로') // 영
    expect(withRo('M33')).toBe('M33으로') // 삼
    expect(withRo('M16')).toBe('M16으로') // 육
    expect(withRo('북아메리카 성운')).toBe('북아메리카 성운으로')
    expect(withRo('Orion')).toBe('Orion으로')
  })
})

describe('제품 이름의 조사', () => {
  it('이름의 받침에 맞춰 이/가 · 을/를 · 은/는 · 과/와', () => {
    const f = hasFinalConsonant(PRODUCT.name)
    expect(PRODUCT.ga).toBe(PRODUCT.name + (f ? '이' : '가'))
    expect(PRODUCT.reul).toBe(PRODUCT.name + (f ? '을' : '를'))
    expect(PRODUCT.neun).toBe(PRODUCT.name + (f ? '은' : '는'))
    expect(PRODUCT.wa).toBe(PRODUCT.name + (f ? '과' : '와'))
  })
})
