import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { resolveBodyClass, usePaperThemeStore } from '../../store/paperThemeStore'

const STORAGE_KEY = 'td.paper.mode.v2'
const LEGACY_STORAGE_KEY = 'td.paper.mode'

function setMatchMediaDark(isDark: boolean) {
  let currentMatches = isDark
  const instances: Array<{
    matches: boolean
    listeners: Set<(ev: MediaQueryListEvent) => void>
  }> = []
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn().mockImplementation(() => {
      const instance = {
        matches: currentMatches,
        listeners: new Set<(ev: MediaQueryListEvent) => void>(),
      }
      instances.push(instance)
      return {
        get matches() {
          return instance.matches
        },
        media: '(prefers-color-scheme: dark)',
        addEventListener: (
          type: string,
          listener: EventListenerOrEventListenerObject,
        ) => {
          if (type === 'change' && typeof listener === 'function') {
            instance.listeners.add(listener as (ev: MediaQueryListEvent) => void)
          }
        },
        removeEventListener: (
          type: string,
          listener: EventListenerOrEventListenerObject,
        ) => {
          if (type === 'change' && typeof listener === 'function') {
            instance.listeners.delete(listener as (ev: MediaQueryListEvent) => void)
          }
        },
      }
    }),
  })
  return {
    fire(matches: boolean) {
      // Real browsers update .matches before firing 'change'.
      currentMatches = matches
      instances.forEach((instance) => {
        instance.matches = matches
        instance.listeners.forEach((l) => l({ matches } as MediaQueryListEvent))
      })
    },
    listenerCount: () =>
      instances.reduce((count, instance) => count + instance.listeners.size, 0),
    instanceCount: () => instances.length,
  }
}

