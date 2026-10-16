import { useMemo } from 'react'
import type { ReadoutView } from '../prepare'
import styles from './LiveView.module.css'
import { quadFit } from './liveViewMath'
import { clamp, FX, FY, polarStars, VW } from './liveSimFields'
import type { SlewTrack } from './useSlewTrack'

/** 하늘 화면(LiveView)의 작업별 판: 초점·SharpCap·가이드 사진·가이드 그래프·하늘 지도·초점 곡선 (큰 파일 분리 2026-10-16 — 옮기기만) */

/**
 * 자동초점 진행 (2026-10-09 사용자 요청 — N.I.N.A.의 자동초점 창 대신): 바탕은 N.I.N.A.가 찍은 마지막 사진, 오른쪽 세로 가운데에 측정점.
 * 가로 = 포커서 위치, 세로 = 별 크기(HFR, 아래가 작음 = 초점). 별을 못 찾은 지점(HFR 0)은 아래 줄에 빈 점으로
 */
export function FocusPanel({ data }: { data: unknown }) {
  const d = data as { points?: { pos: number; hfr: number }[]; last?: number | null } | null
  const pts = d?.points ?? []
  const W = 320
  const H = 168
  const x0 = 20
  const x1 = W - 20
  const top = 40
  const y0 = 128
  const good = pts.filter((p) => p.hfr > 0)
  const xs = pts.map((p) => p.pos).concat(d?.last != null ? [d.last] : [])
  const lo = xs.length ? Math.min(...xs) : 0
  const hi = xs.length ? Math.max(...xs) : 1
  const span = Math.max(1, hi - lo)
  const maxH = good.length ? Math.max(...good.map((p) => p.hfr)) : 1
  const x = (pos: number) => x0 + ((x1 - x0) * (pos - lo)) / span
  const y = (h: number) => y0 - ((y0 - top) * h) / (maxH * 1.15)
  const sorted = [...good].sort((a, b) => a.pos - b.pos)
  const best = good.length ? good.reduce((a, b) => (b.hfr < a.hfr ? b : a)) : null
  return (
    <div className={styles.hist}>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="자동초점 측정점">
        <text x={x0} y={24} className={styles.small}>
          자동초점 · {pts.length} 지점
        </text>
        <text x={x1} y={24} textAnchor="end" className={styles.small}>
          {best ? `가장 작은 별 ${best.hfr.toFixed(1)} · ${best.pos.toLocaleString()}` : pts.length ? '별을 찾지 못함' : '기다리는 중'}
        </text>
        <line x1={x0} y1={y0} x2={x1} y2={y0} className={styles.axis} />
        {sorted.length > 1 && <path d={sorted.map((p, i) => `${i ? 'L' : 'M'}${x(p.pos).toFixed(1)},${y(p.hfr).toFixed(1)}`).join('')} className={styles.ra} />}
        {pts.map((p, i) =>
          p.hfr > 0 ? (
            <circle key={i} cx={x(p.pos)} cy={y(p.hfr)} r={4} className={styles.accFill} />
          ) : (
            <circle key={i} cx={x(p.pos)} cy={y0 - 6} r={4} className={styles.mark} />
          ),
        )}
        <text x={x0} y={y0 + 24} className={styles.small}>
          {lo.toLocaleString()}
        </text>
        <text x={x1} y={y0 + 24} textAnchor="end" className={styles.small}>
          {hi.toLocaleString()}
        </text>
      </svg>
    </div>
  )
}

