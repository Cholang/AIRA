import { useEffect, useRef, useState, type ReactNode } from 'react'
import { inDesktop, onHostMessage, tellHost } from '../host'
import { scaleOf } from '../frame'
import GlowFrame from './GlowFrame'
import styles from './LiveView.module.css'

type Box = { x: number; y: number; w: number; h: number }

/**
 * 다른 프로그램 창의 한 칸을 이 자리에 실시간으로 (2026-10-11 — SharpCap 영상 칸). 실제로 그리는 것은 데스크톱 창(WindowThumbnail, DWM 썸네일)이고,
 * 화면은 **비춰도 되는 자리**만 알려 준다: 같은 화면의 글자·버튼(`data-occludes`) 위아래를 피한 띠. 썸네일은 웹 화면 위에 그려져 그 자리의 글자를 가리기 때문.
 * 비친 영상 둘레에는 빛 테두리(GlowFrame — 칼 같은 경계를 부드럽게, 2026-10-11 사용자 요청).
 * 비추지 않는 까닭을 나눈다 (CX-28-02·03): 열린 창(dialog)·화면 가려짐 = 잠깐 감춤(아무것도 안 그림) / 촬영용 적색 테마(썸네일은 빨강 필터를 못 거침 — 밤눈 보호)·
 * 자리 부족·데스크톱 창이 못 비춤(SharpCap 최소화·못 찾음)·브라우저 = fallback(창 캡처 사진, 적색 테마면 빨강 필터)
 */
export default function WindowView({ target, fallback }: { target: 'sharpcap'; fallback: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  const [state, setState] = useState<{ shown: boolean; rect: Box | null } | null>(null)
  // 화면 쪽에서 비추지 않기로 한 까닭: null = 비춤, hidden = 잠깐 감춤, fallback = 캡처 사진으로
  const [off, setOff] = useState<null | 'hidden' | 'fallback'>(null)
  const shown = off === null ? (state?.shown ?? null) : false
  const desktop = inDesktop()

  useEffect(() => {
    if (!desktop) return
    let last = ''
    const send = () => {
      const el = ref.current
      const hidden = !el || document.hidden || !!document.querySelector('dialog[open]')
      const night = document.documentElement.dataset.theme === 'night'
      const rect = hidden || night ? null : slotOf(el)
      setOff(hidden ? 'hidden' : night || !rect ? 'fallback' : null)
      const key = JSON.stringify(rect) + window.devicePixelRatio
      if (key === last) return
      last = key
      tellHost({ type: 'window-view', target, rect, dpr: window.devicePixelRatio })
    }
    send()
    const timer = window.setInterval(send, 250)
    const off = onHostMessage<{ target: string; shown: boolean; rect: Box | null }>('window-view-state', (d) => {
      if (d.target === target) setState({ shown: d.shown, rect: d.rect })
    })
    return () => {
      clearInterval(timer)
      off()
      tellHost({ type: 'window-view', target, rect: null })
    }
  }, [desktop, target])

  return (
    <>
      <div ref={ref} className={styles.slot} />
      {shown && state?.rect && <Glow at={state.rect} slot={ref.current} />}
      {(!desktop || (off !== 'hidden' && shown === false)) && fallback}
    </>
  )
}

/** 비친 영상 둘레의 빛 테두리 — 데스크톱 창이 알려 준 자리(화면 기준)를 이 칸 기준으로 */
function Glow({ at, slot }: { at: Box; slot: HTMLElement | null }) {
  const box = slot?.getBoundingClientRect()
  if (!slot || !box) return null
  // 화면 틀은 창 크기에 맞춰 줄여 그린다(scale) — 화면 기준 길이를 틀 안의 길이로 (2026-10-11 실기: 테두리가 0.8배로 작게 그려짐)
  const s = scaleOf(slot)
  return <GlowFrame style={{ position: 'absolute', left: (at.x - box.left) / s, top: (at.y - box.top) / s, width: at.w / s, height: at.h / s }} />
}

/** 비출 자리 (화면 기준 CSS 픽셀): 이 칸 안에서 위쪽 글자 아래 ~ 아래쪽 글자 위, 좌우 여백. 너무 작으면 null */
export function slotOf(el: HTMLElement): { x: number; y: number; w: number; h: number } | null {
  const box = el.getBoundingClientRect()
  const mid = box.top + box.height / 2
  let top = box.top
  let bottom = box.bottom
  for (const o of el.closest('main')?.querySelectorAll<HTMLElement>('[data-occludes]') ?? []) {
    const r = o.getBoundingClientRect()
    if (r.height === 0) continue
    if (r.top + r.height / 2 < mid) top = Math.max(top, r.bottom)
    else bottom = Math.min(bottom, r.top)
  }
  const gap = 16
  const x = box.left + 24
  const w = box.width - 48
  const y = top + gap
  const h = bottom - gap - y
  return w < 160 || h < 120 ? null : { x: Math.round(x), y: Math.round(y), w: Math.round(w), h: Math.round(h) }
}
