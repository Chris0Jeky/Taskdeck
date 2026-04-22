import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, ref, nextTick } from 'vue'
import { createRouter, createMemoryHistory } from 'vue-router'
import ErrorBoundary from '../../components/ErrorBoundary.vue'

/** A child component that throws on render when `shouldThrow` is true. */
const ThrowingChild = defineComponent({
  name: 'ThrowingChild',
  props: {
    shouldThrow: { type: Boolean, default: false },
    message: { type: String, default: 'kaboom from child' },
  },
  setup(props) {
    return () => {
      if (props.shouldThrow) {
        throw new Error(props.message)
      }
      return h('div', { class: 'healthy-child' }, 'child is healthy')
    }
  },
})

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div>home</div>' } },
      { path: '/other', name: 'other', component: { template: '<div>other</div>' } },
    ],
  })
}

describe('ErrorBoundary', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    // Ensure no leftover Sentry global between tests.
    delete (globalThis as { Sentry?: unknown }).Sentry
  })

  afterEach(() => {
    consoleErrorSpy.mockRestore()
    delete (globalThis as { Sentry?: unknown }).Sentry
  })

  it('renders its slot content when no error occurs', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: false }),
      },
    })

    expect(wrapper.text()).toContain('child is healthy')
    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(false)
  })

  it('renders fallback UI with role="alert" when a descendant throws', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true, message: 'render-boom' }),
      },
    })

    await flushPromises()

    const fallback = wrapper.find('[data-testid="error-boundary-fallback"]')
    expect(fallback.exists()).toBe(true)
    expect(fallback.attributes('role')).toBe('alert')
    expect(fallback.attributes('aria-live')).toBe('assertive')
    expect(fallback.text()).toContain('Something went wrong')
  })

  it('logs the caught error to console.error', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true, message: 'log-me' }),
      },
    })
    await flushPromises()

    expect(consoleErrorSpy).toHaveBeenCalled()
    const allCalls = consoleErrorSpy.mock.calls.flat().map(String).join(' ')
    expect(allCalls).toContain('ErrorBoundary')
  })

  it('forwards the error to window.Sentry.captureException with ErrorBoundary source and info', async () => {
    const captureException = vi.fn()
    ;(globalThis as { Sentry?: unknown }).Sentry = { captureException }

    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true, message: 'to-sentry' }),
      },
    })
    await flushPromises()

    expect(captureException).toHaveBeenCalledTimes(1)
    const [err, hint] = captureException.mock.calls[0]
    expect(err).toBeInstanceOf(Error)
    expect((err as Error).message).toBe('to-sentry')
    // The hint must carry the ErrorBoundary source and the Vue lifecycle info
    // string so Sentry context stays consistent across reporting paths.
    expect(hint).toMatchObject({ source: 'ErrorBoundary' })
    expect((hint as { info?: string }).info).toEqual(expect.any(String))
  })

  it('does not throw when Sentry.captureException itself throws', async () => {
    ;(globalThis as { Sentry?: unknown }).Sentry = {
      captureException: () => {
        throw new Error('sentry exploded')
      },
    }

    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    expect(() => {
      mount(ErrorBoundary, {
        global: { plugins: [router] },
        slots: {
          default: () => h(ThrowingChild, { shouldThrow: true }),
        },
      })
    }).not.toThrow()
  })

  it('renders Reload and Go to home buttons in the fallback', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true }),
      },
    })
    await flushPromises()

    const buttonTexts = wrapper.findAll('button').map((b) => b.text())
    expect(buttonTexts).toEqual(
      expect.arrayContaining([
        expect.stringMatching(/Reload/i),
        expect.stringMatching(/home/i),
        expect.stringMatching(/Dismiss/i),
      ]),
    )
  })

  it('navigates to home and clears fallback when "Go to home" is clicked', async () => {
    const router = makeRouter()
    await router.push('/other')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true }),
      },
    })
    await flushPromises()

    const homeBtn = wrapper.findAll('button').find((b) => /home/i.test(b.text()))
    expect(homeBtn).toBeTruthy()
    await homeBtn!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/')
  })

  it('resets the fallback when the route changes (resetOnRouteChange=true)', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    // Use a reactive flag so we can flip the child back to healthy before the
    // route change — otherwise the child will just throw again and the
    // boundary will re-trap.
    const shouldThrow = ref(true)
    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: shouldThrow.value }),
      },
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(true)

    shouldThrow.value = false
    await nextTick()
    await router.push('/other')
    await flushPromises()

    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(false)
  })

  it('does not reset on route change when resetOnRouteChange=false', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const shouldThrow = ref(true)
    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      props: { resetOnRouteChange: false },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: shouldThrow.value }),
      },
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(true)

    shouldThrow.value = false
    await nextTick()
    await router.push('/other')
    await flushPromises()

    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(true)
  })

  it('emits an "error" event with the error and info when a child throws', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: true, message: 'emit-me' }),
      },
    })
    await flushPromises()

    const emitted = wrapper.emitted('error')
    expect(emitted).toBeTruthy()
    expect(emitted!.length).toBeGreaterThan(0)
    const [err] = emitted![0]
    expect(err).toBeInstanceOf(Error)
    expect((err as Error).message).toBe('emit-me')
  })

  it('exposes a reset() method that clears fallback state', async () => {
    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const shouldThrow = ref(true)
    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: {
        default: () => h(ThrowingChild, { shouldThrow: shouldThrow.value }),
      },
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(true)

    shouldThrow.value = false
    await nextTick()
    ;(wrapper.vm as unknown as { reset: () => void }).reset()
    await flushPromises()
    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(false)
  })

  it('renders without a router installed (graceful degradation)', async () => {
    // No router plugin — useRoute/useRouter will throw internally. The
    // component must still mount and render the healthy slot.
    const wrapper = mount(ErrorBoundary, {
      slots: { default: () => h(ThrowingChild, { shouldThrow: false }) },
    })
    expect(wrapper.text()).toContain('child is healthy')
  })

  it('renders fallback when a descendant throws a falsy value (null)', async () => {
    // Guards against the sentinel-collision bug: if crash state were tracked
    // as `crashedError === null`, a child that throws `null` would be treated
    // as healthy. We track crash state with an explicit boolean to avoid this.
    const NullThrower = defineComponent({
      name: 'NullThrower',
      setup() {
        return () => {
          throw null
        }
      },
    })

    const router = makeRouter()
    await router.push('/')
    await router.isReady()

    const wrapper = mount(ErrorBoundary, {
      global: { plugins: [router] },
      slots: { default: () => h(NullThrower) },
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="error-boundary-fallback"]').exists()).toBe(true)
  })
})