export function SharpCap({ readout, subStep }: { readout: ReadoutView | null; subStep: string | null }) {
  const stars = useMemo(() => polarStars(), [])
  const align = readout?.kind === 'polar-offset'
  // 극 찾기 동안은 별 자리를 천천히 돌려 "적경축을 돌리는 중"을 흉내 (각도는 상태 줄 문구에)
  const rot = align ? 90 : 0
  const a = (rot * Math.PI) / 180
  const tx = FX
  const ty = FY - 10
  const x = readout?.values.xPx ?? 0
  const y = readout?.values.yPx ?? 0
  const k = 2.4
  const px = clamp(tx + x * k, 120, VW - 120)
  const py = clamp(ty + y * k, 90, FY + 230) // 중앙 정보(등급 말)와 겹치지 않게
  return (
    <g>
      {stars.map((p, i) => {
        const sx = FX + (p.u * Math.cos(a) - p.v * Math.sin(a)) * 820
        const sy = 520 + (p.u * Math.sin(a) + p.v * Math.cos(a)) * 520
        return <circle key={i} className={styles.star} cx={sx} cy={sy} r={p.r * 1.3} opacity={p.b ? 0.85 : 0.45} />
      })}
      {!align && subStep === 'find' && stars.slice(0, 18).map((p, i) => (
        <circle key={`f${i}`} cx={FX + p.u * 820} cy={520 + p.v * 520} r={13} className={styles.mark} />
      ))}
      {align && (
        <g>
          <circle cx={tx} cy={ty} r={30} className={styles.target} />
          <circle cx={tx} cy={ty} r={4} className={styles.targetDot} />
          {Math.hypot(px - tx, py - ty) > 34 && <line x1={px} y1={py} x2={tx} y2={ty} className={styles.dash} />}
          <circle cx={px} cy={py} r={11} className={styles.accFill} />
          <text x={tx + 44} y={ty + 6} className={styles.label}>목표 위치</text>
        </g>
      )}
      <text x={FX} y={52} textAnchor="middle" className={styles.label}>SharpCap 화면 (모의)</text>
    </g>
  )
}

export function GuideImage({ field, readout, data, subStep }: { field: React.ReactNode; readout: ReadoutView | null; data: unknown; subStep: string | null }) {
  // 캘리브레이션: 서쪽으로 n걸음 → 북쪽으로 n걸음, 별이 움직인 길을 그린다
  let star: { x: number; y: number } | null = null
  let path: string | null = null
  if (readout?.kind === 'steps') {
    const n = readout.values.step ?? 0
    const north = readout.values.north === 1 // 방향은 값으로 받는다 (표시 문장과 그림을 묶지 않음)
    const pts: string[] = []
    const west = north ? 12 : n
    for (let i = 0; i <= west; i++) pts.push(`${FX + i * 18},${FY - i * 1.2}`)
    if (north) for (let i = 1; i <= n; i++) pts.push(`${FX + 216 + i * 1.3},${FY - 14 - i * 17}`)
    path = pts.length > 1 ? 'M' + pts.join(' L') : null
    star = north ? { x: FX + 216 + n * 1.3, y: FY - 14 - n * 17 } : { x: FX + n * 18, y: FY - n * 1.2 }
  } else if ((readout && ['star', 'calibration', 'guiding'].includes(readout.kind)) || subStep === 'settle' || subStep === 'measure') star = { x: FX, y: FY }
  const guiding = readout?.kind === 'guiding' || hasSeries(data)
  return (
    <g>
      {field}
      {path && <path d={path} className={styles.trail} />}
      {star && (
        <g className={guiding ? styles.jitter : undefined}>
          <circle cx={star.x} cy={star.y} r={30} className={styles.star} opacity={0.12} />
          <circle cx={star.x} cy={star.y} r={14} className={styles.star} opacity={0.35} />
          <circle cx={star.x} cy={star.y} r={7} className={styles.star} />
          <rect x={star.x - 40} y={star.y - 40} width={80} height={80} rx={4} className={styles.box} />
        </g>
      )}
      {!star && <text x={FX} y={FY} textAnchor="middle" className={styles.label}>별을 찾는 중</text>}
      <text x={FX} y={52} textAnchor="middle" className={styles.label}>가이드 카메라 (PHD2)</text>
    </g>
  )
}

