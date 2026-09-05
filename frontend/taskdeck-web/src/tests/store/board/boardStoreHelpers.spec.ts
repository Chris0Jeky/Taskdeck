import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import axios from 'axios'
import { i18n } from '../../../i18n'

const { mockToastStore, mockGetErrorMessage, mockIsDemoMode } = vi.hoisted(() => ({
  mockToastStore: { error: vi.fn(), info: vi.fn(), success: vi.fn() },
  mockGetErrorMessage: vi.fn((_err: unknown, fallback: string) => fallback),
  mockIsDemoMode: { value: false },
}))

vi.mock('../../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
}))

vi.mock('../../../utils/errorMessage', () => ({
  getErrorMessage: mockGetErrorMessage,
}))

vi.mock('../../../utils/demoMode', () => ({
  get isDemoMode() {
    return mockIsDemoMode.value
  },
  DemoModeError: class DemoModeError extends Error {
    constructor() {
      super('Demo mode')
      this.name = 'DemoModeError'
    }
  },
}))

import { createBoardHelpers } from '../../../store/board/boardStoreHelpers'

function createMockState() {
  return {
    currentBoard: ref<{ id: string; columns: Array<{ id: string; cardCount?: number }> } | null>({
      id: 'board-1',
      columns: [
        { id: 'col-1', cardCount: 5 },
        { id: 'col-2', cardCount: 0 },
      ],
    }),
    error: ref<string | null>(null),
  }
}

