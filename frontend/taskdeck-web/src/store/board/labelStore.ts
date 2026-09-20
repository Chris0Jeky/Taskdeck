/**
 * Label operations: fetch, create, update, delete labels.
 *
 * Successful label writes advance the board-detail mutation epoch: labels are
 * part of the board-detail fan-out, so a detail read that captured the
 * pre-write state must not be allowed to replace the local update when it
 * resolves after the write (#2435).
 */
import { watch } from 'vue'
import { labelsApi } from '../../api/labelsApi'
import type { CreateLabelDto, Label, UpdateLabelDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

interface LabelCacheVisit {
  boardId: string
  labels: Label[]
  generation: number
}

class StaleBoardVisitError extends Error {
  constructor() {
    super('The board visit that queued this label change has ended.')
    this.name = 'StaleBoardVisitError'
  }
}

export function createLabelActions(state: BoardState, helpers: BoardHelpers) {
  // Label state is one selected-board collection. Board-detail commits replace
  // the array, so its identity distinguishes overlapping reads within one visit.
  // A separate generation observes board-id transitions synchronously: unlike
  // array identity it survives a same-board detail refresh, but A→B→A and
  // logout→login can never reuse the old authority.
  const readVersionByBoardId = new Map<string, number>()
  const mutationVersionByBoardId = new Map<string, number>()
  const mutationTailByLabelKey = new Map<string, Promise<void>>()
  let boardVisitGeneration = 0

  watch(
    () => state.currentBoard?.value?.id ?? null,
    (nextBoardId, previousBoardId) => {
      if (nextBoardId !== previousBoardId) boardVisitGeneration++
    },
    { flush: 'sync' },
  )

  function captureLabelVisit(boardId: string): LabelCacheVisit {
    return {
      boardId,
      labels: state.currentBoardLabels.value,
      generation: boardVisitGeneration,
    }
  }

  function isCurrentBoardVisit(visit: LabelCacheVisit) {
    const currentBoard = state.currentBoard?.value
    return (
      (currentBoard == null || currentBoard.id === visit.boardId) &&
      boardVisitGeneration === visit.generation
    )
  }

  function ownsExactLabelCache(visit: LabelCacheVisit) {
    return isCurrentBoardVisit(visit) && state.currentBoardLabels.value === visit.labels
  }

  function nextReadVersion(boardId: string) {
    const version = (readVersionByBoardId.get(boardId) ?? 0) + 1
    readVersionByBoardId.set(boardId, version)
    return version
  }

  function currentMutationVersion(boardId: string) {
    return mutationVersionByBoardId.get(boardId) ?? 0
  }

  function markLabelMutation(boardId: string) {
    mutationVersionByBoardId.set(boardId, currentMutationVersion(boardId) + 1)
  }

  async function runLabelMutation<T>(
    boardId: string,
    labelId: string,
    visit: LabelCacheVisit,
    mutation: () => Promise<T>,
  ): Promise<T> {
    const key = `${boardId}:${labelId}`
    const previous = mutationTailByLabelKey.get(key) ?? Promise.resolve()
    const operation = previous.catch(() => undefined).then(() => {
      // The HTTP interceptor reads the token when transport starts. A queued
      // pre-logout intent must therefore be rejected BEFORE invoking the API,
      // not merely ignored when its response arrives under another session.
      if (!isCurrentBoardVisit(visit)) throw new StaleBoardVisitError()
      return mutation()
    })
    const tail = operation.then(
      () => undefined,
      () => undefined,
    )
    mutationTailByLabelKey.set(key, tail)

    try {
      return await operation
    } finally {
      if (mutationTailByLabelKey.get(key) === tail) {
        mutationTailByLabelKey.delete(key)
      }
    }
  }

  async function reconcileCurrentLabelsAfterStaleVisit(boardId: string) {
    if (state.currentBoard?.value?.id !== boardId) return

    const visit = captureLabelVisit(boardId)
    const readVersion = nextReadVersion(boardId)
    const mutationVersion = currentMutationVersion(boardId)
    try {
      const labels = await labelsApi.getLabels(boardId)
      if (
        isCurrentBoardVisit(visit) &&
        readVersionByBoardId.get(boardId) === readVersion &&
        currentMutationVersion(boardId) === mutationVersion
      ) {
        // This read starts only after the write succeeded. A pre-write detail
        // read is invalidated by the mutation epoch; replacing the latest
        // same-visit array therefore installs the authoritative post-write set.
        state.currentBoardLabels.value.splice(
          0,
          state.currentBoardLabels.value.length,
          ...labels,
        )
      }
    } catch {
      if (isCurrentBoardVisit(visit)) {
        helpers.toast.warning(
          'Label saved, but labels could not be refreshed. Refresh the board before editing again.',
        )
      }
    }
  }

  async function fetchLabels(boardId: string) {
    if (helpers.isDemoMode) return
    const visit = captureLabelVisit(boardId)
    const readVersion = nextReadVersion(boardId)
    const mutationVersion = currentMutationVersion(boardId)
    try {
      const labels = await labelsApi.getLabels(boardId)
      if (
        ownsExactLabelCache(visit) &&
        readVersionByBoardId.get(boardId) === readVersion &&
        currentMutationVersion(boardId) === mutationVersion
      ) {
        visit.labels.splice(0, visit.labels.length, ...labels)
      }
    } catch (e: unknown) {
      if (ownsExactLabelCache(visit)) {
        helpers.handleApiError(e, 'Failed to fetch labels')
      }
      throw e
    }
  }

  async function createLabel(boardId: string, label: CreateLabelDto) {
    helpers.guardDemoMutation()
    const visit = captureLabelVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      const newLabel = await labelsApi.createLabel(boardId, label)
      helpers.markBoardDetailMutation(boardId)
      markLabelMutation(boardId)

      if (isCurrentBoardVisit(visit)) {
        const currentLabels = state.currentBoardLabels.value
        if (!currentLabels.some(existingLabel => existingLabel.id === newLabel.id)) {
          // A same-board refresh can install the stable id before the POST
          // resolves. Preserve that fresher object instead of appending a duplicate.
          currentLabels.push(newLabel)
        }
        helpers.toast.success(`Label "${newLabel.name}" created successfully`)
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentLabelsAfterStaleVisit(boardId)
      }
      return newLabel
    } catch (e: unknown) {
      if (isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to create label')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
    }
  }

  async function updateLabel(boardId: string, labelId: string, label: UpdateLabelDto) {
    helpers.guardDemoMutation()
    const visit = captureLabelVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      const updatedLabel = await runLabelMutation(
        boardId,
        labelId,
        visit,
        () => labelsApi.updateLabel(boardId, labelId, label),
      )
      helpers.markBoardDetailMutation(boardId)
      markLabelMutation(boardId)

      if (isCurrentBoardVisit(visit)) {
        const currentLabels = state.currentBoardLabels.value
        const index = currentLabels.findIndex((candidate) => candidate.id === labelId)
        if (index !== -1) currentLabels[index] = updatedLabel
        helpers.toast.success('Label updated successfully')
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentLabelsAfterStaleVisit(boardId)
      }

      return updatedLabel
    } catch (e: unknown) {
      if (!(e instanceof StaleBoardVisitError) && isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to update label')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
    }
  }

  async function deleteLabel(boardId: string, labelId: string) {
    helpers.guardDemoMutation()
    const visit = captureLabelVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      await runLabelMutation(
        boardId,
        labelId,
        visit,
        () => labelsApi.deleteLabel(boardId, labelId),
      )
      helpers.markBoardDetailMutation(boardId)
      markLabelMutation(boardId)

      if (isCurrentBoardVisit(visit)) {
        const currentLabels = state.currentBoardLabels.value
        const index = currentLabels.findIndex((candidate) => candidate.id === labelId)
        if (index !== -1) currentLabels.splice(index, 1)
        helpers.toast.success('Label deleted successfully')
      } else if (state.currentBoard?.value?.id === boardId) {
        await reconcileCurrentLabelsAfterStaleVisit(boardId)
      }
    } catch (e: unknown) {
      if (!(e instanceof StaleBoardVisitError) && isCurrentBoardVisit(visit)) {
        helpers.handleApiError(e, 'Failed to delete label')
      }
      throw e
    } finally {
      if (isCurrentBoardVisit(visit)) state.loading.value = false
    }
  }

  return {
    fetchLabels,
    createLabel,
    updateLabel,
    deleteLabel,
  }
}
