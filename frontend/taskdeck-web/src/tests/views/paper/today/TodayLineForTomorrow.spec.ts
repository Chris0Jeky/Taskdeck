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
    await vi.advanceTimersByTimeAsync(150)
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
    await vi.advanceTimersByTimeAsync(150)
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
    await vi.advanceTimersByTimeAsync(75)
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

  it('uses backend initial value over stale localStorage when stored drafts are disabled', async () => {
    localStorage.setItem(KEY, 'stale local text')

    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 50,
        initial: 'backend note',
        useStoredDraft: false,
      },
    })

    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')
    expect(input.element.value).toBe('backend note')

    await wrapper.setProps({ initial: 'new backend note' })
    await wrapper.vm.$nextTick()

    expect(input.element.value).toBe('new backend note')
  })

  it('preserves typed text when async backend initial value arrives', async () => {
    const save = vi.fn().mockResolvedValue(undefined)
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
      },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    await input.setValue('typed before fetch')
    await wrapper.setProps({ initial: 'late backend value' })
    await wrapper.vm.$nextTick()

    expect(input.element.value).toBe('typed before fetch')

    await vi.advanceTimersByTimeAsync(150)

    expect(save).toHaveBeenCalledWith('typed before fetch', undefined)
  })

  it('keeps saving status until backend save succeeds', async () => {
    const save = vi.fn().mockResolvedValue(undefined)
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
        saveDate: '2026-01-15',
      },
    })

    await wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]').setValue('backend text')
    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saving')

    await vi.advanceTimersByTimeAsync(150)
    await wrapper.vm.$nextTick()

    expect(save).toHaveBeenCalledWith('backend text', '2026-01-15')
    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saved')
    expect(wrapper.emitted('save')?.[0]).toEqual(['backend text'])
  })

  it('does not mark a superseded save failure as the latest status', async () => {
    let rejectFirst!: (error: unknown) => void
    const save = vi.fn()
      .mockImplementationOnce(() => new Promise<void>((_, reject) => {
        rejectFirst = reject
      }))
      .mockImplementationOnce(() => {
        rejectFirst(new Error('superseded'))
        return Promise.resolve()
      })
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
      },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    await input.setValue('first')
    await vi.advanceTimersByTimeAsync(150)
    await input.setValue('second')
    await vi.advanceTimersByTimeAsync(150)
    await wrapper.vm.$nextTick()

    expect(save).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saved')
    expect(wrapper.emitted('save')).toEqual([['second']])
  })

  it('keeps saving when an in-flight save is superseded before the next debounce flush', async () => {
    let rejectFirst!: (error: unknown) => void
    const save = vi.fn()
      .mockImplementationOnce(() => new Promise<void>((_, reject) => {
        rejectFirst = reject
      }))
      .mockResolvedValue(undefined)
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
      },
    })
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    await input.setValue('first')
    await vi.advanceTimersByTimeAsync(150)
    await input.setValue('second')
    rejectFirst(new Error('Superseded by newer tomorrow note autosave'))
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saving')

    await vi.advanceTimersByTimeAsync(150)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Saved')
    expect(wrapper.emitted('save')).toEqual([['second']])
  })

  it('shows unavailable state when backend save fails', async () => {
    const save = vi.fn().mockRejectedValue(new Error('offline'))
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
      },
    })

    await wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]').setValue('backend text')
    await vi.advanceTimersByTimeAsync(150)
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="line-for-tomorrow-status"]').text()).toContain('Save unavailable')
    expect(wrapper.emitted('save')).toBeUndefined()
  })

  it('passes the edit-time save date even if prop changes before debounce flushes', async () => {
    const save = vi.fn().mockResolvedValue(undefined)
    const wrapper = mount(TodayLineForTomorrow, {
      props: {
        storageKey: KEY,
        debounceMs: 100,
        initial: '',
        useStoredDraft: false,
        save,
        saveDate: '2026-01-15',
      },
    })

    await wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]').setValue('before midnight')
    await wrapper.setProps({ saveDate: '2026-01-16' })
    await vi.advanceTimersByTimeAsync(150)

    expect(save).toHaveBeenCalledWith('before midnight', '2026-01-15')
  })
})
