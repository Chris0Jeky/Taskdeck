import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import AppearanceSettingsView from '../../views/AppearanceSettingsView.vue'
import { usePaperThemeStore } from '../../store/paperThemeStore'

const STORAGE_KEY = 'td.paper.mode'

function segmentByLabel(wrapper: ReturnType<typeof mount>, label: string) {
  return wrapper
    .findAll('.td-theme-segment')
    .find((b) => b.text().includes(label))
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

  it('reflects the current mode via aria-pressed (default off)', () => {
    const wrapper = mount(AppearanceSettingsView)
    expect(segmentByLabel(wrapper, 'Off (Legacy / Obsidian)')?.attributes('aria-pressed')).toBe('true')
    expect(segmentByLabel(wrapper, 'Paper (Light)')?.attributes('aria-pressed')).toBe('false')
    expect(segmentByLabel(wrapper, 'Auto (match system)')?.attributes('aria-pressed')).toBe('false')
  })

  it('selecting a mode calls setMode and persists to localStorage', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByLabel(wrapper, 'Paper Night (Dark)')?.trigger('click')

    expect(store.mode).toBe('paper-night')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('paper-night')
    expect(document.body.classList.contains('paper-night')).toBe(true)
    expect(segmentByLabel(wrapper, 'Paper Night (Dark)')?.attributes('aria-pressed')).toBe('true')
  })

  it('Auto stays Auto (does not collapse to the resolved class)', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByLabel(wrapper, 'Auto (match system)')?.trigger('click')

    expect(store.mode).toBe('auto')
    expect(window.localStorage.getItem(STORAGE_KEY)).toBe('auto')
    expect(segmentByLabel(wrapper, 'Auto (match system)')?.attributes('aria-pressed')).toBe('true')
  })

  it('Off restores Legacy (no paper body class, store off)', async () => {
    const wrapper = mount(AppearanceSettingsView)
    const store = usePaperThemeStore()

    await segmentByLabel(wrapper, 'Paper (Light)')?.trigger('click')
    expect(store.isOn).toBe(true)

    await segmentByLabel(wrapper, 'Off (Legacy / Obsidian)')?.trigger('click')

    expect(store.mode).toBe('off')
    expect(store.isOn).toBe(false)
    expect(document.body.classList.contains('paper')).toBe(false)
    expect(document.body.classList.contains('paper-night')).toBe(false)
  })
})
