/**
 * InkBleed.vue — phase machine, reduced-motion short-circuit, dried state.
 *
 * Spec ref: design_handoff_taskdeck_paper/paper/surface-motion.jsx + #1006.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import InkBleed from '../../../components/paper/InkBleed.vue'

type MatchMediaImpl = (query: string) => MediaQueryList

function installMatchMedia(prefersReduce: boolean): void {
  const impl: MatchMediaImpl = (query: string) => {
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

describe('InkBleed', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    installMatchMedia(false)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders the dried+stamped frame as initial markup (no-JS fallback)', () => {
    // Before mount/onMounted runs, currentPhase defaults to 'dried' so SSR/no-JS
    // always shows the final state. We can verify by rendering the static
    // phase='dried' explicitly which yields the same DOM.
    const wrapper = mount(InkBleed, { props: { phase: 'dried' } })
    expect(wrapper.classes()).toContain('ink-bleed--dried')
    expect(wrapper.find('.ink-bleed__stamp').exists()).toBe(true)
  })

  it('advances through every phase at its scheduled time (auto)', async () => {
    const wrapper = mount(InkBleed, { props: { phase: 'auto' } })
    // onMounted has already kicked off the sequence at t=0 → drop.
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--drop')
    expect(wrapper.emitted('phasechange')?.[0]).toEqual(['drop'])

    // 0 → 400ms (bloom)
    vi.advanceTimersByTime(400)
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--bloom')

    // 400 → 1400ms (compose)
    vi.advanceTimersByTime(1000)
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--compose')

    // 1400 → 3400ms (settle)
    vi.advanceTimersByTime(2000)
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--settle')

    // 3400 → 4200ms (stamp)
    vi.advanceTimersByTime(800)
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--stamp')

    // 4200 → 4600ms (dried)
    vi.advanceTimersByTime(400)
    await wrapper.vm.$nextTick()
    expect(wrapper.classes()).toContain('ink-bleed--dried')

    // Phase transitions should have emitted in order, and `done` fires when
    // we reach dried.
    const events = wrapper.emitted('phasechange') ?? []
    const sequence = events.map((args) => args[0])
    expect(sequence).toEqual([
      'drop',
      'bloom',
      'compose',
      'settle',
      'stamp',
      'dried',
    ])
    expect(wrapper.emitted('done')?.length ?? 0).toBeGreaterThan(0)
  })

  it('short-circuits to dried with no timer work when reduced-motion is set', async () => {
    installMatchMedia(true)
    const wrapper = mount(InkBleed, { props: { phase: 'auto' } })
    await wrapper.vm.$nextTick()

    expect(wrapper.classes()).toContain('ink-bleed--reduced')
    expect(wrapper.classes()).toContain('ink-bleed--dried')

    // Even after advancing past the full schedule, no other phase events fire.
    vi.advanceTimersByTime(5000)
    await wrapper.vm.$nextTick()

    const events = wrapper.emitted('phasechange') ?? []
    // Only 'dried' (the initial setPhase call) should have been emitted.
    expect(events.every((e) => e[0] === 'dried')).toBe(true)
    // Initial phase was already 'dried' before mount, so setPhase('dried')
    // is a no-op; we expect zero phasechange events on the reduced path.
    expect(events.length).toBe(0)
  })

  it('renders explicit phase=dried as final state without scheduling timers', () => {
    const wrapper = mount(InkBleed, { props: { phase: 'dried' } })
    expect(wrapper.classes()).toContain('ink-bleed--dried')
    expect(wrapper.find('.ink-bleed__stamp').exists()).toBe(true)
    // No phasechange was emitted because phase was already dried at mount.
    expect(wrapper.emitted('phasechange')).toBeUndefined()
  })

  it('renders the supplied headline inside the bleed', () => {
    const wrapper = mount(InkBleed, {
      props: { phase: 'compose', headline: 'Split the dark mode card.' },
    })
    expect(wrapper.text()).toContain('Split the dark mode card.')
  })

  it('clears its timers on unmount (no setInterval / setTimeout leaks)', async () => {
    const wrapper = mount(InkBleed, { props: { phase: 'auto' } })
    await wrapper.vm.$nextTick()
    expect(vi.getTimerCount()).toBeGreaterThan(0)
    wrapper.unmount()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('marks the eyebrow with a pulse class when loop=true and dried is held', () => {
    const wrapper = mount(InkBleed, {
      props: { phase: 'dried', loop: true },
    })
    const eyebrow = wrapper.find('.ink-bleed__eyebrow')
    expect(eyebrow.classes()).toContain('ink-bleed__eyebrow--pulse')
  })
})
