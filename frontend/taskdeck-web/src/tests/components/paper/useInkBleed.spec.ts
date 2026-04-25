/**
 * useInkBleed composable — early/late finish, dried hold, singleton guard,
 * reduced-motion short-circuit. Spec ref: issue #1006.
 *
 * Tests use a host component to satisfy `onBeforeUnmount`'s requirement to be
 * called inside a Vue setup context. The host exposes the composable handle
 * so each test can drive `start/finish/cancel` directly.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { useInkBleed, type UseInkBleedReturn } from '../../../composables/useInkBleed'

function installMatchMedia(prefersReduce: boolean): void {
  const impl = (query: string) => {
    const matches =
      query.includes('prefers-reduced-motion') && prefersReduce === true
    return {
      matches,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    } as unknown as MediaQueryList
  }
  Object.defineProperty(globalThis, 'matchMedia', {
    configurable: true,
    writable: true,
    value: impl,
  })
}

interface HostExposed {
  bleed: UseInkBleedReturn
  doneCount: number
}

function createHost() {
  let doneCount = 0
  const Host = defineComponent({
    setup(_, { expose }) {
      const bleed = useInkBleed({
        onDone: () => {
          doneCount += 1
        },
      })
      expose({
        bleed,
        get doneCount() {
          return doneCount
        },
      } satisfies Record<string, unknown>)
      return () => h('div')
    },
  })
  const wrapper = mount(Host)
  return { wrapper, exposed: wrapper.vm as unknown as HostExposed }
}

describe('useInkBleed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    installMatchMedia(false)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('runs the full 4.6s sequence even when finish() resolves early', async () => {
    const { exposed } = createHost()
    exposed.bleed.start()
    expect(exposed.bleed.phase.value).toBe('drop')

    // Caller's async work resolves at 200ms.
    vi.advanceTimersByTime(200)
    exposed.bleed.finish()

    // Should still be in drop/bloom — not done yet.
    expect(exposed.doneCount).toBe(0)

    // Advance to bloom boundary
    vi.advanceTimersByTime(200) // total 400ms
    expect(exposed.bleed.phase.value).toBe('bloom')

    vi.advanceTimersByTime(1000) // total 1400ms
    expect(exposed.bleed.phase.value).toBe('compose')

    vi.advanceTimersByTime(2000) // total 3400ms
    expect(exposed.bleed.phase.value).toBe('settle')

    vi.advanceTimersByTime(800) // total 4200ms
    expect(exposed.bleed.phase.value).toBe('stamp')

    vi.advanceTimersByTime(400) // total 4600ms
    expect(exposed.bleed.phase.value).toBe('dried')
    // Done fires exactly once when the bleed reaches the scheduled end.
    expect(exposed.doneCount).toBe(1)
    // No loop hold because finish() was called early.
    expect(exposed.bleed.loop.value).toBe(false)
  })

  it('holds the dried state and pulses loop when work overruns 4.6s', async () => {
    const { exposed } = createHost()
    exposed.bleed.start()

    // Run past the full schedule without calling finish().
    vi.advanceTimersByTime(4600)
    expect(exposed.bleed.phase.value).toBe('dried')
    // Done has NOT fired yet — we're holding for the late finish.
    expect(exposed.doneCount).toBe(0)
    // Caller is signalled to pulse the eyebrow.
    expect(exposed.bleed.loop.value).toBe(true)

    // Some time later (e.g. +1500ms), the LLM call resolves.
    vi.advanceTimersByTime(1500)
    exposed.bleed.finish()

    expect(exposed.bleed.phase.value).toBe('dried')
    expect(exposed.bleed.loop.value).toBe(false)
    expect(exposed.doneCount).toBe(1)
  })

  it('cancels the previous bleed when start() is called again (singleton)', async () => {
    const { exposed } = createHost()
    exposed.bleed.start()

    vi.advanceTimersByTime(1500) // mid-compose
    expect(exposed.bleed.phase.value).toBe('compose')

    // Re-enter; should reset to drop without firing done for the previous run.
    exposed.bleed.start()
    expect(exposed.bleed.phase.value).toBe('drop')
    expect(exposed.doneCount).toBe(0)

    // Run the new sequence to completion; finish() arrives early.
    vi.advanceTimersByTime(200)
    exposed.bleed.finish()
    vi.advanceTimersByTime(4600)
    expect(exposed.bleed.phase.value).toBe('dried')
    expect(exposed.doneCount).toBe(1)
  })

  it('cancel() clears state without firing done', () => {
    const { exposed } = createHost()
    exposed.bleed.start()
    vi.advanceTimersByTime(1000)

    exposed.bleed.cancel()
    expect(exposed.bleed.phase.value).toBe('dried')
    expect(exposed.doneCount).toBe(0)

    // No further timer work should fire.
    vi.advanceTimersByTime(10000)
    expect(exposed.doneCount).toBe(0)
  })

  it('clears all timers on host unmount', () => {
    const { wrapper, exposed } = createHost()
    exposed.bleed.start()
    expect(vi.getTimerCount()).toBeGreaterThan(0)
    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('short-circuits the timer pipeline when reduced-motion is set', async () => {
    installMatchMedia(true)
    const { exposed } = createHost()
    expect(exposed.bleed.isReducedMotion.value).toBe(true)

    exposed.bleed.start()
    expect(exposed.bleed.phase.value).toBe('dried')

    // Advance one tick — done should fire deferred via setTimeout(0).
    vi.advanceTimersByTime(0)
    expect(exposed.doneCount).toBe(1)

    // Advancing further does nothing — no per-phase timers were ever queued.
    vi.advanceTimersByTime(10000)
    expect(exposed.doneCount).toBe(1)
  })

  it('finish() called after natural dried completion is a no-op', () => {
    const { exposed } = createHost()
    exposed.bleed.start()

    vi.advanceTimersByTime(4600)
    expect(exposed.doneCount).toBe(0) // held for late finish

    exposed.bleed.finish()
    expect(exposed.doneCount).toBe(1)

    // Calling finish again should not re-fire done.
    exposed.bleed.finish()
    expect(exposed.doneCount).toBe(1)
  })
})
