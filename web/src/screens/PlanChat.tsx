import { ArrowRight } from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import {
  searchTargets,
  sendChat,
  type FoundTarget,
  type Message,
  type Plan,
} from '../plan'
import { PRODUCT } from '../product'
import styles from './PlanScreen.module.css'

/** 계획 화면의 AI 대화 (큰 파일 분리 2026-10-16 — 옮기기만) */

export function PlanChat({
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
