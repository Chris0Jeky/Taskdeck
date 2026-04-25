import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperUndoTimeline from '../../../components/paper/PaperUndoTimeline.vue'

interface MqlStub {
  matches: boolean
  listeners: Set<(e: MediaQueryListEvent) => void>
}

function stubMatchMedia(matches: boolean): MqlStub {
  const stub: MqlStub = { matches, listeners: new Set() }
  ;(window as unknown as { matchMedia: (q: string) => MediaQueryList }).matchMedia = (
    q: string,
  ) =>
    ({
      matches: q.includes('reduce') ? stub.matches : false,
      media: q,
      onchange: null,
      addEventListener: (_t: string, h: (e: MediaQueryListEvent) => void) => {
        stub.listeners.add(h)
      },
      removeEventListener: (_t: string, h: (e: MediaQueryListEvent) => void) => {
        stub.listeners.delete(h)
      },
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList
  return stub
}

describe('PaperUndoTimeline', () => {
  let rafCalls: Array<(t: number) => void> = []
  let cancelled = 0
  let nextRafId = 1

  beforeEach(() => {
    rafCalls = []
    cancelled = 0
    nextRafId = 1
    vi.stubGlobal('requestAnimationFrame', (cb: (t: number) => void) => {
      rafCalls.push(cb)
      return nextRafId++
    })
    vi.stubGlobal('cancelAnimationFrame', (_id: number) => {
      cancelled++
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    rafCalls = []
  })

  it('renders an animated timeline with 24 dashes by default', () => {
    stubMatchMedia(false)
    const wrapper = mount(PaperUndoTimeline, {
      props: { appliedAt: new Date(Date.now()) },
    })
    expect(wrapper.find('.paper-undo__track').exists()).toBe(true)
    expect(wrapper.findAll('.paper-undo__dash')).toHaveLength(24)
  })

  it('renders a static labelled bar under prefers-reduced-motion', async () => {
    stubMatchMedia(true)
    const wrapper = mount(PaperUndoTimeline, {
      props: { appliedAt: new Date(Date.now() - 60_000), windowMs: 120_000 },
    })
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.paper-undo__track').exists()).toBe(false)
    expect(wrapper.find('.paper-undo__static').exists()).toBe(true)
    expect(wrapper.attributes('data-reduced')).toBe('true')
    // No raf scheduled in reduced-motion mode.
    expect(rafCalls).toHaveLength(0)
  })

  it('marks dashes spent as the window closes', async () => {
    stubMatchMedia(false)
    const applied = Date.now() - 1_500
    const wrapper = mount(PaperUndoTimeline, {
      props: { appliedAt: new Date(applied), windowMs: 6_000 },
    })
    // First raf tick happens via the loop start.  Simulate one frame.
    expect(rafCalls.length).toBeGreaterThan(0)
    rafCalls.shift()!(0) // first tick: lastTickAt was 0 → updates immediately.
    await wrapper.vm.$nextTick()
    const spent = wrapper.findAll('.paper-undo__dash[data-spent="true"]').length
    expect(spent).toBeGreaterThan(0)
    expect(spent).toBeLessThan(24)
  })

  it('cancels rAF on unmount', () => {
    stubMatchMedia(false)
    const wrapper = mount(PaperUndoTimeline, {
      props: { appliedAt: new Date(Date.now()), windowMs: 6 * 60 * 60 * 1000 },
    })
    expect(cancelled).toBe(0)
    wrapper.unmount()
    expect(cancelled).toBeGreaterThanOrEqual(1)
  })
})
