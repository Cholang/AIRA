import table from '../../src/Astro.Core/UiText.json'
import { hasFinalConsonant, PRODUCT, withRo } from './product'

/**
 * 화면 고정 문구 표 (2026-10-18 사용자 결정 — 게임의 텍스트 키처럼): 버튼 · 상단 제목 · 상태 줄 · 단계 레일의 글자는 코드에 쓰지 않고 키로 부른다.
 * 값은 src/Astro.Core/UiText.json 한 곳(서버 Astro.Core.UiText도 같은 파일)에 있다 — 같은 뜻의 버튼은 같은 키를 쓰면 단어가 저절로 같다.
 * 바뀌는 자리는 {이름}, 조사가 앞말에 맞춰 바뀌는 자리는 {이름}(으)로 · {이름}을(를) · {이름}이(가) · {이름}은(는) · {이름}와(과). {product}는 제품 이름
 */
export type TextKey = Exclude<keyof typeof table, `_${string}`>

const josa = (w: string, withFinal: string, withoutFinal: string) => w + (hasFinalConsonant(w) ? withFinal : withoutFinal)

/** {이름}과 조사 표시를 채운다 (서버 UiText.Fill과 같은 규칙). 모르는 이름은 그대로 둔다 */
export function fill(template: string, values: Record<string, string | number> = {}) {
  return template.replace(/\{(\w+)\}(\(으\)로|을\(를\)|이\(가\)|은\(는\)|와\(과\))?/g, (all, name: string, mark?: string) => {
    const v = name === 'product' ? PRODUCT.name : values[name]
    if (v === undefined) return all
    const w = String(v)
    switch (mark) {
      case '(으)로':
        return withRo(w)
      case '을(를)':
        return josa(w, '을', '를')
      case '이(가)':
        return josa(w, '이', '가')
      case '은(는)':
        return josa(w, '은', '는')
      case '와(과)':
        return josa(w, '과', '와')
      default:
        return w
    }
  })
}

/** 키의 문구 */
export const T = (key: TextKey) => fill(table[key])

/** 바뀌는 자리를 채운 문구 — 예: TF('button.slew.to', { target: 'M31' }) → "M31로 이동" */
export const TF = (key: TextKey, values: Record<string, string | number>) => fill(table[key], values)
