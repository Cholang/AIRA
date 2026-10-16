import { Settings as Gear } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { inDesktop, pickExe } from '../host'
import { PRODUCT } from '../product'
import Button from './Button'
import Modal from './Modal'
import styles from './Settings.module.css'

/** 서버 /api/settings (AppServer.MapSettings) — AI 키 자체는 오지 않는다 */
interface SettingsView {
  aiEnabled: boolean
  /** 키가 어디서 왔나: 설정 화면에서 넣음 · 이 PC 설정(사용자 비밀·환경 변수) · 없음 */
  keyFrom: 'settings' | 'system' | null
  ninaExe: string | null
  ninaAuto: string | null
  sharpCapExe: string | null
  sharpCapAuto: string | null
  logsDir: string
}

/**
 * 설정 (2026-10-11 사용자 결정 — 앱 전체에 걸린 값만: AI 비서 · 프로그램 경로 · 기록).
 * 상태 줄의 톱니 버튼으로 연다(어느 화면에서든 — 문제가 생긴 그 자리에서 진단을 내보낼 수 있게). 장비·관측지·전원 배선은 프로필에 있다.
 * 안전 동작(포커서 0, 추적 끄기)은 설정으로 끄지 못한다.
 */
export function SettingsButton() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" className={styles.gear} aria-label="설정" title="설정" onClick={() => setOpen(true)}>
        <Gear strokeWidth={2} aria-hidden="true" />
      </button>
      {open && <SettingsDialog onClose={() => setOpen(false)} />}
    </>
  )
}

function SettingsDialog({ onClose }: { onClose: () => void }) {
  const [view, setView] = useState<SettingsView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const [key, setKey] = useState('')
  const [exporting, setExporting] = useState(false) // 진단 내보내기 중 — 두 번 누르지 않게 (CX-24-06)

  const load = useCallback(() => {
    fetch('/api/settings')
      .then((r) => (r.ok ? (r.json() as Promise<SettingsView>) : null))
      .then(setView, () => setView(null))
  }, [])
  useEffect(load, [load])

  const put = async (url: string, body: unknown) => {
    setError(null)
    setNote(null)
    const r = await fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).catch(() => null)
    if (!r?.ok) setError(((await r?.json().catch(() => null)) as { error?: string } | null)?.error ?? '저장하지 못했습니다')
    load()
    return !!r?.ok
  }

  const pick = async (which: 'nina' | 'sharpcap', name: string) => {
    const path = await pickExe(name)
    if (path) await put(`/api/settings/paths/${which}`, { path })
  }

  return (
    <Modal open onClose={onClose} size="lg" title="설정" actions={<Button onClick={onClose}>닫기</Button>}>

      {!view ? (
        <p className={styles.muted}>설정을 읽는 중입니다</p>
      ) : (
        <>
          {/* 1. AI 비서 */}
          <section className={styles.section}>
            <h3>AI 비서</h3>
            <label className={styles.switchRow}>
              <input type="checkbox" checked={view.aiEnabled} onChange={(e) => void put('/api/settings/ai', { enabled: e.target.checked })} />
              <span>
                <b>계획 대화에 AI를 씁니다</b>
                <small>끄면 계획 화면은 대화 없이 대상 이름 검색만 씁니다.</small>
              </span>
            </label>
            <div className={styles.field}>
              <span className={styles.label}>Gemini API 키</span>
              <p className={styles.status}>
                {view.keyFrom === 'settings' ? '이 화면에서 넣은 키를 씁니다' : view.keyFrom === 'system' ? '이 PC의 설정에서 읽은 키를 씁니다' : '키가 없습니다 — 계획 화면은 대상 이름 검색만 씁니다'}
              </p>
              <div className={styles.row}>
                <input
                  className={styles.input}
                  type="password"
                  autoComplete="off"
                  spellCheck={false}
                  placeholder={view.keyFrom ? '새 키로 바꾸려면 붙여 넣기' : '키 붙여 넣기'}
                  value={key}
                  onChange={(e) => setKey(e.target.value)}
                />
                <Button
                  size="sm"
                  variant="primary"
                  disabled={!key.trim()}
                  onClick={async () => {
                    if (await put('/api/settings/ai-key', { key })) {
                      setKey('')
                      setNote('키를 저장했습니다. 다음 계획 대화부터 씁니다.')
                    }
                  }}
                >
                  저장
                </Button>
                {view.keyFrom === 'settings' && (
                  <Button size="sm" onClick={() => void put('/api/settings/ai-key', { key: null })}>
                    지우기
                  </Button>
                )}
              </div>
              <small className={styles.muted}>키는 이 PC의 Windows 계정으로만 풀 수 있게 암호화해 저장합니다.</small>
            </div>
          </section>

          {/* 2. 프로그램 경로 */}
          <section className={styles.section}>
            <h3>프로그램 경로</h3>
            <PathRow
              title="N.I.N.A."
              chosen={view.ninaExe}
              auto={view.ninaAuto}
              onPick={() => void pick('nina', 'NINA.exe')}
              onAuto={() => void put('/api/settings/paths/nina', { path: null })}
            />
            <PathRow
              title="SharpCap"
              chosen={view.sharpCapExe}
              auto={view.sharpCapAuto}
              onPick={() => void pick('sharpcap', 'SharpCap.exe')}
              onAuto={() => void put('/api/settings/paths/sharpcap', { path: null })}
            />
            <small className={styles.muted}>PHD2는 N.I.N.A.가 켭니다 — 경로는 N.I.N.A.의 가이더 설정에서 정합니다.</small>
          </section>

          {/* 3. 기록 */}
          <section className={styles.section}>
            <h3>기록</h3>
            <p className={styles.status}>{PRODUCT.name}의 단계·장비 연결 기록은 날짜별 파일로 남습니다 (14일 보관).</p>
            <div className={styles.row}>
              <Button size="sm" onClick={() => void fetch('/api/settings/open-logs', { method: 'POST' })}>
                기록 폴더 열기
              </Button>
              <Button
                size="sm"
                disabled={exporting}
                onClick={async () => {
                  setError(null)
                  setNote(null)
                  setExporting(true)
                  const r = await fetch('/api/settings/diagnostics', { method: 'POST' }).catch(() => null)
                  const b = r?.ok ? ((await r.json().catch(() => null)) as { path: string } | null) : null
                  setExporting(false)
                  if (b) setNote(`바탕 화면에 만들었습니다: ${b.path.split('\\').pop()}`)
                  else setError('진단 묶음을 만들지 못했습니다')
                }}
              >
                {exporting ? '만드는 중…' : '진단 내보내기'}
              </Button>
            </div>
            <small className={styles.muted}>
              진단 내보내기 = 최근 3일 {PRODUCT.name} 기록 + N.I.N.A.·PHD2 최근 기록 + 버전을 zip 하나로 바탕 화면에. 문제를 물어볼 때 이 파일을 함께 주세요. AI 키는 넣지 않습니다.
            </small>
          </section>
        </>
      )}

      <p className={styles.message} data-tone={error ? 'fail' : 'ok'} aria-live="polite">
        {error ?? note ?? ''}
      </p>
    </Modal>
  )
}

