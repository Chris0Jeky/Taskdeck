import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import PaperShortcutsOverlay from '../../../components/paper/PaperShortcutsOverlay.vue'

function teleportContent(): HTMLElement {
  return document.body
}

describe('PaperShortcutsOverlay', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('renders nothing while visible=false', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    expect(teleportContent().querySelector('[data-paper-shortcuts]')).toBeNull()
  })

  it('renders all 3 groups (Navigate, Capture & Review, Boards) when open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement
    expect(root).not.toBeNull()
    const groups = root.querySelectorAll('.paper-shortcuts-overlay__group')
    expect(groups.length).toBe(3)
    const titles = Array.from(groups).map((g) => g.getAttribute('data-group'))
    expect(titles).toEqual(['Navigate', 'Capture & Review', 'Boards'])
  })

  it('documents the AppShell quick-capture shortcut', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement

    expect(root.textContent).toContain('Ctrl/Cmd+Shift+C')
    expect(root.textContent).toContain('Quick capture')
  })

  it('does not handle ? because AppShell owns the toggle', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }))
    expect(wrapper.emitted()).toEqual({})
  })

  it('emits close on Escape when open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('does not close when ? is pressed while open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }))
    expect(wrapper.emitted('close')).toBeUndefined()
  })

  it('does not react to ? inside a text input', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: '?', bubbles: true }))
    expect(wrapper.emitted()).toEqual({})
  })

  it('emits close when the close button is clicked', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const closeBtn = teleportContent().querySelector('.paper-shortcuts-overlay__close') as HTMLButtonElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
