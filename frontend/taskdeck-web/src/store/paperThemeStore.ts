import { defineStore } from 'pinia'

export type PaperMode = 'off' | 'paper' | 'paper-night' | 'auto'

// Bumped to v2 for the canonical-Paper flip (ADR-0038). The default is now 'paper', so a
// pre-flip stored 'off' — which was BOTH the old default AND a deliberate opt-out — must not be
// read as an opt-out under the new regime. A fresh key + the one-time migration below disambiguate.
const STORAGE_KEY = 'td.paper.mode.v2'
const LEGACY_STORAGE_KEY = 'td.paper.mode'

// Paper is the canonical UI (ADR-0038): default ON, in light Paper.
const DEFAULT_MODE: PaperMode = 'paper'

const VALID_MODES: ReadonlyArray<PaperMode> = ['off', 'paper', 'paper-night', 'auto']

// One-time migration to the v2 key at the canonical-Paper flip (ADR-0038). Runs only while v2 is
// unset. A DELIBERATE pre-flip choice (paper / paper-night / auto) is carried over to v2 verbatim;
// a stored 'off' — indistinguishable from the old default, i.e. "never opted into Paper" — and an
// absent/invalid value are dropped so they resolve to the new 'paper' default. The legacy key is
// then cleared either way, so the migration runs at most once and the old key does not linger.
function migrateLegacyMode(): void {
  if (typeof window === 'undefined') return
  try {
    if (window.localStorage.getItem(STORAGE_KEY) !== null) return
    const legacy = window.localStorage.getItem(LEGACY_STORAGE_KEY)
    if (legacy === null) return
    if (legacy === 'paper' || legacy === 'paper-night' || legacy === 'auto') {
      window.localStorage.setItem(STORAGE_KEY, legacy)
    }
    window.localStorage.removeItem(LEGACY_STORAGE_KEY)
  } catch {
    // localStorage may throw in private mode; skip the migration
  }
}

function readStoredMode(): PaperMode {
  if (typeof window === 'undefined') return DEFAULT_MODE
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (raw && (VALID_MODES as readonly string[]).includes(raw)) {
      return raw as PaperMode
    }
  } catch {
    // localStorage may throw in private mode; fall through to default
  }
  return DEFAULT_MODE
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
let mediaQueryList: MediaQueryList | null = null

export const usePaperThemeStore = defineStore('paperTheme', {
  state: () => {
    // Migrate the pre-flip key once (moves a deliberate choice to v2, clears the old key) before
    // the first read, so readStoredMode only ever consults v2.
    migrateLegacyMode()
    return {
      mode: readStoredMode() as PaperMode,
    }
  },
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
      applyBodyClass(resolveBodyClass(this.mode))
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
      // Tear down the previously-registered listener if any
      if (mediaQueryList && mediaListener) {
        mediaQueryList.removeEventListener?.('change', mediaListener)
        mediaListener = null
        mediaQueryList = null
      }
      if (this.mode !== 'auto') return
      const mq = window.matchMedia('(prefers-color-scheme: dark)')
      // Note: re-resolve from `this.mode` directly rather than the
      // `activeClass` getter — the getter is memoized via Pinia/Vue reactivity
      // and prefersDark() is not a reactive dependency, so it would return a
      // stale value when the OS toggles its color scheme without the mode
      // string itself changing.
      const listener = () => applyBodyClass(resolveBodyClass(this.mode))
      mq.addEventListener?.('change', listener)
      mediaListener = listener
      mediaQueryList = mq
    },
  },
})
