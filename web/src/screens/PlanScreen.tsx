import { ArrowRight } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import Button from '../components/Button'
import { Field, NumberField, ResetMark, SelectField } from '../components/Field'
import NightGraph, { Emblem } from '../components/NightGraph'
import { useRailExtra } from '../components/StepRail'
import Tooltip from '../components/Tooltip'
import {
  changeSettings,
  clockOf,
  displayName,
  pickTarget,
  removeTarget,
  searchTargets,
  sendChat,
  setStart,
  type Chart,
  type Edited,
  type FoundTarget,
  type Message,
  type Plan,
  type PlanPick,
  type PlanState,
  type PlanWindow,
} from '../plan'
import { PRODUCT, withRo } from '../product'
import styles from './PlanScreen.module.css'

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
        <Button onClick={load}>다시 시도</Button>
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
  ['guiding', '가이딩'],
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

  /** 화면에서 계획 고치기: 서버가 고친 뒤의 계획·그래프로 바꾼다. 안 되면 이유를 오류 줄에 */
  const edit = useCallback(async (change: () => Promise<Edited>) => {
    setError(null)
    changes.current++
    try {
      const r = await change()
      setPlan(r.plan)
      setChart(r.chart)
    } catch (e) {
      setError(e instanceof Error ? e.message : '계획을 바꾸지 못했습니다.')
      setRejects((n) => n + 1)
    }
  }, [])
  const planFromChat = useCallback((p: Plan) => {
    changes.current++
    setPlan(p)
  }, [])

  // 이동: 서버가 계획을 실행 값으로 확정하고 대상 묶음(이동부터)으로 넘어간다. 실행할 수 없는 계획이면 이유를 오류 줄에
  const [starting, setStarting] = useState(false)
  const start = async () => {
    if (starting) return
    setStarting(true)
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

function PlanChat({
  initialMessages,
  practiceReason,
  onPractice,
  error,
  onError,
  onPlan,
  onTurnEnd,
  onPick,
}: {
  initialMessages: Message[]
  /** AI를 쓸 수 없는 이유 — 있으면 대화 없이 이름 검색만 */
  practiceReason: string | null
  /** 대화 도중 AI를 쓸 수 없게 됨 */
  onPractice: (reason: string) => void
  error: string | null
  onError: (e: string | null) => void
  onPlan: (p: Plan) => void
  onTurnEnd: () => void
  onPick: (id: string) => void
}) {
  const ai = practiceReason === null
  const [messages, setMessages] = useState<Message[]>(initialMessages)
  const [busy, setBusy] = useState(false)
  const [working, setWorking] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [found, setFound] = useState<FoundTarget[] | null>(null)
  const listRef = useRef<HTMLDivElement>(null)

  // 새 글이 오면 맨 아래로
  useEffect(() => {
    const el = listRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages, busy])

  // 입력하면서 찾기 (N.I.N.A. 대상 목록, 오늘 밤 못 찍는 대상은 빠짐)
  useEffect(() => {
    const q = draft.trim()
    if (!q) return setFound(null)
    let alive = true
    const t = window.setTimeout(
      () =>
        void searchTargets(q)
          .then((r) => alive && setFound(r))
          .catch(() => alive && setFound(null)),
      150,
    )
    return () => {
      alive = false
      window.clearTimeout(t)
    }
  }, [draft])

  const send = useCallback(
    async (text: string, fromChoice = false) => {
      text = text.trim()
      if (!text || busy) return
      setBusy(true)
      setWorking(null)
      onError(null)
      setDraft('')
      setMessages((list) => {
        const next = [...list]
        // 선택지를 눌렀으면 그 메시지에 고른 것으로 남긴다
        if (fromChoice) {
          const i = next.findLastIndex((m) => m.role === 'assistant' && m.choices?.includes(text))
          if (i >= 0) next[i] = { ...next[i], picked: text }
        }
        return [...next, { role: 'user', text }, { role: 'assistant', text: '' }]
      })
      let failed: string | null = null
      let practice: string | null = null
      try {
        await sendChat(text, (e) => {
          if (e.type === 'status') setWorking(e.message)
          else if (e.type === 'text')
            setMessages((list) => {
              const last = list[list.length - 1]
              return [...list.slice(0, -1), { ...last, text: last.text + e.text }]
            })
          else if (e.type === 'choices')
            setMessages((list) => {
              const last = list[list.length - 1]
              return [...list.slice(0, -1), { ...last, choices: e.choices }]
            })
          else if (e.type === 'plan') onPlan(e.plan)
          else if (e.type === 'notice')
            // 안내 줄은 지금 쓰는 답(마지막 빈 말풍선) 앞에 넣는다
            setMessages((list) => [...list.slice(0, -1), { role: 'notice', text: e.message }, list[list.length - 1]])
          else if (e.type === 'error') failed = e.message
          else if (e.type === 'practice') practice = e.reason
        })
      } catch {
        failed = `${PRODUCT.name} 내부 서버와 연결이 끊겼습니다. 다시 보내 주세요.`
      }
      if (practice) {
        // 답하지 않은 말은 빼고 입력창에 되돌려 둔다 — 그 글자로 바로 이름 검색이 된다
        setMessages((list) => list.slice(0, -2))
        if (!fromChoice) setDraft(text)
        onPractice(practice)
      } else if (failed) {
        // 실패한 말은 대화에서 빼고 입력창에 되돌려 둔다 (서버도 기록에서 뺐다)
        setMessages((list) => {
          const trimmed = list.slice(0, -2)
          return fromChoice
            ? trimmed.map((m, i) => (i === trimmed.length - 1 && m.picked === text ? { ...m, picked: undefined } : m))
            : trimmed
        })
        if (!fromChoice) setDraft(text)
        onError(failed)
      }
      onTurnEnd()
      setBusy(false)
      setWorking(null)
    },
    [busy, onError, onPlan, onTurnEnd, onPractice],
  )

  const choose = (t: FoundTarget) => {
    setDraft('')
    setFound(null)
    onPick(t.id)
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    // AI가 있으면 묻기, 없으면 찾은 것 가운데 첫 번째를 고른다
    if (ai) void send(draft)
    else if (found?.length) choose(found[0])
  }

  // 지난 대화는 단계적으로 흐리게: 그 뒤에 사용자가 몇 번 말했나 (0 = 지금 대화)
  const ages: number[] = []
  for (let i = messages.length - 1, a = 0; i >= 0; i--) {
    ages[i] = Math.min(3, a)
    if (messages[i].role === 'user') a++
  }
  const lastAssistant = messages.findLastIndex((m) => m.role === 'assistant')

  return (
    <aside className={styles.chat} aria-label={ai ? `${PRODUCT.name}와 대화` : '대상 찾기'}>
      <div className={styles.messages} ref={listRef} aria-live="polite">
        {!ai && <p className={styles.notice}>{practiceReason} 대상은 이름으로 찾을 수 있어요.</p>}
        {messages.map((m, i) =>
          m.role === 'notice' ? (
            <p key={i} className={styles.notice}>
              {m.text}
            </p>
          ) : (
            <div key={i} className={styles.turn} data-age={ages[i]}>
              <div className={styles.message} data-role={m.role}>
                {m.role === 'assistant' && <span className={styles.who}>{PRODUCT.name}</span>}
                {m.text ||
                  (busy && i === messages.length - 1 ? (
                    <>
                      <span className={styles.typing} aria-label="답을 쓰는 중" />
                      {working && <span className={styles.working}>{working}</span>}
                    </>
                  ) : null)}
              </div>
              {m.role === 'assistant' && m.choices && m.choices.length > 0 && (
                <div className={styles.choices}>
                  {m.choices.map((c) => (
                    <button
                      key={c}
                      type="button"
                      className={styles.choice}
                      data-picked={m.picked === c}
                      disabled={busy || i !== lastAssistant || !!m.picked}
                      onClick={() => void send(c, true)}
                    >
                      {c}
                    </button>
                  ))}
                </div>
              )}
            </div>
          ),
        )}
      </div>
      {/* 오류 줄: 자리는 항상 확보 */}
      <p className={styles.error} data-shown={!!error} role="alert">
        {error}
      </p>
      <form className={styles.composer} onSubmit={submit}>
        {found && (
          <div className={styles.found}>
            <div className={styles.foundHint}>
              {found.length ? `N.I.N.A. 대상 목록에서 찾음${ai ? ` · Enter는 ${PRODUCT.name}에게 묻기` : ' · Enter는 첫 번째 고르기'}` : '찾은 대상이 없어요 (오늘 밤 이 관측지에서 못 찍는 대상은 빠져요)'}
            </div>
            {found.map((t) => (
              <button key={t.id} type="button" className={styles.foundItem} onClick={() => choose(t)}>
                <b>{t.name}</b>
                <span>{t.koreanName ?? t.commonName ?? ''}</span>
                <small>{t.type}</small>
              </button>
            ))}
          </div>
        )}
        <div className={styles.composerBox}>
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => e.key === 'Escape' && (setDraft(''), setFound(null))}
            placeholder={ai ? `대상 이름을 찾거나 ${PRODUCT.name}에게 물어보세요` : '대상 이름으로 찾기 (예: M31, 안드로메다)'}
            aria-label={ai ? `대상 찾기 또는 ${PRODUCT.name}에게 말하기` : '대상 이름으로 찾기'}
            maxLength={300}
          />
          {ai && (
            <button type="submit" className={styles.send} disabled={busy || !draft.trim()} aria-label="보내기">
              <ArrowRight strokeWidth={2} />
            </button>
          )}
        </div>
      </form>
    </aside>
  )
}

