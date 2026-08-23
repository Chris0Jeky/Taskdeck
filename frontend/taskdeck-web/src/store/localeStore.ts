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

function applyLocale(locale: SupportedLocale): Promise<void> {
  // `i18n.global.locale` is a WritableComputedRef in composition mode
  // (`legacy: false`), so assigning `.value` is what re-renders every consumer.
  // Flip FIRST: `it`/`es` catalogs are code-split (#1858), and until the chunk
  // arrives the silent en-fallback shows English — the same thing the user
  // already sees for any not-yet-extracted surface. `setLocaleMessage` inside
  // `ensureLocaleMessages` is reactive, so the translations appear as soon as
  // the chunk lands, with no second action needed. On chunk failure the app
  // simply stays on the English fallback and the next switch retries.
  i18n.global.locale.value = locale
  if (typeof document !== 'undefined') {
    document.documentElement.setAttribute('lang', locale)
  }
  return ensureLocaleMessages(locale).then(() => undefined)
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
     * Idempotent. The returned promise settles when the locale's catalog is
     * registered (or its load failed and English fallback stands) — callers
     * that only care about the switch itself may ignore it.
     */
    apply(): Promise<void> {
      return applyLocale(this.locale)
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
