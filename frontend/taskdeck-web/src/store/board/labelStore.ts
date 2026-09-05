/**
 * Label operations: fetch, create, update, delete labels.
 *
 * Successful label writes advance the board-detail mutation epoch: labels are
 * part of the board-detail fan-out, so a detail read that captured the
 * pre-write state must not be allowed to replace the local update when it
 * resolves after the write (#2435).
 */
import { labelsApi } from '../../api/labelsApi'
import type { CreateLabelDto, UpdateLabelDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

export function createLabelActions(state: BoardState, helpers: BoardHelpers) {
  async function fetchLabels(boardId: string) {
    if (helpers.isDemoMode) return
    try {
      state.currentBoardLabels.value = await labelsApi.getLabels(boardId)
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch labels')
      throw e
    }
  }

  async function createLabel(boardId: string, label: CreateLabelDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const newLabel = await labelsApi.createLabel(boardId, label)
      helpers.markBoardDetailMutation(boardId)
      state.currentBoardLabels.value.push(newLabel)
      helpers.toast.success(`Label "${newLabel.name}" created successfully`)
      return newLabel
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to create label')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function updateLabel(boardId: string, labelId: string, label: UpdateLabelDto) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      const updatedLabel = await labelsApi.updateLabel(boardId, labelId, label)
      helpers.markBoardDetailMutation(boardId)

      // Update label in store
      const index = state.currentBoardLabels.value.findIndex((l) => l.id === labelId)
      if (index !== -1) {
        state.currentBoardLabels.value[index] = updatedLabel
      }

      helpers.toast.success('Label updated successfully')
      return updatedLabel
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to update label')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  async function deleteLabel(boardId: string, labelId: string) {
    helpers.guardDemoMutation()
    try {
      state.loading.value = true
      state.error.value = null
      await labelsApi.deleteLabel(boardId, labelId)
      helpers.markBoardDetailMutation(boardId)

      // Remove label from store
      state.currentBoardLabels.value = state.currentBoardLabels.value.filter(
        (l) => l.id !== labelId,
      )

      helpers.toast.success('Label deleted successfully')
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to delete label')
      throw e
    } finally {
      state.loading.value = false
    }
  }

  return {
    fetchLabels,
    createLabel,
    updateLabel,
    deleteLabel,
  }
}