// ── 오른쪽 위: 오늘 밤 한 줄 + 프레임 미리보기 + 대상 · 언제 ─────────

function FramePreview({
  plan,
  current,
  minAltitude,
  onRemove,
  onResetStart,
}: {
  plan: Plan
  current: PlanPick | null
  /** 기본 시작·끝 고도 (서버 PlanRules) */
  minAltitude: number
  onRemove: (id: string) => void
  onResetStart: (id: string) => void
}) {
  const w = plan.window
  return (
    <section className={styles.fovRow}>
      <FovBox plan={plan} target={current} />
      <div className={styles.fovInfo}>
        <p className={styles.night}>{plan.night}</p>
        <div className={styles.tgt}>
          {current && (
            <>
              <div className={styles.big}>{displayName(current)}</div>
              <div className={styles.sub}>
                {current.constellation} {current.type}
                <br />
                {current.fillPercent !== null ? `화면 가로의 약 ${current.fillPercent}% · ` : ''}달과 {current.moonSeparation}°
              </div>
              <div className={styles.dropRow}>
                <Button size="sm" onClick={() => onRemove(current.id)}>
                  목록에서 제거
                </Button>
              </div>
            </>
          )}
        </div>
        <div className={styles.when}>
          {current &&
            (w ? (
              <WhenLines w={w} minAltitude={minAltitude} onReset={() => onResetStart(current.id)} />
            ) : (
              <p className={styles.warnLine}>
                {plan.startMoved ? '옮긴 시작으로는 찍을 수 있는 시간이 없어요. 시작을 기본으로 되돌려 보세요.' : '오늘 밤에는 찍을 수 있는 시간이 없어요.'}
                {plan.startMoved && (
                  <span className={styles.inlineReset}>
                    <ResetMark shown onReset={() => onResetStart(current.id)} label={`시작을 기본으로 (${minAltitude}° 위가 되는 때)`} />
                  </span>
                )}
              </p>
            ))}
        </div>
      </div>
    </section>
  )
}

