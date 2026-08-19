import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TdDialog from '../../../components/ui/TdDialog.vue'

const escapeHandlers: Array<() => void> = []

vi.mock('../../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn((handler: () => void) => {
    escapeHandlers.push(handler)
    return () => {
      const index = escapeHandlers.indexOf(handler)
      if (index >= 0) {
        escapeHandlers.splice(index, 1)
      }
    }
  }),
}))

describe('TdDialog', () => {
  beforeEach(() => {
    escapeHandlers.splice(0, escapeHandlers.length)
    document.body.innerHTML = ''
  })

  it('renders nothing when not open', () => {
    const wrapper = mount(TdDialog, { props: { open: false } })
    expect(wrapper.find('.td-dialog-backdrop').exists()).toBe(false)
  })

  it('renders dialog when open', () => {
    const wrapper = mount(TdDialog, {
      props: { open: true, title: 'Test Dialog' },
      attachTo: document.body,
    })
    const dialog = document.querySelector('.td-dialog')
    expect(dialog).not.toBeNull()
    expect(dialog?.getAttribute('role')).toBe('dialog')
    expect(dialog?.getAttribute('aria-modal')).toBe('true')
    wrapper.unmount()
  })

  it('renders title', () => {
    const wrapper = mount(TdDialog, {
      props: { open: true, title: 'Confirm' },
      attachTo: document.body,
    })
    expect(document.querySelector('.td-dialog__title')?.textContent).toBe('Confirm')
    wrapper.unmount()
  })

  it('renders description', () => {
    const wrapper = mount(TdDialog, {
      props: { open: true, description: 'Are you sure?' },
      attachTo: document.body,
    })
    expect(document.querySelector('.td-dialog__description')?.textContent).toBe('Are you sure?')
    wrapper.unmount()
  })

  it('renders body slot', () => {
    const wrapper = mount(TdDialog, {
      props: { open: true },
      slots: { default: '<p class="test-body">Body content</p>' },
      attachTo: document.body,
    })
    expect(document.querySelector('.test-body')?.textContent).toBe('Body content')
    wrapper.unmount()
  })

  it('renders footer slot', () => {
    const wrapper = mount(TdDialog, {
      props: { open: true },
      slots: { footer: '<button class="test-footer-btn">OK</button>' },
      attachTo: document.body,
    })
    expect(document.querySelector('.test-footer-btn')).not.toBeNull()
    wrapper.unmount()
  })

  it('registers escape handler when opened', async () => {
    const wrapper = mount(TdDialog, {
      props: { open: false },
      attachTo: document.body,
    })
    expect(escapeHandlers.length).toBe(0)
    await wrapper.setProps({ open: true })
    expect(escapeHandlers.length).toBe(1)
    wrapper.unmount()
  })

  it('emits close via escape handler', async () => {
    const wrapper = mount(TdDialog, {
      props: { open: true },
      attachTo: document.body,
    })
    expect(escapeHandlers.length).toBe(1)
    escapeHandlers[0]!()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('emits close on backdrop click when closeOnBackdrop is true', async () => {
    const wrapper = mount(TdDialog, {
      props: { open: true, closeOnBackdrop: true },
      attachTo: document.body,
    })
    const backdrop = document.querySelector('.td-dialog-backdrop') as HTMLElement
    backdrop?.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('does not emit close on backdrop click when closeOnBackdrop is false', async () => {
    const wrapper = mount(TdDialog, {
      props: { open: true, closeOnBackdrop: false },
      attachTo: document.body,
    })
    const backdrop = document.querySelector('.td-dialog-backdrop') as HTMLElement
    backdrop?.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toBeUndefined()
    wrapper.unmount()
  })

  it('unregisters escape handler when closed', async () => {
    const wrapper = mount(TdDialog, {
      props: { open: true },
      attachTo: document.body,
    })
    expect(escapeHandlers.length).toBe(1)
    await wrapper.setProps({ open: false })
    expect(escapeHandlers.length).toBe(0)
    wrapper.unmount()
  })

  describe('visual viewport binding', () => {
    const originalDescriptor = Object.getOwnPropertyDescriptor(window, 'visualViewport')

    function installSyntheticVisualViewport(height: number, offsetTop: number) {
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

    afterEach(() => {
      if (originalDescriptor) {
        Object.defineProperty(window, 'visualViewport', originalDescriptor)
      } else {
        Reflect.deleteProperty(window, 'visualViewport')
      }
    })

    function backdrop() {
      return document.querySelector('.td-dialog-backdrop') as HTMLElement | null
    }

    it('binds the backdrop to the contracted visual viewport', () => {
      installSyntheticVisualViewport(420, 120)

      const wrapper = mount(TdDialog, { props: { open: true }, attachTo: document.body })
      const style = backdrop()!.style

      expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('420px')
      expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('120px')

      wrapper.unmount()
    })

    it('follows the visual viewport as it contracts while the dialog is open', async () => {
      const setVisualViewport = installSyntheticVisualViewport(800, 0)

      const wrapper = mount(TdDialog, { props: { open: true }, attachTo: document.body })
      expect(backdrop()!.style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('800px')

      setVisualViewport({ height: 420, offsetTop: 120 })
      await nextTick()

      const style = backdrop()!.style
      expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('420px')
      expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('120px')

      wrapper.unmount()
    })

    it('sets no viewport custom properties without a VisualViewport API, keeping the 100dvh fallback', () => {
      Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })

      const wrapper = mount(TdDialog, { props: { open: true }, attachTo: document.body })
      const style = backdrop()!.style

      expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('')
      expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('')

      wrapper.unmount()
    })
  })

  it('restores focus to the previously active element when unmounted while open', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()

    const wrapper = mount(TdDialog, {
      props: { open: true },
      attachTo: document.body,
    })

    await nextTick()
    expect(document.activeElement).not.toBe(trigger)

    wrapper.unmount()

    expect(document.activeElement).toBe(trigger)
  })
})
