// 데스크톱 창(WPF + WebView2, src/Astro.Desktop)과 주고받는 메시지.
// 브라우저에서 열었을 때는 창이 없으므로 아무 일도 하지 않는다.

interface HostWebView {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (e: { data: unknown }) => void): void
  removeEventListener(type: 'message', listener: (e: { data: unknown }) => void): void
}

function host(): HostWebView | undefined {
  return (window as unknown as { chrome?: { webview?: HostWebView } }).chrome?.webview
}

/** 데스크톱 창 안에서 열렸는지 (브라우저면 false) */
export const inDesktop = () => host() !== undefined

/** 데스크톱 창에 메시지 보내기: { type: 'theme' | 'close', ... } (MainWindow.xaml.cs가 받는다) */
export function tellHost(message: { type: string } & Record<string, unknown>) {
  host()?.postMessage(message)
}

/** 앱 끄기 (데스크톱 창만) */
export const closeApp = () => tellHost({ type: 'close' })

// 창의 × 버튼을 누르면 창이 "confirm-close"를 보낸다. 진행 중인 화면(단계 레일)이 확인 창을 띄우고, 없으면 바로 닫는다 (2026-10-08)
let confirmClose: (() => void) | null = null

/** 진행 중일 때 × 버튼에 확인 창을 띄울 함수 (null이면 바로 닫음) */
export function onCloseRequest(fn: (() => void) | null) {
  confirmClose = fn
}

host()?.addEventListener('message', (e) => {
  if ((e.data as { type?: string } | null)?.type !== 'confirm-close') return
  if (confirmClose) confirmClose()
  else closeApp()
})

/**
 * 실행 파일 고르기 창 (설정 화면의 "찾아보기" — 데스크톱 창만). 고른 전체 경로, 취소·브라우저면 null.
 * 웹 화면의 파일 선택은 전체 경로를 알려 주지 않아서 창(MainWindow.xaml.cs "pick-exe")이 고른다
 */
export function pickExe(name: string): Promise<string | null> {
  const h = host()
  if (!h) return Promise.resolve(null)
  return new Promise((resolve) => {
    const onMessage = (e: { data: unknown }) => {
      const d = e.data as { type?: string; name?: string; path?: string | null } | null
      if (d?.type !== 'picked-exe' || d.name !== name) return
      h.removeEventListener('message', onMessage)
      resolve(d.path ?? null)
    }
    h.addEventListener('message', onMessage)
    h.postMessage({ type: 'pick-exe', name })
  })
}

/** 데스크톱 창이 보낸 메시지 받기 (type이 같은 것만). 브라우저면 아무 일도 없다. 돌려준 함수로 그만 받는다 */
export function onHostMessage<T>(type: string, fn: (data: T) => void): () => void {
  const h = host()
  if (!h) return () => {}
  const listener = (e: { data: unknown }) => {
    if ((e.data as { type?: string } | null)?.type === type) fn(e.data as T)
  }
  h.addEventListener('message', listener)
  return () => h.removeEventListener('message', listener)
}
