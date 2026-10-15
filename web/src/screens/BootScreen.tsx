import { PRODUCT } from '../product'
import styles from './BootScreen.module.css'

/**
 * 부팅 화면: 로고(AIRA 별자리 글자 · 아이라 · 영어 풀이)·"Press Any Key"·버전은 땅과 장비 선화 장면의 하늘에 있다(RigScene, 2026-10-15 사용자 — 부팅 · 프로필 · 점검이 같은 장면).
 * 여기는 서버 연결 실패만 그 아래에, 그리고 화면 읽기용 안내(장면은 읽지 않게 숨김)
 */
export default function BootScreen({ ready, error }: { ready: boolean; error?: string | null }) {
  return (
    <main className={styles.boot} aria-label={`${PRODUCT.reul} 시작하는 중`}>
      <p className={styles.live} aria-live="polite">
        {ready ? '아무 키나 누르면 시작합니다' : ''}
      </p>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </main>
  )
}
