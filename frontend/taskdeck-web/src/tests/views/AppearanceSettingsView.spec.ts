import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import AppearanceSettingsView from '../../views/AppearanceSettingsView.vue'
import { usePaperThemeStore } from '../../store/paperThemeStore'

const STORAGE_KEY = 'td.paper.mode.v2'

// Select by the stable data-mode hook rather than label text, so the tests are
// resilient to label wording changes.
function segmentByMode(wrapper: ReturnType<typeof mount>, mode: string) {
  return wrapper.find(`[data-mode="${mode}"]`)
}

describe('AppearanceSettingsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.clear()
    document.body.classList.remove('paper', 'paper-night')
  })

  afterEach(() => {
    document.body.classList.remove('paper', 'paper-night')
  })

  it('renders all four theme options', () => {
    const wrapper = mount(AppearanceSettingsView)
    const labels = wrapper.findAll('.td-theme-segment').map((b) => b.text())
    expect(labels).toHaveLength(4)
    expect(labels.some((l) => l.includes('Off (Legacy / Obsidian)'))).toBe(true)
    expect(labels.some((l) => l.includes('Paper (Light)'))).toBe(true)
    expect(labels.some((l) => l.includes('Paper Night (Dark)'))).toBe(true)
    expect(labels.some((l) => l.includes('Auto (match system)'))).toBe(true)
  })

  it('reflects the current mode via aria-pressed (default paper — ADR-0038)', () => {
    const wrapper = mount(AppearanceSettingsView)
    expect(segmentByMode(wrapper, 'paper').attributes('aria-pressed')).toBe('true')
    expect(segmentByMode(wrapper, 'off').attributes('aria-pressed')).toBe('false')
    expect(segmentByMode(wrapper, 'auto').attributes('aria-pressed')).toBe('false')
  })

  it('selecting a mode calls setMode and persists to localStorage', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByMode(wrapper, 'paper-night').trigger('click')

    expect(store.mode).toBe('paper-night')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('paper-night')
    expect(document.body.classList.contains('paper-night')).toBe(true)
    expect(segmentByMode(wrapper, 'paper-night').attributes('aria-pressed')).toBe('true')
  })

  it('Auto stays Auto (does not collapse to the resolved class)', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByMode(wrapper, 'auto').trigger('click')

    expect(store.mode).toBe('auto')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
    expect(segmentByMode(wrapper, 'auto').attributes('aria-pressed')).toBe('true')
  })

  it('Off restores Legacy (no paper body class, store off)', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByMode(wrapper, 'paper').trigger('click')
    expect(store.isOn).toBe(true)

    await segmentByMode(wrapper, 'off').trigger('click')

    expect(store.mode).toBe('off')
    expect(store.isOn).toBe(false)
    expect(document.body.classList.contains('paper')).toBe(false)
    expect(document.body.classList.contains('paper-night')).toBe(false)
  })
})
