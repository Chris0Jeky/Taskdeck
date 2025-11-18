import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import LabelManagerModal from '../../components/board/LabelManagerModal.vue'
import { useBoardStore } from '../../store/boardStore'
import type { Label } from '../../types/board'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

describe('LabelManagerModal', () => {
  let mockStore: any
  let labels: Label[]

  beforeEach(() => {
    setActivePinia(createPinia())

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
      createLabel: vi.fn(),
      updateLabel: vi.fn(),
      deleteLabel: vi.fn().mockResolvedValue(undefined),
    }

    vi.mocked(useBoardStore).mockReturnValue(mockStore as any)
  })

  it('should render when isOpen is true', () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    expect(wrapper.find('h2').text()).toBe('Manage Labels')
  })

  it('should not render when isOpen is false', () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: false,
        boardId: 'board-1',
        labels,
      },
    })

    expect(wrapper.find('h2').exists()).toBe(false)
  })

  it('should display labels sorted alphabetically', () => {
    const unsortedLabels = [
      {
        id: 'label-3',
        boardId: 'board-1',
        name: 'Zebra',
        colorHex: '#EF4444',
        createdAt: new Date().toISOString(),
      },
      {
        id: 'label-1',
        boardId: 'board-1',
        name: 'Apple',
        colorHex: '#10B981',
        createdAt: new Date().toISOString(),
      },
    ]

    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: unsortedLabels,
      },
    })

    const labelElements = wrapper.findAll('.rounded-md.text-sm.font-medium')
    expect(labelElements[0].text()).toBe('Apple')
    expect(labelElements[1].text()).toBe('Zebra')
  })

  it('should show create form when create button is clicked', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    expect(wrapper.find('#label-name').exists()).toBe(true)
    expect(wrapper.text()).toContain('Create New Label')
  })

  it('should emit close event when close button is clicked', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    const closeButton = wrapper.findAll('button').find((btn) =>
      btn.html().includes('M6 18L18 6M6 6l12 12')
    )
    await closeButton?.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should emit close when Done button is clicked', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    const doneButton = wrapper.findAll('button').find((btn) => btn.text() === 'Done')
    await doneButton?.trigger('click')

    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('should call createLabel when creating a new label', async () => {
    const newLabel: Label = {
      id: 'label-3',
      boardId: 'board-1',
      name: 'Enhancement',
      colorHex: '#3B82F6',
      createdAt: new Date().toISOString(),
    }

    mockStore.createLabel.mockResolvedValue(newLabel)

    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Click create button to show form
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    // Fill in form
    const nameInput = wrapper.find('#label-name')
    await nameInput.setValue('Enhancement')

    // Click save button
    const saveButton = wrapper.findAll('button').find((btn) => btn.text() === 'Create')
    await saveButton?.trigger('click')

    expect(mockStore.createLabel).toHaveBeenCalledWith('board-1', {
      name: 'Enhancement',
      colorHex: '#EF4444', // Default first color in palette
    })
  })

  it('should show edit form when edit button is clicked', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    // Find and click edit button for first label
    const editButtons = wrapper.findAll('button[title="Edit"]')
    await editButtons[0].trigger('click')

    await wrapper.vm.$nextTick()

    expect(wrapper.find('#label-name').exists()).toBe(true)
    expect(wrapper.text()).toContain('Edit Label')
    expect((wrapper.find('#label-name').element as HTMLInputElement).value).toBe('Bug')
  })

  it('should call updateLabel when editing a label', async () => {
    const updatedLabel: Label = {
      ...labels[0],
      name: 'Critical Bug',
      colorHex: '#DC2626',
    }

    mockStore.updateLabel.mockResolvedValue(updatedLabel)

    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    // Click edit button
    const editButtons = wrapper.findAll('button[title="Edit"]')
    await editButtons[0].trigger('click')

    await wrapper.vm.$nextTick()

    // Update name
    const nameInput = wrapper.find('#label-name')
    await nameInput.setValue('Critical Bug')

    // Click update button
    const updateButton = wrapper.findAll('button').find((btn) => btn.text() === 'Update')
    await updateButton?.trigger('click')

    expect(mockStore.updateLabel).toHaveBeenCalledWith(
      'board-1',
      'label-1',
      expect.objectContaining({
        name: 'Critical Bug',
      })
    )
  })

  it('should disable save button when name is empty', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Click create button
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    const nameInput = wrapper.find('#label-name')
    await nameInput.setValue('')

    const saveButton = wrapper.findAll('button').find((btn) => btn.text() === 'Create')

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should disable save button when color is invalid', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Click create button
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    const nameInput = wrapper.find('#label-name')
    await nameInput.setValue('Test Label')

    // Set invalid color
    const colorInputs = wrapper.findAll('input[type="text"]')
    const hexInput = colorInputs.find((input) =>
      (input.element as HTMLInputElement).placeholder?.includes('#')
    )
    await hexInput?.setValue('invalid')

    const saveButton = wrapper.findAll('button').find((btn) => btn.text() === 'Create')

    expect((saveButton?.element as HTMLButtonElement).disabled).toBe(true)
  })

  it('should show delete confirmation before deleting', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    const deleteButtons = wrapper.findAll('button[title="Delete"]')
    await deleteButtons[0].trigger('click')

    expect(confirmSpy).toHaveBeenCalled()
    expect(mockStore.deleteLabel).not.toHaveBeenCalled()

    confirmSpy.mockRestore()
  })

  it('should call deleteLabel when deletion is confirmed', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels,
      },
    })

    const deleteButtons = wrapper.findAll('button[title="Delete"]')
    await deleteButtons[0].trigger('click')

    await wrapper.vm.$nextTick()

    expect(mockStore.deleteLabel).toHaveBeenCalledWith('board-1', 'label-1')
    expect(wrapper.emitted('updated')).toBeTruthy()

    confirmSpy.mockRestore()
  })

  it('should cancel form when cancel button is clicked', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Show create form
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    // Click cancel
    const cancelButton = wrapper.findAll('button').find((btn) => btn.text() === 'Cancel')
    await cancelButton?.trigger('click')

    await wrapper.vm.$nextTick()

    // Form should be hidden
    expect(wrapper.find('#label-name').exists()).toBe(false)
  })

  it('should show color palette with 10 colors', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Show create form
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    // Find color palette buttons
    const colorButtons = wrapper.findAll('button.w-8.h-8.rounded-md')
    expect(colorButtons.length).toBe(10)
  })

  it('should update preview when name changes', async () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    // Show create form
    const createButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Create New Label'))
    await createButton?.trigger('click')

    await wrapper.vm.$nextTick()

    const nameInput = wrapper.find('#label-name')
    await nameInput.setValue('My New Label')

    await wrapper.vm.$nextTick()

    const preview = wrapper.html()
    expect(preview).toContain('My New Label')
  })

  it('should show empty state when no labels exist', () => {
    const wrapper = mount(LabelManagerModal, {
      props: {
        isOpen: true,
        boardId: 'board-1',
        labels: [],
      },
    })

    expect(wrapper.text()).toContain('No labels yet')
    expect(wrapper.text()).toContain('Create your first label to get started')
  })
})
