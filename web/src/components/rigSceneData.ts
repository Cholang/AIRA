import type { CSSProperties } from 'react'

/** 장면 RigScene의 데이터: 점검 항목, 하늘(별자리·북극성·극축 점선), 땅·잔디, 구체 회전·부팅·유성 시간 상수 (큰 파일 분리 2026-10-16 — 옮기기만) */

export type Part = 'focus' | 'power' | 'dir' | 'caps'

/**
 * 점검 네 가지 (OPERATION_PROCEDURE.md 4장 — 설치하는 차례). 질문은 그 부분에서 뽑은 선 끝의 여백에 짧게(at = 그림 좌표),
 * leader = 그 부분에서 질문까지의 선, dot = 선의 시작점
 */
export const ITEMS: { part: Part; question: string; at: [number, number]; leader: string; dot: [number, number] }[] = [
  { part: 'focus', question: '포커서 노브를 끝까지 넣어 0에 맞췄나요?', at: [96, 437], leader: 'M358 683 L262 511', dot: [358, 683] },
  { part: 'power', question: '적도의 전원을 켰나요?', at: [780, 812], leader: 'M814 944 L814 852', dot: [814, 944] },
  { part: 'dir', question: '적도의를 북쪽으로 향하게 세우고 홈 자세로 두었나요?', at: [820, 590], leader: 'M587 640 L812 624', dot: [587, 640] },
  { part: 'caps', question: '망원경 뚜껑을 모두 열었나요?', at: [496, 299], leader: 'M700 433 L656 371', dot: [700, 433] },
]

type Star = [number, number, 0 | 1 | 2 | 3 | 4]
/**
 * 하늘: 극축을 맞추면 북극성이 점선 끝에 온다. 맞추기 전에는 하늘 그림이 화면 왼쪽 아래 밖의 한 점(SKY_PIVOT)을 중심으로 SKY_TURN도 돌아 있고,
 * 방향을 확인하면 배경과 함께 그만큼 거꾸로 돌며 맞춰진다 (2026-10-15 사용자 — 옆으로 미는 것은 어색했다)
 */
export const SKY_TURN = 6
export const SKY_PIVOT: [number, number] = [-300, 1500]
/**
 * 별자리 그림의 자리 (2026-10-15 사용자): 처음 그림보다 별 사이를 30% 넓게, 별자리 반 개만큼(SKY_DROP) 아래로 — 별 하나하나의 크기는 그대로.
 * 아래 별자리 좌표는 처음 그림 기준이고, 묶음의 가운데(SKY_CENTER)를 기준으로 place가 옮긴다
 */
