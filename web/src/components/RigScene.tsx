import { useCallback, useEffect, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type Ref } from 'react'
import wordmarkSvg from '../assets/aira-wordmark.svg?raw'
import { scaleOf } from '../frame'
import { scopedSvg } from '../inlineSvg'
import { VERSION } from '../product'
import { setSkyTurn } from '../skyPan'
import styles from './RigScene.module.css'
import {
  ITEMS, SKY_TURN, SKY_PIVOT, POLE_TILT, POLARIS, RAY, CONSTELLATIONS, FIELD_STARS, STAR_SIZE, VW, VH, WORDMARK, LIGHT_MS, ZOOM_MS, BOOT_AT, BOOT_FADE_IN, BOOT_TIMING, FLARE_MS, GROUND, GROUND_TOP, SPIN_ORIGIN, SPIN_MS, RIG_Z, SPIN_SHIFT, SINK_Z, ABOVE_GROUND, GRASS, reduced, METEOR_ANGLE, METEOR_LEN, METEOR_LONG, METEOR_SPEED, type Part, type Meteor,
} from './rigSceneData'

/**
 * 장면이 지금 보여 주는 것 — App이 넘어가는 중인 다음 화면으로 정한다.
 * boot 하늘에 로고 글자 · profiles 빈 하늘(프로필 목록은 화면 영역에) · dim 흐리게(새 프로필) · check 별이 켜지고 점검 · out 사라짐
 */
export type SceneMode = 'boot' | 'profiles' | 'dim' | 'check' | 'out'

export interface RigSceneHandle {
  /** 노트북 화면 쪽으로 확대한 뒤 then — 이어서 하기(점검을 건너뜀)도 이 길로 */
  dive: (then: () => void) => void
}

/**
 * 땅과 장비 선화 한 장면 (2026-10-15 사용자): 부팅 로고 → 프로필 → 출발 전 점검이 같은 땅·장비 위에서 이어진다.
 * App이 화면 전환 페이드 밖, 틀 너비 전체에 깐다 — 화면만 바뀌고 장면은 그대로.
 * 부팅: 장비는 카메라 뒤(보이지 않음), 별자리·북극성 없이 하늘에 AIRA → "아이라" → 영어 풀이(로고의 오른쪽) → "Press Any Key"(prompt)가 차례로, 처음부터 유성.
 * 키를 누르면 "Press Any Key"는 한 번 펄스하며 사라지고 아이라·풀이는 바로 투명해지며, AIRA는 한 번 반짝인 뒤 선 → 별 순서로 사라진다.
 * 같은 때 땅(구체)이 돌며 장비가 화면 아래에서 들어와 제자리에 서고(SPIN), 로고 자리에 프로필 목록.
 * 프로필을 고르면 별자리와 북극성이 켜지고 점검: 아이라가 스스로 알 수 없는 것만 묻는다(OPERATION_PROCEDURE.md 4장).
 * 지금 항목만 강조색으로 숨 쉬고, 그 부분의 그림을 누르면 확인 — 그림이 바뀐다(드로튜브가 들어감 · 전원 불 · 극축 점선이 북극성에 · 캡이 빠짐).
 * 다 확인하면 노트북 화면이 빛나고 역삼각형이 내려와 가리킨다. 노트북(또는 Enter)을 누르면 화면 쪽으로 확대되며 onDone.
 * 버튼은 없다 — 시험용 넘기기는 진행 표시 아래의 [임시] 건너뛰기(App)
 */
