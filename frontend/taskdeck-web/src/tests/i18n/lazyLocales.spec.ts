import { describe, expect, it } from 'vitest'
import { i18n, ensureLocaleMessages, SUPPORTED_LOCALES } from '../../i18n'
import { useLocaleStore } from '../../store/localeStore'

/**
 * Lazy locale catalogs (#1858).
 *
 * Only `en` is registered at module load; `it`/`es` arrive through
 * `ensureLocaleMessages()`. The global test setup preloads every catalog (so
 * the wider suite can keep flipping locales synchronously), which means this
 * spec asserts the CONTRACT of the loader — idempotence, registration, and the
 * flip-first switch semantics — not the pre-load empty state. The "en-only in
 * the initial chunk" claim itself is a build-graph property, proven by the
 * bundle-budget CI gate and the emitted per-locale chunks, not assertable here.
 */

describe('lazy locale catalogs (#1858)', () => {
  it('resolves true and has the catalog registered, for every supported locale', async () => {
    for (const locale of SUPPORTED_LOCALES) {
      await expect(ensureLocaleMessages(locale)).resolves.toBe(true)
      // A registered catalog answers a real key without falling back.
      expect(Object.keys(i18n.global.getLocaleMessage(locale)).length).toBeGreaterThan(0)
    }
  })

  it('is idempotent — a second call resolves immediately with the same result', async () => {
    const first = await ensureLocaleMessages('it')
    const second = await ensureLocaleMessages('it')
    expect(first).toBe(true)
    expect(second).toBe(true)
  })

  it('setLocale flips the runtime locale synchronously and returns the catalog promise', async () => {
    const store = useLocaleStore()

    const pending = store.setLocale('es')
    // Flip-first: the locale is live before the catalog promise settles, so a
    // slow chunk shows English fallback rather than blocking the switch.
    expect(i18n.global.locale.value).toBe('es')
    expect(pending).toBeInstanceOf(Promise)
    await pending

    await store.setLocale('en')
    expect(i18n.global.locale.value).toBe('en')
  })
})
