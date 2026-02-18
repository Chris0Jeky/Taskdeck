import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { auditApi } from '../../api/auditApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('auditApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getBoardHistory encodes boardId and forwards limit', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await auditApi.getBoardHistory('board/1', 25)

    expect(http.get).toHaveBeenCalledWith('/audit/boards/board%2F1?limit=25')
  })

  it('getEntityHistory encodes entity type and id', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await auditApi.getEntityHistory('card/type', 'entity/1', 10)

    expect(http.get).toHaveBeenCalledWith('/audit/entities/card%2Ftype/entity%2F1?limit=10')
  })

  it('getUserHistory uses current user endpoint', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await auditApi.getUserHistory(50)

    expect(http.get).toHaveBeenCalledWith('/audit/users/me?limit=50')
  })
})
