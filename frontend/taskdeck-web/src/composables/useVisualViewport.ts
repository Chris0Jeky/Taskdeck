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
 * exists and otherwise exposes the caller's normal fallback. A later resize
 * event at the unchanged scale may be a keyboard contraction, so it emits the
 * current visual-viewport geometry; lifecycle and scroll reads stay frozen.
 * Returning to scale 1 resumes trusted geometry.
 *
 * Four custom properties are emitted, namespaced by `prefix`:
 *   `${prefix}-visual-viewport-height`
 *   `${prefix}-visual-viewport-offset-top`
 *   `${prefix}-visual-viewport-width`
 *   `${prefix}-visual-viewport-offset-left`
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
  /** Current visual viewport width in CSS pixels. */
  width: Ref<number>
  /** Current visual viewport left offset in CSS pixels. */
  offsetLeft: Ref<number>
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
  const width = ref(0)
  const offsetLeft = ref(0)

  let observed: VisualViewport | null = null
  let observingLayoutViewport = false
  let observedResizeHandler: EventListener | null = null
  let observedScrollHandler: EventListener | null = null
  let layoutResizeHandler: EventListener | null = null
  let lastTrustedGeometry: { height: number; offsetTop: number; width: number; offsetLeft: number } | null = null
  let lastObservedScale: number | null = null

  type RefreshSource = 'initial' | 'lifecycle' | 'resize' | 'scroll' | 'manual'

  function refresh(source: RefreshSource = 'manual') {
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
      width.value = window.innerWidth
      offsetLeft.value = 0
      return
    }

    const visualWidth = Number.isFinite(visualViewport.width) && visualViewport.width > 0
      ? visualViewport.width : window.innerWidth
    const visualOffsetLeft = Number.isFinite(visualViewport.offsetLeft)
      ? visualViewport.offsetLeft : 0
    const scale = visualViewport.scale ?? 1
    const scaleChanged = lastObservedScale !== scale
    lastObservedScale = scale
    if (scale === 1) {
      lastTrustedGeometry = {
        height: visualViewport.height,
        offsetTop: visualViewport.offsetTop,
        width: visualWidth,
        offsetLeft: visualOffsetLeft,
      }
      supported.value = true
      height.value = lastTrustedGeometry.height
      offsetTop.value = lastTrustedGeometry.offsetTop
      width.value = lastTrustedGeometry.width
      offsetLeft.value = lastTrustedGeometry.offsetLeft
      return
    }

    if (scale > 1 && scaleChanged) {
      if (lastTrustedGeometry) {
        supported.value = true
        height.value = lastTrustedGeometry.height
        offsetTop.value = lastTrustedGeometry.offsetTop
        width.value = lastTrustedGeometry.width
        offsetLeft.value = lastTrustedGeometry.offsetLeft
        return
      }

      supported.value = false
      height.value = window.innerHeight
      offsetTop.value = 0
      width.value = window.innerWidth
      offsetLeft.value = 0
      return
    }

    if (scale > 1 && source === 'resize') {
      supported.value = true
      height.value = visualViewport.height
      offsetTop.value = visualViewport.offsetTop
      width.value = visualWidth
      offsetLeft.value = visualOffsetLeft
      return
    }

    if (scale > 1 && lastTrustedGeometry) {
      supported.value = true
      height.value = lastTrustedGeometry.height
      offsetTop.value = lastTrustedGeometry.offsetTop
      width.value = lastTrustedGeometry.width
      offsetLeft.value = lastTrustedGeometry.offsetLeft
      return
    }

    supported.value = false
    height.value = window.innerHeight
    offsetTop.value = 0
    width.value = window.innerWidth
    offsetLeft.value = 0
  }

  // Read eagerly so the very first render is already bound to the visual
  // viewport — waiting for onMounted would paint one frame at layout size.
  refresh('initial')

  const style = computed<Record<string, string>>(() => {
    if (!supported.value && fallback === 'unset') {
      return {}
    }

    return {
      [`${prefix}-visual-viewport-height`]: `${height.value}px`,
      [`${prefix}-visual-viewport-offset-top`]: `${offsetTop.value}px`,
      [`${prefix}-visual-viewport-width`]: `${width.value}px`,
      [`${prefix}-visual-viewport-offset-left`]: `${offsetLeft.value}px`,
    }
  })

  onMounted(() => {
    refresh('lifecycle')
    observed = (typeof window === 'undefined' ? null : window.visualViewport) ?? null
    if (observed) {
      observedResizeHandler = () => refresh('resize')
      observedScrollHandler = () => refresh('scroll')
      observed.addEventListener('resize', observedResizeHandler)
      observed.addEventListener('scroll', observedScrollHandler)
    } else if (fallback === 'layout' && typeof window !== 'undefined') {
      observingLayoutViewport = true
      layoutResizeHandler = () => refresh('manual')
      window.addEventListener('resize', layoutResizeHandler)
    }
  })

  onUnmounted(() => {
    if (observed && observedResizeHandler) {
      observed.removeEventListener('resize', observedResizeHandler)
    }
    if (observed && observedScrollHandler) {
      observed.removeEventListener('scroll', observedScrollHandler)
    }
    if (observingLayoutViewport && layoutResizeHandler && typeof window !== 'undefined') {
      window.removeEventListener('resize', layoutResizeHandler)
      observingLayoutViewport = false
    }
    observed = null
    observedResizeHandler = null
    observedScrollHandler = null
    layoutResizeHandler = null
  })

  return { supported, height, offsetTop, width, offsetLeft, style, refresh }
}
