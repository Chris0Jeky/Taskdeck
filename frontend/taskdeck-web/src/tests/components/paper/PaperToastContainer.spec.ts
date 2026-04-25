import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import PaperToastContainer from '../../../components/paper/PaperToastContainer.vue'
import { useToastStore } from '../../../store/toastStore'

describe('PaperToastContainer', () => {
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

  it('renders multiple toasts from the store', async () => {
    const store = useToastStore()
    store.show('First message', 'success', 0)
    store.show('Second message', 'info', 0)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const cards = wrapper.findAll('.paper-toast')
    expect(cards.length).toBe(2)
    const messages = cards.map((c) => c.find('.paper-toast__msg').text())
    // Stack reverses order so newest is on top.
    expect(messages).toContain('First message')
    expect(messages).toContain('Second message')
  })

  it('pauses the countdown on hover and resumes on leave', async () => {
    const store = useToastStore()
    // duration > 0 wires the auto-remove setTimeout; we use fake timers.
    store.show('Pausable', 'info', 4000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const card = wrapper.find('.paper-toast')
    expect(card.exists()).toBe(true)

    function progressOf(): number {
      const bar = card.find('.paper-toast__bar').element as HTMLElement
      return Number(bar.style.getPropertyValue('--p'))
    }

    // Advance ~halfway, capture progress under hover (uses the float `--p`
    // CSS variable so we don't lose precision to the rounded "Ns" countdown).
    vi.advanceTimersByTime(1000)
    await nextTick()
    const beforeHover = progressOf()

    await card.trigger('mouseenter')
    // While paused, advancing timers should not move the displayed progress.
    vi.advanceTimersByTime(1500)
    await nextTick()
    const afterHover = progressOf()
    expect(afterHover).toBeCloseTo(beforeHover, 2)

    await card.trigger('mouseleave')
    // After resume, the displayed progress should drop again.
    vi.advanceTimersByTime(500)
    await nextTick()
    const afterResume = progressOf()
    expect(afterResume).toBeLessThan(beforeHover)
  })

  it('runs the action handler and emits action when the undo link is clicked', async () => {
    const store = useToastStore()
    const handler = vi.fn()
    const id = store.show('3 cards applied', 'success', 0, {
      title: '3 cards applied',
      action: { label: 'undo', hint: '6h', handler },
    })

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const undoBtn = wrapper.find('.paper-toast__undo')
    expect(undoBtn.exists()).toBe(true)
    expect(undoBtn.text()).toContain('undo')
    expect(undoBtn.text()).toContain('6h')

    await undoBtn.trigger('click')
    await flushPromises()

    expect(handler).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('action')?.[0]).toEqual([id])
    // Toast is removed from the store after action.
    expect(store.toasts.find((t) => t.id === id)).toBeUndefined()
  })
})
