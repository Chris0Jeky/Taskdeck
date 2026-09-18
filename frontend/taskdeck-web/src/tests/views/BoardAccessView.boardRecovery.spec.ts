import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'
import BoardAccessView from '../../views/BoardAccessView.vue'

const mocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
  routerPush: vi.fn(),
  toastError: vi.fn(),
  toastWarning: vi.fn(),
}))

const route = reactive({ query: {} as Record<string, string | string[]> })
const permissions = reactive({
  loading: false,
  boardAccess: new Map<string, Array<{ id: string; userId: string; role: string }>>(),
  fetchBoardAccess: vi.fn<(boardId: string) => Promise<void>>(),
  grantAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  updateAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
  revokeAccess: vi.fn<(...args: unknown[]) => Promise<void>>(),
})

vi.mock('vue-router', () => ({
  useRoute: () => route,
  useRouter: () => ({ push: mocks.routerPush }),
}))

vi.mock('../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../store/permissionsStore', () => ({
  usePermissionsStore: () => permissions,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'current-user' }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    error: mocks.toastError,
    warning: mocks.toastWarning,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

const global = {
  stubs: {
    WorkspaceHelpCallout: {
      template: '<div><slot /><slot name="actions" /></div>',
    },
  },
}

describe('BoardAccessView board-list recovery', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    route.query = {}
    permissions.loading = false
    permissions.boardAccess = new Map()
    permissions.fetchBoardAccess.mockResolvedValue(undefined)
    permissions.grantAccess.mockResolvedValue(undefined)
    permissions.updateAccess.mockResolvedValue(undefined)
    permissions.revokeAccess.mockResolvedValue(undefined)
  })

  it('shows an honest failure state and recovers through a fresh bounded read', async () => {
    mocks.getBoards
      .mockRejectedValueOnce(new Error('board discovery failed'))
      .mockResolvedValueOnce([
        {
          id: 'board-recovered',
          name: 'Recovered Board',
          description: null,
          isArchived: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        },
      ])

    const wrapper = mount(BoardAccessView, { global })
    await flushPromises()

    const boundedRead = {
      timeout: BOARD_REQUEST_TIMEOUT_MS,
      skipRetry: true,
    }

    expect(mocks.getBoards).toHaveBeenNthCalledWith(1, undefined, false, boundedRead)
    expect(wrapper.get('[data-testid="board-access-boards-error"]').text()).toContain(
      'Boards could not be loaded.',
    )
    expect(wrapper.text()).not.toContain('No boards available yet')

    await wrapper.get('[data-testid="board-access-boards-retry"]').trigger('click')
    await flushPromises()

    expect(mocks.getBoards).toHaveBeenNthCalledWith(2, undefined, false, boundedRead)
    expect(wrapper.text()).toContain('Recovered Board')
    expect(wrapper.find('[data-testid="board-access-boards-error"]').exists()).toBe(false)
  })

  it('keeps a deep-linked board and its access row usable when discovery fails', async () => {
    permissions.boardAccess = new Map([
      ['board-deep-link', [{ id: 'access-1', userId: 'teammate', role: 'Viewer' }]],
    ])
    mocks.getBoards.mockRejectedValue(new Error('board discovery failed'))

    const wrapper = mount(BoardAccessView, {
      props: { boardId: 'board-deep-link' },
      global,
    })
    await flushPromises()

    expect((wrapper.get('#board-selector').element as HTMLSelectElement).value).toBe('board-deep-link')
    expect(permissions.fetchBoardAccess).toHaveBeenCalledWith('board-deep-link')
    expect(wrapper.text()).toContain('teammate')
    expect(wrapper.find('[data-testid="board-access-boards-error"]').exists()).toBe(true)
  })
})
