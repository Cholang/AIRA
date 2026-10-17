import { Check, LogOut, X } from 'lucide-react'
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { closeApp, inDesktop, onCloseRequest } from '../host'
import { PRODUCT } from '../product'
import Button from './Button'
import ConfirmDialog from './ConfirmDialog'
import styles from './QuitControl.module.css'
import type { Stage } from './StepRail'
import { stageLabel } from '../appFlow'
import { T } from '../uiText'

/**
 * 아이라 종료 (2026-10-10 레일에서 분리 — 종료는 앱 전체의 일이라 레일을 숨기거나 빼도 그대로 동작해야 한다).
 * 확인 창 → 진행 중인 준비 작업을 멈추고 확인 → (골랐으면) 적도의를 홈으로 → 적도의 추적 끄기 → 포커서를 0으로 → 장비 연결 중이면 멈추고 끊기 →
 * 고른 프로그램(N.I.N.A.·PHD2·SharpCap·Empire)을 하나씩 닫으며 체크 → 1초 뒤 창 닫기.
 * 확인 창에는 누가 켰든 지금 떠 있는 프로그램을 모두 보여 주고 처음엔 모두 체크 — 사용자가 남길 것만 끈다 (2026-10-16 사용자).
 * 적도의가 연결돼 있고 홈이 아니면 맨 위에 "적도의를 홈으로 보내기"(처음엔 체크) — 다음 날 홈 자세에서 시작하게, 낮 시험 등에는 끌 수 있게 (2026-10-17 사용자).
 * N.I.N.A.를 남기고 다른 것을 닫으면 서버가 N.I.N.A.의 장비 연결을 먼저 끊는다(연결된 채 PHD2 등이 사라지면 N.I.N.A.가 오류 창을 띄움).
 * 어느 단계든 확인하지 못하면 거기서 멈추고 안내 + "그래도 종료" — 실패를 완료로 바꾸지 않는다 (CX-20-02).
 * 종료를 누른 뒤에도 목록(홈·프로그램 줄)은 그대로 두고 표시만 바꾼다 — 전에는 단계마다 목록이 사라졌다 나타나 창 높이가 바뀌며
 * 창이 빠르게 깜빡였다 (2026-10-18 실기: N.I.N.A. 체크를 끄고 종료).
 * 레일의 "아이라 종료" 버튼과 창의 × 버튼이 같은 확인을 쓴다(`useQuit`)
 */
const QuitContext = createContext<() => void>(() => closeApp())

/** 종료 확인을 띄운다 (QuitProvider 밖에서는 바로 닫음) */
export const useQuit = () => useContext(QuitContext)

/** 아이라 종료 버튼: 전체화면이라 창 닫기 버튼이 없어서 둔다 (데스크톱 창에서만, 2026-10-07). 자리는 App이 정한다(레일 아래) */
export function QuitButton() {
  const askQuit = useQuit()
  if (!inDesktop()) return null
  return (
    <Button variant="ghost" size="sm" onClick={askQuit}>
      <LogOut strokeWidth={2} aria-hidden="true" />
      <span>{T('button.quit')}</span>
    </Button>
  )
}

/** 종료 중 목록의 한 줄: 적도의 홈(id HOME) 또는 닫는 프로그램 하나 (서버 ProgramCloser.Target) + 진행 상태 */
interface ClosingApp {
  id: string
  name: string
  /** kept = 사용자가 체크를 끈 것, 또는 앞 프로그램이 닫히지 않아 닫지 않고 남겨 둠 */
  state: 'waiting' | 'closing' | 'done' | 'failed' | 'kept'
}

const HOME = 'mount-home'

type Step = 'tasks' | 'home' | 'tracking' | 'focuser' | 'equipment' | 'programs' | 'force'

