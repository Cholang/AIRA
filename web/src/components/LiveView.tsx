import { useMemo } from 'react'
import type { CurrentView } from '../prepare'
import styles from './LiveView.module.css'
import WindowView from './WindowView'
import type { PhotoBox } from './FrameRotate'
import { simPhoto } from './simPhoto'
import { FocusCurve, FocusPanel, GuideImage, GuidingGraph, SharpCap, SkyMap } from './LivePanels'
import { useSlewTrack } from './useSlewTrack'
import { FramedImage, Histogram, NineGrid, RealImage, SolvedPhoto, TestPhoto } from './LivePhoto'
import { starfield, VH, VW } from './liveSimFields'

/**
 * 하늘 화면 (DESIGN.md "하늘 화면" — 배경 전체). 서버의 live.kind에 따라 그린다:
 * none · sharpcap · guide-image · sky-map · focus-curve · solved-photo · test-photo.
 * 지금은 모의 장비라 사진 대신 측정값(readout.values)과 live.data로 그림을 흉내 낸다.
 * 실제 장비(P3)에서는 live.url의 사진·창 캡처를 이 자리에 깐다.
 */

export interface LiveExtras {
  /** 시험 사진 9칸 확대 보기 */
  grid?: boolean
}

export default function LiveView({
  current,
  extras,
  simulated = true,
  onPhoto,
  onPhotoBox,
}: {
  current: CurrentView | null
  extras?: LiveExtras
  simulated?: boolean
  /** 사진을 누르면 (촬영 화면의 전체 화면 보기). 없으면 누를 수 없다 */
  onPhoto?: () => void
  /** 센터링 사진이 놓인 자리 (구도 회전 틀을 그 위에 얹는다). 사진이 없으면 null */
  onPhotoBox?: (box: PhotoBox | null) => void
}) {
  const live = current?.live ?? null
  const kind = live?.kind ?? 'none'
  const readout = current?.center.readout ?? null
  const slew = useSlewTrack(current)

  const dim = useMemo(() => starfield(260, 5, { dim: 0.35 }), [])
  const sparse = useMemo(() => starfield(70, 11, { scale: 1.8 }), [])
  const dense = useMemo(() => starfield(900, 99, { scale: 1.1 }), [])
  // 모의: 센터링을 마치면(카메라 방향을 알면) 실장비처럼 사진 틀에 그림 사진 — 구도 회전 틀을 그 위에 얹는다
  const simAngle = simulated && kind === 'solved-photo' ? readout?.values.cameraAngle : undefined
  const simSrc = useMemo(() => (simAngle === undefined ? null : simPhoto(simAngle)), [simAngle])

  let body: React.ReactNode
  switch (kind) {
    case 'sharpcap':
      body = <SharpCap readout={readout} subStep={activeSub(current)} />
      break
    case 'guide-image':
      body = <GuideImage field={sparse} readout={readout} data={live?.data} subStep={activeSub(current)} />
      break
    case 'sky-map':
      body = <SkyMap field={dim} track={slew} label={current?.taskId === 'slew' ? '대상' : '캘리브레이션 위치'} />
      break
    case 'focus-curve':
      body = <FocusCurve field={dim} data={live?.data} readout={readout} />
      break
    case 'solved-photo':
      body = <SolvedPhoto readout={readout} solving={activeSub(current) === 'solve'} />
      break
    case 'test-photo':
      body = extras?.grid ? <NineGrid /> : <TestPhoto field={dense} />
      break
    default:
      body = <g>{dim}</g>
  }

  // 실제 장비인데 아직 사진이 없음: 가짜 별밭·하늘 바탕을 그리지 않는다 — 앱 배경(성운)이 그대로 보이게 (2026-10-10). 위치 지도 같은 그림만 바탕 없이
  if (!simulated && !live?.url && kind === 'none') return null
  // 실제 장비: 서버가 주는 실제 이미지 (SharpCap 창 · 가이드 카메라 · 솔빙 사진 · 시험 사진)
  if (!simulated && live?.url && !extras?.grid)
    return (
      <div className={styles.view} aria-hidden="true">
        {kind === 'sharpcap' ? (
          // SharpCap 영상: 데스크톱 창이 SharpCap 영상 칸을 그대로 비춘다(WindowView). 못 하면 예전처럼 창 캡처 사진
          <WindowView target="sharpcap" fallback={<RealImage url={live.url} refresh stamp={live.observedAt} />} />
        ) : (
          <RealImage
            url={live.url}
            refresh={kind === 'guide-image' || kind === 'focus-curve'}
            stamp={live.observedAt}
            onOpen={onPhoto}
            onFit={kind === 'solved-photo' ? onPhotoBox : undefined}
          />
        )}
        {kind === 'guide-image' && <GuidingGraph data={live.data} />}
        {kind === 'test-photo' && <Histogram data={live.data} />}
        {kind === 'focus-curve' && <FocusPanel data={live.data} />}
      </div>
    )

  if (simSrc)
    return (
      <div className={styles.view} aria-hidden="true">
        <FramedImage src={simSrc} onFit={onPhotoBox} />
      </div>
    )

  return (
    <div className={styles.view} data-clear={!simulated} aria-hidden="true">
      <svg viewBox={`0 0 ${VW} ${VH}`} preserveAspectRatio="xMidYMid slice">
        {body}
      </svg>
      {kind === 'guide-image' && <GuidingGraph data={live?.data} />}
      {kind === 'test-photo' && !extras?.grid && <Histogram data={live?.data} />}
    </div>
  )
}

function activeSub(current: CurrentView | null) {
  return current?.subSteps.find((s) => s.status === 'Running' || s.status === 'Waiting')?.id ?? null
}

// ── 그림 ─────────
