import { createI18n } from 'vue-i18n'
import en from '../locales/en'
import es from '../locales/es'
import it from '../locales/it'

/**
 * i18n runtime (ADR-0054).
 *
 * Composition API mode (`legacy: false`) with `globalInjection` so `$t` is
 * usable in templates without a `useI18n()` line in every SFC; `useI18n()` is
 * still the way to translate inside `<script setup>`.
 *
 * `en` is both the default and the fallback locale, and missing-key /
 * fallback warnings are OFF on purpose: the rollout is surface-by-surface, so a
 * locale that has no entry for a not-yet-extracted surface is the EXPECTED
 * state, not an error state. A user on `it` sees English there — which is
 * right — and does not see a console full of warnings or a raw key path.
 *
 * The cost of silent fallback is that a genuinely missing key is invisible at
 * runtime. That is paid for at build time instead, by the catalog guard in
 * `src/tests/i18n/catalogs.spec.ts`, which fails on any key present in `en` and
 * absent from `it`/`es` (and vice versa).
 */

export const SUPPORTED_LOCALES = ['en', 'it', 'es'] as const

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

export const DEFAULT_LOCALE: SupportedLocale = 'en'

/**
 * Endonyms — each language names itself in its own language, whatever the
 * active locale. These are deliberately NOT catalog keys: translating them
 * would be the bug (a Spanish speaker looking for their language scans for
 * "Español", not for "Spanish" rendered in Italian).
 */
export const LOCALE_LABELS: Record<SupportedLocale, string> = {
  en: 'English',
  it: 'Italiano',
  es: 'Español',
}

export function isSupportedLocale(value: unknown): value is SupportedLocale {
  return typeof value === 'string' && (SUPPORTED_LOCALES as readonly string[]).includes(value)
}

export const messages = { en, it, es }

export const i18n = createI18n({
  legacy: false,
  globalInjection: true,
  locale: DEFAULT_LOCALE,
  fallbackLocale: DEFAULT_LOCALE,
  // See the block comment above — silent fallback is the specified behaviour.
  missingWarn: false,
  fallbackWarn: false,
  messages,
})

export default i18n
