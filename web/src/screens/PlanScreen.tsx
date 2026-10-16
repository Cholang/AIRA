import { BUTTON } from '../buttonText'
import { useCallback, useEffect, useRef, useState } from 'react'
import Button from '../components/Button'
import NightGraph from '../components/NightGraph'
import { useRailExtra } from '../components/StepRail'
import {
  changeSettings,
  clockOf,
  pickTarget,
  removeTarget,
  setStart,
  type Chart,
  type Edited,
  type Plan,
  type PlanState,
} from '../plan'
import { PRODUCT } from '../product'
import styles from './PlanScreen.module.css'
import { FramePreview } from './PlanTarget'
import { PlanChat } from './PlanChat'
import { SettingsRow } from './PlanSettings'

/**
 * 촬영 계획 (DESIGN.md "계획", 시안 mockups/aira-plan-v3.html — 2026-10-14 개편): 계획 = 무엇을(대상) + 얼마나(촬영 설정).
 * 왼쪽 30%: 아이라와의 대화(AI가 없으면 이름 검색만). 오른쪽 70%: 오늘 밤 한 줄 · 프레임 미리보기 · 일몰~일출 그래프 · 촬영 설정 줄과 이동 버튼.
 * 화면 조작과 AI가 같은 계획(서버)을 고친다. 숫자는 모두 서버 계산 값이다. 이동 버튼을 누르면 적도의가 움직인다(기다려야 하면 기다리기 화면).
 */
export default function PlanScreen({ onContinue, notice = null }: { onContinue: () => void; /** 계획 화면에 오며 생긴 알림(추적을 끄지 못함 등) — 오류 줄에 */ notice?: string | null }) {
  const [state, setState] = useState<PlanState | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoadError(null)
    fetch('/api/plan/state')
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
      .then((s: PlanState) => (s.error ? setLoadError(s.error) : setState(s)))
      .catch(() => setLoadError(`${PRODUCT.name} 내부 서버에서 오늘 밤 정보를 받지 못했습니다.`))
  }, [])

  useEffect(load, [load])

  if (loadError)
    return (
      <main className={styles.problem}>
        <p>{loadError}</p>
        <Button onClick={load}>{BUTTON.retry}</Button>
      </main>
    )
  if (!state) return <main className={styles.plan} aria-busy="true" />
  return <PlanBody initial={state} onContinue={onContinue} notice={notice} />
}

/** 대상 단계의 작업 (진행 표시). 계획 화면에서는 계획이 지금 작업이고 나머지는 대상 묶음(서버)이 이어서 한다 */
const TARGET_ITEMS = [
  ['plan', '계획'],
  ['slew', '이동'],
  ['center', '센터링'],
  ['focuscheck', '초점 확인'],
  ['guiding', '가이드'],
  ['test', '시험 사진'],
] as const