/** 찍을 수 있는 시간 먼저, 기다려야 하면 그 까닭을 아래 줄에 (없으면 줄 없음). 시작을 옮겼으면 되돌리기 화살표 */
function WhenLines({ w, minAltitude, onReset }: { w: PlanWindow; minAltitude: number; onReset: () => void }) {
  const waitText =
    w.moved ? `${w.start}(고도 ${w.startAltitude}°)부터 찍기로 했어요.${w.wait === 'none' ? '' : ' 그때까지 기다려야 해요.'}`
    : w.wait === 'dark' ? `${w.start}에 어두워져요. 그때까지 기다려야 해요.`
    : w.wait === 'altitude' ? `${w.start}에 ${minAltitude}° 위로 올라와요. 그때까지 기다려야 해요.`
    : null
  return (
    <>
      <p>
        <b>{w.start}</b>부터 <b>{w.limit}</b>까지 찍을 수 있어요.{w.transit ? ' 중간에 자오선 반전이 있어요.' : ''}
      </p>
      {waitText && (
        <p className={styles.warnLine}>
          {waitText}
          {w.moved && (
            <span className={styles.inlineReset}>
              <ResetMark shown onReset={onReset} label={`시작을 기본으로 (${minAltitude}° 위가 되는 때)`} />
            </span>
          )}
        </p>
      )}
    </>
  )
}