/** 프로그램 하나: 지금 쓰는 경로(직접 지정 · 자동으로 찾음 · 찾지 못함) + 찾아보기 · 자동으로 */
function PathRow({ title, chosen, auto, onPick, onAuto }: { title: string; chosen: string | null; auto: string | null; onPick: () => void; onAuto: () => void }) {
  const using = chosen ?? auto
  // 촬영(적색) 테마에서는 Windows 파일 선택 창(밝은 흰 창)을 열지 않는다 — 눈의 암적응이 깨진다 (DESIGN.md 6-2)
  const night = document.documentElement.dataset.theme === 'night'
  return (
    <div className={styles.field}>
      <span className={styles.label}>{title}</span>
      <p className={styles.path} data-missing={!using} title={using ?? undefined}>
        {using ?? '찾지 못했습니다 — 찾아보기로 실행 파일을 골라 주세요'}
      </p>
      <small className={styles.muted}>{chosen ? '직접 지정한 경로' : auto ? '자동으로 찾은 경로' : ''}</small>
      <div className={styles.row}>
        <Button
          size="sm"
          onClick={() => !night && onPick()}
          disabled={!inDesktop()}
          aria-disabled={night || undefined}
          title={night ? 'Windows 파일 선택 창은 밝은 흰 창이라 촬영 테마에서는 열지 않습니다. 기본 테마로 바꾼 뒤 골라 주세요' : undefined}
        >
          찾아보기
        </Button>
        {chosen && (
          <Button size="sm" onClick={onAuto}>
            자동으로 찾기
          </Button>
        )}
      </div>
    </div>
  )
}
