import { useEffect, useMemo, useRef, useState } from 'react'
import type { ReadoutView } from '../prepare'
import styles from './LiveView.module.css'
import { scaleOf } from '../frame'
import GlowFrame from './GlowFrame'
import { slotOf } from './WindowView'
import { fitInside, type Box } from './liveViewMath'
import type { PhotoBox } from './FrameRotate'
import { FX, FY, galaxy, rng, starfield } from './liveSimFields'

/** 하늘 화면(LiveView)의 사진들: 히스토그램·실제 사진·사진 틀·솔빙 사진·시험 사진·9칸 (큰 파일 분리 2026-10-16 — 옮기기만) */

/**
 * 시험 사진 히스토그램 (2026-10-09 사용자 요청): 사진 위 오른쪽 세로 가운데. N.I.N.A.처럼 가로 = 밝기 0~최대(선형), 세로 = 픽셀 수.
 * 배경 산의 위치로 노출이 맞는지 본다 — 산이 왼쪽에 붙으면 어둡고, 오른쪽 끝에 몰리면 포화
 */
export function Histogram({ data }: { data: unknown }) {
  const d = data as { histogram?: number[]; background?: number } | null
  const h = d?.histogram
  if (!h || h.length < 2) return null
  const W = 320
  const H = 168
  const x0 = 16
  const x1 = W - 16
  const y0 = 128 // 바닥
  const top = 40
  const max = Math.max(...h)
  if (max <= 0) return null
  const x = (i: number) => x0 + ((x1 - x0) * i) / (h.length - 1)
  const y = (c: number) => y0 - ((y0 - top) * c) / max
  const area = `M${x0},${y0}` + h.map((c, i) => `L${x(i).toFixed(1)},${y(c).toFixed(1)}`).join('') + `L${x1},${y0}Z`
  const total = h.reduce((a, b) => a + b, 0)
  const full = total > 0 ? h[h.length - 1] / total : 0
  const bg = d?.background
  return (
    <div className={styles.hist}>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="시험 사진 히스토그램">
        <text x={x0} y={24} className={styles.small}>
          히스토그램
        </text>
        {bg !== undefined && (
          <text x={x1} y={24} textAnchor="end" className={styles.small}>
            배경 {Math.round(bg * 100)}%{full >= 0.01 ? ` · 포화 ${Math.round(full * 100)}%` : ''}
          </text>
        )}
        {[0.25, 0.5, 0.75].map((f) => (
          <line key={f} x1={x0 + (x1 - x0) * f} y1={top - 6} x2={x0 + (x1 - x0) * f} y2={y0} className={styles.limit} />
        ))}
        <path d={area} className={styles.histArea} />
        <line x1={x0} y1={y0} x2={x1} y2={y0} className={styles.axis} />
        {bg !== undefined && <line x1={x0 + (x1 - x0) * bg} y1={top - 6} x2={x0 + (x1 - x0) * bg} y2={y0} className={styles.histBg} />}
        <text x={x0} y={y0 + 24} className={styles.small}>
          0
        </text>
        <text x={x1} y={y0 + 24} textAnchor="end" className={styles.small}>
          최대
        </text>
      </svg>
    </div>
  )
}

/**
 * 실제 이미지. refresh(창 캡처·가이드 사진·자동초점 사진)면 1초마다 서버에 묻되, **사진이 바뀌었을 때만** 받는다 —
 * 지난번 표지(ETag)를 보내 같으면 서버가 304로 아무것도 보내지 않는다 (2026-10-11 Gemini 리뷰: 같은 사진을 1초마다 통째로 받아 다시 그리던 것).
 * 창이 가려져 있으면 묻지 않고, 다시 보이면 그때 한 번 받는다 (CX-24-04 — 시험 사진처럼 한 번만 받는 사진도).
 * 표지는 주소별로 기억한다 — 상태 시각(stamp)이 바뀌어도 새로 받지 않게 (CX-24-07). 다음 이미지를 다 받은 뒤에 바꾸고(깜빡이지 않게), 받기 전·실패면 하늘색 바탕만
 */
