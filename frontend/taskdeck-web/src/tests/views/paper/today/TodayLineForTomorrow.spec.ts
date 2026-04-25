import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TodayLineForTomorrow from '../../../../views/paper/today/TodayLineForTomorrow.vue'

const KEY = 'td.test.line-for-tomorrow'

describe('TodayLineForTomorrow', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('autosaves on debounce after typing', async () => {
    const wrapper = mount(TodayLineForTomorrow, {
      props: { storageKey: KEY, debounceMs: 100, initial: '' },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')
    await input.setValue('AA contrast first')

    // Status flips to "Saving…" before timer fires
    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saving')
    expect(localStorage.getItem(KEY)).toBe(null)

    // Advance debounce
    vi.advanceTimersByTime(150)
    await wrapper.vm.$nextTick()

    expect(localStorage.getItem(KEY)).toBe('AA contrast first')
    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saved')
    expect(wrapper.emitted('save')?.[0]).toEqual(['AA contrast first'])
  })

  it('reports save failures without emitting a successful save', async () => {
    const originalLocalStorage = window.localStorage
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      value: {
        getItem: vi.fn(() => null),
        setItem: vi.fn(() => {
          throw new Error('quota')
        }),
      },
    })
    const wrapper = mount(TodayLineForTomorrow, {
      props: { storageKey: KEY, debounceMs: 100, initial: '' },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    await input.setValue('AA contrast first')
    vi.advanceTimersByTime(150)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Save unavailable')
    expect(wrapper.emitted('save')).toBeUndefined()
    Object.defineProperty(window, 'localStorage', { configurable: true, value: originalLocalStorage })
  })

  it('persists across remount via the same storage key', async () => {
    // First mount writes
    const a = mount(TodayLineForTomorrow, {
      props: { storageKey: KEY, debounceMs: 50, initial: '' },
    })
    await a.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]').setValue('persisted')
    vi.advanceTimersByTime(75)
    await a.vm.$nextTick()
    expect(localStorage.getItem(KEY)).toBe('persisted')
    a.unmount()

    // Second mount reads back
    const b = mount(TodayLineForTomorrow, {
      props: { storageKey: KEY, debounceMs: 50, initial: '' },
    })
    const text = (b.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]').element).value
    expect(text).toBe('persisted')
  })

  it('reloads from storage when the scoped storage key changes', async () => {
    localStorage.setItem(`${KEY}:user-a`, 'user a line')
    localStorage.setItem(`${KEY}:user-b`, 'user b line')

    const wrapper = mount(TodayLineForTomorrow, {
      props: { storageKey: `${KEY}:user-a`, debounceMs: 50, initial: '' },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')
    expect(input.element.value).toBe('user a line')

    await wrapper.setProps({ storageKey: `${KEY}:user-b` })
    await wrapper.vm.$nextTick()

    expect(input.element.value).toBe('user b line')
  })
})
