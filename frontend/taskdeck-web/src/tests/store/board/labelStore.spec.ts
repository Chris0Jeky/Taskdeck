import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'

const { mockLabelsApi } = vi.hoisted(() => ({
  mockLabelsApi: {
    getLabels: vi.fn(),
    createLabel: vi.fn(),
    updateLabel: vi.fn(),
    deleteLabel: vi.fn(),
  },
}))

vi.mock('../../../api/labelsApi', () => ({
  labelsApi: mockLabelsApi,
}))

import { createLabelActions } from '../../../store/board/labelStore'

function createMockState() {
  return {
    currentBoardLabels: ref([
      { id: 'lbl-1', name: 'Bug', colorHex: '#f00' },
      { id: 'lbl-2', name: 'Feature', colorHex: '#0f0' },
    ]),
    loading: ref(false),
    error: ref<string | null>(null),
  }
}

function createMockHelpers() {
  return {
    guardDemoMutation: vi.fn(),
    handleApiError: vi.fn(),
    isDemoMode: false,
    toast: { success: vi.fn(), error: vi.fn() },
  }
}

describe('labelStore', () => {
  let state: ReturnType<typeof createMockState>
  let helpers: ReturnType<typeof createMockHelpers>

  beforeEach(() => {
    vi.clearAllMocks()
    state = createMockState()
    helpers = createMockHelpers()
  })

  describe('fetchLabels', () => {
    it('succeeds and replaces state.currentBoardLabels', async () => {
      const labels = [
        { id: 'lbl-3', name: 'Urgent', colorHex: '#ff0' },
        { id: 'lbl-4', name: 'Docs', colorHex: '#00f' },
      ]
      mockLabelsApi.getLabels.mockResolvedValueOnce(labels)
      const { fetchLabels } = createLabelActions(state as any, helpers as any)
      await fetchLabels('board-1')
      expect(mockLabelsApi.getLabels).toHaveBeenCalledWith('board-1')
      expect(state.currentBoardLabels.value).toEqual(labels)
    })

    it('skips API call in demo mode', async () => {
      helpers.isDemoMode = true
      const { fetchLabels } = createLabelActions(state as any, helpers as any)
      await fetchLabels('board-1')
      expect(mockLabelsApi.getLabels).not.toHaveBeenCalled()
      expect(state.currentBoardLabels.value).toHaveLength(2)
    })

    it('handles error and rethrows', async () => {
      mockLabelsApi.getLabels.mockRejectedValueOnce(new Error('network'))
      const { fetchLabels } = createLabelActions(state as any, helpers as any)
      await expect(fetchLabels('board-1')).rejects.toThrow('network')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to fetch labels',
      )
    })
  })

  describe('createLabel', () => {
    it('appends to array and shows toast with label name', async () => {
      const newLabel = { id: 'lbl-3', name: 'Chore', colorHex: '#abc' }
      mockLabelsApi.createLabel.mockResolvedValueOnce(newLabel)
      const { createLabel } = createLabelActions(state as any, helpers as any)
      const result = await createLabel('board-1', { name: 'Chore', colorHex: '#abc' } as any)
      expect(result).toEqual(newLabel)
      expect(state.currentBoardLabels.value).toHaveLength(3)
      expect(state.currentBoardLabels.value[2]).toEqual(newLabel)
      expect(helpers.toast.success).toHaveBeenCalledWith('Label "Chore" created successfully')
      expect(state.loading.value).toBe(false)
    })

    it('sets loading to true during call', async () => {
      let loadingDuringCall = false
      mockLabelsApi.createLabel.mockImplementationOnce(async () => {
        loadingDuringCall = state.loading.value
        return { id: 'lbl-3', name: 'X', colorHex: '#000' }
      })
      const { createLabel } = createLabelActions(state as any, helpers as any)
      await createLabel('board-1', { name: 'X', colorHex: '#000' } as any)
      expect(loadingDuringCall).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('clears error before call', async () => {
      state.error.value = 'old error'
      mockLabelsApi.createLabel.mockResolvedValueOnce({ id: 'lbl-3', name: 'X', colorHex: '#000' })
      const { createLabel } = createLabelActions(state as any, helpers as any)
      await createLabel('board-1', { name: 'X', colorHex: '#000' } as any)
      expect(state.error.value).toBeNull()
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { createLabel } = createLabelActions(state as any, helpers as any)
      await expect(createLabel('board-1', { name: 'X' } as any)).rejects.toThrow('demo')
      expect(mockLabelsApi.createLabel).not.toHaveBeenCalled()
    })

    it('handles error, sets loading false, and rethrows', async () => {
      mockLabelsApi.createLabel.mockRejectedValueOnce(new Error('create fail'))
      const { createLabel } = createLabelActions(state as any, helpers as any)
      await expect(createLabel('board-1', { name: 'X' } as any)).rejects.toThrow('create fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to create label',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('updateLabel', () => {
    it('finds and replaces label by index', async () => {
      const updated = { id: 'lbl-1', name: 'Critical Bug', colorHex: '#f00' }
      mockLabelsApi.updateLabel.mockResolvedValueOnce(updated)
      const { updateLabel } = createLabelActions(state as any, helpers as any)
      const result = await updateLabel('board-1', 'lbl-1', { name: 'Critical Bug' } as any)
      expect(result).toEqual(updated)
      expect(state.currentBoardLabels.value[0]).toEqual(updated)
      expect(state.currentBoardLabels.value).toHaveLength(2)
      expect(helpers.toast.success).toHaveBeenCalledWith('Label updated successfully')
      expect(state.loading.value).toBe(false)
    })

    it('does not modify array if label not found', async () => {
      const updated = { id: 'lbl-99', name: 'Ghost', colorHex: '#fff' }
      mockLabelsApi.updateLabel.mockResolvedValueOnce(updated)
      const { updateLabel } = createLabelActions(state as any, helpers as any)
      await updateLabel('board-1', 'lbl-99', { name: 'Ghost' } as any)
      expect(state.currentBoardLabels.value).toHaveLength(2)
      expect(state.currentBoardLabels.value[0].name).toBe('Bug')
      expect(state.currentBoardLabels.value[1].name).toBe('Feature')
    })

    it('sets loading to true during call', async () => {
      let loadingDuringCall = false
      mockLabelsApi.updateLabel.mockImplementationOnce(async () => {
        loadingDuringCall = state.loading.value
        return { id: 'lbl-1', name: 'Updated', colorHex: '#f00' }
      })
      const { updateLabel } = createLabelActions(state as any, helpers as any)
      await updateLabel('board-1', 'lbl-1', { name: 'Updated' } as any)
      expect(loadingDuringCall).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { updateLabel } = createLabelActions(state as any, helpers as any)
      await expect(updateLabel('board-1', 'lbl-1', {} as any)).rejects.toThrow('demo')
      expect(mockLabelsApi.updateLabel).not.toHaveBeenCalled()
    })

    it('handles error, sets loading false, and rethrows', async () => {
      mockLabelsApi.updateLabel.mockRejectedValueOnce(new Error('update fail'))
      const { updateLabel } = createLabelActions(state as any, helpers as any)
      await expect(updateLabel('board-1', 'lbl-1', {} as any)).rejects.toThrow('update fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to update label',
      )
      expect(state.loading.value).toBe(false)
    })
  })

  describe('deleteLabel', () => {
    it('filters out label from array and shows toast', async () => {
      mockLabelsApi.deleteLabel.mockResolvedValueOnce(undefined)
      const { deleteLabel } = createLabelActions(state as any, helpers as any)
      await deleteLabel('board-1', 'lbl-1')
      expect(state.currentBoardLabels.value).toHaveLength(1)
      expect(state.currentBoardLabels.value[0].id).toBe('lbl-2')
      expect(helpers.toast.success).toHaveBeenCalledWith('Label deleted successfully')
      expect(state.loading.value).toBe(false)
    })

    it('sets loading to true during call', async () => {
      let loadingDuringCall = false
      mockLabelsApi.deleteLabel.mockImplementationOnce(async () => {
        loadingDuringCall = state.loading.value
      })
      const { deleteLabel } = createLabelActions(state as any, helpers as any)
      await deleteLabel('board-1', 'lbl-1')
      expect(loadingDuringCall).toBe(true)
      expect(state.loading.value).toBe(false)
    })

    it('guards demo mutation', async () => {
      helpers.guardDemoMutation.mockImplementation(() => {
        throw new Error('demo')
      })
      const { deleteLabel } = createLabelActions(state as any, helpers as any)
      await expect(deleteLabel('board-1', 'lbl-1')).rejects.toThrow('demo')
      expect(mockLabelsApi.deleteLabel).not.toHaveBeenCalled()
    })

    it('handles error, sets loading false, and rethrows', async () => {
      mockLabelsApi.deleteLabel.mockRejectedValueOnce(new Error('delete fail'))
      const { deleteLabel } = createLabelActions(state as any, helpers as any)
      await expect(deleteLabel('board-1', 'lbl-1')).rejects.toThrow('delete fail')
      expect(helpers.handleApiError).toHaveBeenCalledWith(
        expect.any(Error),
        'Failed to delete label',
      )
      expect(state.loading.value).toBe(false)
    })
  })
})