const STEP_TEXT: Record<Step, string> = {
  tasks: '진행 중인 작업을 멈추는 중입니다… (촬영 중이면 지금 사진이 끝나기를 기다립니다)',
  home: '적도의를 홈으로 보내는 중입니다…',
  tracking: '적도의 추적을 끄는 중입니다…',
  focuser: '포커서를 0으로 되돌리는 중입니다…',
  equipment: '장비 연결을 멈추고 연결을 끊는 중입니다…',
  programs: '촬영을 위한 프로그램을 종료 중입니다.',
  force: '가이드를 멈추고 적도의 추적을 끄는 중입니다…',
}

const NOTE: Record<ClosingApp['state'], string> = { waiting: '', closing: '닫는 중', done: '닫음', failed: '닫지 못함', kept: '남겨 둠' }
const HOME_NOTE: Record<ClosingApp['state'], string> = { waiting: '', closing: '가는 중', done: '도착', failed: '확인 못 함', kept: '그대로 둠' }

/** POST 후 본문(JSON). 응답이 없거나 실패면 null — 호출하는 쪽이 "확인하지 못함"으로 다룬다 */
const post = <T,>(url: string) =>
  fetch(url, { method: 'POST' })
    .then((r) => (r.ok ? (r.json() as Promise<T>) : null))
    .catch(() => null)

