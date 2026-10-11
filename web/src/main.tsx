import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AppError, ErrorBoundary } from './components/ErrorBoundary'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/* 화면 영역 밖(상태 줄 등)까지 실패해도 종료 절차는 쓸 수 있게 (자체 점검 H3) */}
    <ErrorBoundary name="앱" fallback={(e) => <AppError error={e} />}>
      <App />
    </ErrorBoundary>
  </StrictMode>,
)
