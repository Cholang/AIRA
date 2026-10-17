import { AlertTriangle, House, Lock } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import Button from './Button'
import ConfirmDialog from './ConfirmDialog'
import { T } from '../uiText'

interface HomeState {
  available: boolean
  homing: boolean
  reason: string | null
  /** 마지막 홈이 실패했으면 그 안내 */
  problem: string | null
  /** 멈춘 것도 확인하지 못함 — 사용자가 직접 확인했다고 할 때까지 홈을 막는다 (CX-20-03) */
  needsCheck: boolean
}

/**
 * 적도의 홈 (2026-10-09 사용자 요청, 2026-10-10 레일에서 분리 — 레일은 자리만 주고 이 기능을 모른다).
 * 서버가 지금 움직여도 되는지(촬영·적도의를 쓰는 작업·실제 이동·가이딩·미연결이면 안 됨)와 이유를 알려 준다 (2초마다).
 * 움직여도 되면 집 아이콘, 안 되면 자물쇠 아이콘 + 흐리게 — 아이콘만 봐도 구분되게. 누르면 한 번 묻고 홈으로 보낸 뒤 추적을 끈다.
 * 홈이 실패하면 그 안내를 한 번 띄우고, 멈춘 것도 확인하지 못했으면 "적도의 확인 필요"로 남아 직접 확인받는다
 */
export default function MountHomeButton() {
  const [state, setState] = useState<HomeState | null>(null)
  const [asking, setAsking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const shownProblem = useRef<string | null>(null)
  useEffect(() => {
    let alive = true
    const poll = () =>
      fetch('/api/mount/home')
        .then((r) => (r.ok ? (r.json() as Promise<HomeState>) : null))
        .then((s) => {
          if (!alive) return
          setState(s)
          // 홈이 실패했다는 안내는 한 번만 (같은 안내를 2초마다 다시 띄우지 않게)
          if (s?.problem && s.problem !== shownProblem.current) {
            shownProblem.current = s.problem
            setError(s.problem)
          }
        })
        .catch(() => alive && setState(null))
    void poll()
    const t = window.setInterval(poll, 2000)
    return () => {
      alive = false
      window.clearInterval(t)
    }
  }, [])
  if (!state) return null
  const check = state.needsCheck
  const on = state.available || check
  const label = state.homing ? T('button.mountHome.homing') : check ? T('button.mountHome.check') : T('button.mountHome')
  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        aria-disabled={!on}
        title={state.available ? T('button.mountHome.tip') : (state.reason ?? undefined)}
        aria-label={state.available ? '적도의 홈으로 보내기' : `${label} — ${state.reason ?? '지금은 쓸 수 없음'}`}
        onClick={() => on && setAsking(true)}
      >
        {check ? <AlertTriangle strokeWidth={2} aria-hidden="true" /> : state.available || state.homing ? <House strokeWidth={2} aria-hidden="true" /> : <Lock strokeWidth={2} aria-hidden="true" />}
        <span>{label}</span>
      </Button>
      <ConfirmDialog
        open={asking || error !== null}
        tone="device"
        message={
          error ??
          (check
            ? (state.problem ?? '적도의가 멈췄는지 확인하지 못했습니다.') + ' 적도의가 멈춰 있는지 확인해 주세요.'
            : '적도의를 홈 위치로 보내고 추적을 끕니다.\n망원경 주변에 걸리는 것이 없는지 확인해 주세요.')
        }
        confirmLabel={error ? T('button.ok') : check ? T('button.confirmStopped') : T('button.sendHome')}
        single={error !== null}
        onConfirm={() => {
          if (error) return setError(null)
          setAsking(false)
          if (check) {
            void fetch('/api/mount/home/confirm', { method: 'POST' })
            return
          }
          void fetch('/api/mount/home', { method: 'POST' }).then(async (r) => {
            if (!r.ok) setError(((await r.json().catch(() => null)) as { error?: string } | null)?.error ?? '적도의를 홈으로 보내지 못했습니다')
          })
        }}
        onCancel={() => {
          setAsking(false)
          setError(null)
        }}
      />
    </>
  )
}
