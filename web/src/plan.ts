// 촬영 계획 화면과 서버(/api/plan) 사이의 모양. 서버 PlanView·NightChart와 같게 유지한다 (2026-10-14 개편: 계획 = 대상 + 촬영 설정).

/** 그래프에 함께 그리는 고른 대상 하나 */
export interface PlanPick {
  id: string
  name: string
  koreanName: string | null
  commonName: string | null
  type: string
  constellation: string
  sizeArcmin: number | null
  raDeg: number
  decDeg: number
  fillPercent: number | null
  moonSeparation: number
}

export interface PlanSettings {
  filter: string
  iso: number
  exposure: number
  totalMinutes: number
  /** 되돌리기 화살표가 돌아가는 값 (대상과 상관없이 장비·필터·달·가이딩으로 정해짐) */
  defaults: { iso: number; exposure: number; totalMinutes: number }
}

export type GainOption =
  | { kind: 'list'; unit: string; values: number[] }
  | { kind: 'range'; unit: string; min: number | null; max: number | null }

export interface PlanOptions {
  filters: { key: string; label: string }[]
  gain: GainOption
  exposure: { min: number; max: number; step: number }
  totalMinutes: { min: number; max: number; step: number }
  maxPicks: number
  prepMinutes: number
}

/** 지금 대상의 촬영 시간대·장수 (시각은 HH:mm) */
export interface PlanWindow {
  start: string
  /** 물리 한계: 새벽 또는 대상이 끝 고도 아래로 */
  limit: string
  /** 다 채우는 밤의 예상 끝 (못 채우면 limit) */
  end: string
  moveAt: string
  startAltitude: number
  endAltitude: number
  /** 시작이 늦어지는 까닭 */
  wait: 'none' | 'dark' | 'altitude' | 'chosen'
  stop: 'dawn' | 'low'
  moved: boolean
  transit: string | null
  frames: number
  maxFrames: number
  planned: number
  clipped: boolean
  secondsPerFrame: number
  overheadSeconds: number
  minutes: number
}

export interface Plan {
  complete: boolean
  asking: string | null
  /** 오늘 밤 한 줄 (어두운 시간 · 달 · 구름) */
  night: string
  current: string | null
  picks: PlanPick[]
  fov: { width: number; height: number; camera: string; telescope: string }
  settings: PlanSettings | null
  options: PlanOptions
  window: PlanWindow | null
}

/** 그래프 자료. 시각은 모두 일몰부터의 분 */
export interface ChartPick {
  id: string
  name: string
  koreanName: string | null
  type: string
  /** [분, 고도] 5분마다 */
  track: [number, number][]
  peak: { at: number; alt: number; label: string } | null
  window: {
    start: number
    limit: number
    end: number
    clipped: boolean
    startAltitude: number
    endAltitude: number
    transit: number | null
    transitLabel: string | null
    dragFrom: number
    dragTo: number
    moved: boolean
    moveAt: number
  } | null
}

export interface Chart {
  sunsetLabel: string
  sunriseLabel: string
  span: number
  /** 일몰의 하루 중 분 (분 → 시각 글자) */
  startClock: number
  dark: { start: number; end: number; startLabel: string; endLabel: string }
  now: number
  nowLabel: string
  hours: { at: number; label: string }[]
  clouds: { at: number; cover: number }[]
  moon: { illumination: number; waxing: boolean; samples: { at: number; alt: number; label: string }[] }
  current: string | null
  picks: ChartPick[]
  minAltitude: number
  lowestAltitude: number
}

export interface Message {
  /** notice: 연습 대화로 넘어감 등 안내 한 줄 */
  role: 'assistant' | 'user' | 'notice'
  text: string
  choices?: string[] | null
  /** 이 메시지의 선택지 중 사용자가 고른 것 */
  picked?: string
}

export interface PlanState {
  error?: string
  messages: Message[]
  plan: Plan
  chart: Chart
  catalogAvailable: boolean
  cloudsAvailable: boolean
  /** AI를 쓸 수 없는 이유. 있으면 대화 대신 이름 검색만 */
  practiceReason: string | null
}

/** 검색창 결과 한 줄 */
export interface FoundTarget {
  id: string
  name: string
  koreanName: string | null
  commonName: string | null
  type: string
  constellation: string
}

/** 분(일몰부터) → "HH:mm" */
export function clockOf(chart: Chart, minute: number) {
  const m = Math.round(chart.startClock + minute)
  const d = ((m % 1440) + 1440) % 1440
  return `${String(Math.floor(d / 60)).padStart(2, '0')}:${String(d % 60).padStart(2, '0')}`
}

/** 대상 이름: "M31 안드로메다 은하" (한국어 통칭이 없으면 영문 통칭) */
export const displayName = (t: { name: string; koreanName: string | null; commonName?: string | null }) =>
  [t.name, t.koreanName ?? t.commonName].filter(Boolean).join(' ')

export type Edited = { plan: Plan; chart: Chart }

/** 계획 고치기. 고칠 수 없으면 서버의 이유 문장으로 실패 */
async function edit(path: string, body: unknown): Promise<Edited> {
  const res = await fetch(`/api/plan/${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (res.ok) return res.json()
  const err = (await res.json().catch(() => null)) as { error?: string } | null
  throw new Error(err?.error ?? '계획을 바꾸지 못했습니다. 다시 해 주세요.')
}

export const pickTarget = (id: string) => edit('pick', { id })
export const removeTarget = (id: string) => edit('remove', { id })
export const changeSettings = (change: { filter?: string; iso?: number; exposure?: number; totalMinutes?: number }) => edit('settings', change)
/** 시작점 옮기기 (일몰부터의 분). null이면 기본으로 */
export const setStart = (id: string, minute: number | null) => edit('start', { id, minute })

export async function searchTargets(q: string): Promise<FoundTarget[]> {
  const res = await fetch(`/api/plan/search?q=${encodeURIComponent(q)}`)
  return res.ok ? res.json() : []
}

export type ChatEvent =
  | { type: 'text'; text: string }
  | { type: 'plan'; plan: Plan }
  | { type: 'choices'; choices: string[] }
  | { type: 'notice'; message: string }
  /** AI가 도구를 쓰는 동안 지금 하는 일 (답 글자가 오기 전까지 점 옆에) */
  | { type: 'status'; message: string }
  | { type: 'error'; message: string }
  | { type: 'done' }

/** 사용자 말 하나를 보내고, 서버가 보내는 이벤트(SSE)를 하나씩 넘겨준다 */
export async function sendChat(text: string, onEvent: (e: ChatEvent) => void): Promise<void> {
  const res = await fetch('/api/plan/chat', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ text }),
  })
  if (!res.ok || !res.body) throw new Error(`서버 응답 ${res.status}`)
  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value
    let cut: number
    while ((cut = buffer.indexOf('\n\n')) >= 0) {
      const block = buffer.slice(0, cut)
      buffer = buffer.slice(cut + 2)
      let type = 'message'
      let data = ''
      for (const line of block.split('\n')) {
        if (line.startsWith('event:')) type = line.slice(6).trim()
        else if (line.startsWith('data:')) data += line.slice(5).trim()
      }
      if (!data) continue
      const payload = JSON.parse(data)
      if (type === 'plan') onEvent({ type, plan: payload })
      else onEvent({ type, ...payload } as ChatEvent)
    }
  }
}