const SKY_GROW = 1.3
const SKY_CENTER: [number, number] = [1374, 212]
const SKY_DROP = 75
const POLARIS_DRAWN: [number, number] = [1361, 103]
/** 적도의 몸통(극축) 기울기 — 그림의 rotate(-POLE_TILT)와 같은 값 */
export const POLE_TILT = 37
/** 극축 점선의 시작: 적도의 몸통 앞 끝 가운데 (장비 좌표 몸통 (80, 78)을 rotate(-37) · 1.16배 · (80, 338)로 옮긴 곳) */
const RAY_FROM: [number, number] = [642.4, 598.1]
const spread = (x: number, y: number): [number, number] => [SKY_CENTER[0] + (x - SKY_CENTER[0]) * SKY_GROW, SKY_CENTER[1] + SKY_DROP + (y - SKY_CENTER[1]) * SKY_GROW]
/** 하늘 그림 전체를 옆으로: 북극성이 극축(몸통 기울기) 위에 오게 (2026-10-16 사용자 — 점선이 몸통보다 덜 기울었다) */
const SKY_SLIDE = (() => {
  const [px, py] = spread(...POLARIS_DRAWN)
  return RAY_FROM[0] + (RAY_FROM[1] - py) / Math.tan((POLE_TILT * Math.PI) / 180) - px
})()
const place = (x: number, y: number): [number, number] => {
  const [sx, sy] = spread(x, y)
  return [sx + SKY_SLIDE, sy]
}
/** 북극성 (맞춘 뒤 자리) — 극축 점선의 끝 */
export const POLARIS = place(...POLARIS_DRAWN)
/** 극축 점선: 적도의 몸통 앞 끝 → 북극성 (몸통과 같은 기울기) */
export const RAY = `M${RAY_FROM[0]} ${RAY_FROM[1]} L${POLARIS[0].toFixed(1)} ${POLARIS[1].toFixed(1)}`
/** 별자리 (맞춘 뒤 좌표). lines = 별 번호를 잇는 선들 */
type Constellation = { name: string; stars: Star[]; lines: number[][] }
export const CONSTELLATIONS: Constellation[] = (
  [
    // 작은곰자리: 북극성(손잡이 끝) · δ · ε · ζ · η · γ · β(코카브)
    { name: 'umi', stars: [[...POLARIS_DRAWN, 4], [1396, 124, 1], [1426, 154, 1], [1448, 190, 1], [1494, 196, 0], [1504, 240, 2], [1458, 244, 3]], lines: [[0, 1, 2, 3, 4, 5, 6, 3]] },
    // 북두칠성: 국자 끝 두 별(메라크 → 두베)이 북극성을 가리킨다
    { name: 'uma', stars: [[1262, 322, 3], [1244, 364, 3], [1286, 382, 2], [1304, 340, 1], [1343, 357, 3], [1381, 369, 2], [1418, 393, 3]], lines: [[1, 0, 3, 2, 1], [3, 4, 5, 6]] },
    // 카시오페이아 (W)
    { name: 'cas', stars: [[1288, 40, 1], [1314, 62, 2], [1340, 38, 2], [1370, 60, 2], [1398, 32, 2]], lines: [[0, 1, 2, 3, 4]] },
  ] satisfies Constellation[]
).map((k) => ({ ...k, stars: k.stars.map(([x, y, m]): Star => [...place(x, y), m]) }))
/** 별자리 밖의 희미한 별 */
export const FIELD_STARS: Star[] = [[780, 150, 0], [860, 230, 0], [985, 120, 0], [1150, 190, 0], [1210, 470, 0], [1530, 420, 0], [1570, 300, 0], [1650, 230, 0], [1100, 60, 0]]
/** 밝기별 [빛나는 가운데, 번짐] 반지름 */
export const STAR_SIZE: Record<Star[2], [number, number]> = { 0: [0.9, 3.5], 1: [1.2, 5], 2: [1.6, 7], 3: [2, 9], 4: [2.4, 12] }

/** 그림 좌표 = 틀 너비 × 상태 줄 아래 높이 (1920×1200 틀). 진행 표시 밑까지 그려 땅이 오른쪽 끝에서 잘리지 않는다 */
export const VW = 1920
export const VH = 1156
/** 부팅: AIRA 별자리 글자 · 아이라 · 영어 풀이 (aira-wordmark.svg 506×390) — 하늘 오른쪽 가운데, 프로필 목록과 같은 자리 */
export const WORDMARK = { x: 907, y: 200, w: 506 }
export const LIGHT_MS = 1500 // 프로필을 고르면 별이 켜지고, 이만큼 뒤에 제목과 첫 질문
export const ZOOM_MS = 820 // 노트북 화면 쪽으로 확대된 뒤 다음 단계로
/**
 * 부팅 로고가 차례로 나타나는 때 (장면이 나타난 뒤, ms — 2026-10-16 사용자): AIRA 별자리 → 아이라 → 영어 풀이 → "Press Any Key".
 * 하나마다 BOOT_FADE_IN에 걸쳐 나타난다(CSS .wordmark가 --boot-* 값으로). AIRA가 다 나타나면 별이 무작위로 빛나기 시작한다
 */
