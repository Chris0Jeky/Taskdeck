import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ArchiveView from '../../views/ArchiveView.vue'

const mocks = vi.hoisted(() => ({
  getItems: vi.fn(),
  restoreItem: vi.fn(),
  getBoards: vi.fn(),
  updateBoard: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
}))

vi.mock('../../api/archiveApi', () => ({
  archiveApi: {
    getItems: mocks.getItems,
    restoreItem: mocks.restoreItem,
  },
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: mocks.getBoards,
    updateBoard: mocks.updateBoard,
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

async function waitForAsyncUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('ArchiveView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.getItems.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([])
  })

  it('renders archived boards from boards API and archived recovery items', async () => {
    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Archived Card',
        archivedByUserId: 'user-1',
        archivedAt: new Date().toISOString(),
        reason: null,
        restoreStatus: 'Available',
        restoredAt: null,
        restoredByUserId: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-archived',
        name: 'Board To Restore',
        description: null,
        isArchived: true,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: 'board-active',
        name: 'Active Board',
        description: null,
        isArchived: false,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Board To Restore')
    expect(wrapper.text()).toContain('Archived Card')
    expect(mocks.getBoards).toHaveBeenCalledWith(undefined, true)
  })

  it('keeps archived items visible when archived boards loading fails', async () => {
    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Recoverable Card',
        archivedByUserId: 'user-1',
        archivedAt: new Date().toISOString(),
        reason: null,
        restoreStatus: 'Available',
        restoredAt: null,
        restoredByUserId: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.getBoards.mockRejectedValue(new Error('boards request failed'))

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Recoverable Card')
    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to load archived boards')
  })

  it('does not refetch boards when only archive item filter changes', async () => {
    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(mocks.getBoards).toHaveBeenCalledTimes(1)
    expect(mocks.getItems).toHaveBeenCalledTimes(1)
    expect(mocks.getItems).toHaveBeenNthCalledWith(1, {
      entityType: undefined,
      limit: 200,
    })

    const entityFilter = wrapper.find('select')
    await entityFilter.setValue('card')
    await waitForAsyncUi()

    expect(mocks.getBoards).toHaveBeenCalledTimes(1)
    expect(mocks.getItems).toHaveBeenCalledTimes(2)
    expect(mocks.getItems).toHaveBeenNthCalledWith(2, {
      entityType: 'card',
      limit: 200,
    })
  })

  it('restores archived board from archive view', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-archived',
        name: 'Archived Board',
        description: null,
        isArchived: true,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])
    mocks.updateBoard.mockResolvedValue({
      id: 'board-archived',
      name: 'Board To Restore',
      description: null,
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    })

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreBoardButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Restore Board'))
    await restoreBoardButton?.trigger('click')
    await waitForAsyncUi()

    expect(mocks.updateBoard).toHaveBeenCalledWith('board-archived', { isArchived: false })
    expect(mocks.successToast).toHaveBeenCalledWith('Restored board "Archived Board"')
    expect(wrapper.findAll('.td-archive-list--section .td-archive-row')).toHaveLength(0)

    confirmSpy.mockRestore()
  })

  it('hides archived board from default list and reveals it when hidden toggle is enabled', async () => {
    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-archived-1',
        name: 'Board Hidden Candidate',
        description: null,
        isArchived: true,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const hideButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Hide'))
    await hideButton?.trigger('click')
    await waitForAsyncUi()

    expect(wrapper.text()).not.toContain('Board Hidden Candidate')
    expect(wrapper.text()).toContain('Show Hidden Boards (1)')
    expect(localStorage.getItem('taskdeck_archive_hidden_boards')).toContain('board-archived-1')

    const showHiddenButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Show Hidden Boards'))
    await showHiddenButton?.trigger('click')
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Board Hidden Candidate')
    expect(wrapper.text()).toContain('Unhide')
  })
})
