import { computed, onMounted, onUnmounted, ref, type ComputedRef, type Ref } from 'vue'

/**
 * Observes `window.visualViewport` and exposes it as CSS custom properties so a
 * fixed-position overlay can follow the *visual* viewport instead of the layout
 * viewport.
 *
 * Why this matters: a software keyboard contracts the visual viewport but leaves
 * the layout viewport untouched. A `position: fixed; inset: 0` overlay therefore
 * keeps spanning the full layout viewport and its footer actions end up beneath
 * the keyboard. Binding the overlay to `visualViewport.offsetTop` /
 * `visualViewport.height` keeps those actions on screen.
 *
 * Two custom properties are emitted, namespaced by `prefix`:
 *   `${prefix}-visual-viewport-height`
 *   `${prefix}-visual-viewport-offset-top`
 *
 * Fallback behaviour when `window.visualViewport` is unavailable is explicit
 * because the two current call sites need different things:
 *
 * - `'layout'` (default) — emit the layout viewport (`window.innerHeight` / `0`)
 *   as pixel values. `CardModal` relies on this: its container has no other
 *   height declaration to fall back to.
 * - `'unset'` — emit no custom properties at all, so a stylesheet written as
 *   `height: var(--x-visual-viewport-height, 100dvh)` keeps its `100dvh`
 *   fallback. `TdDialog` relies on this: it is a full-screen `100dvh` sheet on
 *   mobile and must stay one on browsers without a VisualViewport API.
 */
export type VisualViewportFallback = 'layout' | 'unset'

export interface UseVisualViewportOptions {
  /** Custom-property namespace, e.g. `'--td-dialog'`. Must start with `--`. */
  prefix: string
  /** What to emit when `window.visualViewport` is unavailable. Default `'layout'`. */
  fallback?: VisualViewportFallback
}

export interface UseVisualViewportResult {
  /** True when `window.visualViewport` is present and being observed. */
  supported: Ref<boolean>
  /** Current visual viewport height in CSS pixels (layout height when unsupported). */
  height: Ref<number>
  /** Current visual viewport top offset in CSS pixels (0 when unsupported). */
  offsetTop: Ref<number>
  /** Bind to an element's `:style`. Empty object under the `'unset'` fallback. */
  style: ComputedRef<Record<string, string>>
  /** Re-read the viewport. Exposed for tests and for imperative refreshes. */
  refresh: () => void
}

export function useVisualViewport(options: UseVisualViewportOptions): UseVisualViewportResult {
  const { prefix, fallback = 'layout' } = options

  const supported = ref(false)
  const height = ref(0)
  const offsetTop = ref(0)

  let observed: VisualViewport | null = null

  function refresh() {
    if (typeof window === 'undefined') {
      supported.value = false
      return
    }

    const visualViewport = window.visualViewport
    supported.value = Boolean(visualViewport)
    height.value = visualViewport?.height ?? window.innerHeight
    offsetTop.value = visualViewport?.offsetTop ?? 0
  }

  // Read eagerly so the very first render is already bound to the visual
  // viewport — waiting for onMounted would paint one frame at layout size.
  refresh()

  const style = computed<Record<string, string>>(() => {
    if (!supported.value && fallback === 'unset') {
      return {}
    }

    return {
      [`${prefix}-visual-viewport-height`]: `${height.value}px`,
      [`${prefix}-visual-viewport-offset-top`]: `${offsetTop.value}px`,
    }
  })

  onMounted(() => {
    refresh()
    observed = (typeof window === 'undefined' ? null : window.visualViewport) ?? null
    observed?.addEventListener('resize', refresh)
    observed?.addEventListener('scroll', refresh)
  })

  onUnmounted(() => {
    observed?.removeEventListener('resize', refresh)
    observed?.removeEventListener('scroll', refresh)
    observed = null
  })

  return { supported, height, offsetTop, style, refresh }
}
