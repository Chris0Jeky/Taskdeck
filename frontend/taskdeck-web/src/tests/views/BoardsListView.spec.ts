import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardsListView from '../../views/BoardsListView.vue'
import type { Board } from '../../types/board'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const mockBoardStore = reactive({
  boards: [] as Board[],
  loading: false,
  error: null as string | null,
  fetchBoards: vi.fn<() => Promise<void>>(),
  createBoard: vi.fn<(payload: { name: string }) => Promise<Board>>(),
})

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

function makeBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: 'board-1',
    name: 'Test Board',
    description: null,
    isArchived: false,
    createdAt: '2024-01-01T00:00:00.000Z',
    updatedAt: '2024-01-01T00:00:00.000Z',
    ...overrides,
  }
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('BoardsListView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.boards = []
    mockBoardStore.loading = false
    mockBoardStore.error = null
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockBoardStore.createBoard.mockResolvedValue(makeBoard())
  })

  it('fetches boards on mount', async () => {
    mount(BoardsListView)
    await waitForUi()

    expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(1)
  })

  it('renders the page title', async () => {
    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('My Boards')
  })

  it('renders with the Paper theme class hooks (not the legacy Obsidian / Tailwind ones)', async () => {
    mockBoardStore.boards = [makeBoard({ id: 'board-xyz', name: 'My Board' })]

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.find('.paper-boards').exists()).toBe(true)
    expect(wrapper.find('.paper-boards__hero').exists()).toBe(true)
    expect(wrapper.find('.paper-boards__card').exists()).toBe(true)

    // The legacy hooks and the raw Tailwind color utilities that resolved to
    // Obsidian values (`bg-surface`, `bg-ember`, `text-on-surface`) must be gone.
    const html = wrapper.html()
    expect(html).not.toContain('td-boards-skeleton')
    expect(html).not.toContain('td-panel')
    expect(html).not.toContain('td-btn')
    expect(html).not.toContain('bg-surface')
    expect(html).not.toContain('bg-ember')
    expect(html).not.toContain('text-on-surface')
  })

  it('shows loading skeleton while boards are loading', async () => {
    mockBoardStore.loading = true

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading boards...')
    expect(wrapper.find('.paper-boards__skeleton').exists()).toBe(true)
  })

  it('shows error state when boardStore.error is set', async () => {
    mockBoardStore.error = 'Failed to load boards'

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Failed to load boards')
  })

  // #2689 item 1. Since #2685 the list read is bounded with `skipRetry`, so a
  // one-off 503 or an API restart during mount no longer heals itself in the
  // retry layer: without a control the alert stood until the user navigated
  // away and back, and because the read is shared every unfiltered caller on
  // the page failed with it.
  describe('retry control in the error state', () => {
    it('renders a focusable Retry button beside the alert', async () => {
      mockBoardStore.error = 'Failed to load boards'

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const retry = wrapper.find('[data-action="retry-board-load"]')
      expect(retry.exists()).toBe(true)
      // A real button: in the tab order, and activated by Enter/Space without
      // any key handling of this view's own.
      expect(retry.element.tagName).toBe('BUTTON')
      expect(retry.attributes('type')).toBe('button')
      expect(retry.text()).toBe('Retry board load')

      // The live region announces the sentence alone; the control is a sibling
      // that points back at it, so the announcement does not carry the button
      // label and the button is still reachable.
      const alert = wrapper.find('[role="alert"]')
      expect(alert.exists()).toBe(true)
      expect(alert.text()).toBe('Failed to load boards')
      expect(alert.attributes('id')).toBeTruthy()
      expect(retry.attributes('aria-describedby')).toBe(alert.attributes('id'))
    })

    it('re-issues the read, shows the skeleton in flight, and leaves the error state on success', async () => {
      mockBoardStore.error = 'Failed to load boards'

      const wrapper = mount(BoardsListView)
      await waitForUi()
      expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(1)

      // What the store actually does on a successful read: the loading flag is
      // raised synchronously on entry, then the list is committed and `error`
      // cleared on the success path (boardCrudStore, #2689 item 4). Held open
      // deliberately so the in-flight frame is observable rather than a race
      // with the mock's own resolution.
      let settleRead!: () => void
      mockBoardStore.fetchBoards.mockImplementation(() => {
        mockBoardStore.loading = true
        return new Promise<void>((resolve) => {
          settleRead = () => {
            mockBoardStore.boards = [makeBoard({ id: 'b1', name: 'Recovered Board' })]
            mockBoardStore.error = null
            mockBoardStore.loading = false
            resolve()
          }
        })
      })

      await wrapper.find('[data-action="retry-board-load"]').trigger('click')

      expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(2)
      expect(wrapper.find('.paper-boards__skeleton').exists()).toBe(true)
      expect(wrapper.find('[role="alert"]').exists()).toBe(false)

      settleRead()
      await flushPromises()

      expect(wrapper.find('[role="alert"]').exists()).toBe(false)
      expect(wrapper.find('[data-action="retry-board-load"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('Recovered Board')
    })

    // #2689 round-2 finding 1. The alert on this surface is not raised only by
    // the list read: `error` is shared, so a create/rename/archive failure puts
    // the view on its error branch while the throttle window from an earlier
    // SUCCESSFUL read is still open. Unforced, the click returned inside the
    // store before `loading` was touched — no skeleton, no request, a dead
    // button until the window passed.
    it('forces past the throttle window when the alert came from another action after a good read', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      // The mount read does NOT force: an ordinary mount inside another view's
      // window must still be throttled.
      expect(mockBoardStore.fetchBoards).toHaveBeenCalledWith(undefined, false, {})

      // Inside that window, another action fails and sets the shared ref.
      mockBoardStore.error = 'Board name already exists'
      await waitForUi()

      let settleRead!: () => void
      mockBoardStore.fetchBoards.mockImplementation(() => {
        mockBoardStore.loading = true
        return new Promise<void>((resolve) => {
          settleRead = () => {
            mockBoardStore.loading = false
            resolve()
          }
        })
      })

      await wrapper.find('[data-action="retry-board-load"]').trigger('click')

      expect(mockBoardStore.fetchBoards).toHaveBeenLastCalledWith(undefined, false, {
        force: true,
      })
      expect(wrapper.find('.paper-boards__skeleton').exists()).toBe(true)

      settleRead()
      await flushPromises()
    })

    // #2689 round-2 finding 3. Activating Retry unmounts the focused button
    // (the loading branch replaces the error block) and a failed retry builds a
    // NEW one, so focus fell back to <body> and a keyboard or screen-reader
    // user had to tab from the top of the page to retry again. Attached to the
    // document because focus and `document.activeElement` are meaningless for a
    // detached tree.
    it('returns focus to the Retry button when the retry fails', async () => {
      mockBoardStore.error = 'Failed to load boards'

      const wrapper = mount(BoardsListView, { attachTo: document.body })
      await waitForUi()

      const firstButton = wrapper.find('[data-action="retry-board-load"]')
        .element as HTMLButtonElement
      firstButton.focus()
      expect(document.activeElement).toBe(firstButton)

      let failRead!: () => void
      mockBoardStore.fetchBoards.mockImplementation(() => {
        mockBoardStore.loading = true
        return new Promise<void>((_resolve, reject) => {
          failRead = () => {
            mockBoardStore.error = 'Failed to load boards'
            mockBoardStore.loading = false
            reject(new Error('still failing'))
          }
        })
      })

      await wrapper.find('[data-action="retry-board-load"]').trigger('click')

      // In flight the error block is gone, so the focused element went with it.
      expect(wrapper.find('[data-action="retry-board-load"]').exists()).toBe(false)
      expect(document.activeElement).toBe(document.body)

      failRead()
      await flushPromises()

      const rebuiltButton = wrapper.find('[data-action="retry-board-load"]')
        .element as HTMLButtonElement
      expect(rebuiltButton).not.toBe(firstButton)
      expect(document.activeElement).toBe(rebuiltButton)

      wrapper.unmount()
    })

    it('shows the alert again, still retryable, when the retry also fails', async () => {
      mockBoardStore.error = 'Failed to load boards'

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const timeoutCopy =
        'The request took too long, so it was stopped. Check your connection, then try again.'
      mockBoardStore.fetchBoards.mockImplementation(async () => {
        mockBoardStore.loading = true
        await Promise.resolve()
        mockBoardStore.error = timeoutCopy
        mockBoardStore.loading = false
        // The store rethrows after handleApiError; the view's catch is what
        // keeps that from becoming a lifecycle-hook error on the retry path
        // too, not only on mount.
        throw new Error('timeout of 10000ms exceeded')
      })

      await wrapper.find('[data-action="retry-board-load"]').trigger('click')
      await flushPromises()

      expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(2)
      const alert = wrapper.find('[role="alert"]')
      expect(alert.exists()).toBe(true)
      expect(alert.text()).toBe(timeoutCopy)
      expect(wrapper.find('[data-action="retry-board-load"]').exists()).toBe(true)
    })
  })

  it('shows empty state when no boards exist', async () => {
    mockBoardStore.boards = []

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('No boards')
    expect(wrapper.text()).toContain('Get started by creating a new board.')
  })

  it('renders boards grid when boards exist', async () => {
    mockBoardStore.boards = [
      makeBoard({ id: 'b1', name: 'Alpha Board', description: 'First board' }),
      makeBoard({ id: 'b2', name: 'Beta Board', description: null }),
    ]

    const wrapper = mount(BoardsListView)
    await waitForUi()

    expect(wrapper.text()).toContain('Alpha Board')
    expect(wrapper.text()).toContain('First board')
    expect(wrapper.text()).toContain('Beta Board')
    expect(wrapper.text()).toContain('No description')
  })

  it('navigates to a board when its card is clicked', async () => {
    mockBoardStore.boards = [makeBoard({ id: 'board-xyz', name: 'My Board' })]

    const wrapper = mount(BoardsListView)
    await waitForUi()

    // Find the clickable board card by looking for the board name in a clickable div
    const boardCards = wrapper.findAll('.paper-boards__card')
    const targetCard = boardCards.find((c) => c.text().includes('My Board'))
    expect(targetCard).toBeDefined()
    await targetCard!.trigger('click')

    expect(routerMocks.push).toHaveBeenCalledWith('/boards/board-xyz')
  })

  describe('create board form', () => {
    it('toggles the create form when + New Board is clicked', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      expect(newBoardBtn).toBeDefined()
      await newBoardBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)
      expect(wrapper.text()).toContain('Create New Board')
    })

    it('shows the create form when "Create Board" empty-state button is clicked', async () => {
      mockBoardStore.boards = []

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const createBtn = wrapper.findAll('button').find((b) => b.text().includes('+ Create Board'))
      expect(createBtn).toBeDefined()
      await createBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)
    })

    it('does not submit when board name is empty', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(mockBoardStore.createBoard).not.toHaveBeenCalled()
    })

    it('creates a board and navigates to it', async () => {
      const newBoard = makeBoard({ id: 'new-board-id', name: 'My New Board' })
      mockBoardStore.createBoard.mockResolvedValue(newBoard)

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('input[placeholder="Board name"]').setValue('My New Board')
      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(mockBoardStore.createBoard).toHaveBeenCalledWith({ name: 'My New Board' })
      expect(routerMocks.push).toHaveBeenCalledWith('/boards/new-board-id')
    })

    it('hides the create form after successful board creation', async () => {
      const newBoard = makeBoard({ id: 'nb', name: 'Fresh Board' })
      mockBoardStore.createBoard.mockResolvedValue(newBoard)

      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      await wrapper.find('input[placeholder="Board name"]').setValue('Fresh Board')
      await wrapper.find('form').trigger('submit')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)
    })

    it('cancels the create form when Cancel is clicked', async () => {
      const wrapper = mount(BoardsListView)
      await waitForUi()

      const newBoardBtn = wrapper.findAll('button').find((b) => b.text().includes('+ New Board'))
      await newBoardBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(true)

      const cancelBtn = wrapper.findAll('button').find((b) => b.text().includes('Cancel'))
      expect(cancelBtn).toBeDefined()
      await cancelBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('form').exists()).toBe(false)
    })
  })
})