/** 프레임 미리보기: 대상 좌표의 실제 하늘 사진(DSS2, 화각보다 조금 넓게) + 점선 = 찍히는 범위. 못 받으면 형상 그림 */
function FovBox({ plan, target }: { plan: Plan; target: PlanPick | null }) {
  const [failed, setFailed] = useState<string | null>(null)
  if (!target) return <div className={`${styles.fovBox} ${styles.fovEmpty}`}>프레임 미리보기</div>
  const { width, height } = plan.fov
  const aspect = width > 0 && height > 0 ? width / height : 1.5
  const url =
    'https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=CDS%2FP%2FDSS2%2Fcolor' +
    `&width=600&height=${Math.round(600 / aspect)}&fov=${(width * 1.07).toFixed(2)}&projection=TAN&coordsys=icrs` +
    `&ra=${target.raDeg}&dec=${target.decDeg}&format=jpg`
  const fill = target.fillPercent ?? 10
  const r = Math.max(8, ((280 * fill) / 100) / 2)
  return (
    <div className={styles.fovBox} style={{ aspectRatio: String(aspect) }}>
      <svg className={styles.shape} viewBox="0 0 300 200" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
        <Emblem type={target.type} cx={150} cy={100} r={r} />
      </svg>
      {failed !== url && <img src={url} alt="" onError={() => setFailed(url)} />}
      <div className={styles.frameLine} />
      <span className={styles.src}>
        DSS2 · 화각 {width}° × {height}°
      </span>
    </div>
  )
}

// ── 오른쪽 아래: 촬영 설정 줄 + 이동 ─────────

