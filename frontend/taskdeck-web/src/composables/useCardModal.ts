import { onBeforeUnmount, ref, computed, watch } from 'vue'
import { cardsApi } from '../api/cardsApi'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'
import type { CardDetachPreview, CardWorkItemType, Card, CardCaptureProvenance, Label, UpdateCardDto } from '../types/board'
import type { CardComment } from '../types/comments'
import { useToastStore } from '../store/toastStore'
import { logError } from '../utils/errorReporting'
import { getValidationReason, isValidationError } from './useErrorMapper'
import { estimatedEffortInputs, parseEstimatedEffort } from '../utils/estimatedEffort'
import {
  calendarDateKeyToMidnightUtc,
  formatCalendarDate,
  isCalendarDateOverdue,
  toCalendarDateKey,
} from '../utils/dueDates'

export interface UseCardModalOptions {
  getCard: () => Card
  getIsOpen: () => boolean
  getLabels: () => Label[]
  onUpdated: () => void
  onClose: () => void
  onPermissionDenied?: () => void
}

export function useCardModal(options: UseCardModalOptions) {
  const boardStore = useBoardStore()
  const sessionStore = useSessionStore()
  const toast = useToastStore()
  // A write refusal belongs to the editor/account that submitted it. Closing and
  // reopening the same card must retire it too, even when the card id is unchanged.
  let permissionGeneration = 0
  watch(
    [() => options.getIsOpen(), () => options.getCard().boardId,
      () => options.getCard().id, () => sessionStore.userId],
    () => {
      permissionGeneration++
      isSaving.value = false
      saveError.value = null
    },
    { flush: 'sync' },
  )
  function reportPermissionDenied(error: unknown, requestGeneration: number) {
    if (requestGeneration !== permissionGeneration || !options.getIsOpen()) return
    if ((error as { response?: { status?: number } })?.response?.status === 403) {
      options.onPermissionDenied?.()
    }
  }

  // Form state
  const parentCardId = ref<string | null>(null)
  const detachPreview = ref<CardDetachPreview | null>(null)
  const deletePreviewError = ref<string | null>(null)
  const deletePreviewLoading = ref(false)
  const workItemType = ref<CardWorkItemType>('Task')
  const isSaving = ref(false)
  const saveError = ref<string | null>(null)
  const title = ref('')
  const description = ref('')
  const dueDate = ref('')
  const estimateHours = ref('')
  const estimateMinutes = ref('')
  const initialEstimateMinutes = ref<number | null>(null)
  const parsedEstimate = computed(() => parseEstimatedEffort(estimateHours.value, estimateMinutes.value))
  const estimateChanged = computed(() => Boolean(parsedEstimate.value.error) ||
    parsedEstimate.value.value !== initialEstimateMinutes.value)
  const isBlocked = ref(false)
  const blockReason = ref('')
  const selectedLabelIds = ref<string[]>([])
  const expectedUpdatedAt = ref<string | null>(null)
  let draftRevision = 0
  watch([parentCardId, workItemType, title, description, dueDate, estimateHours, estimateMinutes,
    isBlocked, blockReason, () => selectedLabelIds.value.join()], () => { draftRevision++ }, { flush: 'sync' })

  // Comment state
  const newCommentContent = ref('')
  const replyDraftByParent = ref<Record<string, string>>({})
  const editingCommentId = ref<string | null>(null)
  const editingCommentContent = ref('')
  const commentPendingDeletion = ref<CardComment | null>(null)
  const showCommentDeleteConfirm = ref(false)
  const isDeletingComment = ref(false)

  // Provenance state
  const captureProvenance = ref<CardCaptureProvenance | null>(null)
  const captureProvenanceError = ref<string | null>(null)
  const loadingCaptureProvenance = ref(false)
  const loadedCaptureProvenanceCardId = ref<string | null>(null)
  let loadingCaptureProvenanceCardId: string | null = null
  let provenanceLoadVersion = 0
  let cardSessionVersion = 0

  // Delete state
  const showDeleteConfirm = ref(false)
  const isDeleting = ref(false)
  // Ownership token for the delete preview. Each attempt takes the next value; cancelling,
  // reopening, switching cards, closing the modal and unmounting all bump it, so a late
  // success, failure or finally effect from a superseded attempt can never populate the
  // current dialog, clear its loading state, or replace a newer preview.
  let deletePreviewGeneration = 0

  // Computed
  const card = computed(() => options.getCard())

  const deleteConfirmDescription = computed(
    () => `Are you sure you want to delete "${card.value.title}"? This action cannot be undone.`
  )

  const comments = computed<CardComment[]>(() => boardStore.getCardComments(card.value.id))
  const topLevelComments = computed(() => comments.value.filter(comment => !comment.parentCommentId))

  const formattedDueDate = computed(() => {
    if (!card.value.dueDate) return 'No due date'
    return formatCalendarDate(
      card.value.dueDate,
      { year: 'numeric', month: 'long', day: 'numeric' },
      'en-US',
    ) || 'No due date'
  })

  const isOverdue = computed(() => {
    return isCalendarDateOverdue(card.value.dueDate)
  })

  const isFormValid = computed(() => {
    if (title.value.trim().length === 0) return false
    if (isBlocked.value && blockReason.value.trim().length === 0) return false
    if (dueDate.value && !calendarDateKeyToMidnightUtc(dueDate.value)) return false
    if (parsedEstimate.value.error) return false
    return true
  })

  const hasUnsavedChanges = computed(() => {
    const currentCard = card.value
    return (
      parentCardId.value !== (currentCard.parentCardId ?? null) ||
      workItemType.value !== (currentCard.workItemType ?? 'Task') ||
      title.value !== currentCard.title ||
      description.value !== (currentCard.description || '') ||
      dueDate.value !== (toCalendarDateKey(currentCard.dueDate) ?? '') ||
      estimateChanged.value ||
      isBlocked.value !== currentCard.isBlocked ||
      blockReason.value !== (currentCard.blockReason || '') ||
      selectedLabelIds.value.length !== currentCard.labels.length ||
      selectedLabelIds.value.some((id) => !currentCard.labels.some((label) => label.id === id)) ||
      newCommentContent.value.trim().length > 0 ||
      Object.values(replyDraftByParent.value).some((draft) => draft.trim().length > 0) ||
      editingCommentId.value !== null
    )
  })

  // Watchers
  watch(() => options.getCard(), (newCard, previousCard) => {
    if (newCard) {
      const switchedCards = Boolean(previousCard &&
        (previousCard.id !== newCard.id || previousCard.boardId !== newCard.boardId))
      // Realtime and assignment saves replace the card object. Keep independently
      // edited card fields instead of overwriting the draft with that fresh object.
      if (!switchedCards && previousCard && options.getIsOpen() && (
        title.value !== previousCard.title || description.value !== (previousCard.description || '') ||
        parentCardId.value !== (previousCard.parentCardId ?? null) ||
        workItemType.value !== (previousCard.workItemType ?? 'Task') ||
        dueDate.value !== (toCalendarDateKey(previousCard.dueDate) ?? '') ||
        estimateChanged.value ||
        isBlocked.value !== previousCard.isBlocked || blockReason.value !== (previousCard.blockReason || '') ||
        [...selectedLabelIds.value].sort().join() !== previousCard.labels.map(l => l.id).sort().join()
      )) return
      if (switchedCards) {
        isSaving.value = false
        saveError.value = null
        cardSessionVersion += 1
        invalidateDeletePreview()
      }
      parentCardId.value = newCard.parentCardId ?? null
      detachPreview.value = null
      deletePreviewError.value = null
      workItemType.value = newCard.workItemType ?? 'Task'
      title.value = newCard.title
      description.value = newCard.description || ''
      dueDate.value = toCalendarDateKey(newCard.dueDate) ?? ''
      initialEstimateMinutes.value = newCard.estimatedEffortMinutes ?? null
      const estimateInputs = estimatedEffortInputs(newCard.estimatedEffortMinutes)
      estimateHours.value = estimateInputs.hours
      estimateMinutes.value = estimateInputs.minutes
      isBlocked.value = newCard.isBlocked
      blockReason.value = newCard.blockReason || ''
      selectedLabelIds.value = newCard.labels.map(l => l.id)
      captureProvenance.value = null
      captureProvenanceError.value = null
      loadedCaptureProvenanceCardId.value = null

      if (switchedCards && options.getIsOpen()) {
        provenanceLoadVersion += 1
        loadingCaptureProvenanceCardId = null
        loadingCaptureProvenance.value = false
        expectedUpdatedAt.value = newCard.updatedAt
        newCommentContent.value = ''
        replyDraftByParent.value = {}
        editingCommentId.value = null
        editingCommentContent.value = ''
        commentPendingDeletion.value = null
        showCommentDeleteConfirm.value = false
        isDeletingComment.value = false
        showDeleteConfirm.value = false
        isDeleting.value = false

        if (previousCard && boardStore.editingCardId === previousCard.id) {
          boardStore.setEditingCard(null)
        }
        boardStore.setEditingCard(newCard.id)
        void loadCardComments(newCard)
        void loadCaptureProvenance()
      }
    }
  }, { immediate: true })

  watch(
    () => options.getIsOpen(),
    async (isOpen) => {
      if (isOpen) {
        saveError.value = null
        expectedUpdatedAt.value = card.value.updatedAt
        void loadCardComments(card.value)
        await loadCaptureProvenance()
        boardStore.setEditingCard(card.value.id)
        return
      }

      newCommentContent.value = ''
      replyDraftByParent.value = {}
      editingCommentId.value = null
      editingCommentContent.value = ''
      commentPendingDeletion.value = null
      showCommentDeleteConfirm.value = false
      isDeletingComment.value = false
      captureProvenance.value = null
      captureProvenanceError.value = null
      loadingCaptureProvenance.value = false
      loadedCaptureProvenanceCardId.value = null
      loadingCaptureProvenanceCardId = null
      provenanceLoadVersion += 1
      showDeleteConfirm.value = false
      invalidateDeletePreview()

      if (boardStore.editingCardId === card.value.id) {
        boardStore.setEditingCard(null)
      }
    },
    { immediate: true }
  )

  function loadCardComments(targetCard: Card) {
    return boardStore.fetchCardComments(targetCard.boardId, targetCard.id).catch((error: unknown) => {
      // The store owns the user-facing error state and toast. Keep cached comments intact
      // and let the rest of the card editor continue loading, but never swallow the failure
      // silently: this is the only reporting sink once the rejection is caught here.
      logError('Failed to load card comments:', error)
    })
  }

  // Provenance
  async function loadCaptureProvenance() {
    const targetCard = card.value
    if (
      loadingCaptureProvenanceCardId === targetCard.id ||
      loadedCaptureProvenanceCardId.value === targetCard.id
    ) {
      return
    }

    const requestVersion = ++provenanceLoadVersion
    loadingCaptureProvenanceCardId = targetCard.id
    loadingCaptureProvenance.value = true
    captureProvenanceError.value = null
    try {
      const provenance = await boardStore.fetchCardProvenance(targetCard.boardId, targetCard.id)
      if (requestVersion !== provenanceLoadVersion || card.value.id !== targetCard.id) return
      captureProvenance.value = provenance
      loadedCaptureProvenanceCardId.value = targetCard.id
    } catch {
      if (requestVersion !== provenanceLoadVersion || card.value.id !== targetCard.id) return
      captureProvenance.value = null
      captureProvenanceError.value = 'Unable to load capture provenance.'
      loadedCaptureProvenanceCardId.value = targetCard.id
    } finally {
      if (requestVersion === provenanceLoadVersion) {
        loadingCaptureProvenance.value = false
        loadingCaptureProvenanceCardId = null
      }
    }
  }

  // Save
  async function handleSave() {
    if (!isFormValid.value || isSaving.value) return
    const permissionRequest = permissionGeneration
    const submittedDraftRevision = draftRevision

    const targetCard = card.value
    const targetSessionVersion = cardSessionVersion
    const currentDueDateKey = toCalendarDateKey(targetCard.dueDate) ?? ''
    const dueDateChanged = dueDate.value !== currentDueDateKey
    const update: UpdateCardDto = {
      title: title.value !== targetCard.title ? title.value : null,
      description: description.value !== targetCard.description ? description.value : null,
      isBlocked: isBlocked.value !== targetCard.isBlocked ? isBlocked.value : null,
      blockReason: isBlocked.value ? blockReason.value : null,
      labelIds: selectedLabelIds.value,
      expectedUpdatedAt: expectedUpdatedAt.value,
    }
    if (parentCardId.value !== (targetCard.parentCardId ?? null)) {
      if (parentCardId.value) update.parentCardId = parentCardId.value
      else update.clearParent = true
    }
    if (workItemType.value !== (targetCard.workItemType ?? 'Task')) update.workItemType = workItemType.value
    if (dueDateChanged) {
      update.dueDate = dueDate.value ? calendarDateKeyToMidnightUtc(dueDate.value) : null
      update.clearDueDate = Boolean(targetCard.dueDate) && !dueDate.value
    }
    // Compare with the loaded draft baseline, not a newer realtime card object:
    // an unrelated title save must never send an unedited estimate back.
    if (estimateChanged.value) {
      if (parsedEstimate.value.value === null) update.clearEstimatedEffort = true
      else update.estimatedEffortMinutes = parsedEstimate.value.value
    }
    const ownsSave = () => permissionRequest === permissionGeneration &&
      isCurrentCardSession(targetCard.id, targetSessionVersion)
    isSaving.value = true
    saveError.value = null
    try {
      await boardStore.updateCard(targetCard.boardId, targetCard.id, update)

      if (!ownsSave() || draftRevision !== submittedDraftRevision) return
      options.onUpdated()
      options.onClose()
    } catch (error) {
      logError('Failed to update card:', error)
      reportPermissionDenied(error, permissionRequest)
      if (!ownsSave() || draftRevision !== submittedDraftRevision) return
      const status = (error as { response?: { status?: number } })?.response?.status
      saveError.value = isValidationError(error)
        ? `${getValidationReason(error) ?? 'Please check the card fields.'} Your draft is kept.`
        : status === 409
        ? 'The card changed or is read-only. Your draft is kept. Refresh the board and reopen the card before saving again.'
        : status === 403
          ? 'This card save was refused. Your draft is kept.'
          : 'Could not confirm the save. Your draft is kept. Refresh the board before trying again.'
      toast.error(saveError.value)
    } finally {
      if (ownsSave()) isSaving.value = false
    }
  }

  // Delete
  /**
   * Retires the delete preview attempt that owns the current generation. Everything the
   * dialog renders is reset together with the token so no stale view survives the bump.
   */
  function invalidateDeletePreview() {
    deletePreviewGeneration += 1
    detachPreview.value = null
    deletePreviewError.value = null
    deletePreviewLoading.value = false
  }

  /** True only while `generation` is still the attempt the open dialog is waiting on. */
  function ownsDeletePreview(generation: number, cardId: string, session: number): boolean {
    return (
      generation === deletePreviewGeneration &&
      showDeleteConfirm.value &&
      isCurrentCardSession(cardId, session)
    )
  }

  async function handleDeleteClick() {
    const target = card.value
    const session = cardSessionVersion
    // Supersede any attempt still in flight before starting this one.
    invalidateDeletePreview()
    const generation = deletePreviewGeneration
    showDeleteConfirm.value = true
    deletePreviewLoading.value = true
    try {
      const preview = await cardsApi.previewDetach(target.boardId, target.id)
      if (ownsDeletePreview(generation, target.id, session)) detachPreview.value = preview
    } catch {
      if (ownsDeletePreview(generation, target.id, session)) deletePreviewError.value = 'Could not load the full child list. Close and refresh before deleting.'
    } finally {
      if (ownsDeletePreview(generation, target.id, session)) deletePreviewLoading.value = false
    }
  }

  function handleDeleteCancel() {
    showDeleteConfirm.value = false
    invalidateDeletePreview()
  }

  async function handleDeleteConfirm() {
    if (isDeleting.value || !detachPreview.value || deletePreviewError.value) return
    const permissionRequest = permissionGeneration
    isDeleting.value = true
    try {
      await boardStore.deleteCard(card.value.boardId, card.value.id, detachPreview.value)
      showDeleteConfirm.value = false
      options.onUpdated()
      options.onClose()
    } catch (error) {
      logError('Failed to delete card:', error)
      reportPermissionDenied(error, permissionRequest)
      deletePreviewError.value = 'Card or children changed, or deletion could not be confirmed. Close and refresh before confirming again.'
      toast.error(deletePreviewError.value)
    } finally {
      isDeleting.value = false
    }
  }

  // Due date
  function clearDueDate() {
    dueDate.value = ''
  }

  // Comments
  function getReplies(parentCommentId: string) {
    return comments.value.filter(comment => comment.parentCommentId === parentCommentId)
  }

  function canEditComment(comment: CardComment) {
    return sessionStore.userId === comment.authorUserId
  }

  async function handleAddComment(parentCommentId?: string) {
    const permissionRequest = permissionGeneration
    const targetCard = card.value
    const targetSessionVersion = cardSessionVersion
    const content = parentCommentId
      ? (replyDraftByParent.value[parentCommentId] ?? '').trim()
      : newCommentContent.value.trim()

    if (!content) {
      return
    }

    try {
      await boardStore.createCardComment(targetCard.boardId, targetCard.id, {
        content,
        parentCommentId: parentCommentId ?? null,
      })

      if (!isCurrentCardSession(targetCard.id, targetSessionVersion)) return
      if (parentCommentId) {
        if ((replyDraftByParent.value[parentCommentId] ?? '').trim() === content) {
          replyDraftByParent.value[parentCommentId] = ''
        }
      } else if (newCommentContent.value.trim() === content) {
        newCommentContent.value = ''
      }
    } catch (error) {
      logError('Failed to add comment:', error)
      reportPermissionDenied(error, permissionRequest)
      if (!isCurrentCardSession(targetCard.id, targetSessionVersion)) return
      toast.error('Failed to add comment. Please try again.')
    }
  }

  function handleStartEditComment(comment: CardComment) {
    if (!canEditComment(comment) || comment.isDeleted) {
      return
    }

    editingCommentId.value = comment.id
    editingCommentContent.value = comment.content
  }

  function handleCancelEditComment() {
    editingCommentId.value = null
    editingCommentContent.value = ''
  }

  async function handleSaveEditComment(commentId: string) {
    const permissionRequest = permissionGeneration
    const targetCard = card.value
    const targetSessionVersion = cardSessionVersion
    const content = editingCommentContent.value.trim()
    if (!content) {
      return
    }

    try {
      await boardStore.updateCardComment(targetCard.boardId, targetCard.id, commentId, { content })
      if (
        isCurrentCardSession(targetCard.id, targetSessionVersion) &&
        editingCommentId.value === commentId &&
        editingCommentContent.value.trim() === content
      ) {
        handleCancelEditComment()
      }
    } catch (error) {
      logError('Failed to update comment:', error)
      reportPermissionDenied(error, permissionRequest)
      if (!isCurrentCardSession(targetCard.id, targetSessionVersion)) return
      toast.error('Failed to update comment. Please try again.')
    }
  }

  function isCurrentCardSession(cardId: string, sessionVersion: number): boolean {
    return cardSessionVersion === sessionVersion && card.value.id === cardId
  }

  function handleDeleteComment(comment: CardComment) {
    if (!canEditComment(comment)) {
      return
    }

    commentPendingDeletion.value = comment
    showCommentDeleteConfirm.value = true
  }

  function handleCommentDeleteCancel() {
    if (isDeletingComment.value) {
      return
    }

    showCommentDeleteConfirm.value = false
    commentPendingDeletion.value = null
  }

  async function handleCommentDeleteConfirm() {
    const comment = commentPendingDeletion.value
    if (!comment || isDeletingComment.value) {
      return
    }
    const permissionRequest = permissionGeneration

    isDeletingComment.value = true
    try {
      await boardStore.deleteCardComment(card.value.boardId, card.value.id, comment.id)
      showCommentDeleteConfirm.value = false
      commentPendingDeletion.value = null
    } catch (error) {
      logError('Failed to delete comment:', error)
      reportPermissionDenied(error, permissionRequest)
      toast.error('Failed to delete comment. Please try again.')
    } finally {
      isDeletingComment.value = false
    }
  }

  // Provenance links
  function captureHref(captureItemId: string): string {
    return `/workspace/inbox?boardId=${encodeURIComponent(card.value.boardId)}#capture-${encodeURIComponent(captureItemId)}`
  }

  function proposalHref(proposalId: string): string {
    return `/workspace/review?boardId=${encodeURIComponent(card.value.boardId)}#proposal-${encodeURIComponent(proposalId)}`
  }

  // Cleanup
  onBeforeUnmount(() => {
    permissionGeneration++
    if (boardStore.editingCardId === card.value.id) {
      boardStore.setEditingCard(null)
    }

    expectedUpdatedAt.value = null
    newCommentContent.value = ''
    replyDraftByParent.value = {}
    editingCommentId.value = null
    editingCommentContent.value = ''
    commentPendingDeletion.value = null
    showCommentDeleteConfirm.value = false
    isDeletingComment.value = false
    captureProvenance.value = null
    captureProvenanceError.value = null
    loadingCaptureProvenance.value = false
    loadedCaptureProvenanceCardId.value = null
    loadingCaptureProvenanceCardId = null
    provenanceLoadVersion += 1
    cardSessionVersion += 1
    showDeleteConfirm.value = false
    invalidateDeletePreview()
  })

  return {
    acceptAssignmentVersion: (updatedAt: string, previousVersion?: string) => {
      // Only advance the draft's CAS after our own write from its exact version.
      // A conflict refresh must not silently authorize overwriting someone else's edit.
      if (previousVersion === expectedUpdatedAt.value) expectedUpdatedAt.value = updatedAt
    },
    // Form state
    parentCardId,
    detachPreview,
    deletePreviewError,
    deletePreviewLoading,
    workItemType,
    title,
    description,
    dueDate,
    estimateHours,
    estimateMinutes,
    isBlocked,
    blockReason,
    selectedLabelIds,
    isFormValid,
    hasUnsavedChanges,

    // Due date
    formattedDueDate,
    isOverdue,
    clearDueDate,

    // Comments
    newCommentContent,
    replyDraftByParent,
    editingCommentId,
    editingCommentContent,
    topLevelComments,
    getReplies,
    canEditComment,
    handleAddComment,
    handleStartEditComment,
    handleCancelEditComment,
    handleSaveEditComment,
    handleDeleteComment,
    commentPendingDeletion,
    showCommentDeleteConfirm,
    isDeletingComment,
    handleCommentDeleteCancel,
    handleCommentDeleteConfirm,

    // Provenance
    captureProvenance,
    captureProvenanceError,
    loadingCaptureProvenance,
    loadedCaptureProvenanceCardId,
    captureHref,
    proposalHref,

    // Delete
    showDeleteConfirm,
    isDeleting,
    deleteConfirmDescription,
    handleDeleteClick,
    handleDeleteCancel,
    handleDeleteConfirm,

    // Save
    isSaving,
    saveError,
    handleSave,
  }
}
