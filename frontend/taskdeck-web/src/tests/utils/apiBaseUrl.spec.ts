import { describe, expect, it } from 'vitest'
import {
  LOCAL_DEV_API_BASE_URL,
  isLoopbackApiBase,
  isStaticHostedDemoOrigin,
  resolveApiBaseUrl,
  shouldUseDemoMode,
} from '../../utils/apiBaseUrl'

describe('apiBaseUrl demo detection', () => {
  describe('isStaticHostedDemoOrigin', () => {
    it.each([
      ['chris0jeky.github.io', true],
      ['github.io', true],
      ['taskdeck.pages.dev', true],
      ['localhost', false],
      ['127.0.0.1', false],
      ['example.com', false],
      ['', false],
    ])('%o → %s', (hostname, expected) => {
      expect(isStaticHostedDemoOrigin(hostname)).toBe(expected)
    })
  })

  describe('isLoopbackApiBase', () => {
    it.each([
      ['http://localhost:5000/api', true],
      ['http://127.0.0.1:5000/api', true],
      ['https://api.example.test/api', false],
      ['/api', false],
      ['/Taskdeck/api', false],
      ['', false],
    ])('%o → %s', (apiBase, expected) => {
      expect(isLoopbackApiBase(apiBase)).toBe(expected)
    })
  })

  describe('shouldUseDemoMode', () => {
    it('is on when the API base is empty (Pages build)', () => {
      expect(shouldUseDemoMode({ apiBase: '', hostname: 'localhost', demoFlag: false })).toBe(true)
    })

    it('is on when VITE_DEMO_MODE is set', () => {
      expect(shouldUseDemoMode({
        apiBase: LOCAL_DEV_API_BASE_URL,
        hostname: 'localhost',
        demoFlag: true,
      })).toBe(true)
    })

    it('is on when GitHub Pages still points at loopback', () => {
      expect(shouldUseDemoMode({
        apiBase: LOCAL_DEV_API_BASE_URL,
        hostname: 'chris0jeky.github.io',
        demoFlag: false,
      })).toBe(true)
    })

    it('stays off for local-dev against the loopback API', () => {
      expect(shouldUseDemoMode({
        apiBase: LOCAL_DEV_API_BASE_URL,
        hostname: 'localhost',
        demoFlag: false,
      })).toBe(false)
    })

    it('stays off for a hosted origin with a real API base', () => {
      expect(shouldUseDemoMode({
        apiBase: 'https://api.taskdeck.example/api',
        hostname: 'chris0jeky.github.io',
        demoFlag: false,
      })).toBe(false)
    })
  })

  describe('resolveApiBaseUrl', () => {
    it('never returns localhost in demo mode', () => {
      expect(resolveApiBaseUrl({ apiBase: '', hostname: 'chris0jeky.github.io' })).toBe('')
      expect(resolveApiBaseUrl({
        apiBase: LOCAL_DEV_API_BASE_URL,
        hostname: 'chris0jeky.github.io',
      })).toBe('')
    })

    it('keeps a configured local-dev API base when demo is off', () => {
      expect(resolveApiBaseUrl({
        apiBase: LOCAL_DEV_API_BASE_URL,
        hostname: 'localhost',
        demoFlag: false,
      })).toBe(LOCAL_DEV_API_BASE_URL)
    })
  })
})
