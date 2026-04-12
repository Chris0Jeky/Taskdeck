import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import SwUpdatePrompt from '../../components/shell/SwUpdatePrompt.vue'

// Capture the onNeedRefresh callback
let capturedOnNeedRefresh: (() => void) | undefined
const mockUpdateSW = vi.fn(async () => {})

vi.mock('virtual:pwa-register', () => ({
  registerSW: (options?: { onNeedRefresh?: () => void }) => {
    capturedOnNeedRefresh = options?.onNeedRefresh
    return mockUpdateSW
  },
}))

describe('SwUpdatePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    capturedOnNeedRefresh = undefined
  })

  it('does not show update prompt initially', () => {
    const wrapper = mount(SwUpdatePrompt)
    expect(wrapper.find('.td-sw-update').exists()).toBe(false)
  })

  it('shows update prompt when onNeedRefresh is called', async () => {
    const wrapper = mount(SwUpdatePrompt)
    expect(capturedOnNeedRefresh).toBeDefined()

    capturedOnNeedRefresh!()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.td-sw-update').exists()).toBe(true)
    expect(wrapper.text()).toContain('A new version of Taskdeck is available.')
  })

  it('shows Update now and dismiss buttons', async () => {
    const wrapper = mount(SwUpdatePrompt)
    capturedOnNeedRefresh!()
    await wrapper.vm.$nextTick()

    const updateBtn = wrapper.find('.td-sw-update__btn--primary')
    expect(updateBtn.exists()).toBe(true)
    expect(updateBtn.text()).toBe('Update now')

    const dismissBtn = wrapper.find('.td-sw-update__btn--dismiss')
    expect(dismissBtn.exists()).toBe(true)
    expect(dismissBtn.attributes('aria-label')).toBe('Dismiss update notification')
  })

  it('calls updateSW and hides prompt when Update now is clicked', async () => {
    const wrapper = mount(SwUpdatePrompt)
    capturedOnNeedRefresh!()
    await wrapper.vm.$nextTick()

    await wrapper.find('.td-sw-update__btn--primary').trigger('click')
    await flushPromises()

    expect(mockUpdateSW).toHaveBeenCalled()
    expect(wrapper.find('.td-sw-update').exists()).toBe(false)
  })

  it('hides prompt without updating when dismiss is clicked', async () => {
    const wrapper = mount(SwUpdatePrompt)
    capturedOnNeedRefresh!()
    await wrapper.vm.$nextTick()

    await wrapper.find('.td-sw-update__btn--dismiss').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.td-sw-update').exists()).toBe(false)
    expect(mockUpdateSW).not.toHaveBeenCalled()
  })

  it('has status role and aria-live polite for accessibility', async () => {
    const wrapper = mount(SwUpdatePrompt)
    capturedOnNeedRefresh!()
    await wrapper.vm.$nextTick()

    const updateEl = wrapper.find('.td-sw-update')
    expect(updateEl.attributes('role')).toBe('status')
    expect(updateEl.attributes('aria-live')).toBe('polite')
  })
})
