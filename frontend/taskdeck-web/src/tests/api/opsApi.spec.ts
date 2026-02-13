import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { opsApi } from '../../api/opsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('opsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads command templates', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await opsApi.getTemplates()

    expect(http.get).toHaveBeenCalledWith('/ops/cli/templates')
  })

  it('builds log query string', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await opsApi.queryLogs({ level: 'Error', source: 'OpsCliService', limit: 20 })

    expect(http.get).toHaveBeenCalledWith('/logs?level=Error&source=OpsCliService&limit=20')
  })
})
