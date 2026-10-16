import { useId, type ReactNode } from 'react'
import styles from './Tooltip.module.css'

/**
 * 가리키거나 키보드 초점이 들어오면 뜨는 말풍선 (DESIGN.md "공통 부품" — 툴팁).
 * 버튼이 누르면 무엇이 일어나는지, 숫자가 왜 그런지처럼 "필요할 때만 보는 한두 문장"에. 물음표 아이콘이 필요하면 InfoTip.
 * placement: 말풍선이 놓이는 쪽(top · bottom · left · right), align: 위·아래 변에서의 맞춤(start · center · end).
 * text가 비어 있으면 아무것도 띄우지 않는다. 자리는 늘 같아 나타날 때 주변이 움직이지 않는다(투명도만).
 * open: 가리킴과 상관없이 띄운다 — SVG 그림 속 한 점처럼 감쌀 수 없는 것은 그 자리에 빈 기준점을 두고 쓰는 쪽이 켠다(밤 그래프의 달).
 */
export default function Tooltip({
  text,
  placement = 'top',
  align = 'center',
  open,
  className,
  children,
}: {
  text: ReactNode
  placement?: 'top' | 'bottom' | 'left' | 'right'
  align?: 'start' | 'center' | 'end'
  open?: boolean
  className?: string
  children?: ReactNode
}) {
  const id = useId()
  const has = text !== null && text !== undefined && text !== ''
  return (
    <span className={[styles.anchor, className].filter(Boolean).join(' ')} aria-describedby={has ? id : undefined}>
      {children}
      {has && (
        <span role="tooltip" id={id} className={styles.tip} data-placement={placement} data-align={align} data-open={open || undefined}>
          {text}
        </span>
      )}
    </span>
  )
}
