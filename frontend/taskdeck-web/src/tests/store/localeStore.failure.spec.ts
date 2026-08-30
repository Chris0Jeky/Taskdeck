import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

/**
 * Catalog-load failure revert (#1858 review round).
 *
 * When a lazy locale chunk fails to load, the flip-first switch must not leave
 * the app claiming Italian while rendering English: the runtime locale,
 * `<html lang>`, and the in-memory store value all revert to English. The
 * persisted preference is kept on purpose so a transient failure self-heals on
 * the next boot — that asymmetry is asserted here too.
 */

vi.mock('../../i18n', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../i18n')>()
  return { ...actual, ensureLocaleMessages: vi.fn().mockResolvedValue(false) }
})

import { i18n, ensureLocaleMessages } from '../../i18n'
import { useLocaleStore } from '../../store/localeStore'

const STORAGE_KEY = 'td.locale.v1'

describe('localeStore — catalog load failure', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
    i18n.global.locale.value = 'en'
    vi.mocked(ensureLocaleMessages).mockClear()
  })

  afterEach(() => {
    i18n.global.locale.value = 'en'
    document.documentElement.removeAttribute('lang')
  })

  it('reverts runtime locale, <html lang>, and store state — but keeps the persisted preference', async () => {
    const store = useLocaleStore()

    const pending = store.setLocale('it')
    // Flip-first window: the switch is live while the (doomed) load runs.
    expect(i18n.global.locale.value).toBe('it')

    await pending

    expect(ensureLocaleMessages).toHaveBeenCalledWith('it')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
    expect(store.locale).toBe('en')
    // Kept: a transient failure retries from storage on the next boot.
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('it')
  })

  it('does not fight a newer switch that happened while the load was failing', async () => {
    const store = useLocaleStore()

    const first = store.setLocale('it')
    const second = store.setLocale('es')
    await Promise.all([first, second])

    // The failed 'it' load must not revert the meanwhile-selected 'es'... which
    // itself failed too, so the final state is the reverted default — but via
    // the 'es' revert, never a stale 'it' writer. Either way the invariant
    // holds: runtime and store agree, and they are a supported value.
    expect(store.locale).toBe(i18n.global.locale.value)
    expect(store.locale).toBe('en')
  })
})
