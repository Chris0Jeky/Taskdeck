/**
 * Label operations: fetch, create, update, delete labels.
 *
 * Successful label writes advance the board-detail mutation epoch: labels are
 * part of the board-detail fan-out, so a detail read that captured the
 * pre-write state must not be allowed to replace the local update when it
 * resolves after the write (#2435).
 */
import { labelsApi } from '../../api/labelsApi'
import type { CreateLabelDto, Label, UpdateLabelDto } from '../../types/board'
import type { BoardState } from './boardState'
import type { BoardHelpers } from './boardStoreHelpers'

interface LabelCacheVisit {
  boardId: string
  labels: Label[]
}

export function createLabelActions(state: BoardState, helpers: BoardHelpers) {
  // Label state is one selected-board collection. Board-detail commits replace
  // the array, so its identity is the visit/session boundary; same-visit label
  // operations mutate that array in place. Separate read and mutation versions
  // order reads, while same-label writes are serialized because the API has no
  // revision precondition to reject an older request that reaches the server last.
  const readVersionByBoardId = new Map<string, number>()
  const mutationVersionByBoardId = new Map<string, number>()
  const mutationTailByLabelKey = new Map<string, Promise<void>>()

  function captureLabelVisit(boardId: string): LabelCacheVisit {
    return {
      boardId,
      labels: state.currentBoardLabels.value,
    }
  }

  function ownsCurrentLabels(visit: LabelCacheVisit) {
    const currentBoard = state.currentBoard?.value
    return (
      (currentBoard == null || currentBoard.id === visit.boardId) &&
      state.currentBoardLabels.value === visit.labels
    )
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
    mutation: () => Promise<T>,
  ): Promise<T> {
    const key = `${boardId}:${labelId}`
    const previous = mutationTailByLabelKey.get(key) ?? Promise.resolve()
    const operation = previous.catch(() => undefined).then(mutation)
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

  async function fetchLabels(boardId: string) {
    if (helpers.isDemoMode) return
    const visit = captureLabelVisit(boardId)
    const readVersion = nextReadVersion(boardId)
    const mutationVersion = currentMutationVersion(boardId)
    try {
      const labels = await labelsApi.getLabels(boardId)
      if (
        ownsCurrentLabels(visit) &&
        readVersionByBoardId.get(boardId) === readVersion &&
        currentMutationVersion(boardId) === mutationVersion
      ) {
        visit.labels.splice(0, visit.labels.length, ...labels)
      }
    } catch (e: unknown) {
      helpers.handleApiError(e, 'Failed to fetch labels')
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
      if (ownsCurrentLabels(visit)) {
        markLabelMutation(boardId)
        if (!visit.labels.some(existingLabel => existingLabel.id === newLabel.id)) {
          // A board-detail refresh can commit the new stable id before the POST
          // resolves. Preserve that fresher object instead of appending a duplicate.
          visit.labels.push(newLabel)
        }
        helpers.toast.success(`Label "${newLabel.name}" created successfully`)
      }
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
    const visit = captureLabelVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      const updatedLabel = await runLabelMutation(
        boardId,
        labelId,
        () => labelsApi.updateLabel(boardId, labelId, label),
      )
      helpers.markBoardDetailMutation(boardId)

      if (ownsCurrentLabels(visit)) {
        markLabelMutation(boardId)
        const index = visit.labels.findIndex((candidate) => candidate.id === labelId)
        if (index !== -1) {
          visit.labels[index] = updatedLabel
        }
        helpers.toast.success('Label updated successfully')
      }

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
    const visit = captureLabelVisit(boardId)
    try {
      state.loading.value = true
      state.error.value = null
      await runLabelMutation(
        boardId,
        labelId,
        () => labelsApi.deleteLabel(boardId, labelId),
      )
      helpers.markBoardDetailMutation(boardId)

      if (ownsCurrentLabels(visit)) {
        markLabelMutation(boardId)
        const index = visit.labels.findIndex((candidate) => candidate.id === labelId)
        if (index !== -1) visit.labels.splice(index, 1)
        helpers.toast.success('Label deleted successfully')
      }
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
