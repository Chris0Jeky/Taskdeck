import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { archiveApi } from '../../api/archiveApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('archiveApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('queries archive items with filters', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await archiveApi.getItems({ entityType: 'card', limit: 50 })

    expect(http.get).toHaveBeenCalledWith('/archive/items?entityType=card&limit=50')
  })

  it('posts restore payload to entity endpoint', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { success: true } })

    await archiveApi.restoreItem('card', 'id/1', {
      targetBoardId: null,
      restoreMode: 0,
      conflictStrategy: 0,
    })

    expect(http.post).toHaveBeenCalledWith(
      '/archive/card/id%2F1/restore',
      {
        targetBoardId: null,
        restoreMode: 0,
        conflictStrategy: 0,
      }
    )
  })
})
