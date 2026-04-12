import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { telemetryApi } from '../../api/telemetryApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('telemetryApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('getConfig', () => {
    it('should fetch telemetry config from correct endpoint', async () => {
      const mockConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: false },
      }
      vi.mocked(http.get).mockResolvedValue({ data: mockConfig })

      const result = await telemetryApi.getConfig()

      expect(http.get).toHaveBeenCalledWith('/telemetry/config')
      expect(result).toEqual(mockConfig)
    })
  })

  describe('sendEvents', () => {
    it('should post events to correct endpoint', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: { recorded: 2 } })

      const events = [
        {
          event: 'capture.submitted',
          timestamp: '2026-04-09T12:00:00Z',
          sessionId: 'abc',
          workspaceMode: 'guided',
          appVersion: '0.1.0',
          platform: 'web' as const,
        },
        {
          event: 'board.loaded',
          timestamp: '2026-04-09T12:00:01Z',
          sessionId: 'abc',
          workspaceMode: 'guided',
          appVersion: '0.1.0',
          platform: 'web' as const,
        },
      ]

      const result = await telemetryApi.sendEvents(events)

      expect(http.post).toHaveBeenCalledWith('/telemetry/events', { events })
      expect(result.recorded).toBe(2)
    })

    it('should send empty events array', async () => {
      vi.mocked(http.post).mockResolvedValue({ data: { recorded: 0 } })

      const result = await telemetryApi.sendEvents([])

      expect(http.post).toHaveBeenCalledWith('/telemetry/events', { events: [] })
      expect(result.recorded).toBe(0)
    })
  })
})
