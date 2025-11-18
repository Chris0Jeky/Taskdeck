import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import BoardSettingsModal from '../../components/board/BoardSettingsModal.vue'
import { useBoardStore } from '../../store/boardStore'
import type { Board } from '../../types/board'

const mockRouter = {
  push: vi.fn(),
}

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
}))

describe('BoardSettingsModal', () => {
  let mockStore: any
  let board: Board

  beforeEach(() => {
    setActivePinia(createPinia())

    board = {
      id: 'board-1',
      name: 'Test Board',
      description: 'Test description',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      columns: [],
    }

    mockStore = {
      updateBoard: vi.fn().mockResolvedValue(board),
      deleteBoard: vi.fn().mockResolvedValue(undefined),
    }

    vi.mocked(useBoardStore).mockReturnValue(mockStore as any)
    mockRouter.push.mockClear()
  })

  it('should render when isOpen is true', () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    expect(wrapper.find('h2').text()).toBe('Board Settings')
    expect(wrapper.find('#board-name').exists()).toBe(true)
  })

  it('should not render when isOpen is false', () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: false,
      },
    })

    expect(wrapper.find('h2').exists()).toBe(false)
  })

  it('should populate form fields with board data', () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const nameInput = wrapper.find('#board-name') as any
    const descriptionInput = wrapper.find('#board-description') as any
    const archivedCheckbox = wrapper.find('#board-archived') as any

    expect(nameInput.element.value).toBe('Test Board')
    expect(descriptionInput.element.value).toBe('Test description')
    expect(archivedCheckbox.element.checked).toBe(false)
  })

  it('should emit close event when close button is clicked', async () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const closeButton = wrapper.findAll('button').find((btn) =>
      btn.html().includes('M6 18L18 6M6 6l12 12')
    )
    await closeButton?.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should call updateBoard when save is clicked', async () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const nameInput = wrapper.find('#board-name')
    await nameInput.setValue('Updated Board Name')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateBoard).toHaveBeenCalledWith(
      'board-1',
      expect.objectContaining({
        name: 'Updated Board Name',
      })
    )
  })

  it('should emit updated and close events after successful save', async () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
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
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const nameInput = wrapper.find('#board-name')
    await nameInput.setValue('')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should show delete confirmation before deleting', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Board'))
    await deleteButton?.trigger('click')

    expect(confirmSpy).toHaveBeenCalled()
    expect(mockStore.deleteBoard).not.toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('should call deleteBoard and navigate when deletion is confirmed', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const deleteButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Delete Board'))
    await deleteButton?.trigger('click')

    await wrapper.vm.$nextTick()

    expect(mockStore.deleteBoard).toHaveBeenCalledWith('board-1')
    expect(wrapper.emitted('close')).toBeTruthy()
    expect(mockRouter.push).toHaveBeenCalledWith('/boards')

    confirmSpy.mockRestore()
  })

  it('should handle archived board toggle', async () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const archivedCheckbox = wrapper.find('#board-archived')
    await archivedCheckbox.setValue(true)

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateBoard).toHaveBeenCalledWith(
      'board-1',
      expect.objectContaining({
        isArchived: true,
      })
    )
  })

  it('should only send changed fields to updateBoard', async () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    // Don't change name or isArchived, only description
    const descriptionInput = wrapper.find('#board-description')
    await descriptionInput.setValue('New description')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateBoard).toHaveBeenCalledWith('board-1', {
      name: null,
      description: 'New description',
      isArchived: null,
    })
  })

  it('should display board metadata', () => {
    const wrapper = mount(BoardSettingsModal, {
      props: {
        board,
        isOpen: true,
      },
    })

    const metadataSection = wrapper.html()
    expect(metadataSection).toContain('Created:')
    expect(metadataSection).toContain('Last updated:')
  })
})
