import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import LiveView from '../components/LiveView'
import PrepCenter from '../components/PrepCenter'
import PrepGuide from '../components/PrepGuide'
import { FlipSteps, ShootGauges, ShootGrades } from '../components/ShootPanels'
import { useScreenSky } from '../components/ScreenSky'
import { useRailExtra } from '../components/StepRail'
import { stageLabel } from '../appFlow'
import { postSkip, useTempSkip } from '../components/TempSkip'
import type { CurrentView, PrepAction, Tone } from '../prepare'
import { BUTTON } from '../buttonText'
import { answerShoot, recheckShootStop, stopShoot, watchShoot, type ShootView } from '../shoot'
import styles from './PrepareScreen.module.css'
import { T, TF } from '../uiText'

/**
 * 촬영 (DESIGN.md "촬영", 시안 mockups/aa-shoot-wrap-v10.html).
 * 주인공은 방금 찍은 사진(배경 전체). 왼쪽 위 안내, 가운데 아래 계기판(가이딩 · 진행 · 별 크기), 왼쪽 아래 등급.
 * 디더링·자오선 반전·초점 다시 맞추기·구름 멈춤은 서버가 묻지 않고 하고, 화면은 보여 주기만 한다.
 * 레일 아래 "촬영 중단" → 끝내고 마무리하기 / 다른 대상으로 변경 / 계속 찍기 (지금 사진까지 찍고 멈춤).
 * 계획 장수·대상 낮아짐·새벽이면 저절로 끝 → 플랫 찍으러 가기 / 다른 대상으로 변경.
 */

type Then = 'wrap' | 'retarget'

const ENDED: Record<string, string> = {
  done: '계획한 장수를 다 찍었어요. ',
  low: '대상이 계획한 고도 아래로 내려가 계획대로 끝냈어요. ',
  dawn: '새벽 박명이 시작돼 끝냈어요. ',
  user: '',
  error: '사진을 계속 받지 못해 촬영을 멈췄어요. ',
  cloud: '별이 30분 넘게 돌아오지 않아 촬영을 멈췄어요. 하늘을 확인해 주세요. ',
  mount: '적도의가 추적을 멈췄어요. 한계에 닿았거나 전원이 끊겼는지 확인해 주세요. ',
  guider: '가이드 카메라(PHD2) 연결이 끊겨 다시 연결하지 못했어요. 연결을 확인해 주세요. ',
  flip: '자오선 반전이 되지 않아 촬영을 멈췄어요. 적도의를 확인해 주세요. ',
  recenter: '다시 가운데로 맞추지 못해 촬영을 멈췄어요. ',
  focus: '자동초점이 멈췄는지 확인하지 못해 촬영을 멈췄어요. N.I.N.A.를 확인해 주세요. ',
  frames: '별이 없는 사진이 30분 넘게 이어져 촬영을 멈췄어요. 렌즈 덮개·이슬·초점·구름을 확인해 주세요. ',
}

/** 가이드 별을 잃어 멈췄을 때 원인별 안내 (docs/implementation/SHOOT.md B) */
const PAUSE: Record<string, [string, string]> = {
  light: [T('title.shoot.pause.light'), '차 불빛이나 손전등 같은 빛에 가이드 별이 묻혔어요. 별을 새로 고르지 않고 같은 자리에서 기다려요. 빛이 찍힌 사진은 등급을 보고 제외 폴더로 옮겨요.'],
  cloud: [T('title.shoot.pause.cloud'), '가이드 별을 잃어 촬영을 잠시 멈췄어요. 별이 돌아오면 자동으로 이어서 찍고, 오래 멈췄으면 다시 가운데로 맞춰요. 30분 넘게 돌아오지 않으면 멈추고 알려 드려요.'],
  jump: [T('title.shoot.pause.jump'), '바람이나 케이블 걸림으로 적도의가 흔들린 것 같아요. 가이드 별을 다시 고르고 이어서 찍어요.'],
  guider: [T('title.shoot.pause.guider'), 'PHD2 가이드 카메라를 다시 연결하고 있어요. 두 번 해도 안 되면 촬영을 멈추고 알려 드려요.'],
  frames: [T('title.shoot.pause.frames'), '가이드 별은 괜찮은데 사진에 별이 없어요. 렌즈 덮개, 주 망원경 렌즈의 이슬, 초점, 구름을 확인해 주세요. 3분마다 한 장씩 찍어 별이 돌아오면 이어서 찍고, 30분 넘게 그대로면 촬영을 멈춰요.'],
  hotpixel: [T('title.shoot.pause.hotpixel'), '가이드 카메라가 별 대신 핫픽셀이나 너무 작은 별을 잡은 것 같아요. 기다리면 같은 점을 다시 잡을 수 있어 바로 다른 별을 골라요. 자주 생기면 가이드 노출을 늘려요.'],
  unstable: [T('title.shoot.pause.unstable'), '디더링 뒤 가이드가 세 번 연속 자리를 잡지 못해 촬영을 잠시 멈췄어요. 가이드 오차가 1분 동안 기준 안에 머물면 자동으로 이어서 찍어요. 바람·케이블 걸림·가이드 초점을 확인해 주세요.'],
}

