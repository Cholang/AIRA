import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import styles from './TempSkip.module.css'

/**
 * [임시] 건너뛰기 (2026-10-14 사용자 요청 — 장비 없이도 모든 단계를 넘겨 보며 화면을 확인).
 * 진행 표시 아래(종료 버튼 위)에 늘 같은 자리. 성공 = 더미 성공 기록으로 다음 단계, 실패 = 실패 기록을 남기고 다음으로.
 * 실패가 의미 없는 단계(설치 확인·엔진 켜기·장비 연결·출발 전 점검·계획·초점 확인)는 성공만.
 * 화면은 useTempSkip으로 무엇을 넘길지 알리고, 단순한 단계는 App이 기본값을 준다.
 * 지울 때: 이 파일 · App·PrepareScreen·ShootScreen의 useTempSkip/TempSkipProvider · 서버 temp-skip ("[임시] 건너뛰기"로 찾으면 됨)
 */
export interface TempSkipSpec {
  /** 넘기는 것 (예: "캘리브레이션") */
  what: string
  /** 실패면 이유 문장, 성공이면 null/undefined */
  success: () => Promise<string | null | void> | void
  /** 없으면 성공만 */
  failure?: () => Promise<string | null | void> | void
}

const Ctx = createContext<(spec: TempSkipSpec | null) => void>(() => {})

export function TempSkipProvider({ children, onChange }: { children: ReactNode; onChange: (spec: TempSkipSpec | null) => void }) {
  return <Ctx.Provider value={onChange}>{children}</Ctx.Provider>
}

/** 화면에서: 지금 넘길 것을 알린다 (화면을 떠나면 지움). 함수는 늘 최신 것을 부른다 */
export function useTempSkip(spec: TempSkipSpec | null) {
  const set = useContext(Ctx)
  const latest = useRef(spec)
  latest.current = spec
  const key = spec ? `${spec.what}|${!!spec.failure}` : ''
  useEffect(() => {
    set(
      latest.current && {
        what: latest.current.what,
        success: () => latest.current?.success(),
        failure: latest.current.failure && (() => latest.current?.failure?.()),
      },
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, set])
  useEffect(() => () => set(null), [set])
}

/** POST 후 실패면 서버의 이유 문장 */
export async function postSkip(url: string): Promise<string | null> {
  try {
    const r = await fetch(url, { method: 'POST' })
    if (r.ok) return null
    return ((await r.json().catch(() => null)) as { error?: string } | null)?.error ?? '건너뛰지 못했습니다'
  } catch {
    return '내부 서버와 연결이 끊겼습니다'
  }
}

export function TempSkipButtons({ spec }: { spec: TempSkipSpec | null }) {
  const [busy, setBusy] = useState(false)
  // 이유는 그 단계(what)에만 — 다음 단계로 넘어가면 보이지 않는다
  const [failed, setFailed] = useState<{ what: string; text: string } | null>(null)
  if (!spec) return null
  const error = failed?.what === spec.what ? failed.text : null
  const run = async (fn: () => Promise<string | null | void> | void) => {
    if (busy) return
    setBusy(true)
    setFailed(null)
    try {
      const problem = await fn()
      if (problem) setFailed({ what: spec.what, text: problem })
    } finally {
      setBusy(false)
    }
  }
  return (
    <div className={styles.skip}>
      <span className={styles.what}>[임시] {spec.what}</span>
      <div className={styles.row}>
        <button type="button" onClick={() => void run(spec.success)} disabled={busy}>
          건너뛰기-성공
        </button>
        {spec.failure && (
          <button type="button" onClick={() => void run(spec.failure!)} disabled={busy}>
            건너뛰기-실패
          </button>
        )}
      </div>
      {error && <span className={styles.error}>{error}</span>}
    </div>
  )
}
