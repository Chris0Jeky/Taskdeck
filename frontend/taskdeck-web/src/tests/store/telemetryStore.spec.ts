import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useTelemetryStore } from '../../store/telemetryStore'

// Mock the telemetry API
vi.mock('../../api/telemetryApi', () => ({
  telemetryApi: {
    getConfig: vi.fn().mockResolvedValue({
      sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
      analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
      telemetry: { enabled: false },
    }),
    sendEvents: vi.fn().mockResolvedValue({ recorded: 0 }),
  },
}))

import { telemetryApi } from '../../api/telemetryApi'

describe('telemetryStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.clearAllMocks()
  })

  afterEach(() => {
    const store = useTelemetryStore()
    store.stopFlushTimer()
  })

  describe('consent', () => {
    it('should default to no consent', () => {
      const store = useTelemetryStore()
      expect(store.consentGiven).toBe(false)
    })

    it('should set consent and persist to localStorage', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      expect(store.consentGiven).toBe(true)
      expect(localStorage.getItem('taskdeck_telemetry_consent')).toBe('true')
    })

    it('should restore consent from localStorage', () => {
      localStorage.setItem('taskdeck_telemetry_consent', 'true')
      const store = useTelemetryStore()
      store.restoreConsent()
      expect(store.consentGiven).toBe(true)
    })

    it('should clear buffer when consent is revoked', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      // Manually push an event into buffer
      store.eventBuffer.push({
        event: 'test.event',
        timestamp: new Date().toISOString(),
        sessionId: 'abc',
        workspaceMode: 'guided',
        appVersion: '0.1.0',
        platform: 'web',
      })
      expect(store.eventBuffer.length).toBe(1)

      store.setConsent(false)
      expect(store.eventBuffer.length).toBe(0)
    })
  })

  describe('isActive', () => {
    it('should be false when consent not given', () => {
      const store = useTelemetryStore()
      expect(store.isActive).toBe(false)
    })

    it('should be false when consent given but server telemetry disabled', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: false },
      }
      expect(store.isActive).toBe(false)
    })

    it('should be true when both consent and server enable telemetry', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      expect(store.isActive).toBe(true)
    })
  })

  describe('emit', () => {
    it('should not buffer events when inactive', () => {
      const store = useTelemetryStore()
      store.emit('capture.submitted')
      expect(store.eventBuffer.length).toBe(0)
    })

    it('should buffer events when active', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      store.emit('capture.submitted', { source: 'manual' })
      expect(store.eventBuffer.length).toBe(1)
      expect(store.eventBuffer[0].event).toBe('capture.submitted')
      expect(store.eventBuffer[0].properties).toEqual({ source: 'manual' })
    })

    it('should cap buffer size to prevent unbounded growth', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      // Push more than MAX_BUFFER_SIZE (200) events
      for (let i = 0; i < 250; i++) {
        store.emit('test.event')
      }
      expect(store.eventBuffer.length).toBeLessThanOrEqual(200)
    })
  })

  describe('flush', () => {
    it('should not flush when inactive', async () => {
      const store = useTelemetryStore()
      await store.flush()
      expect(telemetryApi.sendEvents).not.toHaveBeenCalled()
    })

    it('should not flush when buffer is empty', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      await store.flush()
      expect(telemetryApi.sendEvents).not.toHaveBeenCalled()
    })

    it('should send buffered events and clear buffer on success', async () => {
      vi.mocked(telemetryApi.sendEvents).mockResolvedValueOnce({ recorded: 1 })
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      store.emit('capture.submitted')
      expect(store.eventBuffer.length).toBe(1)

      await store.flush()
      expect(telemetryApi.sendEvents).toHaveBeenCalledTimes(1)
      expect(store.eventBuffer.length).toBe(0)
    })

    it('should re-buffer events on flush failure', async () => {
      vi.mocked(telemetryApi.sendEvents).mockRejectedValueOnce(new Error('network'))
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: true },
      }
      store.emit('capture.submitted')

      await store.flush()
      // Events should be re-buffered
      expect(store.eventBuffer.length).toBe(1)
    })
  })

  describe('loadConfig', () => {
    it('should fetch and store server config', async () => {
      const mockConfig = {
        sentry: { enabled: true, dsn: 'https://test@sentry.io/123', environment: 'prod', tracesSampleRate: 0.1 },
        analytics: { enabled: true, provider: 'plausible', scriptUrl: 'https://plausible.example.com/js/script.js', siteId: 'taskdeck.example.com' },
        telemetry: { enabled: true },
      }
      vi.mocked(telemetryApi.getConfig).mockResolvedValueOnce(mockConfig)

      const store = useTelemetryStore()
      await store.loadConfig()
      expect(store.configLoaded).toBe(true)
      expect(store.serverConfig).toEqual(mockConfig)
    })

    it('should handle config fetch failure gracefully', async () => {
      vi.mocked(telemetryApi.getConfig).mockRejectedValueOnce(new Error('network'))

      const store = useTelemetryStore()
      await store.loadConfig()
      expect(store.configLoaded).toBe(true)
      expect(store.serverConfig).toBeNull()
    })
  })

  describe('sentryAvailable', () => {
    it('should be false when consent not given', () => {
      const store = useTelemetryStore()
      store.serverConfig = {
        sentry: { enabled: true, dsn: 'https://test@sentry.io/123', environment: 'test', tracesSampleRate: 0.1 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: false },
      }
      expect(store.sentryAvailable).toBe(false)
    })

    it('should be true when consent given and sentry enabled with DSN', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: true, dsn: 'https://test@sentry.io/123', environment: 'test', tracesSampleRate: 0.1 },
        analytics: { enabled: false, provider: '', scriptUrl: '', siteId: '' },
        telemetry: { enabled: false },
      }
      expect(store.sentryAvailable).toBe(true)
    })
  })

  describe('analyticsConfig', () => {
    it('should be null when consent not given', () => {
      const store = useTelemetryStore()
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: true, provider: 'plausible', scriptUrl: 'https://example.com/script.js', siteId: 'test.com' },
        telemetry: { enabled: false },
      }
      expect(store.analyticsConfig).toBeNull()
    })

    it('should return config when consent given and analytics enabled', () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: { enabled: true, provider: 'plausible', scriptUrl: 'https://example.com/script.js', siteId: 'test.com' },
        telemetry: { enabled: false },
      }
      expect(store.analyticsConfig).not.toBeNull()
      expect(store.analyticsConfig?.provider).toBe('plausible')
    })
  })

  describe('sessionId', () => {
    it('should generate a non-empty session ID', () => {
      const store = useTelemetryStore()
      expect(store.sessionId).toBeTruthy()
      expect(store.sessionId.length).toBeGreaterThan(0)
    })
  })
})
