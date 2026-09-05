import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import PaperBoardDialogShell from '../../../views/paper/board/PaperBoardDialogShell.vue'

const originalVisualViewport = Object.getOwnPropertyDescriptor(window, 'visualViewport')

function installVisualViewport(height: number, offsetTop: number) {
  const events = new EventTarget()
  let currentHeight = height
  let currentOffsetTop = offsetTop

  Object.defineProperty(window, 'visualViewport', {
    configurable: true,
    value: {
      get height() {
        return currentHeight
      },
      get offsetTop() {
        return currentOffsetTop
      },
      addEventListener: events.addEventListener.bind(events),
      removeEventListener: events.removeEventListener.bind(events),
    },
  })

  return (next: { height: number; offsetTop: number }) => {
    currentHeight = next.height
    currentOffsetTop = next.offsetTop
    events.dispatchEvent(new Event('resize'))
    events.dispatchEvent(new Event('scroll'))
  }
}

function mountShell() {
  return mount(PaperBoardDialogShell, {
    attachTo: document.body,
    props: {
      isOpen: true,
      eyebrow: 'Board',
      title: 'Board settings',
      closeLabel: 'Close settings',
      testid: 'paper-dialog-shell',
    },
    slots: {
      default: '<input data-testid="dialog-input" />',
      footer: '<button data-testid="dialog-save">Save</button>',
    },
  })
}

afterEach(() => {
  document.body.innerHTML = ''
  if (originalVisualViewport) {
    Object.defineProperty(window, 'visualViewport', originalVisualViewport)
  } else {
    Reflect.deleteProperty(window, 'visualViewport')
  }
})

describe('PaperBoardDialogShell', () => {
  it('traps forward and backward Tab movement inside the dialog', async () => {
    const wrapper = mountShell()
    await nextTick()

    const close = wrapper.get('[data-action="close-dialog"]').element as HTMLElement
    const save = wrapper.get('[data-testid="dialog-save"]').element as HTMLElement

    save.focus()
    await wrapper.get('[data-testid="dialog-save"]').trigger('keydown', { key: 'Tab' })
    expect(document.activeElement).toBe(close)

    await wrapper.get('[data-action="close-dialog"]').trigger('keydown', {
      key: 'Tab',
      shiftKey: true,
    })
    expect(document.activeElement).toBe(save)

    wrapper.unmount()
  })

  it('follows the contracted visual viewport while open', async () => {
    const resizeViewport = installVisualViewport(760, 0)
    const wrapper = mountShell()
    const backdrop = wrapper.get('[data-testid="paper-dialog-shell"]').element as HTMLElement

    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-height'),
    ).toBe('760px')

    resizeViewport({ height: 420, offsetTop: 120 })
    await nextTick()

    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-height'),
    ).toBe('420px')
    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-offset-top'),
    ).toBe('120px')

    wrapper.unmount()
  })

  it('writes both viewport custom properties for a browser with no dvh support', () => {
    // The browser class issue #2180 exposed: VisualViewport API present, CSS
    // `dvh` absent. Nothing about `dvh` is visible to this component, so it
    // publishes both properties either way — and that is exactly why the
    // stylesheet has to consume BOTH outside its `@supports (height: 100dvh)`
    // guard. The shipped bug was the asymmetry: `top` took the offset
    // unconditionally while `height` was upgraded only inside the guard, so
    // this class of browser was pushed down by `offsetTop` while staying a full
    // layout viewport tall and overflowed the screen by exactly that offset.
    //
    // The CSS half of the invariant lives in
    // `src/tests/config/DialogVisualViewportCss.spec.ts`: the DOM environment
    // vitest runs (happy-dom) evaluates neither feature queries nor viewport
    // units, so no mounted spec can distinguish guarded from unguarded.
    installVisualViewport(420, 120)
    const wrapper = mountShell()
    const backdrop = wrapper.get('[data-testid="paper-dialog-shell"]').element as HTMLElement

    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-height'),
    ).toBe('420px')
    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-offset-top'),
    ).toBe('120px')

    wrapper.unmount()
  })

  it('keeps the dynamic-viewport CSS fallback when VisualViewport is unavailable', () => {
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
    const wrapper = mountShell()
    const backdrop = wrapper.get('[data-testid="paper-dialog-shell"]').element as HTMLElement

    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-height'),
    ).toBe('')
    expect(
      backdrop.style.getPropertyValue('--paper-board-dialog-visual-viewport-offset-top'),
    ).toBe('')

    wrapper.unmount()
  })
})
