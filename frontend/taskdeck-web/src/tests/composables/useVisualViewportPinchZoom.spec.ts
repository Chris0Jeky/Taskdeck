import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import {
  useVisualViewport,
  type UseVisualViewportOptions,
} from '../../composables/useVisualViewport'

const originalVisualViewport = Object.getOwnPropertyDescriptor(window, 'visualViewport')
const originalInnerHeight = Object.getOwnPropertyDescriptor(window, 'innerHeight')

function restoreWindowProperty(
  key: 'visualViewport' | 'innerHeight',
  descriptor: PropertyDescriptor | undefined,
) {
  if (descriptor) Object.defineProperty(window, key, descriptor)
  else Reflect.deleteProperty(window, key)
}

function installSyntheticVisualViewport(initial: {
  height: number
  offsetTop: number
  scale: number
}) {
  const events = new EventTarget()
  let geometry = initial

  const viewport = {
    get height() {
      return geometry.height
    },
    get offsetTop() {
      return geometry.offsetTop
    },
    get scale() {
      return geometry.scale
    },
    addEventListener(type: string, listener: EventListener) {
      events.addEventListener(type, listener)
    },
    removeEventListener(type: string, listener: EventListener) {
      events.removeEventListener(type, listener)
    },
  }

  Object.defineProperty(window, 'visualViewport', {
    configurable: true,
    value: viewport,
  })

  return {
    set(next: typeof initial) {
      geometry = next
      events.dispatchEvent(new Event('resize'))
    },
  }
}

function mountHost(options: UseVisualViewportOptions) {
  const Host = defineComponent({
    setup() {
      const viewport = useVisualViewport(options)
      return () => h('div', {
        style: viewport.style.value,
        'data-testid': 'host',
        'data-supported': String(viewport.supported.value),
      })
    },
  })

  return mount(Host, { attachTo: document.body })
}

function property(wrapper: ReturnType<typeof mountHost>, name: string) {
  return (wrapper.get('[data-testid="host"]').element as HTMLElement).style.getPropertyValue(name)
}

describe('useVisualViewport pinch zoom policy', () => {
  afterEach(() => {
    restoreWindowProperty('visualViewport', originalVisualViewport)
    restoreWindowProperty('innerHeight', originalInnerHeight)
    document.body.innerHTML = ''
  })

  it('uses layout geometry while scale is above one, then resumes keyboard geometry at scale one', async () => {
    Object.defineProperty(window, 'innerHeight', {
      configurable: true,
      writable: true,
      value: 900,
    })
    const synthetic = installSyntheticVisualViewport({ height: 500, offsetTop: 80, scale: 1 })
    const wrapper = mountHost({ prefix: '--card-modal' })

    expect(property(wrapper, '--card-modal-visual-viewport-height')).toBe('500px')
    expect(property(wrapper, '--card-modal-visual-viewport-offset-top')).toBe('80px')

    synthetic.set({ height: 260, offsetTop: 310, scale: 2 })
    await nextTick()

    expect(property(wrapper, '--card-modal-visual-viewport-height')).toBe('900px')
    expect(property(wrapper, '--card-modal-visual-viewport-offset-top')).toBe('0px')
    expect(wrapper.get('[data-testid="host"]').attributes('data-supported')).toBe('false')

    synthetic.set({ height: 430, offsetTop: 120, scale: 1 })
    await nextTick()

    expect(property(wrapper, '--card-modal-visual-viewport-height')).toBe('430px')
    expect(property(wrapper, '--card-modal-visual-viewport-offset-top')).toBe('120px')
    expect(wrapper.get('[data-testid="host"]').attributes('data-supported')).toBe('true')
    wrapper.unmount()
  })

  it.each([0.75, 0])(
    'uses layout fallback instead of zoomed or inactive geometry at scale %s',
    async (scale) => {
      Object.defineProperty(window, 'innerHeight', {
        configurable: true,
        writable: true,
        value: 900,
      })
      const synthetic = installSyntheticVisualViewport({ height: 500, offsetTop: 80, scale: 1 })
      const wrapper = mountHost({ prefix: '--card-modal' })

      synthetic.set({ height: 260, offsetTop: 310, scale })
      await nextTick()

      expect(property(wrapper, '--card-modal-visual-viewport-height')).toBe('900px')
      expect(property(wrapper, '--card-modal-visual-viewport-offset-top')).toBe('0px')
      expect(wrapper.get('[data-testid="host"]').attributes('data-supported')).toBe('false')
      wrapper.unmount()
    },
  )

  it('restores the CSS fallback for unset callers during pinch zoom', async () => {
    const synthetic = installSyntheticVisualViewport({ height: 500, offsetTop: 80, scale: 1 })
    const wrapper = mountHost({ prefix: '--td-dialog', fallback: 'unset' })

    expect(property(wrapper, '--td-dialog-visual-viewport-height')).toBe('500px')

    synthetic.set({ height: 260, offsetTop: 310, scale: 2 })
    await nextTick()

    expect(property(wrapper, '--td-dialog-visual-viewport-height')).toBe('')
    expect(property(wrapper, '--td-dialog-visual-viewport-offset-top')).toBe('')
    wrapper.unmount()
  })
})