export function RealImage({
  url,
  refresh,
  stamp,
  onOpen,
  onFit,
}: {
  url: string
  refresh: boolean
  stamp: string
  onOpen?: () => void
  onFit?: (box: PhotoBox | null) => void
}) {
  const [shown, setShown] = useState<string | null>(null)
  const tags = useRef(new Map<string, string>()) // 받은 주소 → ETag
  const objectUrl = useRef<string | null>(null)
  // 실시간 사진은 상태 시각과 상관없이 같은 주소를 계속 본다 — stamp는 한 번 받는 사진에만
  const src = refresh ? url : `${url}${url.includes('?') ? '&' : '?'}t=${encodeURIComponent(stamp)}`
  useEffect(() => {
    let alive = true
    let timer = 0
    let pending = false // 가려져 있어서 미룬 받기
    const load = async () => {
      if (document.hidden) {
        pending = true
        return
      }
      try {
        const tag = tags.current.get(src)
        const res = await fetch(src, { cache: 'no-store', headers: tag ? { 'If-None-Match': tag } : {} })
        if (res.status === 200) {
          const etag = res.headers.get('ETag')
          if (etag) tags.current.set(src, etag)
          const blobUrl = URL.createObjectURL(await res.blob())
          // 다 읽은 뒤에 바꾼다 (깜빡이지 않게)
          const img = new Image()
          img.src = blobUrl
          await img.decode().catch(() => undefined)
          if (!alive) {
            URL.revokeObjectURL(blobUrl)
            return
          }
          const old = objectUrl.current
          objectUrl.current = blobUrl
          setShown(blobUrl)
          if (old) window.setTimeout(() => URL.revokeObjectURL(old), 1000)
        }
      } catch {
        /* 받지 못함 — 다음에 다시 */
      }
      if (alive && refresh) timer = window.setTimeout(load, 1000)
    }
    const onVisible = () => {
      if (!document.hidden && pending) {
        pending = false
        void load()
      }
    }
    document.addEventListener('visibilitychange', onVisible)
    void load()
    return () => {
      alive = false
      clearTimeout(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [src, refresh])
  // 보여 주던 사진은 다음 사진으로 바뀔 때(위) 또는 사라질 때만 놓는다 — 주소가 바뀌는 동안 빈 화면이 되지 않게
  useEffect(
    () => () => {
      if (objectUrl.current) URL.revokeObjectURL(objectUrl.current)
    },
    [],
  )
  return shown ? <FramedImage src={shown} onOpen={onOpen} onFit={onFit} /> : null
}

/**
 * 사진을 SharpCap 영상과 같은 자리에 (2026-10-11 사용자 요청 — 사진이 화면을 위아래로 다 덮어 구석 태양까지 가렸다):
 * 같은 화면의 글자·버튼(data-occludes) 위아래를 피한 띠(WindowView slotOf) 안에 비율을 지켜 가운데, 둘레에 빛 테두리.
 * onOpen이 있으면 누를 수 있다 (촬영 화면: 전체 화면으로)
 */
export function FramedImage({ src, onOpen, onFit }: { src: string; onOpen?: () => void; onFit?: (box: PhotoBox | null) => void }) {
  const ref = useRef<HTMLDivElement>(null)
  const [natural, setNatural] = useState<{ w: number; h: number } | null>(null)
  const box = useSlotBox(ref)
  const fit = box && natural ? fitInside(natural, box) : null
  // 사진 자리를 알린다 (구도 회전 틀) — 바뀔 때만, 사라지면 null
  const sent = useRef('')
  useEffect(() => {
    const key = JSON.stringify(fit)
    if (!onFit || key === sent.current) return
    sent.current = key
    onFit(fit)
  }, [fit, onFit])
  useEffect(() => () => onFit?.(null), [onFit])
  return (
    <div ref={ref} className={styles.slot}>
      <img
        className={styles.framed}
        src={src}
        alt=""
        data-open={!!onOpen}
        style={fit ?? { visibility: 'hidden' }}
        onLoad={(e) => setNatural({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })}
        onClick={onOpen}
      />
      {fit && <GlowFrame style={{ position: 'absolute', ...fit }} />}
    </div>
  )
}

/**
 * 사진이 놓일 띠(글자·버튼 위아래를 피한 자리, WindowView slotOf)를 틀 좌표로. 글자 칸 높이가 바뀌면(상태 문장 등) 자리도 바뀐다 — 자주 다시 잰다
 */
function useSlotBox(ref: React.RefObject<HTMLDivElement | null>) {
  const [box, setBox] = useState<Box | null>(null)
  useEffect(() => {
    const measure = () => {
      const el = ref.current
      if (!el) return
      const at = slotOf(el)
      const b = el.getBoundingClientRect()
      const s = scaleOf(el)
      const next = at ? { x: (at.x - b.left) / s, y: (at.y - b.top) / s, w: at.w / s, h: at.h / s } : null
      setBox((prev) => (JSON.stringify(prev) === JSON.stringify(next) ? prev : next))
    }
    measure()
    const t = window.setInterval(measure, 300)
    return () => clearInterval(t)
  }, [ref])
  return box
}

const TILE_NAMES = ['왼쪽 위', '위', '오른쪽 위', '왼쪽', '가운데', '오른쪽', '왼쪽 아래', '아래', '오른쪽 아래']

/**
 * 실제 장비의 9칸 확대 보기 (2026-10-16): 서버가 시험 사진에서 잘라 둔 조각(네 귀퉁이 · 네 변 가운데 · 한가운데, 원본 화소 그대로)을
 * 사진 자리(띠) 안에 3×3 정사각형으로. 칸마다 자리 이름과 그 칸 별의 크기·길쭉함(측정값 tile{k}Hfd·tile{k}Ecc·tile{k}Stars) —
 * 귀퉁이 별이 한쪽만 크거나 길쭉하면 기울어짐, 고르게 크면 백포커스·상 굽음. 둘레에 사진 틀과 같은 빛 테두리
 */
export function TileGrid({ stamp, values }: { stamp: string; values: Record<string, number> | undefined }) {
  const ref = useRef<HTMLDivElement>(null)
  const box = useSlotBox(ref)
  const gap = 10
  const cell = box ? Math.floor(Math.min((box.w - 2 * gap) / 3, (box.h - 2 * gap) / 3)) : 0
  const size = cell * 3 + gap * 2
  const at = box && cell >= 60 ? { left: box.x + (box.w - size) / 2, top: box.y + (box.h - size) / 2, width: size, height: size } : null
  return (
    <div ref={ref} className={styles.slot}>
      {at && (
        <>
          <div className={styles.tiles} style={{ ...at, gridTemplateColumns: `repeat(3, ${cell}px)`, gap }}>
            {TILE_NAMES.map((name, k) => {
              const hfd = values?.[`tile${k}Hfd`]
              const ecc = values?.[`tile${k}Ecc`]
              const stars = values?.[`tile${k}Stars`]
              return (
                <figure key={name} className={styles.tile}>
                  <img src={`/api/prepare/live/test-tile-${k}?t=${encodeURIComponent(stamp)}`} alt="" />
                  <figcaption>
                    <b>{name}</b>
                    {hfd !== undefined && (
                      <span>
                        별 크기 {hfd.toFixed(1)}
                        {ecc !== undefined ? ` · 길쭉함 ${ecc.toFixed(2)}` : ''}
                      </span>
                    )}
                    {stars !== undefined && stars < 5 && <span>별 {stars}개 — 판정에 모자람</span>}
                  </figcaption>
                </figure>
              )
            })}
          </div>
          <GlowFrame style={{ position: 'absolute', ...at }} />
        </>
      )}
    </div>
  )
}

export function SolvedPhoto({ readout, solving }: { readout: ReadoutView | null; solving: boolean }) {
  const err = readout?.kind === 'center-error' ? readout.values.errorArcmin ?? 0 : null
  const off = err === null ? { x: 300, y: -170 } : { x: Math.min(err, 14) * 24, y: -Math.min(err, 14) * 13.7 }
  const field = useMemo(() => starfield(620, 31 + Math.round(off.x)), [off.x])
  const cx = FX + off.x
  const cy = FY + off.y
  return (
    <g>
      {field}
      {galaxy(cx, cy, 2.2)}
      <line x1={FX - 40} y1={FY} x2={FX + 40} y2={FY} className={styles.cross} />
      <line x1={FX} y1={FY - 40} x2={FX} y2={FY + 40} className={styles.cross} />
      <circle cx={cx} cy={cy} r={42} className={styles.acc} />
      <text x={FX} y={FY + 300} textAnchor="middle" className={styles.label}>
        {solving ? '사진에서 위치를 계산하는 중' : '+ 화면 가운데 · ○ 대상'}
      </text>
    </g>
  )
}

export function TestPhoto({ field }: { field: React.ReactNode }) {
  return (
    <g>
      {field}
      {galaxy(FX, FY, 1.8)}
    </g>
  )
}

/** 9칸 확대 보기: 가운데 + 네 귀퉁이·네 변 (귀퉁이 별이 길쭉하면 기울어짐·백포커스) */
export function NineGrid() {
  const CW = 180
  const G = 12
  const X0 = FX - (CW * 1.5 + G)
  const Y0 = FY - (CW * 1.5 + G) + 70 // 안내 글 아래, 중앙 정보 위
  const names = ['왼쪽 위', '위', '오른쪽 위', '왼쪽', '가운데', '오른쪽', '왼쪽 아래', '아래', '오른쪽 아래']
  return (
    <g>
      {names.map((n, k) => {
        const cx = X0 + (k % 3) * (CW + G)
        const cy = Y0 + Math.floor(k / 3) * (CW + G)
        const r = rng(300 + k)
        return (
          <g key={n}>
            <rect x={cx} y={cy} width={CW} height={CW} rx={6} className={styles.cell} />
            {Array.from({ length: 9 }, (_, j) => {
              const m = r()
              return <circle key={j} cx={cx + 20 + r() * (CW - 40)} cy={cy + 20 + r() * (CW - 40)} r={m > 0.7 ? 7 : m > 0.35 ? 4.5 : 3} className={styles.star} />
            })}
            {k === 4 && galaxy(cx + CW / 2, cy + CW / 2, 0.8)}
            <text x={cx + 12} y={cy + CW - 12} className={styles.small}>{n}</text>
          </g>
        )
      })}
    </g>
  )
}

// ── 도우미 ─────────
