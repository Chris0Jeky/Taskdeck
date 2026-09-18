import { describe, expect, it, vi } from 'vitest'
import {
  BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
  refreshBoardAfterLifecycleChange,
} from '../../utils/boardLifecycleRefresh'

describe('refreshBoardAfterLifecycleChange', () => {
  it('uses a warned, comment-preserving background read while the card editor remains open', async () => {
    const fetchBoard = vi.fn().mockResolvedValue(true)

    await expect(refreshBoardAfterLifecycleChange(
      { fetchBoard },
      'board-1',
      { preserveCardComments: true },
    )).resolves.toBe(true)

    expect(fetchBoard).toHaveBeenCalledWith('board-1', {
      intent: 'background',
      backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
      preserveCardComments: true,
    })
  })

  it('uses a warned background read after the editor closes', async () => {
    const fetchBoard = vi.fn().mockResolvedValue(true)

    await expect(refreshBoardAfterLifecycleChange({ fetchBoard }, 'board-1')).resolves.toBe(true)

    expect(fetchBoard).toHaveBeenCalledWith('board-1', {
      intent: 'background',
      backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
    })
  })

  it('preserves the false result after the store surfaces its configured warning', async () => {
    const fetchBoard = vi.fn().mockResolvedValue(false)

    await expect(refreshBoardAfterLifecycleChange({ fetchBoard }, 'board-1')).resolves.toBe(false)

    expect(fetchBoard).toHaveBeenCalledWith('board-1', {
      intent: 'background',
      backgroundFailureMessage: BOARD_LIFECYCLE_REFRESH_FAILURE_MESSAGE,
    })
  })

  it('contains an unexpected adapter rejection instead of creating an unhandled promise', async () => {
    const fetchBoard = vi.fn().mockRejectedValue(new Error('refresh failed'))

    await expect(refreshBoardAfterLifecycleChange(
      { fetchBoard },
      'board-1',
      { preserveCardComments: true },
    )).resolves.toBe(false)
  })
})
