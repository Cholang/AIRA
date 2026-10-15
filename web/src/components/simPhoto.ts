/**
 * [모의] 센터링 사진 대신 쓰는 그림 (실장비는 N.I.N.A.가 찍은 사진): 별밭 + 가운데 길쭉한 은하, 3:2.
 * rotate = 카메라 방향(°) — 모의 장비에서 카메라를 돌리면(구도 회전) 하늘이 사진 안에서 돌아 보인다.
 * 사진이라 색은 고정 (촬영용 적색 테마에서는 다른 사진처럼 img에 빨강 필터)
 */
export function simPhoto(rotate: number): string {
  const W = 1500
  const H = 1000
  let seed = 7
  const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647
  const stars = Array.from({ length: 460 }, () => {
    // 돌려도 빈 곳이 없게 사진보다 넓게 뿌린다
    const x = rnd() * W * 1.6 - W * 0.3
    const y = rnd() * W * 1.2 - (W * 1.2 - H) / 2
    const m = rnd()
    const r = m > 0.97 ? 3.2 : m > 0.85 ? 1.9 : 1.1
    return `<circle cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" r="${r}" fill="#fff" opacity="${(0.45 + rnd() * 0.55).toFixed(2)}"/>`
  }).join('')
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">` +
    `<defs><radialGradient id="g"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset=".3" stop-color="#d8d0c2" stop-opacity=".45"/><stop offset="1" stop-color="#8b8070" stop-opacity="0"/></radialGradient></defs>` +
    `<rect width="${W}" height="${H}" fill="#08090b"/>` +
    `<g transform="rotate(${rotate.toFixed(1)} ${W / 2} ${H / 2})">${stars}<ellipse cx="${W / 2}" cy="${H / 2}" rx="340" ry="90" fill="url(#g)"/></g>` +
    `</svg>`
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}
