import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ShellKeyboardHelp from '../../components/shell/ShellKeyboardHelp.vue'

vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn(() => vi.fn()),
}))

function mountHelp(visible: boolean) {
  return mount(ShellKeyboardHelp, {
    props: { visible },
    attachTo: document.body,
  })
}

function bodyText() {
  return document.body.textContent ?? ''
}

describe('ShellKeyboardHelp', () => {
  it('renders nothing when visible is false', () => {
    const wrapper = mountHelp(false)
    expect(document.body.querySelector('.td-keyboard-help')).toBeNull()
    wrapper.unmount()
  })

  it('renders keyboard shortcuts dialog when visible is true', () => {
    const wrapper = mountHelp(true)
    expect(document.body.querySelector('.td-keyboard-help')).not.toBeNull()
    expect(bodyText()).toContain('Keyboard Shortcuts')
    wrapper.unmount()
  })

  it('displays all shortcut sections (Global, Board Navigation, Editor)', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Global')
    expect(text).toContain('Board Navigation')
    expect(text).toContain('Editor')
    wrapper.unmount()
  })

  it('displays key global shortcuts', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('Ctrl+K')
    expect(text).toContain('Command palette')
    expect(text).toContain('Ctrl+Shift+C')
    expect(text).toContain('Quick capture modal')
    expect(text).toContain('Escape')
    expect(text).toContain('Close top surface')
    wrapper.unmount()
  })

  it('displays board navigation shortcuts', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    expect(text).toContain('h / Left')
    expect(text).toContain('Previous column')
    expect(text).toContain('j / Down')
    expect(text).toContain('Next card')
    expect(text).toContain('Enter')
    expect(text).toContain('Open card')
    wrapper.unmount()
  })

  it('emits close when close button is clicked', async () => {
    const wrapper = mountHelp(true)
    const closeBtn = document.body.querySelector('.td-keyboard-help__header button') as HTMLElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('has dialog role and aria-label for accessibility', () => {
    const wrapper = mountHelp(true)
    const overlay = document.body.querySelector('.td-overlay') as HTMLElement
    expect(overlay).not.toBeNull()
    expect(overlay.getAttribute('role')).toBe('dialog')
    expect(overlay.getAttribute('aria-label')).toBe('Keyboard shortcuts')
    expect(overlay.getAttribute('aria-modal')).toBe('true')
    wrapper.unmount()
  })
})
