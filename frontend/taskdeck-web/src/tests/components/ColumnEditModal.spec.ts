import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ColumnEditModal from '../../components/board/ColumnEditModal.vue'
import { useBoardStore } from '../../store/boardStore'
import type { Column } from '../../types/board'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

describe('ColumnEditModal', () => {
  let mockStore: any
  let column: Column

  beforeEach(() => {
    setActivePinia(createPinia())

    column = {
      id: 'column-1',
      boardId: 'board-1',
      name: 'To Do',
      position: 0,
      wipLimit: null,
      cardCount: 0,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }

    mockStore = {
      updateColumn: vi.fn().mockResolvedValue(column),
      deleteColumn: vi.fn().mockResolvedValue(undefined),
    }

    vi.mocked(useBoardStore).mockReturnValue(mockStore as any)
  })

  it('should render when isOpen is true', () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    expect(wrapper.find('h2').text()).toBe('Edit Column')
    expect(wrapper.find('#column-name').exists()).toBe(true)
  })

  it('should not render when isOpen is false', () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: false,
        boardId: 'board-1',
      },
    })

    expect(wrapper.find('h2').exists()).toBe(false)
  })

  it('should populate form fields with column data', () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const nameInput = wrapper.find('#column-name') as any
    const wipCheckbox = wrapper.find('#column-has-wip-limit') as any

    expect(nameInput.element.value).toBe('To Do')
    expect(wipCheckbox.element.checked).toBe(false)
  })

  it('should show WIP limit input when column has WIP limit', () => {
    const columnWithWip = {
      ...column,
      wipLimit: 5,
    }

    const wrapper = mount(ColumnEditModal, {
      props: {
        column: columnWithWip,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const wipCheckbox = wrapper.find('#column-has-wip-limit') as any
    expect(wipCheckbox.element.checked).toBe(true)

    const wipLimitInput = wrapper.find('#wip-limit')
    expect(wipLimitInput.exists()).toBe(true)
  })

  it('should emit close event when close button is clicked', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const closeButton = wrapper.findAll('button').find((btn) =>
      btn.html().includes('M6 18L18 6M6 6l12 12')
    )
    await closeButton?.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should call updateColumn when save is clicked', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const nameInput = wrapper.find('#column-name')
    await nameInput.setValue('In Progress')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateColumn).toHaveBeenCalledWith(
      'board-1',
      'column-1',
      expect.objectContaining({
        name: 'In Progress',
      })
    )
  })

  it('should emit updated and close events after successful save', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('updated')).toBeTruthy()
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should disable save button when name is empty', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const nameInput = wrapper.find('#column-name')
    await nameInput.setValue('')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should disable save button when WIP limit is enabled but invalid', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const wipCheckbox = wrapper.find('#column-has-wip-limit')
    await wipCheckbox.setValue(true)

    await wrapper.vm.$nextTick()

    const wipLimitInput = wrapper.find('#wip-limit')
    await wipLimitInput.setValue(0)

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should visually disable delete button when column has cards', () => {
    const columnWithCards = {
      ...column,
      cardCount: 3,
    }

    const wrapper = mount(ColumnEditModal, {
      props: {
        column: columnWithCards,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Column'))

    expect(deleteButton?.classes()).toContain('opacity-50')
    expect((deleteButton?.element as HTMLButtonElement).disabled).toBe(false)
  })

  it('should show alert when trying to delete column with cards', async () => {
    const columnWithCards = {
      ...column,
      cardCount: 3,
    }

    const alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => {})

    const wrapper = mount(ColumnEditModal, {
      props: {
        column: columnWithCards,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Column'))
    await deleteButton?.trigger('click')

    expect(alertSpy).toHaveBeenCalled()
    expect(mockStore.deleteColumn).not.toHaveBeenCalled()

    alertSpy.mockRestore()
  })

  it('should show confirmation before deleting empty column', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Column'))
    await deleteButton?.trigger('click')

    expect(confirmSpy).toHaveBeenCalled()
    expect(mockStore.deleteColumn).not.toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('should call deleteColumn when deletion is confirmed', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Column'))
    await deleteButton?.trigger('click')

    await wrapper.vm.$nextTick()

    expect(mockStore.deleteColumn).toHaveBeenCalledWith('board-1', 'column-1')
    expect(wrapper.emitted('close')).toBeTruthy()

    confirmSpy.mockRestore()
  })

  it('should display column metadata', () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const metadataSection = wrapper.html()
    expect(metadataSection).toContain('Position:')
    expect(metadataSection).toContain('Cards:')
    expect(metadataSection).toContain('Created:')
  })

  it('should handle WIP limit toggle', async () => {
    const wrapper = mount(ColumnEditModal, {
      props: {
        column,
        isOpen: true,
        boardId: 'board-1',
      },
    })

    const wipCheckbox = wrapper.find('#column-has-wip-limit')
    await wipCheckbox.setValue(true)

    await wrapper.vm.$nextTick()

    const wipLimitInput = wrapper.find('#wip-limit')
    await wipLimitInput.setValue(5)

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateColumn).toHaveBeenCalledWith(
      'board-1',
      'column-1',
      expect.objectContaining({
        wipLimit: 5,
      })
    )
  })
})
