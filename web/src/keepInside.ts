import { useLayoutEffect, useRef } from 'react'
import { scaleOf } from './frame'

/**
 * 행성 옆에 펼치는 목록이 잘리는 영역(`data-clip-box`) 밖으로 나가지 않게 세로로 민다 (CX-24-05: 화면 위쪽 행성의 긴 목록은 윗부분이 잘렸다).
 * 목록은 `transform: translateY(calc(-50% + var(--keep-shift, 0px)))`로 둔다. 목록 내용이 바뀌어 길이가 바뀌면 다시 잰다
 */
export function useKeepInside<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  useLayoutEffect(() => {
    const el = ref.current
    const box = el?.closest<HTMLElement>('[data-clip-box]')
    if (!el || !box) return
    const fit = () => {
      el.style.setProperty('--keep-shift', '0px')
      const s = scaleOf(el)
      const r = el.getBoundingClientRect()
      const b = box.getBoundingClientRect()
      const margin = 8 * s
      const shift = r.top < b.top + margin ? b.top + margin - r.top : r.bottom > b.bottom - margin ? b.bottom - margin - r.bottom : 0
      el.style.setProperty('--keep-shift', `${shift / s}px`)
    }
    fit()
    const ro = new ResizeObserver(fit)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])
  return ref
}
