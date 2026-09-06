import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

/**
 * Catalog-load failure and atomic locale commit (#2003).
 *
 * A lazy locale switch must not claim Italian while the Italian catalog is
 * still loading or has failed: the previously committed runtime locale,
 * `<html lang>`, and store value remain aligned. The persisted preference is
 * kept on purpose so a transient failure self-heals on the next boot — that
 * asymmetry is asserted here too.
 */

vi.mock('../../i18n', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../i18n')>()
  return { ...actual, ensureLocaleMessages: vi.fn().mockResolvedValue(false) }
})

import { i18n, ensureLocaleMessages } from '../../i18n'
import { useLocaleStore } from '../../store/localeStore'

const STORAGE_KEY = 'td.locale.v1'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

describe('localeStore — catalog load failure', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
    i18n.global.locale.value = 'en'
    document.documentElement.setAttribute('lang', 'en')
    vi.mocked(ensureLocaleMessages).mockClear()
    vi.mocked(ensureLocaleMessages).mockResolvedValue(false)
  })

  afterEach(() => {
    i18n.global.locale.value = 'en'
    document.documentElement.removeAttribute('lang')
  })

  it('keeps the committed locale aligned while a switch fails, and reports the failed target', async () => {
    const store = useLocaleStore()

    const pending = store.setLocale('it')
    // Atomic commit: the previous language remains live while the catalog is
    // in flight, and the pending target is explicit state for the picker.
    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBe('it')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')

    await pending

    expect(ensureLocaleMessages).toHaveBeenCalledWith('it')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBeNull()
    expect(store.failedLocale).toBe('it')
    // Kept: a transient failure retries from storage on the next boot.
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('it')
  })

  it('restores a stored locale atomically during startup when its catalog fails', async () => {
    window.localStorage.setItem(STORAGE_KEY, 'it')
    setActivePinia(createPinia())

    const store = useLocaleStore()
    const pending = store.apply()

    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBe('it')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')

    await pending

    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBeNull()
    expect(store.failedLocale).toBe('it')
    expect(i18n.global.locale.value).toBe('en')
    expect(document.documentElement.getAttribute('lang')).toBe('en')
  })

  it('lets the latest request win when an obsolete catalog resolves first', async () => {
    const store = useLocaleStore()
    const italian = deferred<boolean>()
    const spanish = deferred<boolean>()

    vi.mocked(ensureLocaleMessages).mockImplementation((locale) =>
      locale === 'it' ? italian.promise : spanish.promise,
    )

    const first = store.setLocale('it')
    const second = store.setLocale('es')

    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBe('es')
    expect(i18n.global.locale.value).toBe('en')

    italian.resolve(true)
    await first

    // The obsolete success must not commit Italian or clear Spanish's
    // pending state.
    expect(store.locale).toBe('en')
    expect(store.pendingLocale).toBe('es')
    expect(i18n.global.locale.value).toBe('en')

    spanish.resolve(true)
    await second

    expect(store.locale).toBe('es')
    expect(store.pendingLocale).toBeNull()
    expect(store.failedLocale).toBeNull()
    expect(i18n.global.locale.value).toBe('es')
    expect(document.documentElement.getAttribute('lang')).toBe('es')
  })

  it('does not let an obsolete failure undo a newer successful switch', async () => {
    const store = useLocaleStore()
    const italian = deferred<boolean>()
    const spanish = deferred<boolean>()

    vi.mocked(ensureLocaleMessages).mockImplementation((locale) =>
      locale === 'it' ? italian.promise : spanish.promise,
    )

    const first = store.setLocale('it')
    const second = store.setLocale('es')

    spanish.resolve(true)
    await second
    expect(store.locale).toBe('es')
    expect(store.failedLocale).toBeNull()

    italian.resolve(false)
    await first

    expect(store.locale).toBe('es')
    expect(store.pendingLocale).toBeNull()
    expect(store.failedLocale).toBeNull()
    expect(i18n.global.locale.value).toBe('es')
    expect(document.documentElement.getAttribute('lang')).toBe('es')
  })
})
