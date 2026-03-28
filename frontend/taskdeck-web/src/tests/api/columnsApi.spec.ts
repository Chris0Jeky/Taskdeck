import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { columnsApi } from '../../api/columnsApi'
import type { CreateColumnDto, UpdateColumnDto } from '../../types/board'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('columnsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getColumns sends GET to board columns endpoint', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [{ id: '1', name: 'Todo' }] })

    const result = await columnsApi.getColumns('board-1')

    expect(http.get).toHaveBeenCalledWith('/boards/board-1/columns')
    expect(result).toEqual([{ id: '1', name: 'Todo' }])
  })

  it('createColumn sends POST with column payload', async () => {
    const column: CreateColumnDto = { name: 'In Progress', position: 1 }
    vi.mocked(http.post).mockResolvedValue({ data: { id: '2', ...column } })

    const result = await columnsApi.createColumn('board-1', column)

    expect(http.post).toHaveBeenCalledWith('/boards/board-1/columns', column)
    expect(result).toEqual({ id: '2', ...column })
  })

  it('updateColumn sends PATCH with column payload', async () => {
    const update: UpdateColumnDto = { name: 'Done' }
    vi.mocked(http.patch).mockResolvedValue({ data: { id: '1', name: 'Done' } })

    const result = await columnsApi.updateColumn('board-1', 'col-1', update)

    expect(http.patch).toHaveBeenCalledWith('/boards/board-1/columns/col-1', update)
    expect(result).toEqual({ id: '1', name: 'Done' })
  })

  it('deleteColumn sends DELETE to column endpoint', async () => {
    vi.mocked(http.delete).mockResolvedValue({})

    await columnsApi.deleteColumn('board-1', 'col-1')

    expect(http.delete).toHaveBeenCalledWith('/boards/board-1/columns/col-1')
  })

  it('reorderColumns sends POST with columnIds array', async () => {
    const columnIds = ['col-3', 'col-1', 'col-2']
    vi.mocked(http.post).mockResolvedValue({ data: [{ id: 'col-3' }, { id: 'col-1' }, { id: 'col-2' }] })

    const result = await columnsApi.reorderColumns('board-1', columnIds)

    expect(http.post).toHaveBeenCalledWith('/boards/board-1/columns/reorder', { columnIds })
    expect(result).toEqual([{ id: 'col-3' }, { id: 'col-1' }, { id: 'col-2' }])
  })
})