export default function RigScene({
  mode,
  theme,
  prompt,
  onDone,
  ref,
}: {
  mode: SceneMode
  theme: string
  /** 부팅: "Press Any Key"를 보인다 (입력은 App이 받는다) */
  prompt: boolean
  onDone: () => void
  ref?: Ref<RigSceneHandle>
}) {
  // 별이 다 켜져 점검 중인가 · 노트북으로 확대 중
  const [checking, setChecking] = useState(false)
  const [at, setAt] = useState(0)
  const [zoom, setZoom] = useState<CSSProperties | null>(null)
  const stage = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLDivElement>(null)
  const screenRect = useRef<SVGRectElement>(null)
  const timers = useRef<number[]>([])
  const later = useCallback((fn: () => void, ms: number) => {
    timers.current.push(window.setTimeout(fn, reduced() ? 0 : ms))
  }, [])
  useEffect(() => () => timers.current.forEach(clearTimeout), [])

  // 프로필을 고르면 별이 하나씩 켜지고, 다 켜지면 점검. 켜지는 중에 누르면 바로
  const lit = mode === 'check'
  useEffect(() => {
    if (lit) later(() => setChecking(true), LIGHT_MS)
  }, [lit, later])
  const scene = zoom ? 'zoom' : checking ? 'check' : lit ? 'light' : 'idle'
  const skipLight = () => scene === 'light' && setChecking(true)

  const done = at >= ITEMS.length
  const aligned = ITEMS.findIndex((x) => x.part === 'dir') < at
  // 극축 방향을 확인하면 배경도 같은 중심(창 좌표로 바꿔)·각도로 돈다 — 별자리 그림(CSS .skyArt)과 같은 시간
  useEffect(() => {
    const el = canvas.current
    if (!aligned || !el) return
    const r = el.getBoundingClientRect()
    const k = r.width / VW
    setSkyTurn(-SKY_TURN, r.left + SKY_PIVOT[0] * k, r.top + SKY_PIVOT[1] * k)
  }, [aligned])

  const confirm = (i: number) => {
    if (scene === 'check' && i === at) setAt((a) => a + 1)
  }

  // 부팅 → 프로필: 구체가 돌며(e: 0 = 부팅, 1 = 제자리) 장비와 잔디 줄이 깊이만큼 함께 움직인다 (SPIN_VP 설명). 하늘은 멀어서 그대로
  const rigArt = useRef<SVGGElement>(null)
  // 잔디 줄마다 바깥(가라앉을 때 가장자리 위만 보이게 자름) · 안(배율)
  const grassClip = useRef<(SVGGElement | null)[]>([])
  const grassRows = useRef<(SVGGElement | null)[]>([])
  const roll = useCallback((e: number) => {
    const back = SPIN_SHIFT * (1 - e)
    if (rigArt.current) rigArt.current.style.transform = `scale(${RIG_Z / (RIG_Z - back)})`
    GRASS.forEach((row, i) => {
      const outer = grassClip.current[i]
      const inner = grassRows.current[i]
      if (!outer || !inner) return
      const z = row.z - back
      // 카메라 뒤(z ≤ 0)이거나 가장자리 너머로 다 가라앉았으면 그리지 않는다
      const shown = z > 0.02 && z < 1 + SINK_Z
      outer.style.visibility = shown ? 'visible' : 'hidden'
      if (!shown) return
      // 가장자리 너머(z > 1): 가장자리 바로 아래로 내려가며 가장자리 위만 보인다 — 땅 뒤로 가라앉는다
      const beyond = z > 1
      outer.style.clipPath = beyond ? 'url(#pf-beyond)' : ''
      inner.style.transform = `scale(${(beyond ? 1 + (z - 1) : 1 / z) * row.z})`
    })
  }, [])
  // 처음 그릴 때 자리: 부팅이면 돌기 전, 아니면(오류 뒤 다시 그림 등) 제자리
  const spinFromBoot = useRef(mode === 'boot')
  useLayoutEffect(() => roll(spinFromBoot.current ? 0 : 1), [roll])
  const booted = mode !== 'boot'
  useEffect(() => {
    if (!booted || !spinFromBoot.current) return
    if (reduced()) return roll(1)
    let raf = 0
    const t0 = performance.now()
    const step = (now: number) => {
      const t = Math.min(1, (now - t0) / SPIN_MS)
      roll(0.5 - 0.5 * Math.cos(Math.PI * t)) // 천천히 돌기 시작해 천천히 멈춘다
      if (t < 1) raf = requestAnimationFrame(step)
    }
    raf = requestAnimationFrame(step)
    return () => {
      cancelAnimationFrame(raf)
      roll(1)
    }
  }, [booted, roll])

  // 노트북 화면 쪽으로 확대 → then (한 번만)
  const diving = useRef(false)
  const dive = useCallback(
    (then: () => void) => {
      if (diving.current) return
      diving.current = true
      const el = stage.current
      const r = screenRect.current
      if (reduced() || !el || !r) return then()
      const s = scaleOf(el)
      const a = el.getBoundingClientRect()
      const b = r.getBoundingClientRect()
      const cx = (b.left + b.width / 2 - a.left) / s
      const cy = (b.top + b.height / 2 - a.top) / s
      const k = (a.width / b.width) * 1.15
      setZoom({ transformOrigin: `${cx}px ${cy}px`, transform: `translate(${a.width / s / 2 - cx}px, ${a.height / s / 2 - cy}px) scale(${k})`, opacity: 0 })
      later(then, ZOOM_MS)
    },
    [later],
  )
  useImperativeHandle(ref, () => ({ dive }), [dive])
  const enter = useCallback(() => {
    if (scene === 'check' && done) dive(onDone)
  }, [scene, done, dive, onDone])
  useEffect(() => {
    if (!done || scene !== 'check') return
    // 팝업(종료 확인 등)이 열려 있거나 이미 처리된 키·길게 눌러 반복되는 키는 넘기지 않는다 — 팝업에서 누른 Enter가 window까지 올라와
    // 뒤 화면도 넘어갔다 (CX-36-08)
    const key = (e: globalThis.KeyboardEvent) => {
      if (e.key !== 'Enter' || e.repeat || e.defaultPrevented || document.querySelector('dialog[open]')) return
      enter()
    }
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [done, scene, enter])

  // 로고 SVG는 data-theme으로 색이 바뀐다 — 지금 테마를 넣어 인라인으로, 안의 스타일·id는 그림 안에 가둔다
  const wordmark = useMemo(() => scopedSvg(wordmarkSvg, 'aira-wordmark', theme), [theme])

  // 부팅: 로고가 다 그려지면 AIRA 별이 무작위로 하나씩 빛나고, 그 별에 이어진 선도 함께 빛난다 (2026-10-15 사용자).
  // 규칙: 다음 별은 0.3~0.7초 뒤 무작위(같은 별이 잇달아 나오지 않게). 빛난 별의 이웃(선으로 이어진 별)이 0.25초 뒤 이어서 빛나기도 한다(절반쯤, 두 번까지 — 선을 따라 흐르듯).
  // 별 = 그림의 .tw(차례가 번호), 선 = path[data-a·data-b](잇는 두 별 번호)
  const wordArt = useRef<HTMLDivElement>(null)
  const [drawn, setDrawn] = useState(false)
  useEffect(() => later(() => setDrawn(true), BOOT_AT.aira + BOOT_FADE_IN), [later])
  useEffect(() => {
    const root = wordArt.current
    if (mode !== 'boot' || !drawn || !root || reduced()) return
    const stars = [...root.querySelectorAll<SVGGElement>('.tw')]
    const lines = [...root.querySelectorAll<SVGPathElement>('path[data-a]')]
    const pending = new Set<number>()
    const after = (fn: () => void, ms: number) => {
      const id = window.setTimeout(() => {
        pending.delete(id)
        fn()
      }, ms)
      pending.add(id)
    }
    const glow = (i: number, from: number, hop: number) => {
      const touching = lines.filter((l) => l.dataset.a === String(i) || l.dataset.b === String(i))
      const lit = [stars[i], ...touching]
      lit.forEach((el) => el.classList.add('on'))
      after(() => lit.forEach((el) => el.classList.remove('on')), FLARE_MS)
      const next = touching.map((l) => Number(l.dataset.a === String(i) ? l.dataset.b : l.dataset.a)).filter((j) => j !== from)
      if (hop < 2 && next.length > 0 && Math.random() < 0.5) after(() => glow(next[Math.floor(Math.random() * next.length)], i, hop + 1), 250)
    }
    let last = -1
    const tick = () => {
      let i = Math.floor(Math.random() * stars.length)
      if (i === last) i = (i + 1) % stars.length
      last = i
      glow(i, -1, 0)
      after(tick, 300 + Math.random() * 400)
    }
    tick()
    return () => {
      pending.forEach(clearTimeout)
      root.querySelectorAll('.on').forEach((el) => el.classList.remove('on'))
    }
  }, [mode, drawn, wordmark])

  // 부팅: 장면이 나타날 때부터 키를 누를 때까지 유성 (METEOR_ANGLE 설명, 2026-10-16 사용자 — 로고를 기다리지 않음)
  const [meteors, setMeteors] = useState<Meteor[]>([])
  useEffect(() => {
    if (mode !== 'boot' || reduced()) return
    const pending = new Set<number>()
    const after = (fn: () => void, ms: number) => {
      const t = window.setTimeout(() => {
        pending.delete(t)
        fn()
      }, ms)
      pending.add(t)
    }
    let id = 0
    const burst = () => {
      const long = Math.random() < METEOR_LONG
      const dist = long ? 760 + Math.random() * 420 : 360 + Math.random() * 180
      const made: Meteor[] = [
        {
          id: ++id,
          // 길게 가는 것은 오른쪽에서 시작해 화면 왼쪽까지
          x: long ? 1300 + Math.random() * 500 : 800 + Math.random() * 1050,
          y: 30 + Math.random() * 260,
          delay: 0,
          dur: dist / METEOR_SPEED,
          dist,
          len: long ? 0.85 + Math.random() * 0.3 : 0.6 + Math.random() * 0.4,
        },
      ]
      setMeteors((list) => [...list, ...made])
      const end = Math.max(...made.map((m) => m.delay + m.dur)) + 100
      after(() => setMeteors((list) => list.filter((m) => !made.includes(m))), end)
      after(burst, 2000 + Math.random() * 3000)
    }
    after(burst, 500)
    return () => {
      pending.forEach(clearTimeout)
      setMeteors([])
    }
  }, [mode])

  const step = scene === 'check' && !done ? ITEMS[at].part : ''
  // 지금 항목: 누르는 면 · 키보드(Enter·Space)
  const part = (p: Part) => {
    const i = ITEMS.findIndex((x) => x.part === p)
    const now = scene === 'check' && i === at
    return {
      className: styles.part,
      'data-now': now,
      'data-done': i < at,
      role: now ? 'button' : undefined,
      tabIndex: now ? 0 : undefined,
      'aria-label': now ? ITEMS[i].question : undefined,
      onClick: () => confirm(i),
      onKeyDown: (e: KeyboardEvent) => {
        if (e.key !== 'Enter' && e.key !== ' ') return
        e.preventDefault()
        e.stopPropagation()
        confirm(i)
      },
    }
  }
  const c = (...names: string[]) => names.map((n) => styles[n]).join(' ')

  return (
    <div
      ref={stage}
      className={styles.stage}
      role={lit ? 'main' : undefined}
      aria-hidden={lit ? undefined : true}
      data-mode={mode}
      data-scene={scene}
      data-step={step}
      data-aligned={aligned}
      onClick={skipLight}
    >
      <div className={styles.world} data-zoom={!!zoom} style={zoom ?? undefined}>
        <div className={styles.art}>
          {/* 그림과 질문을 같은 비율의 판에 — 질문(HTML)이 그림 좌표에 맞게 놓이고 함께 커진다 */}
          <div ref={canvas} className={styles.canvas}>
            {/* 망원경·적도의·삼각대·노트북 선화. 누를 수 있는 것은 지금 항목과(다 확인하면) 노트북뿐 */}
            <svg viewBox={`0 0 ${VW} ${VH}`}>
              <defs>
                <radialGradient id="pf-glow">
                  <stop offset="0" className={styles.glowIn} />
                  <stop offset="1" className={styles.glowOut} />
                </radialGradient>
                <clipPath id="pf-beyond" clipPathUnits="userSpaceOnUse">
                  <path d={ABOVE_GROUND} />
                </clipPath>
                {/* 유성 꼬리: 머리(0)가 밝고 뒤(-METEOR_LEN)로 갈수록 사라진다 — 유성의 제 좌표계 기준 */}
                <linearGradient id="pf-meteor" gradientUnits="userSpaceOnUse" x1={-METEOR_LEN} y1="0" x2="0" y2="0">
                  <stop offset="0" className={styles.meteorTail} />
                  <stop offset="1" className={styles.meteorHead} />
                </linearGradient>
              </defs>
              {/* 부팅 하늘의 유성 — 땅보다 먼저 그려 땅이 가린다 */}
              {meteors.map((m) => (
                <g key={m.id} transform={`translate(${m.x.toFixed(1)} ${m.y.toFixed(1)}) rotate(${METEOR_ANGLE})`}>
                  <g
                    className={styles.meteor}
                    style={{ '--dist': `${m.dist.toFixed(0)}px`, '--len': m.len.toFixed(2), animationDuration: `${m.dur.toFixed(0)}ms`, animationDelay: `${m.delay.toFixed(0)}ms` } as CSSProperties}
                  >
                    <line x1={-METEOR_LEN} y1="0" x2="0" y2="0" />
                  </g>
                </g>
              ))}
              {/* 땅(구체): 면과 가장자리는 움직이지 않고, 잔디 줄이 구체를 따라 가장자리 쪽으로 흘러가 너머로 가라앉는다 (SPIN_VP 설명) */}
              <path className={styles.ground} d={GROUND} />
              {GRASS.map((row, i) => (
                <g
                  key={i}
                  ref={(el) => {
                    grassClip.current[i] = el
                  }}
                >
                  <g
                    ref={(el) => {
                      grassRows.current[i] = el
                    }}
                    style={SPIN_ORIGIN}
                  >
                    <path className={styles.grass} d={row.d} />
                  </g>
                </g>
              ))}
              <path className={styles.horizon} d={GROUND_TOP} />
              {/* 하늘 그림: 북극성이 든 작은곰자리 · 북극성을 가리키는 북두칠성 · 카시오페이아 — 배경 별처럼 부드럽게 빛나는 점 (2026-10-15 사용자).
                  부팅·프로필에는 없고, 프로필을 고르면 북극성부터 하나씩 켜진다. 좌표는 극축을 맞춘 뒤 자리, 맞추기 전에는 SKY_PIVOT을 중심으로 SKY_TURN도 돌아 있다 (북극성이 점선 오른쪽 아래에) */}
              <g className={styles.skyArt} style={{ transformOrigin: `${SKY_PIVOT[0]}px ${SKY_PIVOT[1]}px`, '--turn': `${SKY_TURN}deg` } as CSSProperties}>
                <g>
                  {CONSTELLATIONS.map((k) => (
                    <path key={k.name} className={c('ln', 'soft', 'thin', 'asterism')} d={k.lines.map((run) => 'M' + run.map((i) => `${k.stars[i][0]} ${k.stars[i][1]}`).join(' L')).join(' ')} />
                  ))}
                  {[...CONSTELLATIONS.flatMap((k) => k.stars), ...FIELD_STARS].map(([x, y, m], i) => (
                    <g key={i} className={styles.star} style={{ '--d': `${i * 38}ms` } as CSSProperties}>
                      <circle cx={x} cy={y} r={STAR_SIZE[m][1]} className={styles.starHalo} />
                      <circle cx={x} cy={y} r={STAR_SIZE[m][0]} className={styles.starCore} />
                    </g>
                  ))}
                  <text className={styles.lbl} x={POLARIS[0] + 27} y={POLARIS[1] - 8}>
                    북
                  </text>
                </g>
              </g>
              {/* 장비 전체(노트북까지): 부팅에는 카메라 뒤 — 화면 밖에서 8배. 구체가 돌면 발 둘레 잔디와 같은 배율로 들어와 제자리에 (roll) */}
              <g ref={rigArt} style={SPIN_ORIGIN}>
              {/* 장비: 그림 좌표를 1.16배로 (노트북 제외) */}
              <g transform="translate(80 338) scale(1.16)">
                {/* 삼각대: 굵은 다리(윗마디 · 아랫마디 · 잠금 · 발), 뒷다리는 흐리게 */}
                <g>
                  <g transform="translate(414 372) rotate(-4.5)" className={styles.backLeg}>
                    <rect className={c('ln', 'leg')} x="-4" y="93" width="8" height="76" rx="2" />
                    <rect className={c('ln', 'leg')} x="-6.5" y="0" width="13" height="97" rx="3" />
                    <rect className={c('ln', 'leg')} x="-8" y="87" width="16" height="12" rx="2.5" />
                    <rect className={c('ln', 'leg')} x="-7" y="168" width="14" height="9" rx="3" />
                  </g>
                  <g transform="translate(396 372) rotate(26.3)">
                    <rect className={c('ln', 'leg')} x="-4" y="115" width="8" height="94" rx="2" />
                    <rect className={c('ln', 'leg')} x="-6.5" y="0" width="13" height="119" rx="3" />
                    <rect className={c('ln', 'leg')} x="-8" y="109" width="16" height="12" rx="2.5" />
                    <rect className={c('ln', 'leg')} x="-7" y="208" width="14" height="9" rx="3" />
                  </g>
                  <g transform="translate(432 372) rotate(-27.3)">
                    <rect className={c('ln', 'leg')} x="-4" y="116" width="8" height="94" rx="2" />
                    <rect className={c('ln', 'leg')} x="-6.5" y="0" width="13" height="120" rx="3" />
                    <rect className={c('ln', 'leg')} x="-8" y="110" width="16" height="12" rx="2.5" />
                    <rect className={c('ln', 'leg')} x="-7" y="209" width="14" height="9" rx="3" />
                  </g>
                </g>

                {/* 망원경 방향(북쪽 · 홈 자세): 적도의 — 사용자 장비(UMi 17X, 하모닉이라 균형추 없음)를 닮게 (2026-10-15 사용자 사진 두 장).
                    받침판 · 둥근 위도 기둥(다이얼 · 이름판 · 전원 불)은 삼각대 가운데(x 414)에 맞춘다 · 극축을 따라 기운 몸통 — 그 위 경사면에 새들이 놓이고 경통이 올라간다.
                    뒤의 것을 앞의 면(바탕색)이 가리도록 받침 → 기둥 → 몸통 → 새들 차례 */}
                <g {...part('dir')}>
                  <rect className={styles.hit} x="364" y="262" width="116" height="112" rx="10" />
                  <rect className={c('ln', 'box')} x="368" y="360" width="92" height="12" rx="4" />
                  <path className={c('ln', 'box')} d="M384 360 L384 296 A30 30 0 0 1 444 296 L444 360 Z" />
                  <circle className={c('ln', 'thin')} cx="414" cy="326" r="9" />
                  <circle className={c('ln', 'thin')} cx="414" cy="326" r="2.5" />
                  <rect className={c('ln', 'thin')} x="400" y="341" width="28" height="11" rx="2.5" />
                  <g transform={`translate(374 210) rotate(${-POLE_TILT})`}>
                    <rect className={styles.hit} x="-50" y="34" width="140" height="80" rx="10" />
                    <rect className={c('ln', 'box')} x="-40" y="48" width="120" height="60" rx="12" />
                    <path className={c('ln', 'thin')} d="M-28 58 L68 58 M-26 64 L-31 74 M-19 64 L-24 74 M-12 64 L-17 74" />
                    {/* 새들: 몸통 가운데에 몸통보다 짧게 (2026-10-15 사용자 — 뒤로 길게 나와 있던 것을 줄임) */}
                    <rect className={c('ln', 'box')} x="-18" y="36" width="76" height="12" rx="2.5" />
                  </g>
                  <Badge x={519} y={232} />
                </g>

                {/* 망원경: 면은 바탕색으로 칠해 뒤(잔디·하늘)가 비치지 않게 (2026-10-15 사용자) — 고리는 경통을 칠한 뒤에 그린다 */}
                <g transform={`translate(374 210) rotate(${-POLE_TILT})`}>
                  <rect className={c('ln', 'box')} x="-82" y="26" width="164" height="10" rx="3" />
                  <rect className={c('ln', 'box')} x="-118" y="-26" width="236" height="52" rx="6" />
                  <path className={styles.ln} d="M-56 -30 L-56 30 M-44 -30 L-44 30 M50 -30 L50 30 M62 -30 L62 30" />
                  <rect className={c('ln', 'box')} x="118" y="-31" width="74" height="62" rx="5" />
                  <path className={c('ln', 'thin')} d="M126 -31 L126 31" />
                  <path className={c('ln', 'thin')} d="M-30 -26 L-30 -40 M50 -26 L50 -40" />
                  <rect className={c('ln', 'box')} x="-46" y="-56" width="118" height="16" rx="4" />
                  <rect className={c('ln', 'box')} x="-64" y="-58" width="18" height="20" rx="3" />
                </g>

                {/* 포커서 0점: 확인하면 드로튜브가 들어간다 */}
                <g {...part('focus')}>
                  <g transform={`translate(374 210) rotate(${-POLE_TILT})`}>
                    <rect className={styles.hit} x="-262" y="-54" width="150" height="96" rx="12" />
                    <rect className={c('ln', 'box')} x="-146" y="-18" width="28" height="36" rx="4" />
                    <rect className={c('ln', 'box')} x="-142" y="18" width="22" height="16" rx="3" />
                    <rect className={c('ln', 'box', 'tube')} x="-176" y="-12" width="32" height="24" rx="3" />
                    <g className={styles.drawtube}>
                      <path className={c('ln', 'thin')} d="M-170 -12 L-170 12" />
                      <rect className={c('ln', 'box')} x="-226" y="-30" width="50" height="58" rx="7" />
                      <rect className={c('ln', 'thin', 'box')} x="-218" y="-40" width="18" height="10" rx="2" />
                      <circle className={c('ln', 'thin', 'box')} cx="-190" cy="-36" r="3" />
                    </g>
                    <path className={c('inArrow', 'ln', 'hi')} d="M-182 -32 L-150 -32 M-158 -38 L-150 -32 L-158 -26" />
                  </g>
                  <Badge x={164} y={285} />
                </g>

                {/* 망원경 캡: 경통 끝을 덮는 깊은 캡(납작한 판이 아님) — 확인하면 가이드경 캡이 먼저 가볍게, 주경 캡이 조금 뒤에 천천히 빠지며 사라진다 */}
                <g {...part('caps')}>
                  <g transform={`translate(374 210) rotate(${-POLE_TILT})`}>
                    <rect className={styles.hit} x="170" y="-46" width="60" height="92" rx="12" />
                    <rect className={styles.hit} x="56" y="-70" width="40" height="44" rx="6" />
                    <g className={styles.cap}>
                      <rect className={c('ln', 'box')} x="180" y="-35" width="34" height="70" rx="8" />
                      <path className={c('ln', 'thin')} d="M187 -35 L187 35 M214 -12 L219 -12 L219 12 L214 12" />
                    </g>
                    <g className={styles.capGuide}>
                      <rect className={c('ln', 'box')} x="64" y="-61" width="22" height="26" rx="5" />
                      <path className={c('ln', 'thin')} d="M69 -61 L69 -35" />
                    </g>
                  </g>
                  <Badge x={574} y={95} />
                </g>

                {/* 적도의 전원: 전원 상자(적도의 오른쪽 바닥) → 선은 상자 왼쪽에서 오른쪽 다리 바깥으로 거의 곧게 올라가 적도의 몸통 앞 끝면으로 (2026-10-15 사용자 그림).
                    확인하면 위도 기둥의 전원 불과 상자의 불이 켜진다 */}
                <g {...part('power')}>
                  <rect className={styles.hit} x="544" y="496" width="120" height="82" rx="12" />
                  <circle className={styles.hit} cx="433" cy="326" r="14" />
                  <path className={c('ln', 'thin')} d="M556 546 C 532 548, 524 532, 525 506 C 527 440, 512 330, 507 262 Q 505 240, 494 236" />
                  <circle className={styles.led} cx="433" cy="326" r="4.5" />
                  <path className={styles.ln} d="M586 522 Q605 506 624 522" />
                  <rect className={c('ln', 'box')} x="556" y="522" width="96" height="46" rx="7" />
                  <path className={c('ln', 'thin')} d="M568 538 L594 538 M568 548 L586 548" />
                  <circle className={styles.led} cx="636" cy="540" r="4.5" />
                  <Badge x={662} y={514} />
                </g>
              </g>
              {/* 노트북: 다 확인하면 화면이 빛나고 역삼각형이 내려와 가리킨다. 누르면 화면 쪽으로 확대되며 다음 단계 */}
              <g transform="translate(220 230) scale(1.45)">
                <g
                  className={styles.laptop}
                  data-ready={done || !!zoom}
                  role={done ? 'button' : undefined}
                  tabIndex={done ? 0 : undefined}
                  aria-label={done ? '다음 단계로' : undefined}
                  onClick={enter}
                >
                  <rect className={c('ln', 'box')} x="600" y="512" width="150" height="56" rx="6" />
                  <path className={c('ln', 'thin')} d="M600 528 L750 528 M660 520 L690 520" />
                  <path className={c('ln', 'box')} d="M612 512 L738 512 L730 500 L620 500 Z" />
                  <rect className={c('ln', 'box')} x="626" y="428" width="100" height="70" rx="5" />
                  <rect ref={screenRect} className={styles.screenFill} x="628" y="430" width="96" height="66" rx="4" />
                  <path className={styles.uiLine} d="M636 442 L668 442 M636 452 L656 452 M684 442 L712 442 M684 450 L712 450 M684 458 L700 458" />
                  <circle cx="658" cy="476" r="6" fill="none" className={styles.uiLine} />
                  <g className={styles.pointer}>
                    <path d="M664 390 L688 390 L676 408 Z" />
                  </g>
                </g>
              </g>
              </g>
              {/* 극축 점선: 방향 단계가 되면 적도의에서 하늘로 한 점씩 길어지고, 확인하면 북극성에 닿은 채 남는다 */}
              <mask id="pf-ray" maskUnits="userSpaceOnUse" x="0" y="0" width={VW} height={VH}>
                <path className={styles.rayReveal} d={RAY} pathLength={1} />
              </mask>
              <path className={styles.ray} d={RAY} mask="url(#pf-ray)" />
              {/* 질문: 지금 항목만 — 그 부분에서 선을 뽑아 근처 여백에 */}
              {ITEMS.map((it) => (
                <g key={it.part} className={styles.leader} data-shown={step === it.part}>
                  <path d={it.leader} />
                  <circle cx={it.dot[0]} cy={it.dot[1]} r="4" />
                </g>
              ))}
            </svg>
            {/* 질문: SVG 안(foreignObject)에 두면 바뀔 때 그 칸이 잠깐 검은 사각형으로 그려졌다(2026-10-15 사용자, 아이라 창) → 그림 밖 HTML로 */}
            {ITEMS.map((it) => (
              <p
                key={it.part}
                className={styles.callout}
                data-shown={step === it.part}
                style={{ left: `${(it.at[0] / VW) * 100}%`, top: `${(it.at[1] / VH) * 100}%` }}
              >
                {it.question}
              </p>
            ))}
            {/* 부팅: 하늘에 반짝이는 AIRA 별자리 글자 · 아이라 · 영어 풀이 + 버전 — 차례로 나타나고(BOOT_AT), 키를 누르면 차례로 사라진다(CSS) */}
            <div
              className={styles.wordmark}
              style={{ left: `${(WORDMARK.x / VW) * 100}%`, top: `${(WORDMARK.y / VH) * 100}%`, width: `${(WORDMARK.w / VW) * 100}%`, ...BOOT_TIMING }}
            >
              <div ref={wordArt} dangerouslySetInnerHTML={{ __html: wordmark }} />
              {/* 게임 시작 화면처럼: 영어 풀이 뒤(BOOT_PROMPT_MS) 프로필 목록을 받았으면 나타나 천천히 깜빡인다. 자리는 처음부터 (입력은 App이 받는다) */}
              <p className={styles.prompt} data-shown={prompt}>
                Press Any Key
              </p>
              <p className={styles.version}>{VERSION}</p>
            </div>
          </div>
        </div>
        <h1 className={styles.title}>촬영 시작 전에 장비를 한 번 둘러봐 주세요</h1>
      </div>
      <p className={styles.live} aria-live="polite">
        {step ? ITEMS[at].question : done ? '모두 확인했어요. 노트북을 누르면 다음 단계로 갑니다.' : ''}
      </p>
    </div>
  )
}

/** 확인 표시 (V) */
function Badge({ x, y }: { x: number; y: number }) {
  return (
    <g className={styles.badge} transform={`translate(${x} ${y})`}>
      <circle r="13" />
      <path d="M-6 0 L-1.5 4.5 L6.5 -4.5" />
    </g>
  )
}
