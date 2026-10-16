import { useRef } from 'react'
import type { CurrentView } from '../prepare'

export interface SlewTrack {
  /** 0 = 출발, 1 = 도착 */
  progress: number
  targetAlt: number
}

/** 대상 이동: 남은 거리(readout)로 적도의 점의 위치를 정한다 — 처음 본 거리를 출발로 */
export function useSlewTrack(current: CurrentView | null): SlewTrack {
  const start = useRef<{ run: number; dist: number } | null>(null)
  const alt = useRef(42)
  const r = current?.center.readout
  const runId = current?.runId ?? 0
  if (current?.taskId !== 'slew') start.current = null
  if (r?.values.altitudeDeg !== undefined) alt.current = r.values.altitudeDeg
  let progress = 0
  if (r?.kind === 'distance') {
    if (r.values.arrivalDeg !== undefined) progress = 1
    else if (r.values.remainingDeg !== undefined) {
      const d = r.values.remainingDeg
      if (!start.current || start.current.run !== runId || d > start.current.dist) start.current = { run: runId, dist: d }
      progress = start.current.dist > 0 ? 1 - d / start.current.dist : 1
    }
  }
  return { progress, targetAlt: alt.current }
}
