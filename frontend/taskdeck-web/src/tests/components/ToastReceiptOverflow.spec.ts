import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ToastReceiptOverflow from '../../components/common/ToastReceiptOverflow.vue'
import { MAX_VISIBLE_TOASTS, useToastStore } from '../../store/toastStore'

describe('ToastReceiptOverflow', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    setActivePinia(createPinia())
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  it('lets users open and copy details from an evicted error receipt', async () => {
    const store = useToastStore()
    const id = store.error('Request failed', 0, { details: 'status: 503\nrequest id: abc' })
    for (let index = 0; index < MAX_VISIBLE_TOASTS; index += 1) {
      store.info(`Toast ${index}`, 0)
    }
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })

    wrapper = mount(ToastReceiptOverflow)
    const toggle = wrapper.get('[data-toast-overflow-toggle]')
    expect(toggle.text()).toContain('1 older error receipt')

    await toggle.trigger('click')
    expect(wrapper.get('[data-toast-overflow-list]').text()).toContain('Request failed')

    const detailsButton = wrapper.get('button[aria-expanded="false"]')
    await detailsButton.trigger('click')
    expect(wrapper.get(`[data-toast-receipt-id="${id}"] pre`).text()).toContain('request id: abc')

    const copyButton = wrapper.findAll('button').find((button) => button.text() === 'Copy details')
    expect(copyButton).toBeDefined()
    await copyButton!.trigger('click')
    expect(writeText).toHaveBeenCalledWith('Request failed\n\nstatus: 503\nrequest id: abc')
    expect(copyButton!.text()).toBe('Copied')
  })

  it('dismisses an archived receipt and hides the overflow surface when empty', async () => {
    const store = useToastStore()
    const id = store.error('Request failed', 0)
    for (let index = 0; index < MAX_VISIBLE_TOASTS; index += 1) {
      store.info(`Toast ${index}`, 0)
    }

    wrapper = mount(ToastReceiptOverflow)
    await wrapper.get('[data-toast-overflow-toggle]').trigger('click')
    await wrapper.get(`[data-toast-receipt-id="${id}"] button[aria-label="Dismiss notification"]`).trigger('click')

    expect(store.evictedErrors).toHaveLength(0)
    expect(wrapper.find('[data-toast-overflow]').exists()).toBe(false)
  })
})
