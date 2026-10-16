import { useState } from 'react'
import Button from '../components/Button'
import { ResetMark } from '../components/Field'
import { Emblem } from '../components/NightGraph'
import {
  displayName,
  type Plan,
  type PlanPick,
  type PlanWindow,
} from '../plan'
import styles from './PlanScreen.module.css'

/** 계획 화면의 대상 미리보기: 구도·시간대 줄·화각 상자 (큰 파일 분리 2026-10-16 — 옮기기만) */

export function FramePreview({
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
export function WhenLines({ w, minAltitude, onReset }: { w: PlanWindow; minAltitude: number; onReset: () => void }) {
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
export function FovBox({ plan, target }: { plan: Plan; target: PlanPick | null }) {
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
