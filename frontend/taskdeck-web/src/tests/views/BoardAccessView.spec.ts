import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import BoardAccessView from '../../views/BoardAccessView.vue'
import boardAccessSource from '../../views/BoardAccessView.vue?raw'

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>((innerResolve) => {
    resolve = innerResolve
  })

  return { promise, resolve }
}

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const routeMock = reactive({
  query: {} as Record<string, string | string[]>,
})

const boardsApiMocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
}))

const permissionsStore = reactive({
  loading: false,
  boardAccess: new Map<string, Array<{ id: string; userId: string; role: string }>>(),
  fetchBoardAccess: vi.fn<(boardId: string) => Promise<void>>(),
  grantAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  updateAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  revokeAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
})

const sessionStore = reactive({
  userId: 'user-1',
})

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => ({
    push: routerMocks.push,
  }),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: {
    getBoards: boardsApiMocks.getBoards,
  },
}))

vi.mock('../../store/permissionsStore', () => ({
  usePermissionsStore: () => permissionsStore,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    error: toastMocks.error,
    warning: toastMocks.warning,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => ({
    message: `${fallback} ${error instanceof Error ? error.message : ''}`.trim(),
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

function seedBoards() {
  return [
    {
      id: 'board-1',
      name: 'Alpha Board',
      description: 'First board',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
    {
      id: 'board-2',
      name: 'Beta Board',
      description: 'Second board',
      isArchived: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    },
  ]
}

let mountedWrapper: ReturnType<typeof mount> | null = null

describe('BoardAccessView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    routeMock.query = {}
    permissionsStore.loading = false
    permissionsStore.boardAccess = new Map([
      ['board-1', [{ id: 'access-1', userId: 'user-1', role: 'Owner' }]],
      ['board-2', [{ id: 'access-2', userId: 'user-2', role: 'Viewer' }]],
    ])
    permissionsStore.fetchBoardAccess.mockResolvedValue(undefined)
    permissionsStore.grantAccess.mockResolvedValue(undefined)
    permissionsStore.updateAccess.mockResolvedValue(undefined)
    permissionsStore.revokeAccess.mockResolvedValue(undefined)
    boardsApiMocks.getBoards.mockResolvedValue(seedBoards())
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('defaults to the first board from the selector and avoids a raw board-id input', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(boardsApiMocks.getBoards).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-1')
    expect(wrapper.find('#board-selector').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="Enter board ID"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Normal flows should not depend on memorized board IDs')
    expect(wrapper.text()).toContain('Why use the board selector here?')
  })

  it('renders with the Paper theme class hooks (not the legacy Obsidian ones)', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    // Root, hero, and panel should use the Paper (`paper-access__*`) idiom, and
    // none of the legacy Obsidian (`td-access*`) hooks should survive. The
    // shared WorkspaceHelpCallout keeps its own chrome and is out of scope.
    expect(wrapper.find('.paper-access').exists()).toBe(true)
    expect(wrapper.find('.paper-access__hero').exists()).toBe(true)
    expect(wrapper.find('.paper-access__panel').exists()).toBe(true)
    expect(wrapper.find('[class*="td-access"]').exists()).toBe(false)
  })

  it('fetches the selected board access list when the selector changes', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const selector = wrapper.get('#board-selector')
    await selector.setValue('board-2')
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenLastCalledWith('board-2')
    expect(wrapper.text()).toContain('user-2')
  })

  it('fetches access once when the boardId prop changes', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()
    permissionsStore.fetchBoardAccess.mockClear()

    await wrapper.setProps({ boardId: 'board-2' })
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
  })

  it('fetches access once when the route query changes', async () => {
    mountedWrapper = mount(BoardAccessView)
    await waitForUi()
    permissionsStore.fetchBoardAccess.mockClear()

    routeMock.query = { boardId: 'board-2' }
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
  })

  it('uses the first board id when the route query provides multiple values', async () => {
    routeMock.query = { boardId: ['board-2', 'board-1'] }

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(permissionsStore.fetchBoardAccess).toHaveBeenCalledWith('board-2')
    expect((wrapper.get('#board-selector').element as HTMLSelectElement).value).toBe('board-2')
  })

  it('disables refresh while access is already loading', async () => {
    permissionsStore.loading = true

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const refreshButton = wrapper.findAll('button').find((node) => node.text().includes('Refreshing...'))
    expect(refreshButton?.attributes('disabled')).toBeDefined()
  })

  it('disables refresh while boards are loading', async () => {
    const deferredBoards = createDeferred<ReturnType<typeof seedBoards>>()
    boardsApiMocks.getBoards.mockReturnValueOnce(deferredBoards.promise)

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await Promise.resolve()

    const refreshButton = wrapper.findAll('button').find((node) => node.text().includes('Loading boards...'))
    expect(refreshButton?.attributes('disabled')).toBeDefined()

    deferredBoards.resolve(seedBoards())
    await waitForUi()
  })

  it('shows the guided empty state when there are no boards to manage yet', async () => {
    boardsApiMocks.getBoards.mockResolvedValue([])

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(wrapper.text()).toContain('No boards available yet')
    expect(wrapper.text()).toContain('Create a board first')
    expect(wrapper.findAll('button').some((node) => node.text() === 'Create or Open Boards')).toBe(true)
  })

  it('grants access by email or username identifier instead of a raw user id', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const addMember = wrapper.findAll('button').find((node) => node.text() === '+ Add Member')
    expect(addMember).toBeTruthy()
    await addMember!.trigger('click')
    await waitForUi()

    const identifierInput = wrapper.get('#grant-user')
    expect(identifierInput.attributes('placeholder')).toBe('Enter email or username')
    // The old raw user-id affordance must be gone.
    expect(wrapper.find('input[placeholder="Enter user ID"]').exists()).toBe(false)

    await identifierInput.setValue('friend@example.com')

    const grantButton = wrapper.findAll('button').find((node) => node.text().includes('Grant Access'))
    await grantButton!.trigger('click')
    await waitForUi()

    expect(permissionsStore.grantAccess).toHaveBeenCalledTimes(1)
    expect(permissionsStore.grantAccess).toHaveBeenCalledWith('board-1', {
      identifier: 'friend@example.com',
      role: 'Viewer',
    })
  })

  it('warns and does not grant when the identifier is blank', async () => {
    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    const addMember = wrapper.findAll('button').find((node) => node.text() === '+ Add Member')
    await addMember!.trigger('click')
    await waitForUi()

    const grantButton = wrapper.findAll('button').find((node) => node.text().includes('Grant Access'))
    await grantButton!.trigger('click')
    await waitForUi()

    expect(toastMocks.warning).toHaveBeenCalledWith('Please enter an email or username.')
    expect(permissionsStore.grantAccess).not.toHaveBeenCalled()
  })

  it('surfaces the mapped board-load error details', async () => {
    boardsApiMocks.getBoards.mockRejectedValueOnce(new Error('boom'))

    const wrapper = mount(BoardAccessView)
    mountedWrapper = wrapper
    await waitForUi()

    expect(toastMocks.error).toHaveBeenCalledWith('Failed to load boards for access management. boom')
  })
})

// ── #1808 review (MEDIUM): Legacy ("off") mode substrate guard ──
// Paper tokens exist only under `.paper` / `.paper-night` (paper-tokens.css), so
// in Legacy mode this view's `color: var(--ink, …)` resolves to the near-black
// literal while AppShell's `.td-content` still paints `--td-surface-base`
// (#131313) — ~1.05:1 on the hero. A root that sets the Paper ink MUST therefore
// also paint the Paper substrate; that is a no-op under `.paper`/`.paper-night`.
// Source is read through Vite's `?raw` rather than `node:fs` because
// `tsconfig.vitest.json` deliberately omits the "node" types.
// #1815 tracks unifying these per-view assertions into one wave-wide spec.
describe('BoardAccessView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = boardAccessSource.match(/^\.paper-access \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-access root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })
})
