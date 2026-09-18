import { describe, expect, it, vi } from 'vitest'
import { refreshBoardAfterLifecycleChange } from '../../utils/boardLifecycleRefresh'

describe('refreshBoardAfterLifecycleChange', () => {
  it('uses a comment-preserving background read while the card editor remains open', async () => {
    const fetchBoard = vi.fn().mockResolvedValue(true)

    await expect(refreshBoardAfterLifecycleChange(
      { fetchBoard },
      'board-1',
      { preserveCardComments: true },
    )).resolves.toBe(true)

    expect(fetchBoard).toHaveBeenCalledWith('board-1', {
      intent: 'background',
      preserveCardComments: true,
    })
  })

  it('uses an ordinary background read after the editor closes', async () => {
    const fetchBoard = vi.fn().mockResolvedValue(true)

    await expect(refreshBoardAfterLifecycleChange({ fetchBoard }, 'board-1')).resolves.toBe(true)

    expect(fetchBoard).toHaveBeenCalledWith('board-1', { intent: 'background' })
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
