import { defineStore } from 'pinia'
import {
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  ensureLocaleMessages,
  isSupportedLocale,
  i18n,
  type SupportedLocale,
} from '../i18n'

/**
 * Language preference (ADR-0054 §7).
 *
 * Deliberately mirrors `paperThemeStore`: a Pinia store whose preferred value
 * is read from and written to `localStorage`, validated on read, defaulted on
 * garbage, with an `apply()` action that pushes a loaded preference into the
 * runtime — there, a class on `<body>`; here, the vue-i18n locale plus
 * `<html lang>`.
 *
 * This is a CLIENT DISPLAY preference. It is not sent to the backend and there
 * is no server-side user-preference row for it. If it ever needs to follow the
 * account across devices, that is a separate decision and a backend change.
 */

const STORAGE_KEY = 'td.locale.v1'

export { SUPPORTED_LOCALES, DEFAULT_LOCALE }
export type { SupportedLocale }

function readStoredLocale(): SupportedLocale {
  if (typeof window === 'undefined') return DEFAULT_LOCALE
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (isSupportedLocale(raw)) return raw
  } catch {
    // localStorage may throw in private mode; fall through to the default.
  }
  return DEFAULT_LOCALE
}

function applyLocale(locale: SupportedLocale) {
  // `i18n.global.locale` is a WritableComputedRef in composition mode
  // (`legacy: false`), so assigning `.value` is what re-renders every consumer.
  i18n.global.locale.value = locale
  if (typeof document !== 'undefined') {
    document.documentElement.setAttribute('lang', locale)
  }
}

function persistLocale(locale: SupportedLocale) {
  try {
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(STORAGE_KEY, locale)
    }
  } catch {
    // ignore quota / private-mode failures — the in-memory preference still applies
  }
}

// A catalog response is allowed to commit only if it belongs to the most
// recent request. This is deliberately separate from the catalog loader's
// per-locale in-flight deduplication: two different locale requests can be in
// flight at once, and the older one must not overwrite the newer choice.
let latestRequestGeneration = 0

export const useLocaleStore = defineStore('locale', {
  state: () => ({
    // `locale` is the committed/displayed locale. The stored preference is a
    // desired locale until its catalog has loaded successfully.
    locale: DEFAULT_LOCALE as SupportedLocale,
    preferredLocale: readStoredLocale() as SupportedLocale,
    pendingLocale: null as SupportedLocale | null,
    failedLocale: null as SupportedLocale | null,
  }),
  getters: {
    available(): ReadonlyArray<SupportedLocale> {
      return SUPPORTED_LOCALES
    },
    isPending: (state): boolean => state.pendingLocale !== null,
  },
  actions: {
    /**
     * Restore the stored preference before the first app mount. A non-English
     * catalog is loaded before the preference is committed, so the first
     * mounted render cannot claim a language whose messages are unavailable.
     *
     * If the chunk fails, the committed runtime locale remains usable and the
     * persisted preference is deliberately kept so a transient failure
     * (offline, stale deployment mid-swap) retries on the next boot or manual
     * switch. `failedLocale` lets the mounted picker report that target
     * honestly instead of silently falling back.
     */
    apply(): Promise<void> {
      const target = this.preferredLocale
      const generation = ++latestRequestGeneration

      this.failedLocale = null
      applyLocale(this.locale)

      if (target === this.locale) {
        this.pendingLocale = null
        return Promise.resolve()
      }

      this.pendingLocale = target
      return Promise.resolve()
        .then(() => ensureLocaleMessages(target))
        .catch(() => false)
        .then((ok) => {
          if (generation !== latestRequestGeneration) return

          this.pendingLocale = null
          if (ok) {
            this.locale = target
            applyLocale(target)
            return
          }

          applyLocale(this.locale)
          this.failedLocale = target
        })
    },
    setLocale(locale: SupportedLocale): Promise<void> {
      // Guard the public entry point too: a bad value here would otherwise be
      // persisted and only rejected on the NEXT read, leaving the running app
      // on a locale with no catalog.
      if (!isSupportedLocale(locale)) return Promise.resolve()

      this.preferredLocale = locale
      persistLocale(locale)

      const generation = ++latestRequestGeneration
      this.failedLocale = null
      applyLocale(this.locale)

      // Selecting the committed language again cancels an older pending
      // request and restores a truthful, idle picker immediately.
      if (locale === this.locale) {
        this.pendingLocale = null
        return Promise.resolve()
      }

      this.pendingLocale = locale
      return Promise.resolve()
        .then(() => ensureLocaleMessages(locale))
        .catch(() => false)
        .then((ok) => {
          if (generation !== latestRequestGeneration) return

          this.pendingLocale = null
          if (ok) {
            this.locale = locale
            applyLocale(locale)
            return
          }

          // Do not infer that a failed chunk left a usable catalog. Keep the
          // last committed language and report the requested target to the UI.
          applyLocale(this.locale)
          this.failedLocale = locale
        })
    },
  },
})