function hasSeries(data: unknown): data is { series: [number, number][]; limit: number } {
  return !!data && typeof data === 'object' && Array.isArray((data as { series?: unknown }).series)
}

/** 가이딩 그래프: 가이드 별 아래, 중앙 정보 위 */
export function GuidingGraph({ data }: { data: unknown }) {
  if (!hasSeries(data) || data.series.length < 2) return null
  const W = 650
  const H = 190
  const x0 = 20
  const x1 = W - 20
  const gy = 108
  const gh = 120
  const n = 70
  const lim = data.limit || 2.4
  const s = data.series.slice(-n)
  const line = (pick: (p: [number, number]) => number) =>
    s.map((p, i) => `${i ? 'L' : 'M'}${(x0 + ((x1 - x0) * i) / (n - 1)).toFixed(1)},${(gy - (clamp(pick(p), -lim * 1.4, lim * 1.4) * gh) / 2 / (lim * 1.4)).toFixed(1)}`).join('')
  return (
    <div className={styles.graph}>
      <svg viewBox={`0 0 ${W} ${H}`}>
        <line x1={x0} y1={gy} x2={x1} y2={gy} className={styles.axis} />
        <line x1={x0} y1={gy - gh / 2.8} x2={x1} y2={gy - gh / 2.8} className={styles.limit} />
        <line x1={x0} y1={gy + gh / 2.8} x2={x1} y2={gy + gh / 2.8} className={styles.limit} />
        <path d={line((p) => p[1])} className={styles.dec} />
        <path d={line((p) => p[0])} className={styles.ra} />
        <text x={x1} y={24} textAnchor="end" className={styles.small}>
          — 적경  — 적위 · 기준 ±{lim.toFixed(2)}″
        </text>
      </svg>
    </div>
  )
}

export function SkyMap({ field, track, label }: { field: React.ReactNode; track: SlewTrack; label: string }) {
  const CX = FX
  const CY = FY + 20
  const RR = 310
  const pos = (alt: number, az: number) => {
    const rr = (RR * (90 - alt)) / 90
    const a = ((az - 90) * Math.PI) / 180
    return { x: CX + rr * Math.cos(a), y: CY + rr * Math.sin(a) }
  }
  const tgt = { alt: track.targetAlt, az: 64 }
  const from = { alt: 50, az: 203 }
  const e = 1 - Math.pow(1 - track.progress, 2.2)
  const now = { alt: from.alt + (tgt.alt - from.alt) * e, az: from.az + (tgt.az - from.az) * e }
  const trail: string[] = []
  for (let i = 0; i <= 24; i++) {
    const f = (i / 24) * track.progress
    const ee = 1 - Math.pow(1 - f, 2.2)
    const p = pos(from.alt + (tgt.alt - from.alt) * ee, from.az + (tgt.az - from.az) * ee)
    trail.push(`${p.x.toFixed(1)},${p.y.toFixed(1)}`)
  }
  const t = pos(tgt.alt, tgt.az)
  const s = pos(now.alt, now.az)
  return (
    <g>
      {field}
      <circle cx={CX} cy={CY} r={RR} className={styles.grid} />
      <circle cx={CX} cy={CY} r={(RR * 2) / 3} className={styles.grid} strokeDasharray="5 8" />
      <circle cx={CX} cy={CY} r={RR / 3} className={styles.grid} />
      <text x={CX} y={CY - RR - 14} textAnchor="middle" className={styles.label}>북</text>
      <text x={CX + RR + 16} y={CY + 6} className={styles.label}>동</text>
      <text x={CX} y={CY + RR + 30} textAnchor="middle" className={styles.label}>남</text>
      <text x={CX - RR - 16} y={CY + 6} textAnchor="end" className={styles.label}>서</text>
      <text x={CX + 8} y={CY - (RR * 2) / 3 - 8} className={styles.label}>30°</text>
      {track.progress > 0 && <path d={'M' + trail.join(' L')} className={styles.trailDim} />}
      <circle cx={t.x} cy={t.y} r={20} className={styles.acc} />
      <text x={t.x + 28} y={t.y + 6} className={styles.labelStrong}>{label}</text>
      <circle cx={s.x} cy={s.y} r={10} className={styles.accFill} />
    </g>
  )
}

