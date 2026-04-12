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

  it('loads today summary', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { summary: { dueTodayCards: 0 } } })

    await workspaceApi.getTodaySummary()

    expect(http.get).toHaveBeenCalledWith('/workspace/today')
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

  it('updates onboarding visibility', async () => {
    vi.mocked(http.put).mockResolvedValue({ data: { visibility: 'dismissed' } })

    await workspaceApi.updateOnboarding({ action: 'dismiss' })

    expect(http.put).toHaveBeenCalledWith('/workspace/onboarding', { action: 'dismiss' })
  })

  it('loads calendar data with from/to parameters', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { from: '2026-04-01', to: '2026-05-01', totalCards: 0, cards: [] } })

    const from = '2026-04-01T00:00:00.000Z'
    const to = '2026-05-01T00:00:00.000Z'
    await workspaceApi.getCalendar(from, to)

    expect(http.get).toHaveBeenCalledWith(
      expect.stringContaining('/workspace/calendar?'),
    )
    expect(http.get).toHaveBeenCalledWith(
      expect.stringContaining('from='),
    )
    expect(http.get).toHaveBeenCalledWith(
      expect.stringContaining('to='),
    )
  })
})