describe('boardStoreHelpers', () => {
  let state: ReturnType<typeof createMockState>

  beforeEach(() => {
    vi.clearAllMocks()
    mockIsDemoMode.value = false
    state = createMockState()
  })

  describe('handleApiError', () => {
    it('sets state.error and shows toast', () => {
      mockGetErrorMessage.mockReturnValueOnce('Something went wrong')
      const helpers = createBoardHelpers(state as any)
      helpers.handleApiError(new Error('oops'), 'Fallback message')
      expect(state.error.value).toBe('Something went wrong')
      expect(mockToastStore.error).toHaveBeenCalledWith('Something went wrong')
    })

    it('uses fallback from getErrorMessage', () => {
      const helpers = createBoardHelpers(state as any)
      helpers.handleApiError(new Error(), 'My fallback')
      expect(mockGetErrorMessage).toHaveBeenCalledWith(expect.any(Error), 'My fallback')
    })

    // #2689 item 3. Since #2685 every board read is bounded, so a 10 s timeout
    // is a routine outcome rather than an exotic one — and `getErrorMessage`
    // prefers `err.message`, which for a transport failure is axios' own
    // untranslated English rendered verbatim inside a localized alert.
    describe('transport failures', () => {
      const TIMEOUT_COPY =
        'The request took too long, so it was stopped. Check your connection, then try again.'
      const CANCELLED_COPY = 'The request was stopped before it finished. Try again.'

      const timeoutError = () =>
        new axios.AxiosError('timeout of 10000ms exceeded', 'ECONNABORTED')

      it('maps an axios timeout to catalog copy for both the alert and the toast', () => {
        const helpers = createBoardHelpers(state as any)

        helpers.handleApiError(timeoutError(), 'Failed to fetch boards')

        expect(state.error.value).toBe(TIMEOUT_COPY)
        expect(mockToastStore.error).toHaveBeenCalledWith(TIMEOUT_COPY)
        expect(state.error.value).not.toContain('timeout of 10000ms exceeded')
        // The raw-message path is not consulted at all for a transport failure.
        expect(mockGetErrorMessage).not.toHaveBeenCalled()
      })

      it('follows the active language, so the copy is catalog-driven and not a constant', () => {
        i18n.global.locale.value = 'it'
        const helpers = createBoardHelpers(state as any)

        helpers.handleApiError(timeoutError(), 'Failed to fetch boards')

        expect(state.error.value).toBe(
          'La richiesta ha richiesto troppo tempo ed è stata interrotta. Controlla la connessione e riprova.',
        )
      })

      it('also maps the ETIMEDOUT code axios uses with clarifyTimeoutError', () => {
        const helpers = createBoardHelpers(state as any)

        helpers.handleApiError(
          new axios.AxiosError('timeout of 10000ms exceeded', 'ETIMEDOUT'),
          'Failed to fetch boards',
        )

        expect(state.error.value).toBe(TIMEOUT_COPY)
      })

      // A cancel should not reach here — both read paths return on
      // `axios.isCancel` first — but if one ever does, silence would leave
      // `error` null with `boards` still empty, which BoardsListView's
      // `v-else-if` chain renders as the EMPTY state: an unconfirmed claim that
      // the account has no boards.
      it('maps an axios cancel to catalog copy rather than leaving the surface silent', () => {
        const helpers = createBoardHelpers(state as any)

        helpers.handleApiError(new axios.CanceledError('canceled'), 'Failed to fetch boards')

        expect(state.error.value).toBe(CANCELLED_COPY)
        expect(mockToastStore.error).toHaveBeenCalledWith(CANCELLED_COPY)
      })

      // The message arm of the timeout check is gated on the absence of a
      // response, so a server error whose own wording mentions a timeout keeps
      // that wording instead of being overwritten by the generic copy.
      it('leaves a server error message alone even when it mentions a timeout', () => {
        mockGetErrorMessage.mockReturnValueOnce('Upstream gateway timeout while saving')
        const helpers = createBoardHelpers(state as any)

        helpers.handleApiError(
          new axios.AxiosError(
            'Request timeout reported by the gateway',
            'ERR_BAD_RESPONSE',
            undefined,
            undefined,
            {
              status: 504,
              data: { message: 'Upstream gateway timeout while saving' },
            } as never,
          ),
          'Failed to fetch boards',
        )

        expect(state.error.value).toBe('Upstream gateway timeout while saving')
      })
    })
  })

  describe('guardDemoMutation', () => {
    it('does nothing when not in demo mode', () => {
      mockIsDemoMode.value = false
      const helpers = createBoardHelpers(state as any)
      expect(() => helpers.guardDemoMutation()).not.toThrow()
    })

    it('throws DemoModeError and shows info toast when in demo mode', () => {
      mockIsDemoMode.value = true
      const helpers = createBoardHelpers(state as any)
      expect(() => helpers.guardDemoMutation()).toThrow(
        expect.objectContaining({ name: 'DemoModeError', message: 'Demo mode' }),
      )
      expect(mockToastStore.info).toHaveBeenCalledWith('This action is view-only in demo mode.')
    })
  })

  describe('isHttpNotFound', () => {
    it('returns true for 404 response', () => {
      const helpers = createBoardHelpers(state as any)
      expect(helpers.isHttpNotFound({ response: { status: 404 } })).toBe(true)
    })

    it('returns false for other status codes', () => {
      const helpers = createBoardHelpers(state as any)
      expect(helpers.isHttpNotFound({ response: { status: 500 } })).toBe(false)
    })

    it('returns false for null', () => {
      const helpers = createBoardHelpers(state as any)
      expect(helpers.isHttpNotFound(null)).toBe(false)
    })
  })

  describe('isHttpConflict', () => {
    it('returns true for 409 response', () => {
      const helpers = createBoardHelpers(state as any)
      expect(helpers.isHttpConflict({ response: { status: 409 } })).toBe(true)
    })

    it('returns false for other status codes', () => {
      const helpers = createBoardHelpers(state as any)
      expect(helpers.isHttpConflict({ response: { status: 400 } })).toBe(false)
    })
  })

  describe('updateColumnCardCount', () => {
    it('increments column card count', () => {
      const helpers = createBoardHelpers(state as any)
      helpers.updateColumnCardCount('col-1', 1)
      expect(state.currentBoard.value!.columns[0].cardCount).toBe(6)
    })

    it('decrements column card count', () => {
      const helpers = createBoardHelpers(state as any)
      helpers.updateColumnCardCount('col-1', -1)
      expect(state.currentBoard.value!.columns[0].cardCount).toBe(4)
    })

    it('does not go below zero', () => {
      const helpers = createBoardHelpers(state as any)
      helpers.updateColumnCardCount('col-2', -5)
      expect(state.currentBoard.value!.columns[1].cardCount).toBe(0)
    })

    it('handles undefined cardCount as 0', () => {
      state.currentBoard.value!.columns.push({ id: 'col-3' })
      const helpers = createBoardHelpers(state as any)
      helpers.updateColumnCardCount('col-3', 2)
      expect(state.currentBoard.value!.columns[2].cardCount).toBe(2)
    })

    it('does nothing when currentBoard is null', () => {
      state.currentBoard.value = null
      const helpers = createBoardHelpers(state as any)
      expect(() => helpers.updateColumnCardCount('col-1', 1)).not.toThrow()
    })

    it('does nothing when column not found', () => {
      const helpers = createBoardHelpers(state as any)
      expect(() => helpers.updateColumnCardCount('col-unknown', 1)).not.toThrow()
      expect(state.currentBoard.value!.columns[0].cardCount).toBe(5)
    })
  })
})
