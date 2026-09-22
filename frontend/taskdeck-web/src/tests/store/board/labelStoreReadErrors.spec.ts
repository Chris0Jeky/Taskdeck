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

const savedLabel = { id: 'label-1', boardId: 'board-1', name: 'Saved', colorHex: '#123456' }

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function createHarness() {
  const state = {
    currentBoard: ref<{ id: string } | null>({ id: 'board-1' }),
    currentBoardLabels: ref<Array<typeof savedLabel>>([]),
    loading: ref(false),
    error: ref<string | null>(null),
  }
  const helpers = {
    isDemoMode: false,
    guardDemoMutation: vi.fn(),
    markBoardDetailMutation: vi.fn(),
    toast: { success: vi.fn(), warning: vi.fn(), error: vi.fn() },
    handleApiError: vi.fn((error: unknown) => {
      state.error.value = error instanceof Error ? error.message : String(error)
      throw error
    }),
  }
  return { state, helpers, actions: createLabelActions(state as never, helpers as never) }
}

describe('labelStore read error ownership', () => {
  beforeEach(() => {
    for (const mock of Object.values(mockLabelsApi)) mock.mockReset()
  })

  it('does not publish an older read error after a newer read succeeds', async () => {
    const older = deferred<Array<typeof savedLabel>>()
    mockLabelsApi.getLabels.mockReturnValueOnce(older.promise).mockResolvedValueOnce([savedLabel])
    const { state, helpers, actions } = createHarness()
    const pending = actions.fetchLabels('board-1').catch(error => error)

    await actions.fetchLabels('board-1')
    const failure = new Error('obsolete read failed')
    older.reject(failure)

    expect(await pending).toBe(failure)
    expect(state.currentBoardLabels.value).toEqual([savedLabel])
    expect(state.error.value).toBeNull()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
  })

  it('does not replace a newer read error with an older read error', async () => {
    const older = deferred<Array<typeof savedLabel>>()
    const latestFailure = new Error('current read failed')
    mockLabelsApi.getLabels.mockReturnValueOnce(older.promise).mockRejectedValueOnce(latestFailure)
    const { state, helpers, actions } = createHarness()
    const pending = actions.fetchLabels('board-1').catch(error => error)

    await expect(actions.fetchLabels('board-1')).rejects.toBe(latestFailure)
    const staleFailure = new Error('obsolete read failed')
    older.reject(staleFailure)

    expect(await pending).toBe(staleFailure)
    expect(state.error.value).toBe(latestFailure.message)
    expect(helpers.handleApiError).toHaveBeenCalledExactlyOnceWith(latestFailure, 'Failed to fetch labels')
  })

  it('does not publish a pre-write read error after a confirmed label mutation', async () => {
    const older = deferred<Array<typeof savedLabel>>()
    mockLabelsApi.getLabels.mockReturnValueOnce(older.promise)
    mockLabelsApi.createLabel.mockResolvedValueOnce(savedLabel)
    const { state, helpers, actions } = createHarness()
    const pending = actions.fetchLabels('board-1').catch(error => error)

    await actions.createLabel('board-1', { name: savedLabel.name, colorHex: savedLabel.colorHex })
    const failure = new Error('pre-write read failed')
    older.reject(failure)

    expect(await pending).toBe(failure)
    expect(state.currentBoardLabels.value).toEqual([savedLabel])
    expect(state.error.value).toBeNull()
    expect(helpers.handleApiError).not.toHaveBeenCalled()
  })

  it('still publishes and rejects a current read failure', async () => {
    const failure = new Error('current read failed')
    mockLabelsApi.getLabels.mockRejectedValueOnce(failure)
    const { state, helpers, actions } = createHarness()

    await expect(actions.fetchLabels('board-1')).rejects.toBe(failure)

    expect(state.error.value).toBe(failure.message)
    expect(helpers.handleApiError).toHaveBeenCalledExactlyOnceWith(failure, 'Failed to fetch labels')
  })
})
