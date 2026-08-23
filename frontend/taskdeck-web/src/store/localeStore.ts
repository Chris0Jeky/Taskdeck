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
 * Deliberately mirrors `paperThemeStore`: a Pinia store whose value is read
 * from and written to `localStorage`, validated on read, defaulted on garbage,
 * with an `apply()` action that pushes the value into the runtime — there, a
 * class on `<body>`; here, the vue-i18n locale plus `<html lang>`.
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

export const useLocaleStore = defineStore('locale', {
  state: () => ({
    locale: readStoredLocale() as SupportedLocale,
  }),
  getters: {
    available(): ReadonlyArray<SupportedLocale> {
      return SUPPORTED_LOCALES
    },
  },
  actions: {
    /**
     * Push the current locale into the i18n runtime and `<html lang>`.
     * Idempotent. Flip FIRST: `it`/`es` catalogs are code-split (#1858), and
     * until the chunk arrives the silent en-fallback shows English — the same
     * thing the user already sees for any not-yet-extracted surface;
     * `setLocaleMessage` is reactive, so translations appear when it lands.
     *
     * If the chunk FAILS, the runtime, `<html lang>`, and the in-memory store
     * value are all reverted to English so the UI never claims a language it
     * is not rendering (the flip-first window is bounded by the request; a
     * failure is not). The persisted preference is deliberately KEPT: a
     * transient failure (offline, stale deployment mid-swap) self-heals on the
     * next boot or the next manual switch instead of silently discarding the
     * user's choice. The returned promise settles after any revert; callers
     * that only care about the switch itself may ignore it.
     */
    apply(): Promise<void> {
      const target = this.locale
      applyLocale(target)
      return ensureLocaleMessages(target).then((ok) => {
        const stillWanted = this.locale === target && i18n.global.locale.value === target
        if (!ok && stillWanted && target !== DEFAULT_LOCALE) {
          this.locale = DEFAULT_LOCALE
          applyLocale(DEFAULT_LOCALE)
        }
      })
    },
    setLocale(locale: SupportedLocale): Promise<void> {
      // Guard the public entry point too: a bad value here would otherwise be
      // persisted and only rejected on the NEXT read, leaving the running app
      // on a locale with no catalog.
      if (!isSupportedLocale(locale)) return Promise.resolve()
      this.locale = locale
      try {
        if (typeof window !== 'undefined') {
          window.localStorage.setItem(STORAGE_KEY, locale)
        }
      } catch {
        // ignore quota / private-mode failures — the in-memory switch still applies
      }
      return this.apply()
    },
  },
})
