import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'
import ArchiveView from '../../views/ArchiveView.vue'

const mocks = vi.hoisted(() => ({
  getItems: vi.fn(),
  restoreItem: vi.fn(),
  getBoards: vi.fn(),
  updateBoard: vi.fn(),
  routerPush: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mocks.routerPush }),
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

describe('ArchiveView archived-board recovery', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.getItems.mockResolvedValue([])
  })

  it('bounds the board read and retries it from the inline failure state', async () => {
    mocks.getBoards
      .mockRejectedValueOnce(new Error('boards request failed'))
      .mockResolvedValueOnce([
        {
          id: 'board-recovered',
          name: 'Recovered Board',
          description: null,
          isArchived: true,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        },
      ])

    const wrapper = mount(ArchiveView, {
      global: {
        stubs: {
          RouterLink: {
            template: '<a><slot /></a>',
            props: ['to'],
          },
        },
      },
    })
    await flushPromises()

    const boundedRead = {
      timeout: BOARD_REQUEST_TIMEOUT_MS,
      skipRetry: true,
    }

    expect(mocks.getBoards).toHaveBeenNthCalledWith(1, undefined, true, boundedRead)
    expect(wrapper.get('[data-testid="archive-boards-error"]').text()).toContain(
      'Archived boards could not be loaded.',
    )

    await wrapper.get('[data-testid="archive-boards-retry"]').trigger('click')
    await flushPromises()

    expect(mocks.getBoards).toHaveBeenNthCalledWith(2, undefined, true, boundedRead)
    expect(wrapper.text()).toContain('Recovered Board')
    expect(wrapper.find('[data-testid="archive-boards-error"]').exists()).toBe(false)
  })
})
