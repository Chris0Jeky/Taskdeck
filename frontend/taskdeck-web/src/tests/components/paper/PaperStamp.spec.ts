import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperStamp from '../../../components/paper/PaperStamp.vue'

interface ReducedMotionStub {
  matches: boolean
}

function stubMatchMedia(reducedMotion: boolean): ReducedMotionStub {
  const stub: ReducedMotionStub = { matches: reducedMotion }
  ;(window as unknown as { matchMedia: (q: string) => MediaQueryList }).matchMedia = (
    q: string,
  ) =>
    ({
      matches: q.includes('reduce') ? stub.matches : false,
      media: q,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList
  return stub
}

describe('PaperStamp', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    stubMatchMedia(false)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it.each([
    ['applied', 'applied'],
    ['proposed', 'ember'],
    ['captured', undefined],
    ['overdue', 'overdue'],
    ['draft', undefined],
  ] as const)('renders %s kind with the right class', (kind, modifier) => {
    const wrapper = mount(PaperStamp, {
      props: { kind, date: 'Apr 25', time: '11:42', num: '014' },
    })
    expect(wrapper.classes()).toContain('stamp')
    if (modifier) expect(wrapper.classes()).toContain(modifier)
    expect(wrapper.attributes('data-kind')).toBe(kind)
    expect(wrapper.text()).toContain('Apr 25')
    expect(wrapper.text()).toContain('11:42')
    expect(wrapper.text()).toContain('#014')
  })

  it('applies an embossed class only on applied', () => {
    const applied = mount(PaperStamp, { props: { kind: 'applied' } })
    expect(applied.classes()).toContain('stamp--embossed')
    const proposed = mount(PaperStamp, { props: { kind: 'proposed' } })
    expect(proposed.classes()).not.toContain('stamp--embossed')
  })

  it('uses the explicit rotate prop when provided', () => {
    const wrapper = mount(PaperStamp, { props: { kind: 'applied', rotate: -3 } })
    expect(wrapper.attributes('style')).toContain('rotate(-3deg)')
  })

  it('picks a stable rotation between -7 and -9 degrees once on mount', () => {
    const a = mount(PaperStamp, { props: { kind: 'applied' } })
    const initial = a.attributes('style') ?? ''
    const match = /rotate\(([-0-9.]+)deg\)/.exec(initial)
    expect(match).not.toBeNull()
    const deg = Number(match![1])
    expect(deg).toBeLessThanOrEqual(-7)
    expect(deg).toBeGreaterThanOrEqual(-9)
    // Re-rendering by updating an unrelated prop must not change rotation.
    return a.setProps({ date: 'Apr 26' }).then(() => {
      expect(a.attributes('style')).toContain(initial.split('transform:')[1]!.split(';')[0])
    })
  })

  it('crossfades when undoing applied → proposed', async () => {
    const wrapper = mount(PaperStamp, {
      props: { kind: 'applied', date: 'Apr 25' },
    })
    expect(wrapper.attributes('data-fading')).toBeUndefined()

    await wrapper.setProps({ kind: 'proposed' })
    expect(wrapper.attributes('data-fading')).toBe('true')

    vi.advanceTimersByTime(260)
    await wrapper.vm.$nextTick()
    expect(wrapper.attributes('data-fading')).toBeUndefined()
  })

  it('skips the crossfade under prefers-reduced-motion', async () => {
    stubMatchMedia(true)
    const wrapper = mount(PaperStamp, { props: { kind: 'applied' } })
    await wrapper.setProps({ kind: 'proposed' })
    expect(wrapper.attributes('data-fading')).toBeUndefined()
  })
})
