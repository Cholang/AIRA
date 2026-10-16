import { BUTTON } from '../buttonText'
import { AlertTriangle, Check, RotateCw } from 'lucide-react'
import { useCallback, useEffect, useState, type CSSProperties } from 'react'
import ConfirmDialog from '../components/ConfirmDialog'
import DeviceIcon from '../components/DeviceIcon'
import type { CheckItem } from '../checks'
import { useKeepInside } from '../keepInside'
import { PRODUCT } from '../product'
import { HUB, selectable, type Planet } from './equipmentOrbits'
import styles from './EquipmentScreen.module.css'

/** 장비 연결 화면의 부품: 행성·편집 행성·드라이버 목록·카메라 전원·상태 글·[임시] 실패 버튼 (큰 파일 분리 2026-10-16 — 옮기기만) */

const WAITING_FOR_HUB = '전원 허브가 연결되면 확인합니다'

/**
 * 연결 모드의 행성 하나. 등록된 장비: 장비 아이콘(오른쪽 아래에 연결 표시 V·!) + 제품 이름.
 * 등록 안 된 장비: 같은 자리·크기의 흐린 점선 행성, 아이콘만.
 * 연결 안 된 행성(실패·경고)을 가리키면 가운데에 다시 연결 아이콘 (준필수는 그 아래 "없이 진행")
 */
export function PlanetNode({
  item,
  planet,
  style,
  picked,
  onPick,
  onRetry,
  onWithout,
}: {
  item: CheckItem
  planet: Planet
  style: CSSProperties
  picked: boolean
  onPick: () => void
  onRetry: () => void
  onWithout: () => void
}) {
  const absent = item.status === 'Absent'
  const canPick = selectable(item)
  return (
    // 행성 안에 버튼(다시 연결·없이 진행)이 들어가므로 행성 자체는 div + role=button
    <div
      role="button"
      className={styles.node}
      data-status={item.status}
      data-absent={absent}
      data-selectable={canPick}
      data-hub={item.id === HUB}
      data-selected={picked}
      style={style}
      title={item.title}
      onClick={onPick}
      onKeyDown={(e) => {
        if (e.target !== e.currentTarget || (e.key !== 'Enter' && e.key !== ' ')) return
        e.preventDefault()
        onPick()
      }}
      tabIndex={canPick ? 0 : -1}
      aria-disabled={!canPick}
      aria-pressed={canPick ? picked : undefined}
      aria-label={absent ? `${item.title}: 등록되지 않음` : `${item.title} ${item.term ?? ''}: ${statusText(item)}`}
    >
      {/* 목성: 내용 아래 희미한 대적점 */}
      {planet.spot && !absent && <span className={styles.spot} aria-hidden="true" />}
      <span className={styles.devWrap}>
        <DeviceIcon kind={item.id} className={styles.dev} />
        {/* 연결 표시는 행성 안 자리가 모자라 장비 아이콘 오른쪽 아래에 겹쳐서 (2026-10-11) */}
        {!absent && (
          <span className={styles.mark} data-shown={item.status === 'Pass' || item.status === 'Fail' || item.status === 'Warn'} aria-hidden="true">
            {item.status === 'Pass' && <Check strokeWidth={3} />}
            {(item.status === 'Fail' || item.status === 'Warn') && <AlertTriangle strokeWidth={2.5} />}
          </span>
        )}
      </span>
      {!absent && <span className={styles.name}>{item.term}</span>}
      {canPick && (
        <span className={styles.hoverBody} data-without={item.status === 'Fail' && item.severity === 'Recommended'}>
          <button
            type="button"
            className={styles.retryIcon}
            onClick={(e) => {
              e.stopPropagation()
              onRetry()
            }}
            aria-label={`${item.title} 다시 연결`}
          >
            <RotateCw strokeWidth={2} />
          </button>
          {item.status === 'Fail' && item.severity === 'Recommended' && (
            <button
              type="button"
              className={styles.textAction}
              onClick={(e) => {
                e.stopPropagation()
                onWithout()
              }}
            >
              없이 진행
            </button>
          )}
        </span>
      )}
    </div>
  )
}

/**
 * 변경 모드의 행성 (자리·크기는 연결 모드와 같다).
 * - 누르면(selected) 행성 옆에 드라이버 목록이 바로 열리고, 한 번 더 누르면 닫히며 normal로 돌아간다
 * - 누른 행성(목록이 열림)에만 이름 아래에 "제거"가 보인다 (등록된 선택·준필수 장비만). 가리키기만 해서는 안 보인다
 */
