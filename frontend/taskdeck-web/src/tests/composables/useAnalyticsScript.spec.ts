import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { defineComponent, nextTick } from 'vue'
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

const SCRIPT_ID = 'taskdeck-analytics-script'

function cleanupScript() {
  const script = document.getElementById(SCRIPT_ID)
  if (script) {
    script.remove()
  }
}

describe('useAnalyticsScript', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    cleanupScript()
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanupScript()
    const store = useTelemetryStore()
    store.stopFlushTimer()
  })

  describe('useAnalyticsScript composable', () => {
    it('does not inject script when analyticsConfig is null', async () => {
      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
    })

    it('injects script when analyticsConfig is valid', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      const script = document.getElementById(SCRIPT_ID)
      expect(script).not.toBeNull()
      expect(script?.getAttribute('src')).toBe('https://plausible.example.com/js/script.js')
      expect(script?.getAttribute('data-domain')).toBe('taskdeck.example.com')
    })

    it('sets data-website-id for umami provider', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'umami',
          scriptUrl: 'https://umami.example.com/umami.js',
          siteId: 'abc-123-def',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      const script = document.getElementById(SCRIPT_ID)
      expect(script).not.toBeNull()
      expect(script?.getAttribute('data-website-id')).toBe('abc-123-def')
    })

    it('rejects non-HTTPS script URLs', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'http://insecure.example.com/script.js',
          siteId: 'test.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalledWith(
        '[Taskdeck] Analytics script URL rejected: must be HTTPS',
        'http://insecure.example.com/script.js'
      )

      warnSpy.mockRestore()
    })

    it('rejects javascript: protocol URLs', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'javascript:alert(1)',
          siteId: 'test.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalled()

      warnSpy.mockRestore()
    })

    it('rejects unsupported analytics providers', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'unsupported-provider',
          scriptUrl: 'https://example.com/script.js',
          siteId: 'test.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalledWith(
        '[Taskdeck] Analytics provider rejected: unsupported provider',
        'unsupported-provider'
      )

      warnSpy.mockRestore()
    })

    it('rejects invalid siteId formats', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://example.com/script.js',
          siteId: '<script>alert(1)</script>',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalledWith(
        '[Taskdeck] Analytics siteId rejected: invalid format',
        '<script>alert(1)</script>'
      )

      warnSpy.mockRestore()
    })

    it('rejects empty siteId', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://example.com/script.js',
          siteId: '',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalled()

      warnSpy.mockRestore()
    })

    it('does not inject duplicate scripts', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          const { injectScript } = useAnalyticsScript()
          // Call inject multiple times
          injectScript()
          injectScript()
          injectScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      const scripts = document.querySelectorAll(`#${SCRIPT_ID}`)
      expect(scripts.length).toBe(1)
    })

    it('removes script when component unmounts', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      const wrapper = mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).not.toBeNull()

      wrapper.unmount()
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
    })

    it('removes script when analyticsConfig becomes null', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).not.toBeNull()

      // Revoke consent
      store.setConsent(false)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
    })

    it('handles provider case insensitively', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'PLAUSIBLE',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      const script = document.getElementById(SCRIPT_ID)
      expect(script).not.toBeNull()
      expect(script?.getAttribute('data-domain')).toBe('taskdeck.example.com')
    })

    it('sets defer and async attributes on script', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      const TestComponent = defineComponent({
        setup() {
          useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)
      await nextTick()

      const script = document.getElementById(SCRIPT_ID) as HTMLScriptElement
      expect(script?.defer).toBe(true)
      expect(script?.async).toBe(true)
    })

    it('exposes injectScript and removeScript functions', async () => {
      const { useAnalyticsScript } = await import('../../composables/useAnalyticsScript')

      let exposed: ReturnType<typeof useAnalyticsScript> | null = null

      const TestComponent = defineComponent({
        setup() {
          exposed = useAnalyticsScript()
          return {}
        },
        template: '<div>Test</div>',
      })

      mount(TestComponent)

      expect(exposed).not.toBeNull()
      expect(typeof exposed?.injectScript).toBe('function')
      expect(typeof exposed?.removeScript).toBe('function')
    })
  })

  describe('initAnalyticsScriptWatcher', () => {
    it('injects script when analyticsConfig is valid', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      const script = document.getElementById(SCRIPT_ID)
      expect(script).not.toBeNull()
    })

    it('does not inject script when config is invalid', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'invalid',
          scriptUrl: 'https://example.com/script.js',
          siteId: 'test.com',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()

      warnSpy.mockRestore()
    })

    it('removes script when consent is revoked', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/js/script.js',
          siteId: 'taskdeck.example.com',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).not.toBeNull()

      // Revoke consent
      store.setConsent(false)
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
    })

    it('handles umami provider correctly', async () => {
      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'umami',
          scriptUrl: 'https://umami.example.com/umami.js',
          siteId: 'site-uuid-123',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      const script = document.getElementById(SCRIPT_ID)
      expect(script).not.toBeNull()
      expect(script?.getAttribute('data-website-id')).toBe('site-uuid-123')
    })

    it('validates HTTPS requirement', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'http://insecure.com/script.js',
          siteId: 'test.com',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalled()

      warnSpy.mockRestore()
    })

    it('validates siteId format', async () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      const store = useTelemetryStore()
      store.setConsent(true)
      store.serverConfig = {
        sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
        analytics: {
          enabled: true,
          provider: 'plausible',
          scriptUrl: 'https://plausible.example.com/script.js',
          siteId: 'invalid<>site',
        },
        telemetry: { enabled: false },
      }

      const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
      initAnalyticsScriptWatcher()
      await nextTick()

      expect(document.getElementById(SCRIPT_ID)).toBeNull()
      expect(warnSpy).toHaveBeenCalled()

      warnSpy.mockRestore()
    })
  })

  describe('valid siteId patterns', () => {
    const validSiteIds = [
      'example.com',
      'sub.example.com',
      'my-site.example.com',
      'my_site.example.com',
      'Site123',
      'a-b_c.d',
      'UUID-123-456',
    ]

    for (const siteId of validSiteIds) {
      it(`accepts valid siteId: ${siteId}`, async () => {
        cleanupScript()

        const store = useTelemetryStore()
        store.setConsent(true)
        store.serverConfig = {
          sentry: { enabled: false, dsn: '', environment: 'test', tracesSampleRate: 0 },
          analytics: {
            enabled: true,
            provider: 'plausible',
            scriptUrl: 'https://plausible.example.com/script.js',
            siteId,
          },
          telemetry: { enabled: false },
        }

        const { initAnalyticsScriptWatcher } = await import('../../composables/useAnalyticsScript')
        initAnalyticsScriptWatcher()
        await nextTick()

        const script = document.getElementById(SCRIPT_ID)
        expect(script).not.toBeNull()
        expect(script?.getAttribute('data-domain')).toBe(siteId)
      })
    }
  })
})
