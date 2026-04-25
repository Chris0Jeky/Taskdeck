import { defineStore } from 'pinia'

export type PaperMode = 'off' | 'paper' | 'paper-night' | 'auto'

const STORAGE_KEY = 'td.paper.mode'

const VALID_MODES: ReadonlyArray<PaperMode> = ['off', 'paper', 'paper-night', 'auto']

function readStoredMode(): PaperMode {
  if (typeof window === 'undefined') return 'off'
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (raw && (VALID_MODES as readonly string[]).includes(raw)) {
      return raw as PaperMode
    }
  } catch {
    // localStorage may throw in private mode; fall through to default
  }
  return 'off'
}

function prefersDark(): boolean {
  if (typeof window === 'undefined' || !window.matchMedia) return false
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

/**
 * Resolve a mode value to the actual class that should sit on <body>.
 * `auto` flips with prefers-color-scheme; `off` returns null (no class).
 */
export function resolveBodyClass(mode: PaperMode): 'paper' | 'paper-night' | null {
  switch (mode) {
    case 'off':
      return null
    case 'paper':
      return 'paper'
    case 'paper-night':
      return 'paper-night'
    case 'auto':
      return prefersDark() ? 'paper-night' : 'paper'
  }
}

function applyBodyClass(klass: 'paper' | 'paper-night' | null) {
  if (typeof document === 'undefined') return
  const body = document.body
  body.classList.remove('paper', 'paper-night')
  if (klass) body.classList.add(klass)
}

// The prefers-color-scheme listener is module-scoped rather than living in
// Pinia state. Functions in reactive state get proxied, leak into devtools
// snapshots, and confuse $state cloning. We only ever need one listener at a
// time; the store action below tears down the previous one before wiring a
// new one.
let mediaListener: ((ev: MediaQueryListEvent) => void) | null = null

export const usePaperThemeStore = defineStore('paperTheme', {
  state: () => ({
    mode: readStoredMode() as PaperMode,
  }),
  getters: {
    isOn(state): boolean {
      return state.mode !== 'off'
    },
    activeClass(state): 'paper' | 'paper-night' | null {
      return resolveBodyClass(state.mode)
    },
  },
  actions: {
    /**
     * Apply current mode to <body>. Idempotent.
     * Also wires the prefers-color-scheme listener when in auto mode.
     */
    apply() {
      applyBodyClass(this.activeClass)
      this._wireAutoListener()
    },
    setMode(mode: PaperMode) {
      this.mode = mode
      try {
        if (typeof window !== 'undefined') {
          window.localStorage.setItem(STORAGE_KEY, mode)
        }
      } catch {
        // ignore quota / private mode failures
      }
      this.apply()
    },
    toggleNight() {
      // Quick toggle between light and night when Paper is on.
      // Off/auto round-trip through paper.
      if (this.mode === 'paper') this.setMode('paper-night')
      else if (this.mode === 'paper-night') this.setMode('paper')
      else this.setMode('paper')
    },
    enable() {
      if (this.mode === 'off') this.setMode('paper')
      else this.apply()
    },
    disable() {
      this.setMode('off')
    },
    _wireAutoListener() {
      if (typeof window === 'undefined' || !window.matchMedia) return
      const mq = window.matchMedia('(prefers-color-scheme: dark)')
      // Tear down the previously-registered listener if any
      if (mediaListener) {
        mq.removeEventListener?.('change', mediaListener)
        mediaListener = null
      }
      if (this.mode !== 'auto') return
      // Note: re-resolve from `this.mode` directly rather than the
      // `activeClass` getter — the getter is memoized via Pinia/Vue reactivity
      // and prefersDark() is not a reactive dependency, so it would return a
      // stale value when the OS toggles its color scheme without the mode
      // string itself changing.
      const listener = () => applyBodyClass(resolveBodyClass(this.mode))
      mq.addEventListener?.('change', listener)
      mediaListener = listener
    },
  },
})