export function EditNode({
  item,
  style,
  left,
  listOpen,
  error,
  onToggleList,
  onCloseList,
  onSelect,
}: {
  item: CheckItem
  style: CSSProperties
  /** 오른쪽에 자리가 없으면 목록을 왼쪽으로 연다 */
  left: boolean
  listOpen: boolean
  /** 바꾸기·제거에 실패했을 때 행성 아래에 보여 줄 문장 (원래 장비는 그대로) */
  error: string | null
  onToggleList: () => void
  onCloseList: () => void
  onSelect: (device: { id: string; name: string } | null) => void
}) {
  const absent = item.status === 'Absent'
  const canRemove = !absent && item.severity !== 'Required'
  return (
    <div className={styles.editNode} data-absent={absent} data-selected={listOpen} data-list-host={listOpen ? 'open' : undefined} style={style}>
      <button
        type="button"
        className={styles.editCircle}
        onClick={onToggleList}
        aria-expanded={listOpen}
        aria-label={`${item.title}: ${absent ? '등록되지 않음. 눌러서 등록' : `${item.term ?? ''}. 눌러서 변경`}`}
      >
        <DeviceIcon kind={item.id} />
      </button>
      <div className={styles.editLabel}>
        <b>{item.title}</b>
        {!absent && <small>{item.term}</small>}
      </div>
      {canRemove && (
        <div className={styles.actions}>
          <button type="button" className={styles.textAction} onClick={() => onSelect(null)}>
            제거
          </button>
        </div>
      )}
      {error && (
        <p className={styles.editError} role="alert">
          {error}
        </p>
      )}
      {listOpen && <DeviceList kind={item.id} left={left} current={item.term} onPick={onSelect} onClose={onCloseList} />}
    </div>
  )
}

/** 설치된 드라이버 목록 (N.I.N.A.가 이 PC에서 찾은 것). 행성 옆에 작게 열린다 */
export function DeviceList({
  kind,
  left,
  current,
  onPick,
  onClose,
}: {
  kind: string
  left: boolean
  current: string | null
  onPick: (device: { id: string; name: string }) => void
  onClose: () => void
}) {
  const [devices, setDevices] = useState<{ id: string; name: string }[] | null>(null)
  const [error, setError] = useState(false)
  const keep = useKeepInside<HTMLDivElement>()
  const load = useCallback(() => {
    setDevices(null)
    setError(false)
    fetch(`/api/equipment/devices/${kind}`)
      .then((r) => (r.ok ? r.json() : Promise.reject()))
      .then(setDevices, () => setError(true))
  }, [kind])
  useEffect(load, [load])

  // Esc로 닫기
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div ref={keep} className={styles.deviceList} data-left={left} role="listbox" aria-label="설치된 드라이버">
      {devices === null && !error && <p className={styles.listNote}>드라이버 목록을 읽는 중입니다</p>}
      {error && <p className={styles.listNote}>N.I.N.A.에서 목록을 받지 못했습니다</p>}
      {devices?.map((d) => (
        <button key={d.id} type="button" role="option" aria-selected={d.name === current} onClick={() => onPick(d)}>
          {d.name}
        </button>
      ))}
      <p className={styles.listNote}>
        목록에 없나요? 제조사 드라이버를 설치한 뒤{' '}
        <button type="button" className={styles.textAction} onClick={load}>
          새로고침
        </button>
      </p>
      {kind === 'camera' && <CameraPower />}
    </div>
  )
}

/** "Regulated 0-13.2V adjustable DC2: DC2" → "DC2" */
const shortOutlet = (name: string) => (name.includes(':') ? name.slice(name.lastIndexOf(':') + 1).trim() : name)

/**
 * 카메라 전원 (docs/implementation/PRECHECK.md J05, 2026-10-09): 배터리 또는 전원 허브 출력. 출력을 고르면 장비 연결 때 카메라가 안 보이면 아이라가 그 출력을 켠다.
 * 아이라는 출력 전압을 읽거나 바꾸지 못하므로, 고를 때 Empire에서 전압을 맞췄는지 한 번 확인받는다 (실기: 7~8.4V 어댑터를 12V 출력에 꽂아 켜지지 않음)
 */
