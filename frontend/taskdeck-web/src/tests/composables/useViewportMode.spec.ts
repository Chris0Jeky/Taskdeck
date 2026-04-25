import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useViewportMode } from '../../composables/useViewportMode'

type Listener = (ev: MediaQueryListEvent) => void

function installMatchMedia() {
  const queries = new Map<
    string,
    { matches: boolean; listeners: Set<Listener> }
  >()
  function get(query: string) {
    let entry = queries.get(query)
    if (!entry) {
      entry = { matches: false, listeners: new Set<Listener>() }
      queries.set(query, entry)
    }
    return entry
  }
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn((query: string) => {
      const entry = get(query)
      // Real MediaQueryList.matches is a live getter; mirror that so
      // composables that read .matches from a captured MQL stay reactive.
      return {
        get matches() {
          return entry.matches
        },
        media: query,
        addEventListener: (type: string, listener: Listener) => {
          if (type === 'change') entry.listeners.add(listener)
        },
        removeEventListener: (type: string, listener: Listener) => {
          if (type === 'change') entry.listeners.delete(listener)
        },
      }
    }),
  })
  return {
    set(query: string, matches: boolean) {
      const entry = get(query)
      entry.matches = matches
      entry.listeners.forEach((l) => l({ matches } as MediaQueryListEvent))
    },
    listenerCount(query: string) {
      return get(query).listeners.size
    },
  }
}

const Host = defineComponent({
  name: 'ViewportHost',
  setup() {
    const { mode } = useViewportMode()
    return () => h('div', { 'data-mode': mode.value }, mode.value)
  },
})

function readMode(wrapper: ReturnType<typeof mount>): string {
  return wrapper.find('div').attributes('data-mode') ?? ''
}

describe('useViewportMode', () => {
  let mq: ReturnType<typeof installMatchMedia>

  beforeEach(() => {
    mq = installMatchMedia()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('reports desktop when no media query matches', () => {
    const wrapper = mount(Host)
    expect(readMode(wrapper)).toBe('desktop')
  })

  it('reports phone when the phone query matches', () => {
    mq.set('(max-width: 480px)', true)
    mq.set('(max-width: 1024px)', true)
    const wrapper = mount(Host)
    expect(readMode(wrapper)).toBe('phone')
  })

  it('reports tablet when only the tablet query matches', () => {
    mq.set('(max-width: 1024px)', true)
    const wrapper = mount(Host)
    expect(readMode(wrapper)).toBe('tablet')
  })

  it('reacts to live media changes', async () => {
    const wrapper = mount(Host)
    expect(readMode(wrapper)).toBe('desktop')
    mq.set('(max-width: 1024px)', true)
    await nextTick()
    expect(readMode(wrapper)).toBe('tablet')
    mq.set('(max-width: 480px)', true)
    await nextTick()
    expect(readMode(wrapper)).toBe('phone')
  })

  it('cleans up listeners on unmount', () => {
    const wrapper = mount(Host)
    expect(mq.listenerCount('(max-width: 480px)')).toBe(1)
    expect(mq.listenerCount('(max-width: 1024px)')).toBe(1)
    wrapper.unmount()
    expect(mq.listenerCount('(max-width: 480px)')).toBe(0)
    expect(mq.listenerCount('(max-width: 1024px)')).toBe(0)
  })

  it('does not leak listeners across multiple mount/unmount cycles', () => {
    for (let cycle = 0; cycle < 3; cycle += 1) {
      const wrapper = mount(Host)
      expect(mq.listenerCount('(max-width: 480px)')).toBe(1)
      expect(mq.listenerCount('(max-width: 1024px)')).toBe(1)
      wrapper.unmount()
      expect(mq.listenerCount('(max-width: 480px)')).toBe(0)
      expect(mq.listenerCount('(max-width: 1024px)')).toBe(0)
    }
  })

  it('treats absent matchMedia as desktop without throwing', () => {
    const original = Object.getOwnPropertyDescriptor(window, 'matchMedia')
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: undefined,
    })
    try {
      const wrapper = mount(Host)
      expect(readMode(wrapper)).toBe('desktop')
      wrapper.unmount()
    } finally {
      if (original) {
        Object.defineProperty(window, 'matchMedia', original)
      }
    }
  })
})
