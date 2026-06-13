import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import KeyboardShortcutsHelp from '../../components/KeyboardShortcutsHelp.vue'

vi.mock('../../composables/useEscapeToClose', () => ({
  useEscapeToClose: vi.fn(),
}))

function mountHelp(isOpen: boolean) {
  return mount(KeyboardShortcutsHelp, {
    props: { isOpen },
    attachTo: document.body,
  })
}

function bodyText() {
  return document.body.textContent ?? ''
}

describe('KeyboardShortcutsHelp', () => {
  it('renders nothing when isOpen is false', () => {
    const wrapper = mountHelp(false)
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
    wrapper.unmount()
  })

  it('renders dialog when isOpen is true', () => {
    const wrapper = mountHelp(true)
    const dialog = document.body.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog).not.toBeNull()
    expect(dialog.getAttribute('aria-label')).toBe('Keyboard Shortcuts')
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    wrapper.unmount()
  })

  it('displays all four shortcut categories', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Navigation')
    expect(text).toContain('Card Movement')
    expect(text).toContain('Actions')
    expect(text).toContain('General')
    wrapper.unmount()
  })

  it('displays navigation shortcuts', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Select next card')
    expect(text).toContain('Select previous card')
    expect(text).toContain('Move to previous column')
    expect(text).toContain('Move to next column')
    wrapper.unmount()
  })

  it('displays card movement shortcuts', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Alt + ArrowRight')
    expect(text).toContain('Move card to next column')
    expect(text).toContain('Alt + ArrowUp')
    expect(text).toContain('Move card up in column')
    wrapper.unmount()
  })

  it('displays action shortcuts with Enter and n keys', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Open selected card')
    expect(text).toContain('Create new card in current column')
    wrapper.unmount()
  })

  it('displays general shortcuts including ? key', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Toggle this help dialog')
    expect(text).toContain('Toggle filter panel')
    wrapper.unmount()
  })

  it('emits close when close button is clicked', async () => {
    const wrapper = mountHelp(true)
    const closeBtn = document.body.querySelector('button[aria-label="Close"]') as HTMLElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('emits close when "Got it!" button is clicked', async () => {
    const wrapper = mountHelp(true)
    const buttons = document.body.querySelectorAll('button')
    const gotItBtn = Array.from(buttons).find((b) => b.textContent?.includes('Got it!'))
    expect(gotItBtn).toBeTruthy()
    gotItBtn?.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('shows footer hint about the ? key', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('anytime to show or hide this help')
    wrapper.unmount()
  })

  it('uses design-token surfaces, not hardcoded light-theme classes', () => {
    // Regression guard for ADR-0038 paper-night activation (#1135): this legacy
    // overlay must survive the dark/paper canonical theme. Every surface, text,
    // and accent color must come from the --td-*/Material token utilities, never
    // from raw Tailwind light-theme classes that bake in a white background.
    const wrapper = mountHelp(true)
    const panel = document.body.querySelector('.kbd-help-panel') as HTMLElement
    expect(panel).not.toBeNull()
    expect(panel.className).toContain('bg-surface-container')

    const markup = document.body.querySelector('[role="dialog"]')?.outerHTML ?? ''
    expect(markup).not.toMatch(/\bbg-white\b/)
    expect(markup).not.toMatch(/\btext-gray-\d/)
    expect(markup).not.toMatch(/\bbg-gray-\d/)
    expect(markup).not.toMatch(/\bborder-gray-\d/)
    expect(markup).not.toMatch(/\b(?:bg|text|border)-blue-\d/)
    wrapper.unmount()
  })
})
