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

function createState() {
  return {
    currentBoard: ref<{ id: string } | null>({ id: 'board-1' }),
    currentBoardLabels: ref([
      { id: 'lbl-1', name: 'Bug', colorHex: '#f00' },
      { id: 'lbl-2', name: 'Feature', colorHex: '#0f0' },
    ]),
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

describe('labelStore board ownership', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockLabelsApi.getLabels.mockReset()
    mockLabelsApi.createLabel.mockReset()
    mockLabelsApi.updateLabel.mockReset()
    mockLabelsApi.deleteLabel.mockReset()
  })

  it('does not let a late label read overwrite the newly selected board', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<Array<{ id: string; name: string; colorHex: string }>>()
    mockLabelsApi.getLabels.mockReturnValueOnce(response.promise)
    const { fetchLabels } = createLabelActions(state as never, helpers as never)

    const pendingFetch = fetchLabels('board-1')

    const nextBoardLabels = [{ id: 'lbl-next', name: 'Next', colorHex: '#00f' }]
    state.currentBoard.value = { id: 'board-2' }
    state.currentBoardLabels.value = nextBoardLabels
    response.resolve([{ id: 'lbl-old', name: 'Old board', colorHex: '#aaa' }])
    await pendingFetch

    expect(state.currentBoardLabels.value).toEqual(nextBoardLabels)
  })

  it('does not append a late create response to the newly selected board', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<{ id: string; name: string; colorHex: string }>()
    mockLabelsApi.createLabel.mockReturnValueOnce(response.promise)
    const { createLabel } = createLabelActions(state as never, helpers as never)

    const pendingCreate = createLabel('board-1', { name: 'Chore', colorHex: '#abc' })

    const nextBoardLabels = [{ id: 'lbl-next', name: 'Next', colorHex: '#00f' }]
    state.currentBoard.value = { id: 'board-2' }
    state.currentBoardLabels.value = nextBoardLabels
    const created = { id: 'lbl-created', name: 'Chore', colorHex: '#abc' }
    response.resolve(created)
    const result = await pendingCreate

    expect(result).toEqual(created)
    expect(state.currentBoardLabels.value).toEqual(nextBoardLabels)
    expect(helpers.markBoardDetailMutation).toHaveBeenCalledWith('board-1')
  })

  it('preserves a fresher detail label when it commits before create settles', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<{ id: string; name: string; colorHex: string }>()
    mockLabelsApi.createLabel.mockReturnValueOnce(response.promise)
    const { createLabel } = createLabelActions(state as never, helpers as never)

    const pendingCreate = createLabel('board-1', { name: 'Chore', colorHex: '#abc' })

    const refreshed = { id: 'lbl-created', name: 'Chore from detail', colorHex: '#def' }
    state.currentBoardLabels.value.push(refreshed)
    response.resolve({ id: 'lbl-created', name: 'Chore', colorHex: '#abc' })
    await pendingCreate

    expect(state.currentBoardLabels.value.filter(label => label.id === refreshed.id)).toEqual([
      refreshed,
    ])
  })

  it('does not update the prior label collection after another board is selected', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<{ id: string; name: string; colorHex: string }>()
    mockLabelsApi.updateLabel.mockReturnValueOnce(response.promise)
    const { updateLabel } = createLabelActions(state as never, helpers as never)

    const pendingUpdate = updateLabel('board-1', 'lbl-1', { name: 'Critical' })

    state.currentBoard.value = { id: 'board-2' }
    response.resolve({ id: 'lbl-1', name: 'Critical', colorHex: '#f00' })
    await pendingUpdate

    expect(state.currentBoardLabels.value[0]).toEqual({
      id: 'lbl-1',
      name: 'Bug',
      colorHex: '#f00',
    })
    expect(helpers.markBoardDetailMutation).toHaveBeenCalledWith('board-1')
  })

  it('does not delete from the prior label collection after another board is selected', async () => {
    const state = createState()
    const helpers = createHelpers()
    const response = deferred<void>()
    mockLabelsApi.deleteLabel.mockReturnValueOnce(response.promise)
    const { deleteLabel } = createLabelActions(state as never, helpers as never)

    const pendingDelete = deleteLabel('board-1', 'lbl-1')

    state.currentBoard.value = { id: 'board-2' }
    response.resolve()
    await pendingDelete

    expect(state.currentBoardLabels.value.map(label => label.id)).toEqual(['lbl-1', 'lbl-2'])
    expect(helpers.markBoardDetailMutation).toHaveBeenCalledWith('board-1')
  })
})
