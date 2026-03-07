import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { workspaceApi } from '../../api/workspaceApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    put: vi.fn(),
  },
}))

describe('workspaceApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads home summary', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { isFirstRun: false } })

    await workspaceApi.getHomeSummary()

    expect(http.get).toHaveBeenCalledWith('/workspace/home')
  })

  it('loads preferences', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { workspaceMode: 'guided' } })

    await workspaceApi.getPreferences()

    expect(http.get).toHaveBeenCalledWith('/workspace/preferences')
  })

  it('updates preferences', async () => {
    vi.mocked(http.put).mockResolvedValue({ data: { workspaceMode: 'agent' } })

    await workspaceApi.updatePreferences({ workspaceMode: 'agent' })

    expect(http.put).toHaveBeenCalledWith('/workspace/preferences', { workspaceMode: 'agent' })
  })
})
