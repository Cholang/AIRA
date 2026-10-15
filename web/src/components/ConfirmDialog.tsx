import { useRef, type ReactNode } from 'react'
import Button from './Button'
import Modal from './Modal'
import styles from './ConfirmDialog.module.css'

/**
 * 확인 창 (공통). 되돌릴 수 없는 일을 하기 전에 한 번 묻는다 (예: 진행 중에 아이라 종료). 껍데기는 공통 Modal.
 * Esc·바깥 누름은 취소, 키보드 초점은 창 안에 머문다. 처음 초점은 안전한 쪽(취소)에.
 * 창 위에 창을 겹치지 않는다 (DESIGN.md "하지 말 것").
 * busy = 확인한 일을 하는 중 (버튼을 잠그고 닫히지 않음 — 예: 장비 연결을 끊고 종료).
 * children = 문장 아래 덧붙이는 내용 (예: 종료 중 닫는 프로그램 목록).
 * single = 버튼 하나(알림), dismissable=false면 Esc·바깥 누름으로 닫히지 않는다(고르지 않고 넘어가면 안 되는 질문 — 예: 이어서 할까요).
 * tone="device" = 적도의가 움직이거나 아이라가 꺼지는 확인(빛 테두리), size = 문장이 길면 md. message 속 \n은 줄바꿈
 */
export default function ConfirmDialog({
  open,
  message,
  confirmLabel,
  cancelLabel = '취소',
  onConfirm,
  onCancel,
  single = false,
  dismissable = true,
  busy = false,
  tone,
  size = 'sm',
  children,
}: {
  open: boolean
  message: string
  confirmLabel: string
  cancelLabel?: string
  onConfirm: () => void
  onCancel: () => void
  single?: boolean
  dismissable?: boolean
  busy?: boolean
  tone?: 'device'
  size?: 'sm' | 'md'
  children?: ReactNode
}) {
  const cancelRef = useRef<HTMLButtonElement>(null)
  return (
    <Modal
      open={open}
      onClose={onCancel}
      size={size}
      tone={tone}
      dismissable={dismissable && !busy}
      initialFocus={single ? undefined : cancelRef}
      actions={
        <>
          <Button variant="primary" onClick={onConfirm} disabled={busy}>
            {confirmLabel}
          </Button>
          {!single && (
            <Button ref={cancelRef} onClick={onCancel} disabled={busy}>
              {cancelLabel}
            </Button>
          )}
        </>
      }
    >
      <p className={styles.message} aria-live="polite">
        {message}
      </p>
      {children}
    </Modal>
  )
}
