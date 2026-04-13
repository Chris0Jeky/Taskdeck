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

function findButtonByText(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper
    .findAll('button')
    .find((candidate) => candidate.text().includes(text))
}

describe('ArchiveView — item restore flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.getItems.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([])
  })

  it('restores an archive item and removes it from the list', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Restorable Card',
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
    mocks.restoreItem.mockResolvedValue({
      success: true,
      resolvedName: 'Restorable Card',
      errorMessage: null,
    })

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Restorable Card')

    const restoreButton = findButtonByText(wrapper, 'Restore')
    expect(restoreButton).toBeDefined()
    await restoreButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.restoreItem).toHaveBeenCalledWith('card', 'card-1', {
      targetBoardId: null,
      restoreMode: 0,
      conflictStrategy: 0,
    })
    expect(mocks.successToast).toHaveBeenCalledWith('Restored "Restorable Card"')
    expect(wrapper.text()).not.toContain('Restorable Card')

    confirmSpy.mockRestore()
  })

  it('does not restore when user cancels the confirmation dialog', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Card Not Restored',
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

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreButton = findButtonByText(wrapper, 'Restore')
    await restoreButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.restoreItem).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Card Not Restored')

    confirmSpy.mockRestore()
  })

  it('shows error toast when item restore fails', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'column',
        entityId: 'col-1',
        boardId: 'board-1',
        name: 'Failed Column',
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
    mocks.restoreItem.mockRejectedValue(new Error('network failure'))

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreButton = findButtonByText(wrapper, 'Restore')
    await restoreButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to restore archive item')
    // Item remains in the list
    expect(wrapper.text()).toContain('Failed Column')

    confirmSpy.mockRestore()
  })

  it('shows error toast when item restore returns success=false', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

    mocks.getItems.mockResolvedValue([
      {
        id: 'item-1',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Conflicting Card',
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
    mocks.restoreItem.mockResolvedValue({
      success: false,
      resolvedName: null,
      errorMessage: 'Conflict detected',
    })

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreButton = findButtonByText(wrapper, 'Restore')
    await restoreButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.errorToast).toHaveBeenCalledWith('Conflict detected')
    expect(wrapper.text()).toContain('Conflicting Card')

    confirmSpy.mockRestore()
  })
})

describe('ArchiveView — loading and empty states', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.getItems.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([])
  })

  it('shows empty state for items when no archive items exist', async () => {
    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('No archived items found in recovery inventory.')
  })

  it('shows empty state for boards when no archived boards exist', async () => {
    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).toContain('No archived boards found.')
  })

  it('shows error toast when archive items loading fails', async () => {
    mocks.getItems.mockRejectedValue(new Error('items request failed'))

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(mocks.errorToast).toHaveBeenCalledWith('Failed to load archive items')
    expect(wrapper.text()).toContain('No archived items found in recovery inventory.')
  })

  it('renders the Refresh Items button and reloads items on click', async () => {
    mocks.getItems.mockResolvedValueOnce([]).mockResolvedValueOnce([
      {
        id: 'item-2',
        entityType: 'card',
        entityId: 'card-2',
        boardId: 'board-1',
        name: 'Refreshed Card',
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

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    expect(wrapper.text()).not.toContain('Refreshed Card')

    const refreshButton = findButtonByText(wrapper, 'Refresh Items')
    expect(refreshButton).toBeDefined()
    await refreshButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.getItems).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('Refreshed Card')
  })

  it('disables restore button for items with non-Available restore status', async () => {
    mocks.getItems.mockResolvedValue([
      {
        id: 'item-restored',
        entityType: 'card',
        entityId: 'card-restored',
        boardId: 'board-1',
        name: 'Already Restored',
        archivedByUserId: 'user-1',
        archivedAt: new Date().toISOString(),
        reason: null,
        restoreStatus: 'Restored',
        restoredAt: new Date().toISOString(),
        restoredByUserId: 'user-1',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreButton = findButtonByText(wrapper, 'Restore')
    expect(restoreButton).toBeDefined()
    expect(restoreButton!.attributes('disabled')).toBeDefined()
  })

  it('does not restore board when confirm is cancelled', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false)

    mocks.getBoards.mockResolvedValue([
      {
        id: 'board-archived',
        name: 'Not Restored Board',
        description: null,
        isArchived: true,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ])

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const restoreBoardButton = findButtonByText(wrapper, 'Restore Board')
    await restoreBoardButton!.trigger('click')
    await waitForAsyncUi()

    expect(mocks.updateBoard).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Not Restored Board')

    confirmSpy.mockRestore()
  })
})

describe('ArchiveView — entity type badges', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.getItems.mockResolvedValue([])
    mocks.getBoards.mockResolvedValue([])
  })

  it('displays entity type badge for each archive item', async () => {
    mocks.getItems.mockResolvedValue([
      {
        id: 'item-card',
        entityType: 'card',
        entityId: 'card-1',
        boardId: 'board-1',
        name: 'Card Item',
        archivedByUserId: 'user-1',
        archivedAt: new Date().toISOString(),
        reason: null,
        restoreStatus: 'Available',
        restoredAt: null,
        restoredByUserId: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: 'item-col',
        entityType: 'column',
        entityId: 'col-1',
        boardId: 'board-1',
        name: 'Column Item',
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

    const wrapper = mount(ArchiveView)
    await waitForAsyncUi()

    const badges = wrapper.findAll('.td-badge')
    const badgeTexts = badges.map((b) => b.text().toLowerCase())
    expect(badgeTexts).toContain('card')
    expect(badgeTexts).toContain('column')
  })
})
