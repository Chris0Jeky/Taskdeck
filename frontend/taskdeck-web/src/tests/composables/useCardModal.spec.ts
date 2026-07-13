import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, nextTick, defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { useCardModal, type UseCardModalOptions } from '../../composables/useCardModal'
import type { Card, Label } from '../../types/board'
import type { CardComment } from '../../types/comments'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockBoardStore = {
  getCardComments: vi.fn().mockReturnValue([]),
  fetchCardComments: vi.fn().mockResolvedValue([]),
  fetchCardProvenance: vi.fn().mockResolvedValue(null),
  updateCard: vi.fn().mockResolvedValue(undefined),
  deleteCard: vi.fn().mockResolvedValue(undefined),
  createCardComment: vi.fn().mockResolvedValue(undefined),
  updateCardComment: vi.fn().mockResolvedValue(undefined),
  deleteCardComment: vi.fn().mockResolvedValue(undefined),
  editingCardId: null as string | null,
  setEditingCard: vi.fn(),
}

const mockSessionStore = {
  userId: 'user-1',
}

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeCard(overrides: Partial<Card> = {}): Card {
  return {
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Test Card',
    description: 'Some description',
    dueDate: '2025-12-31T00:00:00Z',
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-06-15T00:00:00Z',
    ...overrides,
  }
}

function makeComment(overrides: Partial<CardComment> = {}): CardComment {
  return {
    id: 'comment-1',
    boardId: 'board-1',
    cardId: 'card-1',
    parentCommentId: null,
    authorUserId: 'user-1',
    authorUsername: 'testuser',
    content: 'Hello',
    isDeleted: false,
    editedAt: null,
    mentions: [],
    createdAt: '2025-06-01T00:00:00Z',
    updatedAt: '2025-06-01T00:00:00Z',
    ...overrides,
  }
}

function makeLabel(overrides: Partial<Label> = {}): Label {
  return {
    id: 'label-1',
    boardId: 'board-1',
    name: 'Bug',
    colorHex: '#FF0000',
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    ...overrides,
  }
}

/**
 * Mount useCardModal inside a thin wrapper component so Vue lifecycle hooks
 * (watchers, onBeforeUnmount) fire correctly.
 */
