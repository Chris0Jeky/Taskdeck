import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { labelsApi } from '../../api/labelsApi'
import type { CreateLabelDto, UpdateLabelDto } from '../../types/board'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('labelsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getLabels sends GET to board labels endpoint', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [{ id: '1', name: 'Bug' }] })

    const result = await labelsApi.getLabels('board-1')

    expect(http.get).toHaveBeenCalledWith('/boards/board-1/labels')
    expect(result).toEqual([{ id: '1', name: 'Bug' }])
  })

  it('createLabel sends POST with label payload', async () => {
    const label: CreateLabelDto = { name: 'Feature', colorHex: '#00ff00' }
    vi.mocked(http.post).mockResolvedValue({ data: { id: '2', ...label } })

    const result = await labelsApi.createLabel('board-1', label)

    expect(http.post).toHaveBeenCalledWith('/boards/board-1/labels', label)
    expect(result).toEqual({ id: '2', ...label })
  })

  it('updateLabel sends PATCH with label payload', async () => {
    const update: UpdateLabelDto = { name: 'Bugfix' }
    vi.mocked(http.patch).mockResolvedValue({ data: { id: '1', name: 'Bugfix' } })

    const result = await labelsApi.updateLabel('board-1', 'label-1', update)

    expect(http.patch).toHaveBeenCalledWith('/boards/board-1/labels/label-1', update)
    expect(result).toEqual({ id: '1', name: 'Bugfix' })
  })

  it('deleteLabel sends DELETE to label endpoint', async () => {
    vi.mocked(http.delete).mockResolvedValue({})

    await labelsApi.deleteLabel('board-1', 'label-1')

    expect(http.delete).toHaveBeenCalledWith('/boards/board-1/labels/label-1')
  })
})