function SettingsRow({
  plan,
  current,
  waits,
  moveAt,
  starting,
  rejects,
  onChange,
  onGo,
}: {
  plan: Plan
  current: PlanPick | null
  /** 지금 누르면 바로 움직이지 않고 기다리기로 가는가 */
  waits: boolean
  moveAt: string | null
  starting: boolean
  /** 서버가 거절한 횟수 — 바뀌면 숫자 칸이 계획 값으로 돌아간다 */
  rejects: number
  onChange: (c: { filter?: string; iso?: number; exposure?: number; totalMinutes?: number }) => void
  onGo: () => void
}) {
  const s = plan.settings
  const o = plan.options
  const w = plan.window
  const off = !s || !current
  const gain = o.gain
  const short = !!w && w.clipped && w.maxFrames < w.frames

  const goLabel = !current || !w ? '-' : waits ? `${current.name} 기다리기 (${w.start})` : `${withRo(current.name)} 이동`
  const goTip = !current || !w ? '' : waits
    ? `${moveAt}에 적도의가 움직이고 ${w.start}부터 찍어요. 그때까지 기다리는 화면으로 넘어가요`
    : `누르면 적도의가 바로 ${current.name} 쪽으로 움직여요`

  return (
    <section className={styles.settings} aria-label="촬영 설정">
      <div className={styles.fields}>
        <Field label="필터" off={off}>
          <SelectField
            name="필터"
            value={s?.filter ?? null}
            options={o.filters.map((f) => ({ value: f.key, label: f.label }))}
            onSelect={(v) => onChange({ filter: v })}
            disabled={off}
          />
        </Field>
        <Field label="게인" off={off}>
          {gain.kind === 'list' ? (
            <SelectField
              name="게인"
              prefix={gain.unit}
              value={s?.iso ?? null}
              options={gain.values.map((v) => ({ value: v, label: String(v) }))}
              onSelect={(v) => onChange({ iso: v })}
              disabled={off}
              reset={<ResetMark shown={!!s && s.iso !== s.defaults.iso} onReset={() => s && onChange({ iso: s.defaults.iso })} />}
            />
          ) : (
            <NumberField
              name="게인"
              value={s?.iso ?? null}
              min={gain.min ?? 0}
              max={gain.max ?? 1000}
              step={10}
              unit=""
              onCommit={(v) => onChange({ iso: v })}
              revision={rejects}
              disabled={off}
              reset={<ResetMark shown={!!s && s.iso !== s.defaults.iso} onReset={() => s && onChange({ iso: s.defaults.iso })} />}
            />
          )}
        </Field>
        <Field label="노출 (한 장)" off={off}>
          <NumberField
            name="노출"
            value={s?.exposure ?? null}
            min={o.exposure.min}
            max={o.exposure.max}
            step={o.exposure.step}
            unit="초"
            onCommit={(v) => onChange({ exposure: v })}
            revision={rejects}
            disabled={off}
            reset={<ResetMark shown={!!s && s.exposure !== s.defaults.exposure} onReset={() => s && onChange({ exposure: s.defaults.exposure })} />}
          />
        </Field>
        <Field label="총 노출" off={off}>
          <NumberField
            name="총 노출"
            value={s?.totalMinutes ?? null}
            min={o.totalMinutes.min}
            max={o.totalMinutes.max}
            step={o.totalMinutes.step}
            unit="분"
            onCommit={(v) => onChange({ totalMinutes: v })}
            revision={rejects}
            disabled={off}
            reset={<ResetMark shown={!!s && s.totalMinutes !== s.defaults.totalMinutes} onReset={() => s && onChange({ totalMinutes: s.defaults.totalMinutes })} />}
          />
        </Field>
        <Field label="촬영 횟수" tone="result" off={off}>
          <span className={styles.count}>{w && !off ? `${short ? w.maxFrames : w.frames}장` : '-'}</span>
          {short && w && (
            <Tooltip text={whyShort(w)} placement="top" align="end" className={styles.warnTip}>
              <span className={styles.warnIcon} tabIndex={0} aria-label="장수가 줄어든 이유">
                !
              </span>
            </Tooltip>
          )}
        </Field>
        <Tooltip text={goTip} placement="top" align="end" className={styles.goTip}>
          <Button variant="primary" className={styles.go} onClick={onGo} disabled={!current || !w || !plan.complete || starting}>
            {goLabel}
          </Button>
        </Tooltip>
      </div>
    </section>
  )
}

/** 촬영 횟수가 목표보다 적은 까닭: "가장 빨리 hh:mm에 찍기 시작해도 {끝 사유}. {시간} ÷ 한 장 n초(쉬는 시간 포함) = n장" */
function whyShort(w: PlanWindow) {
  const h = Math.floor(w.minutes / 60)
  const m = w.minutes % 60
  const stop = w.stop === 'dawn' ? `${w.limit}에 새벽이 와요` : `${w.limit}에 ${w.endAltitude}° 아래로 내려가요`
  const lead = w.moved ? `${w.start}에 찍기 시작하면` : `가장 빨리 ${w.start}에 찍기 시작해도`
  return `${lead} ${stop}. ${h ? `${h}시간 ` : ''}${m ? `${m}분` : ''} ÷ 한 장 ${Math.round(w.secondsPerFrame)}초(쉬는 시간 포함) = ${w.maxFrames}장${w.transit ? ' (자오선 반전 5분 빼고)' : ''}`
}
