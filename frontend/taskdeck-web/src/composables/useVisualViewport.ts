import { computed, onMounted, onUnmounted, ref, type ComputedRef, type Ref } from 'vue'
import { logWarn } from '../utils/errorReporting'

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
 * Browser pinch zoom also changes those measurements, but it is not a keyboard
 * contraction. When entering a scale above 1, browser zoom owns that geometry,
 * so this composable freezes the last trusted scale-one measurement when one
 * exists and otherwise exposes the caller's normal fallback. A later event at
 * the unchanged scale may be a keyboard contraction, so it emits the current
 * visual-viewport geometry. Returning to scale 1 resumes trusted geometry.
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
  /** True when visual-viewport geometry is present, including a frozen zoom state. */
  supported: Ref<boolean>
  /** Current visual viewport height in CSS pixels (layout height before trusted geometry exists). */
  height: Ref<number>
  /** Current visual viewport top offset in CSS pixels (0 before trusted geometry exists). */
  offsetTop: Ref<number>
  /** Bind to an element's `:style`. Empty object under the `'unset'` fallback. */
  style: ComputedRef<Record<string, string>>
  /** Re-read the viewport. Exposed for tests and for imperative refreshes. */
  refresh: () => void
}

export function useVisualViewport(options: UseVisualViewportOptions): UseVisualViewportResult {
  const { prefix, fallback = 'layout' } = options

  if (import.meta.env.DEV && (typeof prefix !== 'string' || !prefix.startsWith('--') || prefix === '--')) {
    logWarn('[useVisualViewport] prefix must start with a CSS custom-property marker (`--`)', prefix)
  }

  const supported = ref(false)
  const height = ref(0)
  const offsetTop = ref(0)

  let observed: VisualViewport | null = null
  let observingLayoutViewport = false
  let lastTrustedGeometry: { height: number; offsetTop: number } | null = null
  let lastObservedScale: number | null = null

  function refresh() {
    if (typeof window === 'undefined') {
      supported.value = false
      return
    }

    const visualViewport = window.visualViewport
    if (!visualViewport) {
      lastTrustedGeometry = null
      lastObservedScale = null
      supported.value = false
      height.value = window.innerHeight
      offsetTop.value = 0
      return
    }

    const scale = visualViewport.scale ?? 1
    const scaleChanged = lastObservedScale !== scale
    lastObservedScale = scale
    if (scale === 1) {
      lastTrustedGeometry = {
        height: visualViewport.height,
        offsetTop: visualViewport.offsetTop,
      }
      supported.value = true
      height.value = lastTrustedGeometry.height
      offsetTop.value = lastTrustedGeometry.offsetTop
      return
    }

    if (scale > 1 && scaleChanged) {
      if (lastTrustedGeometry) {
        supported.value = true
        height.value = lastTrustedGeometry.height
        offsetTop.value = lastTrustedGeometry.offsetTop
        return
      }

      supported.value = false
      height.value = window.innerHeight
      offsetTop.value = 0
      return
    }

    if (scale > 1) {
      supported.value = true
      height.value = visualViewport.height
      offsetTop.value = visualViewport.offsetTop
      return
    }

    supported.value = false
    height.value = window.innerHeight
    offsetTop.value = 0
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
    if (observed) {
      observed.addEventListener('resize', refresh)
      observed.addEventListener('scroll', refresh)
    } else if (fallback === 'layout' && typeof window !== 'undefined') {
      observingLayoutViewport = true
      window.addEventListener('resize', refresh)
    }
  })

  onUnmounted(() => {
    observed?.removeEventListener('resize', refresh)
    observed?.removeEventListener('scroll', refresh)
    if (observingLayoutViewport && typeof window !== 'undefined') {
      window.removeEventListener('resize', refresh)
      observingLayoutViewport = false
    }
    observed = null
  })

  return { supported, height, offsetTop, style, refresh }
}
