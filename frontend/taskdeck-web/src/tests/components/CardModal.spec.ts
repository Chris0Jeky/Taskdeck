import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import CardModal from '../../components/board/CardModal.vue'
import { useBoardStore } from '../../store/boardStore'
import type { Card, Label } from '../../types/board'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

describe('CardModal', () => {
  let mockStore: any
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
    }

    vi.mocked(useBoardStore).mockReturnValue(mockStore as any)
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
      })
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

  it('should show delete confirmation before deleting', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Card'))
    await deleteButton?.trigger('click')

    expect(confirmSpy).toHaveBeenCalled()
    expect(mockStore.deleteCard).not.toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('should call deleteCard when deletion is confirmed', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mount(CardModal, {
      props: {
        card,
        isOpen: true,
        labels,
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Card'))
    await deleteButton?.trigger('click')

    await wrapper.vm.$nextTick()

    expect(mockStore.deleteCard).toHaveBeenCalledWith('board-1', 'card-1')
    expect(wrapper.emitted('close')).toBeTruthy()

    confirmSpy.mockRestore()
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
})
