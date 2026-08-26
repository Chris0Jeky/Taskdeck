import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import CardModal from '../../components/board/CardModal.vue'
import { useBoardStore } from '../../store/boardStore'
import { useSessionStore } from '../../store/sessionStore'
import type { Card, Label } from '../../types/board'
import type { CardComment } from '../../types/comments'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: vi.fn(),
}))

describe('CardModal', () => {
  let mockStore: any
  let mockSessionStore: { userId: string }
  let card: Card
  let labels: Label[]

  beforeEach(() => {
    setActivePinia(createPinia())

    card = {
      id: 'card-1',
      boardId: 'board-1',
      columnId: 'column-1',
      title: 'Test Card',
      description: 'Test description',
      position: 0,
      dueDate: '2025-12-31',
      isBlocked: false,
      blockReason: null,
      labels: [],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    labels = [
      {
        id: 'label-1',
        boardId: 'board-1',
        name: 'Bug',
        colorHex: '#EF4444',
        createdAt: new Date().toISOString(),
      },
      {
        id: 'label-2',
        boardId: 'board-1',
        name: 'Feature',
        colorHex: '#10B981',
        createdAt: new Date().toISOString(),
      },
    ]

    mockStore = {
      updateCard: vi.fn().mockResolvedValue(card),
      deleteCard: vi.fn().mockResolvedValue(undefined),
      fetchCardComments: vi.fn().mockResolvedValue([]),
      fetchCardProvenance: vi.fn().mockResolvedValue(null),
      getCardComments: vi.fn().mockReturnValue([]),
      createCardComment: vi.fn().mockResolvedValue(undefined),
      updateCardComment: vi.fn().mockResolvedValue(undefined),
      deleteCardComment: vi.fn().mockResolvedValue(undefined),
      editingCardId: null,
      setEditingCard: vi.fn((cardId: string | null) => {
        mockStore.editingCardId = cardId
      }),
    }
    mockSessionStore = { userId: 'user-1' }

    vi.mocked(useBoardStore).mockReturnValue(mockStore as any)
    vi.mocked(useSessionStore).mockReturnValue(mockSessionStore as any)
  })

  it('should request capture provenance when modal opens', async () => {
    mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    await Promise.resolve()
    await Promise.resolve()

    expect(mockStore.fetchCardProvenance).toHaveBeenCalledWith('board-1', 'card-1')
    expect(mockStore.fetchCardProvenance).toHaveBeenCalledTimes(1)
  })

  it('keeps provenance responses scoped to the card that requested them', async () => {
    let resolveFirst!: (value: any) => void
    let resolveSecond!: (value: any) => void
    mockStore.fetchCardProvenance.mockImplementation((_boardId: string, cardId: string) => (
      new Promise((resolve) => {
        if (cardId === 'card-1') resolveFirst = resolve
        else resolveSecond = resolve
      })
    ))
    const secondCard: Card = {
      ...card,
      id: 'card-2',
      title: 'Second Card',
      updatedAt: '2026-08-26T12:00:00.000Z',
    }
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels },
    })

    await nextTick()
    await wrapper.setProps({ card: secondCard })
    await nextTick()

    expect(mockStore.fetchCardProvenance).toHaveBeenCalledWith('board-1', 'card-1')
    expect(mockStore.fetchCardProvenance).toHaveBeenCalledWith('board-1', 'card-2')

    resolveFirst({
      cardId: 'card-1',
      captureItemId: 'capture-first',
      proposalId: null,
      proposalStatus: 'Applied',
      triageRunId: null,
    })
    await flushPromises()
    expect(wrapper.find('a[href*="capture-first"]').exists()).toBe(false)

    resolveSecond({
      cardId: 'card-2',
      captureItemId: 'capture-second',
      proposalId: null,
      proposalStatus: 'Applied',
      triageRunId: null,
    })
    await flushPromises()
    expect(wrapper.find('a[href="/workspace/inbox?boardId=board-1#capture-capture-second"]').exists()).toBe(true)
  })

  it('should render capture provenance marker and links when available', async () => {
    mockStore.fetchCardProvenance.mockResolvedValue({
      cardId: 'card-1',
      captureItemId: 'capture-7',
      proposalId: 'proposal-9',
      proposalStatus: 'Applied',
      triageRunId: 'triage-5',
    })

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    await Promise.resolve()
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Capture Origin')
    expect(wrapper.find('a[href="/workspace/inbox?boardId=board-1#capture-capture-7"]').exists()).toBe(true)
    expect(wrapper.find('a[href="/workspace/review?boardId=board-1#proposal-proposal-9"]').exists()).toBe(true)
  })

  it('should render when isOpen is true', () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    expect(wrapper.find('h2').text()).toBe('Edit Card')
    expect(wrapper.find('#card-title').exists()).toBe(true)
  })

  it('should fall back to the layout viewport when visualViewport is unavailable', () => {
    const descriptor = Object.getOwnPropertyDescriptor(window, 'visualViewport')

    try {
      Object.defineProperty(window, 'visualViewport', {
        configurable: true,
        value: undefined,
      })

      const wrapper = mount(CardModal, {
        props: {
          card,
          isOpen: true,
          labels,
        },
      })
      const style = (wrapper.find('[role="dialog"]').element as HTMLElement).style

      expect(style.getPropertyValue('--card-modal-visual-viewport-height')).toBe(
        `${window.innerHeight}px`,
      )
      expect(style.getPropertyValue('--card-modal-visual-viewport-offset-top')).toBe('0px')

      wrapper.unmount()
    } finally {
      if (descriptor) {
        Object.defineProperty(window, 'visualViewport', descriptor)
      } else {
        Reflect.deleteProperty(window, 'visualViewport')
      }
    }
  })

  it('should focus the close control when opened and restore the invoking card focus', async () => {
    const opener = document.createElement('button')
    opener.type = 'button'
    opener.textContent = 'Open card'
    document.body.appendChild(opener)
    opener.focus()

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
      attachTo: document.body,
    })

    await nextTick()
    expect(document.activeElement).toBe(
      wrapper.find('[aria-label="Close card editor"]').element,
    )

    await wrapper.setProps({ isOpen: false })
    await nextTick()
    expect(document.activeElement).toBe(opener)

    wrapper.unmount()
    opener.remove()
  })

  it('should keep Tab and Shift+Tab inside the dialog focus cycle', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
      attachTo: document.body,
    })

    await nextTick()
    const dialog = wrapper.find('[role="dialog"]')
    const focusable = dialog.findAll('a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')
    const first = focusable[0]!
    const last = focusable[focusable.length - 1]!

    ;(first.element as HTMLElement).focus()
    await dialog.trigger('keydown', { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(last.element)

    ;(last.element as HTMLElement).focus()
    await dialog.trigger('keydown', { key: 'Tab' })
    expect(document.activeElement).toBe(first.element)

    wrapper.unmount()
  })

  it('renders the desktop inspector as non-modal without trapping board focus', async () => {
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation: 'inspector' },
      attachTo: document.body,
    })
    await nextTick()

    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBeUndefined()
    expect(wrapper.get('[data-testid="card-modal-scroll-region"]').attributes('data-presentation')).toBe('inspector')

    const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    dialog.element.dispatchEvent(tab)
    expect(tab.defaultPrevented).toBe(false)

    wrapper.unmount()
  })

  it('requires explicit confirmation before closing an unsaved card draft', async () => {
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels, presentation: 'inspector' },
      attachTo: document.body,
    })
    await nextTick()

    await wrapper.get('#card-title').setValue('Unsaved title')
    await wrapper.get('[aria-label="Close card editor"]').trigger('click')
    await nextTick()

    expect(wrapper.emitted('close')).toBeUndefined()
    expect(document.body.querySelector('[data-testid="card-discard-confirm"]')).not.toBeNull()

    ;(document.body.querySelector('[data-testid="card-discard-confirm"]') as HTMLButtonElement).click()
    await nextTick()

    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('should not render when isOpen is false', () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: false,
        labels,
      },
    })

    expect(wrapper.find('h2').exists()).toBe(false)
  })

  it('should populate form fields with card data', () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const titleInput = wrapper.find('#card-title') as any
    const descriptionInput = wrapper.find('#card-description') as any
    const dueDateInput = wrapper.find('#card-due-date') as any

    expect(titleInput.element.value).toBe('Test Card')
    expect(descriptionInput.element.value).toBe('Test description')
    expect(dueDateInput.element.value).toBe('2025-12-31')
  })

  it('should show block reason input when card is blocked', async () => {
    const blockedCard = {
      ...card,
      isBlocked: true,
      blockReason: 'Waiting for dependencies',
    }

    const wrapper = mount(CardModal, {
      props: {
        card: blockedCard,
        isOpen: true,
        labels,
      },
    })

    const blockCheckbox = wrapper.find('#card-is-blocked') as any
    expect(blockCheckbox.element.checked).toBe(true)

    const blockReasonInput = wrapper.find('#card-block-reason')
    expect(blockReasonInput.exists()).toBe(true)
  })

  it('should emit close event when close button is clicked', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const closeButton = wrapper.findAll('button').find((btn) =>
      btn.html().includes('M6 18L18 6M6 6l12 12')
    )
    await closeButton?.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should emit close event when Escape key is pressed', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should call updateCard when form is submitted', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const titleInput = wrapper.find('#card-title')
    await titleInput.setValue('Updated Title')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateCard).toHaveBeenCalledWith(
      'board-1',
      'card-1',
      expect.objectContaining({
        title: 'Updated Title',
        expectedUpdatedAt: card.updatedAt,
      })
    )
  })

  it('resets drafts, concurrency state, and presence when the open card changes', async () => {
    mockStore.getCardComments.mockReturnValue([makeOwnComment()])
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels },
    })
    await flushPromises()

    await wrapper.get('#card-title').setValue('Unsaved title')
    await wrapper.get('#new-card-comment').setValue('Unsaved comment')
    await wrapper.get('textarea[aria-label="Reply to comment"]').setValue('Unsaved reply')
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])

    const secondCard: Card = {
      ...card,
      id: 'card-2',
      title: 'Second Card',
      description: 'Second description',
      updatedAt: '2026-08-26T12:00:00.000Z',
    }
    await wrapper.setProps({ card: secondCard })
    await flushPromises()

    expect((wrapper.get('#card-title').element as HTMLInputElement).value).toBe('Second Card')
    expect((wrapper.get('#new-card-comment').element as HTMLTextAreaElement).value).toBe('')
    expect((wrapper.get('textarea[aria-label="Reply to comment"]').element as HTMLTextAreaElement).value).toBe('')
    expect(mockStore.setEditingCard).toHaveBeenCalledWith(null)
    expect(mockStore.setEditingCard).toHaveBeenCalledWith('card-2')

    const saveButton = wrapper.findAll('button').find((button) => button.text().includes('Save Changes'))
    await saveButton?.trigger('click')
    expect(mockStore.updateCard).toHaveBeenCalledWith(
      'board-1',
      'card-2',
      expect.objectContaining({ expectedUpdatedAt: secondCard.updatedAt }),
    )
  })

  it('should emit updated event after successful save', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('updated')).toBeTruthy()
  })

  it('should disable save button when title is empty', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const titleInput = wrapper.find('#card-title')
    await titleInput.setValue('')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should disable save button when blocked but no reason provided', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const blockCheckbox = wrapper.find('#card-is-blocked')
    await blockCheckbox.setValue(true)

    await wrapper.vm.$nextTick()

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should show delete confirmation dialog before deleting', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
      attachTo: document.body,
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Card'))
    await deleteButton?.trigger('click')
    await wrapper.vm.$nextTick()

    // Dialog should now be open; deleteCard must NOT have been called yet
    expect(mockStore.deleteCard).not.toHaveBeenCalled()
    // The confirmation dialog should be visible in the DOM
    expect(document.querySelector('.td-dialog')).not.toBeNull()

    wrapper.unmount()
  })

  it('should call deleteCard when deletion is confirmed in dialog', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
      attachTo: document.body,
    })

    // Open the confirmation dialog
    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Card'))
    await deleteButton?.trigger('click')
    await wrapper.vm.$nextTick()

    // Click the "Delete" confirm button inside the dialog
    const confirmButton = Array.from(document.querySelectorAll<HTMLButtonElement>('button')).find(
      (btn) => btn.textContent?.trim() === 'Delete',
    )
    confirmButton?.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    expect(mockStore.deleteCard).toHaveBeenCalledWith('board-1', 'card-1')
    expect(wrapper.emitted('close')).toBeTruthy()

    wrapper.unmount()
  })

  it('should handle label selection', async () => {
    const cardWithLabel = {
      ...card,
      labels: [labels[0]],
    }

    const wrapper = mount(CardModal, {
      props: {
        card: cardWithLabel,
        isOpen: true,
        labels,
      },
    })

    // Find label checkboxes
    const labelCheckboxes = wrapper.findAll('input[type="checkbox"]')
    // First checkbox is "blocked", so labels start at index 1
    const bugLabelCheckbox = labelCheckboxes[1] as any

    expect(bugLabelCheckbox.element.checked).toBe(true)
  })

  it('should create a new comment from modal comment input', async () => {
    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const commentInput = wrapper.find('#new-card-comment')
    await commentInput.setValue('New card comment')

    const addCommentButton = wrapper.find('#add-card-comment')
    await addCommentButton.trigger('click')

    expect(mockStore.createCardComment).toHaveBeenCalledWith('board-1', 'card-1', {
      content: 'New card comment',
      parentCommentId: null,
    })
  })

  function makeOwnComment(): CardComment {
    return {
      id: 'comment-1',
      boardId: 'board-1',
      cardId: 'card-1',
      parentCommentId: null,
      authorUserId: 'user-1',
      authorUsername: 'testuser',
      content: 'Delete me',
      isDeleted: false,
      editedAt: null,
      mentions: [],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
  }

  async function openCommentDeleteDialog(wrapper: ReturnType<typeof mount>) {
    await nextTick()
    const deleteButton = wrapper.findAll('button').find((button) => button.text().trim() === 'Delete')
    expect(deleteButton).toBeDefined()
    ;(deleteButton!.element as HTMLButtonElement).focus()
    await deleteButton!.trigger('click')
    await nextTick()
    return deleteButton!
  }

  it('cancels comment deletion through the dialog and restores focus after Escape', async () => {
    mockStore.getCardComments.mockReturnValue([makeOwnComment()])
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels },
      attachTo: document.body,
    })

    const deleteButton = await openCommentDeleteDialog(wrapper)
    expect(document.body.querySelector('[data-testid="card-comment-delete-confirm"]')).not.toBeNull()

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()

    expect(mockStore.deleteCardComment).not.toHaveBeenCalled()
    expect(document.body.querySelector('[data-testid="card-comment-delete-confirm"]')).toBeNull()
    expect(document.activeElement).toBe(deleteButton.element)
    expect(wrapper.emitted('close')).toBeUndefined()

    wrapper.unmount()
  })

  it('deletes a confirmed comment exactly once through the rendered dialog', async () => {
    mockStore.getCardComments.mockReturnValue([makeOwnComment()])
    const wrapper = mount(CardModal, {
      props: { card, isOpen: true, labels },
      attachTo: document.body,
    })

    await openCommentDeleteDialog(wrapper)
    const confirm = document.body.querySelector(
      '[data-testid="card-comment-delete-confirm"]',
    ) as HTMLButtonElement
    confirm.click()
    confirm.click()
    await nextTick()
    await nextTick()

    expect(mockStore.deleteCardComment).toHaveBeenCalledTimes(1)
    expect(mockStore.deleteCardComment).toHaveBeenCalledWith('board-1', 'card-1', 'comment-1')

    wrapper.unmount()
  })

  it('should render "Created manually" empty state when capture provenance is unavailable (manual card)', async () => {
    mockStore.fetchCardProvenance.mockResolvedValue(null)

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    await Promise.resolve()
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    const emptyState = wrapper.find('[data-testid="provenance-empty-state"]')
    expect(emptyState.exists()).toBe(true)
    expect(emptyState.text()).toContain('Created manually')
    // No error alert should be shown for an expected empty state
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('should render a provenance error message when capture provenance fetch fails', async () => {
    mockStore.fetchCardProvenance.mockRejectedValue(new Error('provenance unavailable'))

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    await Promise.resolve()
    await Promise.resolve()
    await wrapper.vm.$nextTick()

    expect(mockStore.fetchCardProvenance).toHaveBeenCalledWith('board-1', 'card-1')
    expect(wrapper.text()).toContain('Unable to load capture provenance.')
    expect(wrapper.find('[data-testid="provenance-empty-state"]').exists()).toBe(false)
  })
})