function PlanBody({ initial, onContinue, notice }: { initial: PlanState; onContinue: () => void; notice: string | null }) {
  useRailExtra({ stage: '대상', items: TARGET_ITEMS.map(([id, label], i) => ({ id, label, state: i === 0 ? 'now' : 'todo' })) })
  const [plan, setPlan] = useState<Plan>(initial.plan)
  const [chart, setChart] = useState<Chart>(initial.chart)
  // AI를 쓸 수 없는 이유 — 대화 도중 한도가 다 되면 서버가 알려 이름 검색으로 바뀐다 (1분마다 받는 상태로도)
  const [practiceReason, setPracticeReason] = useState<string | null>(initial.practiceReason)
  const [error, setError] = useState<string | null>(notice)
  useEffect(() => {
    if (notice) setError(notice)
  }, [notice])

  const refreshChart = useCallback(() => {
    fetch('/api/plan/chart')
      .then((r) => (r.ok ? r.json() : null))
      .then((c: Chart | null) => c && setChart(c))
      .catch(() => {})
  }, [])

  // 계획이 바뀐 횟수 (화면 고치기·AI 답) — 그 전에 보낸 1분 상태가 늦게 와서 새 값을 덮지 않게
  const changes = useRef(0)
  // 서버가 거절한 횟수 — 숫자 칸이 거절된 값을 그대로 보이지 않고 계획 값으로 돌아가게
  const [rejects, setRejects] = useState(0)

  // 시각이 흐르면(지금 선·가장 이른 시작) 1분마다 다시 받는다
  useEffect(() => {
    const t = window.setInterval(() => {
      const at = changes.current
      fetch('/api/plan/state')
        .then((r) => (r.ok ? r.json() : null))
        .then((s: PlanState | null) => {
          if (!s || s.error || changes.current !== at) return
          setPlan(s.plan)
          setChart(s.chart)
          setPracticeReason(s.practiceReason ?? null)
        })
        .catch(() => {})
    }, 60_000)
    return () => window.clearInterval(t)
  }, [])

  // 화면에서 고친 것은 차례로 보내고, 이동(확정)은 마지막 고침이 끝난 뒤에 (CX-36-05 — 노출을 입력하고 바로 이동을 누르면 이전 값으로 확정될 수 있었다)
  const edits = useRef<Promise<boolean>>(Promise.resolve(true))
  const pendingEdits = useRef(0)

  // 화면과 서버의 계획이 어긋났을 수 있음 (고침이 거절된 뒤 다시 받기를 못 함) — 확정 전에 다시 받는다
  const stale = useRef(false)
  const resync = useCallback(async () => {
    const at = changes.current
    const s = await fetch('/api/plan/state')
      .then((r) => (r.ok ? (r.json() as Promise<PlanState>) : null))
      .catch(() => null)
    stale.current = !s || !!s.error
    if (s && !s.error && changes.current === at) {
      setPlan(s.plan)
      setChart(s.chart)
    }
    return !stale.current
  }, [])

  /** 화면에서 계획 고치기: 서버가 고친 뒤의 계획·그래프로 바꾼다. 안 되면 이유를 오류 줄에. 그 뒤 다른 고침·AI 답이 있었으면 늦은 응답은 버린다 */
  const edit = useCallback((change: () => Promise<Edited>) => {
    setError(null)
    const mine = ++changes.current
    pendingEdits.current++
    const next = edits.current.then(async () => {
      try {
        const r = await change()
        if (changes.current === mine) {
          setPlan(r.plan)
          setChart(r.chart)
        }
        return true
      } catch (e) {
        setError(e instanceof Error ? e.message : '계획을 바꾸지 못했습니다.')
        setRejects((n) => n + 1)
        // 앞서 성공한 고침의 응답을 버렸을 수 있다 — 서버의 지금 계획으로 화면을 다시 맞춘다 (CX-38-02)
        await resync()
        return false
      } finally {
        pendingEdits.current--
      }
    })
    edits.current = next
    return next
  }, [resync])
  const planFromChat = useCallback((p: Plan) => {
    changes.current++
    setPlan(p)
  }, [])

  // 이동: 서버가 계획을 실행 값으로 확정하고 대상 묶음(이동부터)으로 넘어간다. 실행할 수 없는 계획이면 이유를 오류 줄에
  const [starting, setStarting] = useState(false)
  const start = async () => {
    if (starting) return
    setStarting(true)
    // 칸을 떠나며 보낸 고침(같은 누름에서 시작됨)과 기다리는 사이 더한 고침이 모두 끝나기를 기다린다 (CX-38-03 — 누른 순간의 것만 기다렸다).
    // 그중 하나라도 거절됐거나 화면을 서버와 다시 맞추지 못했으면 확정하지 않는다(오류 줄을 보게)
    let failed = false
    for (;;) {
      const p = edits.current
      const waited = pendingEdits.current > 0
      if (!(await p) && waited) failed = true
      if (p === edits.current) break
    }
    if (failed || (stale.current && !(await resync()))) {
      if (stale.current) setError('서버의 계획을 다시 받지 못했습니다. 잠시 뒤 다시 눌러 주세요.')
      setStarting(false)
      return
    }
    setError(null)
    try {
      const res = await fetch('/api/plan/confirm', { method: 'POST' })
      if (res.ok) return onContinue()
      const body = (await res.json().catch(() => null)) as { error?: string } | null
      setError(body?.error ?? '계획을 확정하지 못했습니다. 다시 눌러 주세요.')
    } catch {
      setError(`${PRODUCT.name} 내부 서버와 연결이 끊겼습니다. 다시 눌러 주세요.`)
    }
    setStarting(false)
  }

  const current = plan.picks.find((p) => p.id === plan.current) ?? null
  const chartCurrent = chart.picks.find((p) => p.id === chart.current) ?? null
  const waits = !!chartCurrent?.window && chartCurrent.window.moveAt > chart.now + 1

  return (
    <main className={styles.plan}>
      <PlanChat
        initialMessages={initial.messages}
        practiceReason={practiceReason}
        onPractice={setPracticeReason}
        error={error}
        onError={setError}
        onPlan={planFromChat}
        onTurnEnd={refreshChart}
        onPick={(id) => void edit(() => pickTarget(id))}
      />

      <div className={styles.right}>
        <FramePreview
          plan={plan}
          current={current}
          minAltitude={chart.minAltitude}
          onRemove={(id) => void edit(() => removeTarget(id))}
          onResetStart={(id) => void edit(() => setStart(id, null))}
        />

        <section className={styles.graphWrap} data-empty={plan.picks.length === 0}>
          <div className={styles.graphBox}>
            <NightGraph
              chart={chart}
              onSelect={(id) => void edit(() => pickTarget(id))}
              onStartChange={(id, minute) => void edit(() => setStart(id, minute))}
            />
          </div>
          <p className={styles.graphEmpty}>오늘은 무엇을 찍을까요?</p>
        </section>

        <SettingsRow
          plan={plan}
          current={current}
          waits={waits}
          moveAt={chartCurrent?.window ? clockOf(chart, chartCurrent.window.moveAt) : null}
          starting={starting}
          rejects={rejects}
          onChange={(change) => void edit(() => changeSettings(change))}
          onGo={() => void start()}
        />
      </div>
    </main>
  )
}

// ── 왼쪽: 대화 (AI) 또는 이름 검색 ─────────