export function QuitProvider({
  stage,
  confirmOnClose,
  children,
}: {
  /** 지금 단계 (확인 문구 "○○ 진행을 종료하고…"). 단계 전 화면이면 null */
  stage: Stage | null
  /** 창 ×도 확인을 거칠지 (진행 중일 때 — 단계 전 화면·마지막 요약은 바로 닫음) */
  confirmOnClose: boolean
  children: ReactNode
}) {
  const [quitting, setQuitting] = useState(false)
  // 종료 확인 뒤 단계(step). 확인하지 못한 것이 있으면 busy=false + problem — 프로그램은 더 닫지 않고 "그래도 종료"만 남긴다
  // forced = "그래도 종료"에서 추적을 끄지 못했다고 이미 알림 — 한 번 더 누르면 그대로 끈다
  const [closing, setClosing] = useState<{ busy: boolean; step?: Step; problem?: string; forced?: boolean } | null>(null)
  const [apps, setApps] = useState<ClosingApp[]>([])
  // 확인 창에서 고르는 프로그램 (지금 떠 있는 것, 처음엔 모두 체크). null = 아직 목록을 받는 중
  const [choices, setChoices] = useState<{ id: string; name: string; checked: boolean }[] | null>(null)
  // 적도의를 홈으로 보낼지 (서버가 보일 때만 — 연결돼 있고 홈이 아님). null = 보이지 않음
  const [sendHome, setSendHome] = useState<boolean | null>(null)
  // 목록을 받지 못함 — "아무것도 안 고름"과 다르다 (CX-40-03)
  const [listFailed, setListFailed] = useState(false)

  /** 확인 창을 열며 지금 떠 있는 프로그램을 받는다 (받지 못하면 목록 없이 — 종료 4단계가 다시 묻는다) */
  const openQuit = () => {
    setQuitting(true)
    setChoices(null)
    setSendHome(null)
    setListFailed(false)
    fetch('/api/engine/programs')
      .then((r) => (r.ok ? (r.json() as Promise<{ apps: { id: string; name: string }[]; home?: boolean }>) : null))
      .catch(() => null)
      .then((b) => {
        if (!b) setListFailed(true)
        setChoices((cur) => cur ?? (b?.apps ?? []).map((a) => ({ ...a, checked: true })))
        setSendHome((cur) => cur ?? (b?.home ? true : null))
      })
  }

  const quit = async () => {
    // "그래도 종료": 확인하지 못한 것이 있어도 끈다(남은 프로그램은 그대로 — 사용자가 직접 확인). 다만 끄기 전에 가이딩 멈춤·추적 끄기를 한 번 시도하고,
    // 추적을 끄지 못했으면 그렇다고 분명히 알린 뒤 한 번 더 누르면 끈다 (2026-10-14 자체 리뷰: 아무것도 시도하지 않고 꺼져 적도의가 밤새 돌 수 있었다)
    if (closing && !closing.busy) {
      if (closing.forced) return closeApp()
      setClosing({ busy: true, step: 'force' })
      const safe = await post<{ problem: string | null }>('/api/quit/force-safe')
      if (safe === null || safe.problem)
        return setClosing({
          busy: false,
          forced: true,
          problem: (safe?.problem ?? '적도의 추적을 껐는지 확인하지 못했습니다. 이대로 두면 적도의가 밤새 돌아갈 수 있으니 N.I.N.A.나 적도의에서 추적을 직접 꺼 주세요.') + ' 그래도 종료하면 이대로 끕니다.',
        })
      return closeApp()
    }
    // 1. 진행 중인 준비 작업(이동·자동초점·가이딩)을 멈추고 멈춘 것을 확인 — 확인 없이 N.I.N.A.를 닫으면 장비를 놓친다
    // 홈으로 보내기를 골랐으면 진행 중인 홈 이동(장비 준비 끝의 자동 홈 등)은 멈추지 않고 다음 단계에서 끝나기를 기다린다
    const home = sendHome === true
    // 확인 창의 줄을 그대로 종료 중 목록으로 (체크를 끈 것은 처음부터 "남겨 둠"·"그대로 둠")
    let now: ClosingApp[] = [
      ...(sendHome === null ? [] : [{ id: HOME, name: T('button.mountHome'), state: home ? 'waiting' : 'kept' } as ClosingApp]),
      ...(choices ?? []).map((c): ClosingApp => ({ id: c.id, name: c.name, state: c.checked ? 'waiting' : 'kept' })),
    ]
    const mark = (id: string, state: ClosingApp['state']) => {
      now = now.map((x) => (x.id === id ? { ...x, state } : x))
      setApps(now)
    }
    setApps(now)
    setClosing({ busy: true, step: 'tasks' })
    const tasks = await post<{ stopped: boolean; problem: string | null }>('/api/prepare/stop-for-quit' + (home ? '?keepHome=true' : ''))
    if (!tasks?.stopped)
      return setClosing({
        busy: false,
        problem: tasks?.problem ?? '진행 중인 작업을 멈췄는지 확인하지 못했습니다. 적도의·포커서·가이드가 멈췄는지 직접 확인해 주세요.',
      })
    // 1-1. 적도의를 홈으로 (골랐을 때): 홈 도착을 확인하고 추적을 끈다. 확인하지 못하면 멈추고 안내 + "그래도 종료"
    if (home) {
      setClosing({ busy: true, step: 'home' })
      mark(HOME, 'closing')
      const homed = await post<{ problem: string | null }>('/api/mount/home-for-quit')
      mark(HOME, homed === null || homed.problem ? 'failed' : 'done')
      if (homed === null || homed.problem)
        return setClosing({ busy: false, problem: homed?.problem ?? '적도의가 홈에 도착했는지 확인하지 못했습니다. 적도의가 멈춰 있는지 직접 확인해 주세요.' })
    }
    // 1-2. 적도의 추적 끄기 — 켜 둔 채 끄면 밤새 돌아 케이블이 감기거나 경통이 삼각대에 닿는다 (2026-10-11 실기: 극축 정렬 중 끄자 몇 시간 뒤 RA 90°). 촬영 중이면 그대로
    setClosing({ busy: true, step: 'tracking' })
    const tracking = await post<{ ok: boolean }>('/api/mount/tracking-off-for-quit')
    if (!tracking?.ok)
      return setClosing({ busy: false, problem: '적도의 추적을 끄지 못했습니다. 이대로 두면 적도의가 계속 돌아갑니다. N.I.N.A.나 적도의에서 추적을 직접 꺼 주세요.' })
    // 2. 포커서를 0(노브 끝까지 넣은 위치)에 두고 끝낸다 — 다음에 노브를 손으로 맞추지 않게 (2026-10-09). 못 해도(촬영 중·미연결 등) 종료는 계속
    setClosing({ busy: true, step: 'focuser' })
    await post<{ problem: string | null }>('/api/focuser/park')
    // 3. 장비 연결 중이면 멈추고 끊기. 응답을 못 받으면 끊었는지 모르는 것 — 끊은 것으로 치지 않는다
    setClosing({ busy: true, step: 'equipment' })
    const abort = await post<{ notDisconnected: string[] }>('/api/equipment/abort')
    if (!abort) return setClosing({ busy: false, problem: '장비 연결을 끊었는지 확인하지 못했습니다. N.I.N.A.에서 장비 연결 상태를 직접 확인해 주세요.' })
    if (abort.notDisconnected.length > 0)
      return setClosing({ busy: false, problem: `${abort.notDisconnected.join(', ')} 연결을 끊었는지 확인하지 못했습니다. N.I.N.A.에서 직접 확인해 주세요.` })
    // 4. 고른 프로그램을 차례로 닫는다(체크를 끈 것은 "남겨 둠") — 남겨 두면 아이라 없이 N.I.N.A. 알림만 뜬다 (2026-10-09). 하나씩 체크하고, 다 닫히면 1초 뒤에 끈다 (2026-10-10).
    //    하나라도 닫히지 않으면(N.I.N.A.의 저장 확인 창 등) 뒤의 프로그램은 닫지 않고 남겨 둔다 — 서버도 N.I.N.A.가 남아 있으면 나머지를 닫지 않는다
    //    목록을 받지 못한 것은 "닫을 것 없음"이 아니다 — 그대로 끄지 않고 안내 (CX-21-07). 작업이 아직 돌면 서버가 held로 이유를 준다
    const body = await fetch('/api/engine/close')
      .then((r) => (r.ok ? (r.json() as Promise<{ apps: { id: string; name: string }[]; held: string | null }>) : null))
      .catch(() => null)
    if (!body)
      return setClosing({ busy: false, problem: '닫을 프로그램 목록을 받지 못했습니다. 취소한 뒤 다시 종료해 보거나, 그래도 종료하고 N.I.N.A.·PHD2를 직접 닫아 주세요.' })
    if (body.held) return setClosing({ busy: false, problem: body.held + ' 멈춘 것을 확인한 뒤 다시 종료해 주세요.' })
    const list = body.apps
    if (list.length === 0) return closeApp()
    // 처음 목록을 받지 못해 고르지 못했다 — 아무것도 고르지 않은 것으로 보고 끄지 않는다 (CX-40-03)
    if (listFailed)
      return setClosing({ busy: false, step: 'programs', problem: '닫을 프로그램 목록을 처음에 받지 못해 고르지 못했습니다. 취소한 뒤 다시 종료하거나, 그래도 종료하고 N.I.N.A.·PHD2를 직접 닫아 주세요.' })
    // 확인 창 목록에 없던 것(그 사이 켜짐)은 닫지 않는다 — 사용자가 고르지 않았다. 그런 줄은 목록 끝에 "남겨 둠"으로 더한다
    const picked = new Set((choices ?? []).filter((c) => c.checked).map((c) => c.id))
    if (!list.some((x) => picked.has(x.id))) return closeApp()
    now = [...now, ...list.filter((x) => !now.some((r) => r.id === x.id)).map((x): ClosingApp => ({ ...x, state: 'kept' }))]
    // 고른 프로그램이 그 사이 이미 꺼졌으면 닫힌 것
    now = now.map((r) => (r.id !== HOME && r.state === 'waiting' && !list.some((x) => x.id === r.id) ? { ...r, state: 'done' } : r))
    setApps(now)
    setClosing({ busy: true, step: 'programs' })
    let failed = false
    let why: string | null = null
    for (const app of list) {
      if (!picked.has(app.id)) continue
      if (failed) {
        mark(app.id, 'kept')
        continue
      }
      mark(app.id, 'closing')
      const r = await post<{ closed: boolean; problem?: string | null }>('/api/engine/close/' + app.id)
      const closed = r?.closed ?? false
      mark(app.id, closed ? 'done' : 'failed')
      if (!closed) {
        failed = true
        why = r?.problem ?? null // N.I.N.A.의 장비 연결을 끊지 못함 등 (CX-40-02)
      }
    }
    if (failed)
      return setClosing({ busy: false, step: 'programs', problem: why ?? '닫지 못한 프로그램이 있습니다(저장 확인 창 등). 직접 닫아 주세요. 뒤의 프로그램은 그대로 두었습니다.' })
    await new Promise((r) => window.setTimeout(r, 1000))
    closeApp()
  }

  // 창의 × 버튼도 같은 확인
  useEffect(() => {
    if (!confirmOnClose) return
    onCloseRequest(openQuit)
    return () => onCloseRequest(null)
  }, [confirmOnClose])

  const message =
    closing?.problem ??
    (closing?.busy && closing.step
      ? STEP_TEXT[closing.step]
      : stage === 'shoot'
        ? // 촬영 중 종료: 아이라가 한 장씩 시키는 촬영이라 꺼지면 이어 찍을 수 없다 — 지금 사진이 끝나면 멈추고 평소대로 정리 (CX-30-01).
          // 중단하지 않는 이유: X-T5는 일반 노출을 소프트웨어로 중단할 수 없다 (N.I.N.A. 후지 플러그인은 벌브만 멈춤 — 2026-10-11 실기, ShootSession.StopForQuitAsync)
          `촬영을 중단하고 ${PRODUCT.reul} 종료합니다.\n지금 촬영 중인 사진 노출이 끝나면 가이드와 적도의 추적을 종료합니다.`
        : stage
        ? `${stageLabel(stage)} 진행을 중단하고 ${PRODUCT.reul} 종료합니다.`
        : `${PRODUCT.reul} 종료합니다.`)

  return (
    <QuitContext.Provider value={openQuit}>
      {children}
      <ConfirmDialog
        open={quitting}
        tone="device"
        message={message}
        confirmLabel={closing && !closing.busy ? T('button.quitAnyway') : T('button.quitConfirm')}
        busy={closing?.busy}
        // 고를 목록을 받기 전에는 종료를 누를 수 없다 — 받기 전에 누르면 아무것도 고르지 않은 것이 됐다 (CX-40-03)
        confirmDisabled={!closing && choices === null}
        onConfirm={() => void quit()}
        onCancel={() => {
          setQuitting(false)
          setClosing(null)
        }}
      >
        {!closing && choices && (choices.length > 0 || sendHome !== null) && (
          <ul className={styles.apps} aria-label="종료하며 할 일">
            {sendHome !== null && (
              <li data-state={sendHome ? 'pick' : 'kept'}>
                <input type="checkbox" className={styles.pick} id="quit-home" checked={sendHome} onChange={(e) => setSendHome(e.target.checked)} />
                <label htmlFor="quit-home">적도의를 홈으로 보내기</label>
                <span className={styles.note}>{sendHome ? '보냄' : '그대로 둠'}</span>
              </li>
            )}
            {choices.map((c) => (
              <li key={c.id} data-state={c.checked ? 'pick' : 'kept'}>
                <input
                  type="checkbox"
                  className={styles.pick}
                  id={'quit-' + c.id}
                  checked={c.checked}
                  onChange={(e) => setChoices((cs) => cs && cs.map((x) => (x.id === c.id ? { ...x, checked: e.target.checked } : x)))}
                />
                <label htmlFor={'quit-' + c.id}>{c.name}</label>
                <span className={styles.note}>{c.checked ? '닫음' : '남겨 둠'}</span>
              </li>
            ))}
          </ul>
        )}
        {closing && apps.length > 0 && (
          <ul className={styles.apps}>
            {apps.map((a) => (
              <li key={a.id} data-state={a.state}>
                <span className={styles.mark} aria-hidden="true">
                  {a.state === 'done' ? <Check strokeWidth={3} /> : a.state === 'failed' ? <X strokeWidth={3} /> : null}
                </span>
                <span>{a.name}</span>
                <span className={styles.note}>{(a.id === HOME ? HOME_NOTE : NOTE)[a.state]}</span>
              </li>
            ))}
          </ul>
        )}
      </ConfirmDialog>
    </QuitContext.Provider>
  )
}