describe('paperThemeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
    document.body.classList.remove('paper', 'paper-night')
    setMatchMediaDark(false)
  })

  afterEach(() => {
    document.body.classList.remove('paper', 'paper-night')
  })

  it('defaults to paper when nothing stored (Paper is canonical — ADR-0038)', () => {
    const store = usePaperThemeStore()
    expect(store.mode).toBe('paper')
    expect(store.isOn).toBe(true)
    expect(store.activeClass).toBe('paper')
  })

  it('restores mode from localStorage', () => {
    window.localStorage.setItem(STORAGE_KEY, 'paper-night')
    const store = usePaperThemeStore()
    expect(store.mode).toBe('paper-night')
    expect(store.activeClass).toBe('paper-night')
  })

  it('falls back to the paper default for an invalid stored value', () => {
    window.localStorage.setItem(STORAGE_KEY, 'midnight')
    const store = usePaperThemeStore()
    expect(store.mode).toBe('paper')
  })

  describe('storage-key v2 migration (ADR-0038 flip)', () => {
    it('carries a deliberate non-off legacy choice over to v2 and clears the old key', () => {
      window.localStorage.setItem(LEGACY_STORAGE_KEY, 'paper-night')
      const store = usePaperThemeStore()
      expect(store.mode).toBe('paper-night')
      expect(window.localStorage.getItem(STORAGE_KEY)).toBe('paper-night')
      expect(window.localStorage.getItem(LEGACY_STORAGE_KEY)).toBeNull()
    })

    it('carries over a legacy auto choice', () => {
      window.localStorage.setItem(LEGACY_STORAGE_KEY, 'auto')
      const store = usePaperThemeStore()
      expect(store.mode).toBe('auto')
      expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
    })

    it('treats a legacy off (the pre-flip default / never-opted-in) as the new paper default, dropping the old key', () => {
      window.localStorage.setItem(LEGACY_STORAGE_KEY, 'off')
      const store = usePaperThemeStore()
      expect(store.mode).toBe('paper')
      // 'off' is dropped (not carried to v2); the old key is cleared so it can't be re-read.
      expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull()
      expect(window.localStorage.getItem(LEGACY_STORAGE_KEY)).toBeNull()
    })

    it('lets a v2 value take precedence over the legacy key (no migration when v2 is set)', () => {
      window.localStorage.setItem(LEGACY_STORAGE_KEY, 'paper-night')
      window.localStorage.setItem(STORAGE_KEY, 'off')
      const store = usePaperThemeStore()
      expect(store.mode).toBe('off')
    })
  })

  it('applies the paper class to <body> on apply()', () => {
    const store = usePaperThemeStore()
    store.setMode('paper')
    expect(document.body.classList.contains('paper')).toBe(true)
    expect(document.body.classList.contains('paper-night')).toBe(false)
  })

  it('replaces paper with paper-night cleanly', () => {
    const store = usePaperThemeStore()
    store.setMode('paper')
    store.setMode('paper-night')
    expect(document.body.classList.contains('paper')).toBe(false)
    expect(document.body.classList.contains('paper-night')).toBe(true)
  })

  it('removes any paper class when set to off', () => {
    const store = usePaperThemeStore()
    store.setMode('paper-night')
    store.setMode('off')
    expect(document.body.classList.contains('paper')).toBe(false)
    expect(document.body.classList.contains('paper-night')).toBe(false)
  })

  it('persists the mode to localStorage', () => {
    const store = usePaperThemeStore()
    store.setMode('paper-night')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('paper-night')
  })

  it('toggleNight flips between paper and paper-night', () => {
    const store = usePaperThemeStore()
    store.setMode('paper')
    store.toggleNight()
    expect(store.mode).toBe('paper-night')
    store.toggleNight()
    expect(store.mode).toBe('paper')
  })

  it('toggleNight from off enables paper light first', () => {
    const store = usePaperThemeStore()
    store.setMode('off') // default is now 'paper'; this test exercises the from-off path explicitly
    store.toggleNight()
    expect(store.mode).toBe('paper')
  })

  it('auto mode resolves to paper-night when prefers-color-scheme is dark', () => {
    setMatchMediaDark(true)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(store.activeClass).toBe('paper-night')
    expect(document.body.classList.contains('paper-night')).toBe(true)
  })

  it('auto mode resolves to paper when prefers-color-scheme is light', () => {
    setMatchMediaDark(false)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(store.activeClass).toBe('paper')
  })

  it('auto mode reacts to live changes in prefers-color-scheme', () => {
    const mq = setMatchMediaDark(false)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(document.body.classList.contains('paper')).toBe(true)
    expect(mq.listenerCount()).toBe(1)
    mq.fire(true)
    // The body class is the source of truth for what the user sees.
    // We deliberately bypass the activeClass getter here because Pinia
    // memoizes it on state.mode and prefersDark() is not a reactive dep —
    // see the comment in _wireAutoListener.
    expect(document.body.classList.contains('paper-night')).toBe(true)
    expect(document.body.classList.contains('paper')).toBe(false)
    mq.fire(false)
    expect(document.body.classList.contains('paper')).toBe(true)
    expect(document.body.classList.contains('paper-night')).toBe(false)
  })

  // PAPER-12 regression: localStorage must persist 'auto' across OS-scheme
  // changes — we never want the persisted value to drift to the resolved
  // 'paper-night' / 'paper' literal when the user picked 'auto'.
  it('persists auto mode in localStorage even after OS scheme toggles', () => {
    const mq = setMatchMediaDark(false)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
    mq.fire(true)
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
    mq.fire(false)
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
  })

  it('re-resolves auto mode whenever apply() runs after an OS theme change', () => {
    const mq = setMatchMediaDark(false)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(document.body.classList.contains('paper')).toBe(true)

    mq.fire(true)
    store.apply()

    expect(document.body.classList.contains('paper-night')).toBe(true)
    expect(document.body.classList.contains('paper')).toBe(false)
  })

  it('cleans up the auto listener when leaving auto mode', () => {
    const mq = setMatchMediaDark(false)
    const store = usePaperThemeStore()
    store.setMode('auto')
    expect(mq.listenerCount()).toBe(1)
    expect(mq.instanceCount()).toBeGreaterThanOrEqual(1)
    store.setMode('paper')
    expect(mq.listenerCount()).toBe(0)
  })

  it('resolveBodyClass is pure and SSR-safe for off mode', () => {
    expect(resolveBodyClass('off')).toBeNull()
    expect(resolveBodyClass('paper')).toBe('paper')
    expect(resolveBodyClass('paper-night')).toBe('paper-night')
  })

  it('survives localStorage throwing (private mode)', () => {
    const original = window.localStorage.setItem
    window.localStorage.setItem = vi.fn(() => {
      throw new Error('quota')
    })
    try {
      const store = usePaperThemeStore()
      expect(() => store.setMode('paper')).not.toThrow()
      expect(document.body.classList.contains('paper')).toBe(true)
    } finally {
      window.localStorage.setItem = original
    }
  })
})
