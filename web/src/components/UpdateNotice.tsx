import { ExternalLink } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import Modal from './Modal'
import styles from './UpdateNotice.module.css'
import { TF } from '../uiText'

/** 서버 Setup/UpdateChecker.cs의 UpdateInfo와 같은 모양 */
interface UpdateInfo {
  id: string
  name: string
  installed: string
  latest: string
  url: string
  how: string
}

/**
 * 새 버전 알림 (DESIGN.md "프로필" — 새 버전). 업데이트를 권하지 않고 알리기만 한다.
 * 왼쪽 아래 "새 버전 n개" → 누르면 공통 팝업(Modal, 큰 창)에 프로그램마다 지금 버전·새 버전·내려받기·다시 알리지 않기 (2026-10-14 서랍 → 팝업).
 * 자리는 처음부터 확보(절대 위치)라 늦게 나타나도 프로필 목록이 움직이지 않는다. 확인에 실패하면 아무것도 보이지 않는다.
 */
export default function UpdateNotice() {
  const [items, setItems] = useState<UpdateInfo[]>([])
  const [open, setOpen] = useState(false)
  const button = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    let alive = true
    fetch('/api/updates')
      .then((r) => (r.ok ? (r.json() as Promise<UpdateInfo[]>) : []))
      .then((list) => alive && setItems(list))
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])

  const close = () => {
    setOpen(false)
    button.current?.focus({ preventScroll: true })
  }

  const dismiss = (u: UpdateInfo) => {
    void fetch('/api/updates/dismiss', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ id: u.id, version: u.latest }),
    })
    const rest = items.filter((i) => i.id !== u.id)
    setItems(rest)
    if (rest.length === 0) setOpen(false)
  }

  if (items.length === 0) return null

  return (
    <>
      <button
        ref={button}
        type="button"
        className={styles.badge}
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
      >
        새 버전 {items.length}개
      </button>

      <Modal open={open} onClose={close} size="lg" title={TF('title.updates', { count: items.length })}>
        <p className={styles.lead}>업데이트는 촬영이 없는 날 하세요. 지금 버전으로도 그대로 쓸 수 있습니다.</p>
        <ul className={styles.list}>
          {items.map((u) => (
            <li key={u.id} className={styles.row}>
              <div className={styles.what}>
                <b>{u.name}</b>
                <span>
                  {u.latest} <small>(지금 {u.installed})</small>
                </span>
              </div>
              <span className={styles.how}>{u.how}</span>
              <a className={styles.link} href={u.url} target="_blank" rel="noreferrer">
                내려받기
                <ExternalLink strokeWidth={2} aria-hidden="true" />
              </a>
              <button type="button" className={styles.dismiss} onClick={() => dismiss(u)}>
                이 버전은 다시 알리지 않기
              </button>
            </li>
          ))}
        </ul>
      </Modal>
    </>
  )
}