export function FocusCurve({ field, data, readout }: { field: React.ReactNode; data: unknown; readout: ReadoutView | null }) {
  const d = (data ?? {}) as { points?: { pos: number; hfr: number }[]; last?: number | null }
  const pts = d.points ?? []
  const X0 = 380
  const X1 = 1300
  const Y0 = 700
  const Y1 = 300
  const lo = Math.min(...pts.map((p) => p.pos), d.last ?? Infinity)
  const hi = Math.max(...pts.map((p) => p.pos), d.last ?? -Infinity)
  const span = Number.isFinite(lo) && Number.isFinite(hi) && hi > lo ? [lo, hi] : [0, 1]
  const hmax = Math.max(4, ...pts.map((p) => p.hfr)) + 0.5
  const hmin = Math.max(0, Math.min(...pts.map((p) => p.hfr), 2) - 0.5)
  const px = (p: number) => X0 + ((X1 - X0) * (p - span[0])) / (span[1] - span[0])
  const py = (h: number) => Y0 - ((Y0 - Y1) * (Math.min(h, hmax) - hmin)) / (hmax - hmin)
  const done = readout?.kind === 'hfr' && readout.tone === 'Ok'
  const fit = done && pts.length >= 3 ? quadFit(pts.map((p) => [p.pos, p.hfr])) : null
  const best = done ? readout?.values.position ?? (fit ? -fit[1] / (2 * fit[0]) : null) : null
  let curve = ''
  if (fit) for (let t = 0; t <= 40; t++) {
    const p = span[0] + ((span[1] - span[0]) * t) / 40
    curve += `${t ? 'L' : 'M'}${px(p).toFixed(1)},${py(fit[0] * p * p + fit[1] * p + fit[2]).toFixed(1)}`
  }
  return (
    <g>
      {field}
      <line x1={X0} y1={Y0} x2={X1} y2={Y0} className={styles.grid} />
      <line x1={X0} y1={Y0} x2={X0} y2={Y1} className={styles.grid} />
      <text x={X0} y={Y0 + 32} className={styles.label}>포커서 위치</text>
      <text x={X0 - 12} y={Y1 + 6} textAnchor="end" className={styles.label}>별 크기</text>
      {d.last != null && pts.length > 0 && (
        <g>
          <line x1={px(d.last)} y1={Y0} x2={px(d.last)} y2={Y1 + 20} className={styles.trailDim} />
          <text x={px(d.last)} y={Y1 + 10} textAnchor="middle" className={styles.label}>지난번 {d.last.toLocaleString()}</text>
        </g>
      )}
      {curve && <path d={curve} className={styles.acc} opacity={0.8} />}
      {best != null && fit && (
        <g>
          <line x1={px(best)} y1={Y0} x2={px(best)} y2={py(fit[0] * best * best + fit[1] * best + fit[2])} className={styles.accDash} />
          <text x={px(best)} y={Y0 + 32} textAnchor="middle" className={styles.labelAcc}>{Math.round(best).toLocaleString()}</text>
        </g>
      )}
      {pts.map((p, i) => (
        <circle key={i} cx={px(p.pos)} cy={py(p.hfr)} r={8} className={i === pts.length - 1 && !done ? styles.accFill : styles.star} />
      ))}
      {pts.length === 0 && <text x={(X0 + X1) / 2} y={(Y0 + Y1) / 2} textAnchor="middle" className={styles.label}>포커서를 옮기는 중</text>}
    </g>
  )
}
