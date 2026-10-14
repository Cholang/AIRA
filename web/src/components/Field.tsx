import { ChevronUp, Minus, Plus, RotateCcw } from 'lucide-react'
import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import styles from './Field.module.css'

/**
 * 설정 칸 부품 (DESIGN.md "공통 부품" — 설정 칸, 시안 mockups/aira-plan-v3.html의 촬영 설정 줄).
 * 칸 = 이름 + 높이 42px 상자. 상자 안 왼쪽에 되돌리기 화살표 자리(고친 칸만 보임), 오른쪽에 도드라진 둥근 사각 버튼.
 * - NumberField: 숫자 직접 입력(1 단위) + − / + (step 단위)
 * - SelectField: 값 + 위 쉐브론 → 목록이 칸 위로 펼침(화면 아래쪽 메뉴라서. 기본 select 목록은 방향을 못 정하고 촬영 테마 색도 못 바꾼다)
 * 값이 없으면 "-"로 보이고 꺼진다(disabled).
 */

export function Field({
  label,
  tone,
  off,
  className,
  children,
}: {
  label: ReactNode
  /** result: 다른 칸에서 나온 값(촬영 횟수) — 배경·외곽선이 조금 밝다 */
  tone?: 'result'
  /** 꺼진 칸 (대상을 고르기 전 등): 흐리게 */
  off?: boolean
  className?: string
  children: ReactNode
}) {
  return (
    <div className={[styles.field, className].filter(Boolean).join(' ')} data-tone={tone} data-off={off}>
      <span className={styles.label}>{label}</span>
      <div className={styles.box}>{children}</div>
    </div>
  )
}

/** 되돌리기 화살표: 자리는 늘 두고, 기본값과 다를 때만 보인다 */
export function ResetMark({ shown, onReset, label = '기본값으로' }: { shown: boolean; onReset: () => void; label?: string }) {
  return (
    <button type="button" className={styles.reset} data-shown={shown} onClick={onReset} aria-label={label} title={label} tabIndex={shown ? 0 : -1}>
      <RotateCcw aria-hidden="true" />
    </button>
  )
}

/** 상자 안 오른쪽의 도드라진 둥근 사각 버튼 (− + · 위 쉐브론) */
export function StepButton({
  label,
  onClick,
  disabled,
  expanded,
  children,
}: {
  label: string
  onClick: () => void
  disabled?: boolean
  expanded?: boolean
  children: ReactNode
}) {
  return (
    <button
      type="button"
      className={styles.step}
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      aria-expanded={expanded}
      aria-haspopup={expanded === undefined ? undefined : 'listbox'}
    >
      {children}
    </button>
  )
}

/** 숫자 칸: 직접 입력은 Enter·칸을 떠날 때 반영, − + 는 step씩. 범위를 넘으면 범위 끝으로 */
export function NumberField({
  value,
  onCommit,
  min,
  max,
  step,
  unit,
  disabled,
  name,
  reset,
}: {
  value: number | null
  onCommit: (v: number) => void
  min: number
  max: number
  step: number
  unit: string
  disabled?: boolean
  /** 화면 읽기용 이름 (예: "노출") */
  name: string
  reset?: ReactNode
}) {
  const [draft, setDraft] = useState(value === null ? '' : String(value))
  useEffect(() => setDraft(value === null ? '' : String(value)), [value])
  const clamp = (v: number) => Math.min(max, Math.max(min, Math.round(v)))
  const commit = () => {
    const v = Number(draft)
    if (draft.trim() === '' || !Number.isFinite(v)) return setDraft(value === null ? '' : String(value))
    const c = clamp(v)
    setDraft(String(c))
    if (c !== value) onCommit(c)
  }
  const bump = (d: number) => value !== null && onCommit(clamp(value + d))
  return (
    <>
      {reset}
      <input
        className={styles.input}
        inputMode="numeric"
        value={draft}
        placeholder="-"
        disabled={disabled}
        aria-label={name}
        onChange={(e) => setDraft(e.target.value.replace(/[^\d]/g, ''))}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit()
          else if ((e.key === 'ArrowUp' || e.key === 'ArrowDown') && value !== null) {
            e.preventDefault()
            onCommit(clamp(value + (e.key === 'ArrowUp' ? 1 : -1)))
          }
        }}
      />
      <span className={styles.unit}>{unit}</span>
      <span className={styles.steps}>
        <StepButton label={`${name} ${step}${unit} 줄이기`} onClick={() => bump(-step)} disabled={disabled || value === null || value <= min}>
          <Minus aria-hidden="true" />
        </StepButton>
        <StepButton label={`${name} ${step}${unit} 늘리기`} onClick={() => bump(step)} disabled={disabled || value === null || value >= max}>
          <Plus aria-hidden="true" />
        </StepButton>
      </span>
    </>
  )
}

/** 고르는 칸: 값을 누르거나 위 쉐브론 → 칸 위로 목록. 바깥을 누르거나 Esc면 닫힘 */
export function SelectField<T extends string | number>({
  value,
  options,
  onSelect,
  disabled,
  name,
  prefix,
  reset,
}: {
  value: T | null
  options: { value: T; label: string }[]
  onSelect: (v: T) => void
  disabled?: boolean
  name: string
  /** 값 앞의 작은 단위 (예: ISO) */
  prefix?: string
  reset?: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLSpanElement>(null)
  const list = useRef<HTMLDivElement>(null)
  const id = useId()
  const current = options.find((o) => o.value === value)

  useEffect(() => {
    if (!open) return
    const away = (e: PointerEvent) => !root.current?.contains(e.target as Node) && setOpen(false)
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    window.addEventListener('pointerdown', away)
    window.addEventListener('keydown', esc)
    // 지금 값이 목록 가운데 오게
    const on = list.current?.querySelector<HTMLElement>('[aria-selected="true"]')
    if (on && list.current) list.current.scrollTop = on.offsetTop - list.current.clientHeight / 2 + on.offsetHeight / 2
    on?.focus()
    return () => {
      window.removeEventListener('pointerdown', away)
      window.removeEventListener('keydown', esc)
    }
  }, [open])
  useEffect(() => {
    if (disabled) setOpen(false)
  }, [disabled])

  const toggle = () => !disabled && setOpen((o) => !o)
  return (
    <span className={styles.select} ref={root}>
      {reset}
      {prefix && <span className={styles.unit}>{prefix}</span>}
      <button type="button" className={styles.value} onClick={toggle} disabled={disabled} aria-label={`${name}: ${current?.label ?? '없음'}`} aria-controls={id}>
        {current?.label ?? '-'}
      </button>
      <span className={styles.steps}>
        <StepButton label={`${name} 목록 열기`} onClick={toggle} disabled={disabled} expanded={open}>
          <ChevronUp aria-hidden="true" />
        </StepButton>
      </span>
      <div className={styles.menu} data-open={open} role="listbox" id={id} ref={list} aria-label={name}>
        {open &&
          options.map((o) => (
            <button
              key={String(o.value)}
              type="button"
              role="option"
              aria-selected={o.value === value}
              className={styles.option}
              onClick={() => {
                setOpen(false)
                if (o.value !== value) onSelect(o.value)
              }}
              onKeyDown={(e) => {
                const items = [...(list.current?.querySelectorAll<HTMLElement>('[role="option"]') ?? [])]
                const i = items.indexOf(e.currentTarget)
                if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return
                e.preventDefault()
                items[e.key === 'ArrowUp' ? Math.max(0, i - 1) : Math.min(items.length - 1, i + 1)]?.focus()
              }}
            >
              {o.label}
            </button>
          ))}
      </div>
    </span>
  )
}
