import { mount } from '@vue/test-utils'
import { defineComponent, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEscapeToClose } from '../../composables/useEscapeToClose'

function pressKey(key: string) {
  window.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
}

function mountWithEscapeToClose(initialOpen = true) {
  const isOpen = ref(initialOpen)
  const onClose = vi.fn()

  const TestComponent = defineComponent({
    setup() {
      useEscapeToClose(() => isOpen.value, onClose)
      return {}
    },
    template: '<div></div>',
  })

  const wrapper = mount(TestComponent, { attachTo: document.body })
  return { wrapper, isOpen, onClose }
}

describe('useEscapeToClose', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls the close callback when Escape is pressed while open', () => {
    const { wrapper, onClose } = mountWithEscapeToClose()

    pressKey('Escape')

    expect(onClose).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('does not call the close callback for non-Escape keys', () => {
    const { wrapper, onClose } = mountWithEscapeToClose()

    pressKey('Enter')

    expect(onClose).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('removes the escape listener on cleanup', async () => {
    const { wrapper, isOpen, onClose } = mountWithEscapeToClose()

    isOpen.value = false
    await nextTick()

    pressKey('Escape')
    expect(onClose).not.toHaveBeenCalled()

    isOpen.value = true
    await nextTick()
    pressKey('Escape')
    expect(onClose).toHaveBeenCalledTimes(1)

    wrapper.unmount()
    pressKey('Escape')

    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
