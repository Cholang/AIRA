import { T } from './uiText'
/**
 * 버튼 단어 표준 (DESIGN.md "버튼 단어 표준", 2026-10-17 사용자 결정) — 같은 뜻이면 늘 같은 단어.
 * 무엇이 되는지·이유·주의는 버튼 밖 설명(버튼 레이아웃 영역 `PrepCenter`의 prompt)에. 단어는 화면 문구 표(src/Astro.Core/UiText.json의 button.*) —
 * 서버(Prepare/Flow/ButtonText.cs)도 같은 키를 읽는다 (2026-10-18)
 */
export const BUTTON = {
  /** 방금 실패한 것을 다시 */
  retry: T('button.retry'),
  /** 경고·실패를 알고 지금 상태로 넘어감 */
  proceed: T('button.proceed'),
  /** 이 단계를 하지 않고 다음으로 */
  skip: T('button.skip'),
  /** 사람이 손으로 한 일을 끝냄 (할 일은 설명에) */
  done: T('button.done'),
  /** 장비 상태를 다시 읽음 */
  recheck: T('button.recheck'),
  /** 끝난 단계를 처음부터 다시 */
  redo: T('button.redo'),
  /** 하지 않고 창 닫기 */
  cancel: T('button.cancel'),
  /** 알림·설정 창 닫기 */
  close: T('button.close'),
  /** 이전 화면으로 */
  back: T('button.back'),
} as const
