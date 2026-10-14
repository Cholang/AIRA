import { displayName, type ChartPick } from '../plan'

// 밤 그래프(NightGraph)의 계산 — 화면 없이 시험할 수 있게 따로 둔다 (nightGraphMath.test.ts)

/** 시각 t(분)의 고도: 5분 간격 자료 사이를 잇는다 */
export function altitudeOn(track: [number, number][], t: number) {
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
export function trackPath(
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
export function layoutTags(picks: ChartPick[], at: (p: ChartPick) => { x: number; y: number }) {
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
