import { describe, expect, it } from 'vitest'
import { i18n, ensureLocaleMessages, SUPPORTED_LOCALES } from '../../i18n'
import { useLocaleStore } from '../../store/localeStore'

/**
 * Lazy locale catalogs (#1858).
 *
 * Only `en` is registered at module load; `it`/`es` arrive through
 * `ensureLocaleMessages()`. The global test setup preloads every catalog (so
 * the wider suite can keep checking translated rendering), which means this
 * spec asserts the loader contract plus the atomic switch boundary. The
 * "en-only in the initial chunk" claim itself is a build-graph property,
 * proven by the bundle-budget CI gate and the emitted per-locale chunks, not
 * assertable here.
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

  it('keeps the committed locale until the catalog promise settles', async () => {
    const store = useLocaleStore()

    const pending = store.setLocale('es')
    // A slow chunk must not make the UI claim Spanish while the old language
    // is still rendering.
    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBe('es')
    expect(i18n.global.locale.value).toBe('en')
    expect(pending).toBeInstanceOf(Promise)
    await pending

    expect(store.locale).toBe('es')
    expect(store.pendingLocale).toBeNull()
    expect(i18n.global.locale.value).toBe('es')

    await store.setLocale('en')
    expect(store.locale).toBe('en')
    expect(i18n.global.locale.value).toBe('en')
  })
})