export const BOOT_AT = { aira: 150, name: 950, tagline: 1650 }
export const BOOT_FADE_IN = 900
/** "Press Any Key"가 나타나는 때 — App이 이때(프로필 목록을 받았으면)부터 입력을 받는다. 앞의 차례가 그대로 기다림이 된다 */
export const BOOT_PROMPT_MS = 2400
export const BOOT_TIMING = {
  '--boot-aira': `${BOOT_AT.aira}ms`,
  '--boot-name': `${BOOT_AT.name}ms`,
  '--boot-tagline': `${BOOT_AT.tagline}ms`,
  '--boot-fade': `${BOOT_FADE_IN}ms`,
} as CSSProperties
export const FLARE_MS = 1100 // 별 하나(와 이어진 선)가 빛나 있는 시간
/** 땅: 오른쪽으로 완만하게 내려감, 진행 표시 밑으로도. 틀 밖(16:9 등 창의 여백)까지 같은 기울기로 이어 창 끝에서 끝난다 */
export const GROUND = 'M-2000 857 L-40 930 Q900 965 1990 1086 L4000 1309 L4000 3000 L-2000 3000 Z'
export const GROUND_TOP = 'M-2000 857 L-40 930 Q900 965 1990 1086 L4000 1309'

/** 땅의 윗선(그림 좌표): 왼쪽 직선 → 2차 곡선(-40,930 · 900,965 · 1990,1086) → 오른쪽 직선. 땅 path와 같은 모양 */
export function groundTop(x: number) {
  if (x < -40) return 930 + (x + 40) * (35 / 940)
  if (x > 1990) return 1086 + (x - 1990) * (121 / 1090)
  const t = (-1880 + Math.sqrt(1880 * 1880 + 600 * (x + 40))) / 300 // x(t) = -40 + 1880t + 150t²
  return 930 * (1 - t) ** 2 + 2 * 965 * t * (1 - t) + 1086 * t * t
}

/**
 * 부팅 → 프로필: 땅은 큰 구체 (2026-10-15 사용자). 키를 누르면 구체가 돌며 카메라 뒤에 있던 장비가 화면에 들어와 제자리에 선다.
 * 땅 윗선(GROUND_TOP)은 구체의 가장자리라 움직이지 않고, 땅 위의 것(잔디 · 장비)은 카메라 쪽에서 가장자리 쪽으로 흘러가 가장자리 너머로 사라진다.
 * 깊이 z(가장자리 = 1, 작을수록 카메라에 가까움)의 점은 화면에서 카메라 눈높이 점(SPIN_VP)을 중심으로 가장자리 곡선을 1/z배 한 곡선 위에 있다 —
 * 그래서 같은 깊이의 것은 SPIN_VP를 중심으로 한 배율로 함께 움직인다(장비와 그 발 둘레 잔디가 같은 배율 → 땅 위를 미끄러지지 않는다).
 * 구체가 돌면 모든 깊이가 같은 만큼 줄어든다(부팅 = SPIN_SHIFT만큼 돌기 전, 그때 장비는 8배 — 화면 밖).
 * SPIN_VP는 장비 발보다 위 — 발 높이에 두면 장비가 옆으로 미끄러져 와 떠 보였다
 */
const SPIN_VP: [number, number] = [1000, 600]
export const SPIN_ORIGIN: CSSProperties = { transformOrigin: `${SPIN_VP[0]}px ${SPIN_VP[1]}px` }
export const SPIN_MS = 2600
/** 장비의 깊이: 발(그림 560, 995)이 놓인 곳 */
export const RIG_Z = (groundTop(560) - SPIN_VP[1]) / (995 - SPIN_VP[1])
export const SPIN_SHIFT = RIG_Z * (1 - 1 / 8)

/** 가장자리 너머로 넘어간 풀이 땅 뒤로 가라앉는 깊이 — 이만큼 넘어가면 다 가라앉아 그리지 않는다 (가라앉는 동안 배율 1 + 넘어간 깊이) */
export const SINK_Z = 0.07
/** 가장자리 위쪽(하늘) — 넘어간 풀은 이 안만 보인다(아랫부분부터 땅 뒤로 가라앉음). GROUND_TOP의 반대쪽 */
export const ABOVE_GROUND = 'M-2000 -2000 L4000 -2000 L4000 1309 L1990 1086 Q900 965 -40 930 L-2000 857 Z'

