import { describe, it, expect, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { useVisualViewport, type UseVisualViewportOptions } from '../../composables/useVisualViewport'

type SyntheticVisualViewport = {
  /** Mutate the viewport and dispatch exactly the named events, so a test can
   * prove which listener carried the update. */
  set: (next: { height: number; offsetTop: number }, events: Array<'resize' | 'scroll'>) => void
  listeners: () => number
}

const originalDescriptor = Object.getOwnPropertyDescriptor(window, 'visualViewport')

function restoreVisualViewport() {
  if (originalDescriptor) {
    Object.defineProperty(window, 'visualViewport', originalDescriptor)
  } else {
    Reflect.deleteProperty(window, 'visualViewport')
  }
}

/**
 * jsdom has no VisualViewport API, so install one whose height/offsetTop can be
 * driven from the test and whose listener count is observable.
 */
function installSyntheticVisualViewport(height: number, offsetTop: number): SyntheticVisualViewport {
  const events = new EventTarget()
  let currentHeight = height
  let currentOffsetTop = offsetTop
  let listenerCount = 0

  const visualViewport = {
    get height() {
      return currentHeight
    },
    get offsetTop() {
      return currentOffsetTop
    },
    addEventListener(type: string, handler: EventListener) {
      listenerCount += 1
      events.addEventListener(type, handler)
    },
    removeEventListener(type: string, handler: EventListener) {
      listenerCount -= 1
      events.removeEventListener(type, handler)
    },
  }

  Object.defineProperty(window, 'visualViewport', {
    configurable: true,
    value: visualViewport,
  })

  return {
    set(next, dispatched) {
      currentHeight = next.height
      currentOffsetTop = next.offsetTop
      for (const type of dispatched) {
        events.dispatchEvent(new Event(type))
      }
    },
    listeners: () => listenerCount,
  }
}

function mountHost(options: UseVisualViewportOptions) {
  const Host = defineComponent({
    setup() {
      const viewport = useVisualViewport(options)
      return () => h('div', { style: viewport.style.value, 'data-testid': 'host' })
    },
  })

  return mount(Host, { attachTo: document.body })
}

function hostStyle(wrapper: ReturnType<typeof mountHost>) {
  return (wrapper.get('[data-testid="host"]').element as HTMLElement).style
}

describe('useVisualViewport', () => {
  afterEach(() => {
    restoreVisualViewport()
  })

  it('emits the visual viewport height and offset as prefixed custom properties', () => {
    installSyntheticVisualViewport(420, 120)

    const wrapper = mountHost({ prefix: '--td-dialog' })
    const style = hostStyle(wrapper)

    expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('420px')
    expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('120px')

    wrapper.unmount()
  })

  it('tracks a contraction announced by the resize event alone', async () => {
    const synthetic = installSyntheticVisualViewport(800, 0)

    const wrapper = mountHost({ prefix: '--td-dialog' })
    expect(hostStyle(wrapper).getPropertyValue('--td-dialog-visual-viewport-height')).toBe('800px')

    synthetic.set({ height: 420, offsetTop: 120 }, ['resize'])
    await wrapper.vm.$nextTick()

    const style = hostStyle(wrapper)
    expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('420px')
    expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('120px')

    wrapper.unmount()
  })

  it('tracks an offset change announced by the scroll event alone', async () => {
    const synthetic = installSyntheticVisualViewport(420, 0)

    const wrapper = mountHost({ prefix: '--td-dialog' })
    expect(hostStyle(wrapper).getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('0px')

    synthetic.set({ height: 420, offsetTop: 200 }, ['scroll'])
    await wrapper.vm.$nextTick()

    expect(hostStyle(wrapper).getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe(
      '200px',
    )

    wrapper.unmount()
  })

  it('falls back to the layout viewport under the default "layout" fallback', () => {
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })

    const wrapper = mountHost({ prefix: '--card-modal' })
    const style = hostStyle(wrapper)

    expect(style.getPropertyValue('--card-modal-visual-viewport-height')).toBe(
      `${window.innerHeight}px`,
    )
    expect(style.getPropertyValue('--card-modal-visual-viewport-offset-top')).toBe('0px')

    wrapper.unmount()
  })

  it('emits no custom properties under the "unset" fallback so CSS keeps its 100dvh default', () => {
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })

    const wrapper = mountHost({ prefix: '--td-dialog', fallback: 'unset' })
    const style = hostStyle(wrapper)

    expect(style.getPropertyValue('--td-dialog-visual-viewport-height')).toBe('')
    expect(style.getPropertyValue('--td-dialog-visual-viewport-offset-top')).toBe('')

    wrapper.unmount()
  })

  it('registers two listeners on mount and removes them on unmount', () => {
    const synthetic = installSyntheticVisualViewport(800, 0)

    const wrapper = mountHost({ prefix: '--td-dialog' })
    expect(synthetic.listeners()).toBe(2)

    wrapper.unmount()
    expect(synthetic.listeners()).toBe(0)
  })

  it('reports whether the VisualViewport API is being observed', () => {
    installSyntheticVisualViewport(420, 120)

    const supportedStates: boolean[] = []
    const Host = defineComponent({
      setup() {
        const { supported } = useVisualViewport({ prefix: '--td-dialog' })
        supportedStates.push(supported.value)
        return () => h('div')
      },
    })

    const wrapper = mount(Host)
    expect(supportedStates).toEqual([true])
    wrapper.unmount()

    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
    const unsupported = mount(Host)
    expect(supportedStates).toEqual([true, false])
    unsupported.unmount()
  })
})
