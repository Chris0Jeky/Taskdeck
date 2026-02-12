import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { boardAccessApi } from '../../api/boardAccessApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('boardAccessApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('normalizes numeric roles from getAccess', async () => {
    vi.mocked(http.get).mockResolvedValue({
      data: [
        {
          id: 'a1',
          boardId: 'b1',
          userId: 'u1',
          role: 1,
          grantedBy: 'u0',
          grantedAt: '2026-01-01T00:00:00Z',
        },
      ],
    })

    const result = await boardAccessApi.getAccess('b1')

    expect(result[0]?.role).toBe('Admin')
  })

  it('grantAccess encodes grantedBy and sends numeric enum', async () => {
    vi.mocked(http.post).mockResolvedValue({
      data: {
        id: 'a1',
        boardId: 'b1',
        userId: 'u2',
        role: 3,
        grantedBy: 'u/1',
        grantedAt: '2026-01-01T00:00:00Z',
      },
    })

    const result = await boardAccessApi.grantAccess('b1', { userId: 'u2', role: 'Viewer' }, 'u/1')

    expect(http.post).toHaveBeenCalledWith('/boards/b1/access?grantedBy=u%2F1', {
      userId: 'u2',
      role: 3,
    })
    expect(result.role).toBe('Viewer')
  })

  it('updateAccess encodes updatedBy and sends numeric enum', async () => {
    vi.mocked(http.put).mockResolvedValue({
      data: {
        id: 'a1',
        boardId: 'b1',
        userId: 'u2',
        role: 2,
        grantedBy: 'u1',
        grantedAt: '2026-01-01T00:00:00Z',
      },
    })

    const result = await boardAccessApi.updateAccess('b1', 'a1', { role: 'Editor' }, 'u/1')

    expect(http.put).toHaveBeenCalledWith('/boards/b1/access/a1?updatedBy=u%2F1', {
      role: 2,
    })
    expect(result.role).toBe('Editor')
  })

  it('revokeAccess encodes revokedBy', async () => {
    vi.mocked(http.delete).mockResolvedValue({})

    await boardAccessApi.revokeAccess('b1', 'a1', 'u/1')

    expect(http.delete).toHaveBeenCalledWith('/boards/b1/access/a1?revokedBy=u%2F1')
  })
})
