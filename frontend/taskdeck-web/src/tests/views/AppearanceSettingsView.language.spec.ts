import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import AppearanceSettingsView from '../../views/AppearanceSettingsView.vue'
import { useLocaleStore } from '../../store/localeStore'
import { i18n, DEFAULT_LOCALE } from '../../i18n'

/**
 * Language switcher (#1770 / ADR-0054 §7).
 *
 * Covers the three things the acceptance criteria actually claim: the language
 * is selectable, the choice persists through the existing preferences
 * mechanism, and switching applies LIVE — this page re-renders in the new
 * language with no reload.
 */

const STORAGE_KEY = 'td.locale.v1'

function localeButton(wrapper: ReturnType<typeof mount>, locale: string) {
  return wrapper.find(`[data-locale="${locale}"]`)
}

describe('AppearanceSettingsView — language', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
    i18n.global.locale.value = DEFAULT_LOCALE
  })

  afterEach(() => {
    i18n.global.locale.value = DEFAULT_LOCALE
    document.documentElement.removeAttribute('lang')
  })

  it('offers every supported language, labelled by its own endonym', () => {
    const wrapper = mount(AppearanceSettingsView)
    const buttons = wrapper.findAll('[data-locale]')

    expect(buttons.map((b) => b.attributes('data-locale'))).toEqual(['en', 'it', 'es'])
    // Endonyms, not translations: a Spanish speaker looks for "Español".
    const names = wrapper.findAll('.paper-appearance__segment-name')
    expect(names.map((n) => n.text())).toEqual(['English', 'Italiano', 'Español'])
    // Each endonym is marked with its own language so a screen reader switches
    // voice for it rather than reading "Español" with an English pronunciation.
    // (The `lang` sits on the endonym span, not the button — the MT note below
    // it is in the active locale.)
    expect(names.map((n) => n.attributes('lang'))).toEqual(['en', 'it', 'es'])
  })

  it('discloses unreviewed machine translation on it/es, not on en (#1770)', () => {
    const wrapper = mount(AppearanceSettingsView)

    expect(localeButton(wrapper, 'en').find('[data-testid="mt-badge"]').exists()).toBe(false)
    expect(localeButton(wrapper, 'it').find('[data-testid="mt-badge"]').text()).toBe(
      'Machine-translated',
    )
    expect(localeButton(wrapper, 'es').find('[data-testid="mt-badge"]').text()).toBe(
      'Machine-translated',
    )
  })

  it('defaults to English with no stored preference', () => {
    const wrapper = mount(AppearanceSettingsView)
    expect(localeButton(wrapper, 'en').attributes('aria-pressed')).toBe('true')
    expect(localeButton(wrapper, 'it').attributes('aria-pressed')).toBe('false')
    expect(useLocaleStore().locale).toBe('en')
  })

  it('shows a pending status without moving the committed selection', () => {
    const store = useLocaleStore()
    store.pendingLocale = 'it'

    const wrapper = mount(AppearanceSettingsView)
    const group = wrapper.find('[data-testid="appearance-language"] [role="group"]')
    const status = wrapper.find('[data-testid="appearance-language-status"]')

    expect(group.attributes('aria-busy')).toBe('true')
    expect(localeButton(wrapper, 'en').attributes('aria-pressed')).toBe('true')
    expect(localeButton(wrapper, 'it').attributes('aria-pressed')).toBe('false')
    expect(status.attributes('role')).toBe('status')
    expect(status.text()).toBe('Loading Italiano…')
    expect(wrapper.findAll('[data-locale]:disabled')).toHaveLength(0)
  })

  it('names a failed target and the language that remains active', () => {
    const store = useLocaleStore()
    store.failedLocale = 'it'

    const wrapper = mount(AppearanceSettingsView)
    const status = wrapper.find('[data-testid="appearance-language-status"]')

    expect(status.attributes('role')).toBe('alert')
    expect(status.text()).toBe("Couldn’t load Italiano. English remains active. Try again.")
    expect(localeButton(wrapper, 'en').attributes('aria-pressed')).toBe('true')
    expect(localeButton(wrapper, 'it').attributes('aria-pressed')).toBe('false')
  })

  it('selecting a language persists it through the preferences mechanism', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = useLocaleStore()

    await localeButton(wrapper, 'it').trigger('click')
    await flushPromises()

    expect(store.locale).toBe('it')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('it')
    expect(localeButton(wrapper, 'it').attributes('aria-pressed')).toBe('true')
    expect(localeButton(wrapper, 'en').attributes('aria-pressed')).toBe('false')
  })

  it('applies the new language live, without a reload', async () => {
    const wrapper = mount(AppearanceSettingsView)

    expect(wrapper.text()).toContain('Appearance')
    expect(wrapper.text()).toContain('Language')

    await localeButton(wrapper, 'it').trigger('click')
    await flushPromises()

    // The page it lives on is itself re-rendered in Italian.
    expect(wrapper.text()).toContain('Aspetto')
    expect(wrapper.text()).toContain('Lingua')
    expect(wrapper.text()).not.toContain('Appearance')
    // Theme option labels come from a computed over the catalog, so they move too.
    expect(wrapper.find('[data-mode="paper"]').text()).toBe('Paper (chiaro)')

    await localeButton(wrapper, 'es').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('Apariencia')
    expect(wrapper.text()).toContain('Idioma')
  })

  it('sets <html lang> so assistive tech and the browser agree with the UI', async () => {
    const wrapper = mount(AppearanceSettingsView)

    await localeButton(wrapper, 'es').trigger('click')
    await flushPromises()
    expect(document.documentElement.getAttribute('lang')).toBe('es')

    await localeButton(wrapper, 'en').trigger('click')
    await flushPromises()
    expect(document.documentElement.getAttribute('lang')).toBe('en')
  })

  it('restores a persisted language on the next visit', async () => {
    window.localStorage.setItem(STORAGE_KEY, 'es')
    setActivePinia(createPinia())

    const store = useLocaleStore()
    await store.apply()

    expect(store.locale).toBe('es')
    expect(mount(AppearanceSettingsView).text()).toContain('Apariencia')
  })

  it('ignores a garbage stored value instead of running with no catalog', () => {
    window.localStorage.setItem(STORAGE_KEY, 'klingon')
    setActivePinia(createPinia())

    expect(useLocaleStore().locale).toBe('en')
  })

  it('rejects an unsupported locale passed to setLocale', () => {
    const store = useLocaleStore()

    // @ts-expect-error — deliberately exercising the runtime guard
    store.setLocale('de')

    expect(store.locale).toBe('en')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBeNull()
  })
})
