import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import PaperToastContainer from '../../../components/paper/PaperToastContainer.vue'
import { useToastStore } from '../../../store/toastStore'

/**
 * Error-refresh countdown sync (GH-3474).
 *
 * `toastStore.show()` reuses a live error receipt (same ID) and rebuilds its
 * removal timer. These tests prove the Paper surface restarts its local
 * countdown from that explicit refresh signal — including when the duration
 * number itself did not change — without duplicating the receipt, the alert,
 * or any screen-reader announcement.
 */
describe('PaperToastContainer error refresh', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    wrapper?.unmount()
    wrapper = null
  })

  function progressOf(id: string): number {
    const bar = wrapper!
      .get(`[data-toast-id="${id}"]`)
      .find('.paper-toast__bar').element as HTMLElement
    return Number(bar.style.getPropertyValue('--p'))
  }

  function countdownOf(id: string): string {
    return wrapper!.get(`[data-toast-id="${id}"]`).find('.paper-toast__countdown').text()
  }

  function announcerText(): string {
    return wrapper!.get('[data-toast-polite-announcer]').text()
  }

  it('starts the visible countdown when a persistent error is reshown as timed', async () => {
    const store = useToastStore()
    const id = store.error('Save failed', 0)

    wrapper = mount(PaperToastContainer)
    await nextTick()
    expect(wrapper.get(`[data-toast-id="${id}"]`).find('.paper-toast__countdown').exists()).toBe(false)

    expect(store.error('Save failed', 3000)).toBe(id)
    expect(store.toasts).toHaveLength(1)
    expect(store.evictedErrors).toHaveLength(0)
    await nextTick()

    expect(countdownOf(id)).toBe('3s')
    expect(progressOf(id)).toBeCloseTo(1, 2)
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(announcerText()).toBe('')

    vi.advanceTimersByTime(1000)
    await nextTick()
    expect(countdownOf(id)).toBe('2s')

    vi.advanceTimersByTime(2000)
    await nextTick()
    expect(store.toasts).toHaveLength(0)
  })

  it('restarts the visible countdown in sync when a timed error repeats with the same duration', async () => {
    const store = useToastStore()
    const id = store.error('Sync failed', 3000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    vi.advanceTimersByTime(2500)
    await nextTick()
    expect(countdownOf(id)).toBe('1s')
    expect(progressOf(id)).toBeCloseTo(500 / 3000, 2)

    expect(store.error('Sync failed', 3000)).toBe(id)
    expect(store.toasts).toHaveLength(1)
    expect(store.evictedErrors).toHaveLength(0)
    await nextTick()

    expect(countdownOf(id)).toBe('3s')
    expect(progressOf(id)).toBeCloseTo(1, 2)
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(announcerText()).toBe('')

    vi.advanceTimersByTime(2999)
    await nextTick()
    expect(store.toasts).toHaveLength(1)

    vi.advanceTimersByTime(1)
    await nextTick()
    expect(store.toasts).toHaveLength(0)
  })

  it('keeps a hovered toast paused across a refresh and resumes with the full duration', async () => {
    const store = useToastStore()
    const id = store.error('Hover me', 3000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    vi.advanceTimersByTime(1000)
    await nextTick()
    await wrapper.get(`[data-toast-id="${id}"]`).trigger('mouseenter')
    vi.advanceTimersByTime(500)
    await nextTick()
    const pausedProgress = progressOf(id)

    expect(store.error('Hover me', 3000)).toBe(id)
    await nextTick()

    expect(countdownOf(id)).toBe('3s')
    expect(progressOf(id)).toBeCloseTo(1, 2)

    vi.advanceTimersByTime(2000)
    await nextTick()
    expect(countdownOf(id)).toBe('3s')
    expect(progressOf(id)).toBeCloseTo(1, 2)
    expect(progressOf(id)).toBeGreaterThan(pausedProgress)
    expect(store.toasts).toHaveLength(1)

    await wrapper.get(`[data-toast-id="${id}"]`).trigger('mouseleave')
    vi.advanceTimersByTime(500)
    await nextTick()
    expect(progressOf(id)).toBeLessThan(1)

    vi.advanceTimersByTime(2499)
    await nextTick()
    expect(store.toasts).toHaveLength(1)

    vi.advanceTimersByTime(1)
    await nextTick()
    expect(store.toasts).toHaveLength(0)
  })

  it('keeps a focused toast paused across a refresh', async () => {
    const store = useToastStore()
    const id = store.error('Focus me', 3000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    vi.advanceTimersByTime(1000)
    await nextTick()
    await wrapper.get(`[data-toast-id="${id}"]`).trigger('focusin')

    expect(store.error('Focus me', 3000)).toBe(id)
    await nextTick()

    expect(countdownOf(id)).toBe('3s')

    vi.advanceTimersByTime(2000)
    await nextTick()
    expect(countdownOf(id)).toBe('3s')
    expect(progressOf(id)).toBeCloseTo(1, 2)
    expect(store.toasts).toHaveLength(1)
    expect(announcerText()).toBe('')

    const card = wrapper.get(`[data-toast-id="${id}"]`)
    card.element.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }))
    await nextTick()

    vi.advanceTimersByTime(2999)
    await nextTick()
    expect(store.toasts).toHaveLength(1)

    vi.advanceTimersByTime(1)
    await nextTick()
    expect(store.toasts).toHaveLength(0)
  })
})
