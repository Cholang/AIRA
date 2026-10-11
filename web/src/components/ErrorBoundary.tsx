import { Component, type ErrorInfo, type ReactNode } from 'react'
import { logEvent } from '../log'
import { PRODUCT } from '../product'
import Button from './Button'
import styles from './ErrorBoundary.module.css'
import { QuitButton, QuitProvider } from './QuitControl'

/**
 * 화면 오류 격리 (2026-10-11 자체 점검 H3): 한 구역을 그리다 예외가 나도 앱 전체가 빈 화면이 되지 않게 그 구역만 대신 그린다.
 * 그전에는 어느 화면이든 예외 하나로 앱 전체가 내려가 상태 줄·레일·종료 버튼이 사라졌고, 창 ×가 종료 절차(추적 끄기 등) 없이 바로 닫았다.
 * 오류는 기록 파일에 "화면 오류"로 남긴다. resetKey가 바뀌면(다른 화면으로 넘어감) 다시 그려 본다
 */
export class ErrorBoundary extends Component<
  { name: string; resetKey?: unknown; fallback: (error: Error, reset: () => void) => ReactNode; children: ReactNode },
  { error: Error | null }
> {
  state: { error: Error | null } = { error: null }

  static getDerivedStateFromError(error: Error) {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    const where = info.componentStack?.split('\n').find((l) => l.trim().length > 0)?.trim() ?? ''
    logEvent(`화면 오류 (${this.props.name}): ${error.message} ${where}`.slice(0, 480))
  }

  componentDidUpdate(prev: { resetKey?: unknown }) {
    if (this.state.error && prev.resetKey !== this.props.resetKey) this.setState({ error: null })
  }

  reset = () => this.setState({ error: null })

  render() {
    return this.state.error ? this.props.fallback(this.state.error, this.reset) : this.props.children
  }
}

/** 화면 영역 하나가 실패했을 때: 그 자리만. 상태 줄·레일·종료는 그대로 쓸 수 있다 */
export function ScreenError({ error, onRetry }: { error: Error; onRetry: () => void }) {
  return (
    <div className={styles.screen} role="alert">
      <h2>이 화면을 그리지 못했습니다</h2>
      <p>장비는 그대로입니다. 다시 그려 보고, 계속 안 되면 오른쪽 아래 종료로 끝낸 뒤 다시 켜 주세요.</p>
      <small>{error.message}</small>
      <Button variant="primary" onClick={onRetry}>
        다시 그리기
      </Button>
    </div>
  )
}

/**
 * 앱 전체가 실패했을 때 (화면 영역 밖 — 상태 줄 등): 종료 절차만큼은 쓸 수 있게 종료 버튼과 창 × 확인을 다시 둔다
 */
export function AppError({ error }: { error: Error }) {
  return (
    <QuitProvider stage={null} confirmOnClose>
      <div className={styles.app} role="alert">
        <h2>{PRODUCT.ga} 화면을 그리지 못했습니다</h2>
        <p>장비는 그대로입니다. 종료를 누르면 평소처럼 작업을 멈추고 추적을 끈 뒤 닫습니다. 다시 켜면 진행 기록에서 이어서 할 수 있어요.</p>
        <small>{error.message}</small>
        <div className={styles.row}>
          <Button onClick={() => location.reload()}>다시 불러오기</Button>
          <QuitButton />
        </div>
      </div>
    </QuitProvider>
  )
}
