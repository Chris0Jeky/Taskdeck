import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { useKeyboardShortcuts, type ShortcutConfig } from '../../composables/useKeyboardShortcuts'

function mountWithShortcuts(shortcuts: ShortcutConfig[]) {
  const TestComponent = defineComponent({
    setup() {
      useKeyboardShortcuts(shortcuts)
      return {}
    },
    template: '<div></div>',
  })

  return mount(TestComponent, { attachTo: document.body })
}

describe('useKeyboardShortcuts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should trigger callback when shortcut key is pressed', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'j', description: 'Next card', action },
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j' }))

    expect(action).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('should be case insensitive for key matching', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'n', description: 'New card', action },
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'N' }))

    expect(action).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('should respect ctrl modifier key', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'n', ctrl: true, description: 'New card', action },
    ])

    // Without ctrl — should not trigger
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', ctrlKey: false }))
    expect(action).not.toHaveBeenCalled()

    // With ctrl — should trigger
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', ctrlKey: true }))
    expect(action).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  it('should respect shift modifier key', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: '?', shift: true, description: 'Show help', action },
    ])

    // Without shift — should not trigger
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?', shiftKey: false }))
    expect(action).not.toHaveBeenCalled()

    // With shift — should trigger
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?', shiftKey: true }))
    expect(action).toHaveBeenCalledTimes(1)

    wrapper.unmount()
  })

  it('should ignore keystrokes when target is an INPUT element', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'j', description: 'Next card', action },
    ])

    const input = document.createElement('input')
    document.body.appendChild(input)

    const event = new KeyboardEvent('keydown', { key: 'j', bubbles: true })
    Object.defineProperty(event, 'target', { value: input })
    window.dispatchEvent(event)

    expect(action).not.toHaveBeenCalled()

    document.body.removeChild(input)
    wrapper.unmount()
  })

  it('should ignore keystrokes when target is a TEXTAREA element', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'k', description: 'Previous card', action },
    ])

    const textarea = document.createElement('textarea')
    document.body.appendChild(textarea)

    const event = new KeyboardEvent('keydown', { key: 'k', bubbles: true })
    Object.defineProperty(event, 'target', { value: textarea })
    window.dispatchEvent(event)

    expect(action).not.toHaveBeenCalled()

    document.body.removeChild(textarea)
    wrapper.unmount()
  })

  it('should allow Escape key even when typing in an input', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'Escape', description: 'Close', action },
    ])

    const input = document.createElement('input')
    document.body.appendChild(input)

    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })
    Object.defineProperty(event, 'target', { value: input })
    window.dispatchEvent(event)

    expect(action).toHaveBeenCalledTimes(1)

    document.body.removeChild(input)
    wrapper.unmount()
  })

  it('should clean up event listener on unmount', () => {
    const action = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'j', description: 'Next card', action },
    ])

    wrapper.unmount()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j' }))

    expect(action).not.toHaveBeenCalled()
  })

  it('should only trigger the first matching shortcut', () => {
    const action1 = vi.fn()
    const action2 = vi.fn()
    const wrapper = mountWithShortcuts([
      { key: 'j', description: 'First', action: action1 },
      { key: 'j', description: 'Second', action: action2 },
    ])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j' }))

    expect(action1).toHaveBeenCalledTimes(1)
    expect(action2).not.toHaveBeenCalled()

    wrapper.unmount()
  })
})
