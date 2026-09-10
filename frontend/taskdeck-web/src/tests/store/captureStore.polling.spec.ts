import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { captureApi } from '../../api/captureApi'
import { useCaptureStore } from '../../store/captureStore'
import type { CaptureItem, CaptureTriageStatus } from '../../types/capture'

const counts = vi.hoisted(() => vi.fn(async () => {}))
vi.mock('../../api/captureApi', () => ({ captureApi: {
  getStatus: vi.fn(), getItem: vi.fn(), enqueueTriage: vi.fn(),
} }))
vi.mock('../../store/workspaceStore', () => ({ useWorkspaceStore: () => ({ refreshWorkloadCounts: counts }) }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ success: vi.fn(), error: vi.fn() }) }))
const detail = (id: string, status: CaptureItem['status'] = 'Triaging'): CaptureItem => ({
  id, status, userId: 'owner', boardId: 'board', source: 'Typed', textExcerpt: 'source',
  rawText: 'private source', processedAt: null, createdAt: '2026-09-10', retryCount: 0,
})
const status = (id: string, value: CaptureItem['status'] = 'Triaging'): CaptureTriageStatus => ({
  id, status: value, processedAt: null, errorMessage: null, disposition: null, canEditSuggestion: false,
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(r => { resolve = r })
  return { promise, resolve }
}

describe('ordinary Inbox triage status polling', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.resetAllMocks()
    setActivePinia(createPinia())
    vi.mocked(captureApi.getStatus).mockImplementation(async id => status(id))
    vi.mocked(captureApi.getItem).mockImplementation(async id => detail(id, 'ProposalCreated'))
    vi.mocked(captureApi.enqueueTriage).mockImplementation(async id => ({ id, status: 'Triaging', alreadyTriaging: false }))
  })
  afterEach(() => { useCaptureStore().stopTriagePolling(); vi.useRealTimers() })

  it('watches A then B fairly with one active read and status-only nonterminal requests', async () => {
    const store = useCaptureStore()
    const first = deferred<CaptureTriageStatus>()
    vi.mocked(captureApi.getStatus).mockReturnValueOnce(first.promise)
    store.pollTriageCompletion('A')
    store.pollTriageCompletion('B')
    await vi.advanceTimersByTimeAsync(2000)
    expect(captureApi.getStatus).toHaveBeenCalledTimes(1)
    expect(store.triagePollingItemIds).toEqual(new Set(['A', 'B']))
    first.resolve(status('A'))
    await vi.advanceTimersByTimeAsync(1)
    expect(vi.mocked(captureApi.getStatus).mock.calls.map(c => c[0])).toEqual(['A', 'B'])
    expect(captureApi.getItem).not.toHaveBeenCalled()
    expect(store.items).toEqual([])
  })

  it('continues beyond the former 450-attempt limit and never expires long provider work', async () => {
    const store = useCaptureStore()
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(450 * 30_000)
    expect(captureApi.getStatus).toHaveBeenCalledTimes(453)
    expect(store.triagePollingItemIds.has('A')).toBe(true)
  })

  it('patches only status fields of existing rows, preserving source, order and membership', async () => {
    const store = useCaptureStore()
    store.items = [detail('B'), detail('A')]
    store.detailById.A = detail('A')
    vi.mocked(captureApi.getStatus).mockResolvedValue({ ...status('A'), canEditSuggestion: true, rawText: 'injected' } as CaptureTriageStatus)
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(store.items.map(i => i.id)).toEqual(['B', 'A'])
    expect(store.detailById.A?.rawText).toBe('private source')
    expect(store.detailById.A?.canEditSuggestion).toBe(true)
    store.items = []
    await vi.advanceTimersByTimeAsync(4000)
    expect(store.items).toEqual([])
  })

  it.each(['Triaged', 'ProposalCreated', 'Converted', 'Ignored', 'Failed'] as const)('confirms fresh %s detail and notifies exactly once', async terminal => {
    const store = useCaptureStore()
    store.detailById.A = detail('A', terminal)
    const fresh = deferred<CaptureItem>()
    vi.mocked(captureApi.getStatus).mockResolvedValue(status('A', terminal))
    vi.mocked(captureApi.getItem).mockReturnValue(fresh.promise)
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(store.triagePollingItemIds.has('A')).toBe(true)
    expect(counts).not.toHaveBeenCalled()
    fresh.resolve({ ...detail('A', terminal), rawText: 'fresh source' })
    await vi.advanceTimersByTimeAsync(1)
    expect(store.detailById.A?.rawText).toBe('fresh source')
    expect(store.items).toEqual([])
    expect(store.triagePollingItemIds.size).toBe(0)
    await vi.advanceTimersByTimeAsync(60_000)
    expect(counts).toHaveBeenCalledTimes(1)
  })

  it('keeps watching when terminal status is followed by fresh nonterminal detail', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.getStatus).mockResolvedValue(status('A', 'Triaged'))
    vi.mocked(captureApi.getItem).mockResolvedValue(detail('A'))
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(store.triagePollingItemIds.has('A')).toBe(true)
    expect(counts).not.toHaveBeenCalled()
  })

  it('invalidates an old terminal hydration when the same item is enqueued again', async () => {
    const store = useCaptureStore()
    store.detailById.A = detail('A')
    const old = deferred<CaptureItem>()
    vi.mocked(captureApi.getStatus).mockResolvedValueOnce(status('A', 'Triaged'))
    vi.mocked(captureApi.getItem).mockReturnValueOnce(old.promise)
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(2000)
    await store.triageItem('A')
    old.resolve(detail('A', 'Failed'))
    await vi.advanceTimersByTimeAsync(1)
    expect(store.detailById.A?.status).toBe('Triaging')
    expect(store.triagePollingItemIds.has('A')).toBe(true)
    expect(counts).toHaveBeenCalledTimes(1) // enqueue only
  })

  it.each(['status', 'detail'])('deadlines a hanging %s request, aborts it and ignores its late result', async phase => {
    const store = useCaptureStore()
    store.detailById.A = detail('A')
    const old = deferred<CaptureItem>()
    if (phase === 'status') vi.mocked(captureApi.getStatus).mockReturnValueOnce(old.promise)
    else {
      vi.mocked(captureApi.getStatus).mockResolvedValueOnce(status('A', 'Triaged'))
      vi.mocked(captureApi.getItem).mockReturnValueOnce(old.promise)
    }
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(12_000)
    const read = phase === 'status' ? captureApi.getStatus : captureApi.getItem
    expect(vi.mocked(read).mock.calls[0]?.[1]?.signal?.aborted).toBe(true)
    expect(store.triagePollingProblems.A).toBe('retrying')
    old.resolve({ ...detail('A', 'Failed'), rawText: 'late' })
    await vi.advanceTimersByTimeAsync(4000)
    expect(store.detailById.A?.rawText).toBe('private source')
    expect(store.triagePollingProblems.A).toBeUndefined()
    expect(store.triagePollingItemIds.has('A')).toBe(true)
  })

  it.each([403, 404])('retires only the unavailable row on %s and permits an explicit retry', async code => {
    const store = useCaptureStore()
    vi.mocked(captureApi.getStatus).mockRejectedValueOnce({ response: { status: code } })
    store.pollTriageCompletion('A'); store.pollTriageCompletion('B')
    await vi.advanceTimersByTimeAsync(2001)
    expect(store.triagePollingProblems.A).toBe('unavailable')
    expect(store.triagePollingItemIds).toEqual(new Set(['B']))
    store.retryTriagePolling()
    expect(store.triagePollingItemIds).toEqual(new Set(['B', 'A']))
  })

  it('pauses all status checks on 401 until session reset', async () => {
    const store = useCaptureStore()
    vi.mocked(captureApi.getStatus).mockRejectedValueOnce({ response: { status: 401 } })
    store.pollTriageCompletion('A'); store.pollTriageCompletion('B')
    await vi.advanceTimersByTimeAsync(2001)
    expect(store.triagePollingPaused).toBe(true)
    expect(store.triagePollingItemIds.size).toBe(0)
    store.retryTriagePolling(); store.pollTriageCompletion('C')
    await vi.advanceTimersByTimeAsync(60_000)
    expect(captureApi.getStatus).toHaveBeenCalledTimes(1)
    store.resetForLogout()
    expect(store.triagePollingPaused).toBe(false)
  })

  it.each([undefined, 500])('keeps accepted work and visibly retries transient %s errors', async code => {
    const store = useCaptureStore()
    vi.mocked(captureApi.getStatus).mockRejectedValueOnce({ response: { status: code } })
    await store.triageItem('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(store.actionError).toBeNull()
    expect(store.triagePollingProblems.A).toBe('retrying')
    await vi.advanceTimersByTimeAsync(4000)
    expect(store.triagePollingProblems.A).toBeUndefined()
    expect(store.triagePollingItemIds.has('A')).toBe(true)
  })

  it.each(['stopTriagePolling', 'resetForLogout'] as const)('%s aborts pending reads and invalidates late responses', async stop => {
    const store = useCaptureStore()
    store.detailById.A = detail('A')
    const pending = deferred<CaptureTriageStatus>()
    vi.mocked(captureApi.getStatus).mockReturnValueOnce(pending.promise)
    store.pollTriageCompletion('A')
    await vi.advanceTimersByTimeAsync(2000)
    store[stop]()
    expect(vi.mocked(captureApi.getStatus).mock.calls[0]?.[1]?.signal?.aborted).toBe(true)
    pending.resolve(status('A', 'Failed'))
    await vi.advanceTimersByTimeAsync(60_000)
    expect(store.detailById.A?.status).toBe('Triaging')
    expect(store.triagePollingItemIds.size).toBe(0)
    expect(counts).not.toHaveBeenCalled()
  })

  it('does not start a watch when enqueue completes after a scope exit', async () => {
    const store = useCaptureStore()
    const accepted = deferred<{ id: string; status: 'Triaging'; alreadyTriaging: boolean }>()
    vi.mocked(captureApi.enqueueTriage).mockReturnValueOnce(accepted.promise)
    const action = store.triageItem('A')
    store.stopTriagePolling()
    accepted.resolve({ id: 'A', status: 'Triaging', alreadyTriaging: false })
    await action
    expect(store.triagePollingItemIds.size).toBe(0)
  })
})
