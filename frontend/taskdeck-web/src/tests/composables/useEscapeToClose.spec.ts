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
  let wrapper: ReturnType<typeof mountWithEscapeToClose>['wrapper'] | undefined

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
  })

  it('calls the close callback when Escape is pressed while open', () => {
    const ctx = mountWithEscapeToClose()
    wrapper = ctx.wrapper

    pressKey('Escape')

    expect(ctx.onClose).toHaveBeenCalledTimes(1)
  })

  it('does not call the close callback when initially closed', () => {
    const ctx = mountWithEscapeToClose(false)
    wrapper = ctx.wrapper

    pressKey('Escape')

    expect(ctx.onClose).not.toHaveBeenCalled()
  })

  it('does not call the close callback for non-Escape keys', () => {
    const ctx = mountWithEscapeToClose()
    wrapper = ctx.wrapper

    pressKey('Enter')

    expect(ctx.onClose).not.toHaveBeenCalled()
  })

  it('removes the escape listener on cleanup', async () => {
    const ctx = mountWithEscapeToClose()
    wrapper = ctx.wrapper

    ctx.isOpen.value = false
    await nextTick()

    pressKey('Escape')
    expect(ctx.onClose).not.toHaveBeenCalled()

    ctx.isOpen.value = true
    await nextTick()
    pressKey('Escape')
    expect(ctx.onClose).toHaveBeenCalledTimes(1)

    ctx.wrapper.unmount()
    wrapper = undefined
    pressKey('Escape')

    expect(ctx.onClose).toHaveBeenCalledTimes(1)
  })
})
