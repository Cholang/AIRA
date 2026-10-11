import { useEffect, useRef } from 'react'
import styles from './SkyBackdrop.module.css'
import { PAN, skyPan } from '../skyPan'

/**
 * 앱 전체 배경의 은은한 효과 (2026-10-09 사용자 선택 — Codex 시안 mockups/aira-background.html "성운과 별", 세기 1.1):
 * 성운 두 층이 서로 다른 방향으로 천천히 흐르고, 별은 잠깐씩 나타나 빛났다 사라지며(동시에 6개까지), 혜성 하나가 약 30분에 걸쳐 지나간다.
 *
 * 2026-10-11 비용 줄이기 (Gemini 리뷰, 사용자 동의 — 세 번째 방법): 매 프레임 그리지 않는다.
 * 성운 무늬·고정된 별은 **한 번만** 그림으로 그리고(테마·창 크기가 바뀔 때만), 움직임(흐름·돌기·커짐, 반짝임, 혜성)은
 * 브라우저가 그래픽 하드웨어로 처리하는 CSS 애니메이션(transform·opacity)으로만 한다 → 자바스크립트가 프레임마다 돌지 않는다.
 * 동작 줄이기면 멈춘 한 장(반짝임·혜성 없음). 다크(촬영) 테마는 붉은빛으로 더 약하게, 움직임은 그대로. 창이 가려지면 브라우저가 알아서 멈춘다
 */
const LEVEL = 1.1

type Theme = 'dark' | 'night'
const THEMES_ALL: Theme[] = ['dark', 'night']

/** 성운 두 층의 색 */
const NEBULA: Record<Theme, [number, number, number][]> = {
  dark: [[92, 132, 174], [147, 105, 157]],
  night: [[110, 0, 0], [80, 0, 0]],
}
/** 고정된 별의 색 (r, g, b) — 반짝이는 별·혜성은 CSS의 --star (SkyBackdrop.module.css) */
const STAR: Record<Theme, string> = { dark: '235, 232, 227', night: '150, 0, 0' }
const GLINTS = 6

const themeOf = (): Theme => (document.documentElement.dataset.theme === 'night' ? 'night' : 'dark')
/** 테마별 세기 (촬영 테마는 더 약하게) */
const levelOf = (t: Theme) => LEVEL * (t === 'night' ? 0.55 : 1)

const rand = (a: number, b: number) => a + Math.random() * (b - a)
const hash = (x: number, y: number) => {
  const n = Math.sin(x * 127.1 + y * 311.7) * 43758.5453
  return n - Math.floor(n)
}
function noise(x: number, y: number) {
  const ix = Math.floor(x), iy = Math.floor(y)
  let u = x - ix, v = y - iy
  u = u * u * (3 - 2 * u)
  v = v * v * (3 - 2 * v)
  return (hash(ix, iy) * (1 - u) + hash(ix + 1, iy) * u) * (1 - v) + (hash(ix, iy + 1) * (1 - u) + hash(ix + 1, iy + 1) * u) * v
}
function fbm(x: number, y: number) {
  let n = 0, a = 0.5
  for (let i = 0; i < 5; i++) {
    n += a * noise(x, y)
    x = x * 2.03 + 13
    y = y * 2.03 + 7
    a *= 0.5
  }
  return n
}

/** 성운 한 층의 무늬 (640×400, 화면에서 늘려 보인다) */
function nebula(index: number, theme: Theme) {
  const c = document.createElement('canvas')
  c.width = 640
  c.height = 400
  const g = c.getContext('2d')!
  const d = g.createImageData(640, 400)
  const [r, gr, b] = NEBULA[theme][index]
  const gain = 240
  for (let y = 0; y < 400; y++)
    for (let x = 0; x < 640; x++) {
      const u = x / 640, v = y / 400
      const wx = u * 4 + index * 17, wy = v * 3 + index * 9
      const warp = fbm(wx, wy), n = fbm(wx + warp * 2, wy + warp * 1.4)
      const band = Math.exp(-Math.pow((v - (0.67 - 0.4 * u + 0.12 * Math.sin(u * 5 + index * 2))) / 0.25, 2))
      const edge = Math.pow(Math.sin(Math.PI * u) * Math.sin(Math.PI * v), 0.65)
      const i = (y * 640 + x) * 4
      d.data[i] = r
      d.data[i + 1] = gr
      d.data[i + 2] = b
      d.data[i + 3] = Math.min(255, Math.pow(Math.max(0, n - 0.24), 1.65) * band * edge * gain)
    }
  g.putImageData(d, 0, 0)
  return c
}

