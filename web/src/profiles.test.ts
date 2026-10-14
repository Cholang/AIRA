import { describe, expect, it } from 'vitest'
import { clampBytes, NICKNAME_MAX_BYTES, textBytes } from './profiles'

describe('글자 수 (byte, 서버 ProfileStore.TextBytes와 같은 규칙)', () => {
  it('영문·숫자·기호 1, 한글 2', () => {
    expect(textBytes('M31 ')).toBe(4)
    expect(textBytes('안드')).toBe(4)
    expect(textBytes('M31 안드')).toBe(8)
    expect(textBytes('')).toBe(0)
  })

  it('별명은 한글 10자까지', () => {
    expect(clampBytes('가나다라마바사아자차카타', NICKNAME_MAX_BYTES)).toBe('가나다라마바사아자차')
  })

  it('넘는 글자는 통째로 뺀다 (한글을 반으로 자르지 않음)', () => {
    expect(clampBytes('abc가', 4)).toBe('abc')
    expect(clampBytes('가나', 4)).toBe('가나')
  })

  it('이모지를 반으로 잘라 깨진 글자를 남기지 않는다', () => {
    expect(clampBytes('ab😀', 4)).toBe('ab')
    expect(clampBytes('ab😀', 6)).toBe('ab😀')
  })
})