/**
 * 잔디 (2026-10-15 사용자): 로고의 풀처럼 곧은 획 2~3개짜리 풀포기를 듬성듬성 (mockups/aira-brand/aira-logo-v5.svg —
 * 셋: 가운데가 곧게 가장 길고 양옆은 바깥으로 기움 / 둘: 벌어지는 두 획). 선 굵기는 장비 선화와 비슷하게.
 * 깊이마다 한 줄(rows) — 가까울수록(z가 작을수록) 크고 화면에서 성기다. 부팅 때 보이는 줄부터 끝에 보이는 줄까지(가장자리 너머로 넘어갈 줄도) 미리 만든다.
 * 가장자리에 따로 고정된 풀은 두지 않는다 — 넘어가는 풀이 가라앉으며 사라진다(SINK_Z). 씨앗이 고정이라 늘 같은 모양
 */
export const GRASS = (() => {
  let seed = 20261015
  const rnd = () => {
    seed = (seed * 1664525 + 1013904223) % 4294967296
    return seed / 4294967296
  }
  const p = (n: number) => n.toFixed(1)
  const line = (x1: number, y1: number, x2: number, y2: number) => `M${p(x1)} ${p(y1)} L${p(x2)} ${p(y2)}`
  // 풀포기 하나 (밑동 x, y · 가운데 획 길이 h). 로고는 굵은 선이라 획 사이가 붙어 보였다 — 가는 선에서는 밑동을 모아야 한 포기로 보인다
  const tuft = (out: string[], x: number, y: number, h: number) => {
    const f = rnd() < 0.5 ? -1 : 1
    if (rnd() < 0.6) {
      out.push(line(x, y, x, y - h), line(x - 0.35 * h, y - 0.05 * h, x - 0.8 * h, y - 0.75 * h), line(x + 0.35 * h, y - 0.05 * h, x + 0.8 * h, y - 0.75 * h))
    } else {
      out.push(line(x, y, x - 0.3 * h * f, y - h), line(x + 0.35 * h * f, y, x + 0.8 * h * f, y - 0.7 * h))
    }
  }
  // 줄: 화면에 보이는 깊이(가장자리 1 ~ 화면 아래 0.55)와, 부팅 때 그 자리에 있다가 끝에 가장자리 너머로 넘어갈 깊이까지
  const rows: { z: number; d: string }[] = []
  for (let z = 0.55; z <= 1 + SPIN_SHIFT + SINK_Z; z += 0.035) {
    const m = 1 / z
    const out: string[] = []
    for (let k = 0; k < 5; k++) {
      const x0 = -400 + rnd() * 2750
      tuft(out, SPIN_VP[0] + (x0 - SPIN_VP[0]) * m, SPIN_VP[1] + (groundTop(x0) + 6 - SPIN_VP[1]) * m, (12 + rnd() * 4) * m)
    }
    rows.push({ z, d: out.join(' ') })
  }
  return rows
})()

export const reduced = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

/**
 * 부팅 하늘의 유성 (2026-10-15 사용자, 2026-10-16 고침): 로고가 다 그려진 뒤 키를 누를 때까지 2~5초마다 하나씩(동시에 둘은 없음), 빠르게 선을 그으며 떨어진다.
 * 대부분은 짧게, 가끔(METEOR_LONG) 화면 왼쪽까지 길게 — 빠르기는 같다.
 * 모두 METEOR_ANGLE 방향(그림 좌표의 도 — 오른쪽 위에서 왼쪽 아래로). 하늘 위쪽에서 시작해 땅에 닿기 전에 사라진다(땅이 앞에 그려져 가린다)
 */
export const METEOR_ANGLE = 155
/** 유성 꼬리 길이(그림 px) — 그릴 때 0.6~1배로 */
export const METEOR_LEN = 220
/** 길게 지나가는 유성의 비율 · 빠르기(그림 px/ms) */
export const METEOR_LONG = 0.35
export const METEOR_SPEED = 0.7
export type Meteor = { id: number; x: number; y: number; delay: number; dur: number; dist: number; len: number }
