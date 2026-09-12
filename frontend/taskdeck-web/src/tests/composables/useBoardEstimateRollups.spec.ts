import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, reactive, ref } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { useBoardEstimateRollups } from '../../composables/useBoardEstimateRollups'
import { estimateRollupsApi } from '../../api/estimateRollupsApi'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'
import type { BoardEstimateRollup } from '../../types/estimateRollups'

vi.mock('../../api/estimateRollupsApi', () => ({ estimateRollupsApi: { get: vi.fn() } }))
const session = reactive({ userId: 'owner', token: 'session' })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))

function fixture(boardId = 'board', knownEstimateMinutes = 90): BoardEstimateRollup {
  const totals = { cardCount: 2, knownEstimateMinutes, missingEstimateCount: 1 }
  return { boardId, generatedAt: '2026-09-12T20:00:00Z', board: totals, unassigned: totals,
    columns: [], participants: [] }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}
const wrappers: VueWrapper[] = []
function create() {
  const boardId = ref('board')
  const revision = ref(0)
  let api!: ReturnType<typeof useBoardEstimateRollups>
  const wrapper = mount(defineComponent({
    setup() { api = useBoardEstimateRollups(boardId, revision); return () => null },
  }))
  wrappers.push(wrapper)
  return { api, boardId, revision, wrapper }
}
function signal(index = 0): AbortSignal {
  const result = vi.mocked(estimateRollupsApi.get).mock.calls[index]?.[1]?.signal
  expect(result).toBeInstanceOf(AbortSignal)
  return result!
}

describe('estimate read ownership and recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    session.userId = 'owner'; session.token = 'session'
    vi.mocked(estimateRollupsApi.get).mockReset().mockResolvedValue(fixture())
  })
  afterEach(() => {
    wrappers.splice(0).forEach(wrapper => wrapper.unmount())
    vi.useRealTimers()
  })

  it('does not start reads while closed or without a user', async () => {
    const { api } = create()
    await api.refresh()
    session.userId = ''
    api.toggle()
    await api.refresh()
    expect(estimateRollupsApi.get).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('aborts on close and reopens with a fresh request', async () => {
    const first = deferred<BoardEstimateRollup>()
    vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(first.promise)
    const { api } = create()
    api.toggle()
    const oldSignal = signal()
    api.toggle()
    expect(oldSignal.aborted).toBe(true)
    expect(api.loading.value).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
    api.toggle()
    expect(signal(1)).not.toBe(oldSignal)
    await flushPromises()
    first.resolve(fixture('board', 500))
    await flushPromises()
    expect(api.rollup.value?.board.knownEstimateMinutes).toBe(90)
    expect(api.error.value).toBeNull()
  })

  for (const boundary of ['board', 'user', 'token', 'revision', 'unmount'] as const) {
    it.each(['success', 'failure'])(`aborts at ${boundary} and ignores late %s`, async outcome => {
      const pending = deferred<BoardEstimateRollup>()
      vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(pending.promise)
      const { api, boardId, revision, wrapper } = create()
      api.toggle()
      const requestSignal = signal()
      if (boundary === 'board') boardId.value = 'other'
      if (boundary === 'user') session.userId = 'other'
      if (boundary === 'token') session.token = 'rotated'
      if (boundary === 'revision') revision.value++
      if (boundary === 'unmount') wrapper.unmount()
      expect(requestSignal.aborted).toBe(true)
      expect(vi.getTimerCount()).toBe(0)
      if (outcome === 'success') pending.resolve(fixture())
      else pending.reject(new Error('Obsolete failure'))
      await flushPromises()
      expect(api.rollup.value).toBeNull()
      expect(api.error.value).toBeNull()
      if (boundary === 'revision') {
        expect(api.stale.value).toBe(true)
        expect(api.open.value).toBe(true)
        expect(api.loading.value).toBe(false)
      } else if (boundary !== 'unmount') expect(api.open.value).toBe(false)
      expect(estimateRollupsApi.get).toHaveBeenCalledTimes(1)
    })
  }

  it.each(['success', 'failure'])('replacing a read ignores the old %s and its finally block', async outcome => {
    const first = deferred<BoardEstimateRollup>()
    const second = deferred<BoardEstimateRollup>()
    vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { api } = create()
    api.toggle()
    const next = api.refresh()
    expect(signal().aborted).toBe(true)
    if (outcome === 'success') first.resolve(fixture('board', 500))
    else first.reject(new Error('Obsolete failure'))
    await flushPromises()
    expect(api.loading.value).toBe(true)
    expect(api.rollup.value).toBeNull()
    expect(api.error.value).toBeNull()
    expect(vi.getTimerCount()).toBe(1)
    second.resolve(fixture())
    await next
    expect(api.rollup.value?.board.knownEstimateMinutes).toBe(90)
    expect(api.loading.value).toBe(false)
    expect(vi.getTimerCount()).toBe(0)
  })

  it.each(['success', 'failure'])('bounds an unresponsive transport and ignores its late %s after retry', async outcome => {
    const pending = deferred<BoardEstimateRollup>()
    vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(pending.promise)
    const { api } = create()
    api.toggle()
    await vi.advanceTimersByTimeAsync(BOARD_REQUEST_TIMEOUT_MS - 1)
    expect(api.loading.value).toBe(true)
    await vi.advanceTimersByTimeAsync(1)
    expect(signal().aborted).toBe(true)
    expect(api.loading.value).toBe(false)
    expect(api.error.value).toContain('timed out')
    expect(api.rollup.value).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
    expect(estimateRollupsApi.get).toHaveBeenCalledTimes(1)
    await api.refresh()
    if (outcome === 'success') pending.resolve(fixture('board', 500))
    else pending.reject(new Error('Late timeout receipt'))
    await flushPromises()
    expect(api.rollup.value?.board.knownEstimateMinutes).toBe(90)
    expect(api.error.value).toBeNull()
    expect(api.loading.value).toBe(false)
  })

  it.each(['success', 'failure'])('clears the deadline on normal %s settlement', async outcome => {
    if (outcome === 'failure') vi.mocked(estimateRollupsApi.get).mockRejectedValueOnce(new Error('Offline'))
    const { api } = create()
    api.toggle()
    await flushPromises()
    expect(vi.getTimerCount()).toBe(0)
    const result = api.rollup.value
    const message = api.error.value
    await vi.advanceTimersByTimeAsync(BOARD_REQUEST_TIMEOUT_MS)
    expect(api.rollup.value).toBe(result)
    expect(api.error.value).toBe(message)
    expect(api.loading.value).toBe(false)
  })
})
