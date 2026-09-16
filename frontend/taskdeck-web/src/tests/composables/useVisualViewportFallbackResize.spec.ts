import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useVisualViewport } from '../../composables/useVisualViewport'

const originalVisualViewport = Object.getOwnPropertyDescriptor(window, 'visualViewport')
const originalInnerHeight = Object.getOwnPropertyDescriptor(window, 'innerHeight')

function restoreWindowProperty(
  key: 'visualViewport' | 'innerHeight',
  descriptor: PropertyDescriptor | undefined,
) {
  if (descriptor) Object.defineProperty(window, key, descriptor)
  else Reflect.deleteProperty(window, key)
}

function setInnerHeight(value: number) {
  Object.defineProperty(window, 'innerHeight', {
    configurable: true,
    writable: true,
    value,
  })
}

function mountHost() {
  const Host = defineComponent({
    setup() {
      const viewport = useVisualViewport({ prefix: '--card-modal' })
      return () => h('div', { style: viewport.style.value, 'data-testid': 'host' })
    },
  })

  return mount(Host, { attachTo: document.body })
}

describe('useVisualViewport layout fallback resize', () => {
  afterEach(() => {
    restoreWindowProperty('visualViewport', originalVisualViewport)
    restoreWindowProperty('innerHeight', originalInnerHeight)
    vi.restoreAllMocks()
  })

  it('tracks window resize without VisualViewport and removes the fallback listener on unmount', async () => {
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
    setInnerHeight(844)
    const addListener = vi.spyOn(window, 'addEventListener')
    const removeListener = vi.spyOn(window, 'removeEventListener')

    const wrapper = mountHost()
    const host = wrapper.get('[data-testid="host"]').element as HTMLElement
    expect(host.style.getPropertyValue('--card-modal-visual-viewport-height')).toBe('844px')

    const resizeRegistration = addListener.mock.calls.find(([type]) => type === 'resize')
    expect(resizeRegistration).toBeDefined()

    setInnerHeight(390)
    window.dispatchEvent(new Event('resize'))
    await nextTick()

    expect(host.style.getPropertyValue('--card-modal-visual-viewport-height')).toBe('390px')
    expect(host.style.getPropertyValue('--card-modal-visual-viewport-offset-top')).toBe('0px')

    const resizeHandler = resizeRegistration?.[1]
    wrapper.unmount()
    expect(removeListener).toHaveBeenCalledWith('resize', resizeHandler)
  })
})
