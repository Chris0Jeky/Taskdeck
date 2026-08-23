import { onBeforeUnmount, ref, computed, watch } from 'vue'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'
import type { Card, CardCaptureProvenance, Label } from '../types/board'
import type { CardComment } from '../types/comments'
import { useToastStore } from '../store/toastStore'
import { logError } from '../utils/errorReporting'

export interface UseCardModalOptions {
  getCard: () => Card
  getIsOpen: () => boolean
  getLabels: () => Label[]
  onUpdated: () => void
  onClose: () => void
}

export function useCardModal(options: UseCardModalOptions) {
  const boardStore = useBoardStore()
  const sessionStore = useSessionStore()
  const toast = useToastStore()

  // Form state
  const title = ref('')
  const description = ref('')
  const dueDate = ref('')
  const isBlocked = ref(false)
  const blockReason = ref('')
  const selectedLabelIds = ref<string[]>([])
  const expectedUpdatedAt = ref<string | null>(null)

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

  // Delete state
  const showDeleteConfirm = ref(false)
  const isDeleting = ref(false)

  // Computed
  const card = computed(() => options.getCard())

  const deleteConfirmDescription = computed(
    () => `Are you sure you want to delete "${card.value.title}"? This action cannot be undone.`
  )

  const comments = computed<CardComment[]>(() => boardStore.getCardComments(card.value.id))
  const topLevelComments = computed(() => comments.value.filter(comment => !comment.parentCommentId))

  const formattedDueDate = computed(() => {
    if (!card.value.dueDate) return 'No due date'
    const date = new Date(card.value.dueDate)
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' })
  })

  const isOverdue = computed(() => {
    if (!card.value.dueDate) return false
    return new Date(card.value.dueDate) < new Date()
  })

  const isFormValid = computed(() => {
    if (title.value.trim().length === 0) return false
    if (isBlocked.value && blockReason.value.trim().length === 0) return false
    return true
  })

  // Watchers
  watch(() => options.getCard(), (newCard) => {
    if (newCard) {
      title.value = newCard.title
      description.value = newCard.description || ''
      dueDate.value = newCard.dueDate
        ? new Date(newCard.dueDate).toISOString().split('T')[0] ?? ''
        : ''
      isBlocked.value = newCard.isBlocked
      blockReason.value = newCard.blockReason || ''
      selectedLabelIds.value = newCard.labels.map(l => l.id)
      captureProvenance.value = null
      captureProvenanceError.value = null
      loadedCaptureProvenanceCardId.value = null

      if (options.getIsOpen()) {
        loadCaptureProvenance().catch(() => {})
      }
    }
  }, { immediate: true })

  watch(
    () => options.getIsOpen(),
    async (isOpen) => {
      if (isOpen) {
        expectedUpdatedAt.value = card.value.updatedAt
        await boardStore.fetchCardComments(card.value.boardId, card.value.id)
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

      if (boardStore.editingCardId === card.value.id) {
        boardStore.setEditingCard(null)
      }
    },
    { immediate: true }
  )

  // Provenance
  async function loadCaptureProvenance() {
    if (loadingCaptureProvenance.value || loadedCaptureProvenanceCardId.value === card.value.id) {
      return
    }

    loadingCaptureProvenance.value = true
    captureProvenanceError.value = null
    try {
      captureProvenance.value = await boardStore.fetchCardProvenance(card.value.boardId, card.value.id)
      loadedCaptureProvenanceCardId.value = card.value.id
    } catch {
      captureProvenance.value = null
      captureProvenanceError.value = 'Unable to load capture provenance.'
      loadedCaptureProvenanceCardId.value = card.value.id
    } finally {
      loadingCaptureProvenance.value = false
    }
  }

  // Save
  async function handleSave() {
    if (!isFormValid.value) return

    try {
      await boardStore.updateCard(card.value.boardId, card.value.id, {
        title: title.value !== card.value.title ? title.value : null,
        description: description.value !== card.value.description ? description.value : null,
        dueDate: dueDate.value ? new Date(dueDate.value).toISOString() : null,
        clearDueDate: Boolean(card.value.dueDate) && !dueDate.value,
        isBlocked: isBlocked.value !== card.value.isBlocked ? isBlocked.value : null,
        blockReason: isBlocked.value ? blockReason.value : null,
        labelIds: selectedLabelIds.value,
        expectedUpdatedAt: expectedUpdatedAt.value,
      })

      options.onUpdated()
      options.onClose()
    } catch (error) {
      logError('Failed to update card:', error)
      toast.error('Failed to save card changes. Please try again.')
    }
  }

  // Delete
  function handleDeleteClick() {
    showDeleteConfirm.value = true
  }

  function handleDeleteCancel() {
    showDeleteConfirm.value = false
  }

  async function handleDeleteConfirm() {
    if (isDeleting.value) return
    isDeleting.value = true
    try {
      await boardStore.deleteCard(card.value.boardId, card.value.id)
      showDeleteConfirm.value = false
      options.onUpdated()
      options.onClose()
    } catch (error) {
      logError('Failed to delete card:', error)
      toast.error('Failed to delete card. Please try again.')
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
    const content = parentCommentId
      ? (replyDraftByParent.value[parentCommentId] ?? '').trim()
      : newCommentContent.value.trim()

    if (!content) {
      return
    }

    try {
      await boardStore.createCardComment(card.value.boardId, card.value.id, {
        content,
        parentCommentId: parentCommentId ?? null,
      })

      if (parentCommentId) {
        replyDraftByParent.value[parentCommentId] = ''
      } else {
        newCommentContent.value = ''
      }
    } catch (error) {
      logError('Failed to add comment:', error)
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
    const content = editingCommentContent.value.trim()
    if (!content) {
      return
    }

    try {
      await boardStore.updateCardComment(card.value.boardId, card.value.id, commentId, { content })
      handleCancelEditComment()
    } catch (error) {
      logError('Failed to update comment:', error)
      toast.error('Failed to update comment. Please try again.')
    }
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

    isDeletingComment.value = true
    try {
      await boardStore.deleteCardComment(card.value.boardId, card.value.id, comment.id)
      showCommentDeleteConfirm.value = false
      commentPendingDeletion.value = null
    } catch (error) {
      logError('Failed to delete comment:', error)
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
  })

  return {
    // Form state
    title,
    description,
    dueDate,
    isBlocked,
    blockReason,
    selectedLabelIds,
    isFormValid,

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
    handleSave,
  }
}
