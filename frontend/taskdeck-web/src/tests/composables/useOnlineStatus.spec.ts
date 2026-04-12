import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'

// We need to test in a component-like context for onMounted/onUnmounted to fire.
// Import the composable after mocking navigator.onLine.

function fireWindowEvent(type: 'online' | 'offline') {
  window.dispatchEvent(new Event(type))
}

describe('useOnlineStatus', () => {
  let useOnlineStatus: typeof import('../../composables/useOnlineStatus').useOnlineStatus

  beforeEach(async () => {
    vi.resetModules()
    // Default: browser is online
    vi.stubGlobal('navigator', { ...navigator, onLine: true })
    const mod = await import('../../composables/useOnlineStatus')
    useOnlineStatus = mod.useOnlineStatus
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('initializes isOnline to true when navigator.onLine is true', () => {
    const { isOnline } = useOnlineStatus()
    expect(isOnline.value).toBe(true)
  })

  it('initializes isOnline to false when navigator.onLine is false', async () => {
    vi.resetModules()
    vi.stubGlobal('navigator', { ...navigator, onLine: false })
    const mod = await import('../../composables/useOnlineStatus')
    const { isOnline } = mod.useOnlineStatus()
    expect(isOnline.value).toBe(false)
  })

  it('initializes lastChangedAt to null', () => {
    const { lastChangedAt } = useOnlineStatus()
    expect(lastChangedAt.value).toBeNull()
  })

  it('sets isOnline to false when _handleOffline is called', () => {
    const { isOnline, _handleOffline } = useOnlineStatus()
    expect(isOnline.value).toBe(true)

    _handleOffline()

    expect(isOnline.value).toBe(false)
  })

  it('sets isOnline to true when _handleOnline is called after going offline', () => {
    const { isOnline, _handleOffline, _handleOnline } = useOnlineStatus()

    _handleOffline()
    expect(isOnline.value).toBe(false)

    _handleOnline()
    expect(isOnline.value).toBe(true)
  })

  it('updates lastChangedAt when going offline', () => {
    const { lastChangedAt, _handleOffline } = useOnlineStatus()
    expect(lastChangedAt.value).toBeNull()

    const before = new Date()
    _handleOffline()
    const after = new Date()

    expect(lastChangedAt.value).not.toBeNull()
    expect(lastChangedAt.value!.getTime()).toBeGreaterThanOrEqual(before.getTime())
    expect(lastChangedAt.value!.getTime()).toBeLessThanOrEqual(after.getTime())
  })

  it('updates lastChangedAt when going online', () => {
    const { lastChangedAt, _handleOffline, _handleOnline } = useOnlineStatus()

    _handleOffline()
    const offlineTime = lastChangedAt.value!

    _handleOnline()
    expect(lastChangedAt.value!.getTime()).toBeGreaterThanOrEqual(offlineTime.getTime())
  })

  it('isOnline ref is readonly — assignment is a no-op', () => {
    const { isOnline } = useOnlineStatus()
    // readonly refs throw in dev mode or silently ignore writes
    expect(() => {
      // @ts-expect-error — intentional write to readonly ref
      isOnline.value = false
    }).not.toThrow() // readonly refs do not throw; they just log a warning
    // The value should remain true because the write is ignored
    expect(isOnline.value).toBe(true)
  })

  it('handles multiple offline/online transitions', () => {
    const { isOnline, lastChangedAt, _handleOffline, _handleOnline } = useOnlineStatus()

    _handleOffline()
    expect(isOnline.value).toBe(false)
    const t1 = lastChangedAt.value!

    _handleOnline()
    expect(isOnline.value).toBe(true)
    const t2 = lastChangedAt.value!
    expect(t2.getTime()).toBeGreaterThanOrEqual(t1.getTime())

    _handleOffline()
    expect(isOnline.value).toBe(false)
    const t3 = lastChangedAt.value!
    expect(t3.getTime()).toBeGreaterThanOrEqual(t2.getTime())
  })

  it('responds to window online/offline events when mounted in a component', async () => {
    // Use a minimal component wrapper to trigger onMounted/onUnmounted
    const { mount } = await import('@vue/test-utils')
    const { defineComponent, toRefs } = await import('vue')

    const TestComponent = defineComponent({
      setup() {
        const status = useOnlineStatus()
        return { ...toRefs(status) }
      },
      template: '<div>{{ isOnline }}</div>',
    })

    const wrapper = mount(TestComponent)
    await nextTick()

    expect(wrapper.text()).toBe('true')

    // Simulate going offline
    fireWindowEvent('offline')
    await nextTick()
    expect(wrapper.text()).toBe('false')

    // Simulate going online
    fireWindowEvent('online')
    await nextTick()
    expect(wrapper.text()).toBe('true')

    wrapper.unmount()
  })

  it('cleans up event listeners on unmount', async () => {
    const { mount } = await import('@vue/test-utils')
    const { defineComponent } = await import('vue')

    const addSpy = vi.spyOn(window, 'addEventListener')
    const removeSpy = vi.spyOn(window, 'removeEventListener')

    const TestComponent = defineComponent({
      setup() {
        useOnlineStatus()
        return {}
      },
      template: '<div />',
    })

    const wrapper = mount(TestComponent)
    await nextTick()

    // Should have registered both online and offline listeners
    const onlineCalls = addSpy.mock.calls.filter((c) => c[0] === 'online')
    const offlineCalls = addSpy.mock.calls.filter((c) => c[0] === 'offline')
    expect(onlineCalls.length).toBeGreaterThanOrEqual(1)
    expect(offlineCalls.length).toBeGreaterThanOrEqual(1)

    wrapper.unmount()

    // Should have removed both listeners
    const removeOnlineCalls = removeSpy.mock.calls.filter((c) => c[0] === 'online')
    const removeOfflineCalls = removeSpy.mock.calls.filter((c) => c[0] === 'offline')
    expect(removeOnlineCalls.length).toBeGreaterThanOrEqual(1)
    expect(removeOfflineCalls.length).toBeGreaterThanOrEqual(1)

    addSpy.mockRestore()
    removeSpy.mockRestore()
  })
})
