import { X } from 'lucide-react'
import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react'
import { useFrame } from '../frame'
import GlowFrame from './GlowFrame'
import styles from './Modal.module.css'

/**
 * 가운데 뜨는 창의 껍데기 하나 (DESIGN.md "공통 부품" — 팝업, 시안 mockups/aira-modal.html, 2026-10-14 사용자 결정).
 * 배경(덜 어둡게 — 하늘이 비침) · 유리 카드(반투명 + 뒤 흐림 + 옅은 외곽선) · 크기 sm 470 / md 600 / lg 800 ·
 * 제목 줄(제목 + 닫기, title이 있을 때) · 본문 · 버튼 줄(오른쪽, 주 버튼 하나는 쓰는 쪽이 정함).
 * tone="device": 적도의가 움직이거나 앱이 꺼지는 확인 — 테두리에 빛(GlowFrame)이 돈다. 촬영(적색) 테마에서는 돌지 않는 옅은 빛.
 * 브라우저 <dialog>(showModal)라 키보드 초점이 창 안에 머문다. Esc·바깥 누름·닫기는 dismissable일 때만 onClose.
 * 나타날 때는 카드 면·흐림·그림자가 짙어지고 안의 글자·버튼만 투명도로 (Modal.module.css). 문구는 쓰는 쪽이 넘긴다 — 이 부품은 모양만.
 * 주의: 카드나 그 조상에 투명도가 걸려 있으면 뒤 흐림이 꺼진다 → 카드에는 투명도 애니메이션을 걸지 않고, 배경 어둡게는 ::backdrop에
 */
export default function Modal({
  open,
  onClose,
  size = 'sm',
  tone,
  title,
  dismissable = true,
  initialFocus,
  actions,
  children,
}: {
  open: boolean
  /** Esc·바깥 누름·닫기(×) — dismissable일 때만 부른다 */
  onClose: () => void
  /** sm: 확인 한 문장 · md: 몇 문장·목록 · lg: 설정처럼 칸이 많은 창 */
  size?: 'sm' | 'md' | 'lg'
  /** device: 장비가 움직이거나 앱이 꺼지는 확인 (빛 테두리) */
  tone?: 'device'
  /** 있으면 제목 줄(제목 + 닫기) */
  title?: ReactNode
  /** false면 Esc·바깥 누름으로 닫히지 않고 닫기(×)도 없다 (고르지 않고 넘어가면 안 되는 질문, 처리 중) */
  dismissable?: boolean
  /** 처음 초점 (없으면 창 자체) — 확인 창은 안전한 쪽(취소)에 */
  initialFocus?: RefObject<HTMLElement | null>
  /** 버튼 줄 (오른쪽 정렬) */
  actions?: ReactNode
  children?: ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  // 창은 맨 위 층(top layer)에 떠서 앱 틀의 확대·축소를 받지 않는다 → 같은 배율을 직접 건다 (DESIGN.md 1장 "기준 화면")
  const frame = useFrame()

  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) {
      d.showModal()
      initialFocus?.current?.focus()
    } else if (!open && d.open) d.close()
  }, [open, initialFocus])

  // 촬영(적색) 테마에서는 빛이 돌지 않게 — 어둠 적응을 깨지 않는다
  const still = typeof document !== 'undefined' && document.documentElement.dataset.theme === 'night'

  const card = (
    <div className={styles.card}>
      {title !== undefined && (
        <div className={styles.head}>
          <h2 id={titleId}>{title}</h2>
          {dismissable && (
            <button type="button" className={styles.close} onClick={onClose} aria-label="닫기">
              <X strokeWidth={2} aria-hidden="true" />
            </button>
          )}
        </div>
      )}
      <div className={styles.body}>{children}</div>
      {actions && <div className={styles.actions}>{actions}</div>}
    </div>
  )

  return (
    <dialog
      ref={ref}
      className={styles.dialog}
      data-size={size}
      style={frame.fixed && frame.scale !== 1 ? { scale: frame.scale } : undefined}
      aria-labelledby={title !== undefined ? titleId : undefined}
      onCancel={(e) => {
        e.preventDefault()
        if (dismissable) onClose()
      }}
      onClick={(e) => {
        // 바깥(배경) 누름 = 닫기
        if (dismissable && e.target === ref.current) onClose()
      }}
    >
      {tone === 'device' ? (
        <GlowFrame className={styles.glow} still={still}>
          {card}
        </GlowFrame>
      ) : (
        card
      )}
    </dialog>
  )
}
