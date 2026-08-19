import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import AppearanceSettingsView from '../../views/AppearanceSettingsView.vue'
import appearanceSource from '../../views/AppearanceSettingsView.vue?raw'
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
    const labels = wrapper.findAll('.paper-appearance__segment').map((b) => b.text())
    expect(labels).toHaveLength(4)
    expect(labels.some((l) => l.includes('Off (Legacy / Obsidian)'))).toBe(true)
    expect(labels.some((l) => l.includes('Paper (Light)'))).toBe(true)
    expect(labels.some((l) => l.includes('Paper Night (Dark)'))).toBe(true)
    expect(labels.some((l) => l.includes('Auto (match system)'))).toBe(true)
  })

  it('renders with the Paper theme class hooks (not the legacy Obsidian ones)', () => {
    const wrapper = mount(AppearanceSettingsView)

    // #1779 ruling: the theme-control page wears the same Paper chrome as every
    // other settings surface. No legacy `td-appearance-settings`/`td-theme-*`
    // hooks should survive.
    expect(wrapper.find('.paper-appearance').exists()).toBe(true)
    expect(wrapper.find('.paper-appearance__panel').exists()).toBe(true)
    expect(wrapper.find('[class*="td-appearance-settings"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-theme-"]').exists()).toBe(false)
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

// ── #1808 review (MEDIUM): Legacy ("off") mode substrate guard ──
// Paper tokens exist only under `.paper` / `.paper-night` (paper-tokens.css), so
// in Legacy mode this view's `color: var(--ink, …)` resolves to the near-black
// literal while AppShell's `.td-content` still paints `--td-surface-base`
// (#131313) — ~1.05:1 on the hero. This is the sharpest case in the wave: the
// user selects "Off (Legacy / Obsidian)" on THIS page, so its own <h1> is the
// first thing that would disappear. A root that sets the Paper ink MUST also
// paint the Paper substrate; that is a no-op under `.paper`/`.paper-night`.
// Source is read through Vite's `?raw` rather than `node:fs` because
// `tsconfig.vitest.json` deliberately omits the "node" types.
// #1815 tracks unifying these per-view assertions into one wave-wide spec.
describe('AppearanceSettingsView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = appearanceSource.match(/^\.paper-appearance \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-appearance root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })
})