export default function SkyBackdrop() {
  const root = useRef<HTMLDivElement>(null)
  const clouds = [useRef<HTMLCanvasElement>(null), useRef<HTMLCanvasElement>(null)]
  const starsRef = useRef<HTMLCanvasElement>(null)
  const glintsRef = useRef<HTMLDivElement>(null)
  const cometRef = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    const el = root.current, starCv = starsRef.current, glintBox = glintsRef.current, comet = cometRef.current
    const layers = clouds.map((r) => r.current)
    if (!el || !starCv || !glintBox || !comet || layers.some((c) => !c)) return
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)')
    let theme = themeOf()
    let stars: { x: number; y: number; r: number; a: number }[] = []

    // 멈춤: 동작 줄이기(Windows 애니메이션 효과 끔)일 때만. 다크 테마도 움직인다 — 그래픽 하드웨어로 옮기기만 해 밤새 켜도 부담이 작아서
    // (2026-10-11: 처음엔 배터리 때문에 다크 테마에서 멈췄다가, 그리는 방식을 바꾼 뒤 사용자 동의로 되돌림)
    const applyStill = () => {
      el.dataset.still = String(reduce.matches)
      if (el.dataset.still === 'true') cometAnim?.pause()
      else cometAnim?.play()
      el.style.setProperty('--k', String(levelOf(theme)))
    }

    // 고정된 별: 창 크기·테마가 바뀔 때만 한 번 그린다
    const drawStars = () => {
      const W = window.innerWidth * PAN, H = window.innerHeight * PAN // 밀어도 비지 않게 창보다 크게
      const d = Math.min(window.devicePixelRatio || 1, 1.5)
      starCv.width = Math.round(W * d)
      starCv.height = Math.round(H * d)
      const ctx = starCv.getContext('2d')
      if (!ctx) return
      ctx.setTransform(d, 0, 0, d, 0, 0)
      ctx.clearRect(0, 0, W, H)
      const k = levelOf(theme)
      for (const s of stars) {
        ctx.fillStyle = `rgba(${STAR[theme]},${s.a * k})`
        ctx.beginPath()
        ctx.arc(s.x * W, s.y * H, s.r, 0, Math.PI * 2)
        ctx.fill()
      }
    }

    // 혜성: 왼쪽 아래 → 오른쪽 위로 28~32분, 지날 때마다 새 길. 곡선을 transform 40칸으로 나눠 그래픽 하드웨어가 움직이게 한다
    // (CSS offset-path는 프레임마다 스타일 계산을 일으켰다 — 2026-10-11 측정)
    let cometAnim: Animation | null = null
    const newComet = () => {
      cometAnim?.cancel()
      const W = window.innerWidth, H = window.innerHeight
      const x0 = rand(0.04, 0.12) * W, y0 = rand(0.82, 0.94) * H, x1 = rand(0.4, 0.55) * W, y1 = rand(0.28, 0.48) * H
      const x2 = rand(1.12, 1.22) * W, y2 = rand(-0.24, -0.16) * H
      comet.style.setProperty('--tail', `${Math.min(72, W * 0.055, H * 0.09)}px`)
      const frames = Array.from({ length: 41 }, (_, i) => {
        const u = i / 40, v = 1 - u
        const x = v * v * x0 + 2 * v * u * x1 + u * u * x2
        const y = v * v * y0 + 2 * v * u * y1 + u * u * y2
        const ang = Math.atan2(v * (y1 - y0) + u * (y2 - y1), v * (x1 - x0) + u * (x2 - x1))
        return { transform: `translate(${x}px, ${y}px) rotate(${ang}rad)`, opacity: i === 0 ? 0 : 1, offset: u }
      })
      cometAnim = comet.animate(frames, { duration: rand(28, 32) * 60_000, easing: 'linear' })
      cometAnim.onfinish = newComet
      if (el.dataset.still === 'true') cometAnim.pause()
    }

    // 반짝이는 별: 6개가 각자 다른 길이로 빛났다 사라지고, 한 번 돌 때마다 다른 자리로
    const glints = Array.from(glintBox.children) as HTMLElement[]
    const placeGlint = (g: HTMLElement, first = false) => {
      // 지금 보이는 자리(밀린 만큼 비켜서) 안에 — 판은 창의 PAN배
      const { x, y } = skyPan()
      const W = window.innerWidth, H = window.innerHeight
      g.style.left = `${((-x + (rand(6, 94) / 100) * W) / (W * PAN)) * 100}%`
      g.style.top = `${((-y + (rand(8, 92) / 100) * H) / (H * PAN)) * 100}%`
      g.style.setProperty('--r', `${rand(0.85, 1.3)}px`)
      g.style.animationDuration = `${rand(5, 9) + rand(1.4, 2.6) * GLINTS}s` // 빛나는 시간 + 쉬는 시간 (평균 2초에 하나꼴)
      if (first) g.style.animationDelay = `${-rand(0, 20)}s`
    }
    glints.forEach((g) => {
      placeGlint(g, true)
      g.addEventListener('animationiteration', () => placeGlint(g))
    })

    const resize = () => {
      const W = window.innerWidth, H = window.innerHeight
      stars = Array.from({ length: Math.min(130 * PAN * PAN, Math.round((W * H * PAN * PAN) / 15000)) }, () => ({ x: Math.random(), y: Math.random(), r: rand(0.45, 1), a: rand(0.08, 0.25) }))
      drawStars()
      newComet()
    }

    // 성운 무늬는 무거워(수백 ms) 첫 화면이 그려진 뒤에 만들고, 다 되면 천천히 나타난다.
    // 두 테마의 무늬를 미리 만들어 두어 테마를 바꾸면 바탕색과 같은 순간에 바뀐다 (2026-10-10)
    const made: Partial<Record<Theme, HTMLCanvasElement[]>> = {}
    const cloudsOf = (t: Theme) => (made[t] ??= [nebula(0, t), nebula(1, t)])
    const paintClouds = () => {
      const src = cloudsOf(theme)
      layers.forEach((c, i) => {
        const g = c!.getContext('2d')
        if (!g) return
        g.clearRect(0, 0, 640, 400)
        g.drawImage(src[i], 0, 0)
      })
    }
    let timer = window.setTimeout(() => {
      paintClouds()
      el.dataset.ready = 'true'
      // 다른 테마 무늬는 조금 뒤 한가할 때
      timer = window.setTimeout(() => THEMES_ALL.forEach(cloudsOf), 1500)
    }, 300)

    const themeWatch = new MutationObserver(() => {
      if (themeOf() === theme) return
      theme = themeOf()
      if (el.dataset.ready === 'true') paintClouds()
      drawStars()
      applyStill()
    })
    themeWatch.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    window.addEventListener('resize', resize)
    reduce.addEventListener('change', applyStill)
    applyStill()
    resize()
    return () => {
      window.clearTimeout(timer)
      themeWatch.disconnect()
      cometAnim?.cancel()
      window.removeEventListener('resize', resize)
      reduce.removeEventListener('change', applyStill)
    }
    // 그림 층(ref)은 처음 한 번만 잡는다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div ref={root} className={styles.sky} aria-hidden="true">
      {/* 카메라가 움직이는 연출(2026-10-11): 성운·별·반짝이는 별은 창보다 크게(오른쪽·아래로 PAN배) 그려 두고 --sky-pan-x/y만큼 함께 민다 (skyPan.ts).
          혜성은 밀지 않는다 — 시간 표시 역할 */}
      <div className={styles.pan}>
      {/* 성운 두 층: 바깥(가로 흐름) → 안(세로 흐름) → 돌기 → 그림(커짐). 모두 transform이라 그래픽 하드웨어가 합성 */}
      {[0, 1].map((i) => (
        <div key={i} className={styles.driftX} data-layer={i}>
          <div className={styles.driftY}>
            <div className={styles.spin}>
              <canvas ref={clouds[i]} className={styles.cloud} width={640} height={400} />
            </div>
          </div>
        </div>
      ))}
      <canvas ref={starsRef} className={styles.stars} />
      <div ref={glintsRef} className={styles.glints}>
        {Array.from({ length: GLINTS }, (_, i) => (
          <span key={i} />
        ))}
      </div>
      </div>
      <span ref={cometRef} className={styles.comet} />
    </div>
  )
}