function mountComposable(optionOverrides: Partial<UseCardModalOptions> = {}) {
  const cardRef = ref(makeCard())
  const isOpenRef = ref(false)
  const labelsRef = ref<Label[]>([])

  const onUpdated = vi.fn()
  const onClose = vi.fn()

  let result: ReturnType<typeof useCardModal>

  const TestComponent = defineComponent({
    setup() {
      result = useCardModal({
        getCard: () => cardRef.value,
        getIsOpen: () => isOpenRef.value,
        getLabels: () => labelsRef.value,
        onUpdated,
        onClose,
        ...optionOverrides,
      })
      return {}
    },
    template: '<div></div>',
  })

  const wrapper = mount(TestComponent)

  return {
    get result() { return result! },
    wrapper,
    cardRef,
    isOpenRef,
    labelsRef,
    onUpdated,
    onClose,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('useCardModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    mockBoardStore.editingCardId = null
    mockBoardStore.getCardComments.mockReturnValue([])
    mockBoardStore.fetchCardComments.mockResolvedValue([])
    mockBoardStore.fetchCardProvenance.mockResolvedValue(null)
    mockBoardStore.updateCard.mockResolvedValue(undefined)
    mockBoardStore.deleteCard.mockResolvedValue(undefined)
    mockBoardStore.createCardComment.mockResolvedValue(undefined)
    mockBoardStore.updateCardComment.mockResolvedValue(undefined)
    mockBoardStore.deleteCardComment.mockResolvedValue(undefined)
    mockSessionStore.userId = 'user-1'
  })

  // -------------------------------------------------------------------------
  // Initialisation & card watcher
  // -------------------------------------------------------------------------

  describe('card watcher', () => {
    it('populates form fields from card on mount', async () => {
      const { result } = mountComposable()
      await nextTick()

      expect(result.title.value).toBe('Test Card')
      expect(result.description.value).toBe('Some description')
      expect(result.dueDate.value).toBe('2025-12-31')
      expect(result.isBlocked.value).toBe(false)
      expect(result.blockReason.value).toBe('')
    })

    it('uses empty string when card description is null', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ description: '' })
      await nextTick()

      expect(ctx.result.description.value).toBe('')
    })

    it('uses empty string when card dueDate is null', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: null })
      await nextTick()

      expect(ctx.result.dueDate.value).toBe('')
    })

    it('uses empty string when card blockReason is null', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ blockReason: null })
      await nextTick()

      expect(ctx.result.blockReason.value).toBe('')
    })

    it('maps card labels to selectedLabelIds', async () => {
      const label = makeLabel({ id: 'lbl-99' })
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ labels: [label] })
      await nextTick()

      expect(ctx.result.selectedLabelIds.value).toEqual(['lbl-99'])
    })

    it('loads provenance when card changes while modal is open', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      mockBoardStore.fetchCardProvenance.mockClear()

      // Change card (reset the loaded provenance card ID by changing to a new card)
      ctx.cardRef.value = makeCard({ id: 'card-2' })
      await nextTick()
      await nextTick()

      expect(mockBoardStore.fetchCardProvenance).toHaveBeenCalledWith('board-1', 'card-2')
    })

    it('does not load provenance when card changes while modal is closed', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = false
      await nextTick()

      mockBoardStore.fetchCardProvenance.mockClear()
      ctx.cardRef.value = makeCard({ id: 'card-3' })
      await nextTick()
      await nextTick()

      expect(mockBoardStore.fetchCardProvenance).not.toHaveBeenCalled()
    })
  })

  // -------------------------------------------------------------------------
  // isOpen watcher
  // -------------------------------------------------------------------------

  describe('isOpen watcher', () => {
    it('fetches comments and provenance when modal opens', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true

      // The isOpen watcher is async: it awaits fetchCardComments, then
      // loadCaptureProvenance, then calls setEditingCard. We need to flush
      // the microtask queue several times to let all awaits settle.
      await nextTick()
      await nextTick()
      await nextTick()
      await nextTick()

      expect(mockBoardStore.fetchCardComments).toHaveBeenCalledWith('board-1', 'card-1')
      expect(mockBoardStore.setEditingCard).toHaveBeenCalledWith('card-1')
    })

    it('resets comment and provenance state when modal closes', async () => {
      const ctx = mountComposable()

      // Open
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      // Set some state
      ctx.result.newCommentContent.value = 'draft'
      ctx.result.editingCommentId.value = 'c-1'
      ctx.result.editingCommentContent.value = 'editing'
      ctx.result.replyDraftByParent.value = { c1: 'reply' }

      // Close
      ctx.isOpenRef.value = false
      await nextTick()

      expect(ctx.result.newCommentContent.value).toBe('')
      expect(ctx.result.editingCommentId.value).toBeNull()
      expect(ctx.result.editingCommentContent.value).toBe('')
      expect(ctx.result.replyDraftByParent.value).toEqual({})
    })

    it('clears editing card when closing if board store matches', async () => {
      const ctx = mountComposable()
      mockBoardStore.editingCardId = 'card-1'

      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      mockBoardStore.setEditingCard.mockClear()
      ctx.isOpenRef.value = false
      await nextTick()

      expect(mockBoardStore.setEditingCard).toHaveBeenCalledWith(null)
    })

    it('does not clear editing card when closing if board store has different card', async () => {
      const ctx = mountComposable()
      mockBoardStore.editingCardId = 'card-other'

      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      mockBoardStore.setEditingCard.mockClear()
      ctx.isOpenRef.value = false
      await nextTick()

      // setEditingCard should NOT be called with null (only the close-reset branch)
      const nullCalls = mockBoardStore.setEditingCard.mock.calls.filter(
        (args: unknown[]) => args[0] === null,
      )
      expect(nullCalls).toHaveLength(0)
    })
  })

  // -------------------------------------------------------------------------
  // Computed: formattedDueDate, isOverdue, isFormValid
  // -------------------------------------------------------------------------

  describe('formattedDueDate', () => {
    it('returns "No due date" when card has no dueDate', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: null })
      await nextTick()

      expect(ctx.result.formattedDueDate.value).toBe('No due date')
    })

    it('formats a valid due date', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: '2025-12-31T00:00:00Z' })
      await nextTick()

      expect(ctx.result.formattedDueDate.value).toContain('2025')
    })
  })

  describe('isOverdue', () => {
    it('returns false when card has no dueDate', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: null })
      await nextTick()

      expect(ctx.result.isOverdue.value).toBe(false)
    })

    it('returns true when due date is in the past', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: '2020-01-01T00:00:00Z' })
      await nextTick()

      expect(ctx.result.isOverdue.value).toBe(true)
    })

    it('returns false when due date is in the future', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: '2099-12-31T00:00:00Z' })
      await nextTick()

      expect(ctx.result.isOverdue.value).toBe(false)
    })
  })

  describe('isFormValid', () => {
    it('returns false when title is empty', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.title.value = '   '
      expect(ctx.result.isFormValid.value).toBe(false)
    })

    it('returns false when blocked but blockReason is empty', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.title.value = 'Valid Title'
      ctx.result.isBlocked.value = true
      ctx.result.blockReason.value = ''
      expect(ctx.result.isFormValid.value).toBe(false)
    })

    it('returns true when title is set and not blocked', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.title.value = 'Valid Title'
      ctx.result.isBlocked.value = false
      expect(ctx.result.isFormValid.value).toBe(true)
    })

    it('returns true when blocked with a reason provided', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.title.value = 'Valid Title'
      ctx.result.isBlocked.value = true
      ctx.result.blockReason.value = 'Waiting on API'
      expect(ctx.result.isFormValid.value).toBe(true)
    })
  })

  // -------------------------------------------------------------------------
  // deleteConfirmDescription
  // -------------------------------------------------------------------------

  describe('deleteConfirmDescription', () => {
    it('includes the card title', async () => {
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.deleteConfirmDescription.value).toContain('Test Card')
      expect(ctx.result.deleteConfirmDescription.value).toContain('cannot be undone')
    })
  })

  // -------------------------------------------------------------------------
  // Comments computed
  // -------------------------------------------------------------------------

  describe('topLevelComments', () => {
    it('filters out replies (comments with parentCommentId)', async () => {
      const top = makeComment({ id: 'c-1', parentCommentId: null })
      const reply = makeComment({ id: 'c-2', parentCommentId: 'c-1' })
      mockBoardStore.getCardComments.mockReturnValue([top, reply])

      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.topLevelComments.value).toHaveLength(1)
      expect(ctx.result.topLevelComments.value[0]!.id).toBe('c-1')
    })
  })

  describe('getReplies', () => {
    it('returns only comments with matching parentCommentId', async () => {
      const top = makeComment({ id: 'c-1', parentCommentId: null })
      const reply1 = makeComment({ id: 'c-2', parentCommentId: 'c-1' })
      const reply2 = makeComment({ id: 'c-3', parentCommentId: 'c-1' })
      const other = makeComment({ id: 'c-4', parentCommentId: 'c-99' })
      mockBoardStore.getCardComments.mockReturnValue([top, reply1, reply2, other])

      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.getReplies('c-1')).toHaveLength(2)
      expect(ctx.result.getReplies('c-99')).toHaveLength(1)
      expect(ctx.result.getReplies('nonexistent')).toHaveLength(0)
    })
  })

  // -------------------------------------------------------------------------
  // canEditComment
  // -------------------------------------------------------------------------

  describe('canEditComment', () => {
    it('returns true when session user matches comment author', async () => {
      mockSessionStore.userId = 'user-1'
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.canEditComment(makeComment({ authorUserId: 'user-1' }))).toBe(true)
    })

    it('returns false when session user differs from comment author', async () => {
      mockSessionStore.userId = 'user-1'
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.canEditComment(makeComment({ authorUserId: 'user-other' }))).toBe(false)
    })
  })

  // -------------------------------------------------------------------------
  // loadCaptureProvenance
  // -------------------------------------------------------------------------

  describe('loadCaptureProvenance (via open watcher)', () => {
    it('sets capture provenance on success', async () => {
      const provenance = {
        cardId: 'card-1',
        captureItemId: 'cap-1',
        proposalId: 'prop-1',
        proposalStatus: 'Applied' as const,
        triageRunId: null,
      }
      mockBoardStore.fetchCardProvenance.mockResolvedValue(provenance)

      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()
      await nextTick()

      expect(ctx.result.captureProvenance.value).toEqual(provenance)
      expect(ctx.result.loadedCaptureProvenanceCardId.value).toBe('card-1')
      expect(ctx.result.loadingCaptureProvenance.value).toBe(false)
    })

    it('sets error state when provenance fetch fails', async () => {
      mockBoardStore.fetchCardProvenance.mockRejectedValue(new Error('Network error'))

      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()
      await nextTick()

      expect(ctx.result.captureProvenance.value).toBeNull()
      expect(ctx.result.captureProvenanceError.value).toBe('Unable to load capture provenance.')
      expect(ctx.result.loadedCaptureProvenanceCardId.value).toBe('card-1')
      expect(ctx.result.loadingCaptureProvenance.value).toBe(false)
    })
  })

  // -------------------------------------------------------------------------
  // handleSave
  // -------------------------------------------------------------------------

  describe('handleSave', () => {
    it('does nothing when form is invalid', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.title.value = ''
      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).not.toHaveBeenCalled()
      expect(ctx.onUpdated).not.toHaveBeenCalled()
    })

    it('sends only changed fields to updateCard', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      // Modify only title
      ctx.result.title.value = 'New Title'
      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          title: 'New Title',
          description: null, // unchanged
        }),
      )
      expect(ctx.onUpdated).toHaveBeenCalled()
      expect(ctx.onClose).toHaveBeenCalled()
    })

    it('sends explicit clearDueDate when an existing due date is removed', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.dueDate.value = ''
      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          dueDate: null,
          clearDueDate: true,
        }),
      )
    })

    it('does not request a due-date clear when the card never had one', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: null })
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          dueDate: null,
          clearDueDate: false,
        }),
      )
    })

    it('sends ISO dueDate when dueDate is set', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.dueDate.value = '2026-06-01'
      await ctx.result.handleSave()

      const call = mockBoardStore.updateCard.mock.calls[0]!
      expect(call[2].dueDate).toContain('2026-06-01')
    })

    it('sends isBlocked delta and blockReason when blocked', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.isBlocked.value = true
      ctx.result.blockReason.value = 'Blocked reason'
      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          isBlocked: true,
          blockReason: 'Blocked reason',
        }),
      )
    })

    it('sends null blockReason when not blocked', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.isBlocked.value = false
      await ctx.result.handleSave()

      expect(mockBoardStore.updateCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({
          blockReason: null,
        }),
      )
    })

    it('handles updateCard failure gracefully', async () => {
      mockBoardStore.updateCard.mockRejectedValue(new Error('Save failed'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      await ctx.result.handleSave()

      expect(consoleSpy).toHaveBeenCalledWith('Failed to update card:', expect.any(Error))
      expect(ctx.onUpdated).not.toHaveBeenCalled()

      consoleSpy.mockRestore()
    })
  })

  // -------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------

  describe('delete operations', () => {
    it('handleDeleteClick sets showDeleteConfirm', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.handleDeleteClick()
      expect(ctx.result.showDeleteConfirm.value).toBe(true)
    })

    it('handleDeleteCancel clears showDeleteConfirm', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.handleDeleteClick()
      ctx.result.handleDeleteCancel()
      expect(ctx.result.showDeleteConfirm.value).toBe(false)
    })

    it('handleDeleteConfirm calls deleteCard and emits', async () => {
      const ctx = mountComposable()
      await nextTick()

      await ctx.result.handleDeleteConfirm()

      expect(mockBoardStore.deleteCard).toHaveBeenCalledWith('board-1', 'card-1')
      expect(ctx.result.showDeleteConfirm.value).toBe(false)
      expect(ctx.onUpdated).toHaveBeenCalled()
      expect(ctx.onClose).toHaveBeenCalled()
      expect(ctx.result.isDeleting.value).toBe(false)
    })

    it('handleDeleteConfirm does nothing when already deleting', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.isDeleting.value = true
      await ctx.result.handleDeleteConfirm()

      expect(mockBoardStore.deleteCard).not.toHaveBeenCalled()
    })

    it('handleDeleteConfirm handles error and resets isDeleting', async () => {
      mockBoardStore.deleteCard.mockRejectedValue(new Error('Delete failed'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      await ctx.result.handleDeleteConfirm()

      expect(consoleSpy).toHaveBeenCalledWith('Failed to delete card:', expect.any(Error))
      expect(ctx.result.isDeleting.value).toBe(false)

      consoleSpy.mockRestore()
    })
  })

  // -------------------------------------------------------------------------
  // Due date
  // -------------------------------------------------------------------------

  describe('clearDueDate', () => {
    it('resets dueDate to empty string', async () => {
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.dueDate.value).toBe('2025-12-31')
      ctx.result.clearDueDate()
      expect(ctx.result.dueDate.value).toBe('')
    })
  })

  // -------------------------------------------------------------------------
  // Comments
  // -------------------------------------------------------------------------

  describe('handleAddComment', () => {
    it('creates a top-level comment when no parentCommentId', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.newCommentContent.value = 'New comment'
      await ctx.result.handleAddComment()

      expect(mockBoardStore.createCardComment).toHaveBeenCalledWith('board-1', 'card-1', {
        content: 'New comment',
        parentCommentId: null,
      })
      expect(ctx.result.newCommentContent.value).toBe('')
    })

    it('creates a reply when parentCommentId is provided', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.replyDraftByParent.value = { 'c-1': 'Reply text' }
      await ctx.result.handleAddComment('c-1')

      expect(mockBoardStore.createCardComment).toHaveBeenCalledWith('board-1', 'card-1', {
        content: 'Reply text',
        parentCommentId: 'c-1',
      })
      expect(ctx.result.replyDraftByParent.value['c-1']).toBe('')
    })

    it('does nothing when top-level content is empty', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.newCommentContent.value = '   '
      await ctx.result.handleAddComment()

      expect(mockBoardStore.createCardComment).not.toHaveBeenCalled()
    })

    it('does nothing when reply content is empty', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.replyDraftByParent.value = { 'c-1': '   ' }
      await ctx.result.handleAddComment('c-1')

      expect(mockBoardStore.createCardComment).not.toHaveBeenCalled()
    })

    it('does nothing when reply draft is missing (nullish coalescing)', async () => {
      const ctx = mountComposable()
      await nextTick()

      // replyDraftByParent does not have 'c-1' key
      ctx.result.replyDraftByParent.value = {}
      await ctx.result.handleAddComment('c-1')

      expect(mockBoardStore.createCardComment).not.toHaveBeenCalled()
    })

    it('handles createCardComment failure gracefully', async () => {
      mockBoardStore.createCardComment.mockRejectedValue(new Error('fail'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      ctx.result.newCommentContent.value = 'comment'
      await ctx.result.handleAddComment()

      expect(consoleSpy).toHaveBeenCalledWith('Failed to add comment:', expect.any(Error))
      consoleSpy.mockRestore()
    })
  })

  describe('handleStartEditComment', () => {
    it('sets editing state for an editable comment', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1', content: 'Edit me' })
      ctx.result.handleStartEditComment(comment)

      expect(ctx.result.editingCommentId.value).toBe('c-1')
      expect(ctx.result.editingCommentContent.value).toBe('Edit me')
    })

    it('does nothing when user cannot edit the comment', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-other' })
      ctx.result.handleStartEditComment(comment)

      expect(ctx.result.editingCommentId.value).toBeNull()
    })

    it('does nothing when comment is deleted', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1', isDeleted: true })
      ctx.result.handleStartEditComment(comment)

      expect(ctx.result.editingCommentId.value).toBeNull()
    })
  })

  describe('handleCancelEditComment', () => {
    it('resets editing state', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.editingCommentId.value = 'c-1'
      ctx.result.editingCommentContent.value = 'some content'

      ctx.result.handleCancelEditComment()

      expect(ctx.result.editingCommentId.value).toBeNull()
      expect(ctx.result.editingCommentContent.value).toBe('')
    })
  })

  describe('handleSaveEditComment', () => {
    it('updates the comment and clears editing state', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.editingCommentContent.value = 'Updated content'
      await ctx.result.handleSaveEditComment('c-1')

      expect(mockBoardStore.updateCardComment).toHaveBeenCalledWith(
        'board-1', 'card-1', 'c-1', { content: 'Updated content' },
      )
      expect(ctx.result.editingCommentId.value).toBeNull()
    })

    it('does nothing when editing content is empty', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.editingCommentContent.value = '   '
      await ctx.result.handleSaveEditComment('c-1')

      expect(mockBoardStore.updateCardComment).not.toHaveBeenCalled()
    })

    it('handles updateCardComment failure gracefully', async () => {
      mockBoardStore.updateCardComment.mockRejectedValue(new Error('fail'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      ctx.result.editingCommentContent.value = 'content'
      await ctx.result.handleSaveEditComment('c-1')

      expect(consoleSpy).toHaveBeenCalledWith('Failed to update comment:', expect.any(Error))
      consoleSpy.mockRestore()
    })
  })

  describe('handleDeleteComment', () => {
    it('deletes the comment when user confirms', async () => {
      vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      await ctx.result.handleDeleteComment(comment)

      expect(mockBoardStore.deleteCardComment).toHaveBeenCalledWith('board-1', 'card-1', 'c-1')
    })

    it('does nothing when user is not the author', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-other' })
      await ctx.result.handleDeleteComment(comment)

      expect(mockBoardStore.deleteCardComment).not.toHaveBeenCalled()
    })

    it('does nothing when user cancels confirmation', async () => {
      vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      await ctx.result.handleDeleteComment(comment)

      expect(mockBoardStore.deleteCardComment).not.toHaveBeenCalled()
    })

    it('handles deleteCardComment failure gracefully', async () => {
      vi.spyOn(globalThis, 'confirm').mockReturnValue(true)
      mockBoardStore.deleteCardComment.mockRejectedValue(new Error('fail'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      await ctx.result.handleDeleteComment(comment)

      expect(consoleSpy).toHaveBeenCalledWith('Failed to delete comment:', expect.any(Error))
      consoleSpy.mockRestore()
    })
  })

  // -------------------------------------------------------------------------
  // Provenance links
  // -------------------------------------------------------------------------

  describe('captureHref / proposalHref', () => {
    it('generates correct capture href', async () => {
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.captureHref('cap-1')).toBe(
        '/workspace/inbox?boardId=board-1#capture-cap-1',
      )
    })

    it('generates correct proposal href', async () => {
      const ctx = mountComposable()
      await nextTick()

      expect(ctx.result.proposalHref('prop-1')).toBe(
        '/workspace/review?boardId=board-1#proposal-prop-1',
      )
    })
  })

  // -------------------------------------------------------------------------
  // onBeforeUnmount cleanup
  // -------------------------------------------------------------------------

  describe('onBeforeUnmount', () => {
    it('clears editing card when it matches the current card', async () => {
      mockBoardStore.editingCardId = 'card-1'

      const ctx = mountComposable()
      await nextTick()

      mockBoardStore.setEditingCard.mockClear()
      ctx.wrapper.unmount()

      expect(mockBoardStore.setEditingCard).toHaveBeenCalledWith(null)
    })

    it('does not clear editing card when it does not match', async () => {
      mockBoardStore.editingCardId = 'card-other'

      const ctx = mountComposable()
      await nextTick()

      mockBoardStore.setEditingCard.mockClear()
      ctx.wrapper.unmount()

      const nullCalls = mockBoardStore.setEditingCard.mock.calls.filter(
        (args: unknown[]) => args[0] === null,
      )
      expect(nullCalls).toHaveLength(0)
    })
  })
})
