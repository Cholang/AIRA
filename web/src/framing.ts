/**
 * 구도 회전 (⑤ 센터링 뒤, 2026-10-16 사용자 결정 — 서버 Prepare/Tasks/Center/Framing.cs, 화면 components/FrameRotate).
 * 각도는 모두 "화면에서 시계 방향 +", 지금 보이는 센터링 사진 기준.
 */

/**
 * [실기 확인] 화면의 사진이 카메라 뒤에서 본 모습(카메라 뒷면 화면과 같은 방향)이면, 틀을 시계 방향으로 돌린 구도 =
 * 카메라를 뒤에서 볼 때 시계 방향으로 돌림 (1). 실제로 반대로 돌아가면 −1
 */
export const CAMERA_TURN_SIGN = 1

/** -180 초과 ~ 180 이하로 */
export function normDeg(deg: number) {
  let d = deg % 360
  if (d > 180) d -= 360
  if (d <= -180) d += 360
  return d
}

/** 사진 가운데 안내에 쓸 방향과 크기 */
export function turnText(deg: number) {
  return { way: deg * CAMERA_TURN_SIGN > 0 ? '시계 방향' : '반시계 방향', amount: `${Math.round(Math.abs(deg))}°` }
}

async function post(url: string, body?: unknown): Promise<string | null> {
  try {
    const r = await fetch(url, {
      method: 'POST',
      headers: body === undefined ? undefined : { 'content-type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    if (r.ok) return null
    const b = (await r.json().catch(() => null)) as { error?: string } | null
    return b?.error ?? '구도를 저장하지 못했습니다.'
  } catch {
    return '내부 서버에 연결하지 못했습니다.'
  }
}

/** 원하는 구도: 지금 사진에서 틀을 돌린 각도. 허용 오차 안이면 서버가 지운다. 안 되면 이유 */
export const setFraming = (turnDeg: number) => post('/api/prepare/target/framing', { turnDeg })

/** 카메라를 돌린 뒤 다시 찍어(센터링) 남은 각도를 잰다. 안 되면 이유 */
export const checkFraming = () => post('/api/prepare/target/framing/check')
