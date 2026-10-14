import { useEffect, useLayoutEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { clockOf, displayName, type Chart, type ChartPick } from '../plan'
import styles from './NightGraph.module.css'

/**
 * 계획 화면 "오늘 밤" 그래프 (DESIGN.md "계획", 시안 mockups/aira-plan-v3.html — 2026-10-14).
 * 가로 = 일몰~일출, 세로 = 고도(아래 변 −5° = 먼 산 바닥, 위 변 100°, 눈금은 0·30·60·90°).
 * 위쪽 띠: 정각마다 구름, 그 아래 고른 대상 이름·그림(지금 대상만 밝게, 눌러서 바꿈). 위 변 위에 지금 대상의 "총 노출 계획" 막대.
 * 지금 대상의 시작점(흰 동그라미)이나 위쪽 그림을 좌우로 끌어 촬영 시작을 옮긴다(곡선을 따라). 두 번 누르면 기본으로.
 * 대상을 바꾸면 곡선·그림·이름이 0.24초 동안 서로 바뀐다. 색은 모두 토큰(촬영용 적색 테마).
 */
export default function NightGraph({
  chart,
  onSelect,
  onStartChange,
}: {
  chart: Chart
  onSelect: (id: string) => void
  /** 시작점 옮기기 (분). null = 기본으로 */
  onStartChange: (id: string, minute: number | null) => void
}) {
  const box = useRef<HTMLDivElement>(null)
  const svg = useRef<SVGSVGElement>(null)
  const [size, setSize] = useState({ w: 1100, h: 480 })
  useLayoutEffect(() => {
    const el = box.current
    if (!el) return
    const measure = () => setSize({ w: Math.max(400, el.offsetWidth), h: Math.max(320, el.offsetHeight) })
    measure()
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // 끄는 중인 시작점 (분). 놓으면 서버에 보내고 지운다
  const [drag, setDrag] = useState<{ id: string; minute: number } | null>(null)
  const [moonTip, setMoonTip] = useState<{ x: number; y: number; text: string } | null>(null)

  const { w: W, h: H } = size
  const L = 44
  const R = W - 4
  const TOP = 158
  const BOT = H - 46
  const CL = 14 // 구름 줄
  const EM = 92 // 대상 그림 첫 줄 (이름은 그 위)
  const ROW = 46
  const span = chart.span
  const x = (t: number) => L + (Math.max(0, Math.min(span, t)) / span) * (R - L)
  const y = (a: number) => BOT - ((Math.max(-5, Math.min(100, a)) + 5) / 105) * (BOT - TOP)

  const current = chart.picks.find((p) => p.id === chart.current) ?? null

  // 끌기: 시작점 손잡이나 지금 대상 그림을 누른 채 좌우로
  const minuteAt = (clientX: number) => {
    const r = svg.current!.getBoundingClientRect()
    const sx = ((clientX - r.left) * W) / r.width
    return ((sx - L) / (R - L)) * span
  }
  const beginDrag = (p: ChartPick, e: ReactPointerEvent) => {
    if (!p.window) return
    e.preventDefault()
    const w = p.window
    const startX = e.clientX
    // 마지막 위치는 화면을 다시 그리기 전에 놓아도 잃지 않게 여기에 둔다
    const last = { minute: w.start, moved: false }
    setDrag({ id: p.id, minute: w.start })
    const move = (ev: PointerEvent) => {
      last.minute = Math.round(Math.max(w.dragFrom, Math.min(w.dragTo, minuteAt(ev.clientX))))
      last.moved ||= Math.abs(ev.clientX - startX) > 3
      setDrag({ id: p.id, minute: last.minute })
    }
    const done = (commit: boolean) => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', up)
      window.removeEventListener('pointercancel', cancel)
      setDrag(null)
      if (commit && last.moved && Math.round(last.minute) !== Math.round(w.start)) onStartChange(p.id, last.minute)
    }
    const up = () => done(true)
    // 터치가 취소되면(스크롤 등) 옮기지 않고 끝 — 남은 이벤트가 계속 끌지 않게
    const cancel = () => done(false)
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', up)
    window.addEventListener('pointercancel', cancel)
  }
  useEffect(() => setDrag(null), [chart])

  const alt = (p: ChartPick, t: number) => altitudeOn(p.track, t)
  const startOf = (p: ChartPick) => (drag?.id === p.id ? drag.minute : p.window?.start ?? null)

  // 대상 이름표 자리: 촬영 시작점(못 찍으면 봉우리)에서 위로. 가까우면 아래 줄
  const tags = layoutTags(chart.picks, (p) => {
    const at = startOf(p) ?? p.peak?.at ?? 0
    return { x: x(at), y: y(Math.max(0, alt(p, at))) }
  })

  const nowIn = chart.now >= 0 && chart.now <= span
  const ridge = (h0: number, amp: number, f1: number, f2: number, ph: number) => {
    let d = `M${L},${y(-5)}`
    for (let xx = L; xx <= R; xx += 6) {
      const u = (xx - L) / (R - L)
      d += `L${xx},${y(h0 + amp * (0.55 * Math.sin(u * f1 + ph) + 0.3 * Math.sin(u * f2 + ph * 2.3) + 0.15 * Math.sin(u * 47 + ph)))}`
    }
    return `${d}L${R},${y(-5)}Z`
  }
  const moonR = Math.min(10, (x(30) - x(0)) * 0.36)

  return (
    <div className={styles.wrap} ref={box}>
      <svg ref={svg} className={styles.graph} viewBox={`0 0 ${W} ${H}`} role="img" aria-label="오늘 밤 대상 고도 그래프">
        <defs>
          <linearGradient id="ng-dusk" x1="0" x2="1">
            <stop offset="0" className={styles.twilightStop} stopOpacity=".75" />
            <stop offset="1" className={styles.twilightStop} stopOpacity="0" />
          </linearGradient>
          <linearGradient id="ng-dawn" x1="1" x2="0">
            <stop offset="0" className={styles.twilightStop} stopOpacity=".75" />
            <stop offset="1" className={styles.twilightStop} stopOpacity="0" />
          </linearGradient>
          <clipPath id="ng-plot">
            <rect x={L} y={TOP} width={R - L} height={BOT - TOP} />
          </clipPath>
        </defs>

        {/* 천문 박명: 어두워지기 전 · 밝아진 뒤 */}
        <rect x={x(0)} y={TOP} width={x(chart.dark.start) - x(0)} height={BOT - TOP} fill="url(#ng-dusk)" />
        <rect x={x(chart.dark.end)} y={TOP} width={x(span) - x(chart.dark.end)} height={BOT - TOP} fill="url(#ng-dawn)" />
        <text x={x(chart.dark.start) - 6} y={BOT - 8} textAnchor="end" className={styles.small}>천문 박명 {chart.dark.startLabel}</text>
        <text x={x(chart.dark.end) + 6} y={BOT - 8} className={styles.small}>{chart.dark.endLabel} 천문 박명</text>

        {/* 고도 눈금 (위 변 100°는 적지 않음) */}
        {[0, 30, 60, 90].map((a) => (
          <g key={a}>
            <line x1={L} x2={R} y1={y(a)} y2={y(a)} className={a === chart.minAltitude ? styles.guide : styles.grid} />
            <text x={L - 8} y={y(a) + 4} textAnchor="end">{a}°</text>
          </g>
        ))}
        <line x1={L} x2={R} y1={TOP} y2={TOP} className={styles.edge} />
        <line x1={L} x2={R} y1={BOT} y2={BOT} className={styles.edge} />
        <text x={L - 8} y={TOP + 4} textAnchor="end" className={styles.strong}>고도</text>

        {/* 바닥 0~10°: 먼 산 두 겹 (꾸밈 — 대상과 상관없이 고정) */}
        <path d={ridge(6.5, 3.2, 9, 23, 0.7)} className={styles.ridgeFar} />
        <path d={ridge(3.5, 2.6, 13, 31, 2.1)} className={styles.ridgeNear} />

        {/* 구름: 정각마다 */}
        <text x={L - 8} y={CL + 4} textAnchor="end">구름</text>
        {chart.clouds.map((c) => (
          <CloudIcon key={c.at} cx={x(c.at)} cy={CL} pct={c.cover} />
        ))}

        {/* 달: 30분마다 위상 그림 (그래프 밖은 잘림), 가리키면 그때 고도 */}
        <g clipPath="url(#ng-plot)">
          {chart.moon.samples.map((m) => (
            <g
              key={m.at}
              className={styles.moon}
              onMouseEnter={() => setMoonTip({ x: x(m.at), y: y(m.alt) - moonR - 6, text: `달 ${m.label} · 고도 ${Math.round(m.alt)}°` })}
              onMouseLeave={() => setMoonTip(null)}
            >
              <MoonIcon cx={x(m.at)} cy={y(m.alt)} r={moonR} k={chart.moon.illumination / 100} waxing={chart.moon.waxing} />
            </g>
          ))}
        </g>

        {/* 고른 대상: 뒤(촬영 범위) → 가운데(곡선) → 앞(이름표). 대상마다 켜진 모습·꺼진 모습을 함께 그려 바꿀 때 서로 바뀐다 */}
        {chart.picks.map((p) => {
          const on = p.id === chart.current
          const w = p.window
          return (
            <g key={`back-${p.id}`} className={styles.tk} data-on={on}>
              {w && (
                <g className={styles.on}>
                  <rect className={styles.range} x={x(w.start)} y={TOP} width={Math.max(0, x(w.end) - x(w.start))} height={BOT - TOP} />
                  {w.clipped ? (
                    <line className={styles.limit} x1={x(w.limit)} x2={x(w.limit)} y1={TOP} y2={BOT} />
                  ) : (
                    <rect className={styles.rangeFade} x={x(w.end)} y={TOP} width={Math.max(0, x(w.end + 25) - x(w.end))} height={BOT - TOP} />
                  )}
                </g>
              )}
            </g>
          )
        })}
        {chart.picks.map((p) => {
          const on = p.id === chart.current
          const w = p.window
          const path = trackPath(p.track, x, y)
          return (
            <g key={`mid-${p.id}`} className={styles.tk} data-on={on}>
              <path className={`${styles.other} ${styles.off}`} d={path} />
              <g className={styles.on}>
                <path className={styles.cur} d={path} />
                {p.peak && (
                  <>
                    <line className={styles.peakLine} x1={x(p.peak.at)} x2={x(p.peak.at)} y1={y(p.peak.alt) + 6} y2={BOT} />
                    <circle className={styles.peakDot} cx={x(p.peak.at)} cy={y(p.peak.alt)} r={4} />
                    <text x={x(p.peak.at)} y={y(p.peak.alt) - 10} textAnchor="middle" className={styles.strong}>{Math.round(p.peak.alt)}°</text>
                    <text x={x(p.peak.at) + 5} y={BOT - 6} className={styles.accentText}>{p.peak.label}</text>
                  </>
                )}
                {w && (
                  <>
                    <path className={styles.lit} d={trackPath(p.track.filter(([t]) => t >= w.start && t <= w.end), x, y, [w.start, alt(p, w.start)], [w.end, alt(p, w.end)])} />
                    {w.transit !== null && (
                      <>
                        <line className={styles.flip} x1={x(w.transit)} x2={x(w.transit)} y1={TOP} y2={BOT} />
                        <text x={x(w.transit) + 6} y={TOP + 18} className={styles.warnText}>자오선 반전 {w.transitLabel} · 약 5분 멈춤</text>
                      </>
                    )}
                    <PlanBar x1={x(w.start)} x2={x(w.end)} fadeTo={w.clipped ? null : x(w.end + 25)} top={TOP} id={p.id} />
                  </>
                )}
              </g>
            </g>
          )
        })}
        {tags.map(({ p, x: tx, y: ty, row, w: tw }) => {
          const on = p.id === chart.current
          const ey = EM + row * ROW
          return (
            <g
              key={`tag-${p.id}`}
              className={`${styles.tk} ${styles.tag}`}
              data-on={on}
              data-drag={drag?.id === p.id}
              onPointerDown={on ? (e) => beginDrag(p, e) : undefined}
              onClick={on ? undefined : () => onSelect(p.id)}
            >
              <g className={styles.dim}>
                <line className={`${styles.stem} ${styles.on}`} x1={tx} x2={tx} y1={ty - 6} y2={ey + 13} />
                <circle className={`${styles.tagDotOff} ${styles.off}`} cx={tx} cy={ty} r={4} />
                <circle className={`${styles.tagDotOn} ${styles.on}`} cx={tx} cy={ty} r={5} />
                <rect x={tx - tw / 2} y={ey - 34} width={tw} height={48} fill="transparent" />
                <Emblem type={p.type} cx={tx} cy={ey} r={13} />
                <text x={tx} y={ey - 19} textAnchor="middle" className={`${styles.tagName} ${styles.off}`}>{displayName(p)}</text>
                <text x={tx} y={ey - 19} textAnchor="middle" className={`${styles.tagNameOn} ${styles.on}`}>{displayName(p)}</text>
              </g>
              <title>{on ? '끌어서 촬영 시작 옮기기' : `${displayName(p)}로 바꾸기`}</title>
            </g>
          )
        })}

        {/* 지금 대상의 시작점 손잡이 */}
        {current?.window && (() => {
          const at = startOf(current)!
          const hx = x(at)
          const hy = y(alt(current, at))
          const label = `${clockOf(chart, at)} · ${Math.round(alt(current, at))}°`
          return (
            <g
              className={styles.handle}
              data-drag={drag?.id === current.id}
              onPointerDown={(e) => beginDrag(current, e)}
              onDoubleClick={() => onStartChange(current.id, null)}
            >
              <circle cx={hx} cy={hy} r={16} fill="transparent" />
              <circle className={styles.knob} cx={hx} cy={hy} r={7} />
              <g className={styles.handleLabel}>
                <rect x={hx + 12} y={hy - 30} width={label.length * 7.6 + 16} height={22} rx={6} />
                <text x={hx + 20} y={hy - 15}>{label}</text>
              </g>
              <title>끌어서 촬영 시작 옮기기 · 두 번 누르면 기본으로</title>
            </g>
          )
        })()}

        {/* 지금: 위·아래 변에 강조점, 시각은 아래 줄. 일몰 전이면 왼쪽 끝에 글자만 */}
        {nowIn ? (
          <>
            <line className={styles.now} x1={x(chart.now)} x2={x(chart.now)} y1={TOP} y2={BOT} />
            {[TOP, BOT].map((cy) => (
              <g key={cy}>
                <circle className={styles.nowGlow} cx={x(chart.now)} cy={cy} r={10} />
                <circle className={styles.nowDot} cx={x(chart.now)} cy={cy} r={5} />
              </g>
            ))}
            <text x={x(chart.now)} y={BOT + 24} textAnchor="middle" className={styles.strong}>지금 {chart.nowLabel}</text>
          </>
        ) : chart.now < 0 ? (
          <text x={x(0) + 46} y={BOT + 24} className={styles.strong}>지금 {chart.nowLabel} · 일몰 전</text>
        ) : null}

        {/* 시각: 아래 변 바로 아래 줄, 그 아래 일몰·일출 */}
        <text x={x(0)} y={BOT + 24}>{chart.sunsetLabel}</text>
        <text x={x(0)} y={BOT + 41}>일몰</text>
        <text x={x(span)} y={BOT + 24} textAnchor="end">{chart.sunriseLabel}</text>
        <text x={x(span)} y={BOT + 41} textAnchor="end">일출</text>
        {chart.hours
          .filter((h) => h.at > 30 && h.at < span - 30 && Number(h.label.replace('시', '')) % 2 === 0)
          .filter((h) => !nowIn || Math.abs(x(h.at) - x(chart.now)) >= 62)
          .filter((h) => chart.now >= 0 || x(h.at) > x(0) + 200)
          .map((h) => (
            <text key={h.at} x={x(h.at)} y={BOT + 24} textAnchor="middle">{h.label}</text>
          ))}
      </svg>
      {moonTip && (
        <div className={styles.tip} style={{ left: moonTip.x, top: moonTip.y }}>
          {moonTip.text}
        </div>
      )}
    </div>
  )
}

/** 시각 t(분)의 고도: 5분 간격 자료 사이를 잇는다 */
function altitudeOn(track: [number, number][], t: number) {
  if (track.length === 0) return 0
  if (t <= track[0][0]) return track[0][1]
  for (let i = 1; i < track.length; i++) {
    const [t1, a1] = track[i]
    if (t <= t1) {
      const [t0, a0] = track[i - 1]
      return a0 + ((a1 - a0) * (t - t0)) / (t1 - t0 || 1)
    }
  }
  return track[track.length - 1][1]
}

/** 지평선 위 부분만 잇는 곡선 (아래로 내려가면 끊는다) */
function trackPath(
  track: [number, number][],
  x: (t: number) => number,
  y: (a: number) => number,
  first?: [number, number],
  last?: [number, number],
) {
  const pts = [...(first ? [first] : []), ...track, ...(last ? [last] : [])]
  let d = ''
  let up = false
  for (const [t, a] of pts) {
    if (a < 0) {
      up = false
      continue
    }
    d += `${up ? 'L' : 'M'}${x(t).toFixed(1)},${y(a).toFixed(1)}`
    up = true
  }
  return d
}

/** 이름표 줄 배치: 왼쪽부터, 앞 이름표와 겹치면 아래 줄 */
function layoutTags(picks: ChartPick[], at: (p: ChartPick) => { x: number; y: number }) {
  const rowEnd = [-1e9, -1e9]
  return picks
    .map((p) => ({ p, ...at(p), w: displayName(p).length * 13.5 }))
    .sort((a, b) => a.x - b.x)
    .map((t) => {
      const left = t.x - t.w / 2
      const row = left >= rowEnd[0] + 12 ? 0 : left >= rowEnd[1] + 12 ? 1 : 0
      rowEnd[row] = t.x + t.w / 2
      return { ...t, row }
    })
}

/** 위 변에 겹친 "총 노출 계획" 막대. 다 채우는 밤이면 끝이 흐려진다 */
function PlanBar({ x1, x2, fadeTo, top, id }: { x1: number; x2: number; fadeTo: number | null; top: number; id: string }) {
  const bw = x2 - x1
  const gid = `ng-bar-${id.replace(/[^\w-]/g, '')}`
  return (
    <>
      {fadeTo !== null && (
        <defs>
          <linearGradient id={gid} gradientUnits="userSpaceOnUse" x1={x2} x2={fadeTo}>
            <stop offset="0" className={styles.barStop} />
            <stop offset="1" className={styles.barStop} stopOpacity="0" />
          </linearGradient>
        </defs>
      )}
      <rect
        className={styles.bar}
        x={x1}
        y={top - 3}
        width={Math.max(0, (fadeTo ?? x2) - x1)}
        height={6}
        rx={3}
        style={fadeTo !== null ? { fill: `url(#${gid})` } : undefined}
      />
      <text x={bw > 110 ? x1 + bw / 2 : x1 + 18} y={top - 10} textAnchor={bw > 110 ? 'middle' : 'start'} className={styles.barLabel}>
        총 노출 계획
      </text>
    </>
  )
}

function CloudIcon({ cx, cy, pct }: { cx: number; cy: number; pct: number }) {
  if (pct < 10) return <circle className={styles.clear} cx={cx} cy={cy} r={4} />
  const o = pct < 40 ? 0.45 : pct < 70 ? 0.75 : 0.95
  const sc = pct < 40 ? 0.7 : 1
  return (
    <>
      <g transform={`translate(${cx} ${cy}) scale(${sc})`} opacity={o}>
        <path className={styles.cloud} d="M-11,5 h22 a6,6 0 0 0 0,-11 a8,8 0 0 0 -15,-2 a6,6 0 0 0 -7,13 z" />
      </g>
      {pct >= 40 && (
        <text x={cx} y={cy + 20} textAnchor="middle" className={styles.tiny}>{pct}%</text>
      )}
    </>
  )
}

/** 달 위상: 어두운 원 + 밝은 부분 (k = 밝은 비율, 차는 달은 오른쪽이 밝음) */
export function MoonIcon({ cx, cy, r, k, waxing }: { cx: number; cy: number; r: number; k: number; waxing: boolean }) {
  const rx = Math.abs(1 - 2 * k) * r
  const lit = `M0,${-r} A${r},${r} 0 0 1 0,${r} A${rx.toFixed(2)},${r} 0 0 ${k < 0.5 ? 0 : 1} 0,${-r}Z`
  return (
    <g transform={`translate(${cx} ${cy})${waxing ? '' : ' scale(-1 1)'}`}>
      <circle r={r} className={styles.moonDark} />
      <path d={lit} className={styles.moonLit} />
    </g>
  )
}

/** 대상 종류 그림: 은하(기운 타원) · 성단(별 여러 개) · 성운(번진 타원) */
export function Emblem({ type, cx, cy, r }: { type: string; cx: number; cy: number; r: number }) {
  if (/은하/.test(type))
    return (
      <g transform={`translate(${cx} ${cy}) rotate(-30)`}>
        <ellipse rx={r} ry={r * 0.4} className={styles.galaxy} />
        <ellipse rx={r * 0.55} ry={r * 0.22} className={styles.galaxyCore} />
        <circle r={2.6} className={styles.starCore} />
      </g>
    )
  if (/성단/.test(type) && !/성운/.test(type)) {
    const pts = [[0, 0], [-8, -5], [7, -7], [10, 4], [-4, 8], [-11, 3], [3, -12]]
    return (
      <g>
        {pts.map(([dx, dy], i) => (
          <g key={i}>
            <circle cx={cx + dx} cy={cy + dy} r={6} className={styles.clusterGlow} />
            <circle cx={cx + dx} cy={cy + dy} r={i < 3 ? 2.6 : 1.8} className={styles.cluster} />
          </g>
        ))}
      </g>
    )
  }
  return (
    <g transform={`translate(${cx} ${cy})`}>
      <ellipse rx={r} ry={r * 0.7} className={styles.nebula} />
      <ellipse cx={-5} cy={3} rx={r * 0.6} ry={r * 0.45} className={styles.nebulaInner} />
      <circle r={2} className={styles.starCore} />
    </g>
  )
}
