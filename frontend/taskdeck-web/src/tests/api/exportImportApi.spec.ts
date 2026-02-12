import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { exportImportApi } from '../../api/exportImportApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('exportImportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('exportBoardJson encodes boardId and userId', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'b1' } })

    await exportImportApi.exportBoardJson('board/1', 'user/1')

    expect(http.get).toHaveBeenCalledWith('/export/boards/board%2F1/json?userId=user%2F1')
  })

  it('importBoard encodes userId and forwards payload', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { success: true } })
    const payload = { name: 'Imported Board' }

    await exportImportApi.importBoard(payload, 'user/1')

    expect(http.post).toHaveBeenCalledWith('/import/boards?userId=user%2F1', payload)
  })

  it('importBoardJson parses JSON and posts parsed payload', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { success: true } })
    const json = '{"name":"Imported Board","columns":[]}'

    await exportImportApi.importBoardJson(json, 'user/1')

    expect(http.post).toHaveBeenCalledWith(
      '/import/boards/json?userId=user%2F1',
      { name: 'Imported Board', columns: [] },
      { headers: { 'Content-Type': 'application/json' } }
    )
  })

  it('importBoardJson throws for invalid JSON before API call', async () => {
    await expect(exportImportApi.importBoardJson('{invalid', 'user-1')).rejects.toThrow()
    expect(http.post).not.toHaveBeenCalled()
  })
})
