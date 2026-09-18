import { afterEach, describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, nextTick, defineComponent, reactive } from 'vue'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { useCardModal, type UseCardModalOptions } from '../../composables/useCardModal'
import { cardsApi } from '../../api/cardsApi'
import type { Card, CardDetachPreview, Label, UpdateCardDto } from '../../types/board'
import type { CardComment } from '../../types/comments'
import { installTimeZone } from '../utils/timeZone'

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

const mockSessionStore = reactive({
  userId: 'user-1',
})

vi.mock('../../api/cardsApi', () => ({ cardsApi: {
  getCards: vi.fn().mockResolvedValue([]),
  previewDetach: vi.fn().mockResolvedValue({ cardId: 'card-1', expectedUpdatedAt: '2025-06-15T00:00:00Z', expectedChildrenFingerprint: 'v1:fixed', children: [] }),
} }))

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

function makePreview(expectedChildrenFingerprint: string, cardId = 'card-1'): CardDetachPreview {
  return {
    cardId,
    expectedUpdatedAt: '2025-06-15T00:00:00Z',
    expectedChildrenFingerprint,
    children: [],
  }
}

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason?: unknown) => void
}

/** A promise whose settlement the test drives, so two attempts can overlap deterministically. */
function defer<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

/** Drain the microtask queue so awaited continuations inside the composable have run. */
function flush(): Promise<void> {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
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
  // `installTimeZone`, not `vi.stubEnv('TZ', ...)`: the env stub only moves the
  // runtime zone under the default `forks` pool, and Stryker's Vitest dry run
  // forces `pool: 'threads'`, where it silently leaves the host zone in place
  // (#2943).
  let restoreZone: (() => void) | null = null

  afterEach(() => {
    restoreZone?.()
    restoreZone = null
  })

  it('keeps other field drafts across assignment updates without advancing an unrelated stale version', async () => {
    const state = mountComposable()
    state.isOpenRef.value = true; await nextTick()
    const original = state.cardRef.value.updatedAt
    state.result.title.value = 'Unsaved thought'
    state.result.acceptAssignmentVersion('own-assignment', original)
    state.cardRef.value = { ...state.cardRef.value, updatedAt: 'own-assignment' }
    await nextTick()
    expect(state.result.title.value).toBe('Unsaved thought')
    state.result.acceptAssignmentVersion('remote-refresh')
    state.cardRef.value = { ...state.cardRef.value, title: 'Someone else', updatedAt: 'remote-refresh' }
    await nextTick()
    expect(state.result.title.value).toBe('Unsaved thought')
    await state.result.handleSave()
    expect(mockBoardStore.updateCard).toHaveBeenCalledWith('board-1', 'card-1', expect.objectContaining({
      title: 'Unsaved thought', expectedUpdatedAt: 'own-assignment',
    }))
  })

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

  describe('estimated effort drafts', () => {
    it.each([
      { name: 'positive', initial: null, saved: 75 },
      { name: 'zero', initial: 90, saved: 0 },
      { name: 'clear', initial: 0, saved: null },
    ])('accepts the $name receipt and resaves a newer title from a snapshot host', async ({ initial, saved }) => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: initial, updatedAt: 'loaded-v1' })
      ctx.isOpenRef.value = true
      await nextTick()
      const pending = defer<Card>()
      mockBoardStore.updateCard.mockReturnValueOnce(pending.promise)
      // ColumnLane holds a selected-card snapshot: the store receipt never replaces
      // ctx.cardRef, so the editor must learn its new baseline from its own save.
      ctx.result.estimateHours.value = saved === null ? '' : String(Math.floor(saved / 60))
      ctx.result.estimateMinutes.value = saved === null ? '' : String(saved % 60)
      const firstSave = ctx.result.handleSave()
      ctx.result.title.value = 'Newer title draft'
      pending.resolve(makeCard({ estimatedEffortMinutes: saved, updatedAt: 'saved-v2' }))
      await firstSave
      expect(ctx.cardRef.value.updatedAt).toBe('loaded-v1')
      expect(ctx.result.title.value).toBe('Newer title draft')
      expect(ctx.onClose).not.toHaveBeenCalled()
      mockBoardStore.updateCard.mockImplementationOnce((_boardId: string, _cardId: string, update: UpdateCardDto) =>
        update.expectedUpdatedAt === 'saved-v2'
          ? Promise.resolve(makeCard({ title: 'Newer title draft', estimatedEffortMinutes: saved, updatedAt: 'saved-v3' }))
          : Promise.reject({ response: { status: 409 } }),
      )
      await ctx.result.handleSave()
      const retry = mockBoardStore.updateCard.mock.calls[1]![2]
      expect(retry).toMatchObject({ title: 'Newer title draft', expectedUpdatedAt: 'saved-v2' })
      expect(retry).not.toHaveProperty('estimatedEffortMinutes')
      expect(retry).not.toHaveProperty('clearEstimatedEffort')
      expect(ctx.result.saveError.value).toBeNull()
      expect(ctx.onUpdated).toHaveBeenCalledTimes(1)
      expect(ctx.onClose).toHaveBeenCalledTimes(1)
      ctx.wrapper.unmount()
    })

    it.each([
      { initial: null, saved: 0, next: { clearEstimatedEffort: true } },
      { initial: 0, saved: null, next: { estimatedEffortMinutes: 0 } },
    ])('uses the receipt baseline when a newer estimate draft reverses $saved', async ({ initial, saved, next }) => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: initial, updatedAt: 'loaded-v1' })
      ctx.isOpenRef.value = true
      await nextTick()
      const pending = defer<Card>()
      mockBoardStore.updateCard.mockReturnValueOnce(pending.promise)
      ctx.result.estimateHours.value = saved === null ? '' : '0'
      ctx.result.estimateMinutes.value = saved === null ? '' : '0'
      const firstSave = ctx.result.handleSave()
      ctx.result.estimateHours.value = initial === null ? '' : '0'
      ctx.result.estimateMinutes.value = initial === null ? '' : '0'
      pending.resolve(makeCard({ estimatedEffortMinutes: saved, updatedAt: 'saved-v2' }))
      await firstSave
      expect(ctx.onClose).not.toHaveBeenCalled()
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard.mock.calls[1]![2]).toMatchObject({ ...next, expectedUpdatedAt: 'saved-v2' })
      ctx.wrapper.unmount()
    })

    it.each(['card', 'board', 'account', 'reopen'] as const)('does not advance the current baseline from an old %s receipt', async context => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: 90, updatedAt: 'loaded-v1' })
      ctx.isOpenRef.value = true
      await nextTick()
      const pending = defer<Card>()
      mockBoardStore.updateCard.mockReturnValueOnce(pending.promise)
      ctx.result.estimateHours.value = '0'
      ctx.result.estimateMinutes.value = '15'
      const firstSave = ctx.result.handleSave()
      if (context === 'card') ctx.cardRef.value = makeCard({ id: 'card-2', estimatedEffortMinutes: 120, updatedAt: 'current-v1' })
      if (context === 'board') ctx.cardRef.value = makeCard({ boardId: 'board-2', estimatedEffortMinutes: 120, updatedAt: 'current-v1' })
      if (context === 'account') mockSessionStore.userId = 'user-2'
      if (context === 'reopen') {
        ctx.isOpenRef.value = false
        await nextTick()
        ctx.isOpenRef.value = true
      }
      await nextTick()
      ctx.result.estimateHours.value = context === 'card' || context === 'board' ? '2' : '1'
      ctx.result.estimateMinutes.value = context === 'card' || context === 'board' ? '0' : '30'
      ctx.result.title.value = 'Current editor draft'
      pending.resolve(makeCard({ estimatedEffortMinutes: 15, updatedAt: 'old-receipt-v2' }))
      await firstSave
      expect(ctx.onClose).not.toHaveBeenCalled()
      await ctx.result.handleSave()
      const currentSave = mockBoardStore.updateCard.mock.calls[1]![2]
      expect(currentSave.expectedUpdatedAt).toBe(ctx.cardRef.value.updatedAt)
      expect(currentSave).not.toHaveProperty('estimatedEffortMinutes')
      expect(currentSave).not.toHaveProperty('clearEstimatedEffort')
      ctx.wrapper.unmount()
    })

    it.each([undefined, null, 0, 90, 1_000_000])('hydrates %s without making an unrelated save an estimate write', async value => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: value })
      ctx.isOpenRef.value = true
      await nextTick()
      expect([ctx.result.estimateHours.value, ctx.result.estimateMinutes.value]).toEqual(
        value == null ? ['', ''] : [String(Math.floor(value / 60)), String(value % 60)],
      )
      expect(ctx.result.hasUnsavedChanges.value).toBe(false)
      ctx.result.title.value = 'Only title changed'
      await ctx.result.handleSave()
      const update = mockBoardStore.updateCard.mock.calls[0]![2]
      expect(update).not.toHaveProperty('estimatedEffortMinutes')
      expect(update).not.toHaveProperty('clearEstimatedEffort')
      ctx.wrapper.unmount()
    })

    it.each([['', '0', 0], ['1', '30', 90], ['16666', '40', 1_000_000]])('saves entered %sh %sm with the loaded version', async (hours, minutes, value) => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      ctx.result.estimateHours.value = String(hours)
      ctx.result.estimateMinutes.value = String(minutes)
      expect(ctx.result.hasUnsavedChanges.value).toBe(true)
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard).toHaveBeenCalledWith('board-1', 'card-1', expect.objectContaining({
        estimatedEffortMinutes: value, expectedUpdatedAt: ctx.cardRef.value.updatedAt,
      }))
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).not.toHaveProperty('clearEstimatedEffort')
      ctx.wrapper.unmount()
    })

    it.each([['-1', '0'], ['1.5', ''], ['1e2', ''], ['0', '60'], ['16666', '41'], ['no', ''], ['99999999999999999999', '0']])('refuses invalid draft %sh %sm without dropping it', async (hours, minutes) => {
      const ctx = mountComposable()
      ctx.result.estimateHours.value = hours
      ctx.result.estimateMinutes.value = minutes
      expect(ctx.result.isFormValid.value).toBe(false)
      expect(ctx.result.hasUnsavedChanges.value).toBe(true)
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard).not.toHaveBeenCalled()
      expect(ctx.result.estimateHours.value).toBe(hours)
      ctx.wrapper.unmount()
    })

    it.each([0, 90])('explicitly clears a loaded %s estimate', async value => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: value })
      ctx.isOpenRef.value = true
      await nextTick()
      ctx.result.estimateHours.value = ''
      ctx.result.estimateMinutes.value = ''
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).toMatchObject({ clearEstimatedEffort: true })
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).not.toHaveProperty('estimatedEffortMinutes')
      ctx.wrapper.unmount()
    })

    it('keeps an estimate-only draft across assignment and realtime card replacement', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      ctx.result.estimateMinutes.value = '25'
      ctx.result.acceptAssignmentVersion('own-assignment', ctx.cardRef.value.updatedAt)
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: 80, updatedAt: 'remote-newer' })
      await nextTick()
      expect(ctx.result.estimateMinutes.value).toBe('25')
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).toMatchObject({ estimatedEffortMinutes: 25, expectedUpdatedAt: 'own-assignment' })
      ctx.wrapper.unmount()
    })

    it('omits an untouched estimate even when a remote estimate changes behind another draft', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      ctx.result.title.value = 'My title'
      ctx.cardRef.value = makeCard({ estimatedEffortMinutes: 80, updatedAt: 'remote-newer' })
      await nextTick()
      await ctx.result.handleSave()
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).not.toHaveProperty('estimatedEffortMinutes')
      expect(mockBoardStore.updateCard.mock.calls[0]![2]).not.toHaveProperty('clearEstimatedEffort')
      ctx.wrapper.unmount()
    })

    it.each(['card', 'board', 'account', 'reopen', 'draft'] as const)('does not close or replace the current editor on a stale save after %s changes', async context => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      const pending = defer<void>()
      mockBoardStore.updateCard.mockReturnValueOnce(pending.promise)
      ctx.result.estimateMinutes.value = '15'
      const save = ctx.result.handleSave()
      if (context === 'card') ctx.cardRef.value = makeCard({ id: 'card-2', estimatedEffortMinutes: 120 })
      if (context === 'board') ctx.cardRef.value = makeCard({ boardId: 'board-2', estimatedEffortMinutes: 120 })
      if (context === 'account') mockSessionStore.userId = 'user-2'
      if (context === 'reopen') {
        ctx.isOpenRef.value = false
        await nextTick()
        ctx.isOpenRef.value = true
      }
      await nextTick()
      ctx.result.estimateMinutes.value = '35'
      pending.resolve()
      await save
      expect(ctx.result.estimateMinutes.value).toBe('35')
      expect(ctx.onClose).not.toHaveBeenCalled()
      expect(ctx.onUpdated).not.toHaveBeenCalled()
      ctx.wrapper.unmount()
    })

    it.each([403, 409, 500])('preserves estimate drafts after save failure %s', async status => {
      const denied = vi.fn()
      const ctx = mountComposable({ onPermissionDenied: denied })
      ctx.isOpenRef.value = true
      await nextTick()
      ctx.result.estimateMinutes.value = '0'
      mockBoardStore.updateCard.mockRejectedValueOnce({ response: { status } })
      await ctx.result.handleSave()
      expect(ctx.result.estimateMinutes.value).toBe('0')
      expect(ctx.result.saveError.value).toContain('Your draft is kept')
      expect(denied).toHaveBeenCalledTimes(status === 403 ? 1 : 0)
      expect(ctx.onClose).not.toHaveBeenCalled()
      ctx.wrapper.unmount()
    })
  })

  describe('write permission refusal bridge', () => {
    type Operation = 'updateCard' | 'deleteCard' | 'createCardComment' | 'updateCardComment' | 'deleteCardComment'
    function startWrite(state: ReturnType<typeof mountComposable>, operation: Operation) {
      const api = state.result
      switch (operation) {
        case 'updateCard': return api.handleSave()
        case 'deleteCard':
          api.detachPreview.value = makePreview('current')
          return api.handleDeleteConfirm()
        case 'createCardComment':
          api.newCommentContent.value = 'Kept comment'
          return api.handleAddComment()
        case 'updateCardComment':
          api.editingCommentId.value = 'comment-1'
          api.editingCommentContent.value = 'Kept edit'
          return api.handleSaveEditComment('comment-1')
        case 'deleteCardComment':
          api.handleDeleteComment(makeComment())
          return api.handleCommentDeleteConfirm()
      }
    }

    for (const operation of ['updateCard', 'deleteCard', 'createCardComment', 'updateCardComment', 'deleteCardComment'] as const) {
      it.each([403, 409])(`${operation}: reports only a confirmed403, preserving the draft (status=%s)`, async status => {
        const onPermissionDenied = vi.fn()
        const state = mountComposable({ onPermissionDenied })
        state.isOpenRef.value = true
        await nextTick()
        state.result.title.value = 'Kept title'
        mockBoardStore[operation].mockRejectedValueOnce({ response: { status } })
        await startWrite(state, operation)
        expect(onPermissionDenied).toHaveBeenCalledTimes(status === 403 ? 1 : 0)
        expect(state.result.title.value).toBe('Kept title')
        expect(state.onClose).not.toHaveBeenCalled()
        if (operation === 'createCardComment') expect(state.result.newCommentContent.value).toBe('Kept comment')
        if (operation === 'updateCardComment') expect(state.result.editingCommentContent.value).toBe('Kept edit')
        state.wrapper.unmount()
      })
    }

    it.each([
      ['updateCard', 'card'], ['deleteCard', 'board'], ['createCardComment', 'account'],
      ['updateCardComment', 'reopen'], ['deleteCardComment', 'unmount'],
    ] as const)('ignores a late %s403 after %s context replacement', async (operation, replacement) => {
      const onPermissionDenied = vi.fn()
      const state = mountComposable({ onPermissionDenied })
      state.isOpenRef.value = true
      await nextTick()
      const pending = defer<void>()
      mockBoardStore[operation].mockReturnValueOnce(pending.promise)
      const write = startWrite(state, operation)
      if (replacement === 'card') state.cardRef.value = makeCard({ id: 'next-card' })
      if (replacement === 'board') state.cardRef.value = makeCard({ boardId: 'next-board' })
      if (replacement === 'account') mockSessionStore.userId = 'next-user'
      if (replacement === 'reopen') {
        state.isOpenRef.value = false
        state.isOpenRef.value = true
      }
      if (replacement === 'unmount') state.wrapper.unmount()
      await nextTick()
      pending.reject({ response: { status: 403 } })
      await write
      expect(onPermissionDenied).not.toHaveBeenCalled()
      if (replacement !== 'unmount') state.wrapper.unmount()
    })
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

    it('keeps the UTC calendar key in the date input west of UTC', async () => {
      restoreZone = installTimeZone('America/Los_Angeles')
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: '2026-08-23T00:00:00.000Z' })
      await nextTick()

      expect(ctx.result.dueDate.value).toBe('2026-08-23')
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

      expect(ctx.result.formattedDueDate.value).toBe('December 31, 2025')
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

      const update = mockBoardStore.updateCard.mock.calls[0]![2]
      expect(Object.prototype.hasOwnProperty.call(update, 'dueDate')).toBe(false)
      expect(Object.prototype.hasOwnProperty.call(update, 'clearDueDate')).toBe(false)
    })

    it('omits an unchanged due date when the stored value has a legacy time or offset', async () => {
      const ctx = mountComposable()
      ctx.cardRef.value = makeCard({ dueDate: '2025-12-31T18:30:00-05:00' })
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.title.value = 'Unrelated title edit'
      await ctx.result.handleSave()

      const update = mockBoardStore.updateCard.mock.calls[0]![2]
      expect(update.title).toBe('Unrelated title edit')
      expect(Object.prototype.hasOwnProperty.call(update, 'dueDate')).toBe(false)
      expect(Object.prototype.hasOwnProperty.call(update, 'clearDueDate')).toBe(false)
    })

    it('sends midnight UTC when a calendar due date is set', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()

      ctx.result.dueDate.value = '2026-06-01'
      await ctx.result.handleSave()

      const call = mockBoardStore.updateCard.mock.calls[0]!
      expect(call[2].dueDate).toBe('2026-06-01T00:00:00.000Z')
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

    it.each([
      'Card parents cannot form a cycle.',
      'Card hierarchy supports at most three links (four levels), including descendants.',
    ])('keeps the parent draft and explains validation: %s', async (message) => {
      mockBoardStore.updateCard.mockRejectedValue({ response: { status: 400, data: { errorCode: 'ValidationError', message } } })
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await nextTick()
      ctx.result.parentCardId.value = 'selected-parent'

      await ctx.result.handleSave()

      expect(ctx.result.saveError.value).toBe(`${message} Your draft is kept.`)
      expect(ctx.result.parentCardId.value).toBe('selected-parent')
      expect(ctx.onUpdated).not.toHaveBeenCalled()
      expect(ctx.onClose).not.toHaveBeenCalled()
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

      await ctx.result.handleDeleteClick()
      await ctx.result.handleDeleteConfirm()

      expect(mockBoardStore.deleteCard).toHaveBeenCalledWith('board-1', 'card-1', expect.objectContaining({ expectedChildrenFingerprint: 'v1:fixed' }))
      expect(ctx.result.showDeleteConfirm.value).toBe(false)
      expect(ctx.onUpdated).toHaveBeenCalled()
      expect(ctx.onClose).toHaveBeenCalled()
      expect(ctx.result.isDeleting.value).toBe(false)
    })

    it('handleDeleteConfirm does nothing when already deleting', async () => {
      const ctx = mountComposable()
      await nextTick()

      ctx.result.isDeleting.value = true
      await ctx.result.handleDeleteClick()
      await ctx.result.handleDeleteConfirm()

      expect(mockBoardStore.deleteCard).not.toHaveBeenCalled()
    })

    it('handleDeleteConfirm handles error and resets isDeleting', async () => {
      mockBoardStore.deleteCard.mockRejectedValue(new Error('Delete failed'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      await ctx.result.handleDeleteClick()
      await ctx.result.handleDeleteConfirm()

      expect(consoleSpy).toHaveBeenCalledWith('Failed to delete card:', expect.any(Error))
      expect(ctx.result.isDeleting.value).toBe(false)

      consoleSpy.mockRestore()
    })
  })

  // -------------------------------------------------------------------------
  // Delete preview request ownership (GH-2968)
  // -------------------------------------------------------------------------

  describe('delete preview ownership', () => {
    const defaultPreview = makePreview('v1:fixed')
    const previewDetachMock = vi.mocked(cardsApi.previewDetach)
    let pending: Deferred<CardDetachPreview>[]

    beforeEach(() => {
      pending = []
      previewDetachMock.mockImplementation(() => {
        const attempt = defer<CardDetachPreview>()
        pending.push(attempt)
        return attempt.promise
      })
    })

    afterEach(() => {
      previewDetachMock.mockReset()
      previewDetachMock.mockResolvedValue(defaultPreview)
    })

    it('drops a canceled attempt that resolves after the reopened one started', async () => {
      const ctx = mountComposable()
      await nextTick()

      const first = ctx.result.handleDeleteClick()
      ctx.result.handleDeleteCancel()
      const second = ctx.result.handleDeleteClick()
      expect(pending).toHaveLength(2)

      pending[0].resolve(makePreview('v1:stale'))
      await flush()

      // The stale reply must not populate the new dialog nor clear its loading state.
      expect(ctx.result.detachPreview.value).toBeNull()
      expect(ctx.result.deletePreviewError.value).toBeNull()
      expect(ctx.result.deletePreviewLoading.value).toBe(true)

      pending[1].resolve(makePreview('v2:fresh'))
      await Promise.all([first, second])
      await flush()

      expect(ctx.result.detachPreview.value?.expectedChildrenFingerprint).toBe('v2:fresh')
      expect(ctx.result.deletePreviewLoading.value).toBe(false)
    })

    it('does not let a stale attempt replace a newer preview that already landed', async () => {
      const ctx = mountComposable()
      await nextTick()

      const first = ctx.result.handleDeleteClick()
      ctx.result.handleDeleteCancel()
      const second = ctx.result.handleDeleteClick()

      pending[1].resolve(makePreview('v2:fresh'))
      await flush()
      expect(ctx.result.detachPreview.value?.expectedChildrenFingerprint).toBe('v2:fresh')
      expect(ctx.result.deletePreviewLoading.value).toBe(false)

      pending[0].resolve(makePreview('v1:stale'))
      await Promise.all([first, second])
      await flush()

      expect(ctx.result.detachPreview.value?.expectedChildrenFingerprint).toBe('v2:fresh')
      expect(ctx.result.deletePreviewLoading.value).toBe(false)
    })

    it('does not surface a stale attempt failure as an error in the new dialog', async () => {
      const ctx = mountComposable()
      await nextTick()

      const first = ctx.result.handleDeleteClick()
      ctx.result.handleDeleteCancel()
      const second = ctx.result.handleDeleteClick()

      pending[0].reject(new Error('preview failed'))
      await flush()

      expect(ctx.result.deletePreviewError.value).toBeNull()
      expect(ctx.result.deletePreviewLoading.value).toBe(true)

      pending[1].resolve(makePreview('v2:fresh'))
      await Promise.all([first, second])
      await flush()

      expect(ctx.result.deletePreviewError.value).toBeNull()
      expect(ctx.result.detachPreview.value?.expectedChildrenFingerprint).toBe('v2:fresh')
    })

    it('cancel clears the displayed child list, the error and the loading state', async () => {
      const ctx = mountComposable()
      await nextTick()

      const attempt = ctx.result.handleDeleteClick()
      pending[0].resolve(makePreview('v1:shown'))
      await attempt
      await flush()
      expect(ctx.result.detachPreview.value?.expectedChildrenFingerprint).toBe('v1:shown')

      ctx.result.handleDeleteCancel()

      expect(ctx.result.showDeleteConfirm.value).toBe(false)
      expect(ctx.result.detachPreview.value).toBeNull()
      expect(ctx.result.deletePreviewError.value).toBeNull()
      expect(ctx.result.deletePreviewLoading.value).toBe(false)
    })

    it('discards an in-flight preview when the card changes mid-flight', async () => {
      const ctx = mountComposable()
      await nextTick()

      const attempt = ctx.result.handleDeleteClick()
      expect(ctx.result.deletePreviewLoading.value).toBe(true)

      ctx.cardRef.value = makeCard({ id: 'card-2', title: 'Other Card' })
      await nextTick()

      pending[0].resolve(makePreview('v1:card-1'))
      await attempt
      await flush()

      expect(ctx.result.detachPreview.value).toBeNull()
      expect(ctx.result.deletePreviewError.value).toBeNull()
      // The finally effect of the abandoned attempt must not leave the next dialog spinning.
      expect(ctx.result.deletePreviewLoading.value).toBe(false)
    })

    it('discards an in-flight preview when the card modal closes', async () => {
      const ctx = mountComposable()
      ctx.isOpenRef.value = true
      await nextTick()
      await flush()

      const attempt = ctx.result.handleDeleteClick()
      ctx.isOpenRef.value = false
      await nextTick()
      await flush()

      expect(ctx.result.showDeleteConfirm.value).toBe(false)

      pending[0].resolve(makePreview('v1:closed'))
      await attempt
      await flush()

      expect(ctx.result.detachPreview.value).toBeNull()
      expect(ctx.result.deletePreviewLoading.value).toBe(false)
    })

    it('confirms deletion against the child list the user is currently shown', async () => {
      const ctx = mountComposable()
      await nextTick()

      const first = ctx.result.handleDeleteClick()
      ctx.result.handleDeleteCancel()
      const second = ctx.result.handleDeleteClick()

      pending[1].resolve(makePreview('v2:fresh'))
      await flush()
      pending[0].resolve(makePreview('v1:stale'))
      await Promise.all([first, second])
      await flush()

      await ctx.result.handleDeleteConfirm()

      expect(mockBoardStore.deleteCard).toHaveBeenCalledWith(
        'board-1',
        'card-1',
        expect.objectContaining({ expectedChildrenFingerprint: 'v2:fresh' }),
      )
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
    it.each(['success', 'failure'] as const)(
      'keeps card-save recovery usable after comment update %s',
      async (outcome) => {
        const ctx = mountComposable()
        await nextTick()
        ctx.result.workItemType.value = 'Epic'
        mockBoardStore.updateCard.mockRejectedValueOnce(new Error('card save unavailable'))
        await ctx.result.handleSave()
        const cardSaveError = ctx.result.saveError.value
        expect(cardSaveError).toBeTruthy()

        ctx.result.editingCommentId.value = 'c-1'
        ctx.result.editingCommentContent.value = 'Edited comment'
        if (outcome === 'failure') {
          mockBoardStore.updateCardComment.mockRejectedValueOnce(new Error('comment save unavailable'))
        }
        await ctx.result.handleSaveEditComment('c-1')

        expect(ctx.result.isSaving.value).toBe(false)
        expect(ctx.result.saveError.value).toBe(cardSaveError)
        expect(ctx.result.workItemType.value).toBe('Epic')
        await ctx.result.handleSave()
        expect(mockBoardStore.updateCard).toHaveBeenCalledTimes(2)
        expect(mockBoardStore.updateCard).toHaveBeenLastCalledWith(
          'board-1', 'card-1', expect.objectContaining({ workItemType: 'Epic' }),
        )
        expect(ctx.onClose).toHaveBeenCalledOnce()
        ctx.wrapper.unmount()
      },
    )

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
    it('opens an in-app confirmation before deleting the comment', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      ctx.result.handleDeleteComment(comment)

      expect(ctx.result.commentPendingDeletion.value).toEqual(comment)
      expect(ctx.result.showCommentDeleteConfirm.value).toBe(true)
      expect(mockBoardStore.deleteCardComment).not.toHaveBeenCalled()
    })

    it('does nothing when user is not the author', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-other' })
      await ctx.result.handleDeleteComment(comment)

      expect(mockBoardStore.deleteCardComment).not.toHaveBeenCalled()
    })

    it('clears the pending comment when confirmation is cancelled', async () => {
      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      ctx.result.handleDeleteComment(comment)
      ctx.result.handleCommentDeleteCancel()

      expect(mockBoardStore.deleteCardComment).not.toHaveBeenCalled()
      expect(ctx.result.showCommentDeleteConfirm.value).toBe(false)
      expect(ctx.result.commentPendingDeletion.value).toBeNull()
    })

    it('handles deleteCardComment failure gracefully', async () => {
      mockBoardStore.deleteCardComment.mockRejectedValue(new Error('fail'))
      const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

      const ctx = mountComposable()
      await nextTick()

      const comment = makeComment({ id: 'c-1', authorUserId: 'user-1' })
      ctx.result.handleDeleteComment(comment)
      await ctx.result.handleCommentDeleteConfirm()

      expect(consoleSpy).toHaveBeenCalledWith('Failed to delete comment:', expect.any(Error))
      expect(ctx.result.showCommentDeleteConfirm.value).toBe(true)
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
