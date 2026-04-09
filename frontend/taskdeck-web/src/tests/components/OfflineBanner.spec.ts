import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import OfflineBanner from '../../components/shell/OfflineBanner.vue'

describe('OfflineBanner', () => {
  beforeEach(() => {
    vi.stubGlobal('navigator', { ...navigator, onLine: true })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('does not render the banner when online', () => {
    const wrapper = mount(OfflineBanner)
    expect(wrapper.find('.td-offline-banner').exists()).toBe(false)
  })

  it('renders the banner when offline', async () => {
    vi.stubGlobal('navigator', { ...navigator, onLine: false })

    // Re-import to pick up new navigator.onLine value
    vi.resetModules()
    const { default: OfflineBannerFresh } = await import('../../components/shell/OfflineBanner.vue')

    const wrapper = mount(OfflineBannerFresh)
    expect(wrapper.find('.td-offline-banner').exists()).toBe(true)
  })

  it('has correct ARIA attributes for accessibility', async () => {
    vi.stubGlobal('navigator', { ...navigator, onLine: false })
    vi.resetModules()
    const { default: OfflineBannerFresh } = await import('../../components/shell/OfflineBanner.vue')

    const wrapper = mount(OfflineBannerFresh)
    const banner = wrapper.find('.td-offline-banner')

    expect(banner.attributes('role')).toBe('status')
    expect(banner.attributes('aria-live')).toBe('assertive')
    expect(banner.attributes('aria-atomic')).toBe('true')
  })

  it('displays the correct offline message text', async () => {
    vi.stubGlobal('navigator', { ...navigator, onLine: false })
    vi.resetModules()
    const { default: OfflineBannerFresh } = await import('../../components/shell/OfflineBanner.vue')

    const wrapper = mount(OfflineBannerFresh)
    expect(wrapper.text()).toContain('You are offline')
    expect(wrapper.text()).toContain('sync when reconnected')
  })

  it('shows banner when going offline via window event', async () => {
    const wrapper = mount(OfflineBanner)
    expect(wrapper.find('.td-offline-banner').exists()).toBe(false)

    // Simulate going offline
    window.dispatchEvent(new Event('offline'))
    await nextTick()

    expect(wrapper.find('.td-offline-banner').exists()).toBe(true)
  })

  it('hides banner when going back online via window event', async () => {
    const wrapper = mount(OfflineBanner)

    // Go offline first
    window.dispatchEvent(new Event('offline'))
    await nextTick()
    expect(wrapper.find('.td-offline-banner').exists()).toBe(true)

    // Go back online
    window.dispatchEvent(new Event('online'))
    await nextTick()
    expect(wrapper.find('.td-offline-banner').exists()).toBe(false)
  })

  it('includes the cloud_off icon', async () => {
    vi.stubGlobal('navigator', { ...navigator, onLine: false })
    vi.resetModules()
    const { default: OfflineBannerFresh } = await import('../../components/shell/OfflineBanner.vue')

    const wrapper = mount(OfflineBannerFresh)
    const icon = wrapper.find('.td-offline-banner__icon')
    expect(icon.exists()).toBe(true)
    expect(icon.text()).toContain('cloud_off')
    expect(icon.attributes('aria-hidden')).toBe('true')
  })
})
