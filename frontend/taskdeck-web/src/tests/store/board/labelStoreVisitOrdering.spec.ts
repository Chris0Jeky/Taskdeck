import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

const { mockLabelsApi } = vi.hoisted(() => ({
  mockLabelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

vi.mock('../../../api/labelsApi', () => ({ labelsApi: mockLabelsApi }))

import { createLabelActions } from '../../../store/board/labelStore'

interface TestLabel {
  id: string
  boardId: string
  name: string
  colorHex: string
  createdAt: string
  updatedAt: string
}

const originalLabel: TestLabel = {
  id: 'lbl-1',
  boardId: 'board-1',
  name: 'Bug',
  colorHex: '#f00',
  createdAt: '2026-09-20T10:00:00Z',
  updatedAt: '2026-09-20T10:00:00Z',
}

function createState() {
  return {
    currentBoard: ref<{ id: string } | null>({ id: 'board-1' }),
    currentBoardLabels: ref<TestLabel[]>([{ ...originalLabel }]),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn() },
    markBoardDetailMutation: vi.fn(),
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((settle) => {
    resolve = settle
  })
  return { promise, resolve }
}

describe('labelStore visit and settlement ordering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockLabelsApi.getLabels.mockReset()
    mockLabelsApi.updateLabel.mockReset()
  })

  it('does not let an earlier A visit overwrite the authoritative A read after A to B to A', async () => {
    const state = createState()
    const helpers = createHelpers()
    const oldVisitRead = deferred<TestLabel[]>()
    const reopenedRead = deferred<TestLabel[]>()
    mockLabelsApi.getLabels
      .mockReturnValueOnce(oldVisitRead.promise)
      .mockReturnValueOnce(reopenedRead.promise)
    const { fetchLabels } = createLabelActions(state as never, helpers as never)

    const pendingOldRead = fetchLabels('board-1')

    state.currentBoard.value = { id: 'board-2' }
    state.currentBoardLabels.value = []
    state.currentBoard.value = { id: 'board-1' }
    const reopenedCache: TestLabel[] = []
    state.currentBoardLabels.value = reopenedCache
    const pendingReopenedRead = fetchLabels('board-1')

    oldVisitRead.resolve([{ ...originalLabel, name: 'Old visit' }])
    await pendingOldRead
    expect(state.currentBoardLabels.value).toBe(reopenedCache)
    expect(state.currentBoardLabels.value).toEqual([])

    const authoritative = [{
      ...originalLabel,
      name: 'Reopened authoritative',
      updatedAt: '2026-09-20T10:02:00Z',
    }]
    reopenedRead.resolve(authoritative)
    await pendingReopenedRead

    expect(state.currentBoardLabels.value).toBe(reopenedCache)
    expect(state.currentBoardLabels.value).toEqual(authoritative)
  })

  it('does not let a fetch that started first erase a confirmed label update', async () => {
    const state = createState()
    const helpers = createHelpers()
    const staleRead = deferred<TestLabel[]>()
    mockLabelsApi.getLabels.mockReturnValueOnce(staleRead.promise)
    const updated = {
      ...originalLabel,
      name: 'Critical',
      updatedAt: '2026-09-20T10:01:00Z',
    }
    mockLabelsApi.updateLabel.mockResolvedValueOnce(updated)
    const actions = createLabelActions(state as never, helpers as never)

    const pendingRead = actions.fetchLabels('board-1')
    await actions.updateLabel('board-1', 'lbl-1', { name: 'Critical' })
    staleRead.resolve([{ ...originalLabel }])
    await pendingRead

    expect(state.currentBoardLabels.value).toEqual([updated])
  })

  it('does not let an older label update settle over a newer update or invalidate its refresh', async () => {
    const state = createState()
    const helpers = createHelpers()
    const firstUpdate = deferred<TestLabel>()
    const secondUpdate = deferred<TestLabel>()
    const authoritativeRead = deferred<TestLabel[]>()
    mockLabelsApi.updateLabel
      .mockReturnValueOnce(firstUpdate.promise)
      .mockReturnValueOnce(secondUpdate.promise)
    mockLabelsApi.getLabels.mockReturnValueOnce(authoritativeRead.promise)
    const actions = createLabelActions(state as never, helpers as never)

    const pendingFirst = actions.updateLabel('board-1', 'lbl-1', { name: 'First' })
    const pendingSecond = actions.updateLabel('board-1', 'lbl-1', { name: 'Second' })

    const secondResult = {
      ...originalLabel,
      name: 'Second',
      updatedAt: '2026-09-20T10:02:00Z',
    }
    secondUpdate.resolve(secondResult)
    await pendingSecond
    const pendingRead = actions.fetchLabels('board-1')

    firstUpdate.resolve({
      ...originalLabel,
      name: 'First',
      updatedAt: '2026-09-20T10:01:00Z',
    })
    await pendingFirst
    authoritativeRead.resolve([secondResult])
    await pendingRead

    expect(state.currentBoardLabels.value).toEqual([secondResult])
    expect(helpers.toast.success).toHaveBeenCalledTimes(1)
    expect(helpers.toast.success).toHaveBeenCalledWith('Label updated successfully')
  })
})
