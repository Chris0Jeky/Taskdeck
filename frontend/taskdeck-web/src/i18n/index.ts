import { createI18n } from 'vue-i18n'
import en from '../locales/en'

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
 *
 * Only the `en` catalog ships in the initial bundle (#1858): `it`/`es` are
 * code-split behind `ensureLocaleMessages()` and fetched the first time the
 * user selects them, so adding translated surfaces no longer spends the
 * total-JS budget for users who never leave English. While a catalog is in
 * flight (or if its chunk fails to load), the silent-fallback semantics above
 * already describe what the user sees: English.
 */

export const SUPPORTED_LOCALES = ['en', 'it', 'es'] as const

export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number]

export const DEFAULT_LOCALE: SupportedLocale = 'en'

/**
 * Locales whose catalogs are machine-translated and not yet reviewed by a
 * native speaker (#1770, walkthrough decision e-7 of 2026-08-23). The language
 * picker discloses this next to each affected option; remove a locale from
 * this list only when its review is actually recorded on #1770.
 */
export const MACHINE_TRANSLATED_LOCALES: ReadonlyArray<SupportedLocale> = ['it', 'es']

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

export const i18n = createI18n({
  legacy: false,
  globalInjection: true,
  locale: DEFAULT_LOCALE,
  fallbackLocale: DEFAULT_LOCALE,
  // See the block comment above — silent fallback is the specified behaviour.
  missingWarn: false,
  fallbackWarn: false,
  // The cast keeps the LOCALE type the full union while only `en` is actually
  // registered eagerly — without it, inference narrows the runtime locale to
  // the literal 'en' and every `locale.value = 'it'` stops typechecking. The
  // missing it/es entries are exactly the lazy-load contract: vue-i18n treats
  // an unregistered locale as empty and falls back silently (see above).
  messages: { en } as Record<SupportedLocale, typeof en>,
})

// Static per-locale thunks (not a template-string import) so Vite can see each
// chunk at build time and emit exactly one per locale. The catalog guard keeps
// every locale structurally identical to `en`, which is what makes the shared
// `typeof en` here honest.
type LazyLocale = Exclude<SupportedLocale, 'en'>
const localeLoaders: Record<LazyLocale, () => Promise<{ default: typeof en }>> = {
  it: () => import('../locales/it'),
  es: () => import('../locales/es'),
}

const inFlight = new Map<SupportedLocale, Promise<boolean>>()
const loaded = new Set<SupportedLocale>([DEFAULT_LOCALE])

/**
 * Fetch and register a locale's catalog, once. Resolves `true` when the
 * catalog is available (already or newly), `false` when the chunk failed to
 * load — in which case the failure is forgotten so a later switch retries,
 * and the user simply stays on English fallback in the meantime. Never throws.
 */
export function ensureLocaleMessages(locale: SupportedLocale): Promise<boolean> {
  if (loaded.has(locale)) return Promise.resolve(true)
  const pending = inFlight.get(locale)
  if (pending) return pending
  const load = localeLoaders[locale as LazyLocale]()
    .then((mod) => {
      i18n.global.setLocaleMessage(locale, mod.default)
      loaded.add(locale)
      return true
    })
    .catch(() => false)
    .finally(() => {
      inFlight.delete(locale)
    })
  inFlight.set(locale, load)
  return load
}

export default i18n