/** [임시] 모의 실패 (모의 장비일 때만) — 서버 SimFaults.Known */
const FAULTS: [string, string][] = [
  ['shoot.trail', '다음 사진 별 흐름'],
  ['shoot.cloud', '구름'],
  ['shoot.light', '강한 빛'],
  ['shoot.wind', '바람·케이블'],
  ['shoot.dew', '이슬'],
  ['shoot.guider', '가이드 카메라 끊김'],
  ['shoot.mount', '적도의 멈춤'],
  ['shoot.temp', '기온 변화'],
  ['shoot.flip', '반전 시각'],
  ['shoot.low', '대상 낮아짐'],
  ['shoot.flipfail', '반전 실패'],
  ['shoot.stopfail', '끝날 때 가이드 정지 확인 실패'],
  ['shoot.abortfail', '노출 멈춤 실패'],
  ['shoot.unstable', '가이드 불안정 (곧 안정)'],
  ['shoot.unstablelong', '가이드 불안정 (오래)'],
  ['shoot.nostars', '별 없는 사진 5장 (렌즈 덮개)'],
  ['shoot.flipmoving', '반전 실패 + 적도의 정지 모름'],
  ['shoot.hotpixel', '가이드 별 대신 핫픽셀'],
]

export default function ShootScreen({ onWrap, onRetarget }: { onWrap: () => void; onRetarget: () => void }) {
  const [view, setView] = useState<ShootView | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [asking, setAsking] = useState(false)
  const [then, setThen] = useState<Then | null>(null)
  const [full, setFull] = useState(false) // 사진 전체 화면
  const [actError, setActError] = useState<string | null>(null)
  const [faultsOpen, setFaultsOpen] = useState(false)

  useEffect(() => watchShoot(setView, setProblem), [])

  const ended = view?.mode === 'Ended'

  // [임시] 건너뛰기 (2026-10-14): 다 찍은 것(성공)·오류로 끝난 것(실패)으로 끝낸다 — 그 뒤 마무리·다른 대상은 평소처럼 고른다
  useTempSkip(
    view && view.mode !== 'Idle'
      ? { what: '촬영', success: () => postSkip('/api/shoot/temp-skip?ok=true'), failure: () => postSkip('/api/shoot/temp-skip?ok=false') }
      : null,
  )
  // 중단을 고른 뒤 끝나면 고른 곳으로
  const stopped = (view?.guideStopped ?? true) && (view?.mountStopped ?? true)
  useEffect(() => {
    if (!ended || !then || !stopped) return
    if (then === 'wrap') onWrap()
    else onRetarget()
  }, [ended, then, stopped, onWrap, onRetarget])

  useScreenSky(true)
  useRailExtra({
    stage: 'shoot',
    action: view && !ended && !then ? { label: T('button.shoot.stop'), onClick: () => setAsking(true) } : undefined,
  })

  const stop = async (to: Then) => {
    setAsking(false)
    setActError(null)
    const err = await stopShoot()
    if (err) setActError(err)
    else setThen(to)
  }

  // 하늘 화면: 방금 찍은 사진 (초점을 다시 맞출 때는 초점 곡선)
  const live: CurrentView | null = useMemo(
    () =>
      view
        ? {
            taskId: 'shoot',
            runId: 0,
            subSteps: [],
            guide: { title: '', text: '' },
            center: { readout: null, status: null, actions: [] },
            live:
              view.mode === 'Focus'
                ? { kind: 'focus-curve', url: null, data: { points: view.focusPoints ?? [], last: null }, observedAt: view.photoAt ?? '' }
                : { kind: 'test-photo', url: view.photoUrl ? `${view.photoUrl}?t=${encodeURIComponent(view.photoAt ?? '')}` : null, data: null, observedAt: view.photoAt ?? '' },
          }
        : null,
    [view],
  )

  if (problem)
    return (
      <main className={styles.stage}>
        <LiveView current={null} />
        <PrepGuide task={stageLabel('shoot')} title={T('title.startFailed')} text={problem} />
      </main>
    )
  if (!view) return <main className={styles.stage} aria-busy="true" />

  const target = view.target ?? ''
  let title = TF('title.shoot.running', { target })
  let text = `계획대로 ${view.exposureSeconds}초씩 찍고 있어요. 사진마다 등급을 매겨 실패한 사진은 제외 폴더로 옮겨요. 자오선 반전·디더링·초점 다시 맞추기는 자동이에요. 자오선 반전 때는 적도의가 반대편으로 크게 도니 케이블이 걸리지 않게 해 주세요.`
  let actions: PrepAction[] = []
  let prompt: string | null = null
  let custom: ReactNode = <ShootGauges v={view} />
  if (asking && !ended) {
    title = T('title.shoot.askStop')
    text = '지금 찍는 사진은 끝까지 찍고 멈춰요. 장비 준비는 그대로 써요.'
    actions = [
      { id: 'wrap', label: T('button.shoot.stopAndWrap'), primary: true },
      { id: 'retarget', label: T('button.newTarget'), primary: false },
      { id: 'continue', label: T('button.shoot.continue'), primary: false },
    ]
    prompt = '촬영을 멈추고 플랫·다크를 찍어 오늘 밤을 마무리할까요?\n새 대상을 찾기를 누르면 계획 화면으로 돌아가 다른 대상을 고르고, 계속 찍기를 누르면 그대로 찍어요.'
  } else if (then) {
    title = T('title.shoot.stopping')
    text = then === 'wrap' ? '지금 사진까지 찍고 가이드를 멈춰요. 그다음 보정 프레임을 찍어요.' : '지금 사진까지 찍고 가이드를 멈춰요. 그다음 대상 단계(계획)로 돌아가요.'
  } else if (view.mode === 'Flip') {
    title = T('title.shoot.flip')
    text = '대상이 자오선을 지나 적도의를 반대편으로 돌리고 있어요. 다시 가운데로 맞추고 가이드를 켠 뒤 이어서 찍어요. 찍은 사진은 그대로예요.'
    custom = <FlipSteps step={view.flipStep} />
  } else if (view.mode === 'Focus') {
    title = T('title.shoot.refocus')
    text = '찍던 사진을 마치고 자동으로 다시 맞춘 뒤 이어서 찍어요.'
    const p = view.focusPoints?.at(-1)
    custom = (
      <div className={styles.metricPlain}>
        <strong>{p ? p.hfr.toFixed(1) : '—'}</strong>
        <span>별 크기 (HFR) · 이번 지점 · 자동초점 {view.focusPoints?.length ?? 0} / 9</span>
      </div>
    )
  } else if (view.mode === 'Paused' && view.ask === 'unstable') {
    title = T('title.shoot.unstableLong')
    text = '구름이나 바람 때문일 수 있어요.'
    actions = [
      { id: 'ask-shoot', label: T('button.shoot.keepGoing'), primary: true },
      { id: 'ask-wait', label: T('button.shoot.wait'), primary: false },
    ]
    prompt = '별이 흐른 사진은 등급을 보고 제외 폴더로 옮기며 그대로 찍을까요? 이 대상에서는 다시 멈추지 않아요.\n더 기다리기를 누르면 15분 뒤에 다시 물어봐요.'
  } else if (view.mode === 'Paused') {
    ;[title, text] = PAUSE[view.pause ?? 'cloud'] ?? PAUSE.cloud
  } else if (ended && !stopped) {
    // 가이딩 정지를 확인하지 못함: 다시 확인하기 전에는 다음으로 가지 않는다 (CX-SHOOT-01)
    if (!view.mountStopped) {
      title = T('title.shoot.checkMount')
      text = '자오선 반전이 되지 않아 촬영을 멈췄는데, 적도의가 아직 움직이는지 확인하지 못했어요. 움직이는 채로 다른 이동을 명령하면 위험해요.'
      prompt = '적도의를 직접 본 뒤 장비 상태를 다시 읽어 볼까요?'
    } else {
      title = T('title.shoot.checkGuide')
      text = '촬영은 멈췄지만 PHD2 가이드가 멈췄는지 확인하지 못했어요. 가이드가 켜진 채로 적도의를 옮기면 위험해요.'
      prompt = 'PHD2를 확인한 뒤 장비 상태를 다시 읽어 볼까요?'
    }
    actions = [{ id: 'recheck', label: BUTTON.recheck, primary: true }]
  } else if (ended) {
    title = T('title.shoot.ended')
    text = `${ENDED[view.ended ?? ''] ?? ''}가이드를 멈췄어요. 이제 보정 프레임(플랫·다크)을 찍고 장비를 정리해요.`
    actions = [
      { id: 'wrap', label: T('button.flat.start'), primary: true },
      { id: 'retarget', label: T('button.newTarget'), primary: false },
    ]
    prompt = '망원경을 하늘 위쪽으로 돌려 플랫을 찍을까요? 누르면 적도의가 움직여요.\n아직 밤이 남았으면 새 대상을 찾기를 눌러 다른 대상을 골라도 돼요.'
    custom = (
      <div className={styles.metricPlain}>
        <strong data-tone="Ok">{view.good}장</strong>
        <span>
          {target} · 쓸 사진 {view.good}장 · 제외 {view.excluded}장(제외 폴더)
        </span>
      </div>
    )
  }

  // 사진 전체 화면 (2026-10-11 사용자 요청): 사진을 누르면 앱 창 전체로, 다시 누르면 돌아온다. 그동안 새 사진이 오면 바로 바뀐다(가장 싼 방법 — 사용자 허락)
  const photoUrl = view.mode !== 'Focus' && live?.live?.url ? live.live.url : null

  const status: { text: string; tone: Tone } | null = actError ? { text: actError, tone: 'Fail' } : view.note

  const act = async (id: string) => {
    if (id === 'continue') return setAsking(false)
    if (id === 'recheck') return setActError(await recheckShootStop())
    if (id === 'ask-shoot' || id === 'ask-wait') return setActError(await answerShoot(id === 'ask-shoot' ? 'shoot' : 'wait'))
    if (ended) return id === 'wrap' ? onWrap() : onRetarget()
    void stop(id as Then)
  }

  return (
    <main className={styles.stage}>
      {full && photoUrl && <FullPhoto url={photoUrl} onClose={() => setFull(false)} />}
      <LiveView current={live} simulated={view.simulated} onPhoto={photoUrl ? () => setFull(true) : undefined} />
      <div className={styles.scrim} />
      <div className={styles.over}>
        <PrepGuide task={stageLabel('shoot')} title={title} text={text} />
        <PrepCenter readout={null} status={status} actions={actions} prompt={prompt} onAct={(id) => void act(id)} custom={custom} />
        <ShootGrades v={view} />
      </div>
      {/* [임시] 모의 실패: 다음 동작 하나를 일부러 (모의 장비일 때만) */}
      {view.simulated && !ended && (
        <div className={styles.temp}>
          <button type="button" className={styles.tempToggle} onClick={() => setFaultsOpen((o) => !o)} aria-expanded={faultsOpen}>
            [임시] 모의 실패
          </button>
          {faultsOpen && (
            <div className={styles.tempList}>
              {FAULTS.map(([k, label]) => (
                <button key={k} type="button" onClick={() => void fetch(`/api/prepare/sim/fail-next/${k}`, { method: 'POST' })}>
                  {label}
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </main>
  )
}

/** 촬영 사진 전체 화면: 앱 틀(축소된 화면) 밖, 창 전체에 (portal). 누르거나 Esc로 닫는다 */
function FullPhoto({ url, onClose }: { url: string; onClose: () => void }) {
  useEffect(() => {
    const key = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [onClose])
  return createPortal(
    <button type="button" className={styles.fullPhoto} onClick={onClose} aria-label="전체 화면 닫기">
      <img src={url} alt="" />
    </button>,
    document.body,
  )
}
