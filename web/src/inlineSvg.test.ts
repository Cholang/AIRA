import { describe, expect, it } from 'vitest'
import { scopedSvg } from './inlineSvg'

// 로고처럼 <style>·id·url(#…)·href="#…"를 가진 그림
const raw =
  '<svg viewBox="0 0 10 10" data-theme="default"><style>svg{--ink:#fff}svg[data-theme="night"]{--ink:#a00}.ink{stroke:url(#g)}#mark .ink{fill:none}</style>' +
  '<defs><linearGradient id="g"/><path id="spark" d="M0 0"/></defs><g id="mark"><use href="#spark"/><path class="ink" d="M1 1"/></g></svg>'

describe('인라인 SVG 가두기 (scopedSvg)', () => {
  const out = scopedSvg(raw, 'logo', 'night')

  it('바깥 svg에 이름표, data-theme은 지금 테마', () => {
    expect(out).toMatch(/^<svg class="logo"/)
    expect(out).toContain('data-theme="night"')
  })

  it('id와 그 id를 가리키는 곳(url · href · 선택자)을 모두 이름표로 바꾼다', () => {
    expect(out).toContain('id="logo-g"')
    expect(out).toContain('id="logo-spark"')
    expect(out).toContain('id="logo-mark"')
    expect(out).toContain('url(#logo-g)')
    expect(out).toContain('href="#logo-spark"')
    expect(out).toContain('.logo #logo-mark .ink{')
    expect(out).not.toMatch(/id="(g|spark|mark)"/)
  })

  it('스타일 선택자는 그림 안으로만 — svg{…}는 .logo{…}, 나머지는 .logo 아래로 (앱 아이콘에 번지던 2026-10-11 버그)', () => {
    expect(out).toContain('.logo{--ink:#fff}')
    expect(out).toContain('.logo[data-theme="night"]{--ink:#a00}')
    expect(out).toContain('.logo .ink{')
    expect(out).not.toMatch(/<style>svg\{/)
  })

  it('같은 그림을 다른 이름표로 두 번 넣어도 id가 겹치지 않는다', () => {
    const other = scopedSvg(raw, 'mark2', 'dark')
    expect(other).toContain('id="mark2-g"')
    expect(other).not.toContain('logo-')
  })
})
