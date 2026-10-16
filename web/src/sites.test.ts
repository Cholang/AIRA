import { describe, expect, it } from 'vitest'
import type { Profile } from './profiles'
import { describeSite, parseCoords, sameSpot } from './sites'

describe('붙여 넣은 좌표 읽기 (parseCoords)', () => {
  it('소수 위도·경도 — 쉼표 · 띄어쓰기', () => {
    expect(parseCoords('37.6231, 128.7432')).toEqual({ latitude: 37.6231, longitude: 128.7432 })
    expect(parseCoords('  37.6231 128.7432 ')).toEqual({ latitude: 37.6231, longitude: 128.7432 })
  })

  it('도·분·초 — 방위가 앞에(구글) · 뒤에', () => {
    const a = parseCoords(`N 37° 37' 23.2", E 128° 44' 35.5"`)!
    expect(a.latitude).toBeCloseTo(37 + 37 / 60 + 23.2 / 3600, 6)
    expect(a.longitude).toBeCloseTo(128 + 44 / 60 + 35.5 / 3600, 6)
    const b = parseCoords(`37°37'23.2"N 128°44'35.5"E`)!
    expect(b.latitude).toBeCloseTo(a.latitude, 6)
    expect(b.longitude).toBeCloseTo(a.longitude, 6)
  })

  it('경도가 먼저 오면(동 … 북 …) 순서를 바꿔 읽는다 · 한글 방위', () => {
    const r = parseCoords(`동 128° 44' 35.5", 북 37° 37' 23.2"`)!
    expect(r.latitude).toBeCloseTo(37.6231, 3)
    expect(r.longitude).toBeCloseTo(128.7432, 3)
  })

  it('남반구 · 서경은 음수', () => {
    const r = parseCoords(`33°52'S 151°12'W`)!
    expect(r.latitude).toBeCloseTo(-(33 + 52 / 60), 6)
    expect(r.longitude).toBeCloseTo(-(151 + 12 / 60), 6)
  })

  it('읽을 수 없거나 범위 밖 · (0, 0)은 null', () => {
    expect(parseCoords('')).toBeNull()
    expect(parseCoords('서울')).toBeNull()
    expect(parseCoords('37.5')).toBeNull()
    expect(parseCoords('95, 127')).toBeNull()
    expect(parseCoords('37, 190')).toBeNull()
    expect(parseCoords('0, 0')).toBeNull()
  })
})

describe('지금 관측지와 프로필 맞추기 (describeSite)', () => {
  const home = { id: 'h', name: '집', latitude: 37.5, longitude: 127.0, elevation: 30 }
  const camp = { id: 'c', name: '캠핑장', latitude: 37.9, longitude: 128.5, elevation: 700 }
  const me = { id: 'me', sites: [home] } as unknown as Profile
  const other = { id: 'o', sites: [camp] } as unknown as Profile

  it('약 50m 안이면 같은 곳', () => {
    expect(sameSpot(home, { latitude: 37.5004, longitude: 127.0004 })).toBe(true)
    expect(sameSpot(home, { latitude: 37.501, longitude: 127.0 })).toBe(false)
  })

  it('내 목록에 있으면 saved, 다른 프로필에만 있으면 그 이름, 없으면 둘 다 null', () => {
    expect(describeSite({ latitude: 37.5, longitude: 127.0, elevation: 0 }, me, [me, other])).toEqual({ saved: home, otherName: null })
    expect(describeSite({ latitude: 37.9, longitude: 128.5, elevation: 0 }, me, [me, other])).toEqual({ saved: null, otherName: '캠핑장' })
    expect(describeSite({ latitude: 35, longitude: 129, elevation: 0 }, me, [me, other])).toEqual({ saved: null, otherName: null })
    expect(describeSite(null, me, [me])).toEqual({ saved: null, otherName: null })
  })
})
