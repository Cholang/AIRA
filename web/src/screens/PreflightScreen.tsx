import { useCallback, useContext, useEffect, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { scaleOf, useFrame } from '../frame'
import { ScreenReady } from '../screenReady'
import { setSkyIntro } from '../skyPan'
import styles from './PreflightScreen.module.css'

type Part = 'focus' | 'power' | 'dir' | 'caps'

/**
 * 점검 네 가지 (OPERATION_PROCEDURE.md 4장 — 설치하는 차례). 질문은 그 부분에서 뽑은 선 끝의 여백에 짧게(at = 그림 좌표),
 * leader = 그 부분에서 질문까지의 선, dot = 선의 시작점
 */
const ITEMS: { part: Part; question: string; at: [number, number]; leader: string; dot: [number, number] }[] = [
  { part: 'focus', question: '포커서 노브를 끝까지 넣어 0에 맞췄나요?', at: [96, 478], leader: 'M342 724 L262 552', dot: [342, 724] },
  { part: 'power', question: '적도의 전원을 켰나요?', at: [96, 876], leader: 'M404 940 L300 902', dot: [404, 940] },
  { part: 'dir', question: '적도의를 북쪽으로 향하게 세우고 홈 자세로 두었나요?', at: [820, 590], leader: 'M590 712 L812 624', dot: [590, 712] },
  { part: 'caps', question: '망원경 뚜껑을 모두 열었나요?', at: [480, 340], leader: 'M684 474 L640 412', dot: [684, 474] },
]

// 동작 (DESIGN.md "출발 전 점검", 시안 mockups/aira-preflight-motion.html)
const RISE_MS = 2000 // 장비가 아래에서 올라와 멈출 때까지 (CSS .art 0.55초 뒤 1.3초)
const ZOOM_MS = 820 // 노트북 화면 쪽으로 확대된 뒤 다음 단계로
const DROP = 360 // 카메라가 내려오는 만큼 배경이 위로 (틀 px)
const ALIGN_X = -200 // 극축 방향을 확인하면 하늘이 왼쪽으로 밀려 북극성이 점선에 걸린다 (틀 px)

const reduced = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

/**
 * 출발 전 점검: 아이라가 스스로 알 수 없는 것만 묻는다 (2026-10-15 카드덱 → 장비 선화 한 장, 프로필 바로 다음).
 * 들어오면 카메라가 밤하늘에서 내려오듯 배경이 위로 흐르고 장비가 아래에서 올라와 멈춘다(누르면 끝으로).
 * 지금 항목만 강조색으로 숨 쉬고, 그 부분의 그림을 누르면 확인 — 그림이 바뀐다(드로튜브가 들어감 · 전원 불 · 극축 점선이 북극성에 · 캡이 빠짐).
 * 다 확인하면 노트북 화면이 빛나고 역삼각형이 내려와 가리킨다. 노트북(또는 Enter)을 누르면 화면 쪽으로 확대되며 다음 단계.
 * 버튼은 없다 — 시험용 넘기기는 진행 표시 아래의 [임시] 건너뛰기(App)
 */
export default function PreflightScreen({ onContinue }: { onContinue: () => void }) {
  const ready = useContext(ScreenReady)
  const frame = useFrame()
  // 들어오기가 끝났나(장비가 올라와 멈춤) · 노트북으로 확대 중
  const [risen, setRisen] = useState(false)
  const [at, setAt] = useState(0)
  const [zoom, setZoom] = useState<CSSProperties | null>(null)
  const stage = useRef<HTMLElement>(null)
  const screenRect = useRef<SVGRectElement>(null)
  const timers = useRef<number[]>([])
  const later = useCallback((fn: () => void, ms: number) => {
    timers.current.push(window.setTimeout(fn, reduced() ? 0 : ms))
  }, [])
  useEffect(() => () => timers.current.forEach(clearTimeout), [])

  const scene = zoom ? 'zoom' : risen ? 'check' : ready ? 'rise' : 'wait'

  // 들어오기: 전환이 끝난 뒤 배경을 위로(카메라가 내려옴), 장비가 올라와 멈추면 점검. 들어오는 중에 누르면 끝으로
  const px = frame.fixed ? frame.scale : 1
  useEffect(() => {
    if (!ready) return
    setSkyIntro(0, -DROP * px)
    later(() => setRisen(true), RISE_MS)
  }, [ready, px, later])
  const skipIntro = () => ready && setRisen(true)

  const done = at >= ITEMS.length
  const aligned = ITEMS.findIndex((x) => x.part === 'dir') < at
  useEffect(() => {
    if (aligned) setSkyIntro(ALIGN_X * px, -DROP * px)
  }, [aligned, px])

  const confirm = (i: number) => {
    if (scene === 'check' && i === at) setAt((a) => a + 1)
  }

  // 노트북 화면 쪽으로 확대 → 다음 단계
  const enter = useCallback(() => {
    if (scene !== 'check' || !done || !stage.current || !screenRect.current) return
    if (reduced()) return onContinue()
    const s = scaleOf(stage.current)
    const a = stage.current.getBoundingClientRect()
    const r = screenRect.current.getBoundingClientRect()
    const cx = (r.left + r.width / 2 - a.left) / s
    const cy = (r.top + r.height / 2 - a.top) / s
    const k = (a.width / r.width) * 1.15
    setZoom({ transformOrigin: `${cx}px ${cy}px`, transform: `translate(${a.width / s / 2 - cx}px, ${a.height / s / 2 - cy}px) scale(${k})`, opacity: 0 })
    later(onContinue, ZOOM_MS)
  }, [scene, done, onContinue, later])
  useEffect(() => {
    if (!done || scene !== 'check') return
    const key = (e: globalThis.KeyboardEvent) => e.key === 'Enter' && enter()
    window.addEventListener('keydown', key)
    return () => window.removeEventListener('keydown', key)
  }, [done, scene, enter])

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
    <main ref={stage} className={styles.stage} data-scene={scene} data-step={step} data-aligned={aligned} onClick={skipIntro}>
      <div className={styles.world} data-zoom={!!zoom} style={zoom ?? undefined}>
        <div className={styles.art}>
          {/* 망원경·적도의·삼각대·노트북 선화. 누를 수 있는 것은 지금 항목과(다 확인하면) 노트북뿐 */}
          <svg viewBox="0 0 1770 1156">
            <defs>
              <path id="pf-spark" d="M0-10 C1.2-10 1.1-5.8 2.9-3.1 C4.4-1.1 8-1.1 8 0 C8 1.1 4.4 1.1 2.9 3.1 C1.1 5.8 1.2 10 0 10 C-1.2 10-1.1 5.8-2.9 3.1 C-4.4 1.1-8 1.1-8 0 C-8-1.1-4.4-1.1-2.9-3.1 C-1.1-5.8-1.2-10 0-10 Z" />
            </defs>
            {/* 땅: 오른쪽으로 완만하게 내려감, 화면 끝까지 */}
            <path className={styles.ground} d="M-40 930 Q900 965 1990 1086 L1990 1400 L-40 1400 Z" />
            <path className={styles.horizon} d="M-40 930 Q900 965 1990 1086" />
            <path className={c('ln', 'soft', 'thin')} d="M190 990 q5 -14 10 0 M204 989 q4 -10 8 0 M1480 1080 q5 -14 10 0 M1494 1079 q4 -10 8 0 M60 1010 q4 -10 8 0 M1660 1100 q4 -12 8 0" />
            {/* 하늘 그림: 별자리 · 반짝임 · 북극성 — 정렬 전에는 북극성이 오른쪽으로 치우쳐 있다 */}
            <g className={styles.skyArt}>
              <path className={c('ln', 'soft', 'thin')} d="M300 300 L405 254 L497 300 L541 387 M497 300 L596 286" />
              <circle className={styles.starDot} cx="300" cy="300" r="2.4" />
              <circle className={styles.starDot} cx="405" cy="254" r="2.4" />
              <circle className={styles.starDot} cx="497" cy="300" r="2.6" />
              <circle className={styles.starDot} cx="541" cy="387" r="2.2" />
              <circle className={styles.starDot} cx="596" cy="286" r="2.2" />
              <use href="#pf-spark" className={styles.spark} transform="translate(760 330) scale(.7)" opacity=".7" />
              <use href="#pf-spark" className={styles.spark} transform="translate(1410 560) scale(.62)" opacity=".6" />
              <use href="#pf-spark" className={styles.spark} transform="translate(1561 103) scale(1.6)" />
              <text className={styles.lbl} x="1588" y="95">
                북
              </text>
            </g>
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
                <path className={c('ln', 'soft')} d="M348 469 L421 460 L482 469 M348 469 L415 482 L482 469" />
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

              {/* 망원경 방향(북쪽 · 홈 자세): 적도의 머리 */}
              <g {...part('dir')}>
                <rect className={styles.hit} x="362" y="296" width="104" height="84" rx="10" />
                <path className={styles.ln} d="M386 350 L436 313 L448 362 L380 362 Z" />
                <path className={styles.ln} d="M372 362 L456 362 L456 372 L372 372 Z" />
                <g transform="translate(360 245) rotate(-37)">
                  <rect className={styles.hit} x="-74" y="30" width="128" height="80" rx="10" />
                  <rect className={styles.ln} x="-62" y="68" width="104" height="34" rx="8" />
                  <path className={c('ln', 'thin')} d="M-40 68 L-40 102 M22 68 L22 102" />
                  <rect className={styles.ln} x="-22" y="36" width="44" height="32" rx="6" />
                  <circle className={c('ln', 'thin')} cx="0" cy="52" r="7" />
                </g>
                <Badge x={470} y={336} />
              </g>

              {/* 망원경 */}
              <g transform="translate(360 245) rotate(-37)">
                <rect className={styles.ln} x="-82" y="26" width="164" height="10" rx="3" />
                <path className={styles.ln} d="M-56 -30 L-56 30 M-44 -30 L-44 30 M50 -30 L50 30 M62 -30 L62 30" />
                <rect className={styles.ln} x="-118" y="-26" width="236" height="52" rx="6" />
                <rect className={styles.ln} x="118" y="-31" width="74" height="62" rx="5" />
                <path className={c('ln', 'thin')} d="M126 -31 L126 31" />
                <path className={c('ln', 'thin')} d="M-30 -26 L-30 -40 M50 -26 L50 -40" />
                <rect className={styles.ln} x="-46" y="-56" width="118" height="16" rx="4" />
                <rect className={styles.ln} x="-64" y="-58" width="18" height="20" rx="3" />
              </g>

              {/* 포커서 0점: 확인하면 드로튜브가 들어간다 */}
              <g {...part('focus')}>
                <g transform="translate(360 245) rotate(-37)">
                  <rect className={styles.hit} x="-262" y="-54" width="150" height="96" rx="12" />
                  <rect className={styles.ln} x="-146" y="-18" width="28" height="36" rx="4" />
                  <rect className={styles.ln} x="-142" y="18" width="22" height="16" rx="3" />
                  <rect className={c('ln', 'tube')} x="-176" y="-12" width="32" height="24" rx="3" />
                  <g className={styles.drawtube}>
                    <path className={c('ln', 'thin')} d="M-170 -12 L-170 12" />
                    <rect className={styles.ln} x="-226" y="-30" width="50" height="58" rx="7" />
                    <rect className={c('ln', 'thin')} x="-218" y="-40" width="18" height="10" rx="2" />
                    <circle className={c('ln', 'thin')} cx="-190" cy="-36" r="3" />
                  </g>
                  <path className={c('inArrow', 'ln', 'hi')} d="M-182 -32 L-150 -32 M-158 -38 L-150 -32 L-158 -26" />
                </g>
                <Badge x={150} y={320} />
              </g>

              {/* 망원경 캡: 확인하면 빠지며 사라진다 */}
              <g {...part('caps')}>
                <g transform="translate(360 245) rotate(-37)">
                  <rect className={styles.hit} x="112" y="-44" width="114" height="88" rx="12" />
                  <rect className={styles.hit} x="58" y="-68" width="34" height="38" rx="6" />
                  <g className={styles.cap}>
                    <rect className={styles.ln} x="192" y="-35" width="12" height="70" rx="4" />
                    <path className={c('ln', 'thin')} d="M204 -10 L210 -10 L210 10 L204 10" />
                  </g>
                  <g className={styles.cap}>
                    <rect className={styles.ln} x="72" y="-59" width="8" height="22" rx="3" />
                  </g>
                </g>
                <Badge x={560} y={130} />
              </g>

              {/* 적도의 전원: 단자 불 + 케이블 + 전원 상자 (왼쪽 다리 앞). 확인하면 불이 켜진다 */}
              <g {...part('power')}>
                <rect className={styles.hit} x="234" y="496" width="120" height="82" rx="12" />
                <circle className={styles.hit} cx="435" cy="295" r="16" />
                <path className={c('ln', 'thin')} d="M435 299 C 420 360, 330 430, 300 522" />
                <circle className={styles.led} cx="435" cy="295" r="4.5" />
                <path className={styles.ln} d="M276 522 Q295 506 314 522" />
                <rect className={c('ln', 'box')} x="246" y="522" width="96" height="46" rx="7" />
                <path className={c('ln', 'thin')} d="M258 538 L284 538 M258 548 L276 548" />
                <circle className={styles.led} cx="326" cy="540" r="4.5" />
                <Badge x={352} y={514} />
              </g>
            </g>
            {/* 케이블: 적도의 → 노트북 */}
            <path className={c('ln', 'soft', 'thin')} d="M596 721 C 760 800, 950 900, 1110 955" />
            {/* 노트북: 다 확인하면 화면이 빛나고 역삼각형이 내려와 가리킨다. 누르면 화면 쪽으로 확대되며 다음 단계 */}
            <g transform="translate(220 230) scale(1.45)">
              <g
                className={styles.laptop}
                data-ready={done}
                role={done ? 'button' : undefined}
                tabIndex={done ? 0 : undefined}
                aria-label={done ? '다음 단계로' : undefined}
                onClick={enter}
              >
                <rect className={c('ln', 'box')} x="600" y="512" width="150" height="56" rx="6" />
                <path className={c('ln', 'thin')} d="M600 528 L750 528 M660 520 L690 520" />
                <path className={c('ln', 'box')} d="M612 512 L738 512 L730 500 L620 500 Z" />
                <rect ref={screenRect} className={styles.screenFill} x="628" y="430" width="96" height="66" rx="4" />
                <rect className={styles.ln} x="626" y="428" width="100" height="70" rx="5" />
                <path className={styles.uiLine} d="M636 442 L668 442 M636 452 L656 452 M684 442 L712 442 M684 450 L712 450 M684 458 L700 458" />
                <circle cx="658" cy="476" r="6" fill="none" className={styles.uiLine} />
                <g className={styles.pointer}>
                  <path d="M664 390 L688 390 L676 408 Z" />
                </g>
              </g>
            </g>
            {/* 극축 점선: 방향 단계가 되면 적도의에서 하늘로 한 점씩 길어지고, 확인하면 북극성에 닿은 채 남는다 */}
            <mask id="pf-ray" maskUnits="userSpaceOnUse" x="0" y="0" width="1770" height="1156">
              <path className={styles.rayReveal} d="M563 705 L1401 73" pathLength={1} />
            </mask>
            <path className={styles.ray} d="M563 705 L1401 73" mask="url(#pf-ray)" />
            {/* 질문: 지금 항목만 — 그 부분에서 선을 뽑아 근처 여백에 */}
            {ITEMS.map((it) => (
              <g key={it.part} className={styles.leader} data-shown={step === it.part}>
                <path d={it.leader} />
                <circle cx={it.dot[0]} cy={it.dot[1]} r="4" />
                <foreignObject x={it.at[0]} y={it.at[1]} width="300" height="140">
                  <p className={styles.callout}>{it.question}</p>
                </foreignObject>
              </g>
            ))}
          </svg>
        </div>
        <h1 className={styles.title}>촬영 시작 전에 장비를 한 번 둘러봐 주세요</h1>
      </div>
      <p className={styles.live} aria-live="polite">
        {step ? ITEMS[at].question : done ? '모두 확인했어요. 노트북을 누르면 다음 단계로 갑니다.' : ''}
      </p>
    </main>
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