export function CameraPower() {
  const [state, setState] = useState<{ outlet: string | null; outlets: string[] } | null>(null)
  const [asking, setAsking] = useState<string | null>(null)
  useEffect(() => {
    fetch('/api/equipment/camera-power')
      .then((r) => (r.ok ? r.json() : null))
      .then(setState, () => setState(null))
  }, [])
  const save = (outlet: string | null) =>
    void fetch('/api/equipment/camera-power', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ outlet }) })
      .then((r) => (r.ok ? r.json() : null))
      .then((b: { outlet: string | null } | null) => b && setState((s) => (s ? { ...s, outlet: b.outlet } : s)))
  if (!state) return null
  return (
    <div className={styles.powerPick}>
      <p className={styles.listNote}>카메라 전원</p>
      <div className={styles.powerChoices} role="radiogroup" aria-label="카메라 전원">
        <button type="button" role="radio" aria-checked={state.outlet === null} onClick={() => save(null)}>
          배터리
        </button>
        {state.outlets.map((o) => (
          <button key={o} type="button" role="radio" aria-checked={state.outlet === o} onClick={() => state.outlet !== o && setAsking(o)}>
            허브 {shortOutlet(o)}
          </button>
        ))}
      </div>
      {state.outlets.length === 0 && <p className={styles.listNote}>전원 허브가 연결되면 허브 출력도 고를 수 있습니다</p>}
      <ConfirmDialog
        open={asking !== null}
        message={`Wanderer Empire에서 ${asking ? shortOutlet(asking) : ''} 출력 전압을 카메라 전원 어댑터에 맞게(어댑터에 적힌 입력 범위 안, 예: 8V) 맞췄나요? 전압이 맞지 않으면 카메라나 어댑터가 상할 수 있습니다. ${PRODUCT.neun} 전압을 바꾸지 않고 켜고 끄기만 합니다.`}
        confirmLabel={BUTTON.done}
        size="md"
        onConfirm={() => {
          save(asking)
          setAsking(null)
        }}
        onCancel={() => setAsking(null)}
      />
    </div>
  )
}

function statusText(item: CheckItem) {
  switch (item.status) {
    case 'Pass':
      return '연결되어 있습니다'
    case 'Fail':
      return item.message || '연결되어 있지 않습니다'
    case 'Running':
      return '연결하는 중입니다'
    case 'Skipped':
      return '전원 허브를 먼저 연결해 주세요'
    case 'Warn':
      return item.message
    default:
      return '연결 대기 중입니다'
  }
}

/**
 * [임시] 화면 설계용: 장비마다 "연결 실패로 만들기" 버튼.
 * 실패 결과는 서버가 실제 실패와 같은 모양으로 만든다 (Equipment:Simulate일 때만).
 * 되돌리기는 "다시 연결" — 시뮬레이션에서는 다시 연결에 성공한다.
 */
export function TempFailButtons({
  items,
  done,
  patch,
}: {
  items: CheckItem[]
  done: boolean
  patch: (fn: (prev: CheckItem[]) => CheckItem[]) => void
}) {
  // 모의 장비일 때만 (실장비에서는 이 버튼이 실제 다시 연결이 되어 헷갈림 — 2026-10-08 시뮬레이터 전체 시험에서 발견)
  const [simulated, setSimulated] = useState(false)
  useEffect(() => {
    let alive = true
    fetch('/api/equipment/simulated')
      .then((r) => (r.ok ? (r.json() as Promise<{ simulated: boolean }>) : null))
      .then((b) => alive && setSimulated(!!b?.simulated))
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])
  const fail = async (id: string) => {
    const res = await fetch(`/api/equipment/connect/${id}?simulateFail=true`)
    if (!res.ok) return
    const result = (await res.json()) as CheckItem
    patch((prev) =>
      prev.map((i) =>
        i.id === id
          ? result
          : id === HUB
            ? { ...i, status: i.status === 'Absent' ? i.status : ('Skipped' as const), message: WAITING_FOR_HUB, diagnosis: null }
            : i,
      ),
    )
  }

  const connected = items.filter((i) => i.status === 'Pass')
  if (!simulated || !done || connected.length === 0) return null
  return (
    <div className={styles.temp}>
      <span>연결 실패로 만들기 (임시)</span>
      {connected.map((i) => (
        <button key={i.id} type="button" onClick={() => fail(i.id)}>
          {i.title}
        </button>
      ))}
    </div>
  )
}
