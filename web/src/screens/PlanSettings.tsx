import Button from '../components/Button'
import { Field, NumberField, ResetMark, SelectField } from '../components/Field'
import Tooltip from '../components/Tooltip'
import {
  type Plan,
  type PlanPick,
  type PlanWindow,
} from '../plan'
import { withRo } from '../product'
import styles from './PlanScreen.module.css'

/** 계획 화면의 설정 줄 (큰 파일 분리 2026-10-16 — 옮기기만) */

export function SettingsRow({
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
